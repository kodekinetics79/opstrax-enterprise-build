namespace Opstrax.Tests;

public sealed class MobilePilotIdentityContractTests
{
    [Fact]
    public void PasswordLogin_RequiresTenantCodeAndScopesTheUserLookup()
    {
        var endpoints = ReadSource("backend-dotnet", "Controllers", "EndpointMappings.cs");

        Assert.Contains("LoginRequest(string Email, string Password, string CompanyCode)", endpoints, StringComparison.Ordinal);
        Assert.Contains("LOWER(c.company_code)=LOWER(@companyCode)", endpoints, StringComparison.Ordinal);
        Assert.Contains("cmd.Parameters.AddWithValue(\"@companyCode\", request.CompanyCode.Trim())", endpoints, StringComparison.Ordinal);

        var migration = ReadSource("database", "migrations", "2026_08_13_stage79_tenant_provisioning_runtime_contract.sql");
        Assert.Contains("GROUP BY company_id, lower(email)", migration, StringComparison.Ordinal);
        Assert.Contains("HAVING count(*) > 1", migration, StringComparison.Ordinal);
        Assert.DoesNotContain("count(DISTINCT company_id) > 1", migration, StringComparison.Ordinal);
    }

    [Fact]
    public void WebPasswordLogin_CollectsAndSendsTenantCode()
    {
        var authApi = ReadSource("frontend", "src", "services", "authApi.ts");
        var loginPage = ReadSource("frontend", "src", "pages", "LoginPage.tsx");

        Assert.Contains("login: async (usernameOrEmail: string, password: string, companyCode: string)", authApi, StringComparison.Ordinal);
        Assert.Contains("{ email, password, companyCode: companyCode.trim() }", authApi, StringComparison.Ordinal);
        Assert.Contains("Organization code", loginPage, StringComparison.Ordinal);
        Assert.Contains("authApi.login(e, p, code)", loginPage, StringComparison.Ordinal);
        Assert.Contains("organization code, email, or password was not recognized", loginPage, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void SsoDiscovery_UsesTenantCodeToDisambiguateSharedEmailDomains()
    {
        var endpoints = ReadSource("backend-dotnet", "Controllers", "EndpointMappings.cs");
        var authApi = ReadSource("frontend", "src", "services", "authApi.ts");

        Assert.Contains("SsoDiscoverRequest(string? Email, string? CompanyCode)", endpoints, StringComparison.Ordinal);
        Assert.Contains("lower(c.company_code) = lower(@companyCode)", endpoints, StringComparison.Ordinal);
        Assert.Contains("cmd.Parameters.AddWithValue(\"@companyCode\", companyCode)", endpoints, StringComparison.Ordinal);
        Assert.Contains("ssoDiscover: async (email: string, companyCode: string)", authApi, StringComparison.Ordinal);
        Assert.Contains("companyCode: companyCode.trim()", authApi, StringComparison.Ordinal);
    }

    [Fact]
    public void DriverCurrentAssignment_ReturnsTheDispatchedVehicleIdentity()
    {
        var endpoints = ReadSource("backend-dotnet", "Controllers", "EndpointMappings.cs");
        var handlerStart = endpoints.IndexOf("private static async Task<IResult> DriverCurrentAssignment", StringComparison.Ordinal);
        Assert.True(handlerStart >= 0);
        var handler = endpoints[handlerStart..Math.Min(endpoints.Length, handlerStart + 5000)];

        Assert.Contains("da.vehicle_id", handler, StringComparison.Ordinal);
        Assert.Contains("da.driver_id=@did AND da.company_id=@cid", handler, StringComparison.Ordinal);
    }

    private static string ReadSource(params string[] parts)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !Directory.Exists(Path.Combine(dir.FullName, "backend-dotnet")))
            dir = dir.Parent;

        Assert.NotNull(dir);
        return File.ReadAllText(Path.Combine([dir!.FullName, .. parts]));
    }
}
