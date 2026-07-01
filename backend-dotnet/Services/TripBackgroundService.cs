using System.Text.Json;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Npgsql;
using NpgsqlTypes;
using Opstrax.Api.Data;

namespace Opstrax.Api.Services;

// Runs every 5 minutes to:
//   1. Auto-create trips from active routes that have an assigned vehicle.
//   2. Bind location_events for that vehicle to the trip.
//   3. Detect stop completion by bounding-box proximity (≈300 m).
//   4. Compute route compliance score from timing + stop data + telemetry gaps.
//   5. Generate route_deviation safety events for overdue uncompleted stops.
//   6. Mark trips completed/exception when the parent route is done.
public sealed class TripBackgroundService(
    Database db, ILogger<TripBackgroundService> log, ServiceRunTracker tracker)
    : BackgroundService
{
    private static readonly TimeSpan Interval = TimeSpan.FromMinutes(5);
    private const string SvcName = "TripBackgroundService";

    // Stop proximity bounding box: ≈300 m at mid-US latitudes.
    private const decimal LatBoxDeg = 0.003m;   // ~330 m north-south
    private const decimal LngBoxDeg = 0.004m;   // ~310 m east-west at 38°N

    // A stop is "late" when vehicle has not arrived and time_window_end has passed by this margin.
    private static readonly TimeSpan LateStopGrace = TimeSpan.FromMinutes(30);

    // A safety event of type route_deviation is generated once per overdue uncompleted stop.
    // Compliance deductions:
    //   Start delay   — -10 if >15 min late to depart
    //   Missed stop   — -15 per stop not completed when route is done (or time_window_end expired)
    //   Late stop     — -5  per stop where actual_arrival_time > time_window_end
    //   Telemetry gap — -10 if any gap between consecutive events > 15 min (hard-signal loss)
    //   Speeding      — -3  per speeding telemetry_alert during the trip window

    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        // Let app fully start before first run.
        await Task.Delay(TimeSpan.FromSeconds(20), ct);
        while (!ct.IsCancellationRequested)
        {
            var sw    = System.Diagnostics.Stopwatch.StartNew();
            var runId = await tracker.BeginAsync(SvcName, ct);
            try
            {
                // Cross-tenant worker (all-company routes, filtered by company_id):
                // run the whole tick under the platform-admin bypass scope.
                await db.RunInSystemScopeAsync(() => RunCycleAsync(ct), ct);
                sw.Stop();
                await tracker.CompleteAsync(runId, SvcName, 0, (int)sw.ElapsedMilliseconds, ct);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                sw.Stop();
                log.LogError(ex, "{Svc} cycle failed", SvcName);
                await tracker.FailAsync(runId, SvcName, ex, (int)sw.ElapsedMilliseconds, ct);
            }
            await Task.Delay(Interval, ct);
        }
    }

    private async Task RunCycleAsync(CancellationToken ct)
    {
        // Step 1 — upsert trips for every active route with an assigned vehicle.
        await CreateTripsFromActiveRoutesAsync(ct);

        // Step 2 — bind unassigned location_events to their active trip.
        await BindLocationEventsAsync(ct);

        // Step 3 — detect stop completion by proximity.
        await DetectStopCompletionsAsync(ct);

        // Step 4 — compute compliance scores for all active trips.
        await ComputeComplianceAsync(ct);

        // Step 5 — generate route_deviation safety events for overdue stops.
        await GenerateDeviationAlertsAsync(ct);

        // Step 6 — close trips whose parent route is completed/cancelled.
        await CloseFinalizedTripsAsync(ct);
    }

    // ── Step 1: Create trips ──────────────────────────────────────────────────────
    private async Task CreateTripsFromActiveRoutesAsync(CancellationToken ct)
    {
        var routes = await db.QueryAsync(
            @"SELECT r.id, r.company_id, r.assigned_vehicle_id, r.assigned_driver_id,
                     r.planned_start, r.planned_end, r.route_name, r.status,
                     r.estimated_distance, r.estimated_duration_minutes,
                     rs_first.address AS origin,
                     rs_last.address  AS destination,
                     COUNT(rs.id)     AS total_stops
              FROM routes r
              LEFT JOIN route_stops rs ON rs.route_id=r.id
              LEFT JOIN route_stops rs_first ON rs_first.route_id=r.id
                    AND rs_first.stop_sequence=(SELECT MIN(s2.stop_sequence) FROM route_stops s2 WHERE s2.route_id=r.id)
              LEFT JOIN route_stops rs_last ON rs_last.route_id=r.id
                    AND rs_last.stop_sequence=(SELECT MAX(s3.stop_sequence) FROM route_stops s3 WHERE s3.route_id=r.id)
              WHERE r.status='Active' AND r.assigned_vehicle_id IS NOT NULL
              GROUP BY r.id, rs_first.address, rs_last.address",
            ct: ct);

        foreach (var route in routes)
        {
            var routeId   = Convert.ToInt64(route["id"]);
            var companyId = Convert.ToInt64(route["companyId"]);
            var vehicleId = Convert.ToInt64(route["assignedVehicleId"]);
            var driverId  = route["assignedDriverId"] is null ? (long?)null : Convert.ToInt64(route["assignedDriverId"]);

            // Create trip if one doesn't already exist for this route.
            var existingId = await db.ScalarLongAsync(
                "SELECT id FROM trips WHERE route_id=@rid AND status IN ('planned','active') LIMIT 1",
                c => c.Parameters.AddWithValue("@rid", routeId), ct);

            if (existingId > 0) continue;

            var tripId = await db.InsertAsync(
                @"INSERT INTO trips
                    (company_id, driver_id, vehicle_id, route_id,
                     status, planned_start_time, planned_end_time,
                     origin, destination,
                     planned_distance_miles, planned_duration_minutes, total_planned_stops)
                  VALUES (@cid, @did, @vid, @rid,
                          'planned', @pstart, @pend,
                          @origin, @dest,
                          @pdist, @pdur, @tstops)",
                c =>
                {
                    c.Parameters.AddWithValue("@cid",    companyId);
                    c.Parameters.AddWithValue("@did",    (object?)driverId ?? DBNull.Value);
                    c.Parameters.AddWithValue("@vid",    vehicleId);
                    c.Parameters.AddWithValue("@rid",    routeId);
                    c.Parameters.AddWithValue("@pstart", route["plannedStart"] ?? (object)DBNull.Value);
                    c.Parameters.AddWithValue("@pend",   route["plannedEnd"]   ?? (object)DBNull.Value);
                    c.Parameters.AddWithValue("@origin", route["origin"]       ?? (object)DBNull.Value);
                    c.Parameters.AddWithValue("@dest",   route["destination"]  ?? (object)DBNull.Value);
                    c.Parameters.AddWithValue("@pdist",  route["estimatedDistance"]        ?? (object)DBNull.Value);
                    c.Parameters.AddWithValue("@pdur",   route["estimatedDurationMinutes"] ?? (object)DBNull.Value);
                    c.Parameters.AddWithValue("@tstops", route["totalStops"] ?? 0);
                }, ct);

            // Set human-readable trip reference.
            await db.ExecuteAsync(
                "UPDATE trips SET trip_ref=CONCAT('TRP-',id) WHERE id=@id",
                c => c.Parameters.AddWithValue("@id", tripId), ct);

            // Seed trip_stops from route_stops for this route.
            await db.ExecuteAsync(
                @"INSERT INTO trip_stops
                    (company_id, trip_id, route_stop_id, stop_sequence, stop_type,
                     address, lat, lng, time_window_start, time_window_end,
                     planned_arrival_time, status)
                  SELECT @cid, @tid, rs.id, rs.stop_sequence,
                         COALESCE(rs.stop_type,'Delivery'),
                         rs.address,
                         COALESCE(rs.latitude, rs.lat),
                         COALESCE(rs.longitude, rs.lng),
                         rs.time_window_start, rs.time_window_end,
                         rs.eta, 'pending'
                  FROM route_stops rs
                  WHERE rs.route_id=@rid
                  ORDER BY rs.stop_sequence",
                c =>
                {
                    c.Parameters.AddWithValue("@cid", companyId);
                    c.Parameters.AddWithValue("@tid", tripId);
                    c.Parameters.AddWithValue("@rid", routeId);
                }, ct);

            log.LogInformation("[TripBgSvc] Created trip {TripId} for route {RouteId}", tripId, routeId);
        }
    }

    // ── Step 2: Bind location_events to trips ─────────────────────────────────────
    private async Task BindLocationEventsAsync(CancellationToken ct)
    {
        // For each active trip, bind unclaimed location_events for the trip's vehicle
        // that fall within the trip time window (actual_start_time → now, or planned start → now).
        await db.ExecuteAsync(
            @"UPDATE location_events le
              SET trip_id = t.id
              FROM trips t
              WHERE t.vehicle_id=le.vehicle_id
                AND t.company_id=le.company_id
                AND t.status IN ('planned','active')
                AND le.event_time >= COALESCE(t.actual_start_time, t.planned_start_time, t.created_at)
                AND le.event_time <= NOW()
                AND le.trip_id IS NULL",
            ct: ct);

        // Auto-activate trips that now have at least one bound location_event.
        await db.ExecuteAsync(
            @"UPDATE trips
              SET status='active',
                  actual_start_time=COALESCE(actual_start_time,
                    (SELECT MIN(le2.event_time) FROM location_events le2 WHERE le2.trip_id=trips.id))
              WHERE status='planned'
                AND EXISTS (SELECT 1 FROM location_events le3 WHERE le3.trip_id=trips.id)",
            ct: ct);

        // Assign trip_sequence (order within trip) to newly bound events.
        // Using variables for per-trip sequencing.
        await db.ExecuteAsync(
            @"UPDATE location_events le
              SET trip_sequence=ranked.seq
              FROM (
                SELECT id, ROW_NUMBER() OVER (PARTITION BY trip_id ORDER BY event_time ASC) AS seq
                FROM location_events WHERE trip_id IS NOT NULL AND trip_sequence IS NULL
              ) ranked
              WHERE ranked.id=le.id
                AND le.trip_id IS NOT NULL AND le.trip_sequence IS NULL",
            ct: ct);
    }

    // ── Step 3: Detect stop completions by bounding-box proximity ─────────────────
    private async Task DetectStopCompletionsAsync(CancellationToken ct)
    {
        // Find pending stops where a location_event exists within the bounding box.
        // Mark as completed and record the earliest matching event time as actual_arrival_time.
        var stopsToComplete = await db.QueryAsync(
            @"SELECT ts.id AS stop_id, ts.trip_id, ts.time_window_end,
                     MIN(le.event_time) AS arrival_time
              FROM trip_stops ts
              JOIN trips t ON t.id=ts.trip_id AND t.status='active'
              JOIN location_events le ON le.trip_id=ts.trip_id
                   AND ABS(le.lat - ts.lat) < @latbox
                   AND ABS(le.lng - ts.lng) < @lngbox
              WHERE ts.status='pending'
                AND ts.lat IS NOT NULL AND ts.lng IS NOT NULL
              GROUP BY ts.id, ts.trip_id, ts.time_window_end",
            c =>
            {
                c.Parameters.AddWithValue("@latbox", LatBoxDeg);
                c.Parameters.AddWithValue("@lngbox", LngBoxDeg);
            }, ct);

        foreach (var stop in stopsToComplete)
        {
            var stopId      = Convert.ToInt64(stop["stopId"]);
            var arrivalTime = stop["arrivalTime"];
            var windowEnd   = stop["timeWindowEnd"];

            int delayMinutes = 0;
            if (arrivalTime is not null && windowEnd is not null)
            {
                var arrival = Convert.ToDateTime(arrivalTime);
                var winEnd  = Convert.ToDateTime(windowEnd);
                if (arrival > winEnd)
                    delayMinutes = (int)(arrival - winEnd).TotalMinutes;
            }

            await db.ExecuteAsync(
                @"UPDATE trip_stops
                  SET status='completed', actual_arrival_time=@arr, arrival_delay_minutes=@delay, updated_at=NOW()
                  WHERE id=@id AND status='pending'",
                c =>
                {
                    c.Parameters.AddWithValue("@arr",   arrivalTime ?? (object)DBNull.Value);
                    c.Parameters.AddWithValue("@delay", delayMinutes);
                    c.Parameters.AddWithValue("@id",    stopId);
                }, ct);
        }

        // Update trip-level counts for completed/on-time stops.
        await db.ExecuteAsync(
            @"UPDATE trips
              SET stops_completed = (SELECT COUNT(*) FROM trip_stops ts WHERE ts.trip_id=trips.id AND ts.status='completed'),
                  stops_on_time   = (SELECT COUNT(*) FROM trip_stops ts WHERE ts.trip_id=trips.id AND ts.status='completed' AND ts.arrival_delay_minutes=0)
              WHERE status='active'",
            ct: ct);
    }

    // ── Step 4: Compute compliance score ─────────────────────────────────────────
    internal static async Task<(decimal Score, string BreakdownJson)> ComputeComplianceAsync(
        Database db, long tripId, CancellationToken ct)
    {
        decimal deductions    = 0;
        var breakdownParts    = new Dictionary<string, object>();

        var trip = (await db.QueryAsync(
            @"SELECT t.planned_start_time, t.actual_start_time, t.total_planned_stops,
                     t.stops_completed, t.stops_on_time,
                     t.company_id, t.vehicle_id,
                     (SELECT COUNT(*) FROM trip_stops WHERE trip_id=t.id AND arrival_delay_minutes > 0) AS late_stops
              FROM trips t WHERE t.id=@id LIMIT 1",
            c => c.Parameters.AddWithValue("@id", tripId), ct)).FirstOrDefault();

        if (trip is null) return (100m, "{}");

        var companyId = Convert.ToInt64(trip["companyId"]);
        var vehicleId = trip["vehicleId"] is null ? (long?)null : Convert.ToInt64(trip["vehicleId"]);

        // ── Start delay ──
        int startDelayMinutes = 0;
        if (trip["plannedStartTime"] is not null && trip["actualStartTime"] is not null)
        {
            var planned = Convert.ToDateTime(trip["plannedStartTime"]);
            var actual  = Convert.ToDateTime(trip["actualStartTime"]);
            if (actual > planned)
                startDelayMinutes = (int)(actual - planned).TotalMinutes;
        }
        if (startDelayMinutes > 15)
        {
            deductions += 10;
            breakdownParts["start_delay"] = new { minutes = startDelayMinutes, deduction = 10 };
        }

        // ── Missed stops ──
        int totalStops     = Convert.ToInt32(trip["totalPlannedStops"] ?? 0);
        int completedStops = Convert.ToInt32(trip["stopsCompleted"]    ?? 0);
        int missedStops    = Math.Max(0, totalStops - completedStops);
        // Only count as "missed" (not just "pending") if time_window_end is expired.
        var expiredUncompleted = await db.ScalarLongAsync(
            @"SELECT COUNT(*) FROM trip_stops
              WHERE trip_id=@tid AND status='pending'
                AND time_window_end IS NOT NULL AND time_window_end < NOW()",
            c => c.Parameters.AddWithValue("@tid", tripId), ct);
        if (expiredUncompleted > 0)
        {
            var missedDeduction = expiredUncompleted * 15m;
            deductions += missedDeduction;
            breakdownParts["missed_stops"] = new { count = expiredUncompleted, deduction = missedDeduction };
        }

        // ── Late stop arrivals ──
        int lateStops = Convert.ToInt32(trip["lateStops"] ?? 0);
        if (lateStops > 0)
        {
            var lateDeduction = lateStops * 5m;
            deductions += lateDeduction;
            breakdownParts["late_stops"] = new { count = lateStops, deduction = lateDeduction };
        }

        // ── Telemetry continuity — max gap between consecutive events ──
        if (vehicleId.HasValue)
        {
            var maxGapMinutes = await db.ScalarLongAsync(
                @"SELECT COALESCE(MAX(gap_mins),0) FROM (
                    SELECT (EXTRACT(EPOCH FROM (event_time - LAG(event_time) OVER (ORDER BY event_time))) / 60)::BIGINT AS gap_mins
                    FROM location_events WHERE trip_id=@tid
                  ) g WHERE gap_mins IS NOT NULL",
                c => c.Parameters.AddWithValue("@tid", tripId), ct);

            if (maxGapMinutes > 15)
            {
                deductions += 10;
                breakdownParts["telemetry_gap"] = new { maxGapMinutes, deduction = 10 };
            }
        }

        // ── Speeding alerts during trip window ──
        var speedingCount = await db.ScalarLongAsync(
            @"SELECT COUNT(*) FROM telemetry_alerts ta
              JOIN trips t ON t.id=@tid
              WHERE ta.company_id=@cid
                AND ta.vehicle_id=t.vehicle_id
                AND ta.alert_type IN ('speeding','repeated_speeding')
                AND ta.created_at >= COALESCE(t.actual_start_time, t.planned_start_time, t.created_at)
                AND ta.created_at <= COALESCE(t.actual_end_time, NOW())",
            c =>
            {
                c.Parameters.AddWithValue("@tid", tripId);
                c.Parameters.AddWithValue("@cid", companyId);
            }, ct);
        if (speedingCount > 0)
        {
            var speedDeduction = speedingCount * 3m;
            deductions += speedDeduction;
            breakdownParts["speeding_events"] = new { count = speedingCount, deduction = speedDeduction };
        }

        var score = Math.Round(Math.Max(0m, Math.Min(100m, 100m - deductions)), 2);
        var breakdownJson = JsonSerializer.Serialize(breakdownParts);
        return (score, breakdownJson);
    }

    // Runs compliance computation for all active trips.
    private async Task ComputeComplianceAsync(CancellationToken ct)
    {
        var activeTrips = await db.QueryAsync(
            "SELECT id FROM trips WHERE status='active'", ct: ct);

        foreach (var trip in activeTrips)
        {
            var tripId = Convert.ToInt64(trip["id"]);
            var (score, breakdown) = await ComputeComplianceAsync(db, tripId, ct);

            await db.ExecuteAsync(
                @"UPDATE trips SET route_compliance_score=@score,
                         compliance_breakdown_json=@json,
                         speeding_events_count=COALESCE((@json::jsonb->'speeding_events'->>'count')::int, 0),
                         updated_at=NOW()
                  WHERE id=@id",
                c =>
                {
                    c.Parameters.AddWithValue("@score", score);
                    c.Parameters.Add(new NpgsqlParameter("@json", NpgsqlDbType.Jsonb) { Value = breakdown });
                    c.Parameters.AddWithValue("@id",    tripId);
                }, ct);
        }
    }

    // ── Step 5: Generate route_deviation safety alerts ────────────────────────────
    private async Task GenerateDeviationAlertsAsync(CancellationToken ct)
    {
        // Conditions for a route_deviation event:
        //   (a) Active trip with at least one location_event bound.
        //   (b) A trip_stop is pending + time_window_end expired > LateStopGrace.
        //   (c) Latest vehicle position is NOT near the stop (outside bounding box).
        //   (d) No open route_deviation safety event already exists for this trip+stop.
        var overdue = await db.QueryAsync(
            @"SELECT ts.id AS stop_id, ts.trip_id, ts.stop_sequence,
                     t.company_id, t.driver_id, t.vehicle_id,
                     v.vehicle_code, d.full_name AS driver_name,
                     ts.lat AS stop_lat, ts.lng AS stop_lng, ts.address,
                     lvp.lat AS cur_lat, lvp.lng AS cur_lng
              FROM trip_stops ts
              JOIN trips t ON t.id=ts.trip_id AND t.status='active'
              LEFT JOIN vehicles v ON v.id=t.vehicle_id
              LEFT JOIN drivers d ON d.id=t.driver_id
              LEFT JOIN latest_vehicle_positions lvp ON lvp.vehicle_id=t.vehicle_id
              WHERE ts.status='pending'
                AND ts.time_window_end IS NOT NULL
                AND ts.time_window_end < NOW() - 30 * INTERVAL '1 minute'
                AND ts.lat IS NOT NULL AND ts.lng IS NOT NULL",
            ct: ct);

        foreach (var stop in overdue)
        {
            var companyId   = Convert.ToInt64(stop["companyId"]);
            var vehicleId   = stop["vehicleId"]  is null ? (long?)null : Convert.ToInt64(stop["vehicleId"]);
            var tripId      = Convert.ToInt64(stop["tripId"]);
            var stopId      = Convert.ToInt64(stop["stopId"]);
            var stopLat     = stop["stopLat"]  is null ? (decimal?)null : Convert.ToDecimal(stop["stopLat"]);
            var stopLng     = stop["stopLng"]  is null ? (decimal?)null : Convert.ToDecimal(stop["stopLng"]);
            var curLat      = stop["curLat"]   is null ? (decimal?)null : Convert.ToDecimal(stop["curLat"]);
            var curLng      = stop["curLng"]   is null ? (decimal?)null : Convert.ToDecimal(stop["curLng"]);
            var vehicleCode = stop["vehicleCode"]?.ToString() ?? "";
            var driverName  = stop["driverName"]?.ToString() ?? "Unknown Driver";

            // Skip if vehicle is currently near the stop.
            if (stopLat.HasValue && stopLng.HasValue && curLat.HasValue && curLng.HasValue)
            {
                if (Math.Abs(curLat.Value - stopLat.Value) < LatBoxDeg &&
                    Math.Abs(curLng.Value - stopLng.Value) < LngBoxDeg)
                    continue; // vehicle is actually at or near the stop
            }

            // Check for existing open deviation alert for this trip+stop.
            var existingAlert = await db.ScalarLongAsync(
                @"SELECT COUNT(*) FROM safety_events
                  WHERE company_id=@cid AND event_type='route_deviation'
                    AND status NOT IN ('resolved','dismissed')
                    AND meta_json->>'tripId' = @tid
                    AND meta_json->>'stopId' = @sid",
                c =>
                {
                    c.Parameters.AddWithValue("@cid", companyId);
                    c.Parameters.AddWithValue("@tid", tripId.ToString());
                    c.Parameters.AddWithValue("@sid", stopId.ToString());
                }, ct);

            if (existingAlert > 0) continue;

            // Create the safety event.
            var metaJson = JsonSerializer.Serialize(new
            {
                tripId   = tripId,
                stopId   = stopId,
                address  = stop["address"]?.ToString(),
                stopLat  = stopLat,
                stopLng  = stopLng,
            });

            await db.InsertAsync(
                @"INSERT INTO safety_events
                    (company_id, driver_id, vehicle_id, event_type, severity, status,
                     occurred_at, description, meta_json, score_impact, system_insight)
                  SELECT @cid, t.driver_id, t.vehicle_id,
                         'route_deviation', 'High', 'open',
                         NOW(),
                         @desc, @meta, 10,
                         @insight
                  FROM trips t WHERE t.id=@tid",
                c =>
                {
                    c.Parameters.AddWithValue("@cid",     companyId);
                    c.Parameters.AddWithValue("@tid",     tripId);
                    c.Parameters.AddWithValue("@desc",    $"Route deviation: {vehicleCode} has not arrived at stop {stop["stopSequence"]} ({stop["address"] ?? "unknown address"}) within the scheduled time window.");
                    c.Parameters.AddWithValue("@meta",    metaJson);
                    c.Parameters.AddWithValue("@insight", $"Rule-Based Safety Insight: {driverName} deviated from the assigned route. Vehicle {vehicleCode} has not reached the planned stop address '{stop["address"] ?? ""}' within the time window. Review for unauthorized detour or delay.");
                }, ct);

            // Flag the stop as deviation.
            await db.ExecuteAsync(
                "UPDATE trip_stops SET deviation_flagged=1, updated_at=NOW() WHERE id=@id",
                c => c.Parameters.AddWithValue("@id", stopId), ct);

            log.LogWarning("[TripBgSvc] Route deviation event created: trip={TripId} stop={StopId} vehicle={Vehicle}", tripId, stopId, vehicleCode);
        }
    }

    // ── Step 6: Finalize completed/cancelled trips ────────────────────────────────
    private async Task CloseFinalizedTripsAsync(CancellationToken ct)
    {
        // Routes that are 'Completed' → mark trips 'completed'.
        await db.ExecuteAsync(
            @"UPDATE trips
              SET status='completed',
                  actual_end_time=COALESCE(trips.actual_end_time, NOW()),
                  actual_duration_minutes=(EXTRACT(EPOCH FROM (NOW() - trips.actual_start_time)) / 60)::BIGINT,
                  updated_at=NOW()
              FROM routes r
              WHERE r.id=trips.route_id AND trips.status='active' AND r.status='Completed'",
            ct: ct);

        // Routes that are 'Cancelled' → mark trips 'cancelled'.
        await db.ExecuteAsync(
            @"UPDATE trips
              SET status='cancelled', updated_at=NOW()
              FROM routes r
              WHERE r.id=trips.route_id AND trips.status IN ('planned','active') AND r.status='Cancelled'",
            ct: ct);

        // Compute actual distance from odometer for newly completed trips.
        await db.ExecuteAsync(
            @"UPDATE trips
              SET actual_distance_miles=od.dist
              FROM (
                SELECT trip_id,
                       MAX(odometer_miles) - MIN(odometer_miles) AS dist
                FROM location_events
                WHERE trip_id IS NOT NULL AND odometer_miles IS NOT NULL
                GROUP BY trip_id
              ) od
              WHERE od.trip_id=trips.id
                AND trips.status='completed' AND trips.actual_distance_miles IS NULL AND od.dist > 0",
            ct: ct);
    }
}
