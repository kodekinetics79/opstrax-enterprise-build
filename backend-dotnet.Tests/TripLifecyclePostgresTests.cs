using System.Reflection;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Opstrax.Api.Controllers;
using Opstrax.Api.Data;
using Opstrax.Api.Services;

namespace Opstrax.Tests;

[Trait("Category", "Integration")]
public sealed class TripLifecyclePostgresTests
{
    [Fact]
    public async Task Lifecycle_ExceptionResumeCompleteAndCancel_PersistsAtomicStateAndAudit()
    {
        var db = Db();
        await new Batch2SchemaService(db).EnsureAsync();
        await new TripSchemaService(db).EnsureAsync();
        await new DispatchSchemaService(db, NullLogger<DispatchSchemaService>.Instance).EnsureAsync();
        var seed = await Seed(db);
        try
        {
            var http = Principal(seed.CompanyId, seed.BranchId, "dispatch:view", "dispatch:update", "dispatch:cancel");
            var audit = new AuditService(db);
            var route = await Route(db, seed.CompanyId, seed.VehicleId, seed.DriverId, "Active");
            var trip = await Trip(db, seed.CompanyId, seed.VehicleId, seed.DriverId, route);

            AssertStatus(await Invoke("TripStart", trip, http, db, audit, CancellationToken.None), StatusCodes.Status200OK);
            AssertStatus(await InvokeWithBody("TripException", trip, http, "x", db, audit), StatusCodes.Status400BadRequest);
            AssertStatus(await InvokeWithBody("TripException", trip, http, "Customer dock unexpectedly closed", db, audit), StatusCodes.Status200OK);
            AssertStatus(await Invoke("TripComplete", trip, http, db, audit, CancellationToken.None), StatusCodes.Status409Conflict);
            AssertStatus(await Invoke("TripStart", trip, http, db, audit, CancellationToken.None), StatusCodes.Status200OK);
            AssertStatus(await Invoke("TripComplete", trip, http, db, audit, CancellationToken.None), StatusCodes.Status200OK);
            AssertStatus(await InvokeWithBody("TripCancel", trip, http, "Already completed", db, audit), StatusCodes.Status409Conflict);

            var finished = (await db.QuerySingleAsync(
                "SELECT status,actual_start_time,started_at,actual_end_time,completed_at,actual_duration_minutes,compliance_score,compliance_breakdown_json FROM trips WHERE id=@id",
                c => c.Parameters.AddWithValue("@id", trip)))!;
            Assert.Equal("completed", finished["status"]);
            Assert.NotNull(finished["actualStartTime"]);
            Assert.NotNull(finished["startedAt"]);
            Assert.NotNull(finished["actualEndTime"]);
            Assert.NotNull(finished["completedAt"]);
            Assert.True(Convert.ToInt64(finished["actualDurationMinutes"]) >= 0);
            Assert.Equal(90m, Convert.ToDecimal(finished["complianceScore"]));
            Assert.Contains("start_delay", finished["complianceBreakdownJson"]?.ToString(), StringComparison.Ordinal);
            var completedRoute = (await db.QuerySingleAsync("SELECT status,assigned_driver_id,assigned_vehicle_id FROM routes WHERE id=@id", c => c.Parameters.AddWithValue("@id", route)))!;
            Assert.Equal("Completed", completedRoute["status"]);
            Assert.Null(completedRoute["assignedDriverId"]);
            Assert.Null(completedRoute["assignedVehicleId"]);

            var actions = await db.QueryAsync(
                "SELECT action_name FROM audit_logs WHERE company_id=@c AND entity_name='Trip' AND entity_id=@id ORDER BY id",
                c => { c.Parameters.AddWithValue("@c", seed.CompanyId); c.Parameters.AddWithValue("@id", trip); });
            Assert.Equal(new[] { "trip.started", "trip.exception", "trip.resumed", "trip.completed" },
                actions.Select(row => row["actionName"]?.ToString()).ToArray());

            var cancelRoute = await Route(db, seed.CompanyId, seed.VehicleId, seed.DriverId, "Active");
            var cancelTrip = await Trip(db, seed.CompanyId, seed.VehicleId, seed.DriverId, cancelRoute);
            var noCancel = Principal(seed.CompanyId, seed.BranchId, "dispatch:view", "dispatch:update");
            AssertStatus(await InvokeWithBody("TripCancel", cancelTrip, noCancel, "Customer cancelled order", db, audit), StatusCodes.Status403Forbidden);
            AssertStatus(await InvokeWithBody("TripCancel", cancelTrip, http, "Customer cancelled order", db, audit), StatusCodes.Status200OK);
            Assert.Equal("cancelled", (await db.QuerySingleAsync("SELECT status FROM trips WHERE id=@id", c => c.Parameters.AddWithValue("@id", cancelTrip)))!["status"]);
            var cancelledRoute = (await db.QuerySingleAsync("SELECT status,assigned_driver_id,assigned_vehicle_id FROM routes WHERE id=@id", c => c.Parameters.AddWithValue("@id", cancelRoute)))!;
            Assert.Equal("Cancelled", cancelledRoute["status"]);
            Assert.Null(cancelledRoute["assignedDriverId"]);
            Assert.Null(cancelledRoute["assignedVehicleId"]);
        }
        finally { await Cleanup(db, seed.CompanyId); }
    }

    [Fact]
    public async Task BranchAndTenantBoundariesAndMalformedFilters_FailClosed()
    {
        var db = Db();
        await new Batch2SchemaService(db).EnsureAsync();
        await new TripSchemaService(db).EnsureAsync();
        await new DispatchSchemaService(db, NullLogger<DispatchSchemaService>.Instance).EnsureAsync();
        var seed = await Seed(db);
        try
        {
            var trip = await Trip(db, seed.CompanyId, seed.VehicleId, seed.DriverId, null);
            var wrongBranch = Principal(seed.CompanyId, seed.OtherBranchId, "dispatch:view", "dispatch:update");
            var audit = new AuditService(db);
            AssertStatus(await Invoke("TripDetail", trip, wrongBranch, db, CancellationToken.None), StatusCodes.Status404NotFound);
            AssertStatus(await Invoke("TripStart", trip, wrongBranch, db, audit, CancellationToken.None), StatusCodes.Status404NotFound);
            var wrongTenant = Principal(seed.CompanyId + 999_999, seed.BranchId, "dispatch:view", "dispatch:update");
            AssertStatus(await Invoke("TripDetail", trip, wrongTenant, db, CancellationToken.None), StatusCodes.Status404NotFound);
            AssertStatus(await Invoke("TripBreadcrumbs", trip, wrongTenant, db, CancellationToken.None), StatusCodes.Status404NotFound);
            AssertStatus(await Invoke("TripCompliance", trip, wrongTenant, db, CancellationToken.None), StatusCodes.Status404NotFound);
            AssertStatus(await Invoke("TripStart", trip, wrongTenant, db, audit, CancellationToken.None), StatusCodes.Status404NotFound);
            Assert.Equal("planned", (await db.QuerySingleAsync("SELECT status FROM trips WHERE id=@id", c => c.Parameters.AddWithValue("@id", trip)))!["status"]);
            Assert.Equal(0, await db.ScalarLongAsync("SELECT COUNT(*) FROM audit_logs WHERE company_id=@c AND entity_id=@id", c => { c.Parameters.AddWithValue("@c", seed.CompanyId); c.Parameters.AddWithValue("@id", trip); }));

            var valid = Principal(seed.CompanyId, seed.BranchId, "dispatch:view");
            valid.Request.QueryString = new QueryString("?vehicleId=not-a-number");
            AssertStatus(await Invoke("TripsList", valid, db, CancellationToken.None), StatusCodes.Status400BadRequest);
            valid.Request.QueryString = new QueryString("?limit=-1");
            AssertStatus(await Invoke("TripsList", valid, db, CancellationToken.None), StatusCodes.Status400BadRequest);
            valid.Request.QueryString = new QueryString("?status=teleported");
            AssertStatus(await Invoke("TripsList", valid, db, CancellationToken.None), StatusCodes.Status400BadRequest);

            var otherRoute = await Route(db, seed.CompanyId, seed.VehicleId, seed.DriverId, "Planned");
            await db.ExecuteAsync("UPDATE routes SET branch_id=@b WHERE id=@id",
                c => { c.Parameters.AddWithValue("@b", seed.OtherBranchId); c.Parameters.AddWithValue("@id", otherRoute); });
            var contradictoryTrip = await Trip(db, seed.CompanyId, seed.VehicleId, seed.DriverId, otherRoute);
            AssertStatus(await Invoke("TripDetail", contradictoryTrip, valid, db, CancellationToken.None), StatusCodes.Status404NotFound);
            AssertStatus(await Invoke("TripStart", contradictoryTrip, Principal(seed.CompanyId, seed.BranchId, "dispatch:update"), db, audit, CancellationToken.None), StatusCodes.Status404NotFound);
        }
        finally { await Cleanup(db, seed.CompanyId); }
    }

    [Fact]
    public async Task ConcurrentStartsSharingVehicleAndDriver_AllowExactlyOneActiveTrip()
    {
        var setupDb = Db();
        await new Batch2SchemaService(setupDb).EnsureAsync();
        await new TripSchemaService(setupDb).EnsureAsync();
        await new DispatchSchemaService(setupDb, NullLogger<DispatchSchemaService>.Instance).EnsureAsync();
        var seed = await Seed(setupDb);
        try
        {
            var first = await Trip(setupDb, seed.CompanyId, seed.VehicleId, seed.DriverId, null);
            var second = await Trip(setupDb, seed.CompanyId, seed.VehicleId, seed.DriverId, null);
            var calls = new[] { first, second }.Select(async id =>
            {
                var callDb = Db();
                return await Invoke("TripStart", id,
                    Principal(seed.CompanyId, seed.BranchId, "dispatch:view", "dispatch:update"),
                    callDb, new AuditService(callDb), CancellationToken.None);
            });
            var results = await Task.WhenAll(calls);

            Assert.Single(results.Where(result => Status(result) == StatusCodes.Status200OK));
            Assert.Single(results.Where(result => Status(result) == StatusCodes.Status409Conflict));
            Assert.Equal(1, await setupDb.ScalarLongAsync(
                "SELECT COUNT(*) FROM trips WHERE company_id=@c AND LOWER(status)='active'",
                c => c.Parameters.AddWithValue("@c", seed.CompanyId)));
            Assert.Equal(1, await setupDb.ScalarLongAsync(
                "SELECT COUNT(*) FROM audit_logs WHERE company_id=@c AND action_name='trip.started'",
                c => c.Parameters.AddWithValue("@c", seed.CompanyId)));
        }
        finally { await Cleanup(setupDb, seed.CompanyId); }
    }

    [Fact]
    public async Task BackgroundTelemetryBinding_IsExecutableTenantScopedAndDeterministic()
    {
        var db = Db();
        await new Batch2SchemaService(db).EnsureAsync();
        await new TripSchemaService(db).EnsureAsync();
        var seed = await Seed(db);
        try
        {
            var earlier = await Trip(db, seed.CompanyId, seed.VehicleId, seed.DriverId, null);
            var later = await Trip(db, seed.CompanyId, seed.VehicleId, seed.DriverId, null);
            await db.ExecuteAsync("UPDATE trips SET planned_start_time=NOW()-INTERVAL '10 minutes' WHERE id=@id", c => c.Parameters.AddWithValue("@id", later));
            var eventId = await db.InsertAsync(
                "INSERT INTO location_events(company_id,vehicle_id,lat,lng,event_time) VALUES (@c,@v,40,-74,NOW())",
                c => { c.Parameters.AddWithValue("@c", seed.CompanyId); c.Parameters.AddWithValue("@v", seed.VehicleId); });

            var worker = new TripBackgroundService(db, NullLogger<TripBackgroundService>.Instance, null!);
            await InvokeWorker(worker, "BindLocationEventsAsync");
            await InvokeWorker(worker, "BindLocationEventsAsync");

            var bound = (await db.QuerySingleAsync("SELECT trip_id,trip_sequence FROM location_events WHERE id=@id", c => c.Parameters.AddWithValue("@id", eventId)))!;
            Assert.Equal(earlier, Convert.ToInt64(bound["tripId"]));
            Assert.Equal(1, Convert.ToInt32(bound["tripSequence"]));
            Assert.Equal("active", (await db.QuerySingleAsync("SELECT status FROM trips WHERE id=@id", c => c.Parameters.AddWithValue("@id", earlier)))!["status"]);
            Assert.Equal("planned", (await db.QuerySingleAsync("SELECT status FROM trips WHERE id=@id", c => c.Parameters.AddWithValue("@id", later)))!["status"]);
        }
        finally { await Cleanup(db, seed.CompanyId); }
    }

    [Fact]
    public async Task LifecycleMutations_RequireExplicitUpdatePermission()
    {
        var db = Db();
        await new Batch2SchemaService(db).EnsureAsync();
        await new TripSchemaService(db).EnsureAsync();
        await new DispatchSchemaService(db, NullLogger<DispatchSchemaService>.Instance).EnsureAsync();
        var seed = await Seed(db);
        try
        {
            var audit = new AuditService(db);
            var planned = await Trip(db, seed.CompanyId, seed.VehicleId, seed.DriverId, null);
            var active = await Trip(db, seed.CompanyId, seed.VehicleId, seed.DriverId, null);
            await db.ExecuteAsync("UPDATE trips SET status='active',actual_start_time=NOW(),started_at=NOW() WHERE id=@id",
                c => c.Parameters.AddWithValue("@id", active));

            foreach (var aliasOnly in new[] { "dispatch:assign", "dispatch:cancel" })
            {
                var principal = Principal(seed.CompanyId, seed.BranchId, "dispatch:view", aliasOnly);
                AssertStatus(await Invoke("TripStart", planned, principal, db, audit, CancellationToken.None), StatusCodes.Status403Forbidden);
                AssertStatus(await Invoke("TripComplete", active, principal, db, audit, CancellationToken.None), StatusCodes.Status403Forbidden);
                AssertStatus(await InvokeWithBody("TripException", active, principal, "Unexpected road closure", db, audit), StatusCodes.Status403Forbidden);
            }

            Assert.Equal("planned", (await db.QuerySingleAsync("SELECT status FROM trips WHERE id=@id", c => c.Parameters.AddWithValue("@id", planned)))!["status"]);
            Assert.Equal("active", (await db.QuerySingleAsync("SELECT status FROM trips WHERE id=@id", c => c.Parameters.AddWithValue("@id", active)))!["status"]);
            Assert.Equal(0, await db.ScalarLongAsync("SELECT COUNT(*) FROM audit_logs WHERE company_id=@c AND entity_name='Trip'",
                c => c.Parameters.AddWithValue("@c", seed.CompanyId)));
        }
        finally { await Cleanup(db, seed.CompanyId); }
    }

    [Fact]
    public async Task Start_RejectsTerminalRouteAndResourceDrift_ThenSynchronizesEligibleRoute()
    {
        var db = Db();
        await new Batch2SchemaService(db).EnsureAsync();
        await new TripSchemaService(db).EnsureAsync();
        await new DispatchSchemaService(db, NullLogger<DispatchSchemaService>.Instance).EnsureAsync();
        var seed = await Seed(db);
        try
        {
            var http = Principal(seed.CompanyId, seed.BranchId, "dispatch:view", "dispatch:update");
            var audit = new AuditService(db);
            var route = await Route(db, seed.CompanyId, seed.VehicleId, seed.DriverId, "Planned");
            var trip = await Trip(db, seed.CompanyId, seed.VehicleId, seed.DriverId, route);

            await db.ExecuteAsync("UPDATE routes SET status='Completed' WHERE id=@id", c => c.Parameters.AddWithValue("@id", route));
            AssertStatus(await Invoke("TripStart", trip, http, db, audit, CancellationToken.None), StatusCodes.Status409Conflict);

            var otherVehicle = await db.InsertAsync(
                "INSERT INTO vehicles(company_id,branch_id,vehicle_code,type,vin_exception_type,alternate_identifier,status) VALUES (@c,@b,@code,'Truck','legacy-fleet-identifier',@code,'Available')",
                c => { c.Parameters.AddWithValue("@c", seed.CompanyId); c.Parameters.AddWithValue("@b", seed.BranchId); c.Parameters.AddWithValue("@code", $"VEH-{Guid.NewGuid():N}"[..20]); });
            await db.ExecuteAsync("UPDATE routes SET status='Planned',assigned_vehicle_id=@v WHERE id=@id",
                c => { c.Parameters.AddWithValue("@v", otherVehicle); c.Parameters.AddWithValue("@id", route); });
            AssertStatus(await Invoke("TripStart", trip, http, db, audit, CancellationToken.None), StatusCodes.Status409Conflict);

            await db.ExecuteAsync("UPDATE routes SET assigned_vehicle_id=@v WHERE id=@id",
                c => { c.Parameters.AddWithValue("@v", seed.VehicleId); c.Parameters.AddWithValue("@id", route); });
            AssertStatus(await Invoke("TripStart", trip, http, db, audit, CancellationToken.None), StatusCodes.Status200OK);
            Assert.Equal("Active", (await db.QuerySingleAsync("SELECT status FROM routes WHERE id=@id", c => c.Parameters.AddWithValue("@id", route)))!["status"]);
        }
        finally { await Cleanup(db, seed.CompanyId); }
    }

    [Fact]
    public async Task DeviationRequiresBoundTelemetry_AndRemainsIdempotent()
    {
        var db = Db();
        await new Batch2SchemaService(db).EnsureAsync();
        await new Batch4SchemaService(db).EnsureAsync();
        await db.ExecuteAsync("ALTER TABLE safety_events ADD COLUMN IF NOT EXISTS system_insight TEXT NULL");
        await new TripSchemaService(db).EnsureAsync();
        var seed = await Seed(db);
        try
        {
            var trip = await Trip(db, seed.CompanyId, seed.VehicleId, seed.DriverId, null);
            await db.ExecuteAsync("UPDATE trips SET status='active',actual_start_time=NOW()-INTERVAL '2 hours',started_at=NOW()-INTERVAL '2 hours' WHERE id=@id",
                c => c.Parameters.AddWithValue("@id", trip));
            var stop = await db.InsertAsync(
                @"INSERT INTO trip_stops(company_id,trip_id,stop_sequence,address,lat,lng,time_window_end,status)
                  VALUES (@c,@t,1,'Overdue stop',40,-74,NOW()-INTERVAL '2 hours','pending')",
                c => { c.Parameters.AddWithValue("@c", seed.CompanyId); c.Parameters.AddWithValue("@t", trip); });
            var worker = new TripBackgroundService(db, NullLogger<TripBackgroundService>.Instance, null!);

            await InvokeWorker(worker, "GenerateDeviationAlertsAsync");
            Assert.Equal(0, await DeviationCount(db, seed.CompanyId, trip, stop));

            await db.InsertAsync(
                "INSERT INTO location_events(company_id,vehicle_id,trip_id,lat,lng,event_time) VALUES (@c,@v,@t,41,-75,NOW())",
                c => { c.Parameters.AddWithValue("@c", seed.CompanyId); c.Parameters.AddWithValue("@v", seed.VehicleId); c.Parameters.AddWithValue("@t", trip); });
            await InvokeWorker(worker, "GenerateDeviationAlertsAsync");
            await InvokeWorker(worker, "GenerateDeviationAlertsAsync");
            Assert.Equal(1, await DeviationCount(db, seed.CompanyId, trip, stop));
            Assert.Equal(1, await db.ScalarLongAsync("SELECT CASE WHEN deviation_flagged THEN 1 ELSE 0 END FROM trip_stops WHERE id=@id", c => c.Parameters.AddWithValue("@id", stop)));
        }
        finally { await Cleanup(db, seed.CompanyId); }
    }

    [Fact]
    public async Task ConcurrentBackgroundInstance_SkipsWhenCycleLockIsOwned()
    {
        var ownerDb = Db();
        await new Batch2SchemaService(ownerDb).EnsureAsync();
        await new TripSchemaService(ownerDb).EnsureAsync();
        var seed = await Seed(ownerDb);
        try
        {
            var route = await Route(ownerDb, seed.CompanyId, seed.VehicleId, seed.DriverId, "Active");
            await ownerDb.RunInSystemTransactionAsync(async () =>
            {
                await ownerDb.ExecuteAsync("SELECT pg_advisory_xact_lock(hashtextextended('trip-background-cycle',0))");
                var contenderDb = Db();
                var contender = new TripBackgroundService(contenderDb, NullLogger<TripBackgroundService>.Instance, null!);
                await contenderDb.RunInSystemTransactionAsync(async () =>
                {
                    await InvokeWorker(contender, "RunCycleAsync");
                    return true;
                });
                Assert.Equal(0, await ownerDb.ScalarLongAsync("SELECT COUNT(*) FROM trips WHERE company_id=@c AND route_id=@r",
                    c => { c.Parameters.AddWithValue("@c", seed.CompanyId); c.Parameters.AddWithValue("@r", route); }));
                return true;
            });
        }
        finally { await Cleanup(ownerDb, seed.CompanyId); }
    }

    private static Task<long> DeviationCount(Database db, long company, long trip, long stop) => db.ScalarLongAsync(
        @"SELECT COUNT(*) FROM safety_events WHERE company_id=@c AND event_type='route_deviation'
          AND meta_json->>'tripId'=@t AND meta_json->>'stopId'=@s",
        c => { c.Parameters.AddWithValue("@c", company); c.Parameters.AddWithValue("@t", trip.ToString()); c.Parameters.AddWithValue("@s", stop.ToString()); });

    private static async Task InvokeWorker(TripBackgroundService worker, string methodName)
    {
        var method = typeof(TripBackgroundService).GetMethod(methodName, BindingFlags.NonPublic | BindingFlags.Instance)!;
        var args = methodName == "RunCycleAsync"
            ? new object[] { -1L, CancellationToken.None }
            : new object[] { CancellationToken.None };
        try { await (Task)method.Invoke(worker, args)!; }
        catch (TargetInvocationException ex) when (ex.InnerException is not null)
        { System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(ex.InnerException).Throw(); throw; }
    }

    private static async Task<IResult> Invoke(string name, params object[] args)
    {
        var method = typeof(EndpointMappings).GetMethod(name, BindingFlags.NonPublic | BindingFlags.Static)!;
        try { return await (Task<IResult>)method.Invoke(null, args)!; }
        catch (TargetInvocationException ex) when (ex.InnerException is not null)
        { System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(ex.InnerException).Throw(); throw; }
    }

    private static Task<IResult> InvokeWithBody(string name, long tripId, HttpContext http, string text, Database db, AuditService audit)
    {
        var method = typeof(EndpointMappings).GetMethod(name, BindingFlags.NonPublic | BindingFlags.Static)!;
        var bodyType = method.GetParameters()[2].ParameterType;
        var body = Activator.CreateInstance(bodyType, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
            binder: null, args: new object?[] { text }, culture: null)!;
        return Invoke(name, tripId, http, body, db, audit, CancellationToken.None);
    }

    private static void AssertStatus(IResult result, int expected) => Assert.Equal(expected, Status(result));
    private static int? Status(IResult result) => Assert.IsAssignableFrom<IStatusCodeHttpResult>(result).StatusCode;

    private static Database Db() => new(new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
    { ["ConnectionStrings:DefaultConnection"] = TestDb.ConnectionString, ["Rls:EnforceTenantContext"] = "false" }).Build());

    private static DefaultHttpContext Principal(long company, long branch, params string[] permissions)
    {
        var http = new DefaultHttpContext();
        http.Items[EndpointMappings.AuthCompanyIdItemKey] = company;
        http.Items[EndpointMappings.AuthBranchIdItemKey] = branch;
        http.Items[EndpointMappings.AuthUserIdItemKey] = 42L;
        http.Items[EndpointMappings.AuthRoleItemKey] = "Trip Test Operator";
        http.Items[EndpointMappings.AuthPermissionsItemKey] = permissions;
        return http;
    }

    [Fact]
    public async Task RouteAssignmentCreatesLinkedTripAndStopsBeforeReturningAndIsIdempotent()
    {
        var db = Db(); await new TripSchemaService(db).EnsureAsync();
        await new DispatchSchemaService(db, NullLogger<DispatchSchemaService>.Instance).EnsureAsync();
        var seed = await Seed(db);
        try
        {
            var route = await Route(db, seed.CompanyId, seed.VehicleId, seed.DriverId, "Planned");
            await db.ExecuteAsync("UPDATE routes SET branch_id=@b WHERE id=@r", c => { c.Parameters.AddWithValue("b", seed.BranchId); c.Parameters.AddWithValue("r", route); });
            var job = await db.InsertAsync("INSERT INTO jobs(company_id,branch_id,route_id,job_code,job_type,status) VALUES (@c,@b,@r,@code,'Delivery','Assigned')", c => { c.Parameters.AddWithValue("c", seed.CompanyId); c.Parameters.AddWithValue("b", seed.BranchId); c.Parameters.AddWithValue("r", route); c.Parameters.AddWithValue("code", $"SYNC-{Guid.NewGuid():N}"); });
            foreach (var (sequence, type, address) in new[] { (1, "Pickup", "Dock A"), (2, "Delivery", "Dock B") })
                await db.ExecuteAsync("INSERT INTO route_stops(company_id,route_id,job_id,stop_sequence,stop_type,address,status) VALUES (@c,@r,@j,@s,@t,@a,'Pending')", c => { c.Parameters.AddWithValue("c", seed.CompanyId); c.Parameters.AddWithValue("r", route); c.Parameters.AddWithValue("j", job); c.Parameters.AddWithValue("s", sequence); c.Parameters.AddWithValue("t", type); c.Parameters.AddWithValue("a", address); });
            async Task<IResult> Activate()
            {
                var connection = Db();
                return await Invoke("AssignRoute", Principal(seed.CompanyId, seed.BranchId, "dispatch:assign"), route,
                    new Dictionary<string, object?> { ["driverId"] = seed.DriverId, ["vehicleId"] = seed.VehicleId }, connection, new AuditService(connection), CancellationToken.None);
            }
            var results = await Task.WhenAll(Activate(), Activate());
            Assert.All(results, result => AssertStatus(result, 200));
            var trips = await db.QueryAsync("SELECT * FROM trips WHERE company_id=@c AND route_id=@r", c => { c.Parameters.AddWithValue("c", seed.CompanyId); c.Parameters.AddWithValue("r", route); });
            var trip = Assert.Single(trips);
            Assert.Equal(job, Convert.ToInt64(trip["jobId"]));
            Assert.Equal("planned", trip["status"]);
            Assert.Equal("runtime_route_projection", trip["dataOrigin"]);
            Assert.Equal("derived_from_recorded_route", trip["verificationStatus"]);
            Assert.Equal("Dock A", trip["origin"]); Assert.Equal("Dock B", trip["destination"]);
            var tripId = Convert.ToInt64(trip["id"]);
            Assert.Equal(2, await db.ScalarLongAsync("SELECT COUNT(*) FROM trip_stops WHERE company_id=@c AND trip_id=@t AND status='pending'", c => { c.Parameters.AddWithValue("c", seed.CompanyId); c.Parameters.AddWithValue("t", tripId); }));
            await db.ExecuteAsync("UPDATE trips SET status='completed' WHERE id=@t", c => c.Parameters.AddWithValue("t", tripId));
            AssertStatus(await Activate(), 200);
            Assert.Equal(1, await db.ScalarLongAsync("SELECT COUNT(*) FROM trips WHERE company_id=@c AND route_id=@r", c => { c.Parameters.AddWithValue("c", seed.CompanyId); c.Parameters.AddWithValue("r", route); }));
        }
        finally { await Cleanup(db, seed.CompanyId); }
    }

    [Fact]
    public async Task TripStartRechecksAuthoritativeHosAndMaintenanceAfterAssignment()
    {
        var db = Db(); await new TripSchemaService(db).EnsureAsync(); var seed = await Seed(db);
        try
        {
            var http = Principal(seed.CompanyId, seed.BranchId, "dispatch:update");
            var trip = await Trip(db, seed.CompanyId, seed.VehicleId, seed.DriverId, null);
            var audit = new AuditService(db);
            await db.ExecuteAsync("UPDATE hos_clocks SET source_observed_at=NOW()-INTERVAL '25 hours' WHERE company_id=@c", c => c.Parameters.AddWithValue("c", seed.CompanyId));
            AssertStatus(await Invoke("TripStart", trip, http, db, audit, CancellationToken.None), 409);
            Assert.Equal("planned", (await db.QuerySingleAsync("SELECT status FROM trips WHERE id=@id", c => c.Parameters.AddWithValue("id", trip)))!["status"]);
            await db.ExecuteAsync("UPDATE hos_clocks SET source_observed_at=NOW() WHERE company_id=@c", c => c.Parameters.AddWithValue("c", seed.CompanyId));
            await db.ExecuteAsync("UPDATE vehicles SET out_of_service=true WHERE id=@id", c => c.Parameters.AddWithValue("id", seed.VehicleId));
            AssertStatus(await Invoke("TripStart", trip, http, db, audit, CancellationToken.None), 409);
            await db.ExecuteAsync("UPDATE vehicles SET out_of_service=false WHERE id=@id", c => c.Parameters.AddWithValue("id", seed.VehicleId));
            await db.ExecuteAsync("DELETE FROM hos_clocks WHERE company_id=@c", c => c.Parameters.AddWithValue("c", seed.CompanyId));
            AssertStatus(await Invoke("TripStart", trip, http, db, audit, CancellationToken.None), 409);
        }
        finally { await Cleanup(db, seed.CompanyId); }
    }

    private static async Task<SeedData> Seed(Database db)
    {
        var suffix = Guid.NewGuid().ToString("N")[..10];
        var company = await db.InsertAsync(
            "INSERT INTO companies(company_code,name,industry) VALUES (@code,'Trip lifecycle test','Transportation')",
            c => c.Parameters.AddWithValue("@code", $"TRIP-{suffix}"));
        var branch = await db.InsertAsync(
            "INSERT INTO branches(company_id,branch_code,name,status) VALUES (@c,'MAIN','Main','Active')",
            c => c.Parameters.AddWithValue("@c", company));
        var otherBranch = await db.InsertAsync(
            "INSERT INTO branches(company_id,branch_code,name,status) VALUES (@c,'OTHER','Other','Active')",
            c => c.Parameters.AddWithValue("@c", company));
        var driver = await db.InsertAsync(
            "INSERT INTO drivers(company_id,branch_id,driver_code,full_name,status) VALUES (@c,@b,@code,'Trip Test Driver','Available')",
            c => { c.Parameters.AddWithValue("@c", company); c.Parameters.AddWithValue("@b", branch); c.Parameters.AddWithValue("@code", $"DRV-{suffix}"); });
        var vehicle = await db.InsertAsync(
            "INSERT INTO vehicles(company_id,branch_id,vehicle_code,type,vin_exception_type,alternate_identifier,status) VALUES (@c,@b,@code,'Truck','legacy-fleet-identifier',@code,'Available')",
            c => { c.Parameters.AddWithValue("@c", company); c.Parameters.AddWithValue("@b", branch); c.Parameters.AddWithValue("@code", $"VEH-{suffix}"); });
        await db.ExecuteAsync("INSERT INTO hos_clocks(company_id,branch_id,driver_id,drive_time_remaining_minutes,shift_time_remaining_minutes,cycle_time_remaining_minutes,status,clock_source,source_observed_at,source_authority,source_quality) VALUES (@c,@b,@d,480,480,3600,'OK','local-test-provider',NOW(),'Authoritative','Verified')",
            c => { c.Parameters.AddWithValue("c", company); c.Parameters.AddWithValue("b", branch); c.Parameters.AddWithValue("d", driver); });
        return new(company, branch, otherBranch, driver, vehicle);
    }

    private static Task<long> Route(Database db, long company, long vehicle, long driver, string status) => db.InsertAsync(
        "INSERT INTO routes(company_id,route_code,name,status,assigned_vehicle_id,assigned_driver_id) VALUES (@c,@code,@name,@status,@v,@d)",
        c =>
        {
            var code = $"R-{Guid.NewGuid():N}"[..20];
            c.Parameters.AddWithValue("@c", company); c.Parameters.AddWithValue("@code", code);
            c.Parameters.AddWithValue("@name", code); c.Parameters.AddWithValue("@status", status);
            c.Parameters.AddWithValue("@v", vehicle); c.Parameters.AddWithValue("@d", driver);
        });

    private static Task<long> Trip(Database db, long company, long vehicle, long driver, long? route) => db.InsertAsync(
        @"INSERT INTO trips(company_id,vehicle_id,driver_id,route_id,status,trip_ref,planned_start_time)
          VALUES (@c,@v,@d,@r,'planned',@ref,NOW()-INTERVAL '20 minutes')",
        c =>
        {
            c.Parameters.AddWithValue("@c", company); c.Parameters.AddWithValue("@v", vehicle);
            c.Parameters.AddWithValue("@d", driver); c.Parameters.AddWithValue("@r", (object?)route ?? DBNull.Value);
            c.Parameters.AddWithValue("@ref", $"TRP-{Guid.NewGuid():N}"[..24]);
        });

    private static async Task Cleanup(Database db, long company)
    {
        foreach (var sql in new[]
        {
            "DELETE FROM safety_events WHERE company_id=@c", "DELETE FROM audit_logs WHERE company_id=@c",
            "DELETE FROM location_events WHERE company_id=@c", "DELETE FROM trip_stops WHERE company_id=@c",
            "DELETE FROM dispatch_assignments WHERE company_id=@c", "DELETE FROM trips WHERE company_id=@c",
            "DELETE FROM route_stops WHERE route_id IN (SELECT id FROM routes WHERE company_id=@c)",
            "DELETE FROM routes WHERE company_id=@c", "DELETE FROM jobs WHERE company_id=@c",
            "DELETE FROM hos_clocks WHERE company_id=@c", "DELETE FROM vehicles WHERE company_id=@c", "DELETE FROM drivers WHERE company_id=@c",
            "DELETE FROM branches WHERE company_id=@c", "DELETE FROM companies WHERE id=@c"
        }) await db.ExecuteAsync(sql, c => c.Parameters.AddWithValue("@c", company));
    }

    private sealed record SeedData(long CompanyId, long BranchId, long OtherBranchId, long DriverId, long VehicleId);
}
