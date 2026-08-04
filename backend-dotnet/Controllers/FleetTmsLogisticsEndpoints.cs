using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Npgsql;
using Opstrax.Api.Data;
using Opstrax.Api.DTOs;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Opstrax.Api.Controllers;

// Fleet TMS (PR3) logistics endpoints — last-mile dispatch orders, delivery routes and
// stops, ported from the Zayra LogisticsController onto raw Npgsql + minimal API.
// Re-namespaced from /api/logistics/* to /api/fleet-tms/logistics/* to keep all ported
// work under the approved additive namespace. Company-scoped; orders/routes/stops are
// linked denormally by order_number/route_code (matching the source model).
public static class FleetTmsLogisticsEndpoints
{
    public static void MapFleetTmsLogisticsEndpoints(this WebApplication app)
    {
        app.MapGet("/api/fleet-tms/logistics/overview", Overview);
        app.MapGet("/api/fleet-tms/logistics/orders", Orders);
        app.MapGet("/api/fleet-tms/logistics/orders/{id:long}", Order);
        app.MapPost("/api/fleet-tms/logistics/orders", CreateOrder);
        app.MapPut("/api/fleet-tms/logistics/orders/{id:long}", UpdateOrder);
        app.MapGet("/api/fleet-tms/logistics/routes", Routes);
        app.MapPost("/api/fleet-tms/logistics/routes", CreateRoute);
        app.MapPut("/api/fleet-tms/logistics/routes/{id:long}", UpdateRoute);
        app.MapGet("/api/fleet-tms/logistics/routes/{id:long}/stops", RouteStops);
        app.MapGet("/api/fleet-tms/logistics/last-mile", LastMile);
        app.MapGet("/api/fleet-tms/logistics/last-mile/export", ExportLastMile);
        app.MapPost("/api/fleet-tms/logistics/orders/{id:long}/dispatch", DispatchOrder);
        app.MapPost("/api/fleet-tms/logistics/routes/{id:long}/progress", ProgressRoute);
        app.MapPost("/api/fleet-tms/logistics/stops/{id:long}/deliver", ConfirmDelivery);
        app.MapPost("/api/fleet-tms/logistics/stops/{id:long}/attempt", RecordAttempt);
        app.MapPost("/api/fleet-tms/logistics/stops/{id:long}/reschedule", RescheduleStop);
    }

    private static long Cid(HttpContext http) => EndpointMappings.GetCompanyId(http);
    private static long? Bid(HttpContext http) => EndpointMappings.GetBranchId(http);
    private static IResult Ok<T>(T data) => Results.Ok(ApiResponse<object>.Ok(data!));
    private static IResult NotFound(string m = "Not found") => Results.NotFound(ApiResponse<object>.Fail(m));
    private static IResult Bad(string m) => Results.BadRequest(ApiResponse<object>.Fail(m));
    private static object S(string? v) => (object?)v ?? DBNull.Value;
    private static object N(decimal? v) => (object?)v ?? DBNull.Value;
    private static object I(int? v) => (object?)v ?? DBNull.Value;
    private static object Dt(DateTime? v) => (object?)v ?? DBNull.Value;

    private static IResult? RequireView(HttpContext http) => EndpointMappings.RequirePermission(http, "dispatch:view");
    private static IResult? RequireManage(HttpContext http) => RequireExplicit(http, "dispatch:update");
    private static IResult? RequireExplicit(HttpContext http, string permission, params string[] alternatives)
    {
        var permissions = http.Items[EndpointMappings.AuthPermissionsItemKey] as string[] ?? [];
        var accepted = alternatives.Append(permission).Append("dispatch:manage")
            .Select(value => value.Trim().ToLowerInvariant().Replace('.', ':'))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (permissions.Any(value => value == "*" || accepted.Contains(value.Trim().ToLowerInvariant().Replace('.', ':')))) return null;
        if (EndpointMappings.RequirePermission(http, permission) is { } denied) return denied;
        return Results.Json(ApiResponse<object>.Fail("Forbidden", $"Explicit {permission} permission is required"), statusCode: StatusCodes.Status403Forbidden);
    }

    private static void BindScope(NpgsqlCommand command, long companyId, long? branchId)
    {
        command.Parameters.AddWithValue("@companyId", companyId);
        command.Parameters.AddWithValue("@branchId", branchId ?? (object)DBNull.Value);
    }

    private static async Task<Dictionary<string, object?>?> Row(Database db, string table, long companyId, long? branchId, long id, CancellationToken ct, bool forUpdate = false)
        => await db.QuerySingleAsync($"SELECT * FROM {table} WHERE id=@id AND company_id=@companyId AND (@branchId::BIGINT IS NULL OR branch_id=@branchId){(forUpdate ? " FOR UPDATE" : "")}",
            c => { c.Parameters.AddWithValue("@id", id); BindScope(c, companyId, branchId); }, ct);

    private static Task<Dictionary<string, object?>?> Row(Database db, string table, long companyId, long id, CancellationToken ct)
        => Row(db, table, companyId, null, id, ct);

    private static string? Clean(string? value, int maxLength)
    {
        var cleaned = value?.Trim();
        return string.IsNullOrEmpty(cleaned) || cleaned.Length > maxLength ? null : cleaned;
    }

    internal static string LastMileCsvCell(object? value)
    {
        var text = value is DateTimeOffset dto ? dto.ToString("O") : value?.ToString() ?? "";
        var trimmed = text.AsSpan().TrimStart();
        if (!trimmed.IsEmpty && trimmed[0] is '=' or '+' or '-' or '@') text = "'" + text;
        return $"\"{text.Replace("\"", "\"\"")}\"";
    }

    private static bool ValidStopStatus(string? status) => status is "OutForDelivery" or "Attempted" or "Failed" or "Rescheduled" or "Delivered";
    private static bool TerminalStop(string? status) => status == "Delivered";
    private static bool ValidAttemptProofStatus(string? status) => string.IsNullOrWhiteSpace(status) ||
        status is "None" or "Pending" or "Captured" or "Verified" or "POD" or "Rejected" or "Not Required";
    private static bool ValidOrderStatus(string? status) => status is "Queued" or "Dispatched" or "InTransit" or "Exception" or "Delivered" or "Returned";
    private static bool ValidRouteStatus(string? status) => status is "Planned" or "Ready" or "Active" or "Delayed" or "Closed" or "Completed";

    private static string ActionRequestHash<T>(T request)
        => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(request)))).ToLowerInvariant();

    private static async Task<(bool Seen, bool SameRequest)> ActionReplay(Database db, long companyId,
        string operation, string key, string requestHash, CancellationToken ct)
    {
        var existing = await db.QuerySingleAsync(
            "SELECT request_hash FROM idempotency_keys WHERE tenant_id=@companyId AND operation=@operation AND idempotency_key=@key",
            c => { c.Parameters.AddWithValue("@companyId", companyId); c.Parameters.AddWithValue("@operation", operation); c.Parameters.AddWithValue("@key", key); }, ct);
        return existing is null
            ? (false, false)
            : (true, string.Equals(existing["requestHash"]?.ToString(), requestHash, StringComparison.OrdinalIgnoreCase));
    }

    private static Task RecordActionReceipt(Database db, long companyId, string operation, string key,
        string requestHash, long entityId, CancellationToken ct)
        => db.ExecuteAsync(
            @"INSERT INTO idempotency_keys
                (tenant_id,operation,idempotency_key,request_hash,response_reference,status,expires_at,created_at)
              VALUES (@companyId,@operation,@key,@hash,@reference,'completed',NOW()+INTERVAL '10 years',NOW())
              ON CONFLICT (tenant_id,operation,idempotency_key) DO NOTHING",
            c =>
            {
                c.Parameters.AddWithValue("@companyId", companyId);
                c.Parameters.AddWithValue("@operation", operation);
                c.Parameters.AddWithValue("@key", key);
                c.Parameters.AddWithValue("@hash", requestHash);
                c.Parameters.AddWithValue("@reference", entityId.ToString());
            }, ct);

    private static Task<Dictionary<string, object?>?> StopWorkflowGraph(Database db, long companyId, long stopId, CancellationToken ct)
        => db.QuerySingleAsync(
            @"SELECT r.planned_stops,
                     (o.branch_id IS NOT DISTINCT FROM s.branch_id AND r.branch_id IS NOT DISTINCT FROM s.branch_id) branch_consistent
              FROM fleet_tms_last_mile_stops s
              JOIN fleet_tms_dispatch_orders o ON o.company_id=s.company_id AND o.order_number=s.order_number
              JOIN fleet_tms_delivery_routes r ON r.company_id=s.company_id AND r.route_code=s.route_code
              WHERE s.id=@stopId AND s.company_id=@companyId",
            c => { c.Parameters.AddWithValue("@stopId", stopId); c.Parameters.AddWithValue("@companyId", companyId); }, ct);

    private static IResult? StopGraphDenied(Dictionary<string, object?>? graph, bool requirePlannedStops)
    {
        if (graph is null) return Results.Conflict(ApiResponse<object>.Fail("The stop is not linked to exactly one order and route."));
        if (graph["branchConsistent"] is not true)
            return Results.Conflict(ApiResponse<object>.Fail("The stop, order, and route must belong to the same branch."));
        if (requirePlannedStops && Convert.ToInt32(graph["plannedStops"] ?? 0) < 1)
            return Results.Conflict(ApiResponse<object>.Fail("The route must have at least one planned stop before delivery."));
        return null;
    }

    // ── Overview ────────────────────────────────────────────────────────────────

    private static async Task<IResult> Overview(HttpContext http, Database db, CancellationToken ct)
    {
        if (RequireView(http) is { } denied) return denied;
        var companyId = Cid(http);
        var branchId = Bid(http);
        const string scope = "company_id=@companyId AND (@branchId::BIGINT IS NULL OR branch_id=@branchId)";
        void B(NpgsqlCommand c) => BindScope(c, companyId, branchId);
        var summary = new
        {
            activeOrders = await db.ScalarLongAsync($"SELECT COUNT(*) FROM fleet_tms_dispatch_orders WHERE {scope} AND status NOT IN ('Delivered','Returned')", B, ct),
            inTransit = await db.ScalarLongAsync($"SELECT COUNT(*) FROM fleet_tms_dispatch_orders WHERE {scope} AND status IN ('Dispatched','InTransit')", B, ct),
            deliveredToday = await db.ScalarLongAsync($"SELECT COUNT(*) FROM fleet_tms_dispatch_orders WHERE {scope} AND status='Delivered' AND delivered_at_utc >= date_trunc('day', NOW())", B, ct),
            exceptionOrders = await db.ScalarLongAsync($"SELECT COUNT(*) FROM fleet_tms_dispatch_orders WHERE {scope} AND status='Exception'", B, ct),
            activeRoutes = await db.ScalarLongAsync($"SELECT COUNT(*) FROM fleet_tms_delivery_routes WHERE {scope} AND status IN ('Ready','Active','Delayed')", B, ct),
            plannedRoutes = await db.ScalarLongAsync($"SELECT COUNT(*) FROM fleet_tms_delivery_routes WHERE {scope} AND status='Planned'", B, ct),
            delayedRoutes = await db.ScalarLongAsync($"SELECT COUNT(*) FROM fleet_tms_delivery_routes WHERE {scope} AND status='Delayed'", B, ct),
            completedRoutes = await db.ScalarLongAsync($"SELECT COUNT(*) FROM fleet_tms_delivery_routes WHERE {scope} AND status IN ('Closed','Completed')", B, ct),
            averageStopsPerRoute = Math.Round(await db.ScalarDecimalAsync($"SELECT COALESCE(AVG(planned_stops),0) FROM fleet_tms_delivery_routes WHERE {scope}", B, ct) ?? 0m, 1),
            routeEfficiencyScore = Math.Round(await db.ScalarDecimalAsync($"SELECT COALESCE(AVG(completion_percent),0) FROM fleet_tms_delivery_routes WHERE {scope}", B, ct) ?? 0m, 1),
            highRiskRoutes = await db.ScalarLongAsync($"SELECT COUNT(*) FROM fleet_tms_delivery_routes WHERE {scope} AND status='Delayed'", B, ct),
            onTimeRate = Math.Round(await db.ScalarDecimalAsync($"SELECT COALESCE(AVG(completion_percent),0) FROM fleet_tms_delivery_routes WHERE {scope}", B, ct) ?? 0m, 1),
        };
        var routeCards = await db.QueryAsync($"SELECT id, route_code, hub, territory, driver_name, vehicle_number, status, planned_stops, completed_stops, completion_percent, current_stop, next_stop, notes FROM fleet_tms_delivery_routes WHERE {scope} ORDER BY completed_stops DESC, route_code LIMIT 4", B, ct);
        var orderCards = await db.QueryAsync($"SELECT id, order_number, customer_name, city, area, priority, status, route_code, driver_name, vehicle_number, item_count, order_value, promised_at_utc, dispatched_at_utc, delivered_at_utc, dispatch_notes FROM fleet_tms_dispatch_orders WHERE {scope} ORDER BY created_at_utc DESC LIMIT 5", B, ct);
        var alerts = await db.QueryAsync($"SELECT order_number, customer_name, route_code, status, exception_reason, attempt_count, rider_name, eta_utc FROM fleet_tms_last_mile_stops WHERE {scope} AND (status IN ('Attempted','Failed','Rescheduled') OR exception_reason <> '') ORDER BY created_at_utc DESC LIMIT 4", B, ct);
        var liveStops = await db.QueryAsync($"SELECT id, order_number, customer_name, address_line, city, route_code, status, proof_status, rider_name, time_window, attempt_count, eta_utc FROM fleet_tms_last_mile_stops WHERE {scope} ORDER BY eta_utc DESC LIMIT 6", B, ct);
        return Ok(new { generatedAtUtc = DateTime.UtcNow, summary, alerts, routeCards, orderCards, liveStops });
    }

    // ── Orders ──────────────────────────────────────────────────────────────────

    private static async Task<IResult> Orders(HttpContext http, Database db, CancellationToken ct, string? status = null, int page = 1, int pageSize = 50)
    {
        if (RequireView(http) is { } denied) return denied;
        var companyId = Cid(http);
        var branchId = Bid(http);
        page = page < 1 ? 1 : page; pageSize = pageSize is < 1 or > 200 ? 20 : pageSize;
        status = Clean(status, 30);
        if (status is not null && !ValidOrderStatus(status)) return Bad("Invalid order status filter.");
        var where = "WHERE company_id=@companyId AND (@branchId::BIGINT IS NULL OR branch_id=@branchId)" + (status is null ? "" : " AND status=@status");
        void B(NpgsqlCommand c) { BindScope(c, companyId, branchId); if (status is not null) c.Parameters.AddWithValue("@status", status); }
        var total = await db.ScalarLongAsync($"SELECT COUNT(*) FROM fleet_tms_dispatch_orders {where}", B, ct);
        var items = await db.QueryAsync($"SELECT * FROM fleet_tms_dispatch_orders {where} ORDER BY created_at_utc DESC OFFSET @offset LIMIT @limit",
            c => { B(c); c.Parameters.AddWithValue("@offset", (page - 1) * pageSize); c.Parameters.AddWithValue("@limit", pageSize); }, ct);
        return Ok(new { total, page, pageSize, items });
    }

    private static async Task<IResult> Order(HttpContext http, long id, Database db, CancellationToken ct)
    {
        if (RequireView(http) is { } denied) return denied;
        var item = await Row(db, "fleet_tms_dispatch_orders", Cid(http), Bid(http), id, ct);
        return item is null ? NotFound() : Ok(item);
    }

    private static async Task<IResult> CreateOrder(HttpContext http, LogisticsOrderRequest req, Database db, CancellationToken ct)
    {
        if (RequireExplicit(http, "dispatch:create") is { } denied) return denied;
        var orderNumber = Clean(req.OrderNumber, 60);
        var customerName = Clean(req.CustomerName, 255);
        if (orderNumber is null) return Bad("Order number is required and cannot exceed 60 characters.");
        if (customerName is null) return Bad("Customer name is required and cannot exceed 255 characters.");
        var status = Clean(req.Status, 30) ?? "Queued";
        if (!ValidOrderStatus(status) || status != "Queued") return Bad("New orders must start in Queued status.");
        if (req.ItemCount is < 1 or > 100_000) return Bad("Item count must be between 1 and 100000.");
        if (req.OrderValue is < 0 or > 1_000_000_000m) return Bad("Order value must be between 0 and 1000000000.");
        var companyId = Cid(http);
        var branchId = Bid(http);
        long id;
        try { id = await db.InsertAsync(@"
INSERT INTO fleet_tms_dispatch_orders (company_id, branch_id, order_number, customer_name, customer_segment, sales_channel, city, area, status, priority, item_count, order_value, route_code, driver_name, vehicle_number, dispatch_notes, created_at_utc, promised_at_utc, updated_at_utc)
VALUES (@companyId, @branchId, @num, @customer, @segment, @channel, @city, @area, @status, @priority, @items, @value, @route, @driver, @vehicle, @notes, NOW(), @promised, NOW())",
            c =>
            {
                BindScope(c, companyId, branchId);
                c.Parameters.AddWithValue("@num", orderNumber);
                c.Parameters.AddWithValue("@customer", customerName);
                c.Parameters.AddWithValue("@segment", req.CustomerSegment?.Trim() ?? "Retail");
                c.Parameters.AddWithValue("@channel", req.SalesChannel?.Trim() ?? "Portal");
                c.Parameters.AddWithValue("@city", req.City?.Trim() ?? "");
                c.Parameters.AddWithValue("@area", req.Area?.Trim() ?? "");
                c.Parameters.AddWithValue("@status", status);
                c.Parameters.AddWithValue("@priority", req.Priority?.Trim() ?? "Normal");
                c.Parameters.AddWithValue("@items", req.ItemCount ?? 1);
                c.Parameters.AddWithValue("@value", req.OrderValue ?? 0m);
                c.Parameters.AddWithValue("@route", req.RouteCode?.Trim() ?? "");
                c.Parameters.AddWithValue("@driver", req.DriverName?.Trim() ?? "");
                c.Parameters.AddWithValue("@vehicle", req.VehicleNumber?.Trim() ?? "");
                c.Parameters.AddWithValue("@notes", req.DispatchNotes?.Trim() ?? "");
                c.Parameters.AddWithValue("@promised", Dt(req.PromisedAtUtc));
            }, ct); }
        catch (PostgresException ex) when (ex.SqlState == PostgresErrorCodes.UniqueViolation)
        { return Results.Conflict(ApiResponse<object>.Fail("Order number already exists for this company.")); }
        return Ok(await Row(db, "fleet_tms_dispatch_orders", companyId, branchId, id, ct)!);
    }

    private static async Task<IResult> UpdateOrder(HttpContext http, long id, LogisticsOrderRequest req, Database db, CancellationToken ct)
    {
        if (RequireExplicit(http, "dispatch:update") is { } denied) return denied;
        var companyId = Cid(http);
        var branchId = Bid(http);
        var current = await Row(db, "fleet_tms_dispatch_orders", companyId, branchId, id, ct);
        if (current is null) return NotFound();
        if (current["status"]?.ToString() is "Delivered" or "Returned")
            return Results.Conflict(ApiResponse<object>.Fail("A terminal order cannot be edited."));
        if (!string.IsNullOrWhiteSpace(req.Status) && !string.Equals(req.Status.Trim(), current["status"]?.ToString(), StringComparison.Ordinal))
            return Bad("Order status must be changed through a workflow action.");
        if (req.ItemCount is < 1 or > 100_000) return Bad("Item count must be between 1 and 100000.");
        if (req.OrderValue is < 0 or > 1_000_000_000m) return Bad("Order value must be between 0 and 1000000000.");
        var rows = await db.ExecuteAsync(@"
UPDATE fleet_tms_dispatch_orders SET
  customer_name=COALESCE(NULLIF(@customer,''), customer_name), customer_segment=COALESCE(NULLIF(@segment,''), customer_segment),
  sales_channel=COALESCE(NULLIF(@channel,''), sales_channel), city=COALESCE(NULLIF(@city,''), city), area=COALESCE(NULLIF(@area,''), area),
  status=COALESCE(NULLIF(@status,''), status), priority=COALESCE(NULLIF(@priority,''), priority),
  item_count=COALESCE(@items, item_count), order_value=COALESCE(@value, order_value),
  route_code=COALESCE(NULLIF(@route,''), route_code), driver_name=COALESCE(NULLIF(@driver,''), driver_name),
  vehicle_number=COALESCE(NULLIF(@vehicle,''), vehicle_number), dispatch_notes=COALESCE(NULLIF(@notes,''), dispatch_notes),
  promised_at_utc=COALESCE(@promised, promised_at_utc), updated_at_utc=NOW()
WHERE id=@id AND company_id=@companyId AND (@branchId::BIGINT IS NULL OR branch_id=@branchId)",
            c =>
            {
                c.Parameters.AddWithValue("@customer", req.CustomerName?.Trim() ?? "");
                c.Parameters.AddWithValue("@segment", req.CustomerSegment?.Trim() ?? "");
                c.Parameters.AddWithValue("@channel", req.SalesChannel?.Trim() ?? "");
                c.Parameters.AddWithValue("@city", req.City?.Trim() ?? "");
                c.Parameters.AddWithValue("@area", req.Area?.Trim() ?? "");
                c.Parameters.AddWithValue("@status", req.Status?.Trim() ?? "");
                c.Parameters.AddWithValue("@priority", req.Priority?.Trim() ?? "");
                c.Parameters.AddWithValue("@items", I(req.ItemCount));
                c.Parameters.AddWithValue("@value", N(req.OrderValue));
                c.Parameters.AddWithValue("@route", req.RouteCode?.Trim() ?? "");
                c.Parameters.AddWithValue("@driver", req.DriverName?.Trim() ?? "");
                c.Parameters.AddWithValue("@vehicle", req.VehicleNumber?.Trim() ?? "");
                c.Parameters.AddWithValue("@notes", req.DispatchNotes?.Trim() ?? "");
                c.Parameters.AddWithValue("@promised", Dt(req.PromisedAtUtc));
                c.Parameters.AddWithValue("@id", id);
                BindScope(c, companyId, branchId);
            }, ct);
        if (rows == 0) return NotFound();
        return Ok(await Row(db, "fleet_tms_dispatch_orders", companyId, branchId, id, ct)!);
    }

    // ── Routes ──────────────────────────────────────────────────────────────────

    private static async Task<IResult> Routes(HttpContext http, Database db, string? status, CancellationToken ct)
    {
        if (RequireView(http) is { } denied) return denied;
        var companyId = Cid(http);
        var branchId = Bid(http);
        status = Clean(status, 30);
        if (status is not null && !ValidRouteStatus(status)) return Bad("Invalid route status filter.");
        var where = "WHERE company_id=@companyId AND (@branchId::BIGINT IS NULL OR branch_id=@branchId)" + (status is null ? "" : " AND status=@status");
        var items = await db.QueryAsync($"SELECT * FROM fleet_tms_delivery_routes {where} ORDER BY planned_for_date DESC, route_code",
            c => { BindScope(c, companyId, branchId); if (status is not null) c.Parameters.AddWithValue("@status", status); }, ct);
        return Ok(new { items });
    }

    private static async Task<IResult> CreateRoute(HttpContext http, LogisticsRouteRequest req, Database db, CancellationToken ct)
    {
        if (RequireExplicit(http, "dispatch:create") is { } denied) return denied;
        if (Clean(req.RouteCode, 60) is null) return Bad("Route code is required and cannot exceed 60 characters.");
        if (req.PlannedStops is < 0 or > 100_000 || req.CompletedStops is < 0) return Bad("Route stop counts are invalid.");
        if ((req.CompletedStops ?? 0) > (req.PlannedStops ?? 0)) return Bad("Completed stops cannot exceed planned stops.");
        if (req.CompletionPercent is < 0 or > 100) return Bad("Completion percent must be between 0 and 100.");
        if ((req.CompletedStops ?? 0) != 0 || (req.CompletionPercent ?? 0) != 0)
            return Bad("New routes must start with zero completed stops and zero completion percent.");
        if (!string.IsNullOrWhiteSpace(req.Status) && req.Status != "Planned") return Bad("New routes must start in Planned status.");
        var companyId = Cid(http);
        var branchId = Bid(http);
        long id;
        try { id = await db.InsertAsync(@"
INSERT INTO fleet_tms_delivery_routes (company_id, branch_id, route_code, hub, territory, driver_name, vehicle_number, status, planned_stops, completed_stops, distance_km, completion_percent, current_stop, next_stop, planned_for_date, departure_time_utc, eta_complete_utc, notes)
VALUES (@companyId, @branchId, @code, @hub, @territory, @driver, @vehicle, @status, @planned, @completed, @distance, @percent, @current, @next, @forDate, @departure, @eta, @notes)",
            c => BindRoute(c, companyId, branchId, req, isCreate: true), ct); }
        catch (PostgresException ex) when (ex.SqlState == PostgresErrorCodes.UniqueViolation)
        { return Results.Conflict(ApiResponse<object>.Fail("Route code already exists for this company.")); }
        return Ok(await Row(db, "fleet_tms_delivery_routes", companyId, branchId, id, ct)!);
    }

    private static void BindRoute(NpgsqlCommand c, long companyId, long? branchId, LogisticsRouteRequest req, bool isCreate)
    {
        BindScope(c, companyId, branchId);
        c.Parameters.AddWithValue("@code", req.RouteCode?.Trim() ?? "");
        c.Parameters.AddWithValue("@hub", req.Hub?.Trim() ?? "");
        c.Parameters.AddWithValue("@territory", req.Territory?.Trim() ?? "");
        c.Parameters.AddWithValue("@driver", req.DriverName?.Trim() ?? "");
        c.Parameters.AddWithValue("@vehicle", req.VehicleNumber?.Trim() ?? "");
        c.Parameters.AddWithValue("@status", req.Status?.Trim() ?? (isCreate ? "Planned" : ""));
        c.Parameters.AddWithValue("@planned", isCreate ? (req.PlannedStops ?? 0) : I(req.PlannedStops));
        c.Parameters.AddWithValue("@completed", isCreate ? (req.CompletedStops ?? 0) : I(req.CompletedStops));
        c.Parameters.AddWithValue("@distance", isCreate ? (req.DistanceKm ?? 0m) : N(req.DistanceKm));
        c.Parameters.AddWithValue("@percent", isCreate ? (req.CompletionPercent ?? 0m) : N(req.CompletionPercent));
        c.Parameters.AddWithValue("@current", req.CurrentStop?.Trim() ?? "");
        c.Parameters.AddWithValue("@next", req.NextStop?.Trim() ?? "");
        c.Parameters.AddWithValue("@forDate", (object?)(req.PlannedForDate?.Date) ?? (isCreate ? DateTime.UtcNow.Date : DBNull.Value));
        c.Parameters.AddWithValue("@departure", (object?)req.DepartureTimeUtc ?? (isCreate ? DateTime.UtcNow : DBNull.Value));
        c.Parameters.AddWithValue("@eta", Dt(req.EtaCompleteUtc));
        c.Parameters.AddWithValue("@notes", req.Notes?.Trim() ?? "");
    }

    private static async Task<IResult> UpdateRoute(HttpContext http, long id, LogisticsRouteRequest req, Database db, CancellationToken ct)
    {
        if (RequireExplicit(http, "dispatch:update") is { } denied) return denied;
        var companyId = Cid(http);
        var branchId = Bid(http);
        var currentRoute = await Row(db, "fleet_tms_delivery_routes", companyId, branchId, id, ct);
        if (currentRoute is null) return NotFound();
        if (currentRoute["status"]?.ToString() is "Closed" or "Completed")
            return Results.Conflict(ApiResponse<object>.Fail("A completed route cannot be edited."));
        if (!string.IsNullOrWhiteSpace(req.Status) && !string.Equals(req.Status.Trim(), currentRoute["status"]?.ToString(), StringComparison.Ordinal))
            return Bad("Route status must be changed through a workflow action.");
        if (req.PlannedStops is < 0 or > 100_000 || req.CompletedStops is < 0 || req.CompletionPercent is < 0 or > 100)
            return Bad("Route progress values are invalid.");
        if (req.CompletedStops.HasValue && req.CompletedStops.Value != Convert.ToInt32(currentRoute["completedStops"]) ||
            req.CompletionPercent.HasValue && req.CompletionPercent.Value != Convert.ToDecimal(currentRoute["completionPercent"]))
            return Bad("Completed stops and completion percent must be changed through the route progress or delivery workflow.");
        var targetPlanned = req.PlannedStops ?? Convert.ToInt32(currentRoute["plannedStops"]);
        var targetCompleted = req.CompletedStops ?? Convert.ToInt32(currentRoute["completedStops"]);
        if (targetCompleted > targetPlanned) return Bad("Completed stops cannot exceed planned stops.");
        var rows = await db.ExecuteAsync(@"
UPDATE fleet_tms_delivery_routes SET
  hub=COALESCE(NULLIF(@hub,''), hub), territory=COALESCE(NULLIF(@territory,''), territory),
  driver_name=COALESCE(NULLIF(@driver,''), driver_name), vehicle_number=COALESCE(NULLIF(@vehicle,''), vehicle_number),
  status=COALESCE(NULLIF(@status,''), status), planned_stops=COALESCE(@planned, planned_stops),
  completed_stops=COALESCE(@completed, completed_stops), distance_km=COALESCE(@distance, distance_km),
  completion_percent=COALESCE(@percent, completion_percent), current_stop=COALESCE(NULLIF(@current,''), current_stop),
  next_stop=COALESCE(NULLIF(@next,''), next_stop), planned_for_date=COALESCE(@forDate, planned_for_date),
  departure_time_utc=COALESCE(@departure, departure_time_utc), eta_complete_utc=COALESCE(@eta, eta_complete_utc),
  notes=COALESCE(NULLIF(@notes,''), notes)
WHERE id=@id AND company_id=@companyId AND (@branchId::BIGINT IS NULL OR branch_id=@branchId)",
            c => { BindRoute(c, companyId, branchId, req, isCreate: false); c.Parameters.AddWithValue("@id", id); }, ct);
        if (rows == 0) return NotFound();
        return Ok(await Row(db, "fleet_tms_delivery_routes", companyId, branchId, id, ct)!);
    }

    private static async Task<IResult> RouteStops(HttpContext http, long id, Database db, CancellationToken ct)
    {
        if (RequireView(http) is { } denied) return denied;
        var companyId = Cid(http);
        var branchId = Bid(http);
        var route = await Row(db, "fleet_tms_delivery_routes", companyId, branchId, id, ct);
        if (route is null) return NotFound();
        var items = await db.QueryAsync("SELECT * FROM fleet_tms_last_mile_stops WHERE company_id=@companyId AND (@branchId::BIGINT IS NULL OR branch_id=@branchId) AND route_code=@route ORDER BY eta_utc",
            c => { BindScope(c, companyId, branchId); c.Parameters.AddWithValue("@route", route["routeCode"]?.ToString() ?? ""); }, ct);
        return Ok(new { items });
    }

    private static async Task<IResult> LastMile(HttpContext http, Database db, CancellationToken ct, string? status = null, string? routeCode = null, string? search = null, int page = 1, int pageSize = 50)
    {
        if (RequireView(http) is { } denied) return denied;
        var companyId = Cid(http);
        var branchId = Bid(http);
        page = page < 1 ? 1 : page; pageSize = pageSize is < 1 or > 200 ? 20 : pageSize;
        status = Clean(status, 30);
        routeCode = Clean(routeCode, 60);
        search = Clean(search, 120);
        if (status is not null && !ValidStopStatus(status)) return Bad("Invalid stop status filter.");
        var where = "WHERE company_id=@companyId AND (@branchId::BIGINT IS NULL OR branch_id=@branchId)" +
                    (status is null ? "" : " AND status=@status") +
                    (routeCode is null ? "" : " AND route_code=@route") +
                    (search is null ? "" : " AND (order_number ILIKE @search OR customer_name ILIKE @search OR route_code ILIKE @search OR address_line ILIKE @search OR city ILIKE @search OR rider_name ILIKE @search)");
        void B(NpgsqlCommand c)
        {
            BindScope(c, companyId, branchId);
            if (status is not null) c.Parameters.AddWithValue("@status", status);
            if (routeCode is not null) c.Parameters.AddWithValue("@route", routeCode);
            if (search is not null) c.Parameters.AddWithValue("@search", $"%{search}%");
        }
        var total = await db.ScalarLongAsync($"SELECT COUNT(*) FROM fleet_tms_last_mile_stops {where}", B, ct);
        var items = await db.QueryAsync($"SELECT * FROM fleet_tms_last_mile_stops {where} ORDER BY eta_utc DESC OFFSET @offset LIMIT @limit",
            c => { B(c); c.Parameters.AddWithValue("@offset", (page - 1) * pageSize); c.Parameters.AddWithValue("@limit", pageSize); }, ct);
        return Ok(new { total, page, pageSize, items });
    }

    private static async Task<IResult> ExportLastMile(HttpContext http, Database db, CancellationToken ct, string? status = null, string? routeCode = null, string? search = null)
    {
        if (RequireView(http) is { } denied) return denied;
        var companyId = Cid(http);
        var branchId = Bid(http);
        status = Clean(status, 30);
        routeCode = Clean(routeCode, 60);
        search = Clean(search, 120);
        if (status is not null && !ValidStopStatus(status)) return Bad("Invalid stop status filter.");
        var where = "WHERE company_id=@companyId AND (@branchId::BIGINT IS NULL OR branch_id=@branchId)" +
                    (status is null ? "" : " AND status=@status") + (routeCode is null ? "" : " AND route_code=@route") +
                    (search is null ? "" : " AND (order_number ILIKE @search OR customer_name ILIKE @search OR route_code ILIKE @search OR address_line ILIKE @search OR city ILIKE @search OR rider_name ILIKE @search)");
        var rows = await db.QueryAsync($@"SELECT order_number,route_code,customer_name,address_line,city,status,proof_status,recipient_name,
                                                attempt_count,rider_name,time_window,eta_utc,delivered_at_utc,exception_reason
                                         FROM fleet_tms_last_mile_stops {where} ORDER BY eta_utc DESC",
            c =>
            {
                BindScope(c, companyId, branchId);
                if (status is not null) c.Parameters.AddWithValue("@status", status);
                if (routeCode is not null) c.Parameters.AddWithValue("@route", routeCode);
                if (search is not null) c.Parameters.AddWithValue("@search", $"%{search}%");
            }, ct);
        var columns = new[] { "orderNumber", "routeCode", "customerName", "addressLine", "city", "status", "proofStatus", "recipientName", "attemptCount", "riderName", "timeWindow", "etaUtc", "deliveredAtUtc", "exceptionReason" };
        var csv = new System.Text.StringBuilder().AppendLine(string.Join(',', columns));
        foreach (var row in rows) csv.AppendLine(string.Join(',', columns.Select(column => LastMileCsvCell(row.GetValueOrDefault(column)))));
        return Results.File(System.Text.Encoding.UTF8.GetBytes(csv.ToString()), "text/csv", $"last-mile-{DateTime.UtcNow:yyyyMMddHHmmss}.csv");
    }

    // ── Workflow actions ────────────────────────────────────────────────────────

    private static async Task<IResult> DispatchOrder(HttpContext http, long id, DispatchOrderRequest req, Database db, CancellationToken ct)
    {
        if (RequireExplicit(http, "dispatch:assign") is { } denied) return denied;
        var companyId = Cid(http);
        var branchId = Bid(http);
        return await db.RunInTenantTransactionAsync<IResult>(companyId, async () =>
        {
        var order = await Row(db, "fleet_tms_dispatch_orders", companyId, branchId, id, ct, forUpdate: true);
        if (order is null) return NotFound();
        var status = order["status"]?.ToString();
        if (status == "Dispatched")
        {
            var currentRoute = Clean(order["routeCode"]?.ToString(), 60);
            var currentDriver = Clean(order["driverName"]?.ToString(), 255);
            var currentVehicle = Clean(order["vehicleNumber"]?.ToString(), 60);
            var retryRoute = Clean(req.RouteCode, 60) ?? currentRoute;
            var retryDriver = Clean(req.DriverName, 255) ?? currentDriver;
            var retryVehicle = Clean(req.VehicleNumber, 60) ?? currentVehicle;
            if (string.Equals(retryRoute, currentRoute, StringComparison.Ordinal) &&
                string.Equals(retryDriver, currentDriver, StringComparison.Ordinal) &&
                string.Equals(retryVehicle, currentVehicle, StringComparison.Ordinal))
                return Ok(order);
            return Results.Conflict(ApiResponse<object>.Fail("An already dispatched order cannot be reassigned."));
        }
        if (status != "Queued") return Results.Conflict(ApiResponse<object>.Fail("The order cannot be dispatched from its current state."));
        var routeCode = Clean(req.RouteCode, 60) ?? Clean(order["routeCode"]?.ToString(), 60);
        if (routeCode is null) return Bad("A route is required before dispatch.");
        var orderBranch = order["branchId"] is null or DBNull ? (object)DBNull.Value : Convert.ToInt64(order["branchId"]);
        var route = await db.QuerySingleAsync(
            @"SELECT * FROM fleet_tms_delivery_routes
              WHERE company_id=@companyId AND branch_id IS NOT DISTINCT FROM @orderBranch
                AND route_code=@route AND status IN ('Planned','Ready','Active','Delayed') FOR UPDATE",
            c => { c.Parameters.AddWithValue("@companyId", companyId); c.Parameters.AddWithValue("@orderBranch", orderBranch); c.Parameters.AddWithValue("@route", routeCode); }, ct);
        if (route is null) return Bad("Route was not found in the order branch or is closed.");
        if (Convert.ToInt32(route["plannedStops"] ?? 0) < 1) return Bad("The route must have at least one planned stop before dispatch.");
        var driver = Clean(req.DriverName, 255) ?? Clean(order["driverName"]?.ToString(), 255) ?? Clean(route["driverName"]?.ToString(), 255);
        var vehicle = Clean(req.VehicleNumber, 60) ?? Clean(order["vehicleNumber"]?.ToString(), 60) ?? Clean(route["vehicleNumber"]?.ToString(), 60);
        if (driver is null || vehicle is null) return Bad("Driver and vehicle are required before dispatch.");

        var orderNumber = order["orderNumber"]?.ToString() ?? "";
        var existingStop = await db.QuerySingleAsync(
            "SELECT * FROM fleet_tms_last_mile_stops WHERE company_id=@companyId AND order_number=@orderNumber FOR UPDATE",
            c => { c.Parameters.AddWithValue("@companyId", companyId); c.Parameters.AddWithValue("@orderNumber", orderNumber); }, ct);
        if (existingStop is not null && existingStop["branchId"] is not null and not DBNull &&
            (orderBranch is DBNull || Convert.ToInt64(existingStop["branchId"]) != Convert.ToInt64(orderBranch)))
            return Results.Conflict(ApiResponse<object>.Fail("The existing stop belongs to a different branch."));
        if (existingStop is not null && existingStop["branchId"] is null && orderBranch is not DBNull)
            return Results.Conflict(ApiResponse<object>.Fail("The existing stop belongs to a different branch."));
        if (existingStop is not null && TerminalStop(existingStop["status"]?.ToString()))
            return Results.Conflict(ApiResponse<object>.Fail("A delivered stop cannot be dispatched again."));
        if (existingStop is null)
        {
            var assignedStops = await db.ScalarLongAsync(
                "SELECT COUNT(*) FROM fleet_tms_last_mile_stops WHERE company_id=@companyId AND route_code=@route",
                c => { c.Parameters.AddWithValue("@companyId", companyId); c.Parameters.AddWithValue("@route", routeCode); }, ct);
            if (assignedStops >= Convert.ToInt32(route["plannedStops"]))
                return Results.Conflict(ApiResponse<object>.Fail("The route already has its planned number of stops."));
        }

        if (status != "Dispatched") await db.ExecuteAsync(@"
UPDATE fleet_tms_dispatch_orders SET status='Dispatched', driver_name=COALESCE(NULLIF(@driver,''), driver_name),
  vehicle_number=COALESCE(NULLIF(@vehicle,''), vehicle_number), route_code=COALESCE(NULLIF(@route,''), route_code),
  dispatch_notes=COALESCE(NULLIF(@notes,''), dispatch_notes), dispatched_at_utc=NOW(), updated_at_utc=NOW()
WHERE id=@id AND company_id=@companyId AND (@branchId::BIGINT IS NULL OR branch_id=@branchId)",
            c => { c.Parameters.AddWithValue("@driver", driver); c.Parameters.AddWithValue("@vehicle", vehicle); c.Parameters.AddWithValue("@route", routeCode); c.Parameters.AddWithValue("@notes", Clean(req.Notes, 4000) ?? ""); c.Parameters.AddWithValue("@id", id); BindScope(c, companyId, branchId); }, ct);

        var updated = await Row(db, "fleet_tms_dispatch_orders", companyId, branchId, id, ct)!;
        await db.ExecuteAsync("UPDATE fleet_tms_delivery_routes SET status='Ready' WHERE company_id=@companyId AND (@branchId::BIGINT IS NULL OR branch_id=@branchId) AND route_code=@route AND status='Planned'",
            c => { BindScope(c, companyId, branchId); c.Parameters.AddWithValue("@route", routeCode); }, ct);
        if (existingStop is null)
            await db.ExecuteAsync(
                @"INSERT INTO fleet_tms_last_mile_stops
                    (company_id,branch_id,order_number,route_code,customer_name,address_line,city,status,proof_status,rider_name,eta_utc,created_at_utc,updated_at_utc)
                  VALUES (@companyId,@orderBranch,@orderNumber,@route,@customer,@address,@city,'OutForDelivery','None',@driver,COALESCE(@eta,NOW()),NOW(),NOW())",
                c =>
                {
                    c.Parameters.AddWithValue("@companyId", companyId); c.Parameters.AddWithValue("@orderBranch", orderBranch);
                    c.Parameters.AddWithValue("@orderNumber", orderNumber); c.Parameters.AddWithValue("@route", routeCode);
                    c.Parameters.AddWithValue("@customer", order["customerName"]?.ToString() ?? "");
                    c.Parameters.AddWithValue("@address", order["area"]?.ToString() ?? ""); c.Parameters.AddWithValue("@city", order["city"]?.ToString() ?? "");
                    c.Parameters.AddWithValue("@driver", driver); c.Parameters.AddWithValue("@eta", order["promisedAtUtc"] ?? DBNull.Value);
                }, ct);
        else
            await db.ExecuteAsync(
                @"UPDATE fleet_tms_last_mile_stops SET route_code=@route,status='OutForDelivery',rider_name=@driver,updated_at_utc=NOW()
                  WHERE id=@stopId AND company_id=@companyId",
                c => { c.Parameters.AddWithValue("@route", routeCode); c.Parameters.AddWithValue("@driver", driver); c.Parameters.AddWithValue("@stopId", Convert.ToInt64(existingStop["id"])); c.Parameters.AddWithValue("@companyId", companyId); }, ct);
        return Ok(updated);
        }, ct);
    }

    private static async Task<IResult> ProgressRoute(HttpContext http, long id, RouteProgressRequest req, Database db, CancellationToken ct)
    {
        if (RequireManage(http) is { } denied) return denied;
        var companyId = Cid(http);
        var branchId = Bid(http);
        var key = Clean(req.IdempotencyKey, 80);
        if (key is null) return Bad("A valid idempotencyKey is required.");
        if (req.CompletedStopsDelta is < 1 or > 500) return Bad("completedStopsDelta must be between 1 and 500.");
        var operation = $"fleet-tms.route.{id}.progress";
        var requestHash = ActionRequestHash(req);
        return await db.RunInTenantTransactionAsync<IResult>(companyId, async () =>
        {
            var route = await Row(db, "fleet_tms_delivery_routes", companyId, branchId, id, ct, forUpdate: true);
            if (route is null) return NotFound();
            var replay = await ActionReplay(db, companyId, operation, key, requestHash, ct);
            if (replay.Seen && !replay.SameRequest) return Results.Conflict(ApiResponse<object>.Fail("The idempotency key was already used with a different request."));
            if (replay.Seen) return Ok(route);
            if (route["lastProgressKey"]?.ToString() == key)
            {
                await RecordActionReceipt(db, companyId, operation, key, requestHash, id, ct);
                return Ok(route);
            }
            if (route["status"]?.ToString() is "Closed" or "Completed")
                return Results.Conflict(ApiResponse<object>.Fail("A completed route cannot be progressed."));
            var planned = Convert.ToInt32(route["plannedStops"]);
            if (planned <= 0) return Bad("A route must have at least one planned stop before progress can be recorded.");
            var completed = Math.Min(planned, Convert.ToInt32(route["completedStops"]) + req.CompletedStopsDelta);
            var percent = Math.Round(completed / (decimal)planned * 100m, 1);
            var status = completed >= planned ? "Closed" : "Active";
            await db.ExecuteAsync(@"
UPDATE fleet_tms_delivery_routes SET completed_stops=@completed, completion_percent=@percent, status=@status,
  current_stop=COALESCE(@current, current_stop), next_stop=COALESCE(@next, next_stop),
  eta_complete_utc=COALESCE(@eta, eta_complete_utc), notes=COALESCE(@notes, notes), last_progress_key=@key
WHERE id=@id AND company_id=@companyId AND (@branchId::BIGINT IS NULL OR branch_id=@branchId)",
                c => { c.Parameters.AddWithValue("@completed", completed); c.Parameters.AddWithValue("@percent", percent); c.Parameters.AddWithValue("@status", status); c.Parameters.AddWithValue("@current", S(Clean(req.CurrentStop, 255))); c.Parameters.AddWithValue("@next", S(Clean(req.NextStop, 255))); c.Parameters.AddWithValue("@eta", Dt(req.EtaCompleteUtc)); c.Parameters.AddWithValue("@notes", S(Clean(req.Notes, 4000))); c.Parameters.AddWithValue("@key", key); c.Parameters.AddWithValue("@id", id); BindScope(c, companyId, branchId); }, ct);
            await RecordActionReceipt(db, companyId, operation, key, requestHash, id, ct);
            return Ok(await Row(db, "fleet_tms_delivery_routes", companyId, branchId, id, ct)!);
        }, ct);
    }

    private static async Task<IResult> ConfirmDelivery(HttpContext http, long id, ConfirmDeliveryRequest req, Database db, CancellationToken ct)
    {
        if (RequireExplicit(http, "fleet.pod.manage", "shipments:update", "dispatch:update") is { } denied) return denied;
        var companyId = Cid(http);
        var branchId = Bid(http);
        var key = Clean(req.IdempotencyKey, 80);
        var recipient = Clean(req.RecipientName, 255);
        var evidence = Clean(req.EvidenceReference, 2000);
        var proof = Clean(req.ProofStatus, 30) ?? "Captured";
        if (key is null) return Bad("A valid idempotencyKey is required.");
        if (recipient is null) return Bad("Recipient name is required.");
        if (evidence is null) return Bad("Delivery evidence (signature or photo reference) is required to confirm delivery.");
        if (proof is not ("Captured" or "Verified")) return Bad("proofStatus must be Captured or Verified.");
        var operation = $"fleet-tms.stop.{id}.deliver";
        var requestHash = ActionRequestHash(req);
        return await db.RunInTenantTransactionAsync<IResult>(companyId, async () =>
        {
        var stop = await Row(db, "fleet_tms_last_mile_stops", companyId, branchId, id, ct, forUpdate: true);
        if (stop is null) return NotFound();
        if (StopGraphDenied(await StopWorkflowGraph(db, companyId, id, ct), requirePlannedStops: true) is { } graphDenied) return graphDenied;
        var replay = await ActionReplay(db, companyId, operation, key, requestHash, ct);
        if (replay.Seen && !replay.SameRequest) return Results.Conflict(ApiResponse<object>.Fail("The idempotency key was already used with a different request."));
        if (replay.Seen) return Ok(stop);
        if (stop["lastActionKey"]?.ToString() == key && stop["lastActionType"]?.ToString() == "Deliver")
        {
            await RecordActionReceipt(db, companyId, operation, key, requestHash, id, ct);
            return Ok(stop);
        }
        if (TerminalStop(stop["status"]?.ToString())) return Results.Conflict(ApiResponse<object>.Fail("This stop is already delivered; retry with the original idempotency key."));
        if (stop["status"]?.ToString() is not ("OutForDelivery" or "Attempted" or "Rescheduled"))
            return Results.Conflict(ApiResponse<object>.Fail("The stop cannot be delivered from its current state."));
        await db.ExecuteAsync(@"
UPDATE fleet_tms_last_mile_stops SET status='Delivered', proof_status=@proof, recipient_name=COALESCE(NULLIF(@recipient,''), recipient_name),
  proof_evidence_ref=@evidence, delivered_at_utc=NOW(), exception_reason=@exception, attempt_count=GREATEST(attempt_count, 1), updated_at_utc=NOW(),
  last_action_key=@key, last_action_type='Deliver'
WHERE id=@id AND company_id=@companyId AND (@branchId::BIGINT IS NULL OR branch_id=@branchId)",
            c => { c.Parameters.AddWithValue("@proof", proof); c.Parameters.AddWithValue("@recipient", recipient); c.Parameters.AddWithValue("@evidence", evidence); c.Parameters.AddWithValue("@exception", Clean(req.ExceptionReason, 2000) ?? ""); c.Parameters.AddWithValue("@key", key); c.Parameters.AddWithValue("@id", id); BindScope(c, companyId, branchId); }, ct);

        var orderNumber = stop["orderNumber"]?.ToString() ?? "";
        var routeCode = stop["routeCode"]?.ToString() ?? "";
        var customer = stop["customerName"]?.ToString() ?? "";
        var workflowBranchId = stop["branchId"] is null or DBNull ? (long?)null : Convert.ToInt64(stop["branchId"]);
        var orderCount = await db.ExecuteAsync("UPDATE fleet_tms_dispatch_orders SET status='Delivered', delivered_at_utc=COALESCE(delivered_at_utc,NOW()), updated_at_utc=NOW() WHERE company_id=@companyId AND (@branchId::BIGINT IS NULL OR branch_id=@branchId) AND order_number=@num",
            c => { BindScope(c, companyId, branchId); c.Parameters.AddWithValue("@num", orderNumber); }, ct);
        if (orderCount != 1) throw new InvalidOperationException("Last-mile stop does not have exactly one authorized dispatch order.");
        await BridgeLastMileToBillingCoreAsync(db, companyId, workflowBranchId, orderNumber, customer, ct);

        var routeCount = await db.ExecuteAsync(@"
UPDATE fleet_tms_delivery_routes r SET completed_stops=x.delivered,
  completion_percent=CASE WHEN r.planned_stops=0 THEN 0 ELSE ROUND(LEAST(r.planned_stops, x.delivered) / r.planned_stops::numeric * 100, 1) END,
  status=CASE WHEN x.delivered >= r.planned_stops THEN 'Closed' ELSE 'Active' END,
  current_stop=@customer
FROM (SELECT COUNT(*)::int delivered FROM fleet_tms_last_mile_stops WHERE company_id=@companyId AND (@branchId::BIGINT IS NULL OR branch_id=@branchId) AND route_code=@route AND status='Delivered') x
WHERE r.company_id=@companyId AND (@branchId::BIGINT IS NULL OR r.branch_id=@branchId) AND r.route_code=@route",
            c => { c.Parameters.AddWithValue("@customer", customer); BindScope(c, companyId, branchId); c.Parameters.AddWithValue("@route", routeCode); }, ct);
        if (routeCount != 1) throw new InvalidOperationException("Last-mile stop does not have exactly one authorized delivery route.");
        await RecordActionReceipt(db, companyId, operation, key, requestHash, id, ct);
        return Ok(await Row(db, "fleet_tms_last_mile_stops", companyId, branchId, id, ct)!);
        }, ct);
    }

    // Revenue bridge (P0 fix): a confirmed last-mile delivery must reach order-to-cash. The fleet_tms lane
    // has no job/customer/charge linkage, so a delivered order was invisible to invoicing/rev-rec/settlement.
    // Materialize a canonical customer + job + delivered dispatch_assignment + a MANUAL job_charge from the
    // order's order_value. A manual charge is billed by BillingConsolidationService (the rating/outbox path
    // alone cannot bill it — a fleet_tms order has no rate card, so rating writes zero charges). Every step is
    // idempotent, so a duplicate ConfirmDelivery never double-bills. internal for direct testing.
    internal static async Task BridgeLastMileToBillingAsync(Database db, long companyId, string orderNumber, string customerName, CancellationToken ct)
    {
        await db.RunInTenantTransactionAsync(companyId, async () =>
        {
            await BridgeLastMileToBillingCoreAsync(db, companyId, null, orderNumber, customerName, ct);
            return true;
        }, ct);
    }

    private static async Task BridgeLastMileToBillingCoreAsync(Database db, long companyId, long? branchId, string orderNumber, string customerName, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(orderNumber) || string.IsNullOrWhiteSpace(customerName))
            throw new InvalidOperationException("Order number and customer name are required for billing.");
        await db.ExecuteAsync("SELECT pg_advisory_xact_lock(hashtextextended(@key,0))", c => c.Parameters.AddWithValue("@key", $"ftms:{companyId}:{orderNumber}"), ct);

        var orderValue = await db.ScalarDecimalAsync(
            "SELECT order_value FROM fleet_tms_dispatch_orders WHERE company_id=@c AND (@b::BIGINT IS NULL OR branch_id=@b) AND order_number=@n LIMIT 1",
            c => { c.Parameters.AddWithValue("@c", companyId); c.Parameters.AddWithValue("@b", branchId ?? (object)DBNull.Value); c.Parameters.AddWithValue("@n", orderNumber); }, ct)
            ?? throw new InvalidOperationException("Dispatch order was not found for billing.");

        // 1) find-or-create the customer (deterministic code so re-runs converge on one row)
        await db.ExecuteAsync(
            @"INSERT INTO customers (company_id, customer_code, name)
              VALUES (@c, 'FTMS-'||left(md5(lower(@name)),12), @name)
              ON CONFLICT (company_id, customer_code) DO NOTHING",
            c => { c.Parameters.AddWithValue("@c", companyId); c.Parameters.AddWithValue("@name", customerName); }, ct);
        var customerId = await db.ScalarLongAsync(
            "SELECT id FROM customers WHERE company_id=@c AND customer_code='FTMS-'||left(md5(lower(@name)),12) LIMIT 1",
            c => { c.Parameters.AddWithValue("@c", companyId); c.Parameters.AddWithValue("@name", customerName); }, ct);
        if (customerId <= 0) return;

        // 2) materialize the canonical job (one per order via UNIQUE(company_id, job_code))
        var jobId = await db.ScalarLongAsync(
            @"INSERT INTO jobs (company_id, branch_id, customer_id, job_code, job_type, status)
              VALUES (@c, @b, @cust, 'FTMS-'||@num, 'last_mile', 'Delivered')
              ON CONFLICT (company_id, job_code) DO UPDATE SET status='Delivered', customer_id=COALESCE(jobs.customer_id,EXCLUDED.customer_id), branch_id=COALESCE(EXCLUDED.branch_id,jobs.branch_id)
              RETURNING id",
            c => { c.Parameters.AddWithValue("@c", companyId); c.Parameters.AddWithValue("@b", branchId ?? (object)DBNull.Value); c.Parameters.AddWithValue("@cust", customerId); c.Parameters.AddWithValue("@num", orderNumber); }, ct);
        if (jobId <= 0) return;

        // 3) a delivered dispatch_assignment (BillingConsolidationService's period filter requires it)
        await db.ExecuteAsync(
            @"INSERT INTO dispatch_assignments (company_id, branch_id, job_id, status, assignment_status, actual_delivery_at)
              SELECT @c, @b, @j, 'Delivered', 'delivered', NOW()
              WHERE NOT EXISTS (SELECT 1 FROM dispatch_assignments WHERE company_id=@c AND job_id=@j)",
            c => { c.Parameters.AddWithValue("@c", companyId); c.Parameters.AddWithValue("@b", branchId ?? (object)DBNull.Value); c.Parameters.AddWithValue("@j", jobId); }, ct);
        await db.ExecuteAsync(
            @"UPDATE dispatch_assignments SET status='Delivered', assignment_status='delivered', actual_delivery_at=COALESCE(actual_delivery_at, NOW())
              WHERE company_id=@c AND job_id=@j",
            c => { c.Parameters.AddWithValue("@c", companyId); c.Parameters.AddWithValue("@j", jobId); }, ct);

        // 4) the manual revenue charge (idempotent on charge_code -> billed at most once)
        await db.ExecuteAsync(
            @"INSERT INTO job_charges (company_id, job_id, charge_code, charge_name, charge_type, quantity, unit_rate, amount, source, billing_status)
              SELECT @c, @j, 'LASTMILE', 'Last-mile delivery', 'base', 1, @v, @v, 'manual', 'unbilled'
              WHERE NOT EXISTS (SELECT 1 FROM job_charges WHERE company_id=@c AND job_id=@j AND charge_code='LASTMILE')",
            c => { c.Parameters.AddWithValue("@c", companyId); c.Parameters.AddWithValue("@j", jobId); c.Parameters.AddWithValue("@v", orderValue); }, ct);

        // Match the canonical POD/dispatch delivery contract so downstream consumers see the
        // delivery exactly once even when mobile clients retry ConfirmDelivery concurrently.
        await db.ExecuteAsync(
            @"INSERT INTO outbox_messages
                (tenant_id, event_type, aggregate_type, aggregate_id, payload_json, created_at, status, retry_count)
              VALUES (@c, 'job.delivered', 'job', @j::TEXT,
                      jsonb_build_object('jobId',@j,'companyId',@c), NOW(), 'pending', 0)
              ON CONFLICT (tenant_id, aggregate_id) WHERE event_type='job.delivered' DO NOTHING",
            c => { c.Parameters.AddWithValue("@c", companyId); c.Parameters.AddWithValue("@j", jobId); }, ct);
    }

    private static async Task<IResult> RecordAttempt(HttpContext http, long id, StopAttemptRequest req, Database db, CancellationToken ct)
    {
        if (RequireManage(http) is { } denied) return denied;
        var companyId = Cid(http);
        var branchId = Bid(http);
        var key = Clean(req.IdempotencyKey, 80);
        var newStatus = Clean(req.Status, 30) ?? "Attempted";
        var reason = Clean(req.ExceptionReason, 2000);
        var proof = Clean(req.ProofStatus, 30);
        if (key is null) return Bad("A valid idempotencyKey is required.");
        if (newStatus is not ("Attempted" or "Failed")) return Bad("status must be Attempted or Failed.");
        if (reason is null) return Bad("An exception reason is required.");
        if (!ValidAttemptProofStatus(proof)) return Bad("proofStatus is invalid.");
        if (req.NextEtaUtc is { } eta && (eta <= DateTime.UtcNow || eta > DateTime.UtcNow.AddDays(365))) return Bad("nextEtaUtc must be a future date within 365 days.");
        var operation = $"fleet-tms.stop.{id}.attempt";
        var requestHash = ActionRequestHash(req);
        return await db.RunInTenantTransactionAsync<IResult>(companyId, async () =>
        {
        var stop = await Row(db, "fleet_tms_last_mile_stops", companyId, branchId, id, ct, forUpdate: true);
        if (stop is null) return NotFound();
        if (StopGraphDenied(await StopWorkflowGraph(db, companyId, id, ct), requirePlannedStops: false) is { } graphDenied) return graphDenied;
        var replay = await ActionReplay(db, companyId, operation, key, requestHash, ct);
        if (replay.Seen && !replay.SameRequest) return Results.Conflict(ApiResponse<object>.Fail("The idempotency key was already used with a different request."));
        if (replay.Seen) return Ok(stop);
        if (stop["lastActionKey"]?.ToString() == key && stop["lastActionType"]?.ToString() == "Attempt")
        {
            await RecordActionReceipt(db, companyId, operation, key, requestHash, id, ct);
            return Ok(stop);
        }
        if (TerminalStop(stop["status"]?.ToString())) return Results.Conflict(ApiResponse<object>.Fail("A delivered stop cannot be attempted."));
        await db.ExecuteAsync(@"
UPDATE fleet_tms_last_mile_stops SET status=@status, exception_reason=@exception, attempt_count=GREATEST(1, attempt_count + 1),
  proof_status=COALESCE(NULLIF(@proof,''), proof_status), eta_utc=COALESCE(@nextEta, eta_utc), updated_at_utc=NOW(),
  last_action_key=@key, last_action_type='Attempt'
WHERE id=@id AND company_id=@companyId AND (@branchId::BIGINT IS NULL OR branch_id=@branchId)",
            c => { c.Parameters.AddWithValue("@status", newStatus); c.Parameters.AddWithValue("@exception", reason); c.Parameters.AddWithValue("@proof", proof ?? ""); c.Parameters.AddWithValue("@nextEta", Dt(req.NextEtaUtc)); c.Parameters.AddWithValue("@key", key); c.Parameters.AddWithValue("@id", id); BindScope(c, companyId, branchId); }, ct);

        var orderNumber = stop["orderNumber"]?.ToString() ?? "";
        var routeCode = stop["routeCode"]?.ToString() ?? "";
        var customer = stop["customerName"]?.ToString() ?? "";
        var orderCount = await db.ExecuteAsync("UPDATE fleet_tms_dispatch_orders SET status=@status, dispatch_notes=COALESCE(NULLIF(@notes,''), dispatch_notes), updated_at_utc=NOW() WHERE company_id=@companyId AND (@branchId::BIGINT IS NULL OR branch_id=@branchId) AND order_number=@num",
            c => { c.Parameters.AddWithValue("@status", newStatus == "Failed" ? "Exception" : "InTransit"); c.Parameters.AddWithValue("@notes", reason); BindScope(c, companyId, branchId); c.Parameters.AddWithValue("@num", orderNumber); }, ct);
        var routeCount = await db.ExecuteAsync("UPDATE fleet_tms_delivery_routes SET status=CASE WHEN @failed THEN 'Delayed' ELSE status END, current_stop=@customer, next_stop=COALESCE(NULLIF(@next,''), next_stop), notes=@notes WHERE company_id=@companyId AND (@branchId::BIGINT IS NULL OR branch_id=@branchId) AND route_code=@route",
            c => { c.Parameters.AddWithValue("@failed", newStatus == "Failed"); c.Parameters.AddWithValue("@customer", customer); c.Parameters.AddWithValue("@next", Clean(req.NextStop, 255) ?? ""); c.Parameters.AddWithValue("@notes", reason); BindScope(c, companyId, branchId); c.Parameters.AddWithValue("@route", routeCode); }, ct);
        if (orderCount != 1 || routeCount != 1) throw new InvalidOperationException("Last-mile stop is not linked to exactly one authorized order and route.");
        await RecordActionReceipt(db, companyId, operation, key, requestHash, id, ct);
        return Ok(await Row(db, "fleet_tms_last_mile_stops", companyId, branchId, id, ct)!);
        }, ct);
    }

    private static async Task<IResult> RescheduleStop(HttpContext http, long id, StopRescheduleRequest req, Database db, CancellationToken ct)
    {
        if (RequireManage(http) is { } denied) return denied;
        var companyId = Cid(http);
        var branchId = Bid(http);
        var key = Clean(req.IdempotencyKey, 80);
        var reason = Clean(req.Reason, 2000);
        var window = Clean(req.TimeWindow, 80);
        if (key is null) return Bad("A valid idempotencyKey is required.");
        if (reason is null) return Bad("A reschedule reason is required.");
        if (req.NextEtaUtc is not { } eta || eta <= DateTime.UtcNow || eta > DateTime.UtcNow.AddDays(365)) return Bad("nextEtaUtc must be a future date within 365 days.");
        var operation = $"fleet-tms.stop.{id}.reschedule";
        var requestHash = ActionRequestHash(req);
        return await db.RunInTenantTransactionAsync<IResult>(companyId, async () =>
        {
        var stop = await Row(db, "fleet_tms_last_mile_stops", companyId, branchId, id, ct, forUpdate: true);
        if (stop is null) return NotFound();
        if (StopGraphDenied(await StopWorkflowGraph(db, companyId, id, ct), requirePlannedStops: false) is { } graphDenied) return graphDenied;
        var replay = await ActionReplay(db, companyId, operation, key, requestHash, ct);
        if (replay.Seen && !replay.SameRequest) return Results.Conflict(ApiResponse<object>.Fail("The idempotency key was already used with a different request."));
        if (replay.Seen) return Ok(stop);
        if (stop["lastActionKey"]?.ToString() == key && stop["lastActionType"]?.ToString() == "Reschedule")
        {
            await RecordActionReceipt(db, companyId, operation, key, requestHash, id, ct);
            return Ok(stop);
        }
        if (TerminalStop(stop["status"]?.ToString())) return Results.Conflict(ApiResponse<object>.Fail("A delivered stop cannot be rescheduled."));
        if (stop["status"]?.ToString() is not ("OutForDelivery" or "Attempted" or "Failed" or "Rescheduled")) return Results.Conflict(ApiResponse<object>.Fail("The stop cannot be rescheduled from its current state."));
        await db.ExecuteAsync(@"
UPDATE fleet_tms_last_mile_stops SET status='Rescheduled', time_window=COALESCE(NULLIF(@window,''), time_window),
  eta_utc=@nextEta, exception_reason=@reason, updated_at_utc=NOW(), last_action_key=@key, last_action_type='Reschedule'
WHERE id=@id AND company_id=@companyId AND (@branchId::BIGINT IS NULL OR branch_id=@branchId)",
            c => { c.Parameters.AddWithValue("@window", window ?? ""); c.Parameters.AddWithValue("@nextEta", eta); c.Parameters.AddWithValue("@reason", reason); c.Parameters.AddWithValue("@key", key); c.Parameters.AddWithValue("@id", id); BindScope(c, companyId, branchId); }, ct);
        var orderCount = await db.ExecuteAsync("UPDATE fleet_tms_dispatch_orders SET status='Exception', dispatch_notes=@reason, updated_at_utc=NOW() WHERE company_id=@companyId AND (@branchId::BIGINT IS NULL OR branch_id=@branchId) AND order_number=@num",
            c => { c.Parameters.AddWithValue("@reason", reason); BindScope(c, companyId, branchId); c.Parameters.AddWithValue("@num", stop["orderNumber"]?.ToString() ?? ""); }, ct);
        var routeCount = await db.ExecuteAsync("UPDATE fleet_tms_delivery_routes SET status='Delayed', notes=@reason WHERE company_id=@companyId AND (@branchId::BIGINT IS NULL OR branch_id=@branchId) AND route_code=@route AND status NOT IN ('Closed','Completed')",
            c => { c.Parameters.AddWithValue("@reason", reason); BindScope(c, companyId, branchId); c.Parameters.AddWithValue("@route", stop["routeCode"]?.ToString() ?? ""); }, ct);
        if (orderCount != 1 || routeCount != 1) throw new InvalidOperationException("Last-mile stop is not linked to exactly one authorized open order and route.");
        await RecordActionReceipt(db, companyId, operation, key, requestHash, id, ct);
        return Ok(await Row(db, "fleet_tms_last_mile_stops", companyId, branchId, id, ct)!);
        }, ct);
    }
}

// ── Request DTOs ──
public record LogisticsOrderRequest(string? OrderNumber, string? CustomerName, string? CustomerSegment, string? SalesChannel, string? City, string? Area, string? Status, string? Priority, int? ItemCount, decimal? OrderValue, string? RouteCode, string? DriverName, string? VehicleNumber, string? DispatchNotes, DateTime? PromisedAtUtc);
public record LogisticsRouteRequest(string? RouteCode, string? Hub, string? Territory, string? DriverName, string? VehicleNumber, string? Status, int? PlannedStops, int? CompletedStops, decimal? DistanceKm, decimal? CompletionPercent, string? CurrentStop, string? NextStop, DateTime? PlannedForDate, DateTime? DepartureTimeUtc, DateTime? EtaCompleteUtc, string? Notes);
public record DispatchOrderRequest(string? RouteCode, string? DriverName, string? VehicleNumber, string? Notes);
public record RouteProgressRequest(int CompletedStopsDelta, string? CurrentStop, string? NextStop, DateTime? EtaCompleteUtc, string? Notes, string? IdempotencyKey = null);
public record ConfirmDeliveryRequest(string? RecipientName, string? ProofStatus, string? ExceptionReason, string? EvidenceReference = null, string? IdempotencyKey = null);
public record StopAttemptRequest(string? Status, string? ProofStatus, string? ExceptionReason, DateTime? NextEtaUtc, string? NextStop, string? IdempotencyKey = null);
public record StopRescheduleRequest(DateTime? NextEtaUtc, string? TimeWindow, string? Reason, string? IdempotencyKey = null);
