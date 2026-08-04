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
//   Telemetry:Simulator:Enabled          (bool,   default false)
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
        var enabled = config.GetValue("Telemetry:Simulator:Enabled", false);
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

        // Additive provenance: the simulator stamps source='simulator' on the rows it
        // bootstraps, ONLY when the columns exist (deploy-safe — production may pre-date
        // migration 001, and the simulator is demo/dev anyway). Probed once (cached).
        var hasProv = await TelemetryProvenance.ColumnsAvailableAsync(db, ct);
        var bootstrapProvCols   = hasProv ? ", source, normalized_at" : "";
        var bootstrapProvSelect = hasProv ? $", '{TelemetryProvenance.SourceSimulator}', NOW()" : "";

        // Step 0 — Bootstrap coverage. Any vehicle that is assigned/on-road and has a
        // recent location_events fix but NO latest_vehicle_positions row is seeded one
        // from its most recent event. Without this, the simulator only ever moved the
        // handful of vehicles the DB seed happened to include (leaving the rest of the
        // fleet frozen at Offline). This makes movement fleet-wide and self-healing as
        // new vehicles/events appear. Scoped to non-OOS units with a real GPS fix.
        await db.ExecuteAsync(
            $@"INSERT INTO latest_vehicle_positions
                  (company_id, vehicle_id, device_id, driver_id, lat, lng, speed_mph, heading,
                   engine_status, fuel_level, odometer_miles, event_time, received_at, event_count,
                   telemetry_status, risk_level, updated_at{bootstrapProvCols})
              SELECT DISTINCT ON (le.vehicle_id)
                     le.company_id, le.vehicle_id, le.device_id, le.driver_id,
                     le.lat, le.lng,
                     COALESCE(le.speed_mph, 32), COALESCE(le.heading, (random()*360)::int),
                     CASE WHEN COALESCE(le.engine_status,'') IN ('Moving','Idle') THEN le.engine_status ELSE 'Moving' END,
                     COALESCE(le.fuel_level, 70), COALESCE(le.odometer_miles, 0),
                     NOW(), NOW(), 1, 'healthy', 'low', NOW(){bootstrapProvSelect}
              FROM location_events le
              JOIN vehicles v ON v.id = le.vehicle_id AND v.deleted_at IS NULL
              WHERE le.vehicle_id IS NOT NULL AND le.lat IS NOT NULL AND le.lng IS NOT NULL
                AND COALESCE(v.status,'') NOT IN ('Maintenance','Out of Service')
                AND NOT EXISTS (
                    SELECT 1 FROM latest_vehicle_positions p
                    WHERE p.company_id = le.company_id AND p.vehicle_id = le.vehicle_id)
              ORDER BY le.vehicle_id, le.event_time DESC
              ON CONFLICT DO NOTHING;

              -- Wake a realistic majority of assigned units so the board reads as an
              -- active fleet; keep ~25% idle/off for honesty. Deterministic by id.
              -- Match ANY non-active state (Off, NULL, '', or any legacy value that isn't
              -- Moving/Idle) — earlier this only caught 'Off', so vehicles seeded with a
              -- NULL/empty engine_status stayed frozen and the map looked dead. This makes
              -- the demo self-heal to a lively fleet on any DB state, in every environment.
              UPDATE latest_vehicle_positions SET engine_status='Moving',
                     speed_mph=GREATEST(COALESCE(speed_mph,0), 28), received_at=NOW(), event_time=NOW()
              WHERE COALESCE(NULLIF(TRIM(engine_status), ''), 'Off') NOT IN ('Moving','Idle')
                AND lat IS NOT NULL AND lng IS NOT NULL
                AND (vehicle_id % 4) <> 0;", ct: ct);

        // Step 1 — advance the fleet. Moving units glide along their heading and drift
        // speed/heading for life; idle units stay parked but fresh; any unit that
        // wandered off the continental box is bounced back inward.
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
              WHERE engine_status = 'Moving';

              UPDATE latest_vehicle_positions SET
                  event_time = NOW(), received_at = NOW()
              WHERE engine_status = 'Idle';

              UPDATE latest_vehicle_positions SET
                  heading = ((heading + 180) % 360)::smallint
              WHERE engine_status = 'Moving'
                AND (lat NOT BETWEEN 25 AND 49 OR lng NOT BETWEEN -125 AND -67);

              -- Breadcrumb: append a real location_events row from every fresh position,
              -- then prune simulator breadcrumbs older than 2h. Folded into this same
              -- batch (which runs in the system/platform-admin scope) so the INSERT
              -- passes RLS WITH CHECK — a separate ExecuteAsync round-trip did not.
              INSERT INTO location_events
                  (company_id, vehicle_id, driver_id, device_id, lat, lng, speed_mph, heading,
                   engine_status, fuel_level, odometer_miles, event_type, event_time, received_at, source)
              SELECT company_id, vehicle_id, driver_id, device_id, lat, lng, speed_mph, heading,
                     engine_status, fuel_level, odometer_miles, 'position', NOW(), NOW(), 'simulator'
              FROM latest_vehicle_positions
              WHERE engine_status IN ('Moving','Idle') AND received_at > NOW() - INTERVAL '10 seconds';

              DELETE FROM location_events
              WHERE source='simulator' AND event_time < NOW() - INTERVAL '2 hours';

              -- Refresh the derived live-state that GPS Tracking, OBD/J1939 and Device
              -- Health read (telemetry_live_asset_states). Done in-batch (system scope) so
              -- it passes RLS — the per-tenant service refresh ran outside the scope and
              -- silently no-op'd. Status/risk mirror TelemetryLiveStateService's rules:
              -- stale > 900s → stale/high; speeding > 65 → watch/medium; else healthy/low.
              UPDATE telemetry_live_asset_states s SET
                  lat = p.lat, lng = p.lng, speed_mph = p.speed_mph, heading = p.heading,
                  engine_status = p.engine_status,
                  stale_seconds = EXTRACT(EPOCH FROM (NOW() - p.received_at))::bigint,
                  last_event_time = p.event_time, received_at = p.received_at,
                  telemetry_status = CASE
                      WHEN EXTRACT(EPOCH FROM (NOW() - p.received_at)) > 900 THEN 'stale'
                      WHEN p.speed_mph > 65 THEN 'watch' ELSE 'healthy' END,
                  risk_level = CASE
                      WHEN EXTRACT(EPOCH FROM (NOW() - p.received_at)) > 900 THEN 'high'
                      WHEN p.speed_mph > 65 THEN 'medium' ELSE 'low' END,
                  next_action = CASE
                      WHEN EXTRACT(EPOCH FROM (NOW() - p.received_at)) > 900 THEN 'Check device heartbeat and field power'
                      WHEN p.speed_mph > 65 THEN 'Review speeding and driver coaching'
                      ELSE 'No action required' END,
                  updated_at = NOW()
              FROM latest_vehicle_positions p
              WHERE s.company_id = p.company_id AND s.vehicle_id = p.vehicle_id;

              -- Insert live-state for freshly-bootstrapped vehicles that have none yet.
              INSERT INTO telemetry_live_asset_states
                  (company_id, vehicle_id, device_id, driver_id, vehicle_code, driver_name,
                   lat, lng, speed_mph, heading, engine_status, telemetry_status, risk_level,
                   stale_seconds, last_event_time, received_at, next_action, updated_at)
              SELECT p.company_id, p.vehicle_id, p.device_id, p.driver_id, v.vehicle_code, d.full_name,
                     p.lat, p.lng, p.speed_mph, p.heading, p.engine_status,
                     CASE WHEN p.speed_mph > 65 THEN 'watch' ELSE 'healthy' END,
                     CASE WHEN p.speed_mph > 65 THEN 'medium' ELSE 'low' END,
                     0, p.event_time, p.received_at,
                     CASE WHEN p.speed_mph > 65 THEN 'Review speeding and driver coaching' ELSE 'No action required' END,
                     NOW()
              FROM latest_vehicle_positions p
              JOIN vehicles v ON v.id = p.vehicle_id
              LEFT JOIN drivers d ON d.id = p.driver_id
              WHERE NOT EXISTS (
                  SELECT 1 FROM telemetry_live_asset_states s
                  WHERE s.company_id = p.company_id AND s.vehicle_id = p.vehicle_id);",
            c =>
            {
                c.Parameters.AddWithValue("@dt", dtSeconds);
                c.Parameters.AddWithValue("@mult", speedMultiplier);
            }, ct);

        return affected;
    }
}
