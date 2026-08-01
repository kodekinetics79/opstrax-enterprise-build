namespace Opstrax.Tests;

public sealed class FleetRuntimeRouteContractRegressionTests
{
    [Fact]
    public void Stage55PackagesEveryRuntimeOnlyFleetRouteColumn()
    {
        var sql = Read("database", "migrations", "2026_07_30_stage55_fleet_runtime_route_contract.sql");
        foreach (var fragment in new[]
        {
            "ALTER TABLE companies", "ADD COLUMN IF NOT EXISTS country", "ADD COLUMN IF NOT EXISTS currency",
            "CREATE TABLE IF NOT EXISTS authorization_decision_logs", "GRANT SELECT,INSERT ON authorization_decision_logs",
            "ADD COLUMN IF NOT EXISTS carrier_number", "ADD COLUMN IF NOT EXISTS compliance_status",
            "ADD COLUMN IF NOT EXISTS deleted_at", "ADD COLUMN IF NOT EXISTS source_event_id",
            "ADD COLUMN IF NOT EXISTS source TEXT", "ADD COLUMN IF NOT EXISTS provider TEXT",
            "ADD COLUMN IF NOT EXISTS device_fix_time", "ADD COLUMN IF NOT EXISTS quality_flags",
            "ALTER TABLE location_events", "ADD COLUMN IF NOT EXISTS source VARCHAR(40)",
            "ADD COLUMN IF NOT EXISTS source_channel", "2026_07_30_stage55_fleet_runtime_route_contract"
        }) Assert.Contains(fragment, sql);
    }

    [Fact]
    public void FleetTrackingAndCarrierRoutesDependOnlyOnStage55PackagedColumns()
    {
        var endpoints = Read("backend-dotnet", "Controllers", "FleetTmsEndpoints.cs");
        var sql = Read("database", "migrations", "2026_07_30_stage55_fleet_runtime_route_contract.sql");
        foreach (var column in new[] { "carrier_number", "compliance_status", "deleted_at", "source_event_id", "device_fix_time", "provider", "source_channel" })
        {
            Assert.Contains(column, endpoints);
            Assert.Contains(column, sql);
        }
    }

    [Fact]
    public void ProductionReadinessRequiresStage55AndEveryRouteColumn()
    {
        var source = Read("backend-dotnet", "Services", "FleetProductionReadinessService.cs");
        Assert.Contains("runtime_route_columns", source);
        Assert.Contains("runtime_route_column_violations", source);
        Assert.Contains("runtime_route_object_violations", source);
        Assert.Contains("2026_07_30_stage55_fleet_runtime_route_contract", source);
    }

    [Fact]
    public void Stage55RepairsAuthorizationPolicyAndPrivilegeDriftExactly()
    {
        var sql = Read("database", "migrations", "2026_07_30_stage55_fleet_runtime_route_contract.sql");
        Assert.Contains("SELECT policyname FROM pg_policies", sql);
        Assert.Contains("DROP POLICY %I ON public.authorization_decision_logs", sql);
        Assert.Contains("tablename='authorization_decision_logs')<>2", sql);
        Assert.Contains("policyname='tenant_isolation'", sql);
        Assert.Contains("policyname='platform_admin_bypass'", sql);
        Assert.Contains("REVOKE ALL PRIVILEGES ON authorization_decision_logs", sql);
        Assert.Contains("has_sequence_privilege", sql);
    }

    private static string Read(params string[] parts)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !Directory.Exists(Path.Combine(directory.FullName,"backend-dotnet")))
            directory=directory.Parent;
        Assert.NotNull(directory);
        return File.ReadAllText(Path.Combine(new[] { directory.FullName }.Concat(parts).ToArray()));
    }
}
