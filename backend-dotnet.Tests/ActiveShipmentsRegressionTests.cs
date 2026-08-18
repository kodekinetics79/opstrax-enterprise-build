using System.Net;
using System.Reflection;
using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Opstrax.Api.Controllers;
using Opstrax.Api.Data;
using Opstrax.Api.Foundation;
using Opstrax.Api.Services;

namespace Opstrax.Tests;

public sealed class ActiveShipmentsContractTests
{
    [Theory]
    [InlineData("=1+1", "\"'=1+1\"")]
    [InlineData(" +SUM(A1:A2)", "\"' +SUM(A1:A2)\"")]
    [InlineData("-2+3", "\"'-2+3\"")]
    [InlineData("@SUM(A1:A2)", "\"'@SUM(A1:A2)\"")]
    [InlineData("ordinary \"quoted\" value", "\"ordinary \"\"quoted\"\" value\"")]
    public void CsvCell_PreventsSpreadsheetFormulaExecution(string input, string expected)
        => Assert.Equal(expected, ActiveShipmentsEndpoints.CsvCell(input));

    [Fact]
    public void ActiveShipmentsUsesCanonicalJobsLifecycleAndFunctionalDeepLinks()
    {
        var page = ReadSource("frontend", "src", "pages", "ActiveShipmentsPage.tsx");
        var jobsPage = ReadSource("frontend", "src", "pages", "JobsPage.tsx");
        var endpoint = ReadSource("backend-dotnet", "Controllers", "ActiveShipmentsEndpoints.cs");
        var migration = ReadSource("database", "migrations", "2026_08_01_stage64_shipments_pilot.sql");
        var schema = ReadSource("backend-dotnet", "Services", "Stage9SchemaService.cs");

        Assert.Contains("FROM jobs j", endpoint, StringComparison.Ordinal);
        Assert.DoesNotContain("FROM fleet_tms_shipments", endpoint, StringComparison.Ordinal);
        Assert.Contains("LOWER(j.status) NOT IN ('delivered','cancelled','canceled')", endpoint, StringComparison.Ordinal);
        Assert.Contains("proof_packages", endpoint, StringComparison.Ordinal);
        Assert.Contains("proof_artifacts", endpoint, StringComparison.Ordinal);
        Assert.Contains("billing_confidence_records", endpoint, StringComparison.Ordinal);
        Assert.Contains("shipments:export", endpoint, StringComparison.Ordinal);
        Assert.Contains("\"@offset\", ((long)page - 1) * pageSize", endpoint, StringComparison.Ordinal);
        Assert.Contains("/shipments?jobId=", page, StringComparison.Ordinal);
        Assert.Contains("/proof-of-delivery?jobId=", page, StringComparison.Ordinal);
        Assert.DoesNotContain("shipmentId=", page, StringComparison.Ordinal);
        Assert.DoesNotContain("/map-view?shipmentNumber=", page, StringComparison.Ordinal);
        Assert.Contains("focusedJobId", jobsPage, StringComparison.Ordinal);
        foreach (var index in new[]
        {
            "idx_jobs_active_projection", "idx_dispatch_assignments_job_projection_recent",
            "idx_proof_packages_company_job_recent", "idx_location_company_vehicle_recent",
            "idx_pod_company_job_projection_recent"
        })
        {
            Assert.Contains(index, migration, StringComparison.Ordinal);
            Assert.Contains(index, schema, StringComparison.Ordinal);
        }
        Assert.DoesNotContain("DROP INDEX", migration, StringComparison.OrdinalIgnoreCase);
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
public sealed class ActiveShipmentsPostgresRegressionTests
{
    [Fact]
    public async Task RestrictedRoleProjectionFailsClosedWhenRequestTenantAndSignedScopeDisagree()
    {
        var owner = Db();
        var runtime = RlsDb();
        var companyA = 8_210_000_000L + Random.Shared.Next(1, 100_000);
        var companyB = companyA + 200_000;
        var branch = companyA + 10;
        var numberA = $"AS-RLS-A-{Guid.NewGuid():N}";
        var numberB = $"AS-RLS-B-{Guid.NewGuid():N}";
        WebApplication? app = null;
        try
        {
            await SeedCompanyBranch(owner, companyA, branch);
            await SeedCompanyBranch(owner, companyB, branch + 1);
            await InsertJob(owner, companyA, branch, numberA, "Assigned", etaSql: "NOW()+INTERVAL '3 hours'");
            await InsertJob(owner, companyB, branch + 1, numberB, "Assigned", etaSql: "NOW()+INTERVAL '3 hours'");

            var builder = WebApplication.CreateBuilder();
            builder.WebHost.UseKestrel().UseUrls("http://127.0.0.1:0");
            builder.Services.AddSingleton(runtime);
            app = builder.Build();
            app.Use(async (context, next) =>
            {
                var requestCompany = long.Parse(context.Request.Headers["X-Request-Company"].ToString());
                var scopeCompany = long.Parse(context.Request.Headers["X-Scope-Company"].ToString());
                context.Items[EndpointMappings.AuthCompanyIdItemKey] = requestCompany;
                context.Items[EndpointMappings.AuthBranchIdItemKey] = requestCompany == companyA ? branch : branch + 1;
                context.Items[EndpointMappings.AuthUserIdItemKey] = 881099L;
                context.Items[EndpointMappings.AuthRoleItemKey] = "Shipment RLS Tester";
                context.Items[EndpointMappings.AuthPermissionsItemKey] = new[] { "shipments:view" };
                await runtime.RunInTenantScopeAsync(scopeCompany, async () => { await next(); return true; });
            });
            app.MapActiveShipmentsEndpoints();
            await app.StartAsync();
            using var client = Client(app);

            var own = await ReadScoped(client, companyA, companyA);
            Assert.Contains(numberA, own, StringComparison.Ordinal);
            Assert.DoesNotContain(numberB, own, StringComparison.Ordinal);
            var mismatch = await ReadScoped(client, companyB, companyA);
            Assert.DoesNotContain(numberA, mismatch, StringComparison.Ordinal);
            Assert.DoesNotContain(numberB, mismatch, StringComparison.Ordinal);
        }
        finally
        {
            if (app is not null) { await app.StopAsync(); await app.DisposeAsync(); }
            await Cleanup(owner, companyA);
            await Cleanup(owner, companyB);
        }
    }

    [Fact]
    public async Task CanonicalProjectionEnforcesBranchPermissionsRiskPagingExportAndIgnoresFleetOnlyRows()
    {
        var db = Db();
        await EnsureRuntimeSchema(db);
        var company = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() + Random.Shared.Next(10_000, 99_999);
        var otherCompany = company + 1_000;
        var branch = company + 10;
        var otherBranch = branch + 1;
        var suffix = Guid.NewGuid().ToString("N")[..10];
        var atRisk = $"JOB-AT-{suffix}";
        var breached = $"JOB-BR-{suffix}";
        var noEta = $"JOB-NE-{suffix}";
        var completed = $"JOB-CP-{suffix}";
        var delivered = $"JOB-DL-{suffix}";
        var cancelled = $"JOB-CN-{suffix}";
        var otherBranchNumber = $"JOB-OB-{suffix}";
        var otherTenantNumber = $"JOB-OT-{suffix}";
        var fleetOnly = $"FTMS-ONLY-{suffix}";
        WebApplication? app = null;
        try
        {
            await SeedCompanyBranch(db, company, branch);
            await db.ExecuteAsync("INSERT INTO branches(id,company_id,branch_code,name,status) OVERRIDING SYSTEM VALUE VALUES (@id,@c,@code,'Other Branch','Active')",
                c => { c.Parameters.AddWithValue("@id", otherBranch); c.Parameters.AddWithValue("@c", company); c.Parameters.AddWithValue("@code", $"OB-{company}"); });
            await SeedCompanyBranch(db, otherCompany, otherCompany + 10);
            var customer = await db.InsertAsync("INSERT INTO customers(company_id,customer_code,name,status) VALUES (@c,@code,@name,'Active')",
                c => { c.Parameters.AddWithValue("@c", company); c.Parameters.AddWithValue("@code", $"CUS-{company}"); c.Parameters.AddWithValue("@name", "=SUM(1,1)"); });
            var driver = await Driver(db, company, branch, $"DRV-{suffix}", "Pilot Driver");
            var vehicle = await Vehicle(db, company, branch, $"VEH-{suffix}");
            var foreignDriver = await Driver(db, company, otherBranch, $"ODRV-{suffix}", "Other Branch Driver");
            var foreignVehicle = await Vehicle(db, company, otherBranch, $"OVEH-{suffix}");

            await InsertJob(db, company, branch, atRisk, "Assigned", customer, driver, vehicle,
                etaSql: "NOW()+INTERVAL '3 hours'", commitmentSql: "NOW()+INTERVAL '2 hours'");
            var breachedId = await InsertJob(db, company, branch, breached, "In Progress", customer, driver, vehicle,
                etaSql: "NOW()+INTERVAL '1 hour'", commitmentSql: "NOW()-INTERVAL '10 minutes'");
            await db.ExecuteAsync("INSERT INTO location_events(company_id,vehicle_id,lat,lng,event_time) VALUES (@c,@v,43.6532,-79.3832,NOW()-INTERVAL '2 minutes')",
                c => { c.Parameters.AddWithValue("@c", company); c.Parameters.AddWithValue("@v", vehicle); });
            await InsertJob(db, company, branch, noEta, "Custom Hold", customer, foreignDriver, foreignVehicle);
            var completedId = await InsertJob(db, company, branch, completed, "Completed", customer, etaSql: "NOW()+INTERVAL '1 hour'", commitmentSql: "NOW()+INTERVAL '2 hours'");
            await db.ExecuteAsync("UPDATE jobs SET proof_status='Captured' WHERE id=@j", c => c.Parameters.AddWithValue("@j", completedId));
            await db.ExecuteAsync(
                "INSERT INTO proof_of_delivery(company_id,job_id,receiver_name,received_by,proof_type,status,captured_at) VALUES (@c,@j,'Receiver','Receiver','Delivery Photo','Captured',NOW()-INTERVAL '1 minute')",
                c => { c.Parameters.AddWithValue("@c", company); c.Parameters.AddWithValue("@j", completedId); });
            var mismatchedPackage = await db.InsertAsync(
                @"INSERT INTO proof_packages(company_id,job_id,proof_type,status,validation_status,captured_at,created_at)
                  VALUES (@c,@j,'proof_of_delivery','validated','passed',NOW()-INTERVAL '2 minutes',NOW())",
                c => { c.Parameters.AddWithValue("@c", company); c.Parameters.AddWithValue("@j", completedId); });
            var mismatchedEvidence = await db.InsertAsync(
                @"INSERT INTO documents(company_id,title,document_type,entity_type,entity_id,category,status,file_url)
                  VALUES (@c,'Mismatched evidence','POD Photo','Job',@j,'Proof of Delivery','Active',@url)",
                c => { c.Parameters.AddWithValue("@c", company); c.Parameters.AddWithValue("@j", completedId); c.Parameters.AddWithValue("@url", $"objkey:tenant/{company}/proof/2026/08/{Guid.NewGuid():N}.png"); });
            await db.ExecuteAsync("INSERT INTO proof_artifacts(company_id,proof_package_id,artifact_type,file_id,captured_at) VALUES (@c,@p,'photo',@f,NOW()-INTERVAL '2 minutes')",
                c => { c.Parameters.AddWithValue("@c", company); c.Parameters.AddWithValue("@p", mismatchedPackage); c.Parameters.AddWithValue("@f", mismatchedEvidence); });
            await db.ExecuteAsync("INSERT INTO billing_confidence_records(company_id,job_id,proof_package_id,confidence_score,status) VALUES (@c,@j,@p,0.95,'ready')",
                c => { c.Parameters.AddWithValue("@c", company); c.Parameters.AddWithValue("@j", completedId); c.Parameters.AddWithValue("@p", mismatchedPackage); });
            await InsertJob(db, company, branch, delivered, "Delivered", customer);
            await InsertJob(db, company, branch, cancelled, "cancelled", customer);
            await InsertJob(db, company, otherBranch, otherBranchNumber, "Assigned", customer);
            await InsertJob(db, otherCompany, otherCompany + 10, otherTenantNumber, "Assigned");
            await db.ExecuteAsync("INSERT INTO fleet_tms_shipments(company_id,branch_id,shipment_number,status,customer_name) VALUES (@c,@b,@n,'InTransit','Fleet-only')",
                c => { c.Parameters.AddWithValue("@c", company); c.Parameters.AddWithValue("@b", branch); c.Parameters.AddWithValue("@n", fleetOnly); });

            app = await StartApp(db, company, branch);
            using var client = Client(app);
            Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync("/api/fleet-tms/active-shipments")).StatusCode);
            Assert.Equal(HttpStatusCode.Forbidden, (await Send(client, "/api/fleet-tms/active-shipments", "dispatch:view")).StatusCode);
            Assert.Equal(HttpStatusCode.Forbidden, (await Send(client, "/api/fleet-tms/active-shipments/export", "dispatch:manage")).StatusCode);
            using (var portal = new HttpRequestMessage(HttpMethod.Get, "/api/fleet-tms/active-shipments"))
            {
                portal.Headers.Add("X-Test-Permissions", "shipments:view");
                portal.Headers.Add("X-Test-Customer-Portal", "1");
                Assert.Equal(HttpStatusCode.Forbidden, (await client.SendAsync(portal)).StatusCode);
            }

            using (var response = await Send(client, "/api/fleet-tms/active-shipments", "shipments:view"))
            {
                Assert.Equal(HttpStatusCode.OK, response.StatusCode);
                using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
                var data = json.RootElement.GetProperty("data");
                Assert.Equal(4, data.GetProperty("total").GetInt64());
                var items = data.GetProperty("items").EnumerateArray().ToArray();
                Assert.Contains(items, x => x.GetProperty("shipmentNumber").GetString() == atRisk && x.GetProperty("riskStatus").GetString() == "AtRisk");
                Assert.Contains(items, x => x.GetProperty("id").GetInt64() == breachedId && x.GetProperty("riskStatus").GetString() == "Breached" && x.GetProperty("trackingFreshness").GetString() == "Live");
                Assert.Contains(items, x => x.GetProperty("shipmentNumber").GetString() == noEta && x.GetProperty("status").GetString() == "Custom Hold" && x.GetProperty("assignmentStatus").GetString() == "Unassigned");
                Assert.Contains(items, x => x.GetProperty("shipmentNumber").GetString() == completed && !x.GetProperty("podReady").GetBoolean() && !x.GetProperty("isInvoiceReady").GetBoolean());
                var body = data.GetRawText();
                foreach (var hidden in new[] { delivered, cancelled, otherBranchNumber, otherTenantNumber, fleetOnly, "Other Branch Driver" })
                    Assert.DoesNotContain(hidden, body, StringComparison.Ordinal);
            }

            await AssertSingle(client, "/api/fleet-tms/active-shipments?risk=Breached", breached);
            await AssertSingle(client, "/api/fleet-tms/active-shipments?lifecycle=Completed", completed);
            using (var response = await Send(client, "/api/fleet-tms/active-shipments?page=1&pageSize=1", "shipments.view"))
            {
                Assert.Equal(HttpStatusCode.OK, response.StatusCode);
                using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
                Assert.Equal(4, json.RootElement.GetProperty("data").GetProperty("total").GetInt64());
                Assert.Single(json.RootElement.GetProperty("data").GetProperty("items").EnumerateArray());
            }
            using (var response = await Send(client, $"/api/fleet-tms/active-shipments/export?search={Uri.EscapeDataString(atRisk)}", "shipments.export"))
            {
                Assert.Equal(HttpStatusCode.OK, response.StatusCode);
                var csv = await response.Content.ReadAsStringAsync();
                Assert.Contains(atRisk, csv, StringComparison.Ordinal);
                Assert.Contains("\"'=SUM(1,1)\"", csv, StringComparison.Ordinal);
                Assert.DoesNotContain(otherBranchNumber, csv, StringComparison.Ordinal);
            }
        }
        finally
        {
            if (app is not null) { await app.StopAsync(); await app.DisposeAsync(); }
            await Cleanup(db, company);
            await Cleanup(db, otherCompany);
        }
    }

    [Fact]
    public async Task CreatedJobFlowsThroughActiveAssignmentProgressAndPodThenDeliveredExitsProjection()
    {
        var db = Db();
        await EnsureRuntimeSchema(db);
        var company = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() + Random.Shared.Next(200_000, 299_999);
        var branch = company + 10;
        WebApplication? app = null;
        try
        {
            await SeedCompanyBranch(db, company, branch);
            var customer = await db.InsertAsync("INSERT INTO customers(company_id,customer_code,name,status) VALUES (@c,@code,'Lifecycle Customer','Active')",
                c => { c.Parameters.AddWithValue("@c", company); c.Parameters.AddWithValue("@code", $"CUS-{company}"); });
            var driver = await Driver(db, company, branch, $"DRV-{company}", "Lifecycle Driver");
            var vehicle = await Vehicle(db, company, branch, $"VEH-{company}");
            await db.ExecuteAsync("INSERT INTO hos_records(company_id,driver_id,shift_date,remaining_drive_hours,remaining_shift_hours,hos_status) VALUES (@c,@d,CURRENT_DATE,8,8,'On Duty')",
                c => { c.Parameters.AddWithValue("@c", company); c.Parameters.AddWithValue("@d", driver); });
            var http = Principal(company, branch);
            var code = $"FLOW-{company}";
            var created = await Invoke("CreateJob", http, new Dictionary<string, object?>
            {
                ["jobNumber"] = code, ["customerId"] = customer, ["pickupAddress"] = "Origin Dock",
                ["dropoffAddress"] = "Destination Dock", ["eta"] = DateTimeOffset.UtcNow.AddHours(3).ToString("O")
            }, db, new AuditService(db), new NoopEvents(), CancellationToken.None);
            Assert.Equal(StatusCodes.Status201Created, Status(created));
            var jobId = await db.ScalarLongAsync("SELECT id FROM jobs WHERE company_id=@c AND job_code=@code",
                c => { c.Parameters.AddWithValue("@c", company); c.Parameters.AddWithValue("@code", code); });

            app = await StartApp(db, company, branch);
            using var client = Client(app);
            await AssertSingle(client, $"/api/fleet-tms/active-shipments?search={code}", code);

            var assigned = await Invoke("AssignJob", http, jobId,
                new Dictionary<string, object?> { ["driverId"] = driver, ["vehicleId"] = vehicle }, db, new AuditService(db), CancellationToken.None);
            Assert.Equal(StatusCodes.Status200OK, Status(assigned));
            var assignedItem = await SingleItem(client, code);
            Assert.Equal("FullyAssigned", assignedItem.GetProperty("assignmentStatus").GetString());
            Assert.Equal("Lifecycle Driver", assignedItem.GetProperty("driverName").GetString());

            foreach (var next in new[] { "En Route", "At Stop", "Completed" })
            {
                var moved = await Invoke("ChangeJobStatus", http, jobId, new Dictionary<string, object?> { ["status"] = next }, db, new AuditService(db), CancellationToken.None);
                Assert.Equal(StatusCodes.Status200OK, Status(moved));
                Assert.Equal(next, (await SingleItem(client, code)).GetProperty("status").GetString());
            }

            var evidence = await db.InsertAsync(
                @"INSERT INTO documents(company_id,title,document_type,entity_type,entity_id,category,status,file_url)
                  VALUES (@c,'Delivery photo','POD Photo','Job',@j,'Proof of Delivery','Active',@url)",
                c => { c.Parameters.AddWithValue("@c", company); c.Parameters.AddWithValue("@j", jobId); c.Parameters.AddWithValue("@url", $"objkey:tenant/{company}/proof/2026/08/{Guid.NewGuid():N}.png"); });
            var submitted = await Invoke("CaptureProof", http, jobId, new Dictionary<string, object?>
            {
                ["receivedBy"] = "Receiving Lead", ["photoFileId"] = evidence, ["idempotencyKey"] = $"pod-{jobId}"
            }, db, new AuditService(db), CancellationToken.None);
            Assert.Equal(StatusCodes.Status200OK, Status(submitted));
            var awaitingReview = await SingleItem(client, code);
            Assert.Equal("Submitted", awaitingReview.GetProperty("podStatus").GetString());
            Assert.False(awaitingReview.GetProperty("podReady").GetBoolean());
            Assert.False(awaitingReview.GetProperty("isInvoiceReady").GetBoolean());

            var proofId = await db.ScalarLongAsync("SELECT id FROM proof_of_delivery WHERE company_id=@c AND job_id=@j ORDER BY id DESC LIMIT 1",
                c => { c.Parameters.AddWithValue("@c", company); c.Parameters.AddWithValue("@j", jobId); });
            var reviewer = Principal(company, branch, userId: 84L);
            var verified = await Invoke("VerifyProofOfDelivery", reviewer, proofId, db, new AuditService(db), CancellationToken.None);
            Assert.Equal(StatusCodes.Status200OK, Status(verified));
            using var after = await Send(client, $"/api/fleet-tms/active-shipments?search={code}", "shipments:view");
            using var json = JsonDocument.Parse(await after.Content.ReadAsStringAsync());
            Assert.Equal(0, json.RootElement.GetProperty("data").GetProperty("total").GetInt64());
            Assert.Equal("Delivered", (await db.QuerySingleAsync("SELECT status FROM jobs WHERE id=@j", c => c.Parameters.AddWithValue("@j", jobId)))!["status"]);
            Assert.Equal(1, await db.ScalarLongAsync("SELECT COUNT(*) FROM outbox_messages WHERE tenant_id=@c AND event_type='job.delivered' AND aggregate_id=@j",
                c => { c.Parameters.AddWithValue("@c", company); c.Parameters.AddWithValue("@j", jobId.ToString()); }));
        }
        finally
        {
            if (app is not null) { await app.StopAsync(); await app.DisposeAsync(); }
            await Cleanup(db, company);
        }
    }

    private static async Task EnsureRuntimeSchema(Database db)
    {
        await new Batch2SchemaService(db).EnsureAsync();
        await new DriverSchemaService(db, NullLogger<DriverSchemaService>.Instance).EnsureAsync();
        await new MaintenanceSchemaService(db).EnsureAsync();
        await new DispatchSchemaService(db, NullLogger<DispatchSchemaService>.Instance).EnsureAsync();
        await new FoundationSchemaService(db).EnsureAsync();
        await new Stage9SchemaService(db).EnsureAsync();
        await new FleetTmsSchemaService(db, NullLogger<FleetTmsSchemaService>.Instance).EnsureAsync();
    }

    private static async Task<WebApplication> StartApp(Database db, long company, long branch)
    {
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseKestrel().UseUrls("http://127.0.0.1:0");
        builder.Services.AddSingleton(db);
        var app = builder.Build();
        app.Use(async (context, next) =>
        {
            context.Items[EndpointMappings.AuthCompanyIdItemKey] = company;
            context.Items[EndpointMappings.AuthBranchIdItemKey] = branch;
            context.Items[EndpointMappings.AuthUserIdItemKey] = 881100L;
            context.Items[EndpointMappings.AuthRoleItemKey] = "Shipment Pilot Tester";
            context.Items[EndpointMappings.AuthPermissionsItemKey] = context.Request.Headers["X-Test-Permissions"].ToString()
                .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            if (context.Request.Headers.ContainsKey("X-Test-Customer-Portal"))
                context.Items[EndpointMappings.AuthCustomerIdItemKey] = company + 700;
            await next();
        });
        app.MapActiveShipmentsEndpoints();
        await app.StartAsync();
        return app;
    }

    private static HttpClient Client(WebApplication app) => new()
    {
        BaseAddress = new Uri(app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.Single())
    };

    private static async Task<string> ReadScoped(HttpClient client, long requestCompany, long scopeCompany)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/fleet-tms/active-shipments");
        request.Headers.Add("X-Request-Company", requestCompany.ToString());
        request.Headers.Add("X-Scope-Company", scopeCompany.ToString());
        using var response = await client.SendAsync(request);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return await response.Content.ReadAsStringAsync();
    }

    private static async Task<HttpResponseMessage> Send(HttpClient client, string path, string permissions)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, path);
        request.Headers.Add("X-Test-Permissions", permissions);
        return await client.SendAsync(request);
    }

    private static async Task AssertSingle(HttpClient client, string path, string number)
    {
        using var response = await Send(client, path, "shipments:view");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal(1, json.RootElement.GetProperty("data").GetProperty("total").GetInt64());
        Assert.Equal(number, json.RootElement.GetProperty("data").GetProperty("items")[0].GetProperty("shipmentNumber").GetString());
    }

    private static async Task<JsonElement> SingleItem(HttpClient client, string number)
    {
        using var response = await Send(client, $"/api/fleet-tms/active-shipments?search={Uri.EscapeDataString(number)}", "shipments:view");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return json.RootElement.GetProperty("data").GetProperty("items")[0].Clone();
    }

    private static async Task<long> InsertJob(Database db, long company, long branch, string number, string status,
        long? customer = null, long? driver = null, long? vehicle = null, string? etaSql = null, string? commitmentSql = null)
    {
        var eta = etaSql ?? "NULL";
        var commitment = commitmentSql ?? "NULL";
        return await db.InsertAsync($@"INSERT INTO jobs(company_id,branch_id,customer_id,job_code,job_number,job_type,status,priority,
                                      pickup_address,dropoff_address,assigned_driver_id,assigned_vehicle_id,eta,sla_window_end,proof_status)
            VALUES (@c,@b,@customer,@number,@number,'Delivery',@status,'Normal','Toronto','Montreal',@driver,@vehicle,{eta},{commitment},'Pending')",
            c =>
            {
                c.Parameters.AddWithValue("@c", company); c.Parameters.AddWithValue("@b", branch);
                c.Parameters.AddWithValue("@number", number); c.Parameters.AddWithValue("@status", status);
                c.Parameters.AddWithValue("@customer", customer ?? (object)DBNull.Value);
                c.Parameters.AddWithValue("@driver", driver ?? (object)DBNull.Value);
                c.Parameters.AddWithValue("@vehicle", vehicle ?? (object)DBNull.Value);
            });
    }

    private static Task SeedCompanyBranch(Database db, long company, long branch) => db.ExecuteAsync(
        @"INSERT INTO companies(id,company_code,name,industry) OVERRIDING SYSTEM VALUE VALUES (@c,@code,'Active Shipment Test','Transportation');
          INSERT INTO branches(id,company_id,branch_code,name,status) OVERRIDING SYSTEM VALUE VALUES (@b,@c,@branchCode,'Pilot Branch','Active')",
        c => { c.Parameters.AddWithValue("@c", company); c.Parameters.AddWithValue("@b", branch); c.Parameters.AddWithValue("@code", $"AS-{company}"); c.Parameters.AddWithValue("@branchCode", $"ASB-{company}"); });

    private static Task<long> Driver(Database db, long company, long branch, string code, string name) => db.InsertAsync(
        "INSERT INTO drivers(company_id,branch_id,driver_code,full_name,status,safety_score,readiness_score,compliance_score) VALUES (@c,@b,@code,@name,'Available',95,95,95)",
        c => { c.Parameters.AddWithValue("@c", company); c.Parameters.AddWithValue("@b", branch); c.Parameters.AddWithValue("@code", code); c.Parameters.AddWithValue("@name", name); });

    private static Task<long> Vehicle(Database db, long company, long branch, string code) => db.InsertAsync(
        "INSERT INTO vehicles(company_id,branch_id,vehicle_code,type,vin_exception_type,alternate_identifier,status,availability_status,out_of_service,readiness_score,risk_score) VALUES (@c,@b,@code,'Truck','legacy-fleet-identifier',@code,'Available','available',false,95,5)",
        c => { c.Parameters.AddWithValue("@c", company); c.Parameters.AddWithValue("@b", branch); c.Parameters.AddWithValue("@code", code); });

    private static DefaultHttpContext Principal(long company, long branch, long userId = 42L)
    {
        var http = new DefaultHttpContext();
        http.Items[EndpointMappings.AuthCompanyIdItemKey] = company;
        http.Items[EndpointMappings.AuthBranchIdItemKey] = branch;
        http.Items[EndpointMappings.AuthUserIdItemKey] = userId;
        http.Items[EndpointMappings.AuthRoleItemKey] = "Tenant Admin";
        http.Items[EndpointMappings.AuthPermissionsItemKey] = new[] { "shipments:view", "shipments:export", "shipments:create", "shipments:update", "dispatch:manage", "dispatch:assign", "dispatch:override", "operations.proof.validate" };
        return http;
    }

    private static int? Status(IResult result) => Assert.IsAssignableFrom<IStatusCodeHttpResult>(result).StatusCode;

    private static async Task<IResult> Invoke(string method, params object[] args)
    {
        var target = typeof(EndpointMappings).GetMethod(method, BindingFlags.NonPublic | BindingFlags.Static)!;
        try { return await (Task<IResult>)target.Invoke(null, args)!; }
        catch (TargetInvocationException ex) when (ex.InnerException is not null)
        { System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(ex.InnerException).Throw(); throw; }
    }

    private static Database Db() => new(new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
    {
        ["ConnectionStrings:DefaultConnection"] = TestDb.ConnectionString, ["Rls:EnforceTenantContext"] = "false"
    }).Build());

    private static Database RlsDb() => new(new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
    {
        ["ConnectionStrings:DefaultConnection"] = TestDb.AppConnectionString,
        ["ConnectionStrings:SystemConnection"] = TestDb.SystemConnectionString,
        ["Rls:EnforceTenantContext"] = "true", ["Rls:TenantTicketTtlSeconds"] = "120"
    }).Build(), new TenantScopeAccessor());

    private static async Task Cleanup(Database db, long company)
    {
        foreach (var sql in new[]
        {
            "DELETE FROM fleet_tms_shipments WHERE company_id=@c", "DELETE FROM outbox_messages WHERE tenant_id=@c",
            "DELETE FROM billing_confidence_records WHERE company_id=@c", "DELETE FROM proof_artifacts WHERE company_id=@c",
            "DELETE FROM proof_packages WHERE company_id=@c", "DELETE FROM proof_of_delivery WHERE company_id=@c",
            "DELETE FROM dispatch_assignments WHERE company_id=@c", "DELETE FROM job_status_events WHERE company_id=@c",
            "DELETE FROM entity_timeline_events WHERE company_id=@c", "DELETE FROM audit_logs WHERE company_id=@c",
            "DELETE FROM documents WHERE company_id=@c", "DELETE FROM jobs WHERE company_id=@c",
            "DELETE FROM hos_records WHERE company_id=@c", "DELETE FROM location_events WHERE company_id=@c",
            "DELETE FROM vehicles WHERE company_id=@c", "DELETE FROM drivers WHERE company_id=@c",
            "DELETE FROM customers WHERE company_id=@c", "DELETE FROM branches WHERE company_id=@c", "DELETE FROM companies WHERE id=@c"
        })
            await db.ExecuteAsync(sql, c => c.Parameters.AddWithValue("@c", company));
    }

    private sealed class NoopEvents : IDomainEventPublisher
    {
        public DomainEventRecord Publish(string tenantId, string eventType, string aggregateType, string aggregateId,
            string payloadJson, string? correlationId = null, string? causationId = null, string? idempotencyKey = null) => null!;
    }
}
