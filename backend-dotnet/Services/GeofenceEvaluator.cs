using Opstrax.Api.Data;

namespace Opstrax.Api.Services;

// Geofence entry/exit engine. The geofence dashboard reads geofence_events, but nothing ever wrote them —
// the whole feature was dead (audit P1/P2). This evaluates each vehicle's latest position against every
// active CIRCULAR geofence and emits debounced Entry/Exit events: an Entry only when a vehicle that was
// last seen outside (or never seen) is now inside, and an Exit only when a vehicle last seen inside is now
// outside. Debouncing off the last event means a vehicle parked inside a zone does not re-emit every tick.
// (Polygon geofences carry polygon_json and are a follow-up; circular is the common case.)
public static class GeofenceEvaluator
{
    private const double EarthRadiusMeters = 6_371_000d;

    internal static async Task EvaluateAsync(Database db, CancellationToken ct = default)
    {
        var fences = await db.QueryAsync(
            @"SELECT id, company_id, center_lat, center_lng, radius_meters
              FROM geofences
              WHERE status='Active' AND center_lat IS NOT NULL AND center_lng IS NOT NULL AND radius_meters IS NOT NULL",
            ct: ct);
        if (fences.Count == 0) return;

        var positions = await db.QueryAsync(
            "SELECT vehicle_id, company_id, lat, lng FROM latest_vehicle_positions WHERE lat IS NOT NULL AND lng IS NOT NULL",
            ct: ct);
        if (positions.Count == 0) return;

        // Current inside/outside state per (geofence, vehicle), derived from the most recent event.
        var states = await db.QueryAsync(
            @"SELECT DISTINCT ON (geofence_id, vehicle_id) geofence_id, vehicle_id, event_type
              FROM geofence_events ORDER BY geofence_id, vehicle_id, event_time DESC",
            ct: ct);
        var lastEvent = new Dictionary<(long, long), string>();
        foreach (var s in states)
            lastEvent[(Convert.ToInt64(s["geofenceId"]), Convert.ToInt64(s["vehicleId"]))] = s["eventType"]?.ToString() ?? "";

        foreach (var f in fences)
        {
            var gid    = Convert.ToInt64(f["id"]);
            var fcid   = Convert.ToInt64(f["companyId"]);
            var clat   = Convert.ToDouble(f["centerLat"]);
            var clng   = Convert.ToDouble(f["centerLng"]);
            var radius = Convert.ToDouble(f["radiusMeters"]);

            foreach (var p in positions)
            {
                if (Convert.ToInt64(p["companyId"]) != fcid) continue;
                var vid = Convert.ToInt64(p["vehicleId"]);
                var inside = DistanceMeters(clat, clng, Convert.ToDouble(p["lat"]), Convert.ToDouble(p["lng"])) <= radius;
                lastEvent.TryGetValue((gid, vid), out var last);

                if (inside && last != "Entry")
                    await EmitAsync(db, fcid, gid, vid, "Entry", ct);
                else if (!inside && last == "Entry")
                    await EmitAsync(db, fcid, gid, vid, "Exit", ct);
            }
        }
    }

    private static Task EmitAsync(Database db, long companyId, long geofenceId, long vehicleId, string eventType, CancellationToken ct) =>
        db.ExecuteAsync(
            @"INSERT INTO geofence_events (company_id, geofence_id, vehicle_id, event_type, event_time)
              VALUES (@c, @g, @v, @t, NOW())",
            c =>
            {
                c.Parameters.AddWithValue("@c", companyId);
                c.Parameters.AddWithValue("@g", geofenceId);
                c.Parameters.AddWithValue("@v", vehicleId);
                c.Parameters.AddWithValue("@t", eventType);
            }, ct);

    // Great-circle distance in metres (haversine).
    private static double DistanceMeters(double lat1, double lon1, double lat2, double lon2)
    {
        double Rad(double d) => d * Math.PI / 180d;
        var dLat = Rad(lat2 - lat1);
        var dLon = Rad(lon2 - lon1);
        var a = Math.Sin(dLat / 2) * Math.Sin(dLat / 2) +
                Math.Cos(Rad(lat1)) * Math.Cos(Rad(lat2)) * Math.Sin(dLon / 2) * Math.Sin(dLon / 2);
        return EarthRadiusMeters * 2 * Math.Atan2(Math.Sqrt(a), Math.Sqrt(1 - a));
    }
}
