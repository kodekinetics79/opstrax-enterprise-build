using Microsoft.Extensions.Configuration;
using Npgsql;
using Opstrax.Api.Data;
using Xunit.Sdk;

namespace Opstrax.Tests;

[Trait("Category", "DeviceRetirementPostgres")]
[Trait("Lane", "DedicatedDatabase")]
public sealed class DeviceRetirementPostgresTests
{
    [Fact]
    public async Task ExactClosedDeviceCreatesImmutableUnverifiedReceipt()
    {
        var db = Db();
        var fixture = await CreateFixture(db);
        var effectiveAt = DateTimeOffset.UtcNow;
        var profile = await InsertCurrentConnectivity(db, fixture, effectiveAt.AddDays(-1));
        await EndConnectivity(db, fixture, profile, effectiveAt);
        await RetireDeviceRow(db, fixture, effectiveAt, clearCredentials: true);

        var receipt = await InsertReceipt(db, fixture, effectiveAt, profile);
        var stored = await db.QuerySingleAsync(
            @"SELECT credentials_revoked,record_status,physical_disposition_status,
                     physical_disposition_claim,certification_claim,row_version_before,row_version_after
                FROM device_retirement_records WHERE id=@id",
            command => command.Parameters.AddWithValue("@id", receipt));

        Assert.NotNull(stored);
        Assert.Equal(true, stored!["credentialsRevoked"]);
        Assert.Equal("OperatorRecorded", stored["recordStatus"]);
        Assert.Equal("Unverified", stored["physicalDispositionStatus"]);
        Assert.Equal(false, stored["physicalDispositionClaim"]);
        Assert.Equal(false, stored["certificationClaim"]);
        Assert.Equal(4L, Convert.ToInt64(stored["rowVersionBefore"]));
        Assert.Equal(5L, Convert.ToInt64(stored["rowVersionAfter"]));

        foreach (var statement in new[]
        {
            "UPDATE device_retirement_records SET source_reference='changed' WHERE id=@id",
            "DELETE FROM device_retirement_records WHERE id=@id"
        })
        {
            var immutable = await Assert.ThrowsAsync<PostgresException>(() => db.ExecuteAsync(statement,
                command => command.Parameters.AddWithValue("@id", receipt)));
            Assert.Equal("ck_stage123_retirement_immutable", immutable.ConstraintName);
        }
    }

    [Fact]
    public async Task ReceiptRejectsCurrentInstallation()
    {
        var db = Db();
        var fixture = await CreateFixture(db);
        var effectiveAt = DateTimeOffset.UtcNow;
        await db.InsertAsync(
            @"INSERT INTO device_installations(company_id,branch_id,device_id,status,effective_from)
              VALUES(@company,@branch,@device,'Installed',NOW()-INTERVAL '1 day')",
            command => Bind(command, fixture));
        await RetireDeviceRow(db, fixture, effectiveAt, clearCredentials: true);

        var failure = await Assert.ThrowsAsync<PostgresException>(() => InsertReceipt(db, fixture, effectiveAt, null));
        Assert.Equal("ck_stage123_no_current_installation", failure.ConstraintName);
    }

    [Fact]
    public async Task ReceiptRejectsCurrentConnectivityAndUsableCredentials()
    {
        var db = Db();
        var currentProfileFixture = await CreateFixture(db);
        var firstEffectiveAt = DateTimeOffset.UtcNow;
        await InsertCurrentConnectivity(db, currentProfileFixture, firstEffectiveAt.AddDays(-1));
        await RetireDeviceRow(db, currentProfileFixture, firstEffectiveAt, clearCredentials: true);
        var currentProfileFailure = await Assert.ThrowsAsync<PostgresException>(() =>
            InsertReceipt(db, currentProfileFixture, firstEffectiveAt, null));
        Assert.Equal("ck_stage123_no_current_connectivity", currentProfileFailure.ConstraintName);

        var credentialFixture = await CreateFixture(db);
        var secondEffectiveAt = DateTimeOffset.UtcNow;
        await RetireDeviceRow(db, credentialFixture, secondEffectiveAt, clearCredentials: false);
        var credentialFailure = await Assert.ThrowsAsync<PostgresException>(() =>
            InsertReceipt(db, credentialFixture, secondEffectiveAt, null));
        Assert.Equal("ck_stage123_retired_credentials_cleared", credentialFailure.ConstraintName);
    }

    [Fact]
    public async Task RuntimeRolesCannotRewriteOrForgeRetirementReceipts()
    {
        var db = Db();
        Assert.Equal(1, await Has(db, "opstrax_app", "SELECT"));
        foreach (var privilege in new[] { "INSERT", "UPDATE", "DELETE", "TRUNCATE" })
            Assert.Equal(0, await Has(db, "opstrax_app", privilege));
        Assert.Equal(1, await Has(db, "opstrax_system", "SELECT,INSERT"));
        foreach (var privilege in new[] { "UPDATE", "DELETE", "TRUNCATE" })
            Assert.Equal(0, await Has(db, "opstrax_system", privilege));
    }

    private static Task<long> Has(Database db, string role, string privilege) => db.ScalarLongAsync(
        "SELECT CASE WHEN has_table_privilege(@role,'device_retirement_records',@privilege) THEN 1 ELSE 0 END",
        command =>
        {
            command.Parameters.AddWithValue("@role", role);
            command.Parameters.AddWithValue("@privilege", privilege);
        });

    private static async Task<Fixture> CreateFixture(Database db)
    {
        var suffix = Guid.NewGuid().ToString("N");
        var company = await db.InsertAsync(
            "INSERT INTO companies(company_code,name,industry) VALUES(@code,'Retirement test','Transportation')",
            command => command.Parameters.AddWithValue("@code", $"RET-{suffix[..10]}"));
        var branch = await db.InsertAsync(
            "INSERT INTO branches(company_id,branch_code,name) VALUES(@company,@code,'Test branch')",
            command =>
            {
                command.Parameters.AddWithValue("@company", company);
                command.Parameters.AddWithValue("@code", $"BR-{suffix}");
            });
        var user = await db.InsertAsync(
            @"INSERT INTO users(company_id,branch_id,full_name,email,role_name,status)
              VALUES(@company,@branch,'Retirement operator',@email,'Fleet Manager','Active')",
            command =>
            {
                command.Parameters.AddWithValue("@company", company);
                command.Parameters.AddWithValue("@branch", branch);
                command.Parameters.AddWithValue("@email", $"retirement-{suffix}@example.test");
            });
        var serial = $"RET-DEV-{suffix}";
        var device = await db.InsertAsync(
            @"INSERT INTO eld_devices(company_id,branch_id,device_serial,status,device_state,row_version,
                                      api_key_hash,hmac_secret_encrypted)
              VALUES(@company,@branch,@serial,'Active','Online',4,@apiKey,'enc:test-secret')",
            command =>
            {
                command.Parameters.AddWithValue("@company", company);
                command.Parameters.AddWithValue("@branch", branch);
                command.Parameters.AddWithValue("@serial", serial);
                command.Parameters.AddWithValue("@apiKey", new string('a', 64));
            });
        return new Fixture(company, branch, user, device, serial);
    }

    private static Task<long> InsertCurrentConnectivity(
        Database db, Fixture fixture, DateTimeOffset effectiveFrom) => db.InsertAsync(
        @"INSERT INTO device_connectivity_profiles
            (company_id,branch_id,device_id,assignment_status,effective_from)
          VALUES(@company,@branch,@device,'Assigned',@effectiveFrom)",
        command =>
        {
            Bind(command, fixture);
            command.Parameters.AddWithValue("@effectiveFrom", effectiveFrom);
        });

    private static Task EndConnectivity(
        Database db, Fixture fixture, long profileId, DateTimeOffset effectiveAt) => db.ExecuteAsync(
        @"UPDATE device_connectivity_profiles
             SET assignment_status='Ended',effective_to=@effectiveAt,end_reason='Device retirement',ended_by=@user
           WHERE company_id=@company AND id=@profile AND device_id=@device",
        command =>
        {
            Bind(command, fixture);
            command.Parameters.AddWithValue("@profile", profileId);
            command.Parameters.AddWithValue("@effectiveAt", effectiveAt);
        });

    private static Task RetireDeviceRow(
        Database db, Fixture fixture, DateTimeOffset effectiveAt, bool clearCredentials) => db.ExecuteAsync(
        $@"UPDATE eld_devices SET status='Retired',device_state='Retired',retired_at=@effectiveAt,
                 revoked_at=@effectiveAt,row_version=5
                 {(clearCredentials ? ",api_key_hash=NULL,hmac_secret=NULL,hmac_secret_encrypted=NULL,api_key_previous_hash=NULL,api_key_previous_valid_until=NULL,hmac_previous_secret_encrypted=NULL,hmac_previous_valid_until=NULL" : "")}
            WHERE company_id=@company AND id=@device",
        command =>
        {
            Bind(command, fixture);
            command.Parameters.AddWithValue("@effectiveAt", effectiveAt);
        });

    private static Task<long> InsertReceipt(
        Database db, Fixture fixture, DateTimeOffset effectiveAt, long? endedProfileId) => db.InsertAsync(
        @"INSERT INTO device_retirement_records
            (company_id,branch_id,device_id,device_serial_snapshot,retirement_reason,disposition_plan,
             source_reference,effective_at,prior_status,prior_device_state,row_version_before,row_version_after,
             ended_connectivity_profile_id,idempotency_key,retired_by)
          VALUES(@company,@branch,@device,@serial,'Fleet replacement cycle','ReturnToVendor','WO-RET-123',
                 @effectiveAt,'Active','Online',4,5,@profile,@key,@user)",
        command =>
        {
            Bind(command, fixture);
            command.Parameters.AddWithValue("@serial", fixture.Serial);
            command.Parameters.AddWithValue("@effectiveAt", effectiveAt);
            command.Parameters.AddWithValue("@profile", (object?)endedProfileId ?? DBNull.Value);
            command.Parameters.AddWithValue("@key", Guid.NewGuid());
        });

    private static void Bind(NpgsqlCommand command, Fixture fixture)
    {
        command.Parameters.AddWithValue("@company", fixture.CompanyId);
        command.Parameters.AddWithValue("@branch", fixture.BranchId);
        command.Parameters.AddWithValue("@device", fixture.DeviceId);
        command.Parameters.AddWithValue("@user", fixture.UserId);
    }

    private sealed record Fixture(long CompanyId, long BranchId, long UserId, long DeviceId, string Serial);

    private static Database Db() => new(new ConfigurationBuilder().AddInMemoryCollection(
        new Dictionary<string, string?>
        {
            ["ConnectionStrings:DefaultConnection"] = GuardedConnection(),
            ["ConnectionStrings:SystemConnection"] = GuardedConnection(),
            ["Rls:EnforceTenantContext"] = "false",
        }).Build());

    private static string GuardedConnection()
    {
        var configured = Environment.GetEnvironmentVariable("OPSTRAX_DEVICEOPS_STAGE123_DB");
        if (string.IsNullOrWhiteSpace(configured))
            throw SkipException.ForSkip("Set OPSTRAX_DEVICEOPS_STAGE123_DB to the dedicated disposable retirement database.");
        var builder = new NpgsqlConnectionStringBuilder(configured);
        if (builder.Host is not ("127.0.0.1" or "::1") || builder.Port != 55448 ||
            builder.Database != "opstrax_deviceops_stage123")
            throw new InvalidOperationException("Retirement tests require the dedicated local database on port 55448.");
        return builder.ConnectionString;
    }
}
