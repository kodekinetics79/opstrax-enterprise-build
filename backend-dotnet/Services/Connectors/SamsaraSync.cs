using System.Net.Http.Headers;
using System.Text.Json;
using Opstrax.Api.Data;
using Opstrax.Api.Services;

namespace Opstrax.Api.Services.Connectors;

// The Samsara → OpsTrax data pipeline. Pulls the vehicle stats feed and writes GPS
// into the live-position tables the map reads. Kept separate from the connector so
// it is unit-focused: one public RunAsync that does fetch → match → write → refresh.
public sealed class SamsaraSync(HttpClient client, IServiceScopeFactory scopeFactory, ILogger logger)
{
    public sealed record SyncSummary(
        int VehiclesSeen,
        int PositionsWritten,
        int Unmatched,
        int HistoricalOnly,
        int Rejected,
        string? NextCursor,
        bool HasNextPage);

    private sealed record ParsedFeed(List<SamsaraGps> Readings, int Rejected);

    private sealed record SamsaraGps(string VehicleId, string? Name, double Lat, double Lng, double SpeedMph, int Heading, DateTime EventTime, double? OdometerMiles, string? EngineState);

    public async Task<SyncSummary> RunAsync(
        ConnectorOperationContext operation,
        string? afterCursor,
        CancellationToken ct)
    {
        var companyId = operation.CompanyId;
        // 1. Pull one page of the stats feed (gps + engine + odometer). Cursor makes it incremental.
        var url = "/fleet/vehicles/stats/feed?types=gps,engineStates,obdOdometerMeters";
        if (!string.IsNullOrWhiteSpace(afterCursor)) url += $"&after={Uri.EscapeDataString(afterCursor!)}";

        using var doc = await GetWithRetryAsync(url, ct);

        var parsed = ParseFeed(doc.RootElement);
        var readings = parsed.Readings;
        var (nextCursor, hasNext) = ReadPagination(doc.RootElement);

        if (readings.Count == 0)
            return new SyncSummary(0, 0, 0, 0, parsed.Rejected, nextCursor, hasNext);

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
        var historicalOnly = 0;
        var touchedVehicles = new HashSet<long>();

        await db.RunInSystemTransactionAsync(async () =>
        {
            // This lock is acquired in the SAME transaction as all provider-derived
            // telemetry writes. If disconnect/configure won the race, the generation or
            // token no longer matches and the transaction aborts before its first side
            // effect. If this transaction won, disconnect waits and returns only after
            // the committed data has been followed by connector invalidation.
            await ConnectorOperationLease.AssertCurrentForWriteAsync(db, operation, ct);
            foreach (var r in readings)
            {
                var (telemetryStatus, riskLevel) =
                    TelemetryFixFreshness.Classify(r.EventTime, DateTime.UtcNow);
                // Discover the provider device without inventing an asset mapping. Governed
                // attribution comes only from the one effective-dated installation valid at
                // the provider event time. A missing/ambiguous mapping retains unbound history;
                // an ended historical installation retains lineage but cannot update live state.
                var deviceId = await EnsureDiscoveredDeviceAsync(db, companyId, r.VehicleId, r.EventTime, ct);
                var identity = deviceId is { } did
                    ? await TelemetryIdentityResolver.ResolveAsync(
                        db, companyId, did, new DateTimeOffset(DateTime.SpecifyKind(r.EventTime, DateTimeKind.Utc)), ct)
                    : null;
                var vehicleId = identity?.VehicleId;

                // History (breadcrumbs) — always, even when unmatched.
                var eventId = await db.InsertAsync(
                    @"INSERT INTO location_events
                        (company_id, vehicle_id, device_id, installation_id, assignment_id, trip_id, driver_id,
                         lat, lng, speed_mph, heading,
                         event_type, engine_status, odometer_miles, source, source_channel,
                         idempotency_key, observed_at, normalized_at, event_time, received_at)
                      SELECT @cid, @vid, @did, @installationId, @assignmentId, @tripId, @driverId,
                             @lat, @lng, @spd, @hdg, 'ping', @eng, @odo,
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
                        c.Parameters.AddWithValue("@installationId", (object?)identity?.InstallationId ?? DBNull.Value);
                        c.Parameters.AddWithValue("@assignmentId", (object?)identity?.AssignmentId ?? DBNull.Value);
                        c.Parameters.AddWithValue("@tripId", (object?)identity?.TripId ?? DBNull.Value);
                        c.Parameters.AddWithValue("@driverId", (object?)identity?.DriverId ?? DBNull.Value);
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
                if (identity is null) { unmatched++; continue; }
                if (!identity.IsCurrentInstallation) { historicalOnly++; continue; }

                // Live snapshot — the UPSERT the map reads. Mirrors the ingest handler.
                var projected = await db.ExecuteAsync(
                    $@"INSERT INTO latest_vehicle_positions
                        (company_id, vehicle_id, device_id, installation_id, assignment_id, trip_id, driver_id,
                         lat, lng, speed_mph, heading,
                         engine_status, odometer_miles, event_time, received_at, event_count,
                         source_channel, telemetry_status, risk_level, updated_at{provCols})
                      VALUES (@cid, @vid, @did, @installationId, @assignmentId, @tripId, @driverId,
                              @lat, @lng, @spd, @hdg, @eng, @odo, @etime, NOW(), 1,
                              'samsara-api', @telemetryStatus, @riskLevel, NOW(){provVals})
                      ON CONFLICT (company_id, vehicle_id) DO UPDATE SET
                        device_id=EXCLUDED.device_id, installation_id=EXCLUDED.installation_id,
                        assignment_id=EXCLUDED.assignment_id, trip_id=EXCLUDED.trip_id,
                        driver_id=EXCLUDED.driver_id, lat=EXCLUDED.lat, lng=EXCLUDED.lng,
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
                        c.Parameters.AddWithValue("@vid", identity.VehicleId);
                        c.Parameters.AddWithValue("@did", (object?)deviceId ?? DBNull.Value);
                        c.Parameters.AddWithValue("@installationId", identity.InstallationId);
                        c.Parameters.AddWithValue("@assignmentId", (object?)identity.AssignmentId ?? DBNull.Value);
                        c.Parameters.AddWithValue("@tripId", (object?)identity.TripId ?? DBNull.Value);
                        c.Parameters.AddWithValue("@driverId", (object?)identity.DriverId ?? DBNull.Value);
                        c.Parameters.AddWithValue("@lat", (decimal)r.Lat);
                        c.Parameters.AddWithValue("@lng", (decimal)r.Lng);
                        c.Parameters.AddWithValue("@spd", (decimal)r.SpeedMph);
                        c.Parameters.AddWithValue("@hdg", (short)Math.Clamp(r.Heading, 0, 359));
                        // Missing engine evidence must agree with history; a GPS fix
                        // alone cannot establish that the engine is running.
                        c.Parameters.AddWithValue("@eng", (object?)r.EngineState ?? DBNull.Value);
                        c.Parameters.AddWithValue("@odo", (object?)(r.OdometerMiles is { } o ? (decimal)o : (object?)null) ?? DBNull.Value);
                        c.Parameters.AddWithValue("@etime", r.EventTime);
                        c.Parameters.AddWithValue("@telemetryStatus", telemetryStatus);
                        c.Parameters.AddWithValue("@riskLevel", riskLevel);
                    }, ct);

                // A novel but out-of-order historical fix remains durable history, but must not
                // create a misleading current/open alert when it lost the monotonic latest race.
                if (projected > 0)
                    await ProjectAlertsAsync(db, companyId, deviceId, eventId, identity, r, ct);

                if (projected > 0)
                {
                    written++;
                    touchedVehicles.Add(identity.VehicleId);
                }
            }

            // This is provider-event freshness, not request/scheduler freshness.
            // Advance it only from authentic, parse-valid event timestamps committed
            // in the same fenced page transaction; old/backfill pages cannot regress it.
            var newestProviderEventAt = readings.Max(r => r.EventTime);
            await db.ExecuteAsync(
                @"UPDATE integrations SET
                      provider_last_event_at=CASE
                        WHEN provider_last_event_at IS NULL OR provider_last_event_at < @eventAt THEN @eventAt
                        ELSE provider_last_event_at END
                  WHERE company_id=@cid AND id=@id
                    AND operation_generation=@generation
                    AND operation_lease_token=@token
                    AND operation_lease_expires_at > NOW()",
                c =>
                {
                    c.Parameters.AddWithValue("@eventAt", newestProviderEventAt);
                    c.Parameters.AddWithValue("@cid", operation.CompanyId);
                    c.Parameters.AddWithValue("@id", operation.IntegrationId);
                    c.Parameters.AddWithValue("@generation", operation.Generation);
                    c.Parameters.AddWithValue("@token", operation.LeaseToken);
                }, ct);
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

        return new SyncSummary(readings.Select(r => r.VehicleId).Distinct(StringComparer.Ordinal).Count(),
            written, unmatched, historicalOnly, parsed.Rejected, nextCursor, hasNext);
    }

    internal static (string EndCursor, bool HasNextPage) ReadPagination(JsonElement root)
    {
        if (!root.TryGetProperty("pagination", out var pagination)
            || pagination.ValueKind != JsonValueKind.Object)
            throw new InvalidDataException("the required pagination object is missing.");

        if (!pagination.TryGetProperty("hasNextPage", out var hasNextPage)
            || hasNextPage.ValueKind is not (JsonValueKind.True or JsonValueKind.False))
            throw new InvalidDataException("pagination.hasNextPage must be a boolean.");

        if (!pagination.TryGetProperty("endCursor", out var endCursor)
            || endCursor.ValueKind != JsonValueKind.String)
            throw new InvalidDataException("pagination.endCursor must be a string.");

        var cursor = endCursor.GetString() ?? string.Empty;
        var hasNext = hasNextPage.GetBoolean();
        if (hasNext && string.IsNullOrWhiteSpace(cursor))
            throw new InvalidDataException("pagination.endCursor cannot be empty while more pages are available.");

        return (cursor, hasNext);
    }

    private static async Task ProjectAlertsAsync(
        Database db,
        long companyId,
        long? deviceId,
        long sourceEventId,
        ResolvedTelemetryIdentity identity,
        SamsaraGps reading,
        CancellationToken ct)
    {
        var vehicleId = identity.VehicleId;
        var speedThreshold = await db.ScalarDecimalAsync(
            "SELECT threshold_value FROM telemetry_rules WHERE company_id=@cid AND rule_type='speeding' AND enabled=TRUE LIMIT 1",
            c => c.Parameters.AddWithValue("@cid", companyId), ct) ?? 65m;
        if ((decimal)reading.SpeedMph > speedThreshold)
        {
            await db.ExecuteAsync(
                @"INSERT INTO telemetry_alerts
                    (company_id,vehicle_id,device_id,installation_id,assignment_id,trip_id,driver_id,
                     alert_type,severity,message,source_event_id,status,source_channel,created_at)
                  SELECT @cid,@vid,@did,@installationId,@assignmentId,@tripId,@driverId,'speeding',
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
                    c.Parameters.AddWithValue("@installationId", identity.InstallationId);
                    c.Parameters.AddWithValue("@assignmentId", (object?)identity.AssignmentId ?? DBNull.Value);
                    c.Parameters.AddWithValue("@tripId", (object?)identity.TripId ?? DBNull.Value);
                    c.Parameters.AddWithValue("@driverId", (object?)identity.DriverId ?? DBNull.Value);
                    c.Parameters.AddWithValue("@msg", $"Vehicle {reading.SpeedMph:F0} mph exceeds {speedThreshold:F0} mph threshold");
                    c.Parameters.AddWithValue("@eventId", sourceEventId);
                }, ct);
        }

        // Same authorized-area set semantics as native HMAC and gateway ingest: a point
        // inside any valid scoped fence is authorized; only outside all is a breach.
        var breached = await GeofenceEvaluator.ProjectPositionAsync(
            db, companyId, identity.VehicleBranchId, vehicleId,
            reading.Lat, reading.Lng, new DateTimeOffset(reading.EventTime.ToUniversalTime()), ct);

        if (breached is null) return;
        var fenceName = breached.GetValueOrDefault("name")?.ToString() ?? "Unknown geofence";
        var message = $"Vehicle outside geofence: {fenceName}";
        await db.ExecuteAsync(
            @"INSERT INTO telemetry_alerts
                (company_id,vehicle_id,device_id,installation_id,assignment_id,trip_id,driver_id,
                 alert_type,severity,message,source_event_id,status,source_channel,created_at)
              SELECT @cid,@vid,@did,@installationId,@assignmentId,@tripId,@driverId,
                     'geofence_breach','High',@msg,@eventId,'Open','samsara-api',NOW()
              WHERE NOT EXISTS (
                SELECT 1 FROM telemetry_alerts
                WHERE company_id=@cid AND vehicle_id=@vid AND alert_type='geofence_breach'
                  AND message=@msg AND status='Open')",
            c =>
            {
                c.Parameters.AddWithValue("@cid", companyId);
                c.Parameters.AddWithValue("@vid", vehicleId);
                c.Parameters.AddWithValue("@did", (object?)deviceId ?? DBNull.Value);
                c.Parameters.AddWithValue("@installationId", identity.InstallationId);
                c.Parameters.AddWithValue("@assignmentId", (object?)identity.AssignmentId ?? DBNull.Value);
                c.Parameters.AddWithValue("@tripId", (object?)identity.TripId ?? DBNull.Value);
                c.Parameters.AddWithValue("@driverId", (object?)identity.DriverId ?? DBNull.Value);
                c.Parameters.AddWithValue("@msg", message);
                c.Parameters.AddWithValue("@eventId", sourceEventId);
            }, ct);
    }

    private async Task<JsonDocument> GetWithRetryAsync(string url, CancellationToken ct)
    {
        for (var attempt = 0; ; attempt++)
        {
            TimeSpan retryAfter;
            // Keep the deadline alive through body reads: headers-only completion
            // stops HttpClient's own timeout before the content has arrived.
            using (var requestCts = CancellationTokenSource.CreateLinkedTokenSource(ct))
            {
                requestCts.CancelAfter(SamsaraResponseReader.RequestTimeout);
                using var response = await client.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, requestCts.Token);
                if ((int)response.StatusCode is not (429 or >= 500) || attempt >= 4)
                {
                    response.EnsureSuccessStatusCode();
                    return await SamsaraResponseReader.ReadJsonAsync(response.Content, requestCts.Token);
                }
                retryAfter = ResolveRetryDelay(response.Headers.RetryAfter, attempt, DateTimeOffset.UtcNow);
            }
            // Retriable error bodies are never read; disposal precedes the delay.
            await Task.Delay(retryAfter, ct);
        }
    }

    internal static TimeSpan ResolveRetryDelay(
        RetryConditionHeaderValue? retryAfterHeader,
        int attempt,
        DateTimeOffset now)
    {
        var retryAfter = retryAfterHeader?.Delta
            ?? (retryAfterHeader?.Date is { } retryDate
                ? retryDate - now
                : TimeSpan.FromMilliseconds(250 * Math.Pow(2, attempt)));
        if (retryAfter < TimeSpan.Zero) retryAfter = TimeSpan.Zero;
        return retryAfter > TimeSpan.FromSeconds(10) ? TimeSpan.FromSeconds(10) : retryAfter;
    }

    // Upsert only the discovered provider device. Asset ownership is never read from
    // eld_devices.vehicle_id here; TelemetryIdentityResolver resolves the effective
    // governed installation independently at the provider event time.
    internal static async Task<long?> EnsureDiscoveredDeviceAsync(
        Database db,
        long companyId,
        string providerVehicleId,
        DateTime eventTime,
        CancellationToken ct)
    {
        var serial = $"samsara-{providerVehicleId}";
        // Serialize discovery on the normalized provider identity before checking or
        // inserting. The Stage80 ambiguity trigger runs BEFORE ON CONFLICT handling;
        // attempting a duplicate INSERT would therefore quarantine the legitimate
        // existing row even when PostgreSQL later discarded the duplicate. This lock +
        // select-before-insert is both race-safe for connector discovery and trigger-safe.
        await db.QuerySingleAsync(
            "SELECT pg_advisory_xact_lock(hashtextextended(@serial,0)) AS locked",
            c => c.Parameters.AddWithValue("@serial", serial), ct);
        var existing = await db.QuerySingleAsync(
            "SELECT id,company_id FROM eld_devices WHERE device_serial=@serial LIMIT 1",
            c => c.Parameters.AddWithValue("@serial", serial), ct);
        if (existing is not null && Convert.ToInt64(existing["companyId"]) != companyId)
            return null;

        var deviceId = existing is null
            ? await db.InsertAsync(
                @"INSERT INTO eld_devices (company_id,device_serial,provider,status,last_seen_at)
                  SELECT @cid,@serial,'Samsara','Provisioning',@eventTime
                  WHERE NOT EXISTS (SELECT 1 FROM eld_devices WHERE device_serial=@serial)
                  RETURNING id",
                c =>
                {
                    c.Parameters.AddWithValue("@cid", companyId);
                    c.Parameters.AddWithValue("@serial", serial);
                    c.Parameters.AddWithValue("@eventTime", eventTime);
                }, ct)
            : Convert.ToInt64(existing["id"]);

        // Status is 'Provisioning', NOT 'Active': a Samsara device is an external data
        // SOURCE we pull from — it does not authenticate via our HMAC ingest path, so it
        // has no api_key_hash/hmac_secret and cannot be 'Active' (a check constraint,
        // ck_eld_devices_active_credentials, enforces that Active devices carry real
        // credentials). Provisioning correctly reflects "linked, externally sourced".
        if (deviceId == 0) return null;
        // Provider heartbeat reflects the actual device fix time, not the time our poll ran.
        // This prevents an old/stuck provider feed from making a device look freshly online.
        await db.ExecuteAsync(
            @"UPDATE eld_devices
              SET last_seen_at=CASE WHEN last_seen_at IS NULL OR last_seen_at<@eventTime THEN @eventTime ELSE last_seen_at END
              WHERE id=@id AND company_id=@companyId",
            c =>
            {
                c.Parameters.AddWithValue("@id", deviceId);
                c.Parameters.AddWithValue("@companyId", companyId);
                c.Parameters.AddWithValue("@eventTime", eventTime);
            }, ct);
        return deviceId;
    }

    // Canonical /stats/feed uses data[].gps[] even for its initial last-known page.
    // Sibling engine/odometer arrays have independent timestamps: never zip them or
    // borrow their latest values. Only decorations on this GPS event are associated.
    private static ParsedFeed ParseFeed(JsonElement root)
    {
        var list = new List<SamsaraGps>();
        var rejected = 0;
        if (!root.TryGetProperty("data", out var data) || data.ValueKind != JsonValueKind.Array)
            throw new InvalidDataException("the required data array is missing.");
        foreach (var v in data.EnumerateArray())
        {
            if (v.ValueKind != JsonValueKind.Object)
                throw new InvalidDataException("each data entry must be a vehicle object.");
            var id = v.TryGetProperty("id", out var idEl) && idEl.ValueKind == JsonValueKind.String ? idEl.GetString() : null;
            if (string.IsNullOrWhiteSpace(id)) { rejected++; continue; }
            var name = v.TryGetProperty("name", out var nEl) && nEl.ValueKind == JsonValueKind.String ? nEl.GetString() : null;
            // An engine/odometer-only update legitimately has no GPS event.
            if (!v.TryGetProperty("gps", out var gpsEvents)) continue;
            if (gpsEvents.ValueKind != JsonValueKind.Array)
                throw new InvalidDataException("gps must be an array of timestamped events; the page was not consumed.");

            foreach (var gps in gpsEvents.EnumerateArray())
            {
                if (gps.ValueKind != JsonValueKind.Object)
                    throw new InvalidDataException("each gps event must be an object; the page was not consumed.");
                var lat = Number(gps, "latitude") ?? double.NaN;
                var lng = Number(gps, "longitude") ?? double.NaN;
                if (!double.IsFinite(lat) || !double.IsFinite(lng) || lat is < -90 or > 90 || lng is < -180 or > 180 || (lat == 0 && lng == 0))
                { rejected++; continue; }
                if (!gps.TryGetProperty("time", out var tm) || tm.ValueKind != JsonValueKind.String ||
                    !DateTimeOffset.TryParse(tm.GetString(), out var parsedTime)) { rejected++; continue; }
                var time = parsedTime.UtcDateTime;
                // Keep genuine backfill; existing monotonic projection protects live state.
                if (time < new DateTime(2000, 1, 1, 0, 0, 0, DateTimeKind.Utc) || time > DateTime.UtcNow.AddMinutes(5))
                { rejected++; continue; }

                // These fields are optional at Samsara, but our current storage is NOT
                // NULL-capable. Pause resumably before any page writes rather than
                // invent zero or discard a valid location. Nullable storage is a separate
                // explicit readiness dependency, not an implicit provider requirement.
                var speed = Number(gps, "speedMilesPerHour") ?? throw Unrepresentable("speedMilesPerHour");
                var bearing = Number(gps, "headingDegrees") ?? throw Unrepresentable("headingDegrees");
                if (!double.IsFinite(speed) || speed is < 0 or > 200 || !double.IsFinite(bearing) || bearing is < 0 or > 360)
                { rejected++; continue; }
                var heading = (int)Math.Floor(bearing % 360); // existing whole-degree storage

                var decorations = gps.TryGetProperty("decorations", out var dec) ? dec : default;
                if (decorations.ValueKind is not (JsonValueKind.Undefined or JsonValueKind.Null or JsonValueKind.Object))
                    throw new InvalidDataException("gps decorations must be an object.");
                var engineValue = DecorationValue(decorations, "engineStates");
                if (engineValue.ValueKind is not (JsonValueKind.Undefined or JsonValueKind.Null or JsonValueKind.String))
                    throw new InvalidDataException("gps engineStates decoration value must be a string.");
                var engine = engineValue.ValueKind == JsonValueKind.String ? engineValue.GetString() : null;
                if (string.IsNullOrWhiteSpace(engine)) engine = null;
                var odoValue = DecorationValue(decorations, "obdOdometerMeters");
                double? odoMiles = null;
                if (odoValue.ValueKind is not (JsonValueKind.Undefined or JsonValueKind.Null))
                {
                    if (odoValue.ValueKind != JsonValueKind.Number || !odoValue.TryGetInt64(out var meters) || meters < 0)
                        throw new InvalidDataException("gps obdOdometerMeters decoration must contain nonnegative integer meters.");
                    odoMiles = meters / 1609.344;
                }
                list.Add(new SamsaraGps(id!, name, lat, lng, speed, heading, time, odoMiles, engine));
            }
        }
        return new ParsedFeed(list, rejected);
    }

    private static double? Number(JsonElement value, string property) =>
        value.TryGetProperty(property, out var number) && number.ValueKind == JsonValueKind.Number && number.TryGetDouble(out var result)
            ? result : null;

    private static InvalidDataException Unrepresentable(string field) =>
        new($"GPS {field} is unavailable. Samsara permits missing measurements, but current OpsTrax storage cannot represent them; the page was not consumed.");

    private static JsonElement DecorationValue(JsonElement decorations, string property)
    {
        if (decorations.ValueKind != JsonValueKind.Object || !decorations.TryGetProperty(property, out var value)
            || value.ValueKind == JsonValueKind.Null) return default;
        if (value.ValueKind != JsonValueKind.Object)
            throw new InvalidDataException($"gps {property} decoration must be an object.");
        return value.TryGetProperty("value", out var reading) ? reading : default;
    }
}
