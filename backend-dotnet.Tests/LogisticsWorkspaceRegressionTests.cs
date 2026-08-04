using System.Reflection;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Opstrax.Api.Controllers;
using Opstrax.Api.Data;
using Opstrax.Api.Seed;
using Opstrax.Api.Services;

namespace Opstrax.Tests;

public sealed class LogisticsWorkspaceRegressionTests
{
    [Theory]
    [InlineData("=1+1", "\"'=1+1\"")]
    [InlineData(" +SUM(A1:A2)", "\"' +SUM(A1:A2)\"")]
    [InlineData("-2+3", "\"'-2+3\"")]
    [InlineData("@SUM(A1:A2)", "\"'@SUM(A1:A2)\"")]
    [InlineData("ordinary \"quoted\" value", "\"ordinary \"\"quoted\"\" value\"")]
    public void LastMileCsvCell_PreventsSpreadsheetFormulaExecution(string input, string expected)
        => Assert.Equal(expected, FleetTmsLogisticsEndpoints.LastMileCsvCell(input));

    [Fact]
    public void WorkspaceExposesOnlyPermissionAwareValidatedWorkflowActions()
    {
        var source = ReadSource("frontend", "src", "pages", "DispatchWorkspacePage.tsx");

        Assert.Contains("directlyHas('dispatch:create')", source, StringComparison.Ordinal);
        Assert.Contains("directlyHas('dispatch:update')", source, StringComparison.Ordinal);
        Assert.Contains("directlyHas('dispatch:assign')", source, StringComparison.Ordinal);
        Assert.Contains("logisticsApi.updateOrder", source, StringComparison.Ordinal);
        Assert.Contains("logisticsApi.updateRoute", source, StringComparison.Ordinal);
        Assert.Contains("logisticsApi.exportLastMile", source, StringComparison.Ordinal);
        Assert.Contains("Order status filter", source, StringComparison.Ordinal);
        Assert.Contains("Last-mile status filter", source, StringComparison.Ordinal);
        Assert.Contains("Actual recipient name", source, StringComparison.Ordinal);
        Assert.Contains("Next ETA", source, StringComparison.Ordinal);
        Assert.Contains("aria-pressed={active}", source, StringComparison.Ordinal);
        Assert.DoesNotContain("Date.now() + 4", source, StringComparison.Ordinal);
        Assert.DoesNotContain("Date.now() + 8", source, StringComparison.Ordinal);
        Assert.DoesNotContain("customerName.split", source, StringComparison.Ordinal);
    }

    [Fact]
    public void LogisticsSeedUsesOnlyCanonicalStage62OrderStatuses()
    {
        var source = ReadSource("backend-dotnet", "Seed", "FleetTmsSeeder.cs");
        var marker = "var statusPool = new[]";
        var start = source.IndexOf(marker, StringComparison.Ordinal);
        Assert.True(start >= 0);
        var end = source.IndexOf(';', start);
        var statusPool = source[start..end];
        Assert.DoesNotContain("Picking", statusPool, StringComparison.Ordinal);
        foreach (var status in new[] { "Queued", "Dispatched", "InTransit", "Delivered", "Exception" })
            Assert.Contains($"\"{status}\"", statusPool, StringComparison.Ordinal);
    }

    private static string ReadSource(params string[] parts)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !Directory.Exists(Path.Combine(dir.FullName, "backend-dotnet"))) dir = dir.Parent;
        Assert.NotNull(dir);
        return File.ReadAllText(Path.Combine([dir!.FullName, .. parts]));
    }
}

[Trait("Category", "Integration")]
public sealed class LogisticsSeederPostgresRegressionTests
{
    [Fact]
    public async Task RealHttpWorkspaceContractEnforcesPermissionsFiltersAndDispatchStateMachine()
    {
        var db = Db();
        await new FleetTmsLogisticsSchemaService(db, NullLogger<FleetTmsLogisticsSchemaService>.Instance).EnsureAsync();
        var suffix = Guid.NewGuid().ToString("N")[..10];
        var company = await db.InsertAsync("INSERT INTO companies(company_code,name,industry) VALUES (@code,'Logistics HTTP Regression','Transportation')",
            command => command.Parameters.AddWithValue("@code", $"LHR-{suffix}"));
        var branch = await db.InsertAsync("INSERT INTO branches(company_id,branch_code,name,status) VALUES (@companyId,'MAIN','Main','Active')",
            command => command.Parameters.AddWithValue("@companyId", company));
        var firstRoute = $"R1-{suffix}";
        var secondRoute = $"R2-{suffix}";
        var orderNumber = $"O-{suffix}";
        var orderId = 0L;
        WebApplication? app = null;
        try
        {
            foreach (var route in new[] { firstRoute, secondRoute })
                await db.ExecuteAsync("INSERT INTO fleet_tms_delivery_routes(company_id,branch_id,route_code,driver_name,vehicle_number,status,planned_stops) VALUES (@companyId,@branchId,@route,'HTTP Driver','HTTP-VAN','Planned',1)",
                    command => { command.Parameters.AddWithValue("@companyId", company); command.Parameters.AddWithValue("@branchId", branch); command.Parameters.AddWithValue("@route", route); });
            orderId = await db.InsertAsync("INSERT INTO fleet_tms_dispatch_orders(company_id,branch_id,order_number,customer_name,city,status,item_count,route_code,driver_name,vehicle_number) VALUES (@companyId,@branchId,@order,'HTTP Customer','Ottawa','Queued',1,@route,'HTTP Driver','HTTP-VAN')",
                command => { command.Parameters.AddWithValue("@companyId", company); command.Parameters.AddWithValue("@branchId", branch); command.Parameters.AddWithValue("@order", orderNumber); command.Parameters.AddWithValue("@route", firstRoute); });

            var builder = WebApplication.CreateBuilder();
            builder.WebHost.UseKestrel().UseUrls("http://127.0.0.1:0");
            builder.Services.AddSingleton(db);
            app = builder.Build();
            app.Use(async (context, next) =>
            {
                context.Items[EndpointMappings.AuthCompanyIdItemKey] = company;
                context.Items[EndpointMappings.AuthBranchIdItemKey] = branch;
                context.Items[EndpointMappings.AuthUserIdItemKey] = 991100L;
                context.Items[EndpointMappings.AuthRoleItemKey] = "Logistics HTTP Tester";
                context.Items[EndpointMappings.AuthPermissionsItemKey] = context.Request.Headers["X-Test-Permissions"].ToString()
                    .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
                await next();
            });
            app.MapFleetTmsLogisticsEndpoints();
            await app.StartAsync();
            var address = app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.Single();
            using var client = new HttpClient { BaseAddress = new Uri(address) };

            Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync("/api/fleet-tms/logistics/orders")).StatusCode);
            using (var invalidFilter = new HttpRequestMessage(HttpMethod.Get, "/api/fleet-tms/logistics/orders?status=MadeUp"))
            {
                invalidFilter.Headers.Add("X-Test-Permissions", "dispatch:view");
                Assert.Equal(HttpStatusCode.BadRequest, (await client.SendAsync(invalidFilter)).StatusCode);
            }

            async Task<HttpStatusCode> Dispatch(string route)
            {
                using var request = new HttpRequestMessage(HttpMethod.Post, $"/api/fleet-tms/logistics/orders/{orderId}/dispatch");
                request.Headers.Add("X-Test-Permissions", "dispatch:assign");
                request.Content = JsonContent.Create(new DispatchOrderRequest(route, "HTTP Driver", "HTTP-VAN", "HTTP dispatch"));
                return (await client.SendAsync(request)).StatusCode;
            }

            Assert.Equal(HttpStatusCode.OK, await Dispatch(firstRoute));
            Assert.Equal(HttpStatusCode.OK, await Dispatch(firstRoute));

            foreach (var search in new[] { firstRoute, "Ottawa" })
            {
                using var request = new HttpRequestMessage(HttpMethod.Get,
                    $"/api/fleet-tms/logistics/last-mile?search={Uri.EscapeDataString(search)}");
                request.Headers.Add("X-Test-Permissions", "dispatch:view");
                using var response = await client.SendAsync(request);
                Assert.Equal(HttpStatusCode.OK, response.StatusCode);
                Assert.Contains(orderNumber, await response.Content.ReadAsStringAsync(), StringComparison.Ordinal);
            }

            await db.ExecuteAsync("UPDATE fleet_tms_last_mile_stops SET customer_name='=1+1' WHERE company_id=@companyId AND order_number=@order",
                command => { command.Parameters.AddWithValue("@companyId", company); command.Parameters.AddWithValue("@order", orderNumber); });
            using (var request = new HttpRequestMessage(HttpMethod.Get,
                $"/api/fleet-tms/logistics/last-mile/export?routeCode={Uri.EscapeDataString(firstRoute)}"))
            {
                request.Headers.Add("X-Test-Permissions", "dispatch:view");
                using var response = await client.SendAsync(request);
                Assert.Equal(HttpStatusCode.OK, response.StatusCode);
                Assert.Contains("\"'=1+1\"", await response.Content.ReadAsStringAsync(), StringComparison.Ordinal);
            }

            Assert.Equal(HttpStatusCode.Conflict, await Dispatch(secondRoute));
            Assert.Equal(1, await db.ScalarLongAsync("SELECT COUNT(*) FROM fleet_tms_last_mile_stops WHERE company_id=@companyId AND order_number=@order",
                command => { command.Parameters.AddWithValue("@companyId", company); command.Parameters.AddWithValue("@order", orderNumber); }));
            await db.ExecuteAsync("UPDATE fleet_tms_dispatch_orders SET status='Exception' WHERE id=@id", command => command.Parameters.AddWithValue("@id", orderId));
            Assert.Equal(HttpStatusCode.Conflict, await Dispatch(firstRoute));
        }
        finally
        {
            if (app is not null) { await app.StopAsync(); await app.DisposeAsync(); }
            foreach (var table in new[] { "fleet_tms_last_mile_stops", "fleet_tms_delivery_routes", "fleet_tms_dispatch_orders" })
                await db.ExecuteAsync($"DELETE FROM {table} WHERE company_id=@companyId", command => command.Parameters.AddWithValue("@companyId", company));
            await db.ExecuteAsync("DELETE FROM branches WHERE company_id=@companyId", command => command.Parameters.AddWithValue("@companyId", company));
            await db.ExecuteAsync("DELETE FROM companies WHERE id=@companyId", command => command.Parameters.AddWithValue("@companyId", company));
        }
    }

    [Fact]
    public async Task LastMileDemoSeedSatisfiesCanonicalDatabaseStatusChecks()
    {
        var db = new Database(new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["ConnectionStrings:DefaultConnection"] = TestDb.ConnectionString,
            ["Rls:EnforceTenantContext"] = "false",
        }).Build());
        await new FleetTmsLogisticsSchemaService(db, NullLogger<FleetTmsLogisticsSchemaService>.Instance).EnsureAsync();
        var suffix = Guid.NewGuid().ToString("N")[..10];
        var company = await db.InsertAsync("INSERT INTO companies(company_code,name,industry) VALUES (@code,'Logistics Seeder Regression','Transportation')",
            command => command.Parameters.AddWithValue("@code", $"LSR-{suffix}"));
        try
        {
            var seeder = new FleetTmsSeeder(db, NullLogger<FleetTmsSeeder>.Instance,
                new ConfigurationBuilder().Build(), new TestHostEnvironment());
            var method = typeof(FleetTmsSeeder).GetMethod("SeedLogistics", BindingFlags.Instance | BindingFlags.NonPublic)!;
            await (Task)method.Invoke(seeder, [company, CancellationToken.None])!;

            var rows = await db.QueryAsync("SELECT status FROM fleet_tms_dispatch_orders WHERE company_id=@companyId",
                command => command.Parameters.AddWithValue("@companyId", company));
            Assert.Equal(8, rows.Count);
            var canonical = new HashSet<string>(["Queued", "Dispatched", "InTransit", "Exception", "Delivered", "Returned"], StringComparer.Ordinal);
            Assert.All(rows, row => Assert.Contains(Assert.IsType<string>(row["status"]), canonical));
        }
        finally
        {
            foreach (var table in new[] { "fleet_tms_last_mile_stops", "fleet_tms_delivery_routes", "fleet_tms_dispatch_orders" })
                await db.ExecuteAsync($"DELETE FROM {table} WHERE company_id=@companyId", command => command.Parameters.AddWithValue("@companyId", company));
            await db.ExecuteAsync("DELETE FROM companies WHERE id=@companyId", command => command.Parameters.AddWithValue("@companyId", company));
        }
    }

    [Fact]
    public async Task WorkspaceListUpdateAndStatusBypassPathsArePermissionAndBranchSafe()
    {
        var db = Db();
        await new FleetTmsLogisticsSchemaService(db, NullLogger<FleetTmsLogisticsSchemaService>.Instance).EnsureAsync();
        var suffix = Guid.NewGuid().ToString("N")[..10];
        var company = await db.InsertAsync("INSERT INTO companies(company_code,name,industry) VALUES (@code,'Logistics Workspace Paths','Transportation')",
            command => command.Parameters.AddWithValue("@code", $"LWP-{suffix}"));
        var branch = await db.InsertAsync("INSERT INTO branches(company_id,branch_code,name,status) VALUES (@companyId,'MAIN','Main','Active')",
            command => command.Parameters.AddWithValue("@companyId", company));
        var otherBranch = await db.InsertAsync("INSERT INTO branches(company_id,branch_code,name,status) VALUES (@companyId,'OTHER','Other','Active')",
            command => command.Parameters.AddWithValue("@companyId", company));
        try
        {
            var routeCode = $"R-{suffix}";
            var routeId = await db.InsertAsync("INSERT INTO fleet_tms_delivery_routes(company_id,branch_id,route_code,status,planned_stops,completed_stops) VALUES (@companyId,@branchId,@routeCode,'Planned',2,0)",
                command => { command.Parameters.AddWithValue("@companyId", company); command.Parameters.AddWithValue("@branchId", branch); command.Parameters.AddWithValue("@routeCode", routeCode); });
            var orderId = await db.InsertAsync("INSERT INTO fleet_tms_dispatch_orders(company_id,branch_id,order_number,customer_name,status,item_count,order_value,route_code) VALUES (@companyId,@branchId,@orderNumber,'Original Customer','Queued',1,10,@routeCode)",
                command => { command.Parameters.AddWithValue("@companyId", company); command.Parameters.AddWithValue("@branchId", branch); command.Parameters.AddWithValue("@orderNumber", $"O-{suffix}"); command.Parameters.AddWithValue("@routeCode", routeCode); });
            await db.ExecuteAsync("INSERT INTO fleet_tms_dispatch_orders(company_id,branch_id,order_number,customer_name,status,item_count) VALUES (@companyId,@branchId,@orderNumber,'Second Customer','Queued',1)",
                command => { command.Parameters.AddWithValue("@companyId", company); command.Parameters.AddWithValue("@branchId", branch); command.Parameters.AddWithValue("@orderNumber", $"O2-{suffix}"); });

            var viewer = Principal(company, branch, "dispatch:view");
            var page = await Invoke("Orders", viewer, db, CancellationToken.None, "Queued", 1, 1);
            Assert.Equal(StatusCodes.Status200OK, Status(page));
            var json = JsonSerializer.Serialize(Assert.IsAssignableFrom<IValueHttpResult>(page).Value, new JsonSerializerOptions(JsonSerializerDefaults.Web));
            Assert.Contains("\"total\":2", json, StringComparison.Ordinal);
            Assert.Contains("\"pageSize\":1", json, StringComparison.Ordinal);
            Assert.Equal(StatusCodes.Status400BadRequest, Status(await Invoke("Orders", viewer, db, CancellationToken.None, "MadeUp", 1, 1)));

            var orderUpdate = new LogisticsOrderRequest(null, "Updated Customer", null, null, "Ottawa", "North", null, "High", 5, 99, routeCode, "Driver One", "VAN-1", "Customer confirmed", DateTime.UtcNow.AddHours(4));
            Assert.Equal(StatusCodes.Status403Forbidden, Status(await Invoke("UpdateOrder", viewer, orderId, orderUpdate, db, CancellationToken.None)));
            Assert.Equal(StatusCodes.Status404NotFound, Status(await Invoke("UpdateOrder", Principal(company, otherBranch, "dispatch:update"), orderId, orderUpdate, db, CancellationToken.None)));
            Assert.Equal(StatusCodes.Status200OK, Status(await Invoke("UpdateOrder", Principal(company, branch, "dispatch:update"), orderId, orderUpdate, db, CancellationToken.None)));
            Assert.Equal("Updated Customer", (await db.QuerySingleAsync("SELECT customer_name FROM fleet_tms_dispatch_orders WHERE id=@id", command => command.Parameters.AddWithValue("@id", orderId)))!["customerName"]);
            await db.ExecuteAsync("UPDATE fleet_tms_dispatch_orders SET status='Delivered' WHERE id=@id", command => command.Parameters.AddWithValue("@id", orderId));
            Assert.Equal(StatusCodes.Status409Conflict, Status(await Invoke("UpdateOrder", Principal(company, branch, "dispatch:update"), orderId, orderUpdate, db, CancellationToken.None)));

            var routeBypass = new LogisticsRouteRequest(null, "Hub", "Territory", "Driver", "VAN-1", "Active", 2, 0, 20, 0, null, null, DateTime.UtcNow.Date, DateTime.UtcNow, null, "Bypass attempt");
            Assert.Equal(StatusCodes.Status400BadRequest, Status(await Invoke("UpdateRoute", Principal(company, branch, "dispatch:update"), routeId, routeBypass, db, CancellationToken.None)));
        }
        finally
        {
            foreach (var table in new[] { "fleet_tms_last_mile_stops", "fleet_tms_delivery_routes", "fleet_tms_dispatch_orders" })
                await db.ExecuteAsync($"DELETE FROM {table} WHERE company_id=@companyId", command => command.Parameters.AddWithValue("@companyId", company));
            await db.ExecuteAsync("DELETE FROM branches WHERE company_id=@companyId", command => command.Parameters.AddWithValue("@companyId", company));
            await db.ExecuteAsync("DELETE FROM companies WHERE id=@companyId", command => command.Parameters.AddWithValue("@companyId", company));
        }
    }

    private static async Task<IResult> Invoke(string name, params object[] parameters)
    {
        var method = typeof(FleetTmsLogisticsEndpoints).GetMethod(name, BindingFlags.NonPublic | BindingFlags.Static)!;
        try { return await (Task<IResult>)method.Invoke(null, parameters)!; }
        catch (TargetInvocationException ex) when (ex.InnerException is not null)
        { System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(ex.InnerException).Throw(); throw; }
    }

    private static int? Status(IResult result) => Assert.IsAssignableFrom<IStatusCodeHttpResult>(result).StatusCode;

    private static DefaultHttpContext Principal(long company, long branch, params string[] permissions)
    {
        var http = new DefaultHttpContext();
        http.Items[EndpointMappings.AuthCompanyIdItemKey] = company;
        http.Items[EndpointMappings.AuthBranchIdItemKey] = branch;
        http.Items[EndpointMappings.AuthUserIdItemKey] = 990100L;
        http.Items[EndpointMappings.AuthRoleItemKey] = "Logistics Workspace Tester";
        http.Items[EndpointMappings.AuthPermissionsItemKey] = permissions;
        return http;
    }

    private static Database Db() => new(new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
    {
        ["ConnectionStrings:DefaultConnection"] = TestDb.ConnectionString,
        ["Rls:EnforceTenantContext"] = "false",
    }).Build());

    private sealed class TestHostEnvironment : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = Environments.Development;
        public string ApplicationName { get; set; } = "Opstrax.Tests";
        public string ContentRootPath { get; set; } = Directory.GetCurrentDirectory();
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }
}
