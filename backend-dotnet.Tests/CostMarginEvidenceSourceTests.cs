namespace Opstrax.Tests;

public sealed class CostMarginEvidenceSourceTests
{
    private static string RepoRoot => Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../"));

    [Fact]
    public void CostMarginEndpoints_CannotReturnLegacySyntheticRowsOrInventedRecalculationCounts()
    {
        var source = File.ReadAllText(Path.Combine(RepoRoot, "backend-dotnet", "Controllers", "EndpointMappings.cs"));
        var start = source.IndexOf("// BATCH 5 HANDLERS — PREDICTIVE COST & MARGIN", StringComparison.Ordinal);
        var end = source.IndexOf("// BATCH 5 HANDLERS — COST LEAKAGE INTELLIGENCE", start, StringComparison.Ordinal);
        var handlers = source[start..end];

        Assert.DoesNotContain("cost_margin_records", handlers);
        Assert.DoesNotContain("cost_margin_predictions", handlers);
        Assert.DoesNotContain("jobsUpdated = 12", handlers);
        Assert.DoesNotContain("routesUpdated = 6", handlers);
        Assert.DoesNotContain("vehiclesUpdated = 8", handlers);
        Assert.DoesNotContain("600m", handlers);
        Assert.DoesNotContain("320m", handlers);
        Assert.DoesNotContain("280m", handlers);
        Assert.Contains("Status501NotImplemented", handlers);
        Assert.Contains("No persisted model prediction evidence is available", handlers);
    }

    [Fact]
    public void CostMarginService_RequiresPersistedEvidenceAndSeparatesCurrency()
    {
        var source = File.ReadAllText(Path.Combine(RepoRoot, "backend-dotnet", "Services", "CostMarginEvidenceService.cs"));

        Assert.Contains("issued_invoices", source);
        Assert.Contains("LOWER(e.approval_status)='approved'", source);
        Assert.Contains("NOT LIKE 'EXP-B5-%'", source);
        Assert.Contains("x.currency=r.currency", source);
        Assert.Contains("group.Key", source);
        Assert.Contains("Currencies are never combined", source);
        Assert.DoesNotContain("cost_margin_records", source);
        Assert.DoesNotContain("cost_margin_predictions", source);

        var revenueSource = File.ReadAllText(Path.Combine(RepoRoot, "backend-dotnet", "Services", "RevenueReadinessService.cs"));
        Assert.Contains("e.invoice_count > 0 AND e.cost_record_count > 0", revenueSource);
        Assert.Contains("'customer:' || e.customer_id || ':' || e.currency", revenueSource);
    }

    [Fact]
    public void CostMarginUi_PresentsRecordedEvidenceWithoutRecalculationOrRiskClaims()
    {
        var source = File.ReadAllText(Path.Combine(RepoRoot, "frontend", "src", "pages", "Batch5FinancePage.tsx"));
        var start = source.IndexOf("\"cost-margin\": {", StringComparison.Ordinal);
        var end = source.IndexOf("\"cost-leakage\": {", start, StringComparison.Ordinal);
        var config = source[start..end];

        Assert.Contains("Cost & Margin Evidence", config);
        Assert.Contains("issued invoices and approved, non-demo expenses", config);
        Assert.Contains("actions: []", config);
        Assert.DoesNotContain("marginRisk", config);
        Assert.DoesNotContain("riskScore", config);
        Assert.Contains("Currencies are never combined", source);
        Assert.Contains("Only jobs with both issued-revenue and approved-cost evidence appear", source);
    }
}
