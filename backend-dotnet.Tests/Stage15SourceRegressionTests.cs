using System.IO;
using System.Linq;
using Xunit;

namespace Opstrax.Tests;

public class Stage15SourceRegressionTests
{
    private static string RepoRoot =>
        Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../"));

    private static string ReadSource(params string[] parts)
    {
        var path = Path.Combine(new[] { RepoRoot }.Concat(parts).ToArray());
        return File.ReadAllText(path);
    }

    [Fact]
    public void TripsPage_Is_Wired_To_Real_Trip_Api_Without_Fallback_Masking()
    {
        var page = ReadSource("frontend", "src", "pages", "TripsPage.tsx");

        Assert.Contains("tripApi.list", page);
        Assert.Contains("tripApi.detail", page);
        Assert.Contains("tripApi.breadcrumbs", page);
        Assert.Contains("tripApi.compliance", page);
        Assert.Contains("Start Trip", page);
        Assert.Contains("Complete Trip", page);
        Assert.DoesNotContain("withFallback", page);
        Assert.DoesNotContain("developmentFleetSeedData", page);
    }

    [Fact]
    public void TripsRoute_Is_Visible_In_AppShell_ModuleConfig_And_AppRoutes()
    {
        var app = ReadSource("frontend", "src", "App.tsx");
        var modules = ReadSource("frontend", "src", "modules", "moduleConfig.ts");
        var shell = ReadSource("frontend", "src", "layouts", "AppShell.tsx");
        var dashboard = ReadSource("frontend", "src", "pages", "CommandCenterPage.tsx");

        Assert.Contains("const TripsPage = lazy(() => import(\"@/pages/TripsPage\")", app);
        Assert.Contains("path=\"/trips\"", app);
        Assert.Contains("operatingRoutes", app);

        Assert.Contains("key: \"trips\"", modules);
        Assert.Contains("route: \"/trips\"", modules);
        Assert.Contains("title: \"Trips\"", modules);

        Assert.Contains("\"trips\"", shell);
        Assert.Contains("Trips", dashboard);
        Assert.Contains("/trips", dashboard);
    }
}

