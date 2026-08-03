using System.Security.Cryptography;
using Npgsql;
using Opstrax.Telematics.Contracts;
using Opstrax.Telematics.Contracts.Eventing;
using Opstrax.Telematics.Contracts.Identity;
using Opstrax.Telematics.Contracts.Provenance;
using Opstrax.Telematics.Gateway.Buffering;
using Opstrax.Telematics.Gateway.Eventing;
using Opstrax.Telematics.Gateway.Identity;

namespace Opstrax.Telematics.IntegrationTests;

public sealed class PostgresProductionDurabilityTests
{
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
            INSERT INTO eld_devices VALUES
                (101,11,501,'Active','Online','serial-a','111111111111111',NULL),
                (202,22,502,'Active','Online','serial-b','222222222222222',NULL);
            INSERT INTO telematics_device_trust_policy VALUES
                ('101','ImeiAllowlistOnly','None',NULL,NULL,NULL,NULL,true),
                ('202','ImeiAllowlistOnly','None',NULL,NULL,NULL,NULL,true);
            """);

        var restartedRegistry = new PostgresDeviceRegistry(database.ScopedConnectionString);
        var companyA = await restartedRegistry.ResolveTrustAsync(new DeviceIdentityRef(Imei: "111111111111111"));
        var companyB = await restartedRegistry.ResolveTrustAsync(new DeviceIdentityRef(Imei: "222222222222222"));

        Assert.Equal(11, companyA?.Owner.CompanyId);
        Assert.Equal(501, companyA?.Owner.VehicleId);
        Assert.Equal(22, companyB?.Owner.CompanyId);
        Assert.NotEqual(companyA?.Owner.TenantId, companyB?.Owner.TenantId);

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

        await restartedProcess.AbandonAsync(lease, "simulated downstream failure");
        var recoveredProcess = new PostgresStoreAndForwardBuffer(database.ScopedConnectionString, key);
        StoreAndForwardLease retry = Assert.IsType<StoreAndForwardLease>(
            await recoveredProcess.TryAcquireAsync());
        Assert.Equal(eventId,
            Assert.IsType<EventEnvelope<CanonicalTelemetryEvent>>(retry.Entry.Envelope).EventId);
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
                ?? "Host=127.0.0.1;Port=59955;Database=opstrax_consultant;Username=zayra;Password=zayra";
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
