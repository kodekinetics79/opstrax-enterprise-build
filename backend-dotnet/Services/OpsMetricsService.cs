using Opstrax.Api.Data;

namespace Opstrax.Api.Services;

// ─────────────────────────────────────────────────────────────────────────────
// OpsMetricsService — Scoped
//
// Aggregates platform-wide operational metrics from multiple tables.
// All queries are read-only and NEVER expose tenant customer/driver private data.
// Metrics are counts and statuses only — no PII, no raw records.
// ─────────────────────────────────────────────────────────────────────────────

public sealed class OpsMetricsService(Database db)
{
    public async Task<OpsMetricsSnapshot> GetSnapshotAsync(CancellationToken ct = default)
    {
        // Run all metric queries in parallel
        var telemetryTask     = GetTelemetryMetricsAsync(ct);
        var alertsTask        = GetAlertMetricsAsync(ct);
        var safetyTask        = GetSafetyMetricsAsync(ct);
        var dispatchTask      = GetDispatchMetricsAsync(ct);
        var notifTask         = GetNotificationMetricsAsync(ct);
        var reportsTask       = GetReportMetricsAsync(ct);
        var servicesTask      = GetServiceStatusAsync(ct);
        var incidentTask      = GetIncidentSummaryAsync(ct);
        var dbTask            = GetDbStatusAsync(ct);

        await Task.WhenAll(telemetryTask, alertsTask, safetyTask, dispatchTask,
            notifTask, reportsTask, servicesTask, incidentTask, dbTask);

        return new OpsMetricsSnapshot(
            Telemetry:     await telemetryTask,
            Alerts:        await alertsTask,
            Safety:        await safetyTask,
            Dispatch:      await dispatchTask,
            Notifications: await notifTask,
            Reports:       await reportsTask,
            Services:      await servicesTask,
            Incidents:     await incidentTask,
            Database:      await dbTask,
            CapturedUtc:   DateTime.UtcNow);
    }

    // ── Telemetry ─────────────────────────────────────────────────────────────

    private async Task<TelemetryMetrics> GetTelemetryMetricsAsync(CancellationToken ct)
    {
        var rows = await db.QueryAsync(
            @"SELECT
                COUNT(*) AS total,
                SUM(validation_status='accepted')    AS accepted,
                SUM(validation_status='rejected')    AS rejected,
                SUM(validation_status='auth_failed') AS auth_failed,
                SUM(is_replay=1)                     AS replay_detected
              FROM telemetry_events
              WHERE received_at >= DATE_SUB(NOW(), INTERVAL 24 HOUR)", ct: ct);

        if (rows.Count == 0) return new(0, 0, 0, 0, 0);
        var r = rows[0];
        return new(
            Total:          L(r, "total"),
            Accepted:       L(r, "accepted"),
            Rejected:       L(r, "rejected"),
            AuthFailed:     L(r, "authFailed"),
            ReplayDetected: L(r, "replayDetected"));
    }

    // ── Alerts ────────────────────────────────────────────────────────────────

    private async Task<AlertMetrics> GetAlertMetricsAsync(CancellationToken ct)
    {
        var rows = await db.QueryAsync(
            @"SELECT
                COUNT(*)                                         AS total_24h,
                SUM(status='open' OR status='Open')             AS open_count,
                SUM(severity='critical' OR severity='Critical') AS critical_count
              FROM telemetry_alerts
              WHERE created_at >= DATE_SUB(NOW(), INTERVAL 24 HOUR)", ct: ct);

        if (rows.Count == 0) return new(0, 0, 0);
        var r = rows[0];
        return new(L(r, "total24h"), L(r, "openCount"), L(r, "criticalCount"));
    }

    // ── Safety ────────────────────────────────────────────────────────────────

    private async Task<SafetyMetrics> GetSafetyMetricsAsync(CancellationToken ct)
    {
        var rows = await db.QueryAsync(
            @"SELECT
                COUNT(*) AS generated_24h,
                SUM(review_status NOT IN ('Closed','Dismissed')) AS open_review
              FROM safety_events
              WHERE event_time >= DATE_SUB(NOW(), INTERVAL 24 HOUR)", ct: ct);

        if (rows.Count == 0) return new(0, 0);
        var r = rows[0];
        return new(L(r, "generated24h"), L(r, "openReview"));
    }

    // ── Dispatch ──────────────────────────────────────────────────────────────

    private async Task<DispatchMetrics> GetDispatchMetricsAsync(CancellationToken ct)
    {
        var rows = await db.QueryAsync(
            @"SELECT
                SUM(assignment_status NOT IN ('Completed','Cancelled','Failed')) AS active,
                SUM(exception_count > 0) AS has_exceptions
              FROM dispatch_assignments
              WHERE created_at >= DATE_SUB(NOW(), INTERVAL 7 DAY)", ct: ct);

        var excRows = await db.QueryAsync(
            @"SELECT COUNT(*) AS open_exceptions
              FROM dispatch_exceptions
              WHERE status NOT IN ('resolved','Resolved','closed','Closed')", ct: ct);

        if (rows.Count == 0) return new(0, 0, 0);
        var r = rows[0];
        return new(L(r, "active"), L(r, "hasExceptions"), excRows.Count > 0 ? L(excRows[0], "openExceptions") : 0);
    }

    // ── Notifications ─────────────────────────────────────────────────────────

    private async Task<NotificationMetrics> GetNotificationMetricsAsync(CancellationToken ct)
    {
        var rows = await db.QueryAsync(
            @"SELECT
                SUM(status IN ('pending','sent'))           AS pending,
                SUM(status='failed')                        AS failed,
                SUM(status IN ('read','acknowledged')
                    AND created_at >= DATE_SUB(NOW(), INTERVAL 24 HOUR)) AS acked_24h,
                SUM(delivery_status='not_configured')       AS not_configured
              FROM notifications", ct: ct);

        if (rows.Count == 0) return new(0, 0, 0, 0);
        var r = rows[0];
        return new(L(r, "pending"), L(r, "failed"), L(r, "acked24h"), L(r, "notConfigured"));
    }

    // ── Scheduled Reports ─────────────────────────────────────────────────────

    private async Task<ReportMetrics> GetReportMetricsAsync(CancellationToken ct)
    {
        var rows = await db.QueryAsync(
            @"SELECT
                SUM(last_status='completed') AS succeeded,
                SUM(last_status='failed')    AS failed,
                SUM(status='Active')         AS active_schedules
              FROM scheduled_reports", ct: ct);

        var logRows = await db.QueryAsync(
            @"SELECT COUNT(*) AS runs_24h
              FROM report_execution_log
              WHERE executed_at >= DATE_SUB(NOW(), INTERVAL 24 HOUR)", ct: ct);

        if (rows.Count == 0) return new(0, 0, 0, 0);
        var r = rows[0];
        return new(L(r, "succeeded"), L(r, "failed"), L(r, "activeSchedules"),
            logRows.Count > 0 ? L(logRows[0], "runs24h") : 0);
    }

    // ── Background Services ───────────────────────────────────────────────────

    private async Task<List<ServiceStatusEntry>> GetServiceStatusAsync(CancellationToken ct)
    {
        var rows = await db.QueryAsync(
            @"SELECT service_name, last_heartbeat_at, last_run_at, last_run_status,
                     consecutive_failures, last_error_safe
              FROM service_heartbeats
              ORDER BY service_name", ct: ct);

        return rows.Select(r => new ServiceStatusEntry(
            ServiceName:          r["serviceName"]?.ToString() ?? "",
            LastHeartbeatUtc:     r["lastHeartbeatAt"] as DateTime?,
            LastRunUtc:           r["lastRunAt"] as DateTime?,
            LastRunStatus:        r["lastRunStatus"]?.ToString(),
            ConsecutiveFailures:  (int)Math.Min(L(r, "consecutiveFailures"), int.MaxValue),
            LastErrorSafe:        r["lastErrorSafe"]?.ToString()
        )).ToList();
    }

    // ── Incidents ─────────────────────────────────────────────────────────────

    private async Task<IncidentSummary> GetIncidentSummaryAsync(CancellationToken ct)
    {
        var rows = await db.QueryAsync(
            @"SELECT
                SUM(status IN ('open','investigating')) AS open_count,
                SUM(severity='critical' AND status IN ('open','investigating')) AS critical_open
              FROM platform_incidents", ct: ct);

        if (rows.Count == 0) return new(0, 0);
        var r = rows[0];
        return new(L(r, "openCount"), L(r, "criticalOpen"));
    }

    // ── Database ──────────────────────────────────────────────────────────────

    private async Task<DbStatus> GetDbStatusAsync(CancellationToken ct)
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();
        try
        {
            await db.ScalarLongAsync("SELECT 1", ct: ct);
            sw.Stop();
            return new(Connected: true, LatencyMs: (int)sw.ElapsedMilliseconds);
        }
        catch
        {
            return new(Connected: false, LatencyMs: -1);
        }
    }

    // ── Helper ────────────────────────────────────────────────────────────────

    private static long L(Dictionary<string, object?> r, string key) =>
        r.TryGetValue(key, out var v) && v is not null and not DBNull
            ? Convert.ToInt64(v) : 0;
}

// ── DTOs (pure data, no PII) ──────────────────────────────────────────────────

public sealed record OpsMetricsSnapshot(
    TelemetryMetrics     Telemetry,
    AlertMetrics         Alerts,
    SafetyMetrics        Safety,
    DispatchMetrics      Dispatch,
    NotificationMetrics  Notifications,
    ReportMetrics        Reports,
    List<ServiceStatusEntry> Services,
    IncidentSummary      Incidents,
    DbStatus             Database,
    DateTime             CapturedUtc);

public sealed record TelemetryMetrics(
    long Total, long Accepted, long Rejected, long AuthFailed, long ReplayDetected);

public sealed record AlertMetrics(
    long Total24h, long OpenCount, long CriticalCount);

public sealed record SafetyMetrics(
    long Generated24h, long OpenReview);

public sealed record DispatchMetrics(
    long Active, long WithExceptions, long OpenExceptions);

public sealed record NotificationMetrics(
    long Pending, long Failed, long Acknowledged24h, long NotConfigured);

public sealed record ReportMetrics(
    long Succeeded, long Failed, long ActiveSchedules, long Runs24h);

public sealed record ServiceStatusEntry(
    string  ServiceName,
    DateTime? LastHeartbeatUtc,
    DateTime? LastRunUtc,
    string? LastRunStatus,
    int     ConsecutiveFailures,
    string? LastErrorSafe);

public sealed record IncidentSummary(long OpenCount, long CriticalOpen);

public sealed record DbStatus(bool Connected, int LatencyMs);
