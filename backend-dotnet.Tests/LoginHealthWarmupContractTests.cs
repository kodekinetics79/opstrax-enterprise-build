namespace Opstrax.Tests;

public sealed class LoginHealthWarmupContractTests
{
    [Fact]
    public void LoginBootstrap_UsesMappedLivenessRouteInsteadOfGuaranteed404()
    {
        var root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../"));
        var source = File.ReadAllText(Path.Combine(root, "frontend", "src", "services", "authApi.ts"));

        Assert.Contains("apiClient.get(\"/health/live\")", source, StringComparison.Ordinal);
        Assert.DoesNotContain("apiClient.get(\"/api/health\")", source, StringComparison.Ordinal);

        var loginPage = File.ReadAllText(Path.Combine(root, "frontend", "src", "pages", "LoginPage.tsx"));
        Assert.Contains("onError: () =>", loginPage, StringComparison.Ordinal);
        Assert.Contains("setPassword(\"\")", loginPage, StringComparison.Ordinal);
        Assert.Contains("passwordRef.current?.focus()", loginPage, StringComparison.Ordinal);
    }
}
