using System.Text.Json;
using Npgsql;
using Opstrax.Api.Data;
using Opstrax.Api.DTOs;

namespace Opstrax.Api.Controllers;

// Fleet TMS (PR2) endpoints — cold chain, returnable assets and Saudi fleet readiness,
// ported from the Zayra FleetTmsColdChain/Assets/Compliance controllers onto raw Npgsql
// + minimal API. Additive, all under /api/fleet-tms/* and company-scoped. Reads of
// fleet_tms_shipments (PR1) provide shipment context; the existing `carriers` table is
// only LEFT JOINed read-only for a display name.
public static class FleetTmsColdChainEndpoints
{
    public static void MapFleetTmsColdChainEndpoints(this WebApplication app)
    {
        // Cold chain
        app.MapGet("/api/fleet-tms/cold-chain/summary", ColdChainSummary);
        app.MapGet("/api/fleet-tms/cold-chain/devices", ColdChainDevices);
        app.MapPost("/api/fleet-tms/cold-chain/devices", CreateDevice);
        app.MapGet("/api/fleet-tms/cold-chain/shipments/{shipmentId:long}/readings", ShipmentReadings);
        app.MapPost("/api/fleet-tms/cold-chain/readings", CreateReading);
        app.MapGet("/api/fleet-tms/cold-chain/alerts", ColdChainAlerts);
        app.MapPost("/api/fleet-tms/cold-chain/alerts/{id:long}/resolve", ResolveAlert);
        app.MapGet("/api/fleet-tms/cold-chain/reports/{shipmentId:long}", ColdChainReport);

        // Assets
        app.MapGet("/api/fleet-tms/assets/types", AssetTypes);
        app.MapPost("/api/fleet-tms/assets/types", CreateAssetType);
        app.MapGet("/api/fleet-tms/assets", Assets);
        app.MapGet("/api/fleet-tms/assets/{id:long}", AssetDetail);
        app.MapPost("/api/fleet-tms/assets", CreateAsset);
        app.MapPut("/api/fleet-tms/assets/{id:long}", UpdateAsset);
        app.MapPost("/api/fleet-tms/assets/{id:long}/assign", AssignAsset);
        app.MapPost("/api/fleet-tms/assets/{id:long}/check-in", CheckInAsset);
        app.MapPost("/api/fleet-tms/assets/{id:long}/check-out", CheckOutAsset);
        app.MapGet("/api/fleet-tms/assets/{id:long}/events", AssetEvents);
        app.MapPost("/api/fleet-tms/assets/scan", ScanAsset);

        // Saudi readiness / compliance
        app.MapGet("/api/fleet-tms/saudi/regions", SaudiRegions);
        app.MapGet("/api/fleet-tms/compliance/documents", ComplianceDocuments);
        app.MapPost("/api/fleet-tms/compliance/documents", CreateComplianceDocument);
        app.MapPut("/api/fleet-tms/compliance/documents/{id:long}", UpdateComplianceDocument);
        app.MapGet("/api/fleet-tms/compliance/expiries", ComplianceExpiries);
        app.MapGet("/api/fleet-tms/vat/invoice-ready", VatInvoiceReady);
    }

    private const int ExpiryWindowDays = 30;

    private static long Cid(HttpContext http) => EndpointMappings.GetCompanyId(http);
    private static string Actor(HttpContext http)
        => http.Items.TryGetValue(EndpointMappings.AuthUserIdItemKey, out var u) && u is not null ? $"user:{u}" : "system";
    private static IResult Ok<T>(T data) => Results.Ok(ApiResponse<object>.Ok(data!));
    private static IResult NotFound(string m = "Not found") => Results.NotFound(ApiResponse<object>.Fail(m));
    private static IResult Bad(string m) => Results.BadRequest(ApiResponse<object>.Fail(m));
    private static object N(decimal? v) => (object?)v ?? DBNull.Value;
    private static object Nl(long? v) => (object?)v ?? DBNull.Value;
    private static object Dt(DateTime? v) => (object?)v ?? DBNull.Value;
    private static object Dte(DateOnly? v) => v.HasValue ? v.Value.ToDateTime(TimeOnly.MinValue) : DBNull.Value;

    private static async Task<Dictionary<string, object?>?> Row(Database db, string table, long companyId, long id, CancellationToken ct)
        => await db.QuerySingleAsync($"SELECT * FROM {table} WHERE id=@id AND company_id=@companyId",
            c => { c.Parameters.AddWithValue("@id", id); c.Parameters.AddWithValue("@companyId", companyId); }, ct);

    // ── Cold chain ──────────────────────────────────────────────────────────────

    private static async Task<IResult> ColdChainSummary(HttpContext http, Database db, CancellationToken ct)
    {
        var companyId = Cid(http);
        void B(NpgsqlCommand c) => c.Parameters.AddWithValue("@companyId", companyId);
        var totalReadings = await db.ScalarLongAsync("SELECT COUNT(*) FROM fleet_tms_temperature_readings WHERE company_id=@companyId", B, ct);
        var breachReadings = await db.ScalarLongAsync("SELECT COUNT(*) FROM fleet_tms_temperature_readings WHERE company_id=@companyId AND status='Breach'", B, ct);
        var summary = new
        {
            activeDevices = await db.ScalarLongAsync("SELECT COUNT(*) FROM fleet_tms_temperature_devices WHERE company_id=@companyId AND status='Active'", B, ct),
            readingsToday = await db.ScalarLongAsync("SELECT COUNT(*) FROM fleet_tms_temperature_readings WHERE company_id=@companyId AND recorded_at_utc >= date_trunc('day', NOW())", B, ct),
            openAlerts = await db.ScalarLongAsync("SELECT COUNT(*) FROM fleet_tms_temperature_alerts WHERE company_id=@companyId AND status IN ('Open','InReview')", B, ct),
            totalReadings,
            breachReadings,
            avgTemperatureCelsius = Math.Round(await db.ScalarDecimalAsync("SELECT COALESCE(AVG(temperature_celsius),0) FROM fleet_tms_temperature_readings WHERE company_id=@companyId", B, ct) ?? 0m, 1),
            compliancePercent = totalReadings == 0 ? 0m : Math.Round((1m - (breachReadings / (decimal)totalReadings)) * 100m, 1),
        };
        var zones = await db.QueryAsync("SELECT id, code, name, min_celsius, max_celsius, color, is_active, notes FROM fleet_tms_temperature_zones WHERE company_id=@companyId ORDER BY name", B, ct);
        var devices = await db.QueryAsync("SELECT id, device_code, name, vehicle_number, status, last_reported_temperature_celsius, battery_percent, last_ping_at_utc, notes FROM fleet_tms_temperature_devices WHERE company_id=@companyId ORDER BY last_ping_at_utc DESC NULLS LAST LIMIT 6", B, ct);
        var alerts = await db.QueryAsync("SELECT id, alert_type, severity, status, measured_temperature, threshold_min, threshold_max, triggered_at_utc, resolution_notes FROM fleet_tms_temperature_alerts WHERE company_id=@companyId AND status <> 'Resolved' ORDER BY triggered_at_utc DESC LIMIT 6", B, ct);
        var reports = await db.QueryAsync("SELECT id, shipment_id, shipment_number, generated_at_utc, compliance_percent, min_temperature_celsius, max_temperature_celsius, total_readings, breach_count, summary_json, notes FROM fleet_tms_cold_chain_reports WHERE company_id=@companyId ORDER BY generated_at_utc DESC LIMIT 6", B, ct);
        return Ok(new { generatedAtUtc = DateTime.UtcNow, summary, zones, devices, alerts, reports });
    }

    private static async Task<IResult> ColdChainDevices(HttpContext http, Database db, CancellationToken ct)
    {
        var items = await db.QueryAsync(@"
SELECT d.id, d.device_code, d.name, d.zone_id, z.code zone_code, z.name zone_name,
       d.shipment_id, s.shipment_number, d.vehicle_number, d.status,
       d.last_reported_temperature_celsius, d.battery_percent, d.last_ping_at_utc, d.notes
FROM fleet_tms_temperature_devices d
LEFT JOIN fleet_tms_temperature_zones z ON z.id=d.zone_id
LEFT JOIN fleet_tms_shipments s ON s.id=d.shipment_id
WHERE d.company_id=@companyId ORDER BY d.last_ping_at_utc DESC NULLS LAST, d.device_code",
            c => c.Parameters.AddWithValue("@companyId", Cid(http)), ct);
        return Ok(new { items });
    }

    private static async Task<IResult> CreateDevice(HttpContext http, TemperatureDeviceRequest req, Database db, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(req.DeviceCode)) return Bad("Device code is required.");
        if (string.IsNullOrWhiteSpace(req.Name)) return Bad("Device name is required.");
        var companyId = Cid(http);
        var id = await db.InsertAsync(@"
INSERT INTO fleet_tms_temperature_devices (company_id, device_code, name, zone_id, shipment_id, vehicle_number, status, last_reported_temperature_celsius, battery_percent, last_ping_at_utc, notes, created_at_utc, updated_at_utc)
VALUES (@companyId, @code, @name, @zone, @shipment, @vehicle, @status, @temp, @battery, @ping, @notes, NOW(), NOW())",
            c =>
            {
                c.Parameters.AddWithValue("@companyId", companyId);
                c.Parameters.AddWithValue("@code", req.DeviceCode.Trim());
                c.Parameters.AddWithValue("@name", req.Name.Trim());
                c.Parameters.AddWithValue("@zone", Nl(req.ZoneId));
                c.Parameters.AddWithValue("@shipment", Nl(req.ShipmentId));
                c.Parameters.AddWithValue("@vehicle", req.VehicleNumber?.Trim() ?? "");
                c.Parameters.AddWithValue("@status", req.Status?.Trim() ?? "Active");
                c.Parameters.AddWithValue("@temp", req.LastReportedTemperatureCelsius ?? 0m);
                c.Parameters.AddWithValue("@battery", req.BatteryPercent ?? 0m);
                c.Parameters.AddWithValue("@ping", req.LastPingAtUtc ?? DateTime.UtcNow);
                c.Parameters.AddWithValue("@notes", req.Notes?.Trim() ?? "");
            }, ct);
        return Ok(await Row(db, "fleet_tms_temperature_devices", companyId, id, ct)!);
    }

    private static async Task<IResult> ShipmentReadings(HttpContext http, long shipmentId, Database db, CancellationToken ct)
    {
        var items = await db.QueryAsync(@"
SELECT r.id, r.device_id, d.device_code, r.zone_id, z.code zone_code, r.temperature_celsius, r.humidity_percent,
       r.latitude, r.longitude, r.source, r.status, r.notes, r.recorded_at_utc, r.created_at_utc
FROM fleet_tms_temperature_readings r
LEFT JOIN fleet_tms_temperature_devices d ON d.id=r.device_id
LEFT JOIN fleet_tms_temperature_zones z ON z.id=r.zone_id
WHERE r.company_id=@companyId AND r.shipment_id=@sid ORDER BY r.recorded_at_utc DESC",
            c => { c.Parameters.AddWithValue("@companyId", Cid(http)); c.Parameters.AddWithValue("@sid", shipmentId); }, ct);
        return Ok(new { items });
    }

    private static async Task<IResult> CreateReading(HttpContext http, TemperatureReadingRequest req, Database db, CancellationToken ct)
    {
        var companyId = Cid(http);
        var device = await Row(db, "fleet_tms_temperature_devices", companyId, req.DeviceId, ct);
        if (device is null) return NotFound("Temperature device not found for this tenant.");

        long? zoneId = req.ZoneId ?? (device["zoneId"] is null ? null : Convert.ToInt64(device["zoneId"]));
        Dictionary<string, object?>? zone = zoneId.HasValue ? await Row(db, "fleet_tms_temperature_zones", companyId, zoneId.Value, ct) : null;
        long? shipmentId = req.ShipmentId ?? (device["shipmentId"] is null ? null : Convert.ToInt64(device["shipmentId"]));

        var status = string.IsNullOrWhiteSpace(req.Status) ? "Normal" : req.Status.Trim();
        var isBreach = zone is not null && (req.TemperatureCelsius < Convert.ToDecimal(zone["minCelsius"]) || req.TemperatureCelsius > Convert.ToDecimal(zone["maxCelsius"]));
        if (isBreach) status = "Breach";

        var readingId = await db.InsertAsync(@"
INSERT INTO fleet_tms_temperature_readings (company_id, device_id, shipment_id, zone_id, temperature_celsius, humidity_percent, latitude, longitude, source, status, notes, recorded_at_utc, created_at_utc)
VALUES (@companyId, @device, @shipment, @zone, @temp, @humidity, @lat, @lng, @source, @status, @notes, NOW(), NOW())",
            c =>
            {
                c.Parameters.AddWithValue("@companyId", companyId);
                c.Parameters.AddWithValue("@device", req.DeviceId);
                c.Parameters.AddWithValue("@shipment", Nl(shipmentId));
                c.Parameters.AddWithValue("@zone", Nl(zoneId));
                c.Parameters.AddWithValue("@temp", req.TemperatureCelsius);
                c.Parameters.AddWithValue("@humidity", N(req.HumidityPercent));
                c.Parameters.AddWithValue("@lat", N(req.Latitude));
                c.Parameters.AddWithValue("@lng", N(req.Longitude));
                c.Parameters.AddWithValue("@source", string.IsNullOrWhiteSpace(req.Source) ? "Sensor" : req.Source.Trim());
                c.Parameters.AddWithValue("@status", status);
                c.Parameters.AddWithValue("@notes", req.Notes?.Trim() ?? "");
            }, ct);

        await db.ExecuteAsync(@"
UPDATE fleet_tms_temperature_devices SET last_reported_temperature_celsius=@temp,
    battery_percent=CASE WHEN battery_percent <= 1 THEN 98 ELSE battery_percent END,
    last_ping_at_utc=NOW(), shipment_id=COALESCE(@shipment, shipment_id), zone_id=COALESCE(@zone, zone_id), updated_at_utc=NOW()
WHERE id=@device AND company_id=@companyId",
            c => { c.Parameters.AddWithValue("@temp", req.TemperatureCelsius); c.Parameters.AddWithValue("@shipment", Nl(shipmentId)); c.Parameters.AddWithValue("@zone", Nl(zoneId)); c.Parameters.AddWithValue("@device", req.DeviceId); c.Parameters.AddWithValue("@companyId", companyId); }, ct);

        if (isBreach && zone is not null)
        {
            var min = Convert.ToDecimal(zone["minCelsius"]);
            var max = Convert.ToDecimal(zone["maxCelsius"]);
            await db.ExecuteAsync(@"
INSERT INTO fleet_tms_temperature_alerts (company_id, device_id, shipment_id, reading_id, alert_type, severity, status, threshold_min, threshold_max, measured_temperature, triggered_at_utc, notes)
VALUES (@companyId, @device, @shipment, @reading, 'TemperatureBreach', @severity, 'Open', @min, @max, @temp, NOW(), 'Breach auto-generated from live temperature reading.')",
                c =>
                {
                    c.Parameters.AddWithValue("@companyId", companyId);
                    c.Parameters.AddWithValue("@device", req.DeviceId);
                    c.Parameters.AddWithValue("@shipment", Nl(shipmentId));
                    c.Parameters.AddWithValue("@reading", readingId);
                    c.Parameters.AddWithValue("@severity", req.TemperatureCelsius > max + 2 ? "Critical" : "High");
                    c.Parameters.AddWithValue("@min", min);
                    c.Parameters.AddWithValue("@max", max);
                    c.Parameters.AddWithValue("@temp", req.TemperatureCelsius);
                }, ct);
        }

        return Ok(await Row(db, "fleet_tms_temperature_readings", companyId, readingId, ct)!);
    }

    private static async Task<IResult> ColdChainAlerts(HttpContext http, Database db, string? status, CancellationToken ct)
    {
        var companyId = Cid(http);
        var where = "WHERE a.company_id=@companyId" + (string.IsNullOrWhiteSpace(status) ? "" : " AND a.status=@status");
        var items = await db.QueryAsync($@"
SELECT a.id, a.device_id, d.device_code, a.shipment_id, s.shipment_number, a.reading_id, a.alert_type, a.severity, a.status,
       a.threshold_min, a.threshold_max, a.measured_temperature, a.triggered_at_utc, a.resolved_at_utc, a.resolved_by, a.resolution_notes, a.notes
FROM fleet_tms_temperature_alerts a
LEFT JOIN fleet_tms_temperature_devices d ON d.id=a.device_id
LEFT JOIN fleet_tms_shipments s ON s.id=a.shipment_id
{where} ORDER BY a.triggered_at_utc DESC",
            c => { c.Parameters.AddWithValue("@companyId", companyId); if (!string.IsNullOrWhiteSpace(status)) c.Parameters.AddWithValue("@status", status); }, ct);
        return Ok(new { items });
    }

    private static async Task<IResult> ResolveAlert(HttpContext http, long id, TemperatureAlertResolveRequest req, Database db, CancellationToken ct)
    {
        var companyId = Cid(http);
        var rows = await db.ExecuteAsync(@"
UPDATE fleet_tms_temperature_alerts SET status='Resolved', resolved_at_utc=NOW(), resolved_by=@actor, resolution_notes=@notes WHERE id=@id AND company_id=@companyId",
            c => { c.Parameters.AddWithValue("@actor", Actor(http)); c.Parameters.AddWithValue("@notes", req?.ResolutionNotes?.Trim() ?? "Resolved by operations."); c.Parameters.AddWithValue("@id", id); c.Parameters.AddWithValue("@companyId", companyId); }, ct);
        if (rows == 0) return NotFound("Temperature alert not found for this tenant.");
        return Ok(await Row(db, "fleet_tms_temperature_alerts", companyId, id, ct)!);
    }

    private static async Task<IResult> ColdChainReport(HttpContext http, long shipmentId, Database db, CancellationToken ct)
    {
        var companyId = Cid(http);
        var existing = await db.QuerySingleAsync("SELECT * FROM fleet_tms_cold_chain_reports WHERE company_id=@companyId AND shipment_id=@sid ORDER BY generated_at_utc DESC LIMIT 1",
            c => { c.Parameters.AddWithValue("@companyId", companyId); c.Parameters.AddWithValue("@sid", shipmentId); }, ct);
        if (existing is not null) return Ok(existing);

        var shipment = await Row(db, "fleet_tms_shipments", companyId, shipmentId, ct);
        if (shipment is null) return NotFound("Shipment not found for this tenant.");
        var readings = await db.QueryAsync("SELECT temperature_celsius, status FROM fleet_tms_temperature_readings WHERE company_id=@companyId AND shipment_id=@sid",
            c => { c.Parameters.AddWithValue("@companyId", companyId); c.Parameters.AddWithValue("@sid", shipmentId); }, ct);
        if (readings.Count == 0) return NotFound("No cold-chain readings found for this shipment.");

        var temps = readings.Select(r => Convert.ToDecimal(r["temperatureCelsius"])).ToList();
        var breachCount = readings.Count(r => string.Equals(r["status"]?.ToString(), "Breach", StringComparison.OrdinalIgnoreCase));
        var shipmentNumber = shipment["shipmentNumber"]?.ToString() ?? "";
        var summaryJson = JsonSerializer.Serialize(new { shipment = shipmentNumber, readings = readings.Count, breachCount });
        var id = await db.InsertAsync(@"
INSERT INTO fleet_tms_cold_chain_reports (company_id, shipment_id, shipment_number, generated_at_utc, compliance_percent, min_temperature_celsius, max_temperature_celsius, total_readings, breach_count, summary_json, notes)
VALUES (@companyId, @sid, @num, NOW(), @compliance, @min, @max, @total, @breach, @summary::jsonb, 'Generated on demand from live temperature readings.')",
            c =>
            {
                c.Parameters.AddWithValue("@companyId", companyId);
                c.Parameters.AddWithValue("@sid", shipmentId);
                c.Parameters.AddWithValue("@num", shipmentNumber);
                c.Parameters.AddWithValue("@compliance", Math.Round((1m - (breachCount / (decimal)readings.Count)) * 100m, 1));
                c.Parameters.AddWithValue("@min", temps.Min());
                c.Parameters.AddWithValue("@max", temps.Max());
                c.Parameters.AddWithValue("@total", readings.Count);
                c.Parameters.AddWithValue("@breach", breachCount);
                c.Parameters.AddWithValue("@summary", summaryJson);
            }, ct);
        return Ok(await Row(db, "fleet_tms_cold_chain_reports", companyId, id, ct)!);
    }

    // ── Assets ──────────────────────────────────────────────────────────────────

    private static async Task<IResult> AssetTypes(HttpContext http, Database db, CancellationToken ct)
        => Ok(new { items = await db.QueryAsync("SELECT * FROM fleet_tms_asset_types WHERE company_id=@companyId ORDER BY name", c => c.Parameters.AddWithValue("@companyId", Cid(http)), ct) });

    private static async Task<IResult> CreateAssetType(HttpContext http, AssetTypeRequest req, Database db, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(req.Code)) return Bad("Asset type code is required.");
        if (string.IsNullOrWhiteSpace(req.Name)) return Bad("Asset type name is required.");
        var companyId = Cid(http);
        var id = await db.InsertAsync(@"
INSERT INTO fleet_tms_asset_types (company_id, code, name, description, is_returnable, created_at_utc, updated_at_utc)
VALUES (@companyId, @code, @name, @desc, @returnable, NOW(), NOW())",
            c =>
            {
                c.Parameters.AddWithValue("@companyId", companyId);
                c.Parameters.AddWithValue("@code", req.Code.Trim());
                c.Parameters.AddWithValue("@name", req.Name.Trim());
                c.Parameters.AddWithValue("@desc", req.Description?.Trim() ?? "");
                c.Parameters.AddWithValue("@returnable", req.IsReturnable ?? true);
            }, ct);
        return Ok(await Row(db, "fleet_tms_asset_types", companyId, id, ct)!);
    }

    private static async Task<IResult> Assets(HttpContext http, Database db, CancellationToken ct)
    {
        var items = await db.QueryAsync(@"
SELECT a.id, a.asset_type_id, t.code asset_type_code, t.name asset_type_name, a.asset_tag, a.name, a.status,
       a.current_location, a.condition, a.is_returnable, a.quantity, a.unit_of_measure, a.notes, a.last_seen_at_utc, a.created_at_utc,
       (SELECT COUNT(*) FROM fleet_tms_asset_assignments aa WHERE aa.asset_id=a.id AND aa.company_id=a.company_id) assignment_count
FROM fleet_tms_assets a
LEFT JOIN fleet_tms_asset_types t ON t.id=a.asset_type_id
WHERE a.company_id=@companyId ORDER BY a.last_seen_at_utc DESC NULLS LAST, a.asset_tag",
            c => c.Parameters.AddWithValue("@companyId", Cid(http)), ct);
        return Ok(new { items });
    }

    private static async Task<IResult> AssetDetail(HttpContext http, long id, Database db, CancellationToken ct)
    {
        var companyId = Cid(http);
        var asset = await Row(db, "fleet_tms_assets", companyId, id, ct);
        if (asset is null) return NotFound();
        var assignments = await db.QueryAsync(@"
SELECT aa.id, aa.asset_id, aa.shipment_id, s.shipment_number, aa.carrier_id, c.name carrier_name,
       aa.assignee_type, aa.assignee_name, aa.quantity, aa.status, aa.assigned_at_utc, aa.released_at_utc, aa.notes
FROM fleet_tms_asset_assignments aa
LEFT JOIN fleet_tms_shipments s ON s.id=aa.shipment_id
LEFT JOIN carriers c ON c.id=aa.carrier_id
WHERE aa.company_id=@companyId AND aa.asset_id=@id ORDER BY aa.assigned_at_utc DESC",
            c => { c.Parameters.AddWithValue("@companyId", companyId); c.Parameters.AddWithValue("@id", id); }, ct);
        var events = await LoadAssetEvents(db, companyId, id, ct);
        return Ok(new { asset, assignments, events });
    }

    private static async Task<IResult> CreateAsset(HttpContext http, AssetRequest req, Database db, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(req.AssetTag)) return Bad("Asset tag is required.");
        if (string.IsNullOrWhiteSpace(req.Name)) return Bad("Asset name is required.");
        if (req.AssetTypeId == 0) return Bad("Asset type is required.");
        var companyId = Cid(http);
        if (await Row(db, "fleet_tms_asset_types", companyId, req.AssetTypeId, ct) is null) return NotFound("Asset type not found for this tenant.");
        var id = await db.InsertAsync(@"
INSERT INTO fleet_tms_assets (company_id, asset_type_id, asset_tag, name, status, current_location, condition, is_returnable, quantity, unit_of_measure, notes, last_seen_at_utc, created_at_utc, updated_at_utc)
VALUES (@companyId, @type, @tag, @name, @status, @loc, @condition, @returnable, @qty, @uom, @notes, @lastSeen, NOW(), NOW())",
            c => BindAsset(c, companyId, req), ct);
        return Ok(await Row(db, "fleet_tms_assets", companyId, id, ct)!);
    }

    private static void BindAsset(NpgsqlCommand c, long companyId, AssetRequest req)
    {
        c.Parameters.AddWithValue("@companyId", companyId);
        c.Parameters.AddWithValue("@type", req.AssetTypeId);
        c.Parameters.AddWithValue("@tag", req.AssetTag?.Trim() ?? "");
        c.Parameters.AddWithValue("@name", req.Name?.Trim() ?? "");
        c.Parameters.AddWithValue("@status", req.Status?.Trim() ?? "Available");
        c.Parameters.AddWithValue("@loc", req.CurrentLocation?.Trim() ?? "");
        c.Parameters.AddWithValue("@condition", req.Condition?.Trim() ?? "Good");
        c.Parameters.AddWithValue("@returnable", req.IsReturnable ?? true);
        c.Parameters.AddWithValue("@qty", req.Quantity ?? 1m);
        c.Parameters.AddWithValue("@uom", req.UnitOfMeasure?.Trim() ?? "Each");
        c.Parameters.AddWithValue("@notes", req.Notes?.Trim() ?? "");
        c.Parameters.AddWithValue("@lastSeen", Dt(req.LastSeenAtUtc));
    }

    private static async Task<IResult> UpdateAsset(HttpContext http, long id, AssetRequest req, Database db, CancellationToken ct)
    {
        var companyId = Cid(http);
        if (await Row(db, "fleet_tms_assets", companyId, id, ct) is null) return NotFound();
        if (req.AssetTypeId != 0 && await Row(db, "fleet_tms_asset_types", companyId, req.AssetTypeId, ct) is null) return NotFound("Asset type not found for this tenant.");
        await db.ExecuteAsync(@"
UPDATE fleet_tms_assets SET
  asset_type_id=CASE WHEN @type=0 THEN asset_type_id ELSE @type END,
  asset_tag=COALESCE(NULLIF(@tag,''), asset_tag), name=COALESCE(NULLIF(@name,''), name),
  status=COALESCE(NULLIF(@status,''), status), current_location=COALESCE(NULLIF(@loc,''), current_location),
  condition=COALESCE(NULLIF(@condition,''), condition), is_returnable=@returnable,
  quantity=@qty, unit_of_measure=COALESCE(NULLIF(@uom,''), unit_of_measure), notes=COALESCE(NULLIF(@notes,''), notes),
  last_seen_at_utc=COALESCE(@lastSeen, last_seen_at_utc), updated_at_utc=NOW()
WHERE id=@id AND company_id=@companyId",
            c => { BindAsset(c, companyId, req); c.Parameters.AddWithValue("@id", id); }, ct);
        return Ok(await Row(db, "fleet_tms_assets", companyId, id, ct)!);
    }

    private static async Task<IResult> AssignAsset(HttpContext http, long id, AssetAssignmentRequest req, Database db, CancellationToken ct)
    {
        var companyId = Cid(http);
        var asset = await Row(db, "fleet_tms_assets", companyId, id, ct);
        if (asset is null) return NotFound();
        var qty = req.Quantity ?? Convert.ToDecimal(asset["quantity"]);
        var assignId = await db.InsertAsync(@"
INSERT INTO fleet_tms_asset_assignments (company_id, asset_id, shipment_id, carrier_id, assignee_type, assignee_name, quantity, status, assigned_at_utc, released_at_utc, notes)
VALUES (@companyId, @asset, @shipment, @carrier, @atype, @aname, @qty, @status, NOW(), @released, @notes)",
            c =>
            {
                c.Parameters.AddWithValue("@companyId", companyId);
                c.Parameters.AddWithValue("@asset", id);
                c.Parameters.AddWithValue("@shipment", Nl(req.ShipmentId));
                c.Parameters.AddWithValue("@carrier", Nl(req.CarrierId));
                c.Parameters.AddWithValue("@atype", string.IsNullOrWhiteSpace(req.AssigneeType) ? (req.ShipmentId.HasValue ? "Shipment" : "Warehouse") : req.AssigneeType.Trim());
                c.Parameters.AddWithValue("@aname", req.AssigneeName?.Trim() ?? (req.ShipmentId.HasValue ? req.ShipmentId.Value.ToString() : "Warehouse"));
                c.Parameters.AddWithValue("@qty", qty);
                c.Parameters.AddWithValue("@status", req.Status?.Trim() ?? "Assigned");
                c.Parameters.AddWithValue("@released", Dt(req.ReleasedAtUtc));
                c.Parameters.AddWithValue("@notes", req.Notes?.Trim() ?? "");
            }, ct);
        await db.ExecuteAsync("UPDATE fleet_tms_assets SET status='Assigned', current_location=COALESCE(NULLIF(@loc,''), current_location), last_seen_at_utc=NOW(), updated_at_utc=NOW() WHERE id=@id AND company_id=@companyId",
            c => { c.Parameters.AddWithValue("@loc", req.CurrentLocation?.Trim() ?? ""); c.Parameters.AddWithValue("@id", id); c.Parameters.AddWithValue("@companyId", companyId); }, ct);
        await LogAssetEvent(db, companyId, id, "Assigned", qty, asset["currentLocation"]?.ToString() ?? "", Actor(http), req.Notes?.Trim() ?? "", ct);
        return Ok(await Row(db, "fleet_tms_asset_assignments", companyId, assignId, ct)!);
    }

    private static async Task<IResult> CheckInAsset(HttpContext http, long id, AssetMovementRequest req, Database db, CancellationToken ct)
    {
        var companyId = Cid(http);
        var asset = await Row(db, "fleet_tms_assets", companyId, id, ct);
        if (asset is null) return NotFound();
        await db.ExecuteAsync("UPDATE fleet_tms_assets SET status='Available', current_location=COALESCE(NULLIF(@loc,''), current_location), condition=COALESCE(NULLIF(@condition,''), condition), last_seen_at_utc=NOW(), updated_at_utc=NOW() WHERE id=@id AND company_id=@companyId",
            c => { c.Parameters.AddWithValue("@loc", req.Location?.Trim() ?? ""); c.Parameters.AddWithValue("@condition", req.Condition?.Trim() ?? ""); c.Parameters.AddWithValue("@id", id); c.Parameters.AddWithValue("@companyId", companyId); }, ct);
        await db.ExecuteAsync("UPDATE fleet_tms_asset_assignments SET status='Returned', released_at_utc=NOW(), notes=COALESCE(NULLIF(@notes,''), notes) WHERE id=(SELECT id FROM fleet_tms_asset_assignments WHERE company_id=@companyId AND asset_id=@id AND released_at_utc IS NULL ORDER BY assigned_at_utc DESC LIMIT 1)",
            c => { c.Parameters.AddWithValue("@notes", req.Notes?.Trim() ?? ""); c.Parameters.AddWithValue("@id", id); c.Parameters.AddWithValue("@companyId", companyId); }, ct);
        var asset2 = await Row(db, "fleet_tms_assets", companyId, id, ct)!;
        await LogAssetEvent(db, companyId, id, "CheckIn", Convert.ToDecimal(asset2!["quantity"]), asset2["currentLocation"]?.ToString() ?? "", Actor(http), req.Notes?.Trim() ?? "Asset checked back into inventory.", ct);
        return Ok(asset2);
    }

    private static async Task<IResult> CheckOutAsset(HttpContext http, long id, AssetMovementRequest req, Database db, CancellationToken ct)
    {
        var companyId = Cid(http);
        var asset = await Row(db, "fleet_tms_assets", companyId, id, ct);
        if (asset is null) return NotFound();
        var qty = req.Quantity ?? Convert.ToDecimal(asset["quantity"]);
        await db.ExecuteAsync("UPDATE fleet_tms_assets SET status='InUse', current_location=COALESCE(NULLIF(@loc,''), current_location), condition=COALESCE(NULLIF(@condition,''), condition), last_seen_at_utc=NOW(), updated_at_utc=NOW() WHERE id=@id AND company_id=@companyId",
            c => { c.Parameters.AddWithValue("@loc", req.Location?.Trim() ?? ""); c.Parameters.AddWithValue("@condition", req.Condition?.Trim() ?? ""); c.Parameters.AddWithValue("@id", id); c.Parameters.AddWithValue("@companyId", companyId); }, ct);
        var notes = req.Notes?.Trim() ?? "Asset checked out.";
        await db.ExecuteAsync(@"
INSERT INTO fleet_tms_asset_assignments (company_id, asset_id, shipment_id, carrier_id, assignee_type, assignee_name, quantity, status, assigned_at_utc, notes)
VALUES (@companyId, @asset, @shipment, @carrier, @atype, @aname, @qty, 'CheckedOut', NOW(), @notes)",
            c =>
            {
                c.Parameters.AddWithValue("@companyId", companyId);
                c.Parameters.AddWithValue("@asset", id);
                c.Parameters.AddWithValue("@shipment", Nl(req.ShipmentId));
                c.Parameters.AddWithValue("@carrier", Nl(req.CarrierId));
                c.Parameters.AddWithValue("@atype", string.IsNullOrWhiteSpace(req.AssigneeType) ? (req.ShipmentId.HasValue ? "Shipment" : "Dispatch") : req.AssigneeType.Trim());
                c.Parameters.AddWithValue("@aname", req.AssigneeName?.Trim() ?? (req.ShipmentId.HasValue ? req.ShipmentId.Value.ToString() : "Dispatch"));
                c.Parameters.AddWithValue("@qty", qty);
                c.Parameters.AddWithValue("@notes", notes);
            }, ct);
        await LogAssetEvent(db, companyId, id, "CheckOut", qty, req.Location?.Trim() ?? asset["currentLocation"]?.ToString() ?? "", Actor(http), notes, ct);
        return Ok(await Row(db, "fleet_tms_assets", companyId, id, ct)!);
    }

    private static async Task<IResult> AssetEvents(HttpContext http, long id, Database db, CancellationToken ct)
    {
        var companyId = Cid(http);
        if (await Row(db, "fleet_tms_assets", companyId, id, ct) is null) return NotFound();
        return Ok(new { items = await LoadAssetEvents(db, companyId, id, ct) });
    }

    private static async Task<IResult> ScanAsset(HttpContext http, AssetScanRequest req, Database db, CancellationToken ct)
    {
        var companyId = Cid(http);
        var isRfid = string.Equals(req.Kind, "RFID", StringComparison.OrdinalIgnoreCase) || !string.IsNullOrWhiteSpace(req.TagId);
        if (isRfid)
        {
            var id = await db.InsertAsync(@"
INSERT INTO fleet_tms_rfid_events (company_id, asset_id, shipment_id, tag_id, reader_id, event_type, status, recorded_at_utc, notes)
VALUES (@companyId, @asset, @shipment, @tag, @reader, @etype, @status, NOW(), @notes)",
                c =>
                {
                    c.Parameters.AddWithValue("@companyId", companyId);
                    c.Parameters.AddWithValue("@asset", Nl(req.AssetId));
                    c.Parameters.AddWithValue("@shipment", Nl(req.ShipmentId));
                    c.Parameters.AddWithValue("@tag", req.TagId?.Trim() ?? req.ScannedValue?.Trim() ?? "");
                    c.Parameters.AddWithValue("@reader", req.ReaderId?.Trim() ?? "RFID-GATE");
                    c.Parameters.AddWithValue("@etype", req.EventType?.Trim() ?? "Read");
                    c.Parameters.AddWithValue("@status", req.Status?.Trim() ?? "Captured");
                    c.Parameters.AddWithValue("@notes", req.Notes?.Trim() ?? "");
                }, ct);
            return Ok(await Row(db, "fleet_tms_rfid_events", companyId, id, ct)!);
        }
        var bid = await db.InsertAsync(@"
INSERT INTO fleet_tms_barcode_scan_events (company_id, asset_id, shipment_id, scanned_value, scanner_id, event_type, status, recorded_at_utc, notes)
VALUES (@companyId, @asset, @shipment, @value, @scanner, @etype, @status, NOW(), @notes)",
            c =>
            {
                c.Parameters.AddWithValue("@companyId", companyId);
                c.Parameters.AddWithValue("@asset", Nl(req.AssetId));
                c.Parameters.AddWithValue("@shipment", Nl(req.ShipmentId));
                c.Parameters.AddWithValue("@value", req.ScannedValue?.Trim() ?? "");
                c.Parameters.AddWithValue("@scanner", req.ScannerId?.Trim() ?? "BARCODE-SCAN");
                c.Parameters.AddWithValue("@etype", req.EventType?.Trim() ?? "Scan");
                c.Parameters.AddWithValue("@status", req.Status?.Trim() ?? "Captured");
                c.Parameters.AddWithValue("@notes", req.Notes?.Trim() ?? "");
            }, ct);
        return Ok(await Row(db, "fleet_tms_barcode_scan_events", companyId, bid, ct)!);
    }

    private static async Task<List<Dictionary<string, object?>>> LoadAssetEvents(Database db, long companyId, long assetId, CancellationToken ct)
        => await db.QueryAsync(@"
SELECT id, type, event_type, quantity, location, actor_name, occurred_at_utc, notes FROM (
  SELECT id, 'AssetEvent' type, event_type, quantity, location, actor_name, occurred_at_utc, notes
    FROM fleet_tms_asset_events WHERE company_id=@companyId AND asset_id=@id
  UNION ALL
  SELECT id, 'BarcodeScan' type, event_type, 1 quantity, scanner_id location, scanner_id actor_name, recorded_at_utc occurred_at_utc, notes
    FROM fleet_tms_barcode_scan_events WHERE company_id=@companyId AND asset_id=@id
  UNION ALL
  SELECT id, 'RfidEvent' type, event_type, 1 quantity, reader_id location, reader_id actor_name, recorded_at_utc occurred_at_utc, notes
    FROM fleet_tms_rfid_events WHERE company_id=@companyId AND asset_id=@id
  UNION ALL
  SELECT id, 'Assignment' type, status event_type, quantity, assignee_name location, assignee_type actor_name, assigned_at_utc occurred_at_utc, notes
    FROM fleet_tms_asset_assignments WHERE company_id=@companyId AND asset_id=@id
) e ORDER BY occurred_at_utc DESC",
            c => { c.Parameters.AddWithValue("@companyId", companyId); c.Parameters.AddWithValue("@id", assetId); }, ct);

    private static async Task LogAssetEvent(Database db, long companyId, long assetId, string type, decimal qty, string location, string actor, string notes, CancellationToken ct)
        => await db.ExecuteAsync(@"
INSERT INTO fleet_tms_asset_events (company_id, asset_id, event_type, quantity, location, actor_name, occurred_at_utc, notes)
VALUES (@companyId, @asset, @type, @qty, @loc, @actor, NOW(), @notes)",
            c => { c.Parameters.AddWithValue("@companyId", companyId); c.Parameters.AddWithValue("@asset", assetId); c.Parameters.AddWithValue("@type", type); c.Parameters.AddWithValue("@qty", qty); c.Parameters.AddWithValue("@loc", location); c.Parameters.AddWithValue("@actor", actor); c.Parameters.AddWithValue("@notes", notes); }, ct);

    // ── Saudi readiness / compliance ────────────────────────────────────────────

    private static async Task<IResult> SaudiRegions(Database db, CancellationToken ct)
    {
        var rows = await db.QueryAsync("SELECT id, code, name_en, name_ar, country_code, is_gcc_ready, cities_json FROM fleet_tms_saudi_regions ORDER BY sort_order, name_en", ct: ct);
        var items = rows.Select(r => new
        {
            id = r["id"],
            code = r["code"],
            nameEn = r["nameEn"],
            nameAr = r["nameAr"],
            countryCode = r["countryCode"],
            isGccReady = r["isGccReady"],
            cities = ParseCities(r["citiesJson"]?.ToString()),
        });
        return Ok(new { items });
    }

    private static async Task<IResult> ComplianceDocuments(HttpContext http, Database db, string? kind, string? subjectType, CancellationToken ct)
    {
        var companyId = Cid(http);
        var where = "WHERE company_id=@companyId"
            + (string.IsNullOrWhiteSpace(kind) ? "" : " AND kind=@kind")
            + (string.IsNullOrWhiteSpace(subjectType) ? "" : " AND subject_type=@subjectType");
        var items = await db.QueryAsync($"SELECT * FROM fleet_tms_readiness_documents {where} ORDER BY gregorian_expiry_date DESC NULLS LAST, subject_name",
            c =>
            {
                c.Parameters.AddWithValue("@companyId", companyId);
                if (!string.IsNullOrWhiteSpace(kind)) c.Parameters.AddWithValue("@kind", kind);
                if (!string.IsNullOrWhiteSpace(subjectType)) c.Parameters.AddWithValue("@subjectType", subjectType);
            }, ct);
        return Ok(new { items });
    }

    private static async Task<IResult> CreateComplianceDocument(HttpContext http, FleetReadinessDocumentRequest req, Database db, CancellationToken ct)
    {
        var err = ValidateDoc(req);
        if (err is not null) return Bad(err);
        var companyId = Cid(http);
        var id = await db.InsertAsync(@"
INSERT INTO fleet_tms_readiness_documents
 (company_id, kind, subject_type, subject_id, subject_name, document_type, document_number, transport_document_no, permit_no,
  vat_number, commercial_registration_no, country_code, national_address_building_no, national_address_additional_no,
  district, city, region, postal_code, document_status, expiry_status, issue_date, hijri_expiry_date, gregorian_expiry_date, notes, created_at_utc, updated_at_utc)
VALUES (@companyId, @kind, @subjectType, @subjectId, @subjectName, @docType, @docNumber, @transportDoc, @permit,
  @vat, @cr, @country, @building, @additional, @district, @city, @region, @postal, @docStatus, @expiryStatus,
  @issue, @hijri, @gregorian, @notes, NOW(), NOW())",
            c => BindDoc(c, companyId, req), ct);
        return Ok(await Row(db, "fleet_tms_readiness_documents", companyId, id, ct)!);
    }

    private static async Task<IResult> UpdateComplianceDocument(HttpContext http, long id, FleetReadinessDocumentRequest req, Database db, CancellationToken ct)
    {
        var err = ValidateDoc(req);
        if (err is not null) return Bad(err);
        var companyId = Cid(http);
        if (await Row(db, "fleet_tms_readiness_documents", companyId, id, ct) is null) return NotFound("Compliance document not found for this tenant.");
        await db.ExecuteAsync(@"
UPDATE fleet_tms_readiness_documents SET kind=@kind, subject_type=@subjectType, subject_id=@subjectId, subject_name=@subjectName,
  document_type=@docType, document_number=@docNumber, transport_document_no=@transportDoc, permit_no=@permit,
  vat_number=@vat, commercial_registration_no=@cr, country_code=@country, national_address_building_no=@building,
  national_address_additional_no=@additional, district=@district, city=@city, region=@region, postal_code=@postal,
  document_status=@docStatus, expiry_status=@expiryStatus, issue_date=@issue, hijri_expiry_date=@hijri,
  gregorian_expiry_date=@gregorian, notes=@notes, updated_at_utc=NOW()
WHERE id=@id AND company_id=@companyId",
            c => { BindDoc(c, companyId, req); c.Parameters.AddWithValue("@id", id); }, ct);
        return Ok(await Row(db, "fleet_tms_readiness_documents", companyId, id, ct)!);
    }

    private static void BindDoc(NpgsqlCommand c, long companyId, FleetReadinessDocumentRequest req)
    {
        c.Parameters.AddWithValue("@companyId", companyId);
        c.Parameters.AddWithValue("@kind", req.Kind.Trim());
        c.Parameters.AddWithValue("@subjectType", req.SubjectType.Trim());
        c.Parameters.AddWithValue("@subjectId", req.SubjectId?.Trim() ?? "");
        c.Parameters.AddWithValue("@subjectName", req.SubjectName.Trim());
        c.Parameters.AddWithValue("@docType", req.DocumentType.Trim());
        c.Parameters.AddWithValue("@docNumber", req.DocumentNumber?.Trim() ?? "");
        c.Parameters.AddWithValue("@transportDoc", req.TransportDocumentNo?.Trim() ?? "");
        c.Parameters.AddWithValue("@permit", req.PermitNo?.Trim() ?? "");
        c.Parameters.AddWithValue("@vat", req.VATNumber?.Trim() ?? "");
        c.Parameters.AddWithValue("@cr", req.CommercialRegistrationNo?.Trim() ?? "");
        c.Parameters.AddWithValue("@country", req.CountryCode?.Trim() ?? "SA");
        c.Parameters.AddWithValue("@building", req.NationalAddressBuildingNo?.Trim() ?? "");
        c.Parameters.AddWithValue("@additional", req.NationalAddressAdditionalNo?.Trim() ?? "");
        c.Parameters.AddWithValue("@district", req.District?.Trim() ?? "");
        c.Parameters.AddWithValue("@city", req.City?.Trim() ?? "");
        c.Parameters.AddWithValue("@region", req.Region?.Trim() ?? "");
        c.Parameters.AddWithValue("@postal", req.PostalCode?.Trim() ?? "");
        c.Parameters.AddWithValue("@docStatus", req.DocumentStatus?.Trim() ?? "Active");
        c.Parameters.AddWithValue("@expiryStatus", ComputeExpiryStatus(req.GregorianExpiryDate, req.DocumentStatus));
        c.Parameters.AddWithValue("@issue", Dte(req.IssueDate));
        c.Parameters.AddWithValue("@hijri", Dte(req.HijriExpiryDate));
        c.Parameters.AddWithValue("@gregorian", Dte(req.GregorianExpiryDate));
        c.Parameters.AddWithValue("@notes", req.Notes?.Trim() ?? "");
    }

    private static async Task<IResult> ComplianceExpiries(HttpContext http, Database db, CancellationToken ct)
    {
        var companyId = Cid(http);
        var docs = await db.QueryAsync("SELECT * FROM fleet_tms_readiness_documents WHERE company_id=@companyId",
            c => c.Parameters.AddWithValue("@companyId", companyId), ct);
        var today = DateOnly.FromDateTime(DateTime.UtcNow.Date);
        var rows = docs.Select(d =>
        {
            var expiry = (d["gregorianExpiryDate"] ?? d["hijriExpiryDate"]) as DateTime?;
            var days = expiry.HasValue ? (int)(expiry.Value.Date - DateTime.UtcNow.Date).TotalDays : int.MaxValue;
            return new
            {
                id = d["id"],
                kind = d["kind"],
                subjectType = d["subjectType"],
                subjectName = d["subjectName"],
                documentType = d["documentType"],
                documentNumber = d["documentNumber"],
                documentStatus = d["documentStatus"],
                expiryStatus = d["expiryStatus"],
                countryCode = d["countryCode"],
                gregorianExpiryDate = d["gregorianExpiryDate"],
                hijriExpiryDate = d["hijriExpiryDate"],
                daysRemaining = days,
                notes = d["notes"],
            };
        }).Where(x => x.daysRemaining <= ExpiryWindowDays).OrderBy(x => x.daysRemaining).ToList();

        return Ok(new
        {
            generatedAtUtc = DateTime.UtcNow,
            items = rows,
            summary = new
            {
                totalDocuments = docs.Count,
                expiringSoon = rows.Count(x => x.daysRemaining >= 0),
                expired = rows.Count(x => x.daysRemaining < 0),
                healthy = docs.Count - rows.Count,
                windowDays = ExpiryWindowDays,
                today,
            }
        });
    }

    private static async Task<IResult> VatInvoiceReady(HttpContext http, Database db, CancellationToken ct)
    {
        var companyId = Cid(http);
        void B(NpgsqlCommand c) => c.Parameters.AddWithValue("@companyId", companyId);
        var readyShipments = await db.QueryAsync(@"
SELECT id, shipment_number, customer_name, status, customer_vat_number, customer_commercial_registration_no,
       invoice_ready_at_utc, invoice_readiness_notes, origin, destination, carrier_name, route_code
FROM fleet_tms_shipments
WHERE company_id=@companyId AND is_invoice_ready AND customer_vat_number <> '' AND customer_commercial_registration_no <> ''
ORDER BY invoice_ready_at_utc DESC NULLS LAST, shipment_number LIMIT 10", B, ct);
        var blockedShipments = await db.QueryAsync(@"
SELECT id, shipment_number, customer_name, status, invoice_readiness_notes, origin, destination, carrier_name, route_code
FROM fleet_tms_shipments
WHERE company_id=@companyId AND (NOT is_invoice_ready OR customer_vat_number = '' OR customer_commercial_registration_no = '')
ORDER BY created_at_utc DESC LIMIT 10", B, ct);

        var readyCount = readyShipments.Count;
        var blockedCount = blockedShipments.Count;
        var readinessPercent = readyCount + blockedCount == 0 ? 0m : Math.Round(readyCount / (decimal)(readyCount + blockedCount) * 100m, 1);
        // OpsTrax has no `branches` table and the existing `carriers` table carries no
        // Saudi VAT/CR columns, so these readiness counts are reported as 0 here.
        return Ok(new
        {
            generatedAtUtc = DateTime.UtcNow,
            summary = new { readyCount, blockedCount, readinessPercent, carrierReady = 0, branchReady = 0 },
            readyShipments,
            blockedShipments,
        });
    }

    private static string? ValidateDoc(FleetReadinessDocumentRequest req)
    {
        if (string.IsNullOrWhiteSpace(req.Kind)) return "Document kind is required.";
        if (string.IsNullOrWhiteSpace(req.SubjectType)) return "Subject type is required.";
        if (string.IsNullOrWhiteSpace(req.SubjectName)) return "Subject name is required.";
        if (string.IsNullOrWhiteSpace(req.DocumentType)) return "Document type is required.";
        if (req.RequiresExpiry && !req.GregorianExpiryDate.HasValue && !req.HijriExpiryDate.HasValue) return "Expiry date is required for readiness documents.";
        return null;
    }

    private static string ComputeExpiryStatus(DateOnly? gregorianExpiryDate, string? documentStatus)
    {
        if (!string.IsNullOrWhiteSpace(documentStatus) && !string.Equals(documentStatus, "Active", StringComparison.OrdinalIgnoreCase))
            return documentStatus.Trim();
        if (!gregorianExpiryDate.HasValue) return "Healthy";
        var days = (gregorianExpiryDate.Value.ToDateTime(TimeOnly.MinValue).Date - DateTime.UtcNow.Date).Days;
        if (days < 0) return "Expired";
        if (days <= ExpiryWindowDays) return "ExpiringSoon";
        return "Healthy";
    }

    private static IReadOnlyCollection<string> ParseCities(string? citiesJson)
    {
        if (string.IsNullOrWhiteSpace(citiesJson)) return [];
        try { return JsonSerializer.Deserialize<string[]>(citiesJson) ?? []; }
        catch { return []; }
    }
}

// ── Request DTOs (camelCase JSON binds via default web serializer) ──
public record TemperatureDeviceRequest(string? DeviceCode, string? Name, long? ZoneId, long? ShipmentId, string? VehicleNumber, string? Status, decimal? LastReportedTemperatureCelsius, decimal? BatteryPercent, DateTime? LastPingAtUtc, string? Notes);
public record TemperatureReadingRequest(long DeviceId, long? ShipmentId, long? ZoneId, decimal TemperatureCelsius, decimal? HumidityPercent, decimal? Latitude, decimal? Longitude, string? Source, string? Status, string? Notes);
public record TemperatureAlertResolveRequest(string? ResolutionNotes);
public record AssetRequest(long AssetTypeId, string? AssetTag, string? Name, string? Status, string? CurrentLocation, string? Condition, bool? IsReturnable, decimal? Quantity, string? UnitOfMeasure, string? Notes, DateTime? LastSeenAtUtc);
public record AssetTypeRequest(string? Code, string? Name, string? Description, bool? IsReturnable);
public record AssetAssignmentRequest(long? ShipmentId, long? CarrierId, string? AssigneeType, string? AssigneeName, decimal? Quantity, string? Status, string? CurrentLocation, DateTime? ReleasedAtUtc, string? Notes);
public record AssetMovementRequest(string? Location, string? Condition, string? Notes, long? ShipmentId, long? CarrierId, string? AssigneeType, string? AssigneeName, decimal? Quantity);
public record AssetScanRequest(string? Kind, long? AssetId, long? ShipmentId, string? ScannedValue, string? TagId, string? ScannerId, string? ReaderId, string? EventType, string? Status, string? Notes);
public record FleetReadinessDocumentRequest(string Kind, string SubjectType, string? SubjectId, string SubjectName, string DocumentType, string? DocumentNumber, string? TransportDocumentNo, string? PermitNo, string? VATNumber, string? CommercialRegistrationNo, string? CountryCode, string? NationalAddressBuildingNo, string? NationalAddressAdditionalNo, string? District, string? City, string? Region, string? PostalCode, string? DocumentStatus, DateOnly? IssueDate, DateOnly? HijriExpiryDate, DateOnly? GregorianExpiryDate, string? Notes, bool RequiresExpiry = true);
