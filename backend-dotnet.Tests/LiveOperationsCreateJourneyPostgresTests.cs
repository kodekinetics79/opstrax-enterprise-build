using System.Reflection;
using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Opstrax.Api.Controllers;
using Opstrax.Api.Data;

namespace Opstrax.Tests;

[Trait("Category", "Integration")]
public sealed class LiveOperationsCreateJourneyPostgresTests
{
    [Fact]
    public async Task JobCustomerOptionsAreMinimalTenantScopedActiveAndCreatePermissionBound()
    {
        var db = Db();
        var companyId = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() + Random.Shared.Next(3_000_000, 3_900_000);
        var foreignCompanyId = companyId + 1;
        await Company(db, companyId, "OWN");
        await Company(db, foreignCompanyId, "FOREIGN");
        try
        {
            await Customer(db, companyId, "OWN-A", "Acme Active", "Active");
            await Customer(db, companyId, "OWN-I", "Acme Inactive", "Inactive");
            await Customer(db, foreignCompanyId, "FOREIGN-A", "Acme Foreign", "Active");

            var creator = Principal(companyId, "shipments:create");
            creator.Request.QueryString = new QueryString("?search=Acme&limit=200");
            var result = await Invoke(creator, db);
            var json = Json(result);

            Assert.Equal(StatusCodes.Status200OK, Status(result));
            Assert.Contains("Acme Active", json, StringComparison.Ordinal);
            Assert.Contains("OWN-A", json, StringComparison.Ordinal);
            Assert.DoesNotContain("Acme Inactive", json, StringComparison.Ordinal);
            Assert.DoesNotContain("Acme Foreign", json, StringComparison.Ordinal);
            Assert.DoesNotContain("companyId", json, StringComparison.OrdinalIgnoreCase);

            var viewer = await Invoke(Principal(companyId, "shipments:view"), db);
            Assert.Equal(StatusCodes.Status403Forbidden, Status(viewer));
        }
        finally
        {
            await db.ExecuteAsync("DELETE FROM customers WHERE company_id IN (@own,@foreign)",
                c => { c.Parameters.AddWithValue("@own", companyId); c.Parameters.AddWithValue("@foreign", foreignCompanyId); });
            await db.ExecuteAsync("DELETE FROM companies WHERE id IN (@own,@foreign)",
                c => { c.Parameters.AddWithValue("@own", companyId); c.Parameters.AddWithValue("@foreign", foreignCompanyId); });
        }
    }

    private static DefaultHttpContext Principal(long companyId, params string[] permissions)
    {
        var http = new DefaultHttpContext();
        http.Items[EndpointMappings.AuthCompanyIdItemKey] = companyId;
        http.Items[EndpointMappings.AuthUserIdItemKey] = 42L;
        http.Items[EndpointMappings.AuthRoleItemKey] = "Dispatcher";
        http.Items[EndpointMappings.AuthPermissionsItemKey] = permissions;
        return http;
    }

    private static async Task<IResult> Invoke(HttpContext http, Database db)
    {
        var target = typeof(EndpointMappings).GetMethod("JobCustomerOptions", BindingFlags.NonPublic | BindingFlags.Static)!;
        try { return await (Task<IResult>)target.Invoke(null, [http, db, CancellationToken.None])!; }
        catch (TargetInvocationException ex) when (ex.InnerException is not null)
        { System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(ex.InnerException).Throw(); throw; }
    }

    private static Task Company(Database db, long id, string suffix) => db.ExecuteAsync(
        "INSERT INTO companies(id,company_code,name,industry) OVERRIDING SYSTEM VALUE VALUES (@id,@code,@name,'Transportation')",
        c => { c.Parameters.AddWithValue("@id", id); c.Parameters.AddWithValue("@code", $"LCJ-{suffix}-{id}"); c.Parameters.AddWithValue("@name", $"Create Journey {suffix}"); });

    private static Task Customer(Database db, long companyId, string code, string name, string status) => db.ExecuteAsync(
        "INSERT INTO customers(company_id,customer_code,name,status) VALUES (@companyId,@code,@name,@status)",
        c => { c.Parameters.AddWithValue("@companyId", companyId); c.Parameters.AddWithValue("@code", $"{code}-{companyId}"); c.Parameters.AddWithValue("@name", name); c.Parameters.AddWithValue("@status", status); });

    private static int? Status(IResult result) => Assert.IsAssignableFrom<IStatusCodeHttpResult>(result).StatusCode;
    private static string Json(IResult result) => JsonSerializer.Serialize(
        Assert.IsAssignableFrom<IValueHttpResult>(result).Value,
        new JsonSerializerOptions(JsonSerializerDefaults.Web));
    private static Database Db() => new(new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        { ["ConnectionStrings:DefaultConnection"] = TestDb.ConnectionString, ["Rls:EnforceTenantContext"] = "false" }).Build());
}

public sealed class LiveOperationsCreateJourneyContractTests
{
    [Fact]
    public void CustomerSelectionAndRouteErrorsAreWiredIntoTheRenderedWorkflows()
    {
        var jobs = Source("frontend", "src", "pages", "JobsPage.tsx");
        var routes = Source("frontend", "src", "pages", "RoutePlanningPage.tsx");
        var api = Source("frontend", "src", "services", "jobsApi.ts");

        Assert.Contains("jobsApi.customerOptions", jobs, StringComparison.Ordinal);
        Assert.Contains("Select an active customer", jobs, StringComparison.Ordinal);
        Assert.Contains("No active customers are available", jobs, StringComparison.Ordinal);
        Assert.Contains("useHasDirectPermission", jobs, StringComparison.Ordinal);
        Assert.Contains("canCreate ? <button", jobs, StringComparison.Ordinal);
        Assert.Contains("canImport ? <>", jobs, StringComparison.Ordinal);
        Assert.Contains("canExport ? <button", jobs, StringComparison.Ordinal);
        Assert.DoesNotContain("[\"customerId\", \"Customer ID\"]", jobs, StringComparison.Ordinal);
        Assert.Contains("/api/jobs/customer-options", api, StringComparison.Ordinal);
        Assert.Contains("prepareRouteForm(form)", routes, StringComparison.Ordinal);
        Assert.Contains("role=\"alert\"", routes, StringComparison.Ordinal);
        Assert.Contains("apiErrorMessage(save.error", routes, StringComparison.Ordinal);
        Assert.Contains("Save Route", routes, StringComparison.Ordinal);
    }

    private static string Source(params string[] parts)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !Directory.Exists(Path.Combine(dir.FullName, "backend-dotnet"))) dir = dir.Parent;
        Assert.NotNull(dir);
        return File.ReadAllText(Path.Combine([dir!.FullName, .. parts]));
    }
}
