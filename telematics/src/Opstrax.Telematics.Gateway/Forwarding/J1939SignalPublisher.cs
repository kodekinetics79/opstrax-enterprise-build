using System.Globalization;
using System.Text.Json;
using Opstrax.Telematics.Contracts;
using Opstrax.Telematics.Contracts.Eventing;
using Opstrax.Telematics.Protocols.J1939;

namespace Opstrax.Telematics.Gateway.Forwarding;

/// <summary>
/// Publishes a catalog-supported J1939 signal message after the physical acquisition
/// host has resolved and authenticated its owning device. The future CAN listener calls
/// this boundary; source address is never used as tenant or device identity.
/// </summary>
internal sealed class J1939SignalPublisher(IEventBackbone backbone)
{
    private readonly IEventBackbone _backbone = backbone ?? throw new ArgumentNullException(nameof(backbone));

    public async Task<CanonicalTelemetryEvent> PublishAsync(
        J1939AcquiredMessage message,
        J1939CanonicalizationContext context,
        CancellationToken cancellationToken = default)
    {
        var canonical = J1939CanonicalEventFactory.Create(message, context);
        var frameEvidence = message.Frames.ToArray();
        var adapterTypes = frameEvidence.Select(frame => frame.AdapterType).Distinct(StringComparer.Ordinal).ToArray();
        var channels = frameEvidence.Select(frame => frame.Channel).Distinct(StringComparer.Ordinal).ToArray();
        if (adapterTypes.Length != 1 || channels.Length != 1)
            throw new InvalidOperationException("A J1939 message must retain one acquisition adapter/channel path.");

        var headers = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["j1939.pgn"] = message.Pgn.ToString(CultureInfo.InvariantCulture),
            ["j1939.spns"] = string.Join(",", J1939SignalDecoder.SupportedSignals
                .Where(definition => definition.Pgn == message.Pgn)
                .Select(definition => definition.Spn)),
            ["j1939.source_address"] = $"0x{message.SourceAddress:X2}",
            ["j1939.destination_address"] = $"0x{message.DestinationAddress:X2}",
            ["j1939.adapter_type"] = adapterTypes[0],
            ["j1939.channel"] = channels[0],
            ["j1939.capture_references"] = JsonSerializer.Serialize(
                frameEvidence.Select(frame => frame.CaptureReference).ToArray()),
        };
        var envelope = new EventEnvelope<CanonicalTelemetryEvent>
        {
            EventId = canonical.EventId,
            CorrelationId = canonical.CorrelationId,
            OccurredAt = message.CompletedAt,
            TenantId = canonical.TenantId,
            CompanyId = canonical.CompanyId,
            SchemaVersion = canonical.SchemaVersion,
            Payload = canonical,
            Headers = headers,
        };
        var key = TelematicsEventKey.ForDevice(
            canonical.TenantId,
            canonical.CompanyId,
            canonical.DeviceId);

        await _backbone.PublishAsync(
            TelematicsTopics.TelemetryNormalized,
            key,
            envelope,
            cancellationToken).ConfigureAwait(false);
        return canonical;
    }
}
