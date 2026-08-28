using Opstrax.Api.Services;

namespace Opstrax.Tests;

public sealed class TelemetryWorkerColdStartRegressionTests
{
    [Fact]
    public void StaleDeviceScan_IsBoundedAndPublishesProgress()
    {
        Assert.InRange(TelemetryBackgroundService.MaxStaleAlertsPerTick, 1, 250);
        Assert.InRange(TelemetryBackgroundService.ProgressHeartbeatBatchSize, 1,
            TelemetryBackgroundService.MaxStaleAlertsPerTick);

        var source = File.ReadAllText(Path.Combine(
            FindRoot(), "backend-dotnet", "Services", "TelemetryBackgroundService.cs"));

        Assert.Contains("LIMIT 100", source, StringComparison.Ordinal);
        Assert.Contains("AND NOT EXISTS (", source, StringComparison.Ordinal);
        Assert.DoesNotContain("SELECT COUNT(*) FROM telemetry_alerts", source, StringComparison.Ordinal);
        Assert.Contains("processed % ProgressHeartbeatBatchSize == 0", source, StringComparison.Ordinal);
        Assert.Contains("await tracker.HeartbeatAsync(SvcName, runId, ct);", source, StringComparison.Ordinal);
        Assert.Contains("await tracker.PulseAsync(SvcName, ct);", source, StringComparison.Ordinal);
        Assert.Contains("staleAlertsProcessed >= MaxStaleAlertsPerTick", source, StringComparison.Ordinal);
    }

    private static string FindRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !Directory.Exists(Path.Combine(dir.FullName, "backend-dotnet")))
            dir = dir.Parent;
        return dir?.FullName ?? throw new DirectoryNotFoundException("Repository root not found.");
    }
}
