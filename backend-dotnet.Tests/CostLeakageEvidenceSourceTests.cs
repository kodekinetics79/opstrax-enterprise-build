namespace Opstrax.Tests;

public sealed class CostLeakageEvidenceSourceTests
{
    private static string RepoRoot => Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../"));

    [Fact]
    public void CostLeakageHandlers_DoNotExposeDemoRowsOrInventActionEvidence()
    {
        var source = File.ReadAllText(Path.Combine(RepoRoot, "backend-dotnet", "Controllers", "EndpointMappings.cs"));
        var start = source.IndexOf("// BATCH 5 HANDLERS — COST LEAKAGE INTELLIGENCE", StringComparison.Ordinal);
        var end = source.IndexOf("// ===== BATCH 6 HANDLERS", start, StringComparison.Ordinal);
        var handlers = source[start..end];

        Assert.Contains("leakage_number LIKE 'RLK-%'", handlers);
        Assert.Contains("data_origin='runtime_detector'", handlers);
        Assert.Contains("Detected loss and action estimates are grouped by currency and never combined", handlers);
        Assert.Contains("Estimated savings must be a recorded amount greater than zero", handlers);
        Assert.Contains("RunInTenantTransactionAsync", handlers);
        Assert.DoesNotContain("CONCAT('$'", handlers);
        Assert.DoesNotContain("?? 1", handlers);
        Assert.DoesNotContain("Cost recovery action\"", handlers);
    }

    [Fact]
    public void RuntimeDetector_PersistsCurrencyProvenanceAndCanonicalStatus()
    {
        var source = File.ReadAllText(Path.Combine(RepoRoot, "backend-dotnet", "Services", "RevenueReadinessService.cs"));
        var start = source.IndexOf("public async Task<RevenueLeakageDetectionOutcome> DetectRevenueLeakageAsync", StringComparison.Ordinal);
        var end = source.IndexOf("private static decimal? DecN", start, StringComparison.Ordinal);
        var detector = source[start..end];

        Assert.Contains("pg_advisory_xact_lock", detector);
        Assert.Contains("UPPER(jc.currency)=UPPER(rc.currency)", detector);
        Assert.Contains("'runtime_detector'", detector);
        Assert.Contains("'Open'", detector);
        Assert.Contains("amount_evidence_status", detector);
    }

    [Fact]
    public void CostLeakageUi_SeparatesCurrencyAndDoesNotCreateFabricatedActions()
    {
        var source = File.ReadAllText(Path.Combine(RepoRoot, "frontend", "src", "pages", "Batch5FinancePage.tsx"));
        var start = source.IndexOf("\"cost-leakage\": {", StringComparison.Ordinal);
        var end = source.IndexOf("} satisfies Record<Kind", start, StringComparison.Ordinal);
        var config = source[start..end];

        Assert.Contains("Revenue Leakage Evidence", config);
        Assert.Contains("amountEvidenceStatus", config);
        Assert.Contains("actions: [\"acknowledge\"]", config);
        Assert.DoesNotContain("createAction", config);
        Assert.DoesNotContain("recoverableSavings", config);
        Assert.Contains("Currencies are never combined", source);
        Assert.DoesNotContain("estimatedSavings: 500", source);
    }
}
