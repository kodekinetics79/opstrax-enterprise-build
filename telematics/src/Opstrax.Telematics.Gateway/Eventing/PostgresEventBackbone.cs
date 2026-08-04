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
            if (envelope.TenantId != canonicalPayload.TenantId || envelope.CompanyId != canonicalPayload.CompanyId)
                throw new InvalidOperationException("Envelope ownership does not match canonical payload ownership.");
            string expectedKey = TelematicsEventKey.ForDevice(
                canonicalPayload.TenantId, canonicalPayload.CompanyId, canonicalPayload.DeviceId);
            if (!string.Equals(key, expectedKey, StringComparison.Ordinal))
                throw new InvalidOperationException("Telemetry partition key does not match registry-resolved ownership.");
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
                (company_id, vehicle_id, device_id, correlation_id, event_type,
                 lat, lng, speed_mph, heading, source, provider, protocol,
                 adapter_version, confidence, trust_score, quality_flags, payload,
                 device_fix_time, gateway_received_at, event_time)
            VALUES
                (@company_id, @vehicle_id, @device_id, @correlation_id, 'location.updated',
                 @lat, @lng, @speed_mph, @heading, @source, @provider, @protocol,
                 @adapter_version, @confidence, @trust_score, @quality_flags::jsonb, @payload::jsonb,
                 @device_fix_time, @gateway_received_at, @event_time);
            """;
        await using var command = new NpgsqlCommand(sql, connection, transaction);
        command.Parameters.AddWithValue("company_id", evt.CompanyId);
        command.Parameters.AddWithValue("vehicle_id", (object?)evt.VehicleId ?? DBNull.Value);
        command.Parameters.AddWithValue("device_id", long.TryParse(evt.DeviceId, out long deviceId) ? deviceId : DBNull.Value);
        command.Parameters.AddWithValue("correlation_id", evt.CorrelationId);
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
        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
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
