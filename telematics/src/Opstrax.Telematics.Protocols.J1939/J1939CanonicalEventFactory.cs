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
        Validate(context, message);

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

    private static void Validate(J1939CanonicalizationContext context, J1939AcquiredMessage message)
    {
        if (context.Owner.TenantId == Guid.Empty)
            throw new ArgumentException("Registry-resolved tenant identity is required.", nameof(context));
        if (context.Owner.CompanyId <= 0)
            throw new ArgumentException("Registry-resolved company identity is required.", nameof(context));
        if (string.IsNullOrWhiteSpace(context.Owner.DeviceId) ||
            !string.Equals(context.Owner.DeviceId, context.Owner.DeviceId.Trim(), StringComparison.Ordinal))
            throw new ArgumentException("Registry-resolved device identity is required without surrounding whitespace.", nameof(context));
        if (context.EventId == Guid.Empty || context.CorrelationId == Guid.Empty)
            throw new ArgumentException("Non-empty event and correlation identities are required.", nameof(context));
        if (!Enum.IsDefined(context.Source))
            throw new ArgumentOutOfRangeException(nameof(context), "Telemetry source is invalid.");
        if (context.NormalizedAtUtc.Kind != DateTimeKind.Utc)
            throw new ArgumentException("Normalization time must be UTC.", nameof(context));
        if (context.FreshnessBudget <= TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(context), "Freshness budget must be positive.");
        if (context.NormalizedAtUtc < message.CompletedAt.UtcDateTime)
            throw new ArgumentException("Normalization time cannot precede CAN capture completion.", nameof(context));
        if (!double.IsFinite(context.TrustScore) || context.TrustScore is < 0 or > 1)
            throw new ArgumentOutOfRangeException(nameof(context), "Trust score must be finite and inside [0,1].");
        if (!double.IsFinite(context.Confidence) || context.Confidence is < 0 or > 1)
            throw new ArgumentOutOfRangeException(nameof(context), "Confidence must be finite and inside [0,1].");
        if (message.Frames.Count == 0)
            throw new ArgumentException("Acquisition frame evidence is required.", nameof(message));
    }
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
