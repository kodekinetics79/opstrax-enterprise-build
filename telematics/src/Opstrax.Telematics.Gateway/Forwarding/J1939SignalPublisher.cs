using System.Globalization;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Opstrax.Telematics.Contracts;
using Opstrax.Telematics.Contracts.Eventing;
using Opstrax.Telematics.Gateway.Buffering;
using Opstrax.Telematics.Protocols.J1939;

namespace Opstrax.Telematics.Gateway.Forwarding;

/// <summary>
/// Publishes a catalog-supported J1939 signal message after the physical acquisition
/// host has resolved its configuration-bound device through the trusted registry.
/// Source address is never used as tenant or device identity.
/// </summary>
internal sealed class J1939SignalPublisher
{
    private readonly IEventBackbone _backbone;
    private readonly IStoreAndForwardBuffer? _forwardBuffer;
    private readonly ILogger<J1939SignalPublisher> _logger;

    internal J1939SignalPublisher(IEventBackbone backbone)
    {
        _backbone = backbone ?? throw new ArgumentNullException(nameof(backbone));
        _logger = NullLogger<J1939SignalPublisher>.Instance;
    }

    public J1939SignalPublisher(
        IEventBackbone backbone,
        IStoreAndForwardBuffer forwardBuffer,
        ILogger<J1939SignalPublisher> logger)
    {
        _backbone = backbone ?? throw new ArgumentNullException(nameof(backbone));
        _forwardBuffer = forwardBuffer ?? throw new ArgumentNullException(nameof(forwardBuffer));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

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

        try
        {
            await _backbone.PublishAsync(
                TelematicsTopics.TelemetryNormalized,
                key,
                envelope,
                cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (_forwardBuffer is not null)
        {
            _logger.LogError(
                ex,
                "J1939 canonical publish failed for device {DeviceId}; parking event {EventId} in store-and-forward.",
                canonical.DeviceId,
                canonical.EventId);

            try
            {
                await _forwardBuffer.EnqueueAsync(
                    new StoreAndForwardEntry(
                        TelematicsTopics.TelemetryNormalized,
                        key,
                        envelope,
                        DateTimeOffset.UtcNow),
                    CancellationToken.None).ConfigureAwait(false);
            }
            catch (Exception bufferException)
            {
                _logger.LogCritical(
                    bufferException,
                    "Both canonical persistence and store-and-forward failed for J1939 event {EventId}.",
                    canonical.EventId);
                throw new AggregateException(
                    "Both J1939 canonical persistence and store-and-forward failed.",
                    ex,
                    bufferException);
            }
        }

        return canonical;
    }
}
