namespace Opstrax.Tests;

// Regression: the customer-visibility ETA engine (ComputeEtaAsync) queried a `recorded_at` column
// that does NOT exist on latest_vehicle_positions (it has received_at / event_time / device_fix_time).
// That threw Postgres 42703 -> HTTP 500 on every customer tracking request for a shipment with an
// assigned vehicle. This pins the fix: staleness is computed off received_at, and recorded_at is gone.
public sealed class CustomerVisibilityEtaRegressionTests
{
    [Fact]
    public void ComputeEta_DoesNotReferenceNonexistentRecordedAtColumn()
    {
        var source = Source();
        // The broken column that 42703'd/500'd the customer tracker must be gone from ALL queries
        // against latest_vehicle_positions (the table has received_at/event_time/device_fix_time only).
        Assert.DoesNotContain("recorded_at FROM latest_vehicle_positions", source, StringComparison.Ordinal);
        // Staleness now derives from received_at (always present), computed in SQL.
        Assert.Contains("NOW() - received_at))::BIGINT AS stale_seconds FROM latest_vehicle_positions", source, StringComparison.Ordinal);
    }

    private static string Source()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !Directory.Exists(Path.Combine(dir.FullName, "backend-dotnet")))
            dir = dir.Parent;
        Assert.NotNull(dir);
        return File.ReadAllText(Path.Combine(dir!.FullName, "backend-dotnet", "Controllers", "EndpointMappings.cs"));
    }
}
