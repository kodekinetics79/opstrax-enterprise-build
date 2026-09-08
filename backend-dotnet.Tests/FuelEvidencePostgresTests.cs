using System.Collections;
using System.Reflection;
using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Opstrax.Api.Controllers;
using Opstrax.Api.Data;
using Opstrax.Api.Services;

namespace Opstrax.Tests;

[Trait("Category", "Integration")]
public sealed class FuelEvidencePostgresTests
{
    [Fact]
    public async Task FuelSurface_HidesDemoRows_SeparatesCurrencies_AndPersistsValidatedManualEvidence()
    {
        var db = Db();
        await new Batch5SchemaService(db).EnsureAsync();
        await db.ExecuteAsync(File.ReadAllText(Path.Combine(RepoRoot, "database", "migrations", "2026_09_08_fuel_evidence_integrity.sql")));
        var suffix = Guid.NewGuid().ToString("N")[..10];
        var company = await Company(db, $"FUEL-A-{suffix}");
        var foreignCompany = await Company(db, $"FUEL-B-{suffix}");
        var vehicle = await Vehicle(db, company, $"FUEL-V-A-{suffix}");
        var foreignVehicle = await Vehicle(db, foreignCompany, $"FUEL-V-B-{suffix}");
        try
        {
            var usd = await Fuel(db, company, vehicle, $"FUEL-USD-{suffix}", "USD", "Gallons", 10m, 4m, "manual_entry");
            _ = await Fuel(db, company, vehicle, $"FUEL-CAD-{suffix}", "CAD", "Liters", 5m, 2m, "manual_entry");
            _ = await Fuel(db, company, vehicle, $"FUEL-LEGACY-{suffix}", "USD", "Gallons", 1m, 54_321m, "legacy_unverified");
            _ = await Fuel(db, company, vehicle, $"FT-B5-{suffix}", "USD", "Gallons", 500m, 20m, "demo_seed");
            _ = await Fuel(db, foreignCompany, foreignVehicle, $"FUEL-X-{suffix}", "USD", "Gallons", 900m, 20m, "manual_entry");

            await Idling(db, company, vehicle, $"IDLE-MANUAL-{suffix}", "USD", 30m, 12m, "manual_entry");
            await Idling(db, company, vehicle, $"IDLE-1001", "USD", 600m, 9_999m, "demo_seed");
            var anomaly = await Anomaly(db, company, usd, vehicle, "USD", 15m, "runtime_detector");
            _ = await Anomaly(db, company, usd, vehicle, "USD", 88_888m, "demo_seed");
            var foreignAnomaly = await Anomaly(db, foreignCompany, null, foreignVehicle, "USD", 77_777m, "runtime_detector");

            var http = Principal(company);
            var audit = new AuditService(db);
            var rowsResult = await Invoke("FuelTransactions", http, db, CancellationToken.None);
            var rows = Data(rowsResult).Cast<Dictionary<string, object?>>().ToList();
            Assert.Equal(3, rows.Count);
            Assert.DoesNotContain(rows, row => row["transactionNumber"]?.ToString()?.StartsWith("FT-B5-") == true);
            Assert.Equal(2, rows.Count(row => row["recordOrigin"]?.ToString() == "Manual entry"));
            Assert.Single(rows, row => row["recordOrigin"]?.ToString() == "Legacy origin unverified");

            var summary = await Invoke("FuelSummary", http, db, CancellationToken.None);
            var summaryJson = JsonSerializer.Serialize(Value(summary));
            Assert.Contains("\"currency\":\"CAD\"", summaryJson);
            Assert.Contains("\"currency\":\"USD\"", summaryJson);
            Assert.Contains("\"spendToday\":10", summaryJson);
            Assert.Contains("\"spendToday\":40", summaryJson);
            Assert.Contains("\"durationMinutes\":30", summaryJson);
            Assert.Contains("\"recordedEstimatedCost\":12", summaryJson);
            Assert.Contains("\"fuelCardImportStatus\":\"Not configured\"", summaryJson);
            Assert.Contains("\"legacyUnverifiedTransactions\":1", summaryJson);
            Assert.DoesNotContain("9999", summaryJson);
            Assert.DoesNotContain("77777", summaryJson);
            Assert.DoesNotContain("54321", summaryJson);

            var detail = await Invoke("FuelTransactionDetail", http, usd, db, CancellationToken.None);
            var detailJson = JsonSerializer.Serialize(Value(detail));
            Assert.Contains("\"estimatedLoss\":15", detailJson);
            Assert.DoesNotContain("88888", detailJson);

            var invalidBody = new Dictionary<string, object?>
            {
                ["vehicleId"] = foreignVehicle, ["fuelDate"] = DateTime.UtcNow.Date.ToString("yyyy-MM-dd"),
                ["fuelType"] = "Diesel", ["quantity"] = 3m, ["unit"] = "Gallons", ["unitPrice"] = 4.25m,
                ["currency"] = "USD"
            };
            Assert.Equal(StatusCodes.Status400BadRequest,
                Status(await Invoke("CreateFuelTransaction", http, invalidBody, db, audit, CancellationToken.None)));

            var validBody = new Dictionary<string, object?>(invalidBody)
            {
                ["vehicleId"] = vehicle,
                ["transactionNumber"] = $"FUEL-NEW-{suffix}",
                ["totalCost"] = 999_999m,
                ["anomalyStatus"] = "Anomaly Detected"
            };
            var created = await Invoke("CreateFuelTransaction", http, validBody, db, audit, CancellationToken.None);
            Assert.Equal(StatusCodes.Status201Created, Status(created));
            var persisted = await db.QuerySingleAsync(
                "SELECT total_cost, anomaly_status, data_origin FROM fuel_transactions WHERE company_id=@company AND transaction_number=@number",
                c => { c.Parameters.AddWithValue("@company", company); c.Parameters.AddWithValue("@number", $"FUEL-NEW-{suffix}"); });
            Assert.Equal(12.75m, Convert.ToDecimal(persisted!["totalCost"]));
            Assert.Equal("Not Evaluated", persisted["anomalyStatus"]?.ToString());
            Assert.Equal("manual_entry", persisted["dataOrigin"]?.ToString());

            var invalidIdle = new Dictionary<string, object?>
            {
                ["vehicleId"] = foreignVehicle, ["durationMinutes"] = 12m,
                ["estimatedCost"] = 0m, ["currency"] = "USD"
            };
            Assert.Equal(StatusCodes.Status400BadRequest,
                Status(await Invoke("CreateIdlingEvent", http, invalidIdle, db, audit, CancellationToken.None)));
            var validIdle = new Dictionary<string, object?>(invalidIdle) { ["vehicleId"] = vehicle };
            Assert.Equal(StatusCodes.Status201Created,
                Status(await Invoke("CreateIdlingEvent", http, validIdle, db, audit, CancellationToken.None)));
            var recordedIdle = await db.QuerySingleAsync(
                "SELECT data_origin, cost_evidence_status FROM idling_events WHERE company_id=@company ORDER BY id DESC LIMIT 1",
                c => c.Parameters.AddWithValue("@company", company));
            Assert.Equal("manual_entry", recordedIdle!["dataOrigin"]?.ToString());
            Assert.Equal("Unavailable", recordedIdle["costEvidenceStatus"]?.ToString());

            Assert.Equal(StatusCodes.Status200OK,
                Status(await Invoke("FuelAnomalyReview", http, anomaly, new Dictionary<string, object?>(), db, audit, CancellationToken.None)));
            Assert.Equal(StatusCodes.Status409Conflict,
                Status(await Invoke("FuelAnomalyReview", http, anomaly, new Dictionary<string, object?>(), db, audit, CancellationToken.None)));
            Assert.Equal(StatusCodes.Status409Conflict,
                Status(await Invoke("FuelAnomalyReview", http, foreignAnomaly, new Dictionary<string, object?>(), db, audit, CancellationToken.None)));
            Assert.Equal(StatusCodes.Status501NotImplemented,
                Status(await Invoke("FuelImportPreview", http, new Dictionary<string, object?>(), CancellationToken.None)));
        }
        finally
        {
            await Cleanup(db, company, foreignCompany);
        }
    }

    private static string RepoRoot => Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../"));
    private static Database Db() => new(new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
    {
        ["ConnectionStrings:DefaultConnection"] = TestDb.ConnectionString,
        ["Rls:EnforceTenantContext"] = "false"
    }).Build());

    private static DefaultHttpContext Principal(long company)
    {
        var http = new DefaultHttpContext { TraceIdentifier = $"fuel-evidence-{Guid.NewGuid():N}" };
        http.Items[EndpointMappings.AuthUserIdItemKey] = 91L;
        http.Items[EndpointMappings.AuthCompanyIdItemKey] = company;
        http.Items[EndpointMappings.AuthRoleItemKey] = "Fleet Manager";
        http.Items[EndpointMappings.AuthPermissionsItemKey] = new[] { "fuel:view", "fuel:manage" };
        return http;
    }

    private static async Task<IResult> Invoke(string name, params object?[] args)
    {
        var method = typeof(EndpointMappings).GetMethod(name, BindingFlags.NonPublic | BindingFlags.Static)
            ?? throw new InvalidOperationException($"Missing endpoint {name}");
        try
        {
            return await ((Task<IResult>?)method.Invoke(null, args)
                ?? throw new InvalidOperationException($"{name} did not return Task<IResult>"));
        }
        catch (TargetInvocationException ex) when (ex.InnerException is not null)
        {
            System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(ex.InnerException).Throw();
            throw;
        }
    }

    private static int? Status(IResult result) => Assert.IsAssignableFrom<IStatusCodeHttpResult>(result).StatusCode;
    private static object Value(IResult result) => Assert.IsAssignableFrom<IValueHttpResult>(result).Value!
        .GetType().GetProperty("Data")!.GetValue(Assert.IsAssignableFrom<IValueHttpResult>(result).Value!)!;
    private static IEnumerable Data(IResult result) => Assert.IsAssignableFrom<IEnumerable>(Value(result));

    private static Task<long> Company(Database db, string code) => db.InsertAsync(
        "INSERT INTO companies(company_code,name,industry) VALUES(@code,@code,'Logistics') RETURNING id",
        c => c.Parameters.AddWithValue("@code", code));
    private static Task<long> Vehicle(Database db, long company, string code) => db.InsertAsync(
        "INSERT INTO vehicles(company_id,vehicle_code,type) VALUES(@company,@code,'Truck') RETURNING id",
        c => { c.Parameters.AddWithValue("@company", company); c.Parameters.AddWithValue("@code", code); });
    private static Task<long> Fuel(Database db, long company, long vehicle, string number, string currency, string unit, decimal quantity, decimal price, string origin) => db.InsertAsync(
        @"INSERT INTO fuel_transactions(company_id,vehicle_id,gallons,total_cost,transaction_number,fuel_date,fuel_type,quantity,unit,unit_price,currency,anomaly_status,data_origin)
          VALUES(@company,@vehicle,@quantity,@total,@number,CURRENT_DATE,'Diesel',@quantity,@unit,@price,@currency,'Not Evaluated',@origin) RETURNING id",
        c => { c.Parameters.AddWithValue("@company", company); c.Parameters.AddWithValue("@vehicle", vehicle); c.Parameters.AddWithValue("@quantity", quantity); c.Parameters.AddWithValue("@total", quantity * price); c.Parameters.AddWithValue("@number", number); c.Parameters.AddWithValue("@unit", unit); c.Parameters.AddWithValue("@price", price); c.Parameters.AddWithValue("@currency", currency); c.Parameters.AddWithValue("@origin", origin); });
    private static Task<long> Idling(Database db, long company, long vehicle, string number, string currency, decimal minutes, decimal cost, string origin) => db.InsertAsync(
        @"INSERT INTO idling_events(company_id,event_number,vehicle_id,duration_minutes,estimated_fuel_burn,estimated_cost,currency,threshold_status,data_origin,cost_evidence_status)
          VALUES(@company,@number,@vehicle,@minutes,0,@cost,@currency,'Excessive',@origin,'Recorded estimate') RETURNING id",
        c => { c.Parameters.AddWithValue("@company", company); c.Parameters.AddWithValue("@number", number); c.Parameters.AddWithValue("@vehicle", vehicle); c.Parameters.AddWithValue("@minutes", minutes); c.Parameters.AddWithValue("@cost", cost); c.Parameters.AddWithValue("@currency", currency); c.Parameters.AddWithValue("@origin", origin); });
    private static Task<long> Anomaly(Database db, long company, long? transaction, long vehicle, string currency, decimal loss, string origin) => db.InsertAsync(
        @"INSERT INTO fuel_anomalies(company_id,fuel_transaction_id,vehicle_id,anomaly_type,severity,description,estimated_loss,currency,data_origin,amount_evidence_status,status)
          VALUES(@company,@transaction,@vehicle,'Recorded variance','High','Runtime evidence',@loss,@currency,@origin,'Recorded estimate','Open') RETURNING id",
        c => { c.Parameters.AddWithValue("@company", company); c.Parameters.AddWithValue("@transaction", (object?)transaction ?? DBNull.Value); c.Parameters.AddWithValue("@vehicle", vehicle); c.Parameters.AddWithValue("@loss", loss); c.Parameters.AddWithValue("@currency", currency); c.Parameters.AddWithValue("@origin", origin); });

    private static async Task Cleanup(Database db, long company, long other)
    {
        foreach (var table in new[] { "audit_logs", "fuel_anomalies", "idling_events", "fuel_transactions", "vehicles" })
            await db.ExecuteAsync($"DELETE FROM {table} WHERE company_id=@company OR company_id=@other",
                c => { c.Parameters.AddWithValue("@company", company); c.Parameters.AddWithValue("@other", other); });
        await db.ExecuteAsync("DELETE FROM companies WHERE id=@company OR id=@other",
            c => { c.Parameters.AddWithValue("@company", company); c.Parameters.AddWithValue("@other", other); });
    }
}
