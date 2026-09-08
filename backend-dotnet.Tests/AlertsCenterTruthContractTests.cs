namespace Opstrax.Tests;

public sealed class AlertsCenterTruthContractTests
{
    [Fact]
    public void CustomerAlertQueueUsesProductionWrittenTelemetryRecords()
    {
        var section = Section(
            "// ── Alerts / Exception Management",
            "private static Task<IResult> AlertRulesList");

        Assert.Contains("FROM telemetry_alerts ta", section, StringComparison.Ordinal);
        Assert.DoesNotContain("FROM ai_insights", section, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("UPDATE ai_insights", section, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("CASE WHEN ta.status='Resolved' THEN 'Closed'", section, StringComparison.Ordinal);
        Assert.Contains("aft.source_type='Telemetry'", section, StringComparison.Ordinal);
        Assert.Contains("source_type, title", section, StringComparison.Ordinal);
    }

    [Fact]
    public void AlertReadsAndMutationsFailClosedToTheAuthenticatedBranch()
    {
        var section = Section(
            "// ── Alerts / Exception Management",
            "private static Task<IResult> AlertRulesList");

        Assert.Contains("GetBranchId(http)", section, StringComparison.Ordinal);
        Assert.Contains("WHEN ta.vehicle_id IS NOT NULL THEN v.branch_id", section, StringComparison.Ordinal);
        Assert.Contains("WHEN ta.driver_id IS NOT NULL THEN d.branch_id", section, StringComparison.Ordinal);
        Assert.Contains("device_installations", section, StringComparison.Ordinal);
        Assert.Contains("if (affected == 0) return Results.NotFound", section, StringComparison.Ordinal);
    }

    [Fact]
    public void FollowUpTaskSourceIdentityIsPresentInRuntimeAndDeploymentSchema()
    {
        var runtime = Read("backend-dotnet", "Services", "AlertWorkflowSchemaService.cs");
        var migration = Read("database", "migrations", "2026_09_08_stage131_alert_source_truth.sql");
        var runner = Read("tools", "apply-neon-predeploy-migrations.sh");

        Assert.Contains("source_type VARCHAR(40) NOT NULL DEFAULT 'LegacyInsight'", runtime, StringComparison.Ordinal);
        Assert.Contains("idx_alert_tasks_source_alert", runtime, StringComparison.Ordinal);
        Assert.Contains("source_type VARCHAR(40) NOT NULL DEFAULT 'LegacyInsight'", migration, StringComparison.Ordinal);
        Assert.Contains("idx_alert_tasks_source_alert", migration, StringComparison.Ordinal);
        Assert.Contains("2026_09_08_stage131_alert_source_truth", runner, StringComparison.Ordinal);
        Assert.Contains("Stage131 alert source identity boundary is missing", runner, StringComparison.Ordinal);
    }

    private static string Section(string startMarker, string endMarker)
    {
        var source = Read("backend-dotnet", "Controllers", "EndpointMappings.cs");
        var start = source.IndexOf(startMarker, StringComparison.Ordinal);
        Assert.True(start >= 0, $"start marker not found: {startMarker}");
        var end = source.IndexOf(endMarker, start + startMarker.Length, StringComparison.Ordinal);
        Assert.True(end > start, $"end marker not found after start: {endMarker}");
        return source[start..end];
    }

    private static string Read(params string[] parts)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !Directory.Exists(Path.Combine(directory.FullName, "backend-dotnet")))
            directory = directory.Parent;
        Assert.NotNull(directory);
        return File.ReadAllText(Path.Combine([directory!.FullName, .. parts]));
    }
}
