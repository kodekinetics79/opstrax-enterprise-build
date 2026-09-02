using System.IO;
using System.Linq;
using Xunit;

namespace Opstrax.Tests;

public sealed class CoreFleetDriversRegressionTests
{
    private static string RepoRoot => Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../"));
    private static string Read(params string[] parts) => File.ReadAllText(Path.Combine(new[] { RepoRoot }.Concat(parts).ToArray()));

    [Fact]
    public void DriverSummaryAndDetailApplyBranchScope()
    {
        var source = Read("backend-dotnet", "Controllers", "EndpointMappings.cs");
        var summary = Block(source, "private static async Task<IResult> DriverSummary", "private static async Task<IResult> CustomerSummary");
        var detail = Block(source, "private static async Task<IResult> DriverDetail", "private static async Task<IResult> CustomerDetail");

        Assert.Contains("StrictBranchFilter(http, \"d\")", summary);
        Assert.Contains("branchClause", summary);
        Assert.Contains("StrictBranchFilter(http, \"d\")", detail);
        Assert.Contains("branchClause", detail);
    }

    [Fact]
    public void DriverCreatePersistsAuthenticatedBranch()
    {
        var source = Read("backend-dotnet", "Controllers", "EndpointMappings.cs");
        var create = Block(source, "private static async Task<IResult> CreateDriver", "private static async Task<IResult> UpdateDriver");

        Assert.Contains("(company_id, branch_id, driver_code", create);
        Assert.Contains("GetBranchId(http)", create);
    }

    [Fact]
    public void DriverUpdateRejectsDuplicatesAndOutOfBranchOrMissingRecords()
    {
        var source = Read("backend-dotnet", "Controllers", "EndpointMappings.cs");
        var update = Block(source, "private static async Task<IResult> UpdateDriver", "private static async Task<IResult> CreateCustomer");

        Assert.Contains("id<>@id AND LOWER(driver_code)=LOWER(@code)", update);
        Assert.Contains("license_number_bidx=@bidx OR", update);
        Assert.Contains("NULLIF(BTRIM(license_number_bidx),'') IS NULL", update);
        Assert.Contains("LOWER(BTRIM(license_number))=LOWER(BTRIM(@license))", update);
        Assert.Contains("codeValue is DBNull ? null", update);
        Assert.Contains("AND branch_id=@branchId", update);
        Assert.Contains("if (affected == 0)", update);
    }

    [Fact]
    public void DriverPortalOperationsAreBranchScopedAndCannotConvertStaffAccounts()
    {
        var source = Read("backend-dotnet", "Controllers", "EndpointMappings.cs");
        var portal = Block(source, "private static async Task<IResult> DriverPortalInvite", "private static string GenerateDriverTempPassword");

        Assert.Contains("GetBranchId(http)", portal);
        Assert.Contains("AND branch_id=@branchId", portal);
        Assert.Contains("already belongs to a staff account", portal);
        Assert.Contains("branch_id=@branchId", portal);
    }

    [Fact]
    public void DriverDetailReturnsPersistedPortalAccessState()
    {
        var source = Read("backend-dotnet", "Controllers", "EndpointMappings.cs");
        var detail = Block(source, "private static async Task<IResult> DriverDetail", "private static async Task<IResult> CustomerDetail");

        Assert.Contains("LEFT JOIN users pu ON pu.id=d.user_id AND pu.company_id=d.company_id", detail);
        Assert.Contains("portal_status", detail);
        Assert.Contains("portal_email", detail);
    }

    [Fact]
    public void DriverNavigationUsesCanonicalRuntimePermission()
    {
        var config = Read("frontend", "src", "modules", "moduleConfig.ts");
        Assert.Contains("key: \"drivers\"", config);
        Assert.Contains("requiredPermission: \"drivers:view\"", config);
        Assert.DoesNotContain("requiredPermission: \"drivers.view\"", config);
    }

    private static string Block(string source, string startMarker, string endMarker)
    {
        var start = source.IndexOf(startMarker, StringComparison.Ordinal);
        var end = source.IndexOf(endMarker, start + startMarker.Length, StringComparison.Ordinal);
        Assert.True(start >= 0 && end > start);
        return source[start..end];
    }
}
