using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Opstrax.Api.Data;
using Opstrax.Api.Foundation;

namespace Opstrax.Api.Services;

// Runs every 5 minutes independently of SSE client connections.
// Generates stale_device alerts and prunes expired nonces.
public sealed class TelemetryBackgroundService(
    IServiceScopeFactory scopeFactory,
    ILogger<TelemetryBackgroundService> logger,
    ServiceRunTracker tracker) : BackgroundService
{
    private static readonly TimeSpan CheckInterval = TimeSpan.FromMinutes(5);
    private const string SvcName = "TelemetryBackgroundService";

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Brief startup delay so schema migrations complete first
        await Task.Delay(TimeSpan.FromSeconds(30), stoppingToken).ContinueWith(_ => { }, stoppingToken);

        while (!stoppingToken.IsCancellationRequested)
        {
            var sw    = System.Diagnostics.Stopwatch.StartNew();
            var runId = await tracker.BeginAsync(SvcName, stoppingToken);
            try
            {
                await CheckStaleDevicesAsync(stoppingToken);
                await PruneExpiredNoncesAsync(stoppingToken);
                sw.Stop();
                await tracker.CompleteAsync(runId, SvcName, 0, (int)sw.ElapsedMilliseconds, stoppingToken);
            }
            catch (OperationCanceledException) { break; }
            catch (Exception ex)
            {
                sw.Stop();
                logger.LogError(ex, "{Svc} tick failed", SvcName);
                await tracker.FailAsync(runId, SvcName, ex, (int)sw.ElapsedMilliseconds, stoppingToken);
            }

            try { await Task.Delay(CheckInterval, stoppingToken); }
            catch (OperationCanceledException) { break; }
        }
    }

    private async Task CheckStaleDevicesAsync(CancellationToken ct)
    {
        using var scope = scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<Database>();
        var telemetry = scope.ServiceProvider.GetRequiredService<TelemetryLiveStateService>();
        var ai = scope.ServiceProvider.GetRequiredService<PostgresAiFoundationService>();

        // Join telemetry_rules to get per-tenant stale threshold (default 900s = 15 min)
        var stale = await db.QueryAsync(
            @"SELECT lvp.company_id, lvp.vehicle_id, lvp.device_id,
                     EXTRACT(EPOCH FROM (NOW() - lvp.received_at))::BIGINT seconds_stale,
                     COALESCE(tr.threshold_value, 900) stale_threshold
              FROM latest_vehicle_positions lvp
              LEFT JOIN telemetry_rules tr
                ON tr.company_id=lvp.company_id AND tr.rule_type='stale_device' AND tr.enabled=TRUE
              WHERE EXTRACT(EPOCH FROM (NOW() - lvp.received_at))::BIGINT > COALESCE(tr.threshold_value, 900)",
            ct: ct);

        var companiesToRefresh = new HashSet<long>();
        foreach (var pos in stale)
        {
            var companyId = Convert.ToInt64(pos["companyId"]);
            var vehicleId = Convert.ToInt64(pos["vehicleId"]);
            var seconds   = Convert.ToInt64(pos["secondsStale"]);
            companiesToRefresh.Add(companyId);

            // Idempotent: skip if an open stale_device alert already exists
            var open = await db.ScalarLongAsync(
                "SELECT COUNT(*) FROM telemetry_alerts WHERE company_id=@cid AND vehicle_id=@vid AND alert_type='stale_device' AND status='Open'",
                c => { c.Parameters.AddWithValue("@cid", companyId); c.Parameters.AddWithValue("@vid", vehicleId); }, ct);

            if (open == 0)
            {
                var alertId = await db.InsertAsync(
                    @"INSERT INTO telemetry_alerts (company_id, vehicle_id, device_id, alert_type, severity, message, status)
                      VALUES (@cid, @vid, @did, 'stale_device', 'Warning', @msg, 'Open')
                      RETURNING id",
                    c =>
                    {
                        c.Parameters.AddWithValue("@cid", companyId);
                        c.Parameters.AddWithValue("@vid", vehicleId);
                        c.Parameters.AddWithValue("@did", pos["deviceId"] ?? (object)DBNull.Value);
                        c.Parameters.AddWithValue("@msg", $"No telemetry received for {seconds / 60} minutes (background check)");
                    }, ct);

                logger.LogInformation("Stale-device alert created: company={CompanyId} vehicle={VehicleId}", companyId, vehicleId);

                var recommendation = ai.CreateRecommendation(
                    companyId.ToString(),
                    "telemetry.stale_device",
                    $"Stale telemetry for vehicle {vehicleId}",
                    "No telemetry has been received within the configured stale window.",
                    0.76m,
                    0.94m,
                    System.Text.Json.JsonSerializer.Serialize(new { companyId, vehicleId, secondsStale = seconds, threshold = 900 }),
                    System.Text.Json.JsonSerializer.Serialize(new { reason = "stale telemetry alert", secondsStale = seconds }),
                    System.Text.Json.JsonSerializer.Serialize(new { action = "check_device_connectivity", vehicleId, companyId }),
                    "high",
                    alertId.ToString(),
                    ActorTypes.System,
                    "telemetry-background");

                await db.ExecuteAsync(
                    "UPDATE telemetry_alerts SET ai_recommendation_id=@rid, updated_at=NOW() WHERE id=@id AND company_id=@cid",
                    c =>
                    {
                        c.Parameters.AddWithValue("@rid", recommendation.Id);
                        c.Parameters.AddWithValue("@id", alertId);
                        c.Parameters.AddWithValue("@cid", companyId);
                    }, ct);

                await telemetry.RefreshVehicleAsync(companyId, vehicleId, ct);
            }
        }

        foreach (var companyId in companiesToRefresh)
        {
            await telemetry.RefreshCompanyAsync(companyId, ct);
        }
    }

    private async Task PruneExpiredNoncesAsync(CancellationToken ct)
    {
        using var scope = scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<Database>();
        // Remove nonces older than 24 h — beyond any replay window
        var deleted = await db.ExecuteAsync(
            "DELETE FROM telemetry_nonces WHERE used_at < NOW() - 24 * INTERVAL '1 hour'",
            ct: ct);
        if (deleted > 0)
            logger.LogDebug("Pruned {Count} expired telemetry nonces", deleted);
    }
}
