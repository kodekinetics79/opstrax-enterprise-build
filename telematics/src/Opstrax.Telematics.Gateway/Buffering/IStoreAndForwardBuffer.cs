namespace Opstrax.Telematics.Gateway.Buffering;

/// <summary>
/// One event the gateway decoded and owns, but could not hand to the backbone. The entry
/// keeps the routing decision (<c>Topic</c>, <c>Key</c>) that was already made, so a later
/// retry republishes to exactly the same partition and does not have to re-derive ownership
/// from a packet that is long gone.
/// </summary>
/// <param name="Topic">The topic the envelope was destined for.</param>
/// <param name="Key">The tenant/company/device-scoped partition key it was to be published under.</param>
/// <param name="Envelope">The boxed <c>EventEnvelope&lt;T&gt;</c> awaiting republication.</param>
/// <param name="EnqueuedAt">When the publish failed and the entry was buffered.</param>
internal readonly record struct StoreAndForwardEntry(
    string Topic,
    string Key,
    object Envelope,
    DateTimeOffset EnqueuedAt);

/// <summary>
/// The gateway's durability seam. A device edge cannot apply backpressure to a truck: when
/// the backbone is unavailable, the bytes have already been decoded and acknowledged to the
/// device, so dropping them loses a fix that no longer exists anywhere else. Everything the
/// gateway fails to publish is parked here and replayed when the broker returns.
/// </summary>
/// <remarks>
/// <para>
/// The in-process implementation (<see cref="InMemoryStoreAndForwardBuffer"/>) survives a
/// broker outage but <b>not</b> a host restart. A production implementation backs this with
/// local disk (or a WAL) so a gateway pod that dies mid-outage still replays on boot. Keeping
/// that behind this interface is what lets the durability upgrade land without touching the
/// framing loop.
/// </para>
/// </remarks>
internal interface IStoreAndForwardBuffer
{
    /// <summary>Number of entries currently parked awaiting replay.</summary>
    int Count { get; }

    /// <summary>
    /// Parks an entry whose publish failed. Implementations must not throw for a full buffer —
    /// they should shed the oldest entry and keep accepting, because the newest fix is the one
    /// the live map needs.
    /// </summary>
    ValueTask EnqueueAsync(StoreAndForwardEntry entry, CancellationToken cancellationToken = default);

    /// <summary>Takes the next parked entry for a replay attempt, oldest first.</summary>
    bool TryDequeue(out StoreAndForwardEntry entry);
}
