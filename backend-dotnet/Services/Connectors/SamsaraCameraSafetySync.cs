using System.Net;
using System.Text;
using System.Text.Json;
using Opstrax.Api.Services;

namespace Opstrax.Api.Services.Connectors;

/// <summary>
/// Pulls one page from Samsara's production safety-events stream and records each
/// provider event in the camera intake ledger. This lane deliberately stores no
/// playable media and creates no authoritative customer safety event: provider,
/// media, privacy and device evidence remain external certification holds.
/// </summary>
public sealed class SamsaraCameraSafetySync(
    HttpClient client,
    IServiceScopeFactory scopeFactory,
    ILogger logger)
{
    internal const string PayloadSchemaVersion = "samsara-safety-v2";
    private const int MaximumEventsPerPage = 2_000;

    public sealed record SyncSummary(
        int EventsObserved,
        int EventsAccepted,
        int EventsReplayed,
        int EventsQuarantined,
        string? NextCursor,
        bool HasNextPage);

    internal sealed record ParsedEvent(
        CameraProviderEventEnvelope Envelope,
        ReadOnlyMemory<byte> AuthenticatedPayload);

    internal sealed record ParsedPage(
        IReadOnlyList<ParsedEvent> Events,
        string EndCursor,
        bool HasNextPage);

    public async Task<SyncSummary> RunAsync(
        ConnectorOperationContext operation,
        DateTimeOffset startTimeUtc,
        string? afterCursor,
        DateTimeOffset retrievedAtUtc,
        CancellationToken ct)
    {
        if (startTimeUtc.Offset != TimeSpan.Zero || retrievedAtUtc.Offset != TimeSpan.Zero)
            throw new InvalidDataException("Samsara safety sync times must be UTC.");

        var url = "/safety-events/stream?startTime="
            + Uri.EscapeDataString(startTimeUtc.ToString("O"))
            + "&queryByTimeField=updatedAtTime&includeAsset=false&includeDriver=false";
        if (!string.IsNullOrWhiteSpace(afterCursor))
            url += "&after=" + Uri.EscapeDataString(afterCursor);

        using var document = await GetJsonWithRetryAsync(url, ct);
        var page = ParsePage(
            document.RootElement,
            $"opstrax-integration:{operation.IntegrationId}:generation:{operation.Generation}",
            retrievedAtUtc);

        if (page.Events.Count == 0)
            return new(0, 0, 0, 0, page.EndCursor, page.HasNextPage);

        using var scope = scopeFactory.CreateScope();
        var intake = scope.ServiceProvider.GetRequiredService<CameraProviderIngestService>();
        var accepted = 0;
        var replayed = 0;
        var quarantined = 0;
        foreach (var item in page.Events)
        {
            ct.ThrowIfCancellationRequested();
            var result = await intake.IngestUnderConnectorLeaseAsync(
                operation,
                item.Envelope,
                item.AuthenticatedPayload,
                ct);
            switch (result.Disposition)
            {
                case CameraProviderIngestDisposition.Accepted: accepted++; break;
                case CameraProviderIngestDisposition.Replay: replayed++; break;
                case CameraProviderIngestDisposition.Quarantined: quarantined++; break;
            }
        }

        logger.LogInformation(
            "Samsara camera safety page recorded: company={CompanyId} integration={IntegrationId} observed={Observed} accepted={Accepted} replayed={Replayed} quarantined={Quarantined}",
            operation.CompanyId, operation.IntegrationId, page.Events.Count, accepted, replayed, quarantined);
        return new(page.Events.Count, accepted, replayed, quarantined, page.EndCursor, page.HasNextPage);
    }

    internal static ParsedPage ParsePage(
        JsonElement root,
        string providerAccountReference,
        DateTimeOffset retrievedAtUtc)
    {
        if (root.ValueKind != JsonValueKind.Object)
            throw Invalid("the response root must be an object.");
        if (retrievedAtUtc.Offset != TimeSpan.Zero)
            throw Invalid("the provider receipt time must be UTC.");
        if (string.IsNullOrWhiteSpace(providerAccountReference))
            throw Invalid("the provider account reference is missing.");
        if (!root.TryGetProperty("data", out var data) || data.ValueKind != JsonValueKind.Array)
            throw Invalid("the required data array is missing.");
        if (data.GetArrayLength() > MaximumEventsPerPage)
            throw Invalid($"a page cannot contain more than {MaximumEventsPerPage} safety events.");

        var (endCursor, hasNextPage) = SamsaraSync.ReadPagination(root);
        // This endpoint is used as an unending updatedAtTime feed (no endTime).
        // Samsara documents a continuation cursor on every call; accepting an empty
        // cursor would restart the lookback window and make forward progress unclear.
        if (string.IsNullOrWhiteSpace(endCursor))
            throw Invalid("pagination.endCursor cannot be empty for the unending safety-events stream.");
        var parsed = new List<ParsedEvent>(data.GetArrayLength());
        var eventIds = new HashSet<string>(StringComparer.Ordinal);
        foreach (var item in data.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.Object)
                throw Invalid("each safety event must be an object.");
            var eventId = RequiredString(item, "id", 180);
            if (!eventIds.Add(eventId))
                throw Invalid("the page contains a duplicate safety event ID.");

            var assetId = ConsistentIdentity(item, "assetId", "asset", "vehicleId", "vehicle", required: true)!;
            var driverId = ConsistentIdentity(item, "driverId", "driver", null, null, required: false);
            var occurredAtUtc = RequiredUnixMilliseconds(item, "startMs");
            var providerUpdatedAtUtc = RequiredRfc3339Utc(item, "updatedAtTime");
            if (providerUpdatedAtUtc > retrievedAtUtc.AddMinutes(5))
                throw Invalid($"safety event '{eventId}' was updated after the local retrieval time.");
            if (occurredAtUtc > providerUpdatedAtUtc.AddMinutes(5))
                throw Invalid($"safety event '{eventId}' starts after the provider receipt time.");
            var eventType = EventType(item);

            var raw = Encoding.UTF8.GetBytes(item.GetRawText());
            parsed.Add(new ParsedEvent(
                new CameraProviderEventEnvelope(
                    "samsara",
                    providerAccountReference,
                    eventId,
                    PayloadSchemaVersion,
                    eventType,
                    occurredAtUtc,
                    providerUpdatedAtUtc,
                    BranchId: null,
                    VehicleId: null,
                    DriverId: null,
                    TripId: null,
                    VehicleExternalId: assetId,
                    DriverExternalId: driverId,
                    TripExternalId: null,
                    MediaReferences: Array.Empty<CameraProviderMediaReference>()),
                raw));
        }

        return new ParsedPage(parsed, endCursor, hasNextPage);
    }

    private static string EventType(JsonElement item)
    {
        if (!item.TryGetProperty("behaviorLabels", out var labels))
            return "SamsaraSafetyEvent";
        if (labels.ValueKind != JsonValueKind.Array)
            throw Invalid("safety event behaviorLabels must be an array.");
        if (labels.GetArrayLength() > 20)
            throw Invalid("a safety event cannot contain more than 20 behavior labels.");

        var values = new SortedSet<string>(StringComparer.Ordinal);
        foreach (var label in labels.EnumerateArray())
        {
            string? value = label.ValueKind switch
            {
                JsonValueKind.String => label.GetString(),
                JsonValueKind.Object => OptionalConsistentString(label, "name", "label"),
                _ => throw Invalid("each safety event behavior label must be a string or object."),
            };
            if (string.IsNullOrWhiteSpace(value) || value.Length > 80)
                throw Invalid("a safety event behavior label is missing or too long.");
            values.Add(value);
        }
        if (values.Count == 0) return "SamsaraSafetyEvent";
        var type = "Samsara:" + string.Join('|', values);
        if (type.Length > 120)
            throw Invalid("the combined safety event behavior labels are too long.");
        return type;
    }

    private static string? ConsistentIdentity(
        JsonElement item,
        string directName,
        string objectName,
        string? alternateDirectName,
        string? alternateObjectName,
        bool required)
    {
        var values = new List<string>();
        AddDirect(item, directName, values);
        AddObjectId(item, objectName, values);
        if (alternateDirectName is not null) AddDirect(item, alternateDirectName, values);
        if (alternateObjectName is not null) AddObjectId(item, alternateObjectName, values);
        var distinct = values.Distinct(StringComparer.Ordinal).ToArray();
        if (distinct.Length > 1)
            throw Invalid($"safety event {directName} identities conflict.");
        if (distinct.Length == 0)
        {
            if (required) throw Invalid($"safety event {directName} is missing.");
            return null;
        }
        return distinct[0];
    }

    private static void AddDirect(JsonElement item, string name, List<string> values)
    {
        if (!item.TryGetProperty(name, out var value) || value.ValueKind == JsonValueKind.Null) return;
        if (value.ValueKind != JsonValueKind.String || string.IsNullOrWhiteSpace(value.GetString()))
            throw Invalid($"safety event {name} must be a non-empty string.");
        var parsed = value.GetString()!;
        if (parsed.Length > 180) throw Invalid($"safety event {name} is too long.");
        values.Add(parsed);
    }

    private static void AddObjectId(JsonElement item, string name, List<string> values)
    {
        if (!item.TryGetProperty(name, out var value) || value.ValueKind == JsonValueKind.Null) return;
        if (value.ValueKind != JsonValueKind.Object)
            throw Invalid($"safety event {name} must be an object.");
        values.Add(RequiredString(value, "id", 180));
    }

    private static string RequiredString(JsonElement item, string name, int maximumLength)
    {
        if (!item.TryGetProperty(name, out var value)
            || value.ValueKind != JsonValueKind.String
            || string.IsNullOrWhiteSpace(value.GetString()))
            throw Invalid($"safety event {name} must be a non-empty string.");
        var parsed = value.GetString()!;
        if (parsed.Length > maximumLength)
            throw Invalid($"safety event {name} is too long.");
        return parsed;
    }

    private static string? OptionalConsistentString(JsonElement item, string first, string second)
    {
        string? Read(string name)
        {
            if (!item.TryGetProperty(name, out var value) || value.ValueKind == JsonValueKind.Null) return null;
            if (value.ValueKind != JsonValueKind.String)
                throw Invalid($"safety event behavior label {name} must be a string.");
            return value.GetString();
        }
        var one = Read(first);
        var two = Read(second);
        if (!string.IsNullOrWhiteSpace(one) && !string.IsNullOrWhiteSpace(two)
            && !string.Equals(one, two, StringComparison.Ordinal))
            throw Invalid("safety event behavior label names conflict.");
        return string.IsNullOrWhiteSpace(one) ? two : one;
    }

    private static DateTimeOffset RequiredUnixMilliseconds(JsonElement item, string name)
    {
        if (!item.TryGetProperty(name, out var value)
            || value.ValueKind != JsonValueKind.Number
            || !value.TryGetInt64(out var milliseconds))
            throw Invalid($"safety event {name} must be an integer Unix millisecond timestamp.");
        try { return DateTimeOffset.FromUnixTimeMilliseconds(milliseconds); }
        catch (ArgumentOutOfRangeException) { throw Invalid($"safety event {name} is outside the supported timestamp range."); }
    }

    private static DateTimeOffset RequiredRfc3339Utc(JsonElement item, string name)
    {
        if (!item.TryGetProperty(name, out var value)
            || value.ValueKind != JsonValueKind.String
            || !DateTimeOffset.TryParseExact(
                value.GetString(),
                ["yyyy-MM-dd'T'HH:mm:ssK", "yyyy-MM-dd'T'HH:mm:ss.FFFFFFFK"],
                System.Globalization.CultureInfo.InvariantCulture,
                System.Globalization.DateTimeStyles.None,
                out var parsed)
            || parsed.Offset != TimeSpan.Zero)
            throw Invalid($"safety event {name} must be an RFC 3339 UTC timestamp.");
        return parsed;
    }

    private async Task<JsonDocument> GetJsonWithRetryAsync(string url, CancellationToken ct)
    {
        for (var attempt = 0; ; attempt++)
        {
            TimeSpan retryAfter;
            using (var requestCts = CancellationTokenSource.CreateLinkedTokenSource(ct))
            {
                requestCts.CancelAfter(SamsaraResponseReader.RequestTimeout);
                using var response = await client.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, requestCts.Token);
                if (response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
                    throw new UnauthorizedAccessException(
                        "Samsara denied the Safety Events & Scores scope; no camera events were consumed.");
                if ((int)response.StatusCode is not (429 or >= 500) || attempt >= 4)
                {
                    response.EnsureSuccessStatusCode();
                    return await SamsaraResponseReader.ReadJsonAsync(response.Content, requestCts.Token);
                }
                retryAfter = SamsaraSync.ResolveRetryDelay(
                    response.Headers.RetryAfter, attempt, DateTimeOffset.UtcNow);
            }
            await Task.Delay(retryAfter, ct);
        }
    }

    private static InvalidDataException Invalid(string message) =>
        new($"Samsara safety stream response is invalid: {message}");
}
