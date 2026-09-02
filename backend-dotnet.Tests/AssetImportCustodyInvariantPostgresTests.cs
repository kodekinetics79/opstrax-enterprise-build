using System.Reflection;
using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Opstrax.Api.Controllers;
using Opstrax.Api.Data;
using Opstrax.Api.Services;

namespace Opstrax.Tests;

[Trait("Category", "Integration")]
public sealed class AssetImportCustodyInvariantPostgresTests
{
    [Fact]
    public async Task ImportRejectsActiveCustodyChangesAtomicallyAndAllowsSafeMetadataUpdates()
    {
        var db = Db();
        await new FleetTmsColdChainSchemaService(db, NullLogger<FleetTmsColdChainSchemaService>.Instance).EnsureAsync();
        await new FleetTmsColdChainFoundationSchemaService(db).EnsureAsync();
        var companyId = await db.InsertAsync(
            "INSERT INTO companies(company_code,name,industry) VALUES (@code,'Asset import custody test','Transportation')",
            command => command.Parameters.AddWithValue("@code", $"AIC-{Guid.NewGuid():N}"));
        var branchCode = $"AIC-{Guid.NewGuid():N}"[..20];
        var branchId = await db.InsertAsync(
            "INSERT INTO branches(company_id,branch_code,name,status) VALUES (@company,@code,'Custody Branch','Active')",
            command =>
            {
                command.Parameters.AddWithValue("@company", companyId);
                command.Parameters.AddWithValue("@code", branchCode);
            });

        try
        {
            var typeId = await db.InsertAsync(
                "INSERT INTO fleet_tms_asset_types(company_id,branch_id,code,name) VALUES (@company,NULL,'TRAILER','Trailer')",
                command => command.Parameters.AddWithValue("@company", companyId));
            var unassignedId = await InsertAsset(db, companyId, branchId, typeId, "AIC-FREE", "Unassigned original", "Available", "North Yard", 5m);
            var assignedId = await InsertAsset(db, companyId, branchId, typeId, "AIC-HELD", "Assigned original", "InUse", "Customer Dock", 5m);
            await db.ExecuteAsync(@"
INSERT INTO fleet_tms_asset_assignments(company_id,branch_id,asset_id,assignee_type,assignee_name,quantity,status)
VALUES (@company,@branch,@asset,'Customer','Certification Customer',2,'InUse')", command =>
            {
                command.Parameters.AddWithValue("@company", companyId);
                command.Parameters.AddWithValue("@branch", branchId);
                command.Parameters.AddWithValue("@asset", assignedId);
            });

            var http = Principal(companyId, branchId);
            var audit = new AuditService(db);

            var atomicFailure = await Commit(http, db, audit,
                Row("AIC-FREE", branchCode, "Must roll back", "Available", "North Yard", 5m, "atomic predecessor"),
                Row("AIC-HELD", branchCode, "Assigned original", "Available", "Customer Dock", 5m, "illegal status"));
            Assert.Equal(StatusCodes.Status400BadRequest, Status(atomicFailure));
            var atomicPayload = Payload(atomicFailure);
            Assert.Contains("row 2", atomicPayload, StringComparison.Ordinal);
            Assert.Contains("AIC-HELD", atomicPayload, StringComparison.Ordinal);
            Assert.Contains("status must remain", atomicPayload, StringComparison.Ordinal);
            Assert.Contains("InUse", atomicPayload, StringComparison.Ordinal);
            Assert.Contains("No rows were changed", atomicPayload, StringComparison.Ordinal);
            Assert.Equal("Unassigned original", await AssetValue(db, unassignedId, "name"));
            Assert.Equal("InUse", await AssetValue(db, assignedId, "status"));

            var locationFailure = await Commit(http, db, audit,
                Row("AIC-HELD", branchCode, "Assigned original", "InUse", "Remote Yard", 5m, "illegal move"));
            Assert.Equal(StatusCodes.Status400BadRequest, Status(locationFailure));
            Assert.Contains("currentLocation must remain", Payload(locationFailure), StringComparison.Ordinal);
            Assert.Contains("Customer Dock", Payload(locationFailure), StringComparison.Ordinal);
            Assert.Equal("Customer Dock", await AssetValue(db, assignedId, "current_location"));

            var quantityFailure = await Commit(http, db, audit,
                Row("AIC-HELD", branchCode, "Assigned original", "InUse", "Customer Dock", 1m, "illegal quantity"));
            Assert.Equal(StatusCodes.Status400BadRequest, Status(quantityFailure));
            Assert.Contains("active custody quantity 2", Payload(quantityFailure), StringComparison.Ordinal);
            Assert.Equal("5.00", await AssetValue(db, assignedId, "quantity"));

            var safe = await Commit(http, db, audit,
                Row("AIC-FREE", branchCode, "Unassigned metadata updated", "Available", "North Yard", 6m, "safe unassigned update"),
                Row("AIC-HELD", branchCode, "Assigned metadata updated", "InUse", "Customer Dock", 5m, "safe assigned metadata"));
            Assert.Equal(StatusCodes.Status200OK, Status(safe));
            Assert.Equal("Unassigned metadata updated", await AssetValue(db, unassignedId, "name"));
            Assert.Equal("Assigned metadata updated", await AssetValue(db, assignedId, "name"));
            Assert.Equal("InUse", await AssetValue(db, assignedId, "status"));
            Assert.Equal("Customer Dock", await AssetValue(db, assignedId, "current_location"));
            Assert.Equal(1, await db.ScalarLongAsync(@"
SELECT COUNT(*) FROM fleet_tms_asset_assignments
WHERE company_id=@company AND asset_id=@asset AND released_at_utc IS NULL", command =>
            {
                command.Parameters.AddWithValue("@company", companyId);
                command.Parameters.AddWithValue("@asset", assignedId);
            }));
        }
        finally
        {
            foreach (var sql in new[]
            {
                "DELETE FROM audit_logs WHERE company_id=@company",
                "DELETE FROM fleet_tms_asset_events WHERE company_id=@company",
                "DELETE FROM fleet_tms_asset_assignments WHERE company_id=@company",
                "DELETE FROM fleet_tms_assets WHERE company_id=@company",
                "DELETE FROM fleet_tms_asset_types WHERE company_id=@company",
                "DELETE FROM branches WHERE company_id=@company",
                "DELETE FROM companies WHERE id=@company",
            })
                await db.ExecuteAsync(sql, command => command.Parameters.AddWithValue("@company", companyId));
        }
    }

    private static object Row(string tag, string branchCode, string name, string status, string location, decimal quantity, string notes) => new
    {
        assetTag = tag,
        branchCode,
        name,
        assetTypeCode = "TRAILER",
        status,
        currentLocation = location,
        condition = "Good",
        isReturnable = true,
        quantity,
        unitOfMeasure = "Each",
        notes,
    };

    private static async Task<IResult> Commit(DefaultHttpContext http, Database db, AuditService audit, params object[] rows)
    {
        var body = JsonSerializer.Deserialize<Dictionary<string, object?>>(JsonSerializer.Serialize(new { rows }))!;
        return await Invoke("AssetsImportCommit", http, body, db, audit, CancellationToken.None);
    }

    private static async Task<long> InsertAsset(Database db, long companyId, long branchId, long typeId,
        string tag, string name, string status, string location, decimal quantity)
        => await db.InsertAsync(@"
INSERT INTO fleet_tms_assets(company_id,branch_id,asset_type_id,asset_tag,name,status,current_location,condition,is_returnable,quantity,unit_of_measure,notes)
VALUES (@company,@branch,@type,@tag,@name,@status,@location,'Good',true,@quantity,'Each','original')", command =>
        {
            command.Parameters.AddWithValue("@company", companyId);
            command.Parameters.AddWithValue("@branch", branchId);
            command.Parameters.AddWithValue("@type", typeId);
            command.Parameters.AddWithValue("@tag", tag);
            command.Parameters.AddWithValue("@name", name);
            command.Parameters.AddWithValue("@status", status);
            command.Parameters.AddWithValue("@location", location);
            command.Parameters.AddWithValue("@quantity", quantity);
        });

    private static async Task<string> AssetValue(Database db, long id, string column)
        => (await db.QuerySingleAsync($"SELECT {column} value FROM fleet_tms_assets WHERE id=@id",
            command => command.Parameters.AddWithValue("@id", id)))!["value"]?.ToString() ?? "";

    private static int? Status(IResult result) => Assert.IsAssignableFrom<IStatusCodeHttpResult>(result).StatusCode;

    private static string Payload(IResult result)
        => JsonSerializer.Serialize(Assert.IsAssignableFrom<IValueHttpResult>(result).Value,
            new JsonSerializerOptions(JsonSerializerDefaults.Web));

    private static DefaultHttpContext Principal(long companyId, long branchId)
    {
        var http = new DefaultHttpContext();
        http.Items[EndpointMappings.AuthCompanyIdItemKey] = companyId;
        http.Items[EndpointMappings.AuthBranchIdItemKey] = branchId;
        http.Items[EndpointMappings.AuthUserIdItemKey] = 42L;
        http.Items[EndpointMappings.AuthRoleItemKey] = "Fleet Manager";
        http.Items[EndpointMappings.AuthPermissionsItemKey] = new[] { "fleet:view", "fleet:manage" };
        return http;
    }

    private static async Task<IResult> Invoke(string methodName, params object[] arguments)
    {
        var method = typeof(FleetTmsColdChainEndpoints).GetMethod(methodName, BindingFlags.NonPublic | BindingFlags.Static)
            ?? throw new InvalidOperationException($"Missing endpoint {methodName}");
        try { return await (Task<IResult>)method.Invoke(null, arguments)!; }
        catch (TargetInvocationException ex) when (ex.InnerException is not null)
        {
            System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(ex.InnerException).Throw();
            throw;
        }
    }

    private static Database Db() => new(new ConfigurationBuilder()
        .AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["ConnectionStrings:DefaultConnection"] = TestDb.ConnectionString,
            ["Rls:EnforceTenantContext"] = "false",
        }).Build(), new TenantScopeAccessor());
}

public sealed class AssetImportCustodyInvariantContractTests
{
    [Fact]
    public void AssetImportCommitLocksCustodyAndRollsBackTheWholeMutationSet()
    {
        var root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../"));
        var source = File.ReadAllText(Path.Combine(root, "backend-dotnet", "Controllers", "FleetTmsColdChainEndpoints.cs"));
        var start = source.IndexOf(" AssetsImportCommit(", StringComparison.Ordinal);
        var end = source.IndexOf("\n    private static async Task<IResult> AssetDetail(", start, StringComparison.Ordinal);
        Assert.True(start >= 0 && end > start, "AssetsImportCommit source block must exist");
        var commit = source[start..end];

        Assert.Contains("WithTransactionAsync", commit, StringComparison.Ordinal);
        Assert.Contains("SAVEPOINT assets_import_commit", commit, StringComparison.Ordinal);
        Assert.Contains("ROLLBACK TO SAVEPOINT assets_import_commit", commit, StringComparison.Ordinal);
        Assert.Contains("LockAsset(connection, transaction", commit, StringComparison.Ordinal);
        Assert.Contains("ActiveCustodyState(connection, transaction", commit, StringComparison.Ordinal);
        Assert.Contains("status must remain", commit, StringComparison.Ordinal);
        Assert.Contains("currentLocation must remain", commit, StringComparison.Ordinal);
        Assert.Contains("cannot be reduced below active custody quantity", commit, StringComparison.Ordinal);
        Assert.Contains("No rows were changed", commit, StringComparison.Ordinal);
    }
}
