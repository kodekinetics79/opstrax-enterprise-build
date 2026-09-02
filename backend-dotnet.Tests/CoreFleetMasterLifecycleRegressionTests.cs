using System.IO;
using System.Linq;
using Xunit;

namespace Opstrax.Tests;

public sealed class CoreFleetMasterLifecycleRegressionTests
{
    private static string RepoRoot => Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../"));
    private static string Read(params string[] parts) =>
        File.ReadAllText(Path.Combine(new[] { RepoRoot }.Concat(parts).ToArray()));

    [Fact]
    public void VehicleAndDriverRoutesExposeExplicitReversibleLifecycle()
    {
        var source = Read("backend-dotnet", "Controllers", "EndpointMappings.cs");

        Assert.Contains("/api/vehicles/{id:long}/archive", source);
        Assert.Contains("/api/vehicles/{id:long}/reactivate", source);
        Assert.Contains("/api/drivers/{id:long}/archive", source);
        Assert.Contains("/api/drivers/{id:long}/reactivate", source);
        Assert.Contains("app.MapDelete(\"/api/vehicles/{id:long}\", FleetMasterLifecycle", source);
        Assert.Contains("app.MapDelete(\"/api/drivers/{id:long}\", FleetMasterLifecycle", source);
    }

    [Fact]
    public void LifecycleMutationIsPermissionTenantAndBranchScoped()
    {
        var source = Read("backend-dotnet", "Controllers", "EndpointMappings.cs");
        var lifecycle = Block(source, "private static Func<HttpContext, long, Database, AuditService, CancellationToken, Task<IResult>> FleetMasterLifecycle", "private static Func<HttpContext, long, Database, AuditService, CancellationToken, Task<IResult>> SoftDelete");

        Assert.Contains("RequirePermission(http, \"fleet:manage\")", lifecycle);
        Assert.Contains("company_id=@companyId", lifecycle);
        Assert.Contains("AND branch_id=@branchId", lifecycle);
        Assert.Contains("FOR UPDATE", lifecycle);
        Assert.Contains("pg_advisory_xact_lock", lifecycle);
        Assert.Contains("RunInTenantTransactionAsync", lifecycle);
    }

    [Fact]
    public void ArchiveBlocksOperationalDependenciesAndReleasesMasterPairingHistory()
    {
        var source = Read("backend-dotnet", "Controllers", "EndpointMappings.cs");
        var lifecycle = Block(source, "private static Func<HttpContext, long, Database, AuditService, CancellationToken, Task<IResult>> FleetMasterLifecycle", "private static Func<HttpContext, long, Database, AuditService, CancellationToken, Task<IResult>> SoftDelete");

        Assert.Contains("FleetMasterArchiveBlockers", lifecycle);
        Assert.Contains("dispatch_assignments", lifecycle);
        Assert.Contains("device_installations", lifecycle);
        Assert.Contains("SET {reciprocalColumn}=NULL", lifecycle);
        Assert.Contains("UPDATE vehicle_assignments SET status='Released'", lifecycle);
        Assert.Contains("released_at=COALESCE(released_at,NOW())", lifecycle);
        Assert.Contains("SET {currentColumn}=NULL,deleted_at=NOW()", lifecycle);
    }

    [Fact]
    public void ReactivationPreservesStatusAndReportsIdentityConflicts()
    {
        var source = Read("backend-dotnet", "Controllers", "EndpointMappings.cs");
        var lifecycle = Block(source, "private static Func<HttpContext, long, Database, AuditService, CancellationToken, Task<IResult>> FleetMasterLifecycle", "private static Func<HttpContext, long, Database, AuditService, CancellationToken, Task<IResult>> SoftDelete");

        Assert.Contains("SET deleted_at=NULL,status=CASE WHEN status IN ('Deleted','Archived') THEN 'Available' ELSE status END", lifecycle);
        Assert.Contains("IsVehicleIdentityViolation(ex)", lifecycle);
        Assert.Contains("IsDriverIdentityViolation(ex)", lifecycle);
        Assert.Contains("cannot be reactivated because an active record", lifecycle);
        Assert.Contains("AddTimeline", lifecycle);
        Assert.Contains("audit.LogAsync", lifecycle);
    }

    [Fact]
    public void FleetReadsRequireAnExplicitActiveOrArchivedLifecycle()
    {
        var source = Read("backend-dotnet", "Controllers", "EndpointMappings.cs");
        foreach (var block in new[]
        {
            Block(source, "private static Task<IResult> Vehicles", "private static Task<IResult> Drivers"),
            Block(source, "private static Task<IResult> Drivers", "private static async Task<IResult> Customers"),
            Block(source, "private static async Task<IResult> VehicleDetail", "private static async Task<IResult> DriverDetail"),
            Block(source, "private static async Task<IResult> DriverDetail", "private static async Task<IResult> CustomerDetail")
        })
        {
            Assert.Contains("lifecycle", block);
            Assert.Contains("active", block);
            Assert.Contains("archived", block);
            Assert.Contains("deleted_at IS NOT NULL", block);
            Assert.Contains("deleted_at IS NULL", block);
        }
    }

    private static string Block(string source, string startMarker, string endMarker)
    {
        var start = source.IndexOf(startMarker, StringComparison.Ordinal);
        var end = source.IndexOf(endMarker, start + startMarker.Length, StringComparison.Ordinal);
        Assert.True(start >= 0 && end > start);
        return source[start..end];
    }
}
