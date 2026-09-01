using Microsoft.Extensions.Configuration;
using Opstrax.Api.Controllers;
using Opstrax.Api.Data;

namespace Opstrax.Tests;

public sealed class AuditExportRequestPostgresTests
{
    [Fact]
    [Trait("Category", "Integration")]
    public async Task ListQueryExecutesAgainstTheMaterializedRuntimeTable()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:DefaultConnection"] = TestDb.ConnectionString,
            })
            .Build();
        var db = new Database(configuration);
        var tenantId = 9_100_000_000L + Random.Shared.Next(1, 1_000_000);

        try
        {
            await db.ExecuteAsync(
                """
                INSERT INTO audit_export_requests
                    (tenant_id, requested_by_name, date_from, date_to, filters_json, status, created_at)
                VALUES
                    (@tenantId, 'Older Request', '2026-08-01', '2026-08-02', '{"exportFormat":"CSV"}'::jsonb, 'Completed', '2026-08-03T10:00:00Z'),
                    (@tenantId, 'Newer Request', '2026-08-04', '2026-08-05', '{"exportFormat":"PDF"}'::jsonb, 'Pending',   '2026-08-06T12:00:00Z')
                """,
                command => command.Parameters.AddWithValue("@tenantId", tenantId));

            var rows = await db.QueryAsync(
                EndpointMappings.AuditExportRequestsListSql,
                command => command.Parameters.AddWithValue("@tenantId", tenantId));

            Assert.Collection(rows,
                newer =>
                {
                    Assert.Equal("Newer Request", newer["requestedByName"]);
                    Assert.Equal("PDF", newer["exportFormat"]);
                    Assert.NotNull(newer["dateFrom"]);
                    Assert.NotNull(newer["dateTo"]);
                    Assert.NotNull(newer["createdAt"]);
                },
                older =>
                {
                    Assert.Equal("Older Request", older["requestedByName"]);
                    Assert.Equal("CSV", older["exportFormat"]);
                    Assert.NotNull(older["createdAt"]);
                });
        }
        finally
        {
            await db.ExecuteAsync(
                "DELETE FROM audit_export_requests WHERE tenant_id=@tenantId",
                command => command.Parameters.AddWithValue("@tenantId", tenantId));
        }
    }
}
