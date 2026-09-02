using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Opstrax.Api.Controllers;

namespace Opstrax.Tests;

public sealed class ProductPilotHarnessTests
{
    [Fact]
    public void Gate_DefaultsFailClosed() =>
        Assert.False(ProductPilotEndpoints.IsAvailable(Host(Environments.Production), new ConfigurationBuilder().Build()));

    [Theory]
    [InlineData("Production", true, "staging", "CERT-LARGE-20260825")]
    [InlineData("Development", true, "staging", "CERT-LARGE-20260825")]
    [InlineData("Staging", false, "staging", "CERT-LARGE-20260825")]
    [InlineData("Staging", true, "production", "CERT-LARGE-20260825")]
    [InlineData("Staging", true, "staging", "ANOTHER-TENANT")]
    [InlineData("Staging", true, null, "CERT-LARGE-20260825")]
    public void Gate_RejectsAnyIncompleteOrNonStagingConfiguration(string hostEnvironment, bool enabled, string? stage, string tenantCode)
    {
        var config = Config(enabled, stage, tenantCode);
        Assert.False(ProductPilotEndpoints.IsAvailable(Host(hostEnvironment), config));
    }

    [Fact]
    public void Gate_RequiresAllExactStagingControls()
    {
        var config = Config(true, "staging", ProductPilotEndpoints.CertificationTenantCode);
        Assert.True(ProductPilotEndpoints.IsAvailable(Host(Environments.Staging), config));
    }

    [Fact]
    public void HarnessContract_ContainsNoSeederTelemetryOrBusinessRecordInsert()
    {
        var source = Read("backend-dotnet", "Controllers", "ProductPilotEndpoints.cs");
        Assert.DoesNotContain("DemoTenantSeeder", source, StringComparison.Ordinal);
        Assert.DoesNotContain("TelemetrySimulator", source, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("TelemetryIngest", source, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("INSERT INTO customers", source, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("INSERT INTO jobs", source, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("INSERT INTO routes", source, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("platform:pilot:run", source, StringComparison.Ordinal);
        Assert.Contains("FOR UPDATE", source, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("RunInSystemTransactionAsync", source, StringComparison.Ordinal);
        Assert.Contains("WHERE c.name=@identifier", source, StringComparison.Ordinal);
        Assert.Contains("(SELECT COUNT(*) FROM companies WHERE name=@identifier)=1", source, StringComparison.Ordinal);
        Assert.DoesNotContain("WHERE company_code=@code", source, StringComparison.Ordinal);
        Assert.Contains("Opstrax.Api.Observability.BuildInfo.Version", source, StringComparison.Ordinal);
        Assert.DoesNotContain("configuration[\"OPSTRAX_DEPLOY_VERSION\"]", source, StringComparison.Ordinal);

        var migration = Read("database", "migrations", "2026_08_26_stage90_product_pilot_permission.sql");
        Assert.Contains("platform:pilot:run", migration, StringComparison.Ordinal);
        Assert.Contains("ux_platform_audit_product_pilot_request", migration, StringComparison.Ordinal);
        Assert.Contains("details_json->>'requestId'", migration, StringComparison.Ordinal);

        var runner = Read("tools", "apply-neon-predeploy-migrations.sh");
        Assert.Contains("2026_08_26_stage90_product_pilot_permission", runner, StringComparison.Ordinal);
    }

    private static IConfiguration Config(bool enabled, string? stage, string tenantCode) =>
        new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["ProductPilot:Enabled"] = enabled.ToString(),
            ["ProductPilot:DeploymentStage"] = stage,
            ["ProductPilot:TenantCode"] = tenantCode,
        }).Build();

    private static IHostEnvironment Host(string name) => new TestHostEnvironment { EnvironmentName = name };

    private sealed class TestHostEnvironment : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = Environments.Production;
        public string ApplicationName { get; set; } = "Opstrax.Tests";
        public string ContentRootPath { get; set; } = Directory.GetCurrentDirectory();
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }

    private static string Read(params string[] parts)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !Directory.Exists(Path.Combine(directory.FullName, "backend-dotnet")))
            directory = directory.Parent;
        Assert.NotNull(directory);
        return File.ReadAllText(Path.Combine(new[] { directory.FullName }.Concat(parts).ToArray()));
    }
}
