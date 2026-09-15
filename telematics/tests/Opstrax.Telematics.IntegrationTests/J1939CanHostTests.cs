using System.Globalization;
using System.Runtime.CompilerServices;
using Microsoft.Extensions.Logging.Abstractions;
using Opstrax.Telematics.Contracts;
using Opstrax.Telematics.Contracts.Diagnostics;
using Opstrax.Telematics.Contracts.Eventing;
using Opstrax.Telematics.Contracts.Identity;
using Opstrax.Telematics.Contracts.Lifecycle;
using Opstrax.Telematics.Contracts.Provenance;
using Opstrax.Telematics.Gateway.Forwarding;
using Opstrax.Telematics.Gateway.Eventing;
using Opstrax.Telematics.Gateway.Identity;
using Opstrax.Telematics.Gateway.J1939;
using Opstrax.Telematics.Protocols.J1939;

namespace Opstrax.Telematics.IntegrationTests;

public sealed class J1939CanHostTests
{
    private static readonly DateTimeOffset CapturedAt =
        new(2026, 9, 8, 15, 0, 0, TimeSpan.Zero);
    private static readonly Guid TenantId =
        Guid.Parse("81000000-0000-0000-0000-000000000008");

    [Fact]
    public void Enabled_host_configuration_is_valid_only_with_bounded_explicit_identity()
    {
        J1939CanHostOptions options = Options();

        Assert.Null(J1939CanHostOptions.Validate(options));

        options.Interface = "can0;other-command";
        Assert.Contains("Interface", J1939CanHostOptions.Validate(options), StringComparison.Ordinal);
    }

    [Fact]
    public void Candump_parser_accepts_timestamped_extended_classic_can_without_retaining_raw_bytes()
    {
        J1939CanHostOptions options = Options();
        string line = Line("0CF004AB", "FFFFFFE02EFFFFFF");

        J1939RawCanFrame frame = CandumpJ1939CanFrameSource.ParseLine(line, options);

        Assert.Equal(0x0CF004ABu, frame.Identifier);
        Assert.True(frame.IsExtendedIdentifier);
        Assert.Equal([0xFF, 0xFF, 0xFF, 0xE0, 0x2E, 0xFF, 0xFF, 0xFF], frame.Data.ToArray());
        Assert.Equal(CapturedAt, frame.CapturedAt);
        Assert.Equal("exact-adapter-model", frame.AdapterType);
        Assert.Equal("can0", frame.Channel);
        Assert.StartsWith("candump:sha256:", frame.CaptureReference, StringComparison.Ordinal);
        Assert.DoesNotContain("FFFFFFE02EFFFFFF", frame.CaptureReference, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("(1788879600.000000) can0 123#0102")]
    [InlineData("(1788879600.000000) can1 0CF00400#0102")]
    [InlineData("(1788879600.000000) can0 0CF00400##0102")]
    [InlineData("(1788879600.000000) can0 0CF00400#R")]
    [InlineData("(1788879600.000000) can0 2FFFFFFF#0102")]
    [InlineData("can0 0CF00400#0102")]
    public void Candump_parser_rejects_nonclassic_wrong_interface_or_unbounded_identity_records(string line)
    {
        J1939CanInputException exception = Assert.Throws<J1939CanInputException>(
            () => CandumpJ1939CanFrameSource.ParseLine(line, Options()));

        Assert.DoesNotContain("0102", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Bounded_reader_discards_oversized_record_and_recovers_at_the_next_line()
    {
        string valid = Line("0CF00400", "FFFFFFE02EFFFFFF");
        using var reader = new StringReader(new string('A', 257) + "\n" + valid + "\n");
        var records = new List<BoundedCanRecord>();

        await foreach (BoundedCanRecord record in
                       CandumpJ1939CanFrameSource.ReadRecordsAsync(reader, 256))
            records.Add(record);

        Assert.Equal(2, records.Count);
        Assert.True(records[0].IsOversized);
        Assert.Null(records[0].Text);
        Assert.False(records[1].IsOversized);
        Assert.Equal(valid, records[1].Text);
    }

    [Fact]
    public async Task Configured_registry_serial_owns_signal_and_can_source_address_cannot_choose_owner()
    {
        var backbone = new InMemoryEventBackbone();
        await using IEventSubscription<CanonicalTelemetryEvent> subscription =
            backbone.Subscribe<CanonicalTelemetryEvent>(TelematicsTopics.TelemetryNormalized, TenantId);
        J1939CanIngestService service = Service(backbone, Registry(DeviceLifecycleState.Online));
        J1939RawCanFrame frame = CandumpJ1939CanFrameSource.ParseLine(
            Line("0CF004AB", "FFFFFFE02EFFFFFF"),
            Options());

        CanonicalTelemetryEvent? published = await service.ProcessFrameAsync(
            new J1939MessageAcquisition(),
            frame);
        DeliveredEvent<CanonicalTelemetryEvent> delivered = await ReadOneAsync(subscription);

        Assert.NotNull(published);
        Assert.Same(published, delivered.Envelope.Payload);
        Assert.Equal(TenantId, published.TenantId);
        Assert.Equal(8001, published.CompanyId);
        Assert.Equal("registry-device-88", published.DeviceId);
        Assert.Equal(9901, published.VehicleId);
        Assert.Equal(TelemetrySource.DirectDevice, published.Source);
        Assert.Equal("0xAB", delivered.Envelope.Headers["j1939.source_address"]);
        Assert.DoesNotContain("AB", published.DeviceId, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Replaying_the_same_capture_produces_the_same_event_identity()
    {
        var backbone = new InMemoryEventBackbone();
        J1939CanIngestService service = Service(backbone, Registry(DeviceLifecycleState.Online));
        J1939RawCanFrame frame = CandumpJ1939CanFrameSource.ParseLine(
            Line("0CF00400", "FFFFFFE02EFFFFFF"),
            Options());

        CanonicalTelemetryEvent first = Assert.IsType<CanonicalTelemetryEvent>(
            await service.ProcessFrameAsync(new J1939MessageAcquisition(), frame));
        CanonicalTelemetryEvent replay = Assert.IsType<CanonicalTelemetryEvent>(
            await service.ProcessFrameAsync(new J1939MessageAcquisition(), frame));

        Assert.Equal(first.EventId, replay.EventId);
        Assert.Equal(first.CorrelationId, replay.CorrelationId);
        Assert.NotEqual(first.EventId, first.CorrelationId);
    }

    [Fact]
    public async Task Dm1_flows_from_can_host_to_tenant_owned_canonical_diagnostic_event()
    {
        var backbone = new InMemoryEventBackbone();
        await using IEventSubscription<CanonicalTelemetryEvent> subscription =
            backbone.Subscribe<CanonicalTelemetryEvent>(TelematicsTopics.TelemetryNormalized, TenantId);
        J1939CanIngestService service = Service(backbone, Registry(DeviceLifecycleState.Online));
        J1939RawCanFrame frame = CandumpJ1939CanFrameSource.ParseLine(
            Line("18FECA31", "400034120501"),
            Options());

        CanonicalTelemetryEvent canonical = Assert.IsType<CanonicalTelemetryEvent>(
            await service.ProcessFrameAsync(new J1939MessageAcquisition(), frame));
        DeliveredEvent<CanonicalTelemetryEvent> delivered = await ReadOneAsync(subscription);

        Assert.Equal("diagnostic.event", PostgresEventBackbone.ClassifyCanonicalEventType(canonical));
        Assert.NotNull(canonical.Diagnostic);
        Assert.True(canonical.Diagnostic.IsActive);
        Assert.Equal(["SPN-4660-FMI-5"], canonical.DtcCodes);
        Assert.Equal(DiagnosticLampState.On, canonical.Diagnostic.Lamps.Protect);
        Assert.Equal("DM1", delivered.Envelope.Headers["j1939.diagnostic_kind"]);
        Assert.Equal("0x31", delivered.Envelope.Headers["j1939.source_address"]);
        Assert.Equal(TenantId, delivered.Envelope.TenantId);
        Assert.DoesNotContain("400034120501", string.Join('|', delivered.Envelope.Headers.Values));
    }

    [Fact]
    public async Task Dm2_flows_as_historical_evidence_without_clearing_active_faults()
    {
        var backbone = new InMemoryEventBackbone();
        J1939CanIngestService service = Service(backbone, Registry(DeviceLifecycleState.Online));
        J1939RawCanFrame frame = CandumpJ1939CanFrameSource.ParseLine(
            Line("18FECB31", "FFFFFFFFFFFF"),
            Options());

        CanonicalTelemetryEvent canonical = Assert.IsType<CanonicalTelemetryEvent>(
            await service.ProcessFrameAsync(new J1939MessageAcquisition(), frame));

        Assert.NotNull(canonical.Diagnostic);
        Assert.False(canonical.Diagnostic.IsActive);
        Assert.Empty(canonical.DtcCodes);
    }

    [Theory]
    [InlineData(DeviceLifecycleState.Draft)]
    [InlineData(DeviceLifecycleState.AwaitingAssignment)]
    [InlineData(DeviceLifecycleState.AwaitingConfiguration)]
    [InlineData(DeviceLifecycleState.Quarantined)]
    [InlineData(DeviceLifecycleState.Suspended)]
    [InlineData(DeviceLifecycleState.Retired)]
    public async Task Noningestible_registry_lifecycle_fails_closed(DeviceLifecycleState lifecycle)
    {
        var backbone = new InMemoryEventBackbone();
        await using IEventSubscription<CanonicalTelemetryEvent> subscription =
            backbone.Subscribe<CanonicalTelemetryEvent>(TelematicsTopics.TelemetryNormalized, TenantId);
        J1939CanIngestService service = Service(backbone, Registry(lifecycle));

        CanonicalTelemetryEvent? result = await service.ProcessFrameAsync(
            new J1939MessageAcquisition(),
            CandumpJ1939CanFrameSource.ParseLine(
                Line("0CF00400", "FFFFFFE02EFFFFFF"),
                Options()));

        Assert.Null(result);
        Assert.Null(await ReadOptionalAsync(subscription, TimeSpan.FromMilliseconds(100)));
    }

    [Fact]
    public async Task Unknown_configured_registry_serial_fails_closed()
    {
        var backbone = new InMemoryEventBackbone();
        var emptyRegistry = new InMemoryDeviceRegistry(
            Array.Empty<KeyValuePair<string, ResolvedDeviceOwner>>());
        J1939CanIngestService service = Service(backbone, emptyRegistry);

        CanonicalTelemetryEvent? result = await service.ProcessFrameAsync(
            new J1939MessageAcquisition(),
            CandumpJ1939CanFrameSource.ParseLine(
                Line("0CF00400", "FFFFFFE02EFFFFFF"),
                Options()));

        Assert.Null(result);
    }

    [Fact]
    public async Task Registry_lifecycle_hold_stops_publication_after_the_bounded_refresh_interval()
    {
        var backbone = new InMemoryEventBackbone();
        var owner = new ResolvedDeviceOwner(
            TenantId,
            CompanyId: 8001,
            DeviceId: "registry-device-88",
            VehicleId: 9901,
            LifecycleState: DeviceLifecycleState.Online,
            CredentialHandle: "physical-access-boundary");
        var registry = new MutableRegistry(owner);
        var time = new FixedTimeProvider(CapturedAt);
        J1939CanIngestService service = Service(backbone, registry, time);
        J1939RawCanFrame frame = CandumpJ1939CanFrameSource.ParseLine(
            Line("0CF00400", "FFFFFFE02EFFFFFF"),
            Options());

        Assert.NotNull(await service.ProcessFrameAsync(new J1939MessageAcquisition(), frame));
        registry.Owner = owner with { LifecycleState = DeviceLifecycleState.Suspended };
        time.UtcNow = CapturedAt.AddSeconds(6);

        Assert.Null(await service.ProcessFrameAsync(new J1939MessageAcquisition(), frame));
    }

    [Theory]
    [InlineData(-301)]
    [InlineData(6)]
    public async Task Capture_time_outside_admission_window_is_rejected(int secondsFromNow)
    {
        var backbone = new InMemoryEventBackbone();
        J1939CanIngestService service = Service(backbone, Registry(DeviceLifecycleState.Online));
        J1939RawCanFrame frame = CandumpJ1939CanFrameSource.ParseLine(
            Line("0CF00400", "FFFFFFE02EFFFFFF"),
            Options()) with
        {
            CapturedAt = CapturedAt.AddSeconds(secondsFromNow),
        };

        CanonicalTelemetryEvent? result = await service.ProcessFrameAsync(
            new J1939MessageAcquisition(),
            frame);

        Assert.Null(result);
    }

    [Fact]
    public async Task Malformed_supported_message_is_dropped_without_ending_the_acquisition_object()
    {
        var backbone = new InMemoryEventBackbone();
        J1939CanIngestService service = Service(backbone, Registry(DeviceLifecycleState.Online));
        var acquisition = new J1939MessageAcquisition();
        J1939RawCanFrame malformed = CandumpJ1939CanFrameSource.ParseLine(
            Line("0CF00400", "0102"),
            Options());
        J1939RawCanFrame valid = CandumpJ1939CanFrameSource.ParseLine(
            Line("0CF00400", "FFFFFFE02EFFFFFF"),
            Options());

        Assert.Null(await service.ProcessFrameAsync(acquisition, malformed));
        Assert.NotNull(await service.ProcessFrameAsync(acquisition, valid));
    }

    private static J1939CanIngestService Service(
        InMemoryEventBackbone backbone,
        IDeviceRegistry registry,
        TimeProvider? timeProvider = null)
    {
        J1939CanHostOptions options = Options();
        return new J1939CanIngestService(
            new EmptyFrameSource(),
            registry,
            new J1939SignalPublisher(new CanonicalTelemetryPublisher(backbone)),
            new J1939DiagnosticPublisher(new CanonicalTelemetryPublisher(backbone)),
            options,
            NullLogger<J1939CanIngestService>.Instance,
            timeProvider ?? new FixedTimeProvider(CapturedAt));
    }

    private static InMemoryDeviceRegistry Registry(DeviceLifecycleState lifecycle) => new(
        new[]
        {
            new KeyValuePair<string, ResolvedDeviceOwner>(
                "physical-registry-serial-88",
                new ResolvedDeviceOwner(
                    TenantId,
                    CompanyId: 8001,
                    DeviceId: "registry-device-88",
                    VehicleId: 9901,
                    LifecycleState: lifecycle,
                    CredentialHandle: "physical-access-boundary")),
        });

    private static J1939CanHostOptions Options() => new()
    {
        Enabled = true,
        CandumpPath = "/usr/bin/true",
        Interface = "can0",
        AdapterType = "exact-adapter-model",
        RegistryDeviceSerial = "physical-registry-serial-88",
        MaximumCaptureAge = TimeSpan.FromMinutes(5),
        MaximumFutureSkew = TimeSpan.FromSeconds(5),
        FreshnessBudget = TimeSpan.FromMinutes(1),
        RegistryRefreshInterval = TimeSpan.FromSeconds(5),
        RestartDelay = TimeSpan.FromMilliseconds(10),
        TransportSessionTimeout = TimeSpan.FromSeconds(30),
        MaximumLineLength = 256,
        TrustScore = 0.25,
        Confidence = 0.75,
    };

    private static string Line(string identifier, string payload) => string.Create(
        CultureInfo.InvariantCulture,
        $"({CapturedAt.ToUnixTimeSeconds()}.000000) can0 {identifier}#{payload}");

    private static async Task<DeliveredEvent<CanonicalTelemetryEvent>> ReadOneAsync(
        IEventSubscription<CanonicalTelemetryEvent> subscription)
    {
        DeliveredEvent<CanonicalTelemetryEvent>? result =
            await ReadOptionalAsync(subscription, TimeSpan.FromSeconds(2));
        return result ?? throw new Xunit.Sdk.XunitException("Expected one J1939 CAN host event.");
    }

    private static async Task<DeliveredEvent<CanonicalTelemetryEvent>?> ReadOptionalAsync(
        IEventSubscription<CanonicalTelemetryEvent> subscription,
        TimeSpan timeout)
    {
        using var cts = new CancellationTokenSource(timeout);
        try
        {
            await foreach (DeliveredEvent<CanonicalTelemetryEvent> delivered in
                           subscription.ReadAllAsync(cts.Token))
                return delivered;
        }
        catch (OperationCanceledException) when (cts.IsCancellationRequested)
        {
        }

        return null;
    }

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public DateTimeOffset UtcNow { get; set; } = now;
        public override DateTimeOffset GetUtcNow() => UtcNow;
    }

    private sealed class MutableRegistry(ResolvedDeviceOwner owner) : IDeviceRegistry
    {
        public ResolvedDeviceOwner Owner { get; set; } = owner;

        public ValueTask<ResolvedDeviceOwner?> ResolveAsync(
            DeviceIdentityRef identity,
            CancellationToken cancellationToken = default) =>
            ValueTask.FromResult<ResolvedDeviceOwner?>(Owner);

        public ValueTask<ResolvedDeviceTrust?> ResolveTrustAsync(
            DeviceIdentityRef identity,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }

    private sealed class EmptyFrameSource : IJ1939CanFrameSource
    {
        public async IAsyncEnumerable<J1939RawCanFrame> ReadSessionAsync(
            [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            await Task.CompletedTask;
            yield break;
        }
    }
}
