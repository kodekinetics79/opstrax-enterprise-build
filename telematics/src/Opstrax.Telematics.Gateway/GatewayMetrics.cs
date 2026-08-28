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
    private long _framesReceived;
    private long _crcFailures;
    private long _malformedFrames;
    private long _acksSent;
    private long _loginPackets;
    private long _locationPackets;
    private long _heartbeatPackets;
    private long _statusPackets;
    private long _alarmPackets;
    private long _unknownPackets;
    private long _framesRejected;
    private long _sessionIdentityViolations;
    private long _duplicateSessionsDisplaced;

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

    // ── Frame-level counters ───────────────────────────────────────────────────
    // These partition the wire. For any connection:
    //
    //     FramesReceived == FramesDecoded + CrcFailures
    //
    // because a frame is counted as received exactly once, when the decoder steps over it, and it
    // then either yielded a message (decoded) or failed its checksum. MalformedFrames is counted
    // separately and is NOT part of that identity: malformed framing means the decoder could not
    // establish a frame boundary at all, so there is no frame to have received.

    /// <summary>
    /// Complete, correctly framed frames read off the wire — CRC-valid and CRC-invalid alike.
    /// This is the frame-attempt count: the denominator for a corruption rate.
    /// </summary>
    public long FramesReceived => Interlocked.Read(ref _framesReceived);

    /// <summary>
    /// Frames whose CRC-ITU checksum did not verify. Such a frame yields no message, is never
    /// normalized, published, stored or acknowledged, and never advances the replay ledger. A
    /// non-zero and rising value is the signature of a bad link or a corrupting middlebox — it is
    /// what distinguishes "the device is silent" from "the device is shouting through noise".
    /// </summary>
    public long CrcFailures => Interlocked.Read(ref _crcFailures);

    /// <summary>
    /// Streams abandoned because framing itself was impossible (bad start marker, an impossible
    /// length, missing stop bits). Distinct from <see cref="CrcFailures"/>: a CRC failure is one
    /// bad frame inside a trustworthy stream, this is a stream that cannot be framed at all.
    /// </summary>
    public long MalformedFrames => Interlocked.Read(ref _malformedFrames);

    /// <summary>Protocol acknowledgements actually written back to a device socket.</summary>
    public long AcksSent => Interlocked.Read(ref _acksSent);

    /// <summary>Decoded login frames.</summary>
    public long LoginPackets => Interlocked.Read(ref _loginPackets);

    /// <summary>Decoded location/GPS frames.</summary>
    public long LocationPackets => Interlocked.Read(ref _locationPackets);

    /// <summary>Decoded heartbeat frames.</summary>
    public long HeartbeatPackets => Interlocked.Read(ref _heartbeatPackets);

    /// <summary>Decoded status frames.</summary>
    public long StatusPackets => Interlocked.Read(ref _statusPackets);

    /// <summary>Decoded alarm frames.</summary>
    public long AlarmPackets => Interlocked.Read(ref _alarmPackets);

    /// <summary>
    /// Decoded frames whose protocol number this build does not decode (including GT06 <c>0x18</c>
    /// LBS-extended). They are well-framed and CRC-valid; their raw bytes are retained and no field
    /// is invented for them. A rising count is the signal to add a decoder, not a fault.
    /// </summary>
    public long UnknownPackets => Interlocked.Read(ref _unknownPackets);

    /// <summary>
    /// Decoded frames refused by an ingest gate — unresolvable identity, a device barred from
    /// ingest, or a frame on a session that never completed a login. Counts decisions after
    /// decoding, so it never overlaps <see cref="CrcFailures"/> or <see cref="MalformedFrames"/>.
    /// </summary>
    public long FramesRejected => Interlocked.Read(ref _framesRejected);

    /// <summary>
    /// Logins that tried to change the device identity of an already-bound socket. This is not a
    /// protocol event — a device does not re-introduce itself as a different device — so any
    /// non-zero value is either a badly broken tracker or someone attempting to attribute their
    /// traffic to another tenant's vehicle. Alarm on it.
    /// </summary>
    public long SessionIdentityViolations => Interlocked.Read(ref _sessionIdentityViolations);

    /// <summary>
    /// Sessions torn down because the same device authenticated on a newer socket. Routine on a
    /// roaming fleet (the tower dropped without a FIN); a sustained spike means devices are
    /// reconnect-looping.
    /// </summary>
    public long DuplicateSessionsDisplaced => Interlocked.Read(ref _duplicateSessionsDisplaced);

    /// <summary>Records <paramref name="count"/> complete frames stepped over by the decoder.</summary>
    public void AddFramesReceived(int count)
    {
        if (count > 0) Interlocked.Add(ref _framesReceived, count);
    }

    /// <summary>Records <paramref name="count"/> frames rejected by the checksum.</summary>
    public void AddCrcFailures(int count)
    {
        if (count > 0) Interlocked.Add(ref _crcFailures, count);
    }

    /// <summary>Records a stream abandoned for impossible framing.</summary>
    public void IncrementMalformedFrames() => Interlocked.Increment(ref _malformedFrames);

    /// <summary>Records an acknowledgement written to a device socket.</summary>
    public void IncrementAcksSent() => Interlocked.Increment(ref _acksSent);

    /// <summary>Records a decoded frame against its protocol category.</summary>
    /// <param name="messageType">The decoded category; anything unmapped counts as unknown.</param>
    public void RecordDecodedMessage(Contracts.Adapters.MessageType messageType)
    {
        switch (messageType)
        {
            case Contracts.Adapters.MessageType.Login:
                Interlocked.Increment(ref _loginPackets);
                break;
            case Contracts.Adapters.MessageType.Location:
                Interlocked.Increment(ref _locationPackets);
                break;
            case Contracts.Adapters.MessageType.Heartbeat:
                Interlocked.Increment(ref _heartbeatPackets);
                break;
            case Contracts.Adapters.MessageType.Status:
                Interlocked.Increment(ref _statusPackets);
                break;
            case Contracts.Adapters.MessageType.Alarm:
                Interlocked.Increment(ref _alarmPackets);
                break;
            default:
                Interlocked.Increment(ref _unknownPackets);
                break;
        }
    }

    /// <summary>Records a decoded frame refused by an ingest gate.</summary>
    public void IncrementFramesRejected() => Interlocked.Increment(ref _framesRejected);

    /// <summary>Records a login that attempted to re-identify an already-bound socket.</summary>
    public void IncrementSessionIdentityViolations() => Interlocked.Increment(ref _sessionIdentityViolations);

    /// <summary>Records a session torn down because the device authenticated on a newer socket.</summary>
    public void IncrementDuplicateSessionsDisplaced() => Interlocked.Increment(ref _duplicateSessionsDisplaced);
}
