using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Opstrax.Telematics.Gateway.Edge;

namespace Opstrax.Telematics.Gateway.Forwarding;

/// <summary>
/// Drains the store-and-forward outbox: retries parked fixes until OpsTrax takes them, and gives
/// up only on entries it terminally refuses or that have aged past the point of being ingestable.
/// </summary>
/// <remarks>
/// <para>
/// <b>Order is preserved and a failure stops the sweep.</b> Entries are drained oldest-first, and
/// the first <see cref="ForwardOutcome.Retryable"/> ends the sweep rather than skipping ahead. If
/// OpsTrax is down, everything behind it will fail too, and marching through 50 000 entries to
/// discover that is a self-inflicted denial of service on a host that is also serving trackers.
/// </para>
/// <para>
/// <b>Backoff.</b> A failed sweep waits <see cref="OutboxOptions.FailureBackoff"/> instead of
/// <see cref="OutboxOptions.DrainInterval"/>, so a long outage costs a request every couple of
/// minutes rather than one every fifteen seconds.
/// </para>
/// </remarks>
internal sealed class OutboxDrainService : BackgroundService
{
    /// <summary>
    /// Entries per sweep. Bounded so the drain shares the host fairly with the listener, and so a
    /// recovered backlog is delivered steadily rather than as one thundering burst that OpsTrax
    /// would rate-limit.
    /// </summary>
    private const int BatchSize = 64;

    private readonly IForwardOutbox _outbox;
    private readonly IOpstraxForwarder _forwarder;
    private readonly OutboxOptions _options;
    private readonly EdgeMetrics _metrics;
    private readonly ILogger<OutboxDrainService> _logger;

    /// <summary>Creates the drain service.</summary>
    public OutboxDrainService(
        IForwardOutbox outbox,
        IOpstraxForwarder forwarder,
        OutboxOptions options,
        EdgeMetrics metrics,
        ILogger<OutboxDrainService> logger)
    {
        _outbox = outbox ?? throw new ArgumentNullException(nameof(outbox));
        _forwarder = forwarder ?? throw new ArgumentNullException(nameof(forwarder));
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _metrics = metrics ?? throw new ArgumentNullException(nameof(metrics));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <inheritdoc />
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation(
            "Outbox drain started: every {Interval} while healthy, {Backoff} after a failure, {MaxAge} age limit.",
            _options.DrainInterval, _options.FailureBackoff, _options.MaxAge);

        while (!stoppingToken.IsCancellationRequested)
        {
            TimeSpan delay = _options.DrainInterval;

            try
            {
                await _outbox.PurgeExpiredAsync(_options.MaxAge, stoppingToken).ConfigureAwait(false);

                SweepResult result = await SweepAsync(stoppingToken).ConfigureAwait(false);

                if (result.Blocked)
                    delay = _options.FailureBackoff;
                else if (result.Delivered > 0 && _outbox.Count > 0)
                    delay = TimeSpan.Zero; // Backlog draining cleanly: keep going without waiting.
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                // The drain loop must survive anything the filesystem or network throws at it; a
                // dead drain silently converts a recoverable outage into permanent data loss.
                _logger.LogError(ex, "Outbox drain sweep faulted; retrying after {Backoff}.", _options.FailureBackoff);
                delay = _options.FailureBackoff;
            }

            if (delay <= TimeSpan.Zero) continue;

            try
            {
                await Task.Delay(delay, stoppingToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }

        _logger.LogInformation("Outbox drain stopped with {Parked} fix(es) still parked.", _outbox.Count);
    }

    /// <summary>Attempts one batch, oldest first, stopping at the first retryable failure.</summary>
    private async Task<SweepResult> SweepAsync(CancellationToken cancellationToken)
    {
        IReadOnlyList<OutboxEntry> batch = await _outbox.PeekAsync(BatchSize, cancellationToken).ConfigureAwait(false);
        if (batch.Count == 0) return new SweepResult(0, Blocked: false);

        int delivered = 0;

        foreach (OutboxEntry entry in batch)
        {
            cancellationToken.ThrowIfCancellationRequested();

            ForwardResult result = await _forwarder
                .SendAsync(entry.PayloadJson, cancellationToken)
                .ConfigureAwait(false);

            switch (result.Outcome)
            {
                case ForwardOutcome.Delivered:
                    await _outbox.ReleaseAsync(entry.Id, cancellationToken).ConfigureAwait(false);
                    _metrics.IncrementObservationsDelivered();
                    delivered++;
                    break;

                case ForwardOutcome.Rejected:
                    // OpsTrax understood it and said no. Keeping it would block every entry behind
                    // it forever, because the sweep stops at the first failure.
                    await _outbox.ReleaseAsync(entry.Id, cancellationToken).ConfigureAwait(false);
                    _metrics.IncrementObservationsRejected();
                    _metrics.AddOutboxEntriesDiscarded(1);
                    _logger.LogError(
                        "Discarding a parked fix OpsTrax terminally refused ({Status}: {Detail}).",
                        result.StatusCode, result.Detail);
                    break;

                default:
                    if (delivered > 0)
                        _logger.LogInformation(
                            "Drained {Delivered} parked fix(es) before OpsTrax became unavailable again ({Detail}).",
                            delivered, result.Detail);
                    else
                        _logger.LogDebug("Outbox still blocked ({Detail}); {Parked} fix(es) parked.",
                            result.Detail, _outbox.Count);

                    return new SweepResult(delivered, Blocked: true);
            }
        }

        if (delivered > 0)
            _logger.LogInformation(
                "Drained {Delivered} parked fix(es) to OpsTrax; {Parked} remaining.", delivered, _outbox.Count);

        return new SweepResult(delivered, Blocked: false);
    }

    private readonly record struct SweepResult(int Delivered, bool Blocked);
}
