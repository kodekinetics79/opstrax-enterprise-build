using System;
using System.IO;
using Xunit;

namespace Opstrax.Tests;

public sealed class HosOperationalAlertSourceTruthTests
{
    private static string RepoRoot =>
        Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../"));

    private static string Source => File.ReadAllText(Path.Combine(
        RepoRoot, "backend-dotnet", "Services", "OperationalAlertDetectionService.cs"));

    [Fact]
    public void HosAlertSweep_DoesNotTreatLegacyHosRecordsAsCertifiedEvidence()
    {
        var source = Source;

        Assert.DoesNotContain("SweepAsync(db, \"hos_violation\", HosRecordsSql", source, StringComparison.Ordinal);
        Assert.DoesNotContain("private const string HosRecordsSql", source, StringComparison.Ordinal);
        Assert.Contains("hos_records table can contain demo/manual values", source, StringComparison.Ordinal);
    }

    [Fact]
    public void HosAlertSweep_RequiresAuthoritativeFreshClockProvenance()
    {
        var source = Source;

        Assert.Contains("c.source_authority = 'Authoritative'", source, StringComparison.Ordinal);
        Assert.Contains("c.clock_source IS NOT NULL", source, StringComparison.Ordinal);
        Assert.Contains("c.source_observed_at IS NOT NULL", source, StringComparison.Ordinal);
        Assert.Contains("c.source_observed_at > NOW() - INTERVAL '24 hours'", source, StringComparison.Ordinal);
        Assert.Contains("hos_violation(authoritative_clocks)", source, StringComparison.Ordinal);
    }
}
