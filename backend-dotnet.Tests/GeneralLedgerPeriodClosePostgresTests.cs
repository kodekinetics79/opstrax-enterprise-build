using Microsoft.Extensions.Configuration;
using Npgsql;
using Opstrax.Api.Data;
using Opstrax.Api.Services;

namespace Opstrax.Tests;

// GL period close + ERP export (blueprint period-close-erp-export). Locks the month-end control:
// maker-checker close (checker != maker), the trial-balance gate, the HARD back-posting lock (DB
// trigger blocks every writer, app check gives the friendly error), reopen, and a deterministic,
// fail-closed, injection-hardened ERP export.
[Trait("Category", "Integration")]
public class GeneralLedgerPeriodClosePostgresTests
{
    private static readonly DateTime LastMonth = new DateTime(DateTime.UtcNow.Year, DateTime.UtcNow.Month, 1).AddMonths(-1).AddDays(4);

    [Fact]
    public async Task Close_Locks_The_Month_Against_BackPosting_Then_Reopen_Unlocks()
    {
        var db = CreateDatabase();
        var cid = await SeedCompanyAsync(db);
        try
        {
            var gl = new GeneralLedgerService(db);
            var periods = new GeneralLedgerPeriodService(db);
            var code = GeneralLedgerPeriodService.PeriodCode(LastMonth);

            await periods.EnsurePeriodAsync(cid, LastMonth);
            await gl.PostEntryAsync(cid, LastMonth, "manual", $"pc-{Guid.NewGuid():N}", "sale",
                new[] { new GeneralLedgerService.Line("1000", 100m, 0m), new GeneralLedgerService.Line("4000", 0m, 100m) });

            // Maker-checker: same user rejected, different user closes; re-close is idempotent.
            var req = await periods.RequestCloseAsync(cid, code, makerUserId: 1);
            Assert.True(req.Ok);
            var sameUser = await periods.ApproveCloseAsync(cid, code, checkerUserId: 1);
            Assert.False(sameUser.Ok);
            Assert.Equal("maker_checker_same_user", sameUser.Reason);
            var closed = await periods.ApproveCloseAsync(cid, code, checkerUserId: 2);
            Assert.True(closed.Ok);
            var again = await periods.ApproveCloseAsync(cid, code, checkerUserId: 2);
            Assert.True(again.Ok); // idempotent

            // Frozen totals balance and the checksum is recorded.
            var p = (await periods.GetPeriodAsync(cid, code))!;
            Assert.Equal(Convert.ToDecimal(p["totalDebits"]), Convert.ToDecimal(p["totalCredits"]));
            Assert.False(string.IsNullOrEmpty(p["closeChecksum"]?.ToString()));

            // App-level lock: the service refuses a back-post with a typed exception.
            await Assert.ThrowsAsync<PeriodClosedException>(() =>
                gl.PostEntryAsync(cid, LastMonth, "manual", $"pc-{Guid.NewGuid():N}", "late",
                    new[] { new GeneralLedgerService.Line("1000", 5m, 0m), new GeneralLedgerService.Line("4000", 0m, 5m) }));

            // DB trigger: even a DIRECT insert (any writer, service bypassed) is rejected with P0001.
            var pgEx = await Assert.ThrowsAsync<PostgresException>(() => db.ExecuteAsync(
                "INSERT INTO journal_entries (company_id, entry_date, source_type, source_ref) VALUES (@c, @d, 'manual', 'sneak')",
                c => { c.Parameters.AddWithValue("@c", cid); c.Parameters.AddWithValue("@d", LastMonth.Date); }));
            Assert.Equal("P0001", pgEx.SqlState);

            // Posting into the CURRENT (open) month still works — the lock is scoped to the closed month.
            var openEntry = await gl.PostEntryAsync(cid, DateTime.UtcNow, "manual", $"pc-{Guid.NewGuid():N}", "current",
                new[] { new GeneralLedgerService.Line("1000", 7m, 0m), new GeneralLedgerService.Line("4000", 0m, 7m) });
            Assert.True(openEntry > 0);

            // Reopen unlocks back-posting.
            var reopened = await periods.ReopenAsync(cid, code, userId: 2);
            Assert.True(reopened.Ok);
            var lateEntry = await gl.PostEntryAsync(cid, LastMonth, "manual", $"pc-{Guid.NewGuid():N}", "adjustment",
                new[] { new GeneralLedgerService.Line("1000", 3m, 0m), new GeneralLedgerService.Line("4000", 0m, 3m) });
            Assert.True(lateEntry > 0);
        }
        finally { await CleanupAsync(db, cid); }
    }

    [Fact]
    public async Task Cannot_Close_The_Current_Month()
    {
        var db = CreateDatabase();
        var cid = await SeedCompanyAsync(db);
        try
        {
            var periods = new GeneralLedgerPeriodService(db);
            await periods.EnsurePeriodAsync(cid, DateTime.UtcNow);
            var req = await periods.RequestCloseAsync(cid, GeneralLedgerPeriodService.PeriodCode(DateTime.UtcNow), makerUserId: 1);
            Assert.False(req.Ok);
            Assert.Equal("cannot_close_current_or_future_period", req.Reason);
        }
        finally { await CleanupAsync(db, cid); }
    }

    [Fact]
    public async Task Export_Is_Deterministic_FailClosed_And_Injection_Hardened()
    {
        var db = CreateDatabase();
        var cid = await SeedCompanyAsync(db);
        try
        {
            var gl = new GeneralLedgerService(db);
            var periods = new GeneralLedgerPeriodService(db);
            var export = new GeneralLedgerExportService(db);
            var code = GeneralLedgerPeriodService.PeriodCode(LastMonth);
            await periods.EnsurePeriodAsync(cid, LastMonth);

            // Empty period: refused, never emits a file.
            var empty = await export.BuildExportAsync(cid, code, "csv", userId: 9);
            Assert.False(empty.Ok);
            Assert.Equal("empty_period", empty.Reason);

            // Memo starting with '=' must be neutralized in the CSV (anti-injection).
            await gl.PostEntryAsync(cid, LastMonth, "manual", $"ex-{Guid.NewGuid():N}", "=cmd()",
                new[] { new GeneralLedgerService.Line("1100", 230m, 0m), new GeneralLedgerService.Line("4000", 0m, 200m), new GeneralLedgerService.Line("2200", 0m, 30m) });

            var run1 = await export.BuildExportAsync(cid, code, "csv", userId: 9);
            var run2 = await export.BuildExportAsync(cid, code, "csv", userId: 9);
            Assert.True(run1.Ok && run2.Ok);
            Assert.Equal(run1.Checksum, run2.Checksum);                       // deterministic
            Assert.Equal(run1.Bytes!, run2.Bytes!);                           // byte-identical
            var text = System.Text.Encoding.UTF8.GetString(run1.Bytes!);
            Assert.Contains("'=cmd()", text);                                  // formula neutralized
            Assert.DoesNotContain(",=cmd()", text);

            // QuickBooks shape: header + zero cells blank, dates MM/dd/yyyy.
            var qbo = await export.BuildExportAsync(cid, code, "quickbooks", userId: 9);
            Assert.True(qbo.Ok);
            var qboText = System.Text.Encoding.UTF8.GetString(qbo.Bytes!);
            Assert.StartsWith("JournalNo,JournalDate,Currency,Memo,AccountNumber,Account,Debits,Credits", qboText);
            Assert.Contains(LastMonth.ToString("MM/dd/yyyy"), qboText);

            // Every run is audited.
            var runs = await db.ScalarLongAsync(
                "SELECT COUNT(*) FROM gl_export_runs WHERE company_id=@c AND period_code=@p",
                c => { c.Parameters.AddWithValue("@c", cid); c.Parameters.AddWithValue("@p", code); });
            Assert.Equal(3, runs);

            // For a CLOSED period the export checksum equals the frozen close checksum.
            await periods.RequestCloseAsync(cid, code, makerUserId: 1);
            await periods.ApproveCloseAsync(cid, code, checkerUserId: 2);
            var p = (await periods.GetPeriodAsync(cid, code))!;
            Assert.Equal(p["closeChecksum"]?.ToString(), run1.Checksum);
        }
        finally { await CleanupAsync(db, cid); }
    }

    private static async Task<long> SeedCompanyAsync(Database db) =>
        await db.InsertAsync("INSERT INTO companies (company_code, name, industry) VALUES (@code, 'PC Co', 'logistics') RETURNING id",
            c => c.Parameters.AddWithValue("@code", $"PC-{Guid.NewGuid():N}".Substring(0, 15)));

    private static async Task CleanupAsync(Database db, long cid)
    {
        await db.ExecuteAsync("DELETE FROM outbox_messages WHERE tenant_id=@t", c => c.Parameters.AddWithValue("@t", cid));
        foreach (var t in new[] { "journal_lines", "journal_entries", "chart_of_accounts", "gl_export_runs", "gl_periods" })
            await db.ExecuteAsync($"DELETE FROM {t} WHERE company_id=@c", c => c.Parameters.AddWithValue("@c", cid));
        await db.ExecuteAsync("DELETE FROM companies WHERE id=@c", c => c.Parameters.AddWithValue("@c", cid));
    }

    private static Database CreateDatabase() =>
        new(new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { ["ConnectionStrings:DefaultConnection"] = TestDb.ConnectionString }).Build());
}
