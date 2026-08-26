using System.Reflection;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Npgsql;
using Opstrax.Api.Controllers;
using Opstrax.Api.Data;
using Opstrax.Api.Services;

namespace Opstrax.Tests;

[Trait("Category", "Integration")]
[Collection("fleet-identity-schema")]
public sealed class FleetIdentityInstallationPostgresTests
{
    [Fact]
    public async Task InstallationLifecycleRejectsFutureEffectiveTimesBeforeMutation()
    {
        var http = Principal(1001, userId: 42);
        var future = DateTimeOffset.UtcNow.AddMinutes(1);
        var db = Db();
        var audit = new AuditService(db);

        var create = await Invoke("DeviceInstallationCreate", http, 2001L,
            Body("DeviceInstallationCreateBody", 3001L, "GPS", true, (DateTimeOffset?)future,
                null, null, null, null, null), db, audit, CancellationToken.None);
        Assert.Equal(StatusCodes.Status400BadRequest, Status(create));

        var remove = await Invoke("DeviceInstallationRemove", http, 2001L, 4001L,
            Body("DeviceInstallationRemoveBody", "scheduled removal", (DateTimeOffset?)future, (int?)1),
            db, audit, CancellationToken.None);
        Assert.Equal(StatusCodes.Status400BadRequest, Status(remove));

        var transfer = await Invoke("DeviceInstallationTransfer", http, 2001L,
            Body("DeviceInstallationTransferBody", 3002L, (long?)4001L, "replace", "route change",
                "GPS", true, (DateTimeOffset?)future, null, null, null, (int?)1, null),
            db, audit, CancellationToken.None);
        Assert.Equal(StatusCodes.Status400BadRequest, Status(transfer));

        var unversionedRemove = await Invoke("DeviceInstallationRemove", http, 2001L, 4001L,
            Body("DeviceInstallationRemoveBody", "remove", null, null), db, audit, CancellationToken.None);
        Assert.Equal(StatusCodes.Status400BadRequest, Status(unversionedRemove));

        var unversionedTransfer = await Invoke("DeviceInstallationTransfer", http, 2001L,
            Body("DeviceInstallationTransferBody", 3002L, null, "replace", "route change",
                "GPS", true, null, null, null, null, null, null), db, audit, CancellationToken.None);
        Assert.Equal(StatusCodes.Status400BadRequest, Status(unversionedTransfer));
    }

    [Fact]
    public async Task SimultaneousInstallationsCannotBindOneDeviceToTwoVehicles()
    {
        var setupDb = Db();
        var companyId = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() + Random.Shared.Next(1_000_000, 1_900_000);
        await Company(setupDb, companyId, "RACE");
        try
        {
            var branch = await Branch(setupDb, companyId, "RACE");
            var firstVehicle = await Vehicle(setupDb, companyId, branch, $"RACE-A-{companyId}", vin: "1HGCM82633A004352");
            var secondVehicle = await Vehicle(setupDb, companyId, branch, $"RACE-B-{companyId}", alternate: $"RACE-MFG-{companyId}");
            var deviceId = await Device(setupDb, companyId, $"RACE-SERIAL-{companyId}");
            var start = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

            async Task<IResult> Attempt(long vehicleId, string key)
            {
                var db = Db();
                await start.Task;
                return await Invoke("DeviceInstallationCreate", Principal(companyId, 42), deviceId,
                    Body("DeviceInstallationCreateBody", vehicleId, "GPS", true, DateTimeOffset.UtcNow.AddSeconds(-1),
                        "cab", 1m, "heartbeat", "race proof", key), db, new AuditService(db), CancellationToken.None);
            }

            var first = Attempt(firstVehicle, $"race-a-{Guid.NewGuid():N}");
            var second = Attempt(secondVehicle, $"race-b-{Guid.NewGuid():N}");
            start.SetResult();
            var statuses = (await Task.WhenAll(first, second)).Select(Status).OrderBy(value => value).ToArray();
            Assert.Equal(new int?[] { StatusCodes.Status201Created, StatusCodes.Status409Conflict }, statuses);
            Assert.Equal(1, await setupDb.ScalarLongAsync(
                "SELECT COUNT(*) FROM device_installations WHERE company_id=@c AND device_id=@d AND effective_to IS NULL AND status IN ('Installed','Verified')",
                c => { c.Parameters.AddWithValue("@c", companyId); c.Parameters.AddWithValue("@d", deviceId); }));
        }
        finally
        {
            foreach (var sql in new[]
            {
                "DELETE FROM audit_logs WHERE company_id=@c", "DELETE FROM device_state_transitions WHERE company_id=@c",
                "DELETE FROM device_installations WHERE company_id=@c", "DELETE FROM eld_devices WHERE company_id=@c",
                "DELETE FROM vehicles WHERE company_id=@c", "DELETE FROM branches WHERE company_id=@c",
                "DELETE FROM companies WHERE id=@c"
            }) await setupDb.ExecuteAsync(sql, c => c.Parameters.AddWithValue("@c", companyId));
        }
    }

    [Fact]
    public async Task InstallationTransferIsTemporalTenantSafeIdempotentAndRefreshPersistent()
    {
        var db = Db();
        var companyId = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() + Random.Shared.Next(100_000,900_000);
        var otherCompanyId = companyId + 1;
        await Company(db,companyId,"A");
        await Company(db,otherCompanyId,"B");
        try
        {
            var branchA = await Branch(db,companyId,"A");
            var branchB = await Branch(db,companyId,"B");
            var otherBranch = await Branch(db,otherCompanyId,"X");
            var vehicleA = await Vehicle(db,companyId,branchA,$"UNIT-A-{companyId}",vin:"1HGCM82633A004352");
            var vehicleB = await Vehicle(db,companyId,branchB,$"UNIT-B-{companyId}",alternate:$"MFG-{companyId}-B");
            var otherVehicle = await Vehicle(db,otherCompanyId,otherBranch,$"UNIT-X-{companyId}",alternate:$"MFG-{companyId}-X");
            var deviceId = await Device(db,companyId,$"SERIAL-{companyId}");
            var http = Principal(companyId,userId:42);
            var audit = new AuditService(db);
            var installKey = $"install-{Guid.NewGuid():N}";
            var installedAt = DateTimeOffset.UtcNow.AddMinutes(-2);

            var created = await Invoke("DeviceInstallationCreate",http,deviceId,
                Body("DeviceInstallationCreateBody",vehicleA,"GPS",true,installedAt,"cab",123m,"heartbeat", "pilot install",installKey),
                db,audit,CancellationToken.None);
            Assert.Equal(StatusCodes.Status201Created,Status(created));
            var first = await db.QuerySingleAsync(
                "SELECT id,row_version FROM device_installations WHERE company_id=@c AND device_id=@d AND effective_to IS NULL",
                c => { c.Parameters.AddWithValue("@c",companyId); c.Parameters.AddWithValue("@d",deviceId); });
            Assert.NotNull(first);
            var firstId = Convert.ToInt64(first!["id"]);

            var replay = await Invoke("DeviceInstallationCreate",http,deviceId,
                Body("DeviceInstallationCreateBody",vehicleA,"GPS",true,installedAt,"cab",123m,"heartbeat", "pilot install",installKey),
                db,audit,CancellationToken.None);
            Assert.Equal(StatusCodes.Status200OK,Status(replay));
            Assert.Equal(1,await Count(db,companyId,deviceId));

            var keyReuse = await Invoke("DeviceInstallationCreate",http,deviceId,
                Body("DeviceInstallationCreateBody",vehicleB,"GPS",true,installedAt,"cab",123m,"heartbeat", "wrong reuse",installKey),
                db,audit,CancellationToken.None);
            Assert.Equal(StatusCodes.Status409Conflict,Status(keyReuse));

            var crossTenant = await Invoke("DeviceInstallationCreate",http,deviceId,
                Body("DeviceInstallationCreateBody",otherVehicle,"GPS",true,installedAt,"cab",123m,"heartbeat", "cross tenant",$"cross-{Guid.NewGuid():N}"),
                db,audit,CancellationToken.None);
            Assert.Equal(StatusCodes.Status400BadRequest,Status(crossTenant));

            var transferKey = $"transfer-{Guid.NewGuid():N}";
            var transferAt = DateTimeOffset.UtcNow.AddMilliseconds(-50);
            async Task<IResult> TransferAttempt()
            {
                var attemptDb = Db();
                return await Invoke("DeviceInstallationTransfer",Principal(companyId,userId:42),deviceId,
                    Body("DeviceInstallationTransferBody",vehicleB,(long?)firstId,"replace vehicle","new route", "GPS",true,
                        (DateTimeOffset?)transferAt,"yard bay 2",57838m,"heartbeat",(int?)1,transferKey),
                    attemptDb,new AuditService(attemptDb),CancellationToken.None);
            }
            var concurrentResults = await Task.WhenAll(TransferAttempt(), TransferAttempt());
            Assert.All(concurrentResults, result => Assert.Equal(StatusCodes.Status200OK, Status(result)));
            var history = await db.QueryAsync(
                "SELECT id,vehicle_id,status,effective_from,effective_to,replaced_installation_id,odometer_at_installation FROM device_installations WHERE company_id=@c AND device_id=@d ORDER BY effective_from,id",
                c => { c.Parameters.AddWithValue("@c",companyId); c.Parameters.AddWithValue("@d",deviceId); });
            Assert.Equal(2,history.Count);
            Assert.Equal("Removed",history[0]["status"]);
            Assert.NotNull(history[0]["effectiveTo"]);
            Assert.Equal(vehicleA,Convert.ToInt64(history[0]["vehicleId"]));
            Assert.Equal("Installed",history[1]["status"]);
            Assert.Equal(vehicleB,Convert.ToInt64(history[1]["vehicleId"]));
            Assert.Equal(firstId,Convert.ToInt64(history[1]["replacedInstallationId"]));
            Assert.Equal(57838m, Convert.ToDecimal(history[1]["odometerAtInstallation"]));
            Assert.Equal(vehicleB,await db.ScalarLongAsync(
                "SELECT vehicle_id FROM eld_devices WHERE company_id=@c AND id=@d",
                c => { c.Parameters.AddWithValue("@c",companyId); c.Parameters.AddWithValue("@d",deviceId); }));

            var transferReplay = await Invoke("DeviceInstallationTransfer",http,deviceId,
                Body("DeviceInstallationTransferBody",vehicleB,(long?)firstId,"replace vehicle","new route", "GPS",true,
                    (DateTimeOffset?)transferAt,"yard bay 2",57838m,"heartbeat",(int?)1,transferKey),
                db,audit,CancellationToken.None);
            Assert.Equal(StatusCodes.Status200OK,Status(transferReplay));
            Assert.Equal(2,await Count(db,companyId,deviceId));
            var changedReplay = await Invoke("DeviceInstallationTransfer",http,deviceId,
                Body("DeviceInstallationTransferBody",vehicleB,(long?)firstId,"replace vehicle","new route", "GPS",true,
                    (DateTimeOffset?)transferAt,"yard bay 2",57839m,"heartbeat",(int?)1,transferKey),
                db,audit,CancellationToken.None);
            Assert.Equal(StatusCodes.Status409Conflict, Status(changedReplay));
            Assert.Equal(2,await Count(db,companyId,deviceId));
            Assert.Equal(2,await db.ScalarLongAsync(
                "SELECT COUNT(*) FROM audit_logs WHERE company_id=@c AND action_name IN ('device.installation.created','device.installation.transferred')",
                c => c.Parameters.AddWithValue("@c",companyId)));
        }
        finally
        {
            foreach (var company in new[] { companyId,otherCompanyId })
            foreach (var sql in new[]
            {
                "DELETE FROM audit_logs WHERE company_id=@c","DELETE FROM device_state_transitions WHERE company_id=@c",
                "DELETE FROM device_installations WHERE company_id=@c","DELETE FROM eld_devices WHERE company_id=@c",
                "DELETE FROM vehicles WHERE company_id=@c","DELETE FROM branches WHERE company_id=@c",
                "DELETE FROM idempotency_keys WHERE tenant_id=@c",
                "DELETE FROM companies WHERE id=@c"
            })
                await db.ExecuteAsync(sql,c => c.Parameters.AddWithValue("@c",company));
        }
    }

    [Fact]
    public async Task RuntimeAppRoleTransfersWithoutCanonicalTelemetryPrivilegeUsingSystemEvidenceLane()
    {
        var owner = Db();
        var runtime = Db(TestDb.AppConnectionString, true, TestDb.SystemConnectionString);
        var companyId = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() + Random.Shared.Next(950_000, 999_000);
        var otherCompanyId = companyId + 1;
        await Company(owner, companyId, "RUNTIME-LANE");
        await Company(owner, otherCompanyId, "OTHER-LANE");
        try
        {
            Assert.Equal(0, await owner.ScalarLongAsync(
                "SELECT CASE WHEN has_table_privilege('opstrax_app','canonical_telemetry_events','SELECT') THEN 1 ELSE 0 END"));
            Assert.Equal(1, await owner.ScalarLongAsync(
                "SELECT CASE WHEN has_table_privilege('opstrax_system','canonical_telemetry_events','SELECT') THEN 1 ELSE 0 END"));

            var branch = await Branch(owner, companyId, "RUNTIME-LANE");
            var sourceVehicle = await Vehicle(owner, companyId, branch, $"RUNTIME-A-{companyId}", alternate: $"RUNTIME-MFG-A-{companyId}");
            var targetVehicle = await Vehicle(owner, companyId, branch, $"RUNTIME-B-{companyId}", alternate: $"RUNTIME-MFG-B-{companyId}");
            var deviceId = await Device(owner, companyId, $"RUNTIME-DEVICE-{companyId}");
            var installedAt = DateTimeOffset.UtcNow.AddMinutes(-10);
            var installationId = await owner.InsertAsync(
                @"INSERT INTO device_installations
                    (company_id,branch_id,device_id,vehicle_id,status,device_role,is_primary,effective_from,installed_at,source)
                  VALUES (@c,@b,@d,@v,'Installed','GPS',TRUE,@at,@at,'test')",
                c =>
                {
                    c.Parameters.AddWithValue("@c", companyId); c.Parameters.AddWithValue("@b", branch);
                    c.Parameters.AddWithValue("@d", deviceId); c.Parameters.AddWithValue("@v", sourceVehicle);
                    c.Parameters.AddWithValue("@at", installedAt);
                });
            await owner.ExecuteAsync(
                @"INSERT INTO canonical_telemetry_events
                    (company_id,device_id,installation_id,event_type,event_time)
                  VALUES (@other,@d,@i,'device.heartbeat',NOW())",
                c =>
                {
                    c.Parameters.AddWithValue("@other", otherCompanyId);
                    c.Parameters.AddWithValue("@d", deviceId);
                    c.Parameters.AddWithValue("@i", installationId);
                });

            // Execute with the same split identities used in production. The app role
            // performs the tenant mutation while the system role returns only the
            // bounded canonical-event existence result.
            var transferred = await Invoke("DeviceInstallationTransfer", Principal(companyId, 42), deviceId,
                Body("DeviceInstallationTransferBody", targetVehicle, (long?)installationId,
                    "replace vehicle", "new route", "GPS", true, (DateTimeOffset?)DateTimeOffset.UtcNow.AddSeconds(-1),
                    "yard bay 2", 49919m, "heartbeat", (int?)1, $"runtime-transfer-{Guid.NewGuid():N}"),
                runtime, new AuditService(runtime), CancellationToken.None);

            Assert.Equal(StatusCodes.Status200OK, Status(transferred));
            Assert.Equal(2, await Count(owner, companyId, deviceId));
            Assert.Equal(targetVehicle, await owner.ScalarLongAsync(
                "SELECT vehicle_id FROM device_installations WHERE company_id=@c AND device_id=@d AND effective_to IS NULL",
                c => { c.Parameters.AddWithValue("@c", companyId); c.Parameters.AddWithValue("@d", deviceId); }));
        }
        finally
        {
            foreach (var company in new[] { companyId, otherCompanyId })
            foreach (var sql in new[]
            {
                "DELETE FROM canonical_telemetry_events WHERE company_id=@c",
                "DELETE FROM audit_logs WHERE company_id=@c", "DELETE FROM device_state_transitions WHERE company_id=@c",
                "DELETE FROM idempotency_keys WHERE tenant_id=@c", "DELETE FROM device_installations WHERE company_id=@c",
                "DELETE FROM eld_devices WHERE company_id=@c", "DELETE FROM vehicles WHERE company_id=@c",
                "DELETE FROM branches WHERE company_id=@c", "DELETE FROM companies WHERE id=@c"
            }) await owner.ExecuteAsync(sql, c => c.Parameters.AddWithValue("@c", company));
        }
    }

    [Fact]
    public async Task TransferWaitsForInFlightCanonicalEventThenRechecksAndRejects()
    {
        var owner = Db();
        var applicationName = $"transfer-race-{Guid.NewGuid():N}";
        var appConnection = new NpgsqlConnectionStringBuilder(TestDb.AppConnectionString)
        {
            ApplicationName = applicationName
        }.ConnectionString;
        var runtime = Db(appConnection, true, TestDb.SystemConnectionString);
        var companyId = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() + Random.Shared.Next(1_000_000, 1_049_000);
        await Company(owner, companyId, "INGEST-RACE");
        try
        {
            var branch = await Branch(owner, companyId, "INGEST-RACE");
            var sourceVehicle = await Vehicle(owner, companyId, branch, $"INGEST-A-{companyId}", alternate: $"INGEST-MFG-A-{companyId}");
            var targetVehicle = await Vehicle(owner, companyId, branch, $"INGEST-B-{companyId}", alternate: $"INGEST-MFG-B-{companyId}");
            var deviceId = await Device(owner, companyId, $"INGEST-DEVICE-{companyId}");
            var installationId = await owner.InsertAsync(
                @"INSERT INTO device_installations
                    (company_id,branch_id,device_id,vehicle_id,status,device_role,is_primary,effective_from,installed_at,source)
                  VALUES (@c,@b,@d,@v,'Installed','GPS',TRUE,NOW()-INTERVAL '10 minutes',NOW()-INTERVAL '10 minutes','test')",
                c =>
                {
                    c.Parameters.AddWithValue("@c", companyId); c.Parameters.AddWithValue("@b", branch);
                    c.Parameters.AddWithValue("@d", deviceId); c.Parameters.AddWithValue("@v", sourceVehicle);
                });
            var transferAt = DateTimeOffset.UtcNow.AddSeconds(-2);

            await using var ingestionConnection = new NpgsqlConnection(TestDb.SystemConnectionString);
            await ingestionConnection.OpenAsync();
            await using var ingestionTransaction = await ingestionConnection.BeginTransactionAsync();
            await using (var lockDevice = new NpgsqlCommand(
                "SELECT id FROM eld_devices WHERE id=@d AND company_id=@c AND deleted_at IS NULL FOR UPDATE",
                ingestionConnection, ingestionTransaction))
            {
                lockDevice.Parameters.AddWithValue("@d", deviceId);
                lockDevice.Parameters.AddWithValue("@c", companyId);
                Assert.Equal(deviceId, Convert.ToInt64(await lockDevice.ExecuteScalarAsync()));
            }

            var transferTask = Invoke("DeviceInstallationTransfer", Principal(companyId, 42), deviceId,
                Body("DeviceInstallationTransferBody", targetVehicle, (long?)installationId,
                    "replace vehicle", "new route", "GPS", true, (DateTimeOffset?)transferAt,
                    "yard bay 2", 49919m, "heartbeat", (int?)1, $"race-transfer-{Guid.NewGuid():N}"),
                runtime, new AuditService(runtime), CancellationToken.None);

            var observedWaiting = false;
            for (var attempt = 0; attempt < 100 && !observedWaiting; attempt++)
            {
                observedWaiting = await owner.ScalarLongAsync(
                    @"SELECT COUNT(*) FROM pg_stat_activity
                       WHERE application_name=@app AND wait_event_type='Lock'
                         AND query LIKE '%eld_devices%' AND query LIKE '%FOR UPDATE%'",
                    c => c.Parameters.AddWithValue("@app", applicationName)) != 0;
                if (!observedWaiting) await Task.Delay(20);
            }
            Assert.True(observedWaiting, "Transfer did not wait on the ingestion device row lock");
            Assert.False(transferTask.IsCompleted, "Transfer completed before in-flight telemetry committed");

            await using (var insertEvent = new NpgsqlCommand(
                @"INSERT INTO canonical_telemetry_events
                    (company_id,vehicle_id,device_id,installation_id,event_type,event_time)
                  VALUES (@c,@v,@d,@i,'device.heartbeat',@at)",
                ingestionConnection, ingestionTransaction))
            {
                insertEvent.Parameters.AddWithValue("@c", companyId);
                insertEvent.Parameters.AddWithValue("@v", sourceVehicle);
                insertEvent.Parameters.AddWithValue("@d", deviceId);
                insertEvent.Parameters.AddWithValue("@i", installationId);
                insertEvent.Parameters.AddWithValue("@at", DateTimeOffset.UtcNow);
                await insertEvent.ExecuteNonQueryAsync();
            }
            await ingestionTransaction.CommitAsync();

            var transfer = await transferTask.WaitAsync(TimeSpan.FromSeconds(10));
            Assert.Equal(StatusCodes.Status409Conflict, Status(transfer));
            Assert.Equal(1, await Count(owner, companyId, deviceId));
            Assert.Equal(sourceVehicle, await owner.ScalarLongAsync(
                "SELECT vehicle_id FROM device_installations WHERE company_id=@c AND device_id=@d AND effective_to IS NULL",
                c => { c.Parameters.AddWithValue("@c", companyId); c.Parameters.AddWithValue("@d", deviceId); }));
            Assert.Equal(0, await owner.ScalarLongAsync(
                "SELECT COUNT(*) FROM idempotency_keys WHERE tenant_id=@c AND operation='device.installation.transfer'",
                c => c.Parameters.AddWithValue("@c", companyId)));
        }
        finally
        {
            foreach (var sql in new[]
            {
                "DELETE FROM canonical_telemetry_events WHERE company_id=@c",
                "DELETE FROM audit_logs WHERE company_id=@c", "DELETE FROM device_state_transitions WHERE company_id=@c",
                "DELETE FROM idempotency_keys WHERE tenant_id=@c", "DELETE FROM device_installations WHERE company_id=@c",
                "DELETE FROM eld_devices WHERE company_id=@c", "DELETE FROM vehicles WHERE company_id=@c",
                "DELETE FROM branches WHERE company_id=@c", "DELETE FROM companies WHERE id=@c"
            }) await owner.ExecuteAsync(sql, c => c.Parameters.AddWithValue("@c", companyId));
        }
    }

    [Fact]
    public async Task CanonicalEvidencePrivilegeFailureReturnsSafeUnavailableAndRollsBack()
    {
        var owner = Db();
        // Deliberately misconfigure only the test system lane as the restricted app
        // identity. Its canonical SELECT fails with 42501 without changing shared ACLs.
        var runtimeWithoutCanonicalLane = Db(TestDb.AppConnectionString, true, TestDb.AppConnectionString);
        var companyId = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() + Random.Shared.Next(1_050_000, 1_099_000);
        await Company(owner, companyId, "PRIVILEGE-ROLLBACK");
        try
        {
            var branch = await Branch(owner, companyId, "PRIVILEGE-ROLLBACK");
            var sourceVehicle = await Vehicle(owner, companyId, branch, $"ROLLBACK-A-{companyId}", alternate: $"ROLLBACK-MFG-A-{companyId}");
            var targetVehicle = await Vehicle(owner, companyId, branch, $"ROLLBACK-B-{companyId}", alternate: $"ROLLBACK-MFG-B-{companyId}");
            var deviceId = await Device(owner, companyId, $"ROLLBACK-DEVICE-{companyId}");
            var installationId = await owner.InsertAsync(
                @"INSERT INTO device_installations
                    (company_id,branch_id,device_id,vehicle_id,status,device_role,is_primary,effective_from,installed_at,source)
                  VALUES (@c,@b,@d,@v,'Installed','GPS',TRUE,NOW()-INTERVAL '10 minutes',NOW()-INTERVAL '10 minutes','test')",
                c =>
                {
                    c.Parameters.AddWithValue("@c", companyId); c.Parameters.AddWithValue("@b", branch);
                    c.Parameters.AddWithValue("@d", deviceId); c.Parameters.AddWithValue("@v", sourceVehicle);
                });
            var idempotencyKey = $"privilege-transfer-{Guid.NewGuid():N}";

            var transfer = await Invoke("DeviceInstallationTransfer", Principal(companyId, 42), deviceId,
                Body("DeviceInstallationTransferBody", targetVehicle, (long?)installationId,
                    "replace vehicle", "new route", "GPS", true, (DateTimeOffset?)DateTimeOffset.UtcNow.AddSeconds(-1),
                    "yard bay 2", 49919m, "heartbeat", (int?)1, idempotencyKey),
                runtimeWithoutCanonicalLane, new AuditService(runtimeWithoutCanonicalLane), CancellationToken.None);

            Assert.Equal(StatusCodes.Status503ServiceUnavailable, Status(transfer));
            Assert.Equal(1, await Count(owner, companyId, deviceId));
            Assert.Equal(sourceVehicle, await owner.ScalarLongAsync(
                "SELECT vehicle_id FROM device_installations WHERE company_id=@c AND device_id=@d AND effective_to IS NULL",
                c => { c.Parameters.AddWithValue("@c", companyId); c.Parameters.AddWithValue("@d", deviceId); }));
            Assert.Equal(0, await owner.ScalarLongAsync(
                "SELECT COUNT(*) FROM idempotency_keys WHERE tenant_id=@c AND idempotency_key=@key",
                c => { c.Parameters.AddWithValue("@c", companyId); c.Parameters.AddWithValue("@key", idempotencyKey); }));
        }
        finally
        {
            foreach (var sql in new[]
            {
                "DELETE FROM audit_logs WHERE company_id=@c", "DELETE FROM device_state_transitions WHERE company_id=@c",
                "DELETE FROM idempotency_keys WHERE tenant_id=@c", "DELETE FROM device_installations WHERE company_id=@c",
                "DELETE FROM eld_devices WHERE company_id=@c", "DELETE FROM vehicles WHERE company_id=@c",
                "DELETE FROM branches WHERE company_id=@c", "DELETE FROM companies WHERE id=@c"
            }) await owner.ExecuteAsync(sql, c => c.Parameters.AddWithValue("@c", companyId));
        }
    }

    [Fact]
    public async Task CompatibilityProjectionUsesMutationTimeWhenTransferStartsAfterTransaction()
    {
        var db = Db();
        var companyId = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() + Random.Shared.Next(900_000, 990_000);
        await Company(db, companyId, "PROJECTION-CLOCK");
        try
        {
            var branch = await Branch(db, companyId, "PROJECTION-CLOCK");
            var vehicleA = await Vehicle(db, companyId, branch, $"CLOCK-A-{companyId}", alternate: $"CLOCK-MFG-{companyId}-A");
            var vehicleB = await Vehicle(db, companyId, branch, $"CLOCK-B-{companyId}", alternate: $"CLOCK-MFG-{companyId}-B");
            var deviceId = await Device(db, companyId, $"CLOCK-SERIAL-{companyId}");
            var firstId = await db.InsertAsync(
                @"INSERT INTO device_installations
                    (company_id,branch_id,device_id,vehicle_id,status,device_role,is_primary,effective_from,installed_at,source)
                  VALUES (@c,@b,@d,@v,'Installed','GPS',TRUE,NOW()-INTERVAL '1 hour',NOW()-INTERVAL '1 hour','test')",
                c =>
                {
                    c.Parameters.AddWithValue("@c", companyId); c.Parameters.AddWithValue("@b", branch);
                    c.Parameters.AddWithValue("@d", deviceId); c.Parameters.AddWithValue("@v", vehicleA);
                });
            Assert.Equal(vehicleA, await db.ScalarLongAsync("SELECT vehicle_id FROM eld_devices WHERE id=@d",
                c => c.Parameters.AddWithValue("@d", deviceId)));

            await using var connection = await db.OpenAsync();
            await using var transaction = await connection.BeginTransactionAsync();
            await using (var establishTransactionTime = new NpgsqlCommand("SELECT transaction_timestamp()", connection, transaction))
                await establishTransactionTime.ExecuteScalarAsync();
            DateTimeOffset effectiveAt;
            await using (var effectiveClock = new NpgsqlCommand("SELECT clock_timestamp() FROM pg_sleep(0.02)", connection, transaction))
                effectiveAt = new DateTimeOffset(((DateTime)(await effectiveClock.ExecuteScalarAsync())!).ToUniversalTime());

            await using (var closePrior = new NpgsqlCommand(
                "UPDATE device_installations SET status='Removed',effective_to=@at,removed_at=@at WHERE id=@id", connection, transaction))
            {
                closePrior.Parameters.AddWithValue("@at", effectiveAt);
                closePrior.Parameters.AddWithValue("@id", firstId);
                await closePrior.ExecuteNonQueryAsync();
            }
            await using (var insertCurrent = new NpgsqlCommand(
                @"INSERT INTO device_installations
                    (company_id,branch_id,device_id,vehicle_id,status,device_role,is_primary,effective_from,installed_at,source,replaced_installation_id)
                  VALUES (@c,@b,@d,@v,'Installed','GPS',TRUE,@at,@at,'test',@prior)", connection, transaction))
            {
                insertCurrent.Parameters.AddWithValue("@c", companyId); insertCurrent.Parameters.AddWithValue("@b", branch);
                insertCurrent.Parameters.AddWithValue("@d", deviceId); insertCurrent.Parameters.AddWithValue("@v", vehicleB);
                insertCurrent.Parameters.AddWithValue("@at", effectiveAt); insertCurrent.Parameters.AddWithValue("@prior", firstId);
                await insertCurrent.ExecuteNonQueryAsync();
            }
            await using (var projection = new NpgsqlCommand("SELECT vehicle_id FROM eld_devices WHERE id=@d", connection, transaction))
            {
                projection.Parameters.AddWithValue("@d", deviceId);
                Assert.Equal(vehicleB, Convert.ToInt64(await projection.ExecuteScalarAsync()));
            }
            await transaction.CommitAsync();
        }
        finally
        {
            foreach (var sql in new[]
            {
                "DELETE FROM audit_logs WHERE company_id=@c", "DELETE FROM device_state_transitions WHERE company_id=@c",
                "DELETE FROM device_installations WHERE company_id=@c", "DELETE FROM eld_devices WHERE company_id=@c",
                "DELETE FROM vehicles WHERE company_id=@c", "DELETE FROM branches WHERE company_id=@c",
                "DELETE FROM companies WHERE id=@c"
            }) await db.ExecuteAsync(sql, c => c.Parameters.AddWithValue("@c", companyId));
        }
    }

    [Fact]
    public async Task RemovedHistoryRetainsDeviceAndPrimaryVehicleRoleNonOverlap()
    {
        var db = Db();
        var companyId = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() + Random.Shared.Next(2_000_000,2_900_000);
        await Company(db,companyId,"HISTORY");
        try
        {
            var branch = await Branch(db,companyId,"HISTORY");
            var vehicleA = await Vehicle(db,companyId,branch,$"HISTORY-A-{companyId}",vin:"1HGCM82633A004352");
            var vehicleB = await Vehicle(db,companyId,branch,$"HISTORY-B-{companyId}",alternate:$"HISTORY-MFG-{companyId}");
            var deviceA = await Device(db,companyId,$"HISTORY-DEVICE-A-{companyId}");
            var deviceB = await Device(db,companyId,$"HISTORY-DEVICE-B-{companyId}");
            var from = DateTimeOffset.UtcNow.AddDays(-10);
            var to = DateTimeOffset.UtcNow.AddDays(-5);

            var first = await RemovedInstallation(db,companyId,branch,deviceA,vehicleA,from,to);
            var vehicleOverlap = await Assert.ThrowsAsync<PostgresException>(() =>
                RemovedInstallation(db,companyId,branch,deviceB,vehicleA,from.AddDays(1),to.AddDays(-1),"gps"));
            Assert.Equal(PostgresErrorCodes.ExclusionViolation,vehicleOverlap.SqlState);

            var deviceOverlap = await Assert.ThrowsAsync<PostgresException>(() =>
                RemovedInstallation(db,companyId,branch,deviceA,vehicleB,from.AddDays(1),to.AddDays(-1)));
            Assert.Equal(PostgresErrorCodes.ExclusionViolation,deviceOverlap.SqlState);

            var rewrite = await Assert.ThrowsAsync<PostgresException>(() => db.ExecuteAsync(
                "UPDATE device_installations SET effective_from=effective_from-INTERVAL '1 day' WHERE id=@id",
                c => c.Parameters.AddWithValue("@id",first)));
            Assert.Equal(PostgresErrorCodes.ObjectNotInPrerequisiteState,rewrite.SqlState);
        }
        finally
        {
            foreach (var sql in new[]
            {
                "DELETE FROM device_installations WHERE company_id=@c","DELETE FROM eld_devices WHERE company_id=@c",
                "DELETE FROM vehicles WHERE company_id=@c","DELETE FROM branches WHERE company_id=@c",
                "DELETE FROM companies WHERE id=@c"
            }) await db.ExecuteAsync(sql,c => c.Parameters.AddWithValue("@c",companyId));
        }
    }

    [Fact]
    public async Task EvidenceReferenceIsTenantCoherentAndAppendOnly()
    {
        var db = Db();
        var companyId = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() + Random.Shared.Next(3_000_000,3_900_000);
        var otherCompanyId = companyId + 1;
        await Company(db,companyId,"EVIDENCE-A");
        await Company(db,otherCompanyId,"EVIDENCE-B");
        try
        {
            var branchA = await Branch(db,companyId,"EVIDENCE-A");
            var branchB = await Branch(db,otherCompanyId,"EVIDENCE-B");
            var vehicleA = await Vehicle(db,companyId,branchA,$"EVIDENCE-A-{companyId}",vin:"1HGCM82633A004352");
            var vehicleB = await Vehicle(db,otherCompanyId,branchB,$"EVIDENCE-B-{companyId}",alternate:$"EVIDENCE-MFG-{companyId}");
            var deviceA = await Device(db,companyId,$"EVIDENCE-DEVICE-A-{companyId}");
            var deviceB = await Device(db,otherCompanyId,$"EVIDENCE-DEVICE-B-{companyId}");
            var from = DateTimeOffset.UtcNow.AddDays(-4);
            var installationA = await RemovedInstallation(db,companyId,branchA,deviceA,vehicleA,from,from.AddDays(1));
            var installationB = await RemovedInstallation(db,otherCompanyId,branchB,deviceB,vehicleB,from,from.AddDays(1));

            var mismatch = await Assert.ThrowsAsync<PostgresException>(() => db.ExecuteAsync(
                @"INSERT INTO device_installation_evidence(company_id,branch_id,installation_id,evidence_type,object_key,sha256)
                  VALUES (@c,@b,@i,'photo','cross-tenant-proof',repeat('a',64))",
                c => { c.Parameters.AddWithValue("@c",companyId); c.Parameters.AddWithValue("@b",branchA); c.Parameters.AddWithValue("@i",installationB); }));
            Assert.Equal(PostgresErrorCodes.ForeignKeyViolation,mismatch.SqlState);

            var evidenceId = await db.InsertAsync(
                @"INSERT INTO device_installation_evidence(company_id,branch_id,installation_id,evidence_type,object_key,sha256)
                  VALUES (@c,@b,@i,'photo','tenant-proof',repeat('b',64))",
                c => { c.Parameters.AddWithValue("@c",companyId); c.Parameters.AddWithValue("@b",branchA); c.Parameters.AddWithValue("@i",installationA); });
            var rewrite = await Assert.ThrowsAsync<PostgresException>(() => db.ExecuteAsync(
                "UPDATE device_installation_evidence SET object_key='rewritten' WHERE id=@id",
                c => c.Parameters.AddWithValue("@id",evidenceId)));
            Assert.Equal(PostgresErrorCodes.ObjectNotInPrerequisiteState,rewrite.SqlState);
        }
        finally
        {
            foreach (var company in new[] { companyId,otherCompanyId })
            foreach (var sql in new[]
            {
                "DELETE FROM device_installation_evidence WHERE company_id=@c","DELETE FROM device_installations WHERE company_id=@c",
                "DELETE FROM eld_devices WHERE company_id=@c","DELETE FROM vehicles WHERE company_id=@c",
                "DELETE FROM branches WHERE company_id=@c","DELETE FROM companies WHERE id=@c"
            }) await db.ExecuteAsync(sql,c => c.Parameters.AddWithValue("@c",company));
        }
    }

    [Fact]
    public async Task InstallationEvidenceApiIsTypedTenantScopedAppendOnlyAndIdempotent()
    {
        var db = Db();
        var companyId = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() + Random.Shared.Next(4_000_000,4_900_000);
        var otherCompanyId = companyId + 1;
        await Company(db,companyId,"EVIDENCE-API-A");
        await Company(db,otherCompanyId,"EVIDENCE-API-B");
        try
        {
            var branch = await Branch(db,companyId,"EVIDENCE-API-A");
            var otherBranch = await Branch(db,otherCompanyId,"EVIDENCE-API-B");
            var vehicle = await Vehicle(db,companyId,branch,$"EVIDENCE-API-A-{companyId}",vin:"1HGCM82633A004352");
            var otherVehicle = await Vehicle(db,otherCompanyId,otherBranch,$"EVIDENCE-API-B-{companyId}",alternate:$"EVIDENCE-API-{companyId}");
            var device = await Device(db,companyId,$"EVIDENCE-API-DEVICE-A-{companyId}");
            var otherDevice = await Device(db,otherCompanyId,$"EVIDENCE-API-DEVICE-B-{companyId}");
            var installation = await RemovedInstallation(db,companyId,branch,device,vehicle,DateTimeOffset.UtcNow.AddHours(-2),DateTimeOffset.UtcNow.AddHours(-1));
            var otherInstallation = await RemovedInstallation(db,otherCompanyId,otherBranch,otherDevice,otherVehicle,DateTimeOffset.UtcNow.AddHours(-2),DateTimeOffset.UtcNow.AddHours(-1));
            var sha = new string('a',64);
            var body = Body("DeviceInstallationEvidenceBody","removal-photo","fleet/installations/removal.jpg",sha,(DateTimeOffset?)DateTimeOffset.UtcNow.AddHours(-1));

            var created = await Invoke("DeviceInstallationEvidenceCreate",Principal(companyId,42),device,installation,body,db,new AuditService(db),CancellationToken.None);
            Assert.Equal(StatusCodes.Status201Created,Status(created));
            var replay = await Invoke("DeviceInstallationEvidenceCreate",Principal(companyId,42),device,installation,body,db,new AuditService(db),CancellationToken.None);
            Assert.Equal(StatusCodes.Status200OK,Status(replay));
            Assert.Equal(1,await db.ScalarLongAsync(
                "SELECT COUNT(*) FROM device_installation_evidence WHERE company_id=@c AND installation_id=@i",
                c => { c.Parameters.AddWithValue("@c",companyId); c.Parameters.AddWithValue("@i",installation); }));

            var badHash = await Invoke("DeviceInstallationEvidenceCreate",Principal(companyId,42),device,installation,
                Body("DeviceInstallationEvidenceBody","removal-photo","fleet/installations/removal.jpg","not-a-hash",(DateTimeOffset?)DateTimeOffset.UtcNow),
                db,new AuditService(db),CancellationToken.None);
            Assert.Equal(StatusCodes.Status400BadRequest,Status(badHash));
            var externalUrl = await Invoke("DeviceInstallationEvidenceCreate",Principal(companyId,42),device,installation,
                Body("DeviceInstallationEvidenceBody","removal-photo","https://untrusted.example/evidence",sha,(DateTimeOffset?)DateTimeOffset.UtcNow),
                db,new AuditService(db),CancellationToken.None);
            Assert.Equal(StatusCodes.Status400BadRequest,Status(externalUrl));

            var crossTenantCreate = await Invoke("DeviceInstallationEvidenceCreate",Principal(companyId,42),otherDevice,otherInstallation,body,db,new AuditService(db),CancellationToken.None);
            Assert.Equal(StatusCodes.Status404NotFound,Status(crossTenantCreate));
            var crossTenantRead = await Invoke("DeviceInstallationEvidenceList",Principal(companyId,42),otherDevice,otherInstallation,db,CancellationToken.None);
            Assert.Equal(StatusCodes.Status404NotFound,Status(crossTenantRead));
            var readOnlyCreate = await Invoke("DeviceInstallationEvidenceCreate",Principal(companyId,42,new[] { "telemetry.devices.read" }),device,installation,body,db,new AuditService(db),CancellationToken.None);
            Assert.Equal(StatusCodes.Status403Forbidden,Status(readOnlyCreate));
        }
        finally
        {
            foreach (var company in new[] { companyId,otherCompanyId })
            foreach (var sql in new[]
            {
                "DELETE FROM audit_logs WHERE company_id=@c","DELETE FROM device_installation_evidence WHERE company_id=@c",
                "DELETE FROM device_installations WHERE company_id=@c","DELETE FROM eld_devices WHERE company_id=@c",
                "DELETE FROM vehicles WHERE company_id=@c","DELETE FROM branches WHERE company_id=@c",
                "DELETE FROM companies WHERE id=@c"
            }) await db.ExecuteAsync(sql,c => c.Parameters.AddWithValue("@c",company));
        }
    }

    [Fact]
    public async Task CommissioningPassRequiresPersistedTelemetryProofAndExpectedVersion()
    {
        var db = Db();
        var companyId = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() + Random.Shared.Next(5_000_000,5_900_000);
        await Company(db,companyId,"COMMISSION");
        try
        {
            var branch = await Branch(db,companyId,"COMMISSION");
            var vehicle = await Vehicle(db,companyId,branch,$"COMMISSION-{companyId}",vin:"1HGCM82633A004352");
            var device = await Device(db,companyId,$"COMMISSION-DEVICE-{companyId}");
            var created = await Invoke("DeviceInstallationCreate",Principal(companyId,42),device,
                Body("DeviceInstallationCreateBody",vehicle,"GPS",true,(DateTimeOffset?)DateTimeOffset.UtcNow.AddMinutes(-2),
                    "cab",100m,"authenticated-heartbeat","commissioning test",$"commission-{Guid.NewGuid():N}"),
                db,new AuditService(db),CancellationToken.None);
            Assert.Equal(StatusCodes.Status201Created,Status(created));
            var installation = await db.ScalarLongAsync(
                "SELECT id FROM device_installations WHERE company_id=@c AND device_id=@d AND effective_to IS NULL",
                c => { c.Parameters.AddWithValue("@c",companyId); c.Parameters.AddWithValue("@d",device); });

            var withoutTelemetry = await Invoke("DeviceInstallationCommission",Principal(companyId,42),device,installation,
                Body("DeviceInstallationCommissionBody","passed","operator assertion",(int?)1),
                db,new AuditService(db),CancellationToken.None);
            Assert.Equal(StatusCodes.Status409Conflict,Status(withoutTelemetry));

            await db.ExecuteAsync(
                "UPDATE device_installations SET activation_verified_at=NOW() WHERE id=@i",
                c => c.Parameters.AddWithValue("@i",installation));
            var eventId = await db.InsertAsync(
                @"INSERT INTO location_events(company_id,vehicle_id,device_id,installation_id,lat,lng,event_time)
                  VALUES (@c,@v,@d,@i,38.7500000,-77.5500000,NOW())",
                c => { c.Parameters.AddWithValue("@c",companyId); c.Parameters.AddWithValue("@v",vehicle); c.Parameters.AddWithValue("@d",device); c.Parameters.AddWithValue("@i",installation); });
            var commissioned = await Invoke("DeviceInstallationCommission",Principal(companyId,42),device,installation,
                Body("DeviceInstallationCommissionBody","passed","operator assertion",(int?)1),
                db,new AuditService(db),CancellationToken.None);
            Assert.Equal(StatusCodes.Status200OK,Status(commissioned));
            var row = await db.QuerySingleAsync(
                "SELECT status,commissioning_result,verification_reference,row_version FROM device_installations WHERE id=@i",
                c => c.Parameters.AddWithValue("@i",installation));
            Assert.NotNull(row);
            Assert.Equal("Verified",row!["status"]);
            Assert.Equal("Passed",row["commissioningResult"]);
            Assert.Equal($"location-event:{eventId}",row["verificationReference"]);
            Assert.Equal(2,Convert.ToInt32(row["rowVersion"]));

            var staleReplay = await Invoke("DeviceInstallationCommission",Principal(companyId,42),device,installation,
                Body("DeviceInstallationCommissionBody","passed","operator assertion",(int?)1),
                db,new AuditService(db),CancellationToken.None);
            Assert.Equal(StatusCodes.Status409Conflict,Status(staleReplay));
        }
        finally
        {
            foreach (var sql in new[]
            {
                "DELETE FROM audit_logs WHERE company_id=@c","DELETE FROM location_events WHERE company_id=@c",
                "DELETE FROM device_state_transitions WHERE company_id=@c","DELETE FROM device_installations WHERE company_id=@c",
                "DELETE FROM eld_devices WHERE company_id=@c","DELETE FROM vehicles WHERE company_id=@c",
                "DELETE FROM branches WHERE company_id=@c","DELETE FROM companies WHERE id=@c"
            }) await db.ExecuteAsync(sql,c => c.Parameters.AddWithValue("@c",companyId));
        }
    }

    [Fact]
    public async Task DeviceSuspendAndActivateAreTenantScopedIdempotentAndAudited()
    {
        var db = Db();
        var companyId = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() + Random.Shared.Next(6_000_000,6_900_000);
        var otherCompanyId = companyId + 1;
        await Company(db,companyId,"STATE-A");
        await Company(db,otherCompanyId,"STATE-B");
        try
        {
            var device = await Device(db,companyId,$"STATE-A-{companyId}");
            var otherDevice = await Device(db,otherCompanyId,$"STATE-B-{companyId}");
            var suspend = await Invoke("DeviceSuspend",Principal(companyId,42),device,db,new AuditService(db),CancellationToken.None);
            Assert.Equal(StatusCodes.Status200OK,Status(suspend));
            var suspendReplay = await Invoke("DeviceSuspend",Principal(companyId,42),device,db,new AuditService(db),CancellationToken.None);
            Assert.Equal(StatusCodes.Status200OK,Status(suspendReplay));
            Assert.Equal(1,await db.ScalarLongAsync(
                "SELECT COUNT(*) FROM device_state_transitions WHERE company_id=@c AND device_id=@d AND to_state='Suspended'",
                c => { c.Parameters.AddWithValue("@c",companyId); c.Parameters.AddWithValue("@d",device); }));

            var activate = await Invoke("DeviceActivate",Principal(companyId,42),device,db,new AuditService(db),CancellationToken.None);
            Assert.Equal(StatusCodes.Status200OK,Status(activate));
            var activateReplay = await Invoke("DeviceActivate",Principal(companyId,42),device,db,new AuditService(db),CancellationToken.None);
            Assert.Equal(StatusCodes.Status200OK,Status(activateReplay));
            var state = await db.QuerySingleAsync("SELECT status,device_state,row_version FROM eld_devices WHERE id=@d",
                c => c.Parameters.AddWithValue("@d",device));
            Assert.NotNull(state);
            Assert.Equal("Active",state!["status"]);
            Assert.Equal("Registered",state["deviceState"]);
            Assert.Equal(3,Convert.ToInt32(state["rowVersion"]));
            Assert.Equal(2,await db.ScalarLongAsync(
                "SELECT COUNT(*) FROM device_state_transitions WHERE company_id=@c AND device_id=@d",
                c => { c.Parameters.AddWithValue("@c",companyId); c.Parameters.AddWithValue("@d",device); }));
            Assert.Equal(2,await db.ScalarLongAsync(
                "SELECT COUNT(*) FROM audit_logs WHERE company_id=@c AND entity_id=@d AND action_name IN ('device.suspended','device.activated')",
                c => { c.Parameters.AddWithValue("@c",companyId); c.Parameters.AddWithValue("@d",device); }));

            var crossTenant = await Invoke("DeviceSuspend",Principal(companyId,42),otherDevice,db,new AuditService(db),CancellationToken.None);
            Assert.Equal(StatusCodes.Status404NotFound,Status(crossTenant));
            var readOnly = await Invoke("DeviceSuspend",Principal(companyId,42,new[] { "telemetry.devices.read" }),device,db,new AuditService(db),CancellationToken.None);
            Assert.Equal(StatusCodes.Status403Forbidden,Status(readOnly));
        }
        finally
        {
            foreach (var company in new[] { companyId,otherCompanyId })
            foreach (var sql in new[]
            {
                "DELETE FROM audit_logs WHERE company_id=@c","DELETE FROM device_state_transitions WHERE company_id=@c",
                "DELETE FROM eld_devices WHERE company_id=@c","DELETE FROM companies WHERE id=@c"
            }) await db.ExecuteAsync(sql,c => c.Parameters.AddWithValue("@c",company));
        }
    }

    [Fact]
    public async Task ApplicationRoleCannotUpdateCompatibilityVehicleOrDriverProjection()
    {
        var db = Db();
        Assert.Equal(0,await db.ScalarLongAsync("SELECT CASE WHEN has_column_privilege('opstrax_app','eld_devices','vehicle_id','UPDATE') THEN 1 ELSE 0 END"));
        Assert.Equal(0,await db.ScalarLongAsync("SELECT CASE WHEN has_column_privilege('opstrax_app','eld_devices','driver_id','UPDATE') THEN 1 ELSE 0 END"));
        Assert.Equal(1,await db.ScalarLongAsync("SELECT CASE WHEN has_column_privilege('opstrax_app','eld_devices','device_state','UPDATE') THEN 1 ELSE 0 END"));
    }

    private static async Task Company(Database db,long id,string suffix) => await db.ExecuteAsync(
        "INSERT INTO companies(id,company_code,name,industry) OVERRIDING SYSTEM VALUE VALUES (@c,@code,@name,'transport')",
        c => { c.Parameters.AddWithValue("@c",id); c.Parameters.AddWithValue("@code",$"INSTALL-{id}-{suffix}"); c.Parameters.AddWithValue("@name",$"Installation tenant {suffix}"); });
    private static Task<long> Branch(Database db,long company,string suffix) => db.InsertAsync(
        "INSERT INTO branches(company_id,branch_code,name,status) VALUES (@c,@code,@name,'Active')",
        c => { c.Parameters.AddWithValue("@c",company); c.Parameters.AddWithValue("@code",$"BR-{company}-{suffix}"); c.Parameters.AddWithValue("@name",$"Branch {suffix}"); });
    private static Task<long> Vehicle(Database db,long company,long branch,string code,string? vin=null,string? alternate=null) => db.InsertAsync(
        @"INSERT INTO vehicles(company_id,branch_id,vehicle_code,type,vin,vin_exception_type,alternate_identifier,status,availability_status,out_of_service)
          VALUES (@c,@b,@code,'Truck',@vin,@exception,@alternate,'Available','available',FALSE)",
        c =>
        {
            c.Parameters.AddWithValue("@c",company); c.Parameters.AddWithValue("@b",branch); c.Parameters.AddWithValue("@code",code);
            c.Parameters.AddWithValue("@vin",(object?)vin ?? DBNull.Value);
            c.Parameters.AddWithValue("@exception",alternate is null ? DBNull.Value : "manufacturer-serial-number");
            c.Parameters.AddWithValue("@alternate",(object?)alternate ?? DBNull.Value);
        });
    private static Task<long> Device(Database db,long company,string serial) => db.InsertAsync(
        @"INSERT INTO eld_devices(company_id,device_serial,status,device_state,api_key_hash,hmac_secret_encrypted,hmac_key_version,created_at)
          VALUES (@c,@serial,'Active','Registered',encode(sha256(@serial::bytea),'hex'),repeat('b',32),1,NOW())",
        c => { c.Parameters.AddWithValue("@c",company); c.Parameters.AddWithValue("@serial",serial); });
    private static Task<long> RemovedInstallation(Database db,long company,long branch,long device,long vehicle,DateTimeOffset from,DateTimeOffset to,string role="GPS") => db.InsertAsync(
        @"INSERT INTO device_installations(company_id,branch_id,device_id,vehicle_id,status,device_role,is_primary,effective_from,effective_to,installed_at,removed_at,source)
          VALUES (@c,@b,@d,@v,'Removed',@role,TRUE,@from,@to,@from,@to,'test')",
        c => { c.Parameters.AddWithValue("@c",company); c.Parameters.AddWithValue("@b",branch); c.Parameters.AddWithValue("@d",device);
               c.Parameters.AddWithValue("@v",vehicle); c.Parameters.AddWithValue("@from",from); c.Parameters.AddWithValue("@to",to);
               c.Parameters.AddWithValue("@role",role); });
    private static Task<long> Count(Database db,long company,long device) => db.ScalarLongAsync(
        "SELECT COUNT(*) FROM device_installations WHERE company_id=@c AND device_id=@d",
        c => { c.Parameters.AddWithValue("@c",company); c.Parameters.AddWithValue("@d",device); });
    private static Database Db() => new(new ConfigurationBuilder().AddInMemoryCollection(
        new Dictionary<string,string?> { ["ConnectionStrings:DefaultConnection"] = TestDb.ConnectionString }).Build());
    private static Database Db(string appConnection, bool rls, string systemConnection) => new(
        new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string,string?>
        {
            ["ConnectionStrings:DefaultConnection"] = appConnection,
            ["ConnectionStrings:SystemConnection"] = systemConnection,
            ["Rls:EnforceTenantContext"] = rls.ToString(),
            ["Rls:TenantTicketTtlSeconds"] = "120"
        }).Build(), new TenantScopeAccessor());
    private static DefaultHttpContext Principal(long companyId,long userId,string[]? permissions=null)
    {
        var http = new DefaultHttpContext { TraceIdentifier=$"install-{Guid.NewGuid():N}" };
        http.Items[EndpointMappings.AuthCompanyIdItemKey]=companyId;
        http.Items[EndpointMappings.AuthUserIdItemKey]=userId;
        http.Items[EndpointMappings.AuthRoleItemKey]="CompanyAdmin";
        http.Items[EndpointMappings.AuthPermissionsItemKey]=permissions ?? new[] { "telemetry.devices.manage","telemetry.devices.read" };
        return http;
    }
    private static object Body(string name,params object?[] args)
    {
        var type=typeof(EndpointMappings).GetNestedType(name,BindingFlags.NonPublic)
            ?? throw new InvalidOperationException($"Missing body {name}");
        return Activator.CreateInstance(type,args) ?? throw new InvalidOperationException($"Unable to create {name}");
    }
    private static async Task<IResult> Invoke(string name,params object?[] args)
    {
        var method=typeof(EndpointMappings).GetMethod(name,BindingFlags.NonPublic|BindingFlags.Static)
            ?? throw new InvalidOperationException($"Missing endpoint {name}");
        return await ((Task<IResult>?)method.Invoke(null,args) ?? throw new InvalidOperationException($"{name} did not return a task"));
    }
    private static int? Status(IResult result) => Assert.IsAssignableFrom<IStatusCodeHttpResult>(result).StatusCode;
}
