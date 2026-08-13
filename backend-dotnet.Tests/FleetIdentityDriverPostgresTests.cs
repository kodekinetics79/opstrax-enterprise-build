using System.Reflection;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Npgsql;
using Opstrax.Api.Controllers;
using Opstrax.Api.Data;
using Opstrax.Api.Services;

namespace Opstrax.Tests;

[Trait("Category", "Integration")]
public sealed class FleetIdentityDriverPostgresTests
{
    [Fact]
    public async Task DriverSwapClosesOldAssignmentAndCreatesAuditedEffectiveDatedReplacement()
    {
        var db = Db();
        await new DispatchSchemaService(db, NullLogger<DispatchSchemaService>.Instance).EnsureAsync();
        var companyId = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() + Random.Shared.Next(100_000, 900_000);
        await db.ExecuteAsync(
            "INSERT INTO companies(id,company_code,name,industry) OVERRIDING SYSTEM VALUE VALUES (@c,@code,'Driver swap test','transport')",
            c => { c.Parameters.AddWithValue("@c", companyId); c.Parameters.AddWithValue("@code", $"SWAP-{companyId}"); });
        try
        {
            var branchId = await db.InsertAsync(
                "INSERT INTO branches(company_id,branch_code,name,status) VALUES (@c,@code,'Swap branch','Active')",
                c => { c.Parameters.AddWithValue("@c", companyId); c.Parameters.AddWithValue("@code", $"SWAP-BR-{companyId}"); });
            var actorId = await db.InsertAsync(
                "INSERT INTO users(company_id,branch_id,full_name,email,role_name,status) VALUES (@c,@b,'Dispatcher',@email,'Dispatcher','Active')",
                c => { c.Parameters.AddWithValue("@c", companyId); c.Parameters.AddWithValue("@b", branchId); c.Parameters.AddWithValue("@email", $"dispatcher-{companyId}@invalid.example"); });
            var firstDriver = await db.InsertAsync(
                "INSERT INTO drivers(company_id,branch_id,driver_code,full_name,status,safety_score) VALUES (@c,@b,@code,'First Driver','Available',100)",
                c => { c.Parameters.AddWithValue("@c", companyId); c.Parameters.AddWithValue("@b", branchId); c.Parameters.AddWithValue("@code", $"SWAP-A-{companyId}"); });
            var replacementDriver = await db.InsertAsync(
                "INSERT INTO drivers(company_id,branch_id,driver_code,full_name,status,safety_score) VALUES (@c,@b,@code,'Replacement Driver','Available',100)",
                c => { c.Parameters.AddWithValue("@c", companyId); c.Parameters.AddWithValue("@b", branchId); c.Parameters.AddWithValue("@code", $"SWAP-B-{companyId}"); });
            var vehicleId = await db.InsertAsync(
                @"INSERT INTO vehicles(company_id,branch_id,vehicle_code,type,vin,status,availability_status,out_of_service)
                  VALUES (@c,@b,@code,'Truck','1HGCM82633A004352','Available','available',FALSE)",
                c => { c.Parameters.AddWithValue("@c", companyId); c.Parameters.AddWithValue("@b", branchId); c.Parameters.AddWithValue("@code", $"SWAP-UNIT-{companyId}"); });
            var assignmentId = await db.InsertAsync(
                @"INSERT INTO dispatch_assignments(company_id,branch_id,vehicle_id,driver_id,assignment_status,status,assigned_at,accepted_at)
                  VALUES (@c,@b,@v,@d,'accepted','Accepted',NOW()-INTERVAL '1 hour',NOW()-INTERVAL '55 minutes')",
                c => { c.Parameters.AddWithValue("@c", companyId); c.Parameters.AddWithValue("@b", branchId); c.Parameters.AddWithValue("@v", vehicleId); c.Parameters.AddWithValue("@d", firstDriver); });

            var http = DispatcherPrincipal(companyId, branchId, actorId);
            var result = await Invoke("DispatchAssignmentSwapDriver", assignmentId, http,
                NestedBody("DispatchDriverSwapBody", replacementDriver, "scheduled relief"),
                db, new AuditService(db), new NotificationService(db), CancellationToken.None);
            Assert.Equal(StatusCodes.Status201Created, Status(result));

            var rows = await db.QueryAsync(
                @"SELECT id,driver_id,assignment_status,assigned_at,cancelled_at,supersedes_assignment_id,driver_change_reason
                  FROM dispatch_assignments WHERE company_id=@c AND vehicle_id=@v ORDER BY assigned_at,id",
                c => { c.Parameters.AddWithValue("@c", companyId); c.Parameters.AddWithValue("@v", vehicleId); });
            Assert.Equal(2, rows.Count);
            Assert.Equal(firstDriver, Convert.ToInt64(rows[0]["driverId"]));
            Assert.Equal("cancelled", rows[0]["assignmentStatus"]);
            Assert.Equal(replacementDriver, Convert.ToInt64(rows[1]["driverId"]));
            Assert.Equal("assigned", rows[1]["assignmentStatus"]);
            Assert.Equal(assignmentId, Convert.ToInt64(rows[1]["supersedesAssignmentId"]));
            Assert.Equal(rows[0]["cancelledAt"], rows[1]["assignedAt"]);
            Assert.Equal(2, await db.ScalarLongAsync(
                "SELECT COUNT(*) FROM audit_logs WHERE company_id=@c AND action_name IN ('dispatch.assignment.driver_released','dispatch.assignment.driver_swapped')",
                c => c.Parameters.AddWithValue("@c", companyId)));
        }
        finally
        {
            foreach (var sql in new[]
            {
                "DELETE FROM notification_recipients WHERE notification_id IN (SELECT id FROM notifications WHERE company_id=@c)",
                "DELETE FROM notifications WHERE company_id=@c", "DELETE FROM audit_logs WHERE company_id=@c",
                "DELETE FROM dispatch_assignments WHERE company_id=@c", "DELETE FROM drivers WHERE company_id=@c",
                "DELETE FROM vehicles WHERE company_id=@c", "DELETE FROM users WHERE company_id=@c",
                "DELETE FROM branches WHERE company_id=@c", "DELETE FROM companies WHERE id=@c"
            }) await db.ExecuteAsync(sql, c => c.Parameters.AddWithValue("@c", companyId));
        }
    }

    [Fact]
    public async Task DriverMustConfirmExactVehicleAndSubmitSafePretripBeforeDeparture()
    {
        var db = Db();
        var companyId = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() + Random.Shared.Next(10_000, 90_000);
        await db.ExecuteAsync(
            "INSERT INTO companies(id,company_code,name,industry) OVERRIDING SYSTEM VALUE VALUES (@c,@code,'Driver identity test','transport')",
            c => { c.Parameters.AddWithValue("@c", companyId); c.Parameters.AddWithValue("@code", $"DID-{companyId}"); });
        try
        {
            var branchId = await db.InsertAsync(
                "INSERT INTO branches(company_id,branch_code,name,status) VALUES (@c,@code,'Driver branch','Active')",
                c => { c.Parameters.AddWithValue("@c", companyId); c.Parameters.AddWithValue("@code", $"BR-{companyId}"); });
            var userId = await db.InsertAsync(
                "INSERT INTO users(company_id,branch_id,full_name,email,role_name,status) VALUES (@c,@b,'Pilot Driver',@email,'Driver','Active')",
                c => { c.Parameters.AddWithValue("@c", companyId); c.Parameters.AddWithValue("@b", branchId); c.Parameters.AddWithValue("@email", $"driver-{companyId}@invalid.example"); });
            var driverId = await db.InsertAsync(
                "INSERT INTO drivers(company_id,branch_id,user_id,driver_code,full_name,status) VALUES (@c,@b,@u,@code,'Pilot Driver','Available')",
                c => { c.Parameters.AddWithValue("@c", companyId); c.Parameters.AddWithValue("@b", branchId); c.Parameters.AddWithValue("@u", userId); c.Parameters.AddWithValue("@code", $"DRV-{companyId}"); });
            var vehicleCode = $"UNIT-{companyId}";
            var vehicleId = await db.InsertAsync(
                @"INSERT INTO vehicles(company_id,branch_id,vehicle_code,type,vin,status,availability_status,out_of_service)
                  VALUES (@c,@b,@code,'Truck','1HGCM82633A004352','Available','available',FALSE)",
                c => { c.Parameters.AddWithValue("@c", companyId); c.Parameters.AddWithValue("@b", branchId); c.Parameters.AddWithValue("@code", vehicleCode); });
            var assignmentId = await db.InsertAsync(
                @"INSERT INTO dispatch_assignments(company_id,branch_id,vehicle_id,driver_id,assignment_status,status,assigned_at,accepted_at)
                  VALUES (@c,@b,@v,@d,'accepted','Accepted',NOW(),NOW())",
                c => { c.Parameters.AddWithValue("@c", companyId); c.Parameters.AddWithValue("@b", branchId); c.Parameters.AddWithValue("@v", vehicleId); c.Parameters.AddWithValue("@d", driverId); });
            var http = Principal(companyId, branchId, userId);
            var audit = new AuditService(db);

            var wrong = await Invoke("DriverConfirmVehicle", http, assignmentId,
                NestedBody("DriverVehicleConfirmationBody", "unit_suffix", "WRONG"), db, audit, CancellationToken.None);
            Assert.Equal(StatusCodes.Status422UnprocessableEntity, Status(wrong));

            var suffix = vehicleCode[^4..];
            var confirmed = await Invoke("DriverConfirmVehicle", http, assignmentId,
                NestedBody("DriverVehicleConfirmationBody", "unit_suffix", suffix), db, audit, CancellationToken.None);
            Assert.Equal(StatusCodes.Status200OK, Status(confirmed));

            var blocked = await Invoke("DriverUpdateStatus", http, assignmentId,
                NestedBody("DriverStatusBody", "en_route_pickup", null), db, audit, CancellationToken.None);
            Assert.Equal(StatusCodes.Status409Conflict, Status(blocked));

            await db.InsertAsync(
                @"INSERT INTO dvir_reports
                    (company_id,branch_id,report_number,driver_id,vehicle_id,inspection_type,inspection_status,
                     defects_found,safe_to_operate,driver_signature_status,submitted_at)
                  VALUES (@c,@b,@number,@d,@v,'pre_trip','submitted',0,TRUE,'Pending',NOW())",
                c =>
                {
                    c.Parameters.AddWithValue("@c", companyId); c.Parameters.AddWithValue("@b", branchId);
                    c.Parameters.AddWithValue("@number", $"DVIR-UNSIGNED-{companyId}"); c.Parameters.AddWithValue("@d", driverId);
                    c.Parameters.AddWithValue("@v", vehicleId);
                });
            var unsignedBlocked = await Invoke("DriverUpdateStatus", http, assignmentId,
                NestedBody("DriverStatusBody", "en_route_pickup", null), db, audit, CancellationToken.None);
            Assert.Equal(StatusCodes.Status409Conflict, Status(unsignedBlocked));

            var dvirId = await db.InsertAsync(
                @"INSERT INTO dvir_reports
                    (company_id,branch_id,report_number,driver_id,vehicle_id,inspection_type,inspection_status,
                     defects_found,safe_to_operate,driver_signature_status,submitted_at)
                  VALUES (@c,@b,@number,@d,@v,'pre_trip','submitted',0,TRUE,'Signed',NOW())",
                c =>
                {
                    c.Parameters.AddWithValue("@c", companyId); c.Parameters.AddWithValue("@b", branchId);
                    c.Parameters.AddWithValue("@number", $"DVIR-{companyId}"); c.Parameters.AddWithValue("@d", driverId);
                    c.Parameters.AddWithValue("@v", vehicleId);
                });
            var newerUnsafeDvirId = await db.InsertAsync(
                @"INSERT INTO dvir_reports
                    (company_id,branch_id,report_number,driver_id,vehicle_id,inspection_type,inspection_status,
                     defects_found,safe_to_operate,driver_signature_status,submitted_at)
                  VALUES (@c,@b,@number,@d,@v,'pre_trip','submitted',1,FALSE,'Signed',NOW()+INTERVAL '1 second') RETURNING id",
                c =>
                {
                    c.Parameters.AddWithValue("@c", companyId); c.Parameters.AddWithValue("@b", branchId);
                    c.Parameters.AddWithValue("@number", $"DVIR-UNSAFE-{companyId}"); c.Parameters.AddWithValue("@d", driverId);
                    c.Parameters.AddWithValue("@v", vehicleId);
                });
            var supersededSafeBlocked = await Invoke("DriverUpdateStatus", http, assignmentId,
                NestedBody("DriverStatusBody", "en_route_pickup", null), db, audit, CancellationToken.None);
            Assert.Equal(StatusCodes.Status409Conflict, Status(supersededSafeBlocked));
            await db.ExecuteAsync("DELETE FROM dvir_reports WHERE id=@id AND company_id=@c",
                c => { c.Parameters.AddWithValue("@id", newerUnsafeDvirId); c.Parameters.AddWithValue("@c", companyId); });

            await using (var blocker = new NpgsqlConnection(TestDb.ConnectionString))
            {
                await blocker.OpenAsync();
                await using var blockerTx = await blocker.BeginTransactionAsync();
                await using (var lockCommand = new NpgsqlCommand(
                    "SELECT pg_advisory_xact_lock(hashtextextended(@key,0))", blocker, blockerTx))
                {
                    lockCommand.Parameters.AddWithValue("@key", $"fleet-departure-safety:{companyId}:{vehicleId}:{driverId}");
                    await lockCommand.ExecuteNonQueryAsync();
                }

                var concurrentDeparture = Invoke("DriverUpdateStatus", http, assignmentId,
                    NestedBody("DriverStatusBody", "en_route_pickup", null), db, audit, CancellationToken.None);
                await Task.Delay(150);
                await using (var unsafeInsert = new NpgsqlCommand(
                    @"INSERT INTO dvir_reports
                        (company_id,branch_id,report_number,driver_id,vehicle_id,inspection_type,inspection_status,
                         defects_found,safe_to_operate,driver_signature_status,submitted_at)
                      VALUES (@c,@b,@number,@d,@v,'pre_trip','submitted',1,FALSE,'Signed',NOW()+INTERVAL '2 seconds')", blocker, blockerTx))
                {
                    unsafeInsert.Parameters.AddWithValue("@c", companyId); unsafeInsert.Parameters.AddWithValue("@b", branchId);
                    unsafeInsert.Parameters.AddWithValue("@number", $"DVIR-CONCURRENT-UNSAFE-{companyId}");
                    unsafeInsert.Parameters.AddWithValue("@d", driverId); unsafeInsert.Parameters.AddWithValue("@v", vehicleId);
                    await unsafeInsert.ExecuteNonQueryAsync();
                }
                await blockerTx.CommitAsync();
                Assert.Equal(StatusCodes.Status409Conflict, Status(await concurrentDeparture));
            }
            await db.ExecuteAsync("DELETE FROM dvir_reports WHERE company_id=@c AND report_number=@number",
                c => { c.Parameters.AddWithValue("@c", companyId); c.Parameters.AddWithValue("@number", $"DVIR-CONCURRENT-UNSAFE-{companyId}"); });

            await db.ExecuteAsync("UPDATE vehicles SET out_of_service=TRUE WHERE id=@id AND company_id=@c",
                c => { c.Parameters.AddWithValue("@id", vehicleId); c.Parameters.AddWithValue("@c", companyId); });
            var outOfServiceBlocked = await Invoke("DriverUpdateStatus", http, assignmentId,
                NestedBody("DriverStatusBody", "en_route_pickup", null), db, audit, CancellationToken.None);
            Assert.Equal(StatusCodes.Status409Conflict, Status(outOfServiceBlocked));
            await db.ExecuteAsync("UPDATE vehicles SET out_of_service=FALSE WHERE id=@id AND company_id=@c",
                c => { c.Parameters.AddWithValue("@id", vehicleId); c.Parameters.AddWithValue("@c", companyId); });

            var departed = await Invoke("DriverUpdateStatus", http, assignmentId,
                NestedBody("DriverStatusBody", "en_route_pickup", null), db, audit, CancellationToken.None);
            Assert.Equal(StatusCodes.Status200OK, Status(departed));
            var stored = await db.QuerySingleAsync(
                "SELECT assignment_status,vehicle_confirmed_by_driver_id,pretrip_dvir_id,operational_started_at FROM dispatch_assignments WHERE id=@id AND company_id=@c",
                c => { c.Parameters.AddWithValue("@id", assignmentId); c.Parameters.AddWithValue("@c", companyId); });
            Assert.Equal("en_route_pickup", stored!["assignmentStatus"]);
            Assert.Equal(driverId, Convert.ToInt64(stored["vehicleConfirmedByDriverId"]));
            Assert.Equal(dvirId, Convert.ToInt64(stored["pretripDvirId"]));
            Assert.NotNull(stored["operationalStartedAt"]);
            Assert.Equal(1, await db.ScalarLongAsync(
                "SELECT COUNT(*) FROM audit_logs WHERE company_id=@c AND action_name='driver.assignment.vehicle_confirmed' AND entity_id=@id",
                c => { c.Parameters.AddWithValue("@c", companyId); c.Parameters.AddWithValue("@id", assignmentId); }));
        }
        finally
        {
            foreach (var sql in new[]
            {
                "DELETE FROM audit_logs WHERE company_id=@c", "DELETE FROM dvir_reports WHERE company_id=@c",
                "DELETE FROM dispatch_assignments WHERE company_id=@c", "DELETE FROM drivers WHERE company_id=@c",
                "DELETE FROM vehicles WHERE company_id=@c", "DELETE FROM users WHERE company_id=@c",
                "DELETE FROM branches WHERE company_id=@c", "DELETE FROM companies WHERE id=@c"
            })
                await db.ExecuteAsync(sql, c => c.Parameters.AddWithValue("@c", companyId));
        }
    }

    private static Database Db() => new(new ConfigurationBuilder().AddInMemoryCollection(
        new Dictionary<string, string?> { ["ConnectionStrings:DefaultConnection"] = TestDb.ConnectionString }).Build());

    private static DefaultHttpContext Principal(long companyId, long branchId, long userId)
    {
        var http = new DefaultHttpContext();
        http.Items[EndpointMappings.AuthCompanyIdItemKey] = companyId;
        http.Items[EndpointMappings.AuthBranchIdItemKey] = branchId;
        http.Items[EndpointMappings.AuthUserIdItemKey] = userId;
        http.Items[EndpointMappings.AuthRoleItemKey] = "Driver";
        http.Items[EndpointMappings.AuthPermissionsItemKey] = new[] { "driver:self" };
        return http;
    }

    private static DefaultHttpContext DispatcherPrincipal(long companyId, long branchId, long userId)
    {
        var http = new DefaultHttpContext();
        http.Items[EndpointMappings.AuthCompanyIdItemKey] = companyId;
        http.Items[EndpointMappings.AuthBranchIdItemKey] = branchId;
        http.Items[EndpointMappings.AuthUserIdItemKey] = userId;
        http.Items[EndpointMappings.AuthRoleItemKey] = "Dispatcher";
        http.Items[EndpointMappings.AuthPermissionsItemKey] = new[] { "dispatch:assign", "dispatch:manage" };
        return http;
    }

    private static object NestedBody(string name, params object?[] args)
    {
        var type = typeof(EndpointMappings).GetNestedType(name, BindingFlags.NonPublic)
            ?? throw new InvalidOperationException($"Missing nested body {name}");
        return Activator.CreateInstance(type, args) ?? throw new InvalidOperationException($"Unable to create {name}");
    }

    private static async Task<IResult> Invoke(string name, params object?[] args)
    {
        var method = typeof(EndpointMappings).GetMethod(name, BindingFlags.NonPublic | BindingFlags.Static)
            ?? throw new InvalidOperationException($"Missing endpoint {name}");
        var task = (Task<IResult>?)method.Invoke(null, args) ?? throw new InvalidOperationException($"Endpoint {name} did not return a task");
        return await task;
    }

    private static int? Status(IResult result) => Assert.IsAssignableFrom<IStatusCodeHttpResult>(result).StatusCode;
}
