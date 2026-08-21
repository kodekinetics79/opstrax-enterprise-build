using Opstrax.Api.Services;

namespace Opstrax.Tests;

public sealed class MaintenanceWorkerHeartbeatRegressionTests
{
    [Fact]
    public void ProgressHeartbeat_CoversStartupAndBoundedVehicleBatches()
    {
        Assert.InRange(MaintenanceBackgroundService.ProgressHeartbeatBatchSize, 1, 250);

        var source = File.ReadAllText(Path.Combine(FindRoot(), "backend-dotnet", "Services", "MaintenanceBackgroundService.cs"));
        Assert.Contains("await tracker.HeartbeatAsync(SvcName, runId, ct);", source, StringComparison.Ordinal);
        Assert.Contains("await tracker.PulseAsync(SvcName, ct);", source, StringComparison.Ordinal);
        Assert.Contains("processedVehicles % ProgressHeartbeatBatchSize == 0", source, StringComparison.Ordinal);
        Assert.Contains("await ReportProgressAsync(runId, ct);", source, StringComparison.Ordinal);
    }

    private static string FindRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !Directory.Exists(Path.Combine(dir.FullName, "backend-dotnet")))
            dir = dir.Parent;
        return dir?.FullName ?? throw new DirectoryNotFoundException("Repository root not found.");
    }
}
