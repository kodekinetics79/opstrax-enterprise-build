using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Opstrax.Api.Controllers;
using Opstrax.Api.Data;
using Opstrax.Api.Foundation;
using Opstrax.Api.Services;
using System.Reflection;
using System.Text.Json;

namespace Opstrax.Tests;

[Trait("Category", "Integration")]
public sealed class CoreJobsBranchHosApiTests
{
    [Fact]
    public async Task TrackingReferencesAreTenantScopedAndPublicTokensAreGloballyUnique()
    {
        var db = Db();
        await EnsureJobRuntimeSchema(db);
        var companyA = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() + Random.Shared.Next(1_000_000, 1_900_000);
        var companyB = companyA + 1;
        await SeedCompany(db, companyA);
        await SeedCompany(db, companyB);
        try
        {
            const string tracking = "SHARED-CUSTOMER-REFERENCE";
            const string token = "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";
            var jobA = await db.InsertAsync(
                "INSERT INTO jobs(company_id,job_code,job_type,status,tracking_code) VALUES (@c,@code,'Delivery','Unassigned',@tracking)",
                c => { c.Parameters.AddWithValue("@c", companyA); c.Parameters.AddWithValue("@code", $"TA-{companyA}"); c.Parameters.AddWithValue("@tracking", tracking); });
            var jobB = await db.InsertAsync(
                "INSERT INTO jobs(company_id,job_code,job_type,status,tracking_code) VALUES (@c,@code,'Delivery','Unassigned',@tracking)",
                c => { c.Parameters.AddWithValue("@c", companyB); c.Parameters.AddWithValue("@code", $"TB-{companyB}"); c.Parameters.AddWithValue("@tracking", tracking); });

            await Assert.ThrowsAsync<Npgsql.PostgresException>(() => db.InsertAsync(
                "INSERT INTO jobs(company_id,job_code,job_type,status,tracking_code) VALUES (@c,@code,'Delivery','Unassigned',@tracking)",
                c => { c.Parameters.AddWithValue("@c", companyA); c.Parameters.AddWithValue("@code", $"TA2-{companyA}"); c.Parameters.AddWithValue("@tracking", tracking); }));

            await db.ExecuteAsync(
                "INSERT INTO customer_eta_links(company_id,job_id,tracking_code,secure_token,expires_at) VALUES (@c,@j,@tracking,@token,NOW()+INTERVAL '1 day')",
                c => { c.Parameters.AddWithValue("@c", companyA); c.Parameters.AddWithValue("@j", jobA); c.Parameters.AddWithValue("@tracking", tracking); c.Parameters.AddWithValue("@token", token); });
            await Assert.ThrowsAsync<Npgsql.PostgresException>(() => db.InsertAsync(
                "INSERT INTO customer_eta_links(company_id,job_id,tracking_code,secure_token,expires_at) VALUES (@c,@j,@tracking,@token,NOW()+INTERVAL '1 day')",
                c => { c.Parameters.AddWithValue("@c", companyB); c.Parameters.AddWithValue("@j", jobB); c.Parameters.AddWithValue("@tracking", "OTHER-REFERENCE"); c.Parameters.AddWithValue("@token", token); }));
        }
        finally
        {
            await Cleanup(db, companyA);
            await Cleanup(db, companyB);
        }
    }

    [Fact]
    public async Task CancelOnlyPermissionCannotReachAssignmentStatusEtaOrProofThroughSemanticAliases()
    {
        var db = Db();
        var http = Principal(999_999_991, 9201);
        http.Items[EndpointMappings.AuthPermissionsItemKey] = new[] { "dispatch:cancel" };
        var audit = new AuditService(db);
        foreach (var result in new[]
        {
            await Invoke("AssignJob", http, 1L, new Dictionary<string, object?> { ["driverId"] = 1, ["vehicleId"] = 1 }, db, audit, CancellationToken.None),
            await Invoke("ChangeJobStatus", http, 1L, new Dictionary<string, object?> { ["status"] = "En Route" }, db, audit, CancellationToken.None),
            await Invoke("SendEta", http, 1L, new Dictionary<string, object?>(), db, audit, CancellationToken.None),
            await Invoke("CreateProofPlaceholder", http, 1L, new Dictionary<string, object?>(), db, audit, CancellationToken.None),
            await Invoke("CaptureProof", http, 1L, new Dictionary<string, object?> { ["receivedBy"] = "Receiver" }, db, audit, CancellationToken.None),
        })
            Assert.Equal(StatusCodes.Status403Forbidden, Assert.IsAssignableFrom<IStatusCodeHttpResult>(result).StatusCode);
    }

    [Fact]
    public async Task RealisticCreateAssignAdvanceProofAndCancelWorkflowStaysConsistent()
    {
        var db = Db();
        await EnsureJobRuntimeSchema(db);
        var companyId = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() + Random.Shared.Next(100_000, 900_000);
        const long branchId = 9201;
        await SeedCompany(db, companyId);
        try
        {
            var customer = await db.InsertAsync(
                "INSERT INTO customers(company_id,customer_code,name,status) VALUES (@c,@code,'Wave 1 Customer','Active')",
                c => { c.Parameters.AddWithValue("@c", companyId); c.Parameters.AddWithValue("@code", $"CUS-{companyId}"); });
            var driver = await Driver(db, companyId, branchId, $"DRV-{companyId}", "Wave 1 Driver");
            var vehicle = await db.InsertAsync(
                "INSERT INTO vehicles(company_id,branch_id,vehicle_code,type,status,availability_status,out_of_service,readiness_score,risk_score) VALUES (@c,@b,@code,'Truck','Available','available',false,95,5)",
                c => { c.Parameters.AddWithValue("@c", companyId); c.Parameters.AddWithValue("@b", branchId); c.Parameters.AddWithValue("@code", $"VEH-{companyId}"); });
            await Hos(db, companyId, driver, "On Duty", 8m);
            var http = Principal(companyId, branchId);
            var audit = new AuditService(db);
            var code = $"JOB-{companyId}";

            var create = await Invoke("CreateJob", http, new Dictionary<string, object?>
            {
                ["jobNumber"] = code,
                ["customerId"] = customer.ToString(),
                ["pickupAddress"] = "Dock A",
                ["dropoffAddress"] = "Dock B",
                ["priority"] = "High",
                ["assignedDriverId"] = driver.ToString(),
                ["assignedVehicleId"] = vehicle.ToString(),
                ["status"] = "Assigned"
            }, db, audit, new NoopEvents(), CancellationToken.None);
            Assert.Equal(StatusCodes.Status201Created, Assert.IsAssignableFrom<IStatusCodeHttpResult>(create).StatusCode);
            var jobId = Convert.ToInt64((await db.QuerySingleAsync("SELECT id FROM jobs WHERE company_id=@c AND job_code=@code",
                c => { c.Parameters.AddWithValue("@c", companyId); c.Parameters.AddWithValue("@code", code); }))!["id"]);
            Assert.Equal(1, await db.ScalarLongAsync("SELECT COUNT(*) FROM dispatch_assignments WHERE company_id=@c AND job_id=@j AND assignment_status='assigned'",
                c => { c.Parameters.AddWithValue("@c", companyId); c.Parameters.AddWithValue("@j", jobId); }));

            var bypass = await Invoke("UpdateJob", http, jobId, new Dictionary<string, object?> { ["status"] = "Delivered" }, db, audit, new NoopEvents(), CancellationToken.None);
            Assert.Equal(StatusCodes.Status400BadRequest, Assert.IsAssignableFrom<IStatusCodeHttpResult>(bypass).StatusCode);
            var concurrentDbA = Db();
            var concurrentDbB = Db();
            var concurrentAdvance = await Task.WhenAll(
                Invoke("ChangeJobStatus", Principal(companyId, branchId), jobId, new Dictionary<string, object?> { ["status"] = "En Route" }, concurrentDbA, new AuditService(concurrentDbA), CancellationToken.None),
                Invoke("ChangeJobStatus", Principal(companyId, branchId), jobId, new Dictionary<string, object?> { ["status"] = "En Route" }, concurrentDbB, new AuditService(concurrentDbB), CancellationToken.None));
            Assert.Equal(1, concurrentAdvance.Count(result => Assert.IsAssignableFrom<IStatusCodeHttpResult>(result).StatusCode == StatusCodes.Status200OK));
            Assert.Equal(1, concurrentAdvance.Count(result => Assert.IsAssignableFrom<IStatusCodeHttpResult>(result).StatusCode == StatusCodes.Status409Conflict));
            foreach (var next in new[] { "At Stop", "Completed" })
            {
                var moved = await Invoke("ChangeJobStatus", http, jobId, new Dictionary<string, object?> { ["status"] = next }, db, audit, CancellationToken.None);
                Assert.Equal(StatusCodes.Status200OK, Assert.IsAssignableFrom<IStatusCodeHttpResult>(moved).StatusCode);
            }
            var invalidBackwards = await Invoke("ChangeJobStatus", http, jobId, new Dictionary<string, object?> { ["status"] = "Assigned" }, db, audit, CancellationToken.None);
            Assert.Equal(StatusCodes.Status409Conflict, Assert.IsAssignableFrom<IStatusCodeHttpResult>(invalidBackwards).StatusCode);

            var queued = await Invoke("CreateProofPlaceholder", http, jobId, new Dictionary<string, object?>(), db, audit, CancellationToken.None);
            Assert.Equal(StatusCodes.Status200OK, Assert.IsAssignableFrom<IStatusCodeHttpResult>(queued).StatusCode);
            var proof = await Invoke("CaptureProof", http, jobId,
                new Dictionary<string, object?> { ["receivedBy"] = "Receiving Lead", ["notes"] = "Seal intact" }, db, audit, CancellationToken.None);
            Assert.Equal(StatusCodes.Status200OK, Assert.IsAssignableFrom<IStatusCodeHttpResult>(proof).StatusCode);
            var delivered = await db.QuerySingleAsync("SELECT status,proof_status FROM jobs WHERE id=@j", c => c.Parameters.AddWithValue("@j", jobId));
            Assert.Equal("Delivered", delivered!["status"]);
            Assert.Equal("Captured", delivered["proofStatus"]);
            Assert.Equal(1, await db.ScalarLongAsync("SELECT COUNT(*) FROM dispatch_assignments WHERE company_id=@c AND job_id=@j AND assignment_status='delivered'",
                c => { c.Parameters.AddWithValue("@c", companyId); c.Parameters.AddWithValue("@j", jobId); }));
            Assert.Equal(1, await db.ScalarLongAsync("SELECT COUNT(*) FROM outbox_messages WHERE tenant_id=@c AND event_type='job.delivered' AND aggregate_id=@j",
                c => { c.Parameters.AddWithValue("@c", companyId); c.Parameters.AddWithValue("@j", jobId.ToString()); }));
            var duplicateProof = await Invoke("CaptureProof", http, jobId,
                new Dictionary<string, object?> { ["receivedBy"] = "Duplicate Receiver" }, db, audit, CancellationToken.None);
            Assert.Equal(StatusCodes.Status409Conflict, Assert.IsAssignableFrom<IStatusCodeHttpResult>(duplicateProof).StatusCode);
            Assert.Equal(1, await db.ScalarLongAsync("SELECT COUNT(*) FROM outbox_messages WHERE tenant_id=@c AND event_type='job.delivered' AND aggregate_id=@j",
                c => { c.Parameters.AddWithValue("@c", companyId); c.Parameters.AddWithValue("@j", jobId.ToString()); }));

            var cancelCode = $"CANCEL-{companyId}";
            var cancelJob = await db.InsertAsync(
                "INSERT INTO jobs(company_id,branch_id,customer_id,job_code,job_type,status,priority,tracking_code) VALUES (@c,@b,@customer,@code,'Delivery','Unassigned','Normal',@tracking)",
                c => { c.Parameters.AddWithValue("@c", companyId); c.Parameters.AddWithValue("@b", branchId); c.Parameters.AddWithValue("@customer", customer); c.Parameters.AddWithValue("@code", cancelCode); c.Parameters.AddWithValue("@tracking", $"TRK-{companyId}"); });
            var eta = await Invoke("SendEta", http, cancelJob, new Dictionary<string, object?>(), db, audit, CancellationToken.None);
            Assert.Equal(StatusCodes.Status200OK, Assert.IsAssignableFrom<IStatusCodeHttpResult>(eta).StatusCode);
            var cancelled = await Invoke("ChangeJobStatus", http, cancelJob, new Dictionary<string, object?> { ["status"] = "Cancelled" }, db, audit, CancellationToken.None);
            Assert.Equal(StatusCodes.Status200OK, Assert.IsAssignableFrom<IStatusCodeHttpResult>(cancelled).StatusCode);
            Assert.Equal("Cancelled", (await db.QuerySingleAsync("SELECT status FROM jobs WHERE id=@j", c => c.Parameters.AddWithValue("@j", cancelJob)))!["status"]);
            Assert.Equal(1, await db.ScalarLongAsync("SELECT COUNT(*) FROM customer_eta_links WHERE company_id=@c AND job_id=@j AND public_status='Revoked'",
                c => { c.Parameters.AddWithValue("@c", companyId); c.Parameters.AddWithValue("@j", cancelJob); }));
        }
        finally { await Cleanup(db, companyId); }
    }

    [Fact]
    public async Task BranchBoundJobApiRejectsCrossBranchReadsAndMutationsWithoutSideEffects()
    {
        var db = Db();
        await EnsureJobRuntimeSchema(db);
        var companyId = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() + Random.Shared.Next(1000, 9999);
        const long branchA = 9211;
        const long branchB = 9212;
        await SeedCompany(db, companyId);
        try
        {
            var jobId = await db.InsertAsync(
                "INSERT INTO jobs(company_id,branch_id,job_code,job_type,status,priority) VALUES (@c,@b,@code,'Delivery','Unassigned','Normal')",
                c => { c.Parameters.AddWithValue("@c", companyId); c.Parameters.AddWithValue("@b", branchB); c.Parameters.AddWithValue("@code", $"BR-{companyId}"); });
            await db.ExecuteAsync(
                "INSERT INTO entity_timeline_events(company_id,entity_type,entity_id,event_type,title,severity) VALUES (@c,'Job',@j,'seed','Private branch event','Info')",
                c => { c.Parameters.AddWithValue("@c", companyId); c.Parameters.AddWithValue("@j", jobId); });
            await db.ExecuteAsync(
                "INSERT INTO proof_of_delivery(company_id,job_id,receiver_name,received_by,proof_type,status) VALUES (@c,@j,'Private Receiver','Private Receiver','Digital Signature','Captured')",
                c => { c.Parameters.AddWithValue("@c", companyId); c.Parameters.AddWithValue("@j", jobId); });

            var http = Principal(companyId, branchA);
            var audit = new AuditService(db);
            var beforeTimeline = await Count(db, "entity_timeline_events", companyId, jobId);
            var beforeAudit = await Count(db, "audit_logs", companyId, jobId);

            await AssertNotFound("JobDetail", http, jobId, db, CancellationToken.None);
            await AssertNotFound("JobTimeline", http, jobId, db, CancellationToken.None);
            await AssertNotFound("JobRecommendations", http, jobId, db, CancellationToken.None);
            await AssertNotFound("GetJobProof", http, jobId, db, CancellationToken.None);
            await AssertNotFound("UpdateJob", http, jobId,
                new Dictionary<string, object?> { ["priority"] = "Critical" }, db, audit, new NoopEvents(), CancellationToken.None);
            await AssertNotFound("ChangeJobStatus", http, jobId,
                new Dictionary<string, object?> { ["status"] = "Delayed" }, db, audit, CancellationToken.None);
            await AssertNotFound("SendEta", http, jobId, new Dictionary<string, object?>(), db, audit, CancellationToken.None);
            await AssertNotFound("CreateProofPlaceholder", http, jobId,
                new Dictionary<string, object?> { ["receivedBy"] = "Wrong branch" }, db, audit, CancellationToken.None);
            await AssertNotFound("CaptureProof", http, jobId,
                new Dictionary<string, object?> { ["receivedBy"] = "Wrong branch" }, db, audit, CancellationToken.None);

            var podList = Assert.IsAssignableFrom<IValueHttpResult>(await Invoke("ProofOfDeliveryList", http, db, CancellationToken.None));
            var podPayload = JsonSerializer.Serialize(podList.Value, new JsonSerializerOptions(JsonSerializerDefaults.Web));
            Assert.DoesNotContain($"BR-{companyId}", podPayload, StringComparison.Ordinal);
            Assert.DoesNotContain("Private Receiver", podPayload, StringComparison.Ordinal);

            var noPermission = Principal(companyId, branchA);
            noPermission.Items[EndpointMappings.AuthPermissionsItemKey] = Array.Empty<string>();
            var forbiddenPod = await Invoke("ProofOfDeliveryList", noPermission, db, CancellationToken.None);
            Assert.Equal(StatusCodes.Status403Forbidden, Assert.IsAssignableFrom<IStatusCodeHttpResult>(forbiddenPod).StatusCode);

            await AssertNotFound("ArchiveJob", http, jobId, db, audit, CancellationToken.None);

            var row = await db.QuerySingleAsync("SELECT status,priority,deleted_at FROM jobs WHERE id=@id", c => c.Parameters.AddWithValue("@id", jobId));
            Assert.Equal("Unassigned", row!["status"]);
            Assert.Equal("Normal", row["priority"]);
            Assert.True(row["deletedAt"] is null or DBNull);
            Assert.Equal(1, await db.ScalarLongAsync("SELECT COUNT(*) FROM proof_of_delivery WHERE company_id=@c AND job_id=@j", c => { c.Parameters.AddWithValue("@c", companyId); c.Parameters.AddWithValue("@j", jobId); }));
            Assert.Equal(beforeTimeline, await Count(db, "entity_timeline_events", companyId, jobId));
            Assert.Equal(beforeAudit, await Count(db, "audit_logs", companyId, jobId));
        }
        finally { await Cleanup(db, companyId); }
    }

    [Fact]
    public async Task AvailableDriversExcludesOffDutyAndLegacyAssignmentRejectsIt()
    {
        var db = Db();
        await EnsureJobRuntimeSchema(db);
        var companyId = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() + Random.Shared.Next(10000, 99999);
        const long branchId = 9221;
        await SeedCompany(db, companyId);
        try
        {
            var eligible = await Driver(db, companyId, branchId, "ELIGIBLE", "Eligible Driver");
            var offDuty = await Driver(db, companyId, branchId, "OFF-DUTY", "Off Duty Driver");
            var vehicle = await db.InsertAsync(
                "INSERT INTO vehicles(company_id,branch_id,vehicle_code,type,status,availability_status,out_of_service) VALUES (@c,@b,'UNIT-9221','Truck','Available','available',false)",
                c => { c.Parameters.AddWithValue("@c", companyId); c.Parameters.AddWithValue("@b", branchId); });
            await Hos(db, companyId, eligible, "On Duty", 8m);
            await Hos(db, companyId, offDuty, "Off Duty", 8m);
            var jobId = await db.InsertAsync(
                "INSERT INTO jobs(company_id,branch_id,job_code,job_type,status) VALUES (@c,@b,@code,'Delivery','Unassigned')",
                c => { c.Parameters.AddWithValue("@c", companyId); c.Parameters.AddWithValue("@b", branchId); c.Parameters.AddWithValue("@code", $"HOS-{companyId}"); });

            var http = Principal(companyId, branchId);
            var result = Assert.IsAssignableFrom<IValueHttpResult>(await Invoke("AvailableDrivers", http, db, CancellationToken.None));
            var payload = JsonSerializer.Serialize(result.Value, new JsonSerializerOptions(JsonSerializerDefaults.Web));
            Assert.Contains("Eligible Driver", payload, StringComparison.Ordinal);
            Assert.DoesNotContain("Off Duty Driver", payload, StringComparison.Ordinal);

            var assign = await Invoke("AssignJob", http, jobId,
                new Dictionary<string, object?> { ["driverId"] = offDuty, ["vehicleId"] = vehicle },
                db, new AuditService(db), CancellationToken.None);
            Assert.Equal(StatusCodes.Status400BadRequest, Assert.IsAssignableFrom<IStatusCodeHttpResult>(assign).StatusCode);
            Assert.Equal("Unassigned", (await db.QuerySingleAsync("SELECT status FROM jobs WHERE id=@id", c => c.Parameters.AddWithValue("@id", jobId)))!["status"]);
            Assert.Equal(0, await db.ScalarLongAsync("SELECT COUNT(*) FROM dispatch_assignments WHERE company_id=@c AND job_id=@j", c => { c.Parameters.AddWithValue("@c", companyId); c.Parameters.AddWithValue("@j", jobId); }));
        }
        finally { await Cleanup(db, companyId); }
    }

    private static async Task AssertNotFound(string method, params object[] args)
    {
        var result = await Invoke(method, args);
        Assert.Equal(StatusCodes.Status404NotFound, Assert.IsAssignableFrom<IStatusCodeHttpResult>(result).StatusCode);
    }

    private static async Task<IResult> Invoke(string method, params object[] args)
    {
        var target = typeof(EndpointMappings).GetMethod(method, BindingFlags.NonPublic | BindingFlags.Static)!;
        try { return await (Task<IResult>)target.Invoke(null, args)!; }
        catch (TargetInvocationException ex) when (ex.InnerException is not null)
        { System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(ex.InnerException).Throw(); throw; }
    }

    private static Database Db() => new(new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
    { ["ConnectionStrings:DefaultConnection"] = TestDb.ConnectionString, ["Rls:EnforceTenantContext"] = "false" }).Build());

    private static async Task EnsureJobRuntimeSchema(Database db)
    {
        await new Batch2SchemaService(db).EnsureAsync();
        await new DriverSchemaService(db, NullLogger<DriverSchemaService>.Instance).EnsureAsync();
        await new MaintenanceSchemaService(db).EnsureAsync();
        await new DispatchSchemaService(db, NullLogger<DispatchSchemaService>.Instance).EnsureAsync();
        await new FoundationSchemaService(db).EnsureAsync();
    }

    private static DefaultHttpContext Principal(long companyId, long branchId)
    {
        var http = new DefaultHttpContext();
        http.Items[EndpointMappings.AuthCompanyIdItemKey] = companyId;
        http.Items[EndpointMappings.AuthBranchIdItemKey] = branchId;
        http.Items[EndpointMappings.AuthUserIdItemKey] = 42L;
        http.Items[EndpointMappings.AuthRoleItemKey] = "Tenant Admin";
        http.Items[EndpointMappings.AuthPermissionsItemKey] = new[] { "shipments:view", "job:update", "dispatch:view", "dispatch:manage", "dispatch:assign", "dispatch:override" };
        return http;
    }

    private static Task SeedCompany(Database db, long id) => db.ExecuteAsync(
        "INSERT INTO companies(id,company_code,name,industry) OVERRIDING SYSTEM VALUE VALUES (@id,@code,'Core API Branch Test','Transportation')",
        c => { c.Parameters.AddWithValue("@id", id); c.Parameters.AddWithValue("@code", $"CAB-{id}"); });
    private static Task<long> Driver(Database db, long company, long branch, string code, string name) => db.InsertAsync(
        "INSERT INTO drivers(company_id,branch_id,driver_code,full_name,status,safety_score,readiness_score,compliance_score) VALUES (@c,@b,@code,@name,'Available',95,95,95)",
        c => { c.Parameters.AddWithValue("@c", company); c.Parameters.AddWithValue("@b", branch); c.Parameters.AddWithValue("@code", code); c.Parameters.AddWithValue("@name", name); });
    private static Task Hos(Database db, long company, long driver, string status, decimal hours) => db.ExecuteAsync(
        "INSERT INTO hos_records(company_id,driver_id,shift_date,remaining_drive_hours,remaining_shift_hours,hos_status) VALUES (@c,@d,CURRENT_DATE,@h,@h,@s)",
        c => { c.Parameters.AddWithValue("@c", company); c.Parameters.AddWithValue("@d", driver); c.Parameters.AddWithValue("@h", hours); c.Parameters.AddWithValue("@s", status); });
    private static Task<long> Count(Database db, string table, long company, long job) => db.ScalarLongAsync(
        $"SELECT COUNT(*) FROM {table} WHERE company_id=@c AND entity_id=@j", c => { c.Parameters.AddWithValue("@c", company); c.Parameters.AddWithValue("@j", job); });

    private static async Task Cleanup(Database db, long company)
    {
        foreach (var sql in new[]
        {
            "DELETE FROM outbox_messages WHERE tenant_id=@c", "DELETE FROM dispatch_assignments WHERE company_id=@c", "DELETE FROM proof_of_delivery WHERE company_id=@c",
            "DELETE FROM customer_eta_links WHERE company_id=@c", "DELETE FROM eta_updates WHERE company_id=@c",
            "DELETE FROM job_status_events WHERE company_id=@c", "DELETE FROM entity_timeline_events WHERE company_id=@c",
            "DELETE FROM audit_logs WHERE company_id=@c", "DELETE FROM jobs WHERE company_id=@c",
            "DELETE FROM hos_records WHERE company_id=@c", "DELETE FROM vehicles WHERE company_id=@c",
            "DELETE FROM drivers WHERE company_id=@c", "DELETE FROM customers WHERE company_id=@c", "DELETE FROM companies WHERE id=@c"
        })
            await db.ExecuteAsync(sql, c => c.Parameters.AddWithValue("@c", company));
    }

    private sealed class NoopEvents : IDomainEventPublisher
    {
        public DomainEventRecord Publish(string tenantId, string eventType, string aggregateType, string aggregateId,
            string payloadJson, string? correlationId = null, string? causationId = null, string? idempotencyKey = null) => null!;
    }
}
