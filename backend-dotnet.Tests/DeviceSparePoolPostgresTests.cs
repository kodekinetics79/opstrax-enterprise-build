using Microsoft.Extensions.Configuration;
using Npgsql;
using Opstrax.Api.Data;
using Xunit.Sdk;

namespace Opstrax.Tests;

[Trait("Category", "DeviceSparePoolPostgres")]
[Trait("Lane", "DedicatedDatabase")]
public sealed class DeviceSparePoolPostgresTests
{
    [Fact]
    public async Task ExactSpareCanBeReservedReleasedAndRemovedWithoutPromotedClaims()
    {
        var db = Db();
        var fixture = await CreateFixture(db);
        var entry = await AddEntry(db, fixture, fixture.SpareDeviceId);
        await AddEvent(db, fixture, entry, fixture.SpareDeviceId, "Added", "Available", null, null, -4);
        await AddEvent(db, fixture, entry, fixture.SpareDeviceId, "Reserved", "Reserved",
            fixture.CaseId, fixture.FailedDeviceId, -3);
        await AddEvent(db, fixture, entry, fixture.SpareDeviceId, "Released", "Available",
            fixture.CaseId, fixture.FailedDeviceId, -2);
        var removed = await AddEvent(db, fixture, entry, fixture.SpareDeviceId, "Removed", "Removed", null, null, -1);

        var row = await db.QuerySingleAsync(
            @"SELECT state_after,event_status,physical_possession_claim,condition_verified_claim,
                     compatibility_claim,certification_claim
                FROM device_spare_pool_events WHERE id=@id",
            command => command.Parameters.AddWithValue("@id", removed));
        Assert.Equal("Removed", row!["stateAfter"]);
        Assert.Equal("OperatorRecorded", row["eventStatus"]);
        Assert.Equal(false, row["physicalPossessionClaim"]);
        Assert.Equal(false, row["conditionVerifiedClaim"]);
        Assert.Equal(false, row["compatibilityClaim"]);
        Assert.Equal(false, row["certificationClaim"]);
    }

    [Fact]
    public async Task FirstEventTransitionsAndHistoryAreDatabaseGoverned()
    {
        var db = Db();
        var fixture = await CreateFixture(db);
        var entry = await AddEntry(db, fixture, fixture.SpareDeviceId);
        var invalidFirst = await Assert.ThrowsAsync<PostgresException>(() => AddEvent(db, fixture,
            entry, fixture.SpareDeviceId, "Removed", "Removed", null, null, -2));
        Assert.Equal("ck_stage125_pool_first_event", invalidFirst.ConstraintName);

        var added = await AddEvent(db, fixture, entry, fixture.SpareDeviceId,
            "Added", "Available", null, null, -2);
        var invalidTransition = await Assert.ThrowsAsync<PostgresException>(() => AddEvent(db, fixture,
            entry, fixture.SpareDeviceId, "Released", "Available", fixture.CaseId, fixture.FailedDeviceId, -1));
        Assert.Equal("ck_stage125_pool_transition", invalidTransition.ConstraintName);
        var immutable = await Assert.ThrowsAsync<PostgresException>(() => db.ExecuteAsync(
            "UPDATE device_spare_pool_events SET action_reason='Changed after recording' WHERE id=@id",
            command => command.Parameters.AddWithValue("@id", added)));
        Assert.Equal("ck_stage125_pool_event_immutable", immutable.ConstraintName);
    }

    [Fact]
    public async Task OneOpenRmaCaseCannotReserveTwoSpares()
    {
        var db = Db();
        var fixture = await CreateFixture(db, twoSpares: true);
        var firstEntry = await AddEntry(db, fixture, fixture.SpareDeviceId);
        await AddEvent(db, fixture, firstEntry, fixture.SpareDeviceId, "Added", "Available", null, null, -4);
        await AddEvent(db, fixture, firstEntry, fixture.SpareDeviceId, "Reserved", "Reserved",
            fixture.CaseId, fixture.FailedDeviceId, -3);

        var secondEntry = await AddEntry(db, fixture, fixture.SecondSpareDeviceId!.Value);
        await AddEvent(db, fixture, secondEntry, fixture.SecondSpareDeviceId.Value,
            "Added", "Available", null, null, -2);
        var duplicate = await Assert.ThrowsAsync<PostgresException>(() => AddEvent(db, fixture,
            secondEntry, fixture.SecondSpareDeviceId.Value, "Reserved", "Reserved",
            fixture.CaseId, fixture.FailedDeviceId, -1));
        Assert.Equal("ck_stage125_pool_unique_active_case", duplicate.ConstraintName);
    }

    [Fact]
    public async Task InstalledDeviceCannotEnterAndActivePoolDeviceCannotRetire()
    {
        var db = Db();
        var fixture = await CreateFixture(db);
        await db.InsertAsync(
            @"INSERT INTO vehicles(company_id,branch_id,vehicle_code,type,vin,make,model,year,status)
              VALUES(@company,@branch,@number,'Truck',@vin,'Test','Vehicle',2026,'Available')",
            command =>
            {
                command.Parameters.AddWithValue("@company", fixture.CompanyId);
                command.Parameters.AddWithValue("@branch", fixture.BranchId);
                command.Parameters.AddWithValue("@number", $"V-{Guid.NewGuid():N}");
                command.Parameters.AddWithValue("@vin", Guid.NewGuid().ToString("N")[..17].ToUpperInvariant());
            });
        var vehicle = await db.ScalarLongAsync("SELECT MAX(id) FROM vehicles WHERE company_id=@company",
            command => command.Parameters.AddWithValue("@company", fixture.CompanyId));
        await db.InsertAsync(
            @"INSERT INTO device_installations(company_id,branch_id,device_id,vehicle_id,effective_from,status)
              VALUES(@company,@branch,@device,@vehicle,NOW(),'Installed')",
            command =>
            {
                command.Parameters.AddWithValue("@company", fixture.CompanyId);
                command.Parameters.AddWithValue("@branch", fixture.BranchId);
                command.Parameters.AddWithValue("@device", fixture.SpareDeviceId);
                command.Parameters.AddWithValue("@vehicle", vehicle);
            });
        var installed = await Assert.ThrowsAsync<PostgresException>(() => AddEntry(db, fixture, fixture.SpareDeviceId));
        Assert.Equal("ck_stage125_pool_no_current_installation", installed.ConstraintName);

        await db.ExecuteAsync("UPDATE device_installations SET effective_to=NOW(),status='Removed' WHERE company_id=@company AND device_id=@device",
            command => { command.Parameters.AddWithValue("@company", fixture.CompanyId); command.Parameters.AddWithValue("@device", fixture.SpareDeviceId); });
        var entry = await AddEntry(db, fixture, fixture.SpareDeviceId);
        await AddEvent(db, fixture, entry, fixture.SpareDeviceId, "Added", "Available", null, null, -1);
        var terminal = await Assert.ThrowsAsync<PostgresException>(() => db.ExecuteAsync(
            "UPDATE eld_devices SET status='Retired',device_state='Retired' WHERE company_id=@company AND id=@device",
            command => { command.Parameters.AddWithValue("@company", fixture.CompanyId); command.Parameters.AddWithValue("@device", fixture.SpareDeviceId); }));
        Assert.Equal("ck_stage125_device_not_in_pool", terminal.ConstraintName);
    }

    [Fact]
    public async Task RuntimeRolesCanReadButOnlySystemCanAppend()
    {
        var db = Db();
        foreach (var table in new[] { "device_spare_pool_entries", "device_spare_pool_events" })
        {
            Assert.Equal(1, await Has(db, table, "opstrax_app", "SELECT"));
            Assert.Equal(0, await Has(db, table, "opstrax_app", "INSERT,UPDATE,DELETE"));
            Assert.Equal(1, await Has(db, table, "opstrax_system", "SELECT,INSERT"));
            Assert.Equal(0, await Has(db, table, "opstrax_system", "UPDATE,DELETE"));
        }
    }

    private static Task<long> Has(Database db, string table, string role, string privilege) => db.ScalarLongAsync(
        "SELECT CASE WHEN has_table_privilege(@role,@table,@privilege) THEN 1 ELSE 0 END",
        command =>
        {
            command.Parameters.AddWithValue("@role", role);
            command.Parameters.AddWithValue("@table", table);
            command.Parameters.AddWithValue("@privilege", privilege);
        });

    private static async Task<Fixture> CreateFixture(Database db, bool twoSpares = false)
    {
        var suffix = Guid.NewGuid().ToString("N");
        var company = await db.InsertAsync(
            "INSERT INTO companies(company_code,name,industry) VALUES(@code,'Spare pool test','Transportation')",
            command => command.Parameters.AddWithValue("@code", $"SP-{suffix[..12]}"));
        var branch = await db.InsertAsync(
            "INSERT INTO branches(company_id,branch_code,name) VALUES(@company,@code,'Pool branch')",
            command => { command.Parameters.AddWithValue("@company", company); command.Parameters.AddWithValue("@code", $"SPB-{suffix}"); });
        var actor = await db.InsertAsync(
            "INSERT INTO users(company_id,branch_id,full_name,email,role_name,status) VALUES(@company,@branch,'Pool operator',@email,'Fleet Manager','Active')",
            command => { command.Parameters.AddWithValue("@company", company); command.Parameters.AddWithValue("@branch", branch); command.Parameters.AddWithValue("@email", $"pool-{suffix}@example.test"); });
        async Task<long> Device(string label) => await db.InsertAsync(
            "INSERT INTO eld_devices(company_id,branch_id,device_serial,status,device_state) VALUES(@company,@branch,@serial,'Active','Online')",
            command => { command.Parameters.AddWithValue("@company", company); command.Parameters.AddWithValue("@branch", branch); command.Parameters.AddWithValue("@serial", $"{label}-{suffix}"); });
        var failed = await Device("FAILED");
        var spare = await Device("SPARE-A");
        var second = twoSpares ? await Device("SPARE-B") : (long?)null;
        var caseId = await db.InsertAsync(
            @"INSERT INTO device_rma_cases(company_id,branch_id,device_id,device_serial,severity,failure_category,
                failure_description,observed_at,warranty_posture,support_sla_reference,response_due_at,
                source_reference,idempotency_key,created_by)
              SELECT @company,@branch,@device,device_serial,'P1','Connectivity','Repeated connectivity loss in field',
                NOW()-INTERVAL '1 hour','Unknown','SLA-4H',NOW()+INTERVAL '3 hours','RMA-125',@key,@actor
                FROM eld_devices WHERE company_id=@company AND id=@device",
            command =>
            {
                command.Parameters.AddWithValue("@company", company); command.Parameters.AddWithValue("@branch", branch);
                command.Parameters.AddWithValue("@device", failed); command.Parameters.AddWithValue("@key", Guid.NewGuid());
                command.Parameters.AddWithValue("@actor", actor);
            });
        return new(company, branch, actor, failed, spare, second, caseId);
    }

    private static Task<long> AddEntry(Database db, Fixture f, long device) => db.InsertAsync(
        @"INSERT INTO device_spare_pool_entries(company_id,branch_id,device_id,device_serial_snapshot,pool_name,
            entry_reason,source_reference,idempotency_key,added_by)
          SELECT @company,@branch,@device,device_serial,'Toronto spares','Held for governed replacement planning',
                 'INV-125',@key,@actor FROM eld_devices WHERE company_id=@company AND id=@device",
        command =>
        {
            command.Parameters.AddWithValue("@company", f.CompanyId); command.Parameters.AddWithValue("@branch", f.BranchId);
            command.Parameters.AddWithValue("@device", device); command.Parameters.AddWithValue("@key", Guid.NewGuid());
            command.Parameters.AddWithValue("@actor", f.ActorId);
        });

    private static Task<long> AddEvent(Database db, Fixture f, long entry, long device, string action,
        string state, long? caseId, long? failedDeviceId, int minutes) => db.InsertAsync(
        @"INSERT INTO device_spare_pool_events(company_id,branch_id,entry_id,device_id,action_type,state_after,
            rma_case_id,failed_device_id,action_reason,source_reference,effective_at,idempotency_key,recorded_by)
          VALUES(@company,@branch,@entry,@device,@action,@state,@case,@failed,'Explicit governed pool transition',
                 'INV-125',NOW()+(@minutes||' minutes')::INTERVAL,@key,@actor)",
        command =>
        {
            command.Parameters.AddWithValue("@company", f.CompanyId); command.Parameters.AddWithValue("@branch", f.BranchId);
            command.Parameters.AddWithValue("@entry", entry); command.Parameters.AddWithValue("@device", device);
            command.Parameters.AddWithValue("@action", action); command.Parameters.AddWithValue("@state", state);
            command.Parameters.AddWithValue("@case", (object?)caseId ?? DBNull.Value);
            command.Parameters.AddWithValue("@failed", (object?)failedDeviceId ?? DBNull.Value);
            command.Parameters.AddWithValue("@minutes", minutes); command.Parameters.AddWithValue("@key", Guid.NewGuid());
            command.Parameters.AddWithValue("@actor", f.ActorId);
        });

    private sealed record Fixture(long CompanyId, long BranchId, long ActorId, long FailedDeviceId,
        long SpareDeviceId, long? SecondSpareDeviceId, long CaseId);

    private static Database Db() => new(new ConfigurationBuilder().AddInMemoryCollection(
        new Dictionary<string, string?>
        {
            ["ConnectionStrings:DefaultConnection"] = GuardedConnection(),
            ["ConnectionStrings:SystemConnection"] = GuardedConnection(),
            ["Rls:EnforceTenantContext"] = "false",
        }).Build());

    private static string GuardedConnection()
    {
        var configured = Environment.GetEnvironmentVariable("OPSTRAX_DEVICEOPS_STAGE125_DB");
        if (string.IsNullOrWhiteSpace(configured))
            throw SkipException.ForSkip("Set OPSTRAX_DEVICEOPS_STAGE125_DB to the dedicated disposable spare-pool database.");
        var builder = new NpgsqlConnectionStringBuilder(configured);
        if (builder.Host is not ("127.0.0.1" or "::1") || builder.Port != 55451 ||
            builder.Database != "opstrax_deviceops_stage125")
            throw new InvalidOperationException("Spare-pool tests require the dedicated local database on port 55451.");
        return builder.ConnectionString;
    }
}
