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
        var scope = Block(source, "private const string DocumentBranchScopeSql", "private static Task<IResult> MaintenanceItems");
        Assert.Contains("vehicles v_scope", scope, StringComparison.Ordinal);
        Assert.Contains("drivers dr_scope", scope, StringComparison.Ordinal);
        Assert.Contains("fleet_tms_assets a_scope", scope, StringComparison.Ordinal);
        Assert.Contains("company_id=d.company_id", scope, StringComparison.Ordinal);
        Assert.Contains("branch_id=@branchId", scope, StringComparison.Ordinal);

        var workflows = Block(source, "private static Task<IResult> Documents(", "private const string SafetySql");
        Assert.True(Count(workflows, "DocumentBranchScopeSql") >= 8,
            "Document list, summary, detail, update, delete, download, proxy, renewal and timeline must all fail closed to branch scope.");
        Assert.Contains("recommendations = GetBranchId(http) is null", workflows, StringComparison.Ordinal);
        Assert.Contains("_ => \"fleet_tms_assets\"", workflows, StringComparison.Ordinal);
        Assert.DoesNotContain("_ => \"assets\"", workflows, StringComparison.Ordinal);
        Assert.Contains("ValidateDocumentEntityAsync", workflows, StringComparison.Ordinal);
        Assert.Contains("Document issued date is invalid", workflows, StringComparison.Ordinal);
        Assert.Contains("Document expiry date is invalid", workflows, StringComparison.Ordinal);
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
        var page = File.ReadAllText(Path.Combine(RepoRoot, "frontend", "src", "pages", "Batch3OperationsPage.tsx"));
        var api = File.ReadAllText(Path.Combine(RepoRoot, "frontend", "src", "services", "documentsApi.ts"));
        Assert.Contains("form.file instanceof File", page, StringComparison.Ordinal);
        Assert.Contains("String(form.title", page, StringComparison.Ordinal);
        Assert.Contains("String(form.entityId", page, StringComparison.Ordinal);
        Assert.Contains("disabled={saving || !uploadReady}", page, StringComparison.Ordinal);
        Assert.Contains("role=\"alert\"", page, StringComparison.Ordinal);
        Assert.DoesNotContain("upload-placeholder", api, StringComparison.Ordinal);
        Assert.DoesNotContain("renew-placeholder", api, StringComparison.Ordinal);
        Assert.Contains("/renew`, {}", api, StringComparison.Ordinal);
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

        var workflows = Block(ReadMappings(), "private static async Task<IResult> CreateDocument(", "private static async Task<IResult> DeleteDocument(");
        Assert.DoesNotContain("file_url=COALESCE", workflows, StringComparison.Ordinal);
        var create = Block(workflows, "private static async Task<IResult> CreateDocument(", "private static async Task<IResult> UpdateDocument(");
        Assert.DoesNotContain("file_url", create, StringComparison.OrdinalIgnoreCase);
    }

    private static int Count(string source, string value) =>
        source.Split(value, StringSplitOptions.None).Length - 1;

    private static string ReadMappings() =>
        File.ReadAllText(Path.Combine(RepoRoot, "backend-dotnet", "Controllers", "EndpointMappings.cs"));

    private static string RepoRoot => Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../"));

    private static string Block(string source, string startMarker, string endMarker)
    {
        var start = source.IndexOf(startMarker, StringComparison.Ordinal);
        var end = source.IndexOf(endMarker, start + startMarker.Length, StringComparison.Ordinal);
        Assert.True(start >= 0 && end > start, $"Could not locate block {startMarker}");
        return source[start..end];
    }
}
