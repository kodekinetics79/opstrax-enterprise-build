using Microsoft.Extensions.Configuration;
using Opstrax.Api.Data;
using Opstrax.Api.Services;
using Xunit;

namespace Opstrax.Tests;

// Customer Portal integration tests. Proves the NEW customer-within-tenant isolation
// dimension (a customer must not see another customer's data even in the same company)
// and query-level internal-field stripping — with realistic seeded data + real amounts.
public class CustomerPortalPostgresTests
{
    private const string LocalConnectionString =
        "Host=127.0.0.1;Port=5433;Database=opstrax_local;Username=zayra;Password=zayra";

    [Fact]
    public async Task PortalInvoices_AreScopedToTheCustomer_NotJustTheCompany()
    {
        var db = CreateDatabase();
        await EnsureSchemasAsync(db);
        var companyId = await SeedCompanyAsync(db);
        var customerA = await SeedCustomerAsync(db, companyId, "cust-portal-A");
        var customerB = await SeedCustomerAsync(db, companyId, "cust-portal-B");
        var portalUserA = await SeedPortalUserAsync(db, companyId, customerA, "portal-a@acme.example");
        await SeedPortalUserAsync(db, companyId, null, "internal-staff@acme.example"); // not a portal user

        await SeedInvoiceAsync(db, companyId, customerA, "INV-A-1", 1800.00m, 1800.00m, dueInDays: 12);
        await SeedInvoiceAsync(db, companyId, customerA, "INV-A-2", 2450.75m, 0m, dueInDays: 20);   // paid
        await SeedInvoiceAsync(db, companyId, customerB, "INV-B-1", 9999.99m, 9999.99m, dueInDays: -5); // other customer, overdue

        var svc = new CustomerPortalService(db);

        // Auth resolution: the portal user maps to customer A; internal staff maps to null.
        Assert.Equal(customerA, await svc.ResolveCustomerIdForUserAsync(companyId, portalUserA));
        var internalUserId = await db.ScalarLongAsync("SELECT id FROM users WHERE company_id=@c AND email='internal-staff@acme.example'", c => c.Parameters.AddWithValue("@c", companyId));
        Assert.Null(await svc.ResolveCustomerIdForUserAsync(companyId, internalUserId));

        var invoices = await svc.GetOwnInvoicesAsync(companyId, customerA);
        var numbers = invoices.Select(i => i["invoiceNumber"]?.ToString()).ToHashSet();

        Assert.Equal(2, invoices.Count);
        Assert.Contains("INV-A-1", numbers);
        Assert.Contains("INV-A-2", numbers);
        Assert.DoesNotContain("INV-B-1", numbers); // customer B's invoice never appears for customer A

        // Plain-English AR status (no raw bucket jargon).
        var invA1 = invoices.Single(i => i["invoiceNumber"]?.ToString() == "INV-A-1");
        Assert.Equal("Due in 12 day(s)", invA1["arStatus"]);
        var invA2 = invoices.Single(i => i["invoiceNumber"]?.ToString() == "INV-A-2");
        Assert.Equal("Paid", invA2["arStatus"]); // balance 0

        await CleanupAsync(db, companyId);
    }

    [Fact]
    public async Task PortalJobDetail_StripsInternalFields_AtTheQueryLevel()
    {
        var db = CreateDatabase();
        await EnsureSchemasAsync(db);
        var companyId = await SeedCompanyAsync(db);
        var customerId = await SeedCustomerAsync(db, companyId, "cust-strip");
        var driverId = await SeedDriverAsync(db, companyId, "Risky McDriver", riskScore: 88);
        // Job with internal cost/margin/risk + a dispatcher note + an assigned (risky) driver.
        var jobId = await SeedJobAsync(db, companyId, customerId, "JOB-STRIP-1", "Completed",
            riskScore: 91.5m, costEstimate: 640.00m, marginEstimate: 210.00m, revenueEstimate: 850.00m,
            notes: "INTERNAL: driver flagged, watch margin", assignedDriverId: driverId);
        // Internal AI recommendation tied to the job.
        await SeedAiRecommendationAsync(db, companyId, $"job:{jobId}:leakage", "Internal AI: underbilled by 210.00, re-rate the lane");

        var svc = new CustomerPortalService(db);
        var detail = await svc.GetOwnJobDetailAsync(companyId, customerId, jobId);
        Assert.NotNull(detail);
        var job = (Dictionary<string, object?>)detail!["job"]!;

        // Customer-appropriate fields ARE present.
        Assert.Equal("JOB-STRIP-1", job["jobNumber"]);
        Assert.Equal("Completed", job["status"]);
        Assert.True(job.ContainsKey("pickupAddress"));

        // Internal fields are ABSENT (stripped at the SELECT list, not just the UI).
        foreach (var forbidden in new[] { "riskScore", "costEstimate", "marginEstimate", "revenueEstimate", "notes", "assignedDriverId" })
        {
            Assert.False(job.ContainsKey(forbidden), $"internal field '{forbidden}' leaked to the customer");
        }

        // No internal AI reasoning or driver risk anywhere in the serialized payload.
        var serialized = System.Text.Json.JsonSerializer.Serialize(detail);
        Assert.DoesNotContain("Internal AI", serialized);
        Assert.DoesNotContain("underbilled", serialized);
        Assert.DoesNotContain("driver flagged", serialized);
        Assert.DoesNotContain("Risky McDriver", serialized);

        await CleanupAsync(db, companyId);
    }

    [Fact]
    public async Task PortalProofs_AreCustomerSafe_AndScopedToTheCustomer()
    {
        var db = CreateDatabase();
        await EnsureSchemasAsync(db);
        var companyId = await SeedCompanyAsync(db);
        var customerA = await SeedCustomerAsync(db, companyId, "cust-proof-A");
        var customerB = await SeedCustomerAsync(db, companyId, "cust-proof-B");
        var jobA = await SeedJobAsync(db, companyId, customerA, "JOB-PROOF-A", "Completed");
        await SeedProofAsync(db, companyId, jobA, "POD", receiverName: "Front desk", internalNote: "INTERNAL: recipient argued about damage");

        var svc = new CustomerPortalService(db);

        var proofs = await svc.GetOwnProofsAsync(companyId, customerA, jobA);
        var pkg = Assert.Single(proofs);
        Assert.Equal("POD", pkg["proofType"]);
        Assert.Equal("Front desk", pkg["receiverName"]);
        Assert.True(pkg.ContainsKey("artifacts"));
        foreach (var forbidden in new[] { "capturedByUserId", "deviceId", "validationSummary", "notes", "metadataJson", "correlationId" })
        {
            Assert.False(pkg.ContainsKey(forbidden), $"internal proof field '{forbidden}' leaked");
        }
        Assert.DoesNotContain("INTERNAL", System.Text.Json.JsonSerializer.Serialize(proofs));

        // Customer B cannot see customer A's proofs.
        var bProofs = await svc.GetOwnProofsAsync(companyId, customerB, jobA);
        Assert.Empty(bProofs);

        await CleanupAsync(db, companyId);
    }

    [Fact]
    public async Task PortalFeedback_IsCustomerAndTenantScoped()
    {
        var db = CreateDatabase();
        await EnsureSchemasAsync(db);
        var companyId = await SeedCompanyAsync(db);
        var customerA = await SeedCustomerAsync(db, companyId, "cust-fb-A");
        var customerB = await SeedCustomerAsync(db, companyId, "cust-fb-B");
        var jobA = await SeedJobAsync(db, companyId, customerA, "JOB-FB-A", "Completed");
        var jobB = await SeedJobAsync(db, companyId, customerB, "JOB-FB-B", "Completed");

        // Second tenant with its own feedback — must never surface for tenant 1.
        var otherCompanyId = await SeedCompanyAsync(db);
        var otherCustomer = await SeedCustomerAsync(db, otherCompanyId, "cust-fb-other");
        var otherJob = await SeedJobAsync(db, otherCompanyId, otherCustomer, "JOB-FB-OTHER", "Completed");

        var svc = new CustomerPortalService(db);

        var submitted = await svc.SubmitFeedbackAsync(companyId, customerA, jobA, 2, "Late and damaged", "complaint", "Delivery issue");
        Assert.NotNull(submitted);
        Assert.Equal("open", submitted!["status"]);
        await svc.SubmitFeedbackAsync(otherCompanyId, otherCustomer, otherJob, 5, "Great", "praise", "Thanks");

        // Customer A sees their own feedback.
        var aFeedback = await svc.GetOwnFeedbackAsync(companyId, customerA);
        Assert.Contains(aFeedback, f => f["comment"]?.ToString() == "Late and damaged" && f["status"]?.ToString() == "open");

        // Customer B (same company) does NOT see customer A's feedback.
        var bFeedback = await svc.GetOwnFeedbackAsync(companyId, customerB);
        Assert.DoesNotContain(bFeedback, f => f["comment"]?.ToString() == "Late and damaged");

        // A customer cannot file feedback against another customer's job.
        Assert.Null(await svc.SubmitFeedbackAsync(companyId, customerA, jobB, 1, "not mine", "complaint", null));

        // The other tenant's feedback never appears for tenant 1's customers.
        Assert.DoesNotContain(aFeedback, f => f["comment"]?.ToString() == "Great");

        await CleanupAsync(db, companyId);
        await CleanupAsync(db, otherCompanyId);
    }

    // ── Helpers (self-contained; seeded/torn down per test) ────────────────────────
    private static Database CreateDatabase()
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["ConnectionStrings:DefaultConnection"] = LocalConnectionString })
            .Build();
        return new Database(config);
    }

    private static async Task EnsureSchemasAsync(Database db)
    {
        await new FoundationSchemaService(db).EnsureAsync();
        await new BusinessSpineSchemaService(db).EnsureAsync();
        await new RevenueReadinessSchemaService(db).EnsureAsync();
        await new FinanceActivationSchemaService(db).EnsureAsync();
        // Stage-21 portal columns (idempotent — mirrors the migration).
        await db.ExecuteAsync("ALTER TABLE users ADD COLUMN IF NOT EXISTS customer_id BIGINT NULL");
        await db.ExecuteAsync("ALTER TABLE customer_feedback ADD COLUMN IF NOT EXISTS status VARCHAR(30) NOT NULL DEFAULT 'open'");
        await db.ExecuteAsync("ALTER TABLE customer_feedback ADD COLUMN IF NOT EXISTS subject VARCHAR(200) NULL");
        await db.ExecuteAsync("ALTER TABLE customer_feedback ADD COLUMN IF NOT EXISTS updated_at TIMESTAMPTZ NULL");
        await db.ExecuteAsync("ALTER TABLE ai_recommendations ADD COLUMN IF NOT EXISTS company_id BIGINT NOT NULL DEFAULT 1");
        await db.ExecuteAsync("ALTER TABLE ai_recommendations ADD COLUMN IF NOT EXISTS body TEXT NULL");
    }

    private static async Task<long> SeedCompanyAsync(Database db) => await db.InsertAsync(
        @"INSERT INTO companies (company_code, name, industry, timezone, status)
          VALUES (@code, 'Portal Tenant', 'Logistics', 'America/New_York', 'Active')
          ON CONFLICT (company_code) DO UPDATE SET name=EXCLUDED.name",
        c => c.Parameters.AddWithValue("@code", $"portal-{Guid.NewGuid():N}"));

    private static async Task<long> SeedCustomerAsync(Database db, long companyId, string code) => await db.InsertAsync(
        @"INSERT INTO customers (company_id, customer_code, name, contact_name, status, sla_tier, sla_health_score, delivery_experience_score, risk_score)
          VALUES (@companyId, @code, @name, 'Ops', 'Active', 'Standard', 95, 95, 10)
          ON CONFLICT (company_id, customer_code) DO UPDATE SET name=EXCLUDED.name",
        c =>
        {
            c.Parameters.AddWithValue("@companyId", companyId);
            c.Parameters.AddWithValue("@code", $"{code}-{Guid.NewGuid():N}".Substring(0, 40));
            c.Parameters.AddWithValue("@name", $"{code} customer");
        });

    private static async Task<long> SeedPortalUserAsync(Database db, long companyId, long? customerId, string email) => await db.InsertAsync(
        @"INSERT INTO users (company_id, customer_id, full_name, email, role_name, status)
          VALUES (@companyId, @customerId, @name, @email, 'Customer Portal', 'Active')",
        c =>
        {
            c.Parameters.AddWithValue("@companyId", companyId);
            c.Parameters.AddWithValue("@customerId", (object?)customerId ?? DBNull.Value);
            c.Parameters.AddWithValue("@name", email.Split('@')[0]);
            c.Parameters.AddWithValue("@email", email);
        });

    private static async Task<long> SeedDriverAsync(Database db, long companyId, string name, decimal riskScore) => await db.InsertAsync(
        @"INSERT INTO drivers (company_id, driver_code, full_name, status, safety_score, readiness_score, risk_score, compliance_score)
          VALUES (@companyId, @code, @name, 'Available', 70, 80, @risk, 85)",
        c =>
        {
            c.Parameters.AddWithValue("@companyId", companyId);
            c.Parameters.AddWithValue("@code", $"DRV-{Guid.NewGuid():N}".Substring(0, 16));
            c.Parameters.AddWithValue("@name", name);
            c.Parameters.AddWithValue("@risk", riskScore);
        });

    private static async Task<long> SeedJobAsync(
        Database db, long companyId, long customerId, string jobCode, string status,
        decimal riskScore = 0, decimal costEstimate = 0, decimal marginEstimate = 0, decimal revenueEstimate = 0,
        string? notes = null, long? assignedDriverId = null) => await db.InsertAsync(
        @"INSERT INTO jobs (company_id, customer_id, job_code, job_number, job_type, pickup_address, dropoff_address,
                            scheduled_start, scheduled_end, status, priority, risk_score, cost_estimate, margin_estimate, revenue_estimate, notes, assigned_driver_id)
          VALUES (@companyId, @customerId, @jobCode, @jobCode, 'Delivery', '100 Origin St', '200 Destination Ave',
                  NOW() - INTERVAL '2 days', NOW() - INTERVAL '2 days' + INTERVAL '4 hours', @status, 'Normal', @risk, @cost, @margin, @revenue, @notes, @driverId)
          ON CONFLICT (company_id, job_code) DO UPDATE SET status=EXCLUDED.status, updated_at=NOW()",
        c =>
        {
            c.Parameters.AddWithValue("@companyId", companyId);
            c.Parameters.AddWithValue("@customerId", customerId);
            c.Parameters.AddWithValue("@jobCode", jobCode);
            c.Parameters.AddWithValue("@status", status);
            c.Parameters.AddWithValue("@risk", riskScore);
            c.Parameters.AddWithValue("@cost", costEstimate);
            c.Parameters.AddWithValue("@margin", marginEstimate);
            c.Parameters.AddWithValue("@revenue", revenueEstimate);
            c.Parameters.AddWithValue("@notes", (object?)notes ?? DBNull.Value);
            c.Parameters.AddWithValue("@driverId", (object?)assignedDriverId ?? DBNull.Value);
        });

    private static async Task SeedInvoiceAsync(Database db, long companyId, long customerId, string invoiceNumber, decimal total, decimal balanceDue, int dueInDays)
    {
        var draftNo = $"DR-{invoiceNumber}";
        var draftRows = await db.QueryAsync(
            @"INSERT INTO invoice_drafts (company_id, customer_id, invoice_draft_no) VALUES (@companyId, @customerId, @draftNo) RETURNING id",
            c => { c.Parameters.AddWithValue("@companyId", companyId); c.Parameters.AddWithValue("@customerId", customerId); c.Parameters.AddWithValue("@draftNo", draftNo); });
        var draftId = (Guid)draftRows[0]["id"]!;
        await db.ExecuteAsync(
            @"INSERT INTO issued_invoices
                (company_id, customer_id, source_invoice_draft_id, source_invoice_draft_no, invoice_number, status, currency,
                 subtotal, tax_total, total, amount_paid, balance_due, payment_status, issued_at, due_at, paid_at)
              VALUES (@companyId, @customerId, @draftId, @draftNo, @invoiceNumber, @status, 'USD',
                 @total, 0, @total, @amountPaid, @balanceDue, @paymentStatus,
                 NOW() - INTERVAL '5 days', NOW() + make_interval(days => @dueInDays),
                 CASE WHEN @balanceDue <= 0 THEN NOW() - INTERVAL '1 day' ELSE NULL END)",
            c =>
            {
                c.Parameters.AddWithValue("@companyId", companyId);
                c.Parameters.AddWithValue("@customerId", customerId);
                c.Parameters.AddWithValue("@draftId", draftId);
                c.Parameters.AddWithValue("@draftNo", draftNo);
                c.Parameters.AddWithValue("@invoiceNumber", invoiceNumber);
                c.Parameters.AddWithValue("@status", balanceDue <= 0 ? "paid" : "issued");
                c.Parameters.AddWithValue("@total", total);
                c.Parameters.AddWithValue("@amountPaid", total - balanceDue);
                c.Parameters.AddWithValue("@balanceDue", balanceDue);
                c.Parameters.AddWithValue("@paymentStatus", balanceDue <= 0 ? "paid" : "unpaid");
                c.Parameters.AddWithValue("@dueInDays", dueInDays);
            });
    }

    private static async Task SeedProofAsync(Database db, long companyId, long jobId, string proofType, string receiverName, string internalNote)
    {
        var packageId = await db.InsertAsync(
            @"INSERT INTO proof_packages (company_id, job_id, proof_type, status, completed_at, receiver_name, geo_latitude, geo_longitude, notes, validation_summary, captured_by_user_id, device_id)
              VALUES (@companyId, @jobId, @proofType, 'completed', NOW() - INTERVAL '1 day', @receiverName, 38.75, -77.48, @note, @note, 4242, 'DEVICE-XYZ')",
            c =>
            {
                c.Parameters.AddWithValue("@companyId", companyId);
                c.Parameters.AddWithValue("@jobId", jobId);
                c.Parameters.AddWithValue("@proofType", proofType);
                c.Parameters.AddWithValue("@receiverName", receiverName);
                c.Parameters.AddWithValue("@note", internalNote);
            });
        await db.ExecuteAsync(
            @"INSERT INTO proof_artifacts (company_id, proof_package_id, artifact_type, file_id, captured_at, geo_latitude, geo_longitude, notes, device_id, captured_by_user_id)
              VALUES (@companyId, @packageId, 'photo', 9001, NOW() - INTERVAL '1 day', 38.75, -77.48, @note, 'DEVICE-XYZ', 4242)",
            c =>
            {
                c.Parameters.AddWithValue("@companyId", companyId);
                c.Parameters.AddWithValue("@packageId", packageId);
                c.Parameters.AddWithValue("@note", internalNote);
            });
    }

    private static async Task SeedAiRecommendationAsync(Database db, long companyId, string sourceEventId, string body)
    {
        await db.ExecuteAsync(
            @"INSERT INTO ai_recommendations (company_id, tenant_id, recommendation_type, module_key, title, summary, body, confidence_score, urgency_score, status, source_event_id)
              VALUES (@companyId, @companyId, 'revenue_leakage', 'finance', 'Internal AI', @body, @body, 90, 60, 'active', @sourceEventId)",
            c =>
            {
                c.Parameters.AddWithValue("@companyId", companyId);
                c.Parameters.AddWithValue("@body", body);
                c.Parameters.AddWithValue("@sourceEventId", sourceEventId);
            });
    }

    private static async Task CleanupAsync(Database db, long companyId)
    {
        foreach (var sql in new[]
        {
            "DELETE FROM customer_feedback WHERE company_id=@c",
            "DELETE FROM proof_artifacts WHERE company_id=@c",
            "DELETE FROM proof_packages WHERE company_id=@c",
            "DELETE FROM invoice_payments WHERE company_id=@c",
            "DELETE FROM issued_invoice_lines WHERE company_id=@c",
            "DELETE FROM issued_invoices WHERE company_id=@c",
            "DELETE FROM invoice_draft_lines WHERE company_id=@c",
            "DELETE FROM invoice_drafts WHERE company_id=@c",
            "DELETE FROM ai_recommendations WHERE company_id=@c",
            "DELETE FROM jobs WHERE company_id=@c",
            "DELETE FROM users WHERE company_id=@c AND role_name='Customer Portal'",
            "DELETE FROM drivers WHERE company_id=@c",
            "DELETE FROM customers WHERE company_id=@c",
            // Delete the tenant company itself LAST (after all FK children) — without
            // this every test run leaked a 'Portal Tenant' shell company into the shared
            // dev DB and polluted the platform Tenants list.
            "DELETE FROM companies WHERE id=@c",
        })
        {
            await db.ExecuteAsync(sql, c => c.Parameters.AddWithValue("@c", companyId));
        }
    }
}
