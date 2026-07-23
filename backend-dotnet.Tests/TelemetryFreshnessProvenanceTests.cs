using Opstrax.Api.Controllers;

namespace Opstrax.Tests;

// TEL-P1-TRUTH-003 regression.
//
// The live-map "freshness" label (live | delayed | stale) is the truth signal an
// operator reads to decide whether a dot is where the truck IS right now. If it is
// computed from received_at alone (when the PIPELINE last delivered a row), then a
// fix whose DEVICE clock is old — an offline-buffered batch dump, or a replayed frame
// stamped with a fresh gateway timestamp — lands with received_at=NOW() and is
// mislabeled 'live' even though the actual GPS fix is hours or days old.
//
// The fix: when the provenance columns exist, freshness must use the WORST (greatest)
// of receipt-age and device-fix-age, so an old device_fix_time forces delayed/stale.
// These tests pin that in both surfaces that emit freshness (the REST /positions
// snapshot and the SSE /stream), and confirm the pre-migration fallback (no provenance
// columns) still degrades to received_at-only rather than crashing.
public sealed class TelemetryFreshnessProvenanceTests
{
    [Fact]
    public void PositionsSnapshot_Freshness_UsesDeviceFixAge_NotReceiptAgeAlone()
    {
        var positions = MethodSource(
            "private static async Task<IResult> TelemetryPositions(",
            "// ── GET /api/telemetry/breadcrumbs");

        AssertHonestFreshness(positions);
    }

    [Fact]
    public void SseStream_Freshness_UsesDeviceFixAge_NotReceiptAgeAlone()
    {
        var stream = MethodSource("Build the SQL once, reuse it every 3s tick.", "FROM latest_vehicle_positions lvp");

        AssertHonestFreshness(stream);
    }

    private static void AssertHonestFreshness(string block)
    {
        // The honest-age expression is only valid when provenance columns exist, so it
        // must be built on the hasProv branch and reference device_fix_time inside a
        // GREATEST(receipt-age, fix-age) so the OLDER of the two wins.
        Assert.Contains("device_fix_time", block, StringComparison.Ordinal);
        Assert.Contains("GREATEST(", block, StringComparison.Ordinal);
        Assert.Contains("COALESCE(lvp.device_fix_time, lvp.received_at)", block, StringComparison.Ordinal);

        // The freshness CASE must classify off the freshAge expression, not directly off
        // received_at. Guard: the 'live' threshold must be driven by freshAge.
        Assert.Contains("{freshAge} <= 120", block, StringComparison.Ordinal);
        Assert.Contains("{freshAge} <= 900", block, StringComparison.Ordinal);

        // Deploy-safe fallback: with no provenance columns, degrade to received_at only.
        Assert.Contains("EXTRACT(EPOCH FROM (NOW() - lvp.received_at))::BIGINT", block, StringComparison.Ordinal);
    }

    private static string MethodSource(string startMarker, string endMarker)
    {
        var source = Source();
        var start = source.IndexOf(startMarker, StringComparison.Ordinal);
        var end = source.IndexOf(endMarker, start < 0 ? 0 : start, StringComparison.Ordinal);
        Assert.True(start >= 0 && end > start, $"Unable to locate source block {startMarker}");
        return source[start..end];
    }

    private static string Source()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !Directory.Exists(Path.Combine(dir.FullName, "backend-dotnet")))
            dir = dir.Parent;

        Assert.NotNull(dir);
        return File.ReadAllText(Path.Combine(
            dir!.FullName, "backend-dotnet", "Controllers", "EndpointMappings.cs"));
    }
}
