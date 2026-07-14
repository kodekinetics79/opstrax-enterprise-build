using System.Data;
using Npgsql;

namespace Opstrax.Telematics.Gateway.Security.Replay;

/// <summary>
/// A durable, shared <see cref="ITelemetryReplayGuard"/> backed by the
/// <c>telemetry_replay_seen</c> table (see migration
/// <c>database/migrations/telematics/005_replay_guard.sql</c>). Its replay guarantee is the same
/// atomic primitive the strong ingest path uses for <c>telemetry_nonces</c>: a
/// <c>UNIQUE(device_id, serial, content_hash)</c> constraint means only the first insert of a given
/// triple can win, and every concurrent or later attempt is rejected — durably and across every
/// gateway instance that shares the database.
/// </summary>
/// <remarks>
/// <para>
/// <b>Why durable + shared matters.</b> Threat-model §1.2 / row D2 notes the legacy gps-ingest
/// replay cache is process-local and non-durable: it forgets its window on restart and is not shared
/// across instances, so a replay accepted by instance B after instance A saw it, or after a pod
/// bounce, slips through. Pushing the dedup set into Postgres closes that gap — the window is the
/// database, not a single process's heap.
/// </para>
/// <para>
/// <b>Atomicity.</b> Each <see cref="Check"/> runs one round-trip: a CTE reads the device's current
/// high-water serial and, in the same statement, attempts the insert with
/// <c>ON CONFLICT DO NOTHING</c>. Three outcomes:
/// </para>
/// <list type="bullet">
///   <item><description>insert suppressed by the unique constraint ⇒
///     <see cref="ReplayOutcome.DuplicateReplay"/>;</description></item>
///   <item><description>insert succeeded but the serial is strictly below the pre-existing
///     high-water mark ⇒ <see cref="ReplayOutcome.OutOfOrder"/>;</description></item>
///   <item><description>insert succeeded at or ahead of the mark ⇒
///     <see cref="ReplayOutcome.Accept"/>.</description></item>
/// </list>
/// <para>
/// <b>Serial semantics.</b> This guard compares serials as plain 64-bit values. If a raw wrapping
/// protocol counter (GT06's 16-bit serial) is fed directly it will read a legitimate wrap as
/// out-of-order; feed it a monotonic ingest sequence, or unwrap the counter before calling, when
/// wrap tolerance is required. The in-memory guard offers a wraparound mode for dev/test.
/// </para>
/// <para>
/// <b>Blocking I/O.</b> <see cref="Check"/> is synchronous to satisfy the interface and performs a
/// synchronous, pooled round-trip; prefer <see cref="CheckAsync"/> from async call sites.
/// Connections are opened per call and returned to Npgsql's pool.
/// </para>
/// </remarks>
public sealed class PostgresReplayGuard : ITelemetryReplayGuard, IDisposable
{
    /// <summary>The table the guard reads and writes. Matches migration 005.</summary>
    public const string TableName = "telemetry_replay_seen";

    private readonly NpgsqlDataSource _dataSource;
    private readonly bool _ownsDataSource;

    // Single-round-trip classify+record. Named CTE `prev` captures the high-water mark BEFORE the
    // insert lands; `ins` performs the idempotent insert. `inserted` distinguishes a fresh row (1)
    // from a suppressed duplicate (0); `prev_max` is NULL for a device's very first frame.
    private const string CheckSql = $@"
WITH prev AS (
    SELECT max(serial) AS max_serial FROM {TableName} WHERE device_id = @device_id
),
ins AS (
    INSERT INTO {TableName} (device_id, serial, content_hash, device_fix_time)
    VALUES (@device_id, @serial, @content_hash, @device_fix_time)
    ON CONFLICT (device_id, serial, content_hash) DO NOTHING
    RETURNING 1
)
SELECT (SELECT count(*) FROM ins)::int AS inserted,
       (SELECT max_serial FROM prev)   AS prev_max;";

    /// <summary>Creates a guard over an existing, caller-owned <see cref="NpgsqlDataSource"/>.</summary>
    /// <param name="dataSource">A configured Npgsql data source. Not disposed by this guard.</param>
    public PostgresReplayGuard(NpgsqlDataSource dataSource)
    {
        _dataSource = dataSource ?? throw new ArgumentNullException(nameof(dataSource));
        _ownsDataSource = false;
    }

    /// <summary>Creates a guard from a connection string, building (and owning) its own data source.</summary>
    /// <param name="connectionString">A Postgres connection string.</param>
    public PostgresReplayGuard(string connectionString)
    {
        if (string.IsNullOrWhiteSpace(connectionString))
            throw new ArgumentException("connectionString must be non-empty.", nameof(connectionString));
        _dataSource = NpgsqlDataSource.Create(connectionString);
        _ownsDataSource = true;
    }

    /// <inheritdoc />
    public ReplayDecision Check(string deviceId, long protocolSerial, string contentHash, DateTime deviceFixTimeUtc)
    {
        ValidateArgs(deviceId, contentHash);

        using var connection = _dataSource.OpenConnection();
        using var command = BuildCommand(connection, deviceId, protocolSerial, contentHash, deviceFixTimeUtc);
        using var reader = command.ExecuteReader(CommandBehavior.SingleRow);
        return Interpret(reader, protocolSerial);
    }

    /// <summary>Asynchronous equivalent of <see cref="Check"/>; prefer this from async call sites.</summary>
    public async Task<ReplayDecision> CheckAsync(
        string deviceId,
        long protocolSerial,
        string contentHash,
        DateTime deviceFixTimeUtc,
        CancellationToken cancellationToken = default)
    {
        ValidateArgs(deviceId, contentHash);

        await using var connection = await _dataSource.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using var command = BuildCommand(connection, deviceId, protocolSerial, contentHash, deviceFixTimeUtc);
        await using var reader = await command
            .ExecuteReaderAsync(CommandBehavior.SingleRow, cancellationToken)
            .ConfigureAwait(false);
        if (!await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            return ReplayDecision.Accept(); // unreachable: the SELECT always yields one row.
        return InterpretRow(reader, protocolSerial);
    }

    private static NpgsqlCommand BuildCommand(
        NpgsqlConnection connection,
        string deviceId,
        long protocolSerial,
        string contentHash,
        DateTime deviceFixTimeUtc)
    {
        var command = new NpgsqlCommand(CheckSql, connection);
        command.Parameters.AddWithValue("device_id", deviceId);
        command.Parameters.AddWithValue("serial", protocolSerial);
        command.Parameters.AddWithValue("content_hash", contentHash);
        object fixTimeParam = deviceFixTimeUtc == default
            ? DBNull.Value
            : DateTime.SpecifyKind(deviceFixTimeUtc, DateTimeKind.Utc);
        command.Parameters.AddWithValue("device_fix_time", fixTimeParam);
        return command;
    }

    private static ReplayDecision Interpret(NpgsqlDataReader reader, long protocolSerial)
    {
        if (!reader.Read())
            return ReplayDecision.Accept(); // unreachable: the SELECT always yields one row.
        return InterpretRow(reader, protocolSerial);
    }

    private static ReplayDecision InterpretRow(NpgsqlDataReader reader, long protocolSerial)
    {
        int inserted = reader.GetInt32(0);
        long? prevMax = reader.IsDBNull(1) ? null : reader.GetInt64(1);

        if (inserted == 0)
            return ReplayDecision.DuplicateReplay();

        if (prevMax is long mark && protocolSerial < mark)
            return ReplayDecision.OutOfOrder(mark);

        return ReplayDecision.Accept();
    }

    private static void ValidateArgs(string deviceId, string contentHash)
    {
        if (string.IsNullOrEmpty(deviceId))
            throw new ArgumentException("deviceId must be non-empty.", nameof(deviceId));
        if (string.IsNullOrEmpty(contentHash))
            throw new ArgumentException("contentHash must be non-empty.", nameof(contentHash));
    }

    /// <summary>Disposes the data source when this guard created it.</summary>
    public void Dispose()
    {
        if (_ownsDataSource)
            _dataSource.Dispose();
    }
}
