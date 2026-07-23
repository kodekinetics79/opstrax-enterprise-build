namespace Opstrax.Telematics.Contracts.Eventing;

/// <summary>
/// The durable, partitioned, publish/subscribe event backbone the whole telematics fabric
/// runs on. This is the seam between the domain and the broker: application code depends only
/// on this interface, while the concrete implementation is an in-memory bus for tests/dev
/// (<see cref="InMemoryEventBackbone"/>) or a Kafka/Redpanda client in production. Swapping one
/// for the other must not change a single line of producer or consumer logic.
/// </summary>
/// <remarks>
/// <para>
/// Delivery is <b>at-least-once</b>: a consumer may see the same <see cref="EventEnvelope{T}.EventId"/>
/// more than once and must deduplicate on it. Ordering is guaranteed <b>per key</b> only — two
/// events published with the same key are delivered in publish order; no ordering is promised
/// across different keys (they may live on different partitions).
/// </para>
/// <para>
/// Keys must always be built with <see cref="TelematicsEventKey"/> so that they carry the
/// tenant/company/device scoping the backbone relies on for both ordering and isolation.
/// </para>
/// </remarks>
public interface IEventBackbone
{
    /// <summary>
    /// Publishes <paramref name="envelope"/> to <paramref name="topic"/> under
    /// <paramref name="key"/>. The key determines the partition, and thus both the ordering
    /// group (same key ⇒ same partition ⇒ ordered) and the isolation boundary (the key is
    /// tenant/company/device scoped). The returned task completes once the broker has durably
    /// accepted the event (or, for the in-memory bus, once it has been enqueued to every
    /// current subscriber).
    /// </summary>
    /// <typeparam name="T">The payload type carried on this topic.</typeparam>
    /// <param name="topic">A topic name from <see cref="TelematicsTopics"/>.</param>
    /// <param name="key">A partition/ordering key from <see cref="TelematicsEventKey"/>.</param>
    /// <param name="envelope">The event to publish.</param>
    /// <param name="cancellationToken">Cancels the publish attempt.</param>
    Task PublishAsync<T>(
        string topic,
        string key,
        EventEnvelope<T> envelope,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Opens a subscription to <paramref name="topic"/> and returns a handle whose
    /// <see cref="IEventSubscription{T}.ReadAllAsync"/> streams every matching event as an
    /// <see cref="IAsyncEnumerable{T}"/>. Dispose the handle to unsubscribe and release the
    /// underlying buffer.
    /// </summary>
    /// <typeparam name="T">The payload type expected on this topic.</typeparam>
    /// <param name="topic">A topic name from <see cref="TelematicsTopics"/>.</param>
    /// <param name="tenantFilter">
    /// When non-null, only events whose <see cref="EventEnvelope{T}.TenantId"/> equals this
    /// value are delivered — a defense-in-depth isolation guard on top of key scoping. When
    /// null, all tenants on the topic are delivered (for platform/admin consumers).
    /// </param>
    IEventSubscription<T> Subscribe<T>(string topic, Guid? tenantFilter = null);
}

/// <summary>
/// A live subscription handle returned by <see cref="IEventBackbone.Subscribe{T}"/>. Streams
/// delivered events and, when disposed, tears the subscription down and stops buffering.
/// </summary>
/// <typeparam name="T">The payload type carried on the subscribed topic.</typeparam>
public interface IEventSubscription<T> : IAsyncDisposable
{
    /// <summary>The topic this subscription is bound to.</summary>
    string Topic { get; }

    /// <summary>
    /// Streams delivered events until the subscription is disposed or
    /// <paramref name="cancellationToken"/> fires. Events sharing a key arrive in publish
    /// order; events on different keys may interleave.
    /// </summary>
    /// <param name="cancellationToken">Stops the stream.</param>
    IAsyncEnumerable<DeliveredEvent<T>> ReadAllAsync(CancellationToken cancellationToken = default);
}

/// <summary>
/// One event as handed to a consumer: the <see cref="Envelope"/> together with the
/// <see cref="Key"/> it was published under and the partition it landed on. Exposing the key
/// and partition lets consumers reason about ordering groups and shard their own work the same
/// way the broker did, without re-deriving the key from the payload.
/// </summary>
/// <typeparam name="T">The payload type.</typeparam>
/// <param name="Topic">The topic the event was delivered from.</param>
/// <param name="Key">The partition/ordering key the event was published under.</param>
/// <param name="Partition">The partition index the key mapped to.</param>
/// <param name="Envelope">The delivered envelope.</param>
public readonly record struct DeliveredEvent<T>(
    string Topic,
    string Key,
    int Partition,
    EventEnvelope<T> Envelope);
