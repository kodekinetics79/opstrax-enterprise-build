using System.Reflection;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Opstrax.Api.Controllers;
using Opstrax.Api.Data;
using Opstrax.Api.Services;

namespace Opstrax.Tests;

[Trait("Category", "Integration")]
public sealed class DispatchPilotPostgresTests
{
    [Fact]
    public async Task CreateAndOverridePrivileges_CannotBeBorrowedFromMutationAliases()
    {
        var db = Db();
        var scope = new SeedData(987654321, 123, 124, 42, 1, 1, 1);
        var normalBody = new object?[] { 1L, 1L, null, null, null, null, null, null, null, false, null, null };
        Assert.Equal(StatusCodes.Status403Forbidden,
            Status(await InvokeCreate(Principal(scope, "dispatch:update"), normalBody, db)));

        var overrideBody = new object?[] { 1L, 1L, null, null, null, null, null, null, "Supervisor approved", true, null, null };
        Assert.Equal(StatusCodes.Status403Forbidden,
            Status(await InvokeCreate(Principal(scope, "dispatch:assign"), overrideBody, db)));
        Assert.Equal(StatusCodes.Status403Forbidden,
            Status(await InvokeCreate(Principal(scope, "dispatch:manage"), overrideBody, db)));
    }

    [Fact]
    public async Task ExactCancelPermissionAndExceptionResume_FailClosedAndPreserveMirrors()
    {
        var db = Db();
        await new FoundationSchemaService(db).EnsureAsync();
        await new Batch2SchemaService(db).EnsureAsync();
        await new DispatchSchemaService(db, NullLogger<DispatchSchemaService>.Instance).EnsureAsync();
        var seed = await Seed(db);
        try
        {
            var assigned = await Assignment(db, seed, "assigned", null);
            var updateOnly = Principal(seed, "dispatch:update");
            Assert.Equal(StatusCodes.Status403Forbidden,
                Status(await InvokeWithBody("DispatchAssignmentCancel", assigned, updateOnly, new object?[] { "Customer cancelled" }, db)));
            Assert.Equal("assigned", (await Row(db, assigned))["assignmentStatus"]);

            Assert.Equal(StatusCodes.Status200OK,
                Status(await InvokeWithBody("DispatchAssignmentCancel", assigned, Principal(seed, "dispatch:cancel"), new object?[] { "Customer cancelled" }, db)));
            var cancelled = await Row(db, assigned);
            Assert.Equal("cancelled", cancelled["assignmentStatus"]);
            Assert.Equal("Cancelled", cancelled["status"]);
            Assert.Equal("Cancelled", cancelled["jobStatus"]);

            var exception = await Assignment(db, seed, "exception", "accepted");
            await db.InsertAsync(
                "INSERT INTO dispatch_exceptions(company_id,assignment_id,job_id,exception_type,severity,status,notes) VALUES (@c,@a,@j,'customer_hold','High','open','Dock closed')",
                c => { c.Parameters.AddWithValue("@c", seed.CompanyId); c.Parameters.AddWithValue("@a", exception); c.Parameters.AddWithValue("@j", seed.JobId); });
            var updater = Principal(seed, "dispatch:update");
            Assert.Equal(StatusCodes.Status409Conflict,
                Status(await InvokeWithBody("DispatchAssignmentStatus", exception, updater, new object?[] { "in_transit", null }, db)));
            Assert.Equal("exception", (await Row(db, exception))["assignmentStatus"]);
            Assert.Equal(StatusCodes.Status200OK,
                Status(await InvokeWithBody("DispatchAssignmentStatus", exception, updater, new object?[] { "accepted", null }, db)));
            var resumed = await Row(db, exception);
            Assert.Equal("accepted", resumed["assignmentStatus"]);
            Assert.Equal("Accepted", resumed["status"]);
            Assert.Equal("Accepted", resumed["jobStatus"]);
            Assert.Equal(0, await db.ScalarLongAsync(
                "SELECT COUNT(*) FROM dispatch_exceptions WHERE company_id=@c AND assignment_id=@a AND status='open'",
                c => { c.Parameters.AddWithValue("@c", seed.CompanyId); c.Parameters.AddWithValue("@a", exception); }));
        }
        finally { await Cleanup(db, seed.CompanyId); }
    }

    [Fact]
    public async Task ConcurrentDispatcherAndDriverDeliveryProof_HasOneWinnerOneProofOneOutbox()
    {
        var db = Db();
        await new FoundationSchemaService(db).EnsureAsync();
        await new Batch2SchemaService(db).EnsureAsync();
        await new DispatchSchemaService(db, NullLogger<DispatchSchemaService>.Instance).EnsureAsync();
        var seed = await Seed(db);
        try
        {
            var assignment = await Assignment(db, seed, "arrived_delivery", "in_transit");
            var dispatchDb = Db();
            var driverDb = Db();
            var dispatchCall = InvokeWithBody("DispatchAssignmentProof", assignment,
                Principal(seed, "dispatch:update"), new object?[] { "delivery", "Signed by receiver", "hash-a", 40m, -74m }, dispatchDb);
            var driverCall = InvokeWithBody("DriverSubmitProof", assignment,
                Principal(seed, "driver:self"), new object?[] { "delivery", "Signed by receiver", "hash-b", 40m, -74m, null }, driverDb);
            var results = await Task.WhenAll(dispatchCall, driverCall);

            Assert.Single(results.Where(r => Status(r) is StatusCodes.Status200OK or StatusCodes.Status201Created));
            Assert.Single(results.Where(r => Status(r) == StatusCodes.Status409Conflict));
            var final = await Row(db, assignment);
            Assert.Equal("delivered", final["assignmentStatus"]);
            Assert.Equal("Delivered", final["status"]);
            Assert.Equal("Delivered", final["jobStatus"]);
            Assert.Equal(1, await db.ScalarLongAsync("SELECT COUNT(*) FROM dispatch_proofs WHERE company_id=@c AND assignment_id=@a AND proof_type='delivery'", c => { c.Parameters.AddWithValue("@c", seed.CompanyId); c.Parameters.AddWithValue("@a", assignment); }));
            Assert.Equal(1, await db.ScalarLongAsync("SELECT COUNT(*) FROM outbox_messages WHERE tenant_id=@c AND aggregate_id=@j::text AND event_type='job.delivered'", c => { c.Parameters.AddWithValue("@c", seed.CompanyId); c.Parameters.AddWithValue("@j", seed.JobId); }));
        }
        finally { await Cleanup(db, seed.CompanyId); }
    }

    [Fact]
    public async Task BranchBoundaryAndDatabaseUniqueness_BlockLeaksAndDoubleBooking()
    {
        var db = Db();
        await new FoundationSchemaService(db).EnsureAsync();
        await new Batch2SchemaService(db).EnsureAsync();
        await new DispatchSchemaService(db, NullLogger<DispatchSchemaService>.Instance).EnsureAsync();
        var seed = await Seed(db);
        try
        {
            var assignment = await Assignment(db, seed, "assigned", null);
            var wrongBranch = Principal(seed with { BranchId = seed.OtherBranchId }, "dispatch:view");
            Assert.Equal(StatusCodes.Status404NotFound, Status(await Invoke("DispatchAssignmentDetail", assignment, wrongBranch, db, CancellationToken.None)));

            await Assert.ThrowsAnyAsync<Exception>(() => db.InsertAsync(
                @"INSERT INTO dispatch_assignments(company_id,branch_id,job_id,vehicle_id,driver_id,assignment_status,status)
                  VALUES (@c,@b,@j,@v,@d,'assigned','Assigned')",
                c => { c.Parameters.AddWithValue("@c", seed.CompanyId); c.Parameters.AddWithValue("@b", seed.BranchId); c.Parameters.AddWithValue("@j", seed.JobId); c.Parameters.AddWithValue("@v", seed.VehicleId); c.Parameters.AddWithValue("@d", seed.DriverId); }));
            Assert.Equal(1, await db.ScalarLongAsync("SELECT COUNT(*) FROM dispatch_assignments WHERE company_id=@c AND assignment_status NOT IN ('delivered','cancelled')", c => c.Parameters.AddWithValue("@c", seed.CompanyId)));
        }
        finally { await Cleanup(db, seed.CompanyId); }
    }

    private static async Task<IResult> Invoke(string name, params object[] args)
    {
        var method = typeof(EndpointMappings).GetMethod(name, BindingFlags.NonPublic | BindingFlags.Static)!;
        try { return await (Task<IResult>)method.Invoke(null, args)!; }
        catch (TargetInvocationException ex) when (ex.InnerException is not null)
        { System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(ex.InnerException).Throw(); throw; }
    }

    private static Task<IResult> InvokeWithBody(string name, long id, HttpContext http, object?[] bodyArgs, Database db)
    {
        var method = typeof(EndpointMappings).GetMethod(name, BindingFlags.NonPublic | BindingFlags.Static)!;
        var body = Activator.CreateInstance(method.GetParameters()[2].ParameterType,
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic, null, bodyArgs, null)!;
        return method.GetParameters()[0].ParameterType == typeof(HttpContext)
            ? Invoke(name, http, id, body, db, new AuditService(db), CancellationToken.None)
            : Invoke(name, id, http, body, db, new AuditService(db), CancellationToken.None);
    }

    private static Task<IResult> InvokeCreate(HttpContext http, object?[] bodyArgs, Database db)
    {
        var method = typeof(EndpointMappings).GetMethod("DispatchAssignmentCreate", BindingFlags.NonPublic | BindingFlags.Static)!;
        var body = Activator.CreateInstance(method.GetParameters()[1].ParameterType,
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic, null, bodyArgs, null)!;
        return Invoke("DispatchAssignmentCreate", http, body, db, new AuditService(db), new NotificationService(db), CancellationToken.None);
    }

    private static int? Status(IResult result) => Assert.IsAssignableFrom<IStatusCodeHttpResult>(result).StatusCode;
    private static Database Db() => new(new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
    { ["ConnectionStrings:DefaultConnection"] = TestDb.ConnectionString, ["Rls:EnforceTenantContext"] = "false" }).Build());

    private static DefaultHttpContext Principal(SeedData seed, params string[] permissions)
    {
        var http = new DefaultHttpContext();
        http.Items[EndpointMappings.AuthCompanyIdItemKey] = seed.CompanyId;
        http.Items[EndpointMappings.AuthBranchIdItemKey] = seed.BranchId;
        http.Items[EndpointMappings.AuthUserIdItemKey] = seed.UserId;
        http.Items[EndpointMappings.AuthRoleItemKey] = "Dispatch pilot tester";
        http.Items[EndpointMappings.AuthPermissionsItemKey] = permissions;
        return http;
    }

    private static async Task<SeedData> Seed(Database db)
    {
        var suffix = Guid.NewGuid().ToString("N")[..10];
        var company = await db.InsertAsync("INSERT INTO companies(company_code,name,industry) VALUES (@x,'Dispatch pilot test','Transportation')", c => c.Parameters.AddWithValue("@x", $"DSP-{suffix}"));
        var branch = await db.InsertAsync("INSERT INTO branches(company_id,branch_code,name,status) VALUES (@c,'MAIN','Main','Active')", c => c.Parameters.AddWithValue("@c", company));
        var other = await db.InsertAsync("INSERT INTO branches(company_id,branch_code,name,status) VALUES (@c,'OTHER','Other','Active')", c => c.Parameters.AddWithValue("@c", company));
        var user = await db.InsertAsync("INSERT INTO users(company_id,branch_id,full_name,email,role_name,status) VALUES (@c,@b,'Pilot Driver',@e,'Driver','Active')", c => { c.Parameters.AddWithValue("@c", company); c.Parameters.AddWithValue("@b", branch); c.Parameters.AddWithValue("@e", $"{suffix}@example.invalid"); });
        var driver = await db.InsertAsync("INSERT INTO drivers(company_id,branch_id,user_id,driver_code,full_name,status) VALUES (@c,@b,@u,@x,'Pilot Driver','Available')", c => { c.Parameters.AddWithValue("@c", company); c.Parameters.AddWithValue("@b", branch); c.Parameters.AddWithValue("@u", user); c.Parameters.AddWithValue("@x", $"D-{suffix}"); });
        var vehicle = await db.InsertAsync("INSERT INTO vehicles(company_id,branch_id,vehicle_code,type,vin_exception_type,alternate_identifier,status) VALUES (@c,@b,@x,'Truck','legacy-fleet-identifier',@x,'Available')", c => { c.Parameters.AddWithValue("@c", company); c.Parameters.AddWithValue("@b", branch); c.Parameters.AddWithValue("@x", $"V-{suffix}"); });
        var job = await db.InsertAsync("INSERT INTO jobs(company_id,branch_id,job_code,job_type,status) VALUES (@c,@b,@x,'Delivery','Assigned')", c => { c.Parameters.AddWithValue("@c", company); c.Parameters.AddWithValue("@b", branch); c.Parameters.AddWithValue("@x", $"J-{suffix}"); });
        return new(company, branch, other, user, driver, vehicle, job);
    }

    private static Task<long> Assignment(Database db, SeedData s, string status, string? previous) => db.InsertAsync(
        @"INSERT INTO dispatch_assignments(company_id,branch_id,job_id,vehicle_id,driver_id,assignment_status,status,previous_status)
          VALUES (@c,@b,@j,@v,@d,@s,@display,@p)",
        c => { c.Parameters.AddWithValue("@c", s.CompanyId); c.Parameters.AddWithValue("@b", s.BranchId); c.Parameters.AddWithValue("@j", s.JobId); c.Parameters.AddWithValue("@v", s.VehicleId); c.Parameters.AddWithValue("@d", s.DriverId); c.Parameters.AddWithValue("@s", status); c.Parameters.AddWithValue("@display", status); c.Parameters.AddWithValue("@p", (object?)previous ?? DBNull.Value); });

    private static Task<Dictionary<string, object?>?> Row(Database db, long id) => db.QuerySingleAsync(
        @"SELECT da.assignment_status,da.status,j.status job_status FROM dispatch_assignments da
          LEFT JOIN jobs j ON j.id=da.job_id AND j.company_id=da.company_id WHERE da.id=@id", c => c.Parameters.AddWithValue("@id", id));

    private static async Task Cleanup(Database db, long company)
    {
        foreach (var sql in new[] { "DELETE FROM dispatch_proof_artifacts WHERE company_id=@c", "DELETE FROM dispatch_proofs WHERE company_id=@c", "DELETE FROM dispatch_exceptions WHERE company_id=@c", "DELETE FROM audit_logs WHERE company_id=@c", "DELETE FROM outbox_messages WHERE tenant_id=@c", "DELETE FROM dispatch_assignments WHERE company_id=@c", "DELETE FROM jobs WHERE company_id=@c", "DELETE FROM vehicles WHERE company_id=@c", "DELETE FROM drivers WHERE company_id=@c", "DELETE FROM users WHERE company_id=@c", "DELETE FROM branches WHERE company_id=@c", "DELETE FROM companies WHERE id=@c" })
            await db.ExecuteAsync(sql, c => c.Parameters.AddWithValue("@c", company));
    }

    private sealed record SeedData(long CompanyId, long BranchId, long OtherBranchId, long UserId, long DriverId, long VehicleId, long JobId);
}
