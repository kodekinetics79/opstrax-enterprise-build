using Npgsql;
using Opstrax.Api.Data;
using Opstrax.Api.DTOs;

namespace Opstrax.Api.Controllers;

// Fleet TMS (PR1) endpoints — ported from the Zayra opstrax-codex-backup branch
// (FleetTmsController + FleetTmsLifecycleController), re-namespaced to Opstrax.Api
// and rewritten from EF Core onto the repo's raw-Npgsql `Database` helper.
//
// ADDITIVE & ISOLATED: every route lives under /api/fleet-tms/* (authenticated,
// tenant-scoped via the existing session middleware) or /api/public/shipments/*
// (anonymous, token-scoped — bypass registered in Program.cs). Nothing here touches
// existing tables, endpoints or the canonical P4 dispatch vocabulary.
public static class FleetTmsEndpoints
{
    public static void MapFleetTmsEndpoints(this WebApplication app)
    {
        // ── Fleet TMS workspace overview & list endpoints ──────────────────────
        app.MapGet("/api/fleet-tms/overview", Overview);
        app.MapGet("/api/fleet-tms/shipments", Shipments);
        app.MapGet("/api/fleet-tms/shipments/invoice-ready", InvoiceReady);
        app.MapGet("/api/fleet-tms/vehicles", Vehicles);
        app.MapGet("/api/fleet-tms/tracking", Tracking);
        app.MapGet("/api/fleet-tms/maintenance", Maintenance);
        app.MapGet("/api/fleet-tms/fuel", Fuel);
        app.MapPost("/api/fleet-tms/shipments/{id:long}/dispatch", DispatchShipment);
        app.MapPost("/api/fleet-tms/vehicles/{id:long}/service", ServiceVehicle);
        app.MapPost("/api/fleet-tms/maintenance/{id:long}/close", CloseMaintenance);
        app.MapPost("/api/fleet-tms/fuel/{id:long}/flag", FlagFuelEvent);
        app.MapPost("/api/fleet-tms/shipments/{id:long}/mark-invoice-ready", MarkInvoiceReady);

        // ── Shipment lifecycle (stops / POD / events / tracking links) ─────────
        app.MapGet("/api/fleet-tms/shipments/{shipmentId:long}/stops", GetStops);
        app.MapPost("/api/fleet-tms/shipments/{shipmentId:long}/stops", CreateStop);
        app.MapPut("/api/fleet-tms/shipments/{shipmentId:long}/stops/{stopId:long}", UpdateStop);
        app.MapPost("/api/fleet-tms/shipments/{shipmentId:long}/stops/{stopId:long}/arrive", ArriveStop);
        app.MapPost("/api/fleet-tms/shipments/{shipmentId:long}/stops/{stopId:long}/complete", CompleteStop);
        app.MapGet("/api/fleet-tms/shipments/{shipmentId:long}/events", GetShipmentEvents);
        app.MapGet("/api/fleet-tms/shipments/{shipmentId:long}/pod", GetPod);
        app.MapPost("/api/fleet-tms/shipments/{shipmentId:long}/pod", CreatePod);
        app.MapPut("/api/fleet-tms/shipments/{shipmentId:long}/pod/{podId:long}", UpdatePod);
        app.MapPost("/api/fleet-tms/shipments/{shipmentId:long}/pod/{podId:long}/submit", SubmitPod);
        app.MapPost("/api/fleet-tms/shipments/{shipmentId:long}/pod/{podId:long}/verify", VerifyPod);
        app.MapPost("/api/fleet-tms/shipments/{shipmentId:long}/pod/{podId:long}/reject", RejectPod);
        app.MapGet("/api/fleet-tms/shipments/{shipmentId:long}/tracking-link", GetTrackingLinks);
        app.MapPost("/api/fleet-tms/shipments/{shipmentId:long}/tracking-link", CreateTrackingLink);
        app.MapDelete("/api/fleet-tms/shipments/{shipmentId:long}/tracking-link/{linkId:long}", RevokeTrackingLink);

        // ── Driver tasks ──────────────────────────────────────────────────────
        app.MapGet("/api/fleet-tms/driver/tasks", GetDriverTasks);
        app.MapGet("/api/fleet-tms/driver/tasks/{taskId:long}", GetDriverTask);
        app.MapPost("/api/fleet-tms/driver/tasks/{taskId:long}/arrive", ArriveDriverTask);
        app.MapPost("/api/fleet-tms/driver/tasks/{taskId:long}/complete", CompleteDriverTask);
        app.MapPost("/api/fleet-tms/driver/tasks/{taskId:long}/pod", UpsertDriverTaskPod);

        // ── Public customer tracking (anonymous, token-scoped) ─────────────────
        app.MapGet("/api/public/shipments/track/{token}", PublicTrack);
        app.MapGet("/api/public/shipments/track/{token}/events", PublicTrackEvents);
        app.MapGet("/api/public/shipments/track/{token}/pod", PublicTrackPod);
        // POD asset proxy — raw signature/photo/document storage URLs are NEVER
        // returned to anonymous callers; instead the public payload exposes this
        // token-scoped proxy path. Access is re-validated (token must be live &
        // unrevoked) on every fetch and only served from allowlisted storage hosts.
        app.MapGet("/api/public/shipments/track/{token}/pod/{podId:long}/asset/{kind}", PublicTrackPodAsset);
    }

    private static long CompanyId(HttpContext http) => EndpointMappings.GetCompanyId(http);

    private static string Actor(HttpContext http)
        => http.Items.TryGetValue(EndpointMappings.AuthUserIdItemKey, out var u) && u is not null
            ? $"user:{u}" : "system";

    private static IResult Ok<T>(T data, string message = "") => Results.Ok(ApiResponse<object>.Ok(data!, message));
    private static IResult NotFound(string message = "Not found") => Results.NotFound(ApiResponse<object>.Fail(message));
    private static IResult Bad(string message) => Results.BadRequest(ApiResponse<object>.Fail(message));

    // ── Entitlement + usage helpers (revenue foundation) ────────────────────────
    private static Opstrax.Api.Services.EntitlementService Ent(Database db) => new(db);

    // Returns a 403 IResult when the tenant's plan blocks the module, else null.
    private static async Task<IResult?> RequireModule(HttpContext http, Database db, string moduleKey, CancellationToken ct)
    {
        var decision = await Ent(db).CheckModuleAsync(CompanyId(http), moduleKey, ct);
        return decision.Allowed
            ? null
            : Results.Json(ApiResponse<object>.Fail("Feature not entitled", decision.Reason ?? moduleKey),
                statusCode: StatusCodes.Status403Forbidden);
    }

    private static Task Meter(Database db, HttpContext http, string meterKey, string? reference = null, CancellationToken ct = default)
        => Ent(db).RecordAsync(CompanyId(http), meterKey, 1, reference, Actor(http), ct);

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static async Task<Dictionary<string, object?>?> FindShipment(Database db, long companyId, long id, CancellationToken ct)
        => await db.QuerySingleAsync(
            "SELECT * FROM fleet_tms_shipments WHERE id=@id AND company_id=@companyId",
            c => { c.Parameters.AddWithValue("@id", id); c.Parameters.AddWithValue("@companyId", companyId); }, ct);

    private static async Task LogEvent(Database db, long companyId, long shipmentId, string eventType, string message, string actor, string visibility, CancellationToken ct)
        => await db.ExecuteAsync(@"
INSERT INTO fleet_tms_shipment_events (company_id, shipment_id, event_type, message, actor_name, visibility, occurred_at_utc, created_at_utc)
VALUES (@companyId, @shipmentId, @type, @message, @actor, @visibility, NOW(), NOW())",
            c =>
            {
                c.Parameters.AddWithValue("@companyId", companyId);
                c.Parameters.AddWithValue("@shipmentId", shipmentId);
                c.Parameters.AddWithValue("@type", eventType);
                c.Parameters.AddWithValue("@message", message);
                c.Parameters.AddWithValue("@actor", actor);
                c.Parameters.AddWithValue("@visibility", visibility);
            }, ct);

    private static async Task<Dictionary<string, object?>?> RowById(Database db, string table, long companyId, long id, CancellationToken ct)
        => await db.QuerySingleAsync(
            $"SELECT * FROM {table} WHERE id=@id AND company_id=@companyId",
            c => { c.Parameters.AddWithValue("@id", id); c.Parameters.AddWithValue("@companyId", companyId); }, ct);

    private static object S(string? value) => (object?)value ?? DBNull.Value;
    private static object N(decimal? value) => (object?)value ?? DBNull.Value;

    // ── Overview ───────────────────────────────────────────────────────────────

    private static async Task<IResult> Overview(HttpContext http, Database db, CancellationToken ct)
    {
        var companyId = CompanyId(http);
        void Bind(NpgsqlCommand c) => c.Parameters.AddWithValue("@companyId", companyId);

        var summary = new
        {
            activeShipments = await db.ScalarLongAsync("SELECT COUNT(*) FROM fleet_tms_shipments WHERE company_id=@companyId AND status NOT IN ('Delivered','Cancelled')", Bind, ct),
            enRoute = await db.ScalarLongAsync("SELECT COUNT(*) FROM fleet_tms_shipments WHERE company_id=@companyId AND status IN ('PickedUp','InTransit','Loaded')", Bind, ct),
            deliveredToday = await db.ScalarLongAsync("SELECT COUNT(*) FROM fleet_tms_shipments WHERE company_id=@companyId AND status='Delivered' AND delivered_at_utc >= date_trunc('day', NOW())", Bind, ct),
            activeVehicles = await db.ScalarLongAsync("SELECT COUNT(*) FROM fleet_tms_vehicles WHERE company_id=@companyId AND status IN ('Available','OnTrip','Maintenance')", Bind, ct),
            onTripVehicles = await db.ScalarLongAsync("SELECT COUNT(*) FROM fleet_tms_vehicles WHERE company_id=@companyId AND status='OnTrip'", Bind, ct),
            openMaintenance = await db.ScalarLongAsync("SELECT COUNT(*) FROM fleet_tms_maintenance_tickets WHERE company_id=@companyId AND status IN ('Open','InProgress','AwaitingParts')", Bind, ct),
            fuelAlerts = await db.ScalarLongAsync("SELECT COUNT(*) FROM fleet_tms_fuel_events WHERE company_id=@companyId AND anomaly_flag", Bind, ct),
            trackingEvents = await db.ScalarLongAsync("SELECT COUNT(*) FROM fleet_tms_tracking_points WHERE company_id=@companyId", Bind, ct),
            avgFuelLevel = Math.Round(await db.ScalarDecimalAsync("SELECT COALESCE(AVG(fuel_level_percent),0) FROM fleet_tms_vehicles WHERE company_id=@companyId", Bind, ct) ?? 0m, 1),
        };

        var generatedAtUtc = DateTime.UtcNow;
        var shipmentCards = await db.QueryAsync("SELECT * FROM fleet_tms_shipments WHERE company_id=@companyId ORDER BY created_at_utc DESC LIMIT 6", Bind, ct);
        var vehicleCards = await db.QueryAsync("SELECT * FROM fleet_tms_vehicles WHERE company_id=@companyId ORDER BY last_ping_at_utc DESC NULLS LAST, vehicle_number LIMIT 6", Bind, ct);
        var trackingCards = await db.QueryAsync("SELECT * FROM fleet_tms_tracking_points WHERE company_id=@companyId ORDER BY recorded_at_utc DESC LIMIT 6", Bind, ct);
        var maintenanceCards = await db.QueryAsync("SELECT * FROM fleet_tms_maintenance_tickets WHERE company_id=@companyId ORDER BY opened_at_utc DESC LIMIT 6", Bind, ct);
        var fuelCards = await db.QueryAsync("SELECT * FROM fleet_tms_fuel_events WHERE company_id=@companyId ORDER BY recorded_at_utc DESC LIMIT 6", Bind, ct);
        var loadPlanCards = await db.QueryAsync(@"
SELECT route_code, COUNT(*) shipment_count, COALESCE(SUM(weight_kg),0) total_weight_kg, COALESCE(SUM(volume_cbm),0) total_volume_cbm,
       COUNT(*) FILTER (WHERE priority IN ('High','Critical')) high_priority,
       COUNT(*) FILTER (WHERE status='Delivered') delivered
FROM fleet_tms_shipments WHERE company_id=@companyId GROUP BY route_code ORDER BY shipment_count DESC LIMIT 4", Bind, ct);

        return Ok(new { generatedAtUtc, summary, shipmentCards, vehicleCards, trackingCards, maintenanceCards, fuelCards, loadPlanCards });
    }

    // ── List endpoints ──────────────────────────────────────────────────────────

    private static async Task<IResult> Shipments(HttpContext http, Database db, string? status, int page, int pageSize, CancellationToken ct)
    {
        var companyId = CompanyId(http);
        page = page < 1 ? 1 : page;
        pageSize = pageSize is < 1 or > 200 ? 20 : pageSize;
        var where = "WHERE company_id=@companyId" + (string.IsNullOrWhiteSpace(status) ? "" : " AND status=@status");
        void Bind(NpgsqlCommand c)
        {
            c.Parameters.AddWithValue("@companyId", companyId);
            if (!string.IsNullOrWhiteSpace(status)) c.Parameters.AddWithValue("@status", status);
        }
        var total = await db.ScalarLongAsync($"SELECT COUNT(*) FROM fleet_tms_shipments {where}", Bind, ct);
        var items = await db.QueryAsync(
            $"SELECT * FROM fleet_tms_shipments {where} ORDER BY created_at_utc DESC OFFSET @offset LIMIT @limit",
            c => { Bind(c); c.Parameters.AddWithValue("@offset", (page - 1) * pageSize); c.Parameters.AddWithValue("@limit", pageSize); }, ct);
        return Ok(new { total, page, pageSize, items });
    }

    private static async Task<IResult> InvoiceReady(HttpContext http, Database db, CancellationToken ct)
    {
        var items = await db.QueryAsync(
            "SELECT * FROM fleet_tms_shipments WHERE company_id=@companyId AND is_invoice_ready ORDER BY invoice_ready_at_utc DESC",
            c => c.Parameters.AddWithValue("@companyId", CompanyId(http)), ct);
        return Ok(new { items });
    }

    private static async Task<IResult> Vehicles(HttpContext http, Database db, string? status, CancellationToken ct)
    {
        var companyId = CompanyId(http);
        var where = "WHERE company_id=@companyId" + (string.IsNullOrWhiteSpace(status) ? "" : " AND status=@status");
        var items = await db.QueryAsync($"SELECT * FROM fleet_tms_vehicles {where} ORDER BY vehicle_number",
            c => { c.Parameters.AddWithValue("@companyId", companyId); if (!string.IsNullOrWhiteSpace(status)) c.Parameters.AddWithValue("@status", status); }, ct);
        return Ok(new { items });
    }

    private static async Task<IResult> Tracking(HttpContext http, Database db, string? shipmentNumber, int page, int pageSize, CancellationToken ct)
    {
        var companyId = CompanyId(http);
        page = page < 1 ? 1 : page;
        pageSize = pageSize is < 1 or > 200 ? 20 : pageSize;
        var where = "WHERE company_id=@companyId" + (string.IsNullOrWhiteSpace(shipmentNumber) ? "" : " AND shipment_number=@sn");
        void Bind(NpgsqlCommand c)
        {
            c.Parameters.AddWithValue("@companyId", companyId);
            if (!string.IsNullOrWhiteSpace(shipmentNumber)) c.Parameters.AddWithValue("@sn", shipmentNumber);
        }
        var total = await db.ScalarLongAsync($"SELECT COUNT(*) FROM fleet_tms_tracking_points {where}", Bind, ct);
        var items = await db.QueryAsync($"SELECT * FROM fleet_tms_tracking_points {where} ORDER BY recorded_at_utc DESC OFFSET @offset LIMIT @limit",
            c => { Bind(c); c.Parameters.AddWithValue("@offset", (page - 1) * pageSize); c.Parameters.AddWithValue("@limit", pageSize); }, ct);
        return Ok(new { total, page, pageSize, items });
    }

    private static async Task<IResult> Maintenance(HttpContext http, Database db, string? status, int page, int pageSize, CancellationToken ct)
    {
        var companyId = CompanyId(http);
        page = page < 1 ? 1 : page;
        pageSize = pageSize is < 1 or > 200 ? 20 : pageSize;
        var where = "WHERE company_id=@companyId" + (string.IsNullOrWhiteSpace(status) ? "" : " AND status=@status");
        void Bind(NpgsqlCommand c)
        {
            c.Parameters.AddWithValue("@companyId", companyId);
            if (!string.IsNullOrWhiteSpace(status)) c.Parameters.AddWithValue("@status", status);
        }
        var total = await db.ScalarLongAsync($"SELECT COUNT(*) FROM fleet_tms_maintenance_tickets {where}", Bind, ct);
        var items = await db.QueryAsync($"SELECT * FROM fleet_tms_maintenance_tickets {where} ORDER BY opened_at_utc DESC OFFSET @offset LIMIT @limit",
            c => { Bind(c); c.Parameters.AddWithValue("@offset", (page - 1) * pageSize); c.Parameters.AddWithValue("@limit", pageSize); }, ct);
        return Ok(new { total, page, pageSize, items });
    }

    private static async Task<IResult> Fuel(HttpContext http, Database db, bool? anomaliesOnly, int page, int pageSize, CancellationToken ct)
    {
        // Fuel Intelligence entitlement — fuel analytics requires the Fuel package.
        if (await RequireModule(http, db, Opstrax.Api.Services.RevenueSchemaService.Modules.Fuel, ct) is { } denied)
            return denied;
        var companyId = CompanyId(http);
        page = page < 1 ? 1 : page;
        pageSize = pageSize is < 1 or > 200 ? 20 : pageSize;
        var where = "WHERE company_id=@companyId" + (anomaliesOnly == true ? " AND anomaly_flag" : "");
        void Bind(NpgsqlCommand c) => c.Parameters.AddWithValue("@companyId", companyId);
        var total = await db.ScalarLongAsync($"SELECT COUNT(*) FROM fleet_tms_fuel_events {where}", Bind, ct);
        var items = await db.QueryAsync($"SELECT * FROM fleet_tms_fuel_events {where} ORDER BY recorded_at_utc DESC OFFSET @offset LIMIT @limit",
            c => { Bind(c); c.Parameters.AddWithValue("@offset", (page - 1) * pageSize); c.Parameters.AddWithValue("@limit", pageSize); }, ct);
        return Ok(new { total, page, pageSize, items });
    }

    // ── Fleet actions ───────────────────────────────────────────────────────────

    private static async Task<IResult> DispatchShipment(HttpContext http, long id, DispatchShipmentRequest req, Database db, CancellationToken ct)
    {
        var companyId = CompanyId(http);
        var shipment = await FindShipment(db, companyId, id, ct);
        if (shipment is null) return NotFound();

        await db.ExecuteAsync(@"
UPDATE fleet_tms_shipments SET status='InTransit',
    driver_name=COALESCE(NULLIF(@driver,''), driver_name),
    vehicle_number=COALESCE(NULLIF(@vehicle,''), vehicle_number),
    route_code=COALESCE(NULLIF(@route,''), route_code),
    notes=COALESCE(@notes, notes),
    picked_up_at_utc=NOW(), updated_at_utc=NOW()
WHERE id=@id AND company_id=@companyId",
            c =>
            {
                c.Parameters.AddWithValue("@driver", req.DriverName ?? "");
                c.Parameters.AddWithValue("@vehicle", req.VehicleNumber ?? "");
                c.Parameters.AddWithValue("@route", req.RouteCode ?? "");
                c.Parameters.AddWithValue("@notes", S(req.Notes));
                c.Parameters.AddWithValue("@id", id);
                c.Parameters.AddWithValue("@companyId", companyId);
            }, ct);

        await LogEvent(db, companyId, id, "ShipmentDispatched", "Shipment dispatched from the command center.", Actor(http), "Public", ct);
        return Ok(await RowById(db, "fleet_tms_shipments", companyId, id, ct)!);
    }

    private static async Task<IResult> ServiceVehicle(HttpContext http, long id, VehicleServiceRequest req, Database db, CancellationToken ct)
    {
        var companyId = CompanyId(http);
        var rows = await db.ExecuteAsync(@"
UPDATE fleet_tms_vehicles SET status=COALESCE(NULLIF(@status,''),'Maintenance'),
    health_status=COALESCE(NULLIF(@health,''), health_status),
    last_service_at_utc=NOW(), next_service_at_utc=COALESCE(@next, next_service_at_utc),
    notes=COALESCE(@notes, notes), updated_at_utc=NOW()
WHERE id=@id AND company_id=@companyId",
            c =>
            {
                c.Parameters.AddWithValue("@status", req.Status ?? "");
                c.Parameters.AddWithValue("@health", req.HealthStatus ?? "");
                c.Parameters.AddWithValue("@next", (object?)req.NextServiceAtUtc ?? DBNull.Value);
                c.Parameters.AddWithValue("@notes", S(req.Notes));
                c.Parameters.AddWithValue("@id", id);
                c.Parameters.AddWithValue("@companyId", companyId);
            }, ct);
        if (rows == 0) return NotFound();
        return Ok(await RowById(db, "fleet_tms_vehicles", companyId, id, ct)!);
    }

    private static async Task<IResult> CloseMaintenance(HttpContext http, long id, CloseMaintenanceRequest req, Database db, CancellationToken ct)
    {
        var companyId = CompanyId(http);
        var rows = await db.ExecuteAsync(@"
UPDATE fleet_tms_maintenance_tickets SET status=COALESCE(NULLIF(@status,''),'Closed'),
    closed_at_utc=NOW(), actual_cost=COALESCE(@cost, actual_cost),
    notes=COALESCE(@notes, notes), updated_at_utc=NOW()
WHERE id=@id AND company_id=@companyId",
            c =>
            {
                c.Parameters.AddWithValue("@status", req.Status ?? "");
                c.Parameters.AddWithValue("@cost", N(req.ActualCost));
                c.Parameters.AddWithValue("@notes", S(req.Notes));
                c.Parameters.AddWithValue("@id", id);
                c.Parameters.AddWithValue("@companyId", companyId);
            }, ct);
        if (rows == 0) return NotFound();
        return Ok(await RowById(db, "fleet_tms_maintenance_tickets", companyId, id, ct)!);
    }

    private static async Task<IResult> FlagFuelEvent(HttpContext http, long id, FlagFuelEventRequest req, Database db, CancellationToken ct)
    {
        var companyId = CompanyId(http);
        var rows = await db.ExecuteAsync(@"
UPDATE fleet_tms_fuel_events SET anomaly_flag=@flag, notes=COALESCE(@notes, notes), updated_at_utc=NOW()
WHERE id=@id AND company_id=@companyId",
            c =>
            {
                c.Parameters.AddWithValue("@flag", req.AnomalyFlag);
                c.Parameters.AddWithValue("@notes", S(req.Notes));
                c.Parameters.AddWithValue("@id", id);
                c.Parameters.AddWithValue("@companyId", companyId);
            }, ct);
        if (rows == 0) return NotFound();
        await Meter(db, http, "fuel_transactions.monthly", $"fuel:{id}", ct);
        return Ok(await RowById(db, "fleet_tms_fuel_events", companyId, id, ct)!);
    }

    private static async Task<IResult> MarkInvoiceReady(HttpContext http, long id, InvoiceReadyRequest req, Database db, CancellationToken ct)
    {
        var companyId = CompanyId(http);
        var shipment = await FindShipment(db, companyId, id, ct);
        if (shipment is null) return NotFound();

        var podOk = await db.ScalarLongAsync(
            "SELECT COUNT(*) FROM fleet_tms_pods WHERE company_id=@companyId AND shipment_id=@id AND status='Verified'",
            c => { c.Parameters.AddWithValue("@companyId", companyId); c.Parameters.AddWithValue("@id", id); }, ct) > 0;
        if (!podOk && !req.Override)
            return Bad("Shipment cannot be invoice-ready until a verified POD exists or an override is granted.");

        await db.ExecuteAsync(@"
UPDATE fleet_tms_shipments SET is_invoice_ready=true, invoice_ready_at_utc=NOW(),
    invoice_readiness_notes=COALESCE(@notes, invoice_readiness_notes), updated_at_utc=NOW()
WHERE id=@id AND company_id=@companyId",
            c => { c.Parameters.AddWithValue("@notes", S(req.Notes)); c.Parameters.AddWithValue("@id", id); c.Parameters.AddWithValue("@companyId", companyId); }, ct);

        await LogEvent(db, companyId, id, "InvoiceReady", "Shipment marked invoice-ready.", Actor(http), "Private", ct);
        await Meter(db, http, "invoice_ready.monthly", $"shipment:{id}", ct);
        return Ok(await RowById(db, "fleet_tms_shipments", companyId, id, ct)!);
    }

    // ── Stops ─────────────────────────────────────────────────────────────────

    private static async Task<IResult> GetStops(HttpContext http, long shipmentId, Database db, CancellationToken ct)
    {
        var companyId = CompanyId(http);
        if (await FindShipment(db, companyId, shipmentId, ct) is null) return NotFound();
        var items = await db.QueryAsync(
            "SELECT * FROM fleet_tms_shipment_stops WHERE company_id=@companyId AND shipment_id=@sid ORDER BY sequence_no",
            c => { c.Parameters.AddWithValue("@companyId", companyId); c.Parameters.AddWithValue("@sid", shipmentId); }, ct);
        return Ok(new { items });
    }

    private static async Task<IResult> CreateStop(HttpContext http, long shipmentId, ShipmentStopRequest req, Database db, CancellationToken ct)
    {
        var companyId = CompanyId(http);
        if (await FindShipment(db, companyId, shipmentId, ct) is null) return NotFound();
        if (string.IsNullOrWhiteSpace(req.StopType)) return Bad("Stop type is required.");
        if (req.SequenceNo < 1) return Bad("Stop sequence is required.");
        if (string.IsNullOrWhiteSpace(req.LocationName)) return Bad("Location name is required.");

        var id = await db.InsertAsync(@"
INSERT INTO fleet_tms_shipment_stops
 (company_id, shipment_id, stop_type, sequence_no, location_name, contact_name, contact_phone,
  address_line1, address_line2, city, region, postal_code, country,
  saudi_national_address_building_no, saudi_national_address_additional_no, saudi_national_address_district,
  latitude, longitude, planned_arrival_at, status, notes, created_at, updated_at)
VALUES (@companyId, @sid, @stopType, @seq, @loc, @contactName, @contactPhone,
  @addr1, @addr2, @city, @region, @postal, @country,
  @saudiBuilding, @saudiAdditional, @saudiDistrict,
  @lat, @lng, @planned, 'Planned', @notes, NOW(), NOW())",
            c => BindStop(c, companyId, shipmentId, req), ct);

        await LogEvent(db, companyId, shipmentId, "StopAdded", $"Stop {req.SequenceNo} added for {req.LocationName?.Trim()}.", Actor(http), "Private", ct);
        return Ok(await RowById(db, "fleet_tms_shipment_stops", companyId, id, ct)!);
    }

    private static void BindStop(NpgsqlCommand c, long companyId, long shipmentId, ShipmentStopRequest req)
    {
        c.Parameters.AddWithValue("@companyId", companyId);
        c.Parameters.AddWithValue("@sid", shipmentId);
        c.Parameters.AddWithValue("@stopType", req.StopType?.Trim() ?? "Pickup");
        c.Parameters.AddWithValue("@seq", req.SequenceNo);
        c.Parameters.AddWithValue("@loc", req.LocationName?.Trim() ?? "");
        c.Parameters.AddWithValue("@contactName", req.ContactName?.Trim() ?? "");
        c.Parameters.AddWithValue("@contactPhone", req.ContactPhone?.Trim() ?? "");
        c.Parameters.AddWithValue("@addr1", req.AddressLine1?.Trim() ?? "");
        c.Parameters.AddWithValue("@addr2", req.AddressLine2?.Trim() ?? "");
        c.Parameters.AddWithValue("@city", req.City?.Trim() ?? "");
        c.Parameters.AddWithValue("@region", req.Region?.Trim() ?? "");
        c.Parameters.AddWithValue("@postal", req.PostalCode?.Trim() ?? "");
        c.Parameters.AddWithValue("@country", req.Country?.Trim() ?? "");
        c.Parameters.AddWithValue("@saudiBuilding", req.SaudiNationalAddressBuildingNo?.Trim() ?? "");
        c.Parameters.AddWithValue("@saudiAdditional", req.SaudiNationalAddressAdditionalNo?.Trim() ?? "");
        c.Parameters.AddWithValue("@saudiDistrict", req.SaudiNationalAddressDistrict?.Trim() ?? "");
        c.Parameters.AddWithValue("@lat", N(req.Latitude));
        c.Parameters.AddWithValue("@lng", N(req.Longitude));
        c.Parameters.AddWithValue("@planned", (object?)req.PlannedArrivalAt ?? DBNull.Value);
        c.Parameters.AddWithValue("@notes", req.Notes?.Trim() ?? "");
    }

    private static async Task<IResult> UpdateStop(HttpContext http, long shipmentId, long stopId, ShipmentStopRequest req, Database db, CancellationToken ct)
    {
        var companyId = CompanyId(http);
        if (req.SequenceNo < 1) return Bad("Stop sequence is required.");
        if (string.IsNullOrWhiteSpace(req.StopType)) return Bad("Stop type is required.");
        var rows = await db.ExecuteAsync(@"
UPDATE fleet_tms_shipment_stops SET
  stop_type=@stopType, sequence_no=@seq,
  location_name=COALESCE(NULLIF(@loc,''), location_name),
  contact_name=COALESCE(NULLIF(@contactName,''), contact_name),
  contact_phone=COALESCE(NULLIF(@contactPhone,''), contact_phone),
  address_line1=COALESCE(NULLIF(@addr1,''), address_line1),
  address_line2=COALESCE(NULLIF(@addr2,''), address_line2),
  city=COALESCE(NULLIF(@city,''), city), region=COALESCE(NULLIF(@region,''), region),
  postal_code=COALESCE(NULLIF(@postal,''), postal_code), country=COALESCE(NULLIF(@country,''), country),
  saudi_national_address_building_no=COALESCE(NULLIF(@saudiBuilding,''), saudi_national_address_building_no),
  saudi_national_address_additional_no=COALESCE(NULLIF(@saudiAdditional,''), saudi_national_address_additional_no),
  saudi_national_address_district=COALESCE(NULLIF(@saudiDistrict,''), saudi_national_address_district),
  latitude=COALESCE(@lat, latitude), longitude=COALESCE(@lng, longitude),
  planned_arrival_at=@planned, notes=COALESCE(NULLIF(@notes,''), notes), updated_at=NOW()
WHERE id=@stopId AND shipment_id=@sid AND company_id=@companyId",
            c => { BindStop(c, companyId, shipmentId, req); c.Parameters.AddWithValue("@stopId", stopId); }, ct);
        if (rows == 0) return NotFound();
        return Ok(await RowById(db, "fleet_tms_shipment_stops", companyId, stopId, ct)!);
    }

    private static async Task<IResult> ArriveStop(HttpContext http, long shipmentId, long stopId, Database db, CancellationToken ct)
    {
        var companyId = CompanyId(http);
        var stop = await db.QuerySingleAsync(
            "UPDATE fleet_tms_shipment_stops SET status='Arrived', actual_arrival_at=NOW(), updated_at=NOW() WHERE id=@stopId AND shipment_id=@sid AND company_id=@companyId RETURNING *",
            c => { c.Parameters.AddWithValue("@stopId", stopId); c.Parameters.AddWithValue("@sid", shipmentId); c.Parameters.AddWithValue("@companyId", companyId); }, ct);
        if (stop is null) return NotFound();
        await LogEvent(db, companyId, shipmentId, "StopArrived", $"Stop {stop["sequenceNo"]} arrived at {stop["locationName"]}.", Actor(http), "Private", ct);
        return Ok(stop);
    }

    private static async Task<IResult> CompleteStop(HttpContext http, long shipmentId, long stopId, NotesRequest req, Database db, CancellationToken ct)
    {
        var companyId = CompanyId(http);
        var stop = await db.QuerySingleAsync(
            "UPDATE fleet_tms_shipment_stops SET status='Completed', completed_at=NOW(), notes=COALESCE(NULLIF(@notes,''), notes), updated_at=NOW() WHERE id=@stopId AND shipment_id=@sid AND company_id=@companyId RETURNING *",
            c => { c.Parameters.AddWithValue("@notes", req?.Notes?.Trim() ?? ""); c.Parameters.AddWithValue("@stopId", stopId); c.Parameters.AddWithValue("@sid", shipmentId); c.Parameters.AddWithValue("@companyId", companyId); }, ct);
        if (stop is null) return NotFound();
        await LogEvent(db, companyId, shipmentId, "StopCompleted", $"Stop {stop["sequenceNo"]} completed at {stop["locationName"]}.", Actor(http), "Private", ct);
        return Ok(stop);
    }

    // ── Events / tracking links ─────────────────────────────────────────────────

    private static async Task<IResult> GetShipmentEvents(HttpContext http, long shipmentId, Database db, CancellationToken ct)
    {
        var companyId = CompanyId(http);
        if (await FindShipment(db, companyId, shipmentId, ct) is null) return NotFound();
        var items = await db.QueryAsync(
            "SELECT * FROM fleet_tms_shipment_events WHERE company_id=@companyId AND shipment_id=@sid ORDER BY occurred_at_utc DESC",
            c => { c.Parameters.AddWithValue("@companyId", companyId); c.Parameters.AddWithValue("@sid", shipmentId); }, ct);
        return Ok(new { items });
    }

    private static async Task<IResult> GetTrackingLinks(HttpContext http, long shipmentId, Database db, CancellationToken ct)
    {
        var companyId = CompanyId(http);
        if (await FindShipment(db, companyId, shipmentId, ct) is null) return NotFound();
        var items = await db.QueryAsync(
            "SELECT * FROM fleet_tms_tracking_links WHERE company_id=@companyId AND shipment_id=@sid ORDER BY created_at_utc DESC",
            c => { c.Parameters.AddWithValue("@companyId", companyId); c.Parameters.AddWithValue("@sid", shipmentId); }, ct);
        return Ok(new { items });
    }

    private static async Task<IResult> CreateTrackingLink(HttpContext http, long shipmentId, TrackingLinkRequest req, Database db, CancellationToken ct)
    {
        var companyId = CompanyId(http);
        // Customer Visibility entitlement — tenant cannot generate a customer tracking
        // link unless the Customer Visibility package / fleet.customer_tracking module
        // is enabled for the company.
        if (await RequireModule(http, db, Opstrax.Api.Services.RevenueSchemaService.Modules.CustomerTracking, ct) is { } denied)
            return denied;
        if (await FindShipment(db, companyId, shipmentId, ct) is null) return NotFound();
        var token = string.IsNullOrWhiteSpace(req.Token) ? Guid.NewGuid().ToString("N") : req.Token.Trim();
        var id = await db.InsertAsync(@"
INSERT INTO fleet_tms_tracking_links (company_id, shipment_id, token, expires_at_utc, shared_by, created_at_utc)
VALUES (@companyId, @sid, @token, @expires, @sharedBy, NOW())",
            c =>
            {
                c.Parameters.AddWithValue("@companyId", companyId);
                c.Parameters.AddWithValue("@sid", shipmentId);
                c.Parameters.AddWithValue("@token", token);
                c.Parameters.AddWithValue("@expires", (object?)req.ExpiresAtUtc ?? DateTime.UtcNow.AddDays(7));
                c.Parameters.AddWithValue("@sharedBy", Actor(http));
            }, ct);
        await LogEvent(db, companyId, shipmentId, "TrackingLinkCreated", "Customer tracking link generated.", Actor(http), "Public", ct);
        await Meter(db, http, "tracking_links.monthly", $"shipment:{shipmentId}", ct);
        return Ok(await RowById(db, "fleet_tms_tracking_links", companyId, id, ct)!);
    }

    private static async Task<IResult> RevokeTrackingLink(HttpContext http, long shipmentId, long linkId, Database db, CancellationToken ct)
    {
        var companyId = CompanyId(http);
        var rows = await db.ExecuteAsync(
            "UPDATE fleet_tms_tracking_links SET is_revoked=true, revoked_at_utc=NOW(), updated_at_utc=NOW() WHERE id=@linkId AND shipment_id=@sid AND company_id=@companyId",
            c => { c.Parameters.AddWithValue("@linkId", linkId); c.Parameters.AddWithValue("@sid", shipmentId); c.Parameters.AddWithValue("@companyId", companyId); }, ct);
        if (rows == 0) return NotFound();
        return Results.NoContent();
    }

    // ── POD ───────────────────────────────────────────────────────────────────

    private static async Task<IResult> GetPod(HttpContext http, long shipmentId, Database db, CancellationToken ct)
    {
        var companyId = CompanyId(http);
        if (await FindShipment(db, companyId, shipmentId, ct) is null) return NotFound();
        var items = await db.QueryAsync(
            "SELECT * FROM fleet_tms_pods WHERE company_id=@companyId AND shipment_id=@sid ORDER BY captured_at DESC",
            c => { c.Parameters.AddWithValue("@companyId", companyId); c.Parameters.AddWithValue("@sid", shipmentId); }, ct);
        return Ok(new { items });
    }

    private static async Task<IResult> CreatePod(HttpContext http, long shipmentId, PodRequest req, Database db, CancellationToken ct)
    {
        var companyId = CompanyId(http);
        var stop = await db.QuerySingleAsync(
            "SELECT * FROM fleet_tms_shipment_stops WHERE id=@stopId AND shipment_id=@sid AND company_id=@companyId",
            c => { c.Parameters.AddWithValue("@stopId", req.StopId); c.Parameters.AddWithValue("@sid", shipmentId); c.Parameters.AddWithValue("@companyId", companyId); }, ct);
        if (stop is null) return NotFound("Stop not found for shipment.");
        if (string.IsNullOrWhiteSpace(req.RecipientName)) return Bad("Recipient name is required.");
        if (string.IsNullOrWhiteSpace(req.DeliveryCondition)) return Bad("Delivery condition is required.");

        var userId = http.Items.TryGetValue(EndpointMappings.AuthUserIdItemKey, out var u) && u is not null ? (object)Convert.ToInt64(u) : DBNull.Value;
        var id = await db.InsertAsync(@"
INSERT INTO fleet_tms_pods
 (company_id, shipment_id, stop_id, captured_by_user_id, recipient_name, recipient_phone,
  signature_url, photo_url, document_url, notes, delivery_condition, captured_latitude, captured_longitude,
  captured_at, status, created_at, updated_at)
VALUES (@companyId, @sid, @stopId, @uid, @recipient, @phone, @sig, @photo, @doc, @notes, @condition, @lat, @lng, NOW(), 'Draft', NOW(), NOW())",
            c =>
            {
                c.Parameters.AddWithValue("@companyId", companyId);
                c.Parameters.AddWithValue("@sid", shipmentId);
                c.Parameters.AddWithValue("@stopId", req.StopId);
                c.Parameters.AddWithValue("@uid", userId);
                c.Parameters.AddWithValue("@recipient", req.RecipientName.Trim());
                c.Parameters.AddWithValue("@phone", req.RecipientPhone?.Trim() ?? "");
                c.Parameters.AddWithValue("@sig", req.SignatureUrl?.Trim() ?? "");
                c.Parameters.AddWithValue("@photo", req.PhotoUrl?.Trim() ?? "");
                c.Parameters.AddWithValue("@doc", req.DocumentUrl?.Trim() ?? "");
                c.Parameters.AddWithValue("@notes", req.Notes?.Trim() ?? "");
                c.Parameters.AddWithValue("@condition", req.DeliveryCondition.Trim());
                c.Parameters.AddWithValue("@lat", N(req.CapturedLatitude));
                c.Parameters.AddWithValue("@lng", N(req.CapturedLongitude));
            }, ct);

        await LogEvent(db, companyId, shipmentId, "PodCreated", $"POD draft created for stop {stop["sequenceNo"]}.", Actor(http), "Private", ct);
        return Ok(await RowById(db, "fleet_tms_pods", companyId, id, ct)!);
    }

    private static async Task<IResult> UpdatePod(HttpContext http, long shipmentId, long podId, PodRequest req, Database db, CancellationToken ct)
    {
        var companyId = CompanyId(http);
        var rows = await db.ExecuteAsync(@"
UPDATE fleet_tms_pods SET
  recipient_name=COALESCE(NULLIF(@recipient,''), recipient_name),
  recipient_phone=COALESCE(NULLIF(@phone,''), recipient_phone),
  signature_url=COALESCE(NULLIF(@sig,''), signature_url),
  photo_url=COALESCE(NULLIF(@photo,''), photo_url),
  document_url=COALESCE(NULLIF(@doc,''), document_url),
  notes=COALESCE(NULLIF(@notes,''), notes),
  delivery_condition=COALESCE(NULLIF(@condition,''), delivery_condition),
  captured_latitude=COALESCE(@lat, captured_latitude), captured_longitude=COALESCE(@lng, captured_longitude),
  updated_at=NOW()
WHERE id=@podId AND shipment_id=@sid AND company_id=@companyId",
            c =>
            {
                c.Parameters.AddWithValue("@recipient", req.RecipientName?.Trim() ?? "");
                c.Parameters.AddWithValue("@phone", req.RecipientPhone?.Trim() ?? "");
                c.Parameters.AddWithValue("@sig", req.SignatureUrl?.Trim() ?? "");
                c.Parameters.AddWithValue("@photo", req.PhotoUrl?.Trim() ?? "");
                c.Parameters.AddWithValue("@doc", req.DocumentUrl?.Trim() ?? "");
                c.Parameters.AddWithValue("@notes", req.Notes?.Trim() ?? "");
                c.Parameters.AddWithValue("@condition", req.DeliveryCondition?.Trim() ?? "");
                c.Parameters.AddWithValue("@lat", N(req.CapturedLatitude));
                c.Parameters.AddWithValue("@lng", N(req.CapturedLongitude));
                c.Parameters.AddWithValue("@podId", podId);
                c.Parameters.AddWithValue("@sid", shipmentId);
                c.Parameters.AddWithValue("@companyId", companyId);
            }, ct);
        if (rows == 0) return NotFound();
        return Ok(await RowById(db, "fleet_tms_pods", companyId, podId, ct)!);
    }

    private static async Task<IResult> SubmitPod(HttpContext http, long shipmentId, long podId, Database db, CancellationToken ct)
    {
        var companyId = CompanyId(http);
        var pod = await RowById(db, "fleet_tms_pods", companyId, podId, ct);
        if (pod is null || Convert.ToInt64(pod["shipmentId"]) != shipmentId) return NotFound();
        if (string.IsNullOrWhiteSpace(pod["recipientName"]?.ToString())) return Bad("Recipient name is required.");
        if (string.IsNullOrWhiteSpace(pod["deliveryCondition"]?.ToString())) return Bad("Delivery condition is required.");
        await db.ExecuteAsync("UPDATE fleet_tms_pods SET status='Submitted', updated_at=NOW() WHERE id=@podId AND company_id=@companyId",
            c => { c.Parameters.AddWithValue("@podId", podId); c.Parameters.AddWithValue("@companyId", companyId); }, ct);
        await LogEvent(db, companyId, shipmentId, "PodSubmitted", "POD submitted for verification.", Actor(http), "Private", ct);
        await Meter(db, http, "pod.monthly", $"shipment:{shipmentId}", ct);
        return Ok(await RowById(db, "fleet_tms_pods", companyId, podId, ct)!);
    }

    private static async Task<IResult> VerifyPod(HttpContext http, long shipmentId, long podId, Database db, CancellationToken ct)
    {
        var companyId = CompanyId(http);
        var userId = http.Items.TryGetValue(EndpointMappings.AuthUserIdItemKey, out var u) && u is not null ? (object)Convert.ToInt64(u) : DBNull.Value;
        var rows = await db.ExecuteAsync(
            "UPDATE fleet_tms_pods SET status='Verified', verified_at=NOW(), verified_by_user_id=@uid, updated_at=NOW() WHERE id=@podId AND shipment_id=@sid AND company_id=@companyId",
            c => { c.Parameters.AddWithValue("@uid", userId); c.Parameters.AddWithValue("@podId", podId); c.Parameters.AddWithValue("@sid", shipmentId); c.Parameters.AddWithValue("@companyId", companyId); }, ct);
        if (rows == 0) return NotFound();
        await LogEvent(db, companyId, shipmentId, "PodVerified", "POD verified.", Actor(http), "Private", ct);
        return Ok(await RowById(db, "fleet_tms_pods", companyId, podId, ct)!);
    }

    private static async Task<IResult> RejectPod(HttpContext http, long shipmentId, long podId, NotesRequest req, Database db, CancellationToken ct)
    {
        var companyId = CompanyId(http);
        var rows = await db.ExecuteAsync(
            "UPDATE fleet_tms_pods SET status='Rejected', notes=COALESCE(NULLIF(@notes,''), notes), updated_at=NOW() WHERE id=@podId AND shipment_id=@sid AND company_id=@companyId",
            c => { c.Parameters.AddWithValue("@notes", req?.Notes?.Trim() ?? ""); c.Parameters.AddWithValue("@podId", podId); c.Parameters.AddWithValue("@sid", shipmentId); c.Parameters.AddWithValue("@companyId", companyId); }, ct);
        if (rows == 0) return NotFound();
        await LogEvent(db, companyId, shipmentId, "PodRejected", "POD rejected.", Actor(http), "Private", ct);
        return Ok(await RowById(db, "fleet_tms_pods", companyId, podId, ct)!);
    }

    // ── Driver tasks ────────────────────────────────────────────────────────────

    private static async Task<IResult> GetDriverTasks(HttpContext http, Database db, string? driverName, CancellationToken ct)
    {
        var companyId = CompanyId(http);
        var where = "WHERE company_id=@companyId" + (string.IsNullOrWhiteSpace(driverName) ? "" : " AND driver_name=@driver");
        var items = await db.QueryAsync($"SELECT * FROM fleet_tms_driver_tasks {where} ORDER BY due_at_utc",
            c => { c.Parameters.AddWithValue("@companyId", companyId); if (!string.IsNullOrWhiteSpace(driverName)) c.Parameters.AddWithValue("@driver", driverName); }, ct);
        return Ok(new { items });
    }

    private static async Task<IResult> GetDriverTask(HttpContext http, long taskId, Database db, CancellationToken ct)
    {
        var task = await RowById(db, "fleet_tms_driver_tasks", CompanyId(http), taskId, ct);
        return task is null ? NotFound() : Ok(task);
    }

    private static async Task<IResult> ArriveDriverTask(HttpContext http, long taskId, Database db, CancellationToken ct)
    {
        var companyId = CompanyId(http);
        var task = await db.QuerySingleAsync(
            "UPDATE fleet_tms_driver_tasks SET status='Arrived', updated_at_utc=NOW() WHERE id=@taskId AND company_id=@companyId RETURNING *",
            c => { c.Parameters.AddWithValue("@taskId", taskId); c.Parameters.AddWithValue("@companyId", companyId); }, ct);
        if (task is null) return NotFound();
        await LogEvent(db, companyId, Convert.ToInt64(task["shipmentId"]), "DriverTaskArrived", $"{task["title"]} arrived.", Actor(http), "Private", ct);
        return Ok(task);
    }

    private static async Task<IResult> CompleteDriverTask(HttpContext http, long taskId, NotesRequest req, Database db, CancellationToken ct)
    {
        var companyId = CompanyId(http);
        var task = await db.QuerySingleAsync(
            "UPDATE fleet_tms_driver_tasks SET status='Completed', completed_at_utc=NOW(), notes=COALESCE(NULLIF(@notes,''), notes), updated_at_utc=NOW() WHERE id=@taskId AND company_id=@companyId RETURNING *",
            c => { c.Parameters.AddWithValue("@notes", req?.Notes?.Trim() ?? ""); c.Parameters.AddWithValue("@taskId", taskId); c.Parameters.AddWithValue("@companyId", companyId); }, ct);
        if (task is null) return NotFound();
        await LogEvent(db, companyId, Convert.ToInt64(task["shipmentId"]), "DriverTaskCompleted", $"{task["title"]} completed.", Actor(http), "Private", ct);
        return Ok(task);
    }

    private static async Task<IResult> UpsertDriverTaskPod(HttpContext http, long taskId, PodRequest req, Database db, CancellationToken ct)
    {
        var companyId = CompanyId(http);
        var task = await RowById(db, "fleet_tms_driver_tasks", companyId, taskId, ct);
        if (task is null) return NotFound();
        var shipmentId = Convert.ToInt64(task["shipmentId"]);
        var pod = await db.QuerySingleAsync(
            "SELECT * FROM fleet_tms_pods WHERE company_id=@companyId AND shipment_id=@sid ORDER BY captured_at DESC LIMIT 1",
            c => { c.Parameters.AddWithValue("@companyId", companyId); c.Parameters.AddWithValue("@sid", shipmentId); }, ct);
        if (pod is null) return NotFound();
        await db.ExecuteAsync(
            "UPDATE fleet_tms_pods SET recipient_name=COALESCE(NULLIF(@recipient,''), recipient_name), delivery_condition=COALESCE(NULLIF(@condition,''), delivery_condition), updated_at=NOW() WHERE id=@podId AND company_id=@companyId",
            c => { c.Parameters.AddWithValue("@recipient", req.RecipientName?.Trim() ?? ""); c.Parameters.AddWithValue("@condition", req.DeliveryCondition?.Trim() ?? ""); c.Parameters.AddWithValue("@podId", Convert.ToInt64(pod["id"])); c.Parameters.AddWithValue("@companyId", companyId); }, ct);
        return Ok(await RowById(db, "fleet_tms_pods", companyId, Convert.ToInt64(pod["id"]), ct)!);
    }

    // ── Public tracking (anonymous, token-scoped) ───────────────────────────────

    private static async Task<Dictionary<string, object?>?> ResolveLink(Database db, string token, CancellationToken ct)
        => await db.QuerySingleAsync(
            "SELECT * FROM fleet_tms_tracking_links WHERE token=@token AND is_revoked=false AND expires_at_utc > NOW()",
            c => c.Parameters.AddWithValue("@token", token), ct);

    private static async Task<IResult> PublicTrack(string token, Database db, CancellationToken ct)
    {
        var link = await ResolveLink(db, token, ct);
        if (link is null) return NotFound();
        var companyId = Convert.ToInt64(link["companyId"]);
        var shipmentId = Convert.ToInt64(link["shipmentId"]);
        var shipment = await db.QuerySingleAsync(
            "SELECT shipment_number, status, origin, destination, pickup_scheduled_at_utc, delivered_at_utc FROM fleet_tms_shipments WHERE id=@sid AND company_id=@companyId",
            c => { c.Parameters.AddWithValue("@sid", shipmentId); c.Parameters.AddWithValue("@companyId", companyId); }, ct);
        if (shipment is null) return NotFound();

        var stops = await db.QueryAsync(
            "SELECT sequence_no, stop_type, location_name, city, status, planned_arrival_at, actual_arrival_at, completed_at FROM fleet_tms_shipment_stops WHERE company_id=@companyId AND shipment_id=@sid ORDER BY sequence_no",
            c => { c.Parameters.AddWithValue("@companyId", companyId); c.Parameters.AddWithValue("@sid", shipmentId); }, ct);
        var events = await db.QueryAsync(
            "SELECT event_type, message, occurred_at_utc FROM fleet_tms_shipment_events WHERE company_id=@companyId AND shipment_id=@sid AND visibility <> 'Private' ORDER BY occurred_at_utc DESC",
            c => { c.Parameters.AddWithValue("@companyId", companyId); c.Parameters.AddWithValue("@sid", shipmentId); }, ct);
        var podRows = await db.QueryAsync(
            "SELECT id, recipient_name, delivery_condition, status, captured_at, verified_at, signature_url, photo_url, document_url FROM fleet_tms_pods WHERE company_id=@companyId AND shipment_id=@sid AND status <> 'Rejected'",
            c => { c.Parameters.AddWithValue("@companyId", companyId); c.Parameters.AddWithValue("@sid", shipmentId); }, ct);

        return Ok(new
        {
            shipmentNumber = shipment["shipmentNumber"],
            status = shipment["status"],
            origin = shipment["origin"],
            destination = shipment["destination"],
            pickupScheduledAtUtc = shipment["pickupScheduledAtUtc"],
            deliveredAtUtc = shipment["deliveredAtUtc"],
            stops,
            publicEvents = events,
            pod = podRows.Select(p => SafePublicPod(token, p)),
        });
    }

    // Projects a POD row into a public-safe shape. Raw storage URLs are stripped and
    // replaced with token-scoped proxy paths + boolean availability flags, so the
    // anonymous payload never discloses a directly-fetchable asset URL.
    private static object SafePublicPod(string token, Dictionary<string, object?> p)
    {
        var podId = Convert.ToInt64(p["id"]);
        string? Proxy(string column)
        {
            var raw = p.TryGetValue(column, out var v) ? v?.ToString() : null;
            if (string.IsNullOrWhiteSpace(raw)) return null;
            var kind = column.Replace("_url", "");
            return $"/api/public/shipments/track/{token}/pod/{podId}/asset/{kind}";
        }
        return new
        {
            recipientName = p.GetValueOrDefault("recipientName"),
            deliveryCondition = p.GetValueOrDefault("deliveryCondition"),
            status = p.GetValueOrDefault("status"),
            capturedAt = p.GetValueOrDefault("capturedAt"),
            verifiedAt = p.GetValueOrDefault("verifiedAt"),
            hasSignature = !string.IsNullOrWhiteSpace(p.GetValueOrDefault("signatureUrl")?.ToString()),
            hasPhoto = !string.IsNullOrWhiteSpace(p.GetValueOrDefault("photoUrl")?.ToString()),
            hasDocument = !string.IsNullOrWhiteSpace(p.GetValueOrDefault("documentUrl")?.ToString()),
            signatureUrl = Proxy("signature_url"),
            photoUrl = Proxy("photo_url"),
            documentUrl = Proxy("document_url"),
        };
    }

    private static async Task<IResult> PublicTrackEvents(string token, Database db, CancellationToken ct)
    {
        var link = await ResolveLink(db, token, ct);
        if (link is null) return NotFound();
        var items = await db.QueryAsync(
            "SELECT event_type, message, occurred_at_utc FROM fleet_tms_shipment_events WHERE company_id=@companyId AND shipment_id=@sid AND visibility <> 'Private' ORDER BY occurred_at_utc DESC",
            c => { c.Parameters.AddWithValue("@companyId", Convert.ToInt64(link["companyId"])); c.Parameters.AddWithValue("@sid", Convert.ToInt64(link["shipmentId"])); }, ct);
        return Ok(new { items });
    }

    private static async Task<IResult> PublicTrackPod(string token, Database db, CancellationToken ct)
    {
        var link = await ResolveLink(db, token, ct);
        if (link is null) return NotFound();
        var rows = await db.QueryAsync(
            "SELECT id, recipient_name, delivery_condition, status, captured_at, verified_at, signature_url, photo_url, document_url FROM fleet_tms_pods WHERE company_id=@companyId AND shipment_id=@sid AND status <> 'Rejected'",
            c => { c.Parameters.AddWithValue("@companyId", Convert.ToInt64(link["companyId"])); c.Parameters.AddWithValue("@sid", Convert.ToInt64(link["shipmentId"])); }, ct);
        return Ok(new { items = rows.Select(p => SafePublicPod(token, p)) });
    }

    // Token-scoped POD asset proxy. Re-validates the tracking link on every request
    // (expiry + revocation enforced by ResolveLink) and only streams assets from
    // operator-allowlisted storage hosts (config Fleet:PodAssetAllowedHosts) — this
    // prevents SSRF to arbitrary hosts and ensures revoking a link immediately cuts
    // off proof images. Assets on non-allowlisted hosts are reported unavailable
    // (404) until private/secure storage is configured.
    private static async Task<IResult> PublicTrackPodAsset(
        string token, long podId, string kind, Database db, IConfiguration config, IHttpClientFactory httpFactory, CancellationToken ct)
    {
        var column = kind switch
        {
            "signature" => "signature_url",
            "photo" => "photo_url",
            "document" => "document_url",
            _ => null,
        };
        if (column is null) return NotFound();

        var link = await ResolveLink(db, token, ct);
        if (link is null) return NotFound();

        var pod = await db.QuerySingleAsync(
            $"SELECT {column} AS asset_url FROM fleet_tms_pods WHERE id=@podId AND company_id=@companyId AND shipment_id=@sid AND status <> 'Rejected'",
            c => { c.Parameters.AddWithValue("@podId", podId); c.Parameters.AddWithValue("@companyId", Convert.ToInt64(link["companyId"])); c.Parameters.AddWithValue("@sid", Convert.ToInt64(link["shipmentId"])); }, ct);
        var rawUrl = pod?.GetValueOrDefault("assetUrl")?.ToString();
        if (string.IsNullOrWhiteSpace(rawUrl)) return NotFound();

        if (!Uri.TryCreate(rawUrl, UriKind.Absolute, out var uri) ||
            (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps) ||
            !IsAllowlistedHost(config, uri.Host))
        {
            // Asset exists but lives on a host we will not proxy (open/untrusted
            // storage). Do not disclose or fetch it — treat as not-yet-available.
            return Results.Json(ApiResponse<object>.Fail("Asset unavailable", "POD asset storage is not yet secured for public delivery."), statusCode: StatusCodes.Status404NotFound);
        }

        var client = httpFactory.CreateClient("pod-assets");
        client.Timeout = TimeSpan.FromSeconds(15);
        try
        {
            var upstream = await client.GetAsync(uri, HttpCompletionOption.ResponseHeadersRead, ct);
            if (!upstream.IsSuccessStatusCode) return NotFound();
            var contentType = upstream.Content.Headers.ContentType?.ToString() ?? "application/octet-stream";
            var bytes = await upstream.Content.ReadAsByteArrayAsync(ct);
            return Results.File(bytes, contentType);
        }
        catch (Exception)
        {
            return NotFound();
        }
    }

    private static bool IsAllowlistedHost(IConfiguration config, string host)
    {
        var configured = config["Fleet:PodAssetAllowedHosts"]
                         ?? Environment.GetEnvironmentVariable("FLEET_POD_ASSET_ALLOWED_HOSTS");
        if (string.IsNullOrWhiteSpace(configured)) return false; // closed by default
        return configured
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Any(allowed => string.Equals(allowed, host, StringComparison.OrdinalIgnoreCase)
                            || host.EndsWith("." + allowed, StringComparison.OrdinalIgnoreCase));
    }
}

// ── Request DTOs (camelCase JSON binds to these via the default web serializer) ──
public record ShipmentStopRequest(
    string? StopType, int SequenceNo, string? LocationName, string? ContactName, string? ContactPhone,
    string? AddressLine1, string? AddressLine2, string? City, string? Region, string? PostalCode, string? Country,
    string? SaudiNationalAddressBuildingNo, string? SaudiNationalAddressAdditionalNo, string? SaudiNationalAddressDistrict,
    decimal? Latitude, decimal? Longitude, DateTime? PlannedArrivalAt, string? Notes);

public record NotesRequest(string? Notes);
public record PodRequest(
    long StopId, string? RecipientName, string? RecipientPhone, string? SignatureUrl, string? PhotoUrl,
    string? DocumentUrl, string? Notes, string? DeliveryCondition, decimal? CapturedLatitude, decimal? CapturedLongitude);
public record TrackingLinkRequest(string? Token, DateTime? ExpiresAtUtc);
public record InvoiceReadyRequest(bool Override, string? Notes);
public record DispatchShipmentRequest(string? VehicleNumber, string? DriverName, string? RouteCode, string? Notes);
public record VehicleServiceRequest(string? Status, string? HealthStatus, DateTime? NextServiceAtUtc, string? Notes);
public record CloseMaintenanceRequest(string? Status, decimal? ActualCost, string? Notes);
public record FlagFuelEventRequest(bool AnomalyFlag, string? Notes);
