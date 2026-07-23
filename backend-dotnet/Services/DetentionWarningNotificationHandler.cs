using Opstrax.Api.Data;
using Opstrax.Api.Foundation;

namespace Opstrax.Api.Services;

// Delivery seam for the pre-expiry 'meter running' notice (the contemporaneous-notice proof that
// legally preserves the right to bill detention). Consumes the durable detention.dwell.warning
// outbox event and attempts real email delivery via the platform SMTP; the notice flips
// 'logged' -> 'sent' (or 'failed'). Fail-soft: with SMTP unconfigured the notice stays 'logged' —
// the evidence bundle already discloses delivery_status honestly. Delivery-status-guarded, so
// at-least-once outbox redelivery never double-sends.
public sealed class DetentionWarningNotificationHandler(Database db) : IOutboxMessageHandler
{
    public string EventType => "detention.dwell.warning";

    public async Task HandleAsync(OutboxMessageRecord message, CancellationToken ct = default)
    {
        if (!long.TryParse(message.TenantId, out var companyId)) return;
        if (!long.TryParse(message.AggregateId, out var dwellId)) return;

        var notice = await db.QuerySingleAsync(
            @"SELECT id, recipient_address, body_snapshot FROM detention_notices
              WHERE company_id=@c AND dwell_id=@d AND notice_type='customer_meter_running' AND delivery_status='logged'",
            c => { c.Parameters.AddWithValue("@c", companyId); c.Parameters.AddWithValue("@d", dwellId); }, ct);
        if (notice is null) return;   // already delivered (or no notice) — idempotent no-op

        var to = notice["recipientAddress"]?.ToString();
        if (string.IsNullOrWhiteSpace(to) || !PlatformMailService.IsConfigured) return;   // stays 'logged'

        var sent = await PlatformMailService.TrySendAsync(
            to, "Detention notice — free time expiring", notice["bodySnapshot"]?.ToString() ?? "", ct);

        await db.ExecuteAsync(
            @"UPDATE detention_notices SET delivery_status=@s, sent_at=CASE WHEN @s='sent' THEN NOW() ELSE sent_at END
              WHERE company_id=@c AND id=@id AND delivery_status='logged'",
            c =>
            {
                c.Parameters.AddWithValue("@c", companyId);
                c.Parameters.AddWithValue("@id", Convert.ToInt64(notice["id"]));
                c.Parameters.AddWithValue("@s", sent ? "sent" : "failed");
            }, ct);
    }
}
