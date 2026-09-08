using System.Collections;
using System.Reflection;
using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Npgsql;
using Opstrax.Api.Controllers;
using Opstrax.Api.Data;
using Opstrax.Api.Services;

namespace Opstrax.Tests;

[Trait("Category", "Integration")]
public sealed class CostLeakageEvidencePostgresTests
{
    [Fact]
    public async Task OperationalQueue_HidesDemoAndForeignRows_SeparatesCurrency_AndGuardsActions()
    {
        var db = Db();
        await new Batch5SchemaService(db).EnsureAsync();
        await db.ExecuteAsync(File.ReadAllText(Path.Combine(RepoRoot, "database", "migrations", "2026_09_08_cost_leakage_evidence.sql")));
        var suffix = Guid.NewGuid().ToString("N")[..10];
        var company = await Company(db, $"CL-A-{suffix}");
        var otherCompany = await Company(db, $"CL-B-{suffix}");
        try
        {
            var usd = await Leakage(db, company, $"RLK-runtime-usd-{suffix}", "USD", 100m, "Open", "runtime_detector");
            var cad = await Leakage(db, company, $"RLK-runtime-cad-{suffix}", "CAD", 50m, "Open", "runtime_detector");
            _ = await Leakage(db, company, $"LEAK-{suffix}", "USD", 999_999m, "Open", null);
            var foreign = await Leakage(db, otherCompany, $"RLK-runtime-x-{suffix}", "USD", 888_888m, "Open", "runtime_detector");
            var legacyRuntime = await Leakage(db, company, $"RLK-runtime-legacy-{suffix}", "USD", 10m, "Open", null);
            await new Batch5SchemaService(db).EnsureAsync();
            var legacyEvidence = await db.QuerySingleAsync(
                "SELECT data_origin, amount_evidence_status FROM cost_leakage_items WHERE id=@id",
                c => c.Parameters.AddWithValue("@id", legacyRuntime));
            Assert.Equal("runtime_detector", legacyEvidence?["dataOrigin"]?.ToString());
            Assert.Equal("Recorded", legacyEvidence?["amountEvidenceStatus"]?.ToString());
            await db.ExecuteAsync("DELETE FROM cost_leakage_items WHERE id=@id", c => c.Parameters.AddWithValue("@id", legacyRuntime));
            var http = Principal(company);
            var audit = new AuditService(db);

            var list = await Invoke("CostLeakageItems", http, db, CancellationToken.None);
            Assert.Equal(StatusCodes.Status200OK, Status(list));
            var rows = Data(list).Cast<Dictionary<string, object?>>().ToList();
            Assert.Equal(2, rows.Count);
            Assert.Contains(rows, row => Convert.ToInt64(row["id"]) == usd && row["recordOrigin"]?.ToString() == "Runtime detector");
            Assert.Contains(rows, row => Convert.ToInt64(row["id"]) == cad);
            Assert.DoesNotContain(rows, row => Convert.ToInt64(row["id"]) == foreign);

            var summary = await Invoke("CostLeakageSummary", http, db, CancellationToken.None);
            var summaryJson = JsonSerializer.Serialize(Value(summary));
            Assert.Contains("\"openItems\":2", summaryJson);
            Assert.Contains("\"currency\":\"CAD\"", summaryJson);
            Assert.Contains("\"currency\":\"USD\"", summaryJson);
            Assert.DoesNotContain("999999", summaryJson);
            Assert.DoesNotContain("888888", summaryJson);

            var invalid = new Dictionary<string, object?> { ["actionTitle"] = "", ["estimatedSavings"] = 500m };
            Assert.Equal(StatusCodes.Status400BadRequest,
                Status(await Invoke("CostLeakageCreateAction", http, usd, invalid, db, audit, CancellationToken.None)));
            Assert.Equal("Open", await ScalarString(db, "SELECT status FROM cost_leakage_items WHERE id=@id", usd));

            var valid = new Dictionary<string, object?>
            {
                ["actionTitle"] = "Recover documented shortfall",
                ["actionDescription"] = "Finance reviewed the job and recorded the expected recovery.",
                ["estimatedSavings"] = 125.50m,
                ["dueAt"] = DateTimeOffset.UtcNow.AddDays(2).ToString("O")
            };
            Assert.Equal(StatusCodes.Status201Created,
                Status(await Invoke("CostLeakageCreateAction", http, usd, valid, db, audit, CancellationToken.None)));
            Assert.Equal("In Progress", await ScalarString(db, "SELECT status FROM cost_leakage_items WHERE id=@id", usd));
            Assert.Equal(125.50m, await ScalarDecimal(db,
                "SELECT estimated_savings FROM cost_leakage_actions WHERE company_id=@company AND cost_leakage_item_id=@id",
                company, usd));

            Assert.Equal(StatusCodes.Status404NotFound,
                Status(await Invoke("CostLeakageCreateAction", http, foreign, valid, db, audit, CancellationToken.None)));
            Assert.Equal(StatusCodes.Status200OK,
                Status(await Invoke("CostLeakageAcknowledge", http, cad, new Dictionary<string, object?>(), db, audit, CancellationToken.None)));
            Assert.Equal(StatusCodes.Status409Conflict,
                Status(await Invoke("CostLeakageAcknowledge", http, cad, new Dictionary<string, object?>(), db, audit, CancellationToken.None)));

            var constraint = await Assert.ThrowsAsync<PostgresException>(() => db.ExecuteAsync(
                @"INSERT INTO cost_leakage_actions(company_id,cost_leakage_item_id,action_title,estimated_savings,status)
                  VALUES(@company,@item,'Invalid negative evidence',-1,'Open')",
                c => { c.Parameters.AddWithValue("@company", company); c.Parameters.AddWithValue("@item", usd); }));
            Assert.Equal("23514", constraint.SqlState);
        }
        finally
        {
            await Cleanup(db, company, otherCompany);
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
        var http = new DefaultHttpContext { TraceIdentifier = $"cost-leakage-{Guid.NewGuid():N}" };
        http.Items[EndpointMappings.AuthUserIdItemKey] = 91L;
        http.Items[EndpointMappings.AuthCompanyIdItemKey] = company;
        http.Items[EndpointMappings.AuthRoleItemKey] = "Finance Manager";
        http.Items[EndpointMappings.AuthPermissionsItemKey] = new[] { "finance:view", "finance:manage", "finance.revenue.summary.read" };
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

    private static Task<long> Leakage(Database db, long company, string number, string currency, decimal amount, string status, string? origin) => db.InsertAsync(
        @"INSERT INTO cost_leakage_items
            (company_id,leakage_number,category,entity_type,entity_id,title,estimated_loss,currency,data_origin,amount_evidence_status,severity,status)
          VALUES(@company,@number,'stale_draft_charge','charge',1,'Recorded leakage',@amount,@currency,@origin,'Recorded','High',@status)
          RETURNING id",
        c => { c.Parameters.AddWithValue("@company", company); c.Parameters.AddWithValue("@number", number); c.Parameters.AddWithValue("@amount", amount); c.Parameters.AddWithValue("@currency", currency); c.Parameters.AddWithValue("@origin", (object?)origin ?? DBNull.Value); c.Parameters.AddWithValue("@status", status); });

    private static async Task<string> ScalarString(Database db, string sql, long id)
    {
        var row = await db.QuerySingleAsync(sql, c => c.Parameters.AddWithValue("@id", id));
        return row?.Values.Single()?.ToString() ?? string.Empty;
    }

    private static async Task<decimal> ScalarDecimal(Database db, string sql, long company, long id)
    {
        var row = await db.QuerySingleAsync(sql, c => { c.Parameters.AddWithValue("@company", company); c.Parameters.AddWithValue("@id", id); });
        return Convert.ToDecimal(row?.Values.Single() ?? 0);
    }

    private static async Task Cleanup(Database db, long company, long otherCompany)
    {
        await db.ExecuteAsync(
            @"DELETE FROM cost_leakage_actions
               WHERE company_id=@company OR (@other > 0 AND company_id=@other)",
            c => { c.Parameters.AddWithValue("@company", company); c.Parameters.AddWithValue("@other", otherCompany); });
        foreach (var table in new[] { "audit_logs", "cost_leakage_items" })
            await db.ExecuteAsync($"DELETE FROM {table} WHERE company_id=@company OR (@other > 0 AND company_id=@other)", c => { c.Parameters.AddWithValue("@company", company); c.Parameters.AddWithValue("@other", otherCompany); });
        await db.ExecuteAsync("DELETE FROM companies WHERE id=@company OR (@other > 0 AND id=@other)", c => { c.Parameters.AddWithValue("@company", company); c.Parameters.AddWithValue("@other", otherCompany); });
    }
}
