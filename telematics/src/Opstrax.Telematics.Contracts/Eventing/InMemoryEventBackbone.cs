using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using System.Threading.Channels;

namespace Opstrax.Telematics.Contracts.Eventing;

/// <summary>
/// A dependency-free, in-process implementation of <see cref="IEventBackbone"/> for local
/// development, unit tests and deterministic demos. It reproduces the three guarantees the real
/// broker gives — <b>per-key ordering</b>, <b>tenant/company/device isolation</b> and
/// <b>bounded buffering with producer backpressure</b> — without standing up Kafka/Redpanda, so
/// the same producer and consumer code exercised here runs unchanged against a cluster.
/// </summary>
/// <remarks>
/// <para>
/// <b>Ordering.</b> Fan-out to subscribers is serialized per topic on a
/// <see cref="SemaphoreSlim"/>, so every event is enqueued to every current subscriber strictly
/// in publish order. Because each subscriber reads from a single FIFO channel, events that share
/// a key are therefore always observed in publish order — the per-partition ordering contract.
/// The <see cref="DeliveredEvent{T}.Partition"/> index is computed with the same
/// <see cref="TelematicsEventKey.Partition"/> function a real cluster would use, so a key
/// co-partitions here exactly as it does in production.
/// </para>
/// <para>
/// <b>Isolation.</b> Keys are tenant/company/device scoped, and <see cref="Subscribe{T}"/>
/// accepts an optional tenant filter that drops any envelope whose
/// <see cref="EventEnvelope{T}.TenantId"/> does not match — defense in depth so a mis-keyed
/// event can never leak across tenants even in-process.
/// </para>
/// <para>
/// <b>Backpressure and dead-lettering.</b> Each subscriber owns a <em>bounded</em> channel
/// created with <see cref="BoundedChannelFullMode.Wait"/>. When a subscriber's buffer is full,
/// <see cref="PublishAsync{T}"/> <em>awaits</em> a free slot rather than silently dropping or
/// growing without limit — a slow consumer applies real backpressure to its producers, exactly
/// as a bounded partition would. The wait is cancellation-aware and bounded: each attempt waits
/// at most <c>backpressureWait</c>, and after <c>maxDeliveryAttempts</c> exhausted attempts the
/// envelope is routed to <see cref="TelematicsTopics.IntegrationDeadLetter"/> (with provenance
/// headers) instead of blocking the fabric forever. This is the in-memory analogue of a
/// broker's retry-then-DLQ policy for a poisoned or perpetually-backed-up consumer.
/// </para>
/// <para>
/// This bus is <b>not durable</b>: there is no persistence, no replay from an offset, and only
/// currently-connected subscribers receive events (no historical catch-up). Those are exactly
/// the properties that distinguish it from the production backbone, and why it is confined to
/// dev/test. It is safe for concurrent publishers and subscribers.
/// </para>
/// </remarks>
public sealed class InMemoryEventBackbone : IEventBackbone
{
    /// <summary>Header key stamped on a dead-lettered envelope naming the topic it failed on.</summary>
    public const string DeadLetterOriginTopicHeader = "deadletter.origin-topic";

    /// <summary>Header key stamped on a dead-lettered envelope carrying the original partition/ordering key.</summary>
    public const string DeadLetterOriginKeyHeader = "deadletter.origin-key";

    /// <summary>Header key stamped on a dead-lettered envelope explaining why delivery was abandoned.</summary>
    public const string DeadLetterReasonHeader = "deadletter.reason";

    private readonly ConcurrentDictionary<string, TopicState> _topics = new();
    private readonly int _partitionCount;
    private readonly int _subscriberCapacity;
    private readonly TimeSpan _backpressureWait;
    private readonly int _maxDeliveryAttempts;

    /// <summary>
    /// Creates the in-memory bus.
    /// </summary>
    /// <param name="partitionCount">
    /// The simulated partition count used to compute <see cref="DeliveredEvent{T}.Partition"/>.
    /// Matches a production topic's partition count so co-partitioning behaviour is identical.
    /// Defaults to 16; must be positive.
    /// </param>
    /// <param name="subscriberCapacity">
    /// The bounded buffer depth of each subscriber's channel. Once this many undelivered events
    /// are queued for a subscriber, publishers to that topic block (backpressure) until the
    /// subscriber drains. Defaults to 1024; must be positive.
    /// </param>
    /// <param name="backpressureWait">
    /// The maximum time a single publish attempt waits for a full subscriber to free a slot
    /// before the attempt is abandoned and retried. When <see langword="null"/> defaults to 30
    /// seconds; must be positive. Total worst-case block per subscriber is this multiplied by
    /// <paramref name="maxDeliveryAttempts"/>.
    /// </param>
    /// <param name="maxDeliveryAttempts">
    /// How many bounded <paramref name="backpressureWait"/> attempts to make before giving up on
    /// a stuck subscriber and routing the envelope to
    /// <see cref="TelematicsTopics.IntegrationDeadLetter"/>. Defaults to 3; must be at least 1.
    /// </param>
    /// <exception cref="ArgumentOutOfRangeException">A numeric argument is out of range.</exception>
    public InMemoryEventBackbone(
        int partitionCount = 16,
        int subscriberCapacity = 1024,
        TimeSpan? backpressureWait = null,
        int maxDeliveryAttempts = 3)
    {
        if (partitionCount <= 0)
            throw new ArgumentOutOfRangeException(nameof(partitionCount), partitionCount,
                "Partition count must be positive.");
        if (subscriberCapacity <= 0)
            throw new ArgumentOutOfRangeException(nameof(subscriberCapacity), subscriberCapacity,
                "Subscriber capacity must be positive.");
        if (maxDeliveryAttempts < 1)
            throw new ArgumentOutOfRangeException(nameof(maxDeliveryAttempts), maxDeliveryAttempts,
                "Delivery attempts must be at least 1.");

        TimeSpan wait = backpressureWait ?? TimeSpan.FromSeconds(30);
        if (wait <= TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(backpressureWait), wait,
                "Backpressure wait must be positive.");

        _partitionCount = partitionCount;
        _subscriberCapacity = subscriberCapacity;
        _backpressureWait = wait;
        _maxDeliveryAttempts = maxDeliveryAttempts;
    }

    /// <inheritdoc />
    public async Task PublishAsync<T>(
        string topic,
        string key,
        EventEnvelope<T> envelope,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(topic))
            throw new ArgumentException("Topic must be non-empty.", nameof(topic));
        if (string.IsNullOrEmpty(key))
            throw new ArgumentException("Key must be non-empty.", nameof(key));
        ArgumentNullException.ThrowIfNull(envelope);
        cancellationToken.ThrowIfCancellationRequested();

        // No subscribers yet ⇒ nothing to enqueue (dev bus has no historical retention).
        if (!_topics.TryGetValue(topic, out TopicState? state))
            return;

        int partition = TelematicsEventKey.Partition(key, _partitionCount);

        // Serialize fan-out per topic so all subscribers observe a single, consistent publish
        // order — the foundation of the per-key ordering guarantee. A SemaphoreSlim (not a lock)
        // because the fan-out below awaits when a bounded subscriber is full (backpressure).
        await state.PublishGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            ISubscriberSink[] sinks;
            lock (state.Gate)
                sinks = state.Sinks.ToArray();

            if (sinks.Length == 0)
                return;

            // Box once; every sink filters on payload type and tenant.
            object boxed = new DeliveredEvent<T>(topic, key, partition, envelope);

            foreach (ISubscriberSink sink in sinks)
            {
                bool accepted = await sink.DeliverAsync(boxed, cancellationToken).ConfigureAwait(false);

                // A subscriber that stayed full for every bounded attempt has exhausted its
                // retries. Route the poisoned envelope to the dead-letter topic rather than
                // blocking the whole topic on it forever. Events that were already on the
                // dead-letter topic are terminal and are simply dropped if their sink is stuck.
                if (!accepted && !string.Equals(topic, TelematicsTopics.IntegrationDeadLetter, StringComparison.Ordinal))
                {
                    DeadLetter(
                        topic,
                        key,
                        envelope,
                        $"delivery to a subscriber on '{topic}' exhausted {_maxDeliveryAttempts} " +
                        $"attempt(s) of {_backpressureWait.TotalMilliseconds:0}ms under backpressure");
                }
            }
        }
        finally
        {
            state.PublishGate.Release();
        }
    }

    /// <inheritdoc />
    public IEventSubscription<T> Subscribe<T>(string topic, Guid? tenantFilter = null)
    {
        if (string.IsNullOrEmpty(topic))
            throw new ArgumentException("Topic must be non-empty.", nameof(topic));

        TopicState state = _topics.GetOrAdd(topic, _ => new TopicState());
        var subscription = new Subscription<T>(
            topic, tenantFilter, state, _subscriberCapacity, _backpressureWait, _maxDeliveryAttempts);

        lock (state.Gate)
            state.Sinks.Add(subscription);

        return subscription;
    }

    /// <summary>
    /// Best-effort, non-blocking terminal delivery of a poisoned envelope to the dead-letter
    /// topic. Runs while the origin topic's publish gate is held, so it must never await; it
    /// stamps provenance headers and <see cref="ISubscriberSink.TryDeliver"/>s to each current
    /// dead-letter subscriber. If the dead-letter buffer is itself full the event is dropped —
    /// the dead-letter topic is the end of the line, so it must not become a backpressure source.
    /// </summary>
    private void DeadLetter<T>(string originTopic, string key, EventEnvelope<T> envelope, string reason)
    {
        if (!_topics.TryGetValue(TelematicsTopics.IntegrationDeadLetter, out TopicState? dlq))
            return;

        var headers = new Dictionary<string, string>(envelope.Headers)
        {
            [DeadLetterOriginTopicHeader] = originTopic,
            [DeadLetterOriginKeyHeader] = key,
            [DeadLetterReasonHeader] = reason,
        };

        EventEnvelope<T> dead = envelope with { Headers = headers };
        int partition = TelematicsEventKey.Partition(key, _partitionCount);
        object boxed = new DeliveredEvent<T>(TelematicsTopics.IntegrationDeadLetter, key, partition, dead);

        lock (dlq.Gate)
        {
            foreach (ISubscriberSink sink in dlq.Sinks)
                sink.TryDeliver(boxed);
        }
    }

    /// <summary>Non-generic delivery sink so a topic can hold subscribers of differing payload types.</summary>
    private interface ISubscriberSink
    {
        /// <summary>
        /// Enqueues <paramref name="boxedDelivery"/> to this subscriber, awaiting a free slot
        /// under backpressure. Returns <see langword="true"/> when the event was delivered, was
        /// not addressed to this subscriber (type/tenant mismatch), or the subscriber has been
        /// disposed; returns <see langword="false"/> only when every bounded retry was exhausted
        /// while the buffer stayed full — the caller's signal to dead-letter.
        /// </summary>
        /// <param name="boxedDelivery">A boxed <see cref="DeliveredEvent{T}"/>; the sink unboxes to its own payload type.</param>
        /// <param name="cancellationToken">Cancels the publish attempt.</param>
        ValueTask<bool> DeliverAsync(object boxedDelivery, CancellationToken cancellationToken);

        /// <summary>
        /// Non-blocking best-effort enqueue used for terminal dead-letter delivery. Returns
        /// whether the event was accepted; never waits and never throws on a full buffer.
        /// </summary>
        /// <param name="boxedDelivery">A boxed <see cref="DeliveredEvent{T}"/>; the sink unboxes to its own payload type.</param>
        bool TryDeliver(object boxedDelivery);
    }

    /// <summary>Per-topic subscriber registry, the fan-out serialization gate, and the registry lock.</summary>
    private sealed class TopicState
    {
        /// <summary>Guards mutation and snapshotting of <see cref="Sinks"/>.</summary>
        public object Gate { get; } = new();

        /// <summary>Serializes publishes on this topic so writes preserve global publish order across async, backpressured fan-out.</summary>
        public SemaphoreSlim PublishGate { get; } = new(1, 1);

        /// <summary>Currently-connected subscribers.</summary>
        public List<ISubscriberSink> Sinks { get; } = new();
    }

    private sealed class Subscription<T> : IEventSubscription<T>, ISubscriberSink
    {
        private readonly Guid? _tenantFilter;
        private readonly TopicState _owner;
        private readonly TimeSpan _backpressureWait;
        private readonly int _maxDeliveryAttempts;
        private readonly Channel<DeliveredEvent<T>> _channel;
        private int _disposed;

        public Subscription(
            string topic,
            Guid? tenantFilter,
            TopicState owner,
            int capacity,
            TimeSpan backpressureWait,
            int maxDeliveryAttempts)
        {
            Topic = topic;
            _tenantFilter = tenantFilter;
            _owner = owner;
            _backpressureWait = backpressureWait;
            _maxDeliveryAttempts = maxDeliveryAttempts;
            _channel = Channel.CreateBounded<DeliveredEvent<T>>(new BoundedChannelOptions(capacity)
            {
                SingleReader = true,
                SingleWriter = false,
                // Real backpressure: writers await a free slot instead of dropping or unbounded growth.
                FullMode = BoundedChannelFullMode.Wait,
            });
        }

        public string Topic { get; }

        // Serialized per topic by TopicState.PublishGate, so writes preserve global publish order.
        public async ValueTask<bool> DeliverAsync(object boxedDelivery, CancellationToken cancellationToken)
        {
            // Only deliver envelopes whose payload type matches this subscriber's T. A topic
            // should carry one payload type; a mismatch means a wrong-typed subscriber and is
            // simply skipped (treated as delivered — it was never addressed to us).
            if (boxedDelivery is not DeliveredEvent<T> typed)
                return true;

            if (_tenantFilter is { } tenant && typed.Envelope.TenantId != tenant)
                return true;

            for (int attempt = 1; attempt <= _maxDeliveryAttempts; attempt++)
            {
                // Cancellation-aware bounded wait: cancel on the caller's token OR when this
                // attempt's backpressure budget elapses.
                using var attemptCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                attemptCts.CancelAfter(_backpressureWait);

                try
                {
                    await _channel.Writer.WriteAsync(typed, attemptCts.Token).ConfigureAwait(false);
                    return true;
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    // A genuine caller cancellation propagates; it is not a dead-letter.
                    throw new OperationCanceledException(cancellationToken);
                }
                catch (OperationCanceledException)
                {
                    // Only this attempt's bounded wait elapsed while the buffer stayed full;
                    // retry until attempts are exhausted, then fall through to dead-letter.
                }
                catch (ChannelClosedException)
                {
                    // Subscriber disposed mid-publish; nothing to deliver and nothing to dead-letter.
                    return true;
                }
            }

            return false;
        }

        public bool TryDeliver(object boxedDelivery)
        {
            if (boxedDelivery is not DeliveredEvent<T> typed)
                return false;

            if (_tenantFilter is { } tenant && typed.Envelope.TenantId != tenant)
                return false;

            return _channel.Writer.TryWrite(typed);
        }

        public async IAsyncEnumerable<DeliveredEvent<T>> ReadAllAsync(
            [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            await foreach (DeliveredEvent<T> item in
                _channel.Reader.ReadAllAsync(cancellationToken).ConfigureAwait(false))
            {
                yield return item;
            }
        }

        public ValueTask DisposeAsync()
        {
            if (Interlocked.Exchange(ref _disposed, 1) == 0)
            {
                lock (_owner.Gate)
                    _owner.Sinks.Remove(this);
                _channel.Writer.TryComplete();
            }

            return ValueTask.CompletedTask;
        }
    }
}
