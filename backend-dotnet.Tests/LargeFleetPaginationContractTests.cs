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
        Assert.Contains("Math.Clamp(parsedPageSize, 1, 100)", endpoints, StringComparison.Ordinal);
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
