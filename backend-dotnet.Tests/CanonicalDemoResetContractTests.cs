using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.FileProviders;
using Opstrax.Api.Controllers;
using Xunit;

namespace Opstrax.Tests;

public sealed class CanonicalDemoResetContractTests
{
    [Theory]
    [InlineData("Production", true, false)]
    [InlineData("Staging", true, false)]
    [InlineData("Demo", true, false)]
    [InlineData("Development", false, false)]
    [InlineData("Development", true, true)]
    public void ResetAvailability_IsDevelopmentOnly_AndExplicitlyOptedIn(
        string environmentName, bool resetEnabled, bool expected)
    {
        var environment = new TestEnvironment(environmentName);
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(
            new Dictionary<string, string?>
            {
                ["DemoSeed:ResetEnabled"] = resetEnabled.ToString(),
            }).Build();

        Assert.Equal(expected, CanonicalDemoResetEndpoints.IsEnabled(environment, configuration));
    }

    [Fact]
    public void ResetContract_IsCanonicalPlatformAuthorizedConfirmedAndAudited()
    {
        var root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../.."));
        var endpoint = File.ReadAllText(Path.Combine(root,
            "backend-dotnet/Controllers/CanonicalDemoResetEndpoints.cs"));
        var mapper = File.ReadAllText(Path.Combine(root,
            "backend-dotnet/Controllers/DevSeedEndpoints.cs"));
        var program = File.ReadAllText(Path.Combine(root, "backend-dotnet/Program.cs"));

        Assert.Contains("/api/platform/dev/reset-canonical-demo", endpoint, StringComparison.Ordinal);
        Assert.Contains("if (!app.Environment.IsDevelopment()) return", endpoint, StringComparison.Ordinal);
        Assert.Contains("DemoSeed:ResetEnabled", endpoint, StringComparison.Ordinal);
        Assert.Contains("platform:tenants:offboard", endpoint, StringComparison.Ordinal);
        Assert.Contains("RESET MERIDIAN-DEMO", endpoint, StringComparison.Ordinal);
        Assert.Contains("DemoTenantSeeder.DemoCompanyCode", endpoint, StringComparison.Ordinal);
        Assert.Contains("offboarding.DeleteTenantAsync", endpoint, StringComparison.Ordinal);
        Assert.Contains("seeder.SeedAsync(ct)", endpoint, StringComparison.Ordinal);
        Assert.Contains("demo.fixture.reset.started", endpoint, StringComparison.Ordinal);
        Assert.Contains("demo.fixture.reset.completed", endpoint, StringComparison.Ordinal);
        Assert.Contains("demo.fixture.reset.failed", endpoint, StringComparison.Ordinal);
        Assert.Contains("app.MapCanonicalDemoResetEndpoints()", mapper, StringComparison.Ordinal);
        Assert.Contains("if (!app.Environment.IsDevelopment())", mapper, StringComparison.Ordinal);
        Assert.Contains("path.StartsWith(\"/api/platform\"", program, StringComparison.Ordinal);
    }

    private sealed class TestEnvironment(string environmentName) : IWebHostEnvironment
    {
        public string EnvironmentName { get; set; } = environmentName;
        public string ApplicationName { get; set; } = "Opstrax.Tests";
        public string WebRootPath { get; set; } = string.Empty;
        public IFileProvider WebRootFileProvider { get; set; } = new NullFileProvider();
        public string ContentRootPath { get; set; } = string.Empty;
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }
}
