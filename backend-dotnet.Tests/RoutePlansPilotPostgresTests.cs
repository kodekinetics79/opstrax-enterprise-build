using System.Reflection;
using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Opstrax.Api.Controllers;
using Opstrax.Api.Data;
using Opstrax.Api.Foundation;
using Opstrax.Api.Services;

namespace Opstrax.Tests;

[Trait("Category", "Integration")]
public sealed class RoutePlansPilotPostgresTests
{
    [Fact]
    public async Task Stage63AndTerminalRlsContractsArePresentOnTheFreshSchema()
    {
        var db = Db();
        var indexes = await db.QueryAsync(
            "SELECT indexname,indexdef FROM pg_indexes WHERE schemaname='public' AND indexname=ANY(@names) ORDER BY indexname",
            c => c.Parameters.AddWithValue("@names", new[]
            {
                "idx_routes_company_branch", "uq_routes_company_code_active", "uq_route_stops_company_route_sequence",
                "uq_routes_active_driver", "uq_routes_active_vehicle",
            }));
        Assert.Equal(5, indexes.Count);
        Assert.Contains(indexes, row => row["indexName"]?.ToString() == "uq_routes_active_driver" &&
            row["indexDef"]?.ToString()?.Contains("status", StringComparison.OrdinalIgnoreCase) == true);
        var rls = await db.QuerySingleAsync(
            "SELECT relrowsecurity rls_enabled,relforcerowsecurity rls_forced FROM pg_class WHERE oid='public.routes'::regclass");
        Assert.NotNull(rls);
        Assert.Equal(true, rls!["rlsEnabled"]);
        Assert.Equal(true, rls["rlsForced"]);
        Assert.Equal(4, await db.ScalarLongAsync(
            "SELECT COUNT(*) FROM pg_policies WHERE schemaname='public' AND tablename IN ('routes','route_stops') AND policyname IN ('tenant_ticket_app','system_control_plane')"));
    }

    [Fact]
    public async Task RealRouteWorkflowEnforcesBranchConcurrencyHosStopsOptimizationAndArchive()
    {
        var db = Db();
        await EnsureSchema(db);
        var companyId = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() + Random.Shared.Next(2_000_000, 2_900_000);
        const long branchA = 9311;
        const long branchB = 9312;
        await SeedCompany(db, companyId);
        try
        {
            var driver = await db.InsertAsync(
                "INSERT INTO drivers(company_id,branch_id,driver_code,full_name,status,safety_score,readiness_score,compliance_score) VALUES (@c,@b,@code,'Route Driver','Available',95,95,95)",
                c => { c.Parameters.AddWithValue("@c", companyId); c.Parameters.AddWithValue("@b", branchA); c.Parameters.AddWithValue("@code", $"RDRV-{companyId}"); });
            var vehicle = await db.InsertAsync(
                "INSERT INTO vehicles(company_id,branch_id,vehicle_code,type,vin_exception_type,alternate_identifier,status,availability_status,out_of_service,readiness_score,risk_score) VALUES (@c,@b,@code,'Truck','legacy-fleet-identifier',@code,'Available','available',false,95,5)",
                c => { c.Parameters.AddWithValue("@c", companyId); c.Parameters.AddWithValue("@b", branchA); c.Parameters.AddWithValue("@code", $"RVEH-{companyId}"); });
            await db.ExecuteAsync(
                "INSERT INTO hos_records(company_id,driver_id,shift_date,remaining_drive_hours,remaining_shift_hours,hos_status) VALUES (@c,@d,CURRENT_DATE,8,8,'On Duty')",
                c => { c.Parameters.AddWithValue("@c", companyId); c.Parameters.AddWithValue("@d", driver); });
            var customer = await db.InsertAsync(
                "INSERT INTO customers(company_id,customer_code,name,status) VALUES (@c,@code,'Route Customer','Active')",
                c => { c.Parameters.AddWithValue("@c", companyId); c.Parameters.AddWithValue("@code", $"RCUS-{companyId}"); });
            var job = await db.InsertAsync(
                "INSERT INTO jobs(company_id,branch_id,customer_id,job_code,job_type,status) VALUES (@c,@b,@customer,@code,'Delivery','Unassigned')",
                c => { c.Parameters.AddWithValue("@c", companyId); c.Parameters.AddWithValue("@b", branchA); c.Parameters.AddWithValue("@customer", customer); c.Parameters.AddWithValue("@code", $"RJOB-{companyId}"); });

            var httpA = Principal(companyId, branchA);
            var audit = new AuditService(db);
            var invalid = await Invoke("CreateRoute", httpA, new Dictionary<string, object?>
            {
                ["routeCode"] = $"BAD-{companyId}", ["routeName"] = "Bad window",
                ["plannedStart"] = "2026-08-02T12:00:00Z", ["plannedEnd"] = "2026-08-02T10:00:00Z",
            }, db, audit, CancellationToken.None);
            Assert.Equal(StatusCodes.Status400BadRequest, Status(invalid));

            var terminalCreate = await Invoke("CreateRoute", httpA, new Dictionary<string, object?>
            {
                ["routeCode"] = $"TERMINAL-{companyId}", ["routeName"] = "Injected terminal route", ["status"] = "Completed",
                ["plannedStart"] = "2026-08-02T10:00:00Z", ["plannedEnd"] = "2026-08-02T14:00:00Z",
            }, db, audit, CancellationToken.None);
            Assert.Equal(StatusCodes.Status400BadRequest, Status(terminalCreate));

            var viewer = Principal(companyId, branchA);
            viewer.Items[EndpointMappings.AuthPermissionsItemKey] = new[] { "dispatch:view" };
            var deniedCreate = await Invoke("CreateRoute", viewer, new Dictionary<string, object?>
            {
                ["routeCode"] = $"DENIED-{companyId}", ["routeName"] = "Denied route",
                ["plannedStart"] = "2026-08-02T10:00:00Z", ["plannedEnd"] = "2026-08-02T14:00:00Z",
            }, db, audit, CancellationToken.None);
            Assert.Equal(StatusCodes.Status403Forbidden, Status(deniedCreate));

            var routeA = await CreateRoute(db, audit, companyId, branchA, $"RA-{companyId}");
            var routeB = await CreateRoute(db, audit, companyId, branchA, $"RB-{companyId}");
            var crossBranch = await Invoke("RouteDetail", Principal(companyId, branchB), routeA, db, CancellationToken.None);
            Assert.Equal(StatusCodes.Status404NotFound, Status(crossBranch));

            var partialInvalidWindow = await Invoke("UpdateRoute", httpA, routeA,
                new Dictionary<string, object?> { ["plannedStart"] = "2026-08-02T15:00:00Z" }, db, audit, CancellationToken.None);
            Assert.Equal(StatusCodes.Status400BadRequest, Status(partialInvalidWindow));

            var wrongBranchJob = await db.InsertAsync(
                "INSERT INTO jobs(company_id,branch_id,customer_id,job_code,job_type,status) VALUES (@c,@b,@customer,@code,'Delivery','Unassigned')",
                c => { c.Parameters.AddWithValue("@c", companyId); c.Parameters.AddWithValue("@b", branchB); c.Parameters.AddWithValue("@customer", customer); c.Parameters.AddWithValue("@code", $"RJOB-B-{companyId}"); });
            var tenantWide = Principal(companyId, branchA);
            tenantWide.Items.Remove(EndpointMappings.AuthBranchIdItemKey);
            var branchMismatchStop = await Invoke("CreateRouteStop", tenantWide, routeA, new Dictionary<string, object?>
            {
                ["stopSequence"] = 99, ["jobId"] = wrongBranchJob, ["customerId"] = customer,
                ["stopType"] = "Pickup", ["address"] = "Wrong Branch Way",
            }, db, audit, CancellationToken.None);
            Assert.Equal(StatusCodes.Status400BadRequest, Status(branchMismatchStop));

            var explicitBranchRoute = await CreateRoute(db, audit, companyId, branchB, $"RC-{companyId}");
            await db.ExecuteAsync("UPDATE routes SET assigned_driver_id=@d WHERE id=@id AND company_id=@c",
                c => { c.Parameters.AddWithValue("@d", driver); c.Parameters.AddWithValue("@id", explicitBranchRoute); c.Parameters.AddWithValue("@c", companyId); });
            var staleLegacyRelationship = await Invoke("RouteDetail", httpA, explicitBranchRoute, db, CancellationToken.None);
            Assert.Equal(StatusCodes.Status404NotFound, Status(staleLegacyRelationship));

            var paged = Principal(companyId, branchA);
            paged.Request.QueryString = new QueryString($"?limit=1&offset=0&status=Planned&search=RA-{companyId}");
            var page = await Invoke("Routes", paged, db, CancellationToken.None);
            Assert.Equal(StatusCodes.Status200OK, Status(page));
            Assert.Equal("1", paged.Response.Headers["X-Total-Count"].ToString());
            Assert.Contains($"RA-{companyId}", Json(page), StringComparison.Ordinal);

            var exhaustedDriver = await db.InsertAsync(
                "INSERT INTO drivers(company_id,branch_id,driver_code,full_name,status,safety_score,readiness_score,compliance_score) VALUES (@c,@b,@code,'Exhausted Driver','Available',95,95,95)",
                c => { c.Parameters.AddWithValue("@c", companyId); c.Parameters.AddWithValue("@b", branchA); c.Parameters.AddWithValue("@code", $"RDRV-X-{companyId}"); });
            var spareVehicle = await db.InsertAsync(
                "INSERT INTO vehicles(company_id,branch_id,vehicle_code,type,vin_exception_type,alternate_identifier,status,availability_status,out_of_service,readiness_score,risk_score) VALUES (@c,@b,@code,'Truck','legacy-fleet-identifier',@code,'Available','available',false,95,5)",
                c => { c.Parameters.AddWithValue("@c", companyId); c.Parameters.AddWithValue("@b", branchA); c.Parameters.AddWithValue("@code", $"RVEH-X-{companyId}"); });
            await db.ExecuteAsync(
                "INSERT INTO hos_records(company_id,driver_id,shift_date,remaining_drive_hours,remaining_shift_hours,hos_status) VALUES (@c,@d,CURRENT_DATE,0.5,8,'On Duty')",
                c => { c.Parameters.AddWithValue("@c", companyId); c.Parameters.AddWithValue("@d", exhaustedDriver); });
            var hosDenied = await Invoke("AssignRoute", httpA, routeB,
                new Dictionary<string, object?> { ["driverId"] = exhaustedDriver, ["vehicleId"] = spareVehicle }, db, audit, CancellationToken.None);
            Assert.Equal(StatusCodes.Status400BadRequest, Status(hosDenied));
            Assert.Contains("remaining drive time", Json(hosDenied), StringComparison.OrdinalIgnoreCase);

            await db.ExecuteAsync("UPDATE vehicles SET out_of_service=true,availability_status='out_of_service' WHERE id=@id AND company_id=@c",
                c => { c.Parameters.AddWithValue("@id", spareVehicle); c.Parameters.AddWithValue("@c", companyId); });
            var maintenanceDenied = await Invoke("AssignRoute", httpA, routeB,
                new Dictionary<string, object?> { ["driverId"] = driver, ["vehicleId"] = spareVehicle }, db, audit, CancellationToken.None);
            Assert.Equal(StatusCodes.Status400BadRequest, Status(maintenanceDenied));
            Assert.Contains("out-of-service", Json(maintenanceDenied), StringComparison.OrdinalIgnoreCase);

            async Task<IResult> AssignFresh(long routeId)
            {
                var concurrentDb = Db();
                return await Invoke("AssignRoute", Principal(companyId, branchA), routeId,
                    new Dictionary<string, object?> { ["driverId"] = driver, ["vehicleId"] = vehicle },
                    concurrentDb, new AuditService(concurrentDb), CancellationToken.None);
            }
            var assignResults = await Task.WhenAll(AssignFresh(routeA), AssignFresh(routeB));
            Assert.Equal(1, assignResults.Count(result => Status(result) == StatusCodes.Status200OK));
            Assert.Equal(1, assignResults.Count(result => Status(result) == StatusCodes.Status409Conflict || Status(result) == StatusCodes.Status400BadRequest));
            var activeRoute = Convert.ToInt64((await db.QuerySingleAsync(
                "SELECT id FROM routes WHERE company_id=@c AND status='Active' AND assigned_driver_id=@d",
                c => { c.Parameters.AddWithValue("@c", companyId); c.Parameters.AddWithValue("@d", driver); }))!["id"]);

            var assignedEdit = await Invoke("UpdateRoute", httpA, activeRoute,
                new Dictionary<string, object?> { ["routeName"] = "Assigned route edited", ["region"] = "East" },
                db, audit, CancellationToken.None);
            Assert.Equal(StatusCodes.Status200OK, Status(assignedEdit));
            var assignedAfterEdit = await db.QuerySingleAsync(
                "SELECT route_name,region,assigned_driver_id,assigned_vehicle_id FROM routes WHERE id=@id AND company_id=@c",
                c => { c.Parameters.AddWithValue("@id", activeRoute); c.Parameters.AddWithValue("@c", companyId); });
            Assert.NotNull(assignedAfterEdit);
            Assert.Equal("Assigned route edited", assignedAfterEdit!["routeName"]?.ToString());
            Assert.Equal("East", assignedAfterEdit["region"]?.ToString());
            Assert.Equal(driver, Convert.ToInt64(assignedAfterEdit["assignedDriverId"]));
            Assert.Equal(vehicle, Convert.ToInt64(assignedAfterEdit["assignedVehicleId"]));

            var contestedRouteA = await CreateRoute(db, audit, companyId, branchA, $"RACE-A-{companyId}");
            var contestedRouteB = await CreateRoute(db, audit, companyId, branchA, $"RACE-B-{companyId}");
            var contestedJob = await db.InsertAsync(
                "INSERT INTO jobs(company_id,branch_id,customer_id,job_code,job_type,status) VALUES (@c,@b,@customer,@code,'Delivery','Unassigned')",
                c => { c.Parameters.AddWithValue("@c", companyId); c.Parameters.AddWithValue("@b", branchA); c.Parameters.AddWithValue("@customer", customer); c.Parameters.AddWithValue("@code", $"RJOB-RACE-{companyId}"); });
            async Task<IResult> CreateContestedJobStop(long routeId)
            {
                var concurrentDb = Db();
                return await Invoke("CreateRouteStop", Principal(companyId, branchA), routeId, new Dictionary<string, object?>
                {
                    ["stopSequence"] = 50, ["jobId"] = contestedJob, ["customerId"] = customer,
                    ["stopType"] = "Delivery", ["address"] = $"Contested route {routeId}",
                }, concurrentDb, new AuditService(concurrentDb), CancellationToken.None);
            }
            var contestedResults = await Task.WhenAll(CreateContestedJobStop(contestedRouteA), CreateContestedJobStop(contestedRouteB));
            Assert.Equal(1, contestedResults.Count(result => Status(result) == StatusCodes.Status201Created));
            Assert.Equal(1, contestedResults.Count(result => Status(result) == StatusCodes.Status409Conflict));
            Assert.Equal(1, await db.ScalarLongAsync("SELECT COUNT(DISTINCT route_id) FROM route_stops WHERE company_id=@c AND job_id=@j",
                c => { c.Parameters.AddWithValue("@c", companyId); c.Parameters.AddWithValue("@j", contestedJob); }));

            var stopBody = new Dictionary<string, object?>
            {
                ["stopSequence"] = 1, ["jobId"] = job, ["customerId"] = customer, ["stopType"] = "Pickup",
                ["address"] = "100 Pilot Way", ["latitude"] = 38.75m, ["longitude"] = -77.47m,
                ["timeWindowStart"] = "2026-08-02T10:00:00Z", ["timeWindowEnd"] = "2026-08-02T11:00:00Z",
            };
            async Task<IResult> CreateStopFresh()
            {
                var concurrentDb = Db();
                return await Invoke("CreateRouteStop", Principal(companyId, branchA), activeRoute, stopBody,
                    concurrentDb, new AuditService(concurrentDb), CancellationToken.None);
            }
            var concurrentStops = await Task.WhenAll(CreateStopFresh(), CreateStopFresh());
            Assert.Equal(1, concurrentStops.Count(result => Status(result) == StatusCodes.Status201Created));
            Assert.Equal(1, concurrentStops.Count(result => Status(result) == StatusCodes.Status409Conflict));
            Assert.Equal(1, await db.ScalarLongAsync("SELECT total_stops FROM routes WHERE id=@id", c => c.Parameters.AddWithValue("@id", activeRoute)));
            Assert.Equal(activeRoute, await db.ScalarLongAsync("SELECT route_id FROM jobs WHERE id=@id", c => c.Parameters.AddWithValue("@id", job)));

            var unavailable = await Invoke("RouteOptimizePreview", Principal(companyId, branchA), activeRoute, db, audit, CancellationToken.None);
            Assert.Contains("optimizationAvailable\":false", Json(unavailable), StringComparison.OrdinalIgnoreCase);
            var secondStop = await Invoke("CreateRouteStop", Principal(companyId, branchA), activeRoute, new Dictionary<string, object?>
            {
                ["stopSequence"] = 2, ["customerId"] = customer, ["stopType"] = "Drop-off", ["address"] = "200 Pilot Way",
                ["latitude"] = 38.80m, ["longitude"] = -77.40m, ["timeWindowStart"] = "2026-08-02T11:00:00Z", ["timeWindowEnd"] = "2026-08-02T12:00:00Z",
            }, db, audit, CancellationToken.None);
            Assert.Equal(StatusCodes.Status201Created, Status(secondStop));
            var optimized = await Invoke("RouteOptimizePreview", Principal(companyId, branchA), activeRoute, db, audit, CancellationToken.None);
            var optimizedJson = Json(optimized);
            Assert.Contains("optimizationAvailable\":true", optimizedJson, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("deterministic-geodesic-sequencing", optimizedJson, StringComparison.Ordinal);
            Assert.DoesNotContain("estimatedSavingsMinutes", optimizedJson, StringComparison.Ordinal);

            var archiveActive = await Invoke("ArchiveRoute", Principal(companyId, branchA), activeRoute, db, audit, CancellationToken.None);
            Assert.Equal(StatusCodes.Status409Conflict, Status(archiveActive));
            var complete = await Invoke("UpdateRoute", Principal(companyId, branchA), activeRoute,
                new Dictionary<string, object?> { ["status"] = "Completed" }, db, audit, CancellationToken.None);
            Assert.Equal(StatusCodes.Status200OK, Status(complete));
            var terminalUpdate = await Invoke("UpdateRoute", Principal(companyId, branchA), activeRoute,
                new Dictionary<string, object?> { ["notes"] = "must not mutate" }, db, audit, CancellationToken.None);
            Assert.Equal(StatusCodes.Status409Conflict, Status(terminalUpdate));
            var terminalStopCreate = await Invoke("CreateRouteStop", Principal(companyId, branchA), activeRoute,
                new Dictionary<string, object?> { ["stopSequence"] = 60, ["address"] = "must not add" }, db, audit, CancellationToken.None);
            Assert.Equal(StatusCodes.Status409Conflict, Status(terminalStopCreate));
            var existingStopId = await db.ScalarLongAsync(
                "SELECT id FROM route_stops WHERE company_id=@c AND route_id=@r ORDER BY id LIMIT 1",
                c => { c.Parameters.AddWithValue("@c", companyId); c.Parameters.AddWithValue("@r", activeRoute); });
            var terminalStopUpdate = await Invoke("UpdateRouteStop", Principal(companyId, branchA), activeRoute, existingStopId,
                new Dictionary<string, object?> { ["notes"] = "must not edit" }, db, audit, CancellationToken.None);
            Assert.Equal(StatusCodes.Status409Conflict, Status(terminalStopUpdate));
            var terminalStopDelete = await Invoke("DeleteRouteStop", Principal(companyId, branchA), activeRoute, existingStopId,
                db, audit, CancellationToken.None);
            Assert.Equal(StatusCodes.Status409Conflict, Status(terminalStopDelete));
            var archived = await Invoke("ArchiveRoute", Principal(companyId, branchA), activeRoute, db, audit, CancellationToken.None);
            Assert.Equal(StatusCodes.Status200OK, Status(archived));
            Assert.Equal(0, await db.ScalarLongAsync("SELECT COUNT(*) FROM jobs WHERE id=@id AND route_id IS NOT NULL", c => c.Parameters.AddWithValue("@id", job)));
        }
        finally { await Cleanup(db, companyId); }
    }

    [Fact]
    public void RouteFrontendUsesServerPagingExportAndCompleteWriteWorkflows()
    {
        var page = ReadSource("frontend", "src", "pages", "RoutePlanningPage.tsx");
        var api = ReadSource("frontend", "src", "services", "routesApi.ts");
        Assert.Contains("routesApi.listPaged", ReadSource("frontend", "src", "hooks", "useBatch2.ts"), StringComparison.Ordinal);
        Assert.Contains("downloadServerExport(\"/api/routes/export\"", page, StringComparison.Ordinal);
        Assert.Contains("routesApi.assign", page, StringComparison.Ordinal);
        Assert.Contains("routesApi.updateStop", page, StringComparison.Ordinal);
        Assert.Contains("routesApi.deleteStop", page, StringComparison.Ordinal);
        Assert.Contains("optimizationAvailable", page, StringComparison.Ordinal);
        Assert.Contains("apiPaged", api, StringComparison.Ordinal);
        Assert.Equal("\"'=HYPERLINK(\"\"https://invalid\"\")\"", EndpointMappings.CsvCell("=HYPERLINK(\"https://invalid\")"));
        Assert.Equal("\"Pilot, Route\"", EndpointMappings.CsvCell("Pilot, Route"));
    }

    private static async Task<long> CreateRoute(Database db, AuditService audit, long company, long branch, string code)
    {
        var result = await Invoke("CreateRoute", Principal(company, branch), new Dictionary<string, object?>
        {
            ["routeCode"] = code, ["routeName"] = $"Pilot {code}", ["status"] = "Planned",
            ["plannedStart"] = "2026-08-02T10:00:00Z", ["plannedEnd"] = "2026-08-02T14:00:00Z",
            ["routeType"] = "Delivery", ["optimizationMode"] = "Balanced",
        }, db, audit, CancellationToken.None);
        Assert.Equal(StatusCodes.Status201Created, Status(result));
        return Convert.ToInt64((await db.QuerySingleAsync("SELECT id FROM routes WHERE company_id=@c AND route_code=@code",
            c => { c.Parameters.AddWithValue("@c", company); c.Parameters.AddWithValue("@code", code); }))!["id"]);
    }

    private static DefaultHttpContext Principal(long company, long branch)
    {
        var http = new DefaultHttpContext();
        http.Items[EndpointMappings.AuthCompanyIdItemKey] = company;
        http.Items[EndpointMappings.AuthBranchIdItemKey] = branch;
        http.Items[EndpointMappings.AuthUserIdItemKey] = 42L;
        http.Items[EndpointMappings.AuthRoleItemKey] = "Tenant Admin";
        http.Items[EndpointMappings.AuthPermissionsItemKey] = new[] { "dispatch:view", "dispatch:manage", "dispatch:assign", "dispatch:override" };
        return http;
    }

    private static async Task<IResult> Invoke(string method, params object[] args)
    {
        var target = typeof(EndpointMappings).GetMethod(method, BindingFlags.NonPublic | BindingFlags.Static)!;
        try { return await (Task<IResult>)target.Invoke(null, args)!; }
        catch (TargetInvocationException ex) when (ex.InnerException is not null)
        { System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(ex.InnerException).Throw(); throw; }
    }

    private static int? Status(IResult result) => Assert.IsAssignableFrom<IStatusCodeHttpResult>(result).StatusCode;
    private static string Json(IResult result) => JsonSerializer.Serialize(Assert.IsAssignableFrom<IValueHttpResult>(result).Value, new JsonSerializerOptions(JsonSerializerDefaults.Web));
    private static Database Db() => new(new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        { ["ConnectionStrings:DefaultConnection"] = TestDb.ConnectionString, ["Rls:EnforceTenantContext"] = "false" }).Build());
    private static async Task EnsureSchema(Database db)
    {
        await new Batch2SchemaService(db).EnsureAsync();
        await new DriverSchemaService(db, NullLogger<DriverSchemaService>.Instance).EnsureAsync();
        await new MaintenanceSchemaService(db).EnsureAsync();
        await new DispatchSchemaService(db, NullLogger<DispatchSchemaService>.Instance).EnsureAsync();
        await new FoundationSchemaService(db).EnsureAsync();
    }
    private static Task SeedCompany(Database db, long id) => db.ExecuteAsync(
        "INSERT INTO companies(id,company_code,name,industry) OVERRIDING SYSTEM VALUE VALUES (@id,@code,'Route Pilot Test','Transportation')",
        c => { c.Parameters.AddWithValue("@id", id); c.Parameters.AddWithValue("@code", $"RPT-{id}"); });
    private static async Task Cleanup(Database db, long company)
    {
        foreach (var sql in new[]
        {
            "DELETE FROM route_recommendations WHERE company_id=@c", "DELETE FROM route_paths WHERE company_id=@c",
            "DELETE FROM entity_timeline_events WHERE company_id=@c", "DELETE FROM audit_logs WHERE company_id=@c",
            "DELETE FROM route_stops WHERE company_id=@c", "UPDATE jobs SET route_id=NULL WHERE company_id=@c",
            "DELETE FROM routes WHERE company_id=@c", "DELETE FROM hos_records WHERE company_id=@c", "DELETE FROM jobs WHERE company_id=@c",
            "DELETE FROM vehicles WHERE company_id=@c", "DELETE FROM drivers WHERE company_id=@c", "DELETE FROM customers WHERE company_id=@c",
            "DELETE FROM companies WHERE id=@c",
        }) await db.ExecuteAsync(sql, c => c.Parameters.AddWithValue("@c", company));
    }
    private static string ReadSource(params string[] parts)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !Directory.Exists(Path.Combine(dir.FullName, "backend-dotnet"))) dir = dir.Parent;
        Assert.NotNull(dir);
        return File.ReadAllText(Path.Combine([dir!.FullName, .. parts]));
    }
}
