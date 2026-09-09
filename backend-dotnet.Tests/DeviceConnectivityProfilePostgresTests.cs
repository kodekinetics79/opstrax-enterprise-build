using Microsoft.Extensions.Configuration;
using Npgsql;
using Opstrax.Api.Data;
using Xunit.Sdk;

namespace Opstrax.Tests;

[Trait("Category", "DeviceConnectivityProfilePostgres")]
[Trait("Lane", "DedicatedDatabase")]
public sealed class DeviceConnectivityProfilePostgresTests
{
    [Fact]
    public async Task AppRoleCanReadMasksButNotEncryptedPayloadColumns()
    {
        var db = Db();
        Assert.Equal(1, await db.ScalarLongAsync(
            "SELECT CASE WHEN has_column_privilege('opstrax_app','device_connectivity_profiles','iccid_last4','SELECT') THEN 1 ELSE 0 END"));
        foreach (var column in new[] { "iccid_encrypted", "msisdn_encrypted", "apn_encrypted" })
        {
            Assert.Equal(0, await db.ScalarLongAsync(
                "SELECT CASE WHEN has_column_privilege('opstrax_app','device_connectivity_profiles',@column,'SELECT') THEN 1 ELSE 0 END",
                command => command.Parameters.AddWithValue("@column", column)));
        }
    }

    [Fact]
    public async Task PlaintextSensitiveInventoryIsRejected()
    {
        var db = Db();
        var deviceId = await CreateDevice(db);
        try
        {
            var error = await Assert.ThrowsAsync<PostgresException>(() => InsertProfile(
                db, deviceId, "plaintext-iccid", new string('a', 64), Guid.NewGuid()));
            Assert.Equal(PostgresErrorCodes.CheckViolation, error.SqlState);
        }
        finally { await DeleteDevice(db, deviceId); }
    }

    [Fact]
    public async Task OneCurrentProfilePerDeviceAndIccidIsEnforced()
    {
        var db = Db();
        var firstDevice = await CreateDevice(db);
        var secondDevice = await CreateDevice(db);
        var bidx = Guid.NewGuid().ToString("N").PadRight(64, 'a');
        try
        {
            await InsertProfile(db, firstDevice, "enc:first", bidx, Guid.NewGuid());

            var sameDevice = await Assert.ThrowsAsync<PostgresException>(() => InsertProfile(
                db, firstDevice, "enc:second", Guid.NewGuid().ToString("N").PadRight(64, 'b'), Guid.NewGuid()));
            Assert.Equal(PostgresErrorCodes.UniqueViolation, sameDevice.SqlState);
            Assert.Equal("uq_stage116_connectivity_current_device", sameDevice.ConstraintName);

            var reusedIccid = await Assert.ThrowsAsync<PostgresException>(() => InsertProfile(
                db, secondDevice, "enc:third", bidx, Guid.NewGuid()));
            Assert.Equal(PostgresErrorCodes.UniqueViolation, reusedIccid.SqlState);
            Assert.Equal("uq_stage116_connectivity_current_iccid", reusedIccid.ConstraintName);
        }
        finally
        {
            await DeleteDevice(db, firstDevice);
            await DeleteDevice(db, secondDevice);
        }
    }

    [Fact]
    public async Task EndedProfileCannotBeRewrittenAndIdempotencyCannotDuplicate()
    {
        var db = Db();
        var deviceId = await CreateDevice(db);
        var idempotency = Guid.NewGuid();
        try
        {
            var profileId = await InsertProfile(db, deviceId, "enc:first", Guid.NewGuid().ToString("N").PadRight(64, 'c'), idempotency);
            await db.ExecuteAsync(
                @"UPDATE device_connectivity_profiles
                     SET assignment_status='Ended',effective_to=effective_from+INTERVAL '1 minute',
                         end_reason='Observed SIM replacement',ended_by=1,updated_at=NOW()
                   WHERE id=@id",
                command => command.Parameters.AddWithValue("@id", profileId));

            var rewrite = await Assert.ThrowsAsync<PostgresException>(() => db.ExecuteAsync(
                "UPDATE device_connectivity_profiles SET end_reason='Rewritten history' WHERE id=@id",
                command => command.Parameters.AddWithValue("@id", profileId)));
            Assert.Equal(PostgresErrorCodes.CheckViolation, rewrite.SqlState);

            var duplicate = await Assert.ThrowsAsync<PostgresException>(() => InsertProfile(
                db, deviceId, "enc:replacement", Guid.NewGuid().ToString("N").PadRight(64, 'd'), idempotency,
                DateTimeOffset.UtcNow.AddMinutes(2)));
            Assert.Equal(PostgresErrorCodes.UniqueViolation, duplicate.SqlState);
            Assert.Equal("uq_stage116_connectivity_idempotency", duplicate.ConstraintName);
        }
        finally { await DeleteDevice(db, deviceId); }
    }

    private static async Task<long> CreateDevice(Database db) => await db.InsertAsync(
        "INSERT INTO eld_devices(device_serial,company_id,branch_id) VALUES(@serial,1,NULL)",
        command => command.Parameters.AddWithValue("@serial", $"SIM-TEST-{Guid.NewGuid():N}"));

    private static Task<long> InsertProfile(
        Database db, long deviceId, string iccidEncrypted, string iccidBidx, Guid idempotency,
        DateTimeOffset? effectiveFrom = null) => db.InsertAsync(
        @"INSERT INTO device_connectivity_profiles
            (company_id,branch_id,device_id,profile_kind,carrier_name,
             iccid_encrypted,iccid_bidx,iccid_last4,apn_configured,
             assignment_status,effective_from,source_reference,change_reason,idempotency_key,created_by)
          VALUES (1,NULL,@deviceId,'PhysicalSIM','Test Carrier',
                  @iccidEncrypted,@iccidBidx,'6789',FALSE,
                  'Assigned',@effectiveFrom,'carrier-test-reference','Initial test assignment',@idempotency,1)",
        command =>
        {
            command.Parameters.AddWithValue("@deviceId", deviceId);
            command.Parameters.AddWithValue("@iccidEncrypted", iccidEncrypted);
            command.Parameters.AddWithValue("@iccidBidx", iccidBidx);
            command.Parameters.AddWithValue("@effectiveFrom", effectiveFrom ?? DateTimeOffset.UtcNow);
            command.Parameters.AddWithValue("@idempotency", idempotency);
        });

    private static async Task DeleteDevice(Database db, long deviceId)
    {
        await db.ExecuteAsync("DELETE FROM device_connectivity_profiles WHERE device_id=@id", command => command.Parameters.AddWithValue("@id", deviceId));
        await db.ExecuteAsync("DELETE FROM eld_devices WHERE id=@id", command => command.Parameters.AddWithValue("@id", deviceId));
    }

    private static Database Db() => new(new ConfigurationBuilder().AddInMemoryCollection(
        new Dictionary<string, string?>
        {
            ["ConnectionStrings:DefaultConnection"] = GuardedConnection(),
            ["ConnectionStrings:SystemConnection"] = GuardedConnection(),
            ["Rls:EnforceTenantContext"] = "false",
        }).Build());

    private static string GuardedConnection()
    {
        var configured = Environment.GetEnvironmentVariable("OPSTRAX_DEVICEOPS_STAGE116_DB");
        if (string.IsNullOrWhiteSpace(configured))
            throw SkipException.ForSkip("Set OPSTRAX_DEVICEOPS_STAGE116_DB to the dedicated disposable DeviceOps database.");
        var builder = new NpgsqlConnectionStringBuilder(configured);
        if (builder.Host is not ("127.0.0.1" or "::1") || builder.Port != 55443 ||
            builder.Database != "opstrax_deviceops_stage116")
            throw new InvalidOperationException("DeviceOps connectivity tests require the dedicated local database on port 55443.");
        return builder.ConnectionString;
    }
}
