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
/// Postgres-backed <see cref="IPositionProjectionStore"/>. Enforces the two invariants of the
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
/// Doing the inbox insert and the snapshot upsert atomically is what makes the dedupe reliable: a
/// crash between them can never leave an event marked "seen" but not projected, nor projected but
/// not marked seen.
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

        await using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);

        await using NpgsqlTransaction tx =
            await connection.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);

        // Scope the transaction to the event's tenant so the RLS predicates on both tables admit it.
        await SetTenantAsync(connection, tx, evt.CompanyId, cancellationToken).ConfigureAwait(false);

        // ── (a) idempotent inbox insert ─────────────────────────────────────────
        int inboxRows = await InsertInboxAsync(connection, tx, evt, cancellationToken).ConfigureAwait(false);
        if (inboxRows == 0)
        {
            // Already projected once — commit the (empty) transaction and no-op the snapshot.
            await tx.CommitAsync(cancellationToken).ConfigureAwait(false);
            return ProjectionOutcome.DuplicateIgnored;
        }

        // The event is now recorded as seen. Only positional, vehicle-bound events reach the snapshot.
        if (evt.Location is null)
        {
            await tx.CommitAsync(cancellationToken).ConfigureAwait(false);
            return ProjectionOutcome.NoLocation;
        }

        if (evt.VehicleId is not { } vehicleId)
        {
            await tx.CommitAsync(cancellationToken).ConfigureAwait(false);
            return ProjectionOutcome.NoVehicle;
        }

        // ── (b) monotonic snapshot upsert ───────────────────────────────────────
        int upsertRows = await UpsertLatestPositionAsync(connection, tx, evt, vehicleId, cancellationToken)
            .ConfigureAwait(false);

        await tx.CommitAsync(cancellationToken).ConfigureAwait(false);

        // Zero rows means the WHERE guard rejected an older-or-equal-blocked fix. Because the seen-set
        // gate above already removed exact duplicates, a 0 here is specifically a stale (older) fix.
        return upsertRows == 0 ? ProjectionOutcome.StaleIgnored : ProjectionOutcome.Applied;
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
        NpgsqlConnection connection, NpgsqlTransaction tx, CanonicalTelemetryEvent evt, long vehicleId, CancellationToken ct)
    {
        // correlation_id is deliberately NOT written here — see the stage12a drift note in the
        // 004 migration. The clean UUID correlation anchor lives on the inbox row.
        const string sql = """
            INSERT INTO latest_vehicle_positions
                (company_id, vehicle_id, device_id, lat, lng, speed_mph, heading,
                 source, provider, protocol, adapter_version, confidence, trust_score,
                 quality_flags, device_fix_time, gateway_received_at, normalized_at,
                 event_time, received_at, event_count)
            VALUES
                (@company_id, @vehicle_id, NULL, @lat, @lng, @speed_mph, @heading,
                 @source, @provider, @protocol, @adapter_version, @confidence, @trust_score,
                 @quality_flags::jsonb, @device_fix_time, @gateway_received_at, @normalized_at,
                 @event_time, @received_at, 1)
            ON CONFLICT (company_id, vehicle_id) DO UPDATE SET
                lat = EXCLUDED.lat, lng = EXCLUDED.lng,
                speed_mph = EXCLUDED.speed_mph, heading = EXCLUDED.heading,
                source = EXCLUDED.source, provider = EXCLUDED.provider,
                protocol = EXCLUDED.protocol, adapter_version = EXCLUDED.adapter_version,
                confidence = EXCLUDED.confidence, trust_score = EXCLUDED.trust_score,
                quality_flags = EXCLUDED.quality_flags,
                device_fix_time = EXCLUDED.device_fix_time,
                gateway_received_at = EXCLUDED.gateway_received_at,
                normalized_at = EXCLUDED.normalized_at,
                event_time = EXCLUDED.event_time,
                received_at = EXCLUDED.received_at,
                event_count = latest_vehicle_positions.event_count + 1
            WHERE EXCLUDED.device_fix_time IS NOT NULL
              AND (latest_vehicle_positions.device_fix_time IS NULL
                   OR EXCLUDED.device_fix_time >= latest_vehicle_positions.device_fix_time);
            """;

        GeoPointValues geo = ReadGeo(evt.Location!.Value);

        await using var cmd = new NpgsqlCommand(sql, connection, tx);
        cmd.Parameters.Add(new NpgsqlParameter("company_id", NpgsqlDbType.Bigint) { Value = evt.CompanyId });
        cmd.Parameters.Add(new NpgsqlParameter("vehicle_id", NpgsqlDbType.Bigint) { Value = vehicleId });
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

        return await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
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
