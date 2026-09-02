using System.Data;
using Npgsql;
using NpgsqlTypes;

namespace Opstrax.Telematics.Gateway.Security.Replay;

/// <summary>
/// A durable, shared <see cref="ITelemetryReplayGuard"/> backed by the
/// <c>telemetry_replay_seen</c> table (see migration
/// <c>database/migrations/telematics/005_replay_guard.sql</c>). Its replay guarantee is the same
/// atomic primitive the strong ingest path uses for <c>telemetry_nonces</c>. A per-device locked
/// high-water row unwraps wrapping counters into a durable monotonic sequence; the immutable seen
/// ledger is unique on <c>(device_id,unwrapped_serial,content_hash)</c> and stores the stable event
/// UUID returned to every immediate retry.
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
/// <b>Atomicity.</b> Each <see cref="Check"/> runs a transaction under a per-device advisory lock,
/// locks/advances the durable unwrap state only for forward frames, and inserts the immutable
/// occurrence identity. Three outcomes:
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
/// <b>Serial semantics.</b> With a modulus, the nearer half-range is forward, the farther half is
/// behind, and exactly half is ambiguous/fail-closed. State survives restart and scale-out. A
/// legacy device with no state bootstraps into a fresh epoch strictly above its legacy rows, since
/// the predecessor's raw-key uniqueness made historical chronology incomplete.
/// </para>
/// <para>
/// <b>Blocking I/O.</b> <see cref="Check"/> is synchronous to satisfy the interface and performs a
/// synchronous, pooled transaction; prefer <see cref="CheckAsync"/> from async call sites.
/// Connections are opened per call and returned to Npgsql's pool.
/// </para>
/// </remarks>
public sealed class PostgresReplayGuard : ITelemetryReplayGuard, IDisposable
{
    /// <summary>The table the guard reads and writes. Matches migration 005.</summary>
    public const string TableName = "telemetry_replay_seen";

    private readonly NpgsqlDataSource _dataSource;
    private readonly bool _ownsDataSource;
    private readonly long? _serialModulus;

    /// <summary>Creates a guard over an existing, caller-owned <see cref="NpgsqlDataSource"/>.</summary>
    /// <param name="dataSource">A configured Npgsql data source. Not disposed by this guard.</param>
    public PostgresReplayGuard(NpgsqlDataSource dataSource, long? serialModulus = null)
    {
        _dataSource = dataSource ?? throw new ArgumentNullException(nameof(dataSource));
        _ownsDataSource = false;
        _serialModulus = ValidateModulus(serialModulus);
    }

    /// <summary>Creates a guard from a connection string, building (and owning) its own data source.</summary>
    /// <param name="connectionString">A Postgres connection string.</param>
    public PostgresReplayGuard(string connectionString, long? serialModulus = null)
    {
        if (string.IsNullOrWhiteSpace(connectionString))
            throw new ArgumentException("connectionString must be non-empty.", nameof(connectionString));
        _dataSource = NpgsqlDataSource.Create(connectionString);
        _ownsDataSource = true;
        _serialModulus = ValidateModulus(serialModulus);
    }

    /// <inheritdoc />
    public ReplayDecision Check(string deviceId, long protocolSerial, string contentHash, DateTime deviceFixTimeUtc)
    {
        ValidateArgs(deviceId, contentHash);

        return CheckCoreAsync(deviceId, protocolSerial, contentHash, deviceFixTimeUtc, CancellationToken.None)
            .GetAwaiter().GetResult();
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

        return await CheckCoreAsync(deviceId, protocolSerial, contentHash, deviceFixTimeUtc, cancellationToken)
            .ConfigureAwait(false);
    }

    private async Task<ReplayDecision> CheckCoreAsync(
        string deviceId,
        long protocolSerial,
        string contentHash,
        DateTime deviceFixTimeUtc,
        CancellationToken cancellationToken)
    {
        ValidateArgs(deviceId, contentHash);
        if (_serialModulus is long modulus && (protocolSerial < 0 || protocolSerial >= modulus))
            throw new ArgumentOutOfRangeException(nameof(protocolSerial),
                "A modular protocol serial must be inside its configured counter range.");

        await using var connection = await _dataSource.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);

        // The state row may not exist for a first frame, so use a per-device advisory transaction
        // lock as the creation/update mutex. Every gateway instance converges on one unwrap epoch.
        await using (var deviceLock = new NpgsqlCommand(
            "SELECT pg_advisory_xact_lock(hashtextextended(@device_id,0))", connection, transaction))
        {
            deviceLock.Parameters.AddWithValue("device_id", deviceId);
            await deviceLock.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }

        DeviceHighWater? state = null;
        await using (var stateCommand = new NpgsqlCommand(
            "SELECT last_raw_serial,high_water_unwrapped,pending_epoch_base,epoch_floor FROM telemetry_replay_device_state WHERE device_id=@device_id FOR UPDATE",
            connection, transaction))
        {
            stateCommand.Parameters.AddWithValue("device_id", deviceId);
            await using var stateReader = await stateCommand.ExecuteReaderAsync(
                CommandBehavior.SingleRow, cancellationToken).ConfigureAwait(false);
            if (await stateReader.ReadAsync(cancellationToken).ConfigureAwait(false))
                state = new DeviceHighWater(
                    stateReader.GetInt64(0),
                    stateReader.GetInt64(1),
                    stateReader.IsDBNull(2) ? null : stateReader.GetInt64(2),
                    stateReader.IsDBNull(3) ? 0L : stateReader.GetInt64(3));
        }

        long unwrappedSerial;
        if (state is null)
        {
            await using var legacyHigh = new NpgsqlCommand(
                $"SELECT MAX(unwrapped_serial) FROM {TableName} WHERE device_id=@device_id",
                connection, transaction);
            legacyHigh.Parameters.AddWithValue("device_id", deviceId);
            object? highResult = await legacyHigh.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);
            long? maxExisting = highResult is null or DBNull ? null : Convert.ToInt64(highResult);
            unwrappedSerial = BootstrapUnwrapped(protocolSerial, maxExisting, _serialModulus);
        }
        else if (state.PendingEpochBase is long epochBase)
        {
            // A successful login declared that this device may legitimately have restarted its
            // counter. Apply the generation base directly: base + raw is strictly ahead of the
            // previous high-water mark for EVERY serial in the counter's range, whereas nudging the
            // unwrap origin and re-using the nearer-half rule would still push a high raw serial
            // backwards. Applied once; the row's pending base is cleared below.
            unwrappedSerial = checked(epochBase + protocolSerial);
        }
        else
        {
            unwrappedSerial = Unwrap(
                protocolSerial, state.LastRawSerial, state.HighWaterUnwrapped, _serialModulus);
        }

        // ── Cross-epoch replay defence. ─────────────────────────────────────────────
        // A login-declared epoch deliberately re-issues low serials, so the same captured frame
        // presented after a power cycle would receive a BRAND-NEW unwrapped serial and slide past
        // the UNIQUE (device_id,unwrapped_serial,content_hash) key untouched. That is the hole the
        // epoch mechanism would otherwise open, and this closes it: a digest this device already
        // produced BELOW the current epoch floor is a replay, whatever serial it now claims.
        //
        // Deliberately scoped to the epoch floor rather than to the device's whole history. A
        // natural counter WRAP is real forward progress — the device genuinely emitted 65 536
        // frames — so identical bytes recurring after one are a new occurrence, not a replay, and
        // the floor only moves when an authenticated login says a reset may have happened.
        if (state is { EpochFloor: > 0 } floored)
        {
            await using var priorEpoch = new NpgsqlCommand(
                $"SELECT event_id FROM {TableName} WHERE device_id=@device_id AND content_hash=@content_hash AND unwrapped_serial < @floor LIMIT 1",
                connection, transaction);
            priorEpoch.Parameters.AddWithValue("device_id", deviceId);
            priorEpoch.Parameters.AddWithValue("content_hash", contentHash);
            priorEpoch.Parameters.AddWithValue("floor", floored.EpochFloor);
            object? priorEvent = await priorEpoch.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);
            if (priorEvent is Guid priorEventId)
            {
                await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
                return ReplayDecision.DuplicateReplay(priorEventId);
            }
        }

        long? previousHighWater = state?.HighWaterUnwrapped;
        long? previousRawSerial = state?.LastRawSerial;

        if (state is null)
        {
            await using var createState = new NpgsqlCommand(
                "INSERT INTO telemetry_replay_device_state(device_id,last_raw_serial,high_water_unwrapped,updated_at) VALUES(@device_id,@serial,@unwrapped,NOW())",
                connection, transaction);
            AddStateParameters(createState, deviceId, protocolSerial, unwrappedSerial);
            await createState.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }
        else if (unwrappedSerial > state.HighWaterUnwrapped)
        {
            await using var advanceState = new NpgsqlCommand(
                "UPDATE telemetry_replay_device_state SET last_raw_serial=@serial,high_water_unwrapped=@unwrapped,pending_epoch_base=NULL,updated_at=NOW() WHERE device_id=@device_id",
                connection, transaction);
            AddStateParameters(advanceState, deviceId, protocolSerial, unwrappedSerial);
            await advanceState.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }
        else if (state.PendingEpochBase is not null)
        {
            // The epoch was consumed by a frame that did not advance the mark. Clear it anyway so a
            // single login can only ever open ONE generation, no matter how the first frame lands.
            await using var clearEpoch = new NpgsqlCommand(
                "UPDATE telemetry_replay_device_state SET pending_epoch_base=NULL,updated_at=NOW() WHERE device_id=@device_id",
                connection, transaction);
            clearEpoch.Parameters.AddWithValue("device_id", deviceId);
            await clearEpoch.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }

        Guid candidateEventId = Guid.NewGuid();
        Guid eventId;
        bool inserted;
        await using (var insert = new NpgsqlCommand(
            $"""
            INSERT INTO {TableName}
                (device_id,serial,unwrapped_serial,content_hash,event_id,device_fix_time)
            VALUES (@device_id,@serial,@unwrapped,@content_hash,@event_id,@device_fix_time)
            ON CONFLICT (device_id,unwrapped_serial,content_hash) DO NOTHING
            RETURNING event_id
            """, connection, transaction))
        {
            insert.Parameters.AddWithValue("device_id", deviceId);
            insert.Parameters.AddWithValue("serial", protocolSerial);
            insert.Parameters.AddWithValue("unwrapped", unwrappedSerial);
            insert.Parameters.AddWithValue("content_hash", contentHash);
            insert.Parameters.AddWithValue("event_id", candidateEventId);
            insert.Parameters.Add("device_fix_time", NpgsqlDbType.TimestampTz).Value =
                deviceFixTimeUtc == default
                    ? DBNull.Value
                    : DateTime.SpecifyKind(deviceFixTimeUtc, DateTimeKind.Utc);
            object? result = await insert.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);
            inserted = result is Guid;
            eventId = result is Guid created ? created : Guid.Empty;
        }

        if (!inserted)
        {
            await using var existing = new NpgsqlCommand(
                $"SELECT event_id FROM {TableName} WHERE device_id=@device_id AND unwrapped_serial=@unwrapped AND content_hash=@content_hash",
                connection, transaction);
            existing.Parameters.AddWithValue("device_id", deviceId);
            existing.Parameters.AddWithValue("unwrapped", unwrappedSerial);
            existing.Parameters.AddWithValue("content_hash", contentHash);
            eventId = (Guid)(await existing.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false)
                ?? throw new InvalidOperationException("Replay identity conflict did not resolve to a durable event id."));
        }

        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);

        if (!inserted) return ReplayDecision.DuplicateReplay(eventId);
        if (previousHighWater is long high && unwrappedSerial < high)
            return ReplayDecision.OutOfOrder(previousRawSerial ?? protocolSerial, eventId);
        return ReplayDecision.Accept(eventId);
    }

    /// <inheritdoc />
    public async Task BeginSessionEpochAsync(string deviceId, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(deviceId))
            throw new ArgumentException("deviceId must be non-empty.", nameof(deviceId));
        if (_serialModulus is not long modulus)
            return; // A non-wrapping counter has no generations to advance.

        await using var connection = await _dataSource.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);

        // Same advisory lock the frame path takes, so an epoch stamp and an in-flight frame for the
        // same device are serialised against each other rather than interleaving.
        await using (var deviceLock = new NpgsqlCommand(
            "SELECT pg_advisory_xact_lock(hashtextextended(@device_id,0))", connection, transaction))
        {
            deviceLock.Parameters.AddWithValue("device_id", deviceId);
            await deviceLock.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }

        // Only a device with existing state needs moving: a device with none bootstraps its own
        // generation on its first frame. Note this UPDATE touches ONLY pending_epoch_base — the
        // high-water mark is not lowered and not one row of the seen ledger is removed, so replay
        // history survives the reboot intact.
        await using (var stamp = new NpgsqlCommand(
            "UPDATE telemetry_replay_device_state "
            + "SET pending_epoch_base=((high_water_unwrapped / @modulus) + 1) * @modulus, "
            + "    epoch_floor=((high_water_unwrapped / @modulus) + 1) * @modulus, "
            + "    updated_at=NOW() "
            + "WHERE device_id=@device_id",
            connection, transaction))
        {
            stamp.Parameters.AddWithValue("device_id", deviceId);
            stamp.Parameters.AddWithValue("modulus", modulus);
            await stamp.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }

        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
    }

    private static void AddStateParameters(
        NpgsqlCommand command,
        string deviceId,
        long rawSerial,
        long unwrappedSerial)
    {
        command.Parameters.AddWithValue("device_id", deviceId);
        command.Parameters.AddWithValue("serial", rawSerial);
        command.Parameters.AddWithValue("unwrapped", unwrappedSerial);
    }

    internal static long Unwrap(long candidate, long lastRaw, long highWater, long? modulus)
    {
        // Preserve the bootstrap offset even for a non-wrapping counter. Normally raw and
        // unwrapped values are equal; after a legacy cutover the first frame may deliberately
        // start above incomplete historical rows.
        if (modulus is not long size) return checked(highWater + (candidate - lastRaw));
        long forward = ((candidate - lastRaw) % size + size) % size;
        if (forward == 0) return highWater;
        // Exactly half a counter range is directionally ambiguous. Fail closed by mapping it
        // behind the high-water mark; only a strictly nearer forward distance may advance state.
        return forward <= (size - 1) / 2
            ? checked(highWater + forward)
            : checked(highWater - (size - forward));
    }

    internal static long BootstrapUnwrapped(long candidate, long? maxExisting, long? modulus)
    {
        if (maxExisting is null) return candidate;
        if (modulus is not long size) return checked(maxExisting.Value + 1);
        long nextEpoch = checked(((maxExisting.Value / size) + 1) * size);
        return checked(nextEpoch + candidate);
    }

    private static long? ValidateModulus(long? modulus)
    {
        if (modulus is <= 1)
            throw new ArgumentOutOfRangeException(nameof(modulus), "Serial modulus must be greater than one.");
        return modulus;
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

    private sealed record DeviceHighWater(long LastRawSerial, long HighWaterUnwrapped, long? PendingEpochBase, long EpochFloor);
}
