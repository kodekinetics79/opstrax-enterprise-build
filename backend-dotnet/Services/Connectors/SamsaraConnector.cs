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
// Verify: GET /fleet/vehicles plus a bounded GET of the vehicle stats feed.
//         Both required read scopes must succeed before the connector is Connected.
// Sync:   GET /fleet/vehicles/stats/feed?types=gps,engineStates,obdOdometerMeters
//         (cursor-paginated; endCursor persisted per-connector so each sync is
//          incremental). Each vehicle is resolved through a globally unique
//          eld_devices identity and its effective-dated installation history;
//          only an active installation may write vehicle telemetry.
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
            using var client = Client(token!);
            using var handshakeCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            handshakeCts.CancelAfter(TimeSpan.FromSeconds(25));
            int sampleVehicleCount;
            string? sampleVehicleId;
            using (var vehiclesCts = CancellationTokenSource.CreateLinkedTokenSource(handshakeCts.Token))
            {
                vehiclesCts.CancelAfter(SamsaraResponseReader.RequestTimeout);
                using var vehiclesResponse = await client.GetAsync("/fleet/vehicles?limit=1", HttpCompletionOption.ResponseHeadersRead, vehiclesCts.Token);
                if ((int)vehiclesResponse.StatusCode is 401 or 403)
                    return ConnectorResult.Fail("Samsara rejected the token or its 'Read Vehicles' scope.");
                if (!vehiclesResponse.IsSuccessStatusCode)
                    return ConnectorResult.Fail($"Samsara vehicle access returned HTTP {(int)vehiclesResponse.StatusCode}.");

                try
                {
                    using var vehiclesDocument = await SamsaraResponseReader.ReadJsonAsync(vehiclesResponse.Content, vehiclesCts.Token);
                    if (!vehiclesDocument.RootElement.TryGetProperty("data", out var data) || data.ValueKind != JsonValueKind.Array)
                        return ConnectorResult.Fail("Samsara vehicle access returned an invalid response envelope; connection was not accepted.");

                    sampleVehicleCount = data.GetArrayLength();
                    sampleVehicleId = sampleVehicleCount > 0
                        && data[0].ValueKind == JsonValueKind.Object
                        && data[0].TryGetProperty("id", out var id)
                        && id.ValueKind == JsonValueKind.String
                            ? id.GetString()
                            : null;
                    if (sampleVehicleCount > 0 && string.IsNullOrWhiteSpace(sampleVehicleId))
                        return ConnectorResult.Fail("Samsara vehicle access returned a vehicle without its required identifier; connection was not accepted.");
                }
                catch (JsonException)
                {
                    return ConnectorResult.Fail("Samsara vehicle access returned malformed JSON; connection was not accepted.");
                }
            }

            var statisticsUrl = "/fleet/vehicles/stats/feed?types=gps";
            if (!string.IsNullOrWhiteSpace(sampleVehicleId))
                statisticsUrl += $"&vehicleIds={Uri.EscapeDataString(sampleVehicleId)}";

            using var statisticsCts = CancellationTokenSource.CreateLinkedTokenSource(handshakeCts.Token);
            statisticsCts.CancelAfter(SamsaraResponseReader.RequestTimeout);
            using var statisticsResponse = await client.GetAsync(statisticsUrl, HttpCompletionOption.ResponseHeadersRead, statisticsCts.Token);
            if ((int)statisticsResponse.StatusCode is 401 or 403)
                return ConnectorResult.Fail("Samsara rejected the token or its 'Read Vehicle Statistics' scope; connection was not accepted.");
            if (!statisticsResponse.IsSuccessStatusCode)
                return ConnectorResult.Fail($"Samsara vehicle-statistics access returned HTTP {(int)statisticsResponse.StatusCode}.");

            try
            {
                using var statisticsDocument = await SamsaraResponseReader.ReadJsonAsync(statisticsResponse.Content, statisticsCts.Token);
                if (!statisticsDocument.RootElement.TryGetProperty("data", out var statisticsData)
                    || statisticsData.ValueKind != JsonValueKind.Array)
                    return ConnectorResult.Fail("Samsara vehicle-statistics access returned an invalid response envelope: the required data array is missing.");
                _ = SamsaraSync.ReadPagination(statisticsDocument.RootElement);
            }
            catch (JsonException)
            {
                return ConnectorResult.Fail("Samsara vehicle-statistics access returned malformed JSON; connection was not accepted.");
            }
            catch (InvalidDataException ex)
            {
                return ConnectorResult.Fail($"Samsara vehicle-statistics access returned an invalid response envelope: {ex.Message}");
            }

            return ConnectorResult.Ok(
                "Connected to Samsara — token and both required read scopes were verified.",
                new Dictionary<string, object?>
                {
                    ["sampleVehicleCount"] = sampleVehicleCount,
                    ["readVehiclesVerified"] = true,
                    ["readVehicleStatisticsVerified"] = true,
                });
        }
        catch (SamsaraResponseReader.ResponseTooLargeException) { return ConnectorResult.Fail("Samsara response exceeded the allowed size; connection was not accepted."); }
        catch (OperationCanceledException) { return ConnectorResult.Fail("Samsara did not respond in time (timeout)."); }
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
        long integrationId = body is { } bi && bi.TryGetProperty("integrationId", out var iid) && iid.TryGetInt64(out var i) ? i : 0;
        long operationGeneration = body is { } bg && bg.TryGetProperty("operationGeneration", out var gen) && gen.TryGetInt64(out var g) ? g : -1;
        var operationLeaseTokenRaw = body is { } bl && bl.TryGetProperty("operationLeaseToken", out var lease) ? lease.GetString() : null;
        if (integrationId <= 0 || operationGeneration < 0 || !Guid.TryParse(operationLeaseTokenRaw, out var operationLeaseToken))
            return ConnectorResult.Fail("Sync requires a valid generation-bound connector operation lease.");
        var operation = new ConnectorOperationContext(
            companyId, integrationId, operationGeneration, operationLeaseToken, "samsara", null, "Connected",
            IsSyncOperation: true);
        var afterCursor = body is { } b2 && b2.TryGetProperty("cursor", out var cur) ? cur.GetString() : null;
        var cursor = afterCursor;
        var positionsWritten = 0;
        var vehiclesSeen = 0;
        var unmatched = 0;
        var historicalOnly = 0;
        var rejected = 0;
        var hasNextPage = false;
        var pagesCommitted = 0;
        var paginationIntegrityFailure = false;
        var requestedDurationSeconds = body is { } bmd && bmd.TryGetProperty("maxDurationSeconds", out var mds) && mds.TryGetInt32(out var requestedSeconds)
            ? requestedSeconds
            : configuration.GetValue("Samsara:MaxDurationSeconds", 60);
        var maxDurationSeconds = Math.Clamp(requestedDurationSeconds, 10, 90);
        using var boundedCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        boundedCts.CancelAfter(TimeSpan.FromSeconds(maxDurationSeconds));

        ConnectorResult FailureWithCommittedProgress(string message, bool persistCursor = true) => ConnectorResult.Fail(
            message,
            new Dictionary<string, object?>
            {
                ["positionsWritten"] = positionsWritten,
                ["vehiclesSeen"] = vehiclesSeen,
                ["unmatched"] = unmatched,
                ["historicalOnly"] = historicalOnly,
                ["rejected"] = rejected,
                ["nextCursor"] = persistCursor && pagesCommitted > 0 ? cursor : null,
                ["hasNextPage"] = hasNextPage,
                ["boundedPartial"] = pagesCommitted > 0,
                ["pagesCommitted"] = pagesCommitted,
            });

        try
        {
            using var client = Client(token!);
            var sync = new SamsaraSync(client, scopeFactory, logger);
            var seenCursors = new HashSet<string>(StringComparer.Ordinal);
            if (!string.IsNullOrWhiteSpace(cursor)) seenCursors.Add(cursor);
            var completed = false;
            var configuredMaxPages = Math.Clamp(configuration.GetValue("Samsara:MaxPagesPerSync", 200), 1, 200);
            var requestedMaxPages = body is { } bmp && bmp.TryGetProperty("maxPages", out var mp) && mp.TryGetInt32(out var requestedPages)
                ? requestedPages
                : configuredMaxPages;
            var maxPages = Math.Clamp(requestedMaxPages, 1, configuredMaxPages);
            var interPageDelayMs = Math.Clamp(configuration.GetValue("Samsara:InterPageDelayMs", 100), 0, 1_000);
            var runCt = boundedCts.Token;
            // Drain the cursor backlog in the same run instead of waiting five minutes
            // between pages. The cap prevents a permanently advancing feed from owning a
            // worker forever; the returned cursor resumes exactly where this run stopped.
            for (var page = 0; page < maxPages; page++)
            {
                var pageSummary = await sync.RunAsync(operation, cursor, runCt);
                positionsWritten += pageSummary.PositionsWritten;
                vehiclesSeen += pageSummary.VehiclesSeen;
                unmatched += pageSummary.Unmatched;
                historicalOnly += pageSummary.HistoricalOnly;
                rejected += pageSummary.Rejected;
                hasNextPage = pageSummary.HasNextPage;
                pagesCommitted++;
                if (!hasNextPage)
                {
                    if (!string.IsNullOrWhiteSpace(pageSummary.NextCursor)) cursor = pageSummary.NextCursor;
                    completed = true;
                    break;
                }
                var candidateCursor = pageSummary.NextCursor;
                if (string.IsNullOrWhiteSpace(candidateCursor) || !seenCursors.Add(candidateCursor))
                {
                    paginationIntegrityFailure = true;
                    throw new InvalidOperationException("Samsara pagination did not advance its cursor.");
                }
                // Promote only after the cycle/repeat guard. Pagination-integrity
                // failures deliberately retain the pre-run durable cursor.
                cursor = candidateCursor;
                if (interPageDelayMs > 0)
                    await Task.Delay(TimeSpan.FromMilliseconds(interPageDelayMs), runCt);
            }
            var boundedPartial = !completed && hasNextPage;
            return ConnectorResult.Ok(
                $"Synced {positionsWritten} vehicle position(s) from Samsara" +
                $"{(unmatched > 0 ? $"; {unmatched} Samsara vehicle(s) had no matching OpsTrax vehicle (map a device to link them)." : ".")}" +
                $"{(historicalOnly > 0 ? $" Retained {historicalOnly} historical fix(es) against ended installations without changing live state." : "")}" +
                $"{(rejected > 0 ? $" Rejected {rejected} invalid provider fix(es); no telemetry was fabricated." : "")}" +
                $"{(boundedPartial ? $" Reached the bounded {maxPages}-page run limit; the returned cursor will resume the remaining backlog." : "")}",
                new Dictionary<string, object?>
                {
                    ["positionsWritten"] = positionsWritten,
                    ["vehiclesSeen"] = vehiclesSeen,
                    ["unmatched"] = unmatched,
                    ["historicalOnly"] = historicalOnly,
                    ["rejected"] = rejected,
                    ["nextCursor"] = cursor,
                    ["hasNextPage"] = hasNextPage,
                    ["boundedPartial"] = boundedPartial,
                });
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested && boundedCts.IsCancellationRequested)
        {
            return FailureWithCommittedProgress(
                "Samsara sync reached its bounded run duration. Complete page transactions were retained and their latest cursor will resume the remaining backlog; no partial page is claimed.");
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (OperationCanceledException)
        {
            return FailureWithCommittedProgress(
                "A Samsara provider request timed out. Complete page transactions were retained and their latest cursor will resume on a later run; no in-run transport retry or partial page is claimed.");
        }
        catch (SamsaraResponseReader.ResponseTooLargeException)
        {
            return FailureWithCommittedProgress(
                "Samsara response exceeded the allowed size. Complete page transactions were retained; their latest cursor will resume on a later run. The oversized page was not written, skipped or retried.");
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Samsara sync failed for company {Company}", companyId);
            return FailureWithCommittedProgress(
                $"Samsara sync failed: {ex.Message}. Complete page transactions were retained; " +
                (paginationIntegrityFailure
                    ? "the pre-run durable cursor was preserved because pagination integrity failed."
                    : "their latest cursor will resume on a later run; no partial page is claimed."),
                persistCursor: !paginationIntegrityFailure);
        }
    }
}
