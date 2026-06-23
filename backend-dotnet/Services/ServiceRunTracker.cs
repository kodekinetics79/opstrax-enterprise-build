using System.Text.RegularExpressions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Opstrax.Api.Data;

namespace Opstrax.Api.Services;

// ─────────────────────────────────────────────────────────────────────────────
// ServiceRunTracker — Singleton
//
// All background hosted services call BeginAsync/CompleteAsync/FailAsync to
// record every execution cycle into service_run_history and maintain the
// service_heartbeats summary row.
//
// SECURITY
// ────────
// SanitizeError() removes connection strings, JWT secrets, IP addresses, and
// other credential-shaped strings before the message is persisted.
// Raw stack traces are never stored — only the sanitized top-level message.
// ─────────────────────────────────────────────────────────────────────────────

public sealed class ServiceRunTracker(
    IServiceScopeFactory scopeFactory,
    ILogger<ServiceRunTracker> logger)
{
    // Repeated consecutive failures beyond this threshold auto-create a platform incident.
    internal const int IncidentThreshold = 3;

    // ── Public API ────────────────────────────────────────────────────────────

    /// <summary>Call at the start of each service cycle. Returns a run ID to pass to
    /// CompleteAsync or FailAsync.</summary>
    public async Task<long> BeginAsync(string serviceName, CancellationToken ct)
    {
        try
        {
            using var scope = scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<Database>();
            return await db.ScalarLongAsync(
                @"INSERT INTO service_run_history (service_name, status, started_at, heartbeat_at)
                  VALUES (@sn, 'running', NOW(), NOW());
                  SELECT LAST_INSERT_ID()",
                c => c.Parameters.AddWithValue("@sn", serviceName), ct);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "[Tracker] BeginAsync failed for {Service}", serviceName);
            return -1;
        }
    }

    /// <summary>Update the heartbeat timestamp mid-cycle (call every ~60 s in long-running cycles).</summary>
    public async Task HeartbeatAsync(string serviceName, long runId, CancellationToken ct)
    {
        if (runId <= 0) return;
        try
        {
            using var scope = scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<Database>();
            await db.ExecuteAsync(
                "UPDATE service_run_history SET heartbeat_at = NOW() WHERE id = @id",
                c => c.Parameters.AddWithValue("@id", runId), ct);
        }
        catch { /* non-critical — tracker must not crash the service */ }
    }

    /// <summary>Record a successful cycle completion.</summary>
    public async Task CompleteAsync(
        long runId, string serviceName, int processedCount, int durationMs,
        CancellationToken ct)
    {
        try
        {
            using var scope = scopeFactory.CreateScope();
            var db        = scope.ServiceProvider.GetRequiredService<Database>();

            if (runId > 0)
                await db.ExecuteAsync(
                    @"UPDATE service_run_history
                      SET status='succeeded', finished_at=NOW(),
                          duration_ms=@ms, processed_count=@pc, heartbeat_at=NOW()
                      WHERE id = @id",
                    c =>
                    {
                        c.Parameters.AddWithValue("@ms", durationMs);
                        c.Parameters.AddWithValue("@pc", processedCount);
                        c.Parameters.AddWithValue("@id", runId);
                    }, ct);

            // Reset consecutive failure count on success
            await db.ExecuteAsync(
                @"INSERT INTO service_heartbeats
                    (service_name, last_heartbeat_at, last_run_at, last_run_status,
                     consecutive_failures, last_error_safe, updated_at)
                  VALUES (@sn, NOW(), NOW(), 'succeeded', 0, NULL, NOW())
                  ON DUPLICATE KEY UPDATE
                    last_heartbeat_at    = NOW(),
                    last_run_at          = NOW(),
                    last_run_status      = 'succeeded',
                    consecutive_failures = 0,
                    last_error_safe      = NULL,
                    updated_at           = NOW()",
                c => c.Parameters.AddWithValue("@sn", serviceName), ct);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "[Tracker] CompleteAsync failed for {Service}", serviceName);
        }
    }

    /// <summary>Record a cycle failure. Auto-creates a platform incident after 3+ consecutive failures.</summary>
    public async Task FailAsync(
        long runId, string serviceName, Exception ex, int durationMs,
        CancellationToken ct)
    {
        var safeMsg  = SanitizeError(ex.Message);
        var errCode  = ex.GetType().Name;

        try
        {
            using var scope    = scopeFactory.CreateScope();
            var db             = scope.ServiceProvider.GetRequiredService<Database>();
            var incidentSvc    = scope.ServiceProvider.GetRequiredService<IncidentService>();

            if (runId > 0)
                await db.ExecuteAsync(
                    @"UPDATE service_run_history
                      SET status='failed', finished_at=NOW(),
                          duration_ms=@ms, error_code=@ec,
                          error_message_safe=@em, heartbeat_at=NOW()
                      WHERE id = @id",
                    c =>
                    {
                        c.Parameters.AddWithValue("@ms", durationMs);
                        c.Parameters.AddWithValue("@ec", errCode);
                        c.Parameters.AddWithValue("@em", safeMsg);
                        c.Parameters.AddWithValue("@id", runId);
                    }, ct);

            // Increment consecutive failures
            await db.ExecuteAsync(
                @"INSERT INTO service_heartbeats
                    (service_name, last_heartbeat_at, last_run_at, last_run_status,
                     consecutive_failures, last_error_safe, updated_at)
                  VALUES (@sn, NOW(), NOW(), 'failed', 1, @em, NOW())
                  ON DUPLICATE KEY UPDATE
                    last_heartbeat_at    = NOW(),
                    last_run_at          = NOW(),
                    last_run_status      = 'failed',
                    consecutive_failures = consecutive_failures + 1,
                    last_error_safe      = @em,
                    updated_at           = NOW()",
                c =>
                {
                    c.Parameters.AddWithValue("@sn", serviceName);
                    c.Parameters.AddWithValue("@em", safeMsg);
                }, ct);

            // Read current consecutive failure count to decide on incident creation
            var consecutiveFails = await db.ScalarLongAsync(
                "SELECT consecutive_failures FROM service_heartbeats WHERE service_name=@sn",
                c => c.Parameters.AddWithValue("@sn", serviceName), ct);

            if (consecutiveFails >= IncidentThreshold)
            {
                await incidentSvc.CreateIfNotExistsAsync(
                    severity:        consecutiveFails >= 10 ? "critical" : "high",
                    sourceService:   serviceName,
                    sourceEvent:     "repeated_failure",
                    title:           $"{serviceName}: {consecutiveFails} consecutive failures",
                    safeDescription: $"The service has failed {consecutiveFails} times consecutively. Last error: {safeMsg}",
                    ct:              ct);
            }
        }
        catch (Exception trackEx)
        {
            logger.LogWarning(trackEx, "[Tracker] FailAsync record failed for {Service}", serviceName);
        }
    }

    // ── Error sanitization ────────────────────────────────────────────────────
    // Never store raw exceptions. Remove credential-shaped values before persistence.

    internal static string SanitizeError(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return "Unknown error";

        var s = raw;

        // Remove connection string password values  (Password=xxx; or pwd=xxx;)
        s = Regex.Replace(s, @"(?i)(password|pwd|secret|key|token|credential|auth)\s*=\s*[^\s;,'""\]]+", "$1=[redacted]");

        // Remove Bearer/Basic token payloads
        s = Regex.Replace(s, @"(?i)(Bearer|Basic)\s+[A-Za-z0-9+/=._-]{10,}", "$1 [redacted]");

        // Remove dotted IP:port combinations  (192.168.x.x:3306 etc.)
        s = Regex.Replace(s, @"\b\d{1,3}\.\d{1,3}\.\d{1,3}\.\d{1,3}(:\d+)?\b", "[ip:redacted]");

        // Truncate to 2000 chars — full stack traces must go to structured logger only
        if (s.Length > 2000) s = s[..2000] + " … (truncated)";

        return s;
    }
}
