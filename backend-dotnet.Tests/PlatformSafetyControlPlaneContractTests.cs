using System;
using System.IO;
using Xunit;

namespace Opstrax.Tests;

public sealed class PlatformSafetyControlPlaneContractTests
{
    private static string RepoRoot =>
        Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../"));

    private static string Read(params string[] parts) =>
        File.ReadAllText(Path.Combine(new[] { RepoRoot }.Concat(parts).ToArray()));

    [Fact]
    public void ModuleEntitlements_CoverManagerAndDriverSafetySurfaces()
    {
        var program = Read("backend-dotnet", "Program.cs");

        Assert.Contains("(\"/api/safety\",", program, StringComparison.Ordinal);
        Assert.Contains("(\"/api/incidents\",", program, StringComparison.Ordinal);
        Assert.Contains("(\"/api/coaching\",", program, StringComparison.Ordinal);
        Assert.Contains("(\"/api/driver/coaching\",     \"safety\")", program, StringComparison.Ordinal);

        Assert.Contains("(\"/api/dvir\",", program, StringComparison.Ordinal);
        Assert.Contains("(\"/api/driver/dvir\",         \"maintenance\")", program, StringComparison.Ordinal);

        Assert.Contains("(\"/api/hos\",", program, StringComparison.Ordinal);
        Assert.Contains("(\"/api/driver/hos\",          \"compliance\")", program, StringComparison.Ordinal);
        Assert.Contains("(\"/api/eld\",", program, StringComparison.Ordinal);
    }

    [Fact]
    public void Integrations_HaveServerGateAndPlatformAdminControls()
    {
        var program = Read("backend-dotnet", "Program.cs");
        var tenants = Read("frontend", "src", "pages", "platform", "PlatformTenantsPage.tsx");
        var packages = Read("frontend", "src", "pages", "platform", "PlatformPackagesPage.tsx");

        Assert.Contains("(\"/api/integrations\",        \"integrations\")", program, StringComparison.Ordinal);
        Assert.Contains("key: \"integrations\"", tenants, StringComparison.Ordinal);
        Assert.Contains("\"integrations\"", packages, StringComparison.Ordinal);
    }

    [Fact]
    public void MarketPackControl_CapturesOperatorReasonForAudit()
    {
        var revenue = Read("frontend", "src", "pages", "platform", "PlatformRevenuePage.tsx");

        Assert.Contains("Operator reason (recorded in Platform audit)", revenue, StringComparison.Ordinal);
        Assert.Contains("reason })", revenue, StringComparison.Ordinal);
        Assert.Contains("!marketPackReason.trim()", revenue, StringComparison.Ordinal);
    }
}
