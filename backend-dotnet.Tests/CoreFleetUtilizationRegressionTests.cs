using System.IO;
using System.Linq;
using Xunit;

namespace Opstrax.Tests;

public sealed class CoreFleetUtilizationRegressionTests
{
    private static string RepoRoot => Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../"));
    private static string Read(params string[] parts) => File.ReadAllText(Path.Combine(new[] { RepoRoot }.Concat(parts).ToArray()));

    [Fact]
    public void UtilizationEndpointsRequirePermissionAndStrictBranchScope()
    {
        var source = Read("backend-dotnet", "Controllers", "EndpointMappings.cs");
        var list = Block(source, "private static async Task<IResult> FleetUtilizationList", "private static async Task<IResult> FleetUtilizationSummary");
        var summary = Block(source, "private static async Task<IResult> FleetUtilizationSummary", "private static async Task<IResult> CustomerSummary");

        Assert.Contains("RequirePermission(http, \"fleet:view\")", list);
        Assert.Contains("StrictBranchFilter(http, \"v\")", list);
        Assert.Contains("RequirePermission(http, \"fleet:view\")", summary);
        Assert.Contains("v.branch_id=@branchId", summary);
        Assert.Contains("ev.branch_id=@branchId", summary);
    }

    [Fact]
    public void UtilizationIsCalculatedFromTripHoursNotVehicleStatus()
    {
        var source = Read("backend-dotnet", "Controllers", "EndpointMappings.cs");
        var handlers = Block(source, "private static async Task<IResult> FleetUtilizationList", "private static async Task<IResult> CustomerSummary");

        Assert.Contains("active_hours_30d", handlers);
        Assert.Contains("GREATEST(started_at,NOW()-INTERVAL '30 days')", handlers);
        Assert.Contains("started_at + INTERVAL '24 hours'", handlers);
        Assert.Contains("open_trip_estimate_count", handlers);
        Assert.Contains("trip_hours_30d_estimated_open", handlers);
        Assert.Contains("/ 240.0 * 100", handlers);
        Assert.Contains("utilization_basis", handlers);
        Assert.DoesNotContain("WHEN 'On Route'  THEN LEAST(98", handlers);
    }

    [Fact]
    public void UiDisclosesEvidenceBasisAndDoesNotGenerateEvidenceFreeUtilizationActions()
    {
        var page = Read("frontend", "src", "pages", "FleetUtilizationPage.tsx");
        Assert.Contains("30-day utilization", page);
        Assert.Contains("240-hour operating baseline", page);
        Assert.Contains("hasUtilizationEvidence && fuelCost", page);
        Assert.Contains("hasUtilizationEvidence && utilization <= 35", page);
        Assert.Contains("lack trip-hour evidence", page);
        Assert.Contains("open trip(s) estimated and capped at 24h", page);
    }

    [Fact]
    public void UtilizationSummaryReturnsNumericCostsForClientFormatting()
    {
        var source = Read("backend-dotnet", "Controllers", "EndpointMappings.cs");
        var summary = Block(source, "private static async Task<IResult> FleetUtilizationSummary", "private static async Task<IResult> CustomerSummary");
        Assert.Contains("ROUND(SUM(ie.estimated_cost),2)", summary);
        Assert.Contains("ROUND(SUM(ft.total_cost),2)", summary);
        Assert.DoesNotContain("CONCAT('$'", summary);
    }

    private static string Block(string source, string startMarker, string endMarker)
    {
        var start = source.IndexOf(startMarker, StringComparison.Ordinal);
        var end = source.IndexOf(endMarker, start + startMarker.Length, StringComparison.Ordinal);
        Assert.True(start >= 0 && end > start);
        return source[start..end];
    }
}
