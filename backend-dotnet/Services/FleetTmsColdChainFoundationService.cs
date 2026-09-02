using System.Globalization;
using System.Text.Json;
using Npgsql;
using Opstrax.Api.Controllers;
using Opstrax.Api.Data;

namespace Opstrax.Api.Services;

public sealed record ColdChainPolicyRecord(
    long Id,
    long CompanyId,
    long? BranchId,
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
    public async Task<IReadOnlyList<ColdChainPolicyRecord>> ListPoliciesAsync(long companyId, long? branchId, CancellationToken ct = default)
    {
        var rows = await db.QueryAsync(
            @"SELECT *
              FROM fleet_tms_cold_chain_policies
              WHERE company_id=@companyId AND (@branchId IS NULL OR branch_id=@branchId OR branch_id IS NULL)
              ORDER BY status DESC, scope_type, scope_key, policy_code",
            c =>
            {
                c.Parameters.AddWithValue("@companyId", companyId);
                AddNullableBigint(c, "@branchId", branchId);
            }, ct);
        return rows.Select(MapPolicy).ToList();
    }

    public async Task<ColdChainPolicyRecord> UpsertPolicyAsync(
        long companyId,
        long? branchId,
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
        return await db.RunInTenantTransactionAsync(companyId, async () =>
        {
        var normalizedCode = Normalize(policyCode, $"CCP-{DateTimeOffset.UtcNow.ToUnixTimeSeconds()}");
        var normalizedScopeType = Normalize(scopeType, "default");
        var normalizedScopeKey = Normalize(scopeKey, "");
        var normalizedIdempotencyKey = string.IsNullOrWhiteSpace(idempotencyKey) ? null : idempotencyKey.Trim();
        var lockKeys = new List<string>
        {
            $"cold-policy:identity:{companyId}:{branchId ?? 0}:{normalizedCode}:{normalizedScopeType}:{normalizedScopeKey}"
        };
        if (normalizedIdempotencyKey is not null)
            lockKeys.Add($"cold-policy:idem:{companyId}:{branchId ?? 0}:{normalizedIdempotencyKey}");
        foreach (var lockKey in lockKeys.Order(StringComparer.Ordinal))
            await db.ExecuteAsync("SELECT pg_advisory_xact_lock(hashtextextended(@lockKey,0))",
                c => c.Parameters.AddWithValue("@lockKey", lockKey), ct);

        if (normalizedIdempotencyKey is not null)
        {
            var existing = await db.QuerySingleAsync(
                @"SELECT *
                  FROM fleet_tms_cold_chain_policies
                  WHERE company_id=@companyId AND branch_id IS NOT DISTINCT FROM @branchId AND idempotency_key=@idempotencyKey
                  LIMIT 1",
                c =>
                {
                    c.Parameters.AddWithValue("@companyId", companyId);
                    c.Parameters.AddWithValue("@branchId", (object?)branchId ?? DBNull.Value);
                    c.Parameters.AddWithValue("@idempotencyKey", normalizedIdempotencyKey);
                }, ct);
            if (existing is not null)
            {
                return MapPolicy(existing);
            }
        }

        var effectiveSeverity = Normalize(severity, "High");
        var effectiveStatus = Normalize(status, "Active");
        var now = DateTimeOffset.UtcNow;

        await db.ExecuteAsync(
            @"INSERT INTO fleet_tms_cold_chain_policies
                (company_id, branch_id, policy_code, scope_type, scope_key, min_celsius, max_celsius, humidity_min_percent, humidity_max_percent,
                 severity, requires_acknowledgement, status, source_channel, client_generated_id, idempotency_key, correlation_id,
                 causation_id, metadata_json, notes, created_at_utc, updated_at_utc)
              VALUES
                (@companyId, @branchId, @policyCode, @scopeType, @scopeKey, @minCelsius, @maxCelsius, @humidityMinPercent, @humidityMaxPercent,
                 @severity, @requiresAcknowledgement, @status, @sourceChannel, @clientGeneratedId, @idempotencyKey, @correlationId,
                 @causationId, @metadata::jsonb, @notes, @createdAt, @updatedAt)
              ON CONFLICT DO NOTHING",
            c =>
            {
                c.Parameters.AddWithValue("@companyId", companyId);
                c.Parameters.AddWithValue("@branchId", (object?)branchId ?? DBNull.Value);
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
                c.Parameters.AddWithValue("@idempotencyKey", (object?)normalizedIdempotencyKey ?? DBNull.Value);
                c.Parameters.AddWithValue("@correlationId", (object?)correlationId ?? DBNull.Value);
                c.Parameters.AddWithValue("@causationId", (object?)causationId ?? DBNull.Value);
                c.Parameters.AddWithValue("@metadata", string.IsNullOrWhiteSpace(metadataJson) ? "{}" : metadataJson);
                c.Parameters.AddWithValue("@notes", (object?)notes ?? DBNull.Value);
                c.Parameters.AddWithValue("@createdAt", now);
                c.Parameters.AddWithValue("@updatedAt", now);
            }, ct);

        await db.ExecuteAsync(
            @"UPDATE fleet_tms_cold_chain_policies SET
                min_celsius = @minCelsius,
                max_celsius = @maxCelsius,
                humidity_min_percent = @humidityMinPercent,
                humidity_max_percent = @humidityMaxPercent,
                severity = @severity,
                requires_acknowledgement = @requiresAcknowledgement,
                status = @status,
                source_channel = @sourceChannel,
                client_generated_id = @clientGeneratedId,
                idempotency_key = COALESCE(@idempotencyKey, idempotency_key),
                correlation_id = @correlationId,
                causation_id = @causationId,
                metadata_json = @metadata::jsonb,
                notes = @notes,
                updated_at_utc = @updatedAt
              WHERE company_id=@companyId AND branch_id IS NOT DISTINCT FROM @branchId
                AND policy_code=@policyCode AND scope_type=@scopeType AND scope_key=@scopeKey",
            c =>
            {
                c.Parameters.AddWithValue("@companyId", companyId);
                c.Parameters.AddWithValue("@branchId", (object?)branchId ?? DBNull.Value);
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
                c.Parameters.AddWithValue("@idempotencyKey", (object?)normalizedIdempotencyKey ?? DBNull.Value);
                c.Parameters.AddWithValue("@correlationId", (object?)correlationId ?? DBNull.Value);
                c.Parameters.AddWithValue("@causationId", (object?)causationId ?? DBNull.Value);
                c.Parameters.AddWithValue("@metadata", string.IsNullOrWhiteSpace(metadataJson) ? "{}" : metadataJson);
                c.Parameters.AddWithValue("@notes", (object?)notes ?? DBNull.Value);
                c.Parameters.AddWithValue("@updatedAt", now);
            }, ct);

        var row = await db.QuerySingleAsync(
            @"SELECT *
              FROM fleet_tms_cold_chain_policies
              WHERE company_id=@companyId AND branch_id IS NOT DISTINCT FROM @branchId AND policy_code=@policyCode AND scope_type=@scopeType AND scope_key=@scopeKey
              LIMIT 1",
            c =>
            {
                c.Parameters.AddWithValue("@companyId", companyId);
                c.Parameters.AddWithValue("@branchId", (object?)branchId ?? DBNull.Value);
                c.Parameters.AddWithValue("@policyCode", normalizedCode);
                c.Parameters.AddWithValue("@scopeType", normalizedScopeType);
                c.Parameters.AddWithValue("@scopeKey", normalizedScopeKey);
            }, ct);

        return row is null ? throw new InvalidOperationException("Cold-chain policy could not be loaded after save") : MapPolicy(row);
        }, ct);
    }

    public async Task<Dictionary<string, object?>> RecordTemperatureReadingAsync(
        long companyId,
        long? branchId,
        TemperatureReadingRequest req,
        CancellationToken ct = default)
    {
        if (req.DeviceId <= 0)
            throw new InvalidOperationException("Temperature device id is required.");

        var device = await db.QuerySingleAsync(
            @"SELECT *
              FROM fleet_tms_temperature_devices
              WHERE company_id=@companyId AND id=@id AND (@branchId IS NULL OR branch_id=@branchId)
              LIMIT 1",
            c =>
            {
                c.Parameters.AddWithValue("@companyId", companyId);
                AddNullableBigint(c, "@branchId", branchId);
                c.Parameters.AddWithValue("@id", req.DeviceId);
            }, ct);

        if (device is null)
            throw new InvalidOperationException("Temperature device not found for this tenant.");
        long? effectiveBranchId = device["branchId"] is null or DBNull ? null : Convert.ToInt64(device["branchId"], CultureInfo.InvariantCulture);

        if (!string.IsNullOrWhiteSpace(req.IdempotencyKey))
        {
            var existing = await db.QuerySingleAsync(
                @"SELECT *
                  FROM fleet_tms_temperature_readings
                  WHERE company_id=@companyId AND branch_id IS NOT DISTINCT FROM @effectiveBranchId AND idempotency_key=@idempotencyKey
                  LIMIT 1",
                c =>
                {
                    c.Parameters.AddWithValue("@companyId", companyId);
                    c.Parameters.AddWithValue("@effectiveBranchId", (object?)effectiveBranchId ?? DBNull.Value);
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
                    AND (branch_id=@effectiveBranchId OR branch_id IS NULL)
                  LIMIT 1",
                c =>
                {
                    c.Parameters.AddWithValue("@companyId", companyId);
                    AddNullableBigint(c, "@effectiveBranchId", effectiveBranchId);
                    c.Parameters.AddWithValue("@id", zoneId.Value);
                }, ct)
            : null;

        if (zoneId.HasValue && zone is null)
            throw new InvalidOperationException("Temperature zone not found for this tenant and branch.");

        var policy = await ResolvePolicyAsync(companyId, effectiveBranchId, zone, shipmentId, vehicleNumber, req, ct);
        var effectiveMin = policy?.MinCelsius ?? (zone is null ? null : DN(zone, "minCelsius"));
        var effectiveMax = policy?.MaxCelsius ?? (zone is null ? null : DN(zone, "maxCelsius"));
        var effectiveHumidityMin = policy?.HumidityMinPercent;
        var effectiveHumidityMax = policy?.HumidityMaxPercent;
        var isTemperatureBreach = effectiveMin.HasValue && req.TemperatureCelsius < effectiveMin.Value
            || effectiveMax.HasValue && req.TemperatureCelsius > effectiveMax.Value;
        var isHumidityBreach = req.HumidityPercent.HasValue &&
            (effectiveHumidityMin.HasValue && req.HumidityPercent.Value < effectiveHumidityMin.Value
             || effectiveHumidityMax.HasValue && req.HumidityPercent.Value > effectiveHumidityMax.Value);
        var isBreach = isTemperatureBreach || isHumidityBreach;
        var status = isBreach ? "Breach" : "Normal";

        var readingId = await PersistTemperatureFlowAsync(companyId, effectiveBranchId, shipmentId, zoneId,
            req, status, policy, effectiveMin, effectiveMax, effectiveHumidityMin, effectiveHumidityMax,
            isTemperatureBreach, isHumidityBreach, ct);

        var row = await db.QuerySingleAsync(
            @"SELECT *
              FROM fleet_tms_temperature_readings
              WHERE company_id=@companyId AND id=@id AND branch_id IS NOT DISTINCT FROM @branchId
              LIMIT 1",
            c =>
            {
                c.Parameters.AddWithValue("@companyId", companyId);
                c.Parameters.AddWithValue("@branchId", (object?)effectiveBranchId ?? DBNull.Value);
                c.Parameters.AddWithValue("@id", readingId);
            }, ct);
        return row is null ? throw new InvalidOperationException("Temperature reading could not be loaded after save") : row;
    }

    private async Task<long> PersistTemperatureFlowAsync(long companyId, long? branchId, long? shipmentId, long? zoneId,
        TemperatureReadingRequest req, string status, ColdChainPolicyRecord? policy, decimal? effectiveMin,
        decimal? effectiveMax, decimal? effectiveHumidityMin, decimal? effectiveHumidityMax,
        bool isTemperatureBreach, bool isHumidityBreach, CancellationToken ct)
    {
        var isBreach = isTemperatureBreach || isHumidityBreach;
        var alertType = (isTemperatureBreach, isHumidityBreach) switch
        {
            (true, true) => "TemperatureAndHumidityBreach",
            (true, false) => "TemperatureBreach",
            _ => "HumidityBreach",
        };
        return await db.WithTransactionAsync(async (connection, transaction) =>
        {
            await using var lockDevice = new NpgsqlCommand(@"
SELECT id FROM fleet_tms_temperature_devices
WHERE id=@device AND company_id=@companyId AND branch_id IS NOT DISTINCT FROM @branchId
FOR UPDATE", connection, transaction);
            lockDevice.Parameters.AddWithValue("@device", req.DeviceId);
            lockDevice.Parameters.AddWithValue("@companyId", companyId);
            lockDevice.Parameters.AddWithValue("@branchId", (object?)branchId ?? DBNull.Value);
            if (await lockDevice.ExecuteScalarAsync(ct) is null)
                throw new InvalidOperationException("Temperature device not found for this tenant.");

            long readingId;
            await using (var insertReading = new NpgsqlCommand(@"
INSERT INTO fleet_tms_temperature_readings
 (company_id, branch_id, device_id, shipment_id, zone_id, temperature_celsius, humidity_percent, latitude, longitude, source, status,
  notes, source_channel, client_generated_id, idempotency_key, correlation_id, causation_id, metadata_json,
  applied_policy_code, applied_policy_scope, applied_min_celsius, applied_max_celsius, recorded_at_utc, created_at_utc)
VALUES
 (@companyId,@branchId,@device,@shipment,@zone,@temp,@humidity,@lat,@lng,@source,@status,@notes,@sourceChannel,
  @clientGeneratedId,@idempotencyKey,@correlationId,@causationId,@metadata::jsonb,@policyCode,@policyScope,@policyMin,@policyMax,NOW(),NOW())
ON CONFLICT DO NOTHING RETURNING id", connection, transaction))
            {
                BindReadingParameters(insertReading, companyId, branchId, shipmentId, zoneId, req, status, policy, effectiveMin, effectiveMax);
                var inserted = await insertReading.ExecuteScalarAsync(ct);
                if (inserted is null)
                {
                    if (string.IsNullOrWhiteSpace(req.IdempotencyKey))
                        throw new InvalidOperationException("Temperature reading could not be persisted.");
                    await using var existing = new NpgsqlCommand(@"
SELECT id FROM fleet_tms_temperature_readings
WHERE company_id=@companyId AND branch_id IS NOT DISTINCT FROM @branchId AND idempotency_key=@idempotencyKey", connection, transaction);
                    existing.Parameters.AddWithValue("@companyId", companyId);
                    existing.Parameters.AddWithValue("@branchId", (object?)branchId ?? DBNull.Value);
                    existing.Parameters.AddWithValue("@idempotencyKey", req.IdempotencyKey);
                    return Convert.ToInt64(await existing.ExecuteScalarAsync(ct));
                }
                readingId = Convert.ToInt64(inserted);
            }

            await using (var updateDevice = new NpgsqlCommand(@"
UPDATE fleet_tms_temperature_devices SET last_reported_temperature_celsius=@temp,
 last_ping_at_utc=NOW(), shipment_id=COALESCE(@shipment,shipment_id), zone_id=COALESCE(@zone,zone_id),
 source_channel=COALESCE(@sourceChannel,source_channel), client_generated_id=COALESCE(@clientGeneratedId,client_generated_id),
 correlation_id=COALESCE(@correlationId,correlation_id),
 causation_id=COALESCE(@causationId,causation_id), metadata_json=COALESCE(@metadata::jsonb,metadata_json), updated_at_utc=NOW()
WHERE id=@device AND company_id=@companyId AND branch_id IS NOT DISTINCT FROM @branchId", connection, transaction))
            {
                BindFlowParameters(updateDevice, companyId, branchId, shipmentId, zoneId, req);
                await updateDevice.ExecuteNonQueryAsync(ct);
            }

            if (isBreach)
            {
                await using var alert = new NpgsqlCommand(@"
INSERT INTO fleet_tms_temperature_alerts
 (company_id,branch_id,device_id,shipment_id,reading_id,alert_type,severity,status,threshold_min,threshold_max,
  measured_temperature,measured_humidity,humidity_threshold_min,humidity_threshold_max,triggered_at_utc,
  notes,source_channel,client_generated_id,idempotency_key,correlation_id,causation_id,
  metadata_json,applied_policy_code,applied_policy_scope)
VALUES (@companyId,@branchId,@device,@shipment,@reading,@alertType,@severity,'Open',@min,@max,@temp,@humidity,@humidityMin,@humidityMax,NOW(),
 'Breach derived from the persisted cold-chain policy.',@sourceChannel,@clientGeneratedId,@idempotencyKey,@correlationId,@causationId,
 @metadata::jsonb,@policyCode,@policyScope)
ON CONFLICT DO NOTHING", connection, transaction);
                BindFlowParameters(alert, companyId, branchId, shipmentId, zoneId, req);
                alert.Parameters.AddWithValue("@reading", readingId);
                alert.Parameters.AddWithValue("@alertType", alertType);
                alert.Parameters.AddWithValue("@severity", policy?.Severity ?? (req.TemperatureCelsius > (effectiveMax ?? req.TemperatureCelsius) + 2 ? "Critical" : "High"));
                alert.Parameters.AddWithValue("@min", (object?)effectiveMin ?? DBNull.Value);
                alert.Parameters.AddWithValue("@max", (object?)effectiveMax ?? DBNull.Value);
                alert.Parameters.AddWithValue("@humidity", (object?)req.HumidityPercent ?? DBNull.Value);
                alert.Parameters.AddWithValue("@humidityMin", (object?)effectiveHumidityMin ?? DBNull.Value);
                alert.Parameters.AddWithValue("@humidityMax", (object?)effectiveHumidityMax ?? DBNull.Value);
                alert.Parameters.AddWithValue("@policyCode", (object?)policy?.PolicyCode ?? DBNull.Value);
                alert.Parameters.AddWithValue("@policyScope", (object?)policy?.ScopeType ?? DBNull.Value);
                await alert.ExecuteNonQueryAsync(ct);
            }

            await InsertFlowEvent(connection, transaction, companyId, branchId, "cold_chain.temperature_reading.recorded", readingId,
                new { readingId, req.DeviceId, shipmentId, zoneId, req.TemperatureCelsius, req.HumidityPercent, status, policyCode=policy?.PolicyCode, policyScope=policy?.ScopeType, breach=isBreach }, req, ct);
            if (isBreach)
                await InsertFlowEvent(connection, transaction, companyId, branchId, "cold_chain.condition_breach.detected", readingId,
                    new { readingId, req.DeviceId, shipmentId, zoneId, req.TemperatureCelsius, req.HumidityPercent,
                        effectiveMin, effectiveMax, effectiveHumidityMin, effectiveHumidityMax, alertType,
                        policyCode=policy?.PolicyCode, policyScope=policy?.ScopeType }, req, ct);
            return readingId;
        }, ct);
    }

    private static void BindFlowParameters(NpgsqlCommand command, long companyId, long? branchId, long? shipmentId, long? zoneId, TemperatureReadingRequest req)
    {
        command.Parameters.AddWithValue("@companyId", companyId);
        command.Parameters.AddWithValue("@branchId", (object?)branchId ?? DBNull.Value);
        command.Parameters.AddWithValue("@device", req.DeviceId);
        command.Parameters.AddWithValue("@shipment", (object?)shipmentId ?? DBNull.Value);
        command.Parameters.AddWithValue("@zone", (object?)zoneId ?? DBNull.Value);
        command.Parameters.AddWithValue("@temp", req.TemperatureCelsius);
        command.Parameters.AddWithValue("@sourceChannel", (object?)req.SourceChannel ?? DBNull.Value);
        command.Parameters.AddWithValue("@clientGeneratedId", (object?)req.ClientGeneratedId ?? DBNull.Value);
        command.Parameters.AddWithValue("@idempotencyKey", (object?)req.IdempotencyKey ?? DBNull.Value);
        command.Parameters.AddWithValue("@correlationId", (object?)req.CorrelationId ?? DBNull.Value);
        command.Parameters.AddWithValue("@causationId", (object?)req.CausationId ?? DBNull.Value);
        command.Parameters.AddWithValue("@metadata", string.IsNullOrWhiteSpace(req.MetadataJson) ? "{}" : req.MetadataJson);
    }

    private static void BindReadingParameters(NpgsqlCommand command, long companyId, long? branchId, long? shipmentId, long? zoneId,
        TemperatureReadingRequest req, string status, ColdChainPolicyRecord? policy, decimal? effectiveMin, decimal? effectiveMax)
    {
        BindFlowParameters(command, companyId, branchId, shipmentId, zoneId, req);
        command.Parameters.AddWithValue("@humidity", (object?)req.HumidityPercent ?? DBNull.Value);
        command.Parameters.AddWithValue("@lat", (object?)req.Latitude ?? DBNull.Value);
        command.Parameters.AddWithValue("@lng", (object?)req.Longitude ?? DBNull.Value);
        command.Parameters.AddWithValue("@source", NormalizeReadingSource(req.Source));
        command.Parameters.AddWithValue("@status", status);
        command.Parameters.AddWithValue("@notes", req.Notes?.Trim() ?? string.Empty);
        command.Parameters.AddWithValue("@policyCode", (object?)policy?.PolicyCode ?? DBNull.Value);
        command.Parameters.AddWithValue("@policyScope", (object?)policy?.ScopeType ?? DBNull.Value);
        command.Parameters.AddWithValue("@policyMin", (object?)effectiveMin ?? DBNull.Value);
        command.Parameters.AddWithValue("@policyMax", (object?)effectiveMax ?? DBNull.Value);
    }

    internal static string NormalizeReadingSource(string? source) => source?.Trim().ToLowerInvariant() switch
    {
        null or "" or "sensor" => "Sensor",
        "gateway" => "Gateway",
        "manual" => "Manual",
        "import" => "Import",
        _ => throw new InvalidOperationException("Reading source is invalid."),
    };

    private static async Task InsertFlowEvent(NpgsqlConnection connection, NpgsqlTransaction transaction, long companyId, long? branchId,
        string eventType, long readingId, object payload, TemperatureReadingRequest req, CancellationToken ct)
    {
        await using var command = new NpgsqlCommand(@"
INSERT INTO fleet_tms_cold_chain_event_log
 (company_id,branch_id,event_type,aggregate_type,aggregate_id,payload_json,correlation_id,causation_id,idempotency_key,status,occurred_at_utc,processed_at_utc,created_at_utc)
VALUES (@companyId,@branchId,@eventType,'temperature_reading',@aggregateId,@payload::jsonb,@correlationId,@causationId,@idempotencyKey,'processed',NOW(),NOW(),NOW())
ON CONFLICT DO NOTHING", connection, transaction);
        command.Parameters.AddWithValue("@companyId", companyId);
        command.Parameters.AddWithValue("@branchId", (object?)branchId ?? DBNull.Value);
        command.Parameters.AddWithValue("@eventType", eventType);
        command.Parameters.AddWithValue("@aggregateId", readingId.ToString(CultureInfo.InvariantCulture));
        command.Parameters.AddWithValue("@payload", JsonSerializer.Serialize(payload));
        command.Parameters.AddWithValue("@correlationId", (object?)req.CorrelationId ?? DBNull.Value);
        command.Parameters.AddWithValue("@causationId", (object?)req.CausationId ?? DBNull.Value);
        command.Parameters.AddWithValue("@idempotencyKey", (object?)req.IdempotencyKey ?? DBNull.Value);
        await command.ExecuteNonQueryAsync(ct);
    }

    public async Task<Dictionary<string, object?>> ResolveAlertAsync(long companyId, long? branchId, long id, TemperatureAlertResolveRequest req, string? actor, CancellationToken ct = default)
    {
        return await db.RunInTenantTransactionAsync(companyId, async () =>
        {
        var existing = await db.QuerySingleAsync(
            @"SELECT *
              FROM fleet_tms_temperature_alerts
              WHERE company_id=@companyId AND id=@id AND (@branchId IS NULL OR branch_id=@branchId)
              LIMIT 1",
            c =>
            {
                c.Parameters.AddWithValue("@companyId", companyId);
                AddNullableBigint(c, "@branchId", branchId);
                c.Parameters.AddWithValue("@id", id);
            }, ct);
        if (existing is null)
            throw new InvalidOperationException("Temperature alert not found for this tenant.");
        if (string.Equals(existing.GetValueOrDefault("status")?.ToString(), "Resolved", StringComparison.Ordinal))
            return existing;

        var notes = req.ResolutionNotes?.Trim() ?? "Resolved by operations.";
        var rows = await db.ExecuteAsync(
            @"UPDATE fleet_tms_temperature_alerts
              SET status='Resolved',
                  resolved_at_utc=NOW(),
                  resolved_by=@actor,
                  resolution_notes=@notes
              WHERE id=@id AND company_id=@companyId AND branch_id IS NOT DISTINCT FROM @effectiveBranchId
                AND status<>'Resolved'",
            c =>
            {
                c.Parameters.AddWithValue("@actor", actor ?? "system");
                c.Parameters.AddWithValue("@notes", notes);
                c.Parameters.AddWithValue("@id", id);
                c.Parameters.AddWithValue("@companyId", companyId);
                c.Parameters.AddWithValue("@effectiveBranchId", existing["branchId"] ?? DBNull.Value);
            }, ct);

        if (rows == 0)
        {
            var replay = await db.QuerySingleAsync(
                @"SELECT * FROM fleet_tms_temperature_alerts
                  WHERE company_id=@companyId AND id=@id AND branch_id IS NOT DISTINCT FROM @effectiveBranchId
                  LIMIT 1",
                c =>
                {
                    c.Parameters.AddWithValue("@companyId", companyId);
                    c.Parameters.AddWithValue("@effectiveBranchId", existing["branchId"] ?? DBNull.Value);
                    c.Parameters.AddWithValue("@id", id);
                }, ct);
            return replay ?? throw new InvalidOperationException("Temperature alert not found for this tenant.");
        }

        await WriteEventAsync(
            companyId,
            existing["branchId"] is null or DBNull ? null : Convert.ToInt64(existing["branchId"], CultureInfo.InvariantCulture),
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
              WHERE company_id=@companyId AND id=@id AND branch_id IS NOT DISTINCT FROM @effectiveBranchId
              LIMIT 1",
            c =>
            {
                c.Parameters.AddWithValue("@companyId", companyId);
                c.Parameters.AddWithValue("@effectiveBranchId", existing["branchId"] ?? DBNull.Value);
                c.Parameters.AddWithValue("@id", id);
            }, ct);
        return row is null ? throw new InvalidOperationException("Temperature alert could not be loaded after resolution") : row;
        }, ct);
    }

    public async Task<long> WriteEventAsync(
        long companyId,
        long? branchId,
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
                (company_id, branch_id, event_type, aggregate_type, aggregate_id, payload_json, correlation_id, causation_id, idempotency_key, status, occurred_at_utc, processed_at_utc, created_at_utc)
              VALUES
                (@companyId, @branchId, @eventType, @aggregateType, @aggregateId, @payload::jsonb, @correlationId, @causationId, @idempotencyKey, 'processed', NOW(), NOW(), NOW())",
            c =>
            {
                c.Parameters.AddWithValue("@companyId", companyId);
                c.Parameters.AddWithValue("@branchId", (object?)branchId ?? DBNull.Value);
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
        long? branchId,
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
                  WHERE company_id=@companyId AND (branch_id=@branchId OR branch_id IS NULL)
                    AND scope_type=@scopeType AND scope_key=@scopeKey AND status='Active'
                  ORDER BY CASE WHEN branch_id=@branchId THEN 0 ELSE 1 END,
                           updated_at_utc DESC NULLS LAST, created_at_utc DESC, id DESC
                  LIMIT 1",
                c =>
                {
                    c.Parameters.AddWithValue("@companyId", companyId);
                    AddNullableBigint(c, "@branchId", branchId);
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

    private static void AddNullableBigint(NpgsqlCommand command, string name, long? value)
        => command.Parameters.Add(new NpgsqlParameter(name, NpgsqlTypes.NpgsqlDbType.Bigint)
        {
            Value = (object?)value ?? DBNull.Value,
        });

    private static ColdChainPolicyRecord MapPolicy(Dictionary<string, object?> row) => new(
        L(row, "id"),
        L(row, "companyId"),
        row["branchId"] is null or DBNull ? null : L(row, "branchId"),
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
