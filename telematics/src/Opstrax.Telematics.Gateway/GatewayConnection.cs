using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Threading.Channels;
using Microsoft.Extensions.Logging;
using Opstrax.Telematics.Contracts;
using Opstrax.Telematics.Contracts.Adapters;
using Opstrax.Telematics.Contracts.Eventing;
using Opstrax.Telematics.Contracts.Identity;
using Opstrax.Telematics.Contracts.Quality;
using Opstrax.Telematics.Gateway.Buffering;
using Opstrax.Telematics.Gateway.Eventing;
using Opstrax.Telematics.Gateway.Observability;
using Opstrax.Telematics.Gateway.Projection;
using Opstrax.Telematics.Gateway.Security;
using Opstrax.Telematics.Gateway.Security.Auth;
using Opstrax.Telematics.Gateway.Security.Replay;
using Opstrax.Telematics.Protocols.Gt06;

namespace Opstrax.Telematics.Gateway;

/// <summary>
/// Serves exactly one accepted TCP connection: reassembles GT06 frames off the socket,
/// resolves the device's owner through the registry, acknowledges what the protocol requires,
/// and pushes canonical events onto the backbone.
/// </summary>
/// <remarks>
/// <para>
/// <b>Isolation is the whole job.</b> A connection is an untrusted, attacker-controlled byte
/// source. Every failure mode it can produce — a corrupt frame, a hostile length header, a
/// half-open socket, a device that logs in as somebody else's IMEI, a socket that dies
/// mid-write — is contained inside <see cref="RunAsync"/>'s try/catch and takes down nothing
/// but this one connection. Nothing here is allowed to reach the host.
/// </para>
/// <para>
/// <b>Session state, and why it exists.</b> GT06 sends the IMEI exactly once, in the login
/// frame; every subsequent location/heartbeat frame is anonymous and identified only by the
/// TCP connection it arrived on. So the resolved owner is pinned to the connection at login
/// and reused for that connection's lifetime. A frame that arrives before a successful login
/// has no resolvable identity and is rejected — it is <em>not</em> attributed to a guess.
/// </para>
/// <para>
/// <b>Backpressure.</b> Decoded events go onto a bounded channel drained by a single pump task
/// (single pump ⇒ per-device publish order is preserved). When the backbone slows down the
/// channel fills, the read loop blocks on <c>ChannelWriter&lt;T&gt;.WriteAsync</c>, it stops
/// draining the socket, and the TCP receive window closes — pushing the backpressure onto the
/// device instead of into an unbounded in-memory queue.
/// </para>
/// </remarks>
internal sealed class GatewayConnection
{
    /// <summary>The OTel/metric protocol label. Lowercase per metrics.md, distinct from the adapter name "GT06".</summary>
    private const string ProtocolLabel = "gt06";

    private readonly TcpClient _client;
    private readonly Gt06Adapter _adapter;
    private readonly IDeviceRegistry _registry;
    private readonly IDeviceAuthenticator _authenticator;
    private readonly ITelemetryReplayGuard _replayGuard;
    private readonly IPositionProjectionStore _projectionStore;
    private readonly IEventBackbone _backbone;
    private readonly IStoreAndForwardBuffer _forwardBuffer;
    private readonly GatewayOptions _options;
    private readonly GatewayMetrics _metrics;
    private readonly ILogger _logger;

    /// <summary>Correlates every event, ack and rejection derived from this connection.</summary>
    private readonly Guid _correlationId = Guid.NewGuid();

    private readonly Channel<PendingTelemetry> _publishChannel;
    private readonly string _remoteEndpoint;

    /// <summary>Observed source address of the peer, for the authenticator's source-IP pin. Null if unknown.</summary>
    private readonly IPAddress? _remoteIp;

    /// <summary>
    /// Set once a login is refused (rejected/quarantined) so the read loop terminates instead of
    /// leaving a rejected, unbound session draining the socket — the Increment-1 finding.
    /// </summary>
    private bool _closeRequested;

    /// <summary>Frame reassembly buffer: holds the bytes that have not yet formed a complete frame.</summary>
    private byte[] _accumulator;
    private int _accumulated;

    /// <summary>Registry-resolved owner for this session. Null until a login resolves. NEVER derived from a packet.</summary>
    private ResolvedDeviceOwner? _owner;

    /// <summary>
    /// The IMEI claim that bound this session, kept so a later login can be compared against it
    /// WITHOUT going back to the registry. One socket carries one device for its whole life, so a
    /// second login is either that same device re-introducing itself or an attempt to re-point the
    /// session — and the second must never reach the registry or the authenticator at all.
    /// </summary>
    private string? _boundImeiClaim;

    /// <summary>Tracks which socket is authoritative for a device across the whole gateway.</summary>
    private readonly ActiveDeviceSessionRegistry _sessions;

    /// <summary>Device key this session holds a lease on, and the lease id. Null until login binds.</summary>
    private string? _leaseKey;
    private long _leaseId;

    /// <summary>
    /// Cancelled when this session must stop: host shutdown, or a newer socket taking ownership of
    /// the same device. The read loop runs on this token rather than the host token so a displaced
    /// session dies promptly instead of lingering until its idle timeout.
    /// </summary>
    private CancellationTokenSource? _sessionCts;

    /// <summary>Whether the opening bytes have been positively identified as GT06.</summary>
    private bool _protocolIdentified;

    public GatewayConnection(
        TcpClient client,
        Gt06Adapter adapter,
        IDeviceRegistry registry,
        IDeviceAuthenticator authenticator,
        ITelemetryReplayGuard replayGuard,
        IPositionProjectionStore projectionStore,
        IEventBackbone backbone,
        IStoreAndForwardBuffer forwardBuffer,
        GatewayOptions options,
        ActiveDeviceSessionRegistry sessions,
        GatewayMetrics metrics,
        ILogger logger)
    {
        _client = client;
        _adapter = adapter;
        _sessions = sessions ?? throw new ArgumentNullException(nameof(sessions));
        _registry = registry;
        _authenticator = authenticator;
        _replayGuard = replayGuard;
        _projectionStore = projectionStore;
        _backbone = backbone;
        _forwardBuffer = forwardBuffer;
        _options = options;
        _metrics = metrics;
        _logger = logger;

        EndPoint? remote = SafeRemoteEndPoint(client);
        _remoteEndpoint = MaskRemoteEndpoint(remote);
        _remoteIp = (remote as IPEndPoint)?.Address;
        _accumulator = new byte[Math.Max(options.ReadBufferBytes, 512)];

        _publishChannel = Channel.CreateBounded<PendingTelemetry>(
            new BoundedChannelOptions(Math.Max(1, options.MaxInFlightPerConnection))
            {
                FullMode = BoundedChannelFullMode.Wait, // Wait == backpressure. Never DropWrite: a dropped fix is a lost fix.
                SingleReader = true,
                SingleWriter = true,
            });
    }

    private static string MaskRemoteEndpoint(EndPoint? endpoint)
    {
        if (endpoint is not IPEndPoint ipEndpoint) return "unknown";
        byte[] bytes = ipEndpoint.Address.GetAddressBytes();
        int retained = bytes.Length == 4 ? 3 : 6; // IPv4 /24; IPv6 /48. Never retain the source port.
        Array.Clear(bytes, retained, bytes.Length - retained);
        return $"{new IPAddress(bytes)}/{(bytes.Length == 4 ? 24 : 48)}";
    }

    /// <summary>
    /// Runs the connection to completion. Returns normally on every expected termination
    /// (peer closed, idle timeout, malformed stream, host shutdown) — it does not propagate
    /// connection-scoped faults to the accept loop.
    /// </summary>
    /// <param name="stoppingToken">Fires on host shutdown; triggers a graceful drain.</param>
    public async Task RunAsync(CancellationToken stoppingToken)
    {
        _logger.LogDebug("Connection accepted from {RemoteEndpoint} (correlation {CorrelationId}).",
            _remoteEndpoint, _correlationId);

        // The session token is the host token PLUS "a newer socket took this device". Everything
        // below runs on it, so displacing this session actually unblocks the socket read instead of
        // leaving a stale owner draining bytes until the idle timeout.
        using var sessionCts = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);
        _sessionCts = sessionCts;

        // The pump runs for the connection's whole life and is awaited in the finally block, so
        // queued events are still published even when the read loop ends abruptly.
        Task pump = Task.Run(PublishPumpAsync, CancellationToken.None);

        try
        {
            using (_client)
            {
                await ReadLoopAsync(_client.GetStream(), sessionCts.Token).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException)
        {
            // Host shutdown observed while writing an ack or queuing an event. Routine.
            _logger.LogDebug("Connection {RemoteEndpoint} cancelled by host shutdown.", _remoteEndpoint);
        }
        catch (Exception ex) when (ex is IOException or SocketException)
        {
            // The peer vanished mid-write. On a cellular fleet this is hourly, not exceptional.
            _logger.LogDebug(ex, "Connection {RemoteEndpoint} dropped by peer.", _remoteEndpoint);
        }
        catch (Exception ex)
        {
            // Last-resort net. Anything that escapes ReadLoopAsync's own handling dies here,
            // with this connection, and never reaches the host.
            _logger.LogError(ex, "Unhandled fault on connection {RemoteEndpoint}; closing it. Host and other connections are unaffected.",
                _remoteEndpoint);
        }
        finally
        {
            // Release the device lease BEFORE anything else can fail. Release is lease-checked, so
            // if a newer socket already displaced this one it owns the registry entry and this call
            // is a no-op — the old session can never evict the new one on its way out.
            if (_leaseKey is { } leaseKey)
            {
                _sessions.Release(leaseKey, _leaseId);
                _leaseKey = null;
            }

            // Graceful drain: stop accepting new events, then let the pump finish publishing
            // everything already decoded before this connection is considered done.
            _publishChannel.Writer.TryComplete();

            try
            {
                await pump.ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Publish pump faulted for connection {RemoteEndpoint}.", _remoteEndpoint);
            }

            _logger.LogDebug("Connection {RemoteEndpoint} closed.", _remoteEndpoint);
        }
    }

    // ── Framing loop ───────────────────────────────────────────────────────────

    private async Task ReadLoopAsync(NetworkStream stream, CancellationToken stoppingToken)
    {
        byte[] readBuffer = new byte[Math.Max(256, _options.ReadBufferBytes)];

        while (!stoppingToken.IsCancellationRequested)
        {
            int read;
            try
            {
                read = await ReadWithIdleTimeoutAsync(stream, readBuffer, stoppingToken).ConfigureAwait(false);
            }
            catch (IdleTimeoutException)
            {
                _metrics.IncrementIdleConnectionsClosed();
                _logger.LogInformation(
                    "Closing connection {RemoteEndpoint}: idle for more than {IdleTimeout}.",
                    _remoteEndpoint, _options.IdleTimeout);
                return;
            }
            catch (OperationCanceledException)
            {
                _logger.LogDebug("Connection {RemoteEndpoint} cancelled by host shutdown; draining.", _remoteEndpoint);
                return;
            }
            catch (IOException ex)
            {
                // Cellular trackers drop sockets constantly. This is routine, not an error.
                _logger.LogDebug(ex, "Connection {RemoteEndpoint} reset by peer.", _remoteEndpoint);
                return;
            }

            if (read == 0)
            {
                _logger.LogDebug("Connection {RemoteEndpoint} closed by peer.", _remoteEndpoint);
                return;
            }

            Append(readBuffer.AsSpan(0, read));
            DateTime receivedAtUtc = DateTime.UtcNow;

            // ── Protocol identification: is this stream even GT06? ──────────────
            if (!_protocolIdentified)
            {
                ProtocolMatch match = _adapter.TryIdentify(_accumulator.AsSpan(0, _accumulated));

                if (match.NeedMoreData)
                    continue; // fewer than 2 bytes so far; cannot decide yet.

                if (!match.IsMatch)
                {
                    // Not our protocol. Fail closed and hang up rather than guessing at the bytes.
                    _metrics.IncrementMalformedConnectionsDropped();
                    _logger.LogWarning(
                        "Dropping connection {RemoteEndpoint}: opening bytes are not a recognised GT06 stream.",
                        _remoteEndpoint);
                    return;
                }

                _protocolIdentified = true;
                _logger.LogDebug("Connection {RemoteEndpoint} identified as {Protocol} (confidence {Confidence}).",
                    _remoteEndpoint, Gt06Adapter.ProtocolName, match.Confidence);
            }

            // ── Decode every complete frame currently buffered ──────────────────
            IReadOnlyList<DecodedMessage> messages;
            int consumed;
            long decodeStart = Stopwatch.GetTimestamp();
            FrameDecodeStats frameStats;
            try
            {
                // Decode is span-based and synchronous; nothing it returns aliases the
                // accumulator (DecodedMessage copies its raw frame), so it is safe to
                // compact the buffer and then await.
                messages = _adapter.Decode(_accumulator.AsSpan(0, _accumulated), out consumed, out frameStats);
            }
            catch (ProtocolException ex)
            {
                // Impossible framing — a hostile or hopelessly corrupt stream. Fail closed:
                // drop THIS connection only. Never fabricate an event out of corrupt bytes.
                _metrics.IncrementMalformedFrames();
                _metrics.IncrementMalformedConnectionsDropped();
                _logger.LogWarning(ex,
                    "Dropping connection {RemoteEndpoint}: malformed {Protocol} framing at offset {Offset}.",
                    _remoteEndpoint, ex.AdapterName ?? Gt06Adapter.ProtocolName, ex.Offset);
                return;
            }

            if (consumed > 0)
                Consume(consumed);

            // Counted from the decoder's own report so a CRC-invalid frame is visible as a frame
            // that ARRIVED and was rejected, rather than as silence. A bad-CRC frame contributes to
            // FramesReceived and CrcFailures and to nothing else: it produced no message, so it can
            // reach neither normalization, nor the replay ledger, nor storage, nor an ACK.
            _metrics.AddFramesReceived(frameStats.FramesRead);
            _metrics.AddCrcFailures(frameStats.CrcFailures);

            if (messages.Count > 0)
            {
                double decodeMs = Stopwatch.GetElapsedTime(decodeStart).TotalMilliseconds;
                TelematicsInstrumentation.DecodeLatencyMs.Record(decodeMs, new TagList
                {
                    { TelematicsInstrumentation.MetricLabels.Protocol, ProtocolLabel },
                    { TelematicsInstrumentation.MetricLabels.Adapter, Gt06Adapter.ProtocolName },
                    { TelematicsInstrumentation.MetricLabels.AdapterVersion, Gt06Adapter.AdapterVersion },
                });
            }

            foreach (DecodedMessage message in messages)
            {
                _metrics.IncrementFramesDecoded();
                _metrics.RecordDecodedMessage(message.MessageType);
                await HandleMessageAsync(message, stream, receivedAtUtc, stoppingToken).ConfigureAwait(false);

                // A refused or displaced session stops mid-batch: the remaining frames in this read
                // belong to a session that is no longer entitled to be served.
                if (_closeRequested || stoppingToken.IsCancellationRequested)
                    break;
            }

            // A refused login (rejected/quarantined) tears the connection down here rather than
            // letting the read loop keep draining a session that will never be bound to an owner.
            if (_closeRequested)
            {
                _logger.LogDebug("Closing connection {RemoteEndpoint} after a refused login.", _remoteEndpoint);
                return;
            }

            // Residue guard: whatever is left is, by definition, an incomplete frame. If that
            // partial frame alone exceeds the max frame size, no valid frame can ever complete
            // it — a peer is dribbling bytes to grow our buffer. Fail closed.
            if (_accumulated > _options.MaxFrameBytes)
            {
                _metrics.IncrementMalformedConnectionsDropped();
                _logger.LogWarning(
                    "Dropping connection {RemoteEndpoint}: {Buffered} unframed bytes exceed the {MaxFrameBytes}-byte frame ceiling.",
                    _remoteEndpoint, _accumulated, _options.MaxFrameBytes);
                return;
            }
        }
    }

    /// <summary>
    /// Reads from the socket, failing with <see cref="IdleTimeoutException"/> if the peer says
    /// nothing for <see cref="GatewayOptions.IdleTimeout"/>. Half-open sockets never deliver a
    /// FIN, so without this the connection slot leaks until the quota is exhausted.
    /// </summary>
    private async Task<int> ReadWithIdleTimeoutAsync(NetworkStream stream, byte[] buffer, CancellationToken stoppingToken)
    {
        using var idleCts = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);
        idleCts.CancelAfter(_options.IdleTimeout);

        try
        {
            return await stream.ReadAsync(buffer.AsMemory(), idleCts.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (idleCts.IsCancellationRequested && !stoppingToken.IsCancellationRequested)
        {
            throw new IdleTimeoutException();
        }
    }

    // ── Message handling ───────────────────────────────────────────────────────

    private async Task HandleMessageAsync(
        DecodedMessage message,
        NetworkStream stream,
        DateTime receivedAtUtc,
        CancellationToken cancellationToken)
    {
        if (message.MessageType == MessageType.Login)
        {
            await HandleLoginAsync(message, stream, receivedAtUtc, cancellationToken).ConfigureAwait(false);
            return;
        }

        // Every non-login frame is anonymous on the wire. If this connection never completed a
        // login that the registry recognised, there is NO identity to attribute it to — and we
        // do not invent one. Reject, unbound.
        if (_owner is not { } owner)
        {
            _logger.LogWarning(
                "Rejecting {MessageType} frame on unidentified session {RemoteEndpoint}: no successful login precedes it.",
                message.MessageType, _remoteEndpoint);

            // Count the decision BEFORE emitting it: a consumer that has already seen the
            // rejection must never observe a counter that has not caught up with it.
            _metrics.IncrementUnknownDeviceRejections();
            _metrics.IncrementFramesRejected();

            await PublishRejectionAsync(
                message, RejectionReasons.UnidentifiedSession, message.Identity, receivedAtUtc, cancellationToken)
                .ConfigureAwait(false);

            return;
        }

        // Position, alarm, heartbeat, and status observations are durably published (or parked in
        // the encrypted Postgres queue) before the protocol ACK is emitted. Heartbeats are real
        // lifecycle evidence even without a location; dropping them here leaves device health stale.
        // An ACK is a promise that the frame is owned, so it follows durable acceptance.
        if (message.MessageType is MessageType.Location or MessageType.Alarm or
            MessageType.Heartbeat or MessageType.Status)
            await PublishTelemetryAsync(message, owner, receivedAtUtc, cancellationToken).ConfigureAwait(false);

        // Observable frames reach here only after the publish pump confirmed an authoritative
        // write or durable store-forward enqueue.
        await SendAckIfRequiredAsync(message, stream, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Runs one observable frame from a bound device through the ingest pipeline: opens the OTel
    /// span chain, applies per-device replay/sequence defence, and — for a novel or merely
    /// out-of-order observation — queues the canonical event for publication. A byte-for-byte
    /// retry is counted and idempotently repair-processed with its stored event identity; an
    /// out-of-order observation is <b>flagged, not dropped</b>.
    /// </summary>
    private async Task PublishTelemetryAsync(
        DecodedMessage message,
        ResolvedDeviceOwner owner,
        DateTime receivedAtUtc,
        CancellationToken cancellationToken)
    {
        using PipelineTrace trace = PipelineTrace.StartPacketReceive(
            _correlationId, _remoteEndpoint, ProtocolLabel, imei: DeviceIdentifier.Mask(message.Identity?.Imei));
        trace.SetAdapter(Gt06Adapter.ProtocolName, Gt06Adapter.AdapterVersion);
        trace.ResolveOwnership(owner.TenantId, owner.CompanyId, owner.DeviceId, owner.VehicleId);

        CanonicalTelemetryEvent evt = _adapter.ToCanonicalEvent(
            message,
            owner,                    // ← the ONLY source of tenant/company/device/vehicle
            receivedAtUtc,
            _correlationId);

        // ── Replay / sequence defence, keyed on the REGISTRY-resolved device id ──────
        long serial = message.ProtocolMessageId ?? 0;
        string contentHash = HashFrame(message.RawFrame);

        ReplayDecision decision;
        bool duplicateRepair = false;
        using (Activity? validation = trace.StartValidation())
        {
            decision = await _replayGuard.CheckAsync(
                owner.DeviceId, serial, contentHash, evt.OccurredAtDeviceUtc, cancellationToken).ConfigureAwait(false);
            // The durable guard owns counter unwrapping and event identity. Immediate retries get
            // the stored UUID, while a legitimate later GT06 counter generation gets a new UUID.
            // Refuse a guard that cannot provide this acceptance-repair invariant.
            evt = evt with
            {
                EventId = decision.EventId
                    ?? throw new InvalidOperationException("Replay guard did not return a durable event identity.")
            };

            switch (decision.Outcome)
            {
                case ReplayOutcome.DuplicateReplay:
                    // The replay ledger may have been reserved by an attempt whose projection or
                    // canonical persistence failed before ACK. Re-run the idempotent acceptance
                    // pipeline with the replay ledger's stored EventId. A fully accepted duplicate becomes
                    // a no-op; a partial attempt is repaired before this retry is acknowledged.
                    duplicateRepair = true;
                    trace.MarkRejected(validation, "duplicate-replay");
                    TelematicsInstrumentation.Duplicates.Add(1, ReplayLabels(owner.CompanyId));
                    TelematicsInstrumentation.Replays.Add(1, ReplayLabels(owner.CompanyId));
                    _logger.LogInformation(
                        "Repair-checking replayed {MessageType} (serial {Serial}) for device {DeviceId} on {RemoteEndpoint}.",
                        message.MessageType, serial, owner.DeviceId, _remoteEndpoint);
                    break;

                case ReplayOutcome.OutOfOrder:
                    // Stale/reordered but not a recognised duplicate: keep the fix, but FLAG it so
                    // downstream trust-scoring and the monotonic projection can decide what to do.
                    trace.MarkRejected(validation, "out-of-order");
                    TelematicsInstrumentation.OutOfOrder.Add(1, ReplayLabels(owner.CompanyId));
                    evt = evt with { Quality = evt.Quality with { IsOutOfOrder = true } };
                    _logger.LogDebug(
                        "Flagging out-of-order {MessageType} (serial {Serial} behind {HighWater}) for device {DeviceId}.",
                        message.MessageType, serial, decision.LastSeenSerial, owner.DeviceId);
                    break;
            }
        }

        trace.SetEventId(evt.EventId);

        using (trace.StartPublish())
        {
            // Bounded write: this is where backpressure lands if the backbone is slow.
            var persisted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            await _publishChannel.Writer.WriteAsync(new PendingTelemetry(evt, persisted), cancellationToken)
                .ConfigureAwait(false);
            await persisted.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
        }

        // Count only the novel validation winner. A duplicate still traverses the pipeline so it
        // can repair a partial prior attempt, but idempotent storage and metrics must not double-count.
        if (!duplicateRepair)
            TelematicsInstrumentation.PacketsAccepted.Add(1, new TagList
            {
                { TelematicsInstrumentation.MetricLabels.Protocol, ProtocolLabel },
                { TelematicsInstrumentation.MetricLabels.Adapter, Gt06Adapter.ProtocolName },
                { TelematicsInstrumentation.MetricLabels.CompanyId, owner.CompanyId },
            });
    }

    private static TagList ReplayLabels(long companyId) => new()
    {
        { TelematicsInstrumentation.MetricLabels.Protocol, ProtocolLabel },
        { TelematicsInstrumentation.MetricLabels.CompanyId, companyId },
    };

    /// <summary>SHA-256 hex digest of the raw frame — the opaque content hash the replay guard dedups on.</summary>
    private static string HashFrame(IReadOnlyList<byte> rawFrame)
    {
        byte[] bytes = rawFrame as byte[] ?? rawFrame.ToArray();
        return Convert.ToHexString(SHA256.HashData(bytes));
    }

    private async Task HandleLoginAsync(
        DecodedMessage message,
        NetworkStream stream,
        DateTime receivedAtUtc,
        CancellationToken cancellationToken)
    {
        DeviceIdentityRef? claim = message.Identity;
        string masked = DeviceIdentifier.Mask(claim?.Imei);

        // ── One socket → one immutable device, for the socket's whole life. ──────────────
        // GT06 announces the IMEI once, at login; every later frame on this socket is anonymous and
        // is attributed to whatever this session is bound to. So if a bound session could be
        // re-pointed by a second login, an attacker who owns (or can reach) one admitted device
        // could log in as it, then log in again as a DIFFERENT tenant's vehicle, and every
        // subsequent anonymous fix would be published under that vehicle. The bind therefore
        // happens once and is never revisited.
        //
        // This is checked on the CLAIM, before any registry or authenticator work, so a mismatched
        // claim cannot even probe the registry from an already-bound socket.
        if (_owner is not null)
        {
            bool sameClaim =
                _boundImeiClaim is not null &&
                claim?.Imei is { Length: > 0 } repeated &&
                string.Equals(_boundImeiClaim, repeated, StringComparison.Ordinal);

            if (sameClaim)
            {
                // Idempotent re-login: the device did not see our first ACK (a common outcome when
                // a cell bearer stalls mid-handshake) and is re-introducing itself. The identity is
                // ALREADY correct and is left exactly as it is — no re-resolution, no re-auth, no
                // rebind. Answering keeps the device from wedging in a login retry loop.
                _logger.LogDebug(
                    "Re-login for the already-bound device {Imei} on {RemoteEndpoint}; identity unchanged, re-acknowledging.",
                    masked, _remoteEndpoint);

                await SendAckIfRequiredAsync(message, stream, cancellationToken).ConfigureAwait(false);
                return;
            }

            // A different device on a socket that already belongs to one. Not a protocol event.
            _metrics.IncrementSessionIdentityViolations();
            _metrics.IncrementUnknownDeviceRejections();
            _metrics.IncrementFramesRejected();
            TelematicsInstrumentation.AuthFailures.Add(1, new TagList
            {
                { TelematicsInstrumentation.MetricLabels.Protocol, ProtocolLabel },
                { TelematicsInstrumentation.MetricLabels.Reason, RejectionReasons.SessionIdentityChange },
            });

            _logger.LogWarning(
                "Refusing login from {RemoteEndpoint} claiming IMEI {Imei}: this socket is already bound to a " +
                "different device. One connection carries one identity for its lifetime; no acknowledgement is " +
                "sent, the binding is unchanged, and the connection is closing.",
                _remoteEndpoint, masked);

            await PublishRejectionAsync(
                    message, RejectionReasons.SessionIdentityChange, claim, receivedAtUtc, cancellationToken)
                .ConfigureAwait(false);

            _closeRequested = true;
            return;
        }

        ResolvedDeviceTrust? resolved = null;
        if (claim is { } identity && identity.HasAnyIdentifier)
        {
            try
            {
                resolved = await _registry.ResolveTrustAsync(identity, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                // A registry fault is treated as fail-closed: we would rather reject a real
                // device than bind a fix to a guessed tenant.
                _logger.LogError(ex, "Device registry faulted resolving {Imei}; failing closed.", masked);
                resolved = null;
            }
        }

        if (resolved is { } trust)
        {
            // ── Trust enforcement: identity resolved, now decide if it may ingest right now. ──
            // The registry says WHO the device is AND WHICH controls apply — its per-device trust
            // policy (auth mode, allowlist, source-IP/SIM pins, replay requirement). The authenticator
            // enforces THAT policy against this login. Because the policy is sourced from the registry
            // per device, a device provisioned with pins or PerDeviceHmac is actually held to them;
            // the GT06 seeded device is the honest ImeiAllowlistOnly baseline (spoofable → LowSpoofable).
            ResolvedDeviceOwner owner = trust.Owner;
            DeviceTrustPolicy policy = trust.Policy;
            var loginContext = new DeviceLoginContext(
                RemoteIp: _remoteIp,
                Imei: claim?.Imei,
                Serial: claim?.Serial);

            AuthResult auth = await _authenticator
                .AuthenticateAsync(owner, policy, loginContext, cancellationToken)
                .ConfigureAwait(false);

            if (auth.IsAuthenticated)
            {
                _owner = owner;
                _boundImeiClaim = claim?.Imei;

                _logger.LogInformation(
                    "Device {Imei} bound to tenant {TenantId} / company {CompanyId} as device {DeviceId} (vehicle {VehicleId}); trust tier {TrustTier}.",
                    masked, owner.TenantId, owner.CompanyId, owner.DeviceId, owner.VehicleId, auth.TrustTier);

                // ── One device → one authoritative socket. ───────────────────────────────
                // A roaming tracker whose bearer dropped without a FIN reconnects while the gateway
                // still holds its previous, half-open socket. The newest admitted login wins: it
                // becomes authoritative and the socket it displaced is cancelled, so the two never
                // publish under the same device concurrently.
                TakeDeviceSession(owner.DeviceId, masked);

                // A successful login is also the boundary at which a rebooted device may legitimately
                // restart its 16-bit frame counter from 1. Opening a replay epoch here lets the guard
                // accept that without forgetting a single durable row of replay history.
                await BeginReplayEpochAsync(owner.DeviceId, cancellationToken).ConfigureAwait(false);

                await SendAckIfRequiredAsync(message, stream, cancellationToken).ConfigureAwait(false);
                return;
            }

            // ── Authenticated identity but refused ingest (rejected/quarantined). ────
            // Do NOT bind, do NOT ack, and CLOSE the connection (Increment-1 fix). A quarantined
            // or otherwise barred device gets no service and cannot proceed to stream fixes.
            _logger.LogWarning(
                "Refusing login from {RemoteEndpoint} claiming IMEI {Imei}: {Outcome}/{Code} ({Detail}). No tenant is bound; closing.",
                _remoteEndpoint, masked, auth.Outcome, auth.Code, auth.Detail);

            _metrics.IncrementUnknownDeviceRejections();
            _metrics.IncrementFramesRejected();
            TelematicsInstrumentation.AuthFailures.Add(1, new TagList
            {
                { TelematicsInstrumentation.MetricLabels.Protocol, ProtocolLabel },
                { TelematicsInstrumentation.MetricLabels.Reason, auth.Code.ToString() },
            });

            await PublishRejectionAsync(
                    message, RejectionReasons.DeviceNotIngestable, claim, receivedAtUtc, cancellationToken)
                .ConfigureAwait(false);
            _closeRequested = true;
            return;
        }

        // ── Unknown device: the registry did not recognise the claim. Do NOT bind, do NOT ack. ──
        _logger.LogWarning(
            "Rejecting login from {RemoteEndpoint} claiming IMEI {Imei}: {Reason}. No tenant is bound; closing.",
            _remoteEndpoint, masked, RejectionReasons.UnknownDevice);

        // Count the decision BEFORE emitting it: a consumer that has already seen the rejection
        // must never observe a counter that has not caught up with it.
        _metrics.IncrementUnknownDeviceRejections();
        _metrics.IncrementFramesRejected();
        TelematicsInstrumentation.UnknownDevices.Add(1, new TagList
        {
            { TelematicsInstrumentation.MetricLabels.Protocol, ProtocolLabel },
        });

        await PublishRejectionAsync(message, RejectionReasons.UnknownDevice, claim, receivedAtUtc, cancellationToken)
            .ConfigureAwait(false);

        // Fail closed AND hang up: a rejected login must not leave the read loop draining a
        // session that can never be bound to an owner (Increment-1 finding).
        _closeRequested = true;
    }

    /// <summary>
    /// Claims the authoritative session for <paramref name="deviceId"/> and tears down whichever
    /// socket held it before.
    /// </summary>
    /// <remarks>
    /// The displaced session is cancelled, not deleted: it observes its own token, unwinds through
    /// its normal drain (publishing anything it had already decoded) and then calls
    /// <see cref="ActiveDeviceSessionRegistry.Release(string, long)"/> with ITS lease id, which no
    /// longer matches — so its cleanup cannot remove the entry this session just installed.
    /// </remarks>
    private void TakeDeviceSession(string deviceId, string maskedImei)
    {
        CancellationTokenSource? sessionCts = _sessionCts;
        if (sessionCts is null) return; // Not running under RunAsync (unit-constructed); nothing to lease.

        SessionAcquisition acquisition = _sessions.Acquire(deviceId, sessionCts);
        _leaseKey = deviceId;
        _leaseId = acquisition.LeaseId;

        if (acquisition.DisplacedSession is not { } displaced) return;

        _metrics.IncrementDuplicateSessionsDisplaced();
        _logger.LogWarning(
            "Device {Imei} authenticated on {RemoteEndpoint} while an earlier session for the same device was " +
            "still live; the earlier session is being closed. This connection is now the only authoritative one.",
            maskedImei, _remoteEndpoint);

        try
        {
            displaced.Cancel();
        }
        catch (ObjectDisposedException)
        {
            // The displaced connection finished on its own between the swap and this cancel.
            // Its lease is already stale, so there is nothing left to stop.
        }
    }

    /// <summary>
    /// Tells the replay guard that a fresh, authenticated session has begun for this device, so a
    /// counter that restarts at 1 after a power cycle reads as forward progress rather than as ten
    /// thousand stale frames.
    /// </summary>
    private async Task BeginReplayEpochAsync(string deviceId, CancellationToken cancellationToken)
    {
        try
        {
            await _replayGuard.BeginSessionEpochAsync(deviceId, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // Failing to open an epoch is not a reason to refuse a device: the guard simply stays on
            // the previous epoch, which is the STRICTER behaviour (a reset counter reads as
            // out-of-order). Never the weaker one.
            _logger.LogError(ex,
                "Could not open a replay epoch for device {DeviceId}; continuing on the previous epoch.", deviceId);
        }
    }

    private async Task SendAckIfRequiredAsync(DecodedMessage message, NetworkStream stream, CancellationToken cancellationToken)
    {
        if (!message.RequiresAck)
            return;

        byte[] ack = _adapter.EncodeAck(message);
        if (ack.Length == 0)
            return;

        // Written to THIS connection's own stream, which is the only stream this object ever holds.
        // There is no shared writer and no route by which one device's acknowledgement could be
        // emitted onto another device's socket.
        await stream.WriteAsync(ack, cancellationToken).ConfigureAwait(false);
        _metrics.IncrementAcksSent();

        _logger.LogDebug("Acked {MessageType} (serial {Serial}) on {RemoteEndpoint}.",
            message.MessageType, message.ProtocolMessageId, _remoteEndpoint);
    }

    private static EndPoint? SafeRemoteEndPoint(TcpClient client)
    {
        try
        {
            return client.Client.RemoteEndPoint;
        }
        catch (Exception)
        {
            return null;
        }
    }

    // ── Publishing ─────────────────────────────────────────────────────────────

    /// <summary>
    /// Drains the bounded channel and publishes to the backbone. A single reader per connection
    /// means a device's fixes are published in the order they were decoded — the per-key
    /// ordering guarantee the backbone relies on.
    /// </summary>
    private async Task PublishPumpAsync()
    {
        await foreach (PendingTelemetry pending in
            _publishChannel.Reader.ReadAllAsync(CancellationToken.None).ConfigureAwait(false))
        {
            CanonicalTelemetryEvent evt = pending.Event;
            // Idempotent live-map projection. Applied on the same single-reader pump so a device's
            // fixes fold into the snapshot in decode order; it is idempotent + monotonic, so a
            // store-and-forward replay of the SAME event after an outage is a safe no-op and can
            // never double-count or overwrite a fresher fix with a stale one.
            try
            {
                ProjectionResult projection = await _projectionStore
                    .ApplyAsync(evt, CancellationToken.None).ConfigureAwait(false);
                evt = projection.Event;
            }
            catch (Exception ex)
            {
                // History/latest/heartbeat/alerts are part of raw-ingest acceptance parity. Do not
                // publish or ACK a canonical-only event: the replay ledger's stable retry will reuse this
                // EventId and safely repair the projection before canonical persistence.
                pending.Persisted.TrySetException(ex);
                _logger.LogError(ex,
                    "Position projection failed for device {DeviceId}, event {EventId}; no ACK will be sent.",
                    evt.DeviceId, evt.EventId);
                continue;
            }

            // The production projector replaces cached login-time vehicle ownership with the
            // effective installation at device time. Build the canonical envelope only after that
            // resolution so history, durable events, and downstream consumers agree.
            string key = TelematicsEventKey.ForDevice(evt.TenantId, evt.CompanyId, evt.DeviceId);
            var envelope = EventEnvelope<CanonicalTelemetryEvent>.Create(
                tenantId: evt.TenantId,
                companyId: evt.CompanyId,
                payload: evt,
                correlationId: evt.CorrelationId,
                schemaVersion: evt.SchemaVersion,
                occurredAt: evt.OccurredAtDeviceUtc) with { EventId = evt.EventId };

            try
            {
                // Intentionally NOT passing the stopping token: these events are already decoded
                // and awaiting the persistence result that gates their ACK. Cancelling the publish
                // on shutdown could lose the only durable copy. The drain is time-bounded by the service.
                await _backbone
                    .PublishAsync(TelematicsTopics.TelemetryNormalized, key, envelope, CancellationToken.None)
                    .ConfigureAwait(false);

                _metrics.IncrementEventsPublished();
                pending.Persisted.TrySetResult();
            }
            catch (Exception ex)
            {
                // The backbone is down but the truck already moved. Park it, do not drop it. The
                // StoreAndForwardReplayService drains this FIFO in per-device order once the broker
                // recovers, and the idempotent projection above makes the eventual redelivery safe.
                _logger.LogError(ex,
                    "Backbone publish failed for device {DeviceId}; parking event {EventId} in store-and-forward.",
                    evt.DeviceId, evt.EventId);

                try
                {
                    await _forwardBuffer
                        .EnqueueAsync(new StoreAndForwardEntry(
                            TelematicsTopics.TelemetryNormalized, key, envelope, DateTimeOffset.UtcNow))
                        .ConfigureAwait(false);
                    _metrics.IncrementPublishFailuresBuffered();
                    pending.Persisted.TrySetResult();
                }
                catch (Exception bufferException)
                {
                    pending.Persisted.TrySetException(bufferException);
                    _logger.LogCritical(bufferException,
                        "Both canonical persistence and durable store-forward failed for event {EventId}; no ACK will be sent.",
                        evt.EventId);
                }
            }
        }
    }

    private readonly record struct PendingTelemetry(
        CanonicalTelemetryEvent Event,
        TaskCompletionSource Persisted);

    /// <summary>
    /// Publishes a rejection to <c>telemetry.rejected</c>. The envelope is deliberately
    /// <b>unbound</b> — tenant <see cref="Guid.Empty"/>, company <c>0</c> — because we do not
    /// know who owns this device and will not invent an owner to fill a required field.
    /// </summary>
    private async Task PublishRejectionAsync(
        DecodedMessage message,
        string reason,
        DeviceIdentityRef? claim,
        DateTime receivedAtUtc,
        CancellationToken cancellationToken)
    {
        string masked = DeviceIdentifier.Mask(claim?.Imei ?? claim?.Serial ?? claim?.DeviceId);

        TelematicsInstrumentation.PacketsRejected.Add(1, new TagList
        {
            { TelematicsInstrumentation.MetricLabels.Protocol, ProtocolLabel },
            { TelematicsInstrumentation.MetricLabels.Adapter, Gt06Adapter.ProtocolName },
            { TelematicsInstrumentation.MetricLabels.Reason, reason },
        });

        var rejection = new TelemetryRejection
        {
            Reason = reason,
            ClaimedIdentifierMasked = masked,
            ProtocolName = Gt06Adapter.ProtocolName,
            MessageType = message.MessageType.ToString(),
            ReceivedAtGatewayUtc = new DateTimeOffset(receivedAtUtc, TimeSpan.Zero),
            RawFrameBytes = message.RawFrame.Count,
            RemoteEndpoint = _remoteEndpoint,
        };

        var envelope = EventEnvelope<TelemetryRejection>.Create(
            tenantId: Guid.Empty,   // ← NOT tenant-bound. This is the point of the rejection.
            companyId: 0L,
            payload: rejection,
            correlationId: _correlationId,
            schemaVersion: 1,
            occurredAt: new DateTimeOffset(receivedAtUtc, TimeSpan.Zero));

        // Keyed on the masked claim so a probe campaign from one forged IMEI stays on one
        // partition and stays ordered — without the raw IMEI entering the key space.
        string key = TelematicsEventKey.ForDevice(Guid.Empty, 0L, masked);

        try
        {
            await _backbone
                .PublishAsync(TelematicsTopics.TelemetryRejected, key, envelope, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            // A failed rejection must never escalate into a connection fault.
            _logger.LogError(ex, "Failed to publish rejection ({Reason}) for {Imei}.", reason, masked);
        }
    }

    // ── Reassembly buffer ──────────────────────────────────────────────────────

    private void Append(ReadOnlySpan<byte> data)
    {
        int required = _accumulated + data.Length;
        if (required > _accumulator.Length)
        {
            int capacity = _accumulator.Length;
            while (capacity < required)
                capacity *= 2;

            Array.Resize(ref _accumulator, capacity);
        }

        data.CopyTo(_accumulator.AsSpan(_accumulated));
        _accumulated = required;
    }

    /// <summary>Drops the leading <paramref name="count"/> bytes the decoder consumed, retaining the partial-frame remainder.</summary>
    private void Consume(int count)
    {
        int remaining = _accumulated - count;
        if (remaining > 0)
            Array.Copy(_accumulator, count, _accumulator, 0, remaining);

        _accumulated = remaining;
    }

    /// <summary>Signals that the peer exceeded <see cref="GatewayOptions.IdleTimeout"/>, distinguishing it from host shutdown.</summary>
    private sealed class IdleTimeoutException : Exception
    {
    }
}
