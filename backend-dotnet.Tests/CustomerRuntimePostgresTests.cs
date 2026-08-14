using System.Reflection;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Opstrax.Api.Controllers;
using Opstrax.Api.Data;
using Opstrax.Api.Services;

namespace Opstrax.Tests;

[Trait("Category", "Integration")]
public sealed class CustomerRuntimePostgresTests
{
    [Fact]
    public async Task CustomerList_ExecutesProtectedHealthContractWithoutManufacturedScores()
    {
        var db = Db();
        var suffix = Guid.NewGuid().ToString("N")[..10];
        var companyId = await db.InsertAsync(
            "INSERT INTO companies(company_code,name,industry) VALUES (@code,'Customer runtime test','Transportation')",
            c => c.Parameters.AddWithValue("@code", $"CUST-{suffix}"));
        try
        {
            Assert.Equal(1, await db.ScalarLongAsync(
                @"SELECT CASE WHEN has_table_privilege('opstrax_app','customers','SELECT')
                                  AND has_table_privilege('opstrax_app','customers','UPDATE')
                                  AND has_column_privilege('opstrax_app','customers','health_computed_at','UPDATE')
                               THEN 1 ELSE 0 END"));
            var customerId = await db.InsertAsync(
                @"INSERT INTO customers(company_id,customer_code,name,status)
                  VALUES (@company,@code,'Unrated evidence customer','Active')",
                c =>
                {
                    c.Parameters.AddWithValue("@company", companyId);
                    c.Parameters.AddWithValue("@code", $"C-{suffix}");
                });
            var http = new DefaultHttpContext();
            http.Items[EndpointMappings.AuthCompanyIdItemKey] = companyId;
            http.Items[EndpointMappings.AuthUserIdItemKey] = 1L;
            http.Items[EndpointMappings.AuthRoleItemKey] = "Customer runtime tester";
            http.Items[EndpointMappings.AuthPermissionsItemKey] = new[] { "customers:view" };

            var method = typeof(EndpointMappings).GetMethod("Customers", BindingFlags.NonPublic | BindingFlags.Static)!;
            var result = await Invoke(method, http, db, new CustomerHealthService(db), CancellationToken.None);

            Assert.Equal(StatusCodes.Status200OK, Assert.IsAssignableFrom<IStatusCodeHttpResult>(result).StatusCode);
            var row = await db.QuerySingleAsync(
                @"SELECT sla_health_score,delivery_experience_score,risk_score,health_state,health_computed_at
                  FROM customers WHERE id=@id AND company_id=@company",
                c => { c.Parameters.AddWithValue("@id", customerId); c.Parameters.AddWithValue("@company", companyId); });
            Assert.NotNull(row);
            Assert.Null(row!["slaHealthScore"]);
            Assert.Null(row["deliveryExperienceScore"]);
            Assert.Null(row["riskScore"]);
            Assert.Equal("insufficient_data", row["healthState"]);
            Assert.NotNull(row["healthComputedAt"]);
        }
        finally
        {
            await db.ExecuteAsync("DELETE FROM customers WHERE company_id=@company", c => c.Parameters.AddWithValue("@company", companyId));
            await db.ExecuteAsync("DELETE FROM companies WHERE id=@company", c => c.Parameters.AddWithValue("@company", companyId));
        }
    }

    private static async Task<IResult> Invoke(MethodInfo method, params object[] args)
    {
        try { return await (Task<IResult>)method.Invoke(null, args)!; }
        catch (TargetInvocationException ex) when (ex.InnerException is not null)
        { System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(ex.InnerException).Throw(); throw; }
    }

    private static Database Db() => new(new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
    {
        ["ConnectionStrings:DefaultConnection"] = TestDb.ConnectionString,
        ["Rls:EnforceTenantContext"] = "false"
    }).Build());
}
