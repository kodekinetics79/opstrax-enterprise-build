using Microsoft.Extensions.Configuration;
using Npgsql;
using Opstrax.Api.Data;
using Xunit.Sdk;

namespace Opstrax.Tests;

[Trait("Category", "DeviceSupportTierPostgres")]
[Trait("Lane", "DedicatedDatabase")]
public sealed class DeviceSupportTierPostgresTests
{
    [Fact]
    public async Task RoutingPlanCanChangeAndEndWithoutPromotedClaims()
    {
        var db = Db();
        var fixture = await CreateFixture(db);
        await Insert(db, fixture, "Assigned", "Assigned", "Standard", "BusinessHours", 240, -3);
        await Insert(db, fixture, "Changed", "Assigned", "CriticalOps", "AlwaysOn", 30, -2,
            escalation: "ESC-CRITICAL", commercial: "ORDER-CRITICAL");
        var activeRetirement = await Assert.ThrowsAsync<PostgresException>(() => db.ExecuteAsync(
            "UPDATE eld_devices SET status='Retired',device_state='Retired' WHERE company_id=@company AND id=@device",
            command => { command.Parameters.AddWithValue("@company", fixture.CompanyId); command.Parameters.AddWithValue("@device", fixture.DeviceId); }));
        Assert.Equal("ck_stage126_device_support_tier_active", activeRetirement.ConstraintName);
        var ended = await Insert(db, fixture, "Ended", "NotAssigned", "CriticalOps", "AlwaysOn", 30, -1,
            escalation: "ESC-CRITICAL", commercial: "ORDER-CRITICAL");
        await db.ExecuteAsync(
            "UPDATE eld_devices SET status='Retired',device_state='Retired' WHERE company_id=@company AND id=@device",
            command => { command.Parameters.AddWithValue("@company", fixture.CompanyId); command.Parameters.AddWithValue("@device", fixture.DeviceId); });
        var row = await db.QuerySingleAsync(
            @"SELECT state_after,record_status,commercial_entitlement_verified_claim,provider_support_claim,
                     hardware_supportability_claim,certification_claim
                FROM device_support_tier_events WHERE id=@id",
            command => command.Parameters.AddWithValue("@id", ended));
        Assert.Equal("NotAssigned", row!["stateAfter"]);
        Assert.Equal("OperatorRecordedUnverified", row["recordStatus"]);
        Assert.Equal(false, row["commercialEntitlementVerifiedClaim"]);
        Assert.Equal(false, row["providerSupportClaim"]);
        Assert.Equal(false, row["hardwareSupportabilityClaim"]);
        Assert.Equal(false, row["certificationClaim"]);
    }

    [Fact]
    public async Task FirstEventMaterialChangesAndImmutabilityAreDatabaseGoverned()
    {
        var db = Db();
        var fixture = await CreateFixture(db);
        var invalidFirst = await Assert.ThrowsAsync<PostgresException>(() => Insert(
            db, fixture, "Changed", "Assigned", "Priority", "AlwaysOn", 60, -2));
        Assert.Equal("ck_stage126_support_first_event", invalidFirst.ConstraintName);
        var assigned = await Insert(db, fixture, "Assigned", "Assigned", "Priority", "AlwaysOn", 60, -2);
        var noChange = await Assert.ThrowsAsync<PostgresException>(() => Insert(
            db, fixture, "Changed", "Assigned", "Priority", "AlwaysOn", 60, -1));
        Assert.Equal("ck_stage126_support_material_change", noChange.ConstraintName);
        var immutable = await Assert.ThrowsAsync<PostgresException>(() => db.ExecuteAsync(
            "UPDATE device_support_tier_events SET action_reason='Changed after recording' WHERE id=@id",
            command => command.Parameters.AddWithValue("@id", assigned)));
        Assert.Equal("ck_stage126_support_immutable", immutable.ConstraintName);
    }

    [Fact]
    public async Task TerminalDeviceCannotReceiveAnActiveRoutingPlan()
    {
        var db = Db();
        var fixture = await CreateFixture(db, terminal: true);
        var rejected = await Assert.ThrowsAsync<PostgresException>(() => Insert(
            db, fixture, "Assigned", "Assigned", "Standard", "BusinessHours", 240, -1));
        Assert.Equal("ck_stage126_support_device_terminal", rejected.ConstraintName);
    }

    [Fact]
    public async Task RuntimeRolesCanReadButOnlySystemCanAppend()
    {
        var db = Db();
        Assert.Equal(1, await Has(db, "opstrax_app", "SELECT"));
        Assert.Equal(0, await Has(db, "opstrax_app", "INSERT,UPDATE,DELETE"));
        Assert.Equal(1, await Has(db, "opstrax_system", "SELECT,INSERT"));
        Assert.Equal(0, await Has(db, "opstrax_system", "UPDATE,DELETE"));
    }

    private static Task<long> Has(Database db, string role, string privilege) => db.ScalarLongAsync(
        "SELECT CASE WHEN has_table_privilege(@role,'device_support_tier_events',@privilege) THEN 1 ELSE 0 END",
        command => { command.Parameters.AddWithValue("@role", role); command.Parameters.AddWithValue("@privilege", privilege); });

    private static async Task<Fixture> CreateFixture(Database db, bool terminal = false)
    {
        var suffix = Guid.NewGuid().ToString("N");
        var company = await db.InsertAsync(
            "INSERT INTO companies(company_code,name,industry) VALUES(@code,'Support tier test','Transportation')",
            command => command.Parameters.AddWithValue("@code", $"ST-{suffix[..12]}"));
        var branch = await db.InsertAsync(
            "INSERT INTO branches(company_id,branch_code,name) VALUES(@company,@code,'Support branch')",
            command => { command.Parameters.AddWithValue("@company", company); command.Parameters.AddWithValue("@code", $"STB-{suffix}"); });
        var actor = await db.InsertAsync(
            "INSERT INTO users(company_id,branch_id,full_name,email,role_name,status) VALUES(@company,@branch,'Support planner',@email,'Fleet Manager','Active')",
            command => { command.Parameters.AddWithValue("@company", company); command.Parameters.AddWithValue("@branch", branch); command.Parameters.AddWithValue("@email", $"support-tier-{suffix}@example.test"); });
        var device = await db.InsertAsync(
            @"INSERT INTO eld_devices(company_id,branch_id,device_serial,status,device_state)
              VALUES(@company,@branch,@serial,@status,@state)",
            command =>
            {
                command.Parameters.AddWithValue("@company", company); command.Parameters.AddWithValue("@branch", branch);
                command.Parameters.AddWithValue("@serial", $"SUPPORT-{suffix}");
                command.Parameters.AddWithValue("@status", terminal ? "Retired" : "Active");
                command.Parameters.AddWithValue("@state", terminal ? "Retired" : "Online");
            });
        return new(company, branch, actor, device);
    }

    private static Task<long> Insert(Database db, Fixture f, string action, string state, string tier,
        string coverage, int target, int minutes, string escalation = "ESC-STANDARD", string commercial = "ORDER-STANDARD") =>
        db.InsertAsync(
            @"INSERT INTO device_support_tier_events(company_id,branch_id,device_id,device_serial_snapshot,
                action_type,state_after,tier_code,coverage_window,routing_response_target_minutes,
                escalation_policy_reference,commercial_reference,action_reason,source_reference,
                effective_at,idempotency_key,recorded_by)
              SELECT @company,@branch,@device,device_serial,@action,@state,@tier,@coverage,@target,
                     @escalation,@commercial,'Explicit support routing transition','OPS-126',
                     NOW()+(@minutes||' minutes')::INTERVAL,@key,@actor
                FROM eld_devices WHERE company_id=@company AND id=@device",
            command =>
            {
                command.Parameters.AddWithValue("@company", f.CompanyId); command.Parameters.AddWithValue("@branch", f.BranchId);
                command.Parameters.AddWithValue("@device", f.DeviceId); command.Parameters.AddWithValue("@action", action);
                command.Parameters.AddWithValue("@state", state); command.Parameters.AddWithValue("@tier", tier);
                command.Parameters.AddWithValue("@coverage", coverage); command.Parameters.AddWithValue("@target", target);
                command.Parameters.AddWithValue("@escalation", escalation); command.Parameters.AddWithValue("@commercial", commercial);
                command.Parameters.AddWithValue("@minutes", minutes); command.Parameters.AddWithValue("@key", Guid.NewGuid());
                command.Parameters.AddWithValue("@actor", f.ActorId);
            });

    private sealed record Fixture(long CompanyId, long BranchId, long ActorId, long DeviceId);

    private static Database Db() => new(new ConfigurationBuilder().AddInMemoryCollection(
        new Dictionary<string, string?>
        {
            ["ConnectionStrings:DefaultConnection"] = GuardedConnection(),
            ["ConnectionStrings:SystemConnection"] = GuardedConnection(),
            ["Rls:EnforceTenantContext"] = "false",
        }).Build());

    private static string GuardedConnection()
    {
        var configured = Environment.GetEnvironmentVariable("OPSTRAX_DEVICEOPS_STAGE126_DB");
        if (string.IsNullOrWhiteSpace(configured))
            throw SkipException.ForSkip("Set OPSTRAX_DEVICEOPS_STAGE126_DB to the dedicated disposable support-tier database.");
        var builder = new NpgsqlConnectionStringBuilder(configured);
        if (builder.Host is not ("127.0.0.1" or "::1") || builder.Port != 55452 ||
            builder.Database != "opstrax_deviceops_stage126")
            throw new InvalidOperationException("Support-tier tests require the dedicated local database on port 55452.");
        return builder.ConnectionString;
    }
}
