using System.Globalization;
using System.Text.Json;
using Opstrax.Api.Controllers;
using Opstrax.Api.Data;
using Opstrax.Api.Foundation;

namespace Opstrax.Api.Services;

public sealed class SafetyMaintenanceFoundationService(Database db, PostgresAiFoundationService ai)
{
    private const string QualifiedSafetyEventSql =
        @"((se.data_origin='user_workflow' AND se.verification_status='recorded_by_authenticated_actor')
           OR (se.data_origin='runtime_detection' AND se.verification_status='derived_from_qualified_source')
           OR (se.data_origin='provider_import' AND se.verification_status='provider_verified'))";

    private const string QualifiedMaintenanceItemSql =
        @"((mi.data_origin='user_workflow' AND mi.verification_status='recorded_by_authenticated_actor')
           OR (mi.data_origin='runtime_pm' AND mi.verification_status='derived_from_qualified_source'))";

    private const string QualifiedDvirReportSql =
        @"((dr.data_origin='user_workflow' AND dr.verification_status='recorded_by_authenticated_actor')
           OR (dr.data_origin='provider_import' AND dr.verification_status='provider_verified'))";

    private const string QualifiedDriverScoreSql =
        @"dss.data_origin='runtime_computed'
           AND dss.verification_status='calculated_from_qualified_sources'
           AND dss.computed_at>=NOW()-INTERVAL '15 minutes'
           AND dss.events_30d>0
           AND dss.events_30d=(
             SELECT COUNT(*) FROM safety_events score_event
             WHERE score_event.company_id=dss.company_id AND score_event.driver_id=dss.driver_id
               AND score_event.deleted_at IS NULL AND LOWER(score_event.status)<>'dismissed'
               AND score_event.data_origin='runtime_detection'
               AND score_event.verification_status='derived_from_qualified_source'
               AND score_event.score_impact IS NOT NULL AND score_event.score_impact>0
               AND score_event.event_time>NOW()-INTERVAL '30 days')";

    private const string QualifiedTelemetryStateSql =
        @"tls.source_event_id IS NOT NULL
           AND tls.source_channel IN ('native-hmac','trusted-gateway','samsara-api')";

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
        var currentSnapshot = metrics.EvidenceQualified ? latestSnapshot : null;
        var recommendations = await db.QueryAsync(
            @"SELECT id, recommendation_type, title, summary, confidence_score, urgency_score, risk_level, status, source_event_id, created_at
              FROM ai_recommendations
              WHERE company_id=@companyId
                " + EndpointMappings.GroundedRecommendationSql + @"
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
            ["fleet_health_summary"] = currentSnapshot ?? metrics.FleetHealthSummary,
            ["driver_score_summary"] = metrics.DriverScoreSummary,
            ["vehicle_score_summary"] = metrics.VehicleScoreSummary,
            ["latest_snapshot"] = currentSnapshot,
            ["recommendations"] = recommendations,
            ["next_best_actions"] = metrics.NextBestActions,
            ["foundation_ready"] = metrics.EvidenceQualified,
            ["evidence_status"] = metrics.EvidenceQualified
                ? "calculated_from_qualified_sources"
                : "unavailable_missing_qualified_domain_evidence",
        };
    }

    public async Task<Dictionary<string, object?>> RefreshFleetHealthSnapshotAsync(long companyId, CancellationToken ct = default)
    {
        var metrics = await CollectMetricsAsync(companyId, ct);
        if (!metrics.EvidenceQualified)
            return metrics.FleetHealthSummary;

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
                (company_id, scope_type, scope_value, snapshot_date, fleet_health_score, safety_score, maintenance_score, telemetry_score, risk_level, reason_json, next_action, data_origin, verification_status, created_at, updated_at)
              VALUES
                (@companyId, 'company', @scopeValue, @snapshotDate, @fleetHealthScore, @safetyScore, @maintenanceScore, @telemetryScore, @riskLevel, @reasonJson::jsonb, @nextAction, 'runtime_computed', 'calculated_from_qualified_sources', NOW(), NOW())
              ON CONFLICT (company_id, scope_type, scope_value, snapshot_date)
              DO UPDATE SET
                fleet_health_score=EXCLUDED.fleet_health_score,
                safety_score=EXCLUDED.safety_score,
                maintenance_score=EXCLUDED.maintenance_score,
                telemetry_score=EXCLUDED.telemetry_score,
                risk_level=EXCLUDED.risk_level,
                reason_json=EXCLUDED.reason_json,
                next_action=EXCLUDED.next_action,
                data_origin=EXCLUDED.data_origin,
                verification_status=EXCLUDED.verification_status,
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
                    status: "active",
                    moduleKey: "fleet-health");
            }
        }

        return await GetLatestSnapshotAsync(companyId, ct) ?? metrics.FleetHealthSummary;
    }

    public async Task<List<Dictionary<string, object?>>> ListSnapshotsAsync(long companyId, int limit = 14, CancellationToken ct = default)
        => await db.QueryAsync(
            @"SELECT *
              FROM fleet_health_snapshots
              WHERE company_id=@companyId
                AND data_origin='runtime_computed'
                AND verification_status='calculated_from_qualified_sources'
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
                AND data_origin='runtime_computed'
                AND verification_status='calculated_from_qualified_sources'
              ORDER BY snapshot_date DESC, id DESC
              LIMIT 1",
            c => c.Parameters.AddWithValue("@companyId", companyId), ct);

    private async Task<FoundationMetrics> CollectMetricsAsync(long companyId, CancellationToken ct)
    {
        var safety = await db.QuerySingleAsync(
            $@"SELECT
                COUNT(*) AS total_events,
                COUNT(*) FILTER (WHERE LOWER(status) NOT IN ('resolved','dismissed')) AS open_events,
                COUNT(*) FILTER (WHERE LOWER(severity) IN ('high','critical') AND LOWER(status) NOT IN ('resolved','dismissed')) AS critical_events,
                ROUND(AVG(risk_score), 1) AS average_risk_score
              FROM safety_events se
              WHERE se.company_id=@companyId AND se.deleted_at IS NULL AND {QualifiedSafetyEventSql}",
            c => c.Parameters.AddWithValue("@companyId", companyId), ct) ?? new Dictionary<string, object?>();

        var incidents = await db.QuerySingleAsync(
            $@"SELECT
                COUNT(*) AS total_incidents,
                COUNT(*) FILTER (WHERE LOWER(i.status) NOT IN ('closed','dismissed')) AS open_incidents,
                COUNT(*) FILTER (WHERE LOWER(i.severity) IN ('high','critical') AND LOWER(i.status) NOT IN ('closed','dismissed')) AS critical_incidents
              FROM incidents i
              WHERE i.company_id=@companyId AND i.deleted_at IS NULL
                AND EXISTS (
                  SELECT 1 FROM safety_events se
                  WHERE se.company_id=i.company_id AND se.deleted_at IS NULL AND {QualifiedSafetyEventSql}
                    AND (se.id=i.safety_event_id OR EXISTS (
                      SELECT 1 FROM incident_evidence ie
                      WHERE ie.company_id=i.company_id AND ie.incident_id=i.id
                        AND ie.source_entity_type='safety_event' AND ie.source_entity_id=se.id) OR EXISTS (
                      SELECT 1 FROM evidence_packages ep
                      WHERE ep.company_id=i.company_id AND ep.incident_id=i.id
                        AND ep.safety_event_id=se.id AND ep.deleted_at IS NULL)))",
            c => c.Parameters.AddWithValue("@companyId", companyId), ct) ?? new Dictionary<string, object?>();

        var evidence = await db.QuerySingleAsync(
            $@"WITH qualified_packages AS (
                SELECT ep.* FROM evidence_packages ep
                WHERE ep.company_id=@companyId AND ep.deleted_at IS NULL
                  AND EXISTS (
                    SELECT 1 FROM safety_events se
                    WHERE se.company_id=ep.company_id AND se.deleted_at IS NULL AND {QualifiedSafetyEventSql}
                      AND (se.id=ep.safety_event_id OR EXISTS (
                        SELECT 1 FROM incidents i
                        WHERE i.id=ep.incident_id AND i.company_id=ep.company_id AND i.deleted_at IS NULL
                          AND (i.safety_event_id=se.id OR EXISTS (
                            SELECT 1 FROM incident_evidence ie
                            WHERE ie.company_id=i.company_id AND ie.incident_id=i.id
                              AND ie.source_entity_type='safety_event' AND ie.source_entity_id=se.id)))))
              )
              SELECT
                COUNT(*) AS total_packages,
                COUNT(*) FILTER (WHERE LOWER(status)='draft') AS draft_packages,
                COUNT(*) FILTER (WHERE locked=TRUE) AS locked_packages,
                (SELECT COUNT(*) FROM evidence_package_items epi JOIN qualified_packages ep ON ep.id=epi.evidence_package_id AND ep.company_id=epi.company_id) AS evidence_items
              FROM qualified_packages",
            c => c.Parameters.AddWithValue("@companyId", companyId), ct) ?? new Dictionary<string, object?>();

        var inspections = await db.QuerySingleAsync(
            $@"SELECT
                COUNT(*) AS total_inspections,
                COUNT(*) FILTER (WHERE LOWER(dr.inspection_status) NOT IN ('submitted','passed','completed','closed')) AS open_inspections,
                COUNT(*) FILTER (WHERE dr.safe_to_operate=FALSE) AS unsafe_inspections
              FROM dvir_reports dr
              WHERE dr.company_id=@companyId AND dr.deleted_at IS NULL AND {QualifiedDvirReportSql}",
            c => c.Parameters.AddWithValue("@companyId", companyId), ct) ?? new Dictionary<string, object?>();

        var maintenance = await db.QuerySingleAsync(
            $@"SELECT
                COUNT(*) AS total_items,
                COUNT(*) FILTER (WHERE LOWER(mi.status)='overdue' OR mi.due_date<CURRENT_DATE) AS overdue_items,
                COUNT(*) FILTER (WHERE LOWER(REPLACE(mi.status,' ','_')) IN ('open','scheduled','in_progress','waiting_parts')) AS open_work_orders,
                COUNT(*) FILTER (WHERE LOWER(mi.priority) IN ('high','critical')) AS critical_items,
                COUNT(*) FILTER (WHERE LOWER(mi.status)='active' AND (mi.due_date<=CURRENT_DATE+14*INTERVAL '1 day' OR mi.due_date IS NULL)) AS pm_due_soon
              FROM maintenance_items mi
              WHERE mi.company_id=@companyId AND mi.deleted_at IS NULL AND {QualifiedMaintenanceItemSql}",
            c => c.Parameters.AddWithValue("@companyId", companyId), ct) ?? new Dictionary<string, object?>();

        var telemetry = await db.QuerySingleAsync(
            $@"SELECT
                COUNT(*) AS qualified_assets,
                COUNT(*) FILTER (WHERE tls.telemetry_status='stale' OR tls.stale_seconds>=900 OR tls.risk_level IN ('high','critical')) AS risk_assets,
                COUNT(*) FILTER (WHERE tls.telemetry_status='stale' OR tls.stale_seconds>=900) AS stale_assets,
                COUNT(*) FILTER (WHERE tls.open_alert_count>0) AS open_alert_assets,
                ROUND(AVG(tls.open_alert_count),1) AS avg_alert_count
              FROM telemetry_live_asset_states tls
              WHERE tls.company_id=@companyId AND {QualifiedTelemetryStateSql}",
            c => c.Parameters.AddWithValue("@companyId", companyId), ct) ?? new Dictionary<string, object?>();

        var drivers = await db.QuerySingleAsync(
            $@"SELECT
                COUNT(*) AS scored_drivers,
                ROUND(AVG(dss.score_30d),1) AS average_driver_score,
                COUNT(*) FILTER (WHERE dss.score_30d<70) AS low_driver_scores
              FROM driver_safety_scores dss
              WHERE dss.company_id=@companyId AND {QualifiedDriverScoreSql}",
            c => c.Parameters.AddWithValue("@companyId", companyId), ct) ?? new Dictionary<string, object?>();

        var vehicles = await db.QuerySingleAsync(
            @"WITH vehicle_risk AS (
                SELECT se.vehicle_id,LEAST(100,SUM(se.score_impact)) risk_score
                FROM safety_events se
                WHERE se.company_id=@companyId AND se.vehicle_id IS NOT NULL AND se.deleted_at IS NULL
                  AND LOWER(se.status)<>'dismissed' AND se.event_time>NOW()-INTERVAL '30 days'
                  AND se.data_origin='runtime_detection' AND se.verification_status='derived_from_qualified_source'
                  AND se.score_impact IS NOT NULL AND se.score_impact>0
                GROUP BY se.vehicle_id)
              SELECT COUNT(*) scored_vehicles,
                     ROUND(AVG(100-risk_score),1) average_vehicle_safety_score,
                     COUNT(*) FILTER (WHERE risk_score>=50) high_vehicle_risk
              FROM vehicle_risk",
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
        var safetyEvidenceCount = L(safety, "totalEvents");
        var maintenanceEvidenceCount = L(maintenance, "totalItems") + L(inspections, "totalInspections");
        var telemetryEvidenceCount = L(telemetry, "qualifiedAssets");
        var evidenceQualified = safetyEvidenceCount > 0 && maintenanceEvidenceCount > 0 && telemetryEvidenceCount > 0;

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
            ["average_risk_score"] = DN(safety, "averageRiskScore"),
            ["evidence_status"] = safetyEvidenceCount > 0 ? "qualified" : "unavailable",
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
            ["evidence_status"] = maintenanceEvidenceCount > 0 ? "qualified" : "unavailable",
        };

        var telemetrySummary = new Dictionary<string, object?>
        {
            ["risk_assets"] = telemetryRisk,
            ["stale_assets"] = telemetryStale,
            ["open_alert_assets"] = L(telemetry, "openAlertAssets"),
            ["average_open_alert_count"] = DN(telemetry, "avgAlertCount"),
            ["qualified_assets"] = telemetryEvidenceCount,
            ["evidence_status"] = telemetryEvidenceCount > 0 ? "qualified" : "unavailable",
        };

        var driverScoreSummary = new Dictionary<string, object?>
        {
            ["average_driver_score"] = DN(drivers, "averageDriverScore"),
            ["low_driver_scores"] = lowDriverScores,
            ["scored_drivers"] = L(drivers, "scoredDrivers"),
            ["evidence_status"] = L(drivers, "scoredDrivers") > 0 ? "qualified" : "unavailable",
        };

        var vehicleScoreSummary = new Dictionary<string, object?>
        {
            ["average_vehicle_safety_score"] = DN(vehicles, "averageVehicleSafetyScore"),
            ["high_vehicle_risk"] = highVehicleRisk,
            ["scored_vehicles"] = L(vehicles, "scoredVehicles"),
            ["evidence_status"] = L(vehicles, "scoredVehicles") > 0 ? "qualified" : "unavailable",
        };

        var nextBestActions = new List<string>();
        if (safetyCritical > 0) nextBestActions.Add("Review critical safety events and assign coaching");
        if (incidentCritical > 0) nextBestActions.Add("Open incident review and preserve evidence chain");
        if (maintenanceOverdue > 0) nextBestActions.Add("Release overdue maintenance work orders");
        if (unsafeInspections > 0) nextBestActions.Add("Escalate failed inspections before dispatch");
        if (telemetryStale > 0) nextBestActions.Add("Investigate stale telemetry assets");
        if (safetyEvidenceCount == 0) nextBestActions.Add("Record qualified safety evidence before calculating fleet health");
        if (maintenanceEvidenceCount == 0) nextBestActions.Add("Record qualified maintenance or inspection evidence before calculating fleet health");
        if (telemetryEvidenceCount == 0) nextBestActions.Add("Connect qualified telemetry evidence before calculating fleet health");
        if (nextBestActions.Count == 0) nextBestActions.Add("Continue monitoring live safety and maintenance signals");

        var fleetSummary = new Dictionary<string, object?>
        {
            ["fleet_health_score"] = evidenceQualified ? fleetHealthScore : null,
            ["safety_score"] = safetyEvidenceCount > 0 ? safetyScore : null,
            ["maintenance_score"] = maintenanceEvidenceCount > 0 ? maintenanceScore : null,
            ["telemetry_score"] = telemetryEvidenceCount > 0 ? telemetryScore : null,
            ["risk_level"] = evidenceQualified ? riskLevel : "unavailable",
            ["status"] = !evidenceQualified ? "unavailable" : fleetHealthScore >= 90m ? "healthy" : fleetHealthScore >= 75m ? "watch" : fleetHealthScore >= 60m ? "at_risk" : "critical",
            ["evidence_status"] = evidenceQualified ? "calculated_from_qualified_sources" : "unavailable_missing_qualified_domain_evidence",
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
            nextBestActions,
            evidenceQualified);
    }

    private static decimal Clamp(decimal value, decimal min, decimal max) => Math.Min(max, Math.Max(min, value));

    private static string FleetRiskLevel(decimal score)
        => score >= 90m ? "low" : score >= 75m ? "medium" : score >= 60m ? "high" : "critical";

    private static long L(Dictionary<string, object?> row, string key)
        => row.TryGetValue(key, out var value) && value is not null and not DBNull ? Convert.ToInt64(value, CultureInfo.InvariantCulture) : 0L;

    private static decimal? DN(Dictionary<string, object?> row, string key)
        => row.TryGetValue(key, out var value) && value is not null and not DBNull ? Convert.ToDecimal(value, CultureInfo.InvariantCulture) : null;

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
        IReadOnlyList<string> NextBestActions,
        bool EvidenceQualified);
}
