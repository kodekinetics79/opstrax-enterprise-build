using System.Globalization;
using System.Linq;
using System.Text.Json;
using Opstrax.Api.Data;
using Opstrax.Api.Foundation;

namespace Opstrax.Api.Services;

public sealed class TelemetryLiveStateService(Database db)
{
    public async Task RefreshVehicleAsync(long companyId, long vehicleId, CancellationToken ct = default)
    {
        var row = await LoadCurrentVehicleRowAsync(companyId, vehicleId, ct);
        if (row is null)
        {
            return;
        }

        var staleSeconds = row.TryGetValue("staleSeconds", out var staleRaw) && staleRaw is not null
            ? Convert.ToInt64(staleRaw, CultureInfo.InvariantCulture)
            : 0L;
        var openAlerts = row.TryGetValue("openAlertCount", out var openAlertRaw) && openAlertRaw is not null
            ? Convert.ToInt32(openAlertRaw, CultureInfo.InvariantCulture)
            : 0;
        var alertCount = row.TryGetValue("alertCount", out var alertRaw) && alertRaw is not null
            ? Convert.ToInt32(alertRaw, CultureInfo.InvariantCulture)
            : 0;
        var speedMph = row.TryGetValue("speedMph", out var speedRaw) && speedRaw is not null
            ? Convert.ToDecimal(speedRaw, CultureInfo.InvariantCulture)
            : 0m;
        var speedThreshold = await GetRuleThresholdAsync(companyId, "speeding", 65m, ct);
        var staleThreshold = await GetRuleThresholdAsync(companyId, "stale_device", 900m, ct);

        var telemetryStatus = staleSeconds > (long)staleThreshold
            ? "stale"
            : openAlerts > 0
                ? "watch"
                : speedMph > speedThreshold
                    ? "watch"
                    : "healthy";
        var riskLevel = staleSeconds > (long)staleThreshold
            ? "high"
            : openAlerts > 2 || speedMph > speedThreshold
                ? "medium"
                : "low";
        var nextAction = telemetryStatus switch
        {
            "stale" => "Check device heartbeat and field power",
            "watch" when speedMph > speedThreshold => "Review speeding and driver coaching",
            "watch" when openAlerts > 0 => "Review open telemetry alerts",
            _ => "No action required"
        };

        var summary = JsonSerializer.Serialize(new
        {
            companyId,
            vehicleId,
            telemetryStatus,
            riskLevel,
            alertCount,
            openAlerts,
            staleSeconds,
            nextAction,
            lastAlertType = row.TryGetValue("lastAlertType", out var lastAlertRaw) ? lastAlertRaw?.ToString() : null,
            vehicleCode = row.TryGetValue("vehicleCode", out var vehicleCodeRaw) ? vehicleCodeRaw?.ToString() : null,
            deviceSerial = row.TryGetValue("deviceSerial", out var deviceSerialRaw) ? deviceSerialRaw?.ToString() : null,
            driverName = row.TryGetValue("driverName", out var driverNameRaw) ? driverNameRaw?.ToString() : null,
        });

        await db.ExecuteAsync(
            @"INSERT INTO telemetry_live_asset_states
                (company_id, vehicle_id, device_id, driver_id, vehicle_code, device_serial, driver_name,
                 lat, lng, speed_mph, heading, engine_status, telemetry_status, risk_level,
                 alert_count, open_alert_count, stale_seconds, last_event_time, received_at,
                 source_event_id, correlation_id, causation_id, source_channel, next_action,
                 summary_json, updated_at)
              VALUES
                (@companyId, @vehicleId, @deviceId, @driverId, @vehicleCode, @deviceSerial, @driverName,
                 @lat, @lng, @speedMph, @heading, @engineStatus, @telemetryStatus, @riskLevel,
                 @alertCount, @openAlertCount, @staleSeconds, @lastEventTime, @receivedAt,
                 @sourceEventId, @correlationId, @causationId, @sourceChannel, @nextAction,
                 COALESCE(@summary::jsonb, '{}'::jsonb), NOW())
              ON CONFLICT (company_id, vehicle_id) DO UPDATE SET
                device_id=EXCLUDED.device_id,
                driver_id=EXCLUDED.driver_id,
                vehicle_code=EXCLUDED.vehicle_code,
                device_serial=EXCLUDED.device_serial,
                driver_name=EXCLUDED.driver_name,
                lat=EXCLUDED.lat,
                lng=EXCLUDED.lng,
                speed_mph=EXCLUDED.speed_mph,
                heading=EXCLUDED.heading,
                engine_status=EXCLUDED.engine_status,
                telemetry_status=EXCLUDED.telemetry_status,
                risk_level=EXCLUDED.risk_level,
                alert_count=EXCLUDED.alert_count,
                open_alert_count=EXCLUDED.open_alert_count,
                stale_seconds=EXCLUDED.stale_seconds,
                last_event_time=EXCLUDED.last_event_time,
                received_at=EXCLUDED.received_at,
                source_event_id=EXCLUDED.source_event_id,
                correlation_id=EXCLUDED.correlation_id,
                causation_id=EXCLUDED.causation_id,
                source_channel=EXCLUDED.source_channel,
                next_action=EXCLUDED.next_action,
                summary_json=EXCLUDED.summary_json,
                updated_at=NOW()",
            c =>
            {
                c.Parameters.AddWithValue("@companyId", companyId);
                c.Parameters.AddWithValue("@vehicleId", vehicleId);
                c.Parameters.AddWithValue("@deviceId", Value(row, "deviceId", "device_id") ?? DBNull.Value);
                c.Parameters.AddWithValue("@driverId", Value(row, "driverId", "driver_id") ?? DBNull.Value);
                c.Parameters.AddWithValue("@vehicleCode", Value(row, "vehicleCode", "vehicle_code") ?? DBNull.Value);
                c.Parameters.AddWithValue("@deviceSerial", Value(row, "deviceSerial", "device_serial") ?? DBNull.Value);
                c.Parameters.AddWithValue("@driverName", Value(row, "driverName", "driver_name") ?? DBNull.Value);
                c.Parameters.AddWithValue("@lat", Value(row, "lat") ?? DBNull.Value);
                c.Parameters.AddWithValue("@lng", Value(row, "lng") ?? DBNull.Value);
                c.Parameters.AddWithValue("@speedMph", Value(row, "speedMph", "speed_mph") ?? 0m);
                c.Parameters.AddWithValue("@heading", Value(row, "heading") ?? 0);
                c.Parameters.AddWithValue("@engineStatus", Value(row, "engineStatus", "engine_status") ?? DBNull.Value);
                c.Parameters.AddWithValue("@telemetryStatus", telemetryStatus);
                c.Parameters.AddWithValue("@riskLevel", riskLevel);
                c.Parameters.AddWithValue("@alertCount", alertCount);
                c.Parameters.AddWithValue("@openAlertCount", openAlerts);
                c.Parameters.AddWithValue("@staleSeconds", staleSeconds);
                c.Parameters.AddWithValue("@lastEventTime", Value(row, "eventTime", "event_time") ?? DateTimeOffset.UtcNow);
                c.Parameters.AddWithValue("@receivedAt", Value(row, "receivedAt", "received_at") ?? DateTimeOffset.UtcNow);
                c.Parameters.AddWithValue("@sourceEventId", Value(row, "sourceEventId", "source_event_id") ?? DBNull.Value);
                c.Parameters.AddWithValue("@correlationId", Value(row, "correlationId", "correlation_id") ?? DBNull.Value);
                c.Parameters.AddWithValue("@causationId", Value(row, "causationId", "causation_id") ?? DBNull.Value);
                c.Parameters.AddWithValue("@sourceChannel", Value(row, "sourceChannel", "source_channel") ?? "device");
                c.Parameters.AddWithValue("@nextAction", nextAction);
                c.Parameters.AddWithValue("@summary", summary);
            }, ct);
    }

    public async Task RefreshCompanyAsync(long companyId, CancellationToken ct = default)
    {
        var vehicleIds = await db.QueryAsync(
            "SELECT DISTINCT vehicle_id FROM latest_vehicle_positions WHERE company_id=@cid AND vehicle_id IS NOT NULL",
            c => c.Parameters.AddWithValue("@cid", companyId), ct);

        foreach (var row in vehicleIds)
        {
            if (row.TryGetValue("vehicle_id", out var vehicleIdRaw) && vehicleIdRaw is not null)
            {
                await RefreshVehicleAsync(companyId, Convert.ToInt64(vehicleIdRaw, CultureInfo.InvariantCulture), ct);
            }
        }
    }

    public async Task<List<Dictionary<string, object?>>> ListLiveStatesAsync(long companyId, CancellationToken ct = default)
    {
        var rows = await db.QueryAsync(
            @"SELECT lsa.*, 
                     EXTRACT(EPOCH FROM (NOW() - lsa.received_at))::BIGINT seconds_since_ping
              FROM telemetry_live_asset_states lsa
              WHERE lsa.company_id=@cid
              ORDER BY CASE lsa.risk_level WHEN 'high' THEN 0 WHEN 'medium' THEN 1 WHEN 'low' THEN 2 ELSE 3 END,
                       lsa.open_alert_count DESC,
                       lsa.updated_at DESC",
            c => c.Parameters.AddWithValue("@cid", companyId), ct);
        return rows.ToList();
    }

    public async Task<Dictionary<string, object?>?> GetLiveStateAsync(long companyId, long vehicleId, CancellationToken ct = default)
    {
        var row = await db.QuerySingleAsync(
            @"SELECT lsa.*, 
                     EXTRACT(EPOCH FROM (NOW() - lsa.received_at))::BIGINT seconds_since_ping
              FROM telemetry_live_asset_states lsa
              WHERE lsa.company_id=@cid AND lsa.vehicle_id=@vid
              LIMIT 1",
            c =>
            {
                c.Parameters.AddWithValue("@cid", companyId);
                c.Parameters.AddWithValue("@vid", vehicleId);
            }, ct);
        return row;
    }

    public async Task<List<Dictionary<string, object?>>> ListDevicesAsync(long companyId, CancellationToken ct = default)
    {
        var rows = await db.QueryAsync(
            @"SELECT e.id, e.device_serial, e.device_model, e.provider, e.status,
                     e.vehicle_id, e.driver_id, e.firmware_version,
                     e.last_seen_at, e.revoked_at, e.created_at,
                     v.vehicle_code, d.full_name driver_name,
                     lsa.telemetry_status, lsa.risk_level, lsa.open_alert_count,
                     lsa.alert_count, lsa.stale_seconds, lsa.next_action, lsa.updated_at live_state_updated_at,
                     EXTRACT(EPOCH FROM (NOW() - e.last_seen_at))::BIGINT seconds_since_ping
              FROM eld_devices e
              LEFT JOIN vehicles v ON v.id=e.vehicle_id
              LEFT JOIN drivers d ON d.id=e.driver_id
              LEFT JOIN telemetry_live_asset_states lsa
                ON lsa.company_id=e.company_id AND lsa.vehicle_id=e.vehicle_id
              WHERE e.company_id=@cid AND e.deleted_at IS NULL
              ORDER BY e.device_serial",
            c => c.Parameters.AddWithValue("@cid", companyId), ct);
        return rows.ToList();
    }

    public async Task<Dictionary<string, object?>?> GetDeviceAsync(long companyId, long id, CancellationToken ct = default)
    {
        var row = await db.QuerySingleAsync(
            @"SELECT e.id, e.device_serial, e.device_model, e.provider, e.status,
                     e.vehicle_id, e.driver_id, e.firmware_version,
                     e.last_seen_at, e.revoked_at, e.created_at,
                     v.vehicle_code, d.full_name driver_name,
                     lsa.telemetry_status, lsa.risk_level, lsa.open_alert_count,
                     lsa.alert_count, lsa.stale_seconds, lsa.next_action, lsa.updated_at live_state_updated_at,
                     EXTRACT(EPOCH FROM (NOW() - e.last_seen_at))::BIGINT seconds_since_ping
              FROM eld_devices e
              LEFT JOIN vehicles v ON v.id=e.vehicle_id
              LEFT JOIN drivers d ON d.id=e.driver_id
              LEFT JOIN telemetry_live_asset_states lsa
                ON lsa.company_id=e.company_id AND lsa.vehicle_id=e.vehicle_id
              WHERE e.company_id=@cid AND e.id=@id AND e.deleted_at IS NULL
              LIMIT 1",
            c =>
            {
                c.Parameters.AddWithValue("@cid", companyId);
                c.Parameters.AddWithValue("@id", id);
            }, ct);
        return row;
    }

    public async Task<Dictionary<string, object?>> BuildSummaryAsync(long companyId, CancellationToken ct = default)
    {
        try
        {
            var states = await ListLiveStatesAsync(companyId, ct);
            var devices = await ListDevicesAsync(companyId, ct);
            var alerts = await db.QueryAsync(
                @"SELECT ta.id, ta.alert_type, ta.severity, ta.message, ta.status,
                         ta.source_event_id, ta.correlation_id, ta.causation_id, ta.created_at, ta.updated_at,
                         v.vehicle_code, d.full_name driver_name, e.device_serial
                  FROM telemetry_alerts ta
                  LEFT JOIN vehicles v ON v.id=ta.vehicle_id
                  LEFT JOIN drivers d ON d.id=ta.driver_id
                  LEFT JOIN eld_devices e ON e.id=ta.device_id
                  WHERE ta.company_id=@cid AND ta.status='Open'
                  ORDER BY ta.created_at DESC
                  LIMIT 50",
                c => c.Parameters.AddWithValue("@cid", companyId), ct);
            var rules = await db.QueryAsync(
                @"SELECT id, rule_type, threshold_value, severity, enabled, notes, created_at, updated_at
                  FROM telemetry_rules
                  WHERE company_id=@cid
                  ORDER BY rule_type",
                c => c.Parameters.AddWithValue("@cid", companyId), ct);
            var geofences = await db.QueryAsync(
                @"SELECT g.id, g.name, g.status, g.center_lat, g.center_lng, g.radius_meters,
                         (SELECT COUNT(*) FROM geofence_events ge WHERE ge.geofence_id=g.id) event_count,
                         (SELECT COUNT(*) FROM geofence_events ge WHERE ge.geofence_id=g.id AND ge.event_time::date=CURRENT_DATE) events_today
                  FROM geofences g
                  WHERE g.company_id=@cid
                  ORDER BY g.name",
                c => c.Parameters.AddWithValue("@cid", companyId), ct);
            var recommendations = await db.QueryAsync(
                @"SELECT id,
                         module_key AS recommendation_type,
                         title,
                         COALESCE(body, description) AS summary,
                         score AS confidence_score,
                         score AS urgency_score,
                         COALESCE(priority, 'medium') AS risk_level,
                         status,
                         correlation_id AS source_event_id,
                         action_label AS actor_type,
                         action_type AS actor_id,
                         NULL::timestamptz AS created_at
                  FROM ai_recommendations
                  WHERE company_id=@tenantId AND (module_key LIKE 'telemetry.%' OR module_key IN ('control-tower', 'command-center', 'dispatch'))
                  ORDER BY id DESC
                  LIMIT 12",
                c => c.Parameters.AddWithValue("@tenantId", companyId), ct);

            var entities = BuildEntities(states);
            if (entities.Count == 0)
            {
                entities = BuildEntitiesFromDevices(devices);
            }

            var kpis = BuildKpis(states, devices, alerts);

            return new Dictionary<string, object?>
            {
                ["kpis"] = kpis,
                ["entities"] = entities,
                ["deviceRegistry"] = devices,
                ["alerts"] = alerts,
                ["riskRules"] = rules,
                ["geofences"] = geofences,
                ["recommendations"] = recommendations,
                ["mobileReadiness"] = BuildMobileReadiness(),
                ["source"] = "telemetry.live-state",
                ["asOf"] = DateTimeOffset.UtcNow,
            };
        }
        catch (Exception ex)
        {
            return new Dictionary<string, object?>
            {
                ["kpis"] = BuildKpis(new List<Dictionary<string, object?>>(), new List<Dictionary<string, object?>>(), new List<Dictionary<string, object?>>()),
                ["entities"] = new List<Dictionary<string, object?>>(),
                ["deviceRegistry"] = new List<Dictionary<string, object?>>(),
                ["alerts"] = new List<Dictionary<string, object?>>(),
                ["riskRules"] = new List<Dictionary<string, object?>>(),
                ["geofences"] = new List<Dictionary<string, object?>>(),
                ["recommendations"] = new List<Dictionary<string, object?>>(),
                ["mobileReadiness"] = BuildMobileReadiness(),
                ["source"] = "telemetry.live-state",
                ["asOf"] = DateTimeOffset.UtcNow,
                ["error"] = "Telemetry live-map summary unavailable",
                ["errorDetail"] = ex.Message,
            };
        }
    }

    private async Task<Dictionary<string, object?>?> LoadCurrentVehicleRowAsync(long companyId, long vehicleId, CancellationToken ct)
    {
        return await db.QuerySingleAsync(
            @"SELECT lvp.company_id, lvp.vehicle_id, lvp.device_id, lvp.driver_id,
                     lvp.lat, lvp.lng, lvp.speed_mph, lvp.heading,
                     lvp.accuracy_meters, lvp.engine_status, lvp.fuel_level, lvp.odometer_miles,
                     lvp.battery_voltage, lvp.event_time, lvp.received_at, lvp.event_count,
                     v.vehicle_code, d.full_name driver_name, e.device_serial,
                     COALESCE((SELECT COUNT(*) FROM telemetry_alerts ta WHERE ta.company_id=@cid AND ta.vehicle_id=@vid), 0) alert_count,
                     COALESCE((SELECT COUNT(*) FROM telemetry_alerts ta WHERE ta.company_id=@cid AND ta.vehicle_id=@vid AND ta.status='Open'), 0) open_alert_count,
                     COALESCE((SELECT ta.alert_type FROM telemetry_alerts ta WHERE ta.company_id=@cid AND ta.vehicle_id=@vid ORDER BY ta.created_at DESC LIMIT 1), 'clear') last_alert_type,
                     EXTRACT(EPOCH FROM (NOW() - lvp.received_at))::BIGINT stale_seconds
              FROM latest_vehicle_positions lvp
              LEFT JOIN vehicles v ON v.id=lvp.vehicle_id
              LEFT JOIN drivers d ON d.id=lvp.driver_id
              LEFT JOIN eld_devices e ON e.id=lvp.device_id
              WHERE lvp.company_id=@cid AND lvp.vehicle_id=@vid
              LIMIT 1",
            c =>
            {
                c.Parameters.AddWithValue("@cid", companyId);
                c.Parameters.AddWithValue("@vid", vehicleId);
            }, ct);
    }

    private async Task<decimal> GetRuleThresholdAsync(long companyId, string ruleType, decimal fallback, CancellationToken ct)
    {
        var value = await db.ScalarDecimalAsync(
            "SELECT threshold_value FROM telemetry_rules WHERE company_id=@cid AND rule_type=@type AND enabled=TRUE LIMIT 1",
            c =>
            {
                c.Parameters.AddWithValue("@cid", companyId);
                c.Parameters.AddWithValue("@type", ruleType);
            }, ct);
        return value ?? fallback;
    }

    private static IReadOnlyList<Dictionary<string, object?>> BuildEntities(List<Dictionary<string, object?>> states)
    {
        return states.Select(state => new Dictionary<string, object?>
        {
            ["id"] = Value(state, "vehicle_id", "vehicleId", "id"),
            ["vehicleId"] = Value(state, "vehicle_id", "vehicleId", "id"),
            ["deviceId"] = Value(state, "device_id", "deviceId", "id"),
            ["driverId"] = Value(state, "driver_id", "driverId"),
            ["label"] = Value(state, "vehicle_code", "vehicleCode") ?? "Unknown vehicle",
            ["vehicleCode"] = Value(state, "vehicle_code", "vehicleCode"),
            ["driverName"] = Value(state, "driver_name", "driverName"),
            ["deviceSerial"] = Value(state, "device_serial", "deviceSerial"),
            ["lat"] = Value(state, "lat"),
            ["lng"] = Value(state, "lng"),
            ["speedMph"] = Value(state, "speed_mph", "speedMph"),
            ["heading"] = Value(state, "heading"),
            ["secondsSincePing"] = Value(state, "seconds_since_ping", "secondsSincePing"),
            ["isStale"] = string.Equals(Value(state, "telemetry_status", "telemetryStatus")?.ToString(), "stale", StringComparison.OrdinalIgnoreCase),
            ["telemetryStatus"] = Value(state, "telemetry_status", "telemetryStatus"),
            ["riskLevel"] = Value(state, "risk_level", "riskLevel"),
            ["liveAlert"] = Value(state, "next_action", "nextAction"),
            ["openAlertCount"] = Value(state, "open_alert_count", "openAlertCount"),
            ["alertCount"] = Value(state, "alert_count", "alertCount"),
            ["lastEventAt"] = Value(state, "last_event_time", "lastEventAt"),
            ["receivedAt"] = Value(state, "received_at", "receivedAt"),
            ["updatedAt"] = Value(state, "updated_at", "updatedAt"),
            ["summary"] = Value(state, "summary_json", "summary"),
        }).ToList();
    }

    private static IReadOnlyList<Dictionary<string, object?>> BuildEntitiesFromDevices(List<Dictionary<string, object?>> devices)
    {
        return devices.Select(device => new Dictionary<string, object?>
        {
            ["id"] = Value(device, "vehicle_id", "vehicleId", "id"),
            ["vehicleId"] = Value(device, "vehicle_id", "vehicleId", "id"),
            ["deviceId"] = Value(device, "id", "device_id", "deviceId"),
            ["driverId"] = Value(device, "driver_id", "driverId"),
            ["label"] = Value(device, "vehicle_code", "vehicleCode", "device_serial", "deviceSerial") ?? "Unknown vehicle",
            ["vehicleCode"] = Value(device, "vehicle_code", "vehicleCode"),
            ["driverName"] = Value(device, "driver_name", "driverName"),
            ["deviceSerial"] = Value(device, "device_serial", "deviceSerial"),
            ["lat"] = null,
            ["lng"] = null,
            ["speedMph"] = 0m,
            ["heading"] = null,
            ["secondsSincePing"] = Value(device, "seconds_since_ping", "secondsSincePing"),
            ["isStale"] = false,
        }).ToList();
    }

    private static Dictionary<string, object?> BuildKpis(List<Dictionary<string, object?>> states, List<Dictionary<string, object?>> devices, List<Dictionary<string, object?>> alerts)
    {
        var highRisk = states.Count(row => string.Equals(row.GetValueOrDefault("risk_level")?.ToString(), "high", StringComparison.OrdinalIgnoreCase));
        var watch = states.Count(row => string.Equals(row.GetValueOrDefault("risk_level")?.ToString(), "medium", StringComparison.OrdinalIgnoreCase));
        var healthy = states.Count(row => string.Equals(row.GetValueOrDefault("telemetry_status")?.ToString(), "healthy", StringComparison.OrdinalIgnoreCase));
        var stale = states.Count(row => string.Equals(row.GetValueOrDefault("telemetry_status")?.ToString(), "stale", StringComparison.OrdinalIgnoreCase));
        return new Dictionary<string, object?>
        {
            ["liveUnits"] = states.Count,
            ["registeredDevices"] = devices.Count,
            ["openAlerts"] = alerts.Count,
            ["highRiskUnits"] = highRisk,
            ["watchUnits"] = watch,
            ["healthyUnits"] = healthy,
            ["staleUnits"] = stale,
            ["liveCoverage"] = states.Count == 0 ? 0 : Math.Round((decimal)healthy / states.Count * 100m, 1),
            ["asOf"] = DateTimeOffset.UtcNow,
        };
    }

    private static List<Dictionary<string, object?>> BuildMobileReadiness()
    {
        return
        [
            new Dictionary<string, object?>
            {
                ["role"] = "Driver / Operator",
                ["routeFamilies"] = new[] { "/driver/*", "/proof-of-delivery", "/last-mile-delivery" },
                ["permissions"] = new[] { "driver:self", "operations.proof.read", "operations.proof.submit" },
                ["offlineIdempotency"] = "Supported via existing session + client-generated IDs",
                ["metadataReadiness"] = "Evidence/location/device metadata can be carried in telemetry and proof payloads",
                ["futureNotificationEvents"] = new[] { "telemetry.alert.created", "proof.submitted", "assignment.updated" },
            },
            new Dictionary<string, object?>
            {
                ["role"] = "Field Worker / Cleaner / Technician / Guard",
                ["routeFamilies"] = new[] { "/operations/proof-center", "/iot-devices", "/alerts" },
                ["permissions"] = new[] { "operations.execution_summary.read", "telemetry.devices.read", "alerts:view" },
                ["offlineIdempotency"] = "Supported through existing idempotent API patterns",
                ["metadataReadiness"] = "Location, device and evidence metadata are retained in the telemetry projection",
                ["futureNotificationEvents"] = new[] { "telemetry.device.status.changed", "telemetry.alert.created" },
            },
            new Dictionary<string, object?>
            {
                ["role"] = "Dispatcher / Supervisor",
                ["routeFamilies"] = new[] { "/map-view", "/dispatch", "/alerts" },
                ["permissions"] = new[] { "telemetry.live_state.read", "dispatch:view", "alerts:view" },
                ["offlineIdempotency"] = "Supported for retry-safe reads and ack/resolve actions",
                ["metadataReadiness"] = "Live state exposes device, driver, route and risk metadata",
                ["futureNotificationEvents"] = new[] { "telemetry.live_state.changed", "telemetry.alert.created" },
            },
            new Dictionary<string, object?>
            {
                ["role"] = "Warehouse User",
                ["routeFamilies"] = new[] { "/operations/proof-center" },
                ["permissions"] = new[] { "operations.execution_summary.read", "operations.warehouse_handover.read" },
                ["offlineIdempotency"] = "Supported for read-heavy mobile scanning flows",
                ["metadataReadiness"] = "Handover and proof metadata already flow through the operational foundation",
                ["futureNotificationEvents"] = new[] { "warehouse.handover.completed", "proof.validation.changed" },
            },
            new Dictionary<string, object?>
            {
                ["role"] = "Third-Party Pickup User",
                ["routeFamilies"] = new[] { "/operations/proof-center" },
                ["permissions"] = new[] { "operations.pickup_authorization.read", "operations.pickup_authorization.verify" },
                ["offlineIdempotency"] = "Supported for verification retries",
                ["metadataReadiness"] = "Pickup authorizations keep issuer, validity and verification metadata",
                ["futureNotificationEvents"] = new[] { "pickup.authorization.created", "pickup.authorization.verified" },
            },
            new Dictionary<string, object?>
            {
                ["role"] = "Customer / Client User",
                ["routeFamilies"] = new[] { "/customer-portal", "/customer-eta", "/customer-visibility" },
                ["permissions"] = new[] { "customer_portal:view", "operations.proof.read" },
                ["offlineIdempotency"] = "Read-only mobile/web flows are safe under retry",
                ["metadataReadiness"] = "Customer-safe views stay scoped and hide internal-only fields",
                ["futureNotificationEvents"] = new[] { "customer.eta.updated", "proof.shared" },
            },
        ];
    }

    private static object? Value(Dictionary<string, object?> row, params string[] keys)
    {
        foreach (var key in keys)
        {
            if (row.TryGetValue(key, out var value) && value is not null)
            {
                return value;
            }
        }

        return null;
    }
}
