namespace Opstrax.Tests;

using System.Reflection;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Opstrax.Api.Controllers;
using Opstrax.Api.Data;

public sealed class TelemetryClusterPaginationContractTests
{
    [Fact]
    public void DevicePageUsesScopedEvidenceBackedClusterPaging()
    {
        var source = Read("backend-dotnet", "Controllers", "EndpointMappings.cs");

        Assert.Contains("\"gps\" => \"telematics:gps:view\"", source, StringComparison.Ordinal);
        Assert.Contains("\"diagnostics\" => \"telematics:diagnostics:view\"", source, StringComparison.Ordinal);
        Assert.Contains("(@branchId::BIGINT IS NULL OR e.branch_id=@branchId)", source, StringComparison.Ordinal);
        Assert.Contains("var cluster = http.Request.Query[\"cluster\"]", source, StringComparison.Ordinal);
        Assert.Contains("(obd(-ii)?|j1939|can)", source, StringComparison.Ordinal);
        Assert.Contains("SELECT p.* FROM latest_vehicle_positions p", source, StringComparison.Ordinal);
        Assert.Contains("p.company_id=e.company_id AND p.device_id=e.id", source, StringComparison.Ordinal);
        Assert.DoesNotContain("p.vehicle_id=current_install.vehicle_id", source, StringComparison.Ordinal);
        Assert.Contains("LIMIT @limit OFFSET @offset", source, StringComparison.Ordinal);
        Assert.Contains("Math.Clamp(parsedPageSize, 1, 100)", source, StringComparison.Ordinal);
    }

    [Fact]
    public void ClusterSummaryClassifiesMissingOrStalePositionAsOfflineAndAttention()
    {
        var source = Read("backend-dotnet", "Controllers", "EndpointMappings.cs");

        Assert.Contains("lp.id IS NULL OR lp.lat NOT BETWEEN -90 AND 90 OR lp.lng NOT BETWEEN -180 AND 180", source, StringComparison.Ordinal);
        Assert.Contains("COALESCE(lp.device_fix_time,lp.event_time,lp.received_at)", source, StringComparison.Ordinal);
        Assert.Contains("LOWER(fc.status)='active')", source, StringComparison.Ordinal);
        Assert.Contains("LOWER(COALESCE(e.device_state,'')) IN ('quarantined','suspended')", source, StringComparison.Ordinal);
        Assert.Contains("active_fault_codes", source, StringComparison.Ordinal);
        Assert.Contains("CASE WHEN lp.engine_status IS NOT NULL OR lp.odometer_miles IS NOT NULL", source, StringComparison.Ordinal);
        Assert.Contains("GREATEST(", source, StringComparison.Ordinal);
        Assert.Contains("diagnostic_evidence.observed_at", source, StringComparison.Ordinal);
    }

    [Fact]
    public void BrowserClusterUsesServerPageAndNeverTreatsNoPositionAsHealthy()
    {
        var service = Read("frontend", "src", "services", "telematicsService.ts");
        var page = Read("frontend", "src", "pages", "TelematicsCommandPage.tsx");

        Assert.Contains("getTelemetryClusterPage", service, StringComparison.Ordinal);
        Assert.Contains("pageSize: Math.min(100, Math.max(1, options.pageSize ?? 50))", service, StringComparison.Ordinal);
        Assert.Contains("|| !requiredEvidenceAvailable", service, StringComparison.Ordinal);
        Assert.Contains("recordsQ.data?.summary.offline", page, StringComparison.Ordinal);
        Assert.Contains("recordsQ.data?.summary.attention", page, StringComparison.Ordinal);
        Assert.Contains("Showing {(page - 1) * pageSize + 1}", page, StringComparison.Ordinal);
        Assert.Contains(">Previous</button>", page, StringComparison.Ordinal);
        Assert.Contains(">Next</button>", page, StringComparison.Ordinal);
        Assert.Contains("enabled: Boolean(selected?.deviceId) && !paged", page, StringComparison.Ordinal);
        Assert.Contains("detail?: DeviceDetailRecord", page, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("gps", "telematics:devices:view")]
    [InlineData("diagnostics", "telematics:devices:view")]
    [InlineData("gps", "telematics:diagnostics:view")]
    [InlineData("diagnostics", "telematics:gps:view")]
    public async Task ClusterEndpointDeniesMissingOrWrongDedicatedPermission(string cluster, string heldPermission)
    {
        var http = new DefaultHttpContext();
        http.Request.QueryString = new QueryString($"?cluster={cluster}");
        http.Items[EndpointMappings.AuthUserIdItemKey] = 10L;
        http.Items[EndpointMappings.AuthCompanyIdItemKey] = 20L;
        http.Items[EndpointMappings.AuthRoleItemKey] = "Narrow telemetry reader";
        http.Items[EndpointMappings.AuthPermissionsItemKey] = new[] { heldPermission };
        var db = new Database(new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["ConnectionStrings:DefaultConnection"] = "Host=127.0.0.1;Port=1;Database=unused;Username=unused;Password=unused",
            ["Rls:EnforceTenantContext"] = "false",
        }).Build(), new TenantScopeAccessor());

        var method = typeof(EndpointMappings).GetMethod("TelemetryDevicePage", BindingFlags.NonPublic | BindingFlags.Static)!;
        var result = await (Task<IResult>)method.Invoke(null, [http, db, CancellationToken.None])!;

        Assert.Equal(StatusCodes.Status403Forbidden, Assert.IsAssignableFrom<IStatusCodeHttpResult>(result).StatusCode);
    }

    private static string Read(params string[] parts)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !Directory.Exists(Path.Combine(directory.FullName, "backend-dotnet"))) directory = directory.Parent;
        Assert.NotNull(directory);
        return File.ReadAllText(Path.Combine([directory!.FullName, .. parts]));
    }
}
