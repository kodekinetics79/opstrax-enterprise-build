namespace Opstrax.Telematics.Protocols.J1939;

/// <summary>
/// Parses classic CAN frames at the explicit 29-bit J1939 acquisition boundary and
/// joins TP.CM/TP.DT traffic through <see cref="J1939TransportReassembler"/>.
/// It preserves capture provenance but does not claim that a source is physical,
/// calibrated, trusted, or compatible; those are external evidence decisions.
/// </summary>
public sealed class J1939CanAcquisition
{
    public const int MaxActiveTransportPaths = 64;

    private readonly TimeSpan _transportSessionTimeout;
    private readonly object _transportGate = new();
    private readonly Dictionary<AcquisitionPath, TransportPathState> _transportByPath = new();

    public J1939CanAcquisition(TimeSpan? transportSessionTimeout = null)
    {
        // Validate the caller's override even before the first transport frame arrives.
        _transportSessionTimeout = transportSessionTimeout ?? TimeSpan.FromSeconds(30);
        _ = new J1939TransportReassembler(_transportSessionTimeout);
    }

    public int ActiveTransportSessionCount
    {
        get
        {
            lock (_transportGate)
                return _transportByPath.Values.Sum(value => value.Reassembler.ActiveSessionCount);
        }
    }

    /// <summary>
    /// Accepts one captured classic CAN frame. A direct frame produces a message
    /// immediately. TP.CM/TP.DT frames produce a message only after exact, bounded
    /// transport reassembly.
    /// </summary>
    public J1939AcquiredMessage? Accept(J1939RawCanFrame rawFrame)
    {
        var frame = J1939CanIdentifier.Parse(rawFrame);

        if (frame.Pgn is J1939TransportReassembler.TpCmPgn or J1939TransportReassembler.TpDtPgn)
        {
            lock (_transportGate)
            {
                var path = new AcquisitionPath(frame.AdapterType, frame.Channel);
                RemoveInactivePaths(frame.CapturedAt);
                if (!_transportByPath.TryGetValue(path, out var state))
                {
                    if (_transportByPath.Count >= MaxActiveTransportPaths)
                        throw new J1939AcquisitionException(
                            $"The acquisition boundary already has {MaxActiveTransportPaths} active adapter/channel transport paths.");
                    state = new TransportPathState(
                        new J1939TransportReassembler(_transportSessionTimeout),
                        frame.CapturedAt);
                    _transportByPath.Add(path, state);
                }
                else if (frame.CapturedAt < state.LastFrameAt)
                {
                    throw new J1939AcquisitionException(
                        "Capture time regressed on the adapter/channel path; the frame was not admitted.");
                }

                J1939ReassembledMessage? completed;
                try
                {
                    completed = state.Reassembler.Accept(new J1939TransportFrame(
                        frame.Pgn,
                        frame.SourceAddress,
                        frame.DestinationAddress,
                        frame.Data,
                        frame.CapturedAt)
                    {
                        Acquisition = frame,
                    });
                    if (frame.CapturedAt > state.LastFrameAt)
                        state.LastFrameAt = frame.CapturedAt;
                }
                finally
                {
                    if (state.Reassembler.ActiveSessionCount == 0)
                        _transportByPath.Remove(path);
                }

                return completed is null
                    ? null
                    : new J1939AcquiredMessage(
                        completed.Pgn,
                        completed.SourceAddress,
                        completed.DestinationAddress,
                        completed.Payload.ToArray(),
                        IsTransported: true,
                        MessagePriority: null,
                        completed.FirstFrameAt,
                        completed.CompletedAt,
                        completed.Frames);
            }
        }

        return new J1939AcquiredMessage(
            frame.Pgn,
            frame.SourceAddress,
            frame.DestinationAddress,
            frame.Data.ToArray(),
            IsTransported: false,
            frame.Priority,
            frame.CapturedAt,
            frame.CapturedAt,
            Array.AsReadOnly([frame]));
    }

    private void RemoveInactivePaths(DateTimeOffset now)
    {
        foreach (var path in _transportByPath
                     .Where(pair => now >= pair.Value.LastFrameAt &&
                                    now - pair.Value.LastFrameAt > _transportSessionTimeout)
                     .Select(pair => pair.Key)
                     .ToArray())
        {
            _transportByPath.Remove(path);
        }
    }

    private readonly record struct AcquisitionPath(string AdapterType, string Channel);

    private sealed class TransportPathState(J1939TransportReassembler reassembler, DateTimeOffset lastFrameAt)
    {
        public J1939TransportReassembler Reassembler { get; } = reassembler;
        public DateTimeOffset LastFrameAt { get; set; } = lastFrameAt;
    }
}

/// <summary>
/// Strict parser for the 29-bit identifier used by classic SAE J1939 CAN frames.
/// </summary>
public static class J1939CanIdentifier
{
    public const uint MaximumExtendedIdentifier = 0x1FFFFFFF;
    public const byte GlobalAddress = 0xFF;
    private const int MaxAdapterOrChannelLength = 128;
    private const int MaxCaptureReferenceLength = 256;

    public static J1939CanFrameEnvelope Parse(J1939RawCanFrame rawFrame)
    {
        ArgumentNullException.ThrowIfNull(rawFrame);

        if (!rawFrame.IsExtendedIdentifier)
            throw new J1939AcquisitionException("A J1939 frame must use a 29-bit extended CAN identifier.");
        if (rawFrame.Identifier > MaximumExtendedIdentifier)
            throw new J1939AcquisitionException("The CAN identifier exceeds the 29-bit extended identifier range.");
        if (rawFrame.IsRemoteTransmissionRequest)
            throw new J1939AcquisitionException("Remote-transmission-request frames do not carry J1939 evidence.");
        if (rawFrame.IsErrorFrame)
            throw new J1939AcquisitionException("CAN error frames cannot be interpreted as J1939 messages.");
        if (rawFrame.Data.Length > 8)
            throw new J1939AcquisitionException("This acquisition boundary accepts classic CAN payloads of at most eight bytes.");
        if (rawFrame.CapturedAt == default)
            throw new J1939AcquisitionException("A capture timestamp is required.");

        ValidateEvidenceLabel(rawFrame.AdapterType, nameof(rawFrame.AdapterType), MaxAdapterOrChannelLength);
        ValidateEvidenceLabel(rawFrame.Channel, nameof(rawFrame.Channel), MaxAdapterOrChannelLength);
        ValidateEvidenceLabel(rawFrame.CaptureReference, nameof(rawFrame.CaptureReference), MaxCaptureReferenceLength);

        var identifier = rawFrame.Identifier;
        var priority = (byte)((identifier >> 26) & 0x07);
        var pduFormat = (byte)((identifier >> 16) & 0xFF);
        var pduSpecific = (byte)((identifier >> 8) & 0xFF);
        var sourceAddress = (byte)(identifier & 0xFF);

        // The 18-bit PGN carries the reserved/extended-data-page bit, data-page bit,
        // PDU format and (for PDU2 only) group extension. PDU1 uses PDU Specific as
        // a destination, so its PGN low byte must be zeroed.
        var pgnBits = (int)((identifier >> 8) & 0x3FFFF);
        var isPeerToPeer = pduFormat < 240;
        var pgn = isPeerToPeer ? pgnBits & 0x3FF00 : pgnBits;
        var destinationAddress = isPeerToPeer ? pduSpecific : GlobalAddress;

        return new J1939CanFrameEnvelope(
            identifier,
            priority,
            pgn,
            sourceAddress,
            destinationAddress,
            isPeerToPeer,
            rawFrame.Data.ToArray(),
            rawFrame.CapturedAt,
            rawFrame.AdapterType,
            rawFrame.Channel,
            rawFrame.CaptureReference);
    }

    private static void ValidateEvidenceLabel(string value, string field, int maximumLength)
    {
        if (string.IsNullOrWhiteSpace(value))
            throw new J1939AcquisitionException($"{field} is required for capture provenance.");
        if (value.Length > maximumLength)
            throw new J1939AcquisitionException($"{field} cannot exceed {maximumLength} characters.");
        if (!string.Equals(value, value.Trim(), StringComparison.Ordinal))
            throw new J1939AcquisitionException($"{field} cannot contain leading or trailing whitespace.");
        if (value.Any(char.IsControl))
            throw new J1939AcquisitionException($"{field} cannot contain control characters.");
    }
}

public sealed record J1939RawCanFrame(
    uint Identifier,
    bool IsExtendedIdentifier,
    ReadOnlyMemory<byte> Data,
    DateTimeOffset CapturedAt,
    string AdapterType,
    string Channel,
    string CaptureReference,
    bool IsRemoteTransmissionRequest = false,
    bool IsErrorFrame = false);

public sealed record J1939CanFrameEnvelope(
    uint RawIdentifier,
    byte Priority,
    int Pgn,
    byte SourceAddress,
    byte DestinationAddress,
    bool IsPeerToPeer,
    ReadOnlyMemory<byte> Data,
    DateTimeOffset CapturedAt,
    string AdapterType,
    string Channel,
    string CaptureReference);

/// <summary>
/// One complete J1939 message with its bounded acquisition evidence. MessagePriority
/// is deliberately null for transported messages because TP.CM identifies the target
/// PGN but does not preserve the target message's original CAN priority.
/// </summary>
public sealed record J1939AcquiredMessage(
    int Pgn,
    byte SourceAddress,
    byte DestinationAddress,
    ReadOnlyMemory<byte> Payload,
    bool IsTransported,
    byte? MessagePriority,
    DateTimeOffset FirstFrameAt,
    DateTimeOffset CompletedAt,
    IReadOnlyList<J1939CanFrameEnvelope> Frames);

public sealed class J1939AcquisitionException(string message) : Exception(message);
