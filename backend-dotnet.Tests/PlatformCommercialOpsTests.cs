using Microsoft.Extensions.Configuration;
using Opstrax.Api.Controllers;
using Opstrax.Api.Data;
using Opstrax.Api.Services;
using System.Text.Json;

namespace Opstrax.Tests;

[Trait("Category", "Integration")]
public class PlatformCommercialOpsTests
{
    private const string LocalConnectionString =
        "Host=127.0.0.1;Port=5433;Database=opstrax_local;Username=zayra;Password=zayra";

    [Fact]
    public void HasPlatformPermission_Allows_Wildcard_And_Prefix()
    {
        Assert.True(PlatformEndpoints.HasPlatformPermission(new[] { "platform:*" }, "platform:dashboard:view"));
        Assert.True(PlatformEndpoints.HasPlatformPermission(new[] { "platform:tenants:*" }, "platform:tenants:manage"));
    }

    [Fact]
    public void HasPlatformPermission_Denies_When_Missing()
    {
        Assert.False(PlatformEndpoints.HasPlatformPermission(Array.Empty<string>(), "platform:dashboard:view"));
        Assert.False(PlatformEndpoints.HasPlatformPermission(new[] { "platform:billing:view" }, "platform:dashboard:view"));
    }

    [Fact]
    public async Task CommercialOpsSummary_Composes_Platform_Cockpit_From_Live_Data()
    {
        var db = CreateDatabase();
        var schema = new PlatformSchemaService(db);
        var companyId = NextCompanyId();
        var companyCode = $"CO-{companyId}";
        long packageId = 0;

        try
        {
            await schema.EnsureAsync();

            var insertedCompanyId = await db.InsertAsync(
                "INSERT INTO companies (company_code, name, industry, status) VALUES (@code, @name, @industry, @status) RETURNING id",
                c =>
                {
                    c.Parameters.AddWithValue("@code", companyCode);
                    c.Parameters.AddWithValue("@name", "Acme Commercial");
                    c.Parameters.AddWithValue("@industry", "Logistics");
                    c.Parameters.AddWithValue("@status", "Active");
                });

            packageId = await db.InsertAsync(
                @"INSERT INTO packages (id, package_code, name, billing_interval, currency, base_price_cents, seat_price_cents, included_seats, setup_fee_cents, annual_price_cents, module_keys, is_custom, active)
                  OVERRIDING SYSTEM VALUE VALUES (@id, @code, @name, 'monthly', 'USD', 120000, 2500, 10, 0, 1200000, '[""crm"",""reports""]'::jsonb, false, true)
                  RETURNING id",
                c =>
                {
                    c.Parameters.AddWithValue("@id", NextPackageId());
                    c.Parameters.AddWithValue("@code", "PKG-COMM-1");
                    c.Parameters.AddWithValue("@name", "Commercial Pro");
                });

            await db.ExecuteAsync(
                @"INSERT INTO tenant_subscriptions (company_id, package_id, status, seat_limit, billing_currency, mrr_cents, trial_ends_at, contract_start, contract_end, account_owner, support_owner)
                  VALUES (@cid, @pid, 'past_due', 12, 'USD', 145000, NOW() + INTERVAL '5 day', CURRENT_DATE - 30, CURRENT_DATE + 25, 'A. Owner', 'S. Owner')",
                c =>
                {
                    c.Parameters.AddWithValue("@cid", insertedCompanyId);
                    c.Parameters.AddWithValue("@pid", packageId);
                });

            await db.ExecuteAsync(
                @"INSERT INTO platform_invoices (company_id, invoice_number, status, kind, amount_cents, currency, line_items, notes, issued_at, due_at)
                  VALUES (@cid, @num, 'sent', 'recurring', 145000, 'USD', '[]'::jsonb, 'Commercial cockpit test', NOW(), NOW() + INTERVAL '15 day')",
                c =>
                {
                    c.Parameters.AddWithValue("@cid", insertedCompanyId);
                    c.Parameters.AddWithValue("@num", $"INV-{companyId}");
                });

            await db.ExecuteAsync(
                @"INSERT INTO platform_audit_log (actor_admin_id, actor_email, actor_role, action, entity_type, entity_id, target_company_id, details_json, ip_address)
                  VALUES (1, 'platform@opstrax.io', 'platform_super_admin', 'tenant.created', 'Tenant', @entityId, @companyId, '{}'::jsonb, '127.0.0.1')",
                c =>
                {
                    c.Parameters.AddWithValue("@entityId", companyId);
                    c.Parameters.AddWithValue("@companyId", companyId);
                });

            var summary = await PlatformEndpoints.BuildCommercialOpsSummaryAsync(db, CancellationToken.None);

            using var json = JsonDocument.Parse(JsonSerializer.Serialize(summary));
            var root = json.RootElement;
            var tenantLifecycle = root.GetProperty("tenantLifecycle");
            var billing = root.GetProperty("billing");
            var packages = root.GetProperty("packages");
            var health = root.GetProperty("health");
            var audit = root.GetProperty("audit");
            var roles = root.GetProperty("roles");

            Assert.True(tenantLifecycle.GetProperty("total").GetInt64() >= 1);
            Assert.True(tenantLifecycle.GetProperty("pastDue").GetInt64() >= 1);
            Assert.True(billing.GetProperty("openInvoiceCount").GetInt64() >= 1);
            Assert.True(packages.GetProperty("total").GetInt64() >= 1);
            Assert.True(health.GetProperty("total").GetInt64() >= 1);
            Assert.True(audit.GetProperty("recent").GetArrayLength() > 0);
            Assert.True(roles.GetProperty("total").GetInt64() >= 1);
            Assert.True(packages.GetProperty("items").GetArrayLength() > 0);
        }
        finally
        {
            if (packageId > 0)
            {
                await CleanupAsync(db, companyCode, packageId);
            }
        }
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

    private static long NextCompanyId() => Interlocked.Increment(ref _nextCompanyId);
    private static long NextPackageId() => Interlocked.Increment(ref _nextPackageId);

    private static long _nextCompanyId = 91000;
    private static long _nextPackageId = 99000;

    private static async Task CleanupAsync(Database db, string companyCode, long packageId)
    {
        var companyId = await db.ScalarLongAsync("SELECT id FROM companies WHERE company_code=@code LIMIT 1", c => c.Parameters.AddWithValue("@code", companyCode));
        await db.ExecuteAsync("DELETE FROM platform_audit_log WHERE target_company_id=@id", c => c.Parameters.AddWithValue("@id", companyId));
        await db.ExecuteAsync("DELETE FROM platform_invoices WHERE company_id=@id", c => c.Parameters.AddWithValue("@id", companyId));
        await db.ExecuteAsync("DELETE FROM tenant_subscriptions WHERE company_id=@id", c => c.Parameters.AddWithValue("@id", companyId));
        await db.ExecuteAsync("DELETE FROM packages WHERE id=@id", c => c.Parameters.AddWithValue("@id", packageId));
        await db.ExecuteAsync("DELETE FROM companies WHERE company_code=@code", c => c.Parameters.AddWithValue("@code", companyCode));
    }
}
