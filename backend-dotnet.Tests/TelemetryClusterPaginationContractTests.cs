namespace Opstrax.Tests;

public sealed class TelemetryClusterPaginationContractTests
{
    [Fact]
    public void DevicePageUsesScopedEvidenceBackedClusterPaging()
    {
        var source = Read("backend-dotnet", "Controllers", "EndpointMappings.cs");

        Assert.Contains("RequirePermission(http, \"telemetry.devices.read\")", source, StringComparison.Ordinal);
        Assert.Contains("(@branchId::BIGINT IS NULL OR e.branch_id=@branchId)", source, StringComparison.Ordinal);
        Assert.Contains("var cluster = http.Request.Query[\"cluster\"]", source, StringComparison.Ordinal);
        Assert.Contains("COALESCE(e.device_category,'') ~* '(obd|j1939|can)'", source, StringComparison.Ordinal);
        Assert.Contains("SELECT p.* FROM latest_vehicle_positions p", source, StringComparison.Ordinal);
        Assert.Contains("p.company_id=e.company_id", source, StringComparison.Ordinal);
        Assert.Contains("LIMIT @limit OFFSET @offset", source, StringComparison.Ordinal);
        Assert.Contains("Math.Clamp(parsedPageSize, 1, 100)", source, StringComparison.Ordinal);
    }

    [Fact]
    public void ClusterSummaryClassifiesMissingOrStalePositionAsOfflineAndAttention()
    {
        var source = Read("backend-dotnet", "Controllers", "EndpointMappings.cs");

        Assert.Contains("lp.id IS NULL OR lp.lat NOT BETWEEN -90 AND 90 OR lp.lng NOT BETWEEN -180 AND 180", source, StringComparison.Ordinal);
        Assert.Contains("COALESCE(lp.device_fix_time,lp.event_time,lp.received_at)", source, StringComparison.Ordinal);
        Assert.Contains("LOWER(fc.status)='active'))) attention", source, StringComparison.Ordinal);
        Assert.Contains("active_fault_codes", source, StringComparison.Ordinal);
    }

    [Fact]
    public void BrowserClusterUsesServerPageAndNeverTreatsNoPositionAsHealthy()
    {
        var service = Read("frontend", "src", "services", "telematicsService.ts");
        var page = Read("frontend", "src", "pages", "TelematicsCommandPage.tsx");

        Assert.Contains("getTelemetryClusterPage", service, StringComparison.Ordinal);
        Assert.Contains("pageSize: Math.min(100, Math.max(1, options.pageSize ?? 50))", service, StringComparison.Ordinal);
        Assert.Contains("|| !positionAvailable", service, StringComparison.Ordinal);
        Assert.Contains("recordsQ.data?.summary.offline", page, StringComparison.Ordinal);
        Assert.Contains("recordsQ.data?.summary.attention", page, StringComparison.Ordinal);
        Assert.Contains("Showing {(page - 1) * pageSize + 1}", page, StringComparison.Ordinal);
        Assert.Contains(">Previous</button>", page, StringComparison.Ordinal);
        Assert.Contains(">Next</button>", page, StringComparison.Ordinal);
    }

    private static string Read(params string[] parts)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !Directory.Exists(Path.Combine(directory.FullName, "backend-dotnet"))) directory = directory.Parent;
        Assert.NotNull(directory);
        return File.ReadAllText(Path.Combine([directory!.FullName, .. parts]));
    }
}
