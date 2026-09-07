using Microsoft.Extensions.Configuration;
using Npgsql;
using Opstrax.Api.Data;
using Xunit.Sdk;

namespace Opstrax.Tests;

[Trait("Category", "DeviceRmaPostgres")]
public sealed class DeviceRmaPostgresTests
{
    [Fact]
    public async Task ApplicationRoleCanReadButCannotRewriteRmaHistory()
    {
        var db = Db();
        foreach (var table in new[] { "device_rma_cases", "device_rma_events", "device_rma_replacements" })
        {
            Assert.Equal(1, await db.ScalarLongAsync(
                "SELECT CASE WHEN has_table_privilege('opstrax_app',@table,'SELECT') THEN 1 ELSE 0 END",
                command => command.Parameters.AddWithValue("@table", table)));
            foreach (var privilege in new[] { "INSERT", "UPDATE", "DELETE" })
                Assert.Equal(0, await db.ScalarLongAsync(
                    "SELECT CASE WHEN has_table_privilege('opstrax_app',@table,@privilege) THEN 1 ELSE 0 END",
                    command =>
                    {
                        command.Parameters.AddWithValue("@table", table);
                        command.Parameters.AddWithValue("@privilege", privilege);
                    }));
        }
    }

    [Fact]
    public async Task TenantPolicyHidesAnotherCompanyCase()
    {
        var deviceId = await CreateDevice(Db(), 1);
        await InsertCase(Db(), 1, deviceId);
        await using var connection = new NpgsqlConnection(GuardedConnection());
        await connection.OpenAsync();
        await using var transaction = await connection.BeginTransactionAsync();
        await using (var setup = new NpgsqlCommand(
            "SET LOCAL ROLE opstrax_app; SELECT set_config('app.current_tenant_id','2',true);", connection, transaction))
            await setup.ExecuteNonQueryAsync();
        await using var query = new NpgsqlCommand("SELECT COUNT(*) FROM device_rma_cases", connection, transaction);
        Assert.Equal(0L, Convert.ToInt64(await query.ExecuteScalarAsync()));
    }

    [Fact]
    public async Task WarrantyAndPhysicalClaimsCannotBePromoted()
    {
        var db = Db();
        var deviceId = await CreateDevice(db, 1);
        var badWarranty = await Assert.ThrowsAsync<PostgresException>(() => InsertCase(
            db, 1, deviceId, warrantyStatus: "Verified"));
        Assert.Equal(PostgresErrorCodes.CheckViolation, badWarranty.SqlState);

        var caseId = await InsertCase(db, 1, deviceId);
        var badEvent = await Assert.ThrowsAsync<PostgresException>(() => InsertEvent(
            db, 1, caseId, deviceId, physicalClaim: true));
        Assert.Equal(PostgresErrorCodes.CheckViolation, badEvent.SqlState);
    }

    [Fact]
    public async Task ReplacementMustBeDistinctAndRemainsExternalHold()
    {
        var db = Db();
        var failedId = await CreateDevice(db, 1);
        var replacementId = await CreateDevice(db, 1);
        var caseId = await InsertCase(db, 1, failedId);

        var sameDevice = await Assert.ThrowsAsync<PostgresException>(() => InsertReplacement(
            db, 1, caseId, failedId, failedId));
        Assert.Equal(PostgresErrorCodes.CheckViolation, sameDevice.SqlState);

        var promoted = await Assert.ThrowsAsync<PostgresException>(() => InsertReplacement(
            db, 1, caseId, failedId, replacementId, swapStatus: "Completed"));
        Assert.Equal(PostgresErrorCodes.CheckViolation, promoted.SqlState);

        Assert.True(await InsertReplacement(db, 1, caseId, failedId, replacementId) > 0);
    }

    [Fact]
    public async Task CaseEventAndReplacementRowsAreImmutable()
    {
        var db = Db();
        var failedId = await CreateDevice(db, 1);
        var replacementId = await CreateDevice(db, 1);
        var caseId = await InsertCase(db, 1, failedId);
        var eventId = await InsertEvent(db, 1, caseId, failedId);
        var replacementLinkId = await InsertReplacement(db, 1, caseId, failedId, replacementId);
        foreach (var statement in new[]
        {
            $"UPDATE device_rma_cases SET failure_description='rewritten case history' WHERE id={caseId}",
            $"DELETE FROM device_rma_events WHERE id={eventId}",
            $"UPDATE device_rma_replacements SET change_reason='rewritten' WHERE id={replacementLinkId}",
        })
        {
            var exception = await Assert.ThrowsAsync<PostgresException>(() => db.ExecuteAsync(statement));
            Assert.Equal(PostgresErrorCodes.CheckViolation, exception.SqlState);
        }
    }

    [Fact]
    public async Task EventSequenceAndIdempotencyAreUniquePerGovernedBoundary()
    {
        var db = Db();
        var deviceId = await CreateDevice(db, 1);
        var idempotency = Guid.NewGuid();
        var caseId = await InsertCase(db, 1, deviceId, idempotency: idempotency);
        var duplicateCase = await Assert.ThrowsAsync<PostgresException>(() => InsertCase(
            db, 1, deviceId, idempotency: idempotency));
        Assert.Equal("uq_stage118_case_idempotency", duplicateCase.ConstraintName);

        await InsertEvent(db, 1, caseId, deviceId, sequence: 1);
        var duplicateSequence = await Assert.ThrowsAsync<PostgresException>(() => InsertEvent(
            db, 1, caseId, deviceId, sequence: 1));
        Assert.Equal("uq_stage118_event_sequence", duplicateSequence.ConstraintName);
    }

    private static Task<long> CreateDevice(Database db, long companyId) => db.InsertAsync(
        @"INSERT INTO eld_devices(device_serial,company_id,branch_id,manufacturer,device_model,hardware_revision,firmware_version)
          VALUES(@serial,@company,NULL,'Acme','Tracker-X','rev-a','v2.3.0')",
        command =>
        {
            command.Parameters.AddWithValue("@serial", $"RMA-TEST-{Guid.NewGuid():N}");
            command.Parameters.AddWithValue("@company", companyId);
        });

    private static Task<long> InsertCase(Database db, long companyId, long deviceId,
        string warrantyStatus = "Unverified", Guid? idempotency = null) => db.InsertAsync(
        @"INSERT INTO device_rma_cases
            (company_id,branch_id,device_id,device_serial,manufacturer,device_model,hardware_revision,
             reported_firmware_version,severity,failure_category,failure_description,observed_at,
             warranty_posture,warranty_reference,warranty_evidence_status,support_sla_reference,
             response_due_at,source_reference,physical_evidence_claim,idempotency_key,created_by)
          SELECT @company,NULL,id,device_serial,manufacturer,device_model,hardware_revision,firmware_version,
                 'P1','Power','Repeated power loss during operation',NOW()-INTERVAL '1 hour',
                 'ClaimedInWarranty','Warranty policy W-42',@warrantyStatus,'SLA-4H',NOW()+INTERVAL '3 hours',
                 'Support ticket SUP-118',FALSE,@idempotency,1
            FROM eld_devices WHERE company_id=@company AND id=@deviceId",
        command =>
        {
            command.Parameters.AddWithValue("@company", companyId);
            command.Parameters.AddWithValue("@deviceId", deviceId);
            command.Parameters.AddWithValue("@warrantyStatus", warrantyStatus);
            command.Parameters.AddWithValue("@idempotency", idempotency ?? Guid.NewGuid());
        });

    private static Task<long> InsertEvent(Database db, long companyId, long caseId, long deviceId,
        int sequence = 1, bool physicalClaim = false) => db.InsertAsync(
        @"INSERT INTO device_rma_events
            (company_id,branch_id,case_id,device_id,sequence_number,event_type,case_status_after,
             occurred_at,custody_location,tracking_reference,evidence_reference,evidence_status,
             notes,physical_completion_claim,idempotency_key,recorded_by)
          VALUES(@company,NULL,@caseId,@deviceId,@sequence,'ReturnAuthorized','AwaitingReturn',NOW(),
                 NULL,NULL,'Vendor RMA RA-118','Unverified','Return authorization recorded',
                 @physicalClaim,@idempotency,1)",
        command =>
        {
            command.Parameters.AddWithValue("@company", companyId);
            command.Parameters.AddWithValue("@caseId", caseId);
            command.Parameters.AddWithValue("@deviceId", deviceId);
            command.Parameters.AddWithValue("@sequence", sequence);
            command.Parameters.AddWithValue("@physicalClaim", physicalClaim);
            command.Parameters.AddWithValue("@idempotency", Guid.NewGuid());
        });

    private static Task<long> InsertReplacement(Database db, long companyId, long caseId,
        long failedId, long replacementId, string swapStatus = "ExternalHold") => db.InsertAsync(
        @"INSERT INTO device_rma_replacements
            (company_id,branch_id,case_id,failed_device_id,failed_device_serial,replacement_device_id,
             replacement_device_serial,replacement_manufacturer,replacement_device_model,
             replacement_hardware_revision,replacement_firmware_version,replacement_status,
             physical_swap_status,physical_swap_claim,change_reason,source_reference,idempotency_key,created_by)
          SELECT @company,NULL,@caseId,@failedId,failed.device_serial,replacement.id,replacement.device_serial,
                 replacement.manufacturer,replacement.device_model,replacement.hardware_revision,
                 replacement.firmware_version,'Planned',@swapStatus,FALSE,'Plan exact replacement unit',
                 'Support ticket SUP-118',@idempotency,1
            FROM eld_devices failed CROSS JOIN eld_devices replacement
           WHERE failed.company_id=@company AND failed.id=@failedId
             AND replacement.company_id=@company AND replacement.id=@replacementId",
        command =>
        {
            command.Parameters.AddWithValue("@company", companyId);
            command.Parameters.AddWithValue("@caseId", caseId);
            command.Parameters.AddWithValue("@failedId", failedId);
            command.Parameters.AddWithValue("@replacementId", replacementId);
            command.Parameters.AddWithValue("@swapStatus", swapStatus);
            command.Parameters.AddWithValue("@idempotency", Guid.NewGuid());
        });

    private static Database Db() => new(new ConfigurationBuilder().AddInMemoryCollection(
        new Dictionary<string, string?>
        {
            ["ConnectionStrings:DefaultConnection"] = GuardedConnection(),
            ["ConnectionStrings:SystemConnection"] = GuardedConnection(),
            ["Rls:EnforceTenantContext"] = "false",
        }).Build());

    private static string GuardedConnection()
    {
        var configured = Environment.GetEnvironmentVariable("OPSTRAX_DEVICEOPS_STAGE118_DB");
        if (string.IsNullOrWhiteSpace(configured))
            throw SkipException.ForSkip("Set OPSTRAX_DEVICEOPS_STAGE118_DB to the dedicated disposable DeviceOps database.");
        var builder = new NpgsqlConnectionStringBuilder(configured);
        if (builder.Host is not ("127.0.0.1" or "::1") || builder.Port != 55443 ||
            builder.Database != "opstrax_deviceops_stage118")
            throw new InvalidOperationException("RMA tests require the dedicated local database on port 55443.");
        return builder.ConnectionString;
    }
}
