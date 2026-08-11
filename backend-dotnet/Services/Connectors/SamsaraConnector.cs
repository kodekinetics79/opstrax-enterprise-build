using System.Net.Http.Headers;
using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Opstrax.Api.Data;

namespace Opstrax.Api.Services.Connectors;

// ─────────────────────────────────────────────────────────────────────────────
// Samsara — the first DEEP integration: not just an auth handshake, but a real
// data pipeline that pulls live vehicle GPS/telemetry from Samsara's fleet API and
// writes it into the SAME tables OpsTrax's live map + GPS pages already read
// (latest_vehicle_positions + location_events), so Samsara vehicles appear live.
//
// Auth:   Bearer <apiToken>  (config key "apiToken" / "apiKey", SENSITIVE)
// Verify: GET /fleet/vehicles  (200 = token valid + has fleet read scope)
// Sync:   GET /fleet/vehicles/stats/feed?types=gps,engineStates,obdOdometerMeters
//         (cursor-paginated; endCursor persisted per-connector so each sync is
//          incremental). Each vehicle is matched to an OpsTrax vehicle via an
//          eld_devices row keyed by the Samsara vehicle id (device_serial), then
//          its GPS UPSERTs latest_vehicle_positions and appends location_events.
//
// The connector is a singleton, so it resolves scoped services (Database,
// TelemetryLiveStateService) per call via IServiceScopeFactory, and wraps cross-
// tenant writes in Database.RunInSystemTransactionAsync so replay/history/latest/alerts
// succeed under RLS and commit atomically.
// ─────────────────────────────────────────────────────────────────────────────
public sealed class SamsaraConnector(
    IHttpClientFactory httpFactory,
    IServiceScopeFactory scopeFactory,
    IConfiguration configuration,
    ILogger<SamsaraConnector> logger) : IConnector
{
    public IReadOnlyCollection<string> Keys { get; } = new[] { "samsara" };
    public string DisplayName => "Samsara";

    private const string BaseUrl = "https://api.samsara.com";

    private HttpClient Client(string token)
    {
        var c = httpFactory.CreateClient("samsara");
        c.BaseAddress ??= new Uri(BaseUrl);
        c.Timeout = TimeSpan.FromSeconds(20);
        c.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return c;
    }

    private static string? Token(IReadOnlyDictionary<string, string?> config)
        => config.GetValueOrDefault("apiToken") ?? config.GetValueOrDefault("apiKey") ?? config.GetValueOrDefault("token");

    // ── Real auth handshake ────────────────────────────────────────────────────────
    public async Task<ConnectorResult> TestConnectionAsync(IReadOnlyDictionary<string, string?> config, CancellationToken ct)
    {
        var token = Token(config);
        if (string.IsNullOrWhiteSpace(token))
            return ConnectorResult.Fail("Add a Samsara API token (apiToken) in Configure, then test again. Create one in Samsara → Settings → API Tokens with 'Read Vehicles' + 'Read Vehicle Statistics'.");
        try
        {
            var client = Client(token!);
            using var resp = await client.GetAsync("/fleet/vehicles?limit=1", ct);
            if (resp.IsSuccessStatusCode)
            {
                int count = 0;
                try
                {
                    using var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync(ct));
                    if (doc.RootElement.TryGetProperty("data", out var data) && data.ValueKind == JsonValueKind.Array)
                        count = data.GetArrayLength();
                }
                catch { /* body optional */ }
                return ConnectorResult.Ok("Connected to Samsara — token is valid and has fleet read access.",
                    new Dictionary<string, object?> { ["sampleVehicleCount"] = count });
            }
            if ((int)resp.StatusCode is 401 or 403)
                return ConnectorResult.Fail("Samsara rejected the token (auth). Check the token and that it has 'Read Vehicles' + 'Read Vehicle Statistics' scopes.");
            return ConnectorResult.Fail($"Samsara returned {(int)resp.StatusCode} {resp.ReasonPhrase}.");
        }
        catch (TaskCanceledException) { return ConnectorResult.Fail("Samsara did not respond in time (timeout)."); }
        catch (Exception ex) { logger.LogWarning(ex, "Samsara test failed"); return ConnectorResult.Fail($"Could not reach Samsara: {ex.Message}"); }
    }

    // ── Live actions: sync ─────────────────────────────────────────────────────────
    public async Task<ConnectorResult> RunActionAsync(string action, IReadOnlyDictionary<string, string?> config, JsonElement? body, CancellationToken ct)
    {
        if (!string.Equals(action, "sync", StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(action, "sync-telemetry", StringComparison.OrdinalIgnoreCase))
            return ConnectorResult.Fail($"Action '{action}' is not supported by Samsara. Use 'sync'.");

        var token = Token(config);
        if (string.IsNullOrWhiteSpace(token))
            return ConnectorResult.Fail("Missing Samsara API token.");

        // The connector doesn't know its own company/cursor — the endpoint passes them
        // in the action body so the sync is tenant-scoped and incremental.
        long companyId = body is { } b && b.TryGetProperty("companyId", out var cid) && cid.TryGetInt64(out var c) ? c : 0;
        if (companyId <= 0) return ConnectorResult.Fail("Sync requires a tenant context (companyId).");
        var afterCursor = body is { } b2 && b2.TryGetProperty("cursor", out var cur) ? cur.GetString() : null;

        try
        {
            var sync = new SamsaraSync(Client(token!), scopeFactory, logger);
            var cursor = afterCursor;
            var positionsWritten = 0;
            var vehiclesSeen = 0;
            var unmatched = 0;
            var hasNextPage = false;
            var seenCursors = new HashSet<string>(StringComparer.Ordinal);
            if (!string.IsNullOrWhiteSpace(cursor)) seenCursors.Add(cursor);
            var completed = false;
            var maxPages = Math.Clamp(configuration.GetValue("Samsara:MaxPagesPerSync", 200), 1, 200);
            var interPageDelayMs = Math.Clamp(configuration.GetValue("Samsara:InterPageDelayMs", 100), 0, 1_000);
            // Drain the cursor backlog in the same run instead of waiting five minutes
            // between pages. The cap prevents a permanently advancing feed from owning a
            // worker forever; the returned cursor resumes exactly where this run stopped.
            for (var page = 0; page < maxPages; page++)
            {
                var pageSummary = await sync.RunAsync(companyId, cursor, ct);
                positionsWritten += pageSummary.PositionsWritten;
                vehiclesSeen += pageSummary.VehiclesSeen;
                unmatched += pageSummary.Unmatched;
                hasNextPage = pageSummary.HasNextPage;
                if (!string.IsNullOrWhiteSpace(pageSummary.NextCursor)) cursor = pageSummary.NextCursor;
                if (!hasNextPage)
                {
                    completed = true;
                    break;
                }
                if (string.IsNullOrWhiteSpace(cursor) || !seenCursors.Add(cursor))
                    throw new InvalidOperationException("Samsara pagination did not advance its cursor.");
                if (interPageDelayMs > 0)
                    await Task.Delay(TimeSpan.FromMilliseconds(interPageDelayMs), ct);
            }
            var boundedPartial = !completed && hasNextPage;
            return ConnectorResult.Ok(
                $"Synced {positionsWritten} vehicle position(s) from Samsara" +
                $"{(unmatched > 0 ? $"; {unmatched} Samsara vehicle(s) had no matching OpsTrax vehicle (map a device to link them)." : ".")}" +
                $"{(boundedPartial ? $" Reached the bounded {maxPages}-page run limit; the returned cursor will resume the remaining backlog." : "")}",
                new Dictionary<string, object?>
                {
                    ["positionsWritten"] = positionsWritten,
                    ["vehiclesSeen"] = vehiclesSeen,
                    ["unmatched"] = unmatched,
                    ["nextCursor"] = cursor,
                    ["hasNextPage"] = hasNextPage,
                    ["boundedPartial"] = boundedPartial,
                });
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Samsara sync failed for company {Company}", companyId);
            return ConnectorResult.Fail($"Samsara sync failed: {ex.Message}");
        }
    }
}
