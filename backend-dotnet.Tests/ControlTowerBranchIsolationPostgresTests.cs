using System.Reflection;
using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Opstrax.Api.Controllers;
using Opstrax.Api.Data;

namespace Opstrax.Tests;

public sealed class ControlTowerBranchIsolationPostgresTests
{
    [Fact]
    [Trait("Category", "Integration")]
    public async Task SummaryFailsClosedToTheAuthenticatedBranchAcrossEveryReturnedQueue()
    {
        var db = Database();
        var suffix = Guid.NewGuid().ToString("N")[..10];
        var company = await db.InsertAsync(
            "INSERT INTO companies(company_code,name,industry) VALUES (@code,'Control Tower branch test','Transportation')",
            c => c.Parameters.AddWithValue("@code", $"CTB-{suffix}"));
        var branchA = await Branch(db, company, $"A-{suffix}");
        var branchB = await Branch(db, company, $"B-{suffix}");

        try
        {
            var ownVehicle = await Vehicle(db, company, branchA, $"OWN-{suffix}", 85);
            var foreignVehicle = await Vehicle(db, company, branchB, $"FOREIGN-{suffix}", 95);
            var unallocatedVehicle = await Vehicle(db, company, null, $"NULL-{suffix}", 99);
            var ownDriver = await Driver(db, company, branchA, $"OWN-DRV-{suffix}");
            var foreignDriver = await Driver(db, company, branchB, $"FOREIGN-DRV-{suffix}");
            var unallocatedDriver = await Driver(db, company, null, $"NULL-DRV-{suffix}");
            await Location(db, company, ownVehicle, 71);
            await Location(db, company, foreignVehicle, 72);
            await Location(db, company, unallocatedVehicle, 73);

            await Geofence(db, company, branchA, $"Own yard {suffix}");
            await Geofence(db, company, branchB, $"Foreign yard {suffix}");
            await Geofence(db, company, null, $"Tenant yard {suffix}");

            var ownJob = await Job(db, company, branchA, $"OWN-JOB-{suffix}");
            var foreignJob = await Job(db, company, branchB, $"FOREIGN-JOB-{suffix}");
            await OperationalEvent(db, company, "Vehicle", ownVehicle, $"Own event {suffix}");
            await OperationalEvent(db, company, "Vehicle", foreignVehicle, $"Foreign event {suffix}");
            await OperationalEvent(db, company, "Job", ownJob, $"Own job event {suffix}");
            await OperationalEvent(db, company, "Job", foreignJob, $"Foreign job event {suffix}");
            await OperationalEvent(db, company, "Driver", ownDriver, $"Own driver event {suffix}");
            await OperationalEvent(db, company, "Driver", foreignDriver, $"Foreign driver event {suffix}");
            await OperationalEvent(db, company, "Driver", unallocatedDriver, $"Unallocated driver event {suffix}");
            await OperationalEvent(db, company, "Vehicle", unallocatedVehicle, $"Unallocated vehicle event {suffix}");
            await OperationalEvent(db, company, "Asset", ownVehicle, $"Unknown entity event {suffix}");
            await DashcamEvent(db, company, ownVehicle, null, $"OWN-CAM-{suffix}");
            await DashcamEvent(db, company, foreignVehicle, null, $"FOREIGN-CAM-{suffix}");
            await DashcamEvent(db, company, unallocatedVehicle, null, $"NULL-CAM-{suffix}");
            await DashcamEvent(db, company, ownVehicle, foreignDriver, $"OWN-VEH-FOREIGN-DRV-{suffix}");
            await DashcamEvent(db, company, foreignVehicle, ownDriver, $"FOREIGN-VEH-OWN-DRV-{suffix}");
            await DashcamEvent(db, company, null, ownDriver, $"OWN-DRV-ONLY-{suffix}");
            await DashcamEvent(db, company, null, foreignDriver, $"FOREIGN-DRV-ONLY-{suffix}");

            await db.ExecuteAsync(
                @"INSERT INTO ai_recommendations(company_id,tenant_id,recommendation_type,module_key,title,summary,body,score,status)
                  VALUES (@cid,@cid,'control.test','control-tower',@title,'Tenant-wide recommendation','Tenant-wide recommendation',1,'active')",
                c => { c.Parameters.AddWithValue("@cid", company); c.Parameters.AddWithValue("@title", $"Tenant rec {suffix}"); });

            var branchPayload = Payload(await Invoke(Principal(company, branchA), db));
            var branchData = branchPayload.GetProperty("data");
            Assert.Equal(1, branchData.GetProperty("entities").GetArrayLength());
            Assert.Equal($"OWN-{suffix}", branchData.GetProperty("entities")[0].GetProperty("label").GetString());
            Assert.Equal(1, branchData.GetProperty("kpis").GetProperty("trackedEntities").GetInt64());
            Assert.Equal(1, branchData.GetProperty("kpis").GetProperty("onlineDevices").GetInt64());
            Assert.Equal(1, branchData.GetProperty("kpis").GetProperty("onlineCameras").GetInt64());
            Assert.Equal(1, branchData.GetProperty("kpis").GetProperty("activeUnits").GetInt64());
            Assert.Equal(1, branchData.GetProperty("kpis").GetProperty("highRiskUnits").GetInt64());
            Assert.Equal(1, branchData.GetProperty("kpis").GetProperty("speedAlerts").GetInt64());
            Assert.Equal(90, branchData.GetProperty("kpis").GetProperty("telemetryQuality").GetDecimal());
            Assert.Equal(90, branchData.GetProperty("kpis").GetProperty("fleetReadiness").GetDecimal());
            Assert.Single(branchData.GetProperty("geofences").EnumerateArray());
            Assert.Equal($"Own yard {suffix}", branchData.GetProperty("geofences")[0].GetProperty("name").GetString());
            Assert.Equal(3, branchData.GetProperty("events").GetArrayLength());
            Assert.All(branchData.GetProperty("events").EnumerateArray(), item => Assert.DoesNotContain("Foreign", item.GetProperty("title").GetString()));
            Assert.All(branchData.GetProperty("events").EnumerateArray(), item => Assert.DoesNotContain("Unallocated", item.GetProperty("title").GetString()));
            Assert.All(branchData.GetProperty("events").EnumerateArray(), item => Assert.DoesNotContain("Unknown", item.GetProperty("title").GetString()));
            Assert.Single(branchData.GetProperty("jobs").EnumerateArray());
            Assert.Equal($"OWN-JOB-{suffix}", branchData.GetProperty("jobs")[0].GetProperty("jobNumber").GetString());
            Assert.Single(branchData.GetProperty("diagnostics").EnumerateArray());
            Assert.Equal($"OWN-{suffix}", branchData.GetProperty("diagnostics")[0].GetProperty("vehicleCode").GetString());
            var branchSafety = branchData.GetProperty("safetyVideo").EnumerateArray().ToArray();
            Assert.Equal(3, branchSafety.Length);
            Assert.Contains(branchSafety, item => item.GetProperty("eventNumber").GetString() == $"OWN-CAM-{suffix}");
            Assert.Contains(branchSafety, item => item.GetProperty("eventNumber").GetString() == $"OWN-DRV-ONLY-{suffix}" && item.GetProperty("driverName").GetString() == $"OWN-DRV-{suffix}");
            var crossLinked = Assert.Single(branchSafety, item => item.GetProperty("eventNumber").GetString() == $"OWN-VEH-FOREIGN-DRV-{suffix}");
            Assert.Equal(JsonValueKind.Null, crossLinked.GetProperty("driverName").ValueKind);
            Assert.Empty(branchData.GetProperty("recommendations").EnumerateArray());
            Assert.Equal(4, branchData.GetProperty("actionQueue").GetArrayLength());
            Assert.All(branchData.GetProperty("actionQueue").EnumerateArray(), item => Assert.DoesNotContain("Foreign", item.GetProperty("title").GetString()));
            Assert.All(branchData.GetProperty("actionQueue").EnumerateArray(), item => Assert.DoesNotContain("Unallocated", item.GetProperty("title").GetString()));
            Assert.All(branchData.GetProperty("actionQueue").EnumerateArray(), item => Assert.DoesNotContain("Unknown", item.GetProperty("title").GetString()));

            var tenantPayload = Payload(await Invoke(Principal(company, null), db));
            var tenantData = tenantPayload.GetProperty("data");
            Assert.Equal(3, tenantData.GetProperty("entities").GetArrayLength());
            Assert.Equal(3, tenantData.GetProperty("kpis").GetProperty("trackedEntities").GetInt64());
            Assert.Equal(3, tenantData.GetProperty("kpis").GetProperty("onlineDevices").GetInt64());
            Assert.Equal(3, tenantData.GetProperty("kpis").GetProperty("onlineCameras").GetInt64());
            Assert.Equal(3, tenantData.GetProperty("kpis").GetProperty("activeUnits").GetInt64());
            Assert.Equal(3, tenantData.GetProperty("kpis").GetProperty("highRiskUnits").GetInt64());
            Assert.Equal(3, tenantData.GetProperty("kpis").GetProperty("speedAlerts").GetInt64());
            Assert.Equal(3, tenantData.GetProperty("geofences").GetArrayLength());
            Assert.Equal(9, tenantData.GetProperty("events").GetArrayLength());
            Assert.Equal(2, tenantData.GetProperty("jobs").GetArrayLength());
            Assert.Equal(3, tenantData.GetProperty("diagnostics").GetArrayLength());
            Assert.Equal(6, tenantData.GetProperty("safetyVideo").GetArrayLength());
            Assert.Single(tenantData.GetProperty("recommendations").EnumerateArray());
            Assert.Equal(11, tenantData.GetProperty("actionQueue").GetArrayLength());
        }
        finally
        {
            await db.ExecuteAsync("DELETE FROM operational_events WHERE company_id=@cid", c => c.Parameters.AddWithValue("@cid", company));
            await db.ExecuteAsync("DELETE FROM location_events WHERE company_id=@cid", c => c.Parameters.AddWithValue("@cid", company));
            await db.ExecuteAsync("DELETE FROM geofences WHERE company_id=@cid", c => c.Parameters.AddWithValue("@cid", company));
            await db.ExecuteAsync("DELETE FROM dashcam_events WHERE company_id=@cid", c => c.Parameters.AddWithValue("@cid", company));
            await db.ExecuteAsync("DELETE FROM jobs WHERE company_id=@cid", c => c.Parameters.AddWithValue("@cid", company));
            await db.ExecuteAsync("DELETE FROM ai_recommendations WHERE tenant_id=@cid", c => c.Parameters.AddWithValue("@cid", company));
            await db.ExecuteAsync("DELETE FROM vehicles WHERE company_id=@cid", c => c.Parameters.AddWithValue("@cid", company));
            await db.ExecuteAsync("DELETE FROM drivers WHERE company_id=@cid", c => c.Parameters.AddWithValue("@cid", company));
            await db.ExecuteAsync("DELETE FROM branches WHERE company_id=@cid", c => c.Parameters.AddWithValue("@cid", company));
            await db.ExecuteAsync("DELETE FROM companies WHERE id=@cid", c => c.Parameters.AddWithValue("@cid", company));
        }
    }

    private static Task<long> Branch(Database db, long company, string code) => db.InsertAsync(
        "INSERT INTO branches(company_id,branch_code,name,status) VALUES (@cid,@code,@code,'Active')",
        c => { c.Parameters.AddWithValue("@cid", company); c.Parameters.AddWithValue("@code", code); });

    private static Task<long> Vehicle(Database db, long company, long? branch, string code, int risk) => db.InsertAsync(
        @"INSERT INTO vehicles(company_id,branch_id,vehicle_code,type,vin_exception_type,alternate_identifier,status,device_status,camera_status,risk_score,data_quality_score,readiness_score)
          VALUES (@cid,@branch,@code,'Truck','legacy-fleet-identifier',@code,'Active','Online','Online',@risk,90,90)",
        c => { c.Parameters.AddWithValue("@cid", company); c.Parameters.AddWithValue("@branch", (object?)branch ?? DBNull.Value); c.Parameters.AddWithValue("@code", code); c.Parameters.AddWithValue("@risk", risk); });

    private static Task<long> Driver(Database db, long company, long? branch, string code) => db.InsertAsync(
        "INSERT INTO drivers(company_id,branch_id,driver_code,full_name) VALUES (@cid,@branch,@code,@code)",
        c => { c.Parameters.AddWithValue("@cid", company); c.Parameters.AddWithValue("@branch", (object?)branch ?? DBNull.Value); c.Parameters.AddWithValue("@code", code); });

    private static Task Location(Database db, long company, long vehicle, int speed) => db.ExecuteAsync(
        "INSERT INTO location_events(company_id,vehicle_id,lat,lng,speed_mph,event_type,event_time) VALUES (@cid,@vehicle,43,-79,@speed,'position',NOW())",
        c => { c.Parameters.AddWithValue("@cid", company); c.Parameters.AddWithValue("@vehicle", vehicle); c.Parameters.AddWithValue("@speed", speed); });

    private static Task Geofence(Database db, long company, long? branch, string name) => db.ExecuteAsync(
        "INSERT INTO geofences(company_id,branch_id,name,geofence_type,center_lat,center_lng,radius_meters,status) VALUES (@cid,@branch,@name,'Circle',43,-79,500,'Active')",
        c => { c.Parameters.AddWithValue("@cid", company); c.Parameters.AddWithValue("@branch", (object?)branch ?? DBNull.Value); c.Parameters.AddWithValue("@name", name); });

    private static Task<long> Job(Database db, long company, long branch, string code) => db.InsertAsync(
        @"INSERT INTO jobs(company_id,branch_id,job_code,job_number,job_type,status,priority,sla_status,scheduled_start)
          VALUES (@cid,@branch,@code,@code,'Delivery','At Risk','High','At Risk',NOW())",
        c => { c.Parameters.AddWithValue("@cid", company); c.Parameters.AddWithValue("@branch", branch); c.Parameters.AddWithValue("@code", code); });

    private static Task OperationalEvent(Database db, long company, string type, long id, string title) => db.ExecuteAsync(
        "INSERT INTO operational_events(company_id,entity_type,entity_id,event_type,title,severity,event_time) VALUES (@cid,@type,@id,'branch.test',@title,'Warning',NOW())",
        c => { c.Parameters.AddWithValue("@cid", company); c.Parameters.AddWithValue("@type", type); c.Parameters.AddWithValue("@id", id); c.Parameters.AddWithValue("@title", title); });

    private static Task DashcamEvent(Database db, long company, long? vehicle, long? driver, string eventNumber) => db.ExecuteAsync(
        "INSERT INTO dashcam_events(company_id,event_number,event_type,title,severity,vehicle_id,driver_id,occurred_at) VALUES (@cid,@number,'branch.test',@number,'Warning',@vehicle,@driver,NOW())",
        c => { c.Parameters.AddWithValue("@cid", company); c.Parameters.AddWithValue("@number", eventNumber); c.Parameters.AddWithValue("@vehicle", (object?)vehicle ?? DBNull.Value); c.Parameters.AddWithValue("@driver", (object?)driver ?? DBNull.Value); });

    private static DefaultHttpContext Principal(long company, long? branch)
    {
        var http = new DefaultHttpContext();
        http.Items[EndpointMappings.AuthUserIdItemKey] = 41L;
        http.Items[EndpointMappings.AuthCompanyIdItemKey] = company;
        http.Items[EndpointMappings.AuthRoleItemKey] = branch is null ? "Tenant Administrator" : "Fleet Manager";
        http.Items[EndpointMappings.AuthPermissionsItemKey] = new[] { "dashboard:view" };
        if (branch is not null) http.Items[EndpointMappings.AuthBranchIdItemKey] = branch.Value;
        return http;
    }

    private static async Task<IResult> Invoke(DefaultHttpContext http, Database db)
    {
        var method = typeof(EndpointMappings).GetMethod("ControlTowerSummary", BindingFlags.NonPublic | BindingFlags.Static)!;
        return await (Task<IResult>)method.Invoke(null, [http, db, CancellationToken.None])!;
    }

    private static JsonElement Payload(IResult result)
    {
        var value = Assert.IsAssignableFrom<IValueHttpResult>(result).Value;
        return JsonDocument.Parse(JsonSerializer.Serialize(value, new JsonSerializerOptions(JsonSerializerDefaults.Web))).RootElement.Clone();
    }

    private static Database Database() => new(new ConfigurationBuilder()
        .AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["ConnectionStrings:DefaultConnection"] = TestDb.ConnectionString,
            ["Rls:EnforceTenantContext"] = "false",
        }).Build(), new TenantScopeAccessor());
}
