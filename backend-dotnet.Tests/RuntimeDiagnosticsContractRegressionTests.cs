namespace Opstrax.Tests;

public sealed class RuntimeDiagnosticsContractRegressionTests
{
    [Fact]
    public void Frontend_AcceptsTheCriticalWorkerAggregateReturnedByPublicReadiness()
    {
        var root = FindRoot();
        var frontend = File.ReadAllText(Path.Combine(root, "frontend", "src", "services", "runtimeDiagnostics.ts"));
        var program = File.ReadAllText(Path.Combine(root, "backend-dotnet", "Program.cs"));

        Assert.Contains("critical_worker_violations", frontend, StringComparison.Ordinal);
        Assert.Contains("critical_worker_startup_grace_active", frontend, StringComparison.Ordinal);
        Assert.Contains("apiClient.get(\"/health/ready\"", frontend, StringComparison.Ordinal);
        Assert.DoesNotContain("apiClient.get(\"/health/deep\"", frontend, StringComparison.Ordinal);
        Assert.Contains("state = \"Starting\"", frontend, StringComparison.Ordinal);
        Assert.Contains("critical_worker_violations", program, StringComparison.Ordinal);
        Assert.Contains("critical_worker_startup_grace_active", program, StringComparison.Ordinal);
        Assert.Contains("startupGraceActive ? \"starting\" : \"healthy\"", program, StringComparison.Ordinal);
    }

    private static string FindRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !Directory.Exists(Path.Combine(dir.FullName, "backend-dotnet")))
            dir = dir.Parent;
        return dir?.FullName ?? throw new DirectoryNotFoundException("Repository root not found.");
    }
}
