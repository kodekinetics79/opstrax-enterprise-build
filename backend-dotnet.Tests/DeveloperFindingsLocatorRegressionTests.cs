using System.Text.Json;
using Opstrax.Api.Controllers;

namespace Opstrax.Tests;

public sealed class DeveloperFindingsLocatorRegressionTests
{
    [Fact]
    public void BillingInputParsersRejectMalformedValuesWithoutThrowing()
    {
        var body = new Dictionary<string, object?>
        {
            ["jobId"] = JsonSerializer.Deserialize<JsonElement>("\"not-a-job\""),
            ["amount"] = JsonSerializer.Deserialize<JsonElement>("\"twelve dollars\""),
        };

        Assert.False(BusinessSpineEndpoints.TryLong(body, "jobId", out _));
        Assert.False(BusinessSpineEndpoints.TryDecimal(body, "amount", out _));
    }

    [Fact]
    public void CityJsonFallbackIsLimitedToMalformedJson()
    {
        Assert.Empty(FleetTmsColdChainEndpoints.ParseCities("{not-json"));
        Assert.Equal(new[] { "Riyadh", "Jeddah" },
            FleetTmsColdChainEndpoints.ParseCities("[\"Riyadh\",\"Jeddah\"]"));

        var source = Source("backend-dotnet", "Controllers", "FleetTmsColdChainEndpoints.cs");
        var start = source.IndexOf("IReadOnlyCollection<string> ParseCities", StringComparison.Ordinal);
        var end = source.IndexOf("\n    }", start, StringComparison.Ordinal);
        var method = source[start..(end + 6)];
        Assert.Contains("catch (JsonException)", method, StringComparison.Ordinal);
        Assert.DoesNotContain("catch {", method, StringComparison.Ordinal);
    }

    [Fact]
    public void OperationalBackgroundWorkersAwaitFoundationPersistence()
    {
        foreach (var path in new[]
        {
            new[] { "backend-dotnet", "Services", "TelemetryBackgroundService.cs" },
            new[] { "backend-dotnet", "Services", "SafetyBackgroundService.cs" },
            new[] { "backend-dotnet", "Services", "MaintenanceBackgroundService.cs" },
            new[] { "backend-dotnet", "Services", "SafetyMaintenanceFoundationService.cs" },
            new[] { "backend-dotnet", "Services", "AgenticOpsBackgroundService.cs" },
        })
        {
            var source = Source(path);
            Assert.DoesNotContain(".CreateRecommendation(", source, StringComparison.Ordinal);
        }

        var agentic = Source("backend-dotnet", "Services", "AgenticOpsBackgroundService.cs");
        Assert.DoesNotContain(".StartReasoningRun(", agentic, StringComparison.Ordinal);
        Assert.DoesNotContain(".CompleteReasoningRun(", agentic, StringComparison.Ordinal);
        Assert.DoesNotContain(".FailReasoningRun(", agentic, StringComparison.Ordinal);

        var dispatcher = Source("backend-dotnet", "Foundation", "FoundationDispatcherServices.cs");
        Assert.DoesNotContain("eventLogs.Record(", dispatcher, StringComparison.Ordinal);
        Assert.Contains("await eventLogs.RecordAsync(", dispatcher, StringComparison.Ordinal);
    }

    [Fact]
    public void SharedEntityListOnlyConfiguresItsTwoLiveConsumers()
    {
        var source = Source("frontend", "src", "pages", "EntityListPage.tsx");
        var start = source.IndexOf("const config:", StringComparison.Ordinal);
        var end = source.IndexOf("export function EntityListPage", start, StringComparison.Ordinal);
        var configuration = source[start..end];

        Assert.Contains("drivers: {", configuration, StringComparison.Ordinal);
        Assert.Contains("assets: {", configuration, StringComparison.Ordinal);
        Assert.DoesNotContain("vehicles: {", configuration, StringComparison.Ordinal);
        Assert.DoesNotContain("customers: {", configuration, StringComparison.Ordinal);
        Assert.DoesNotContain("jobs: {", configuration, StringComparison.Ordinal);
        Assert.Contains("type EntityKind = \"drivers\" | \"assets\"", source, StringComparison.Ordinal);
    }

    [Fact]
    public void TenantFilterIndexesAreMigrationOwnedForOperationalExpenseAndDocumentReads()
    {
        var migration = Source("database", "migrations", "2026_09_15_stage146_tenant_query_indexes.sql");
        foreach (var expected in new[]
        {
            "expenses(company_id, approval_status, expense_date DESC)",
            "documents(company_id, status, expires_at)",
            "vehicle_documents(company_id, vehicle_id, expiry_date)",
            "driver_documents(company_id, driver_id, expiry_date)",
        })
            Assert.Contains(expected, migration, StringComparison.Ordinal);

        var runner = Source("tools", "apply-neon-predeploy-migrations.sh");
        Assert.Contains("2026_09_15_stage146_tenant_query_indexes", runner, StringComparison.Ordinal);
    }

    private static string Source(params string[] path)
        => File.ReadAllText(Path.Combine([RepoRoot(), .. path]));

    private static string RepoRoot()
        => Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../"));
}
