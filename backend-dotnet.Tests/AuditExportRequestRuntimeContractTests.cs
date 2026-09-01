using Opstrax.Api.Controllers;

namespace Opstrax.Tests;

public sealed class AuditExportRequestRuntimeContractTests
{
    [Fact]
    public void ExportRequestReadPathUsesTheTimestampDefinedByTheRuntimeSchema()
    {
        var runtimeSchema = Read("backend-dotnet", "Services", "Batch7SchemaService.cs");
        var canonicalSchema = Read("database", "init", "001_schema.sql");

        Assert.Contains("ORDER BY request.created_at DESC", EndpointMappings.AuditExportRequestsListSql);
        Assert.Contains("AS export_format", EndpointMappings.AuditExportRequestsListSql);
        Assert.DoesNotContain("requested_at", EndpointMappings.AuditExportRequestsListSql);
        AssertAuditExportRequestSchemaUsesCreatedAt(runtimeSchema);
        AssertAuditExportRequestSchemaUsesCreatedAt(canonicalSchema);
    }

    [Fact]
    public void ExportRequestJourneyRendersTheCamelCaseDatabaseTimestamp()
    {
        var page = Read("frontend", "src", "pages", "AuditLogsPage.tsx");

        foreach (var field in new[] { "e.requestedByName", "e.dateFrom", "e.dateTo", "e.createdAt", "record.exportFormat" })
            Assert.Contains(field, page);
        Assert.DoesNotContain("e.requested_at", page);
        Assert.DoesNotContain("e.requested_by_name", page);
        Assert.DoesNotContain("e.date_range_start", page);
        Assert.DoesNotContain("e.date_range_end", page);
        Assert.DoesNotContain("e.export_format", page);
        Assert.Contains("Not recorded", page);
    }

    private static void AssertAuditExportRequestSchemaUsesCreatedAt(string source)
    {
        const string createTable = "CREATE TABLE IF NOT EXISTS audit_export_requests";
        var tableStart = source.IndexOf(createTable, StringComparison.Ordinal);
        Assert.True(tableStart >= 0, "audit_export_requests schema is missing");
        var tableEnd = source.IndexOf("CREATE TABLE IF NOT EXISTS", tableStart + createTable.Length, StringComparison.Ordinal);
        var table = source[tableStart..(tableEnd > tableStart ? tableEnd : source.Length)];

        Assert.Contains("created_at", table);
        Assert.DoesNotContain("requested_at", table);
    }

    private static string Read(params string[] parts)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !Directory.Exists(Path.Combine(directory.FullName, "backend-dotnet")))
            directory = directory.Parent;
        Assert.NotNull(directory);
        return File.ReadAllText(Path.Combine([directory.FullName, .. parts]));
    }
}
