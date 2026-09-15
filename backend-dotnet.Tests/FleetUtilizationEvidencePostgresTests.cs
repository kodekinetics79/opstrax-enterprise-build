using System.Reflection;
using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Npgsql;
using Opstrax.Api.Controllers;
using Opstrax.Api.Data;

namespace Opstrax.Tests;

public sealed class FleetUtilizationEvidencePostgresTests
{
    [Fact]
    [Trait("Category", "Integration")]
    public async Task EndpointsFailClosedForMissingOrUnverifiedEvidenceAndRespectBranchScope()
    {
        var db = Database();
        var suffix = Guid.NewGuid().ToString("N")[..10];
        var company = await db.InsertAsync(
            "INSERT INTO companies(company_code,name,industry) VALUES (@code,'Fleet utilization evidence test','Transportation')",
            c => c.Parameters.AddWithValue("@code", $"FUE-{suffix}"));
        var branchA = await db.InsertAsync(
            "INSERT INTO branches(company_id,branch_code,name,status) VALUES (@cid,@code,@code,'Active')",
            c => { c.Parameters.AddWithValue("@cid", company); c.Parameters.AddWithValue("@code", $"A-{suffix}"); });
        var branchB = await db.InsertAsync(
            "INSERT INTO branches(company_id,branch_code,name,status) VALUES (@cid,@code,@code,'Active')",
            c => { c.Parameters.AddWithValue("@cid", company); c.Parameters.AddWithValue("@code", $"B-{suffix}"); });
        var ownVehicle = await Vehicle(db, company, branchA, $"OWN-{suffix}", "Available");
        var foreignVehicle = await Vehicle(db, company, branchB, $"FOREIGN-{suffix}", "Active");

        try
        {
            var principal = Principal(company, branchA);
            var initialRows = Data(await Invoke("FleetUtilizationList", principal, db)).EnumerateArray().ToArray();
            var initial = Assert.Single(initialRows);
            Assert.Equal($"OWN-{suffix}", initial.GetProperty("vehicleCode").GetString());
            Assert.Equal(JsonValueKind.Null, initial.GetProperty("utilizationPct").ValueKind);
            Assert.Equal(JsonValueKind.Null, initial.GetProperty("readinessScore").ValueKind);
            Assert.Equal(JsonValueKind.Null, initial.GetProperty("riskScore").ValueKind);
            Assert.Equal(JsonValueKind.Null, initial.GetProperty("idleMinutesToday").ValueKind);
            Assert.Equal(JsonValueKind.Null, initial.GetProperty("fuelCostMonth").ValueKind);
            Assert.Equal("no_qualified_trip_evidence", initial.GetProperty("utilizationBasis").GetString());

            await Trip(db, company, ownVehicle, "legacy_unverified", "unverified", 20);
            var stillUnverified = Assert.Single(Data(await Invoke("FleetUtilizationList", principal, db)).EnumerateArray());
            Assert.Equal(JsonValueKind.Null, stillUnverified.GetProperty("utilizationPct").ValueKind);
            await Assert.ThrowsAsync<PostgresException>(() =>
                Trip(db, company, ownVehicle, "runtime_route_projection", "unverified", 1));

            await Trip(db, company, ownVehicle, "runtime_route_projection", "derived_from_recorded_route", 12);
            await Trip(db, company, foreignVehicle, "runtime_route_projection", "derived_from_recorded_route", 24);
            await Fuel(db, company, ownVehicle, 12m, 48m);
            await Fuel(db, company, foreignVehicle, 50m, 200m);
            await Idle(db, company, ownVehicle, 60m, 15m);
            await Idle(db, company, foreignVehicle, 300m, 75m);

            var qualified = Assert.Single(Data(await Invoke("FleetUtilizationList", principal, db)).EnumerateArray());
            Assert.Equal($"OWN-{suffix}", qualified.GetProperty("vehicleCode").GetString());
            Assert.Equal(5m, qualified.GetProperty("utilizationPct").GetDecimal());
            Assert.Equal(12m, qualified.GetProperty("activeHours30d").GetDecimal());
            Assert.Equal("qualified", qualified.GetProperty("idleEvidenceStatus").GetString());
            Assert.Equal(60m, qualified.GetProperty("idleMinutesToday").GetDecimal());
            Assert.Equal("qualified", qualified.GetProperty("fuelEvidenceStatus").GetString());
            Assert.Equal(48m, qualified.GetProperty("fuelCostMonth").GetDecimal());

            var summary = Data(await Invoke("FleetUtilizationSummary", principal, db));
            Assert.Equal(1, summary.GetProperty("totalVehicles").GetInt64());
            Assert.Equal(1, summary.GetProperty("utilizationEvidenceVehicles").GetInt64());
            Assert.Equal(5m, summary.GetProperty("avgUtilizationPct").GetDecimal());
            Assert.Equal(JsonValueKind.Null, summary.GetProperty("avgReadiness").ValueKind);
            Assert.Equal(1m, summary.GetProperty("idleHoursToday").GetDecimal());
            Assert.Equal(15m, summary.GetProperty("idleCostToday").GetDecimal());
            Assert.Equal(48m, summary.GetProperty("fuelSpendMonth").GetDecimal());
        }
        finally
        {
            await db.ExecuteAsync("DELETE FROM idling_events WHERE company_id=@cid", c => c.Parameters.AddWithValue("@cid", company));
            await db.ExecuteAsync("DELETE FROM fuel_transactions WHERE company_id=@cid", c => c.Parameters.AddWithValue("@cid", company));
            await db.ExecuteAsync("DELETE FROM trips WHERE company_id=@cid", c => c.Parameters.AddWithValue("@cid", company));
            await db.ExecuteAsync("DELETE FROM vehicles WHERE company_id=@cid", c => c.Parameters.AddWithValue("@cid", company));
            await db.ExecuteAsync("DELETE FROM branches WHERE company_id=@cid", c => c.Parameters.AddWithValue("@cid", company));
            await db.ExecuteAsync("DELETE FROM companies WHERE id=@cid", c => c.Parameters.AddWithValue("@cid", company));
        }
    }

    private static Task<long> Vehicle(Database db, long company, long branch, string code, string status) => db.InsertAsync(
        @"INSERT INTO vehicles(company_id,branch_id,vehicle_code,type,vin_exception_type,alternate_identifier,status,risk_score,readiness_score)
          VALUES (@cid,@branch,@code,'Truck','legacy-fleet-identifier',@code,@status,99,99)",
        c =>
        {
            c.Parameters.AddWithValue("@cid", company);
            c.Parameters.AddWithValue("@branch", branch);
            c.Parameters.AddWithValue("@code", code);
            c.Parameters.AddWithValue("@status", status);
        });

    private static Task<long> Trip(Database db, long company, long vehicle, string origin, string verification, int hours) => db.InsertAsync(
        @"INSERT INTO trips(company_id,vehicle_id,status,started_at,completed_at,data_origin,verification_status)
          VALUES (@cid,@vehicle,'completed',NOW()-make_interval(hours => @hours),NOW(),@origin,@verification)",
        c =>
        {
            c.Parameters.AddWithValue("@cid", company);
            c.Parameters.AddWithValue("@vehicle", vehicle);
            c.Parameters.AddWithValue("@hours", hours);
            c.Parameters.AddWithValue("@origin", origin);
            c.Parameters.AddWithValue("@verification", verification);
        });

    private static Task<long> Fuel(Database db, long company, long vehicle, decimal quantity, decimal cost) => db.InsertAsync(
        @"INSERT INTO fuel_transactions(company_id,vehicle_id,gallons,quantity,unit_price,total_cost,fuel_date,data_origin,verification_status)
          VALUES (@cid,@vehicle,@quantity,@quantity,@price,@cost,CURRENT_DATE,'manual_entry','recorded_by_authenticated_actor')",
        c =>
        {
            c.Parameters.AddWithValue("@cid", company);
            c.Parameters.AddWithValue("@vehicle", vehicle);
            c.Parameters.AddWithValue("@quantity", quantity);
            c.Parameters.AddWithValue("@price", cost / quantity);
            c.Parameters.AddWithValue("@cost", cost);
        });

    private static Task<long> Idle(Database db, long company, long vehicle, decimal minutes, decimal cost) => db.InsertAsync(
        @"INSERT INTO idling_events(company_id,event_number,vehicle_id,started_at,duration_minutes,estimated_fuel_burn,estimated_cost,
            data_origin,verification_status,cost_evidence_status)
          VALUES (@cid,@number,@vehicle,NOW(),@minutes,1,@cost,'manual_entry','recorded_by_authenticated_actor','Recorded estimate')",
        c =>
        {
            c.Parameters.AddWithValue("@cid", company);
            c.Parameters.AddWithValue("@number", $"IDLE-TEST-{Guid.NewGuid():N}");
            c.Parameters.AddWithValue("@vehicle", vehicle);
            c.Parameters.AddWithValue("@minutes", minutes);
            c.Parameters.AddWithValue("@cost", cost);
        });

    private static DefaultHttpContext Principal(long company, long branch)
    {
        var http = new DefaultHttpContext();
        http.Items[EndpointMappings.AuthUserIdItemKey] = 41L;
        http.Items[EndpointMappings.AuthCompanyIdItemKey] = company;
        http.Items[EndpointMappings.AuthRoleItemKey] = "Fleet Manager";
        http.Items[EndpointMappings.AuthPermissionsItemKey] = new[] { "fleet:view" };
        http.Items[EndpointMappings.AuthBranchIdItemKey] = branch;
        return http;
    }

    private static async Task<IResult> Invoke(string methodName, DefaultHttpContext http, Database db)
    {
        var method = typeof(EndpointMappings).GetMethod(methodName, BindingFlags.NonPublic | BindingFlags.Static)!;
        return await (Task<IResult>)method.Invoke(null, [http, db, CancellationToken.None])!;
    }

    private static JsonElement Data(IResult result)
    {
        var value = Assert.IsAssignableFrom<IValueHttpResult>(result).Value;
        var payload = JsonDocument.Parse(JsonSerializer.Serialize(value, new JsonSerializerOptions(JsonSerializerDefaults.Web))).RootElement.Clone();
        return payload.GetProperty("data");
    }

    private static Database Database() => new(new ConfigurationBuilder()
        .AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["ConnectionStrings:DefaultConnection"] = TestDb.ConnectionString,
            ["Rls:EnforceTenantContext"] = "false",
        }).Build(), new TenantScopeAccessor());
}
