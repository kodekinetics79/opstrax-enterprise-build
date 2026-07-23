using Microsoft.Extensions.Configuration;
using Opstrax.Api.Data;
using Opstrax.Api.Services;

namespace Opstrax.Tests;

// General Ledger foundation — the double-entry book of record that unifies the AR/AP/tax/rev-rec
// sub-ledgers. These tests lock the two invariants that make it trustworthy: every entry balances, and a
// source event posts at most once — so the trial balance always nets to zero.
[Trait("Category", "Integration")]
public class GeneralLedgerPostgresTests
{
    private static GeneralLedgerService Gl(Database db) => new(db);

    [Fact]
    public async Task Balanced_Entry_Posts_And_Trial_Balance_Nets_To_Zero()
    {
        var db = CreateDatabase();
        var cid = await SeedCompanyAsync(db);
        try
        {
            await Gl(db).PostEntryAsync(cid, DateTime.UtcNow, "manual", $"t-{Guid.NewGuid():N}", "cash sale",
                new[] { new GeneralLedgerService.Line("1000", 100m, 0m), new GeneralLedgerService.Line("4000", 0m, 100m) });

            var tb = await Gl(db).TrialBalanceAsync(cid);
            Assert.True(tb.IsBalanced);
            Assert.Equal(100m, tb.TotalDebits);
            Assert.Equal(100m, tb.TotalCredits);
        }
        finally { await CleanupAsync(db, cid); }
    }

    [Fact]
    public async Task Unbalanced_Entry_Is_Rejected()
    {
        var db = CreateDatabase();
        var cid = await SeedCompanyAsync(db);
        try
        {
            await Assert.ThrowsAsync<InvalidOperationException>(() =>
                Gl(db).PostEntryAsync(cid, DateTime.UtcNow, "manual", $"t-{Guid.NewGuid():N}", "bad",
                    new[] { new GeneralLedgerService.Line("1000", 100m, 0m), new GeneralLedgerService.Line("4000", 0m, 90m) }));
        }
        finally { await CleanupAsync(db, cid); }
    }

    [Fact]
    public async Task Posting_An_Invoice_Twice_Is_Idempotent_And_Books_AR_Revenue_Tax()
    {
        var db = CreateDatabase();
        var cid = await SeedCompanyAsync(db);
        try
        {
            var invId = await SeedIssuedInvoiceAsync(db, cid, subtotal: 200m, tax: 30m, total: 230m);

            var e1 = await Gl(db).PostInvoiceAsync(cid, invId);
            var e2 = await Gl(db).PostInvoiceAsync(cid, invId);   // second call must not double-post
            Assert.Equal(e1, e2);

            // Dr Accounts Receivable 230, Cr Revenue 200, Cr Tax Payable 30.
            Assert.Equal(230m, await AcctDebit(db, cid, "1100"));
            Assert.Equal(200m, await AcctCredit(db, cid, "4000"));
            Assert.Equal(30m,  await AcctCredit(db, cid, "2200"));

            var tb = await Gl(db).TrialBalanceAsync(cid);
            Assert.True(tb.IsBalanced);
            Assert.Equal(230m, tb.TotalDebits);
            Assert.Equal(230m, tb.TotalCredits);

            // Exactly one journal entry for the invoice.
            var entries = await db.ScalarLongAsync(
                "SELECT COUNT(*) FROM journal_entries WHERE company_id=@c AND source_type='invoice' AND source_ref=@r",
                c => { c.Parameters.AddWithValue("@c", cid); c.Parameters.AddWithValue("@r", invId); });
            Assert.Equal(1, entries);
        }
        finally { await CleanupAsync(db, cid); }
    }

    private static async Task<decimal> AcctDebit(Database db, long cid, string code) =>
        (await db.ScalarDecimalAsync("SELECT COALESCE(SUM(debit),0) FROM journal_lines WHERE company_id=@c AND account_code=@a",
            c => { c.Parameters.AddWithValue("@c", cid); c.Parameters.AddWithValue("@a", code); })) ?? 0m;

    private static async Task<decimal> AcctCredit(Database db, long cid, string code) =>
        (await db.ScalarDecimalAsync("SELECT COALESCE(SUM(credit),0) FROM journal_lines WHERE company_id=@c AND account_code=@a",
            c => { c.Parameters.AddWithValue("@c", cid); c.Parameters.AddWithValue("@a", code); })) ?? 0m;

    private static async Task<long> SeedCompanyAsync(Database db) =>
        await db.InsertAsync("INSERT INTO companies (company_code, name, industry) VALUES (@code, 'GL Co', 'logistics') RETURNING id",
            c => c.Parameters.AddWithValue("@code", $"GL-{Guid.NewGuid():N}".Substring(0, 15)));

    private static async Task<string> SeedIssuedInvoiceAsync(Database db, long cid, decimal subtotal, decimal tax, decimal total)
    {
        var custId = await db.InsertAsync(
            "INSERT INTO customers (company_id, customer_code, name) VALUES (@c, @code, 'GL Cust') RETURNING id",
            c => { c.Parameters.AddWithValue("@c", cid); c.Parameters.AddWithValue("@code", $"GLC-{Guid.NewGuid():N}".Substring(0, 14)); });
        // issued_invoices.source_invoice_draft_id has an FK to invoice_drafts — seed a draft first.
        var draftNo = $"D-{Guid.NewGuid():N}".Substring(0, 12);
        var draft = (await db.QuerySingleAsync(
            "INSERT INTO invoice_drafts (company_id, customer_id, invoice_draft_no) VALUES (@c, @cust, @dno) RETURNING id",
            c => { c.Parameters.AddWithValue("@c", cid); c.Parameters.AddWithValue("@cust", custId); c.Parameters.AddWithValue("@dno", draftNo); }))!;
        var draftId = draft["id"]!.ToString()!;
        var row = (await db.QuerySingleAsync(
            @"INSERT INTO issued_invoices
                (company_id, customer_id, source_invoice_draft_id, source_invoice_draft_no, invoice_number,
                 subtotal, tax_total, total, status, currency, issued_at)
              VALUES (@c, @cust, @draftId::uuid, @dno, @ino, @sub, @tax, @tot, 'issued', 'USD', NOW())
              RETURNING id",
            c =>
            {
                c.Parameters.AddWithValue("@c", cid);
                c.Parameters.AddWithValue("@cust", custId);
                c.Parameters.AddWithValue("@draftId", draftId);
                c.Parameters.AddWithValue("@dno", draftNo);
                c.Parameters.AddWithValue("@ino", $"INV-{Guid.NewGuid():N}".Substring(0, 12));
                c.Parameters.AddWithValue("@sub", subtotal);
                c.Parameters.AddWithValue("@tax", tax);
                c.Parameters.AddWithValue("@tot", total);
            }))!;
        return row["id"]!.ToString()!;
    }

    private static async Task CleanupAsync(Database db, long cid)
    {
        await db.ExecuteAsync("DELETE FROM journal_lines WHERE company_id=@c", c => c.Parameters.AddWithValue("@c", cid));
        await db.ExecuteAsync("DELETE FROM journal_entries WHERE company_id=@c", c => c.Parameters.AddWithValue("@c", cid));
        await db.ExecuteAsync("DELETE FROM chart_of_accounts WHERE company_id=@c", c => c.Parameters.AddWithValue("@c", cid));
        await db.ExecuteAsync("DELETE FROM issued_invoices WHERE company_id=@c", c => c.Parameters.AddWithValue("@c", cid));
        await db.ExecuteAsync("DELETE FROM invoice_drafts WHERE company_id=@c", c => c.Parameters.AddWithValue("@c", cid));
        await db.ExecuteAsync("DELETE FROM customers WHERE company_id=@c", c => c.Parameters.AddWithValue("@c", cid));
        await db.ExecuteAsync("DELETE FROM companies WHERE id=@c", c => c.Parameters.AddWithValue("@c", cid));
    }

    private static Database CreateDatabase() =>
        new(new ConfigurationBuilder().AddInMemoryCollection(
            new Dictionary<string, string?> { ["ConnectionStrings:DefaultConnection"] = TestDb.ConnectionString }).Build());
}
