using System;
using System.IO;
using Xunit;

namespace Opstrax.Tests;

public sealed class DashcamRuntimeTruthContractTests
{
    private static string Root => Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../"));

    [Fact]
    public void RuntimeSchema_DoesNotRecreatePlaceholderProviderOrAiConfidence()
    {
        var source = File.ReadAllText(Path.Combine(Root, "backend-dotnet", "Services", "Batch4SchemaService.cs"));

        // Demo-only seed text may legitimately contain the literal provider label when
        // DemoSeedGate is explicitly enabled. The runtime truth contract is narrower:
        // production/runtime schema repair must never recreate that label as a column
        // default, and AI confidence must remain nullable/unknown until provider evidence
        // supplies a real value.
        Assert.DoesNotContain(
            "new(\"dashcam_events\", \"video_provider\", \"VARCHAR(120) NOT NULL DEFAULT 'OpsTrax Placeholder'\")",
            source,
            StringComparison.Ordinal);
        Assert.DoesNotContain("new(\"dashcam_events\", \"ai_confidence\", \"DECIMAL(6,2) NOT NULL DEFAULT 84\")", source, StringComparison.Ordinal);
        Assert.Contains("new(\"dashcam_events\", \"video_provider\", \"VARCHAR(120) NULL\")", source, StringComparison.Ordinal);
        Assert.Contains("new(\"dashcam_events\", \"ai_confidence\", \"DECIMAL(6,2) NULL\")", source, StringComparison.Ordinal);
        Assert.DoesNotContain("'/placeholder/dashcam-thumb.jpg'", source, StringComparison.Ordinal);
        Assert.DoesNotContain("'/placeholder/road-facing.mp4'", source, StringComparison.Ordinal);
        Assert.DoesNotContain("'/placeholder/driver-facing.mp4'", source, StringComparison.Ordinal);
        Assert.DoesNotContain("'OpsTrax Placeholder'", source, StringComparison.Ordinal);
        Assert.DoesNotContain("'AI dashcam event '", source, StringComparison.Ordinal);
        Assert.DoesNotContain("INSERT INTO dashcam_events", source, StringComparison.Ordinal);
        Assert.Contains("new(\"dashcam_events\", \"source_authority\", \"VARCHAR(32) NOT NULL DEFAULT 'LegacyUnverified'\")", source, StringComparison.Ordinal);
        Assert.Contains("new(\"dashcam_events\", \"media_status\", \"VARCHAR(32) NOT NULL DEFAULT 'ProviderPending'\")", source, StringComparison.Ordinal);
    }

    [Fact]
    public void ProductionReadiness_AgreesWithStage100NullableConfidenceContract()
    {
        var source = File.ReadAllText(Path.Combine(Root, "backend-dotnet", "Services", "FleetProductionReadinessService.cs"));

        Assert.Contains("('dashcam_events','ai_confidence','numeric(6,2)',false,'','')", source, StringComparison.Ordinal);
        Assert.DoesNotContain("('dashcam_events','ai_confidence','numeric(6,2)',true,'84','')", source, StringComparison.Ordinal);
    }

    [Fact]
    public void Stage104_RemovesOnlyRecognizedLegacyCameraDemoRowsAndIsEnrolled()
    {
        var migration = File.ReadAllText(Path.Combine(Root, "database", "migrations", "2026_09_06_stage104_camera_demo_truth_cleanup.sql"));
        var runner = File.ReadAllText(Path.Combine(Root, "tools", "apply-neon-predeploy-migrations.sh"));

        Assert.Contains("source_authority IN ('LegacyUnverified','ProviderPending')", migration, StringComparison.Ordinal);
        Assert.Contains("LOWER(c.name) LIKE '%demo%'", migration, StringComparison.Ordinal);
        Assert.Contains("title ~ '^AI dashcam (review|event) [0-9]+$'", migration, StringComparison.Ordinal);
        Assert.Contains("deleted_at=NOW()", migration.Replace(" ", "", StringComparison.Ordinal), StringComparison.Ordinal);
        Assert.DoesNotContain("source_authority='Authoritative'", migration.Replace(" ", "", StringComparison.Ordinal), StringComparison.Ordinal);
        Assert.Contains("2026_09_06_stage104_camera_demo_truth_cleanup", runner, StringComparison.Ordinal);
        Assert.Contains("Stage104 camera demo truth cleanup is incomplete", runner, StringComparison.Ordinal);
    }
}
