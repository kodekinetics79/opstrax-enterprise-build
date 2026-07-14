using Microsoft.Extensions.Logging.Abstractions;
using Opstrax.Telematics.Contracts;
using Opstrax.Telematics.Contracts.Eventing;
using Opstrax.Telematics.Contracts.Provenance;
using Opstrax.Telematics.Gateway.Buffering;

namespace Opstrax.Telematics.IntegrationTests;

/// <summary>
/// Unit tests for the store-and-forward replay path — the consumer of
/// <see cref="IStoreAndForwardBuffer.TryDequeue"/> the Increment-1 review flagged as missing.
/// The two guarantees under test are the ones an outage would otherwise break: the drain
/// republishes parked events in per-device order, and a transient downstream failure neither loses
/// an event nor lets a later fix overtake the stuck one.
/// </summary>
public class StoreAndForwardReplayServiceTests
{
    private static readonly Guid Tenant = Guid.Parse("22222222-2222-2222-2222-222222222222");

    private static readonly StoreAndForwardReplayOptions FastOptions = new()
    {
        InitialBackoff = TimeSpan.Zero,
        MaxBackoff = TimeSpan.Zero,
        BackoffMultiplier = 1.0,
        IdlePollInterval = TimeSpan.Zero,
    };

    // Zero-duration delay that still yields, so the drain is cooperatively async (never a
    // synchronous spin) while staying timing-free and deterministic.
    private static async Task NoDelay(TimeSpan _, CancellationToken __) => await Task.Yield();

    private static StoreAndForwardEntry Entry(string deviceKey, Guid eventId, long companyId = 1L)
    {
        var envelope = new EventEnvelope<CanonicalTelemetryEvent>
        {
            EventId = eventId,
            CorrelationId = eventId,
            OccurredAt = DateTimeOffset.UtcNow,
            TenantId = Tenant,
            CompanyId = companyId,
            SchemaVersion = 1,
            Payload = new CanonicalTelemetryEvent
            {
                SchemaVersion = 1,
                EventId = eventId,
                CorrelationId = eventId,
                OccurredAtDeviceUtc = DateTime.UtcNow,
                ReceivedAtGatewayUtc = DateTime.UtcNow,
                NormalizedAtUtc = DateTime.UtcNow,
                TenantId = Tenant,
                CompanyId = companyId,
                DeviceId = deviceKey,
                Source = TelemetrySource.DirectDevice,
                Transport = Transport.Tcp,
                ProtocolName = "GT06",
                AdapterName = "GT06",
                AdapterVersion = "1.0.0",
            },
        };

        string key = TelematicsEventKey.ForDevice(Tenant, companyId, deviceKey);
        return new StoreAndForwardEntry(TelematicsTopics.TelemetryNormalized, key, envelope, DateTimeOffset.UtcNow);
    }

    private static StoreAndForwardReplayService NewService(
        IStoreAndForwardBuffer buffer, IEventBackbone backbone) =>
        new(buffer, backbone, FastOptions, NullLogger<StoreAndForwardReplayService>.Instance, NoDelay);

    [Fact]
    public async Task Drain_republishes_every_parked_event_exactly_once()
    {
        var buffer = new InMemoryStoreAndForwardBuffer();
        var backbone = new RecordingBackbone();

        Guid[] ids = Enumerable.Range(0, 5).Select(_ => Guid.NewGuid()).ToArray();
        foreach (Guid id in ids)
            await buffer.EnqueueAsync(Entry("device-A", id));

        var service = NewService(buffer, backbone);
        await service.DrainAsync(CancellationToken.None);

        Assert.Equal(ids, backbone.PublishedEventIds);
        Assert.Equal(0, buffer.Count);
        Assert.Equal(5, service.Replayed);
    }

    [Fact]
    public async Task Drain_preserves_per_device_order_across_interleaved_devices()
    {
        var buffer = new InMemoryStoreAndForwardBuffer();
        var backbone = new RecordingBackbone();

        // Two devices' fixes interleaved in the buffer, each device in fix order.
        var a1 = Guid.NewGuid();
        var b1 = Guid.NewGuid();
        var a2 = Guid.NewGuid();
        var b2 = Guid.NewGuid();
        var a3 = Guid.NewGuid();

        await buffer.EnqueueAsync(Entry("device-A", a1));
        await buffer.EnqueueAsync(Entry("device-B", b1));
        await buffer.EnqueueAsync(Entry("device-A", a2));
        await buffer.EnqueueAsync(Entry("device-B", b2));
        await buffer.EnqueueAsync(Entry("device-A", a3));

        var service = NewService(buffer, backbone);
        await service.DrainAsync(CancellationToken.None);

        // Each device's events must appear in their enqueue order within the published sequence.
        List<Guid> published = backbone.PublishedEventIds;
        Assert.Equal(new[] { a1, a2, a3 }, published.Where(id => id == a1 || id == a2 || id == a3));
        Assert.Equal(new[] { b1, b2 }, published.Where(id => id == b1 || id == b2));
        // And overall FIFO order is preserved.
        Assert.Equal(new[] { a1, b1, a2, b2, a3 }, published);
    }

    [Fact]
    public async Task Drain_resumes_and_preserves_order_after_a_transient_failure()
    {
        var buffer = new InMemoryStoreAndForwardBuffer();
        // Fail the first 3 publish attempts (the whole broker is "down"), then recover.
        var backbone = new RecordingBackbone(failFirstAttempts: 3);

        var first = Guid.NewGuid();
        var second = Guid.NewGuid();
        var third = Guid.NewGuid();
        await buffer.EnqueueAsync(Entry("device-A", first));
        await buffer.EnqueueAsync(Entry("device-A", second));
        await buffer.EnqueueAsync(Entry("device-A", third));

        var service = NewService(buffer, backbone);
        await service.DrainAsync(CancellationToken.None);

        // Nothing lost, nothing reordered: the first entry retried through the outage and still led.
        Assert.Equal(new[] { first, second, third }, backbone.PublishedEventIds);
        Assert.Equal(0, buffer.Count);
        Assert.Equal(3, service.Replayed);
        Assert.True(service.Retries >= 3, $"expected at least 3 retries, saw {service.Retries}");
    }

    [Fact]
    public async Task A_failing_entry_blocks_the_drain_and_is_never_dropped()
    {
        var buffer = new InMemoryStoreAndForwardBuffer();
        // Permanent outage for the duration of the drain: publishing always throws.
        var backbone = new RecordingBackbone(failForever: true);

        await buffer.EnqueueAsync(Entry("device-A", Guid.NewGuid()));
        await buffer.EnqueueAsync(Entry("device-A", Guid.NewGuid()));

        var service = NewService(buffer, backbone);

        // Drain in the background; it can never complete while the backbone is down (it holds the
        // head entry and retries), so cancel it after it has had a chance to try.
        using var cts = new CancellationTokenSource();
        Task drain = service.DrainAsync(cts.Token);

        // Let it accumulate retries, then stop it.
        while (service.Retries < 5)
            await Task.Yield();
        cts.Cancel();
        await drain;

        // Nothing was published and nothing was lost: the held entry was returned to the buffer.
        Assert.Empty(backbone.PublishedEventIds);
        Assert.Equal(2, buffer.Count);
    }

    /// <summary>
    /// A fake backbone that records the envelope EventIds it accepts, in publish order, and can be
    /// told to fail a fixed number of attempts (a transient outage) or fail forever.
    /// </summary>
    private sealed class RecordingBackbone : IEventBackbone
    {
        private readonly object _gate = new();
        private readonly List<Guid> _published = new();
        private readonly int _failFirstAttempts;
        private readonly bool _failForever;
        private int _attempts;

        public RecordingBackbone(int failFirstAttempts = 0, bool failForever = false)
        {
            _failFirstAttempts = failFirstAttempts;
            _failForever = failForever;
        }

        public List<Guid> PublishedEventIds
        {
            get { lock (_gate) return new List<Guid>(_published); }
        }

        public Task PublishAsync<T>(string topic, string key, EventEnvelope<T> envelope, CancellationToken cancellationToken = default)
        {
            int n = Interlocked.Increment(ref _attempts);
            if (_failForever || n <= _failFirstAttempts)
                throw new InvalidOperationException("simulated backbone outage");

            lock (_gate)
                _published.Add(envelope.EventId);
            return Task.CompletedTask;
        }

        public IEventSubscription<T> Subscribe<T>(string topic, Guid? tenantFilter = null) =>
            throw new NotSupportedException("The replay tests do not consume subscriptions.");
    }
}
