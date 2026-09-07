using Microsoft.Extensions.Configuration;
using Npgsql;
using Opstrax.Api.Data;
using Xunit.Sdk;

namespace Opstrax.Tests;

[Trait("Category", "DeviceFirmwareCampaignPostgres")]
public sealed class DeviceFirmwareCampaignPostgresTests
{
    [Fact]
    public async Task AppRoleCanReadButCannotMutatePlanningHistory()
    {
        var db = Db();
        foreach (var table in new[] { "device_firmware_campaigns", "device_firmware_campaign_targets" })
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
    public async Task CampaignCannotEscapeExternalHoldOrClaimRemoteUpgrade()
    {
        var db = Db();
        var invalidStatus = await Assert.ThrowsAsync<PostgresException>(() => InsertCampaign(
            db, executionStatus: "Queued"));
        Assert.Equal(PostgresErrorCodes.CheckViolation, invalidStatus.SqlState);

        var invalidClaim = await Assert.ThrowsAsync<PostgresException>(() => InsertCampaign(
            db, remoteUpgradeClaim: true));
        Assert.Equal(PostgresErrorCodes.CheckViolation, invalidClaim.SqlState);
    }

    [Fact]
    public async Task TargetCannotClaimDeliveryOrChangeCampaignVersion()
    {
        var db = Db();
        var deviceId = await CreateDevice(db);
        var campaignId = await InsertCampaign(db);

        var deliveryClaim = await Assert.ThrowsAsync<PostgresException>(() => InsertTarget(
            db, campaignId, deviceId, deliveryStatus: "Applied"));
        Assert.Equal(PostgresErrorCodes.CheckViolation, deliveryClaim.SqlState);

        var wrongVersion = await Assert.ThrowsAsync<PostgresException>(() => InsertTarget(
            db, campaignId, deviceId, targetVersion: "v9.9.9"));
        Assert.Equal(PostgresErrorCodes.ForeignKeyViolation, wrongVersion.SqlState);
    }

    [Fact]
    public async Task RecordedCampaignAndTargetAreImmutable()
    {
        var db = Db();
        var deviceId = await CreateDevice(db);
        var campaignId = await InsertCampaign(db);
        var targetId = await InsertTarget(db, campaignId, deviceId);

        var campaignRewrite = await Assert.ThrowsAsync<PostgresException>(() => db.ExecuteAsync(
            "UPDATE device_firmware_campaigns SET change_reason='rewritten' WHERE id=@id",
            command => command.Parameters.AddWithValue("@id", campaignId)));
        Assert.Equal(PostgresErrorCodes.CheckViolation, campaignRewrite.SqlState);

        var targetRewrite = await Assert.ThrowsAsync<PostgresException>(() => db.ExecuteAsync(
            "UPDATE device_firmware_campaign_targets SET planning_reason='rewritten' WHERE id=@id",
            command => command.Parameters.AddWithValue("@id", targetId)));
        Assert.Equal(PostgresErrorCodes.CheckViolation, targetRewrite.SqlState);
    }

    [Fact]
    public async Task IdempotencyAndOneTargetPerCampaignAreEnforced()
    {
        var db = Db();
        var idempotency = Guid.NewGuid();
        var first = await InsertCampaign(db, idempotency: idempotency);
        var duplicateCampaign = await Assert.ThrowsAsync<PostgresException>(() => InsertCampaign(db, idempotency: idempotency));
        Assert.Equal(PostgresErrorCodes.UniqueViolation, duplicateCampaign.SqlState);
        Assert.Equal("uq_stage117_campaign_idempotency", duplicateCampaign.ConstraintName);

        var deviceId = await CreateDevice(db);
        await InsertTarget(db, first, deviceId);
        var duplicateTarget = await Assert.ThrowsAsync<PostgresException>(() => InsertTarget(db, first, deviceId));
        Assert.Equal(PostgresErrorCodes.UniqueViolation, duplicateTarget.SqlState);
        Assert.Equal("uq_stage117_target_campaign_device", duplicateTarget.ConstraintName);
    }

    private static Task<long> CreateDevice(Database db) => db.InsertAsync(
        @"INSERT INTO eld_devices(device_serial,company_id,branch_id,manufacturer,device_model,hardware_revision,firmware_version)
          VALUES(@serial,1,NULL,'Acme','Tracker-X','rev-a','v2.3.0')",
        command => command.Parameters.AddWithValue("@serial", $"FW-TEST-{Guid.NewGuid():N}"));

    private static Task<long> InsertCampaign(
        Database db,
        Guid? idempotency = null,
        string executionStatus = "ExternalHold",
        bool remoteUpgradeClaim = false) => db.InsertAsync(
        @"INSERT INTO device_firmware_campaigns
            (company_id,branch_id,campaign_name,target_firmware_version,rollback_firmware_version,
             rollout_strategy,batch_size,scheduled_for,maintenance_window_minutes,
             execution_status,provider_capability_status,remote_upgrade_claim,external_hold_reason,
             source_reference,change_reason,idempotency_key,created_by)
          VALUES
            (1,NULL,'Test canary','v2.4.1','v2.3.0','Canary',1,NOW()+INTERVAL '1 hour',60,
             @executionStatus,'Unverified',@remoteUpgradeClaim,'Provider and physical evidence pending',
             'Test change ticket','Test planning reason',@idempotency,1)",
        command =>
        {
            command.Parameters.AddWithValue("@executionStatus", executionStatus);
            command.Parameters.AddWithValue("@remoteUpgradeClaim", remoteUpgradeClaim);
            command.Parameters.AddWithValue("@idempotency", idempotency ?? Guid.NewGuid());
        });

    private static Task<long> InsertTarget(
        Database db,
        long campaignId,
        long deviceId,
        string targetVersion = "v2.4.1",
        string deliveryStatus = "ExternalHold") => db.InsertAsync(
        @"INSERT INTO device_firmware_campaign_targets
            (company_id,branch_id,campaign_id,device_id,device_serial,manufacturer,device_model,
             hardware_revision,reported_firmware_version,target_firmware_version,planning_status,
             planning_reason,rollout_batch,delivery_status,provider_capability_status,remote_upgrade_claim)
          SELECT 1,NULL,@campaignId,@deviceId,device_serial,manufacturer,device_model,hardware_revision,
                 firmware_version,@targetVersion,'ReadyForExternalEvidence','External evidence remains required',
                 1,@deliveryStatus,'Unverified',FALSE
            FROM eld_devices WHERE id=@deviceId AND company_id=1",
        command =>
        {
            command.Parameters.AddWithValue("@campaignId", campaignId);
            command.Parameters.AddWithValue("@deviceId", deviceId);
            command.Parameters.AddWithValue("@targetVersion", targetVersion);
            command.Parameters.AddWithValue("@deliveryStatus", deliveryStatus);
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
        var configured = Environment.GetEnvironmentVariable("OPSTRAX_DEVICEOPS_STAGE117_DB");
        if (string.IsNullOrWhiteSpace(configured))
            throw SkipException.ForSkip("Set OPSTRAX_DEVICEOPS_STAGE117_DB to the dedicated disposable DeviceOps database.");
        var builder = new NpgsqlConnectionStringBuilder(configured);
        if (builder.Host is not ("127.0.0.1" or "::1") || builder.Port != 55443 ||
            builder.Database != "opstrax_deviceops_stage117")
            throw new InvalidOperationException("Firmware campaign tests require the dedicated local database on port 55443.");
        return builder.ConnectionString;
    }
}
