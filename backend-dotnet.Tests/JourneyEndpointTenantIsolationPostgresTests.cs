using System.Reflection;
using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Opstrax.Api.Controllers;
using Opstrax.Api.Data;
using Opstrax.Api.Foundation;

namespace Opstrax.Tests;

// The owner seeds fixtures only. Endpoint assertions use the restricted runtime
// identity and signed tenant scopes, with positive controls for both companies.
[Trait("Category", "Integration")]
[Collection("fleet-identity-schema")]
public sealed class JourneyEndpointTenantIsolationPostgresTests
{
    [Fact]
    public async Task ContractCustomerOptionsIsolateBothCompaniesAndRequireCreationPermission()
    {
        await using var f = await Fixtures.Create();
        foreach (var (own, foreign) in new[] { (f.A, f.B), (f.B, f.A) })
        {
            var result = await f.Endpoint(own, null, "contract.create", "ContractCustomerOptions");
            Assert.Equal(200, Status(result));
            var json = Json(result);
            Assert.Contains($"Active-{own}", json);
            Assert.DoesNotContain($"Active-{foreign}", json);
            Assert.DoesNotContain($"Inactive-{own}", json);
            Assert.DoesNotContain($"Deleted-{own}", json);
            Assert.DoesNotContain("email", json, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("companyId", json, StringComparison.OrdinalIgnoreCase);
            Assert.Equal(403, Status(await f.Endpoint(own, null, "contract.view", "ContractCustomerOptions")));
        }
    }

    [Fact]
    public async Task AssignmentOptionsUsePersistedJobBranchAndRejectForeignJobs()
    {
        await using var f = await Fixtures.Create();
        foreach (var (own, foreign) in new[] { (f.A, f.B), (f.B, f.A) })
        {
            var job = f.Jobs[own];
            var result = await f.Endpoint(own, Fixtures.Branch, "dispatch:assign", "JobAssignmentOptions", job);
            Assert.Equal(200, Status(result));
            var json = Json(result);
            Assert.Contains($"Driver-{own}", json);
            Assert.Contains($"Vehicle-{own}", json);
            Assert.DoesNotContain($"Driver-{foreign}", json);
            Assert.DoesNotContain($"Vehicle-{foreign}", json);
            Assert.DoesNotContain($"OtherBranch-{own}", json);
            Assert.DoesNotContain($"DeletedDriver-{own}", json);
            Assert.Contains("Authoritative HOS unavailable or stale", json);
            Assert.DoesNotContain("license", json, StringComparison.OrdinalIgnoreCase);
            Assert.Equal(404, Status(await f.Endpoint(own, Fixtures.Branch, "dispatch:assign", "JobAssignmentOptions", f.Jobs[foreign])));
            Assert.Equal(404, Status(await f.Endpoint(own, Fixtures.Branch + 1, "dispatch:assign", "JobAssignmentOptions", job)));
            Assert.Equal(403, Status(await f.Endpoint(own, Fixtures.Branch, "dispatch:view", "JobAssignmentOptions", job)));
            // A company-wide actor still picks resources in this job's persisted branch.
            var wide = Json(await f.Endpoint(own, null, "dispatch:assign", "JobAssignmentOptions", job));
            Assert.Contains($"Driver-{own}", wide);
            Assert.DoesNotContain($"OtherBranch-{own}", wide);
        }
    }

    [Fact]
    public async Task RouteAssignmentOptionsIsolateCompaniesBranchesAndNullBranchResources()
    {
        await using var f = await Fixtures.Create();
        foreach (var (own, foreign) in new[] { (f.A, f.B), (f.B, f.A) })
        {
            var result = await f.Endpoint(own, Fixtures.Branch, "dispatch:assign", "RouteAssignmentOptions", f.Routes[own]);
            Assert.Equal(200, Status(result));
            var json = Json(result);
            Assert.Contains($"Driver-{own}", json);
            Assert.Contains($"Vehicle-{own}", json);
            Assert.DoesNotContain($"Driver-{foreign}", json);
            Assert.DoesNotContain($"OtherBranch-{own}", json);
            Assert.Contains("Authoritative HOS unavailable or stale", json);
            Assert.Equal(404, Status(await f.Endpoint(own, Fixtures.Branch, "dispatch:assign", "RouteAssignmentOptions", f.Routes[foreign])));
            Assert.Equal(404, Status(await f.Endpoint(own, Fixtures.Branch + 1, "dispatch:assign", "RouteAssignmentOptions", f.Routes[own])));
            Assert.Equal(403, Status(await f.Endpoint(own, Fixtures.Branch, "dispatch:view", "RouteAssignmentOptions", f.Routes[own])));
            var wide = Json(await f.Endpoint(own, null, "dispatch:manage", "RouteAssignmentOptions", f.Routes[own]));
            Assert.Contains($"Driver-{own}", wide); Assert.DoesNotContain($"OtherBranch-{own}", wide);
            var unbranched = Json(await f.Endpoint(own, null, "dispatch:assign", "RouteAssignmentOptions", f.NullRoutes[own]));
            Assert.DoesNotContain($"Driver-{own}", unbranched); Assert.DoesNotContain($"Vehicle-{own}", unbranched);
            Assert.Contains("\"drivers\":[]", unbranched); Assert.Contains("\"vehicles\":[]", unbranched);
        }
    }

    [Fact]
    public async Task MaintenanceListAndDetailHaveTwoCompanyPositiveControlsAndForeignDenials()
    {
        await using var f = await Fixtures.Create();
        foreach (var (own, foreign) in new[] { (f.A, f.B), (f.B, f.A) })
        {
            var listed = await f.Endpoint(own, Fixtures.Branch, "maintenance:view", "MaintenanceItems");
            Assert.Equal(200, Status(listed));
            Assert.Contains($"Repair-{own}", Json(listed));
            Assert.DoesNotContain($"Repair-{foreign}", Json(listed));
            Assert.Equal(200, Status(await f.Endpoint(own, Fixtures.Branch, "maintenance:view", "MaintenanceDetail", f.Maintenance[own])));
            Assert.Equal(404, Status(await f.Endpoint(own, Fixtures.Branch, "maintenance:view", "MaintenanceDetail", f.Maintenance[foreign])));
            Assert.Equal(404, Status(await f.Endpoint(own, Fixtures.Branch + 1, "maintenance:view", "MaintenanceDetail", f.Maintenance[own])));
            Assert.Equal(403, Status(await f.Endpoint(own, Fixtures.Branch, "shipments:view", "MaintenanceItems")));
        }
    }

    [Fact]
    public async Task InvoiceApprovalsAreTenantBranchScopedAndRequireADifferentReviewer()
    {
        await using var f=await Fixtures.Create();await f.SeedApprovals();
        foreach(var (own,foreign) in new[]{(f.A,f.B),(f.B,f.A)})
        {
            var list=await f.ApprovalEndpoint(own,Fixtures.Branch,43,"finance.invoice.issue","ListApprovalRequests");
            Assert.Equal(200,Status(list));Assert.Contains($"Draft-{own}",Json(list));Assert.DoesNotContain($"Draft-{foreign}",Json(list));
            Assert.Equal("[]",System.Text.Json.JsonDocument.Parse(Json(await f.ApprovalEndpoint(own,Fixtures.Branch+1,43,"finance.invoice.issue","ListApprovalRequests"))).RootElement.GetProperty("data").GetRawText());
            var body=new Dictionary<string,object?>{["decision"]="approved",["notes"]="Local isolated test"};
            Assert.Equal(404,Status(await f.ApprovalEndpoint(own,Fixtures.Branch,43,"finance.invoice.issue","DecideApprovalRequest",f.Approvals[foreign],body)));
            Assert.Equal(404,Status(await f.ApprovalEndpoint(own,Fixtures.Branch+1,43,"finance.invoice.issue","DecideApprovalRequest",f.Approvals[own],body)));
            Assert.Equal(409,Status(await f.ApprovalEndpoint(own,Fixtures.Branch,42,"finance.invoice.issue","DecideApprovalRequest",f.Approvals[own],body)));
            Assert.Equal(400,Status(await f.ApprovalEndpoint(own,Fixtures.Branch,43,"finance.invoice.issue","DecideApprovalRequest",f.Approvals[own],new Dictionary<string,object?>{["decision"]="invalid"})));
            Assert.Equal(403,Status(await f.ApprovalEndpoint(own,Fixtures.Branch,43,"finance.invoice.read","DecideApprovalRequest",f.Approvals[own],body)));
            await f.SetRequester(own, null);
            Assert.Equal(409,Status(await f.ApprovalEndpoint(own,Fixtures.Branch,43,"finance.invoice.issue","DecideApprovalRequest",f.Approvals[own],body)));
            await f.SetRequester(own, "42");
            Assert.Equal(200,Status(await f.ApprovalEndpoint(own,Fixtures.Branch,43,"finance.invoice.issue","DecideApprovalRequest",f.Approvals[own],body)));
            Assert.Equal(409,Status(await f.ApprovalEndpoint(own,Fixtures.Branch,44,"finance.invoice.issue","DecideApprovalRequest",f.Approvals[own],body)));
        }
    }

    [Fact]
    public async Task ConcurrentInvoiceReviewersRecordOnlyOneDecision()
    {
        await using var f = await Fixtures.Create();
        await f.SeedApprovals();
        var body = new Dictionary<string, object?> { ["decision"] = "approved" };
        var results = await Task.WhenAll(
            f.ApprovalEndpoint(f.A, Fixtures.Branch, 43, "finance.invoice.issue", "DecideApprovalRequest", f.Approvals[f.A], body),
            f.ApprovalEndpoint(f.A, Fixtures.Branch, 44, "finance.invoice.issue", "DecideApprovalRequest", f.Approvals[f.A], body));
        Assert.Single(results, result => Status(result) == 200);
        Assert.Single(results, result => Status(result) == 409);
    }

    private static int? Status(IResult r) => Assert.IsAssignableFrom<IStatusCodeHttpResult>(r).StatusCode;
    private static string Json(IResult r) => JsonSerializer.Serialize(Assert.IsAssignableFrom<IValueHttpResult>(r).Value, new JsonSerializerOptions(JsonSerializerDefaults.Web));

    private sealed class Fixtures : IAsyncDisposable
    {
        public const long Branch = 9821;
        public long A { get; } = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() + Random.Shared.Next(10_000_000, 19_000_000);
        public long B => A + 1;
        public Dictionary<long, long> Jobs { get; } = new();
        public Dictionary<long, long> Routes { get; } = new();
        public Dictionary<long, long> NullRoutes { get; } = new();
        public Dictionary<long, long> Maintenance { get; } = new();
        public Dictionary<long,long> Approvals {get;}=new();
        private readonly Database owner = Db(false);
        private readonly Database runtime = Db(true);
        private static Database Db(bool restricted) => new(new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["ConnectionStrings:DefaultConnection"] = restricted ? TestDb.AppConnectionString : TestDb.ConnectionString,
            ["ConnectionStrings:SystemConnection"] = TestDb.SystemConnectionString,
            ["Rls:EnforceTenantContext"] = restricted.ToString(),
        }).Build(), new TenantScopeAccessor());
        public static async Task<Fixtures> Create()
        {
            var f = new Fixtures();
            foreach (var company in new[] { f.A, f.B })
            {
                await f.owner.ExecuteAsync("INSERT INTO companies(id,company_code,name,industry) OVERRIDING SYSTEM VALUE VALUES (@c,@code,'Journey scope fixture','Transportation')", c => { c.Parameters.AddWithValue("c", company); c.Parameters.AddWithValue("code", $"JS-{company}"); });
                foreach (var (name, status, deleted) in new[] { ("Active", "Active", false), ("Inactive", "Inactive", false), ("Deleted", "Active", true) })
                    await f.owner.ExecuteAsync("INSERT INTO customers(company_id,customer_code,name,status,deleted_at) VALUES (@c,@code,@name,@status,CASE WHEN @deleted THEN NOW() ELSE NULL END)", c => { c.Parameters.AddWithValue("c", company); c.Parameters.AddWithValue("code", $"{name}-{company}"); c.Parameters.AddWithValue("name", $"{name}-{company}"); c.Parameters.AddWithValue("status", status); c.Parameters.AddWithValue("deleted", deleted); });
                foreach (var (name, branch, deleted) in new[] { ("Driver", Branch, false), ("OtherBranch", Branch + 1, false), ("DeletedDriver", Branch, true) })
                    await f.owner.ExecuteAsync("INSERT INTO drivers(company_id,branch_id,driver_code,full_name,status,deleted_at) VALUES (@c,@b,@code,@code,'Available',CASE WHEN @deleted THEN NOW() ELSE NULL END)", c => { c.Parameters.AddWithValue("c", company); c.Parameters.AddWithValue("b", branch); c.Parameters.AddWithValue("code", $"{name}-{company}"); c.Parameters.AddWithValue("deleted", deleted); });
                var vehicle = await f.owner.InsertAsync("INSERT INTO vehicles(company_id,branch_id,vehicle_code,type,status,vin_exception_type,alternate_identifier) VALUES (@c,@b,@code,'Truck','Available','legacy-fleet-identifier',@code)", c => { c.Parameters.AddWithValue("c", company); c.Parameters.AddWithValue("b", Branch); c.Parameters.AddWithValue("code", $"Vehicle-{company}"); });
                await f.owner.ExecuteAsync("INSERT INTO vehicles(company_id,branch_id,vehicle_code,type,status,vin_exception_type,alternate_identifier) VALUES (@c,@b,@code,'Truck','Available','legacy-fleet-identifier',@code)", c => { c.Parameters.AddWithValue("c", company); c.Parameters.AddWithValue("b", Branch + 1); c.Parameters.AddWithValue("code", $"OtherBranch-{company}"); });
                f.Routes[company] = await f.owner.InsertAsync("INSERT INTO routes(company_id,branch_id,route_code,name,status) VALUES (@c,@b,@code,'Journey scoped route','Draft')", c => { c.Parameters.AddWithValue("c", company); c.Parameters.AddWithValue("b", Branch); c.Parameters.AddWithValue("code", $"Route-{company}"); });
                f.NullRoutes[company] = await f.owner.InsertAsync("INSERT INTO routes(company_id,route_code,name,status) VALUES (@c,@code,'Journey null branch route','Draft')", c => { c.Parameters.AddWithValue("c", company); c.Parameters.AddWithValue("code", $"NullRoute-{company}"); });
                f.Jobs[company] = await f.owner.InsertAsync("INSERT INTO jobs(company_id,branch_id,job_code,job_type,status) VALUES (@c,@b,@code,'Delivery','Unassigned')", c => { c.Parameters.AddWithValue("c", company); c.Parameters.AddWithValue("b", Branch); c.Parameters.AddWithValue("code", $"Job-{company}"); });
                f.Maintenance[company] = await f.owner.InsertAsync("INSERT INTO maintenance_items(company_id,vehicle_id,service_type,title,category,status,priority,due_date) VALUES (@c,@v,@title,@title,'Preventive','Scheduled','Low',CURRENT_DATE+7)", c => { c.Parameters.AddWithValue("c", company); c.Parameters.AddWithValue("v", vehicle); c.Parameters.AddWithValue("title", $"Repair-{company}"); });
            }
            return f;
        }
        public async Task SeedApprovals()
        {
            foreach(var company in new[]{A,B})
            {
                var customer=await owner.ScalarLongAsync("SELECT id FROM customers WHERE company_id=@c AND name=@name",c=>{c.Parameters.AddWithValue("c",company);c.Parameters.AddWithValue("name",$"Active-{company}");});
                var id=Guid.NewGuid();
                await owner.ExecuteAsync("INSERT INTO invoice_drafts(id,company_id,customer_id,job_id,invoice_draft_no,status,currency,subtotal,tax_total,total,source,metadata_json) VALUES (@id,@c,@customer,@job,@no,'pending_review','USD',25,0,25,'job_charges','{}')",c=>{c.Parameters.AddWithValue("id",id);c.Parameters.AddWithValue("c",company);c.Parameters.AddWithValue("customer",customer);c.Parameters.AddWithValue("job",Jobs[company]);c.Parameters.AddWithValue("no",$"Draft-{company}");});
                Approvals[company]=new PostgresApprovalWorkflowService(owner).CreateRequest(company.ToString(),ActorTypes.TenantUser,"42","finance.invoice.issue","invoice_draft",id.ToString(),"{}","high").Id;
            }
        }
        public Task SetRequester(long company, string? requester) => owner.ExecuteAsync("UPDATE approval_requests SET requested_by_actor_id=@requester WHERE id=@id AND tenant_id=@company", c =>
        {
            c.Parameters.AddWithValue("requester", (object?)requester ?? DBNull.Value);
            c.Parameters.AddWithValue("id", Approvals[company]);
            c.Parameters.AddWithValue("company", company);
        });
        public Task<IResult> ApprovalEndpoint(long company,long? branch,long reviewer,string permission,string method,params object[] extra)
        {
            var http=new DefaultHttpContext();http.Items[EndpointMappings.AuthCompanyIdItemKey]=company;if(branch.HasValue)http.Items[EndpointMappings.AuthBranchIdItemKey]=branch.Value;
            http.Items[EndpointMappings.AuthUserIdItemKey]=reviewer;http.Items[EndpointMappings.AuthRoleItemKey]="Reviewer";http.Items[EndpointMappings.AuthPermissionsItemKey]=new[]{permission};
            return runtime.RunInTenantScopeAsync(company,async()=>
            {
                var args=new List<object>{http};args.AddRange(extra);args.Add(runtime);if(method=="DecideApprovalRequest")args.Add(new PostgresApprovalWorkflowService(runtime));args.Add(CancellationToken.None);
                return await (Task<IResult>)typeof(RevenueReadinessEndpoints).GetMethod(method,BindingFlags.NonPublic|BindingFlags.Static)!.Invoke(null,args.ToArray())!;
            });
        }
        public Task<IResult> Endpoint(long company, long? branch, string permission, string method, params object[] extra)
        {
            var http = new DefaultHttpContext();
            http.Items[EndpointMappings.AuthCompanyIdItemKey] = company;
            if (branch.HasValue) http.Items[EndpointMappings.AuthBranchIdItemKey] = branch.Value;
            http.Items[EndpointMappings.AuthUserIdItemKey] = 42L;
            http.Items[EndpointMappings.AuthRoleItemKey] = "Dispatcher";
            http.Items[EndpointMappings.AuthPermissionsItemKey] = new[] { permission };
            return runtime.RunInTenantScopeAsync(company, async () =>
            {
                var arguments = new List<object> { http }; arguments.AddRange(extra); arguments.Add(runtime); arguments.Add(CancellationToken.None);
                var target = typeof(EndpointMappings).GetMethod(method, BindingFlags.NonPublic | BindingFlags.Static)!;
                try { return await (Task<IResult>)target.Invoke(null, arguments.ToArray())!; }
                catch (TargetInvocationException ex) when (ex.InnerException is not null) { System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(ex.InnerException).Throw(); throw; }
            });
        }
        public async ValueTask DisposeAsync()
        {
            foreach(var table in new[]{"approval_decisions","approval_requests"})
                await owner.ExecuteAsync($"DELETE FROM {table} WHERE tenant_id IN (@a,@b)",c=>{c.Parameters.AddWithValue("a",A);c.Parameters.AddWithValue("b",B);});
            await owner.ExecuteAsync("DELETE FROM invoice_drafts WHERE company_id IN (@a,@b)",c=>{c.Parameters.AddWithValue("a",A);c.Parameters.AddWithValue("b",B);});
            foreach (var table in new[] { "maintenance_items", "jobs", "routes", "vehicles", "drivers", "customers" })
                await owner.ExecuteAsync($"DELETE FROM {table} WHERE company_id IN (@a,@b)", c => { c.Parameters.AddWithValue("a", A); c.Parameters.AddWithValue("b", B); });
            await owner.ExecuteAsync("DELETE FROM companies WHERE id IN (@a,@b)", c => { c.Parameters.AddWithValue("a", A); c.Parameters.AddWithValue("b", B); });
        }
    }
}
