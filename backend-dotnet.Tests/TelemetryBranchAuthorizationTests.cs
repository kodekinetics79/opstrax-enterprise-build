using System.Collections;
using System.Reflection;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Opstrax.Api.Controllers;
using Opstrax.Api.Data;
using Opstrax.Api.Services;

namespace Opstrax.Tests;

[Trait("Category", "Integration")]
public sealed class TelemetryBranchAuthorizationTests
{
    [Fact]
    public async Task BranchUserCannotReadOtherBranchLiveStateAlertsDevicesOrInstallationHistory()
    {
        var db = Db();
        var suffix = Guid.NewGuid().ToString("N")[..10];
        var company = await db.InsertAsync(
            "INSERT INTO companies(company_code,name,industry) VALUES (@code,'Telemetry branch authorization','Transportation')",
            c => c.Parameters.AddWithValue("@code", $"TEL-BR-{suffix}"));
        try
        {
            var branchA = await Branch(db, company, $"A-{suffix}");
            var branchB = await Branch(db, company, $"B-{suffix}");
            var vehicleA = await Vehicle(db, company, branchA, $"TEL-A-{suffix}");
            var vehicleB = await Vehicle(db, company, branchB, $"TEL-B-{suffix}");
            // Model a legitimate historical transfer: the device starts in Branch B,
            // is removed there, then ownership moves to Branch A before reinstall.
            var deviceA = await Device(db, company, branchB, $"TEL-DEV-A-{suffix}");
            var deviceB = await Device(db, company, branchB, $"TEL-DEV-B-{suffix}");

            var oldB = await db.InsertAsync(
                @"INSERT INTO device_installations
                    (company_id,branch_id,device_id,vehicle_id,status,device_role,is_primary,effective_from,effective_to,installed_at,removed_at,source)
                  VALUES (@c,@b,@d,@v,'Removed','GPS',TRUE,NOW()-INTERVAL '10 days',NOW()-INTERVAL '5 days',NOW()-INTERVAL '10 days',NOW()-INTERVAL '5 days','test')",
                c => { c.Parameters.AddWithValue("@c", company); c.Parameters.AddWithValue("@b", branchB); c.Parameters.AddWithValue("@d", deviceA); c.Parameters.AddWithValue("@v", vehicleB); });
            await db.ExecuteAsync(
                "UPDATE eld_devices SET branch_id=@b WHERE company_id=@c AND id=@d",
                c => { c.Parameters.AddWithValue("@c", company); c.Parameters.AddWithValue("@b", branchA); c.Parameters.AddWithValue("@d", deviceA); });
            await db.ExecuteAsync(
                @"INSERT INTO device_installations
                    (company_id,branch_id,device_id,vehicle_id,status,device_role,is_primary,effective_from,installed_at,source)
                  VALUES (@c,@b,@d,@v,'Installed','GPS',TRUE,NOW()-INTERVAL '1 day',NOW()-INTERVAL '1 day','test')",
                c => { c.Parameters.AddWithValue("@c", company); c.Parameters.AddWithValue("@b", branchA); c.Parameters.AddWithValue("@d", deviceA); c.Parameters.AddWithValue("@v", vehicleA); });
            await db.ExecuteAsync(
                @"INSERT INTO device_state_transitions(company_id,branch_id,device_id,from_state,to_state,reason_code,occurred_at)
                  VALUES (@c,@b,@d,'Registered','Online','other-branch-history',NOW()-INTERVAL '6 days'),
                         (@c,@a,@d,'Online','Idle','visible-branch-history',NOW())",
                c => { c.Parameters.AddWithValue("@c", company); c.Parameters.AddWithValue("@b", branchB); c.Parameters.AddWithValue("@a", branchA); c.Parameters.AddWithValue("@d", deviceA); });

            await LiveState(db, company, vehicleA, deviceA, $"TEL-A-{suffix}");
            await LiveState(db, company, vehicleB, deviceB, $"TEL-B-{suffix}");
            var alertA = await Alert(db, company, vehicleA, deviceA, "branch-a-alert");
            var alertB = await Alert(db, company, vehicleB, deviceB, "branch-b-alert");

            var telemetry = new TelemetryLiveStateService(db);
            var states = await telemetry.ListLiveStatesAsync(company, branchA);
            Assert.Equal(vehicleA, Convert.ToInt64(Assert.Single(states)["vehicleId"]));
            Assert.Null(await telemetry.GetLiveStateAsync(company, vehicleB, branchA));
            Assert.Equal(deviceA, Convert.ToInt64(Assert.Single(await telemetry.ListDevicesAsync(company, branchA))["id"]));

            var summary = await telemetry.BuildSummaryAsync(company, branchA);
            var alerts = Assert.IsAssignableFrom<IReadOnlyList<Dictionary<string, object?>>>(summary["alerts"]);
            Assert.Contains(alerts, row => row["message"]?.ToString() == "branch-a-alert");
            Assert.DoesNotContain(alerts, row => row["message"]?.ToString() == "branch-b-alert");

            var detail = await Invoke("DeviceDetail", Principal(company, branchA), deviceA, db, CancellationToken.None);
            var envelope = Assert.IsAssignableFrom<IValueHttpResult>(detail).Value!;
            var data = envelope.GetType().GetProperty("Data")!.GetValue(envelope)!;
            var detailDevice = Assert.IsType<Dictionary<string, object?>>(data.GetType().GetProperty("device")!.GetValue(data));
            Assert.True(Convert.ToInt64(detailDevice["secondsSincePing"]) >= 3_000,
                "Single-device detail must carry the same stale-check-in signal as the paged list.");
            Assert.Equal(1L, Convert.ToInt64(detailDevice["openAlertCount"]));
            Assert.Equal(0L, Convert.ToInt64(detailDevice["activeFaultCount"]));
            var history = Assert.IsAssignableFrom<IEnumerable>(data.GetType().GetProperty("installationHistory")!.GetValue(data));
            var rows = history.Cast<Dictionary<string, object?>>().ToArray();
            Assert.Single(rows);
            Assert.DoesNotContain(rows, row => Convert.ToInt64(row["id"]) == oldB);
            Assert.Equal(vehicleA, Convert.ToInt64(rows[0]["vehicleId"]));
            var transitions = Assert.IsAssignableFrom<IEnumerable>(data.GetType().GetProperty("assignmentHistory")!.GetValue(data));
            var transitionRows = transitions.Cast<Dictionary<string, object?>>().ToArray();
            Assert.Contains(transitionRows, row => row["reasonCode"]?.ToString() == "visible-branch-history");
            Assert.DoesNotContain(transitionRows, row => row["reasonCode"]?.ToString() == "other-branch-history");

            var branchPrincipal = Principal(company, branchA);
            var audit = new AuditService(db);
            Assert.Equal(StatusCodes.Status404NotFound, Status(await Invoke(
                "TelemetryAlertAcknowledge", branchPrincipal, alertB, db, audit, telemetry, CancellationToken.None)));
            Assert.Equal(StatusCodes.Status404NotFound, Status(await Invoke(
                "TelemetryAlertResolve", branchPrincipal, alertB, db, audit, telemetry, CancellationToken.None)));
            Assert.Equal(StatusCodes.Status200OK, Status(await Invoke(
                "TelemetryAlertAcknowledge", branchPrincipal, alertA, db, audit, telemetry, CancellationToken.None)));
            Assert.Equal(StatusCodes.Status200OK, Status(await Invoke(
                "TelemetryAlertResolve", branchPrincipal, alertA, db, audit, telemetry, CancellationToken.None)));

            var alertStatuses = await db.QueryAsync(
                "SELECT id,status FROM telemetry_alerts WHERE company_id=@c AND id IN (@a,@b) ORDER BY id",
                c => { c.Parameters.AddWithValue("@c", company); c.Parameters.AddWithValue("@a", alertA); c.Parameters.AddWithValue("@b", alertB); });
            Assert.Equal("Resolved", alertStatuses.Single(row => Convert.ToInt64(row["id"]) == alertA)["status"]);
            Assert.Equal("Open", alertStatuses.Single(row => Convert.ToInt64(row["id"]) == alertB)["status"]);
        }
        finally
        {
            foreach (var table in new[] { "device_installation_evidence", "device_installations", "device_state_transitions", "telemetry_alerts", "telemetry_live_asset_states", "latest_vehicle_positions", "eld_devices", "vehicles", "branches" })
                await db.ExecuteAsync($"DELETE FROM {table} WHERE company_id=@c", c => c.Parameters.AddWithValue("@c", company));
            await db.ExecuteAsync("DELETE FROM companies WHERE id=@c", c => c.Parameters.AddWithValue("@c", company));
        }
    }

    private static DefaultHttpContext Principal(long company, long branch)
    {
        var http = new DefaultHttpContext();
        http.Items[EndpointMappings.AuthCompanyIdItemKey] = company;
        http.Items[EndpointMappings.AuthBranchIdItemKey] = branch;
        http.Items[EndpointMappings.AuthUserIdItemKey] = 1L;
        http.Items[EndpointMappings.AuthRoleItemKey] = "Dispatcher";
        http.Items[EndpointMappings.AuthPermissionsItemKey] = new[] { "telematics:devices:view", "map:view", "alerts:view", "alerts:manage" };
        return http;
    }

    private static int Status(IResult result)
        => Assert.IsAssignableFrom<IStatusCodeHttpResult>(result).StatusCode ?? StatusCodes.Status200OK;

    private static async Task<IResult> Invoke(string name, params object[] args)
    {
        var method = typeof(EndpointMappings).GetMethod(name, BindingFlags.NonPublic | BindingFlags.Static)!;
        try { return await (Task<IResult>)method.Invoke(null, args)!; }
        catch (TargetInvocationException ex) when (ex.InnerException is not null)
        { System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(ex.InnerException).Throw(); throw; }
    }

    private static Task<long> Branch(Database db, long company, string code) => db.InsertAsync(
        "INSERT INTO branches(company_id,branch_code,name,status) VALUES (@c,@code,@code,'Active')",
        c => { c.Parameters.AddWithValue("@c", company); c.Parameters.AddWithValue("@code", code); });

    private static Task<long> Vehicle(Database db, long company, long branch, string code) => db.InsertAsync(
        @"INSERT INTO vehicles(company_id,branch_id,vehicle_code,type,vin_exception_type,alternate_identifier,status,availability_status,out_of_service)
          VALUES (@c,@b,@code,'Truck','legacy-fleet-identifier',@code,'Available','available',FALSE)",
        c => { c.Parameters.AddWithValue("@c", company); c.Parameters.AddWithValue("@b", branch); c.Parameters.AddWithValue("@code", code); });

    private static Task<long> Device(Database db, long company, long branch, string serial) => db.InsertAsync(
        @"INSERT INTO eld_devices(company_id,branch_id,device_serial,status,device_state,api_key_hash,hmac_secret_encrypted,hmac_key_version,last_seen_at,created_at)
          VALUES (@c,@b,@serial,'Active','Registered',encode(sha256(@serial::bytea),'hex'),repeat('b',32),1,NOW()-INTERVAL '1 hour',NOW())",
        c => { c.Parameters.AddWithValue("@c", company); c.Parameters.AddWithValue("@b", branch); c.Parameters.AddWithValue("@serial", serial); });

    private static Task LiveState(Database db, long company, long vehicle, long device, string code) => db.ExecuteAsync(
        @"INSERT INTO telemetry_live_asset_states(company_id,vehicle_id,device_id,vehicle_code,device_serial,lat,lng,received_at)
          VALUES (@c,@v,@d,@code,@code,40,-74,NOW())",
        c => { c.Parameters.AddWithValue("@c", company); c.Parameters.AddWithValue("@v", vehicle); c.Parameters.AddWithValue("@d", device); c.Parameters.AddWithValue("@code", code); });

    private static Task<long> Alert(Database db, long company, long vehicle, long device, string message) => db.InsertAsync(
        @"INSERT INTO telemetry_alerts(company_id,vehicle_id,device_id,alert_type,severity,message,status)
          VALUES (@c,@v,@d,'branch-test','Warning',@message,'Open')",
        c => { c.Parameters.AddWithValue("@c", company); c.Parameters.AddWithValue("@v", vehicle); c.Parameters.AddWithValue("@d", device); c.Parameters.AddWithValue("@message", message); });

    private static Database Db() => new(new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
    {
        ["ConnectionStrings:DefaultConnection"] = TestDb.ConnectionString,
        ["Rls:EnforceTenantContext"] = "false"
    }).Build());
}
