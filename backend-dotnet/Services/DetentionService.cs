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
        await AttributeDwellsAsync(db, ct); // Phase D  — attach the overlapping job/assignment
        await ApplyClockAsync(db, ct);      // Phase B½ — later-of(appointment, arrival) clock
        await NotifyPreExpiryAsync(db, ct); // Phase C½ — 'meter running' notice before free time expires
        await PriceDwellsAsync(db, ct);     // Phase E  — fail-closed pricing after the settle delay
    }

    // ── Phase D — ATTRIBUTE: strict-overlap assignment match, best overlap wins per dwell. ──
    private static Task AttributeDwellsAsync(Database db, CancellationToken ct) =>
        db.ExecuteAsync(
            @"UPDATE detention_dwells d
              SET job_id = m.job_id, dispatch_assignment_id = m.aid, driver_id = m.driver_id,
                  stop_role = m.stop_role, updated_at = NOW()
              FROM (
                  SELECT DISTINCT ON (d2.id) d2.id AS dwell_id, d2.company_id AS cid,
                         da.id AS aid, da.job_id, da.driver_id,
                         CASE WHEN j.dropoff_latitude IS NOT NULL
                                   AND abs(j.dropoff_latitude - g.center_lat) + abs(j.dropoff_longitude - g.center_lng)
                                     < abs(COALESCE(j.pickup_latitude, 1e9) - g.center_lat) + abs(COALESCE(j.pickup_longitude, 1e9) - g.center_lng)
                              THEN 'dropoff' ELSE 'pickup' END AS stop_role
                  FROM detention_dwells d2
                  JOIN geofences g ON g.id = d2.geofence_id AND g.company_id = d2.company_id
                  JOIN dispatch_assignments da ON da.company_id = d2.company_id AND da.vehicle_id = d2.vehicle_id
                  JOIN jobs j ON j.id = da.job_id AND j.company_id = da.company_id AND j.customer_id = d2.customer_id
                  WHERE d2.status = 'closed' AND d2.job_id IS NULL AND d2.billed_to_at IS NOT NULL
                    AND da.assigned_at <= d2.billed_to_at
                    AND COALESCE(da.actual_delivery_at, da.completed_at, 'infinity'::timestamptz) >= d2.entered_at
                  ORDER BY d2.id,
                           LEAST(COALESCE(da.actual_delivery_at, da.completed_at, d2.billed_to_at), d2.billed_to_at)
                         - GREATEST(da.assigned_at, d2.entered_at) DESC
              ) m
              WHERE d.id = m.dwell_id AND d.company_id = m.cid", ct: ct);

    // ── Phase B½ — CLOCK: billable time starts at LATER-OF(appointment, arrival); early arrival
    // never accrues. No appointment and no attestation -> 'needs_appointment' (detected, never priced). ──
    private static async Task ApplyClockAsync(Database db, CancellationToken ct)
    {
        // Pull the appointment from the attributed assignment's planned time by stop role.
        await db.ExecuteAsync(
            @"UPDATE detention_dwells d
              SET appointment_at = CASE WHEN d.stop_role = 'dropoff' THEN da.planned_delivery_at ELSE da.planned_pickup_at END,
                  appointment_source = 'assignment_planned', updated_at = NOW()
              FROM dispatch_assignments da
              WHERE d.status = 'closed' AND d.appointment_at IS NULL AND d.appointment_source IS NULL
                AND da.id = d.dispatch_assignment_id AND da.company_id = d.company_id
                AND (CASE WHEN d.stop_role = 'dropoff' THEN da.planned_delivery_at ELSE da.planned_pickup_at END) IS NOT NULL", ct: ct);

        // Clock: later-of(appointment, arrival). Attested-none clocks from arrival.
        await db.ExecuteAsync(
            @"UPDATE detention_dwells SET
                  clock_start_at = CASE WHEN appointment_source = 'attested_none' OR appointment_at IS NULL
                                        THEN billed_from_at
                                        ELSE GREATEST(appointment_at, billed_from_at) END,
                  clock_rule = 'later_of_appointment_arrival_v1', updated_at = NOW()
              WHERE status = 'closed' AND clock_start_at IS NULL AND billed_from_at IS NOT NULL
                AND (appointment_at IS NOT NULL OR appointment_source = 'attested_none')", ct: ct);
    }

    // ── Phase C½ — PRE-EXPIRY NOTICE: where detention money is legally won. Guarded stamp is the race lock. ──
    private static async Task NotifyPreExpiryAsync(Database db, CancellationToken ct)
    {
        var due = await db.QueryAsync(
            @"SELECT d.id, d.company_id, d.customer_id, g.name AS site_name,
                     rc.free_minutes, rc.rate_per_hour, rc.currency, rc.notice_percent,
                     GREATEST(COALESCE(d.appointment_at, d.billed_from_at), d.billed_from_at) AS clock_base,
                     c.contact_name, c.email AS contact_email
              FROM detention_dwells d
              JOIN geofences g ON g.id = d.geofence_id AND g.company_id = d.company_id
              JOIN customers c ON c.id = d.customer_id AND c.company_id = d.company_id
              JOIN LATERAL (
                  SELECT free_minutes, rate_per_hour, currency, notice_percent FROM detention_rule_cards rc
                  WHERE rc.company_id = d.company_id AND rc.active
                    AND ((rc.scope_type = 'customer' AND rc.scope_id = d.customer_id) OR rc.scope_type = 'tenant')
                  ORDER BY CASE WHEN rc.scope_type = 'customer' THEN 0 ELSE 1 END, rc.effective_date DESC, rc.version DESC
                  LIMIT 1) rc ON TRUE
              WHERE d.status = 'open' AND d.warning_notified_at IS NULL AND d.billed_from_at IS NOT NULL
                AND NOW() >= GREATEST(COALESCE(d.appointment_at, d.billed_from_at), d.billed_from_at)
                             + make_interval(mins => rc.free_minutes * rc.notice_percent / 100)",
            ct: ct);

        foreach (var d in due)
        {
            var cid = Convert.ToInt64(d["companyId"]);
            var dwellId = Convert.ToInt64(d["id"]);
            var freeMin = Convert.ToInt32(d["freeMinutes"]);
            var rate = Convert.ToDecimal(d["ratePerHour"]);
            var site = d["siteName"]?.ToString() ?? "customer site";
            var expiresAt = Convert.ToDateTime(d["clockBase"]).AddMinutes(freeMin);
            var body = $"Free time at {site} expires {expiresAt:yyyy-MM-dd HH:mm} UTC; detention meter starts at {rate:0.00}/h.";

            // The guarded stamp — exactly one tick wins.
            var won = await db.ExecuteAsync(
                "UPDATE detention_dwells SET warning_notified_at=NOW(), updated_at=NOW() WHERE company_id=@c AND id=@id AND warning_notified_at IS NULL",
                c => { c.Parameters.AddWithValue("@c", cid); c.Parameters.AddWithValue("@id", dwellId); }, ct);
            if (won == 0) continue;

            await db.ExecuteAsync(
                @"INSERT INTO notifications (company_id, event_type, severity, title, message, body, audience_type, channel, status, dedupe_key)
                  VALUES (@c, 'detention.warning', 'warning', @title, @body, @body, 'dispatch', 'in_app', 'unread', @dedupe)
                  ON CONFLICT DO NOTHING",
                c =>
                {
                    c.Parameters.AddWithValue("@c", cid);
                    c.Parameters.AddWithValue("@title", $"Detention warning — {site}");
                    c.Parameters.AddWithValue("@body", body);
                    c.Parameters.AddWithValue("@dedupe", $"detention-warning-{dwellId}");
                }, ct);
            await db.ExecuteAsync(
                @"INSERT INTO detention_notices (company_id, dwell_id, notice_type, recipient_name, recipient_address, channel, body_snapshot, delivery_status)
                  VALUES (@c, @d, 'customer_meter_running', @name, @addr, 'email', @body, 'logged')
                  ON CONFLICT DO NOTHING",
                c =>
                {
                    c.Parameters.AddWithValue("@c", cid); c.Parameters.AddWithValue("@d", dwellId);
                    c.Parameters.AddWithValue("@name", (object?)d["contactName"] ?? DBNull.Value);
                    c.Parameters.AddWithValue("@addr", (object?)d["contactEmail"] ?? DBNull.Value);
                    c.Parameters.AddWithValue("@body", body);
                }, ct);
        }
    }

    // ── Phase E — PRICE with the settle delay (a closed dwell rests through one merge window so
    // bounce re-entries actually merge). Fail-closed: no rule card -> 'unpriced_no_terms', no
    // appointment/attestation -> 'needs_appointment'; below free time is terminal. Round DOWN. ──
    private static async Task PriceDwellsAsync(Database db, CancellationToken ct)
    {
        var settled = await db.QueryAsync(
            @"SELECT d.id, d.company_id, d.customer_id, d.billed_from_at, d.billed_to_at, d.clock_start_at,
                     d.appointment_at, d.appointment_source
              FROM detention_dwells d
              WHERE d.status = 'closed'
                AND d.exited_at IS NOT NULL AND NOW() > d.exited_at + make_interval(mins => @gap)
              ORDER BY d.id",
            c => c.Parameters.AddWithValue("@gap", DefaultMergeGapMinutes), ct);

        foreach (var d in settled)
        {
            var cid = Convert.ToInt64(d["companyId"]);
            var dwellId = Convert.ToInt64(d["id"]);
            var custId = d["customerId"] is null or DBNull ? 0L : Convert.ToInt64(d["customerId"]);

            var card = await db.QuerySingleAsync(
                @"SELECT id, version, free_minutes, rate_per_hour, currency, billing_increment_minutes,
                         max_charge_amount, claim_window_days
                  FROM detention_rule_cards rc
                  WHERE rc.company_id=@c AND rc.active
                    AND ((rc.scope_type='customer' AND rc.scope_id=@cust) OR rc.scope_type='tenant')
                  ORDER BY CASE WHEN rc.scope_type='customer' THEN 0 ELSE 1 END, rc.effective_date DESC, rc.version DESC
                  LIMIT 1",
                c => { c.Parameters.AddWithValue("@c", cid); c.Parameters.AddWithValue("@cust", custId); }, ct);
            if (card is null)
            {
                await SetStatusAsync(db, cid, dwellId, "unpriced_no_terms", ct);
                continue;
            }
            if (d["clockStartAt"] is null or DBNull)
            {
                // No appointment recorded and no attestation — detected and shown, never priced.
                await SetStatusAsync(db, cid, dwellId, "needs_appointment", ct);
                continue;
            }

            var clockStart = Convert.ToDateTime(d["clockStartAt"]);
            var billedTo = Convert.ToDateTime(d["billedToAt"]);
            var freeMin = Convert.ToInt32(card["freeMinutes"]);
            var increment = Math.Max(1, Convert.ToInt32(card["billingIncrementMinutes"]));
            var rate = Convert.ToDecimal(card["ratePerHour"]);
            var cap = card["maxChargeAmount"] is null or DBNull ? (decimal?)null : Convert.ToDecimal(card["maxChargeAmount"]);
            var claimDays = Convert.ToInt32(card["claimWindowDays"]);

            var dwellMinutes = (int)Math.Max(0, (billedTo - clockStart).TotalMinutes);
            var billable = Math.Max(0, dwellMinutes - freeMin);
            billable = billable / increment * increment;   // round DOWN (every ambiguity in the customer's favor)

            if (billable <= 0)
            {
                await db.ExecuteAsync(
                    @"UPDATE detention_dwells SET status='below_free_time', dwell_minutes=@dm, free_minutes_applied=@fm,
                          billable_minutes=0, rule_card_id=@rc, rule_card_version=@ver, updated_at=NOW()
                      WHERE company_id=@c AND id=@id AND status='closed'",
                    c =>
                    {
                        c.Parameters.AddWithValue("@c", cid); c.Parameters.AddWithValue("@id", dwellId);
                        c.Parameters.AddWithValue("@dm", dwellMinutes); c.Parameters.AddWithValue("@fm", freeMin);
                        c.Parameters.AddWithValue("@rc", Convert.ToInt64(card["id"])); c.Parameters.AddWithValue("@ver", Convert.ToInt32(card["version"]));
                    }, ct);
                continue;
            }

            var qtyHours = Math.Round(billable / 60m, 3);
            var amount = Math.Round(qtyHours * rate, 2);
            if (cap.HasValue) amount = Math.Min(amount, cap.Value);

            await db.ExecuteAsync(
                @"UPDATE detention_dwells SET status='priced_pending_review',
                      dwell_minutes=@dm, free_minutes_applied=@fm, billable_minutes=@bm,
                      rule_card_id=@rc, rule_card_version=@ver,
                      quantity_hours=@qty, unit_rate=@rate, amount=@amt, currency=@cur,
                      claim_deadline_at = exited_at + make_interval(days => @cw), updated_at=NOW()
                  WHERE company_id=@c AND id=@id AND status='closed'",
                c =>
                {
                    c.Parameters.AddWithValue("@c", cid); c.Parameters.AddWithValue("@id", dwellId);
                    c.Parameters.AddWithValue("@dm", dwellMinutes); c.Parameters.AddWithValue("@fm", freeMin);
                    c.Parameters.AddWithValue("@bm", billable);
                    c.Parameters.AddWithValue("@rc", Convert.ToInt64(card["id"])); c.Parameters.AddWithValue("@ver", Convert.ToInt32(card["version"]));
                    c.Parameters.AddWithValue("@qty", qtyHours); c.Parameters.AddWithValue("@rate", rate);
                    c.Parameters.AddWithValue("@amt", amount); c.Parameters.AddWithValue("@cur", card["currency"]?.ToString() ?? "USD");
                    c.Parameters.AddWithValue("@cw", claimDays);
                }, ct);
        }
    }

    private static Task SetStatusAsync(Database db, long cid, long dwellId, string status, CancellationToken ct) =>
        db.ExecuteAsync(
            "UPDATE detention_dwells SET status=@s, updated_at=NOW() WHERE company_id=@c AND id=@id AND status='closed'",
            c => { c.Parameters.AddWithValue("@c", cid); c.Parameters.AddWithValue("@id", dwellId); c.Parameters.AddWithValue("@s", status); }, ct);

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
