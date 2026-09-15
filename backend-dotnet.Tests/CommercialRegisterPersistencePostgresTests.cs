using System.Reflection;
using System.Runtime.ExceptionServices;
using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Opstrax.Api.Controllers;
using Opstrax.Api.Data;
using Opstrax.Api.Services;

namespace Opstrax.Tests;

public sealed class CommercialRegisterPersistencePostgresTests
{
    [Fact]
    public async Task VisibleCommercialFields_SurviveCreateAndReload()
    {
        var db = CreateDatabase();
        await new AlertWorkflowSchemaService(db).EnsureAsync();
        var suffix = Guid.NewGuid().ToString("N")[..10];
        var companyId = await db.InsertAsync(
            "INSERT INTO companies(company_code,name,industry) VALUES (@code,'Commercial relationship test','Transportation')",
            c => c.Parameters.AddWithValue("@code", $"COM-{suffix}"));
        var http = Principal(companyId);

        try
        {
            await Create(db, http, "leads", new()
            {
                ["title"] = "Northwind Foods",
                ["status"] = "New",
                ["contactPerson"] = "Morgan Lee",
                ["industry"] = "Cold Chain",
                ["source"] = "Referral",
                ["requiredService"] = "Refrigerated transport",
                ["estimatedMonthlyLoads"] = 42,
                ["cityCountry"] = "Toronto, CA",
                ["assignedRep"] = "Avery Stone",
                ["nextFollowUp"] = "2026-10-01T14:30:00Z",
            });
            await Create(db, http, "opportunities", new()
            {
                ["title"] = "Northwind national lane",
                ["status"] = "Qualified",
                ["customerLead"] = "Northwind Foods",
                ["probability"] = 65,
                ["expectedCloseDate"] = "2026-11-15T00:00:00Z",
                ["owner"] = "Avery Stone",
                ["competitor"] = "Contoso Freight",
                ["expectedLoadsMonth"] = 35,
                ["estimatedContractValue"] = 275000m,
                ["currency"] = "CAD",
            });
            await Create(db, http, "quotations", new()
            {
                ["title"] = "Northwind Toronto-Montreal",
                ["status"] = "Draft",
                ["origin"] = "Toronto, ON",
                ["destination"] = "Montreal, QC",
                ["cargo"] = "Frozen food",
                ["quoteAmount"] = 4800m,
                ["currency"] = "CAD",
                ["margin"] = 18.5m,
                ["validUntil"] = "2026-10-31T00:00:00Z",
            });
            await Create(db, http, "campaigns", new()
            {
                ["title"] = "Cold-chain prospecting",
                ["status"] = "Scheduled",
                ["segment"] = "Food distributors",
                ["channel"] = "Email",
                ["audienceSize"] = 1250,
                ["startDate"] = "2026-10-05T00:00:00Z",
            });

            var lead = await Stored(db, companyId, "leads");
            Assert.Equal("Avery Stone", lead["ownerName"]);
            Assert.Equal("Toronto, CA", lead["locationName"]);
            AssertMetadata(lead, "contactPerson", "Morgan Lee", "estimatedMonthlyLoads", "42");

            var opportunity = await Stored(db, companyId, "opportunities");
            Assert.Equal(275000m, opportunity["amount"]);
            AssertMetadata(opportunity, "probability", "65", "currency", "CAD");

            var quotation = await Stored(db, companyId, "quotations");
            Assert.Equal(4800m, quotation["amount"]);
            AssertMetadata(quotation, "origin", "Toronto, ON", "cargo", "Frozen food");

            var campaign = await Stored(db, companyId, "campaigns");
            Assert.Null(campaign["amount"]);
            AssertMetadata(campaign, "segment", "Food distributors", "audienceSize", "1250");

            var listResult = await Invoke("GenericModule", http, "quotations", db, CancellationToken.None);
            var listJson = JsonSerializer.Serialize(
                Assert.IsAssignableFrom<IValueHttpResult>(listResult).Value,
                new JsonSerializerOptions(JsonSerializerDefaults.Web));
            Assert.Contains("Northwind Toronto-Montreal", listJson, StringComparison.Ordinal);
            Assert.Contains("\"origin\":\"Toronto, ON\"", listJson, StringComparison.Ordinal);
            Assert.Contains("\"currency\":\"CAD\"", listJson, StringComparison.Ordinal);
        }
        finally
        {
            await db.ExecuteAsync("DELETE FROM audit_logs WHERE company_id=@companyId", c => c.Parameters.AddWithValue("@companyId", companyId));
            await db.ExecuteAsync("DELETE FROM module_records WHERE company_id=@companyId", c => c.Parameters.AddWithValue("@companyId", companyId));
            await db.ExecuteAsync("DELETE FROM companies WHERE id=@companyId", c => c.Parameters.AddWithValue("@companyId", companyId));
        }
    }

    private static async Task Create(Database db, HttpContext http, string moduleKey, Dictionary<string, object?> body)
    {
        var result = await Invoke("CreateGenericModuleRecord", http, moduleKey, body, db, new AuditService(db), CancellationToken.None);
        Assert.Equal(StatusCodes.Status201Created, Assert.IsAssignableFrom<IStatusCodeHttpResult>(result).StatusCode);
    }

    private static async Task<Dictionary<string, object?>> Stored(Database db, long companyId, string moduleKey)
        => Assert.IsType<Dictionary<string, object?>>(await db.QuerySingleAsync(
            "SELECT * FROM module_records WHERE company_id=@companyId AND module_key=@moduleKey ORDER BY id DESC LIMIT 1",
            c =>
            {
                c.Parameters.AddWithValue("@companyId", companyId);
                c.Parameters.AddWithValue("@moduleKey", moduleKey);
            }));

    private static void AssertMetadata(Dictionary<string, object?> row, string firstKey, string firstValue, string secondKey, string secondValue)
    {
        using var metadata = JsonDocument.Parse(Assert.IsType<string>(row["metadataJson"]));
        Assert.Equal(firstValue, metadata.RootElement.GetProperty(firstKey).ToString());
        Assert.Equal(secondValue, metadata.RootElement.GetProperty(secondKey).ToString());
    }

    private static async Task<IResult> Invoke(string name, params object[] args)
    {
        var method = typeof(EndpointMappings).GetMethod(name, BindingFlags.NonPublic | BindingFlags.Static)!;
        try
        {
            return await (Task<IResult>)method.Invoke(null, args)!;
        }
        catch (TargetInvocationException ex) when (ex.InnerException is not null)
        {
            ExceptionDispatchInfo.Capture(ex.InnerException).Throw();
            throw;
        }
    }

    private static DefaultHttpContext Principal(long companyId)
    {
        var http = new DefaultHttpContext();
        http.Items[EndpointMappings.AuthCompanyIdItemKey] = companyId;
        http.Items[EndpointMappings.AuthBranchIdItemKey] = 1L;
        http.Items[EndpointMappings.AuthUserIdItemKey] = 0L;
        http.Items[EndpointMappings.AuthRoleItemKey] = "Commercial relationship tester";
        http.Items[EndpointMappings.AuthPermissionsItemKey] = new[] { "customers:view", "customers:manage" };
        return http;
    }

    private static Database CreateDatabase() => new(new ConfigurationBuilder().AddInMemoryCollection(
        new Dictionary<string, string?>
        {
            ["ConnectionStrings:DefaultConnection"] = TestDb.ConnectionString,
            ["Rls:EnforceTenantContext"] = "false",
        }).Build());
}
