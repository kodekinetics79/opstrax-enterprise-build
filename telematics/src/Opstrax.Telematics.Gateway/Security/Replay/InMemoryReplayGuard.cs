using System.Collections.Concurrent;

namespace Opstrax.Telematics.Gateway.Security.Replay;

/// <summary>
/// A bounded, thread-safe, in-process <see cref="ITelemetryReplayGuard"/> for development, tests
/// and single-instance deployments. It holds a per-device high-water serial plus a bounded LRU set
/// of recently-seen <c>(unwrappedSerial, contentHash)</c> occurrences.
/// </summary>
/// <remarks>
/// <para>
/// <b>Bounding.</b> Each device keeps at most <see cref="PerDeviceWindow"/> dedup entries in an LRU
/// (least-recently-seen evicted first). This is the fix for threat-model row D2 — the legacy
/// gps-ingest cache is bounded only by TTL and can balloon under a distinct-nonce flood; here the
/// memory a single device can pin is a hard constant.
/// </para>
/// <para>
/// <b>Not durable.</b> This guard is process-local: its window is empty again after a restart and
/// is not shared across gateway instances, so on its own it reopens the replay window on
/// restart/scale-out. Use <see cref="PostgresReplayGuard"/> for the durable, shared guarantee; this
/// type exists so the gateway, its tests and the framing loop can exercise the seam without a
/// database.
/// </para>
/// <para>
/// <b>Concurrency.</b> Devices are looked up through a <see cref="ConcurrentDictionary{TKey,TValue}"/>;
/// the check-and-record critical section for a single device is serialised on that device's own
/// lock, so operations on different devices never contend. Because the whole classify-then-record
/// step runs under the lock, two racing duplicates for the same device cannot both be accepted.
/// </para>
/// </remarks>
public sealed class InMemoryReplayGuard : ITelemetryReplayGuard
{
    /// <summary>Default per-device dedup window size when none is supplied.</summary>
    public const int DefaultPerDeviceWindow = 512;

    private readonly ConcurrentDictionary<string, DeviceState> _devices = new(StringComparer.Ordinal);
    private readonly long? _serialModulus;

    /// <summary>Creates a guard.</summary>
    /// <param name="perDeviceWindow">
    /// Maximum number of <c>(unwrappedSerial, contentHash)</c> dedup entries retained per device. Must be
    /// positive. Larger windows tolerate more reordering before an old duplicate is downgraded from
    /// <see cref="ReplayOutcome.DuplicateReplay"/> to <see cref="ReplayOutcome.OutOfOrder"/> (both
    /// still rejected).
    /// </param>
    /// <param name="serialModulus">
    /// When set, serials are compared on a circle of this size so a protocol counter that wraps
    /// (e.g. GT06's 65 536) is handled: a step is "forward" when the circular distance ahead is in
    /// <c>(0, modulus/2)</c>. Exact half is ambiguous and fails closed. When <see langword="null"/>
    /// (default) serials are compared as plain
    /// monotonic 64-bit values.
    /// </param>
    public InMemoryReplayGuard(int perDeviceWindow = DefaultPerDeviceWindow, long? serialModulus = null)
    {
        if (perDeviceWindow <= 0)
            throw new ArgumentOutOfRangeException(nameof(perDeviceWindow), "Per-device window must be positive.");
        if (serialModulus is <= 1)
            throw new ArgumentOutOfRangeException(nameof(serialModulus), "Serial modulus must be greater than 1.");

        PerDeviceWindow = perDeviceWindow;
        _serialModulus = serialModulus;
    }

    /// <summary>The per-device LRU dedup window capacity.</summary>
    public int PerDeviceWindow { get; }

    /// <summary>Number of distinct devices currently tracked. Primarily for tests/metrics.</summary>
    public int TrackedDeviceCount => _devices.Count;

    /// <inheritdoc />
    public System.Threading.Tasks.Task<ReplayDecision> CheckAsync(
        string deviceId, long protocolSerial, string contentHash, DateTime deviceFixTimeUtc,
        System.Threading.CancellationToken cancellationToken = default)
        // Pure in-memory lock work, no I/O — complete synchronously without parking a thread.
        => System.Threading.Tasks.Task.FromResult(Check(deviceId, protocolSerial, contentHash, deviceFixTimeUtc));

    /// <inheritdoc />
    public ReplayDecision Check(string deviceId, long protocolSerial, string contentHash, DateTime deviceFixTimeUtc)
    {
        if (string.IsNullOrEmpty(deviceId))
            throw new ArgumentException("deviceId must be non-empty.", nameof(deviceId));
        if (string.IsNullOrEmpty(contentHash))
            throw new ArgumentException("contentHash must be non-empty.", nameof(contentHash));

        var state = _devices.GetOrAdd(deviceId, _ => new DeviceState(PerDeviceWindow));

        lock (state.Gate)
        {
            long unwrappedSerial = Unwrap(protocolSerial, state);
            string dedupKey = MakeKey(unwrappedSerial, contentHash);
            // 1. Exact-replay check first: the dedup window is authoritative for byte-for-byte
            //    duplicates near the high-water mark. A hit also refreshes LRU recency so a
            //    still-active replay keeps its window slot (true least-recently-SEEN eviction).
            if (state.TryTouch(dedupKey, out Guid existingEventId))
                return ReplayDecision.DuplicateReplay(existingEventId);

            // 2. Sequence check: a serial strictly behind the high-water mark (that we did not
            //    recognise as a duplicate) is stale/reordered or an evicted replay.
            if (state.HasSerial && unwrappedSerial < state.HighWaterUnwrapped)
            {
                Guid staleEventId = Guid.NewGuid();
                state.Record(dedupKey, protocolSerial, unwrappedSerial,
                    deviceFixTimeUtc, advancesHighWater: false, eventId: staleEventId);
                return ReplayDecision.OutOfOrder(state.HighWaterSerial, staleEventId);
            }

            // 3. Accept: remember it (bounded LRU) and advance the high-water mark.
            Guid eventId = Guid.NewGuid();
            state.Record(dedupKey, protocolSerial, unwrappedSerial, deviceFixTimeUtc,
                advancesHighWater: !state.HasSerial || unwrappedSerial > state.HighWaterUnwrapped,
                eventId: eventId);
            return ReplayDecision.Accept(eventId);
        }
    }

    private static string MakeKey(long unwrappedSerial, string contentHash) =>
        // ':' is not a decimal digit, so the serial/hash boundary is unambiguous.
        string.Concat(unwrappedSerial.ToString(System.Globalization.CultureInfo.InvariantCulture), ":", contentHash);

    private long Unwrap(long candidate, DeviceState state)
    {
        if (!state.HasSerial || _serialModulus is null) return candidate;
        return PostgresReplayGuard.Unwrap(
            candidate, state.HighWaterSerial, state.HighWaterUnwrapped, _serialModulus);
    }

    /// <summary>Per-device state: a serial high-water mark plus a bounded LRU dedup set.</summary>
    private sealed class DeviceState
    {
        public readonly object Gate = new();

        private readonly int _capacity;
        private readonly Dictionary<string, LinkedListNode<SeenEntry>> _index;
        private readonly LinkedList<SeenEntry> _order = new(); // First = most-recently seen.

        public long HighWaterSerial { get; private set; }
        public long HighWaterUnwrapped { get; private set; }
        public bool HasSerial { get; private set; }
        public DateTime LastFixTimeUtc { get; private set; }

        public DeviceState(int capacity)
        {
            _capacity = capacity;
            _index = new Dictionary<string, LinkedListNode<SeenEntry>>(capacity, StringComparer.Ordinal);
        }

        /// <summary>
        /// If <paramref name="dedupKey"/> is present, moves it to the most-recent position and
        /// returns <see langword="true"/>; otherwise returns <see langword="false"/>.
        /// </summary>
        public bool TryTouch(string dedupKey, out Guid eventId)
        {
            if (!_index.TryGetValue(dedupKey, out var node))
            {
                eventId = Guid.Empty;
                return false;
            }
            if (!ReferenceEquals(node, _order.First))
            {
                _order.Remove(node);
                _order.AddFirst(node);
            }
            eventId = node.Value.EventId;
            return true;
        }

        public void Record(
            string dedupKey,
            long serial,
            long unwrappedSerial,
            DateTime fixTimeUtc,
            bool advancesHighWater,
            Guid eventId)
        {
            // Insert as most-recent; evict least-recent if over capacity.
            var node = _order.AddFirst(new SeenEntry(dedupKey, eventId));
            _index[dedupKey] = node;

            if (_index.Count > _capacity)
            {
                var oldest = _order.Last!;
                _order.RemoveLast();
                _index.Remove(oldest.Value.DedupKey);
            }

            if (!HasSerial || advancesHighWater)
            {
                HighWaterSerial = serial;
                HighWaterUnwrapped = unwrappedSerial;
                HasSerial = true;
            }

            if (fixTimeUtc > LastFixTimeUtc)
                LastFixTimeUtc = fixTimeUtc;
        }

        private sealed record SeenEntry(string DedupKey, Guid EventId);
    }
}
