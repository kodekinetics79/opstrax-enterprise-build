using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Opstrax.Telematics.Contracts;
using Opstrax.Telematics.Contracts.Eventing;
using Opstrax.Telematics.Gateway.Buffering;

namespace Opstrax.Telematics.Gateway.Forwarding;

/// <summary>
/// Publishes one already-owned canonical event and parks its exact envelope when the
/// backbone is unavailable. All gateway CAN producers share this durability boundary.
/// </summary>
internal sealed class CanonicalTelemetryPublisher
{
    private readonly IEventBackbone _backbone;
    private readonly IStoreAndForwardBuffer? _forwardBuffer;
    private readonly ILogger<CanonicalTelemetryPublisher> _logger;

    internal CanonicalTelemetryPublisher(IEventBackbone backbone)
    {
        _backbone = backbone ?? throw new ArgumentNullException(nameof(backbone));
        _logger = NullLogger<CanonicalTelemetryPublisher>.Instance;
    }

    public CanonicalTelemetryPublisher(
        IEventBackbone backbone,
        IStoreAndForwardBuffer forwardBuffer,
        ILogger<CanonicalTelemetryPublisher> logger)
    {
        _backbone = backbone ?? throw new ArgumentNullException(nameof(backbone));
        _forwardBuffer = forwardBuffer ?? throw new ArgumentNullException(nameof(forwardBuffer));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task PublishAsync(
        CanonicalTelemetryEvent canonical,
        IReadOnlyDictionary<string, string> headers,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(canonical);
        ArgumentNullException.ThrowIfNull(headers);

        string key = TelematicsEventKey.ForDevice(
            canonical.TenantId,
            canonical.CompanyId,
            canonical.DeviceId);
        var envelope = new EventEnvelope<CanonicalTelemetryEvent>
        {
            EventId = canonical.EventId,
            CorrelationId = canonical.CorrelationId,
            OccurredAt = new DateTimeOffset(canonical.OccurredAtDeviceUtc, TimeSpan.Zero),
            TenantId = canonical.TenantId,
            CompanyId = canonical.CompanyId,
            SchemaVersion = canonical.SchemaVersion,
            Payload = canonical,
            Headers = new Dictionary<string, string>(headers, StringComparer.Ordinal),
        };

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
                "Canonical publish failed for device {DeviceId}; parking event {EventId} in store-and-forward.",
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
                    "Both canonical persistence and store-and-forward failed for event {EventId}.",
                    canonical.EventId);
                throw new AggregateException(
                    "Both canonical persistence and store-and-forward failed.",
                    ex,
                    bufferException);
            }
        }
    }
}
