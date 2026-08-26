using Microsoft.AspNetCore.Http;
using Opstrax.Api.Controllers;

namespace Opstrax.Tests;

public sealed class Module1BrowserWorkflowContractTests
{
    [Fact]
    public void FleetLifecycleUsesAnInProductConfirmationInsteadOfNativeConfirm()
    {
        var source = Read("frontend", "src", "pages", "EntityListPage.tsx");

        Assert.DoesNotContain("window.confirm", source, StringComparison.Ordinal);
        Assert.Contains("pendingArchive", source, StringComparison.Ordinal);
        Assert.Contains("aria-labelledby=\"archive-confirm-title\"", source, StringComparison.Ordinal);
        Assert.Contains("deleteMutation.mutate(String(pendingArchive.id))", source, StringComparison.Ordinal);
        var modal = Block(source, "{pendingArchive ? (", "{editing &&");
        Assert.Contains("deleteMutation.error", modal, StringComparison.Ordinal);
        Assert.Contains("fleetArchiveErrorMessage(deleteMutation.error)", modal, StringComparison.Ordinal);
        Assert.Contains("role=\"alert\"", modal, StringComparison.Ordinal);
        Assert.Contains("deleteMutation.reset()", modal, StringComparison.Ordinal);
    }

    [Fact]
    public void FullDatasetExportsRequireExplicitExportPermissions()
    {
        var backend = Read("backend-dotnet", "Controllers", "EndpointMappings.cs");
        var routes = Block(backend, "app.MapGet(\"/api/vehicles/export\"", "app.MapGet(\"/api/jobs/export\"");
        var exports = Block(backend, "private static Task<IResult> VehiclesExport(", "// Keep user-controlled identifiers/names");

        Assert.Contains("VehiclesExport", routes, StringComparison.Ordinal);
        Assert.Contains("DriversExport", routes, StringComparison.Ordinal);
        Assert.Contains("ExportCsv(http, db, \"vehicles:export\"", exports, StringComparison.Ordinal);
        Assert.Contains("ExportCsv(http, db, \"drivers:export\"", exports, StringComparison.Ordinal);
        Assert.DoesNotContain("ExportCsv(http, db, \"vehicles:view\"", exports, StringComparison.Ordinal);
        Assert.DoesNotContain("ExportCsv(http, db, \"drivers:view\"", exports, StringComparison.Ordinal);
        Assert.Contains("b.branch_code", exports, StringComparison.Ordinal);
        Assert.Contains("v.vehicle_class", exports, StringComparison.Ordinal);
        Assert.Contains("v.vin_exception_type", exports, StringComparison.Ordinal);
        Assert.Contains("v.alternate_identifier", exports, StringComparison.Ordinal);
        Assert.Contains("v.plate_jurisdiction", exports, StringComparison.Ordinal);
        Assert.Contains("d.readiness_score", exports, StringComparison.Ordinal);
        Assert.Contains("d.risk_score", exports, StringComparison.Ordinal);
        Assert.Contains("LEFT JOIN branches b ON b.id=v.branch_id AND b.company_id=v.company_id", exports, StringComparison.Ordinal);
        Assert.Contains("LEFT JOIN branches b ON b.id=d.branch_id AND b.company_id=d.company_id", exports, StringComparison.Ordinal);
        Assert.Contains("\"v.vehicle_code,v.id\"", exports, StringComparison.Ordinal);
        Assert.Contains("\"d.driver_code,d.id\"", exports, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("vehicles:view", "vehicles:export")]
    [InlineData("drivers:view", "drivers:export")]
    public void ViewOnlyPrincipalCannotSatisfyFleetMasterExportPermission(string held, string required)
    {
        var http = new DefaultHttpContext();
        http.Items[EndpointMappings.AuthUserIdItemKey] = 7L;
        http.Items[EndpointMappings.AuthCompanyIdItemKey] = 4242L;
        http.Items[EndpointMappings.AuthRoleItemKey] = "Fleet Manager";
        http.Items[EndpointMappings.AuthPermissionsItemKey] = new[] { held };

        var denied = EndpointMappings.RequirePermission(http, required);

        Assert.NotNull(denied);
        Assert.Equal(StatusCodes.Status403Forbidden,
            Assert.IsAssignableFrom<IStatusCodeHttpResult>(denied).StatusCode);
    }

    [Theory]
    [InlineData("vehicles:export")]
    [InlineData("drivers:export")]
    public void ExplicitFleetMasterExportPermissionIsAccepted(string permission)
    {
        var http = new DefaultHttpContext();
        http.Items[EndpointMappings.AuthUserIdItemKey] = 7L;
        http.Items[EndpointMappings.AuthCompanyIdItemKey] = 4242L;
        http.Items[EndpointMappings.AuthRoleItemKey] = "Fleet Manager";
        http.Items[EndpointMappings.AuthPermissionsItemKey] = new[] { permission };

        Assert.Null(EndpointMappings.RequirePermission(http, permission));
    }

    [Fact]
    public void LargeFleetListsPageAfterSortingAndExportTheFullServerDataset()
    {
        var domain = Read("frontend", "src", "services", "fleetDomainApi.ts");
        var page = Read("frontend", "src", "pages", "EntityListPage.tsx");
        var table = Read("frontend", "src", "components", "ui.tsx");

        Assert.Contains("limit: 2000", domain, StringComparison.Ordinal);
        Assert.Contains("/api/drivers/export", page, StringComparison.Ordinal);
        Assert.Contains("/api/vehicles/export", page, StringComparison.Ordinal);
        Assert.Contains("const pageSize = 100", table, StringComparison.Ordinal);
        Assert.Contains("sorted.slice(page * pageSize", table, StringComparison.Ordinal);
        Assert.Contains("Page {page + 1} of {pageCount}", table, StringComparison.Ordinal);
    }

    [Fact]
    public void DriverRosterUsesLifecycleActiveAndTheAuthoritativeBranchAwareImportWorkflow()
    {
        var roster = Read("frontend", "src", "pages", "EntityListPage.tsx");
        var module = Read("frontend", "src", "pages", "DriversModulePage.tsx");
        var messaging = Read("frontend", "src", "pages", "DriverMessagingPage.tsx");

        Assert.Contains("(kind === \"vehicles\" || kind === \"drivers\") && statusFilter === \"Active\"", roster, StringComparison.Ordinal);
        Assert.Contains("kind === \"drivers\" && canCreate", roster, StringComparison.Ordinal);
        Assert.Contains("templateEndpoint: \"/api/drivers/import-template\"", roster, StringComparison.Ordinal);
        Assert.Contains("columns: [\"driverCode\", \"branchCode\"", roster, StringComparison.Ordinal);
        Assert.Contains("importPreview: driversApi.importPreview", roster, StringComparison.Ordinal);
        Assert.Contains("importCommit: driversApi.importCommit", roster, StringComparison.Ordinal);
        Assert.Contains("canExport={hasPermission(\"drivers:export\")}", module, StringComparison.Ordinal);

        // Driver pickers must neither poison the canonical module roster cache nor
        // retain the raw API envelope under an array-shaped query key.
        Assert.Contains("[\"drivers\", \"module\", \"active\"]", module, StringComparison.Ordinal);
        Assert.Contains("[\"driver-messaging\", \"driver-options\"]", messaging, StringComparison.Ordinal);
        Assert.Contains("unwrap<AnyRecord[]>(apiClient.get(\"/api/drivers\"))", messaging, StringComparison.Ordinal);
    }

    [Fact]
    public void DocumentWorkflowUploadsARealFileWithEntityAndExpiryMetadata()
    {
        var api = Read("frontend", "src", "services", "documentsApi.ts");
        var page = Read("frontend", "src", "pages", "Batch3OperationsPage.tsx");
        var backend = Read("backend-dotnet", "Controllers", "EndpointMappings.cs");

        Assert.Contains("new FormData()", api, StringComparison.Ordinal);
        Assert.Contains("/api/documents/upload", api, StringComparison.Ordinal);
        Assert.Contains("type=\"file\"", page, StringComparison.Ordinal);
        Assert.Contains("kind === \"documents\"", page, StringComparison.Ordinal);

        var upload = Block(backend, "private static async Task<IResult> DocumentUpload(", "// GET /api/documents/{id}/download");
        Assert.Contains("Choose a vehicle, driver, or asset", upload, StringComparison.Ordinal);
        Assert.Contains("branch_id=@branchId", upload, StringComparison.Ordinal);
        Assert.Contains("expires_at", upload, StringComparison.Ordinal);
        Assert.Contains("Renewal Required", upload, StringComparison.Ordinal);
        Assert.Contains("files.DeleteAsync(stored.Reference", upload, StringComparison.Ordinal);
    }

    private static string RepoRoot => Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../"));
    private static string Read(params string[] parts) => File.ReadAllText(Path.Combine([RepoRoot, .. parts]));

    private static string Block(string source, string startMarker, string endMarker)
    {
        var start = source.IndexOf(startMarker, StringComparison.Ordinal);
        var end = source.IndexOf(endMarker, start + startMarker.Length, StringComparison.Ordinal);
        Assert.True(start >= 0 && end > start, $"Could not locate block {startMarker}");
        return source[start..end];
    }
}
