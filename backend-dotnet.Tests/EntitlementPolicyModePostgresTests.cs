using Microsoft.Extensions.Configuration;
using Npgsql;
using Opstrax.Api.Controllers;
using Opstrax.Api.Data;
using Opstrax.Api.Services;

namespace Opstrax.Tests;

[Collection("platform-control-plane")]
[Trait("Category", "Integration")]
public sealed class EntitlementPolicyModePostgresTests
{
    private static Database Database()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:DefaultConnection"] = TestDb.ConnectionString,
            })
            .Build();
        return new Database(configuration);
    }

    [Fact]
    public void Production_Migration_Is_Compatibility_Safe_And_Ledgered()
    {
        var path = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory,
            "../../../../database/migrations/2026_08_02_stage68_entitlement_policy_mode.sql"));
        Assert.True(File.Exists(path), $"Production migration not found: {path}");
        var sql = File.ReadAllText(path);
        Assert.Contains("DEFAULT 'legacy_allow'", sql, StringComparison.Ordinal);
        Assert.Contains("WHERE entitlement_policy_mode IS NULL", sql, StringComparison.Ordinal);
        Assert.Contains("ALTER COLUMN entitlement_policy_mode SET NOT NULL", sql, StringComparison.Ordinal);
        Assert.Contains("CHECK (entitlement_policy_mode IN ('legacy_allow','package_allowlist'))", sql, StringComparison.Ordinal);
        Assert.Contains("2026_08_02_stage68_entitlement_policy_mode", sql, StringComparison.Ordinal);
        Assert.DoesNotContain("UPDATE companies\nSET entitlement_policy_mode='package_allowlist'", sql, StringComparison.Ordinal);
        Assert.Contains("CREATE TABLE IF NOT EXISTS demo_fixture_versions", sql, StringComparison.Ordinal);
        Assert.Contains("ALTER TABLE demo_fixture_versions ENABLE ROW LEVEL SECURITY", sql, StringComparison.Ordinal);
        Assert.Contains("ALTER TABLE demo_fixture_versions FORCE ROW LEVEL SECURITY", sql, StringComparison.Ordinal);
        Assert.Contains("CREATE POLICY tenant_ticket_app ON demo_fixture_versions", sql, StringComparison.Ordinal);
        Assert.Contains("FOR ALL TO opstrax_app", sql, StringComparison.Ordinal);
        Assert.Contains("REVOKE ALL ON demo_fixture_versions FROM opstrax_app", sql, StringComparison.Ordinal);
        Assert.Contains("GRANT SELECT ON demo_fixture_versions TO opstrax_app", sql, StringComparison.Ordinal);
        Assert.Contains("CREATE POLICY system_control_plane ON demo_fixture_versions", sql, StringComparison.Ordinal);
        Assert.Contains("FOREIGN KEY(company_id) REFERENCES companies(id) ON DELETE CASCADE", sql, StringComparison.Ordinal);

        var stage58Path = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory,
            "../../../../database/migrations/2026_07_31_stage58_nonforgeable_tenant_ticket.sql"));
        var stage58 = File.ReadAllText(stage58Path);
        Assert.Contains("('tenant_entitlements',false,false,false),('demo_fixture_versions',false,false,false)", stage58, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Legacy_Migration_And_Allowlist_Semantics_Are_Explicit()
    {
        var db = Database();
        await new PlatformSchemaService(db).EnsureAsync();
        var code = $"POL-{Guid.NewGuid():N}"[..16];
        var companyId = await db.InsertAsync(
            "INSERT INTO companies (company_code,name,industry,status) VALUES (@code,'Policy compatibility','Logistics','Active') RETURNING id",
            c => c.Parameters.AddWithValue("@code", code));
        try
        {
            var policy = (await db.QuerySingleAsync(
                "SELECT entitlement_policy_mode FROM companies WHERE id=@id",
                c => c.Parameters.AddWithValue("@id", companyId)))?["entitlementPolicyMode"]?.ToString();
            Assert.Equal(EntitlementService.LegacyAllowPolicy, policy);

            var evaluator = new EntitlementService(db);
            Assert.True((await evaluator.CheckModuleAsync(companyId, "safety")).Allowed);

            await db.ExecuteAsync(
                "UPDATE companies SET entitlement_policy_mode='package_allowlist' WHERE id=@id",
                c => c.Parameters.AddWithValue("@id", companyId));
            Assert.False((await evaluator.CheckModuleAsync(companyId, "safety")).Allowed);

            await db.ExecuteAsync(
                "INSERT INTO tenant_entitlements(company_id,module_key,enabled,source) VALUES (@id,'safety',true,'override')",
                c => c.Parameters.AddWithValue("@id", companyId));
            Assert.True((await evaluator.CheckModuleAsync(companyId, "safety")).Allowed);
            Assert.False((await evaluator.CheckModuleAsync(companyId, "dispatch")).Allowed);

            await Assert.ThrowsAsync<Npgsql.PostgresException>(() => db.ExecuteAsync(
                "UPDATE companies SET entitlement_policy_mode='unknown' WHERE id=@id",
                c => c.Parameters.AddWithValue("@id", companyId)));
        }
        finally
        {
            await db.ExecuteAsync("DELETE FROM tenant_entitlements WHERE company_id=@id", c => c.Parameters.AddWithValue("@id", companyId));
            await db.ExecuteAsync("DELETE FROM companies WHERE id=@id", c => c.Parameters.AddWithValue("@id", companyId));
        }
    }

    [Fact]
    public async Task Package_Reassignment_Replaces_Only_Package_Derived_Rights()
    {
        var db = Database();
        await new PlatformSchemaService(db).EnsureAsync();
        var suffix = Guid.NewGuid().ToString("N")[..10];
        long companyId = 0, safetyPackageId = 0, maintenancePackageId = 0;
        try
        {
            companyId = await db.InsertAsync(
                "INSERT INTO companies(company_code,name,industry,status,entitlement_policy_mode) VALUES (@code,'Allowlist package test','Logistics','Active','package_allowlist') RETURNING id",
                c => c.Parameters.AddWithValue("@code", $"APL-{suffix}"));
            safetyPackageId = await CreatePackageAsync(db, $"safety-{suffix}", "[\"safety\"]");
            maintenancePackageId = await CreatePackageAsync(db, $"maint-{suffix}", "[\"maintenance\"]");

            await PlatformEndpoints.ApplyAssignPackageAsync(db, companyId, safetyPackageId, 5, "policy-test", CancellationToken.None);
            var evaluator = new EntitlementService(db);
            Assert.True((await evaluator.CheckModuleAsync(companyId, "safety")).Allowed);
            Assert.False((await evaluator.CheckModuleAsync(companyId, "maintenance")).Allowed);

            await db.ExecuteAsync(
                "INSERT INTO tenant_entitlements(company_id,module_key,enabled,source) VALUES (@id,'integrations',true,'override')",
                c => c.Parameters.AddWithValue("@id", companyId));
            await PlatformEndpoints.ApplyAssignPackageAsync(db, companyId, maintenancePackageId, null, "policy-test", CancellationToken.None);

            Assert.False((await evaluator.CheckModuleAsync(companyId, "safety")).Allowed);
            Assert.True((await evaluator.CheckModuleAsync(companyId, "maintenance")).Allowed);
            Assert.True((await evaluator.CheckModuleAsync(companyId, "integrations")).Allowed);
            Assert.Equal(0, await db.ScalarLongAsync(
                "SELECT COUNT(*) FROM tenant_entitlements WHERE company_id=@id AND module_key='safety' AND source='package'",
                c => c.Parameters.AddWithValue("@id", companyId)));
        }
        finally
        {
            if (companyId > 0)
            {
                await db.ExecuteAsync("DELETE FROM tenant_entitlements WHERE company_id=@id", c => c.Parameters.AddWithValue("@id", companyId));
                await db.ExecuteAsync("DELETE FROM tenant_subscriptions WHERE company_id=@id", c => c.Parameters.AddWithValue("@id", companyId));
                await db.ExecuteAsync("DELETE FROM companies WHERE id=@id", c => c.Parameters.AddWithValue("@id", companyId));
            }
            foreach (var packageId in new[] { safetyPackageId, maintenancePackageId }.Where(id => id > 0))
                await db.ExecuteAsync("DELETE FROM packages WHERE id=@id", c => c.Parameters.AddWithValue("@id", packageId));
        }
    }

    private static Task<long> CreatePackageAsync(Database db, string code, string modules) =>
        db.InsertAsync(
            "INSERT INTO packages(package_code,name,module_keys,active) VALUES (@code,@code,@modules::jsonb,true) RETURNING id",
            c =>
            {
                c.Parameters.AddWithValue("@code", code);
                c.Parameters.AddWithValue("@modules", modules);
            });
}
