using System.Reflection;
using Microsoft.Extensions.Configuration;
using Opstrax.Api.Controllers;
using Opstrax.Api.Data;

namespace Opstrax.Tests;

// TelemetryIdentityResolution.cs (ResolveTelemetryIdentityAsync) had zero behavioural tests despite
// deciding which vehicle/driver a GPS fix is attributed to. The method is `private static` and returns
// a `private` nested record, so -- exactly like TelemetryLineagePostgresTests reflecting into other
// private EndpointMappings members -- it is invoked here via reflection, the returned Task is awaited
// as its non-generic base, and the record's public (but unnameable outside the class) properties are
// read back through reflection too.
[Trait("Category", "Integration")]
[Collection("fleet-identity-schema")]
public sealed class TelemetryIdentityResolutionPostgresTests
{
    [Fact]
    public async Task ResolvesInstallationVehicleAssignmentTripAndDriverWhenUnambiguous()
    {
        var db = Db();
        var companyId = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() + Random.Shared.Next(7_000_000, 7_150_000);
        await Company(db, companyId, "HAPPY");
        try
        {
            var branch = await Branch(db, companyId, "HAPPY");
            var vehicleId = await Vehicle(db, companyId, branch, $"HAPPY-{companyId}", vin: "1HGCM82633A004352");
            var driverId = await db.InsertAsync(
                "INSERT INTO drivers(company_id,branch_id,driver_code,full_name,status,safety_score) VALUES (@c,@b,@code,'Resolver Test Driver','Available',100)",
                c => { c.Parameters.AddWithValue("@c", companyId); c.Parameters.AddWithValue("@b", branch); c.Parameters.AddWithValue("@code", $"HAPPY-DRV-{companyId}"); });
            var deviceId = await Device(db, companyId, $"HAPPY-SERIAL-{companyId}");
            var attributionTime = DateTimeOffset.UtcNow.AddMinutes(-10);
            var installationId = await Installation(db, companyId, branch, deviceId, vehicleId, attributionTime.AddHours(-2));
            const long tripId = 5_551_001;
            var assignmentId = await db.InsertAsync(
                @"INSERT INTO dispatch_assignments(company_id,branch_id,vehicle_id,driver_id,trip_id,assignment_status,status,assigned_at)
                  VALUES (@c,@b,@v,@d,@t,'in_transit','In Transit',@at)",
                c =>
                {
                    c.Parameters.AddWithValue("@c", companyId); c.Parameters.AddWithValue("@b", branch);
                    c.Parameters.AddWithValue("@v", vehicleId); c.Parameters.AddWithValue("@d", driverId);
                    c.Parameters.AddWithValue("@t", tripId); c.Parameters.AddWithValue("@at", attributionTime.AddHours(-1));
                });

            var result = await ResolveIdentity(db, companyId, deviceId, attributionTime);

            Assert.NotNull(result);
            Assert.Equal(installationId, Convert.ToInt64(Field(result, "InstallationId")));
            Assert.Equal(vehicleId, Convert.ToInt64(Field(result, "VehicleId")));
            Assert.Equal(assignmentId, Convert.ToInt64(Field(result, "AssignmentId")));
            Assert.Equal(tripId, Convert.ToInt64(Field(result, "TripId")));
            Assert.Equal(driverId, Convert.ToInt64(Field(result, "DriverId")));
            Assert.Equal(branch, Convert.ToInt64(Field(result, "VehicleBranchId")));
            Assert.True(Convert.ToBoolean(Field(result, "IsCurrentInstallation")));
        }
        finally
        {
            foreach (var sql in new[]
            {
                "DELETE FROM dispatch_assignments WHERE company_id=@c", "DELETE FROM device_installations WHERE company_id=@c",
                "DELETE FROM eld_devices WHERE company_id=@c", "DELETE FROM drivers WHERE company_id=@c",
                "DELETE FROM vehicles WHERE company_id=@c", "DELETE FROM branches WHERE company_id=@c",
                "DELETE FROM companies WHERE id=@c"
            }) await db.ExecuteAsync(sql, c => c.Parameters.AddWithValue("@c", companyId));
        }
    }

    [Fact]
    public async Task ReturnsNullWhenDeviceInstallationsOverlapForTheSameDevice()
    {
        var db = Db();
        var companyId = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() + Random.Shared.Next(7_200_000, 7_350_000);
        await Company(db, companyId, "OVERLAP-INSTALL");
        try
        {
            var branch = await Branch(db, companyId, "OVERLAP-INSTALL");
            var vehicleA = await Vehicle(db, companyId, branch, $"OVERLAP-A-{companyId}", vin: "1HGCM82633A004352");
            var vehicleB = await Vehicle(db, companyId, branch, $"OVERLAP-B-{companyId}", alternate: $"OVERLAP-MFG-{companyId}");
            var deviceId = await Device(db, companyId, $"OVERLAP-SERIAL-{companyId}");
            var attributionTime = DateTimeOffset.UtcNow.AddMinutes(-30);
            var first = await Installation(db, companyId, branch, deviceId, vehicleA, attributionTime.AddHours(-2));

            var beforeOverlap = await ResolveIdentity(db, companyId, deviceId, attributionTime);
            Assert.NotNull(beforeOverlap);
            Assert.Equal(first, Convert.ToInt64(Field(beforeOverlap, "InstallationId")));

            // ex_stage80_device_installation_period (company_id,device_id,effective_period) makes a
            // second overlapping installation for one device unreachable through the governed
            // install/transfer API -- see FleetIdentityInstallationPostgresTests.
            // RemovedHistoryRetainsDeviceAndPrimaryVehicleRoleNonOverlap, which proves the database
            // rejects exactly this overlap. Drop the constraint for the single INSERT that manufactures
            // the corrupted state so the resolver's own defensive `installations.Count != 1` guard can
            // be exercised, then restore it immediately -- mirroring how
            // FleetIdentityInstallationPostgresTests/DetentionApprovalPostgresTests temporarily lift a
            // database guard to build a fixture and always restore it in a finally block.
            await db.ExecuteAsync("ALTER TABLE device_installations DROP CONSTRAINT ex_stage80_device_installation_period");
            object? result;
            try
            {
                await Installation(db, companyId, branch, deviceId, vehicleB, attributionTime.AddHours(-1));
                result = await ResolveIdentity(db, companyId, deviceId, attributionTime);
            }
            finally
            {
                // Remove the deliberately corrupt overlap before recreating the
                // exclusion constraint. PostgreSQL validates all existing rows
                // when ADD CONSTRAINT runs, so restoring first would necessarily
                // fail and leave the shared integration database unprotected.
                await db.ExecuteAsync(
                    "DELETE FROM device_installations WHERE company_id=@c",
                    c => c.Parameters.AddWithValue("@c", companyId));
                await db.ExecuteAsync(
                    @"ALTER TABLE device_installations ADD CONSTRAINT ex_stage80_device_installation_period
                      EXCLUDE USING gist (company_id WITH =,device_id WITH =,effective_period WITH &&)
                      WHERE (status IN ('Installed','Verified','Removed'))");
            }

            Assert.Null(result);
        }
        finally
        {
            foreach (var sql in new[]
            {
                "DELETE FROM device_installations WHERE company_id=@c", "DELETE FROM eld_devices WHERE company_id=@c",
                "DELETE FROM vehicles WHERE company_id=@c", "DELETE FROM branches WHERE company_id=@c",
                "DELETE FROM companies WHERE id=@c"
            }) await db.ExecuteAsync(sql, c => c.Parameters.AddWithValue("@c", companyId));
        }
    }

    [Fact]
    public async Task ReturnsNullWhenDispatchAssignmentsOverlapForTheVehicle()
    {
        var db = Db();
        var companyId = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() + Random.Shared.Next(7_400_000, 7_550_000);
        await Company(db, companyId, "OVERLAP-ASSIGN");
        try
        {
            var branch = await Branch(db, companyId, "OVERLAP-ASSIGN");
            var vehicleId = await Vehicle(db, companyId, branch, $"OVERLAP-ASSIGN-{companyId}", vin: "1HGCM82633A004352");
            var deviceId = await Device(db, companyId, $"OVERLAP-ASSIGN-SERIAL-{companyId}");
            var now = DateTimeOffset.UtcNow;
            var attributionTime = now.AddMinutes(-90);
            await Installation(db, companyId, branch, deviceId, vehicleId, now.AddMinutes(-200));

            var firstAssignment = await db.InsertAsync(
                @"INSERT INTO dispatch_assignments(company_id,branch_id,vehicle_id,assignment_status,status,assigned_at,actual_delivery_at)
                  VALUES (@c,@b,@v,'delivered','Delivered',@from,@to)",
                c =>
                {
                    c.Parameters.AddWithValue("@c", companyId); c.Parameters.AddWithValue("@b", branch); c.Parameters.AddWithValue("@v", vehicleId);
                    c.Parameters.AddWithValue("@from", now.AddMinutes(-150)); c.Parameters.AddWithValue("@to", now.AddMinutes(-45));
                });

            var beforeOverlap = await ResolveIdentity(db, companyId, deviceId, attributionTime);
            Assert.NotNull(beforeOverlap);
            Assert.Equal(firstAssignment, Convert.ToInt64(Field(beforeOverlap, "AssignmentId")));

            // uq_da_active_vehicle only guards non-terminal assignment_status rows, so two 'delivered'
            // rows with overlapping [assigned_at,actual_delivery_at) windows are a legitimate,
            // database-reachable data-quality state (e.g. a relay handoff recorded without properly
            // closing the first leg) -- unlike the installations case above, no constraint needs to be
            // bypassed to build this fixture.
            await db.InsertAsync(
                @"INSERT INTO dispatch_assignments(company_id,branch_id,vehicle_id,assignment_status,status,assigned_at,actual_delivery_at)
                  VALUES (@c,@b,@v,'delivered','Delivered',@from,@to)",
                c =>
                {
                    c.Parameters.AddWithValue("@c", companyId); c.Parameters.AddWithValue("@b", branch); c.Parameters.AddWithValue("@v", vehicleId);
                    c.Parameters.AddWithValue("@from", now.AddMinutes(-120)); c.Parameters.AddWithValue("@to", now.AddMinutes(-15));
                });

            var result = await ResolveIdentity(db, companyId, deviceId, attributionTime);
            Assert.Null(result);
        }
        finally
        {
            foreach (var sql in new[]
            {
                "DELETE FROM dispatch_assignments WHERE company_id=@c", "DELETE FROM device_installations WHERE company_id=@c",
                "DELETE FROM eld_devices WHERE company_id=@c", "DELETE FROM vehicles WHERE company_id=@c",
                "DELETE FROM branches WHERE company_id=@c", "DELETE FROM companies WHERE id=@c"
            }) await db.ExecuteAsync(sql, c => c.Parameters.AddWithValue("@c", companyId));
        }
    }

    [Theory]
    [InlineData("Suspended")]
    [InlineData("Quarantined")]
    public async Task ReturnsNullForSuspendedOrQuarantinedDevice(string deviceState)
    {
        var db = Db();
        var companyId = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() + Random.Shared.Next(7_600_000, 7_750_000);
        await Company(db, companyId, $"STATE-{deviceState}");
        try
        {
            var deviceId = await Device(db, companyId, $"STATE-{deviceState}-{companyId}", deviceState: deviceState);

            var result = await ResolveIdentity(db, companyId, deviceId, DateTimeOffset.UtcNow);

            Assert.Null(result);
        }
        finally
        {
            await db.ExecuteAsync("DELETE FROM eld_devices WHERE company_id=@c", c => c.Parameters.AddWithValue("@c", companyId));
            await db.ExecuteAsync("DELETE FROM companies WHERE id=@c", c => c.Parameters.AddWithValue("@c", companyId));
        }
    }

    private static async Task Company(Database db, long id, string suffix) => await db.ExecuteAsync(
        "INSERT INTO companies(id,company_code,name,industry) OVERRIDING SYSTEM VALUE VALUES (@c,@code,@name,'transport')",
        c => { c.Parameters.AddWithValue("@c", id); c.Parameters.AddWithValue("@code", $"IDENT-{id}-{suffix}"); c.Parameters.AddWithValue("@name", $"Identity resolution tenant {suffix}"); });

    private static Task<long> Branch(Database db, long company, string suffix) => db.InsertAsync(
        "INSERT INTO branches(company_id,branch_code,name,status) VALUES (@c,@code,@name,'Active')",
        c => { c.Parameters.AddWithValue("@c", company); c.Parameters.AddWithValue("@code", $"BR-{company}-{suffix}"); c.Parameters.AddWithValue("@name", $"Branch {suffix}"); });

    private static Task<long> Vehicle(Database db, long company, long branch, string code, string? vin = null, string? alternate = null) => db.InsertAsync(
        @"INSERT INTO vehicles(company_id,branch_id,vehicle_code,type,vin,vin_exception_type,alternate_identifier,status,availability_status,out_of_service)
          VALUES (@c,@b,@code,'Truck',@vin,@exception,@alternate,'Available','available',FALSE)",
        c =>
        {
            c.Parameters.AddWithValue("@c", company); c.Parameters.AddWithValue("@b", branch); c.Parameters.AddWithValue("@code", code);
            c.Parameters.AddWithValue("@vin", (object?)vin ?? DBNull.Value);
            c.Parameters.AddWithValue("@exception", alternate is null ? DBNull.Value : "manufacturer-serial-number");
            c.Parameters.AddWithValue("@alternate", (object?)alternate ?? DBNull.Value);
        });

    private static Task<long> Device(Database db, long company, string serial, string deviceState = "Registered", string status = "Active") => db.InsertAsync(
        @"INSERT INTO eld_devices(company_id,device_serial,status,device_state,api_key_hash,hmac_secret_encrypted,hmac_key_version,created_at)
          VALUES (@c,@serial,@status,@state,encode(sha256(@serial::bytea),'hex'),repeat('b',32),1,NOW())",
        c => { c.Parameters.AddWithValue("@c", company); c.Parameters.AddWithValue("@serial", serial); c.Parameters.AddWithValue("@status", status); c.Parameters.AddWithValue("@state", deviceState); });

    private static Task<long> Installation(Database db, long company, long branch, long device, long vehicle, DateTimeOffset from, string role = "GPS") => db.InsertAsync(
        @"INSERT INTO device_installations(company_id,branch_id,device_id,vehicle_id,status,device_role,is_primary,effective_from,installed_at,source)
          VALUES (@c,@b,@d,@v,'Installed',@role,TRUE,@from,@from,'test')",
        c =>
        {
            c.Parameters.AddWithValue("@c", company); c.Parameters.AddWithValue("@b", branch); c.Parameters.AddWithValue("@d", device);
            c.Parameters.AddWithValue("@v", vehicle); c.Parameters.AddWithValue("@from", from); c.Parameters.AddWithValue("@role", role);
        });

    private static Database Db() => new(new ConfigurationBuilder().AddInMemoryCollection(
        new Dictionary<string, string?> { ["ConnectionStrings:DefaultConnection"] = TestDb.ConnectionString }).Build());

    // ResolveTelemetryIdentityAsync is `private static` and returns `Task<TelemetryIdentityContext?>`
    // where TelemetryIdentityContext is itself a `private` nested record -- unnameable from this
    // assembly. Invoke it by reflection, await the boxed Task as its non-generic base (no need to name
    // the closed generic type to do that), then read `Result` off the completed task by reflection too.
    private static async Task<object?> ResolveIdentity(Database db, long companyId, long deviceId, DateTimeOffset attributionTime)
    {
        var method = typeof(EndpointMappings).GetMethod("ResolveTelemetryIdentityAsync", BindingFlags.NonPublic | BindingFlags.Static)
            ?? throw new InvalidOperationException("Missing ResolveTelemetryIdentityAsync");
        try
        {
            var task = (Task)method.Invoke(null, new object?[] { db, companyId, deviceId, attributionTime, CancellationToken.None })!;
            await task.ConfigureAwait(false);
            return task.GetType().GetProperty("Result")!.GetValue(task);
        }
        catch (TargetInvocationException ex) when (ex.InnerException is not null)
        {
            System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(ex.InnerException).Throw();
            throw;
        }
    }

    // TelemetryIdentityContext's positional-record properties are public get accessors even though the
    // record type itself is private, so plain reflection (no NonPublic flag) reads them back fine.
    private static object? Field(object? record, string name) => record?.GetType().GetProperty(name)?.GetValue(record);
}
