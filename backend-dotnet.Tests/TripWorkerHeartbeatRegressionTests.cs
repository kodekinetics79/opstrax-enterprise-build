namespace Opstrax.Tests;

public sealed class TripWorkerHeartbeatRegressionTests
{
    [Fact]
    public void TripCycle_ReportsCommittedProgressBeforeAndBetweenAllPhases()
    {
        var source = File.ReadAllText(Path.Combine(FindRoot(), "backend-dotnet", "Services", "TripBackgroundService.cs"));
        Assert.Contains("await tracker.HeartbeatAsync(SvcName, runId, ct);", source, StringComparison.Ordinal);
        Assert.Contains("await tracker.PulseAsync(SvcName, ct);", source, StringComparison.Ordinal);
        Assert.True(Occurrences(source, "await ReportProgressAsync(runId, ct);") >= 7);
    }

    private static int Occurrences(string source, string value) =>
        source.Split(value, StringSplitOptions.None).Length - 1;

    private static string FindRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !Directory.Exists(Path.Combine(dir.FullName, "backend-dotnet")))
            dir = dir.Parent;
        return dir?.FullName ?? throw new DirectoryNotFoundException("Repository root not found.");
    }
}
