using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Opstrax.Api.Controllers;
using Opstrax.Api.Data;
using Opstrax.Api.Foundation;
using Opstrax.Api.Services;
using Opstrax.Api.Storage;
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
        var objectStoreRoot = Path.Combine(Path.GetTempPath(), $"opstrax-core-job-pod-{Guid.NewGuid():N}");
        Directory.CreateDirectory(objectStoreRoot);
        await SeedCompany(db, companyId);
        try
        {
            var customer = await db.InsertAsync(
                "INSERT INTO customers(company_id,customer_code,name,status) VALUES (@c,@code,'Wave 1 Customer','Active')",
                c => { c.Parameters.AddWithValue("@c", companyId); c.Parameters.AddWithValue("@code", $"CUS-{companyId}"); });
            var driver = await Driver(db, companyId, branchId, $"DRV-{companyId}", "Wave 1 Driver");
            var vehicle = await db.InsertAsync(
                "INSERT INTO vehicles(company_id,branch_id,vehicle_code,type,vin_exception_type,alternate_identifier,status,availability_status,out_of_service,readiness_score,risk_score) VALUES (@c,@b,@code,'Truck','legacy-fleet-identifier',@code,'Available','available',false,95,5)",
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

            var evidenceBytes = new byte[] { 0x89, 0x50, 0x4e, 0x47, 0x0d, 0x0a, 0x1a, 0x0a };
            var evidenceStream = new MemoryStream(evidenceBytes);
            var evidenceFile = new FormFile(evidenceStream, 0, evidenceBytes.Length, "file", "delivery.png")
            {
                Headers = new HeaderDictionary(),
                ContentType = "image/png"
            };
            http.Request.ContentType = "multipart/form-data; boundary=opstrax-test-boundary";
            http.Request.Form = new FormCollection(
                new Dictionary<string, Microsoft.Extensions.Primitives.StringValues>
                {
                    ["kind"] = "photo"
                },
                new FormFileCollection { evidenceFile });
            var storage = new FileStorageService(new LocalObjectStore(objectStoreRoot), NullLogger<FileStorageService>.Instance);
            var uploaded = await Invoke("UploadJobProofEvidence", http, jobId, storage, db, audit, CancellationToken.None);
            Assert.Equal(StatusCodes.Status201Created, Assert.IsAssignableFrom<IStatusCodeHttpResult>(uploaded).StatusCode);
            var evidence = await db.QuerySingleAsync(
                "SELECT id,file_url FROM documents WHERE company_id=@c AND entity_type='Job' AND entity_id=@j ORDER BY id DESC LIMIT 1",
                c => { c.Parameters.AddWithValue("@c", companyId); c.Parameters.AddWithValue("@j", jobId); });
            Assert.NotNull(evidence);
            Assert.StartsWith($"objkey:tenant/{companyId}/proof/", evidence!["fileUrl"]?.ToString(), StringComparison.Ordinal);
            var photoFileId = Convert.ToInt64(evidence["id"]);

            var proof = await Invoke("CaptureProof", http, jobId,
                new Dictionary<string, object?>
                {
                    ["receivedBy"] = "Receiving Lead",
                    ["notes"] = "Seal intact",
                    ["photoFileId"] = photoFileId,
                    ["idempotencyKey"] = $"pod-{jobId}"
                }, db, audit, CancellationToken.None);
            Assert.Equal(StatusCodes.Status200OK, Assert.IsAssignableFrom<IStatusCodeHttpResult>(proof).StatusCode);
            var submitted = await db.QuerySingleAsync(
                "SELECT id,status FROM proof_of_delivery WHERE company_id=@c AND job_id=@j ORDER BY id DESC LIMIT 1",
                c => { c.Parameters.AddWithValue("@c", companyId); c.Parameters.AddWithValue("@j", jobId); });
            Assert.Equal("Submitted", submitted!["status"]);
            var awaitingVerification = await db.QuerySingleAsync("SELECT status,proof_status FROM jobs WHERE id=@j", c => c.Parameters.AddWithValue("@j", jobId));
            Assert.Equal("Completed", awaitingVerification!["status"]);
            Assert.Equal("Submitted", awaitingVerification["proofStatus"]);
            Assert.Equal(0, await db.ScalarLongAsync("SELECT COUNT(*) FROM outbox_messages WHERE tenant_id=@c AND event_type='job.delivered' AND aggregate_id=@j",
                c => { c.Parameters.AddWithValue("@c", companyId); c.Parameters.AddWithValue("@j", jobId.ToString()); }));

            var reviewer = Principal(companyId, branchId);
            reviewer.Items[EndpointMappings.AuthUserIdItemKey] = 84L;
            var verified = await Invoke("VerifyProofOfDelivery", reviewer, Convert.ToInt64(submitted["id"]), db, audit, CancellationToken.None);
            Assert.Equal(StatusCodes.Status200OK, Assert.IsAssignableFrom<IStatusCodeHttpResult>(verified).StatusCode);
            var delivered = await db.QuerySingleAsync("SELECT status,proof_status FROM jobs WHERE id=@j", c => c.Parameters.AddWithValue("@j", jobId));
            Assert.Equal("Delivered", delivered!["status"]);
            Assert.Equal("Captured", delivered["proofStatus"]);
            Assert.Equal(1, await db.ScalarLongAsync("SELECT COUNT(*) FROM dispatch_assignments WHERE company_id=@c AND job_id=@j AND assignment_status='delivered'",
                c => { c.Parameters.AddWithValue("@c", companyId); c.Parameters.AddWithValue("@j", jobId); }));
            Assert.Equal(1, await db.ScalarLongAsync("SELECT COUNT(*) FROM outbox_messages WHERE tenant_id=@c AND event_type='job.delivered' AND aggregate_id=@j",
                c => { c.Parameters.AddWithValue("@c", companyId); c.Parameters.AddWithValue("@j", jobId.ToString()); }));
            var duplicateProof = await Invoke("CaptureProof", http, jobId,
                new Dictionary<string, object?> { ["receivedBy"] = "Duplicate Receiver", ["photoFileId"] = photoFileId }, db, audit, CancellationToken.None);
            Assert.Equal(StatusCodes.Status409Conflict, Assert.IsAssignableFrom<IStatusCodeHttpResult>(duplicateProof).StatusCode);
            Assert.Equal(1, await db.ScalarLongAsync("SELECT COUNT(*) FROM outbox_messages WHERE tenant_id=@c AND event_type='job.delivered' AND aggregate_id=@j",
                c => { c.Parameters.AddWithValue("@c", companyId); c.Parameters.AddWithValue("@j", jobId.ToString()); }));

            var cancelCode = $"CANCEL-{companyId}";
            var cancelJob = await db.InsertAsync(
                "INSERT INTO jobs(company_id,branch_id,customer_id,job_code,job_type,status,priority,tracking_code) VALUES (@c,@b,@customer,@code,'Delivery','Unassigned','Normal',@tracking)",
                c => { c.Parameters.AddWithValue("@c", companyId); c.Parameters.AddWithValue("@b", branchId); c.Parameters.AddWithValue("@customer", customer); c.Parameters.AddWithValue("@code", cancelCode); c.Parameters.AddWithValue("@tracking", $"TRK-{companyId}"); });
            var eta = await Invoke("SendEta", http, cancelJob,
                new Dictionary<string, object?> { ["eta"] = DateTimeOffset.UtcNow.AddHours(3).ToString("O") }, db, audit, CancellationToken.None);
            Assert.Equal(StatusCodes.Status200OK, Assert.IsAssignableFrom<IStatusCodeHttpResult>(eta).StatusCode);
            var cancelled = await Invoke("ChangeJobStatus", http, cancelJob, new Dictionary<string, object?> { ["status"] = "Cancelled" }, db, audit, CancellationToken.None);
            Assert.Equal(StatusCodes.Status200OK, Assert.IsAssignableFrom<IStatusCodeHttpResult>(cancelled).StatusCode);
            Assert.Equal("Cancelled", (await db.QuerySingleAsync("SELECT status FROM jobs WHERE id=@j", c => c.Parameters.AddWithValue("@j", cancelJob)))!["status"]);
            Assert.Equal(1, await db.ScalarLongAsync("SELECT COUNT(*) FROM customer_eta_links WHERE company_id=@c AND job_id=@j AND public_status='Revoked'",
                c => { c.Parameters.AddWithValue("@c", companyId); c.Parameters.AddWithValue("@j", cancelJob); }));
        }
        finally
        {
            await Cleanup(db, companyId);
            if (Directory.Exists(objectStoreRoot)) Directory.Delete(objectStoreRoot, recursive: true);
        }
    }

    [Fact]
    public async Task EtaNotificationsUseStoredEtaAndAreDurablyIdempotent()
    {
        var db = Db();
        await EnsureJobRuntimeSchema(db);
        var companyId = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() + Random.Shared.Next(900_001, 999_999);
        const long branchId = 9205;
        await SeedCompany(db, companyId);
        try
        {
            var customerId = await db.InsertAsync(
                "INSERT INTO customers(company_id,customer_code,name,status) VALUES (@c,@code,'ETA Customer','Active')",
                c => { c.Parameters.AddWithValue("@c", companyId); c.Parameters.AddWithValue("@code", $"ETA-CUS-{companyId}"); });
            var storedEta = DateTimeOffset.UtcNow.AddHours(5).ToUniversalTime();
            var jobId = await db.InsertAsync(
                @"INSERT INTO jobs(company_id,branch_id,customer_id,job_code,job_type,status,priority,eta,tracking_code)
                  VALUES (@c,@b,@customer,@code,'Delivery','Unassigned','Normal',@eta,@tracking)",
                c =>
                {
                    c.Parameters.AddWithValue("@c", companyId);
                    c.Parameters.AddWithValue("@b", branchId);
                    c.Parameters.AddWithValue("@customer", customerId);
                    c.Parameters.AddWithValue("@code", $"ETA-{companyId}");
                    c.Parameters.AddWithValue("@eta", storedEta);
                    c.Parameters.AddWithValue("@tracking", $"ETA-TRK-{companyId}");
                });
            var http = Principal(companyId, branchId);
            http.Request.Headers["Idempotency-Key"] = $"eta-notification-{jobId}";
            var body = new Dictionary<string, object?>
            {
                ["channel"] = "SMS",
                ["confidenceLevel"] = "High",
                ["message"] = "Driver is on schedule."
            };
            var audit = new AuditService(db);

            var first = await Invoke("SendEta", http, jobId, body, db, audit, CancellationToken.None);
            var replay = await Invoke("SendEta", http, jobId, body, db, audit, CancellationToken.None);
            Assert.Equal(StatusCodes.Status200OK, Assert.IsAssignableFrom<IStatusCodeHttpResult>(first).StatusCode);
            Assert.Equal(StatusCodes.Status200OK, Assert.IsAssignableFrom<IStatusCodeHttpResult>(replay).StatusCode);
            Assert.Equal(1, await db.ScalarLongAsync("SELECT COUNT(*) FROM eta_updates WHERE company_id=@c AND job_id=@j",
                c => { c.Parameters.AddWithValue("@c", companyId); c.Parameters.AddWithValue("@j", jobId); }));
            Assert.Equal(1, await db.ScalarLongAsync("SELECT COUNT(*) FROM customer_communications WHERE company_id=@c AND job_id=@j",
                c => { c.Parameters.AddWithValue("@c", companyId); c.Parameters.AddWithValue("@j", jobId); }));
            Assert.Equal(1, await db.ScalarLongAsync("SELECT COUNT(*) FROM customer_eta_links WHERE company_id=@c AND job_id=@j",
                c => { c.Parameters.AddWithValue("@c", companyId); c.Parameters.AddWithValue("@j", jobId); }));
            var persisted = await db.QuerySingleAsync("SELECT eta,channel,message,status FROM eta_updates WHERE company_id=@c AND job_id=@j",
                c => { c.Parameters.AddWithValue("@c", companyId); c.Parameters.AddWithValue("@j", jobId); });
            Assert.Equal("SMS", persisted!["channel"]);
            Assert.Equal("Driver is on schedule.", persisted["message"]);
            Assert.Equal("Queued", persisted["status"]);
            Assert.Equal("Queued", (await db.QuerySingleAsync("SELECT status FROM customer_communications WHERE company_id=@c AND job_id=@j",
                c => { c.Parameters.AddWithValue("@c", companyId); c.Parameters.AddWithValue("@j", jobId); }))!["status"]);
            Assert.InRange(Math.Abs((new DateTimeOffset(Convert.ToDateTime(persisted["eta"])).ToUniversalTime() - storedEta).TotalSeconds), 0, 1);

            var conflictingReplay = await Invoke("SendEta", http, jobId,
                new Dictionary<string, object?>(body) { ["message"] = "Changed payload" }, db, audit, CancellationToken.None);
            Assert.Equal(StatusCodes.Status409Conflict, Assert.IsAssignableFrom<IStatusCodeHttpResult>(conflictingReplay).StatusCode);
            Assert.Equal(1, await db.ScalarLongAsync("SELECT COUNT(*) FROM customer_communications WHERE company_id=@c AND job_id=@j",
                c => { c.Parameters.AddWithValue("@c", companyId); c.Parameters.AddWithValue("@j", jobId); }));

            var concurrentJob = await db.InsertAsync(
                @"INSERT INTO jobs(company_id,branch_id,customer_id,job_code,job_type,status,priority,eta,tracking_code)
                  VALUES (@c,@b,@customer,@code,'Delivery','Unassigned','Normal',@eta,@tracking)",
                c =>
                {
                    c.Parameters.AddWithValue("@c", companyId);
                    c.Parameters.AddWithValue("@b", branchId);
                    c.Parameters.AddWithValue("@customer", customerId);
                    c.Parameters.AddWithValue("@code", $"ETA-CONCURRENT-{companyId}");
                    c.Parameters.AddWithValue("@eta", storedEta);
                    c.Parameters.AddWithValue("@tracking", $"ETA-CONCURRENT-TRK-{companyId}");
                });
            var concurrentKey = $"eta-concurrent-{concurrentJob}";
            (Database Database, DefaultHttpContext Context) Request()
            {
                var database = Db();
                var context = Principal(companyId, branchId);
                context.Request.Headers["Idempotency-Key"] = concurrentKey;
                return (database, context);
            }
            var requestA = Request();
            var requestB = Request();
            var concurrentResults = await Task.WhenAll(
                Invoke("SendEta", requestA.Context, concurrentJob, body, requestA.Database, new AuditService(requestA.Database), CancellationToken.None),
                Invoke("SendEta", requestB.Context, concurrentJob, body, requestB.Database, new AuditService(requestB.Database), CancellationToken.None));
            Assert.All(concurrentResults, result => Assert.Equal(StatusCodes.Status200OK, Assert.IsAssignableFrom<IStatusCodeHttpResult>(result).StatusCode));
            Assert.Equal(1, await db.ScalarLongAsync("SELECT COUNT(*) FROM eta_updates WHERE company_id=@c AND job_id=@j",
                c => { c.Parameters.AddWithValue("@c", companyId); c.Parameters.AddWithValue("@j", concurrentJob); }));
            Assert.Equal(1, await db.ScalarLongAsync("SELECT COUNT(*) FROM customer_communications WHERE company_id=@c AND job_id=@j",
                c => { c.Parameters.AddWithValue("@c", companyId); c.Parameters.AddWithValue("@j", concurrentJob); }));

            var noEtaJob = await db.InsertAsync(
                "INSERT INTO jobs(company_id,branch_id,customer_id,job_code,job_type,status,priority) VALUES (@c,@b,@customer,@code,'Delivery','Unassigned','Normal')",
                c => { c.Parameters.AddWithValue("@c", companyId); c.Parameters.AddWithValue("@b", branchId); c.Parameters.AddWithValue("@customer", customerId); c.Parameters.AddWithValue("@code", $"NO-ETA-{companyId}"); });
            var noEta = await Invoke("SendEta", Principal(companyId, branchId), noEtaJob, new Dictionary<string, object?>(), db, audit, CancellationToken.None);
            Assert.Equal(StatusCodes.Status400BadRequest, Assert.IsAssignableFrom<IStatusCodeHttpResult>(noEta).StatusCode);
            Assert.Equal(0, await db.ScalarLongAsync("SELECT COUNT(*) FROM customer_communications WHERE company_id=@c AND job_id=@j",
                c => { c.Parameters.AddWithValue("@c", companyId); c.Parameters.AddWithValue("@j", noEtaJob); }));
        }
        finally { await Cleanup(db, companyId); }
    }

    [Fact]
    public async Task ShipmentRegisterFiltersExportAndImportStayBranchScopedAndRejectBadRows()
    {
        var db = Db();
        await EnsureJobRuntimeSchema(db);
        var companyId = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() + Random.Shared.Next(2_000_000, 2_900_000);
        const long branchA = 9207;
        const long branchB = 9208;
        await SeedCompany(db, companyId);
        try
        {
            var customerId = await db.InsertAsync(
                "INSERT INTO customers(company_id,customer_code,name,status) VALUES (@c,@code,'Needle Customer','Active')",
                c => { c.Parameters.AddWithValue("@c", companyId); c.Parameters.AddWithValue("@code", $"REG-CUS-{companyId}"); });
            var visibleCode = $"=NEEDLE-{companyId}";
            var visibleId = await db.InsertAsync(
                @"INSERT INTO jobs(company_id,branch_id,customer_id,job_code,job_number,job_type,status,priority,pickup_address,dropoff_address,scheduled_start)
                  VALUES (@c,@b,@customer,@code,@code,'Delivery','Assigned','High','Needle Dock','Customer Dock',NOW())",
                c => { c.Parameters.AddWithValue("@c", companyId); c.Parameters.AddWithValue("@b", branchA); c.Parameters.AddWithValue("@customer", customerId); c.Parameters.AddWithValue("@code", visibleCode); });
            var hiddenCode = $"HIDDEN-{companyId}";
            var hiddenId = await db.InsertAsync(
                @"INSERT INTO jobs(company_id,branch_id,customer_id,job_code,job_type,status,priority,pickup_address,dropoff_address,scheduled_start)
                  VALUES (@c,@b,@customer,@code,'Delivery','Assigned','High','Needle Other Branch','Customer Dock',NOW())",
                c => { c.Parameters.AddWithValue("@c", companyId); c.Parameters.AddWithValue("@b", branchB); c.Parameters.AddWithValue("@customer", customerId); c.Parameters.AddWithValue("@code", hiddenCode); });
            var inProgressCode = $"INPROGRESS-{companyId}";
            var deliveredCode = $"DELIVERED-{companyId}";
            foreach (var pair in new[] { (inProgressCode, "In Progress"), (deliveredCode, "Delivered") })
                await db.ExecuteAsync(
                    "INSERT INTO jobs(company_id,branch_id,customer_id,job_code,job_type,status,priority,pickup_address,dropoff_address,scheduled_start) VALUES (@c,@b,@customer,@code,'Delivery',@status,'Normal','Group Dock','Group Customer',NOW())",
                    c => { c.Parameters.AddWithValue("@c", companyId); c.Parameters.AddWithValue("@b", branchA); c.Parameters.AddWithValue("@customer", customerId); c.Parameters.AddWithValue("@code", pair.Item1); c.Parameters.AddWithValue("@status", pair.Item2); });

            var http = Principal(companyId, branchA);
            http.Request.QueryString = new QueryString("?search=needle&status=assigned&priority=high&limit=1&offset=0");
            var listed = await Invoke("Jobs", http, db, CancellationToken.None);
            Assert.Equal(StatusCodes.Status200OK, Assert.IsAssignableFrom<IStatusCodeHttpResult>(listed).StatusCode);
            Assert.Equal("1", http.Response.Headers["X-Total-Count"].ToString());
            var listPayload = JsonSerializer.Serialize(Assert.IsAssignableFrom<IValueHttpResult>(listed).Value, new JsonSerializerOptions(JsonSerializerDefaults.Web));
            Assert.Contains(visibleCode, listPayload, StringComparison.Ordinal);
            Assert.DoesNotContain(hiddenCode, listPayload, StringComparison.Ordinal);

            http.Request.QueryString = new QueryString($"?jobId={visibleId}");
            var focused = await Invoke("Jobs", http, db, CancellationToken.None);
            Assert.Equal("1", http.Response.Headers["X-Total-Count"].ToString());
            Assert.Contains(visibleCode, JsonSerializer.Serialize(Assert.IsAssignableFrom<IValueHttpResult>(focused).Value), StringComparison.Ordinal);
            http.Request.QueryString = new QueryString($"?jobId={hiddenId}");
            var hiddenFocus = await Invoke("Jobs", http, db, CancellationToken.None);
            Assert.Equal("0", http.Response.Headers["X-Total-Count"].ToString());
            Assert.DoesNotContain(hiddenCode, JsonSerializer.Serialize(Assert.IsAssignableFrom<IValueHttpResult>(hiddenFocus).Value), StringComparison.Ordinal);
            http.Request.QueryString = new QueryString("?jobId=not-a-positive-id");
            Assert.Equal(StatusCodes.Status400BadRequest,
                Assert.IsAssignableFrom<IStatusCodeHttpResult>(await Invoke("Jobs", http, db, CancellationToken.None)).StatusCode);

            http.Request.QueryString = new QueryString("?status=En%20Route");
            var enRouteGroup = await Invoke("Jobs", http, db, CancellationToken.None);
            Assert.Equal("1", http.Response.Headers["X-Total-Count"].ToString());
            Assert.Contains(inProgressCode, JsonSerializer.Serialize(Assert.IsAssignableFrom<IValueHttpResult>(enRouteGroup).Value), StringComparison.Ordinal);
            http.Request.QueryString = new QueryString("?status=Completed");
            var completedGroup = await Invoke("Jobs", http, db, CancellationToken.None);
            Assert.Equal("1", http.Response.Headers["X-Total-Count"].ToString());
            Assert.Contains(deliveredCode, JsonSerializer.Serialize(Assert.IsAssignableFrom<IValueHttpResult>(completedGroup).Value), StringComparison.Ordinal);

            http.Request.QueryString = new QueryString("?status=not-a-real-status");
            var invalidFilter = await Invoke("Jobs", http, db, CancellationToken.None);
            Assert.Equal(StatusCodes.Status400BadRequest, Assert.IsAssignableFrom<IStatusCodeHttpResult>(invalidFilter).StatusCode);

            http.Request.QueryString = new QueryString("?search=needle&status=assigned&priority=high");
            var export = await Invoke("JobsExport", http, db, CancellationToken.None);
            var rawFile = export.GetType().GetProperty("FileContents")!.GetValue(export);
            var csvBytes = rawFile switch
            {
                byte[] bytes => bytes,
                ReadOnlyMemory<byte> memory => memory.ToArray(),
                _ => throw new InvalidOperationException("Unexpected CSV result type")
            };
            var csv = System.Text.Encoding.UTF8.GetString(csvBytes);
            Assert.Contains($"'{visibleCode}", csv, StringComparison.Ordinal);
            Assert.DoesNotContain(hiddenCode, csv, StringComparison.Ordinal);

            var readOnly = Principal(companyId, branchA);
            readOnly.Items[EndpointMappings.AuthPermissionsItemKey] = new[] { "shipments:view" };
            var forbiddenExport = await Invoke("JobsExport", readOnly, db, CancellationToken.None);
            Assert.Equal(StatusCodes.Status403Forbidden, Assert.IsAssignableFrom<IStatusCodeHttpResult>(forbiddenExport).StatusCode);

            var newCode = $"IMPORT-NEW-{companyId}";
            var importRows = new object[]
            {
                new { jobNumber = newCode, customerId = customerId.ToString(), jobType = "Delivery", priority = "Normal", pickupAddress = "A", dropoffAddress = "B" },
                new { jobNumber = visibleCode, customerId = customerId.ToString(), jobType = "Delivery", priority = "Critical", pickupAddress = "Updated A", dropoffAddress = "Updated B" },
                new { jobNumber = newCode, customerId = customerId.ToString(), jobType = "Delivery", priority = "Normal", pickupAddress = "Duplicate", dropoffAddress = "Duplicate" },
                new { jobNumber = $"BAD-{companyId}", customerId = "not-an-id", jobType = "Delivery", priority = "Normal", pickupAddress = "A", dropoffAddress = "B" },
                new { jobNumber = hiddenCode, customerId = customerId.ToString(), jobType = "Delivery", priority = "Normal", pickupAddress = "A", dropoffAddress = "B" }
            };
            var importBody = JsonBody(new { rows = importRows });
            var preview = await Invoke("JobsImportPreview", http, importBody, db, CancellationToken.None);
            Assert.Equal(StatusCodes.Status200OK, Assert.IsAssignableFrom<IStatusCodeHttpResult>(preview).StatusCode);
            var previewPayload = JsonSerializer.Serialize(Assert.IsAssignableFrom<IValueHttpResult>(preview).Value, new JsonSerializerOptions(JsonSerializerDefaults.Web));
            Assert.Contains("\"creates\":1", previewPayload, StringComparison.Ordinal);
            Assert.Contains("\"updates\":1", previewPayload, StringComparison.Ordinal);
            Assert.Contains("\"invalid\":3", previewPayload, StringComparison.Ordinal);
            Assert.Contains("outside the authorized branch", previewPayload, StringComparison.OrdinalIgnoreCase);

            http.Request.Headers["Idempotency-Key"] = $"jobs-import-{companyId}";
            var committed = await Invoke("JobsImportCommit", http, importBody, db, new AuditService(db), new NoopEvents(), CancellationToken.None);
            Assert.Equal(StatusCodes.Status200OK, Assert.IsAssignableFrom<IStatusCodeHttpResult>(committed).StatusCode);
            Assert.Equal(1, await db.ScalarLongAsync("SELECT COUNT(*) FROM jobs WHERE company_id=@c AND branch_id=@b AND job_code=@code",
                c => { c.Parameters.AddWithValue("@c", companyId); c.Parameters.AddWithValue("@b", branchA); c.Parameters.AddWithValue("@code", newCode); }));
            var updated = await db.QuerySingleAsync("SELECT priority,pickup_address FROM jobs WHERE id=@id", c => c.Parameters.AddWithValue("@id", visibleId));
            Assert.Equal("Critical", updated!["priority"]);
            Assert.Equal("Updated A", updated["pickupAddress"]);
            Assert.Equal(1, await db.ScalarLongAsync("SELECT COUNT(*) FROM jobs WHERE company_id=@c AND branch_id=@b AND job_code=@code",
                c => { c.Parameters.AddWithValue("@c", companyId); c.Parameters.AddWithValue("@b", branchB); c.Parameters.AddWithValue("@code", hiddenCode); }));

            var auditCount = await db.ScalarLongAsync("SELECT COUNT(*) FROM audit_logs WHERE company_id=@c AND action_name='jobs.imported'",
                c => c.Parameters.AddWithValue("@c", companyId));
            var replay = await Invoke("JobsImportCommit", http, importBody, db, new AuditService(db), new NoopEvents(), CancellationToken.None);
            Assert.Equal(StatusCodes.Status200OK, Assert.IsAssignableFrom<IStatusCodeHttpResult>(replay).StatusCode);
            Assert.Equal(auditCount, await db.ScalarLongAsync("SELECT COUNT(*) FROM audit_logs WHERE company_id=@c AND action_name='jobs.imported'",
                c => c.Parameters.AddWithValue("@c", companyId)));
            var conflictingBody = JsonBody(new { rows = new[] { new { jobNumber = $"CONFLICT-{companyId}", customerId = customerId.ToString(), pickupAddress = "A", dropoffAddress = "B" } } });
            var conflict = await Invoke("JobsImportCommit", http, conflictingBody, db, new AuditService(db), new NoopEvents(), CancellationToken.None);
            Assert.Equal(StatusCodes.Status409Conflict, Assert.IsAssignableFrom<IStatusCodeHttpResult>(conflict).StatusCode);
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
                new Dictionary<string, object?> { ["receivedBy"] = "Wrong branch", ["photoFileId"] = 1L }, db, audit, CancellationToken.None);

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
                "INSERT INTO vehicles(company_id,branch_id,vehicle_code,type,vin_exception_type,alternate_identifier,status,availability_status,out_of_service) VALUES (@c,@b,'UNIT-9221','Truck','legacy-fleet-identifier','UNIT-9221','Available','available',false)",
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

            await db.ExecuteAsync("UPDATE vehicles SET status='Maintenance',availability_status='out_of_service',out_of_service=true WHERE id=@id",
                c => c.Parameters.AddWithValue("@id", vehicle));
            var redTagOverride = await Invoke("AssignJob", http, jobId,
                new Dictionary<string, object?>
                {
                    ["driverId"] = eligible,
                    ["vehicleId"] = vehicle,
                    ["override"] = true,
                    ["overrideReason"] = "Customer escalation approved by dispatch manager"
                }, db, new AuditService(db), CancellationToken.None);
            Assert.Equal(StatusCodes.Status400BadRequest, Assert.IsAssignableFrom<IStatusCodeHttpResult>(redTagOverride).StatusCode);
            Assert.Equal(0, await db.ScalarLongAsync("SELECT COUNT(*) FROM dispatch_assignments WHERE company_id=@c AND job_id=@j", c => { c.Parameters.AddWithValue("@c", companyId); c.Parameters.AddWithValue("@j", jobId); }));
        }
        finally { await Cleanup(db, companyId); }
    }

    [Fact]
    public async Task ConcurrentCreatesSerializeCaseInsensitiveJobNumbers()
    {
        var setup = Db();
        await EnsureJobRuntimeSchema(setup);
        var companyId = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() + Random.Shared.Next(3_000_000, 3_900_000);
        const long branchId = 9231;
        await SeedCompany(setup, companyId);
        try
        {
            var customer = await setup.InsertAsync(
                "INSERT INTO customers(company_id,customer_code,name,status) VALUES (@c,@code,'Case Customer','Active')",
                c => { c.Parameters.AddWithValue("@c", companyId); c.Parameters.AddWithValue("@code", $"CASE-CUS-{companyId}"); });
            var baseCode = $"Case-Job-{companyId}";
            Dictionary<string, object?> Body(string code) => new()
            {
                ["jobNumber"] = code,
                ["customerId"] = customer,
                ["pickupAddress"] = "Origin",
                ["dropoffAddress"] = "Destination"
            };
            var dbA = Db();
            var dbB = Db();
            var results = await Task.WhenAll(
                Invoke("CreateJob", Principal(companyId, branchId), Body(baseCode), dbA, new AuditService(dbA), new NoopEvents(), CancellationToken.None),
                Invoke("CreateJob", Principal(companyId, branchId), Body(baseCode.ToUpperInvariant()), dbB, new AuditService(dbB), new NoopEvents(), CancellationToken.None));
            Assert.Equal(1, results.Count(result => Assert.IsAssignableFrom<IStatusCodeHttpResult>(result).StatusCode == StatusCodes.Status201Created));
            Assert.Equal(1, results.Count(result => Assert.IsAssignableFrom<IStatusCodeHttpResult>(result).StatusCode == StatusCodes.Status400BadRequest));
            Assert.Equal(1, await setup.ScalarLongAsync(
                "SELECT COUNT(*) FROM jobs WHERE company_id=@c AND LOWER(job_code)=LOWER(@code)",
                c => { c.Parameters.AddWithValue("@c", companyId); c.Parameters.AddWithValue("@code", baseCode); }));
        }
        finally { await Cleanup(setup, companyId); }
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

    private static Dictionary<string, object?> JsonBody(object value) =>
        JsonSerializer.Deserialize<Dictionary<string, object?>>(JsonSerializer.Serialize(value))!;

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
        http.Items[EndpointMappings.AuthPermissionsItemKey] = new[] { "shipments:view", "shipments:export", "job:update", "dispatch:view", "dispatch:manage", "dispatch:assign", "dispatch:override", "fleet.pod.view" };
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
            "DELETE FROM outbox_messages WHERE tenant_id=@c", "DELETE FROM billing_confidence_records WHERE company_id=@c",
            "DELETE FROM proof_artifacts WHERE company_id=@c", "DELETE FROM proof_packages WHERE company_id=@c",
            "DELETE FROM dispatch_assignments WHERE company_id=@c", "DELETE FROM proof_of_delivery WHERE company_id=@c",
            "DELETE FROM customer_eta_links WHERE company_id=@c", "DELETE FROM eta_updates WHERE company_id=@c", "DELETE FROM customer_communications WHERE company_id=@c",
            "DELETE FROM idempotency_keys WHERE tenant_id=@c",
            "DELETE FROM job_status_events WHERE company_id=@c", "DELETE FROM entity_timeline_events WHERE company_id=@c",
            "DELETE FROM audit_logs WHERE company_id=@c", "DELETE FROM documents WHERE company_id=@c", "DELETE FROM jobs WHERE company_id=@c",
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
