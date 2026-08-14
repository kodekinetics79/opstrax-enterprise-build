using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Npgsql;
using Opstrax.Api.Data;
using Opstrax.Api.Services;

namespace Opstrax.Tests;

[Trait("Category", "Integration")]
public sealed class MarketCatalogRuntimePostgresTests
{
    [Fact]
    public async Task ProtectedMigrationOwnsMarketRevenueDependenciesAndExactAclBoundary()
    {
        var db = Db();
        Assert.Equal(6, await db.ScalarLongAsync(
            @"SELECT COUNT(*) FROM (VALUES
                ('module_packages'),('usage_meters'),('usage_events'),
                ('usage_counters'),('pricing_rules'),('tenant_contract_overrides')
              ) expected(table_name)
              WHERE to_regclass('public.'||table_name) IS NOT NULL"));
        Assert.Equal(1, await db.ScalarLongAsync(
            @"SELECT CASE WHEN
                NOT has_table_privilege('opstrax_app','module_packages','INSERT')
                AND NOT has_table_privilege('opstrax_app','usage_meters','UPDATE')
                AND NOT has_table_privilege('opstrax_app','pricing_rules','DELETE')
                AND has_table_privilege('opstrax_app','module_packages','SELECT')
                AND has_table_privilege('opstrax_system','module_packages','INSERT')
                AND NOT has_sequence_privilege('opstrax_app','module_packages_id_seq','USAGE')
                AND has_sequence_privilege('opstrax_system','module_packages_id_seq','USAGE')
              THEN 1 ELSE 0 END"));
        Assert.Equal(1, await db.ScalarLongAsync(
            @"SELECT CASE WHEN
                has_table_privilege('opstrax_app','usage_events','SELECT')
                AND has_table_privilege('opstrax_app','usage_events','INSERT')
                AND NOT has_table_privilege('opstrax_app','usage_events','UPDATE')
                AND NOT has_table_privilege('opstrax_app','usage_events','DELETE')
                AND has_table_privilege('opstrax_system','usage_events','SELECT')
                AND has_table_privilege('opstrax_system','usage_events','INSERT')
                AND has_table_privilege('opstrax_system','usage_events','UPDATE')
                AND has_table_privilege('opstrax_system','usage_events','DELETE')
              THEN 1 ELSE 0 END"));
        Assert.Equal(3, await db.ScalarLongAsync(
            @"SELECT COUNT(*) FROM pg_class c
              WHERE c.oid IN ('usage_events'::regclass,'usage_counters'::regclass,'tenant_contract_overrides'::regclass)
                AND c.relrowsecurity AND c.relforcerowsecurity"));
        Assert.Equal(6, await db.ScalarLongAsync(
            @"SELECT COUNT(*) FROM pg_policies WHERE schemaname='public'
                AND tablename IN ('usage_events','usage_counters','tenant_contract_overrides')
                AND policyname IN ('tenant_ticket_app','system_control_plane')"));
        var readiness = await new FleetProductionReadinessService(
            db, NullLogger<FleetProductionReadinessService>.Instance).CheckAsync();
        Assert.True(readiness.MarketCatalogReady, readiness.FailureCode);

        await new MarketPackSchemaService(db).EnsureAsync();

        Assert.Equal(2, await db.ScalarLongAsync(
            "SELECT COUNT(*) FROM module_packages WHERE package_key IN ('canada_na_compliance','saudi_gcc_compliance')"));
        Assert.Equal(3, await db.ScalarLongAsync(
            "SELECT COUNT(*) FROM usage_meters WHERE meter_key IN ('compliance_documents.count','compliance_expiry_alerts.monthly','inspection_records.monthly')"));
    }

    [Fact]
    public async Task UsageEventsAreAppendOnlyForTenantRuntimeRole()
    {
        var owner = Db();
        var runtime = RuntimeDb();
        var suffix = Guid.NewGuid().ToString("N");
        var companyId = await owner.InsertAsync(
            "INSERT INTO companies(company_code,name,industry,status) VALUES(@code,@name,'Logistics','Active') RETURNING id",
            c =>
            {
                c.Parameters.AddWithValue("@code", $"UE-{suffix[..16]}".ToUpperInvariant());
                c.Parameters.AddWithValue("@name", $"Usage Event {suffix[..8]}");
            });
        long eventId = 0;

        try
        {
            eventId = await runtime.RunInTenantScopeAsync(companyId, () => runtime.InsertAsync(
                @"INSERT INTO usage_events(company_id,meter_key,quantity,reference,actor,period_key)
                  VALUES(@cid,'inspection_records.monthly',1,@reference,'integration-test','2026-08') RETURNING id",
                c =>
                {
                    c.Parameters.AddWithValue("@cid", companyId);
                    c.Parameters.AddWithValue("@reference", $"append-only:{suffix}");
                }));

            Assert.Equal(1, await runtime.RunInTenantScopeAsync(companyId, () => runtime.ScalarLongAsync(
                "SELECT COUNT(*) FROM usage_events WHERE id=@id AND company_id=@cid",
                c =>
                {
                    c.Parameters.AddWithValue("@id", eventId);
                    c.Parameters.AddWithValue("@cid", companyId);
                })));

            var update = await Assert.ThrowsAsync<PostgresException>(() => runtime.RunInTenantScopeAsync(companyId,
                () => runtime.ExecuteAsync("UPDATE usage_events SET quantity=2 WHERE id=@id",
                    c => c.Parameters.AddWithValue("@id", eventId))));
            Assert.Equal(PostgresErrorCodes.InsufficientPrivilege, update.SqlState);

            var delete = await Assert.ThrowsAsync<PostgresException>(() => runtime.RunInTenantScopeAsync(companyId,
                () => runtime.ExecuteAsync("DELETE FROM usage_events WHERE id=@id",
                    c => c.Parameters.AddWithValue("@id", eventId))));
            Assert.Equal(PostgresErrorCodes.InsufficientPrivilege, delete.SqlState);
        }
        finally
        {
            if (eventId > 0)
                await owner.ExecuteAsync("DELETE FROM usage_events WHERE id=@id", c => c.Parameters.AddWithValue("@id", eventId));
            await owner.ExecuteAsync("DELETE FROM companies WHERE id=@cid", c => c.Parameters.AddWithValue("@cid", companyId));
        }
    }

    private static Database Db() => new(new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
    {
        ["ConnectionStrings:DefaultConnection"] = TestDb.ConnectionString,
        ["Rls:EnforceTenantContext"] = "false"
    }).Build());

    private static Database RuntimeDb() => new(new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
    {
        ["ConnectionStrings:DefaultConnection"] = TestDb.AppConnectionString,
        ["ConnectionStrings:SystemConnection"] = TestDb.SystemConnectionString,
        ["Rls:EnforceTenantContext"] = "true",
        ["Rls:TenantTicketTtlSeconds"] = "120"
    }).Build(), new TenantScopeAccessor());
}
