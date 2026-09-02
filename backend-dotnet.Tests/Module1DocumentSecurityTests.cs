using Opstrax.Api.Controllers;

namespace Opstrax.Tests;

public sealed class Module1DocumentSecurityTests
{
    [Theory]
    [InlineData("not-a-date", "2027-01-01", "issued")]
    [InlineData("2026-01-01", "tomorrow-ish", "expiry")]
    [InlineData("2026-08-26", "2026-08-25", "before")]
    public void InvalidOrReversedDatesAreRejected(string issuedAt, string expiresAt, string expected)
    {
        var errors = EndpointMappings.ValidateDocumentDateFields(new()
        {
            ["issuedAt"] = issuedAt,
            ["expiresAt"] = expiresAt
        });

        Assert.Contains(errors, error => error.Contains(expected, StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void ValidDatesAreAccepted()
    {
        Assert.Empty(EndpointMappings.ValidateDocumentDateFields(new()
        {
            ["issuedAt"] = "2026-08-25",
            ["expiresAt"] = "2027-08-25"
        }));
    }

    [Fact]
    public void EveryDocumentReadAndMutationUsesDerivedBranchScopeAndCurrentAssetMaster()
    {
        var source = ReadMappings();
        var lifecycle = ReadLifecycleMappings();
        var scope = Block(source, "private const string DocumentBranchScopeSql", "private static Task<IResult> MaintenanceItems");
        Assert.Contains("vehicles v_scope", scope, StringComparison.Ordinal);
        Assert.Contains("drivers dr_scope", scope, StringComparison.Ordinal);
        Assert.Contains("fleet_tms_assets a_scope", scope, StringComparison.Ordinal);
        Assert.Contains("company_id=d.company_id", scope, StringComparison.Ordinal);
        Assert.Contains("branch_id=@branchId", scope, StringComparison.Ordinal);

        var routes = Block(source, "app.MapGet(\"/api/documents/summary\"", "// Safety v1 legacy routes forwarded to v2 handlers");
        Assert.Contains("app.MapGet(\"/api/documents\", Documents)", routes, StringComparison.Ordinal);
        Assert.Contains("app.MapGet(\"/api/documents/{id:long}\", DocumentDetail)", routes, StringComparison.Ordinal);
        Assert.Contains("DocumentWriteKind.Create, parsed => CreateDocument", routes, StringComparison.Ordinal);
        Assert.Contains("DocumentWriteKind.Update, parsed => UpdateDocument", routes, StringComparison.Ordinal);
        Assert.Contains("app.MapDelete(\"/api/documents/{id:long}\", DeleteDocument)", routes, StringComparison.Ordinal);
        Assert.Contains("DocumentWriteKind.Renew, parsed => DocumentRenew", routes, StringComparison.Ordinal);
        Assert.Contains("app.MapGet(\"/api/documents/{id:long}/timeline\", DocumentTimeline)", routes, StringComparison.Ordinal);
        Assert.Contains("app.MapPost(\"/api/documents/upload\", DocumentUpload)", routes, StringComparison.Ordinal);
        Assert.Contains("app.MapGet(\"/api/documents/{id:long}/download\", DocumentDownload)", routes, StringComparison.Ordinal);
        Assert.Contains("app.MapGet(\"/api/files/{**key}\", FileProxyDownload)", routes, StringComparison.Ordinal);

        var expiring = Block(routes, "app.MapGet(\"/api/documents/expiring\"", "app.MapGet(\"/api/documents/recommendations\"");
        var recommendations = Block(routes, "app.MapGet(\"/api/documents/recommendations\"", "app.MapGet(\"/api/documents\", Documents)");
        var list = Block(source, "private static Task<IResult> Documents(", "private static async Task<IResult> DocumentsSummary(");
        var summary = Block(source, "private static async Task<IResult> DocumentsSummary(", "private static async Task<IResult> DocumentDetail(");
        var detail = Block(source, "private static async Task<IResult> DocumentDetail(", "private static Task<IResult> CreateDocument(");
        var download = Block(source, "private static async Task<IResult> DocumentDownload(", "private static async Task<IResult> FileProxyDownload(");
        var proxy = Block(source, "private static async Task<IResult> FileProxyDownload(", "private static long? ToNullableLong(");
        var timeline = Block(source, "private static async Task<IResult> DocumentTimeline(", "private const string SafetySql");
        foreach (var read in new[] { expiring, list, summary, detail, download, proxy, timeline })
            Assert.Contains("DocumentBranchScopeSql", read, StringComparison.Ordinal);
        Assert.Contains("@branchId::BIGINT IS NULL", recommendations, StringComparison.Ordinal);

        var lockDocument = Block(lifecycle, "private static async Task<Dictionary<string, object?>> LockDocument(", "private static async Task LockDocumentOwners(");
        var lockOwners = Block(lifecycle, "private static async Task LockDocumentOwners(", "private static (string Type, long Id) DocumentOwner(");
        var update = Block(lifecycle, "private static Task<IResult> UpdateLifecycleDocument(", "private static Task<IResult> DeleteLifecycleDocument(");
        var delete = Block(lifecycle, "private static Task<IResult> DeleteLifecycleDocument(", "private static async Task<IResult> UploadLifecycleDocument(");
        Assert.Contains("DocumentBranchScopeSql", lockDocument, StringComparison.Ordinal);
        Assert.Contains("company_id=@cid", lockOwners, StringComparison.Ordinal);
        Assert.Contains("branch_id=@branchId", lockOwners, StringComparison.Ordinal);
        Assert.Contains("\"asset\" => \"fleet_tms_assets\"", lockOwners, StringComparison.Ordinal);
        Assert.Contains("DocumentBranchScopeSql", update, StringComparison.Ordinal);
        Assert.Contains("DocumentBranchScopeSql", delete, StringComparison.Ordinal);
        Assert.Contains("recommendations = GetBranchId(http) is null", source, StringComparison.Ordinal);
        Assert.DoesNotContain("\"asset\" => \"assets\"", lockOwners, StringComparison.Ordinal);
        Assert.Contains("LockDocumentOwners", lifecycle, StringComparison.Ordinal);
        Assert.Contains("DocumentOwner", lifecycle, StringComparison.Ordinal);
        Assert.Contains("Document issued date is invalid", source, StringComparison.Ordinal);
        Assert.Contains("Document expiry date is invalid", source, StringComparison.Ordinal);
    }

    [Fact]
    public void EntityDisplaySubqueriesAreTenantCorrelated()
    {
        var source = ReadMappings();
        var sql = Block(source, "private const string DocumentsBaseSql", "private const string DocumentBranchScopeSql");
        Assert.Equal(4, Count(sql, "company_id=d.company_id"));
        Assert.Contains("fleet_tms_assets", sql, StringComparison.Ordinal);
    }

    [Fact]
    public void CustomerUploadRequiresRealFileAndVisibleRequiredMetadata()
    {
        var editor = File.ReadAllText(Path.Combine(RepoRoot, "frontend", "src", "components", "DocumentEditor.tsx"));
        var api = File.ReadAllText(Path.Combine(RepoRoot, "frontend", "src", "services", "documentsApi.ts"));
        var upload = Block(api, "  upload: (", "  update: (");
        var renewLine = api.Split('\n').Single(line => line.Contains("  renew:", StringComparison.Ordinal));
        Assert.Contains("form.file instanceof File", editor, StringComparison.Ordinal);
        Assert.Contains("String(form.title", editor, StringComparison.Ordinal);
        Assert.Contains("String(form.entityId", editor, StringComparison.Ordinal);
        Assert.Contains("[\"title\", \"entityType\", \"entityId\"]", editor, StringComparison.Ordinal);
        Assert.Contains("disabled={busy || requiresReload || !uploadReady}", editor, StringComparison.Ordinal);
        Assert.Contains("role=\"alert\"", editor, StringComparison.Ordinal);
        Assert.Contains("file instanceof File", upload, StringComparison.Ordinal);
        Assert.Contains("form.append(\"file\", file)", upload, StringComparison.Ordinal);
        Assert.Contains("/api/documents/upload\", form, sessionBoundRequest(session", upload, StringComparison.Ordinal);
        Assert.DoesNotContain("upload-placeholder", upload, StringComparison.Ordinal);
        Assert.DoesNotContain("renew-placeholder", renewLine, StringComparison.Ordinal);
        Assert.Contains("/renew`, { expectedVersion }, sessionBoundRequest(session)", renewLine, StringComparison.Ordinal);
    }

    [Fact]
    public void JsonMetadataWritesDiscardCustomerSuppliedFileReferences()
    {
        var body = new Dictionary<string, object?>
        {
            ["title"] = "Certification metadata",
            ["fileUrl"] = "https://attacker.invalid/file",
            ["file_url"] = "objkey:tenant/other-tenant/secret",
        };

        EndpointMappings.RemoveCustomerDocumentFileReference(body);

        Assert.False(body.ContainsKey("fileUrl"));
        Assert.False(body.ContainsKey("file_url"));
        Assert.Equal("Certification metadata", body["title"]);

        var lifecycle = ReadLifecycleMappings();
        var mappings = ReadMappings();
        var routes = Block(mappings, "app.MapGet(\"/api/documents/summary\"", "// Safety v1 legacy routes forwarded to v2 handlers");
        Assert.Contains("DocumentWriteKind.Create, parsed => CreateDocument", routes, StringComparison.Ordinal);
        Assert.Contains("DocumentWriteKind.Update, parsed => UpdateDocument", routes, StringComparison.Ordinal);
        Assert.Contains("DocumentWriteKind.Renew, parsed => DocumentRenew", routes, StringComparison.Ordinal);
        Assert.Contains("app.MapPost(\"/api/documents/upload\", DocumentUpload)", routes, StringComparison.Ordinal);

        var createWrapper = Block(mappings, "private static Task<IResult> CreateDocument(", "private static Task<IResult> UpdateDocument(");
        var updateWrapper = Block(mappings, "private static Task<IResult> UpdateDocument(", "private static Task<IResult> DeleteDocument(");
        var uploadWrapper = Block(mappings, "private static Task<IResult> DocumentUpload(", "// GET /api/documents/{id}/download");
        var renewWrapper = Block(mappings, "private static Task<IResult> DocumentRenew(", "private static async Task<IResult> DocumentTimeline(");
        Assert.Contains("CreateLifecycleDocument", createWrapper, StringComparison.Ordinal);
        Assert.Contains("UpdateLifecycleDocument", updateWrapper, StringComparison.Ordinal);
        Assert.Contains("UploadLifecycleDocument", uploadWrapper, StringComparison.Ordinal);
        Assert.Contains("UpdateLifecycleDocument", renewWrapper, StringComparison.Ordinal);

        var create = Block(lifecycle, "private static Task<IResult> CreateLifecycleDocument(", "private static async Task<Dictionary<string, object?>> InsertLifecycleDocument(");
        var insert = Block(lifecycle, "private static async Task<Dictionary<string, object?>> InsertLifecycleDocument(", "private static Task<IResult> UpdateLifecycleDocument(");
        var update = Block(lifecycle, "private static Task<IResult> UpdateLifecycleDocument(", "private static Task<IResult> DeleteLifecycleDocument(");
        var upload = Block(lifecycle, "private static async Task<IResult> UploadLifecycleDocument(", "\n}");
        Assert.Contains("RemoveCustomerDocumentFileReference(body)", create, StringComparison.Ordinal);
        Assert.Contains("RemoveCustomerDocumentFileReference(body)", update, StringComparison.Ordinal);
        Assert.Contains("RemoveCustomerDocumentFileReference(body)", upload, StringComparison.Ordinal);
        Assert.Contains("string? fileReference", insert, StringComparison.Ordinal);
        Assert.Contains("@file", insert, StringComparison.Ordinal);
        Assert.DoesNotContain("file_url=COALESCE", lifecycle, StringComparison.Ordinal);
        AssertOccursBefore(create, "RemoveCustomerDocumentFileReference(body)", "InsertLifecycleDocument");
        AssertOccursBefore(update, "RemoveCustomerDocumentFileReference(body)", "UPDATE documents d SET");
        AssertOccursBefore(upload, "RemoveCustomerDocumentFileReference(body)", "files.UploadAsync");
        AssertOccursBefore(upload, "files.UploadAsync", "InsertLifecycleDocument");
    }

    private static int Count(string source, string value) =>
        source.Split(value, StringSplitOptions.None).Length - 1;

    private static string ReadMappings() =>
        File.ReadAllText(Path.Combine(RepoRoot, "backend-dotnet", "Controllers", "EndpointMappings.cs"));

    private static string ReadLifecycleMappings() =>
        File.ReadAllText(Path.Combine(RepoRoot, "backend-dotnet", "Controllers", "DocumentLifecycleEndpoints.cs"));

    private static string RepoRoot => Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../"));

    private static void AssertOccursBefore(string source, string earlier, string later)
    {
        var earlierAt = source.IndexOf(earlier, StringComparison.Ordinal);
        var laterAt = source.IndexOf(later, StringComparison.Ordinal);
        Assert.True(earlierAt >= 0 && laterAt > earlierAt, $"Expected {earlier} before {later}");
    }

    private static string Block(string source, string startMarker, string endMarker)
    {
        var start = source.IndexOf(startMarker, StringComparison.Ordinal);
        var end = source.IndexOf(endMarker, start + startMarker.Length, StringComparison.Ordinal);
        Assert.True(start >= 0 && end > start, $"Could not locate block {startMarker}");
        return source[start..end];
    }
}
