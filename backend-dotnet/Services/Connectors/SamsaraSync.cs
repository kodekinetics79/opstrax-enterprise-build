using System.Text.Json;
using Opstrax.Api.Data;

namespace Opstrax.Api.Services.Connectors;

// The Samsara → OpsTrax data pipeline. Pulls the vehicle stats feed and writes GPS
// into the live-position tables the map reads. Kept separate from the connector so
// it is unit-focused: one public RunAsync that does fetch → match → write → refresh.
public sealed class SamsaraSync(HttpClient client, IServiceScopeFactory scopeFactory, ILogger logger)
{
    public sealed record SyncSummary(int VehiclesSeen, int PositionsWritten, int Unmatched, string? NextCursor, bool HasNextPage);

    private sealed record SamsaraGps(string VehicleId, string? Name, double Lat, double Lng, double SpeedMph, int Heading, DateTime EventTime, double? OdometerMiles, string? EngineState);

    public async Task<SyncSummary> RunAsync(long companyId, string? afterCursor, CancellationToken ct)
    {
        // 1. Pull one page of the stats feed (gps + engine + odometer). Cursor makes it incremental.
        var url = "/fleet/vehicles/stats/feed?types=gps,engineStates,obdOdometerMeters";
        if (!string.IsNullOrWhiteSpace(afterCursor)) url += $"&after={Uri.EscapeDataString(afterCursor!)}";

        using var resp = await client.GetAsync(url, ct);
        resp.EnsureSuccessStatusCode();
        using var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync(ct));

        var readings = ParseFeed(doc.RootElement);
        string? nextCursor = null;
        var hasNext = false;
        if (doc.RootElement.TryGetProperty("pagination", out var pg))
        {
            nextCursor = pg.TryGetProperty("endCursor", out var ec) ? ec.GetString() : null;
            hasNext = pg.TryGetProperty("hasNextPage", out var hn) && hn.GetBoolean();
        }

        if (readings.Count == 0)
            return new SyncSummary(0, 0, 0, nextCursor, hasNext);

        // 2/3. Match + write inside a system scope (cross-tenant background write under RLS),
        //      then refresh the live-asset projection so the map/SSE update.
        using var scope = scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<Database>();
        var telemetry = scope.ServiceProvider.GetService<TelemetryLiveStateService>();

        var written = 0;
        var unmatched = 0;
        var touchedVehicles = new HashSet<long>();

        await db.RunInSystemScopeAsync(async () =>
        {
            foreach (var r in readings)
            {
                // Match the Samsara vehicle to an OpsTrax vehicle via an eld_devices row.
                // The Samsara vehicle id is stored as the device_serial. We upsert the
                // device row (provider='Samsara') so the mapping self-heals; a device
                // with no vehicle_id yet only lands history (location_events).
                var (deviceId, vehicleId) = await ResolveDeviceAsync(db, companyId, r, ct);

                // History (breadcrumbs) — always, even when unmatched.
                await db.ExecuteAsync(
                    @"INSERT INTO location_events
                        (company_id, vehicle_id, device_id, lat, lng, speed_mph, heading,
                         event_type, engine_status, odometer_miles, source, source_channel,
                         event_time, received_at)
                      VALUES (@cid, @vid, @did, @lat, @lng, @spd, @hdg, 'ping', @eng, @odo,
                              'samsara', 'samsara-api', @etime, NOW())",
                    c =>
                    {
                        c.Parameters.AddWithValue("@cid", companyId);
                        c.Parameters.AddWithValue("@vid", (object?)vehicleId ?? DBNull.Value);
                        c.Parameters.AddWithValue("@did", (object?)deviceId ?? DBNull.Value);
                        c.Parameters.AddWithValue("@lat", (decimal)r.Lat);
                        c.Parameters.AddWithValue("@lng", (decimal)r.Lng);
                        c.Parameters.AddWithValue("@spd", (decimal)r.SpeedMph);
                        c.Parameters.AddWithValue("@hdg", (short)Math.Clamp(r.Heading, 0, 359));
                        c.Parameters.AddWithValue("@eng", (object?)r.EngineState ?? DBNull.Value);
                        c.Parameters.AddWithValue("@odo", (object?)(r.OdometerMiles is { } o ? (decimal)o : (object?)null) ?? DBNull.Value);
                        c.Parameters.AddWithValue("@etime", r.EventTime);
                    }, ct);

                if (vehicleId is null) { unmatched++; continue; }

                // Live snapshot — the UPSERT the map reads. Mirrors the ingest handler.
                await db.ExecuteAsync(
                    @"INSERT INTO latest_vehicle_positions
                        (company_id, vehicle_id, device_id, lat, lng, speed_mph, heading,
                         engine_status, odometer_miles, event_time, received_at, event_count,
                         source_channel, telemetry_status, risk_level, updated_at)
                      VALUES (@cid, @vid, @did, @lat, @lng, @spd, @hdg, @eng, @odo, @etime, NOW(), 1,
                              'samsara-api', 'healthy', 'low', NOW())
                      ON CONFLICT (company_id, vehicle_id) DO UPDATE SET
                        device_id=EXCLUDED.device_id, lat=EXCLUDED.lat, lng=EXCLUDED.lng,
                        speed_mph=EXCLUDED.speed_mph, heading=EXCLUDED.heading,
                        engine_status=EXCLUDED.engine_status, odometer_miles=EXCLUDED.odometer_miles,
                        event_time=EXCLUDED.event_time, received_at=EXCLUDED.received_at,
                        event_count=latest_vehicle_positions.event_count+1,
                        source_channel=EXCLUDED.source_channel, telemetry_status='healthy',
                        risk_level='low', updated_at=NOW()",
                    c =>
                    {
                        c.Parameters.AddWithValue("@cid", companyId);
                        c.Parameters.AddWithValue("@vid", vehicleId.Value);
                        c.Parameters.AddWithValue("@did", (object?)deviceId ?? DBNull.Value);
                        c.Parameters.AddWithValue("@lat", (decimal)r.Lat);
                        c.Parameters.AddWithValue("@lng", (decimal)r.Lng);
                        c.Parameters.AddWithValue("@spd", (decimal)r.SpeedMph);
                        c.Parameters.AddWithValue("@hdg", (short)Math.Clamp(r.Heading, 0, 359));
                        c.Parameters.AddWithValue("@eng", (object?)r.EngineState ?? "Running");
                        c.Parameters.AddWithValue("@odo", (object?)(r.OdometerMiles is { } o ? (decimal)o : (object?)null) ?? DBNull.Value);
                        c.Parameters.AddWithValue("@etime", r.EventTime);
                    }, ct);

                written++;
                touchedVehicles.Add(vehicleId.Value);
            }
        }, ct);

        // 4. Refresh the live-asset projection + push SSE so the map reflects Samsara data.
        if (telemetry is not null)
        {
            try
            {
                foreach (var vid in touchedVehicles)
                    await telemetry.RefreshVehicleAsync(companyId, vid, ct);
            }
            catch (Exception ex) { logger.LogWarning(ex, "Samsara live-state refresh failed for company {Company}", companyId); }
        }

        return new SyncSummary(readings.Count, written, unmatched, nextCursor, hasNext);
    }

    // Upsert the eld_devices row for a Samsara vehicle (keyed by device_serial=Samsara
    // vehicle id) and return (numeric device id, linked vehicle_id or null).
    private static async Task<(long? deviceId, long? vehicleId)> ResolveDeviceAsync(Database db, long companyId, SamsaraGps r, CancellationToken ct)
    {
        var serial = $"samsara-{r.VehicleId}";
        // Insert-if-absent (provider Samsara). Never overwrites an existing mapping.
        // Status is 'Provisioning', NOT 'Active': a Samsara device is an external data
        // SOURCE we pull from — it does not authenticate via our HMAC ingest path, so it
        // has no api_key_hash/hmac_secret and cannot be 'Active' (a check constraint,
        // ck_eld_devices_active_credentials, enforces that Active devices carry real
        // credentials). Provisioning correctly reflects "linked, externally sourced".
        await db.ExecuteAsync(
            @"INSERT INTO eld_devices (company_id, device_serial, provider, status, last_seen_at)
              SELECT @cid, @serial, 'Samsara', 'Provisioning', NOW()
              WHERE NOT EXISTS (SELECT 1 FROM eld_devices WHERE company_id=@cid AND device_serial=@serial)",
            c => { c.Parameters.AddWithValue("@cid", companyId); c.Parameters.AddWithValue("@serial", serial); }, ct);

        var row = await db.QuerySingleAsync(
            "SELECT id, vehicle_id FROM eld_devices WHERE company_id=@cid AND device_serial=@serial LIMIT 1",
            c => { c.Parameters.AddWithValue("@cid", companyId); c.Parameters.AddWithValue("@serial", serial); }, ct);
        if (row is null) return (null, null);
        var deviceId = row.TryGetValue("id", out var idv) && idv is not null ? Convert.ToInt64(idv) : (long?)null;
        var vehicleId = row.TryGetValue("vehicleId", out var vv) && vv is not null ? Convert.ToInt64(vv) : (long?)null;
        // Best-effort heartbeat.
        if (deviceId is not null)
            await db.ExecuteAsync("UPDATE eld_devices SET last_seen_at=NOW() WHERE id=@id",
                c => c.Parameters.AddWithValue("@id", deviceId.Value), ct);
        return (deviceId, vehicleId);
    }

    // Parse the Samsara stats-feed response into flat GPS readings. Shape:
    // { data: [ { id, name, gps:{time,latitude,longitude,headingDegrees,speedMilesPerHour},
    //             engineStates:{value|time}, obdOdometerMeters:{value} } ] }
    private static List<SamsaraGps> ParseFeed(JsonElement root)
    {
        var list = new List<SamsaraGps>();
        if (!root.TryGetProperty("data", out var data) || data.ValueKind != JsonValueKind.Array) return list;
        foreach (var v in data.EnumerateArray())
        {
            var id = v.TryGetProperty("id", out var idEl) ? idEl.GetString() : null;
            if (string.IsNullOrWhiteSpace(id)) continue;
            var name = v.TryGetProperty("name", out var nEl) ? nEl.GetString() : null;
            if (!v.TryGetProperty("gps", out var gps) || gps.ValueKind != JsonValueKind.Object) continue;

            double lat = gps.TryGetProperty("latitude", out var la) && la.TryGetDouble(out var laV) ? laV : double.NaN;
            double lng = gps.TryGetProperty("longitude", out var lo) && lo.TryGetDouble(out var loV) ? loV : double.NaN;
            if (double.IsNaN(lat) || double.IsNaN(lng)) continue; // no real fix → skip (no fabrication)

            double speed = gps.TryGetProperty("speedMilesPerHour", out var sp) && sp.TryGetDouble(out var spV) ? spV : 0;
            int heading = gps.TryGetProperty("headingDegrees", out var hd) && hd.TryGetInt32(out var hdV) ? hdV : 0;
            DateTime time = gps.TryGetProperty("time", out var tm) && tm.ValueKind == JsonValueKind.String && DateTime.TryParse(tm.GetString(), out var t)
                ? t.ToUniversalTime() : DateTime.UtcNow;

            double? odoMiles = null;
            if (v.TryGetProperty("obdOdometerMeters", out var odo) && odo.TryGetProperty("value", out var ov) && ov.TryGetDouble(out var meters))
                odoMiles = meters / 1609.344; // meters → miles

            string? engine = null;
            if (v.TryGetProperty("engineStates", out var es))
                engine = es.ValueKind == JsonValueKind.Object && es.TryGetProperty("value", out var ev) ? ev.GetString()
                       : es.ValueKind == JsonValueKind.String ? es.GetString() : null;

            list.Add(new SamsaraGps(id!, name, lat, lng, speed, heading, time, odoMiles, engine));
        }
        return list;
    }
}
