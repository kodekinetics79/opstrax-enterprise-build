namespace Opstrax.Tests;

public sealed class DeepHealthSystemLaneRegressionTests
{
    [Fact]
    public void DeepHealth_UsesExplicitSystemLane_AndExposesTerminalMigrationChecks()
    {
        var source = File.ReadAllText(Path.Combine(FindRoot(), "backend-dotnet", "Program.cs"));
        var start = source.IndexOf("app.MapGet(\"/health/deep\"", StringComparison.Ordinal);
        var end = source.IndexOf("app.MapOpsTraxEndpoints();", start, StringComparison.Ordinal);
        Assert.True(start >= 0 && end > start);
        var deepHealth = source[start..end];

        Assert.Contains("db.RunInSystemScopeAsync(() => db.QueryAsync(", deepHealth, StringComparison.Ordinal);
        Assert.Contains("FROM service_heartbeats ORDER BY service_name", deepHealth, StringComparison.Ordinal);
        Assert.Contains("CriticalWorkerNames", deepHealth, StringComparison.Ordinal);
        Assert.Contains("critical_worker_contract", deepHealth, StringComparison.Ordinal);
        Assert.Contains("heartbeat_ledger_unavailable", deepHealth, StringComparison.Ordinal);
        Assert.Contains("servicesDegraded", deepHealth, StringComparison.Ordinal);
        Assert.Contains("overallStatus == \"healthy\" ? StatusCodes.Status200OK : StatusCodes.Status503ServiceUnavailable", deepHealth, StringComparison.Ordinal);
        Assert.Contains("tenant_ticket_migration_applied", deepHealth, StringComparison.Ordinal);
        Assert.Contains("data_protection_key_ring_migration_applied", deepHealth, StringComparison.Ordinal);
        Assert.True(source.Split("tenant_ticket_migration_applied", StringSplitOptions.None).Length - 1 >= 2);
        Assert.True(source.Split("data_protection_key_ring_migration_applied", StringSplitOptions.None).Length - 1 >= 2);
    }

    private static string FindRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !Directory.Exists(Path.Combine(dir.FullName, "backend-dotnet")))
            dir = dir.Parent;
        return dir?.FullName ?? throw new DirectoryNotFoundException("Repository root not found.");
    }
}
