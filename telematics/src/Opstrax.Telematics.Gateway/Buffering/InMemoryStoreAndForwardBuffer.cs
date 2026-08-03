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
    private readonly object _gate = new();
    private readonly LinkedList<StoreAndForwardEntry> _queue = new();
    private readonly Dictionary<Guid, StoreAndForwardEntry> _claimed = new();
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
    public int Count
    {
        get { lock (_gate) return _queue.Count + _claimed.Count; }
    }

    /// <inheritdoc />
    public ValueTask EnqueueAsync(StoreAndForwardEntry entry, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (_gate)
        {
            _queue.AddLast(entry);
            while (_queue.Count + _claimed.Count > _capacity && _queue.First is not null)
                _queue.RemoveFirst();
        }
        return ValueTask.CompletedTask;
    }

    /// <inheritdoc />
    public ValueTask<StoreAndForwardLease?> TryAcquireAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (_gate)
        {
            if (_queue.First is null)
                return ValueTask.FromResult<StoreAndForwardLease?>(null);
            StoreAndForwardEntry entry = _queue.First.Value;
            _queue.RemoveFirst();
            var token = Guid.NewGuid();
            _claimed[token] = entry;
            return ValueTask.FromResult<StoreAndForwardLease?>(new StoreAndForwardLease(token, entry));
        }
    }

    /// <inheritdoc />
    public ValueTask CompleteAsync(StoreAndForwardLease lease, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (_gate)
            _claimed.Remove(lease.Token);
        return ValueTask.CompletedTask;
    }

    /// <inheritdoc />
    public ValueTask AbandonAsync(
        StoreAndForwardLease lease,
        string? reason = null,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (_gate)
        {
            if (_claimed.Remove(lease.Token, out StoreAndForwardEntry entry))
                _queue.AddFirst(entry);
        }
        return ValueTask.CompletedTask;
    }
}
