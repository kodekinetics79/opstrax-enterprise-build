using System.Diagnostics;
using System.Reflection;
using System.Runtime.ExceptionServices;
using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Npgsql;
using Opstrax.Api.Controllers;
using Opstrax.Api.Data;
using Opstrax.Api.Services;
using Xunit.Abstractions;

namespace Opstrax.Tests;

[Trait("Category", "Integration")]
[Collection("fleet-identity-schema")]
public sealed class DeviceInstallationImportPostgresTests(ITestOutputHelper output)
{
    [Fact]
    public async Task LargeTenantWideBatchCommits499RowsAtomically()
    {
        var db = Db();
        var companyId = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() + Random.Shared.Next(19_000_000, 19_900_000);
        await Company(db, companyId);
        try
        {
            var effective = DateTimeOffset.UtcNow.AddMinutes(-2).ToString("O");
            var rows = new List<Dictionary<string, object?>>(499);
            for (var branchNumber = 1; branchNumber <= 5; branchNumber++)
            {
                var branchCode = $"LARGE-{branchNumber}";
                var branchId = await Branch(db, companyId, branchCode);
                var first = branchNumber == 1 ? 2 : 1;
                for (var item = first; item <= 100; item++)
                {
                    var vehicleCode = $"LARGE-V-{branchNumber}-{item:D4}-{companyId}";
                    var serial = $"LARGE-D-{branchNumber}-{item:D4}-{companyId}";
                    await Vehicle(db, companyId, branchId, vehicleCode);
                    await Device(db, companyId, branchId, serial);
                    rows.Add(Row(serial, branchCode, vehicleCode, effective,
                        $"large-install-{branchNumber}-{item:D4}-{companyId}"));
                }
            }

            var stopwatch = Stopwatch.StartNew();
            var result = await Invoke("DeviceInstallationsImportCommit", Principal(companyId, null),
                ImportBody(rows.ToArray()), db, new AuditService(db), CancellationToken.None);
            stopwatch.Stop();
            output.WriteLine("499-row/five-branch installation commit duration: {0:F3}s", stopwatch.Elapsed.TotalSeconds);

            Assert.Equal(StatusCodes.Status200OK, Status(result));
            Assert.True(stopwatch.Elapsed < TimeSpan.FromSeconds(30),
                $"499-row atomic commit took {stopwatch.Elapsed.TotalSeconds:F3}s; expected <30s for a 4x client-timeout safety margin.");
            Assert.Equal(499, await db.ScalarLongAsync(
                "SELECT COUNT(*) FROM device_installations WHERE company_id=@c AND source='operator'",
                command => command.Parameters.AddWithValue("@c", companyId)));
            Assert.Equal(499, await db.ScalarLongAsync(
                "SELECT COUNT(*) FROM device_state_transitions WHERE company_id=@c AND reason_code='installation_created'",
                command => command.Parameters.AddWithValue("@c", companyId)));
            Assert.Equal(499, await db.ScalarLongAsync(
                "SELECT COUNT(*) FROM idempotency_keys WHERE tenant_id=@c AND operation='device.installation.bulk-import' AND status='completed'",
                command => command.Parameters.AddWithValue("@c", companyId)));
            Assert.Equal(499, await db.ScalarLongAsync(
                "SELECT COUNT(*) FROM audit_logs WHERE company_id=@c AND action_name='device.installation.created'",
                command => command.Parameters.AddWithValue("@c", companyId)));
            Assert.Equal(1, await db.ScalarLongAsync(
                "SELECT COUNT(*) FROM audit_logs WHERE company_id=@c AND action_name='device.installations.imported'",
                command => command.Parameters.AddWithValue("@c", companyId)));
        }
        finally
        {
            await Cleanup(db, companyId);
        }
    }

    [Fact]
    public async Task AmbientTenantScopeCountMismatchReturns409RollsBackAndRemainsCommittable()
    {
        var owner = Db();
        var companyId = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() + Random.Shared.Next(17_000_000, 17_400_000);
        await Company(owner, companyId);
        try
        {
            var branch = await Branch(owner, companyId, "AMBIENT-COUNT");
            var vehicleCode = $"AMBIENT-COUNT-V-{companyId}";
            var serial = $"AMBIENT-COUNT-D-{companyId}";
            await Vehicle(owner, companyId, branch, vehicleCode);
            var device = await Device(owner, companyId, branch, serial);
            await owner.ExecuteAsync(
                @"CREATE OR REPLACE FUNCTION test_suppress_bulk_install_transition() RETURNS TRIGGER
                    LANGUAGE plpgsql AS $$
                    BEGIN
                      IF NEW.reason='Force batch count mismatch' THEN RETURN NULL; END IF;
                      RETURN NEW;
                    END $$;
                  DROP TRIGGER IF EXISTS test_suppress_bulk_install_transition ON device_state_transitions;
                  CREATE TRIGGER test_suppress_bulk_install_transition
                    BEFORE INSERT ON device_state_transitions FOR EACH ROW
                    EXECUTE FUNCTION test_suppress_bulk_install_transition()" );

            var runtime = ProtectedDb();
            var result = await runtime.RunInTenantScopeAsync(companyId, async () =>
            {
                var response = await Invoke("DeviceInstallationsImportCommit", Principal(companyId, branch),
                    ImportBody(Row(serial, "AMBIENT-COUNT", vehicleCode,
                        DateTimeOffset.UtcNow.AddMinutes(-2).ToString("O"), $"ambient-count-{companyId}",
                        "Force batch count mismatch")),
                    runtime, new AuditService(runtime), CancellationToken.None);
                Assert.Equal(StatusCodes.Status409Conflict, Status(response));
                Assert.Contains("No rows changed", ResponseJson(response), StringComparison.Ordinal);
                Assert.Equal(1, await runtime.ScalarLongAsync("SELECT 1"));
                return response;
            });
            Assert.Equal(StatusCodes.Status409Conflict, Status(result));
            Assert.Equal("Registered", (await owner.QuerySingleAsync(
                "SELECT device_state FROM eld_devices WHERE company_id=@c AND id=@d",
                command => { command.Parameters.AddWithValue("@c", companyId); command.Parameters.AddWithValue("@d", device); }))!["deviceState"]?.ToString());
            await AssertNoInstallationImportMutation(owner, companyId);
        }
        finally
        {
            await owner.ExecuteAsync(
                @"DROP TRIGGER IF EXISTS test_suppress_bulk_install_transition ON device_state_transitions;
                  DROP FUNCTION IF EXISTS test_suppress_bulk_install_transition()" );
            await Cleanup(owner, companyId);
        }
    }

    [Fact]
    public async Task AmbientTenantScopeConstraintReturnsUseful409RollsBackAndRemainsCommittable()
    {
        var owner = Db();
        var companyId = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() + Random.Shared.Next(17_500_000, 17_900_000);
        await Company(owner, companyId);
        try
        {
            var branch = await Branch(owner, companyId, "AMBIENT-CONSTRAINT");
            var vehicleCode = $"AMBIENT-CONSTRAINT-V-{companyId}";
            var serial = $"AMBIENT-CONSTRAINT-D-{companyId}";
            await Vehicle(owner, companyId, branch, vehicleCode);
            var device = await Device(owner, companyId, branch, serial);
            await owner.ExecuteAsync(
                @"ALTER TABLE device_installations DROP CONSTRAINT IF EXISTS test_bulk_import_constraint;
                  ALTER TABLE device_installations ADD CONSTRAINT test_bulk_import_constraint
                    CHECK (assignment_reason IS DISTINCT FROM 'Force constraint conflict')" );

            var runtime = ProtectedDb();
            var result = await runtime.RunInTenantScopeAsync(companyId, async () =>
            {
                var response = await Invoke("DeviceInstallationsImportCommit", Principal(companyId, branch),
                    ImportBody(Row(serial, "AMBIENT-CONSTRAINT", vehicleCode,
                        DateTimeOffset.UtcNow.AddMinutes(-2).ToString("O"), $"ambient-constraint-{companyId}",
                        "Force constraint conflict")),
                    runtime, new AuditService(runtime), CancellationToken.None);
                Assert.Equal(StatusCodes.Status409Conflict, Status(response));
                Assert.Contains("No rows changed", ResponseJson(response), StringComparison.Ordinal);
                Assert.Equal(1, await runtime.ScalarLongAsync("SELECT 1"));
                return response;
            });
            Assert.Equal(StatusCodes.Status409Conflict, Status(result));
            Assert.Equal("Registered", (await owner.QuerySingleAsync(
                "SELECT device_state FROM eld_devices WHERE company_id=@c AND id=@d",
                command => { command.Parameters.AddWithValue("@c", companyId); command.Parameters.AddWithValue("@d", device); }))!["deviceState"]?.ToString());
            await AssertNoInstallationImportMutation(owner, companyId);
        }
        finally
        {
            await owner.ExecuteAsync(
                "ALTER TABLE device_installations DROP CONSTRAINT IF EXISTS test_bulk_import_constraint");
            await Cleanup(owner, companyId);
        }
    }

    [Fact]
    public async Task AmbientTenantScopeAuditSequenceConflictFailsClosedAndRemainsCommittable()
    {
        var owner = Db();
        var companyId = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() + Random.Shared.Next(16_500_000, 16_900_000);
        await Company(owner, companyId);
        try
        {
            var branch = await Branch(owner, companyId, "AMBIENT-AUDIT");
            var vehicleCode = $"AMBIENT-AUDIT-V-{companyId}";
            var serial = $"AMBIENT-AUDIT-D-{companyId}";
            await Vehicle(owner, companyId, branch, vehicleCode);
            var device = await Device(owner, companyId, branch, serial);
            var occupiedId = await owner.InsertAsync(
                @"INSERT INTO audit_logs(company_id,actor_name,action_name,entity_name,details_json)
                  VALUES (@c,'test','test.audit.sequence.occupied','DeviceInstallation','{}'::jsonb)",
                command => command.Parameters.AddWithValue("@c", companyId));
            await owner.ExecuteAsync(
                "SELECT setval(pg_get_serial_sequence('audit_logs','id'),@occupied,FALSE)",
                command => command.Parameters.AddWithValue("@occupied", occupiedId));

            var runtime = ProtectedDb();
            var result = await runtime.RunInTenantScopeAsync(companyId, async () =>
            {
                var response = await Invoke("DeviceInstallationsImportCommit", Principal(companyId, branch),
                    ImportBody(Row(serial, "AMBIENT-AUDIT", vehicleCode,
                        DateTimeOffset.UtcNow.AddMinutes(-2).ToString("O"), $"ambient-audit-{companyId}")),
                    runtime, new AuditService(runtime), CancellationToken.None);
                Assert.Equal(StatusCodes.Status409Conflict, Status(response));
                Assert.Contains("No rows changed", ResponseJson(response), StringComparison.Ordinal);
                Assert.Equal(1, await runtime.ScalarLongAsync("SELECT 1"));
                return response;
            });
            Assert.Equal(StatusCodes.Status409Conflict, Status(result));
            Assert.Equal("Registered", (await owner.QuerySingleAsync(
                "SELECT device_state FROM eld_devices WHERE company_id=@c AND id=@d",
                command => { command.Parameters.AddWithValue("@c", companyId); command.Parameters.AddWithValue("@d", device); }))!["deviceState"]?.ToString());
            await AssertNoInstallationImportMutation(owner, companyId);
        }
        finally
        {
            await owner.ExecuteAsync(
                @"SELECT setval(pg_get_serial_sequence('audit_logs','id'),
                    GREATEST((SELECT COALESCE(MAX(id),0) FROM audit_logs),1),TRUE)" );
            await Cleanup(owner, companyId);
        }
    }

    [Fact]
    public async Task PreviewRejectsClosedHistoryOverlapAndCommitRemainsAtomic()
    {
        var db = Db();
        var companyId = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() + Random.Shared.Next(18_000_000, 18_900_000);
        await Company(db, companyId);
        try
        {
            var branch = await Branch(db, companyId, "HISTORY-A");
            var historicalVehicleCode = $"HISTORY-V-1-{companyId}";
            var validVehicleCode = $"HISTORY-V-2-{companyId}";
            var historicalSerial = $"HISTORY-D-1-{companyId}";
            var validSerial = $"HISTORY-D-2-{companyId}";
            var historicalVehicle = await Vehicle(db, companyId, branch, historicalVehicleCode);
            var validVehicle = await Vehicle(db, companyId, branch, validVehicleCode);
            var historicalDevice = await Device(db, companyId, branch, historicalSerial);
            var validDevice = await Device(db, companyId, branch, validSerial);
            var historyFrom = DateTimeOffset.UtcNow.AddDays(-10);
            var historyTo = DateTimeOffset.UtcNow.AddDays(-5);
            await db.ExecuteAsync(
                @"INSERT INTO device_installations
                    (company_id,branch_id,device_id,vehicle_id,status,device_role,is_primary,effective_from,effective_to,
                     installed_at,removed_at,assignment_reason,removal_reason,source,idempotency_key,created_at)
                  VALUES (@c,@b,@d,@v,'Removed','GPS',TRUE,@from,@to,@from,@to,
                          'Historical assignment','Historical removal','operator',@key,NOW())",
                command =>
                {
                    command.Parameters.AddWithValue("@c", companyId); command.Parameters.AddWithValue("@b", branch);
                    command.Parameters.AddWithValue("@d", historicalDevice); command.Parameters.AddWithValue("@v", historicalVehicle);
                    command.Parameters.AddWithValue("@from", historyFrom); command.Parameters.AddWithValue("@to", historyTo);
                    command.Parameters.AddWithValue("@key", $"history-existing-{companyId}");
                });

            var exclusion = await Assert.ThrowsAsync<PostgresException>(() => db.ExecuteAsync(
                @"INSERT INTO device_installations
                    (company_id,branch_id,device_id,vehicle_id,status,device_role,is_primary,effective_from,
                     installed_at,assignment_reason,source,idempotency_key,created_at)
                  VALUES (@c,@b,@d,@v,'Installed','GPS',TRUE,@from,@from,
                          'Overlapping assignment','operator',@key,NOW())",
                command =>
                {
                    command.Parameters.AddWithValue("@c", companyId); command.Parameters.AddWithValue("@b", branch);
                    command.Parameters.AddWithValue("@d", historicalDevice); command.Parameters.AddWithValue("@v", historicalVehicle);
                    command.Parameters.AddWithValue("@from", historyFrom.AddDays(2));
                    command.Parameters.AddWithValue("@key", $"history-direct-overlap-{companyId}");
                }));
            Assert.Equal(PostgresErrorCodes.ExclusionViolation, exclusion.SqlState);

            var overlapping = ImportBody(
                Row(historicalSerial, "HISTORY-A", historicalVehicleCode,
                    historyFrom.AddDays(2).ToString("O"), $"history-overlap-{companyId}"),
                Row(validSerial, "HISTORY-A", validVehicleCode,
                    DateTimeOffset.UtcNow.AddDays(-1).ToString("O"), $"history-valid-{companyId}"));
            var preview = await Invoke("DeviceInstallationsImportPreview", Principal(companyId, branch),
                overlapping, db, CancellationToken.None);

            Assert.Equal(StatusCodes.Status200OK, Status(preview));
            Assert.Contains("overlaps closed installation history", ResponseJson(preview), StringComparison.Ordinal);
            Assert.Contains("overlaps closed primary GPS assignment history", ResponseJson(preview), StringComparison.Ordinal);
            var rejected = await Invoke("DeviceInstallationsImportCommit", Principal(companyId, branch),
                overlapping, db, new AuditService(db), CancellationToken.None);
            Assert.Equal(StatusCodes.Status400BadRequest, Status(rejected));
            Assert.Equal(1, await db.ScalarLongAsync(
                "SELECT COUNT(*) FROM device_installations WHERE company_id=@c",
                command => command.Parameters.AddWithValue("@c", companyId)));
            Assert.Equal(0, await db.ScalarLongAsync(
                "SELECT COUNT(*) FROM idempotency_keys WHERE tenant_id=@c AND operation='device.installation.bulk-import'",
                command => command.Parameters.AddWithValue("@c", companyId)));
            Assert.Equal("Registered", (await db.QuerySingleAsync(
                "SELECT device_state FROM eld_devices WHERE company_id=@c AND id=@d",
                command => { command.Parameters.AddWithValue("@c", companyId); command.Parameters.AddWithValue("@d", validDevice); }))!["deviceState"]?.ToString());

            var corrected = ImportBody(
                Row(historicalSerial, "HISTORY-A", historicalVehicleCode,
                    historyTo.AddDays(1).ToString("O"), $"history-corrected-{companyId}"),
                Row(validSerial, "HISTORY-A", validVehicleCode,
                    DateTimeOffset.UtcNow.AddDays(-1).ToString("O"), $"history-valid-{companyId}"));
            var committed = await Invoke("DeviceInstallationsImportCommit", Principal(companyId, branch),
                corrected, db, new AuditService(db), CancellationToken.None);
            Assert.Equal(StatusCodes.Status200OK, Status(committed));
            Assert.Equal(2, await db.ScalarLongAsync(
                "SELECT COUNT(*) FROM device_installations WHERE company_id=@c AND effective_to IS NULL",
                command => command.Parameters.AddWithValue("@c", companyId)));
        }
        finally
        {
            await Cleanup(db, companyId);
        }
    }

    [Fact]
    public async Task BulkInstallPersistsHistoryReplaysAndRollsBackConflictsAndBranchMismatch()
    {
        var db = Db();
        var companyId = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() + Random.Shared.Next(20_000_000, 20_900_000);
        await Company(db, companyId);
        try
        {
            var branchA = await Branch(db, companyId, "BULK-A");
            var branchB = await Branch(db, companyId, "BULK-B");
            var vehicleA1 = await Vehicle(db, companyId, branchA, $"BULK-A1-{companyId}");
            var vehicleA2 = await Vehicle(db, companyId, branchA, $"BULK-A2-{companyId}");
            var vehicleA3 = await Vehicle(db, companyId, branchA, $"BULK-A3-{companyId}");
            var vehicleA4 = await Vehicle(db, companyId, branchA, $"BULK-A4-{companyId}");
            var vehicleA5 = await Vehicle(db, companyId, branchA, $"BULK-A5-{companyId}");
            var deviceA1 = await Device(db, companyId, branchA, $"BULK-DEV-A1-{companyId}");
            var deviceA2 = await Device(db, companyId, branchA, $"BULK-DEV-A2-{companyId}");
            var deviceA3 = await Device(db, companyId, branchA, $"BULK-DEV-A3-{companyId}");
            var deviceA4 = await Device(db, companyId, branchA, $"BULK-DEV-A4-{companyId}");
            var deviceA5 = await Device(db, companyId, branchA, $"BULK-DEV-A5-{companyId}");
            var deviceB = await Device(db, companyId, branchB, $"BULK-DEV-B-{companyId}");
            var effective = DateTimeOffset.UtcNow.AddMinutes(-2).ToString("O");
            var initial = ImportBody(
                Row($"BULK-DEV-A1-{companyId}", "BULK-A", $"BULK-A1-{companyId}", effective, $"bulk-a1-{companyId}"),
                Row($"BULK-DEV-A2-{companyId}", "BULK-A", $"BULK-A2-{companyId}", effective, $"bulk-a2-{companyId}"));
            var http = Principal(companyId, branchA);

            var created = await Invoke("DeviceInstallationsImportCommit", http, initial, db, new AuditService(db), CancellationToken.None);
            Assert.Equal(StatusCodes.Status200OK, Status(created));
            Assert.Equal(2, await db.ScalarLongAsync(
                "SELECT COUNT(*) FROM device_installations WHERE company_id=@c AND device_id=ANY(@ids)",
                c => { c.Parameters.AddWithValue("@c", companyId); c.Parameters.AddWithValue("@ids", new[] { deviceA1, deviceA2 }); }));

            var alteredReplayRow = Row($"BULK-DEV-A1-{companyId}", "BULK-A", $"BULK-A1-{companyId}", effective, $"bulk-a1-{companyId}");
            alteredReplayRow["installationLocation"] = "Rear bulkhead";
            var alteredReplay = await Invoke("DeviceInstallationsImportCommit", Principal(companyId, branchA),
                ImportBody(alteredReplayRow), db, new AuditService(db), CancellationToken.None);
            Assert.Equal(StatusCodes.Status409Conflict, Status(alteredReplay));
            Assert.Equal(2, await db.ScalarLongAsync(
                "SELECT COUNT(*) FROM device_installations WHERE company_id=@c AND device_id=ANY(@ids)",
                c => { c.Parameters.AddWithValue("@c", companyId); c.Parameters.AddWithValue("@ids", new[] { deviceA1, deviceA2 }); }));
            Assert.Equal(2, await db.ScalarLongAsync(
                "SELECT COUNT(*) FROM device_state_transitions WHERE company_id=@c AND reason_code='installation_created' AND device_id=ANY(@ids)",
                c => { c.Parameters.AddWithValue("@c", companyId); c.Parameters.AddWithValue("@ids", new[] { deviceA1, deviceA2 }); }));

            var timezoneLess = DateTimeOffset.UtcNow.AddMinutes(-2).ToString("yyyy-MM-dd'T'HH:mm:ss", System.Globalization.CultureInfo.InvariantCulture);
            var invalidTimezoneBatch = ImportBody(
                Row($"BULK-DEV-A3-{companyId}", "BULK-A", $"BULK-A3-{companyId}", effective, $"bulk-a3-time-{companyId}"),
                Row($"BULK-DEV-A4-{companyId}", "BULK-A", $"BULK-A4-{companyId}", timezoneLess, $"bulk-a4-time-{companyId}"));
            var invalidTimezone = await Invoke("DeviceInstallationsImportCommit", Principal(companyId, branchA),
                invalidTimezoneBatch, db, new AuditService(db), CancellationToken.None);
            Assert.Equal(StatusCodes.Status400BadRequest, Status(invalidTimezone));
            Assert.Equal(0, await db.ScalarLongAsync(
                "SELECT COUNT(*) FROM device_installations WHERE company_id=@c AND device_id=ANY(@ids)",
                c => { c.Parameters.AddWithValue("@c", companyId); c.Parameters.AddWithValue("@ids", new[] { deviceA3, deviceA4 }); }));
            Assert.Equal(0, await db.ScalarLongAsync(
                "SELECT COUNT(*) FROM device_installations WHERE company_id=@c AND status='Verified' AND device_id=ANY(@ids)",
                c => { c.Parameters.AddWithValue("@c", companyId); c.Parameters.AddWithValue("@ids", new[] { deviceA1, deviceA2 }); }));
            Assert.Equal(2, await db.ScalarLongAsync(
                "SELECT COUNT(*) FROM device_state_transitions WHERE company_id=@c AND reason_code='installation_created' AND device_id=ANY(@ids)",
                c => { c.Parameters.AddWithValue("@c", companyId); c.Parameters.AddWithValue("@ids", new[] { deviceA1, deviceA2 }); }));

            var replay = await Invoke("DeviceInstallationsImportCommit", Principal(companyId, branchA), initial, db, new AuditService(db), CancellationToken.None);
            Assert.Equal(StatusCodes.Status200OK, Status(replay));
            Assert.Equal(2, await db.ScalarLongAsync(
                "SELECT COUNT(*) FROM device_installations WHERE company_id=@c AND device_id=ANY(@ids)",
                c => { c.Parameters.AddWithValue("@c", companyId); c.Parameters.AddWithValue("@ids", new[] { deviceA1, deviceA2 }); }));

            var conflictBatch = ImportBody(
                Row($"BULK-DEV-A3-{companyId}", "BULK-A", $"BULK-A3-{companyId}", effective, $"bulk-a3-{companyId}"),
                Row($"BULK-DEV-A1-{companyId}", "BULK-A", $"BULK-A2-{companyId}", effective, $"bulk-conflict-{companyId}"));
            var conflict = await Invoke("DeviceInstallationsImportCommit", Principal(companyId, branchA), conflictBatch, db, new AuditService(db), CancellationToken.None);
            Assert.Equal(StatusCodes.Status400BadRequest, Status(conflict));
            Assert.Equal(0, await db.ScalarLongAsync(
                "SELECT COUNT(*) FROM device_installations WHERE company_id=@c AND device_id=@d",
                c => { c.Parameters.AddWithValue("@c", companyId); c.Parameters.AddWithValue("@d", deviceA3); }));

            var branchMismatch = ImportBody(Row(
                $"BULK-DEV-B-{companyId}", "BULK-B", $"BULK-A3-{companyId}", effective, $"bulk-branch-{companyId}"));
            var denied = await Invoke("DeviceInstallationsImportCommit", Principal(companyId, branchA), branchMismatch, db, new AuditService(db), CancellationToken.None);
            Assert.Equal(StatusCodes.Status400BadRequest, Status(denied));
            Assert.Equal(0, await db.ScalarLongAsync(
                "SELECT COUNT(*) FROM device_installations WHERE company_id=@c AND device_id=@d",
                c => { c.Parameters.AddWithValue("@c", companyId); c.Parameters.AddWithValue("@d", deviceB); }));

            var legacyEffective = DateTimeOffset.UtcNow.AddMinutes(-1);
            await db.ExecuteAsync(
                @"INSERT INTO device_installations
                    (company_id,branch_id,device_id,vehicle_id,status,device_role,is_primary,effective_from,installed_at,
                     installation_location,odometer_at_installation,commissioning_method,assignment_reason,source,idempotency_key,created_at)
                  VALUES (@c,@b,@d,@v,'Installed','GPS',TRUE,@effective,@effective,'Front dashboard',100,
                          'CSV onboarding','Initial governed installation','operator',@key,NOW())",
                c =>
                {
                    c.Parameters.AddWithValue("@c", companyId); c.Parameters.AddWithValue("@b", branchA);
                    c.Parameters.AddWithValue("@d", deviceA4); c.Parameters.AddWithValue("@v", vehicleA4);
                    c.Parameters.AddWithValue("@effective", legacyEffective);
                    c.Parameters.AddWithValue("@key", $"bulk-legacy-{companyId}");
                });
            var legacyReplay = await Invoke("DeviceInstallationsImportCommit", Principal(companyId, branchA),
                ImportBody(Row($"BULK-DEV-A5-{companyId}", "BULK-A", $"BULK-A5-{companyId}",
                    legacyEffective.ToString("O"), $"bulk-legacy-{companyId}")), db, new AuditService(db), CancellationToken.None);
            Assert.Equal(StatusCodes.Status409Conflict, Status(legacyReplay));
            Assert.Equal(1, await db.ScalarLongAsync(
                "SELECT COUNT(*) FROM device_installations WHERE company_id=@c AND device_id=@d",
                c => { c.Parameters.AddWithValue("@c", companyId); c.Parameters.AddWithValue("@d", deviceA4); }));
            Assert.Equal(0, await db.ScalarLongAsync(
                "SELECT COUNT(*) FROM device_installations WHERE company_id=@c AND device_id=@d",
                c => { c.Parameters.AddWithValue("@c", companyId); c.Parameters.AddWithValue("@d", deviceA5); }));

            _ = vehicleA1; _ = vehicleA2; _ = vehicleA3; _ = vehicleA4; _ = vehicleA5;
        }
        finally
        {
            foreach (var sql in new[]
            {
                "DELETE FROM audit_logs WHERE company_id=@c", "DELETE FROM device_state_transitions WHERE company_id=@c",
                "DELETE FROM idempotency_keys WHERE tenant_id=@c",
                "DELETE FROM device_installations WHERE company_id=@c", "DELETE FROM eld_devices WHERE company_id=@c",
                "DELETE FROM vehicles WHERE company_id=@c", "DELETE FROM branches WHERE company_id=@c",
                "DELETE FROM companies WHERE id=@c"
            }) await db.ExecuteAsync(sql, c => c.Parameters.AddWithValue("@c", companyId));
        }
    }

    [Theory]
    [InlineData("Suspended", "Suspended", false)]
    [InlineData("Revoked", "Decommissioned", true)]
    public async Task LifecycleLockFirstMakesBulkFailAtomicallyWithoutOverwritingState(
        string lifecycleStatus, string lifecycleState, bool revoked)
    {
        var observer = Db();
        var companyId = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() + Random.Shared.Next(21_000_000, 21_900_000);
        await Company(observer, companyId);
        try
        {
            var branch = await Branch(observer, companyId, "RACE-A");
            var vehicle = await Vehicle(observer, companyId, branch, $"RACE-V-{companyId}");
            var device = await Device(observer, companyId, branch, $"RACE-DEV-{companyId}");
            var applicationName = $"bulk-lifecycle-first-{companyId}";
            await using var lifecycleConnection = new NpgsqlConnection(TestDb.ConnectionString);
            await lifecycleConnection.OpenAsync();
            await using var lifecycleTransaction = await lifecycleConnection.BeginTransactionAsync();
            await using (var command = new NpgsqlCommand(
                "SELECT id FROM eld_devices WHERE company_id=@c AND id=@d FOR UPDATE", lifecycleConnection, lifecycleTransaction))
            {
                command.Parameters.AddWithValue("@c", companyId); command.Parameters.AddWithValue("@d", device);
                await command.ExecuteNonQueryAsync();
            }
            await using (var command = new NpgsqlCommand(
                @"UPDATE eld_devices SET status=@status,device_state=@state,
                     revoked_at=CASE WHEN @revoked THEN NOW() ELSE NULL END,updated_at=NOW(),row_version=row_version+1
                   WHERE company_id=@c AND id=@d", lifecycleConnection, lifecycleTransaction))
            {
                command.Parameters.AddWithValue("@status", lifecycleStatus); command.Parameters.AddWithValue("@state", lifecycleState);
                command.Parameters.AddWithValue("@revoked", revoked); command.Parameters.AddWithValue("@c", companyId); command.Parameters.AddWithValue("@d", device);
                await command.ExecuteNonQueryAsync();
            }

            var body = ImportBody(Row($"RACE-DEV-{companyId}", "RACE-A", $"RACE-V-{companyId}",
                DateTimeOffset.UtcNow.AddMinutes(-1).ToString("O"), $"race-lifecycle-{companyId}"));
            var bulkDb = Db(applicationName);
            var bulk = Invoke("DeviceInstallationsImportCommit", Principal(companyId, branch), body,
                bulkDb, new AuditService(bulkDb), CancellationToken.None);
            await WaitForDatabaseLock(observer, applicationName, lifecycleConnection.ProcessID, "%FROM eld_devices d%");
            await lifecycleTransaction.CommitAsync();

            var result = await bulk;
            Assert.Equal(StatusCodes.Status409Conflict, Status(result));
            Assert.Equal(0, await observer.ScalarLongAsync(
                "SELECT COUNT(*) FROM device_installations WHERE company_id=@c AND device_id=@d",
                c => { c.Parameters.AddWithValue("@c", companyId); c.Parameters.AddWithValue("@d", device); }));
            Assert.Equal(0, await observer.ScalarLongAsync(
                "SELECT COUNT(*) FROM idempotency_keys WHERE tenant_id=@c AND operation='device.installation.bulk-import'",
                c => c.Parameters.AddWithValue("@c", companyId)));
            var persisted = await observer.QuerySingleAsync(
                "SELECT status,device_state,revoked_at FROM eld_devices WHERE company_id=@c AND id=@d",
                c => { c.Parameters.AddWithValue("@c", companyId); c.Parameters.AddWithValue("@d", device); });
            Assert.Equal(lifecycleStatus, persisted!["status"]?.ToString());
            Assert.Equal(lifecycleState, persisted["deviceState"]?.ToString());
            Assert.Equal(revoked, persisted["revokedAt"] is not (null or DBNull));
            _ = vehicle;
        }
        finally
        {
            await Cleanup(observer, companyId);
        }
    }

    [Fact]
    public async Task BulkLockFirstThenSuspendLeavesLaterLifecycleStateAuthoritative()
    {
        var observer = Db();
        var companyId = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() + Random.Shared.Next(22_000_000, 22_900_000);
        await Company(observer, companyId);
        try
        {
            var branch = await Branch(observer, companyId, "ORDER-A");
            var vehicle = await Vehicle(observer, companyId, branch, $"ORDER-V-{companyId}");
            var device = await Device(observer, companyId, branch, $"ORDER-DEV-{companyId}");
            await using var blockerConnection = new NpgsqlConnection(TestDb.ConnectionString);
            await blockerConnection.OpenAsync();
            await using var blockerTransaction = await blockerConnection.BeginTransactionAsync();
            await using (var command = new NpgsqlCommand(
                "SELECT id FROM vehicles WHERE company_id=@c AND id=@v FOR UPDATE", blockerConnection, blockerTransaction))
            {
                command.Parameters.AddWithValue("@c", companyId); command.Parameters.AddWithValue("@v", vehicle);
                await command.ExecuteNonQueryAsync();
            }

            var bulkApplication = $"bulk-first-{companyId}";
            var bulkDb = Db(bulkApplication);
            var bulk = Invoke("DeviceInstallationsImportCommit", Principal(companyId, branch),
                ImportBody(Row($"ORDER-DEV-{companyId}", "ORDER-A", $"ORDER-V-{companyId}",
                    DateTimeOffset.UtcNow.AddMinutes(-1).ToString("O"), $"order-bulk-{companyId}")),
                bulkDb, new AuditService(bulkDb), CancellationToken.None);
            await WaitForDatabaseLock(observer, bulkApplication, blockerConnection.ProcessID, "%FROM vehicles v%");

            var suspendApplication = $"suspend-second-{companyId}";
            var suspendDb = Db(suspendApplication);
            var suspend = Invoke("DeviceSuspend", Principal(companyId, branch), device,
                suspendDb, new AuditService(suspendDb), CancellationToken.None);
            await WaitForDatabaseLock(observer, suspendApplication);
            await blockerTransaction.CommitAsync();

            Assert.Equal(StatusCodes.Status200OK, Status(await bulk));
            Assert.Equal(StatusCodes.Status200OK, Status(await suspend));
            Assert.Equal(1, await observer.ScalarLongAsync(
                "SELECT COUNT(*) FROM device_installations WHERE company_id=@c AND device_id=@d AND effective_to IS NULL",
                c => { c.Parameters.AddWithValue("@c", companyId); c.Parameters.AddWithValue("@d", device); }));
            var persisted = await observer.QuerySingleAsync(
                "SELECT status,device_state FROM eld_devices WHERE company_id=@c AND id=@d",
                c => { c.Parameters.AddWithValue("@c", companyId); c.Parameters.AddWithValue("@d", device); });
            Assert.Equal("Suspended", persisted!["status"]?.ToString());
            Assert.Equal("Suspended", persisted["deviceState"]?.ToString());
        }
        finally
        {
            await Cleanup(observer, companyId);
        }
    }

    [Fact]
    public async Task IdentityReplacementBetweenValidationAndLockReturnsConflictWithoutTouchingReplacement()
    {
        var observer = Db();
        var companyId = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() + Random.Shared.Next(23_000_000, 23_900_000);
        await Company(observer, companyId);
        Exception? primaryFailure = null;
        try
        {
            var branch = await Branch(observer, companyId, "IDENTITY-A");
            var vehicle = await Vehicle(observer, companyId, branch, $"IDENTITY-V-{companyId}");
            var serial = $"IDENTITY-DEV-{companyId}";
            var originalDevice = await Device(observer, companyId, branch, serial);
            await using var identityConnection = new NpgsqlConnection(TestDb.ConnectionString);
            await identityConnection.OpenAsync();
            await using var identityTransaction = await identityConnection.BeginTransactionAsync();
            await using (var command = new NpgsqlCommand(
                "SELECT id FROM eld_devices WHERE company_id=@c AND id=@d FOR UPDATE", identityConnection, identityTransaction))
            {
                command.Parameters.AddWithValue("@c", companyId); command.Parameters.AddWithValue("@d", originalDevice);
                await command.ExecuteNonQueryAsync();
            }

            var applicationName = $"bulk-identity-change-{companyId}";
            var bulkDb = Db(applicationName);
            var bulk = Invoke("DeviceInstallationsImportCommit", Principal(companyId, branch),
                ImportBody(Row(serial, "IDENTITY-A", $"IDENTITY-V-{companyId}",
                    DateTimeOffset.UtcNow.AddMinutes(-1).ToString("O"), $"identity-race-{companyId}")),
                bulkDb, new AuditService(bulkDb), CancellationToken.None);
            await WaitForDatabaseLock(observer, applicationName, identityConnection.ProcessID, "%FROM eld_devices d%");

            await using (var rename = new NpgsqlCommand(
                "UPDATE eld_devices SET device_serial=@replacement,updated_at=NOW() WHERE company_id=@c AND id=@d",
                identityConnection, identityTransaction))
            {
                rename.Parameters.AddWithValue("@replacement", $"IDENTITY-OLD-{companyId}");
                rename.Parameters.AddWithValue("@c", companyId); rename.Parameters.AddWithValue("@d", originalDevice);
                await rename.ExecuteNonQueryAsync();
            }
            long replacementDevice;
            await using (var create = new NpgsqlCommand(
                @"INSERT INTO eld_devices(company_id,branch_id,device_serial,status,device_state,api_key_hash,
                     hmac_secret_encrypted,hmac_key_version,created_at)
                   VALUES (@c,@b,@serial,'Active','Registered',
                     encode(sha256((@serial || '-replacement-key')::bytea),'hex'),repeat('c',32),1,NOW())
                   RETURNING id", identityConnection, identityTransaction))
            {
                create.Parameters.AddWithValue("@c", companyId); create.Parameters.AddWithValue("@b", branch);
                create.Parameters.AddWithValue("@serial", serial);
                replacementDevice = Convert.ToInt64(await create.ExecuteScalarAsync());
            }
            await identityTransaction.CommitAsync();

            Assert.Equal(StatusCodes.Status409Conflict, Status(await bulk));
            Assert.Equal(0, await observer.ScalarLongAsync(
                "SELECT COUNT(*) FROM device_installations WHERE company_id=@c AND device_id=ANY(@ids)",
                c => { c.Parameters.AddWithValue("@c", companyId); c.Parameters.AddWithValue("@ids", new[] { originalDevice, replacementDevice }); }));
            var replacement = await observer.QuerySingleAsync(
                "SELECT device_state FROM eld_devices WHERE company_id=@c AND id=@d",
                c => { c.Parameters.AddWithValue("@c", companyId); c.Parameters.AddWithValue("@d", replacementDevice); });
            Assert.Equal("Registered", replacement!["deviceState"]?.ToString());
            Assert.Equal(0, await observer.ScalarLongAsync(
                "SELECT COUNT(*) FROM idempotency_keys WHERE tenant_id=@c AND operation='device.installation.bulk-import'",
                c => c.Parameters.AddWithValue("@c", companyId)));
            _ = vehicle;
        }
        catch (Exception exception)
        {
            primaryFailure = exception;
        }
        finally
        {
            try { await Cleanup(observer, companyId); }
            catch when (primaryFailure is not null) { }
        }
        if (primaryFailure is not null) ExceptionDispatchInfo.Capture(primaryFailure).Throw();
    }

    [Fact]
    public async Task SingleCreateFirstAndBulkImportSerializeWithoutDeadlockOrDuplicateHistory()
    {
        var observer = Db();
        var companyId = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() + Random.Shared.Next(24_000_000, 24_900_000);
        await Company(observer, companyId);
        try
        {
            var branch = await Branch(observer, companyId, "SERIALIZE-A");
            var vehicle = await Vehicle(observer, companyId, branch, $"SERIALIZE-V-{companyId}");
            var serial = $"SERIALIZE-DEV-{companyId}";
            var device = await Device(observer, companyId, branch, serial);
            await using var blockerConnection = new NpgsqlConnection(TestDb.ConnectionString);
            await blockerConnection.OpenAsync();
            await using var blockerTransaction = await blockerConnection.BeginTransactionAsync();
            await using (var command = new NpgsqlCommand(
                "SELECT id FROM eld_devices WHERE company_id=@c AND id=@d FOR UPDATE", blockerConnection, blockerTransaction))
            {
                command.Parameters.AddWithValue("@c", companyId); command.Parameters.AddWithValue("@d", device);
                await command.ExecuteNonQueryAsync();
            }

            var singleApplication = $"single-install-first-{companyId}";
            var singleDb = Db(singleApplication);
            var single = Invoke("DeviceInstallationCreate", Principal(companyId, branch), device,
                Body("DeviceInstallationCreateBody", vehicle, "GPS", true,
                    (DateTimeOffset?)DateTimeOffset.UtcNow.AddMinutes(-1), "Front dashboard", (decimal?)100m,
                    "Governed form", "Single installation wins race", $"single-race-{companyId}"),
                singleDb, new AuditService(singleDb), CancellationToken.None);
            await WaitForDatabaseLock(observer, singleApplication, blockerConnection.ProcessID, "%FROM eld_devices d%");
            var singlePid = await observer.ScalarLongAsync(
                "SELECT pid FROM pg_stat_activity WHERE application_name=@app",
                command => command.Parameters.AddWithValue("@app", singleApplication));

            var bulkApplication = $"bulk-install-second-{companyId}";
            var bulkDb = Db(bulkApplication);
            var bulk = Invoke("DeviceInstallationsImportCommit", Principal(companyId, branch),
                ImportBody(Row(serial, "SERIALIZE-A", $"SERIALIZE-V-{companyId}",
                    DateTimeOffset.UtcNow.AddMinutes(-1).ToString("O"), $"bulk-race-{companyId}")),
                bulkDb, new AuditService(bulkDb), CancellationToken.None);
            await WaitForDatabaseLock(observer, bulkApplication, checked((int)singlePid), "%pg_advisory_xact_lock%");
            await blockerTransaction.CommitAsync();

            Assert.Equal(StatusCodes.Status201Created, Status(await single));
            Assert.Equal(StatusCodes.Status409Conflict, Status(await bulk));
            Assert.Equal(1, await observer.ScalarLongAsync(
                "SELECT COUNT(*) FROM device_installations WHERE company_id=@c AND device_id=@d AND effective_to IS NULL",
                command => { command.Parameters.AddWithValue("@c", companyId); command.Parameters.AddWithValue("@d", device); }));
            Assert.Equal(1, await observer.ScalarLongAsync(
                "SELECT COUNT(*) FROM device_state_transitions WHERE company_id=@c AND device_id=@d AND reason_code='installation_created'",
                command => { command.Parameters.AddWithValue("@c", companyId); command.Parameters.AddWithValue("@d", device); }));
            Assert.Equal(0, await observer.ScalarLongAsync(
                "SELECT COUNT(*) FROM idempotency_keys WHERE tenant_id=@c AND operation='device.installation.bulk-import'",
                command => command.Parameters.AddWithValue("@c", companyId)));
            var state = await observer.QuerySingleAsync(
                "SELECT status,device_state FROM eld_devices WHERE company_id=@c AND id=@d",
                command => { command.Parameters.AddWithValue("@c", companyId); command.Parameters.AddWithValue("@d", device); });
            Assert.Equal("Active", state!["status"]?.ToString());
            Assert.Equal("Installed", state["deviceState"]?.ToString());
        }
        finally
        {
            await Cleanup(observer, companyId);
        }
    }

    [Theory]
    [InlineData("Suspended", "Suspended", false)]
    [InlineData("Revoked", "Decommissioned", true)]
    public async Task LifecycleFirstMakesSingleInstallationFailAtomically(
        string lifecycleStatus, string lifecycleState, bool revoked)
    {
        var observer = Db();
        var companyId = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() + Random.Shared.Next(28_000_000, 28_900_000);
        await Company(observer, companyId);
        try
        {
            var branch = await Branch(observer, companyId, "SINGLE-LIFECYCLE-A");
            var vehicle = await Vehicle(observer, companyId, branch, $"SINGLE-LIFECYCLE-V-{companyId}");
            var device = await Device(observer, companyId, branch, $"SINGLE-LIFECYCLE-DEV-{companyId}");
            await using var lifecycleConnection = new NpgsqlConnection(TestDb.ConnectionString);
            await lifecycleConnection.OpenAsync();
            await using var lifecycleTransaction = await lifecycleConnection.BeginTransactionAsync();
            await using (var command = new NpgsqlCommand(
                @"UPDATE eld_devices SET status=@status,device_state=@state,
                     revoked_at=CASE WHEN @revoked THEN NOW() ELSE NULL END,updated_at=NOW()
                   WHERE company_id=@c AND id=@d", lifecycleConnection, lifecycleTransaction))
            {
                command.Parameters.AddWithValue("@status", lifecycleStatus); command.Parameters.AddWithValue("@state", lifecycleState);
                command.Parameters.AddWithValue("@revoked", revoked); command.Parameters.AddWithValue("@c", companyId);
                command.Parameters.AddWithValue("@d", device);
                Assert.Equal(1, await command.ExecuteNonQueryAsync());
            }

            var applicationName = $"single-after-lifecycle-{companyId}";
            var singleDb = Db(applicationName);
            var single = Invoke("DeviceInstallationCreate", Principal(companyId, branch), device,
                Body("DeviceInstallationCreateBody", vehicle, "GPS", true,
                    (DateTimeOffset?)DateTimeOffset.UtcNow.AddMinutes(-1), "Front dashboard", (decimal?)100m,
                    "Governed form", "Lifecycle ordering test", $"single-lifecycle-{companyId}"),
                singleDb, new AuditService(singleDb), CancellationToken.None);
            await WaitForDatabaseLock(observer, applicationName, lifecycleConnection.ProcessID, "%FROM eld_devices d%");
            await lifecycleTransaction.CommitAsync();

            Assert.Equal(StatusCodes.Status409Conflict, Status(await single));
            Assert.Equal(0, await observer.ScalarLongAsync(
                "SELECT COUNT(*) FROM device_installations WHERE company_id=@c AND device_id=@d",
                command => { command.Parameters.AddWithValue("@c", companyId); command.Parameters.AddWithValue("@d", device); }));
            Assert.Equal(0, await observer.ScalarLongAsync(
                "SELECT COUNT(*) FROM device_state_transitions WHERE company_id=@c AND device_id=@d",
                command => { command.Parameters.AddWithValue("@c", companyId); command.Parameters.AddWithValue("@d", device); }));
            var state = await observer.QuerySingleAsync(
                "SELECT status,device_state,revoked_at FROM eld_devices WHERE company_id=@c AND id=@d",
                command => { command.Parameters.AddWithValue("@c", companyId); command.Parameters.AddWithValue("@d", device); });
            Assert.Equal(lifecycleStatus, state!["status"]?.ToString());
            Assert.Equal(lifecycleState, state["deviceState"]?.ToString());
            Assert.Equal(revoked, state["revokedAt"] is not (null or DBNull));
        }
        finally
        {
            await Cleanup(observer, companyId);
        }
    }

    [Fact]
    public async Task SingleInstallationFirstThenSuspendLeavesLifecycleAuthoritative()
    {
        var observer = Db();
        var companyId = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() + Random.Shared.Next(29_000_000, 29_900_000);
        await Company(observer, companyId);
        try
        {
            var branch = await Branch(observer, companyId, "SINGLE-ORDER-A");
            var vehicle = await Vehicle(observer, companyId, branch, $"SINGLE-ORDER-V-{companyId}");
            var device = await Device(observer, companyId, branch, $"SINGLE-ORDER-DEV-{companyId}");
            await using var vehicleBlockerConnection = new NpgsqlConnection(TestDb.ConnectionString);
            await vehicleBlockerConnection.OpenAsync();
            await using var vehicleBlockerTransaction = await vehicleBlockerConnection.BeginTransactionAsync();
            await using (var command = new NpgsqlCommand(
                "SELECT id FROM vehicles WHERE company_id=@c AND id=@v FOR UPDATE",
                vehicleBlockerConnection, vehicleBlockerTransaction))
            {
                command.Parameters.AddWithValue("@c", companyId); command.Parameters.AddWithValue("@v", vehicle);
                await command.ExecuteNonQueryAsync();
            }

            var singleApplication = $"single-install-lock-first-{companyId}";
            var singleDb = Db(singleApplication);
            var single = Invoke("DeviceInstallationCreate", Principal(companyId, branch), device,
                Body("DeviceInstallationCreateBody", vehicle, "GPS", true,
                    (DateTimeOffset?)DateTimeOffset.UtcNow.AddMinutes(-1), "Front dashboard", (decimal?)100m,
                    "Governed form", "Single installation first", $"single-order-{companyId}"),
                singleDb, new AuditService(singleDb), CancellationToken.None);
            await WaitForDatabaseLock(observer, singleApplication, vehicleBlockerConnection.ProcessID, "%FROM vehicles v%");
            var singlePid = await observer.ScalarLongAsync(
                "SELECT pid FROM pg_stat_activity WHERE application_name=@app",
                command => command.Parameters.AddWithValue("@app", singleApplication));

            var suspendApplication = $"suspend-after-single-{companyId}";
            var suspendDb = Db(suspendApplication);
            var suspend = Invoke("DeviceSuspend", Principal(companyId, branch), device,
                suspendDb, new AuditService(suspendDb), CancellationToken.None);
            await WaitForDatabaseLock(observer, suspendApplication, checked((int)singlePid), "%pg_advisory_xact_lock%");
            await vehicleBlockerTransaction.CommitAsync();

            Assert.Equal(StatusCodes.Status201Created, Status(await single));
            Assert.Equal(StatusCodes.Status200OK, Status(await suspend));
            Assert.Equal(1, await observer.ScalarLongAsync(
                "SELECT COUNT(*) FROM device_installations WHERE company_id=@c AND device_id=@d AND effective_to IS NULL",
                command => { command.Parameters.AddWithValue("@c", companyId); command.Parameters.AddWithValue("@d", device); }));
            var state = await observer.QuerySingleAsync(
                "SELECT status,device_state FROM eld_devices WHERE company_id=@c AND id=@d",
                command => { command.Parameters.AddWithValue("@c", companyId); command.Parameters.AddWithValue("@d", device); });
            Assert.Equal("Suspended", state!["status"]?.ToString());
            Assert.Equal("Suspended", state["deviceState"]?.ToString());
            Assert.Equal(2, await observer.ScalarLongAsync(
                "SELECT COUNT(*) FROM device_state_transitions WHERE company_id=@c AND device_id=@d",
                command => { command.Parameters.AddWithValue("@c", companyId); command.Parameters.AddWithValue("@d", device); }));
        }
        finally
        {
            await Cleanup(observer, companyId);
        }
    }

    [Fact]
    public async Task TransferFirstThenSuspendSerializesAndRemovalPreservesTerminalLifecycle()
    {
        var observer = Db();
        var companyId = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() + Random.Shared.Next(30_000_000, 30_900_000);
        await Company(observer, companyId);
        try
        {
            var branch = await Branch(observer, companyId, "TRANSFER-ORDER-A");
            var sourceVehicle = await Vehicle(observer, companyId, branch, $"TRANSFER-SOURCE-{companyId}");
            var targetVehicle = await Vehicle(observer, companyId, branch, $"TRANSFER-TARGET-{companyId}");
            var device = await Device(observer, companyId, branch, $"TRANSFER-DEV-{companyId}");
            var created = await Invoke("DeviceInstallationCreate", Principal(companyId, branch), device,
                Body("DeviceInstallationCreateBody", sourceVehicle, "GPS", true,
                    (DateTimeOffset?)DateTimeOffset.UtcNow.AddMinutes(-3), "Front dashboard", (decimal?)100m,
                    "Governed form", "Initial transfer source", $"transfer-source-{companyId}"),
                observer, new AuditService(observer), CancellationToken.None);
            Assert.Equal(StatusCodes.Status201Created, Status(created));
            var priorId = await observer.ScalarLongAsync(
                "SELECT id FROM device_installations WHERE company_id=@c AND device_id=@d AND effective_to IS NULL",
                command => { command.Parameters.AddWithValue("@c", companyId); command.Parameters.AddWithValue("@d", device); });

            await using var vehicleBlockerConnection = new NpgsqlConnection(TestDb.ConnectionString);
            await vehicleBlockerConnection.OpenAsync();
            await using var vehicleBlockerTransaction = await vehicleBlockerConnection.BeginTransactionAsync();
            await using (var command = new NpgsqlCommand(
                "SELECT id FROM vehicles WHERE company_id=@c AND id=@v FOR UPDATE",
                vehicleBlockerConnection, vehicleBlockerTransaction))
            {
                command.Parameters.AddWithValue("@c", companyId); command.Parameters.AddWithValue("@v", targetVehicle);
                await command.ExecuteNonQueryAsync();
            }

            var transferApplication = $"transfer-lock-first-{companyId}";
            var transferDb = Db(transferApplication);
            var transfer = Invoke("DeviceInstallationTransfer", Principal(companyId, branch), device,
                Body("DeviceInstallationTransferBody", targetVehicle, (long?)priorId,
                    "Replace assigned vehicle", "Assign target vehicle", "GPS", true,
                    (DateTimeOffset?)DateTimeOffset.UtcNow.AddMinutes(-1), "Front dashboard", (decimal?)150m,
                    "Governed transfer", (int?)1, $"transfer-target-{companyId}"),
                transferDb, new AuditService(transferDb), CancellationToken.None);
            await WaitForDatabaseLock(observer, transferApplication, vehicleBlockerConnection.ProcessID, "%FROM vehicles v%");
            var transferPid = await observer.ScalarLongAsync(
                "SELECT pid FROM pg_stat_activity WHERE application_name=@app",
                command => command.Parameters.AddWithValue("@app", transferApplication));
            var suspendApplication = $"suspend-after-transfer-{companyId}";
            var suspendDb = Db(suspendApplication);
            var suspend = Invoke("DeviceSuspend", Principal(companyId, branch), device,
                suspendDb, new AuditService(suspendDb), CancellationToken.None);
            await WaitForDatabaseLock(observer, suspendApplication, checked((int)transferPid), "%pg_advisory_xact_lock%");
            await vehicleBlockerTransaction.CommitAsync();

            Assert.Equal(StatusCodes.Status200OK, Status(await transfer));
            Assert.Equal(StatusCodes.Status200OK, Status(await suspend));
            var active = await observer.QuerySingleAsync(
                "SELECT id,vehicle_id,row_version FROM device_installations WHERE company_id=@c AND device_id=@d AND effective_to IS NULL",
                command => { command.Parameters.AddWithValue("@c", companyId); command.Parameters.AddWithValue("@d", device); });
            Assert.Equal(targetVehicle, Convert.ToInt64(active!["vehicleId"]));
            var activeId = Convert.ToInt64(active["id"]);
            var remove = await Invoke("DeviceInstallationRemove", Principal(companyId, branch), device, activeId,
                Body("DeviceInstallationRemoveBody", "Remove after lifecycle suspension",
                    (DateTimeOffset?)DateTimeOffset.UtcNow, (int?)Convert.ToInt32(active["rowVersion"])),
                observer, new AuditService(observer), CancellationToken.None);
            Assert.Equal(StatusCodes.Status200OK, Status(remove));
            var finalState = await observer.QuerySingleAsync(
                "SELECT status,device_state FROM eld_devices WHERE company_id=@c AND id=@d",
                command => { command.Parameters.AddWithValue("@c", companyId); command.Parameters.AddWithValue("@d", device); });
            Assert.Equal("Suspended", finalState!["status"]?.ToString());
            Assert.Equal("Suspended", finalState["deviceState"]?.ToString());
            Assert.Equal(0, await observer.ScalarLongAsync(
                "SELECT COUNT(*) FROM device_installations WHERE company_id=@c AND device_id=@d AND effective_to IS NULL",
                command => { command.Parameters.AddWithValue("@c", companyId); command.Parameters.AddWithValue("@d", device); }));
            Assert.Equal(0, await observer.ScalarLongAsync(
                "SELECT COUNT(*) FROM device_state_transitions WHERE company_id=@c AND device_id=@d AND reason_code='installation_removed'",
                command => { command.Parameters.AddWithValue("@c", companyId); command.Parameters.AddWithValue("@d", device); }));
        }
        finally
        {
            await Cleanup(observer, companyId);
        }
    }

    [Fact]
    public async Task BranchInactivationFirstMakesSingleInstallationFailWithoutMutation()
    {
        var observer = Db();
        var companyId = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() + Random.Shared.Next(31_000_000, 31_900_000);
        await Company(observer, companyId);
        try
        {
            var branch = await Branch(observer, companyId, "SINGLE-BRANCH-A");
            var vehicle = await Vehicle(observer, companyId, branch, $"SINGLE-BRANCH-V-{companyId}");
            var device = await Device(observer, companyId, branch, $"SINGLE-BRANCH-DEV-{companyId}");
            await using var branchConnection = new NpgsqlConnection(TestDb.ConnectionString);
            await branchConnection.OpenAsync();
            await using var branchTransaction = await branchConnection.BeginTransactionAsync();
            await using (var command = new NpgsqlCommand(
                "UPDATE branches SET status='Inactive',updated_at=NOW() WHERE company_id=@c AND id=@b",
                branchConnection, branchTransaction))
            {
                command.Parameters.AddWithValue("@c", companyId); command.Parameters.AddWithValue("@b", branch);
                Assert.Equal(1, await command.ExecuteNonQueryAsync());
            }
            var applicationName = $"single-branch-inactive-{companyId}";
            var singleDb = Db(applicationName);
            var single = Invoke("DeviceInstallationCreate", Principal(companyId, branch), device,
                Body("DeviceInstallationCreateBody", vehicle, "GPS", true,
                    (DateTimeOffset?)DateTimeOffset.UtcNow.AddMinutes(-1), "Front dashboard", (decimal?)100m,
                    "Governed form", "Inactive branch rejection", $"single-inactive-{companyId}"),
                singleDb, new AuditService(singleDb), CancellationToken.None);
            await WaitForDatabaseLock(observer, applicationName, branchConnection.ProcessID, "%FROM branches b%");
            await branchTransaction.CommitAsync();
            Assert.Equal(StatusCodes.Status400BadRequest, Status(await single));
            Assert.Equal(0, await observer.ScalarLongAsync(
                "SELECT COUNT(*) FROM device_installations WHERE company_id=@c",
                command => command.Parameters.AddWithValue("@c", companyId)));
            Assert.Equal("Registered", (await observer.QuerySingleAsync(
                "SELECT device_state FROM eld_devices WHERE company_id=@c AND id=@d",
                command => { command.Parameters.AddWithValue("@c", companyId); command.Parameters.AddWithValue("@d", device); }))!["deviceState"]?.ToString());
        }
        finally
        {
            await Cleanup(observer, companyId);
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task TargetVehicleMoveOrArchiveFirstMakesTransferFailWithoutClosingPrior(bool archive)
    {
        var observer = Db();
        var companyId = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() + Random.Shared.Next(32_000_000, 32_900_000);
        await Company(observer, companyId);
        try
        {
            var branchA = await Branch(observer, companyId, "TRANSFER-VEHICLE-A");
            var branchB = await Branch(observer, companyId, "TRANSFER-VEHICLE-B");
            var sourceVehicle = await Vehicle(observer, companyId, branchA, $"TRANSFER-VEHICLE-SOURCE-{companyId}");
            var targetVehicle = await Vehicle(observer, companyId, branchA, $"TRANSFER-VEHICLE-TARGET-{companyId}");
            var device = await Device(observer, companyId, branchA, $"TRANSFER-VEHICLE-DEV-{companyId}");
            var created = await Invoke("DeviceInstallationCreate", Principal(companyId, branchA), device,
                Body("DeviceInstallationCreateBody", sourceVehicle, "GPS", true,
                    (DateTimeOffset?)DateTimeOffset.UtcNow.AddMinutes(-3), "Front dashboard", (decimal?)100m,
                    "Governed form", "Initial assignment", $"vehicle-race-source-{companyId}"),
                observer, new AuditService(observer), CancellationToken.None);
            Assert.Equal(StatusCodes.Status201Created, Status(created));
            var priorId = await observer.ScalarLongAsync(
                "SELECT id FROM device_installations WHERE company_id=@c AND device_id=@d AND effective_to IS NULL",
                command => { command.Parameters.AddWithValue("@c", companyId); command.Parameters.AddWithValue("@d", device); });

            await using var vehicleConnection = new NpgsqlConnection(TestDb.ConnectionString);
            await vehicleConnection.OpenAsync();
            await using var vehicleTransaction = await vehicleConnection.BeginTransactionAsync();
            await using (var command = new NpgsqlCommand(
                archive
                    ? "UPDATE vehicles SET deleted_at=NOW() WHERE company_id=@c AND id=@v"
                    : "UPDATE vehicles SET branch_id=@other WHERE company_id=@c AND id=@v",
                vehicleConnection, vehicleTransaction))
            {
                command.Parameters.AddWithValue("@c", companyId); command.Parameters.AddWithValue("@v", targetVehicle);
                if (!archive) command.Parameters.AddWithValue("@other", branchB);
                Assert.Equal(1, await command.ExecuteNonQueryAsync());
            }
            var applicationName = $"transfer-vehicle-change-{archive}-{companyId}";
            var transferDb = Db(applicationName);
            var transfer = Invoke("DeviceInstallationTransfer", Principal(companyId, branchA), device,
                Body("DeviceInstallationTransferBody", targetVehicle, (long?)priorId,
                    "Replace assigned vehicle", "Assign target vehicle", "GPS", true,
                    (DateTimeOffset?)DateTimeOffset.UtcNow.AddMinutes(-1), "Front dashboard", (decimal?)150m,
                    "Governed transfer", (int?)1, $"vehicle-race-target-{companyId}"),
                transferDb, new AuditService(transferDb), CancellationToken.None);
            await WaitForDatabaseLock(observer, applicationName, vehicleConnection.ProcessID, "%FROM vehicles v%");
            await vehicleTransaction.CommitAsync();
            Assert.Equal(StatusCodes.Status400BadRequest, Status(await transfer));
            var prior = await observer.QuerySingleAsync(
                "SELECT status,effective_to,vehicle_id FROM device_installations WHERE company_id=@c AND id=@i",
                command => { command.Parameters.AddWithValue("@c", companyId); command.Parameters.AddWithValue("@i", priorId); });
            Assert.Equal("Installed", prior!["status"]?.ToString());
            Assert.True(prior["effectiveTo"] is null or DBNull);
            Assert.Equal(sourceVehicle, Convert.ToInt64(prior["vehicleId"]));
            Assert.Equal(1, await observer.ScalarLongAsync(
                "SELECT COUNT(*) FROM device_installations WHERE company_id=@c AND device_id=@d",
                command => { command.Parameters.AddWithValue("@c", companyId); command.Parameters.AddWithValue("@d", device); }));
            Assert.Equal("Installed", (await observer.QuerySingleAsync(
                "SELECT device_state FROM eld_devices WHERE company_id=@c AND id=@d",
                command => { command.Parameters.AddWithValue("@c", companyId); command.Parameters.AddWithValue("@d", device); }))!["deviceState"]?.ToString());
        }
        finally
        {
            await Cleanup(observer, companyId);
        }
    }

    [Fact]
    public async Task BranchInactivationFirstMakesBulkFailAtomically()
    {
        var observer = Db();
        var companyId = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() + Random.Shared.Next(24_900_001, 25_400_000);
        await Company(observer, companyId);
        try
        {
            var branch = await Branch(observer, companyId, "BRANCH-RACE-A");
            var vehicle = await Vehicle(observer, companyId, branch, $"BRANCH-RACE-V-{companyId}");
            var serial = $"BRANCH-RACE-DEV-{companyId}";
            var device = await Device(observer, companyId, branch, serial);
            await using var branchConnection = new NpgsqlConnection(TestDb.ConnectionString);
            await branchConnection.OpenAsync();
            await using var branchTransaction = await branchConnection.BeginTransactionAsync();
            await using (var command = new NpgsqlCommand(
                "UPDATE branches SET status='Inactive',updated_at=NOW() WHERE company_id=@c AND id=@b",
                branchConnection, branchTransaction))
            {
                command.Parameters.AddWithValue("@c", companyId); command.Parameters.AddWithValue("@b", branch);
                Assert.Equal(1, await command.ExecuteNonQueryAsync());
            }

            var bulkApplication = $"bulk-branch-inactive-first-{companyId}";
            var bulkDb = Db(bulkApplication);
            var bulk = Invoke("DeviceInstallationsImportCommit", Principal(companyId, branch),
                ImportBody(Row(serial, "BRANCH-RACE-A", $"BRANCH-RACE-V-{companyId}",
                    DateTimeOffset.UtcNow.AddMinutes(-1).ToString("O"), $"branch-inactive-{companyId}")),
                bulkDb, new AuditService(bulkDb), CancellationToken.None);
            await WaitForDatabaseLock(observer, bulkApplication, branchConnection.ProcessID, "%FROM branches b%");
            await branchTransaction.CommitAsync();

            Assert.Equal(StatusCodes.Status409Conflict, Status(await bulk));
            Assert.Equal(0, await observer.ScalarLongAsync(
                "SELECT COUNT(*) FROM device_installations WHERE company_id=@c",
                command => command.Parameters.AddWithValue("@c", companyId)));
            Assert.Equal(0, await observer.ScalarLongAsync(
                "SELECT COUNT(*) FROM idempotency_keys WHERE tenant_id=@c AND operation='device.installation.bulk-import'",
                command => command.Parameters.AddWithValue("@c", companyId)));
            var state = await observer.QuerySingleAsync(
                "SELECT d.device_state,b.status branch_status FROM eld_devices d JOIN branches b ON b.id=d.branch_id WHERE d.company_id=@c AND d.id=@d",
                command => { command.Parameters.AddWithValue("@c", companyId); command.Parameters.AddWithValue("@d", device); });
            Assert.Equal("Registered", state!["deviceState"]?.ToString());
            Assert.Equal("Inactive", state["branchStatus"]?.ToString());
            _ = vehicle;
        }
        finally
        {
            await Cleanup(observer, companyId);
        }
    }

    [Fact]
    public async Task BulkBranchLockFirstCommitsBeforeLaterInactivation()
    {
        var observer = Db();
        var companyId = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() + Random.Shared.Next(25_400_001, 25_900_000);
        await Company(observer, companyId);
        try
        {
            var branch = await Branch(observer, companyId, "BRANCH-ORDER-A");
            var vehicle = await Vehicle(observer, companyId, branch, $"BRANCH-ORDER-V-{companyId}");
            var serial = $"BRANCH-ORDER-DEV-{companyId}";
            var device = await Device(observer, companyId, branch, serial);
            await using var deviceBlockerConnection = new NpgsqlConnection(TestDb.ConnectionString);
            await deviceBlockerConnection.OpenAsync();
            await using var deviceBlockerTransaction = await deviceBlockerConnection.BeginTransactionAsync();
            await using (var command = new NpgsqlCommand(
                "SELECT id FROM eld_devices WHERE company_id=@c AND id=@d FOR UPDATE",
                deviceBlockerConnection, deviceBlockerTransaction))
            {
                command.Parameters.AddWithValue("@c", companyId); command.Parameters.AddWithValue("@d", device);
                await command.ExecuteNonQueryAsync();
            }

            var bulkApplication = $"bulk-branch-lock-first-{companyId}";
            var bulkDb = Db(bulkApplication);
            var bulk = Invoke("DeviceInstallationsImportCommit", Principal(companyId, branch),
                ImportBody(Row(serial, "BRANCH-ORDER-A", $"BRANCH-ORDER-V-{companyId}",
                    DateTimeOffset.UtcNow.AddMinutes(-1).ToString("O"), $"branch-order-{companyId}")),
                bulkDb, new AuditService(bulkDb), CancellationToken.None);
            await WaitForDatabaseLock(observer, bulkApplication, deviceBlockerConnection.ProcessID, "%FROM eld_devices d%");
            var bulkPid = await observer.ScalarLongAsync(
                "SELECT pid FROM pg_stat_activity WHERE application_name=@app",
                command => command.Parameters.AddWithValue("@app", bulkApplication));

            var updaterBuilder = new NpgsqlConnectionStringBuilder(TestDb.ConnectionString)
            {
                ApplicationName = $"branch-inactive-second-{companyId}"
            };
            await using var updaterConnection = new NpgsqlConnection(updaterBuilder.ConnectionString);
            await updaterConnection.OpenAsync();
            await using var updaterTransaction = await updaterConnection.BeginTransactionAsync();
            await using var updaterCommand = new NpgsqlCommand(
                "UPDATE branches SET status='Inactive',updated_at=NOW() WHERE company_id=@c AND id=@b",
                updaterConnection, updaterTransaction);
            updaterCommand.Parameters.AddWithValue("@c", companyId); updaterCommand.Parameters.AddWithValue("@b", branch);
            var update = updaterCommand.ExecuteNonQueryAsync();
            await WaitForDatabaseLock(observer, updaterBuilder.ApplicationName, checked((int)bulkPid), "%UPDATE branches%");

            await deviceBlockerTransaction.CommitAsync();
            Assert.Equal(StatusCodes.Status200OK, Status(await bulk));
            Assert.Equal(1, await update);
            await updaterTransaction.CommitAsync();
            Assert.Equal(1, await observer.ScalarLongAsync(
                "SELECT COUNT(*) FROM device_installations WHERE company_id=@c AND device_id=@d AND effective_to IS NULL",
                command => { command.Parameters.AddWithValue("@c", companyId); command.Parameters.AddWithValue("@d", device); }));
            var persisted = await observer.QuerySingleAsync(
                "SELECT d.device_state,b.status branch_status FROM eld_devices d JOIN branches b ON b.id=d.branch_id WHERE d.company_id=@c AND d.id=@d",
                command => { command.Parameters.AddWithValue("@c", companyId); command.Parameters.AddWithValue("@d", device); });
            Assert.Equal("Installed", persisted!["deviceState"]?.ToString());
            Assert.Equal("Inactive", persisted["branchStatus"]?.ToString());
            _ = vehicle;
        }
        finally
        {
            await Cleanup(observer, companyId);
        }
    }

    [Fact]
    public async Task AmbiguousQuarantinedDeviceIdentityIsRejectedWithoutMutation()
    {
        var observer = Db();
        var companyId = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() + Random.Shared.Next(26_000_000, 26_900_000);
        await Company(observer, companyId);
        try
        {
            var branch = await Branch(observer, companyId, "AMBIGUOUS-A");
            var vehicle = await Vehicle(observer, companyId, branch, $"AMBIGUOUS-V-{companyId}");
            var serial = $"AMBIGUOUS-DEV-{companyId}";
            _ = await Device(observer, companyId, branch, serial);
            await observer.ExecuteAsync(
                @"INSERT INTO eld_devices(company_id,branch_id,device_serial,status,device_state,api_key_hash,
                     hmac_secret_encrypted,hmac_key_version,created_at)
                   VALUES (@c,@b,@serial,'Active','Registered',
                     encode(sha256((@serial || '-second-key')::bytea),'hex'),repeat('q',32),1,NOW())",
                command =>
                {
                    command.Parameters.AddWithValue("@c", companyId); command.Parameters.AddWithValue("@b", branch);
                    command.Parameters.AddWithValue("@serial", serial.ToLowerInvariant());
                });
            Assert.Equal(2, await observer.ScalarLongAsync(
                "SELECT COUNT(*) FROM eld_devices WHERE company_id=@c AND UPPER(BTRIM(device_serial))=UPPER(@serial) AND device_state='Quarantined'",
                command => { command.Parameters.AddWithValue("@c", companyId); command.Parameters.AddWithValue("@serial", serial); }));

            var body = ImportBody(Row(serial, "AMBIGUOUS-A", $"AMBIGUOUS-V-{companyId}",
                DateTimeOffset.UtcNow.AddMinutes(-1).ToString("O"), $"ambiguous-key-{companyId}"));
            var preview = await Invoke("DeviceInstallationsImportPreview", Principal(companyId, branch), body,
                observer, CancellationToken.None);
            Assert.Equal(StatusCodes.Status200OK, Status(preview));
            Assert.Equal(new[]
            {
                "Device identity is ambiguous or quarantined; resolve identity quarantine before import."
            }, ErrorStrings(preview));
            var commit = await Invoke("DeviceInstallationsImportCommit", Principal(companyId, branch), body,
                observer, new AuditService(observer), CancellationToken.None);
            Assert.Equal(StatusCodes.Status400BadRequest, Status(commit));
            Assert.Contains("Device identity is ambiguous or quarantined", ResponseJson(commit), StringComparison.Ordinal);
            Assert.Equal(0, await observer.ScalarLongAsync(
                "SELECT COUNT(*) FROM device_installations WHERE company_id=@c",
                command => command.Parameters.AddWithValue("@c", companyId)));
            Assert.Equal(0, await observer.ScalarLongAsync(
                "SELECT COUNT(*) FROM idempotency_keys WHERE tenant_id=@c AND operation='device.installation.bulk-import'",
                command => command.Parameters.AddWithValue("@c", companyId)));
            Assert.Equal(0, await observer.ScalarLongAsync(
                "SELECT COUNT(*) FROM device_state_transitions WHERE company_id=@c",
                command => command.Parameters.AddWithValue("@c", companyId)));
            _ = vehicle;
        }
        finally
        {
            await Cleanup(observer, companyId);
        }
    }

    [Fact]
    public async Task AmbiguousNormalizedBranchCodeIsRejectedWithoutEnrichmentOrMutation()
    {
        var observer = Db();
        var companyId = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() + Random.Shared.Next(27_000_000, 27_900_000);
        await Company(observer, companyId);
        try
        {
            _ = await Branch(observer, companyId, "CASE-BRANCH");
            _ = await Branch(observer, companyId, "case-branch");
            var body = ImportBody(Row($"CASE-DEV-{companyId}", "CASE-BRANCH", $"CASE-V-{companyId}",
                DateTimeOffset.UtcNow.AddMinutes(-1).ToString("O"), $"case-branch-key-{companyId}"));
            var preview = await Invoke("DeviceInstallationsImportPreview", Principal(companyId, null), body,
                observer, CancellationToken.None);
            Assert.Equal(StatusCodes.Status200OK, Status(preview));
            Assert.Equal(new[]
            {
                "Submitted branch identity is ambiguous; resolve duplicate branch codes before import."
            }, ErrorStrings(preview));
            Assert.DoesNotContain("not registered", ResponseJson(preview), StringComparison.OrdinalIgnoreCase);
            var commit = await Invoke("DeviceInstallationsImportCommit", Principal(companyId, null), body,
                observer, new AuditService(observer), CancellationToken.None);
            Assert.Equal(StatusCodes.Status400BadRequest, Status(commit));
            Assert.Contains("Submitted branch identity is ambiguous", ResponseJson(commit), StringComparison.Ordinal);
            Assert.Equal(0, await observer.ScalarLongAsync(
                "SELECT COUNT(*) FROM device_installations WHERE company_id=@c",
                command => command.Parameters.AddWithValue("@c", companyId)));
            Assert.Equal(0, await observer.ScalarLongAsync(
                "SELECT COUNT(*) FROM idempotency_keys WHERE tenant_id=@c AND operation='device.installation.bulk-import'",
                command => command.Parameters.AddWithValue("@c", companyId)));
            Assert.Equal(0, await observer.ScalarLongAsync(
                "SELECT COUNT(*) FROM device_state_transitions WHERE company_id=@c",
                command => command.Parameters.AddWithValue("@c", companyId)));
        }
        finally
        {
            await Cleanup(observer, companyId);
        }
    }

    [Fact]
    public async Task BranchScopedPreviewAndCommitDoNotRevealCrossBranchIdentitiesOrKeys()
    {
        var observer = Db();
        var companyId = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() + Random.Shared.Next(25_000_000, 25_900_000);
        await Company(observer, companyId);
        try
        {
            var branchA = await Branch(observer, companyId, "SCOPE-A");
            var branchB = await Branch(observer, companyId, "SCOPE-B");
            var vehicleBCode = $"SCOPE-B-V-{companyId}";
            var vehicleB = await Vehicle(observer, companyId, branchB, vehicleBCode);
            var serialB = $"SCOPE-B-DEV-{companyId}";
            var deviceB = await Device(observer, companyId, branchB, serialB);
            var existingKey = $"scope-existing-{companyId}";
            var seedDb = Db();
            var seeded = await Invoke("DeviceInstallationCreate", Principal(companyId, branchB), deviceB,
                Body("DeviceInstallationCreateBody", vehicleB, "GPS", true,
                    (DateTimeOffset?)DateTimeOffset.UtcNow.AddMinutes(-2), "Front dashboard", (decimal?)100m,
                    "Governed form", "Existing branch B installation", existingKey),
                seedDb, new AuditService(seedDb), CancellationToken.None);
            Assert.Equal(StatusCodes.Status201Created, Status(seeded));

            var effective = DateTimeOffset.UtcNow.AddMinutes(-1).ToString("O");
            var realClaimingB = ImportBody(Row(serialB, "SCOPE-B", vehicleBCode, effective, existingKey));
            var randomClaimingB = ImportBody(Row($"RANDOM-DEV-{companyId}", "SCOPE-B", $"RANDOM-V-{companyId}", effective, existingKey));
            var realClaimingA = ImportBody(Row(serialB, "SCOPE-A", vehicleBCode, effective, existingKey));
            var randomClaimingA = ImportBody(Row($"RANDOM-DEV-{companyId}", "SCOPE-A", $"RANDOM-V-{companyId}", effective, existingKey));
            var branchAPrincipal = Principal(companyId, branchA);

            var realBPreview = await Invoke("DeviceInstallationsImportPreview", branchAPrincipal, realClaimingB, observer, CancellationToken.None);
            var randomBPreview = await Invoke("DeviceInstallationsImportPreview", Principal(companyId, branchA), randomClaimingB, observer, CancellationToken.None);
            Assert.Equal(StatusCodes.Status200OK, Status(realBPreview));
            Assert.Equal(StatusCodes.Status200OK, Status(randomBPreview));
            Assert.Equal(ErrorStrings(randomBPreview), ErrorStrings(realBPreview));
            Assert.Equal(new[] { "Submitted branch is outside the authorized branch." }, ErrorStrings(realBPreview));

            var realAPreview = await Invoke("DeviceInstallationsImportPreview", Principal(companyId, branchA), realClaimingA, observer, CancellationToken.None);
            var randomAPreview = await Invoke("DeviceInstallationsImportPreview", Principal(companyId, branchA), randomClaimingA, observer, CancellationToken.None);
            Assert.Equal(ErrorStrings(randomAPreview), ErrorStrings(realAPreview));
            Assert.Equal(new[]
            {
                "Device is not registered in this tenant.",
                "Vehicle is not active in this tenant."
            }, ErrorStrings(realAPreview));
            Assert.DoesNotContain("active installation", ResponseJson(realAPreview), StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("idempotency", ResponseJson(realAPreview), StringComparison.OrdinalIgnoreCase);

            foreach (var body in new[] { realClaimingB, randomClaimingB, realClaimingA, randomClaimingA })
            {
                var commit = await Invoke("DeviceInstallationsImportCommit", Principal(companyId, branchA), body,
                    observer, new AuditService(observer), CancellationToken.None);
                Assert.Equal(StatusCodes.Status400BadRequest, Status(commit));
                Assert.DoesNotContain("active installation", ResponseJson(commit), StringComparison.OrdinalIgnoreCase);
                Assert.DoesNotContain("idempotencyKey was already", ResponseJson(commit), StringComparison.OrdinalIgnoreCase);
            }
            await observer.ExecuteAsync(
                "UPDATE branches SET status='Inactive' WHERE company_id=@c AND id=@b",
                command => { command.Parameters.AddWithValue("@c", companyId); command.Parameters.AddWithValue("@b", branchA); });
            var blankBranchRow = Row($"RANDOM-INACTIVE-{companyId}", "SCOPE-A", $"RANDOM-INACTIVE-V-{companyId}",
                effective, $"inactive-branch-{companyId}");
            blankBranchRow["branchCode"] = "";
            var inactiveBody = ImportBody(blankBranchRow);
            var inactivePreview = await Invoke("DeviceInstallationsImportPreview", Principal(companyId, branchA),
                inactiveBody, observer, CancellationToken.None);
            Assert.Equal(new[] { "Submitted branch is outside the authorized branch." }, ErrorStrings(inactivePreview));
            var inactiveCommit = await Invoke("DeviceInstallationsImportCommit", Principal(companyId, branchA),
                inactiveBody, observer, new AuditService(observer), CancellationToken.None);
            Assert.Equal(StatusCodes.Status400BadRequest, Status(inactiveCommit));
            Assert.Contains("Submitted branch is outside the authorized branch.", ResponseJson(inactiveCommit), StringComparison.Ordinal);
            Assert.Equal(1, await observer.ScalarLongAsync(
                "SELECT COUNT(*) FROM device_installations WHERE company_id=@c",
                command => command.Parameters.AddWithValue("@c", companyId)));
            Assert.Equal(0, await observer.ScalarLongAsync(
                "SELECT COUNT(*) FROM idempotency_keys WHERE tenant_id=@c AND operation='device.installation.bulk-import'",
                command => command.Parameters.AddWithValue("@c", companyId)));
            Assert.Equal(1, await observer.ScalarLongAsync(
                "SELECT COUNT(*) FROM device_state_transitions WHERE company_id=@c AND reason_code='installation_created'",
                command => command.Parameters.AddWithValue("@c", companyId)));
        }
        finally
        {
            await Cleanup(observer, companyId);
        }
    }

    [Fact]
    public async Task CrossBranchCommissionAndRemoveDirectUrlsAreNonDisclosingAndImmutable()
    {
        var observer = Db();
        var companyId = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() + Random.Shared.Next(33_000_000, 33_900_000);
        await Company(observer, companyId);
        try
        {
            var branchA = await Branch(observer, companyId, "DIRECT-A");
            var branchB = await Branch(observer, companyId, "DIRECT-B");
            var vehicleB = await Vehicle(observer, companyId, branchB, $"DIRECT-B-V-{companyId}");
            var deviceB = await Device(observer, companyId, branchB, $"DIRECT-B-DEV-{companyId}");
            var created = await Invoke("DeviceInstallationCreate", Principal(companyId, branchB), deviceB,
                Body("DeviceInstallationCreateBody", vehicleB, "GPS", true,
                    (DateTimeOffset?)DateTimeOffset.UtcNow.AddMinutes(-2), "Front dashboard", (decimal?)100m,
                    "Governed form", "Cross branch fixture", $"direct-b-{companyId}"),
                observer, new AuditService(observer), CancellationToken.None);
            Assert.Equal(StatusCodes.Status201Created, Status(created));
            var installationId = await observer.ScalarLongAsync(
                "SELECT id FROM device_installations WHERE company_id=@c AND device_id=@d AND effective_to IS NULL",
                command => { command.Parameters.AddWithValue("@c", companyId); command.Parameters.AddWithValue("@d", deviceB); });
            var transitionCount = await observer.ScalarLongAsync(
                "SELECT COUNT(*) FROM device_state_transitions WHERE company_id=@c AND device_id=@d",
                command => { command.Parameters.AddWithValue("@c", companyId); command.Parameters.AddWithValue("@d", deviceB); });

            var commission = await Invoke("DeviceInstallationCommission", Principal(companyId, branchA), deviceB, installationId,
                Body("DeviceInstallationCommissionBody", "failed", "Cross branch rejection", (int?)1),
                observer, new AuditService(observer), CancellationToken.None);
            var remove = await Invoke("DeviceInstallationRemove", Principal(companyId, branchA), deviceB, installationId,
                Body("DeviceInstallationRemoveBody", "Cross branch rejection", (DateTimeOffset?)DateTimeOffset.UtcNow, (int?)1),
                observer, new AuditService(observer), CancellationToken.None);
            Assert.Equal(StatusCodes.Status404NotFound, Status(commission));
            Assert.Equal(StatusCodes.Status404NotFound, Status(remove));
            var installation = await observer.QuerySingleAsync(
                "SELECT status,effective_to,row_version FROM device_installations WHERE company_id=@c AND id=@i",
                command => { command.Parameters.AddWithValue("@c", companyId); command.Parameters.AddWithValue("@i", installationId); });
            Assert.Equal("Installed", installation!["status"]?.ToString());
            Assert.True(installation["effectiveTo"] is null or DBNull);
            Assert.Equal(1, Convert.ToInt32(installation["rowVersion"]));
            Assert.Equal("Installed", (await observer.QuerySingleAsync(
                "SELECT device_state FROM eld_devices WHERE company_id=@c AND id=@d",
                command => { command.Parameters.AddWithValue("@c", companyId); command.Parameters.AddWithValue("@d", deviceB); }))!["deviceState"]?.ToString());
            Assert.Equal(transitionCount, await observer.ScalarLongAsync(
                "SELECT COUNT(*) FROM device_state_transitions WHERE company_id=@c AND device_id=@d",
                command => { command.Parameters.AddWithValue("@c", companyId); command.Parameters.AddWithValue("@d", deviceB); }));
            Assert.Equal(0, await observer.ScalarLongAsync(
                "SELECT COUNT(*) FROM audit_logs WHERE company_id=@c AND action_name IN ('device.installation.commissioned','device.installation.removed')",
                command => command.Parameters.AddWithValue("@c", companyId)));
        }
        finally
        {
            await Cleanup(observer, companyId);
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task TerminalLifecycleFirstRejectsCommissionWithoutMutation(bool revoke)
    {
        var observer = Db();
        var companyId = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() + Random.Shared.Next(34_000_000, 34_900_000);
        await Company(observer, companyId);
        try
        {
            var branch = await Branch(observer, companyId, "COMMISSION-LIFECYCLE-A");
            var vehicle = await Vehicle(observer, companyId, branch, $"COMMISSION-LIFECYCLE-V-{companyId}");
            var device = await Device(observer, companyId, branch, $"COMMISSION-LIFECYCLE-DEV-{companyId}");
            var created = await Invoke("DeviceInstallationCreate", Principal(companyId, branch), device,
                Body("DeviceInstallationCreateBody", vehicle, "GPS", true,
                    (DateTimeOffset?)DateTimeOffset.UtcNow.AddMinutes(-2), "Front dashboard", (decimal?)100m,
                    "Governed form", "Commission lifecycle fixture", $"commission-lifecycle-{companyId}"),
                observer, new AuditService(observer), CancellationToken.None);
            Assert.Equal(StatusCodes.Status201Created, Status(created));
            var installationId = await observer.ScalarLongAsync(
                "SELECT id FROM device_installations WHERE company_id=@c AND device_id=@d AND effective_to IS NULL",
                command => { command.Parameters.AddWithValue("@c", companyId); command.Parameters.AddWithValue("@d", device); });
            var lifecycle = await Invoke(revoke ? "DeviceRevoke" : "DeviceSuspend", Principal(companyId, branch), device,
                observer, new AuditService(observer), CancellationToken.None);
            Assert.Equal(StatusCodes.Status200OK, Status(lifecycle));
            var commission = await Invoke("DeviceInstallationCommission", Principal(companyId, branch), device, installationId,
                Body("DeviceInstallationCommissionBody", "failed", "Terminal lifecycle rejection", (int?)1),
                observer, new AuditService(observer), CancellationToken.None);
            Assert.Equal(StatusCodes.Status409Conflict, Status(commission));
            var installation = await observer.QuerySingleAsync(
                "SELECT status,commissioning_result,row_version FROM device_installations WHERE company_id=@c AND id=@i",
                command => { command.Parameters.AddWithValue("@c", companyId); command.Parameters.AddWithValue("@i", installationId); });
            Assert.Equal("Installed", installation!["status"]?.ToString());
            Assert.True(installation["commissioningResult"] is null or DBNull);
            Assert.Equal(1, Convert.ToInt32(installation["rowVersion"]));
            var deviceState = await observer.QuerySingleAsync(
                "SELECT status,device_state FROM eld_devices WHERE company_id=@c AND id=@d",
                command => { command.Parameters.AddWithValue("@c", companyId); command.Parameters.AddWithValue("@d", device); });
            Assert.Equal(revoke ? "Revoked" : "Suspended", deviceState!["status"]?.ToString());
            Assert.Equal(revoke ? "Decommissioned" : "Suspended", deviceState["deviceState"]?.ToString());
        }
        finally
        {
            await Cleanup(observer, companyId);
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task CommissionFirstThenLifecycleLeavesLaterTerminalStateAuthoritative(bool revoke)
    {
        var observer = Db();
        var companyId = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() + Random.Shared.Next(35_000_000, 35_900_000);
        await Company(observer, companyId);
        try
        {
            var branch = await Branch(observer, companyId, "COMMISSION-ORDER-A");
            var vehicle = await Vehicle(observer, companyId, branch, $"COMMISSION-ORDER-V-{companyId}");
            var device = await Device(observer, companyId, branch, $"COMMISSION-ORDER-DEV-{companyId}");
            var created = await Invoke("DeviceInstallationCreate", Principal(companyId, branch), device,
                Body("DeviceInstallationCreateBody", vehicle, "GPS", true,
                    (DateTimeOffset?)DateTimeOffset.UtcNow.AddMinutes(-2), "Front dashboard", (decimal?)100m,
                    "Governed form", "Commission ordering fixture", $"commission-order-{companyId}"),
                observer, new AuditService(observer), CancellationToken.None);
            Assert.Equal(StatusCodes.Status201Created, Status(created));
            var installationId = await observer.ScalarLongAsync(
                "SELECT id FROM device_installations WHERE company_id=@c AND device_id=@d AND effective_to IS NULL",
                command => { command.Parameters.AddWithValue("@c", companyId); command.Parameters.AddWithValue("@d", device); });
            await using var blockerConnection = new NpgsqlConnection(TestDb.ConnectionString);
            await blockerConnection.OpenAsync();
            await using var blockerTransaction = await blockerConnection.BeginTransactionAsync();
            await using (var command = new NpgsqlCommand(
                "SELECT id FROM device_installations WHERE company_id=@c AND id=@i FOR UPDATE",
                blockerConnection, blockerTransaction))
            {
                command.Parameters.AddWithValue("@c", companyId); command.Parameters.AddWithValue("@i", installationId);
                await command.ExecuteNonQueryAsync();
            }
            var commissionApplication = $"commission-first-{revoke}-{companyId}";
            var commissionDb = Db(commissionApplication);
            var commission = Invoke("DeviceInstallationCommission", Principal(companyId, branch), device, installationId,
                Body("DeviceInstallationCommissionBody", "failed", "Commission ordering failure", (int?)1),
                commissionDb, new AuditService(commissionDb), CancellationToken.None);
            await WaitForDatabaseLock(observer, commissionApplication, blockerConnection.ProcessID, "%FROM device_installations i%");
            var commissionPid = await observer.ScalarLongAsync(
                "SELECT pid FROM pg_stat_activity WHERE application_name=@app",
                command => command.Parameters.AddWithValue("@app", commissionApplication));
            var lifecycleApplication = $"lifecycle-after-commission-{revoke}-{companyId}";
            var lifecycleDb = Db(lifecycleApplication);
            var lifecycle = Invoke(revoke ? "DeviceRevoke" : "DeviceSuspend", Principal(companyId, branch), device,
                lifecycleDb, new AuditService(lifecycleDb), CancellationToken.None);
            await WaitForDatabaseLock(observer, lifecycleApplication, checked((int)commissionPid), "%pg_advisory_xact_lock%");
            await blockerTransaction.CommitAsync();
            Assert.Equal(StatusCodes.Status200OK, Status(await commission));
            Assert.Equal(StatusCodes.Status200OK, Status(await lifecycle));
            Assert.Equal("Failed", (await observer.QuerySingleAsync(
                "SELECT status FROM device_installations WHERE company_id=@c AND id=@i",
                command => { command.Parameters.AddWithValue("@c", companyId); command.Parameters.AddWithValue("@i", installationId); }))!["status"]?.ToString());
            var finalState = await observer.QuerySingleAsync(
                "SELECT status,device_state FROM eld_devices WHERE company_id=@c AND id=@d",
                command => { command.Parameters.AddWithValue("@c", companyId); command.Parameters.AddWithValue("@d", device); });
            Assert.Equal(revoke ? "Revoked" : "Suspended", finalState!["status"]?.ToString());
            Assert.Equal(revoke ? "Decommissioned" : "Suspended", finalState["deviceState"]?.ToString());
        }
        finally
        {
            await Cleanup(observer, companyId);
        }
    }

    private static Dictionary<string, object?> Row(string serial, string branch, string vehicle, string effective, string key,
        string reason = "Initial governed installation") => new()
    {
        ["deviceSerial"] = serial, ["branchCode"] = branch, ["vehicleCode"] = vehicle,
        ["deviceRole"] = "GPS", ["isPrimary"] = "true", ["effectiveFrom"] = effective,
        ["installationLocation"] = "Front dashboard", ["odometerAtInstallation"] = "100",
        ["commissioningMethod"] = "CSV onboarding", ["assignmentReason"] = reason,
        ["idempotencyKey"] = key
    };

    private static Dictionary<string, object?> ImportBody(params Dictionary<string, object?>[] rows) =>
        new() { ["rows"] = JsonSerializer.SerializeToElement(rows) };

    private static object Body(string nestedType, params object?[] args)
    {
        var type = typeof(EndpointMappings).GetNestedType(nestedType, BindingFlags.NonPublic)!;
        return Activator.CreateInstance(type, args)!;
    }

    private static string ResponseJson(IResult result) => JsonSerializer.Serialize(
        Assert.IsAssignableFrom<IValueHttpResult>(result).Value);

    private static string[] ErrorStrings(IResult result)
    {
        using var document = JsonDocument.Parse(ResponseJson(result));
        var errors = new List<string>();
        Collect(document.RootElement);
        return errors.OrderBy(value => value, StringComparer.Ordinal).ToArray();

        void Collect(JsonElement element)
        {
            if (element.ValueKind == JsonValueKind.Object)
            {
                foreach (var property in element.EnumerateObject())
                {
                    if (property.Name.Equals("errors", StringComparison.OrdinalIgnoreCase) &&
                        property.Value.ValueKind == JsonValueKind.Array)
                    {
                        errors.AddRange(property.Value.EnumerateArray()
                            .Where(value => value.ValueKind == JsonValueKind.String)
                            .Select(value => value.GetString()!));
                    }
                    Collect(property.Value);
                }
            }
            else if (element.ValueKind == JsonValueKind.Array)
            {
                foreach (var child in element.EnumerateArray()) Collect(child);
            }
        }
    }

    private static async Task Company(Database db, long id) => await db.ExecuteAsync(
        "INSERT INTO companies(id,company_code,name,industry) OVERRIDING SYSTEM VALUE VALUES (@c,@code,@name,'transport')",
        c => { c.Parameters.AddWithValue("@c", id); c.Parameters.AddWithValue("@code", $"BULK-{id}"); c.Parameters.AddWithValue("@name", $"Bulk installation tenant {id}"); });
    private static Task<long> Branch(Database db, long company, string code) => db.InsertAsync(
        "INSERT INTO branches(company_id,branch_code,name,status) VALUES (@c,@code,@name,'Active')",
        c => { c.Parameters.AddWithValue("@c", company); c.Parameters.AddWithValue("@code", code); c.Parameters.AddWithValue("@name", code); });
    private static Task<long> Vehicle(Database db, long company, long branch, string code) => db.InsertAsync(
        @"INSERT INTO vehicles(company_id,branch_id,vehicle_code,type,vin_exception_type,alternate_identifier,status,availability_status,out_of_service)
          VALUES (@c,@b,@code,'Truck','manufacturer-serial-number',@alternate,'Available','available',FALSE)",
        c => { c.Parameters.AddWithValue("@c", company); c.Parameters.AddWithValue("@b", branch); c.Parameters.AddWithValue("@code", code); c.Parameters.AddWithValue("@alternate", $"ALT-{code}"); });
    private static Task<long> Device(Database db, long company, long branch, string serial) => db.InsertAsync(
        @"INSERT INTO eld_devices(company_id,branch_id,device_serial,status,device_state,api_key_hash,hmac_secret_encrypted,hmac_key_version,created_at)
          VALUES (@c,@b,@serial,'Active','Registered',encode(sha256(@serial::bytea),'hex'),repeat('b',32),1,NOW())",
        c => { c.Parameters.AddWithValue("@c", company); c.Parameters.AddWithValue("@b", branch); c.Parameters.AddWithValue("@serial", serial); });
    private static Database Db(string? applicationName = null)
    {
        var builder = new NpgsqlConnectionStringBuilder(TestDb.ConnectionString);
        if (applicationName is not null) builder.ApplicationName = applicationName;
        return new Database(new ConfigurationBuilder().AddInMemoryCollection(
            new Dictionary<string, string?> { ["ConnectionStrings:DefaultConnection"] = builder.ConnectionString }).Build());
    }
    private static Database ProtectedDb() => new(new ConfigurationBuilder().AddInMemoryCollection(
        new Dictionary<string, string?>
        {
            ["ConnectionStrings:DefaultConnection"] = TestDb.AppConnectionString,
            ["ConnectionStrings:SystemConnection"] = TestDb.SystemConnectionString,
            ["Rls:EnforceTenantContext"] = "true",
            ["ASPNETCORE_ENVIRONMENT"] = "Staging"
        }).Build());
    private static async Task AssertNoInstallationImportMutation(Database db, long companyId)
    {
        Assert.Equal(0, await db.ScalarLongAsync(
            "SELECT COUNT(*) FROM device_installations WHERE company_id=@c AND source='operator'",
            command => command.Parameters.AddWithValue("@c", companyId)));
        Assert.Equal(0, await db.ScalarLongAsync(
            "SELECT COUNT(*) FROM device_state_transitions WHERE company_id=@c AND reason_code='installation_created'",
            command => command.Parameters.AddWithValue("@c", companyId)));
        Assert.Equal(0, await db.ScalarLongAsync(
            "SELECT COUNT(*) FROM idempotency_keys WHERE tenant_id=@c AND operation='device.installation.bulk-import'",
            command => command.Parameters.AddWithValue("@c", companyId)));
        Assert.Equal(0, await db.ScalarLongAsync(
            "SELECT COUNT(*) FROM audit_logs WHERE company_id=@c AND action_name IN ('device.installation.created','device.installations.imported')",
            command => command.Parameters.AddWithValue("@c", companyId)));
    }
    private static async Task WaitForDatabaseLock(
        Database observer, string applicationName, int? blockerPid = null, string? queryPattern = null)
    {
        for (var attempt = 0; attempt < 200; attempt++)
        {
            var waiting = await observer.ScalarLongAsync(
                @"SELECT COUNT(*) FROM pg_stat_activity a
                   WHERE a.application_name=@app AND a.wait_event_type='Lock'
                     AND (@blocker::INT IS NULL OR @blocker=ANY(pg_blocking_pids(a.pid)))
                     AND (@query_pattern::TEXT IS NULL OR a.query ILIKE @query_pattern)",
                c =>
                {
                    c.Parameters.AddWithValue("@app", applicationName);
                    c.Parameters.AddWithValue("@blocker", NpgsqlTypes.NpgsqlDbType.Integer, (object?)blockerPid ?? DBNull.Value);
                    c.Parameters.AddWithValue("@query_pattern", NpgsqlTypes.NpgsqlDbType.Text, (object?)queryPattern ?? DBNull.Value);
                });
            if (waiting > 0) return;
            await Task.Delay(20);
        }
        Assert.Fail($"Database session '{applicationName}' did not reach the expected lock wait.");
    }
    private static async Task Cleanup(Database db, long companyId)
    {
        foreach (var sql in new[]
        {
            "DELETE FROM audit_logs WHERE company_id=@c", "DELETE FROM device_state_transitions WHERE company_id=@c",
            "DELETE FROM idempotency_keys WHERE tenant_id=@c", "DELETE FROM device_installations WHERE company_id=@c",
            "DELETE FROM device_installation_quarantine WHERE company_id=@c",
            "DELETE FROM eld_devices WHERE company_id=@c", "DELETE FROM vehicles WHERE company_id=@c",
            "DELETE FROM branches WHERE company_id=@c", "DELETE FROM companies WHERE id=@c"
        }) await db.ExecuteAsync(sql, c => c.Parameters.AddWithValue("@c", companyId));
    }
    private static DefaultHttpContext Principal(long company, long? branch)
    {
        var http = new DefaultHttpContext { TraceIdentifier = $"bulk-install-{Guid.NewGuid():N}" };
        http.Items[EndpointMappings.AuthCompanyIdItemKey] = company;
        if (branch.HasValue) http.Items[EndpointMappings.AuthBranchIdItemKey] = branch.Value;
        http.Items[EndpointMappings.AuthUserIdItemKey] = 42L;
        http.Items[EndpointMappings.AuthRoleItemKey] = "Fleet Manager";
        http.Items[EndpointMappings.AuthPermissionsItemKey] = new[] { "telemetry.devices.manage", "telemetry.devices.read" };
        return http;
    }
    private static async Task<IResult> Invoke(string name, params object?[] args)
    {
        var method = typeof(EndpointMappings).GetMethod(name, BindingFlags.NonPublic | BindingFlags.Static)!;
        return await ((Task<IResult>)method.Invoke(null, args)!);
    }
    private static int? Status(IResult result) => Assert.IsAssignableFrom<IStatusCodeHttpResult>(result).StatusCode;
}
