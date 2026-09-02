using System.Net;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Opstrax.Api.Controllers;
using Opstrax.Api.Data;
using Opstrax.Api.Services;
using Opstrax.Api.Services.Connectors;

namespace Opstrax.Tests;

[Trait("Category", "Integration")]
public sealed class SamsaraSyncPostgresTests
{
    [Fact]
    public async Task OversizedLaterPageRetainsOnlyCommittedNonemptyHistoryAndCursor()
    {
        var db = CreateDatabase();
        var suffix = Guid.NewGuid().ToString("N");
        var companyId = await db.InsertAsync(
            "INSERT INTO companies(company_code,name,industry) VALUES(@code,'Synthetic response bound test','Transportation') RETURNING id",
            c => c.Parameters.AddWithValue("@code", $"SRB-{suffix[..10]}"));
        try
        {
            var integrationId = await db.InsertAsync(
                @"INSERT INTO integrations(company_id,provider_name,category,status,integration_key,config_json)
                   VALUES(@cid,'Samsara','Telematics & ELD','Connected','samsara','{}'::jsonb) RETURNING id",
                c => c.Parameters.AddWithValue("@cid", companyId));
            var operation = await ConnectorOperationLease.TryAcquireAsync(
                db, companyId, integrationId, ["Connected"], TimeSpan.FromSeconds(180), CancellationToken.None);
            Assert.NotNull(operation);
            var observedAt = DateTimeOffset.UtcNow.AddMinutes(-1);
            var firstPage = $$$"""
                {"data":[{"id":"synthetic-{{{suffix}}}","gps":[{"time":"{{{observedAt:O}}}","latitude":34.05,"longitude":-118.24,"speedMilesPerHour":40,"headingDegrees":90}]}],"pagination":{"endCursor":"complete-1","hasNextPage":true}}
                """;
            var stream = new SamsaraBodyFixture([], endless: true);
            using var content = new SamsaraContentFixture(stream);
            var calls = 0;
            var connector = SamsaraResponseBoundsTests.Connector(_ => Task.FromResult(++calls == 1
                ? SamsaraResponseBoundsTests.Json(firstPage)
                : new HttpResponseMessage(HttpStatusCode.OK) { Content = content }), db);
            using var body = JsonDocument.Parse(JsonSerializer.Serialize(new
            {
                companyId, integrationId, operationGeneration = operation!.Generation,
                operationLeaseToken = operation.LeaseToken.ToString(),
            }));
            var result = await connector.RunActionAsync("sync",
                new Dictionary<string, string?> { ["apiToken"] = "synthetic-test-token" }, body.RootElement, CancellationToken.None);

            Assert.False(result.Success);
            Assert.Contains("exceeded", result.Message, StringComparison.OrdinalIgnoreCase);
            Assert.Equal(2, calls);
            Assert.Equal(1, result.Details!["pagesCommitted"]);
            Assert.Equal("complete-1", result.Details["nextCursor"]);
            Assert.Equal(1, result.Details["vehiclesSeen"]);
            Assert.Equal(1, result.Details["unmatched"]);
            Assert.Equal(0, result.Details["positionsWritten"]);
            Assert.Equal(1, await db.ScalarLongAsync("SELECT COUNT(*) FROM location_events WHERE company_id=@cid",
                c => c.Parameters.AddWithValue("@cid", companyId)));
            Assert.Equal(1, await db.ScalarLongAsync("SELECT COUNT(*) FROM eld_devices WHERE company_id=@cid",
                c => c.Parameters.AddWithValue("@cid", companyId)));
            Assert.Equal(0, await db.ScalarLongAsync("SELECT COUNT(*) FROM latest_vehicle_positions WHERE company_id=@cid",
                c => c.Parameters.AddWithValue("@cid", companyId)));
            Assert.Equal(0, await db.ScalarLongAsync("SELECT COUNT(*) FROM telemetry_alerts WHERE company_id=@cid",
                c => c.Parameters.AddWithValue("@cid", companyId)));
            var providerEventAt = await db.QuerySingleAsync("SELECT provider_last_event_at FROM integrations WHERE company_id=@cid AND id=@id",
                c => { c.Parameters.AddWithValue("@cid", companyId); c.Parameters.AddWithValue("@id", integrationId); });
            Assert.Equal(observedAt.UtcDateTime, Convert.ToDateTime(providerEventAt!["providerLastEventAt"]).ToUniversalTime(), TimeSpan.FromMilliseconds(1));
            Assert.False(content.WasBuffered);
            Assert.True(stream.Disposed);
        }
        finally
        {
            await db.ExecuteAsync("DELETE FROM location_events WHERE company_id=@cid", c => c.Parameters.AddWithValue("@cid", companyId));
            await db.ExecuteAsync("DELETE FROM eld_devices WHERE company_id=@cid", c => c.Parameters.AddWithValue("@cid", companyId));
            await db.ExecuteAsync("DELETE FROM integrations WHERE company_id=@cid", c => c.Parameters.AddWithValue("@cid", companyId));
            await db.ExecuteAsync("DELETE FROM companies WHERE id=@cid", c => c.Parameters.AddWithValue("@cid", companyId));
        }
    }

    [Fact]
    public async Task ConfigurationRowLockAndDisconnectAreLinearizableWithoutCredentialResurrection()
    {
        var dbConfigure = CreateDatabase();
        var dbDisconnect = CreateDatabase();
        var suffix = Guid.NewGuid().ToString("N");
        var companyId = await dbConfigure.InsertAsync(
            "INSERT INTO companies(company_code,name,industry) VALUES(@code,'Connector configuration lock test','Transportation') RETURNING id",
            c => c.Parameters.AddWithValue("@code", $"SCL-{suffix[..10]}"));
        var integrationId = await dbConfigure.InsertAsync(
            @"INSERT INTO integrations(company_id,provider_name,category,status,integration_key,config_json)
              VALUES(@cid,'Samsara','Telematics & ELD','Connected','samsara','{""apiToken"":""old-secret""}'::jsonb) RETURNING id",
            c => c.Parameters.AddWithValue("@cid", companyId));

        try
        {
            var lockAcquired = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var allowConfigureCommit = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var configure = EndpointMappings.RunLockedIntegrationMutationAsync(
                dbConfigure, companyId, integrationId, async existing =>
                {
                    lockAcquired.SetResult();
                    await allowConfigureCommit.Task;
                    return await dbConfigure.ExecuteAsync(
                        @"UPDATE integrations SET config_json=@config::jsonb,status='Pending',
                              operation_generation=operation_generation+1
                          WHERE company_id=@cid AND id=@id",
                        c =>
                        {
                            c.Parameters.AddWithValue("@config", existing["configJson"]?.ToString() ?? "{}");
                            c.Parameters.AddWithValue("@cid", companyId);
                            c.Parameters.AddWithValue("@id", integrationId);
                        });
                });
            await lockAcquired.Task;

            var disconnect = dbDisconnect.RunInTenantTransactionAsync(companyId, () =>
                dbDisconnect.ExecuteAsync(
                    @"UPDATE integrations SET status='Disconnected',config_json='{}'::jsonb,
                          operation_generation=operation_generation+1
                      WHERE company_id=@cid AND id=@id",
                    c =>
                    {
                        c.Parameters.AddWithValue("@cid", companyId);
                        c.Parameters.AddWithValue("@id", integrationId);
                    }));

            Assert.NotSame(disconnect, await Task.WhenAny(disconnect, Task.Delay(150)));
            allowConfigureCommit.SetResult();
            Assert.True(await configure);
            Assert.Equal(1, await disconnect);

            var afterLockFirst = await dbConfigure.QuerySingleAsync(
                "SELECT status,config_json FROM integrations WHERE company_id=@cid AND id=@id",
                c =>
                {
                    c.Parameters.AddWithValue("@cid", companyId);
                    c.Parameters.AddWithValue("@id", integrationId);
                });
            Assert.Equal("Disconnected", afterLockFirst?["status"]?.ToString());
            Assert.Equal("{}", afterLockFirst?["configJson"]?.ToString());

            // Opposite order: disconnect commits first. The subsequent locked merge sees
            // only the cleared object and therefore cannot recover the old token.
            var configureAfterDisconnect = await EndpointMappings.RunLockedIntegrationMutationAsync(
                dbConfigure, companyId, integrationId, existing => dbConfigure.ExecuteAsync(
                    @"UPDATE integrations SET config_json=@config::jsonb,status='Pending',
                          operation_generation=operation_generation+1
                      WHERE company_id=@cid AND id=@id",
                    c =>
                    {
                        c.Parameters.AddWithValue("@config", existing["configJson"]?.ToString() ?? "{}");
                        c.Parameters.AddWithValue("@cid", companyId);
                        c.Parameters.AddWithValue("@id", integrationId);
                    }));
            Assert.True(configureAfterDisconnect);
            var final = await dbConfigure.QuerySingleAsync(
                "SELECT status,config_json FROM integrations WHERE company_id=@cid AND id=@id",
                c =>
                {
                    c.Parameters.AddWithValue("@cid", companyId);
                    c.Parameters.AddWithValue("@id", integrationId);
                });
            Assert.Equal("Pending", final?["status"]?.ToString());
            Assert.Equal("{}", final?["configJson"]?.ToString());
        }
        finally
        {
            await dbConfigure.ExecuteAsync("DELETE FROM integrations WHERE company_id=@cid", c => c.Parameters.AddWithValue("@cid", companyId));
            await dbConfigure.ExecuteAsync("DELETE FROM companies WHERE id=@cid", c => c.Parameters.AddWithValue("@cid", companyId));
        }
    }

    [Fact]
    public async Task SyncLockFirstMakesDisconnectWaitThenInvalidateTheCommittedOperation()
    {
        var dbSync = CreateDatabase();
        var dbDisconnect = CreateDatabase();
        var suffix = Guid.NewGuid().ToString("N");
        var companyId = await dbSync.InsertAsync(
            "INSERT INTO companies(company_code,name,industry) VALUES(@code,'Samsara sync lock order test','Transportation') RETURNING id",
            c => c.Parameters.AddWithValue("@code", $"SLO-{suffix[..10]}"));
        var integrationId = await dbSync.InsertAsync(
            @"INSERT INTO integrations(company_id,provider_name,category,status,integration_key,config_json)
              VALUES(@cid,'Samsara','Telematics & ELD','Connected','samsara','{}'::jsonb) RETURNING id",
            c => c.Parameters.AddWithValue("@cid", companyId));

        try
        {
            var operation = await ConnectorOperationLease.TryAcquireAsync(
                dbSync, companyId, integrationId, ["Connected"], TimeSpan.FromSeconds(180), CancellationToken.None);
            Assert.NotNull(operation);
            var writeLockAcquired = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var allowWriteCommit = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var serial = $"samsara-lock-first-{suffix}";

            var providerWrite = dbSync.RunInSystemTransactionAsync(async () =>
            {
                await ConnectorOperationLease.AssertCurrentForWriteAsync(dbSync, operation!, CancellationToken.None);
                writeLockAcquired.SetResult();
                await allowWriteCommit.Task;
                return await dbSync.InsertAsync(
                    @"INSERT INTO eld_devices(company_id,device_serial,provider,status,last_seen_at)
                      VALUES(@cid,@serial,'Samsara','Provisioning',NOW()) RETURNING id",
                    c =>
                    {
                        c.Parameters.AddWithValue("@cid", companyId);
                        c.Parameters.AddWithValue("@serial", serial);
                    });
            });
            await writeLockAcquired.Task;

            var disconnect = dbDisconnect.RunInTenantTransactionAsync(companyId, () =>
                dbDisconnect.ExecuteAsync(
                    @"UPDATE integrations SET status='Disconnected',config_json='{}'::jsonb,
                          operation_generation=operation_generation+1,
                          operation_lease_token=NULL,operation_lease_expires_at=NULL
                      WHERE company_id=@cid AND id=@id",
                    c =>
                    {
                        c.Parameters.AddWithValue("@cid", companyId);
                        c.Parameters.AddWithValue("@id", integrationId);
                    }));
            Assert.NotSame(disconnect, await Task.WhenAny(disconnect, Task.Delay(150)));
            allowWriteCommit.SetResult();
            Assert.True(await providerWrite > 0);
            Assert.Equal(1, await disconnect);

            var final = await dbSync.QuerySingleAsync(
                "SELECT status,operation_lease_token FROM integrations WHERE company_id=@cid AND id=@id",
                c =>
                {
                    c.Parameters.AddWithValue("@cid", companyId);
                    c.Parameters.AddWithValue("@id", integrationId);
                });
            Assert.Equal("Disconnected", final?["status"]?.ToString());
            Assert.Null(final?["operationLeaseToken"]);
            Assert.Equal(1, await dbSync.ScalarLongAsync(
                "SELECT COUNT(*) FROM eld_devices WHERE company_id=@cid AND device_serial=@serial",
                c =>
                {
                    c.Parameters.AddWithValue("@cid", companyId);
                    c.Parameters.AddWithValue("@serial", serial);
                }));
        }
        finally
        {
            await dbSync.ExecuteAsync("DELETE FROM eld_devices WHERE company_id=@cid", c => c.Parameters.AddWithValue("@cid", companyId));
            await dbSync.ExecuteAsync("DELETE FROM integrations WHERE company_id=@cid", c => c.Parameters.AddWithValue("@cid", companyId));
            await dbSync.ExecuteAsync("DELETE FROM companies WHERE id=@cid", c => c.Parameters.AddWithValue("@cid", companyId));
        }
    }

    [Fact]
    public async Task ConcurrentFirstDiscoveryCreatesOneDeviceWithoutAmbiguityQuarantine()
    {
        var dbFirst = CreateDatabase();
        var dbSecond = CreateDatabase();
        var suffix = Guid.NewGuid().ToString("N");
        var companyId = await dbFirst.InsertAsync(
            "INSERT INTO companies(company_code,name,industry) VALUES(@code,'Samsara discovery race test','Transportation') RETURNING id",
            c => c.Parameters.AddWithValue("@code", $"SDR-{suffix[..10]}"));
        var providerVehicleId = $"race-{suffix}";
        var serial = $"samsara-{providerVehicleId}";

        try
        {
            var firstLockAcquired = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var allowFirstCommit = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var first = dbFirst.RunInSystemTransactionAsync(async () =>
            {
                await dbFirst.QuerySingleAsync(
                    "SELECT pg_advisory_xact_lock(hashtextextended(@serial,0)) AS locked",
                    c => c.Parameters.AddWithValue("@serial", serial));
                firstLockAcquired.SetResult();
                var id = await SamsaraSync.EnsureDiscoveredDeviceAsync(
                    dbFirst, companyId, providerVehicleId, DateTime.UtcNow.AddMinutes(-1), CancellationToken.None);
                await allowFirstCommit.Task;
                return id;
            });
            await firstLockAcquired.Task;

            var second = dbSecond.RunInSystemTransactionAsync(() =>
                SamsaraSync.EnsureDiscoveredDeviceAsync(
                    dbSecond, companyId, providerVehicleId, DateTime.UtcNow, CancellationToken.None));
            Assert.NotSame(second, await Task.WhenAny(second, Task.Delay(150)));
            allowFirstCommit.SetResult();
            var firstId = await first;
            var secondId = await second;

            Assert.NotNull(firstId);
            Assert.Equal(firstId, secondId);
            Assert.Equal(1, await dbFirst.ScalarLongAsync(
                "SELECT COUNT(*) FROM eld_devices WHERE company_id=@cid AND device_serial=@serial",
                c =>
                {
                    c.Parameters.AddWithValue("@cid", companyId);
                    c.Parameters.AddWithValue("@serial", serial);
                }));
            Assert.Equal(0, await dbFirst.ScalarLongAsync(
                "SELECT COUNT(*) FROM device_installation_quarantine WHERE company_id=@cid AND reason_code='ambiguous_device_identifier'",
                c => c.Parameters.AddWithValue("@cid", companyId)));
        }
        finally
        {
            await dbFirst.ExecuteAsync("DELETE FROM device_installation_quarantine WHERE company_id=@cid", c => c.Parameters.AddWithValue("@cid", companyId));
            await dbFirst.ExecuteAsync("DELETE FROM eld_devices WHERE company_id=@cid", c => c.Parameters.AddWithValue("@cid", companyId));
            await dbFirst.ExecuteAsync("DELETE FROM companies WHERE id=@cid", c => c.Parameters.AddWithValue("@cid", companyId));
        }
    }

    [Fact]
    public async Task WorkerCandidateOrderingRotatesARepeatedlyFailingPrefix()
    {
        var db = CreateDatabase();
        var suffix = Guid.NewGuid().ToString("N");
        var companyIds = new List<long>();
        var ids = new List<long>();

        try
        {
            for (var index = 0; index < 3; index++)
            {
                var companyId = await db.InsertAsync(
                    "INSERT INTO companies(company_code,name,industry) VALUES(@code,@name,'Transportation') RETURNING id",
                    c =>
                    {
                        c.Parameters.AddWithValue("@code", $"SCF-{suffix[..8]}-{index}");
                        c.Parameters.AddWithValue("@name", $"Connector fairness tenant {index}");
                    });
                companyIds.Add(companyId);
                ids.Add(await db.InsertAsync(
                    @"INSERT INTO integrations(company_id,provider_name,category,status,integration_key,config_json)
                      VALUES(@cid,@name,'Telematics & ELD','Connected','samsara','{}'::jsonb) RETURNING id",
                    c =>
                    {
                        c.Parameters.AddWithValue("@cid", companyId);
                        c.Parameters.AddWithValue("@name", $"Samsara fairness {index}");
                    }));
            }

            var first = await ConnectorSyncBackgroundService.SelectCandidateRowsAsync(
                db, 2, CancellationToken.None, companyIds.ToArray());
            Assert.Equal(ids.Take(2), first.Select(row => Convert.ToInt64(row["id"])));
            for (var index = 0; index < 2; index++)
            {
                var operation = await ConnectorOperationLease.TryAcquireAsync(
                    db, companyIds[index], ids[index], ["Connected"], TimeSpan.FromSeconds(30), CancellationToken.None);
                Assert.NotNull(operation);
                Assert.Equal(1, await ConnectorOperationLease.ReleaseAsErrorAsync(db, operation!, CancellationToken.None));
            }
            await db.ExecuteAsync(
                "UPDATE integrations SET updated_at=NOW()-INTERVAL '16 minutes' WHERE id=ANY(@ids)",
                c => c.Parameters.AddWithValue("@ids", ids.Take(2).ToArray()));

            var second = await ConnectorSyncBackgroundService.SelectCandidateRowsAsync(
                db, 2, CancellationToken.None, companyIds.ToArray());
            Assert.Equal(ids[2], Convert.ToInt64(second[0]["id"]));
            Assert.Contains(second, row => Convert.ToInt64(row["id"]) == ids[2]);
        }
        finally
        {
            foreach (var companyId in companyIds)
            {
                await db.ExecuteAsync("DELETE FROM integrations WHERE company_id=@cid", c => c.Parameters.AddWithValue("@cid", companyId));
                await db.ExecuteAsync("DELETE FROM companies WHERE id=@cid", c => c.Parameters.AddWithValue("@cid", companyId));
            }
        }
    }

    [Fact]
    public async Task DisconnectGenerationInvalidatesHandshakeAndBlocksAllTelemetrySideEffects()
    {
        var db = CreateDatabase();
        var suffix = Guid.NewGuid().ToString("N");
        var companyId = await db.InsertAsync(
            "INSERT INTO companies(company_code,name,industry) VALUES(@code,'Samsara lease barrier test','Transportation') RETURNING id",
            c => c.Parameters.AddWithValue("@code", $"SLB-{suffix[..10]}"));
        var integrationId = await db.InsertAsync(
            @"INSERT INTO integrations(company_id,provider_name,category,status,integration_key,config_json)
              VALUES(@cid,'Samsara','Telematics & ELD','Connected','samsara','{}'::jsonb) RETURNING id",
            c => c.Parameters.AddWithValue("@cid", companyId));

        try
        {
            var operation = await ConnectorOperationLease.TryAcquireAsync(
                db, companyId, integrationId, ["Connected"], TimeSpan.FromSeconds(180), CancellationToken.None);
            Assert.NotNull(operation);

            // Deterministic interleaving: provider I/O has captured this operation, then
            // disconnect commits before either its handshake result or telemetry writes.
            await db.ExecuteAsync(
                @"UPDATE integrations SET status='Disconnected',config_json='{}'::jsonb,
                      operation_generation=operation_generation+1,
                      operation_lease_token=NULL,operation_lease_expires_at=NULL
                  WHERE company_id=@cid AND id=@id",
                c =>
                {
                    c.Parameters.AddWithValue("@cid", companyId);
                    c.Parameters.AddWithValue("@id", integrationId);
                });

            var staleHandshakeRows = await ConnectorOperationLease.CompleteTestAsync(
                db, operation!, ConnectorResult.Ok("stale provider success"), CancellationToken.None);
            Assert.Equal(0, staleHandshakeRows);
            var disconnected = await db.QuerySingleAsync(
                "SELECT status,provider_last_event_at FROM integrations WHERE company_id=@cid AND id=@id",
                c =>
                {
                    c.Parameters.AddWithValue("@cid", companyId);
                    c.Parameters.AddWithValue("@id", integrationId);
                });
            Assert.Equal("Disconnected", disconnected?["status"]?.ToString());
            Assert.Null(disconnected?["providerLastEventAt"]);

            var observedAt = DateTimeOffset.UtcNow.AddMinutes(-1).ToString("O");
            var feed = $$$"""
                {"data":[{"id":"stale-{{{suffix}}}","gps":[{"time":"{{{observedAt}}}","latitude":34.05,"longitude":-118.24,"headingDegrees":90,"speedMilesPerHour":40}]}],"pagination":{"endCursor":"","hasNextPage":false}}
                """;
            await Assert.ThrowsAsync<StaleConnectorOperationException>(() =>
                Sync(db, feed).RunAsync(operation!, null, CancellationToken.None));

            Assert.Equal(0, await db.ScalarLongAsync(
                "SELECT COUNT(*) FROM eld_devices WHERE company_id=@cid",
                c => c.Parameters.AddWithValue("@cid", companyId)));
            Assert.Equal(0, await db.ScalarLongAsync(
                "SELECT COUNT(*) FROM location_events WHERE company_id=@cid AND source_channel='samsara-api'",
                c => c.Parameters.AddWithValue("@cid", companyId)));
            Assert.Equal(0, await db.ScalarLongAsync(
                "SELECT COUNT(*) FROM latest_vehicle_positions WHERE company_id=@cid",
                c => c.Parameters.AddWithValue("@cid", companyId)));
            Assert.Equal(0, await db.ScalarLongAsync(
                "SELECT COUNT(*) FROM telemetry_alerts WHERE company_id=@cid AND source_channel='samsara-api'",
                c => c.Parameters.AddWithValue("@cid", companyId)));
        }
        finally
        {
            await db.ExecuteAsync("DELETE FROM integrations WHERE company_id=@cid", c => c.Parameters.AddWithValue("@cid", companyId));
            await db.ExecuteAsync("DELETE FROM companies WHERE id=@cid", c => c.Parameters.AddWithValue("@cid", companyId));
        }
    }

    [Fact]
    public async Task HandshakeAndSyncMaintainIndependentDurableHealthClocks()
    {
        var db = CreateDatabase();
        var suffix = Guid.NewGuid().ToString("N");
        var companyId = await db.InsertAsync(
            "INSERT INTO companies(company_code,name,industry) VALUES(@code,'Connector health clocks','Transportation') RETURNING id",
            c => c.Parameters.AddWithValue("@code", $"SHC-{suffix[..10]}"));
        var integrationId = await db.InsertAsync(
            @"INSERT INTO integrations(company_id,provider_name,category,status,integration_key,config_json)
              VALUES(@cid,'Samsara','Telematics & ELD','Connected','samsara','{}'::jsonb) RETURNING id",
            c => c.Parameters.AddWithValue("@cid", companyId));

        try
        {
            var handshake = await ConnectorOperationLease.TryAcquireAsync(
                db, companyId, integrationId, ["Connected"], TimeSpan.FromSeconds(30), CancellationToken.None);
            Assert.NotNull(handshake);
            Assert.Equal(1, await ConnectorOperationLease.CompleteTestAsync(
                db, handshake!, ConnectorResult.Fail("provider handshake failed"), CancellationToken.None));

            var afterHandshake = await db.QuerySingleAsync(
                @"SELECT operation_last_attempt_at,sync_last_attempt_at,sync_last_completed_at,sync_last_ok
                  FROM integrations WHERE company_id=@cid AND id=@id",
                c =>
                {
                    c.Parameters.AddWithValue("@cid", companyId);
                    c.Parameters.AddWithValue("@id", integrationId);
                });
            Assert.NotNull(afterHandshake?["operationLastAttemptAt"]);
            Assert.Null(afterHandshake?["syncLastAttemptAt"]);
            Assert.Null(afterHandshake?["syncLastCompletedAt"]);
            Assert.Null(afterHandshake?["syncLastOk"]);

            var sync = await ConnectorOperationLease.TryAcquireAsync(
                db, companyId, integrationId, ["Error"], TimeSpan.FromSeconds(30), CancellationToken.None,
                isSyncOperation: true);
            Assert.NotNull(sync);
            Assert.Equal(1, await ConnectorOperationLease.CompleteSyncAsync(
                db, sync!, ConnectorResult.Fail("bounded sync ended after a complete page"), "cursor-1", CancellationToken.None));

            var afterSync = await db.QuerySingleAsync(
                @"SELECT sync_last_attempt_at,sync_last_completed_at,sync_last_ok,config_json->>'syncCursor' sync_cursor
                  FROM integrations WHERE company_id=@cid AND id=@id",
                c =>
                {
                    c.Parameters.AddWithValue("@cid", companyId);
                    c.Parameters.AddWithValue("@id", integrationId);
                });
            Assert.NotNull(afterSync?["syncLastAttemptAt"]);
            Assert.NotNull(afterSync?["syncLastCompletedAt"]);
            Assert.Equal(false, afterSync?["syncLastOk"]);
            Assert.Equal("cursor-1", afterSync?["syncCursor"]?.ToString());

            var integrityFailure = await ConnectorOperationLease.TryAcquireAsync(
                db, companyId, integrationId, ["Error"], TimeSpan.FromSeconds(30), CancellationToken.None,
                isSyncOperation: true);
            Assert.NotNull(integrityFailure);
            Assert.Equal(1, await ConnectorOperationLease.CompleteSyncAsync(
                db, integrityFailure!, ConnectorResult.Fail("pagination cycle"), null, CancellationToken.None));
            var afterIntegrityFailure = await db.QuerySingleAsync(
                "SELECT config_json->>'syncCursor' sync_cursor FROM integrations WHERE company_id=@cid AND id=@id",
                c =>
                {
                    c.Parameters.AddWithValue("@cid", companyId);
                    c.Parameters.AddWithValue("@id", integrationId);
                });
            Assert.Equal("cursor-1", afterIntegrityFailure?["syncCursor"]?.ToString());
        }
        finally
        {
            await db.ExecuteAsync("DELETE FROM integrations WHERE company_id=@cid", c => c.Parameters.AddWithValue("@cid", companyId));
            await db.ExecuteAsync("DELETE FROM companies WHERE id=@cid", c => c.Parameters.AddWithValue("@cid", companyId));
        }
    }

    [Fact]
    public async Task ProviderEventFreshnessAdvancesMonotonicallyInsideFencedPageTransaction()
    {
        var db = CreateDatabase();
        var suffix = Guid.NewGuid().ToString("N");
        var companyId = await db.InsertAsync(
            "INSERT INTO companies(company_code,name,industry) VALUES(@code,'Provider freshness clock','Transportation') RETURNING id",
            c => c.Parameters.AddWithValue("@code", $"PFC-{suffix[..10]}"));
        var integrationId = await db.InsertAsync(
            @"INSERT INTO integrations(company_id,provider_name,category,status,integration_key,config_json)
              VALUES(@cid,'Samsara','Telematics & ELD','Connected','samsara','{}'::jsonb) RETURNING id",
            c => c.Parameters.AddWithValue("@cid", companyId));

        try
        {
            var operation = await ConnectorOperationLease.TryAcquireAsync(
                db, companyId, integrationId, ["Connected"], TimeSpan.FromSeconds(180), CancellationToken.None,
                isSyncOperation: true);
            Assert.NotNull(operation);
            var newest = DateTimeOffset.UtcNow.AddMinutes(-1);
            var older = newest.AddHours(-2);
            string Feed(string id, DateTimeOffset observedAt) => $$$"""
                {"data":[{"id":"{{{id}}}","gps":[{"time":"{{{observedAt:O}}}","latitude":34.05,"longitude":-118.24,"headingDegrees":90,"speedMilesPerHour":40}]}],"pagination":{"endCursor":"","hasNextPage":false}}
                """;

            await Sync(db, Feed($"newest-{suffix}", newest)).RunAsync(operation!, null, CancellationToken.None);
            await Sync(db, Feed($"older-{suffix}", older)).RunAsync(operation!, null, CancellationToken.None);

            var row = await db.QuerySingleAsync(
                "SELECT provider_last_event_at FROM integrations WHERE company_id=@cid AND id=@id",
                c =>
                {
                    c.Parameters.AddWithValue("@cid", companyId);
                    c.Parameters.AddWithValue("@id", integrationId);
                });
            Assert.Equal(
                newest.UtcDateTime,
                Convert.ToDateTime(row?["providerLastEventAt"]).ToUniversalTime(),
                TimeSpan.FromMilliseconds(1));
            Assert.Equal(2, await db.ScalarLongAsync(
                "SELECT COUNT(*) FROM location_events WHERE company_id=@cid AND source_channel='samsara-api'",
                c => c.Parameters.AddWithValue("@cid", companyId)));
        }
        finally
        {
            await db.ExecuteAsync("DELETE FROM location_events WHERE company_id=@cid", c => c.Parameters.AddWithValue("@cid", companyId));
            await db.ExecuteAsync("DELETE FROM eld_devices WHERE company_id=@cid", c => c.Parameters.AddWithValue("@cid", companyId));
            await db.ExecuteAsync("DELETE FROM integrations WHERE company_id=@cid", c => c.Parameters.AddWithValue("@cid", companyId));
            await db.ExecuteAsync("DELETE FROM companies WHERE id=@cid", c => c.Parameters.AddWithValue("@cid", companyId));
        }
    }

    [Fact]
    public async Task ReplayedProviderPageDoesNotIncrementLatestOrCreateAnotherAlert()
    {
        var db = CreateDatabase();
        var suffix = Guid.NewGuid().ToString("N");
        var companyId = await db.InsertAsync(
            "INSERT INTO companies(company_code,name,industry) VALUES(@code,'Samsara replay test','Transportation') RETURNING id",
            c => c.Parameters.AddWithValue("@code", $"SAM-{suffix[..10]}"));
        var integrationId = await db.InsertAsync(
            @"INSERT INTO integrations(company_id,provider_name,category,status,integration_key,config_json)
              VALUES(@cid,'Samsara','Telematics & ELD','Connected','samsara','{}'::jsonb) RETURNING id",
            c => c.Parameters.AddWithValue("@cid", companyId));
        var operation = await ConnectorOperationLease.TryAcquireAsync(
            db, companyId, integrationId, ["Connected"], TimeSpan.FromSeconds(180), CancellationToken.None);
        Assert.NotNull(operation);
        var branchId = await db.InsertAsync(
            "INSERT INTO branches(company_id,branch_code,name,status) VALUES(@cid,@code,'Samsara test branch','Active') RETURNING id",
            c =>
            {
                c.Parameters.AddWithValue("@cid", companyId);
                c.Parameters.AddWithValue("@code", $"SAM-BR-{suffix[..8]}");
            });
        var vehicleId = await db.InsertAsync(
            "INSERT INTO vehicles(company_id,branch_id,vehicle_code,type,vin_exception_type,alternate_identifier) VALUES(@cid,@branch,@code,'truck','legacy-fleet-identifier',@code) RETURNING id",
            c =>
            {
                c.Parameters.AddWithValue("@cid", companyId);
                c.Parameters.AddWithValue("@branch", branchId);
                c.Parameters.AddWithValue("@code", $"SV-{suffix[..10]}");
            });
        var providerVehicleId = $"provider-{suffix}";
        var deviceId = await db.InsertAsync(
            @"INSERT INTO eld_devices(company_id,device_serial,provider,vehicle_id,status,last_seen_at)
              VALUES(@cid,@serial,'Samsara',@vid,'Provisioning',NULL)
              RETURNING id",
            c =>
            {
                c.Parameters.AddWithValue("@cid", companyId);
                c.Parameters.AddWithValue("@serial", $"samsara-{providerVehicleId}");
                c.Parameters.AddWithValue("@vid", vehicleId);
            });
        var initialInstallationAt = DateTimeOffset.UtcNow.AddHours(-3);
        var initialInstallationId = await db.InsertAsync(
            @"INSERT INTO device_installations
                (company_id,branch_id,device_id,vehicle_id,status,device_role,is_primary,effective_from,installed_at,source)
              VALUES(@cid,@branch,@device,@vehicle,'Installed','GPS',TRUE,@from,@from,'samsara-test')
              RETURNING id",
            c =>
            {
                c.Parameters.AddWithValue("@cid", companyId);
                c.Parameters.AddWithValue("@branch", branchId);
                c.Parameters.AddWithValue("@device", deviceId);
                c.Parameters.AddWithValue("@vehicle", vehicleId);
                c.Parameters.AddWithValue("@from", initialInstallationAt);
            });
        await db.ExecuteAsync(
            @"INSERT INTO telemetry_rules(company_id,rule_type,threshold_value,severity,enabled)
              VALUES(@cid,'speeding',50,'High',TRUE)
              ON CONFLICT(company_id,rule_type) DO UPDATE SET threshold_value=50,severity='High',enabled=TRUE",
            c => c.Parameters.AddWithValue("@cid", companyId));
        await db.ExecuteAsync(
            @"INSERT INTO geofences(company_id,name,geofence_type,center_lat,center_lng,radius_meters,status)
              VALUES
                (@cid,'Outside yard','Circle',35.05,-119.24,100,'Active'),
                (@cid,'Authorized yard','Circle',34.05,-118.24,500,'Active')",
            c => c.Parameters.AddWithValue("@cid", companyId));

        try
        {
            var observedAt = DateTimeOffset.UtcNow.AddMinutes(-30).ToString("O");
            var feed = $$$"""
                {"data":[{"id":"{{{providerVehicleId}}}","name":"Replay truck","gps":[{"time":"{{{observedAt}}}","latitude":34.05,"longitude":-118.24,"headingDegrees":90,"speedMilesPerHour":80}]}],"pagination":{"endCursor":"cursor-1","hasNextPage":false}}
                """;
            var client = new HttpClient(new StaticJsonHandler(feed))
            {
                BaseAddress = new Uri("https://samsara.invalid")
            };
            var services = new ServiceCollection().AddSingleton(db).BuildServiceProvider();
            var sync = new SamsaraSync(client, services.GetRequiredService<IServiceScopeFactory>(), NullLogger.Instance);

            var identityBeforeSync = await TelemetryIdentityResolver.ResolveAsync(
                db, companyId, deviceId, DateTimeOffset.Parse(observedAt), CancellationToken.None);
            Assert.NotNull(identityBeforeSync);

            var first = await sync.RunAsync(operation!, null, CancellationToken.None);
            var second = await sync.RunAsync(operation!, null, CancellationToken.None);

            var firstHistory = await db.QuerySingleAsync(
                "SELECT device_id,installation_id,vehicle_id FROM location_events WHERE company_id=@cid AND source_channel='samsara-api' ORDER BY id LIMIT 1",
                c => c.Parameters.AddWithValue("@cid", companyId));
            Assert.True(first.PositionsWritten == 1,
                $"Expected one current projection; seen={first.VehiclesSeen}, unmatched={first.Unmatched}, historical={first.HistoricalOnly}, rejected={first.Rejected}, expectedDevice={deviceId}, historyDevice={firstHistory?.GetValueOrDefault("deviceId")}, installation={firstHistory?.GetValueOrDefault("installationId")}.");
            Assert.Equal(0, second.PositionsWritten);
            Assert.Equal(1, await db.ScalarLongAsync(
                "SELECT COUNT(*) FROM location_events WHERE company_id=@cid AND source_channel='samsara-api'",
                c => c.Parameters.AddWithValue("@cid", companyId)));
            var firstLineage = await db.QuerySingleAsync(
                @"SELECT installation_id,vehicle_id,device_id FROM location_events
                  WHERE company_id=@cid AND source_channel='samsara-api' ORDER BY id LIMIT 1",
                c => c.Parameters.AddWithValue("@cid", companyId));
            Assert.NotNull(firstLineage);
            Assert.Equal(initialInstallationId, Convert.ToInt64(firstLineage!["installationId"]));
            Assert.Equal(vehicleId, Convert.ToInt64(firstLineage["vehicleId"]));
            Assert.Equal(deviceId, Convert.ToInt64(firstLineage["deviceId"]));
            Assert.Equal(1, await db.ScalarLongAsync(
                "SELECT event_count FROM latest_vehicle_positions WHERE company_id=@cid AND vehicle_id=@vid",
                c =>
                {
                    c.Parameters.AddWithValue("@cid", companyId);
                    c.Parameters.AddWithValue("@vid", vehicleId);
                }));
            Assert.Equal(1, await db.ScalarLongAsync(
                "SELECT COUNT(*) FROM telemetry_alerts WHERE company_id=@cid AND vehicle_id=@vid AND alert_type='speeding' AND source_channel='samsara-api'",
                c =>
                {
                    c.Parameters.AddWithValue("@cid", companyId);
                    c.Parameters.AddWithValue("@vid", vehicleId);
                }));
            Assert.Equal(0, await db.ScalarLongAsync(
                "SELECT COUNT(*) FROM telemetry_alerts WHERE company_id=@cid AND vehicle_id=@vid AND alert_type='geofence_breach'",
                c =>
                {
                    c.Parameters.AddWithValue("@cid", companyId);
                    c.Parameters.AddWithValue("@vid", vehicleId);
                }));

            // A novel but older provider fix is retained in history, but cannot replace latest or
            // open a new current speeding/geofence alert after the prior alert is closed.
            await db.ExecuteAsync(
                "UPDATE telemetry_alerts SET status='Closed' WHERE company_id=@cid",
                c => c.Parameters.AddWithValue("@cid", companyId));
            var olderAt = DateTimeOffset.UtcNow.AddDays(-30).ToString("O");
            var olderFeed = $$$"""
                {"data":[{"id":"{{{providerVehicleId}}}","name":"Replay truck","gps":[{"time":"{{{olderAt}}}","latitude":35.05,"longitude":-119.24,"headingDegrees":90,"speedMilesPerHour":90}]}],"pagination":{"endCursor":"cursor-older","hasNextPage":false}}
                """;
            var olderSync = Sync(db, olderFeed);
            var olderSummary = await olderSync.RunAsync(operation!, null, CancellationToken.None);
            Assert.Equal(0, olderSummary.PositionsWritten);
            Assert.Equal(0, olderSummary.Rejected);
            Assert.Equal(2, await db.ScalarLongAsync(
                "SELECT COUNT(*) FROM location_events WHERE company_id=@cid AND source_channel='samsara-api'",
                c => c.Parameters.AddWithValue("@cid", companyId)));
            Assert.Equal(1, await db.ScalarLongAsync(
                "SELECT event_count FROM latest_vehicle_positions WHERE company_id=@cid AND vehicle_id=@vid",
                c => { c.Parameters.AddWithValue("@cid", companyId); c.Parameters.AddWithValue("@vid", vehicleId); }));
            Assert.Equal(0, await db.ScalarLongAsync(
                "SELECT COUNT(*) FROM telemetry_alerts WHERE company_id=@cid AND status='Open'",
                c => c.Parameters.AddWithValue("@cid", companyId)));

            // Transfer the discovered Samsara device, then deliver a delayed provider fix
            // whose event time belongs to the ended installation. The mutable compatibility
            // projection points to the new vehicle, but governed event-time attribution must
            // retain the fix against the old installation and never mutate live/latest/alerts.
            var transferAt = DateTimeOffset.UtcNow.AddMinutes(-20);
            await db.ExecuteAsync(
                @"UPDATE device_installations
                  SET status='Removed',effective_to=@at,removed_at=@at
                  WHERE company_id=@cid AND id=@installation",
                c =>
                {
                    c.Parameters.AddWithValue("@at", transferAt);
                    c.Parameters.AddWithValue("@cid", companyId);
                    c.Parameters.AddWithValue("@installation", initialInstallationId);
                });
            var vehicleB = await db.InsertAsync(
                @"INSERT INTO vehicles(company_id,branch_id,vehicle_code,type,vin_exception_type,alternate_identifier)
                  VALUES(@cid,@branch,@code,'truck','legacy-fleet-identifier',@code) RETURNING id",
                c =>
                {
                    c.Parameters.AddWithValue("@cid", companyId);
                    c.Parameters.AddWithValue("@branch", branchId);
                    c.Parameters.AddWithValue("@code", $"SVB-{suffix[..9]}");
                });
            var currentInstallationId = await db.InsertAsync(
                @"INSERT INTO device_installations
                    (company_id,branch_id,device_id,vehicle_id,status,device_role,is_primary,effective_from,installed_at,source,replaced_installation_id)
                  VALUES(@cid,@branch,@device,@vehicle,'Installed','GPS',TRUE,@from,@from,'samsara-test',@replaced)
                  RETURNING id",
                c =>
                {
                    c.Parameters.AddWithValue("@cid", companyId);
                    c.Parameters.AddWithValue("@branch", branchId);
                    c.Parameters.AddWithValue("@device", deviceId);
                    c.Parameters.AddWithValue("@vehicle", vehicleB);
                    c.Parameters.AddWithValue("@from", transferAt);
                    c.Parameters.AddWithValue("@replaced", initialInstallationId);
                });
            await db.ExecuteAsync(
                "UPDATE eld_devices SET vehicle_id=@vehicle WHERE company_id=@cid AND id=@device",
                c =>
                {
                    c.Parameters.AddWithValue("@vehicle", vehicleB);
                    c.Parameters.AddWithValue("@cid", companyId);
                    c.Parameters.AddWithValue("@device", deviceId);
                });

            var delayedAt = transferAt.AddMinutes(-5).ToString("O");
            var delayedFeed = $$$"""
                {"data":[{"id":"{{{providerVehicleId}}}","gps":[{"time":"{{{delayedAt}}}","latitude":35.05,"longitude":-119.24,"headingDegrees":180,"speedMilesPerHour":99}]}],"pagination":{"endCursor":"","hasNextPage":false}}
                """;
            var delayed = await Sync(db, delayedFeed).RunAsync(operation!, null, CancellationToken.None);
            Assert.Equal(0, delayed.PositionsWritten);
            Assert.Equal(1, delayed.HistoricalOnly);
            var delayedLineage = await db.QuerySingleAsync(
                @"SELECT installation_id,vehicle_id FROM location_events
                  WHERE company_id=@cid AND idempotency_key=@key LIMIT 1",
                c =>
                {
                    c.Parameters.AddWithValue("@cid", companyId);
                    c.Parameters.AddWithValue("@key", $"samsara:{providerVehicleId}:{DateTimeOffset.Parse(delayedAt).UtcTicks}");
                });
            Assert.NotNull(delayedLineage);
            Assert.Equal(initialInstallationId, Convert.ToInt64(delayedLineage!["installationId"]));
            Assert.Equal(vehicleId, Convert.ToInt64(delayedLineage["vehicleId"]));
            Assert.Equal(0, await db.ScalarLongAsync(
                "SELECT COUNT(*) FROM latest_vehicle_positions WHERE company_id=@cid AND vehicle_id=@vehicle",
                c => { c.Parameters.AddWithValue("@cid", companyId); c.Parameters.AddWithValue("@vehicle", vehicleB); }));
            Assert.Equal(0, await db.ScalarLongAsync(
                "SELECT COUNT(*) FROM telemetry_alerts WHERE company_id=@cid AND status='Open'",
                c => c.Parameters.AddWithValue("@cid", companyId)));

            var currentAt = DateTimeOffset.UtcNow.AddSeconds(-5).ToString("O");
            var currentFeed = $$$"""
                {"data":[{"id":"{{{providerVehicleId}}}","gps":[{"time":"{{{currentAt}}}","latitude":34.05,"longitude":-118.24,"headingDegrees":0,"speedMilesPerHour":0}]}],"pagination":{"endCursor":"","hasNextPage":false}}
                """;
            var current = await Sync(db, currentFeed).RunAsync(operation!, null, CancellationToken.None);
            Assert.Equal(1, current.PositionsWritten);
            var currentLineage = await db.QuerySingleAsync(
                @"SELECT installation_id,vehicle_id FROM latest_vehicle_positions
                  WHERE company_id=@cid AND vehicle_id=@vehicle LIMIT 1",
                c => { c.Parameters.AddWithValue("@cid", companyId); c.Parameters.AddWithValue("@vehicle", vehicleB); });
            Assert.NotNull(currentLineage);
            Assert.Equal(currentInstallationId, Convert.ToInt64(currentLineage!["installationId"]));
            Assert.Equal(vehicleB, Convert.ToInt64(currentLineage["vehicleId"]));

            // A first, buffered fix for a newly discovered provider device must not stamp NOW().
            var newProviderVehicleId = $"new-{suffix}";
            var staleFirstFeed = $$$"""
                {"data":[{"id":"{{{newProviderVehicleId}}}","gps":[{"time":"{{{olderAt}}}","latitude":34.05,"longitude":-118.24,"headingDegrees":0,"speedMilesPerHour":0}]}],"pagination":{"endCursor":"","hasNextPage":false}}
                """;
            var unmatched = await Sync(db, staleFirstFeed).RunAsync(operation!, null, CancellationToken.None);
            Assert.Equal(1, unmatched.Unmatched);
            var discovered = await db.QuerySingleAsync(
                "SELECT last_seen_at FROM eld_devices WHERE company_id=@cid AND device_serial=@serial",
                c =>
                {
                    c.Parameters.AddWithValue("@cid", companyId);
                    c.Parameters.AddWithValue("@serial", $"samsara-{newProviderVehicleId}");
                });
            Assert.NotNull(discovered);
            var lastSeen = discovered!["lastSeenAt"] switch
            {
                DateTimeOffset dto => dto,
                DateTime dt => new DateTimeOffset(DateTime.SpecifyKind(dt, DateTimeKind.Utc)),
                var value => DateTimeOffset.Parse(value!.ToString()!, System.Globalization.CultureInfo.InvariantCulture),
            };
            Assert.True(lastSeen < DateTimeOffset.UtcNow.AddHours(-1));
            var unmatchedHistory = await db.QuerySingleAsync(
                @"SELECT vehicle_id,installation_id FROM location_events
                  WHERE company_id=@cid AND device_id=(SELECT id FROM eld_devices WHERE company_id=@cid AND device_serial=@serial)
                  ORDER BY id DESC LIMIT 1",
                c =>
                {
                    c.Parameters.AddWithValue("@cid", companyId);
                    c.Parameters.AddWithValue("@serial", $"samsara-{newProviderVehicleId}");
                });
            Assert.NotNull(unmatchedHistory);
            Assert.True(unmatchedHistory!["vehicleId"] is null or DBNull);
            Assert.True(unmatchedHistory["installationId"] is null or DBNull);
        }
        finally
        {
            await db.ExecuteAsync("DELETE FROM telemetry_alerts WHERE company_id=@cid", c => c.Parameters.AddWithValue("@cid", companyId));
            await db.ExecuteAsync("DELETE FROM latest_vehicle_positions WHERE company_id=@cid", c => c.Parameters.AddWithValue("@cid", companyId));
            await db.ExecuteAsync("DELETE FROM location_events WHERE company_id=@cid", c => c.Parameters.AddWithValue("@cid", companyId));
            await db.ExecuteAsync("DELETE FROM geofences WHERE company_id=@cid", c => c.Parameters.AddWithValue("@cid", companyId));
            await db.ExecuteAsync("DELETE FROM telemetry_rules WHERE company_id=@cid", c => c.Parameters.AddWithValue("@cid", companyId));
            await db.ExecuteAsync("DELETE FROM device_installations WHERE company_id=@cid", c => c.Parameters.AddWithValue("@cid", companyId));
            await db.ExecuteAsync("DELETE FROM eld_devices WHERE company_id=@cid", c => c.Parameters.AddWithValue("@cid", companyId));
            await db.ExecuteAsync("DELETE FROM vehicles WHERE company_id=@cid", c => c.Parameters.AddWithValue("@cid", companyId));
            await db.ExecuteAsync("DELETE FROM branches WHERE company_id=@cid", c => c.Parameters.AddWithValue("@cid", companyId));
            await db.ExecuteAsync("DELETE FROM integrations WHERE company_id=@cid", c => c.Parameters.AddWithValue("@cid", companyId));
            await db.ExecuteAsync("DELETE FROM companies WHERE id=@cid", c => c.Parameters.AddWithValue("@cid", companyId));
        }
    }

    private static Database CreateDatabase() =>
        new(new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["ConnectionStrings:DefaultConnection"] = TestDb.ConnectionString,
        }).Build());

    private static SamsaraSync Sync(Database db, string feed)
    {
        var client = new HttpClient(new StaticJsonHandler(feed))
        {
            BaseAddress = new Uri("https://samsara.invalid")
        };
        var services = new ServiceCollection().AddSingleton(db).BuildServiceProvider();
        return new SamsaraSync(client, services.GetRequiredService<IServiceScopeFactory>(), NullLogger.Instance);
    }

    private sealed class StaticJsonHandler(string body) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(body, Encoding.UTF8, "application/json"),
            });
    }
}
