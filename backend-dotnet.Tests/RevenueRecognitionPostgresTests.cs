using Microsoft.Extensions.Configuration;
using Opstrax.Api.Data;
using Opstrax.Api.Services;

namespace Opstrax.Tests;

// Revenue recognition sub-ledger (ADR-008). Verified against real Postgres: the additive default (no
// profile => no recognition), accrual/on_invoice full-total recognition, idempotency, fail-closed on
// unsupported method/trigger, reversing entries, and period close freezing entries to 'posted'.
[Trait("Category", "Integration")]
public class RevenueRecognitionPostgresTests
{
    [Fact]
    public async Task No_Profile_Produces_No_Recognition()
    {
        var (db, svc, cid, cust) = Setup();
        try
        {
            var inv = await IssuedInvoiceAsync(db, cid, cust, 1000m, DateTime.UtcNow);
            var o = await svc.RecognizeInvoiceAsync(cid, inv, RecognitionMode.Commit);
            Assert.False(o.Recognized);
            Assert.Equal("no_revrec_profile", o.Reason);
            Assert.Equal(0, await EntryCount(db, cid, inv));
        }
        finally { await CleanupAsync(db, cid); }
    }

    [Fact]
    public async Task Accrual_OnInvoice_Recognizes_Full_Total()
    {
        var (db, svc, cid, cust) = Setup();
        try
        {
            await ProfileAsync(db, cid, "accrual", "on_invoice");
            var inv = await IssuedInvoiceAsync(db, cid, cust, 1000m, DateTime.UtcNow);
            var o = await svc.RecognizeInvoiceAsync(cid, inv, RecognitionMode.Commit);

            Assert.True(o.Recognized);
            Assert.Equal(1000m, o.Amount);
            Assert.Equal(1000m, o.AmountFunctional);
            Assert.Equal("pending", o.Status);
            Assert.Equal(1, await EntryCount(db, cid, inv));
        }
        finally { await CleanupAsync(db, cid); }
    }

    [Fact]
    public async Task Recognition_Is_Idempotent()
    {
        var (db, svc, cid, cust) = Setup();
        try
        {
            await ProfileAsync(db, cid, "accrual", "on_invoice");
            var inv = await IssuedInvoiceAsync(db, cid, cust, 1000m, DateTime.UtcNow);
            await svc.RecognizeInvoiceAsync(cid, inv, RecognitionMode.Commit);
            var o = await svc.RecognizeInvoiceAsync(cid, inv, RecognitionMode.Commit);
            Assert.True(o.Recognized);
            Assert.Equal(1, await EntryCount(db, cid, inv));   // one recognition, not two
        }
        finally { await CleanupAsync(db, cid); }
    }

    [Fact]
    public async Task Cash_Method_Is_FailClosed()
    {
        var (db, svc, cid, cust) = Setup();
        try
        {
            await ProfileAsync(db, cid, "cash", "on_invoice");
            var inv = await IssuedInvoiceAsync(db, cid, cust, 1000m, DateTime.UtcNow);
            var o = await svc.RecognizeInvoiceAsync(cid, inv, RecognitionMode.Commit);
            Assert.False(o.Recognized);
            Assert.StartsWith("method_unsupported_phase0", o.Reason);
            Assert.Equal(0, await EntryCount(db, cid, inv));
        }
        finally { await CleanupAsync(db, cid); }
    }

    [Fact]
    public async Task OverTime_Trigger_Is_FailClosed()
    {
        var (db, svc, cid, cust) = Setup();
        try
        {
            await ProfileAsync(db, cid, "accrual", "over_time");
            var inv = await IssuedInvoiceAsync(db, cid, cust, 1000m, DateTime.UtcNow);
            var o = await svc.RecognizeInvoiceAsync(cid, inv, RecognitionMode.Commit);
            Assert.False(o.Recognized);
            Assert.StartsWith("trigger_unsupported_phase0", o.Reason);
        }
        finally { await CleanupAsync(db, cid); }
    }

    [Fact]
    public async Task Reverse_Posts_A_Contra_Entry()
    {
        var (db, svc, cid, cust) = Setup();
        try
        {
            await ProfileAsync(db, cid, "accrual", "on_invoice");
            var inv = await IssuedInvoiceAsync(db, cid, cust, 1000m, DateTime.UtcNow);
            await svc.RecognizeInvoiceAsync(cid, inv, RecognitionMode.Commit);

            var o = await svc.ReverseInvoiceRecognitionAsync(cid, inv, "void", 42);
            Assert.True(o.Recognized);
            var net = await db.ScalarDecimalAsync("SELECT COALESCE(SUM(amount_functional),0) FROM revenue_recognition_entries WHERE company_id=@c AND issued_invoice_id=@i",
                c => { c.Parameters.AddWithValue("@c", cid); c.Parameters.AddWithValue("@i", inv); });
            Assert.Equal(0m, net);   // recognition + reversal net to zero
            // Idempotent: a second reverse adds no further contra.
            await svc.ReverseInvoiceRecognitionAsync(cid, inv, "void", 42);
            Assert.Equal(2, await EntryCount(db, cid, inv));
        }
        finally { await CleanupAsync(db, cid); }
    }

    [Fact]
    public async Task Close_Period_Freezes_Entries_To_Posted()
    {
        var (db, svc, cid, cust) = Setup();
        try
        {
            await ProfileAsync(db, cid, "accrual", "on_invoice");
            // Invoice issued in a fully-past month so the period can be closed.
            var past = new DateTime(2026, 1, 15, 0, 0, 0, DateTimeKind.Utc);
            var inv = await IssuedInvoiceAsync(db, cid, cust, 500m, past);
            await svc.RecognizeInvoiceAsync(cid, inv, RecognitionMode.Commit);

            var o = await svc.CloseFiscalPeriodAsync(cid, "2026-01", 42);
            Assert.True(o.Ok);
            var posted = await db.ScalarLongAsync("SELECT COUNT(*) FROM revenue_recognition_entries WHERE company_id=@c AND issued_invoice_id=@i AND status='posted'",
                c => { c.Parameters.AddWithValue("@c", cid); c.Parameters.AddWithValue("@i", inv); });
            Assert.Equal(1, posted);
            var closed = (await db.QuerySingleAsync("SELECT status, recognized_total_functional FROM revrec_fiscal_periods WHERE company_id=@c AND period_code='2026-01'",
                c => c.Parameters.AddWithValue("@c", cid)))!;
            Assert.Equal("closed", closed["status"]?.ToString());
            Assert.Equal(500m, Convert.ToDecimal(closed["recognizedTotalFunctional"]));
        }
        finally { await CleanupAsync(db, cid); }
    }

    // ── helpers ──

    private static (Database, RevenueRecognitionService, long, long) Setup()
    {
        var db = new Database(new ConfigurationBuilder().AddInMemoryCollection(
            new Dictionary<string, string?> { ["ConnectionStrings:DefaultConnection"] = TestDb.ConnectionString }).Build());
        var cid = db.InsertAsync("INSERT INTO companies (company_code, name, industry) VALUES (@code,'RR Co','logistics') RETURNING id",
            c => c.Parameters.AddWithValue("@code", $"RR-{Guid.NewGuid():N}".Substring(0, 16))).GetAwaiter().GetResult();
        var cust = db.InsertAsync("INSERT INTO customers (company_id, customer_code, name) VALUES (@c,@code,'Buyer') RETURNING id",
            c => { c.Parameters.AddWithValue("@c", cid); c.Parameters.AddWithValue("@code", $"CU-{cid}"); }).GetAwaiter().GetResult();
        return (db, new RevenueRecognitionService(db), cid, cust);
    }

    private static async Task ProfileAsync(Database db, long cid, string method, string trigger) =>
        await db.ExecuteAsync(
            @"INSERT INTO revrec_profiles (company_id, profile_code, profile_name, method, trigger, recognize_base, functional_currency, status, is_default)
              VALUES (@c, @code, 'P', @m, @t, 'total', 'USD', 'published', TRUE)",
            c => { c.Parameters.AddWithValue("@c", cid); c.Parameters.AddWithValue("@code", $"RP-{cid}");
                   c.Parameters.AddWithValue("@m", method); c.Parameters.AddWithValue("@t", trigger); });

    private static async Task<Guid> IssuedInvoiceAsync(Database db, long cid, long cust, decimal total, DateTime issuedAt)
    {
        var draftId = (Guid)(await db.QuerySingleAsync(
            "INSERT INTO invoice_drafts (company_id, customer_id, invoice_draft_no, status, currency, subtotal, tax_total, total) VALUES (@c,@cust,@no,'issued','USD',@t,0,@t) RETURNING id",
            c => { c.Parameters.AddWithValue("@c", cid); c.Parameters.AddWithValue("@cust", cust);
                   c.Parameters.AddWithValue("@no", $"D-{cid}-{Guid.NewGuid():N}".Substring(0, 24)); c.Parameters.AddWithValue("@t", total); }))!["id"]!;
        return (Guid)(await db.QuerySingleAsync(
            @"INSERT INTO issued_invoices (company_id, customer_id, source_invoice_draft_id, source_invoice_draft_no, invoice_number, status, currency, subtotal, tax_total, total, issued_at)
              VALUES (@c, @cust, @sd, @sn, @num, 'issued', 'USD', @t, 0, @t, @issued) RETURNING id",
            c => { c.Parameters.AddWithValue("@c", cid); c.Parameters.AddWithValue("@cust", cust);
                   c.Parameters.AddWithValue("@sd", draftId); c.Parameters.AddWithValue("@sn", $"D-{cid}");
                   c.Parameters.AddWithValue("@num", $"INV-{cid}-{Guid.NewGuid():N}".Substring(0, 24));
                   c.Parameters.AddWithValue("@t", total); c.Parameters.AddWithValue("@issued", issuedAt); }))!["id"]!;
    }

    private static async Task<long> EntryCount(Database db, long cid, Guid inv) =>
        await db.ScalarLongAsync("SELECT COUNT(*) FROM revenue_recognition_entries WHERE company_id=@c AND issued_invoice_id=@i",
            c => { c.Parameters.AddWithValue("@c", cid); c.Parameters.AddWithValue("@i", inv); });

    private static async Task CleanupAsync(Database db, long cid)
    {
        foreach (var t in new[] { "revenue_recognition_entries", "revrec_fiscal_periods", "revrec_profiles", "issued_invoices", "invoice_drafts", "customers" })
            await db.ExecuteAsync($"DELETE FROM {t} WHERE company_id=@c", c => c.Parameters.AddWithValue("@c", cid));
        await db.ExecuteAsync("DELETE FROM companies WHERE id=@c", c => c.Parameters.AddWithValue("@c", cid));
    }
}
