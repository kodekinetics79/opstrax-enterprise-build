using System.Collections.Concurrent;

namespace Opstrax.Telematics.Gateway.Buffering;

/// <summary>
/// In-process, bounded, FIFO implementation of <see cref="IStoreAndForwardBuffer"/> for
/// dev/test and for riding out short broker blips.
/// </summary>
/// <remarks>
/// <para>
/// The buffer is <b>bounded and lossy-oldest-first</b> on purpose. An unbounded retry queue
/// converts a broker outage into a gateway OOM — the failure mode gets strictly worse the
/// longer the outage lasts. When the cap is hit we drop the *oldest* entry, because during a
/// sustained outage a stale fix from an hour ago is worth less than the one that just landed.
/// </para>
/// <para>
/// Not durable across process restart — see <see cref="IStoreAndForwardBuffer"/>.
/// </para>
/// </remarks>
internal sealed class InMemoryStoreAndForwardBuffer : IStoreAndForwardBuffer
{
    private readonly ConcurrentQueue<StoreAndForwardEntry> _queue = new();
    private readonly int _capacity;

    /// <summary>Creates the buffer.</summary>
    /// <param name="capacity">Maximum parked entries before the oldest is shed. Must be positive.</param>
    public InMemoryStoreAndForwardBuffer(int capacity = 10_000)
    {
        if (capacity <= 0)
            throw new ArgumentOutOfRangeException(nameof(capacity), capacity, "Capacity must be positive.");
        _capacity = capacity;
    }

    /// <inheritdoc />
    public int Count => _queue.Count;

    /// <inheritdoc />
    public ValueTask EnqueueAsync(StoreAndForwardEntry entry, CancellationToken cancellationToken = default)
    {
        _queue.Enqueue(entry);

        // Shed oldest until we are back within the cap. Racy by construction under concurrent
        // enqueues, which is fine: the cap is a safety bound, not an exact quota.
        while (_queue.Count > _capacity && _queue.TryDequeue(out _))
        {
        }

        return ValueTask.CompletedTask;
    }

    /// <inheritdoc />
    public bool TryDequeue(out StoreAndForwardEntry entry) => _queue.TryDequeue(out entry);
}
