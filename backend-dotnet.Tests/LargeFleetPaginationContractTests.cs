using Xunit;

namespace Opstrax.Tests;

public sealed class LargeFleetPaginationContractTests
{
    private static string Root => Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../"));

    private static string Read(params string[] parts) => File.ReadAllText(Path.Combine([Root, .. parts]));

    [Fact]
    public void ReturnableAssets_UseBoundedServerPagingSearchSortAndScopedFullExport()
    {
        var endpoints = Read("backend-dotnet", "Controllers", "FleetTmsColdChainEndpoints.cs");
        var api = Read("frontend", "src", "services", "fleetTmsApi.ts");
        var page = Read("frontend", "src", "pages", "FleetAssetManagementPage.tsx");

        Assert.Contains("Math.Clamp(parsedPageSize, 1, 100)", endpoints, StringComparison.Ordinal);
        Assert.Contains("LIMIT @limit OFFSET @offset", endpoints, StringComparison.Ordinal);
        Assert.Contains("a.asset_tag ILIKE '%' || @search || '%'", endpoints, StringComparison.Ordinal);
        Assert.Contains("BranchScope(http, \"a.\")", endpoints, StringComparison.Ordinal);
        Assert.Contains("/api/fleet-tms/assets/export", endpoints, StringComparison.Ordinal);
        Assert.Contains("Guard(app.MapGet(\"/api/fleet-tms/assets/export\", AssetsExport), \"fleet:manage\")", endpoints, StringComparison.Ordinal);
        Assert.Contains("\"lastseen\" => \"a.last_seen_at_utc\"", endpoints, StringComparison.Ordinal);
        Assert.Contains("LIMIT 100000", endpoints, StringComparison.Ordinal);
        Assert.Contains("EndpointMappings.CsvCell", endpoints, StringComparison.Ordinal);

        Assert.Contains("pageSize: Math.min(100", api, StringComparison.Ordinal);
        Assert.Contains("fleetAssetApi.assets({ page: assetPage, pageSize: 100", page, StringComparison.Ordinal);
        Assert.Contains("exportEndpoint: '/api/fleet-tms/assets/export'", page, StringComparison.Ordinal);
        Assert.Contains("Page {assetPage} of {assetPageCount}", page, StringComparison.Ordinal);
        Assert.DoesNotContain("assets.slice(0, 7)", page, StringComparison.Ordinal);
    }

    [Fact]
    public void DeviceInventory_UsesBoundedServerPagesAndNeverBuildsExportFromRenderedRows()
    {
        var endpoints = Read("backend-dotnet", "Controllers", "EndpointMappings.cs");
        var service = Read("frontend", "src", "services", "telematicsService.ts");
        var page = Read("frontend", "src", "pages", "IotDevicesPage.tsx");

        Assert.Contains("/api/telemetry/devices/page", endpoints, StringComparison.Ordinal);
        Assert.Contains("/api/telemetry/devices/export", endpoints, StringComparison.Ordinal);
        Assert.Contains("const int maxViewPageSize = 100", endpoints, StringComparison.Ordinal);
        Assert.Contains("LIMIT @limit OFFSET @offset", endpoints, StringComparison.Ordinal);
        Assert.Contains("(@branchId::BIGINT IS NULL OR e.branch_id=@branchId)", endpoints, StringComparison.Ordinal);
        Assert.Contains("LIMIT 100000", endpoints, StringComparison.Ordinal);
        Assert.Contains("RequirePermission(http, \"telematics:devices:export\")", DeviceExportMethod(endpoints), StringComparison.Ordinal);
        Assert.DoesNotContain("api_key_hash", DeviceExportMethod(endpoints), StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("hmac_secret", DeviceExportMethod(endpoints), StringComparison.OrdinalIgnoreCase);

        Assert.Contains("getDevicePage(options: DevicePageOptions", service, StringComparison.Ordinal);
        Assert.Contains("pageSize: Math.min(100", service, StringComparison.Ordinal);
        Assert.Contains("downloadServerExport(\"/api/telemetry/devices/export\"", service, StringComparison.Ordinal);
        Assert.Contains("pageSize: 100", page, StringComparison.Ordinal);
        Assert.Contains("Page {devicePage} of {devicePageCount}", page, StringComparison.Ordinal);
        Assert.DoesNotContain("exportDevicesCsv()", page, StringComparison.Ordinal);
    }

    [Fact]
    public void TelematicsControlTower_UsesServerPagingAndFullFleetSummary()
    {
        var endpoints = Read("backend-dotnet", "Controllers", "EndpointMappings.cs");
        var schema = Read("backend-dotnet", "Services", "TelemetrySchemaService.cs");
        var service = Read("frontend", "src", "services", "telematicsService.ts");
        var page = Read("frontend", "src", "pages", "TelematicsControlTowerPage.tsx");

        Assert.Contains("getDevicePage({ page, pageSize", page, StringComparison.Ordinal);
        Assert.Contains("pageSize = 100", page, StringComparison.Ordinal);
        Assert.Contains("Search priority queue", page, StringComparison.Ordinal);
        Assert.Contains("Page {page} of {pageCount}", page, StringComparison.Ordinal);
        Assert.Contains("Export full device inventory", page, StringComparison.Ordinal);
        Assert.DoesNotContain("getGpsTrackingRecords()", page, StringComparison.Ordinal);
        Assert.DoesNotContain("getDiagnosticsRecords()", page, StringComparison.Ordinal);
        Assert.DoesNotContain("telematicsService.getDevices()", page, StringComparison.Ordinal);

        Assert.Contains("neverConnected: Number", service, StringComparison.Ordinal);
        Assert.Contains("Summary cards describe the complete authorized fleet/cluster", endpoints, StringComparison.Ordinal);
        Assert.Contains("e.status NOT IN ('Revoked','Retired') AND e.last_seen_at IS NULL) never_connected", endpoints, StringComparison.Ordinal);
        Assert.Contains("COUNT(*) FILTER (WHERE e.revoked_at IS NOT NULL OR e.status IN ('Revoked','Retired')) archived", endpoints, StringComparison.Ordinal);
        Assert.Contains("HasPermission(permissions, \"telematics:diagnostics:view\")", endpoints, StringComparison.Ordinal);
        Assert.Contains("HasPermission(permissions, \"telemetry.alerts.read\")", endpoints, StringComparison.Ordinal);
        Assert.Contains("faultAttentionClause", endpoints, StringComparison.Ordinal);
        Assert.Contains("\"priority\" => priorityExpression", endpoints, StringComparison.Ordinal);
        Assert.Contains("command.Parameters.AddWithValue(\"@search\", \"\")", endpoints, StringComparison.Ordinal);
        Assert.DoesNotContain("placeholderData: keepPreviousData", page, StringComparison.Ordinal);
        Assert.Contains("Highest risk first", page, StringComparison.Ordinal);
        Assert.Contains("sort === \"priority\" ? \"desc\" : \"asc\"", page, StringComparison.Ordinal);
        Assert.Contains("role=\"status\" aria-live=\"polite\" aria-busy=\"true\"", page, StringComparison.Ordinal);
        Assert.Contains("Updating priority queue…", page, StringComparison.Ordinal);
        Assert.Contains("const queueTransitionPending = searchPending || (query.isLoading && hasLoadedQueue.current)", page, StringComparison.Ordinal);
        Assert.Contains("const lastSuccessfulSummary = useRef<DevicePageResult[\"summary\"] | null>(null)", page, StringComparison.Ordinal);
        Assert.Contains("lastSuccessfulSummary.current = query.data.summary", page, StringComparison.Ordinal);
        Assert.Contains("const summary = query.data?.summary ?? lastSuccessfulSummary.current", page, StringComparison.Ordinal);
        Assert.Contains("aria-busy={queueTransitionPending}", page, StringComparison.Ordinal);
        Assert.Contains("!queueTransitionPending ? <div", page, StringComparison.Ordinal);
        Assert.Contains("const governedHold = /quarantined|suspended/i.test(device.deviceState)", page, StringComparison.Ordinal);
        Assert.Contains("Resolve the governed device hold before returning it to service", page, StringComparison.Ordinal);
        Assert.Contains("LOWER(COALESCE(e.device_state,'')) NOT IN ('quarantined','suspended')", endpoints, StringComparison.Ordinal);
        Assert.Contains("idx_lvp_company_device_received", schema, StringComparison.Ordinal);
        Assert.Contains("\"delayed-gps\"", endpoints, StringComparison.Ordinal);
        Assert.Contains("\"fixtime\" when cluster == \"gps\"", endpoints, StringComparison.Ordinal);
        Assert.Contains("\"freshness\" when cluster == \"gps\"", endpoints, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("=HYPERLINK(\"https://invalid.example\")", "'=HYPERLINK")]
    [InlineData("+SUM(1,2)", "'+SUM")]
    [InlineData("@cmd", "'@cmd")]
    public void ServerCsvExport_NeutralizesSpreadsheetFormulas(string input, string expectedPrefix)
    {
        Assert.Contains(expectedPrefix, Opstrax.Api.Controllers.EndpointMappings.CsvCell(input), StringComparison.Ordinal);
    }

    private static string DeviceExportMethod(string source)
    {
        var start = source.IndexOf("private static async Task<IResult> TelemetryDeviceExport", StringComparison.Ordinal);
        var end = source.IndexOf("private static IResult DevicesImportTemplate", start, StringComparison.Ordinal);
        Assert.True(start >= 0 && end > start);
        return source[start..end];
    }
}
