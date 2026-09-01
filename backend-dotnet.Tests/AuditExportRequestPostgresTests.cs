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

        var rows = await db.QueryAsync(
            EndpointMappings.AuditExportRequestsListSql,
            command => command.Parameters.AddWithValue("@tenantId", 1L));

        Assert.True(rows.Count <= 20);
        Assert.All(rows, row => Assert.True(row.ContainsKey("createdAt")));
    }
}
