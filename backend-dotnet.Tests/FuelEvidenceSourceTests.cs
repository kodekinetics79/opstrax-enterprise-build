namespace Opstrax.Tests;

public sealed class FuelEvidenceSourceTests
{
    private static string RepoRoot => Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../"));

    [Fact]
    public void FuelHandlers_DoNotExposeDemoRowsOrFabricatedMetrics()
    {
        var source = File.ReadAllText(Path.Combine(RepoRoot, "backend-dotnet", "Controllers", "EndpointMappings.cs"));
        var start = source.IndexOf("// BATCH 5 HANDLERS — FUEL & IDLING", StringComparison.Ordinal);
        var end = source.IndexOf("// BATCH 5 HANDLERS — EXPENSES", start, StringComparison.Ordinal);
        var handlers = source[start..end];

        Assert.Contains("data_origin,'legacy_unverified') <> 'demo_seed'", handlers);
        Assert.Contains("fuelByCurrencyAndUnit", handlers);
        Assert.Contains("idlingByCurrency", handlers);
        Assert.Contains("Fuel-card import is not configured", handlers);
        Assert.Contains("'manual_entry'", handlers);
        Assert.Contains("'Not Evaluated'", handlers);
        Assert.DoesNotContain("average_mpg_placeholder", handlers);
        Assert.DoesNotContain("estimated_savings_opportunity", handlers);
        Assert.DoesNotContain("'Integration Ready' fuel_card_import_status", handlers);
        Assert.DoesNotContain("detectedRows = 28", handlers);
        Assert.DoesNotContain("CONCAT('$'", handlers);
    }

    [Fact]
    public void FuelUi_LabelsRecordedEvidenceAndKeepsCurrencyAndUnitSeparate()
    {
        var source = File.ReadAllText(Path.Combine(RepoRoot, "frontend", "src", "pages", "Batch5FinancePage.tsx"));
        var start = source.IndexOf("fuel: {", StringComparison.Ordinal);
        var end = source.IndexOf("expenses: {", start, StringComparison.Ordinal);
        var config = source[start..end];

        Assert.Contains("Recorded fuel and idling evidence", config);
        Assert.Contains("Legacy Fuel Origin Unverified", config);
        Assert.DoesNotContain("Savings Opportunity", config);
        Assert.DoesNotContain("Spend Today", config);
        Assert.Contains("Currencies and measurement units remain separate", source);
        Assert.Contains("Fuel-card provider import: not configured", source);
        Assert.DoesNotContain("Anomaly detection active", source);
    }
}
