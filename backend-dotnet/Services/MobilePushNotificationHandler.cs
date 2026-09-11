using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Opstrax.Api.Data;
using Opstrax.Api.Foundation;

namespace Opstrax.Api.Services;

/// <summary>
/// Delivers recipient-scoped OpsTrax notifications to active Expo device tokens.
/// The outbox aggregate id is a notification_recipients row, so tenant/user routing
/// is resolved entirely from server-owned rows; no mobile payload can select another
/// tenant or user. The notification payload contains no URL, tenant id, auth data, or
/// privileged navigation instruction. Tapping a push only wakes the authenticated app.
/// </summary>
public sealed class MobilePushNotificationHandler(
    Database db,
    IHttpClientFactory httpClientFactory,
    IConfiguration configuration,
    ILogger<MobilePushNotificationHandler> logger) : IOutboxMessageHandler
{
    public const string RequestedEventType = "mobile.push.notification.requested";
    public string EventType => RequestedEventType;

    public async Task HandleAsync(OutboxMessageRecord message, CancellationToken ct = default)
    {
        if (!long.TryParse(message.TenantId, out var companyId) || companyId <= 0) return;
        if (!long.TryParse(message.AggregateId, out var recipientId) || recipientId <= 0) return;

        await db.RunInSystemScopeAsync(
            () => HandleCoreAsync(companyId, recipientId, ct), ct);
    }

    private async Task HandleCoreAsync(long companyId, long recipientId, CancellationToken ct)
    {
        var recipient = await db.QuerySingleAsync(
            @"SELECT nr.id, nr.user_id, nr.status AS recipient_status, nr.external_ref,
                     n.id AS notification_id, n.title, n.message, n.severity, n.event_type
                FROM notification_recipients nr
                JOIN notifications n ON n.id=nr.notification_id AND n.company_id=nr.company_id
               WHERE nr.id=@rid AND nr.company_id=@cid
               LIMIT 1",
            c =>
            {
                c.Parameters.AddWithValue("@rid", recipientId);
                c.Parameters.AddWithValue("@cid", companyId);
            }, ct);
        if (recipient is null) return;

        // An already-recorded Expo outcome makes outbox redelivery idempotent.
        var externalRef = recipient["externalRef"]?.ToString();
        if (!string.IsNullOrWhiteSpace(externalRef) && externalRef.StartsWith("expo:", StringComparison.OrdinalIgnoreCase))
            return;

        if (recipient["userId"] is null or DBNull) return;
        var userId = Convert.ToInt64(recipient["userId"]);
        var recipientStatus = recipient["recipientStatus"]?.ToString()?.Trim().ToLowerInvariant() ?? "unread";
        if (recipientStatus is "read" or "acknowledged") return;

        var devices = await db.QueryAsync(
            @"SELECT id, push_token, token_fingerprint, product, platform
                FROM mobile_device_tokens
               WHERE company_id=@cid AND user_id=@uid AND status='active'
               ORDER BY last_registered_at DESC, id DESC
               LIMIT 20",
            c =>
            {
                c.Parameters.AddWithValue("@cid", companyId);
                c.Parameters.AddWithValue("@uid", userId);
            }, ct);

        if (devices.Count == 0)
        {
            await SetExternalRefAsync(companyId, recipientId, "expo:no-active-device", ct);
            return;
        }

        var title = Clamp(recipient["title"]?.ToString(), 100, "OpsTrax update");
        var body = Clamp(recipient["message"]?.ToString(), 240, "You have a new OpsTrax notification.");
        var outbound = devices.Select(device => new ExpoPushMessage(
            To: device["pushToken"]?.ToString() ?? string.Empty,
            Title: title,
            Body: body,
            Sound: "default",
            Priority: "high",
            ChannelId: "operations"
        )).Where(item => IsExpoToken(item.To)).ToList();

        if (outbound.Count == 0)
        {
            await RevokeMalformedTokensAsync(companyId, devices, ct);
            await SetExternalRefAsync(companyId, recipientId, "expo:no-valid-device", ct);
            return;
        }

        var client = httpClientFactory.CreateClient("expo-push");
        using var request = new HttpRequestMessage(HttpMethod.Post, "--/api/v2/push/send")
        {
            Content = new StringContent(JsonSerializer.Serialize(outbound, JsonOptions), Encoding.UTF8, "application/json")
        };
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        var accessToken = configuration["Expo:AccessToken"]?.Trim()
                          ?? configuration["EXPO_ACCESS_TOKEN"]?.Trim();
        if (!string.IsNullOrWhiteSpace(accessToken))
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);

        using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);
        var payload = await response.Content.ReadAsStringAsync(ct);
        if (!response.IsSuccessStatusCode)
            throw new InvalidOperationException($"Expo push service returned HTTP {(int)response.StatusCode}.");

        ExpoPushEnvelope? envelope;
        try
        {
            envelope = JsonSerializer.Deserialize<ExpoPushEnvelope>(payload, JsonOptions);
        }
        catch (JsonException ex)
        {
            throw new InvalidOperationException("Expo push service returned an unreadable response.", ex);
        }

        var tickets = envelope?.Data ?? [];
        var okTicketIds = new List<string>();
        var transientFailures = 0;
        var invalidDeviceIndexes = new List<int>();

        for (var index = 0; index < outbound.Count; index++)
        {
            var ticket = index < tickets.Count ? tickets[index] : null;
            if (ticket is null)
            {
                transientFailures++;
                continue;
            }
            if (string.Equals(ticket.Status, "ok", StringComparison.OrdinalIgnoreCase) && !string.IsNullOrWhiteSpace(ticket.Id))
            {
                okTicketIds.Add(ticket.Id!);
                continue;
            }

            var code = ticket.Details?.Error?.Trim();
            if (string.Equals(code, "DeviceNotRegistered", StringComparison.OrdinalIgnoreCase))
                invalidDeviceIndexes.Add(index);
            else
                transientFailures++;
        }

        foreach (var index in invalidDeviceIndexes)
        {
            if (index >= devices.Count) continue;
            var deviceId = Convert.ToInt64(devices[index]["id"]);
            await db.ExecuteAsync(
                @"UPDATE mobile_device_tokens
                     SET status='revoked', revoked_at=NOW(), updated_at=NOW()
                   WHERE company_id=@cid AND id=@id AND status='active'",
                c =>
                {
                    c.Parameters.AddWithValue("@cid", companyId);
                    c.Parameters.AddWithValue("@id", deviceId);
                }, ct);
        }

        if (okTicketIds.Count == 0 && transientFailures > 0)
            throw new InvalidOperationException("Expo push service did not accept any device delivery tickets.");

        var marker = okTicketIds.Count > 0
            ? $"expo:{okTicketIds[0]}"
            : "expo:no-active-device";
        await SetExternalRefAsync(companyId, recipientId, marker, ct);

        // Log counts only. Raw provider tokens and ticket payload bodies are intentionally excluded.
        logger.LogInformation(
            "Mobile push handled recipient {RecipientId} company {CompanyId}: accepted={Accepted} revoked={Revoked} transient={Transient}",
            recipientId, companyId, okTicketIds.Count, invalidDeviceIndexes.Count, transientFailures);
    }

    private async Task SetExternalRefAsync(long companyId, long recipientId, string marker, CancellationToken ct)
        => await db.ExecuteAsync(
            @"UPDATE notification_recipients
                 SET external_ref=@ref, updated_at=NOW()
               WHERE company_id=@cid AND id=@rid AND (external_ref IS NULL OR external_ref NOT LIKE 'expo:%')",
            c =>
            {
                c.Parameters.AddWithValue("@ref", marker.Length <= 255 ? marker : marker[..255]);
                c.Parameters.AddWithValue("@cid", companyId);
                c.Parameters.AddWithValue("@rid", recipientId);
            }, ct);

    private async Task RevokeMalformedTokensAsync(long companyId, IReadOnlyList<Dictionary<string, object?>> devices, CancellationToken ct)
    {
        foreach (var device in devices)
        {
            var token = device["pushToken"]?.ToString() ?? string.Empty;
            if (IsExpoToken(token)) continue;
            var id = Convert.ToInt64(device["id"]);
            await db.ExecuteAsync(
                @"UPDATE mobile_device_tokens SET status='revoked',revoked_at=NOW(),updated_at=NOW()
                   WHERE company_id=@cid AND id=@id AND status='active'",
                c => { c.Parameters.AddWithValue("@cid", companyId); c.Parameters.AddWithValue("@id", id); }, ct);
        }
    }

    private static bool IsExpoToken(string token)
        => token.Length is >= 20 and <= 4096
           && !token.Any(char.IsWhiteSpace)
           && (token.StartsWith("ExpoPushToken[", StringComparison.Ordinal)
               || token.StartsWith("ExponentPushToken[", StringComparison.Ordinal))
           && token.EndsWith(']');

    private static string Clamp(string? value, int max, string fallback)
    {
        var clean = string.IsNullOrWhiteSpace(value) ? fallback : value.Trim();
        return clean.Length <= max ? clean : clean[..max];
    }

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = true
    };

    private sealed record ExpoPushMessage(
        string To,
        string Title,
        string Body,
        string Sound,
        string Priority,
        string ChannelId);

    private sealed record ExpoPushEnvelope(List<ExpoPushTicket>? Data);
    private sealed record ExpoPushTicket(string? Status, string? Id, string? Message, ExpoPushDetails? Details);
    private sealed record ExpoPushDetails(string? Error);
}
