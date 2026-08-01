using System.Reflection;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Opstrax.Api.Controllers;
using Opstrax.Api.Data;
using Opstrax.Api.Services;

namespace Opstrax.Tests;

[Trait("Category", "Integration")]
public sealed class LastMileWorkflowPostgresTests
{
    [Fact]
    public void DispatchWorkspaceUsesAnAcceptedDeliveryProofState()
    {
        var source = ReadSource("frontend", "src", "pages", "DispatchWorkspacePage.tsx");
        Assert.Contains("proofStatus: 'Captured'", source, StringComparison.Ordinal);
        Assert.DoesNotContain("proofStatus: 'POD'", source, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AttemptRescheduleDeliver_AreBranchSafeLegalAtomicAndIdempotent()
    {
        var db = Db();
        await new FleetTmsLogisticsSchemaService(db, NullLogger<FleetTmsLogisticsSchemaService>.Instance).EnsureAsync();
        var seed = await SeedAsync(db);
        try
        {
            var wrongBranch = Principal(seed.Company, seed.OtherBranch, "fleet.pod.manage");
            Assert.Equal(StatusCodes.Status404NotFound, Status(await Invoke("ConfirmDelivery", wrongBranch, seed.Stop,
                new ConfirmDeliveryRequest("Receiver", "Captured", null, "wrong-branch"), Db())));

            var operatorHttp = Principal(seed.Company, seed.Branch, "dispatch:update");
            Assert.Equal(StatusCodes.Status400BadRequest, Status(await Invoke("RecordAttempt", operatorHttp, seed.Stop,
                new StopAttemptRequest("MadeUp", null, "No answer", null, null, "invalid-status"), db)));
            Assert.Equal(StatusCodes.Status400BadRequest, Status(await Invoke("RecordAttempt", operatorHttp, seed.Stop,
                new StopAttemptRequest("Attempted", null, "No answer", DateTime.UtcNow.AddMinutes(-1), null, "past-eta"), db)));
            Assert.Equal(StatusCodes.Status400BadRequest, Status(await Invoke("RecordAttempt", operatorHttp, seed.Stop,
                new StopAttemptRequest("Attempted", "Fabricated", "No answer", null, null, "invalid-proof"), db)));

            var attempt = new StopAttemptRequest("Attempted", null, "No answer at door", DateTime.UtcNow.AddHours(2), "Depot", "attempt-one");
            Assert.Equal(StatusCodes.Status200OK, Status(await Invoke("RecordAttempt", operatorHttp, seed.Stop, attempt, db)));
            Assert.Equal(StatusCodes.Status200OK, Status(await Invoke("RecordAttempt", operatorHttp, seed.Stop, attempt, db)));
            Assert.Equal(StatusCodes.Status409Conflict, Status(await Invoke("RecordAttempt", operatorHttp, seed.Stop,
                attempt with { ExceptionReason = "Different payload" }, db)));
            Assert.Equal(1, await db.ScalarLongAsync("SELECT attempt_count FROM fleet_tms_last_mile_stops WHERE id=@id", c => c.Parameters.AddWithValue("@id", seed.Stop)));

            var reschedule = new StopRescheduleRequest(DateTime.UtcNow.AddHours(4), "14:00-16:00", "Customer requested later window", "reschedule-one");
            Assert.Equal(StatusCodes.Status200OK, Status(await Invoke("RescheduleStop", operatorHttp, seed.Stop, reschedule, db)));
            Assert.Equal("Delayed", (await db.QuerySingleAsync("SELECT status FROM fleet_tms_delivery_routes WHERE id=@id", c => c.Parameters.AddWithValue("@id", seed.Route)))!["status"]);

            // A mobile retry can arrive after a later successful action. Idempotency must
            // remain durable across the whole workflow, not only for the immediately prior key.
            Assert.Equal(StatusCodes.Status200OK, Status(await Invoke("RecordAttempt", operatorHttp, seed.Stop, attempt, db)));
            var replayState = (await db.QuerySingleAsync("SELECT status,attempt_count FROM fleet_tms_last_mile_stops WHERE id=@id",
                c => c.Parameters.AddWithValue("@id", seed.Stop)))!;
            Assert.Equal("Rescheduled", replayState["status"]);
            Assert.Equal(1, Convert.ToInt32(replayState["attemptCount"]));

            var podHttp = Principal(seed.Company, seed.Branch, "fleet.pod.manage");
            podHttp.Items.Remove(EndpointMappings.AuthBranchIdItemKey);
            var delivery = new ConfirmDeliveryRequest("Receiver", "Captured", null, "deliver-one");
            var results = await Task.WhenAll(
                Invoke("ConfirmDelivery", podHttp, seed.Stop, delivery, Db()),
                Invoke("ConfirmDelivery", TenantWidePrincipal(seed.Company, "fleet.pod.manage"), seed.Stop, delivery, Db()));
            Assert.All(results, result => Assert.Equal(StatusCodes.Status200OK, Status(result)));

            var state = (await db.QuerySingleAsync(
                @"SELECT s.status,s.proof_status,s.attempt_count,o.status order_status,r.completed_stops,r.completion_percent
                  FROM fleet_tms_last_mile_stops s
                  JOIN fleet_tms_dispatch_orders o ON o.company_id=s.company_id AND o.order_number=s.order_number
                  JOIN fleet_tms_delivery_routes r ON r.company_id=s.company_id AND r.route_code=s.route_code
                  WHERE s.id=@id", c => c.Parameters.AddWithValue("@id", seed.Stop)))!;
            Assert.Equal("Delivered", state["status"]);
            Assert.Equal("Captured", state["proofStatus"]);
            Assert.Equal("Delivered", state["orderStatus"]);
            Assert.Equal(1, Convert.ToInt32(state["completedStops"]));
            Assert.Equal(100m, Convert.ToDecimal(state["completionPercent"]));

            var job = (await db.QuerySingleAsync("SELECT id,status,branch_id FROM jobs WHERE company_id=@c AND job_code='FTMS-'||@n",
                c => { c.Parameters.AddWithValue("@c", seed.Company); c.Parameters.AddWithValue("@n", seed.OrderNumber); }))!;
            Assert.Equal("Delivered", job["status"]);
            Assert.Equal(seed.Branch, Convert.ToInt64(job["branchId"]));
            var jobId = Convert.ToInt64(job["id"]);
            Assert.Equal(1, await Count(db, "dispatch_assignments", seed.Company, jobId, ""));
            Assert.Equal(seed.Branch, await db.ScalarLongAsync("SELECT branch_id FROM dispatch_assignments WHERE company_id=@c AND job_id=@j",
                c => { c.Parameters.AddWithValue("@c", seed.Company); c.Parameters.AddWithValue("@j", jobId); }));
            Assert.Equal(1, await Count(db, "job_charges", seed.Company, jobId, " AND charge_code='LASTMILE'"));

            Assert.Equal(StatusCodes.Status409Conflict, Status(await Invoke("RecordAttempt", operatorHttp, seed.Stop,
                new StopAttemptRequest("Failed", null, "Late rollback", null, null, "after-delivery"), db)));
            Assert.Equal(StatusCodes.Status409Conflict, Status(await Invoke("RescheduleStop", operatorHttp, seed.Stop,
                new StopRescheduleRequest(DateTime.UtcNow.AddDays(1), null, "Late rollback", "after-delivery-2"), db)));
        }
        finally { await Cleanup(db, seed.Company); }
    }

    [Fact]
    public async Task RouteProgressReplayRemainsIdempotentAfterLaterProgress()
    {
        var db = Db();
        await new FleetTmsLogisticsSchemaService(db, NullLogger<FleetTmsLogisticsSchemaService>.Instance).EnsureAsync();
        var seed = await SeedAsync(db);
        try
        {
            await db.ExecuteAsync("UPDATE fleet_tms_delivery_routes SET planned_stops=4 WHERE id=@id", c => c.Parameters.AddWithValue("@id", seed.Route));
            var http = Principal(seed.Company, seed.Branch, "dispatch:update");
            var first = new RouteProgressRequest(1, "One", "Two", null, null, "progress-a");
            var second = new RouteProgressRequest(1, "Two", "Three", null, null, "progress-b");
            Assert.Equal(StatusCodes.Status200OK, Status(await Invoke("ProgressRoute", http, seed.Route, first, db)));
            Assert.Equal(StatusCodes.Status200OK, Status(await Invoke("ProgressRoute", http, seed.Route, second, db)));
            Assert.Equal(StatusCodes.Status200OK, Status(await Invoke("ProgressRoute", http, seed.Route, first, db)));
            Assert.Equal(2, await db.ScalarLongAsync("SELECT completed_stops FROM fleet_tms_delivery_routes WHERE id=@id", c => c.Parameters.AddWithValue("@id", seed.Route)));
            Assert.Equal(StatusCodes.Status409Conflict, Status(await Invoke("ProgressRoute", http, seed.Route,
                first with { CompletedStopsDelta = 2 }, db)));
        }
        finally { await Cleanup(db, seed.Company); }
    }

    [Fact]
    public async Task TenantWideDeliveryRejectsCrossBranchRouteLinkAndRollsBack()
    {
        var db = Db();
        await new FleetTmsLogisticsSchemaService(db, NullLogger<FleetTmsLogisticsSchemaService>.Instance).EnsureAsync();
        var seed = await SeedAsync(db);
        try
        {
            await db.ExecuteAsync("UPDATE fleet_tms_delivery_routes SET branch_id=@b WHERE id=@id",
                c => { c.Parameters.AddWithValue("@b", seed.OtherBranch); c.Parameters.AddWithValue("@id", seed.Route); });
            var tenantWide = Principal(seed.Company, seed.Branch, "fleet.pod.manage");
            tenantWide.Items.Remove(EndpointMappings.AuthBranchIdItemKey);
            var result = await Invoke("ConfirmDelivery", tenantWide, seed.Stop,
                new ConfirmDeliveryRequest("Receiver", "Captured", null, "cross-branch-link"), db);
            Assert.Equal(StatusCodes.Status409Conflict, Status(result));
            Assert.Equal("OutForDelivery", (await db.QuerySingleAsync("SELECT status FROM fleet_tms_last_mile_stops WHERE id=@id", c => c.Parameters.AddWithValue("@id", seed.Stop)))!["status"]);
            Assert.Equal("InTransit", (await db.QuerySingleAsync("SELECT status FROM fleet_tms_dispatch_orders WHERE company_id=@c AND order_number=@o", c => { c.Parameters.AddWithValue("@c", seed.Company); c.Parameters.AddWithValue("@o", seed.OrderNumber); }))!["status"]);
        }
        finally { await Cleanup(db, seed.Company); }
    }

    [Fact]
    public async Task DeliveryRejectsZeroStopRouteWithoutPartialWrites()
    {
        var db = Db();
        await new FleetTmsLogisticsSchemaService(db, NullLogger<FleetTmsLogisticsSchemaService>.Instance).EnsureAsync();
        var seed = await SeedAsync(db);
        try
        {
            await db.ExecuteAsync("UPDATE fleet_tms_delivery_routes SET planned_stops=0 WHERE id=@id", c => c.Parameters.AddWithValue("@id", seed.Route));
            var result = await Invoke("ConfirmDelivery", Principal(seed.Company, seed.Branch, "fleet.pod.manage"), seed.Stop,
                new ConfirmDeliveryRequest("Receiver", "Captured", null, "zero-plan"), db);
            Assert.Equal(StatusCodes.Status409Conflict, Status(result));
            Assert.Equal("OutForDelivery", (await db.QuerySingleAsync("SELECT status FROM fleet_tms_last_mile_stops WHERE id=@id", c => c.Parameters.AddWithValue("@id", seed.Stop)))!["status"]);
        }
        finally { await Cleanup(db, seed.Company); }
    }

    [Fact]
    public async Task CreateDispatchMaterializesStopInOrderBranchAndRejectsDuplicateKeys()
    {
        var db = Db();
        await new FleetTmsLogisticsSchemaService(db, NullLogger<FleetTmsLogisticsSchemaService>.Instance).EnsureAsync();
        var suffix = Guid.NewGuid().ToString("N")[..10];
        var company = await db.InsertAsync("INSERT INTO companies(company_code,name,industry) VALUES (@x,'Last Mile Dispatch Pilot','Transportation')", c => c.Parameters.AddWithValue("@x", $"LMD-{suffix}"));
        var branch = await db.InsertAsync("INSERT INTO branches(company_id,branch_code,name,status) VALUES (@c,'MAIN','Main','Active')", c => c.Parameters.AddWithValue("@c", company));
        try
        {
            var createHttp = Principal(company, branch, "dispatch:create");
            var routeCode = $"R-{suffix}";
            var alternateRouteCode = $"R2-{suffix}";
            var orderNumber = $"O-{suffix}";
            var routeRequest = new LogisticsRouteRequest(routeCode, "Hub", "Territory", "Pilot Driver", "VAN-1", "Planned", 1, 0, 10, 0, null, null, DateTime.UtcNow.Date, DateTime.UtcNow, null, null);
            Assert.Equal(StatusCodes.Status200OK, Status(await InvokeCreate("CreateRoute", createHttp, routeRequest, db)));
            Assert.Equal(StatusCodes.Status409Conflict, Status(await InvokeCreate("CreateRoute", createHttp, routeRequest, db)));
            Assert.Equal(StatusCodes.Status200OK, Status(await InvokeCreate("CreateRoute", createHttp,
                routeRequest with { RouteCode = alternateRouteCode, DriverName = "Alternate Driver", VehicleNumber = "VAN-2" }, db)));

            var orderRequest = new LogisticsOrderRequest(orderNumber, "Pilot Customer", "Retail", "Portal", "City", "Area", "Queued", "Normal", 1, 50, routeCode, null, null, null, DateTime.UtcNow.AddHours(3));
            Assert.Equal(StatusCodes.Status200OK, Status(await InvokeCreate("CreateOrder", createHttp, orderRequest, db)));
            Assert.Equal(StatusCodes.Status409Conflict, Status(await InvokeCreate("CreateOrder", createHttp, orderRequest, db)));
            var orderId = await db.ScalarLongAsync("SELECT id FROM fleet_tms_dispatch_orders WHERE company_id=@c AND order_number=@o", c => { c.Parameters.AddWithValue("@c", company); c.Parameters.AddWithValue("@o", orderNumber); });

            Assert.Equal(StatusCodes.Status200OK, Status(await Invoke("DispatchOrder", Principal(company, branch, "dispatch:assign"), orderId,
                new DispatchOrderRequest(routeCode, null, null, "Pilot dispatch"), db)));
            var stop = (await db.QuerySingleAsync("SELECT branch_id,route_code,status,rider_name FROM fleet_tms_last_mile_stops WHERE company_id=@c AND order_number=@o",
                c => { c.Parameters.AddWithValue("@c", company); c.Parameters.AddWithValue("@o", orderNumber); }))!;
            Assert.Equal(branch, Convert.ToInt64(stop["branchId"]));
            Assert.Equal(routeCode, stop["routeCode"]);
            Assert.Equal("OutForDelivery", stop["status"]);
            Assert.Equal("Pilot Driver", stop["riderName"]);

            // A transport retry of the exact assignment is harmless, but an already-dispatched
            // order cannot be silently moved to another route and an exception is not dispatchable.
            var dispatch = new DispatchOrderRequest(routeCode, null, null, "Pilot dispatch");
            Assert.Equal(StatusCodes.Status200OK, Status(await Invoke("DispatchOrder", Principal(company, branch, "dispatch:assign"), orderId, dispatch, db)));
            Assert.Equal(1, await db.ScalarLongAsync(
                "SELECT COUNT(*) FROM fleet_tms_last_mile_stops WHERE company_id=@c AND order_number=@o",
                c => { c.Parameters.AddWithValue("@c", company); c.Parameters.AddWithValue("@o", orderNumber); }));
            Assert.Equal(StatusCodes.Status409Conflict, Status(await Invoke("DispatchOrder", Principal(company, branch, "dispatch:assign"), orderId,
                new DispatchOrderRequest(alternateRouteCode, "Alternate Driver", "VAN-2", "Unauthorized reroute"), db)));
            Assert.Equal(routeCode, (await db.QuerySingleAsync(
                "SELECT route_code FROM fleet_tms_last_mile_stops WHERE company_id=@c AND order_number=@o",
                c => { c.Parameters.AddWithValue("@c", company); c.Parameters.AddWithValue("@o", orderNumber); }))!["routeCode"]);

            await db.ExecuteAsync("UPDATE fleet_tms_dispatch_orders SET status='Exception' WHERE id=@id", c => c.Parameters.AddWithValue("@id", orderId));
            Assert.Equal(StatusCodes.Status409Conflict, Status(await Invoke("DispatchOrder", Principal(company, branch, "dispatch:assign"), orderId, dispatch, db)));
            Assert.Equal("Exception", (await db.QuerySingleAsync("SELECT status FROM fleet_tms_dispatch_orders WHERE id=@id",
                c => c.Parameters.AddWithValue("@id", orderId)))!["status"]);
        }
        finally { await Cleanup(db, company); }
    }

    private static async Task<IResult> Invoke(string name, HttpContext http, long id, object request, Database db)
    {
        var method = typeof(FleetTmsLogisticsEndpoints).GetMethod(name, BindingFlags.NonPublic | BindingFlags.Static)!;
        try { return await (Task<IResult>)method.Invoke(null, new[] { http, (object)id, request, db, CancellationToken.None })!; }
        catch (TargetInvocationException ex) when (ex.InnerException is not null)
        { System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(ex.InnerException).Throw(); throw; }
    }

    private static async Task<IResult> InvokeCreate(string name, HttpContext http, object request, Database db)
    {
        var method = typeof(FleetTmsLogisticsEndpoints).GetMethod(name, BindingFlags.NonPublic | BindingFlags.Static)!;
        try { return await (Task<IResult>)method.Invoke(null, new[] { http, request, db, CancellationToken.None })!; }
        catch (TargetInvocationException ex) when (ex.InnerException is not null)
        { System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(ex.InnerException).Throw(); throw; }
    }

    private static int? Status(IResult result) => Assert.IsAssignableFrom<IStatusCodeHttpResult>(result).StatusCode;
    private static Database Db() => new(new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
    { ["ConnectionStrings:DefaultConnection"] = TestDb.ConnectionString, ["Rls:EnforceTenantContext"] = "false" }).Build());

    private static DefaultHttpContext Principal(long company, long branch, params string[] permissions)
    {
        var http = new DefaultHttpContext();
        http.Items[EndpointMappings.AuthCompanyIdItemKey] = company;
        http.Items[EndpointMappings.AuthBranchIdItemKey] = branch;
        http.Items[EndpointMappings.AuthUserIdItemKey] = 42L;
        http.Items[EndpointMappings.AuthRoleItemKey] = "Last-mile pilot tester";
        http.Items[EndpointMappings.AuthPermissionsItemKey] = permissions;
        return http;
    }

    private static DefaultHttpContext TenantWidePrincipal(long company, params string[] permissions)
    {
        var http = Principal(company, 1, permissions);
        http.Items.Remove(EndpointMappings.AuthBranchIdItemKey);
        return http;
    }

    private static async Task<SeedData> SeedAsync(Database db)
    {
        var suffix = Guid.NewGuid().ToString("N")[..10];
        var company = await db.InsertAsync("INSERT INTO companies(company_code,name,industry) VALUES (@x,'Last Mile Pilot','Transportation')", c => c.Parameters.AddWithValue("@x", $"LMW-{suffix}"));
        var branch = await db.InsertAsync("INSERT INTO branches(company_id,branch_code,name,status) VALUES (@c,'MAIN','Main','Active')", c => c.Parameters.AddWithValue("@c", company));
        var other = await db.InsertAsync("INSERT INTO branches(company_id,branch_code,name,status) VALUES (@c,'OTHER','Other','Active')", c => c.Parameters.AddWithValue("@c", company));
        var order = $"LM-{suffix}";
        var routeCode = $"R-{suffix}";
        await db.ExecuteAsync("INSERT INTO fleet_tms_dispatch_orders(company_id,branch_id,order_number,customer_name,status,order_value,route_code) VALUES (@c,@b,@o,'Pilot Customer','InTransit',125,@r)", c => { c.Parameters.AddWithValue("@c", company); c.Parameters.AddWithValue("@b", branch); c.Parameters.AddWithValue("@o", order); c.Parameters.AddWithValue("@r", routeCode); });
        var route = await db.InsertAsync("INSERT INTO fleet_tms_delivery_routes(company_id,branch_id,route_code,status,planned_stops,completed_stops) VALUES (@c,@b,@r,'Active',1,0)", c => { c.Parameters.AddWithValue("@c", company); c.Parameters.AddWithValue("@b", branch); c.Parameters.AddWithValue("@r", routeCode); });
        var stop = await db.InsertAsync("INSERT INTO fleet_tms_last_mile_stops(company_id,branch_id,order_number,route_code,customer_name,status) VALUES (@c,@b,@o,@r,'Pilot Customer','OutForDelivery')", c => { c.Parameters.AddWithValue("@c", company); c.Parameters.AddWithValue("@b", branch); c.Parameters.AddWithValue("@o", order); c.Parameters.AddWithValue("@r", routeCode); });
        return new(company, branch, other, route, stop, order);
    }

    private static Task<long> Count(Database db, string table, long company, long job, string suffix) => db.ScalarLongAsync(
        $"SELECT COUNT(*) FROM {table} WHERE company_id=@c AND job_id=@j{suffix}", c => { c.Parameters.AddWithValue("@c", company); c.Parameters.AddWithValue("@j", job); });

    private static async Task Cleanup(Database db, long company)
    {
        foreach (var sql in new[] { "DELETE FROM idempotency_keys WHERE tenant_id=@c", "DELETE FROM job_charges WHERE company_id=@c", "DELETE FROM dispatch_assignments WHERE company_id=@c", "DELETE FROM jobs WHERE company_id=@c", "DELETE FROM customers WHERE company_id=@c", "DELETE FROM fleet_tms_last_mile_stops WHERE company_id=@c", "DELETE FROM fleet_tms_delivery_routes WHERE company_id=@c", "DELETE FROM fleet_tms_dispatch_orders WHERE company_id=@c", "DELETE FROM branches WHERE company_id=@c", "DELETE FROM companies WHERE id=@c" })
            await db.ExecuteAsync(sql, c => c.Parameters.AddWithValue("@c", company));
    }

    private sealed record SeedData(long Company, long Branch, long OtherBranch, long Route, long Stop, string OrderNumber);

    private static string ReadSource(params string[] parts)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !Directory.Exists(Path.Combine(dir.FullName, "backend-dotnet"))) dir = dir.Parent;
        Assert.NotNull(dir);
        return File.ReadAllText(Path.Combine([dir!.FullName, .. parts]));
    }
}
