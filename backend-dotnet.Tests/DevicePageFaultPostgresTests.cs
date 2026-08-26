using System.Reflection;
using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Opstrax.Api.Controllers;
using Opstrax.Api.Data;

namespace Opstrax.Tests;

[Trait("Category", "Integration")]
public sealed class DevicePageFaultPostgresTests
{
    [Fact]
    public async Task DiagnosticsFreshnessUsesLatestApplicableEngineOrFaultEvidenceOnly()
    {
        var db = Db();
        var suffix = Guid.NewGuid().ToString("N")[..10];
        var companyId = await db.InsertAsync(
            "INSERT INTO companies(company_code,name,industry) VALUES (@code,'Diagnostic timeline truth','Transportation')",
            c => c.Parameters.AddWithValue("@code", $"DTL-{suffix}"));
        try
        {
            var branchId = await db.InsertAsync(
                "INSERT INTO branches(company_id,branch_code,name,status) VALUES (@c,'MAIN','Main','Active')",
                c => c.Parameters.AddWithValue("@c", companyId));
            var recentGpsVehicle = await Vehicle(db, companyId, branchId, $"GPS-{suffix}");
            var recentFaultVehicle = await Vehicle(db, companyId, branchId, $"FLT-{suffix}");
            var oldFaultSerial = $"OBD-OLD-FAULT-{suffix}";
            var newFaultSerial = $"OBD-NEW-FAULT-{suffix}";
            var oldFaultDevice = await Device(db, companyId, branchId, oldFaultSerial);
            var newFaultDevice = await Device(db, companyId, branchId, newFaultSerial);

            // Recent GPS-only position on the same source device must not refresh an
            // hour-old diagnostic fault because no engine fields accompany the fix.
            await db.ExecuteAsync(
                @"INSERT INTO latest_vehicle_positions(company_id,vehicle_id,device_id,lat,lng,event_time,received_at,protocol)
                  VALUES (@c,@v,@d,43.65,-79.38,NOW(),NOW(),'GPS')",
                c => { c.Parameters.AddWithValue("@c", companyId); c.Parameters.AddWithValue("@v", recentGpsVehicle); c.Parameters.AddWithValue("@d", oldFaultDevice); });
            await Fault(db, companyId, oldFaultSerial, "P1001", "1 hour");

            // Conversely a stale engine snapshot must not mask a newer active fault.
            await db.ExecuteAsync(
                @"INSERT INTO latest_vehicle_positions(company_id,vehicle_id,device_id,lat,lng,engine_status,event_time,received_at,protocol)
                  VALUES (@c,@v,@d,43.66,-79.39,'Running',NOW()-INTERVAL '1 hour',NOW()-INTERVAL '1 hour','OBD-II')",
                c => { c.Parameters.AddWithValue("@c", companyId); c.Parameters.AddWithValue("@v", recentFaultVehicle); c.Parameters.AddWithValue("@d", newFaultDevice); });
            await Fault(db, companyId, newFaultSerial, "P1002", "0 seconds");

            var http = Principal(companyId, branchId, "telematics:diagnostics:view");
            http.Request.QueryString = new QueryString("?cluster=diagnostics&view=fresh&pageSize=50");
            using var freshPayload = Payload(await Invoke("TelemetryDevicePage", http, db, CancellationToken.None));
            var freshData = freshPayload.RootElement.GetProperty("data");
            Assert.Equal(1, freshData.GetProperty("total").GetInt64());
            Assert.Equal(newFaultSerial, Assert.Single(freshData.GetProperty("items").EnumerateArray()).GetProperty("deviceSerial").GetString());

            http.Request.QueryString = new QueryString("?cluster=diagnostics&view=stale&pageSize=50");
            using var stalePayload = Payload(await Invoke("TelemetryDevicePage", http, db, CancellationToken.None));
            var staleData = stalePayload.RootElement.GetProperty("data");
            Assert.Equal(1, staleData.GetProperty("total").GetInt64());
            Assert.Equal(oldFaultSerial, Assert.Single(staleData.GetProperty("items").EnumerateArray()).GetProperty("deviceSerial").GetString());
        }
        finally
        {
            await db.ExecuteAsync("DELETE FROM fault_codes WHERE company_id=@cid", c => c.Parameters.AddWithValue("@cid", companyId));
            await db.ExecuteAsync("DELETE FROM latest_vehicle_positions WHERE company_id=@cid", c => c.Parameters.AddWithValue("@cid", companyId));
            await db.ExecuteAsync("DELETE FROM eld_devices WHERE company_id=@cid", c => c.Parameters.AddWithValue("@cid", companyId));
            await db.ExecuteAsync("DELETE FROM vehicles WHERE company_id=@cid", c => c.Parameters.AddWithValue("@cid", companyId));
            await db.ExecuteAsync("DELETE FROM branches WHERE company_id=@cid", c => c.Parameters.AddWithValue("@cid", companyId));
            await db.ExecuteAsync("DELETE FROM companies WHERE id=@cid", c => c.Parameters.AddWithValue("@cid", companyId));
        }
    }

    [Fact]
    public async Task GpsEvidenceOnSharedVehicleIsNeverAttributedToDifferentDiagnosticsDevice()
    {
        var db = Db();
        var suffix = Guid.NewGuid().ToString("N")[..10];
        var companyId = await db.InsertAsync(
            "INSERT INTO companies(company_code,name,industry) VALUES (@code,'Mixed telemetry attribution','Transportation')",
            c => c.Parameters.AddWithValue("@code", $"MIX-{suffix}"));
        try
        {
            var branchId = await db.InsertAsync(
                "INSERT INTO branches(company_id,branch_code,name,status) VALUES (@c,'MAIN','Main','Active')",
                c => c.Parameters.AddWithValue("@c", companyId));
            var vehicleId = await db.InsertAsync(
                @"INSERT INTO vehicles(company_id,branch_id,vehicle_code,type,vin_exception_type,alternate_identifier,status)
                  VALUES (@c,@b,@code,'Truck','legacy-fleet-identifier',@code,'Available')",
                c => { c.Parameters.AddWithValue("@c", companyId); c.Parameters.AddWithValue("@b", branchId); c.Parameters.AddWithValue("@code", $"V-{suffix}"); });
            var gpsSerial = $"GPS-{suffix}";
            var obdSerial = $"OBD-{suffix}";
            var gpsId = await db.InsertAsync(
                "INSERT INTO eld_devices(company_id,branch_id,device_serial,device_category,status,device_state) VALUES (@c,@b,@s,'GPS Tracker','Provisioning','Registered')",
                c => { c.Parameters.AddWithValue("@c", companyId); c.Parameters.AddWithValue("@b", branchId); c.Parameters.AddWithValue("@s", gpsSerial); });
            await db.InsertAsync(
                "INSERT INTO eld_devices(company_id,branch_id,device_serial,device_category,status,device_state) VALUES (@c,@b,@s,'OBD-II','Provisioning','Registered')",
                c => { c.Parameters.AddWithValue("@c", companyId); c.Parameters.AddWithValue("@b", branchId); c.Parameters.AddWithValue("@s", obdSerial); });
            await db.ExecuteAsync(
                @"INSERT INTO latest_vehicle_positions(company_id,vehicle_id,device_id,lat,lng,engine_status,event_time,received_at)
                  VALUES (@c,@v,@d,43.65,-79.38,'Running',NOW(),NOW())",
                c => { c.Parameters.AddWithValue("@c", companyId); c.Parameters.AddWithValue("@v", vehicleId); c.Parameters.AddWithValue("@d", gpsId); });

            var diagnostics = Principal(companyId, branchId, "telematics:diagnostics:view");
            diagnostics.Request.QueryString = new QueryString("?cluster=diagnostics&pageSize=50");
            using var diagnosticPayload = Payload(await Invoke("TelemetryDevicePage", diagnostics, db, CancellationToken.None));
            var diagnosticData = diagnosticPayload.RootElement.GetProperty("data");
            Assert.Equal(1, diagnosticData.GetProperty("total").GetInt64());
            var obd = Assert.Single(diagnosticData.GetProperty("items").EnumerateArray());
            Assert.Equal(obdSerial, obd.GetProperty("deviceSerial").GetString());
            Assert.Equal("none", obd.GetProperty("positionFreshness").GetString());
            Assert.Equal(1, diagnosticData.GetProperty("summary").GetProperty("offline").GetInt64());

            var gps = Principal(companyId, branchId, "telematics:gps:view");
            gps.Request.QueryString = new QueryString("?cluster=gps&pageSize=50");
            using var gpsPayload = Payload(await Invoke("TelemetryDevicePage", gps, db, CancellationToken.None));
            var gpsItems = gpsPayload.RootElement.GetProperty("data").GetProperty("items").EnumerateArray().ToArray();
            Assert.Equal(43.65m, gpsItems.Single(item => item.GetProperty("deviceSerial").GetString() == gpsSerial).GetProperty("positionLat").GetDecimal());
            Assert.Equal(JsonValueKind.Null, gpsItems.Single(item => item.GetProperty("deviceSerial").GetString() == obdSerial).GetProperty("positionLat").ValueKind);

            gps.Request.QueryString = new QueryString("?cluster=gps&view=offline&pageSize=50");
            using var offlinePayload = Payload(await Invoke("TelemetryDevicePage", gps, db, CancellationToken.None));
            var offlineData = offlinePayload.RootElement.GetProperty("data");
            Assert.Equal(1, offlineData.GetProperty("total").GetInt64());
            // Fleet summary stays scoped to the full authorized GPS cluster; only the
            // queue total/items shrink under a view filter.
            Assert.Equal(2, offlineData.GetProperty("summary").GetProperty("active").GetInt64());
            Assert.Equal(obdSerial, Assert.Single(offlineData.GetProperty("items").EnumerateArray()).GetProperty("deviceSerial").GetString());

            gps.Request.QueryString = new QueryString("?cluster=gps&view=stale-gps&pageSize=50");
            using var stalePayload = Payload(await Invoke("TelemetryDevicePage", gps, db, CancellationToken.None));
            Assert.Equal(0, stalePayload.RootElement.GetProperty("data").GetProperty("total").GetInt64());

            gps.Request.QueryString = new QueryString($"?cluster=gps&search={gpsSerial}&pageSize=50");
            using var searchPayload = Payload(await Invoke("TelemetryDevicePage", gps, db, CancellationToken.None));
            var searchData = searchPayload.RootElement.GetProperty("data");
            Assert.Equal(1, searchData.GetProperty("total").GetInt64());
            // Search narrows the queue, not the full-fleet KPI denominator.
            Assert.Equal(2, searchData.GetProperty("summary").GetProperty("active").GetInt64());
        }
        finally
        {
            await db.ExecuteAsync("DELETE FROM latest_vehicle_positions WHERE company_id=@cid", c => c.Parameters.AddWithValue("@cid", companyId));
            await db.ExecuteAsync("DELETE FROM eld_devices WHERE company_id=@cid", c => c.Parameters.AddWithValue("@cid", companyId));
            await db.ExecuteAsync("DELETE FROM vehicles WHERE company_id=@cid", c => c.Parameters.AddWithValue("@cid", companyId));
            await db.ExecuteAsync("DELETE FROM branches WHERE company_id=@cid", c => c.Parameters.AddWithValue("@cid", companyId));
            await db.ExecuteAsync("DELETE FROM companies WHERE id=@cid", c => c.Parameters.AddWithValue("@cid", companyId));
        }
    }

    [Fact]
    public async Task DiagnosticsAndFaultCountRecognizeCanonicalLowercaseActiveFault()
    {
        var db = Db();
        var companyId = await db.InsertAsync(
            "INSERT INTO companies(company_code,name,industry) VALUES (@code,'Device fault page test','Transportation')",
            c => c.Parameters.AddWithValue("@code", $"DFP-{Guid.NewGuid():N}"));
        try
        {
            var serial = $"LOWER-ACTIVE-{Guid.NewGuid():N}";
            await db.ExecuteAsync(
                "INSERT INTO eld_devices(company_id,device_serial,status,device_state) VALUES (@cid,@serial,'Provisioning','Registered')",
                c => { c.Parameters.AddWithValue("@cid", companyId); c.Parameters.AddWithValue("@serial", serial); });
            await db.ExecuteAsync(
                @"INSERT INTO fault_codes
                    (company_id,device_id,protocol,code,canonical_identity,last_observed_at,last_source_event_id,status)
                  VALUES (@cid,@serial,'OBD','P1000','OBD:UNKNOWN:P1000',NOW(),@event,'active')",
                c =>
                {
                    c.Parameters.AddWithValue("@cid", companyId);
                    c.Parameters.AddWithValue("@serial", serial);
                    c.Parameters.AddWithValue("@event", $"lower-active-{Guid.NewGuid():N}");
                });

            var http = Principal(companyId, null, "telematics:diagnostics:view");
            http.Request.QueryString = new QueryString("?cluster=diagnostics&pageSize=100");
            var result = await Invoke("TelemetryDevicePage", http, db, CancellationToken.None);
            Assert.Equal(StatusCodes.Status200OK, Assert.IsAssignableFrom<IStatusCodeHttpResult>(result).StatusCode);
            using var payload = Payload(result);
            var data = payload.RootElement.GetProperty("data");
            Assert.Equal(1, data.GetProperty("total").GetInt64());
            var item = Assert.Single(data.GetProperty("items").EnumerateArray());
            Assert.Equal(serial, item.GetProperty("deviceSerial").GetString());
            Assert.Equal(1, item.GetProperty("activeFaultCount").GetInt64());

            var deviceOnly = Principal(companyId);
            deviceOnly.Request.QueryString = new QueryString("?pageSize=100");
            using var devicePayload = Payload(await Invoke("TelemetryDevicePage", deviceOnly, db, CancellationToken.None));
            var safeItem = Assert.Single(devicePayload.RootElement.GetProperty("data").GetProperty("items").EnumerateArray());
            Assert.Equal(0, safeItem.GetProperty("activeFaultCount").GetInt64());
            Assert.False(safeItem.TryGetProperty("activeFaultCodes", out _));
            Assert.False(safeItem.TryGetProperty("positionLat", out _));

            deviceOnly.Request.QueryString = new QueryString("?search=P1000&pageSize=100");
            using var hiddenSearchPayload = Payload(await Invoke("TelemetryDevicePage", deviceOnly, db, CancellationToken.None));
            Assert.Equal(0, hiddenSearchPayload.RootElement.GetProperty("data").GetProperty("total").GetInt64());
        }
        finally
        {
            await db.ExecuteAsync("DELETE FROM fault_codes WHERE company_id=@cid", c => c.Parameters.AddWithValue("@cid", companyId));
            await db.ExecuteAsync("DELETE FROM eld_devices WHERE company_id=@cid", c => c.Parameters.AddWithValue("@cid", companyId));
            await db.ExecuteAsync("DELETE FROM companies WHERE id=@cid", c => c.Parameters.AddWithValue("@cid", companyId));
        }
    }

    private static DefaultHttpContext Principal(long companyId, long? branchId = null, params string[] permissions)
    {
        var http = new DefaultHttpContext();
        http.Items[EndpointMappings.AuthUserIdItemKey] = 41L;
        http.Items[EndpointMappings.AuthCompanyIdItemKey] = companyId;
        if (branchId.HasValue) http.Items[EndpointMappings.AuthBranchIdItemKey] = branchId.Value;
        http.Items[EndpointMappings.AuthRoleItemKey] = "Fleet Manager";
        http.Items[EndpointMappings.AuthPermissionsItemKey] = permissions.Length > 0 ? permissions : new[] { "telematics:devices:view" };
        return http;
    }

    private static async Task<IResult> Invoke(string name, params object[] args)
    {
        var method = typeof(EndpointMappings).GetMethod(name, BindingFlags.NonPublic | BindingFlags.Static)
            ?? throw new InvalidOperationException($"Missing endpoint {name}");
        return await ((Task<IResult>)method.Invoke(null, args)!);
    }

    private static Task<long> Vehicle(Database db, long companyId, long branchId, string code) => db.InsertAsync(
        @"INSERT INTO vehicles(company_id,branch_id,vehicle_code,type,vin_exception_type,alternate_identifier,status)
          VALUES (@c,@b,@code,'Truck','legacy-fleet-identifier',@code,'Available')",
        c => { c.Parameters.AddWithValue("@c", companyId); c.Parameters.AddWithValue("@b", branchId); c.Parameters.AddWithValue("@code", code); });

    private static Task<long> Device(Database db, long companyId, long branchId, string serial) => db.InsertAsync(
        "INSERT INTO eld_devices(company_id,branch_id,device_serial,device_category,status,device_state) VALUES (@c,@b,@s,'OBD-II','Provisioning','Registered')",
        c => { c.Parameters.AddWithValue("@c", companyId); c.Parameters.AddWithValue("@b", branchId); c.Parameters.AddWithValue("@s", serial); });

    private static Task<int> Fault(Database db, long companyId, string serial, string code, string age) => db.ExecuteAsync(
        @"INSERT INTO fault_codes(company_id,device_id,protocol,code,canonical_identity,last_observed_at,last_source_event_id,status)
          VALUES (@c,@s,'OBD',@code,'OBD:UNKNOWN:'||@code,NOW()-CAST(@age AS INTERVAL),@event,'active')",
        c =>
        {
            c.Parameters.AddWithValue("@c", companyId); c.Parameters.AddWithValue("@s", serial);
            c.Parameters.AddWithValue("@code", code); c.Parameters.AddWithValue("@age", age);
            c.Parameters.AddWithValue("@event", $"timeline-{Guid.NewGuid():N}");
        });

    private static JsonDocument Payload(IResult result)
    {
        var value = Assert.IsAssignableFrom<IValueHttpResult>(result).Value;
        return JsonDocument.Parse(JsonSerializer.Serialize(value, new JsonSerializerOptions(JsonSerializerDefaults.Web)));
    }

    private static Database Db() => new(new ConfigurationBuilder()
        .AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["ConnectionStrings:DefaultConnection"] = TestDb.ConnectionString,
            ["Rls:EnforceTenantContext"] = "false",
        }).Build(), new TenantScopeAccessor());
}
