namespace Opstrax.Tests;

public sealed class ExecutiveAnalyticsEvidenceSourceTests
{
    private static string RepoRoot => Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../"));

    [Fact]
    public void ExecutiveAndSlaAnalytics_DoNotUseGeneratedScoresOrMissingSlaStatus()
    {
        var handlers = File.ReadAllText(Path.Combine(RepoRoot, "backend-dotnet", "Controllers", "EndpointMappings.cs"));
        var executiveStart = handlers.IndexOf("private static async Task<IResult> ExecutiveSummary", StringComparison.Ordinal);
        var executiveEnd = handlers.IndexOf("private static string String", executiveStart, StringComparison.Ordinal);
        var executive = handlers[executiveStart..executiveEnd];
        Assert.Contains("QualifiedExecutiveSnapshotSql", executive);
        Assert.Contains("calculated_from_qualified_sources", executive);
        Assert.Contains("QualifiedSlaEvidenceSql", executive);
        Assert.DoesNotContain("SELECT * FROM executive_snapshots", executive);

        var analyticsStart = handlers.IndexOf("private static async Task<IResult> AnalyticsExecutive", StringComparison.Ordinal);
        var analyticsEnd = handlers.IndexOf("private static async Task<IResult> AnalyticsOperations", analyticsStart, StringComparison.Ordinal);
        var analytics = handlers[analyticsStart..analyticsEnd];
        Assert.Contains("RecordedFleetCountsFor", analytics);
        Assert.DoesNotContain("fleetUtilTarget    = 88m", analytics);
        Assert.DoesNotContain("otdTarget          = 96m", analytics);
        Assert.DoesNotContain("safetyTarget       = 85m", analytics);
        Assert.DoesNotContain("AVG(compliance_score)", analytics);

        var slaAnalyticsStart = handlers.IndexOf("private static async Task<IResult> AnalyticsCustomer", StringComparison.Ordinal);
        var slaAnalyticsEnd = handlers.IndexOf("// P9 — Platform Operations Endpoints", slaAnalyticsStart, StringComparison.Ordinal);
        var slaAnalytics = handlers[slaAnalyticsStart..slaAnalyticsEnd];
        Assert.Contains("QualifiedSlaEvidenceSql", slaAnalytics);
        Assert.Contains("verified On-Time Delivery SLA measurements", slaAnalytics);
        Assert.DoesNotContain("sla_status IS NULL", slaAnalytics);
    }

    [Fact]
    public void ExecutiveUi_PresentsRecordCountsWithoutInventedTargetsOrRates()
    {
        var executive = File.ReadAllText(Path.Combine(RepoRoot, "frontend", "src", "pages", "ExecutivePage.tsx"));
        Assert.Contains("Evidence-qualified record counts", executive);
        Assert.Contains("Known generated fixtures are excluded", executive);
        Assert.DoesNotContain("Derived Current Rates", executive);
        Assert.DoesNotContain("On-time Delivery", executive);
        Assert.DoesNotContain("Driver Safety Average", executive);

        var analytics = File.ReadAllText(Path.Combine(RepoRoot, "frontend", "src", "pages", "AnalyticsDashboardPage.tsx"));
        Assert.Contains("known generated fixtures excluded", analytics);
        Assert.Contains("Verified SLA Met Rate", analytics);
        Assert.Contains("Verified OTD Last 30d", analytics);
        Assert.DoesNotContain("target=\"95%\"", analytics);
    }
}
