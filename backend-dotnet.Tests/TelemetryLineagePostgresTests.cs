using System.Collections;
using System.Reflection;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Opstrax.Api.Controllers;
using Opstrax.Api.Data;

namespace Opstrax.Tests;

[Trait("Category", "Integration")]
public sealed class TelemetryLineagePostgresTests
{
    [Fact]
    public async Task BreadcrumbApi_ReturnsPersistedPerEventOperationalLineage()
    {
        var db = Db();
        var suffix = Guid.NewGuid().ToString("N")[..10];
        var companyId = await db.InsertAsync(
            "INSERT INTO companies(company_code,name,industry) VALUES (@code,'Breadcrumb lineage test','Transportation')",
            c => c.Parameters.AddWithValue("@code", $"BREAD-{suffix}"));
        try
        {
            var branchId = await db.InsertAsync(
                "INSERT INTO branches(company_id,branch_code,name,status) VALUES (@c,'MAIN','Main','Active')",
                c => c.Parameters.AddWithValue("@c", companyId));
            var vehicleId = await db.InsertAsync(
                @"INSERT INTO vehicles(company_id,branch_id,vehicle_code,type,vin_exception_type,alternate_identifier,status)
                  VALUES (@c,@b,@code,'Truck','legacy-fleet-identifier',@code,'Available')",
                c => { c.Parameters.AddWithValue("@c", companyId); c.Parameters.AddWithValue("@b", branchId); c.Parameters.AddWithValue("@code", $"V-{suffix}"); });
            const long assignmentId = 730001;
            const long tripId = 730002;
            const long driverId = 730003;
            var eventId = await db.InsertAsync(
                @"INSERT INTO location_events(company_id,vehicle_id,driver_id,assignment_id,trip_id,lat,lng,event_time,source)
                  VALUES (@c,@v,@d,@a,@t,40.7128,-74.0060,NOW(),'device')",
                c =>
                {
                    c.Parameters.AddWithValue("@c", companyId); c.Parameters.AddWithValue("@v", vehicleId);
                    c.Parameters.AddWithValue("@d", driverId); c.Parameters.AddWithValue("@a", assignmentId);
                    c.Parameters.AddWithValue("@t", tripId);
                });
            var http = new DefaultHttpContext();
            http.Request.QueryString = new QueryString($"?vehicleId={vehicleId}");
            http.Items[EndpointMappings.AuthCompanyIdItemKey] = companyId;
            http.Items[EndpointMappings.AuthBranchIdItemKey] = branchId;
            http.Items[EndpointMappings.AuthUserIdItemKey] = 1L;
            http.Items[EndpointMappings.AuthRoleItemKey] = "Telemetry lineage tester";
            http.Items[EndpointMappings.AuthPermissionsItemKey] = new[] { "telemetry.live_state.read" };

            var method = typeof(EndpointMappings).GetMethod("TelemetryBreadcrumbs", BindingFlags.NonPublic | BindingFlags.Static)!;
            var result = await Invoke(method, http, db, CancellationToken.None);

            Assert.Equal(StatusCodes.Status200OK, Assert.IsAssignableFrom<IStatusCodeHttpResult>(result).StatusCode);
            var envelope = Assert.IsAssignableFrom<IValueHttpResult>(result).Value!;
            var data = envelope.GetType().GetProperty("Data")!.GetValue(envelope)!;
            var points = Assert.IsAssignableFrom<IEnumerable>(data.GetType().GetProperty("points", BindingFlags.IgnoreCase | BindingFlags.Public | BindingFlags.Instance)!.GetValue(data));
            var point = Assert.IsType<Dictionary<string, object?>>(Assert.Single(points.Cast<object>()));
            Assert.Equal(eventId, Convert.ToInt64(point["id"]));
            Assert.Equal(assignmentId, Convert.ToInt64(point["assignmentId"]));
            Assert.Equal(tripId, Convert.ToInt64(point["tripId"]));
            Assert.Equal(driverId, Convert.ToInt64(point["driverId"]));
        }
        finally
        {
            await db.ExecuteAsync("DELETE FROM location_events WHERE company_id=@c", c => c.Parameters.AddWithValue("@c", companyId));
            await db.ExecuteAsync("DELETE FROM vehicles WHERE company_id=@c", c => c.Parameters.AddWithValue("@c", companyId));
            await db.ExecuteAsync("DELETE FROM branches WHERE company_id=@c", c => c.Parameters.AddWithValue("@c", companyId));
            await db.ExecuteAsync("DELETE FROM companies WHERE id=@c", c => c.Parameters.AddWithValue("@c", companyId));
        }
    }

    private static async Task<IResult> Invoke(MethodInfo method, params object[] args)
    {
        try { return await (Task<IResult>)method.Invoke(null, args)!; }
        catch (TargetInvocationException ex) when (ex.InnerException is not null)
        { System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(ex.InnerException).Throw(); throw; }
    }

    private static Database Db() => new(new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
    {
        ["ConnectionStrings:DefaultConnection"] = TestDb.ConnectionString,
        ["Rls:EnforceTenantContext"] = "false"
    }).Build());
}
