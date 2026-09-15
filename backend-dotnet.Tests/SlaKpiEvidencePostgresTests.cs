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
public sealed class SlaKpiEvidencePostgresTests
{
    [Fact]
    public async Task SlaKpiWorkflow_HidesUnverifiedRows_QualifiesEvidence_AndScopesBreachUpdates()
    {
        var db = Db();
        await new Batch7SchemaService(db).EnsureAsync();
        await db.ExecuteAsync(File.ReadAllText(Path.Combine(RepoRoot, "database", "migrations", "2026_09_08_sla_kpi_evidence_integrity.sql")));
        var suffix = Guid.NewGuid().ToString("N")[..10];
        var company = await Company(db, $"SLA-A-{suffix}");
        var other = await Company(db, $"SLA-B-{suffix}");
        try
        {
            var customer = await Customer(db, company, $"CUS-A-{suffix}");
            var foreignCustomer = await Customer(db, other, $"CUS-B-{suffix}");
            await Vehicle(db, company, $"VEH-{suffix}");

            await Target(db, company, $"VER-{suffix}", "manual_entry", "verified");
            await Target(db, company, $"LEG-{suffix}", "legacy_unverified", "unverified");
            await Target(db, company, $"DEMO-{suffix}", "demo_seed", "demo_seed");
            await Target(db, other, $"FOR-{suffix}", "provider_import", "verified");

            var verified = await Sla(db, company, customer, $"SLA-VER-{suffix}", "job_derived", "calculated_from_events", "Breached");
            var legacy = await Sla(db, company, customer, $"SLA-LEG-{suffix}", "legacy_unverified", "unverified", "Met");
            var demo = await Sla(db, company, customer, $"SLA-DEMO-{suffix}", "demo_seed", "demo_seed", "Met");
            await Sla(db, company, foreignCustomer, $"SLA-FORGED-{suffix}", "manual_entry", "manual_verified", "Met");
            var foreign = await Sla(db, other, foreignCustomer, $"SLA-FOR-{suffix}", "provider_import", "provider_verified", "Met");

            var verifiedBreach = await Breach(db, company, verified, "derived_from_sla", "Open");
            await Breach(db, company, legacy, "legacy_unverified", "Open");
            await Breach(db, company, demo, "demo_seed", "Open");
            var foreignBreach = await Breach(db, other, foreign, "provider_import", "Open");

            var http = Principal(company, "reports:view", "reports:manage");
            var targetRows = Data(await Invoke("KpiTargets", http, db, CancellationToken.None))
                .Cast<Dictionary<string, object?>>().ToList();
            Assert.Single(targetRows);
            Assert.Equal($"VER-{suffix}", targetRows[0]["kpiCode"]);

            var kpiJson = JsonSerializer.Serialize(Value(await Invoke("KpiMetricsComputed", http, db, CancellationToken.None)));
            Assert.Contains("Recorded vehicles", kpiJson);
            Assert.Contains("\"actual_value\":1", kpiJson);
            Assert.Contains("\"target_value\":null", kpiJson);
            Assert.DoesNotContain("On-Time Delivery Rate", kpiJson);

            var slaRows = Data(await Invoke("SlaRecords", http, db, CancellationToken.None))
                .Cast<Dictionary<string, object?>>().ToList();
            var verifiedRow = Assert.Single(slaRows);
            Assert.Equal(verified, Convert.ToInt64(verifiedRow["id"]));
            Assert.Equal("calculated_from_events", verifiedRow["measurementEvidenceStatus"]);

            var summaryJson = JsonSerializer.Serialize(Value(await Invoke("SlaSummary", http, db, CancellationToken.None)));
            Assert.Contains("\"total\":1", summaryJson);
            Assert.Contains("\"breached\":1", summaryJson);
            Assert.Contains($"SLA-VER-{suffix}", summaryJson);
            Assert.DoesNotContain($"SLA-LEG-{suffix}", summaryJson);

            var breaches = Data(await Invoke("SlaBreaches", http, db, CancellationToken.None))
                .Cast<Dictionary<string, object?>>().ToList();
            Assert.Single(breaches);
            Assert.Equal(verifiedBreach, Convert.ToInt64(breaches[0]["id"]));

            var audit = new AuditService(db);
            Assert.Equal(StatusCodes.Status200OK, Status(await Invoke(
                "SlaBreachStatus", http, verifiedBreach, "Acknowledged", "sla.breach_acknowledged", db, audit, CancellationToken.None)));
            Assert.Equal(StatusCodes.Status404NotFound, Status(await Invoke(
                "SlaBreachStatus", http, foreignBreach, "Acknowledged", "sla.breach_acknowledged", db, audit, CancellationToken.None)));

            var noPermission = Principal(company);
            Assert.Equal(StatusCodes.Status403Forbidden,
                Status(await Invoke("KpiMetricsComputed", noPermission, db, CancellationToken.None)));
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
        var http = new DefaultHttpContext { TraceIdentifier = $"sla-kpi-evidence-{Guid.NewGuid():N}" };
        http.Items[EndpointMappings.AuthUserIdItemKey] = 94L;
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

    private static int? Status(IResult result) => Assert.IsAssignableFrom<IStatusCodeHttpResult>(result).StatusCode;
    private static object Value(IResult result) => Assert.IsAssignableFrom<IValueHttpResult>(result).Value!
        .GetType().GetProperty("Data")!.GetValue(Assert.IsAssignableFrom<IValueHttpResult>(result).Value!)!;
    private static IEnumerable Data(IResult result) => Assert.IsAssignableFrom<IEnumerable>(Value(result));

    private static Task<long> Company(Database db, string code) => db.InsertAsync(
        "INSERT INTO companies(company_code,name,industry) VALUES(@code,@code,'Logistics') RETURNING id",
        c => c.Parameters.AddWithValue("@code", code));

    private static Task<long> Customer(Database db, long company, string code) => db.InsertAsync(
        @"INSERT INTO customers(company_id,customer_code,name,status,sla_tier)
          VALUES(@company,@code,@code,'Active','Standard') RETURNING id",
        c => { c.Parameters.AddWithValue("@company", company); c.Parameters.AddWithValue("@code", code); });

    private static Task Vehicle(Database db, long company, string code) => db.ExecuteAsync(
        @"INSERT INTO vehicles(company_id,vehicle_code,type,status,vin_exception_type,alternate_identifier)
          VALUES(@company,@code,'Truck','Available','legacy-fleet-identifier',@code)",
        c => { c.Parameters.AddWithValue("@company", company); c.Parameters.AddWithValue("@code", code); });

    private static Task Target(Database db, long company, string code, string origin, string verification) => db.ExecuteAsync(
        @"INSERT INTO kpi_targets(tenant_id,kpi_code,target_value,unit,effective_date,status,data_origin,verification_status)
          VALUES(@company,@code,10,'count',CURRENT_DATE,'Active',@origin,@verification)",
        c => { c.Parameters.AddWithValue("@company", company); c.Parameters.AddWithValue("@code", code); c.Parameters.AddWithValue("@origin", origin); c.Parameters.AddWithValue("@verification", verification); });

    private static Task<long> Sla(Database db, long company, long customer, string number, string origin, string evidence, string status) => db.InsertAsync(
        @"INSERT INTO sla_records(company_id,tenant_id,sla_number,customer_id,sla_type,metric_name,target_value,actual_value,unit,status,data_origin,measurement_evidence_status,measured_at)
          VALUES(@company,@company,@number,@customer,'On-Time Delivery',@number,100,90,'%',@status,@origin,@evidence,NOW()) RETURNING id",
        c => { c.Parameters.AddWithValue("@company", company); c.Parameters.AddWithValue("@number", number); c.Parameters.AddWithValue("@customer", customer); c.Parameters.AddWithValue("@status", status); c.Parameters.AddWithValue("@origin", origin); c.Parameters.AddWithValue("@evidence", evidence); });

    private static Task<long> Breach(Database db, long company, long sla, string origin, string status) => db.InsertAsync(
        @"INSERT INTO sla_breaches(tenant_id,sla_record_id,breach_type,severity,description,status,data_origin)
          VALUES(@company,@sla,'Delivery Delay','High','Recorded breach',@status,@origin) RETURNING id",
        c => { c.Parameters.AddWithValue("@company", company); c.Parameters.AddWithValue("@sla", sla); c.Parameters.AddWithValue("@status", status); c.Parameters.AddWithValue("@origin", origin); });

    private static async Task Cleanup(Database db, long company, long other)
    {
        await db.ExecuteAsync(@"DELETE FROM audit_logs WHERE company_id=@company OR company_id=@other;
            DELETE FROM sla_breaches WHERE tenant_id=@company OR tenant_id=@other;
            DELETE FROM sla_records WHERE company_id=@company OR company_id=@other;
            DELETE FROM kpi_targets WHERE tenant_id=@company OR tenant_id=@other;
            DELETE FROM vehicles WHERE company_id=@company OR company_id=@other;
            DELETE FROM customers WHERE company_id=@company OR company_id=@other;
            DELETE FROM companies WHERE id=@company OR id=@other;",
            c => { c.Parameters.AddWithValue("@company", company); c.Parameters.AddWithValue("@other", other); });
    }
}
