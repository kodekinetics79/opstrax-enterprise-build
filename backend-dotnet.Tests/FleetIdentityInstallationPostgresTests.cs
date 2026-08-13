using System.Reflection;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Npgsql;
using Opstrax.Api.Controllers;
using Opstrax.Api.Data;
using Opstrax.Api.Services;

namespace Opstrax.Tests;

[Trait("Category", "Integration")]
public sealed class FleetIdentityInstallationPostgresTests
{
    [Fact]
    public async Task InstallationLifecycleRejectsFutureEffectiveTimesBeforeMutation()
    {
        var http = Principal(1001, userId: 42);
        var future = DateTimeOffset.UtcNow.AddMinutes(1);
        var db = Db();
        var audit = new AuditService(db);

        var create = await Invoke("DeviceInstallationCreate", http, 2001L,
            Body("DeviceInstallationCreateBody", 3001L, "GPS", true, (DateTimeOffset?)future,
                null, null, null, null, null), db, audit, CancellationToken.None);
        Assert.Equal(StatusCodes.Status400BadRequest, Status(create));

        var remove = await Invoke("DeviceInstallationRemove", http, 2001L, 4001L,
            Body("DeviceInstallationRemoveBody", "scheduled removal", (DateTimeOffset?)future, (int?)1),
            db, audit, CancellationToken.None);
        Assert.Equal(StatusCodes.Status400BadRequest, Status(remove));

        var transfer = await Invoke("DeviceInstallationTransfer", http, 2001L,
            Body("DeviceInstallationTransferBody", 3002L, (long?)4001L, "replace", "route change",
                "GPS", true, (DateTimeOffset?)future, null, null, null, (int?)1, null),
            db, audit, CancellationToken.None);
        Assert.Equal(StatusCodes.Status400BadRequest, Status(transfer));

        var unversionedRemove = await Invoke("DeviceInstallationRemove", http, 2001L, 4001L,
            Body("DeviceInstallationRemoveBody", "remove", null, null), db, audit, CancellationToken.None);
        Assert.Equal(StatusCodes.Status400BadRequest, Status(unversionedRemove));

        var unversionedTransfer = await Invoke("DeviceInstallationTransfer", http, 2001L,
            Body("DeviceInstallationTransferBody", 3002L, null, "replace", "route change",
                "GPS", true, null, null, null, null, null, null), db, audit, CancellationToken.None);
        Assert.Equal(StatusCodes.Status400BadRequest, Status(unversionedTransfer));
    }

    [Fact]
    public async Task SimultaneousInstallationsCannotBindOneDeviceToTwoVehicles()
    {
        var setupDb = Db();
        var companyId = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() + Random.Shared.Next(1_000_000, 1_900_000);
        await Company(setupDb, companyId, "RACE");
        try
        {
            var branch = await Branch(setupDb, companyId, "RACE");
            var firstVehicle = await Vehicle(setupDb, companyId, branch, $"RACE-A-{companyId}", vin: "1HGCM82633A004352");
            var secondVehicle = await Vehicle(setupDb, companyId, branch, $"RACE-B-{companyId}", alternate: $"RACE-MFG-{companyId}");
            var deviceId = await Device(setupDb, companyId, $"RACE-SERIAL-{companyId}");
            var start = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

            async Task<IResult> Attempt(long vehicleId, string key)
            {
                var db = Db();
                await start.Task;
                return await Invoke("DeviceInstallationCreate", Principal(companyId, 42), deviceId,
                    Body("DeviceInstallationCreateBody", vehicleId, "GPS", true, DateTimeOffset.UtcNow.AddSeconds(-1),
                        "cab", 1m, "heartbeat", "race proof", key), db, new AuditService(db), CancellationToken.None);
            }

            var first = Attempt(firstVehicle, $"race-a-{Guid.NewGuid():N}");
            var second = Attempt(secondVehicle, $"race-b-{Guid.NewGuid():N}");
            start.SetResult();
            var statuses = (await Task.WhenAll(first, second)).Select(Status).OrderBy(value => value).ToArray();
            Assert.Equal(new int?[] { StatusCodes.Status201Created, StatusCodes.Status409Conflict }, statuses);
            Assert.Equal(1, await setupDb.ScalarLongAsync(
                "SELECT COUNT(*) FROM device_installations WHERE company_id=@c AND device_id=@d AND effective_to IS NULL AND status IN ('Installed','Verified')",
                c => { c.Parameters.AddWithValue("@c", companyId); c.Parameters.AddWithValue("@d", deviceId); }));
        }
        finally
        {
            foreach (var sql in new[]
            {
                "DELETE FROM audit_logs WHERE company_id=@c", "DELETE FROM device_state_transitions WHERE company_id=@c",
                "DELETE FROM device_installations WHERE company_id=@c", "DELETE FROM eld_devices WHERE company_id=@c",
                "DELETE FROM vehicles WHERE company_id=@c", "DELETE FROM branches WHERE company_id=@c",
                "DELETE FROM companies WHERE id=@c"
            }) await setupDb.ExecuteAsync(sql, c => c.Parameters.AddWithValue("@c", companyId));
        }
    }

    [Fact]
    public async Task InstallationTransferIsTemporalTenantSafeIdempotentAndRefreshPersistent()
    {
        var db = Db();
        var companyId = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() + Random.Shared.Next(100_000,900_000);
        var otherCompanyId = companyId + 1;
        await Company(db,companyId,"A");
        await Company(db,otherCompanyId,"B");
        try
        {
            var branchA = await Branch(db,companyId,"A");
            var branchB = await Branch(db,companyId,"B");
            var otherBranch = await Branch(db,otherCompanyId,"X");
            var vehicleA = await Vehicle(db,companyId,branchA,$"UNIT-A-{companyId}",vin:"1HGCM82633A004352");
            var vehicleB = await Vehicle(db,companyId,branchB,$"UNIT-B-{companyId}",alternate:$"MFG-{companyId}-B");
            var otherVehicle = await Vehicle(db,otherCompanyId,otherBranch,$"UNIT-X-{companyId}",alternate:$"MFG-{companyId}-X");
            var deviceId = await Device(db,companyId,$"SERIAL-{companyId}");
            var http = Principal(companyId,userId:42);
            var audit = new AuditService(db);
            var installKey = $"install-{Guid.NewGuid():N}";
            var installedAt = DateTimeOffset.UtcNow.AddMinutes(-2);

            var created = await Invoke("DeviceInstallationCreate",http,deviceId,
                Body("DeviceInstallationCreateBody",vehicleA,"GPS",true,installedAt,"cab",123m,"heartbeat", "pilot install",installKey),
                db,audit,CancellationToken.None);
            Assert.Equal(StatusCodes.Status201Created,Status(created));
            var first = await db.QuerySingleAsync(
                "SELECT id,row_version FROM device_installations WHERE company_id=@c AND device_id=@d AND effective_to IS NULL",
                c => { c.Parameters.AddWithValue("@c",companyId); c.Parameters.AddWithValue("@d",deviceId); });
            Assert.NotNull(first);
            var firstId = Convert.ToInt64(first!["id"]);

            var replay = await Invoke("DeviceInstallationCreate",http,deviceId,
                Body("DeviceInstallationCreateBody",vehicleA,"GPS",true,installedAt,"cab",123m,"heartbeat", "pilot install",installKey),
                db,audit,CancellationToken.None);
            Assert.Equal(StatusCodes.Status200OK,Status(replay));
            Assert.Equal(1,await Count(db,companyId,deviceId));

            var keyReuse = await Invoke("DeviceInstallationCreate",http,deviceId,
                Body("DeviceInstallationCreateBody",vehicleB,"GPS",true,installedAt,"cab",123m,"heartbeat", "wrong reuse",installKey),
                db,audit,CancellationToken.None);
            Assert.Equal(StatusCodes.Status409Conflict,Status(keyReuse));

            var crossTenant = await Invoke("DeviceInstallationCreate",http,deviceId,
                Body("DeviceInstallationCreateBody",otherVehicle,"GPS",true,installedAt,"cab",123m,"heartbeat", "cross tenant",$"cross-{Guid.NewGuid():N}"),
                db,audit,CancellationToken.None);
            Assert.Equal(StatusCodes.Status400BadRequest,Status(crossTenant));

            var transferKey = $"transfer-{Guid.NewGuid():N}";
            var transferred = await Invoke("DeviceInstallationTransfer",http,deviceId,
                Body("DeviceInstallationTransferBody",vehicleB,(long?)firstId,"replace vehicle","new route", "GPS",true,
                    (DateTimeOffset?)DateTimeOffset.UtcNow,null,null,"heartbeat",(int?)1,transferKey),
                db,audit,CancellationToken.None);
            Assert.Equal(StatusCodes.Status200OK,Status(transferred));
            var history = await db.QueryAsync(
                "SELECT id,vehicle_id,status,effective_from,effective_to,replaced_installation_id FROM device_installations WHERE company_id=@c AND device_id=@d ORDER BY effective_from,id",
                c => { c.Parameters.AddWithValue("@c",companyId); c.Parameters.AddWithValue("@d",deviceId); });
            Assert.Equal(2,history.Count);
            Assert.Equal("Removed",history[0]["status"]);
            Assert.NotNull(history[0]["effectiveTo"]);
            Assert.Equal(vehicleA,Convert.ToInt64(history[0]["vehicleId"]));
            Assert.Equal("Installed",history[1]["status"]);
            Assert.Equal(vehicleB,Convert.ToInt64(history[1]["vehicleId"]));
            Assert.Equal(firstId,Convert.ToInt64(history[1]["replacedInstallationId"]));
            Assert.Equal(vehicleB,await db.ScalarLongAsync(
                "SELECT vehicle_id FROM eld_devices WHERE company_id=@c AND id=@d",
                c => { c.Parameters.AddWithValue("@c",companyId); c.Parameters.AddWithValue("@d",deviceId); }));

            var transferReplay = await Invoke("DeviceInstallationTransfer",http,deviceId,
                Body("DeviceInstallationTransferBody",vehicleB,(long?)firstId,"replace vehicle","new route", "GPS",true,
                    (DateTimeOffset?)DateTimeOffset.UtcNow,null,null,"heartbeat",(int?)1,transferKey),
                db,audit,CancellationToken.None);
            Assert.Equal(StatusCodes.Status200OK,Status(transferReplay));
            Assert.Equal(2,await Count(db,companyId,deviceId));
            Assert.Equal(2,await db.ScalarLongAsync(
                "SELECT COUNT(*) FROM audit_logs WHERE company_id=@c AND action_name IN ('device.installation.created','device.installation.transferred')",
                c => c.Parameters.AddWithValue("@c",companyId)));
        }
        finally
        {
            foreach (var company in new[] { companyId,otherCompanyId })
            foreach (var sql in new[]
            {
                "DELETE FROM audit_logs WHERE company_id=@c","DELETE FROM device_state_transitions WHERE company_id=@c",
                "DELETE FROM device_installations WHERE company_id=@c","DELETE FROM eld_devices WHERE company_id=@c",
                "DELETE FROM vehicles WHERE company_id=@c","DELETE FROM branches WHERE company_id=@c",
                "DELETE FROM companies WHERE id=@c"
            })
                await db.ExecuteAsync(sql,c => c.Parameters.AddWithValue("@c",company));
        }
    }

    [Fact]
    public async Task RemovedHistoryRetainsDeviceAndPrimaryVehicleRoleNonOverlap()
    {
        var db = Db();
        var companyId = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() + Random.Shared.Next(2_000_000,2_900_000);
        await Company(db,companyId,"HISTORY");
        try
        {
            var branch = await Branch(db,companyId,"HISTORY");
            var vehicleA = await Vehicle(db,companyId,branch,$"HISTORY-A-{companyId}",vin:"1HGCM82633A004352");
            var vehicleB = await Vehicle(db,companyId,branch,$"HISTORY-B-{companyId}",alternate:$"HISTORY-MFG-{companyId}");
            var deviceA = await Device(db,companyId,$"HISTORY-DEVICE-A-{companyId}");
            var deviceB = await Device(db,companyId,$"HISTORY-DEVICE-B-{companyId}");
            var from = DateTimeOffset.UtcNow.AddDays(-10);
            var to = DateTimeOffset.UtcNow.AddDays(-5);

            var first = await RemovedInstallation(db,companyId,branch,deviceA,vehicleA,from,to);
            var vehicleOverlap = await Assert.ThrowsAsync<PostgresException>(() =>
                RemovedInstallation(db,companyId,branch,deviceB,vehicleA,from.AddDays(1),to.AddDays(-1),"gps"));
            Assert.Equal(PostgresErrorCodes.ExclusionViolation,vehicleOverlap.SqlState);

            var deviceOverlap = await Assert.ThrowsAsync<PostgresException>(() =>
                RemovedInstallation(db,companyId,branch,deviceA,vehicleB,from.AddDays(1),to.AddDays(-1)));
            Assert.Equal(PostgresErrorCodes.ExclusionViolation,deviceOverlap.SqlState);

            var rewrite = await Assert.ThrowsAsync<PostgresException>(() => db.ExecuteAsync(
                "UPDATE device_installations SET effective_from=effective_from-INTERVAL '1 day' WHERE id=@id",
                c => c.Parameters.AddWithValue("@id",first)));
            Assert.Equal(PostgresErrorCodes.ObjectNotInPrerequisiteState,rewrite.SqlState);
        }
        finally
        {
            foreach (var sql in new[]
            {
                "DELETE FROM device_installations WHERE company_id=@c","DELETE FROM eld_devices WHERE company_id=@c",
                "DELETE FROM vehicles WHERE company_id=@c","DELETE FROM branches WHERE company_id=@c",
                "DELETE FROM companies WHERE id=@c"
            }) await db.ExecuteAsync(sql,c => c.Parameters.AddWithValue("@c",companyId));
        }
    }

    [Fact]
    public async Task EvidenceReferenceIsTenantCoherentAndAppendOnly()
    {
        var db = Db();
        var companyId = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() + Random.Shared.Next(3_000_000,3_900_000);
        var otherCompanyId = companyId + 1;
        await Company(db,companyId,"EVIDENCE-A");
        await Company(db,otherCompanyId,"EVIDENCE-B");
        try
        {
            var branchA = await Branch(db,companyId,"EVIDENCE-A");
            var branchB = await Branch(db,otherCompanyId,"EVIDENCE-B");
            var vehicleA = await Vehicle(db,companyId,branchA,$"EVIDENCE-A-{companyId}",vin:"1HGCM82633A004352");
            var vehicleB = await Vehicle(db,otherCompanyId,branchB,$"EVIDENCE-B-{companyId}",alternate:$"EVIDENCE-MFG-{companyId}");
            var deviceA = await Device(db,companyId,$"EVIDENCE-DEVICE-A-{companyId}");
            var deviceB = await Device(db,otherCompanyId,$"EVIDENCE-DEVICE-B-{companyId}");
            var from = DateTimeOffset.UtcNow.AddDays(-4);
            var installationA = await RemovedInstallation(db,companyId,branchA,deviceA,vehicleA,from,from.AddDays(1));
            var installationB = await RemovedInstallation(db,otherCompanyId,branchB,deviceB,vehicleB,from,from.AddDays(1));

            var mismatch = await Assert.ThrowsAsync<PostgresException>(() => db.ExecuteAsync(
                @"INSERT INTO device_installation_evidence(company_id,branch_id,installation_id,evidence_type,object_key,sha256)
                  VALUES (@c,@b,@i,'photo','cross-tenant-proof',repeat('a',64))",
                c => { c.Parameters.AddWithValue("@c",companyId); c.Parameters.AddWithValue("@b",branchA); c.Parameters.AddWithValue("@i",installationB); }));
            Assert.Equal(PostgresErrorCodes.ForeignKeyViolation,mismatch.SqlState);

            var evidenceId = await db.InsertAsync(
                @"INSERT INTO device_installation_evidence(company_id,branch_id,installation_id,evidence_type,object_key,sha256)
                  VALUES (@c,@b,@i,'photo','tenant-proof',repeat('b',64))",
                c => { c.Parameters.AddWithValue("@c",companyId); c.Parameters.AddWithValue("@b",branchA); c.Parameters.AddWithValue("@i",installationA); });
            var rewrite = await Assert.ThrowsAsync<PostgresException>(() => db.ExecuteAsync(
                "UPDATE device_installation_evidence SET object_key='rewritten' WHERE id=@id",
                c => c.Parameters.AddWithValue("@id",evidenceId)));
            Assert.Equal(PostgresErrorCodes.ObjectNotInPrerequisiteState,rewrite.SqlState);
        }
        finally
        {
            foreach (var company in new[] { companyId,otherCompanyId })
            foreach (var sql in new[]
            {
                "DELETE FROM device_installation_evidence WHERE company_id=@c","DELETE FROM device_installations WHERE company_id=@c",
                "DELETE FROM eld_devices WHERE company_id=@c","DELETE FROM vehicles WHERE company_id=@c",
                "DELETE FROM branches WHERE company_id=@c","DELETE FROM companies WHERE id=@c"
            }) await db.ExecuteAsync(sql,c => c.Parameters.AddWithValue("@c",company));
        }
    }

    [Fact]
    public async Task ApplicationRoleCannotUpdateCompatibilityVehicleOrDriverProjection()
    {
        var db = Db();
        Assert.Equal(0,await db.ScalarLongAsync("SELECT CASE WHEN has_column_privilege('opstrax_app','eld_devices','vehicle_id','UPDATE') THEN 1 ELSE 0 END"));
        Assert.Equal(0,await db.ScalarLongAsync("SELECT CASE WHEN has_column_privilege('opstrax_app','eld_devices','driver_id','UPDATE') THEN 1 ELSE 0 END"));
        Assert.Equal(1,await db.ScalarLongAsync("SELECT CASE WHEN has_column_privilege('opstrax_app','eld_devices','device_state','UPDATE') THEN 1 ELSE 0 END"));
    }

    private static async Task Company(Database db,long id,string suffix) => await db.ExecuteAsync(
        "INSERT INTO companies(id,company_code,name,industry) OVERRIDING SYSTEM VALUE VALUES (@c,@code,@name,'transport')",
        c => { c.Parameters.AddWithValue("@c",id); c.Parameters.AddWithValue("@code",$"INSTALL-{id}-{suffix}"); c.Parameters.AddWithValue("@name",$"Installation tenant {suffix}"); });
    private static Task<long> Branch(Database db,long company,string suffix) => db.InsertAsync(
        "INSERT INTO branches(company_id,branch_code,name,status) VALUES (@c,@code,@name,'Active')",
        c => { c.Parameters.AddWithValue("@c",company); c.Parameters.AddWithValue("@code",$"BR-{company}-{suffix}"); c.Parameters.AddWithValue("@name",$"Branch {suffix}"); });
    private static Task<long> Vehicle(Database db,long company,long branch,string code,string? vin=null,string? alternate=null) => db.InsertAsync(
        @"INSERT INTO vehicles(company_id,branch_id,vehicle_code,type,vin,vin_exception_type,alternate_identifier,status,availability_status,out_of_service)
          VALUES (@c,@b,@code,'Truck',@vin,@exception,@alternate,'Available','available',FALSE)",
        c =>
        {
            c.Parameters.AddWithValue("@c",company); c.Parameters.AddWithValue("@b",branch); c.Parameters.AddWithValue("@code",code);
            c.Parameters.AddWithValue("@vin",(object?)vin ?? DBNull.Value);
            c.Parameters.AddWithValue("@exception",alternate is null ? DBNull.Value : "manufacturer-serial-number");
            c.Parameters.AddWithValue("@alternate",(object?)alternate ?? DBNull.Value);
        });
    private static Task<long> Device(Database db,long company,string serial) => db.InsertAsync(
        @"INSERT INTO eld_devices(company_id,device_serial,status,device_state,api_key_hash,hmac_secret_encrypted,hmac_key_version,created_at)
          VALUES (@c,@serial,'Active','Registered',encode(sha256(@serial::bytea),'hex'),repeat('b',32),1,NOW())",
        c => { c.Parameters.AddWithValue("@c",company); c.Parameters.AddWithValue("@serial",serial); });
    private static Task<long> RemovedInstallation(Database db,long company,long branch,long device,long vehicle,DateTimeOffset from,DateTimeOffset to,string role="GPS") => db.InsertAsync(
        @"INSERT INTO device_installations(company_id,branch_id,device_id,vehicle_id,status,device_role,is_primary,effective_from,effective_to,installed_at,removed_at,source)
          VALUES (@c,@b,@d,@v,'Removed',@role,TRUE,@from,@to,@from,@to,'test')",
        c => { c.Parameters.AddWithValue("@c",company); c.Parameters.AddWithValue("@b",branch); c.Parameters.AddWithValue("@d",device);
               c.Parameters.AddWithValue("@v",vehicle); c.Parameters.AddWithValue("@from",from); c.Parameters.AddWithValue("@to",to);
               c.Parameters.AddWithValue("@role",role); });
    private static Task<long> Count(Database db,long company,long device) => db.ScalarLongAsync(
        "SELECT COUNT(*) FROM device_installations WHERE company_id=@c AND device_id=@d",
        c => { c.Parameters.AddWithValue("@c",company); c.Parameters.AddWithValue("@d",device); });
    private static Database Db() => new(new ConfigurationBuilder().AddInMemoryCollection(
        new Dictionary<string,string?> { ["ConnectionStrings:DefaultConnection"] = TestDb.ConnectionString }).Build());
    private static DefaultHttpContext Principal(long companyId,long userId)
    {
        var http = new DefaultHttpContext { TraceIdentifier=$"install-{Guid.NewGuid():N}" };
        http.Items[EndpointMappings.AuthCompanyIdItemKey]=companyId;
        http.Items[EndpointMappings.AuthUserIdItemKey]=userId;
        http.Items[EndpointMappings.AuthRoleItemKey]="CompanyAdmin";
        http.Items[EndpointMappings.AuthPermissionsItemKey]=new[] { "telemetry.devices.manage","telemetry.devices.read" };
        return http;
    }
    private static object Body(string name,params object?[] args)
    {
        var type=typeof(EndpointMappings).GetNestedType(name,BindingFlags.NonPublic)
            ?? throw new InvalidOperationException($"Missing body {name}");
        return Activator.CreateInstance(type,args) ?? throw new InvalidOperationException($"Unable to create {name}");
    }
    private static async Task<IResult> Invoke(string name,params object?[] args)
    {
        var method=typeof(EndpointMappings).GetMethod(name,BindingFlags.NonPublic|BindingFlags.Static)
            ?? throw new InvalidOperationException($"Missing endpoint {name}");
        return await ((Task<IResult>?)method.Invoke(null,args) ?? throw new InvalidOperationException($"{name} did not return a task"));
    }
    private static int? Status(IResult result) => Assert.IsAssignableFrom<IStatusCodeHttpResult>(result).StatusCode;
}
