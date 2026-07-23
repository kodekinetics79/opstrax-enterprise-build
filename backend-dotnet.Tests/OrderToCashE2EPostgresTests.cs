using Microsoft.Extensions.Configuration;
using Opstrax.Api.Data;
using Opstrax.Api.Foundation;
using Opstrax.Api.Services;

namespace Opstrax.Tests;

// Cross-module END-TO-END: proves the financial platform composes as one chain, not just per-module.
// A delivered job flows: RATE -> ready-to-bill -> DRAFT (TAX applied) -> approve -> ISSUE ->
// RECOGNIZE revenue -> SETTLE driver pay. Every ADR-007/008 money module participates in a single test.
[Trait("Category", "Integration")]
public class OrderToCashE2EPostgresTests
{
    [Fact]
    public async Task Delivered_Job_Flows_Rate_Bill_Tax_Issue_Recognize_Settle()
    {
        var db = Db();
        var corr = new InMemoryCorrelationContext("e2e-corr", "e2e-cause", "e2e-req", "1", ActorTypes.TenantUser, "42");
        var spine = new BusinessSpineService(db);
        var tax = new TaxService(db);
        var rating = new RatingService(db, spine);
        var revenue = new RevenueReadinessService(db, new PostgresAiFoundationService(db, corr),
            new PostgresApprovalWorkflowService(db, corr), new PostgresIdempotencyService(db),
            new PostgresDomainEventPublisher(db, corr), corr, tax);
        var revrec = new RevenueRecognitionService(db);
        var settlement = new SettlementService(db);

        var cid = await Seed.CompanyAsync(db);
        try
        {
            // ── seed a delivered job with a per-mile contract, 100 mi, a driver ──
            var customerId = await Seed.CustomerAsync(db, cid);
            var contractId = await Seed.ContractAsync(db, cid, customerId);
            var rateCardId = await Seed.RateCardAsync(db, cid, contractId, perMile: 2.00m);
            var driverId = 7001L;
            var jobId = await Seed.JobAsync(db, cid, customerId, contractId, rateCardId);
            await Seed.TripAsync(db, cid, jobId, miles: 100m);
            await Seed.DeliveredAssignmentAsync(db, cid, jobId, driverId);

            // ── config: 15% VAT, a default rev-rec profile, and a driver pay agreement ──
            await Seed.PublishedVatAsync(db, cid, rate: 0.15m);
            await Seed.RevrecDefaultAsync(db, cid);
            await Seed.PayAgreementAsync(db, cid, perMile: 0.55m);

            // ── 1. RATE: per-mile charge = 100 * 2.00 = 200 ──
            var rated = await rating.RateJobAsync(cid, jobId, RateMode.Commit);
            Assert.True(rated.Priced);
            var charges = await db.ScalarDecimalAsync("SELECT COALESCE(SUM(amount),0) FROM job_charges WHERE company_id=@c AND job_id=@j AND source='rating'",
                c => { c.Parameters.AddWithValue("@c", cid); c.Parameters.AddWithValue("@j", jobId); });
            Assert.Equal(200m, charges);

            // ── 2. ready-to-bill, then 3. DRAFT with TAX applied (200 + 15% = 230) ──
            await revenue.MarkJobReadyToBillAsync(cid, jobId);
            var draftOutcome = await revenue.CreateInvoiceDraftFromJobAsync(cid, jobId, "e2e-draft");
            Assert.True(draftOutcome.Success);
            var draft = await revenue.GetInvoiceDraftAsync(cid, draftOutcome.Draft!.Id);
            Assert.Equal(30m, draft!.TaxTotal);
            Assert.Equal(230m, draft.Total);

            // ── 4. approve, 5. ISSUE ──
            var gate = await revenue.UpdateInvoiceDraftAsync(cid, draftOutcome.Draft.Id, "approved");
            new PostgresApprovalWorkflowService(db, corr).Decide(gate.ApprovalRequestId!.Value, "approver", "approved", "ok");
            var issue = await revenue.IssueInvoiceFromDraftAsync(cid, draftOutcome.Draft.Id, "e2e-issue");
            Assert.True(issue.Success);
            Assert.Equal(30m, issue.Invoice!.TaxTotal);
            Assert.Equal(230m, issue.Invoice.Total);

            // ── 6. RECOGNIZE revenue (full total at issue) ──
            var recog = await revrec.RecognizeInvoiceAsync(cid, issue.Invoice.Id, RecognitionMode.Commit);
            Assert.True(recog.Recognized);
            Assert.Equal(230m, recog.AmountFunctional);

            // ── 7. SETTLE driver pay (100 mi * 0.55 = 55) ──
            var stmt = await settlement.GenerateDriverStatementAsync(cid, driverId, new DateOnly(2026, 1, 1), new DateOnly(2026, 12, 31), SettlementMode.Commit);
            Assert.True(stmt.Generated);
            Assert.Equal(55m, stmt.Total);

            // ── whole-chain assertion: AR (revenue) and AP (driver pay) both derived from the same load ──
            var recognized = await db.ScalarDecimalAsync("SELECT COALESCE(SUM(amount_functional),0) FROM revenue_recognition_entries WHERE company_id=@c", c => c.Parameters.AddWithValue("@c", cid));
            var payable = await db.ScalarDecimalAsync("SELECT COALESCE(SUM(total),0) FROM settlement_statements WHERE company_id=@c", c => c.Parameters.AddWithValue("@c", cid));
            Assert.Equal(230m, recognized);
            Assert.Equal(55m, payable);
        }
        finally { await Seed.CleanupAsync(db, cid); }
    }

    private static Database Db() => new(new ConfigurationBuilder().AddInMemoryCollection(
        new Dictionary<string, string?> { ["ConnectionStrings:DefaultConnection"] = TestDb.ConnectionString }).Build());

    // Seed helpers kept in one place for readability of the flow above.
    private static class Seed
    {
        public static async Task<long> CompanyAsync(Database db) =>
            await db.InsertAsync("INSERT INTO companies (company_code, name, industry) VALUES (@c,'E2E Co','logistics') RETURNING id",
                c => c.Parameters.AddWithValue("@c", $"E2E-{Guid.NewGuid():N}".Substring(0, 16)));
        public static async Task<long> CustomerAsync(Database db, long cid) =>
            await db.InsertAsync("INSERT INTO customers (company_id, customer_code, name) VALUES (@c,@code,'Buyer') RETURNING id",
                c => { c.Parameters.AddWithValue("@c", cid); c.Parameters.AddWithValue("@code", $"CU-{cid}"); });
        public static async Task<long> ContractAsync(Database db, long cid, long customerId) =>
            await db.InsertAsync("INSERT INTO contracts (company_id, customer_id, contract_code, title, rate_type, status) VALUES (@c,@cust,@code,'E2E Contract','per_mile','active') RETURNING id",
                c => { c.Parameters.AddWithValue("@c", cid); c.Parameters.AddWithValue("@cust", customerId); c.Parameters.AddWithValue("@code", $"CT-{cid}"); });
        public static async Task<long> RateCardAsync(Database db, long cid, long contractId, decimal perMile) =>
            await db.InsertAsync(@"INSERT INTO rate_cards (company_id, contract_id, rate_card_code, rate_card_name, billing_basis, base_rate, currency, effective_date, status)
                VALUES (@c,@k,@code,'Card','Per Mile',@r,'USD',CURRENT_DATE,'Active') RETURNING id",
                c => { c.Parameters.AddWithValue("@c", cid); c.Parameters.AddWithValue("@k", contractId); c.Parameters.AddWithValue("@code", $"RC-{cid}"); c.Parameters.AddWithValue("@r", perMile); });
        public static async Task<long> JobAsync(Database db, long cid, long customerId, long contractId, long rateCardId) =>
            await db.InsertAsync("INSERT INTO jobs (company_id, job_code, job_type, customer_id, contract_id, rate_card_id, status) VALUES (@c,@code,'freight',@cust,@k,@rc,'delivered') RETURNING id",
                c => { c.Parameters.AddWithValue("@c", cid); c.Parameters.AddWithValue("@code", $"J-{cid}"); c.Parameters.AddWithValue("@cust", customerId); c.Parameters.AddWithValue("@k", contractId); c.Parameters.AddWithValue("@rc", rateCardId); });
        public static async Task TripAsync(Database db, long cid, long jobId, decimal miles) =>
            await db.ExecuteAsync("INSERT INTO trips (company_id, job_id, actual_distance_miles) VALUES (@c,@j,@m)",
                c => { c.Parameters.AddWithValue("@c", cid); c.Parameters.AddWithValue("@j", jobId); c.Parameters.AddWithValue("@m", miles); });
        public static async Task DeliveredAssignmentAsync(Database db, long cid, long jobId, long driverId) =>
            await db.ExecuteAsync("INSERT INTO dispatch_assignments (company_id, job_id, driver_id, assignment_status, status, actual_delivery_at) VALUES (@c,@j,@d,'delivered','Delivered', TIMESTAMPTZ '2026-06-15T12:00:00Z')",
                c => { c.Parameters.AddWithValue("@c", cid); c.Parameters.AddWithValue("@j", jobId); c.Parameters.AddWithValue("@d", driverId); });
        public static async Task PublishedVatAsync(Database db, long cid, decimal rate)
        {
            var p = await db.InsertAsync(@"INSERT INTO tax_profiles (company_id, profile_code, profile_name, regime, price_inclusive, currency, effective_date, status, author_user_id, published_by_user_id, published_at)
                VALUES (@c,'E2E-TAX','P','vat',FALSE,NULL,DATE '2025-01-01','published',1,2,NOW()) RETURNING id", c => c.Parameters.AddWithValue("@c", cid));
            await db.ExecuteAsync("INSERT INTO tax_rules (company_id, tax_profile_id, tax_code, tax_category, rate, taxable, priority) VALUES (@c,@p,'STANDARD','S',@r,TRUE,0)",
                c => { c.Parameters.AddWithValue("@c", cid); c.Parameters.AddWithValue("@p", p); c.Parameters.AddWithValue("@r", rate); });
            await db.ExecuteAsync("INSERT INTO seller_tax_registration (company_id, jurisdiction, regime, tax_registration_no, effective_date) VALUES (@c,'XX','vat','TRN', DATE '2025-01-01')",
                c => c.Parameters.AddWithValue("@c", cid));
        }
        public static async Task RevrecDefaultAsync(Database db, long cid) =>
            await db.ExecuteAsync(@"INSERT INTO revrec_profiles (company_id, profile_code, profile_name, method, trigger, recognize_base, functional_currency, status, is_default)
                VALUES (@c,'E2E-RR','P','accrual','on_invoice','total','USD','published',TRUE)", c => c.Parameters.AddWithValue("@c", cid));
        public static async Task PayAgreementAsync(Database db, long cid, decimal perMile) =>
            await db.ExecuteAsync(@"INSERT INTO pay_agreements (company_id, agreement_code, agreement_name, payee_type, payee_id, basis, rate, effective_date, status)
                VALUES (@c,'E2E-PA','Driver','driver',NULL,'per_mile',@r,DATE '2025-01-01','active')", c => { c.Parameters.AddWithValue("@c", cid); c.Parameters.AddWithValue("@r", perMile); });

        public static async Task CleanupAsync(Database db, long cid)
        {
            foreach (var t in new[] { "revenue_recognition_entries", "revrec_fiscal_periods", "revrec_profiles",
                "settlement_lines", "settlement_statements", "pay_agreements",
                "issued_invoice_tax_lines", "invoice_tax_lines", "issued_invoice_lines", "issued_invoices",
                "invoice_draft_lines", "invoice_drafts", "tax_rules", "tax_profiles", "seller_tax_registration",
                "job_charges", "dispatch_assignments", "trips", "jobs", "rate_cards", "contracts", "customers" })
                await db.ExecuteAsync($"DELETE FROM {t} WHERE company_id=@c", c => c.Parameters.AddWithValue("@c", cid));
            await db.ExecuteAsync("DELETE FROM outbox_messages WHERE tenant_id=@c", c => c.Parameters.AddWithValue("@c", cid));
            await db.ExecuteAsync("DELETE FROM companies WHERE id=@c", c => c.Parameters.AddWithValue("@c", cid));
        }
    }
}
