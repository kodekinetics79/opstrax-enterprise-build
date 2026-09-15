using Microsoft.Extensions.Configuration;
using Opstrax.Api.Data;
using Opstrax.Api.Foundation;
using Opstrax.Api.Services;

namespace Opstrax.Tests;

[Trait("Category", "Integration")]
public class Stage13BSafetyMaintenanceTests
{
    private static readonly string LocalConnectionString = TestDb.ConnectionString;

    [Fact]
    public async Task SafetyMaintenanceFoundation_Persists_Snapshot_And_Recommendations_Without_Mutating_Business_Tables()
    {
        var db = CreateDatabase();
        var companyId = NextCompanyId();
        var vehicleId = await GetAnyVehicleIdAsync(db);
        var driverId = await GetAnyDriverIdAsync(db);
        var ambient = new AmbientCorrelationContext();
        var ai = new PostgresAiFoundationService(db, ambient);
        var foundation = new SafetyMaintenanceFoundationService(db, ai);
        var safetySchema = new SafetySchemaService(db);
        var maintenanceSchema = new MaintenanceSchemaService(db);
        var batch3Schema = new Batch3SchemaService(db);
        var batch4Schema = new Batch4SchemaService(db);
        var telemetrySchema = new TelemetrySchemaService(db);
        var foundationSchema = new FoundationSchemaService(db);
        var smfSchema = new SafetyMaintenanceFoundationSchemaService(db);

        try
        {
            await foundationSchema.EnsureAsync();
            await telemetrySchema.EnsureAsync();
            await safetySchema.EnsureAsync();
            await batch3Schema.EnsureAsync();
            await batch4Schema.EnsureAsync();
            await maintenanceSchema.EnsureAsync();
            await smfSchema.EnsureAsync();

            await SeedFoundationSignalsAsync(db, companyId, vehicleId, driverId);

            var beforeCounts = await CaptureCountsAsync(db, companyId);
            var snapshot = await foundation.RefreshFleetHealthSnapshotAsync(companyId);
            var summary = await foundation.GetSummaryAsync(companyId);
            var afterCounts = await CaptureCountsAsync(db, companyId);

            Assert.True(Convert.ToDecimal(snapshot["fleetHealthScore"]) < 80m);
            Assert.Equal("medium", snapshot["riskLevel"]?.ToString());
            Assert.Contains("fleet_health_summary", summary.Keys);
            Assert.Contains("next_best_actions", summary.Keys);
            Assert.Contains("recommendations", summary.Keys);

            var fleetSummary = (Dictionary<string, object?>)summary["fleet_health_summary"]!;
            Assert.True(Convert.ToDecimal(fleetSummary["fleetHealthScore"]) < 80m);
            Assert.Equal("medium", fleetSummary["riskLevel"]?.ToString());

            Assert.Equal(beforeCounts["safety_events"], afterCounts["safety_events"]);
            Assert.Equal(beforeCounts["maintenance_items"], afterCounts["maintenance_items"]);
            Assert.Equal(beforeCounts["telemetry_live_asset_states"], afterCounts["telemetry_live_asset_states"]);

            Assert.Equal(0, await db.ScalarLongAsync("SELECT COUNT(*) FROM approval_requests WHERE tenant_id=@tenantId", c => c.Parameters.AddWithValue("@tenantId", companyId)));
            Assert.Equal(0, await db.ScalarLongAsync("SELECT COUNT(*) FROM ai_action_requests WHERE tenant_id=@tenantId", c => c.Parameters.AddWithValue("@tenantId", companyId)));
            Assert.True(await db.ScalarLongAsync("SELECT COUNT(*) FROM fleet_health_snapshots WHERE company_id=@companyId", c => c.Parameters.AddWithValue("@companyId", companyId)) > 0);
            Assert.True(await db.ScalarLongAsync("SELECT COUNT(*) FROM ai_recommendations WHERE tenant_id=@tenantId AND recommendation_type='fleet.health.review'", c => c.Parameters.AddWithValue("@tenantId", companyId)) > 0);
            Assert.Equal(1, await db.ScalarLongAsync(@"SELECT COUNT(*) FROM fleet_health_snapshots
                    WHERE company_id=@companyId AND data_origin='runtime_computed'
                      AND verification_status='calculated_from_qualified_sources'",
                c => c.Parameters.AddWithValue("@companyId", companyId)));
        }
        finally
        {
            await CleanupTenantAsync(db, companyId);
        }
    }

    [Fact]
    public async Task FoundationSummary_Contains_All_Safety_Maintenance_Sections()
    {
        var db = CreateDatabase();
        var companyId = NextCompanyId();
        var vehicleId = await GetAnyVehicleIdAsync(db);
        var driverId = await GetAnyDriverIdAsync(db);
        var ambient = new AmbientCorrelationContext();
        var ai = new PostgresAiFoundationService(db, ambient);
        var foundation = new SafetyMaintenanceFoundationService(db, ai);
        var safetySchema = new SafetySchemaService(db);
        var maintenanceSchema = new MaintenanceSchemaService(db);
        var batch3Schema = new Batch3SchemaService(db);
        var batch4Schema = new Batch4SchemaService(db);
        var telemetrySchema = new TelemetrySchemaService(db);
        var foundationSchema = new FoundationSchemaService(db);
        var smfSchema = new SafetyMaintenanceFoundationSchemaService(db);

        try
        {
            await foundationSchema.EnsureAsync();
            await telemetrySchema.EnsureAsync();
            await safetySchema.EnsureAsync();
            await batch3Schema.EnsureAsync();
            await batch4Schema.EnsureAsync();
            await maintenanceSchema.EnsureAsync();
            await smfSchema.EnsureAsync();

            await SeedFoundationSignalsAsync(db, companyId, vehicleId, driverId);

            var summary = await foundation.GetSummaryAsync(companyId);

            Assert.True((bool)summary["foundation_ready"]!);
            Assert.Contains("safety_summary", summary.Keys);
            Assert.Contains("incident_summary", summary.Keys);
            Assert.Contains("evidence_summary", summary.Keys);
            Assert.Contains("inspection_summary", summary.Keys);
            Assert.Contains("maintenance_summary", summary.Keys);
            Assert.Contains("telemetry_bridge_summary", summary.Keys);
            Assert.Contains("fleet_health_summary", summary.Keys);
            Assert.Contains("driver_score_summary", summary.Keys);
            Assert.Contains("vehicle_score_summary", summary.Keys);
        }
        finally
        {
            await CleanupTenantAsync(db, companyId);
        }
    }

    [Fact]
    public async Task UnverifiedAndSimulatorSignals_DoNotProduceCurrentFleetHealthEvidence()
    {
        var db = CreateDatabase();
        var companyId = NextCompanyId();
        var vehicleId = await GetAnyVehicleIdAsync(db);
        var driverId = await GetAnyDriverIdAsync(db);
        var foundation = new SafetyMaintenanceFoundationService(db, new PostgresAiFoundationService(db, new AmbientCorrelationContext()));

        try
        {
            await new FoundationSchemaService(db).EnsureAsync();
            await new TelemetrySchemaService(db).EnsureAsync();
            await new SafetySchemaService(db).EnsureAsync();
            await new Batch3SchemaService(db).EnsureAsync();
            await new Batch4SchemaService(db).EnsureAsync();
            await new MaintenanceSchemaService(db).EnsureAsync();
            await new SafetyMaintenanceFoundationSchemaService(db).EnsureAsync();

            await db.ExecuteAsync(@"INSERT INTO safety_events
                    (company_id,driver_id,vehicle_id,event_type,severity,status,event_time,risk_score,score_impact)
                  VALUES(@companyId,@driverId,@vehicleId,'speeding','Critical','open',NOW(),95,40)",
                c => { c.Parameters.AddWithValue("@companyId", companyId); c.Parameters.AddWithValue("@driverId", driverId); c.Parameters.AddWithValue("@vehicleId", vehicleId); });
            await db.ExecuteAsync(@"INSERT INTO maintenance_items
                    (company_id,vehicle_id,service_type,title,category,status,priority,due_date,risk_score)
                  VALUES(@companyId,@vehicleId,'Brake','Legacy brake alert','Maintenance','Overdue','Critical',CURRENT_DATE-1,95)",
                c => { c.Parameters.AddWithValue("@companyId", companyId); c.Parameters.AddWithValue("@vehicleId", vehicleId); });
            await db.ExecuteAsync(@"INSERT INTO telemetry_live_asset_states
                    (company_id,vehicle_id,lat,lng,telemetry_status,risk_level,open_alert_count,stale_seconds,last_event_time,received_at,source_event_id,source_channel)
                  VALUES(@companyId,@vehicleId,1,1,'stale','critical',9,9999,NOW(),NOW(),70001,'simulator')",
                c => { c.Parameters.AddWithValue("@companyId", companyId); c.Parameters.AddWithValue("@vehicleId", vehicleId); });
            await db.ExecuteAsync(@"INSERT INTO fleet_health_snapshots
                    (company_id,scope_type,scope_value,snapshot_date,fleet_health_score,safety_score,maintenance_score,telemetry_score,risk_level)
                  VALUES(@companyId,'company',@scope,CURRENT_DATE,99,99,99,99,'low')",
                c => { c.Parameters.AddWithValue("@companyId", companyId); c.Parameters.AddWithValue("@scope", companyId.ToString()); });
            await db.ExecuteAsync(@"INSERT INTO fleet_health_snapshots
                    (company_id,scope_type,scope_value,snapshot_date,fleet_health_score,safety_score,maintenance_score,telemetry_score,risk_level,data_origin,verification_status)
                  VALUES(@companyId,'company',@scope,CURRENT_DATE-1,88,88,88,88,'low','runtime_computed','calculated_from_qualified_sources')",
                c => { c.Parameters.AddWithValue("@companyId", companyId); c.Parameters.AddWithValue("@scope", companyId.ToString()); });

            var result = await foundation.RefreshFleetHealthSnapshotAsync(companyId);
            Assert.Null(result["fleet_health_score"]);
            Assert.Equal("unavailable_missing_qualified_domain_evidence", result["evidence_status"]);

            var summary = await foundation.GetSummaryAsync(companyId);
            Assert.False((bool)summary["foundation_ready"]!);
            Assert.Null(summary["latest_snapshot"]);
            Assert.Single(await foundation.ListSnapshotsAsync(companyId));
            Assert.Equal(0, await db.ScalarLongAsync(
                "SELECT COUNT(*) FROM ai_recommendations WHERE tenant_id=@tenantId AND recommendation_type='fleet.health.review'",
                c => c.Parameters.AddWithValue("@tenantId", companyId)));
        }
        finally
        {
            await CleanupTenantAsync(db, companyId);
        }
    }

    private static Database CreateDatabase()
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:DefaultConnection"] = LocalConnectionString,
            })
            .Build();
        return new Database(config);
    }

    private static long NextCompanyId() => Interlocked.Increment(ref _nextCompanyId);

    private static long _nextCompanyId = 71000;

    private static async Task SeedFoundationSignalsAsync(Database db, long companyId, long vehicleId, long driverId)
    {
        var safetyEvent1 = await db.InsertAsync(
            @"INSERT INTO safety_events (company_id, driver_id, vehicle_id, event_type, severity, status, event_time, risk_score, score_impact, data_origin, verification_status)
              VALUES (@companyId, @driverId, @vehicleId, 'speeding', 'Critical', 'open', NOW(), 92, 14, 'runtime_detection', 'derived_from_qualified_source')",
            c =>
            {
                c.Parameters.AddWithValue("@companyId", companyId);
                c.Parameters.AddWithValue("@driverId", driverId);
                c.Parameters.AddWithValue("@vehicleId", vehicleId);
            });
        _ = await db.InsertAsync(
            @"INSERT INTO safety_events (company_id, driver_id, vehicle_id, event_type, severity, status, event_time, risk_score, score_impact, data_origin, verification_status)
              VALUES (@companyId, @driverId, @vehicleId, 'geofence_breach', 'Critical', 'open', NOW(), 89, 16, 'runtime_detection', 'derived_from_qualified_source')",
            c =>
            {
                c.Parameters.AddWithValue("@companyId", companyId);
                c.Parameters.AddWithValue("@driverId", driverId);
                c.Parameters.AddWithValue("@vehicleId", vehicleId);
            });

        var incidentId = await db.InsertAsync(
            @"INSERT INTO incidents (company_id, incident_number, driver_id, vehicle_id, incident_type, severity, status, occurred_at, recommended_action)
              VALUES (@companyId, CONCAT('INC-STAGE13B-', floor(extract(epoch from now()))::bigint), @driverId, @vehicleId, 'Near Miss', 'Critical', 'New', NOW(), 'Review evidence and coaching')",
            c =>
            {
                c.Parameters.AddWithValue("@companyId", companyId);
                c.Parameters.AddWithValue("@driverId", driverId);
                c.Parameters.AddWithValue("@vehicleId", vehicleId);
            });

        var evidencePackageId = await db.InsertAsync(
            @"INSERT INTO evidence_packages (company_id, package_number, incident_id, safety_event_id, driver_id, vehicle_id, package_type, status, locked, summary)
              VALUES (@companyId, CONCAT('EP-STAGE13B-', floor(extract(epoch from now()))::bigint), @incidentId, @safetyEventId, @driverId, @vehicleId, 'Insurance Evidence', 'Draft', TRUE, 'Evidence bundle for Stage 13B')",
            c =>
            {
                c.Parameters.AddWithValue("@companyId", companyId);
                c.Parameters.AddWithValue("@incidentId", incidentId);
                c.Parameters.AddWithValue("@safetyEventId", safetyEvent1);
                c.Parameters.AddWithValue("@driverId", driverId);
                c.Parameters.AddWithValue("@vehicleId", vehicleId);
            });

        await db.ExecuteAsync(
            @"INSERT INTO evidence_package_items (company_id, package_id, evidence_package_id, item_type, item_title, item_json)
              VALUES (@companyId, @packageId, @packageId, 'photo', 'Scene photo', '{""source"":""stage13b""}'::jsonb)",
            c =>
            {
                c.Parameters.AddWithValue("@companyId", companyId);
                c.Parameters.AddWithValue("@packageId", evidencePackageId);
            });

        var dvirReportId = await db.InsertAsync(
            @"INSERT INTO dvir_reports (company_id, report_number, driver_id, vehicle_id, inspection_type, inspection_status, defects_found, safe_to_operate, risk_score, recommended_action, data_origin, verification_status)
              VALUES (@companyId, CONCAT('DVIR-STAGE13B-', floor(extract(epoch from now()))::bigint), @driverId, @vehicleId, 'Pre-Trip', 'Submitted', 1, FALSE, 88, 'Repair defects before dispatch', 'user_workflow', 'recorded_by_authenticated_actor')",
            c =>
            {
                c.Parameters.AddWithValue("@companyId", companyId);
                c.Parameters.AddWithValue("@driverId", driverId);
                c.Parameters.AddWithValue("@vehicleId", vehicleId);
            });

        await db.ExecuteAsync(
            @"INSERT INTO dvir_defects (company_id, dvir_report_id, defect_category, defect_description, severity, status, out_of_service)
              VALUES (@companyId, @reportId, 'Brakes', 'Brake wear exceeds threshold', 'Critical', 'Open', TRUE)",
            c =>
            {
                c.Parameters.AddWithValue("@companyId", companyId);
                c.Parameters.AddWithValue("@reportId", dvirReportId);
            });

        await db.ExecuteAsync(
            @"INSERT INTO maintenance_items (company_id, vehicle_id, service_type, title, category, status, priority, due_date, risk_score, recommended_action, data_origin, verification_status)
              VALUES (@companyId, @vehicleId, 'Oil Change', 'Overdue oil change', 'Preventive Maintenance', 'Overdue', 'Critical', CURRENT_DATE - INTERVAL '2 days', 90, 'Schedule service immediately', 'user_workflow', 'recorded_by_authenticated_actor')",
            c =>
            {
                c.Parameters.AddWithValue("@companyId", companyId);
                c.Parameters.AddWithValue("@vehicleId", vehicleId);
            });

        await db.ExecuteAsync(
            @"INSERT INTO maintenance_items (company_id, vehicle_id, service_type, title, category, status, priority, due_date, risk_score, recommended_action, data_origin, verification_status)
              VALUES (@companyId, @vehicleId, 'Brake Inspection', 'Inspection due soon', 'Preventive Maintenance', 'Open', 'High', CURRENT_DATE + INTERVAL '10 days', 78, 'Plan inspection window', 'user_workflow', 'recorded_by_authenticated_actor')",
            c =>
            {
                c.Parameters.AddWithValue("@companyId", companyId);
                c.Parameters.AddWithValue("@vehicleId", vehicleId);
            });

        await db.ExecuteAsync(
            @"INSERT INTO telemetry_live_asset_states
                (company_id, vehicle_id, device_id, driver_id, vehicle_code, device_serial, driver_name, lat, lng, speed_mph, heading, engine_status,
                 telemetry_status, risk_level, alert_count, open_alert_count, stale_seconds, last_event_time, received_at, source_event_id, correlation_id,
                 causation_id, source_channel, next_action, summary_json, updated_at)
              VALUES
                (@companyId, @vehicleId, NULL, @driverId, 'STAGE13B', 'DEV-STAGE13B', 'Driver Stage 13B', 38.9000000, -77.0000000, 0, 90, 'Running',
                 'stale', 'high', 2, 2, 1800, NOW() - INTERVAL '40 minutes', NOW(), 50001, 'stage13b-corr', 'stage13b-cause',
                 'trusted-gateway', 'Investigate stale telemetry', '{}'::jsonb, NOW())",
            c =>
            {
                c.Parameters.AddWithValue("@companyId", companyId);
                c.Parameters.AddWithValue("@vehicleId", vehicleId);
                c.Parameters.AddWithValue("@driverId", driverId);
            });

        await db.ExecuteAsync(
            @"INSERT INTO driver_safety_scores
                (company_id,driver_id,score_7d,score_30d,score_90d,events_7d,events_30d,events_90d,breakdown_json,data_origin,verification_status)
              VALUES (@companyId,@driverId,70,70,70,2,2,2,'{}'::jsonb,'runtime_computed','calculated_from_qualified_sources')
              ON CONFLICT(company_id,driver_id) DO UPDATE SET score_30d=70,events_30d=2,computed_at=NOW(),
                data_origin='runtime_computed',verification_status='calculated_from_qualified_sources'",
            c => { c.Parameters.AddWithValue("@companyId", companyId); c.Parameters.AddWithValue("@driverId", driverId); });
    }

    private static async Task<Dictionary<string, long>> CaptureCountsAsync(Database db, long companyId)
    {
        return new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase)
        {
            ["safety_events"] = await db.ScalarLongAsync("SELECT COUNT(*) FROM safety_events WHERE company_id=@companyId", c => c.Parameters.AddWithValue("@companyId", companyId)),
            ["maintenance_items"] = await db.ScalarLongAsync("SELECT COUNT(*) FROM maintenance_items WHERE company_id=@companyId", c => c.Parameters.AddWithValue("@companyId", companyId)),
            ["telemetry_live_asset_states"] = await db.ScalarLongAsync("SELECT COUNT(*) FROM telemetry_live_asset_states WHERE company_id=@companyId", c => c.Parameters.AddWithValue("@companyId", companyId)),
        };
    }

    private static async Task CleanupTenantAsync(Database db, long companyId)
    {
        await db.ExecuteAsync("DELETE FROM ai_recommendations WHERE tenant_id=@tenantId", c => c.Parameters.AddWithValue("@tenantId", companyId));
        if (await TableExistsAsync(db, "fleet_health_snapshots"))
        {
            await db.ExecuteAsync("DELETE FROM fleet_health_snapshots WHERE company_id=@companyId", c => c.Parameters.AddWithValue("@companyId", companyId));
        }
        await db.ExecuteAsync("DELETE FROM telemetry_live_asset_states WHERE company_id=@companyId", c => c.Parameters.AddWithValue("@companyId", companyId));
        await db.ExecuteAsync("DELETE FROM maintenance_items WHERE company_id=@companyId", c => c.Parameters.AddWithValue("@companyId", companyId));
        await db.ExecuteAsync("DELETE FROM dvir_defects WHERE company_id=@companyId", c => c.Parameters.AddWithValue("@companyId", companyId));
        await db.ExecuteAsync("DELETE FROM dvir_reports WHERE company_id=@companyId", c => c.Parameters.AddWithValue("@companyId", companyId));
        await db.ExecuteAsync("DELETE FROM evidence_package_items WHERE company_id=@companyId", c => c.Parameters.AddWithValue("@companyId", companyId));
        await db.ExecuteAsync("DELETE FROM evidence_packages WHERE company_id=@companyId", c => c.Parameters.AddWithValue("@companyId", companyId));
        await db.ExecuteAsync("DELETE FROM incidents WHERE company_id=@companyId", c => c.Parameters.AddWithValue("@companyId", companyId));
        await db.ExecuteAsync("DELETE FROM driver_safety_scores WHERE company_id=@companyId", c => c.Parameters.AddWithValue("@companyId", companyId));
        await db.ExecuteAsync("DELETE FROM safety_events WHERE company_id=@companyId", c => c.Parameters.AddWithValue("@companyId", companyId));
        await db.ExecuteAsync("DELETE FROM approval_requests WHERE tenant_id=@tenantId", c => c.Parameters.AddWithValue("@tenantId", companyId));
        await db.ExecuteAsync("DELETE FROM ai_action_requests WHERE tenant_id=@tenantId", c => c.Parameters.AddWithValue("@tenantId", companyId));
    }

    private static async Task<bool> TableExistsAsync(Database db, string tableName)
        => await db.ScalarLongAsync(
            @"SELECT COUNT(*)
              FROM information_schema.tables
              WHERE table_schema = current_schema()
                AND table_name = @tableName",
            c => c.Parameters.AddWithValue("@tableName", tableName)) > 0;

    private static async Task<long> GetAnyVehicleIdAsync(Database db)
        => await db.ScalarLongAsync("SELECT id FROM vehicles ORDER BY id LIMIT 1");

    private static async Task<long> GetAnyDriverIdAsync(Database db)
        => await db.ScalarLongAsync("SELECT id FROM drivers ORDER BY id LIMIT 1");
}
