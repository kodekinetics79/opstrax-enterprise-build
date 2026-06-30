using System.Reflection;
using System.IO;
using System.Linq;
using Opstrax.Api.Controllers;
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
