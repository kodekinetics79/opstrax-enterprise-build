using System.Globalization;
using System.Text.Json;
using Opstrax.Api.Controllers;
using Opstrax.Api.Data;

namespace Opstrax.Api.Services;

public sealed record ColdChainPolicyRecord(
    long Id,
    long CompanyId,
    string PolicyCode,
    string ScopeType,
    string ScopeKey,
    decimal? MinCelsius,
    decimal? MaxCelsius,
    decimal? HumidityMinPercent,
    decimal? HumidityMaxPercent,
    string Severity,
    bool RequiresAcknowledgement,
    string Status,
    string? SourceChannel,
    string? ClientGeneratedId,
    string? IdempotencyKey,
    string? CorrelationId,
    string? CausationId,
    string? MetadataJson,
    string? Notes,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset? UpdatedAtUtc);

public sealed class FleetTmsColdChainFoundationService(Database db)
{
    public async Task<IReadOnlyList<ColdChainPolicyRecord>> ListPoliciesAsync(long companyId, CancellationToken ct = default)
    {
        var rows = await db.QueryAsync(
            @"SELECT *
              FROM fleet_tms_cold_chain_policies
              WHERE company_id=@companyId
              ORDER BY status DESC, scope_type, scope_key, policy_code",
            c => c.Parameters.AddWithValue("@companyId", companyId), ct);
        return rows.Select(MapPolicy).ToList();
    }

    public async Task<ColdChainPolicyRecord> UpsertPolicyAsync(
        long companyId,
        string policyCode,
        string scopeType,
        string scopeKey,
        decimal? minCelsius,
        decimal? maxCelsius,
        decimal? humidityMinPercent,
        decimal? humidityMaxPercent,
        string? severity,
        bool requiresAcknowledgement,
        string? status,
        string? sourceChannel,
        string? clientGeneratedId,
        string? idempotencyKey,
        string? correlationId,
        string? causationId,
        string? metadataJson,
        string? notes,
        CancellationToken ct = default)
    {
        if (!string.IsNullOrWhiteSpace(idempotencyKey))
        {
            var existing = await db.QuerySingleAsync(
                @"SELECT *
                  FROM fleet_tms_cold_chain_policies
                  WHERE company_id=@companyId AND idempotency_key=@idempotencyKey
                  LIMIT 1",
                c =>
                {
                    c.Parameters.AddWithValue("@companyId", companyId);
                    c.Parameters.AddWithValue("@idempotencyKey", idempotencyKey);
                }, ct);
            if (existing is not null)
            {
                return MapPolicy(existing);
            }
        }

        var normalizedCode = Normalize(policyCode, $"CCP-{DateTimeOffset.UtcNow.ToUnixTimeSeconds()}");
        var normalizedScopeType = Normalize(scopeType, "default");
        var normalizedScopeKey = Normalize(scopeKey, "");
        var effectiveSeverity = Normalize(severity, "High");
        var effectiveStatus = Normalize(status, "Active");
        var now = DateTimeOffset.UtcNow;

        await db.ExecuteAsync(
            @"INSERT INTO fleet_tms_cold_chain_policies
                (company_id, policy_code, scope_type, scope_key, min_celsius, max_celsius, humidity_min_percent, humidity_max_percent,
                 severity, requires_acknowledgement, status, source_channel, client_generated_id, idempotency_key, correlation_id,
                 causation_id, metadata_json, notes, created_at_utc, updated_at_utc)
              VALUES
                (@companyId, @policyCode, @scopeType, @scopeKey, @minCelsius, @maxCelsius, @humidityMinPercent, @humidityMaxPercent,
                 @severity, @requiresAcknowledgement, @status, @sourceChannel, @clientGeneratedId, @idempotencyKey, @correlationId,
                 @causationId, @metadata::jsonb, @notes, @createdAt, @updatedAt)
              ON CONFLICT (company_id, policy_code, scope_type, scope_key)
              DO UPDATE SET
                min_celsius = EXCLUDED.min_celsius,
                max_celsius = EXCLUDED.max_celsius,
                humidity_min_percent = EXCLUDED.humidity_min_percent,
                humidity_max_percent = EXCLUDED.humidity_max_percent,
                severity = EXCLUDED.severity,
                requires_acknowledgement = EXCLUDED.requires_acknowledgement,
                status = EXCLUDED.status,
                source_channel = EXCLUDED.source_channel,
                client_generated_id = EXCLUDED.client_generated_id,
                idempotency_key = COALESCE(EXCLUDED.idempotency_key, fleet_tms_cold_chain_policies.idempotency_key),
                correlation_id = EXCLUDED.correlation_id,
                causation_id = EXCLUDED.causation_id,
                metadata_json = EXCLUDED.metadata_json,
                notes = EXCLUDED.notes,
                updated_at_utc = EXCLUDED.updated_at_utc",
            c =>
            {
                c.Parameters.AddWithValue("@companyId", companyId);
                c.Parameters.AddWithValue("@policyCode", normalizedCode);
                c.Parameters.AddWithValue("@scopeType", normalizedScopeType);
                c.Parameters.AddWithValue("@scopeKey", normalizedScopeKey);
                c.Parameters.AddWithValue("@minCelsius", (object?)minCelsius ?? DBNull.Value);
                c.Parameters.AddWithValue("@maxCelsius", (object?)maxCelsius ?? DBNull.Value);
                c.Parameters.AddWithValue("@humidityMinPercent", (object?)humidityMinPercent ?? DBNull.Value);
                c.Parameters.AddWithValue("@humidityMaxPercent", (object?)humidityMaxPercent ?? DBNull.Value);
                c.Parameters.AddWithValue("@severity", effectiveSeverity);
                c.Parameters.AddWithValue("@requiresAcknowledgement", requiresAcknowledgement);
                c.Parameters.AddWithValue("@status", effectiveStatus);
                c.Parameters.AddWithValue("@sourceChannel", (object?)sourceChannel ?? DBNull.Value);
                c.Parameters.AddWithValue("@clientGeneratedId", (object?)clientGeneratedId ?? DBNull.Value);
                c.Parameters.AddWithValue("@idempotencyKey", (object?)idempotencyKey ?? DBNull.Value);
                c.Parameters.AddWithValue("@correlationId", (object?)correlationId ?? DBNull.Value);
                c.Parameters.AddWithValue("@causationId", (object?)causationId ?? DBNull.Value);
                c.Parameters.AddWithValue("@metadata", string.IsNullOrWhiteSpace(metadataJson) ? "{}" : metadataJson);
                c.Parameters.AddWithValue("@notes", (object?)notes ?? DBNull.Value);
                c.Parameters.AddWithValue("@createdAt", now);
                c.Parameters.AddWithValue("@updatedAt", now);
            }, ct);

        var row = await db.QuerySingleAsync(
            @"SELECT *
              FROM fleet_tms_cold_chain_policies
              WHERE company_id=@companyId AND policy_code=@policyCode AND scope_type=@scopeType AND scope_key=@scopeKey
              LIMIT 1",
            c =>
            {
                c.Parameters.AddWithValue("@companyId", companyId);
                c.Parameters.AddWithValue("@policyCode", normalizedCode);
                c.Parameters.AddWithValue("@scopeType", normalizedScopeType);
                c.Parameters.AddWithValue("@scopeKey", normalizedScopeKey);
            }, ct);

        return row is null ? throw new InvalidOperationException("Cold-chain policy could not be loaded after save") : MapPolicy(row);
    }

    public async Task<Dictionary<string, object?>> RecordTemperatureReadingAsync(
        long companyId,
        TemperatureReadingRequest req,
        CancellationToken ct = default)
    {
        if (req.DeviceId <= 0)
            throw new InvalidOperationException("Temperature device id is required.");

        var device = await db.QuerySingleAsync(
            @"SELECT *
              FROM fleet_tms_temperature_devices
              WHERE company_id=@companyId AND id=@id
              LIMIT 1",
            c =>
            {
                c.Parameters.AddWithValue("@companyId", companyId);
                c.Parameters.AddWithValue("@id", req.DeviceId);
            }, ct);

        if (device is null)
            throw new InvalidOperationException("Temperature device not found for this tenant.");

        if (!string.IsNullOrWhiteSpace(req.IdempotencyKey))
        {
            var existing = await db.QuerySingleAsync(
                @"SELECT *
                  FROM fleet_tms_temperature_readings
                  WHERE company_id=@companyId AND idempotency_key=@idempotencyKey
                  LIMIT 1",
                c =>
                {
                    c.Parameters.AddWithValue("@companyId", companyId);
                    c.Parameters.AddWithValue("@idempotencyKey", req.IdempotencyKey);
                }, ct);
            if (existing is not null)
            {
                return existing;
            }
        }

        var zoneId = req.ZoneId ?? (device["zoneId"] is null or DBNull ? null : Convert.ToInt64(device["zoneId"], CultureInfo.InvariantCulture));
        var shipmentId = req.ShipmentId ?? (device["shipmentId"] is null or DBNull ? null : Convert.ToInt64(device["shipmentId"], CultureInfo.InvariantCulture));
        var vehicleNumber = Convert.ToString(device["vehicleNumber"], CultureInfo.InvariantCulture) ?? string.Empty;
        var zone = zoneId.HasValue
            ? await db.QuerySingleAsync(
                @"SELECT *
                  FROM fleet_tms_temperature_zones
                  WHERE company_id=@companyId AND id=@id
                  LIMIT 1",
                c =>
                {
                    c.Parameters.AddWithValue("@companyId", companyId);
                    c.Parameters.AddWithValue("@id", zoneId.Value);
                }, ct)
            : null;

        var policy = await ResolvePolicyAsync(companyId, zone, shipmentId, vehicleNumber, req, ct);
        var effectiveMin = policy?.MinCelsius ?? (zone is null ? null : DN(zone, "minCelsius"));
        var effectiveMax = policy?.MaxCelsius ?? (zone is null ? null : DN(zone, "maxCelsius"));
        var status = string.IsNullOrWhiteSpace(req.Status) ? "Normal" : req.Status.Trim();
        var isBreach = effectiveMin.HasValue && req.TemperatureCelsius < effectiveMin.Value
            || effectiveMax.HasValue && req.TemperatureCelsius > effectiveMax.Value;
        if (isBreach)
        {
            status = "Breach";
        }

        var readingId = await db.InsertAsync(
            @"INSERT INTO fleet_tms_temperature_readings
                (company_id, device_id, shipment_id, zone_id, temperature_celsius, humidity_percent, latitude, longitude, source, status,
                 notes, source_channel, client_generated_id, idempotency_key, correlation_id, causation_id, metadata_json,
                 applied_policy_code, applied_policy_scope, applied_min_celsius, applied_max_celsius, recorded_at_utc, created_at_utc)
              VALUES
                (@companyId, @device, @shipment, @zone, @temp, @humidity, @lat, @lng, @source, @status, @notes, @sourceChannel,
                 @clientGeneratedId, @idempotencyKey, @correlationId, @causationId, @metadata::jsonb, @policyCode, @policyScope,
                 @policyMin, @policyMax, NOW(), NOW())",
            c =>
            {
                c.Parameters.AddWithValue("@companyId", companyId);
                c.Parameters.AddWithValue("@device", req.DeviceId);
                c.Parameters.AddWithValue("@shipment", (object?)shipmentId ?? DBNull.Value);
                c.Parameters.AddWithValue("@zone", (object?)zoneId ?? DBNull.Value);
                c.Parameters.AddWithValue("@temp", req.TemperatureCelsius);
                c.Parameters.AddWithValue("@humidity", (object?)req.HumidityPercent ?? DBNull.Value);
                c.Parameters.AddWithValue("@lat", (object?)req.Latitude ?? DBNull.Value);
                c.Parameters.AddWithValue("@lng", (object?)req.Longitude ?? DBNull.Value);
                c.Parameters.AddWithValue("@source", string.IsNullOrWhiteSpace(req.Source) ? "Sensor" : req.Source.Trim());
                c.Parameters.AddWithValue("@status", status);
                c.Parameters.AddWithValue("@notes", req.Notes?.Trim() ?? string.Empty);
                c.Parameters.AddWithValue("@sourceChannel", (object?)req.SourceChannel ?? DBNull.Value);
                c.Parameters.AddWithValue("@clientGeneratedId", (object?)req.ClientGeneratedId ?? DBNull.Value);
                c.Parameters.AddWithValue("@idempotencyKey", (object?)req.IdempotencyKey ?? DBNull.Value);
                c.Parameters.AddWithValue("@correlationId", (object?)req.CorrelationId ?? DBNull.Value);
                c.Parameters.AddWithValue("@causationId", (object?)req.CausationId ?? DBNull.Value);
                c.Parameters.AddWithValue("@metadata", string.IsNullOrWhiteSpace(req.MetadataJson) ? "{}" : req.MetadataJson);
                c.Parameters.AddWithValue("@policyCode", (object?)policy?.PolicyCode ?? DBNull.Value);
                c.Parameters.AddWithValue("@policyScope", (object?)policy?.ScopeType ?? DBNull.Value);
                c.Parameters.AddWithValue("@policyMin", (object?)effectiveMin ?? DBNull.Value);
                c.Parameters.AddWithValue("@policyMax", (object?)effectiveMax ?? DBNull.Value);
            }, ct);

        await db.ExecuteAsync(
            @"UPDATE fleet_tms_temperature_devices
              SET last_reported_temperature_celsius=@temp,
                  battery_percent=CASE WHEN battery_percent <= 1 THEN 98 ELSE battery_percent END,
                  last_ping_at_utc=NOW(),
                  shipment_id=COALESCE(@shipment, shipment_id),
                  zone_id=COALESCE(@zone, zone_id),
                  source_channel=COALESCE(@sourceChannel, source_channel),
                  client_generated_id=COALESCE(@clientGeneratedId, client_generated_id),
                  idempotency_key=COALESCE(@idempotencyKey, idempotency_key),
                  correlation_id=COALESCE(@correlationId, correlation_id),
                  causation_id=COALESCE(@causationId, causation_id),
                  metadata_json=COALESCE(@metadata::jsonb, metadata_json),
                  updated_at_utc=NOW()
              WHERE id=@device AND company_id=@companyId",
            c =>
            {
                c.Parameters.AddWithValue("@temp", req.TemperatureCelsius);
                c.Parameters.AddWithValue("@shipment", (object?)shipmentId ?? DBNull.Value);
                c.Parameters.AddWithValue("@zone", (object?)zoneId ?? DBNull.Value);
                c.Parameters.AddWithValue("@sourceChannel", (object?)req.SourceChannel ?? DBNull.Value);
                c.Parameters.AddWithValue("@clientGeneratedId", (object?)req.ClientGeneratedId ?? DBNull.Value);
                c.Parameters.AddWithValue("@idempotencyKey", (object?)req.IdempotencyKey ?? DBNull.Value);
                c.Parameters.AddWithValue("@correlationId", (object?)req.CorrelationId ?? DBNull.Value);
                c.Parameters.AddWithValue("@causationId", (object?)req.CausationId ?? DBNull.Value);
                c.Parameters.AddWithValue("@metadata", string.IsNullOrWhiteSpace(req.MetadataJson) ? "{}" : req.MetadataJson);
                c.Parameters.AddWithValue("@device", req.DeviceId);
                c.Parameters.AddWithValue("@companyId", companyId);
            }, ct);

        if (isBreach)
        {
            var severity = policy?.Severity ?? (req.TemperatureCelsius > (effectiveMax ?? req.TemperatureCelsius) + 2 ? "Critical" : "High");
            await db.ExecuteAsync(
                @"INSERT INTO fleet_tms_temperature_alerts
                    (company_id, device_id, shipment_id, reading_id, alert_type, severity, status, threshold_min, threshold_max,
                     measured_temperature, triggered_at_utc, notes, source_channel, client_generated_id, idempotency_key, correlation_id,
                     causation_id, metadata_json, applied_policy_code, applied_policy_scope)
                  VALUES
                    (@companyId, @device, @shipment, @reading, 'TemperatureBreach', @severity, 'Open', @min, @max, @temp, NOW(),
                     'Breach auto-generated from live temperature reading.', @sourceChannel, @clientGeneratedId, @idempotencyKey,
                     @correlationId, @causationId, @metadata::jsonb, @policyCode, @policyScope)",
                c =>
                {
                    c.Parameters.AddWithValue("@companyId", companyId);
                    c.Parameters.AddWithValue("@device", req.DeviceId);
                    c.Parameters.AddWithValue("@shipment", (object?)shipmentId ?? DBNull.Value);
                    c.Parameters.AddWithValue("@reading", readingId);
                    c.Parameters.AddWithValue("@severity", severity);
                    c.Parameters.AddWithValue("@min", (object?)effectiveMin ?? DBNull.Value);
                    c.Parameters.AddWithValue("@max", (object?)effectiveMax ?? DBNull.Value);
                    c.Parameters.AddWithValue("@temp", req.TemperatureCelsius);
                    c.Parameters.AddWithValue("@sourceChannel", (object?)req.SourceChannel ?? DBNull.Value);
                    c.Parameters.AddWithValue("@clientGeneratedId", (object?)req.ClientGeneratedId ?? DBNull.Value);
                    c.Parameters.AddWithValue("@idempotencyKey", (object?)req.IdempotencyKey ?? DBNull.Value);
                    c.Parameters.AddWithValue("@correlationId", (object?)req.CorrelationId ?? DBNull.Value);
                    c.Parameters.AddWithValue("@causationId", (object?)req.CausationId ?? DBNull.Value);
                    c.Parameters.AddWithValue("@metadata", string.IsNullOrWhiteSpace(req.MetadataJson) ? "{}" : req.MetadataJson);
                    c.Parameters.AddWithValue("@policyCode", (object?)policy?.PolicyCode ?? DBNull.Value);
                    c.Parameters.AddWithValue("@policyScope", (object?)policy?.ScopeType ?? DBNull.Value);
                }, ct);
        }

        await WriteEventAsync(
            companyId,
            "cold_chain.temperature_reading.recorded",
            "temperature_reading",
            readingId.ToString(CultureInfo.InvariantCulture),
            new
            {
                readingId,
                req.DeviceId,
                shipmentId,
                zoneId,
                req.TemperatureCelsius,
                req.HumidityPercent,
                status,
                policyCode = policy?.PolicyCode,
                policyScope = policy?.ScopeType,
                breach = isBreach
            },
            req.CorrelationId,
            req.CausationId,
            req.IdempotencyKey,
            ct);

        if (isBreach)
        {
            await WriteEventAsync(
                companyId,
                "cold_chain.temperature_breach.detected",
                "temperature_reading",
                readingId.ToString(CultureInfo.InvariantCulture),
                new
                {
                    readingId,
                    req.DeviceId,
                    shipmentId,
                    zoneId,
                    req.TemperatureCelsius,
                    effectiveMin,
                    effectiveMax,
                    policyCode = policy?.PolicyCode,
                    policyScope = policy?.ScopeType
                },
                req.CorrelationId,
                req.CausationId,
                req.IdempotencyKey,
                ct);
        }

        var row = await db.QuerySingleAsync(
            @"SELECT *
              FROM fleet_tms_temperature_readings
              WHERE company_id=@companyId AND id=@id
              LIMIT 1",
            c =>
            {
                c.Parameters.AddWithValue("@companyId", companyId);
                c.Parameters.AddWithValue("@id", readingId);
            }, ct);
        return row is null ? throw new InvalidOperationException("Temperature reading could not be loaded after save") : row;
    }

    public async Task<Dictionary<string, object?>> ResolveAlertAsync(long companyId, long id, TemperatureAlertResolveRequest req, string? actor, CancellationToken ct = default)
    {
        var existing = await db.QuerySingleAsync(
            @"SELECT *
              FROM fleet_tms_temperature_alerts
              WHERE company_id=@companyId AND id=@id
              LIMIT 1",
            c =>
            {
                c.Parameters.AddWithValue("@companyId", companyId);
                c.Parameters.AddWithValue("@id", id);
            }, ct);
        if (existing is null)
            throw new InvalidOperationException("Temperature alert not found for this tenant.");

        var notes = req.ResolutionNotes?.Trim() ?? "Resolved by operations.";
        await db.ExecuteAsync(
            @"UPDATE fleet_tms_temperature_alerts
              SET status='Resolved',
                  resolved_at_utc=NOW(),
                  resolved_by=@actor,
                  resolution_notes=@notes,
                  updated_at_utc=NOW()
              WHERE id=@id AND company_id=@companyId",
            c =>
            {
                c.Parameters.AddWithValue("@actor", actor ?? "system");
                c.Parameters.AddWithValue("@notes", notes);
                c.Parameters.AddWithValue("@id", id);
                c.Parameters.AddWithValue("@companyId", companyId);
            }, ct);

        await WriteEventAsync(
            companyId,
            "cold_chain.alert.resolved",
            "temperature_alert",
            id.ToString(CultureInfo.InvariantCulture),
            new { alertId = id, resolvedBy = actor, notes },
            correlationId: null,
            causationId: null,
            idempotencyKey: null,
            ct);

        var row = await db.QuerySingleAsync(
            @"SELECT *
              FROM fleet_tms_temperature_alerts
              WHERE company_id=@companyId AND id=@id
              LIMIT 1",
            c =>
            {
                c.Parameters.AddWithValue("@companyId", companyId);
                c.Parameters.AddWithValue("@id", id);
            }, ct);
        return row is null ? throw new InvalidOperationException("Temperature alert could not be loaded after resolution") : row;
    }

    public async Task<long> WriteEventAsync(
        long companyId,
        string eventType,
        string aggregateType,
        string aggregateId,
        object payload,
        string? correlationId,
        string? causationId,
        string? idempotencyKey,
        CancellationToken ct = default)
    {
        return await db.InsertAsync(
            @"INSERT INTO fleet_tms_cold_chain_event_log
                (company_id, event_type, aggregate_type, aggregate_id, payload_json, correlation_id, causation_id, idempotency_key, status, occurred_at_utc, processed_at_utc, created_at_utc)
              VALUES
                (@companyId, @eventType, @aggregateType, @aggregateId, @payload::jsonb, @correlationId, @causationId, @idempotencyKey, 'processed', NOW(), NOW(), NOW())",
            c =>
            {
                c.Parameters.AddWithValue("@companyId", companyId);
                c.Parameters.AddWithValue("@eventType", eventType);
                c.Parameters.AddWithValue("@aggregateType", aggregateType);
                c.Parameters.AddWithValue("@aggregateId", aggregateId);
                c.Parameters.AddWithValue("@payload", JsonSerializer.Serialize(payload));
                c.Parameters.AddWithValue("@correlationId", (object?)correlationId ?? DBNull.Value);
                c.Parameters.AddWithValue("@causationId", (object?)causationId ?? DBNull.Value);
                c.Parameters.AddWithValue("@idempotencyKey", (object?)idempotencyKey ?? DBNull.Value);
            }, ct);
    }

    private async Task<ColdChainPolicyRecord?> ResolvePolicyAsync(
        long companyId,
        Dictionary<string, object?>? zone,
        long? shipmentId,
        string vehicleNumber,
        TemperatureReadingRequest req,
        CancellationToken ct)
    {
        var candidates = new List<(string scopeType, string scopeKey)>
        {
            ("device", req.DeviceId.ToString(CultureInfo.InvariantCulture)),
            ("shipment", shipmentId?.ToString(CultureInfo.InvariantCulture) ?? string.Empty),
            ("vehicle", vehicleNumber),
            ("zone", zone is null ? string.Empty : Convert.ToString(zone["code"], CultureInfo.InvariantCulture) ?? string.Empty),
            ("default", string.Empty)
        };

        foreach (var (scopeType, scopeKey) in candidates)
        {
            var row = await db.QuerySingleAsync(
                @"SELECT *
                  FROM fleet_tms_cold_chain_policies
                  WHERE company_id=@companyId AND scope_type=@scopeType AND scope_key=@scopeKey AND status='Active'
                  ORDER BY updated_at_utc DESC NULLS LAST, created_at_utc DESC, id DESC
                  LIMIT 1",
                c =>
                {
                    c.Parameters.AddWithValue("@companyId", companyId);
                    c.Parameters.AddWithValue("@scopeType", scopeType);
                    c.Parameters.AddWithValue("@scopeKey", scopeKey);
                }, ct);
            if (row is not null)
            {
                return MapPolicy(row);
            }
        }

        return null;
    }

    private static ColdChainPolicyRecord MapPolicy(Dictionary<string, object?> row) => new(
        L(row, "id"),
        L(row, "companyId"),
        S(row, "policyCode") ?? string.Empty,
        S(row, "scopeType") ?? "default",
        S(row, "scopeKey") ?? string.Empty,
        DN(row, "minCelsius"),
        DN(row, "maxCelsius"),
        DN(row, "humidityMinPercent"),
        DN(row, "humidityMaxPercent"),
        S(row, "severity") ?? "High",
        B(row, "requiresAcknowledgement", true),
        S(row, "status") ?? "Active",
        S(row, "sourceChannel"),
        S(row, "clientGeneratedId"),
        S(row, "idempotencyKey"),
        S(row, "correlationId"),
        S(row, "causationId"),
        S(row, "metadataJson"),
        S(row, "notes"),
        Dto(row, "createdAtUtc"),
        DtoN(row, "updatedAtUtc"));

    private static string Normalize(string? value, string fallback)
        => string.IsNullOrWhiteSpace(value) ? fallback : value.Trim();

    private static string? S(Dictionary<string, object?> row, string key)
        => row.TryGetValue(key, out var value) && value is not null and not DBNull ? Convert.ToString(value, CultureInfo.InvariantCulture) : null;

    private static long L(Dictionary<string, object?> row, string key)
        => row.TryGetValue(key, out var value) && value is not null and not DBNull ? Convert.ToInt64(value, CultureInfo.InvariantCulture) : 0;

    private static decimal? DN(Dictionary<string, object?> row, string key)
        => row.TryGetValue(key, out var value) && value is not null and not DBNull ? Convert.ToDecimal(value, CultureInfo.InvariantCulture) : null;

    private static bool B(Dictionary<string, object?> row, string key, bool fallback = false)
        => row.TryGetValue(key, out var value) && value is not null and not DBNull ? Convert.ToBoolean(value, CultureInfo.InvariantCulture) : fallback;

    private static DateTimeOffset Dto(Dictionary<string, object?> row, string key)
    {
        if (!row.TryGetValue(key, out var value) || value is null || value is DBNull)
            return DateTimeOffset.UnixEpoch;
        return value switch
        {
            DateTimeOffset dto => dto,
            DateTime dt => new DateTimeOffset(DateTime.SpecifyKind(dt, DateTimeKind.Utc)),
            _ => new DateTimeOffset(Convert.ToDateTime(value, CultureInfo.InvariantCulture), TimeSpan.Zero)
        };
    }

    private static DateTimeOffset? DtoN(Dictionary<string, object?> row, string key)
        => row.TryGetValue(key, out var value) && value is not null and not DBNull ? Dto(row, key) : null;
}
