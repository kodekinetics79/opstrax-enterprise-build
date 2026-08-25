using Microsoft.AspNetCore.Http;
using Opstrax.Api.Controllers;

namespace Opstrax.Tests;

public sealed class Module1AlternateReadAuthorizationTests
{
    [Fact]
    public void ControlTowerReadsUseEntityPermissionsStrictBranchesAndSafeDriverProjection()
    {
        var source = ReadMappings();
        var list = Block(source, "private static async Task<IResult> ControlTowerEntities(", "private static Task<IResult> ControlTowerEntity(");
        var router = Block(source, "private static Task<IResult> ControlTowerEntity(", "private static async Task<IResult> ControlTowerDriverDetail(");
        var driver = Block(source, "private static async Task<IResult> ControlTowerDriverDetail(", "private static async Task<IResult> ControlTowerVehicleDetail(");
        var vehicle = Block(source, "private static async Task<IResult> ControlTowerVehicleDetail(", "private static Task<IResult> Vehicles(");

        Assert.Contains("StrictBranchFilter(http, \"v\")", list, StringComparison.Ordinal);
        Assert.Contains("branchClause", list, StringComparison.Ordinal);

        Assert.Contains("\"driver\" => ControlTowerDriverDetail", router, StringComparison.Ordinal);
        Assert.Contains("\"vehicle\" => ControlTowerVehicleDetail", router, StringComparison.Ordinal);
        Assert.DoesNotContain("EntityById", router, StringComparison.Ordinal);

        Assert.Contains("RequirePermission(http, \"drivers:view\")", driver, StringComparison.Ordinal);
        Assert.Contains("StrictBranchFilter(http, \"d\")", driver, StringComparison.Ordinal);
        Assert.Contains("d.deleted_at IS NULL", driver, StringComparison.Ordinal);
        Assert.DoesNotContain("d.*", driver, StringComparison.Ordinal);
        Assert.DoesNotContain("license_number", driver, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("licenseNumber", driver, StringComparison.OrdinalIgnoreCase);

        Assert.Contains("RequirePermission(http, \"vehicles:view\")", vehicle, StringComparison.Ordinal);
        Assert.Contains("StrictBranchFilter(http, \"v\")", vehicle, StringComparison.Ordinal);
        Assert.Contains("v.deleted_at IS NULL", vehicle, StringComparison.Ordinal);
    }

    [Fact]
    public void FleetHealthDirectDrawersApplyStrictBranchScope()
    {
        var source = ReadMappings();
        var vehicle = Block(source, "private static async Task<IResult> FleetHealthVehicleDetail(", "private static async Task<IResult> FleetHealthDriverDetail(");
        var driver = Block(source, "private static async Task<IResult> FleetHealthDriverDetail(", "// ── Fleet Health scoring helpers");

        Assert.Contains("StrictBranchFilter(http, \"v\")", vehicle, StringComparison.Ordinal);
        Assert.Contains("branchClause", vehicle, StringComparison.Ordinal);
        Assert.Contains("StrictBranchFilter(http, \"d\")", driver, StringComparison.Ordinal);
        Assert.Contains("branchClause", driver, StringComparison.Ordinal);
    }

    [Fact]
    public void LegacyAssetListAndDetailFailClosedToDerivedOwningBranch()
    {
        var source = ReadMappings();
        var list = Block(source, "private static Task<IResult> Assets(", "private static async Task<IResult> VehicleSummary(");
        var detail = Block(source, "private static async Task<IResult> AssetDetail(", "private sealed record JobListFilters");

        foreach (var block in new[] { list, detail })
        {
            Assert.Contains("StrictBranchFilter(http, \"a\")", block, StringComparison.Ordinal);
            Assert.Contains("owner_vehicle.branch_id", block, StringComparison.Ordinal);
            Assert.Contains("owner_driver.branch_id", block, StringComparison.Ordinal);
            Assert.Contains("ELSE NULL", block, StringComparison.Ordinal);
            Assert.Contains("branchClause", block, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void StrictBranchFilterPreservesTenantWideAdministratorAccess()
    {
        var tenantAdmin = new DefaultHttpContext();
        var tenantScope = EndpointMappings.StrictBranchFilter(tenantAdmin, "v");
        Assert.Equal("", tenantScope.clause);
        Assert.Null(tenantScope.branchId);

        var branchUser = new DefaultHttpContext();
        branchUser.Items[EndpointMappings.AuthBranchIdItemKey] = 42L;
        var branchScope = EndpointMappings.StrictBranchFilter(branchUser, "v");
        Assert.Equal(" AND v.branch_id = @branchId", branchScope.clause);
        Assert.Equal(42L, branchScope.branchId);
    }

    private static string ReadMappings()
        => File.ReadAllText(Path.Combine(RepoRoot, "backend-dotnet", "Controllers", "EndpointMappings.cs"));

    private static string RepoRoot => Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../"));

    private static string Block(string source, string startMarker, string endMarker)
    {
        var start = source.IndexOf(startMarker, StringComparison.Ordinal);
        var end = source.IndexOf(endMarker, start + startMarker.Length, StringComparison.Ordinal);
        Assert.True(start >= 0 && end > start, $"Could not locate block {startMarker}");
        return source[start..end];
    }
}
