using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Npgsql;
using NpgsqlTypes;
using Opstrax.Telematics.Contracts;
using Opstrax.Telematics.Contracts.Diagnostics;
using Opstrax.Telematics.Contracts.Eventing;
using Opstrax.Telematics.Contracts.Provenance;

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
            if (envelope.EventId != canonicalPayload.EventId ||
                envelope.CorrelationId != canonicalPayload.CorrelationId ||
                envelope.SchemaVersion != canonicalPayload.SchemaVersion)
                throw new InvalidOperationException("Envelope identity does not match canonical payload identity.");
            string expectedKey = TelematicsEventKey.ForDevice(
                canonicalPayload.TenantId, canonicalPayload.CompanyId, canonicalPayload.DeviceId);
            if (!string.Equals(key, expectedKey, StringComparison.Ordinal))
                throw new InvalidOperationException("Telemetry partition key does not match registry-resolved ownership.");
            if ((canonicalPayload.Signals.Count > 0 || canonicalPayload.Diagnostic is not null) &&
                !long.TryParse(canonicalPayload.DeviceId, out _))
                throw new InvalidOperationException("Canonical device evidence requires the numeric registry device identity.");
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
        await PersistDiagnosticEvidenceAsync(
            connection,
            transaction,
            evt,
            envelope.EventId,
            envelope.CorrelationId,
            envelope.Headers,
            cancellationToken).ConfigureAwait(false);
        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Projects authenticated, canonical J1939 diagnostics into the customer maintenance tables
    /// in the same transaction as the immutable canonical event. Every DM1/DM2 DTC is retained as
    /// an occurrence. Only fresh direct-CAN DM1 may advance the current fault projection. DM2 is
    /// historical evidence and never clears or changes current state. Vehicle holds remain outside
    /// this edge projection until their independent safety gate is complete.
    /// </summary>
    private static async Task PersistDiagnosticEvidenceAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        CanonicalTelemetryEvent evt,
        Guid eventId,
        Guid correlationId,
        IReadOnlyDictionary<string, string> headers,
        CancellationToken cancellationToken)
    {
        DiagnosticSnapshot? diagnostic = evt.Diagnostic;
        if (diagnostic is null)
            return;

        // This bounded projection currently owns direct physical J1939/CAN observations only.
        // Other canonical diagnostic protocols remain durable in canonical_telemetry_events until
        // their protocol-specific semantics and source authentication receive an equivalent gate.
        if (!string.Equals(diagnostic.Protocol, "J1939", StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(evt.ProtocolName, "J1939", StringComparison.OrdinalIgnoreCase) ||
            evt.Source != TelemetrySource.DirectDevice ||
            evt.Transport != Transport.Can)
            return;

        ValidateJ1939Diagnostic(evt, diagnostic);
        if (!long.TryParse(evt.DeviceId, out long numericDeviceId))
            throw new InvalidOperationException("Canonical J1939 diagnostics require the numeric registry device identity.");

        DiagnosticProjectionOwner? owner = await ResolveDiagnosticOwnerAsync(
            connection,
            transaction,
            evt.CompanyId,
            numericDeviceId,
            evt.VehicleId,
            Utc(evt.OccurredAtDeviceUtc),
            cancellationToken).ConfigureAwait(false);

        // A canonical event remains immutable evidence when its historical installation cannot be
        // resolved unambiguously or the registry identity is under quarantine. It must not mutate
        // customer maintenance state under an inferred vehicle or branch.
        if (owner is null)
            return;

        string sourceEventId = eventId.ToString("D");
        string lampStatus = JsonSerializer.Serialize(diagnostic.Lamps);
        string? bus = headers.TryGetValue("j1939.channel", out string? channel) &&
                      channel.Length <= 40 &&
                      channel.All(value => char.IsAsciiLetterOrDigit(value) || value is '_' or '-' or '.' or ':')
            ? channel
            : null;
        string fingerprint = Convert.ToHexString(
            SHA256.HashData(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(diagnostic)))).ToLowerInvariant();
        string severity = DeriveDiagnosticSeverity(diagnostic.Lamps);

        foreach ((DiagnosticTroubleCode dtc, int ordinal) in diagnostic.TroubleCodes.Select((value, index) => (value, index)))
        {
            string safeEvidence = JsonSerializer.Serialize(new
            {
                eventId,
                correlationId,
                diagnostic.Pgn,
                diagnostic.IsActive,
                diagnostic.SourceAddress,
                dtc.CanonicalIdentity,
                dtc.ConversionMethod,
                Adapter = evt.AdapterName,
                evt.AdapterVersion,
                evt.TrustScore,
                evt.Confidence,
            });
            await using var occurrence = new NpgsqlCommand(
                """
                INSERT INTO fault_occurrences
                    (company_id,branch_id,device_id,vehicle_id,source_event_id,dtc_ordinal,canonical_dtc,
                     observed_at,controller,source_address,bus,protocol,code,spn,fmi,occurrence_count,
                     lamp_status,raw_evidence,payload_fingerprint)
                VALUES
                    (@company_id,@branch_id,@device_id,@vehicle_id,@source_event_id,@ordinal,@canonical_dtc,
                     @observed_at,NULL,@source_address,@bus,'J1939',@code,@spn,@fmi,@occurrence_count,
                     @lamp_status::jsonb,@raw_evidence::jsonb,@payload_fingerprint)
                ON CONFLICT (company_id,device_id,source_event_id,dtc_ordinal,canonical_dtc) DO NOTHING;
                """,
                connection,
                transaction);
            AddDiagnosticParameters(
                occurrence,
                evt.CompanyId,
                owner.Value,
                sourceEventId,
                ordinal,
                dtc,
                diagnostic.SourceAddress,
                bus,
                Utc(evt.OccurredAtDeviceUtc),
                lampStatus,
                safeEvidence);
            occurrence.Parameters.AddWithValue("payload_fingerprint", fingerprint);
            await occurrence.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);

            // An old DM1 remains useful occurrence evidence, but it cannot establish a live fault.
            // DM2 is historical by definition and follows the same evidence-only path.
            if (!diagnostic.IsActive || evt.Quality.IsStale)
                continue;

            await using var state = new NpgsqlCommand(
                """
                INSERT INTO fault_codes
                    (company_id,branch_id,device_id,vehicle_id,source_event_id,canonical_identity,last_source_event_id,
                     code_type,protocol,code,description,severity,controller,source_address,bus,spn,fmi,
                     lamp_status,raw_evidence,observed_at,received_at,last_observed_at,occurrence_count,
                     status,first_seen_at,last_seen_at,updated_at)
                VALUES
                    (@company_id,@branch_id,@device_id,@vehicle_id,@source_event_id,@canonical_dtc,@source_event_id,
                     'J1939','J1939',@code,NULL,@severity,NULL,@source_address,@bus,@spn,@fmi,
                     @lamp_status::jsonb,@raw_evidence::jsonb,@observed_at,NOW(),@observed_at,@occurrence_count,
                     'active',@observed_at,@observed_at,NOW())
                ON CONFLICT (company_id,device_id,protocol,canonical_identity) DO UPDATE SET
                    branch_id=EXCLUDED.branch_id,
                    vehicle_id=EXCLUDED.vehicle_id,
                    source_event_id=EXCLUDED.source_event_id,
                    last_source_event_id=EXCLUDED.last_source_event_id,
                    code=EXCLUDED.code,
                    severity=EXCLUDED.severity,
                    source_address=EXCLUDED.source_address,
                    bus=EXCLUDED.bus,
                    spn=EXCLUDED.spn,
                    fmi=EXCLUDED.fmi,
                    lamp_status=EXCLUDED.lamp_status,
                    raw_evidence=EXCLUDED.raw_evidence,
                    observed_at=EXCLUDED.observed_at,
                    received_at=NOW(),
                    last_observed_at=EXCLUDED.last_observed_at,
                    occurrence_count=GREATEST(fault_codes.occurrence_count,EXCLUDED.occurrence_count),
                    status='active',
                    cleared_at=NULL,
                    clear_source=NULL,
                    last_seen_at=EXCLUDED.last_seen_at,
                    updated_at=NOW()
                WHERE fault_codes.last_observed_at<EXCLUDED.last_observed_at
                   OR (fault_codes.last_observed_at=EXCLUDED.last_observed_at
                       AND fault_codes.last_source_event_id<EXCLUDED.last_source_event_id);
                """,
                connection,
                transaction);
            AddDiagnosticParameters(
                state,
                evt.CompanyId,
                owner.Value,
                sourceEventId,
                ordinal,
                dtc,
                diagnostic.SourceAddress,
                bus,
                Utc(evt.OccurredAtDeviceUtc),
                lampStatus,
                safeEvidence);
            state.Parameters.AddWithValue("severity", severity);
            await state.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }
    }

    private static void ValidateJ1939Diagnostic(CanonicalTelemetryEvent evt, DiagnosticSnapshot diagnostic)
    {
        int expectedPgn = diagnostic.IsActive ? 65_226 : 65_227;
        if (diagnostic.Pgn != expectedPgn)
            throw new InvalidOperationException("Canonical J1939 diagnostic kind does not match its PGN.");
        if (diagnostic.SourceAddress is null or < 0 or > 253)
            throw new InvalidOperationException("Canonical J1939 diagnostic source address is outside supported bounds.");
        if (diagnostic.TroubleCodes.Count > 445)
            throw new InvalidOperationException("Canonical J1939 diagnostic exceeds the bounded transport payload.");
        if (evt.DtcCodes.Count != diagnostic.TroubleCodes.Count ||
            !evt.DtcCodes.SequenceEqual(diagnostic.TroubleCodes.Select(value => value.Code), StringComparer.Ordinal))
            throw new InvalidOperationException("Canonical J1939 DTC summary does not match the structured diagnostic.");
        if (new[]
            {
                diagnostic.Lamps.Protect,
                diagnostic.Lamps.AmberWarning,
                diagnostic.Lamps.RedStop,
                diagnostic.Lamps.MalfunctionIndicator,
                diagnostic.Lamps.ProtectFlash,
                diagnostic.Lamps.AmberWarningFlash,
                diagnostic.Lamps.RedStopFlash,
                diagnostic.Lamps.MalfunctionIndicatorFlash,
            }.Any(value => !Enum.IsDefined(value)))
            throw new InvalidOperationException("Canonical J1939 diagnostic contains an invalid lamp state.");

        var identities = new HashSet<string>(StringComparer.Ordinal);
        foreach (DiagnosticTroubleCode dtc in diagnostic.TroubleCodes)
        {
            if (dtc.Spn is null or < 0 or > 524_287 || dtc.Fmi is null or < 0 or > 31 ||
                dtc.OccurrenceCount is < 1 or > 127)
                throw new InvalidOperationException("Canonical J1939 DTC is outside protocol bounds.");
            string expectedCode = $"SPN-{dtc.Spn}-FMI-{dtc.Fmi}";
            string expectedIdentity = $"J1939:SA:{diagnostic.SourceAddress:D2}:SPN:{dtc.Spn}:FMI:{dtc.Fmi}";
            if (!string.Equals(dtc.Code, expectedCode, StringComparison.Ordinal) ||
                !string.Equals(dtc.CanonicalIdentity, expectedIdentity, StringComparison.Ordinal))
                throw new InvalidOperationException("Canonical J1939 DTC identity is inconsistent with its decoded fields.");
            if (!identities.Add(dtc.CanonicalIdentity))
                throw new InvalidOperationException("Canonical J1939 diagnostic contains duplicate DTC identities.");
        }
    }

    private static async Task<DiagnosticProjectionOwner?> ResolveDiagnosticOwnerAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        long companyId,
        long deviceId,
        long? eventVehicleId,
        DateTime observedAt,
        CancellationToken cancellationToken)
    {
        if (!eventVehicleId.HasValue)
            return null;

        const string sql = """
            SELECT e.device_serial,i.vehicle_id,COALESCE(i.branch_id,v.branch_id,e.branch_id) branch_id
              FROM eld_devices e
              JOIN device_installations i
                ON i.company_id=e.company_id AND i.device_id=e.id AND i.vehicle_id=@vehicle_id
               AND i.status IN ('Installed','Verified','Removed')
               AND i.effective_from<=@observed_at
               AND (i.effective_to IS NULL OR i.effective_to>@observed_at)
              JOIN vehicles v
                ON v.company_id=i.company_id AND v.id=i.vehicle_id AND v.deleted_at IS NULL
             WHERE e.company_id=@company_id AND e.id=@device_id AND e.deleted_at IS NULL
               AND LOWER(BTRIM(e.status))='active' AND e.revoked_at IS NULL
               AND LOWER(BTRIM(COALESCE(e.device_state,''))) NOT IN
                   ('suspended','quarantined','lost','decommissioning','decommissioned','retired')
               AND NULLIF(BTRIM(e.device_serial),'') IS NOT NULL
               AND NOT EXISTS (
                    SELECT 1 FROM device_installation_quarantine q
                     WHERE q.company_id=e.company_id AND q.device_id=e.id AND q.resolved_at IS NULL)
             ORDER BY i.effective_from DESC,i.id DESC
             LIMIT 2;
            """;
        await using var command = new NpgsqlCommand(sql, connection, transaction);
        command.Parameters.AddWithValue("company_id", companyId);
        command.Parameters.AddWithValue("device_id", deviceId);
        command.Parameters.AddWithValue("vehicle_id", eventVehicleId.Value);
        command.Parameters.AddWithValue("observed_at", observedAt);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        DiagnosticProjectionOwner? owner = null;
        int matches = 0;
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            matches++;
            owner = new DiagnosticProjectionOwner(
                reader.GetString(0),
                reader.GetInt64(1),
                reader.IsDBNull(2) ? null : reader.GetInt64(2));
        }
        return matches == 1 ? owner : null;
    }

    private static void AddDiagnosticParameters(
        NpgsqlCommand command,
        long companyId,
        DiagnosticProjectionOwner owner,
        string sourceEventId,
        int ordinal,
        DiagnosticTroubleCode dtc,
        int? sourceAddress,
        string? bus,
        DateTime observedAt,
        string lampStatus,
        string safeEvidence)
    {
        command.Parameters.AddWithValue("company_id", companyId);
        command.Parameters.AddWithValue("branch_id", (object?)owner.BranchId ?? DBNull.Value);
        command.Parameters.AddWithValue("device_id", owner.DeviceSerial);
        command.Parameters.AddWithValue("vehicle_id", owner.VehicleId);
        command.Parameters.AddWithValue("source_event_id", sourceEventId);
        command.Parameters.AddWithValue("ordinal", ordinal);
        command.Parameters.AddWithValue("canonical_dtc", dtc.CanonicalIdentity);
        command.Parameters.AddWithValue("observed_at", observedAt);
        command.Parameters.AddWithValue("source_address", (object?)sourceAddress ?? DBNull.Value);
        command.Parameters.AddWithValue("bus", (object?)bus ?? DBNull.Value);
        command.Parameters.AddWithValue("code", dtc.Code);
        command.Parameters.AddWithValue("spn", (object?)dtc.Spn ?? DBNull.Value);
        command.Parameters.AddWithValue("fmi", (object?)dtc.Fmi ?? DBNull.Value);
        command.Parameters.AddWithValue("occurrence_count", dtc.OccurrenceCount);
        command.Parameters.Add(new NpgsqlParameter("lamp_status", NpgsqlDbType.Text) { Value = lampStatus });
        command.Parameters.Add(new NpgsqlParameter("raw_evidence", NpgsqlDbType.Text) { Value = safeEvidence });
    }

    private static string DeriveDiagnosticSeverity(DiagnosticLampSnapshot lamps)
    {
        if (lamps.RedStop == DiagnosticLampState.On || lamps.RedStopFlash == DiagnosticLampState.On)
            return "Critical";
        if (lamps.Protect == DiagnosticLampState.On || lamps.AmberWarning == DiagnosticLampState.On ||
            lamps.MalfunctionIndicator == DiagnosticLampState.On || lamps.ProtectFlash == DiagnosticLampState.On ||
            lamps.AmberWarningFlash == DiagnosticLampState.On || lamps.MalfunctionIndicatorFlash == DiagnosticLampState.On)
            return "Warning";
        return "Info";
    }

    private readonly record struct DiagnosticProjectionOwner(string DeviceSerial, long VehicleId, long? BranchId);

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
        return evt.Diagnostic is not null ? "diagnostic.event" :
            evt.Location is not null ? "location.updated" :
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
