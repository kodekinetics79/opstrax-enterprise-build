using System.Globalization;
using System.Text.Json;
using Opstrax.Api.Data;
using Opstrax.Api.Foundation;

namespace Opstrax.Api.Services;

public sealed class SafetyMaintenanceFoundationService(Database db, PostgresAiFoundationService ai)
{
    public async Task RefreshAllFleetHealthSnapshotsAsync(CancellationToken ct = default)
    {
        var companyIds = await db.QueryAsync(
            @"SELECT DISTINCT company_id
              FROM (
                  SELECT company_id FROM safety_events WHERE deleted_at IS NULL
                  UNION
                  SELECT company_id FROM incidents WHERE deleted_at IS NULL
                  UNION
                  SELECT company_id FROM evidence_packages WHERE deleted_at IS NULL
                  UNION
                  SELECT company_id FROM dvir_reports WHERE deleted_at IS NULL
                  UNION
                  SELECT company_id FROM maintenance_items WHERE deleted_at IS NULL
                  UNION
                  SELECT company_id FROM telemetry_live_asset_states
                  UNION
                  SELECT company_id FROM driver_safety_scores
                  UNION
                  SELECT company_id FROM vehicle_safety_scorecards
              ) scoped",
            ct: ct);

        foreach (var row in companyIds)
        {
            if (row.TryGetValue("companyId", out var value) && value is not null and not DBNull)
            {
                await RefreshFleetHealthSnapshotAsync(Convert.ToInt64(value, CultureInfo.InvariantCulture), ct);
            }
        }
    }

    public async Task<Dictionary<string, object?>> GetSummaryAsync(long companyId, CancellationToken ct = default)
    {
        var metrics = await CollectMetricsAsync(companyId, ct);
        var latestSnapshot = await GetLatestSnapshotAsync(companyId, ct);
        var recommendations = await db.QueryAsync(
            @"SELECT id, recommendation_type, title, summary, confidence_score, urgency_score, risk_level, status, source_event_id, created_at
              FROM ai_recommendations
              WHERE company_id=@companyId
                AND (
                    recommendation_type LIKE 'safety.%'
                    OR recommendation_type LIKE 'maintenance.%'
                    OR recommendation_type LIKE 'fleet.health.%'
                )
              ORDER BY created_at DESC, id DESC
              LIMIT 10",
            c => c.Parameters.AddWithValue("@companyId", companyId), ct);

        return new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
        {
            ["company_id"] = companyId,
            ["safety_summary"] = metrics.SafetySummary,
            ["incident_summary"] = metrics.IncidentSummary,
            ["evidence_summary"] = metrics.EvidenceSummary,
            ["inspection_summary"] = metrics.InspectionSummary,
            ["maintenance_summary"] = metrics.MaintenanceSummary,
            ["telemetry_bridge_summary"] = metrics.TelemetrySummary,
            ["fleet_health_summary"] = latestSnapshot ?? metrics.FleetHealthSummary,
            ["driver_score_summary"] = metrics.DriverScoreSummary,
            ["vehicle_score_summary"] = metrics.VehicleScoreSummary,
            ["latest_snapshot"] = latestSnapshot,
            ["recommendations"] = recommendations,
            ["next_best_actions"] = metrics.NextBestActions,
            ["foundation_ready"] = true,
        };
    }

    public async Task<Dictionary<string, object?>> RefreshFleetHealthSnapshotAsync(long companyId, CancellationToken ct = default)
    {
        var metrics = await CollectMetricsAsync(companyId, ct);
        var snapshotDate = DateOnly.FromDateTime(DateTime.UtcNow);
        var scopeValue = companyId.ToString(CultureInfo.InvariantCulture);
        var reasonJson = JsonSerializer.Serialize(new
        {
            metrics.SafetySummary,
            metrics.IncidentSummary,
            metrics.EvidenceSummary,
            metrics.InspectionSummary,
            metrics.MaintenanceSummary,
            metrics.TelemetrySummary,
            metrics.DriverScoreSummary,
            metrics.VehicleScoreSummary,
        });

        await db.ExecuteAsync(
            @"INSERT INTO fleet_health_snapshots
                (company_id, scope_type, scope_value, snapshot_date, fleet_health_score, safety_score, maintenance_score, telemetry_score, risk_level, reason_json, next_action, created_at, updated_at)
              VALUES
                (@companyId, 'company', @scopeValue, @snapshotDate, @fleetHealthScore, @safetyScore, @maintenanceScore, @telemetryScore, @riskLevel, @reasonJson::jsonb, @nextAction, NOW(), NOW())
              ON CONFLICT (company_id, scope_type, scope_value, snapshot_date)
              DO UPDATE SET
                fleet_health_score=EXCLUDED.fleet_health_score,
                safety_score=EXCLUDED.safety_score,
                maintenance_score=EXCLUDED.maintenance_score,
                telemetry_score=EXCLUDED.telemetry_score,
                risk_level=EXCLUDED.risk_level,
                reason_json=EXCLUDED.reason_json,
                next_action=EXCLUDED.next_action,
                updated_at=NOW()",
            c =>
            {
                c.Parameters.AddWithValue("@companyId", companyId);
                c.Parameters.AddWithValue("@scopeValue", scopeValue);
                c.Parameters.AddWithValue("@snapshotDate", snapshotDate);
                c.Parameters.AddWithValue("@fleetHealthScore", metrics.FleetHealthScore);
                c.Parameters.AddWithValue("@safetyScore", metrics.SafetyScore);
                c.Parameters.AddWithValue("@maintenanceScore", metrics.MaintenanceScore);
                c.Parameters.AddWithValue("@telemetryScore", metrics.TelemetryScore);
                c.Parameters.AddWithValue("@riskLevel", metrics.RiskLevel);
                c.Parameters.AddWithValue("@reasonJson", reasonJson);
                c.Parameters.AddWithValue("@nextAction", metrics.NextBestActions.FirstOrDefault() ?? "Review risk signals");
            }, ct);

        if (metrics.FleetHealthScore < 80m || L(metrics.SafetySummary, "criticalEvents") > 0 || L(metrics.MaintenanceSummary, "overdueItems") > 0)
        {
            var sourceEventId = $"fleet-health:{companyId}:{snapshotDate:yyyy-MM-dd}";
            var existingRecommendationCount = await db.ScalarLongAsync(
                "SELECT COUNT(*) FROM ai_recommendations WHERE company_id=@companyId AND recommendation_type='fleet.health.review' AND source_event_id=@sourceEventId",
                c =>
                {
                    c.Parameters.AddWithValue("@companyId", companyId);
                    c.Parameters.AddWithValue("@sourceEventId", sourceEventId);
                }, ct);

            if (existingRecommendationCount == 0)
            {
                _ = ai.CreateRecommendation(
                    companyId.ToString(CultureInfo.InvariantCulture),
                    "fleet.health.review",
                    "Fleet health review required",
                    $"Fleet health score is {metrics.FleetHealthScore:F1}. Review safety, maintenance, and telemetry risk before the next operating cycle.",
                    Math.Max(0.55m, Math.Min(0.95m, metrics.FleetHealthScore / 100m)),
                    Math.Max(0.55m, Math.Min(0.95m, 1m - (metrics.FleetHealthScore / 100m))),
                    JsonSerializer.Serialize(new
                    {
                        metrics.FleetHealthScore,
                        metrics.SafetyScore,
                        metrics.MaintenanceScore,
                        metrics.TelemetryScore,
                    }),
                    reasonJson,
                    JsonSerializer.Serialize(new
                    {
                        action = "review_fleet_health",
                        nextBestActions = metrics.NextBestActions,
                    }),
                    metrics.RiskLevel,
                    sourceEventId,
                    ActorTypes.System,
                    "safety-maintenance-foundation",
                    status: "active");
            }
        }

        return await GetLatestSnapshotAsync(companyId, ct) ?? metrics.FleetHealthSummary;
    }

    public async Task<List<Dictionary<string, object?>>> ListSnapshotsAsync(long companyId, int limit = 14, CancellationToken ct = default)
        => await db.QueryAsync(
            @"SELECT *
              FROM fleet_health_snapshots
              WHERE company_id=@companyId
              ORDER BY snapshot_date DESC, id DESC
              LIMIT @limit",
            c =>
            {
                c.Parameters.AddWithValue("@companyId", companyId);
                c.Parameters.AddWithValue("@limit", limit);
            }, ct);

    private async Task<Dictionary<string, object?>?> GetLatestSnapshotAsync(long companyId, CancellationToken ct)
        => await db.QuerySingleAsync(
            @"SELECT *
              FROM fleet_health_snapshots
              WHERE company_id=@companyId
              ORDER BY snapshot_date DESC, id DESC
              LIMIT 1",
            c => c.Parameters.AddWithValue("@companyId", companyId), ct);

    private async Task<FoundationMetrics> CollectMetricsAsync(long companyId, CancellationToken ct)
    {
        var safety = await db.QuerySingleAsync(
            @"SELECT
                COUNT(*) FILTER (WHERE deleted_at IS NULL) AS total_events,
                COUNT(*) FILTER (WHERE deleted_at IS NULL AND status NOT IN ('Resolved','Dismissed','resolved','dismissed')) AS open_events,
                COUNT(*) FILTER (WHERE deleted_at IS NULL AND severity IN ('High','Critical') AND status NOT IN ('Resolved','Dismissed','resolved','dismissed')) AS critical_events,
                COALESCE(ROUND(AVG(risk_score), 1), 0) AS average_risk_score
              FROM safety_events
              WHERE company_id=@companyId",
            c => c.Parameters.AddWithValue("@companyId", companyId), ct) ?? new Dictionary<string, object?>();

        var incidents = await db.QuerySingleAsync(
            @"SELECT
                COUNT(*) AS total_incidents,
                COUNT(*) FILTER (WHERE deleted_at IS NULL AND status NOT IN ('Closed','Dismissed','closed','dismissed')) AS open_incidents,
                COUNT(*) FILTER (WHERE deleted_at IS NULL AND severity IN ('High','Critical') AND status NOT IN ('Closed','Dismissed','closed','dismissed')) AS critical_incidents
              FROM incidents
              WHERE company_id=@companyId",
            c => c.Parameters.AddWithValue("@companyId", companyId), ct) ?? new Dictionary<string, object?>();

        var evidence = await db.QuerySingleAsync(
            @"SELECT
                COUNT(*) AS total_packages,
                COUNT(*) FILTER (WHERE deleted_at IS NULL AND status IN ('Draft','draft')) AS draft_packages,
                COUNT(*) FILTER (WHERE deleted_at IS NULL AND locked=TRUE) AS locked_packages,
                COALESCE((SELECT COUNT(*) FROM evidence_package_items epi JOIN evidence_packages ep ON ep.id = epi.evidence_package_id WHERE ep.company_id=@companyId AND ep.deleted_at IS NULL), 0) AS evidence_items
              FROM evidence_packages
              WHERE company_id=@companyId",
            c => c.Parameters.AddWithValue("@companyId", companyId), ct) ?? new Dictionary<string, object?>();

        var inspections = await db.QuerySingleAsync(
            @"SELECT
                COUNT(*) AS total_inspections,
                COUNT(*) FILTER (WHERE deleted_at IS NULL AND inspection_status NOT IN ('Submitted','Passed','Completed','Closed','closed')) AS open_inspections,
                COUNT(*) FILTER (WHERE deleted_at IS NULL AND safe_to_operate = FALSE) AS unsafe_inspections
              FROM dvir_reports
              WHERE company_id=@companyId",
            c => c.Parameters.AddWithValue("@companyId", companyId), ct) ?? new Dictionary<string, object?>();

        var maintenance = await db.QuerySingleAsync(
            @"SELECT
                COUNT(*) FILTER (WHERE deleted_at IS NULL) AS total_items,
                COUNT(*) FILTER (WHERE deleted_at IS NULL AND (status='Overdue' OR due_date < CURRENT_DATE)) AS overdue_items,
                COUNT(*) FILTER (WHERE deleted_at IS NULL AND status IN ('Open','Scheduled','In Progress','Waiting Parts','in_progress','waiting_parts')) AS open_work_orders,
                COUNT(*) FILTER (WHERE deleted_at IS NULL AND priority IN ('High','Critical')) AS critical_items,
                COUNT(*) FILTER (WHERE deleted_at IS NULL AND status IN ('Active','active') AND (due_date <= CURRENT_DATE + 14 * INTERVAL '1 day' OR due_date IS NULL)) AS pm_due_soon
              FROM maintenance_items
              WHERE company_id=@companyId",
            c => c.Parameters.AddWithValue("@companyId", companyId), ct) ?? new Dictionary<string, object?>();

        var telemetry = await db.QuerySingleAsync(
            @"SELECT
                COUNT(*) FILTER (WHERE telemetry_status = 'stale' OR stale_seconds >= 900 OR risk_level IN ('high','critical')) AS risk_assets,
                COUNT(*) FILTER (WHERE telemetry_status = 'stale' OR stale_seconds >= 900) AS stale_assets,
                COUNT(*) FILTER (WHERE open_alert_count > 0) AS open_alert_assets,
                COALESCE(ROUND(AVG(COALESCE(open_alert_count, 0)), 1), 0) AS avg_alert_count
              FROM telemetry_live_asset_states
              WHERE company_id=@companyId",
            c => c.Parameters.AddWithValue("@companyId", companyId), ct) ?? new Dictionary<string, object?>();

        var drivers = await db.QuerySingleAsync(
            @"SELECT
                COALESCE(ROUND(AVG(score_30d), 1), 0) AS average_driver_score,
                COUNT(*) FILTER (WHERE score_30d < 70) AS low_driver_scores
              FROM driver_safety_scores
              WHERE company_id=@companyId",
            c => c.Parameters.AddWithValue("@companyId", companyId), ct) ?? new Dictionary<string, object?>();

        var vehicles = await db.QuerySingleAsync(
            @"SELECT
                COALESCE(ROUND(AVG(safety_score), 1), 0) AS average_vehicle_safety_score,
                COUNT(*) FILTER (WHERE risk_score >= 50) AS high_vehicle_risk
              FROM vehicle_safety_scorecards
              WHERE company_id=@companyId",
            c => c.Parameters.AddWithValue("@companyId", companyId), ct) ?? new Dictionary<string, object?>();

        var safetyOpen = L(safety, "openEvents");
        var safetyCritical = L(safety, "criticalEvents");
        var incidentOpen = L(incidents, "openIncidents");
        var incidentCritical = L(incidents, "criticalIncidents");
        var evidenceDraft = L(evidence, "draftPackages");
        var inspectionOpen = L(inspections, "openInspections");
        var unsafeInspections = L(inspections, "unsafeInspections");
        var maintenanceOverdue = L(maintenance, "overdueItems");
        var maintenanceOpen = L(maintenance, "openWorkOrders");
        var maintenanceCritical = L(maintenance, "criticalItems");
        var telemetryRisk = L(telemetry, "riskAssets");
        var telemetryStale = L(telemetry, "staleAssets");
        var lowDriverScores = L(drivers, "lowDriverScores");
        var highVehicleRisk = L(vehicles, "highVehicleRisk");

        var safetyScore = Clamp(100m - (safetyOpen * 4m) - (safetyCritical * 6m) - (incidentOpen * 3m) - (incidentCritical * 5m) - (lowDriverScores * 2m), 0m, 100m);
        var maintenanceScore = Clamp(100m - (maintenanceOverdue * 6m) - (maintenanceOpen * 3m) - (maintenanceCritical * 5m) - (inspectionOpen * 2m) - (unsafeInspections * 4m), 0m, 100m);
        var telemetryScore = Clamp(100m - (telemetryRisk * 3m) - (telemetryStale * 4m), 0m, 100m);
        var fleetHealthScore = Math.Round((safetyScore * 0.4m) + (maintenanceScore * 0.4m) + (telemetryScore * 0.2m) - (highVehicleRisk * 0.5m), 1);
        fleetHealthScore = Clamp(fleetHealthScore, 0m, 100m);
        var riskLevel = FleetRiskLevel(fleetHealthScore);

        var safetySummary = new Dictionary<string, object?>
        {
            ["total_events"] = L(safety, "totalEvents"),
            ["open_events"] = safetyOpen,
            ["critical_events"] = safetyCritical,
            ["average_risk_score"] = D(safety, "averageRiskScore"),
        };

        var incidentSummary = new Dictionary<string, object?>
        {
            ["total_incidents"] = L(incidents, "totalIncidents"),
            ["open_incidents"] = incidentOpen,
            ["critical_incidents"] = incidentCritical,
        };

        var evidenceSummary = new Dictionary<string, object?>
        {
            ["total_packages"] = L(evidence, "totalPackages"),
            ["draft_packages"] = evidenceDraft,
            ["locked_packages"] = L(evidence, "lockedPackages"),
            ["evidence_items"] = L(evidence, "evidenceItems"),
        };

        var inspectionSummary = new Dictionary<string, object?>
        {
            ["total_inspections"] = L(inspections, "totalInspections"),
            ["open_inspections"] = inspectionOpen,
            ["unsafe_inspections"] = unsafeInspections,
        };

        var maintenanceSummary = new Dictionary<string, object?>
        {
            ["total_items"] = L(maintenance, "totalItems"),
            ["overdue_items"] = maintenanceOverdue,
            ["open_work_orders"] = maintenanceOpen,
            ["critical_items"] = maintenanceCritical,
            ["pm_due_soon"] = L(maintenance, "pmDueSoon"),
        };

        var telemetrySummary = new Dictionary<string, object?>
        {
            ["risk_assets"] = telemetryRisk,
            ["stale_assets"] = telemetryStale,
            ["open_alert_assets"] = L(telemetry, "openAlertAssets"),
            ["average_open_alert_count"] = D(telemetry, "avgAlertCount"),
        };

        var driverScoreSummary = new Dictionary<string, object?>
        {
            ["average_driver_score"] = D(drivers, "averageDriverScore"),
            ["low_driver_scores"] = lowDriverScores,
        };

        var vehicleScoreSummary = new Dictionary<string, object?>
        {
            ["average_vehicle_safety_score"] = D(vehicles, "averageVehicleSafetyScore"),
            ["high_vehicle_risk"] = highVehicleRisk,
        };

        var nextBestActions = new List<string>();
        if (safetyCritical > 0) nextBestActions.Add("Review critical safety events and assign coaching");
        if (incidentCritical > 0) nextBestActions.Add("Open incident review and preserve evidence chain");
        if (maintenanceOverdue > 0) nextBestActions.Add("Release overdue maintenance work orders");
        if (unsafeInspections > 0) nextBestActions.Add("Escalate failed inspections before dispatch");
        if (telemetryStale > 0) nextBestActions.Add("Investigate stale telemetry assets");
        if (nextBestActions.Count == 0) nextBestActions.Add("Continue monitoring live safety and maintenance signals");

        var fleetSummary = new Dictionary<string, object?>
        {
            ["fleet_health_score"] = fleetHealthScore,
            ["safety_score"] = safetyScore,
            ["maintenance_score"] = maintenanceScore,
            ["telemetry_score"] = telemetryScore,
            ["risk_level"] = riskLevel,
            ["status"] = fleetHealthScore >= 90m ? "healthy" : fleetHealthScore >= 75m ? "watch" : fleetHealthScore >= 60m ? "at_risk" : "critical",
            ["reason_json"] = new
            {
                openSafetyEvents = safetyOpen,
                criticalSafetyEvents = safetyCritical,
                openIncidents = incidentOpen,
                overdueMaintenanceItems = maintenanceOverdue,
                openWorkOrders = maintenanceOpen,
                telemetryRiskAssets = telemetryRisk,
                telemetryStaleAssets = telemetryStale,
            },
            ["next_action"] = nextBestActions.FirstOrDefault(),
        };

        return new FoundationMetrics(
            safetySummary,
            incidentSummary,
            evidenceSummary,
            inspectionSummary,
            maintenanceSummary,
            telemetrySummary,
            driverScoreSummary,
            vehicleScoreSummary,
            fleetSummary,
            fleetHealthScore,
            safetyScore,
            maintenanceScore,
            telemetryScore,
            riskLevel,
            nextBestActions);
    }

    private static decimal Clamp(decimal value, decimal min, decimal max) => Math.Min(max, Math.Max(min, value));

    private static string FleetRiskLevel(decimal score)
        => score >= 90m ? "low" : score >= 75m ? "medium" : score >= 60m ? "high" : "critical";

    private static long L(Dictionary<string, object?> row, string key)
        => row.TryGetValue(key, out var value) && value is not null and not DBNull ? Convert.ToInt64(value, CultureInfo.InvariantCulture) : 0L;

    private static decimal D(Dictionary<string, object?> row, string key)
        => row.TryGetValue(key, out var value) && value is not null and not DBNull ? Convert.ToDecimal(value, CultureInfo.InvariantCulture) : 0m;

    private sealed record FoundationMetrics(
        Dictionary<string, object?> SafetySummary,
        Dictionary<string, object?> IncidentSummary,
        Dictionary<string, object?> EvidenceSummary,
        Dictionary<string, object?> InspectionSummary,
        Dictionary<string, object?> MaintenanceSummary,
        Dictionary<string, object?> TelemetrySummary,
        Dictionary<string, object?> DriverScoreSummary,
        Dictionary<string, object?> VehicleScoreSummary,
        Dictionary<string, object?> FleetHealthSummary,
        decimal FleetHealthScore,
        decimal SafetyScore,
        decimal MaintenanceScore,
        decimal TelemetryScore,
        string RiskLevel,
        IReadOnlyList<string> NextBestActions);
}
