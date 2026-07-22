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
        if (loads.Count == 0)
            return new SettlementStatementOutcome(false, null, null, "empty", 0, 0, agreement.Currency, empty, "no_delivered_loads");

        var lines = new List<SettlementComputedLine>(loads.Count);
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

        var subtotal = lines.Sum(l => l.Amount);
        var total = subtotal;

        if (mode == SettlementMode.Preview)
            return new SettlementStatementOutcome(false, null, null, "preview", subtotal, total, agreement.Currency, lines, "preview");

        return await CommitAsync(companyId, driverId, periodStart, periodEnd, agreement, lines, subtotal, total, ct);
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

    private sealed record PayAgreement(long Id, string Basis, decimal Rate, decimal? MinPay, string Currency);
    private sealed record DeliveredLoad(long JobId, decimal? Miles);
}
