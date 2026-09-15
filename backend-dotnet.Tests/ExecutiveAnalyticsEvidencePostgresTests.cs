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
        await new Batch3SchemaService(db).EnsureAsync();
        await new MaintenanceSchemaService(db).EnsureAsync();
        await new Batch4SchemaService(db).EnsureAsync();
        await db.ExecuteAsync(File.ReadAllText(Path.Combine(RepoRoot, "database", "migrations", "2026_09_08_sla_kpi_evidence_integrity.sql")));
        await db.ExecuteAsync(File.ReadAllText(Path.Combine(RepoRoot, "database", "migrations", "2026_09_08_executive_analytics_evidence_integrity.sql")));
        await db.ExecuteAsync(File.ReadAllText(Path.Combine(RepoRoot, "database", "migrations", "2026_09_08_dispatch_analytics_evidence_integrity.sql")));
        await db.ExecuteAsync(File.ReadAllText(Path.Combine(RepoRoot, "database", "migrations", "2026_09_08_safety_analytics_evidence_integrity.sql")));
        await db.ExecuteAsync(File.ReadAllText(Path.Combine(RepoRoot, "database", "migrations", "2026_09_08_maintenance_analytics_evidence_integrity.sql")));
        var suffix = Guid.NewGuid().ToString("N")[..10];
        var company = await Company(db, $"EXEC-A-{suffix}");
        var other = await Company(db, $"EXEC-B-{suffix}");
        try
        {
            await new MaintenanceSchemaService(db).EnsureAsync();
            Assert.Equal(0, await db.ScalarLongAsync(
                "SELECT COUNT(*) FROM maintenance_pm_rules WHERE company_id=@company",
                c => c.Parameters.AddWithValue("@company", company)));
            var customer = await Customer(db, company, $"CUS-{suffix}");
            var vehicle = await Vehicle(db, company, $"VEH-{suffix}");
            var driver = await Driver(db, company, $"DRV-REAL-{suffix}");
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
            var qualifiedSafety = await SafetyEvent(db, company, driver, $"SAFE-VER-{suffix}", "Critical", "user_workflow", "recorded_by_authenticated_actor");
            var legacySafety = await SafetyEvent(db, company, driver, $"SAFE-LEG-{suffix}", "High", "legacy_unverified", "unverified");
            await Coaching(db, company, driver, qualifiedSafety, $"COACH-VER-{suffix}", "user_workflow", "recorded_by_authenticated_actor");
            await Coaching(db, company, driver, legacySafety, $"COACH-BADLINK-{suffix}", "user_workflow", "recorded_by_authenticated_actor");
            await Coaching(db, company, driver, null, $"COACH-LEG-{suffix}", "legacy_unverified", "unverified");
            await MaintenanceItem(db, company, vehicle, $"PM-VER-{suffix}", "user_workflow", "recorded_by_authenticated_actor");
            await MaintenanceItem(db, company, vehicle, $"PM-LEG-{suffix}", "legacy_unverified", "unverified");
            await WorkOrder(db, company, vehicle, $"WO-VER-{suffix}", "user_workflow", "recorded_by_authenticated_actor");
            await WorkOrder(db, company, vehicle, $"WO-LEG-{suffix}", "legacy_unverified", "unverified");
            var qualifiedDvir = await Dvir(db, company, driver, vehicle, $"DVIR-VER-{suffix}", "user_workflow", "recorded_by_authenticated_actor");
            var legacyDvir = await Dvir(db, company, driver, vehicle, $"DVIR-LEG-{suffix}", "legacy_unverified", "unverified");
            await DvirDefect(db, company, vehicle, qualifiedDvir, "Brakes", "dvir_workflow", "derived_from_qualified_source");
            await DvirDefect(db, company, vehicle, legacyDvir, "Tires", "dvir_workflow", "derived_from_qualified_source");

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
            Assert.Contains("\"safetyEventsLast30d\":1", safety);
            Assert.Contains("\"criticalEvents\":1", safety);
            Assert.Contains("\"openCoachingTasks\":1", safety);
            Assert.Contains("\"driverSafetyAvg\":null", safety);
            Assert.Contains($"\"driverCode\":\"DRV-REAL-{suffix}\"", safety);
            Assert.DoesNotContain("safetyScore", safety);

            var maintenance = JsonSerializer.Serialize(Value(await Invoke("AnalyticsMaintenance", http, db, CancellationToken.None)));
            Assert.Contains("\"vehiclesOutOfService\":1", maintenance);
            Assert.Contains("\"criticalDefectsOpen\":1", maintenance);
            Assert.Contains("\"openWorkOrders\":1", maintenance);
            Assert.Contains("\"pmOverdue\":1", maintenance);
            Assert.Contains("\"dvirLast7d\":1", maintenance);
            Assert.Contains("\"defectCategory\":\"Brakes\"", maintenance);
            Assert.DoesNotContain("\"defectCategory\":\"Tires\"", maintenance);
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
    private static Task<long> Vehicle(Database db, long company, string code) => db.InsertAsync(
        @"INSERT INTO vehicles(company_id,vehicle_code,type,status,vin_exception_type,alternate_identifier)
          VALUES(@company,@code,'Truck','Available','legacy-fleet-identifier',@code) RETURNING id",
        c => { c.Parameters.AddWithValue("@company", company); c.Parameters.AddWithValue("@code", code); });
    private static Task<long> Driver(Database db, long company, string code) => db.InsertAsync(
        "INSERT INTO drivers(company_id,driver_code,full_name,email,status) VALUES(@company,@code,@code,@email,'Available') RETURNING id",
        c => { c.Parameters.AddWithValue("@company", company); c.Parameters.AddWithValue("@code", code); c.Parameters.AddWithValue("@email", $"{code.ToLowerInvariant()}@example.test"); });
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

    private static Task<long> SafetyEvent(Database db, long company, long driver, string number, string severity, string origin, string verification) => db.InsertAsync(
        @"INSERT INTO safety_events(company_id,event_number,driver_id,event_type,severity,description,event_time,occurred_at,data_origin,verification_status)
          VALUES(@company,@number,@driver,'Harsh Braking',@severity,'Recorded test event',NOW(),NOW(),@origin,@verification) RETURNING id",
        c => { c.Parameters.AddWithValue("@company", company); c.Parameters.AddWithValue("@number", number); c.Parameters.AddWithValue("@driver", driver); c.Parameters.AddWithValue("@severity", severity); c.Parameters.AddWithValue("@origin", origin); c.Parameters.AddWithValue("@verification", verification); });

    private static Task Coaching(Database db, long company, long driver, long? safetyEvent, string number, string origin, string verification) => db.ExecuteAsync(
        @"INSERT INTO coaching_tasks(company_id,task_number,driver_id,safety_event_id,coaching_type,priority,status,title,description,due_at,data_origin,verification_status)
          VALUES(@company,@number,@driver,@safety,'Safety Review','High','Assigned',@number,'Recorded coaching task',NOW()-INTERVAL '1 day',@origin,@verification)",
        c => { c.Parameters.AddWithValue("@company", company); c.Parameters.AddWithValue("@number", number); c.Parameters.AddWithValue("@driver", driver); c.Parameters.AddWithValue("@safety", (object?)safetyEvent ?? DBNull.Value); c.Parameters.AddWithValue("@origin", origin); c.Parameters.AddWithValue("@verification", verification); });

    private static Task MaintenanceItem(Database db, long company, long vehicle, string title, string origin, string verification) => db.ExecuteAsync(
        @"INSERT INTO maintenance_items(company_id,vehicle_id,title,category,status,risk_level,due_date,data_origin,verification_status)
          VALUES(@company,@vehicle,@title,'Preventive Maintenance','Open','Medium',CURRENT_DATE-1,@origin,@verification)",
        c => { c.Parameters.AddWithValue("@company", company); c.Parameters.AddWithValue("@vehicle", vehicle); c.Parameters.AddWithValue("@title", title); c.Parameters.AddWithValue("@origin", origin); c.Parameters.AddWithValue("@verification", verification); });

    private static Task WorkOrder(Database db, long company, long vehicle, string code, string origin, string verification) => db.ExecuteAsync(
        @"INSERT INTO work_orders(company_id,vehicle_id,work_order_code,title,priority,status,data_origin,verification_status)
          VALUES(@company,@vehicle,@code,@code,'High','Open',@origin,@verification)",
        c => { c.Parameters.AddWithValue("@company", company); c.Parameters.AddWithValue("@vehicle", vehicle); c.Parameters.AddWithValue("@code", code); c.Parameters.AddWithValue("@origin", origin); c.Parameters.AddWithValue("@verification", verification); });

    private static Task<long> Dvir(Database db, long company, long driver, long vehicle, string number, string origin, string verification) => db.InsertAsync(
        @"INSERT INTO dvir_reports(company_id,report_number,driver_id,vehicle_id,inspection_type,inspection_status,submitted_at,data_origin,verification_status)
          VALUES(@company,@number,@driver,@vehicle,'Pre Trip','Submitted',NOW(),@origin,@verification) RETURNING id",
        c => { c.Parameters.AddWithValue("@company", company); c.Parameters.AddWithValue("@number", number); c.Parameters.AddWithValue("@driver", driver); c.Parameters.AddWithValue("@vehicle", vehicle); c.Parameters.AddWithValue("@origin", origin); c.Parameters.AddWithValue("@verification", verification); });

    private static Task DvirDefect(Database db, long company, long vehicle, long report, string category, string origin, string verification) => db.ExecuteAsync(
        @"INSERT INTO dvir_defects(company_id,dvir_report_id,vehicle_id,defect_category,defect_description,severity,status,out_of_service,data_origin,verification_status)
          VALUES(@company,@report,@vehicle,@category,@category,'Critical','Open',TRUE,@origin,@verification)",
        c => { c.Parameters.AddWithValue("@company", company); c.Parameters.AddWithValue("@report", report); c.Parameters.AddWithValue("@vehicle", vehicle); c.Parameters.AddWithValue("@category", category); c.Parameters.AddWithValue("@origin", origin); c.Parameters.AddWithValue("@verification", verification); });

    private static async Task Cleanup(Database db, long company, long other)
    {
        await db.ExecuteAsync(@"DELETE FROM sla_breaches WHERE tenant_id=@company OR tenant_id=@other;
            DELETE FROM sla_records WHERE company_id=@company OR company_id=@other;
            DELETE FROM executive_snapshots WHERE tenant_id=@company OR tenant_id=@other;
            DELETE FROM dispatch_proof_artifacts WHERE company_id=@company OR company_id=@other;
            DELETE FROM dispatch_proofs WHERE company_id=@company OR company_id=@other;
            DELETE FROM dispatch_exceptions WHERE company_id=@company OR company_id=@other;
            DELETE FROM dispatch_assignments WHERE company_id=@company OR company_id=@other;
            DELETE FROM coaching_tasks WHERE company_id=@company OR company_id=@other;
            DELETE FROM safety_events WHERE company_id=@company OR company_id=@other;
            DELETE FROM work_orders WHERE company_id=@company OR company_id=@other;
            DELETE FROM maintenance_items WHERE company_id=@company OR company_id=@other;
            DELETE FROM dvir_defects WHERE company_id=@company OR company_id=@other;
            DELETE FROM dvir_reports WHERE company_id=@company OR company_id=@other;
            DELETE FROM drivers WHERE company_id=@company OR company_id=@other;
            DELETE FROM vehicles WHERE company_id=@company OR company_id=@other;
            DELETE FROM customers WHERE company_id=@company OR company_id=@other;
            DELETE FROM companies WHERE id=@company OR id=@other;",
            c => { c.Parameters.AddWithValue("@company", company); c.Parameters.AddWithValue("@other", other); });
    }
}
