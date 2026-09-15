namespace Opstrax.Tests;

public sealed class SlaKpiEvidenceSourceTests
{
    private static string RepoRoot => Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../"));

    [Fact]
    public void KpiAndSlaHandlers_DoNotPromoteDemoOrMissingEvidenceToCustomerClaims()
    {
        var handlers = File.ReadAllText(Path.Combine(RepoRoot, "backend-dotnet", "Controllers", "EndpointMappings.cs"));
        var start = handlers.IndexOf("// Counts recorded operational rows", StringComparison.Ordinal);
        var end = handlers.IndexOf("private static async Task<IResult> AuditLogs", start, StringComparison.Ordinal);
        var evidenceHandlers = handlers[start..end];

        Assert.Contains("RequirePermission(http, \"reports:view\")", evidenceHandlers);
        Assert.Contains("QualifiedSlaEvidenceSql", evidenceHandlers);
        Assert.Contains("measurement_evidence_status='calculated_from_events'", evidenceHandlers);
        Assert.Contains("measurement_evidence_status='provider_verified'", evidenceHandlers);
        Assert.Contains("measurement_evidence_status='manual_verified'", evidenceHandlers);
        Assert.Contains("data_origin IN ('manual_entry','provider_import')", evidenceHandlers);
        Assert.Contains("verification_status='verified'", evidenceHandlers);
        Assert.Contains("known generated fixtures excluded", evidenceHandlers);
        Assert.DoesNotContain("SELECT * FROM kpi_metrics", evidenceHandlers);
        Assert.DoesNotContain("AVG(safety_score)", evidenceHandlers);
        Assert.DoesNotContain("AVG(readiness_score)", evidenceHandlers);
        Assert.DoesNotContain("sla_status IS NULL", evidenceHandlers);
        Assert.DoesNotContain("driverTotal > 0 ? 95m", evidenceHandlers);
        Assert.DoesNotContain("SimpleUpdateStatus", evidenceHandlers);
    }

    [Fact]
    public void SlaKpiUi_ExplainsEvidenceGaps_AndDoesNotClaimSuccessFromEmptyData()
    {
        var page = File.ReadAllText(Path.Combine(RepoRoot, "frontend", "src", "pages", "SlaKpiPage.tsx"));

        Assert.Contains("Recorded KPI measurements and verified SLA evidence", page);
        Assert.Contains("No verified target recorded", page);
        Assert.Contains("No verified KPI comparisons available", page);
        Assert.Contains("No verified SLA measurements recorded", page);
        Assert.Contains("This does not establish that every SLA commitment was met.", page);
        Assert.Contains("Grounded recommendation", page);
        Assert.DoesNotContain("All SLA commitments are within acceptable thresholds.", page);
        Assert.DoesNotContain("Target: {target.toLocaleString", page);
        Assert.DoesNotContain("AI recommendations", page);
    }
}
