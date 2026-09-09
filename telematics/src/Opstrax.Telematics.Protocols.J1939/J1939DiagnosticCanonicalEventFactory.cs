using Opstrax.Telematics.Contracts;
using Opstrax.Telematics.Contracts.Diagnostics;
using Opstrax.Telematics.Contracts.Provenance;
using Opstrax.Telematics.Contracts.Quality;

namespace Opstrax.Telematics.Protocols.J1939;

/// <summary>Converts one complete DM1/DM2 message into structured canonical evidence.</summary>
public static class J1939DiagnosticCanonicalEventFactory
{
    public const string AdapterName = "J1939DiagnosticDecoder";
    public const string AdapterVersion = "1.0.0";

    public static CanonicalTelemetryEvent Create(
        J1939AcquiredMessage message,
        J1939CanonicalizationContext context)
    {
        ArgumentNullException.ThrowIfNull(message);
        ArgumentNullException.ThrowIfNull(context);
        J1939CanonicalizationGuard.Validate(context, message);

        DiagnosticMessage decoded = J1939DiagnosticDecoder.Decode(message.Pgn, message.Payload.Span);
        bool isStale = context.NormalizedAtUtc - message.CompletedAt.UtcDateTime > context.FreshnessBudget;
        DiagnosticTroubleCode[] troubleCodes = decoded.Dtcs.Select(dtc =>
        {
            string code = $"SPN-{dtc.Spn}-FMI-{dtc.Fmi}";
            string origin = $"SA:{message.SourceAddress:D2}";
            return new DiagnosticTroubleCode(
                code,
                $"J1939:{origin}:SPN:{dtc.Spn}:FMI:{dtc.Fmi}",
                dtc.Spn,
                dtc.Fmi,
                dtc.OccurrenceCount,
                dtc.ConversionMethod);
        }).ToArray();

        var snapshot = new DiagnosticSnapshot(
            J1939CanonicalEventFactory.ProtocolName,
            decoded.Pgn,
            decoded.IsActive,
            message.SourceAddress,
            new DiagnosticLampSnapshot(
                Map(decoded.Lamps.Protect),
                Map(decoded.Lamps.AmberWarning),
                Map(decoded.Lamps.RedStop),
                Map(decoded.Lamps.MalfunctionIndicator),
                Map(decoded.Lamps.ProtectFlash),
                Map(decoded.Lamps.AmberWarningFlash),
                Map(decoded.Lamps.RedStopFlash),
                Map(decoded.Lamps.MalfunctionIndicatorFlash)),
            Array.AsReadOnly(troubleCodes));

        return new CanonicalTelemetryEvent
        {
            SchemaVersion = CanonicalTelemetryEvent.CurrentSchemaVersion,
            EventId = context.EventId,
            CorrelationId = context.CorrelationId,
            OccurredAtDeviceUtc = message.CompletedAt.UtcDateTime,
            ReceivedAtGatewayUtc = message.CompletedAt.UtcDateTime,
            NormalizedAtUtc = context.NormalizedAtUtc,
            TenantId = context.Owner.TenantId,
            CompanyId = context.Owner.CompanyId,
            DeviceId = context.Owner.DeviceId,
            VehicleId = context.Owner.VehicleId,
            Source = context.Source,
            Transport = Transport.Can,
            ProtocolName = J1939CanonicalEventFactory.ProtocolName,
            ProtocolVersion = J1939CanonicalEventFactory.ProtocolVersion,
            AdapterName = AdapterName,
            AdapterVersion = AdapterVersion,
            DtcCodes = Array.AsReadOnly(troubleCodes.Select(code => code.Code).ToArray()),
            Diagnostic = snapshot,
            Quality = new QualityFlags { IsStale = isStale },
            TrustScore = context.TrustScore,
            Confidence = context.Confidence,
        };
    }

    private static DiagnosticLampState Map(LampState state) => state switch
    {
        LampState.Off => DiagnosticLampState.Off,
        LampState.On => DiagnosticLampState.On,
        LampState.Reserved => DiagnosticLampState.Reserved,
        LampState.NotAvailable => DiagnosticLampState.NotAvailable,
        _ => throw new ArgumentOutOfRangeException(nameof(state), state, "Unknown J1939 lamp state."),
    };
}
