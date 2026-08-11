using System.Globalization;
using System.Text.Json;
using Npgsql;
using NpgsqlTypes;
using Opstrax.Telematics.Contracts;
using Opstrax.Telematics.Contracts.Provenance;
using Opstrax.Telematics.Contracts.Quality;
using Opstrax.Telematics.Contracts.Signals;

namespace Opstrax.Telematics.Gateway.Projection;

/// <summary>
/// Postgres-backed <see cref="IPositionProjectionStore"/>. Enforces the invariants of the
/// projection at the database, in ONE transaction per event, exactly as documented in
/// <c>database/migrations/telematics/006_projection_inbox.sql</c>:
/// <list type="number">
///   <item><description>
///     <b>Idempotency</b> — INSERT into <c>telemetry_projection_inbox</c> with
///     <c>ON CONFLICT (event_id) DO NOTHING</c>. Zero rows affected means the event was already
///     projected, so the transaction commits without touching the snapshot.
///   </description></item>
///   <item><description>
///     <b>Monotonicity</b> — the <c>latest_vehicle_positions</c> upsert's
///     <c>DO UPDATE ... WHERE EXCLUDED.device_fix_time &gt;= stored device_fix_time</c> guard
///     refuses to stamp an older fix over a newer one. Zero rows affected there means the incoming
///     fix was stale.
///   </description></item>
/// </list>
/// </summary>
/// <remarks>
/// <para>
/// Both statements run inside a single transaction with <c>SET LOCAL app.current_tenant_id</c> set
/// to the event's company, so the RLS policies on both tables (telematics 003/006) admit the write.
/// Doing the inbox insert, history append, heartbeat, latest snapshot, and alert projection
/// atomically is what makes the dedupe reliable: a crash can never burn the inbox identity while
/// leaving a partially projected device state.
/// </para>
/// <para>
/// This type opens a short-lived connection per call for clarity; a production deployment injects a
/// pooled <see cref="NpgsqlDataSource"/>. It is intentionally NOT exercised by the unit tests (which
/// use <see cref="InMemoryPositionProjectionStore"/>); its contract is pinned by the SQL migration.
/// </para>
/// </remarks>
internal sealed class PostgresPositionProjectionStore : IPositionProjectionStore
{
    private const double KphToMph = 0.621371;

    private readonly string _connectionString;

    public PostgresPositionProjectionStore(string connectionString)
    {
        _connectionString = connectionString ?? throw new ArgumentNullException(nameof(connectionString));
    }

    /// <inheritdoc />
    public async Task<ProjectionOutcome> ApplyAsync(CanonicalTelemetryEvent evt, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(evt);
        if (evt.CompanyId <= 0)
            throw new InvalidOperationException("The production projector requires a positive registry company id.");

        await using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);

        await using NpgsqlTransaction tx =
            await connection.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);

        // Scope the transaction to the event's tenant so the RLS predicates on both tables admit it.
        await SetTenantAsync(connection, tx, evt.CompanyId, cancellationToken).ConfigureAwait(false);

        if (!long.TryParse(evt.DeviceId, NumberStyles.None, CultureInfo.InvariantCulture, out long deviceId) || deviceId <= 0)
            throw new InvalidOperationException("The production projector requires a numeric registry device id.");

        // ── (a) idempotent inbox insert ─────────────────────────────────────────
        int inboxRows = await InsertInboxAsync(connection, tx, evt, cancellationToken).ConfigureAwait(false);
        if (inboxRows == 0)
        {
            // Already projected once — commit the (empty) transaction and no-op the snapshot.
            await tx.CommitAsync(cancellationToken).ConfigureAwait(false);
            return ProjectionOutcome.DuplicateIgnored;
        }

        DeviceContext device = await ResolveDeviceContextAsync(connection, tx, evt, deviceId, cancellationToken)
            .ConfigureAwait(false);
        await UpdateHeartbeatAsync(connection, tx, evt, deviceId, cancellationToken).ConfigureAwait(false);

        // The event is now recorded as seen. Heartbeat-only frames still update device health but
        // correctly do not fabricate position history, a live fix, or an alert.
        if (evt.Location is null)
        {
            await tx.CommitAsync(cancellationToken).ConfigureAwait(false);
            return ProjectionOutcome.NoLocation;
        }

        long historyEventId = await InsertLocationHistoryAsync(
            connection, tx, evt, deviceId, device.DriverId, cancellationToken).ConfigureAwait(false);

        if (evt.VehicleId is not { } vehicleId)
        {
            await tx.CommitAsync(cancellationToken).ConfigureAwait(false);
            return ProjectionOutcome.NoVehicle;
        }

        // ── (b) monotonic snapshot + current-alert parity ────────────────────────
        int upsertRows = await UpsertLatestPositionAsync(
                connection, tx, evt, vehicleId, deviceId, device.DriverId, historyEventId, cancellationToken)
            .ConfigureAwait(false);
        // Novel out-of-order history is retained, but it cannot fabricate a current/open
        // speeding or geofence alert after losing the authoritative latest-position race.
        if (upsertRows > 0)
            await ProjectAlertsAsync(connection, tx, evt, vehicleId, deviceId, device.DriverId,
                device.VehicleBranchId, historyEventId, cancellationToken).ConfigureAwait(false);

        await tx.CommitAsync(cancellationToken).ConfigureAwait(false);

        // Zero rows means the WHERE guard rejected an older fix. Because the seen-set gate above
        // already removed exact duplicates, history and any novel evidence remain durable while the
        // current live snapshot and its open-alert view remain monotonic.
        return upsertRows == 0 ? ProjectionOutcome.StaleIgnored : ProjectionOutcome.Applied;
    }

    private sealed record DeviceContext(long? DriverId, long? VehicleBranchId);

    private static async Task<DeviceContext> ResolveDeviceContextAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction tx,
        CanonicalTelemetryEvent evt,
        long deviceId,
        CancellationToken ct)
    {
        const string sql = """
            SELECT e.driver_id, v.branch_id
              FROM eld_devices e
              LEFT JOIN vehicles v
                ON v.id=e.vehicle_id AND v.company_id=e.company_id AND v.deleted_at IS NULL
             WHERE e.id=@device_id AND e.company_id=@company_id AND e.deleted_at IS NULL
               AND (@vehicle_id::bigint IS NULL OR e.vehicle_id=@vehicle_id)
             LIMIT 1;
            """;
        await using var command = new NpgsqlCommand(sql, connection, tx);
        command.Parameters.AddWithValue("device_id", deviceId);
        command.Parameters.AddWithValue("company_id", evt.CompanyId);
        command.Parameters.Add(new NpgsqlParameter("vehicle_id", NpgsqlDbType.Bigint)
            { Value = (object?)evt.VehicleId ?? DBNull.Value });
        await using var reader = await command.ExecuteReaderAsync(ct).ConfigureAwait(false);
        if (!await reader.ReadAsync(ct).ConfigureAwait(false))
            throw new InvalidOperationException("The registry ownership changed before telemetry projection.");
        return new DeviceContext(
            reader.IsDBNull(0) ? null : reader.GetInt64(0),
            reader.IsDBNull(1) ? null : reader.GetInt64(1));
    }

    private static async Task UpdateHeartbeatAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction tx,
        CanonicalTelemetryEvent evt,
        long deviceId,
        CancellationToken ct)
    {
        const string sql = """
            UPDATE eld_devices
               SET last_seen_at=CASE WHEN last_seen_at IS NULL OR last_seen_at<@received_at
                                     THEN @received_at ELSE last_seen_at END,
                   last_heartbeat_at=CASE WHEN last_heartbeat_at IS NULL OR last_heartbeat_at<@received_at
                                          THEN @received_at ELSE last_heartbeat_at END,
                   updated_at=NOW()
             WHERE id=@device_id AND company_id=@company_id AND deleted_at IS NULL;
            """;
        await using var command = new NpgsqlCommand(sql, connection, tx);
        command.Parameters.AddWithValue("received_at", Utc(evt.ReceivedAtGatewayUtc));
        command.Parameters.AddWithValue("device_id", deviceId);
        command.Parameters.AddWithValue("company_id", evt.CompanyId);
        if (await command.ExecuteNonQueryAsync(ct).ConfigureAwait(false) != 1)
            throw new InvalidOperationException("The registry device disappeared during telemetry projection.");
    }

    private static async Task<long> InsertLocationHistoryAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction tx,
        CanonicalTelemetryEvent evt,
        long deviceId,
        long? driverId,
        CancellationToken ct)
    {
        GeoPointValues geo = ReadGeo(evt.Location!.Value);
        const string sql = """
            INSERT INTO location_events
                (company_id,vehicle_id,device_id,driver_id,lat,lng,speed_mph,heading,
                 event_type,engine_status,fuel_level,odometer_miles,source,source_channel,
                 idempotency_key,observed_at,normalized_at,event_time,received_at)
            VALUES
                (@company_id,@vehicle_id,@device_id,@driver_id,@lat,@lng,@speed_mph,@heading,
                 'ping',@engine_status,@fuel_level,@odometer_miles,'gps-tracker','raw-gt06',
                 @idempotency_key,@device_fix_time,@normalized_at,@device_fix_time,@received_at)
            ON CONFLICT DO NOTHING
            RETURNING id;
            """;
        await using var command = new NpgsqlCommand(sql, connection, tx);
        AddCommonProjectionParameters(command, evt, deviceId, driverId, geo);
        command.Parameters.AddWithValue("idempotency_key", $"gt06:{evt.EventId:D}");
        object? inserted = await command.ExecuteScalarAsync(ct).ConfigureAwait(false);
        if (inserted is null or DBNull)
            throw new InvalidOperationException("Location history identity existed without its projection inbox row.");
        return Convert.ToInt64(inserted, CultureInfo.InvariantCulture);
    }

    private static async Task SetTenantAsync(
        NpgsqlConnection connection, NpgsqlTransaction tx, long companyId, CancellationToken ct)
    {
        await using var cmd = new NpgsqlCommand(
            "SELECT set_config('app.current_tenant_id', @tenant, true)", connection, tx);
        cmd.Parameters.AddWithValue("tenant", companyId.ToString(CultureInfo.InvariantCulture));
        await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
    }

    private static async Task<int> InsertInboxAsync(
        NpgsqlConnection connection, NpgsqlTransaction tx, CanonicalTelemetryEvent evt, CancellationToken ct)
    {
        const string sql = """
            INSERT INTO telemetry_projection_inbox
                (event_id, correlation_id, tenant_id, company_id, device_id,
                 vehicle_id, device_fix_time, schema_version)
            VALUES
                (@event_id, @correlation_id, @tenant_id, @company_id, @device_id,
                 @vehicle_id, @device_fix_time, @schema_version)
            ON CONFLICT (event_id) DO NOTHING;
            """;

        await using var cmd = new NpgsqlCommand(sql, connection, tx);
        cmd.Parameters.Add(new NpgsqlParameter("event_id", NpgsqlDbType.Uuid) { Value = evt.EventId });
        cmd.Parameters.Add(new NpgsqlParameter("correlation_id", NpgsqlDbType.Uuid) { Value = evt.CorrelationId });
        cmd.Parameters.Add(new NpgsqlParameter("tenant_id", NpgsqlDbType.Uuid) { Value = evt.TenantId });
        cmd.Parameters.Add(new NpgsqlParameter("company_id", NpgsqlDbType.Bigint) { Value = evt.CompanyId });
        cmd.Parameters.Add(new NpgsqlParameter("device_id", NpgsqlDbType.Text) { Value = (object?)evt.DeviceId ?? DBNull.Value });
        cmd.Parameters.Add(new NpgsqlParameter("vehicle_id", NpgsqlDbType.Bigint) { Value = (object?)evt.VehicleId ?? DBNull.Value });
        cmd.Parameters.Add(new NpgsqlParameter("device_fix_time", NpgsqlDbType.TimestampTz) { Value = Utc(evt.OccurredAtDeviceUtc) });
        cmd.Parameters.Add(new NpgsqlParameter("schema_version", NpgsqlDbType.Integer) { Value = evt.SchemaVersion });

        return await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
    }

    private static async Task<int> UpsertLatestPositionAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction tx,
        CanonicalTelemetryEvent evt,
        long vehicleId,
        long deviceId,
        long? driverId,
        long sourceEventId,
        CancellationToken ct)
    {
        // correlation_id is deliberately NOT written here — see the stage12a drift note in the
        // 004 migration. The clean UUID correlation anchor lives on the inbox row.
        const string sql = """
            INSERT INTO latest_vehicle_positions
                (company_id, vehicle_id, device_id, driver_id, lat, lng, speed_mph, heading,
                 engine_status, fuel_level, odometer_miles,
                 source, provider, protocol, adapter_version, confidence, trust_score,
                 quality_flags, device_fix_time, gateway_received_at, normalized_at,
                 event_time, received_at, event_count, source_event_id, source_channel,
                 telemetry_status, risk_level, updated_at)
            VALUES
                (@company_id, @vehicle_id, @device_id, @driver_id, @lat, @lng, @speed_mph, @heading,
                 @engine_status, @fuel_level, @odometer_miles,
                 @source, @provider, @protocol, @adapter_version, @confidence, @trust_score,
                 @quality_flags::jsonb, @device_fix_time, @gateway_received_at, @normalized_at,
                 @event_time, @received_at, 1, @source_event_id, 'raw-gt06',
                 @telemetry_status, @risk_level, NOW())
            ON CONFLICT (company_id, vehicle_id) DO UPDATE SET
                device_id = EXCLUDED.device_id, driver_id = EXCLUDED.driver_id,
                lat = EXCLUDED.lat, lng = EXCLUDED.lng,
                speed_mph = EXCLUDED.speed_mph, heading = EXCLUDED.heading,
                engine_status = EXCLUDED.engine_status, fuel_level = EXCLUDED.fuel_level,
                odometer_miles = EXCLUDED.odometer_miles,
                source = EXCLUDED.source, provider = EXCLUDED.provider,
                protocol = EXCLUDED.protocol, adapter_version = EXCLUDED.adapter_version,
                confidence = EXCLUDED.confidence, trust_score = EXCLUDED.trust_score,
                quality_flags = EXCLUDED.quality_flags,
                device_fix_time = EXCLUDED.device_fix_time,
                gateway_received_at = EXCLUDED.gateway_received_at,
                normalized_at = EXCLUDED.normalized_at,
                event_time = EXCLUDED.event_time,
                received_at = EXCLUDED.received_at,
                event_count = latest_vehicle_positions.event_count + 1,
                source_event_id = EXCLUDED.source_event_id,
                source_channel = EXCLUDED.source_channel,
                telemetry_status = EXCLUDED.telemetry_status,
                risk_level = EXCLUDED.risk_level,
                updated_at = NOW()
            WHERE EXCLUDED.device_fix_time IS NOT NULL
              AND (latest_vehicle_positions.device_fix_time IS NULL
                   OR EXCLUDED.device_fix_time >= latest_vehicle_positions.device_fix_time);
            """;

        GeoPointValues geo = ReadGeo(evt.Location!.Value);

        await using var cmd = new NpgsqlCommand(sql, connection, tx);
        cmd.Parameters.Add(new NpgsqlParameter("company_id", NpgsqlDbType.Bigint) { Value = evt.CompanyId });
        cmd.Parameters.Add(new NpgsqlParameter("vehicle_id", NpgsqlDbType.Bigint) { Value = vehicleId });
        cmd.Parameters.Add(new NpgsqlParameter("device_id", NpgsqlDbType.Bigint) { Value = deviceId });
        cmd.Parameters.Add(new NpgsqlParameter("driver_id", NpgsqlDbType.Bigint) { Value = (object?)driverId ?? DBNull.Value });
        cmd.Parameters.Add(new NpgsqlParameter("lat", NpgsqlDbType.Numeric) { Value = geo.Lat });
        cmd.Parameters.Add(new NpgsqlParameter("lng", NpgsqlDbType.Numeric) { Value = geo.Lng });
        cmd.Parameters.Add(new NpgsqlParameter("speed_mph", NpgsqlDbType.Numeric) { Value = geo.SpeedMph });
        cmd.Parameters.Add(new NpgsqlParameter("heading", NpgsqlDbType.Smallint) { Value = geo.Heading });
        cmd.Parameters.Add(new NpgsqlParameter("source", NpgsqlDbType.Text) { Value = MapSource(evt.Source) });
        cmd.Parameters.Add(new NpgsqlParameter("provider", NpgsqlDbType.Text) { Value = (object?)NullIfEmpty(evt.AdapterName) ?? DBNull.Value });
        cmd.Parameters.Add(new NpgsqlParameter("protocol", NpgsqlDbType.Text) { Value = (object?)NullIfEmpty(evt.ProtocolName) ?? DBNull.Value });
        cmd.Parameters.Add(new NpgsqlParameter("adapter_version", NpgsqlDbType.Text) { Value = (object?)NullIfEmpty(evt.AdapterVersion) ?? DBNull.Value });
        cmd.Parameters.Add(new NpgsqlParameter("confidence", NpgsqlDbType.Numeric) { Value = Clamp01(evt.Confidence) });
        cmd.Parameters.Add(new NpgsqlParameter("trust_score", NpgsqlDbType.Numeric) { Value = Clamp01(evt.TrustScore) });
        cmd.Parameters.Add(new NpgsqlParameter("quality_flags", NpgsqlDbType.Text) { Value = SerializeQuality(evt.Quality) });
        cmd.Parameters.Add(new NpgsqlParameter("device_fix_time", NpgsqlDbType.TimestampTz) { Value = Utc(evt.OccurredAtDeviceUtc) });
        cmd.Parameters.Add(new NpgsqlParameter("gateway_received_at", NpgsqlDbType.TimestampTz) { Value = Utc(evt.ReceivedAtGatewayUtc) });
        cmd.Parameters.Add(new NpgsqlParameter("normalized_at", NpgsqlDbType.TimestampTz) { Value = Utc(evt.NormalizedAtUtc) });
        cmd.Parameters.Add(new NpgsqlParameter("event_time", NpgsqlDbType.TimestampTz) { Value = Utc(evt.OccurredAtDeviceUtc) });
        cmd.Parameters.Add(new NpgsqlParameter("received_at", NpgsqlDbType.TimestampTz) { Value = Utc(evt.ReceivedAtGatewayUtc) });
        cmd.Parameters.Add(new NpgsqlParameter("source_event_id", NpgsqlDbType.Bigint) { Value = sourceEventId });
        (string telemetryStatus, string riskLevel) = ClassifyFixFreshness(evt.OccurredAtDeviceUtc, DateTime.UtcNow);
        cmd.Parameters.AddWithValue("telemetry_status", telemetryStatus);
        cmd.Parameters.AddWithValue("risk_level", riskLevel);
        cmd.Parameters.Add(new NpgsqlParameter("engine_status", NpgsqlDbType.Text)
            { Value = evt.EngineOn is null ? DBNull.Value : evt.EngineOn.Value ? "Running" : "Off" });
        cmd.Parameters.Add(new NpgsqlParameter("fuel_level", NpgsqlDbType.Numeric)
            { Value = evt.FuelPercent is { } fuel ? (decimal)fuel : DBNull.Value });
        cmd.Parameters.Add(new NpgsqlParameter("odometer_miles", NpgsqlDbType.Numeric)
            { Value = evt.OdometerKm is { } km ? (decimal)(km * KphToMph) : DBNull.Value });

        return await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
    }

    internal static (string TelemetryStatus, string RiskLevel) ClassifyFixFreshness(
        DateTime deviceFixUtc,
        DateTime nowUtc)
    {
        TimeSpan age = Utc(nowUtc) - Utc(deviceFixUtc);
        if (age < TimeSpan.FromMinutes(-5)) return ("unknown", "unknown");
        return age <= TimeSpan.FromMinutes(5) ? ("healthy", "low") : ("stale", "unknown");
    }

    private static async Task ProjectAlertsAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction tx,
        CanonicalTelemetryEvent evt,
        long vehicleId,
        long deviceId,
        long? driverId,
        long? vehicleBranchId,
        long sourceEventId,
        CancellationToken ct)
    {
        GeoPointValues geo = ReadGeo(evt.Location!.Value);
        decimal speedThreshold = await ReadSpeedThresholdAsync(connection, tx, evt.CompanyId, ct)
            .ConfigureAwait(false);
        if (geo.SpeedMph > speedThreshold)
        {
            const string speedSql = """
                INSERT INTO telemetry_alerts
                    (company_id,vehicle_id,device_id,driver_id,alert_type,severity,message,
                     source_event_id,status,source_channel,created_at)
                SELECT @company_id,@vehicle_id,@device_id,@driver_id,'speeding',
                       COALESCE((SELECT severity FROM telemetry_rules
                                  WHERE company_id=@company_id AND rule_type='speeding' AND enabled=TRUE LIMIT 1),'High'),
                       @message,@source_event_id,'Open','raw-gt06',NOW()
                 WHERE NOT EXISTS (
                       SELECT 1 FROM telemetry_alerts
                        WHERE company_id=@company_id AND vehicle_id=@vehicle_id
                          AND alert_type='speeding' AND status='Open');
                """;
            await using var speed = new NpgsqlCommand(speedSql, connection, tx);
            AddAlertIdentityParameters(speed, evt.CompanyId, vehicleId, deviceId, driverId, sourceEventId);
            speed.Parameters.AddWithValue("message", $"Vehicle {geo.SpeedMph:F0} mph exceeds {speedThreshold:F0} mph threshold");
            await speed.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
        }

        GeofenceBreach? breach = await FindGeofenceBreachAsync(
            connection, tx, evt.CompanyId, vehicleBranchId, geo.Lat, geo.Lng, ct).ConfigureAwait(false);
        if (breach is null) return;

        const string geofenceSql = """
            INSERT INTO telemetry_alerts
                (company_id,vehicle_id,device_id,driver_id,alert_type,severity,message,
                 source_event_id,status,source_channel,created_at)
            SELECT @company_id,@vehicle_id,@device_id,@driver_id,'geofence_breach','High',
                   @message,@source_event_id,'Open','raw-gt06',NOW()
             WHERE NOT EXISTS (
                   SELECT 1 FROM telemetry_alerts
                    WHERE company_id=@company_id AND vehicle_id=@vehicle_id
                      AND alert_type='geofence_breach' AND message=@message AND status='Open');
            """;
        await using var geofence = new NpgsqlCommand(geofenceSql, connection, tx);
        AddAlertIdentityParameters(geofence, evt.CompanyId, vehicleId, deviceId, driverId, sourceEventId);
        geofence.Parameters.AddWithValue("message", $"Vehicle outside geofence: {breach.Name}");
        await geofence.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
    }

    private static async Task<decimal> ReadSpeedThresholdAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction tx,
        long companyId,
        CancellationToken ct)
    {
        await using var command = new NpgsqlCommand(
            "SELECT threshold_value FROM telemetry_rules WHERE company_id=@company_id AND rule_type='speeding' AND enabled=TRUE LIMIT 1",
            connection, tx);
        command.Parameters.AddWithValue("company_id", companyId);
        object? value = await command.ExecuteScalarAsync(ct).ConfigureAwait(false);
        return value is null or DBNull ? 65m : Convert.ToDecimal(value, CultureInfo.InvariantCulture);
    }

    private sealed record GeofenceBreach(long Id, string Name);

    private static async Task<GeofenceBreach?> FindGeofenceBreachAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction tx,
        long companyId,
        long? vehicleBranchId,
        decimal lat,
        decimal lng,
        CancellationToken ct)
    {
        // Membership semantics are "inside any authorized active fence". Selecting the first
        // fence the vehicle is outside of is unsafe when a tenant has multiple yards: a vehicle
        // inside Yard B is naturally outside Yard A and must not be alerted. Alert only when at
        // least one well-formed fence exists and the point is outside every such fence.
        const string sql = """
            SELECT id,name,center_lat,center_lng,radius_meters,polygon_json::text
              FROM geofences
             WHERE company_id=@company_id AND status='Active'
               AND (branch_id IS NULL OR branch_id=@branch_id)
             ORDER BY id;
            """;
        await using var command = new NpgsqlCommand(sql, connection, tx);
        command.Parameters.AddWithValue("company_id", companyId);
        command.Parameters.Add(new NpgsqlParameter("branch_id", NpgsqlDbType.Bigint)
            { Value = (object?)vehicleBranchId ?? DBNull.Value });
        await using var fences = await command.ExecuteReaderAsync(ct).ConfigureAwait(false);
        GeofenceBreach? firstValidFence = null;
        while (await fences.ReadAsync(ct).ConfigureAwait(false))
        {
            bool valid = false;
            bool inside = false;
            if (!fences.IsDBNull(5))
            {
                IReadOnlyList<(double Lat, double Lng)>? ring = ParsePolygon(fences.GetString(5));
                if (ring is not null)
                {
                    valid = true;
                    inside = PointInPolygon((double)lat, (double)lng, ring);
                }
            }
            else if (!fences.IsDBNull(2) && !fences.IsDBNull(3) && !fences.IsDBNull(4))
            {
                valid = true;
                inside = DistanceMeters(
                    (double)lat,
                    (double)lng,
                    Convert.ToDouble(fences.GetValue(2), CultureInfo.InvariantCulture),
                    Convert.ToDouble(fences.GetValue(3), CultureInfo.InvariantCulture))
                    <= Convert.ToDouble(fences.GetValue(4), CultureInfo.InvariantCulture);
            }

            if (!valid) continue;
            firstValidFence ??= new GeofenceBreach(fences.GetInt64(0), fences.GetString(1));
            if (inside) return null;
        }
        return firstValidFence;
    }

    private static void AddCommonProjectionParameters(
        NpgsqlCommand command,
        CanonicalTelemetryEvent evt,
        long deviceId,
        long? driverId,
        GeoPointValues geo)
    {
        command.Parameters.AddWithValue("company_id", evt.CompanyId);
        command.Parameters.Add(new NpgsqlParameter("vehicle_id", NpgsqlDbType.Bigint)
            { Value = (object?)evt.VehicleId ?? DBNull.Value });
        command.Parameters.AddWithValue("device_id", deviceId);
        command.Parameters.Add(new NpgsqlParameter("driver_id", NpgsqlDbType.Bigint)
            { Value = (object?)driverId ?? DBNull.Value });
        command.Parameters.AddWithValue("lat", geo.Lat);
        command.Parameters.AddWithValue("lng", geo.Lng);
        command.Parameters.AddWithValue("speed_mph", geo.SpeedMph);
        command.Parameters.AddWithValue("heading", geo.Heading);
        command.Parameters.Add(new NpgsqlParameter("engine_status", NpgsqlDbType.Text)
            { Value = evt.EngineOn is null ? DBNull.Value : evt.EngineOn.Value ? "Running" : "Off" });
        command.Parameters.Add(new NpgsqlParameter("fuel_level", NpgsqlDbType.Numeric)
            { Value = evt.FuelPercent is { } fuel ? (decimal)fuel : DBNull.Value });
        command.Parameters.Add(new NpgsqlParameter("odometer_miles", NpgsqlDbType.Numeric)
            { Value = evt.OdometerKm is { } km ? (decimal)(km * KphToMph) : DBNull.Value });
        command.Parameters.AddWithValue("device_fix_time", Utc(evt.OccurredAtDeviceUtc));
        command.Parameters.AddWithValue("received_at", Utc(evt.ReceivedAtGatewayUtc));
        command.Parameters.AddWithValue("normalized_at", Utc(evt.NormalizedAtUtc));
    }

    private static void AddAlertIdentityParameters(
        NpgsqlCommand command,
        long companyId,
        long vehicleId,
        long deviceId,
        long? driverId,
        long sourceEventId)
    {
        command.Parameters.AddWithValue("company_id", companyId);
        command.Parameters.AddWithValue("vehicle_id", vehicleId);
        command.Parameters.AddWithValue("device_id", deviceId);
        command.Parameters.Add(new NpgsqlParameter("driver_id", NpgsqlDbType.Bigint)
            { Value = (object?)driverId ?? DBNull.Value });
        command.Parameters.AddWithValue("source_event_id", sourceEventId);
    }

    private static IReadOnlyList<(double Lat, double Lng)>? ParsePolygon(string json)
    {
        try
        {
            using JsonDocument document = JsonDocument.Parse(json);
            if (document.RootElement.ValueKind != JsonValueKind.Array) return null;
            var points = new List<(double Lat, double Lng)>();
            foreach (JsonElement point in document.RootElement.EnumerateArray())
            {
                if (point.ValueKind == JsonValueKind.Array && point.GetArrayLength() >= 2)
                {
                    JsonElement.ArrayEnumerator values = point.EnumerateArray();
                    values.MoveNext();
                    double pointLat = values.Current.GetDouble();
                    values.MoveNext();
                    points.Add((pointLat, values.Current.GetDouble()));
                }
                else if (point.ValueKind == JsonValueKind.Object &&
                         point.TryGetProperty("lat", out JsonElement pointLat) &&
                         point.TryGetProperty("lng", out JsonElement pointLng))
                {
                    points.Add((pointLat.GetDouble(), pointLng.GetDouble()));
                }
                else return null;
            }
            return points.Count >= 3 ? points : null;
        }
        catch (JsonException) { return null; }
        catch (InvalidOperationException) { return null; }
        catch (FormatException) { return null; }
    }

    private static bool PointInPolygon(double lat, double lng, IReadOnlyList<(double Lat, double Lng)> ring)
    {
        bool inside = false;
        for (int i = 0, j = ring.Count - 1; i < ring.Count; j = i++)
        {
            (double yi, double xi) = ring[i];
            (double yj, double xj) = ring[j];
            bool intersects = ((yi > lat) != (yj > lat)) &&
                              (lng < (xj - xi) * (lat - yi) / ((yj - yi) == 0 ? double.Epsilon : yj - yi) + xi);
            if (intersects) inside = !inside;
        }
        return inside;
    }

    private static double DistanceMeters(double lat1, double lng1, double lat2, double lng2)
    {
        const double EarthRadiusMeters = 6_371_000;
        double dLat = (lat2 - lat1) * Math.PI / 180;
        double dLng = (lng2 - lng1) * Math.PI / 180;
        double a = Math.Pow(Math.Sin(dLat / 2), 2) +
                   Math.Cos(lat1 * Math.PI / 180) * Math.Cos(lat2 * Math.PI / 180) *
                   Math.Pow(Math.Sin(dLng / 2), 2);
        return 2 * EarthRadiusMeters * Math.Asin(Math.Sqrt(a));
    }

    private readonly record struct GeoPointValues(decimal Lat, decimal Lng, decimal SpeedMph, short Heading);

    private static GeoPointValues ReadGeo(GeoPoint p)
    {
        decimal speedMph = p.SpeedKph is { } kph ? (decimal)(kph * KphToMph) : 0m;
        short heading = p.HeadingDeg is { } h
            ? (short)(((int)Math.Round(h) % 360 + 360) % 360)
            : (short)0;
        return new GeoPointValues((decimal)p.Lat, (decimal)p.Lng, speedMph, heading);
    }

    /// <summary>Maps the canonical provenance category onto the live-map <c>source</c> vocabulary (telematics 001).</summary>
    private static string MapSource(TelemetrySource source) => source switch
    {
        TelemetrySource.DirectDevice => "gateway",
        TelemetrySource.VendorCloud => "partner_api",
        TelemetrySource.MobileApp => "mobile_app",
        TelemetrySource.Simulator => "simulator",
        TelemetrySource.Seed => "seed",
        TelemetrySource.Import => "import",
        TelemetrySource.Manual => "manual",
        _ => "unknown",
    };

    private static string SerializeQuality(QualityFlags q) => JsonSerializer.Serialize(new
    {
        duplicate = q.IsDuplicate,
        out_of_order = q.IsOutOfOrder,
        replay = q.IsReplay,
        stale = q.IsStale,
        clock_skew = q.ClockSkewSuspected,
        teleport = q.TeleportSuspected,
        impossible_speed = q.ImpossibleSpeed,
        gps_jamming = q.GpsJammingSuspected,
    });

    private static decimal Clamp01(double v) => (decimal)Math.Clamp(v, 0d, 1d);

    private static DateTime Utc(DateTime value) =>
        value.Kind == DateTimeKind.Utc ? value : DateTime.SpecifyKind(value, DateTimeKind.Utc);

    private static string? NullIfEmpty(string? s) => string.IsNullOrEmpty(s) ? null : s;
}
