using System;
using System.IO;
using Xunit;

namespace Opstrax.Tests;

public sealed class HosClockSourceTruthMigrationTests
{
    private static string RepoRoot =>
        Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../"));

    private static string Migration => File.ReadAllText(Path.Combine(
        RepoRoot, "database", "migrations", "2026_09_03_stage99_hos_clock_source_truth.sql"));

    [Fact]
    public void Stage99_RemovesUnverifiedLegalTimeDefaultsAndAddsAuthorityEvidence()
    {
        var sql = Migration;

        Assert.Contains("clock_source VARCHAR(80)", sql, StringComparison.Ordinal);
        Assert.Contains("source_event_id VARCHAR(160)", sql, StringComparison.Ordinal);
        Assert.Contains("source_observed_at TIMESTAMPTZ", sql, StringComparison.Ordinal);
        Assert.Contains("source_authority VARCHAR(32)", sql, StringComparison.Ordinal);

        Assert.Contains("ALTER COLUMN drive_time_remaining_minutes DROP NOT NULL", sql, StringComparison.Ordinal);
        Assert.Contains("ALTER COLUMN drive_time_remaining_minutes DROP DEFAULT", sql, StringComparison.Ordinal);
        Assert.Contains("ALTER COLUMN shift_time_remaining_minutes DROP DEFAULT", sql, StringComparison.Ordinal);
        Assert.Contains("ALTER COLUMN cycle_time_remaining_minutes DROP DEFAULT", sql, StringComparison.Ordinal);
        Assert.Contains("ALTER COLUMN status SET DEFAULT 'Unavailable'", sql, StringComparison.Ordinal);
    }

    [Fact]
    public void Stage99_FailsClosedExistingUnprovenClockRows()
    {
        var sql = Migration;

        Assert.Contains("drive_time_remaining_minutes = NULL", sql, StringComparison.Ordinal);
        Assert.Contains("shift_time_remaining_minutes = NULL", sql, StringComparison.Ordinal);
        Assert.Contains("cycle_time_remaining_minutes = NULL", sql, StringComparison.Ordinal);
        Assert.Contains("status = 'Unavailable'", sql, StringComparison.Ordinal);
        Assert.Contains("source_authority = 'LegacyUnverified'", sql, StringComparison.Ordinal);
        Assert.Contains("Authoritative ELD/HOS source not connected", sql, StringComparison.Ordinal);
    }

    [Fact]
    public void Stage99_RequiresProvenanceBeforeAClockCanBeAuthoritative()
    {
        var sql = Migration;

        Assert.Contains("source_authority = 'Authoritative'", sql, StringComparison.Ordinal);
        Assert.Contains("clock_source IS NOT NULL", sql, StringComparison.Ordinal);
        Assert.Contains("source_observed_at IS NOT NULL", sql, StringComparison.Ordinal);
        Assert.Contains("status IN ('OK','Warning','Violation')", sql, StringComparison.Ordinal);
        Assert.Contains("source_authority IN ('LegacyUnverified','ProviderPending')", sql, StringComparison.Ordinal);
        Assert.Contains("status = 'Unavailable'", sql, StringComparison.Ordinal);
        Assert.Contains("VALIDATE CONSTRAINT ck_hos_clocks_source_authority", sql, StringComparison.Ordinal);
    }

    [Fact]
    public void CurrentHosUiAlreadyRendersNullClockValuesAsUnavailable()
    {
        var page = File.ReadAllText(Path.Combine(RepoRoot, "frontend", "src", "pages", "HosEldPage.tsx"));

        Assert.Contains("if (value == null || value === \"\") return null", page, StringComparison.Ordinal);
        Assert.Contains("value == null ? \"Unavailable\"", page, StringComparison.Ordinal);
        Assert.Contains("Clock value unavailable", page, StringComparison.Ordinal);
    }
}
