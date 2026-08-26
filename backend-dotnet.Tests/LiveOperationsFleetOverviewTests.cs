using System.Reflection;
using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Opstrax.Api.Controllers;
using Opstrax.Api.Data;

namespace Opstrax.Tests;

public sealed class LiveOperationsFleetOverviewTests
{
    [Theory]
    [InlineData("dashboard:view")]
    [InlineData("vehicles:view")]
    public async Task EndpointRequiresDashboardAndVehicleReadPermissions(string heldPermission)
    {
        var http = Principal(20, null, heldPermission);
        var result = await Invoke(http, Db("Host=127.0.0.1;Port=1;Database=unused;Username=unused;Password=unused"));

        Assert.Equal(StatusCodes.Status403Forbidden,
            Assert.IsAssignableFrom<IStatusCodeHttpResult>(result).StatusCode);
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task PagesClassifiesSearchesAndBranchScopesTheFullFleetSummary()
    {
        var db = Db(TestDb.ConnectionString);
        var companyId = await db.InsertAsync(
            "INSERT INTO companies(company_code,name,industry) VALUES (@code,'Live Ops paging test','Transportation')",
            command => command.Parameters.AddWithValue("@code", $"LOP-{Guid.NewGuid():N}"));
        var branchA = await db.InsertAsync(
            "INSERT INTO branches(company_id,branch_code,name,status) VALUES (@cid,@code,'Live Ops A','Active')",
            command => { command.Parameters.AddWithValue("@cid", companyId); command.Parameters.AddWithValue("@code", $"A{Guid.NewGuid():N}"[..20]); });
        var branchB = await db.InsertAsync(
            "INSERT INTO branches(company_id,branch_code,name,status) VALUES (@cid,@code,'Live Ops B','Active')",
            command => { command.Parameters.AddWithValue("@cid", companyId); command.Parameters.AddWithValue("@code", $"B{Guid.NewGuid():N}"[..20]); });

        try
        {
            for (var index = 1; index <= 61; index++)
                await Vehicle(db, companyId, branchA, $"OFF-{index:000}", "Available", "Offline");
            await Vehicle(db, companyId, branchA, "ON-001", "Active", "Online");
            await Vehicle(db, companyId, branchB, "OTHER-001", "Active", "Online");

            var firstPage = Json(await Invoke(
                Principal(companyId, branchA, "dashboard:view", "vehicles:view", "telemetry.devices.read"), db,
                "?page=1&pageSize=50&status=Offline&sort=vehicle"));
            Assert.Equal(61, firstPage.GetProperty("data").GetProperty("total").GetInt64());
            Assert.Equal(2, firstPage.GetProperty("data").GetProperty("pageCount").GetInt32());
            Assert.Equal(50, firstPage.GetProperty("data").GetProperty("items").GetArrayLength());
            Assert.Equal("OFF-001", firstPage.GetProperty("data").GetProperty("items")[0].GetProperty("vehicleCode").GetString());
            var summary = firstPage.GetProperty("data").GetProperty("summary");
            Assert.Equal(62, summary.GetProperty("total").GetInt64());
            Assert.Equal(61, summary.GetProperty("offline").GetInt64());
            Assert.Equal(1, summary.GetProperty("active").GetInt64());
            Assert.Equal(61, summary.GetProperty("deviceOffline").GetInt64());
            Assert.Equal(1, summary.GetProperty("deviceOnline").GetInt64());

            var secondPage = Json(await Invoke(
                Principal(companyId, branchA, "dashboard:view", "vehicles:view", "telemetry.devices.read"), db,
                "?page=2&pageSize=50&status=Offline&sort=vehicle"));
            Assert.Equal(11, secondPage.GetProperty("data").GetProperty("items").GetArrayLength());

            var searched = Json(await Invoke(
                Principal(companyId, branchA, "dashboard:view", "vehicles:view", "telemetry.devices.read"), db,
                "?page=1&pageSize=50&search=ON-001&status=All&sort=status"));
            Assert.Equal(1, searched.GetProperty("data").GetProperty("summary").GetProperty("total").GetInt64());
            Assert.Equal("Active", searched.GetProperty("data").GetProperty("items")[0].GetProperty("status").GetString());

            var vehicleOnly = Json(await Invoke(
                Principal(companyId, branchA, "dashboard:view", "vehicles:view"), db,
                "?page=1&pageSize=50&search=OFF-001&status=All"));
            Assert.Equal("Available", vehicleOnly.GetProperty("data").GetProperty("items")[0].GetProperty("status").GetString());
            Assert.Equal("Unknown", vehicleOnly.GetProperty("data").GetProperty("items")[0].GetProperty("deviceStatus").GetString());

            var tenantWide = Json(await Invoke(
                Principal(companyId, null, "dashboard:view", "vehicles:view", "telemetry.devices.read"), db,
                "?page=1&pageSize=100&status=All"));
            Assert.Equal(63, tenantWide.GetProperty("data").GetProperty("summary").GetProperty("total").GetInt64());
            Assert.Contains(tenantWide.GetProperty("data").GetProperty("items").EnumerateArray(),
                item => item.GetProperty("vehicleCode").GetString() == "OTHER-001");
        }
        finally
        {
            await db.ExecuteAsync("DELETE FROM vehicles WHERE company_id=@cid", command => command.Parameters.AddWithValue("@cid", companyId));
            await db.ExecuteAsync("DELETE FROM branches WHERE company_id=@cid", command => command.Parameters.AddWithValue("@cid", companyId));
            await db.ExecuteAsync("DELETE FROM companies WHERE id=@cid", command => command.Parameters.AddWithValue("@cid", companyId));
        }
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task ThousandVehicleFleetReturnsOnlyOneBoundedTruthfulPage()
    {
        var db = Db(TestDb.ConnectionString);
        var companyId = await db.InsertAsync(
            "INSERT INTO companies(company_code,name,industry) VALUES (@code,'Live Ops scale test','Transportation')",
            command => command.Parameters.AddWithValue("@code", $"LOS-{Guid.NewGuid():N}"));
        var branchId = await db.InsertAsync(
            "INSERT INTO branches(company_id,branch_code,name,status) VALUES (@cid,@code,'Scale Branch','Active')",
            command => { command.Parameters.AddWithValue("@cid", companyId); command.Parameters.AddWithValue("@code", $"S{Guid.NewGuid():N}"[..20]); });

        try
        {
            await db.ExecuteAsync(
                @"INSERT INTO vehicles(company_id,branch_id,vehicle_code,type,vin_exception_type,alternate_identifier,status,device_status,camera_status)
                  SELECT @cid,@branch,'SCALE-' || LPAD(series::text,4,'0'),'Truck','legacy-fleet-identifier',
                         'SCALE-' || LPAD(series::text,4,'0'),'Available',
                         CASE WHEN series=1000 THEN 'Online' ELSE 'Offline' END,'Online'
                    FROM generate_series(1,1000) series",
                command => { command.Parameters.AddWithValue("@cid", companyId); command.Parameters.AddWithValue("@branch", branchId); });

            var offlineVehicle = await db.ScalarLongAsync(
                "SELECT id FROM vehicles WHERE company_id=@cid AND vehicle_code='SCALE-0001'",
                command => command.Parameters.AddWithValue("@cid", companyId));
            var onlineVehicle = await db.ScalarLongAsync(
                "SELECT id FROM vehicles WHERE company_id=@cid AND vehicle_code='SCALE-1000'",
                command => command.Parameters.AddWithValue("@cid", companyId));
            var lifecycleOnlineDevice = await Device(db, companyId, branchId, "LIFECYCLE-ONLINE", "Online");
            var lifecycleOfflineDevice = await Device(db, companyId, branchId, "LIFECYCLE-OFFLINE", "Offline");
            await Installation(db, companyId, branchId, lifecycleOnlineDevice, offlineVehicle);
            await Installation(db, companyId, branchId, lifecycleOfflineDevice, onlineVehicle);

            var result = Json(await Invoke(
                Principal(companyId, branchId, "dashboard:view", "vehicles:view", "telemetry.devices.read"), db,
                "?page=1&pageSize=50&status=All&sort=vehicle"));
            var data = result.GetProperty("data");
            Assert.Equal(1000, data.GetProperty("total").GetInt64());
            Assert.Equal(20, data.GetProperty("pageCount").GetInt32());
            Assert.Equal(50, data.GetProperty("items").GetArrayLength());
            Assert.Equal(999, data.GetProperty("summary").GetProperty("offline").GetInt64());
            Assert.Equal(999, data.GetProperty("summary").GetProperty("deviceOffline").GetInt64());
            Assert.Equal(1, data.GetProperty("summary").GetProperty("available").GetInt64());
            Assert.Equal(1, data.GetProperty("summary").GetProperty("deviceOnline").GetInt64());
        }
        finally
        {
            await db.ExecuteAsync("DELETE FROM device_installations WHERE company_id=@cid", command => command.Parameters.AddWithValue("@cid", companyId));
            await db.ExecuteAsync("DELETE FROM eld_devices WHERE company_id=@cid", command => command.Parameters.AddWithValue("@cid", companyId));
            await db.ExecuteAsync("DELETE FROM vehicles WHERE company_id=@cid", command => command.Parameters.AddWithValue("@cid", companyId));
            await db.ExecuteAsync("DELETE FROM branches WHERE company_id=@cid", command => command.Parameters.AddWithValue("@cid", companyId));
            await db.ExecuteAsync("DELETE FROM companies WHERE id=@cid", command => command.Parameters.AddWithValue("@cid", companyId));
        }
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task DeviceStateRequiresPermissionAndEnabledTenantTelematicsEntitlement()
    {
        var db = Db(TestDb.ConnectionString);
        var companyId = await db.InsertAsync(
            @"INSERT INTO companies(company_code,name,industry,entitlement_policy_mode)
              VALUES (@code,'Live Ops entitlement test','Transportation','package_allowlist')",
            command => command.Parameters.AddWithValue("@code", $"LOE-{Guid.NewGuid():N}"));
        var branchId = await db.InsertAsync(
            "INSERT INTO branches(company_id,branch_code,name,status) VALUES (@cid,@code,'Entitlement Branch','Active')",
            command => { command.Parameters.AddWithValue("@cid", companyId); command.Parameters.AddWithValue("@code", $"E{Guid.NewGuid():N}"[..20]); });

        try
        {
            await db.ExecuteAsync(
                @"INSERT INTO vehicles(company_id,branch_id,vehicle_code,type,vin_exception_type,alternate_identifier,status,device_status,camera_status)
                  VALUES (@cid,@branch,'ENT-001','Truck','legacy-fleet-identifier','ENT-001','Available','Offline','Offline')",
                command => { command.Parameters.AddWithValue("@cid", companyId); command.Parameters.AddWithValue("@branch", branchId); });

            var principal = Principal(companyId, branchId, "dashboard:view", "vehicles:view", "telemetry.devices.read");
            var missingEntitlement = Json(await Invoke(principal, db, "?page=1&pageSize=50"));
            AssertRedactedDeviceState(missingEntitlement);

            await db.ExecuteAsync(
                "INSERT INTO tenant_entitlements(company_id,module_key,enabled) VALUES (@cid,'telematics',FALSE)",
                command => command.Parameters.AddWithValue("@cid", companyId));
            var disabledEntitlement = Json(await Invoke(principal, db, "?page=1&pageSize=50"));
            AssertRedactedDeviceState(disabledEntitlement);

            await db.ExecuteAsync(
                "UPDATE tenant_entitlements SET enabled=TRUE WHERE company_id=@cid AND module_key='telematics'",
                command => command.Parameters.AddWithValue("@cid", companyId));
            var enabledEntitlement = Json(await Invoke(principal, db, "?page=1&pageSize=50"));
            var enabledItem = enabledEntitlement.GetProperty("data").GetProperty("items")[0];
            Assert.Equal("Offline", enabledItem.GetProperty("status").GetString());
            Assert.Equal("Offline", enabledItem.GetProperty("deviceStatus").GetString());
            Assert.Equal("Camera offline", enabledItem.GetProperty("flag").GetString());
        }
        finally
        {
            await db.ExecuteAsync("DELETE FROM tenant_entitlements WHERE company_id=@cid", command => command.Parameters.AddWithValue("@cid", companyId));
            await db.ExecuteAsync("DELETE FROM vehicles WHERE company_id=@cid", command => command.Parameters.AddWithValue("@cid", companyId));
            await db.ExecuteAsync("DELETE FROM branches WHERE company_id=@cid", command => command.Parameters.AddWithValue("@cid", companyId));
            await db.ExecuteAsync("DELETE FROM companies WHERE id=@cid", command => command.Parameters.AddWithValue("@cid", companyId));
        }
    }

    [Fact]
    public void BrowserUsesServerBackedSearchSortAndAccessiblePaging()
    {
        var page = Source("frontend", "src", "pages", "FleetOverviewPage.tsx");
        var service = Source("frontend", "src", "services", "vehiclesApi.ts");

        Assert.Contains("/api/live-operations/fleet-overview", service, StringComparison.Ordinal);
        Assert.Contains("pageSize = 50", page, StringComparison.Ordinal);
        Assert.DoesNotContain("keepPreviousData", page, StringComparison.Ordinal);
        Assert.DoesNotContain("serverPage !== page", page, StringComparison.Ordinal);
        Assert.Contains("const displayPage = Number(vehiclesQ.data?.page ?? page)", page, StringComparison.Ordinal);
        Assert.Contains("aria-label=\"Previous fleet page\"", page, StringComparison.Ordinal);
        Assert.Contains("aria-label=\"Next fleet page\"", page, StringComparison.Ordinal);
        Assert.Contains("placeholder=\"Search vehicle or driver\"", page, StringComparison.Ordinal);
        Assert.Contains("aria-label=\"Sort fleet\"", page, StringComparison.Ordinal);
        Assert.Contains("setSortOrder", page, StringComparison.Ordinal);
        Assert.DoesNotContain("vehiclesApi.list()", page, StringComparison.Ordinal);
    }

    private static async Task Vehicle(Database db, long companyId, long branchId, string code, string status, string deviceStatus)
        => await db.ExecuteAsync(
            @"INSERT INTO vehicles(company_id,branch_id,vehicle_code,type,vin_exception_type,alternate_identifier,status,device_status,camera_status)
              VALUES (@cid,@branch,@code,'Truck','legacy-fleet-identifier',@code,@status,@device,'Online')",
            command =>
            {
                command.Parameters.AddWithValue("@cid", companyId);
                command.Parameters.AddWithValue("@branch", branchId);
                command.Parameters.AddWithValue("@code", code);
                command.Parameters.AddWithValue("@status", status);
                command.Parameters.AddWithValue("@device", deviceStatus);
            });

    private static Task<long> Device(Database db, long companyId, long branchId, string serial, string lifecycleState)
        => db.InsertAsync(
            @"INSERT INTO eld_devices(company_id,branch_id,device_serial,status,device_state,api_key_hash,hmac_secret_encrypted,hmac_key_version,created_at)
              VALUES (@cid,@branch,@serial,'Active',@state,encode(sha256(@serial::bytea),'hex'),repeat('b',32),1,NOW())",
            command =>
            {
                command.Parameters.AddWithValue("@cid", companyId);
                command.Parameters.AddWithValue("@branch", branchId);
                command.Parameters.AddWithValue("@serial", serial);
                command.Parameters.AddWithValue("@state", lifecycleState);
            });

    private static async Task Installation(Database db, long companyId, long branchId, long deviceId, long vehicleId)
        => await db.ExecuteAsync(
            @"INSERT INTO device_installations(company_id,branch_id,device_id,vehicle_id,status,device_role,is_primary,effective_from,installed_at,source)
              VALUES (@cid,@branch,@device,@vehicle,'Installed','GPS',TRUE,NOW(),NOW(),'test')",
            command =>
            {
                command.Parameters.AddWithValue("@cid", companyId);
                command.Parameters.AddWithValue("@branch", branchId);
                command.Parameters.AddWithValue("@device", deviceId);
                command.Parameters.AddWithValue("@vehicle", vehicleId);
            });

    private static void AssertRedactedDeviceState(JsonElement response)
    {
        var data = response.GetProperty("data");
        var item = data.GetProperty("items")[0];
        Assert.Equal("Available", item.GetProperty("status").GetString());
        Assert.Equal("Unknown", item.GetProperty("deviceStatus").GetString());
        Assert.Equal(JsonValueKind.Null, item.GetProperty("flag").ValueKind);
        Assert.Equal(0, data.GetProperty("summary").GetProperty("deviceOffline").GetInt64());
        Assert.Equal(1, data.GetProperty("summary").GetProperty("deviceUnknown").GetInt64());
    }

    private static DefaultHttpContext Principal(long companyId, long? branchId, params string[] permissions)
    {
        var http = new DefaultHttpContext();
        http.Items[EndpointMappings.AuthUserIdItemKey] = 41L;
        http.Items[EndpointMappings.AuthCompanyIdItemKey] = companyId;
        http.Items[EndpointMappings.AuthRoleItemKey] = branchId is null ? "Tenant Administrator" : "Fleet Manager";
        http.Items[EndpointMappings.AuthPermissionsItemKey] = permissions;
        if (branchId is not null) http.Items[EndpointMappings.AuthBranchIdItemKey] = branchId.Value;
        return http;
    }

    private static async Task<IResult> Invoke(DefaultHttpContext http, Database db, string query = "")
    {
        if (query.Length > 0) http.Request.QueryString = new QueryString(query);
        var method = typeof(EndpointMappings).GetMethod("LiveOperationsFleetOverview", BindingFlags.NonPublic | BindingFlags.Static)!;
        return await (Task<IResult>)method.Invoke(null, [http, db, CancellationToken.None])!;
    }

    private static JsonElement Json(IResult result)
    {
        var value = Assert.IsAssignableFrom<IValueHttpResult>(result).Value;
        return JsonDocument.Parse(JsonSerializer.Serialize(value, new JsonSerializerOptions(JsonSerializerDefaults.Web))).RootElement.Clone();
    }

    private static Database Db(string connectionString) => new(new ConfigurationBuilder()
        .AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["ConnectionStrings:DefaultConnection"] = connectionString,
            ["Rls:EnforceTenantContext"] = "false",
        }).Build(), new TenantScopeAccessor());

    private static string Source(params string[] parts)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !Directory.Exists(Path.Combine(directory.FullName, "backend-dotnet"))) directory = directory.Parent;
        Assert.NotNull(directory);
        return File.ReadAllText(Path.Combine([directory!.FullName, .. parts]));
    }
}
