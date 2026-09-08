using Microsoft.Extensions.Configuration;
using Npgsql;
using Opstrax.Api.Data;
using Xunit.Sdk;

namespace Opstrax.Tests;

[Trait("Category", "DeviceRmaSupportPostgres")]
public sealed class DeviceRmaSupportPostgresTests
{
    [Fact]
    public async Task OwnershipCanTransferAndEscalationPreservesCurrentOwner()
    {
        var db = Db();
        var fixture = await CreateFixture(db);
        var claimed = await InsertAction(db, fixture, fixture.FirstUserId, fixture.FirstUserId,
            "OwnershipClaimed", null, DateTimeOffset.UtcNow.AddMinutes(-3));
        var reassigned = await InsertAction(db, fixture, fixture.SecondUserId, fixture.SecondUserId,
            "OwnershipReassigned", null, DateTimeOffset.UtcNow.AddMinutes(-2));
        var escalated = await InsertAction(db, fixture, fixture.SecondUserId, fixture.FirstUserId,
            "Escalated", "P1", DateTimeOffset.UtcNow.AddMinutes(-1));

        Assert.True(claimed > 0 && reassigned > claimed && escalated > reassigned);
        var latest = await db.QuerySingleAsync(
            "SELECT owner_user_id,escalation_severity,support_action_status,support_response_claim,physical_outcome_claim,warranty_acceptance_claim FROM device_rma_support_actions WHERE id=@id",
            command => command.Parameters.AddWithValue("@id", escalated));
        Assert.Equal(fixture.SecondUserId, Convert.ToInt64(latest!["ownerUserId"]));
        Assert.Equal("P1", latest["escalationSeverity"]);
        Assert.Equal("OperatorRecorded", latest["supportActionStatus"]);
        Assert.Equal(false, latest["supportResponseClaim"]);
        Assert.Equal(false, latest["physicalOutcomeClaim"]);
        Assert.Equal(false, latest["warrantyAcceptanceClaim"]);
    }

    [Fact]
    public async Task FirstActionMustBeSelfClaimAndHistoryIsImmutable()
    {
        var db = Db();
        var fixture = await CreateFixture(db);
        var invalid = await Assert.ThrowsAsync<PostgresException>(() => InsertAction(db, fixture,
            fixture.SecondUserId, fixture.FirstUserId, "OwnershipReassigned", null, DateTimeOffset.UtcNow));
        Assert.Equal("ck_stage124_first_owner", invalid.ConstraintName);

        var actionId = await InsertAction(db, fixture, fixture.FirstUserId, fixture.FirstUserId,
            "OwnershipClaimed", null, DateTimeOffset.UtcNow);
        var immutable = await Assert.ThrowsAsync<PostgresException>(() => db.ExecuteAsync(
            "UPDATE device_rma_support_actions SET support_queue='Changed queue' WHERE id=@id",
            command => command.Parameters.AddWithValue("@id", actionId)));
        Assert.Equal("ck_stage124_support_action_immutable", immutable.ConstraintName);
    }

    [Fact]
    public async Task ResolvedCaseRejectsRoutingAndRuntimeRolesCannotForgeIt()
    {
        var db = Db();
        var fixture = await CreateFixture(db, resolved: true);
        var rejected = await Assert.ThrowsAsync<PostgresException>(() => InsertAction(db, fixture,
            fixture.FirstUserId, fixture.FirstUserId, "OwnershipClaimed", null, DateTimeOffset.UtcNow));
        Assert.Equal("ck_stage124_case_open", rejected.ConstraintName);

        Assert.Equal(1, await Has(db, "opstrax_app", "SELECT"));
        Assert.Equal(0, await Has(db, "opstrax_app", "INSERT,UPDATE,DELETE"));
        Assert.Equal(1, await Has(db, "opstrax_system", "SELECT,INSERT"));
        Assert.Equal(0, await Has(db, "opstrax_system", "UPDATE,DELETE"));
    }

    private static Task<long> Has(Database db, string role, string privilege) => db.ScalarLongAsync(
        "SELECT CASE WHEN has_table_privilege(@role,'device_rma_support_actions',@privilege) THEN 1 ELSE 0 END",
        command => { command.Parameters.AddWithValue("@role", role); command.Parameters.AddWithValue("@privilege", privilege); });

    private static async Task<Fixture> CreateFixture(Database db, bool resolved = false)
    {
        var suffix = Guid.NewGuid().ToString("N");
        var company = await db.InsertAsync(
            "INSERT INTO companies(company_code,name,industry) VALUES(@code,'RMA support test','Transportation')",
            command => command.Parameters.AddWithValue("@code", $"RS-{suffix[..12]}"));
        var branch = await db.InsertAsync(
            "INSERT INTO branches(company_id,branch_code,name) VALUES(@company,@code,'Support branch')",
            command => { command.Parameters.AddWithValue("@company", company); command.Parameters.AddWithValue("@code", $"RSB-{suffix}"); });
        async Task<long> User(string name) => await db.InsertAsync(
            "INSERT INTO users(company_id,branch_id,full_name,email,role_name,status) VALUES(@company,@branch,@name,@email,'Fleet Manager','Active')",
            command => { command.Parameters.AddWithValue("@company", company); command.Parameters.AddWithValue("@branch", branch); command.Parameters.AddWithValue("@name", name); command.Parameters.AddWithValue("@email", $"{name.Replace(" ", "").ToLowerInvariant()}-{suffix}@example.test"); });
        var first = await User("First support owner");
        var second = await User("Second support owner");
        var serial = $"RMA-SUPPORT-{suffix}";
        var device = await db.InsertAsync(
            "INSERT INTO eld_devices(company_id,branch_id,device_serial,status,device_state) VALUES(@company,@branch,@serial,'Active','Online')",
            command => { command.Parameters.AddWithValue("@company", company); command.Parameters.AddWithValue("@branch", branch); command.Parameters.AddWithValue("@serial", serial); });
        var caseId = await db.InsertAsync(
            @"INSERT INTO device_rma_cases(company_id,branch_id,device_id,device_serial,severity,failure_category,
                failure_description,observed_at,warranty_posture,support_sla_reference,response_due_at,
                source_reference,idempotency_key,created_by)
              VALUES(@company,@branch,@device,@serial,'P1','Connectivity','Repeated connectivity loss in field',
                NOW()-INTERVAL '1 hour','Unknown','SLA-4H',NOW()+INTERVAL '3 hours','SUP-124',@key,@user)",
            command => { command.Parameters.AddWithValue("@company", company); command.Parameters.AddWithValue("@branch", branch); command.Parameters.AddWithValue("@device", device); command.Parameters.AddWithValue("@serial", serial); command.Parameters.AddWithValue("@key", Guid.NewGuid()); command.Parameters.AddWithValue("@user", first); });
        if (resolved)
            await db.InsertAsync(
                @"INSERT INTO device_rma_events(company_id,branch_id,case_id,device_id,sequence_number,event_type,
                    case_status_after,occurred_at,evidence_reference,notes,idempotency_key,recorded_by)
                  VALUES(@company,@branch,@case,@device,1,'CaseClosed','Resolved',NOW(),'SUP-124','Case closed by operator',@key,@user)",
                command => { command.Parameters.AddWithValue("@company", company); command.Parameters.AddWithValue("@branch", branch); command.Parameters.AddWithValue("@case", caseId); command.Parameters.AddWithValue("@device", device); command.Parameters.AddWithValue("@key", Guid.NewGuid()); command.Parameters.AddWithValue("@user", first); });
        return new(company, branch, device, caseId, first, second);
    }

    private static Task<long> InsertAction(Database db, Fixture fixture, long owner, long actor,
        string actionType, string? severity, DateTimeOffset effectiveAt) => db.InsertAsync(
        @"INSERT INTO device_rma_support_actions(company_id,branch_id,case_id,device_id,action_type,
            owner_user_id,owner_name_snapshot,support_queue,escalation_severity,action_reason,
            source_reference,effective_at,idempotency_key,recorded_by)
          SELECT @company,@branch,@case,@device,@action,@owner,u.full_name,'Device Support',@severity,
                 'Route accountable support work','SUP-124',@effectiveAt,@key,@actor
            FROM users u WHERE u.company_id=@company AND u.id=@owner",
        command =>
        {
            command.Parameters.AddWithValue("@company", fixture.CompanyId); command.Parameters.AddWithValue("@branch", fixture.BranchId);
            command.Parameters.AddWithValue("@case", fixture.CaseId); command.Parameters.AddWithValue("@device", fixture.DeviceId);
            command.Parameters.AddWithValue("@action", actionType); command.Parameters.AddWithValue("@owner", owner);
            command.Parameters.AddWithValue("@severity", (object?)severity ?? DBNull.Value); command.Parameters.AddWithValue("@effectiveAt", effectiveAt);
            command.Parameters.AddWithValue("@key", Guid.NewGuid()); command.Parameters.AddWithValue("@actor", actor);
        });

    private sealed record Fixture(long CompanyId, long BranchId, long DeviceId, long CaseId, long FirstUserId, long SecondUserId);

    private static Database Db() => new(new ConfigurationBuilder().AddInMemoryCollection(
        new Dictionary<string, string?>
        {
            ["ConnectionStrings:DefaultConnection"] = GuardedConnection(),
            ["ConnectionStrings:SystemConnection"] = GuardedConnection(),
            ["Rls:EnforceTenantContext"] = "false",
        }).Build());

    private static string GuardedConnection()
    {
        var configured = Environment.GetEnvironmentVariable("OPSTRAX_DEVICEOPS_STAGE124_DB");
        if (string.IsNullOrWhiteSpace(configured))
            throw SkipException.ForSkip("Set OPSTRAX_DEVICEOPS_STAGE124_DB to the dedicated disposable RMA support database.");
        var builder = new NpgsqlConnectionStringBuilder(configured);
        if (builder.Host is not ("127.0.0.1" or "::1") || builder.Port != 55449 ||
            builder.Database != "opstrax_deviceops_stage124")
            throw new InvalidOperationException("RMA support tests require the dedicated local database on port 55449.");
        return builder.ConnectionString;
    }
}
