namespace Opstrax.Tests;

public sealed class CarrierEvidenceSourceTests
{
    private static string RepoRoot => Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../"));

    [Fact]
    public void CarrierSurfaces_ExcludeDemoRows_AndDoNotPresentUnverifiedScoresAsTruth()
    {
        var handlers = File.ReadAllText(Path.Combine(RepoRoot, "backend-dotnet", "Controllers", "EndpointMappings.cs"));
        var start = handlers.IndexOf("// BATCH 5 HANDLERS — CARRIERS", StringComparison.Ordinal);
        var end = handlers.IndexOf("// BATCH 5 HANDLERS — PREDICTIVE COST & MARGIN", start, StringComparison.Ordinal);
        var carrierHandlers = handlers[start..end];

        Assert.Contains("data_origin,'legacy_unverified')<>'demo_seed'", carrierHandlers);
        Assert.Contains("compliance_evidence_status IN ('authority_verified','provider_verified')", carrierHandlers);
        Assert.Contains("calculation_status='calculated_from_jobs'", carrierHandlers);
        Assert.Contains("calculation_status='provider_reported'", carrierHandlers);
        Assert.Contains("'Unverified' END compliance_status", carrierHandlers);
        Assert.Contains("'manual_entry', 'unverified'", carrierHandlers);
        Assert.DoesNotContain("AVG(performance_score)", carrierHandlers);
        Assert.DoesNotContain("AVG(on_time_percent)", carrierHandlers);
        Assert.DoesNotContain("carrier_cost_this_month", carrierHandlers);
        Assert.DoesNotContain("preferred_carriers", carrierHandlers);
        Assert.DoesNotContain("SELECT c.*", carrierHandlers);
        Assert.DoesNotContain("SELECT cp.*", carrierHandlers);
        Assert.DoesNotContain("SELECT cd.*", carrierHandlers);
    }

    [Fact]
    public void CarrierUi_LabelsEvidence_AndDoesNotSeedComplianceOrPerformanceClaims()
    {
        var page = File.ReadAllText(Path.Combine(RepoRoot, "frontend", "src", "pages", "Batch5FinancePage.tsx"));
        var start = page.IndexOf("carriers: {", StringComparison.Ordinal);
        var end = page.IndexOf("\"cost-margin\": {", start, StringComparison.Ordinal);
        var config = page[start..end];
        var defaultStart = page.IndexOf("function defaultForm", StringComparison.Ordinal);
        var defaults = page[defaultStart..];

        Assert.Contains("Recorded carrier registry and evidence", config);
        Assert.Contains("Verified Compliance", config);
        Assert.Contains("Legacy Origin Unverified", config);
        Assert.Contains("Evidence-qualified Performance", config);
        Assert.Contains("verificationStatus", config);
        Assert.DoesNotContain("Avg Performance", config);
        Assert.DoesNotContain("On-Time %", config);
        Assert.DoesNotContain("Compliance Risk", config);
        Assert.DoesNotContain("Preferred", config);
        Assert.Contains("if (kind === \"carriers\")   return { status: \"Pending\" };", defaults);
        Assert.DoesNotContain("complianceStatus: \"Compliant\"", defaults);

        var workspace = File.ReadAllText(Path.Combine(RepoRoot, "frontend", "src", "pages", "FleetWorkspacePage.tsx"));
        Assert.Contains("Performance evidence", workspace);
        Assert.Contains("carrier records await authority or provider verification", workspace);
        Assert.Contains("evidence-qualified performance records", workspace);
        Assert.DoesNotContain("Avg on-time", workspace);
        Assert.DoesNotContain("carrier.onTime", workspace);
        Assert.DoesNotContain("raw.performanceScore", workspace);
    }
}
