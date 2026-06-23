using Opstrax.Api.Data;

namespace Opstrax.Api.Services;

public sealed class NotificationService(Database db)
{
    /// <summary>
    /// Creates a notification and resolves recipients.
    /// Returns the new notification ID, or -1 if deduplicated/suppressed.
    /// </summary>
    public async Task<long> CreateAsync(
        long companyId,
        string eventType,
        string sourceType,
        long? sourceId,
        string severity,
        string title,
        string message,
        string audienceType,
        CancellationToken ct,
        long? targetDriverId = null,
        long? targetUserId = null,
        string channel = "in_app",
        int priority = 5,
        string? dedupeKey = null,
        TimeSpan? suppressionWindow = null)
    {
        // Deduplication check
        if (!string.IsNullOrWhiteSpace(dedupeKey))
        {
            var windowMinutes = (int)(suppressionWindow?.TotalMinutes ?? 60);
            var existing = await db.ScalarLongAsync(
                @"SELECT COUNT(*) FROM notifications
                  WHERE company_id=@cid AND dedupe_key=@key
                    AND status NOT IN ('read','acknowledged','suppressed')
                    AND created_at > NOW() - @windowMinutes * INTERVAL '1 minute'",
                c =>
                {
                    c.Parameters.AddWithValue("@cid", companyId);
                    c.Parameters.AddWithValue("@key", dedupeKey);
                    c.Parameters.AddWithValue("@windowMinutes", windowMinutes);
                }, ct);
            if (existing > 0) return -1L;
        }

        // Sanitize message — strip internal operational data
        var safeMessage = SanitizeMessage(message, audienceType);

        // Insert notification
        var notifId = await db.InsertAsync(
            @"INSERT INTO notifications
                (company_id, source_type, source_id, event_type, severity, title, message,
                 audience_type, channel, status, dedupe_key, priority, delivered_at)
              VALUES (@cid, @srcType, @srcId, @evType, @sev, @title, @msg,
                      @aud, @chan, 'unread', @dedup, @pri,
                      CASE WHEN @chan='in_app' THEN NOW() ELSE NULL END)",
            c =>
            {
                c.Parameters.AddWithValue("@cid",     companyId);
                c.Parameters.AddWithValue("@srcType",  sourceType);
                c.Parameters.AddWithValue("@srcId",    sourceId.HasValue ? sourceId.Value : DBNull.Value);
                c.Parameters.AddWithValue("@evType",   eventType);
                c.Parameters.AddWithValue("@sev",      severity);
                c.Parameters.AddWithValue("@title",    title);
                c.Parameters.AddWithValue("@msg",      safeMessage);
                c.Parameters.AddWithValue("@aud",      audienceType);
                c.Parameters.AddWithValue("@chan",      channel);
                c.Parameters.AddWithValue("@dedup",    string.IsNullOrWhiteSpace(dedupeKey) ? DBNull.Value : dedupeKey);
                c.Parameters.AddWithValue("@pri",      priority);
            }, ct);

        // Resolve recipients
        if (targetUserId.HasValue)
        {
            await InsertRecipientAsync(notifId, companyId, targetUserId.Value, null, null, channel, ct);
        }
        else if (audienceType == "driver" && targetDriverId.HasValue)
        {
            // Resolve driver → user_id
            var driverRow = await db.QuerySingleAsync(
                "SELECT user_id FROM drivers WHERE id=@did AND company_id=@cid AND deleted_at IS NULL LIMIT 1",
                c => { c.Parameters.AddWithValue("@did", targetDriverId.Value); c.Parameters.AddWithValue("@cid", companyId); }, ct);

            var driverUserId = driverRow?["userId"] is not null and not DBNull
                ? Convert.ToInt64(driverRow["userId"])
                : (long?)null;

            if (driverUserId.HasValue)
                await InsertRecipientAsync(notifId, companyId, driverUserId.Value, targetDriverId.Value, null, channel, ct);
            else
                // Fallback role-broadcast row
                await InsertRoleRecipientAsync(notifId, companyId, "driver", channel, ct);
        }
        else
        {
            // Broadcast by role
            var roleMap = MapAudienceToRole(audienceType);
            var users = await db.QueryAsync(
                "SELECT id FROM users WHERE company_id=@cid AND role_name=@role AND status='Active' AND deleted_at IS NULL",
                c => { c.Parameters.AddWithValue("@cid", companyId); c.Parameters.AddWithValue("@role", roleMap); }, ct);

            foreach (var u in users)
            {
                var uid = Convert.ToInt64(u["id"]);
                await InsertRecipientAsync(notifId, companyId, uid, null, null, channel, ct);
            }

            // Always add role-broadcast fallback row
            await InsertRoleRecipientAsync(notifId, companyId, audienceType, channel, ct);
        }

        // For external channels, mark as not configured
        if (channel != "in_app")
        {
            await db.ExecuteAsync(
                "UPDATE notification_recipients SET external_ref='not_configured' WHERE notification_id=@nid",
                c => c.Parameters.AddWithValue("@nid", notifId), ct);
        }

        return notifId;
    }

    private async Task InsertRecipientAsync(long notifId, long companyId, long userId,
        long? driverId, string? roleTarget, string channel, CancellationToken ct)
    {
        await db.InsertAsync(
            @"INSERT INTO notification_recipients
                (notification_id, company_id, user_id, driver_id, role_target, status, channel, delivered_at)
              VALUES (@nid, @cid, @uid, @did, @role, 'unread', @chan,
                      CASE WHEN @chan='in_app' THEN NOW() ELSE NULL END)",
            c =>
            {
                c.Parameters.AddWithValue("@nid",  notifId);
                c.Parameters.AddWithValue("@cid",  companyId);
                c.Parameters.AddWithValue("@uid",  userId);
                c.Parameters.AddWithValue("@did",  driverId.HasValue ? driverId.Value : DBNull.Value);
                c.Parameters.AddWithValue("@role", roleTarget ?? (object)DBNull.Value);
                c.Parameters.AddWithValue("@chan", channel);
            }, ct);
    }

    private async Task InsertRoleRecipientAsync(long notifId, long companyId, string roleTarget, string channel, CancellationToken ct)
    {
        await db.InsertAsync(
            @"INSERT INTO notification_recipients
                (notification_id, company_id, user_id, driver_id, role_target, status, channel, delivered_at)
              VALUES (@nid, @cid, NULL, NULL, @role, 'unread', @chan,
                      CASE WHEN @chan='in_app' THEN NOW() ELSE NULL END)",
            c =>
            {
                c.Parameters.AddWithValue("@nid",  notifId);
                c.Parameters.AddWithValue("@cid",  companyId);
                c.Parameters.AddWithValue("@role", roleTarget);
                c.Parameters.AddWithValue("@chan", channel);
            }, ct);
    }

    private static string MapAudienceToRole(string audienceType) => audienceType switch
    {
        "dispatcher"          => "Dispatcher",
        "fleet_manager"       => "Fleet Manager",
        "safety_manager"      => "Safety Manager",
        "maintenance"         => "Maintenance Manager",
        "admin"               => "Tenant Admin",
        "customer"            => "Customer",
        _                     => audienceType,
    };

    /// <summary>
    /// Prevent internal operational data from leaking into customer-facing or driver notifications.
    /// </summary>
    private static string SanitizeMessage(string message, string audienceType)
    {
        // Customer and driver notifications must not contain internal safety scores or notes
        if (audienceType is "customer" or "driver")
        {
            // Strip anything that looks like internal safety score data
            if (message.Contains("safety_score") || message.Contains("eligibility_json"))
                return "You have a new notification. Please contact your dispatcher for details.";
        }
        return message;
    }
}
