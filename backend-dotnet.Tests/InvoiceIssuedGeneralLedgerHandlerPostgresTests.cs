using Microsoft.Extensions.Configuration;
using Opstrax.Api.Data;
using Opstrax.Api.Foundation;
using Opstrax.Api.Services;

namespace Opstrax.Tests;

// GL auto-post on invoice.issued (event->ledger spine, blueprint gl-autopost-on-issue).
// Proves the outbox FAN-OUT semantics this feature required (the registry previously crashed on a
// duplicate event type — one handler per event) plus the money-path guarantees:
//   - an issued invoice auto-posts Dr AR / Cr Revenue / Cr Tax and the trial balance nets to zero;
//   - at-least-once delivery posts exactly once (UNIQUE(company_id, source_type, source_ref));
//   - all handlers for an event run; the message is processed only after ALL succeed;
//   - a partial failure retries the whole message WITHOUT duplicating the already-succeeded post;
//   - an imbalanced invoice fails closed (no post, error surfaced with the handler name).
[Trait("Category", "Integration")]
public class InvoiceIssuedGeneralLedgerHandlerPostgresTests
{
    // ── stubs for fan-out semantics ───────────────────────────────────────────
    private sealed class RecordingHandler : IOutboxMessageHandler
    {
        public int Calls;
        public string EventType => "invoice.issued";
        public Task HandleAsync(OutboxMessageRecord message, CancellationToken ct = default)
        { Calls++; return Task.CompletedTask; }
    }

    private sealed class FlakyHandler : IOutboxMessageHandler
    {
        public bool Throw = true;
        public string EventType => "invoice.issued";
        public Task HandleAsync(OutboxMessageRecord message, CancellationToken ct = default)
            => Throw ? throw new InvalidOperationException("simulated downstream outage") : Task.CompletedTask;
    }

    [Fact]
    public async Task Issued_Invoice_Auto_Posts_To_GL_Via_Dispatcher()
    {
        var db = CreateDatabase();
        var cid = await SeedCompanyAsync(db);
        try
        {
            var invId = await SeedIssuedInvoiceAsync(db, cid, subtotal: 200m, tax: 30m, total: 230m);
            await EnqueueInvoiceIssuedAsync(db, cid, invId);

            var processed = await Dispatch(db, cid, new InvoiceIssuedGeneralLedgerHandler(new GeneralLedgerService(db)));
            Assert.True(processed >= 1);

            Assert.Equal(230m, await Sum(db, cid, "debit", "1100"));
            Assert.Equal(200m, await Sum(db, cid, "credit", "4000"));
            Assert.Equal(30m,  await Sum(db, cid, "credit", "2200"));
            var tb = await new GeneralLedgerService(db).TrialBalanceAsync(cid);
            Assert.True(tb.IsBalanced);
            Assert.Equal("processed", await OutboxStatus(db, cid, invId));
        }
        finally { await CleanupAsync(db, cid); }
    }

    [Fact]
    public async Task Double_Delivery_Posts_Exactly_One_Journal_Entry()
    {
        var db = CreateDatabase();
        var cid = await SeedCompanyAsync(db);
        try
        {
            var invId = await SeedIssuedInvoiceAsync(db, cid, 100m, 0m, 100m);
            await EnqueueInvoiceIssuedAsync(db, cid, invId);
            await EnqueueInvoiceIssuedAsync(db, cid, invId);   // duplicate delivery

            await Dispatch(db, cid, new InvoiceIssuedGeneralLedgerHandler(new GeneralLedgerService(db)));

            var entries = await db.ScalarLongAsync(
                "SELECT COUNT(*) FROM journal_entries WHERE company_id=@c AND source_type='invoice' AND source_ref=@r",
                c => { c.Parameters.AddWithValue("@c", cid); c.Parameters.AddWithValue("@r", invId); });
            Assert.Equal(1, entries);
            // Zero-tax invoice books exactly two lines (no 2200 tax line).
            var lines = await db.ScalarLongAsync(
                "SELECT COUNT(*) FROM journal_lines WHERE company_id=@c", c => c.Parameters.AddWithValue("@c", cid));
            Assert.Equal(2, lines);
        }
        finally { await CleanupAsync(db, cid); }
    }

    [Fact]
    public async Task FanOut_Runs_All_Handlers_For_The_Same_Event_Type()
    {
        var db = CreateDatabase();
        var cid = await SeedCompanyAsync(db);
        try
        {
            var invId = await SeedIssuedInvoiceAsync(db, cid, 50m, 5m, 55m);
            await EnqueueInvoiceIssuedAsync(db, cid, invId);

            // Two handlers on one event type: previously this ToDictionary'd and CRASHED at construction.
            var sibling = new RecordingHandler();
            var registry = new OutboxMessageHandlerRegistry(new IOutboxMessageHandler[]
            {
                sibling, new InvoiceIssuedGeneralLedgerHandler(new GeneralLedgerService(db)),
            });
            Assert.Equal(2, registry.ResolveAll("invoice.issued").Count);

            await Dispatch(db, cid, registry);

            Assert.Equal(1, sibling.Calls);                              // sibling ran
            Assert.Equal(55m, await Sum(db, cid, "debit", "1100"));      // and the GL posted
            Assert.Equal("processed", await OutboxStatus(db, cid, invId));
        }
        finally { await CleanupAsync(db, cid); }
    }

    [Fact]
    public async Task Partial_Failure_Retries_Whole_Message_Without_Duplicate_Post()
    {
        var db = CreateDatabase();
        var cid = await SeedCompanyAsync(db);
        try
        {
            var invId = await SeedIssuedInvoiceAsync(db, cid, 80m, 0m, 80m);
            await EnqueueInvoiceIssuedAsync(db, cid, invId);

            // GL first (succeeds), flaky sibling second (throws) -> message must NOT be processed.
            var flaky = new FlakyHandler();
            var registry = new OutboxMessageHandlerRegistry(new IOutboxMessageHandler[]
            {
                new InvoiceIssuedGeneralLedgerHandler(new GeneralLedgerService(db)), flaky,
            });
            await Dispatch(db, cid, registry);

            Assert.Equal("retry_pending", await OutboxStatus(db, cid, invId));
            var lastError = (await db.QuerySingleAsync(
                "SELECT last_error FROM outbox_messages WHERE tenant_id=@t AND aggregate_id=@a",
                c => { c.Parameters.AddWithValue("@t", cid); c.Parameters.AddWithValue("@a", invId); }))!["lastError"]?.ToString();
            Assert.Contains("FlakyHandler", lastError);                  // failing handler named for audit

            // The GL post from the first pass IS committed (derive-beside, idempotent) — one entry.
            Assert.Equal(80m, await Sum(db, cid, "debit", "1100"));

            // Outage ends; clear the backoff and re-dispatch: whole message re-runs, GL does NOT double-post.
            flaky.Throw = false;
            await db.ExecuteAsync("UPDATE outbox_messages SET next_attempt_at=NULL WHERE tenant_id=@t",
                c => c.Parameters.AddWithValue("@t", cid));
            await Dispatch(db, cid, registry);

            Assert.Equal("processed", await OutboxStatus(db, cid, invId));
            Assert.Equal(80m, await Sum(db, cid, "debit", "1100"));      // still exactly one post
            var entries = await db.ScalarLongAsync(
                "SELECT COUNT(*) FROM journal_entries WHERE company_id=@c", c => c.Parameters.AddWithValue("@c", cid));
            Assert.Equal(1, entries);
        }
        finally { await CleanupAsync(db, cid); }
    }

    [Fact]
    public async Task Imbalanced_Invoice_Fails_Closed_And_Never_Posts()
    {
        var db = CreateDatabase();
        var cid = await SeedCompanyAsync(db);
        try
        {
            // total != subtotal + tax_total -> PostEntryAsync must refuse; the ledger stays clean.
            var invId = await SeedIssuedInvoiceAsync(db, cid, subtotal: 100m, tax: 10m, total: 999m);
            await EnqueueInvoiceIssuedAsync(db, cid, invId);

            await Dispatch(db, cid, new InvoiceIssuedGeneralLedgerHandler(new GeneralLedgerService(db)));

            var entries = await db.ScalarLongAsync(
                "SELECT COUNT(*) FROM journal_entries WHERE company_id=@c", c => c.Parameters.AddWithValue("@c", cid));
            Assert.Equal(0, entries);
            Assert.Equal("retry_pending", await OutboxStatus(db, cid, invId));
            var lastError = (await db.QuerySingleAsync(
                "SELECT last_error FROM outbox_messages WHERE tenant_id=@t AND aggregate_id=@a",
                c => { c.Parameters.AddWithValue("@t", cid); c.Parameters.AddWithValue("@a", invId); }))!["lastError"]?.ToString();
            Assert.Contains("InvoiceIssuedGeneralLedgerHandler", lastError);
            Assert.Contains("does not balance", lastError);
        }
        finally { await CleanupAsync(db, cid); }
    }

    // ── helpers ───────────────────────────────────────────────────────────────
    private static Task<int> Dispatch(Database db, long cid, IOutboxMessageHandler handler)
        => Dispatch(db, cid, new OutboxMessageHandlerRegistry(new[] { handler }));

    private static Task<int> Dispatch(Database db, long cid, OutboxMessageHandlerRegistry registry)
        => new PostgresOutboxDispatcher(db, registry, new PostgresEventProcessingLogService(db),
            new OutboxDispatcherOptions { WorkerName = "gl-test-dispatcher", TenantIdFilter = cid })
           .DispatchOutboxOnceAsync();

    private static Task EnqueueInvoiceIssuedAsync(Database db, long cid, string invoiceId) =>
        db.ExecuteAsync(
            @"INSERT INTO outbox_messages (tenant_id, event_type, aggregate_type, aggregate_id, payload_json, status, retry_count)
              VALUES (@t, 'invoice.issued', 'issued_invoice', @a, jsonb_build_object('invoiceId', @a), 'pending', 0)",
            c => { c.Parameters.AddWithValue("@t", cid); c.Parameters.AddWithValue("@a", invoiceId); });

    private static async Task<string?> OutboxStatus(Database db, long cid, string invoiceId) =>
        (await db.QuerySingleAsync(
            "SELECT status FROM outbox_messages WHERE tenant_id=@t AND aggregate_id=@a ORDER BY id LIMIT 1",
            c => { c.Parameters.AddWithValue("@t", cid); c.Parameters.AddWithValue("@a", invoiceId); }))?["status"]?.ToString();

    private static async Task<decimal> Sum(Database db, long cid, string side, string account) =>
        (await db.ScalarDecimalAsync(
            $"SELECT COALESCE(SUM({side}),0) FROM journal_lines WHERE company_id=@c AND account_code=@a",
            c => { c.Parameters.AddWithValue("@c", cid); c.Parameters.AddWithValue("@a", account); })) ?? 0m;

    private static async Task<long> SeedCompanyAsync(Database db) =>
        await db.InsertAsync("INSERT INTO companies (company_code, name, industry) VALUES (@code, 'GLH Co', 'logistics') RETURNING id",
            c => c.Parameters.AddWithValue("@code", $"GLH-{Guid.NewGuid():N}".Substring(0, 15)));

    private static async Task<string> SeedIssuedInvoiceAsync(Database db, long cid, decimal subtotal, decimal tax, decimal total)
    {
        var custId = await db.InsertAsync(
            "INSERT INTO customers (company_id, customer_code, name) VALUES (@c, @code, 'GLH Cust') RETURNING id",
            c => { c.Parameters.AddWithValue("@c", cid); c.Parameters.AddWithValue("@code", $"GLHC-{Guid.NewGuid():N}".Substring(0, 14)); });
        var draftNo = $"D-{Guid.NewGuid():N}".Substring(0, 12);
        var draft = (await db.QuerySingleAsync(
            "INSERT INTO invoice_drafts (company_id, customer_id, invoice_draft_no) VALUES (@c, @cust, @dno) RETURNING id",
            c => { c.Parameters.AddWithValue("@c", cid); c.Parameters.AddWithValue("@cust", custId); c.Parameters.AddWithValue("@dno", draftNo); }))!;
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
                c.Parameters.AddWithValue("@draftId", draft["id"]!.ToString()!);
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
        await db.ExecuteAsync("DELETE FROM outbox_messages WHERE tenant_id=@t", c => c.Parameters.AddWithValue("@t", cid));
        foreach (var t in new[] { "journal_lines", "journal_entries", "chart_of_accounts", "issued_invoices", "invoice_drafts", "customers" })
            await db.ExecuteAsync($"DELETE FROM {t} WHERE company_id=@c", c => c.Parameters.AddWithValue("@c", cid));
        await db.ExecuteAsync("DELETE FROM companies WHERE id=@c", c => c.Parameters.AddWithValue("@c", cid));
    }

    private static Database CreateDatabase() =>
        new(new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { ["ConnectionStrings:DefaultConnection"] = TestDb.ConnectionString }).Build());
}
