using System.Security.Cryptography;
using Npgsql;
using Opstrax.Telematics.Contracts;
using Opstrax.Telematics.Contracts.Eventing;
using Opstrax.Telematics.Contracts.Identity;
using Opstrax.Telematics.Contracts.Provenance;
using Opstrax.Telematics.Contracts.Signals;
using Opstrax.Telematics.Gateway.Buffering;
using Opstrax.Telematics.Gateway.Eventing;
using Opstrax.Telematics.Gateway.Identity;
using Opstrax.Telematics.Gateway.Projection;
using Opstrax.Telematics.Gateway.Security.Replay;

namespace Opstrax.Telematics.IntegrationTests;

public sealed class PostgresProductionDurabilityTests
{
    [Fact]
    public async Task DurableReplay_ConcurrentDuplicatesConvergeOnOneStoredEventIdentityAcrossInstances()
    {
        await using var database = await IsolatedSchema.CreateAsync();
        await database.ExecuteAsync("""
            CREATE TABLE telemetry_replay_seen(
                id bigint GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
                device_id text NOT NULL, serial bigint NOT NULL,
                unwrapped_serial bigint NOT NULL, content_hash text NOT NULL,
                event_id uuid NOT NULL, device_fix_time timestamptz NULL,
                seen_at timestamptz NOT NULL DEFAULT now(),
                UNIQUE(device_id,unwrapped_serial,content_hash));
            CREATE TABLE telemetry_replay_device_state(
                device_id text PRIMARY KEY, last_raw_serial bigint NOT NULL,
                high_water_unwrapped bigint NOT NULL, updated_at timestamptz NOT NULL DEFAULT now());
            """);

        using var firstInstance = new PostgresReplayGuard(
            database.ScopedConnectionString, serialModulus: 65_536);
        using var secondInstance = new PostgresReplayGuard(
            database.ScopedConnectionString, serialModulus: 65_536);
        Task<ReplayDecision>[] attempts = Enumerable.Range(0, 16)
            .Select(index => (index & 1) == 0 ? firstInstance : secondInstance)
            .Select(guard => guard.CheckAsync(
                "shared-device", 12_345, "same-frame", DateTime.MinValue))
            .ToArray();

        ReplayDecision[] decisions = await Task.WhenAll(attempts);

        Assert.Single(decisions, decision => decision.Outcome == ReplayOutcome.Accept);
        Assert.Equal(15, decisions.Count(decision => decision.Outcome == ReplayOutcome.DuplicateReplay));
        Assert.True(decisions[0].EventId.HasValue);
        Guid eventId = decisions[0].EventId.Value;
        Assert.All(decisions, decision => Assert.Equal(eventId, decision.EventId));
        Assert.Equal(1, await database.ScalarLongAsync(
            "SELECT count(*) FROM telemetry_replay_seen WHERE device_id='shared-device'"));
        Assert.Equal(12_345, await database.ScalarLongAsync(
            "SELECT high_water_unwrapped FROM telemetry_replay_device_state WHERE device_id='shared-device'"));
    }

    [Fact]
    public async Task DurableReplay_UnwrapsGt06GenerationsAndBootstrapsAboveIncompleteLegacyHistory()
    {
        await using var database = await IsolatedSchema.CreateAsync();
        await database.ExecuteAsync("""
            CREATE TABLE telemetry_replay_seen(
                id bigint GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
                device_id text NOT NULL, serial bigint NOT NULL,
                unwrapped_serial bigint NOT NULL, content_hash text NOT NULL,
                event_id uuid NOT NULL, device_fix_time timestamptz NULL,
                seen_at timestamptz NOT NULL DEFAULT now(),
                UNIQUE(device_id,unwrapped_serial,content_hash));
            CREATE TABLE telemetry_replay_device_state(
                device_id text PRIMARY KEY, last_raw_serial bigint NOT NULL,
                high_water_unwrapped bigint NOT NULL, updated_at timestamptz NOT NULL DEFAULT now());
            INSERT INTO telemetry_replay_seen
                (device_id,serial,unwrapped_serial,content_hash,event_id,seen_at)
            VALUES
                ('legacy-device',65535,65535,'different-frame','10000000-0000-0000-0000-000000000001',now()-interval '2 days'),
                -- Raw-key uniqueness in the predecessor could retain an old generation of this
                -- repeated heartbeat while suppressing a later one; chronology is unknowable.
                ('legacy-device',40001,40001,'repeated-heartbeat','10000000-0000-0000-0000-000000000002',now()-interval '3 days');
            """);

        using var guard = new PostgresReplayGuard(database.ScopedConnectionString, serialModulus: 65_536);
        ReplayDecision cutover = await guard.CheckAsync(
            "legacy-device", 40001, "repeated-heartbeat", DateTime.MinValue);
        ReplayDecision cutoverRetry = await guard.CheckAsync(
            "legacy-device", 40001, "repeated-heartbeat", DateTime.MinValue);

        Assert.Equal(ReplayOutcome.Accept, cutover.Outcome);
        Assert.Equal(ReplayOutcome.DuplicateReplay, cutoverRetry.Outcome);
        Assert.NotEqual(Guid.Parse("10000000-0000-0000-0000-000000000002"), cutover.EventId);
        Assert.Equal(cutover.EventId, cutoverRetry.EventId);
        Assert.Equal(105537, await database.ScalarLongAsync(
            "SELECT high_water_unwrapped FROM telemetry_replay_device_state WHERE device_id='legacy-device'"));

        ReplayDecision nearWrap = await guard.CheckAsync("wrap-device", 65535, "near-wrap", DateTime.MinValue);
        ReplayDecision afterWrap = await guard.CheckAsync("wrap-device", 0, "after-wrap", DateTime.MinValue);
        ReplayDecision retry = await guard.CheckAsync("wrap-device", 0, "after-wrap", DateTime.MinValue);
        ReplayDecision latePreWrap = await guard.CheckAsync("wrap-device", 65535, "late-pre-wrap", DateTime.MinValue);

        Assert.Equal(ReplayOutcome.Accept, nearWrap.Outcome);
        Assert.Equal(ReplayOutcome.Accept, afterWrap.Outcome);
        Assert.NotEqual(nearWrap.EventId, afterWrap.EventId);
        Assert.Equal(ReplayOutcome.DuplicateReplay, retry.Outcome);
        Assert.Equal(afterWrap.EventId, retry.EventId);
        Assert.Equal(ReplayOutcome.OutOfOrder, latePreWrap.Outcome);
        Assert.Equal(65_536, await database.ScalarLongAsync(
            "SELECT high_water_unwrapped FROM telemetry_replay_device_state WHERE device_id='wrap-device'"));

        ReplayDecision repeatedFirst = await guard.CheckAsync(
            "heartbeat-device", 40000, "identical-heartbeat", DateTime.MinValue);
        await guard.CheckAsync("heartbeat-device", 60000, "advance-one", DateTime.MinValue);
        await guard.CheckAsync("heartbeat-device", 10000, "advance-wrap", DateTime.MinValue);
        ReplayDecision repeatedNextGeneration = await guard.CheckAsync(
            "heartbeat-device", 40000, "identical-heartbeat", DateTime.MinValue);
        Assert.Equal(ReplayOutcome.Accept, repeatedNextGeneration.Outcome);
        Assert.NotEqual(repeatedFirst.EventId, repeatedNextGeneration.EventId);

        await guard.CheckAsync("half-range-device", 0, "baseline", DateTime.MinValue);
        ReplayDecision ambiguousHalf = await guard.CheckAsync(
            "half-range-device", 32_768, "ambiguous", DateTime.MinValue);
        Assert.Equal(ReplayOutcome.OutOfOrder, ambiguousHalf.Outcome);
        Assert.Equal(0, await database.ScalarLongAsync(
            "SELECT high_water_unwrapped FROM telemetry_replay_device_state WHERE device_id='half-range-device'"));
    }

    [Fact]
    public async Task RawGt06Projection_IsAtomicAndReplaySafeAcrossHistoryLatestHeartbeatAndAlerts()
    {
        await using var database = await IsolatedSchema.CreateAsync();
        await database.ExecuteAsync("""
            CREATE TABLE vehicles(
                id bigint PRIMARY KEY, company_id bigint NOT NULL, branch_id bigint NULL,
                deleted_at timestamptz NULL);
            CREATE TABLE eld_devices(
                id bigint PRIMARY KEY, company_id bigint NOT NULL, vehicle_id bigint NULL,
                driver_id bigint NULL, status text NOT NULL DEFAULT 'Active',
                device_state text NULL, deleted_at timestamptz NULL,
                last_seen_at timestamptz NULL, last_heartbeat_at timestamptz NULL,
                updated_at timestamptz NULL);
            CREATE TABLE device_installations(
                id bigint PRIMARY KEY, company_id bigint NOT NULL,device_id bigint NOT NULL,
                vehicle_id bigint NULL,status text NOT NULL,effective_from timestamptz NOT NULL,
                effective_to timestamptz NULL);
            CREATE TABLE device_installation_quarantine(
                id bigint PRIMARY KEY,company_id bigint NOT NULL,device_id bigint NULL,
                resolved_at timestamptz NULL);
            CREATE TABLE dispatch_assignments(
                id bigint PRIMARY KEY,company_id bigint NOT NULL,vehicle_id bigint NULL,
                driver_id bigint NULL,trip_id bigint NULL,assigned_at timestamptz NOT NULL,
                cancelled_at timestamptz NULL,completed_at timestamptz NULL,
                actual_delivery_at timestamptz NULL);
            CREATE TABLE telemetry_projection_inbox(
                event_id uuid PRIMARY KEY, correlation_id uuid NOT NULL, tenant_id uuid NOT NULL,
                company_id bigint NOT NULL, device_id text NULL, vehicle_id bigint NULL,
                device_fix_time timestamptz NOT NULL, schema_version int NOT NULL);
            CREATE TABLE location_events(
                id bigserial PRIMARY KEY, company_id bigint NOT NULL, vehicle_id bigint NULL,
                device_id bigint NULL, driver_id bigint NULL, installation_id bigint NULL,
                assignment_id bigint NULL,trip_id bigint NULL,lat numeric NOT NULL,lng numeric NOT NULL,
                speed_mph numeric NOT NULL, heading smallint NULL, event_type text NOT NULL,
                engine_status text NULL, fuel_level numeric NULL, odometer_miles numeric NULL,
                source text NULL, source_channel text NULL, idempotency_key text NULL,
                observed_at timestamptz NULL, normalized_at timestamptz NULL,
                event_time timestamptz NOT NULL, received_at timestamptz NOT NULL,
                UNIQUE(company_id,idempotency_key));
            CREATE TABLE latest_vehicle_positions(
                id bigserial PRIMARY KEY, company_id bigint NOT NULL, vehicle_id bigint NOT NULL,
                device_id bigint NULL, driver_id bigint NULL,installation_id bigint NULL,
                assignment_id bigint NULL,trip_id bigint NULL,lat numeric NOT NULL,lng numeric NOT NULL,
                speed_mph numeric NOT NULL, heading smallint NOT NULL, engine_status text NULL,
                fuel_level numeric NULL, odometer_miles numeric NULL, source text NULL,
                provider text NULL, protocol text NULL, adapter_version text NULL,
                confidence numeric NULL, trust_score numeric NULL, quality_flags jsonb NULL,
                device_fix_time timestamptz NULL, gateway_received_at timestamptz NULL,
                normalized_at timestamptz NULL, event_time timestamptz NOT NULL,
                received_at timestamptz NOT NULL, event_count bigint NOT NULL,
                source_event_id bigint NULL, source_channel text NULL,
                telemetry_status text NULL, risk_level text NULL, updated_at timestamptz NULL,
                UNIQUE(company_id,vehicle_id));
            CREATE TABLE telemetry_rules(
                id bigserial PRIMARY KEY, company_id bigint NOT NULL, rule_type text NOT NULL,
                threshold_value numeric NOT NULL, severity text NOT NULL, enabled boolean NOT NULL);
            CREATE TABLE geofences(
                id bigserial PRIMARY KEY, company_id bigint NOT NULL, branch_id bigint NULL,
                name text NOT NULL, status text NOT NULL, center_lat numeric NULL,
                center_lng numeric NULL, radius_meters int NULL, polygon_json jsonb NULL);
            CREATE TABLE telemetry_alerts(
                id bigserial PRIMARY KEY, company_id bigint NOT NULL, vehicle_id bigint NULL,
                device_id bigint NULL, driver_id bigint NULL,installation_id bigint NULL,
                assignment_id bigint NULL,trip_id bigint NULL,alert_type text NOT NULL,
                severity text NOT NULL, message text NOT NULL, source_event_id bigint NULL,
                status text NOT NULL, source_channel text NULL, created_at timestamptz NOT NULL);
            INSERT INTO vehicles VALUES(501,11,71,NULL);
            INSERT INTO eld_devices(id,company_id,vehicle_id,driver_id) VALUES(101,11,501,301);
            INSERT INTO device_installations VALUES(1001,11,101,501,'Installed',now()-interval '1 day',NULL);
            INSERT INTO dispatch_assignments VALUES(2001,11,501,311,3001,now()-interval '1 day',NULL,NULL,NULL);
            INSERT INTO telemetry_rules(company_id,rule_type,threshold_value,severity,enabled)
              VALUES(11,'speeding',50,'High',TRUE);
            INSERT INTO geofences(company_id,name,status,center_lat,center_lng,radius_meters)
              VALUES(11,'Depot','Active',34.05,-118.24,100);
            """);

        DateTime fixTime = DateTime.UtcNow.AddSeconds(-30);
        var evt = new CanonicalTelemetryEvent
        {
            SchemaVersion = 1,
            EventId = Guid.NewGuid(),
            CorrelationId = Guid.NewGuid(),
            OccurredAtDeviceUtc = fixTime,
            ReceivedAtGatewayUtc = fixTime.AddSeconds(1),
            NormalizedAtUtc = fixTime.AddSeconds(2),
            TenantId = Guid.NewGuid(),
            CompanyId = 11,
            DeviceId = "101",
            VehicleId = 501,
            Source = TelemetrySource.DirectDevice,
            Transport = Transport.Tcp,
            ProtocolName = "GT06",
            AdapterName = "GT06",
            AdapterVersion = "1.0.0",
            Location = new GeoPoint(35.05, -119.24, SpeedKph: 130, HeadingDeg: 90),
            EngineOn = true,
            FuelPercent = 62,
            OdometerKm = 12_345,
        };
        var projector = new PostgresPositionProjectionStore(database.ScopedConnectionString);

        ProjectionResult applied = await projector.ApplyAsync(evt);
        Assert.Equal(ProjectionOutcome.Applied, applied.Outcome);
        Assert.Equal(1001, applied.Event.InstallationId);
        Assert.Equal(2001, applied.Event.AssignmentId);
        Assert.Equal(3001, applied.Event.TripId);
        Assert.Equal(311, applied.Event.DriverId);
        ProjectionResult duplicate = await projector.ApplyAsync(evt);
        Assert.Equal(ProjectionOutcome.DuplicateIgnored, duplicate.Outcome);
        Assert.Equal(2001, duplicate.Event.AssignmentId);
        Assert.Equal(3001, duplicate.Event.TripId);
        Assert.Equal(311, duplicate.Event.DriverId);

        Assert.Equal(1, await database.ScalarLongAsync("SELECT count(*) FROM location_events"));
        Assert.Equal(1, await database.ScalarLongAsync(
            "SELECT count(*) FROM location_events WHERE installation_id=1001 AND assignment_id=2001 AND trip_id=3001 AND driver_id=311"));
        Assert.Equal(1, await database.ScalarLongAsync("SELECT event_count FROM latest_vehicle_positions"));
        Assert.Equal(1, await database.ScalarLongAsync(
            "SELECT count(*) FROM latest_vehicle_positions WHERE installation_id=1001 AND assignment_id=2001 AND trip_id=3001 AND driver_id=311"));
        Assert.Equal(2, await database.ScalarLongAsync("SELECT count(*) FROM telemetry_alerts"));
        Assert.Equal(2, await database.ScalarLongAsync(
            "SELECT count(*) FROM telemetry_alerts a JOIN location_events e ON e.id=a.source_event_id AND e.company_id=a.company_id"));
        Assert.Equal(1, await database.ScalarLongAsync(
            "SELECT count(*) FROM eld_devices WHERE last_seen_at IS NOT NULL AND last_heartbeat_at IS NOT NULL"));

        // A novel historical fix remains breadcrumb evidence but loses the monotonic latest race.
        // Closing the existing alerts proves the old fix cannot open replacements.
        await database.ExecuteAsync("UPDATE telemetry_alerts SET status='Closed'");
        var outOfOrder = evt with
        {
            EventId = Guid.NewGuid(),
            CorrelationId = Guid.NewGuid(),
            OccurredAtDeviceUtc = fixTime.AddMinutes(-10),
            ReceivedAtGatewayUtc = fixTime.AddMinutes(2),
            NormalizedAtUtc = fixTime.AddMinutes(2).AddSeconds(1),
        };
        Assert.Equal(ProjectionOutcome.StaleIgnored, (await projector.ApplyAsync(outOfOrder)).Outcome);
        Assert.Equal(2, await database.ScalarLongAsync("SELECT count(*) FROM location_events"));
        Assert.Equal(1, await database.ScalarLongAsync("SELECT event_count FROM latest_vehicle_positions"));
        Assert.Equal(0, await database.ScalarLongAsync("SELECT count(*) FROM telemetry_alerts WHERE status='Open'"));

        // A vehicle inside one of several active yards is authorized. Being outside the first
        // yard must not create a false geofence breach when it is inside another active yard.
        await database.ExecuteAsync("""
            INSERT INTO vehicles VALUES(502,11,71,NULL);
            INSERT INTO eld_devices(id,company_id,vehicle_id,driver_id) VALUES(102,11,502,302);
            INSERT INTO device_installations VALUES(1002,11,102,502,'Verified',now()-interval '1 day',NULL);
            INSERT INTO dispatch_assignments VALUES(2002,11,502,312,3002,now()-interval '1 day',NULL,NULL,NULL);
            INSERT INTO geofences(company_id,name,status,center_lat,center_lng,radius_meters)
              VALUES(11,'Alternate Yard','Active',35.05,-119.24,500);
            """);
        var insideAlternateYard = evt with
        {
            EventId = Guid.NewGuid(),
            CorrelationId = Guid.NewGuid(),
            DeviceId = "102",
            VehicleId = 502,
            Location = new GeoPoint(35.05, -119.24, SpeedKph: 20, HeadingDeg: 90),
        };
        Assert.Equal(ProjectionOutcome.Applied, (await projector.ApplyAsync(insideAlternateYard)).Outcome);
        Assert.Equal(0, await database.ScalarLongAsync(
            "SELECT count(*) FROM telemetry_alerts WHERE vehicle_id=502 AND alert_type='geofence_breach'"));

        var heartbeat = evt with
        {
            EventId = Guid.NewGuid(),
            CorrelationId = Guid.NewGuid(),
            OccurredAtDeviceUtc = fixTime.AddMinutes(1),
            ReceivedAtGatewayUtc = fixTime.AddMinutes(1).AddSeconds(1),
            NormalizedAtUtc = fixTime.AddMinutes(1).AddSeconds(2),
            Location = null,
        };
        Assert.Equal(ProjectionOutcome.NoLocation, (await projector.ApplyAsync(heartbeat)).Outcome);
        Assert.Equal(3, await database.ScalarLongAsync("SELECT count(*) FROM location_events"));
        Assert.Equal(4, await database.ScalarLongAsync("SELECT count(*) FROM telemetry_projection_inbox"));

        // A reconnected device may deliver historical evidence from its prior installation. It is
        // attributed by device time, but cannot update the live projection for that ended binding.
        await database.ExecuteAsync("""
            INSERT INTO vehicles VALUES(500,11,71,NULL);
            INSERT INTO device_installations VALUES
              (1000,11,101,500,'Removed',now()-interval '4 days',now()-interval '1 day');
            INSERT INTO dispatch_assignments VALUES
              (2000,11,500,310,3000,now()-interval '4 days',NULL,now()-interval '1 day',NULL);
            """);
        var historical = evt with
        {
            EventId = Guid.NewGuid(),
            CorrelationId = Guid.NewGuid(),
            OccurredAtDeviceUtc = DateTime.UtcNow.AddDays(-2),
            ReceivedAtGatewayUtc = DateTime.UtcNow,
            NormalizedAtUtc = DateTime.UtcNow,
        };
        ProjectionResult historicalProjection = await projector.ApplyAsync(historical);
        Assert.Equal(ProjectionOutcome.StaleIgnored, historicalProjection.Outcome);
        Assert.Equal(1000, historicalProjection.Event.InstallationId);
        Assert.Equal(500, historicalProjection.Event.VehicleId);
        Assert.Equal(2000, historicalProjection.Event.AssignmentId);
        Assert.Equal(3000, historicalProjection.Event.TripId);
        Assert.Equal(310, historicalProjection.Event.DriverId);
        Assert.Equal(1, await database.ScalarLongAsync(
            "SELECT count(*) FROM location_events WHERE installation_id=1000 AND vehicle_id=500 AND assignment_id=2000 AND trip_id=3000 AND driver_id=310"));

        // Authorization is checked before the inbox/replay no-op. A cached session therefore
        // cannot keep writing after revocation or after its current binding changes.
        await database.ExecuteAsync("UPDATE eld_devices SET status='Revoked' WHERE id=101");
        var afterRevocation = evt with { EventId = Guid.NewGuid(), CorrelationId = Guid.NewGuid() };
        await Assert.ThrowsAsync<InvalidOperationException>(() => projector.ApplyAsync(afterRevocation));
        await database.ExecuteAsync("UPDATE eld_devices SET status='Active',device_state='Quarantined' WHERE id=101");
        var afterQuarantine = evt with { EventId = Guid.NewGuid(), CorrelationId = Guid.NewGuid() };
        await Assert.ThrowsAsync<InvalidOperationException>(() => projector.ApplyAsync(afterQuarantine));
        await database.ExecuteAsync("""
            UPDATE eld_devices SET status='Active',device_state='Online',vehicle_id=502 WHERE id=101;
            UPDATE device_installations SET effective_to=now() WHERE id=1001;
            INSERT INTO device_installations VALUES(1003,11,101,502,'Installed',now(),NULL);
            """);
        var staleSession = evt with { EventId = Guid.NewGuid(), CorrelationId = Guid.NewGuid() };
        await Assert.ThrowsAsync<InvalidOperationException>(() => projector.ApplyAsync(staleSession));
    }

    [Fact]
    public async Task Registry_ResolvesOnlyExactOwner_AndRejectsAmbiguousCrossTenantIdentity()
    {
        await using var database = await IsolatedSchema.CreateAsync();
        await database.ExecuteAsync("""
            CREATE TABLE eld_devices(
                id bigint PRIMARY KEY, company_id bigint NOT NULL, vehicle_id bigint NULL,
                status text NOT NULL, device_state text NULL, device_serial text NOT NULL,
                imei text NULL, deleted_at timestamptz NULL);
            CREATE TABLE telematics_device_trust_policy(
                device_id text PRIMARY KEY, auth_mode text NOT NULL, credential_kind text NOT NULL,
                credential_handle text NULL, pinned_source_cidrs text[] NULL,
                pinned_sim_iccid text NULL, pinned_imsi text NULL,
                require_replay_defense boolean NOT NULL);
            CREATE TABLE device_installations(
                id bigint PRIMARY KEY,company_id bigint NOT NULL,device_id bigint NOT NULL,
                vehicle_id bigint NULL,status text NOT NULL,effective_from timestamptz NOT NULL,
                effective_to timestamptz NULL);
            CREATE TABLE device_installation_quarantine(
                id bigint PRIMARY KEY,company_id bigint NOT NULL,device_id bigint NULL,
                resolved_at timestamptz NULL);
            INSERT INTO eld_devices VALUES
                (101,11,501,'Active','Online','serial-a','111111111111111',NULL),
                (202,22,502,'Active','Online','serial-b','222222222222222',NULL);
            INSERT INTO telematics_device_trust_policy VALUES
                ('101','ImeiAllowlistOnly','None',NULL,NULL,NULL,NULL,true),
                ('202','ImeiAllowlistOnly','None',NULL,NULL,NULL,NULL,true);
            INSERT INTO device_installations VALUES
                (1001,11,101,501,'Installed',now()-interval '1 day',NULL),
                (2002,22,202,502,'Verified',now()-interval '1 day',NULL);
            """);

        var restartedRegistry = new PostgresDeviceRegistry(database.ScopedConnectionString);
        var companyA = await restartedRegistry.ResolveTrustAsync(new DeviceIdentityRef(Imei: "111111111111111"));
        var companyB = await restartedRegistry.ResolveTrustAsync(new DeviceIdentityRef(Imei: "222222222222222"));

        Assert.Equal(11, companyA?.Owner.CompanyId);
        Assert.Equal(501, companyA?.Owner.VehicleId);
        Assert.Equal(22, companyB?.Owner.CompanyId);
        Assert.NotEqual(companyA?.Owner.TenantId, companyB?.Owner.TenantId);

        await database.ExecuteAsync(
            "INSERT INTO device_installations VALUES(1002,11,101,501,'Verified',now()-interval '1 hour',NULL)");
        Assert.Null(await restartedRegistry.ResolveTrustAsync(
            new DeviceIdentityRef(Imei: "111111111111111")));
        await database.ExecuteAsync("DELETE FROM device_installations WHERE id=1002");

        await database.ExecuteAsync(
            "UPDATE eld_devices SET imei='111111111111111' WHERE id=202");
        Assert.Null(await restartedRegistry.ResolveTrustAsync(
            new DeviceIdentityRef(Imei: "111111111111111")));
    }

    [Fact]
    public async Task StoreForward_SurvivesRestart_ReleasesFailureLease_AndEncryptsPayload()
    {
        await using var database = await IsolatedSchema.CreateAsync();
        await database.ExecuteAsync("""
            CREATE TABLE telemetry_store_forward(
                id bigserial PRIMARY KEY, event_id uuid UNIQUE NOT NULL, topic text NOT NULL,
                partition_key text NOT NULL, envelope_json jsonb NOT NULL,
                enqueued_at timestamptz NOT NULL, claimed_at timestamptz NULL,
                claim_token uuid NULL, attempts int NOT NULL DEFAULT 0, last_error text NULL);
            """);

        byte[] key = RandomNumberGenerator.GetBytes(32);
        Guid eventId = Guid.NewGuid();
        var firstProcess = new PostgresStoreAndForwardBuffer(database.ScopedConnectionString, key);
        await firstProcess.EnqueueAsync(Entry(eventId, "sensitive-device-101", 11));

        string persisted = await database.ScalarStringAsync(
            "SELECT envelope_json::text FROM telemetry_store_forward LIMIT 1");
        Assert.DoesNotContain("sensitive-device-101", persisted, StringComparison.Ordinal);
        Assert.Contains("aes-256-gcm-v1", persisted, StringComparison.Ordinal);

        var restartedProcess = new PostgresStoreAndForwardBuffer(database.ScopedConnectionString, key);
        StoreAndForwardLease lease = Assert.IsType<StoreAndForwardLease>(
            await restartedProcess.TryAcquireAsync());
        var envelope = Assert.IsType<EventEnvelope<CanonicalTelemetryEvent>>(lease.Entry.Envelope);
        Assert.Equal(eventId, envelope.EventId);
        Assert.Equal(11, envelope.CompanyId);
        Assert.Equal(1001, envelope.Payload.InstallationId);
        Assert.Equal(2001, envelope.Payload.AssignmentId);
        Assert.Equal(3001, envelope.Payload.TripId);
        Assert.Equal(311, envelope.Payload.DriverId);

        await restartedProcess.AbandonAsync(lease, "simulated downstream failure");
        var recoveredProcess = new PostgresStoreAndForwardBuffer(database.ScopedConnectionString, key);
        StoreAndForwardLease retry = Assert.IsType<StoreAndForwardLease>(
            await recoveredProcess.TryAcquireAsync());
        var retriedEnvelope = Assert.IsType<EventEnvelope<CanonicalTelemetryEvent>>(retry.Entry.Envelope);
        Assert.Equal(eventId, retriedEnvelope.EventId);
        Assert.Equal(1001, retriedEnvelope.Payload.InstallationId);
        Assert.Equal(2001, retriedEnvelope.Payload.AssignmentId);
        Assert.Equal(3001, retriedEnvelope.Payload.TripId);
        Assert.Equal(311, retriedEnvelope.Payload.DriverId);
        await recoveredProcess.CompleteAsync(retry);

        Assert.Equal(0, await database.ScalarLongAsync("SELECT count(*) FROM telemetry_store_forward"));
    }

    [Fact]
    public async Task RejectionLedger_IsIdempotent_AndPersistsOnlyMaskedMetadata()
    {
        await using var database = await IsolatedSchema.CreateAsync();
        await database.ExecuteAsync("""
            CREATE TABLE telemetry_gateway_rejections(
                id bigserial PRIMARY KEY, event_id uuid UNIQUE NOT NULL, correlation_id uuid NOT NULL,
                claimed_identifier_masked text NOT NULL, reason text NOT NULL, protocol text NOT NULL,
                message_type text NOT NULL, received_at timestamptz NOT NULL,
                raw_frame_bytes int NOT NULL, remote_endpoint text NOT NULL,
                created_at timestamptz NOT NULL DEFAULT now());
            """);

        Guid eventId = Guid.NewGuid();
        var rejection = new TelemetryRejection
        {
            Reason = RejectionReasons.UnknownDevice,
            ClaimedIdentifierMasked = "***********4321",
            ProtocolName = "GT06",
            MessageType = "Login",
            ReceivedAtGatewayUtc = DateTimeOffset.UtcNow,
            RawFrameBytes = 17,
            RemoteEndpoint = "203.0.113.0/24",
        };
        var envelope = new EventEnvelope<TelemetryRejection>
        {
            EventId = eventId,
            CorrelationId = Guid.NewGuid(),
            OccurredAt = rejection.ReceivedAtGatewayUtc,
            TenantId = Guid.Empty,
            CompanyId = 0,
            SchemaVersion = 1,
            Payload = rejection,
        };
        var backbone = new PostgresEventBackbone(database.ScopedConnectionString);

        await backbone.PublishAsync(TelematicsTopics.TelemetryRejected, "masked", envelope);
        await backbone.PublishAsync(TelematicsTopics.TelemetryRejected, "masked", envelope);

        Assert.Equal(1, await database.ScalarLongAsync("SELECT count(*) FROM telemetry_gateway_rejections"));
        Assert.Equal("203.0.113.0/24", await database.ScalarStringAsync(
            "SELECT remote_endpoint FROM telemetry_gateway_rejections"));
        Assert.Equal(17, await database.ScalarLongAsync(
            "SELECT raw_frame_bytes FROM telemetry_gateway_rejections"));
    }

    [Fact]
    public async Task CanonicalBackbone_RejectsCrossTenantEnvelopeBeforeDatabaseAccess()
    {
        Guid eventId = Guid.NewGuid();
        StoreAndForwardEntry entry = Entry(eventId, "device-101", 11);
        var valid = Assert.IsType<EventEnvelope<CanonicalTelemetryEvent>>(entry.Envelope);
        var forged = valid with { CompanyId = 22 };
        var backbone = new PostgresEventBackbone("Host=127.0.0.1;Port=1;Database=unreachable");

        var error = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            backbone.PublishAsync(TelematicsTopics.TelemetryNormalized, entry.Key, forged));
        Assert.Contains("ownership", error.Message, StringComparison.OrdinalIgnoreCase);

        var unownedPayload = valid.Payload with { CompanyId = 0, TenantId = Guid.Empty };
        var unownedEnvelope = valid with
        {
            CompanyId = 0,
            TenantId = Guid.Empty,
            Payload = unownedPayload,
        };
        var unownedError = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            backbone.PublishAsync(TelematicsTopics.TelemetryNormalized, entry.Key, unownedEnvelope));
        Assert.Contains("registry-resolved tenant ownership", unownedError.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task CanonicalBackbone_PersistsEventTimeLineageIdempotentlyAcrossRetry()
    {
        await using var database = await IsolatedSchema.CreateAsync();
        await database.ExecuteAsync("""
            CREATE TABLE canonical_telemetry_events(
                id bigserial PRIMARY KEY, company_id bigint NOT NULL, vehicle_id bigint NULL,
                device_id bigint NULL, installation_id bigint NULL, assignment_id bigint NULL,
                trip_id bigint NULL, driver_id bigint NULL, correlation_id uuid NOT NULL,
                event_type text NOT NULL, lat numeric NULL, lng numeric NULL,
                speed_mph numeric NULL, heading numeric NULL, source text NOT NULL,
                provider text NULL, protocol text NULL, adapter_version text NULL,
                confidence numeric NULL, trust_score numeric NULL, quality_flags jsonb NULL,
                payload jsonb NOT NULL, device_fix_time timestamptz NOT NULL,
                gateway_received_at timestamptz NOT NULL, event_time timestamptz NOT NULL);
            """);

        Guid eventId = Guid.NewGuid();
        StoreAndForwardEntry entry = Entry(eventId, "101", 11);
        var envelope = Assert.IsType<EventEnvelope<CanonicalTelemetryEvent>>(entry.Envelope);
        var backbone = new PostgresEventBackbone(database.ScopedConnectionString);
        string authoritativeKey = TelematicsEventKey.ForDevice(
            envelope.Payload.TenantId, envelope.Payload.CompanyId, envelope.Payload.DeviceId);

        await backbone.PublishAsync(TelematicsTopics.TelemetryNormalized, authoritativeKey, envelope);
        await backbone.PublishAsync(TelematicsTopics.TelemetryNormalized, authoritativeKey, envelope);

        Assert.Equal(1, await database.ScalarLongAsync("SELECT count(*) FROM canonical_telemetry_events"));
        Assert.Equal(1, await database.ScalarLongAsync("""
            SELECT count(*) FROM canonical_telemetry_events
             WHERE installation_id=1001 AND assignment_id=2001 AND trip_id=3001 AND driver_id=311
               AND payload->'Event'->>'InstallationId'='1001'
               AND payload->'Event'->>'AssignmentId'='2001'
               AND payload->'Event'->>'TripId'='3001'
               AND payload->'Event'->>'DriverId'='311'
            """));
    }

    private static StoreAndForwardEntry Entry(Guid eventId, string deviceId, long companyId)
    {
        Guid tenant = Guid.NewGuid();
        DateTime now = DateTime.UtcNow;
        var payload = new CanonicalTelemetryEvent
        {
            SchemaVersion = 1, EventId = eventId, CorrelationId = eventId,
            OccurredAtDeviceUtc = now, ReceivedAtGatewayUtc = now, NormalizedAtUtc = now,
            TenantId = tenant, CompanyId = companyId, DeviceId = deviceId,
            VehicleId = 501, InstallationId = 1001, AssignmentId = 2001,
            TripId = 3001, DriverId = 311,
            Source = TelemetrySource.DirectDevice, Transport = Transport.Tcp,
            ProtocolName = "GT06", AdapterName = "GT06", AdapterVersion = "1.0.0",
        };
        var envelope = new EventEnvelope<CanonicalTelemetryEvent>
        {
            EventId = eventId, CorrelationId = eventId, OccurredAt = now,
            TenantId = tenant, CompanyId = companyId, SchemaVersion = 1, Payload = payload,
        };
        return new StoreAndForwardEntry(
            TelematicsTopics.TelemetryNormalized, $"{companyId}:{deviceId}", envelope, DateTimeOffset.UtcNow);
    }

    private sealed class IsolatedSchema : IAsyncDisposable
    {
        private readonly string _adminConnectionString;
        private readonly string _schema;
        public string ScopedConnectionString { get; }

        private IsolatedSchema(string adminConnectionString, string schema)
        {
            _adminConnectionString = adminConnectionString;
            _schema = schema;
            var builder = new NpgsqlConnectionStringBuilder(adminConnectionString) { SearchPath = schema };
            ScopedConnectionString = builder.ConnectionString;
        }

        public static async Task<IsolatedSchema> CreateAsync()
        {
            string admin = Environment.GetEnvironmentVariable("OPSTRAX_TEST_DB")
                ?? throw new InvalidOperationException(
                    "OPSTRAX_TEST_DB is required for Postgres production-durability tests.");
            string schema = $"telematics_test_{Guid.NewGuid():N}";
            await using var connection = new NpgsqlConnection(admin);
            await connection.OpenAsync();
            await using var command = new NpgsqlCommand($"CREATE SCHEMA {schema}", connection);
            await command.ExecuteNonQueryAsync();
            return new IsolatedSchema(admin, schema);
        }

        public async Task ExecuteAsync(string sql)
        {
            await using var connection = new NpgsqlConnection(ScopedConnectionString);
            await connection.OpenAsync();
            await using var command = new NpgsqlCommand(sql, connection);
            await command.ExecuteNonQueryAsync();
        }

        public async Task<string> ScalarStringAsync(string sql)
        {
            await using var connection = new NpgsqlConnection(ScopedConnectionString);
            await connection.OpenAsync();
            await using var command = new NpgsqlCommand(sql, connection);
            return Convert.ToString(await command.ExecuteScalarAsync()) ?? string.Empty;
        }

        public async Task<long> ScalarLongAsync(string sql)
        {
            await using var connection = new NpgsqlConnection(ScopedConnectionString);
            await connection.OpenAsync();
            await using var command = new NpgsqlCommand(sql, connection);
            return Convert.ToInt64(await command.ExecuteScalarAsync());
        }

        public async ValueTask DisposeAsync()
        {
            NpgsqlConnection.ClearAllPools();
            await using var connection = new NpgsqlConnection(_adminConnectionString);
            await connection.OpenAsync();
            await using var command = new NpgsqlCommand($"DROP SCHEMA IF EXISTS {_schema} CASCADE", connection);
            await command.ExecuteNonQueryAsync();
        }
    }
}
