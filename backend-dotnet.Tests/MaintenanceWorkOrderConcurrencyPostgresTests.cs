using System.Reflection;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Opstrax.Api.Controllers;
using Opstrax.Api.Data;
using Opstrax.Api.Services;

namespace Opstrax.Tests;

[Trait("Category", "Integration")]
public sealed class MaintenanceWorkOrderConcurrencyPostgresTests
{
    [Fact]
    public async Task ConcurrentOpenWorkOrderCreates_CommitExactlyOneAndRejectTheOther()
    {
        var setup = Db();
        var companyId = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() + Random.Shared.Next(2_000_000, 2_900_000);
        var vehicleId = 0L;
        var serviceType = $"Telematics diagnostic review {Guid.NewGuid():N}";
        await setup.ExecuteAsync(
            "INSERT INTO companies(id,company_code,name,industry) OVERRIDING SYSTEM VALUE VALUES (@id,@code,'Maintenance Concurrency Test','Transportation')",
            c => { c.Parameters.AddWithValue("@id", companyId); c.Parameters.AddWithValue("@code", $"MWO-{companyId}"); });
        try
        {
            vehicleId = await setup.InsertAsync(
                "INSERT INTO vehicles(company_id,vehicle_code,type,status,availability_status,out_of_service,readiness_score,risk_score) VALUES (@c,@code,'Truck','Available','available',false,95,5)",
                c => { c.Parameters.AddWithValue("@c", companyId); c.Parameters.AddWithValue("@code", $"MWO-V-{companyId}"); });

            var dbA = Db();
            var dbB = Db();
            var attempts = await Task.WhenAll(
                InvokeCreate(Principal(companyId), Body(vehicleId, serviceType), dbA, new AuditService(dbA)),
                InvokeCreate(Principal(companyId), Body(vehicleId, serviceType), dbB, new AuditService(dbB)));

            Assert.Equal(1, attempts.Count(result => Status(result) == StatusCodes.Status201Created));
            Assert.Equal(1, attempts.Count(result => Status(result) == StatusCodes.Status409Conflict));
            Assert.Equal(1, await setup.ScalarLongAsync(
                @"SELECT COUNT(*) FROM work_orders
                  WHERE company_id=@c AND vehicle_id=@v AND LOWER(issue_type)=LOWER(@s)
                    AND status NOT IN ('Completed','Cancelled','completed','cancelled') AND deleted_at IS NULL",
                c => { c.Parameters.AddWithValue("@c", companyId); c.Parameters.AddWithValue("@v", vehicleId); c.Parameters.AddWithValue("@s", serviceType); }));
        }
        finally
        {
            await setup.ExecuteAsync("DELETE FROM audit_logs WHERE company_id=@c", c => c.Parameters.AddWithValue("@c", companyId));
            await setup.ExecuteAsync("DELETE FROM work_orders WHERE company_id=@c", c => c.Parameters.AddWithValue("@c", companyId));
            if (vehicleId > 0)
                await setup.ExecuteAsync("DELETE FROM vehicles WHERE company_id=@c", c => c.Parameters.AddWithValue("@c", companyId));
            await setup.ExecuteAsync("DELETE FROM companies WHERE id=@c", c => c.Parameters.AddWithValue("@c", companyId));
        }
    }

    private static int? Status(IResult result) => Assert.IsAssignableFrom<IStatusCodeHttpResult>(result).StatusCode;

    private static async Task<IResult> InvokeCreate(HttpContext http, object body, Database db, AuditService audit)
    {
        var method = typeof(EndpointMappings).GetMethod("MaintWorkOrderCreate", BindingFlags.NonPublic | BindingFlags.Static)!;
        try
        {
            return await (Task<IResult>)method.Invoke(null, new object[] { http, body, db, audit, CancellationToken.None })!;
        }
        catch (TargetInvocationException ex) when (ex.InnerException is not null)
        {
            System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(ex.InnerException).Throw();
            throw;
        }
    }

    private static object Body(long vehicleId, string serviceType)
    {
        var type = typeof(EndpointMappings).GetNestedType("MaintWorkOrderBody", BindingFlags.NonPublic)!;
        return Activator.CreateInstance(type, BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public,
            binder: null,
            args: new object?[]
            {
                vehicleId, "Telematics follow-up", serviceType,
                "Point-in-time diagnostic evidence; revalidate before service.", "High", null, 0m,
                DateTime.UtcNow.ToString("yyyy-MM-dd")
            },
            culture: null)!;
    }

    private static DefaultHttpContext Principal(long companyId)
    {
        var http = new DefaultHttpContext();
        http.Items[EndpointMappings.AuthCompanyIdItemKey] = companyId;
        http.Items[EndpointMappings.AuthUserIdItemKey] = 42L;
        http.Items[EndpointMappings.AuthRoleItemKey] = "Tenant Admin";
        http.Items[EndpointMappings.AuthPermissionsItemKey] = new[] { "maintenance:manage" };
        return http;
    }

    private static Database Db() => new(new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
    {
        ["ConnectionStrings:DefaultConnection"] = TestDb.ConnectionString,
        ["Rls:EnforceTenantContext"] = "false"
    }).Build());
}
