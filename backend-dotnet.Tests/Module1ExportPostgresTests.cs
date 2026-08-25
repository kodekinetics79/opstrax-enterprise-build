using System.Reflection;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Opstrax.Api.Controllers;
using Opstrax.Api.Data;

namespace Opstrax.Tests;

[Trait("Category", "Integration")]
public sealed class Module1ExportPostgresTests
{
    [Fact]
    public async Task EmptyBranchScopedAssetAndDeviceExportsReturnCsvFiles()
    {
        var db = Db();
        var companyId = await db.InsertAsync(
            "INSERT INTO companies(company_code,name,industry) VALUES (@code,'Module 1 export test','Transportation')",
            c => c.Parameters.AddWithValue("@code", $"M1E-{Guid.NewGuid():N}"));
        var branchId = await db.InsertAsync(
            "INSERT INTO branches(company_id,branch_code,name,status) VALUES (@cid,@code,'Export Branch','Active')",
            c =>
            {
                c.Parameters.AddWithValue("@cid", companyId);
                c.Parameters.AddWithValue("@code", $"EXP-{Guid.NewGuid():N}"[..20]);
            });

        try
        {
            var http = Principal(companyId, branchId);
            var assetResult = await Invoke(typeof(FleetTmsColdChainEndpoints), "AssetsExport", http, db, CancellationToken.None);
            var deviceResult = await Invoke(typeof(EndpointMappings), "TelemetryDeviceExport", http, db, CancellationToken.None);

            Assert.EndsWith(".csv", Assert.IsAssignableFrom<IFileHttpResult>(assetResult).FileDownloadName);
            Assert.EndsWith(".csv", Assert.IsAssignableFrom<IFileHttpResult>(deviceResult).FileDownloadName);
            Assert.Equal("text/csv", Assert.IsAssignableFrom<IContentTypeHttpResult>(assetResult).ContentType);
            Assert.Equal("text/csv", Assert.IsAssignableFrom<IContentTypeHttpResult>(deviceResult).ContentType);
        }
        finally
        {
            await db.ExecuteAsync("DELETE FROM branches WHERE id=@id AND company_id=@cid", c =>
            {
                c.Parameters.AddWithValue("@id", branchId);
                c.Parameters.AddWithValue("@cid", companyId);
            });
            await db.ExecuteAsync("DELETE FROM companies WHERE id=@cid", c => c.Parameters.AddWithValue("@cid", companyId));
        }
    }

    private static DefaultHttpContext Principal(long companyId, long branchId)
    {
        var http = new DefaultHttpContext();
        http.Items[EndpointMappings.AuthUserIdItemKey] = 41L;
        http.Items[EndpointMappings.AuthCompanyIdItemKey] = companyId;
        http.Items[EndpointMappings.AuthBranchIdItemKey] = branchId;
        http.Items[EndpointMappings.AuthRoleItemKey] = "Fleet Manager";
        http.Items[EndpointMappings.AuthPermissionsItemKey] = new[] { "telematics:devices:export" };
        return http;
    }

    private static async Task<IResult> Invoke(Type owner, string name, params object[] args)
    {
        var method = owner.GetMethod(name, BindingFlags.NonPublic | BindingFlags.Static)
            ?? throw new InvalidOperationException($"Missing endpoint {owner.Name}.{name}");
        return await ((Task<IResult>)method.Invoke(null, args)!);
    }

    private static Database Db() => new(new ConfigurationBuilder()
        .AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["ConnectionStrings:DefaultConnection"] = TestDb.ConnectionString,
            ["Rls:EnforceTenantContext"] = "false",
        }).Build(), new TenantScopeAccessor());
}
