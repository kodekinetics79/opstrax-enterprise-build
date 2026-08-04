using System.Globalization;
using System.Text.Json;
using Npgsql;
using Opstrax.Api.Data;
using Opstrax.Api.Foundation;

namespace Opstrax.Api.Services;

public sealed record CreditNoteOutcome(
    bool Ok, string Status, string? Reason = null,
    Guid? CreditNoteDraftId = null, Guid? CreditNoteId = null, long? ApprovalRequestId = null,
    decimal CreditTotal = 0, decimal Relieved = 0, decimal RefundDue = 0, bool Replay = false);

// AR credit notes (SME-designed, consultant conventions):
//  - Sign convention: the credit-note issued_invoices row stores NEGATIVE subtotal/tax/total with
//    balance_due=0, so every existing SUM(total)/SUM(balance_due)/aging query stays correct unchanged.
//  - Balance effect lives on the ORIGINAL: balance_due = total - amount_paid - credit_total (new
//    column), so payment recording can never resurrect credited balance.
//  - Paid invoices CAN be credited: the portion beyond the open balance becomes a refund liability
//    (Cr 2100 Customer Refunds Payable in the GL), recorded as refundDue in CN metadata.
//  - Tax reverses PROPORTIONALLY FROM THE ORIGINAL'S SNAPSHOT (never recomputed at today's rates).
//  - Lineage: a first-class credit-note DRAFT (document_type='credit_note') satisfies the append-only
//    draft->issued chain and the UNIQUE(company_id, source_invoice_draft_id) constraint.
//  - Maker-checker: the approver must differ from the requester (checked against approval_requests).
//  - Cap: cumulative credits can never exceed the original's total (serialized FOR UPDATE).
public sealed class CreditNoteService(Database db, IApprovalWorkflowService approval, IDomainEventPublisher events)
{
    public async Task<CreditNoteOutcome> CreateCreditNoteAsync(
        long companyId, Guid invoiceId, decimal? amount, string? reason, long makerUserId, CancellationToken ct = default)
    {
        var orig = await db.QuerySingleAsync(
            @"SELECT id, customer_id, invoice_number, status, currency, subtotal, tax_total, total, credit_total
              FROM issued_invoices
              WHERE company_id=@c AND id=@id AND document_type='invoice'",
            c => { c.Parameters.AddWithValue("@c", companyId); c.Parameters.AddWithValue("@id", invoiceId); }, ct);
        if (orig is null) return new CreditNoteOutcome(false, "missing", "invoice_not_found");
        var status = orig["status"]?.ToString() ?? "";
        if (status is not ("issued" or "paid")) return new CreditNoteOutcome(false, status, "invoice_not_creditable");

        var total = Dec(orig["total"]);
        var subtotal = Dec(orig["subtotal"]);
        var creditedSoFar = Dec(orig["creditTotal"]);
        var remaining = total - creditedSoFar;
        var creditTotal = amount ?? remaining;
        if (creditTotal <= 0 || creditTotal > remaining)
            return new CreditNoteOutcome(false, status, "credit_exceeds_remaining_creditable");

        // Proportional split; header foots by construction (creditTax = creditTotal - creditSubtotal).
        var creditSubtotal = total == 0m ? 0m : Math.Round(creditTotal * subtotal / total, 2);
        var creditTax = creditTotal - creditSubtotal;

        var draftNo = $"CN-DRAFT-{companyId}-{invoiceId:N}"[..Math.Min(60, $"CN-DRAFT-{companyId}-{invoiceId:N}".Length)] + "-" + DateTime.UtcNow.ToString("yyyyMMddHHmmssfff", CultureInfo.InvariantCulture);
        var draftRow = (await db.QuerySingleAsync(
            @"INSERT INTO invoice_drafts
                (company_id, customer_id, invoice_draft_no, status, currency, subtotal, tax_total, total,
                 source, document_type, adjusts_invoice_id, job_id, metadata_json)
              VALUES (@c, @cust, @dno, 'pending_review', @cur, @sub, @tax, @tot,
                      'credit_note', 'credit_note', @orig, NULL, @meta::jsonb)
              RETURNING id",
            c =>
            {
                c.Parameters.AddWithValue("@c", companyId);
                c.Parameters.AddWithValue("@cust", Convert.ToInt64(orig["customerId"]));
                c.Parameters.AddWithValue("@dno", draftNo);
                c.Parameters.AddWithValue("@cur", orig["currency"]?.ToString() ?? "USD");
                c.Parameters.AddWithValue("@sub", -creditSubtotal);
                c.Parameters.AddWithValue("@tax", -creditTax);
                c.Parameters.AddWithValue("@tot", -creditTotal);
                c.Parameters.AddWithValue("@orig", invoiceId);
                c.Parameters.AddWithValue("@meta", JsonSerializer.Serialize(new { reason, makerUserId }));
            }, ct))!;
        var draftId = (Guid)draftRow["id"]!;

        var request = approval.CreateRequest(
            companyId.ToString(CultureInfo.InvariantCulture),
            ActorTypes.TenantUser,
            makerUserId.ToString(CultureInfo.InvariantCulture),
            "finance.credit_note.approve",
            "invoice_draft",
            draftId.ToString(),
            JsonSerializer.Serialize(new { creditNoteDraftId = draftId, adjustsInvoiceId = invoiceId, creditTotal, reason, makerUserId }),
            "high");
        await db.ExecuteAsync(
            "UPDATE invoice_drafts SET approval_request_id=@a, updated_at=NOW() WHERE company_id=@c AND id=@id",
            c => { c.Parameters.AddWithValue("@a", request.Id); c.Parameters.AddWithValue("@c", companyId); c.Parameters.AddWithValue("@id", draftId); }, ct);

        _ = events.Publish(
            companyId.ToString(CultureInfo.InvariantCulture), "credit_note.requested", "invoice_draft", draftId.ToString(),
            JsonSerializer.Serialize(new { creditNoteDraftId = draftId, adjustsInvoiceId = invoiceId, creditTotal }),
            idempotencyKey: $"credit-note-requested-{draftId}");

        return new CreditNoteOutcome(true, "pending_review",
            CreditNoteDraftId: draftId, ApprovalRequestId: request.Id, CreditTotal: creditTotal);
    }

    public async Task<CreditNoteOutcome> ApproveCreditNoteAsync(
        long companyId, Guid creditNoteDraftId, long checkerUserId, CancellationToken ct = default)
    {
        var draft = await db.QuerySingleAsync(
            @"SELECT id, customer_id, invoice_draft_no, status, currency, subtotal, tax_total, total,
                     adjusts_invoice_id, approval_request_id, metadata_json
              FROM invoice_drafts
              WHERE company_id=@c AND id=@id AND document_type='credit_note'",
            c => { c.Parameters.AddWithValue("@c", companyId); c.Parameters.AddWithValue("@id", creditNoteDraftId); }, ct);
        if (draft is null) return new CreditNoteOutcome(false, "missing", "credit_note_draft_not_found");

        // Replay guard: already issued for this draft -> idempotent success.
        var existing = await db.QuerySingleAsync(
            @"SELECT id, total, metadata_json FROM issued_invoices WHERE company_id=@c AND source_invoice_draft_id=@d",
            c => { c.Parameters.AddWithValue("@c", companyId); c.Parameters.AddWithValue("@d", creditNoteDraftId); }, ct);
        if (existing is not null)
            return new CreditNoteOutcome(true, "issued", CreditNoteId: (Guid)existing["id"]!,
                CreditTotal: Math.Abs(Dec(existing["total"])), Replay: true);

        if (draft["status"]?.ToString() != "pending_review")
            return new CreditNoteOutcome(false, draft["status"]?.ToString() ?? "unknown", "not_pending_review");

        // Maker-checker: the approver must not be the requester.
        if (draft["approvalRequestId"] is { } arObj and not DBNull)
        {
            var maker = (await db.QuerySingleAsync(
                "SELECT requested_by_actor_id FROM approval_requests WHERE id=@id",
                c => c.Parameters.AddWithValue("@id", Convert.ToInt64(arObj)), ct))?["requestedByActorId"]?.ToString();
            if (maker == checkerUserId.ToString(CultureInfo.InvariantCulture))
                return new CreditNoteOutcome(false, "pending_review", "maker_checker_same_user");
            approval.Decide(Convert.ToInt64(arObj), checkerUserId.ToString(CultureInfo.InvariantCulture), "approved");
        }

        var origId = (Guid)draft["adjustsInvoiceId"]!;
        var creditTotal = Math.Abs(Dec(draft["total"]));
        var creditSubtotal = Math.Abs(Dec(draft["subtotal"]));
        var creditTax = Math.Abs(Dec(draft["taxTotal"]));

        var (cnId, relieved, refundDue) = await db.WithTransactionAsync(async (conn, tx) =>
        {
            // Serialize concurrent credits against the same invoice + enforce the cumulative cap.
            decimal origTotal, origPaid, origBalance, origCredited, origTaxTotal;
            string origNumber;
            long? origTaxProfile;
            long custId;
            await using (var sel = new NpgsqlCommand(
                @"SELECT total, amount_paid, balance_due, credit_total, tax_total, invoice_number, tax_profile_id, customer_id
                  FROM issued_invoices WHERE company_id=@c AND id=@o FOR UPDATE", conn, tx))
            {
                sel.Parameters.AddWithValue("@c", companyId);
                sel.Parameters.AddWithValue("@o", origId);
                await using var rdr = await sel.ExecuteReaderAsync(ct);
                if (!await rdr.ReadAsync(ct)) throw new InvalidOperationException("original invoice disappeared");
                origTotal = rdr.GetDecimal(0); origPaid = rdr.GetDecimal(1); origBalance = rdr.GetDecimal(2);
                origCredited = rdr.GetDecimal(3); origTaxTotal = rdr.GetDecimal(4); origNumber = rdr.GetString(5);
                origTaxProfile = rdr.IsDBNull(6) ? null : rdr.GetInt64(6);
                custId = rdr.GetInt64(7);
            }
            if (origCredited + creditTotal > origTotal)
                throw new InvalidOperationException("credit_exceeds_remaining_creditable");

            var relievedAmt = Math.Min(creditTotal, origBalance);
            var refund = creditTotal - relievedAmt;

            Guid newCnId;
            await using (var ins = new NpgsqlCommand(
                @"INSERT INTO issued_invoices
                    (company_id, customer_id, source_invoice_draft_id, source_invoice_draft_no, invoice_number,
                     status, currency, subtotal, tax_total, total, amount_paid, balance_due, payment_status,
                     issued_at, document_type, adjusts_invoice_id, tax_profile_id, metadata_json)
                  VALUES (@c, @cust, @draft, @dno, @no,
                          'issued', @cur, @sub, @tax, @tot, 0, 0, 'applied',
                          NOW(), 'credit_note', @orig, @tp, @meta::jsonb)
                  RETURNING id", conn, tx))
            {
                ins.Parameters.AddWithValue("@c", companyId);
                ins.Parameters.AddWithValue("@cust", custId);
                ins.Parameters.AddWithValue("@draft", creditNoteDraftId);
                ins.Parameters.AddWithValue("@dno", draft["invoiceDraftNo"]?.ToString() ?? "");
                ins.Parameters.AddWithValue("@no", $"CN-{companyId}-{creditNoteDraftId:N}");
                ins.Parameters.AddWithValue("@cur", draft["currency"]?.ToString() ?? "USD");
                ins.Parameters.AddWithValue("@sub", -creditSubtotal);
                ins.Parameters.AddWithValue("@tax", -creditTax);
                ins.Parameters.AddWithValue("@tot", -creditTotal);
                ins.Parameters.AddWithValue("@orig", origId);
                ins.Parameters.AddWithValue("@tp", (object?)origTaxProfile ?? DBNull.Value);
                ins.Parameters.AddWithValue("@meta", JsonSerializer.Serialize(new
                {
                    adjustsInvoiceNumber = origNumber,
                    relieved = relievedAmt,
                    refundDue = refund,
                }));
                newCnId = (Guid)(await ins.ExecuteScalarAsync(ct))!;
            }

            // One CN line describing the correction (job_charge_id stays NULL by design — a credit never
            // silently re-opens billed-charge dedup).
            await using (var line = new NpgsqlCommand(
                @"INSERT INTO issued_invoice_lines (company_id, issued_invoice_id, line_no, description, quantity, unit_rate, amount)
                  VALUES (@c, @cn, 1, @desc, 1, @amt, @amt)", conn, tx))
            {
                line.Parameters.AddWithValue("@c", companyId);
                line.Parameters.AddWithValue("@cn", newCnId);
                line.Parameters.AddWithValue("@desc", $"Credit against {origNumber}");
                line.Parameters.AddWithValue("@amt", -creditSubtotal);
                await line.ExecuteNonQueryAsync(ct);
            }

            // Tax reversal: negate the ORIGINAL'S snapshot proportionally (never recompute at today's
            // rates). Per-line rounding residual is pushed into the last line so the snapshot foots.
            if (creditTax > 0 && origTaxTotal > 0)
            {
                var ratio = creditTotal / origTotal;
                await using var cmd = new NpgsqlCommand(
                    @"INSERT INTO issued_invoice_tax_lines
                        (company_id, issued_invoice_id, regime, tax_code, tax_category, jurisdiction,
                         taxable_amount, rate, tax_amount, tax_profile_id, tax_point_date)
                      SELECT company_id, @cn, regime, tax_code, tax_category, jurisdiction,
                             ROUND(-taxable_amount * @ratio, 2), rate, ROUND(-tax_amount * @ratio, 2), tax_profile_id, CURRENT_DATE
                      FROM issued_invoice_tax_lines
                      WHERE company_id=@c AND issued_invoice_id=@orig", conn, tx);
                cmd.Parameters.AddWithValue("@c", companyId);
                cmd.Parameters.AddWithValue("@cn", newCnId);
                cmd.Parameters.AddWithValue("@orig", origId);
                cmd.Parameters.AddWithValue("@ratio", ratio);
                await cmd.ExecuteNonQueryAsync(ct);

                // Foot the snapshot to exactly -creditTax.
                await using var foot = new NpgsqlCommand(
                    @"UPDATE issued_invoice_tax_lines SET tax_amount = tax_amount + (
                          SELECT -@target - COALESCE(SUM(tax_amount),0)
                          FROM issued_invoice_tax_lines WHERE company_id=@c AND issued_invoice_id=@cn)
                      WHERE id = (SELECT id FROM issued_invoice_tax_lines
                                  WHERE company_id=@c AND issued_invoice_id=@cn ORDER BY id DESC LIMIT 1)", conn, tx);
                foot.Parameters.AddWithValue("@c", companyId);
                foot.Parameters.AddWithValue("@cn", newCnId);
                foot.Parameters.AddWithValue("@target", creditTax);
                await foot.ExecuteNonQueryAsync(ct);
            }

            // Balance effect on the original (payment_status transitions per the CPA decision).
            await using (var upd = new NpgsqlCommand(
                @"UPDATE issued_invoices SET
                      credit_total = credit_total + @ct,
                      balance_due = GREATEST(0, total - amount_paid - (credit_total + @ct)),
                      payment_status = CASE
                          WHEN total - amount_paid - (credit_total + @ct) <= 0 AND amount_paid = 0 THEN 'credited'
                          WHEN total - amount_paid - (credit_total + @ct) <= 0 AND amount_paid < total THEN 'settled'
                          ELSE payment_status END,
                      updated_at = NOW()
                  WHERE company_id=@c AND id=@o", conn, tx))
            {
                upd.Parameters.AddWithValue("@c", companyId);
                upd.Parameters.AddWithValue("@o", origId);
                upd.Parameters.AddWithValue("@ct", creditTotal);
                await upd.ExecuteNonQueryAsync(ct);
            }

            await using (var md = new NpgsqlCommand(
                "UPDATE invoice_drafts SET status='issued', updated_at=NOW() WHERE company_id=@c AND id=@d", conn, tx))
            {
                md.Parameters.AddWithValue("@c", companyId);
                md.Parameters.AddWithValue("@d", creditNoteDraftId);
                await md.ExecuteNonQueryAsync(ct);
            }

            return (newCnId, relievedAmt, refund);
        }, ct);

        // Durable event -> GL reversal handler (post-commit, idempotent, retryable).
        _ = events.Publish(
            companyId.ToString(CultureInfo.InvariantCulture), "credit_note.issued", "issued_invoice", cnId.ToString(),
            JsonSerializer.Serialize(new { creditNoteId = cnId, adjustsInvoiceId = origId, creditTotal, relieved, refundDue }),
            idempotencyKey: $"credit-note-issued-{cnId}");

        return new CreditNoteOutcome(true, "issued", CreditNoteId: cnId,
            CreditTotal: creditTotal, Relieved: relieved, RefundDue: refundDue);
    }

    public async Task<List<Dictionary<string, object?>>> ListCreditNotesAsync(long companyId, CancellationToken ct = default) =>
        await db.QueryAsync(
            @"SELECT cn.id, cn.invoice_number, cn.total, cn.tax_total, cn.currency, cn.issued_at, cn.adjusts_invoice_id,
                     orig.invoice_number AS adjusts_invoice_number, cn.metadata_json
              FROM issued_invoices cn
              LEFT JOIN issued_invoices orig ON orig.id = cn.adjusts_invoice_id AND orig.company_id = cn.company_id
              WHERE cn.company_id=@c AND cn.document_type='credit_note'
              ORDER BY cn.issued_at DESC",
            c => c.Parameters.AddWithValue("@c", companyId), ct);

    private static decimal Dec(object? v) => v is null or DBNull ? 0m : Convert.ToDecimal(v);
}

// GL derive-beside handler: an issued credit note posts its reversal (Dr Revenue / Dr Tax; Cr AR for the
// relieved portion, Cr Customer Refunds Payable for the excess over the open balance).
public sealed class CreditNoteIssuedGeneralLedgerHandler(GeneralLedgerService gl) : IOutboxMessageHandler
{
    public string EventType => "credit_note.issued";

    public async Task HandleAsync(OutboxMessageRecord message, CancellationToken ct = default)
    {
        if (!long.TryParse(message.TenantId, out var companyId)) return;
        if (!Guid.TryParse(message.AggregateId, out _))
            throw new InvalidOperationException($"credit_note.issued aggregateId is not a UUID: '{message.AggregateId}'");
        await gl.PostCreditNoteAsync(companyId, message.AggregateId, ct);
    }
}
