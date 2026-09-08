using System;
using System.IO;
using Xunit;

namespace Opstrax.Tests;

public sealed class FleetIntelligenceTruthContractTests
{
    private static string Root => Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../"));

    [Fact]
    public void IntelligenceSurface_DoesNotClaimUnevidencedPredictiveOrModelCapability()
    {
        var page = File.ReadAllText(Path.Combine(Root, "frontend", "src", "pages", "PredictiveAnalyticsPage.tsx"));
        var catalogue = File.ReadAllText(Path.Combine(Root, "frontend", "src", "modules", "moduleConfig.ts"));

        Assert.DoesNotContain("AI-Powered Intelligence", page, StringComparison.Ordinal);
        Assert.DoesNotContain("Predictive risk scoring", page, StringComparison.Ordinal);
        Assert.DoesNotContain("Maintenance Predictions", page, StringComparison.Ordinal);
        Assert.DoesNotContain("Avg Confidence", page, StringComparison.Ordinal);
        Assert.DoesNotContain("Maintenance failure predictions", catalogue, StringComparison.Ordinal);
        Assert.Contains("Rule-based risk signals", page, StringComparison.Ordinal);
        Assert.Contains("Risk scoring", page, StringComparison.Ordinal);
        Assert.Contains("Rule-based maintenance", catalogue, StringComparison.Ordinal);
    }
}
