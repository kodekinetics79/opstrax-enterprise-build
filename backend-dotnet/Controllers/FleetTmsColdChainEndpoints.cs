using System.Globalization;
using System.Text.Json;
using Npgsql;
using Opstrax.Api.Data;
using Opstrax.Api.DTOs;
using Opstrax.Api.Services;

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
        Guard(app.MapGet("/api/fleet-tms/cold-chain/summary", ColdChainSummary), "fleet:view");
        Guard(app.MapGet("/api/fleet-tms/cold-chain/devices", ColdChainDevices), "fleet:view");
        Guard(app.MapPost("/api/fleet-tms/cold-chain/devices", CreateDevice), "fleet:manage");
        Guard(app.MapGet("/api/fleet-tms/cold-chain/policies", ColdChainPolicies), "fleet:view");
        Guard(app.MapPost("/api/fleet-tms/cold-chain/policies", UpsertColdChainPolicy), "fleet:manage");
        Guard(app.MapGet("/api/fleet-tms/cold-chain/events", ColdChainEvents), "fleet:view");
        Guard(app.MapGet("/api/fleet-tms/cold-chain/shipments/{shipmentId:long}/readings", ShipmentReadings), "fleet:view");
        Guard(app.MapPost("/api/fleet-tms/cold-chain/readings", CreateReading), "fleet:manage");
        Guard(app.MapGet("/api/fleet-tms/cold-chain/alerts", ColdChainAlerts), "fleet:view");
        Guard(app.MapPost("/api/fleet-tms/cold-chain/alerts/{id:long}/resolve", ResolveAlert), "fleet:manage");
        Guard(app.MapGet("/api/fleet-tms/cold-chain/reports/{shipmentId:long}", ColdChainReport), "fleet:view");

        // Assets
        Guard(app.MapGet("/api/fleet-tms/assets/types", AssetTypes), "fleet:view");
        Guard(app.MapPost("/api/fleet-tms/assets/types", CreateAssetType), "fleet:manage");
        Guard(app.MapGet("/api/fleet-tms/assets", Assets), "fleet:view");
        Guard(app.MapGet("/api/fleet-tms/assets/export", AssetsExport), "fleet:manage");
        Guard(app.MapGet("/api/fleet-tms/assets/import-template", AssetsImportTemplate), "fleet:view");
        Guard(app.MapPost("/api/fleet-tms/assets/import-preview", AssetsImportPreview), "fleet:manage");
        Guard(app.MapPost("/api/fleet-tms/assets/import-commit", AssetsImportCommit), "fleet:manage");
        Guard(app.MapGet("/api/fleet-tms/assets/{id:long}", AssetDetail), "fleet:view");
        Guard(app.MapPost("/api/fleet-tms/assets", CreateAsset), "fleet:manage");
        Guard(app.MapPut("/api/fleet-tms/assets/{id:long}", UpdateAsset), "fleet:manage");
        Guard(app.MapPost("/api/fleet-tms/assets/{id:long}/assign", AssignAsset), "fleet:manage");
        Guard(app.MapPost("/api/fleet-tms/assets/{id:long}/check-in", CheckInAsset), "fleet:manage");
        Guard(app.MapPost("/api/fleet-tms/assets/{id:long}/check-out", CheckOutAsset), "fleet:manage");
        Guard(app.MapGet("/api/fleet-tms/assets/{id:long}/events", AssetEvents), "fleet:view");
        Guard(app.MapPost("/api/fleet-tms/assets/scan", ScanAsset), "fleet:manage");

        // Saudi readiness / compliance
        Guard(app.MapGet("/api/fleet-tms/saudi/regions", SaudiRegions), "compliance:view");
        Guard(app.MapGet("/api/fleet-tms/compliance/documents", ComplianceDocuments), "compliance:view");
        Guard(app.MapPost("/api/fleet-tms/compliance/documents", CreateComplianceDocument), "compliance:manage");
        Guard(app.MapPut("/api/fleet-tms/compliance/documents/{id:long}", UpdateComplianceDocument), "compliance:manage");
        Guard(app.MapGet("/api/fleet-tms/compliance/expiries", ComplianceExpiries), "compliance:view");
        Guard(app.MapGet("/api/fleet-tms/vat/invoice-ready", VatInvoiceReady), "compliance:view");
    }

    private static RouteHandlerBuilder Guard(RouteHandlerBuilder route, string permission)
        => route.AddEndpointFilter(async (invocation, next) =>
        {
            var denied = EndpointMappings.RequirePermission(invocation.HttpContext, permission);
            if (denied is not null) return denied;
            return await next(invocation);
        });

    private const int ExpiryWindowDays = 30;

    private static long Cid(HttpContext http) => EndpointMappings.GetCompanyId(http);
    private static long? Bid(HttpContext http) => EndpointMappings.GetBranchId(http);
    private static string BranchScope(HttpContext http, string alias = "")
        => Bid(http) is null ? "" : $" AND {alias}branch_id=@branchId";
    private static string SharedConfigScope(HttpContext http, string alias = "")
        => Bid(http) is null ? "" : $" AND ({alias}branch_id=@branchId OR {alias}branch_id IS NULL)";
    private static void BindBranch(NpgsqlCommand command, HttpContext http)
    {
        if (Bid(http) is { } branchId) command.Parameters.AddWithValue("@branchId", branchId);
    }
    private static string Actor(HttpContext http)
        => http.Items.TryGetValue(EndpointMappings.AuthUserIdItemKey, out var u) && u is not null ? $"user:{u}" : "system";
    private static IResult Ok<T>(T data) => Results.Ok(ApiResponse<object>.Ok(data!));
    private static IResult NotFound(string m = "Not found") => Results.NotFound(ApiResponse<object>.Fail(m));
    private static IResult Bad(string m) => Results.BadRequest(ApiResponse<object>.Fail(m));
    private static IResult Conflict(string m) => Results.Conflict(ApiResponse<object>.Fail(m));
    private static async Task<IResult?> RequireSaudiPack(HttpContext http, Database db, CancellationToken ct)
    {
        var decision = await new EntitlementService(db).CheckMarketPackAsync(Cid(http), MarketPackSchemaService.Packs.SaudiGcc, ct);
        return decision.Allowed ? null : Results.Json(ApiResponse<object>.Fail("Feature not entitled", decision.Reason ?? MarketPackSchemaService.Packs.SaudiGcc), statusCode: StatusCodes.Status403Forbidden);
    }
    private static object N(decimal? v) => (object?)v ?? DBNull.Value;
    private static object Nl(long? v) => (object?)v ?? DBNull.Value;
    private static object Dt(DateTime? v) => (object?)v ?? DBNull.Value;
    private static object Dte(DateOnly? v) => v.HasValue ? v.Value.ToDateTime(TimeOnly.MinValue) : DBNull.Value;
    private static string? GetText(Dictionary<string, object?> body, string key)
    {
        if (!body.TryGetValue(key, out var value) || value is null) return null;
        if (value is System.Text.Json.JsonElement je)
        {
            return je.ValueKind switch
            {
                System.Text.Json.JsonValueKind.String => je.GetString(),
                System.Text.Json.JsonValueKind.Number => je.ToString(),
                System.Text.Json.JsonValueKind.True => "true",
                System.Text.Json.JsonValueKind.False => "false",
                System.Text.Json.JsonValueKind.Null or System.Text.Json.JsonValueKind.Undefined => null,
                _ => je.ToString(),
            };
        }

        var text = value.ToString();
        return string.IsNullOrWhiteSpace(text) ? null : text;
    }

    private static decimal? GetDecimal(Dictionary<string, object?> body, string key)
    {
        if (!body.TryGetValue(key, out var value) || value is null) return null;
        if (value is System.Text.Json.JsonElement je)
        {
            return je.ValueKind switch
            {
                System.Text.Json.JsonValueKind.Number when je.TryGetDecimal(out var number) => number,
                System.Text.Json.JsonValueKind.String when decimal.TryParse(je.GetString(), NumberStyles.Any, CultureInfo.InvariantCulture, out var parsed) => parsed,
                _ => null,
            };
        }

        return decimal.TryParse(value.ToString(), NumberStyles.Any, CultureInfo.InvariantCulture, out var fallback) ? fallback : null;
    }

    private static bool GetBool(Dictionary<string, object?> body, string key, bool fallback = false)
    {
        if (!body.TryGetValue(key, out var value) || value is null) return fallback;
        if (value is System.Text.Json.JsonElement je)
        {
            return je.ValueKind switch
            {
                System.Text.Json.JsonValueKind.True => true,
                System.Text.Json.JsonValueKind.False => false,
                System.Text.Json.JsonValueKind.String when bool.TryParse(je.GetString(), out var parsed) => parsed,
                _ => fallback,
            };
        }

        return bool.TryParse(value.ToString(), out var parsedFallback) ? parsedFallback : fallback;
    }

    private static async Task<Dictionary<string, object?>?> Row(Database db, string table, long companyId, long id, CancellationToken ct)
        => await db.QuerySingleAsync($"SELECT * FROM {table} WHERE id=@id AND company_id=@companyId",
            c => { c.Parameters.AddWithValue("@id", id); c.Parameters.AddWithValue("@companyId", companyId); }, ct);

    private static async Task<Dictionary<string, object?>?> OwnedRow(Database db, HttpContext http, string table, long id, CancellationToken ct)
        => await db.QuerySingleAsync($"SELECT * FROM {table} WHERE id=@id AND company_id=@companyId" + BranchScope(http),
            c => { c.Parameters.AddWithValue("@id", id); c.Parameters.AddWithValue("@companyId", Cid(http)); BindBranch(c, http); }, ct);

    private static async Task<Dictionary<string, object?>?> SharedConfigRow(Database db, HttpContext http, string table, long id, CancellationToken ct)
        => await db.QuerySingleAsync($"SELECT * FROM {table} WHERE id=@id AND company_id=@companyId" + SharedConfigScope(http),
            c => { c.Parameters.AddWithValue("@id", id); c.Parameters.AddWithValue("@companyId", Cid(http)); BindBranch(c, http); }, ct);

    // ── Cold chain ──────────────────────────────────────────────────────────────

    private static async Task<IResult> ColdChainSummary(HttpContext http, Database db, FleetTmsColdChainFoundationService foundation, CancellationToken ct)
    {
        var denied = EndpointMappings.RequirePermission(http, "fleet:view");
        if (denied is not null) return denied;
        var companyId = Cid(http);
        void B(NpgsqlCommand c) { c.Parameters.AddWithValue("@companyId", companyId); BindBranch(c, http); }
        var totalReadings = await db.ScalarLongAsync("SELECT COUNT(*) FROM fleet_tms_temperature_readings WHERE company_id=@companyId" + BranchScope(http), B, ct);
        var breachReadings = await db.ScalarLongAsync("SELECT COUNT(*) FROM fleet_tms_temperature_readings WHERE company_id=@companyId AND status='Breach'" + BranchScope(http), B, ct);
        var policyCount = await db.ScalarLongAsync("SELECT COUNT(*) FROM fleet_tms_cold_chain_policies WHERE company_id=@companyId" + SharedConfigScope(http), B, ct);
        var eventLogCount = await db.ScalarLongAsync("SELECT COUNT(*) FROM fleet_tms_cold_chain_event_log WHERE company_id=@companyId" + BranchScope(http), B, ct);
        var summary = new
        {
            activeDevices = await db.ScalarLongAsync("SELECT COUNT(*) FROM fleet_tms_temperature_devices WHERE company_id=@companyId AND status='Active'" + BranchScope(http), B, ct),
            readingsToday = await db.ScalarLongAsync("SELECT COUNT(*) FROM fleet_tms_temperature_readings WHERE company_id=@companyId AND recorded_at_utc >= date_trunc('day', NOW())" + BranchScope(http), B, ct),
            openAlerts = await db.ScalarLongAsync("SELECT COUNT(*) FROM fleet_tms_temperature_alerts WHERE company_id=@companyId AND status IN ('Open','InReview')" + BranchScope(http), B, ct),
            policyCount,
            eventLogCount,
            totalReadings,
            breachReadings,
            avgTemperatureCelsius = Math.Round(await db.ScalarDecimalAsync("SELECT COALESCE(AVG(temperature_celsius),0) FROM fleet_tms_temperature_readings WHERE company_id=@companyId" + BranchScope(http), B, ct) ?? 0m, 1),
            compliancePercent = totalReadings == 0 ? 0m : Math.Round((1m - (breachReadings / (decimal)totalReadings)) * 100m, 1),
        };
        var zones = await db.QueryAsync("SELECT id, code, name, min_celsius, max_celsius, color, is_active, notes FROM fleet_tms_temperature_zones WHERE company_id=@companyId" + SharedConfigScope(http) + " ORDER BY name", B, ct);
        var devices = await db.QueryAsync("SELECT id, device_code, name, vehicle_number, status, last_reported_temperature_celsius, battery_percent, last_ping_at_utc, notes FROM fleet_tms_temperature_devices WHERE company_id=@companyId" + BranchScope(http) + " ORDER BY last_ping_at_utc DESC NULLS LAST LIMIT 6", B, ct);
        var alerts = await db.QueryAsync("SELECT id, device_id, shipment_id, reading_id, alert_type, severity, status, measured_temperature, threshold_min, threshold_max, measured_humidity, humidity_threshold_min, humidity_threshold_max, triggered_at_utc, resolution_notes, source_channel, client_generated_id, correlation_id, causation_id, metadata_json, applied_policy_code, applied_policy_scope FROM fleet_tms_temperature_alerts WHERE company_id=@companyId AND status <> 'Resolved'" + BranchScope(http) + " ORDER BY triggered_at_utc DESC LIMIT 6", B, ct);
        var reports = await db.QueryAsync("SELECT id, shipment_id, shipment_number, generated_at_utc, compliance_percent, min_temperature_celsius, max_temperature_celsius, total_readings, breach_count, summary_json, notes, source_channel, client_generated_id, correlation_id, causation_id, metadata_json FROM fleet_tms_cold_chain_reports WHERE company_id=@companyId" + BranchScope(http) + " ORDER BY generated_at_utc DESC LIMIT 6", B, ct);
        var policies = await foundation.ListPoliciesAsync(companyId, Bid(http), ct);
        return Ok(new { generatedAtUtc = DateTime.UtcNow, summary, zones, devices, alerts, reports, policies });
    }

    private static async Task<IResult> ColdChainPolicies(HttpContext http, FleetTmsColdChainFoundationService foundation, CancellationToken ct)
    {
        var denied = EndpointMappings.RequirePermission(http, "fleet:view");
        if (denied is not null) return denied;
        var policies = await foundation.ListPoliciesAsync(Cid(http), Bid(http), ct);
        return Ok(new { items = policies });
    }

    private static async Task<IResult> UpsertColdChainPolicy(HttpContext http, Dictionary<string, object?> body, FleetTmsColdChainFoundationService foundation, CancellationToken ct)
    {
        var denied = EndpointMappings.RequirePermission(http, "fleet:manage");
        if (denied is not null) return denied;
        if (ValidatePolicyRequest(body) is { } invalid) return Bad(invalid);

        var policyCode = GetText(body, "policyCode") ?? $"CCP-{DateTimeOffset.UtcNow.ToUnixTimeSeconds()}";
        var policy = await foundation.UpsertPolicyAsync(
            Cid(http),
            Bid(http),
            policyCode,
            GetText(body, "scopeType") ?? "default",
            GetText(body, "scopeKey") ?? "",
            GetDecimal(body, "minCelsius"),
            GetDecimal(body, "maxCelsius"),
            GetDecimal(body, "humidityMinPercent"),
            GetDecimal(body, "humidityMaxPercent"),
            GetText(body, "severity"),
            GetBool(body, "requiresAcknowledgement", true),
            GetText(body, "status"),
            GetText(body, "sourceChannel"),
            GetText(body, "clientGeneratedId"),
            GetText(body, "idempotencyKey"),
            GetText(body, "correlationId"),
            GetText(body, "causationId"),
            GetText(body, "metadataJson") ?? "{}",
            GetText(body, "notes"),
            ct);

        return Results.Ok(ApiResponse<object>.Ok(policy, "Cold-chain policy saved"));
    }

    private static async Task<IResult> ColdChainEvents(HttpContext http, Database db, CancellationToken ct)
    {
        var denied = EndpointMappings.RequirePermission(http, "fleet:view");
        if (denied is not null) return denied;
        var companyId = Cid(http);
        var items = await db.QueryAsync(@"
SELECT id, company_id, event_type, aggregate_type, aggregate_id, payload_json, correlation_id, causation_id, idempotency_key,
       status, retry_count, error_message, occurred_at_utc, processed_at_utc, created_at_utc
FROM fleet_tms_cold_chain_event_log
WHERE company_id=@companyId" + BranchScope(http) + @"
ORDER BY occurred_at_utc DESC, id DESC
LIMIT 100",
            c => { c.Parameters.AddWithValue("@companyId", companyId); BindBranch(c, http); }, ct);
        return Ok(new { items });
    }

    private static async Task<IResult> ColdChainDevices(HttpContext http, Database db, CancellationToken ct)
    {
        var denied = EndpointMappings.RequirePermission(http, "fleet:view");
        if (denied is not null) return denied;
        var items = await db.QueryAsync(@"
SELECT d.id, d.device_code, d.name, d.zone_id, z.code zone_code, z.name zone_name,
       d.shipment_id, s.shipment_number, d.vehicle_number, d.status,
       d.last_reported_temperature_celsius, d.battery_percent, d.last_ping_at_utc, d.notes,
       d.source_channel, d.client_generated_id, d.correlation_id, d.causation_id, d.metadata_json,
       d.created_at_utc, d.updated_at_utc, d.idempotency_key
FROM fleet_tms_temperature_devices d
LEFT JOIN fleet_tms_temperature_zones z ON z.id=d.zone_id
LEFT JOIN fleet_tms_shipments s ON s.id=d.shipment_id
WHERE d.company_id=@companyId" + BranchScope(http, "d.") + " ORDER BY d.last_ping_at_utc DESC NULLS LAST, d.device_code",
            c => { c.Parameters.AddWithValue("@companyId", Cid(http)); BindBranch(c, http); }, ct);
        return Ok(new { items });
    }

    private static async Task<IResult> CreateDevice(HttpContext http, TemperatureDeviceRequest req, Database db, CancellationToken ct)
    {
        var denied = EndpointMappings.RequirePermission(http, "fleet:manage");
        if (denied is not null) return denied;
        if (ValidateDeviceRequest(req) is { } invalid) return Bad(invalid);
        var companyId = Cid(http);
        var branchId = Bid(http);
        var deviceCode = req.DeviceCode!.Trim();
        var idempotencyKey = string.IsNullOrWhiteSpace(req.IdempotencyKey) ? null : req.IdempotencyKey.Trim();
        return await db.RunInTenantTransactionAsync(companyId, async () =>
        {
            // Serialize both replay keys and business identities in a deterministic order.
            // The Stage54 unique indexes remain the final cross-process safety net.
            var lockKeys = new List<string> { $"cold-device:code:{companyId}:{deviceCode.ToLowerInvariant()}" };
            if (idempotencyKey is not null)
                lockKeys.Add($"cold-device:idem:{companyId}:{idempotencyKey}");
            foreach (var lockKey in lockKeys.Order(StringComparer.Ordinal))
                await db.ExecuteAsync("SELECT pg_advisory_xact_lock(hashtextextended(@lockKey,0))",
                    c => c.Parameters.AddWithValue("@lockKey", lockKey), ct);

            if (idempotencyKey is not null)
            {
                var replays = await db.QueryAsync(@"
SELECT * FROM fleet_tms_temperature_devices
WHERE company_id=@companyId
  AND (CAST(@branchId AS bigint) IS NULL OR branch_id IS NOT DISTINCT FROM @branchId)
  AND idempotency_key=@idempotencyKey
ORDER BY id", c =>
                {
                    c.Parameters.AddWithValue("@companyId", companyId);
                    c.Parameters.AddWithValue("@branchId", (object?)branchId ?? DBNull.Value);
                    c.Parameters.AddWithValue("@idempotencyKey", idempotencyKey);
                }, ct);
                if (replays.Count == 1) return Ok(replays[0]);
                if (replays.Count > 1) return Conflict("Temperature device retry key is ambiguous across branch scopes.");
            }

            if (await db.QuerySingleAsync(@"
SELECT id FROM fleet_tms_temperature_devices
WHERE company_id=@companyId AND lower(btrim(device_code))=lower(btrim(@deviceCode))
LIMIT 1", c =>
                {
                    c.Parameters.AddWithValue("@companyId", companyId);
                    c.Parameters.AddWithValue("@deviceCode", deviceCode);
                }, ct) is not null)
                return Conflict("Temperature device code already exists in this tenant.");

            if (req.ShipmentId.HasValue && branchId is not null)
                return Bad("Branch-scoped devices cannot attach to tenant-wide shipments.");
            if (req.ZoneId.HasValue && await db.QuerySingleAsync(
                    "SELECT id FROM fleet_tms_temperature_zones WHERE company_id=@companyId AND id=@id" + SharedConfigScope(http),
                    c => { c.Parameters.AddWithValue("@companyId", companyId); c.Parameters.AddWithValue("@id", req.ZoneId.Value); BindBranch(c, http); }, ct) is null)
                return NotFound("Temperature zone not found for this tenant.");
            if (req.ShipmentId.HasValue && await Row(db, "fleet_tms_shipments", companyId, req.ShipmentId.Value, ct) is null)
                return NotFound("Shipment not found for this tenant.");
            var id = await db.InsertAsync(@"
INSERT INTO fleet_tms_temperature_devices (company_id, branch_id, device_code, name, zone_id, shipment_id, vehicle_number, status, last_reported_temperature_celsius, battery_percent, last_ping_at_utc, notes,
    source_channel, client_generated_id, idempotency_key, correlation_id, causation_id, metadata_json, created_at_utc, updated_at_utc)
VALUES (@companyId, @branchId, @code, @name, @zone, @shipment, @vehicle, @status, @temp, @battery, @ping, @notes,
    @sourceChannel, @clientGeneratedId, @idempotencyKey, @correlationId, @causationId, @metadata::jsonb, NOW(), NOW())
ON CONFLICT DO NOTHING",
            c =>
            {
                c.Parameters.AddWithValue("@companyId", companyId);
                c.Parameters.AddWithValue("@branchId", (object?)branchId ?? DBNull.Value);
                c.Parameters.AddWithValue("@code", deviceCode);
                c.Parameters.AddWithValue("@name", req.Name!.Trim());
                c.Parameters.AddWithValue("@zone", Nl(req.ZoneId));
                c.Parameters.AddWithValue("@shipment", Nl(req.ShipmentId));
                c.Parameters.AddWithValue("@vehicle", req.VehicleNumber?.Trim() ?? "");
                c.Parameters.AddWithValue("@status", req.Status?.Trim() ?? "Active");
                c.Parameters.AddWithValue("@temp", (object?)req.LastReportedTemperatureCelsius ?? DBNull.Value);
                c.Parameters.AddWithValue("@battery", (object?)req.BatteryPercent ?? DBNull.Value);
                c.Parameters.AddWithValue("@ping", (object?)req.LastPingAtUtc ?? DBNull.Value);
                c.Parameters.AddWithValue("@notes", req.Notes?.Trim() ?? "");
                c.Parameters.AddWithValue("@sourceChannel", (object?)req.SourceChannel ?? DBNull.Value);
                c.Parameters.AddWithValue("@clientGeneratedId", (object?)req.ClientGeneratedId ?? DBNull.Value);
                c.Parameters.AddWithValue("@idempotencyKey", (object?)idempotencyKey ?? DBNull.Value);
                c.Parameters.AddWithValue("@correlationId", (object?)req.CorrelationId ?? DBNull.Value);
                c.Parameters.AddWithValue("@causationId", (object?)req.CausationId ?? DBNull.Value);
                c.Parameters.AddWithValue("@metadata", string.IsNullOrWhiteSpace(req.MetadataJson) ? "{}" : req.MetadataJson);
            }, ct);
            if (id == 0)
            {
                if (idempotencyKey is not null)
                {
                    var replay = await db.QuerySingleAsync(@"
SELECT * FROM fleet_tms_temperature_devices
WHERE company_id=@companyId AND branch_id IS NOT DISTINCT FROM @branchId AND idempotency_key=@idempotencyKey
LIMIT 1", c =>
                    {
                        c.Parameters.AddWithValue("@companyId", companyId);
                        c.Parameters.AddWithValue("@branchId", (object?)branchId ?? DBNull.Value);
                        c.Parameters.AddWithValue("@idempotencyKey", idempotencyKey);
                    }, ct);
                    if (replay is not null) return Ok(replay);
                }
                return Conflict("Temperature device code already exists in this tenant.");
            }
            return Ok(await Row(db, "fleet_tms_temperature_devices", companyId, id, ct)!);
        }, ct);
    }

    private static async Task<IResult> ShipmentReadings(HttpContext http, long shipmentId, Database db, CancellationToken ct)
    {
        var denied = EndpointMappings.RequirePermission(http, "fleet:view");
        if (denied is not null) return denied;
        var items = await db.QueryAsync(@"
SELECT r.id, r.device_id, d.device_code, r.shipment_id, r.zone_id, z.code zone_code, r.temperature_celsius, r.humidity_percent,
       r.latitude, r.longitude, r.source, r.status, r.notes, r.recorded_at_utc, r.created_at_utc,
       r.source_channel, r.client_generated_id, r.correlation_id, r.causation_id, r.metadata_json,
       r.applied_policy_code, r.applied_policy_scope, r.applied_min_celsius, r.applied_max_celsius
FROM fleet_tms_temperature_readings r
LEFT JOIN fleet_tms_temperature_devices d ON d.id=r.device_id
LEFT JOIN fleet_tms_temperature_zones z ON z.id=r.zone_id
WHERE r.company_id=@companyId AND r.shipment_id=@sid" + BranchScope(http, "r.") + " ORDER BY r.recorded_at_utc DESC",
            c => { c.Parameters.AddWithValue("@companyId", Cid(http)); c.Parameters.AddWithValue("@sid", shipmentId); BindBranch(c, http); }, ct);
        return Ok(new { items });
    }

    private static async Task<IResult> CreateReading(HttpContext http, TemperatureReadingRequest req, FleetTmsColdChainFoundationService foundation, Database db, CancellationToken ct)
    {
        var denied = EndpointMappings.RequirePermission(http, "fleet:manage");
        if (denied is not null) return denied;
        if (ValidateReadingRequest(req) is { } invalid) return Bad(invalid);
        var companyId = Cid(http);
        if (req.ShipmentId.HasValue && Bid(http) is not null)
            return Bad("Branch-scoped readings cannot attach to tenant-wide shipments.");
        if (req.ShipmentId.HasValue && await Row(db, "fleet_tms_shipments", companyId, req.ShipmentId.Value, ct) is null)
            return NotFound("Shipment not found for this tenant.");
        try
        {
            return Ok(await foundation.RecordTemperatureReadingAsync(companyId, Bid(http), req, ct));
        }
        catch (InvalidOperationException ex)
        {
            return NotFound(ex.Message);
        }
    }

    private static async Task<IResult> ColdChainAlerts(HttpContext http, Database db, string? status, CancellationToken ct)
    {
        var denied = EndpointMappings.RequirePermission(http, "fleet:view");
        if (denied is not null) return denied;
        var companyId = Cid(http);
        var where = "WHERE a.company_id=@companyId" + BranchScope(http, "a.") + (string.IsNullOrWhiteSpace(status) ? "" : " AND a.status=@status");
        var items = await db.QueryAsync($@"
SELECT a.id, a.device_id, d.device_code, a.shipment_id, s.shipment_number, a.reading_id, a.alert_type, a.severity, a.status,
       a.threshold_min, a.threshold_max, a.measured_temperature, a.measured_humidity,
       a.humidity_threshold_min, a.humidity_threshold_max,
       a.triggered_at_utc, a.resolved_at_utc, a.resolved_by, a.resolution_notes, a.notes
       ,a.source_channel, a.client_generated_id, a.correlation_id, a.causation_id, a.metadata_json,
       a.applied_policy_code, a.applied_policy_scope
FROM fleet_tms_temperature_alerts a
LEFT JOIN fleet_tms_temperature_devices d ON d.id=a.device_id
LEFT JOIN fleet_tms_shipments s ON s.id=a.shipment_id
{where} ORDER BY a.triggered_at_utc DESC",
            c => { c.Parameters.AddWithValue("@companyId", companyId); BindBranch(c, http); if (!string.IsNullOrWhiteSpace(status)) c.Parameters.AddWithValue("@status", status); }, ct);
        return Ok(new { items });
    }

    private static async Task<IResult> ResolveAlert(HttpContext http, long id, TemperatureAlertResolveRequest req, FleetTmsColdChainFoundationService foundation, Database db, CancellationToken ct)
    {
        var denied = EndpointMappings.RequirePermission(http, "fleet:manage");
        if (denied is not null) return denied;
        var companyId = Cid(http);
        try
        {
            return Ok(await foundation.ResolveAlertAsync(companyId, Bid(http), id, req, Actor(http), ct));
        }
        catch (InvalidOperationException ex)
        {
            return NotFound(ex.Message);
        }
    }

    private static async Task<IResult> ColdChainReport(HttpContext http, long shipmentId, Database db, CancellationToken ct)
    {
        var denied = EndpointMappings.RequirePermission(http, "fleet:view");
        if (denied is not null) return denied;
        var companyId = Cid(http);
        var existing = await db.QuerySingleAsync("SELECT * FROM fleet_tms_cold_chain_reports WHERE company_id=@companyId AND shipment_id=@sid" + BranchScope(http) + " ORDER BY generated_at_utc DESC LIMIT 1",
            c => { c.Parameters.AddWithValue("@companyId", companyId); c.Parameters.AddWithValue("@sid", shipmentId); BindBranch(c, http); }, ct);
        if (existing is not null) return Ok(existing);

        var shipment = await Row(db, "fleet_tms_shipments", companyId, shipmentId, ct);
        if (shipment is null) return NotFound("Shipment not found for this tenant.");
        var readings = await db.QueryAsync("SELECT temperature_celsius, status FROM fleet_tms_temperature_readings WHERE company_id=@companyId AND shipment_id=@sid" + BranchScope(http),
            c => { c.Parameters.AddWithValue("@companyId", companyId); c.Parameters.AddWithValue("@sid", shipmentId); BindBranch(c, http); }, ct);
        if (readings.Count == 0) return NotFound("No cold-chain readings found for this shipment.");

        var temps = readings.Select(r => Convert.ToDecimal(r["temperatureCelsius"])).ToList();
        var breachCount = readings.Count(r => string.Equals(r["status"]?.ToString(), "Breach", StringComparison.OrdinalIgnoreCase));
        var shipmentNumber = shipment["shipmentNumber"]?.ToString() ?? "";
        var summaryJson = JsonSerializer.Serialize(new { shipment = shipmentNumber, readings = readings.Count, breachCount });
        // GET is intentionally read-only. A cache miss returns an ephemeral projection
        // with the same report contract; explicit report persistence belongs on a
        // future fleet:manage POST command, never on a fleet:view read.
        return Ok(new
        {
            id = 0L,
            companyId,
            shipmentId,
            shipmentNumber,
            generatedAtUtc = DateTime.UtcNow,
            compliancePercent = Math.Round((1m - (breachCount / (decimal)readings.Count)) * 100m, 1),
            minTemperatureCelsius = temps.Min(),
            maxTemperatureCelsius = temps.Max(),
            totalReadings = readings.Count,
            breachCount,
            summaryJson,
            notes = "Generated on demand from live temperature readings.",
        });
    }

    // ── Assets ──────────────────────────────────────────────────────────────────

    private static async Task<IResult> AssetTypes(HttpContext http, Database db, CancellationToken ct)
        => Ok(new { items = await db.QueryAsync("SELECT * FROM fleet_tms_asset_types WHERE company_id=@companyId" + SharedConfigScope(http) + " ORDER BY name", c => { c.Parameters.AddWithValue("@companyId", Cid(http)); BindBranch(c, http); }, ct) });

    private static async Task<IResult> CreateAssetType(HttpContext http, AssetTypeRequest req, Database db, CancellationToken ct)
    {
        if (ValidateAssetTypeRequest(req) is { } invalid) return Bad(invalid);
        var companyId = Cid(http);
        var normalizedCode = req.Code!.Trim();
        return await db.RunInTenantTransactionAsync(companyId, async () =>
        {
        await db.ExecuteAsync("SELECT pg_advisory_xact_lock(hashtextextended(@lockKey,0))",
            c => c.Parameters.AddWithValue("@lockKey", $"asset-type:code:{companyId}:{normalizedCode.ToLowerInvariant()}"), ct);
        var duplicate = await db.QuerySingleAsync(@"
SELECT id FROM fleet_tms_asset_types
WHERE company_id=@companyId AND lower(btrim(code))=lower(btrim(@code))
LIMIT 1", c =>
        {
            c.Parameters.AddWithValue("@companyId", companyId);
            c.Parameters.AddWithValue("@code", normalizedCode);
        }, ct);
        if (duplicate is not null) return Conflict("Asset type code already exists in this tenant.");

        var id = await db.InsertAsync(@"
INSERT INTO fleet_tms_asset_types (company_id, branch_id, code, name, description, is_returnable, created_at_utc, updated_at_utc)
VALUES (@companyId, @branchId, @code, @name, @desc, @returnable, NOW(), NOW())
ON CONFLICT DO NOTHING",
            c =>
            {
                c.Parameters.AddWithValue("@companyId", companyId);
                c.Parameters.AddWithValue("@branchId", (object?)Bid(http) ?? DBNull.Value);
                c.Parameters.AddWithValue("@code", normalizedCode);
                c.Parameters.AddWithValue("@name", req.Name!.Trim());
                c.Parameters.AddWithValue("@desc", req.Description?.Trim() ?? "");
                c.Parameters.AddWithValue("@returnable", req.IsReturnable ?? true);
            }, ct);
        if (id == 0) return Conflict("Asset type code already exists in this tenant.");
        return Ok(await OwnedRow(db, http, "fleet_tms_asset_types", id, ct)!);
        }, ct);
    }

    private static async Task<IResult> Assets(HttpContext http, Database db, CancellationToken ct)
    {
        var page = int.TryParse(http.Request.Query["page"].FirstOrDefault(), out var parsedPage)
            ? Math.Max(1, parsedPage)
            : 1;
        var pageSize = int.TryParse(http.Request.Query["pageSize"].FirstOrDefault(), out var parsedPageSize)
            ? Math.Clamp(parsedPageSize, 1, 100)
            : 100;
        var search = http.Request.Query["search"].FirstOrDefault()?.Trim() ?? "";
        var direction = string.Equals(http.Request.Query["direction"].FirstOrDefault(), "desc", StringComparison.OrdinalIgnoreCase)
            ? "DESC"
            : "ASC";
        var sort = http.Request.Query["sort"].FirstOrDefault()?.Trim().ToLowerInvariant() switch
        {
            "name" => "a.name",
            "status" => "a.status",
            "location" => "a.current_location",
            "condition" => "a.condition",
            "type" => "t.name",
            "lastseen" => "a.last_seen_at_utc",
            _ => "a.asset_tag",
        };
        var where = @"WHERE a.company_id=@companyId" + BranchScope(http, "a.") + @"
  AND (@search='' OR a.asset_tag ILIKE '%' || @search || '%'
    OR a.name ILIKE '%' || @search || '%'
    OR COALESCE(t.code,'') ILIKE '%' || @search || '%'
    OR COALESCE(t.name,'') ILIKE '%' || @search || '%'
    OR COALESCE(a.status,'') ILIKE '%' || @search || '%'
    OR COALESCE(a.current_location,'') ILIKE '%' || @search || '%'
    OR COALESCE(a.condition,'') ILIKE '%' || @search || '%')";
        Action<NpgsqlCommand> bind = c =>
        {
            c.Parameters.AddWithValue("@companyId", Cid(http));
            c.Parameters.AddWithValue("@search", search);
            BindBranch(c, http);
        };
        var total = await db.ScalarLongAsync(@"
SELECT COUNT(*)
FROM fleet_tms_assets a
LEFT JOIN fleet_tms_asset_types t ON t.id=a.asset_type_id
" + where, bind, ct);
        var summary = await db.QuerySingleAsync(@"
SELECT COUNT(*) FILTER (WHERE a.status IN ('Assigned','InUse')) assigned,
       COUNT(*) FILTER (WHERE a.status='Available') available,
       COUNT(*) FILTER (WHERE a.condition<>'Good') needs_review
FROM fleet_tms_assets a
LEFT JOIN fleet_tms_asset_types t ON t.id=a.asset_type_id
" + where, bind, ct);
        var items = await db.QueryAsync(@"
SELECT a.id, a.asset_type_id, t.code asset_type_code, t.name asset_type_name, a.asset_tag, a.name, a.status,
       a.current_location, a.condition, a.is_returnable, a.quantity, a.unit_of_measure, a.notes, a.last_seen_at_utc, a.created_at_utc,
       (SELECT COUNT(*) FROM fleet_tms_asset_assignments aa WHERE aa.asset_id=a.id AND aa.company_id=a.company_id AND aa.branch_id IS NOT DISTINCT FROM a.branch_id) assignment_count
FROM fleet_tms_assets a
LEFT JOIN fleet_tms_asset_types t ON t.id=a.asset_type_id
" + where + $" ORDER BY {sort} {direction} NULLS LAST, a.asset_tag LIMIT @limit OFFSET @offset",
            c =>
            {
                bind(c);
                c.Parameters.AddWithValue("@limit", pageSize);
                c.Parameters.AddWithValue("@offset", (page - 1) * pageSize);
            }, ct);
        return Ok(new
        {
            items,
            total,
            page,
            pageSize,
            summary = summary ?? new Dictionary<string, object?>
            {
                ["assigned"] = 0L,
                ["available"] = 0L,
                ["needsReview"] = 0L,
            },
        });
    }

    private static async Task<IResult> AssetsExport(HttpContext http, Database db, CancellationToken ct)
    {
        var search = http.Request.Query["search"].FirstOrDefault()?.Trim() ?? "";
        var rows = await db.QueryAsync(@"
SELECT a.asset_tag, b.code branch_code, a.name, t.code asset_type_code, a.status,
       a.current_location, a.condition, a.is_returnable, a.quantity, a.unit_of_measure,
       a.notes, a.last_seen_at_utc, a.created_at_utc
FROM fleet_tms_assets a
LEFT JOIN fleet_tms_asset_types t ON t.id=a.asset_type_id
LEFT JOIN branches b ON b.id=a.branch_id AND b.company_id=a.company_id
WHERE a.company_id=@companyId" + BranchScope(http, "a.") + @"
  AND (@search='' OR a.asset_tag ILIKE '%' || @search || '%'
    OR a.name ILIKE '%' || @search || '%'
    OR COALESCE(t.code,'') ILIKE '%' || @search || '%'
    OR COALESCE(a.current_location,'') ILIKE '%' || @search || '%')
ORDER BY a.asset_tag
LIMIT 100000",
            c =>
            {
                c.Parameters.AddWithValue("@companyId", Cid(http));
                c.Parameters.AddWithValue("@search", search);
                BindBranch(c, http);
            }, ct);
        return CsvFile(rows, "returnable-assets");
    }

    private static IResult CsvFile(IReadOnlyList<Dictionary<string, object?>> rows, string name)
    {
        var csv = new System.Text.StringBuilder();
        if (rows.Count == 0)
        {
            csv.AppendLine("assetTag,branchCode,name,assetTypeCode,status,currentLocation,condition,isReturnable,quantity,unitOfMeasure,notes,lastSeenAtUtc,createdAtUtc");
        }
        else
        {
            var columns = rows[0].Keys.ToList();
            csv.AppendLine(string.Join(",", columns));
            foreach (var row in rows)
                csv.AppendLine(string.Join(",", columns.Select(column => EndpointMappings.CsvCell(row[column]))));
        }
        return Results.File(System.Text.Encoding.UTF8.GetBytes(csv.ToString()), "text/csv", $"{name}_{DateTime.UtcNow:yyyy-MM-dd_HH-mm}.csv");
    }

    private const int AssetImportMaxRows = 500;

    private static List<Dictionary<string, object?>> AssetImportRows(Dictionary<string, object?> body)
    {
        var rows = new List<Dictionary<string, object?>>();
        if (body.TryGetValue("rows", out var raw) && raw is JsonElement array && array.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in array.EnumerateArray())
            {
                if (item.ValueKind != JsonValueKind.Object) continue;
                rows.Add(item.EnumerateObject().ToDictionary(property => property.Name, property => (object?)property.Value.Clone()));
                if (rows.Count >= AssetImportMaxRows) break;
            }
        }
        return rows;
    }

    private static string? AssetImportStr(Dictionary<string, object?> row, string key) => GetText(row, key)?.Trim();

    private static IResult AssetsImportTemplate(HttpContext http)
    {
        const string csv = "assetTag,branchCode,name,assetTypeCode,status,currentLocation,condition,isReturnable,quantity,unitOfMeasure,notes\n" +
                           "TRL-0001,CL-HQ,Certification Trailer 0001,TRAILER,Available,North Yard,Good,true,1,Each,Non-personal certification inventory\n";
        return Results.File(System.Text.Encoding.UTF8.GetBytes(csv), "text/csv", "assets-import-template.csv");
    }

    private sealed record AssetTypeImportIdentity(long Id, long? BranchId, string Code);

    private sealed record AssetImportLookups(
        IReadOnlyDictionary<string, long> ActiveBranches,
        IReadOnlyList<AssetTypeImportIdentity> AssetTypes,
        IReadOnlyDictionary<string, long> ExistingAssetIds);

    private static string AssetImportIdentityKey(long branchId, string assetTag)
        => $"{branchId}:{assetTag.Trim().ToLowerInvariant()}";

    private static async Task<AssetImportLookups> LoadAssetImportLookups(
        HttpContext http, IReadOnlyList<Dictionary<string, object?>> rows, Database db, CancellationToken ct)
    {
        var companyId = Cid(http);
        var activeBranches = await EndpointMappings.LoadActiveImportBranchMap(
            db, companyId, rows.Select(row => AssetImportStr(row, "branchCode")), ct);
        var typeCodes = rows
            .Select(row => AssetImportStr(row, "assetTypeCode"))
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Select(value => value!.ToLowerInvariant())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        var assetTags = rows
            .Select(row => AssetImportStr(row, "assetTag"))
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Select(value => value!.ToLowerInvariant())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        var typeRows = typeCodes.Length == 0
            ? []
            : await db.QueryAsync(@"
SELECT id, branch_id, lower(btrim(code)) normalized_code
FROM fleet_tms_asset_types
WHERE company_id=@companyId
  AND lower(btrim(code)) = ANY(@codes)",
                c =>
                {
                    c.Parameters.AddWithValue("@companyId", companyId);
                    c.Parameters.AddWithValue("@codes", typeCodes);
                }, ct);
        var existingRows = assetTags.Length == 0
            ? []
            : await db.QueryAsync(@"
SELECT id, branch_id, lower(btrim(asset_tag)) normalized_tag
FROM fleet_tms_assets
WHERE company_id=@companyId
  AND branch_id IS NOT NULL
  AND lower(btrim(asset_tag)) = ANY(@tags)",
                c =>
                {
                    c.Parameters.AddWithValue("@companyId", companyId);
                    c.Parameters.AddWithValue("@tags", assetTags);
                }, ct);

        return new AssetImportLookups(
            activeBranches,
            typeRows.Select(row => new AssetTypeImportIdentity(
                Convert.ToInt64(row["id"]),
                row["branchId"] is null or DBNull ? null : Convert.ToInt64(row["branchId"]),
                row["normalizedCode"]?.ToString() ?? "")).ToList(),
            existingRows.ToDictionary(
                row => AssetImportIdentityKey(Convert.ToInt64(row["branchId"]), row["normalizedTag"]?.ToString() ?? ""),
                row => Convert.ToInt64(row["id"]),
                StringComparer.OrdinalIgnoreCase));
    }

    private static (AssetRequest? Request, List<string> Errors) ValidateAssetImportRow(
        Dictionary<string, object?> row, long? rowBranchId, string? branchError,
        HashSet<string> fileTags, IReadOnlyList<AssetTypeImportIdentity> assetTypes)
    {
        var errors = new List<string>();
        if (branchError is not null) errors.Add(branchError);
        var tag = AssetImportStr(row, "assetTag");
        var name = AssetImportStr(row, "name");
        var typeCode = AssetImportStr(row, "assetTypeCode");
        if (tag is null) errors.Add("assetTag is required.");
        else if (!fileTags.Add(rowBranchId is { } branchId ? AssetImportIdentityKey(branchId, tag) : tag))
            errors.Add($"Duplicate assetTag '{tag}' earlier in this file for the same branch.");
        if (name is null) errors.Add("name is required.");
        if (typeCode is null) errors.Add("assetTypeCode is required.");
        long typeId = 0;
        if (typeCode is not null && rowBranchId is not null)
        {
            var type = assetTypes
                .Where(candidate => string.Equals(candidate.Code, typeCode, StringComparison.OrdinalIgnoreCase)
                    && (candidate.BranchId is null || candidate.BranchId == rowBranchId))
                .OrderByDescending(candidate => candidate.BranchId == rowBranchId)
                .FirstOrDefault();
            if (type is null) errors.Add($"Asset type code '{typeCode}' does not exist in this branch scope. Create it first.");
            else typeId = type.Id;
        }
        decimal? quantity = null;
        var quantityRaw = AssetImportStr(row, "quantity");
        if (quantityRaw is not null)
        {
            if (decimal.TryParse(quantityRaw, NumberStyles.Any, CultureInfo.InvariantCulture, out var parsed)) quantity = parsed;
            else errors.Add("quantity must be a number.");
        }
        bool? isReturnable = null;
        var returnableRaw = AssetImportStr(row, "isReturnable");
        if (returnableRaw is not null)
        {
            if (bool.TryParse(returnableRaw, out var parsed)) isReturnable = parsed;
            else errors.Add("isReturnable must be true or false.");
        }
        var request = new AssetRequest(typeId, tag, name, AssetImportStr(row, "status") ?? "Available",
            AssetImportStr(row, "currentLocation") ?? "", AssetImportStr(row, "condition") ?? "Good",
            isReturnable ?? true, quantity ?? 1m, AssetImportStr(row, "unitOfMeasure") ?? "Each",
            AssetImportStr(row, "notes") ?? "", null);
        if (ValidateAssetRequest(request, true) is { } invalid) errors.Add(invalid);
        return (request, errors);
    }

    private static async Task<IResult> AssetsImportPreview(HttpContext http, Dictionary<string, object?> body, Database db, CancellationToken ct)
    {
        var rows = AssetImportRows(body);
        if (rows.Count == 0) return Bad("No rows to import. Send { rows: [...] } parsed from the CSV.");
        var lookups = await LoadAssetImportLookups(http, rows, db, ct);
        var results = new List<object>();
        var creates = 0; var updates = 0; var invalid = 0;
        var fileTags = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        for (var i = 0; i < rows.Count; i++)
        {
            var resolvedBranch = EndpointMappings.ResolveImportBranch(
                AssetImportStr(rows[i], "branchCode"), Bid(http), lookups.ActiveBranches);
            var (request, errors) = ValidateAssetImportRow(
                rows[i], resolvedBranch.BranchId, resolvedBranch.Error, fileTags, lookups.AssetTypes);
            var existingKey = resolvedBranch.BranchId is { } rowBranchId && request?.AssetTag is { } assetTag
                ? AssetImportIdentityKey(rowBranchId, assetTag) : "";
            var existingId = errors.Count == 0 && lookups.ExistingAssetIds.TryGetValue(existingKey, out var cachedId)
                ? cachedId
                : 0;
            var action = errors.Count > 0 ? "error" : existingId > 0 ? "update" : "create";
            if (action == "create") creates++; else if (action == "update") updates++; else invalid++;
            results.Add(new { rowNumber = i + 1, key = request?.AssetTag ?? AssetImportStr(rows[i], "assetTag") ?? "", action, errors });
        }
        return Ok(new { total = rows.Count, creates, updates, invalid, rows = results });
    }

    private static async Task<IResult> AssetsImportCommit(HttpContext http, Dictionary<string, object?> body, Database db, AuditService audit, CancellationToken ct)
    {
        var rows = AssetImportRows(body);
        if (rows.Count == 0) return Bad("No rows to import. Send { rows: [...] } parsed from the CSV.");
        var lookups = await LoadAssetImportLookups(http, rows, db, ct);
        var companyId = Cid(http);
        var created = 0; var updated = 0; var skipped = new List<object>();
        var fileTags = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        for (var i = 0; i < rows.Count; i++)
        {
            var resolvedBranch = EndpointMappings.ResolveImportBranch(
                AssetImportStr(rows[i], "branchCode"), Bid(http), lookups.ActiveBranches);
            var (request, errors) = ValidateAssetImportRow(
                rows[i], resolvedBranch.BranchId, resolvedBranch.Error, fileTags, lookups.AssetTypes);
            var tag = request?.AssetTag ?? AssetImportStr(rows[i], "assetTag") ?? "";
            var rowBranchId = resolvedBranch.BranchId;
            var existingKey = rowBranchId is { } requestedBranchId
                ? AssetImportIdentityKey(requestedBranchId, tag) : "";
            var existingId = errors.Count == 0 && lookups.ExistingAssetIds.TryGetValue(existingKey, out var cachedId)
                ? cachedId
                : 0;
            if (errors.Count > 0) { skipped.Add(new { rowNumber = i + 1, key = tag, errors }); continue; }
            try
            {
                if (existingId > 0)
                {
                    await db.ExecuteWithSavepointAsync(@"UPDATE fleet_tms_assets SET asset_type_id=@type,name=@name,status=@status,current_location=@loc,
                        condition=@condition,is_returnable=@returnable,quantity=@qty,unit_of_measure=@uom,notes=@notes,updated_at_utc=NOW()
                        WHERE id=@id AND company_id=@companyId AND branch_id IS NOT DISTINCT FROM @branchId", c =>
                    {
                        BindAsset(c, companyId, request!, false); c.Parameters.AddWithValue("@id", existingId);
                        c.Parameters.AddWithValue("@branchId", rowBranchId!.Value);
                    }, ct);
                    updated++;
                }
                else
                {
                    await db.InsertWithSavepointAsync(@"INSERT INTO fleet_tms_assets
                        (company_id,branch_id,asset_type_id,asset_tag,name,status,current_location,condition,is_returnable,quantity,unit_of_measure,notes,last_seen_at_utc,created_at_utc,updated_at_utc)
                        VALUES (@companyId,@branchId,@type,@tag,@name,@status,@loc,@condition,@returnable,@qty,@uom,@notes,@lastSeen,NOW(),NOW())", c =>
                    {
                        BindAsset(c, companyId, request!, true); c.Parameters.AddWithValue("@branchId", rowBranchId!.Value);
                    }, ct);
                    created++;
                }
            }
            catch (PostgresException ex) when (ex.SqlState == PostgresErrorCodes.UniqueViolation)
            {
                skipped.Add(new { rowNumber = i + 1, key = tag, errors = new[] { "Asset tag already exists in this branch scope." } });
            }
        }
        await audit.LogAsync(http, "assets.imported", "FleetTmsAsset", null, JsonSerializer.Serialize(new { created, updated, skipped = skipped.Count, total = rows.Count }), ct);
        return Ok(new { created, updated, skipped, total = rows.Count });
    }

    private static async Task<IResult> AssetDetail(HttpContext http, long id, Database db, CancellationToken ct)
    {
        var companyId = Cid(http);
        var asset = await OwnedRow(db, http, "fleet_tms_assets", id, ct);
        if (asset is null) return NotFound();
        var assignments = await db.QueryAsync(@"
SELECT aa.id, aa.asset_id, aa.shipment_id, s.shipment_number, aa.carrier_id, c.name carrier_name,
       aa.assignee_type, aa.assignee_name, aa.quantity, aa.status, aa.assigned_at_utc, aa.released_at_utc, aa.notes
FROM fleet_tms_asset_assignments aa
LEFT JOIN fleet_tms_shipments s ON s.id=aa.shipment_id
LEFT JOIN carriers c ON c.id=aa.carrier_id
WHERE aa.company_id=@companyId AND aa.asset_id=@id AND aa.branch_id IS NOT DISTINCT FROM @assetBranch ORDER BY aa.assigned_at_utc DESC",
            c => { c.Parameters.AddWithValue("@companyId", companyId); c.Parameters.AddWithValue("@id", id); c.Parameters.AddWithValue("@assetBranch", asset["branchId"] ?? DBNull.Value); }, ct);
        var events = await LoadAssetEvents(db, companyId, asset["branchId"] is null or DBNull ? null : Convert.ToInt64(asset["branchId"]), id, ct);
        return Ok(new { asset, assignments, events });
    }

    private static async Task<IResult> CreateAsset(HttpContext http, AssetRequest req, Database db, CancellationToken ct)
    {
        if (ValidateAssetRequest(req, requireIdentity: true) is { } invalid) return Bad(invalid);
        var companyId = Cid(http);
        if (await SharedConfigRow(db, http, "fleet_tms_asset_types", req.AssetTypeId, ct) is null) return NotFound("Asset type not found for this tenant.");
        var duplicate = await db.QuerySingleAsync("SELECT id FROM fleet_tms_assets WHERE company_id=@companyId AND branch_id IS NOT DISTINCT FROM @branchId AND lower(asset_tag)=lower(@tag) LIMIT 1",
            c => { c.Parameters.AddWithValue("@companyId", companyId); c.Parameters.AddWithValue("@branchId", (object?)Bid(http) ?? DBNull.Value); c.Parameters.AddWithValue("@tag", req.AssetTag!.Trim()); }, ct);
        if (duplicate is not null) return Bad("Asset tag already exists in this branch scope.");
        try
        {
        var id = await db.InsertAsync(@"
INSERT INTO fleet_tms_assets (company_id, branch_id, asset_type_id, asset_tag, name, status, current_location, condition, is_returnable, quantity, unit_of_measure, notes, last_seen_at_utc, created_at_utc, updated_at_utc)
VALUES (@companyId, @branchId, @type, @tag, @name, @status, @loc, @condition, @returnable, @qty, @uom, @notes, @lastSeen, NOW(), NOW())",
            c => { BindAsset(c, companyId, req, applyDefaults: true); c.Parameters.AddWithValue("@branchId", (object?)Bid(http) ?? DBNull.Value); }, ct);
        return Ok(await OwnedRow(db, http, "fleet_tms_assets", id, ct)!);
        }
        catch (PostgresException ex) when (ex.SqlState == PostgresErrorCodes.UniqueViolation)
        {
            return Bad("Asset tag already exists in this branch scope.");
        }
    }

    private static void BindAsset(NpgsqlCommand c, long companyId, AssetRequest req, bool applyDefaults)
    {
        c.Parameters.AddWithValue("@companyId", companyId);
        c.Parameters.AddWithValue("@type", req.AssetTypeId);
        c.Parameters.AddWithValue("@tag", req.AssetTag?.Trim() ?? "");
        c.Parameters.AddWithValue("@name", req.Name?.Trim() ?? "");
        c.Parameters.AddWithValue("@status", req.Status?.Trim() ?? "Available");
        c.Parameters.AddWithValue("@loc", req.CurrentLocation?.Trim() ?? "");
        c.Parameters.AddWithValue("@condition", req.Condition?.Trim() ?? "Good");
        c.Parameters.AddWithValue("@returnable", req.IsReturnable.HasValue ? req.IsReturnable.Value : applyDefaults ? true : DBNull.Value);
        c.Parameters.AddWithValue("@qty", req.Quantity.HasValue ? req.Quantity.Value : applyDefaults ? 1m : DBNull.Value);
        c.Parameters.AddWithValue("@uom", req.UnitOfMeasure?.Trim() ?? "Each");
        c.Parameters.AddWithValue("@notes", req.Notes?.Trim() ?? "");
        c.Parameters.AddWithValue("@lastSeen", Dt(req.LastSeenAtUtc));
    }

    private static async Task<(decimal Quantity, string Location, long? BranchId, string Status)?> LockAsset(
        NpgsqlConnection connection, NpgsqlTransaction transaction, long companyId, long? branchId, long assetId, CancellationToken ct)
    {
        await using var command = new NpgsqlCommand(@"
SELECT quantity, current_location, branch_id, status
FROM fleet_tms_assets
WHERE id=@id AND company_id=@companyId AND branch_id IS NOT DISTINCT FROM @branchId
FOR UPDATE", connection, transaction);
        command.Parameters.AddWithValue("@id", assetId);
        command.Parameters.AddWithValue("@companyId", companyId);
        command.Parameters.AddWithValue("@branchId", (object?)branchId ?? DBNull.Value);
        await using var reader = await command.ExecuteReaderAsync(ct);
        if (!await reader.ReadAsync(ct)) return null;
        return (reader.GetDecimal(0), reader.GetString(1), reader.IsDBNull(2) ? null : reader.GetInt64(2), reader.GetString(3));
    }

    private static async Task<long> ActiveCustodyCount(
        NpgsqlConnection connection, NpgsqlTransaction transaction, long companyId, long? branchId, long assetId, CancellationToken ct)
    {
        await using var command = new NpgsqlCommand(@"
SELECT COUNT(*) FROM fleet_tms_asset_assignments
WHERE company_id=@companyId AND branch_id IS NOT DISTINCT FROM @branchId AND asset_id=@asset
  AND released_at_utc IS NULL AND status IN ('Assigned','CheckedOut','InUse')", connection, transaction);
        command.Parameters.AddWithValue("@companyId", companyId);
        command.Parameters.AddWithValue("@branchId", (object?)branchId ?? DBNull.Value);
        command.Parameters.AddWithValue("@asset", assetId);
        return Convert.ToInt64(await command.ExecuteScalarAsync(ct));
    }

    private static async Task WriteAssetCustodyState(NpgsqlConnection connection, NpgsqlTransaction transaction,
        long companyId, long? branchId, long assetId, string status, string? location, string? condition, CancellationToken ct)
    {
        await using var command = new NpgsqlCommand(@"
UPDATE fleet_tms_assets SET status=@status,
 current_location=COALESCE(NULLIF(@loc,''), current_location),
 condition=COALESCE(NULLIF(@condition,''), condition),
 last_seen_at_utc=NOW(), updated_at_utc=NOW()
WHERE id=@id AND company_id=@companyId AND branch_id IS NOT DISTINCT FROM @branchId", connection, transaction);
        command.Parameters.AddWithValue("@status", status);
        command.Parameters.AddWithValue("@loc", location?.Trim() ?? "");
        command.Parameters.AddWithValue("@condition", condition?.Trim() ?? "");
        command.Parameters.AddWithValue("@id", assetId);
        command.Parameters.AddWithValue("@companyId", companyId);
        command.Parameters.AddWithValue("@branchId", (object?)branchId ?? DBNull.Value);
        await command.ExecuteNonQueryAsync(ct);
    }

    private static async Task WriteAssetEvent(NpgsqlConnection connection, NpgsqlTransaction transaction,
        long companyId, long? branchId, long assetId, string type, decimal qty, string location, string actor, string notes, CancellationToken ct)
    {
        await using var command = new NpgsqlCommand(@"
INSERT INTO fleet_tms_asset_events (company_id, branch_id, asset_id, event_type, quantity, location, actor_name, occurred_at_utc, notes)
VALUES (@companyId, @branchId, @asset, @type, @qty, @loc, @actor, NOW(), @notes)", connection, transaction);
        command.Parameters.AddWithValue("@companyId", companyId);
        command.Parameters.AddWithValue("@branchId", (object?)branchId ?? DBNull.Value);
        command.Parameters.AddWithValue("@asset", assetId);
        command.Parameters.AddWithValue("@type", type);
        command.Parameters.AddWithValue("@qty", qty);
        command.Parameters.AddWithValue("@loc", location);
        command.Parameters.AddWithValue("@actor", actor);
        command.Parameters.AddWithValue("@notes", notes);
        await command.ExecuteNonQueryAsync(ct);
    }

    private static async Task<IResult> UpdateAsset(HttpContext http, long id, AssetRequest req, Database db, CancellationToken ct)
    {
        if (ValidateAssetRequest(req, requireIdentity: false) is { } invalid) return Bad(invalid);
        var companyId = Cid(http);
        var asset = await OwnedRow(db, http, "fleet_tms_assets", id, ct);
        if (asset is null) return NotFound();
        if (req.AssetTypeId != 0 && await SharedConfigRow(db, http, "fleet_tms_asset_types", req.AssetTypeId, ct) is null) return NotFound("Asset type not found for this tenant.");
        try
        {
        var rows = await db.ExecuteAsync(@"
UPDATE fleet_tms_assets SET
  asset_type_id=CASE WHEN @type=0 THEN asset_type_id ELSE @type END,
  asset_tag=COALESCE(NULLIF(@tag,''), asset_tag), name=COALESCE(NULLIF(@name,''), name),
  status=COALESCE(NULLIF(@status,''), status), current_location=COALESCE(NULLIF(@loc,''), current_location),
  condition=COALESCE(NULLIF(@condition,''), condition), is_returnable=COALESCE(@returnable, is_returnable),
  quantity=COALESCE(@qty, quantity), unit_of_measure=COALESCE(NULLIF(@uom,''), unit_of_measure), notes=COALESCE(NULLIF(@notes,''), notes),
  last_seen_at_utc=COALESCE(@lastSeen, last_seen_at_utc), updated_at_utc=NOW()
WHERE id=@id AND company_id=@companyId AND branch_id IS NOT DISTINCT FROM @assetBranch
  AND (@qty IS NULL OR @qty >= COALESCE((
    SELECT SUM(a.quantity) FROM fleet_tms_asset_assignments a
    WHERE a.company_id=@companyId AND a.branch_id IS NOT DISTINCT FROM @assetBranch AND a.asset_id=@id
      AND a.released_at_utc IS NULL AND a.status IN ('Assigned','CheckedOut','InUse')
  ),0))",
            c => { BindAsset(c, companyId, req, applyDefaults: false); c.Parameters.AddWithValue("@id", id); c.Parameters.AddWithValue("@assetBranch", asset["branchId"] ?? DBNull.Value); }, ct);
        if (rows == 0) return Bad("Asset quantity cannot be reduced below active custody quantity.");
        return Ok(await OwnedRow(db, http, "fleet_tms_assets", id, ct)!);
        }
        catch (PostgresException ex) when (ex.SqlState == PostgresErrorCodes.UniqueViolation)
        {
            return Bad("Asset tag already exists in this branch scope.");
        }
    }

    private static async Task<IResult> AssignAsset(HttpContext http, long id, AssetAssignmentRequest req, Database db, CancellationToken ct)
    {
        if (ValidateAssetAssignmentRequest(req) is { } invalid) return Bad(invalid);
        var companyId = Cid(http);
        var asset = await OwnedRow(db, http, "fleet_tms_assets", id, ct);
        if (asset is null) return NotFound();
        if (Bid(http) is not null && (req.ShipmentId.HasValue || req.CarrierId.HasValue))
            return Bad("Branch-scoped assets cannot attach to tenant-wide shipments or carriers.");
        if (req.ShipmentId.HasValue && await Row(db, "fleet_tms_shipments", companyId, req.ShipmentId.Value, ct) is null)
            return NotFound("Shipment not found for this tenant.");
        if (req.CarrierId.HasValue && await Row(db, "carriers", companyId, req.CarrierId.Value, ct) is null)
            return NotFound("Carrier not found for this tenant.");
        long? branchId = asset["branchId"] is null or DBNull ? null : Convert.ToInt64(asset["branchId"]);
        try
        {
            var assignId = await db.WithTransactionAsync(async (connection, transaction) =>
            {
                var locked = await LockAsset(connection, transaction, companyId, branchId, id, ct)
                    ?? throw new InvalidOperationException("Asset was not found in this branch scope.");
                var qty = req.Quantity ?? locked.Quantity;
                if (qty > locked.Quantity) throw new InvalidOperationException("Assignment quantity exceeds available asset quantity.");
                if (await ActiveCustodyCount(connection, transaction, companyId, branchId, id, ct) > 0)
                    throw new InvalidOperationException("Asset already has active custody. Check it in before assigning it again.");

                await using var insert = new NpgsqlCommand(@"
INSERT INTO fleet_tms_asset_assignments (company_id, branch_id, asset_id, shipment_id, carrier_id, assignee_type, assignee_name, quantity, status, assigned_at_utc, released_at_utc, notes)
VALUES (@companyId, @branchId, @asset, @shipment, @carrier, @atype, @aname, @qty, @status, NOW(), @released, @notes)
RETURNING id", connection, transaction);
                insert.Parameters.AddWithValue("@companyId", companyId);
                insert.Parameters.AddWithValue("@branchId", (object?)branchId ?? DBNull.Value);
                insert.Parameters.AddWithValue("@asset", id);
                insert.Parameters.AddWithValue("@shipment", Nl(req.ShipmentId));
                insert.Parameters.AddWithValue("@carrier", Nl(req.CarrierId));
                insert.Parameters.AddWithValue("@atype", string.IsNullOrWhiteSpace(req.AssigneeType) ? (req.ShipmentId.HasValue ? "Shipment" : "Warehouse") : req.AssigneeType.Trim());
                insert.Parameters.AddWithValue("@aname", req.AssigneeName?.Trim() ?? (req.ShipmentId.HasValue ? req.ShipmentId.Value.ToString() : "Warehouse"));
                insert.Parameters.AddWithValue("@qty", qty);
                insert.Parameters.AddWithValue("@status", req.Status?.Trim() ?? "Assigned");
                insert.Parameters.AddWithValue("@released", Dt(req.ReleasedAtUtc));
                insert.Parameters.AddWithValue("@notes", req.Notes?.Trim() ?? "");
                var insertedId = Convert.ToInt64(await insert.ExecuteScalarAsync(ct));

                await WriteAssetCustodyState(connection, transaction, companyId, branchId, id, "Assigned", req.CurrentLocation, null, ct);
                await WriteAssetEvent(connection, transaction, companyId, branchId, id, "Assigned", qty,
                    req.CurrentLocation?.Trim() ?? locked.Location, Actor(http), req.Notes?.Trim() ?? "", ct);
                return insertedId;
            }, ct);
            return Ok(await OwnedRow(db, http, "fleet_tms_asset_assignments", assignId, ct)!);
        }
        catch (InvalidOperationException ex) { return Bad(ex.Message); }
        catch (PostgresException ex) when (ex.SqlState == PostgresErrorCodes.UniqueViolation) { return Bad("Asset already has active custody."); }
    }

    private static async Task<IResult> CheckInAsset(HttpContext http, long id, AssetMovementRequest req, Database db, CancellationToken ct)
    {
        if (ValidateAssetMovementRequest(req) is { } invalid) return Bad(invalid);
        var companyId = Cid(http);
        var asset = await OwnedRow(db, http, "fleet_tms_assets", id, ct);
        if (asset is null) return NotFound();
        long? branchId = asset["branchId"] is null or DBNull ? null : Convert.ToInt64(asset["branchId"]);
        try
        {
            await db.WithTransactionAsync<object?>(async (connection, transaction) =>
            {
                var locked = await LockAsset(connection, transaction, companyId, branchId, id, ct)
                    ?? throw new InvalidOperationException("Asset was not found in this branch scope.");
                int released;
                await using (var close = new NpgsqlCommand(@"
UPDATE fleet_tms_asset_assignments SET status='Returned', released_at_utc=NOW(),
 notes=COALESCE(NULLIF(@notes,''), notes)
WHERE company_id=@companyId AND branch_id IS NOT DISTINCT FROM @branchId AND asset_id=@id
  AND released_at_utc IS NULL AND status IN ('Assigned','CheckedOut','InUse')", connection, transaction))
                {
                    close.Parameters.AddWithValue("@notes", req.Notes?.Trim() ?? "");
                    close.Parameters.AddWithValue("@id", id);
                    close.Parameters.AddWithValue("@companyId", companyId);
                    close.Parameters.AddWithValue("@branchId", (object?)branchId ?? DBNull.Value);
                    released = await close.ExecuteNonQueryAsync(ct);
                }
                // A transport retry after the asset is already available is a stable
                // replay, not a second custody transition. If state and custody are
                // inconsistent, continue through the normal recovery write/event.
                if (released == 0 && string.Equals(locked.Status, "Available", StringComparison.Ordinal))
                    return null;
                await WriteAssetCustodyState(connection, transaction, companyId, branchId, id, "Available", req.Location, req.Condition, ct);
                await WriteAssetEvent(connection, transaction, companyId, branchId, id, "CheckIn", locked.Quantity,
                    req.Location?.Trim() ?? locked.Location, Actor(http), req.Notes?.Trim() ?? "Asset checked back into inventory.", ct);
                return null;
            }, ct);
            return Ok(await OwnedRow(db, http, "fleet_tms_assets", id, ct)!);
        }
        catch (InvalidOperationException ex) { return Bad(ex.Message); }
    }

    private static async Task<IResult> CheckOutAsset(HttpContext http, long id, AssetMovementRequest req, Database db, CancellationToken ct)
    {
        if (ValidateAssetMovementRequest(req) is { } invalid) return Bad(invalid);
        var companyId = Cid(http);
        var asset = await OwnedRow(db, http, "fleet_tms_assets", id, ct);
        if (asset is null) return NotFound();
        if (Bid(http) is not null && (req.ShipmentId.HasValue || req.CarrierId.HasValue))
            return Bad("Branch-scoped assets cannot attach to tenant-wide shipments or carriers.");
        if (req.ShipmentId.HasValue && await Row(db, "fleet_tms_shipments", companyId, req.ShipmentId.Value, ct) is null)
            return NotFound("Shipment not found for this tenant.");
        if (req.CarrierId.HasValue && await Row(db, "carriers", companyId, req.CarrierId.Value, ct) is null)
            return NotFound("Carrier not found for this tenant.");
        long? branchId = asset["branchId"] is null or DBNull ? null : Convert.ToInt64(asset["branchId"]);
        var notes = req.Notes?.Trim() ?? "Asset checked out.";
        try
        {
            await db.WithTransactionAsync<object?>(async (connection, transaction) =>
            {
                var locked = await LockAsset(connection, transaction, companyId, branchId, id, ct)
                    ?? throw new InvalidOperationException("Asset was not found in this branch scope.");
                var qty = req.Quantity ?? locked.Quantity;
                if (qty > locked.Quantity) throw new InvalidOperationException("Checkout quantity exceeds available asset quantity.");
                if (await ActiveCustodyCount(connection, transaction, companyId, branchId, id, ct) > 0)
                    throw new InvalidOperationException("Asset already has active custody. Check it in before checking it out again.");

                await using var insert = new NpgsqlCommand(@"
INSERT INTO fleet_tms_asset_assignments (company_id, branch_id, asset_id, shipment_id, carrier_id, assignee_type, assignee_name, quantity, status, assigned_at_utc, notes)
VALUES (@companyId, @branchId, @asset, @shipment, @carrier, @atype, @aname, @qty, 'CheckedOut', NOW(), @notes)", connection, transaction);
                insert.Parameters.AddWithValue("@companyId", companyId);
                insert.Parameters.AddWithValue("@branchId", (object?)branchId ?? DBNull.Value);
                insert.Parameters.AddWithValue("@asset", id);
                insert.Parameters.AddWithValue("@shipment", Nl(req.ShipmentId));
                insert.Parameters.AddWithValue("@carrier", Nl(req.CarrierId));
                insert.Parameters.AddWithValue("@atype", string.IsNullOrWhiteSpace(req.AssigneeType) ? (req.ShipmentId.HasValue ? "Shipment" : "Dispatch") : req.AssigneeType.Trim());
                insert.Parameters.AddWithValue("@aname", req.AssigneeName?.Trim() ?? (req.ShipmentId.HasValue ? req.ShipmentId.Value.ToString() : "Dispatch"));
                insert.Parameters.AddWithValue("@qty", qty);
                insert.Parameters.AddWithValue("@notes", notes);
                await insert.ExecuteNonQueryAsync(ct);
                await WriteAssetCustodyState(connection, transaction, companyId, branchId, id, "InUse", req.Location, req.Condition, ct);
                await WriteAssetEvent(connection, transaction, companyId, branchId, id, "CheckOut", qty,
                    req.Location?.Trim() ?? locked.Location, Actor(http), notes, ct);
                return null;
            }, ct);
            return Ok(await OwnedRow(db, http, "fleet_tms_assets", id, ct)!);
        }
        catch (InvalidOperationException ex) { return Bad(ex.Message); }
        catch (PostgresException ex) when (ex.SqlState == PostgresErrorCodes.UniqueViolation) { return Bad("Asset already has active custody."); }
    }

    private static async Task<IResult> AssetEvents(HttpContext http, long id, Database db, CancellationToken ct)
    {
        var companyId = Cid(http);
        var asset = await OwnedRow(db, http, "fleet_tms_assets", id, ct);
        if (asset is null) return NotFound();
        return Ok(new { items = await LoadAssetEvents(db, companyId, asset["branchId"] is null or DBNull ? null : Convert.ToInt64(asset["branchId"]), id, ct) });
    }

    private static async Task<IResult> ScanAsset(HttpContext http, AssetScanRequest req, Database db, CancellationToken ct)
    {
        if (ValidateAssetScanRequest(req) is { } invalid) return Bad(invalid);
        var companyId = Cid(http);
        if (req.ShipmentId.HasValue && Bid(http) is not null)
            return Bad("Branch-scoped scans cannot attach to tenant-wide shipments.");
        if (req.ShipmentId.HasValue && await Row(db, "fleet_tms_shipments", companyId, req.ShipmentId.Value, ct) is null)
            return NotFound("Shipment not found for this tenant.");
        var isRfid = string.Equals(req.Kind, "RFID", StringComparison.OrdinalIgnoreCase) || !string.IsNullOrWhiteSpace(req.TagId);
        var identifier = (isRfid && !string.IsNullOrWhiteSpace(req.TagId) ? req.TagId : req.ScannedValue)!.Trim();
        var asset = req.AssetId.HasValue
            ? await OwnedRow(db, http, "fleet_tms_assets", req.AssetId.Value, ct)
            : await db.QuerySingleAsync(
                "SELECT * FROM fleet_tms_assets WHERE company_id=@companyId AND lower(asset_tag)=lower(@tag)" + BranchScope(http) + " LIMIT 1",
                c => { c.Parameters.AddWithValue("@companyId", companyId); c.Parameters.AddWithValue("@tag", identifier); BindBranch(c, http); }, ct);
        if (asset is null || !string.Equals(asset["assetTag"]?.ToString(), identifier, StringComparison.OrdinalIgnoreCase))
            return NotFound("Unknown asset scan identifier for this tenant.");
        var resolvedAssetId = Convert.ToInt64(asset["id"]);
        if (isRfid)
        {
            var id = await db.InsertAsync(@"
INSERT INTO fleet_tms_rfid_events (company_id, branch_id, asset_id, shipment_id, tag_id, reader_id, event_type, status, recorded_at_utc, notes)
VALUES (@companyId, @branchId, @asset, @shipment, @tag, @reader, @etype, @status, NOW(), @notes)",
                c =>
                {
                    c.Parameters.AddWithValue("@companyId", companyId);
                    c.Parameters.AddWithValue("@branchId", asset["branchId"] ?? DBNull.Value);
                    c.Parameters.AddWithValue("@asset", resolvedAssetId);
                    c.Parameters.AddWithValue("@shipment", Nl(req.ShipmentId));
                    c.Parameters.AddWithValue("@tag", identifier);
                    c.Parameters.AddWithValue("@reader", req.ReaderId?.Trim() ?? "RFID-GATE");
                    c.Parameters.AddWithValue("@etype", req.EventType?.Trim() ?? "Read");
                    c.Parameters.AddWithValue("@status", req.Status?.Trim() ?? "Captured");
                    c.Parameters.AddWithValue("@notes", req.Notes?.Trim() ?? "");
                }, ct);
            return Ok(await OwnedRow(db, http, "fleet_tms_rfid_events", id, ct)!);
        }
        var bid = await db.InsertAsync(@"
INSERT INTO fleet_tms_barcode_scan_events (company_id, branch_id, asset_id, shipment_id, scanned_value, scanner_id, event_type, status, recorded_at_utc, notes)
VALUES (@companyId, @branchId, @asset, @shipment, @value, @scanner, @etype, @status, NOW(), @notes)",
            c =>
            {
                c.Parameters.AddWithValue("@companyId", companyId);
                c.Parameters.AddWithValue("@branchId", asset["branchId"] ?? DBNull.Value);
                c.Parameters.AddWithValue("@asset", resolvedAssetId);
                c.Parameters.AddWithValue("@shipment", Nl(req.ShipmentId));
                c.Parameters.AddWithValue("@value", identifier);
                c.Parameters.AddWithValue("@scanner", req.ScannerId?.Trim() ?? "BARCODE-SCAN");
                c.Parameters.AddWithValue("@etype", req.EventType?.Trim() ?? "Scan");
                c.Parameters.AddWithValue("@status", req.Status?.Trim() ?? "Captured");
                c.Parameters.AddWithValue("@notes", req.Notes?.Trim() ?? "");
            }, ct);
        return Ok(await OwnedRow(db, http, "fleet_tms_barcode_scan_events", bid, ct)!);
    }

    private static async Task<List<Dictionary<string, object?>>> LoadAssetEvents(Database db, long companyId, long? branchId, long assetId, CancellationToken ct)
        => await db.QueryAsync(@"
SELECT id, type, event_type, quantity, location, actor_name, occurred_at_utc, notes FROM (
  SELECT id, 'AssetEvent' type, event_type, quantity, location, actor_name, occurred_at_utc, notes
    FROM fleet_tms_asset_events WHERE company_id=@companyId AND branch_id IS NOT DISTINCT FROM @branchId AND asset_id=@id
  UNION ALL
  SELECT id, 'BarcodeScan' type, event_type, 1 quantity, scanner_id location, scanner_id actor_name, recorded_at_utc occurred_at_utc, notes
    FROM fleet_tms_barcode_scan_events WHERE company_id=@companyId AND branch_id IS NOT DISTINCT FROM @branchId AND asset_id=@id
  UNION ALL
  SELECT id, 'RfidEvent' type, event_type, 1 quantity, reader_id location, reader_id actor_name, recorded_at_utc occurred_at_utc, notes
    FROM fleet_tms_rfid_events WHERE company_id=@companyId AND branch_id IS NOT DISTINCT FROM @branchId AND asset_id=@id
  UNION ALL
  SELECT id, 'Assignment' type, status event_type, quantity, assignee_name location, assignee_type actor_name, assigned_at_utc occurred_at_utc, notes
    FROM fleet_tms_asset_assignments WHERE company_id=@companyId AND branch_id IS NOT DISTINCT FROM @branchId AND asset_id=@id
) e ORDER BY occurred_at_utc DESC",
            c => { c.Parameters.AddWithValue("@companyId", companyId); c.Parameters.AddWithValue("@branchId", (object?)branchId ?? DBNull.Value); c.Parameters.AddWithValue("@id", assetId); }, ct);

    private static async Task LogAssetEvent(Database db, long companyId, long? branchId, long assetId, string type, decimal qty, string location, string actor, string notes, CancellationToken ct)
        => await db.ExecuteAsync(@"
INSERT INTO fleet_tms_asset_events (company_id, branch_id, asset_id, event_type, quantity, location, actor_name, occurred_at_utc, notes)
VALUES (@companyId, @branchId, @asset, @type, @qty, @loc, @actor, NOW(), @notes)",
            c => { c.Parameters.AddWithValue("@companyId", companyId); c.Parameters.AddWithValue("@branchId", (object?)branchId ?? DBNull.Value); c.Parameters.AddWithValue("@asset", assetId); c.Parameters.AddWithValue("@type", type); c.Parameters.AddWithValue("@qty", qty); c.Parameters.AddWithValue("@loc", location); c.Parameters.AddWithValue("@actor", actor); c.Parameters.AddWithValue("@notes", notes); }, ct);

    // ── Saudi readiness / compliance ────────────────────────────────────────────

    private static async Task<IResult> SaudiRegions(HttpContext http, Database db, CancellationToken ct)
    {
        if (await RequireSaudiPack(http, db, ct) is { } denied) return denied;
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
        if (await RequireSaudiPack(http, db, ct) is { } denied) return denied;
        return SaudiLedgerRetired();
    }

    private static async Task<IResult> CreateComplianceDocument(HttpContext http, FleetReadinessDocumentRequest req, Database db, CancellationToken ct)
    {
        if (await RequireSaudiPack(http, db, ct) is { } denied) return denied;
        return SaudiLedgerRetired();
    }

    private static async Task<IResult> UpdateComplianceDocument(HttpContext http, long id, FleetReadinessDocumentRequest req, Database db, CancellationToken ct)
    {
        if (await RequireSaudiPack(http, db, ct) is { } denied) return denied;
        return SaudiLedgerRetired();
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
        c.Parameters.AddWithValue("@expiryStatus", ComputeExpiryStatus(req.GregorianExpiryDate ?? req.HijriExpiryDate, req.DocumentStatus));
        c.Parameters.AddWithValue("@issue", Dte(req.IssueDate));
        c.Parameters.AddWithValue("@hijri", Dte(req.HijriExpiryDate));
        c.Parameters.AddWithValue("@gregorian", Dte(req.GregorianExpiryDate));
        c.Parameters.AddWithValue("@notes", req.Notes?.Trim() ?? "");
    }

    private static async Task<IResult> ComplianceExpiries(HttpContext http, Database db, CancellationToken ct)
    {
        if (await RequireSaudiPack(http, db, ct) is { } denied) return denied;
        return SaudiLedgerRetired();
    }

    private static IResult SaudiLedgerRetired() => Results.Json(ApiResponse<object>.Fail(
        "Saudi document ledger moved", "Use /api/fleet-compliance/saudi/documents and /api/fleet-compliance/saudi/expiries."),
        statusCode: StatusCodes.Status410Gone);

    private static async Task<IResult> VatInvoiceReady(HttpContext http, Database db, CancellationToken ct)
    {
        if (await RequireSaudiPack(http, db, ct) is { } denied) return denied;
        var companyId = Cid(http);
        void B(NpgsqlCommand c) => c.Parameters.AddWithValue("@companyId", companyId);
        var shipmentMetricsAvailable = Bid(http) is null;
        var readyShipments = shipmentMetricsAvailable ? await db.QueryAsync(@"
SELECT id, shipment_number, customer_name, status, customer_vat_number, customer_commercial_registration_no,
       invoice_ready_at_utc, invoice_readiness_notes, origin, destination, carrier_name, route_code
FROM fleet_tms_shipments
WHERE company_id=@companyId AND is_invoice_ready AND customer_vat_number <> '' AND customer_commercial_registration_no <> ''
ORDER BY invoice_ready_at_utc DESC NULLS LAST, shipment_number LIMIT 10", B, ct) : [];
        var blockedShipments = shipmentMetricsAvailable ? await db.QueryAsync(@"
SELECT id, shipment_number, customer_name, status, invoice_readiness_notes, origin, destination, carrier_name, route_code
FROM fleet_tms_shipments
WHERE company_id=@companyId AND (NOT is_invoice_ready OR customer_vat_number = '' OR customer_commercial_registration_no = '')
ORDER BY created_at_utc DESC LIMIT 10", B, ct) : [];

        var taxProfiles = await db.QueryAsync(@"
SELECT vat_number, commercial_registration_no, e_invoice_readiness_status
FROM business_tax_readiness
WHERE company_id=@companyId AND pack_code='saudi_gcc'" + BranchScope(http),
            c => { c.Parameters.AddWithValue("@companyId", companyId); BindBranch(c, http); }, ct);

        long? readyCount = shipmentMetricsAvailable ? await db.ScalarLongAsync(@"
SELECT COUNT(*) FROM fleet_tms_shipments
WHERE company_id=@companyId AND is_invoice_ready AND customer_vat_number <> '' AND customer_commercial_registration_no <> ''", B, ct) : null;
        long? blockedCount = shipmentMetricsAvailable ? await db.ScalarLongAsync(@"
SELECT COUNT(*) FROM fleet_tms_shipments
WHERE company_id=@companyId AND (NOT is_invoice_ready OR customer_vat_number = '' OR customer_commercial_registration_no = '')", B, ct) : null;
        decimal? readinessPercent = readyCount.HasValue && blockedCount.HasValue
            ? readyCount.Value + blockedCount.Value == 0 ? 0m : Math.Round(readyCount.Value / (decimal)(readyCount.Value + blockedCount.Value) * 100m, 1)
            : null;
        var branchReady = taxProfiles.Count(d =>
            !string.IsNullOrWhiteSpace(d["vatNumber"]?.ToString()) && !string.IsNullOrWhiteSpace(d["commercialRegistrationNo"]?.ToString())
            && string.Equals(d["eInvoiceReadinessStatus"]?.ToString(), "ready", StringComparison.OrdinalIgnoreCase));
        return Ok(new
        {
            generatedAtUtc = DateTime.UtcNow,
            summary = new { shipmentMetricsAvailable, readyCount, blockedCount, readinessPercent, branchReady },
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
        if (!Allowed(req.Kind, "Compliance", "Transport", "Driver")) return "Document kind is invalid.";
        if (!Allowed(req.SubjectType, "Branch", "Carrier", "Shipment", "Driver", "Vehicle", "Customer", "Location")) return "Subject type is invalid.";
        if (!Allowed(req.DocumentStatus, "Active", "Suspended", "Expired", "Cancelled", "Pending")) return "Document status is invalid.";
        if (req.RequiresExpiry && !req.GregorianExpiryDate.HasValue && !req.HijriExpiryDate.HasValue) return "Expiry date is required for readiness documents.";
        var expiry = req.GregorianExpiryDate ?? req.HijriExpiryDate;
        if (req.IssueDate.HasValue && expiry.HasValue && req.IssueDate.Value > expiry.Value) return "Issue date cannot be after expiry date.";
        if (TooLong(req.Kind, 40) || TooLong(req.SubjectType, 40) || TooLong(req.SubjectId, 80) || TooLong(req.SubjectName, 255)
            || TooLong(req.DocumentType, 120) || TooLong(req.DocumentNumber, 120) || TooLong(req.TransportDocumentNo, 120)
            || TooLong(req.PermitNo, 120) || TooLong(req.VATNumber, 60) || TooLong(req.CommercialRegistrationNo, 60)
            || TooLong(req.CountryCode, 8) || TooLong(req.NationalAddressBuildingNo, 40) || TooLong(req.NationalAddressAdditionalNo, 40)
            || TooLong(req.District, 120) || TooLong(req.City, 120) || TooLong(req.Region, 120) || TooLong(req.PostalCode, 20)
            || TooLong(req.Notes, 4000)) return "One or more document fields exceed their maximum length.";
        return null;
    }

    internal static string ComputeExpiryStatus(DateOnly? gregorianExpiryDate, string? documentStatus)
    {
        if (!string.IsNullOrWhiteSpace(documentStatus) && !string.Equals(documentStatus, "Active", StringComparison.OrdinalIgnoreCase))
            return documentStatus.Trim();
        if (!gregorianExpiryDate.HasValue) return "Healthy";
        var days = (gregorianExpiryDate.Value.ToDateTime(TimeOnly.MinValue).Date - DateTime.UtcNow.Date).Days;
        if (days < 0) return "Expired";
        if (days <= ExpiryWindowDays) return "ExpiringSoon";
        return "Healthy";
    }

    private static DateOnly? DateOnlyValue(object? value) => value switch
    {
        DateOnly date => date,
        DateTime date => DateOnly.FromDateTime(date),
        _ => null,
    };

    private static void ApplyLiveExpiry(Dictionary<string, object?> document)
    {
        var gregorian = DateOnlyValue(document.GetValueOrDefault("gregorianExpiryDate"));
        var hijri = DateOnlyValue(document.GetValueOrDefault("hijriExpiryDate"));
        var effective = gregorian ?? hijri;
        document["expiryStatus"] = ComputeExpiryStatus(effective, document.GetValueOrDefault("documentStatus")?.ToString());
        document["expiryCalendar"] = gregorian.HasValue ? "Gregorian" : hijri.HasValue ? "Hijri" : "None";
        document["effectiveExpiryDate"] = effective?.ToDateTime(TimeOnly.MinValue);
    }

    internal static string? ValidateDeviceRequest(TemperatureDeviceRequest req)
    {
        if (string.IsNullOrWhiteSpace(req.DeviceCode)) return "Device code is required.";
        if (req.DeviceCode.Trim().Length > 60) return "Device code cannot exceed 60 characters.";
        if (string.IsNullOrWhiteSpace(req.Name)) return "Device name is required.";
        if (req.Name.Trim().Length > 120) return "Device name cannot exceed 120 characters.";
        if (req.ZoneId is <= 0) return "Temperature zone id must be positive.";
        if (req.ShipmentId is <= 0) return "Shipment id must be positive.";
        if (!Allowed(req.Status, "Active", "Inactive", "Maintenance", "Retired")) return "Device status is invalid.";
        if (req.LastReportedTemperatureCelsius is < -100 or > 100) return "Temperature must be between -100 and 100 Celsius.";
        if (req.BatteryPercent is < 0 or > 100) return "Battery percent must be between 0 and 100.";
        if (TooLong(req.VehicleNumber, 60) || TooLong(req.Notes, 4000) || TooLong(req.SourceChannel, 80)
            || TooLong(req.ClientGeneratedId, 120) || TooLong(req.IdempotencyKey, 160)
            || TooLong(req.CorrelationId, 160) || TooLong(req.CausationId, 160))
            return "One or more device fields exceed their maximum length.";
        return ValidateJsonObject(req.MetadataJson);
    }

    internal static string? ValidatePolicyRequest(Dictionary<string, object?> body)
    {
        var policyCode = GetText(body, "policyCode");
        var scopeType = GetText(body, "scopeType") ?? "default";
        var scopeKey = GetText(body, "scopeKey") ?? "";
        var severity = GetText(body, "severity") ?? "High";
        var status = GetText(body, "status") ?? "Active";
        var min = GetDecimal(body, "minCelsius");
        var max = GetDecimal(body, "maxCelsius");
        var humidityMin = GetDecimal(body, "humidityMinPercent");
        var humidityMax = GetDecimal(body, "humidityMaxPercent");

        if ((policyCode?.Length ?? 0) > 80) return "Policy code cannot exceed 80 characters.";
        if (!Allowed(scopeType, "default", "device", "shipment", "vehicle", "zone")) return "Policy scope type is invalid.";
        if (scopeType != "default" && string.IsNullOrWhiteSpace(scopeKey)) return "Policy scope key is required for a non-default scope.";
        if (scopeKey.Length > 120) return "Policy scope key cannot exceed 120 characters.";
        if (min is < -100 or > 100 || max is < -100 or > 100) return "Policy temperatures must be between -100 and 100 Celsius.";
        if (min.HasValue && max.HasValue && min.Value >= max.Value) return "Policy minimum temperature must be lower than maximum temperature.";
        if (humidityMin is < 0 or > 100 || humidityMax is < 0 or > 100) return "Policy humidity bounds must be between 0 and 100 percent.";
        if (humidityMin.HasValue && humidityMax.HasValue && humidityMin.Value > humidityMax.Value) return "Policy minimum humidity cannot exceed maximum humidity.";
        if (!Allowed(severity, "Low", "Medium", "High", "Critical")) return "Policy severity is invalid.";
        if (!Allowed(status, "Active", "Inactive")) return "Policy status is invalid.";
        if (TooLong(GetText(body, "sourceChannel"), 80) || TooLong(GetText(body, "clientGeneratedId"), 120)
            || TooLong(GetText(body, "idempotencyKey"), 160) || TooLong(GetText(body, "correlationId"), 160)
            || TooLong(GetText(body, "causationId"), 160) || TooLong(GetText(body, "notes"), 4000))
            return "One or more policy fields exceed their maximum length.";
        return ValidateJsonObject(GetText(body, "metadataJson"));
    }

    internal static string? ValidateReadingRequest(TemperatureReadingRequest req)
    {
        if (req.DeviceId <= 0) return "Temperature device id is required.";
        if (req.ShipmentId is <= 0) return "Shipment id must be positive.";
        if (req.ZoneId is <= 0) return "Temperature zone id must be positive.";
        if (req.TemperatureCelsius is < -100 or > 100) return "Temperature must be between -100 and 100 Celsius.";
        if (req.HumidityPercent is < 0 or > 100) return "Humidity percent must be between 0 and 100.";
        if (req.Latitude is < -90 or > 90) return "Latitude must be between -90 and 90.";
        if (req.Longitude is < -180 or > 180) return "Longitude must be between -180 and 180.";
        if (!Allowed(req.Status, "Normal", "Warning", "Breach")) return "Reading status is invalid.";
        if (!Allowed(req.Source, "Sensor", "Gateway", "Manual", "Import")) return "Reading source is invalid.";
        if (TooLong(req.Source, 30) || TooLong(req.Notes, 4000) || TooLong(req.SourceChannel, 80)
            || TooLong(req.ClientGeneratedId, 120) || TooLong(req.IdempotencyKey, 160)
            || TooLong(req.CorrelationId, 160) || TooLong(req.CausationId, 160))
            return "One or more reading fields exceed their maximum length.";
        return ValidateJsonObject(req.MetadataJson);
    }

    internal static string? ValidateAssetTypeRequest(AssetTypeRequest req)
    {
        if (string.IsNullOrWhiteSpace(req.Code)) return "Asset type code is required.";
        if (req.Code.Trim().Length > 40) return "Asset type code cannot exceed 40 characters.";
        if (string.IsNullOrWhiteSpace(req.Name)) return "Asset type name is required.";
        if (req.Name.Trim().Length > 120) return "Asset type name cannot exceed 120 characters.";
        return TooLong(req.Description, 4000) ? "Asset type description cannot exceed 4000 characters." : null;
    }

    internal static string? ValidateAssetRequest(AssetRequest req, bool requireIdentity)
    {
        if (req.AssetTypeId < 0 || requireIdentity && req.AssetTypeId == 0) return "Asset type is required.";
        if (requireIdentity && string.IsNullOrWhiteSpace(req.AssetTag)) return "Asset tag is required.";
        if (requireIdentity && string.IsNullOrWhiteSpace(req.Name)) return "Asset name is required.";
        if (TooLong(req.AssetTag, 80) || TooLong(req.Name, 160) || TooLong(req.CurrentLocation, 160)
            || TooLong(req.UnitOfMeasure, 30) || TooLong(req.Notes, 4000))
            return "One or more asset fields exceed their maximum length.";
        if (!Allowed(req.Status, "Available", "Assigned", "InUse", "Maintenance", "Retired", "Lost")) return "Asset status is invalid.";
        if (!Allowed(req.Condition, "Good", "Fair", "Damaged", "Repair", "Lost")) return "Asset condition is invalid.";
        if (req.Quantity is <= 0 or > 1_000_000) return "Asset quantity must be greater than zero and no more than 1000000.";
        return null;
    }

    internal static string? ValidateAssetAssignmentRequest(AssetAssignmentRequest req)
    {
        if (req.ShipmentId is <= 0 || req.CarrierId is <= 0) return "Referenced ids must be positive.";
        if (req.Quantity is <= 0 or > 1_000_000) return "Assignment quantity must be greater than zero and no more than 1000000.";
        if (!Allowed(req.AssigneeType, "Shipment", "Warehouse", "Carrier", "Driver", "Dispatch")) return "Assignee type is invalid.";
        if (!Allowed(req.Status, "Assigned", "CheckedOut", "Released", "Returned")) return "Assignment status is invalid.";
        return TooLong(req.AssigneeName, 255) || TooLong(req.CurrentLocation, 160) || TooLong(req.Notes, 4000)
            ? "One or more assignment fields exceed their maximum length." : null;
    }

    internal static string? ValidateAssetMovementRequest(AssetMovementRequest req)
    {
        if (req.ShipmentId is <= 0 || req.CarrierId is <= 0) return "Referenced ids must be positive.";
        if (req.Quantity is <= 0 or > 1_000_000) return "Movement quantity must be greater than zero and no more than 1000000.";
        if (!Allowed(req.Condition, "Good", "Fair", "Damaged", "Repair", "Lost")) return "Asset condition is invalid.";
        if (!Allowed(req.AssigneeType, "Shipment", "Warehouse", "Carrier", "Driver", "Dispatch")) return "Assignee type is invalid.";
        return TooLong(req.Location, 160) || TooLong(req.AssigneeName, 255) || TooLong(req.Notes, 4000)
            ? "One or more movement fields exceed their maximum length." : null;
    }

    internal static string? ValidateAssetScanRequest(AssetScanRequest req)
    {
        if (req.AssetId is <= 0 || req.ShipmentId is <= 0) return "Referenced ids must be positive.";
        if (!Allowed(req.Kind, "Barcode", "RFID")) return "Scan kind is invalid.";
        var isRfid = string.Equals(req.Kind, "RFID", StringComparison.OrdinalIgnoreCase) || !string.IsNullOrWhiteSpace(req.TagId);
        if (isRfid && string.IsNullOrWhiteSpace(req.TagId) && string.IsNullOrWhiteSpace(req.ScannedValue))
            return "RFID tag id or scanned value is required.";
        if (!isRfid && string.IsNullOrWhiteSpace(req.ScannedValue)) return "Barcode scanned value is required.";
        if (TooLong(req.ScannedValue, 255) || TooLong(req.TagId, 120) || TooLong(req.ScannerId, 80)
            || TooLong(req.ReaderId, 80) || TooLong(req.EventType, 40) || TooLong(req.Status, 30)
            || TooLong(req.Notes, 4000)) return "One or more scan fields exceed their maximum length.";
        return null;
    }

    private static bool TooLong(string? value, int max) => value?.Trim().Length > max;

    private static bool Allowed(string? value, params string[] allowed)
        => string.IsNullOrWhiteSpace(value) || allowed.Contains(value.Trim(), StringComparer.OrdinalIgnoreCase);

    private static string? ValidateJsonObject(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        if (value.Length > 16_384) return "Metadata JSON cannot exceed 16384 characters.";
        try
        {
            using var document = JsonDocument.Parse(value);
            return document.RootElement.ValueKind == JsonValueKind.Object ? null : "Metadata JSON must be an object.";
        }
        catch (JsonException)
        {
            return "Metadata JSON is invalid.";
        }
    }

    private static IReadOnlyCollection<string> ParseCities(string? citiesJson)
    {
        if (string.IsNullOrWhiteSpace(citiesJson)) return [];
        try { return JsonSerializer.Deserialize<string[]>(citiesJson) ?? []; }
        catch { return []; }
    }
}

// ── Request DTOs (camelCase JSON binds via default web serializer) ──
public record TemperatureDeviceRequest(string? DeviceCode, string? Name, long? ZoneId, long? ShipmentId, string? VehicleNumber, string? Status, decimal? LastReportedTemperatureCelsius, decimal? BatteryPercent, DateTime? LastPingAtUtc, string? Notes, string? SourceChannel = null, string? ClientGeneratedId = null, string? IdempotencyKey = null, string? CorrelationId = null, string? CausationId = null, string? MetadataJson = null);
public record TemperatureReadingRequest(long DeviceId, long? ShipmentId, long? ZoneId, decimal TemperatureCelsius, decimal? HumidityPercent, decimal? Latitude, decimal? Longitude, string? Source, string? Status, string? Notes, string? SourceChannel = null, string? ClientGeneratedId = null, string? IdempotencyKey = null, string? CorrelationId = null, string? CausationId = null, string? MetadataJson = null);
public record TemperatureAlertResolveRequest(string? ResolutionNotes);
public record AssetRequest(long AssetTypeId, string? AssetTag, string? Name, string? Status, string? CurrentLocation, string? Condition, bool? IsReturnable, decimal? Quantity, string? UnitOfMeasure, string? Notes, DateTime? LastSeenAtUtc);
public record AssetTypeRequest(string? Code, string? Name, string? Description, bool? IsReturnable);
public record AssetAssignmentRequest(long? ShipmentId, long? CarrierId, string? AssigneeType, string? AssigneeName, decimal? Quantity, string? Status, string? CurrentLocation, DateTime? ReleasedAtUtc, string? Notes);
public record AssetMovementRequest(string? Location, string? Condition, string? Notes, long? ShipmentId, long? CarrierId, string? AssigneeType, string? AssigneeName, decimal? Quantity);
public record AssetScanRequest(string? Kind, long? AssetId, long? ShipmentId, string? ScannedValue, string? TagId, string? ScannerId, string? ReaderId, string? EventType, string? Status, string? Notes);
public record FleetReadinessDocumentRequest(string Kind, string SubjectType, string? SubjectId, string SubjectName, string DocumentType, string? DocumentNumber, string? TransportDocumentNo, string? PermitNo, string? VATNumber, string? CommercialRegistrationNo, string? CountryCode, string? NationalAddressBuildingNo, string? NationalAddressAdditionalNo, string? District, string? City, string? Region, string? PostalCode, string? DocumentStatus, DateOnly? IssueDate, DateOnly? HijriExpiryDate, DateOnly? GregorianExpiryDate, string? Notes, bool RequiresExpiry = true);
