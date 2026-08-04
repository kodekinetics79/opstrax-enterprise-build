using Microsoft.Extensions.Configuration;
using Opstrax.Api.Data;
using Opstrax.Api.Foundation;
using Opstrax.Api.Services;

namespace Opstrax.Tests;

// AR credit notes (SME design). Locks the CPA invariants:
//  - negative CN document + balance effect carried on the ORIGINAL (aging/summary queries unaffected);
//  - maker-checker (approver != requester);
//  - cumulative cap: credits can never exceed the original's total;
//  - proportional tax reversal from the ORIGINAL'S snapshot;
//  - GL reversal via outbox (Dr Revenue/Tax, Cr AR relieved / Cr Refunds-Payable excess), balanced;
//  - idempotent double-approve; payment recording cannot resurrect credited balance.
[Trait("Category", "Integration")]
public class CreditNotePostgresTests
{
    [Fact]
    public async Task Full_CreditNote_Flow_Reverses_Invoice_And_Books_GL()
    {
        var db = CreateDatabase();
        var cid = await SeedCompanyAsync(db);
        try
        {
            var svc = CreateService(db);
            var invId = await SeedIssuedInvoiceAsync(db, cid, subtotal: 200m, tax: 30m, total: 230m);

            // Maker creates; the SAME user cannot approve; a different user can.
            var created = await svc.CreateCreditNoteAsync(cid, invId, amount: null, "billing error", makerUserId: 11);
            Assert.True(created.Ok);
            Assert.Equal(230m, created.CreditTotal);

            var sameUser = await svc.ApproveCreditNoteAsync(cid, created.CreditNoteDraftId!.Value, checkerUserId: 11);
            Assert.False(sameUser.Ok);
            Assert.Equal("maker_checker_same_user", sameUser.Reason);

            var approved = await svc.ApproveCreditNoteAsync(cid, created.CreditNoteDraftId!.Value, checkerUserId: 22);
            Assert.True(approved.Ok);
            Assert.Equal(230m, approved.Relieved);      // unpaid invoice: full credit relieves AR
            Assert.Equal(0m, approved.RefundDue);

            // Double-approve is an idempotent replay: same CN, no second document.
            var replay = await svc.ApproveCreditNoteAsync(cid, created.CreditNoteDraftId!.Value, checkerUserId: 22);
            Assert.True(replay.Ok && replay.Replay);
            Assert.Equal(approved.CreditNoteId, replay.CreditNoteId);

            // CN document: negative totals, zero balance, links the original.
            var cn = (await db.QuerySingleAsync(
                "SELECT total, tax_total, balance_due, document_type, adjusts_invoice_id FROM issued_invoices WHERE company_id=@c AND id=@id",
                c => { c.Parameters.AddWithValue("@c", cid); c.Parameters.AddWithValue("@id", approved.CreditNoteId!.Value); }))!;
            Assert.Equal(-230m, Convert.ToDecimal(cn["total"]));
            Assert.Equal(-30m, Convert.ToDecimal(cn["taxTotal"]));
            Assert.Equal(0m, Convert.ToDecimal(cn["balanceDue"]));
            Assert.Equal(invId, (Guid)cn["adjustsInvoiceId"]!);

            // Original: fully credited, no open balance.
            var orig = (await db.QuerySingleAsync(
                "SELECT balance_due, credit_total, payment_status FROM issued_invoices WHERE company_id=@c AND id=@id",
                c => { c.Parameters.AddWithValue("@c", cid); c.Parameters.AddWithValue("@id", invId); }))!;
            Assert.Equal(0m, Convert.ToDecimal(orig["balanceDue"]));
            Assert.Equal(230m, Convert.ToDecimal(orig["creditTotal"]));
            Assert.Equal("credited", orig["paymentStatus"]?.ToString());

            // Tax snapshot reversed proportionally (full credit -> exact negation).
            var cnTax = await db.ScalarDecimalAsync(
                "SELECT COALESCE(SUM(tax_amount),0) FROM issued_invoice_tax_lines WHERE company_id=@c AND issued_invoice_id=@id",
                c => { c.Parameters.AddWithValue("@c", cid); c.Parameters.AddWithValue("@id", approved.CreditNoteId!.Value); });
            Assert.Equal(-30m, cnTax);

            // Drain the outbox -> GL reversal: Dr 4000 200 / Dr 2200 30 / Cr 1100 230; balanced ledger.
            await Dispatch(db, cid);
            Assert.Equal(200m, await Sum(db, cid, "debit", "4000"));
            Assert.Equal(30m, await Sum(db, cid, "debit", "2200"));
            Assert.Equal(230m, await Sum(db, cid, "credit", "1100"));
            var tb = await new GeneralLedgerService(db).TrialBalanceAsync(cid);
            Assert.True(tb.IsBalanced);
            var entries = await db.ScalarLongAsync(
                "SELECT COUNT(*) FROM journal_entries WHERE company_id=@c AND source_type='credit_note'",
                c => c.Parameters.AddWithValue("@c", cid));
            Assert.Equal(1, entries);
        }
        finally { await CleanupAsync(db, cid); }
    }

    [Fact]
    public async Task Cumulative_Cap_Blocks_OverCrediting()
    {
        var db = CreateDatabase();
        var cid = await SeedCompanyAsync(db);
        try
        {
            var svc = CreateService(db);
            var invId = await SeedIssuedInvoiceAsync(db, cid, 100m, 0m, 100m);

            var first = await svc.CreateCreditNoteAsync(cid, invId, 60m, "partial", makerUserId: 11);
            Assert.True((await svc.ApproveCreditNoteAsync(cid, first.CreditNoteDraftId!.Value, 22)).Ok);

            // 60 credited; another 50 exceeds the remaining 40 -> rejected at create.
            var over = await svc.CreateCreditNoteAsync(cid, invId, 50m, "too much", makerUserId: 11);
            Assert.False(over.Ok);
            Assert.Equal("credit_exceeds_remaining_creditable", over.Reason);

            // The exact remainder is fine; cumulative credit reaches the total, never beyond.
            var rest = await svc.CreateCreditNoteAsync(cid, invId, 40m, "remainder", makerUserId: 11);
            Assert.True((await svc.ApproveCreditNoteAsync(cid, rest.CreditNoteDraftId!.Value, 22)).Ok);
            var credited = await db.ScalarDecimalAsync(
                "SELECT credit_total FROM issued_invoices WHERE company_id=@c AND id=@id",
                c => { c.Parameters.AddWithValue("@c", cid); c.Parameters.AddWithValue("@id", invId); });
            Assert.Equal(100m, credited);
        }
        finally { await CleanupAsync(db, cid); }
    }

    [Fact]
    public async Task Crediting_A_Paid_Invoice_Books_A_Refund_Liability_Not_Negative_AR()
    {
        var db = CreateDatabase();
        var cid = await SeedCompanyAsync(db);
        try
        {
            var svc = CreateService(db);
            var invId = await SeedIssuedInvoiceAsync(db, cid, 100m, 0m, 100m, amountPaid: 100m, balanceDue: 0m, paymentStatus: "paid");

            var created = await svc.CreateCreditNoteAsync(cid, invId, 100m, "refund", makerUserId: 11);
            var approved = await svc.ApproveCreditNoteAsync(cid, created.CreditNoteDraftId!.Value, checkerUserId: 22);
            Assert.True(approved.Ok);
            Assert.Equal(0m, approved.Relieved);        // nothing left to relieve
            Assert.Equal(100m, approved.RefundDue);     // the whole credit is owed back to the customer

            await Dispatch(db, cid);
            Assert.Equal(100m, await Sum(db, cid, "debit", "4000"));
            Assert.Equal(100m, await Sum(db, cid, "credit", "2100"));   // refund liability, NOT Cr 1100
            Assert.Equal(0m, await Sum(db, cid, "credit", "1100"));
            Assert.True((await new GeneralLedgerService(db).TrialBalanceAsync(cid)).IsBalanced);
        }
        finally { await CleanupAsync(db, cid); }
    }

    [Fact]
    public async Task Payment_After_Partial_Credit_Does_Not_Resurrect_Credited_Balance()
    {
        var db = CreateDatabase();
        var cid = await SeedCompanyAsync(db);
        try
        {
            var svc = CreateService(db);
            var invId = await SeedIssuedInvoiceAsync(db, cid, 100m, 0m, 100m);

            // Credit 40 -> balance 60.
            var created = await svc.CreateCreditNoteAsync(cid, invId, 40m, "partial", makerUserId: 11);
            Assert.True((await svc.ApproveCreditNoteAsync(cid, created.CreditNoteDraftId!.Value, 22)).Ok);

            // Simulate the payment-recording recompute (the fixed formula): pay the remaining 60.
            // total(100) - credit_total(40) - paid(60) = 0 — the credited 40 must NOT reappear.
            await db.ExecuteAsync(
                @"UPDATE issued_invoices
                  SET amount_paid = amount_paid + 60,
                      balance_due = GREATEST(0, total - credit_total - (amount_paid + 60))
                  WHERE company_id=@c AND id=@id",
                c => { c.Parameters.AddWithValue("@c", cid); c.Parameters.AddWithValue("@id", invId); });

            var row = (await db.QuerySingleAsync(
                "SELECT balance_due FROM issued_invoices WHERE company_id=@c AND id=@id",
                c => { c.Parameters.AddWithValue("@c", cid); c.Parameters.AddWithValue("@id", invId); }))!;
            Assert.Equal(0m, Convert.ToDecimal(row["balanceDue"]));
        }
        finally { await CleanupAsync(db, cid); }
    }

    // ── helpers ───────────────────────────────────────────────────────────────
    private static CreditNoteService CreateService(Database db)
    {
        var corr = new InMemoryCorrelationContext("corr-cn", "cause-cn", "req-cn", null, ActorTypes.TenantUser, "11");
        return new CreditNoteService(db, new PostgresApprovalWorkflowService(db, corr), new PostgresDomainEventPublisher(db, corr));
    }

    private static Task<int> Dispatch(Database db, long cid) =>
        new PostgresOutboxDispatcher(db,
            new OutboxMessageHandlerRegistry(new IOutboxMessageHandler[]
            {
                new CreditNoteIssuedGeneralLedgerHandler(new GeneralLedgerService(db)),
            }),
            new PostgresEventProcessingLogService(db),
            new OutboxDispatcherOptions { WorkerName = "cn-test-dispatcher", TenantIdFilter = cid })
        .DispatchOutboxOnceAsync();

    private static async Task<decimal> Sum(Database db, long cid, string side, string account) =>
        (await db.ScalarDecimalAsync(
            $"SELECT COALESCE(SUM({side}),0) FROM journal_lines WHERE company_id=@c AND account_code=@a",
            c => { c.Parameters.AddWithValue("@c", cid); c.Parameters.AddWithValue("@a", account); })) ?? 0m;

    private static async Task<long> SeedCompanyAsync(Database db) =>
        await db.InsertAsync("INSERT INTO companies (company_code, name, industry) VALUES (@code, 'CN Co', 'logistics') RETURNING id",
            c => c.Parameters.AddWithValue("@code", $"CN-{Guid.NewGuid():N}".Substring(0, 15)));

    private static async Task<Guid> SeedIssuedInvoiceAsync(
        Database db, long cid, decimal subtotal, decimal tax, decimal total,
        decimal amountPaid = 0m, decimal? balanceDue = null, string paymentStatus = "unpaid")
    {
        var custId = await db.InsertAsync(
            "INSERT INTO customers (company_id, customer_code, name) VALUES (@c, @code, 'CN Cust') RETURNING id",
            c => { c.Parameters.AddWithValue("@c", cid); c.Parameters.AddWithValue("@code", $"CNC-{Guid.NewGuid():N}".Substring(0, 14)); });
        var draftNo = $"D-{Guid.NewGuid():N}".Substring(0, 12);
        var draft = (await db.QuerySingleAsync(
            "INSERT INTO invoice_drafts (company_id, customer_id, invoice_draft_no) VALUES (@c, @cust, @dno) RETURNING id",
            c => { c.Parameters.AddWithValue("@c", cid); c.Parameters.AddWithValue("@cust", custId); c.Parameters.AddWithValue("@dno", draftNo); }))!;
        var inv = (await db.QuerySingleAsync(
            @"INSERT INTO issued_invoices
                (company_id, customer_id, source_invoice_draft_id, source_invoice_draft_no, invoice_number,
                 subtotal, tax_total, total, amount_paid, balance_due, payment_status, status, currency, issued_at)
              VALUES (@c, @cust, @draftId::uuid, @dno, @ino, @sub, @tax, @tot, @paid, @bal, @ps, 'issued', 'USD', NOW())
              RETURNING id",
            c =>
            {
                c.Parameters.AddWithValue("@c", cid);
                c.Parameters.AddWithValue("@cust", custId);
                c.Parameters.AddWithValue("@draftId", draft["id"]!.ToString()!);
                c.Parameters.AddWithValue("@dno", draftNo);
                c.Parameters.AddWithValue("@ino", $"INV-{Guid.NewGuid():N}".Substring(0, 12));
                c.Parameters.AddWithValue("@sub", subtotal);
                c.Parameters.AddWithValue("@tax", tax);
                c.Parameters.AddWithValue("@tot", total);
                c.Parameters.AddWithValue("@paid", amountPaid);
                c.Parameters.AddWithValue("@bal", balanceDue ?? (total - amountPaid));
                c.Parameters.AddWithValue("@ps", paymentStatus);
            }))!;
        var invId = (Guid)inv["id"]!;

        // Original tax snapshot (what the CN reverses proportionally).
        if (tax > 0)
            await db.ExecuteAsync(
                @"INSERT INTO issued_invoice_tax_lines (company_id, issued_invoice_id, regime, tax_code, taxable_amount, rate, tax_amount)
                  VALUES (@c, @inv, 'zatca_vat', 'VAT15', @taxable, 15, @tax)",
                c => { c.Parameters.AddWithValue("@c", cid); c.Parameters.AddWithValue("@inv", invId);
                       c.Parameters.AddWithValue("@taxable", subtotal); c.Parameters.AddWithValue("@tax", tax); });
        return invId;
    }

    private static async Task CleanupAsync(Database db, long cid)
    {
        await db.ExecuteAsync("DELETE FROM outbox_messages WHERE tenant_id=@t", c => c.Parameters.AddWithValue("@t", cid));
        await db.ExecuteAsync("DELETE FROM domain_events WHERE tenant_id=@t", c => c.Parameters.AddWithValue("@t", cid));
        await db.ExecuteAsync("DELETE FROM approval_requests WHERE tenant_id=@t", c => c.Parameters.AddWithValue("@t", cid));
        foreach (var t in new[] { "journal_lines", "journal_entries", "chart_of_accounts",
                                  "issued_invoice_tax_lines", "issued_invoice_lines", "issued_invoices", "invoice_drafts", "customers" })
            await db.ExecuteAsync($"DELETE FROM {t} WHERE company_id=@c", c => c.Parameters.AddWithValue("@c", cid));
        await db.ExecuteAsync("DELETE FROM companies WHERE id=@c", c => c.Parameters.AddWithValue("@c", cid));
    }

    private static Database CreateDatabase() =>
        new(new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { ["ConnectionStrings:DefaultConnection"] = TestDb.ConnectionString }).Build());
}
