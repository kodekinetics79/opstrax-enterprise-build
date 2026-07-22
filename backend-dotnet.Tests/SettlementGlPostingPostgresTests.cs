using Microsoft.Extensions.Configuration;
using Opstrax.Api.Data;
using Opstrax.Api.Foundation;
using Opstrax.Api.Services;

namespace Opstrax.Tests;

// AP settlements -> GL (blueprint gl-post-ap-settlement). Approval accrues the payable
// (Dr 5000 Driver Pay Expense / Cr 2000 Accounts Payable); each payment relieves it
// (Dr 2000 / Cr 1000 Cash). Proves the full event chain (approve -> outbox -> handler -> GL),
// idempotency (no double liability / double cash-out), the fail-closed draft guard, and the
// AP reconciliation invariant: account 2000's net credit equals the unpaid balance.
[Trait("Category", "Integration")]
public class SettlementGlPostingPostgresTests
{
    [Fact]
    public async Task Approval_Accrues_Payable_Via_Outbox_End_To_End()
    {
        var db = CreateDatabase();
        var cid = await SeedCompanyAsync(db);
        try
        {
            var stmtId = await SeedStatementAsync(db, cid, total: 500m, status: "draft");

            // Approve through the SERVICE with a real publisher -> durable settlement.approved event.
            var svc = new SettlementService(db, CreatePublisher(db));
            var outcome = await svc.ApproveStatementAsync(cid, stmtId, userId: 7);
            Assert.True(outcome.Ok);

            // Re-approve is a no-op and must not re-publish.
            await svc.ApproveStatementAsync(cid, stmtId, userId: 7);
            var events = await db.ScalarLongAsync(
                "SELECT COUNT(*) FROM outbox_messages WHERE tenant_id=@t AND event_type='settlement.approved' AND aggregate_id=@a",
                c => { c.Parameters.AddWithValue("@t", cid); c.Parameters.AddWithValue("@a", stmtId.ToString()); });
            Assert.Equal(1, events);

            // Drain exactly as the background dispatcher does.
            await Dispatch(db, cid);

            Assert.Equal(500m, await Sum(db, cid, "debit", "5000"));
            Assert.Equal(500m, await Sum(db, cid, "credit", "2000"));
            var tb = await new GeneralLedgerService(db).TrialBalanceAsync(cid);
            Assert.True(tb.IsBalanced);
        }
        finally { await CleanupAsync(db, cid); }
    }

    [Fact]
    public async Task Posting_Twice_Creates_One_Entry_And_Draft_Posts_Nothing()
    {
        var db = CreateDatabase();
        var cid = await SeedCompanyAsync(db);
        try
        {
            var gl = new GeneralLedgerService(db);

            // Draft: fail-closed, nothing posts.
            var draftId = await SeedStatementAsync(db, cid, total: 100m, status: "draft");
            Assert.Equal(0, await gl.PostSettlementAsync(cid, draftId));
            Assert.Equal(0m, await Sum(db, cid, "credit", "2000"));

            // Approved: posts once; a second call returns the SAME entry. (Different payee — one
            // statement per payee+period is enforced by uq_settlement_statements_period.)
            var stmtId = await SeedStatementAsync(db, cid, total: 300m, status: "approved", payeeId: 2);
            var e1 = await gl.PostSettlementAsync(cid, stmtId);
            var e2 = await gl.PostSettlementAsync(cid, stmtId);
            Assert.True(e1 > 0);
            Assert.Equal(e1, e2);
            Assert.Equal(300m, await Sum(db, cid, "credit", "2000"));   // one liability, not two
        }
        finally { await CleanupAsync(db, cid); }
    }

    [Fact]
    public async Task Payments_Relieve_The_Payable_And_Reconcile_To_Unpaid_Balance()
    {
        var db = CreateDatabase();
        var cid = await SeedCompanyAsync(db);
        try
        {
            var stmtId = await SeedStatementAsync(db, cid, total: 400m, status: "approved");
            var svc = new SettlementService(db, CreatePublisher(db));
            var gl = new GeneralLedgerService(db);
            await gl.PostSettlementAsync(cid, stmtId);   // accrue: Cr 2000 = 400

            // Two partial payments; a retried duplicate (same idempotency key) must not re-pay.
            var p1 = await svc.RecordPaymentAsync(cid, stmtId, 150m, "ach", null, "pay-1", 7);
            var dup = await svc.RecordPaymentAsync(cid, stmtId, 150m, "ach", null, "pay-1", 7);
            var p2 = await svc.RecordPaymentAsync(cid, stmtId, 100m, "ach", null, "pay-2", 7);
            Assert.True(p1.Ok && p2.Ok);
            Assert.Equal("duplicate", dup.Reason);

            await Dispatch(db, cid);   // drain settlement.paid events -> GL cash-out entries

            // Exactly two payment entries (the duplicate never re-published or re-posted).
            var payEntries = await db.ScalarLongAsync(
                "SELECT COUNT(*) FROM journal_entries WHERE company_id=@c AND source_type='settlement_payment'",
                c => c.Parameters.AddWithValue("@c", cid));
            Assert.Equal(2, payEntries);

            Assert.Equal(250m, await Sum(db, cid, "debit", "2000"));    // AP relieved by total paid
            Assert.Equal(250m, await Sum(db, cid, "credit", "1000"));   // cash out

            // Reconciliation invariant: net AP credit == unpaid balance (400 - 250 = 150).
            var apNet = await Sum(db, cid, "credit", "2000") - await Sum(db, cid, "debit", "2000");
            Assert.Equal(150m, apNet);
            var tb = await gl.TrialBalanceAsync(cid);
            Assert.True(tb.IsBalanced);
        }
        finally { await CleanupAsync(db, cid); }
    }

    // ── helpers ───────────────────────────────────────────────────────────────
    private static IDomainEventPublisher CreatePublisher(Database db) =>
        new PostgresDomainEventPublisher(db,
            new InMemoryCorrelationContext("corr-apgl", "cause-apgl", "req-apgl", null, ActorTypes.TenantUser, "7"));

    private static Task<int> Dispatch(Database db, long cid) =>
        new PostgresOutboxDispatcher(db,
            new OutboxMessageHandlerRegistry(new IOutboxMessageHandler[]
            {
                new SettlementApprovedGlPostingHandler(new GeneralLedgerService(db)),
                new SettlementPaymentGlPostingHandler(new GeneralLedgerService(db)),
            }),
            new PostgresEventProcessingLogService(db),
            new OutboxDispatcherOptions { WorkerName = "apgl-test-dispatcher", TenantIdFilter = cid })
        .DispatchOutboxOnceAsync();

    private static async Task<decimal> Sum(Database db, long cid, string side, string account) =>
        (await db.ScalarDecimalAsync(
            $"SELECT COALESCE(SUM({side}),0) FROM journal_lines WHERE company_id=@c AND account_code=@a",
            c => { c.Parameters.AddWithValue("@c", cid); c.Parameters.AddWithValue("@a", account); })) ?? 0m;

    private static async Task<long> SeedCompanyAsync(Database db) =>
        await db.InsertAsync("INSERT INTO companies (company_code, name, industry) VALUES (@code, 'APGL Co', 'logistics') RETURNING id",
            c => c.Parameters.AddWithValue("@code", $"APGL-{Guid.NewGuid():N}".Substring(0, 15)));

    private static async Task<long> SeedStatementAsync(Database db, long cid, decimal total, string status, long payeeId = 1) =>
        await db.InsertAsync(
            @"INSERT INTO settlement_statements
                (company_id, statement_no, payee_type, payee_id, period_start, period_end, status, subtotal, total, currency, approved_at)
              VALUES (@c, @no, 'driver', @payee, CURRENT_DATE - 7, CURRENT_DATE, @st, @tot, @tot, 'USD',
                      CASE WHEN @st IN ('approved','paid') THEN NOW() ELSE NULL END)
              RETURNING id",
            c =>
            {
                c.Parameters.AddWithValue("@c", cid);
                c.Parameters.AddWithValue("@no", $"ST-{Guid.NewGuid():N}".Substring(0, 12));
                c.Parameters.AddWithValue("@payee", payeeId);
                c.Parameters.AddWithValue("@st", status);
                c.Parameters.AddWithValue("@tot", total);
            });

    private static async Task CleanupAsync(Database db, long cid)
    {
        await db.ExecuteAsync("DELETE FROM outbox_messages WHERE tenant_id=@t", c => c.Parameters.AddWithValue("@t", cid));
        await db.ExecuteAsync("DELETE FROM domain_events WHERE tenant_id=@t", c => c.Parameters.AddWithValue("@t", cid));
        foreach (var t in new[] { "journal_lines", "journal_entries", "chart_of_accounts", "settlement_payments", "settlement_statements" })
            await db.ExecuteAsync($"DELETE FROM {t} WHERE company_id=@c", c => c.Parameters.AddWithValue("@c", cid));
        await db.ExecuteAsync("DELETE FROM companies WHERE id=@c", c => c.Parameters.AddWithValue("@c", cid));
    }

    private static Database CreateDatabase() =>
        new(new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { ["ConnectionStrings:DefaultConnection"] = TestDb.ConnectionString }).Build());
}
