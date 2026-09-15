using Opstrax.Api.Data;
using Opstrax.Api.Services.Connectors;
using System.Globalization;
using System.Text.Json;

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
    private static readonly TimeSpan CameraFailureInterval = TimeSpan.FromMinutes(15);
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
                    db, companyId, id, ["Connected", "Error"], TimeSpan.FromSeconds(90), token,
                    isSyncOperation: true);
                if (operation is null) return;

                var connector = connectors.Resolve(operation.IntegrationKey);
                var config = connectors.DecryptConfig(operation.ConfigJson);
                var stored = ConnectorRegistry.RedactConfig(operation.ConfigJson);
                var cursor = stored.TryGetValue("syncCursor", out var cv) ? cv?.ToString() : null;

                using var body = BuildSyncOperationBody(operation, cursor);
                var result = await connector.RunActionAsync("sync", config, body.RootElement, token);

                var nextCursor = result.Details?.GetValueOrDefault("nextCursor")?.ToString();
                var completed = await ConnectorOperationLease.CompleteSyncAsync(
                    db, operation, result, nextCursor, token);

                // Camera safety intake is an explicit customer opt-in and gets a new
                // lease after GPS has completed. It owns its own cursor/outcome, so a
                // provider token without Safety & Cameras permission cannot downgrade
                // otherwise healthy telemetry.
                if (completed == 1 && result.Success &&
                    CameraSafetyPollingDue(operation.ConfigJson, DateTimeOffset.UtcNow))
                {
                    await SyncCameraSafetyOnceAsync(db, connectors, companyId, id, logger, token);
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogWarning(ex, "Connector sync failed for integration {Integration} in company {Company}", id, companyId);
                if (operation is not null)
                    await ConnectorOperationLease.ReleaseAsErrorAsync(db, operation, token);
            }
        });
    }

    private static async Task SyncCameraSafetyOnceAsync(
        Database db,
        ConnectorRegistry connectors,
        long companyId,
        long integrationId,
        ILogger logger,
        CancellationToken ct)
    {
        ConnectorOperationContext? operation = null;
        string startTime = DateTimeOffset.UtcNow.AddHours(-24).ToString("O");
        string? cursor = null;
        try
        {
            operation = await ConnectorOperationLease.TryAcquireAsync(
                db, companyId, integrationId, ["Connected"], TimeSpan.FromSeconds(90), ct,
                isSyncOperation: false);
            if (operation is null) return;

            // Recheck the current leased config. Configure/disconnect invalidates the
            // generation and prevents a stale preference from authorizing provider I/O.
            if (!CameraSafetyPollingDue(operation.ConfigJson, DateTimeOffset.UtcNow))
            {
                await ConnectorOperationLease.ReleaseWithoutStatusChangeAsync(db, operation, ct);
                return;
            }

            var config = connectors.DecryptConfig(operation.ConfigJson);
            using var body = BuildCameraSafetyOperationBody(
                operation, operation.ConfigJson, DateTimeOffset.UtcNow, out startTime, out cursor);
            var result = await connectors.Resolve(operation.IntegrationKey).RunActionAsync(
                "sync-camera-safety", config, body.RootElement, ct);
            var nextCursor = result.Details?.GetValueOrDefault("nextCursor")?.ToString();
            await ConnectorOperationLease.CompleteCameraSafetySyncAsync(
                db, operation, result, startTime, nextCursor, ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(
                ex,
                "Automatic camera safety intake failed for integration {Integration} in company {Company}",
                integrationId,
                companyId);
            if (operation is null) return;

            try
            {
                await ConnectorOperationLease.CompleteCameraSafetySyncAsync(
                    db,
                    operation,
                    ConnectorResult.Fail("Automatic camera safety intake failed."),
                    startTime,
                    cursor,
                    ct);
            }
            catch (Exception releaseException) when (releaseException is not OperationCanceledException)
            {
                logger.LogWarning(
                    releaseException,
                    "Automatic camera safety lease cleanup failed for integration {Integration} in company {Company}",
                    integrationId,
                    companyId);
            }
        }
    }

    internal static JsonDocument BuildSyncOperationBody(
        ConnectorOperationContext operation,
        string? cursor) => JsonDocument.Parse(
        JsonSerializer.Serialize(new
        {
            action = "sync",
            companyId = operation.CompanyId,
            integrationId = operation.IntegrationId,
            operationGeneration = operation.Generation,
            operationLeaseToken = operation.LeaseToken,
            providerAccountReference = operation.ProviderAccountReference,
            cursor,
            maxPages = 5,
            maxDurationSeconds = 60,
        }));

    internal static bool CameraSafetyPollingDue(object? configJson, DateTimeOffset now)
    {
        var stored = ConnectorRegistry.RedactConfig(configJson);
        if (!stored.TryGetValue("cameraSafetyAutoSync", out var preference) ||
            !string.Equals(preference?.ToString(), "enabled", StringComparison.Ordinal))
            return false;

        if (!stored.TryGetValue("cameraSafetyLastCompletedAt", out var completedValue) ||
            !DateTimeOffset.TryParse(
                completedValue?.ToString(),
                CultureInfo.InvariantCulture,
                DateTimeStyles.RoundtripKind,
                out var completedAt) ||
            completedAt.Offset != TimeSpan.Zero)
            return true;

        var lastOk = stored.TryGetValue("cameraSafetyLastOk", out var okValue) &&
                     okValue is bool ok && ok;
        var cadence = lastOk ? Interval : CameraFailureInterval;
        return now.ToUniversalTime() - completedAt >= cadence;
    }

    internal static JsonDocument BuildCameraSafetyOperationBody(
        ConnectorOperationContext operation,
        object? configJson,
        DateTimeOffset now,
        out string startTime,
        out string? cursor)
    {
        var stored = ConnectorRegistry.RedactConfig(configJson);
        cursor = stored.TryGetValue("cameraSafetyCursor", out var cursorValue) &&
                 !string.IsNullOrWhiteSpace(cursorValue?.ToString())
            ? cursorValue!.ToString()
            : null;
        startTime = stored.TryGetValue("cameraSafetyStartTime", out var startValue) &&
                    DateTimeOffset.TryParse(
                        startValue?.ToString(),
                        CultureInfo.InvariantCulture,
                        DateTimeStyles.RoundtripKind,
                        out var parsedStart) &&
                    parsedStart.Offset == TimeSpan.Zero
            ? parsedStart.ToString("O")
            : now.ToUniversalTime().AddHours(-24).ToString("O");

        return JsonDocument.Parse(JsonSerializer.Serialize(new
        {
            action = "sync-camera-safety",
            companyId = operation.CompanyId,
            integrationId = operation.IntegrationId,
            operationGeneration = operation.Generation,
            operationLeaseToken = operation.LeaseToken,
            providerAccountReference = operation.ProviderAccountReference,
            cursor,
            startTime,
            maxPages = 5,
            maxDurationSeconds = 60,
        }));
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
