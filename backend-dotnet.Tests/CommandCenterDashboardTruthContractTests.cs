using System.Text.RegularExpressions;

namespace Opstrax.Tests;

// ── Dashboard truth contract ────────────────────────────────────────────────
// The operations dashboard renders four feeds (command-center summary + the
// safety / maintenance / fleet-health bridges). Two classes of silent lie have
// shipped from these endpoints and are pinned here:
//
//  1. Row-key drift: Database row dictionaries are ToCamel'd (total_vehicles →
//     totalVehicles). A snake_case GetValueOrDefault lookup never matches, the
//     value serializes as JSON null, and the UI renders "no data" over real data.
//     This was invisible for weeks precisely because null serializes silently.
//  2. Plausible defaults: COALESCE(readiness_score,50) / (safety_score,100) and
//     `?? 100m` turned unmeasured tenants into a confident constant 70% health
//     score and a perfect 100% safety score. Absence must serialize as null.
public sealed class CommandCenterDashboardTruthContractTests
{
    [Fact]
    public void FleetHealthSummary_Reads_CamelCase_Row_Keys_Only()
    {
        var method = Method("private static async Task<IResult> FleetHealthSummary(", "private static async Task<IResult> FleetHealthRisks(");

        // The bug class: any GetValueOrDefault("snake_case") in this method misses
        // the ToCamel'd row keys and silently emits null.
        var snakeLookups = Regex.Matches(method, "GetValueOrDefault\\(\"([a-z0-9]+_[a-z0-9_]+)\"\\)");
        Assert.True(snakeLookups.Count == 0,
            $"snake_case row lookups can never match ToCamel'd row keys: {string.Join(", ", snakeLookups.Select(m => m.Groups[1].Value))}");

        // The readiness triad the dashboard renders must be read with the keys the
        // row dictionary actually contains.
        Assert.Contains("GetValueOrDefault(\"dispatchReadyVehicles\")", method, StringComparison.Ordinal);
        Assert.Contains("GetValueOrDefault(\"oosVehicles\")", method, StringComparison.Ordinal);
        Assert.Contains("GetValueOrDefault(\"criticalDefectVehicles\")", method, StringComparison.Ordinal);
        Assert.Contains("GetValueOrDefault(\"avgFleetReadiness\")", method, StringComparison.Ordinal);
        Assert.Contains("GetValueOrDefault(\"avgSafetyScore\")", method, StringComparison.Ordinal);
    }

    [Fact]
    public void FleetHealthSummary_Never_Fabricates_Scores_For_Unmeasured_Fleets()
    {
        var method = Method("private static async Task<IResult> FleetHealthSummary(", "private static async Task<IResult> FleetHealthRisks(");

        // SQL-side defaults that turned "unmeasured" into a mid-range score.
        Assert.DoesNotContain("COALESCE(v.readiness_score", method, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("COALESCE(d.safety_score", method, StringComparison.OrdinalIgnoreCase);

        // C#-side defaults: the composite must be null unless both inputs are measured
        // (ToDouble(null,50)*0.6 + ToDouble(null,100)*0.4 was a compile-time constant 70).
        Assert.DoesNotContain(", 50)", method, StringComparison.Ordinal);
        Assert.DoesNotContain(", 100)", method, StringComparison.Ordinal);
        Assert.Contains("double? fleetHealthScore", method, StringComparison.Ordinal);
    }

    [Fact]
    public void SafetyDashboard_Emits_Null_Not_Perfect_Score_When_Unscored()
    {
        var method = Method("private static async Task<IResult> SafetyDashboard(", "// ── GET /api/safety/rules");
        Assert.DoesNotContain("?? 100m", method, StringComparison.Ordinal);
        Assert.Contains("(decimal?)null", method, StringComparison.Ordinal);
    }

    [Fact]
    public void CommandCenterSummary_Scopes_By_Branch_Like_Its_Sibling_Feeds()
    {
        var method = Method("private static async Task<IResult> CommandCenterSummary(", "private static async Task<IResult> ControlTowerSummary(");

        // All four feeds on the dashboard must apply identical company+branch rules;
        // an unbranched fleet count beside a branch-filtered availability reads as
        // contradictory "No data" to a branch-scoped user.
        Assert.Contains("GetBranchId(http)", method, StringComparison.Ordinal);
        Assert.Contains("@branchId::bigint IS NULL OR branch_id=@branchId", method, StringComparison.Ordinal);
        Assert.Contains("RequirePermission(http, \"dashboard:view\")", method, StringComparison.Ordinal);
    }

    [Fact]
    public void CommandCenterSummary_Charts_Only_Series_With_Production_Writers()
    {
        var method = Method("private static async Task<IResult> CommandCenterSummary(", "private static async Task<IResult> ControlTowerSummary(");

        // safety_trends and fuel_anomalies are demo-seed-only tables: a chart over
        // them is empty forever for real tenants while implying live coverage.
        Assert.DoesNotContain("safety_trends", method, StringComparison.Ordinal);
        Assert.DoesNotContain("fuel_anomalies", method, StringComparison.Ordinal);

        // The throughput card is labeled "this week" — the query must be week-bounded.
        Assert.Contains("date_trunc('week', NOW())", method, StringComparison.Ordinal);
    }

    private static string Method(string startMarker, string endMarker)
    {
        var source = Read("backend-dotnet", "Controllers", "EndpointMappings.cs");
        var start = source.IndexOf(startMarker, StringComparison.Ordinal);
        Assert.True(start >= 0, $"start marker not found: {startMarker}");
        var end = source.IndexOf(endMarker, start + startMarker.Length, StringComparison.Ordinal);
        Assert.True(end > start, $"end marker not found after start: {endMarker}");
        return source[start..end];
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
