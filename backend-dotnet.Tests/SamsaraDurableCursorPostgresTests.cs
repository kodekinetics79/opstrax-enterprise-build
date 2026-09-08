using System.Net;
using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Npgsql;
using Opstrax.Api.Controllers;
using Opstrax.Api.Data;
using Opstrax.Api.DTOs;
using Opstrax.Api.Security;
using Opstrax.Api.Services;
using Opstrax.Api.Services.Connectors;

namespace Opstrax.Tests;

// Real endpoint -> connector -> page transactions -> fenced finalizer -> restart.
// HTTP is a synthetic in-process fixture; these tests are NOT provider, browser,
// worker scheduling, RLS authorization, pilot, or certification evidence.
// Requires an explicitly selected disposable local DB with the application schema.
[Trait("Category", "Integration")]
public sealed class SamsaraDurableCursorPostgresTests
{
    private const string SyntheticToken = "samsara-durable-cursor-synthetic-token";
    private const string OriginalCursor = "before /+?=checkpoint";
    private const string SyncTypes = "gps,engineStates,obdOdometerMeters";

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task TerminalOrCappedSuccessPersistsCheckpointForANewRuntime(bool capped)
    {
        await using var fixture = await Fixture.CreateAsync();
        using (var first = fixture.Runtime(_ => Json(fixture.Page("after-1", capped, fixture.Gps(1))),
                   maxPages: capped ? 1 : 20))
        {
            await fixture.SeedAsync(first.Registry);
            var result = SyncData(await first.InvokeAsync("IntegrationSync"));
            Assert.True(result.GetProperty("success").GetBoolean());
            Assert.Equal(capped, result.GetProperty("details").GetProperty("boundedPartial").GetBoolean());
            Assert.Equal(capped, result.GetProperty("details").GetProperty("hasNextPage").GetBoolean());
            Assert.Equal("after-1", result.GetProperty("details").GetProperty("nextCursor").GetString());
            AssertSyncRequest(Assert.Single(first.Requests), OriginalCursor);
        }

        await fixture.AssertStateAsync("Connected", "after-1", true, historyCount: 1);
        // No connector, scope, HTTP client, or in-memory result survives this boundary.
        using var restarted = fixture.Runtime(_ => Json(fixture.Page("after-2", false, fixture.Gps(2))));
        Assert.True(SyncData(await restarted.InvokeAsync("IntegrationSync")).GetProperty("success").GetBoolean());
        AssertSyncRequest(Assert.Single(restarted.Requests), "after-1");
        await fixture.AssertStateAsync("Connected", "after-2", true, historyCount: 2);
        Assert.Equal(2, await fixture.AuditCountAsync("integration.synced"));
    }

    [Theory]
    [InlineData("")]
    [InlineData("terminal-empty-page")]
    public async Task EmptyTerminalPageNeverResetsAnExistingCheckpoint(string returnedCursor)
    {
        await using var fixture = await Fixture.CreateAsync();
        using (var first = fixture.Runtime(_ => Json(EmptyPage(returnedCursor))))
        {
            await fixture.SeedAsync(first.Registry);
            Assert.True(SyncData(await first.InvokeAsync("IntegrationSync")).GetProperty("success").GetBoolean());
            AssertSyncRequest(Assert.Single(first.Requests), OriginalCursor);
        }
        var durableCursor = returnedCursor.Length == 0 ? OriginalCursor : returnedCursor;
        await fixture.AssertStateAsync("Connected", durableCursor, true, historyCount: 0);
        using var restarted = fixture.Runtime(_ => Json(EmptyPage("poll-end")));
        Assert.True(SyncData(await restarted.InvokeAsync("IntegrationSync")).GetProperty("success").GetBoolean());
        AssertSyncRequest(Assert.Single(restarted.Requests), durableCursor);
        await fixture.AssertStateAsync("Connected", "poll-end", true, historyCount: 0);
    }

    [Fact]
    public async Task LaterMalformedPagePersistsCommittedCursorAndErrorThenRecoversAfterHandshake()
    {
        await using var fixture = await Fixture.CreateAsync();
        var calls = 0;
        var partial = fixture.Gps(1);
        partial.Remove("speedMilesPerHour");
        partial.Remove("headingDegrees");
        var malformed = fixture.Gps(3);
        malformed["headingDegrees"] = "malformed";
        using (var first = fixture.Runtime(_ => Json(++calls == 1
                   ? fixture.Page("committed-1", true, partial)
                   : fixture.Page("never-promote", false, fixture.Gps(2), malformed))))
        {
            await fixture.SeedAsync(first.Registry);
            var result = SyncData(await first.InvokeAsync("IntegrationSync"));
            Assert.False(result.GetProperty("success").GetBoolean());
            Assert.Equal(1, result.GetProperty("details").GetProperty("pagesCommitted").GetInt32());
            Assert.Equal("committed-1", result.GetProperty("details").GetProperty("nextCursor").GetString());
            Assert.Equal(2, first.Requests.Count);
            AssertSyncRequest(first.Requests[0], OriginalCursor);
            AssertSyncRequest(first.Requests[1], "committed-1");
        }
        await fixture.AssertStateAsync("Error", "committed-1", false, historyCount: 1);
        await fixture.AssertUnknownMeasurementsAsync(sequence: 1);
        Assert.Equal(1, await fixture.AuditCountAsync("integration.sync.failed"));

        using var restarted = fixture.Runtime(request => IsSync(request)
            ? Json(fixture.Page("recovered-2", false, fixture.Gps(2)))
            : HandshakeResponse(fixture, request));
        // Manual sync deliberately requires Connected. Error is not silently treated
        // as Connected, and a handshake must not erase the durable sync checkpoint.
        AssertStatus(await restarted.InvokeAsync("IntegrationSync"), StatusCodes.Status409Conflict);
        Assert.Empty(restarted.Requests);
        var handshake = SyncData(await restarted.InvokeAsync("IntegrationTestConnection"));
        Assert.True(handshake.GetProperty("success").GetBoolean(), handshake.GetProperty("message").GetString());
        await fixture.AssertStateAsync("Connected", "committed-1", false, historyCount: 1);
        await fixture.AssertUnknownMeasurementsAsync(sequence: 1);
        Assert.True(SyncData(await restarted.InvokeAsync("IntegrationSync")).GetProperty("success").GetBoolean());
        Assert.Equal(3, restarted.Requests.Count);
        AssertSyncRequest(restarted.Requests[2], "committed-1");
        await fixture.AssertStateAsync("Connected", "recovered-2", true, historyCount: 2);
    }

    [Fact]
    public async Task DefaultOffPartialGpsFailurePersistsNoProgressAndReleasesTheLease()
    {
        await using var fixture = await Fixture.CreateAsync();
        var partial = fixture.Gps(1);
        partial.Remove("speedMilesPerHour");
        using var runtime = fixture.Runtime(_ => Json(fixture.Page("never-promote", false, partial)), allowPartialGps: null);
        await fixture.SeedAsync(runtime.Registry);
        var result = SyncData(await runtime.InvokeAsync("IntegrationSync"));
        Assert.False(result.GetProperty("success").GetBoolean());
        Assert.Equal(0, result.GetProperty("details").GetProperty("pagesCommitted").GetInt32());
        Assert.Equal(JsonValueKind.Null, result.GetProperty("details").GetProperty("nextCursor").ValueKind);
        AssertSyncRequest(Assert.Single(runtime.Requests), OriginalCursor);
        await fixture.AssertStateAsync("Error", OriginalCursor, false, historyCount: 0);
        Assert.Equal(1, await fixture.AuditCountAsync("integration.sync.failed"));
    }

    [Fact]
    public async Task PaginationCycleRetainsPreRunCheckpointAndRestartReplaysWithoutDuplicates()
    {
        await using var fixture = await Fixture.CreateAsync();
        var calls = 0;
        using (var first = fixture.Runtime(_ => Json(++calls == 1
                   ? fixture.Page("cycle-1", true, fixture.Gps(1))
                   : fixture.Page(OriginalCursor, true, fixture.Gps(2)))))
        {
            await fixture.SeedAsync(first.Registry);
            var result = SyncData(await first.InvokeAsync("IntegrationSync"));
            Assert.False(result.GetProperty("success").GetBoolean());
            Assert.Contains("pagination", result.GetProperty("message").GetString(), StringComparison.OrdinalIgnoreCase);
            Assert.Equal(2, result.GetProperty("details").GetProperty("pagesCommitted").GetInt32());
            Assert.Equal(JsonValueKind.Null, result.GetProperty("details").GetProperty("nextCursor").ValueKind);
            Assert.Equal(2, first.Requests.Count);
            AssertSyncRequest(first.Requests[0], OriginalCursor);
            AssertSyncRequest(first.Requests[1], "cycle-1");
        }
        await fixture.AssertStateAsync("Error", OriginalCursor, false, historyCount: 2);
        using var restarted = fixture.Runtime(request => IsSync(request)
            ? Json(fixture.Page("cycle-recovered", false, fixture.Gps(1), fixture.Gps(2)))
            : HandshakeResponse(fixture, request));
        var handshake = SyncData(await restarted.InvokeAsync("IntegrationTestConnection"));
        Assert.True(handshake.GetProperty("success").GetBoolean(), handshake.GetProperty("message").GetString());
        var resumed = SyncData(await restarted.InvokeAsync("IntegrationSync"));
        Assert.True(resumed.GetProperty("success").GetBoolean());
        Assert.Equal(0, resumed.GetProperty("details").GetProperty("positionsWritten").GetInt32());
        AssertSyncRequest(restarted.Requests[^1], OriginalCursor);
        await fixture.AssertStateAsync("Connected", "cycle-recovered", true, historyCount: 2);
    }

    [Fact]
    public async Task DisconnectAfterRealConnectorReturnRejectsStaleFinalization()
    {
        await using var fixture = await Fixture.CreateAsync();
        var returned = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var runtime = fixture.Runtime(_ => Json(fixture.Page("must-not-resurrect", false, fixture.Gps(1))),
            afterResult: async (_, ct) =>
            {
                returned.TrySetResult();
                await release.Task.WaitAsync(TimeSpan.FromSeconds(15), ct);
            });
        await fixture.SeedAsync(runtime.Registry);
        var sync = runtime.InvokeAsync("IntegrationSync");
        try
        {
            await returned.Task.WaitAsync(TimeSpan.FromSeconds(15));
            Assert.Equal(1, await fixture.HistoryCountAsync());
            using var disconnectRuntime = fixture.Runtime(_ => throw new InvalidOperationException("Disconnect must not contact a provider."));
            AssertStatus(await disconnectRuntime.InvokeAsync("DisconnectIntegration"), StatusCodes.Status200OK);
            Assert.Empty(disconnectRuntime.Requests);
        }
        finally { release.TrySetResult(); }
        AssertStatus(await sync, StatusCodes.Status409Conflict);
        var state = await fixture.StateAsync();
        Assert.Equal("Disconnected", state["status"]);
        using var config = JsonDocument.Parse(state["configJson"]!.ToString()!);
        Assert.Empty(config.RootElement.EnumerateObject());
        Assert.Null(state["operationLeaseToken"]);
        Assert.Null(state["syncLastCompletedAt"]);
        Assert.Null(state["syncLastOk"]);
        Assert.Equal(1, Convert.ToInt64(state["operationGeneration"]));
        Assert.Equal(1, await fixture.HistoryCountAsync());
        Assert.Equal(0, await fixture.AuditCountAsync("integration.synced"));
        Assert.Equal(1, await fixture.AuditCountAsync("integration.disconnected"));
    }

    [Fact]
    public async Task InterruptedFinalizerRetainsOldCursorUntilLeaseExpiryThenReplaysIdempotently()
    {
        await using var fixture = await Fixture.CreateAsync();
        using var cancelled = new CancellationTokenSource();
        using (var first = fixture.Runtime(_ => Json(fixture.Page("unfinalized-1", false, fixture.Gps(1))),
                   afterResult: (_, _) => { cancelled.Cancel(); return Task.CompletedTask; }))
        {
            await fixture.SeedAsync(first.Registry);
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => first.InvokeAsync("IntegrationSync", cancelled.Token));
            AssertSyncRequest(Assert.Single(first.Requests), OriginalCursor);
        }
        var unfinalized = await fixture.StateAsync();
        Assert.Equal("Connected", unfinalized["status"]);
        Assert.Equal(OriginalCursor, unfinalized["syncCursor"]);
        Assert.NotNull(unfinalized["operationLeaseToken"]);
        Assert.NotNull(unfinalized["syncLastAttemptAt"]);
        Assert.Null(unfinalized["syncLastCompletedAt"]);
        Assert.Null(unfinalized["syncLastOk"]);
        Assert.Equal(1, await fixture.HistoryCountAsync());
        Assert.Equal(0, await fixture.AuditCountAsync("integration.synced"));

        using var restarted = fixture.Runtime(_ => Json(fixture.Page("replay-finalized-2", false, fixture.Gps(1), fixture.Gps(2))));
        AssertStatus(await restarted.InvokeAsync("IntegrationSync"), StatusCodes.Status409Conflict);
        Assert.Empty(restarted.Requests);
        // Advance only this owned fixture's lease clock instead of sleeping 90 s.
        await fixture.Db.ExecuteAsync(
            "UPDATE integrations SET operation_lease_expires_at=NOW()-INTERVAL '1 second' WHERE company_id=@cid AND id=@id",
            command => { command.Parameters.AddWithValue("@cid", fixture.CompanyId); command.Parameters.AddWithValue("@id", fixture.IntegrationId); });
        var resumed = SyncData(await restarted.InvokeAsync("IntegrationSync"));
        Assert.True(resumed.GetProperty("success").GetBoolean());
        Assert.Equal(1, resumed.GetProperty("details").GetProperty("positionsWritten").GetInt32());
        AssertSyncRequest(Assert.Single(restarted.Requests), OriginalCursor);
        await fixture.AssertStateAsync("Connected", "replay-finalized-2", true, historyCount: 2);
    }

    private static JsonElement SyncData(IResult result)
    {
        AssertStatus(result, StatusCodes.Status200OK);
        var envelope = Assert.IsType<ApiResponse<object>>(Assert.IsAssignableFrom<IValueHttpResult>(result).Value);
        Assert.True(envelope.Success);
        return JsonSerializer.SerializeToElement(envelope.Data);
    }

    private static void AssertStatus(IResult result, int status) =>
        Assert.Equal(status, Assert.IsAssignableFrom<IStatusCodeHttpResult>(result).StatusCode);

    private static bool IsSync(Uri uri) => QueryHelpers.ParseQuery(uri.Query).TryGetValue("types", out var types)
        && types == SyncTypes;

    private static void AssertSyncRequest(Uri uri, string cursor)
    {
        Assert.Equal("/fleet/vehicles/stats/feed", uri.AbsolutePath);
        var query = QueryHelpers.ParseQuery(uri.Query);
        Assert.Equal(SyncTypes, query["types"].ToString());
        Assert.Equal(cursor, query["after"].ToString());
        Assert.Equal(2, query.Count); // No silent profile, decoration or cursor reset.
    }

    private static HttpResponseMessage HandshakeResponse(Fixture fixture, Uri uri)
    {
        if (uri.AbsolutePath == "/fleet/vehicles")
            return Json(JsonSerializer.Serialize(new { data = new[] { new { id = fixture.ProviderVehicleId } } }));
        Assert.Equal("/fleet/vehicles/stats/feed", uri.AbsolutePath);
        var query = QueryHelpers.ParseQuery(uri.Query);
        Assert.Equal("gps", query["types"].ToString());
        Assert.Equal(fixture.ProviderVehicleId, query["vehicleIds"].ToString());
        Assert.False(query.ContainsKey("after"));
        return Json(EmptyPage("handshake-cursor-is-not-a-sync-checkpoint"));
    }

    private static string EmptyPage(string cursor) => JsonSerializer.Serialize(new
    {
        data = Array.Empty<object>(), pagination = new { endCursor = cursor, hasNextPage = false },
    });

    private static HttpResponseMessage Json(string body) => new(HttpStatusCode.OK)
    {
        Content = new StringContent(body, Encoding.UTF8, "application/json"),
    };

    private sealed class Fixture : IAsyncDisposable
    {
        private readonly string _connectionString;
        private readonly string _suffix = Guid.NewGuid().ToString("N");
        private readonly DateTimeOffset _eventStart = DateTimeOffset.UtcNow.AddMinutes(-10);
        private string? _encryptedToken;
        private const string RegionMetadata = "synthetic-us";
        private const string ProfileMetadata = """{"types":["gps","engineStates","obdOdometerMeters"],"revision":1,"notes":"preserve / + ?"}""";
        public Database Db { get; }
        public long CompanyId { get; private set; }
        public long IntegrationId { get; private set; }
        public long ActorId { get; private set; }
        private long VehicleId { get; set; }
        public string ProviderVehicleId => "durable-" + _suffix;

        private Fixture(string connectionString)
        {
            _connectionString = connectionString;
            Db = new Database(Configuration());
        }

        public static async Task<Fixture> CreateAsync()
        {
            var configured = Environment.GetEnvironmentVariable("OPSTRAX_TEST_DB");
            if (string.IsNullOrWhiteSpace(configured))
                throw new InvalidOperationException("Set OPSTRAX_TEST_DB explicitly to a disposable local application-schema database.");
            var connection = new NpgsqlConnectionStringBuilder(configured);
            if (connection.Host is not ("127.0.0.1" or "localhost" or "::1"))
                throw new InvalidOperationException("Durable cursor tests refuse remote PostgreSQL hosts.");
            var fixture = new Fixture(connection.ConnectionString);
            fixture.CompanyId = await fixture.Db.InsertAsync(
                "INSERT INTO companies(company_code,name,industry,entitlement_policy_mode) VALUES(@code,'Synthetic durable cursor fixture','Transportation','package_allowlist') RETURNING id",
                command => command.Parameters.AddWithValue("@code", "SDC-" + fixture._suffix[..12]));
            return fixture;
        }

        private IConfiguration Configuration(bool? allowPartialGps = true, int maxPages = 20)
        {
            var values = new Dictionary<string, string?>
            {
                ["ConnectionStrings:DefaultConnection"] = _connectionString,
                ["ConnectionStrings:SystemConnection"] = _connectionString,
                ["Rls:EnforceTenantContext"] = "false",
                ["Samsara:MaxPagesPerSync"] = maxPages.ToString(),
                ["Samsara:InterPageDelayMs"] = "0",
            };
            if (allowPartialGps is not null)
                values["Samsara:AllowPartialGpsMeasurements"] = allowPartialGps.Value.ToString();
            return new ConfigurationBuilder().AddInMemoryCollection(values).Build();
        }

        public Runtime Runtime(Func<Uri, HttpResponseMessage> response, bool? allowPartialGps = true,
            int maxPages = 20, Func<ConnectorResult, CancellationToken, Task>? afterResult = null) =>
            new(this, Configuration(allowPartialGps, maxPages), response, afterResult);

        public async Task SeedAsync(ConnectorRegistry registry)
        {
            await Db.ExecuteAsync(
                "INSERT INTO tenant_entitlements(company_id,module_key,enabled,source) VALUES(@cid,'fleet.integrations',true,'synthetic-test')",
                command => command.Parameters.AddWithValue("@cid", CompanyId));
            ActorId = await Db.InsertAsync(
                "INSERT INTO users(company_id,full_name,email,role_name,status,permissions_json) VALUES(@cid,'Synthetic cursor operator',@email,'Synthetic durable cursor role','Active','[\"integrations:manage\"]'::jsonb) RETURNING id",
                command => { command.Parameters.AddWithValue("@cid", CompanyId); command.Parameters.AddWithValue("@email", "durable-" + _suffix + "@example.invalid"); });
            using var config = JsonDocument.Parse(new JsonObject
            {
                ["apiToken"] = SyntheticToken,
                ["syncCursor"] = OriginalCursor,
                ["region"] = RegionMetadata,
                ["profileMetadata"] = JsonNode.Parse(ProfileMetadata),
            }.ToJsonString());
            var encrypted = registry.EncryptConfigForStorage(config.RootElement);
            using var encryptedDocument = JsonDocument.Parse(encrypted);
            _encryptedToken = encryptedDocument.RootElement.GetProperty("apiToken").GetString();
            Assert.StartsWith("enc:", _encryptedToken);
            Assert.DoesNotContain(SyntheticToken, encrypted);
            IntegrationId = await Db.InsertAsync(
                "INSERT INTO integrations(company_id,provider_name,category,status,integration_key,config_json) VALUES(@cid,'Samsara','Telematics & ELD','Connected','samsara',@config::jsonb) RETURNING id",
                command => { command.Parameters.AddWithValue("@cid", CompanyId); command.Parameters.AddWithValue("@config", encrypted); });
            var branchId = await Db.InsertAsync(
                "INSERT INTO branches(company_id,branch_code,name,status) VALUES(@cid,@code,'Durable cursor branch','Active') RETURNING id",
                command => { command.Parameters.AddWithValue("@cid", CompanyId); command.Parameters.AddWithValue("@code", "DCB-" + _suffix[..12]); });
            VehicleId = await Db.InsertAsync(
                "INSERT INTO vehicles(company_id,branch_id,vehicle_code,type,vin_exception_type,alternate_identifier) VALUES(@cid,@branch,@code,'truck','legacy-fleet-identifier',@code) RETURNING id",
                command => { command.Parameters.AddWithValue("@cid", CompanyId); command.Parameters.AddWithValue("@branch", branchId); command.Parameters.AddWithValue("@code", "DCV-" + _suffix[..12]); });
            var deviceId = await Db.InsertAsync(
                "INSERT INTO eld_devices(company_id,device_serial,provider,vehicle_id,status) VALUES(@cid,@serial,'Samsara',@vid,'Provisioning') RETURNING id",
                command => { command.Parameters.AddWithValue("@cid", CompanyId); command.Parameters.AddWithValue("@serial", "samsara-" + ProviderVehicleId); command.Parameters.AddWithValue("@vid", VehicleId); });
            await Db.ExecuteAsync(
                "INSERT INTO device_installations(company_id,branch_id,device_id,vehicle_id,status,device_role,is_primary,effective_from,installed_at,source) VALUES(@cid,@branch,@device,@vid,'Installed','GPS',true,@from,@from,'synthetic-durable-cursor')",
                command =>
                {
                    command.Parameters.AddWithValue("@cid", CompanyId);
                    command.Parameters.AddWithValue("@branch", branchId);
                    command.Parameters.AddWithValue("@device", deviceId);
                    command.Parameters.AddWithValue("@vid", VehicleId);
                    command.Parameters.AddWithValue("@from", _eventStart.AddMinutes(-1));
                });
        }

        public JsonObject Gps(int sequence) => new()
        {
            ["time"] = _eventStart.AddSeconds(sequence).ToString("O"),
            ["latitude"] = 34.05, ["longitude"] = -118.24,
            ["speedMilesPerHour"] = 20, ["headingDegrees"] = 90,
        };

        public string Page(string cursor, bool hasNext, params JsonObject[] gps) => new JsonObject
        {
            ["data"] = new JsonArray(new JsonObject { ["id"] = ProviderVehicleId, ["gps"] = new JsonArray(gps.Select(item => (JsonNode)item).ToArray()) }),
            ["pagination"] = new JsonObject { ["endCursor"] = cursor, ["hasNextPage"] = hasNext },
        }.ToJsonString();

        public async Task<Dictionary<string, object?>> StateAsync() => (await Db.QuerySingleAsync(
            "SELECT status,config_json,config_json->>'syncCursor' sync_cursor,operation_generation,operation_lease_token,operation_lease_expires_at,sync_last_attempt_at,sync_last_completed_at,sync_last_ok FROM integrations WHERE company_id=@cid AND id=@id",
            command => { command.Parameters.AddWithValue("@cid", CompanyId); command.Parameters.AddWithValue("@id", IntegrationId); }))!;

        public Task<long> HistoryCountAsync() => Db.ScalarLongAsync(
            "SELECT COUNT(*) FROM location_events WHERE company_id=@cid AND source_channel='samsara-api'",
            command => command.Parameters.AddWithValue("@cid", CompanyId));

        public Task<long> AuditCountAsync(string action) => Db.ScalarLongAsync(
            "SELECT COUNT(*) FROM audit_logs WHERE company_id=@cid AND entity_id=@id AND action_name=@action",
            command => { command.Parameters.AddWithValue("@cid", CompanyId); command.Parameters.AddWithValue("@id", IntegrationId); command.Parameters.AddWithValue("@action", action); });

        public async Task AssertStateAsync(string status, string cursor, bool ok, int historyCount)
        {
            var state = await StateAsync();
            Assert.Equal(status, state["status"]);
            Assert.Equal(cursor, state["syncCursor"]);
            Assert.Equal(ok, state["syncLastOk"]);
            Assert.NotNull(state["syncLastAttemptAt"]);
            Assert.NotNull(state["syncLastCompletedAt"]);
            Assert.Null(state["operationLeaseToken"]);
            Assert.Null(state["operationLeaseExpiresAt"]);
            using var config = JsonDocument.Parse(state["configJson"]!.ToString()!);
            // Cursor/status finalization must not re-encrypt credentials or discard
            // unrelated provider metadata while merging into the durable JSON object.
            Assert.Equal(_encryptedToken, config.RootElement.GetProperty("apiToken").GetString());
            Assert.Equal(RegionMetadata, config.RootElement.GetProperty("region").GetString());
            Assert.True(JsonNode.DeepEquals(JsonNode.Parse(ProfileMetadata),
                JsonNode.Parse(config.RootElement.GetProperty("profileMetadata").GetRawText())));
            Assert.Equal(historyCount, await HistoryCountAsync());
            Assert.Equal(historyCount, await Db.ScalarLongAsync(
                "SELECT COUNT(*) FROM location_events WHERE company_id=@cid AND vehicle_id=@vid AND installation_id IS NOT NULL",
                command => { command.Parameters.AddWithValue("@cid", CompanyId); command.Parameters.AddWithValue("@vid", VehicleId); }));
            var latest = await Db.QuerySingleAsync(
                "SELECT event_count FROM latest_vehicle_positions WHERE company_id=@cid AND vehicle_id=@vid",
                command => { command.Parameters.AddWithValue("@cid", CompanyId); command.Parameters.AddWithValue("@vid", VehicleId); });
            if (historyCount == 0) Assert.Null(latest);
            else Assert.Equal(historyCount, Convert.ToInt64(latest!["eventCount"]));
            Assert.Equal(0, await Db.ScalarLongAsync("SELECT COUNT(*) FROM telemetry_alerts WHERE company_id=@cid",
                command => command.Parameters.AddWithValue("@cid", CompanyId)));
        }

        public async Task AssertUnknownMeasurementsAsync(int sequence)
        {
            var expectedTime = _eventStart.AddSeconds(sequence).UtcDateTime;
            foreach (var (table, timeColumn) in new[]
                     {
                         ("location_events", "event_time"),
                         ("latest_vehicle_positions", "event_time"),
                         ("telemetry_live_asset_states", "last_event_time"),
                     })
            {
                var rows = await Db.QueryAsync(
                    $"SELECT speed_mph,heading,lat,lng,{timeColumn} event_time,received_at FROM {table} WHERE company_id=@cid AND vehicle_id=@vid",
                    command => { command.Parameters.AddWithValue("@cid", CompanyId); command.Parameters.AddWithValue("@vid", VehicleId); });
                var row = Assert.Single(rows);
                Assert.Null(row["speedMph"]);
                Assert.Null(row["heading"]);
                Assert.Equal(34.05m, Convert.ToDecimal(row["lat"]));
                Assert.Equal(-118.24m, Convert.ToDecimal(row["lng"]));
                Assert.Equal(expectedTime, Convert.ToDateTime(row["eventTime"]).ToUniversalTime(), TimeSpan.FromMilliseconds(1));
                Assert.NotNull(row["receivedAt"]);
            }
            var freshness = await Db.QuerySingleAsync(
                "SELECT provider_last_event_at FROM integrations WHERE company_id=@cid AND id=@id",
                command => { command.Parameters.AddWithValue("@cid", CompanyId); command.Parameters.AddWithValue("@id", IntegrationId); });
            Assert.Equal(expectedTime, Convert.ToDateTime(freshness!["providerLastEventAt"]).ToUniversalTime(), TimeSpan.FromMilliseconds(1));
        }

        public async ValueTask DisposeAsync()
        {
            // Delete only rows belonging to this test's randomly named, owned company.
            foreach (var table in new[] { "telemetry_live_asset_states", "telemetry_alerts", "latest_vehicle_positions",
                         "location_events", "device_installations", "eld_devices", "vehicles", "branches", "integrations",
                         "audit_logs", "tenant_entitlements", "users" })
                await Db.ExecuteAsync($"DELETE FROM {table} WHERE company_id=@cid", command => command.Parameters.AddWithValue("@cid", CompanyId));
            await Db.ExecuteAsync("DELETE FROM companies WHERE id=@cid", command => command.Parameters.AddWithValue("@cid", CompanyId));
        }
    }

    private sealed class Runtime : IDisposable
    {
        private readonly Fixture _fixture;
        private readonly Database _db;
        private readonly ServiceProvider _services;
        private readonly HttpMessageHandler _handler;
        public ConnectorRegistry Registry { get; }
        public List<Uri> Requests { get; } = [];

        public Runtime(Fixture fixture, IConfiguration configuration, Func<Uri, HttpResponseMessage> response,
            Func<ConnectorResult, CancellationToken, Task>? afterResult)
        {
            _fixture = fixture;
            _db = new Database(configuration, new TenantScopeAccessor());
            _services = new ServiceCollection()
                .AddScoped(_ => new Database(configuration, new TenantScopeAccessor()))
                .AddScoped<TelemetryLiveStateService>().BuildServiceProvider();
            _handler = new Handler(request =>
            {
                Assert.False(_db.HasAmbientTransaction);
                Assert.Equal("Bearer", request.Headers.Authorization?.Scheme);
                Assert.Equal(SyntheticToken, request.Headers.Authorization?.Parameter);
                Requests.Add(request.RequestUri!);
                return response(request.RequestUri!);
            });
            var factory = new Factory(_handler);
            IConnector connector = new SamsaraConnector(factory, _services.GetRequiredService<IServiceScopeFactory>(),
                configuration, NullLogger<SamsaraConnector>.Instance);
            if (afterResult is not null) connector = new AfterResultConnector(connector, afterResult);
            Registry = new ConnectorRegistry([connector], new GenericHttpConnector(factory, NullLogger<GenericHttpConnector>.Instance),
                new PiiProtectionService(new TestKeyProvider(), NullLogger<PiiProtectionService>.Instance), new TestEnvironment());
        }

        public Task<IResult> InvokeAsync(string name, CancellationToken ct = default)
        {
            var http = new DefaultHttpContext();
            http.Request.Method = HttpMethods.Post;
            http.RequestAborted = ct;
            http.Items[EndpointMappings.AuthCompanyIdItemKey] = _fixture.CompanyId;
            http.Items[EndpointMappings.AuthUserIdItemKey] = _fixture.ActorId;
            http.Items[EndpointMappings.AuthRoleItemKey] = "Synthetic durable cursor role";
            http.Items[EndpointMappings.AuthPermissionsItemKey] = new[] { "integrations:manage" };
            var method = typeof(EndpointMappings).GetMethod(name, BindingFlags.Static | BindingFlags.NonPublic)
                ?? throw new InvalidOperationException($"Endpoint {name} was not found.");
            return (Task<IResult>)method.Invoke(null,
                [http, _fixture.IntegrationId, _db, new AuditService(_db), Registry, ct])!;
        }

        public void Dispose() { _services.Dispose(); _handler.Dispose(); }
    }

    // The decorator changes only timing/cancellation AFTER the real connector's
    // return. It neither fabricates a successful result nor bypasses finalization.
    private sealed class AfterResultConnector(IConnector inner, Func<ConnectorResult, CancellationToken, Task> afterResult) : IConnector
    {
        public IReadOnlyCollection<string> Keys => inner.Keys;
        public string DisplayName => inner.DisplayName;
        public Task<ConnectorResult> TestConnectionAsync(IReadOnlyDictionary<string, string?> config, CancellationToken ct) =>
            inner.TestConnectionAsync(config, ct);
        public async Task<ConnectorResult> RunActionAsync(string action, IReadOnlyDictionary<string, string?> config, JsonElement? body, CancellationToken ct)
        {
            var result = await inner.RunActionAsync(action, config, body, ct);
            Assert.True(result.Success, result.Message);
            await afterResult(result, ct);
            return result;
        }
    }

    private sealed class Factory(HttpMessageHandler handler) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new(handler, disposeHandler: false);
    }

    private sealed class Handler(Func<HttpRequestMessage, HttpResponseMessage> response) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            return Task.FromResult(response(request));
        }
    }

    private sealed class TestEnvironment : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = Environments.Staging;
        public string ApplicationName { get; set; } = "samsara-durable-cursor-tests";
        public string ContentRootPath { get; set; } = Directory.GetCurrentDirectory();
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }
}
