using System.Globalization;
using System.Text.Json;
using Npgsql;
using Opstrax.Api.Data;
using Opstrax.Api.Foundation;

namespace Opstrax.Api.Services;

public enum SettlementMode { Preview, Commit }

// One computed pay line for a delivered load. basis_amount is the input the pay was computed
// from (miles for per_mile, 1 for flat) — kept for audit so a statement is reproducible.
public sealed record SettlementComputedLine(
    long JobId, string PayCode, string Description, string Basis,
    decimal BasisAmount, decimal Quantity, decimal UnitRate, decimal Amount);

public sealed record SettlementStatementOutcome(
    bool Generated, long? StatementId, string? StatementNo, string Status,
    decimal Subtotal, decimal Total, string Currency,
    IReadOnlyList<SettlementComputedLine> Lines, string? Reason = null);

// Settlement / carrier-&-driver-pay (AP) — ADR-007 §C. Computes what a payee is owed for the
// loads they delivered in a period, from the load + pay agreement — NEVER from job_charges
// (settlement is decoupled from the customer invoice). Phase 1: driver flat/per-mile only.
//
// Fail-closed like RatingService: no pay agreement, an unsupported basis, or no delivered loads
// yields Generated=false with zero writes — never invent a pay rate. Commit is idempotent
// (delete-and-recompute of the system statement) and refuses to touch an approved/paid statement.
// The publisher is optional so existing `new SettlementService(db)` construction (tests) keeps working;
// when present, approve/payment emit durable settlement.approved / settlement.paid outbox events that
// drive the derive-beside GL posting handlers.
public sealed class SettlementService(Database db, IDomainEventPublisher? events = null)
{
    public async Task<SettlementStatementOutcome> GenerateDriverStatementAsync(
        long companyId, long driverId, DateOnly periodStart, DateOnly periodEnd,
        SettlementMode mode, CancellationToken ct = default)
    {
        var empty = Array.Empty<SettlementComputedLine>();

        if (periodEnd < periodStart)
            return new SettlementStatementOutcome(false, null, null, "invalid", 0, 0, "USD", empty, "invalid_period");

        var agreement = await ResolveDriverAgreementAsync(companyId, driverId, periodStart, periodEnd, ct);
        if (agreement is null)
            return new SettlementStatementOutcome(false, null, null, "unpriced", 0, 0, "USD", empty, "no_pay_agreement");

        // Phase 1 supports flat + per_mile. percent (share of revenue) is modelled but not computed
        // yet — fail closed rather than guess a base.
        if (agreement.Basis is not ("flat" or "per_mile"))
            return new SettlementStatementOutcome(false, null, null, "unpriced", 0, 0, agreement.Currency, empty,
                $"basis_unsupported_phase1:{agreement.Basis}");

        var loads = await LoadDeliveredJobsAsync(companyId, driverId, periodStart, periodEnd, ct);
        // Detention pay is keyed on the TRIGGER date (billed/collected), not the delivery date, so a
        // driver can have a statement of detention pay alone (dwell collected this period, load delivered
        // earlier). Fail only when there is nothing at all to pay.
        var detentionLines = await ComputeDetentionPayLinesAsync(companyId, driverId, periodStart, periodEnd, ct);
        if (loads.Count == 0 && detentionLines.Count == 0)
            return new SettlementStatementOutcome(false, null, null, "empty", 0, 0, agreement.Currency, empty, "no_delivered_loads");

        var lines = new List<SettlementComputedLine>(loads.Count + detentionLines.Count);
        foreach (var load in loads)
        {
            decimal qty, unitRate, basisAmount, amount;
            if (agreement.Basis == "flat")
            {
                qty = 1m; unitRate = agreement.Rate; basisAmount = 1m;
                amount = Math.Round(agreement.Rate, 2, MidpointRounding.AwayFromZero);
            }
            else // per_mile
            {
                var miles = load.Miles ?? 0m;
                qty = miles; unitRate = agreement.Rate; basisAmount = miles;
                amount = Math.Round(miles * agreement.Rate, 2, MidpointRounding.AwayFromZero);
            }

            // Per-load minimum pay (guaranteed floor per delivery).
            if (agreement.MinPay is { } min && amount < min)
                amount = min;

            lines.Add(new SettlementComputedLine(
                load.JobId, "linehaul",
                agreement.Basis == "flat" ? "Flat load pay" : $"Line-haul {basisAmount} mi @ {unitRate}",
                agreement.Basis, basisAmount, qty, unitRate, amount));
        }

        // Detention pay lines derived here (not appended post-hoc) so delete-and-recompute always
        // re-produces them consistently — a regenerate can never orphan or duplicate them.
        lines.AddRange(detentionLines);

        var subtotal = lines.Sum(l => l.Amount);
        var total = subtotal;

        if (mode == SettlementMode.Preview)
            return new SettlementStatementOutcome(false, null, null, "preview", subtotal, total, agreement.Currency, lines, "preview");

        return await CommitAsync(companyId, driverId, periodStart, periodEnd, agreement, lines, subtotal, total, ct);
    }

    public async Task<Dictionary<string, object?>> GetDetentionPayPolicyAsync(long companyId, CancellationToken ct = default) =>
        await db.QuerySingleAsync(
            "SELECT enabled, trigger_state, share_type, share_value, currency FROM driver_detention_pay_policy WHERE company_id=@c",
            c => c.Parameters.AddWithValue("@c", companyId), ct)
        ?? new Dictionary<string, object?> { ["enabled"] = false, ["triggerState"] = "collected", ["shareType"] = "percent", ["shareValue"] = 0m };

    public Task SetDetentionPayPolicyAsync(long companyId, bool enabled, string trigger, string shareType, decimal shareValue, CancellationToken ct = default) =>
        db.ExecuteAsync(
            @"INSERT INTO driver_detention_pay_policy (company_id, enabled, trigger_state, share_type, share_value)
              VALUES (@c, @en, @tr, @st, @sv)
              ON CONFLICT (company_id) DO UPDATE SET
                  enabled=@en, trigger_state=@tr, share_type=@st, share_value=@sv, updated_at=NOW()",
            c =>
            {
                c.Parameters.AddWithValue("@c", companyId); c.Parameters.AddWithValue("@en", enabled);
                c.Parameters.AddWithValue("@tr", trigger); c.Parameters.AddWithValue("@st", shareType);
                c.Parameters.AddWithValue("@sv", shareValue);
            }, ct);

    // Detention -> driver pay (the differentiator). Fail-closed: no enabled policy => empty. One line per
    // dwell attributed to this driver whose detention charge reached the policy's trigger state within the
    // period. 'billed' = the charge is on an invoice issued in-period; 'collected' = that invoice is paid
    // in-period. Amount = share% of the charge, or a flat rate per billable detention hour. Because this is
    // recomputed on every generation and keyed by the dwell's charge, it is inherently idempotent.
    private async Task<List<SettlementComputedLine>> ComputeDetentionPayLinesAsync(
        long companyId, long driverId, DateOnly periodStart, DateOnly periodEnd, CancellationToken ct)
    {
        var policy = await db.QuerySingleAsync(
            "SELECT enabled, trigger_state, share_type, share_value FROM driver_detention_pay_policy WHERE company_id=@c",
            c => c.Parameters.AddWithValue("@c", companyId), ct);
        var result = new List<SettlementComputedLine>();
        if (policy is null || policy["enabled"] is not true) return result;   // fail-closed

        var trigger = policy["triggerState"]?.ToString() ?? "collected";
        var shareType = policy["shareType"]?.ToString() ?? "percent";
        var shareValue = Convert.ToDecimal(policy["shareValue"], CultureInfo.InvariantCulture);
        if (shareValue <= 0) return result;

        var rows = await db.QueryAsync(
            @"SELECT d.id AS dwell_id, d.job_id, d.quantity_hours, jc.amount AS charge_amount, g.name AS site_name
              FROM detention_dwells d
              JOIN job_charges jc ON jc.id = d.job_charge_id AND jc.company_id = d.company_id AND jc.source='detention'
              JOIN geofences g ON g.id = d.geofence_id AND g.company_id = d.company_id
              JOIN issued_invoice_lines iil ON iil.job_charge_id = jc.id AND iil.company_id = jc.company_id
              JOIN issued_invoices ii ON ii.id = iil.issued_invoice_id AND ii.company_id = iil.company_id
              WHERE d.company_id=@c AND d.driver_id=@driver AND d.job_charge_id IS NOT NULL AND d.job_id IS NOT NULL
                AND (
                    (@trigger='billed'    AND ii.issued_at::date BETWEEN @ps AND @pe)
                 OR (@trigger='collected' AND ii.payment_status='paid' AND ii.paid_at IS NOT NULL AND ii.paid_at::date BETWEEN @ps AND @pe)
                )",
            c =>
            {
                c.Parameters.AddWithValue("@c", companyId); c.Parameters.AddWithValue("@driver", driverId);
                c.Parameters.AddWithValue("@trigger", trigger);
                c.Parameters.AddWithValue("@ps", periodStart); c.Parameters.AddWithValue("@pe", periodEnd);
            }, ct);

        foreach (var r in rows)
        {
            var jobId = Convert.ToInt64(r["jobId"], CultureInfo.InvariantCulture);
            var hours = r["quantityHours"] is null or DBNull ? 0m : Convert.ToDecimal(r["quantityHours"], CultureInfo.InvariantCulture);
            var chargeAmount = r["chargeAmount"] is null or DBNull ? 0m : Convert.ToDecimal(r["chargeAmount"], CultureInfo.InvariantCulture);
            var site = r["siteName"]?.ToString() ?? "site";

            decimal amount; decimal basisAmount; decimal unitRate;
            if (shareType == "flat_per_hour") { basisAmount = hours; unitRate = shareValue; amount = Math.Round(hours * shareValue, 2, MidpointRounding.AwayFromZero); }
            else { basisAmount = chargeAmount; unitRate = shareValue; amount = Math.Round(chargeAmount * shareValue / 100m, 2, MidpointRounding.AwayFromZero); }   // percent
            if (amount <= 0) continue;

            result.Add(new SettlementComputedLine(
                jobId, "detention",
                $"Detention pay — {site} ({trigger})",
                shareType, basisAmount, basisAmount, unitRate, amount));
        }
        return result;
    }

    private async Task<SettlementStatementOutcome> CommitAsync(
        long companyId, long driverId, DateOnly periodStart, DateOnly periodEnd,
        PayAgreement agreement, List<SettlementComputedLine> lines, decimal subtotal, decimal total,
        CancellationToken ct)
    {
        // A statement that is already approved/paid is immutable — never regenerate it.
        var existing = await db.QuerySingleAsync(
            @"SELECT id, status FROM settlement_statements
              WHERE company_id=@cid AND payee_type='driver' AND payee_id=@pid
                AND period_start=@ps AND period_end=@pe AND source='system'",
            c => { c.Parameters.AddWithValue("@cid", companyId); c.Parameters.AddWithValue("@pid", driverId);
                   c.Parameters.AddWithValue("@ps", periodStart); c.Parameters.AddWithValue("@pe", periodEnd); }, ct);
        if (existing is not null)
        {
            var st = existing.GetValueOrDefault("status")?.ToString() ?? "draft";
            if (st is "approved" or "paid")
                return new SettlementStatementOutcome(false, Convert.ToInt64(existing["id"], CultureInfo.InvariantCulture),
                    null, st, subtotal, total, agreement.Currency, lines, "statement_locked");
        }

        var statementNo = $"ST-{driverId}-{periodStart:yyyyMMdd}-{periodEnd:yyyyMMdd}";

        var statementId = await db.WithTransactionAsync(async (conn, tx) =>
        {
            // Delete-and-recompute: drop the prior draft statement (cascade clears its lines) so a
            // re-run produces exactly one statement + one line per load.
            await using (var del = new NpgsqlCommand(
                @"DELETE FROM settlement_statements
                  WHERE company_id=@cid AND payee_type='driver' AND payee_id=@pid
                    AND period_start=@ps AND period_end=@pe AND source='system'
                    AND status NOT IN ('approved','paid')", conn, tx))
            {
                del.Parameters.AddWithValue("@cid", companyId); del.Parameters.AddWithValue("@pid", driverId);
                del.Parameters.AddWithValue("@ps", periodStart); del.Parameters.AddWithValue("@pe", periodEnd);
                await del.ExecuteNonQueryAsync(ct);
            }

            long id;
            await using (var ins = new NpgsqlCommand(
                @"INSERT INTO settlement_statements
                    (company_id, statement_no, payee_type, payee_id, period_start, period_end,
                     status, currency, subtotal, total, source, pay_agreement_id, created_at)
                  VALUES (@cid, @no, 'driver', @pid, @ps, @pe,
                          'draft', @cur, @sub, @tot, 'system', @agid, NOW())
                  RETURNING id", conn, tx))
            {
                ins.Parameters.AddWithValue("@cid", companyId); ins.Parameters.AddWithValue("@no", statementNo);
                ins.Parameters.AddWithValue("@pid", driverId);
                ins.Parameters.AddWithValue("@ps", periodStart); ins.Parameters.AddWithValue("@pe", periodEnd);
                ins.Parameters.AddWithValue("@cur", agreement.Currency);
                ins.Parameters.AddWithValue("@sub", subtotal); ins.Parameters.AddWithValue("@tot", total);
                ins.Parameters.AddWithValue("@agid", agreement.Id);
                id = Convert.ToInt64((await ins.ExecuteScalarAsync(ct))!, CultureInfo.InvariantCulture);
            }

            var lineNo = 1;
            foreach (var l in lines)
            {
                await using var li = new NpgsqlCommand(
                    @"INSERT INTO settlement_lines
                        (company_id, statement_id, job_id, line_no, pay_code, description,
                         basis, basis_amount, quantity, unit_rate, amount, pay_agreement_id, source, created_at)
                      VALUES (@cid, @sid, @jid, @ln, @code, @desc,
                              @basis, @bamt, @qty, @rate, @amt, @agid, 'settlement', NOW())", conn, tx);
                li.Parameters.AddWithValue("@cid", companyId); li.Parameters.AddWithValue("@sid", id);
                li.Parameters.AddWithValue("@jid", l.JobId); li.Parameters.AddWithValue("@ln", lineNo++);
                li.Parameters.AddWithValue("@code", l.PayCode); li.Parameters.AddWithValue("@desc", l.Description);
                li.Parameters.AddWithValue("@basis", l.Basis); li.Parameters.AddWithValue("@bamt", l.BasisAmount);
                li.Parameters.AddWithValue("@qty", l.Quantity); li.Parameters.AddWithValue("@rate", l.UnitRate);
                li.Parameters.AddWithValue("@amt", l.Amount); li.Parameters.AddWithValue("@agid", agreement.Id);
                await li.ExecuteNonQueryAsync(ct);
            }
            return id;
        }, ct);

        return new SettlementStatementOutcome(true, statementId, statementNo, "draft", subtotal, total, agreement.Currency, lines, null);
    }

    // Most-specific active + effective agreement for the driver: a driver-specific agreement wins
    // over the tenant default (payee_id IS NULL), newest effective_date breaks ties.
    private async Task<PayAgreement?> ResolveDriverAgreementAsync(
        long companyId, long driverId, DateOnly periodStart, DateOnly periodEnd, CancellationToken ct)
    {
        var row = await db.QuerySingleAsync(
            @"SELECT id, basis, rate, min_pay, currency
              FROM pay_agreements
              WHERE company_id=@cid AND payee_type='driver' AND status='active'
                AND effective_date <= @pe AND (expiry_date IS NULL OR expiry_date >= @ps)
                AND (payee_id = @pid OR payee_id IS NULL)
              ORDER BY (payee_id IS NOT NULL) DESC, effective_date DESC
              LIMIT 1",
            c => { c.Parameters.AddWithValue("@cid", companyId); c.Parameters.AddWithValue("@pid", driverId);
                   c.Parameters.AddWithValue("@ps", periodStart); c.Parameters.AddWithValue("@pe", periodEnd); }, ct);
        if (row is null) return null;
        return new PayAgreement(
            Convert.ToInt64(row["id"], CultureInfo.InvariantCulture),
            (row.GetValueOrDefault("basis")?.ToString() ?? "per_mile").Trim().ToLowerInvariant(),
            Convert.ToDecimal(row.GetValueOrDefault("rate") ?? 0m, CultureInfo.InvariantCulture),
            row.GetValueOrDefault("minPay") is { } mp and not DBNull ? Convert.ToDecimal(mp, CultureInfo.InvariantCulture) : null,
            row.GetValueOrDefault("currency")?.ToString() ?? "USD");
    }

    // Distinct delivered loads for the driver in the period, each with its trip miles (actual, then
    // planned). Keyed by job so a job with multiple trips/assignments is paid once.
    private async Task<List<DeliveredLoad>> LoadDeliveredJobsAsync(
        long companyId, long driverId, DateOnly periodStart, DateOnly periodEnd, CancellationToken ct)
    {
        var rows = await db.QueryAsync(
            @"SELECT da.job_id AS job_id,
                     (SELECT COALESCE(MAX(t.actual_distance_miles), MAX(t.planned_distance_miles))
                        FROM trips t WHERE t.company_id=da.company_id AND t.job_id=da.job_id) AS miles
              FROM dispatch_assignments da
              WHERE da.company_id=@cid AND da.driver_id=@pid
                AND da.assignment_status='delivered' AND da.job_id IS NOT NULL
                AND COALESCE(da.actual_delivery_at, da.completed_at)::date BETWEEN @ps AND @pe
              GROUP BY da.job_id, da.company_id
              ORDER BY da.job_id",
            c => { c.Parameters.AddWithValue("@cid", companyId); c.Parameters.AddWithValue("@pid", driverId);
                   c.Parameters.AddWithValue("@ps", periodStart); c.Parameters.AddWithValue("@pe", periodEnd); }, ct);

        var loads = new List<DeliveredLoad>(rows.Count);
        foreach (var r in rows)
        {
            var jobId = Convert.ToInt64(r["jobId"], CultureInfo.InvariantCulture);
            decimal? miles = r.GetValueOrDefault("miles") is { } m and not DBNull
                ? Convert.ToDecimal(m, CultureInfo.InvariantCulture) : null;
            loads.Add(new DeliveredLoad(jobId, miles));
        }
        return loads;
    }

    public async Task<Dictionary<string, object?>?> GetStatementAsync(long companyId, long statementId, CancellationToken ct = default)
        => await db.QuerySingleAsync(
            "SELECT * FROM settlement_statements WHERE company_id=@cid AND id=@id",
            c => { c.Parameters.AddWithValue("@cid", companyId); c.Parameters.AddWithValue("@id", statementId); }, ct);

    public async Task<List<Dictionary<string, object?>>> ListStatementsAsync(
        long companyId, string? payeeType, long? payeeId, string? status, CancellationToken ct = default)
        => await db.QueryAsync(
            @"SELECT * FROM settlement_statements
              WHERE company_id=@cid
                AND (@ptype IS NULL OR payee_type=@ptype)
                AND (@pid IS NULL OR payee_id=@pid)
                AND (@status IS NULL OR status=@status)
              ORDER BY created_at DESC, id DESC
              LIMIT 500",
            c => { c.Parameters.AddWithValue("@cid", companyId);
                   c.Parameters.AddWithValue("@ptype", (object?)payeeType ?? DBNull.Value);
                   c.Parameters.AddWithValue("@pid", (object?)payeeId ?? DBNull.Value);
                   c.Parameters.AddWithValue("@status", (object?)status ?? DBNull.Value); }, ct);

    public async Task<List<Dictionary<string, object?>>> GetLinesAsync(long companyId, long statementId, CancellationToken ct = default)
        => await db.QueryAsync(
            "SELECT * FROM settlement_lines WHERE company_id=@cid AND statement_id=@sid ORDER BY line_no",
            c => { c.Parameters.AddWithValue("@cid", companyId); c.Parameters.AddWithValue("@sid", statementId); }, ct);

    public sealed record SettlementActionOutcome(bool Ok, string Status, string? Reason = null);

    // Approve a draft statement (guarded by settlement.approve, a high-risk action). Idempotent-safe:
    // approving an already-approved statement is a no-op success; a paid statement can't be re-approved.
    public async Task<SettlementActionOutcome> ApproveStatementAsync(long companyId, long statementId, long userId, CancellationToken ct = default)
    {
        var row = await db.QuerySingleAsync(
            "SELECT status, total, currency FROM settlement_statements WHERE company_id=@cid AND id=@id",
            c => { c.Parameters.AddWithValue("@cid", companyId); c.Parameters.AddWithValue("@id", statementId); }, ct);
        if (row is null) return new SettlementActionOutcome(false, "missing", "not_found");
        var status = row.GetValueOrDefault("status")?.ToString() ?? "draft";
        if (status == "approved") return new SettlementActionOutcome(true, "approved");
        if (status is not ("draft" or "pending_review"))
            return new SettlementActionOutcome(false, status, "not_approvable");

        await db.ExecuteAsync(
            @"UPDATE settlement_statements
              SET status='approved', approved_by_user_id=@uid, approved_at=NOW(), updated_at=NOW()
              WHERE company_id=@cid AND id=@id AND status IN ('draft','pending_review')",
            c => { c.Parameters.AddWithValue("@cid", companyId); c.Parameters.AddWithValue("@id", statementId);
                   c.Parameters.AddWithValue("@uid", userId); }, ct);

        // Durable event on the REAL draft/pending_review -> approved transition only (the re-approve
        // no-op above returns before reaching here). Drives the derive-beside GL accrual handler.
        _ = events?.Publish(
            companyId.ToString(CultureInfo.InvariantCulture),
            "settlement.approved",
            "settlement_statement",
            statementId.ToString(CultureInfo.InvariantCulture),
            JsonSerializer.Serialize(new { statementId, total = row.GetValueOrDefault("total"), currency = row.GetValueOrDefault("currency") }),
            idempotencyKey: $"settlement-approved-{statementId}");

        return new SettlementActionOutcome(true, "approved");
    }

    public sealed record SettlementPaymentOutcome(bool Ok, long? PaymentId, string Status, decimal AmountPaid, string? Reason = null);

    // Record a payment against an approved statement. Pay-before-approve is blocked. Idempotent on
    // idempotency_key (a retried payment returns the original, never double-pays). Marks the
    // statement paid once cumulative payments cover the total.
    public async Task<SettlementPaymentOutcome> RecordPaymentAsync(
        long companyId, long statementId, decimal amount, string? method, string? reference,
        string? idempotencyKey, long userId, CancellationToken ct = default)
    {
        if (amount <= 0) return new SettlementPaymentOutcome(false, null, "invalid", 0, "invalid_amount");

        var stmt = await db.QuerySingleAsync(
            "SELECT status, total, amount_paid, currency FROM settlement_statements WHERE company_id=@cid AND id=@id",
            c => { c.Parameters.AddWithValue("@cid", companyId); c.Parameters.AddWithValue("@id", statementId); }, ct);
        if (stmt is null) return new SettlementPaymentOutcome(false, null, "missing", 0, "not_found");
        var status = stmt.GetValueOrDefault("status")?.ToString() ?? "draft";
        if (status is not ("approved" or "paid"))
            return new SettlementPaymentOutcome(false, null, status, 0, "not_approved");

        if (idempotencyKey is not null)
        {
            var dup = await db.QuerySingleAsync(
                "SELECT id FROM settlement_payments WHERE company_id=@cid AND idempotency_key=@k",
                c => { c.Parameters.AddWithValue("@cid", companyId); c.Parameters.AddWithValue("@k", idempotencyKey); }, ct);
            if (dup is not null)
            {
                var paidNow = await db.ScalarDecimalAsync(
                    "SELECT amount_paid FROM settlement_statements WHERE company_id=@cid AND id=@id",
                    c => { c.Parameters.AddWithValue("@cid", companyId); c.Parameters.AddWithValue("@id", statementId); }, ct) ?? 0m;
                return new SettlementPaymentOutcome(true, Convert.ToInt64(dup["id"], CultureInfo.InvariantCulture), "approved", paidNow, "duplicate");
            }
        }

        var currency = stmt.GetValueOrDefault("currency")?.ToString() ?? "USD";
        var (paymentId, amountPaid, newStatus) = await db.WithTransactionAsync(async (conn, tx) =>
        {
            long pid;
            await using (var ins = new NpgsqlCommand(
                @"INSERT INTO settlement_payments
                    (company_id, statement_id, amount, currency, method, reference, idempotency_key, created_by_user_id, paid_at, created_at)
                  VALUES (@cid, @sid, @amt, @cur, @method, @ref, @idem, @uid, NOW(), NOW())
                  RETURNING id", conn, tx))
            {
                ins.Parameters.AddWithValue("@cid", companyId); ins.Parameters.AddWithValue("@sid", statementId);
                ins.Parameters.AddWithValue("@amt", amount); ins.Parameters.AddWithValue("@cur", currency);
                ins.Parameters.AddWithValue("@method", (object?)method ?? DBNull.Value);
                ins.Parameters.AddWithValue("@ref", (object?)reference ?? DBNull.Value);
                ins.Parameters.AddWithValue("@idem", (object?)idempotencyKey ?? DBNull.Value);
                ins.Parameters.AddWithValue("@uid", userId);
                pid = Convert.ToInt64((await ins.ExecuteScalarAsync(ct))!, CultureInfo.InvariantCulture);
            }

            // Recompute amount_paid from the ledger (source of truth) and mark paid when it covers total.
            decimal paid; string status2;
            await using (var upd = new NpgsqlCommand(
                @"UPDATE settlement_statements s
                  SET amount_paid = COALESCE((SELECT SUM(amount) FROM settlement_payments p
                                              WHERE p.company_id=s.company_id AND p.statement_id=s.id), 0),
                      status = CASE WHEN COALESCE((SELECT SUM(amount) FROM settlement_payments p
                                                   WHERE p.company_id=s.company_id AND p.statement_id=s.id), 0) >= s.total
                                    THEN 'paid' ELSE s.status END,
                      updated_at = NOW()
                  WHERE s.company_id=@cid AND s.id=@id
                  RETURNING amount_paid, status", conn, tx))
            {
                upd.Parameters.AddWithValue("@cid", companyId); upd.Parameters.AddWithValue("@id", statementId);
                await using var rdr = await upd.ExecuteReaderAsync(ct);
                await rdr.ReadAsync(ct);
                paid = rdr.GetDecimal(0); status2 = rdr.GetString(1);
            }
            return (pid, paid, status2);
        }, ct);

        // Durable event for the NEW payment only (the duplicate-idempotency-key path returned earlier,
        // so a retried payment never re-publishes). Drives the derive-beside GL cash-out handler.
        _ = events?.Publish(
            companyId.ToString(CultureInfo.InvariantCulture),
            "settlement.paid",
            "settlement_payment",
            paymentId.ToString(CultureInfo.InvariantCulture),
            JsonSerializer.Serialize(new { statementId, paymentId, amount, newStatus }),
            idempotencyKey: $"settlement-paid-{paymentId}");

        return new SettlementPaymentOutcome(true, paymentId, newStatus, amountPaid);
    }

    // AP summary: what's owed and paid across the payee book, mirroring the AR summary.
    public async Task<Dictionary<string, object?>> GetApSummaryAsync(long companyId, CancellationToken ct = default)
    {
        var row = await db.QuerySingleAsync(
            @"SELECT
                COUNT(*) AS statement_count,
                COALESCE(SUM(total), 0) AS total_pay,
                COALESCE(SUM(amount_paid), 0) AS total_paid,
                COALESCE(SUM(total) FILTER (WHERE status <> 'paid'), 0) - COALESCE(SUM(amount_paid) FILTER (WHERE status <> 'paid'), 0) AS outstanding,
                COUNT(*) FILTER (WHERE status='draft') AS draft_count,
                COUNT(*) FILTER (WHERE status='approved') AS approved_count,
                COUNT(*) FILTER (WHERE status='paid') AS paid_count
              FROM settlement_statements WHERE company_id=@cid",
            c => c.Parameters.AddWithValue("@cid", companyId), ct);
        return row ?? new Dictionary<string, object?>();
    }

    // ── Driver self-service earnings (driver:self) ──────────────────────────────
    // The driver-facing read surface for their OWN pay. The payee_id + payee_type +
    // company_id + source='system' + status predicate lives ONLY here so no driver route
    // can accidentally widen it. "Earned/owed" money comes exclusively from COMMITTED
    // statements (source='system', status approved|paid): drafts are mutable
    // (delete-and-recompute) and manual statements are uncommunicated adjustments, so
    // neither counts. The open period is a live read-only Preview, clearly flagged
    // estimated. Employer economics — basis_amount (the customer's collected charge) and
    // unit_rate (the internal share %) — are NEVER projected to the driver.
    public async Task<Dictionary<string, object?>> GetDriverEarningsAsync(
        long companyId, long driverId, CancellationToken ct = default)
    {
        // Open (uncommitted) window: day after the last cut statement through today, else the
        // current week. Derived in SQL so Postgres owns the week/date semantics.
        // Anchor on the last COMMITTED statement, not the last draft: an unapproved draft is
        // (source='system', status='draft'); anchoring on it would push open_start past today and
        // render live earnings as a misleading $0. The open window is everything since the last
        // statement AP actually cut, which is exactly the driver's not-yet-finalized earnings.
        var window = await db.QuerySingleAsync(
            @"SELECT COALESCE(
                        (SELECT (MAX(period_end) + INTERVAL '1 day')::date FROM settlement_statements
                         WHERE company_id=@cid AND payee_type='driver' AND payee_id=@did
                           AND source='system' AND status IN ('approved','paid')),
                        date_trunc('week', now())::date) AS open_start,
                     CURRENT_DATE AS open_end",
            c => { c.Parameters.AddWithValue("@cid", companyId); c.Parameters.AddWithValue("@did", driverId); }, ct);
        var openStart = DateOnly.FromDateTime(Convert.ToDateTime(window!["openStart"], CultureInfo.InvariantCulture));
        var openEnd   = DateOnly.FromDateTime(Convert.ToDateTime(window["openEnd"], CultureInfo.InvariantCulture));

        // A read-only Preview — zero writes — so the driver watches detention accrue live
        // before AP cuts the statement. If the last statement already runs through today the
        // window is empty; the preview fails closed and we present it as "caught up".
        SettlementStatementOutcome preview = openStart > openEnd
            ? new SettlementStatementOutcome(false, null, null, "preview", 0, 0, "USD", Array.Empty<SettlementComputedLine>(), "invalid_period")
            : await GenerateDriverStatementAsync(companyId, driverId, openStart, openEnd, SettlementMode.Preview, ct);

        var openDetention = preview.Lines.Where(l => l.PayCode == "detention").ToList();
        var openLoads     = preview.Lines.Where(l => l.PayCode != "detention").Select(l => l.JobId).Distinct().Count();
        var openAvailable = preview.Reason == "preview";
        var openPeriod = new Dictionary<string, object?>
        {
            ["periodStart"] = openStart.ToString("yyyy-MM-dd"),
            ["periodEnd"]   = openEnd.ToString("yyyy-MM-dd"),
            ["status"]      = "open",
            ["estimated"]   = true,
            ["available"]   = openAvailable,
            ["reason"]      = openAvailable ? null : FriendlyOpenReason(preview.Reason),
            ["grossPay"]    = openAvailable ? preview.Total : 0m,
            ["detentionPay"] = openDetention.Sum(l => l.Amount),
            ["linehaulPay"] = openAvailable ? preview.Total - openDetention.Sum(l => l.Amount) : 0m,
            ["loadCount"]   = openLoads,
            ["detentionEventCount"] = openDetention.Count,
        };

        // Committed statements (the money): source='system', approved|paid only.
        var rows = await db.QueryAsync(
            @"SELECT s.id, s.statement_no, s.period_start, s.period_end, s.status, s.currency,
                     s.subtotal, s.total, s.amount_paid,
                     (SELECT COALESCE(SUM(sl.amount),0) FROM settlement_lines sl
                        WHERE sl.statement_id=s.id AND sl.company_id=s.company_id AND sl.pay_code='detention') AS detention_total,
                     (SELECT COUNT(DISTINCT sl.job_id) FROM settlement_lines sl
                        WHERE sl.statement_id=s.id AND sl.company_id=s.company_id AND sl.pay_code<>'detention') AS load_count
              FROM settlement_statements s
              WHERE s.company_id=@cid AND s.payee_type='driver' AND s.payee_id=@did
                AND s.source='system' AND s.status IN ('approved','paid')
              ORDER BY s.period_end DESC, s.id DESC
              LIMIT 12",
            c => { c.Parameters.AddWithValue("@cid", companyId); c.Parameters.AddWithValue("@did", driverId); }, ct);

        var statements = rows.Select(r => (object)new Dictionary<string, object?>
        {
            ["id"]           = r["id"],
            ["statementNo"]  = r["statementNo"],
            ["periodStart"]  = r["periodStart"],
            ["periodEnd"]    = r["periodEnd"],
            ["status"]       = r["status"],
            ["currency"]     = r["currency"],
            ["subtotal"]     = Money(r["subtotal"]),
            ["total"]        = Money(r["total"]),
            ["amountPaid"]   = Money(r["amountPaid"]),
            ["outstanding"]  = Money(r["total"]) - Money(r["amountPaid"]),
            ["detentionTotal"] = Money(r["detentionTotal"]),
            ["loadCount"]    = r["loadCount"],
        }).ToList();

        // Money rollups over the same committed scope (statement-level totals).
        var roll = await db.QuerySingleAsync(
            @"SELECT
                COALESCE(SUM(total),0) AS lifetime_earned,
                COALESCE(SUM(amount_paid),0) AS lifetime_paid,
                COALESCE(SUM(total) FILTER (WHERE period_start >= date_trunc('year', now())::date),0) AS ytd_earned,
                COALESCE(SUM(amount_paid) FILTER (WHERE period_start >= date_trunc('year', now())::date),0) AS ytd_paid,
                COUNT(*) FILTER (WHERE period_start >= date_trunc('year', now())::date) AS ytd_count,
                COALESCE(SUM(total - amount_paid) FILTER (WHERE status='approved'),0) AS unpaid_total
              FROM settlement_statements
              WHERE company_id=@cid AND payee_type='driver' AND payee_id=@did AND source='system' AND status IN ('approved','paid')",
            c => { c.Parameters.AddWithValue("@cid", companyId); c.Parameters.AddWithValue("@did", driverId); }, ct);

        // Detention rollups need the line grain (a statement carries linehaul + detention).
        var det = await db.QuerySingleAsync(
            @"SELECT
                COALESCE(SUM(sl.amount),0) AS lifetime_detention,
                COALESCE(SUM(sl.amount) FILTER (WHERE s.period_start >= date_trunc('year', now())::date),0) AS ytd_detention,
                COUNT(*) FILTER (WHERE s.period_start >= date_trunc('year', now())::date) AS ytd_detention_events
              FROM settlement_lines sl
              JOIN settlement_statements s ON s.id = sl.statement_id AND s.company_id = sl.company_id
              WHERE sl.company_id=@cid AND s.payee_type='driver' AND s.payee_id=@did
                AND s.source='system' AND s.status IN ('approved','paid') AND sl.pay_code='detention'",
            c => { c.Parameters.AddWithValue("@cid", companyId); c.Parameters.AddWithValue("@did", driverId); }, ct);

        // Amount + date only — bank/ACH routing (method, reference) is withheld.
        var lastPay = await db.QuerySingleAsync(
            @"SELECT p.amount, p.paid_at
              FROM settlement_payments p
              JOIN settlement_statements s ON s.id = p.statement_id AND s.company_id = p.company_id
              WHERE p.company_id=@cid AND s.payee_type='driver' AND s.payee_id=@did
                AND s.source='system' AND s.status IN ('approved','paid')
              ORDER BY p.paid_at DESC LIMIT 1",
            c => { c.Parameters.AddWithValue("@cid", companyId); c.Parameters.AddWithValue("@did", driverId); }, ct);

        var policy = await db.QuerySingleAsync(
            "SELECT enabled FROM driver_detention_pay_policy WHERE company_id=@cid",
            c => c.Parameters.AddWithValue("@cid", companyId), ct);
        var policyEnabled = policy is not null && policy["enabled"] is true;

        // Single-currency-per-driver assumption (mirrors GetApSummaryAsync): a driver's pay
        // agreement fixes one currency, so rollups are not blind-summed across currencies. The
        // authoritative currency is the most recent COMMITTED statement — the preview's currency
        // is hardcoded 'USD' when the agreement is missing/lapsed, which would mislabel a non-USD
        // driver's real committed figures. Fall back to the preview only when nothing is committed.
        var currency = statements.Count > 0
            ? (statements[0] as Dictionary<string, object?>)!["currency"]?.ToString() ?? preview.Currency
            : preview.Currency;

        return new Dictionary<string, object?>
        {
            ["currency"] = currency,
            ["openPeriod"] = openPeriod,
            ["ytd"] = new Dictionary<string, object?>
            {
                ["year"]         = openEnd.Year,
                ["earned"]       = Money(roll?["ytdEarned"]),
                ["paid"]         = Money(roll?["ytdPaid"]),
                ["detentionPay"] = Money(det?["ytdDetention"]),
                ["detentionEvents"] = det?["ytdDetentionEvents"] ?? 0,
                ["statementCount"] = roll?["ytdCount"] ?? 0,
            },
            ["lifetime"] = new Dictionary<string, object?>
            {
                ["detentionPay"] = Money(det?["lifetimeDetention"]),
                ["earned"]       = Money(roll?["lifetimeEarned"]),
                ["paid"]         = Money(roll?["lifetimePaid"]),
            },
            ["detentionPolicyEnabled"] = policyEnabled,
            ["unpaidTotal"] = Money(roll?["unpaidTotal"]),
            ["lastPayment"] = lastPay is null ? null : new Dictionary<string, object?>
            {
                ["amount"] = Money(lastPay["amount"]),
                ["paidAt"] = lastPay["paidAt"],
            },
            ["statements"] = statements,
        };
    }

    // One owned statement's receipt: header + allow-listed lines + payments. Returns null when the
    // statement is not the driver's own committed statement — the caller maps null to 404 (identical
    // to a nonexistent id, so a sequential-id probe reveals nothing). basis_amount / unit_rate / basis
    // / job_id / payment method / reference are DELIBERATELY omitted from the projection.
    public async Task<Dictionary<string, object?>?> GetDriverStatementDetailAsync(
        long companyId, long driverId, long statementId, CancellationToken ct = default)
    {
        var header = await db.QuerySingleAsync(
            @"SELECT id, statement_no, period_start, period_end, status, currency, subtotal, total, amount_paid
              FROM settlement_statements
              WHERE id=@id AND company_id=@cid AND payee_type='driver' AND payee_id=@did
                AND source='system' AND status IN ('approved','paid')",
            c => { c.Parameters.AddWithValue("@id", statementId); c.Parameters.AddWithValue("@cid", companyId); c.Parameters.AddWithValue("@did", driverId); }, ct);
        if (header is null) return null;   // not owned / not committed -> 404 at the edge

        var lines = await db.QueryAsync(
            "SELECT line_no, pay_code, description, quantity, amount FROM settlement_lines WHERE company_id=@cid AND statement_id=@id ORDER BY line_no",
            c => { c.Parameters.AddWithValue("@cid", companyId); c.Parameters.AddWithValue("@id", statementId); }, ct);
        var payments = await db.QueryAsync(
            "SELECT amount, paid_at FROM settlement_payments WHERE company_id=@cid AND statement_id=@id ORDER BY paid_at",
            c => { c.Parameters.AddWithValue("@cid", companyId); c.Parameters.AddWithValue("@id", statementId); }, ct);

        decimal detention = 0m, linehaul = 0m, other = 0m;
        var lineOut = new List<object>(lines.Count);
        foreach (var l in lines)
        {
            var payCode = l["payCode"]?.ToString() ?? "linehaul";
            var amount  = Money(l["amount"]);
            if (payCode == "detention") detention += amount;
            else if (payCode == "linehaul") linehaul += amount;
            else other += amount;
            lineOut.Add(new Dictionary<string, object?>
            {
                ["lineNo"]      = l["lineNo"],
                ["payCode"]     = payCode,
                ["label"]       = PayCodeLabel(payCode),
                ["description"] = l["description"],
                ["quantity"]    = l["quantity"],
                ["amount"]      = amount,
            });
        }

        var total = Money(header["total"]);
        return new Dictionary<string, object?>
        {
            ["statement"] = new Dictionary<string, object?>
            {
                ["id"]          = header["id"],
                ["statementNo"] = header["statementNo"],
                ["periodStart"] = header["periodStart"],
                ["periodEnd"]   = header["periodEnd"],
                ["status"]      = header["status"],
                ["currency"]    = header["currency"],
                ["subtotal"]    = Money(header["subtotal"]),
                ["total"]       = total,
                ["amountPaid"]  = Money(header["amountPaid"]),
                ["outstanding"] = total - Money(header["amountPaid"]),
            },
            ["totals"] = new Dictionary<string, object?>
            {
                ["linehaul"]  = linehaul,
                ["detention"] = detention,
                ["other"]     = other,
                ["gross"]     = linehaul + detention + other,
            },
            ["lines"] = lineOut,
            ["payments"] = payments.Select(p => (object)new Dictionary<string, object?>
            {
                ["amount"] = Money(p["amount"]),
                ["paidAt"] = p["paidAt"],
            }).ToList(),
        };
    }

    private static decimal Money(object? v) => v is null or DBNull ? 0m : Convert.ToDecimal(v, CultureInfo.InvariantCulture);

    private static string PayCodeLabel(string payCode) => payCode switch
    {
        "linehaul"  => "Line-haul",
        "detention" => "Detention pay",
        _ => System.Globalization.CultureInfo.InvariantCulture.TextInfo.ToTitleCase(payCode.Replace('_', ' ')),
    };

    private static string FriendlyOpenReason(string? reason) => reason switch
    {
        "no_pay_agreement"   => "Your pay setup is being finalized.",
        "no_delivered_loads" => "No earnings calculated for this period yet.",
        "invalid_period"     => "You're all caught up — no open period yet.",
        _ when reason is not null && reason.StartsWith("basis_unsupported_phase1") => "Your pay plan isn't supported for a live preview yet.",
        _ => "No earnings calculated for this period yet.",
    };

    private sealed record PayAgreement(long Id, string Basis, decimal Rate, decimal? MinPay, string Currency);
    private sealed record DeliveredLoad(long JobId, decimal? Miles);
}
