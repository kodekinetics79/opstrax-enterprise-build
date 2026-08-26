using Microsoft.AspNetCore.Http;
using Npgsql;
using Opstrax.Api.Data;
using Opstrax.Api.DTOs;

namespace Opstrax.Api.Controllers;

public static partial class EndpointMappings
{
    private const int FleetOverviewPageSize = 50;

    // Fleet Command is an operational read, not an alternate fleet-master export.
    // It returns one bounded page and full filtered-fleet aggregates so a large tenant
    // never has to load every vehicle merely to render truthful KPIs.
    private static async Task<IResult> LiveOperationsFleetOverview(
        HttpContext http, Database db, CancellationToken ct)
    {
        if (RequirePermission(http, "dashboard:view") is { } dashboardDenied) return dashboardDenied;
        if (RequirePermission(http, "vehicles:view") is { } vehiclesDenied) return vehiclesDenied;

        var query = http.Request.Query;
        var page = int.TryParse(query["page"], out var requestedPage) ? Math.Max(1, requestedPage) : 1;
        var pageSize = int.TryParse(query["pageSize"], out var requestedPageSize)
            ? Math.Clamp(requestedPageSize, 1, 100)
            : FleetOverviewPageSize;
        var search = query["search"].ToString().Trim();
        var status = query["status"].ToString().Trim();
        if (string.IsNullOrEmpty(status)) status = "All";
        var allowedStatuses = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            { "All", "Active", "Idle", "Available", "OOS", "Offline", "Unknown" };
        if (!allowedStatuses.Contains(status))
            return Results.BadRequest(ApiResponse<object>.Fail("Invalid fleet status filter"));

        var sort = query["sort"].ToString().Trim().ToLowerInvariant();
        var orderBy = sort switch
        {
            "driver" => "assigned_driver",
            "status" => "overview_status",
            "readiness" => "readiness_score",
            "device" => "signal_status",
            _ => "vehicle_code",
        };
        var descending = string.Equals(query["order"], "desc", StringComparison.OrdinalIgnoreCase);
        var direction = descending ? "DESC" : "ASC";
        var companyId = GetCompanyId(http);
        var (branchClause, branchId) = StrictBranchFilter(http, "v");
        var canViewDeviceState = RequirePermission(http, "telemetry.devices.read") is null;

        // The overview bucket is deliberately mutually exclusive. In a live-operations
        // surface, an explicitly offline device takes precedence over registry activity;
        // absent connectivity evidence is Unknown, not silently healthy or offline.
        // OOS remains highest priority because it is a dispatch safety restriction.
        var cte = @"
WITH scoped AS (
    SELECT v.id,v.vehicle_code,v.type,v.make,v.model,v.status registry_status,
           v.out_of_service,v.readiness_score,v.risk_score,v.camera_status,
           d.full_name assigned_driver,
           CASE
             WHEN NOT @canViewDeviceState THEN 'Unknown'
             WHEN COALESCE(NULLIF(BTRIM(current_device.device_state),''),NULLIF(BTRIM(v.device_status),'')) IS NULL THEN 'Unknown'
             WHEN LOWER(COALESCE(NULLIF(BTRIM(current_device.device_state),''),NULLIF(BTRIM(v.device_status),'')))='online' THEN 'Online'
             WHEN LOWER(COALESCE(NULLIF(BTRIM(current_device.device_state),''),NULLIF(BTRIM(v.device_status),''))) ~ '(degraded|weak|intermittent)' THEN 'Degraded'
             WHEN LOWER(COALESCE(NULLIF(BTRIM(current_device.device_state),''),NULLIF(BTRIM(v.device_status),''))) ~ '^(offline|inactive|disconnected|revoked|suspended)$' THEN 'Offline'
             ELSE 'Unknown'
           END signal_status
      FROM vehicles v
      LEFT JOIN drivers d ON d.id=v.assigned_driver_id AND d.company_id=v.company_id AND d.deleted_at IS NULL
      LEFT JOIN LATERAL (
        SELECT e.device_state
          FROM device_installations i
          JOIN eld_devices e ON e.id=i.device_id AND e.company_id=i.company_id
         WHERE i.company_id=v.company_id AND i.vehicle_id=v.id AND i.effective_to IS NULL
           AND i.status IN ('Installed','Verified') AND i.device_role IN ('GPS','ELD','OBD-II','J1939/CAN')
         ORDER BY i.is_primary DESC,i.effective_from DESC,i.id DESC LIMIT 1
      ) current_device ON TRUE
     WHERE v.company_id=@cid AND v.deleted_at IS NULL" + branchClause + @"
), classified AS (
    SELECT scoped.*,
           CASE
             WHEN out_of_service IS TRUE OR LOWER(COALESCE(registry_status,'')) ~ '(out.?of.?service|oos|maintenance|repair)' THEN 'OOS'
             WHEN @canViewDeviceState AND signal_status='Offline' THEN 'Offline'
             WHEN @canViewDeviceState AND signal_status='Unknown' THEN 'Unknown'
             WHEN LOWER(COALESCE(registry_status,'')) ~ '(active|on route|driving|in.?transit|dispatched)' THEN 'Active'
             WHEN LOWER(COALESCE(registry_status,'')) ~ '(idle|idling)' THEN 'Idle'
             WHEN LOWER(COALESCE(registry_status,'')) ~ '(available|ready)' THEN 'Available'
             ELSE 'Unknown'
           END overview_status,
           CASE
             WHEN LOWER(COALESCE(registry_status,'')) ~ '(critical)' THEN 'Critical'
             WHEN LOWER(COALESCE(registry_status,'')) ~ '(maintenance|repair|overdue)' THEN 'Overdue'
             WHEN readiness_score IS NULL THEN 'Unknown'
             WHEN readiness_score < 60 THEN 'Overdue'
             WHEN readiness_score < 80 THEN 'Due Soon'
             ELSE 'Healthy'
           END maintenance_status,
           CASE
             WHEN @canViewDeviceState AND LOWER(COALESCE(camera_status,''))='offline' THEN 'Camera offline'
             WHEN out_of_service IS TRUE THEN 'Out of service — do not dispatch'
             WHEN LOWER(COALESCE(registry_status,'')) ~ '(maintenance|repair)' THEN 'In maintenance'
             WHEN risk_score >= 60 THEN CONCAT('Elevated risk score (',ROUND(risk_score),')')
             ELSE NULL
           END flag
      FROM scoped
), searched AS (
    SELECT * FROM classified
     WHERE (@search=''
        OR vehicle_code ILIKE @pattern OR COALESCE(type,'') ILIKE @pattern
        OR COALESCE(make,'') ILIKE @pattern OR COALESCE(model,'') ILIKE @pattern
        OR COALESCE(assigned_driver,'') ILIKE @pattern
        OR overview_status ILIKE @pattern OR signal_status ILIKE @pattern)
)";

        void BindCommon(NpgsqlCommand command)
        {
            command.Parameters.AddWithValue("@cid", companyId);
            if (branchId is not null) command.Parameters.AddWithValue("@branchId", branchId.Value);
            command.Parameters.AddWithValue("@search", search);
            command.Parameters.AddWithValue("@pattern", $"%{search}%");
            command.Parameters.AddWithValue("@canViewDeviceState", canViewDeviceState);
        }

        var summary = await db.QuerySingleAsync(cte + @"
SELECT COUNT(*) total,
       COUNT(*) FILTER (WHERE overview_status='Active') active,
       COUNT(*) FILTER (WHERE overview_status='Idle') idle,
       COUNT(*) FILTER (WHERE overview_status='Available') available,
       COUNT(*) FILTER (WHERE overview_status='OOS') oos,
       COUNT(*) FILTER (WHERE overview_status='Offline') offline,
       COUNT(*) FILTER (WHERE overview_status='Unknown') unknown,
       COUNT(*) FILTER (WHERE signal_status='Online') device_online,
       COUNT(*) FILTER (WHERE signal_status='Degraded') device_degraded,
       COUNT(*) FILTER (WHERE signal_status='Offline') device_offline,
       COUNT(*) FILTER (WHERE signal_status='Unknown') device_unknown,
       COUNT(*) FILTER (WHERE flag IS NOT NULL) flagged,
       COUNT(*) FILTER (WHERE readiness_score > 0) readiness_scored_count,
       ROUND(AVG(readiness_score) FILTER (WHERE readiness_score > 0),1) readiness_average
  FROM searched", BindCommon, ct) ?? new Dictionary<string, object?>();

        var selectedTotal = await db.ScalarLongAsync(cte +
            " SELECT COUNT(*) FROM searched WHERE (@status='All' OR overview_status=@status)",
            command => { BindCommon(command); command.Parameters.AddWithValue("@status", status); }, ct);
        var pageCount = selectedTotal == 0 ? 0 : (int)Math.Ceiling(selectedTotal / (double)pageSize);
        if (pageCount > 0) page = Math.Min(page, pageCount);
        var offset = (page - 1) * pageSize;

        var items = await db.QueryAsync(cte + $@"
SELECT id,vehicle_code,type,make,model,assigned_driver,overview_status status,
       registry_status,signal_status device_status,readiness_score readiness,
       maintenance_status maintenance,risk_score,flag
  FROM searched
 WHERE (@status='All' OR overview_status=@status)
 ORDER BY {orderBy} {direction} NULLS LAST,vehicle_code ASC,id ASC
 LIMIT @limit OFFSET @offset",
            command =>
            {
                BindCommon(command);
                command.Parameters.AddWithValue("@status", status);
                command.Parameters.AddWithValue("@limit", pageSize);
                command.Parameters.AddWithValue("@offset", offset);
            }, ct);

        var lowestReadiness = await db.QuerySingleAsync(cte + @"
SELECT id,vehicle_code,readiness_score readiness
  FROM searched WHERE readiness_score > 0
 ORDER BY readiness_score ASC,vehicle_code ASC,id ASC LIMIT 1", BindCommon, ct);

        return Results.Ok(ApiResponse<object>.Ok(new
        {
            items,
            total = selectedTotal,
            page,
            pageSize,
            pageCount,
            summary,
            lowestReadiness,
        }));
    }
}
