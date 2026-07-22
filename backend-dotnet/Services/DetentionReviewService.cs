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
        var d = await db.QuerySingleAsync(
            @"SELECT d.*, g.name AS fence_name, g.center_lat, g.center_lng, g.radius_meters,
                     c.name AS customer_name,
                     j.job_code, j.po_number, j.bol_number, j.rate_con_number, j.appointment_ref
              FROM detention_dwells d
              JOIN geofences g ON g.id=d.geofence_id AND g.company_id=d.company_id
              LEFT JOIN customers c ON c.id=d.customer_id AND c.company_id=d.company_id
              LEFT JOIN jobs j ON j.id=d.job_id AND j.company_id=d.company_id
              WHERE d.company_id=@c AND d.id=@id",
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
            appointment = new { plannedAt = Iso(d["appointmentAt"]), source = d["appointmentSource"]?.ToString(), arrivedAt = Iso(d["billedFromAt"]) },
            clock = new { rule = d["clockRule"]?.ToString(), clockStartAt = Iso(d["clockStartAt"]) },
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
        if (d["status"]?.ToString() != "priced_pending_review")
            return new DetentionActionOutcome(false, d["status"]?.ToString() ?? "unknown", "not_pending_review");
        if (d["jobId"] is null or DBNull)
            return new DetentionActionOutcome(false, "priced_pending_review", "unattributed_job_required");

        // AP-clerk gate 1: at least one shipper reference, or an audited override.
        var hasRef = new[] { "poNumber", "bolNumber", "rateConNumber", "appointmentRef" }
            .Any(k => d[k] is not null and not DBNull && !string.IsNullOrWhiteSpace(d[k]!.ToString()));
        if (!hasRef && string.IsNullOrWhiteSpace(overrideNote))
            return new DetentionActionOutcome(false, "priced_pending_review", "missing_references");

        // AP-clerk gate 2: claim-window compliance, or an audited override.
        var pastWindow = d["claimDeadlineAt"] is DateTime dl && DateTime.UtcNow > dl.ToUniversalTime();
        if (pastWindow && string.IsNullOrWhiteSpace(overrideNote))
            return new DetentionActionOutcome(false, "priced_pending_review", "claim_window_expired");

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
                  WHERE company_id=@c AND id=@id AND status='priced_pending_review'", conn, tx))
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
                                           detention_dwell_id, evidence_sha256)
                  VALUES (@c, @j, 'DETENTION', @name, 'accessorial',
                          @qty, @rate, @amt, @cur, 'approved', 'detention', 'unbilled', @dw, @sha)
                  RETURNING id", conn, tx))
            {
                ins.Parameters.AddWithValue("@c", companyId); ins.Parameters.AddWithValue("@j", jobId);
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
            await using (var check = new NpgsqlCommand(
                @"SELECT ii.customer_id FROM issued_invoice_lines iil
                  JOIN job_charges jc2 ON jc2.id = iil.job_charge_id AND jc2.company_id = iil.company_id
                  JOIN issued_invoices ii ON ii.id = iil.issued_invoice_id AND ii.company_id = iil.company_id
                  WHERE iil.company_id=@c AND jc2.job_id=@j
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
                (SELECT COALESCE(SUM(iil.amount),0) FROM invoice_draft_lines idl
                 JOIN issued_invoice_lines iil ON iil.job_charge_id = idl.job_charge_id AND iil.company_id = idl.company_id
                 JOIN job_charges jc ON jc.id = idl.job_charge_id AND jc.company_id = idl.company_id
                 WHERE idl.company_id=@c AND jc.source='detention') AS billed_amount,
                (SELECT COALESCE(SUM(iil.amount * CASE WHEN ii.total > 0 THEN LEAST(1, ii.amount_paid / ii.total) ELSE 0 END),0)
                 FROM issued_invoice_lines iil
                 JOIN issued_invoices ii ON ii.id = iil.issued_invoice_id AND ii.company_id = iil.company_id
                 JOIN job_charges jc ON jc.id = iil.job_charge_id AND jc.company_id = iil.company_id
                 WHERE iil.company_id=@c AND jc.source='detention') AS collected_amount",
            c => { c.Parameters.AddWithValue("@c", companyId); c.Parameters.AddWithValue("@f", from); c.Parameters.AddWithValue("@t", to); }, ct);
        return row ?? new Dictionary<string, object?>();
    }

    private async Task<string?> GetEvidenceShaAsync(long companyId, long dwellId, CancellationToken ct) =>
        (await db.QuerySingleAsync(
            "SELECT evidence_sha256 FROM detention_evidence WHERE company_id=@c AND dwell_id=@d",
            c => { c.Parameters.AddWithValue("@c", companyId); c.Parameters.AddWithValue("@d", dwellId); }, ct))?["evidenceSha256"]?.ToString();
}
