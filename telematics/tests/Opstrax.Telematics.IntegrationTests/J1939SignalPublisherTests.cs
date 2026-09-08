using Opstrax.Telematics.Contracts;
using Opstrax.Telematics.Contracts.Eventing;
using Opstrax.Telematics.Contracts.Identity;
using Opstrax.Telematics.Contracts.Lifecycle;
using Opstrax.Telematics.Contracts.Provenance;
using Opstrax.Telematics.Contracts.Signals;
using Opstrax.Telematics.Gateway.Eventing;
using Opstrax.Telematics.Gateway.Forwarding;
using Opstrax.Telematics.Gateway.Buffering;
using Microsoft.Extensions.Logging.Abstractions;
using Opstrax.Telematics.Protocols.J1939;

namespace Opstrax.Telematics.IntegrationTests;

public sealed class J1939SignalPublisherTests
{
    private static readonly DateTimeOffset CapturedAt = new(2026, 9, 8, 15, 0, 0, TimeSpan.Zero);
    private static readonly Guid TenantId = Guid.Parse("40000000-0000-0000-0000-000000000004");

    [Fact]
    public async Task Publishes_owned_signal_with_partition_and_capture_provenance()
    {
        var backbone = new InMemoryEventBackbone();
        await using var subscription = backbone.Subscribe<CanonicalTelemetryEvent>(
            TelematicsTopics.TelemetryNormalized,
            TenantId);
        var publisher = new J1939SignalPublisher(backbone);
        var message = Message(
            J1939SignalDecoder.EngineHoursRevolutionsPgn,
            [0x72, 0x60, 0x00, 0x00, 0xFF, 0xFF, 0xFF, 0xFF]);
        var context = Context();

        var published = await publisher.PublishAsync(message, context);
        var delivered = await ReadOneAsync(subscription);

        Assert.Same(published, delivered.Envelope.Payload);
        Assert.Equal(TelematicsTopics.TelemetryNormalized, delivered.Topic);
        Assert.Equal(
            TelematicsEventKey.ForDevice(TenantId, 42, "can-gateway-17"),
            delivered.Key);
        Assert.Equal(context.EventId, delivered.Envelope.EventId);
        Assert.Equal(context.CorrelationId, delivered.Envelope.CorrelationId);
        Assert.Equal(TenantId, delivered.Envelope.TenantId);
        Assert.Equal("65253", delivered.Envelope.Headers["j1939.pgn"]);
        Assert.Equal("247", delivered.Envelope.Headers["j1939.spns"]);
        Assert.Equal("0x00", delivered.Envelope.Headers["j1939.source_address"]);
        Assert.Equal("0xFF", delivered.Envelope.Headers["j1939.destination_address"]);
        Assert.Equal("socketcan", delivered.Envelope.Headers["j1939.adapter_type"]);
        Assert.Equal("can0", delivered.Envelope.Headers["j1939.channel"]);
        Assert.Equal("[\"capture-j1939-001\"]", delivered.Envelope.Headers["j1939.capture_references"]);
        Assert.Equal(SignalAvailability.Available, Assert.Single(published.Signals).Value.Availability);
    }

    [Fact]
    public async Task Tenant_filtered_subscriber_cannot_receive_another_tenants_can_signal()
    {
        var backbone = new InMemoryEventBackbone();
        await using var subscription = backbone.Subscribe<CanonicalTelemetryEvent>(
            TelematicsTopics.TelemetryNormalized,
            Guid.Parse("50000000-0000-0000-0000-000000000005"));
        var publisher = new J1939SignalPublisher(backbone);

        await publisher.PublishAsync(
            Message(
                J1939SignalDecoder.ElectronicEngineController1Pgn,
                [0xFF, 0xFF, 0xFF, 0xE0, 0x2E, 0xFF, 0xFF, 0xFF]),
            Context());

        Assert.Null(await ReadOptionalAsync(subscription, TimeSpan.FromMilliseconds(100)));
    }

    [Fact]
    public async Task Mixed_adapter_evidence_is_rejected_before_publication()
    {
        var backbone = new InMemoryEventBackbone();
        await using var subscription = backbone.Subscribe<CanonicalTelemetryEvent>(
            TelematicsTopics.TelemetryNormalized,
            TenantId);
        var publisher = new J1939SignalPublisher(backbone);
        var original = Message(
            J1939SignalDecoder.ElectronicEngineController1Pgn,
            [0xFF, 0xFF, 0xFF, 0xE0, 0x2E, 0xFF, 0xFF, 0xFF]);
        var secondFrame = original.Frames[0] with
        {
            AdapterType = "different-adapter",
            CaptureReference = "capture-j1939-002",
        };
        var malformed = original with { Frames = Array.AsReadOnly([original.Frames[0], secondFrame]) };

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            publisher.PublishAsync(malformed, Context()));
        Assert.Null(await ReadOptionalAsync(subscription, TimeSpan.FromMilliseconds(100)));
    }

    [Fact]
    public void Canonical_j1939_signal_is_persisted_as_a_vehicle_signal_event()
    {
        var canonical = J1939CanonicalEventFactory.Create(
            Message(
                J1939SignalDecoder.ElectronicEngineController1Pgn,
                [0xFF, 0xFF, 0xFF, 0xE0, 0x2E, 0xFF, 0xFF, 0xFF]),
            Context());

        Assert.Equal("vehicle.signal", PostgresEventBackbone.ClassifyCanonicalEventType(canonical));
    }

    [Fact]
    public async Task Backbone_outage_parks_exact_canonical_envelope_for_durable_replay()
    {
        var buffer = new InMemoryStoreAndForwardBuffer();
        var publisher = new J1939SignalPublisher(new CanonicalTelemetryPublisher(
            new FailingBackbone(),
            buffer,
            NullLogger<CanonicalTelemetryPublisher>.Instance));
        J1939CanonicalizationContext context = Context();

        CanonicalTelemetryEvent published = await publisher.PublishAsync(
            Message(
                J1939SignalDecoder.ElectronicEngineController1Pgn,
                [0xFF, 0xFF, 0xFF, 0xE0, 0x2E, 0xFF, 0xFF, 0xFF]),
            context);
        StoreAndForwardLease lease = Assert.IsType<StoreAndForwardLease>(await buffer.TryAcquireAsync());
        var envelope = Assert.IsType<EventEnvelope<CanonicalTelemetryEvent>>(lease.Entry.Envelope);

        Assert.Equal(TelematicsTopics.TelemetryNormalized, lease.Entry.Topic);
        Assert.Equal(TelematicsEventKey.ForDevice(TenantId, 42, "can-gateway-17"), lease.Entry.Key);
        Assert.Equal(published.EventId, envelope.EventId);
        Assert.Same(published, envelope.Payload);
    }

    private static J1939CanonicalizationContext Context() => new(
        new ResolvedDeviceOwner(
            TenantId,
            CompanyId: 42,
            DeviceId: "can-gateway-17",
            VehicleId: 701,
            LifecycleState: DeviceLifecycleState.Online,
            CredentialHandle: "opaque-handle"),
        EventId: Guid.Parse("60000000-0000-0000-0000-000000000006"),
        CorrelationId: Guid.Parse("70000000-0000-0000-0000-000000000007"),
        Source: TelemetrySource.Simulator,
        NormalizedAtUtc: CapturedAt.UtcDateTime.AddSeconds(1),
        FreshnessBudget: TimeSpan.FromMinutes(15),
        TrustScore: 0.5d,
        Confidence: 0.75d);

    private static J1939AcquiredMessage Message(int pgn, byte[] payload)
    {
        var frame = new J1939CanFrameEnvelope(
            RawIdentifier: 0x0CF00400,
            Priority: 3,
            Pgn: pgn,
            SourceAddress: 0x00,
            DestinationAddress: J1939CanIdentifier.GlobalAddress,
            IsPeerToPeer: false,
            Data: payload,
            CapturedAt,
            AdapterType: "socketcan",
            Channel: "can0",
            CaptureReference: "capture-j1939-001");
        return new J1939AcquiredMessage(
            pgn,
            SourceAddress: 0x00,
            DestinationAddress: J1939CanIdentifier.GlobalAddress,
            Payload: payload,
            IsTransported: false,
            MessagePriority: 3,
            FirstFrameAt: CapturedAt,
            CompletedAt: CapturedAt,
            Frames: Array.AsReadOnly([frame]));
    }

    private static async Task<DeliveredEvent<CanonicalTelemetryEvent>> ReadOneAsync(
        IEventSubscription<CanonicalTelemetryEvent> subscription)
    {
        var result = await ReadOptionalAsync(subscription, TimeSpan.FromSeconds(2));
        return result ?? throw new Xunit.Sdk.XunitException("Expected one J1939 event.");
    }

    private static async Task<DeliveredEvent<CanonicalTelemetryEvent>?> ReadOptionalAsync(
        IEventSubscription<CanonicalTelemetryEvent> subscription,
        TimeSpan timeout,
        CancellationToken cancellationToken = default)
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        cts.CancelAfter(timeout);
        try
        {
            await foreach (var delivered in subscription.ReadAllAsync(cts.Token))
                return delivered;
        }
        catch (OperationCanceledException) when (cts.IsCancellationRequested)
        {
        }

        return null;
    }

    private sealed class FailingBackbone : IEventBackbone
    {
        public Task PublishAsync<T>(
            string topic,
            string key,
            EventEnvelope<T> envelope,
            CancellationToken cancellationToken = default) =>
            Task.FromException(new IOException("synthetic backbone outage"));

        public IEventSubscription<T> Subscribe<T>(string topic, Guid? tenantFilter = null) =>
            throw new NotSupportedException();
    }
}
