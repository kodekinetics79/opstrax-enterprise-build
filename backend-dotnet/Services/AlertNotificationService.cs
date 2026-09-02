using System.Text.Json;
using Opstrax.Api.Data;
using Opstrax.Api.Foundation;
using Opstrax.Api.Services.Connectors;

namespace Opstrax.Api.Services;

// ─────────────────────────────────────────────────────────────────────────────
// ALERT → NOTIFICATION DELIVERY SPINE
//
// Settings → Notifications renders a per-user channel matrix (user_notification_prefs)
// for fleet alert events, but until this file nothing consumed it: telemetry_alerts
// rows were created by ingest and never fanned out to any channel, so every Email/SMS
// toggle was a dead switch. Two pieces close that gap, both keyed on the SAME pref
// vocabulary the SPA stores (event keys like 'sos_panic'; channel keys are the
// display-cased strings 'Email' / 'SMS' / 'In-App' — that casing is the stored
// contract, see SettingsPage.tsx CHANNELS).
//
//   AlertNotificationBridgeService (hosted, 60s):
//     telemetry_alerts → claim via outbox INSERT (partial unique index, detention
//     pattern) → in-app notification for users whose prefs enable In-App.
//   AlertNotificationDeliveryHandler (outbox consumer):
//     'alert.notification.requested' → email via platform SMTP + SMS via the
//     company's twilio-sms integration, for users whose prefs enable each channel,
//     each send claim-guarded by alert_notification_deliveries so at-least-once
//     outbox redelivery can never double-send.
//
// Users with no prefs row get the SPA's defaults (In-App everything; Email for
// SOS + accident; SMS for SOS) — the settings page shows those toggles as ON for
// users who never saved, so delivery must honor them or the page lies.
// Fail-soft everywhere: unconfigured SMTP/Twilio skips that channel silently and
// the in-app notification still lands.
// ─────────────────────────────────────────────────────────────────────────────
public static class AlertNotificationEvents
{
    public const string EventType = "alert.notification.requested";

    /// <summary>telemetry_alerts.alert_type → Settings-page pref key; null = not a user-facing pref event.</summary>
    public static string? MapPrefKey(string alertType) => alertType.ToLowerInvariant() switch
    {
        "speeding"                                            => "speed_alert",
        "geofence_breach" or "geofence_exit" or "geofence_enter" => "geofence_breach",
        "idling" or "idle" or "excessive_idling"              => "idle_alert",
        "sos"                                                 => "sos_panic",
        "hos_violation"                                       => "hos_violation",
        "maintenance_due"                                     => "maintenance_due",
        "crash"                                               => "accident_event",
        "sla_breach"                                          => "sla_breach",
        "fuel_anomaly"                                        => "fuel_anomaly",
        "stale_device" or "device_offline"                    => "device_offline",
        _ => null,
    };

    public static string Label(string prefKey) => prefKey switch
    {
        "speed_alert"     => "Speed Alert",
        "geofence_breach" => "Geofence Breach",
        "idle_alert"      => "Excessive Idling",
        "sos_panic"       => "SOS / Panic",
        "hos_violation"   => "HOS Violation",
        "maintenance_due" => "Maintenance Due",
        "accident_event"  => "Accident / Collision",
        "sla_breach"      => "SLA Breach Risk",
        "fuel_anomaly"    => "Fuel Anomaly",
        "device_offline"  => "Device Offline",
        _ => prefKey,
    };

    // Mirror of SettingsPage.tsx buildDefaultNotifPrefs — what a user who never saved sees as ON.
    public static bool DefaultFor(string prefKey, string channel) => channel switch
    {
        "Email"  => prefKey is "sos_panic" or "accident_event",
        "SMS"    => prefKey is "sos_panic",
        "In-App" => true,
        _ => false,
    };

    /// <summary>
    /// Active ops users of a company whose stored prefs (or the default when no row / no key)
    /// enable {prefKey, channel}. Drivers and customer-portal users are excluded — the matrix
    /// is an ops-staff surface; drivers get their own targeted driver notifications.
    /// Phone resolves users.phone first, then the linked driver row for driver-linked users.
    /// </summary>
    public static Task<List<Dictionary<string, object?>>> RecipientsAsync(
        Database db, long companyId, string prefKey, string channel, CancellationToken ct)
        => db.QueryAsync(
            @"SELECT u.id, u.full_name, u.email, COALESCE(u.phone, d.phone) AS phone
              FROM users u
              LEFT JOIN drivers d ON d.user_id = u.id AND d.company_id = u.company_id AND d.deleted_at IS NULL
              LEFT JOIN user_notification_prefs unp ON unp.user_id = u.id AND unp.company_id = u.company_id
              WHERE u.company_id = @cid AND u.status = 'Active'
                AND LOWER(u.role_name) NOT IN ('customer', 'driver')
                AND COALESCE((unp.prefs -> @pk ->> @chan)::boolean, @def)",
            c =>
            {
                c.Parameters.AddWithValue("@cid",  companyId);
                c.Parameters.AddWithValue("@pk",   prefKey);
                c.Parameters.AddWithValue("@chan", channel);
                c.Parameters.AddWithValue("@def",  DefaultFor(prefKey, channel));
            }, ct);

    public static int PriorityFor(string severity) => severity.ToLowerInvariant() switch
    {
        "critical" => 1,
        "high"     => 3,
        _          => 5,
    };
}

// Bridges telemetry_alerts into the notification spine. The outbox INSERT doubles as the
// processed-marker (ON CONFLICT on the partial unique index ux_outbox_alert_notification):
// in-app fan-out happens only for the tick that wins the claim, so an alert is bridged
// exactly once no matter how often the sweep re-sees it.
public sealed class AlertNotificationBridgeService(
    IServiceScopeFactory scopeFactory,
    ILogger<AlertNotificationBridgeService> logger,
    ServiceRunTracker tracker) : BackgroundService
{
    // Alerts carry SOS/crash — a 5-minute cadence would be an eternity; keep the sweep tight.
    private static readonly TimeSpan Interval = TimeSpan.FromSeconds(60);
    private const string SvcName = "AlertNotificationBridgeService";

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Startup delay — schema ensures must complete first.
        await Task.Delay(TimeSpan.FromSeconds(40), stoppingToken).ContinueWith(_ => { }, stoppingToken);
        logger.LogInformation("{Svc} started", SvcName);

        while (!stoppingToken.IsCancellationRequested)
        {
            var sw = System.Diagnostics.Stopwatch.StartNew();
            var runId = await tracker.BeginAsync(SvcName, stoppingToken);
            try
            {
                await RunCycleAsync(stoppingToken);
                sw.Stop();
                await tracker.CompleteAsync(runId, SvcName, 0, (int)sw.ElapsedMilliseconds, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
            catch (Exception ex)
            {
                sw.Stop();
                logger.LogError(ex, "{Svc} cycle failed", SvcName);
                await tracker.FailAsync(runId, SvcName, ex, (int)sw.ElapsedMilliseconds, stoppingToken);
            }

            try { await Task.Delay(Interval, stoppingToken); }
            catch (OperationCanceledException) { break; }
        }
        logger.LogInformation("{Svc} stopped", SvcName);
    }

    private async Task RunCycleAsync(CancellationToken ct)
    {
        using var scope = scopeFactory.CreateScope();
        var db    = scope.ServiceProvider.GetRequiredService<Database>();
        var notif = scope.ServiceProvider.GetRequiredService<NotificationService>();

        // Cross-tenant sweep filtered by company_id per row → platform-admin bypass scope.
        await db.RunInSystemScopeAsync(() => RunCycleCoreAsync(db, notif, ct), ct);
    }

    private async Task RunCycleCoreAsync(Database db, NotificationService notif, CancellationToken ct)
    {
        // The 24h window bounds the first-ever sweep on an existing install: history older
        // than a day is stale news nobody should be emailed about. NOT EXISTS keeps the
        // steady-state query cheap; the outbox claim below is what guarantees exactly-once.
        var alerts = await db.QueryAsync(
            @"SELECT ta.id, ta.company_id, ta.alert_type, ta.severity, ta.message, ta.created_at
              FROM telemetry_alerts ta
              WHERE ta.created_at > NOW() - INTERVAL '24 hours'
                AND ta.alert_type IN ('speeding','geofence_breach','geofence_exit','geofence_enter',
                                      'idling','idle','excessive_idling','sos','crash',
                                      'hos_violation','maintenance_due','sla_breach','fuel_anomaly',
                                      'stale_device','device_offline')
                AND NOT EXISTS (SELECT 1 FROM outbox_messages om
                                WHERE om.event_type = 'alert.notification.requested'
                                  AND om.tenant_id = ta.company_id
                                  AND om.aggregate_id = ta.id::text)
              ORDER BY ta.id
              LIMIT 200",
            ct: ct);

        foreach (var alert in alerts)
        {
            ct.ThrowIfCancellationRequested();
            var alertId   = Convert.ToInt64(alert["id"]);
            var companyId = Convert.ToInt64(alert["companyId"]);
            var alertType = alert["alertType"]?.ToString() ?? "";
            var severity  = alert["severity"]?.ToString() ?? "High";
            var message   = alert["message"]?.ToString() ?? "";

            var prefKey = AlertNotificationEvents.MapPrefKey(alertType);
            if (prefKey is null) continue;

            try
            {
                // Claim: the winning INSERT returns a row; a concurrent/previous claim returns none.
                // Requires ux_outbox_alert_notification (NotificationSchemaService / stage85) — until
                // it exists this throws 42P10, is logged, and the alert is retried next tick.
                var claimed = await db.QuerySingleAsync(
                    @"INSERT INTO outbox_messages (tenant_id, event_type, aggregate_type, aggregate_id, payload_json, status, retry_count)
                      VALUES (@t, 'alert.notification.requested', 'telemetry_alert', @agg,
                              jsonb_build_object('alertId', @aid, 'alertType', @atype, 'prefKey', @pk), 'pending', 0)
                      ON CONFLICT (tenant_id, aggregate_id) WHERE event_type = 'alert.notification.requested' DO NOTHING
                      RETURNING id",
                    c =>
                    {
                        c.Parameters.AddWithValue("@t",     companyId);
                        c.Parameters.AddWithValue("@agg",   alertId.ToString());
                        c.Parameters.AddWithValue("@aid",   alertId);
                        c.Parameters.AddWithValue("@atype", alertType);
                        c.Parameters.AddWithValue("@pk",    prefKey);
                    }, ct);
                if (claimed is null) continue;   // already bridged

                var inAppUsers = await AlertNotificationEvents.RecipientsAsync(db, companyId, prefKey, "In-App", ct);
                if (inAppUsers.Count > 0)
                {
                    var label = AlertNotificationEvents.Label(prefKey);
                    await notif.CreateAsync(
                        companyId,
                        eventType: $"alert.{prefKey}",
                        sourceType: "telemetry_alert",
                        sourceId: alertId,
                        severity: severity,
                        title: $"{label} — {severity}",
                        message: string.IsNullOrWhiteSpace(message) ? label : message,
                        audienceType: "ops",
                        ct,
                        priority: AlertNotificationEvents.PriorityFor(severity),
                        dedupeKey: $"alert.notif.{alertId}",
                        targetUserIds: inAppUsers.Select(u => Convert.ToInt64(u["id"])).ToList());
                }
            }
            catch (Exception ex)
            {
                // One bad alert (or the not-yet-applied stage85 index) must not stall the batch.
                logger.LogWarning(ex, "Alert notification bridge failed for telemetry_alert {AlertId} (company {CompanyId})",
                    alertId, companyId);
            }
        }
    }
}

// Consumes 'alert.notification.requested': email + SMS fan-out per user prefs. Every send is
// claim-guarded by alert_notification_deliveries (UNIQUE company_id, alert_id, user_id, channel),
// so outbox redelivery re-enters safely and only un-attempted recipients are tried again.
public sealed class AlertNotificationDeliveryHandler(
    Database db,
    PlatformMailService mail,
    PlatformSettingsService settings,
    ConnectorRegistry connectors,
    ILogger<AlertNotificationDeliveryHandler> logger) : IOutboxMessageHandler
{
    public string EventType => AlertNotificationEvents.EventType;

    public async Task HandleAsync(OutboxMessageRecord message, CancellationToken ct = default)
    {
        if (!long.TryParse(message.TenantId, out var companyId)) return;
        if (!long.TryParse(message.AggregateId, out var alertId)) return;

        var alert = await db.QuerySingleAsync(
            "SELECT alert_type, severity, message, created_at FROM telemetry_alerts WHERE company_id=@c AND id=@id",
            c => { c.Parameters.AddWithValue("@c", companyId); c.Parameters.AddWithValue("@id", alertId); }, ct);
        if (alert is null) return;

        var alertType = alert["alertType"]?.ToString() ?? "";
        var prefKey = AlertNotificationEvents.MapPrefKey(alertType);
        if (prefKey is null) return;

        var severity  = alert["severity"]?.ToString() ?? "High";
        var body      = alert["message"]?.ToString() ?? "";
        var createdAt = alert["createdAt"] is DateTime dt ? dt : DateTime.UtcNow;
        var label     = AlertNotificationEvents.Label(prefKey);

        await DeliverEmailAsync(companyId, alertId, prefKey, label, severity, body, createdAt, ct);
        await DeliverSmsAsync(companyId, alertId, prefKey, label, severity, body, ct);
    }

    private async Task DeliverEmailAsync(
        long companyId, long alertId, string prefKey, string label, string severity,
        string body, DateTime createdAt, CancellationToken ct)
    {
        // Unconfigured SMTP: claim nothing, so recipients are still fresh if the operator
        // configures mail and the message is redelivered (detention fail-soft semantics).
        if (!await mail.IsConfiguredAsync(ct)) return;

        var recipients = await AlertNotificationEvents.RecipientsAsync(db, companyId, prefKey, "Email", ct);
        if (recipients.Count == 0) return;

        var appUrl = await settings.GetTenantAppUrlAsync(ct);
        var subject = $"OpsTrax alert — {label} ({severity})";
        var text =
            $"""
            {(string.IsNullOrWhiteSpace(body) ? label : body)}

            Type:       {label}
            Severity:   {severity}
            Time (UTC): {createdAt:yyyy-MM-dd HH:mm}
            {(string.IsNullOrWhiteSpace(appUrl) ? "" : $"\nView alerts: {appUrl}/alerts\nManage notification preferences: {appUrl}/settings")}
            """;

        foreach (var r in recipients)
        {
            var userId = Convert.ToInt64(r["id"]);
            var email = r["email"]?.ToString();
            if (string.IsNullOrWhiteSpace(email)) continue;

            var claimId = await ClaimAsync(companyId, alertId, userId, "email", email, ct);
            if (claimId is null) continue;   // already attempted on a previous delivery

            var sent = await mail.TrySendAsync(email, subject, text, ct);
            await SettleAsync(claimId.Value, companyId, sent, sent ? null : "smtp send failed", ct);
        }
    }

    private async Task DeliverSmsAsync(
        long companyId, long alertId, string prefKey, string label, string severity,
        string body, CancellationToken ct)
    {
        var recipients = (await AlertNotificationEvents.RecipientsAsync(db, companyId, prefKey, "SMS", ct))
            .Where(r => !string.IsNullOrWhiteSpace(r["phone"]?.ToString()))
            .ToList();
        if (recipients.Count == 0) return;

        // The company's Twilio integration; config columns arrive via later migrations, so any
        // failure here (missing columns, no row, undecryptable config) means "SMS not set up".
        IReadOnlyDictionary<string, string?>? config = null;
        try
        {
            var row = await db.QuerySingleAsync(
                "SELECT config_json FROM integrations WHERE company_id=@cid AND integration_key='twilio-sms' LIMIT 1",
                c => c.Parameters.AddWithValue("@cid", companyId), ct);
            var raw = row?["configJson"]?.ToString();
            if (!string.IsNullOrWhiteSpace(raw)) config = connectors.DecryptConfig(raw);
        }
        catch (Exception ex)
        {
            logger.LogDebug(ex, "No usable twilio-sms integration for company {CompanyId}; skipping SMS", companyId);
            return;
        }
        if (config is null) return;

        var connector = connectors.Resolve("twilio-sms");
        // SMS is terse by design; the alert message already names the vehicle/driver context.
        var smsText = $"OpsTrax {label} ({severity}): {body}";
        if (smsText.Length > 320) smsText = smsText[..317] + "...";

        foreach (var r in recipients)
        {
            var userId = Convert.ToInt64(r["id"]);
            var phone = r["phone"]!.ToString()!;

            var claimId = await ClaimAsync(companyId, alertId, userId, "sms", phone, ct);
            if (claimId is null) continue;

            ConnectorResult result;
            try
            {
                using var doc = JsonDocument.Parse(JsonSerializer.Serialize(new { to = phone, body = smsText }));
                result = await connector.RunActionAsync("send", config, doc.RootElement.Clone(), ct);
            }
            catch (Exception ex)
            {
                result = ConnectorResult.Fail(ex.Message);
            }
            await SettleAsync(claimId.Value, companyId, result.Success, result.Success ? null : result.Message, ct);
        }
    }

    private async Task<long?> ClaimAsync(long companyId, long alertId, long userId, string channel, string recipient, CancellationToken ct)
    {
        var row = await db.QuerySingleAsync(
            @"INSERT INTO alert_notification_deliveries (company_id, alert_id, user_id, channel, recipient, status)
              VALUES (@cid, @aid, @uid, @chan, @rcpt, 'pending')
              ON CONFLICT (company_id, alert_id, user_id, channel) DO NOTHING
              RETURNING id",
            c =>
            {
                c.Parameters.AddWithValue("@cid",  companyId);
                c.Parameters.AddWithValue("@aid",  alertId);
                c.Parameters.AddWithValue("@uid",  userId);
                c.Parameters.AddWithValue("@chan", channel);
                c.Parameters.AddWithValue("@rcpt", recipient);
            }, ct);
        return row?["id"] is { } id and not DBNull ? Convert.ToInt64(id) : null;
    }

    private Task SettleAsync(long claimId, long companyId, bool sent, string? error, CancellationToken ct)
        => db.ExecuteAsync(
            @"UPDATE alert_notification_deliveries
              SET status=@s, error=@e, sent_at=CASE WHEN @s='sent' THEN NOW() ELSE sent_at END
              WHERE company_id=@cid AND id=@id",
            c =>
            {
                c.Parameters.AddWithValue("@s",   sent ? "sent" : "failed");
                c.Parameters.AddWithValue("@e",   error ?? (object)DBNull.Value);
                c.Parameters.AddWithValue("@cid", companyId);
                c.Parameters.AddWithValue("@id",  claimId);
            }, ct);
}
