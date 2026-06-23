using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Opstrax.Api.Data;

namespace Opstrax.Api.Services;

// Runs every 15 minutes to:
//  1. Evaluate PM rules against vehicle odometer/engine_hours/last service date.
//  2. Generate maintenance_items entries when PM thresholds are reached.
//  3. Update vehicle availability based on open critical defects and WO status.
public sealed class MaintenanceBackgroundService(
    Database db, ILogger<MaintenanceBackgroundService> log, ServiceRunTracker tracker)
    : BackgroundService
{
    private static readonly TimeSpan Interval = TimeSpan.FromMinutes(15);
    private const string SvcName = "MaintenanceBackgroundService";

    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        await Task.Delay(TimeSpan.FromSeconds(30), ct);
        while (!ct.IsCancellationRequested)
        {
            var sw    = System.Diagnostics.Stopwatch.StartNew();
            var runId = await tracker.BeginAsync(SvcName, ct);
            try
            {
                await RunCycleAsync(ct);
                sw.Stop();
                await tracker.CompleteAsync(runId, SvcName, 0, (int)sw.ElapsedMilliseconds, ct);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                sw.Stop();
                log.LogError(ex, "{Svc} cycle failed", SvcName);
                await tracker.FailAsync(runId, SvcName, ex, (int)sw.ElapsedMilliseconds, ct);
            }
            await Task.Delay(Interval, ct);
        }
    }

    private async Task RunCycleAsync(CancellationToken ct)
    {
        await EvaluatePmRulesAsync(ct);
        await UpdateVehicleAvailabilityAsync(ct);
    }

    // ── PM Rule evaluation ────────────────────────────────────────────────────────
    // For each active vehicle + enabled PM rule combination, check whether service
    // is due (within warning threshold) or overdue. Generate a maintenance_items
    // entry if one doesn't already exist for this cycle.
    private async Task EvaluatePmRulesAsync(CancellationToken ct)
    {
        var rules = await db.QueryAsync(
            @"SELECT r.*, c.id AS company_id_val
              FROM maintenance_pm_rules r
              JOIN companies c ON c.id=r.company_id
              WHERE r.enabled=1",
            ct: ct);

        foreach (var rule in rules)
        {
            var companyId   = Convert.ToInt64(rule["companyId"]);
            var ruleId      = Convert.ToInt64(rule["id"]);
            var serviceType = rule["serviceType"]?.ToString() ?? "Service";
            var triggerType = rule["triggerType"]?.ToString() ?? "mileage";
            var priority    = rule["priority"]?.ToString() ?? "Medium";
            var estCost     = rule["estimatedCost"] is null ? (decimal?)null : Convert.ToDecimal(rule["estimatedCost"]);
            var vehicleClass = rule["vehicleClass"]?.ToString();

            // Vehicles in scope (matching vehicle_class if set, not deleted, not in maintenance already)
            var vehicles = await db.QueryAsync(
                @"SELECT v.id, v.vehicle_code, v.type AS vehicle_type,
                         v.odometer_miles, v.engine_hours,
                         (SELECT MAX(mi.updated_at) FROM maintenance_items mi
                          WHERE mi.vehicle_id=v.id AND mi.service_type=@stype
                            AND mi.status IN ('Completed','Closed') LIMIT 1) AS last_service_at,
                         (SELECT MAX(mi.odometer_miles) FROM maintenance_items mi
                          WHERE mi.vehicle_id=v.id AND mi.service_type=@stype
                            AND mi.status IN ('Completed','Closed') LIMIT 1) AS last_service_odo
                  FROM vehicles v
                  WHERE v.company_id=@cid AND v.deleted_at IS NULL
                    AND (@vclass IS NULL OR v.type=@vclass)",
                c =>
                {
                    c.Parameters.AddWithValue("@cid",    companyId);
                    c.Parameters.AddWithValue("@stype",  serviceType);
                    c.Parameters.AddWithValue("@vclass", vehicleClass ?? (object)DBNull.Value);
                }, ct);

            foreach (var v in vehicles)
            {
                var vehicleId   = Convert.ToInt64(v["id"]);
                var currentOdo  = v["odometerMiles"]   is null ? (decimal?)null : Convert.ToDecimal(v["odometerMiles"]);
                var engineHrs   = v["engineHours"]     is null ? (decimal?)null : Convert.ToDecimal(v["engineHours"]);
                var lastSvcAt   = v["lastServiceAt"]   is null ? (DateTime?)null : Convert.ToDateTime(v["lastServiceAt"]);
                var lastSvcOdo  = v["lastServiceOdo"]  is null ? (decimal?)null : Convert.ToDecimal(v["lastServiceOdo"]);

                bool isDue = false;
                bool isOverdue = false;
                string? dueReason = null;

                if (triggerType == "mileage" && rule["intervalMiles"] is not null && currentOdo.HasValue)
                {
                    var interval   = Convert.ToInt32(rule["intervalMiles"]);
                    var warnPct    = Convert.ToInt32(rule["warningThresholdPct"] ?? 10);
                    var baseOdo    = lastSvcOdo ?? 0m;
                    var nextDueOdo = baseOdo + interval;
                    var warnOdo    = nextDueOdo - (interval * warnPct / 100m);

                    if (currentOdo >= nextDueOdo) { isOverdue = true; dueReason = $"Odometer {currentOdo:N0} mi >= due at {nextDueOdo:N0} mi"; }
                    else if (currentOdo >= warnOdo) { isDue = true; dueReason = $"Odometer {currentOdo:N0} mi approaching {nextDueOdo:N0} mi service interval"; }
                }
                else if (triggerType == "engine_hours" && rule["intervalEngineHours"] is not null && engineHrs.HasValue)
                {
                    var interval   = Convert.ToInt32(rule["intervalEngineHours"]);
                    var warnPct    = Convert.ToInt32(rule["warningThresholdPct"] ?? 10);
                    var nextDueHrs = (lastSvcAt.HasValue ? 0 : 0) + interval;
                    var warnHrs    = nextDueHrs - (interval * warnPct / 100m);

                    if (engineHrs >= nextDueHrs) { isOverdue = true; dueReason = $"Engine hours {engineHrs:N1} >= due at {nextDueHrs:N0}"; }
                    else if (engineHrs >= warnHrs) { isDue = true; dueReason = $"Engine hours {engineHrs:N1} approaching {nextDueHrs:N0} hr interval"; }
                }
                else if (triggerType == "days" && rule["intervalDays"] is not null)
                {
                    var interval  = Convert.ToInt32(rule["intervalDays"]);
                    var warnPct   = Convert.ToInt32(rule["warningThresholdPct"] ?? 10);
                    var baseDt    = lastSvcAt ?? DateTime.UtcNow.AddDays(-interval);
                    var nextDue   = baseDt.AddDays(interval);
                    var warnDate  = nextDue.AddDays(-(interval * warnPct / 100));

                    if (DateTime.UtcNow >= nextDue) { isOverdue = true; dueReason = $"Service due date {nextDue:yyyy-MM-dd} passed"; }
                    else if (DateTime.UtcNow >= warnDate) { isDue = true; dueReason = $"Service due {nextDue:yyyy-MM-dd} approaching"; }
                }

                if (!isDue && !isOverdue) continue;

                // Skip if an open PM item already exists for this vehicle+service.
                var existing = await db.ScalarLongAsync(
                    @"SELECT COUNT(*) FROM maintenance_items
                      WHERE company_id=@cid AND vehicle_id=@vid AND service_type=@stype
                        AND status NOT IN ('Completed','Cancelled','Closed','Deleted') AND deleted_at IS NULL",
                    c =>
                    {
                        c.Parameters.AddWithValue("@cid",   companyId);
                        c.Parameters.AddWithValue("@vid",   vehicleId);
                        c.Parameters.AddWithValue("@stype", serviceType);
                    }, ct);
                if (existing > 0) continue;

                var status = isOverdue ? "Overdue" : "Open";
                var itemPriority = isOverdue ? "Critical" : priority;
                var riskScore    = isOverdue ? 85m : 55m;

                var itemId = await db.InsertAsync(
                    @"INSERT INTO maintenance_items
                        (company_id, vehicle_id, service_type, title, category, priority, status,
                         due_date, estimated_cost, risk_score, description, recommended_action)
                      VALUES (@cid, @vid, @stype, @title, 'Preventive Maintenance', @pri, @status,
                              CURDATE(), @cost, @risk, @desc, @action)",
                    c =>
                    {
                        c.Parameters.AddWithValue("@cid",    companyId);
                        c.Parameters.AddWithValue("@vid",    vehicleId);
                        c.Parameters.AddWithValue("@stype",  serviceType);
                        c.Parameters.AddWithValue("@title",  $"{serviceType} — {v["vehicleCode"]}");
                        c.Parameters.AddWithValue("@pri",    itemPriority);
                        c.Parameters.AddWithValue("@status", status);
                        c.Parameters.AddWithValue("@cost",   estCost ?? (object)DBNull.Value);
                        c.Parameters.AddWithValue("@risk",   riskScore);
                        c.Parameters.AddWithValue("@desc",   dueReason);
                        c.Parameters.AddWithValue("@action", $"Schedule {serviceType} immediately" + (isOverdue ? " — OVERDUE" : ""));
                    }, ct);

                log.LogInformation("[MaintBgSvc] PM item {Id} created: {ServiceType} for vehicle {VehicleId} ({Reason})", itemId, serviceType, vehicleId, dueReason);
            }
        }
    }

    // ── Vehicle availability update ────────────────────────────────────────────────
    // Critical open defects → out_of_service=1, availability_status='out_of_service'.
    // Work orders in-progress or waiting_parts → availability_status='in_maintenance'.
    // No critical defects + no blocking WO → availability_status='available'.
    // Restoring availability requires BOTH conditions: no critical open defect AND
    // no open WO in in_progress/waiting_parts state.
    internal static async Task UpdateVehicleAvailabilityAsync(Database db, CancellationToken ct)
    {
        // Step 1 — Mark out-of-service where critical open defects exist.
        await db.ExecuteAsync(
            @"UPDATE vehicles v
              SET v.out_of_service=1, v.availability_status='out_of_service'
              WHERE v.deleted_at IS NULL
                AND EXISTS (
                    SELECT 1 FROM dvir_defects dd
                    WHERE dd.vehicle_id=v.id
                      AND dd.out_of_service=1
                      AND dd.status NOT IN ('resolved','rejected','Resolved','Rejected')
                    LIMIT 1
                )",
            ct: ct);

        // Step 2 — Mark in_maintenance where open work orders exist (but not out-of-service).
        await db.ExecuteAsync(
            @"UPDATE vehicles v
              SET v.availability_status='in_maintenance'
              WHERE v.out_of_service=0 AND v.deleted_at IS NULL
                AND EXISTS (
                    SELECT 1 FROM work_orders wo
                    WHERE wo.vehicle_id=v.id
                      AND wo.company_id=v.company_id
                      AND wo.status IN ('in_progress','waiting_parts','In Progress','Waiting Parts')
                      AND (wo.deleted_at IS NULL OR wo.deleted_at='0000-00-00 00:00:00')
                    LIMIT 1
                )",
            ct: ct);

        // Step 3 — Restore to available when conditions are clear.
        await db.ExecuteAsync(
            @"UPDATE vehicles v
              SET v.out_of_service=0, v.availability_status='available'
              WHERE v.deleted_at IS NULL
                AND v.availability_status IN ('out_of_service','in_maintenance')
                AND NOT EXISTS (
                    SELECT 1 FROM dvir_defects dd
                    WHERE dd.vehicle_id=v.id AND dd.out_of_service=1
                      AND dd.status NOT IN ('resolved','rejected','Resolved','Rejected')
                )
                AND NOT EXISTS (
                    SELECT 1 FROM work_orders wo
                    WHERE wo.vehicle_id=v.id AND wo.company_id=v.company_id
                      AND wo.status IN ('in_progress','waiting_parts','In Progress','Waiting Parts')
                      AND (wo.deleted_at IS NULL OR wo.deleted_at='0000-00-00 00:00:00')
                )",
            ct: ct);

        // Step 4 — Hold active dispatch assignments whose vehicle is now OOS.
        await TriggerDispatchHoldForOosVehiclesAsync(db, ct);
    }

    // Detect active dispatch assignments on OOS vehicles and create maintenance_hold exceptions.
    // Idempotent: duplicate open maintenance_hold exceptions for the same assignment are prevented.
    internal static async Task TriggerDispatchHoldForOosVehiclesAsync(Database db, CancellationToken ct)
    {
        var affected = await db.QueryAsync(
            @"SELECT da.id, da.company_id, da.vehicle_id, da.assignment_status
              FROM dispatch_assignments da
              JOIN vehicles v ON v.id = da.vehicle_id AND v.out_of_service = 1
              WHERE da.assignment_status NOT IN ('delivered', 'cancelled')
                AND NOT EXISTS (
                    SELECT 1 FROM dispatch_exceptions de
                    WHERE de.assignment_id = da.id
                      AND de.exception_type = 'maintenance_hold'
                      AND de.status = 'open'
                )",
            ct: ct);

        foreach (var row in affected)
        {
            var assignmentId = Convert.ToInt64(row["id"]);
            var companyId    = Convert.ToInt64(row["companyId"]);
            var prevStatus   = row["assignmentStatus"]?.ToString() ?? "assigned";

            await db.ExecuteAsync(
                @"INSERT INTO dispatch_exceptions
                    (company_id, assignment_id, exception_type, severity, status, title, notes)
                  VALUES
                    (@companyId, @assignmentId, 'maintenance_hold', 'Critical', 'open',
                     'Vehicle placed out of service',
                     'System-generated: vehicle marked out-of-service by maintenance engine. Resolve critical defects or reassign to an available vehicle.')",
                c =>
                {
                    c.Parameters.AddWithValue("@companyId", companyId);
                    c.Parameters.AddWithValue("@assignmentId", assignmentId);
                }, ct);

            await db.ExecuteAsync(
                @"UPDATE dispatch_assignments
                  SET assignment_status = 'exception',
                      previous_status   = @prevStatus,
                      exception_count   = exception_count + 1,
                      updated_at        = NOW()
                  WHERE id = @id
                    AND assignment_status NOT IN ('delivered', 'cancelled')",
                c =>
                {
                    c.Parameters.AddWithValue("@id", assignmentId);
                    c.Parameters.AddWithValue("@prevStatus", prevStatus);
                }, ct);

            await db.ExecuteAsync(
                @"INSERT INTO audit_logs
                    (company_id, actor_user_id, actor_name, action_name, entity_name, entity_id, details_json)
                  VALUES
                    (@companyId, NULL, 'system', 'dispatch.assignment.maintenance_hold',
                     'DispatchAssignment', @assignmentId,
                     JSON_OBJECT('trigger', 'vehicle_oos', 'previousStatus', @prevStatus))",
                c =>
                {
                    c.Parameters.AddWithValue("@companyId", companyId);
                    c.Parameters.AddWithValue("@assignmentId", assignmentId);
                    c.Parameters.AddWithValue("@prevStatus", prevStatus);
                }, ct);
        }
    }

    private async Task UpdateVehicleAvailabilityAsync(CancellationToken ct)
        => await UpdateVehicleAvailabilityAsync(db, ct);
}
