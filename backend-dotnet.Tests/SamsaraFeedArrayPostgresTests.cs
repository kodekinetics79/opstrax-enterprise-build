using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Configuration;
using Opstrax.Api.Data;
using Opstrax.Api.Services.Connectors;

namespace Opstrax.Tests;

[Trait("Category", "Integration")]
public sealed class SamsaraFeedArrayPostgresTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task LaterUnsupportedPageKeepsOnlyPreviousCommittedPageAndResumeCursor(bool wrongContainer)
    {
        var db = new Database(new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["ConnectionStrings:DefaultConnection"] = TestDb.ConnectionString,
        }).Build());
        var suffix = Guid.NewGuid().ToString("N");
        var companyId = await db.InsertAsync(
            "INSERT INTO companies(company_code,name,industry) VALUES(@code,'Synthetic feed contract test','Transportation') RETURNING id",
            c => c.Parameters.AddWithValue("@code", $"SFA-{suffix[..10]}"));
        try
        {
            var integrationId = await db.InsertAsync(
                @"INSERT INTO integrations(company_id,provider_name,category,status,integration_key,config_json)
                  VALUES(@cid,'Samsara','Telematics & ELD','Connected','samsara','{}'::jsonb) RETURNING id",
                c => c.Parameters.AddWithValue("@cid", companyId));
            var operation = await ConnectorOperationLease.TryAcquireAsync(
                db, companyId, integrationId, ["Connected"], TimeSpan.FromSeconds(180), CancellationToken.None);
            Assert.NotNull(operation);
            var firstGps = SamsaraFeedArrayTests.Gps(0);
            var firstTime = DateTimeOffset.Parse(firstGps["time"]!.GetValue<string>());
            var first = SamsaraFeedArrayTests.Page(SamsaraFeedArrayTests.Vehicle($"first-{suffix}", firstGps));
            first["pagination"] = new JsonObject { ["endCursor"] = "complete-1", ["hasNextPage"] = true };

            var badGps = SamsaraFeedArrayTests.Gps(20);
            badGps.Remove("headingDegrees");
            var bad = wrongContainer
                ? new JsonObject { ["id"] = $"bad-{suffix}", ["gps"] = SamsaraFeedArrayTests.Gps(20) }
                : SamsaraFeedArrayTests.Vehicle($"bad-{suffix}", badGps);
            // Even a valid prefix on the unsupported page must not enter any database
            // table or advance provider freshness before full page validation succeeds.
            var second = SamsaraFeedArrayTests.Page(
                SamsaraFeedArrayTests.Vehicle($"prefix-{suffix}", SamsaraFeedArrayTests.Gps(10)), bad);
            second["pagination"] = new JsonObject { ["endCursor"] = "must-not-promote", ["hasNextPage"] = false };
            var requests = new List<string>();
            var connector = SamsaraResponseBoundsTests.Connector(request =>
            {
                requests.Add(request.RequestUri!.Query);
                return Task.FromResult(SamsaraResponseBoundsTests.Json((requests.Count == 1 ? first : second).ToJsonString()));
            }, db);
            using var body = JsonDocument.Parse(JsonSerializer.Serialize(new
            {
                companyId, integrationId, operationGeneration = operation!.Generation,
                operationLeaseToken = operation.LeaseToken.ToString(), cursor = "before-start",
            }));
            var result = await connector.RunActionAsync("sync",
                new Dictionary<string, string?> { ["apiToken"] = "synthetic-token" }, body.RootElement, CancellationToken.None);

            Assert.False(result.Success);
            Assert.Contains(wrongContainer ? "gps must be an array" : "headingDegrees", result.Message, StringComparison.Ordinal);
            Assert.Equal(1, result.Details!["pagesCommitted"]);
            Assert.Equal("complete-1", result.Details["nextCursor"]);
            Assert.Equal(1, result.Details["vehiclesSeen"]);
            Assert.Equal(1, result.Details["unmatched"]);
            Assert.Equal(0, result.Details["positionsWritten"]);
            Assert.Equal(new[]
            {
                "?types=gps,engineStates,obdOdometerMeters&after=before-start",
                "?types=gps,engineStates,obdOdometerMeters&after=complete-1",
            }, requests);
            Assert.Equal(1, await db.ScalarLongAsync("SELECT COUNT(*) FROM location_events WHERE company_id=@cid",
                c => c.Parameters.AddWithValue("@cid", companyId)));
            Assert.Equal(1, await db.ScalarLongAsync("SELECT COUNT(*) FROM eld_devices WHERE company_id=@cid",
                c => c.Parameters.AddWithValue("@cid", companyId)));
            Assert.Equal(0, await db.ScalarLongAsync("SELECT COUNT(*) FROM latest_vehicle_positions WHERE company_id=@cid",
                c => c.Parameters.AddWithValue("@cid", companyId)));
            var freshness = await db.QuerySingleAsync("SELECT provider_last_event_at FROM integrations WHERE company_id=@cid AND id=@id",
                c => { c.Parameters.AddWithValue("@cid", companyId); c.Parameters.AddWithValue("@id", integrationId); });
            Assert.Equal(firstTime.UtcDateTime, Convert.ToDateTime(freshness!["providerLastEventAt"]).ToUniversalTime(), TimeSpan.FromMilliseconds(1));
        }
        finally
        {
            foreach (var table in new[] { "location_events", "eld_devices", "integrations" })
                await db.ExecuteAsync($"DELETE FROM {table} WHERE company_id=@cid", c => c.Parameters.AddWithValue("@cid", companyId));
            await db.ExecuteAsync("DELETE FROM companies WHERE id=@cid", c => c.Parameters.AddWithValue("@cid", companyId));
        }
    }
}
