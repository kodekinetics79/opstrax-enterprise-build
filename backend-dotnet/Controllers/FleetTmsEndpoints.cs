using Npgsql;
using Opstrax.Api.Data;
using Opstrax.Api.DTOs;
using System.Security.Cryptography;
using System.Text;
using System.Net;
using System.Net.Sockets;

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
        Guard(app.MapGet("/api/fleet-tms/overview", Overview), "fleet:view");
        Guard(app.MapGet("/api/fleet-tms/shipments", Shipments), "fleet:view");
        Guard(app.MapGet("/api/fleet-tms/shipments/invoice-ready", InvoiceReady), "fleet:view");
        Guard(app.MapGet("/api/fleet-tms/vehicles", Vehicles), "fleet:view");
        Guard(app.MapGet("/api/fleet-tms/tracking", Tracking), "fleet:view");
        Guard(app.MapGet("/api/fleet-tms/maintenance", Maintenance), "fleet:view");
        Guard(app.MapGet("/api/fleet-tms/fuel", Fuel), "fleet:view");
        Guard(app.MapPost("/api/fleet-tms/shipments/{id:long}/dispatch", DispatchShipment), "fleet:manage");
        Guard(app.MapPost("/api/fleet-tms/vehicles/{id:long}/service", ServiceVehicle), "fleet:manage");
        Guard(app.MapPost("/api/fleet-tms/maintenance/{id:long}/close", CloseMaintenance), "fleet:manage");
        Guard(app.MapPost("/api/fleet-tms/fuel/{id:long}/flag", FlagFuelEvent), "fleet:manage");
        Guard(app.MapPost("/api/fleet-tms/shipments/{id:long}/mark-invoice-ready", MarkInvoiceReady), "fleet:manage");
        Guard(app.MapGet("/api/fleet-tms/carriers", WorkspaceCarriers), "fleet:view");
        Guard(app.MapPost("/api/fleet-tms/shipments/{id:long}/carrier", AssignShipmentCarrier), "fleet:manage");
        Guard(app.MapDelete("/api/fleet-tms/shipments/{id:long}/carrier", UnassignShipmentCarrier), "fleet:manage");

        // ── Shipment lifecycle (stops / POD / events / tracking links) ─────────
        Guard(app.MapGet("/api/fleet-tms/shipments/{shipmentId:long}/stops", GetStops), "fleet:view");
        Guard(app.MapPost("/api/fleet-tms/shipments/{shipmentId:long}/stops", CreateStop), "fleet:manage");
        Guard(app.MapPut("/api/fleet-tms/shipments/{shipmentId:long}/stops/{stopId:long}", UpdateStop), "fleet:manage");
        Guard(app.MapPost("/api/fleet-tms/shipments/{shipmentId:long}/stops/{stopId:long}/arrive", ArriveStop), "fleet:manage");
        Guard(app.MapPost("/api/fleet-tms/shipments/{shipmentId:long}/stops/{stopId:long}/complete", CompleteStop), "fleet:manage");
        Guard(app.MapGet("/api/fleet-tms/shipments/{shipmentId:long}/events", GetShipmentEvents), "fleet:view");
        Guard(app.MapGet("/api/fleet-tms/shipments/{shipmentId:long}/pod", GetPod), "fleet:view");
        Guard(app.MapPost("/api/fleet-tms/shipments/{shipmentId:long}/pod", CreatePod), "fleet:manage");
        Guard(app.MapPut("/api/fleet-tms/shipments/{shipmentId:long}/pod/{podId:long}", UpdatePod), "fleet:manage");
        Guard(app.MapPost("/api/fleet-tms/shipments/{shipmentId:long}/pod/{podId:long}/submit", SubmitPod), "fleet:manage");
        Guard(app.MapPost("/api/fleet-tms/shipments/{shipmentId:long}/pod/{podId:long}/verify", VerifyPod), "fleet:manage");
        Guard(app.MapPost("/api/fleet-tms/shipments/{shipmentId:long}/pod/{podId:long}/reject", RejectPod), "fleet:manage");
        // Listing link metadata contains no bearer secret and is needed by the
        // read-only lifecycle drawer. Creating/revoking links remains manage-only.
        Guard(app.MapGet("/api/fleet-tms/shipments/{shipmentId:long}/tracking-link", GetTrackingLinks), "fleet:view");
        Guard(app.MapPost("/api/fleet-tms/shipments/{shipmentId:long}/tracking-link", CreateTrackingLink), "fleet:manage");
        Guard(app.MapDelete("/api/fleet-tms/shipments/{shipmentId:long}/tracking-link/{linkId:long}", RevokeTrackingLink), "fleet:manage");

        // ── Driver tasks ──────────────────────────────────────────────────────
        GuardDriverTask(app.MapGet("/api/fleet-tms/driver/tasks", GetDriverTasks), "fleet:view");
        GuardDriverTask(app.MapGet("/api/fleet-tms/driver/tasks/{taskId:long}", GetDriverTask), "fleet:view");
        GuardDriverTask(app.MapPost("/api/fleet-tms/driver/tasks/{taskId:long}/arrive", ArriveDriverTask), "fleet:manage");
        GuardDriverTask(app.MapPost("/api/fleet-tms/driver/tasks/{taskId:long}/complete", CompleteDriverTask), "fleet:manage");
        GuardDriverTask(app.MapPost("/api/fleet-tms/driver/tasks/{taskId:long}/pod", UpsertDriverTaskPod), "fleet:manage");

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

    private static RouteHandlerBuilder Guard(RouteHandlerBuilder route, string permission)
        => route.AddEndpointFilter(async (invocation, next) =>
        {
            var denied = EndpointMappings.RequirePermission(invocation.HttpContext, permission);
            if (denied is not null) return denied;
            return await next(invocation);
        });

    private static RouteHandlerBuilder GuardDriverTask(RouteHandlerBuilder route, string staffPermission)
        => route.AddEndpointFilter(async (invocation, next) =>
        {
            if (IsDriver(invocation.HttpContext)) return await next(invocation);
            var denied = EndpointMappings.RequirePermission(invocation.HttpContext, staffPermission);
            return denied ?? await next(invocation);
        });

    private static long CompanyId(HttpContext http) => EndpointMappings.GetCompanyId(http);
    private static long? BranchId(HttpContext http) => EndpointMappings.GetBranchId(http);
    private static string Owned(HttpContext http, string? alias = null)
        => BranchId(http) is null ? "" : $" AND {(string.IsNullOrEmpty(alias) ? "" : alias + ".")}branch_id=@branchId";
    private static void BindOwner(NpgsqlCommand c, HttpContext http)
    {
        c.Parameters.AddWithValue("@companyId", CompanyId(http));
        if (BranchId(http) is { } branchId) c.Parameters.AddWithValue("@branchId", branchId);
    }

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

    private static async Task<Dictionary<string, object?>?> FindShipment(Database db, HttpContext http, long id, CancellationToken ct)
        => await db.QuerySingleAsync(
            $"SELECT * FROM fleet_tms_shipments WHERE id=@id AND company_id=@companyId{Owned(http)}",
            c => { c.Parameters.AddWithValue("@id", id); BindOwner(c, http); }, ct);

    private static async Task LogEvent(Database db, HttpContext http, long shipmentId, string eventType, string message, string visibility, CancellationToken ct)
        => await db.ExecuteAsync(@"
INSERT INTO fleet_tms_shipment_events (company_id, branch_id, shipment_id, event_type, message, actor_name, visibility, occurred_at_utc, created_at_utc)
SELECT company_id, branch_id, id, @type, @message, @actor, @visibility, NOW(), NOW()
FROM fleet_tms_shipments WHERE company_id=@companyId AND id=@shipmentId",
            c =>
            {
                c.Parameters.AddWithValue("@companyId", CompanyId(http));
                c.Parameters.AddWithValue("@shipmentId", shipmentId);
                c.Parameters.AddWithValue("@type", eventType);
                c.Parameters.AddWithValue("@message", message);
                c.Parameters.AddWithValue("@actor", Actor(http));
                c.Parameters.AddWithValue("@visibility", visibility);
            }, ct);

    private static async Task<Dictionary<string, object?>?> RowById(Database db, HttpContext http, string table, long id, CancellationToken ct)
        => await db.QuerySingleAsync(
            $"SELECT * FROM {table} WHERE id=@id AND company_id=@companyId{Owned(http)}",
            c => { c.Parameters.AddWithValue("@id", id); BindOwner(c, http); }, ct);

    private static object S(string? value) => (object?)value ?? DBNull.Value;
    private static object N(decimal? value) => (object?)value ?? DBNull.Value;
    private static bool TooLong(string? value, int max) => (value?.Trim().Length ?? 0) > max;

    private static bool IsDriver(HttpContext http)
        => http.Items.TryGetValue(EndpointMappings.AuthRoleItemKey, out var role)
           && string.Equals(role?.ToString(), "Driver", StringComparison.OrdinalIgnoreCase);

    private static async Task<bool> CanAccessDriverTask(Database db, HttpContext http, Dictionary<string, object?> task, CancellationToken ct)
    {
        if (!IsDriver(http)) return true;
        if (task.GetValueOrDefault("driverId") is not { } rawDriver || rawDriver is DBNull) return false;
        var userId = http.Items.TryGetValue(EndpointMappings.AuthUserIdItemKey, out var rawUser) && rawUser is not null
            ? Convert.ToInt64(rawUser) : 0L;
        return await db.ScalarLongAsync(
            "SELECT COUNT(*) FROM drivers WHERE id=@driverId AND user_id=@userId AND company_id=@companyId AND deleted_at IS NULL",
            c => { c.Parameters.AddWithValue("@driverId", Convert.ToInt64(rawDriver)); c.Parameters.AddWithValue("@userId", userId); c.Parameters.AddWithValue("@companyId", CompanyId(http)); }, ct) == 1;
    }

    // ── Overview ───────────────────────────────────────────────────────────────

    private static async Task<IResult> Overview(HttpContext http, Database db, CancellationToken ct)
    {
        void Bind(NpgsqlCommand c) => BindOwner(c, http);
        var owned = Owned(http);

        var summary = new
        {
            activeShipments = await db.ScalarLongAsync($"SELECT COUNT(*) FROM fleet_tms_shipments WHERE company_id=@companyId{owned} AND status NOT IN ('Delivered','Cancelled')", Bind, ct),
            enRoute = await db.ScalarLongAsync($"SELECT COUNT(*) FROM fleet_tms_shipments WHERE company_id=@companyId{owned} AND status IN ('PickedUp','InTransit','Loaded')", Bind, ct),
            deliveredToday = await db.ScalarLongAsync($"SELECT COUNT(*) FROM fleet_tms_shipments WHERE company_id=@companyId{owned} AND status='Delivered' AND delivered_at_utc >= date_trunc('day', NOW())", Bind, ct),
            activeVehicles = await db.ScalarLongAsync($"SELECT COUNT(*) FROM fleet_tms_vehicles WHERE company_id=@companyId{owned} AND status IN ('Available','OnTrip','Maintenance')", Bind, ct),
            onTripVehicles = await db.ScalarLongAsync($"SELECT COUNT(*) FROM fleet_tms_vehicles WHERE company_id=@companyId{owned} AND status='OnTrip'", Bind, ct),
            openMaintenance = await db.ScalarLongAsync($"SELECT COUNT(*) FROM fleet_tms_maintenance_tickets WHERE company_id=@companyId{owned} AND status IN ('Open','InProgress','AwaitingParts')", Bind, ct),
            fuelAlerts = await db.ScalarLongAsync($"SELECT COUNT(*) FROM fleet_tms_fuel_events WHERE company_id=@companyId{owned} AND anomaly_flag", Bind, ct),
            trackingEvents = await db.ScalarLongAsync($"SELECT COUNT(*) FROM fleet_tms_tracking_points WHERE company_id=@companyId{owned}", Bind, ct),
            avgFuelLevel = Math.Round(await db.ScalarDecimalAsync($"SELECT COALESCE(AVG(fuel_level_percent),0) FROM fleet_tms_vehicles WHERE company_id=@companyId{owned}", Bind, ct) ?? 0m, 1),
        };

        var generatedAtUtc = DateTime.UtcNow;
        var shipmentCards = await db.QueryAsync($"SELECT * FROM fleet_tms_shipments WHERE company_id=@companyId{owned} ORDER BY created_at_utc DESC LIMIT 6", Bind, ct);
        var vehicleCards = await db.QueryAsync($"SELECT * FROM fleet_tms_vehicles WHERE company_id=@companyId{owned} ORDER BY last_ping_at_utc DESC NULLS LAST, vehicle_number LIMIT 6", Bind, ct);
        var trackingCards = await db.QueryAsync($"SELECT * FROM fleet_tms_tracking_points WHERE company_id=@companyId{owned} ORDER BY recorded_at_utc DESC LIMIT 6", Bind, ct);
        var maintenanceCards = await db.QueryAsync($"SELECT * FROM fleet_tms_maintenance_tickets WHERE company_id=@companyId{owned} ORDER BY opened_at_utc DESC LIMIT 6", Bind, ct);
        var fuelCards = await db.QueryAsync($"SELECT * FROM fleet_tms_fuel_events WHERE company_id=@companyId{owned} ORDER BY recorded_at_utc DESC LIMIT 6", Bind, ct);
        var loadPlanCards = await db.QueryAsync(@"
SELECT route_code, COUNT(*) shipment_count, COALESCE(SUM(weight_kg),0) total_weight_kg, COALESCE(SUM(volume_cbm),0) total_volume_cbm,
       COUNT(*) FILTER (WHERE priority IN ('High','Critical')) high_priority,
       COUNT(*) FILTER (WHERE status='Delivered') delivered
FROM fleet_tms_shipments WHERE company_id=@companyId" + owned + " GROUP BY route_code ORDER BY shipment_count DESC LIMIT 4", Bind, ct);

        return Ok(new { generatedAtUtc, summary, shipmentCards, vehicleCards, trackingCards, maintenanceCards, fuelCards, loadPlanCards });
    }

    // ── List endpoints ──────────────────────────────────────────────────────────

    private static async Task<IResult> Shipments(HttpContext http, Database db, string? status, int page = 1, int pageSize = 20, CancellationToken ct = default)
    {
        page = page < 1 ? 1 : page;
        pageSize = pageSize is < 1 or > 200 ? 20 : pageSize;
        var where = "WHERE company_id=@companyId" + Owned(http) + (string.IsNullOrWhiteSpace(status) ? "" : " AND status=@status");
        void Bind(NpgsqlCommand c)
        {
            BindOwner(c, http);
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
            $"SELECT * FROM fleet_tms_shipments WHERE company_id=@companyId{Owned(http)} AND is_invoice_ready ORDER BY invoice_ready_at_utc DESC",
            c => BindOwner(c, http), ct);
        return Ok(new { items });
    }

    private static async Task<IResult> Vehicles(HttpContext http, Database db, string? status, CancellationToken ct)
    {
        var where = "WHERE company_id=@companyId" + Owned(http) + (string.IsNullOrWhiteSpace(status) ? "" : " AND status=@status");
        var items = await db.QueryAsync($"SELECT * FROM fleet_tms_vehicles {where} ORDER BY vehicle_number",
            c => { BindOwner(c, http); if (!string.IsNullOrWhiteSpace(status)) c.Parameters.AddWithValue("@status", status); }, ct);
        return Ok(new { items });
    }

    private static async Task<IResult> Tracking(HttpContext http, Database db, string? shipmentNumber, int page = 1, int pageSize = 20, CancellationToken ct = default)
    {
        page = page < 1 ? 1 : page;
        pageSize = pageSize is < 1 or > 200 ? 20 : pageSize;
        var shipmentFilter = string.IsNullOrWhiteSpace(shipmentNumber) ? "" : " AND shipment_number=@sn";
        var where = "WHERE company_id=@companyId" + Owned(http) + shipmentFilter;
        void Bind(NpgsqlCommand c)
        {
            BindOwner(c, http);
            if (!string.IsNullOrWhiteSpace(shipmentNumber)) c.Parameters.AddWithValue("@sn", shipmentNumber);
        }

        var hasCanonicalTelemetry = await db.ScalarLongAsync(
            "SELECT CASE WHEN to_regclass('public.vehicles') IS NOT NULL AND to_regclass('public.latest_vehicle_positions') IS NOT NULL AND to_regclass('public.location_events') IS NOT NULL THEN 1 ELSE 0 END", ct: ct) == 1;
        var canonical = hasCanonicalTelemetry ? @"
UNION ALL
SELECT le.id, s.company_id, s.branch_id, s.shipment_number, s.vehicle_number,
       COALESCE(NULLIF(le.event_type,''),'Canonical telemetry') location_label,
       s.status, '' geofence_name, '' alert_type, le.lat latitude, le.lng longitude,
       le.speed_mph * 1.609344 speed_kph, le.event_time recorded_at_utc,
       NULL::timestamptz estimated_arrival_utc, '' notes,
       'canonical_location_event' source_type, true is_live,
       COALESCE(NULLIF(le.source,''),NULLIF(le.source_channel,''),'canonical') source,
       GREATEST(0,EXTRACT(EPOCH FROM (NOW()-le.event_time)))::BIGINT freshness_seconds,
       CASE WHEN le.event_time >= NOW()-INTERVAL '5 minutes' THEN 'Live'
            WHEN le.event_time >= NOW()-INTERVAL '1 hour' THEN 'Delayed' ELSE 'Stale' END freshness_status
FROM fleet_tms_shipments s
JOIN vehicles v ON v.company_id=s.company_id AND v.deleted_at IS NULL
  AND v.branch_id IS NOT DISTINCT FROM s.branch_id
  AND (v.vehicle_code=s.vehicle_number OR v.plate_number=s.vehicle_number)
JOIN location_events le ON le.company_id=s.company_id AND le.vehicle_id=v.id
WHERE s.company_id=@companyId" + Owned(http, "s") + (string.IsNullOrWhiteSpace(shipmentNumber) ? "" : " AND s.shipment_number=@sn") + @"
  AND le.event_time >= NOW()-INTERVAL '30 days'
  AND NOT EXISTS (SELECT 1 FROM latest_vehicle_positions lp WHERE lp.company_id=le.company_id AND lp.source_event_id=le.id)
UNION ALL
SELECT lp.id, s.company_id, s.branch_id, s.shipment_number, s.vehicle_number,
       'Latest canonical position' location_label, s.status, '' geofence_name,
       CASE WHEN COALESCE(lp.open_alert_count,0)>0 THEN 'TelemetryAlert' ELSE '' END alert_type,
       lp.lat latitude, lp.lng longitude, lp.speed_mph * 1.609344 speed_kph,
       COALESCE(lp.device_fix_time,lp.event_time,lp.received_at,lp.updated_at) recorded_at_utc,
       NULL::timestamptz estimated_arrival_utc, '' notes,
       'canonical_latest_position' source_type, true is_live,
       COALESCE(NULLIF(lp.source,''),NULLIF(lp.provider,''),NULLIF(lp.source_channel,''),'canonical') source,
       GREATEST(0,EXTRACT(EPOCH FROM (NOW()-COALESCE(lp.device_fix_time,lp.event_time,lp.received_at,lp.updated_at))))::BIGINT freshness_seconds,
       CASE WHEN COALESCE(lp.device_fix_time,lp.event_time,lp.received_at,lp.updated_at) >= NOW()-INTERVAL '5 minutes' THEN 'Live'
            WHEN COALESCE(lp.device_fix_time,lp.event_time,lp.received_at,lp.updated_at) >= NOW()-INTERVAL '1 hour' THEN 'Delayed' ELSE 'Stale' END freshness_status
FROM fleet_tms_shipments s
JOIN vehicles v ON v.company_id=s.company_id AND v.deleted_at IS NULL
  AND v.branch_id IS NOT DISTINCT FROM s.branch_id
  AND (v.vehicle_code=s.vehicle_number OR v.plate_number=s.vehicle_number)
JOIN latest_vehicle_positions lp ON lp.company_id=s.company_id AND lp.vehicle_id=v.id
WHERE s.company_id=@companyId" + Owned(http, "s") + (string.IsNullOrWhiteSpace(shipmentNumber) ? "" : " AND s.shipment_number=@sn") : "";
        var sourceSql = @"
SELECT id, company_id, branch_id, shipment_number, vehicle_number, location_label, status,
       geofence_name, alert_type, latitude, longitude, speed_kph, recorded_at_utc,
       estimated_arrival_utc, notes, 'workspace_projection' source_type, false is_live,
       'workspace_seed_or_projection' source,
       GREATEST(0,EXTRACT(EPOCH FROM (NOW()-recorded_at_utc)))::BIGINT freshness_seconds,
       'NonLive' freshness_status
FROM fleet_tms_tracking_points " + where + canonical;
        var total = await db.ScalarLongAsync($"SELECT COUNT(*) FROM ({sourceSql}) tracking", Bind, ct);
        var items = await db.QueryAsync($"SELECT * FROM ({sourceSql}) tracking ORDER BY is_live DESC, recorded_at_utc DESC OFFSET @offset LIMIT @limit",
            c => { Bind(c); c.Parameters.AddWithValue("@offset", (page - 1) * pageSize); c.Parameters.AddWithValue("@limit", pageSize); }, ct);
        return Ok(new { total, page, pageSize, items });
    }

    private static async Task<IResult> Maintenance(HttpContext http, Database db, string? status, int page = 1, int pageSize = 20, CancellationToken ct = default)
    {
        page = page < 1 ? 1 : page;
        pageSize = pageSize is < 1 or > 200 ? 20 : pageSize;
        var where = "WHERE company_id=@companyId" + Owned(http) + (string.IsNullOrWhiteSpace(status) ? "" : " AND status=@status");
        void Bind(NpgsqlCommand c)
        {
            BindOwner(c, http);
            if (!string.IsNullOrWhiteSpace(status)) c.Parameters.AddWithValue("@status", status);
        }
        var total = await db.ScalarLongAsync($"SELECT COUNT(*) FROM fleet_tms_maintenance_tickets {where}", Bind, ct);
        var items = await db.QueryAsync($"SELECT * FROM fleet_tms_maintenance_tickets {where} ORDER BY opened_at_utc DESC OFFSET @offset LIMIT @limit",
            c => { Bind(c); c.Parameters.AddWithValue("@offset", (page - 1) * pageSize); c.Parameters.AddWithValue("@limit", pageSize); }, ct);
        return Ok(new { total, page, pageSize, items });
    }

    private static async Task<IResult> Fuel(HttpContext http, Database db, bool? anomaliesOnly, int page = 1, int pageSize = 20, CancellationToken ct = default)
    {
        // Fuel Intelligence entitlement — fuel analytics requires the Fuel package.
        if (await RequireModule(http, db, Opstrax.Api.Services.RevenueSchemaService.Modules.Fuel, ct) is { } denied)
            return denied;
        page = page < 1 ? 1 : page;
        pageSize = pageSize is < 1 or > 200 ? 20 : pageSize;
        var where = "WHERE company_id=@companyId" + Owned(http) + (anomaliesOnly == true ? " AND anomaly_flag" : "");
        void Bind(NpgsqlCommand c) => BindOwner(c, http);
        var total = await db.ScalarLongAsync($"SELECT COUNT(*) FROM fleet_tms_fuel_events {where}", Bind, ct);
        var items = await db.QueryAsync($"SELECT * FROM fleet_tms_fuel_events {where} ORDER BY recorded_at_utc DESC OFFSET @offset LIMIT @limit",
            c => { Bind(c); c.Parameters.AddWithValue("@offset", (page - 1) * pageSize); c.Parameters.AddWithValue("@limit", pageSize); }, ct);
        return Ok(new { total, page, pageSize, items });
    }

    // ── Fleet actions ───────────────────────────────────────────────────────────

    private static async Task<IResult> DispatchShipment(HttpContext http, long id, DispatchShipmentRequest req, Database db, CancellationToken ct)
    {
        var companyId = CompanyId(http);
        if (TooLong(req.DriverName, 255) || TooLong(req.VehicleNumber, 60) || TooLong(req.RouteCode, 60) || TooLong(req.Notes, 4000))
            return Bad("Dispatch fields exceed their maximum allowed length.");
        return await db.RunInTenantTransactionAsync(companyId, async () =>
        {
        // Lock the shipment first. Two concurrent dispatch attempts must observe the
        // first committed transition instead of both passing a stale Booked check.
        var shipment = await db.QuerySingleAsync(
            $"SELECT * FROM fleet_tms_shipments WHERE id=@id AND company_id=@companyId{Owned(http)} FOR UPDATE",
            c => { c.Parameters.AddWithValue("@id", id); BindOwner(c, http); }, ct);
        if (shipment is null) return NotFound();
        var currentStatus = shipment.GetValueOrDefault("status")?.ToString() ?? "";
        if (currentStatus is not ("Booked" or "Planned" or "Loaded"))
            return Bad($"Shipment in {currentStatus} status cannot be dispatched.");
        var driver = string.IsNullOrWhiteSpace(req.DriverName) ? shipment.GetValueOrDefault("driverName")?.ToString()?.Trim() : req.DriverName.Trim();
        var vehicle = string.IsNullOrWhiteSpace(req.VehicleNumber) ? shipment.GetValueOrDefault("vehicleNumber")?.ToString()?.Trim() : req.VehicleNumber.Trim();
        var route = string.IsNullOrWhiteSpace(req.RouteCode) ? shipment.GetValueOrDefault("routeCode")?.ToString()?.Trim() : req.RouteCode.Trim();
        if (string.IsNullOrWhiteSpace(driver) || string.IsNullOrWhiteSpace(vehicle) || string.IsNullOrWhiteSpace(route))
            return Bad("Driver, vehicle and route are required before dispatch.");
        var fleetVehicle = await db.QuerySingleAsync(
            $"SELECT * FROM fleet_tms_vehicles WHERE company_id=@companyId{Owned(http)} AND vehicle_number=@vehicle FOR UPDATE",
            c => { BindOwner(c, http); c.Parameters.AddWithValue("@vehicle", vehicle); }, ct);
        if (fleetVehicle is null) return Bad("The selected vehicle is not owned by this tenant and branch.");
        if ((fleetVehicle.GetValueOrDefault("status")?.ToString() ?? "") is not ("Available" or "OnTrip"))
            return Bad("The selected vehicle is not available for dispatch.");
        var activeVehicleLoad = await db.ScalarLongAsync(
            $"SELECT COUNT(*) FROM fleet_tms_shipments WHERE company_id=@companyId{Owned(http)} AND id<>@id AND vehicle_number=@vehicle AND status NOT IN ('Delivered','Cancelled')",
            c => { BindOwner(c, http); c.Parameters.AddWithValue("@id", id); c.Parameters.AddWithValue("@vehicle", vehicle); }, ct);
        if (activeVehicleLoad > 0) return Bad("The selected vehicle is already assigned to an active shipment.");
        var activeDriverLoad = await db.ScalarLongAsync(
            $"SELECT COUNT(*) FROM fleet_tms_shipments WHERE company_id=@companyId{Owned(http)} AND id<>@id AND driver_name=@driver AND status NOT IN ('Delivered','Cancelled')",
            c => { BindOwner(c, http); c.Parameters.AddWithValue("@id", id); c.Parameters.AddWithValue("@driver", driver); }, ct);
        if (activeDriverLoad > 0) return Bad("The selected driver is already assigned to an active shipment.");

        long? driverId = null;
        long? vehicleId = null;
        if (await db.ScalarLongAsync("SELECT COUNT(*) FROM pg_class WHERE oid=to_regclass('public.drivers')", ct: ct) > 0)
        {
            var canonicalDriver = await db.QuerySingleAsync(
                "SELECT id,status FROM drivers WHERE company_id=@companyId AND full_name=@driver AND deleted_at IS NULL" + Owned(http) + " FOR UPDATE",
                c => { BindOwner(c, http); c.Parameters.AddWithValue("@driver", driver); }, ct);
            if (canonicalDriver is null || (canonicalDriver.GetValueOrDefault("status")?.ToString() ?? "") is not ("Active" or "Available" or "Idle"))
                return Bad("Driver must be an active driver in the authorized branch.");
            driverId = Convert.ToInt64(canonicalDriver["id"]);
        }
        if (await db.ScalarLongAsync("SELECT COUNT(*) FROM pg_class WHERE oid=to_regclass('public.vehicles')", ct: ct) > 0)
        {
            var canonicalVehicle = await db.QuerySingleAsync(
                "SELECT id,status,COALESCE(out_of_service,false) out_of_service FROM vehicles WHERE company_id=@companyId AND (vehicle_code=@vehicle OR plate_number=@vehicle) AND deleted_at IS NULL" + Owned(http) + " FOR UPDATE",
                c => { BindOwner(c, http); c.Parameters.AddWithValue("@vehicle", vehicle); }, ct);
            if (canonicalVehicle is null || Convert.ToBoolean(canonicalVehicle.GetValueOrDefault("outOfService") ?? false)
                || (canonicalVehicle.GetValueOrDefault("status")?.ToString() ?? "") is not ("Active" or "Available" or "Idle"))
                return Bad("Vehicle must be active, available and in the authorized branch.");
            vehicleId = Convert.ToInt64(canonicalVehicle["id"]);
        }
        var stopCount = await db.ScalarLongAsync(
            $"SELECT COUNT(*) FROM fleet_tms_shipment_stops WHERE company_id=@companyId{Owned(http)} AND shipment_id=@id",
            c => { BindOwner(c, http); c.Parameters.AddWithValue("@id", id); }, ct);
        if (stopCount == 0) return Bad("At least one shipment stop is required before dispatch.");

        await db.ExecuteAsync(@"
UPDATE fleet_tms_shipments SET status='InTransit',
    driver_name=@driver, vehicle_number=@vehicle, route_code=@route,
    notes=COALESCE(@notes, notes),
    picked_up_at_utc=NOW(), updated_at_utc=NOW()
WHERE id=@id AND company_id=@companyId" + Owned(http),
            c =>
            {
                c.Parameters.AddWithValue("@driver", driver);
                c.Parameters.AddWithValue("@vehicle", vehicle);
                c.Parameters.AddWithValue("@route", route);
                c.Parameters.AddWithValue("@notes", S(req.Notes));
                c.Parameters.AddWithValue("@id", id);
                BindOwner(c, http);
            }, ct);

        await db.ExecuteAsync(@"
UPDATE fleet_tms_vehicles SET status='OnTrip', driver_name=@driver, updated_at_utc=NOW()
WHERE company_id=@companyId AND vehicle_number=@vehicle" + Owned(http),
            c => { BindOwner(c, http); c.Parameters.AddWithValue("@vehicle", vehicle); c.Parameters.AddWithValue("@driver", driver); }, ct);
        await db.ExecuteAsync(@"
INSERT INTO fleet_tms_driver_tasks
 (company_id, branch_id, shipment_id, stop_id, driver_id, vehicle_id, task_type, title, description, status, driver_name, vehicle_number, due_at_utc)
SELECT s.company_id, s.branch_id, s.id, st.id, @driverId, @vehicleId, st.stop_type,
       st.stop_type || ' ' || s.shipment_number || ' — ' || st.location_name,
       'Complete stop and capture proof when required.', 'Open', @driver, @vehicle,
       COALESCE(st.planned_arrival_at, NOW())
FROM fleet_tms_shipments s JOIN fleet_tms_shipment_stops st
  ON st.company_id=s.company_id AND st.shipment_id=s.id
WHERE s.company_id=@companyId AND s.id=@id
  AND NOT EXISTS (SELECT 1 FROM fleet_tms_driver_tasks t
                  WHERE t.company_id=s.company_id AND t.shipment_id=s.id AND t.stop_id=st.id AND t.status <> 'Cancelled')
ON CONFLICT DO NOTHING",
            c => { c.Parameters.AddWithValue("@companyId", companyId); c.Parameters.AddWithValue("@id", id); c.Parameters.AddWithValue("@driverId", (object?)driverId ?? DBNull.Value); c.Parameters.AddWithValue("@vehicleId", (object?)vehicleId ?? DBNull.Value); c.Parameters.AddWithValue("@driver", driver); c.Parameters.AddWithValue("@vehicle", vehicle); }, ct);

        await LogEvent(db, http, id, "ShipmentDispatched", "Shipment dispatched from the command center.", "Public", ct);
        return Ok(await RowById(db, http, "fleet_tms_shipments", id, ct)!);
        }, ct);
    }

    private static async Task<IResult> ServiceVehicle(HttpContext http, long id, VehicleServiceRequest req, Database db, CancellationToken ct)
    {
        var companyId = CompanyId(http);
        var vehicle = await RowById(db, http, "fleet_tms_vehicles", id, ct);
        if (vehicle is null) return NotFound();
        var status = string.IsNullOrWhiteSpace(req.Status) ? "Maintenance" : req.Status.Trim();
        if (status is not ("Available" or "Maintenance" or "OutOfService")) return Bad("Invalid vehicle service status.");
        if (TooLong(req.HealthStatus, 30) || TooLong(req.Notes, 4000)) return Bad("Vehicle service fields exceed their maximum allowed length.");
        if (req.NextServiceAtUtc is { } next && next <= DateTime.UtcNow) return Bad("Next service date must be in the future.");
        var vehicleNumber = vehicle.GetValueOrDefault("vehicleNumber")?.ToString() ?? "";
        if (status is "Maintenance" or "OutOfService" && await db.ScalarLongAsync(
                $"SELECT COUNT(*) FROM fleet_tms_shipments WHERE company_id=@companyId{Owned(http)} AND vehicle_number=@vehicle AND status NOT IN ('Delivered','Cancelled')",
                c => { BindOwner(c, http); c.Parameters.AddWithValue("@vehicle", vehicleNumber); }, ct) > 0)
            return Bad("Vehicle cannot enter service while assigned to an active shipment.");
        var rows = await db.ExecuteAsync(@"
UPDATE fleet_tms_vehicles SET status=COALESCE(NULLIF(@status,''),'Maintenance'),
    health_status=COALESCE(NULLIF(@health,''), health_status),
    last_service_at_utc=NOW(), next_service_at_utc=COALESCE(@next, next_service_at_utc),
    notes=COALESCE(@notes, notes), updated_at_utc=NOW()
WHERE id=@id AND company_id=@companyId" + Owned(http),
            c =>
            {
                c.Parameters.AddWithValue("@status", status);
                c.Parameters.AddWithValue("@health", req.HealthStatus ?? "");
                c.Parameters.AddWithValue("@next", (object?)req.NextServiceAtUtc ?? DBNull.Value);
                c.Parameters.AddWithValue("@notes", S(req.Notes));
                c.Parameters.AddWithValue("@id", id);
                BindOwner(c, http);
            }, ct);
        if (rows == 0) return NotFound();
        return Ok(await RowById(db, http, "fleet_tms_vehicles", id, ct)!);
    }

    private static async Task<IResult> CloseMaintenance(HttpContext http, long id, CloseMaintenanceRequest req, Database db, CancellationToken ct)
    {
        var companyId = CompanyId(http);
        var ticket = await RowById(db, http, "fleet_tms_maintenance_tickets", id, ct);
        if (ticket is null) return NotFound();
        if ((ticket.GetValueOrDefault("status")?.ToString() ?? "") is not ("Open" or "InProgress" or "AwaitingParts"))
            return Bad("Only an open maintenance ticket can be closed.");
        if (req.ActualCost is < 0) return Bad("Actual cost cannot be negative.");
        if (req.ActualCost is > 1000000000m) return Bad("Actual cost exceeds the supported range.");
        if (TooLong(req.Notes, 4000)) return Bad("Maintenance notes cannot exceed 4000 characters.");
        if (!string.IsNullOrWhiteSpace(req.Status) && req.Status.Trim() != "Closed") return Bad("Maintenance close status must be Closed.");
        var rows = await db.ExecuteAsync(@"
UPDATE fleet_tms_maintenance_tickets SET status=COALESCE(NULLIF(@status,''),'Closed'),
    closed_at_utc=NOW(), actual_cost=COALESCE(@cost, actual_cost),
    notes=COALESCE(@notes, notes), updated_at_utc=NOW()
WHERE id=@id AND company_id=@companyId" + Owned(http),
            c =>
            {
                c.Parameters.AddWithValue("@status", req.Status ?? "");
                c.Parameters.AddWithValue("@cost", N(req.ActualCost));
                c.Parameters.AddWithValue("@notes", S(req.Notes));
                c.Parameters.AddWithValue("@id", id);
                BindOwner(c, http);
            }, ct);
        if (rows == 0) return NotFound();
        var vehicleNumber = ticket.GetValueOrDefault("vehicleNumber")?.ToString() ?? "";
        if (!string.IsNullOrWhiteSpace(vehicleNumber))
            await db.ExecuteAsync(@"
UPDATE fleet_tms_vehicles v SET status='Available', health_status='Healthy', updated_at_utc=NOW()
WHERE v.company_id=@companyId AND v.vehicle_number=@vehicle" + Owned(http, "v") + @"
  AND NOT EXISTS (SELECT 1 FROM fleet_tms_maintenance_tickets m
                  WHERE m.company_id=v.company_id AND m.vehicle_number=v.vehicle_number
                    AND m.id<>@id AND m.status IN ('Open','InProgress','AwaitingParts'))
  AND NOT EXISTS (SELECT 1 FROM fleet_tms_shipments s
                  WHERE s.company_id=v.company_id AND s.vehicle_number=v.vehicle_number
                    AND s.status NOT IN ('Delivered','Cancelled'))",
                c => { BindOwner(c, http); c.Parameters.AddWithValue("@vehicle", vehicleNumber); c.Parameters.AddWithValue("@id", id); }, ct);
        return Ok(await RowById(db, http, "fleet_tms_maintenance_tickets", id, ct)!);
    }

    private static async Task<IResult> FlagFuelEvent(HttpContext http, long id, FlagFuelEventRequest req, Database db, CancellationToken ct)
    {
        var companyId = CompanyId(http);
        if (await RowById(db, http, "fleet_tms_fuel_events", id, ct) is null) return NotFound();
        if (req.AnomalyFlag && string.IsNullOrWhiteSpace(req.Notes)) return Bad("An anomaly reason is required.");
        var rows = await db.ExecuteAsync(@"
UPDATE fleet_tms_fuel_events SET anomaly_flag=@flag, notes=COALESCE(@notes, notes), updated_at_utc=NOW()
WHERE id=@id AND company_id=@companyId" + Owned(http),
            c =>
            {
                c.Parameters.AddWithValue("@flag", req.AnomalyFlag);
                c.Parameters.AddWithValue("@notes", S(req.Notes));
                c.Parameters.AddWithValue("@id", id);
                BindOwner(c, http);
            }, ct);
        if (rows == 0) return NotFound();
        await Meter(db, http, "fuel_transactions.monthly", $"fuel:{id}", ct);
        return Ok(await RowById(db, http, "fleet_tms_fuel_events", id, ct)!);
    }

    private static async Task<IResult> MarkInvoiceReady(HttpContext http, long id, InvoiceReadyRequest req, Database db, CancellationToken ct)
    {
        var companyId = CompanyId(http);
        var shipment = await FindShipment(db, http, id, ct);
        if (shipment is null) return NotFound();
        if (Convert.ToBoolean(shipment.GetValueOrDefault("isInvoiceReady") ?? false)) return Ok(shipment);
        if (req.Override && EndpointMappings.RequirePermission(http, "billing:manage") is { } overrideDenied)
            return overrideDenied;

        if (TooLong(req.Notes, 4000)) return Bad("Invoice readiness notes cannot exceed 4000 characters.");
        var deliveryStops = await db.ScalarLongAsync(
            $"SELECT COUNT(*) FROM fleet_tms_shipment_stops WHERE company_id=@companyId{Owned(http)} AND shipment_id=@id AND stop_type='Delivery'",
            c => { BindOwner(c, http); c.Parameters.AddWithValue("@id", id); }, ct);
        var completedVerifiedStops = await db.ScalarLongAsync(
            $@"SELECT COUNT(*) FROM fleet_tms_shipment_stops st
                WHERE st.company_id=@companyId{Owned(http, "st")} AND st.shipment_id=@id AND st.stop_type='Delivery'
                  AND st.status='Completed'
                  AND EXISTS (SELECT 1 FROM fleet_tms_pods p WHERE p.company_id=st.company_id AND p.shipment_id=st.shipment_id AND p.stop_id=st.id AND p.status='Verified')",
            c => { BindOwner(c, http); c.Parameters.AddWithValue("@id", id); }, ct);
        var podOk = deliveryStops > 0 && completedVerifiedStops == deliveryStops;
        var delivered = string.Equals(shipment.GetValueOrDefault("status")?.ToString(), "Delivered", StringComparison.Ordinal);
        if ((!podOk || !delivered) && !req.Override)
            return Bad("Every delivery stop must be completed with its own verified POD, and the shipment must be delivered, before it can be invoice-ready.");

        await db.ExecuteAsync(@"
UPDATE fleet_tms_shipments SET is_invoice_ready=true, invoice_ready_at_utc=NOW(),
    invoice_readiness_notes=COALESCE(@notes, invoice_readiness_notes), updated_at_utc=NOW()
WHERE id=@id AND company_id=@companyId" + Owned(http),
            c => { c.Parameters.AddWithValue("@notes", S(req.Notes)); c.Parameters.AddWithValue("@id", id); BindOwner(c, http); }, ct);

        await LogEvent(db, http, id, "InvoiceReady", "Shipment marked invoice-ready.", "Private", ct);
        await Meter(db, http, "invoice_ready.monthly", $"shipment:{id}", ct);
        return Ok(await RowById(db, http, "fleet_tms_shipments", id, ct)!);
    }

    private static async Task<IResult> WorkspaceCarriers(HttpContext http, Database db, CancellationToken ct)
    {
        var items = await db.QueryAsync(@"
SELECT id, name, carrier_number code, status, region, '' service_type,
       '' vat_number, '' commercial_registration_no, '' transport_document_no,
       '' permit_no, '' national_address_building_no, '' national_address_additional_no,
       '' district, '' city, '' postal_code, '' country,
       compliance_status document_status,
       CASE WHEN insurance_expiry < CURRENT_DATE THEN 'Expired'
            WHEN insurance_expiry <= CURRENT_DATE + INTERVAL '30 days' THEN 'ExpiringSoon'
            ELSE 'Valid' END expiry_status,
       NULL hijri_expiry_date, insurance_expiry gregorian_expiry_date,
       on_time_percent on_time_score, 100-risk_score damage_score, cost_score,
       notes, NOW() created_at_utc, updated_at updated_at_utc
FROM carriers WHERE company_id=@companyId AND deleted_at IS NULL
ORDER BY name",
            c => c.Parameters.AddWithValue("@companyId", CompanyId(http)), ct);
        return Ok(new { items });
    }

    private static async Task<IResult> AssignShipmentCarrier(HttpContext http, long id, AssignCarrierRequest req, Database db, CancellationToken ct)
    {
        if (req.CarrierId <= 0) return Bad("Carrier is required.");
        if (req.QuotedAmount is < 0 || req.AgreedAmount is < 0) return Bad("Carrier amounts cannot be negative.");
        if (req.QuotedAmount is > 1000000000m || req.AgreedAmount is > 1000000000m) return Bad("Carrier amounts exceed the supported range.");
        if (TooLong(req.Notes, 4000)) return Bad("Carrier notes cannot exceed 4000 characters.");
        if (req.QuotedAmount is { } quoted && req.AgreedAmount is { } agreed && agreed > quoted * 2 && quoted > 0)
            return Bad("Agreed amount requires review because it exceeds twice the quoted amount.");
        var companyId = CompanyId(http);
        return await db.RunInTenantTransactionAsync(companyId, async () =>
        {
        // Dispatch and carrier edits lock the same shipment row. Whichever transaction
        // commits first determines whether the carrier edit was made before dispatch;
        // a carrier can never be changed after the shipment leaves pre-dispatch state.
        var shipment = await db.QuerySingleAsync(
            $"SELECT * FROM fleet_tms_shipments WHERE id=@id AND company_id=@companyId{Owned(http)} FOR UPDATE",
            c => { c.Parameters.AddWithValue("@id", id); BindOwner(c, http); }, ct);
        if (shipment is null) return NotFound();
        if ((shipment.GetValueOrDefault("status")?.ToString() ?? "") is not ("Booked" or "Planned"))
            return Bad("Carrier assignment can only be changed before loading or dispatch.");
        var carrier = await db.QuerySingleAsync(
            "SELECT id, name, status, compliance_status FROM carriers WHERE id=@carrierId AND company_id=@companyId AND deleted_at IS NULL",
            c => { c.Parameters.AddWithValue("@carrierId", req.CarrierId); c.Parameters.AddWithValue("@companyId", companyId); }, ct);
        if (carrier is null) return Bad("Carrier is not owned by this tenant.");
        if ((carrier.GetValueOrDefault("status")?.ToString() ?? "") != "Active") return Bad("Only an active carrier can be assigned.");
        if ((carrier.GetValueOrDefault("complianceStatus")?.ToString() ?? "") != "Compliant") return Bad("Carrier must be compliant before assignment.");
        var rows = await db.ExecuteAsync(@"
UPDATE fleet_tms_shipments SET carrier_id=@carrierId, carrier_name=@carrierName,
 carrier_quoted_amount=COALESCE(@quoted,0), carrier_agreed_amount=COALESCE(@agreed,0),
 carrier_assignment_notes=COALESCE(@notes,''), carrier_assigned_at_utc=NOW(), updated_at_utc=NOW()
WHERE id=@id AND company_id=@companyId" + Owned(http) + " AND status IN ('Booked','Planned')",
            c => { c.Parameters.AddWithValue("@carrierId", req.CarrierId); c.Parameters.AddWithValue("@carrierName", carrier["name"]?.ToString() ?? ""); c.Parameters.AddWithValue("@quoted", N(req.QuotedAmount)); c.Parameters.AddWithValue("@agreed", N(req.AgreedAmount)); c.Parameters.AddWithValue("@notes", req.Notes?.Trim() ?? ""); c.Parameters.AddWithValue("@id", id); BindOwner(c, http); }, ct);
        if (rows == 0) return Results.Conflict(ApiResponse<object>.Fail("Shipment state changed while assigning the carrier; reload and retry."));
        await LogEvent(db, http, id, "CarrierAssigned", $"Carrier {carrier["name"]} assigned to shipment.", "Private", ct);
        var updated = await RowById(db, http, "fleet_tms_shipments", id, ct);
        return updated is null ? Results.Problem("Carrier was assigned but shipment could not be reloaded.") : Ok(CarrierAssignment(updated));
        }, ct);
    }

    private static async Task<IResult> UnassignShipmentCarrier(HttpContext http, long id, Database db, CancellationToken ct)
    {
        var companyId = CompanyId(http);
        return await db.RunInTenantTransactionAsync(companyId, async () =>
        {
        var shipment = await db.QuerySingleAsync(
            $"SELECT * FROM fleet_tms_shipments WHERE id=@id AND company_id=@companyId{Owned(http)} FOR UPDATE",
            c => { c.Parameters.AddWithValue("@id", id); BindOwner(c, http); }, ct);
        if (shipment is null) return NotFound();
        if ((shipment.GetValueOrDefault("status")?.ToString() ?? "") is not ("Booked" or "Planned"))
            return Bad("Carrier assignment can only be removed before loading or dispatch.");
        var rows = await db.ExecuteAsync($"UPDATE fleet_tms_shipments SET carrier_id=NULL, carrier_name='', carrier_quoted_amount=0, carrier_agreed_amount=0, carrier_assignment_notes='', carrier_assigned_at_utc=NULL, updated_at_utc=NOW() WHERE id=@id AND company_id=@companyId{Owned(http)} AND status IN ('Booked','Planned')",
            c => { c.Parameters.AddWithValue("@id", id); BindOwner(c, http); }, ct);
        if (rows == 0) return Results.Conflict(ApiResponse<object>.Fail("Shipment state changed while removing the carrier; reload and retry."));
        await LogEvent(db, http, id, "CarrierUnassigned", "Carrier removed from shipment.", "Private", ct);
        return Results.NoContent();
        }, ct);
    }

    private static object CarrierAssignment(Dictionary<string, object?> shipment) => new
    {
        id = shipment["id"], shipmentId = shipment["id"], carrierId = shipment.GetValueOrDefault("carrierId"),
        status = shipment.GetValueOrDefault("carrierId") is null ? "Unassigned" : "Assigned",
        quotedAmount = shipment.GetValueOrDefault("carrierQuotedAmount"), agreedAmount = shipment.GetValueOrDefault("carrierAgreedAmount"),
        notes = shipment.GetValueOrDefault("carrierAssignmentNotes"), assignedAtUtc = shipment.GetValueOrDefault("carrierAssignedAtUtc"),
        updatedAtUtc = shipment.GetValueOrDefault("updatedAtUtc")
    };

    // ── Stops ─────────────────────────────────────────────────────────────────

    private static async Task<IResult> GetStops(HttpContext http, long shipmentId, Database db, CancellationToken ct)
    {
        var companyId = CompanyId(http);
        if (await FindShipment(db, http, shipmentId, ct) is null) return NotFound();
        var items = await db.QueryAsync(
            $"SELECT * FROM fleet_tms_shipment_stops WHERE company_id=@companyId{Owned(http)} AND shipment_id=@sid ORDER BY sequence_no",
            c => { BindOwner(c, http); c.Parameters.AddWithValue("@sid", shipmentId); }, ct);
        return Ok(new { items });
    }

    private static async Task<IResult> CreateStop(HttpContext http, long shipmentId, ShipmentStopRequest req, Database db, CancellationToken ct)
    {
        var companyId = CompanyId(http);
        var shipment = await FindShipment(db, http, shipmentId, ct);
        if (shipment is null) return NotFound();
        if (ValidateStopRequest(req) is { } validation) return Bad(validation);
        var duplicate = await db.ScalarLongAsync(
            $"SELECT COUNT(*) FROM fleet_tms_shipment_stops WHERE company_id=@companyId{Owned(http)} AND shipment_id=@sid AND sequence_no=@seq",
            c => { BindOwner(c, http); c.Parameters.AddWithValue("@sid", shipmentId); c.Parameters.AddWithValue("@seq", req.SequenceNo); }, ct);
        if (duplicate > 0) return Bad("Stop sequence must be unique within the shipment.");

        var id = await db.InsertAsync(@"
INSERT INTO fleet_tms_shipment_stops
 (company_id, branch_id, shipment_id, stop_type, sequence_no, location_name, contact_name, contact_phone,
  address_line1, address_line2, city, region, postal_code, country,
  saudi_national_address_building_no, saudi_national_address_additional_no, saudi_national_address_district,
  latitude, longitude, planned_arrival_at, status, notes, created_at, updated_at)
VALUES (@companyId, @branchId, @sid, @stopType, @seq, @loc, @contactName, @contactPhone,
  @addr1, @addr2, @city, @region, @postal, @country,
  @saudiBuilding, @saudiAdditional, @saudiDistrict,
  @lat, @lng, @planned, 'Planned', @notes, NOW(), NOW())
ON CONFLICT (company_id, shipment_id, sequence_no) DO NOTHING",
            c => { BindStop(c, companyId, shipmentId, req); c.Parameters.AddWithValue("@branchId", shipment.GetValueOrDefault("branchId") ?? DBNull.Value); }, ct);
        if (id == 0) return Bad("Stop sequence must be unique within the shipment.");

        await LogEvent(db, http, shipmentId, "StopAdded", $"Stop {req.SequenceNo} added for {req.LocationName?.Trim()}.", "Private", ct);
        return Ok(await RowById(db, http, "fleet_tms_shipment_stops", id, ct)!);
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

    internal static string? ValidateStopRequest(ShipmentStopRequest req)
    {
        if (string.IsNullOrWhiteSpace(req.StopType) || req.StopType.Trim() is not ("Pickup" or "Delivery" or "Waypoint" or "Return"))
            return "Stop type must be Pickup, Delivery, Waypoint or Return.";
        if (req.SequenceNo is < 1 or > 999) return "Stop sequence must be between 1 and 999.";
        if (string.IsNullOrWhiteSpace(req.LocationName)) return "Location name is required.";
        if (req.LocationName.Trim().Length > 255) return "Location name cannot exceed 255 characters.";
        if (TooLong(req.ContactName, 255) || TooLong(req.ContactPhone, 60) || TooLong(req.AddressLine1, 255)
            || TooLong(req.AddressLine2, 255) || TooLong(req.City, 120) || TooLong(req.Region, 120)
            || TooLong(req.PostalCode, 20) || TooLong(req.Country, 80) || TooLong(req.Notes, 4000))
            return "One or more stop fields exceed their maximum allowed length.";
        if (req.PlannedArrivalAt is null) return "Planned arrival is required.";
        if (req.Latitude is < -90 or > 90) return "Latitude must be between -90 and 90.";
        if (req.Longitude is < -180 or > 180) return "Longitude must be between -180 and 180.";
        return null;
    }

    private static async Task<IResult> UpdateStop(HttpContext http, long shipmentId, long stopId, ShipmentStopRequest req, Database db, CancellationToken ct)
    {
        var companyId = CompanyId(http);
        if (await FindShipment(db, http, shipmentId, ct) is null) return NotFound();
        if (ValidateStopRequest(req) is { } validation) return Bad(validation);
        var existing = await RowById(db, http, "fleet_tms_shipment_stops", stopId, ct);
        if (existing is null || Convert.ToInt64(existing.GetValueOrDefault("shipmentId") ?? 0) != shipmentId) return NotFound();
        if ((existing.GetValueOrDefault("status")?.ToString() ?? "") != "Planned") return Bad("Only a planned stop can be edited.");
        var duplicate = await db.ScalarLongAsync(
            $"SELECT COUNT(*) FROM fleet_tms_shipment_stops WHERE company_id=@companyId{Owned(http)} AND shipment_id=@sid AND sequence_no=@seq AND id<>@stopId",
            c => { BindOwner(c, http); c.Parameters.AddWithValue("@sid", shipmentId); c.Parameters.AddWithValue("@seq", req.SequenceNo); c.Parameters.AddWithValue("@stopId", stopId); }, ct);
        if (duplicate > 0) return Bad("Stop sequence must be unique within the shipment.");
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
WHERE id=@stopId AND shipment_id=@sid AND company_id=@companyId" + Owned(http),
            c => { BindStop(c, companyId, shipmentId, req); if (BranchId(http) is { } branchId) c.Parameters.AddWithValue("@branchId", branchId); c.Parameters.AddWithValue("@stopId", stopId); }, ct);
        if (rows == 0) return NotFound();
        return Ok(await RowById(db, http, "fleet_tms_shipment_stops", stopId, ct)!);
    }

    private static async Task<IResult> ArriveStop(HttpContext http, long shipmentId, long stopId, Database db, CancellationToken ct)
    {
        var companyId = CompanyId(http);
        var stop = await db.QuerySingleAsync(
            $"UPDATE fleet_tms_shipment_stops SET status='Arrived', actual_arrival_at=NOW(), updated_at=NOW() WHERE id=@stopId AND shipment_id=@sid AND company_id=@companyId{Owned(http)} AND status='Planned' RETURNING *",
            c => { c.Parameters.AddWithValue("@stopId", stopId); c.Parameters.AddWithValue("@sid", shipmentId); BindOwner(c, http); }, ct);
        if (stop is null) return Bad("Only a planned stop can be marked arrived.");
        await LogEvent(db, http, shipmentId, "StopArrived", $"Stop {stop["sequenceNo"]} arrived at {stop["locationName"]}.", "Private", ct);
        return Ok(stop);
    }

    private static async Task<IResult> CompleteStop(HttpContext http, long shipmentId, long stopId, NotesRequest req, Database db, CancellationToken ct)
    {
        var companyId = CompanyId(http);
        var stop = await db.QuerySingleAsync(
            $"UPDATE fleet_tms_shipment_stops SET status='Completed', completed_at=NOW(), notes=COALESCE(NULLIF(@notes,''), notes), updated_at=NOW() WHERE id=@stopId AND shipment_id=@sid AND company_id=@companyId{Owned(http)} AND status='Arrived' RETURNING *",
            c => { c.Parameters.AddWithValue("@notes", req?.Notes?.Trim() ?? ""); c.Parameters.AddWithValue("@stopId", stopId); c.Parameters.AddWithValue("@sid", shipmentId); BindOwner(c, http); }, ct);
        if (stop is null) return Bad("Only an arrived stop can be completed.");
        await RefreshShipmentDeliveryState(db, http, shipmentId, ct);
        await LogEvent(db, http, shipmentId, "StopCompleted", $"Stop {stop["sequenceNo"]} completed at {stop["locationName"]}.", "Private", ct);
        return Ok(stop);
    }

    // ── Events / tracking links ─────────────────────────────────────────────────

    private static async Task<IResult> GetShipmentEvents(HttpContext http, long shipmentId, Database db, CancellationToken ct)
    {
        var companyId = CompanyId(http);
        if (await FindShipment(db, http, shipmentId, ct) is null) return NotFound();
        var items = await db.QueryAsync(
            $"SELECT * FROM fleet_tms_shipment_events WHERE company_id=@companyId{Owned(http)} AND shipment_id=@sid ORDER BY occurred_at_utc DESC",
            c => { BindOwner(c, http); c.Parameters.AddWithValue("@sid", shipmentId); }, ct);
        return Ok(new { items });
    }

    private static async Task<IResult> GetTrackingLinks(HttpContext http, long shipmentId, Database db, CancellationToken ct)
    {
        var companyId = CompanyId(http);
        if (await FindShipment(db, http, shipmentId, ct) is null) return NotFound();
        var items = await db.QueryAsync(
            @"SELECT id, shipment_id, expires_at_utc, is_revoked, shared_by,
                     created_at_utc, revoked_at_utc, updated_at_utc
              FROM fleet_tms_tracking_links
              WHERE company_id=@companyId" + Owned(http) + @" AND shipment_id=@sid
              ORDER BY created_at_utc DESC",
            c => { BindOwner(c, http); c.Parameters.AddWithValue("@sid", shipmentId); }, ct);
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
        var shipment = await FindShipment(db, http, shipmentId, ct);
        if (shipment is null) return NotFound();
        var expires = req.ExpiresAtUtc ?? DateTime.UtcNow.AddDays(7);
        if (expires <= DateTime.UtcNow || expires > DateTime.UtcNow.AddDays(30))
            return Bad("Tracking link expiry must be in the future and no more than 30 days away.");
        // Public tracking tokens are ALWAYS server-generated with enforced 256-bit entropy — never accept a
        // caller-supplied value (a tenant or compromised integration could otherwise set a short, sequential
        // or guessable public token, defeating the unguessable-secret guarantee). (P2 security fix.)
        var token = Convert.ToHexString(System.Security.Cryptography.RandomNumberGenerator.GetBytes(32)).ToLowerInvariant();
        var tokenHash = HashTrackingToken(token);
        var id = await db.InsertAsync(@"
INSERT INTO fleet_tms_tracking_links (company_id, branch_id, shipment_id, token_hash, expires_at_utc, shared_by, created_at_utc)
VALUES (@companyId, @branchId, @sid, @tokenHash, @expires, @sharedBy, NOW())",
            c =>
            {
                c.Parameters.AddWithValue("@companyId", companyId);
                c.Parameters.AddWithValue("@branchId", shipment.GetValueOrDefault("branchId") ?? DBNull.Value);
                c.Parameters.AddWithValue("@sid", shipmentId);
                c.Parameters.AddWithValue("@tokenHash", tokenHash);
                c.Parameters.AddWithValue("@expires", expires);
                c.Parameters.AddWithValue("@sharedBy", Actor(http));
            }, ct);
        await LogEvent(db, http, shipmentId, "TrackingLinkCreated", "Customer tracking link generated.", "Public", ct);
        await Meter(db, http, "tracking_links.monthly", $"shipment:{shipmentId}", ct);
        var created = await db.QuerySingleAsync(
            @"SELECT id, shipment_id, expires_at_utc, is_revoked, shared_by,
                     created_at_utc, revoked_at_utc, updated_at_utc
              FROM fleet_tms_tracking_links WHERE id=@id AND company_id=@companyId" + Owned(http),
            c => { c.Parameters.AddWithValue("@id", id); BindOwner(c, http); }, ct);
        if (created is null) return Results.Problem("Tracking link was created but could not be reloaded.");
        created["token"] = token; // one-time secret: only the create response reveals it
        return Ok(created);
    }

    private static async Task<IResult> RevokeTrackingLink(HttpContext http, long shipmentId, long linkId, Database db, CancellationToken ct)
    {
        var companyId = CompanyId(http);
        var rows = await db.ExecuteAsync(
            $"UPDATE fleet_tms_tracking_links SET is_revoked=true, revoked_at_utc=NOW(), updated_at_utc=NOW() WHERE id=@linkId AND shipment_id=@sid AND company_id=@companyId{Owned(http)} AND is_revoked=false",
            c => { c.Parameters.AddWithValue("@linkId", linkId); c.Parameters.AddWithValue("@sid", shipmentId); BindOwner(c, http); }, ct);
        if (rows == 0) return NotFound();
        return Results.NoContent();
    }

    // ── POD ───────────────────────────────────────────────────────────────────

    private static async Task<IResult> GetPod(HttpContext http, long shipmentId, Database db, CancellationToken ct)
    {
        var companyId = CompanyId(http);
        if (await FindShipment(db, http, shipmentId, ct) is null) return NotFound();
        var items = await db.QueryAsync(
            $"SELECT * FROM fleet_tms_pods WHERE company_id=@companyId{Owned(http)} AND shipment_id=@sid ORDER BY captured_at DESC",
            c => { BindOwner(c, http); c.Parameters.AddWithValue("@sid", shipmentId); }, ct);
        return Ok(new { items });
    }

    private static async Task<IResult> CreatePod(HttpContext http, long shipmentId, PodRequest req, Database db, CancellationToken ct)
    {
        var companyId = CompanyId(http);
        var stop = await db.QuerySingleAsync(
            $"SELECT * FROM fleet_tms_shipment_stops WHERE id=@stopId AND shipment_id=@sid AND company_id=@companyId{Owned(http)}",
            c => { c.Parameters.AddWithValue("@stopId", req.StopId); c.Parameters.AddWithValue("@sid", shipmentId); BindOwner(c, http); }, ct);
        if (stop is null) return NotFound("Stop not found for shipment.");
        if ((stop.GetValueOrDefault("stopType")?.ToString() ?? "") != "Delivery") return Bad("POD can only be captured for a delivery stop.");
        if (ValidatePodRequest(req, requireRecipient: true) is { } podValidation) return Bad(podValidation);
        var duplicatePod = await db.ScalarLongAsync(
            $"SELECT COUNT(*) FROM fleet_tms_pods WHERE company_id=@companyId{Owned(http)} AND shipment_id=@sid AND stop_id=@stopId AND status<>'Rejected'",
            c => { BindOwner(c, http); c.Parameters.AddWithValue("@sid", shipmentId); c.Parameters.AddWithValue("@stopId", req.StopId); }, ct);
        if (duplicatePod > 0) return Bad("A POD already exists for this delivery stop.");

        var userId = http.Items.TryGetValue(EndpointMappings.AuthUserIdItemKey, out var u) && u is not null ? (object)Convert.ToInt64(u) : DBNull.Value;
        var id = await db.InsertAsync(@"
INSERT INTO fleet_tms_pods
 (company_id, branch_id, shipment_id, stop_id, captured_by_user_id, recipient_name, recipient_phone,
  signature_url, photo_url, document_url, notes, delivery_condition, captured_latitude, captured_longitude,
  captured_at, status, created_at, updated_at)
VALUES (@companyId, @branchId, @sid, @stopId, @uid, @recipient, @phone, @sig, @photo, @doc, @notes, @condition, @lat, @lng, NOW(), 'Draft', NOW(), NOW())
ON CONFLICT (company_id, shipment_id, stop_id) WHERE status <> 'Rejected' DO NOTHING",
            c =>
            {
                c.Parameters.AddWithValue("@companyId", companyId);
                c.Parameters.AddWithValue("@branchId", stop.GetValueOrDefault("branchId") ?? DBNull.Value);
                c.Parameters.AddWithValue("@sid", shipmentId);
                c.Parameters.AddWithValue("@stopId", req.StopId);
                c.Parameters.AddWithValue("@uid", userId);
                c.Parameters.AddWithValue("@recipient", req.RecipientName!.Trim());
                c.Parameters.AddWithValue("@phone", req.RecipientPhone?.Trim() ?? "");
                c.Parameters.AddWithValue("@sig", req.SignatureUrl?.Trim() ?? "");
                c.Parameters.AddWithValue("@photo", req.PhotoUrl?.Trim() ?? "");
                c.Parameters.AddWithValue("@doc", req.DocumentUrl?.Trim() ?? "");
                c.Parameters.AddWithValue("@notes", req.Notes?.Trim() ?? "");
                c.Parameters.AddWithValue("@condition", req.DeliveryCondition!.Trim());
                c.Parameters.AddWithValue("@lat", N(req.CapturedLatitude));
                c.Parameters.AddWithValue("@lng", N(req.CapturedLongitude));
            }, ct);
        if (id == 0) return Bad("A POD already exists for this delivery stop.");

        await LogEvent(db, http, shipmentId, "PodCreated", $"POD draft created for stop {stop["sequenceNo"]}.", "Private", ct);
        return Ok(await RowById(db, http, "fleet_tms_pods", id, ct)!);
    }

    private static async Task<IResult> UpdatePod(HttpContext http, long shipmentId, long podId, PodRequest req, Database db, CancellationToken ct)
    {
        var companyId = CompanyId(http);
        var existing = await RowById(db, http, "fleet_tms_pods", podId, ct);
        if (existing is null || Convert.ToInt64(existing.GetValueOrDefault("shipmentId") ?? 0) != shipmentId) return NotFound();
        if ((existing.GetValueOrDefault("status")?.ToString() ?? "") is not ("Draft" or "Rejected"))
            return Bad("Only a draft or rejected POD can be edited.");
        if (ValidatePodRequest(req, requireRecipient: false) is { } validation) return Bad(validation);
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
  status='Draft', verified_at=NULL, verified_by_user_id=NULL, updated_at=NOW()
WHERE id=@podId AND shipment_id=@sid AND company_id=@companyId" + Owned(http),
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
                BindOwner(c, http);
            }, ct);
        if (rows == 0) return NotFound();
        return Ok(await RowById(db, http, "fleet_tms_pods", podId, ct)!);
    }

    internal static string? ValidatePodRequest(PodRequest req, bool requireRecipient)
    {
        if (requireRecipient && string.IsNullOrWhiteSpace(req.RecipientName)) return "Recipient name is required.";
        if ((req.RecipientName?.Trim().Length ?? 0) > 255) return "Recipient name cannot exceed 255 characters.";
        if (TooLong(req.RecipientPhone, 60) || TooLong(req.Notes, 4000)) return "POD phone or notes exceed their maximum allowed length.";
        if (!string.IsNullOrWhiteSpace(req.DeliveryCondition) && req.DeliveryCondition.Trim() is not ("Good" or "Damaged" or "Partial" or "Rejected"))
            return "Delivery condition must be Good, Damaged, Partial or Rejected.";
        if (requireRecipient && string.IsNullOrWhiteSpace(req.DeliveryCondition)) return "Delivery condition is required.";
        if (req.CapturedLatitude is < -90 or > 90) return "Captured latitude must be between -90 and 90.";
        if (req.CapturedLongitude is < -180 or > 180) return "Captured longitude must be between -180 and 180.";
        foreach (var (label, raw) in new[] { ("Signature", req.SignatureUrl), ("Photo", req.PhotoUrl), ("Document", req.DocumentUrl) })
        {
            if (string.IsNullOrWhiteSpace(raw)) continue;
            if (raw.Length > 2048 || !Uri.TryCreate(raw, UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps)
                return $"{label} URL must be a valid HTTPS URL no longer than 2048 characters.";
        }
        return null;
    }

    private static async Task<IResult> SubmitPod(HttpContext http, long shipmentId, long podId, Database db, CancellationToken ct)
    {
        var companyId = CompanyId(http);
        var pod = await RowById(db, http, "fleet_tms_pods", podId, ct);
        if (pod is null || Convert.ToInt64(pod["shipmentId"]) != shipmentId) return NotFound();
        if ((pod.GetValueOrDefault("status")?.ToString() ?? "") != "Draft") return Bad("Only a draft POD can be submitted.");
        if (string.IsNullOrWhiteSpace(pod["recipientName"]?.ToString())) return Bad("Recipient name is required.");
        if (string.IsNullOrWhiteSpace(pod["deliveryCondition"]?.ToString())) return Bad("Delivery condition is required.");
        if (new[] { "signatureUrl", "photoUrl", "documentUrl" }.All(key => string.IsNullOrWhiteSpace(pod.GetValueOrDefault(key)?.ToString())))
            return Bad("At least one signature, photo or delivery document is required before submission.");
        await db.ExecuteAsync($"UPDATE fleet_tms_pods SET status='Submitted', updated_at=NOW() WHERE id=@podId AND company_id=@companyId{Owned(http)} AND status='Draft'",
            c => { c.Parameters.AddWithValue("@podId", podId); BindOwner(c, http); }, ct);
        await LogEvent(db, http, shipmentId, "PodSubmitted", "POD submitted for verification.", "Private", ct);
        await Meter(db, http, "pod.monthly", $"shipment:{shipmentId}", ct);
        return Ok(await RowById(db, http, "fleet_tms_pods", podId, ct)!);
    }

    private static async Task<IResult> VerifyPod(HttpContext http, long shipmentId, long podId, Database db, CancellationToken ct)
    {
        var companyId = CompanyId(http);
        var userId = http.Items.TryGetValue(EndpointMappings.AuthUserIdItemKey, out var u) && u is not null ? (object)Convert.ToInt64(u) : DBNull.Value;
        var rows = await db.ExecuteAsync(
            $"UPDATE fleet_tms_pods SET status='Verified', verified_at=NOW(), verified_by_user_id=@uid, updated_at=NOW() WHERE id=@podId AND shipment_id=@sid AND company_id=@companyId{Owned(http)} AND status='Submitted'",
            c => { c.Parameters.AddWithValue("@uid", userId); c.Parameters.AddWithValue("@podId", podId); c.Parameters.AddWithValue("@sid", shipmentId); BindOwner(c, http); }, ct);
        if (rows == 0) return Bad("Only a submitted POD can be verified.");
        await RefreshShipmentDeliveryState(db, http, shipmentId, ct);
        await LogEvent(db, http, shipmentId, "PodVerified", "POD verified.", "Private", ct);
        return Ok(await RowById(db, http, "fleet_tms_pods", podId, ct)!);
    }

    private static async Task<IResult> RejectPod(HttpContext http, long shipmentId, long podId, NotesRequest req, Database db, CancellationToken ct)
    {
        var companyId = CompanyId(http);
        if (string.IsNullOrWhiteSpace(req?.Notes)) return Bad("A rejection reason is required.");
        var rows = await db.ExecuteAsync(
            $"UPDATE fleet_tms_pods SET status='Rejected', notes=@notes, verified_at=NULL, verified_by_user_id=NULL, updated_at=NOW() WHERE id=@podId AND shipment_id=@sid AND company_id=@companyId{Owned(http)} AND status='Submitted'",
            c => { c.Parameters.AddWithValue("@notes", req!.Notes!.Trim()); c.Parameters.AddWithValue("@podId", podId); c.Parameters.AddWithValue("@sid", shipmentId); BindOwner(c, http); }, ct);
        if (rows == 0) return Bad("Only a submitted POD can be rejected.");
        await db.ExecuteAsync($"UPDATE fleet_tms_shipments SET pod_status='Rejected', is_invoice_ready=false, invoice_ready_at_utc=NULL, updated_at_utc=NOW() WHERE id=@sid AND company_id=@companyId{Owned(http)}",
            c => { c.Parameters.AddWithValue("@sid", shipmentId); BindOwner(c, http); }, ct);
        await LogEvent(db, http, shipmentId, "PodRejected", "POD rejected.", "Private", ct);
        return Ok(await RowById(db, http, "fleet_tms_pods", podId, ct)!);
    }

    private static async Task RefreshShipmentDeliveryState(Database db, HttpContext http, long shipmentId, CancellationToken ct)
    {
        var total = await db.ScalarLongAsync(
            $"SELECT COUNT(*) FROM fleet_tms_shipment_stops st WHERE st.company_id=@companyId{Owned(http, "st")} AND st.shipment_id=@sid AND st.stop_type='Delivery'",
            c => { c.Parameters.AddWithValue("@sid", shipmentId); BindOwner(c, http); }, ct);
        var remaining = await db.ScalarLongAsync(
            $@"SELECT COUNT(*) FROM fleet_tms_shipment_stops st
                WHERE st.company_id=@companyId{Owned(http, "st")} AND st.shipment_id=@sid AND st.stop_type='Delivery'
                  AND (st.status<>'Completed' OR NOT EXISTS (
                    SELECT 1 FROM fleet_tms_pods p WHERE p.company_id=st.company_id AND p.shipment_id=st.shipment_id
                      AND p.stop_id=st.id AND p.status='Verified'))",
            c => { c.Parameters.AddWithValue("@sid", shipmentId); BindOwner(c, http); }, ct);
        var verified = await db.ScalarLongAsync(
            $"SELECT COUNT(DISTINCT stop_id) FROM fleet_tms_pods WHERE company_id=@companyId{Owned(http)} AND shipment_id=@sid AND status='Verified'",
            c => { c.Parameters.AddWithValue("@sid", shipmentId); BindOwner(c, http); }, ct);
        var complete = total > 0 && remaining == 0;
        await db.ExecuteAsync(
            $@"UPDATE fleet_tms_shipments SET
                  pod_status=CASE WHEN @complete THEN 'Verified' WHEN @verified>0 THEN 'Partial' ELSE 'Pending' END,
                  status=CASE WHEN @complete THEN 'Delivered' ELSE status END,
                  delivered_at_utc=CASE WHEN @complete THEN COALESCE(delivered_at_utc,NOW()) ELSE delivered_at_utc END,
                  updated_at_utc=NOW()
                WHERE id=@sid AND company_id=@companyId{Owned(http)}",
            c => { c.Parameters.AddWithValue("@complete", complete); c.Parameters.AddWithValue("@verified", verified); c.Parameters.AddWithValue("@sid", shipmentId); BindOwner(c, http); }, ct);
        if (!complete) return;
        var shipment = await FindShipment(db, http, shipmentId, ct);
        var vehicle = shipment?.GetValueOrDefault("vehicleNumber")?.ToString();
        if (string.IsNullOrWhiteSpace(vehicle)) return;
        await db.ExecuteAsync(
            $@"UPDATE fleet_tms_vehicles v SET status='Available', current_load_kg=0, updated_at_utc=NOW()
                WHERE v.company_id=@companyId{Owned(http, "v")} AND v.vehicle_number=@vehicle
                  AND NOT EXISTS (SELECT 1 FROM fleet_tms_shipments s WHERE s.company_id=v.company_id AND s.id<>@sid
                    AND s.vehicle_number=v.vehicle_number AND s.status NOT IN ('Delivered','Cancelled'))",
            c => { c.Parameters.AddWithValue("@vehicle", vehicle); c.Parameters.AddWithValue("@sid", shipmentId); BindOwner(c, http); }, ct);
    }

    // ── Driver tasks ────────────────────────────────────────────────────────────

    private static async Task<IResult> GetDriverTasks(HttpContext http, Database db, string? driverName, CancellationToken ct)
    {
        var driverOwnership = IsDriver(http)
            ? " AND driver_id=(SELECT id FROM drivers WHERE user_id=@userId AND company_id=@companyId AND deleted_at IS NULL LIMIT 1)"
            : "";
        var where = "WHERE company_id=@companyId" + Owned(http) + driverOwnership + (string.IsNullOrWhiteSpace(driverName) || IsDriver(http) ? "" : " AND driver_name=@driver");
        var items = await db.QueryAsync($"SELECT * FROM fleet_tms_driver_tasks {where} ORDER BY due_at_utc",
            c => { BindOwner(c, http); if (IsDriver(http)) c.Parameters.AddWithValue("@userId", Convert.ToInt64(http.Items[EndpointMappings.AuthUserIdItemKey]!)); else if (!string.IsNullOrWhiteSpace(driverName)) c.Parameters.AddWithValue("@driver", driverName.Trim()); }, ct);
        return Ok(new { items });
    }

    private static async Task<IResult> GetDriverTask(HttpContext http, long taskId, Database db, CancellationToken ct)
    {
        var task = await RowById(db, http, "fleet_tms_driver_tasks", taskId, ct);
        return task is null || !await CanAccessDriverTask(db, http, task, ct) ? NotFound() : Ok(task);
    }

    private static async Task<IResult> ArriveDriverTask(HttpContext http, long taskId, Database db, CancellationToken ct)
    {
        var companyId = CompanyId(http);
        var existing = await RowById(db, http, "fleet_tms_driver_tasks", taskId, ct);
        if (existing is null || !await CanAccessDriverTask(db, http, existing, ct)) return NotFound();
        var task = await db.QuerySingleAsync(
            $"UPDATE fleet_tms_driver_tasks SET status='Arrived', updated_at_utc=NOW() WHERE id=@taskId AND company_id=@companyId{Owned(http)} AND status='Open' RETURNING *",
            c => { c.Parameters.AddWithValue("@taskId", taskId); BindOwner(c, http); }, ct);
        if (task is null) return Bad("Only an open driver task can be marked arrived.");
        if (task.GetValueOrDefault("stopId") is { } stopValue && stopValue is not DBNull)
            await db.ExecuteAsync($"UPDATE fleet_tms_shipment_stops SET status='Arrived', actual_arrival_at=COALESCE(actual_arrival_at,NOW()), updated_at=NOW() WHERE id=@stopId AND company_id=@companyId{Owned(http)} AND status='Planned'",
                c => { c.Parameters.AddWithValue("@stopId", Convert.ToInt64(stopValue)); BindOwner(c, http); }, ct);
        await LogEvent(db, http, Convert.ToInt64(task["shipmentId"]), "DriverTaskArrived", $"{task["title"]} arrived.", "Private", ct);
        return Ok(task);
    }

    private static async Task<IResult> CompleteDriverTask(HttpContext http, long taskId, NotesRequest req, Database db, CancellationToken ct)
    {
        var companyId = CompanyId(http);
        if (TooLong(req?.Notes, 4000)) return Bad("Task notes cannot exceed 4000 characters.");
        var existing = await RowById(db, http, "fleet_tms_driver_tasks", taskId, ct);
        if (existing is null || !await CanAccessDriverTask(db, http, existing, ct)) return NotFound();
        var task = await db.QuerySingleAsync(
            $"UPDATE fleet_tms_driver_tasks SET status='Completed', completed_at_utc=NOW(), notes=COALESCE(NULLIF(@notes,''), notes), updated_at_utc=NOW() WHERE id=@taskId AND company_id=@companyId{Owned(http)} AND status='Arrived' RETURNING *",
            c => { c.Parameters.AddWithValue("@notes", req?.Notes?.Trim() ?? ""); c.Parameters.AddWithValue("@taskId", taskId); BindOwner(c, http); }, ct);
        if (task is null) return Bad("Only an arrived driver task can be completed.");
        if (task.GetValueOrDefault("stopId") is { } stopValue && stopValue is not DBNull)
            await db.ExecuteAsync($"UPDATE fleet_tms_shipment_stops SET status='Completed', completed_at=COALESCE(completed_at,NOW()), updated_at=NOW() WHERE id=@stopId AND company_id=@companyId{Owned(http)} AND status='Arrived'",
                c => { c.Parameters.AddWithValue("@stopId", Convert.ToInt64(stopValue)); BindOwner(c, http); }, ct);
        await RefreshShipmentDeliveryState(db, http, Convert.ToInt64(task["shipmentId"]), ct);
        await LogEvent(db, http, Convert.ToInt64(task["shipmentId"]), "DriverTaskCompleted", $"{task["title"]} completed.", "Private", ct);
        return Ok(task);
    }

    private static async Task<IResult> UpsertDriverTaskPod(HttpContext http, long taskId, PodRequest req, Database db, CancellationToken ct)
    {
        var companyId = CompanyId(http);
        var task = await RowById(db, http, "fleet_tms_driver_tasks", taskId, ct);
        if (task is null || !await CanAccessDriverTask(db, http, task, ct)) return NotFound();
        var shipmentId = Convert.ToInt64(task["shipmentId"]);
        if (task.GetValueOrDefault("stopId") is not { } rawStop || rawStop is DBNull) return Bad("Driver task is not linked to a stop.");
        var stopId = Convert.ToInt64(rawStop);
        if (req.StopId > 0 && req.StopId != stopId) return Bad("POD stop must match the driver's assigned task stop.");
        var stop = await db.QuerySingleAsync(
            $"SELECT * FROM fleet_tms_shipment_stops WHERE id=@stopId AND shipment_id=@sid AND company_id=@companyId{Owned(http)} AND stop_type='Delivery'",
            c => { c.Parameters.AddWithValue("@stopId", stopId); c.Parameters.AddWithValue("@sid", shipmentId); BindOwner(c, http); }, ct);
        if (stop is null) return Bad("POD can only be captured for the delivery stop assigned to this task.");
        var pod = await db.QuerySingleAsync(
            $"SELECT * FROM fleet_tms_pods WHERE company_id=@companyId{Owned(http)} AND shipment_id=@sid AND stop_id=@stopId AND status IN ('Draft','Rejected') ORDER BY captured_at DESC LIMIT 1",
            c => { BindOwner(c, http); c.Parameters.AddWithValue("@sid", shipmentId); c.Parameters.AddWithValue("@stopId", stopId); }, ct);
        if (ValidatePodRequest(req, requireRecipient: false) is { } validation) return Bad(validation);
        if (pod is null)
        {
            if (string.IsNullOrWhiteSpace(req.RecipientName) || string.IsNullOrWhiteSpace(req.DeliveryCondition))
                return Bad("Recipient and delivery condition are required to create task POD.");
            var newPodId = await db.InsertAsync(@"
INSERT INTO fleet_tms_pods (company_id,branch_id,shipment_id,stop_id,captured_by_user_id,driver_id,vehicle_id,
 recipient_name,recipient_phone,signature_url,photo_url,document_url,notes,delivery_condition,captured_latitude,captured_longitude,status)
VALUES (@companyId,@branchId,@sid,@stopId,@uid,@driverId,@vehicleId,@recipient,@phone,@sig,@photo,@doc,@notes,@condition,@lat,@lng,'Draft')
ON CONFLICT (company_id, shipment_id, stop_id) WHERE status <> 'Rejected' DO NOTHING",
                c =>
                {
                    c.Parameters.AddWithValue("@companyId", companyId); c.Parameters.AddWithValue("@branchId", stop.GetValueOrDefault("branchId") ?? DBNull.Value);
                    c.Parameters.AddWithValue("@sid", shipmentId); c.Parameters.AddWithValue("@stopId", stopId);
                    c.Parameters.AddWithValue("@uid", http.Items.TryGetValue(EndpointMappings.AuthUserIdItemKey, out var uid) ? uid ?? DBNull.Value : DBNull.Value);
                    c.Parameters.AddWithValue("@driverId", task.GetValueOrDefault("driverId") ?? DBNull.Value); c.Parameters.AddWithValue("@vehicleId", task.GetValueOrDefault("vehicleId") ?? DBNull.Value);
                    c.Parameters.AddWithValue("@recipient", req.RecipientName!.Trim()); c.Parameters.AddWithValue("@phone", req.RecipientPhone?.Trim() ?? "");
                    c.Parameters.AddWithValue("@sig", req.SignatureUrl?.Trim() ?? ""); c.Parameters.AddWithValue("@photo", req.PhotoUrl?.Trim() ?? ""); c.Parameters.AddWithValue("@doc", req.DocumentUrl?.Trim() ?? "");
                    c.Parameters.AddWithValue("@notes", req.Notes?.Trim() ?? ""); c.Parameters.AddWithValue("@condition", req.DeliveryCondition!.Trim());
                    c.Parameters.AddWithValue("@lat", N(req.CapturedLatitude)); c.Parameters.AddWithValue("@lng", N(req.CapturedLongitude));
                }, ct);
            if (newPodId == 0)
            {
                var concurrent = await db.QuerySingleAsync(
                    $"SELECT * FROM fleet_tms_pods WHERE company_id=@companyId{Owned(http)} AND shipment_id=@sid AND stop_id=@stopId AND status<>'Rejected'",
                    c => { BindOwner(c, http); c.Parameters.AddWithValue("@sid", shipmentId); c.Parameters.AddWithValue("@stopId", stopId); }, ct);
                return concurrent is null ? Results.Conflict(ApiResponse<object>.Fail("A POD was created concurrently; retry the task.")) : Ok(concurrent);
            }
            return Ok(await RowById(db, http, "fleet_tms_pods", newPodId, ct)!);
        }
        await db.ExecuteAsync(
            $"UPDATE fleet_tms_pods SET recipient_name=COALESCE(NULLIF(@recipient,''), recipient_name), recipient_phone=COALESCE(NULLIF(@phone,''),recipient_phone), signature_url=COALESCE(NULLIF(@sig,''),signature_url), photo_url=COALESCE(NULLIF(@photo,''),photo_url), document_url=COALESCE(NULLIF(@doc,''),document_url), notes=COALESCE(NULLIF(@notes,''),notes), delivery_condition=COALESCE(NULLIF(@condition,''), delivery_condition), captured_latitude=COALESCE(@lat,captured_latitude), captured_longitude=COALESCE(@lng,captured_longitude), status='Draft', updated_at=NOW() WHERE id=@podId AND stop_id=@stopId AND company_id=@companyId{Owned(http)}",
            c => { c.Parameters.AddWithValue("@recipient", req.RecipientName?.Trim() ?? ""); c.Parameters.AddWithValue("@phone", req.RecipientPhone?.Trim() ?? ""); c.Parameters.AddWithValue("@sig", req.SignatureUrl?.Trim() ?? ""); c.Parameters.AddWithValue("@photo", req.PhotoUrl?.Trim() ?? ""); c.Parameters.AddWithValue("@doc", req.DocumentUrl?.Trim() ?? ""); c.Parameters.AddWithValue("@notes", req.Notes?.Trim() ?? ""); c.Parameters.AddWithValue("@condition", req.DeliveryCondition?.Trim() ?? ""); c.Parameters.AddWithValue("@lat", N(req.CapturedLatitude)); c.Parameters.AddWithValue("@lng", N(req.CapturedLongitude)); c.Parameters.AddWithValue("@podId", Convert.ToInt64(pod["id"])); c.Parameters.AddWithValue("@stopId", stopId); BindOwner(c, http); }, ct);
        return Ok(await RowById(db, http, "fleet_tms_pods", Convert.ToInt64(pod["id"]), ct)!);
    }

    // ── Public tracking (anonymous, token-scoped) ───────────────────────────────

    internal static string HashTrackingToken(string token)
        => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token))).ToLowerInvariant();

    private static async Task<Dictionary<string, object?>?> ResolveLink(Database db, string token, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(token) || token.Length > 120) return null;
        var tokenHash = HashTrackingToken(token);
        return await db.QuerySingleAsync(
            @"SELECT id, company_id, shipment_id, expires_at_utc
              FROM fleet_tms_tracking_links
              WHERE token_hash=@tokenHash AND is_revoked=false AND expires_at_utc > NOW()",
            c => c.Parameters.AddWithValue("@tokenHash", tokenHash), ct);
    }

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
            "SELECT id, recipient_name, delivery_condition, status, captured_at, verified_at, signature_url, photo_url, document_url FROM fleet_tms_pods WHERE company_id=@companyId AND shipment_id=@sid AND status='Verified'",
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
        string? Proxy(string key, string kind)
        {
            var raw = p.TryGetValue(key, out var v) ? v?.ToString() : null;
            if (string.IsNullOrWhiteSpace(raw)) return null;
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
            signatureUrl = Proxy("signatureUrl", "signature"),
            photoUrl = Proxy("photoUrl", "photo"),
            documentUrl = Proxy("documentUrl", "document"),
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
            "SELECT id, recipient_name, delivery_condition, status, captured_at, verified_at, signature_url, photo_url, document_url FROM fleet_tms_pods WHERE company_id=@companyId AND shipment_id=@sid AND status='Verified'",
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
        string token, long podId, string kind, Database db, IConfiguration config, CancellationToken ct)
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
            $"SELECT {column} AS asset_url FROM fleet_tms_pods WHERE id=@podId AND company_id=@companyId AND shipment_id=@sid AND status='Verified'",
            c => { c.Parameters.AddWithValue("@podId", podId); c.Parameters.AddWithValue("@companyId", Convert.ToInt64(link["companyId"])); c.Parameters.AddWithValue("@sid", Convert.ToInt64(link["shipmentId"])); }, ct);
        var rawUrl = pod?.GetValueOrDefault("assetUrl")?.ToString();
        if (string.IsNullOrWhiteSpace(rawUrl)) return NotFound();

        if (!Uri.TryCreate(rawUrl, UriKind.Absolute, out var uri) ||
            uri.Scheme != Uri.UriSchemeHttps ||
            uri.Port != 443 ||
            !string.IsNullOrEmpty(uri.UserInfo) ||
            !IsAllowlistedHost(config, uri.Host))
        {
            // Asset exists but lives on a host we will not proxy (open/untrusted
            // storage). Do not disclose or fetch it — treat as not-yet-available.
            return Results.Json(ApiResponse<object>.Fail("Asset unavailable", "POD asset storage is not yet secured for public delivery."), statusCode: StatusCodes.Status404NotFound);
        }

        try
        {
            var addresses = await Dns.GetHostAddressesAsync(uri.DnsSafeHost, ct);
            if (addresses.Length == 0 || addresses.Any(IsBlockedPodAssetAddress)) return NotFound();

            using var handler = CreatePodAssetHandler(addresses);
            using var client = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(15) };
            using var upstream = await client.GetAsync(uri, HttpCompletionOption.ResponseHeadersRead, ct);
            if (!upstream.IsSuccessStatusCode) return NotFound();
            if ((int)upstream.StatusCode is >= 300 and < 400) return NotFound();
            var contentType = upstream.Content.Headers.ContentType?.MediaType;
            if (!IsAllowedPodAssetContentType(kind, contentType) ||
                upstream.Content.Headers.ContentLength is > MaxPodAssetBytes)
                return NotFound();

            await using var source = await upstream.Content.ReadAsStreamAsync(ct);
            using var destination = new MemoryStream();
            var chunk = new byte[64 * 1024];
            while (true)
            {
                var read = await source.ReadAsync(chunk.AsMemory(), ct);
                if (read == 0) break;
                if (destination.Length + read > MaxPodAssetBytes) return NotFound();
                await destination.WriteAsync(chunk.AsMemory(0, read), ct);
            }
            var bytes = destination.ToArray();
            return Results.File(bytes, contentType);
        }
        catch (Exception ex) when (ex is HttpRequestException or IOException or SocketException or TaskCanceledException)
        {
            return NotFound();
        }
    }

    private const long MaxPodAssetBytes = 10 * 1024 * 1024;

    internal static bool IsAllowedPodAssetContentType(string kind, string? contentType) =>
        kind switch
        {
            "signature" or "photo" => contentType is "image/jpeg" or "image/png" or "image/webp",
            "document" => contentType is "application/pdf" or "image/jpeg" or "image/png",
            _ => false,
        };

    internal static bool IsBlockedPodAssetAddress(IPAddress address)
    {
        if (IPAddress.IsLoopback(address) || address.Equals(IPAddress.Any) ||
            address.Equals(IPAddress.IPv6Any) || address.Equals(IPAddress.IPv6None))
            return true;

        if (address.IsIPv4MappedToIPv6) address = address.MapToIPv4();
        var bytes = address.GetAddressBytes();
        if (address.AddressFamily == AddressFamily.InterNetwork)
        {
            return bytes[0] is 0 or 10 or 127 ||
                   bytes[0] == 100 && bytes[1] is >= 64 and <= 127 ||
                   bytes[0] == 169 && bytes[1] == 254 ||
                   bytes[0] == 172 && bytes[1] is >= 16 and <= 31 ||
                   bytes[0] == 192 && bytes[1] == 0 && bytes[2] == 0 ||
                   bytes[0] == 192 && bytes[1] == 0 && bytes[2] == 2 ||
                   bytes[0] == 192 && bytes[1] == 168 ||
                   bytes[0] == 198 && bytes[1] is 18 or 19 ||
                   bytes[0] == 198 && bytes[1] == 51 && bytes[2] == 100 ||
                   bytes[0] == 203 && bytes[1] == 0 && bytes[2] == 113 ||
                   bytes[0] >= 224;
        }

        return address.IsIPv6LinkLocal || address.IsIPv6Multicast || address.IsIPv6SiteLocal ||
               (bytes[0] & 0xfe) == 0xfc ||
               address.GetAddressBytes().AsSpan(0, 4).SequenceEqual(new byte[] { 0x20, 0x01, 0x0d, 0xb8 });
    }

    private static SocketsHttpHandler CreatePodAssetHandler(IPAddress[] vettedAddresses)
    {
        var next = 0;
        return new SocketsHttpHandler
        {
            AllowAutoRedirect = false,
            AutomaticDecompression = DecompressionMethods.None,
            ConnectTimeout = TimeSpan.FromSeconds(5),
            ConnectCallback = async (context, ct) =>
            {
                Exception? last = null;
                for (var i = 0; i < vettedAddresses.Length; i++)
                {
                    var address = vettedAddresses[(Interlocked.Increment(ref next) + i) % vettedAddresses.Length];
                    var socket = new Socket(address.AddressFamily, SocketType.Stream, ProtocolType.Tcp);
                    try
                    {
                        await socket.ConnectAsync(new IPEndPoint(address, context.DnsEndPoint.Port), ct);
                        return new NetworkStream(socket, ownsSocket: true);
                    }
                    catch (Exception ex)
                    {
                        last = ex;
                        socket.Dispose();
                    }
                }
                throw new HttpRequestException("Unable to connect to the approved asset host.", last);
            },
        };
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
public record AssignCarrierRequest(long CarrierId, decimal? QuotedAmount, decimal? AgreedAmount, string? Notes);
