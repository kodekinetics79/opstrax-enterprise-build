using System.IO;
using System.Linq;
using Xunit;

namespace Opstrax.Tests;

public class Stage17P0StabilityRegressionTests
{
    private static string RepoRoot =>
        Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../"));

    private static string ReadSource(params string[] parts)
    {
        var path = Path.Combine(new[] { RepoRoot }.Concat(parts).ToArray());
        return File.ReadAllText(path);
    }

    [Fact]
    public void AppShell_Search_Uses_ModuleAware_Routing_And_Awaited_Logout()
    {
        var appShell = ReadSource("frontend", "src", "layouts", "AppShell.tsx");

        Assert.Contains("resolveSearchRoute", appShell);
        Assert.Contains("navigate(resolveSearchRoute(sidebarQuery))", appShell);
        Assert.Contains("void logout().finally(() => setProfileOpen(false))", appShell);
        Assert.Contains("route: \"/work-orders\"", appShell);
    }

    [Fact]
    public void ApiClient_Only_Clears_Session_On_Auth_Bootstrap_Failures()
    {
        var apiClient = ReadSource("frontend", "src", "services", "apiClient.ts");

        Assert.Contains("url.includes(\"/api/auth/me\")", apiClient);
        Assert.Contains("url.includes(\"/api/auth/refresh\")", apiClient);
        Assert.DoesNotContain("url.includes(\"/api/fleet-health/\")", apiClient);
        Assert.DoesNotContain("url.includes(\"/api/telemetry/\")", apiClient);
    }

    [Fact]
    public void Vehicle_And_Driver_Module_Details_Fall_Back_To_List_Row_Data()
    {
        var vehicles = ReadSource("frontend", "src", "pages", "VehiclesModulePage.tsx");
        var drivers = ReadSource("frontend", "src", "pages", "DriversModulePage.tsx");

        Assert.Contains("selectedRow = rows.find", vehicles);
        Assert.Contains("detail.isLoading && !record", vehicles);
        Assert.Contains("selectedRow = rows.find", drivers);
        Assert.Contains("detail.isLoading && !record", drivers);
    }

    [Fact]
    public void Forms_Use_Real_Validation_And_Do_Not_Coerce_Blanks_To_Zero()
    {
        var entityList = ReadSource("frontend", "src", "pages", "EntityListPage.tsx");
        var vehiclesPage = ReadSource("frontend", "src", "pages", "VehiclesPage.tsx");
        var fleetHealth = ReadSource("frontend", "src", "pages", "FleetHealthPage.tsx");

        Assert.Contains("must be a valid number", entityList);
        Assert.Contains("target.value", entityList);
        Assert.Contains("must be a valid number", vehiclesPage);
        Assert.Contains("target.value", vehiclesPage);
        Assert.Contains("serviceType: \"fleet_health\"", fleetHealth);
        Assert.Contains("DrawerSkeleton", fleetHealth);
    }
}
