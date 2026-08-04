using System.Reflection;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Opstrax.Api.Controllers;
using Opstrax.Api.Data;
using Opstrax.Api.Foundation;
using Opstrax.Api.Services;

namespace Opstrax.Tests;

[Trait("Category", "Integration")]
public sealed class CrossModuleFleetPilotPostgresTests
{
    [Fact]
    public async Task JobDispatchRouteTripLastMileAndProof_StaySameEntityTenantSafeAndExactlyOnce()
    {
        var db = Db();
        var suffix = Guid.NewGuid().ToString("N")[..10];
        var company = await db.InsertAsync(
            "INSERT INTO companies(company_code,name,industry) VALUES (@code,'Cross-module pilot','Transportation')",
            c => c.Parameters.AddWithValue("@code", $"XMOD-{suffix}"));
        var attackerCompany = await db.InsertAsync(
            "INSERT INTO companies(company_code,name,industry) VALUES (@code,'Cross-module attacker','Transportation')",
            c => c.Parameters.AddWithValue("@code", $"XATT-{suffix}"));
        var branch = await db.InsertAsync(
            "INSERT INTO branches(company_id,branch_code,name,status) VALUES (@c,'MAIN','Main','Active')",
            c => c.Parameters.AddWithValue("@c", company));
        var otherBranch = await db.InsertAsync(
            "INSERT INTO branches(company_id,branch_code,name,status) VALUES (@c,'OTHER','Other','Active')",
            c => c.Parameters.AddWithValue("@c", company));
        var attackerBranch = await db.InsertAsync(
            "INSERT INTO branches(company_id,branch_code,name,status) VALUES (@c,'MAIN','Main','Active')",
            c => c.Parameters.AddWithValue("@c", attackerCompany));

        try
        {
            var user = await db.InsertAsync(
                "INSERT INTO users(company_id,branch_id,full_name,email,role_name,status) VALUES (@c,@b,'Pilot Driver',@email,'Driver','Active')",
                c => { c.Parameters.AddWithValue("@c", company); c.Parameters.AddWithValue("@b", branch); c.Parameters.AddWithValue("@email", $"{suffix}@example.invalid"); });
            var driver = await db.InsertAsync(
                "INSERT INTO drivers(company_id,branch_id,user_id,driver_code,full_name,status,safety_score,readiness_score,compliance_score) VALUES (@c,@b,@u,@code,'Pilot Driver','Available',95,95,95)",
                c => { c.Parameters.AddWithValue("@c", company); c.Parameters.AddWithValue("@b", branch); c.Parameters.AddWithValue("@u", user); c.Parameters.AddWithValue("@code", $"DRV-{suffix}"); });
            var vehicle = await db.InsertAsync(
                "INSERT INTO vehicles(company_id,branch_id,vehicle_code,type,status,availability_status,out_of_service,readiness_score,risk_score) VALUES (@c,@b,@code,'Truck','Available','available',false,95,5)",
                c => { c.Parameters.AddWithValue("@c", company); c.Parameters.AddWithValue("@b", branch); c.Parameters.AddWithValue("@code", $"VEH-{suffix}"); });
            await db.ExecuteAsync(
                "INSERT INTO hos_records(company_id,driver_id,shift_date,remaining_drive_hours,remaining_shift_hours,hos_status) VALUES (@c,@d,CURRENT_DATE,8,8,'On Duty')",
                c => { c.Parameters.AddWithValue("@c", company); c.Parameters.AddWithValue("@d", driver); });
            var customer = await db.InsertAsync(
                "INSERT INTO customers(company_id,customer_code,name,status) VALUES (@c,@code,'Original Pilot Customer','Active')",
                c => { c.Parameters.AddWithValue("@c", company); c.Parameters.AddWithValue("@code", $"CUS-{suffix}"); });

            var orderNumber = $"ORD-{suffix}";
            var jobCode = $"FTMS-{orderNumber}";
            var http = Principal(company, branch);
            var audit = new AuditService(db);

            AssertStatus(await Core("CreateJob", http, new Dictionary<string, object?>
            {
                ["jobNumber"] = jobCode,
                ["customerId"] = customer,
                ["pickupAddress"] = "Origin dock",
                ["dropoffAddress"] = "Customer dock",
                ["status"] = "Unassigned",
            }, db, audit, new NoopEvents(), CancellationToken.None), StatusCodes.Status201Created);
            var job = await db.ScalarLongAsync("SELECT id FROM jobs WHERE company_id=@c AND job_code=@code",
                c => { c.Parameters.AddWithValue("@c", company); c.Parameters.AddWithValue("@code", jobCode); });

            AssertStatus(await Core("AssignJob", http, job,
                new Dictionary<string, object?> { ["driverId"] = driver, ["vehicleId"] = vehicle },
                db, audit, CancellationToken.None), StatusCodes.Status200OK);

            var routeCode = $"CORE-{suffix}";
            AssertStatus(await Core("CreateRoute", http, new Dictionary<string, object?>
            {
                ["routeCode"] = routeCode, ["routeName"] = "Pilot route", ["status"] = "Planned",
                ["plannedStart"] = DateTimeOffset.UtcNow.AddMinutes(-30).ToString("O"),
                ["plannedEnd"] = DateTimeOffset.UtcNow.AddHours(3).ToString("O"),
                ["routeType"] = "Delivery", ["optimizationMode"] = "Balanced",
            }, db, audit, CancellationToken.None), StatusCodes.Status201Created);
            var route = await db.ScalarLongAsync("SELECT id FROM routes WHERE company_id=@c AND route_code=@code",
                c => { c.Parameters.AddWithValue("@c", company); c.Parameters.AddWithValue("@code", routeCode); });
            AssertStatus(await Core("CreateRouteStop", http, route, new Dictionary<string, object?>
            {
                ["stopSequence"] = 1, ["stopType"] = "Delivery", ["address"] = "Customer dock",
                ["jobId"] = job, ["customerId"] = customer,
            }, db, audit, CancellationToken.None), StatusCodes.Status201Created);
            AssertStatus(await Core("AssignRoute", http, route,
                new Dictionary<string, object?> { ["driverId"] = driver, ["vehicleId"] = vehicle },
                db, audit, CancellationToken.None), StatusCodes.Status200OK);

            var trip = await db.InsertAsync(
                "INSERT INTO trips(company_id,vehicle_id,driver_id,route_id,status,trip_ref,planned_start_time) VALUES (@c,@v,@d,@r,'planned',@ref,NOW()-INTERVAL '20 minutes')",
                c => { c.Parameters.AddWithValue("@c", company); c.Parameters.AddWithValue("@v", vehicle); c.Parameters.AddWithValue("@d", driver); c.Parameters.AddWithValue("@r", route); c.Parameters.AddWithValue("@ref", $"TRP-{suffix}"); });
            await db.ExecuteAsync("UPDATE dispatch_assignments SET trip_id=@t,route_id=@r WHERE company_id=@c AND job_id=@j",
                c => { c.Parameters.AddWithValue("@t", trip); c.Parameters.AddWithValue("@r", route); c.Parameters.AddWithValue("@c", company); c.Parameters.AddWithValue("@j", job); });

            AssertStatus(await Core("TripStart", trip, Principal(company, otherBranch), db, audit, CancellationToken.None), StatusCodes.Status404NotFound);
            AssertStatus(await Core("TripStart", trip, Principal(attackerCompany, attackerBranch), db, audit, CancellationToken.None), StatusCodes.Status404NotFound);
            AssertStatus(await Core("TripStart", trip, http, db, audit, CancellationToken.None), StatusCodes.Status200OK);
            AssertStatus(await Core("TripComplete", trip, http, db, audit, CancellationToken.None), StatusCodes.Status200OK);

            var lastMileRouteCode = $"LM-{suffix}";
            AssertStatus(await FleetCreate("CreateRoute", http,
                new LogisticsRouteRequest(lastMileRouteCode, "Hub", "Territory", "Pilot Driver", $"VEH-{suffix}", "Planned", 1, 0, 12, 0, null, null, DateTime.UtcNow.Date, DateTime.UtcNow, null, null), db),
                StatusCodes.Status200OK);
            AssertStatus(await FleetCreate("CreateOrder", http,
                new LogisticsOrderRequest(orderNumber, "Delivery Recipient", "Retail", "Portal", "City", "Area", "Queued", "High", 1, 275m, lastMileRouteCode, null, null, null, DateTime.UtcNow.AddHours(2)), db),
                StatusCodes.Status200OK);
            var order = await db.ScalarLongAsync("SELECT id FROM fleet_tms_dispatch_orders WHERE company_id=@c AND order_number=@n",
                c => { c.Parameters.AddWithValue("@c", company); c.Parameters.AddWithValue("@n", orderNumber); });
            AssertStatus(await Fleet("DispatchOrder", http, order, new DispatchOrderRequest(lastMileRouteCode, null, null, "Pilot dispatch"), db), StatusCodes.Status200OK);
            var stop = await db.ScalarLongAsync("SELECT id FROM fleet_tms_last_mile_stops WHERE company_id=@c AND order_number=@n",
                c => { c.Parameters.AddWithValue("@c", company); c.Parameters.AddWithValue("@n", orderNumber); });

            var delivery = new ConfirmDeliveryRequest("Receiving Lead", "Captured", null, "signed-pod", $"deliver-{suffix}");
            AssertStatus(await Fleet("ConfirmDelivery", Principal(company, otherBranch), stop, delivery, Db()), StatusCodes.Status404NotFound);
            AssertStatus(await Fleet("ConfirmDelivery", Principal(attackerCompany, attackerBranch), stop, delivery, Db()), StatusCodes.Status404NotFound);
            var deliveryResults = await Task.WhenAll(
                Fleet("ConfirmDelivery", http, stop, delivery, Db()),
                Fleet("ConfirmDelivery", http, stop, delivery, Db()));
            Assert.All(deliveryResults, result => AssertStatus(result, StatusCodes.Status200OK));

            Assert.Equal(job, await db.ScalarLongAsync("SELECT id FROM jobs WHERE company_id=@c AND job_code=@code",
                c => { c.Parameters.AddWithValue("@c", company); c.Parameters.AddWithValue("@code", jobCode); }));
            Assert.Equal(customer, await db.ScalarLongAsync("SELECT customer_id FROM jobs WHERE id=@j",
                c => c.Parameters.AddWithValue("@j", job)));
            Assert.Equal(1, await db.ScalarLongAsync("SELECT COUNT(*) FROM job_charges WHERE company_id=@c AND job_id=@j AND charge_code='LASTMILE' AND amount=275",
                c => { c.Parameters.AddWithValue("@c", company); c.Parameters.AddWithValue("@j", job); }));
            Assert.Equal(1, await db.ScalarLongAsync("SELECT COUNT(*) FROM outbox_messages WHERE tenant_id=@c AND event_type='job.delivered' AND aggregate_id=@j::text",
                c => { c.Parameters.AddWithValue("@c", company); c.Parameters.AddWithValue("@j", job); }));

            var document = await db.InsertAsync(
                "INSERT INTO documents(company_id,title,document_type,status,file_url) VALUES (@c,'Delivery photo','proof','Active',@url)",
                c => { c.Parameters.AddWithValue("@c", company); c.Parameters.AddWithValue("@url", $"objkey:tenant/{company}/proof/{suffix}.jpg"); });
            var ambient = new AmbientCorrelationContext();
            var proofService = new Stage9OperationalFoundationService(db,
                new PostgresAiFoundationService(db, ambient), new PostgresApprovalWorkflowService(db, ambient),
                new PostgresDomainEventPublisher(db, ambient), new InMemoryIdempotencyService(), ambient);
            long proofId;
            using (AmbientCorrelationContext.Begin($"xmod-{suffix}", null, null, company.ToString(), ActorTypes.TenantUser, user.ToString()))
            {
                var proofs = await Task.WhenAll(
                    proofService.CreateProofPackageAsync(company, job, trip,
                        new() { ["proofType"] = "proof_of_delivery", ["receiverName"] = "Receiving Lead" }, $"proof-{suffix}"),
                    proofService.CreateProofPackageAsync(company, job, trip,
                        new() { ["proofType"] = "proof_of_delivery", ["receiverName"] = "Receiving Lead" }, $"proof-{suffix}"));
                Assert.All(proofs, Assert.NotNull);
                Assert.Equal(proofs[0]!["id"], proofs[1]!["id"]);
                proofId = Convert.ToInt64(proofs[0]!["id"]);
                Assert.Null(await proofService.GetProofPackageAsync(attackerCompany, proofId));
                Assert.NotNull(await proofService.CreateProofArtifactAsync(company, proofId,
                    new() { ["artifactType"] = "photo", ["fileId"] = document }, $"artifact-{suffix}"));
                Assert.True((await proofService.SubmitProofPackageAsync(company, proofId, new())).Success);
                var validations = await Task.WhenAll(
                    proofService.ValidateProofPackageAsync(company, proofId, new()),
                    proofService.ValidateProofPackageAsync(company, proofId, new()));
                Assert.Single(validations, result => result.Success);
                Assert.Single(validations, result => !result.Success);
            }
            Assert.Equal(1, await db.ScalarLongAsync("SELECT COUNT(*) FROM billing_confidence_records WHERE company_id=@c AND proof_package_id=@p",
                c => { c.Parameters.AddWithValue("@c", company); c.Parameters.AddWithValue("@p", proofId); }));
        }
        finally
        {
            await Cleanup(db, company);
            await Cleanup(db, attackerCompany);
        }
    }

    private static async Task<IResult> Core(string name, params object[] args)
    {
        var method = typeof(EndpointMappings).GetMethod(name, BindingFlags.NonPublic | BindingFlags.Static)!;
        try { return await (Task<IResult>)method.Invoke(null, args)!; }
        catch (TargetInvocationException ex) when (ex.InnerException is not null)
        { System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(ex.InnerException).Throw(); throw; }
    }

    private static async Task<IResult> Fleet(string name, HttpContext http, long id, object request, Database db)
    {
        var method = typeof(FleetTmsLogisticsEndpoints).GetMethod(name, BindingFlags.NonPublic | BindingFlags.Static)!;
        try { return await (Task<IResult>)method.Invoke(null, new[] { http, (object)id, request, db, CancellationToken.None })!; }
        catch (TargetInvocationException ex) when (ex.InnerException is not null)
        { System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(ex.InnerException).Throw(); throw; }
    }

    private static async Task<IResult> FleetCreate(string name, HttpContext http, object request, Database db)
    {
        var method = typeof(FleetTmsLogisticsEndpoints).GetMethod(name, BindingFlags.NonPublic | BindingFlags.Static)!;
        try { return await (Task<IResult>)method.Invoke(null, new[] { http, request, db, CancellationToken.None })!; }
        catch (TargetInvocationException ex) when (ex.InnerException is not null)
        { System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(ex.InnerException).Throw(); throw; }
    }

    private static void AssertStatus(IResult result, int expected) =>
        Assert.Equal(expected, Assert.IsAssignableFrom<IStatusCodeHttpResult>(result).StatusCode);

    private static Database Db() => new(new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
    { ["ConnectionStrings:DefaultConnection"] = TestDb.ConnectionString, ["Rls:EnforceTenantContext"] = "false" }).Build());

    private static DefaultHttpContext Principal(long company, long branch)
    {
        var http = new DefaultHttpContext();
        http.Items[EndpointMappings.AuthCompanyIdItemKey] = company;
        http.Items[EndpointMappings.AuthBranchIdItemKey] = branch;
        http.Items[EndpointMappings.AuthUserIdItemKey] = 42L;
        http.Items[EndpointMappings.AuthRoleItemKey] = "Cross-module pilot tester";
        http.Items[EndpointMappings.AuthPermissionsItemKey] = new[]
        {
            "shipments:view", "shipments:create", "shipments:update", "job:create", "job:update",
            "dispatch:view", "dispatch:create", "dispatch:update", "dispatch:manage", "dispatch:assign",
            "dispatch:override", "dispatch:cancel", "fleet.pod.manage",
        };
        return http;
    }

    private static async Task Cleanup(Database db, long company)
    {
        foreach (var sql in new[]
        {
            "DELETE FROM ai_action_outcomes WHERE tenant_id=@c", "DELETE FROM ai_action_requests WHERE tenant_id=@c",
            "DELETE FROM ai_recommendation_impacts WHERE tenant_id=@c", "DELETE FROM ai_recommendation_reasons WHERE tenant_id=@c",
            "DELETE FROM ai_recommendations WHERE tenant_id=@c", "DELETE FROM ai_reasoning_runs WHERE tenant_id=@c",
            "DELETE FROM approval_decisions WHERE tenant_id=@c", "DELETE FROM approval_requests WHERE tenant_id=@c",
            "DELETE FROM domain_events WHERE tenant_id=@c", "DELETE FROM outbox_messages WHERE tenant_id=@c",
            "DELETE FROM idempotency_keys WHERE tenant_id=@c", "DELETE FROM billing_confidence_records WHERE company_id=@c",
            "DELETE FROM proof_artifacts WHERE company_id=@c", "DELETE FROM proof_packages WHERE company_id=@c",
            "DELETE FROM documents WHERE company_id=@c",
            "DELETE FROM job_charges WHERE company_id=@c", "DELETE FROM dispatch_proof_artifacts WHERE company_id=@c",
            "DELETE FROM dispatch_proofs WHERE company_id=@c", "DELETE FROM dispatch_exceptions WHERE company_id=@c",
            "DELETE FROM audit_logs WHERE company_id=@c", "DELETE FROM entity_timeline_events WHERE company_id=@c",
            "DELETE FROM job_status_events WHERE company_id=@c", "DELETE FROM trip_stops WHERE company_id=@c",
            "DELETE FROM dispatch_assignments WHERE company_id=@c", "DELETE FROM trips WHERE company_id=@c",
            "DELETE FROM route_stops WHERE company_id=@c", "UPDATE jobs SET route_id=NULL WHERE company_id=@c",
            "DELETE FROM routes WHERE company_id=@c", "DELETE FROM fleet_tms_last_mile_stops WHERE company_id=@c",
            "DELETE FROM fleet_tms_delivery_routes WHERE company_id=@c", "DELETE FROM fleet_tms_dispatch_orders WHERE company_id=@c",
            "DELETE FROM hos_records WHERE company_id=@c", "DELETE FROM jobs WHERE company_id=@c",
            "DELETE FROM vehicles WHERE company_id=@c", "DELETE FROM drivers WHERE company_id=@c",
            "DELETE FROM users WHERE company_id=@c", "DELETE FROM customers WHERE company_id=@c",
            "DELETE FROM branches WHERE company_id=@c", "DELETE FROM companies WHERE id=@c",
        }) await db.ExecuteAsync(sql, c => c.Parameters.AddWithValue("@c", company));
    }

    private sealed class NoopEvents : IDomainEventPublisher
    {
        public DomainEventRecord Publish(string tenantId, string eventType, string aggregateType, string aggregateId,
            string payloadJson, string? correlationId = null, string? causationId = null, string? idempotencyKey = null) => null!;
    }
}
