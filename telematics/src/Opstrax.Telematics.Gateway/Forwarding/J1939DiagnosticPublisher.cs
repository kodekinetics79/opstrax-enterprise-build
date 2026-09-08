using System.Globalization;
using System.Text.Json;
using Opstrax.Telematics.Contracts;
using Opstrax.Telematics.Protocols.J1939;

namespace Opstrax.Telematics.Gateway.Forwarding;

/// <summary>Publishes decoded DM1/DM2 evidence through the canonical durability path.</summary>
internal sealed class J1939DiagnosticPublisher(CanonicalTelemetryPublisher publisher)
{
    private readonly CanonicalTelemetryPublisher _publisher =
        publisher ?? throw new ArgumentNullException(nameof(publisher));

    public async Task<CanonicalTelemetryEvent> PublishAsync(
        J1939AcquiredMessage message,
        J1939CanonicalizationContext context,
        CancellationToken cancellationToken = default)
    {
        CanonicalTelemetryEvent canonical = J1939DiagnosticCanonicalEventFactory.Create(message, context);
        J1939CanFrameEnvelope[] evidence = message.Frames.ToArray();
        string[] adapterTypes = evidence.Select(frame => frame.AdapterType).Distinct(StringComparer.Ordinal).ToArray();
        string[] channels = evidence.Select(frame => frame.Channel).Distinct(StringComparer.Ordinal).ToArray();
        if (adapterTypes.Length != 1 || channels.Length != 1)
            throw new InvalidOperationException("A J1939 diagnostic message must retain one acquisition adapter/channel path.");

        var headers = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["j1939.pgn"] = message.Pgn.ToString(CultureInfo.InvariantCulture),
            ["j1939.diagnostic_kind"] = message.Pgn == J1939DiagnosticDecoder.Dm1Pgn ? "DM1" : "DM2",
            ["j1939.source_address"] = $"0x{message.SourceAddress:X2}",
            ["j1939.destination_address"] = $"0x{message.DestinationAddress:X2}",
            ["j1939.adapter_type"] = adapterTypes[0],
            ["j1939.channel"] = channels[0],
            ["j1939.capture_references"] = JsonSerializer.Serialize(
                evidence.Select(frame => frame.CaptureReference).ToArray()),
        };

        await _publisher.PublishAsync(canonical, headers, cancellationToken).ConfigureAwait(false);
        return canonical;
    }
}
