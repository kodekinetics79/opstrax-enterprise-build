using System.Text.Json;
using Npgsql;
using NpgsqlTypes;
using Opstrax.Telematics.Contracts;
using Opstrax.Telematics.Contracts.Eventing;

namespace Opstrax.Telematics.Gateway.Eventing;

/// <summary>
/// Production gateway backbone adapter. The edge currently publishes canonical normalized events;
/// this implementation durably accepts them into <c>canonical_telemetry_events</c>. An advisory
/// transaction lock plus the envelope EventId stored in JSON makes an ambiguous retry idempotent
/// even though the partitioned historical table cannot carry a global EventId unique constraint.
/// </summary>
internal sealed class PostgresEventBackbone(string systemConnectionString) : IEventBackbone
{
    private static readonly HashSet<string> CustomerSafeSignalEvidenceHeaders = new(StringComparer.Ordinal)
    {
        "j1939.pgn",
        "j1939.spns",
        "j1939.source_address",
        "j1939.destination_address",
        "j1939.adapter_type",
        "j1939.channel",
        "j1939.capture_references",
    };

    private readonly string _connectionString = string.IsNullOrWhiteSpace(systemConnectionString)
        ? throw new ArgumentException("A telematics system connection string is required.", nameof(systemConnectionString))
        : systemConnectionString;

    public async Task PublishAsync<T>(
        string topic,
        string key,
        EventEnvelope<T> envelope,
        CancellationToken cancellationToken = default)
    {
        TelemetryRejection? rejectionPayload = envelope.Payload as TelemetryRejection;
        CanonicalTelemetryEvent? canonicalPayload = envelope.Payload as CanonicalTelemetryEvent;
        if (topic == TelematicsTopics.TelemetryRejected && rejectionPayload is not null)
        {
            if (envelope.TenantId != Guid.Empty || envelope.CompanyId != 0)
                throw new InvalidOperationException("A gateway rejection must remain unbound to a tenant.");
        }
        else if (topic == TelematicsTopics.TelemetryNormalized && canonicalPayload is not null)
        {
            if (canonicalPayload.CompanyId <= 0 || canonicalPayload.TenantId == Guid.Empty)
                throw new InvalidOperationException("Canonical telemetry requires registry-resolved tenant ownership.");
            if (envelope.TenantId != canonicalPayload.TenantId || envelope.CompanyId != canonicalPayload.CompanyId)
                throw new InvalidOperationException("Envelope ownership does not match canonical payload ownership.");
            string expectedKey = TelematicsEventKey.ForDevice(
                canonicalPayload.TenantId, canonicalPayload.CompanyId, canonicalPayload.DeviceId);
            if (!string.Equals(key, expectedKey, StringComparison.Ordinal))
                throw new InvalidOperationException("Telemetry partition key does not match registry-resolved ownership.");
            if (canonicalPayload.Signals.Count > 0 && !long.TryParse(canonicalPayload.DeviceId, out _))
                throw new InvalidOperationException("Canonical device signals require the numeric registry device identity.");
        }
        else
        {
            throw new NotSupportedException("The production edge backbone accepts normalized telemetry and masked rejection events only.");
        }

        await using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);

        await using (var scope = new NpgsqlCommand(
            "SELECT set_config('app.platform_admin','on',true)", connection, transaction))
        {
            await scope.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }

        if (rejectionPayload is not null)
        {
            await PersistRejectionAsync(connection, transaction, envelope.EventId, envelope.CorrelationId,
                rejectionPayload, cancellationToken).ConfigureAwait(false);
            await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
            return;
        }

        CanonicalTelemetryEvent evt = canonicalPayload!;

        await using (var advisoryLock = new NpgsqlCommand(
            "SELECT pg_advisory_xact_lock(hashtextextended(@event_id::text,0))", connection, transaction))
        {
            advisoryLock.Parameters.AddWithValue("event_id", envelope.EventId);
            await advisoryLock.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }

        await using (var exists = new NpgsqlCommand(
            "SELECT 1 FROM canonical_telemetry_events WHERE payload->>'_envelopeEventId'=@event_id LIMIT 1",
            connection, transaction))
        {
            exists.Parameters.AddWithValue("event_id", envelope.EventId.ToString("D"));
            if (await exists.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false) is not null)
            {
                await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
                return;
            }
        }

        string payload = JsonSerializer.Serialize(new
        {
            _envelopeEventId = envelope.EventId,
            envelope.CorrelationId,
            envelope.CausationId,
            envelope.TenantId,
            envelope.CompanyId,
            envelope.SchemaVersion,
            envelope.Headers,
            Event = evt,
        });
        const string sql = """
            INSERT INTO canonical_telemetry_events
                (company_id, vehicle_id, device_id, installation_id, assignment_id, trip_id,
                 driver_id, correlation_id, event_type,
                 lat, lng, speed_mph, heading, source, provider, protocol,
                 adapter_version, confidence, trust_score, quality_flags, payload,
                 device_fix_time, gateway_received_at, event_time)
            VALUES
                (@company_id, @vehicle_id, @device_id, @installation_id, @assignment_id, @trip_id,
                 @driver_id, @correlation_id, @event_type,
                 @lat, @lng, @speed_mph, @heading, @source, @provider, @protocol,
                 @adapter_version, @confidence, @trust_score, @quality_flags::jsonb, @payload::jsonb,
                 @device_fix_time, @gateway_received_at, @event_time);
            """;
        await using var command = new NpgsqlCommand(sql, connection, transaction);
        command.Parameters.AddWithValue("company_id", evt.CompanyId);
        command.Parameters.AddWithValue("vehicle_id", (object?)evt.VehicleId ?? DBNull.Value);
        command.Parameters.AddWithValue("device_id", long.TryParse(evt.DeviceId, out long deviceId) ? deviceId : DBNull.Value);
        command.Parameters.AddWithValue("installation_id", (object?)evt.InstallationId ?? DBNull.Value);
        command.Parameters.AddWithValue("assignment_id", (object?)evt.AssignmentId ?? DBNull.Value);
        command.Parameters.AddWithValue("trip_id", (object?)evt.TripId ?? DBNull.Value);
        command.Parameters.AddWithValue("driver_id", (object?)evt.DriverId ?? DBNull.Value);
        command.Parameters.AddWithValue("correlation_id", evt.CorrelationId);
        command.Parameters.AddWithValue(
            "event_type",
            ClassifyCanonicalEventType(evt));
        command.Parameters.AddWithValue("lat", (object?)evt.Location?.Lat ?? DBNull.Value);
        command.Parameters.AddWithValue("lng", (object?)evt.Location?.Lng ?? DBNull.Value);
        command.Parameters.AddWithValue("speed_mph", evt.Location?.SpeedKph is { } kph ? kph * 0.621371 : DBNull.Value);
        command.Parameters.AddWithValue("heading", (object?)evt.Location?.HeadingDeg ?? DBNull.Value);
        command.Parameters.AddWithValue("source", evt.Source.ToString());
        command.Parameters.AddWithValue("provider", evt.AdapterName);
        command.Parameters.AddWithValue("protocol", evt.ProtocolName);
        command.Parameters.AddWithValue("adapter_version", evt.AdapterVersion);
        command.Parameters.AddWithValue("confidence", (decimal)Math.Clamp(evt.Confidence, 0, 1));
        command.Parameters.AddWithValue("trust_score", (decimal)Math.Clamp(evt.TrustScore, 0, 1));
        command.Parameters.Add(new NpgsqlParameter("quality_flags", NpgsqlDbType.Text) { Value = JsonSerializer.Serialize(evt.Quality) });
        command.Parameters.Add(new NpgsqlParameter("payload", NpgsqlDbType.Text) { Value = payload });
        command.Parameters.AddWithValue("device_fix_time", Utc(evt.OccurredAtDeviceUtc));
        command.Parameters.AddWithValue("gateway_received_at", Utc(evt.ReceivedAtGatewayUtc));
        command.Parameters.AddWithValue("event_time", Utc(evt.OccurredAtDeviceUtc));
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        await PersistLatestSignalsAsync(
            connection,
            transaction,
            evt,
            envelope.EventId,
            envelope.CorrelationId,
            envelope.Headers,
            cancellationToken).ConfigureAwait(false);
        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
    }

    private static async Task PersistLatestSignalsAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        CanonicalTelemetryEvent evt,
        Guid eventId,
        Guid correlationId,
        IReadOnlyDictionary<string, string> headers,
        CancellationToken cancellationToken)
    {
        if (evt.Signals.Count == 0)
            return;

        if (!long.TryParse(evt.DeviceId, out long deviceId))
            throw new InvalidOperationException("Canonical device signals require the numeric registry device identity.");

        const string sql = """
            INSERT INTO latest_device_signals
                (company_id,device_id,vehicle_id,signal_path,value_json,unit,availability,
                 source,transport,protocol,adapter_name,adapter_version,trust_score,confidence,
                 quality_flags,evidence_headers,event_id,correlation_id,observed_at,
                 gateway_received_at,normalized_at,certification_claim,updated_at)
            VALUES
                (@company_id,@device_id,@vehicle_id,@signal_path,@value_json::jsonb,@unit,@availability,
                 @source,@transport,@protocol,@adapter_name,@adapter_version,@trust_score,@confidence,
                 @quality_flags::jsonb,@evidence_headers::jsonb,@event_id,@correlation_id,@observed_at,
                 @gateway_received_at,@normalized_at,FALSE,NOW())
            ON CONFLICT(company_id,device_id,signal_path) DO UPDATE SET
                vehicle_id=EXCLUDED.vehicle_id,
                value_json=EXCLUDED.value_json,
                unit=EXCLUDED.unit,
                availability=EXCLUDED.availability,
                source=EXCLUDED.source,
                transport=EXCLUDED.transport,
                protocol=EXCLUDED.protocol,
                adapter_name=EXCLUDED.adapter_name,
                adapter_version=EXCLUDED.adapter_version,
                trust_score=EXCLUDED.trust_score,
                confidence=EXCLUDED.confidence,
                quality_flags=EXCLUDED.quality_flags,
                evidence_headers=EXCLUDED.evidence_headers,
                event_id=EXCLUDED.event_id,
                correlation_id=EXCLUDED.correlation_id,
                observed_at=EXCLUDED.observed_at,
                gateway_received_at=EXCLUDED.gateway_received_at,
                normalized_at=EXCLUDED.normalized_at,
                certification_claim=FALSE,
                updated_at=NOW()
            WHERE EXCLUDED.observed_at>latest_device_signals.observed_at
               OR (EXCLUDED.observed_at=latest_device_signals.observed_at
                   AND EXCLUDED.normalized_at>latest_device_signals.normalized_at);
            """;
        string qualityFlags = JsonSerializer.Serialize(evt.Quality);
        string evidenceHeaders = JsonSerializer.Serialize(headers
            .Where(entry => CustomerSafeSignalEvidenceHeaders.Contains(entry.Key) && entry.Value.Length <= 1024)
            .ToDictionary(entry => entry.Key, entry => entry.Value, StringComparer.Ordinal));
        foreach (var (signalPath, signal) in evt.Signals)
        {
            await using var command = new NpgsqlCommand(sql, connection, transaction);
            command.Parameters.AddWithValue("company_id", evt.CompanyId);
            command.Parameters.AddWithValue("device_id", deviceId);
            command.Parameters.AddWithValue("vehicle_id", (object?)evt.VehicleId ?? DBNull.Value);
            command.Parameters.AddWithValue("signal_path", signalPath);
            command.Parameters.Add(new NpgsqlParameter("value_json", NpgsqlDbType.Text)
            {
                Value = signal.Value is null ? DBNull.Value : JsonSerializer.Serialize(signal.Value),
            });
            command.Parameters.AddWithValue("unit", signal.Unit);
            command.Parameters.AddWithValue("availability", signal.Availability.ToString());
            command.Parameters.AddWithValue("source", signal.Source.ToString());
            command.Parameters.AddWithValue("transport", evt.Transport.ToString());
            command.Parameters.AddWithValue("protocol", evt.ProtocolName);
            command.Parameters.AddWithValue("adapter_name", evt.AdapterName);
            command.Parameters.AddWithValue("adapter_version", evt.AdapterVersion);
            command.Parameters.AddWithValue("trust_score", (decimal)Math.Clamp(evt.TrustScore, 0, 1));
            command.Parameters.AddWithValue("confidence", (decimal)Math.Clamp(signal.Confidence, 0, 1));
            command.Parameters.Add(new NpgsqlParameter("quality_flags", NpgsqlDbType.Text) { Value = qualityFlags });
            command.Parameters.Add(new NpgsqlParameter("evidence_headers", NpgsqlDbType.Text) { Value = evidenceHeaders });
            command.Parameters.AddWithValue("event_id", eventId);
            command.Parameters.AddWithValue("correlation_id", correlationId);
            command.Parameters.AddWithValue("observed_at", Utc(evt.OccurredAtDeviceUtc));
            command.Parameters.AddWithValue("gateway_received_at", Utc(evt.ReceivedAtGatewayUtc));
            command.Parameters.AddWithValue("normalized_at", Utc(evt.NormalizedAtUtc));
            await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }
    }

    internal static string ClassifyCanonicalEventType(CanonicalTelemetryEvent evt)
    {
        ArgumentNullException.ThrowIfNull(evt);
        return evt.Location is not null ? "location.updated" :
            evt.Signals.Count > 0 ? "vehicle.signal" :
            "device.heartbeat";
    }

    public IEventSubscription<T> Subscribe<T>(string topic, Guid? tenantFilter = null) =>
        throw new NotSupportedException("The edge process is a production publisher; durable consumers run outside this process.");

    private static async Task PersistRejectionAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        Guid eventId,
        Guid correlationId,
        TelemetryRejection rejection,
        CancellationToken cancellationToken)
    {
        const string sql = """
            INSERT INTO telemetry_gateway_rejections
                (event_id, correlation_id, claimed_identifier_masked, reason, protocol,
                 message_type, received_at, raw_frame_bytes, remote_endpoint)
            VALUES
                (@event_id, @correlation_id, @identifier, @reason, @protocol,
                 @message_type, @received_at, @raw_frame_bytes, @remote_endpoint)
            ON CONFLICT (event_id) DO NOTHING;
            """;
        await using var command = new NpgsqlCommand(sql, connection, transaction);
        command.Parameters.AddWithValue("event_id", eventId);
        command.Parameters.AddWithValue("correlation_id", correlationId);
        command.Parameters.AddWithValue("identifier", rejection.ClaimedIdentifierMasked);
        command.Parameters.AddWithValue("reason", rejection.Reason);
        command.Parameters.AddWithValue("protocol", rejection.ProtocolName);
        command.Parameters.AddWithValue("message_type", rejection.MessageType);
        command.Parameters.AddWithValue("received_at", rejection.ReceivedAtGatewayUtc);
        command.Parameters.AddWithValue("raw_frame_bytes", rejection.RawFrameBytes);
        command.Parameters.AddWithValue("remote_endpoint", rejection.RemoteEndpoint);
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    private static DateTime Utc(DateTime value) =>
        value.Kind == DateTimeKind.Utc ? value : DateTime.SpecifyKind(value, DateTimeKind.Utc);
}
