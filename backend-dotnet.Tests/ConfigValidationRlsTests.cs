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
    public void Validate_RenderDualConnections_PassDatabaseChecks()
    {
        var values = new Dictionary<string, string?>
        {
            ["ASPNETCORE_ENVIRONMENT"] = "Production",
            ["Jwt:Key"] = new string('j', 64),
            ["PG_CONNECTION_APP"] = "Host=db.example.test;Database=opstrax;Username=opstrax_app;Password=secret",
            ["PG_CONNECTION_SYSTEM"] = "Host=db.example.test;Database=opstrax;Username=opstrax_system;Password=secret2",
            ["Platform:SuperAdminPassword"] = "LocalTestPassword!123",
            ["Cors:AllowedOrigins"] = "https://app.example.test",
            ["Rls:EnforceTenantContext"] = "true",
            ["DATA_ENCRYPTION_KEY"] = "AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA=",
            ["DATA_PROTECTION_CERTIFICATE_BASE64"] = "test-certificate-payload",
            ["DATA_PROTECTION_CERTIFICATE_PASSWORD"] = "test-password-strong-123",
            ["RetentionWorker:Enabled"] = "true",
        };
        var config = new ConfigurationBuilder().AddInMemoryCollection(values).Build();

        var result = new ConfigValidationService(config).Validate();

        Assert.All(result.Issues.Where(i => i.Check.StartsWith("database_", StringComparison.Ordinal)),
            issue => Assert.Equal("pass", issue.Level));
        Assert.Equal(0, result.FailCount);
    }

    [Fact]
    public void Validate_ProductionRlsWithoutSystemConnection_FailsClosed()
    {
        var result = Validate("Production", "true", includeSystem: false);
        Assert.Equal("fail", Assert.Single(result.Issues,
            i => i.Check == "database_system_connection").Level);
    }

    [Fact]
    public void Validate_ProductionRlsWithAliasedIdentities_FailsClosed()
    {
        var result = Validate("Production", "true", aliasSystem: true);
        Assert.Equal("fail", Assert.Single(result.Issues,
            i => i.Check == "database_identity_separation").Level);
    }

    [Fact]
    public void Validate_ProductionRlsWithSharedPassword_FailsClosed()
    {
        var values = BaseValues("Production", "true");
        values["ConnectionStrings:SystemConnection"] =
            "Host=localhost;Database=opstrax;Username=opstrax_system;Password=app-test";
        var result = new ConfigValidationService(
            new ConfigurationBuilder().AddInMemoryCollection(values).Build()).Validate();
        Assert.Equal("fail", Assert.Single(result.Issues,
            i => i.Check == "database_identity_separation").Level);
    }

    [Fact]
    public void Validate_ProductionWithTelemetrySimulator_FailsClosed()
    {
        var values = BaseValues("Production", "true");
        values["Telemetry:Simulator:Enabled"] = "true";
        var result = new ConfigValidationService(
            new ConfigurationBuilder().AddInMemoryCollection(values).Build()).Validate();

        Assert.Equal("fail", Assert.Single(result.Issues,
            i => i.Check == "telemetry_simulator").Level);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("false")]
    [InlineData("not-a-boolean")]
    public void Validate_ProductionWithoutExplicitRetentionExecutor_FailsClosed(string? setting)
    {
        var values = BaseValues("Production", "true");
        values["RetentionWorker:Enabled"] = setting;
        var result = new ConfigValidationService(
            new ConfigurationBuilder().AddInMemoryCollection(values).Build()).Validate();

        var issue = Assert.Single(result.Issues, i => i.Check == "retention_worker");
        Assert.Equal("fail", issue.Level);
        Assert.Contains("explicitly true", issue.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Validate_ProductionWithExplicitRetentionExecutor_Passes()
    {
        var result = Validate("Production", "true");

        Assert.Equal("pass", Assert.Single(result.Issues,
            i => i.Check == "retention_worker").Level);
    }

    [Fact]
    public void Validate_ProductionWithLegacyGatewaySecret_FailsClosed()
    {
        var values = BaseValues("Production", "true");
        values["Telemetry:GatewaySecret"] = new string('g', 40);
        var result = new ConfigValidationService(
            new ConfigurationBuilder().AddInMemoryCollection(values).Build()).Validate();

        Assert.Equal("fail", Assert.Single(result.Issues,
            i => i.Check == "legacy_telemetry_gateway_secret").Level);
    }

    [Fact]
    public void Validate_ProductionWithLegacyDeviceSecretRead_FailsClosed()
    {
        var values = BaseValues("Production", "true");
        values[DeviceHmacSecretProtection.LegacyReadSetting] = "true";
        var result = new ConfigValidationService(
            new ConfigurationBuilder().AddInMemoryCollection(values).Build()).Validate();

        Assert.Equal("fail", Assert.Single(result.Issues,
            i => i.Check == "legacy_device_hmac_read").Level);
    }

    [Fact]
    public void Validate_ProductionWithoutDeviceEncryptionKey_FailsClosed()
    {
        var values = BaseValues("Production", "true");
        values["DATA_ENCRYPTION_KEY"] = null;
        var result = new ConfigValidationService(
            new ConfigurationBuilder().AddInMemoryCollection(values).Build()).Validate();

        Assert.Equal("fail", Assert.Single(result.Issues,
            i => i.Check == "device_hmac_encryption").Level);
        Assert.Equal("fail", Assert.Single(result.Issues,
            i => i.Check == "connector_secret_encryption").Level);
    }

    [Fact]
    public void Validate_StagingWithoutEncryptionKey_FailsDeviceAndConnectorChecks()
    {
        var values = BaseValues("Staging", "false");
        values["DATA_ENCRYPTION_KEY"] = null;
        var result = new ConfigValidationService(
            new ConfigurationBuilder().AddInMemoryCollection(values).Build()).Validate();

        Assert.Equal("fail", Assert.Single(result.Issues,
            i => i.Check == "device_hmac_encryption").Level);
        Assert.Equal("fail", Assert.Single(result.Issues,
            i => i.Check == "connector_secret_encryption").Level);
    }

    [Fact]
    public void Validate_StagingWithLegacyDeviceSecretRead_FailsClosed()
    {
        var values = BaseValues("Staging", "false");
        values[DeviceHmacSecretProtection.LegacyReadSetting] = "true";
        var result = new ConfigValidationService(
            new ConfigurationBuilder().AddInMemoryCollection(values).Build()).Validate();

        Assert.Equal("fail", Assert.Single(result.Issues,
            i => i.Check == "legacy_device_hmac_read").Level);
    }

    [Theory]
    [InlineData("4")]
    [InlineData("301")]
    public void Validate_ProductionRlsWithOutOfRangeTicketTtl_Fails(string ttl)
    {
        var result = Validate("Production", "true", ticketTtl: ttl);
        Assert.Equal("fail", Assert.Single(result.Issues,
            i => i.Check == "tenant_ticket_ttl").Level);
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

    private static ConfigCheckResult Validate(
        string environment,
        string? rlsValue,
        bool includeSystem = true,
        bool aliasSystem = false,
        string? ticketTtl = null)
    {
        var values = BaseValues(environment, rlsValue);
        values["ConnectionStrings:SystemConnection"] = includeSystem
                ? aliasSystem
                    ? "Host=localhost;Database=opstrax;Username=opstrax_app;Password=app-test"
                    : "Host=localhost;Database=opstrax;Username=opstrax_system;Password=system-test"
                : null;
        values["Rls:TenantTicketTtlSeconds"] = ticketTtl;
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(values)
            .Build();

        return new ConfigValidationService(config).Validate();
    }

    private static Dictionary<string, string?> BaseValues(string environment, string? rlsValue) => new()
        {
            ["ASPNETCORE_ENVIRONMENT"] = environment,
            ["Jwt:Key"] = new string('j', 64),
            ["ConnectionStrings:DefaultConnection"] =
                "Host=localhost;Database=opstrax;Username=opstrax_app;Password=app-test",
            ["ConnectionStrings:SystemConnection"] =
                "Host=localhost;Database=opstrax;Username=opstrax_system;Password=system-test",
            ["Platform:SuperAdminPassword"] = "LocalTestPassword!123",
            ["Cors:AllowedOrigins"] = "https://app.example.test",
            ["Rls:EnforceTenantContext"] = rlsValue,
            ["DATA_ENCRYPTION_KEY"] = "AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA=",
            ["DATA_PROTECTION_CERTIFICATE_BASE64"] = "test-certificate-payload",
            ["DATA_PROTECTION_CERTIFICATE_PASSWORD"] = "test-password-strong-123",
            ["RetentionWorker:Enabled"] = "true",
        };

    [Fact]
    public void Validate_ProductionWithoutPersistentDataProtection_FailsClosed()
    {
        var values = BaseValues("Production", "true");
        values["DATA_PROTECTION_CERTIFICATE_BASE64"] = null;
        values["DATA_PROTECTION_CERTIFICATE_PASSWORD"] = null;
        var result = new ConfigValidationService(
            new ConfigurationBuilder().AddInMemoryCollection(values).Build()).Validate();
        Assert.Equal("fail", Assert.Single(result.Issues,
            i => i.Check == "data_protection_key_ring").Level);
    }
}
