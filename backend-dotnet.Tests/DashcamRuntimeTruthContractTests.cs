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

        Assert.DoesNotContain("OpsTrax Placeholder", source, StringComparison.Ordinal);
        Assert.DoesNotContain("new(\"dashcam_events\", \"ai_confidence\", \"DECIMAL(6,2) NOT NULL DEFAULT 84\")", source, StringComparison.Ordinal);
        Assert.Contains("new(\"dashcam_events\", \"video_provider\", \"VARCHAR(120) NULL\")", source, StringComparison.Ordinal);
        Assert.Contains("new(\"dashcam_events\", \"ai_confidence\", \"DECIMAL(6,2) NULL\")", source, StringComparison.Ordinal);
    }

    [Fact]
    public void ProductionReadiness_AgreesWithStage100NullableConfidenceContract()
    {
        var source = File.ReadAllText(Path.Combine(Root, "backend-dotnet", "Services", "FleetProductionReadinessService.cs"));

        Assert.Contains("('dashcam_events','ai_confidence','numeric(6,2)',false,'','')", source, StringComparison.Ordinal);
        Assert.DoesNotContain("('dashcam_events','ai_confidence','numeric(6,2)',true,'84','')", source, StringComparison.Ordinal);
    }
}
