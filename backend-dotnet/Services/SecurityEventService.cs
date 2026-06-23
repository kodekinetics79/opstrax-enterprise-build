using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Opstrax.Api.Data;

namespace Opstrax.Api.Services;

// ─────────────────────────────────────────────────────────────────────────────
// SecurityEventService — Scoped
//
// Records sanitized security events. All events are:
//   - Tenant-scoped (company_id required)
//   - IP truncated to first 3 octets (IPv4) or /48 prefix (IPv6)
//   - User-agent hashed (first 8 hex chars of SHA-256) — not stored in full
//   - Free-text message must be pre-sanitized by caller (safe_message)
//   - Metadata JSON must not include PII beyond what's necessary
//
// Events are never returned across tenant boundaries.
//
// Known event types:
//   login.success, login.failure, login.lockout, logout
//   mfa.required, mfa.satisfied, mfa.failed, mfa.enrolled
//   password.changed, password.expired, password.reset_requested
//   account.locked, account.unlocked
//   role.changed, permission.denied
//   export.denied, export.approved, export.requested
//   report.access_denied, sso.config.changed
//   security.policy.changed, device.credential.rotated, device.credential.revoked
// ─────────────────────────────────────────────────────────────────────────────

public sealed class SecurityEventService(Database db)
{
    public Task LogAsync(
        long     companyId,
        long?    userId,
        string   eventType,
        string   severity,
        string?  sourceIp,
        string?  userAgent,
        bool     success,
        string   safeMessage,
        object?  metadata    = null,
        CancellationToken ct = default)
    {
        var ipTrunc    = TruncateIp(sourceIp);
        var agentHash  = HashUserAgent(userAgent);
        var metaJson   = metadata is null ? null : JsonSerializer.Serialize(metadata);

        return db.ExecuteAsync(
            @"INSERT INTO security_events
                (company_id, user_id, event_type, severity,
                 source_ip_truncated, user_agent_hash, success, safe_message,
                 metadata_json, created_at)
              VALUES
                (@cid, @uid, @type, @sev,
                 @ip, @ua, @ok, @msg,
                 @meta, NOW())",
            c =>
            {
                c.Parameters.AddWithValue("@cid",  companyId);
                c.Parameters.AddWithValue("@uid",  (object?)userId ?? DBNull.Value);
                c.Parameters.AddWithValue("@type", eventType);
                c.Parameters.AddWithValue("@sev",  severity);
                c.Parameters.AddWithValue("@ip",   (object?)ipTrunc  ?? DBNull.Value);
                c.Parameters.AddWithValue("@ua",   (object?)agentHash ?? DBNull.Value);
                c.Parameters.AddWithValue("@ok",   success ? 1 : 0);
                c.Parameters.AddWithValue("@msg",  safeMessage[..Math.Min(safeMessage.Length, 500)]);
                c.Parameters.AddWithValue("@meta", (object?)metaJson ?? DBNull.Value);
            }, ct);
    }

    public Task<List<Dictionary<string, object?>>> GetRecentAsync(
        long companyId,
        int limit  = 200,
        int hours  = 72,
        string? eventType = null,
        CancellationToken ct = default)
    {
        var typeFilter = string.IsNullOrWhiteSpace(eventType)
            ? ""
            : "AND event_type = @type";

        return db.QueryAsync(
            $@"SELECT id, company_id, user_id, event_type, severity,
                      source_ip_truncated, user_agent_hash, success, safe_message,
                      metadata_json, created_at
               FROM security_events
               WHERE company_id = @cid
                 AND created_at >= NOW() - @h * INTERVAL '1 hour'
                 {typeFilter}
               ORDER BY created_at DESC
               LIMIT @lim",
            c =>
            {
                c.Parameters.AddWithValue("@cid",  companyId);
                c.Parameters.AddWithValue("@h",    hours);
                c.Parameters.AddWithValue("@lim",  Math.Min(limit, 500));
                if (!string.IsNullOrWhiteSpace(eventType))
                    c.Parameters.AddWithValue("@type", eventType);
            }, ct);
    }

    // Aggregated failure count for spike detection
    public async Task<int> CountRecentFailuresAsync(
        long companyId, int minutes = 60, CancellationToken ct = default)
    {
        var val = await db.ScalarLongAsync(
            @"SELECT COUNT(*) FROM security_events
              WHERE company_id = @cid
                AND success = 0
                AND created_at >= NOW() - @m * INTERVAL '1 minute'",
            c =>
            {
                c.Parameters.AddWithValue("@cid", companyId);
                c.Parameters.AddWithValue("@m",   minutes);
            }, ct);
        return (int)val;
    }

    // ── Sanitization helpers ──────────────────────────────────────────────────

    // IPv4: keep first 3 octets (e.g. 192.168.1.x → 192.168.1)
    // IPv6: keep first 48 bits (first 3 groups)
    // Non-routable / internal: keep but truncate
    internal static string? TruncateIp(string? ip)
    {
        if (string.IsNullOrWhiteSpace(ip)) return null;
        ip = ip.Trim();

        // IPv4
        var parts = ip.Split('.');
        if (parts.Length == 4)
            return $"{parts[0]}.{parts[1]}.{parts[2]}.x";

        // IPv6 — keep first 3 groups
        var v6parts = ip.Split(':');
        if (v6parts.Length >= 3)
            return $"{v6parts[0]}:{v6parts[1]}:{v6parts[2]}:…";

        // Fallback: keep first 16 chars
        return ip.Length > 16 ? ip[..16] + "…" : ip;
    }

    // First 8 hex chars of SHA-256(user_agent) — enough to correlate, not enough to reconstruct
    internal static string? HashUserAgent(string? ua)
    {
        if (string.IsNullOrWhiteSpace(ua)) return null;
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(ua));
        return Convert.ToHexString(hash)[..8].ToLowerInvariant();
    }
}
