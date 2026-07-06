using Opstrax.Api.Services;

namespace Opstrax.Tests;

public sealed class AuthenticationMfaTests
{
    [Fact]
    public void DisabledPolicy_DoesNotRequireMfa()
    {
        var settings = Settings(required: false, "Company Admin");

        Assert.False(SecuritySettingsService.IsMfaRequiredForRole(settings, "Company Admin"));
    }

    [Fact]
    public void EnabledPolicy_WithNoRoles_AppliesToEveryRole()
    {
        var settings = Settings(required: true);

        Assert.True(SecuritySettingsService.IsMfaRequiredForRole(settings, "Driver"));
    }

    [Theory]
    [InlineData("Company Admin", "company_admin")]
    [InlineData("GROUP-ADMIN", "Group Admin")]
    [InlineData(" manager ", "Manager")]
    public void ConfiguredRole_MatchesCaseAndSeparators(string configuredRole, string loginRole)
    {
        var settings = Settings(required: true, configuredRole);

        Assert.True(SecuritySettingsService.IsMfaRequiredForRole(settings, loginRole));
    }

    [Fact]
    public void UnconfiguredRole_IsNotBlockedByRoleScopedPolicy()
    {
        var settings = Settings(required: true, "Company Admin", "Manager");

        Assert.False(SecuritySettingsService.IsMfaRequiredForRole(settings, "Driver"));
    }

    [Fact]
    public void WildcardPolicy_AppliesToEveryRole()
    {
        var settings = Settings(required: true, "*");

        Assert.True(SecuritySettingsService.IsMfaRequiredForRole(settings, "Customer"));
    }

    [Fact]
    public void BlankConfiguredRoles_FailClosedAsTenantWidePolicy()
    {
        var settings = Settings(required: true, " ", "");

        Assert.True(SecuritySettingsService.IsMfaRequiredForRole(settings, "Driver"));
    }

    [Fact]
    public void LoginSource_RejectsMfaBeforeCreatingTokenOrSession()
    {
        var source = ReadSource("backend-dotnet", "Controllers", "EndpointMappings.cs");
        var loginStart = source.IndexOf("private static async Task<IResult> Login(", StringComparison.Ordinal);
        var loginEnd = source.IndexOf("private static string BearerToken", loginStart, StringComparison.Ordinal);
        var login = source[loginStart..loginEnd];

        var mfaGate = login.IndexOf("IsMfaRequiredForRole", StringComparison.Ordinal);
        var tokenCreation = login.IndexOf("var token =", StringComparison.Ordinal);
        var sessionInsert = login.IndexOf("INSERT INTO user_sessions", StringComparison.Ordinal);

        Assert.True(mfaGate >= 0, "login must enforce tenant MFA policy");
        Assert.True(mfaGate < tokenCreation, "MFA must be enforced before token creation");
        Assert.True(mfaGate < sessionInsert, "MFA must be enforced before session persistence");
        Assert.DoesNotContain("demo_password", login, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("demoPassword", login, StringComparison.Ordinal);
    }

    [Fact]
    public void ChangePasswordSource_DoesNotAcceptLegacyPlaintextCredential()
    {
        var source = ReadSource("backend-dotnet", "Controllers", "EndpointMappings.cs");
        var start = source.IndexOf("private static async Task<IResult> ChangePassword(", StringComparison.Ordinal);
        var end = source.IndexOf("private static async Task<IResult> CommandCenterSummary", start, StringComparison.Ordinal);
        var changePassword = source[start..end];

        Assert.DoesNotContain("SELECT password_hash, demo_password", changePassword, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("user[\"demoPassword\"]", changePassword, StringComparison.Ordinal);
        Assert.Contains("VerifyPasswordHash(current", changePassword, StringComparison.Ordinal);
    }

    [Fact]
    public void AdminUserManagement_NeverStoresOrReturnsDemoPassword()
    {
        var source = ReadSource("backend-dotnet", "Controllers", "EndpointMappings.cs");

        Assert.DoesNotContain("@demoPassword", source, StringComparison.Ordinal);
        Assert.DoesNotContain("demo_password demoPassword", source, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("u.demo_password", source, StringComparison.OrdinalIgnoreCase);
    }

    private static SecuritySettings Settings(bool required, params string[] roles) => new()
    {
        CompanyId = 7,
        MfaRequired = required,
        MfaRequiredRoles = roles,
    };

    private static string ReadSource(params string[] parts)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !Directory.Exists(Path.Combine(dir.FullName, "backend-dotnet")))
            dir = dir.Parent;

        Assert.NotNull(dir);
        return File.ReadAllText(Path.Combine([dir!.FullName, .. parts]));
    }
}
