using Microsoft.Extensions.Configuration;
using Npgsql;
using Opstrax.Api.Data;
using Xunit.Sdk;

namespace Opstrax.Tests;

[Trait("Category", "DeviceRemoteCommandPostgres")]
[Trait("Lane", "DedicatedDatabase")]
public sealed class DeviceRemoteCommandPostgresTests
{
    [Fact]
    public async Task ApplicationRoleCannotCreateOrRewriteCapabilitiesOrCommands()
    {
        var db = Db();
        foreach (var table in new[] { "device_command_capabilities", "telematics_device_commands" })
        {
            Assert.Equal(1, await db.ScalarLongAsync(
                "SELECT CASE WHEN has_table_privilege('opstrax_app',@table,'SELECT') THEN 1 ELSE 0 END",
                command => command.Parameters.AddWithValue("@table", table)));
            foreach (var privilege in new[] { "INSERT", "UPDATE", "DELETE" })
                Assert.Equal(0, await db.ScalarLongAsync(
                    "SELECT CASE WHEN has_table_privilege('opstrax_app',@table,@privilege) THEN 1 ELSE 0 END",
                    command => { command.Parameters.AddWithValue("@table", table); command.Parameters.AddWithValue("@privilege", privilege); }));
        }
    }

    [Fact]
    public async Task CommandWithoutVerifiedCapabilityIsRejected()
    {
        var db = Db();
        var deviceId = await CreateDevice(db);
        var exception = await Assert.ThrowsAsync<PostgresException>(() => InsertCommand(db, deviceId, 999999));
        Assert.Equal(PostgresErrorCodes.CheckViolation, exception.SqlState);
        Assert.Equal("ck_stage119_command_capability_invalid", exception.ConstraintName);
    }

    [Fact]
    public async Task ExactVerifiedCapabilityAdmitsOnlyGovernedEnvelope()
    {
        var db = Db();
        var deviceId = await CreateDevice(db);
        var capabilityId = await InsertCapability(db, deviceId);
        Assert.True(await InsertCommand(db, deviceId, capabilityId) > 0);

        var falseClaim = await Assert.ThrowsAsync<PostgresException>(() => InsertCommand(
            db, deviceId, capabilityId, physicalClaim: true));
        Assert.Equal(PostgresErrorCodes.CheckViolation, falseClaim.SqlState);

        var forgedAcknowledgement = await Assert.ThrowsAsync<PostgresException>(() => InsertCommand(
            db, deviceId, capabilityId, reportedPayload: "{}"));
        Assert.Equal("ck_stage119_command_envelope", forgedAcknowledgement.ConstraintName);
    }

    [Fact]
    public async Task TupleDriftInvalidatesEarlierCapability()
    {
        var db = Db();
        var deviceId = await CreateDevice(db);
        var capabilityId = await InsertCapability(db, deviceId);
        await db.ExecuteAsync("UPDATE eld_devices SET firmware_version='v9.9.9' WHERE id=@id",
            command => command.Parameters.AddWithValue("@id", deviceId));
        var exception = await Assert.ThrowsAsync<PostgresException>(() => InsertCommand(db, deviceId, capabilityId));
        Assert.Equal("ck_stage119_command_tuple_mismatch", exception.ConstraintName);
    }

    [Fact]
    public async Task BranchMismatchCannotAdmitACommand()
    {
        var db = Db();
        var deviceId = await CreateDevice(db, branchId: 10);
        var capabilityId = await InsertCapability(db, deviceId, branchId: 11);
        var exception = await Assert.ThrowsAsync<PostgresException>(() => InsertCommand(db, deviceId, capabilityId, branchId: 10));
        Assert.Equal("ck_stage119_command_capability_invalid", exception.ConstraintName);
    }

    [Fact]
    public async Task RevokedCapabilityCannotBeDispatched()
    {
        var db = Db();
        var deviceId = await CreateDevice(db);
        var capabilityId = await InsertCapability(db, deviceId);
        var commandId = await InsertCommand(db, deviceId, capabilityId);
        await db.ExecuteAsync("UPDATE device_command_capabilities SET capability_status='Revoked' WHERE id=@id",
            command => command.Parameters.AddWithValue("@id", capabilityId));
        var revoked = await Assert.ThrowsAsync<PostgresException>(() => db.ExecuteAsync(
            "UPDATE telematics_device_commands SET status='dispatched',dispatched_at=NOW(),attempt_count=1 WHERE id=@id",
            command => command.Parameters.AddWithValue("@id", commandId)));
        Assert.Equal("ck_stage119_command_dispatch_capability", revoked.ConstraintName);
    }

    [Fact]
    public async Task CapabilityCannotBePromotedRewrittenOrDeleted()
    {
        var db = Db();
        var deviceId = await CreateDevice(db);
        var capabilityId = await InsertCapability(db, deviceId, "Unverified");
        foreach (var statement in new[]
        {
            "UPDATE device_command_capabilities SET capability_status='Verified' WHERE id=@id",
            "UPDATE device_command_capabilities SET evidence_reference='rewritten' WHERE id=@id",
            "DELETE FROM device_command_capabilities WHERE id=@id",
        })
        {
            var exception = await Assert.ThrowsAsync<PostgresException>(() => db.ExecuteAsync(statement,
                command => command.Parameters.AddWithValue("@id", capabilityId)));
            Assert.Equal("ck_stage119_capability_immutable", exception.ConstraintName);
        }
    }

    [Fact]
    public async Task StatusCannotSkipEvidenceBearingTransitions()
    {
        var db = Db();
        var deviceId = await CreateDevice(db);
        var capabilityId = await InsertCapability(db, deviceId);
        var commandId = await InsertCommand(db, deviceId, capabilityId);
        var skipped = await Assert.ThrowsAsync<PostgresException>(() => db.ExecuteAsync(
            "UPDATE telematics_device_commands SET status='applied',applied_at=NOW() WHERE id=@id",
            command => command.Parameters.AddWithValue("@id", commandId)));
        Assert.Equal("ck_stage119_command_transition", skipped.ConstraintName);

        Assert.Equal(1, await db.ExecuteAsync(
            "UPDATE telematics_device_commands SET status='dispatched',dispatched_at=NOW(),attempt_count=1 WHERE id=@id",
            command => command.Parameters.AddWithValue("@id", commandId)));
        var rewritten = await Assert.ThrowsAsync<PostgresException>(() => db.ExecuteAsync(
            "UPDATE telematics_device_commands SET status='acknowledged',dispatched_at=NOW()+INTERVAL '1 minute',acknowledged_at=NOW() WHERE id=@id",
            command => command.Parameters.AddWithValue("@id", commandId)));
        Assert.Equal("ck_stage119_command_evidence_immutable", rewritten.ConstraintName);
        Assert.Equal(1, await db.ExecuteAsync(
            "UPDATE telematics_device_commands SET status='acknowledged',acknowledged_at=NOW() WHERE id=@id",
            command => command.Parameters.AddWithValue("@id", commandId)));
        Assert.Equal(1, await db.ExecuteAsync(
            "UPDATE telematics_device_commands SET status='applied',applied_at=NOW() WHERE id=@id",
            command => command.Parameters.AddWithValue("@id", commandId)));
    }

    [Fact]
    public async Task FailureStateRequiresAnErrorReason()
    {
        var db = Db();
        var deviceId = await CreateDevice(db);
        var capabilityId = await InsertCapability(db, deviceId);
        var commandId = await InsertCommand(db, deviceId, capabilityId);
        Assert.Equal(1, await db.ExecuteAsync(
            "UPDATE telematics_device_commands SET status='dispatched',dispatched_at=NOW(),attempt_count=1 WHERE id=@id",
            command => command.Parameters.AddWithValue("@id", commandId)));
        var missingReason = await Assert.ThrowsAsync<PostgresException>(() => db.ExecuteAsync(
            "UPDATE telematics_device_commands SET status='failed' WHERE id=@id",
            command => command.Parameters.AddWithValue("@id", commandId)));
        Assert.Equal("ck_stage119_command_error_evidence", missingReason.ConstraintName);
    }

    private static Task<long> CreateDevice(Database db, long? branchId = null) => db.InsertAsync(
        @"INSERT INTO eld_devices(device_serial,company_id,branch_id,manufacturer,device_model,hardware_revision,
                                  firmware_version,provider,status,device_state)
          VALUES(@serial,1,@branchId,'Acme','Tracker-X','rev-a','v2.3.0','Provider-X','Active','Registered')",
        command => { command.Parameters.AddWithValue("@serial", $"CMD-{Guid.NewGuid():N}"); command.Parameters.AddWithValue("@branchId", (object?)branchId ?? DBNull.Value); });

    private static Task<long> InsertCapability(Database db, long deviceId, string status = "Verified", long? branchId = null) => db.InsertAsync(
        @"INSERT INTO device_command_capabilities
            (company_id,branch_id,device_id,device_serial,manufacturer,device_model,hardware_revision,
             firmware_version,provider,command_type,command_class,capability_status,evidence_source,
             evidence_reference,observed_at,expires_at,physical_evidence_claim,certification_claim,recorded_by)
          SELECT 1,@branchId,id,device_serial,manufacturer,device_model,hardware_revision,firmware_version,provider,
                 'RequestPosition','Observation',@status,'ProviderCapabilityResponse',@reference,
                 NOW()-INTERVAL '1 minute',NOW()+INTERVAL '1 hour',FALSE,FALSE,1
            FROM eld_devices WHERE company_id=1 AND id=@deviceId",
        command => { command.Parameters.AddWithValue("@deviceId", deviceId); command.Parameters.AddWithValue("@branchId", (object?)branchId ?? DBNull.Value); command.Parameters.AddWithValue("@status", status); command.Parameters.AddWithValue("@reference", $"provider-cap-{Guid.NewGuid():N}"); });

    private static Task<long> InsertCommand(Database db, long deviceId, long capabilityId, bool physicalClaim = false,
        long? branchId = null, string? reportedPayload = null) => db.InsertAsync(
        @"INSERT INTO telematics_device_commands
            (company_id,branch_id,device_id,command_type,desired_payload,status,idempotency_key,attempt_count,
             max_attempts,scheduled_for,expires_at,requested_by,approved_by,capability_id,device_serial_snapshot,
             manufacturer_snapshot,device_model_snapshot,hardware_revision_snapshot,firmware_version_snapshot,
             provider_snapshot,command_class,purpose,source_reference,safety_confirmation_hash,governance_status,
             provider_delivery_claim,physical_outcome_claim,reported_payload)
          SELECT 1,@branchId,id,'RequestPosition','{}'::jsonb,'approved',@key,0,1,NOW(),NOW()+INTERVAL '5 minutes',
                 1,1,@capabilityId,device_serial,manufacturer,device_model,hardware_revision,firmware_version,
                 provider,'Observation','Investigate reported device condition','Approved ticket CMD-119',
                 repeat('a',64),'EvidenceVerified',FALSE,@physicalClaim,CAST(@reportedPayload AS jsonb)
            FROM eld_devices WHERE company_id=1 AND id=@deviceId",
        command => { command.Parameters.AddWithValue("@deviceId", deviceId); command.Parameters.AddWithValue("@branchId", (object?)branchId ?? DBNull.Value); command.Parameters.AddWithValue("@capabilityId", capabilityId); command.Parameters.AddWithValue("@key", Guid.NewGuid().ToString("D")); command.Parameters.AddWithValue("@physicalClaim", physicalClaim); command.Parameters.AddWithValue("@reportedPayload", (object?)reportedPayload ?? DBNull.Value); });

    private static Database Db() => new(new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
    {
        ["ConnectionStrings:DefaultConnection"] = GuardedConnection(),
        ["ConnectionStrings:SystemConnection"] = GuardedConnection(),
        ["Rls:EnforceTenantContext"] = "false",
    }).Build());

    private static string GuardedConnection()
    {
        var configured = Environment.GetEnvironmentVariable("OPSTRAX_DEVICEOPS_STAGE119_DB");
        if (string.IsNullOrWhiteSpace(configured))
            throw SkipException.ForSkip("Set OPSTRAX_DEVICEOPS_STAGE119_DB to the dedicated disposable command-governance database.");
        var builder = new NpgsqlConnectionStringBuilder(configured);
        if (builder.Host is not ("127.0.0.1" or "::1") || builder.Port != 55444 || builder.Database != "opstrax_deviceops_stage119")
            throw new InvalidOperationException("Remote-command tests require the dedicated local database on port 55444.");
        return builder.ConnectionString;
    }
}
