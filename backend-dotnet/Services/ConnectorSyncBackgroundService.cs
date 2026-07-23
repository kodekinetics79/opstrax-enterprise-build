using Opstrax.Api.Data;
using Opstrax.Api.Services.Connectors;

namespace Opstrax.Api.Services;

// Automatic third-party position sync — the 'keep your Samsara' overlay path. Every tick, each
// CONNECTED integration whose connector implements a real 'sync' action (e.g. Samsara -> live
// positions) runs an incremental pull with its stored cursor, so a tenant who connects an API key
// gets continuous positions -> geofence events -> detention detection with zero manual syncs.
// Failures mark the integration 'Error' and never block other tenants' syncs.
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
                using var scope = scopeFactory.CreateScope();
                var db = scope.ServiceProvider.GetRequiredService<Database>();
                var connectors = scope.ServiceProvider.GetRequiredService<ConnectorRegistry>();
                await db.RunInSystemScopeAsync(() => SyncOnceAsync(db, connectors, stoppingToken), stoppingToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogWarning(ex, "Connector sync tick failed; next tick retries");
            }
            await Task.Delay(Interval, stoppingToken).ContinueWith(_ => { }, stoppingToken);
        }
    }

    internal static async Task SyncOnceAsync(Database db, ConnectorRegistry connectors, CancellationToken ct)
    {
        var rows = await db.QueryAsync(
            @"SELECT id, company_id, integration_key, config_json FROM integrations
              WHERE status='Connected' AND integration_key = ANY(@keys)",
            c => c.Parameters.AddWithValue("@keys", SyncCapable), ct);

        foreach (var row in rows)
        {
            var id = Convert.ToInt64(row["id"]);
            var companyId = Convert.ToInt64(row["companyId"]);
            try
            {
                var connector = connectors.Resolve(row["integrationKey"]?.ToString());
                var config = connectors.DecryptConfig(row["configJson"]);
                var stored = ConnectorRegistry.RedactConfig(row["configJson"]);
                var cursor = stored.TryGetValue("syncCursor", out var cv) ? cv?.ToString() : null;

                using var body = System.Text.Json.JsonDocument.Parse(
                    System.Text.Json.JsonSerializer.Serialize(new { action = "sync", companyId, cursor }));
                var result = await connector.RunActionAsync("sync", config, body.RootElement, ct);

                var nextCursor = result.Details?.GetValueOrDefault("nextCursor")?.ToString();
                await db.ExecuteAsync(
                    @"UPDATE integrations SET
                          status = CASE WHEN @ok THEN 'Connected' ELSE 'Error' END,
                          last_sync_at = CASE WHEN @ok THEN NOW() ELSE last_sync_at END,
                          config_json = CASE WHEN @cursor IS NULL THEN config_json
                                             ELSE COALESCE(config_json,'{}'::jsonb) || jsonb_build_object('syncCursor', @cursor::text) END,
                          updated_at = NOW()
                      WHERE company_id=@cid AND id=@id",
                    c =>
                    {
                        c.Parameters.AddWithValue("@cid", companyId); c.Parameters.AddWithValue("@id", id);
                        c.Parameters.AddWithValue("@ok", result.Success);
                        c.Parameters.AddWithValue("@cursor", (object?)nextCursor ?? DBNull.Value);
                    }, ct);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                await db.ExecuteAsync(
                    "UPDATE integrations SET status='Error', updated_at=NOW() WHERE company_id=@cid AND id=@id",
                    c => { c.Parameters.AddWithValue("@cid", companyId); c.Parameters.AddWithValue("@id", id); }, ct);
            }
        }
    }
}
