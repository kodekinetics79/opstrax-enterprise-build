namespace Opstrax.Tests;

public sealed class ControlTowerTruthContractTests
{
    [Fact]
    public void SummaryReturnsOnlyPersistedOperationalEvidence()
    {
        var source = Read("backend-dotnet", "Controllers", "EndpointMappings.cs");
        var start = source.IndexOf("private static async Task<IResult> ControlTowerSummary(", StringComparison.Ordinal);
        var end = source.IndexOf("private static async Task<IResult> ControlTowerEntities(", start, StringComparison.Ordinal);
        Assert.True(start >= 0 && end > start);
        var summary = source[start..end];

        Assert.Contains("Current operational snapshot", summary, StringComparison.Ordinal);
        Assert.DoesNotContain("Live Simulation", summary, StringComparison.Ordinal);
        Assert.DoesNotContain("placeholder", summary, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("competitorGapAnalysis", summary, StringComparison.Ordinal);
        Assert.DoesNotContain("available = true", summary, StringComparison.Ordinal);
    }

    [Fact]
    public void DiagnosticAndColdChainReadsExposeStoredProvenanceAndRelatedIds()
    {
        var endpoints = Read("backend-dotnet", "Controllers", "EndpointMappings.cs");
        Assert.Contains("fc.source_event_id fault_source_event_id", endpoints, StringComparison.Ordinal);
        Assert.Contains("fc.last_source_event_id fault_last_source_event_id", endpoints, StringComparison.Ordinal);

        var cold = Read("backend-dotnet", "Controllers", "FleetTmsColdChainEndpoints.cs");
        Assert.Contains("r.source_channel, r.client_generated_id, r.correlation_id, r.causation_id", cold, StringComparison.Ordinal);
        Assert.Contains("a.source_channel, a.client_generated_id, a.correlation_id, a.causation_id", cold, StringComparison.Ordinal);
        Assert.Contains("r.applied_policy_code, r.applied_policy_scope", cold, StringComparison.Ordinal);
        Assert.Contains("a.device_id", cold, StringComparison.Ordinal);
        Assert.Contains("a.reading_id", cold, StringComparison.Ordinal);
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
