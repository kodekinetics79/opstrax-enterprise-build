using System.Globalization;
using System.Text.Json;
using Npgsql;
using Opstrax.Api.Data;

namespace Opstrax.Api.Services;

public sealed record DetentionActionOutcome(
    bool Ok, string Status, string? Reason = null, long? JobChargeId = null, Guid? SupplementalDraftId = null);

// Detention Recovery — review + approval + evidence + funnel (Increment 3 of the consultant-signed
// spec). The approval queue is the ONLY path to billing (mandate 4): a priced dwell becomes a
// source='detention' job_charge only when a human approves, with the AP-clerk gates enforced —
// shipper references present (or an audited override), claim-window compliance (or an audited
// override) — and the frozen evidence sha welded onto the charge. Charges whose job is already
// invoiced route to a SUPPLEMENTAL draft (the consultant major: they do NOT 'ride the next cycle').
public sealed class DetentionReviewService(Database db)
{
    // ── Evidence: canonical JSON frozen at pricing; immutable by DB trigger. ──
    public async Task<string?> BuildEvidenceAsync(long companyId, long dwellId, CancellationToken ct = default)
    {
        // Defensive status guard (consultant blocker): evidence can only freeze from a PRICED dwell —
        // a raced merge-reopen between pricing SELECT and UPDATE can never produce a garbage bundle.
        var d = await db.QuerySingleAsync(
            @"SELECT d.*, g.name AS fence_name, g.center_lat, g.center_lng, g.radius_meters,
                     c.name AS customer_name,
                     j.job_code, j.po_number, j.bol_number, j.rate_con_number, j.appointment_ref,
                     rc.free_minutes AS rc_free, rc.rate_per_hour AS rc_rate, rc.billing_increment_minutes AS rc_inc,
                     rc.max_charge_amount AS rc_cap, rc.claim_window_days AS rc_cw, rc.grace_minutes AS rc_grace,
                     rc.currency AS rc_currency
              FROM detention_dwells d
              JOIN geofences g ON g.id=d.geofence_id AND g.company_id=d.company_id
              LEFT JOIN customers c ON c.id=d.customer_id AND c.company_id=d.company_id
              LEFT JOIN jobs j ON j.id=d.job_id AND j.company_id=d.company_id
              LEFT JOIN detention_rule_cards rc ON rc.id=d.rule_card_id AND rc.company_id=d.company_id
              WHERE d.company_id=@c AND d.id=@id AND d.status IN ('priced_pending_review','late_arrival','approved','charged')",
            cmd => { cmd.Parameters.AddWithValue("@c", companyId); cmd.Parameters.AddWithValue("@id", dwellId); }, ct);
        if (d is null) return null;

        var notices = await db.QueryAsync(
            "SELECT notice_type, recipient_name, recipient_address, channel, body_snapshot, delivery_status, created_at FROM detention_notices WHERE company_id=@c AND dwell_id=@id ORDER BY created_at",
            cmd => { cmd.Parameters.AddWithValue("@c", companyId); cmd.Parameters.AddWithValue("@id", dwellId); }, ct);

        // Deterministic canonical form: fixed key order, UTC ISO-8601, invariant numbers.
        string Iso(object? v) => v is DateTime t ? t.ToUniversalTime().ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture) : "";
        var canonicalObj = new
        {
            schemaVersion = 2,
            companyId,
            dwellId,
            customer = new { id = d["customerId"], name = d["customerName"]?.ToString() },
            geofence = new { id = d["geofenceId"], name = d["fenceName"]?.ToString(), centerLat = d["centerLat"], centerLng = d["centerLng"], radiusMeters = d["radiusMeters"] },
            vehicleId = d["vehicleId"],
            job = new { id = d["jobId"], jobCode = d["jobCode"]?.ToString(), references = new { poNumber = d["poNumber"]?.ToString(), bolNumber = d["bolNumber"]?.ToString(), rateConNumber = d["rateConNumber"]?.ToString(), appointmentRef = d["appointmentRef"]?.ToString() } },
            appointment = new
            {
                plannedAt = Iso(d["appointmentAt"]),
                source = d["appointmentSource"]?.ToString(),
                arrivedAt = Iso(d["billedFromAt"]),
                // The AP clerk's first check: was the truck on time? (variance > 0 = late arrival)
                onTime = d["appointmentAt"] is DateTime ap0 && d["billedFromAt"] is DateTime ar0 ? (bool?)(ar0 <= ap0) : null,
                varianceMinutes = d["appointmentAt"] is DateTime ap1 && d["billedFromAt"] is DateTime ar1 ? (int?)(int)(ar1 - ap1).TotalMinutes : null,
                graceMinutes = d["rcGrace"] is null or DBNull ? (int?)null : Convert.ToInt32(d["rcGrace"]),
            },
            clock = new
            {
                rule = d["clockRule"]?.ToString(),
                clockStartAt = Iso(d["clockStartAt"]),
                earlyArrivalExcludedMinutes = d["appointmentAt"] is DateTime ap2 && d["billedFromAt"] is DateTime ar2 && ar2 < ap2 ? (int?)(int)(ap2 - ar2).TotalMinutes : 0,
            },
            ruleCardSnapshot = new
            {
                id = d["ruleCardId"], version = d["ruleCardVersion"],
                freeMinutes = d["rcFree"], ratePerHour = d["rcRate"]?.ToString(),
                incrementMinutes = d["rcInc"], roundingDirection = "down",
                maxChargeAmount = d["rcCap"]?.ToString(), claimWindowDays = d["rcCw"],
                currency = d["rcCurrency"]?.ToString(),
            },
            intervals = new { billedFrom = Iso(d["billedFromAt"]), billedTo = Iso(d["billedToAt"]), note = "billed from the shortest provable interval" },
            noticeLog = notices.Select(n => new { type = n["noticeType"]?.ToString(), recipient = n["recipientName"]?.ToString(), address = n["recipientAddress"]?.ToString(), channel = n["channel"]?.ToString(), body = n["bodySnapshot"]?.ToString(), status = n["deliveryStatus"]?.ToString(), loggedAt = Iso(n["createdAt"]) }).ToArray(),
            computation = new
            {
                dwellMinutes = d["dwellMinutes"], freeMinutesApplied = d["freeMinutesApplied"], billableMinutes = d["billableMinutes"],
                quantityHours = d["quantityHours"]?.ToString(), unitRate = d["unitRate"]?.ToString(), amount = d["amount"]?.ToString(), currency = d["currency"]?.ToString(),
                ruleCardId = d["ruleCardId"], ruleCardVersion = d["ruleCardVersion"],
            },
            claimWindow = new { deadlineAt = Iso(d["claimDeadlineAt"]) },
        };
        var canonical = JsonSerializer.Serialize(canonicalObj);
        var sha = Opstrax.Api.TelemetryHmacHelper.Sha256Hex(canonical);

        await db.ExecuteAsync(
            @"INSERT INTO detention_evidence (company_id, dwell_id, evidence_canonical, evidence_json, evidence_sha256)
              VALUES (@c, @d, @canon, @json::jsonb, @sha)
              ON CONFLICT (company_id, dwell_id) DO NOTHING",
            cmd =>
            {
                cmd.Parameters.AddWithValue("@c", companyId); cmd.Parameters.AddWithValue("@d", dwellId);
                cmd.Parameters.AddWithValue("@canon", canonical); cmd.Parameters.AddWithValue("@json", canonical);
                cmd.Parameters.AddWithValue("@sha", sha);
            }, ct);
        return sha;
    }

    // ── APPROVE: the only path to money. Guards: attributed job, references, claim window. ──
    public async Task<DetentionActionOutcome> ApproveAsync(long companyId, long dwellId, long userId, string? overrideNote, CancellationToken ct = default)
    {
        var d = await db.QuerySingleAsync(
            @"SELECT d.status, d.job_id, d.quantity_hours, d.unit_rate, d.amount, d.currency, d.claim_deadline_at,
                     g.name AS fence_name, j.po_number, j.bol_number, j.rate_con_number, j.appointment_ref
              FROM detention_dwells d
              JOIN geofences g ON g.id=d.geofence_id AND g.company_id=d.company_id
              LEFT JOIN jobs j ON j.id=d.job_id AND j.company_id=d.company_id
              WHERE d.company_id=@c AND d.id=@id",
            cmd => { cmd.Parameters.AddWithValue("@c", companyId); cmd.Parameters.AddWithValue("@id", dwellId); }, ct);
        if (d is null) return new DetentionActionOutcome(false, "missing", "not_found");
        var status = d["status"]?.ToString() ?? "unknown";
        if (status is not ("priced_pending_review" or "late_arrival"))
            return new DetentionActionOutcome(false, status, "not_pending_review");
        // Late arrival (market blocker): 'your driver was late — detention void' is the standard denial.
        // A late_arrival dwell is unapprovable without an explicit override note.
        if (status == "late_arrival" && string.IsNullOrWhiteSpace(overrideNote))
            return new DetentionActionOutcome(false, status, "late_arrival_override_required");
        if (d["jobId"] is null or DBNull)
            return new DetentionActionOutcome(false, status, "unattributed_job_required");

        // AP-clerk gate 1: at least one shipper reference, or an audited override.
        var hasRef = new[] { "poNumber", "bolNumber", "rateConNumber", "appointmentRef" }
            .Any(k => d[k] is not null and not DBNull && !string.IsNullOrWhiteSpace(d[k]!.ToString()));
        if (!hasRef && string.IsNullOrWhiteSpace(overrideNote))
            return new DetentionActionOutcome(false, status, "missing_references");

        // AP-clerk gate 2: claim-window compliance, or an audited override.
        var pastWindow = d["claimDeadlineAt"] is DateTime dl && DateTime.UtcNow > dl.ToUniversalTime();
        if (pastWindow && string.IsNullOrWhiteSpace(overrideNote))
            return new DetentionActionOutcome(false, status, "claim_window_expired");

        var jobId = Convert.ToInt64(d["jobId"]);
        var sha = await GetEvidenceShaAsync(companyId, dwellId, ct)
                  ?? await BuildEvidenceAsync(companyId, dwellId, ct);

        return await db.WithTransactionAsync(async (conn, tx) =>
        {
            // The status guard IS the double-approve lock.
            long approvedRows;
            await using (var upd = new NpgsqlCommand(
                @"UPDATE detention_dwells SET status='approved', reviewed_by_user_id=@u, reviewed_at=NOW(),
                      review_note=@note, updated_at=NOW()
                  WHERE company_id=@c AND id=@id AND status IN ('priced_pending_review','late_arrival')", conn, tx))
            {
                upd.Parameters.AddWithValue("@c", companyId); upd.Parameters.AddWithValue("@id", dwellId);
                upd.Parameters.AddWithValue("@u", userId); upd.Parameters.AddWithValue("@note", (object?)overrideNote ?? DBNull.Value);
                approvedRows = await upd.ExecuteNonQueryAsync(ct);
            }
            if (approvedRows == 0) return new DetentionActionOutcome(false, "conflict", "already_reviewed");

            long chargeId;
            await using (var ins = new NpgsqlCommand(
                @"INSERT INTO job_charges (company_id, job_id, charge_code, charge_name, charge_type,
                                           quantity, unit_rate, amount, currency, status, source, billing_status,
                                           detention_dwell_id, evidence_sha256, approved_by_user_id)
                  VALUES (@c, @j, 'DETENTION', @name, 'accessorial',
                          @qty, @rate, @amt, @cur, 'approved', 'detention', 'unbilled', @dw, @sha, @u)
                  RETURNING id", conn, tx))
            {
                ins.Parameters.AddWithValue("@c", companyId); ins.Parameters.AddWithValue("@j", jobId);
                ins.Parameters.AddWithValue("@u", userId);
                ins.Parameters.AddWithValue("@name", $"Detention — {d["fenceName"]}");
                ins.Parameters.AddWithValue("@qty", d["quantityHours"] ?? (object)DBNull.Value);
                ins.Parameters.AddWithValue("@rate", d["unitRate"] ?? (object)DBNull.Value);
                ins.Parameters.AddWithValue("@amt", d["amount"] ?? (object)DBNull.Value);
                ins.Parameters.AddWithValue("@cur", d["currency"]?.ToString() ?? "USD");
                ins.Parameters.AddWithValue("@dw", dwellId);
                ins.Parameters.AddWithValue("@sha", (object?)sha ?? DBNull.Value);
                chargeId = Convert.ToInt64((await ins.ExecuteScalarAsync(ct))!);
            }

            await using (var link = new NpgsqlCommand(
                "UPDATE detention_dwells SET status='charged', job_charge_id=@ch, updated_at=NOW() WHERE company_id=@c AND id=@id", conn, tx))
            {
                link.Parameters.AddWithValue("@c", companyId); link.Parameters.AddWithValue("@id", dwellId);
                link.Parameters.AddWithValue("@ch", chargeId);
                await link.ExecuteNonQueryAsync(ct);
            }

            // Supplemental path (consultant major): a job already invoiced never re-enters its old
            // consolidation group — route the charge to its own supplemental draft immediately.
            Guid? suppId = null;
            // Widened (consultant major): the group is unusable not only when the job is ISSUED, but also
            // when its consolidation draft has left 'draft' (mid-review/approved) — the issued-group lock
            // would strand the charge either way. Any locked group routes to a supplemental draft NOW.
            await using (var check = new NpgsqlCommand(
                @"SELECT customer_id FROM (
                      SELECT ii.customer_id FROM issued_invoice_lines iil
                      JOIN job_charges jc2 ON jc2.id = iil.job_charge_id AND jc2.company_id = iil.company_id
                      JOIN issued_invoices ii ON ii.id = iil.issued_invoice_id AND ii.company_id = iil.company_id
                      WHERE iil.company_id=@c AND jc2.job_id=@j
                      UNION ALL
                      SELECT idr.customer_id FROM invoice_draft_lines idl
                      JOIN job_charges jc3 ON jc3.id = idl.job_charge_id AND jc3.company_id = idl.company_id
                      JOIN invoice_drafts idr ON idr.id = idl.invoice_draft_id AND idr.company_id = idl.company_id
                      WHERE idl.company_id=@c AND jc3.job_id=@j AND idr.status NOT IN ('draft')
                  ) locked
                  LIMIT 1", conn, tx))
            {
                check.Parameters.AddWithValue("@c", companyId); check.Parameters.AddWithValue("@j", jobId);
                var custObj = await check.ExecuteScalarAsync(ct);
                if (custObj is not null and not DBNull)
                {
                    var custId = Convert.ToInt64(custObj);
                    var draftNo = $"DET-SUPP-{companyId}-{dwellId}";
                    await using var draft = new NpgsqlCommand(
                        @"INSERT INTO invoice_drafts (company_id, customer_id, invoice_draft_no, status, currency, subtotal, tax_total, total, source)
                          VALUES (@c, @cust, @dno, 'draft', @cur, @amt, 0, @amt, 'detention_supplemental')
                          ON CONFLICT DO NOTHING
                          RETURNING id", conn, tx);
                    draft.Parameters.AddWithValue("@c", companyId); draft.Parameters.AddWithValue("@cust", custId);
                    draft.Parameters.AddWithValue("@dno", draftNo);
                    draft.Parameters.AddWithValue("@cur", d["currency"]?.ToString() ?? "USD");
                    draft.Parameters.AddWithValue("@amt", d["amount"] ?? (object)DBNull.Value);
                    var did = await draft.ExecuteScalarAsync(ct);
                    if (did is not null and not DBNull)
                    {
                        suppId = (Guid)did;
                        await using var line = new NpgsqlCommand(
                            @"INSERT INTO invoice_draft_lines (company_id, invoice_draft_id, job_charge_id, line_no, description, charge_code, quantity, unit_rate, amount)
                              VALUES (@c, @d, @ch, 1, @desc, 'DETENTION', @qty, @rate, @amt)", conn, tx);
                        line.Parameters.AddWithValue("@c", companyId); line.Parameters.AddWithValue("@d", suppId);
                        line.Parameters.AddWithValue("@ch", chargeId);
                        line.Parameters.AddWithValue("@desc", $"Detention — {d["fenceName"]} (supplemental)");
                        line.Parameters.AddWithValue("@qty", d["quantityHours"] ?? (object)DBNull.Value);
                        line.Parameters.AddWithValue("@rate", d["unitRate"] ?? (object)DBNull.Value);
                        line.Parameters.AddWithValue("@amt", d["amount"] ?? (object)DBNull.Value);
                        await line.ExecuteNonQueryAsync(ct);
                        await using var flip = new NpgsqlCommand(
                            "UPDATE job_charges SET billing_status='drafted' WHERE company_id=@c AND id=@ch", conn, tx);
                        flip.Parameters.AddWithValue("@c", companyId); flip.Parameters.AddWithValue("@ch", chargeId);
                        await flip.ExecuteNonQueryAsync(ct);
                    }
                }
            }

            return new DetentionActionOutcome(true, "charged", JobChargeId: chargeId, SupplementalDraftId: suppId);
        }, ct);
    }

    public async Task<DetentionActionOutcome> DismissAsync(long companyId, long dwellId, long userId, string reason, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(reason)) return new DetentionActionOutcome(false, "invalid", "reason_required");
        var rows = await db.ExecuteAsync(
            @"UPDATE detention_dwells SET status='dismissed', reviewed_by_user_id=@u, reviewed_at=NOW(), review_note=@r, updated_at=NOW()
              WHERE company_id=@c AND id=@id AND status IN ('priced_pending_review','unpriced_no_terms','needs_appointment','below_free_time')",
            c => { c.Parameters.AddWithValue("@c", companyId); c.Parameters.AddWithValue("@id", dwellId); c.Parameters.AddWithValue("@u", userId); c.Parameters.AddWithValue("@r", reason); }, ct);
        return rows > 0 ? new DetentionActionOutcome(true, "dismissed") : new DetentionActionOutcome(false, "conflict", "not_dismissable");
    }

    // ── Share: server-generated 256-bit token; expiring, revocable (trigger allows only these fields). ──
    public async Task<string?> MintShareTokenAsync(long companyId, long dwellId, int daysValid, CancellationToken ct = default)
    {
        var token = Convert.ToHexString(System.Security.Cryptography.RandomNumberGenerator.GetBytes(32)).ToLowerInvariant();
        var rows = await db.ExecuteAsync(
            @"UPDATE detention_evidence SET share_token=@t, share_expires_at=NOW() + make_interval(days => @days)
              WHERE company_id=@c AND dwell_id=@d",
            c => { c.Parameters.AddWithValue("@c", companyId); c.Parameters.AddWithValue("@d", dwellId); c.Parameters.AddWithValue("@t", token); c.Parameters.AddWithValue("@days", daysValid); }, ct);
        return rows > 0 ? token : null;
    }

    public async Task<Dictionary<string, object?>?> GetEvidenceByTokenAsync(string token, CancellationToken ct = default) =>
        await db.QuerySingleAsync(
            @"SELECT evidence_json, evidence_sha256, created_at FROM detention_evidence
              WHERE share_token=@t AND (share_expires_at IS NULL OR share_expires_at > NOW())",
            c => c.Parameters.AddWithValue("@t", token), ct);

    // ── Funnel: detected -> notified -> billed -> collected (the flagship metric). ──
    public async Task<Dictionary<string, object?>> FunnelAsync(long companyId, DateTime from, DateTime to, CancellationToken ct = default)
    {
        var row = await db.QuerySingleAsync(
            @"SELECT
                (SELECT COUNT(*) FROM detention_dwells WHERE company_id=@c AND entered_at BETWEEN @f AND @t AND status <> 'open') AS detected_count,
                (SELECT COALESCE(SUM(amount),0) FROM detention_dwells WHERE company_id=@c AND entered_at BETWEEN @f AND @t AND amount IS NOT NULL) AS detected_amount,
                (SELECT COUNT(*) FROM detention_dwells WHERE company_id=@c AND entered_at BETWEEN @f AND @t AND warning_notified_at IS NOT NULL) AS notified_count,
                (SELECT COALESCE(SUM(jc.amount),0) FROM job_charges jc
                 WHERE jc.company_id=@c AND jc.source='detention' AND jc.detention_dwell_id IS NOT NULL
                   AND jc.created_at BETWEEN @f AND @t) AS approved_amount,
                (SELECT COALESCE(SUM(iil.amount),0) FROM issued_invoice_lines iil
                 JOIN issued_invoices ii ON ii.id = iil.issued_invoice_id AND ii.company_id = iil.company_id
                 JOIN job_charges jc ON jc.id = iil.job_charge_id AND jc.company_id = iil.company_id
                 WHERE iil.company_id=@c AND jc.source='detention'
                   AND ii.issued_at BETWEEN @f AND @t) AS billed_amount,
                (SELECT COALESCE(SUM(iil.amount * CASE WHEN ii.total > 0 THEN LEAST(1, ii.amount_paid / ii.total) ELSE 0 END),0)
                 FROM issued_invoice_lines iil
                 JOIN issued_invoices ii ON ii.id = iil.issued_invoice_id AND ii.company_id = iil.company_id
                 JOIN job_charges jc ON jc.id = iil.job_charge_id AND jc.company_id = iil.company_id
                 WHERE iil.company_id=@c AND jc.source='detention'
                   AND ii.issued_at BETWEEN @f AND @t) AS collected_amount",
            c => { c.Parameters.AddWithValue("@c", companyId); c.Parameters.AddWithValue("@f", from); c.Parameters.AddWithValue("@t", to); }, ct);
        return row ?? new Dictionary<string, object?>();
    }

    // ── Dead-end resolution (consultant major): fail-closed rest states must be recoverable. Each
    // resolver resets the dwell to 'closed' (clearing clock/pricing) so the next tick re-attributes,
    // re-clocks and re-prices it. All are audited by the calling endpoint. ──
    public async Task<DetentionActionOutcome> SetAppointmentAsync(long companyId, long dwellId, DateTime appointmentAt, CancellationToken ct = default)
    {
        var rows = await db.ExecuteAsync(
            @"UPDATE detention_dwells SET appointment_at=@a, appointment_source='manual',
                  clock_start_at=NULL, status='closed', updated_at=NOW()
              WHERE company_id=@c AND id=@id AND status IN ('needs_appointment','unpriced_no_terms','closed','priced_pending_review','late_arrival')
                AND job_charge_id IS NULL",
            c => { c.Parameters.AddWithValue("@c", companyId); c.Parameters.AddWithValue("@id", dwellId); c.Parameters.AddWithValue("@a", appointmentAt); }, ct);
        return rows > 0 ? new DetentionActionOutcome(true, "closed") : new DetentionActionOutcome(false, "conflict", "not_resolvable");
    }

    public async Task<DetentionActionOutcome> AttestNoAppointmentAsync(long companyId, long dwellId, CancellationToken ct = default)
    {
        var rows = await db.ExecuteAsync(
            @"UPDATE detention_dwells SET appointment_source='attested_none', appointment_at=NULL,
                  clock_start_at=NULL, status='closed', updated_at=NOW()
              WHERE company_id=@c AND id=@id AND status IN ('needs_appointment','unpriced_no_terms')
                AND job_charge_id IS NULL",
            c => { c.Parameters.AddWithValue("@c", companyId); c.Parameters.AddWithValue("@id", dwellId); }, ct);
        return rows > 0 ? new DetentionActionOutcome(true, "closed") : new DetentionActionOutcome(false, "conflict", "not_resolvable");
    }

    public async Task<DetentionActionOutcome> AttachJobAsync(long companyId, long dwellId, long jobId, CancellationToken ct = default)
    {
        // The job must belong to the fence's customer (same validation the automatic attribution applies).
        var valid = await db.ScalarLongAsync(
            @"SELECT COUNT(*) FROM detention_dwells d
              JOIN jobs j ON j.company_id = d.company_id AND j.id = @j
              WHERE d.company_id=@c AND d.id=@id AND j.customer_id = d.customer_id",
            c => { c.Parameters.AddWithValue("@c", companyId); c.Parameters.AddWithValue("@id", dwellId); c.Parameters.AddWithValue("@j", jobId); }, ct);
        if (valid == 0) return new DetentionActionOutcome(false, "invalid", "job_customer_mismatch");

        var rows = await db.ExecuteAsync(
            @"UPDATE detention_dwells SET job_id=@j, clock_start_at=NULL, status='closed', updated_at=NOW()
              WHERE company_id=@c AND id=@id AND status IN ('needs_appointment','unpriced_no_terms','closed')
                AND job_charge_id IS NULL",
            c => { c.Parameters.AddWithValue("@c", companyId); c.Parameters.AddWithValue("@id", dwellId); c.Parameters.AddWithValue("@j", jobId); }, ct);
        return rows > 0 ? new DetentionActionOutcome(true, "closed") : new DetentionActionOutcome(false, "conflict", "not_resolvable");
    }

    // Stranded (consultant major): approved-but-unbilled detention charges that no consolidation run
    // will ever pick up — surfaced, never silently lost.
    public async Task<List<Dictionary<string, object?>>> StrandedAsync(long companyId, CancellationToken ct = default) =>
        await db.QueryAsync(
            @"SELECT jc.id AS charge_id, jc.job_id, jc.amount, jc.currency, jc.created_at, j.job_code
              FROM job_charges jc
              JOIN jobs j ON j.id = jc.job_id AND j.company_id = jc.company_id
              WHERE jc.company_id=@c AND jc.source='detention' AND jc.billing_status='unbilled'
                AND NOT EXISTS (SELECT 1 FROM invoice_draft_lines idl
                                WHERE idl.company_id=jc.company_id AND idl.job_charge_id=jc.id)
              ORDER BY jc.created_at",
            c => c.Parameters.AddWithValue("@c", companyId), ct);

    private async Task<string?> GetEvidenceShaAsync(long companyId, long dwellId, CancellationToken ct) =>
        (await db.QuerySingleAsync(
            "SELECT evidence_sha256 FROM detention_evidence WHERE company_id=@c AND dwell_id=@d",
            c => { c.Parameters.AddWithValue("@c", companyId); c.Parameters.AddWithValue("@d", dwellId); }, ct))?["evidenceSha256"]?.ToString();
}
