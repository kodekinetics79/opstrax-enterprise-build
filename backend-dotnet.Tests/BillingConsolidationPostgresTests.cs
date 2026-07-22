using Microsoft.Extensions.Configuration;
using Opstrax.Api.Data;
using Opstrax.Api.Services;

namespace Opstrax.Tests;

// Billing consolidation (ADR-008 Billing layer). Verified against real Postgres: period + per-load
// consolidation, idempotent regenerate, fail-closed on no charges, double-bill prevention, and the
// LegacyDefault (no profile => per-load, reproducing today's per-job billing).
[Trait("Category", "Integration")]
public class BillingConsolidationPostgresTests
{
    private static readonly DateOnly PStart = new(2026, 1, 1);
    private static readonly DateOnly PEnd = new(2026, 12, 31);

    [Fact]
    public async Task Period_Consolidation_Bills_Multiple_Jobs_As_One_Draft()
    {
        var (db, svc, cid, cust) = Setup();
        try
        {
            await ProfileAsync(db, cid, "period");
            await DeliveredChargeAsync(db, cid, cust, 100m);
            await DeliveredChargeAsync(db, cid, cust, 200m);

            var o = await svc.GenerateConsolidatedDraftsAsync(cid, cust, PStart, PEnd, null, BillingMode.Commit);

            Assert.True(o.Generated);
            Assert.Equal(1, o.GroupCount);
            Assert.Equal(1, o.DraftCount);
            Assert.Equal(300m, o.Subtotal);
            var draftId = o.Groups[0].DraftId!.Value;
            var lines = await db.ScalarLongAsync("SELECT COUNT(*) FROM invoice_draft_lines WHERE company_id=@c AND invoice_draft_id=@d",
                c => { c.Parameters.AddWithValue("@c", cid); c.Parameters.AddWithValue("@d", draftId); });
            Assert.Equal(2, lines);
            // Charges claimed.
            var drafted = await db.ScalarLongAsync("SELECT COUNT(*) FROM job_charges WHERE company_id=@c AND billing_status='drafted'",
                c => c.Parameters.AddWithValue("@c", cid));
            Assert.Equal(2, drafted);
        }
        finally { await CleanupAsync(db, cid); }
    }

    [Fact]
    public async Task PerLoad_Default_Bills_One_Draft_Per_Job()
    {
        var (db, svc, cid, cust) = Setup();
        try
        {
            // No profile => LegacyDefault (per_load).
            await DeliveredChargeAsync(db, cid, cust, 100m);
            await DeliveredChargeAsync(db, cid, cust, 200m);

            var o = await svc.GenerateConsolidatedDraftsAsync(cid, cust, PStart, PEnd, null, BillingMode.Commit);

            Assert.True(o.Generated);
            Assert.Equal(2, o.DraftCount);   // one per job
        }
        finally { await CleanupAsync(db, cid); }
    }

    [Fact]
    public async Task Regenerate_Is_Idempotent()
    {
        var (db, svc, cid, cust) = Setup();
        try
        {
            await ProfileAsync(db, cid, "period");
            await DeliveredChargeAsync(db, cid, cust, 100m);
            await DeliveredChargeAsync(db, cid, cust, 200m);

            await svc.GenerateConsolidatedDraftsAsync(cid, cust, PStart, PEnd, null, BillingMode.Commit);
            var o = await svc.GenerateConsolidatedDraftsAsync(cid, cust, PStart, PEnd, null, BillingMode.Commit);

            var runs = await db.ScalarLongAsync("SELECT COUNT(*) FROM billing_consolidation_runs WHERE company_id=@c", c => c.Parameters.AddWithValue("@c", cid));
            var drafts = await db.ScalarLongAsync("SELECT COUNT(*) FROM invoice_drafts WHERE company_id=@c AND source='consolidation'", c => c.Parameters.AddWithValue("@c", cid));
            Assert.Equal(1, runs);
            Assert.Equal(1, drafts);
            Assert.True(o.Generated);
        }
        finally { await CleanupAsync(db, cid); }
    }

    [Fact]
    public async Task No_Billable_Charges_Is_FailClosed()
    {
        var (db, svc, cid, cust) = Setup();
        try
        {
            var o = await svc.GenerateConsolidatedDraftsAsync(cid, cust, PStart, PEnd, null, BillingMode.Commit);
            Assert.False(o.Generated);
            Assert.Equal("no_billable_charges", o.Reason);
        }
        finally { await CleanupAsync(db, cid); }
    }

    [Fact]
    public async Task Charge_Already_On_A_Draft_Line_Is_Excluded()
    {
        var (db, svc, cid, cust) = Setup();
        try
        {
            await ProfileAsync(db, cid, "period");
            var (jobId, chargeId) = await DeliveredChargeAsync(db, cid, cust, 100m);
            await DeliveredChargeAsync(db, cid, cust, 200m);
            // Put chargeId on a pre-existing draft line (billed elsewhere).
            var draft = (Guid)(await db.QuerySingleAsync(
                "INSERT INTO invoice_drafts (company_id, customer_id, invoice_draft_no, status, currency, subtotal, tax_total, total) VALUES (@c,@cust,'PRE',@s,'USD',100,0,100) RETURNING id",
                c => { c.Parameters.AddWithValue("@c", cid); c.Parameters.AddWithValue("@cust", cust); c.Parameters.AddWithValue("@s", "draft"); }))!["id"]!;
            await db.ExecuteAsync("INSERT INTO invoice_draft_lines (id, company_id, invoice_draft_id, job_charge_id, line_no, description, amount) VALUES (gen_random_uuid(),@c,@d,@jc,1,'x',100)",
                c => { c.Parameters.AddWithValue("@c", cid); c.Parameters.AddWithValue("@d", draft); c.Parameters.AddWithValue("@jc", chargeId); });

            var o = await svc.GenerateConsolidatedDraftsAsync(cid, cust, PStart, PEnd, null, BillingMode.Commit);

            Assert.True(o.Generated);
            Assert.Equal(200m, o.Subtotal);   // only the un-billed 200 charge, the 100 was excluded
        }
        finally { await CleanupAsync(db, cid); }
    }

    // ── helpers ──

    private static (Database, BillingConsolidationService, long, long) Setup()
    {
        var db = new Database(new ConfigurationBuilder().AddInMemoryCollection(
            new Dictionary<string, string?> { ["ConnectionStrings:DefaultConnection"] = TestDb.ConnectionString }).Build());
        var cid = db.InsertAsync("INSERT INTO companies (company_code, name, industry) VALUES (@code,'Bill Co','logistics') RETURNING id",
            c => c.Parameters.AddWithValue("@code", $"BC-{Guid.NewGuid():N}".Substring(0, 16))).GetAwaiter().GetResult();
        var cust = db.InsertAsync("INSERT INTO customers (company_id, customer_code, name) VALUES (@c,@code,'Buyer') RETURNING id",
            c => { c.Parameters.AddWithValue("@c", cid); c.Parameters.AddWithValue("@code", $"CU-{cid}"); }).GetAwaiter().GetResult();
        return (db, new BillingConsolidationService(db), cid, cust);
    }

    private static async Task ProfileAsync(Database db, long cid, string consolidation) =>
        await db.ExecuteAsync(
            "INSERT INTO billing_profiles (company_id, profile_code, profile_name, scope_type, consolidation, currency, effective_date, status) VALUES (@c,@code,'P','tenant',@con,'USD', DATE '2025-01-01','active')",
            c => { c.Parameters.AddWithValue("@c", cid); c.Parameters.AddWithValue("@code", $"BP-{cid}-{consolidation}"); c.Parameters.AddWithValue("@con", consolidation); });

    private static async Task<(long jobId, long chargeId)> DeliveredChargeAsync(Database db, long cid, long cust, decimal amount)
    {
        var jobId = await db.InsertAsync(
            "INSERT INTO jobs (company_id, job_code, job_type, customer_id, status) VALUES (@c,@code,'freight',@cust,'delivered') RETURNING id",
            c => { c.Parameters.AddWithValue("@c", cid); c.Parameters.AddWithValue("@code", $"J-{cid}-{Guid.NewGuid():N}".Substring(0, 24)); c.Parameters.AddWithValue("@cust", cust); });
        await db.ExecuteAsync(
            "INSERT INTO dispatch_assignments (company_id, job_id, assignment_status, status, actual_delivery_at) VALUES (@c,@j,'delivered','Delivered', TIMESTAMPTZ '2026-06-15T12:00:00Z')",
            c => { c.Parameters.AddWithValue("@c", cid); c.Parameters.AddWithValue("@j", jobId); });
        var chargeId = await db.InsertAsync(
            "INSERT INTO job_charges (company_id, job_id, charge_code, charge_name, charge_type, quantity, unit_rate, amount, currency, status) VALUES (@c,@j,'BASE','Base','base',1,@amt,@amt,'USD','approved') RETURNING id",
            c => { c.Parameters.AddWithValue("@c", cid); c.Parameters.AddWithValue("@j", jobId); c.Parameters.AddWithValue("@amt", amount); });
        return (jobId, chargeId);
    }

    private static async Task CleanupAsync(Database db, long cid)
    {
        foreach (var t in new[] { "invoice_draft_lines", "invoice_drafts", "billing_consolidation_runs", "billing_profiles", "job_charges", "dispatch_assignments", "jobs", "customers" })
            await db.ExecuteAsync($"DELETE FROM {t} WHERE company_id=@c", c => c.Parameters.AddWithValue("@c", cid));
        await db.ExecuteAsync("DELETE FROM companies WHERE id=@c", c => c.Parameters.AddWithValue("@c", cid));
    }
}
