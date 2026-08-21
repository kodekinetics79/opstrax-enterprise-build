namespace Opstrax.Tests;

public sealed class RuntimeDiagnosticsContractRegressionTests
{
    [Fact]
    public void Frontend_AcceptsTheHealthyCriticalWorkerContractReturnedByDeepHealth()
    {
        var root = FindRoot();
        var frontend = File.ReadAllText(Path.Combine(root, "frontend", "src", "services", "runtimeDiagnostics.ts"));
        var program = File.ReadAllText(Path.Combine(root, "backend-dotnet", "Program.cs"));

        Assert.Contains("workerContractStatus === \"healthy\"", frontend, StringComparison.Ordinal);
        Assert.DoesNotContain("workerContract.status).toLowerCase() === \"valid\"", frontend, StringComparison.Ordinal);
        Assert.Contains("workerContractStatus === \"starting\"", frontend, StringComparison.Ordinal);
        Assert.Contains("state = \"Starting\"", frontend, StringComparison.Ordinal);
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
