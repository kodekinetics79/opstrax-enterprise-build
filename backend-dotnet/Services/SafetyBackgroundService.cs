using System.Globalization;
using System.Text.Json;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Npgsql;
using NpgsqlTypes;
using Opstrax.Api.Data;
using Opstrax.Api.Foundation;

namespace Opstrax.Api.Services;

// Runs every 5 minutes.
// 1. Converts unprocessed telemetry_alerts → safety_events (with duplicate prevention via UNIQUE KEY).
// 2. Detects repeated-speeding patterns and upgrades severity.
// 3. Recomputes driver_safety_scores for all drivers with recent events.
public sealed class SafetyBackgroundService(
    IServiceScopeFactory scopeFactory,
    ILogger<SafetyBackgroundService> logger,
    ServiceRunTracker tracker) : BackgroundService
{
    private static readonly TimeSpan Interval = TimeSpan.FromMinutes(5);
    private const string SvcName = "SafetyBackgroundService";

    // Severity → default score impact (deducted from 100)
    private static readonly Dictionary<string, decimal> SeverityWeights = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Critical"] = 25m,
        ["High"]     = 15m,
        ["Medium"]   = 8m,
        ["Warning"]  = 5m,
        ["Low"]      = 3m,
    };

    // Map the raw telemetry alert_type to the safety-dashboard event_type vocabulary (SafetySummary sums
    // Title-Case tokens like 'Harsh Braking'/'Speeding'). Without this the harsh tiles stayed empty and
    // even speeding didn't match. Unknown types pass through unchanged.
    private static string MapEventType(string alertType) => alertType.ToLowerInvariant() switch
    {
        "harsh_braking" => "Harsh Braking",
        "harsh_acceleration" => "Harsh Acceleration",
        "harsh_turn" or "harsh_cornering" => "Harsh Cornering",
        "crash" => "Crash",
        "sos" => "SOS",
        "speeding" => "Speeding",
        "geofence_breach" => "Geofence Breach",
        "stale_device" => "Stale Device",
        _ => alertType,
    };

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Startup delay — schema migrations and telemetry service must complete first.
        await Task.Delay(TimeSpan.FromSeconds(45), stoppingToken).ContinueWith(_ => { }, stoppingToken);

        while (!stoppingToken.IsCancellationRequested)
        {
            var sw    = System.Diagnostics.Stopwatch.StartNew();
            var runId = await tracker.BeginAsync(SvcName, stoppingToken);
            try
            {
                // Cross-tenant worker (all-company telemetry/safety, filtered by company_id):
                // run the whole tick under the platform-admin bypass scope.
                using (var tickScope = scopeFactory.CreateScope())
                {
                    var tickDb = tickScope.ServiceProvider.GetRequiredService<Database>();
                    await tickDb.RunInSystemScopeAsync(async () =>
                    {
                        await ProcessTelemetryAlertsAsync(stoppingToken);
                        await DetectRepeatedSpeedingAsync(stoppingToken);
                        await RecomputeDriverScoresAsync(stoppingToken);
                        await RefreshFleetHealthSnapshotsAsync(stoppingToken);
                    }, stoppingToken);
                }
                sw.Stop();
                await tracker.CompleteAsync(runId, SvcName, 0, (int)sw.ElapsedMilliseconds, stoppingToken);
            }
            catch (OperationCanceledException) { break; }
            catch (Exception ex)
            {
                sw.Stop();
                logger.LogError(ex, "{Svc} tick failed", SvcName);
                await tracker.FailAsync(runId, SvcName, ex, (int)sw.ElapsedMilliseconds, stoppingToken);
            }

            try { await Task.Delay(Interval, stoppingToken); }
            catch (OperationCanceledException) { break; }
        }
    }

    // Converts new telemetry_alerts into safety_events.
    // UNIQUE KEY uq_se_telemetry_alert prevents double-creation.
    private async Task ProcessTelemetryAlertsAsync(CancellationToken ct)
    {
        using var scope = scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<Database>();

        // Fetch unprocessed alerts (no corresponding safety_event yet)
        var alerts = await db.QueryAsync(
            @"SELECT ta.id, ta.company_id, ta.vehicle_id, ta.device_id, ta.driver_id,
                     ta.alert_type, ta.severity, ta.message, ta.source_event_id, ta.created_at,
                     le.lat, le.lng, le.speed_mph
              FROM telemetry_alerts ta
              LEFT JOIN location_events le ON le.id = ta.source_event_id
              LEFT JOIN safety_events se ON se.source_telemetry_alert_id = ta.id
              WHERE se.id IS NULL
                AND ta.alert_type IN ('speeding','geofence_breach','stale_device',
                                      'harsh_braking','harsh_acceleration','harsh_turn','harsh_cornering','crash','sos')
              ORDER BY ta.created_at
              LIMIT 200",
            ct: ct);

        foreach (var alert in alerts)
        {
            var alertId   = Convert.ToInt64(alert["id"]);
            var companyId = Convert.ToInt64(alert["companyId"]);
            var severity  = alert["severity"]?.ToString() ?? "High";
            var alertType = alert["alertType"]?.ToString() ?? "speeding";
            var vehicleId = alert["vehicleId"] is { } v && v is not DBNull ? (long?)Convert.ToInt64(v) : null;
            var deviceId  = alert["deviceId"]  is { } d && d is not DBNull ? (long?)Convert.ToInt64(d) : null;
            var driverId  = alert["driverId"]  is { } dr && dr is not DBNull ? (long?)Convert.ToInt64(dr) : null;
            var eventSrcId = alert["sourceEventId"] is { } s && s is not DBNull ? (long?)Convert.ToInt64(s) : null;

            // Fetch tenant score weight for this event type
            var weightRuleType = $"safety_weight_{alertType.Replace("-", "_")}";
            var scoreImpact = await db.ScalarDecimalAsync(
                "SELECT threshold_value FROM telemetry_rules WHERE company_id=@cid AND rule_type=@rt AND enabled=TRUE LIMIT 1",
                c => { c.Parameters.AddWithValue("@cid", companyId); c.Parameters.AddWithValue("@rt", weightRuleType); }, ct)
                ?? SeverityWeights.GetValueOrDefault(severity, 10m);

            // Build evidence hash from source event location + speed
            var lat    = alert["lat"] is { } la && la is not DBNull ? Convert.ToString(la) : "0";
            var lng    = alert["lng"] is { } lo && lo is not DBNull ? Convert.ToString(lo) : "0";
            var speed  = alert["speedMph"] is { } sp && sp is not DBNull ? Convert.ToString(sp) : "0";
            var evHash = TelemetryHmacHelper.Sha256Hex($"{alertId}:{lat}:{lng}:{speed}");

            // Build meta_json
            var meta = $"{{\"source\":\"telemetry_alert\",\"alertId\":{alertId},\"alertType\":\"{alertType}\",\"severity\":\"{severity}\"}}";

                try
                {
                    await db.ExecuteAsync(
                    @"INSERT INTO safety_events
                        (company_id, driver_id, vehicle_id, device_id,
                         source_telemetry_alert_id, source_location_event_id,
                         event_type, severity, score_impact, status,
                         event_time, evidence_hash, meta_json)
                      VALUES
                        (@cid, @did, @vid, @devId,
                         @alertId, @srcId,
                         @evType, @sev, @impact, 'open',
                         @evTime, @hash, @meta::jsonb)",
                    c =>
                    {
                        c.Parameters.AddWithValue("@cid",    companyId);
                        c.Parameters.AddWithValue("@did",    driverId  ?? (object)DBNull.Value);
                        c.Parameters.AddWithValue("@vid",    vehicleId ?? (object)DBNull.Value);
                        c.Parameters.AddWithValue("@devId",  deviceId  ?? (object)DBNull.Value);
                        c.Parameters.AddWithValue("@alertId", alertId);
                        c.Parameters.AddWithValue("@srcId",  eventSrcId ?? (object)DBNull.Value);
                        c.Parameters.AddWithValue("@evType", MapEventType(alertType));
                        c.Parameters.AddWithValue("@sev",    severity);
                        c.Parameters.AddWithValue("@impact", scoreImpact);
                        c.Parameters.AddWithValue("@evTime", alert["createdAt"] ?? (object)DBNull.Value);
                        c.Parameters.AddWithValue("@hash",   evHash);
                        c.Parameters.Add(new NpgsqlParameter("@meta", NpgsqlTypes.NpgsqlDbType.Jsonb) { Value = meta });
                    }, ct);

                logger.LogDebug("Safety event created from telemetry_alert {AlertId}", alertId);

                // Best-effort AI enrichment. The safety_event is already committed above; a recommendation
                // failure (AI service unavailable, ai_recommendations write error) must NOT abort conversion
                // of the remaining alerts in this batch — otherwise one bad alert stalls safety scoring for
                // every tenant until the next tick.
                try
                {
                    await CreateSafetyRecommendationAsync(companyId, alertId, alertType, severity, driverId, vehicleId, scoreImpact, ct);
                }
                catch (Exception recEx)
                {
                    logger.LogWarning(recEx, "Safety recommendation enrichment failed for telemetry_alert {AlertId}; safety_event was still recorded", alertId);
                }
            }
            catch (Npgsql.PostgresException ex) when (ex.SqlState == "23505")
            {
                // UNIQUE KEY on source_telemetry_alert_id — already processed
            }
        }
    }

    // Detect repeated speeding: driver with >= threshold speeding events in 24h.
    // Creates a 'repeated_speeding' safety event linked to no single alert.
    private async Task DetectRepeatedSpeedingAsync(CancellationToken ct)
    {
        using var scope = scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<Database>();

        // Find driver+company combos that have recent speeding events at or above threshold
        var repeats = await db.QueryAsync(
            @"SELECT se.company_id, se.driver_id,
                     COUNT(*) event_count,
                     COALESCE(tr.threshold_value, 3) repeat_threshold
              FROM safety_events se
              LEFT JOIN telemetry_rules tr
                ON tr.company_id=se.company_id AND tr.rule_type='safety_repeated_speeding_threshold' AND tr.enabled=TRUE
              WHERE se.event_type='speeding'
                AND se.status != 'dismissed'
                AND se.driver_id IS NOT NULL
                AND se.event_time > NOW() - 24 * INTERVAL '1 hour'
              GROUP BY se.company_id, se.driver_id, repeat_threshold
              HAVING COUNT(*) >= COALESCE(tr.threshold_value, 3)",
            ct: ct);

        foreach (var row in repeats)
        {
            var companyId = Convert.ToInt64(row["companyId"]);
            var driverId  = Convert.ToInt64(row["driverId"]);
            var count     = Convert.ToInt64(row["eventCount"]);

            // Only create one open repeated_speeding event per driver per day
            var existing = await db.ScalarLongAsync(
                "SELECT COUNT(*) FROM safety_events WHERE company_id=@cid AND driver_id=@did AND event_type='repeated_speeding' AND status='open' AND event_time > NOW() - 24 * INTERVAL '1 hour'",
                c => { c.Parameters.AddWithValue("@cid", companyId); c.Parameters.AddWithValue("@did", driverId); }, ct);

            if (existing > 0) continue;

            var weight = await db.ScalarDecimalAsync(
                "SELECT threshold_value FROM telemetry_rules WHERE company_id=@cid AND rule_type='safety_weight_repeated_speeding' AND enabled=TRUE LIMIT 1",
                c => c.Parameters.AddWithValue("@cid", companyId), ct) ?? 25m;

            await db.ExecuteAsync(
                @"INSERT INTO safety_events
                    (company_id, driver_id, event_type, severity, score_impact, status, meta_json)
                  VALUES
                    (@cid, @did, 'repeated_speeding', 'Critical', @impact, 'open',
                     jsonb_build_object('count', @count, 'window_hours', 24))",
                c =>
                {
                    c.Parameters.AddWithValue("@cid",    companyId);
                    c.Parameters.AddWithValue("@did",    driverId);
                    c.Parameters.AddWithValue("@impact", weight);
                    c.Parameters.AddWithValue("@count",  count);
                }, ct);

            logger.LogInformation("Repeated speeding event created: company={CompanyId} driver={DriverId} count={Count}", companyId, driverId, count);
            await CreateRepeatedSpeedingRecommendationAsync(companyId, driverId, count, weight, ct);
        }
    }

    // Recomputes driver_safety_scores for all drivers with safety events in the last 90 days.
    private async Task RecomputeDriverScoresAsync(CancellationToken ct)
    {
        using var scope = scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<Database>();

        // Find all company+driver pairs with events in the last 90 days
        var drivers = await db.QueryAsync(
            @"SELECT DISTINCT company_id, driver_id
              FROM safety_events
              WHERE driver_id IS NOT NULL
                AND event_time > NOW() - 90 * INTERVAL '1 day'",
            ct: ct);

        foreach (var row in drivers)
        {
            var companyId = Convert.ToInt64(row["companyId"]);
            var driverId  = Convert.ToInt64(row["driverId"]);

            // Compute deductions for each time window
            var (score7d, events7d, breakdown7d)   = await ComputeScoreAsync(db, companyId, driverId, 7, ct);
            var (score30d, events30d, breakdown30d) = await ComputeScoreAsync(db, companyId, driverId, 30, ct);
            var (score90d, events90d, _)            = await ComputeScoreAsync(db, companyId, driverId, 90, ct);

            // Upsert driver_safety_scores
            await db.ExecuteAsync(
                @"INSERT INTO driver_safety_scores
                    (company_id, driver_id, score_7d, score_30d, score_90d,
                     events_7d, events_30d, events_90d, breakdown_json, computed_at)
                  VALUES
                    (@cid, @did, @s7, @s30, @s90, @e7, @e30, @e90, @bd, NOW())
                  ON CONFLICT (company_id, driver_id) DO UPDATE SET
                    score_7d=EXCLUDED.score_7d, score_30d=EXCLUDED.score_30d, score_90d=EXCLUDED.score_90d,
                    events_7d=EXCLUDED.events_7d, events_30d=EXCLUDED.events_30d, events_90d=EXCLUDED.events_90d,
                    breakdown_json=EXCLUDED.breakdown_json, computed_at=NOW()",
                c =>
                {
                    c.Parameters.AddWithValue("@cid",  companyId);
                    c.Parameters.AddWithValue("@did",  driverId);
                    c.Parameters.AddWithValue("@s7",   score7d);
                    c.Parameters.AddWithValue("@s30",  score30d);
                    c.Parameters.AddWithValue("@s90",  score90d);
                    c.Parameters.AddWithValue("@e7",   events7d);
                    c.Parameters.AddWithValue("@e30",  events30d);
                    c.Parameters.AddWithValue("@e90",  events90d);
                    c.Parameters.Add(new NpgsqlParameter("@bd", NpgsqlTypes.NpgsqlDbType.Jsonb) { Value = breakdown30d });
                }, ct);
        }
    }

    private async Task RefreshFleetHealthSnapshotsAsync(CancellationToken ct)
    {
        using var scope = scopeFactory.CreateScope();
        var foundation = scope.ServiceProvider.GetRequiredService<SafetyMaintenanceFoundationService>();
        await foundation.RefreshAllFleetHealthSnapshotsAsync(ct);
    }

    private async Task CreateSafetyRecommendationAsync(long companyId, long alertId, string alertType, string severity, long? driverId, long? vehicleId, decimal scoreImpact, CancellationToken ct)
    {
        using var scope = scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<Database>();
        var ai = scope.ServiceProvider.GetRequiredService<PostgresAiFoundationService>();
        var sourceEventId = $"telemetry-alert:{alertId}";
        var existing = await db.ScalarLongAsync(
            "SELECT COUNT(*) FROM ai_recommendations WHERE tenant_id=@tenantId AND recommendation_type='safety.telemetry.review' AND source_event_id=@sourceEventId",
            c =>
            {
                c.Parameters.AddWithValue("@tenantId", companyId);
                c.Parameters.AddWithValue("@sourceEventId", sourceEventId);
            }, ct);
        if (existing > 0) return;

        _ = ai.CreateRecommendation(
            companyId.ToString(CultureInfo.InvariantCulture),
            "safety.telemetry.review",
            $"Review {alertType.Replace('_', ' ')} event",
            $"Telemetry alert {alertId} produced a {severity} safety event. Review the evidence and assign coaching if needed.",
            Math.Min(0.95m, Math.Max(0.60m, 1m - (scoreImpact / 100m))),
            Math.Min(0.95m, Math.Max(0.60m, scoreImpact / 100m)),
            JsonSerializer.Serialize(new { alertId, alertType, severity, driverId, vehicleId, scoreImpact }),
            JsonSerializer.Serialize(new { source = "telemetry_alert", alertId, alertType, severity }),
            JsonSerializer.Serialize(new { action = "review_safety_event", alertId, alertType, severity }),
            severity.ToLowerInvariant(),
            sourceEventId,
            ActorTypes.System,
            "SafetyBackgroundService",
            status: "active");
    }

    private async Task CreateRepeatedSpeedingRecommendationAsync(long companyId, long? driverId, long count, decimal weight, CancellationToken ct)
    {
        if (!driverId.HasValue) return;

        using var scope = scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<Database>();
        var ai = scope.ServiceProvider.GetRequiredService<PostgresAiFoundationService>();
        var sourceEventId = $"repeated-speeding:{companyId}:{driverId}:{count}";
        var existing = await db.ScalarLongAsync(
            "SELECT COUNT(*) FROM ai_recommendations WHERE tenant_id=@tenantId AND recommendation_type='safety.repeated_speeding.review' AND source_event_id=@sourceEventId",
            c =>
            {
                c.Parameters.AddWithValue("@tenantId", companyId);
                c.Parameters.AddWithValue("@sourceEventId", sourceEventId);
            }, ct);
        if (existing > 0) return;

        _ = ai.CreateRecommendation(
            companyId.ToString(CultureInfo.InvariantCulture),
            "safety.repeated_speeding.review",
            "Repeated speeding review required",
            $"Driver {driverId} triggered {count} speeding events in the last 24 hours. This is a behavior pattern that needs coaching and supervisor review.",
            Math.Min(0.96m, Math.Max(0.70m, 1m - (weight / 100m))),
            Math.Min(0.96m, Math.Max(0.70m, weight / 100m)),
            JsonSerializer.Serialize(new { driverId, count, weight }),
            JsonSerializer.Serialize(new { source = "repeated_speeding", driverId, count, weight }),
            JsonSerializer.Serialize(new { action = "review_driver_coaching", driverId, count }),
            "critical",
            sourceEventId,
            ActorTypes.System,
            "SafetyBackgroundService",
            status: "active");
    }

    // Returns (score, event_count, breakdown_json) for a driver in a given day window.
    // Score = 100 - Σ(score_impact) for non-dismissed events, clamped to [0,100].
    internal static async Task<(decimal Score, int Events, string Breakdown)> ComputeScoreAsync(
        Database db, long companyId, long driverId, int days, CancellationToken ct)
    {
        var events = await db.QueryAsync(
            @"SELECT event_type, severity, score_impact
              FROM safety_events
              WHERE company_id=@cid AND driver_id=@did
                AND status NOT IN ('dismissed')
                AND event_time > NOW() - @days * INTERVAL '1 day'",
            c =>
            {
                c.Parameters.AddWithValue("@cid",  companyId);
                c.Parameters.AddWithValue("@did",  driverId);
                c.Parameters.AddWithValue("@days", days);
            }, ct);

        decimal deductions  = 0;
        var byType = new Dictionary<string, (int Count, decimal Impact)>(StringComparer.OrdinalIgnoreCase);

        foreach (var ev in events)
        {
            var impact   = ev["scoreImpact"] is { } si && si is not DBNull ? Convert.ToDecimal(si) : 10m;
            var evType   = ev["eventType"]?.ToString() ?? "unknown";
            deductions  += impact;

            if (!byType.TryGetValue(evType, out var existing))
                byType[evType] = (1, impact);
            else
                byType[evType] = (existing.Count + 1, existing.Impact + impact);
        }

        var score = Math.Max(0m, Math.Min(100m, 100m - deductions));
        var parts = byType.Select(kv => $"\"{kv.Key}\":{{\"count\":{kv.Value.Count},\"impact\":{kv.Value.Impact:F2}}}");
        var breakdown = $"{{{string.Join(",", parts)}}}";

        return (Math.Round(score, 2), events.Count, breakdown);
    }
}
