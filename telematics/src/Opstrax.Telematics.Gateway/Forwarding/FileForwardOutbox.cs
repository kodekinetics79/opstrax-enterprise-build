using System.Globalization;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Opstrax.Telematics.Gateway.Edge;

namespace Opstrax.Telematics.Gateway.Forwarding;

/// <summary>One payload parked for later delivery.</summary>
/// <param name="Id">Opaque handle used to release the entry once it is resolved.</param>
/// <param name="PayloadJson">The exact body to sign and send — byte-identical to what was normalized.</param>
/// <param name="EnqueuedAtUtc">When the edge parked it, used to enforce the age limit.</param>
internal readonly record struct OutboxEntry(string Id, string PayloadJson, DateTimeOffset EnqueuedAtUtc);

/// <summary>The durable queue that makes an OpsTrax outage a delay instead of data loss.</summary>
internal interface IForwardOutbox
{
    /// <summary>Entries currently parked.</summary>
    int Count { get; }

    /// <summary>Parks a payload. Returns false only when it could not be made durable.</summary>
    Task<bool> EnqueueAsync(string payloadJson, CancellationToken cancellationToken = default);

    /// <summary>Reads up to <paramref name="max"/> parked entries, oldest first.</summary>
    Task<IReadOnlyList<OutboxEntry>> PeekAsync(int max, CancellationToken cancellationToken = default);

    /// <summary>Permanently removes an entry that has been delivered or terminally refused.</summary>
    Task ReleaseAsync(string id, CancellationToken cancellationToken = default);

    /// <summary>Discards entries older than <paramref name="maxAge"/>. Returns how many were dropped.</summary>
    Task<int> PurgeExpiredAsync(TimeSpan maxAge, CancellationToken cancellationToken = default);
}

/// <summary>
/// A crash-safe, file-backed <see cref="IForwardOutbox"/>: one file per parked payload in a
/// directory, named so lexicographic order is enqueue order.
/// </summary>
/// <remarks>
/// <para>
/// <b>Why files and not Postgres.</b> The whole point of the HTTPS edge is that the public box
/// holds no database credentials. A durable queue that needed Neon would put them straight back,
/// and would also be unavailable in exactly the situation the queue exists for — the network path
/// to OpsTrax being down.
/// </para>
/// <para>
/// <b>Crash safety.</b> Each entry is written to a <c>.tmp</c> file, flushed to disk, and then
/// moved into place. <c>File.Move</c> within a directory is atomic on POSIX and NTFS, so a crash
/// mid-write leaves a stray <c>.tmp</c> (ignored and cleaned up on the next sweep) and never a
/// half-parsed entry that would be replayed as a corrupt fix.
/// </para>
/// <para>
/// <b>The ceiling drops the oldest.</b> When the queue is full the eldest entry is discarded to
/// admit the newest. During a multi-hour outage the newest fix is the one that puts a truck in
/// the right place on the map when service returns; the two-hour-old one behind it is history
/// that ages out anyway. The alternative — refusing new fixes — freezes the fleet at the moment
/// the outage began, which reads as "every truck stopped" rather than "we lost contact".
/// </para>
/// <para><b>Thread-safety.</b> Enqueue and release are serialised on one lock; the queue is small
/// and the critical section is a file move.</para>
/// </remarks>
internal sealed class FileForwardOutbox : IForwardOutbox
{
    private const string EntryExtension = ".json";
    private const string PendingExtension = ".tmp";

    private readonly string _directory;
    private readonly int _maxEntries;
    private readonly ILogger _logger;
    private readonly EdgeMetrics _metrics;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly Func<DateTimeOffset> _clock;

    private long _sequence;
    private int _count;

    /// <summary>Creates (and if necessary provisions) the outbox directory.</summary>
    /// <param name="options">Outbox configuration.</param>
    /// <param name="metrics">Receives discard counts.</param>
    /// <param name="logger">Receives durability diagnostics.</param>
    /// <param name="clock">UTC clock seam for tests.</param>
    /// <exception cref="InvalidOperationException">The directory cannot be created or written.</exception>
    public FileForwardOutbox(
        OutboxOptions options,
        EdgeMetrics metrics,
        ILogger logger,
        Func<DateTimeOffset>? clock = null)
    {
        ArgumentNullException.ThrowIfNull(options);
        _metrics = metrics ?? throw new ArgumentNullException(nameof(metrics));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _clock = clock ?? (() => DateTimeOffset.UtcNow);
        _maxEntries = options.MaxEntries > 0
            ? options.MaxEntries
            : throw new ArgumentOutOfRangeException(nameof(options), "Outbox MaxEntries must be positive.");

        _directory = Path.GetFullPath(options.Path);

        try
        {
            Directory.CreateDirectory(_directory);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Fail at startup, not on the first outage. An edge that discovers it has no durable
            // queue only when OpsTrax goes down loses precisely the fixes the queue existed for.
            throw new InvalidOperationException(
                $"Outbox directory '{_directory}' could not be created: {ex.Message}. " +
                "Store-and-forward requires writable persistent storage.", ex);
        }

        _count = CountEntries();
        CleanPending();

        if (_count > 0)
            _logger.LogWarning(
                "Outbox at {Directory} holds {Count} fix(es) parked by a previous run; they will be drained first.",
                _directory, _count);
    }

    /// <inheritdoc />
    public int Count => Volatile.Read(ref _count);

    /// <inheritdoc />
    public async Task<bool> EnqueueAsync(string payloadJson, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(payloadJson);

        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (_count >= _maxEntries)
                DiscardOldest(_count - _maxEntries + 1);

            DateTimeOffset now = _clock();
            string name = EntryName(now, Interlocked.Increment(ref _sequence));
            string finalPath = Path.Combine(_directory, name + EntryExtension);
            string pendingPath = Path.Combine(_directory, name + PendingExtension);

            byte[] bytes = Encoding.UTF8.GetBytes(Render(payloadJson, now));

            // Flush through to the device before the move: a move that lands ahead of the data
            // would leave an empty, valid-looking entry after a power loss.
            await using (var stream = new FileStream(
                pendingPath, FileMode.Create, FileAccess.Write, FileShare.None, bufferSize: 4096, useAsync: true))
            {
                await stream.WriteAsync(bytes, cancellationToken).ConfigureAwait(false);
                await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
                stream.Flush(flushToDisk: true);
            }

            File.Move(pendingPath, finalPath, overwrite: false);
            _count++;
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // The caller must NOT acknowledge the frame after this: an unacknowledged frame is
            // retransmitted by the tracker, which is the only remaining path to not losing it.
            _logger.LogCritical(ex, "Could not park a fix in the outbox at {Directory}; it will not be acknowledged.", _directory);
            return false;
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<OutboxEntry>> PeekAsync(int max, CancellationToken cancellationToken = default)
    {
        if (max <= 0) return Array.Empty<OutboxEntry>();

        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var entries = new List<OutboxEntry>(Math.Min(max, 64));

            foreach (string path in EnumerateOrdered().Take(max))
            {
                cancellationToken.ThrowIfCancellationRequested();

                OutboxEntry? entry = TryRead(path);
                if (entry is { } value) entries.Add(value);
                else DeleteCorrupt(path);
            }

            return entries;
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <inheritdoc />
    public async Task ReleaseAsync(string id, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(id);

        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            Delete(Path.Combine(_directory, id));
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <inheritdoc />
    public async Task<int> PurgeExpiredAsync(TimeSpan maxAge, CancellationToken cancellationToken = default)
    {
        if (maxAge <= TimeSpan.Zero) return 0;

        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            DateTimeOffset cutoff = _clock() - maxAge;
            int purged = 0;

            // Ordered oldest-first, so the first entry inside the window ends the sweep.
            foreach (string path in EnumerateOrdered())
            {
                cancellationToken.ThrowIfCancellationRequested();

                OutboxEntry? entry = TryRead(path);
                if (entry is null) { DeleteCorrupt(path); continue; }
                if (entry.Value.EnqueuedAtUtc > cutoff) break;

                Delete(path);
                purged++;
            }

            if (purged > 0)
            {
                _metrics.AddOutboxEntriesDiscarded(purged);
                _logger.LogWarning(
                    "Discarded {Purged} parked fix(es) older than {MaxAge} without delivering them. " +
                    "OpsTrax refuses fixes older than 30 days, so these could never have been ingested.",
                    purged, maxAge);
            }

            return purged;
        }
        finally
        {
            _gate.Release();
        }
    }

    // ── Storage ────────────────────────────────────────────────────────────────

    /// <summary>
    /// Names an entry so a plain lexicographic sort is chronological order: fixed-width ticks,
    /// then a per-process sequence to break ties within the same tick.
    /// </summary>
    private static string EntryName(DateTimeOffset when, long sequence) =>
        string.Create(CultureInfo.InvariantCulture, $"{when.UtcTicks:D19}-{sequence:D9}");

    private static string Render(string payloadJson, DateTimeOffset enqueuedAt)
    {
        var buffer = new System.Buffers.ArrayBufferWriter<byte>();
        using (var writer = new Utf8JsonWriter(buffer))
        {
            writer.WriteStartObject();
            writer.WriteString("enqueuedAt", enqueuedAt.UtcDateTime.ToString("O", CultureInfo.InvariantCulture));
            // Stored as a JSON string, not embedded JSON: reading it back with GetString() returns
            // the original characters exactly, which is what the HMAC was computed over.
            writer.WriteString("payload", payloadJson);
            writer.WriteEndObject();
        }

        return Encoding.UTF8.GetString(buffer.WrittenSpan);
    }

    private OutboxEntry? TryRead(string path)
    {
        try
        {
            using JsonDocument document = JsonDocument.Parse(File.ReadAllBytes(path));
            JsonElement root = document.RootElement;

            if (root.ValueKind != JsonValueKind.Object ||
                !root.TryGetProperty("payload", out JsonElement payload) ||
                payload.ValueKind != JsonValueKind.String)
                return null;

            string? body = payload.GetString();
            if (string.IsNullOrEmpty(body)) return null;

            DateTimeOffset enqueued =
                root.TryGetProperty("enqueuedAt", out JsonElement at) &&
                at.ValueKind == JsonValueKind.String &&
                DateTimeOffset.TryParse(
                    at.GetString(), CultureInfo.InvariantCulture,
                    DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out DateTimeOffset parsed)
                    ? parsed
                    : _clock();

            return new OutboxEntry(Path.GetFileName(path), body, enqueued);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            return null;
        }
    }

    /// <summary>Entry files, oldest first. Excludes <c>.tmp</c> files, which are incomplete by definition.</summary>
    private IEnumerable<string> EnumerateOrdered()
    {
        string[] files;
        try
        {
            files = Directory.GetFiles(_directory, "*" + EntryExtension);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _logger.LogError(ex, "Could not enumerate the outbox at {Directory}.", _directory);
            return Array.Empty<string>();
        }

        Array.Sort(files, StringComparer.Ordinal);
        return files;
    }

    /// <summary>Drops the <paramref name="count"/> eldest entries to make room. Caller holds the gate.</summary>
    private void DiscardOldest(int count)
    {
        int discarded = 0;
        foreach (string path in EnumerateOrdered().Take(count))
        {
            Delete(path);
            discarded++;
        }

        if (discarded == 0) return;

        _metrics.AddOutboxEntriesDiscarded(discarded);
        _logger.LogError(
            "Outbox at {Directory} reached its {MaxEntries}-entry ceiling; discarded the {Discarded} oldest " +
            "fix(es) undelivered to admit newer ones. OpsTrax has been unreachable long enough to lose data.",
            _directory, _maxEntries, discarded);
    }

    private void Delete(string path)
    {
        try
        {
            if (!File.Exists(path)) return;
            File.Delete(path);
            if (_count > 0) _count--;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _logger.LogWarning(ex, "Could not remove outbox entry {Entry}.", Path.GetFileName(path));
        }
    }

    private void DeleteCorrupt(string path)
    {
        _logger.LogError(
            "Outbox entry {Entry} is unreadable or corrupt and is being discarded; the fix it held is lost.",
            Path.GetFileName(path));
        _metrics.AddOutboxEntriesDiscarded(1);
        Delete(path);
    }

    private int CountEntries()
    {
        try
        {
            return Directory.GetFiles(_directory, "*" + EntryExtension).Length;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _logger.LogError(ex, "Could not count outbox entries at {Directory}; assuming empty.", _directory);
            return 0;
        }
    }

    /// <summary>Removes <c>.tmp</c> files stranded by a crash mid-write. They are, by construction, incomplete.</summary>
    private void CleanPending()
    {
        try
        {
            foreach (string path in Directory.GetFiles(_directory, "*" + PendingExtension))
            {
                File.Delete(path);
                _logger.LogWarning(
                    "Removed a partially-written outbox entry {Entry} left by an unclean shutdown.",
                    Path.GetFileName(path));
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _logger.LogWarning(ex, "Could not clean partial outbox entries at {Directory}.", _directory);
        }
    }
}
