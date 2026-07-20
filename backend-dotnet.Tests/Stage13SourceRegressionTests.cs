using System.IO;
using System.Linq;
using Xunit;

namespace Opstrax.Tests;

public class Stage13SourceRegressionTests
{
    private static string RepoRoot =>
        Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../"));

    private static string ReadSource(params string[] parts)
    {
        var path = Path.Combine(new[] { RepoRoot }.Concat(parts).ToArray());
        return File.ReadAllText(path);
    }

    [Fact]
    public void AppShell_UsesDashboard_NotCockpit()
    {
        var appShell = ReadSource("frontend", "src", "layouts", "AppShell.tsx");

        Assert.Contains("?? \"Dashboard\"", appShell);
        Assert.DoesNotContain("\"Cockpit\"", appShell);
    }

    [Fact]
    public void OperationalApiClients_AreLiveOnly_NoSeedFallbackForStage13Surfaces()
    {
        var admin = ReadSource("frontend", "src", "services", "adminApi.ts");
        var incidents = ReadSource("frontend", "src", "services", "incidentsApi.ts");
        var fuel = ReadSource("frontend", "src", "services", "fuelApi.ts");
        var safety = ReadSource("frontend", "src", "services", "safetyApi.ts");
        var maintenance = ReadSource("frontend", "src", "services", "maintenanceApi.ts");
        var fleetHealth = ReadSource("frontend", "src", "services", "fleetHealthApi.ts");

        Assert.DoesNotContain("developmentFleetSeedData", admin);
        Assert.DoesNotContain("withSafeFallback", admin);
        Assert.Contains("/api/admin/permissions", admin);

        Assert.DoesNotContain("withFallback", incidents);
        Assert.DoesNotContain("success: true", incidents);
        Assert.DoesNotContain("Date.now()", incidents);
        Assert.Contains("/api/incidents/${id}/timeline", incidents);
        Assert.Contains("/api/incidents/${id}/recommendations", incidents);

        Assert.DoesNotContain("developmentFleetSeedData", fuel);
        Assert.DoesNotContain("withFallback", fuel);

        Assert.DoesNotContain("getSafetySummary", safety);
        Assert.DoesNotContain("getSafetyEvents", safety);
        Assert.DoesNotContain("withFallback", safety);
        Assert.DoesNotContain("success: true", safety);
        Assert.DoesNotContain("Date.now()", safety);

        Assert.DoesNotContain("getMaintenanceDashboard", maintenance);
        Assert.DoesNotContain("withFallback", maintenance);

        Assert.DoesNotContain("getFleetHealthSummary", fleetHealth);
        Assert.DoesNotContain("getFleetHealthRisks", fleetHealth);
        Assert.DoesNotContain("withFallback", fleetHealth);
    }

    [Fact]
    public void CommandCenterPage_ShowsLiveBridgeStrip()
    {
        var page = ReadSource("frontend", "src", "pages", "CommandCenterPage.tsx");

        Assert.Contains("Safety Bridge", page);
        Assert.Contains("Maintenance Bridge", page);
        Assert.Contains("Fleet Health Bridge", page);
    }

    [Fact]
    public void AdminPage_DoesNotSeedPermissionCatalog_WhenLiveEndpointIsUnavailable()
    {
        var adminPage = ReadSource("frontend", "src", "pages", "AdminPage.tsx");

        Assert.DoesNotContain("developmentFleetSeedData.permissions", adminPage);
        Assert.Contains("The live permissions endpoint failed", adminPage);
        Assert.Contains("seed-backed replacement", adminPage);
    }
}
