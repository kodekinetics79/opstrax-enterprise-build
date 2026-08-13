using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Opstrax.Api.Controllers;
using Opstrax.Api.Data;
using Opstrax.Api.Services;
using System.Reflection;
using System.Text.Json;

namespace Opstrax.Tests;

public sealed class FleetTmsWorkspaceConcurrencyTests
{
    [Fact]
    [Trait("Category", "Integration")]
    public async Task ConcurrentDispatchWithoutAmbientRlsAllowsOnlyOneShipmentPerVehicleAndDriver()
    {
        var setup = Database();
        await new FleetTmsSchemaService(setup, NullLogger<FleetTmsSchemaService>.Instance).EnsureAsync();
        var companyId = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        const long branchId = 811;
        var canonical = await HasCanonicalCore(setup);
        try
        {
            if (canonical) await SeedCanonicalFleet(setup, companyId, branchId, "UNIT-811", "Driver 811");
            await setup.ExecuteAsync("INSERT INTO fleet_tms_vehicles(company_id,branch_id,vehicle_number,status) VALUES (@c,@b,'UNIT-811','Available')",
                c => { c.Parameters.AddWithValue("@c", companyId); c.Parameters.AddWithValue("@b", branchId); });
            var first = await InsertDispatchableShipment(setup, companyId, branchId, "SHIP-811-A");
            var second = await InsertDispatchableShipment(setup, companyId, branchId, "SHIP-811-B");

            var start = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            async Task<IResult> Dispatch(long shipmentId)
            {
                await start.Task;
                return await Invoke("DispatchShipment", Principal(companyId, branchId), shipmentId,
                    new DispatchShipmentRequest("UNIT-811", "Driver 811", "ROUTE-811", null), Database(), CancellationToken.None);
            }
            var attempts = new[] { Dispatch(first), Dispatch(second) };
            start.SetResult();
            await Task.WhenAll(attempts);

            Assert.Equal(1, await setup.ScalarLongAsync(
                "SELECT COUNT(*) FROM fleet_tms_shipments WHERE company_id=@c AND status='InTransit' AND vehicle_number='UNIT-811' AND driver_name='Driver 811'",
                c => c.Parameters.AddWithValue("@c", companyId)));
            Assert.Equal(1, await setup.ScalarLongAsync(
                "SELECT COUNT(*) FROM fleet_tms_shipments WHERE company_id=@c AND status='Booked'",
                c => c.Parameters.AddWithValue("@c", companyId)));
            Assert.Equal(1, await setup.ScalarLongAsync(
                "SELECT COUNT(DISTINCT shipment_id) FROM fleet_tms_driver_tasks WHERE company_id=@c AND status='Open'",
                c => c.Parameters.AddWithValue("@c", companyId)));
        }
        finally { await Cleanup(setup, companyId, canonical); }
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task CarrierAssignmentRacingDispatchCannotMutateShipmentAfterDispatch()
    {
        var setup = Database();
        await new FleetTmsSchemaService(setup, NullLogger<FleetTmsSchemaService>.Instance).EnsureAsync();
        await EnsureCarrierFixtureSchema(setup);
        var companyId = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        const long branchId = 812;
        var canonical = await HasCanonicalCore(setup);
        try
        {
            if (canonical) await SeedCanonicalFleet(setup, companyId, branchId, "UNIT-812", "Driver 812");
            var carrierId = await setup.InsertAsync("INSERT INTO carriers(company_id,name,carrier_number,status,compliance_status) VALUES (@c,'Carrier 812','CAR-812','Active','Compliant')",
                c => c.Parameters.AddWithValue("@c", companyId));
            await setup.ExecuteAsync("INSERT INTO fleet_tms_vehicles(company_id,branch_id,vehicle_number,status) VALUES (@c,@b,'UNIT-812','Available')",
                c => { c.Parameters.AddWithValue("@c", companyId); c.Parameters.AddWithValue("@b", branchId); });
            var shipmentId = await InsertDispatchableShipment(setup, companyId, branchId, "SHIP-812");

            var start = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            async Task<IResult> AssignCarrier()
            {
                await start.Task;
                return await Invoke("AssignShipmentCarrier", Principal(companyId, branchId), shipmentId,
                    new AssignCarrierRequest(carrierId, 500, 450, "Concurrent terms"), Database(), CancellationToken.None);
            }
            async Task<IResult> Dispatch()
            {
                await start.Task;
                return await Invoke("DispatchShipment", Principal(companyId, branchId), shipmentId,
                    new DispatchShipmentRequest("UNIT-812", "Driver 812", "ROUTE-812", null), Database(), CancellationToken.None);
            }
            var attempts = new[] { AssignCarrier(), Dispatch() };
            start.SetResult();
            await Task.WhenAll(attempts);

            var row = await setup.QuerySingleAsync("SELECT status,carrier_id FROM fleet_tms_shipments WHERE id=@id", c => c.Parameters.AddWithValue("@id", shipmentId));
            Assert.Equal("InTransit", row!["status"]);
            var carrierAfterRace = row["carrierId"] is DBNull ? (long?)null : Convert.ToInt64(row["carrierId"]);

            await Invoke("UnassignShipmentCarrier", Principal(companyId, branchId), shipmentId, Database(), CancellationToken.None);
            var afterLateAttempt = await setup.QuerySingleAsync("SELECT carrier_id FROM fleet_tms_shipments WHERE id=@id", c => c.Parameters.AddWithValue("@id", shipmentId));
            Assert.Equal(carrierAfterRace, afterLateAttempt!["carrierId"] is DBNull ? (long?)null : Convert.ToInt64(afterLateAttempt["carrierId"]));
        }
        finally { await Cleanup(setup, companyId, canonical, includeCarriers: true); }
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task TrackingReportsCanonicalLiveProvenanceOrLabelsWorkspaceProjectionNonLive()
    {
        var db = Database();
        await new FleetTmsSchemaService(db, NullLogger<FleetTmsSchemaService>.Instance).EnsureAsync();
        var companyId = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        const long branchId = 813;
        var canonical = await HasCanonicalTelemetry(db);
        try
        {
            if (canonical)
            {
                await SeedCompany(db, companyId);
                var vehicleId = await db.InsertAsync("INSERT INTO vehicles(company_id,branch_id,vehicle_code,type,vin_exception_type,alternate_identifier,status) VALUES (@c,@b,'UNIT-813','Truck','legacy-fleet-identifier','UNIT-813','Available')",
                    c => { c.Parameters.AddWithValue("@c", companyId); c.Parameters.AddWithValue("@b", branchId); });
                await db.ExecuteAsync("INSERT INTO fleet_tms_shipments(company_id,branch_id,shipment_number,status,vehicle_number) VALUES (@c,@b,'SHIP-813','InTransit','UNIT-813')",
                    c => { c.Parameters.AddWithValue("@c", companyId); c.Parameters.AddWithValue("@b", branchId); });
                await db.ExecuteAsync("INSERT INTO latest_vehicle_positions(company_id,vehicle_id,lat,lng,event_time,received_at,source) VALUES (@c,@v,43.65,-79.38,NOW(),NOW(),'pilot-gps')",
                    c => { c.Parameters.AddWithValue("@c", companyId); c.Parameters.AddWithValue("@v", vehicleId); });
            }
            else
            {
                await db.ExecuteAsync("INSERT INTO fleet_tms_tracking_points(company_id,branch_id,shipment_number,vehicle_number,latitude,longitude) VALUES (@c,@b,'SHIP-813','UNIT-813',43.65,-79.38)",
                    c => { c.Parameters.AddWithValue("@c", companyId); c.Parameters.AddWithValue("@b", branchId); });
            }

            var result = Assert.IsAssignableFrom<IValueHttpResult>(await Invoke("Tracking", Principal(companyId, branchId), db, "SHIP-813", 1, 20, CancellationToken.None));
            using var json = JsonDocument.Parse(JsonSerializer.Serialize(result.Value, new JsonSerializerOptions(JsonSerializerDefaults.Web)));
            var item = json.RootElement.GetProperty("data").GetProperty("items")[0];
            if (canonical)
            {
                Assert.Equal("canonical_latest_position", item.GetProperty("sourceType").GetString());
                Assert.True(item.GetProperty("isLive").GetBoolean());
                Assert.Equal("pilot-gps", item.GetProperty("source").GetString());
                Assert.Equal("Live", item.GetProperty("freshnessStatus").GetString());
            }
            else
            {
                Assert.Equal("workspace_projection", item.GetProperty("sourceType").GetString());
                Assert.False(item.GetProperty("isLive").GetBoolean());
                Assert.Equal("NonLive", item.GetProperty("freshnessStatus").GetString());
            }
        }
        finally { await Cleanup(db, companyId, canonical); }
    }

    private static async Task<long> InsertDispatchableShipment(Database db, long companyId, long branchId, string number)
    {
        var id = await db.InsertAsync("INSERT INTO fleet_tms_shipments(company_id,branch_id,shipment_number,status) VALUES (@c,@b,@n,'Booked')",
            c => { c.Parameters.AddWithValue("@c", companyId); c.Parameters.AddWithValue("@b", branchId); c.Parameters.AddWithValue("@n", number); });
        await db.ExecuteAsync("INSERT INTO fleet_tms_shipment_stops(company_id,branch_id,shipment_id,stop_type,sequence_no,location_name,planned_arrival_at) VALUES (@c,@b,@s,'Delivery',1,'Dock',NOW()+INTERVAL '1 hour')",
            c => { c.Parameters.AddWithValue("@c", companyId); c.Parameters.AddWithValue("@b", branchId); c.Parameters.AddWithValue("@s", id); });
        return id;
    }

    private static async Task SeedCanonicalFleet(Database db, long companyId, long branchId, string vehicle, string driver)
    {
        await SeedCompany(db, companyId);
        await db.ExecuteAsync("INSERT INTO drivers(company_id,branch_id,driver_code,full_name,status) VALUES (@c,@b,@code,@name,'Available')",
            c => { c.Parameters.AddWithValue("@c", companyId); c.Parameters.AddWithValue("@b", branchId); c.Parameters.AddWithValue("@code", $"D-{companyId}"); c.Parameters.AddWithValue("@name", driver); });
        await db.ExecuteAsync("INSERT INTO vehicles(company_id,branch_id,vehicle_code,type,vin_exception_type,alternate_identifier,status) VALUES (@c,@b,@vehicle,'Truck','legacy-fleet-identifier',@vehicle,'Available')",
            c => { c.Parameters.AddWithValue("@c", companyId); c.Parameters.AddWithValue("@b", branchId); c.Parameters.AddWithValue("@vehicle", vehicle); });
    }

    private static Task SeedCompany(Database db, long companyId) => db.ExecuteAsync(
        "INSERT INTO companies(id,company_code,name,industry) OVERRIDING SYSTEM VALUE VALUES (@c,@code,'Workspace Concurrency','Transportation')",
        c => { c.Parameters.AddWithValue("@c", companyId); c.Parameters.AddWithValue("@code", $"WSC-{companyId}"); });

    private static async Task Cleanup(Database db, long companyId, bool canonical, bool includeCarriers = false)
    {
        foreach (var table in new[] { "fleet_tms_driver_tasks", "fleet_tms_shipment_events", "fleet_tms_shipment_stops", "fleet_tms_tracking_points", "fleet_tms_shipments", "fleet_tms_vehicles" })
            await db.ExecuteAsync($"DELETE FROM {table} WHERE company_id=@c", c => c.Parameters.AddWithValue("@c", companyId));
        if (canonical)
        {
            if (await HasCanonicalTelemetry(db))
            {
                await db.ExecuteAsync("DELETE FROM latest_vehicle_positions WHERE company_id=@c", c => c.Parameters.AddWithValue("@c", companyId));
                await db.ExecuteAsync("DELETE FROM location_events WHERE company_id=@c", c => c.Parameters.AddWithValue("@c", companyId));
            }
            await db.ExecuteAsync("DELETE FROM vehicles WHERE company_id=@c", c => c.Parameters.AddWithValue("@c", companyId));
            await db.ExecuteAsync("DELETE FROM drivers WHERE company_id=@c", c => c.Parameters.AddWithValue("@c", companyId));
            if (includeCarriers) await db.ExecuteAsync("DELETE FROM carriers WHERE company_id=@c", c => c.Parameters.AddWithValue("@c", companyId));
            await db.ExecuteAsync("DELETE FROM companies WHERE id=@c", c => c.Parameters.AddWithValue("@c", companyId));
        }
        else if (includeCarriers)
            await db.ExecuteAsync("DELETE FROM carriers WHERE company_id=@c", c => c.Parameters.AddWithValue("@c", companyId));
    }

    private static async Task<bool> HasCanonicalCore(Database db) => await db.ScalarLongAsync(
        "SELECT CASE WHEN to_regclass('public.companies') IS NOT NULL AND to_regclass('public.drivers') IS NOT NULL AND to_regclass('public.vehicles') IS NOT NULL THEN 1 ELSE 0 END") == 1;
    private static async Task<bool> HasCanonicalTelemetry(Database db) => await db.ScalarLongAsync(
        "SELECT CASE WHEN to_regclass('public.companies') IS NOT NULL AND to_regclass('public.vehicles') IS NOT NULL AND to_regclass('public.latest_vehicle_positions') IS NOT NULL AND to_regclass('public.location_events') IS NOT NULL THEN 1 ELSE 0 END") == 1;

    private static DefaultHttpContext Principal(long companyId, long branchId)
    {
        var http = new DefaultHttpContext();
        http.Items[EndpointMappings.AuthCompanyIdItemKey] = companyId;
        http.Items[EndpointMappings.AuthBranchIdItemKey] = branchId;
        http.Items[EndpointMappings.AuthUserIdItemKey] = 42L;
        return http;
    }

    private static async Task<IResult> Invoke(string methodName, params object[] arguments)
    {
        var method = typeof(FleetTmsEndpoints).GetMethod(methodName, BindingFlags.NonPublic | BindingFlags.Static)!;
        return await (Task<IResult>)method.Invoke(null, arguments)!;
    }

    private static Database Database()
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["ConnectionStrings:DefaultConnection"] = TestDb.ConnectionString,
            ["Rls:EnforceTenantContext"] = "false",
        }).Build();
        return new Database(configuration);
    }

    private static Task<int> EnsureCarrierFixtureSchema(Database db) => db.ExecuteAsync(@"
CREATE TABLE IF NOT EXISTS carriers (
 id BIGINT GENERATED ALWAYS AS IDENTITY PRIMARY KEY, company_id BIGINT NOT NULL, name VARCHAR(220) NOT NULL,
 carrier_number VARCHAR(80), status VARCHAR(50) NOT NULL DEFAULT 'Active', compliance_status VARCHAR(80) NOT NULL DEFAULT 'Compliant',
 insurance_expiry DATE, on_time_percent DECIMAL(6,2) NOT NULL DEFAULT 90, safety_score DECIMAL(6,2) NOT NULL DEFAULT 88,
 cost_score DECIMAL(6,2) NOT NULL DEFAULT 82, performance_score DECIMAL(6,2) NOT NULL DEFAULT 86,
 risk_score DECIMAL(6,2) NOT NULL DEFAULT 20, notes TEXT, updated_at TIMESTAMPTZ, deleted_at TIMESTAMPTZ)");
}
