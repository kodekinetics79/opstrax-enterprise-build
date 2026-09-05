using Xunit;

namespace Opstrax.Tests;

public class MobilePushContractTests
{
    private static string RepoFile(params string[] parts)
        => Path.GetFullPath(Path.Combine(new[] { AppContext.BaseDirectory, "../../../../" }.Concat(parts).ToArray()));

    [Fact]
    public async Task Stage101_IsForceRls_AndRestrictedRuntimeScoped()
    {
        var path = RepoFile("database", "migrations", "2026_09_05_stage101_mobile_push_tokens.sql");
        Assert.True(File.Exists(path), $"Missing Stage101 migration: {path}");
        var sql = await File.ReadAllTextAsync(path);

        Assert.Contains("CREATE TABLE IF NOT EXISTS mobile_device_tokens", sql);
        Assert.Contains("ENABLE ROW LEVEL SECURITY", sql);
        Assert.Contains("FORCE ROW LEVEL SECURITY", sql);
        Assert.Contains("company_id=(SELECT opstrax_security.current_tenant_id())", sql);
        Assert.Contains("TO opstrax_app", sql);
        Assert.Contains("TO opstrax_system", sql);
        Assert.Contains("REVOKE ALL ON TABLE mobile_device_tokens FROM PUBLIC", sql);
        Assert.Contains("UNIQUE (company_id, token_fingerprint)", sql);
        Assert.Contains("status IN ('active','revoked')", sql);
    }

    [Fact]
    public async Task Registration_BindsTenantAndUserOnlyFromAuthenticatedContext()
    {
        var path = RepoFile("backend-dotnet", "Controllers", "MobileDeviceEndpoints.cs");
        var source = await File.ReadAllTextAsync(path);

        Assert.Contains("AuthCompanyIdItemKey", source);
        Assert.Contains("AuthUserIdItemKey", source);
        Assert.Contains("AuthPermissionsItemKey", source);
        Assert.DoesNotContain("Str(body, \"companyId\")", source);
        Assert.DoesNotContain("Str(body, \"company_id\")", source);
        Assert.DoesNotContain("Str(body, \"userId\")", source);
        Assert.DoesNotContain("Str(body, \"user_id\")", source);
        Assert.Contains("ProductAllowed(http, product)", source);
        Assert.Contains("SHA256.HashData", source);
    }

    [Fact]
    public async Task Registration_DoesNotReturnRawPushToken_AndRevocationIsUserScoped()
    {
        var path = RepoFile("backend-dotnet", "Controllers", "MobileDeviceEndpoints.cs");
        var source = await File.ReadAllTextAsync(path);

        Assert.Contains("WHERE company_id=@company AND user_id=@user AND token_fingerprint=@fingerprint", source);
        Assert.Contains("LEFT(token_fingerprint,8) AS token_fingerprint_prefix", source);
        Assert.DoesNotContain("pushToken =", source);
        Assert.DoesNotContain("token = row", source);
        Assert.Contains("status='revoked'", source);
        Assert.Contains("revoked_at=NOW()", source);
    }

    [Fact]
    public async Task ExpoTokenValidation_IsBoundedAndProviderSpecific()
    {
        var path = RepoFile("backend-dotnet", "Controllers", "MobileDeviceEndpoints.cs");
        var source = await File.ReadAllTextAsync(path);

        Assert.Contains("token.Length is >= 20 and <= 4096", source);
        Assert.Contains("ExponentPushToken[", source);
        Assert.Contains("ExpoPushToken[", source);
        Assert.Contains("!token.Any(char.IsWhiteSpace)", source);
    }
}
