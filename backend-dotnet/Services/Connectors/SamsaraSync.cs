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

        using var resp = await GetWithRetryAsync(url, ct);
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

        // 2/3. Match + write inside one system transaction (cross-tenant background write
        //      under RLS), then refresh the live-asset projection so the map/SSE update.
        using var scope = scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<Database>();
        var telemetry = scope.ServiceProvider.GetService<TelemetryLiveStateService>();

        // Additive provenance: partner/vendor API pull (Samsara). Stamp source/
        // provider/protocol/device_fix_time/normalized_at ONLY when the columns exist
        // (deploy-safe — production may pre-date migration 001). Probed once (cached).
        var hasProv = await TelemetryProvenance.ColumnsAvailableAsync(db, ct);
        var provCols = hasProv ? ", source, provider, protocol, device_fix_time, normalized_at" : "";
        var provVals = hasProv ? $", '{TelemetryProvenance.SourcePartnerApi}', 'Samsara', 'rest_json', @etime, NOW()" : "";
        var provUpd  = hasProv
            ? $", source='{TelemetryProvenance.SourcePartnerApi}', provider='Samsara', protocol='rest_json', device_fix_time=EXCLUDED.device_fix_time, normalized_at=NOW()"
            : "";

        var written = 0;
        var unmatched = 0;
        var touchedVehicles = new HashSet<long>();

        await db.RunInSystemTransactionAsync(async () =>
        {
            foreach (var r in readings)
            {
                var (telemetryStatus, riskLevel) =
                    TelemetryFixFreshness.Classify(r.EventTime, DateTime.UtcNow);
                // Match the Samsara vehicle to an OpsTrax vehicle via an eld_devices row.
                // The Samsara vehicle id is stored as the device_serial. We upsert the
                // device row (provider='Samsara') so the mapping self-heals; a device
                // with no vehicle_id yet only lands history (location_events).
                var (deviceId, vehicleId, vehicleBranchId) = await ResolveDeviceAsync(db, companyId, r, ct);

                // History (breadcrumbs) — always, even when unmatched.
                var eventId = await db.InsertAsync(
                    @"INSERT INTO location_events
                        (company_id, vehicle_id, device_id, lat, lng, speed_mph, heading,
                         event_type, engine_status, odometer_miles, source, source_channel,
                         idempotency_key, observed_at, normalized_at, event_time, received_at)
                      SELECT @cid, @vid, @did, @lat, @lng, @spd, @hdg, 'ping', @eng, @odo,
                             'samsara', 'samsara-api', @idem, @etime, NOW(), @etime, NOW()
                      WHERE NOT EXISTS (
                          SELECT 1 FROM location_events existing
                          WHERE existing.company_id=@cid AND existing.idempotency_key=@idem)
                      ON CONFLICT DO NOTHING
                      RETURNING id",
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
                        c.Parameters.AddWithValue("@idem", $"samsara:{r.VehicleId}:{r.EventTime.Ticks}");
                        c.Parameters.AddWithValue("@etime", r.EventTime);
                    }, ct);

                // A repeated cursor page/provider retry is a true no-op. In particular,
                // do not increment latest_vehicle_positions.event_count or re-open alerts.
                if (eventId == 0) continue;
                if (vehicleId is null) { unmatched++; continue; }

                // Live snapshot — the UPSERT the map reads. Mirrors the ingest handler.
                var projected = await db.ExecuteAsync(
                    $@"INSERT INTO latest_vehicle_positions
                        (company_id, vehicle_id, device_id, lat, lng, speed_mph, heading,
                         engine_status, odometer_miles, event_time, received_at, event_count,
                         source_channel, telemetry_status, risk_level, updated_at{provCols})
                      VALUES (@cid, @vid, @did, @lat, @lng, @spd, @hdg, @eng, @odo, @etime, NOW(), 1,
                              'samsara-api', @telemetryStatus, @riskLevel, NOW(){provVals})
                      ON CONFLICT (company_id, vehicle_id) DO UPDATE SET
                        device_id=EXCLUDED.device_id, lat=EXCLUDED.lat, lng=EXCLUDED.lng,
                        speed_mph=EXCLUDED.speed_mph, heading=EXCLUDED.heading,
                        engine_status=EXCLUDED.engine_status, odometer_miles=EXCLUDED.odometer_miles,
                        event_time=EXCLUDED.event_time, received_at=EXCLUDED.received_at,
                        event_count=latest_vehicle_positions.event_count+1,
                        source_channel=EXCLUDED.source_channel, telemetry_status=EXCLUDED.telemetry_status,
                        risk_level=EXCLUDED.risk_level, updated_at=NOW(){provUpd}
                      WHERE latest_vehicle_positions.event_time IS NULL OR latest_vehicle_positions.event_time <= EXCLUDED.event_time",
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
                        c.Parameters.AddWithValue("@telemetryStatus", telemetryStatus);
                        c.Parameters.AddWithValue("@riskLevel", riskLevel);
                    }, ct);

                // A novel but out-of-order historical fix remains durable history, but must not
                // create a misleading current/open alert when it lost the monotonic latest race.
                if (projected > 0)
                    await ProjectAlertsAsync(db, companyId, vehicleId.Value, vehicleBranchId,
                        deviceId, eventId, r, ct);

                if (projected > 0)
                {
                    written++;
                    touchedVehicles.Add(vehicleId.Value);
                }
            }
            return true;
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

    private static async Task ProjectAlertsAsync(
        Database db,
        long companyId,
        long vehicleId,
        long? vehicleBranchId,
        long? deviceId,
        long sourceEventId,
        SamsaraGps reading,
        CancellationToken ct)
    {
        var speedThreshold = await db.ScalarDecimalAsync(
            "SELECT threshold_value FROM telemetry_rules WHERE company_id=@cid AND rule_type='speeding' AND enabled=TRUE LIMIT 1",
            c => c.Parameters.AddWithValue("@cid", companyId), ct) ?? 65m;
        if ((decimal)reading.SpeedMph > speedThreshold)
        {
            await db.ExecuteAsync(
                @"INSERT INTO telemetry_alerts
                    (company_id,vehicle_id,device_id,alert_type,severity,message,source_event_id,status,source_channel,created_at)
                  SELECT @cid,@vid,@did,'speeding',
                         COALESCE((SELECT severity FROM telemetry_rules WHERE company_id=@cid AND rule_type='speeding' AND enabled=TRUE LIMIT 1),'High'),
                         @msg,@eventId,'Open','samsara-api',NOW()
                  WHERE NOT EXISTS (
                    SELECT 1 FROM telemetry_alerts
                    WHERE company_id=@cid AND vehicle_id=@vid AND alert_type='speeding' AND status='Open')",
                c =>
                {
                    c.Parameters.AddWithValue("@cid", companyId);
                    c.Parameters.AddWithValue("@vid", vehicleId);
                    c.Parameters.AddWithValue("@did", (object?)deviceId ?? DBNull.Value);
                    c.Parameters.AddWithValue("@msg", $"Vehicle {reading.SpeedMph:F0} mph exceeds {speedThreshold:F0} mph threshold");
                    c.Parameters.AddWithValue("@eventId", sourceEventId);
                }, ct);
        }

        // Same authorized-area set semantics as native HMAC and gateway ingest: a point
        // inside any valid scoped fence is authorized; only outside all is a breach.
        var breached = await GeofenceEvaluator.ProjectPositionAsync(
            db, companyId, vehicleBranchId, vehicleId,
            reading.Lat, reading.Lng, new DateTimeOffset(reading.EventTime.ToUniversalTime()), ct);

        if (breached is null) return;
        var fenceName = breached.GetValueOrDefault("name")?.ToString() ?? "Unknown geofence";
        var message = $"Vehicle outside geofence: {fenceName}";
        await db.ExecuteAsync(
            @"INSERT INTO telemetry_alerts
                (company_id,vehicle_id,device_id,alert_type,severity,message,source_event_id,status,source_channel,created_at)
              SELECT @cid,@vid,@did,'geofence_breach','High',@msg,@eventId,'Open','samsara-api',NOW()
              WHERE NOT EXISTS (
                SELECT 1 FROM telemetry_alerts
                WHERE company_id=@cid AND vehicle_id=@vid AND alert_type='geofence_breach'
                  AND message=@msg AND status='Open')",
            c =>
            {
                c.Parameters.AddWithValue("@cid", companyId);
                c.Parameters.AddWithValue("@vid", vehicleId);
                c.Parameters.AddWithValue("@did", (object?)deviceId ?? DBNull.Value);
                c.Parameters.AddWithValue("@msg", message);
                c.Parameters.AddWithValue("@eventId", sourceEventId);
            }, ct);
    }

    private async Task<HttpResponseMessage> GetWithRetryAsync(string url, CancellationToken ct)
    {
        for (var attempt = 0; ; attempt++)
        {
            var response = await client.GetAsync(url, ct);
            if ((int)response.StatusCode is not (429 or >= 500) || attempt >= 4) return response;
            var retryAfter = response.Headers.RetryAfter?.Delta ?? TimeSpan.FromMilliseconds(250 * Math.Pow(2, attempt));
            response.Dispose();
            await Task.Delay(retryAfter > TimeSpan.FromSeconds(10) ? TimeSpan.FromSeconds(10) : retryAfter, ct);
        }
    }

    // Upsert the eld_devices row for a Samsara vehicle (keyed by device_serial=Samsara
    // vehicle id) and return (numeric device id, linked vehicle_id or null).
    private static async Task<(long? deviceId, long? vehicleId, long? vehicleBranchId)> ResolveDeviceAsync(Database db, long companyId, SamsaraGps r, CancellationToken ct)
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
              SELECT @cid, @serial, 'Samsara', 'Provisioning', @eventTime
              WHERE NOT EXISTS (SELECT 1 FROM eld_devices WHERE company_id=@cid AND device_serial=@serial)",
            c =>
            {
                c.Parameters.AddWithValue("@cid", companyId);
                c.Parameters.AddWithValue("@serial", serial);
                c.Parameters.AddWithValue("@eventTime", r.EventTime);
            }, ct);

        var row = await db.QuerySingleAsync(
            @"SELECT e.id,e.vehicle_id,v.branch_id AS vehicle_branch_id
              FROM eld_devices e
              LEFT JOIN vehicles v ON v.id=e.vehicle_id AND v.company_id=e.company_id AND v.deleted_at IS NULL
              WHERE e.company_id=@cid AND e.device_serial=@serial LIMIT 1",
            c => { c.Parameters.AddWithValue("@cid", companyId); c.Parameters.AddWithValue("@serial", serial); }, ct);
        if (row is null) return (null, null, null);
        var deviceId = row.TryGetValue("id", out var idv) && idv is not null and not DBNull ? Convert.ToInt64(idv) : (long?)null;
        var vehicleId = row.TryGetValue("vehicleId", out var vv) && vv is not null and not DBNull ? Convert.ToInt64(vv) : (long?)null;
        var vehicleBranchId = row.TryGetValue("vehicleBranchId", out var branch) && branch is not null and not DBNull
            ? Convert.ToInt64(branch) : (long?)null;
        // Provider heartbeat reflects the actual device fix time, not the time our poll ran.
        // This prevents an old/stuck provider feed from making a device look freshly online.
        if (deviceId is not null)
            await db.ExecuteAsync(
                @"UPDATE eld_devices
                  SET last_seen_at=CASE WHEN last_seen_at IS NULL OR last_seen_at<@eventTime THEN @eventTime ELSE last_seen_at END
                  WHERE id=@id AND company_id=@companyId",
                c =>
                {
                    c.Parameters.AddWithValue("@id", deviceId.Value);
                    c.Parameters.AddWithValue("@companyId", companyId);
                    c.Parameters.AddWithValue("@eventTime", r.EventTime);
                }, ct);
        return (deviceId, vehicleId, vehicleBranchId);
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
            if (double.IsNaN(lat) || double.IsNaN(lng) || lat is < -90 or > 90 || lng is < -180 or > 180 || (lat == 0 && lng == 0))
                continue; // no valid physical fix -> quarantine by omission, never fabricate

            double speed = gps.TryGetProperty("speedMilesPerHour", out var sp) && sp.TryGetDouble(out var spV) ? spV : 0;
            if (speed is < 0 or > 200) continue;
            int heading = gps.TryGetProperty("headingDegrees", out var hd) && hd.TryGetInt32(out var hdV) ? hdV : 0;
            if (!gps.TryGetProperty("time", out var tm) || tm.ValueKind != JsonValueKind.String ||
                !DateTimeOffset.TryParse(tm.GetString(), out var parsedTime)) continue;
            var time = parsedTime.UtcDateTime;
            var now = DateTime.UtcNow;
            if (time < now.AddDays(-7) || time > now.AddMinutes(5)) continue;

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
