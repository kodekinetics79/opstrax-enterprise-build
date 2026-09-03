namespace Opstrax.Telematics.Protocols.J1939;

/// <summary>
/// Bounded reassembly for SAE J1939-21 Transport Protocol connection-management
/// (TP.CM, PGN 60416) and data-transfer (TP.DT, PGN 60160) frames.
///
/// This type intentionally starts *after* CAN-ID parsing: callers must preserve the
/// source and destination addresses from the 29-bit CAN identifier and provide one
/// classic eight-byte TP frame at a time. It supports BAM and passive RTS/CTS capture;
/// it does not transmit CTS/EOM_ACK frames or claim a physical CAN acquisition path.
/// </summary>
public sealed class J1939TransportReassembler
{
    public const int TpCmPgn = 60416; // 0x00EC00
    public const int TpDtPgn = 60160; // 0x00EB00
    public const byte GlobalAddress = 0xFF;

    public const byte RtsControl = 0x10;
    public const byte CtsControl = 0x11;
    public const byte EndOfMessageAckControl = 0x13;
    public const byte BamControl = 0x20;
    public const byte AbortControl = 0xFF;

    public const int MinPayloadBytes = 9;
    public const int MaxPayloadBytes = J1939DiagnosticDecoder.MaximumPayloadBytes;
    public const int MaxPacketCount = 255;

    private readonly TimeSpan _sessionTimeout;
    private readonly Dictionary<SessionKey, Session> _sessions = new();

    public J1939TransportReassembler(TimeSpan? sessionTimeout = null)
    {
        _sessionTimeout = sessionTimeout ?? TimeSpan.FromSeconds(30);
        if (_sessionTimeout <= TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(sessionTimeout), "Session timeout must be positive.");
    }

    /// <summary>
    /// Accepts one TP.CM or TP.DT frame. Returns a completed message only when the
    /// advertised payload has been reconstructed exactly; otherwise returns null.
    /// Malformed, oversized, duplicate-start, out-of-order, or orphan transfer data
    /// fail closed with <see cref="J1939TransportException"/>.
    /// </summary>
    public J1939ReassembledMessage? Accept(J1939TransportFrame frame)
    {
        if (frame.Pgn is not (TpCmPgn or TpDtPgn))
            throw new ArgumentOutOfRangeException(nameof(frame), "Only J1939 TP.CM and TP.DT frames are accepted.");
        if (frame.Data.Length != 8)
            throw new J1939TransportException("A J1939 transport frame must contain exactly eight data bytes.");

        ExpireSessions(frame.ReceivedAt);
        return frame.Pgn == TpCmPgn ? AcceptConnectionManagement(frame) : AcceptDataTransfer(frame);
    }

    public int ActiveSessionCount => _sessions.Count;

    private J1939ReassembledMessage? AcceptConnectionManagement(J1939TransportFrame frame)
    {
        var data = frame.Data.Span;
        var control = data[0];

        if (control == AbortControl)
        {
            var abortedPgn = DecodePgn(data);
            RemoveMatchingSession(frame.SourceAddress, frame.DestinationAddress, abortedPgn);
            RemoveMatchingSession(frame.DestinationAddress, frame.SourceAddress, abortedPgn);
            return null;
        }

        // CTS and EOM_ACK are receiver-side flow-control evidence. A passive capture
        // does not need them to reconstruct the sender's subsequent TP.DT frames.
        if (control is CtsControl or EndOfMessageAckControl)
            return null;

        if (control is not (BamControl or RtsControl))
            throw new J1939TransportException($"Unsupported TP.CM control byte 0x{control:X2}.");

        if (control == BamControl && frame.DestinationAddress != GlobalAddress)
            throw new J1939TransportException("BAM must use the global destination address 0xFF.");
        if (control == RtsControl && frame.DestinationAddress == GlobalAddress)
            throw new J1939TransportException("RTS must identify a specific destination address.");

        var payloadBytes = data[1] | (data[2] << 8);
        var packetCount = data[3];
        var targetPgn = DecodePgn(data);

        if (payloadBytes is < MinPayloadBytes or > MaxPayloadBytes)
            throw new J1939TransportException($"Advertised payload length {payloadBytes} is outside {MinPayloadBytes}..{MaxPayloadBytes} bytes.");
        if (packetCount < 2)
            throw new J1939TransportException("A transport-protocol message must require at least two TP.DT packets.");

        var expectedPackets = (payloadBytes + 6) / 7;
        if (packetCount != expectedPackets)
            throw new J1939TransportException(
                $"Advertised packet count {packetCount} does not match payload length {payloadBytes} (expected {expectedPackets}).");

        // Byte 4 is reserved (BAM) or max-packets-per-CTS (RTS). For RTS, zero is
        // nonsensical because it would permit no progress. Do not over-constrain BAM
        // reserved values: physical vendors occasionally deviate while remaining
        // otherwise parseable, and certification records the raw behavior separately.
        if (control == RtsControl && data[4] == 0)
            throw new J1939TransportException("RTS max-packets-per-CTS cannot be zero.");

        var key = new SessionKey(frame.SourceAddress, frame.DestinationAddress);
        if (_sessions.ContainsKey(key))
            throw new J1939TransportException("A transport session is already active for this source/destination pair.");

        _sessions.Add(key, new Session(
            targetPgn,
            payloadBytes,
            packetCount,
            control == BamControl,
            frame.ReceivedAt));
        return null;
    }

    private J1939ReassembledMessage? AcceptDataTransfer(J1939TransportFrame frame)
    {
        var key = new SessionKey(frame.SourceAddress, frame.DestinationAddress);
        if (!_sessions.TryGetValue(key, out var session))
            throw new J1939TransportException("TP.DT frame has no active transport session for this source/destination pair.");

        var data = frame.Data.Span;
        var sequence = data[0];
        if (sequence != session.NextSequence)
        {
            _sessions.Remove(key);
            throw new J1939TransportException(
                $"Unexpected TP.DT sequence {sequence}; expected {session.NextSequence}. Session discarded.");
        }

        var remaining = session.PayloadBytes - session.BytesWritten;
        var copyCount = Math.Min(7, remaining);
        data.Slice(1, copyCount).CopyTo(session.Buffer.AsSpan(session.BytesWritten, copyCount));
        session.BytesWritten += copyCount;
        session.NextSequence++;
        session.LastFrameAt = frame.ReceivedAt;

        if (sequence < session.PacketCount)
            return null;

        if (sequence != session.PacketCount || session.BytesWritten != session.PayloadBytes)
        {
            _sessions.Remove(key);
            throw new J1939TransportException("Transport session ended without exactly matching the advertised payload length.");
        }

        _sessions.Remove(key);
        return new J1939ReassembledMessage(
            session.TargetPgn,
            frame.SourceAddress,
            frame.DestinationAddress,
            session.Buffer,
            session.IsBroadcast,
            session.FirstFrameAt,
            frame.ReceivedAt);
    }

    private void ExpireSessions(DateTimeOffset now)
    {
        if (_sessions.Count == 0) return;
        foreach (var key in _sessions
                     .Where(pair => now >= pair.Value.LastFrameAt && now - pair.Value.LastFrameAt > _sessionTimeout)
                     .Select(pair => pair.Key)
                     .ToArray())
        {
            _sessions.Remove(key);
        }
    }

    private void RemoveMatchingSession(byte source, byte destination, int targetPgn)
    {
        var key = new SessionKey(source, destination);
        if (_sessions.TryGetValue(key, out var session) && session.TargetPgn == targetPgn)
            _sessions.Remove(key);
    }

    private static int DecodePgn(ReadOnlySpan<byte> data)
        => data[5] | (data[6] << 8) | (data[7] << 16);

    private readonly record struct SessionKey(byte SourceAddress, byte DestinationAddress);

    private sealed class Session(
        int targetPgn,
        int payloadBytes,
        byte packetCount,
        bool isBroadcast,
        DateTimeOffset firstFrameAt)
    {
        public int TargetPgn { get; } = targetPgn;
        public int PayloadBytes { get; } = payloadBytes;
        public byte PacketCount { get; } = packetCount;
        public bool IsBroadcast { get; } = isBroadcast;
        public DateTimeOffset FirstFrameAt { get; } = firstFrameAt;
        public DateTimeOffset LastFrameAt { get; set; } = firstFrameAt;
        public byte NextSequence { get; set; } = 1;
        public int BytesWritten { get; set; }
        public byte[] Buffer { get; } = new byte[payloadBytes];
    }
}

public sealed record J1939TransportFrame(
    int Pgn,
    byte SourceAddress,
    byte DestinationAddress,
    ReadOnlyMemory<byte> Data,
    DateTimeOffset ReceivedAt);

public sealed record J1939ReassembledMessage(
    int Pgn,
    byte SourceAddress,
    byte DestinationAddress,
    byte[] Payload,
    bool IsBroadcast,
    DateTimeOffset FirstFrameAt,
    DateTimeOffset CompletedAt);

public sealed class J1939TransportException(string message) : Exception(message);