using Opstrax.Telematics.Contracts;
using Opstrax.Telematics.Contracts.Identity;
using Opstrax.Telematics.Contracts.Provenance;
using Opstrax.Telematics.Contracts.Quality;
using Opstrax.Telematics.Contracts.Signals;

namespace Opstrax.Telematics.Protocols.J1939;

/// <summary>
/// Converts one supported acquired J1939 message to the canonical telemetry shape.
/// Ownership and trust are supplied by the caller's authenticated registry path;
/// neither is inferred from a CAN source address or capture label.
/// </summary>
public static class J1939CanonicalEventFactory
{
    public const string ProtocolName = "J1939";
    public const string ProtocolVersion = "SAE J1939";
    public const string AdapterName = "J1939SignalCatalog";
    public const string AdapterVersion = "1.1.0";

    public static CanonicalTelemetryEvent Create(
        J1939AcquiredMessage message,
        J1939CanonicalizationContext context)
    {
        ArgumentNullException.ThrowIfNull(message);
        ArgumentNullException.ThrowIfNull(context);
        J1939CanonicalizationGuard.Validate(context, message);

        if (!J1939SignalDecoder.TryDecode(message, out var decoded))
            throw new ArgumentException($"J1939 PGN {message.Pgn} is not in the supported signal catalog.", nameof(message));

        var capturedAtUtc = message.CompletedAt.UtcDateTime;
        var isStale = context.NormalizedAtUtc - capturedAtUtc > context.FreshnessBudget;
        var signals = new Dictionary<string, SignalValue>(StringComparer.Ordinal);
        double? batteryVoltage = null;

        foreach (var observation in decoded!.Observations)
        {
            var availability = Availability(observation.Status, isStale);
            var value = observation.Status == J1939SignalStatus.Valid
                ? observation.Value
                : null;
            signals.Add(
                observation.Definition.CanonicalPath,
                new SignalValue(
                    value,
                    observation.Definition.Unit,
                    context.Source,
                    context.Confidence,
                    availability));

            if (observation.Definition.Spn == J1939SignalDecoder.BatteryPotentialSpn &&
                availability == SignalAvailability.Available)
            {
                batteryVoltage = value;
            }
        }

        return new CanonicalTelemetryEvent
        {
            SchemaVersion = CanonicalTelemetryEvent.CurrentSchemaVersion,
            EventId = context.EventId,
            CorrelationId = context.CorrelationId,
            OccurredAtDeviceUtc = capturedAtUtc,
            ReceivedAtGatewayUtc = capturedAtUtc,
            NormalizedAtUtc = context.NormalizedAtUtc,
            TenantId = context.Owner.TenantId,
            CompanyId = context.Owner.CompanyId,
            DeviceId = context.Owner.DeviceId,
            VehicleId = context.Owner.VehicleId,
            Source = context.Source,
            Transport = Transport.Can,
            ProtocolName = ProtocolName,
            ProtocolVersion = ProtocolVersion,
            AdapterName = AdapterName,
            AdapterVersion = AdapterVersion,
            Signals = signals,
            BatteryVoltage = batteryVoltage,
            Quality = new QualityFlags { IsStale = isStale },
            TrustScore = context.TrustScore,
            Confidence = context.Confidence,
        };
    }

    private static SignalAvailability Availability(J1939SignalStatus status, bool isStale) => status switch
    {
        J1939SignalStatus.Valid when isStale => SignalAvailability.Stale,
        J1939SignalStatus.Valid => SignalAvailability.Available,
        J1939SignalStatus.ParameterSpecificIndicator => SignalAvailability.ParameterSpecific,
        J1939SignalStatus.ErrorIndicator => SignalAvailability.Error,
        J1939SignalStatus.NotAvailable => SignalAvailability.NotAvailable,
        _ => throw new ArgumentOutOfRangeException(nameof(status), status, "Unknown J1939 signal status."),
    };

}

public sealed record J1939CanonicalizationContext(
    ResolvedDeviceOwner Owner,
    Guid EventId,
    Guid CorrelationId,
    TelemetrySource Source,
    DateTime NormalizedAtUtc,
    TimeSpan FreshnessBudget,
    double TrustScore,
    double Confidence);
