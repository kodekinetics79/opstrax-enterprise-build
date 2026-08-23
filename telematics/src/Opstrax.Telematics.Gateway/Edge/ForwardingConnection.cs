using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using Microsoft.Extensions.Logging;
using Opstrax.Telematics.Contracts.Adapters;
using Opstrax.Telematics.Gateway.Forwarding;
using Opstrax.Telematics.Gateway.Security.Replay;

namespace Opstrax.Telematics.Gateway.Edge;

/// <summary>
/// Serves one accepted TCP connection in <see cref="EgressMode.Https"/>: reassembles frames,
/// gates the claimed IMEI against the allowlist, suppresses replays, normalizes each fix, and
/// forwards it to OpsTrax over HTTPS — parking it durably when OpsTrax is unreachable.
/// </summary>
/// <remarks>
/// <para>
/// <b>The edge is a translator, not an authority.</b> Nothing here resolves a tenant, a company,
/// a vehicle or a driver, and nothing here touches a database. OpsTrax resolves the IMEI against
/// <c>eld_devices</c>, enforces that this gateway's credential is scoped to that device's tenant,
/// and derives the installation and dispatch assignment. A public box that scanners can reach
/// holds no database credentials and asserts no ownership.
/// </para>
/// <para>
/// <b>The ACK is a durability promise, not a receipt.</b> A tracker drops a frame from its own
/// buffer once the server acknowledges it, so acknowledging something we might still lose
/// destroys the last copy. Every path below therefore acknowledges only after the fix is either
/// delivered to OpsTrax or durably parked — and stays silent when it is neither, so the device
/// retransmits.
/// </para>
/// <para>
/// <b>Isolation.</b> As with the canonical connection handler, every failure a hostile peer can
/// produce is contained in <see cref="RunAsync"/> and takes down nothing but this connection.
/// </para>
/// </remarks>
internal sealed class ForwardingConnection
{
    private readonly TcpClient _client;
    private readonly ProtocolRouter _router;
    private readonly ImeiAllowlist _allowlist;
    private readonly ITelemetryReplayGuard _replayGuard;
    private readonly IOpstraxForwarder _forwarder;
    private readonly IForwardOutbox _outbox;
    private readonly GatewayOptions _options;
    private readonly string? _edgeInstance;
    private readonly GatewayMetrics _metrics;
    private readonly EdgeMetrics _edgeMetrics;
    private readonly ILogger _logger;

    private readonly string _remoteEndpoint;

    /// <summary>Frame reassembly buffer: bytes that have not yet formed a complete frame.</summary>
    private byte[] _accumulator;
    private int _accumulated;

    /// <summary>The adapter that won arbitration for this stream. Null until the opening bytes identify one.</summary>
    private IProtocolAdapter? _adapter;

    /// <summary>The allowlisted IMEI this session is bound to. Null until a frame carries an admitted claim.</summary>
    private string? _imei;

    /// <summary>Set when a refused login should tear the connection down after the current batch.</summary>
    private bool _closeRequested;

    /// <summary>Creates a connection handler.</summary>
    public ForwardingConnection(
        TcpClient client,
        ProtocolRouter router,
        ImeiAllowlist allowlist,
        ITelemetryReplayGuard replayGuard,
        IOpstraxForwarder forwarder,
        IForwardOutbox outbox,
        GatewayOptions options,
        string? edgeInstance,
        GatewayMetrics metrics,
        EdgeMetrics edgeMetrics,
        ILogger logger)
    {
        _client = client ?? throw new ArgumentNullException(nameof(client));
        _router = router ?? throw new ArgumentNullException(nameof(router));
        _allowlist = allowlist ?? throw new ArgumentNullException(nameof(allowlist));
        _replayGuard = replayGuard ?? throw new ArgumentNullException(nameof(replayGuard));
        _forwarder = forwarder ?? throw new ArgumentNullException(nameof(forwarder));
        _outbox = outbox ?? throw new ArgumentNullException(nameof(outbox));
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _edgeInstance = edgeInstance;
        _metrics = metrics ?? throw new ArgumentNullException(nameof(metrics));
        _edgeMetrics = edgeMetrics ?? throw new ArgumentNullException(nameof(edgeMetrics));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));

        _remoteEndpoint = MaskRemoteEndpoint(SafeRemoteEndPoint(client));
        _accumulator = new byte[Math.Max(options.ReadBufferBytes, 512)];
    }

    /// <summary>
    /// Runs the connection to completion. Returns normally on every expected termination and never
    /// propagates a connection-scoped fault to the accept loop.
    /// </summary>
    public async Task RunAsync(CancellationToken stoppingToken)
    {
        _logger.LogDebug("Edge connection accepted from {RemoteEndpoint}.", _remoteEndpoint);

        try
        {
            using (_client)
            {
                await ReadLoopAsync(_client.GetStream(), stoppingToken).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException)
        {
            _logger.LogDebug("Edge connection {RemoteEndpoint} cancelled by host shutdown.", _remoteEndpoint);
        }
        catch (Exception ex) when (ex is IOException or SocketException)
        {
            // Cellular peers vanish mid-write constantly. Routine, not exceptional.
            _logger.LogDebug(ex, "Edge connection {RemoteEndpoint} dropped by peer.", _remoteEndpoint);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "Unhandled fault on edge connection {RemoteEndpoint}; closing it. Host and other connections are unaffected.",
                _remoteEndpoint);
        }
        finally
        {
            _logger.LogDebug("Edge connection {RemoteEndpoint} closed.", _remoteEndpoint);
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
                    "Closing edge connection {RemoteEndpoint}: idle for more than {IdleTimeout}.",
                    _remoteEndpoint, _options.IdleTimeout);
                return;
            }
            catch (OperationCanceledException)
            {
                return;
            }
            catch (IOException ex)
            {
                _logger.LogDebug(ex, "Edge connection {RemoteEndpoint} reset by peer.", _remoteEndpoint);
                return;
            }

            if (read == 0)
            {
                _logger.LogDebug("Edge connection {RemoteEndpoint} closed by peer.", _remoteEndpoint);
                return;
            }

            Append(readBuffer.AsSpan(0, read));
            DateTime receivedAtUtc = DateTime.UtcNow;

            // ── Which protocol is this? ─────────────────────────────────────────
            if (_adapter is null)
            {
                ProtocolSelection selection = _router.Select(_accumulator.AsSpan(0, _accumulated));

                if (selection.NeedMoreData)
                {
                    // Only keep buffering while the undecided adapters could still be satisfied by
                    // more bytes; past the frame ceiling this is a peer dribbling, handled below.
                    if (_accumulated <= _options.MaxFrameBytes) continue;

                    _edgeMetrics.IncrementUnidentifiedProtocolConnections();
                    _logger.LogWarning(
                        "Dropping edge connection {RemoteEndpoint}: {Buffered} bytes and still no protocol match.",
                        _remoteEndpoint, _accumulated);
                    return;
                }

                if (!selection.IsMatch)
                {
                    _edgeMetrics.IncrementUnidentifiedProtocolConnections();
                    _metrics.IncrementMalformedConnectionsDropped();
                    _logger.LogWarning(
                        "Dropping edge connection {RemoteEndpoint}: opening bytes match no installed adapter ({Adapters}).",
                        _remoteEndpoint, _router.Describe());
                    return;
                }

                _adapter = selection.Adapter;
                _logger.LogDebug(
                    "Edge connection {RemoteEndpoint} identified as {Protocol} (confidence {Confidence}).",
                    _remoteEndpoint, _adapter!.Metadata.Name, selection.Confidence);
            }

            // ── Decode every complete frame currently buffered ──────────────────
            IReadOnlyList<DecodedMessage> messages;
            int consumed;
            try
            {
                messages = _adapter!.Decode(_accumulator.AsSpan(0, _accumulated), out consumed);
            }
            catch (ProtocolException ex)
            {
                // Malformed beyond recovery. Fail closed: drop this connection only, and never
                // fabricate a fix from corrupt bytes.
                _metrics.IncrementMalformedConnectionsDropped();
                _logger.LogWarning(ex,
                    "Dropping edge connection {RemoteEndpoint}: malformed {Protocol} framing at offset {Offset}.",
                    _remoteEndpoint, ex.AdapterName ?? _adapter!.Metadata.Name, ex.Offset);
                return;
            }

            if (consumed > 0) Consume(consumed);

            foreach (DecodedMessage message in messages)
            {
                _metrics.IncrementFramesDecoded();
                await HandleMessageAsync(message, stream, receivedAtUtc, stoppingToken).ConfigureAwait(false);

                if (_closeRequested) break;
            }

            if (_closeRequested)
            {
                _logger.LogDebug("Closing edge connection {RemoteEndpoint} after a refused device.", _remoteEndpoint);
                return;
            }

            // Residue guard: whatever remains is an incomplete frame by definition. If that alone
            // exceeds the frame ceiling, no valid frame can ever complete it — a peer is dribbling
            // bytes to grow our buffer.
            if (_accumulated > _options.MaxFrameBytes)
            {
                _metrics.IncrementMalformedConnectionsDropped();
                _logger.LogWarning(
                    "Dropping edge connection {RemoteEndpoint}: {Buffered} unframed bytes exceed the {MaxFrameBytes}-byte ceiling.",
                    _remoteEndpoint, _accumulated, _options.MaxFrameBytes);
                return;
            }
        }
    }

    /// <summary>Reads, failing with <see cref="IdleTimeoutException"/> when the peer goes silent past the idle bound.</summary>
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
        // Any frame may carry the identity claim, not only a login: GT06 sends the IMEI once at
        // login, but other protocols repeat it per frame. Gating wherever the claim appears keeps
        // the allowlist authoritative regardless of which shape the protocol uses.
        if (message.Identity?.Imei is { Length: > 0 } claimed && !BindSession(claimed))
            return;

        if (message.MessageType == MessageType.Login)
        {
            if (_imei is null)
            {
                // A login that carried no identifier at all cannot be admitted by an allowlist.
                _edgeMetrics.IncrementAllowlistRefusals();
                _metrics.IncrementUnknownDeviceRejections();
                _logger.LogWarning(
                    "Refusing login from {RemoteEndpoint}: the frame carried no device identifier.", _remoteEndpoint);
                _closeRequested = true;
                return;
            }

            // The login is acknowledged only once the IMEI is admitted, so an unlisted device
            // never receives the protocol-level "you are registered" answer.
            await SendAckIfRequiredAsync(message, stream, cancellationToken).ConfigureAwait(false);
            return;
        }

        if (_imei is not { } imei)
        {
            _edgeMetrics.IncrementAllowlistRefusals();
            _metrics.IncrementUnknownDeviceRejections();
            _logger.LogWarning(
                "Rejecting {MessageType} frame on unbound edge session {RemoteEndpoint}: no admitted identity precedes it.",
                message.MessageType, _remoteEndpoint);
            _closeRequested = true;
            return;
        }

        // A positionless keepalive has nowhere to go: the trusted-gateway ingest contract requires
        // a coordinate. It is acknowledged so the device does not retransmit, and counted so the
        // gap is visible rather than looking like delivery.
        if (message.MessageType is MessageType.Heartbeat or MessageType.Ack)
        {
            _edgeMetrics.IncrementHeartbeatsNotForwarded();
            await SendAckIfRequiredAsync(message, stream, cancellationToken).ConfigureAwait(false);
            return;
        }

        await ForwardAsync(message, imei, stream, receivedAtUtc, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Applies the allowlist to a claimed IMEI and binds the session on success.
    /// </summary>
    /// <returns><see langword="false"/> when the device was refused and the connection is closing.</returns>
    private bool BindSession(string claimedImei)
    {
        if (_allowlist.IsAllowed(claimedImei))
        {
            if (_imei is null)
                _logger.LogInformation(
                    "Edge session {RemoteEndpoint} bound to allowlisted device {Imei} via {Protocol}. " +
                    "Ownership is resolved by OpsTrax, not here.",
                    _remoteEndpoint, DeviceIdentifier.Mask(claimedImei), _adapter?.Metadata.Name);

            _imei = claimedImei;
            return true;
        }

        _edgeMetrics.IncrementAllowlistRefusals();
        _metrics.IncrementUnknownDeviceRejections();

        _logger.LogWarning(
            "Refusing device {Imei} from {RemoteEndpoint}: not on the IMEI allowlist ({Admitted} admitted{Faulted}). " +
            "No acknowledgement is sent and the connection is closing.",
            DeviceIdentifier.Mask(claimedImei), _remoteEndpoint, _allowlist.Count,
            _allowlist.IsFileFaulted ? ", allowlist file UNREADABLE" : string.Empty);

        _closeRequested = true;
        return false;
    }

    /// <summary>
    /// Normalizes, deduplicates and delivers one observation, then decides whether the frame may
    /// be acknowledged.
    /// </summary>
    private async Task ForwardAsync(
        DecodedMessage message,
        string imei,
        NetworkStream stream,
        DateTime receivedAtUtc,
        CancellationToken cancellationToken)
    {
        string protocol = _adapter!.Metadata.Name;

        NormalizationResult normalized = ObservationNormalizer.Normalize(
            message, imei, VendorFor(protocol), protocol, _edgeInstance, receivedAtUtc);

        if (normalized.DroppedFields.Count > 0)
        {
            _edgeMetrics.AddImplausibleFieldsDropped(normalized.DroppedFields.Count);
            _logger.LogWarning(
                "Dropped out-of-range reading(s) {Fields} from a {Protocol} fix for {Imei}; the position was still forwarded. " +
                "Repeated occurrences indicate a decoder/field-layout mismatch.",
                string.Join(", ", normalized.DroppedFields), protocol, DeviceIdentifier.Mask(imei));
        }

        if (normalized.Observation is not { } observation)
        {
            // A frame with no coordinate is normal traffic (status report), not a fault; the rest
            // are faults worth naming.
            if (normalized.Rejection == NormalizationRejection.NoLocation)
            {
                _edgeMetrics.IncrementHeartbeatsNotForwarded();
            }
            else
            {
                _edgeMetrics.IncrementNormalizationRejections();
                _logger.LogWarning(
                    "Discarding a {Protocol} frame from {Imei}: {Reason}.",
                    protocol, DeviceIdentifier.Mask(imei), normalized.Rejection);
            }

            // Acknowledged either way: retransmitting cannot make an unusable frame usable, and
            // withholding the ACK would wedge the device retrying it forever.
            await SendAckIfRequiredAsync(message, stream, cancellationToken).ConfigureAwait(false);
            return;
        }

        // ── Replay defence, before anything is sent ─────────────────────────────
        ReplayDecision decision = await _replayGuard.CheckAsync(
            imei,
            message.ProtocolMessageId ?? 0,
            HashFrame(message.RawFrame),
            observation.FixTimeUtc,
            cancellationToken).ConfigureAwait(false);

        if (decision.Outcome == ReplayOutcome.DuplicateReplay)
        {
            // Already accepted once, so it is already delivered or already parked — either way its
            // delivery is guaranteed and re-sending would only earn a 409. ACK so the device stops
            // retransmitting. OpsTrax's own durable ledger remains the authority across restarts.
            _edgeMetrics.IncrementReplayDuplicatesDropped();
            _logger.LogDebug(
                "Suppressed a replayed {Protocol} frame (serial {Serial}) from {Imei}.",
                protocol, message.ProtocolMessageId, DeviceIdentifier.Mask(imei));

            await SendAckIfRequiredAsync(message, stream, cancellationToken).ConfigureAwait(false);
            return;
        }

        if (decision.Outcome == ReplayOutcome.OutOfOrder)
            // Delivered anyway: OpsTrax's projection is monotonic in device fix time, so a delayed
            // frame cannot overwrite a fresher one, and discarding it here would lose real history.
            _logger.LogDebug(
                "Forwarding an out-of-order {Protocol} frame (serial {Serial} behind {HighWater}) from {Imei}.",
                protocol, message.ProtocolMessageId, decision.LastSeenSerial, DeviceIdentifier.Mask(imei));

        // ── Deliver, or make durable ────────────────────────────────────────────
        string payload = observation.ToJson();
        ForwardResult result = await _forwarder.SendAsync(payload, cancellationToken).ConfigureAwait(false);

        switch (result.Outcome)
        {
            case ForwardOutcome.Delivered:
                _edgeMetrics.IncrementObservationsDelivered();
                break;

            case ForwardOutcome.Rejected:
                // OpsTrax understood it and refused. Parking it would retry forever; it is counted
                // and logged by the forwarder, which knows why.
                _edgeMetrics.IncrementObservationsRejected();
                break;

            default:
                if (!await _outbox.EnqueueAsync(payload, cancellationToken).ConfigureAwait(false))
                {
                    // Neither delivered nor durable. Do NOT acknowledge: the device's own buffer is
                    // now the only surviving copy, and it will retransmit.
                    _logger.LogCritical(
                        "A fix from {Imei} could be neither delivered nor parked; withholding the acknowledgement " +
                        "so the device retransmits it.", DeviceIdentifier.Mask(imei));
                    return;
                }

                _edgeMetrics.IncrementObservationsParked();
                _logger.LogDebug(
                    "Parked a fix from {Imei} for later delivery ({Detail}); {Parked} now queued.",
                    DeviceIdentifier.Mask(imei), result.Detail, _outbox.Count);
                break;
        }

        await SendAckIfRequiredAsync(message, stream, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Maps a decoded protocol to its hardware vendor, when the bytes actually identify one.</summary>
    /// <remarks>
    /// GT06 is spoken by many OEMs, so no manufacturer is derivable from a GT06 stream and none is
    /// claimed — an invented <c>provider</c> would show up in the Fix Provenance drawer as fact.
    /// </remarks>
    private static string? VendorFor(string protocolName) =>
        protocolName.Equals("PacificTrack", StringComparison.OrdinalIgnoreCase) ? "Pacific Track" : null;

    private async Task SendAckIfRequiredAsync(DecodedMessage message, NetworkStream stream, CancellationToken cancellationToken)
    {
        if (!message.RequiresAck) return;

        byte[] ack = _adapter!.EncodeAck(message);
        if (ack.Length == 0) return;

        await stream.WriteAsync(ack, cancellationToken).ConfigureAwait(false);
        _logger.LogDebug("Acked {MessageType} (serial {Serial}) on {RemoteEndpoint}.",
            message.MessageType, message.ProtocolMessageId, _remoteEndpoint);
    }

    /// <summary>SHA-256 hex digest of the raw frame — the opaque content hash the replay guard dedups on.</summary>
    private static string HashFrame(IReadOnlyList<byte> rawFrame)
    {
        byte[] bytes = rawFrame as byte[] ?? rawFrame.ToArray();
        return Convert.ToHexString(SHA256.HashData(bytes));
    }

    // ── Reassembly buffer ──────────────────────────────────────────────────────

    private void Append(ReadOnlySpan<byte> data)
    {
        int required = _accumulated + data.Length;
        if (required > _accumulator.Length)
        {
            int capacity = _accumulator.Length;
            while (capacity < required) capacity *= 2;
            Array.Resize(ref _accumulator, capacity);
        }

        data.CopyTo(_accumulator.AsSpan(_accumulated));
        _accumulated = required;
    }

    private void Consume(int count)
    {
        int remaining = _accumulated - count;
        if (remaining > 0) Array.Copy(_accumulator, count, _accumulator, 0, remaining);
        _accumulated = remaining;
    }

    private static EndPoint? SafeRemoteEndPoint(TcpClient client)
    {
        try { return client.Client.RemoteEndPoint; }
        catch (Exception) { return null; }
    }

    /// <summary>Truncates a peer address to a /24 (IPv4) or /48 (IPv6) and never retains the source port.</summary>
    private static string MaskRemoteEndpoint(EndPoint? endpoint)
    {
        if (endpoint is not IPEndPoint ipEndpoint) return "unknown";

        byte[] bytes = ipEndpoint.Address.GetAddressBytes();
        int retained = bytes.Length == 4 ? 3 : 6;
        Array.Clear(bytes, retained, bytes.Length - retained);
        return $"{new IPAddress(bytes)}/{(bytes.Length == 4 ? 24 : 48)}";
    }

    /// <summary>Distinguishes an idle peer from host shutdown.</summary>
    private sealed class IdleTimeoutException : Exception
    {
    }
}
