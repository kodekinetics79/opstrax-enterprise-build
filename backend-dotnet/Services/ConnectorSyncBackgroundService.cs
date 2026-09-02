using Opstrax.Api.Data;
using Opstrax.Api.Services.Connectors;

namespace Opstrax.Api.Services;

// Automatic third-party position sync — the 'keep your Samsara' overlay path. Every tick, each
// CONNECTED or retry-eligible ERROR integration whose connector implements a real 'sync' action
// positions) runs an incremental pull with its stored cursor, so a tenant who connects an API key
// gets continuous positions -> geofence events -> detention detection with zero manual syncs.
// Failures mark the integration 'Error' and retry after a bounded cool-down; they never block
// other tenants' syncs. Error is an observable state, not a terminal scheduling state.
public sealed class ConnectorSyncBackgroundService(
    IServiceScopeFactory scopeFactory,
    ILogger<ConnectorSyncBackgroundService> logger) : BackgroundService
{
    private static readonly TimeSpan Interval = TimeSpan.FromMinutes(5);
    private static readonly string[] SyncCapable = ["samsara"];   // connectors with a real 'sync' action

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await Task.Delay(TimeSpan.FromSeconds(60), stoppingToken).ContinueWith(_ => { }, stoppingToken);
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await SyncOnceAsync(scopeFactory, logger, stoppingToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogWarning(ex, "Connector sync tick failed; next tick retries");
            }
            await Task.Delay(Interval, stoppingToken).ContinueWith(_ => { }, stoppingToken);
        }
    }

    internal static async Task SyncOnceAsync(
        IServiceScopeFactory scopeFactory,
        ILogger logger,
        CancellationToken ct)
    {
        List<Dictionary<string, object?>> rows;
        using (var discoveryScope = scopeFactory.CreateScope())
        {
            var discoveryDb = discoveryScope.ServiceProvider.GetRequiredService<Database>();
            rows = await SelectCandidateRowsAsync(discoveryDb, 500, ct);
        }

        // Each tenant gets its own DI/database scope and bounded provider budget.
        // Four-way concurrency prevents one slow provider account from serially
        // starving every tenant while keeping outbound pressure controlled.
        await Parallel.ForEachAsync(
            rows,
            new ParallelOptions { MaxDegreeOfParallelism = 4, CancellationToken = ct },
            async (row, token) =>
        {
            var id = Convert.ToInt64(row["id"]);
            var companyId = Convert.ToInt64(row["companyId"]);
            ConnectorOperationContext? operation = null;
            using var itemScope = scopeFactory.CreateScope();
            var db = itemScope.ServiceProvider.GetRequiredService<Database>();
            var connectors = itemScope.ServiceProvider.GetRequiredService<ConnectorRegistry>();
            try
            {
                operation = await ConnectorOperationLease.TryAcquireAsync(
                    db, companyId, id, ["Connected", "Error"], TimeSpan.FromSeconds(90), token);
                if (operation is null) return;

                var connector = connectors.Resolve(operation.IntegrationKey);
                var config = connectors.DecryptConfig(operation.ConfigJson);
                var stored = ConnectorRegistry.RedactConfig(operation.ConfigJson);
                var cursor = stored.TryGetValue("syncCursor", out var cv) ? cv?.ToString() : null;

                using var body = System.Text.Json.JsonDocument.Parse(
                    System.Text.Json.JsonSerializer.Serialize(new
                    {
                        action = "sync",
                        companyId,
                        integrationId = id,
                        operationGeneration = operation.Generation,
                        operationLeaseToken = operation.LeaseToken,
                        cursor,
                        maxPages = 5,
                        maxDurationSeconds = 60,
                    }));
                var result = await connector.RunActionAsync("sync", config, body.RootElement, token);

                var nextCursor = result.Details?.GetValueOrDefault("nextCursor")?.ToString();
                await ConnectorOperationLease.CompleteSyncAsync(db, operation, result, nextCursor, token);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogWarning(ex, "Connector sync failed for integration {Integration} in company {Company}", id, companyId);
                if (operation is not null)
                    await ConnectorOperationLease.ReleaseAsErrorAsync(db, operation, token);
            }
        });
    }

    internal static Task<List<Dictionary<string, object?>>> SelectCandidateRowsAsync(
        Database db,
        int requestedLimit,
        CancellationToken ct,
        long[]? companyIds = null)
    {
        var limit = Math.Clamp(requestedLimit, 1, 500);
        // Every lease acquisition records operation_last_attempt_at, including failed
        // attempts. Ordering by that durable fairness clock rotates a repeatedly failing
        // prefix behind tenants that have not yet had a turn; last_sync_at is only the
        // compatibility fallback for rows created before Stage 95.
        return db.RunInSystemScopeAsync(
            () => db.QueryAsync(
                @"SELECT id,company_id FROM integrations
                  WHERE (status='Connected'
                         OR (status='Error' AND updated_at <= NOW() - INTERVAL '15 minutes'))
                    AND integration_key = ANY(@keys)
                    AND (@allCompanies OR company_id = ANY(@companyIds))
                  ORDER BY COALESCE(operation_last_attempt_at,last_sync_at,'epoch'::timestamptz),id
                  LIMIT @limit",
                c =>
                {
                    c.Parameters.AddWithValue("@keys", SyncCapable);
                    c.Parameters.AddWithValue("@allCompanies", companyIds is null);
                    c.Parameters.AddWithValue("@companyIds", companyIds ?? Array.Empty<long>());
                    c.Parameters.AddWithValue("@limit", limit);
                }, ct), ct);
    }
}
