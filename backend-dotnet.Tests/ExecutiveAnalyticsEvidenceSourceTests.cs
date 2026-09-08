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

    [Fact]
    public void OperationalPanels_RequireProvenanceAndUnsupportedPanelsFailClosed()
    {
        var handlers = File.ReadAllText(Path.Combine(RepoRoot, "backend-dotnet", "Controllers", "EndpointMappings.cs"));

        var operationsStart = handlers.IndexOf("private static async Task<IResult> AnalyticsOperations", StringComparison.Ordinal);
        var dispatchStart = handlers.IndexOf("private static async Task<IResult> AnalyticsDispatch", operationsStart, StringComparison.Ordinal);
        var safetyStart = handlers.IndexOf("private static Task<IResult> AnalyticsSafety", dispatchStart, StringComparison.Ordinal);
        var operations = handlers[operationsStart..dispatchStart];
        var dispatch = handlers[dispatchStart..safetyStart];
        Assert.Contains("QualifiedDispatchAssignmentSql", operations);
        Assert.Contains("QualifiedDispatchExceptionSql", operations);
        Assert.Contains("QualifiedDispatchAssignmentSql", dispatch);
        Assert.Contains("dispatch_proof_artifacts", dispatch);
        Assert.DoesNotContain("legacy_unverified", operations);
        Assert.DoesNotContain("legacy_unverified", dispatch);

        AssertFailClosed("AnalyticsSafety", "AnalyticsMaintenance");
        AssertFailClosed("AnalyticsMaintenance", "AnalyticsCustomer");

        var ui = File.ReadAllText(Path.Combine(RepoRoot, "frontend", "src", "pages", "AnalyticsDashboardPage.tsx"));
        Assert.Contains("Qualified Route Compliance", ui);
        Assert.Contains("Qualified Safety Events", ui);
        Assert.Contains("Qualified Critical Defects", ui);
        Assert.DoesNotContain("target=\"85%\"", ui);

        void AssertFailClosed(string method, string nextMethod)
        {
            var start = handlers.IndexOf($"private static Task<IResult> {method}", StringComparison.Ordinal);
            var end = handlers.IndexOf(nextMethod, start + method.Length, StringComparison.Ordinal);
            var section = handlers[start..end];
            Assert.Contains("awaiting qualified evidence", section);
            Assert.Contains("source provenance", section);
            Assert.DoesNotContain("db.Scalar", section);
            Assert.DoesNotContain("db.Query", section);
        }
    }

    [Fact]
    public void DispatchWorkflowWrites_StampEvidenceWhileUnspecifiedRowsDefaultToUnverified()
    {
        var handlers = File.ReadAllText(Path.Combine(RepoRoot, "backend-dotnet", "Controllers", "EndpointMappings.cs"));
        var stage9 = File.ReadAllText(Path.Combine(RepoRoot, "backend-dotnet", "Services", "Stage9OperationalFoundationService.cs"));
        var schema = File.ReadAllText(Path.Combine(RepoRoot, "backend-dotnet", "Services", "DispatchSchemaService.cs"));
        var migration = File.ReadAllText(Path.Combine(RepoRoot, "database", "migrations", "2026_09_08_dispatch_analytics_evidence_integrity.sql"));

        Assert.Contains("'user_workflow', 'recorded_by_authenticated_actor'", handlers);
        Assert.Contains("'user_workflow','recorded_by_authenticated_actor'", handlers);
        Assert.Contains("'user_workflow','recorded_by_authenticated_actor'", stage9);
        Assert.Contains("DEFAULT 'legacy_unverified'", schema);
        Assert.Contains("DEFAULT 'unverified'", schema);
        Assert.Contains("ck_dispatch_assignments_evidence", migration);
        Assert.Contains("ck_dispatch_exceptions_evidence", migration);
    }
}
