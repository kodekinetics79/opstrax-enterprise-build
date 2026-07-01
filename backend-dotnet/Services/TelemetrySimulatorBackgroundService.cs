using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Opstrax.Api.Data;

namespace Opstrax.Api.Services;

// ── Live GPS Simulator ────────────────────────────────────────────────────────
// Demo/dev only. Every few seconds it nudges latest_vehicle_positions so the Live
// Fleet Map shows genuine motion: "moving" units glide along their heading, "idle"
// units stay parked but fresh, and "offline" units (engine_status='Off') are left
// untouched so they age into the stale/Offline bucket.
//
// The SSE stream reads latest_vehicle_positions every 3s, and the frontend merges
// those rows onto the map by vehicle_code — so updating this one table is all that's
// needed for live movement. No external device feed required.
//
// Gated by config so it never runs in a real deployment:
//   Telemetry:Simulator:Enabled          (bool,   default true)
//   Telemetry:Simulator:IntervalSeconds  (int,    default 4)
//   Telemetry:Simulator:SpeedMultiplier  (double, default 20  — exaggerates ground
//                                          distance so motion is visible at city zoom)
public sealed class TelemetrySimulatorBackgroundService(
    IServiceScopeFactory scopeFactory,
    ILogger<TelemetrySimulatorBackgroundService> logger,
    ServiceRunTracker tracker,
    IConfiguration config) : BackgroundService
{
    private const string SvcName = "TelemetrySimulatorBackgroundService";

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var enabled = config.GetValue("Telemetry:Simulator:Enabled", true);
        if (!enabled)
        {
            logger.LogInformation("{Svc} disabled by configuration", SvcName);
            return;
        }

        var intervalSeconds = Math.Clamp(config.GetValue("Telemetry:Simulator:IntervalSeconds", 4), 2, 60);
        var speedMultiplier = config.GetValue("Telemetry:Simulator:SpeedMultiplier", 20.0);
        var interval = TimeSpan.FromSeconds(intervalSeconds);

        // Let schema migrations / position seed settle first.
        await Task.Delay(TimeSpan.FromSeconds(20), stoppingToken).ContinueWith(_ => { }, stoppingToken);

        logger.LogInformation("{Svc} started — tick {Interval}s, speed x{Mult}", SvcName, intervalSeconds, speedMultiplier);

        while (!stoppingToken.IsCancellationRequested)
        {
            var sw    = System.Diagnostics.Stopwatch.StartNew();
            var runId = await tracker.BeginAsync(SvcName, stoppingToken);
            try
            {
                // Cross-tenant worker (updates all-company positions): run the tick under
                // the platform-admin bypass scope so it functions as the restricted role.
                var moved = 0;
                using (var tickScope = scopeFactory.CreateScope())
                {
                    var tickDb = tickScope.ServiceProvider.GetRequiredService<Database>();
                    await tickDb.RunInSystemScopeAsync(async () =>
                    {
                        moved = await TickAsync(intervalSeconds, speedMultiplier, stoppingToken);
                    }, stoppingToken);
                }
                sw.Stop();
                await tracker.CompleteAsync(runId, SvcName, moved, (int)sw.ElapsedMilliseconds, stoppingToken);
            }
            catch (OperationCanceledException) { break; }
            catch (Exception ex)
            {
                sw.Stop();
                logger.LogError(ex, "{Svc} tick failed", SvcName);
                await tracker.FailAsync(runId, SvcName, ex, (int)sw.ElapsedMilliseconds, stoppingToken);
            }

            try { await Task.Delay(interval, stoppingToken); }
            catch (OperationCanceledException) { break; }
        }
    }

    private async Task<int> TickAsync(int dtSeconds, double speedMultiplier, CancellationToken ct)
    {
        using var scope = scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<Database>();

        // One batch, three steps:
        //  1. Advance moving units along their heading; drift heading/speed for life;
        //     1 deg lat ≈ 69 mi, 1 deg lng ≈ 69·cos(lat) mi. RHS uses pre-update values.
        //  2. Keep idle units fresh (parked, not stale) without moving them.
        //  3. Bounce any unit that wandered off the continental box back inward.
        var affected = await db.ExecuteAsync(
            @"UPDATE latest_vehicle_positions SET
                  lat = (lat + ((speed_mph * @dt / 3600.0 * @mult) / 69.0)
                               * COS(RADIANS(heading)))::numeric(10,7),
                  lng = (lng + ((speed_mph * @dt / 3600.0 * @mult)
                               / (69.0 * GREATEST(COS(RADIANS(lat)), 0.2)))
                               * SIN(RADIANS(heading)))::numeric(10,7),
                  heading   = ((heading + (random() * 24 - 12)::int + 360) % 360)::smallint,
                  speed_mph = LEAST(70, GREATEST(20, speed_mph + (random() * 6 - 3)))::numeric(6,2),
                  fuel_level = GREATEST(8, fuel_level - 0.05)::numeric(6,2),
                  event_time = NOW(), received_at = NOW(), event_count = event_count + 1
              WHERE engine_status = 'On';

              UPDATE latest_vehicle_positions SET
                  event_time = NOW(), received_at = NOW()
              WHERE engine_status = 'Idle';

              UPDATE latest_vehicle_positions SET
                  heading = ((heading + 180) % 360)::smallint
              WHERE engine_status = 'On'
                AND (lat NOT BETWEEN 25 AND 49 OR lng NOT BETWEEN -125 AND -67);",
            c =>
            {
                c.Parameters.AddWithValue("@dt", dtSeconds);
                c.Parameters.AddWithValue("@mult", speedMultiplier);
            }, ct);

        return affected;
    }
}
