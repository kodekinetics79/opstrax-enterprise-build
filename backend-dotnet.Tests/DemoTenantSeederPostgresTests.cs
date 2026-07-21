using Microsoft.Extensions.Configuration;
using Opstrax.Api.Data;
using Opstrax.Api.Foundation;
using Opstrax.Api.Services;
using Xunit;

namespace Opstrax.Tests;

// Proves the demo-tenant seeder: it runs through the REAL finance service layer,
// produces the expected counts + AR aging spread, and is idempotent. Doubles as the
// STEP-2 KPI spot-check (numbers are hand-calculated against what was seeded).
public class DemoTenantSeederPostgresTests
{
    private static readonly string LocalConnectionString = TestDb.ConnectionString;

    // Isolated, throwaway tenant code — MUST differ from the production DemoTenantSeeder
    // code so this test never deletes/recreates the real runtime demo tenant a pilot uses.
    private const string TestCompanyName = "Meridian Logistics — Demo (Test)";

    [Fact]
    public async Task DemoSeed_ProducesExpectedCountsAndArSpread_AndIsIdempotent()
    {
        var db = CreateDatabase();
        await EnsureSchemasAsync(db);
        var testCompanyCode = $"MERIDIAN-DEMO-TEST-{Guid.NewGuid():N}"[..36];
        await DeleteDemoTenantAsync(db, testCompanyCode); // clean slate for a full seed

        var seeder = new DemoTenantSeeder(db);
        var result = await seeder.SeedAsync(testCompanyCode, TestCompanyName);

        Assert.False(result.AlreadySeeded);
        Assert.Equal(TestCompanyName, result.CompanyName);
        Assert.Equal(5, result.Vehicles);
        Assert.Equal(5, result.Drivers);
        Assert.Equal(3, result.Customers);
        Assert.Equal(12, result.Jobs);
        Assert.Equal(4, result.IssuedInvoices);
        Assert.Equal(1, result.Payments);
        Assert.Equal(1, result.Feedback);
        // The feedback must actually be persisted (real service enforces job ownership).
        // (Assert deferred until after companyId is known — see below.)
        Assert.True(result.Trips >= 4);
        Assert.True(result.ProofPackages >= 3);

        var companyId = result.CompanyId;

        // KPI spot-check #1 — jobs span every status (12 rows).
        Assert.Equal(12, await db.ScalarLongAsync("SELECT COUNT(*) FROM jobs WHERE company_id=@c", c => c.Parameters.AddWithValue("@c", companyId)));
        // every distinct status present
        foreach (var s in new[] { "draft", "scheduled", "assigned", "in_progress", "completed", "cancelled", "exception" })
        {
            Assert.True(await db.ScalarLongAsync("SELECT COUNT(*) FROM jobs WHERE company_id=@c AND status=@s", c => { c.Parameters.AddWithValue("@c", companyId); c.Parameters.AddWithValue("@s", s); }) >= 1, $"missing job status {s}");
        }

        var revenue = CreateRevenueService(db, companyId);

        // KPI spot-check #2 — AR aging spread (hand-calculated: paid excluded; 1 current, 1 in 31-60, 1 in 90+).
        var aging = await revenue.GetAccountsReceivableAgingAsync(companyId);
        Assert.Equal(2100.50m, aging.Current);
        Assert.Equal(0m, aging.Days1To30);
        Assert.Equal(875.25m, aging.Days31To60);
        Assert.Equal(0m, aging.Days61To90);
        Assert.Equal(3300.00m, aging.Days90Plus);
        Assert.Equal(6275.75m, aging.TotalOutstanding); // 2100.50 + 875.25 + 3300.00 (paid 1450 excluded)

        // KPI spot-check #3 — AR summary: 4 issued invoices, 1450.00 paid.
        var ar = await revenue.GetAccountsReceivableSummaryAsync(companyId);
        Assert.Equal(4, ar.IssuedInvoiceCount);
        Assert.Equal(1450.00m, ar.PaidBalance);
        Assert.Equal(6275.75m, ar.OpenBalance);

        // Proof lifecycle spread: one validated, one rejected, one pending.
        Assert.Equal(1, await db.ScalarLongAsync("SELECT COUNT(*) FROM proof_packages WHERE company_id=@c AND validation_status='validated'", c => c.Parameters.AddWithValue("@c", companyId)));
        Assert.Equal(1, await db.ScalarLongAsync("SELECT COUNT(*) FROM proof_packages WHERE company_id=@c AND validation_status='rejected'", c => c.Parameters.AddWithValue("@c", companyId)));
        Assert.Equal(1, await db.ScalarLongAsync("SELECT COUNT(*) FROM proof_packages WHERE company_id=@c AND validation_status='pending'", c => c.Parameters.AddWithValue("@c", companyId)));

        // Feedback was actually persisted (guards the ownership-mismatch bug the walkthrough caught).
        Assert.Equal(1, await db.ScalarLongAsync("SELECT COUNT(*) FROM customer_feedback WHERE company_id=@c", c => c.Parameters.AddWithValue("@c", companyId)));

        // Idempotent — a second run must NOT duplicate.
        var second = await seeder.SeedAsync(testCompanyCode, TestCompanyName);
        Assert.True(second.AlreadySeeded);
        Assert.Equal(companyId, second.CompanyId);
        Assert.Equal(1, await db.ScalarLongAsync("SELECT COUNT(*) FROM companies WHERE company_code=@code", c => c.Parameters.AddWithValue("@code", testCompanyCode)));
        Assert.Equal(12, await db.ScalarLongAsync("SELECT COUNT(*) FROM jobs WHERE company_id=@c", c => c.Parameters.AddWithValue("@c", companyId)));

        // Two demo login users were seeded (internal admin + customer-portal).
        // Two demo login users seeded for this tenant (emails are code-suffixed for the
        // isolated test tenant so they never collide with the runtime demo tenant's users).
        Assert.Equal(2, await db.ScalarLongAsync("SELECT COUNT(*) FROM users WHERE company_id=@c AND (email LIKE 'admin%@meridian.demo' OR email LIKE 'portal%@acme.demo')", c => c.Parameters.AddWithValue("@c", companyId)));

        // NOTE: intentionally NOT deleted — the demo tenant persists so it is usable in a
        // live demo. The seeder is idempotent, so a subsequent suite run re-seeds cleanly.
    }

    private static RevenueReadinessService CreateRevenueService(Database db, long companyId)
    {
        var correlation = new InMemoryCorrelationContext("corr-demo", "cause-demo", "req-demo", companyId.ToString(), ActorTypes.TenantUser, "1");
        return new RevenueReadinessService(db, new PostgresAiFoundationService(db, correlation), new PostgresApprovalWorkflowService(db, correlation), new PostgresIdempotencyService(db), new PostgresDomainEventPublisher(db, correlation), correlation, new TaxService(db));
    }

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
        await db.ExecuteAsync("ALTER TABLE ai_recommendations ADD COLUMN IF NOT EXISTS company_id BIGINT NOT NULL DEFAULT 1");
        await db.ExecuteAsync("ALTER TABLE ai_recommendations ADD COLUMN IF NOT EXISTS module_key VARCHAR(100) NULL");
        await db.ExecuteAsync("ALTER TABLE ai_recommendations ADD COLUMN IF NOT EXISTS body TEXT NULL");
        await db.ExecuteAsync("ALTER TABLE ai_recommendations ADD COLUMN IF NOT EXISTS score DECIMAL(6,2) NOT NULL DEFAULT 80");
        await db.ExecuteAsync("ALTER TABLE customer_feedback ADD COLUMN IF NOT EXISTS status VARCHAR(30) NOT NULL DEFAULT 'open'");
        await db.ExecuteAsync("ALTER TABLE customer_feedback ADD COLUMN IF NOT EXISTS subject VARCHAR(200) NULL");
    }

    // Schema-driven cleanup (mirrors TenantOffboardingService) — discovers EVERY table
    // carrying company_id/tenant_id and deletes in FK-safe iterative passes, so the demo
    // tenant is always fully removed and this test is order-independent. The old
    // hand-maintained DELETE list omitted several child tables (driver_documents,
    // vehicle_documents, customer_contacts/addresses, maintenance_*, …), which left the
    // company row undeletable and made the idempotency assertion state-dependent.
    private static async Task DeleteDemoTenantAsync(Database db, string companyCode)
    {
        var companyId = await db.ScalarLongAsync("SELECT COALESCE((SELECT id FROM companies WHERE company_code=@code LIMIT 1),0)", c => c.Parameters.AddWithValue("@code", companyCode));
        if (companyId == 0) return;

        var pairs = await db.QueryAsync(
            @"SELECT c.table_name, c.column_name
              FROM information_schema.columns c
              JOIN information_schema.tables t
                ON t.table_name=c.table_name AND t.table_schema=c.table_schema
              WHERE c.table_schema='public' AND t.table_type='BASE TABLE'
                AND c.column_name IN ('company_id','tenant_id')
                AND c.table_name <> 'companies'");
        var tenantTables = pairs
            .Select(p => (Table: p["tableName"]!.ToString()!, Column: p["columnName"]!.ToString()!))
            .ToList();

        for (var pass = 0; pass < tenantTables.Count + 2; pass++)
        {
            var removed = 0;
            foreach (var (table, column) in tenantTables)
            {
                try { removed += await db.ExecuteAsync($"DELETE FROM \"{table}\" WHERE {column}=@c", c => c.Parameters.AddWithValue("@c", companyId)); }
                catch { /* FK-blocked this pass — a later pass clears the child first */ }
            }
            if (removed == 0) break;
        }
        try { await db.ExecuteAsync("DELETE FROM companies WHERE id=@c", c => c.Parameters.AddWithValue("@c", companyId)); } catch { /* residual FK cycle — leave for inspection */ }
    }
}
