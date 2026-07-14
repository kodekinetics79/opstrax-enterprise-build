namespace Opstrax.Telematics.Gateway;

/// <summary>
/// Free-running counters for the gateway's edge behaviour. Deliberately dependency-free
/// (no OpenTelemetry coupling at this layer) so the hot path stays allocation-free and the
/// integration tests can assert on the exact same numbers an exporter would scrape.
/// </summary>
/// <remarks>
/// All mutation is via <see cref="Interlocked"/>: the counters are written from every
/// connection task concurrently and read from the accept loop (for the connection quota),
/// so they must be atomic rather than merely volatile.
/// </remarks>
internal sealed class GatewayMetrics
{
    private long _connectionsAccepted;
    private long _connectionsRejectedQuota;
    private long _activeConnections;
    private long _framesDecoded;
    private long _eventsPublished;
    private long _unknownDeviceRejections;
    private long _malformedConnectionsDropped;
    private long _idleConnectionsClosed;
    private long _publishFailuresBuffered;

    /// <summary>Connections accepted and handed to a connection task.</summary>
    public long ConnectionsAccepted => Interlocked.Read(ref _connectionsAccepted);

    /// <summary>Connections shed because <see cref="GatewayOptions.MaxConnections"/> was already reached.</summary>
    public long ConnectionsRejectedQuota => Interlocked.Read(ref _connectionsRejectedQuota);

    /// <summary>Connections currently being served. Gauge, not a counter.</summary>
    public long ActiveConnections => Interlocked.Read(ref _activeConnections);

    /// <summary>Protocol frames successfully decoded across all connections.</summary>
    public long FramesDecoded => Interlocked.Read(ref _framesDecoded);

    /// <summary>Canonical telemetry events durably handed to the backbone.</summary>
    public long EventsPublished => Interlocked.Read(ref _eventsPublished);

    /// <summary>
    /// Identity claims the registry could not resolve (or that resolved to a device barred
    /// from ingest). This is the number to alarm on: a spike means either a mis-provisioned
    /// fleet or someone probing the edge with forged IMEIs.
    /// </summary>
    public long UnknownDeviceRejections => Interlocked.Read(ref _unknownDeviceRejections);

    /// <summary>Connections dropped fail-closed because their stream was malformed beyond recovery.</summary>
    public long MalformedConnectionsDropped => Interlocked.Read(ref _malformedConnectionsDropped);

    /// <summary>Connections closed for exceeding <see cref="GatewayOptions.IdleTimeout"/>.</summary>
    public long IdleConnectionsClosed => Interlocked.Read(ref _idleConnectionsClosed);

    /// <summary>Events the backbone refused that were handed to the store-and-forward buffer instead of being dropped.</summary>
    public long PublishFailuresBuffered => Interlocked.Read(ref _publishFailuresBuffered);

    /// <summary>Records an accepted connection.</summary>
    public void IncrementConnectionsAccepted() => Interlocked.Increment(ref _connectionsAccepted);

    /// <summary>Records a connection shed by the quota.</summary>
    public void IncrementConnectionsRejectedQuota() => Interlocked.Increment(ref _connectionsRejectedQuota);

    /// <summary>Claims a connection slot and returns the new active count, so the caller can enforce the quota atomically.</summary>
    public long IncrementActiveConnections() => Interlocked.Increment(ref _activeConnections);

    /// <summary>Releases a connection slot.</summary>
    public void DecrementActiveConnections() => Interlocked.Decrement(ref _activeConnections);

    /// <summary>Records a successfully decoded frame.</summary>
    public void IncrementFramesDecoded() => Interlocked.Increment(ref _framesDecoded);

    /// <summary>Records a canonical event accepted by the backbone.</summary>
    public void IncrementEventsPublished() => Interlocked.Increment(ref _eventsPublished);

    /// <summary>Records an identity claim that did not resolve to an ingestable device.</summary>
    public void IncrementUnknownDeviceRejections() => Interlocked.Increment(ref _unknownDeviceRejections);

    /// <summary>Records a connection dropped for malformed framing.</summary>
    public void IncrementMalformedConnectionsDropped() => Interlocked.Increment(ref _malformedConnectionsDropped);

    /// <summary>Records a connection closed for idleness.</summary>
    public void IncrementIdleConnectionsClosed() => Interlocked.Increment(ref _idleConnectionsClosed);

    /// <summary>Records an event diverted into the store-and-forward buffer after a publish failure.</summary>
    public void IncrementPublishFailuresBuffered() => Interlocked.Increment(ref _publishFailuresBuffered);
}
