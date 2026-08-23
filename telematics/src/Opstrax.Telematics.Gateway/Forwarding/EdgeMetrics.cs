namespace Opstrax.Telematics.Gateway.Forwarding;

/// <summary>
/// Free-running counters for the HTTPS forwarding edge, alongside <see cref="GatewayMetrics"/>'s
/// connection-level ones. Dependency-free and atomic for the same reasons.
/// </summary>
/// <remarks>
/// These exist to answer the question an operator actually asks during an incident: <em>is the
/// edge dropping fixes, and if so at which gate?</em> Every gate that can discard a frame
/// increments exactly one of these, so the counters partition the loss rather than overlapping.
/// </remarks>
internal sealed class EdgeMetrics
{
    private long _allowlistRefusals;
    private long _observationsDelivered;
    private long _observationsParked;
    private long _observationsRejected;
    private long _replayDuplicatesDropped;
    private long _normalizationRejections;
    private long _implausibleFieldsDropped;
    private long _heartbeatsNotForwarded;
    private long _outboxEntriesDiscarded;
    private long _unidentifiedProtocolConnections;

    /// <summary>
    /// Login attempts refused because the claimed IMEI is not on the allowlist. On a public port
    /// a low background rate is internet scanning; a spike on a <em>known</em> IMEI is worth
    /// investigating, and a spike right after a deployment usually means the allowlist file did
    /// not ship.
    /// </summary>
    public long AllowlistRefusals => Interlocked.Read(ref _allowlistRefusals);

    /// <summary>Fixes OpsTrax accepted, counting a replay-ledger hit as accepted.</summary>
    public long ObservationsDelivered => Interlocked.Read(ref _observationsDelivered);

    /// <summary>Fixes parked in the outbox because OpsTrax was unreachable or failing closed.</summary>
    public long ObservationsParked => Interlocked.Read(ref _observationsParked);

    /// <summary>Fixes OpsTrax terminally refused (unprovisioned, quarantined, wrong tenant, malformed).</summary>
    public long ObservationsRejected => Interlocked.Read(ref _observationsRejected);

    /// <summary>Frames suppressed by the edge replay guard as byte-for-byte retransmissions.</summary>
    public long ReplayDuplicatesDropped => Interlocked.Read(ref _replayDuplicatesDropped);

    /// <summary>Frames that could not become a valid payload (bad coordinates, no or unusable device clock).</summary>
    public long NormalizationRejections => Interlocked.Read(ref _normalizationRejections);

    /// <summary>Auxiliary sensor readings discarded as out-of-range while still forwarding the position.</summary>
    public long ImplausibleFieldsDropped => Interlocked.Read(ref _implausibleFieldsDropped);

    /// <summary>
    /// Heartbeat/status frames acknowledged but not forwarded. The trusted-gateway ingest contract
    /// requires a coordinate, so a positionless keepalive has nowhere to go — see
    /// <c>ForwardingConnection</c>.
    /// </summary>
    public long HeartbeatsNotForwarded => Interlocked.Read(ref _heartbeatsNotForwarded);

    /// <summary>Parked entries discarded without delivery, by the queue ceiling or the age limit.</summary>
    public long OutboxEntriesDiscarded => Interlocked.Read(ref _outboxEntriesDiscarded);

    /// <summary>Connections closed because no installed adapter recognised their opening bytes.</summary>
    public long UnidentifiedProtocolConnections => Interlocked.Read(ref _unidentifiedProtocolConnections);

    /// <summary>Records a login refused by the IMEI allowlist.</summary>
    public void IncrementAllowlistRefusals() => Interlocked.Increment(ref _allowlistRefusals);

    /// <summary>Records a fix accepted by OpsTrax.</summary>
    public void IncrementObservationsDelivered() => Interlocked.Increment(ref _observationsDelivered);

    /// <summary>Records a fix parked in the outbox.</summary>
    public void IncrementObservationsParked() => Interlocked.Increment(ref _observationsParked);

    /// <summary>Records a fix terminally refused by OpsTrax.</summary>
    public void IncrementObservationsRejected() => Interlocked.Increment(ref _observationsRejected);

    /// <summary>Records a duplicate frame suppressed by the edge replay guard.</summary>
    public void IncrementReplayDuplicatesDropped() => Interlocked.Increment(ref _replayDuplicatesDropped);

    /// <summary>Records a frame that could not be normalized into a valid payload.</summary>
    public void IncrementNormalizationRejections() => Interlocked.Increment(ref _normalizationRejections);

    /// <summary>Records <paramref name="count"/> out-of-range auxiliary readings dropped from a payload.</summary>
    public void AddImplausibleFieldsDropped(int count)
    {
        if (count > 0) Interlocked.Add(ref _implausibleFieldsDropped, count);
    }

    /// <summary>Records a positionless frame acknowledged but not forwarded.</summary>
    public void IncrementHeartbeatsNotForwarded() => Interlocked.Increment(ref _heartbeatsNotForwarded);

    /// <summary>Records <paramref name="count"/> parked entries discarded undelivered.</summary>
    public void AddOutboxEntriesDiscarded(int count)
    {
        if (count > 0) Interlocked.Add(ref _outboxEntriesDiscarded, count);
    }

    /// <summary>Records a connection closed for speaking no recognised protocol.</summary>
    public void IncrementUnidentifiedProtocolConnections() => Interlocked.Increment(ref _unidentifiedProtocolConnections);
}
