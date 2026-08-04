using System.Net;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Opstrax.Api.Controllers;
using Opstrax.Api.Data;
using Opstrax.Api.Services;
using System.Reflection;
using System.Text.Json;

namespace Opstrax.Tests;

[Trait("Category", "Integration")]
public sealed class ProofOfDeliveryPilotTests
{
    [Fact]
    public void PodRuntimeSchemaIndexesNewestProofLookup()
    {
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (root is not null && !Directory.Exists(Path.Combine(root.FullName, "backend-dotnet"))) root = root.Parent;
        Assert.NotNull(root);
        var schema = File.ReadAllText(Path.Combine(root!.FullName, "backend-dotnet", "Services", "Batch2SchemaService.cs"));
        var migration = File.ReadAllText(Path.Combine(root.FullName, "database", "migrations", "2026_08_01_stage64_shipments_pilot.sql"));
        Assert.Contains("idx_pod_company_job_projection_recent", schema, StringComparison.Ordinal);
        Assert.Contains("idx_pod_company_job_projection_recent", migration, StringComparison.Ordinal);
    }

    [Fact]
    public async Task PodSubmissionAndVerificationAreTenantBranchRoleRaceAndOutboxSafe()
    {
        var db = Db();
        var companyId = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() + Random.Shared.Next(2_000_000, 2_900_000);
        var branchId = companyId + 10;
        await SeedCompanyAndBranch(db, companyId, branchId);
        try
        {
            var jobId = await Job(db, companyId, branchId, "Completed");
            var fileId = await Evidence(db, companyId, jobId, "signature");
            var body = Submission(fileId, $"pod-{companyId}");

            var submitted = await Invoke("CaptureProof", Principal(companyId, branchId, "Dispatcher", "dispatch:update"),
                jobId, body, db, new AuditService(db), CancellationToken.None);
            Assert.Equal(StatusCodes.Status200OK, Status(submitted));
            var proof = (await db.QuerySingleAsync(
                "SELECT id,status FROM proof_of_delivery WHERE company_id=@c AND job_id=@j",
                c => { c.Parameters.AddWithValue("@c", companyId); c.Parameters.AddWithValue("@j", jobId); }))!;
            var proofId = Convert.ToInt64(proof["id"]);
            Assert.Equal("Submitted", proof["status"]);
            Assert.Equal("Completed", await ScalarText(db, "SELECT status FROM jobs WHERE id=@j", "@j", jobId));
            Assert.Equal("Submitted", await ScalarText(db, "SELECT proof_status FROM jobs WHERE id=@j", "@j", jobId));
            Assert.Equal(1, await Count(db, "proof_packages", companyId));
            Assert.Equal(1, await Count(db, "proof_artifacts", companyId));
            Assert.Equal(0, await DeliveredOutbox(db, companyId, jobId));

            var replay = await Invoke("CaptureProof", Principal(companyId, branchId, "Dispatcher", "dispatch:update"),
                jobId, body, db, new AuditService(db), CancellationToken.None);
            Assert.Equal(StatusCodes.Status200OK, Status(replay));
            Assert.Equal(1, await Count(db, "proof_packages", companyId));
            Assert.Equal(1, await Count(db, "proof_artifacts", companyId));

            var attacker = await Invoke("VerifyProofOfDelivery", Principal(companyId + 1, branchId, "Fleet Manager", "dispatch:override"),
                proofId, db, new AuditService(db), CancellationToken.None);
            Assert.Equal(StatusCodes.Status404NotFound, Status(attacker));
            var wrongBranch = await Invoke("VerifyProofOfDelivery", Principal(companyId, branchId + 1, "Fleet Manager", "dispatch:override"),
                proofId, db, new AuditService(db), CancellationToken.None);
            Assert.Equal(StatusCodes.Status404NotFound, Status(wrongBranch));
            var dispatcherReview = await Invoke("VerifyProofOfDelivery", Principal(companyId, branchId, "Dispatcher", "dispatch:update"),
                proofId, db, new AuditService(db), CancellationToken.None);
            Assert.Equal(StatusCodes.Status403Forbidden, Status(dispatcherReview));
            var podSubmitterReview = await Invoke("VerifyProofOfDelivery", Principal(companyId, branchId, "POD Submitter", 84L, "fleet.pod.manage"),
                proofId, db, new AuditService(db), CancellationToken.None);
            Assert.Equal(StatusCodes.Status403Forbidden, Status(podSubmitterReview));
            var selfReview = await Invoke("VerifyProofOfDelivery", Principal(companyId, branchId, "Fleet Manager", 42L, "dispatch:override"),
                proofId, db, new AuditService(db), CancellationToken.None);
            Assert.Equal(StatusCodes.Status409Conflict, Status(selfReview));

            var dbA = Db();
            var dbB = Db();
            var reviews = await Task.WhenAll(
                Invoke("VerifyProofOfDelivery", Principal(companyId, branchId, "Fleet Manager", 84L, "dispatch:override"), proofId, dbA, new AuditService(dbA), CancellationToken.None),
                Invoke("VerifyProofOfDelivery", Principal(companyId, branchId, "Fleet Manager", 84L, "dispatch:override"), proofId, dbB, new AuditService(dbB), CancellationToken.None));
            Assert.Single(reviews, result => Status(result) == StatusCodes.Status200OK);
            Assert.Single(reviews, result => Status(result) == StatusCodes.Status409Conflict);
            Assert.Equal("Captured", await ScalarText(db, "SELECT status FROM proof_of_delivery WHERE id=@id", "@id", proofId));
            Assert.Equal("validated", await ScalarText(db, "SELECT status FROM proof_packages WHERE company_id=@c ORDER BY id DESC LIMIT 1", "@c", companyId));
            Assert.Equal("Delivered", await ScalarText(db, "SELECT status FROM jobs WHERE id=@j", "@j", jobId));
            Assert.Equal("ready", await ScalarText(db, "SELECT status FROM billing_confidence_records WHERE company_id=@c", "@c", companyId));
            Assert.Equal(1, await DeliveredOutbox(db, companyId, jobId));

            var immutable = await Invoke("CaptureProof", Principal(companyId, branchId, "Dispatcher", "dispatch:update"),
                jobId, Submission(fileId, $"pod-rewrite-{companyId}"), db, new AuditService(db), CancellationToken.None);
            Assert.Equal(StatusCodes.Status409Conflict, Status(immutable));
            Assert.Equal(1, await DeliveredOutbox(db, companyId, jobId));
        }
        finally { await Cleanup(db, companyId); }
    }

    [Fact]
    public async Task RejectedProofIsNotBillableAndCanBeCorrectedWithNewImmutablePackage()
    {
        var db = Db();
        var companyId = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() + Random.Shared.Next(3_000_000, 3_900_000);
        var branchId = companyId + 10;
        await SeedCompanyAndBranch(db, companyId, branchId);
        try
        {
            var jobId = await Job(db, companyId, branchId, "At Stop");
            var fileId = await Evidence(db, companyId, jobId, "photo");
            Assert.Equal(StatusCodes.Status200OK, Status(await Invoke("CaptureProof",
                Principal(companyId, branchId, "Dispatcher", "dispatch:update"), jobId,
                new Dictionary<string, object?>
                {
                    ["receivedBy"] = "Receiving Lead",
                    ["photoFileId"] = fileId,
                    ["capturedAt"] = DateTimeOffset.UtcNow.ToString("O"),
                    ["idempotencyKey"] = $"first-{companyId}"
                }, db, new AuditService(db), CancellationToken.None)));
            var proofId = await db.ScalarLongAsync("SELECT id FROM proof_of_delivery WHERE company_id=@c AND job_id=@j",
                c => { c.Parameters.AddWithValue("@c", companyId); c.Parameters.AddWithValue("@j", jobId); });

            var rejected = await Invoke("RejectProofOfDelivery", Principal(companyId, branchId, "Fleet Manager", 84L, "dispatch:override"),
                proofId, new Dictionary<string, object?> { ["reason"] = "Photo does not show the delivered seal" }, db, new AuditService(db), CancellationToken.None);
            Assert.Equal(StatusCodes.Status200OK, Status(rejected));
            Assert.Equal("Rejected", await ScalarText(db, "SELECT status FROM proof_of_delivery WHERE id=@id", "@id", proofId));
            Assert.Equal("Pending", await ScalarText(db, "SELECT proof_status FROM jobs WHERE id=@j", "@j", jobId));
            Assert.Equal("At Stop", await ScalarText(db, "SELECT status FROM jobs WHERE id=@j", "@j", jobId));
            Assert.Equal("blocked", await ScalarText(db, "SELECT status FROM billing_confidence_records WHERE company_id=@c", "@c", companyId));
            Assert.Equal(0, await DeliveredOutbox(db, companyId, jobId));

            var correctedFile = await Evidence(db, companyId, jobId, "signature");
            Assert.Equal(StatusCodes.Status200OK, Status(await Invoke("CaptureProof",
                Principal(companyId, branchId, "Dispatcher", "dispatch:update"), jobId,
                Submission(correctedFile, $"corrected-{companyId}"), db, new AuditService(db), CancellationToken.None)));
            Assert.Equal(2, await Count(db, "proof_packages", companyId));
            Assert.Equal("rejected", await ScalarText(db, "SELECT status FROM proof_packages WHERE company_id=@c ORDER BY id LIMIT 1", "@c", companyId));
            Assert.Equal("submitted", await ScalarText(db, "SELECT status FROM proof_packages WHERE company_id=@c ORDER BY id DESC LIMIT 1", "@c", companyId));
        }
        finally { await Cleanup(db, companyId); }
    }

    [Fact]
    public async Task PodReadModelsAreCanonicalPermissionBranchScopedAndDoNotLeakPrivateObjectReferences()
    {
        var db = Db();
        var companyId = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() + Random.Shared.Next(4_000_000, 4_900_000);
        var branchA = companyId + 10;
        var branchB = companyId + 20;
        await SeedCompanyAndBranch(db, companyId, branchA);
        await db.ExecuteAsync(
            "INSERT INTO branches(id,company_id,branch_code,name,status) OVERRIDING SYSTEM VALUE VALUES (@id,@c,@code,'Private Branch B','Active')",
            c => { c.Parameters.AddWithValue("@id", branchB); c.Parameters.AddWithValue("@c", companyId); c.Parameters.AddWithValue("@code", $"PB-B-{companyId}"); });
        try
        {
            var jobA = await Job(db, companyId, branchA, "Completed");
            var jobB = await Job(db, companyId, branchB, "Completed");
            var fileA = await Evidence(db, companyId, jobA, "signature");
            var fileB = await Evidence(db, companyId, jobB, "photo");
            Assert.Equal(StatusCodes.Status200OK, Status(await Invoke("CaptureProof",
                Principal(companyId, branchA, "Fleet Manager", "fleet.pod.manage"), jobA,
                Submission(fileA, $"branch-a-{companyId}"), db, new AuditService(db), CancellationToken.None)));
            Assert.Equal(StatusCodes.Status200OK, Status(await Invoke("CaptureProof",
                Principal(companyId, branchB, "Fleet Manager", "fleet.pod.manage"), jobB,
                new Dictionary<string, object?>
                {
                    ["receivedBy"] = "Private Branch B Receiver",
                    ["photoFileId"] = fileB,
                    ["capturedAt"] = DateTimeOffset.UtcNow.ToString("O"),
                    ["idempotencyKey"] = $"branch-b-{companyId}",
                    ["notes"] = "PRIVATE-BRANCH-B-NOTE"
                }, db, new AuditService(db), CancellationToken.None)));
            var proofA = await db.ScalarLongAsync(
                "SELECT id FROM proof_of_delivery WHERE company_id=@c AND job_id=@j",
                c => { c.Parameters.AddWithValue("@c", companyId); c.Parameters.AddWithValue("@j", jobA); });
            var proofB = await db.ScalarLongAsync(
                "SELECT id FROM proof_of_delivery WHERE company_id=@c AND job_id=@j",
                c => { c.Parameters.AddWithValue("@c", companyId); c.Parameters.AddWithValue("@j", jobB); });

            var viewerA = Principal(companyId, branchA, "POD Viewer", "fleet.pod.view");
            var list = Assert.IsAssignableFrom<IValueHttpResult>(await Invoke("ProofOfDeliveryList", viewerA, db, CancellationToken.None));
            Assert.Equal(StatusCodes.Status200OK, Status((IResult)list));
            var listJson = JsonSerializer.Serialize(list.Value, new JsonSerializerOptions(JsonSerializerDefaults.Web));
            using (var listDocument = JsonDocument.Parse(listJson))
            {
                var records = listDocument.RootElement.GetProperty("data").EnumerateArray().ToArray();
                Assert.Single(records);
                Assert.Equal(jobA, records[0].GetProperty("jobId").GetInt64());
                Assert.DoesNotContain(records, record => record.GetProperty("jobId").GetInt64() == jobB);
            }
            Assert.DoesNotContain("Private Branch B Receiver", listJson, StringComparison.Ordinal);
            Assert.DoesNotContain("PRIVATE-BRANCH-B-NOTE", listJson, StringComparison.Ordinal);
            Assert.DoesNotContain("objkey:", listJson, StringComparison.OrdinalIgnoreCase);

            var summary = Assert.IsAssignableFrom<IValueHttpResult>(await Invoke("ProofOfDeliverySummary", viewerA, db, CancellationToken.None));
            using (var summaryJson = JsonDocument.Parse(JsonSerializer.Serialize(summary.Value, new JsonSerializerOptions(JsonSerializerDefaults.Web))))
            {
                var data = summaryJson.RootElement.GetProperty("data");
                Assert.Equal(1, data.GetProperty("total").GetInt64());
                Assert.Equal(1, data.GetProperty("submitted").GetInt64());
            }

            var detail = Assert.IsAssignableFrom<IValueHttpResult>(await Invoke("ProofOfDeliveryDetail", viewerA, proofA, db, CancellationToken.None));
            Assert.Equal(StatusCodes.Status200OK, Status((IResult)detail));
            var detailJson = JsonSerializer.Serialize(detail.Value, new JsonSerializerOptions(JsonSerializerDefaults.Web));
            Assert.DoesNotContain("objkey:", detailJson, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("Seal intact", detailJson, StringComparison.Ordinal);
            Assert.Equal(StatusCodes.Status404NotFound, Status(await Invoke("ProofOfDeliveryDetail", viewerA, proofB, db, CancellationToken.None)));
            Assert.Equal(StatusCodes.Status404NotFound, Status(await Invoke("ProofOfDeliveryDetail",
                Principal(companyId + 1, branchA, "POD Viewer", "fleet.pod.view"), proofA, db, CancellationToken.None)));

            var noPermission = Principal(companyId, branchA, "POD Viewer");
            Assert.Equal(StatusCodes.Status403Forbidden, Status(await Invoke("ProofOfDeliveryList", noPermission, db, CancellationToken.None)));
            Assert.Equal(StatusCodes.Status403Forbidden, Status(await Invoke("ProofOfDeliverySummary", noPermission, db, CancellationToken.None)));
            Assert.Equal(StatusCodes.Status403Forbidden, Status(await Invoke("ProofOfDeliveryDetail", noPermission, proofA, db, CancellationToken.None)));
        }
        finally { await Cleanup(db, companyId); }
    }

    [Fact]
    public async Task CancelledJobsAndCrossJobIdempotencyKeysFailClosedWithoutPartialWrites()
    {
        var db = Db();
        var companyId = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() + Random.Shared.Next(5_000_000, 5_900_000);
        var branchId = companyId + 10;
        await SeedCompanyAndBranch(db, companyId, branchId);
        try
        {
            var firstJob = await Job(db, companyId, branchId, "Completed");
            var firstFile = await Evidence(db, companyId, firstJob, "photo");
            var key = $"shared-key-{companyId}";
            Assert.Equal(StatusCodes.Status200OK, Status(await Invoke("CaptureProof", Principal(companyId, branchId, "Dispatcher", "dispatch:update"),
                firstJob, new Dictionary<string, object?> { ["receivedBy"] = "Receiver", ["photoFileId"] = firstFile, ["idempotencyKey"] = key }, db, new AuditService(db), CancellationToken.None)));
            var proofId = await db.ScalarLongAsync("SELECT id FROM proof_of_delivery WHERE company_id=@c AND job_id=@j",
                c => { c.Parameters.AddWithValue("@c", companyId); c.Parameters.AddWithValue("@j", firstJob); });
            await db.ExecuteAsync("UPDATE jobs SET status='Cancelled' WHERE id=@j", c => c.Parameters.AddWithValue("@j", firstJob));
            Assert.Equal(StatusCodes.Status409Conflict, Status(await Invoke("VerifyProofOfDelivery", Principal(companyId, branchId, "Reviewer", 84L, "operations.proof.validate"),
                proofId, db, new AuditService(db), CancellationToken.None)));
            Assert.Equal("Cancelled", await ScalarText(db, "SELECT status FROM jobs WHERE id=@j", "@j", firstJob));
            Assert.Equal(0, await DeliveredOutbox(db, companyId, firstJob));

            var secondJob = await Job(db, companyId, branchId, "Completed");
            var secondFile = await Evidence(db, companyId, secondJob, "photo");
            var collision = await Invoke("CaptureProof", Principal(companyId, branchId, "Dispatcher", "dispatch:update"), secondJob,
                new Dictionary<string, object?> { ["receivedBy"] = "Other Receiver", ["photoFileId"] = secondFile, ["idempotencyKey"] = key }, db, new AuditService(db), CancellationToken.None);
            Assert.Equal(StatusCodes.Status409Conflict, Status(collision));
            Assert.Equal(0, await db.ScalarLongAsync("SELECT COUNT(*) FROM proof_of_delivery WHERE company_id=@c AND job_id=@j",
                c => { c.Parameters.AddWithValue("@c", companyId); c.Parameters.AddWithValue("@j", secondJob); }));
            Assert.Equal("Pending", await ScalarText(db, "SELECT proof_status FROM jobs WHERE id=@j", "@j", secondJob));
        }
        finally { await Cleanup(db, companyId); }
    }

    [Fact]
    public async Task RealHttpPodListAndExportArePermissionedFilteredPagedAndBranchSafe()
    {
        var db = Db();
        var companyId = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() + Random.Shared.Next(6_000_000, 6_900_000);
        var branchId = companyId + 10;
        var otherBranch = companyId + 20;
        await SeedCompanyAndBranch(db, companyId, branchId);
        await db.ExecuteAsync("INSERT INTO branches(id,company_id,branch_code,name,status) OVERRIDING SYSTEM VALUE VALUES (@id,@c,@code,'Other POD Branch','Active')",
            c => { c.Parameters.AddWithValue("@id", otherBranch); c.Parameters.AddWithValue("@c", companyId); c.Parameters.AddWithValue("@code", $"POD-OTHER-{companyId}"); });
        WebApplication? app = null;
        try
        {
            for (var index = 0; index < 31; index++)
                await db.ExecuteAsync("INSERT INTO jobs(company_id,branch_id,job_code,job_number,job_type,status,priority,proof_status) VALUES (@c,@b,@code,@code,'Delivery','Completed','Normal','Pending')",
                    c => { c.Parameters.AddWithValue("@c", companyId); c.Parameters.AddWithValue("@b", branchId); c.Parameters.AddWithValue("@code", index == 0 ? $"=POD-SCALE-{companyId}" : $"POD-SCALE-{companyId}-{index:00}"); });
            var hidden = $"POD-SCALE-HIDDEN-{companyId}";
            await db.ExecuteAsync("INSERT INTO jobs(company_id,branch_id,job_code,job_number,job_type,status,priority,proof_status) VALUES (@c,@b,@code,@code,'Delivery','Completed','Normal','Pending')",
                c => { c.Parameters.AddWithValue("@c", companyId); c.Parameters.AddWithValue("@b", otherBranch); c.Parameters.AddWithValue("@code", hidden); });

            var builder = WebApplication.CreateBuilder();
            builder.WebHost.UseKestrel().UseUrls("http://127.0.0.1:0");
            builder.Services.AddSingleton(db);
            app = builder.Build();
            app.Use(async (context, next) =>
            {
                context.Items[EndpointMappings.AuthCompanyIdItemKey] = companyId;
                context.Items[EndpointMappings.AuthBranchIdItemKey] = branchId;
                context.Items[EndpointMappings.AuthUserIdItemKey] = 84L;
                context.Items[EndpointMappings.AuthRoleItemKey] = "POD HTTP Reviewer";
                context.Items[EndpointMappings.AuthPermissionsItemKey] = context.Request.Headers["X-Test-Permissions"].ToString().Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
                await next();
            });
            app.MapGet("/api/proof-of-delivery", (HttpContext http, Database database, CancellationToken ct) =>
                Invoke("ProofOfDeliveryList", http, database, ct));
            app.MapGet("/api/proof-of-delivery/export", (HttpContext http, Database database, CancellationToken ct) =>
                Invoke("ProofOfDeliveryExport", http, database, ct));
            await app.StartAsync();
            var address = app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.Single();
            using var client = new HttpClient { BaseAddress = new Uri(address) };

            Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync("/api/proof-of-delivery")).StatusCode);
            using (var response = await Get(client, $"/api/proof-of-delivery?status=Pending&search=POD-SCALE-{companyId}&limit=10&offset=10", "fleet.pod.view"))
            {
                Assert.Equal(HttpStatusCode.OK, response.StatusCode);
                Assert.Equal("31", response.Headers.GetValues("X-Total-Count").Single());
                using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
                Assert.Equal(10, json.RootElement.GetProperty("data").GetArrayLength());
                Assert.DoesNotContain(hidden, json.RootElement.GetRawText(), StringComparison.Ordinal);
            }
            foreach (var path in new[] { "/api/proof-of-delivery?status=MadeUp", "/api/proof-of-delivery?limit=201", "/api/proof-of-delivery?offset=-1", "/api/proof-of-delivery?jobId=bad" })
                Assert.Equal(HttpStatusCode.BadRequest, (await Get(client, path, "fleet.pod.view")).StatusCode);
            Assert.Equal(HttpStatusCode.Forbidden, (await Get(client, "/api/proof-of-delivery/export", "fleet.pod.view")).StatusCode);
            using (var response = await Get(client, $"/api/proof-of-delivery/export?search=POD-SCALE-{companyId}", "shipments.export"))
            {
                Assert.Equal(HttpStatusCode.OK, response.StatusCode);
                var csv = await response.Content.ReadAsStringAsync();
                Assert.Contains($"'=POD-SCALE-{companyId}", csv, StringComparison.Ordinal);
                Assert.DoesNotContain(hidden, csv, StringComparison.Ordinal);
                Assert.Equal(32, csv.Split('\n', StringSplitOptions.RemoveEmptyEntries).Length);
            }
        }
        finally
        {
            if (app is not null) { await app.StopAsync(); await app.DisposeAsync(); }
            await Cleanup(db, companyId);
        }
    }

    private static async Task<HttpResponseMessage> Get(HttpClient client, string path, string permission)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, path);
        request.Headers.Add("X-Test-Permissions", permission);
        return await client.SendAsync(request);
    }

    private static Dictionary<string, object?> Submission(long fileId, string idempotencyKey) => new()
    {
        ["receivedBy"] = "Receiving Lead",
        ["receiverPhone"] = "+1 555 0100",
        ["signatureFileId"] = fileId,
        ["capturedLatitude"] = 43.6532m,
        ["capturedLongitude"] = -79.3832m,
        ["capturedAt"] = DateTimeOffset.UtcNow.ToString("O"),
        ["idempotencyKey"] = idempotencyKey,
        ["notes"] = "Seal intact"
    };

    private static Database Db() => new(new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
    { ["ConnectionStrings:DefaultConnection"] = TestDb.ConnectionString, ["Rls:EnforceTenantContext"] = "false" }).Build());

    private static DefaultHttpContext Principal(long companyId, long branchId, string role, params string[] permissions)
        => Principal(companyId, branchId, role, 42L, permissions);

    private static DefaultHttpContext Principal(long companyId, long branchId, string role, long userId, params string[] permissions)
    {
        var http = new DefaultHttpContext();
        http.Items[EndpointMappings.AuthCompanyIdItemKey] = companyId;
        http.Items[EndpointMappings.AuthBranchIdItemKey] = branchId;
        http.Items[EndpointMappings.AuthUserIdItemKey] = userId;
        http.Items[EndpointMappings.AuthRoleItemKey] = role;
        http.Items[EndpointMappings.AuthPermissionsItemKey] = permissions;
        return http;
    }

    private static async Task SeedCompanyAndBranch(Database db, long companyId, long branchId)
    {
        await db.ExecuteAsync(
            "INSERT INTO companies(id,company_code,name,industry) OVERRIDING SYSTEM VALUE VALUES (@id,@code,'POD Pilot Test','Transportation')",
            c => { c.Parameters.AddWithValue("@id", companyId); c.Parameters.AddWithValue("@code", $"POD-{companyId}"); });
        await db.ExecuteAsync(
            "INSERT INTO branches(id,company_id,branch_code,name,status) OVERRIDING SYSTEM VALUE VALUES (@id,@c,@code,'POD Branch','Active')",
            c => { c.Parameters.AddWithValue("@id", branchId); c.Parameters.AddWithValue("@c", companyId); c.Parameters.AddWithValue("@code", $"PB-{companyId}"); });
    }

    private static Task<long> Job(Database db, long companyId, long branchId, string status) => db.InsertAsync(
        "INSERT INTO jobs(company_id,branch_id,job_code,job_type,status,priority,proof_status) VALUES (@c,@b,@code,'Delivery',@status,'Normal','Pending')",
        c => { c.Parameters.AddWithValue("@c", companyId); c.Parameters.AddWithValue("@b", branchId); c.Parameters.AddWithValue("@code", $"JOB-{Guid.NewGuid():N}"); c.Parameters.AddWithValue("@status", status); });

    private static Task<long> Evidence(Database db, long companyId, long jobId, string kind) => db.InsertAsync(
        @"INSERT INTO documents(company_id,title,document_type,entity_type,entity_id,category,status,file_url)
          VALUES (@c,@title,@type,'Job',@j,'Proof of Delivery','Active',@reference)",
        c =>
        {
            c.Parameters.AddWithValue("@c", companyId);
            c.Parameters.AddWithValue("@j", jobId);
            c.Parameters.AddWithValue("@title", $"POD {kind}");
            c.Parameters.AddWithValue("@type", $"POD {kind}");
            c.Parameters.AddWithValue("@reference", $"objkey:tenant/{companyId}/proof/2026/08/{Guid.NewGuid():N}.png");
        });

    private static async Task<string> ScalarText(Database db, string sql, string name, object value)
        => (await db.QuerySingleAsync(sql, c => c.Parameters.AddWithValue(name, value)))!.Values.First()!.ToString()!;

    private static Task<long> Count(Database db, string table, long companyId) => db.ScalarLongAsync(
        $"SELECT COUNT(*) FROM {table} WHERE company_id=@c", c => c.Parameters.AddWithValue("@c", companyId));

    private static Task<long> DeliveredOutbox(Database db, long companyId, long jobId) => db.ScalarLongAsync(
        "SELECT COUNT(*) FROM outbox_messages WHERE tenant_id=@c AND event_type='job.delivered' AND aggregate_id=@j",
        c => { c.Parameters.AddWithValue("@c", companyId); c.Parameters.AddWithValue("@j", jobId.ToString()); });

    private static int Status(IResult result) => Assert.IsAssignableFrom<IStatusCodeHttpResult>(result).StatusCode ?? 200;

    private static async Task<IResult> Invoke(string name, params object?[] args)
    {
        var method = typeof(EndpointMappings).GetMethod(name, BindingFlags.NonPublic | BindingFlags.Static)
            ?? throw new MissingMethodException(name);
        try { return await (Task<IResult>)method.Invoke(null, args)!; }
        catch (TargetInvocationException ex) when (ex.InnerException is not null) { throw ex.InnerException; }
    }

    private static async Task Cleanup(Database db, long companyId)
    {
        foreach (var sql in new[]
        {
            "DELETE FROM billing_confidence_records WHERE company_id=@c",
            "DELETE FROM proof_artifacts WHERE company_id=@c",
            "DELETE FROM proof_packages WHERE company_id=@c",
            "DELETE FROM outbox_messages WHERE tenant_id=@c",
            "DELETE FROM dispatch_assignments WHERE company_id=@c",
            "DELETE FROM proof_of_delivery WHERE company_id=@c",
            "DELETE FROM job_status_events WHERE company_id=@c",
            "DELETE FROM entity_timeline_events WHERE company_id=@c",
            "DELETE FROM audit_logs WHERE company_id=@c",
            "DELETE FROM documents WHERE company_id=@c",
            "DELETE FROM jobs WHERE company_id=@c",
            "DELETE FROM branches WHERE company_id=@c",
            "DELETE FROM companies WHERE id=@c"
        })
            await db.ExecuteAsync(sql, c => c.Parameters.AddWithValue("@c", companyId));
    }
}
