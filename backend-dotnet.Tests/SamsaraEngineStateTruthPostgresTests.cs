using System.Net;
using System.Text;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Opstrax.Api.Data;
using Opstrax.Api.Services;
using Opstrax.Api.Services.Connectors;

namespace Opstrax.Tests;

[Trait("Category", "Integration")]
public sealed class SamsaraEngineStateTruthPostgresTests
{
    [Theory]
    [InlineData(null, null)]
    [InlineData("null", null)]
    [InlineData("{}", null)]
    [InlineData("{\"value\":null}", null)]
    [InlineData("{\"value\":\"\"}", null)]
    [InlineData("{\"value\":\"   \"}", null)]
    [InlineData("{\"value\":\"Off\"}", "Off")]
    [InlineData("{\"value\":\"On\"}", "On")]
    [InlineData("{\"value\":\"Idle\"}", "Idle")]
    [InlineData("{\"value\":\"Unknown\"}", "Unknown")]
    public async Task EngineEvidenceRemainsConsistentAcrossHistoryLatestAndLiveProjection(
        string? engineJson, string? expectedEngine)
    {
        var db = new Database(new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["ConnectionStrings:DefaultConnection"] = TestDb.ConnectionString,
        }).Build());
        var suffix = Guid.NewGuid().ToString("N");
        var companyId = await db.InsertAsync(
            "INSERT INTO companies(company_code,name,industry) VALUES(@code,'Synthetic engine truth regression','Transportation') RETURNING id",
            c => c.Parameters.AddWithValue("@code", $"SET-{suffix[..10]}"));
        try
        {
            var integrationId = await db.InsertAsync(
                @"INSERT INTO integrations(company_id,provider_name,category,status,integration_key,config_json)
                  VALUES(@cid,'Samsara','Telematics & ELD','Connected','samsara','{}'::jsonb) RETURNING id",
                c => c.Parameters.AddWithValue("@cid", companyId));
            var branchId = await db.InsertAsync(
                "INSERT INTO branches(company_id,branch_code,name,status) VALUES(@cid,@code,'Synthetic branch','Active') RETURNING id",
                c => { c.Parameters.AddWithValue("@cid", companyId); c.Parameters.AddWithValue("@code", $"SET-B-{suffix[..8]}"); });
            var vehicleId = await db.InsertAsync(
                @"INSERT INTO vehicles(company_id,branch_id,vehicle_code,type,vin_exception_type,alternate_identifier)
                  VALUES(@cid,@branch,@code,'truck','legacy-fleet-identifier',@code) RETURNING id",
                c =>
                {
                    c.Parameters.AddWithValue("@cid", companyId);
                    c.Parameters.AddWithValue("@branch", branchId);
                    c.Parameters.AddWithValue("@code", $"SET-V-{suffix[..8]}");
                });
            var providerVehicleId = $"synthetic-engine-{suffix}";
            var deviceId = await db.InsertAsync(
                @"INSERT INTO eld_devices(company_id,device_serial,provider,vehicle_id,status)
                  VALUES(@cid,@serial,'Samsara',@vid,'Provisioning') RETURNING id",
                c =>
                {
                    c.Parameters.AddWithValue("@cid", companyId);
                    c.Parameters.AddWithValue("@serial", $"samsara-{providerVehicleId}");
                    c.Parameters.AddWithValue("@vid", vehicleId);
                });
            await db.ExecuteAsync(
                @"INSERT INTO device_installations
                    (company_id,branch_id,device_id,vehicle_id,status,device_role,is_primary,effective_from,installed_at,source)
                  VALUES(@cid,@branch,@device,@vehicle,'Installed','GPS',TRUE,NOW()-INTERVAL '3 hours',NOW()-INTERVAL '3 hours','synthetic-engine-test')",
                c =>
                {
                    c.Parameters.AddWithValue("@cid", companyId);
                    c.Parameters.AddWithValue("@branch", branchId);
                    c.Parameters.AddWithValue("@device", deviceId);
                    c.Parameters.AddWithValue("@vehicle", vehicleId);
                });
            var operation = await ConnectorOperationLease.TryAcquireAsync(
                db, companyId, integrationId, ["Connected"], TimeSpan.FromSeconds(180), CancellationToken.None);
            Assert.NotNull(operation);
            using var services = new ServiceCollection().AddSingleton(db)
                .AddSingleton<TelemetryLiveStateService>().BuildServiceProvider();
            var observedAt = DateTimeOffset.UtcNow.AddMinutes(-1);

            async Task<SamsaraSync.SyncSummary> Run(string? state, DateTimeOffset eventAt, bool batch = false)
            {
                var gps = new JsonObject
                {
                    ["time"] = eventAt.ToString("O"), ["latitude"] = 34.05,
                    ["longitude"] = -118.24, ["speedMilesPerHour"] = 0, ["headingDegrees"] = 90,
                };
                if (state is not null)
                    gps["decorations"] = new JsonObject { ["engineStates"] = JsonNode.Parse(state) };
                var events = new JsonArray(gps);
                if (batch)
                {
                    var older = (JsonObject)gps.DeepClone();
                    older["time"] = eventAt.AddSeconds(-5).ToString("O");
                    older["decorations"] = new JsonObject { ["engineStates"] = new JsonObject { ["value"] = "On" } };
                    events.Add(older);
                    events.Add(gps.DeepClone());
                }
                var vehicle = new JsonObject
                {
                    ["id"] = providerVehicleId, ["gps"] = events,
                };
                var feed = new JsonObject
                {
                    ["data"] = new JsonArray(vehicle),
                    ["pagination"] = new JsonObject { ["endCursor"] = "engine-cursor", ["hasNextPage"] = false },
                };
                using var client = new HttpClient(new StaticJsonHandler(feed.ToJsonString()))
                {
                    BaseAddress = new Uri("https://samsara.invalid"),
                };
                var sync = new SamsaraSync(client, services.GetRequiredService<IServiceScopeFactory>(), NullLogger.Instance);
                return await sync.RunAsync(operation!, null, CancellationToken.None);
            }

            async Task AssertStored(string table, string? expected, int eventCount)
            {
                // Only fixed test-owned table names reach this helper.
                var timeColumn = table == "telemetry_live_asset_states" ? "last_event_time" : "event_time";
                var row = await db.QuerySingleAsync(
                    $"SELECT engine_status FROM {table} WHERE company_id=@cid AND vehicle_id=@vid ORDER BY {timeColumn} DESC LIMIT 1",
                    c => { c.Parameters.AddWithValue("@cid", companyId); c.Parameters.AddWithValue("@vid", vehicleId); });
                Assert.NotNull(row);
                Assert.Equal(expected, row!["engineStatus"]?.ToString());
                Assert.Equal(eventCount, await db.ScalarLongAsync(
                    "SELECT event_count FROM latest_vehicle_positions WHERE company_id=@cid AND vehicle_id=@vid",
                    c => { c.Parameters.AddWithValue("@cid", companyId); c.Parameters.AddWithValue("@vid", vehicleId); }));
            }

            // Exercise INSERT with and without explicit engine evidence.
            Assert.Equal(1, (await Run(engineJson, observedAt)).PositionsWritten);
            foreach (var table in new[] { "location_events", "latest_vehicle_positions", "telemetry_live_asset_states" })
                await AssertStored(table, expectedEngine, 1);

            // A newer explicit value may replace unknown; a later missing value must not
            // resurrect it or invent a running state. Older/replayed pages remain no-ops.
            Assert.Equal(1, (await Run("{\"value\":\"Off\"}", observedAt.AddSeconds(10))).PositionsWritten);
            foreach (var table in new[] { "location_events", "latest_vehicle_positions", "telemetry_live_asset_states" })
                await AssertStored(table, "Off", 2);
            Assert.Equal(1, (await Run(null, observedAt.AddSeconds(20))).PositionsWritten);
            Assert.Equal(0, (await Run("{\"value\":\"On\"}", observedAt.AddSeconds(5))).PositionsWritten);
            Assert.Equal(0, (await Run(null, observedAt.AddSeconds(20))).PositionsWritten);
            foreach (var table in new[] { "location_events", "latest_vehicle_positions", "telemetry_live_asset_states" })
                await AssertStored(table, null, 3);
            Assert.Equal(4, await db.ScalarLongAsync("SELECT COUNT(*) FROM location_events WHERE company_id=@cid",
                c => c.Parameters.AddWithValue("@cid", companyId)));
            // A canonical batch contains the newest fix, an older novel fix and an
            // exact replay. Retain both unique history events, but advance latest once
            // and count the provider vehicle once, not once per GPS array element.
            var batched = await Run("{\"value\":\"Idle\"}", observedAt.AddSeconds(40), batch: true);
            Assert.Equal(1, batched.VehiclesSeen);
            Assert.Equal(1, batched.PositionsWritten);
            foreach (var table in new[] { "location_events", "latest_vehicle_positions", "telemetry_live_asset_states" })
                await AssertStored(table, "Idle", 4);
            Assert.Equal(6, await db.ScalarLongAsync("SELECT COUNT(*) FROM location_events WHERE company_id=@cid",
                c => c.Parameters.AddWithValue("@cid", companyId)));
            Assert.Equal(0, await db.ScalarLongAsync("SELECT COUNT(*) FROM telemetry_alerts WHERE company_id=@cid",
                c => c.Parameters.AddWithValue("@cid", companyId)));
        }
        finally
        {
            // Delete only synthetic rows owned by this individual case, never reset shared schema/data.
            foreach (var table in new[] { "telemetry_alerts", "telemetry_live_asset_states", "latest_vehicle_positions", "location_events", "device_installations", "eld_devices", "vehicles", "branches", "integrations" })
                await db.ExecuteAsync($"DELETE FROM {table} WHERE company_id=@cid", c => c.Parameters.AddWithValue("@cid", companyId));
            await db.ExecuteAsync("DELETE FROM companies WHERE id=@cid", c => c.Parameters.AddWithValue("@cid", companyId));
        }
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
