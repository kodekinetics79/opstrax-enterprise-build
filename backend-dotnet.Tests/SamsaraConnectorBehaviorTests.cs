using System.Net;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Opstrax.Api.Services.Connectors;

namespace Opstrax.Tests;

public sealed class SamsaraConnectorBehaviorTests
{
    [Fact]
    public async Task Sync_DrainsAdvancingCursorPagesAndReturnsFinalCursor()
    {
        var handler = new ScriptedHandler(request =>
        {
            var query = request.RequestUri!.Query;
            return query.Contains("after=cursor-1", StringComparison.Ordinal)
                ? Json(HttpStatusCode.OK, """{"data":[],"pagination":{"endCursor":"cursor-2","hasNextPage":false}}""")
                : Json(HttpStatusCode.OK, """{"data":[],"pagination":{"endCursor":"cursor-1","hasNextPage":true}}""");
        });
        var connector = Connector(handler);
        using var body = OperationBody();

        var result = await connector.RunActionAsync("sync", Config(), body.RootElement, CancellationToken.None);

        Assert.True(result.Success, result.Message);
        Assert.Equal("cursor-2", result.Details!["nextCursor"]?.ToString());
        Assert.Equal(false, result.Details["hasNextPage"]);
        Assert.Equal(2, handler.Requests.Count);
    }

    [Fact]
    public async Task Sync_FailsWhenProviderRepeatsCursorInsteadOfLoopingForever()
    {
        var handler = new ScriptedHandler(_ =>
            Json(HttpStatusCode.OK, """{"data":[],"pagination":{"endCursor":"stuck","hasNextPage":true}}"""));
        var connector = Connector(handler);
        using var body = OperationBody("stuck");

        var result = await connector.RunActionAsync("sync", Config(), body.RootElement, CancellationToken.None);

        Assert.False(result.Success);
        Assert.Contains("did not advance", result.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Single(handler.Requests);
    }

    [Fact]
    public async Task Sync_RetriesProvider429ThenSucceeds()
    {
        var responses = new Queue<HttpResponseMessage>(
        [
            Json((HttpStatusCode)429, "{}"),
            Json(HttpStatusCode.OK, """{"data":[],"pagination":{"endCursor":null,"hasNextPage":false}}"""),
        ]);
        var handler = new ScriptedHandler(_ => responses.Dequeue());
        var connector = Connector(handler);
        using var body = OperationBody();

        var result = await connector.RunActionAsync("sync", Config(), body.RootElement, CancellationToken.None);

        Assert.True(result.Success, result.Message);
        Assert.Equal(2, handler.Requests.Count);
    }

    [Fact]
    public async Task Sync_PageBoundReturnsSuccessfulResumablePartialResult()
    {
        var page = 0;
        var handler = new ScriptedHandler(_ =>
        {
            page++;
            return Json(HttpStatusCode.OK,
                $$$"""{"data":[],"pagination":{"endCursor":"cursor-{{{page}}}","hasNextPage":true}}""");
        });
        var connector = Connector(handler, maxPages: 2);
        using var body = OperationBody();

        var result = await connector.RunActionAsync("sync", Config(), body.RootElement, CancellationToken.None);

        Assert.True(result.Success, result.Message);
        Assert.Equal("cursor-2", result.Details!["nextCursor"]?.ToString());
        Assert.Equal(true, result.Details["hasNextPage"]);
        Assert.Equal(true, result.Details["boundedPartial"]);
        Assert.Contains("returned cursor", result.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(2, handler.Requests.Count);
    }

    [Fact]
    public async Task Sync_ReportsInvalidProviderFixesWithoutFabricatingTelemetry()
    {
        var handler = new ScriptedHandler(_ => Json(HttpStatusCode.OK,
            """{"data":[{"id":"bad-fix","gps":{"time":"not-a-time","latitude":999,"longitude":-118.24,"speedMilesPerHour":40}}],"pagination":{"hasNextPage":false}}"""));
        var connector = Connector(handler);
        using var body = OperationBody();

        var result = await connector.RunActionAsync("sync", Config(), body.RootElement, CancellationToken.None);

        Assert.True(result.Success, result.Message);
        Assert.Equal(0, result.Details!["positionsWritten"]);
        Assert.Equal(1, result.Details["rejected"]);
        Assert.Contains("Rejected 1 invalid provider fix", result.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void SyncSource_DeduplicatesBeforeProjectionAndProjectsAlertsInOneTransaction()
    {
        var source = ReadRepositoryFile("backend-dotnet", "Services", "Connectors", "SamsaraSync.cs");
        Assert.Contains("RunInSystemTransactionAsync", source, StringComparison.Ordinal);
        Assert.Contains("if (eventId == 0) continue", source, StringComparison.Ordinal);
        Assert.Contains("WHERE existing.company_id=@cid AND existing.idempotency_key=@idem", source, StringComparison.Ordinal);
        Assert.Contains("ProjectAlertsAsync", source, StringComparison.Ordinal);
        Assert.Contains("sourceEventId", source, StringComparison.Ordinal);
        Assert.Contains("samsara-api", source, StringComparison.Ordinal);

        var duplicateGuard = source.IndexOf("if (eventId == 0) continue", StringComparison.Ordinal);
        var eventCountMutation = source.IndexOf("event_count=latest_vehicle_positions.event_count+1", StringComparison.Ordinal);
        var monotonicAlertGuard = source.IndexOf("if (projected > 0)", StringComparison.Ordinal);
        var alertProjection = source.IndexOf("await ProjectAlertsAsync", StringComparison.Ordinal);
        Assert.True(duplicateGuard >= 0 && eventCountMutation > duplicateGuard
                    && monotonicAlertGuard > eventCountMutation && alertProjection > monotonicAlertGuard,
            "A duplicate provider page must exit before changing latest event_count or creating alerts.");
        Assert.Contains("'Provisioning',@eventTime", source, StringComparison.Ordinal);
        Assert.DoesNotContain("'Provisioning',NOW()", source, StringComparison.Ordinal);
        Assert.Contains("pg_advisory_xact_lock", source, StringComparison.Ordinal);
        Assert.Contains("select-before-insert", source, StringComparison.Ordinal);
    }

    private static SamsaraConnector Connector(HttpMessageHandler handler, int maxPages = 200)
    {
        var services = new ServiceCollection().BuildServiceProvider();
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Samsara:MaxPagesPerSync"] = maxPages.ToString(),
                ["Samsara:InterPageDelayMs"] = "0",
            })
            .Build();
        return new SamsaraConnector(
            new StaticHttpClientFactory(handler),
            services.GetRequiredService<IServiceScopeFactory>(),
            configuration,
            NullLogger<SamsaraConnector>.Instance);
    }

    private static IReadOnlyDictionary<string, string?> Config() =>
        new Dictionary<string, string?> { ["apiToken"] = "test-token-never-sent-to-real-network" };

    private static JsonDocument OperationBody(string? cursor = null) => JsonDocument.Parse(
        JsonSerializer.Serialize(new
        {
            companyId = 17,
            integrationId = 23,
            operationGeneration = 0,
            operationLeaseToken = "11111111-1111-1111-1111-111111111111",
            cursor,
        }));

    private static HttpResponseMessage Json(HttpStatusCode status, string body) => new(status)
    {
        Content = new StringContent(body, Encoding.UTF8, "application/json")
    };

    private static string ReadRepositoryFile(params string[] parts)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !Directory.Exists(Path.Combine(dir.FullName, "backend-dotnet")))
            dir = dir.Parent;
        Assert.NotNull(dir);
        return File.ReadAllText(Path.Combine([dir!.FullName, .. parts]));
    }

    private sealed class StaticHttpClientFactory(HttpMessageHandler handler) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new(handler, disposeHandler: false);
    }

    private sealed class ScriptedHandler(Func<HttpRequestMessage, HttpResponseMessage> response) : HttpMessageHandler
    {
        public List<Uri> Requests { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests.Add(request.RequestUri!);
            return Task.FromResult(response(request));
        }
    }
}
