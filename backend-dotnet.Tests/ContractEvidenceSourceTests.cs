namespace Opstrax.Tests;

public sealed class ContractEvidenceSourceTests
{
    private static string RepoRoot => Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../"));

    [Fact]
    public void ContractSurfaces_ExcludeGeneratedRows_AndDoNotInventCommercialMetrics()
    {
        var handlers = File.ReadAllText(Path.Combine(RepoRoot, "backend-dotnet", "Controllers", "EndpointMappings.cs"));
        var start = handlers.IndexOf("// BATCH 5 HANDLERS — CONTRACTS / RATES", StringComparison.Ordinal);
        var end = handlers.IndexOf("// BATCH 5 HANDLERS — CARRIERS", start, StringComparison.Ordinal);
        handlers = handlers[start..end];

        Assert.Contains("data_origin,'legacy_unverified') <> 'demo_seed'", handlers);
        Assert.Contains("recordedRatesByCurrencyAndType", handlers);
        Assert.Contains("'manual_entry'", handlers);
        Assert.Contains("c.company_id=con.company_id", handlers);
        Assert.Contains("car.company_id=con.company_id", handlers);
        Assert.DoesNotContain("base_rate * 1200", handlers);
        Assert.DoesNotContain("base_rate < 2.20", handlers);
        Assert.DoesNotContain("CONCAT('$'", handlers);
        Assert.DoesNotContain("Renegotiate underpriced rate", handlers);
        Assert.DoesNotContain("con.*", handlers);
    }

    [Fact]
    public void ContractUi_LabelsOriginAndDoesNotPresentUnverifiedMarginRisk()
    {
        var batch = File.ReadAllText(Path.Combine(RepoRoot, "frontend", "src", "pages", "Batch5FinancePage.tsx"));
        var start = batch.IndexOf("contracts: {", StringComparison.Ordinal);
        var end = batch.IndexOf("carriers: {", start, StringComparison.Ordinal);
        var config = batch[start..end];
        var page = File.ReadAllText(Path.Combine(RepoRoot, "frontend", "src", "pages", "ContractsPage.tsx"));

        Assert.Contains("Recorded contract terms", config);
        Assert.Contains("Legacy Origin Unverified", config);
        Assert.Contains("recordOrigin", config);
        Assert.DoesNotContain("Margin Risk", config);
        Assert.DoesNotContain("Underpriced", config);
        Assert.DoesNotContain("RiskBadge", page);
        Assert.Contains("Origin Unverified", page);
        Assert.DoesNotContain("margin risk", page, StringComparison.OrdinalIgnoreCase);
    }
}
