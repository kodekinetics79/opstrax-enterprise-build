using Opstrax.Api;

namespace Opstrax.Tests;

public sealed class TelemetryPayloadFingerprintTests
{
    private const string Fingerprint = "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";

    [Fact]
    public void MissingObservation_IsNew()
        => Assert.Equal(TelemetryPayloadReplayDecision.NewObservation,
            TelemetryPayloadFingerprint.Decide(0, null, 0, 0, Fingerprint));

    [Fact]
    public void ExactFingerprint_IsAnIdenticalReplay()
        => Assert.Equal(TelemetryPayloadReplayDecision.IdenticalReplay,
            TelemetryPayloadFingerprint.Decide(1, Fingerprint, 1, 1, Fingerprint));

    [Theory]
    [InlineData(null, 0, 0, 1)]
    [InlineData("", 0, 0, 1)]
    [InlineData("bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb", 1, 1, 1)]
    [InlineData("aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa", 1, 2, 1)]
    [InlineData("aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa", 1, 1, 2)]
    public void ExistingObservationWithoutOneExactFingerprint_IsAConflict(
        string? stored, long fingerprinted, long distinct, long existing)
        => Assert.Equal(TelemetryPayloadReplayDecision.Conflict,
            TelemetryPayloadFingerprint.Decide(existing, stored, fingerprinted, distinct, Fingerprint));

    [Fact]
    public void NativeReplay_RequiresTheStoredEventToBelongToTheAuthenticatedDevice()
    {
        var root = FindRepoRoot();
        var endpoint = File.ReadAllText(Path.Combine(root, "backend-dotnet", "Controllers", "EndpointMappings.cs"));

        Assert.Contains("SELECT id,device_id,ingest_fingerprint FROM location_events", endpoint,
            StringComparison.Ordinal);
        Assert.Contains("Convert.ToInt64(storedDevice) == deviceId", endpoint, StringComparison.Ordinal);
        Assert.Contains(": TelemetryPayloadReplayDecision.Conflict", endpoint, StringComparison.Ordinal);
    }

    [Fact]
    public void FingerprintMigration_IsRunnerEnrolledTransactionalAndReadinessGated()
    {
        var root = FindRepoRoot();
        var migration = File.ReadAllText(Path.Combine(root, "database", "migrations",
            "2026_08_26_stage91_telematics_ingest_fingerprint.sql"));
        var runner = File.ReadAllText(Path.Combine(root, "tools", "apply-neon-predeploy-migrations.sh"));
        var readiness = File.ReadAllText(Path.Combine(root, "backend-dotnet", "Services",
            "FleetProductionReadinessService.cs"));

        Assert.Contains("BEGIN;", migration, StringComparison.Ordinal);
        Assert.Contains("INSERT INTO schema_migrations", migration, StringComparison.Ordinal);
        Assert.Contains("COMMIT;", migration, StringComparison.Ordinal);
        Assert.Contains("2026_08_26_stage91_telematics_ingest_fingerprint", runner, StringComparison.Ordinal);
        Assert.Contains("2026_08_26_stage91_telematics_ingest_fingerprint", readiness, StringComparison.Ordinal);
        Assert.Contains("('location_events','ingest_fingerprint')", readiness, StringComparison.Ordinal);
        Assert.Contains("('fault_occurrences','payload_fingerprint')", readiness, StringComparison.Ordinal);
    }

    private static string FindRepoRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (Directory.Exists(Path.Combine(directory.FullName, "backend-dotnet")) &&
                Directory.Exists(Path.Combine(directory.FullName, "database", "migrations")))
                return directory.FullName;
            directory = directory.Parent;
        }
        throw new DirectoryNotFoundException("Could not find repository root");
    }
}
