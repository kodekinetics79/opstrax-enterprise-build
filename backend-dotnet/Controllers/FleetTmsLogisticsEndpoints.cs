using Npgsql;
using Opstrax.Api.Data;
using Opstrax.Api.DTOs;

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
        app.MapPost("/api/fleet-tms/logistics/orders/{id:long}/dispatch", DispatchOrder);
        app.MapPost("/api/fleet-tms/logistics/routes/{id:long}/progress", ProgressRoute);
        app.MapPost("/api/fleet-tms/logistics/stops/{id:long}/deliver", ConfirmDelivery);
        app.MapPost("/api/fleet-tms/logistics/stops/{id:long}/attempt", RecordAttempt);
        app.MapPost("/api/fleet-tms/logistics/stops/{id:long}/reschedule", RescheduleStop);
    }

    private static long Cid(HttpContext http) => EndpointMappings.GetCompanyId(http);
    private static IResult Ok<T>(T data) => Results.Ok(ApiResponse<object>.Ok(data!));
    private static IResult NotFound(string m = "Not found") => Results.NotFound(ApiResponse<object>.Fail(m));
    private static IResult Bad(string m) => Results.BadRequest(ApiResponse<object>.Fail(m));
    private static object S(string? v) => (object?)v ?? DBNull.Value;
    private static object N(decimal? v) => (object?)v ?? DBNull.Value;
    private static object I(int? v) => (object?)v ?? DBNull.Value;
    private static object Dt(DateTime? v) => (object?)v ?? DBNull.Value;

    private static IResult? RequireView(HttpContext http) => EndpointMappings.RequirePermission(http, "dispatch:view");
    private static IResult? RequireManage(HttpContext http) => EndpointMappings.RequirePermission(http, "dispatch:manage");

    private static async Task<Dictionary<string, object?>?> Row(Database db, string table, long companyId, long id, CancellationToken ct)
        => await db.QuerySingleAsync($"SELECT * FROM {table} WHERE id=@id AND company_id=@companyId",
            c => { c.Parameters.AddWithValue("@id", id); c.Parameters.AddWithValue("@companyId", companyId); }, ct);

    // ── Overview ────────────────────────────────────────────────────────────────

    private static async Task<IResult> Overview(HttpContext http, Database db, CancellationToken ct)
    {
        if (RequireView(http) is { } denied) return denied;
        var companyId = Cid(http);
        void B(NpgsqlCommand c) => c.Parameters.AddWithValue("@companyId", companyId);
        var summary = new
        {
            activeOrders = await db.ScalarLongAsync("SELECT COUNT(*) FROM fleet_tms_dispatch_orders WHERE company_id=@companyId AND status NOT IN ('Delivered','Returned')", B, ct),
            inTransit = await db.ScalarLongAsync("SELECT COUNT(*) FROM fleet_tms_dispatch_orders WHERE company_id=@companyId AND status IN ('Dispatched','InTransit')", B, ct),
            deliveredToday = await db.ScalarLongAsync("SELECT COUNT(*) FROM fleet_tms_dispatch_orders WHERE company_id=@companyId AND status='Delivered' AND delivered_at_utc >= date_trunc('day', NOW())", B, ct),
            exceptionOrders = await db.ScalarLongAsync("SELECT COUNT(*) FROM fleet_tms_dispatch_orders WHERE company_id=@companyId AND status='Exception'", B, ct),
            activeRoutes = await db.ScalarLongAsync("SELECT COUNT(*) FROM fleet_tms_delivery_routes WHERE company_id=@companyId AND status IN ('Ready','Active','Delayed')", B, ct),
            onTimeRate = Math.Round(await db.ScalarDecimalAsync("SELECT COALESCE(AVG(completion_percent),0) FROM fleet_tms_delivery_routes WHERE company_id=@companyId", B, ct) ?? 0m, 1),
        };
        var routeCards = await db.QueryAsync("SELECT id, route_code, hub, territory, driver_name, vehicle_number, status, planned_stops, completed_stops, completion_percent, current_stop, next_stop, notes FROM fleet_tms_delivery_routes WHERE company_id=@companyId ORDER BY completed_stops DESC, route_code LIMIT 4", B, ct);
        var orderCards = await db.QueryAsync("SELECT id, order_number, customer_name, city, area, priority, status, route_code, driver_name, vehicle_number, item_count, order_value, promised_at_utc, dispatched_at_utc, delivered_at_utc, dispatch_notes FROM fleet_tms_dispatch_orders WHERE company_id=@companyId ORDER BY created_at_utc DESC LIMIT 5", B, ct);
        var alerts = await db.QueryAsync("SELECT order_number, customer_name, route_code, status, exception_reason, attempt_count, rider_name, eta_utc FROM fleet_tms_last_mile_stops WHERE company_id=@companyId AND (status IN ('Attempted','Failed') OR exception_reason <> '') ORDER BY created_at_utc DESC LIMIT 4", B, ct);
        var liveStops = await db.QueryAsync("SELECT id, order_number, customer_name, address_line, city, route_code, status, proof_status, rider_name, time_window, attempt_count, eta_utc FROM fleet_tms_last_mile_stops WHERE company_id=@companyId ORDER BY eta_utc DESC LIMIT 6", B, ct);
        return Ok(new { generatedAtUtc = DateTime.UtcNow, summary, alerts, routeCards, orderCards, liveStops });
    }

    // ── Orders ──────────────────────────────────────────────────────────────────

    private static async Task<IResult> Orders(HttpContext http, Database db, CancellationToken ct, string? status = null, int page = 1, int pageSize = 50)
    {
        if (RequireView(http) is { } denied) return denied;
        var companyId = Cid(http);
        page = page < 1 ? 1 : page; pageSize = pageSize is < 1 or > 200 ? 20 : pageSize;
        var where = "WHERE company_id=@companyId" + (string.IsNullOrWhiteSpace(status) ? "" : " AND status=@status");
        void B(NpgsqlCommand c) { c.Parameters.AddWithValue("@companyId", companyId); if (!string.IsNullOrWhiteSpace(status)) c.Parameters.AddWithValue("@status", status); }
        var total = await db.ScalarLongAsync($"SELECT COUNT(*) FROM fleet_tms_dispatch_orders {where}", B, ct);
        var items = await db.QueryAsync($"SELECT * FROM fleet_tms_dispatch_orders {where} ORDER BY created_at_utc DESC OFFSET @offset LIMIT @limit",
            c => { B(c); c.Parameters.AddWithValue("@offset", (page - 1) * pageSize); c.Parameters.AddWithValue("@limit", pageSize); }, ct);
        return Ok(new { total, page, pageSize, items });
    }

    private static async Task<IResult> Order(HttpContext http, long id, Database db, CancellationToken ct)
    {
        if (RequireView(http) is { } denied) return denied;
        var item = await Row(db, "fleet_tms_dispatch_orders", Cid(http), id, ct);
        return item is null ? NotFound() : Ok(item);
    }

    private static async Task<IResult> CreateOrder(HttpContext http, LogisticsOrderRequest req, Database db, CancellationToken ct)
    {
        if (RequireManage(http) is { } denied) return denied;
        if (string.IsNullOrWhiteSpace(req.OrderNumber)) return Bad("Order number is required.");
        if (string.IsNullOrWhiteSpace(req.CustomerName)) return Bad("Customer name is required.");
        var companyId = Cid(http);
        var id = await db.InsertAsync(@"
INSERT INTO fleet_tms_dispatch_orders (company_id, order_number, customer_name, customer_segment, sales_channel, city, area, status, priority, item_count, order_value, route_code, driver_name, vehicle_number, dispatch_notes, created_at_utc, promised_at_utc, updated_at_utc)
VALUES (@companyId, @num, @customer, @segment, @channel, @city, @area, @status, @priority, @items, @value, @route, @driver, @vehicle, @notes, NOW(), @promised, NOW())",
            c =>
            {
                c.Parameters.AddWithValue("@companyId", companyId);
                c.Parameters.AddWithValue("@num", req.OrderNumber.Trim());
                c.Parameters.AddWithValue("@customer", req.CustomerName.Trim());
                c.Parameters.AddWithValue("@segment", req.CustomerSegment?.Trim() ?? "Retail");
                c.Parameters.AddWithValue("@channel", req.SalesChannel?.Trim() ?? "Portal");
                c.Parameters.AddWithValue("@city", req.City?.Trim() ?? "");
                c.Parameters.AddWithValue("@area", req.Area?.Trim() ?? "");
                c.Parameters.AddWithValue("@status", req.Status?.Trim() ?? "Queued");
                c.Parameters.AddWithValue("@priority", req.Priority?.Trim() ?? "Normal");
                c.Parameters.AddWithValue("@items", req.ItemCount ?? 1);
                c.Parameters.AddWithValue("@value", req.OrderValue ?? 0m);
                c.Parameters.AddWithValue("@route", req.RouteCode?.Trim() ?? "");
                c.Parameters.AddWithValue("@driver", req.DriverName?.Trim() ?? "");
                c.Parameters.AddWithValue("@vehicle", req.VehicleNumber?.Trim() ?? "");
                c.Parameters.AddWithValue("@notes", req.DispatchNotes?.Trim() ?? "");
                c.Parameters.AddWithValue("@promised", Dt(req.PromisedAtUtc));
            }, ct);
        return Ok(await Row(db, "fleet_tms_dispatch_orders", companyId, id, ct)!);
    }

    private static async Task<IResult> UpdateOrder(HttpContext http, long id, LogisticsOrderRequest req, Database db, CancellationToken ct)
    {
        if (RequireManage(http) is { } denied) return denied;
        var companyId = Cid(http);
        var rows = await db.ExecuteAsync(@"
UPDATE fleet_tms_dispatch_orders SET
  customer_name=COALESCE(NULLIF(@customer,''), customer_name), customer_segment=COALESCE(NULLIF(@segment,''), customer_segment),
  sales_channel=COALESCE(NULLIF(@channel,''), sales_channel), city=COALESCE(NULLIF(@city,''), city), area=COALESCE(NULLIF(@area,''), area),
  status=COALESCE(NULLIF(@status,''), status), priority=COALESCE(NULLIF(@priority,''), priority),
  item_count=COALESCE(@items, item_count), order_value=COALESCE(@value, order_value),
  route_code=COALESCE(NULLIF(@route,''), route_code), driver_name=COALESCE(NULLIF(@driver,''), driver_name),
  vehicle_number=COALESCE(NULLIF(@vehicle,''), vehicle_number), dispatch_notes=COALESCE(NULLIF(@notes,''), dispatch_notes),
  promised_at_utc=COALESCE(@promised, promised_at_utc), updated_at_utc=NOW()
WHERE id=@id AND company_id=@companyId",
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
                c.Parameters.AddWithValue("@companyId", companyId);
            }, ct);
        if (rows == 0) return NotFound();
        return Ok(await Row(db, "fleet_tms_dispatch_orders", companyId, id, ct)!);
    }

    // ── Routes ──────────────────────────────────────────────────────────────────

    private static async Task<IResult> Routes(HttpContext http, Database db, string? status, CancellationToken ct)
    {
        if (RequireView(http) is { } denied) return denied;
        var companyId = Cid(http);
        var where = "WHERE company_id=@companyId" + (string.IsNullOrWhiteSpace(status) ? "" : " AND status=@status");
        var items = await db.QueryAsync($"SELECT * FROM fleet_tms_delivery_routes {where} ORDER BY planned_for_date DESC, route_code",
            c => { c.Parameters.AddWithValue("@companyId", companyId); if (!string.IsNullOrWhiteSpace(status)) c.Parameters.AddWithValue("@status", status); }, ct);
        return Ok(new { items });
    }

    private static async Task<IResult> CreateRoute(HttpContext http, LogisticsRouteRequest req, Database db, CancellationToken ct)
    {
        if (RequireManage(http) is { } denied) return denied;
        if (string.IsNullOrWhiteSpace(req.RouteCode)) return Bad("Route code is required.");
        var companyId = Cid(http);
        var id = await db.InsertAsync(@"
INSERT INTO fleet_tms_delivery_routes (company_id, route_code, hub, territory, driver_name, vehicle_number, status, planned_stops, completed_stops, distance_km, completion_percent, current_stop, next_stop, planned_for_date, departure_time_utc, eta_complete_utc, notes)
VALUES (@companyId, @code, @hub, @territory, @driver, @vehicle, @status, @planned, @completed, @distance, @percent, @current, @next, @forDate, @departure, @eta, @notes)",
            c => BindRoute(c, companyId, req, isCreate: true), ct);
        return Ok(await Row(db, "fleet_tms_delivery_routes", companyId, id, ct)!);
    }

    private static void BindRoute(NpgsqlCommand c, long companyId, LogisticsRouteRequest req, bool isCreate)
    {
        c.Parameters.AddWithValue("@companyId", companyId);
        c.Parameters.AddWithValue("@code", req.RouteCode?.Trim() ?? "");
        c.Parameters.AddWithValue("@hub", req.Hub?.Trim() ?? "");
        c.Parameters.AddWithValue("@territory", req.Territory?.Trim() ?? "");
        c.Parameters.AddWithValue("@driver", req.DriverName?.Trim() ?? "");
        c.Parameters.AddWithValue("@vehicle", req.VehicleNumber?.Trim() ?? "");
        c.Parameters.AddWithValue("@status", req.Status?.Trim() ?? "Planned");
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
        if (RequireManage(http) is { } denied) return denied;
        var companyId = Cid(http);
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
WHERE id=@id AND company_id=@companyId",
            c => { BindRoute(c, companyId, req, isCreate: false); c.Parameters.AddWithValue("@id", id); }, ct);
        if (rows == 0) return NotFound();
        return Ok(await Row(db, "fleet_tms_delivery_routes", companyId, id, ct)!);
    }

    private static async Task<IResult> RouteStops(HttpContext http, long id, Database db, CancellationToken ct)
    {
        if (RequireView(http) is { } denied) return denied;
        var companyId = Cid(http);
        var route = await Row(db, "fleet_tms_delivery_routes", companyId, id, ct);
        if (route is null) return NotFound();
        var items = await db.QueryAsync("SELECT * FROM fleet_tms_last_mile_stops WHERE company_id=@companyId AND route_code=@route ORDER BY eta_utc",
            c => { c.Parameters.AddWithValue("@companyId", companyId); c.Parameters.AddWithValue("@route", route["routeCode"]?.ToString() ?? ""); }, ct);
        return Ok(new { items });
    }

    private static async Task<IResult> LastMile(HttpContext http, Database db, CancellationToken ct, string? status = null, int page = 1, int pageSize = 50)
    {
        if (RequireView(http) is { } denied) return denied;
        var companyId = Cid(http);
        page = page < 1 ? 1 : page; pageSize = pageSize is < 1 or > 200 ? 20 : pageSize;
        var where = "WHERE company_id=@companyId" + (string.IsNullOrWhiteSpace(status) ? "" : " AND status=@status");
        void B(NpgsqlCommand c) { c.Parameters.AddWithValue("@companyId", companyId); if (!string.IsNullOrWhiteSpace(status)) c.Parameters.AddWithValue("@status", status); }
        var total = await db.ScalarLongAsync($"SELECT COUNT(*) FROM fleet_tms_last_mile_stops {where}", B, ct);
        var items = await db.QueryAsync($"SELECT * FROM fleet_tms_last_mile_stops {where} ORDER BY eta_utc DESC OFFSET @offset LIMIT @limit",
            c => { B(c); c.Parameters.AddWithValue("@offset", (page - 1) * pageSize); c.Parameters.AddWithValue("@limit", pageSize); }, ct);
        return Ok(new { total, page, pageSize, items });
    }

    // ── Workflow actions ────────────────────────────────────────────────────────

    private static async Task<IResult> DispatchOrder(HttpContext http, long id, DispatchOrderRequest req, Database db, CancellationToken ct)
    {
        if (RequireManage(http) is { } denied) return denied;
        var companyId = Cid(http);
        var order = await Row(db, "fleet_tms_dispatch_orders", companyId, id, ct);
        if (order is null) return NotFound();
        var status = order["status"]?.ToString();
        if (status is "Delivered" or "Returned") return Bad("Delivered or returned orders cannot be dispatched again.");

        await db.ExecuteAsync(@"
UPDATE fleet_tms_dispatch_orders SET status='Dispatched', driver_name=COALESCE(NULLIF(@driver,''), driver_name),
  vehicle_number=COALESCE(NULLIF(@vehicle,''), vehicle_number), route_code=COALESCE(NULLIF(@route,''), route_code),
  dispatch_notes=COALESCE(NULLIF(@notes,''), dispatch_notes), dispatched_at_utc=NOW(), updated_at_utc=NOW()
WHERE id=@id AND company_id=@companyId",
            c => { c.Parameters.AddWithValue("@driver", req.DriverName?.Trim() ?? ""); c.Parameters.AddWithValue("@vehicle", req.VehicleNumber?.Trim() ?? ""); c.Parameters.AddWithValue("@route", req.RouteCode?.Trim() ?? ""); c.Parameters.AddWithValue("@notes", req.Notes?.Trim() ?? ""); c.Parameters.AddWithValue("@id", id); c.Parameters.AddWithValue("@companyId", companyId); }, ct);

        var updated = await Row(db, "fleet_tms_dispatch_orders", companyId, id, ct)!;
        var routeCode = updated!["routeCode"]?.ToString() ?? "";
        var orderNumber = updated["orderNumber"]?.ToString() ?? "";
        var driver = updated["driverName"]?.ToString() ?? "";
        await db.ExecuteAsync("UPDATE fleet_tms_delivery_routes SET status='Ready' WHERE company_id=@companyId AND route_code=@route AND status='Planned'",
            c => { c.Parameters.AddWithValue("@companyId", companyId); c.Parameters.AddWithValue("@route", routeCode); }, ct);
        await db.ExecuteAsync("UPDATE fleet_tms_last_mile_stops SET status='OutForDelivery', rider_name=@driver, updated_at_utc=NOW() WHERE company_id=@companyId AND order_number=@num",
            c => { c.Parameters.AddWithValue("@driver", driver); c.Parameters.AddWithValue("@companyId", companyId); c.Parameters.AddWithValue("@num", orderNumber); }, ct);
        return Ok(updated);
    }

    private static async Task<IResult> ProgressRoute(HttpContext http, long id, RouteProgressRequest req, Database db, CancellationToken ct)
    {
        if (RequireManage(http) is { } denied) return denied;
        var companyId = Cid(http);
        var route = await Row(db, "fleet_tms_delivery_routes", companyId, id, ct);
        if (route is null) return NotFound();
        var planned = Convert.ToInt32(route["plannedStops"]);
        var completed = Math.Min(planned, Convert.ToInt32(route["completedStops"]) + Math.Max(1, req.CompletedStopsDelta));
        var percent = planned == 0 ? 0m : Math.Round(completed / (decimal)planned * 100m, 1);
        var status = completed >= planned ? "Closed" : completed > 0 ? "Active" : route["status"]?.ToString();
        await db.ExecuteAsync(@"
UPDATE fleet_tms_delivery_routes SET completed_stops=@completed, completion_percent=@percent, status=@status,
  current_stop=COALESCE(@current, current_stop), next_stop=COALESCE(@next, next_stop), eta_complete_utc=COALESCE(@eta, eta_complete_utc), notes=COALESCE(@notes, notes)
WHERE id=@id AND company_id=@companyId",
            c => { c.Parameters.AddWithValue("@completed", completed); c.Parameters.AddWithValue("@percent", percent); c.Parameters.AddWithValue("@status", S(status)); c.Parameters.AddWithValue("@current", S(req.CurrentStop)); c.Parameters.AddWithValue("@next", S(req.NextStop)); c.Parameters.AddWithValue("@eta", Dt(req.EtaCompleteUtc)); c.Parameters.AddWithValue("@notes", S(req.Notes)); c.Parameters.AddWithValue("@id", id); c.Parameters.AddWithValue("@companyId", companyId); }, ct);
        return Ok(await Row(db, "fleet_tms_delivery_routes", companyId, id, ct)!);
    }

    private static async Task<IResult> ConfirmDelivery(HttpContext http, long id, ConfirmDeliveryRequest req, Database db, CancellationToken ct)
    {
        if (RequireManage(http) is { } denied) return denied;
        var companyId = Cid(http);
        var stop = await Row(db, "fleet_tms_last_mile_stops", companyId, id, ct);
        if (stop is null) return NotFound();
        await db.ExecuteAsync(@"
UPDATE fleet_tms_last_mile_stops SET status='Delivered', proof_status=@proof, recipient_name=COALESCE(NULLIF(@recipient,''), recipient_name),
  delivered_at_utc=NOW(), exception_reason=@exception, attempt_count=GREATEST(attempt_count, 1), updated_at_utc=NOW()
WHERE id=@id AND company_id=@companyId",
            c => { c.Parameters.AddWithValue("@proof", req.ProofStatus?.Trim() ?? "POD"); c.Parameters.AddWithValue("@recipient", req.RecipientName?.Trim() ?? ""); c.Parameters.AddWithValue("@exception", req.ExceptionReason?.Trim() ?? ""); c.Parameters.AddWithValue("@id", id); c.Parameters.AddWithValue("@companyId", companyId); }, ct);

        var orderNumber = stop["orderNumber"]?.ToString() ?? "";
        var routeCode = stop["routeCode"]?.ToString() ?? "";
        var customer = stop["customerName"]?.ToString() ?? "";
        await db.ExecuteAsync("UPDATE fleet_tms_dispatch_orders SET status='Delivered', delivered_at_utc=NOW(), updated_at_utc=NOW() WHERE company_id=@companyId AND order_number=@num",
            c => { c.Parameters.AddWithValue("@companyId", companyId); c.Parameters.AddWithValue("@num", orderNumber); }, ct);
        await db.ExecuteAsync(@"
UPDATE fleet_tms_delivery_routes SET completed_stops=LEAST(planned_stops, completed_stops + 1),
  completion_percent=CASE WHEN planned_stops=0 THEN 0 ELSE ROUND(LEAST(planned_stops, completed_stops + 1) / planned_stops::numeric * 100, 1) END,
  status=CASE WHEN LEAST(planned_stops, completed_stops + 1) >= planned_stops THEN 'Closed' ELSE 'Active' END,
  current_stop=@customer
WHERE company_id=@companyId AND route_code=@route",
            c => { c.Parameters.AddWithValue("@customer", customer); c.Parameters.AddWithValue("@companyId", companyId); c.Parameters.AddWithValue("@route", routeCode); }, ct);
        return Ok(await Row(db, "fleet_tms_last_mile_stops", companyId, id, ct)!);
    }

    private static async Task<IResult> RecordAttempt(HttpContext http, long id, StopAttemptRequest req, Database db, CancellationToken ct)
    {
        if (RequireManage(http) is { } denied) return denied;
        var companyId = Cid(http);
        var stop = await Row(db, "fleet_tms_last_mile_stops", companyId, id, ct);
        if (stop is null) return NotFound();
        var newStatus = req.Status?.Trim() ?? "Attempted";
        await db.ExecuteAsync(@"
UPDATE fleet_tms_last_mile_stops SET status=@status, exception_reason=@exception, attempt_count=GREATEST(1, attempt_count + 1),
  proof_status=COALESCE(NULLIF(@proof,''), proof_status), eta_utc=COALESCE(@nextEta, eta_utc + INTERVAL '4 hours'), updated_at_utc=NOW()
WHERE id=@id AND company_id=@companyId",
            c => { c.Parameters.AddWithValue("@status", newStatus); c.Parameters.AddWithValue("@exception", req.ExceptionReason?.Trim() ?? "Delivery attempt recorded."); c.Parameters.AddWithValue("@proof", req.ProofStatus?.Trim() ?? ""); c.Parameters.AddWithValue("@nextEta", Dt(req.NextEtaUtc)); c.Parameters.AddWithValue("@id", id); c.Parameters.AddWithValue("@companyId", companyId); }, ct);

        var orderNumber = stop["orderNumber"]?.ToString() ?? "";
        var routeCode = stop["routeCode"]?.ToString() ?? "";
        var customer = stop["customerName"]?.ToString() ?? "";
        await db.ExecuteAsync("UPDATE fleet_tms_dispatch_orders SET status=@status, dispatch_notes=COALESCE(NULLIF(@notes,''), dispatch_notes), updated_at_utc=NOW() WHERE company_id=@companyId AND order_number=@num",
            c => { c.Parameters.AddWithValue("@status", newStatus == "Failed" ? "Exception" : "InTransit"); c.Parameters.AddWithValue("@notes", req.ExceptionReason?.Trim() ?? ""); c.Parameters.AddWithValue("@companyId", companyId); c.Parameters.AddWithValue("@num", orderNumber); }, ct);
        await db.ExecuteAsync("UPDATE fleet_tms_delivery_routes SET status=CASE WHEN @failed THEN 'Delayed' ELSE status END, current_stop=@customer, next_stop=COALESCE(NULLIF(@next,''), next_stop), notes=COALESCE(NULLIF(@notes,''), notes) WHERE company_id=@companyId AND route_code=@route",
            c => { c.Parameters.AddWithValue("@failed", newStatus == "Failed"); c.Parameters.AddWithValue("@customer", customer); c.Parameters.AddWithValue("@next", req.NextStop?.Trim() ?? ""); c.Parameters.AddWithValue("@notes", req.ExceptionReason?.Trim() ?? ""); c.Parameters.AddWithValue("@companyId", companyId); c.Parameters.AddWithValue("@route", routeCode); }, ct);
        return Ok(await Row(db, "fleet_tms_last_mile_stops", companyId, id, ct)!);
    }

    private static async Task<IResult> RescheduleStop(HttpContext http, long id, StopRescheduleRequest req, Database db, CancellationToken ct)
    {
        if (RequireManage(http) is { } denied) return denied;
        var companyId = Cid(http);
        var stop = await Row(db, "fleet_tms_last_mile_stops", companyId, id, ct);
        if (stop is null) return NotFound();
        var reason = req.Reason?.Trim() ?? "Stop rescheduled.";
        await db.ExecuteAsync(@"
UPDATE fleet_tms_last_mile_stops SET status='Rescheduled', time_window=COALESCE(NULLIF(@window,''), time_window),
  eta_utc=COALESCE(@nextEta, eta_utc + INTERVAL '6 hours'), exception_reason=@reason, updated_at_utc=NOW()
WHERE id=@id AND company_id=@companyId",
            c => { c.Parameters.AddWithValue("@window", req.TimeWindow?.Trim() ?? ""); c.Parameters.AddWithValue("@nextEta", Dt(req.NextEtaUtc)); c.Parameters.AddWithValue("@reason", reason); c.Parameters.AddWithValue("@id", id); c.Parameters.AddWithValue("@companyId", companyId); }, ct);
        await db.ExecuteAsync("UPDATE fleet_tms_dispatch_orders SET status='Exception', dispatch_notes=@reason, updated_at_utc=NOW() WHERE company_id=@companyId AND order_number=@num",
            c => { c.Parameters.AddWithValue("@reason", reason); c.Parameters.AddWithValue("@companyId", companyId); c.Parameters.AddWithValue("@num", stop["orderNumber"]?.ToString() ?? ""); }, ct);
        return Ok(await Row(db, "fleet_tms_last_mile_stops", companyId, id, ct)!);
    }
}

// ── Request DTOs ──
public record LogisticsOrderRequest(string? OrderNumber, string? CustomerName, string? CustomerSegment, string? SalesChannel, string? City, string? Area, string? Status, string? Priority, int? ItemCount, decimal? OrderValue, string? RouteCode, string? DriverName, string? VehicleNumber, string? DispatchNotes, DateTime? PromisedAtUtc);
public record LogisticsRouteRequest(string? RouteCode, string? Hub, string? Territory, string? DriverName, string? VehicleNumber, string? Status, int? PlannedStops, int? CompletedStops, decimal? DistanceKm, decimal? CompletionPercent, string? CurrentStop, string? NextStop, DateTime? PlannedForDate, DateTime? DepartureTimeUtc, DateTime? EtaCompleteUtc, string? Notes);
public record DispatchOrderRequest(string? RouteCode, string? DriverName, string? VehicleNumber, string? Notes);
public record RouteProgressRequest(int CompletedStopsDelta, string? CurrentStop, string? NextStop, DateTime? EtaCompleteUtc, string? Notes);
public record ConfirmDeliveryRequest(string? RecipientName, string? ProofStatus, string? ExceptionReason);
public record StopAttemptRequest(string? Status, string? ProofStatus, string? ExceptionReason, DateTime? NextEtaUtc, string? NextStop);
public record StopRescheduleRequest(DateTime? NextEtaUtc, string? TimeWindow, string? Reason);
