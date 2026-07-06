using Microsoft.Extensions.Configuration;
using Opstrax.Api.Services;

namespace Opstrax.Tests;

public sealed class ConfigValidationRlsTests
{
    [Theory]
    [InlineData(null)]
    [InlineData("false")]
    public void Validate_ProductionWithoutRlsEnforcement_Fails(string? rlsValue)
    {
        var result = Validate("Production", rlsValue);

        var issue = Assert.Single(result.Issues, i => i.Check == "tenant_rls_enforcement");
        Assert.Equal("fail", issue.Level);
        Assert.Equal("invalid", result.Status);
    }

    [Fact]
    public void Validate_ProductionWithRlsEnforcement_PassesRlsCheck()
    {
        var result = Validate("Production", "true");

        var issue = Assert.Single(result.Issues, i => i.Check == "tenant_rls_enforcement");
        Assert.Equal("pass", issue.Level);
    }

    [Fact]
    public void Validate_RenderPgConnection_PassesDatabaseCheck()
    {
        var values = new Dictionary<string, string?>
        {
            ["ASPNETCORE_ENVIRONMENT"] = "Production",
            ["Jwt:Key"] = new string('j', 64),
            ["PG_CONNECTION"] = "postgresql://app:secret@db.example.test/opstrax?sslmode=require",
            ["Platform:SuperAdminPassword"] = "LocalTestPassword!123",
            ["Cors:AllowedOrigins"] = "https://app.example.test",
            ["Rls:EnforceTenantContext"] = "true",
        };
        var config = new ConfigurationBuilder().AddInMemoryCollection(values).Build();

        var result = new ConfigValidationService(config).Validate();

        var issue = Assert.Single(result.Issues, i => i.Check == "database_connection");
        Assert.Equal("pass", issue.Level);
        Assert.Equal(0, result.FailCount);
    }

    [Fact]
    public void ProductionContainer_EnablesTenantRlsByDefault()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !Directory.Exists(Path.Combine(dir.FullName, "backend-dotnet")))
            dir = dir.Parent;

        Assert.NotNull(dir);
        var dockerfile = File.ReadAllText(Path.Combine(dir!.FullName, "backend-dotnet", "Dockerfile"));
        Assert.Contains("ENV Rls__EnforceTenantContext=true", dockerfile, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("false")]
    public void Validate_NonProductionWithoutRlsEnforcement_Warns(string? rlsValue)
    {
        var result = Validate("Development", rlsValue);

        var issue = Assert.Single(result.Issues, i => i.Check == "tenant_rls_enforcement");
        Assert.Equal("warn", issue.Level);
    }

    [Fact]
    public void EnsureStartupAllowed_ProductionConfigFailure_Throws()
    {
        var result = Validate("Production", null);

        var exception = Assert.Throws<InvalidOperationException>(
            () => ConfigValidationService.EnsureStartupAllowed(result, isProduction: true));

        Assert.Contains("Refusing to start", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void EnsureStartupAllowed_ProductionWithRlsEnforcement_DoesNotThrow()
    {
        var result = Validate("Production", "true");

        Assert.Equal(0, result.FailCount);
        ConfigValidationService.EnsureStartupAllowed(result, isProduction: true);
    }

    [Fact]
    public void EnsureStartupAllowed_NonProductionConfigFailure_DoesNotThrow()
    {
        var result = new ConfigCheckResult(
            "invalid",
            FailCount: 1,
            WarnCount: 0,
            [new ConfigIssue("example", "fail", "Test failure")]);

        ConfigValidationService.EnsureStartupAllowed(result, isProduction: false);
    }

    private static ConfigCheckResult Validate(string environment, string? rlsValue)
    {
        var values = new Dictionary<string, string?>
        {
            ["ASPNETCORE_ENVIRONMENT"] = environment,
            ["Jwt:Key"] = new string('j', 64),
            ["ConnectionStrings:DefaultConnection"] = "Host=localhost;Database=opstrax",
            ["Platform:SuperAdminPassword"] = "LocalTestPassword!123",
            ["Cors:AllowedOrigins"] = "https://app.example.test",
            ["Rls:EnforceTenantContext"] = rlsValue,
        };
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(values)
            .Build();

        return new ConfigValidationService(config).Validate();
    }
}
