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
