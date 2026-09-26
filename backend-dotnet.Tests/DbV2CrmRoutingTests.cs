namespace Opstrax.Tests;

public sealed class DbV2CrmRoutingTests
{
    [Fact]
    public void GovernedCrmRegistersReadFromCanonicalSecurityInvokerViews()
    {
        var source = Read("backend-dotnet", "Controllers", "DbV2CrmModuleRouting.cs");

        Assert.Contains("ModuleDefinitions[\"leads\"] = new(\"dbv2_leads_module_records\")", source, StringComparison.Ordinal);
        Assert.Contains("ModuleDefinitions[\"opportunities\"] = new(\"dbv2_opportunities_module_records\")", source, StringComparison.Ordinal);
        Assert.Contains("ModuleDefinitions[\"campaigns\"] = new(\"dbv2_campaigns_module_records\")", source, StringComparison.Ordinal);
        Assert.Contains("ModuleDefinitions[\"quotations\"] = new(\"dbv2_quotations_module_records\")", source, StringComparison.Ordinal);
        Assert.DoesNotContain("ModuleDefinitions[\"sales-pipeline\"]", source, StringComparison.Ordinal);
        Assert.DoesNotContain("ModuleDefinitions[\"predictive-margin\"]", source, StringComparison.Ordinal);
    }

    private static string Read(params string[] parts) => File.ReadAllText(Path.Combine([RepoRoot(), .. parts]));

    private static string RepoRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !Directory.Exists(Path.Combine(directory.FullName, "backend-dotnet")))
            directory = directory.Parent;
        Assert.NotNull(directory);
        return directory!.FullName;
    }
}
