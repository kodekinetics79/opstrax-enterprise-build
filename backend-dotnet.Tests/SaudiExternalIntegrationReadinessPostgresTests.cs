using Microsoft.Extensions.Configuration;
using Opstrax.Api.Data;
using Opstrax.Api.Seed;
using Opstrax.Api.Services;

namespace Opstrax.Tests;

public sealed class SaudiExternalIntegrationReadinessPostgresTests
{
    [Fact]
    public async Task CatalogHydration_IsolatesSaudiGovernmentAndPaymentProvidersByTenantMarket()
    {
        var db = CreateDatabase();
        await new CountryProfileSchemaService(db).EnsureAsync();
        var suffix = Guid.NewGuid().ToString("N")[..8].ToUpperInvariant();
        var saCompanyId = await db.InsertAsync(
            "INSERT INTO companies (company_code,name,industry,status,country,currency) VALUES (@code,'Saudi Connector Tenant','Logistics','Active','SA','SAR') RETURNING id",
            c => c.Parameters.AddWithValue("@code", $"SAEXT-{suffix}"));
        var usCompanyId = await db.InsertAsync(
            "INSERT INTO companies (company_code,name,industry,status,country,currency) VALUES (@code,'US Connector Tenant','Logistics','Active','US','USD') RETURNING id",
            c => c.Parameters.AddWithValue("@code", $"USEXT-{suffix}"));

        try
        {
            await IntegrationCatalog.EnsureTenantAsync(db, saCompanyId, CancellationToken.None);
            await IntegrationCatalog.EnsureTenantAsync(db, usCompanyId, CancellationToken.None);

            Assert.Equal(2, await CountAsync(db, saCompanyId, "tamm-saudi", "saudi-payment-gateway"));
            Assert.Equal(0, await CountAsync(db, usCompanyId, "tamm-saudi", "saudi-payment-gateway"));
            Assert.Equal(1, await CountAsync(db, saCompanyId, "locus"));
            Assert.Equal(1, await CountAsync(db, usCompanyId, "locus"));
        }
        finally
        {
            await db.ExecuteAsync("DELETE FROM integrations WHERE company_id IN (@sa,@us)", c =>
            {
                c.Parameters.AddWithValue("@sa", saCompanyId);
                c.Parameters.AddWithValue("@us", usCompanyId);
            });
            await db.ExecuteAsync("DELETE FROM companies WHERE id IN (@sa,@us)", c =>
            {
                c.Parameters.AddWithValue("@sa", saCompanyId);
                c.Parameters.AddWithValue("@us", usCompanyId);
            });
        }
    }

    private static Task<long> CountAsync(Database db, long companyId, params string[] keys) =>
        db.ScalarLongAsync(
            "SELECT COUNT(*) FROM integrations WHERE company_id=@cid AND integration_key = ANY(@keys)",
            c =>
            {
                c.Parameters.AddWithValue("@cid", companyId);
                c.Parameters.AddWithValue("@keys", keys);
            });

    private static Database CreateDatabase()
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:DefaultConnection"] = TestDb.ConnectionString,
            })
            .Build();
        return new Database(config);
    }
}
