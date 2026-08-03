using System.Text;
using Npgsql;
using Opstrax.Api.Data;
using Opstrax.Api.DTOs;

namespace Opstrax.Api.Controllers;

/// <summary>Read-only, live-operations projection for the Active Shipments module.</summary>
public static class ActiveShipmentsEndpoints
{
    private const int MaxExportRows = 100_000;
    // Jobs is the canonical shipment lifecycle store used by Dispatch, POD and Billing.
    // Terminal/cancelled work is deliberately absent from this operational projection.
    private static readonly HashSet<string> ActiveStatuses =
        new(["Unassigned", "Scheduled", "Assigned", "En Route", "In Progress", "At Stop", "Completed", "Delayed", "At Risk", "Exception"], StringComparer.Ordinal);
    private static readonly HashSet<string> RiskFilters =
        new(["OnTrack", "AtRisk", "Breached", "NoEta"], StringComparer.Ordinal);
    private static readonly HashSet<string> AssignmentFilters =
        new(["FullyAssigned", "NeedsResources"], StringComparer.Ordinal);
    private static readonly HashSet<string> ReadinessFilters =
        new(["PodReady", "PodPending", "InvoiceReady", "InvoiceBlocked"], StringComparer.Ordinal);

    public static void MapActiveShipmentsEndpoints(this WebApplication app)
    {
        app.MapGet("/api/fleet-tms/active-shipments", List);
        app.MapGet("/api/fleet-tms/active-shipments/export", Export);
    }

    private static long CompanyId(HttpContext http) => EndpointMappings.GetCompanyId(http);
    private static long? BranchId(HttpContext http) => EndpointMappings.GetBranchId(http);
    private static IResult Ok(object data) => Results.Ok(ApiResponse<object>.Ok(data));
    private static IResult Bad(string message) => Results.BadRequest(ApiResponse<object>.Fail(message));

    private static IResult? RequireDirect(HttpContext http, params string[] accepted)
    {
        if (!http.Items.TryGetValue(EndpointMappings.AuthUserIdItemKey, out var userId) || userId is null ||
            !http.Items.TryGetValue(EndpointMappings.AuthCompanyIdItemKey, out var companyId) || companyId is null ||
            !long.TryParse(companyId.ToString(), out var parsedCompanyId) || parsedCompanyId <= 0 ||
            !http.Items.TryGetValue(EndpointMappings.AuthRoleItemKey, out var role) || string.IsNullOrWhiteSpace(role?.ToString()) ||
            http.Items[EndpointMappings.AuthPermissionsItemKey] is not string[] permissions)
            return Results.Json(ApiResponse<object>.Fail("Unauthorized", "Missing tenant, user, role, or permission context"),
                statusCode: StatusCodes.Status401Unauthorized);
        if (EndpointMappings.IsCustomerPortalPrincipal(http))
            return Results.Json(ApiResponse<object>.Fail("Forbidden", "Customer-portal accounts cannot access internal endpoints"),
                statusCode: StatusCodes.Status403Forbidden);

        var allowed = accepted.Select(Normalize).ToHashSet(StringComparer.OrdinalIgnoreCase);
        return permissions.Any(permission => permission == "*" || allowed.Contains(Normalize(permission)))
            ? null
            : Results.Json(ApiResponse<object>.Fail("Forbidden", $"Explicit {string.Join(" or ", accepted)} permission is required"),
                statusCode: StatusCodes.Status403Forbidden);
    }

    private static string Normalize(string value) => value.Trim().ToLowerInvariant().Replace('.', ':');

    private static string? Clean(string? value, int maxLength)
    {
        var cleaned = value?.Trim();
        return string.IsNullOrEmpty(cleaned) || cleaned.Length > maxLength ? null : cleaned;
    }

    internal static string CsvCell(object? value)
    {
        var text = value is DateTimeOffset dto ? dto.ToString("O") : value?.ToString() ?? "";
        var trimmed = text.AsSpan().TrimStart();
        if (!trimmed.IsEmpty && trimmed[0] is '=' or '+' or '-' or '@') text = "'" + text;
        return $"\"{text.Replace("\"", "\"\"")}\"";
    }

    private const string Projection = """
WITH active_jobs AS (
  SELECT j.*
  FROM jobs j
  WHERE j.company_id=@companyId
    AND (@branchId::bigint IS NULL OR j.branch_id=@branchId)
    AND j.deleted_at IS NULL
    AND LOWER(j.status) NOT IN ('delivered','cancelled','canceled')
), enriched AS (
  SELECT j.id,j.company_id,j.branch_id,
         COALESCE(j.job_number,j.job_code) shipment_number,
         COALESCE(c.name,'Unassigned customer') customer_name,
         COALESCE(j.pickup_address,'') origin,
         COALESCE(j.dropoff_address,'') destination,
         COALESCE(j.dropoff_address,'') city,
         j.status,j.priority,j.job_type mode,
         NULL::integer piece_count,NULL::numeric weight_kg,NULL::numeric volume_cbm,''::text carrier_name,
         COALESCE(d.full_name,'') driver_name,COALESCE(v.vehicle_code,'') vehicle_number,
         COALESCE(r.route_code,'') route_code,
         COALESCE(pod.status,j.proof_status,'Pending') pod_status,
         (LOWER(j.status)='completed' AND LOWER(COALESCE(pod.status,''))='captured' AND LOWER(COALESCE(package.status,''))='validated'
          AND COALESCE(artifacts.evidence_count,0)>0 AND LOWER(COALESCE(billing.status,''))='ready') is_invoice_ready,
         j.scheduled_start pickup_scheduled_at_utc,
         assignment.actual_pickup_at picked_up_at_utc,
         j.created_at created_at_utc,j.updated_at updated_at_utc,
         j.eta eta_utc,
         CASE WHEN j.eta IS NOT NULL THEN 'Persisted job ETA' ELSE 'Unavailable' END eta_source,
         COALESCE(CONCAT(ROUND(tracking.lat::numeric,5),', ',ROUND(tracking.lng::numeric,5)),'') latest_location,
         tracking.event_time last_tracking_at_utc,
         1::integer delivery_stops,
         CASE WHEN LOWER(COALESCE(pod.status,''))='captured' AND LOWER(COALESCE(package.status,''))='validated' AND COALESCE(artifacts.evidence_count,0)>0 THEN 1 ELSE 0 END::integer verified_pods,
         (LOWER(COALESCE(pod.status,''))='captured' AND LOWER(COALESCE(package.status,''))='validated' AND COALESCE(artifacts.evidence_count,0)>0) pod_ready,
         CASE WHEN d.id IS NOT NULL AND v.id IS NOT NULL THEN 'FullyAssigned'
              WHEN d.id IS NOT NULL OR v.id IS NOT NULL OR r.id IS NOT NULL THEN 'Partial'
              ELSE 'Unassigned' END assignment_status,
         j.sla_status,j.risk_score,COALESCE(j.sla_window_end,j.sla_due_at) commitment_utc
  FROM active_jobs j
  LEFT JOIN customers c ON c.id=j.customer_id AND c.company_id=j.company_id
  LEFT JOIN LATERAL (
    SELECT da.driver_id,da.vehicle_id,da.actual_pickup_at
    FROM dispatch_assignments da
    WHERE da.company_id=j.company_id AND da.job_id=j.id
      AND da.branch_id IS NOT DISTINCT FROM j.branch_id
      AND LOWER(COALESCE(da.assignment_status,da.status,'')) NOT IN ('delivered','cancelled','canceled')
    ORDER BY COALESCE(da.updated_at,da.assigned_at,da.created_at) DESC,da.id DESC LIMIT 1
  ) assignment ON true
  LEFT JOIN drivers d ON d.id=COALESCE(assignment.driver_id,j.assigned_driver_id) AND d.company_id=j.company_id
    AND d.branch_id IS NOT DISTINCT FROM j.branch_id AND d.deleted_at IS NULL
  LEFT JOIN vehicles v ON v.id=COALESCE(assignment.vehicle_id,j.assigned_vehicle_id) AND v.company_id=j.company_id
    AND v.branch_id IS NOT DISTINCT FROM j.branch_id AND v.deleted_at IS NULL
  LEFT JOIN routes r ON r.id=j.route_id AND r.company_id=j.company_id
    AND r.branch_id IS NOT DISTINCT FROM j.branch_id AND r.deleted_at IS NULL
  LEFT JOIN LATERAL (
    SELECT le.lat,le.lng,le.event_time
    FROM location_events le
    WHERE le.company_id=j.company_id AND le.vehicle_id=v.id
    ORDER BY le.event_time DESC,le.id DESC LIMIT 1
  ) tracking ON true
  LEFT JOIN LATERAL (
    SELECT p.id,p.status,p.captured_at
    FROM proof_of_delivery p
    WHERE p.company_id=j.company_id AND p.job_id=j.id
    ORDER BY COALESCE(p.captured_at,p.created_at) DESC,p.id DESC LIMIT 1
  ) pod ON true
  LEFT JOIN LATERAL (
    SELECT pp.id,pp.status
    FROM proof_packages pp
    WHERE pp.company_id=j.company_id AND pp.job_id=j.id AND pp.proof_type='proof_of_delivery'
      AND pp.captured_at IS NOT DISTINCT FROM pod.captured_at
    ORDER BY pp.created_at DESC,pp.id DESC LIMIT 1
  ) package ON true
  LEFT JOIN LATERAL (
    SELECT COUNT(*)::integer evidence_count
    FROM proof_artifacts pa
    JOIN documents doc ON doc.id=pa.file_id AND doc.company_id=pa.company_id AND doc.deleted_at IS NULL
    WHERE pa.company_id=j.company_id AND pa.proof_package_id=package.id
  ) artifacts ON true
  LEFT JOIN LATERAL (
    SELECT bc.status
    FROM billing_confidence_records bc
    WHERE bc.company_id=j.company_id AND bc.job_id=j.id AND bc.proof_package_id=package.id
    ORDER BY bc.updated_at DESC,bc.id DESC LIMIT 1
  ) billing ON true
), projected AS (
  SELECT enriched.*,
         CASE WHEN LOWER(COALESCE(sla_status,'')) IN ('breached','missed') OR (commitment_utc IS NOT NULL AND commitment_utc<@asOf) THEN 'Breached'
              WHEN LOWER(COALESCE(sla_status,''))='at risk' OR risk_score>=70 OR (eta_utc IS NOT NULL AND commitment_utc IS NOT NULL AND eta_utc>commitment_utc) THEN 'AtRisk'
              WHEN eta_utc IS NULL THEN 'NoEta'
              ELSE 'OnTrack' END risk_status,
         CASE WHEN eta_utc IS NULL THEN NULL ELSE FLOOR(EXTRACT(EPOCH FROM (eta_utc-@asOf))/60)::bigint END minutes_to_eta,
         CASE WHEN last_tracking_at_utc IS NULL THEN 'NoTracking'
              WHEN last_tracking_at_utc >= @asOf-INTERVAL '5 minutes' THEN 'Live'
              WHEN last_tracking_at_utc >= @asOf-INTERVAL '1 hour' THEN 'Delayed' ELSE 'Stale' END tracking_freshness
  FROM enriched
)
""";

    private sealed record QuerySpec(string Where, string OrderBy, DateTimeOffset AsOf, Action<NpgsqlCommand> Bind);

    private static (IResult? Error, QuerySpec? Spec) BuildQuery(HttpContext http, string? lifecycle, string? risk,
        string? assignment, string? readiness, string? search, string? sort, string? direction)
    {
        var rawLifecycle = lifecycle;
        var rawRisk = risk;
        var rawAssignment = assignment;
        var rawReadiness = readiness;
        var rawSearch = search;
        var rawSort = sort;
        var rawDirection = direction;
        lifecycle = Clean(lifecycle, 40);
        risk = Clean(risk, 40);
        assignment = Clean(assignment, 40);
        readiness = Clean(readiness, 40);
        search = Clean(search, 120);
        sort = Clean(sort, 20) ?? "risk";
        direction = Clean(direction, 5)?.ToLowerInvariant() ?? "asc";
        if (!string.IsNullOrWhiteSpace(rawLifecycle) && lifecycle is null) return (Bad("Lifecycle filter cannot exceed 40 characters."), null);
        if (!string.IsNullOrWhiteSpace(rawRisk) && risk is null) return (Bad("Risk filter cannot exceed 40 characters."), null);
        if (!string.IsNullOrWhiteSpace(rawAssignment) && assignment is null) return (Bad("Assignment filter cannot exceed 40 characters."), null);
        if (!string.IsNullOrWhiteSpace(rawReadiness) && readiness is null) return (Bad("Readiness filter cannot exceed 40 characters."), null);
        if (lifecycle is not null && lifecycle != "All" && !ActiveStatuses.Contains(lifecycle)) return (Bad("Invalid lifecycle filter."), null);
        if (risk is not null && risk != "All" && !RiskFilters.Contains(risk)) return (Bad("Invalid risk filter."), null);
        if (assignment is not null && assignment != "All" && !AssignmentFilters.Contains(assignment)) return (Bad("Invalid assignment filter."), null);
        if (readiness is not null && readiness != "All" && !ReadinessFilters.Contains(readiness)) return (Bad("Invalid readiness filter."), null);
        if (!string.IsNullOrWhiteSpace(rawSearch) && search is null) return (Bad("Search cannot exceed 120 characters."), null);
        if (!string.IsNullOrWhiteSpace(rawSort) && Clean(rawSort, 20) is null) return (Bad("Sort field cannot exceed 20 characters."), null);
        if (!string.IsNullOrWhiteSpace(rawDirection) && Clean(rawDirection, 5) is null) return (Bad("Sort direction cannot exceed 5 characters."), null);
        if (sort is not ("risk" or "eta" or "created" or "priority")) return (Bad("Invalid sort field."), null);
        if (direction is not ("asc" or "desc")) return (Bad("Invalid sort direction."), null);

        var clauses = new List<string>();
        if (lifecycle is not null && lifecycle != "All") clauses.Add("status=@lifecycle");
        if (risk is not null && risk != "All") clauses.Add("risk_status=@risk");
        if (assignment == "FullyAssigned") clauses.Add("assignment_status='FullyAssigned'");
        if (assignment == "NeedsResources") clauses.Add("assignment_status<>'FullyAssigned'");
        if (readiness == "PodReady") clauses.Add("pod_ready");
        if (readiness == "PodPending") clauses.Add("NOT pod_ready");
        if (readiness == "InvoiceReady") clauses.Add("is_invoice_ready");
        if (readiness == "InvoiceBlocked") clauses.Add("NOT is_invoice_ready");
        if (search is not null) clauses.Add("(shipment_number ILIKE @search OR customer_name ILIKE @search OR origin ILIKE @search OR destination ILIKE @search OR driver_name ILIKE @search OR vehicle_number ILIKE @search OR route_code ILIKE @search)");
        var where = clauses.Count == 0 ? "" : " WHERE " + string.Join(" AND ", clauses);
        var orderColumn = sort switch
        {
            "eta" => "eta_utc",
            "created" => "created_at_utc",
            "priority" => "CASE priority WHEN 'Critical' THEN 0 WHEN 'High' THEN 1 WHEN 'Normal' THEN 2 ELSE 3 END",
            _ => "CASE risk_status WHEN 'Breached' THEN 0 WHEN 'AtRisk' THEN 1 WHEN 'NoEta' THEN 2 ELSE 3 END",
        };
        var nulls = sort == "eta" ? " NULLS LAST" : "";
        var orderBy = $" ORDER BY {orderColumn} {direction.ToUpperInvariant()}{nulls},created_at_utc DESC,id DESC";
        var asOf = DateTimeOffset.UtcNow;
        void Bind(NpgsqlCommand command)
        {
            command.Parameters.AddWithValue("@companyId", CompanyId(http));
            command.Parameters.AddWithValue("@branchId", BranchId(http) ?? (object)DBNull.Value);
            command.Parameters.AddWithValue("@asOf", asOf);
            if (lifecycle is not null && lifecycle != "All") command.Parameters.AddWithValue("@lifecycle", lifecycle);
            if (risk is not null && risk != "All") command.Parameters.AddWithValue("@risk", risk);
            if (search is not null) command.Parameters.AddWithValue("@search", $"%{search}%");
        }
        return (null, new QuerySpec(where, orderBy, asOf, Bind));
    }

    private static async Task<IResult> List(HttpContext http, Database db, CancellationToken ct,
        string? lifecycle = null, string? risk = null, string? assignment = null, string? readiness = null,
        string? search = null, string? sort = null, string? direction = null, int page = 1, int pageSize = 25)
    {
        if (RequireDirect(http, "shipments:view") is { } denied) return denied;
        if (page < 1) return Bad("page must be at least 1.");
        if (pageSize is < 1 or > 200) return Bad("pageSize must be between 1 and 200.");
        var built = BuildQuery(http, lifecycle, risk, assignment, readiness, search, sort, direction);
        if (built.Error is not null) return built.Error;
        var spec = built.Spec!;
        var total = await db.ScalarLongAsync(Projection + "SELECT COUNT(*) FROM projected" + spec.Where, spec.Bind, ct);
        var items = await db.QueryAsync(Projection + "SELECT * FROM projected" + spec.Where + spec.OrderBy + " OFFSET @offset LIMIT @limit",
            command => { spec.Bind(command); command.Parameters.AddWithValue("@offset", ((long)page - 1) * pageSize); command.Parameters.AddWithValue("@limit", pageSize); }, ct);
        var summary = await db.QuerySingleAsync(Projection + """
SELECT COUNT(*) active_total,
       COUNT(*) FILTER (WHERE risk_status='Breached') breached,
       COUNT(*) FILTER (WHERE risk_status='AtRisk') at_risk,
       COUNT(*) FILTER (WHERE risk_status='NoEta') no_eta,
       COUNT(*) FILTER (WHERE assignment_status<>'FullyAssigned') needs_resources,
       COUNT(*) FILTER (WHERE NOT pod_ready) pod_pending,
       COUNT(*) FILTER (WHERE NOT is_invoice_ready) invoice_blocked
FROM projected
""" + spec.Where, spec.Bind, ct);
        return Ok(new { total, page, pageSize, generatedAtUtc = spec.AsOf, summary, items });
    }

    private static async Task<IResult> Export(HttpContext http, Database db, CancellationToken ct,
        string? lifecycle = null, string? risk = null, string? assignment = null, string? readiness = null,
        string? search = null, string? sort = null, string? direction = null)
    {
        if (RequireDirect(http, "shipments:export") is { } denied) return denied;
        var built = BuildQuery(http, lifecycle, risk, assignment, readiness, search, sort, direction);
        if (built.Error is not null) return built.Error;
        var spec = built.Spec!;
        var rows = await db.QueryAsync(Projection + "SELECT * FROM projected" + spec.Where + spec.OrderBy + $" LIMIT {MaxExportRows + 1}", spec.Bind, ct);
        if (rows.Count > MaxExportRows)
            return Results.Json(ApiResponse<object>.Fail("Export too large", $"Refine the filters to {MaxExportRows:N0} rows or fewer."),
                statusCode: StatusCodes.Status413PayloadTooLarge);
        var columns = new[] { "shipmentNumber", "customerName", "origin", "destination", "status", "priority", "etaUtc", "etaSource", "riskStatus", "driverName", "vehicleNumber", "routeCode", "assignmentStatus", "podStatus", "podReady", "isInvoiceReady", "latestLocation", "trackingFreshness" };
        var csv = new StringBuilder().AppendLine(string.Join(',', columns));
        foreach (var row in rows) csv.AppendLine(string.Join(',', columns.Select(column => CsvCell(row.GetValueOrDefault(column)))));
        return Results.File(Encoding.UTF8.GetBytes(csv.ToString()), "text/csv", $"active-shipments-{DateTime.UtcNow:yyyyMMddHHmmss}.csv");
    }
}
