namespace Opstrax.Tests;

public sealed class TenantProvisioningMigrationContractTests
{
    [Fact]
    public void ProtectedOwnerMigrationCoversEveryTenantCreateSchemaDependency()
    {
        var migration = Read("database", "migrations", "2026_08_13_stage79_tenant_provisioning_runtime_contract.sql");
        foreach (var required in new[]
        {
            "legal_name", "website", "fleet_size", "tax_id",
            "primary_contact_name", "primary_contact_email", "primary_contact_phone", "billing_email",
            "billing_cycle", "CREATE TABLE IF NOT EXISTS password_reset_tokens",
            "CREATE TABLE IF NOT EXISTS feature_flags", "ux_users_company_email_ci",
            "REVOKE ALL ON TABLE password_reset_tokens, feature_flags FROM PUBLIC",
            "2026_08_13_stage79_tenant_provisioning_runtime_contract",
        })
            Assert.Contains(required, migration, StringComparison.Ordinal);

        Assert.DoesNotContain("app.current_tenant_id", migration, StringComparison.Ordinal);
        Assert.DoesNotContain("platform_admin_bypass", migration, StringComparison.Ordinal);

        var runner = Read("tools", "apply-neon-predeploy-migrations.sh");
        Assert.Contains("2026_08_13_stage79_tenant_provisioning_runtime_contract", runner, StringComparison.Ordinal);
        Assert.Contains("Stage79 tenant-provisioning runtime contract is incomplete", runner, StringComparison.Ordinal);

        var readiness = Read("backend-dotnet", "Services", "FleetProductionReadinessService.cs");
        Assert.Contains("tenant_provisioning_ready", readiness, StringComparison.Ordinal);
        Assert.Contains("TenantProvisioningReady", readiness, StringComparison.Ordinal);
        Assert.Contains("('password_reset_tokens',true),('feature_flags',true)", readiness, StringComparison.Ordinal);

        var program = Read("backend-dotnet", "Program.cs");
        Assert.Contains("tenant_provisioning_ready = fleetResult.TenantProvisioningReady", program, StringComparison.Ordinal);
    }

    private static string Read(params string[] parts)
    {
        var root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../"));
        return File.ReadAllText(Path.Combine([root, .. parts]));
    }
}
