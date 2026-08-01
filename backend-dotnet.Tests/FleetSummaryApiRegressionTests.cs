using System.IO;
using System.Linq;
using Xunit;

namespace Opstrax.Tests;

public sealed class FleetSummaryApiRegressionTests
{
    private static string RepoRoot =>
        Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../"));

    private static string ReadSource(params string[] parts)
    {
        var path = Path.Combine(new[] { RepoRoot }.Concat(parts).ToArray());
        return File.ReadAllText(path);
    }

    [Fact]
    public void VehicleSummary_UsesTenantWideAggregateEndpoint()
    {
        var source = ReadSource("frontend", "src", "services", "vehiclesApi.ts");
        var summary = SummaryExpression(source);

        Assert.Contains("/api/vehicles/summary", summary);
        Assert.DoesNotContain("getVehicles().then", summary);
    }

    [Fact]
    public void DriverSummary_UsesTenantWideAggregateEndpoint()
    {
        var source = ReadSource("frontend", "src", "services", "driversApi.ts");
        var summary = SummaryExpression(source);

        Assert.Contains("/api/drivers/summary", summary);
        Assert.DoesNotContain("getDrivers().then", summary);
    }

    [Fact]
    public void BackendSummaryContracts_ExposeFieldsConsumedByFleetPages()
    {
        var source = ReadSource("backend-dotnet", "Controllers", "EndpointMappings.cs");
        var vehicleSummary = MethodSection(source, "private static async Task<IResult> VehicleSummary", "private static async Task<IResult> VehiclePlanningInsights");
        var driverSummary = MethodSection(source, "private static async Task<IResult> DriverSummary", "private static async Task<IResult> CustomerSummary");

        Assert.Contains("COUNT(*) total", vehicleSummary);
        Assert.Contains("at_risk", vehicleSummary);
        Assert.Contains("fleet_readiness_score", vehicleSummary);
        Assert.Contains("data_completeness_score", vehicleSummary);
        Assert.Contains("device_exceptions", vehicleSummary);

        Assert.Contains("COUNT(*) total", driverSummary);
        Assert.Contains("at_risk", driverSummary);
        Assert.Contains("driver_readiness_score", driverSummary);
        Assert.Contains("data_completeness_score", driverSummary);
        Assert.Contains("safety_score", driverSummary);
    }

    private static string SummaryExpression(string source)
    {
        var start = source.IndexOf("summary: () =>", StringComparison.Ordinal);
        Assert.True(start >= 0, "summary function was not found");
        var end = source.IndexOf('\n', start);
        return source[start..(end >= 0 ? end : source.Length)];
    }

    private static string MethodSection(string source, string startMarker, string endMarker)
    {
        var start = source.IndexOf(startMarker, StringComparison.Ordinal);
        var end = source.IndexOf(endMarker, start + startMarker.Length, StringComparison.Ordinal);
        Assert.True(start >= 0 && end > start, $"Could not locate method section beginning with {startMarker}");
        return source[start..end];
    }
}
