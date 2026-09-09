using System.Net.Http.Headers;
using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Opstrax.Api.Data;
using Opstrax.Api.Services;

namespace Opstrax.Api.Services.Connectors;

// ─────────────────────────────────────────────────────────────────────────────
// Samsara — the first DEEP integration: not just an auth handshake, but a real
// data pipeline that pulls live vehicle GPS/telemetry from Samsara's fleet API and
// writes it into the SAME tables OpsTrax's live map + GPS pages already read
// (latest_vehicle_positions + location_events), so Samsara vehicles appear live.
//
// Auth:   Bearer <apiToken>  (config key "apiToken" / "apiKey", SENSITIVE)
// Verify: GET /me, GET /fleet/vehicles and a bounded GET of the vehicle stats feed.
//         All required read scopes must succeed before the connector is Connected.
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

    private HttpClient Client(string token, Uri apiBaseUri)
    {
        var c = httpFactory.CreateClient("samsara");
        // A named client can carry a default base address. The tenant-selected,
        // strictly allowlisted Samsara cloud must win for every new operation.
        c.BaseAddress = apiBaseUri;
        c.Timeout = TimeSpan.FromSeconds(20);
        c.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return c;
    }

    private static string? Token(IReadOnlyDictionary<string, string?> config)
        => config.GetValueOrDefault("apiToken") ?? config.GetValueOrDefault("apiKey") ?? config.GetValueOrDefault("token");

    internal static bool TryResolveApiRegion(
        IReadOnlyDictionary<string, string?> config,
        out string region,
        out Uri apiBaseUri)
    {
        region = string.IsNullOrWhiteSpace(config.GetValueOrDefault("apiRegion"))
            ? "us"
            : config.GetValueOrDefault("apiRegion")!.Trim().ToLowerInvariant();
        var baseUrl = region switch
        {
            "us" => "https://api.samsara.com",
            "eu" => "https://api.eu.samsara.com",
            "ca" => "https://api.ca.samsara.com",
            _ => null,
        };
        if (baseUrl is null)
        {
            apiBaseUri = null!;
            return false;
        }
        apiBaseUri = new Uri(baseUrl, UriKind.Absolute);
        return true;
    }

    // ── Real auth handshake ────────────────────────────────────────────────────────
    public async Task<ConnectorResult> TestConnectionAsync(IReadOnlyDictionary<string, string?> config, CancellationToken ct)
    {
        var token = Token(config);
        if (string.IsNullOrWhiteSpace(token))
            return ConnectorResult.Fail("Add a Samsara API token (apiToken) in Configure, then test again. Create one in Samsara → Settings → API Tokens with 'Read Org Information' + 'Read Vehicles' + 'Read Vehicle Statistics'.");
        if (!TryResolveApiRegion(config, out var apiRegion, out var apiBaseUri))
            return ConnectorResult.Fail("Select the Samsara cloud region that matches the provider dashboard: United States, Europe / United Kingdom, or Canada.");
        try
        {
            using var client = Client(token!, apiBaseUri);
            using var handshakeCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            handshakeCts.CancelAfter(TimeSpan.FromSeconds(25));
            string organizationReference;
            using (var organizationCts = CancellationTokenSource.CreateLinkedTokenSource(handshakeCts.Token))
            {
                organizationCts.CancelAfter(SamsaraResponseReader.RequestTimeout);
                using var organizationResponse = await client.GetAsync("/me", HttpCompletionOption.ResponseHeadersRead, organizationCts.Token);
                if ((int)organizationResponse.StatusCode is 401 or 403)
                    return ConnectorResult.Fail("Samsara rejected the token or its 'Read Org Information' scope; connection was not accepted.");
                if (!organizationResponse.IsSuccessStatusCode)
                    return ConnectorResult.Fail($"Samsara organization access returned HTTP {(int)organizationResponse.StatusCode}.");
                try
                {
                    using var organizationDocument = await SamsaraResponseReader.ReadJsonAsync(organizationResponse.Content, organizationCts.Token);
                    organizationReference = ReadOrganizationReference(organizationDocument.RootElement);
                }
                catch (JsonException)
                {
                    return ConnectorResult.Fail("Samsara organization access returned malformed JSON; connection was not accepted.");
                }
                catch (InvalidDataException ex)
                {
                    return ConnectorResult.Fail($"Samsara organization access returned an invalid response envelope: {ex.Message}");
                }
            }
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
                $"Connected to Samsara {apiRegion.ToUpperInvariant()} cloud — token, provider organization and all three required read scopes were verified.",
                new Dictionary<string, object?>
                {
                    ["sampleVehicleCount"] = sampleVehicleCount,
                    ["apiRegion"] = apiRegion,
                    ["readOrgInformationVerified"] = true,
                    ["readVehiclesVerified"] = true,
                    ["readVehicleStatisticsVerified"] = true,
                },
                providerAccountReference: organizationReference);
        }
        catch (SamsaraResponseReader.ResponseTooLargeException) { return ConnectorResult.Fail("Samsara response exceeded the allowed size; connection was not accepted."); }
        catch (OperationCanceledException) { return ConnectorResult.Fail("Samsara did not respond in time (timeout)."); }
        catch (Exception ex) { logger.LogWarning(ex, "Samsara test failed"); return ConnectorResult.Fail($"Could not reach Samsara: {ex.Message}"); }
    }

    // ── Live actions: sync ─────────────────────────────────────────────────────────
    public async Task<ConnectorResult> RunActionAsync(string action, IReadOnlyDictionary<string, string?> config, JsonElement? body, CancellationToken ct)
    {
        var telemetrySync = string.Equals(action, "sync", StringComparison.OrdinalIgnoreCase)
            || string.Equals(action, "sync-telemetry", StringComparison.OrdinalIgnoreCase);
        var cameraSafetySync = string.Equals(action, "sync-camera-safety", StringComparison.OrdinalIgnoreCase);
        if (!telemetrySync && !cameraSafetySync)
            return ConnectorResult.Fail(
                $"Action '{action}' is not supported by Samsara. Use 'sync' or 'sync-camera-safety'.");

        var token = Token(config);
        if (string.IsNullOrWhiteSpace(token))
            return ConnectorResult.Fail("Missing Samsara API token.");
        if (!TryResolveApiRegion(config, out _, out var apiBaseUri))
            return ConnectorResult.Fail("Sync requires a supported Samsara cloud region (us, eu, or ca).");

        // The connector doesn't know its own company/cursor — the endpoint passes them
        // in the action body so the sync is tenant-scoped and incremental.
        long companyId = body is { } b && b.TryGetProperty("companyId", out var cid) && cid.TryGetInt64(out var c) ? c : 0;
        if (companyId <= 0) return ConnectorResult.Fail("Sync requires a tenant context (companyId).");
        long integrationId = body is { } bi && bi.TryGetProperty("integrationId", out var iid) && iid.TryGetInt64(out var i) ? i : 0;
        long operationGeneration = body is { } bg && bg.TryGetProperty("operationGeneration", out var gen) && gen.TryGetInt64(out var g) ? g : -1;
        var operationLeaseTokenRaw = body is { } bl && bl.TryGetProperty("operationLeaseToken", out var lease) ? lease.GetString() : null;
        var providerAccountReference = body is { } ba && ba.TryGetProperty("providerAccountReference", out var account)
            ? account.GetString()
            : null;
        if (integrationId <= 0 || operationGeneration < 0 || !Guid.TryParse(operationLeaseTokenRaw, out var operationLeaseToken))
            return ConnectorResult.Fail("Sync requires a valid generation-bound connector operation lease.");
        if (!IsOrganizationReference(providerAccountReference))
            return ConnectorResult.Fail(
                "Sync requires a verified Samsara organization identity. Test the provider connection again with the 'Read Org Information' scope.");
        var operation = new ConnectorOperationContext(
            companyId, integrationId, operationGeneration, operationLeaseToken, "samsara", null, "Connected",
            IsSyncOperation: telemetrySync,
            ProviderAccountReference: providerAccountReference);
        if (cameraSafetySync)
            return await RunCameraSafetyActionAsync(token!, apiBaseUri, operation, body, ct);

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
            using var client = Client(token!, apiBaseUri);
            var sync = new SamsaraSync(client, scopeFactory, logger,
                configuration.GetValue<bool>("Samsara:AllowPartialGpsMeasurements"));
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
                $"{(unmatched > 0 ? $"; {unmatched} Samsara GPS fix(es) had no effective OpsTrax vehicle mapping (map a device to link them)." : ".")}" +
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

    internal static string ReadOrganizationReference(JsonElement root)
    {
        if (root.ValueKind != JsonValueKind.Object
            || !root.TryGetProperty("data", out var data)
            || data.ValueKind != JsonValueKind.Object
            || !data.TryGetProperty("id", out var id)
            || id.ValueKind != JsonValueKind.String)
            throw new InvalidDataException("the required data.id is missing.");
        var value = id.GetString()?.Trim();
        if (string.IsNullOrWhiteSpace(value) || value.Length > 140
            || value.Any(character => char.IsControl(character) || char.IsWhiteSpace(character)))
            throw new InvalidDataException("the organization identifier is invalid.");
        return $"samsara-org:{value}";
    }

    internal static bool IsOrganizationReference(string? value)
    {
        if (value is not { Length: > 12 and <= 160 }
            || !value.StartsWith("samsara-org:", StringComparison.Ordinal))
            return false;
        return value["samsara-org:".Length..]
            .All(character => !char.IsControl(character) && !char.IsWhiteSpace(character));
    }

    private async Task<ConnectorResult> RunCameraSafetyActionAsync(
        string token,
        Uri apiBaseUri,
        ConnectorOperationContext operation,
        JsonElement? body,
        CancellationToken ct)
    {
        var startTimeRaw = body is { } bs && bs.TryGetProperty("startTime", out var start)
            ? start.GetString()
            : null;
        if (!DateTimeOffset.TryParseExact(
                startTimeRaw,
                ["yyyy-MM-dd'T'HH:mm:ssK", "yyyy-MM-dd'T'HH:mm:ss.FFFFFFFK"],
                System.Globalization.CultureInfo.InvariantCulture,
                System.Globalization.DateTimeStyles.None,
                out var startTime)
            || startTime.Offset != TimeSpan.Zero)
            return ConnectorResult.Fail(
                "Samsara camera safety sync requires a stable RFC 3339 UTC startTime.");

        var cursor = body is { } b2 && b2.TryGetProperty("cursor", out var cur) ? cur.GetString() : null;
        var eventsObserved = 0;
        var eventsAccepted = 0;
        var eventsReplayed = 0;
        var eventsQuarantined = 0;
        var pagesCommitted = 0;
        var hasNextPage = false;
        var paginationIntegrityFailure = false;
        var requestedDurationSeconds = body is { } bmd
            && bmd.TryGetProperty("maxDurationSeconds", out var mds)
            && mds.TryGetInt32(out var requestedSeconds)
                ? requestedSeconds
                : configuration.GetValue("Samsara:CameraSafetyMaxDurationSeconds", 60);
        var maxDurationSeconds = Math.Clamp(requestedDurationSeconds, 10, 90);
        using var boundedCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        boundedCts.CancelAfter(TimeSpan.FromSeconds(maxDurationSeconds));

        ConnectorResult Failure(string message, bool persistCursor = true) => ConnectorResult.Fail(
            message,
            new Dictionary<string, object?>
            {
                ["lane"] = "camera-safety",
                ["eventsObserved"] = eventsObserved,
                ["eventsAccepted"] = eventsAccepted,
                ["eventsReplayed"] = eventsReplayed,
                ["eventsQuarantined"] = eventsQuarantined,
                ["nextCursor"] = persistCursor && pagesCommitted > 0 ? cursor : null,
                ["hasNextPage"] = hasNextPage,
                ["boundedPartial"] = pagesCommitted > 0,
                ["pagesCommitted"] = pagesCommitted,
                ["providerVerificationStatus"] = CameraProviderIngestService.ExternalHold,
                ["certificationStatus"] = CameraProviderIngestService.ExternalHold,
                ["mediaAvailable"] = false,
            });

        try
        {
            using var client = Client(token, apiBaseUri);
            var sync = new SamsaraCameraSafetySync(client, scopeFactory, logger);
            var seenCursors = new HashSet<string>(StringComparer.Ordinal);
            if (!string.IsNullOrWhiteSpace(cursor)) seenCursors.Add(cursor);
            var completed = false;
            var configuredMaxPages = Math.Clamp(
                configuration.GetValue("Samsara:CameraSafetyMaxPagesPerSync", 20), 1, 100);
            var requestedMaxPages = body is { } bmp
                && bmp.TryGetProperty("maxPages", out var mp)
                && mp.TryGetInt32(out var requestedPages)
                    ? requestedPages
                    : configuredMaxPages;
            var maxPages = Math.Clamp(requestedMaxPages, 1, configuredMaxPages);
            var interPageDelayMs = Math.Clamp(
                configuration.GetValue("Samsara:CameraSafetyInterPageDelayMs", 250), 200, 2_000);
            var runCt = boundedCts.Token;

            for (var page = 0; page < maxPages; page++)
            {
                var pageSummary = await sync.RunAsync(
                    operation, startTime, cursor, DateTimeOffset.UtcNow, runCt);
                eventsObserved += pageSummary.EventsObserved;
                eventsAccepted += pageSummary.EventsAccepted;
                eventsReplayed += pageSummary.EventsReplayed;
                eventsQuarantined += pageSummary.EventsQuarantined;
                hasNextPage = pageSummary.HasNextPage;

                if (pageSummary.EventsQuarantined > 0)
                    throw new InvalidDataException(
                        "Samsara camera evidence entered quarantine; the durable cursor was not advanced past that page.");

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
                    throw new InvalidOperationException(
                        "Samsara camera safety pagination did not advance its cursor.");
                }
                cursor = candidateCursor;
                await Task.Delay(TimeSpan.FromMilliseconds(interPageDelayMs), runCt);
            }

            var boundedPartial = !completed && hasNextPage;
            return ConnectorResult.Ok(
                $"Recorded {eventsObserved} Samsara safety event(s) in the provider intake ledger; "
                + "provider verification, camera media and certification remain on External hold."
                + (boundedPartial
                    ? $" Reached the bounded {maxPages}-page run limit; the returned cursor will resume the backlog."
                    : string.Empty),
                new Dictionary<string, object?>
                {
                    ["lane"] = "camera-safety",
                    ["eventsObserved"] = eventsObserved,
                    ["eventsAccepted"] = eventsAccepted,
                    ["eventsReplayed"] = eventsReplayed,
                    ["eventsQuarantined"] = eventsQuarantined,
                    ["nextCursor"] = cursor,
                    ["hasNextPage"] = hasNextPage,
                    ["boundedPartial"] = boundedPartial,
                    ["providerVerificationStatus"] = CameraProviderIngestService.ExternalHold,
                    ["certificationStatus"] = CameraProviderIngestService.ExternalHold,
                    ["mediaAvailable"] = false,
                });
        }
        catch (UnauthorizedAccessException ex)
        {
            return Failure(ex.Message);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested && boundedCts.IsCancellationRequested)
        {
            return Failure(
                "Samsara camera safety sync reached its bounded duration. Complete pages were retained; the latest committed cursor will resume the backlog.");
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (SamsaraResponseReader.ResponseTooLargeException)
        {
            return Failure(
                "Samsara camera safety response exceeded the allowed size. Complete pages were retained; the oversized page was not consumed.");
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex,
                "Samsara camera safety sync failed for company {Company}", operation.CompanyId);
            return Failure(
                $"Samsara camera safety sync failed: {ex.Message} Complete pages were retained; "
                + (paginationIntegrityFailure
                    ? "the pre-run durable cursor was preserved because pagination integrity failed."
                    : "the latest committed cursor will resume the backlog."),
                persistCursor: !paginationIntegrityFailure);
        }
    }
}
