using System.Globalization;
using Npgsql;
using Opstrax.Api.Data;

namespace Opstrax.Api.Services;

public sealed record PeriodActionOutcome(bool Ok, string Status, string? Reason = null);

// GL period lifecycle: open -> pending_close -> closed (maker-checker) -> optionally reopened.
// Closing a period locks it against back-posting (DB trigger + app-level check) and freezes the
// period's totals + a deterministic checksum, so an ERP export of a closed period is verifiable.
// Fail-open for OPEN periods: EnsurePeriodAsync lazily creates the calendar month and normal posting
// is never blocked; only an explicitly closed month blocks.
public sealed class GeneralLedgerPeriodService(Database db)
{
    public static string PeriodCode(DateTime date) => date.ToString("yyyy-MM", CultureInfo.InvariantCulture);

    public async Task EnsurePeriodAsync(long companyId, DateTime date, CancellationToken ct = default)
    {
        var start = new DateTime(date.Year, date.Month, 1);
        var end = start.AddMonths(1).AddDays(-1);
        await db.ExecuteAsync(
            @"INSERT INTO gl_periods (company_id, period_code, period_start, period_end)
              VALUES (@c, @code, @s, @e)
              ON CONFLICT (company_id, period_code) DO NOTHING",
            c =>
            {
                c.Parameters.AddWithValue("@c", companyId);
                c.Parameters.AddWithValue("@code", PeriodCode(start));
                c.Parameters.AddWithValue("@s", start.Date);
                c.Parameters.AddWithValue("@e", end.Date);
            }, ct);
    }

    // Maker: request the close. Current/future months cannot close (the month must be over).
    public async Task<PeriodActionOutcome> RequestCloseAsync(long companyId, string periodCode, long makerUserId, CancellationToken ct = default)
    {
        var row = await GetPeriodAsync(companyId, periodCode, ct);
        if (row is null) return new PeriodActionOutcome(false, "missing", "not_found");
        var status = row["status"]?.ToString() ?? "open";
        if (status == "closed") return new PeriodActionOutcome(true, "closed");   // idempotent
        var periodEnd = Convert.ToDateTime(row["periodEnd"]);
        if (periodEnd.Date >= DateTime.UtcNow.Date)
            return new PeriodActionOutcome(false, status, "cannot_close_current_or_future_period");

        await db.ExecuteAsync(
            @"UPDATE gl_periods SET status='pending_close', requested_by_user_id=@u, requested_at=NOW(), updated_at=NOW()
              WHERE company_id=@c AND period_code=@code AND status IN ('open','pending_close')",
            c => { c.Parameters.AddWithValue("@c", companyId); c.Parameters.AddWithValue("@code", periodCode); c.Parameters.AddWithValue("@u", makerUserId); }, ct);
        return new PeriodActionOutcome(true, "pending_close");
    }

    // Checker: lock the period. Dual control (checker != maker), serialized via FOR UPDATE, and the
    // trial-balance gate — a period that does not balance REFUSES to close.
    public async Task<PeriodActionOutcome> ApproveCloseAsync(long companyId, string periodCode, long checkerUserId, CancellationToken ct = default)
    {
        return await db.WithTransactionAsync(async (conn, tx) =>
        {
            long pid; string status; long? maker; DateTime periodStart, periodEnd;
            await using (var sel = new NpgsqlCommand(
                @"SELECT id, status, requested_by_user_id, period_start, period_end
                  FROM gl_periods WHERE company_id=@c AND period_code=@code FOR UPDATE", conn, tx))
            {
                sel.Parameters.AddWithValue("@c", companyId);
                sel.Parameters.AddWithValue("@code", periodCode);
                await using var rdr = await sel.ExecuteReaderAsync(ct);
                if (!await rdr.ReadAsync(ct)) return new PeriodActionOutcome(false, "missing", "not_found");
                pid = rdr.GetInt64(0);
                status = rdr.GetString(1);
                maker = rdr.IsDBNull(2) ? null : rdr.GetInt64(2);
                periodStart = rdr.GetDateTime(3);
                periodEnd = rdr.GetDateTime(4);
            }

            if (status == "closed") return new PeriodActionOutcome(true, "closed");   // idempotent
            if (status != "pending_close") return new PeriodActionOutcome(false, status, "not_pending_close");
            if (maker.HasValue && maker.Value == checkerUserId)
                return new PeriodActionOutcome(false, status, "maker_checker_same_user");

            // Freeze totals + checksum, then assert balance BEFORE committing the lock.
            decimal debits, credits;
            await using (var close = new NpgsqlCommand(
                @"UPDATE gl_periods SET status='closed', closed_by_user_id=@u, closed_at=NOW(), updated_at=NOW(),
                      entry_count = (SELECT COUNT(*) FROM journal_entries je
                                     WHERE je.company_id=@c AND je.entry_date BETWEEN @s AND @e),
                      total_debits = (SELECT COALESCE(SUM(jl.debit),0) FROM journal_lines jl
                                      JOIN journal_entries je ON je.id=jl.journal_entry_id
                                      WHERE jl.company_id=@c AND je.entry_date BETWEEN @s AND @e),
                      total_credits = (SELECT COALESCE(SUM(jl.credit),0) FROM journal_lines jl
                                       JOIN journal_entries je ON je.id=jl.journal_entry_id
                                       WHERE jl.company_id=@c AND je.entry_date BETWEEN @s AND @e),
                      close_checksum = (SELECT md5(COALESCE(string_agg(
                                            je.id||':'||jl.account_code||':'||jl.debit||':'||jl.credit||':'||je.entry_date,
                                            '|' ORDER BY je.id, jl.id), ''))
                                        FROM journal_lines jl
                                        JOIN journal_entries je ON je.id=jl.journal_entry_id
                                        WHERE jl.company_id=@c AND je.entry_date BETWEEN @s AND @e)
                  WHERE id=@pid AND company_id=@c
                  RETURNING total_debits, total_credits", conn, tx))
            {
                close.Parameters.AddWithValue("@c", companyId);
                close.Parameters.AddWithValue("@u", checkerUserId);
                close.Parameters.AddWithValue("@s", periodStart.Date);
                close.Parameters.AddWithValue("@e", periodEnd.Date);
                close.Parameters.AddWithValue("@pid", pid);
                await using var rdr = await close.ExecuteReaderAsync(ct);
                await rdr.ReadAsync(ct);
                debits = rdr.GetDecimal(0);
                credits = rdr.GetDecimal(1);
            }

            if (Math.Round(debits, 2) != Math.Round(credits, 2))
                throw new InvalidOperationException(
                    $"Refusing to close {periodCode}: trial balance does not balance (debits {debits:0.00} != credits {credits:0.00}).");

            // Durable event on the same transaction as the state change.
            await using (var outbox = new NpgsqlCommand(
                @"INSERT INTO outbox_messages (tenant_id, event_type, aggregate_type, aggregate_id, payload_json, status, retry_count)
                  VALUES (@t, 'gl.period_closed', 'gl_period', @code,
                          jsonb_build_object('periodCode', @code, 'totalDebits', @d, 'totalCredits', @cr), 'pending', 0)", conn, tx))
            {
                outbox.Parameters.AddWithValue("@t", companyId);
                outbox.Parameters.AddWithValue("@code", periodCode);
                outbox.Parameters.AddWithValue("@d", debits);
                outbox.Parameters.AddWithValue("@cr", credits);
                await outbox.ExecuteNonQueryAsync(ct);
            }

            return new PeriodActionOutcome(true, "closed");
        }, ct);
    }

    // Auditor escape hatch for corrections: closed -> open, audited by the caller.
    public async Task<PeriodActionOutcome> ReopenAsync(long companyId, string periodCode, long userId, CancellationToken ct = default)
    {
        var row = await GetPeriodAsync(companyId, periodCode, ct);
        if (row is null) return new PeriodActionOutcome(false, "missing", "not_found");
        if (row["status"]?.ToString() != "closed") return new PeriodActionOutcome(false, row["status"]?.ToString() ?? "open", "not_closed");

        await db.ExecuteAsync(
            @"UPDATE gl_periods SET status='open', updated_at=NOW() WHERE company_id=@c AND period_code=@code AND status='closed'",
            c => { c.Parameters.AddWithValue("@c", companyId); c.Parameters.AddWithValue("@code", periodCode); }, ct);
        await db.ExecuteAsync(
            @"INSERT INTO outbox_messages (tenant_id, event_type, aggregate_type, aggregate_id, payload_json, status, retry_count)
              VALUES (@t, 'gl.period_reopened', 'gl_period', @code, jsonb_build_object('periodCode', @code, 'byUserId', @u), 'pending', 0)",
            c => { c.Parameters.AddWithValue("@t", companyId); c.Parameters.AddWithValue("@code", periodCode); c.Parameters.AddWithValue("@u", userId); }, ct);
        return new PeriodActionOutcome(true, "open");
    }

    public async Task<List<Dictionary<string, object?>>> ListPeriodsAsync(long companyId, CancellationToken ct = default) =>
        await db.QueryAsync(
            "SELECT * FROM gl_periods WHERE company_id=@c ORDER BY period_code DESC",
            c => c.Parameters.AddWithValue("@c", companyId), ct);

    public async Task<Dictionary<string, object?>?> GetPeriodAsync(long companyId, string periodCode, CancellationToken ct = default) =>
        await db.QuerySingleAsync(
            "SELECT * FROM gl_periods WHERE company_id=@c AND period_code=@code",
            c => { c.Parameters.AddWithValue("@c", companyId); c.Parameters.AddWithValue("@code", periodCode); }, ct);
}
