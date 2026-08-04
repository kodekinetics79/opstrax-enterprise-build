using System.Diagnostics;
using Opstrax.Telematics.Contracts.Eventing;

namespace Opstrax.Telematics.IntegrationTests;

/// <summary>
/// Proves the two production-critical properties the Increment-1 reviewer flagged as missing on
/// <see cref="InMemoryEventBackbone"/>: bounded subscriber buffers apply <b>real backpressure</b>
/// to publishers (a full, un-drained subscriber blocks <see cref="IEventBackbone.PublishAsync{T}"/>),
/// and an event that exhausts its bounded delivery retries is routed to the
/// <see cref="TelematicsTopics.IntegrationDeadLetter"/> topic with provenance headers.
/// </summary>
public class EventBackboneBackpressureTests
{
    private static readonly Guid Tenant = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private const long Company = 42;
    private const string Device = "dev-backpressure";

    private static EventEnvelope<string> Envelope(string payload) =>
        EventEnvelope<string>.Create(Tenant, Company, payload);

    private static string Key() => TelematicsEventKey.ForDevice(Tenant, Company, Device);

    [Fact]
    public async Task PublishAsync_blocks_when_a_subscriber_buffer_is_full()
    {
        // Capacity 1, long backpressure window so a stuck subscriber blocks rather than dead-letters.
        var backbone = new InMemoryEventBackbone(
            subscriberCapacity: 1,
            backpressureWait: TimeSpan.FromSeconds(30),
            maxDeliveryAttempts: 3);

        string topic = TelematicsTopics.TelemetryNormalized;
        await using IEventSubscription<string> sub = backbone.Subscribe<string>(topic);

        // Fills the single buffer slot; completes immediately.
        await backbone.PublishAsync(topic, Key(), Envelope("first"))
            .WaitAsync(TimeSpan.FromSeconds(5));

        // Second publish must block: the buffer is full and nobody is reading.
        Task blocked = backbone.PublishAsync(topic, Key(), Envelope("second"));

        await Task.Delay(250);
        Assert.False(blocked.IsCompleted); // backpressure observed — the producer is held.

        // Drain one event; that frees a slot and lets the blocked publish complete.
        await using IAsyncEnumerator<DeliveredEvent<string>> reader =
            sub.ReadAllAsync().GetAsyncEnumerator();

        Assert.True(await reader.MoveNextAsync());
        Assert.Equal("first", reader.Current.Envelope.Payload);

        await blocked.WaitAsync(TimeSpan.FromSeconds(5)); // no longer blocked once drained.
        Assert.True(blocked.IsCompletedSuccessfully);

        // The previously-blocked "second" event is now buffered and readable in order.
        Assert.True(await reader.MoveNextAsync());
        Assert.Equal("second", reader.Current.Envelope.Payload);
    }

    [Fact]
    public async Task Event_that_exhausts_retries_lands_on_the_dead_letter_topic()
    {
        // Short, few attempts so a permanently-stuck subscriber dead-letters quickly.
        var backbone = new InMemoryEventBackbone(
            subscriberCapacity: 1,
            backpressureWait: TimeSpan.FromMilliseconds(75),
            maxDeliveryAttempts: 2);

        string topic = TelematicsTopics.TelemetryNormalized;

        // A subscriber that never reads — the poison pill's buffer will stay full forever.
        await using IEventSubscription<string> stuck = backbone.Subscribe<string>(topic);

        // A dead-letter consumer connected BEFORE the failure (no historical retention).
        await using IEventSubscription<string> dlq =
            backbone.Subscribe<string>(TelematicsTopics.IntegrationDeadLetter);

        // Fill the stuck subscriber's only slot.
        await backbone.PublishAsync(topic, Key(), Envelope("fills-slot"))
            .WaitAsync(TimeSpan.FromSeconds(5));

        // This one cannot be delivered; after exhausting retries it must be dead-lettered.
        var sw = Stopwatch.StartNew();
        await backbone.PublishAsync(topic, Key(), Envelope("poison"))
            .WaitAsync(TimeSpan.FromSeconds(5));
        sw.Stop();

        // Publish returned (did not hang) having spent roughly the bounded retry budget.
        Assert.True(sw.Elapsed >= TimeSpan.FromMilliseconds(75),
            $"expected to spend the bounded retry budget before dead-lettering, spent {sw.ElapsedMilliseconds}ms");

        // The poisoned event surfaces on the dead-letter topic with provenance headers.
        await using IAsyncEnumerator<DeliveredEvent<string>> deadReader =
            dlq.ReadAllAsync().GetAsyncEnumerator();

        Task<bool> next = deadReader.MoveNextAsync().AsTask();
        Assert.True(await next.WaitAsync(TimeSpan.FromSeconds(5)));

        DeliveredEvent<string> dead = deadReader.Current;
        Assert.Equal(TelematicsTopics.IntegrationDeadLetter, dead.Topic);
        Assert.Equal("poison", dead.Envelope.Payload);
        Assert.Equal(topic, dead.Envelope.Headers[InMemoryEventBackbone.DeadLetterOriginTopicHeader]);
        Assert.Equal(Key(), dead.Envelope.Headers[InMemoryEventBackbone.DeadLetterOriginKeyHeader]);
        Assert.True(dead.Envelope.Headers.ContainsKey(InMemoryEventBackbone.DeadLetterReasonHeader));
    }
}
