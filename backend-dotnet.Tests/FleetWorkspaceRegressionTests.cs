using System.Reflection;
using System.IO;
using System.Linq;
using Opstrax.Api.Controllers;
using Opstrax.Api.Services;
using Microsoft.Extensions.Configuration;
using Xunit;

namespace Opstrax.Tests;

public class FleetWorkspaceRegressionTests
{
    private static string RepoRoot =>
        Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../"));

    private static string ReadSource(params string[] parts)
    {
        var path = Path.Combine(new[] { RepoRoot }.Concat(parts).ToArray());
        return File.ReadAllText(path);
    }

    [Fact]
    public void FleetTmsPagedRoutes_DefaultToPageOne()
    {
        AssertRouteDefaults("Shipments");
        AssertRouteDefaults("Tracking");
        AssertRouteDefaults("Maintenance");
        AssertRouteDefaults("Fuel");
    }

    [Fact]
    public void FleetWorkspaceApi_SendsPageOne_ForPagedRequests()
    {
        var api = ReadSource("frontend", "src", "services", "fleetTmsApi.ts");

        Assert.Contains("params: { page: 1, pageSize: 20, ...params }", api);
    }

    [Fact]
    public void FleetCompliance_SaudiTab_UsesCanonicalMarketPackLedgerOnly()
    {
        var page = ReadSource("frontend", "src", "pages", "FleetCompliancePage.tsx");
        var saudiSection = page[(page.IndexOf("function SaudiReadiness()", StringComparison.Ordinal))..];

        Assert.Contains("marketPackApi.saudiDocuments()", saudiSection);
        Assert.Contains("marketPackApi.createSaudiDocument", saudiSection);
        Assert.DoesNotContain("fleetReadinessApi.documents()", saudiSection);
        Assert.DoesNotContain("fleetReadinessApi.createDocument", saudiSection);
        var endpoints = ReadSource("backend-dotnet", "Controllers", "FleetTmsColdChainEndpoints.cs");
        Assert.Contains("SaudiLedgerRetired()", endpoints);
        Assert.Contains("Status410Gone", endpoints);
    }

    [Fact]
    public void FleetCompliance_DoesNotCreateFabricatedInspectionOrMileageFacts()
    {
        var page = ReadSource("frontend", "src", "pages", "FleetCompliancePage.tsx");

        Assert.DoesNotContain("vehicleLabel: \"Truck\", inspectionType: \"pre_trip\", status: \"pass\"", page);
        Assert.DoesNotContain("provinceState: \"QC\", distance: \"1200\", taxPeriod: \"2026-Q2\"", page);
        Assert.Contains("inspectorName", page);
        Assert.Contains("mileageForm", page);
    }

    [Fact]
    public void FleetDemoFlag_DoesNotEnableLegacyCrossTenantBatchSeeds()
    {
        var fleetOnly = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Fleet:EnableDemoSeed"] = "true",
        }).Build();
        var legacy = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["LegacyBatchDemoSeed:Enabled"] = "true",
        }).Build();

        Assert.False(DemoSeedGate.IsExplicitlyEnabled(fleetOnly));
        Assert.True(DemoSeedGate.IsExplicitlyEnabled(legacy));
    }

    private static void AssertRouteDefaults(string methodName)
    {
        var method = typeof(FleetTmsEndpoints).GetMethod(methodName, BindingFlags.NonPublic | BindingFlags.Static);
        Assert.NotNull(method);

        var page = method!.GetParameters().Single(parameter => parameter.Name == "page");
        var pageSize = method.GetParameters().Single(parameter => parameter.Name == "pageSize");

        Assert.True(page.HasDefaultValue, $"{methodName}.page should be optional");
        Assert.True(pageSize.HasDefaultValue, $"{methodName}.pageSize should be optional");
        Assert.Equal(1, Convert.ToInt32(page.DefaultValue));
        Assert.Equal(20, Convert.ToInt32(pageSize.DefaultValue));
    }
}
