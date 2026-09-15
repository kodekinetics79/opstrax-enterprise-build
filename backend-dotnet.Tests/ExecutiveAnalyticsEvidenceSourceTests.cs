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
    public void OperationalPanels_RequireProvenance()
    {
        var handlers = File.ReadAllText(Path.Combine(RepoRoot, "backend-dotnet", "Controllers", "EndpointMappings.cs"));

        var operationsStart = handlers.IndexOf("private static async Task<IResult> AnalyticsOperations", StringComparison.Ordinal);
        var dispatchStart = handlers.IndexOf("private static async Task<IResult> AnalyticsDispatch", operationsStart, StringComparison.Ordinal);
        var safetyStart = handlers.IndexOf("private static async Task<IResult> AnalyticsSafety", dispatchStart, StringComparison.Ordinal);
        var maintenanceStart = handlers.IndexOf("private static async Task<IResult> AnalyticsMaintenance", safetyStart, StringComparison.Ordinal);
        var customerStart = handlers.IndexOf("private static async Task<IResult> AnalyticsCustomer", maintenanceStart, StringComparison.Ordinal);
        var operations = handlers[operationsStart..dispatchStart];
        var dispatch = handlers[dispatchStart..safetyStart];
        var safety = handlers[safetyStart..maintenanceStart];
        var maintenance = handlers[maintenanceStart..customerStart];
        Assert.Contains("QualifiedDispatchAssignmentSql", operations);
        Assert.Contains("QualifiedDispatchExceptionSql", operations);
        Assert.Contains("QualifiedDispatchAssignmentSql", dispatch);
        Assert.Contains("dispatch_proof_artifacts", dispatch);
        Assert.DoesNotContain("legacy_unverified", operations);
        Assert.DoesNotContain("legacy_unverified", dispatch);

        Assert.Contains("QualifiedSafetyEventSql", safety);
        Assert.Contains("QualifiedCoachingTaskSql", safety);
        Assert.Contains("QualifiedCoachingSourceSql", safety);
        Assert.Contains("source_authority='Authoritative'", handlers);
        Assert.Contains("media_status='Ready'", handlers);
        Assert.Contains("driverSafetyAvg = avgSafety.HasValue", safety);
        Assert.DoesNotContain("AVG(d.safety_score)", safety);
        Assert.Contains("QualifiedMaintenanceItemSql", maintenance);
        Assert.Contains("QualifiedWorkOrderSql", maintenance);
        Assert.Contains("QualifiedDvirReportSql", maintenance);
        Assert.Contains("QualifiedDvirDefectSql", maintenance);
        Assert.Contains("QualifiedDvirDefectSourceSql", maintenance);
        Assert.Contains("recurringFaultCodes = Array.Empty<object>()", maintenance);

        var ui = File.ReadAllText(Path.Combine(RepoRoot, "frontend", "src", "pages", "AnalyticsDashboardPage.tsx"));
        Assert.Contains("Qualified Route Compliance", ui);
        Assert.Contains("Qualified Safety Events", ui);
        Assert.Contains("Qualified Critical Defects", ui);
        Assert.Contains("Drivers by Qualified Event Count", ui);
        Assert.DoesNotContain("Qualified Driver Safety Avg", ui);
        Assert.DoesNotContain("target=\"85%\"", ui);

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

    [Fact]
    public void SafetyWorkflowWrites_StampEvidenceAndDoNotInventCoachingScores()
    {
        var handlers = File.ReadAllText(Path.Combine(RepoRoot, "backend-dotnet", "Controllers", "EndpointMappings.cs"));
        var pilot = File.ReadAllText(Path.Combine(RepoRoot, "backend-dotnet", "Controllers", "SafetyCoachingScorecardPilotEndpoints.cs"));
        var schema = File.ReadAllText(Path.Combine(RepoRoot, "backend-dotnet", "Services", "Batch4SchemaService.cs"));
        var migration = File.ReadAllText(Path.Combine(RepoRoot, "database", "migrations", "2026_09_08_safety_analytics_evidence_integrity.sql"));

        Assert.Contains("'user_workflow','recorded_by_authenticated_actor'", handlers);
        Assert.Contains("'user_workflow','recorded_by_authenticated_actor'", pilot);
        Assert.DoesNotContain("Generated from OpsTrax safety intelligence.", handlers);
        Assert.DoesNotContain("before_safety_score+6", handlers);
        Assert.DoesNotContain("effectiveness_score=COALESCE(effectiveness_score,88)", handlers);
        Assert.DoesNotContain("before_safety_score,due_at,row_version,updated_at)\n                  VALUES", pilot);
        Assert.Contains("NULL,@due,0,NOW()", pilot);
        Assert.Contains("DEFAULT 'legacy_unverified'", schema);
        Assert.Contains("DEFAULT 'unverified'", schema);
        Assert.Contains("ck_safety_events_evidence", migration);
        Assert.Contains("ck_coaching_tasks_evidence", migration);
    }

    [Fact]
    public void MaintenanceWorkflows_StampEvidenceAndProductionStartupDoesNotSeedPresets()
    {
        var handlers = File.ReadAllText(Path.Combine(RepoRoot, "backend-dotnet", "Controllers", "EndpointMappings.cs"));
        var dvir = File.ReadAllText(Path.Combine(RepoRoot, "backend-dotnet", "Controllers", "DvirHosEndpoints.cs"));
        var schema = File.ReadAllText(Path.Combine(RepoRoot, "backend-dotnet", "Services", "MaintenanceSchemaService.cs"));
        var migration = File.ReadAllText(Path.Combine(RepoRoot, "database", "migrations", "2026_09_08_maintenance_analytics_evidence_integrity.sql"));

        Assert.Contains("'dvir_workflow','derived_from_qualified_source'", handlers);
        Assert.Contains("'workflow_derived','derived_from_qualified_source'", handlers);
        Assert.Contains("'user_workflow','recorded_by_authenticated_actor'", dvir);
        Assert.Contains("DemoSeedGate.IsExplicitlyEnabled(configuration)", schema);
        Assert.DoesNotContain("foreach (var sql in Seeds)", schema);
        Assert.Contains("ck_maintenance_items_evidence", migration);
        Assert.Contains("ck_work_orders_evidence", migration);
        Assert.Contains("ck_dvir_reports_evidence", migration);
        Assert.Contains("ck_dvir_defects_evidence", migration);
    }
}
