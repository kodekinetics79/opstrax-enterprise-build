using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Opstrax.Api.Foundation;

namespace Opstrax.Api.Services;

public sealed class OutboxDispatcherBackgroundService(
    IOutboxDispatcher dispatcher,
    OutboxDispatcherOptions options,
    ILogger<OutboxDispatcherBackgroundService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!options.Enabled)
        {
            logger.LogInformation("Outbox dispatcher disabled by configuration.");
            return;
        }

        if (logger.IsEnabled(LogLevel.Information))
            logger.LogInformation("Outbox dispatcher started with batch size {BatchSize} and interval {Interval}s.", options.BatchSize, options.PollingIntervalSeconds);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var processed = 0;
                do
                {
                    processed = await dispatcher.DispatchOutboxOnceAsync(stoppingToken);
                    processed += await dispatcher.DispatchInboxOnceAsync(stoppingToken);
                }
                while (processed > 0 && !stoppingToken.IsCancellationRequested);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Foundation dispatcher cycle failed.");
            }

            try
            {
                await Task.Delay(TimeSpan.FromSeconds(Math.Max(1, options.PollingIntervalSeconds)), stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
        }

        logger.LogInformation("Outbox dispatcher stopped.");
    }
}
