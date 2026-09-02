using System.IO;
using System.Linq;
using Xunit;

namespace Opstrax.Tests;

public sealed class CoreFleetAssignmentsRegressionTests
{
    private static string RepoRoot => Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../"));
    private static string Read(params string[] parts) => File.ReadAllText(Path.Combine(new[] { RepoRoot }.Concat(parts).ToArray()));

    [Fact]
    public void AssignmentSurfaceUsesItsActualDispatchPermission()
    {
        var config = Read("frontend", "src", "modules", "moduleConfig.ts");
        var app = Read("frontend", "src", "App.tsx");
        Assert.Contains("key: \"assignments\"", config);
        Assert.Contains("requiredPermission: \"dispatch:view\"", config);
        Assert.Contains("path=\"/assignments/*\"", app);
        Assert.Contains("permission=\"dispatch:view\"><FleetAssignmentsPage", app);
    }

    [Fact]
    public void AssignmentReadsAndCandidatePoolsUseStrictBranchScope()
    {
        var source = Read("backend-dotnet", "Controllers", "EndpointMappings.cs");
        var reads = Block(source, "private static async Task<IResult> DispatchAssignmentsList", "// POST /api/dispatch/assignments — create assignment");
        var candidates = Block(source, "private static Task<IResult> DispatchRecommendations", "private static async Task<IResult> DispatchAssign");

        // Assignment ownership is persisted directly; scoping via a nullable joined
        // vehicle would make history leak or disappear after vehicle lifecycle changes.
        Assert.Contains("StrictBranchFilter(http, \"da\")", reads);
        Assert.Contains("StrictBranchFilter(http, \"v\")", candidates);
        Assert.Contains("StrictBranchFilter(http, \"d\")", candidates);
        Assert.Contains("RequirePermission(http, \"dispatch:view\")", candidates);
    }

    [Fact]
    public void AssignmentCreateRejectsCrossBranchDriverOrVehicle()
    {
        var source = Read("backend-dotnet", "Controllers", "EndpointMappings.cs");
        var create = Block(source, "private static async Task<IResult> DispatchAssignmentCreate", "// POST /api/dispatch/assignments/{id}/accept");

        Assert.Contains("var branchId = GetBranchId(http)", create);
        Assert.Contains("v.branch_id=@branchId AND d.branch_id=@branchId", create);
        Assert.Contains("Driver or vehicle not found in authorized branch", create);
    }

    [Fact]
    public void JobsHaveCanonicalBranchOwnershipAndDispatchSupportsJoblessAssignments()
    {
        var schema = Read("database", "init", "001_schema.sql");
        var dispatchSchema = Read("backend-dotnet", "Services", "DispatchSchemaService.cs");
        var source = Read("backend-dotnet", "Controllers", "EndpointMappings.cs");
        Assert.Contains("branch_id BIGINT NULL", schema);
        Assert.Contains("ALTER COLUMN job_id DROP NOT NULL", dispatchSchema);
        Assert.Contains("j.branch_id=@branchId", source);
        Assert.Contains("INSERT INTO jobs (company_id, branch_id", source);
    }

    [Fact]
    public void EligibilityAndLegacyAssignmentPathsAreBranchOwned()
    {
        var source = Read("backend-dotnet", "Controllers", "EndpointMappings.cs");
        var eligibility = Block(source, "private static async Task<IResult> DispatchEligibilityCheck", "// GET /api/dispatch/exceptions");
        var legacy = Block(source, "private static async Task<IResult> AssignJob", "private static async Task<IResult> ChangeJobStatus");
        var insert = Block(source, "private static Task InsertJobAssignment", "private static Task CloseJobAssignments");
        Assert.Contains("v.branch_id=@branchId AND d.branch_id=@branchId", eligibility);
        Assert.Contains("branch_id, job_id, vehicle_id, driver_id", insert);
        Assert.Contains("AND branch_id=@branchId", legacy);
    }

    [Fact]
    public void DirectDriverVehicleAssignmentMaintainsSymmetricOneToOneLinks()
    {
        var source = Read("backend-dotnet", "Controllers", "EndpointMappings.cs");
        var change = Block(source, "private static Func<HttpContext, long, Dictionary<string, object?>, Database, AuditService, CancellationToken, Task<IResult>> ChangeEntityStatus", "private static Func<HttpContext, long, Database, AuditService");

        Assert.Contains("current_target_id", change);
        Assert.Contains("reciprocal_id", change);
        Assert.Contains("FirstOrDefault(value => value is not null and not DBNull", change);
        Assert.Contains("SET {reciprocalColumn}=NULL", change);
        Assert.Contains("SET {column}=NULL", change);
        Assert.Contains("AND branch_id=@branchId", change);
        Assert.Contains("sourceBranchId != targetBranchId", change);
        Assert.Contains("Driver and vehicle must belong to the same branch", change);
        Assert.Contains("targetKeyPresent", change);
        Assert.Contains("Assignment target must be a positive integer", change);
        Assert.Contains("oldTargetId == targetId", change);
    }

    [Fact]
    public void VehicleDetailUsesFleetMasterAssignmentAndReturnsEffectiveDatedHistory()
    {
        var source = Read("backend-dotnet", "Controllers", "EndpointMappings.cs");
        var detail = Block(source, "private static async Task<IResult> VehicleDetail", "private static async Task<IResult> DriverDetail");

        Assert.Contains("d.id=v.assigned_driver_id", detail);
        Assert.Contains("assignmentHistory = await db.QueryAsync", detail);
        Assert.Contains("va.assigned_at effective_from", detail);
        Assert.Contains("va.released_at effective_to", detail);
        Assert.DoesNotContain("current_dispatch", detail);
    }

    [Fact]
    public void VehicleAssignmentUiRequiresExplicitSelectionAndShowsHistory()
    {
        var vehicles = Read("frontend", "src", "pages", "VehiclesPage.tsx");
        var entityList = Read("frontend", "src", "pages", "EntityListPage.tsx");
        var assignments = Read("frontend", "src", "pages", "FleetAssignmentsPage.tsx");

        Assert.Contains("DriverAssignmentModal", vehicles);
        Assert.Contains("Confirm assignment", vehicles);
        Assert.DoesNotContain("Smart assign", vehicles);
        Assert.Contains("Driver assignment history", vehicles);
        Assert.Contains("Fleet assignment history", assignments);
        Assert.Contains("/api/vehicle-assignments", assignments);
        Assert.Contains("FleetMasterAssignmentModal", entityList);
        Assert.Contains("Confirm assignment", entityList);
        Assert.Contains("listPaged({ limit: 2000 })", entityList);
        Assert.Contains("Vehicle Assignment History", entityList);
    }

    [Fact]
    public void AssignmentUiReportsSupportingQueryFailures()
    {
        var page = Read("frontend", "src", "pages", "FleetAssignmentsPage.tsx");
        Assert.Contains("const supportingError", page);
        Assert.Contains("role=\"alert\"", page);
    }

    private static string Block(string source, string startMarker, string endMarker)
    {
        var start = source.IndexOf(startMarker, StringComparison.Ordinal);
        var end = source.IndexOf(endMarker, start + startMarker.Length, StringComparison.Ordinal);
        Assert.True(start >= 0 && end > start);
        return source[start..end];
    }
}
