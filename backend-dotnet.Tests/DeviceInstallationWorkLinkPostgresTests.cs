using Microsoft.Extensions.Configuration;
using Npgsql;
using Opstrax.Api.Data;
using Xunit.Sdk;

namespace Opstrax.Tests;

[Trait("Category", "DeviceInstallationWorkLinkPostgres")]
[Trait("Lane", "DedicatedDatabase")]
public sealed class DeviceInstallationWorkLinkPostgresTests
{
    private static readonly string[] RequiredGpsChecklist =
    [
        "DeviceIdentity", "VehicleIdentity", "Mounting", "PrimaryPower", "Ground", "Ignition",
        "Harness", "GNSSAntenna", "CellularAntenna"
    ];

    [Fact]
    public async Task CompleteExactWorkPackageCanBeLinkedWithoutPromotingPhysicalOrCertificationClaims()
    {
        var db = Db();
        var fixture = await CreateFixture(db);
        var workId = await InsertWork(db, fixture);
        await InsertRequiredChecklist(db, fixture, workId);
        await InsertArtifact(db, fixture, workId);
        var installationId = await InsertInstallation(db, fixture);

        var linkId = await InsertLink(db, fixture, workId, installationId);
        var stored = await db.QuerySingleAsync(
            @"SELECT link_assurance_status,physical_work_claim,certification_claim,linked_by
                FROM device_installation_work_package_links WHERE id=@id",
            command => command.Parameters.AddWithValue("@id", linkId));

        Assert.NotNull(stored);
        Assert.Equal("RecordedUnverified", stored!["linkAssuranceStatus"]);
        Assert.Equal(false, stored["physicalWorkClaim"]);
        Assert.Equal(false, stored["certificationClaim"]);
        Assert.Equal(fixture.UserId, Convert.ToInt64(stored["linkedBy"]));

        foreach (var statement in new[]
        {
            "UPDATE device_installation_work_package_links SET linked_at=NOW() WHERE id=@id",
            "DELETE FROM device_installation_work_package_links WHERE id=@id"
        })
        {
            var immutable = await Assert.ThrowsAsync<PostgresException>(() => db.ExecuteAsync(statement,
                command => command.Parameters.AddWithValue("@id", linkId)));
            Assert.Equal("ck_stage122_work_link_immutable", immutable.ConstraintName);
        }
    }

    [Fact]
    public async Task LinkRejectsIncompleteEvidenceAndLatestFailure()
    {
        var db = Db();
        var incomplete = await CreateFixture(db);
        var incompleteWork = await InsertWork(db, incomplete);
        await InsertArtifact(db, incomplete, incompleteWork);
        var incompleteInstallation = await InsertInstallation(db, incomplete);

        var missingChecklist = await Assert.ThrowsAsync<PostgresException>(() =>
            InsertLink(db, incomplete, incompleteWork, incompleteInstallation));
        Assert.Equal("ck_stage122_installation_link_checklist", missingChecklist.ConstraintName);

        var missingArtifact = await CreateFixture(db);
        var missingArtifactWork = await InsertWork(db, missingArtifact);
        await InsertRequiredChecklist(db, missingArtifact, missingArtifactWork);
        var missingArtifactInstallation = await InsertInstallation(db, missingArtifact);
        var noArtifact = await Assert.ThrowsAsync<PostgresException>(() =>
            InsertLink(db, missingArtifact, missingArtifactWork, missingArtifactInstallation));
        Assert.Equal("ck_stage122_installation_link_artifact", noArtifact.ConstraintName);

        var failed = await CreateFixture(db);
        var failedWork = await InsertWork(db, failed);
        await InsertRequiredChecklist(db, failed, failedWork);
        await InsertChecklist(db, failed, failedWork, "Ground", "Fail", DateTimeOffset.UtcNow.AddMinutes(1));
        await InsertArtifact(db, failed, failedWork);
        var failedInstallation = await InsertInstallation(db, failed);
        var latestFailure = await Assert.ThrowsAsync<PostgresException>(() =>
            InsertLink(db, failed, failedWork, failedInstallation));
        Assert.Equal("ck_stage122_installation_link_checklist", latestFailure.ConstraintName);
    }

    [Fact]
    public async Task LinkRejectsWrongInstallerAndMismatchedVehicle()
    {
        var db = Db();
        var fixture = await CreateFixture(db);
        var workId = await InsertWork(db, fixture);
        await InsertRequiredChecklist(db, fixture, workId);
        await InsertArtifact(db, fixture, workId);
        var installationId = await InsertInstallation(db, fixture);
        var otherUser = await db.InsertAsync(
            @"INSERT INTO users(company_id,branch_id,full_name,email,role_name,status)
              VALUES(@company,@branch,'Other installer',@email,'Fleet Manager','Active')",
            command =>
            {
                command.Parameters.AddWithValue("@company", fixture.CompanyId);
                command.Parameters.AddWithValue("@branch", fixture.BranchId);
                command.Parameters.AddWithValue("@email", $"other-link-installer-{Guid.NewGuid():N}@example.test");
            });

        var wrongActor = await Assert.ThrowsAsync<PostgresException>(() =>
            InsertLink(db, fixture with { UserId = otherUser }, workId, installationId));
        Assert.Equal("ck_stage122_installation_link_actor", wrongActor.ConstraintName);

        var mismatch = await CreateFixture(db);
        var mismatchWork = await InsertWork(db, mismatch);
        await InsertRequiredChecklist(db, mismatch, mismatchWork);
        await InsertArtifact(db, mismatch, mismatchWork);
        var otherVehicle = await db.InsertAsync(
            @"INSERT INTO vehicles(company_id,branch_id,vehicle_code,type,make,model,year,status)
              VALUES(@company,@branch,@code,'Truck','Test','Truck',2026,'Active')",
            command =>
            {
                command.Parameters.AddWithValue("@company", mismatch.CompanyId);
                command.Parameters.AddWithValue("@branch", mismatch.BranchId);
                command.Parameters.AddWithValue("@code", $"OTHER-{Guid.NewGuid():N}");
            });
        var mismatchedInstallation = await InsertInstallation(db, mismatch with { VehicleId = otherVehicle });
        var wrongVehicle = await Assert.ThrowsAsync<PostgresException>(() =>
            InsertLink(db, mismatch, mismatchWork, mismatchedInstallation));
        Assert.Equal("ck_stage122_installation_link_scope", wrongVehicle.ConstraintName);
    }

    [Fact]
    public async Task RuntimeRolesCanOnlySelectAndInsertLinks()
    {
        var db = Db();
        foreach (var role in new[] { "opstrax_app", "opstrax_system" })
        {
            Assert.Equal(1, await db.ScalarLongAsync(
                "SELECT CASE WHEN has_table_privilege(@role,'device_installation_work_package_links','SELECT,INSERT') THEN 1 ELSE 0 END",
                command => command.Parameters.AddWithValue("@role", role)));
            foreach (var privilege in new[] { "UPDATE", "DELETE", "TRUNCATE" })
                Assert.Equal(0, await db.ScalarLongAsync(
                    "SELECT CASE WHEN has_table_privilege(@role,'device_installation_work_package_links',@privilege) THEN 1 ELSE 0 END",
                    command =>
                    {
                        command.Parameters.AddWithValue("@role", role);
                        command.Parameters.AddWithValue("@privilege", privilege);
                    }));
        }
    }

    private static async Task<Fixture> CreateFixture(Database db)
    {
        var suffix = Guid.NewGuid().ToString("N");
        var company = await db.InsertAsync(
            "INSERT INTO companies(company_code,name,industry) VALUES(@code,'Installation link test','Transportation')",
            command => command.Parameters.AddWithValue("@code", $"ILT-{suffix[..10]}"));
        var branch = await db.InsertAsync(
            "INSERT INTO branches(company_id,branch_code,name) VALUES(@company,@code,'Test branch')",
            command => { command.Parameters.AddWithValue("@company", company); command.Parameters.AddWithValue("@code", $"B1-{suffix}"); });
        var user = await db.InsertAsync(
            @"INSERT INTO users(company_id,branch_id,full_name,email,role_name,status)
              VALUES(@company,@branch,'Assigned installer',@email,'Fleet Manager','Active')",
            command =>
            {
                command.Parameters.AddWithValue("@company", company); command.Parameters.AddWithValue("@branch", branch);
                command.Parameters.AddWithValue("@email", $"link-installer-{suffix}@example.test");
            });
        var vehicle = await db.InsertAsync(
            @"INSERT INTO vehicles(company_id,branch_id,vehicle_code,type,make,model,year,status)
              VALUES(@company,@branch,@code,'Truck','Test','Truck',2026,'Active')",
            command =>
            {
                command.Parameters.AddWithValue("@company", company); command.Parameters.AddWithValue("@branch", branch);
                command.Parameters.AddWithValue("@code", $"V1-{suffix}");
            });
        var device = await db.InsertAsync(
            @"INSERT INTO eld_devices(company_id,branch_id,device_serial,status,device_state,device_category)
              VALUES(@company,@branch,@serial,'Provisioning','Registered','GPS Telematics')",
            command =>
            {
                command.Parameters.AddWithValue("@company", company); command.Parameters.AddWithValue("@branch", branch);
                command.Parameters.AddWithValue("@serial", $"DEV1-{suffix}");
            });
        return new Fixture(company, branch, user, vehicle, device);
    }

    private static Task<long> InsertWork(Database db, Fixture fixture) => db.InsertAsync(
        @"INSERT INTO device_installation_work_packages
            (company_id,branch_id,device_id,vehicle_id,assigned_installer_user_id,work_order_reference,
             appointment_start,appointment_end,service_location,work_scope,idempotency_key,created_by)
          VALUES(@company,@branch,@device,@vehicle,@user,@reference,NOW()+INTERVAL '1 day',NOW()+INTERVAL '2 days',
                 'Test depot','Install exact test device',@key,@user)",
        command =>
        {
            Bind(command, fixture); command.Parameters.AddWithValue("@reference", $"WO-{Guid.NewGuid():N}");
            command.Parameters.AddWithValue("@key", Guid.NewGuid().ToString());
        });

    private static async Task InsertRequiredChecklist(Database db, Fixture fixture, long workId)
    {
        foreach (var item in RequiredGpsChecklist)
            await InsertChecklist(db, fixture, workId, item, "Pass", DateTimeOffset.UtcNow);
    }

    private static Task<long> InsertChecklist(
        Database db, Fixture fixture, long workId, string item, string result, DateTimeOffset observedAt) => db.InsertAsync(
        @"INSERT INTO device_installation_checklist_observations
            (company_id,branch_id,device_id,work_package_id,checklist_item,observed_result,
             evidence_reference,observation_notes,observed_at,idempotency_key,recorded_by)
          VALUES(@company,@branch,@device,@work,@item,@result,'operator-reference',
                 'Operator-recorded test observation',@observed,@key,@user)",
        command =>
        {
            Bind(command, fixture); command.Parameters.AddWithValue("@work", workId);
            command.Parameters.AddWithValue("@item", item); command.Parameters.AddWithValue("@result", result);
            command.Parameters.AddWithValue("@observed", observedAt); command.Parameters.AddWithValue("@key", Guid.NewGuid().ToString());
        });

    private static Task<long> InsertArtifact(Database db, Fixture fixture, long workId) => db.InsertAsync(
        @"INSERT INTO device_installation_artifact_references
            (company_id,branch_id,device_id,work_package_id,artifact_type,object_key,sha256,
             captured_at,idempotency_key,recorded_by)
          VALUES(@company,@branch,@device,@work,'WiringPhoto','installations/work/link-photo.jpg',
                 @sha,NOW(),@key,@user)",
        command =>
        {
            Bind(command, fixture); command.Parameters.AddWithValue("@work", workId);
            command.Parameters.AddWithValue("@sha", Guid.NewGuid().ToString("N") + Guid.NewGuid().ToString("N"));
            command.Parameters.AddWithValue("@key", Guid.NewGuid().ToString());
        });

    private static Task<long> InsertInstallation(Database db, Fixture fixture) => db.InsertAsync(
        @"INSERT INTO device_installations(company_id,branch_id,device_id,vehicle_id,status)
          VALUES(@company,@branch,@device,@vehicle,'Installed')",
        command => Bind(command, fixture));

    private static Task<long> InsertLink(Database db, Fixture fixture, long workId, long installationId) => db.InsertAsync(
        @"INSERT INTO device_installation_work_package_links
            (company_id,branch_id,device_id,work_package_id,installation_id,idempotency_key,linked_by)
          VALUES(@company,@branch,@device,@work,@installation,@key,@user)",
        command =>
        {
            Bind(command, fixture); command.Parameters.AddWithValue("@work", workId);
            command.Parameters.AddWithValue("@installation", installationId);
            command.Parameters.AddWithValue("@key", Guid.NewGuid().ToString());
        });

    private static void Bind(NpgsqlCommand command, Fixture fixture)
    {
        command.Parameters.AddWithValue("@company", fixture.CompanyId);
        command.Parameters.AddWithValue("@branch", fixture.BranchId);
        command.Parameters.AddWithValue("@device", fixture.DeviceId);
        command.Parameters.AddWithValue("@vehicle", fixture.VehicleId);
        command.Parameters.AddWithValue("@user", fixture.UserId);
    }

    private sealed record Fixture(long CompanyId, long BranchId, long UserId, long VehicleId, long DeviceId);

    private static Database Db() => new(new ConfigurationBuilder().AddInMemoryCollection(
        new Dictionary<string, string?>
        {
            ["ConnectionStrings:DefaultConnection"] = GuardedConnection(),
            ["ConnectionStrings:SystemConnection"] = GuardedConnection(),
            ["Rls:EnforceTenantContext"] = "false",
        }).Build());

    private static string GuardedConnection()
    {
        var configured = Environment.GetEnvironmentVariable("OPSTRAX_DEVICEOPS_STAGE122_DB");
        if (string.IsNullOrWhiteSpace(configured))
            throw SkipException.ForSkip("Set OPSTRAX_DEVICEOPS_STAGE122_DB to the dedicated disposable installation-link database.");
        var builder = new NpgsqlConnectionStringBuilder(configured);
        if (builder.Host is not ("127.0.0.1" or "::1") || builder.Port != 55447 ||
            builder.Database != "opstrax_deviceops_stage122")
            throw new InvalidOperationException("Installation-link tests require the dedicated local database on port 55447.");
        return builder.ConnectionString;
    }
}
