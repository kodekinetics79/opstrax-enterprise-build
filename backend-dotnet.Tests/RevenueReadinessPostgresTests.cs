using System.Reflection;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Opstrax.Api.Controllers;
using Opstrax.Api.Data;
using Opstrax.Api.Foundation;
using Opstrax.Api.Services;

namespace Opstrax.Tests;

public class RevenueReadinessPostgresTests
{
    private static readonly string LocalConnectionString = TestDb.ConnectionString;

    [Fact]
    public void Stage7A_SchemaContract_File_Exists_And_Contains_RevenueDraftTables()
    {
        var contract = ReadArtifact("database/migrations/2026_06_28_stage7a_revenue_readiness_schema_contract.sql");
        Assert.Contains("CREATE TABLE IF NOT EXISTS invoice_drafts", contract, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("CREATE TABLE IF NOT EXISTS invoice_draft_lines", contract, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("uq_invoice_drafts_company_invoice_no", contract, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Issue_With_Published_Tax_Profile_Taxes_Invoice_And_Snapshots_Breakdown()
    {
        var db = CreateDatabase();
        await EnsureSchemasAsync(db);
        var companyId = await SeedCompanyAsync(db);
        var customerId = await SeedCustomerAsync(db, companyId, "customer-tax");
        var contractId = await SeedContractAsync(db, companyId, customerId, "contract-tax");
        var spine = new BusinessSpineService(db);
        var rateCard = await spine.CreateRateCardAsync(companyId, "RC-TAX-1", "Tax rate card", customerId, contractId,
            "Per Mile", "Metro", "North", "South", "Truck", "USD", 3.25m, 150m, 6.5m, "Base",
            DateOnly.FromDateTime(DateTime.UtcNow.Date), null, "Active", "corr-tax", "cause-tax", "Tax rate card");
        var jobId = await SeedJobAsync(db, companyId, customerId, contractId, rateCard.Id, "Completed", "JOB-TAX-1");
        await spine.CreateJobChargeAsync(companyId, jobId, null, rateCard.Id, "BASE", "Base charge", "base", "Line", 1m, 1000m, 1000m, "USD", "approved");

        // Publish a 15% VAT profile with a catch-all standard rule + seller registration.
        var profileId = await db.InsertAsync(
            @"INSERT INTO tax_profiles (company_id, profile_code, profile_name, regime, price_inclusive, currency, effective_date, status, author_user_id, published_by_user_id, published_at)
              VALUES (@c, 'TP-ISSUE', 'P', 'vat', FALSE, NULL, DATE '2025-01-01', 'published', 1, 2, NOW()) RETURNING id",
            c => c.Parameters.AddWithValue("@c", companyId));
        await db.ExecuteAsync("INSERT INTO tax_rules (company_id, tax_profile_id, tax_code, tax_category, rate, taxable, priority) VALUES (@c,@p,'STANDARD','S',0.15,TRUE,0)",
            c => { c.Parameters.AddWithValue("@c", companyId); c.Parameters.AddWithValue("@p", profileId); });
        await db.ExecuteAsync("INSERT INTO seller_tax_registration (company_id, jurisdiction, regime, tax_registration_no, effective_date) VALUES (@c,'XX','vat','TRN', DATE '2025-01-01')",
            c => c.Parameters.AddWithValue("@c", companyId));

        var service = CreateRevenueService(db);
        var draftOutcome = await service.CreateInvoiceDraftFromJobAsync(companyId, jobId, "tax-issue-draft");
        Assert.True(draftOutcome.Success);
        // Draft already carries the computed tax (1000 * 0.15 = 150).
        var draft = await service.GetInvoiceDraftAsync(companyId, draftOutcome.Draft!.Id);
        Assert.Equal(150m, draft!.TaxTotal);
        Assert.Equal(1150m, draft.Total);

        var gate = await service.UpdateInvoiceDraftAsync(companyId, draftOutcome.Draft.Id, "approved");
        var approval = new PostgresApprovalWorkflowService(db, new InMemoryCorrelationContext("corr-ti", "cause-ti", "req-ti", companyId.ToString(), ActorTypes.TenantUser, "42"));
        approval.Decide(gate.ApprovalRequestId!.Value, "approver-ti", "approved", "ok");

        var issue = await service.IssueInvoiceFromDraftAsync(companyId, draftOutcome.Draft.Id, "tax-issue");
        Assert.True(issue.Success);
        // Issued invoice carries the fresh taxed figures.
        Assert.Equal(150m, issue.Invoice!.TaxTotal);
        Assert.Equal(1150m, issue.Invoice.Total);

        // Immutable tax snapshot exists and foots to the header tax_total.
        var snapSum = await db.ScalarDecimalAsync(
            "SELECT COALESCE(SUM(tax_amount),0) FROM issued_invoice_tax_lines WHERE company_id=@c AND issued_invoice_id=@i",
            c => { c.Parameters.AddWithValue("@c", companyId); c.Parameters.AddWithValue("@i", issue.Invoice.Id); });
        Assert.Equal(150m, snapSum);
    }

    [Fact]
    public async Task MarkReadyToBill_WithCharges_Succeeds_And_Writes_Event()
    {
        var db = CreateDatabase();
        await EnsureSchemasAsync(db);
        var companyId = await SeedCompanyAsync(db);
        var customerId = await SeedCustomerAsync(db, companyId, "customer-ready");
        var contractId = await SeedContractAsync(db, companyId, customerId, "contract-ready");
        var spine = new BusinessSpineService(db);
        var rateCard = await spine.CreateRateCardAsync(
            companyId,
            "RC-READY-1",
            "Ready to bill rate card",
            customerId,
            contractId,
            "Per Mile",
            "Metro",
            "North",
            "South",
            "Truck",
            "USD",
            3.25m,
            150m,
            6.5m,
            "Base",
            DateOnly.FromDateTime(DateTime.UtcNow.Date),
            null,
            "Active",
            "corr-ready",
            "cause-ready",
            "Ready rate card");
        var jobId = await SeedJobAsync(db, companyId, customerId, contractId, rateCard.Id, "Completed", "JOB-READY-1");
        await spine.CreateJobChargeAsync(companyId, jobId, null, rateCard.Id, "BASE", "Base charge", "base", "Completed job charge", 1m, 125m, 125m, "USD", "approved");

        var service = CreateRevenueService(db);
        var outcome = await service.MarkJobReadyToBillAsync(companyId, jobId);

        Assert.True(outcome.Success);
        Assert.Equal("ready_to_bill", outcome.JobStatus);
        Assert.Equal(1, await db.ScalarLongAsync("SELECT COUNT(*) FROM jobs WHERE company_id=@companyId AND id=@jobId AND status='ready_to_bill'", c =>
        {
            c.Parameters.AddWithValue("@companyId", companyId);
            c.Parameters.AddWithValue("@jobId", jobId);
        }));
        Assert.Equal(1, await db.ScalarLongAsync("SELECT COUNT(*) FROM domain_events WHERE tenant_id=@tenantId AND event_type='job.ready_to_bill'", c => c.Parameters.AddWithValue("@tenantId", companyId)));
        Assert.Equal(1, await db.ScalarLongAsync("SELECT COUNT(*) FROM outbox_messages WHERE tenant_id=@tenantId AND event_type='job.ready_to_bill' AND status='pending'", c => c.Parameters.AddWithValue("@tenantId", companyId)));

        await CleanupTenantAsync(db, companyId);
    }

    [Fact]
    public async Task MarkReadyToBill_WithoutCharges_CreatesLeakageRecommendation()
    {
        var db = CreateDatabase();
        await EnsureSchemasAsync(db);
        var companyId = await SeedCompanyAsync(db);
        var customerId = await SeedCustomerAsync(db, companyId, "customer-no-charge");
        var contractId = await SeedContractAsync(db, companyId, customerId, "contract-no-charge");
        var spine = new BusinessSpineService(db);
        var rateCard = await spine.CreateRateCardAsync(
            companyId,
            "RC-NOCHG-1",
            "No charge rate card",
            customerId,
            contractId,
            "Per Mile",
            "Metro",
            "North",
            "South",
            "Truck",
            "USD",
            2.10m,
            100m,
            5m,
            "Base",
            DateOnly.FromDateTime(DateTime.UtcNow.Date),
            null,
            "Active");
        var jobId = await SeedJobAsync(db, companyId, customerId, contractId, rateCard.Id, "Completed", "JOB-NO-CHARGE-1");

        var service = CreateRevenueService(db);
        var outcome = await service.MarkJobReadyToBillAsync(companyId, jobId);

        Assert.False(outcome.Success);
        Assert.True(outcome.RecommendationCreated);
        Assert.Equal(1, await db.ScalarLongAsync("SELECT COUNT(*) FROM ai_recommendations WHERE tenant_id=@tenantId AND recommendation_type='completed_job_missing_charges' AND status='active'", c =>
        {
            c.Parameters.AddWithValue("@tenantId", companyId);
        }));
        Assert.Equal(1, await db.ScalarLongAsync("SELECT COUNT(*) FROM ai_action_requests WHERE tenant_id=@tenantId AND action_key='draft_missing_charge_review' AND status='approval_required'", c =>
        {
            c.Parameters.AddWithValue("@tenantId", companyId);
        }));

        await CleanupTenantAsync(db, companyId);
    }

    [Fact]
    public async Task InvoiceDraft_Create_CopiesCharges_And_IsIdempotent()
    {
        var db = CreateDatabase();
        await EnsureSchemasAsync(db);
        var companyId = await SeedCompanyAsync(db);
        var customerId = await SeedCustomerAsync(db, companyId, "customer-draft");
        var contractId = await SeedContractAsync(db, companyId, customerId, "contract-draft");
        var spine = new BusinessSpineService(db);
        var rateCard = await spine.CreateRateCardAsync(
            companyId,
            "RC-DRAFT-1",
            "Draft rate card",
            customerId,
            contractId,
            "Per Mile",
            "Metro",
            "North",
            "South",
            "Truck",
            "USD",
            4.55m,
            120m,
            4.5m,
            "Base",
            DateOnly.FromDateTime(DateTime.UtcNow.Date),
            null,
            "Active");
        var jobId = await SeedJobAsync(db, companyId, customerId, contractId, rateCard.Id, "Completed", "JOB-DRAFT-1");
        await spine.CreateJobChargeAsync(companyId, jobId, null, rateCard.Id, "BASE", "Base charge", "base", "Base line", 1m, 100m, 100m, "USD", "approved");
        await spine.CreateJobChargeAsync(companyId, jobId, null, rateCard.Id, "FUEL", "Fuel surcharge", "surcharge", "Fuel line", 1m, 25.5m, 25.5m, "USD", "approved");

        var service = CreateRevenueService(db);
        var first = await service.CreateInvoiceDraftFromJobAsync(companyId, jobId, "draft-idem-1");
        var second = await service.CreateInvoiceDraftFromJobAsync(companyId, jobId, "draft-idem-1");

        Assert.True(first.Success);
        Assert.NotNull(first.Draft);
        Assert.NotNull(first.Draft!.Lines);
        Assert.True(second.Success);
        Assert.True(second.Replay);
        Assert.Equal(first.Draft!.Id, second.Draft!.Id);
        Assert.Equal(2, first.Draft.Lines!.Count);
        Assert.Equal(1, await db.ScalarLongAsync("SELECT COUNT(*) FROM invoice_drafts WHERE company_id=@companyId AND job_id=@jobId", c =>
        {
            c.Parameters.AddWithValue("@companyId", companyId);
            c.Parameters.AddWithValue("@jobId", jobId);
        }));
        Assert.Equal(2, await db.ScalarLongAsync("SELECT COUNT(*) FROM invoice_draft_lines WHERE company_id=@companyId AND invoice_draft_id=@draftId", c =>
        {
            c.Parameters.AddWithValue("@companyId", companyId);
            c.Parameters.AddWithValue("@draftId", first.Draft!.Id);
        }));
        Assert.Equal(125.5m, first.Draft!.Total);
        Assert.Equal(1, await db.ScalarLongAsync("SELECT COUNT(*) FROM domain_events WHERE tenant_id=@tenantId AND event_type='invoice_draft.created'", c => c.Parameters.AddWithValue("@tenantId", companyId)));
        Assert.Equal(1, await db.ScalarLongAsync("SELECT COUNT(*) FROM outbox_messages WHERE tenant_id=@tenantId AND event_type='invoice_draft.created'", c => c.Parameters.AddWithValue("@tenantId", companyId)));

        await CleanupTenantAsync(db, companyId);
    }

    [Fact]
    public async Task RevenueSummary_And_CustomerSummary_AreTenantScoped_And_HideMargin()
    {
        var db = CreateDatabase();
        await EnsureSchemasAsync(db);
        var companyA = await SeedCompanyAsync(db, "stage7-a");
        var companyB = await SeedCompanyAsync(db, "stage7-b");

        var customerA = await SeedCustomerAsync(db, companyA, "customer-a");
        var customerB = await SeedCustomerAsync(db, companyB, "customer-b");
        var contractA = await SeedContractAsync(db, companyA, customerA, "contract-a");
        var contractB = await SeedContractAsync(db, companyB, customerB, "contract-b");
        var spine = new BusinessSpineService(db);
        var rateCardA = await spine.CreateRateCardAsync(companyA, "RC-A-1", "Rate A", customerA, contractA, "Per Mile", "Metro", "N", "S", "Truck", "USD", 3m, 90m, 4m, "Base", DateOnly.FromDateTime(DateTime.UtcNow.Date), null, "Active");
        var rateCardB = await spine.CreateRateCardAsync(companyB, "RC-B-1", "Rate B", customerB, contractB, "Per Mile", "Metro", "N", "S", "Truck", "USD", 4m, 100m, 5m, "Base", DateOnly.FromDateTime(DateTime.UtcNow.Date), null, "Active");
        var jobA = await SeedJobAsync(db, companyA, customerA, contractA, rateCardA.Id, "ready_to_bill", "JOB-A-1");
        var jobB = await SeedJobAsync(db, companyB, customerB, contractB, rateCardB.Id, "ready_to_bill", "JOB-B-1");
        await spine.CreateJobChargeAsync(companyA, jobA, null, rateCardA.Id, "BASE", "Base charge", "base", "Charge A", 1m, 60m, 60m, "USD", "approved");
        await spine.CreateJobChargeAsync(companyB, jobB, null, rateCardB.Id, "BASE", "Base charge", "base", "Charge B", 1m, 75m, 75m, "USD", "approved");

        var service = CreateRevenueService(db);
        var draft = await service.CreateInvoiceDraftFromJobAsync(companyA, jobA, "rev-summary");
        Assert.True(draft.Success);
        var revenue = await service.GetRevenueSummaryAsync(companyA);
        var customerSummary = await service.GetCustomerSummaryAsync(companyA, customerA);
        var wrongTenantSummary = await service.GetCustomerSummaryAsync(companyA, customerB);

        Assert.NotNull(customerSummary);
        Assert.Null(wrongTenantSummary);
        Assert.Equal(companyA, revenue.CompanyId);
        Assert.Equal(1, revenue.ReadyToBillJobsCount);
        Assert.Equal(60m, revenue.TotalDraftCharges);
        Assert.Equal(1, customerSummary!.ReadyToBillJobsCount);
        Assert.DoesNotContain(typeof(CustomerSummaryRecord).GetProperties(), p => p.Name.Contains("margin", StringComparison.OrdinalIgnoreCase) || p.Name.Contains("cost", StringComparison.OrdinalIgnoreCase));

        await CleanupTenantAsync(db, companyA);
        await CleanupTenantAsync(db, companyB);
    }

    [Fact]
    public async Task CrossTenant_JobOperations_AreRejected()
    {
        var db = CreateDatabase();
        await EnsureSchemasAsync(db);
        var companyA = await SeedCompanyAsync(db, "stage7-cross-a");
        var companyB = await SeedCompanyAsync(db, "stage7-cross-b");

        var customerB = await SeedCustomerAsync(db, companyB, "customer-cross-b");
        var contractB = await SeedContractAsync(db, companyB, customerB, "contract-cross-b");
        var spine = new BusinessSpineService(db);
        var rateCardB = await spine.CreateRateCardAsync(companyB, "RC-CROSS-1", "Cross rate", customerB, contractB, "Per Mile", "Metro", "N", "S", "Truck", "USD", 4m, 100m, 5m, "Base", DateOnly.FromDateTime(DateTime.UtcNow.Date), null, "Active");
        var jobB = await SeedJobAsync(db, companyB, customerB, contractB, rateCardB.Id, "Completed", "JOB-CROSS-1");
        await spine.CreateJobChargeAsync(companyB, jobB, null, rateCardB.Id, "BASE", "Base charge", "base", "Charge B", 1m, 75m, 75m, "USD", "approved");

        var service = CreateRevenueService(db);
        var readyOutcome = await service.MarkJobReadyToBillAsync(companyA, jobB);
        var draftOutcome = await service.CreateInvoiceDraftFromJobAsync(companyA, jobB, "cross-tenant");

        Assert.False(readyOutcome.Success);
        Assert.Equal("Job not found", readyOutcome.Message);
        Assert.False(draftOutcome.Success);
        Assert.Equal("Job not found", draftOutcome.Message);

        await CleanupTenantAsync(db, companyA);
        await CleanupTenantAsync(db, companyB);
    }

    [Fact]
    public async Task LeakSignals_Create_MissingPricing_And_ReadyToBillWithoutInvoiceDraft()
    {
        var db = CreateDatabase();
        await EnsureSchemasAsync(db);
        var companyId = await SeedCompanyAsync(db);
        var customerId = await SeedCustomerAsync(db, companyId, "customer-leak");
        var contractId = await SeedContractAsync(db, companyId, customerId, "contract-leak");
        var spine = new BusinessSpineService(db);
        var rateCard = await spine.CreateRateCardAsync(companyId, "RC-LEAK-1", "Leak rate", customerId, contractId, "Per Mile", "Metro", "N", "S", "Truck", "USD", 3m, 100m, 4m, "Base", DateOnly.FromDateTime(DateTime.UtcNow.Date), null, "Active");
        var jobWithPricingGap = await SeedJobAsync(db, companyId, customerId, null, null, "Completed", "JOB-LEAK-1");
        await spine.CreateJobChargeAsync(companyId, jobWithPricingGap, null, rateCard.Id, "BASE", "Base charge", "base", "Charge missing pricing", 1m, 42m, 42m, "USD", "approved");
        var readyJob = await SeedJobAsync(db, companyId, customerId, contractId, rateCard.Id, "ready_to_bill", "JOB-LEAK-2");
        await spine.CreateJobChargeAsync(companyId, readyJob, null, rateCard.Id, "BASE", "Base charge", "base", "Charge ready", 1m, 33m, 33m, "USD", "approved");

        var service = CreateRevenueService(db);
        _ = await service.GetRevenueSummaryAsync(companyId);

        Assert.Equal(1, await db.ScalarLongAsync("SELECT COUNT(*) FROM ai_recommendations WHERE tenant_id=@tenantId AND recommendation_type='job_without_contract_or_rate_card'", c => c.Parameters.AddWithValue("@tenantId", companyId)));
        Assert.Equal(1, await db.ScalarLongAsync("SELECT COUNT(*) FROM ai_recommendations WHERE tenant_id=@tenantId AND recommendation_type='ready_to_bill_job_without_invoice_draft'", c => c.Parameters.AddWithValue("@tenantId", companyId)));
        Assert.Equal(2, await db.ScalarLongAsync("SELECT COUNT(*) FROM ai_recommendations WHERE tenant_id=@tenantId AND recommendation_type='approved_charges_not_drafted'", c => c.Parameters.AddWithValue("@tenantId", companyId)));

        await CleanupTenantAsync(db, companyId);
    }

    [Fact]
    public async Task ActiveRateCardChange_RequiresApproval()
    {
        var db = CreateDatabase();
        await EnsureSchemasAsync(db);
        var companyId = await SeedCompanyAsync(db);
        var customerId = await SeedCustomerAsync(db, companyId, "customer-approval");
        var contractId = await SeedContractAsync(db, companyId, customerId, "contract-approval");
        var spine = new BusinessSpineService(db);
        var rateCard = await spine.CreateRateCardAsync(companyId, "RC-APPROVAL-1", "Approval rate", customerId, contractId, "Per Mile", "Metro", "N", "S", "Truck", "USD", 2.5m, 100m, 4m, "Base", DateOnly.FromDateTime(DateTime.UtcNow.Date), null, "Active");

        var services = new ServiceCollection()
            .AddSingleton<IFeatureAccessService, PassthroughFeatureAccessService>()
            .AddSingleton<IAuthorizationDecisionService, AuthorizationDecisionService>()
            .AddSingleton<IAuditLogService, InMemoryAuditLogService>()
            .AddSingleton<ICorrelationContext>(new InMemoryCorrelationContext("corr-rate", "cause-rate", "req-rate", companyId.ToString(), "tenant_user", "42"))
            .BuildServiceProvider();

        var http = new DefaultHttpContext();
        http.RequestServices = services;
        http.Items[EndpointMappings.AuthCompanyIdItemKey] = companyId;
        http.Items[EndpointMappings.AuthRoleItemKey] = "Tenant Admin";
        http.Items[EndpointMappings.AuthUserIdItemKey] = "42";
        http.Items[EndpointMappings.AuthPermissionsItemKey] = new[] { "finance:manage" };

        var approval = new PostgresApprovalWorkflowService(db, new InMemoryCorrelationContext("corr-rate", "cause-rate", "req-rate", companyId.ToString(), "tenant_user", "42"));
        var events = new PostgresDomainEventPublisher(db, new InMemoryCorrelationContext("corr-rate", "cause-rate", "req-rate", companyId.ToString(), "tenant_user", "42"));
        var body = new Dictionary<string, object?>
        {
            ["baseRate"] = 3.75m,
            ["billingBasis"] = "Per Mile"
        };

        var method = typeof(BusinessSpineEndpoints).GetMethod("UpdateRateCard", BindingFlags.NonPublic | BindingFlags.Static);
        Assert.NotNull(method);
        var task = (Task<IResult>)method!.Invoke(null, [http, rateCard.Id, body, spine, approval, events, CancellationToken.None])!;
        var result = await task;

        Assert.Contains("Accepted", result.GetType().Name, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(1, await db.ScalarLongAsync("SELECT COUNT(*) FROM approval_requests WHERE tenant_id=@tenantId AND action_key='customer.contract.rate_change'", c =>
        {
            c.Parameters.AddWithValue("@tenantId", companyId);
        }));
        var storedRate = await spine.GetRateCardByIdAsync(companyId, rateCard.Id);
        Assert.NotNull(storedRate);
        Assert.Equal(2.5m, storedRate!.BaseRate);

        await CleanupTenantAsync(db, companyId);
    }

    [Fact]
    public async Task InvoiceDraft_Issue_RequiresApproval_Then_Issues_And_RecordsPayment()
    {
        var db = CreateDatabase();
        await EnsureSchemasAsync(db);
        var companyId = await SeedCompanyAsync(db);
        var customerId = await SeedCustomerAsync(db, companyId, "customer-stage8");
        var contractId = await SeedContractAsync(db, companyId, customerId, "contract-stage8");
        var spine = new BusinessSpineService(db);
        var rateCard = await spine.CreateRateCardAsync(
            companyId,
            "RC-STAGE8-1",
            "Stage 8 rate card",
            customerId,
            contractId,
            "Per Mile",
            "Metro",
            "North",
            "South",
            "Truck",
            "USD",
            5.25m,
            150m,
            5m,
            "Base",
            DateOnly.FromDateTime(DateTime.UtcNow.Date),
            null,
            "Active");
        var jobId = await SeedJobAsync(db, companyId, customerId, contractId, rateCard.Id, "Completed", "JOB-STAGE8-1");
        await spine.CreateJobChargeAsync(companyId, jobId, null, rateCard.Id, "BASE", "Base charge", "base", "Line 1", 1m, 100m, 100m, "USD", "approved");
        await spine.CreateJobChargeAsync(companyId, jobId, null, rateCard.Id, "FUEL", "Fuel surcharge", "surcharge", "Line 2", 1m, 35m, 35m, "USD", "approved");

        var service = CreateRevenueService(db);
        var draftOutcome = await service.CreateInvoiceDraftFromJobAsync(companyId, jobId, "stage8-draft");
        Assert.True(draftOutcome.Success);
        Assert.NotNull(draftOutcome.Draft);

        var approvalGate = await service.UpdateInvoiceDraftAsync(companyId, draftOutcome.Draft!.Id, "approved");
        Assert.True(approvalGate.ApprovalRequired);
        Assert.True(approvalGate.ApprovalRequestId.HasValue);

        var pendingDraft = await service.GetInvoiceDraftAsync(companyId, draftOutcome.Draft.Id);
        Assert.NotNull(pendingDraft);
        Assert.Equal("pending_review", pendingDraft!.Status);
        Assert.Equal(approvalGate.ApprovalRequestId, pendingDraft.ApprovalRequestId);

        var approval = new PostgresApprovalWorkflowService(db, new InMemoryCorrelationContext("corr-stage8", "cause-stage8", "req-stage8", companyId.ToString(), ActorTypes.TenantUser, "42"));
        var approvalDecision = approval.Decide(approvalGate.ApprovalRequestId!.Value, "approver-stage8", "approved", "approved for issue");
        Assert.Equal("approved", approvalDecision.Decision);

        var issueBlocked = await service.IssueInvoiceFromDraftAsync(companyId, draftOutcome.Draft.Id, "stage8-issue");
        Assert.True(issueBlocked.Success);
        Assert.NotNull(issueBlocked.Invoice);
        Assert.Equal("issued", issueBlocked.Invoice!.Status);
        Assert.Equal(2, issueBlocked.Invoice.Lines!.Count);

        var issueReplay = await service.IssueInvoiceFromDraftAsync(companyId, draftOutcome.Draft.Id, "stage8-issue");
        Assert.True(issueReplay.Success);
        Assert.True(issueReplay.Replay);
        Assert.Equal(issueBlocked.Invoice.Id, issueReplay.Invoice!.Id);

        var payment = await service.RecordInvoicePaymentAsync(companyId, issueBlocked.Invoice.Id, 135m, "USD", "PAY-STAGE8-1", "manual", "{\"channel\":\"check\"}");
        Assert.NotNull(payment);
        Assert.Equal("posted", payment!.Status);

        var issuedInvoice = await service.GetIssuedInvoiceAsync(companyId, issueBlocked.Invoice.Id);
        Assert.NotNull(issuedInvoice);
        Assert.Equal("paid", issuedInvoice!.PaymentStatus);
        Assert.Equal(135m, issuedInvoice.AmountPaid);
        Assert.Equal(0m, issuedInvoice.BalanceDue);

        var ar = await service.GetAccountsReceivableSummaryAsync(companyId);
        Assert.Equal(1, ar.IssuedInvoiceCount);
        Assert.Equal(0, ar.OpenInvoiceCount);
        Assert.Equal(0m, ar.OpenBalance);
        Assert.Equal(135m, ar.PaidBalance);

        Assert.Equal(1, await db.ScalarLongAsync("SELECT COUNT(*) FROM issued_invoices WHERE company_id=@companyId AND source_invoice_draft_id=@draftId AND status='paid'", c =>
        {
            c.Parameters.AddWithValue("@companyId", companyId);
            c.Parameters.AddWithValue("@draftId", draftOutcome.Draft!.Id);
        }));
        Assert.Equal(2, await db.ScalarLongAsync("SELECT COUNT(*) FROM issued_invoice_lines WHERE company_id=@companyId AND issued_invoice_id=@invoiceId", c =>
        {
            c.Parameters.AddWithValue("@companyId", companyId);
            c.Parameters.AddWithValue("@invoiceId", issueBlocked.Invoice.Id);
        }));
        Assert.Equal(1, await db.ScalarLongAsync("SELECT COUNT(*) FROM invoice_payments WHERE company_id=@companyId AND issued_invoice_id=@invoiceId", c =>
        {
            c.Parameters.AddWithValue("@companyId", companyId);
            c.Parameters.AddWithValue("@invoiceId", issueBlocked.Invoice.Id);
        }));
        Assert.Equal(1, await db.ScalarLongAsync("SELECT COUNT(*) FROM approval_requests WHERE tenant_id=@tenantId AND action_key='finance.invoice.issue'", c =>
        {
            c.Parameters.AddWithValue("@tenantId", companyId);
        }));
        Assert.Equal(1, await db.ScalarLongAsync("SELECT COUNT(*) FROM approval_decisions WHERE tenant_id=@tenantId AND approval_request_id=@approvalRequestId", c =>
        {
            c.Parameters.AddWithValue("@tenantId", companyId);
            c.Parameters.AddWithValue("@approvalRequestId", approvalGate.ApprovalRequestId!.Value);
        }));

        await CleanupTenantAsync(db, companyId);
    }

    [Fact]
    public async Task InvoiceIssue_WithoutApproval_ReturnsApprovalRequired()
    {
        var db = CreateDatabase();
        await EnsureSchemasAsync(db);
        var companyId = await SeedCompanyAsync(db);
        var customerId = await SeedCustomerAsync(db, companyId, "customer-stage8-gate");
        var contractId = await SeedContractAsync(db, companyId, customerId, "contract-stage8-gate");
        var spine = new BusinessSpineService(db);
        var rateCard = await spine.CreateRateCardAsync(
            companyId,
            "RC-STAGE8-GATE-1",
            "Stage 8 gating rate",
            customerId,
            contractId,
            "Per Mile",
            "Metro",
            "North",
            "South",
            "Truck",
            "USD",
            4.10m,
            120m,
            4m,
            "Base",
            DateOnly.FromDateTime(DateTime.UtcNow.Date),
            null,
            "Active");
        var jobId = await SeedJobAsync(db, companyId, customerId, contractId, rateCard.Id, "Completed", "JOB-STAGE8-GATE-1");
        await spine.CreateJobChargeAsync(companyId, jobId, null, rateCard.Id, "BASE", "Base charge", "base", "Line 1", 1m, 80m, 80m, "USD", "approved");

        var service = CreateRevenueService(db);
        var draftOutcome = await service.CreateInvoiceDraftFromJobAsync(companyId, jobId, "stage8-gate");
        Assert.True(draftOutcome.Success);

        var issueAttempt = await service.IssueInvoiceFromDraftAsync(companyId, draftOutcome.Draft!.Id, "stage8-gate-issue");
        Assert.False(issueAttempt.Success);
        Assert.True(issueAttempt.ApprovalRequired);
        Assert.NotNull(issueAttempt.ApprovalRequestId);
        Assert.Equal(0, await db.ScalarLongAsync("SELECT COUNT(*) FROM issued_invoices WHERE company_id=@companyId", c => c.Parameters.AddWithValue("@companyId", companyId)));

        await CleanupTenantAsync(db, companyId);
    }

    [Fact]
    public async Task ArAging_BucketsOutstandingInvoicesByAge_AndIsTenantScoped()
    {
        var db = CreateDatabase();
        await EnsureSchemasAsync(db);
        var companyId = await SeedCompanyAsync(db);
        var customerId = await SeedCustomerAsync(db, companyId, "customer-aging");

        // 5 outstanding invoices, one per aging bucket, with real amounts + due dates.
        await SeedIssuedInvoiceAsync(db, companyId, customerId, "INV-AGING-CUR", 1200.00m, 0m, 1200.00m, "unpaid", issuedDaysAgo: 5, dueInDays: 15);
        await SeedIssuedInvoiceAsync(db, companyId, customerId, "INV-AGING-B1", 800.50m, 0m, 800.50m, "unpaid", issuedDaysAgo: 40, dueInDays: -10);
        await SeedIssuedInvoiceAsync(db, companyId, customerId, "INV-AGING-B2", 2500.00m, 0m, 2500.00m, "unpaid", issuedDaysAgo: 75, dueInDays: -45);
        await SeedIssuedInvoiceAsync(db, companyId, customerId, "INV-AGING-B3", 1500.75m, 0m, 1500.75m, "unpaid", issuedDaysAgo: 105, dueInDays: -75);
        await SeedIssuedInvoiceAsync(db, companyId, customerId, "INV-AGING-B4", 3000.00m, 0m, 3000.00m, "unpaid", issuedDaysAgo: 150, dueInDays: -120);
        // Fully paid (0 balance) — must NOT appear in aging.
        await SeedIssuedInvoiceAsync(db, companyId, customerId, "INV-AGING-PAID", 999.99m, 999.99m, 0m, "paid", issuedDaysAgo: 60, dueInDays: -30, paidDaysAgo: 25);

        // Second tenant with a 90+ overdue invoice — must never leak into companyId's aging.
        var otherCompanyId = await SeedCompanyAsync(db);
        var otherCustomerId = await SeedCustomerAsync(db, otherCompanyId, "customer-aging-other");
        await SeedIssuedInvoiceAsync(db, otherCompanyId, otherCustomerId, "INV-AGING-OTHER", 7777.00m, 0m, 7777.00m, "unpaid", issuedDaysAgo: 150, dueInDays: -120);

        var service = CreateRevenueService(db);
        var aging = await service.GetAccountsReceivableAgingAsync(companyId);

        Assert.Equal(1200.00m, aging.Current);
        Assert.Equal(800.50m, aging.Days1To30);
        Assert.Equal(2500.00m, aging.Days31To60);
        Assert.Equal(1500.75m, aging.Days61To90);
        Assert.Equal(3000.00m, aging.Days90Plus);
        Assert.Equal(9001.25m, aging.TotalOutstanding); // paid invoice (0 balance) excluded

        // Tenant isolation: the other tenant's 7777 never appears.
        Assert.DoesNotContain(aging.Customers, c => c.CustomerId == otherCustomerId);
        Assert.All(aging.Customers, c => Assert.NotEqual(7777.00m, c.Days90Plus));
        var cust = Assert.Single(aging.Customers);
        Assert.Equal(customerId, cust.CustomerId);
        Assert.Equal(3000.00m, cust.Days90Plus);
        Assert.Equal(9001.25m, cust.TotalOutstanding);

        await CleanupTenantAsync(db, companyId);
        await CleanupTenantAsync(db, otherCompanyId);
    }

    [Fact]
    public async Task RevenueLeakage_DetectsNoChargeAndStaleDraft_NotCorrectlyBilled_AndIsIdempotent()
    {
        var db = CreateDatabase();
        await EnsureSchemasAsync(db);
        await new Batch5SchemaService(db).EnsureAsync(); // ensures cost_leakage_items exists
        var companyId = await SeedCompanyAsync(db);
        var customerId = await SeedCustomerAsync(db, companyId, "customer-leak");
        var contractId = await SeedContractAsync(db, companyId, customerId, "contract-leak");
        var spine = new BusinessSpineService(db);
        var rateCard = await spine.CreateRateCardAsync(
            companyId, "RC-LEAK-1", "Leak rate card", customerId, contractId,
            "Per Mile", "Metro", "North", "South", "Truck", "USD",
            4.10m, 250.00m, 5m, "Base",
            DateOnly.FromDateTime(DateTime.UtcNow.Date), null, "Active"); // minimum_charge 250.00

        // Job 1: completed, NO charge -> completed_job_no_charge (expected = min_charge 250.00).
        var job1 = await SeedJobAsync(db, companyId, customerId, contractId, rateCard.Id, "Completed", "JOB-LEAK-NOCHG");
        // Job 2: in progress, ONE draft charge 475.25 aged 10 days -> stale_draft_charge.
        var job2 = await SeedJobAsync(db, companyId, customerId, contractId, rateCard.Id, "In Progress", "JOB-LEAK-STALE");
        var draftCharge = await spine.CreateJobChargeAsync(companyId, job2, null, rateCard.Id, "BASE", "Base charge", "base", "Stale draft", 1m, 475.25m, 475.25m, "USD", "draft");
        await db.ExecuteAsync("UPDATE job_charges SET created_at = NOW() - INTERVAL '10 days' WHERE company_id=@c AND id=@id", c =>
        {
            c.Parameters.AddWithValue("@c", companyId);
            c.Parameters.AddWithValue("@id", draftCharge.Id);
        });
        // Job 3: completed, approved charge 620.00 (>= min) -> NO signal.
        var job3 = await SeedJobAsync(db, companyId, customerId, contractId, rateCard.Id, "Completed", "JOB-LEAK-OK");
        await spine.CreateJobChargeAsync(companyId, job3, null, rateCard.Id, "BASE", "Base charge", "base", "Correctly billed", 1m, 620.00m, 620.00m, "USD", "approved");

        var service = CreateRevenueService(db);
        var outcome = await service.DetectRevenueLeakageAsync(companyId, 7);

        Assert.Equal(2, outcome.SignalsCreated);
        Assert.Equal(0, outcome.SignalsAlreadyOpen);
        var byType = outcome.Signals.ToDictionary(s => s.SignalType);
        Assert.True(byType.ContainsKey("completed_job_no_charge"));
        Assert.True(byType.ContainsKey("stale_draft_charge"));
        Assert.False(byType.ContainsKey("below_contract_rate")); // job3 above min; job1 has no charge to compare
        Assert.Equal(250.00m, byType["completed_job_no_charge"].DetectedAmount); // rate card minimum_charge
        Assert.Equal(job1, byType["completed_job_no_charge"].EntityId);
        Assert.Equal(475.25m, byType["stale_draft_charge"].DetectedAmount); // draft charge amount
        Assert.Equal(draftCharge.Id, byType["stale_draft_charge"].EntityId);

        // Correctly-billed job produced nothing.
        Assert.DoesNotContain(outcome.Signals, s => s.EntityType == "job" && s.EntityId == job3);
        // Persisted to cost_leakage_items — exactly 2 open signals for this tenant.
        Assert.Equal(2, await db.ScalarLongAsync(
            "SELECT COUNT(*) FROM cost_leakage_items WHERE company_id=@c AND status='open' AND category IN ('completed_job_no_charge','stale_draft_charge')",
            c => c.Parameters.AddWithValue("@c", companyId)));

        // Idempotent: re-running creates no duplicates.
        var rerun = await service.DetectRevenueLeakageAsync(companyId, 7);
        Assert.Equal(0, rerun.SignalsCreated);
        Assert.Equal(2, rerun.SignalsAlreadyOpen);
        Assert.Equal(2, await db.ScalarLongAsync(
            "SELECT COUNT(*) FROM cost_leakage_items WHERE company_id=@c AND status='open'",
            c => c.Parameters.AddWithValue("@c", companyId)));

        await db.ExecuteAsync("DELETE FROM cost_leakage_items WHERE company_id=@c", c => c.Parameters.AddWithValue("@c", companyId));
        await CleanupTenantAsync(db, companyId);
    }

    [Fact]
    public async Task PaymentSummary_ComputesExactTotals_CollectedOutstandingAndDaysToPay()
    {
        var db = CreateDatabase();
        await EnsureSchemasAsync(db);
        var companyId = await SeedCompanyAsync(db);
        var cust1 = await SeedCustomerAsync(db, companyId, "customer-pay-1");
        var cust2 = await SeedCustomerAsync(db, companyId, "customer-pay-2");

        // Customer 1: two fully-paid invoices — paid_at/issued_at set in the SAME row so
        // the day difference is exact (10 and 14 days).
        var invA = await SeedIssuedInvoiceAsync(db, companyId, cust1, "INV-PAY-A", 1000.00m, 1000.00m, 0m, "paid", issuedDaysAgo: 40, dueInDays: -10, paidDaysAgo: 30); // 10 days to pay
        await SeedInvoicePaymentAsync(db, companyId, invA, 1000.00m, "PAY-A", receivedDaysAgo: 30);
        var invB = await SeedIssuedInvoiceAsync(db, companyId, cust1, "INV-PAY-B", 2000.00m, 2000.00m, 0m, "paid", issuedDaysAgo: 20, dueInDays: 10, paidDaysAgo: 6); // 14 days to pay
        await SeedInvoicePaymentAsync(db, companyId, invB, 2000.00m, "PAY-B", receivedDaysAgo: 6);

        // Customer 2: a partial payment + an unpaid invoice.
        var invC = await SeedIssuedInvoiceAsync(db, companyId, cust2, "INV-PAY-C", 1500.00m, 500.00m, 1000.00m, "partial", issuedDaysAgo: 15, dueInDays: 15);
        await SeedInvoicePaymentAsync(db, companyId, invC, 500.00m, "PAY-C", receivedDaysAgo: 5);
        await SeedIssuedInvoiceAsync(db, companyId, cust2, "INV-PAY-D", 800.00m, 0m, 800.00m, "unpaid", issuedDaysAgo: 10, dueInDays: 20);

        var service = CreateRevenueService(db);
        var from = DateTimeOffset.UtcNow.AddDays(-60);
        var to = DateTimeOffset.UtcNow.AddDays(1);
        var summary = await service.GetPaymentSummaryAsync(companyId, from, to);

        Assert.Equal(3500.00m, summary.TotalCollected);  // 1000 + 2000 + 500
        Assert.Equal(1800.00m, summary.TotalOutstanding); // 1000 (C) + 800 (D)
        Assert.Equal(3, summary.PaymentCount);
        Assert.Equal(2, summary.PaidInvoiceCount);        // A + B paid; C partial, D unpaid
        Assert.NotNull(summary.AverageDaysToPay);
        Assert.Equal(12.0m, Math.Round(summary.AverageDaysToPay!.Value, 2)); // avg(10, 14)

        var c1 = summary.Customers.Single(c => c.CustomerId == cust1);
        Assert.Equal(3000.00m, c1.TotalCollected);
        Assert.Equal(0m, c1.TotalOutstanding);
        Assert.Equal(2, c1.PaidInvoiceCount);
        var c2 = summary.Customers.Single(c => c.CustomerId == cust2);
        Assert.Equal(500.00m, c2.TotalCollected);
        Assert.Equal(1800.00m, c2.TotalOutstanding);
        Assert.Equal(0, c2.PaidInvoiceCount);

        await CleanupTenantAsync(db, companyId);
    }

    [Fact]
    public async Task FinanceExport_ArAgingCsv_ContainsOnlyLiveRows_NoPlaceholders()
    {
        var db = CreateDatabase();
        await EnsureSchemasAsync(db);
        var companyId = await SeedCompanyAsync(db);
        var customerId = await SeedCustomerAsync(db, companyId, "customer-export");
        await SeedIssuedInvoiceAsync(db, companyId, customerId, "INV-EXP-1", 1200.00m, 0m, 1200.00m, "unpaid", issuedDaysAgo: 40, dueInDays: -10);
        await SeedIssuedInvoiceAsync(db, companyId, customerId, "INV-EXP-2", 3000.00m, 0m, 3000.00m, "unpaid", issuedDaysAgo: 150, dueInDays: -120);

        var service = CreateRevenueService(db);
        var aging = await service.GetAccountsReceivableAgingAsync(companyId);
        Assert.Equal(4200.00m, aging.TotalOutstanding); // 1200 + 3000 from live rows

        var csv = RevenueReadinessEndpoints.BuildArAgingCsv(aging);
        var lines = csv.TrimEnd('\n', '\r').Split('\n').Select(l => l.TrimEnd('\r')).ToArray();

        // Header + exactly ONE live customer row + ONE company-total row — no placeholder/sample rows.
        Assert.Equal(3, lines.Length);
        Assert.StartsWith("customer_id,customer_name", lines[0]);
        Assert.Contains(customerId.ToString(), lines[1]);
        Assert.Contains(aging.TotalOutstanding.ToString(System.Globalization.CultureInfo.InvariantCulture), lines[1]);
        Assert.StartsWith("ALL,", lines[2]);
        Assert.Contains(aging.TotalOutstanding.ToString(System.Globalization.CultureInfo.InvariantCulture), lines[2]);

        await CleanupTenantAsync(db, companyId);
    }

    private static async Task<Guid> SeedIssuedInvoiceAsync(
        Database db, long companyId, long customerId, string invoiceNumber,
        decimal total, decimal amountPaid, decimal balanceDue, string paymentStatus,
        int issuedDaysAgo, int dueInDays, int? paidDaysAgo = null)
    {
        // issued_invoices.source_invoice_draft_id has a FK to invoice_drafts — seed a
        // minimal draft first so the invoice references a real row.
        var draftNo = $"DR-{invoiceNumber}";
        var draftRows = await db.QueryAsync(
            @"INSERT INTO invoice_drafts (company_id, customer_id, invoice_draft_no)
              VALUES (@companyId, @customerId, @draftNo) RETURNING id",
            c =>
            {
                c.Parameters.AddWithValue("@companyId", companyId);
                c.Parameters.AddWithValue("@customerId", customerId);
                c.Parameters.AddWithValue("@draftNo", draftNo);
            });
        var draftId = (Guid)draftRows[0]["id"]!;

        var rows = await db.QueryAsync(
            @"INSERT INTO issued_invoices
                (company_id, customer_id, source_invoice_draft_id, source_invoice_draft_no, invoice_number, status, currency,
                 subtotal, tax_total, total, amount_paid, balance_due, payment_status, issued_at, due_at, paid_at)
              VALUES (@companyId, @customerId, @draftId, @draftNo, @invoiceNumber, @status, 'USD',
                 @total, 0, @total, @amountPaid, @balanceDue, @paymentStatus,
                 NOW() - make_interval(days => @issuedDaysAgo),
                 NOW() + make_interval(days => @dueInDays),
                 CASE WHEN @paidDaysAgo < 0 THEN NULL ELSE NOW() - make_interval(days => @paidDaysAgo) END)
              RETURNING id",
            c =>
            {
                c.Parameters.AddWithValue("@companyId", companyId);
                c.Parameters.AddWithValue("@customerId", customerId);
                c.Parameters.AddWithValue("@draftId", draftId);
                c.Parameters.AddWithValue("@draftNo", draftNo);
                c.Parameters.AddWithValue("@invoiceNumber", invoiceNumber);
                c.Parameters.AddWithValue("@status", paymentStatus == "paid" ? "paid" : "issued");
                c.Parameters.AddWithValue("@total", total);
                c.Parameters.AddWithValue("@amountPaid", amountPaid);
                c.Parameters.AddWithValue("@balanceDue", balanceDue);
                c.Parameters.AddWithValue("@paymentStatus", paymentStatus);
                c.Parameters.AddWithValue("@issuedDaysAgo", issuedDaysAgo);
                c.Parameters.AddWithValue("@dueInDays", dueInDays);
                c.Parameters.AddWithValue("@paidDaysAgo", paidDaysAgo ?? -1);
            });
        return (Guid)rows[0]["id"]!;
    }

    private static async Task SeedInvoicePaymentAsync(Database db, long companyId, Guid invoiceId, decimal amount, string reference, int receivedDaysAgo)
    {
        await db.ExecuteAsync(
            @"INSERT INTO invoice_payments (company_id, issued_invoice_id, payment_reference, payment_method, currency, amount, received_at, status)
              VALUES (@companyId, @invoiceId, @ref, 'manual', 'USD', @amount, NOW() - make_interval(days => @days), 'posted')",
            c =>
            {
                c.Parameters.AddWithValue("@companyId", companyId);
                c.Parameters.AddWithValue("@invoiceId", invoiceId);
                c.Parameters.AddWithValue("@ref", reference);
                c.Parameters.AddWithValue("@amount", amount);
                c.Parameters.AddWithValue("@days", receivedDaysAgo);
            });
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task IssueInvoice_POD_Gate_Blocks_Without_Proof_When_Flag_On()
    {
        var db = CreateDatabase();
        await EnsureSchemasAsync(db);
        var companyId = await SeedCompanyAsync(db);
        var customerId = await SeedCustomerAsync(db, companyId, "cust-pod");
        var spine = new BusinessSpineService(db);
        var service = CreateRevenueService(db);

        async Task<(long jobId, Guid draftId)> DraftAsync(string code)
        {
            var jobId = await SeedJobAsync(db, companyId, customerId, null, null, "delivered", code);
            await spine.CreateJobChargeAsync(companyId, jobId, null, null, "BASE", "Base charge", "base", "line", 1m, 100m, 100m, "USD", "approved");
            var d = await service.CreateInvoiceDraftFromJobAsync(companyId, jobId, code);
            Assert.True(d.Success);
            return (jobId, d.Draft!.Id);
        }

        try
        {
            // Flag OFF (default): issuance is not POD-gated.
            var (_, draftOff) = await DraftAsync($"POD-OFF-{companyId}");
            var off = await service.IssueInvoiceFromDraftAsync(companyId, draftOff, $"iss-off-{companyId}");
            Assert.DoesNotContain("no proof of delivery", off.Message, StringComparison.OrdinalIgnoreCase);

            // Turn the gate on for this tenant.
            await db.ExecuteAsync(
                @"INSERT INTO feature_flags (company_id, flag_key, name, enabled)
                  VALUES (@c, 'billing.require_pod_to_issue', 'Require POD to issue', TRUE)",
                c => c.Parameters.AddWithValue("@c", companyId));

            // Flag ON, no POD -> blocked.
            var (_, draftNoPod) = await DraftAsync($"POD-NONE-{companyId}");
            var blocked = await service.IssueInvoiceFromDraftAsync(companyId, draftNoPod, $"iss-nopod-{companyId}");
            Assert.False(blocked.Success);
            Assert.Contains("no proof of delivery", blocked.Message, StringComparison.OrdinalIgnoreCase);

            // Flag ON, POD captured (proof_of_delivery path) -> passes the gate.
            var (jobPod, draftPod) = await DraftAsync($"POD-YES-{companyId}");
            await db.ExecuteAsync(
                "INSERT INTO proof_of_delivery (company_id, job_id, receiver_name, status) VALUES (@c, @j, 'Jane Receiver', 'Captured')",
                c => { c.Parameters.AddWithValue("@c", companyId); c.Parameters.AddWithValue("@j", jobPod); });
            var allowed = await service.IssueInvoiceFromDraftAsync(companyId, draftPod, $"iss-pod-{companyId}");
            Assert.DoesNotContain("no proof of delivery", allowed.Message, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            await db.ExecuteAsync("DELETE FROM feature_flags WHERE company_id=@c", c => c.Parameters.AddWithValue("@c", companyId));
        }
    }

    private static RevenueReadinessService CreateRevenueService(Database db)
    {
        var correlation = new InMemoryCorrelationContext("corr-stage7", "cause-stage7", "req-stage7", null, ActorTypes.TenantUser, "42");
        return new RevenueReadinessService(
            db,
            new PostgresAiFoundationService(db, correlation),
            new PostgresApprovalWorkflowService(db, correlation),
            new PostgresIdempotencyService(db),
            new PostgresDomainEventPublisher(db, correlation),
            correlation,
            new TaxService(db));
    }

    private static Database CreateDatabase()
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:DefaultConnection"] = LocalConnectionString,
            })
            .Build();
        return new Database(config);
    }

    private static async Task EnsureSchemasAsync(Database db)
    {
        await ApplyBaseSchemaAsync(db);
        await new FoundationSchemaService(db).EnsureAsync();
        await new BusinessSpineSchemaService(db).EnsureAsync();
        await new RevenueReadinessSchemaService(db).EnsureAsync();
        await new FinanceActivationSchemaService(db).EnsureAsync();
    }

    private static async Task ApplyBaseSchemaAsync(Database db)
    {
        await db.ExecuteAsync(@"
CREATE TABLE IF NOT EXISTS vehicles (
  id BIGINT GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
  company_id BIGINT NOT NULL,
  vehicle_code VARCHAR(50) NOT NULL,
  type VARCHAR(80) NOT NULL,
  make VARCHAR(100) NULL,
  model VARCHAR(100) NULL,
  year INT NULL,
  vin VARCHAR(120) NULL,
  plate_number VARCHAR(50) NULL,
  status VARCHAR(50) NOT NULL DEFAULT 'Available',
  odometer_miles DECIMAL(12,2) NOT NULL DEFAULT 0,
  readiness_score DECIMAL(6,2) NOT NULL DEFAULT 95,
  data_quality_score DECIMAL(6,2) NOT NULL DEFAULT 95,
  risk_score DECIMAL(6,2) NOT NULL DEFAULT 10,
  device_status VARCHAR(60) NOT NULL DEFAULT 'Online',
  camera_status VARCHAR(60) NOT NULL DEFAULT 'Online',
  assigned_driver_id BIGINT NULL,
  deleted_at TIMESTAMPTZ NULL,
  created_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
  UNIQUE (company_id, vehicle_code)
)");

        await db.ExecuteAsync(@"
INSERT INTO companies (id, company_code, name, industry, timezone, status)
OVERRIDING SYSTEM VALUE
VALUES (1, 'OPX-BASE-ID-1', 'OpsTrax Demo Logistics', 'Transport & Field Operations', 'America/New_York', 'Active')
ON CONFLICT (id) DO UPDATE SET
  name=EXCLUDED.name,
  industry=EXCLUDED.industry,
  timezone=EXCLUDED.timezone,
  status=EXCLUDED.status");
        await db.ExecuteAsync("SELECT setval(pg_get_serial_sequence('companies', 'id'), (SELECT COALESCE(MAX(id), 1) FROM companies))");

        await db.ExecuteAsync("ALTER TABLE ai_recommendations ADD COLUMN IF NOT EXISTS company_id BIGINT NOT NULL DEFAULT 1");
        await db.ExecuteAsync("ALTER TABLE ai_recommendations ADD COLUMN IF NOT EXISTS module_key VARCHAR(100) NULL");
        await db.ExecuteAsync("ALTER TABLE ai_recommendations ADD COLUMN IF NOT EXISTS body TEXT NULL");
        await db.ExecuteAsync("ALTER TABLE ai_recommendations ADD COLUMN IF NOT EXISTS score DECIMAL(6,2) NOT NULL DEFAULT 80");
        await db.ExecuteAsync("ALTER TABLE jobs ADD COLUMN IF NOT EXISTS contract_id BIGINT NULL");
        await db.ExecuteAsync("ALTER TABLE jobs ADD COLUMN IF NOT EXISTS job_number VARCHAR(60) NULL");
        await db.ExecuteAsync("ALTER TABLE jobs ADD COLUMN IF NOT EXISTS updated_at TIMESTAMPTZ NULL");

        await db.ExecuteAsync(@"
INSERT INTO drivers (id, company_id, driver_code, full_name, phone, email, license_number, status, safety_score, readiness_score, risk_score, compliance_score, assigned_vehicle_id)
OVERRIDING SYSTEM VALUE
SELECT n, 1, 'DRV-' || LPAD(n::TEXT, 3, '0'), 'Stage 7 Driver ' || n, '+1 571 430 ' || LPAD((5300 + n)::TEXT, 4, '0'),
       'driver' || n || '@opstrax.example', 'VA-D' || LPAD(n::TEXT, 4, '0'),
       'Available', 90 + (n % 8), 88 + (n % 10), 10 + (n % 7), 90 + (n % 6), NULL
FROM generate_series(1, 20) AS n
ON CONFLICT DO NOTHING");
        await db.ExecuteAsync("SELECT setval(pg_get_serial_sequence('drivers', 'id'), (SELECT COALESCE(MAX(id), 1) FROM drivers))");

        await db.ExecuteAsync(@"
INSERT INTO vehicles (id, company_id, vehicle_code, type, make, model, year, vin, plate_number, status, odometer_miles, readiness_score, data_quality_score, risk_score, device_status, camera_status, assigned_driver_id)
OVERRIDING SYSTEM VALUE
SELECT n, 1,
       (ARRAY['TRK','VAN','BOX','REEFER'])[((n - 1) % 4) + 1] || '-' || (100 + n)::TEXT,
       (ARRAY['Truck','Van','Box Truck','Reefer'])[((n - 1) % 4) + 1],
       (ARRAY['Freightliner','Ford','Isuzu','International','Mercedes'])[((n - 1) % 5) + 1],
       (ARRAY['M2','Transit','NPR','MV','Sprinter'])[((n - 1) % 5) + 1],
       2020 + (n % 5),
       'VINOPSTRAX' || LPAD(n::TEXT, 6, '0'),
       'VA-' || (100 + n)::TEXT,
       (ARRAY['Available','On Route','At Stop','Idle','Delayed','Maintenance'])[((n - 1) % 6) + 1],
       14000 + n * 3100,
       82 + (n % 16),
       86 + (n % 13),
       10 + (n % 20),
       CASE WHEN n % 7 = 0 THEN 'Degraded' ELSE 'Online' END,
       CASE WHEN n % 6 = 0 THEN 'Needs Review' ELSE 'Online' END,
       n
FROM generate_series(1, 20) AS n
ON CONFLICT DO NOTHING");
        await db.ExecuteAsync("SELECT setval(pg_get_serial_sequence('vehicles', 'id'), (SELECT COALESCE(MAX(id), 1) FROM vehicles))");

        await db.ExecuteAsync("UPDATE drivers SET assigned_vehicle_id=v.id FROM vehicles v WHERE v.assigned_driver_id=drivers.id");
    }

    private static async Task<long> SeedCompanyAsync(Database db, string? companyCode = null)
    {
        var companyId = await db.InsertAsync(
            @"INSERT INTO companies (company_code, name, industry, timezone, status)
              VALUES (@code, @name, 'Logistics', 'America/New_York', 'Active')
              ON CONFLICT (company_code) DO UPDATE SET name=EXCLUDED.name",
            c =>
            {
                c.Parameters.AddWithValue("@code", companyCode ?? $"stage7-{Guid.NewGuid():N}");
                c.Parameters.AddWithValue("@name", $"Stage 7 {companyCode ?? "tenant"}");
            });

        await ResetFixtureAsync(db, companyId);
        return companyId;
    }

    private static async Task<long> SeedCustomerAsync(Database db, long companyId, string code)
    {
        return await db.InsertAsync(
            @"INSERT INTO customers (company_id, customer_code, name, contact_name, status, sla_tier, sla_health_score, delivery_experience_score, risk_score)
              VALUES (@companyId, @code, @name, 'Operations', 'Active', 'Standard', 95, 95, 10)
              ON CONFLICT (company_id, customer_code) DO UPDATE SET name=EXCLUDED.name",
            c =>
            {
                c.Parameters.AddWithValue("@companyId", companyId);
                c.Parameters.AddWithValue("@code", code);
                c.Parameters.AddWithValue("@name", $"{code} customer");
            });
    }

    private static async Task<long> SeedContractAsync(Database db, long companyId, long customerId, string code)
    {
        await db.ExecuteAsync("DELETE FROM contracts WHERE company_id=@companyId AND contract_code=@code", c =>
        {
            c.Parameters.AddWithValue("@companyId", companyId);
            c.Parameters.AddWithValue("@code", code);
        });
        return await db.InsertAsync(
            @"INSERT INTO contracts (company_id, customer_id, contract_code, title, rate_type, status, effective_date, expiration_date)
              VALUES (@companyId, @customerId, @code, @title, 'Per Mile', 'Active', CURRENT_DATE, CURRENT_DATE + INTERVAL '180 days')",
            c =>
            {
                c.Parameters.AddWithValue("@companyId", companyId);
                c.Parameters.AddWithValue("@customerId", customerId);
                c.Parameters.AddWithValue("@code", code);
                c.Parameters.AddWithValue("@title", $"{code} contract");
            });
    }

    private static async Task<long> SeedJobAsync(Database db, long companyId, long customerId, long? contractId, long? rateCardId, string status, string jobCode)
    {
        return await db.InsertAsync(
            @"INSERT INTO jobs (company_id, customer_id, contract_id, job_code, job_number, job_type, pickup_address, dropoff_address, scheduled_start, scheduled_end, status, priority, rate_card_id)
              VALUES (@companyId, @customerId, @contractId, @jobCode, @jobNumber, 'Delivery', 'Origin', 'Destination', NOW(), NOW() + INTERVAL '3 hours', @status, 'Normal', @rateCardId)
              ON CONFLICT (company_id, job_code) DO UPDATE SET contract_id=EXCLUDED.contract_id, job_number=EXCLUDED.job_number, rate_card_id=EXCLUDED.rate_card_id, status=EXCLUDED.status, updated_at=NOW()",
            c =>
            {
                c.Parameters.AddWithValue("@companyId", companyId);
                c.Parameters.AddWithValue("@customerId", customerId);
                c.Parameters.AddWithValue("@contractId", (object?)contractId ?? DBNull.Value);
                c.Parameters.AddWithValue("@jobCode", jobCode);
                c.Parameters.AddWithValue("@jobNumber", jobCode);
                c.Parameters.AddWithValue("@status", status);
                c.Parameters.AddWithValue("@rateCardId", (object?)rateCardId ?? DBNull.Value);
            });
    }

    private static async Task CleanupTenantAsync(Database db, long companyId)
    {
        await db.ExecuteAsync("DELETE FROM invoice_payments WHERE company_id=@companyId", c => c.Parameters.AddWithValue("@companyId", companyId));
        await db.ExecuteAsync("DELETE FROM issued_invoice_lines WHERE company_id=@companyId", c => c.Parameters.AddWithValue("@companyId", companyId));
        await db.ExecuteAsync("DELETE FROM issued_invoices WHERE company_id=@companyId", c => c.Parameters.AddWithValue("@companyId", companyId));
        await db.ExecuteAsync("DELETE FROM invoice_draft_lines WHERE company_id=@companyId", c => c.Parameters.AddWithValue("@companyId", companyId));
        await db.ExecuteAsync("DELETE FROM invoice_drafts WHERE company_id=@companyId", c => c.Parameters.AddWithValue("@companyId", companyId));
        await db.ExecuteAsync("DELETE FROM ai_action_requests WHERE tenant_id=@companyId", c => c.Parameters.AddWithValue("@companyId", companyId));
        await db.ExecuteAsync("DELETE FROM ai_recommendations WHERE tenant_id=@companyId", c => c.Parameters.AddWithValue("@companyId", companyId));
        await db.ExecuteAsync("DELETE FROM approval_decisions WHERE tenant_id=@companyId", c => c.Parameters.AddWithValue("@companyId", companyId));
        await db.ExecuteAsync("DELETE FROM approval_requests WHERE tenant_id=@companyId", c => c.Parameters.AddWithValue("@companyId", companyId));
        await db.ExecuteAsync("DELETE FROM event_processing_logs WHERE tenant_id=@companyId", c => c.Parameters.AddWithValue("@companyId", companyId));
        await db.ExecuteAsync("DELETE FROM outbox_messages WHERE tenant_id=@companyId", c => c.Parameters.AddWithValue("@companyId", companyId));
        await db.ExecuteAsync("DELETE FROM domain_events WHERE tenant_id=@companyId", c => c.Parameters.AddWithValue("@companyId", companyId));
        await db.ExecuteAsync("DELETE FROM idempotency_keys WHERE tenant_id=@companyId", c => c.Parameters.AddWithValue("@companyId", companyId));
        await db.ExecuteAsync("DELETE FROM job_charges WHERE company_id=@companyId", c => c.Parameters.AddWithValue("@companyId", companyId));
        await db.ExecuteAsync("DELETE FROM jobs WHERE company_id=@companyId", c => c.Parameters.AddWithValue("@companyId", companyId));
        await db.ExecuteAsync("DELETE FROM contracts WHERE company_id=@companyId", c => c.Parameters.AddWithValue("@companyId", companyId));
        await db.ExecuteAsync("DELETE FROM customers WHERE company_id=@companyId", c => c.Parameters.AddWithValue("@companyId", companyId));
        await db.ExecuteAsync("DELETE FROM companies WHERE id=@companyId", c => c.Parameters.AddWithValue("@companyId", companyId));
    }

    private static async Task ResetFixtureAsync(Database db, long companyId)
    {
        await db.ExecuteAsync("DELETE FROM job_charges WHERE company_id=@companyId", c => c.Parameters.AddWithValue("@companyId", companyId));
        await db.ExecuteAsync("DELETE FROM invoice_payments WHERE company_id=@companyId", c => c.Parameters.AddWithValue("@companyId", companyId));
        await db.ExecuteAsync("DELETE FROM issued_invoice_lines WHERE company_id=@companyId", c => c.Parameters.AddWithValue("@companyId", companyId));
        await db.ExecuteAsync("DELETE FROM issued_invoices WHERE company_id=@companyId", c => c.Parameters.AddWithValue("@companyId", companyId));
        await db.ExecuteAsync("DELETE FROM invoice_draft_lines WHERE company_id=@companyId", c => c.Parameters.AddWithValue("@companyId", companyId));
        await db.ExecuteAsync("DELETE FROM invoice_drafts WHERE company_id=@companyId", c => c.Parameters.AddWithValue("@companyId", companyId));
        await db.ExecuteAsync("DELETE FROM ai_action_requests WHERE tenant_id=@companyId", c => c.Parameters.AddWithValue("@companyId", companyId));
        await db.ExecuteAsync("DELETE FROM ai_recommendations WHERE tenant_id=@companyId", c => c.Parameters.AddWithValue("@companyId", companyId));
        await db.ExecuteAsync("DELETE FROM approval_decisions WHERE tenant_id=@companyId", c => c.Parameters.AddWithValue("@companyId", companyId));
        await db.ExecuteAsync("DELETE FROM approval_requests WHERE tenant_id=@companyId", c => c.Parameters.AddWithValue("@companyId", companyId));
        await db.ExecuteAsync("DELETE FROM event_processing_logs WHERE tenant_id=@companyId", c => c.Parameters.AddWithValue("@companyId", companyId));
        await db.ExecuteAsync("DELETE FROM outbox_messages WHERE tenant_id=@companyId", c => c.Parameters.AddWithValue("@companyId", companyId));
        await db.ExecuteAsync("DELETE FROM domain_events WHERE tenant_id=@companyId", c => c.Parameters.AddWithValue("@companyId", companyId));
        await db.ExecuteAsync("DELETE FROM idempotency_keys WHERE tenant_id=@companyId", c => c.Parameters.AddWithValue("@companyId", companyId));
        await db.ExecuteAsync("DELETE FROM jobs WHERE company_id=@companyId", c => c.Parameters.AddWithValue("@companyId", companyId));
        await db.ExecuteAsync("DELETE FROM rate_cards WHERE company_id=@companyId", c => c.Parameters.AddWithValue("@companyId", companyId));
        await db.ExecuteAsync("DELETE FROM contracts WHERE company_id=@companyId", c => c.Parameters.AddWithValue("@companyId", companyId));
        await db.ExecuteAsync("DELETE FROM customers WHERE company_id=@companyId", c => c.Parameters.AddWithValue("@companyId", companyId));
    }

    private static string ReadArtifact(string relativePath)
    {
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (root is not null && !File.Exists(Path.Combine(root.FullName, relativePath)))
        {
            root = root.Parent;
        }

        if (root is null)
        {
            throw new FileNotFoundException($"Could not locate {relativePath} from test output directory");
        }

        return File.ReadAllText(Path.Combine(root.FullName, relativePath));
    }
}
