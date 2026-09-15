using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Opstrax.Telematics.Contracts;
using Opstrax.Telematics.Contracts.Identity;
using Opstrax.Telematics.Contracts.Lifecycle;
using Opstrax.Telematics.Contracts.Provenance;
using Opstrax.Telematics.Gateway.Forwarding;
using Opstrax.Telematics.Protocols.J1939;

namespace Opstrax.Telematics.Gateway.J1939;

/// <summary>
/// Resolves one configuration-bound CAN interface through the device registry and
/// publishes only catalog-supported, evidence-bearing J1939 signals.
/// </summary>
internal sealed class J1939CanIngestService : BackgroundService
{
    private static readonly IReadOnlySet<DeviceLifecycleState> AdmissibleLifecycle =
        new HashSet<DeviceLifecycleState>
        {
            DeviceLifecycleState.AwaitingFirstConnection,
            DeviceLifecycleState.Identified,
            DeviceLifecycleState.Authenticated,
            DeviceLifecycleState.Validating,
            DeviceLifecycleState.Online,
            DeviceLifecycleState.Delayed,
            DeviceLifecycleState.Stale,
            DeviceLifecycleState.Offline,
            DeviceLifecycleState.Degraded,
        };

    private readonly IJ1939CanFrameSource _source;
    private readonly IDeviceRegistry _registry;
    private readonly J1939SignalPublisher _publisher;
    private readonly J1939DiagnosticPublisher _diagnosticPublisher;
    private readonly J1939CanHostOptions _options;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<J1939CanIngestService> _logger;
    private ResolvedDeviceOwner? _cachedOwner;
    private DateTimeOffset _ownerExpiresAt;

    public J1939CanIngestService(
        IJ1939CanFrameSource source,
        IDeviceRegistry registry,
        J1939SignalPublisher publisher,
        J1939DiagnosticPublisher diagnosticPublisher,
        J1939CanHostOptions options,
        ILogger<J1939CanIngestService> logger,
        TimeProvider? timeProvider = null)
    {
        _source = source ?? throw new ArgumentNullException(nameof(source));
        _registry = registry ?? throw new ArgumentNullException(nameof(registry));
        _publisher = publisher ?? throw new ArgumentNullException(nameof(publisher));
        _diagnosticPublisher = diagnosticPublisher ?? throw new ArgumentNullException(nameof(diagnosticPublisher));
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation(
            "J1939 CAN acquisition started on {Interface}; ownership is bound to the configured registry serial.",
            _options.Interface);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var acquisition = new J1939MessageAcquisition(_options.TransportSessionTimeout);
                await foreach (J1939RawCanFrame frame in _source
                                   .ReadSessionAsync(stoppingToken)
                                   .WithCancellation(stoppingToken)
                                   .ConfigureAwait(false))
                {
                    await ProcessFrameAsync(acquisition, frame, stoppingToken).ConfigureAwait(false);
                }

                _logger.LogWarning(
                    "The CAN acquisition process ended; restarting after {Delay}.",
                    _options.RestartDelay);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(
                    ex,
                    "The CAN acquisition session failed on {Interface}; restarting after {Delay}.",
                    _options.Interface,
                    _options.RestartDelay);
            }

            try
            {
                await Task.Delay(_options.RestartDelay, _timeProvider, stoppingToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
        }
    }

    internal async Task<CanonicalTelemetryEvent?> ProcessFrameAsync(
        J1939MessageAcquisition acquisition,
        J1939RawCanFrame frame,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(acquisition);
        ArgumentNullException.ThrowIfNull(frame);
        cancellationToken.ThrowIfCancellationRequested();

        DateTimeOffset now = _timeProvider.GetUtcNow();
        if (frame.CapturedAt > now + _options.MaximumFutureSkew ||
            frame.CapturedAt < now - _options.MaximumCaptureAge)
        {
            _logger.LogWarning(
                "Rejected a CAN frame on {Interface} because its capture time is outside the admission window.",
                _options.Interface);
            return null;
        }

        J1939MessageAcquisitionResult result;
        try
        {
            result = acquisition.Accept(frame);
        }
        catch (Exception ex) when (ex is J1939AcquisitionException or
                                      J1939SignalDecodeException or
                                      J1939DiagnosticAcquisitionException)
        {
            _logger.LogWarning(
                "Rejected a malformed J1939 frame on {Interface}: {Reason}",
                _options.Interface,
                ex.Message);
            return null;
        }

        if (result.Status is not (J1939MessageAcquisitionStatus.SignalsDecoded or
                                  J1939MessageAcquisitionStatus.DiagnosticDecoded))
            return null;

        ResolvedDeviceOwner? owner = await ResolveOwnerAsync(now, cancellationToken).ConfigureAwait(false);
        if (owner is null)
        {
            _logger.LogWarning(
                "Rejected a decoded J1939 signal on {Interface}: the configured registry serial did not resolve uniquely.",
                _options.Interface);
            return null;
        }
        ResolvedDeviceOwner resolvedOwner = owner.Value;
        if (!AdmissibleLifecycle.Contains(resolvedOwner.LifecycleState))
        {
            _logger.LogWarning(
                "Rejected a decoded J1939 signal on {Interface}: registry lifecycle {Lifecycle} is not ingestible.",
                _options.Interface,
                resolvedOwner.LifecycleState);
            return null;
        }

        J1939AcquiredMessage message = result.Message!;
        Guid eventId = StableIdentity("event", resolvedOwner, message);
        Guid correlationId = StableIdentity("correlation", resolvedOwner, message);
        var context = new J1939CanonicalizationContext(
            resolvedOwner,
            eventId,
            correlationId,
            TelemetrySource.DirectDevice,
            now.UtcDateTime,
            _options.FreshnessBudget,
            _options.TrustScore,
            _options.Confidence);

        return result.Status == J1939MessageAcquisitionStatus.DiagnosticDecoded
            ? await _diagnosticPublisher.PublishAsync(message, context, cancellationToken).ConfigureAwait(false)
            : await _publisher.PublishAsync(message, context, cancellationToken).ConfigureAwait(false);
    }

    private async ValueTask<ResolvedDeviceOwner?> ResolveOwnerAsync(
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        if (now < _ownerExpiresAt)
            return _cachedOwner;

        // Clear before lookup so a registry outage never extends a previously admissible owner.
        _cachedOwner = null;
        _ownerExpiresAt = now;
        ResolvedDeviceOwner? owner = await _registry.ResolveAsync(
            new DeviceIdentityRef(Serial: _options.RegistryDeviceSerial),
            cancellationToken).ConfigureAwait(false);
        _cachedOwner = owner;
        _ownerExpiresAt = now + _options.RegistryRefreshInterval;
        return owner;
    }

    private static Guid StableIdentity(string scope, ResolvedDeviceOwner owner, J1939AcquiredMessage message)
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        Append(hash, scope);
        Append(hash, owner.TenantId.ToString("D"));
        Append(hash, owner.CompanyId.ToString(System.Globalization.CultureInfo.InvariantCulture));
        Append(hash, owner.DeviceId);
        Append(hash, message.Pgn.ToString(System.Globalization.CultureInfo.InvariantCulture));
        Append(hash, message.SourceAddress.ToString("X2", System.Globalization.CultureInfo.InvariantCulture));
        Append(hash, message.DestinationAddress.ToString("X2", System.Globalization.CultureInfo.InvariantCulture));
        foreach (J1939CanFrameEnvelope frame in message.Frames)
            Append(hash, frame.CaptureReference);

        byte[] digest = hash.GetHashAndReset();
        // Deterministic UUID-shaped identity. Version/variant bits make its representation
        // explicit while preserving 122 bits from the SHA-256 evidence digest.
        digest[7] = (byte)((digest[7] & 0x0F) | 0x50);
        digest[8] = (byte)((digest[8] & 0x3F) | 0x80);
        return new Guid(digest.AsSpan(0, 16));
    }

    private static void Append(IncrementalHash hash, string value)
    {
        byte[] bytes = Encoding.UTF8.GetBytes(value);
        Span<byte> length = stackalloc byte[sizeof(int)];
        BinaryPrimitives.WriteInt32LittleEndian(length, bytes.Length);
        hash.AppendData(length);
        hash.AppendData(bytes);
    }
}
