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
public sealed class ExecutiveAnalyticsEvidencePostgresTests
{
    [Fact]
    public async Task ExecutiveAnalytics_HidesUnverifiedSnapshots_AndUsesVerifiedSlaMeasurements()
    {
        var db = Db();
        await new Batch7SchemaService(db).EnsureAsync();
        await db.ExecuteAsync(File.ReadAllText(Path.Combine(RepoRoot, "database", "migrations", "2026_09_08_sla_kpi_evidence_integrity.sql")));
        await db.ExecuteAsync(File.ReadAllText(Path.Combine(RepoRoot, "database", "migrations", "2026_09_08_executive_analytics_evidence_integrity.sql")));
        var suffix = Guid.NewGuid().ToString("N")[..10];
        var company = await Company(db, $"EXEC-A-{suffix}");
        var other = await Company(db, $"EXEC-B-{suffix}");
        try
        {
            var customer = await Customer(db, company, $"CUS-{suffix}");
            await Vehicle(db, company, $"VEH-{suffix}");
            await Snapshot(db, company, DateTime.UtcNow.Date, "runtime_computed", "calculated_from_qualified_sources", 71);
            await Snapshot(db, company, DateTime.UtcNow.Date.AddDays(-1), "legacy_unverified", "unverified", 99);
            await Snapshot(db, company, DateTime.UtcNow.Date.AddDays(-2), "demo_seed", "demo_seed", 98);
            await Snapshot(db, other, DateTime.UtcNow.Date, "provider_import", "provider_verified", 97);

            await Sla(db, company, customer, $"SLA-VER-{suffix}", "job_derived", "calculated_from_events", "Met");
            await Sla(db, company, customer, $"SLA-LEG-{suffix}", "legacy_unverified", "unverified", "Breached");

            var http = Principal(company, "dashboard:view", "reports:view", "customer_portal:view");
            var snapshots = Data(await Invoke("ExecutiveSnapshots", http, db, CancellationToken.None))
                .Cast<Dictionary<string, object?>>().ToList();
            var snapshot = Assert.Single(snapshots);
            Assert.Equal("calculated_from_qualified_sources", snapshot["verificationStatus"]);
            Assert.Equal(71m, Convert.ToDecimal(snapshot["operationsHealthScore"]));

            var summary = JsonSerializer.Serialize(Value(await Invoke("ExecutiveSummary", http, db, CancellationToken.None)));
            Assert.Contains("calculated_from_qualified_sources", summary);
            Assert.DoesNotContain("legacy_unverified", summary);
            Assert.DoesNotContain("demo_seed", summary);

            var current = JsonSerializer.Serialize(Value(await Invoke("AnalyticsExecutive", http, db, CancellationToken.None)));
            Assert.Contains("\"vehicleTotal\":1", current);
            Assert.Contains("\"fleetUtilization\":null", current);
            Assert.Contains("\"onTimeDeliveryRate\":null", current);
            Assert.Contains("known generated fixtures excluded", current);

            var customerAnalytics = JsonSerializer.Serialize(Value(await Invoke("AnalyticsCustomer", http, db, CancellationToken.None)));
            Assert.Contains("\"slaTotal\":1", customerAnalytics);
            Assert.Contains("\"slaMet\":1", customerAnalytics);
            Assert.Contains("\"slaBreached\":0", customerAnalytics);
            Assert.Contains("\"metRate\":100", customerAnalytics);

            var trends = JsonSerializer.Serialize(Value(await Invoke("AnalyticsTrends", http, db, CancellationToken.None)));
            Assert.Contains("\"otdLast30d\":100", trends);
            Assert.Contains("\"otdLast7d\":100", trends);
            Assert.Contains("\"dispatchDailyTrend\":[]", trends);
            Assert.Contains("\"safetyDailyTrend\":[]", trends);

            Assert.Empty(Data(await Invoke("AnalyticsInsights", http, db, CancellationToken.None)).Cast<object>());
        }
        finally
        {
            await Cleanup(db, company, other);
        }
    }

    private static string RepoRoot => Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../"));
    private static Database Db() => new(new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
    {
        ["ConnectionStrings:DefaultConnection"] = TestDb.ConnectionString,
        ["Rls:EnforceTenantContext"] = "false"
    }).Build());

    private static DefaultHttpContext Principal(long company, params string[] permissions)
    {
        var http = new DefaultHttpContext { TraceIdentifier = $"executive-evidence-{Guid.NewGuid():N}" };
        http.Items[EndpointMappings.AuthUserIdItemKey] = 95L;
        http.Items[EndpointMappings.AuthCompanyIdItemKey] = company;
        http.Items[EndpointMappings.AuthRoleItemKey] = "Company Admin";
        http.Items[EndpointMappings.AuthPermissionsItemKey] = permissions;
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

    private static object Value(IResult result) => Assert.IsAssignableFrom<IValueHttpResult>(result).Value!
        .GetType().GetProperty("Data")!.GetValue(Assert.IsAssignableFrom<IValueHttpResult>(result).Value!)!;
    private static IEnumerable Data(IResult result) => Assert.IsAssignableFrom<IEnumerable>(Value(result));

    private static Task<long> Company(Database db, string code) => db.InsertAsync(
        "INSERT INTO companies(company_code,name,industry) VALUES(@code,@code,'Logistics') RETURNING id",
        c => c.Parameters.AddWithValue("@code", code));
    private static Task<long> Customer(Database db, long company, string code) => db.InsertAsync(
        "INSERT INTO customers(company_id,customer_code,name,status,sla_tier) VALUES(@company,@code,@code,'Active','Standard') RETURNING id",
        c => { c.Parameters.AddWithValue("@company", company); c.Parameters.AddWithValue("@code", code); });
    private static Task Vehicle(Database db, long company, string code) => db.ExecuteAsync(
        "INSERT INTO vehicles(company_id,vehicle_code,type,status,vin) VALUES(@company,@code,'Truck','Available',@code)",
        c => { c.Parameters.AddWithValue("@company", company); c.Parameters.AddWithValue("@code", code); });
    private static Task Snapshot(Database db, long company, DateTime day, string origin, string verification, decimal score) => db.ExecuteAsync(
        @"INSERT INTO executive_snapshots(tenant_id,snapshot_date,operations_health_score,cost_health_score,safety_health_score,compliance_health_score,customer_sla_score,fleet_readiness_score,dispatch_readiness_score,data_origin,verification_status)
          VALUES(@company,@day,@score,@score,@score,@score,@score,@score,@score,@origin,@verification)",
        c => { c.Parameters.AddWithValue("@company", company); c.Parameters.AddWithValue("@day", day); c.Parameters.AddWithValue("@score", score); c.Parameters.AddWithValue("@origin", origin); c.Parameters.AddWithValue("@verification", verification); });
    private static Task Sla(Database db, long company, long customer, string number, string origin, string evidence, string status) => db.ExecuteAsync(
        @"INSERT INTO sla_records(company_id,tenant_id,sla_number,customer_id,sla_type,metric_name,target_value,actual_value,unit,status,data_origin,measurement_evidence_status,measured_at)
          VALUES(@company,@company,@number,@customer,'On-Time Delivery',@number,100,100,'%',@status,@origin,@evidence,NOW())",
        c => { c.Parameters.AddWithValue("@company", company); c.Parameters.AddWithValue("@number", number); c.Parameters.AddWithValue("@customer", customer); c.Parameters.AddWithValue("@status", status); c.Parameters.AddWithValue("@origin", origin); c.Parameters.AddWithValue("@evidence", evidence); });

    private static async Task Cleanup(Database db, long company, long other)
    {
        await db.ExecuteAsync(@"DELETE FROM sla_breaches WHERE tenant_id=@company OR tenant_id=@other;
            DELETE FROM sla_records WHERE company_id=@company OR company_id=@other;
            DELETE FROM executive_snapshots WHERE tenant_id=@company OR tenant_id=@other;
            DELETE FROM vehicles WHERE company_id=@company OR company_id=@other;
            DELETE FROM customers WHERE company_id=@company OR company_id=@other;
            DELETE FROM companies WHERE id=@company OR id=@other;",
            c => { c.Parameters.AddWithValue("@company", company); c.Parameters.AddWithValue("@other", other); });
    }
}
