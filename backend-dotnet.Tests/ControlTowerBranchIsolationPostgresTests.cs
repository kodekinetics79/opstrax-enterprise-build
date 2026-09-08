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
            await TelemetryDevice(db, company, branchA, ownVehicle, $"OWN-DEV-{suffix}");
            await Location(db, company, ownVehicle, 71);
            await Location(db, company, foreignVehicle, 72);
            await Location(db, company, unallocatedVehicle, 73);
            var ownTelemetryAlert = await TelemetryAlert(db, company, ownVehicle, $"Own telemetry alert {suffix}");
            var foreignTelemetryAlert = await TelemetryAlert(db, company, foreignVehicle, $"Foreign telemetry alert {suffix}");

            await Geofence(db, company, branchA, $"Own yard {suffix}");
            await Geofence(db, company, branchB, $"Foreign yard {suffix}");
            await Geofence(db, company, null, $"Tenant yard {suffix}");

            var ownJob = await Job(db, company, branchA, $"OWN-JOB-{suffix}");
            var foreignJob = await Job(db, company, branchB, $"FOREIGN-JOB-{suffix}");
            var ownAssignment = await DispatchAssignment(db, company, branchA, ownJob, ownVehicle);
            var foreignAssignment = await DispatchAssignment(db, company, branchB, foreignJob, foreignVehicle);
            var ownException = await DispatchException(db, company, ownAssignment, ownJob, $"Own dispatch exception {suffix}");
            var foreignException = await DispatchException(db, company, foreignAssignment, foreignJob, $"Foreign dispatch exception {suffix}");
            var ownProposal = await AgenticRecommendation(db, company, ownException, ownAssignment, $"Own Copilot proposal {suffix}");
            var mismatchedProposal = await AgenticRecommendation(db, company, ownException, foreignAssignment, $"Mismatched Copilot proposal {suffix}");
            await AgenticRecommendation(db, company, foreignException, foreignAssignment, $"Foreign Copilot proposal {suffix}");
            await AgenticRecommendation(db, company, long.MaxValue, ownAssignment, $"Unbound Copilot proposal {suffix}");
            await OperationalEvent(db, company, "Vehicle", ownVehicle, $"Own event {suffix}");
            await OperationalEvent(db, company, "Vehicle", foreignVehicle, $"Foreign event {suffix}");
            await OperationalEvent(db, company, "Job", ownJob, $"Own job event {suffix}");
            await OperationalEvent(db, company, "Job", foreignJob, $"Foreign job event {suffix}");
            await OperationalEvent(db, company, "Driver", ownDriver, $"Own driver event {suffix}");
            await OperationalEvent(db, company, "Driver", foreignDriver, $"Foreign driver event {suffix}");
            await OperationalEvent(db, company, "Driver", unallocatedDriver, $"Unallocated driver event {suffix}");
            await OperationalEvent(db, company, "Vehicle", unallocatedVehicle, $"Unallocated vehicle event {suffix}");
            await OperationalEvent(db, company, "Asset", ownVehicle, $"Unknown entity event {suffix}");
            await DashcamEvent(db, company, ownVehicle, null, $"OWN-CAM-{suffix}", "Authoritative", "Ready");
            await DashcamEvent(db, company, foreignVehicle, null, $"FOREIGN-CAM-{suffix}", "Authoritative", "Ready");
            await DashcamEvent(db, company, unallocatedVehicle, null, $"NULL-CAM-{suffix}", "Authoritative", "Ready");
            await DashcamEvent(db, company, ownVehicle, foreignDriver, $"OWN-VEH-FOREIGN-DRV-{suffix}", "Authoritative", "Ready");
            await DashcamEvent(db, company, foreignVehicle, ownDriver, $"FOREIGN-VEH-OWN-DRV-{suffix}", "Authoritative", "Ready");
            await DashcamEvent(db, company, null, ownDriver, $"OWN-DRV-ONLY-{suffix}", "Authoritative", "Ready");
            await DashcamEvent(db, company, null, foreignDriver, $"FOREIGN-DRV-ONLY-{suffix}", "Authoritative", "Ready");
            await DashcamEvent(db, company, ownVehicle, ownDriver, $"LEGACY-CAM-{suffix}", "LegacyUnverified", "ProviderPending");
            await DashcamEvent(db, company, ownVehicle, ownDriver, $"PENDING-MEDIA-{suffix}", "Authoritative", "ProviderPending");

            await db.ExecuteAsync(
                @"INSERT INTO ai_recommendations(company_id,tenant_id,recommendation_type,module_key,title,summary,body,score,status)
                  VALUES (@cid,@cid,'control.test','control-tower',@title,'Tenant-wide recommendation','Tenant-wide recommendation',1,'active')",
                c => { c.Parameters.AddWithValue("@cid", company); c.Parameters.AddWithValue("@title", $"Tenant rec {suffix}"); });

            var branchPayload = Payload(await Invoke(Principal(company, branchA, "dashboard:view", "dashcam:view", "telematics:devices:view"), db));
            var branchData = branchPayload.GetProperty("data");
            Assert.Equal(1, branchData.GetProperty("entities").GetArrayLength());
            Assert.Equal($"OWN-{suffix}", branchData.GetProperty("entities")[0].GetProperty("label").GetString());
            Assert.Equal(1, branchData.GetProperty("kpis").GetProperty("trackedEntities").GetInt64());
            Assert.Equal(1, branchData.GetProperty("kpis").GetProperty("onlineDevices").GetInt64());
            Assert.Equal(0, branchData.GetProperty("kpis").GetProperty("onlineCameras").GetInt64());
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
            Assert.DoesNotContain(branchSafety, item => item.GetProperty("eventNumber").GetString() == $"LEGACY-CAM-{suffix}");
            Assert.DoesNotContain(branchSafety, item => item.GetProperty("eventNumber").GetString() == $"PENDING-MEDIA-{suffix}");
            Assert.Empty(branchData.GetProperty("recommendations").EnumerateArray());
            Assert.Equal(4, branchData.GetProperty("actionQueue").GetArrayLength());
            Assert.All(branchData.GetProperty("actionQueue").EnumerateArray(), item => Assert.DoesNotContain("Foreign", item.GetProperty("title").GetString()));
            Assert.All(branchData.GetProperty("actionQueue").EnumerateArray(), item => Assert.DoesNotContain("Unallocated", item.GetProperty("title").GetString()));
            Assert.All(branchData.GetProperty("actionQueue").EnumerateArray(), item => Assert.DoesNotContain("Unknown", item.GetProperty("title").GetString()));

            var authorizedDetail = Payload(await InvokeVehicleDetail(
                Principal(company, branchA, "vehicles:view", "telematics:devices:view", "dashcam:view"), ownVehicle, db)).GetProperty("data");
            Assert.Equal("Online", authorizedDetail.GetProperty("record").GetProperty("deviceStatus").GetString());
            Assert.Equal("Unknown", authorizedDetail.GetProperty("record").GetProperty("cameraStatus").GetString());
            var restrictedDetail = Payload(await InvokeVehicleDetail(
                Principal(company, branchA, "vehicles:view"), ownVehicle, db)).GetProperty("data");
            Assert.Equal("Unknown", restrictedDetail.GetProperty("record").GetProperty("deviceStatus").GetString());
            Assert.Equal("Unknown", restrictedDetail.GetProperty("record").GetProperty("cameraStatus").GetString());
            Assert.Equal(JsonValueKind.Null, restrictedDetail.GetProperty("record").GetProperty("readinessScore").ValueKind);
            Assert.Equal(JsonValueKind.Null, restrictedDetail.GetProperty("record").GetProperty("dataQualityScore").ValueKind);

            var commandCenter = Payload(await InvokeCommandCenter(
                Principal(company, branchA, "dashboard:view"), db)).GetProperty("data");
            Assert.Equal(1, commandCenter.GetProperty("fleetTotal").GetInt64());
            Assert.Equal(1, commandCenter.GetProperty("fleetStatus").GetProperty("driving").GetInt64());
            Assert.Equal(0, commandCenter.GetProperty("fleetStatus").GetProperty("attention").GetInt64());
            Assert.Equal(100, commandCenter.GetProperty("readinessPct").GetInt32());
            Assert.Contains("1 vehicle on active routes", commandCenter.GetProperty("briefItems")[0].GetString());
            Assert.DoesNotContain("device", commandCenter.GetProperty("briefItems")[0].GetString(), StringComparison.OrdinalIgnoreCase);
            Assert.Equal(JsonValueKind.Null, commandCenter.GetProperty("kpis")[1].GetProperty("value").ValueKind);
            var commandCenterWithAlerts = Payload(await InvokeCommandCenter(
                Principal(company, branchA, "dashboard:view", "alerts:view"), db)).GetProperty("data");
            Assert.Equal("Open Telemetry Alerts", commandCenterWithAlerts.GetProperty("kpis")[1].GetProperty("label").GetString());
            Assert.Equal(1, commandCenterWithAlerts.GetProperty("kpis")[1].GetProperty("value").GetInt64());

            var branchAlerts = Payload(await InvokeAlertsList(
                Principal(company, branchA, "alerts:view"), db)).GetProperty("data");
            Assert.Single(branchAlerts.EnumerateArray());
            Assert.Equal($"Own telemetry alert {suffix}", branchAlerts[0].GetProperty("body").GetString());
            Assert.Equal("Telematics", branchAlerts[0].GetProperty("category").GetString());
            Assert.Equal("/control-tower", branchAlerts[0].GetProperty("entityRoute").GetString());
            var branchAiEvidence = Payload(await InvokeAiInsights(
                Principal(company, branchA, "dashboard:view"), db)).GetProperty("data");
            Assert.Single(branchAiEvidence.EnumerateArray());
            Assert.Equal($"Own telemetry alert {suffix}", branchAiEvidence[0].GetProperty("body").GetString());
            var branchProposals = Payload(await InvokeAgenticList(
                Principal(company, branchA, "dispatch:view"), db)).GetProperty("data");
            Assert.Equal(2, branchProposals.GetArrayLength());
            Assert.Contains(branchProposals.EnumerateArray(), proposal => proposal.GetProperty("title").GetString() == $"Own Copilot proposal {suffix}");
            Assert.Contains(branchProposals.EnumerateArray(), proposal => proposal.GetProperty("title").GetString() == $"Mismatched Copilot proposal {suffix}");
            Assert.DoesNotContain(branchProposals.EnumerateArray(), proposal => proposal.GetProperty("title").GetString() == $"Foreign Copilot proposal {suffix}");
            Assert.DoesNotContain(branchProposals.EnumerateArray(), proposal => proposal.GetProperty("title").GetString() == $"Unbound Copilot proposal {suffix}");
            var dispatchManager = Principal(company, branchA, "dispatch:manage");
            Assert.Equal(StatusCodes.Status400BadRequest, Status(await InvokeAgenticApprove(dispatchManager, mismatchedProposal, db)));
            var exceptionCountBeforeApproval = await db.ScalarLongAsync(
                "SELECT COUNT(*) FROM dispatch_exceptions WHERE company_id=@cid",
                c => c.Parameters.AddWithValue("@cid", company));
            var approval = Payload(await InvokeAgenticApprove(dispatchManager, ownProposal, db)).GetProperty("data");
            Assert.Equal("approved", approval.GetProperty("status").GetString());
            Assert.Equal("approval_recorded", approval.GetProperty("outcome").GetString());
            Assert.Equal(exceptionCountBeforeApproval, await db.ScalarLongAsync(
                "SELECT COUNT(*) FROM dispatch_exceptions WHERE company_id=@cid",
                c => c.Parameters.AddWithValue("@cid", company)));
            Assert.Equal(StatusCodes.Status409Conflict, Status(await InvokeAgenticApprove(dispatchManager, ownProposal, db)));
            var branchAlertSummary = Payload(await InvokeAlertsSummary(
                Principal(company, branchA, "alerts:view"), db)).GetProperty("data");
            Assert.Equal(1, branchAlertSummary.GetProperty("total").GetInt64());
            Assert.Equal(1, branchAlertSummary.GetProperty("open").GetInt64());

            var alertPrincipal = Principal(company, branchA, "alerts:view", "alerts:acknowledge", "alerts:close");
            var ownAlertDetail = Payload(await InvokeAlertDetail(alertPrincipal, ownTelemetryAlert, db)).GetProperty("data");
            Assert.Equal($"Own telemetry alert {suffix}", ownAlertDetail.GetProperty("alert").GetProperty("body").GetString());
            Assert.Empty(ownAlertDetail.GetProperty("tasks").EnumerateArray());
            Assert.Equal(StatusCodes.Status404NotFound, Status(await InvokeAlertDetail(alertPrincipal, foreignTelemetryAlert, db)));
            Assert.Equal(StatusCodes.Status404NotFound, Status(await InvokeAlertAction(
                "AlertAcknowledge", alertPrincipal, foreignTelemetryAlert, new(), db)));

            var taskResult = Payload(await InvokeAlertAction(
                "AlertCreateTask", alertPrincipal, ownTelemetryAlert,
                new Dictionary<string, object?> { ["title"] = $"Review telemetry {suffix}" }, db));
            var taskId = taskResult.GetProperty("data").GetProperty("taskId").GetInt64();
            var taskSource = await db.QuerySingleAsync(
                "SELECT source_type FROM alert_follow_up_tasks WHERE id=@id AND company_id=@cid",
                c => { c.Parameters.AddWithValue("@id", taskId); c.Parameters.AddWithValue("@cid", company); });
            Assert.Equal("Telemetry", taskSource!["sourceType"]);

            Assert.Equal(StatusCodes.Status200OK, Status(await InvokeAlertAction(
                "AlertAcknowledge", alertPrincipal, ownTelemetryAlert,
                new Dictionary<string, object?> { ["note"] = "Reviewed in branch scope" }, db)));
            Assert.Equal(StatusCodes.Status200OK, Status(await InvokeAlertAction(
                "AlertClose", alertPrincipal, ownTelemetryAlert,
                new Dictionary<string, object?> { ["resolution"] = "Resolved in branch scope" }, db)));
            var resolvedAlert = Payload(await InvokeAlertDetail(alertPrincipal, ownTelemetryAlert, db)).GetProperty("data");
            Assert.Equal("Closed", resolvedAlert.GetProperty("alert").GetProperty("status").GetString());
            Assert.Single(resolvedAlert.GetProperty("tasks").EnumerateArray());
            var resolvedSummary = Payload(await InvokeAlertsSummary(alertPrincipal, db)).GetProperty("data");
            Assert.Equal(0, resolvedSummary.GetProperty("open").GetInt64());
            Assert.Equal(1, resolvedSummary.GetProperty("closed").GetInt64());

            var tenantPayload = Payload(await Invoke(Principal(company, null, "dashboard:view", "dashcam:view", "telematics:devices:view"), db));
            var tenantData = tenantPayload.GetProperty("data");
            Assert.Equal(3, tenantData.GetProperty("entities").GetArrayLength());
            Assert.Equal(3, tenantData.GetProperty("kpis").GetProperty("trackedEntities").GetInt64());
            Assert.Equal(1, tenantData.GetProperty("kpis").GetProperty("onlineDevices").GetInt64());
            Assert.Equal(0, tenantData.GetProperty("kpis").GetProperty("onlineCameras").GetInt64());
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

            var tenantAlerts = Payload(await InvokeAlertsList(
                Principal(company, null, "alerts:view"), db)).GetProperty("data");
            Assert.Equal(2, tenantAlerts.GetArrayLength());

            var dashboardOnly = Payload(await Invoke(Principal(company, branchA, "dashboard:view"), db)).GetProperty("data");
            Assert.Empty(dashboardOnly.GetProperty("safetyVideo").EnumerateArray());
            Assert.Equal("Unknown", dashboardOnly.GetProperty("entities")[0].GetProperty("deviceStatus").GetString());
            Assert.Equal(JsonValueKind.Null, dashboardOnly.GetProperty("kpis").GetProperty("onlineDevices").ValueKind);
            Assert.Equal(JsonValueKind.Null, dashboardOnly.GetProperty("kpis").GetProperty("onlineCameras").ValueKind);
        }
        finally
        {
            await db.ExecuteAsync("DELETE FROM operational_events WHERE company_id=@cid", c => c.Parameters.AddWithValue("@cid", company));
            await db.ExecuteAsync("DELETE FROM alert_follow_up_tasks WHERE company_id=@cid", c => c.Parameters.AddWithValue("@cid", company));
            await db.ExecuteAsync("DELETE FROM audit_logs WHERE company_id=@cid", c => c.Parameters.AddWithValue("@cid", company));
            await db.ExecuteAsync("DELETE FROM telemetry_alerts WHERE company_id=@cid", c => c.Parameters.AddWithValue("@cid", company));
            await db.ExecuteAsync("DELETE FROM location_events WHERE company_id=@cid", c => c.Parameters.AddWithValue("@cid", company));
            await db.ExecuteAsync("DELETE FROM geofences WHERE company_id=@cid", c => c.Parameters.AddWithValue("@cid", company));
            await db.ExecuteAsync("DELETE FROM dashcam_events WHERE company_id=@cid", c => c.Parameters.AddWithValue("@cid", company));
            await db.ExecuteAsync("DELETE FROM ai_recommendations WHERE tenant_id=@cid", c => c.Parameters.AddWithValue("@cid", company));
            await db.ExecuteAsync("DELETE FROM dispatch_exceptions WHERE company_id=@cid", c => c.Parameters.AddWithValue("@cid", company));
            await db.ExecuteAsync("DELETE FROM dispatch_assignments WHERE company_id=@cid", c => c.Parameters.AddWithValue("@cid", company));
            await db.ExecuteAsync("DELETE FROM jobs WHERE company_id=@cid", c => c.Parameters.AddWithValue("@cid", company));
            await db.ExecuteAsync("DELETE FROM device_installations WHERE company_id=@cid", c => c.Parameters.AddWithValue("@cid", company));
            await db.ExecuteAsync("DELETE FROM eld_devices WHERE company_id=@cid", c => c.Parameters.AddWithValue("@cid", company));
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

    private static async Task TelemetryDevice(Database db, long company, long branch, long vehicle, string serial)
    {
        var device = await db.InsertAsync(
            @"INSERT INTO eld_devices(company_id,device_serial,status,device_state,api_key_hash,hmac_secret_encrypted,hmac_key_version,last_seen_at)
              VALUES (@cid,@serial,'Active','Registered',encode(sha256(@serial::bytea),'hex'),repeat('b',32),1,NOW()-INTERVAL '1 minute')",
            c => { c.Parameters.AddWithValue("@cid", company); c.Parameters.AddWithValue("@serial", serial); });
        await db.ExecuteAsync(
            @"INSERT INTO device_installations(company_id,branch_id,device_id,vehicle_id,status,device_role,is_primary,effective_from,installed_at,source)
              VALUES (@cid,@branch,@device,@vehicle,'Installed','GPS',TRUE,NOW()-INTERVAL '2 hours',NOW()-INTERVAL '2 hours','control-tower-test')",
            c =>
            {
                c.Parameters.AddWithValue("@cid", company);
                c.Parameters.AddWithValue("@branch", branch);
                c.Parameters.AddWithValue("@device", device);
                c.Parameters.AddWithValue("@vehicle", vehicle);
            });
    }

    private static Task Location(Database db, long company, long vehicle, int speed) => db.ExecuteAsync(
        "INSERT INTO location_events(company_id,vehicle_id,lat,lng,speed_mph,event_type,event_time) VALUES (@cid,@vehicle,43,-79,@speed,'position',NOW())",
        c => { c.Parameters.AddWithValue("@cid", company); c.Parameters.AddWithValue("@vehicle", vehicle); c.Parameters.AddWithValue("@speed", speed); });

    private static Task<long> TelemetryAlert(Database db, long company, long vehicle, string message) => db.InsertAsync(
        "INSERT INTO telemetry_alerts(company_id,vehicle_id,alert_type,severity,message,status) VALUES (@cid,@vehicle,'branch.test','Warning',@message,'Open')",
        c => { c.Parameters.AddWithValue("@cid", company); c.Parameters.AddWithValue("@vehicle", vehicle); c.Parameters.AddWithValue("@message", message); });

    private static Task Geofence(Database db, long company, long? branch, string name) => db.ExecuteAsync(
        "INSERT INTO geofences(company_id,branch_id,name,geofence_type,center_lat,center_lng,radius_meters,status) VALUES (@cid,@branch,@name,'Circle',43,-79,500,'Active')",
        c => { c.Parameters.AddWithValue("@cid", company); c.Parameters.AddWithValue("@branch", (object?)branch ?? DBNull.Value); c.Parameters.AddWithValue("@name", name); });

    private static Task<long> Job(Database db, long company, long branch, string code) => db.InsertAsync(
        @"INSERT INTO jobs(company_id,branch_id,job_code,job_number,job_type,status,priority,sla_status,scheduled_start)
          VALUES (@cid,@branch,@code,@code,'Delivery','At Risk','High','At Risk',NOW())",
        c => { c.Parameters.AddWithValue("@cid", company); c.Parameters.AddWithValue("@branch", branch); c.Parameters.AddWithValue("@code", code); });

    private static Task<long> DispatchAssignment(Database db, long company, long branch, long job, long vehicle) => db.InsertAsync(
        @"INSERT INTO dispatch_assignments(company_id,branch_id,job_id,vehicle_id,status,assignment_status)
          VALUES (@cid,@branch,@job,@vehicle,'Assigned','assigned')",
        c =>
        {
            c.Parameters.AddWithValue("@cid", company);
            c.Parameters.AddWithValue("@branch", branch);
            c.Parameters.AddWithValue("@job", job);
            c.Parameters.AddWithValue("@vehicle", vehicle);
        });

    private static Task<long> DispatchException(Database db, long company, long assignment, long job, string title) => db.InsertAsync(
        @"INSERT INTO dispatch_exceptions(company_id,assignment_id,job_id,exception_type,severity,status,title)
          VALUES (@cid,@assignment,@job,'delay','High','open',@title)",
        c =>
        {
            c.Parameters.AddWithValue("@cid", company);
            c.Parameters.AddWithValue("@assignment", assignment);
            c.Parameters.AddWithValue("@job", job);
            c.Parameters.AddWithValue("@title", title);
        });

    private static Task<long> AgenticRecommendation(Database db, long company, long exception, long actionAssignment, string title) => db.InsertAsync(
        @"INSERT INTO ai_recommendations
            (company_id,tenant_id,recommendation_type,module_key,title,summary,body,confidence_score,urgency_score,
             impact_json,reason_json,proposed_action_json,risk_level,status,score,source_event_id,actor_type,actor_id)
          VALUES
            (@cid,@cid,'dispatch_copilot','dispatch',@title,'Recorded proposal','Recorded proposal',0.8,0.9,
             '{}'::jsonb,'{}'::jsonb,@action::jsonb,'high','proposed',0.8,@source,'agent','dispatch-copilot')",
        c =>
        {
            c.Parameters.AddWithValue("@cid", company);
            c.Parameters.AddWithValue("@title", title);
            c.Parameters.AddWithValue("@source", $"dispatch_exception:{exception}");
            c.Parameters.AddWithValue("@action", JsonSerializer.Serialize(new
            {
                action_type = "monitor",
                resource_type = "dispatch_assignment",
                resource_id = actionAssignment.ToString(),
                action_detail = "Observe the recorded exception",
            }));
        });

    private static Task OperationalEvent(Database db, long company, string type, long id, string title) => db.ExecuteAsync(
        "INSERT INTO operational_events(company_id,entity_type,entity_id,event_type,title,severity,event_time) VALUES (@cid,@type,@id,'branch.test',@title,'Warning',NOW())",
        c => { c.Parameters.AddWithValue("@cid", company); c.Parameters.AddWithValue("@type", type); c.Parameters.AddWithValue("@id", id); c.Parameters.AddWithValue("@title", title); });

    private static Task DashcamEvent(Database db, long company, long? vehicle, long? driver, string eventNumber, string authority, string mediaStatus) => db.ExecuteAsync(
        @"INSERT INTO dashcam_events(company_id,event_number,event_type,title,severity,vehicle_id,driver_id,occurred_at,source_authority,media_status,
            video_provider,provider_event_id,provider_received_at,provider_payload_hash,road_facing_media_ref)
          VALUES (@cid,@number,'branch.test',@number,'Warning',@vehicle,@driver,NOW(),@authority,@mediaStatus,
            @provider,@providerEvent,CASE WHEN @authoritative THEN NOW() END,@payloadHash,@mediaRef)",
        c =>
        {
            var authoritative = authority == "Authoritative";
            c.Parameters.AddWithValue("@cid", company);
            c.Parameters.AddWithValue("@number", eventNumber);
            c.Parameters.AddWithValue("@vehicle", (object?)vehicle ?? DBNull.Value);
            c.Parameters.AddWithValue("@driver", (object?)driver ?? DBNull.Value);
            c.Parameters.AddWithValue("@authority", authority);
            c.Parameters.AddWithValue("@mediaStatus", mediaStatus);
            c.Parameters.AddWithValue("@authoritative", authoritative);
            c.Parameters.AddWithValue("@provider", authoritative ? "test-provider" : DBNull.Value);
            c.Parameters.AddWithValue("@providerEvent", authoritative ? eventNumber : DBNull.Value);
            c.Parameters.AddWithValue("@payloadHash", authoritative ? new string('a', 64) : DBNull.Value);
            c.Parameters.AddWithValue("@mediaRef", authoritative && mediaStatus == "Ready" ? $"test-media:{eventNumber}" : DBNull.Value);
        });

    private static DefaultHttpContext Principal(long company, long? branch, params string[] permissions)
    {
        var http = new DefaultHttpContext();
        http.Items[EndpointMappings.AuthUserIdItemKey] = 41L;
        http.Items[EndpointMappings.AuthCompanyIdItemKey] = company;
        http.Items[EndpointMappings.AuthRoleItemKey] = branch is null ? "Tenant Administrator" : "Fleet Manager";
        http.Items[EndpointMappings.AuthPermissionsItemKey] = permissions.Length == 0 ? ["dashboard:view"] : permissions;
        if (branch is not null) http.Items[EndpointMappings.AuthBranchIdItemKey] = branch.Value;
        return http;
    }

    private static async Task<IResult> Invoke(DefaultHttpContext http, Database db)
    {
        var method = typeof(EndpointMappings).GetMethod("ControlTowerSummary", BindingFlags.NonPublic | BindingFlags.Static)!;
        return await (Task<IResult>)method.Invoke(null, [http, db, CancellationToken.None])!;
    }

    private static async Task<IResult> InvokeVehicleDetail(DefaultHttpContext http, long vehicleId, Database db)
    {
        var method = typeof(EndpointMappings).GetMethod("ControlTowerVehicleDetail", BindingFlags.NonPublic | BindingFlags.Static)!;
        return await (Task<IResult>)method.Invoke(null, [http, vehicleId, db, CancellationToken.None])!;
    }

    private static async Task<IResult> InvokeCommandCenter(DefaultHttpContext http, Database db)
    {
        var method = typeof(EndpointMappings).GetMethod("CommandCenterSummary", BindingFlags.NonPublic | BindingFlags.Static)!;
        return await (Task<IResult>)method.Invoke(null, [http, db, CancellationToken.None])!;
    }

    private static async Task<IResult> InvokeAlertsList(DefaultHttpContext http, Database db)
    {
        var method = typeof(EndpointMappings).GetMethod("AlertsList", BindingFlags.NonPublic | BindingFlags.Static)!;
        return await (Task<IResult>)method.Invoke(null, [http, db, CancellationToken.None])!;
    }

    private static async Task<IResult> InvokeAiInsights(DefaultHttpContext http, Database db)
    {
        var method = typeof(EndpointMappings).GetMethod("AiInsights", BindingFlags.NonPublic | BindingFlags.Static)!;
        return await (Task<IResult>)method.Invoke(null, [http, db, CancellationToken.None])!;
    }

    private static async Task<IResult> InvokeAgenticList(DefaultHttpContext http, Database db)
    {
        var method = typeof(EndpointMappings).GetMethod("AgenticRecommendationsList", BindingFlags.NonPublic | BindingFlags.Static)!;
        return await (Task<IResult>)method.Invoke(null, [http, db, CancellationToken.None])!;
    }

    private static async Task<IResult> InvokeAgenticApprove(DefaultHttpContext http, long recommendationId, Database db)
    {
        var method = typeof(EndpointMappings).GetMethod("AgenticRecommendationApprove", BindingFlags.NonPublic | BindingFlags.Static)!;
        return await (Task<IResult>)method.Invoke(null,
            [http, recommendationId, db, new Opstrax.Api.Services.AuditService(db), CancellationToken.None])!;
    }

    private static async Task<IResult> InvokeAlertsSummary(DefaultHttpContext http, Database db)
    {
        var method = typeof(EndpointMappings).GetMethod("AlertsSummary", BindingFlags.NonPublic | BindingFlags.Static)!;
        return await (Task<IResult>)method.Invoke(null, [http, db, CancellationToken.None])!;
    }

    private static async Task<IResult> InvokeAlertDetail(DefaultHttpContext http, long alertId, Database db)
    {
        var method = typeof(EndpointMappings).GetMethod("AlertDetail", BindingFlags.NonPublic | BindingFlags.Static)!;
        return await (Task<IResult>)method.Invoke(null, [http, alertId, db, CancellationToken.None])!;
    }

    private static async Task<IResult> InvokeAlertAction(
        string methodName,
        DefaultHttpContext http,
        long alertId,
        Dictionary<string, object?> body,
        Database db)
    {
        var method = typeof(EndpointMappings).GetMethod(methodName, BindingFlags.NonPublic | BindingFlags.Static)!;
        return await (Task<IResult>)method.Invoke(null, [http, alertId, body, db, new Opstrax.Api.Services.AuditService(db), CancellationToken.None])!;
    }

    private static int Status(IResult result)
        => Assert.IsAssignableFrom<IStatusCodeHttpResult>(result).StatusCode ?? StatusCodes.Status200OK;

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
