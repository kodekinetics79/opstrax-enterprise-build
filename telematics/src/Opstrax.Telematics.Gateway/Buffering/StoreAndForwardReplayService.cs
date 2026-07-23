using System.Collections.Concurrent;
using System.Reflection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Opstrax.Telematics.Contracts;
using Opstrax.Telematics.Contracts.Eventing;

namespace Opstrax.Telematics.Gateway.Buffering;

/// <summary>
/// Tuning knobs for <see cref="StoreAndForwardReplayService"/>. Every value is a durability/rate
/// bound, not a nicety: the replay loop hammers a recovering broker, so its backoff has to grow
/// under a sustained outage instead of spinning.
/// </summary>
internal sealed class StoreAndForwardReplayOptions
{
    /// <summary>Backoff before the FIRST retry of an entry whose republish just failed.</summary>
    public TimeSpan InitialBackoff { get; set; } = TimeSpan.FromMilliseconds(200);

    /// <summary>Ceiling the exponential backoff is clamped to, so a long outage settles at a steady poll.</summary>
    public TimeSpan MaxBackoff { get; set; } = TimeSpan.FromSeconds(30);

    /// <summary>Growth factor applied to the backoff on each successive failure of the same entry.</summary>
    public double BackoffMultiplier { get; set; } = 2.0;

    /// <summary>How long to sleep after fully draining the buffer before checking it again.</summary>
    public TimeSpan IdlePollInterval { get; set; } = TimeSpan.FromMilliseconds(500);
}

/// <summary>
/// The consumer of <see cref="IStoreAndForwardBuffer.TryDequeue"/>. Drains events the gateway
/// parked during a backbone outage and republishes them, closing the durability loop the framing
/// path opens when it enqueues on a publish failure.
/// </summary>
/// <remarks>
/// <para>
/// <b>Ordering is the hard requirement, and it is why this service holds one entry at a time.</b>
/// The buffer is FIFO, so within a device its parked fixes are ordered. The loop dequeues exactly
/// one entry and does not advance to the next until that entry has been republished successfully —
/// a failing entry <em>blocks</em> the drain rather than being dropped or re-queued to the tail.
/// Re-queuing to the tail (the obvious naive fix) would let a later fix for the same device
/// overtake the stuck one, which is precisely the reordering hazard the Increment-1 review flagged.
/// Holding the entry guarantees no event is lost and none overtakes an earlier one for its device.
/// </para>
/// <para>
/// <b>Backoff.</b> Each failed attempt on the held entry grows the delay geometrically up to
/// <see cref="StoreAndForwardReplayOptions.MaxBackoff"/>, so a downstream that is down for minutes
/// costs a steady trickle of attempts, not a hot loop.
/// </para>
/// <para>
/// <b>Shutdown.</b> If the host stops while an entry is still un-republished, that entry is placed
/// back into the buffer so it is not silently dropped for the remainder of the process lifetime.
/// (The in-memory buffer is not durable across a process restart — see
/// <see cref="IStoreAndForwardBuffer"/> — a WAL-backed implementation closes that last gap.)
/// </para>
/// </remarks>
internal sealed class StoreAndForwardReplayService : BackgroundService
{
    private static readonly MethodInfo PublishOpenMethod =
        typeof(IEventBackbone).GetMethod(nameof(IEventBackbone.PublishAsync))
        ?? throw new InvalidOperationException("IEventBackbone.PublishAsync not found.");

    private static readonly ConcurrentDictionary<Type, MethodInfo> PublishByPayload = new();

    private readonly IStoreAndForwardBuffer _buffer;
    private readonly IEventBackbone _backbone;
    private readonly StoreAndForwardReplayOptions _options;
    private readonly ILogger<StoreAndForwardReplayService> _logger;
    private readonly Func<TimeSpan, CancellationToken, Task> _delay;

    private long _replayed;
    private long _retries;

    /// <summary>Production/DI constructor: real time-based backoff.</summary>
    public StoreAndForwardReplayService(
        IStoreAndForwardBuffer buffer,
        IEventBackbone backbone,
        StoreAndForwardReplayOptions options,
        ILogger<StoreAndForwardReplayService> logger)
        : this(buffer, backbone, options, logger, DefaultDelay)
    {
    }

    /// <summary>Test constructor: inject a deterministic (e.g. no-op) delay so drain behaviour is timing-free.</summary>
    internal StoreAndForwardReplayService(
        IStoreAndForwardBuffer buffer,
        IEventBackbone backbone,
        StoreAndForwardReplayOptions options,
        ILogger<StoreAndForwardReplayService> logger,
        Func<TimeSpan, CancellationToken, Task> delay)
    {
        _buffer = buffer ?? throw new ArgumentNullException(nameof(buffer));
        _backbone = backbone ?? throw new ArgumentNullException(nameof(backbone));
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _delay = delay ?? throw new ArgumentNullException(nameof(delay));
    }

    /// <summary>Events successfully republished from the buffer since start. Observability/tests.</summary>
    public long Replayed => Interlocked.Read(ref _replayed);

    /// <summary>Total failed republish attempts across all entries. Observability/tests.</summary>
    public long Retries => Interlocked.Read(ref _retries);

    /// <inheritdoc />
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Store-and-forward replay service started.");

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await DrainAsync(stoppingToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                // The drain loop is total; this is a belt-and-braces net so an unexpected fault
                // pauses-and-retries rather than killing the hosted service.
                _logger.LogError(ex, "Replay drain faulted unexpectedly; backing off before retrying.");
            }

            try
            {
                await _delay(_options.IdlePollInterval, stoppingToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }

        _logger.LogInformation(
            "Store-and-forward replay service stopped. Replayed={Replayed}, Retries={Retries}, Remaining={Remaining}.",
            Replayed, Retries, _buffer.Count);
    }

    /// <summary>
    /// Drains the buffer once: republishes parked entries oldest-first, holding each entry until it
    /// succeeds so order is preserved and nothing is lost. Returns when the buffer is empty (or the
    /// token fires). Exposed internally so tests can drive a deterministic, single drain pass.
    /// </summary>
    internal async Task DrainAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested && _buffer.TryDequeue(out StoreAndForwardEntry entry))
        {
            bool republished = false;
            int attempt = 0;

            while (!cancellationToken.IsCancellationRequested)
            {
                try
                {
                    await RepublishAsync(entry, cancellationToken).ConfigureAwait(false);
                    Interlocked.Increment(ref _replayed);
                    republished = true;
                    break;
                }
                catch (Exception ex)
                {
                    if (cancellationToken.IsCancellationRequested)
                        break; // shutting down; entry is re-buffered below, never dropped.

                    Interlocked.Increment(ref _retries);
                    attempt++;
                    TimeSpan backoff = ComputeBackoff(attempt);

                    _logger.LogWarning(
                        ex,
                        "Replay of topic {Topic} key {Key} failed (attempt {Attempt}); retrying in {Backoff}. Entry stays at the head so no later fix for its device overtakes it.",
                        entry.Topic, entry.Key, attempt, backoff);

                    try
                    {
                        await _delay(backoff, cancellationToken).ConfigureAwait(false);
                    }
                    catch (OperationCanceledException)
                    {
                        break;
                    }
                    // loop: retry the SAME entry.
                }
            }

            if (!republished)
            {
                // Cancelled mid-flight while still holding an un-republished entry. Put it back so it
                // is not lost. It returns to the tail, but the drain is stopping anyway, so no later
                // same-device fix is being republished concurrently to overtake it.
                await _buffer.EnqueueAsync(entry, CancellationToken.None).ConfigureAwait(false);
                return;
            }
        }
    }

    /// <summary>
    /// Republishes one parked envelope to the exact topic and key it was originally destined for, so
    /// it lands on the same partition and stays in its device's ordering group.
    /// </summary>
    private Task RepublishAsync(in StoreAndForwardEntry entry, CancellationToken cancellationToken)
    {
        // Fast path: the framing loop only ever parks canonical telemetry envelopes.
        if (entry.Envelope is EventEnvelope<CanonicalTelemetryEvent> canonical)
            return _backbone.PublishAsync(entry.Topic, entry.Key, canonical, cancellationToken);

        // General path: any other closed EventEnvelope<T>. Resolve PublishAsync<T> once per payload type.
        Type envelopeType = entry.Envelope.GetType();
        if (!envelopeType.IsGenericType || envelopeType.GetGenericTypeDefinition() != typeof(EventEnvelope<>))
        {
            // Not a recognisable envelope — undeliverable. Log and drop rather than block the drain
            // forever on something no PublishAsync overload can accept.
            _logger.LogError(
                "Discarding buffered entry on topic {Topic} key {Key}: payload type {Type} is not an EventEnvelope<T>.",
                entry.Topic, entry.Key, envelopeType.FullName);
            return Task.CompletedTask;
        }

        Type payloadType = envelopeType.GetGenericArguments()[0];
        MethodInfo publish = PublishByPayload.GetOrAdd(payloadType, static t => PublishOpenMethod.MakeGenericMethod(t));
        return (Task)publish.Invoke(_backbone, new object[] { entry.Topic, entry.Key, entry.Envelope, cancellationToken })!;
    }

    private TimeSpan ComputeBackoff(int attempt)
    {
        // attempt is 1-based. Grow InitialBackoff * multiplier^(attempt-1), clamped to MaxBackoff.
        double factor = Math.Pow(_options.BackoffMultiplier, Math.Max(0, attempt - 1));
        double ms = _options.InitialBackoff.TotalMilliseconds * factor;

        if (double.IsNaN(ms) || double.IsInfinity(ms) || ms > _options.MaxBackoff.TotalMilliseconds)
            return _options.MaxBackoff;

        return TimeSpan.FromMilliseconds(ms);
    }

    private static Task DefaultDelay(TimeSpan delay, CancellationToken cancellationToken) =>
        Task.Delay(delay, cancellationToken);
}
