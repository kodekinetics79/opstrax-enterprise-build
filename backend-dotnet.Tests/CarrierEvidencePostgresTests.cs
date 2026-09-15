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
public sealed class CarrierEvidencePostgresTests
{
    [Fact]
    public async Task CarrierWorkflow_HidesDemo_QualifiesEvidence_AndRejectsCallerCertificationClaims()
    {
        var db = Db();
        await new Batch5SchemaService(db).EnsureAsync();
        await db.ExecuteAsync(File.ReadAllText(Path.Combine(RepoRoot, "database", "migrations", "2026_09_08_carrier_evidence_integrity.sql")));
        var suffix = Guid.NewGuid().ToString("N")[..10];
        var company = await Company(db, $"CAR-A-{suffix}");
        var other = await Company(db, $"CAR-B-{suffix}");
        try
        {
            var demo = await Carrier(db, company, $"CAR-B5-{suffix}", "Generated carrier", "demo_seed", "demo_seed", "Compliant", 99);
            var legacy = await Carrier(db, company, $"LEG-{suffix}", "Legacy recorded carrier", "legacy_unverified", "unverified", "Compliant", 98);
            var verified = await Carrier(db, company, $"VER-{suffix}", "Verified carrier", "provider_import", "authority_verified", "Compliant", 94);
            var foreign = await Carrier(db, other, $"FOR-{suffix}", "Foreign carrier", "manual_entry", "unverified", "Compliant", 91);

            await Document(db, company, demo, $"DEMO-DOC-{suffix}", "demo_seed", "demo_seed");
            await Document(db, company, legacy, $"LEG-DOC-{suffix}", "legacy_unverified", "unverified");
            await Document(db, company, verified, $"VER-DOC-{suffix}", "provider_import", "authority_verified");
            await Document(db, other, legacy, $"FORGED-DOC-{suffix}", "provider_import", "authority_verified");

            await Performance(db, company, demo, "demo_seed", "demo_seed", 99);
            await Performance(db, company, legacy, "legacy_unverified", "unverified", 98);
            await Performance(db, company, verified, "job_derived", "calculated_from_jobs", 93);
            await Performance(db, other, legacy, "provider_import", "provider_reported", 97);

            var http = Principal(company);
            var audit = new AuditService(db);
            var rows = Data(await Invoke("Carriers", http, db, CancellationToken.None))
                .Cast<Dictionary<string, object?>>().ToList();
            Assert.Equal(2, rows.Count);
            Assert.DoesNotContain(rows, row => Convert.ToInt64(row["id"]) == demo);
            var legacyRow = Assert.Single(rows, row => Convert.ToInt64(row["id"]) == legacy);
            Assert.Equal("Unverified", legacyRow["complianceStatus"]);
            Assert.Equal("Legacy origin unverified", legacyRow["recordOrigin"]);
            Assert.False(legacyRow.ContainsKey("performanceScore"));
            Assert.False(legacyRow.ContainsKey("riskScore"));

            var verifiedRow = Assert.Single(rows, row => Convert.ToInt64(row["id"]) == verified);
            Assert.Equal("Compliant", verifiedRow["complianceStatus"]);
            Assert.Equal(1L, Convert.ToInt64(verifiedRow["performanceEvidenceCount"]));

            var summaryJson = JsonSerializer.Serialize(Value(await Invoke("CarriersSummary", http, db, CancellationToken.None)));
            Assert.Contains("\"verifiedComplianceCarriers\":1", summaryJson);
            Assert.Contains("\"documentsRecorded\":2", summaryJson);
            Assert.Contains("\"verifiedDocuments\":1", summaryJson);
            Assert.Contains("\"documentsNeedingVerification\":1", summaryJson);
            Assert.Contains("\"performanceEvidenceRecords\":1", summaryJson);
            Assert.DoesNotContain("averageCarrierScore", summaryJson);
            Assert.DoesNotContain("carrierCostThisMonth", summaryJson);

            var legacyDetail = JsonSerializer.Serialize(Value(await Invoke("CarrierDetail", http, legacy, db, CancellationToken.None)));
            Assert.Contains($"LEG-DOC-{suffix}", legacyDetail);
            Assert.DoesNotContain($"FORGED-DOC-{suffix}", legacyDetail);
            Assert.DoesNotContain("\"performanceScore\":98", legacyDetail);

            var performance = Data(await Invoke("CarrierPerformance", http, verified, db, CancellationToken.None))
                .Cast<Dictionary<string, object?>>().ToList();
            Assert.Single(performance);
            Assert.Equal("Calculated from recorded jobs", performance[0]["recordOrigin"]);

            var create = new Dictionary<string, object?>
            {
                ["carrierNumber"] = $"NEW-{suffix}",
                ["name"] = "Customer supplied carrier",
                ["mcNumber"] = $"MC-{suffix}",
                ["status"] = "Active",
                ["complianceStatus"] = "Compliant",
                ["performanceScore"] = 100,
                ["riskScore"] = 0,
                ["insuranceExpiry"] = DateTime.UtcNow.Date.AddYears(1).ToString("yyyy-MM-dd")
            };
            Assert.Equal(StatusCodes.Status201Created,
                Status(await Invoke("CreateCarrier", http, create, db, audit, CancellationToken.None)));
            var created = await db.QuerySingleAsync(
                "SELECT * FROM carriers WHERE company_id=@company AND carrier_number=@number",
                c => { c.Parameters.AddWithValue("@company", company); c.Parameters.AddWithValue("@number", $"NEW-{suffix}"); });
            Assert.Equal("manual_entry", created!["dataOrigin"]);
            Assert.Equal("unverified", created["complianceEvidenceStatus"]);
            Assert.Equal("Unverified", created["complianceStatus"]);
            Assert.Equal(0m, Convert.ToDecimal(created["performanceScore"]));
            Assert.Equal(0m, Convert.ToDecimal(created["riskScore"]));

            var createdId = Convert.ToInt64(created["id"]);
            var forgedUpdate = new Dictionary<string, object?>
            {
                ["name"] = "Updated carrier",
                ["complianceStatus"] = "Compliant",
                ["performanceScore"] = 100
            };
            Assert.Equal(StatusCodes.Status200OK,
                Status(await Invoke("UpdateCarrier", http, createdId, forgedUpdate, db, audit, CancellationToken.None)));
            var afterUpdate = await db.QuerySingleAsync(
                "SELECT name,compliance_status,compliance_evidence_status,performance_score FROM carriers WHERE id=@id",
                c => c.Parameters.AddWithValue("@id", createdId));
            Assert.Equal("Updated carrier", afterUpdate!["name"]);
            Assert.Equal("Unverified", afterUpdate["complianceStatus"]);
            Assert.Equal("unverified", afterUpdate["complianceEvidenceStatus"]);
            Assert.Equal(0m, Convert.ToDecimal(afterUpdate["performanceScore"]));

            Assert.Equal(StatusCodes.Status400BadRequest,
                Status(await Invoke("CarrierStatus", http, createdId, new Dictionary<string, object?> { ["status"] = "Certified" }, db, audit, CancellationToken.None)));
            Assert.Equal(StatusCodes.Status404NotFound,
                Status(await Invoke("CarrierStatus", http, foreign, new Dictionary<string, object?> { ["status"] = "Suspended" }, db, audit, CancellationToken.None)));
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

    private static DefaultHttpContext Principal(long company)
    {
        var http = new DefaultHttpContext { TraceIdentifier = $"carrier-evidence-{Guid.NewGuid():N}" };
        http.Items[EndpointMappings.AuthUserIdItemKey] = 92L;
        http.Items[EndpointMappings.AuthCompanyIdItemKey] = company;
        http.Items[EndpointMappings.AuthRoleItemKey] = "Finance Manager";
        http.Items[EndpointMappings.AuthPermissionsItemKey] = new[] { "fleet:view", "finance:view", "finance:manage" };
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

    private static Task<long> Carrier(Database db, long company, string number, string name, string origin, string evidence, string compliance, decimal score) => db.InsertAsync(
        @"INSERT INTO carriers(company_id,carrier_number,name,status,compliance_status,performance_score,risk_score,data_origin,compliance_evidence_status)
          VALUES(@company,@number,@name,'Active',@compliance,@score,@score,@origin,@evidence) RETURNING id",
        c => { c.Parameters.AddWithValue("@company", company); c.Parameters.AddWithValue("@number", number); c.Parameters.AddWithValue("@name", name); c.Parameters.AddWithValue("@compliance", compliance); c.Parameters.AddWithValue("@score", score); c.Parameters.AddWithValue("@origin", origin); c.Parameters.AddWithValue("@evidence", evidence); });

    private static Task Document(Database db, long company, long carrier, string number, string origin, string verification) => db.ExecuteAsync(
        @"INSERT INTO carrier_documents(company_id,carrier_id,document_type,document_number,status,data_origin,verification_status)
          VALUES(@company,@carrier,'Insurance Certificate',@number,'Active',@origin,@verification)",
        c => { c.Parameters.AddWithValue("@company", company); c.Parameters.AddWithValue("@carrier", carrier); c.Parameters.AddWithValue("@number", number); c.Parameters.AddWithValue("@origin", origin); c.Parameters.AddWithValue("@verification", verification); });

    private static Task Performance(Database db, long company, long carrier, string origin, string calculation, decimal score) => db.ExecuteAsync(
        @"INSERT INTO carrier_performance(company_id,carrier_id,period_start,period_end,jobs_handled,on_time_percent,performance_score,data_origin,calculation_status)
          VALUES(@company,@carrier,CURRENT_DATE-30,CURRENT_DATE,12,@score,@score,@origin,@calculation)",
        c => { c.Parameters.AddWithValue("@company", company); c.Parameters.AddWithValue("@carrier", carrier); c.Parameters.AddWithValue("@score", score); c.Parameters.AddWithValue("@origin", origin); c.Parameters.AddWithValue("@calculation", calculation); });

    private static async Task Cleanup(Database db, long company, long other)
    {
        foreach (var table in new[] { "audit_logs", "carrier_performance", "carrier_documents", "contracts", "expenses", "carriers" })
            await db.ExecuteAsync($"DELETE FROM {table} WHERE company_id=@company OR company_id=@other",
                c => { c.Parameters.AddWithValue("@company", company); c.Parameters.AddWithValue("@other", other); });
        await db.ExecuteAsync("DELETE FROM companies WHERE id=@company OR id=@other",
            c => { c.Parameters.AddWithValue("@company", company); c.Parameters.AddWithValue("@other", other); });
    }
}
