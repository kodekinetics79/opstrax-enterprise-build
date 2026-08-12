using System.Net;
using System.Text;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Opstrax.Api.Data;
using Opstrax.Api.Services.Connectors;

namespace Opstrax.Tests;

[Trait("Category", "Integration")]
public sealed class SamsaraSyncPostgresTests
{
    [Fact]
    public async Task ReplayedProviderPageDoesNotIncrementLatestOrCreateAnotherAlert()
    {
        var db = CreateDatabase();
        var suffix = Guid.NewGuid().ToString("N");
        var companyId = await db.InsertAsync(
            "INSERT INTO companies(company_code,name,industry) VALUES(@code,'Samsara replay test','Transportation') RETURNING id",
            c => c.Parameters.AddWithValue("@code", $"SAM-{suffix[..10]}"));
        var vehicleId = await db.InsertAsync(
            "INSERT INTO vehicles(company_id,vehicle_code,type) VALUES(@cid,@code,'truck') RETURNING id",
            c =>
            {
                c.Parameters.AddWithValue("@cid", companyId);
                c.Parameters.AddWithValue("@code", $"SV-{suffix[..10]}");
            });
        var providerVehicleId = $"provider-{suffix}";
        await db.ExecuteAsync(
            @"INSERT INTO eld_devices(company_id,device_serial,provider,vehicle_id,status,last_seen_at)
              VALUES(@cid,@serial,'Samsara',@vid,'Provisioning',NULL)",
            c =>
            {
                c.Parameters.AddWithValue("@cid", companyId);
                c.Parameters.AddWithValue("@serial", $"samsara-{providerVehicleId}");
                c.Parameters.AddWithValue("@vid", vehicleId);
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
            var observedAt = DateTimeOffset.UtcNow.AddSeconds(-10).ToString("O");
            var feed = $$$"""
                {"data":[{"id":"{{{providerVehicleId}}}","name":"Replay truck","gps":{"time":"{{{observedAt}}}","latitude":34.05,"longitude":-118.24,"headingDegrees":90,"speedMilesPerHour":80}}],"pagination":{"endCursor":"cursor-1","hasNextPage":false}}
                """;
            var client = new HttpClient(new StaticJsonHandler(feed))
            {
                BaseAddress = new Uri("https://samsara.invalid")
            };
            var services = new ServiceCollection().AddSingleton(db).BuildServiceProvider();
            var sync = new SamsaraSync(client, services.GetRequiredService<IServiceScopeFactory>(), NullLogger.Instance);

            var first = await sync.RunAsync(companyId, null, CancellationToken.None);
            var second = await sync.RunAsync(companyId, null, CancellationToken.None);

            Assert.Equal(1, first.PositionsWritten);
            Assert.Equal(0, second.PositionsWritten);
            Assert.Equal(1, await db.ScalarLongAsync(
                "SELECT COUNT(*) FROM location_events WHERE company_id=@cid AND source_channel='samsara-api'",
                c => c.Parameters.AddWithValue("@cid", companyId)));
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
            var olderAt = DateTimeOffset.UtcNow.AddHours(-2).ToString("O");
            var olderFeed = $$$"""
                {"data":[{"id":"{{{providerVehicleId}}}","name":"Replay truck","gps":{"time":"{{{olderAt}}}","latitude":35.05,"longitude":-119.24,"headingDegrees":90,"speedMilesPerHour":90}}],"pagination":{"endCursor":"cursor-older","hasNextPage":false}}
                """;
            var olderSync = Sync(db, olderFeed);
            Assert.Equal(0, (await olderSync.RunAsync(companyId, null, CancellationToken.None)).PositionsWritten);
            Assert.Equal(2, await db.ScalarLongAsync(
                "SELECT COUNT(*) FROM location_events WHERE company_id=@cid AND source_channel='samsara-api'",
                c => c.Parameters.AddWithValue("@cid", companyId)));
            Assert.Equal(1, await db.ScalarLongAsync(
                "SELECT event_count FROM latest_vehicle_positions WHERE company_id=@cid AND vehicle_id=@vid",
                c => { c.Parameters.AddWithValue("@cid", companyId); c.Parameters.AddWithValue("@vid", vehicleId); }));
            Assert.Equal(0, await db.ScalarLongAsync(
                "SELECT COUNT(*) FROM telemetry_alerts WHERE company_id=@cid AND status='Open'",
                c => c.Parameters.AddWithValue("@cid", companyId)));

            // A first, buffered fix for a newly discovered provider device must not stamp NOW().
            var newProviderVehicleId = $"new-{suffix}";
            var staleFirstFeed = $$$"""
                {"data":[{"id":"{{{newProviderVehicleId}}}","gps":{"time":"{{{olderAt}}}","latitude":34.05,"longitude":-118.24,"headingDegrees":0,"speedMilesPerHour":0}}],"pagination":{"hasNextPage":false}}
                """;
            await Sync(db, staleFirstFeed).RunAsync(companyId, null, CancellationToken.None);
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
        }
        finally
        {
            await db.ExecuteAsync("DELETE FROM telemetry_alerts WHERE company_id=@cid", c => c.Parameters.AddWithValue("@cid", companyId));
            await db.ExecuteAsync("DELETE FROM latest_vehicle_positions WHERE company_id=@cid", c => c.Parameters.AddWithValue("@cid", companyId));
            await db.ExecuteAsync("DELETE FROM location_events WHERE company_id=@cid", c => c.Parameters.AddWithValue("@cid", companyId));
            await db.ExecuteAsync("DELETE FROM geofences WHERE company_id=@cid", c => c.Parameters.AddWithValue("@cid", companyId));
            await db.ExecuteAsync("DELETE FROM telemetry_rules WHERE company_id=@cid", c => c.Parameters.AddWithValue("@cid", companyId));
            await db.ExecuteAsync("DELETE FROM eld_devices WHERE company_id=@cid", c => c.Parameters.AddWithValue("@cid", companyId));
            await db.ExecuteAsync("DELETE FROM vehicles WHERE company_id=@cid", c => c.Parameters.AddWithValue("@cid", companyId));
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
