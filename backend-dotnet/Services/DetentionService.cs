using Opstrax.Api.Data;

namespace Opstrax.Api.Services;

// Detention Recovery — detection core (Phases A/B/C of the consultant-signed spec,
// docs/DETENTION_RECOVERY_SPEC.md). Pairs the debounced geofence Entry/Exit stream into
// detention_dwells via an explicit CONSUMED-EVENT LEDGER: every geofence event a dwell absorbs
// (open, close, merged re-entry, superseded exit, duplicate, post-close orphan) gets exactly one
// ledger row, ever — so a bounce re-entry can never seed a duplicate dwell (blocker #1) and a
// reopened dwell can never re-close on its cleared Exit (blocker #2). Runs each safety tick after
// GeofenceEvaluator under the system scope; every query pins company_id explicitly.
// Pricing/notice/evidence are later phases; this core only manages dwell state.
public static class DetentionService
{
    private const int DefaultMergeGapMinutes = 10;
    private const int DefaultMaxDwellHours = 24;
    private const int GpsGapMinutes = 60;

    internal static async Task DetectAsync(Database db, CancellationToken ct = default)
    {
        // Boot health gate: in restricted-role prod the schema chain is skipped; never half-run.
        var ready = await db.ScalarLongAsync(
            "SELECT COUNT(*) FROM pg_tables WHERE schemaname='public' AND tablename IN ('detention_dwells','detention_dwell_events')", ct: ct);
        if (ready < 2) return;

        await OpenDwellsAsync(db, ct);      // Phase A
        await CloseDwellsAsync(db, ct);     // Phase B
        await TimeoutDwellsAsync(db, ct);   // Phase C
    }

    // ── Phase A — OPEN. Candidates: unconsumed Entry events at customer sites (7-day lookback). ──
    private static async Task OpenDwellsAsync(Database db, CancellationToken ct)
    {
        var entries = await db.QueryAsync(
            @"SELECT ge.id, ge.company_id, ge.geofence_id, ge.vehicle_id, ge.event_time, g.customer_id
              FROM geofence_events ge
              JOIN geofences g ON g.id = ge.geofence_id AND g.company_id = ge.company_id
              WHERE ge.event_type = 'Entry'
                AND g.site_role = 'customer_site' AND g.customer_id IS NOT NULL
                AND ge.event_time > NOW() - INTERVAL '7 days'
                AND NOT EXISTS (SELECT 1 FROM detention_dwell_events c
                                WHERE c.company_id = ge.company_id AND c.geofence_event_id = ge.id)
              ORDER BY ge.event_time, ge.id",
            ct: ct);

        foreach (var e in entries)
        {
            var cid = Convert.ToInt64(e["companyId"]);
            var geId = Convert.ToInt64(e["id"]);
            var fenceId = Convert.ToInt64(e["geofenceId"]);
            var vehicleId = Convert.ToInt64(e["vehicleId"]);
            var custId = Convert.ToInt64(e["customerId"]);
            var at = Convert.ToDateTime(e["eventTime"]);

            // Belt-and-braces: an Entry inside an existing dwell's interval is absorbed, never a new dwell.
            var inside = await db.ScalarLongAsync(
                @"SELECT id FROM detention_dwells
                  WHERE company_id=@c AND geofence_id=@g AND vehicle_id=@v
                    AND entered_at <= @t AND COALESCE(exited_at, 'infinity'::timestamptz) >= @t
                    AND status <> 'open'
                  ORDER BY entered_at DESC LIMIT 1",
                c => { c.Parameters.AddWithValue("@c", cid); c.Parameters.AddWithValue("@g", fenceId); c.Parameters.AddWithValue("@v", vehicleId); c.Parameters.AddWithValue("@t", at); }, ct);
            if (inside > 0) { await ConsumeAsync(db, cid, inside, geId, "Entry", at, "absorbed_post_close", ct); continue; }

            // (a) an OPEN dwell exists -> duplicate Entry (evaluator-instance overlap); absorb, no state change.
            var openId = await db.ScalarLongAsync(
                "SELECT id FROM detention_dwells WHERE company_id=@c AND geofence_id=@g AND vehicle_id=@v AND status='open' LIMIT 1",
                c => { c.Parameters.AddWithValue("@c", cid); c.Parameters.AddWithValue("@g", fenceId); c.Parameters.AddWithValue("@v", vehicleId); }, ct);
            if (openId > 0) { await ConsumeAsync(db, cid, openId, geId, "Entry", at, "duplicate_entry", ct); continue; }

            // (b) merge-eligible dwell: recently closed, unreviewed, uncharged, exit within the merge gap.
            var mergeRow = await db.QuerySingleAsync(
                @"SELECT id FROM detention_dwells
                  WHERE company_id=@c AND geofence_id=@g AND vehicle_id=@v
                    AND status IN ('closed','below_free_time','unpriced_no_terms','needs_appointment')
                    AND reviewed_at IS NULL AND job_charge_id IS NULL
                    AND exited_at IS NOT NULL AND exited_at >= @t - make_interval(mins => @gap)
                  ORDER BY exited_at DESC LIMIT 1",
                c =>
                {
                    c.Parameters.AddWithValue("@c", cid); c.Parameters.AddWithValue("@g", fenceId);
                    c.Parameters.AddWithValue("@v", vehicleId); c.Parameters.AddWithValue("@t", at);
                    c.Parameters.AddWithValue("@gap", DefaultMergeGapMinutes);
                }, ct);
            if (mergeRow is not null)
            {
                var dwellId = Convert.ToInt64(mergeRow["id"]);
                // Reopen: clear exit + pricing fields. The cleared Exit STAYS consumed — its ledger row
                // flips to 'superseded_exit' so Phase B can never re-close on it (blocker #2 fix).
                await db.ExecuteAsync(
                    @"UPDATE detention_dwells SET status='open', exited_at=NULL, exit_event_id=NULL, close_reason=NULL,
                          departure_lower=NULL, departure_upper=NULL, billed_to_at=NULL, claim_deadline_at=NULL,
                          dwell_minutes=NULL, billable_minutes=NULL, quantity_hours=NULL, amount=NULL, updated_at=NOW()
                      WHERE company_id=@c AND id=@id AND status <> 'open'",
                    c => { c.Parameters.AddWithValue("@c", cid); c.Parameters.AddWithValue("@id", dwellId); }, ct);
                await db.ExecuteAsync(
                    "UPDATE detention_dwell_events SET consume_role='superseded_exit' WHERE company_id=@c AND dwell_id=@id AND consume_role='close'",
                    c => { c.Parameters.AddWithValue("@c", cid); c.Parameters.AddWithValue("@id", dwellId); }, ct);
                await ConsumeAsync(db, cid, dwellId, geId, "Entry", at, "merged_entry", ct);
                continue;
            }

            // (c) genuinely new dwell.
            var newId = await db.ScalarLongAsync(
                @"INSERT INTO detention_dwells (company_id, geofence_id, vehicle_id, customer_id, entry_event_id, entered_at, arrival_upper, billed_from_at)
                  VALUES (@c, @g, @v, @cust, @ge, @t, @t, @t)
                  ON CONFLICT DO NOTHING
                  RETURNING id",
                c =>
                {
                    c.Parameters.AddWithValue("@c", cid); c.Parameters.AddWithValue("@g", fenceId);
                    c.Parameters.AddWithValue("@v", vehicleId); c.Parameters.AddWithValue("@cust", custId);
                    c.Parameters.AddWithValue("@ge", geId); c.Parameters.AddWithValue("@t", at);
                }, ct);
            if (newId > 0) await ConsumeAsync(db, cid, newId, geId, "Entry", at, "open", ct);
        }
    }

    // ── Phase B — CLOSE with the earliest UNCONSUMED Exit after the dwell's latest ledger event. ──
    private static async Task CloseDwellsAsync(Database db, CancellationToken ct)
    {
        var open = await db.QueryAsync(
            "SELECT id, company_id, geofence_id, vehicle_id FROM detention_dwells WHERE status='open' ORDER BY id",
            ct: ct);

        foreach (var d in open)
        {
            var cid = Convert.ToInt64(d["companyId"]);
            var dwellId = Convert.ToInt64(d["id"]);
            var fenceId = Convert.ToInt64(d["geofenceId"]);
            var vehicleId = Convert.ToInt64(d["vehicleId"]);

            var exit = await db.QuerySingleAsync(
                @"SELECT ge.id, ge.event_time
                  FROM geofence_events ge
                  WHERE ge.company_id=@c AND ge.geofence_id=@g AND ge.vehicle_id=@v AND ge.event_type='Exit'
                    AND ge.event_time > (SELECT MAX(event_time) FROM detention_dwell_events
                                         WHERE company_id=@c AND dwell_id=@id)
                    AND NOT EXISTS (SELECT 1 FROM detention_dwell_events c2
                                    WHERE c2.company_id=ge.company_id AND c2.geofence_event_id=ge.id)
                  ORDER BY ge.event_time, ge.id LIMIT 1",
                c =>
                {
                    c.Parameters.AddWithValue("@c", cid); c.Parameters.AddWithValue("@g", fenceId);
                    c.Parameters.AddWithValue("@v", vehicleId); c.Parameters.AddWithValue("@id", dwellId);
                }, ct);
            if (exit is null) continue;

            var exitId = Convert.ToInt64(exit["id"]);
            var exitAt = Convert.ToDateTime(exit["eventTime"]);
            var rows = await db.ExecuteAsync(
                @"UPDATE detention_dwells SET status='closed', exit_event_id=@ge, exited_at=@t,
                      departure_lower=@t, billed_to_at=@t, close_reason='exit_event', updated_at=NOW()
                  WHERE company_id=@c AND id=@id AND status='open'",
                c =>
                {
                    c.Parameters.AddWithValue("@c", cid); c.Parameters.AddWithValue("@id", dwellId);
                    c.Parameters.AddWithValue("@ge", exitId); c.Parameters.AddWithValue("@t", exitAt);
                }, ct);
            if (rows > 0) await ConsumeAsync(db, cid, dwellId, exitId, "Exit", exitAt, "close", ct);
        }
    }

    // ── Phase C — TIMEOUT / GPS-GAP: never bill un-witnessed time; forced review. ──
    private static async Task TimeoutDwellsAsync(Database db, CancellationToken ct)
    {
        // Timeout: open too long. If the vehicle is provably still inside, mark truncated (undercharge, disclosed).
        await db.ExecuteAsync(
            @"UPDATE detention_dwells d SET status='closed', close_reason='timeout', exited_at=NOW(),
                  departure_lower=NOW(), billed_to_at=NOW(), review_required=TRUE, updated_at=NOW(),
                  truncated = EXISTS (SELECT 1 FROM latest_vehicle_positions p
                                      WHERE p.company_id=d.company_id AND p.vehicle_id=d.vehicle_id
                                        AND p.event_time > NOW() - make_interval(mins => @gap))
              WHERE d.status='open' AND d.entered_at < NOW() - make_interval(hours => @maxh)",
            c => { c.Parameters.AddWithValue("@maxh", DefaultMaxDwellHours); c.Parameters.AddWithValue("@gap", GpsGapMinutes); }, ct);

        // GPS gap: position stale while dwell open — close at the last-known-inside time.
        await db.ExecuteAsync(
            @"UPDATE detention_dwells d SET status='closed', close_reason='gps_gap',
                  exited_at=p.event_time, departure_lower=p.event_time, billed_to_at=p.event_time,
                  review_required=TRUE, updated_at=NOW()
              FROM latest_vehicle_positions p
              WHERE d.status='open' AND p.company_id=d.company_id AND p.vehicle_id=d.vehicle_id
                AND p.event_time < NOW() - make_interval(mins => @gap)
                AND p.event_time > d.entered_at",
            c => c.Parameters.AddWithValue("@gap", GpsGapMinutes), ct);
    }

    private static Task ConsumeAsync(Database db, long cid, long dwellId, long geofenceEventId, string type, DateTime at, string role, CancellationToken ct) =>
        db.ExecuteAsync(
            @"INSERT INTO detention_dwell_events (company_id, dwell_id, geofence_event_id, event_type, event_time, consume_role)
              VALUES (@c, @d, @ge, @t, @at, @r)
              ON CONFLICT DO NOTHING",
            c =>
            {
                c.Parameters.AddWithValue("@c", cid); c.Parameters.AddWithValue("@d", dwellId);
                c.Parameters.AddWithValue("@ge", geofenceEventId); c.Parameters.AddWithValue("@t", type);
                c.Parameters.AddWithValue("@at", at); c.Parameters.AddWithValue("@r", role);
            }, ct);
}
