namespace Opstrax.Tests;

// TEL-P1-REPLAY-005 — durable cross-instance replay defense for POST /api/telemetry/gps-ingest.
//
// The DB-level guarantees (atomic UNIQUE(gateway_id, signature), accept-once, duplicate/raw-
// duplicate rejection, retention that does not reopen the window) are verified directly against
// Postgres in the change's evidence. These source-inspection tests pin the HANDLER + GUARD wiring
// that a unit-less DB test cannot see: that the durable guard replaced the old in-memory cache,
// fails closed, and emits the metric + audit signals gps-ingest previously lacked.
public sealed class TelemetryGatewayReplayTests
{
    [Fact]
    public void GpsIngest_UsesDurableReplayGuard_NotTheOldInMemoryCache()
    {
        var source = EndpointsSource();
        // The process-local cache that could not survive restart / span instances is gone.
        Assert.DoesNotContain("GpsGatewayReplayCache", source, StringComparison.Ordinal);

        var ingest = Block(source, "private static async Task<IResult> GpsTrackerIngest(", "// ── GET /api/telemetry/metrics");
        Assert.Contains("GpsGatewayReplayGuard.DetermineAvailabilityAsync", ingest, StringComparison.Ordinal);
        Assert.Contains("GpsGatewayReplayGuard.TryReserveDurableAsync", ingest, StringComparison.Ordinal);
        // Reservation is scoped by the resolved device + tenant (passed to the guard).
        Assert.Contains("deviceId, companyId, ct", ingest, StringComparison.Ordinal);
    }

    [Fact]
    public void GpsIngest_KeysReplayOnCanonicalHmacBytes_NotRawHeaderCase()
    {
        // The hex-case bypass fix: reserve on the lowercase hex of the verified HMAC bytes, never
        // on the raw request header string (which authenticates case-insensitively).
        var ingest = Block(EndpointsSource(), "private static async Task<IResult> GpsTrackerIngest(", "// ── GET /api/telemetry/metrics");
        Assert.Contains("Convert.ToHexString(expected).ToLowerInvariant()", ingest, StringComparison.Ordinal);
        // The raw header string must NOT be what gets reserved.
        Assert.DoesNotContain("signatureRaw!, timestamp", ingest, StringComparison.Ordinal);
    }

    [Fact]
    public void GpsIngest_DurableReservationIsInsideTheWriteTransaction()
    {
        // TOCTOU fix: the durable reserve must sit INSIDE db.RunInSystemScopeAsync (same tx as the
        // writes) so a write failure rolls the nonce back. Assert the reserve call appears after
        // the scope opens and a duplicate is handled post-commit.
        var ingest = Block(EndpointsSource(), "private static async Task<IResult> GpsTrackerIngest(", "// ── GET /api/telemetry/metrics");
        AssertOrdered(ingest, "db.RunInSystemScopeAsync", "TryReserveDurableAsync");
        AssertOrdered(ingest, "TryReserveDurableAsync", "durableReplayDuplicate = true");
        Assert.Contains("if (durableReplayDuplicate)", ingest, StringComparison.Ordinal);
    }

    [Fact]
    public void GpsIngest_Duplicate_Rejects409_WithMetricAndAudit()
    {
        var ingest = Block(EndpointsSource(), "private static async Task<IResult> GpsTrackerIngest(", "// ── GET /api/telemetry/metrics");

        Assert.Contains("Interlocked.Increment(ref _telemetryRejectedReplay)", ingest, StringComparison.Ordinal);
        Assert.Contains("Interlocked.Increment(ref _telemetryRejected)", ingest, StringComparison.Ordinal);
        Assert.Contains("telemetry.gps.replay_rejected", ingest, StringComparison.Ordinal);
        Assert.Contains("Results.Conflict(", ingest, StringComparison.Ordinal);
    }

    [Fact]
    public void GpsIngest_FailsClosed_OnProbeError_NotFallback()
    {
        var ingest = Block(EndpointsSource(), "private static async Task<IResult> GpsTrackerIngest(", "// ── GET /api/telemetry/metrics");

        // A probe error routes to a hard 503 (fail closed), NOT the in-memory fallback.
        var probeErrIdx = ingest.IndexOf("Availability.ProbeError", StringComparison.Ordinal);
        Assert.True(probeErrIdx >= 0, "Missing ProbeError fail-closed branch");
        var branch = ingest[probeErrIdx..];
        Assert.Contains("telemetry.gps.replay_store_unavailable", branch, StringComparison.Ordinal);
        Assert.Contains("Status503ServiceUnavailable", branch, StringComparison.Ordinal);
    }

    [Fact]
    public void ReplayGuard_IsAtomic_DeploySafe_AndFailsClosedOnProbeError()
    {
        var guard = GuardSource();
        // Atomic DB reservation via ON CONFLICT DO NOTHING on the scoping key.
        Assert.Contains("ON CONFLICT (gateway_id, signature) DO NOTHING", guard, StringComparison.Ordinal);
        Assert.Contains("return rows == 1", guard, StringComparison.Ordinal);
        // Tri-state probe: Present / Absent / ProbeError, with a definitive-only cache.
        Assert.Contains("information_schema.tables", guard, StringComparison.Ordinal);
        Assert.Contains("Availability.ProbeError", guard, StringComparison.Ordinal);
        // A probe EXCEPTION must return ProbeError (fail closed), never a false negative.
        AssertOrdered(guard, "catch", "return Availability.ProbeError");
        // Absent table -> in-memory fallback so ingest never hard-fails pre-migration.
        Assert.Contains("TryReserveInMemory", guard, StringComparison.Ordinal);
    }

    [Fact]
    public void ReplayTable_HasDualSchemaPath_EnsureAndMigration()
    {
        // Owner/dev self-heal via TelemetrySchemaService.
        var schema = File.ReadAllText(Path.Combine(RepoRoot(), "backend-dotnet", "Services", "TelemetrySchemaService.cs"));
        Assert.Contains("CREATE TABLE IF NOT EXISTS gps_gateway_replay", schema, StringComparison.Ordinal);
        Assert.Contains("UNIQUE (gateway_id, signature)", schema, StringComparison.Ordinal);

        // Restricted-prod owner migration exists with the same key + grants.
        var mig = File.ReadAllText(Path.Combine(RepoRoot(), "database", "migrations", "2026_07_14_stage33_gps_gateway_replay.sql"));
        Assert.Contains("UNIQUE (gateway_id, signature)", mig, StringComparison.Ordinal);
        Assert.Contains("GRANT SELECT, INSERT, DELETE ON gps_gateway_replay TO opstrax_app", mig, StringComparison.Ordinal);

        // Bounded retention wired, far beyond the 300s freshness window.
        var bg = File.ReadAllText(Path.Combine(RepoRoot(), "backend-dotnet", "Services", "TelemetryBackgroundService.cs"));
        Assert.Contains("DELETE FROM gps_gateway_replay WHERE received_at <", bg, StringComparison.Ordinal);
    }

    // ── helpers ──────────────────────────────────────────────────────────────
    private static string EndpointsSource() =>
        File.ReadAllText(Path.Combine(RepoRoot(), "backend-dotnet", "Controllers", "EndpointMappings.cs"));

    private static string GuardSource() =>
        File.ReadAllText(Path.Combine(RepoRoot(), "backend-dotnet", "Services", "GpsGatewayReplayGuard.cs"));

    private static string Block(string source, string startMarker, string endMarker)
    {
        var start = source.IndexOf(startMarker, StringComparison.Ordinal);
        var end = source.IndexOf(endMarker, start < 0 ? 0 : start, StringComparison.Ordinal);
        Assert.True(start >= 0 && end > start, $"Unable to locate source block {startMarker}");
        return source[start..end];
    }

    private static void AssertOrdered(string source, string first, string second)
    {
        var a = source.IndexOf(first, StringComparison.Ordinal);
        var b = source.IndexOf(second, StringComparison.Ordinal);
        Assert.True(a >= 0, $"Missing marker: {first}");
        Assert.True(b > a, $"Expected '{first}' before '{second}'");
    }

    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !Directory.Exists(Path.Combine(dir.FullName, "backend-dotnet")))
            dir = dir.Parent;
        Assert.NotNull(dir);
        return dir!.FullName;
    }
}
