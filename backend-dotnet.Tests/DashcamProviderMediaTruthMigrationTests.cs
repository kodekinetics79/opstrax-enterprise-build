using System;
using System.IO;
using Xunit;

namespace Opstrax.Tests;

public sealed class DashcamProviderMediaTruthMigrationTests
{
    private static string RepoRoot =>
        Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../"));

    private static string Migration => File.ReadAllText(Path.Combine(
        RepoRoot, "database", "migrations", "2026_09_03_stage100_dashcam_provider_media_truth.sql"));

    [Fact]
    public void Stage100_RemovesPlaceholderProviderAndConfidenceDefaults()
    {
        var sql = Migration;

        Assert.Contains("ALTER COLUMN video_provider DROP DEFAULT", sql, StringComparison.Ordinal);
        Assert.Contains("ALTER COLUMN video_provider DROP NOT NULL", sql, StringComparison.Ordinal);
        Assert.Contains("ALTER COLUMN ai_confidence DROP DEFAULT", sql, StringComparison.Ordinal);
        Assert.Contains("ALTER COLUMN ai_confidence DROP NOT NULL", sql, StringComparison.Ordinal);
        Assert.Contains("source_authority VARCHAR(32)", sql, StringComparison.Ordinal);
        Assert.Contains("media_status VARCHAR(32)", sql, StringComparison.Ordinal);
    }

    [Fact]
    public void Stage100_FailsClosedLegacyMediaAndMeasuredConfidenceClaims()
    {
        var sql = Migration;

        Assert.Contains("road_facing_clip_url = NULL", sql, StringComparison.Ordinal);
        Assert.Contains("driver_facing_clip_url = NULL", sql, StringComparison.Ordinal);
        Assert.Contains("thumbnail_url = NULL", sql, StringComparison.Ordinal);
        Assert.Contains("ai_confidence = NULL", sql, StringComparison.Ordinal);
        Assert.Contains("source_authority = 'LegacyUnverified'", sql, StringComparison.Ordinal);
        Assert.Contains("media_status = 'Unavailable'", sql, StringComparison.Ordinal);
    }

    [Fact]
    public void Stage100_RequiresProviderEvidenceForAuthoritativeEventsAndReadyMedia()
    {
        var sql = Migration;

        Assert.Contains("source_authority='Authoritative'", sql, StringComparison.Ordinal);
        Assert.Contains("provider_event_id IS NOT NULL", sql, StringComparison.Ordinal);
        Assert.Contains("provider_received_at IS NOT NULL", sql, StringComparison.Ordinal);
        Assert.Contains("provider_payload_hash ~ '^[0-9a-f]{64}$'", sql, StringComparison.Ordinal);
        Assert.Contains("media_status <> 'Ready'", sql, StringComparison.Ordinal);
        Assert.Contains("road_facing_media_ref IS NOT NULL OR driver_facing_media_ref IS NOT NULL", sql, StringComparison.Ordinal);
        Assert.Contains("VALIDATE CONSTRAINT ck_dashcam_source_authority", sql, StringComparison.Ordinal);
        Assert.Contains("VALIDATE CONSTRAINT ck_dashcam_ready_media_reference", sql, StringComparison.Ordinal);
    }

    [Fact]
    public void Stage100_AddsBranchAndIdempotentProviderEventKeys()
    {
        var sql = Migration;

        Assert.Contains("ADD COLUMN IF NOT EXISTS branch_id BIGINT NULL", sql, StringComparison.Ordinal);
        Assert.Contains("uq_dashcam_provider_event", sql, StringComparison.Ordinal);
        Assert.Contains("company_id, LOWER(BTRIM(video_provider)), provider_event_id", sql, StringComparison.Ordinal);
        Assert.Contains("idx_dashcam_company_branch_occurred", sql, StringComparison.Ordinal);
    }
}
