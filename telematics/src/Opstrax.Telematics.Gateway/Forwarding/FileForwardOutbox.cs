using System.Globalization;
using System.Security.Cryptography;
using System.Text;
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
/// A crash-safe, <b>encrypted</b>, file-backed <see cref="IForwardOutbox"/>: one file per parked
/// payload in a directory, named so lexicographic order is enqueue order.
/// </summary>
/// <remarks>
/// <para>
/// <b>Why files and not Postgres.</b> The whole point of the HTTPS edge is that the public box
/// holds no database credentials. A durable queue that needed Neon would put them straight back,
/// and would also be unavailable in exactly the situation the queue exists for — the network path
/// to OpsTrax being down.
/// </para>
/// <para>
/// <b>Why encrypted.</b> A parked payload is the identified, timestamped location of a real
/// vehicle, at rest on the disk of an internet-facing host — the machine in the fleet most likely
/// to be compromised, imaged, or discarded carelessly. Every entry is therefore sealed with
/// AES-256-GCM before it touches the disk. On-disk layout, per entry file:
/// <c>formatVersion(1) || keyVersion(1) || nonce(12) || tag(16) || ciphertext</c>, where
/// <c>formatVersion</c> is <c>0x02</c> (0x01 was the retired plaintext-JSON format), the nonce is
/// random per entry, and the GCM associated data binds the two header bytes and the entry's file
/// name — so a renamed, reordered, or header-tampered file fails authentication instead of
/// re-entering the queue with a forged age. The key arrives via
/// <c>Gateway:StoreForwardEncryptionKey</c> (see docs/telematics/security/OUTBOX_KEY_MANAGEMENT.md);
/// the key-version byte identifies which key sealed an entry so a rotation is observable. Entry
/// files are created owner-read/write only (0600) and the directory is held at 0700 on Unix.
/// </para>
/// <para>
/// <b>Crash safety.</b> Each entry is written to a <c>.tmp</c> file, flushed to disk, and then
/// moved into place. <c>File.Move</c> within a directory is atomic on POSIX and NTFS, so a crash
/// mid-write leaves a stray <c>.tmp</c> (ignored and cleaned up on the next sweep) and never a
/// half-parsed entry that would be replayed as a corrupt fix. The enqueue time lives in the file
/// NAME (UTC ticks), not in the payload, so retention needs no cleartext metadata inside the file.
/// </para>
/// <para>
/// <b>Undecryptable means discarded.</b> A wrong key, a truncated file, or tampering all land on
/// the same corrupt-drop path: the entry is counted, logged, and deleted. Failing closed here is
/// deliberate — an outbox that cannot be read is data already lost, and must never block the
/// entries behind it.
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
internal sealed class FileForwardOutbox : IForwardOutbox, IDisposable
{
    private const string EntryExtension = ".enc";
    private const string PendingExtension = ".tmp";

    /// <summary>0x01 was the plaintext JSON format, retired before any deployment existed.</summary>
    private const byte FormatVersion = 0x02;

    private const int NonceLength = 12;
    private const int TagLength = 16;
    private const int HeaderLength = 2 + NonceLength + TagLength;

    private static readonly UnixFileMode OwnerReadWrite = UnixFileMode.UserRead | UnixFileMode.UserWrite;
    private static readonly UnixFileMode OwnerOnlyDirectory = OwnerReadWrite | UnixFileMode.UserExecute;

    private readonly string _directory;
    private readonly int _maxEntries;
    private readonly ILogger _logger;
    private readonly EdgeMetrics _metrics;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly Func<DateTimeOffset> _clock;
    private readonly byte[] _key;
    private readonly AesGcm _aes;
    private readonly byte _keyVersion;

    private long _sequence;
    private int _count;
    private bool _disposed;

    /// <summary>Creates (and if necessary provisions) the outbox directory.</summary>
    /// <param name="options">Outbox configuration.</param>
    /// <param name="metrics">Receives discard counts.</param>
    /// <param name="logger">Receives durability diagnostics.</param>
    /// <param name="encryptionKey">
    /// 32-byte AES-256-GCM key sealing every entry. The outbox keeps a private copy (zeroed on
    /// disposal); the caller remains responsible for zeroing its own.
    /// </param>
    /// <param name="clock">UTC clock seam for tests.</param>
    /// <exception cref="InvalidOperationException">The directory cannot be created or written.</exception>
    /// <exception cref="ArgumentException">The key is not exactly 32 bytes.</exception>
    public FileForwardOutbox(
        OutboxOptions options,
        EdgeMetrics metrics,
        ILogger logger,
        byte[] encryptionKey,
        Func<DateTimeOffset>? clock = null)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(encryptionKey);
        _metrics = metrics ?? throw new ArgumentNullException(nameof(metrics));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _clock = clock ?? (() => DateTimeOffset.UtcNow);
        _maxEntries = options.MaxEntries > 0
            ? options.MaxEntries
            : throw new ArgumentOutOfRangeException(nameof(options), "Outbox MaxEntries must be positive.");

        if (encryptionKey.Length != 32)
            throw new ArgumentException(
                "The outbox encryption key must be exactly 32 bytes (AES-256).", nameof(encryptionKey));
        if (options.EncryptionKeyVersion is < 1 or > 255)
            throw new ArgumentOutOfRangeException(
                nameof(options), "Outbox EncryptionKeyVersion must be between 1 and 255.");

        _key = (byte[])encryptionKey.Clone();
        _aes = new AesGcm(_key, TagLength);
        _keyVersion = (byte)options.EncryptionKeyVersion;

        _directory = Path.GetFullPath(options.Path);

        try
        {
            if (OperatingSystem.IsWindows())
            {
                Directory.CreateDirectory(_directory);
            }
            else
            {
                // Owner-only on creation, and re-asserted on an existing directory: parked fixes
                // are location data and the service account is the only principal with business here.
                Directory.CreateDirectory(_directory, OwnerOnlyDirectory);
                File.SetUnixFileMode(_directory, OwnerOnlyDirectory);
            }
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

            byte[] bytes = Seal(payloadJson, name);

            var streamOptions = new FileStreamOptions
            {
                Mode = FileMode.Create,
                Access = FileAccess.Write,
                Share = FileShare.None,
                BufferSize = 4096,
                Options = FileOptions.Asynchronous,
            };
            if (!OperatingSystem.IsWindows())
                streamOptions.UnixCreateMode = OwnerReadWrite; // 0600, regardless of umask

            // Flush through to the device before the move: a move that lands ahead of the data
            // would leave an empty, valid-looking entry after a power loss.
            await using (var stream = new FileStream(pendingPath, streamOptions))
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

            // Age comes from the file NAME (enqueue ticks), so expiry needs no decryption.
            // Ordered oldest-first, so the first entry inside the window ends the sweep.
            foreach (string path in EnumerateOrdered())
            {
                cancellationToken.ThrowIfCancellationRequested();

                if (!TryParseEnqueuedAt(Path.GetFileNameWithoutExtension(path), out DateTimeOffset enqueued))
                {
                    DeleteCorrupt(path);
                    continue;
                }

                if (enqueued > cutoff) break;

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

    /// <summary>Zeroes the private key copy and releases the cipher.</summary>
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        _aes.Dispose();
        CryptographicOperations.ZeroMemory(_key);
        _gate.Dispose();
    }

    // ── Storage ────────────────────────────────────────────────────────────────

    /// <summary>
    /// Names an entry so a plain lexicographic sort is chronological order: fixed-width ticks,
    /// then a per-process sequence to break ties within the same tick. The ticks are also the
    /// entry's authoritative enqueue time — deliberately outside the encrypted body, because
    /// retention must work without decrypting, and deliberately bound into the GCM associated
    /// data so renaming a file to dodge expiry fails authentication instead.
    /// </summary>
    private static string EntryName(DateTimeOffset when, long sequence) =>
        string.Create(CultureInfo.InvariantCulture, $"{when.UtcTicks:D19}-{sequence:D9}");

    /// <summary>Recovers the enqueue time from an entry's file name (without extension).</summary>
    private static bool TryParseEnqueuedAt(string name, out DateTimeOffset enqueuedAt)
    {
        enqueuedAt = default;

        int dash = name.IndexOf('-', StringComparison.Ordinal);
        if (dash != 19) return false;

        if (!long.TryParse(name.AsSpan(0, dash), NumberStyles.None, CultureInfo.InvariantCulture, out long ticks))
            return false;
        if (ticks < DateTimeOffset.MinValue.Ticks || ticks > DateTimeOffset.MaxValue.Ticks)
            return false;

        enqueuedAt = new DateTimeOffset(ticks, TimeSpan.Zero);
        return true;
    }

    /// <summary>
    /// Seals a payload: <c>formatVersion || keyVersion || nonce || tag || ciphertext</c>, with
    /// the header bytes and entry name authenticated as associated data.
    /// </summary>
    private byte[] Seal(string payloadJson, string entryName)
    {
        byte[] plaintext = Encoding.UTF8.GetBytes(payloadJson);
        byte[] sealed_ = new byte[HeaderLength + plaintext.Length];

        sealed_[0] = FormatVersion;
        sealed_[1] = _keyVersion;

        Span<byte> nonce = sealed_.AsSpan(2, NonceLength);
        RandomNumberGenerator.Fill(nonce);

        _aes.Encrypt(
            nonce,
            plaintext,
            sealed_.AsSpan(HeaderLength),
            sealed_.AsSpan(2 + NonceLength, TagLength),
            AssociatedData(entryName, _keyVersion));

        CryptographicOperations.ZeroMemory(plaintext);
        return sealed_;
    }

    /// <summary>
    /// Opens an entry. Returns null for anything unreadable: wrong key, tampering, truncation,
    /// or a foreign file. The caller routes null to the corrupt-drop path.
    /// </summary>
    private OutboxEntry? TryRead(string path)
    {
        try
        {
            string name = Path.GetFileNameWithoutExtension(path);
            if (!TryParseEnqueuedAt(name, out DateTimeOffset enqueued)) return null;

            byte[] bytes = File.ReadAllBytes(path);
            if (bytes.Length < HeaderLength) return null;
            if (bytes[0] != FormatVersion) return null;

            byte[] plaintext = new byte[bytes.Length - HeaderLength];
            _aes.Decrypt(
                bytes.AsSpan(2, NonceLength),
                bytes.AsSpan(HeaderLength),
                bytes.AsSpan(2 + NonceLength, TagLength),
                plaintext,
                AssociatedData(name, bytes[1]));

            string body = Encoding.UTF8.GetString(plaintext);
            CryptographicOperations.ZeroMemory(plaintext);
            if (body.Length == 0) return null;

            return new OutboxEntry(Path.GetFileName(path), body, enqueued);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or CryptographicException)
        {
            // AuthenticationTagMismatchException (wrong key / tampering) derives from
            // CryptographicException: both are indistinguishable from corruption by design.
            return null;
        }
    }

    /// <summary>
    /// The associated data every entry is authenticated against: the header bytes plus the entry
    /// name, so header tampering and file renames both fail the tag check.
    /// </summary>
    private static byte[] AssociatedData(string entryName, byte keyVersion)
    {
        byte[] aad = new byte[2 + Encoding.UTF8.GetByteCount(entryName)];
        aad[0] = FormatVersion;
        aad[1] = keyVersion;
        Encoding.UTF8.GetBytes(entryName, aad.AsSpan(2));
        return aad;
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
            "Outbox entry {Entry} is unreadable, undecryptable, or corrupt and is being discarded; " +
            "the fix it held is lost.",
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
