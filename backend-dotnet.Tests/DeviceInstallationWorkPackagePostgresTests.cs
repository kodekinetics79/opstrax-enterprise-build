using Microsoft.Extensions.Configuration;
using Npgsql;
using Opstrax.Api.Data;
using Xunit.Sdk;

namespace Opstrax.Tests;

[Trait("Category", "DeviceInstallationWorkPackagePostgres")]
[Trait("Lane", "DedicatedDatabase")]
public sealed class DeviceInstallationWorkPackagePostgresTests
{
    [Fact]
    public async Task AppRoleCanAppendAndReadButCannotRewriteEvidence()
    {
        var db = Db();
        foreach (var table in Tables)
        {
            Assert.Equal(1, await db.ScalarLongAsync(
                "SELECT CASE WHEN has_table_privilege('opstrax_app',@table,'SELECT,INSERT') THEN 1 ELSE 0 END",
                command => command.Parameters.AddWithValue("@table", table)));
            foreach (var privilege in new[] { "UPDATE", "DELETE", "TRUNCATE" })
                Assert.Equal(0, await db.ScalarLongAsync(
                    "SELECT CASE WHEN has_table_privilege('opstrax_app',@table,@privilege) THEN 1 ELSE 0 END",
                    command => { command.Parameters.AddWithValue("@table", table); command.Parameters.AddWithValue("@privilege", privilege); }));
        }
    }

    [Fact]
    public async Task ExactScopedPackageAcceptsUnverifiedEvidenceAndRejectsMutationAndClaims()
    {
        var db = Db();
        var fixture = await CreateFixture(db);
        var workId = await InsertWork(db, fixture, false);
        var checklistId = await db.InsertAsync(
            @"INSERT INTO device_installation_checklist_observations
                (company_id,branch_id,device_id,work_package_id,checklist_item,observed_result,
                 evidence_reference,observation_notes,observed_at,idempotency_key,recorded_by)
              VALUES(@company,@branch,@device,@work,'PrimaryPower','Pass','meter:12.6v',
                     'Operator recorded a 12.6 V reading',NOW(),@key,@user)",
            command =>
            {
                Bind(command, fixture); command.Parameters.AddWithValue("@work", workId);
                command.Parameters.AddWithValue("@key", Guid.NewGuid().ToString());
            });
        var artifactId = await db.InsertAsync(
            @"INSERT INTO device_installation_artifact_references
                (company_id,branch_id,device_id,work_package_id,artifact_type,object_key,sha256,
                 captured_at,idempotency_key,recorded_by)
              VALUES(@company,@branch,@device,@work,'WiringPhoto','installations/work/photo.jpg',
                     repeat('a',64),NOW(),@key,@user)",
            command =>
            {
                Bind(command, fixture); command.Parameters.AddWithValue("@work", workId);
                command.Parameters.AddWithValue("@key", Guid.NewGuid().ToString());
            });

        var stored = await db.QuerySingleAsync(
            @"SELECT w.physical_appointment_claim,w.physical_work_claim,w.certification_claim,
                     o.assurance_status,o.physical_evidence_claim checklist_physical,o.certification_claim checklist_certification,
                     a.content_verification_status,a.physical_evidence_claim artifact_physical,a.certification_claim artifact_certification
                FROM device_installation_work_packages w
                JOIN device_installation_checklist_observations o ON o.company_id=w.company_id AND o.work_package_id=w.id
                JOIN device_installation_artifact_references a ON a.company_id=w.company_id AND a.work_package_id=w.id
               WHERE w.id=@work",
            command => command.Parameters.AddWithValue("@work", workId));
        Assert.NotNull(stored);
        Assert.Equal(false, stored!["physicalAppointmentClaim"]);
        Assert.Equal(false, stored["physicalWorkClaim"]);
        Assert.Equal(false, stored["certificationClaim"]);
        Assert.Equal("Unverified", stored["assuranceStatus"]);
        Assert.Equal(false, stored["checklistPhysical"]);
        Assert.Equal(false, stored["checklistCertification"]);
        Assert.Equal("Unverified", stored["contentVerificationStatus"]);
        Assert.Equal(false, stored["artifactPhysical"]);
        Assert.Equal(false, stored["artifactCertification"]);

        foreach (var (statement, id) in new[]
        {
            ("UPDATE device_installation_work_packages SET work_scope='rewritten' WHERE id=@id", workId),
            ("DELETE FROM device_installation_checklist_observations WHERE id=@id", checklistId),
            ("UPDATE device_installation_artifact_references SET object_key='other' WHERE id=@id", artifactId),
        })
        {
            var immutable = await Assert.ThrowsAsync<PostgresException>(() => db.ExecuteAsync(statement,
                command => command.Parameters.AddWithValue("@id", id)));
            Assert.Contains(immutable.ConstraintName, new[] { "ck_stage121_work_package_immutable", "ck_stage121_work_evidence_immutable" });
        }

        var claim = await Assert.ThrowsAsync<PostgresException>(() => InsertWork(db, fixture, true));
        Assert.Equal("ck_stage121_no_appointment_claim", claim.ConstraintName);
    }

    [Fact]
    public async Task CrossBranchVehicleAndMismatchedEvidenceAreRejected()
    {
        var db = Db();
        var fixture = await CreateFixture(db);
        var otherBranch = await db.InsertAsync(
            "INSERT INTO branches(company_id,branch_code,name) VALUES(@company,@code,'Other branch')",
            command => { command.Parameters.AddWithValue("@company", fixture.CompanyId); command.Parameters.AddWithValue("@code", $"B2-{Guid.NewGuid():N}"); });
        var otherVehicle = await db.InsertAsync(
            @"INSERT INTO vehicles(company_id,branch_id,vehicle_code,type,make,model,year,status)
              VALUES(@company,@branch,@code,'Truck','Test','Truck',2026,'Active')",
            command =>
            {
                command.Parameters.AddWithValue("@company", fixture.CompanyId); command.Parameters.AddWithValue("@branch", otherBranch);
                command.Parameters.AddWithValue("@code", $"V2-{Guid.NewGuid():N}");
            });
        var crossBranch = await Assert.ThrowsAsync<PostgresException>(() => InsertWork(db, fixture with { VehicleId = otherVehicle }, false));
        Assert.Equal("ck_stage121_work_branch_scope", crossBranch.ConstraintName);

        var workId = await InsertWork(db, fixture, false);
        var otherDevice = await db.InsertAsync(
            "INSERT INTO eld_devices(company_id,branch_id,device_serial,status,device_state) VALUES(@company,@branch,@serial,'Provisioning','Registered')",
            command =>
            {
                command.Parameters.AddWithValue("@company", fixture.CompanyId); command.Parameters.AddWithValue("@branch", fixture.BranchId);
                command.Parameters.AddWithValue("@serial", $"DEV2-{Guid.NewGuid():N}");
            });
        var mismatched = await Assert.ThrowsAsync<PostgresException>(() => db.InsertAsync(
            @"INSERT INTO device_installation_checklist_observations
                (company_id,branch_id,device_id,work_package_id,checklist_item,observed_result,
                 evidence_reference,observation_notes,observed_at,idempotency_key,recorded_by)
              VALUES(@company,@branch,@device,@work,'Ground','NotObserved','pending','Not yet observed',NOW(),@key,@user)",
            command =>
            {
                command.Parameters.AddWithValue("@company", fixture.CompanyId); command.Parameters.AddWithValue("@branch", fixture.BranchId);
                command.Parameters.AddWithValue("@device", otherDevice); command.Parameters.AddWithValue("@work", workId);
                command.Parameters.AddWithValue("@key", Guid.NewGuid().ToString()); command.Parameters.AddWithValue("@user", fixture.UserId);
            }));
        Assert.Equal(PostgresErrorCodes.CheckViolation, mismatched.SqlState);
        Assert.Equal("ck_stage121_work_evidence_scope", mismatched.ConstraintName);

        var otherRecorder = await db.InsertAsync(
            @"INSERT INTO users(company_id,branch_id,full_name,email,role_name,status)
              VALUES(@company,@branch,'Other operator',@email,'Fleet Manager','Active')",
            command =>
            {
                command.Parameters.AddWithValue("@company", fixture.CompanyId);
                command.Parameters.AddWithValue("@branch", fixture.BranchId);
                command.Parameters.AddWithValue("@email", $"other-installer-{Guid.NewGuid():N}@example.test");
            });
        var wrongRecorder = await Assert.ThrowsAsync<PostgresException>(() => db.InsertAsync(
            @"INSERT INTO device_installation_checklist_observations
                (company_id,branch_id,device_id,work_package_id,checklist_item,observed_result,
                 evidence_reference,observation_notes,observed_at,idempotency_key,recorded_by)
              VALUES(@company,@branch,@device,@work,'Ground','Pass','meter:12.4v',
                     'Recorded by a different operator',NOW(),@key,@recorder)",
            command =>
            {
                command.Parameters.AddWithValue("@company", fixture.CompanyId);
                command.Parameters.AddWithValue("@branch", fixture.BranchId);
                command.Parameters.AddWithValue("@device", fixture.DeviceId);
                command.Parameters.AddWithValue("@work", workId);
                command.Parameters.AddWithValue("@key", Guid.NewGuid().ToString());
                command.Parameters.AddWithValue("@recorder", otherRecorder);
            }));
        Assert.Equal("ck_stage121_work_evidence_recorder_scope", wrongRecorder.ConstraintName);
    }

    private static async Task<Fixture> CreateFixture(Database db)
    {
        var suffix = Guid.NewGuid().ToString("N");
        var company = await db.InsertAsync(
            "INSERT INTO companies(company_code,name,industry) VALUES(@code,'Installation work test','Transportation')",
            command => command.Parameters.AddWithValue("@code", $"IWT-{suffix[..10]}"));
        var branch = await db.InsertAsync(
            "INSERT INTO branches(company_id,branch_code,name) VALUES(@company,@code,'Test branch')",
            command => { command.Parameters.AddWithValue("@company", company); command.Parameters.AddWithValue("@code", $"B1-{suffix}"); });
        var user = await db.InsertAsync(
            @"INSERT INTO users(company_id,branch_id,full_name,email,role_name,status)
              VALUES(@company,@branch,'Installer',@email,'Fleet Manager','Active')",
            command =>
            {
                command.Parameters.AddWithValue("@company", company); command.Parameters.AddWithValue("@branch", branch);
                command.Parameters.AddWithValue("@email", $"installer-{suffix}@example.test");
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
            "INSERT INTO eld_devices(company_id,branch_id,device_serial,status,device_state) VALUES(@company,@branch,@serial,'Provisioning','Registered')",
            command =>
            {
                command.Parameters.AddWithValue("@company", company); command.Parameters.AddWithValue("@branch", branch);
                command.Parameters.AddWithValue("@serial", $"DEV1-{suffix}");
            });
        return new Fixture(company, branch, user, vehicle, device);
    }

    private static Task<long> InsertWork(Database db, Fixture fixture, bool appointmentClaim) => db.InsertAsync(
        @"INSERT INTO device_installation_work_packages
            (company_id,branch_id,device_id,vehicle_id,assigned_installer_user_id,work_order_reference,
             appointment_start,appointment_end,service_location,work_scope,idempotency_key,created_by,
             physical_appointment_claim)
          VALUES(@company,@branch,@device,@vehicle,@user,@reference,NOW()+INTERVAL '1 day',NOW()+INTERVAL '2 days',
                 'Test depot','Install exact test device',@key,@user,@claim)",
        command =>
        {
            Bind(command, fixture); command.Parameters.AddWithValue("@reference", $"WO-{Guid.NewGuid():N}");
            command.Parameters.AddWithValue("@key", Guid.NewGuid().ToString()); command.Parameters.AddWithValue("@claim", appointmentClaim);
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
    private static readonly string[] Tables = [
        "device_installation_work_packages", "device_installation_checklist_observations", "device_installation_artifact_references"];

    private static Database Db() => new(new ConfigurationBuilder().AddInMemoryCollection(
        new Dictionary<string, string?>
        {
            ["ConnectionStrings:DefaultConnection"] = GuardedConnection(),
            ["ConnectionStrings:SystemConnection"] = GuardedConnection(),
            ["Rls:EnforceTenantContext"] = "false",
        }).Build());

    private static string GuardedConnection()
    {
        var configured = Environment.GetEnvironmentVariable("OPSTRAX_DEVICEOPS_STAGE121_DB");
        if (string.IsNullOrWhiteSpace(configured))
            throw SkipException.ForSkip("Set OPSTRAX_DEVICEOPS_STAGE121_DB to the dedicated disposable installation-work database.");
        var builder = new NpgsqlConnectionStringBuilder(configured);
        if (builder.Host is not ("127.0.0.1" or "::1") || builder.Port != 55446 ||
            builder.Database != "opstrax_deviceops_stage121")
            throw new InvalidOperationException("Installation-work tests require the dedicated local database on port 55446.");
        return builder.ConnectionString;
    }
}
