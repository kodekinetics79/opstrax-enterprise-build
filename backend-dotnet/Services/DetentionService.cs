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
                  WHERE d2.status IN ('open','closed','needs_appointment','unpriced_no_terms') AND d2.job_id IS NULL
                    AND da.assigned_at <= COALESCE(d2.billed_to_at, NOW())
                    AND COALESCE(da.actual_delivery_at, da.completed_at, 'infinity'::timestamptz) >= d2.entered_at
                  ORDER BY d2.id,
                           LEAST(COALESCE(da.actual_delivery_at, da.completed_at, d2.billed_to_at, NOW()), COALESCE(d2.billed_to_at, NOW()))
                         - GREATEST(da.assigned_at, d2.entered_at) DESC
              ) m
              WHERE d.id = m.dwell_id AND d.company_id = m.cid", ct: ct);

    // ── Phase B½ — CLOCK: billable time starts at LATER-OF(appointment, arrival); early arrival
    // never accrues. No appointment and no attestation -> 'needs_appointment' (detected, never priced). ──
    private static async Task ApplyClockAsync(Database db, CancellationToken ct)
    {
        // Pull the appointment from the attributed assignment's planned time by stop role — for OPEN
        // dwells too, so the pre-expiry notice can be appointment-anchored (consultant major: a notice
        // computed from bare arrival is a factually wrong customer-facing statement for early arrivals).
        await db.ExecuteAsync(
            @"UPDATE detention_dwells d
              SET appointment_at = CASE WHEN d.stop_role = 'dropoff' THEN da.planned_delivery_at ELSE da.planned_pickup_at END,
                  appointment_source = 'assignment_planned', updated_at = NOW()
              FROM dispatch_assignments da
              WHERE d.status IN ('open','closed','needs_appointment') AND d.appointment_at IS NULL AND d.appointment_source IS NULL
                AND da.id = d.dispatch_assignment_id AND da.company_id = d.company_id
                AND (CASE WHEN d.stop_role = 'dropoff' THEN da.planned_delivery_at ELSE da.planned_pickup_at END) IS NOT NULL", ct: ct);

        // Clock: later-of(appointment, arrival). Attested-none clocks from arrival. Open dwells get a
        // provisional clock (for the notice threshold); it is recomputed identically at close.
        await db.ExecuteAsync(
            @"UPDATE detention_dwells SET
                  clock_start_at = CASE WHEN appointment_source = 'attested_none' OR appointment_at IS NULL
                                        THEN billed_from_at
                                        ELSE GREATEST(appointment_at, billed_from_at) END,
                  clock_rule = 'later_of_appointment_arrival_v1', updated_at = NOW()
              WHERE status IN ('open','closed','needs_appointment') AND clock_start_at IS NULL AND billed_from_at IS NOT NULL
                AND (appointment_at IS NOT NULL OR appointment_source = 'attested_none')", ct: ct);
    }

    // ── Phase C½ — PRE-EXPIRY NOTICE: where detention money is legally won. Guarded stamp is the race lock. ──
    private static async Task NotifyPreExpiryAsync(Database db, CancellationToken ct)
    {
        // Spec gate: OPEN dwells with a resolved rule card AND a KNOWN clock start (appointment resolved
        // or attested) — never a bare-arrival guess (consultant major). Card resolution is as-of entered_at.
        var due = await db.QueryAsync(
            @"SELECT d.id, d.company_id, d.customer_id, g.name AS site_name,
                     rc.free_minutes, rc.rate_per_hour, rc.currency, rc.notice_percent,
                     d.clock_start_at AS clock_base,
                     c.contact_name, c.email AS contact_email
              FROM detention_dwells d
              JOIN geofences g ON g.id = d.geofence_id AND g.company_id = d.company_id
              JOIN customers c ON c.id = d.customer_id AND c.company_id = d.company_id
              JOIN LATERAL (
                  SELECT free_minutes, rate_per_hour, currency, notice_percent FROM detention_rule_cards rc
                  WHERE rc.company_id = d.company_id AND rc.active AND rc.effective_date <= d.entered_at::date
                    AND ((rc.scope_type = 'customer' AND rc.scope_id = d.customer_id) OR rc.scope_type = 'tenant')
                  ORDER BY CASE WHEN rc.scope_type = 'customer' THEN 0 ELSE 1 END, rc.effective_date DESC, rc.version DESC
                  LIMIT 1) rc ON TRUE
              WHERE d.status = 'open' AND d.warning_notified_at IS NULL AND d.clock_start_at IS NOT NULL
                AND NOW() >= d.clock_start_at + make_interval(mins => rc.free_minutes * rc.notice_percent / 100)",
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

            // One transaction: the guarded stamp (exactly one tick wins) + BOTH notice writes. A crash
            // can no longer stamp the dwell 'notified' while losing the contemporaneous-notice proof.
            await db.WithTransactionAsync<object?>(async (conn, tx) =>
            {
                long won;
                await using (var stamp = new Npgsql.NpgsqlCommand(
                    "UPDATE detention_dwells SET warning_notified_at=NOW(), updated_at=NOW() WHERE company_id=@c AND id=@id AND warning_notified_at IS NULL", conn, tx))
                {
                    stamp.Parameters.AddWithValue("@c", cid); stamp.Parameters.AddWithValue("@id", dwellId);
                    won = await stamp.ExecuteNonQueryAsync(ct);
                }
                if (won == 0) return null;

                await using (var notif = new Npgsql.NpgsqlCommand(
                    @"INSERT INTO notifications (company_id, event_type, severity, title, message, body, audience_type, channel, status, dedupe_key)
                      VALUES (@c, 'detention.warning', 'warning', @title, @body, @body, 'dispatch', 'in_app', 'unread', @dedupe)
                      ON CONFLICT DO NOTHING", conn, tx))
                {
                    notif.Parameters.AddWithValue("@c", cid);
                    notif.Parameters.AddWithValue("@title", $"Detention warning — {site}");
                    notif.Parameters.AddWithValue("@body", body);
                    notif.Parameters.AddWithValue("@dedupe", $"detention-warning-{dwellId}");
                    await notif.ExecuteNonQueryAsync(ct);
                }
                await using (var log = new Npgsql.NpgsqlCommand(
                    @"INSERT INTO detention_notices (company_id, dwell_id, notice_type, recipient_name, recipient_address, channel, body_snapshot, delivery_status)
                      VALUES (@c, @d, 'customer_meter_running', @name, @addr, 'email', @body, 'logged')
                      ON CONFLICT DO NOTHING", conn, tx))
                {
                    log.Parameters.AddWithValue("@c", cid); log.Parameters.AddWithValue("@d", dwellId);
                    log.Parameters.AddWithValue("@name", (object?)d["contactName"] ?? DBNull.Value);
                    log.Parameters.AddWithValue("@addr", (object?)d["contactEmail"] ?? DBNull.Value);
                    log.Parameters.AddWithValue("@body", body);
                    await log.ExecuteNonQueryAsync(ct);
                }
                // Durable delivery seam: the outbox handler attempts real email delivery and flips the
                // notice to 'sent'/'failed'. At-least-once redelivery is safe (delivery-status guarded).
                await using (var ob = new Npgsql.NpgsqlCommand(
                    @"INSERT INTO outbox_messages (tenant_id, event_type, aggregate_type, aggregate_id, payload_json, status, retry_count)
                      VALUES (@t, 'detention.dwell.warning', 'detention_dwell', @a, jsonb_build_object('dwellId', @a), 'pending', 0)
                      ON CONFLICT (tenant_id, aggregate_id) WHERE event_type='detention.dwell.warning' DO NOTHING", conn, tx))
                {
                    ob.Parameters.AddWithValue("@t", cid);
                    ob.Parameters.AddWithValue("@a", dwellId.ToString());
                    await ob.ExecuteNonQueryAsync(ct);
                }
                return null;
            }, ct);
        }
    }

    // ── Phase E — PRICE with the settle delay (a closed dwell rests through one merge window so
    // bounce re-entries actually merge). Fail-closed: no rule card -> 'unpriced_no_terms', no
    // appointment/attestation -> 'needs_appointment'; below free time is terminal. Round DOWN. ──
    private static async Task PriceDwellsAsync(Database db, CancellationToken ct)
    {
        // Also re-selects the fail-closed rest states so a later rule card or resolved appointment
        // recovers the revenue (consultant major: dead ends must be re-priceable, not dismiss-only).
        var settled = await db.QueryAsync(
            @"SELECT d.id, d.company_id, d.customer_id, d.status, d.billed_from_at, d.billed_to_at, d.clock_start_at,
                     d.appointment_at, d.appointment_source, d.entered_at
              FROM detention_dwells d
              WHERE d.status IN ('closed','unpriced_no_terms','needs_appointment')
                AND d.exited_at IS NOT NULL AND NOW() > d.exited_at + make_interval(mins => @gap)
              ORDER BY d.id",
            c => c.Parameters.AddWithValue("@gap", DefaultMergeGapMinutes), ct);

        foreach (var d in settled)
        {
            var cid = Convert.ToInt64(d["companyId"]);
            var dwellId = Convert.ToInt64(d["id"]);
            var custId = d["customerId"] is null or DBNull ? 0L : Convert.ToInt64(d["customerId"]);
            var prior = d["status"]?.ToString() ?? "closed";

            // Rule card as-of entered_at (spec: 'saving never retro-bills' beyond the card's effective date).
            var card = await db.QuerySingleAsync(
                @"SELECT id, version, free_minutes, rate_per_hour, currency, billing_increment_minutes,
                         max_charge_amount, claim_window_days, grace_minutes
                  FROM detention_rule_cards rc
                  WHERE rc.company_id=@c AND rc.active AND rc.effective_date <= @entered::date
                    AND ((rc.scope_type='customer' AND rc.scope_id=@cust) OR rc.scope_type='tenant')
                  ORDER BY CASE WHEN rc.scope_type='customer' THEN 0 ELSE 1 END, rc.effective_date DESC, rc.version DESC
                  LIMIT 1",
                c => { c.Parameters.AddWithValue("@c", cid); c.Parameters.AddWithValue("@cust", custId); c.Parameters.AddWithValue("@entered", Convert.ToDateTime(d["enteredAt"])); }, ct);
            if (card is null)
            {
                if (prior == "closed") await SetStatusAsync(db, cid, dwellId, prior, "unpriced_no_terms", ct);
                continue;
            }
            if (d["clockStartAt"] is null or DBNull)
            {
                // No appointment recorded and no attestation — detected and shown, never priced.
                if (prior != "needs_appointment") await SetStatusAsync(db, cid, dwellId, prior, "needs_appointment", ct);
                continue;
            }

            var clockStart = Convert.ToDateTime(d["clockStartAt"]);
            var billedTo = Convert.ToDateTime(d["billedToAt"]);
            var freeMin = Convert.ToInt32(card["freeMinutes"]);
            var increment = Math.Max(1, Convert.ToInt32(card["billingIncrementMinutes"]));
            var rate = Convert.ToDecimal(card["ratePerHour"]);
            var cap = card["maxChargeAmount"] is null or DBNull ? (decimal?)null : Convert.ToDecimal(card["maxChargeAmount"]);
            var claimDays = Convert.ToInt32(card["claimWindowDays"]);
            var graceMin = Convert.ToInt32(card["graceMinutes"]);

            var dwellMinutes = (int)Math.Max(0, (billedTo - clockStart).TotalMinutes);
            var billable = Math.Max(0, dwellMinutes - freeMin);
            billable = billable / increment * increment;   // round DOWN (every ambiguity in the customer's favor)

            if (billable <= 0)
            {
                await db.ExecuteAsync(
                    @"UPDATE detention_dwells SET status='below_free_time', dwell_minutes=@dm, free_minutes_applied=@fm,
                          billable_minutes=0, rule_card_id=@rc, rule_card_version=@ver, updated_at=NOW()
                      WHERE company_id=@c AND id=@id AND status=@prior",
                    c =>
                    {
                        c.Parameters.AddWithValue("@c", cid); c.Parameters.AddWithValue("@id", dwellId);
                        c.Parameters.AddWithValue("@prior", prior);
                        c.Parameters.AddWithValue("@dm", dwellMinutes); c.Parameters.AddWithValue("@fm", freeMin);
                        c.Parameters.AddWithValue("@rc", Convert.ToInt64(card["id"])); c.Parameters.AddWithValue("@ver", Convert.ToInt32(card["version"]));
                    }, ct);
                continue;
            }

            var qtyHours = Math.Round(billable / 60m, 3);
            var amount = Math.Round(qtyHours * rate, 2);
            if (cap.HasValue) amount = Math.Min(amount, cap.Value);

            // Late arrival (mandate + market blocker: 'your driver was late — detention void'): arrival
            // past appointment+grace rests in 'late_arrival' — math shown, unapprovable without override.
            var lateArrival = d["appointmentAt"] is DateTime appt
                              && d["billedFromAt"] is DateTime arr
                              && arr > appt.AddMinutes(graceMin);
            var target = lateArrival ? "late_arrival" : "priced_pending_review";

            var priced = await db.ExecuteAsync(
                @"UPDATE detention_dwells SET status=@target,
                      dwell_minutes=@dm, free_minutes_applied=@fm, billable_minutes=@bm,
                      rule_card_id=@rc, rule_card_version=@ver,
                      quantity_hours=@qty, unit_rate=@rate, amount=@amt, currency=@cur,
                      claim_deadline_at = exited_at + make_interval(days => @cw), updated_at=NOW()
                  WHERE company_id=@c AND id=@id AND status=@prior",
                c =>
                {
                    c.Parameters.AddWithValue("@c", cid); c.Parameters.AddWithValue("@id", dwellId);
                    c.Parameters.AddWithValue("@prior", prior); c.Parameters.AddWithValue("@target", target);
                    c.Parameters.AddWithValue("@dm", dwellMinutes); c.Parameters.AddWithValue("@fm", freeMin);
                    c.Parameters.AddWithValue("@bm", billable);
                    c.Parameters.AddWithValue("@rc", Convert.ToInt64(card["id"])); c.Parameters.AddWithValue("@ver", Convert.ToInt32(card["version"]));
                    c.Parameters.AddWithValue("@qty", qtyHours); c.Parameters.AddWithValue("@rate", rate);
                    c.Parameters.AddWithValue("@amt", amount); c.Parameters.AddWithValue("@cur", card["currency"]?.ToString() ?? "USD");
                    c.Parameters.AddWithValue("@cw", claimDays);
                }, ct);

            // Evidence freezes ONLY when THIS tick won the pricing update (consultant blocker: a raced
            // 0-row update must never freeze a bundle from a reopened/unpriced dwell).
            if (priced > 0)
            {
                var sha = await new DetentionReviewService(db).BuildEvidenceAsync(cid, dwellId, ct);
                // External tamper anchor: one durable outbox event per dwell carrying the full sha.
                if (sha is not null)
                    await db.ExecuteAsync(
                        @"INSERT INTO outbox_messages (tenant_id, event_type, aggregate_type, aggregate_id, payload_json, status, retry_count)
                          VALUES (@t, 'detention.dwell.priced', 'detention_dwell', @a,
                                  jsonb_build_object('dwellId', @a, 'amount', @amt, 'evidenceSha256', @sha), 'pending', 0)
                          ON CONFLICT (tenant_id, aggregate_id) WHERE event_type='detention.dwell.priced' DO NOTHING",
                        c =>
                        {
                            c.Parameters.AddWithValue("@t", cid);
                            c.Parameters.AddWithValue("@a", dwellId.ToString());
                            c.Parameters.AddWithValue("@amt", amount);
                            c.Parameters.AddWithValue("@sha", sha);
                        }, ct);
            }
        }
    }

    private static Task SetStatusAsync(Database db, long cid, long dwellId, string priorStatus, string status, CancellationToken ct) =>
        db.ExecuteAsync(
            "UPDATE detention_dwells SET status=@s, updated_at=NOW() WHERE company_id=@c AND id=@id AND status=@prior",
            c => { c.Parameters.AddWithValue("@c", cid); c.Parameters.AddWithValue("@id", dwellId); c.Parameters.AddWithValue("@s", status); c.Parameters.AddWithValue("@prior", priorStatus); }, ct);

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

    // ── Phase C — GPS-GAP then TIMEOUT: NEVER bill un-witnessed time; forced review.
    // Consultant fixes applied: gps-gap runs FIRST (so a dark-then-timed-out dwell bills to the last
    // witnessed moment, not NOW()); both closes verify the position is INSIDE the fence (an outside or
    // absent position bills only to entry — the last provably-inside moment); truncated derives from a
    // fresh INSIDE position, not mere position freshness. ──
    private static async Task TimeoutDwellsAsync(Database db, CancellationToken ct)
    {
        // GPS gap first: position stale while dwell open — close at the last INSIDE-witnessed time.
        // An outside/unknown last position means departure is un-witnessed: bill only to entry.
        await db.ExecuteAsync(
            @"UPDATE detention_dwells d SET status='closed', close_reason='gps_gap',
                  exited_at   = w.bill_to, departure_lower = w.bill_to, billed_to_at = w.bill_to,
                  review_required=TRUE, updated_at=NOW()
              FROM (SELECT d2.id AS dwell_id,
                           GREATEST(d2.entered_at,
                               CASE WHEN 2 * 6371000 * asin(sqrt(
                                         power(sin(radians(p.lat - g.center_lat) / 2), 2) +
                                         cos(radians(g.center_lat)) * cos(radians(p.lat)) *
                                         power(sin(radians(p.lng - g.center_lng) / 2), 2))) <= g.radius_meters
                                    THEN p.event_time ELSE d2.entered_at END) AS bill_to
                    FROM detention_dwells d2
                    JOIN geofences g ON g.id = d2.geofence_id AND g.company_id = d2.company_id
                    JOIN latest_vehicle_positions p ON p.company_id = d2.company_id AND p.vehicle_id = d2.vehicle_id
                    WHERE d2.status='open' AND p.event_time < NOW() - make_interval(mins => @gap)) w
              WHERE d.id = w.dwell_id AND d.status='open'",
            c => c.Parameters.AddWithValue("@gap", GpsGapMinutes), ct);

        // Timeout second: open too long. Bill to the last INSIDE-witnessed position time (never NOW()
        // unless a fresh inside position proves the vehicle is genuinely still there -> truncated).
        await db.ExecuteAsync(
            @"UPDATE detention_dwells d SET status='closed', close_reason='timeout',
                  exited_at   = GREATEST(d.entered_at, COALESCE(w.witnessed_to, d.entered_at)),
                  departure_lower = GREATEST(d.entered_at, COALESCE(w.witnessed_to, d.entered_at)),
                  billed_to_at    = GREATEST(d.entered_at, COALESCE(w.witnessed_to, d.entered_at)),
                  truncated = COALESCE(w.still_inside, FALSE),
                  review_required=TRUE, updated_at=NOW()
              FROM (SELECT d2.id AS dwell_id,
                           CASE WHEN ins.inside AND p2.event_time > NOW() - make_interval(mins => @gap)
                                THEN NOW()                                   -- provably still inside: bill to now, disclose truncation
                                WHEN ins.inside THEN p2.event_time            -- last witnessed inside moment
                                ELSE NULL END AS witnessed_to,               -- outside/unknown: entry only
                           (ins.inside AND p2.event_time > NOW() - make_interval(mins => @gap)) AS still_inside
                    FROM detention_dwells d2
                    JOIN geofences g ON g.id = d2.geofence_id AND g.company_id = d2.company_id
                    LEFT JOIN latest_vehicle_positions p2 ON p2.company_id = d2.company_id AND p2.vehicle_id = d2.vehicle_id
                    LEFT JOIN LATERAL (
                        SELECT (p2.lat IS NOT NULL AND
                                2 * 6371000 * asin(sqrt(
                                    power(sin(radians(p2.lat - g.center_lat) / 2), 2) +
                                    cos(radians(g.center_lat)) * cos(radians(p2.lat)) *
                                    power(sin(radians(p2.lng - g.center_lng) / 2), 2))) <= g.radius_meters) AS inside
                    ) ins ON TRUE
                    WHERE d2.status='open' AND d2.entered_at < NOW() - make_interval(hours => @maxh)) w
              WHERE d.id = w.dwell_id AND d.status='open'",
            c => { c.Parameters.AddWithValue("@maxh", DefaultMaxDwellHours); c.Parameters.AddWithValue("@gap", GpsGapMinutes); }, ct);
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
