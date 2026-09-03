using Opstrax.Api.Data;

namespace Opstrax.Api.Services;

// ─────────────────────────────────────────────────────────────────────────────
// OPERATIONAL ALERT DETECTORS
//
// The Settings → Notifications matrix promises five event families that no
// ingest path produces: HOS Violation, Maintenance Due, SLA Breach Risk,
// Fuel Anomaly, Excessive Idling. This service is their generator: each sweep
// derives the condition from the tables that already carry the data and inserts
// a telemetry_alerts row — from there the existing spine takes over
// (AlertNotificationBridgeService → in-app + outbox → email/SMS per user prefs,
// Alerts surfaces, safety pipeline untouched).
//
// Design rules, all inherited from the existing ingest detectors:
//   • telemetry_alerts INSERTs require the system identity (stage76 revokes
//     INSERT from opstrax_app) — the whole tick runs in RunInSystemScopeAsync.
//   • Dedupe is the atomic INSERT … SELECT … WHERE NOT EXISTS idiom
//     (trusted-gateway pattern), with a per-type re-alert window instead of an
//     open-status check so an acknowledged alert does not instantly re-fire.
//   • Thresholds/severities come from telemetry_rules where tunable
//     ('idling' minutes, 'fuel_drop_pct' percentage points).
//   • Time-bounded sources only: a fresh install with years of seed history
//     must not blast a backlog of emails on first boot.
//   • Sweeps are independent — one failing (e.g. a table missing on an old
//     deployment) must not stop the others.
// ─────────────────────────────────────────────────────────────────────────────
public sealed class OperationalAlertDetectionService(
    IServiceScopeFactory scopeFactory,
    ILogger<OperationalAlertDetectionService> logger,
    ServiceRunTracker tracker) : BackgroundService
{
    private static readonly TimeSpan Interval = TimeSpan.FromMinutes(5);
    private const string SvcName = "OperationalAlertDetectionService";

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Startup delay — schema ensures (telemetry_rules seeds included) run first.
        await Task.Delay(TimeSpan.FromSeconds(50), stoppingToken).ContinueWith(_ => { }, stoppingToken);
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
        var db = scope.ServiceProvider.GetRequiredService<Database>();

        await db.RunInSystemScopeAsync(async () =>
        {
            await SweepAsync(db, "maintenance_due", MaintenanceDueSql, ct);
            await SweepAsync(db, "sla_breach", SlaBreachSql, ct);
            await SweepAsync(db, "hos_violation", HosRecordsSql, ct);
            await SweepAsync(db, "hos_violation(clocks)", HosClocksSql, ct);
            await SweepAsync(db, "fuel_anomaly", FuelAnomalySql, ct);
            await SweepAsync(db, "idling", IdlingSql, ct);
        }, ct);
    }

    private async Task SweepAsync(Database db, string name, string sql, CancellationToken ct)
    {
        try
        {
            var inserted = await db.ExecuteAsync(sql, ct: ct);
            if (inserted > 0)
                logger.LogInformation("{Svc}: {Count} new {Type} alert(s)", SvcName, inserted, name);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "{Svc}: {Type} sweep failed; other sweeps continue", SvcName, name);
        }
    }

    // Maintenance items are already produced (and deduped per vehicle+service) by
    // MaintenanceBackgroundService.EvaluatePmRulesAsync; this alerts on them. One
    // alert per vehicle per 7 days: a persistent overdue item re-alerts weekly
    // rather than every tick, and manual acknowledgement is respected.
    private const string MaintenanceDueSql = """
        INSERT INTO telemetry_alerts (company_id, vehicle_id, alert_type, severity, message, status, source_channel)
        SELECT DISTINCT ON (mi.company_id, mi.vehicle_id)
               mi.company_id, mi.vehicle_id, 'maintenance_due',
               CASE WHEN mi.status='Overdue' THEN 'High' ELSE 'Warning' END,
               'Maintenance ' || LOWER(mi.status) || ': ' || COALESCE(NULLIF(mi.service_type,''), mi.title, 'scheduled service'),
               'Open', 'detector'
        FROM maintenance_items mi
        WHERE mi.deleted_at IS NULL
          AND mi.vehicle_id IS NOT NULL
          AND (mi.status = 'Overdue' OR (mi.status = 'Open' AND mi.category = 'Preventive Maintenance'))
          AND NOT EXISTS (SELECT 1 FROM telemetry_alerts ta
                          WHERE ta.company_id = mi.company_id AND ta.vehicle_id = mi.vehicle_id
                            AND ta.alert_type = 'maintenance_due'
                            AND ta.created_at > NOW() - INTERVAL '7 days')
        ORDER BY mi.company_id, mi.vehicle_id, mi.status DESC
        """;

    // Promise resolution copied from CustomerHealthService: sla_due_at → sla_window_end
    // → eta → scheduled_end. Alerts when the promise has passed, the live ETA blows
    // through it, or the job is already flagged at-risk — for promises inside the
    // last 48h only, so historic seed jobs never flood a fresh install.
    private const string SlaBreachSql = """
        WITH at_risk AS (
          SELECT j.id, j.company_id, j.assigned_vehicle_id,
                 COALESCE(j.sla_due_at, j.sla_window_end, j.eta, j.scheduled_end) AS promised_at,
                 (NOW() > COALESCE(j.sla_due_at, j.sla_window_end, j.eta, j.scheduled_end)) AS breached
          FROM jobs j
          WHERE j.deleted_at IS NULL
            AND COALESCE(j.sla_due_at, j.sla_window_end, j.eta, j.scheduled_end) IS NOT NULL
            AND COALESCE(j.sla_due_at, j.sla_window_end, j.eta, j.scheduled_end) > NOW() - INTERVAL '48 hours'
            AND j.status NOT IN ('Completed','Delivered','Cancelled','Failed')
            AND (
                  NOW() > COALESCE(j.sla_due_at, j.sla_window_end, j.eta, j.scheduled_end)
                  OR j.sla_status IN ('At Risk','Breached','Missed')
                  OR (j.eta IS NOT NULL AND j.sla_due_at IS NOT NULL AND j.eta > j.sla_due_at)
                )
        )
        INSERT INTO telemetry_alerts (company_id, vehicle_id, alert_type, severity, message, status, source_channel)
        SELECT a.company_id, a.assigned_vehicle_id, 'sla_breach',
               CASE WHEN a.breached THEN 'High' ELSE 'Warning' END,
               CASE WHEN a.breached THEN 'SLA breached — job #' ELSE 'SLA at risk — job #' END
                 || a.id || ' (promised ' || to_char(a.promised_at, 'YYYY-MM-DD HH24:MI') || ' UTC)',
               'Open', 'detector'
        FROM at_risk a
        WHERE NOT EXISTS (SELECT 1 FROM telemetry_alerts ta
                          WHERE ta.company_id = a.company_id AND ta.alert_type = 'sla_breach'
                            AND ta.message LIKE '%job #' || a.id || ' (%'
                            AND ta.created_at > NOW() - INTERVAL '24 hours')
        """;

    // Latest hos_records row per driver (today/yesterday): out of drive hours or an
    // explicit violation status. hos_records is the canonical store dispatch already
    // gates on; it fires as soon as real ELD data (or the seeder) populates it.
    private const string HosRecordsSql = """
        INSERT INTO telemetry_alerts (company_id, driver_id, alert_type, severity, message, status, source_channel)
        SELECT r.company_id, r.driver_id, 'hos_violation', 'High',
               'HOS violation — driver #' || r.driver_id || ': ' ||
               CASE WHEN COALESCE(r.remaining_drive_hours, 99) <= 0 THEN 'no drive hours remaining'
                    ELSE 'status ' || COALESCE(r.hos_status, 'violation') END,
               'Open', 'detector'
        FROM (
          SELECT DISTINCT ON (company_id, driver_id) company_id, driver_id, remaining_drive_hours, hos_status
          FROM hos_records
          WHERE company_id IS NOT NULL AND shift_date >= CURRENT_DATE - 1
          ORDER BY company_id, driver_id, shift_date DESC, id DESC
        ) r
        WHERE (COALESCE(r.remaining_drive_hours, 99) <= 0
               OR LOWER(COALESCE(r.hos_status,'')) IN ('violation','out_of_hours','out of hours'))
          AND NOT EXISTS (SELECT 1 FROM telemetry_alerts ta
                          WHERE ta.company_id = r.company_id AND ta.driver_id = r.driver_id
                            AND ta.alert_type = 'hos_violation'
                            AND ta.created_at > NOW() - INTERVAL '24 hours')
        """;

    // Companion source: hos_clocks carries an explicit status ('Violation'|'Warning'|'OK')
    // and remaining drive minutes. Only clocks touched in the last 24h count as live.
    private const string HosClocksSql = """
        INSERT INTO telemetry_alerts (company_id, driver_id, alert_type, severity, message, status, source_channel)
        SELECT c.company_id, c.driver_id, 'hos_violation', 'High',
               'HOS violation — driver #' || c.driver_id || ': ' ||
               CASE WHEN c.status = 'Violation' THEN 'clock in violation' ELSE 'no drive time remaining' END,
               'Open', 'detector'
        FROM hos_clocks c
        WHERE c.company_id IS NOT NULL AND c.driver_id IS NOT NULL
          AND c.updated_at > NOW() - INTERVAL '24 hours'
          AND (c.status = 'Violation' OR c.drive_time_remaining_minutes <= 0)
          AND NOT EXISTS (SELECT 1 FROM telemetry_alerts ta
                          WHERE ta.company_id = c.company_id AND ta.driver_id = c.driver_id
                            AND ta.alert_type = 'hos_violation'
                            AND ta.created_at > NOW() - INTERVAL '24 hours')
        """;

    // Fuel-level drop across the last 45 minutes of live telemetry. max→last is
    // refuel-safe (a refill raises the level, producing a zero/negative drop);
    // threshold in percentage points via the 'fuel_drop_pct' rule (default 20).
    private const string FuelAnomalySql = """
        WITH win AS (
          SELECT le.company_id, le.vehicle_id,
                 MAX(le.fuel_level) AS max_level,
                 (ARRAY_AGG(le.fuel_level ORDER BY le.event_time DESC))[1] AS last_level,
                 COUNT(*) AS fixes
          FROM location_events le
          WHERE le.event_time > NOW() - INTERVAL '45 minutes'
            AND le.fuel_level IS NOT NULL AND le.vehicle_id IS NOT NULL
          GROUP BY le.company_id, le.vehicle_id
        )
        INSERT INTO telemetry_alerts (company_id, vehicle_id, alert_type, severity, message, status, source_channel)
        SELECT w.company_id, w.vehicle_id, 'fuel_anomaly',
               COALESCE((SELECT tr.severity FROM telemetry_rules tr
                         WHERE tr.company_id = w.company_id AND tr.rule_type = 'fuel_drop_pct' AND tr.enabled = TRUE LIMIT 1), 'High'),
               'Fuel anomaly: level dropped ' || ROUND(w.max_level - w.last_level)::int || '% within 45 minutes',
               'Open', 'detector'
        FROM win w
        WHERE w.fixes >= 3
          AND w.max_level - w.last_level >=
              COALESCE((SELECT tr.threshold_value FROM telemetry_rules tr
                        WHERE tr.company_id = w.company_id AND tr.rule_type = 'fuel_drop_pct' AND tr.enabled = TRUE LIMIT 1), 20)
          AND NOT EXISTS (SELECT 1 FROM telemetry_alerts ta
                          WHERE ta.company_id = w.company_id AND ta.vehicle_id = w.vehicle_id
                            AND ta.alert_type = 'fuel_anomaly'
                            AND ta.created_at > NOW() - INTERVAL '6 hours')
        """;

    // A candidate idling window needs explicit low speed AND affirmative engine
    // evidence at every observed sample. NULLs must break evidence coverage, not
    // be removed by a WHERE predicate or ignored by MAX/BOOL_AND. Legacy Running
    // defaults are not engine-on proof. Sparse samples do not prove continuity:
    // the message reports a possible condition, not continuous idling duration.
    private const string IdlingSql = """
        WITH win AS (
          SELECT le.company_id, le.vehicle_id,
                 COALESCE(MAX(t.threshold_value), 15) AS threshold_minutes,
                 COUNT(*) AS fixes,
                 COUNT(CASE WHEN le.speed_mph >= 0 AND le.speed_mph < 1
                   AND LOWER(TRIM(COALESCE(le.engine_status,''))) IN ('on','idle') THEN 1 END) AS affirmative_fixes,
                 MAX(le.speed_mph) AS max_speed,
                 MIN(le.event_time) AS first_seen,
                 MAX(le.event_time) AS last_seen,
                 (ARRAY_AGG(le.engine_status ORDER BY le.event_time DESC))[1] AS last_engine
          FROM location_events le
          LEFT JOIN telemetry_rules t
            ON t.company_id = le.company_id AND t.rule_type = 'idling' AND t.enabled = TRUE
          WHERE le.vehicle_id IS NOT NULL
            AND le.event_time > NOW() - (COALESCE(t.threshold_value, 15) * INTERVAL '1 minute')
          GROUP BY le.company_id, le.vehicle_id
        )
        INSERT INTO telemetry_alerts (company_id, vehicle_id, alert_type, severity, message, status, source_channel)
        SELECT w.company_id, w.vehicle_id, 'idling',
               COALESCE((SELECT tr.severity FROM telemetry_rules tr
                         WHERE tr.company_id = w.company_id AND tr.rule_type = 'idling' AND tr.enabled = TRUE LIMIT 1), 'Warning'),
               'Possible idling: low-speed engine-on samples observed over ' ||
               GREATEST(1, ROUND(EXTRACT(EPOCH FROM (w.last_seen - w.first_seen)) / 60))::int || ' min; continuous idling not established',
               'Open', 'detector'
        FROM win w
        WHERE w.fixes >= 3
          AND w.affirmative_fixes = w.fixes
          AND w.max_speed < 1
          AND w.last_seen > NOW() - INTERVAL '5 minutes'
          AND EXTRACT(EPOCH FROM (w.last_seen - w.first_seen)) / 60 >= w.threshold_minutes * 0.8
          AND NOT EXISTS (SELECT 1 FROM telemetry_alerts ta
                          WHERE ta.company_id = w.company_id AND ta.vehicle_id = w.vehicle_id
                            AND ta.alert_type = 'idling'
                            AND ta.created_at > NOW() - INTERVAL '2 hours')
        """;
}
