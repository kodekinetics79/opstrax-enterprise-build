namespace Opstrax.Telematics.Protocols.J1939;

/// <summary>
/// Joins the bounded classic-CAN acquisition path to the existing DM1/DM2 decoder.
/// It classifies ordinary non-diagnostic J1939 traffic explicitly so callers can
/// route it elsewhere without treating it as a diagnostic or discarding its evidence.
/// </summary>
public sealed class J1939DiagnosticAcquisition
{
    private readonly J1939CanAcquisition _canAcquisition;

    public J1939DiagnosticAcquisition(TimeSpan? transportSessionTimeout = null)
        : this(new J1939CanAcquisition(transportSessionTimeout))
    {
    }

    internal J1939DiagnosticAcquisition(J1939CanAcquisition canAcquisition)
    {
        _canAcquisition = canAcquisition ?? throw new ArgumentNullException(nameof(canAcquisition));
    }

    public int ActiveTransportSessionCount => _canAcquisition.ActiveTransportSessionCount;

    /// <summary>
    /// Accepts one captured CAN frame. Transport fragments and control frames return
    /// <see cref="J1939DiagnosticAcquisitionStatus.NoCompleteMessage"/>. Complete
    /// DM1/DM2 messages are decoded; every other complete PGN is returned as
    /// non-diagnostic with its acquisition evidence intact.
    /// </summary>
    public J1939DiagnosticAcquisitionResult Accept(J1939RawCanFrame rawFrame)
    {
        var message = _canAcquisition.Accept(rawFrame);
        if (message is null)
            return J1939DiagnosticAcquisitionResult.NoCompleteMessage();

        if (message.Pgn is not (J1939DiagnosticDecoder.Dm1Pgn or J1939DiagnosticDecoder.Dm2Pgn))
            return J1939DiagnosticAcquisitionResult.NonDiagnostic(message);

        try
        {
            var diagnostic = J1939DiagnosticDecoder.Decode(message.Pgn, message.Payload.Span);
            return J1939DiagnosticAcquisitionResult.Decoded(message, diagnostic);
        }
        catch (ArgumentException ex)
        {
            throw new J1939DiagnosticAcquisitionException(
                message.Pgn,
                message.SourceAddress,
                message.FirstFrameAt,
                message.CompletedAt,
                message.Frames.Select(frame => frame.CaptureReference).ToArray(),
                $"The complete J1939 diagnostic message was rejected: {ex.Message}",
                ex);
        }
    }
}

public enum J1939DiagnosticAcquisitionStatus
{
    NoCompleteMessage,
    NonDiagnosticMessage,
    DiagnosticDecoded,
}

/// <summary>
/// A total result for one accepted frame. Only a completed message carries
/// <see cref="Message"/>, and only a decoded DM1/DM2 result carries
/// <see cref="Diagnostic"/>.
/// </summary>
public sealed class J1939DiagnosticAcquisitionResult
{
    private J1939DiagnosticAcquisitionResult(
        J1939DiagnosticAcquisitionStatus status,
        J1939AcquiredMessage? message,
        DiagnosticMessage? diagnostic)
    {
        Status = status;
        Message = message;
        Diagnostic = diagnostic;
    }

    public J1939DiagnosticAcquisitionStatus Status { get; }
    public J1939AcquiredMessage? Message { get; }
    public DiagnosticMessage? Diagnostic { get; }

    internal static J1939DiagnosticAcquisitionResult NoCompleteMessage()
        => new(J1939DiagnosticAcquisitionStatus.NoCompleteMessage, null, null);

    internal static J1939DiagnosticAcquisitionResult NonDiagnostic(J1939AcquiredMessage message)
        => new(J1939DiagnosticAcquisitionStatus.NonDiagnosticMessage, message, null);

    internal static J1939DiagnosticAcquisitionResult Decoded(
        J1939AcquiredMessage message,
        DiagnosticMessage diagnostic)
        => new(J1939DiagnosticAcquisitionStatus.DiagnosticDecoded, message, diagnostic);
}

/// <summary>
/// A complete, evidence-bearing DM1/DM2 message failed semantic decoding.
/// Raw payload bytes are deliberately omitted from the exception text and fields.
/// </summary>
public sealed class J1939DiagnosticAcquisitionException : Exception
{
    public J1939DiagnosticAcquisitionException(
        int pgn,
        byte sourceAddress,
        DateTimeOffset firstFrameAt,
        DateTimeOffset completedAt,
        IReadOnlyList<string> captureReferences,
        string message,
        Exception innerException)
        : base(message, innerException)
    {
        Pgn = pgn;
        SourceAddress = sourceAddress;
        FirstFrameAt = firstFrameAt;
        CompletedAt = completedAt;
        CaptureReferences = Array.AsReadOnly(captureReferences.ToArray());
    }

    public int Pgn { get; }
    public byte SourceAddress { get; }
    public DateTimeOffset FirstFrameAt { get; }
    public DateTimeOffset CompletedAt { get; }
    public IReadOnlyList<string> CaptureReferences { get; }
}
