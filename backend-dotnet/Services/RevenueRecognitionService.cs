using System.Globalization;
using Npgsql;
using Opstrax.Api.Data;

namespace Opstrax.Api.Services;

public enum RecognitionMode { Preview, Commit }

public sealed record RecognitionOutcome(
    bool Recognized, string? Reason, long? EntryId, decimal Amount, decimal AmountFunctional, string Currency, string Status);

// Revenue recognition sub-ledger (ADR-008 rev-rec layer). Derives recognized-revenue entries BESIDE
// issued_invoices, never mutating them. P0: accrual + on_invoice only (recognize the full issued total
// at issue date into the current open fiscal period). Fail-closed like Rating/Tax/Settlement: no
// profile / unsupported method|trigger / missing FX rate => zero writes + a durable reason. Append-only
// two-tier immutability: 'pending' (recomputable, open period) -> 'posted' (frozen at close); every
// correction is a reversing entry.
public sealed class RevenueRecognitionService(Database db)
{
    public async Task<RecognitionOutcome> RecognizeInvoiceAsync(long companyId, Guid issuedInvoiceId, RecognitionMode mode, CancellationToken ct = default)
    {
        var inv = await db.QuerySingleAsync(
            "SELECT currency, subtotal, tax_total, total, issued_at, customer_id, job_id FROM issued_invoices WHERE company_id=@c AND id=@i",
            c => { c.Parameters.AddWithValue("@c", companyId); c.Parameters.AddWithValue("@i", issuedInvoiceId); }, ct);
        if (inv is null) return Fail("invoice_not_found");

        var currency = inv.GetValueOrDefault("currency")?.ToString() ?? "USD";
        var issuedAt = DateOnly.FromDateTime(Convert.ToDateTime(inv["issuedAt"], CultureInfo.InvariantCulture));
        var profile = await ResolveProfileAsync(companyId, issuedAt, ct);
        if (profile is null) return Fail("no_revrec_profile");
        if (profile.Method != "accrual") return Fail($"method_unsupported_phase0:{profile.Method}");
        if (profile.Trigger != "on_invoice") return Fail($"trigger_unsupported_phase0:{profile.Trigger}");

        var amount = profile.RecognizeBase == "net_of_tax"
            ? Convert.ToDecimal(inv.GetValueOrDefault("subtotal") ?? 0m, CultureInfo.InvariantCulture)
            : Convert.ToDecimal(inv.GetValueOrDefault("total") ?? 0m, CultureInfo.InvariantCulture);

        // FX: functional currency from the profile. Same currency => rate 1; different + no rate => fail
        // closed (never guess a rate). P0 only carries a 1:1 same-currency path.
        decimal fxRate; decimal amountFunctional;
        if (string.Equals(profile.FunctionalCurrency, currency, StringComparison.OrdinalIgnoreCase))
        {
            fxRate = 1m; amountFunctional = amount;
        }
        else
        {
            return Fail("no_fx_rate");
        }

        if (mode == RecognitionMode.Preview)
            return new RecognitionOutcome(false, "preview", null, amount, amountFunctional, currency, "preview");

        var customerId = inv.GetValueOrDefault("customerId") is { } cu and not DBNull ? Convert.ToInt64(cu, CultureInfo.InvariantCulture) : (long?)null;
        var jobId = inv.GetValueOrDefault("jobId") is { } jb and not DBNull ? Convert.ToInt64(jb, CultureInfo.InvariantCulture) : (long?)null;

        try
        {
            var entryId = await db.WithTransactionAsync(async (conn, tx) =>
            {
                var periodId = await ResolveOpenPeriodAsync(companyId, issuedAt, conn, tx, ct);
                // Serialize against close: lock the period row; a closed period rejects (defensive — for
                // on_invoice the recognition date is always in the current open period).
                await using (var lockCmd = new NpgsqlCommand("SELECT status FROM revrec_fiscal_periods WHERE company_id=@c AND id=@p FOR UPDATE", conn, tx))
                {
                    lockCmd.Parameters.AddWithValue("@c", companyId); lockCmd.Parameters.AddWithValue("@p", periodId);
                    var status = (await lockCmd.ExecuteScalarAsync(ct))?.ToString();
                    if (status == "closed") throw new PeriodClosedException();
                }

                // Recompute scope: drop only this invoice's PENDING system entries in OPEN periods.
                await Exec(conn, tx,
                    @"DELETE FROM revenue_recognition_entries e
                      USING revrec_fiscal_periods p
                      WHERE e.company_id=@c AND e.issued_invoice_id=@i AND e.source='system' AND e.entry_type='recognition'
                        AND e.issued_invoice_line_id IS NULL AND e.schedule_id IS NULL
                        AND e.status='pending' AND p.id=e.fiscal_period_id AND p.status='open'",
                    p => { p.AddWithValue("@c", companyId); p.AddWithValue("@i", issuedInvoiceId); }, ct);

                long id;
                await using (var ins = new NpgsqlCommand(
                    @"INSERT INTO revenue_recognition_entries
                        (company_id, issued_invoice_id, customer_id, job_id, profile_id, fiscal_period_id, entry_type,
                         recognition_date, currency, amount, functional_currency, fx_rate, amount_functional,
                         revenue_account_code, deferred_revenue_account_code, status, source)
                      VALUES (@c, @i, @cust, @job, @pid, @period, 'recognition',
                              @date, @cur, @amt, @fcur, @fx, @famt, @rev, @def, 'pending', 'system') RETURNING id", conn, tx))
                {
                    ins.Parameters.AddWithValue("@c", companyId); ins.Parameters.AddWithValue("@i", issuedInvoiceId);
                    ins.Parameters.AddWithValue("@cust", (object?)customerId ?? DBNull.Value); ins.Parameters.AddWithValue("@job", (object?)jobId ?? DBNull.Value);
                    ins.Parameters.AddWithValue("@pid", profile.Id); ins.Parameters.AddWithValue("@period", periodId);
                    ins.Parameters.AddWithValue("@date", issuedAt); ins.Parameters.AddWithValue("@cur", currency);
                    ins.Parameters.AddWithValue("@amt", amount); ins.Parameters.AddWithValue("@fcur", profile.FunctionalCurrency);
                    ins.Parameters.AddWithValue("@fx", fxRate); ins.Parameters.AddWithValue("@famt", amountFunctional);
                    ins.Parameters.AddWithValue("@rev", (object?)profile.RevenueAccountCode ?? DBNull.Value);
                    ins.Parameters.AddWithValue("@def", (object?)profile.DeferredRevenueAccountCode ?? DBNull.Value);
                    id = Convert.ToInt64((await ins.ExecuteScalarAsync(ct))!, CultureInfo.InvariantCulture);
                }

                // Atomic event enqueue on the same tx (never events.Publish inside a tx).
                await Exec(conn, tx,
                    @"INSERT INTO outbox_messages (tenant_id, event_type, aggregate_type, aggregate_id, payload_json, created_at, status, retry_count)
                      SELECT @c, 'revenue.recognized', 'issued_invoice', @i::text, jsonb_build_object('issuedInvoiceId', @i, 'amountFunctional', @famt), NOW(), 'pending', 0
                      ON CONFLICT (tenant_id, aggregate_id) WHERE event_type='revenue.recognized' DO NOTHING",
                    p => { p.AddWithValue("@c", companyId); p.AddWithValue("@i", issuedInvoiceId); p.AddWithValue("@famt", amountFunctional); }, ct);
                return id;
            }, ct);

            return new RecognitionOutcome(true, null, entryId, amount, amountFunctional, currency, "pending");
        }
        catch (PeriodClosedException)
        {
            return Fail("period_closed");
        }
        catch (PostgresException ex) when (ex.SqlState == "23505")
        {
            // A concurrent/duplicate recognition already exists — a NO-OP success, never dead-letter.
            return new RecognitionOutcome(true, "already_recognized", null, amount, amountFunctional, currency, "pending");
        }
    }

    // Corrections are reversing entries, never edits/deletes. Posts a contra in the current open period.
    public async Task<RecognitionOutcome> ReverseInvoiceRecognitionAsync(long companyId, Guid issuedInvoiceId, string memo, long userId, CancellationToken ct = default)
    {
        var live = await db.QueryAsync(
            @"SELECT id, amount, amount_functional, currency, functional_currency, customer_id, job_id
              FROM revenue_recognition_entries
              WHERE company_id=@c AND issued_invoice_id=@i AND entry_type='recognition'
                AND NOT EXISTS (SELECT 1 FROM revenue_recognition_entries r WHERE r.company_id=@c AND r.reverses_entry_id = revenue_recognition_entries.id)",
            c => { c.Parameters.AddWithValue("@c", companyId); c.Parameters.AddWithValue("@i", issuedInvoiceId); }, ct);
        if (live.Count == 0) return Fail("nothing_to_reverse");

        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var reversed = 0;
        await db.WithTransactionAsync(async (conn, tx) =>
        {
            var periodId = await ResolveOpenPeriodAsync(companyId, today, conn, tx, ct);
            foreach (var e in live)
            {
                try
                {
                    await Exec(conn, tx,
                        @"INSERT INTO revenue_recognition_entries
                            (company_id, issued_invoice_id, customer_id, job_id, fiscal_period_id, entry_type, recognition_date,
                             currency, amount, functional_currency, fx_rate, amount_functional, status, source, reverses_entry_id, memo, created_by_user_id)
                          VALUES (@c, @i, @cust, @job, @period, 'reversal', @date, @cur, @amt, @fcur, 1, @famt, 'pending', 'system', @rev, @memo, @uid)",
                        p => {
                            p.AddWithValue("@c", companyId); p.AddWithValue("@i", issuedInvoiceId);
                            p.AddWithValue("@cust", e.GetValueOrDefault("customerId") is { } cu and not DBNull ? cu : DBNull.Value);
                            p.AddWithValue("@job", e.GetValueOrDefault("jobId") is { } jb and not DBNull ? jb : DBNull.Value);
                            p.AddWithValue("@period", periodId); p.AddWithValue("@date", today);
                            p.AddWithValue("@cur", e.GetValueOrDefault("currency")?.ToString() ?? "USD");
                            p.AddWithValue("@amt", -Convert.ToDecimal(e["amount"], CultureInfo.InvariantCulture));
                            p.AddWithValue("@fcur", e.GetValueOrDefault("functionalCurrency")?.ToString() ?? "USD");
                            p.AddWithValue("@famt", -Convert.ToDecimal(e["amountFunctional"], CultureInfo.InvariantCulture));
                            p.AddWithValue("@rev", Convert.ToInt64(e["id"], CultureInfo.InvariantCulture));
                            p.AddWithValue("@memo", (object?)memo ?? DBNull.Value); p.AddWithValue("@uid", userId);
                        }, ct);
                    reversed++;
                }
                catch (PostgresException ex) when (ex.SqlState == "23505") { /* already reversed — idempotent */ }
            }
            return true;
        }, ct);
        return new RecognitionOutcome(reversed > 0, reversed > 0 ? null : "already_reversed", null, 0, 0, "USD", "reversed");
    }

    // High-risk, approval-gated at the endpoint. Freezes a fully-past open period to 'posted'.
    public async Task<SettlementService.SettlementActionOutcome> CloseFiscalPeriodAsync(long companyId, string periodCode, long userId, CancellationToken ct = default)
    {
        var row = await db.QuerySingleAsync("SELECT id, status, period_end FROM revrec_fiscal_periods WHERE company_id=@c AND period_code=@p",
            c => { c.Parameters.AddWithValue("@c", companyId); c.Parameters.AddWithValue("@p", periodCode); }, ct);
        if (row is null) return new SettlementService.SettlementActionOutcome(false, "missing", "period_not_found");
        var status = row.GetValueOrDefault("status")?.ToString() ?? "open";
        if (status == "closed") return new SettlementService.SettlementActionOutcome(true, "closed"); // idempotent
        var periodEnd = DateOnly.FromDateTime(Convert.ToDateTime(row["periodEnd"], CultureInfo.InvariantCulture));
        if (periodEnd >= DateOnly.FromDateTime(DateTime.UtcNow))
            return new SettlementService.SettlementActionOutcome(false, status, "cannot_close_current_or_future_period");
        var periodId = Convert.ToInt64(row["id"], CultureInfo.InvariantCulture);

        await db.WithTransactionAsync(async (conn, tx) =>
        {
            await using (var lockCmd = new NpgsqlCommand("SELECT 1 FROM revrec_fiscal_periods WHERE company_id=@c AND id=@p FOR UPDATE", conn, tx))
            { lockCmd.Parameters.AddWithValue("@c", companyId); lockCmd.Parameters.AddWithValue("@p", periodId); await lockCmd.ExecuteScalarAsync(ct); }

            await Exec(conn, tx, "UPDATE revenue_recognition_entries SET status='posted' WHERE company_id=@c AND fiscal_period_id=@p AND status='pending'",
                p => { p.AddWithValue("@c", companyId); p.AddWithValue("@p", periodId); }, ct);
            await Exec(conn, tx,
                @"UPDATE revrec_fiscal_periods SET status='closed', closed_at=NOW(), closed_by_user_id=@uid,
                    entry_count=(SELECT COUNT(*) FROM revenue_recognition_entries WHERE company_id=@c AND fiscal_period_id=@p),
                    recognized_total_functional=(SELECT COALESCE(SUM(amount_functional),0) FROM revenue_recognition_entries WHERE company_id=@c AND fiscal_period_id=@p),
                    close_checksum=(SELECT md5(COALESCE(string_agg(issued_invoice_id::text||':'||amount::text||':'||recognition_date::text, '|' ORDER BY issued_invoice_id, amount, recognition_date), ''))
                                    FROM revenue_recognition_entries WHERE company_id=@c AND fiscal_period_id=@p),
                    updated_at=NOW()
                  WHERE company_id=@c AND id=@p",
                p => { p.AddWithValue("@c", companyId); p.AddWithValue("@p", periodId); p.AddWithValue("@uid", userId); }, ct);
            await Exec(conn, tx,
                @"INSERT INTO outbox_messages (tenant_id, event_type, aggregate_type, aggregate_id, payload_json, created_at, status, retry_count)
                  VALUES (@c, 'revenue.period_closed', 'fiscal_period', @p::text, jsonb_build_object('periodCode', @code), NOW(), 'pending', 0)",
                p => { p.AddWithValue("@c", companyId); p.AddWithValue("@p", periodId); p.AddWithValue("@code", periodCode); }, ct);
            return true;
        }, ct);
        return new SettlementService.SettlementActionOutcome(true, "closed");
    }

    // Idempotent admin sweep over historical issued invoices. Run BEFORE closing any period.
    public async Task<RecognitionOutcome> BackfillIssuedInvoicesAsync(long companyId, long userId, CancellationToken ct = default)
    {
        var ids = await db.QueryAsync("SELECT id FROM issued_invoices WHERE company_id=@c ORDER BY issued_at",
            c => c.Parameters.AddWithValue("@c", companyId), ct);
        var recognized = 0;
        foreach (var r in ids)
        {
            var o = await RecognizeInvoiceAsync(companyId, (Guid)r["id"]!, RecognitionMode.Commit, ct);
            if (o.Recognized) recognized++;
        }
        return new RecognitionOutcome(true, $"backfilled {recognized}", null, 0, 0, "USD", "backfill");
    }

    // ── reads ──

    public async Task<Dictionary<string, object?>?> GetInvoiceRecognitionAsync(long companyId, Guid issuedInvoiceId, CancellationToken ct = default)
    {
        var entries = await db.QueryAsync("SELECT * FROM revenue_recognition_entries WHERE company_id=@c AND issued_invoice_id=@i ORDER BY id",
            c => { c.Parameters.AddWithValue("@c", companyId); c.Parameters.AddWithValue("@i", issuedInvoiceId); }, ct);
        return new Dictionary<string, object?> { ["issuedInvoiceId"] = issuedInvoiceId, ["entries"] = entries };
    }

    public async Task<List<Dictionary<string, object?>>> ListEntriesAsync(long companyId, string? periodCode, string? status, long? customerId, CancellationToken ct = default)
        => await db.QueryAsync(
            @"SELECT e.* FROM revenue_recognition_entries e
              LEFT JOIN revrec_fiscal_periods p ON p.id=e.fiscal_period_id AND p.company_id=e.company_id
              WHERE e.company_id=@c AND (@period IS NULL OR p.period_code=@period)
                AND (@status IS NULL OR e.status=@status) AND (@cust IS NULL OR e.customer_id=@cust)
              ORDER BY e.id DESC LIMIT 500",
            c => { c.Parameters.AddWithValue("@c", companyId); c.Parameters.AddWithValue("@period", (object?)periodCode ?? DBNull.Value);
                   c.Parameters.AddWithValue("@status", (object?)status ?? DBNull.Value); c.Parameters.AddWithValue("@cust", (object?)customerId ?? DBNull.Value); }, ct);

    public async Task<List<Dictionary<string, object?>>> GetRecognizedRevenueSummaryAsync(long companyId, DateOnly from, DateOnly to, CancellationToken ct = default)
        => await db.QueryAsync(
            @"SELECT functional_currency, SUM(amount_functional) AS recognized_functional, COUNT(*) AS entry_count
              FROM revenue_recognition_entries
              WHERE company_id=@c AND entry_type IN ('recognition','reversal') AND recognition_date BETWEEN @from AND @to
              GROUP BY functional_currency",
            c => { c.Parameters.AddWithValue("@c", companyId); c.Parameters.AddWithValue("@from", from); c.Parameters.AddWithValue("@to", to); }, ct);

    public async Task<List<Dictionary<string, object?>>> ListPeriodsAsync(long companyId, CancellationToken ct = default)
        => await db.QueryAsync("SELECT * FROM revrec_fiscal_periods WHERE company_id=@c ORDER BY period_start DESC LIMIT 500",
            c => c.Parameters.AddWithValue("@c", companyId), ct);

    // ── helpers ──

    private async Task<RevrecProfile?> ResolveProfileAsync(long companyId, DateOnly asOf, CancellationToken ct)
    {
        var r = await db.QuerySingleAsync(
            @"SELECT id, method, trigger, recognize_base, functional_currency, revenue_account_code, deferred_revenue_account_code
              FROM revrec_profiles
              WHERE company_id=@c AND status='published'
                AND effective_from <= @d AND (effective_to IS NULL OR effective_to > @d)
              ORDER BY is_default ASC, effective_from DESC LIMIT 1",
            c => { c.Parameters.AddWithValue("@c", companyId); c.Parameters.AddWithValue("@d", asOf); }, ct);
        if (r is null) return null;
        return new RevrecProfile(
            Convert.ToInt64(r["id"], CultureInfo.InvariantCulture),
            (r.GetValueOrDefault("method")?.ToString() ?? "accrual").Trim().ToLowerInvariant(),
            (r.GetValueOrDefault("trigger")?.ToString() ?? "on_invoice").Trim().ToLowerInvariant(),
            (r.GetValueOrDefault("recognizeBase")?.ToString() ?? "total").Trim().ToLowerInvariant(),
            r.GetValueOrDefault("functionalCurrency")?.ToString() ?? "USD",
            r.GetValueOrDefault("revenueAccountCode")?.ToString(),
            r.GetValueOrDefault("deferredRevenueAccountCode")?.ToString());
    }

    // Earliest open period covering asOf; lazily creates the month period containing asOf if none.
    private async Task<long> ResolveOpenPeriodAsync(long companyId, DateOnly asOf, NpgsqlConnection conn, NpgsqlTransaction tx, CancellationToken ct)
    {
        await using (var find = new NpgsqlCommand(
            "SELECT id FROM revrec_fiscal_periods WHERE company_id=@c AND status='open' AND period_start<=@d AND period_end>=@d ORDER BY period_start LIMIT 1", conn, tx))
        {
            find.Parameters.AddWithValue("@c", companyId); find.Parameters.AddWithValue("@d", asOf);
            if (await find.ExecuteScalarAsync(ct) is { } existing) return Convert.ToInt64(existing, CultureInfo.InvariantCulture);
        }
        var start = new DateOnly(asOf.Year, asOf.Month, 1);
        var end = start.AddMonths(1).AddDays(-1);
        var code = $"{asOf.Year:D4}-{asOf.Month:D2}";
        await using (var ins = new NpgsqlCommand(
            @"INSERT INTO revrec_fiscal_periods (company_id, period_code, period_start, period_end, status)
              VALUES (@c, @code, @s, @e, 'open') ON CONFLICT (company_id, period_code) DO NOTHING", conn, tx))
        {
            ins.Parameters.AddWithValue("@c", companyId); ins.Parameters.AddWithValue("@code", code);
            ins.Parameters.AddWithValue("@s", start); ins.Parameters.AddWithValue("@e", end);
            await ins.ExecuteNonQueryAsync(ct);
        }
        await using var sel = new NpgsqlCommand("SELECT id FROM revrec_fiscal_periods WHERE company_id=@c AND period_code=@code", conn, tx);
        sel.Parameters.AddWithValue("@c", companyId); sel.Parameters.AddWithValue("@code", code);
        return Convert.ToInt64((await sel.ExecuteScalarAsync(ct))!, CultureInfo.InvariantCulture);
    }

    private static async Task Exec(NpgsqlConnection conn, NpgsqlTransaction tx, string sql, Action<NpgsqlParameterCollection> bind, CancellationToken ct)
    {
        await using var cmd = new NpgsqlCommand(sql, conn, tx);
        bind(cmd.Parameters);
        await cmd.ExecuteNonQueryAsync(ct);
    }

    private static RecognitionOutcome Fail(string reason) => new(false, reason, null, 0, 0, "USD", "unrecognized");

    private sealed record RevrecProfile(long Id, string Method, string Trigger, string RecognizeBase, string FunctionalCurrency, string? RevenueAccountCode, string? DeferredRevenueAccountCode);
    private sealed class PeriodClosedException : Exception;
}
