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
    private const string AutomaticResolutionActor = "system:geofence-reentry";

    internal static async Task EvaluateAsync(Database db, CancellationToken ct = default)
    {
        // GIS: both zone shapes evaluate — circular (center+radius, haversine) and polygon
        // (polygon_json vertex ring, ray-casting point-in-polygon) for docks/yards/irregular sites.
        var fences = await db.QueryAsync(
            @"SELECT id, company_id, branch_id, center_lat, center_lng, radius_meters, polygon_json
              FROM geofences
              WHERE status='Active'
                AND ((center_lat IS NOT NULL AND center_lng IS NOT NULL AND radius_meters IS NOT NULL)
                     OR polygon_json IS NOT NULL)",
            ct: ct);
        if (fences.Count == 0) return;

        var positions = await db.QueryAsync(
            @"SELECT lvp.vehicle_id, lvp.company_id, v.branch_id, lvp.lat, lvp.lng
              FROM latest_vehicle_positions lvp
              JOIN vehicles v ON v.id=lvp.vehicle_id AND v.company_id=lvp.company_id AND v.deleted_at IS NULL
              WHERE lvp.lat IS NOT NULL AND lvp.lng IS NOT NULL",
            ct: ct);
        if (positions.Count == 0) return;

        // Current inside/outside state per (geofence, vehicle), derived from the most recent event.
        var states = await db.QueryAsync(
            @"SELECT DISTINCT ON (geofence_id, vehicle_id) geofence_id, vehicle_id, event_type
              FROM geofence_events ORDER BY geofence_id, vehicle_id, event_time DESC, id DESC",
            ct: ct);
        var lastEvent = new Dictionary<(long, long), string>();
        foreach (var s in states)
            lastEvent[(Convert.ToInt64(s["geofenceId"]), Convert.ToInt64(s["vehicleId"]))] = s["eventType"]?.ToString() ?? "";

        foreach (var f in fences)
        {
            var gid    = Convert.ToInt64(f["id"]);
            var fcid   = Convert.ToInt64(f["companyId"]);
            var fenceBranch = f["branchId"] is null or DBNull ? (long?)null : Convert.ToInt64(f["branchId"]);
            var polygon = ParsePolygon(f["polygonJson"]?.ToString());
            var hasCircle = f["centerLat"] is not null and not DBNull && f["centerLng"] is not null and not DBNull && f["radiusMeters"] is not null and not DBNull;
            if (polygon is null && !hasCircle) continue;
            var clat   = hasCircle ? Convert.ToDouble(f["centerLat"]) : 0;
            var clng   = hasCircle ? Convert.ToDouble(f["centerLng"]) : 0;
            var radius = hasCircle ? Convert.ToDouble(f["radiusMeters"]) : 0;

            foreach (var p in positions)
            {
                if (Convert.ToInt64(p["companyId"]) != fcid) continue;
                var vehicleBranch = p["branchId"] is null or DBNull ? (long?)null : Convert.ToInt64(p["branchId"]);
                if (fenceBranch is not null && vehicleBranch != fenceBranch) continue;
                var vid = Convert.ToInt64(p["vehicleId"]);
                var lat = Convert.ToDouble(p["lat"]);
                var lng = Convert.ToDouble(p["lng"]);
                // A polygon zone, when present, is the authoritative boundary; else the circle.
                var inside = polygon is not null
                    ? PointInPolygon(lat, lng, polygon)
                    : DistanceMeters(clat, clng, lat, lng) <= radius;
                lastEvent.TryGetValue((gid, vid), out var last);

                if (inside && last != "Entry")
                    await EmitAsync(db, fcid, gid, vid, "Entry", ct);
                else if (!inside && last == "Entry")
                    await EmitAsync(db, fcid, gid, vid, "Exit", ct);
            }
        }
    }

    // Polygon ring from polygon_json: accepts [[lat,lng],...] or [{"lat":..,"lng":..},...].
    internal static List<(double Lat, double Lng)>? ParsePolygon(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return null;
        try
        {
            using var doc = System.Text.Json.JsonDocument.Parse(json);
            var ring = new List<(double, double)>();
            foreach (var el in doc.RootElement.EnumerateArray())
            {
                if (el.ValueKind == System.Text.Json.JsonValueKind.Array && el.GetArrayLength() >= 2)
                    ring.Add((el[0].GetDouble(), el[1].GetDouble()));
                else if (el.ValueKind == System.Text.Json.JsonValueKind.Object
                         && el.TryGetProperty("lat", out var la) && el.TryGetProperty("lng", out var lo))
                    ring.Add((la.GetDouble(), lo.GetDouble()));
            }
            return ring.Count >= 3 ? ring : null;
        }
        catch { return null; }   // malformed polygon: fail-closed to the circle (or skip)
    }

    // Ray casting: odd number of edge crossings => inside.
    internal static bool PointInPolygon(double lat, double lng, List<(double Lat, double Lng)> ring)
    {
        var inside = false;
        for (int i = 0, j = ring.Count - 1; i < ring.Count; j = i++)
        {
            var (yi, xi) = (ring[i].Lat, ring[i].Lng);
            var (yj, xj) = (ring[j].Lat, ring[j].Lng);
            if (((yi > lat) != (yj > lat)) &&
                (lng < (xj - xi) * (lat - yi) / (yj - yi) + xi))
                inside = !inside;
        }
        return inside;
    }

    // Alert-oriented membership evaluation. Active tenant/branch fences form one allowed-area
    // set: inside any valid circle or polygon is authorized, and outside every valid fence is a
    // breach. Returning null when no valid fence exists avoids fabricating an alert from bad data.
    internal static async Task<Dictionary<string, object?>?> FindAuthorizedAreaBreachAsync(
        Database db,
        long companyId,
        long? branchId,
        double lat,
        double lng,
        CancellationToken ct = default)
    {
        var fences = await db.QueryAsync(
            @"SELECT id,name,center_lat,center_lng,radius_meters,polygon_json
              FROM geofences
              WHERE company_id=@cid AND status='Active'
                AND (branch_id IS NULL OR branch_id=@branchId)",
            c =>
            {
                c.Parameters.AddWithValue("@cid", companyId);
                c.Parameters.AddWithValue("@branchId", (object?)branchId ?? DBNull.Value);
            }, ct);

        Dictionary<string, object?>? firstValidFence = null;
        foreach (var fence in fences)
        {
            var isValid = false;
            var isInside = false;
            var polygon = ParsePolygon(fence.GetValueOrDefault("polygonJson")?.ToString());
            if (polygon is not null)
            {
                isValid = true;
                isInside = PointInPolygon(lat, lng, polygon);
            }
            else if (TryReadFiniteDouble(fence.GetValueOrDefault("centerLat"), out var centerLat)
                     && TryReadFiniteDouble(fence.GetValueOrDefault("centerLng"), out var centerLng)
                     && TryReadFiniteDouble(fence.GetValueOrDefault("radiusMeters"), out var radiusMeters)
                     && radiusMeters > 0)
            {
                isValid = true;
                isInside = DistanceMeters(lat, lng, centerLat, centerLng) <= radiusMeters;
            }

            if (!isValid) continue;
            firstValidFence ??= new Dictionary<string, object?>
            {
                ["id"] = fence.GetValueOrDefault("id"),
                ["name"] = fence.GetValueOrDefault("name")
            };
            if (isInside) return null;
        }

        return firstValidFence;
    }

    // Project every accepted, monotonic position while it is still in the ingest transaction.
    // The background evaluator remains a reconciliation safety net, but this path preserves
    // transitions that occur between its polling cycles and uses the device event time as evidence.
    internal static async Task<Dictionary<string, object?>?> ProjectPositionAsync(
        Database db,
        long companyId,
        long? branchId,
        long vehicleId,
        double lat,
        double lng,
        DateTimeOffset eventTime,
        CancellationToken ct = default)
    {
        var fences = await db.QueryAsync(
            @"SELECT id,name,center_lat,center_lng,radius_meters,polygon_json
              FROM geofences
              WHERE company_id=@cid AND status='Active'
                AND (branch_id IS NULL OR branch_id=@branchId)",
            c =>
            {
                c.Parameters.AddWithValue("@cid", companyId);
                c.Parameters.AddWithValue("@branchId", (object?)branchId ?? DBNull.Value);
            }, ct);

        var states = await db.QueryAsync(
            @"SELECT DISTINCT ON (geofence_id) geofence_id,event_type
              FROM geofence_events
              WHERE company_id=@cid AND vehicle_id=@vid
              ORDER BY geofence_id,event_time DESC,id DESC",
            c =>
            {
                c.Parameters.AddWithValue("@cid", companyId);
                c.Parameters.AddWithValue("@vid", vehicleId);
            }, ct);
        var lastEvent = states.ToDictionary(
            s => Convert.ToInt64(s["geofenceId"]),
            s => s["eventType"]?.ToString() ?? "");

        Dictionary<string, object?>? firstValidFence = null;
        var insideAny = false;
        foreach (var fence in fences)
        {
            var polygon = ParsePolygon(fence.GetValueOrDefault("polygonJson")?.ToString());
            bool inside;
            if (polygon is not null)
            {
                inside = PointInPolygon(lat, lng, polygon);
            }
            else if (TryReadFiniteDouble(fence.GetValueOrDefault("centerLat"), out var centerLat)
                     && TryReadFiniteDouble(fence.GetValueOrDefault("centerLng"), out var centerLng)
                     && TryReadFiniteDouble(fence.GetValueOrDefault("radiusMeters"), out var radiusMeters)
                     && radiusMeters > 0)
            {
                inside = DistanceMeters(lat, lng, centerLat, centerLng) <= radiusMeters;
            }
            else
            {
                continue;
            }

            var geofenceId = Convert.ToInt64(fence["id"]);
            firstValidFence ??= new Dictionary<string, object?>
            {
                ["id"] = fence.GetValueOrDefault("id"),
                ["name"] = fence.GetValueOrDefault("name")
            };
            lastEvent.TryGetValue(geofenceId, out var last);
            if (inside && last != "Entry")
                await EmitAsync(db, companyId, geofenceId, vehicleId, "Entry", eventTime, ct);
            else if (!inside && last == "Entry")
                await EmitAsync(db, companyId, geofenceId, vehicleId, "Exit", eventTime, ct);

            insideAny |= inside;
        }

        if (firstValidFence is null) return null;
        if (!insideAny) return firstValidFence;

        // Re-entry is objective telemetry evidence, so it closes both open and acknowledged
        // breach alerts. A later exit can then create a new alert with its own source event.
        await db.ExecuteAsync(
            @"UPDATE telemetry_alerts
              SET status='Resolved',resolved_at=NOW(),resolved_by=@actor,updated_at=NOW()
              WHERE company_id=@cid AND vehicle_id=@vid AND alert_type='geofence_breach'
                AND status IN ('Open','Acknowledged')",
            c =>
            {
                c.Parameters.AddWithValue("@actor", AutomaticResolutionActor);
                c.Parameters.AddWithValue("@cid", companyId);
                c.Parameters.AddWithValue("@vid", vehicleId);
            }, ct);
        return null;
    }

    private static bool TryReadFiniteDouble(object? raw, out double value)
    {
        value = 0;
        if (raw is null or DBNull) return false;
        try
        {
            value = Convert.ToDouble(raw, System.Globalization.CultureInfo.InvariantCulture);
            return double.IsFinite(value);
        }
        catch (Exception ex) when (ex is FormatException or InvalidCastException or OverflowException)
        {
            return false;
        }
    }

    private static Task EmitAsync(Database db, long companyId, long geofenceId, long vehicleId, string eventType, CancellationToken ct) =>
        EmitAsync(db, companyId, geofenceId, vehicleId, eventType, null, ct);

    private static Task EmitAsync(Database db, long companyId, long geofenceId, long vehicleId, string eventType, DateTimeOffset? eventTime, CancellationToken ct) =>
        db.ExecuteAsync(
            @"INSERT INTO geofence_events (company_id, geofence_id, vehicle_id, event_type, event_time)
              VALUES (@c, @g, @v, @t, COALESCE(@eventTime,NOW()))",
            c =>
            {
                c.Parameters.AddWithValue("@c", companyId);
                c.Parameters.AddWithValue("@g", geofenceId);
                c.Parameters.AddWithValue("@v", vehicleId);
                c.Parameters.AddWithValue("@t", eventType);
                c.Parameters.AddWithValue("@eventTime", eventTime?.UtcDateTime ?? (object)DBNull.Value);
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
