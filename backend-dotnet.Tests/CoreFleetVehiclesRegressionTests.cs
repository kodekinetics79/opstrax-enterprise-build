using System.IO;
using System.Linq;
using Xunit;

namespace Opstrax.Tests;

public sealed class CoreFleetVehiclesRegressionTests
{
    private static string RepoRoot => Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../"));
    private static string Read(params string[] parts) => File.ReadAllText(Path.Combine(new[] { RepoRoot }.Concat(parts).ToArray()));

    [Fact]
    public void VehicleReadsAndSummaryApplyBranchScope()
    {
        var source = Read("backend-dotnet", "Controllers", "EndpointMappings.cs");
        var summary = Block(source, "private static async Task<IResult> VehicleSummary", "private static async Task<IResult> VehiclePlanningInsights");
        var detail = Block(source, "private static async Task<IResult> VehicleDetail", "private static async Task<IResult> DriverDetail");

        Assert.Contains("StrictBranchFilter(http, \"v\")", summary);
        Assert.Contains("branchClause", summary);
        Assert.Contains("StrictBranchFilter(http, \"v\")", detail);
        Assert.Contains("branchClause", detail);
    }

    [Fact]
    public void VehicleCreatePersistsAuthenticatedBranch()
    {
        var source = Read("backend-dotnet", "Controllers", "EndpointMappings.cs");
        var create = Block(source, "private static async Task<IResult> CreateVehicle", "private static async Task<IResult> UpdateVehicle");

        Assert.Contains("(company_id, branch_id, vehicle_code", create);
        Assert.Contains("GetBranchId(http)", create);
    }

    [Fact]
    public void VehicleUpdateRejectsDuplicatesAndMissingOrOutOfBranchRecords()
    {
        var source = Read("backend-dotnet", "Controllers", "EndpointMappings.cs");
        var update = Block(source, "private static async Task<IResult> UpdateVehicle", "// ─────────────────────────────────────────────────────────────────────────────────────");

        Assert.Contains("id<>@id AND LOWER(vehicle_code)=LOWER(@code)", update);
        Assert.Contains("id<>@id AND LOWER(vin)=LOWER(@vin)", update);
        Assert.Contains("codeValue is DBNull ? null", update);
        Assert.Contains("AND branch_id=@branchId", update);
        Assert.Contains("if (affected == 0)", update);
    }

    [Fact]
    public void VehicleUiSurfacesMutationFailures()
    {
        var page = Read("frontend", "src", "pages", "VehiclesPage.tsx");
        Assert.Contains("const actionError = save.error ?? remove.error ?? assign.error", page);
        Assert.Contains("serverError={save.error", page);
        Assert.Contains("role=\"alert\"", page);
    }

    [Fact]
    public void CleanBootstrapIncludesCoreFleetBranchSchema()
    {
        var schema = Read("database", "init", "001_schema.sql");
        Assert.Contains("CREATE TABLE IF NOT EXISTS branches", schema);
        Assert.Contains("branch_id BIGINT NULL", schema);
        Assert.Contains("idx_vehicles_branch", schema);
        Assert.Contains("idx_drivers_branch", schema);
        Assert.Contains("idx_users_branch", schema);
    }

    private static string Block(string source, string startMarker, string endMarker)
    {
        var start = source.IndexOf(startMarker, StringComparison.Ordinal);
        var end = source.IndexOf(endMarker, start + startMarker.Length, StringComparison.Ordinal);
        Assert.True(start >= 0 && end > start);
        return source[start..end];
    }
}
