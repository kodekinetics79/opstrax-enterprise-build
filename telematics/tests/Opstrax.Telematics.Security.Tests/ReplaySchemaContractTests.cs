using System.Text.RegularExpressions;

namespace Opstrax.Telematics.Security.Tests;

/// <summary>
/// Pins the ordering dependency between the durable replay guard and its schema.
/// </summary>
/// <remarks>
/// <para>
/// <c>PostgresReplayGuard</c> reads <c>pending_epoch_base</c> and <c>epoch_floor</c> on <b>every
/// location frame</b>. Against a database where migration <c>telematics/007</c> has not been
/// applied, that is not a degraded mode: Postgres raises <c>42703 column does not exist</c>, the
/// exception unwinds through the read loop, and the connection dies. Every connection, every
/// frame. The gateway would look healthy and accept no telemetry whatsoever.
/// </para>
/// <para>
/// <c>ProductionStorageReadinessService</c> exists to turn exactly that class of drift into a
/// named startup failure, but it only checks the columns it is told about. A column added to the
/// guard and not to the readiness contract is a silent outage waiting for a deployment that runs
/// the code before the migration — so the two are pinned together here.
/// </para>
/// </remarks>
public sealed class ReplaySchemaContractTests
{
    private static string RepoRoot =>
        Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../../"));

    private static string Read(params string[] parts) =>
        File.ReadAllText(Path.Combine(new[] { RepoRoot }.Concat(parts).ToArray()));

    /// <summary>
    /// Every column the guard selects from the device-state table must be enrolled in the boot
    /// readiness contract.
    /// </summary>
    [Fact]
    public void EveryDeviceStateColumnTheGuardReads_IsEnrolledInTheReadinessContract()
    {
        string guard = Read("telematics", "src", "Opstrax.Telematics.Gateway",
            "Security", "Replay", "PostgresReplayGuard.cs");
        string readiness = Read("telematics", "src", "Opstrax.Telematics.Gateway",
            "Infrastructure", "ProductionStorageReadinessService.cs");

        // The guard's SELECT against telemetry_replay_device_state.
        Match select = Regex.Match(guard, @"SELECT\s+([a-z_,\s]+?)\s+FROM telemetry_replay_device_state");
        Assert.True(select.Success, "could not find the guard's device-state SELECT");

        string[] columns = select.Groups[1].Value
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .ToArray();
        Assert.NotEmpty(columns);

        var unenrolled = columns
            .Where(column => !readiness.Contains(
                $"('telemetry_replay_device_state','{column}')", StringComparison.Ordinal))
            .ToArray();

        Assert.True(unenrolled.Length == 0,
            "PostgresReplayGuard reads column(s) the startup readiness check does not require: " +
            string.Join(", ", unenrolled) +
            ". Deploying this build ahead of its migration would accept no telemetry at all, and " +
            "the gateway would still report healthy.");
    }

    /// <summary>The columns the readiness contract requires must be the ones a migration creates.</summary>
    [Fact]
    public void TheEnrolledColumns_AreCreatedByAMigration()
    {
        string migration = Read("database", "migrations", "telematics", "007_replay_session_epoch.sql");

        Assert.Contains("ADD COLUMN IF NOT EXISTS pending_epoch_base", migration, StringComparison.Ordinal);
        Assert.Contains("ADD COLUMN IF NOT EXISTS epoch_floor", migration, StringComparison.Ordinal);
        Assert.Contains("telemetry_replay_device_state", migration, StringComparison.Ordinal);
    }

    /// <summary>
    /// The cross-epoch replay lookup needs its index. Without it the query still returns the right
    /// answer, so no test would fail; it just degrades to a scan of the device's entire replay
    /// history on every frame, which is a performance cliff that only appears under fleet load.
    /// </summary>
    [Fact]
    public void TheCrossEpochLookup_HasASupportingIndex()
    {
        string migration = Read("database", "migrations", "telematics", "007_replay_session_epoch.sql");
        string guard = Read("telematics", "src", "Opstrax.Telematics.Gateway",
            "Security", "Replay", "PostgresReplayGuard.cs");

        Assert.Contains("device_id AND content_hash", guard.Replace("=@device_id", "").Replace("=@content_hash", ""),
            StringComparison.Ordinal);
        Assert.Contains("(device_id, content_hash)", migration, StringComparison.Ordinal);
    }
}
