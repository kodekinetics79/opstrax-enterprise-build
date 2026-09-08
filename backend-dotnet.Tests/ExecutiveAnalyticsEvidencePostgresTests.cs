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
        await db.ExecuteAsync(File.ReadAllText(Path.Combine(RepoRoot, "database", "migrations", "2026_09_08_dispatch_analytics_evidence_integrity.sql")));
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
            var activeAssignment = await Assignment(db, company, "assigned", "user_workflow", "recorded_by_authenticated_actor");
            var deliveredAssignment = await Assignment(db, company, "delivered", "user_workflow", "recorded_by_authenticated_actor");
            var legacyAssignment = await Assignment(db, company, "assigned", "legacy_unverified", "unverified");
            await DispatchException(db, company, activeAssignment, "user_workflow", "recorded_by_authenticated_actor");
            await DispatchException(db, company, legacyAssignment, "legacy_unverified", "unverified");
            await DeliveryProof(db, company, deliveredAssignment);

            var http = Principal(company, "dashboard:view", "reports:view", "customer_portal:view", "dispatch:view", "safety:view", "maintenance:view");
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

            var operations = JsonSerializer.Serialize(Value(await Invoke("AnalyticsOperations", http, db, CancellationToken.None)));
            Assert.Contains("\"activeTrips\":null", operations);
            Assert.Contains("\"routeComplianceAvg\":null", operations);
            Assert.Contains("\"activeAssignments\":1", operations);
            Assert.Contains("\"openExceptions\":1", operations);
            Assert.Contains("authenticated workflow provenance", operations);

            var dispatch = JsonSerializer.Serialize(Value(await Invoke("AnalyticsDispatch", http, db, CancellationToken.None)));
            Assert.Contains("\"currentlyAssigned\":1", dispatch);
            Assert.Contains("\"delivered\":1", dispatch);
            Assert.Contains("\"openExceptions\":1", dispatch);
            Assert.Contains("\"proofsLast7d\":1", dispatch);
            Assert.DoesNotContain("legacy_unverified", dispatch);

            var safety = JsonSerializer.Serialize(Value(await Invoke("AnalyticsSafety", http, db, CancellationToken.None)));
            Assert.Contains("\"safetyEventsLast30d\":null", safety);
            Assert.Contains("\"topRiskDrivers\":[]", safety);

            var maintenance = JsonSerializer.Serialize(Value(await Invoke("AnalyticsMaintenance", http, db, CancellationToken.None)));
            Assert.Contains("\"vehiclesOutOfService\":null", maintenance);
            Assert.Contains("\"defectsByCategory\":[]", maintenance);
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

    private static Task<long> Assignment(Database db, long company, string status, string origin, string verification) => db.InsertAsync(
        @"INSERT INTO dispatch_assignments(company_id,status,assignment_status,assigned_at,created_at,updated_at,data_origin,verification_status)
          VALUES(@company,@status,@status,NOW(),NOW(),NOW(),@origin,@verification) RETURNING id",
        c => { c.Parameters.AddWithValue("@company", company); c.Parameters.AddWithValue("@status", status); c.Parameters.AddWithValue("@origin", origin); c.Parameters.AddWithValue("@verification", verification); });

    private static Task DispatchException(Database db, long company, long assignment, string origin, string verification) => db.ExecuteAsync(
        @"INSERT INTO dispatch_exceptions(company_id,assignment_id,exception_type,severity,status,created_at,data_origin,verification_status)
          VALUES(@company,@assignment,'customer_hold','High','open',NOW(),@origin,@verification)",
        c => { c.Parameters.AddWithValue("@company", company); c.Parameters.AddWithValue("@assignment", assignment); c.Parameters.AddWithValue("@origin", origin); c.Parameters.AddWithValue("@verification", verification); });

    private static async Task DeliveryProof(Database db, long company, long assignment)
    {
        var proof = await db.InsertAsync(
            @"INSERT INTO dispatch_proofs(company_id,assignment_id,proof_type,confirmed_at,confirmed_by_user_id,confirmed_by_driver_id,evidence_hash)
              VALUES(@company,@assignment,'delivery',NOW(),95,95,'sha256:test') RETURNING id",
            c => { c.Parameters.AddWithValue("@company", company); c.Parameters.AddWithValue("@assignment", assignment); });
        await db.ExecuteAsync(
            @"INSERT INTO dispatch_proof_artifacts(company_id,proof_id,kind,reference,content_type,size_bytes)
              VALUES(@company,@proof,'signature','test://signature','image/png',128)",
            c => { c.Parameters.AddWithValue("@company", company); c.Parameters.AddWithValue("@proof", proof); });
    }

    private static async Task Cleanup(Database db, long company, long other)
    {
        await db.ExecuteAsync(@"DELETE FROM sla_breaches WHERE tenant_id=@company OR tenant_id=@other;
            DELETE FROM sla_records WHERE company_id=@company OR company_id=@other;
            DELETE FROM executive_snapshots WHERE tenant_id=@company OR tenant_id=@other;
            DELETE FROM dispatch_proof_artifacts WHERE company_id=@company OR company_id=@other;
            DELETE FROM dispatch_proofs WHERE company_id=@company OR company_id=@other;
            DELETE FROM dispatch_exceptions WHERE company_id=@company OR company_id=@other;
            DELETE FROM dispatch_assignments WHERE company_id=@company OR company_id=@other;
            DELETE FROM vehicles WHERE company_id=@company OR company_id=@other;
            DELETE FROM customers WHERE company_id=@company OR company_id=@other;
            DELETE FROM companies WHERE id=@company OR id=@other;",
            c => { c.Parameters.AddWithValue("@company", company); c.Parameters.AddWithValue("@other", other); });
    }
}
