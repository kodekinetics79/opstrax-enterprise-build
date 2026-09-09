namespace Opstrax.Telematics.Protocols.J1939;

/// <summary>
/// Routes complete, evidence-bearing J1939 messages through the diagnostic decoder
/// or the explicit signal catalog. All other complete PGNs remain unsupported.
/// </summary>
public sealed class J1939MessageAcquisition
{
    private readonly J1939DiagnosticAcquisition _diagnosticAcquisition;

    public J1939MessageAcquisition(TimeSpan? transportSessionTimeout = null)
    {
        _diagnosticAcquisition = new J1939DiagnosticAcquisition(transportSessionTimeout);
    }

    public int ActiveTransportSessionCount => _diagnosticAcquisition.ActiveTransportSessionCount;

    public J1939MessageAcquisitionResult Accept(J1939RawCanFrame rawFrame)
    {
        var acquired = _diagnosticAcquisition.Accept(rawFrame);
        if (acquired.Status == J1939DiagnosticAcquisitionStatus.NoCompleteMessage)
            return J1939MessageAcquisitionResult.NoCompleteMessage();
        if (acquired.Status == J1939DiagnosticAcquisitionStatus.DiagnosticDecoded)
            return J1939MessageAcquisitionResult.DiagnosticDecoded(acquired.Message!, acquired.Diagnostic!);

        var message = acquired.Message!;
        return J1939SignalDecoder.TryDecode(message, out var signals)
            ? J1939MessageAcquisitionResult.SignalsDecoded(message, signals!)
            : J1939MessageAcquisitionResult.Unsupported(message);
    }
}

public enum J1939MessageAcquisitionStatus
{
    NoCompleteMessage,
    UnsupportedMessage,
    DiagnosticDecoded,
    SignalsDecoded,
}

/// <summary>
/// A total route result. Exactly one of <see cref="Diagnostic"/> and
/// <see cref="Signals"/> is present for a decoded complete message.
/// </summary>
public sealed class J1939MessageAcquisitionResult
{
    private J1939MessageAcquisitionResult(
        J1939MessageAcquisitionStatus status,
        J1939AcquiredMessage? message,
        DiagnosticMessage? diagnostic,
        J1939SignalDecodeResult? signals)
    {
        Status = status;
        Message = message;
        Diagnostic = diagnostic;
        Signals = signals;
    }

    public J1939MessageAcquisitionStatus Status { get; }
    public J1939AcquiredMessage? Message { get; }
    public DiagnosticMessage? Diagnostic { get; }
    public J1939SignalDecodeResult? Signals { get; }

    internal static J1939MessageAcquisitionResult NoCompleteMessage()
        => new(J1939MessageAcquisitionStatus.NoCompleteMessage, null, null, null);

    internal static J1939MessageAcquisitionResult Unsupported(J1939AcquiredMessage message)
        => new(J1939MessageAcquisitionStatus.UnsupportedMessage, message, null, null);

    internal static J1939MessageAcquisitionResult DiagnosticDecoded(
        J1939AcquiredMessage message,
        DiagnosticMessage diagnostic)
        => new(J1939MessageAcquisitionStatus.DiagnosticDecoded, message, diagnostic, null);

    internal static J1939MessageAcquisitionResult SignalsDecoded(
        J1939AcquiredMessage message,
        J1939SignalDecodeResult signals)
        => new(J1939MessageAcquisitionStatus.SignalsDecoded, message, null, signals);
}
