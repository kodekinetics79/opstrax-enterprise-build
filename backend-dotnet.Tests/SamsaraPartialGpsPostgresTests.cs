using System.Net;
using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Opstrax.Api.Data;
using Opstrax.Api.Controllers;
using Opstrax.Api.Services;
using Opstrax.Api.Services.Connectors;

namespace Opstrax.Tests;

[Trait("Category", "Integration")]
public sealed class SamsaraPartialGpsPostgresTests
{
    [Theory]
    [InlineData(null, null)]
    [InlineData(null, 0)]
    [InlineData(0d, null)]
    [InlineData(0d, 0)]
    public async Task UnknownAndMeasuredZeroRemainDistinctThroughHistoryLatestAndLive(double? speed, int? heading)
    {
        await using var fixture = await Fixture.Create();
        var at = DateTimeOffset.UtcNow.AddMinutes(-2);

        async Task<SamsaraSync.SyncSummary> Run(double? spd, int? hdg, DateTimeOffset time, bool explicitNull = false, bool mixedPage = false)
        {
            var gps = SamsaraFeedArrayTests.Gps(0);
            gps["time"] = time.ToString("O");
            if (spd is not null || explicitNull) gps["speedMilesPerHour"] = spd; else gps.Remove("speedMilesPerHour");
            if (hdg is not null || explicitNull) gps["headingDegrees"] = hdg; else gps.Remove("headingDegrees");
            var vehicle = SamsaraFeedArrayTests.Vehicle(fixture.ProviderVehicleId, gps);
            if (mixedPage)
            {
                var before = SamsaraFeedArrayTests.Gps(0); before["time"] = time.AddSeconds(-5).ToString("O");
                var after = SamsaraFeedArrayTests.Gps(0); after["time"] = time.AddSeconds(-3).ToString("O");
                vehicle["gps"] = new JsonArray(before, gps.DeepClone(), after);
            }
            var page = SamsaraFeedArrayTests.Page(vehicle);
            using var client = new HttpClient(new JsonHandler(page.ToJsonString())) { BaseAddress = new Uri("https://samsara.invalid") };
            using var services = new ServiceCollection().AddSingleton(fixture.Db).AddSingleton<TelemetryLiveStateService>().BuildServiceProvider();
            return await new SamsaraSync(client, services.GetRequiredService<IServiceScopeFactory>(), NullLogger.Instance, allowPartialGpsMeasurements: true)
                .RunAsync(fixture.Operation!, "before", CancellationToken.None);
        }

        async Task Stored(double? spd, int? hdg, int count)
        {
            foreach (var table in new[] { "location_events", "latest_vehicle_positions", "telemetry_live_asset_states" })
            {
                var timeColumn = table == "telemetry_live_asset_states" ? "last_event_time" : "event_time";
                var row = await fixture.Db.QuerySingleAsync($"SELECT speed_mph,heading FROM {table} WHERE company_id=@cid ORDER BY {timeColumn} DESC LIMIT 1",
                    c => c.Parameters.AddWithValue("@cid", fixture.CompanyId));
                Assert.NotNull(row);
                Assert.Equal(spd, row!["speedMph"] is { } storedSpeed ? Convert.ToDouble(storedSpeed) : null);
                Assert.Equal(hdg, row["heading"] is { } storedHeading ? Convert.ToInt32(storedHeading) : null);
            }
            Assert.Equal(count, await fixture.Count("SELECT event_count FROM latest_vehicle_positions WHERE company_id=@cid"));
            // The actual live-entity transformation and its JSON preserve explicit null.
            var live = await new TelemetryLiveStateService(fixture.Db).ListLiveStatesAsync(fixture.CompanyId);
            var entities = (IReadOnlyList<Dictionary<string, object?>>)typeof(TelemetryLiveStateService)
                .GetMethod("BuildEntities", BindingFlags.NonPublic | BindingFlags.Static)!.Invoke(null, [live])!;
            var entity = entities.Single();
            Assert.Equal(spd, entity["speedMph"] is { } entitySpeed ? Convert.ToDouble(entitySpeed) : null);
            Assert.Equal(hdg, entity["heading"] is { } entityHeading ? Convert.ToInt32(entityHeading) : null);
            var json = JsonSerializer.SerializeToElement(entity);
            Assert.Equal(spd, json.GetProperty("speedMph").ValueKind == JsonValueKind.Null ? null : json.GetProperty("speedMph").GetDouble());
            Assert.Equal(hdg, json.GetProperty("heading").ValueKind == JsonValueKind.Null ? null : json.GetProperty("heading").GetInt32());
        }

        async Task ApiMeasurements(double? spd, int? hdg)
        {
            var http = new DefaultHttpContext();
            http.Request.QueryString = new QueryString($"?vehicleId={fixture.VehicleId}");
            http.Items[EndpointMappings.AuthCompanyIdItemKey] = fixture.CompanyId;
            http.Items[EndpointMappings.AuthUserIdItemKey] = 1L;
            http.Items[EndpointMappings.AuthRoleItemKey] = "Synthetic partial GPS tester";
            http.Items[EndpointMappings.AuthPermissionsItemKey] = new[] { "telemetry.live_state.read" };
            foreach (var methodName in new[] { "TelemetryPositions", "TelemetryBreadcrumbs" })
            {
                var method = typeof(EndpointMappings).GetMethod(methodName, BindingFlags.NonPublic | BindingFlags.Static)!;
                var result = await (Task<IResult>)method.Invoke(null, [http, fixture.Db, CancellationToken.None])!;
                Assert.Equal(200, Assert.IsAssignableFrom<IStatusCodeHttpResult>(result).StatusCode);
                var json = JsonSerializer.SerializeToElement(Assert.IsAssignableFrom<IValueHttpResult>(result).Value, new JsonSerializerOptions(JsonSerializerDefaults.Web));
                var points = methodName == "TelemetryPositions" ? json.GetProperty("data") : json.GetProperty("data").GetProperty("points");
                var point = points[points.GetArrayLength() - 1];
                Assert.Equal(spd, point.GetProperty("speedMph").ValueKind == JsonValueKind.Null ? null : point.GetProperty("speedMph").GetDouble());
                // History may serialize its numeric column as 0.00; assert the
                // numeric measurement, not an integer-only JSON token spelling.
                Assert.Equal((double?)hdg, point.GetProperty("heading").ValueKind == JsonValueKind.Null ? null : point.GetProperty("heading").GetDouble());
            }
        }

        var first = await Run(speed, heading, at);
        Assert.Equal(1, first.PositionsWritten);
        Assert.Equal("retained-cursor", first.NextCursor);
        Assert.Equal(0, first.Rejected);
        await Stored(speed, heading, 1);
        await ApiMeasurements(speed, heading);
        Assert.Equal(1, (await Run(0, 0, at.AddSeconds(10))).PositionsWritten);
        await Stored(0, 0, 2);
        Assert.Equal(1, (await Run(40, 90, at.AddSeconds(20))).PositionsWritten);
        await Stored(40, 90, 3);
        Assert.Equal(1, (await Run(null, null, at.AddSeconds(30), true)).PositionsWritten);
        Assert.Equal(0, (await Run(99, 180, at.AddSeconds(15))).PositionsWritten);
        Assert.Equal(0, (await Run(null, null, at.AddSeconds(30), true)).PositionsWritten);
        await Stored(null, null, 4);
        Assert.Equal(5, await fixture.Count("SELECT COUNT(*) FROM location_events WHERE company_id=@cid"));
        Assert.Equal(0, await fixture.Count("SELECT COUNT(*) FROM telemetry_alerts WHERE company_id=@cid"));
        // Known high speed with unknown heading must still raise one speeding alert.
        Assert.Equal(1, (await Run(80, null, at.AddSeconds(40))).PositionsWritten);
        Assert.Equal(1, await fixture.Count("SELECT COUNT(*) FROM telemetry_alerts WHERE company_id=@cid AND alert_type='speeding' AND status='Open'"));
        Assert.Equal(1, (await Run(null, null, at.AddSeconds(50))).PositionsWritten);
        Assert.Equal(1, await fixture.Count("SELECT COUNT(*) FROM telemetry_alerts WHERE company_id=@cid AND alert_type='speeding' AND status='Open'"));
        await Stored(null, null, 6);
        var mixed = await Run(null, null, at.AddSeconds(60), explicitNull: true, mixedPage: true);
        Assert.Equal(2, mixed.PositionsWritten); // known prefix, newer partial, older known suffix
        Assert.Equal(1, mixed.VehiclesSeen);
        Assert.Equal(0, mixed.Rejected);
        Assert.Equal("retained-cursor", mixed.NextCursor);
        await Stored(null, null, 8);
        await ApiMeasurements(null, null);
        Assert.Equal(10, await fixture.Count("SELECT COUNT(*) FROM location_events WHERE company_id=@cid"));
        var freshness = await fixture.Db.QuerySingleAsync("SELECT provider_last_event_at FROM integrations WHERE company_id=@cid", c => c.Parameters.AddWithValue("@cid", fixture.CompanyId));
        Assert.Equal(at.AddSeconds(60).UtcDateTime, Convert.ToDateTime(freshness!["providerLastEventAt"]).ToUniversalTime(), TimeSpan.FromMilliseconds(1));
    }

    [Theory]
    [InlineData("On", false, false, true, false)]
    [InlineData("Idle", false, false, true, false)]
    [InlineData("On", true, false, false, false)]
    [InlineData("On", false, true, false, false)]
    [InlineData(null, false, false, false, false)]
    [InlineData("Unknown", false, false, false, false)]
    [InlineData("", false, false, false, false)]
    [InlineData("Off", false, false, false, false)]
    [InlineData("Running", false, false, false, false)]
    [InlineData("On", false, false, false, true)]
    public async Task IdlingRequiresAffirmativeEngineAndSpeedForEveryObservedSample(string? engine, bool mixedSpeed, bool mixedEngine, bool expected, bool allSpeedMissing)
    {
        await using var fixture = await Fixture.Create();
        for (var index = 0; index < 3; index++)
        {
            await fixture.Db.ExecuteAsync(@"INSERT INTO location_events(company_id,vehicle_id,lat,lng,speed_mph,heading,engine_status,event_time,source)
                VALUES(@cid,@vid,34.05,-118.24,@speed,NULL,@engine,NOW()-(@minutes*INTERVAL '1 minute'),'samsara')",
                c =>
                {
                    c.Parameters.AddWithValue("@cid", fixture.CompanyId);
                    c.Parameters.AddWithValue("@vid", fixture.VehicleId);
                    c.Parameters.AddWithValue("@speed", allSpeedMissing || mixedSpeed && index > 0 ? DBNull.Value : (object)0m);
                    c.Parameters.AddWithValue("@engine", mixedEngine && index == 1 ? DBNull.Value : (object?)engine ?? DBNull.Value);
                    c.Parameters.AddWithValue("@minutes", new[] { 14, 7, 1 }[index]);
                });
        }
        var sql = (string)typeof(OperationalAlertDetectionService).GetField("IdlingSql", BindingFlags.NonPublic | BindingFlags.Static)!.GetRawConstantValue()!;
        // Restrict this execution of the actual detector SQL to the test-owned tenant.
        sql = sql.Replace("WHERE le.vehicle_id IS NOT NULL", "WHERE le.company_id=@testCompany AND le.vehicle_id IS NOT NULL", StringComparison.Ordinal);
        await fixture.Db.ExecuteAsync(sql, c => c.Parameters.AddWithValue("@testCompany", fixture.CompanyId));
        Assert.Equal(expected ? 1 : 0, await fixture.Count("SELECT COUNT(*) FROM telemetry_alerts WHERE company_id=@cid AND alert_type='idling'"));
        if (expected)
        {
            await fixture.Db.ExecuteAsync("INSERT INTO location_events(company_id,vehicle_id,lat,lng,speed_mph,heading,engine_status,event_time,source) VALUES(@cid,@vid,34.05,-118.24,NULL,NULL,NULL,NOW(),'samsara')",
                c => { c.Parameters.AddWithValue("@cid", fixture.CompanyId); c.Parameters.AddWithValue("@vid", fixture.VehicleId); });
            await fixture.Db.ExecuteAsync(sql, c => c.Parameters.AddWithValue("@testCompany", fixture.CompanyId));
            Assert.Equal(1, await fixture.Count("SELECT COUNT(*) FROM telemetry_alerts WHERE company_id=@cid AND alert_type='idling' AND status='Open'"));
        }
        await fixture.Db.ExecuteAsync(sql, c => c.Parameters.AddWithValue("@testCompany", fixture.CompanyId));
        Assert.Equal(expected ? 1 : 0, await fixture.Count("SELECT COUNT(*) FROM telemetry_alerts WHERE company_id=@cid AND alert_type='idling'"));
    }

    private sealed class Fixture : IAsyncDisposable
    {
        internal Database Db { get; } = new(new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
            { ["ConnectionStrings:DefaultConnection"] = TestDb.ConnectionString }).Build());
        internal long CompanyId, VehicleId;
        internal string ProviderVehicleId = $"synthetic-partial-{Guid.NewGuid():N}";
        internal ConnectorOperationContext? Operation;

        internal static async Task<Fixture> Create()
        {
            var f = new Fixture();
            try
            {
                var suffix = Guid.NewGuid().ToString("N")[..10];
                f.CompanyId = await f.Db.InsertAsync("INSERT INTO companies(company_code,name,industry) VALUES(@code,'Synthetic partial GPS','Transportation') RETURNING id", c => c.Parameters.AddWithValue("@code", $"SPG-{suffix}"));
                var integrationId = await f.Db.InsertAsync("INSERT INTO integrations(company_id,provider_name,category,status,integration_key,config_json) VALUES(@cid,'Samsara','Telematics & ELD','Connected','samsara','{}') RETURNING id", c => c.Parameters.AddWithValue("@cid", f.CompanyId));
                var branchId = await f.Db.InsertAsync("INSERT INTO branches(company_id,branch_code,name,status) VALUES(@cid,@code,'Synthetic branch','Active') RETURNING id", c => { c.Parameters.AddWithValue("@cid", f.CompanyId); c.Parameters.AddWithValue("@code", $"SPG-B-{suffix}"); });
                f.VehicleId = await f.Db.InsertAsync("INSERT INTO vehicles(company_id,branch_id,vehicle_code,type,vin_exception_type,alternate_identifier) VALUES(@cid,@bid,@code,'truck','legacy-fleet-identifier',@code) RETURNING id", c => { c.Parameters.AddWithValue("@cid", f.CompanyId); c.Parameters.AddWithValue("@bid", branchId); c.Parameters.AddWithValue("@code", $"SPG-V-{suffix}"); });
                var deviceId = await f.Db.InsertAsync("INSERT INTO eld_devices(company_id,device_serial,provider,vehicle_id,status) VALUES(@cid,@serial,'Samsara',@vid,'Provisioning') RETURNING id", c => { c.Parameters.AddWithValue("@cid", f.CompanyId); c.Parameters.AddWithValue("@vid", f.VehicleId); c.Parameters.AddWithValue("@serial", $"samsara-{f.ProviderVehicleId}"); });
                await f.Db.ExecuteAsync(@"INSERT INTO device_installations(company_id,branch_id,device_id,vehicle_id,status,device_role,is_primary,effective_from,installed_at,source)
                    VALUES(@cid,@bid,@did,@vid,'Installed','GPS',TRUE,NOW()-INTERVAL '3 hours',NOW()-INTERVAL '3 hours','synthetic-partial-test')",
                    c => { c.Parameters.AddWithValue("@cid", f.CompanyId); c.Parameters.AddWithValue("@bid", branchId); c.Parameters.AddWithValue("@did", deviceId); c.Parameters.AddWithValue("@vid", f.VehicleId); });
                f.Operation = await ConnectorOperationLease.TryAcquireAsync(f.Db, f.CompanyId, integrationId, ["Connected"], TimeSpan.FromSeconds(180), CancellationToken.None);
                Assert.NotNull(f.Operation);
                return f;
            }
            catch { await f.DisposeAsync(); throw; }
        }

        internal Task<long> Count(string sql) => Db.ScalarLongAsync(sql, c => c.Parameters.AddWithValue("@cid", CompanyId));
        public async ValueTask DisposeAsync()
        {
            if (CompanyId == 0) return;
            foreach (var table in new[] { "telemetry_alerts", "telemetry_live_asset_states", "latest_vehicle_positions", "location_events", "device_installations", "eld_devices", "vehicles", "branches", "integrations" })
                await Db.ExecuteAsync($"DELETE FROM {table} WHERE company_id=@cid", c => c.Parameters.AddWithValue("@cid", CompanyId));
            await Db.ExecuteAsync("DELETE FROM companies WHERE id=@cid", c => c.Parameters.AddWithValue("@cid", CompanyId));
        }
    }

    private sealed class JsonHandler(string body) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(body, Encoding.UTF8, "application/json") });
    }
}
