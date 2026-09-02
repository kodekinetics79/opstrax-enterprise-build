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

    /// <summary>
    /// Default ceiling on how many distinct devices may be tracked at once. The per-device window
    /// was already bounded, but the device map itself was not: on a public edge, every forged IMEI
    /// a scanner tries mints a permanent <see cref="DeviceState"/>, so the guard's footprint grew
    /// with attacker-chosen cardinality and never shrank. This bounds the other dimension.
    /// </summary>
    public const int DefaultMaxTrackedDevices = 50_000;

    private readonly ConcurrentDictionary<string, DeviceState> _devices = new(StringComparer.Ordinal);
    private readonly long? _serialModulus;
    private readonly int _maxTrackedDevices;

    /// <summary>Guards <see cref="_deviceRecency"/>; only touched on insert and eviction, never per frame.</summary>
    private readonly object _recencyGate = new();

    /// <summary>Least-recently-active device keys, most recent first. Bounds device cardinality.</summary>
    private readonly LinkedList<string> _deviceRecency = new();
    private readonly Dictionary<string, LinkedListNode<string>> _recencyIndex = new(StringComparer.Ordinal);

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
    /// <param name="maxTrackedDevices">
    /// Maximum number of distinct devices tracked at once. When exceeded, the least-recently-active
    /// device's state is evicted. Eviction is safe for correctness: an evicted device is treated as
    /// new on its next frame and bootstraps a fresh epoch strictly ahead of anything it could have
    /// sent, so a stale frame cannot be resurrected by eviction. Active devices are never evicted
    /// ahead of idle ones. The durable OpsTrax ledger, not this cache, is the authority.
    /// </param>
    public InMemoryReplayGuard(
        int perDeviceWindow = DefaultPerDeviceWindow,
        long? serialModulus = null,
        int maxTrackedDevices = DefaultMaxTrackedDevices)
    {
        if (perDeviceWindow <= 0)
            throw new ArgumentOutOfRangeException(nameof(perDeviceWindow), "Per-device window must be positive.");
        if (serialModulus is <= 1)
            throw new ArgumentOutOfRangeException(nameof(serialModulus), "Serial modulus must be greater than 1.");
        if (maxTrackedDevices <= 0)
            throw new ArgumentOutOfRangeException(nameof(maxTrackedDevices), "Device ceiling must be positive.");

        PerDeviceWindow = perDeviceWindow;
        _serialModulus = serialModulus;
        _maxTrackedDevices = maxTrackedDevices;
    }

    /// <summary>The ceiling on distinct tracked devices (see the constructor).</summary>
    public int MaxTrackedDevices => _maxTrackedDevices;

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

        DeviceState state = GetOrAddDevice(deviceId);

        lock (state.Gate)
        {
            long unwrappedSerial = state.PendingEpochBase is long epochBase
                ? checked(epochBase + protocolSerial)  // first frame of a new authenticated session
                : Unwrap(protocolSerial, state);

            // 1. Replay check. Two distinct questions are being asked here, and collapsing them is
            //    what makes this subtle:
            //
            //    (a) Is this the SAME occurrence — same bytes at the same unwrapped serial? That is
            //        an ordinary retransmission.
            //    (b) Are these bytes ones this device already sent BEFORE a login-declared epoch
            //        boundary? A reboot epoch deliberately re-issues low serials, so the same
            //        captured frame would otherwise get a brand-new unwrapped serial and sail
            //        through. This arm is what stops an epoch from becoming a replay window.
            //
            //    Crucially (b) is scoped to LOGIN-declared boundaries, not to counter wraps. A
            //    device that genuinely emits 65 536 frames and comes back round to the same
            //    heartbeat bytes has done real work, and that repeat is a new occurrence — the
            //    reboot case is the one where no such work happened.
            if (state.TryTouch(contentHash, out DeviceState.SeenEntry existing) &&
                (existing.UnwrappedSerial == unwrappedSerial || existing.EpochGeneration < state.EpochGeneration))
            {
                return ReplayDecision.DuplicateReplay(existing.EventId);
            }

            // 2. Sequence check: a serial strictly behind the high-water mark (that we did not
            //    recognise as a duplicate) is stale/reordered or an evicted replay.
            if (state.HasSerial && unwrappedSerial < state.HighWaterUnwrapped)
            {
                Guid staleEventId = Guid.NewGuid();
                state.Record(contentHash, protocolSerial, unwrappedSerial,
                    deviceFixTimeUtc, advancesHighWater: false, eventId: staleEventId);
                return ReplayDecision.OutOfOrder(state.HighWaterSerial, staleEventId);
            }

            // 3. Accept: remember it (bounded LRU) and advance the high-water mark.
            Guid eventId = Guid.NewGuid();
            state.Record(contentHash, protocolSerial, unwrappedSerial, deviceFixTimeUtc,
                advancesHighWater: !state.HasSerial || unwrappedSerial > state.HighWaterUnwrapped,
                eventId: eventId);
            state.ConsumePendingEpoch();
            return ReplayDecision.Accept(eventId);
        }
    }

    /// <inheritdoc />
    public System.Threading.Tasks.Task BeginSessionEpochAsync(
        string deviceId, System.Threading.CancellationToken cancellationToken = default)
    {
        BeginSessionEpoch(deviceId);
        return System.Threading.Tasks.Task.CompletedTask;
    }

    /// <summary>Synchronous form of <see cref="BeginSessionEpochAsync"/>.</summary>
    /// <param name="deviceId">The device whose next frame opens a new counter generation.</param>
    public void BeginSessionEpoch(string deviceId)
    {
        if (string.IsNullOrEmpty(deviceId))
            throw new ArgumentException("deviceId must be non-empty.", nameof(deviceId));
        if (_serialModulus is null)
            return; // Non-wrapping counters have no generations to advance.

        DeviceState state = GetOrAddDevice(deviceId);
        lock (state.Gate)
        {
            // Only a device that already has history needs moving; a first-ever frame bootstraps
            // its own generation. The seen-window is deliberately NOT cleared: forgetting it is what
            // would let pre-reboot frames replay.
            if (state.HasSerial)
                state.OpenNewEpoch(_serialModulus.Value);
        }
    }

    /// <summary>
    /// Looks up (or creates) a device's state and marks it as the most recently active, evicting the
    /// least recently active device once the ceiling is exceeded.
    /// </summary>
    private DeviceState GetOrAddDevice(string deviceId)
    {
        if (_devices.TryGetValue(deviceId, out DeviceState? existing))
        {
            TouchDevice(deviceId, isNew: false);
            return existing;
        }

        DeviceState state = _devices.GetOrAdd(deviceId, _ => new DeviceState(PerDeviceWindow));
        TouchDevice(deviceId, isNew: true);
        return state;
    }

    private void TouchDevice(string deviceId, bool isNew)
    {
        string? evict = null;

        lock (_recencyGate)
        {
            if (_recencyIndex.TryGetValue(deviceId, out LinkedListNode<string>? node))
            {
                if (!ReferenceEquals(node, _deviceRecency.First))
                {
                    _deviceRecency.Remove(node);
                    _deviceRecency.AddFirst(node);
                }
            }
            else
            {
                _recencyIndex[deviceId] = _deviceRecency.AddFirst(deviceId);
            }

            if (isNew && _recencyIndex.Count > _maxTrackedDevices)
            {
                LinkedListNode<string> oldest = _deviceRecency.Last!;
                _deviceRecency.RemoveLast();
                _recencyIndex.Remove(oldest.Value);
                evict = oldest.Value;
            }
        }

        // Outside the recency lock: dropping the state must not be ordered under it.
        if (evict is not null)
            _devices.TryRemove(evict, out _);
    }

    private long Unwrap(long candidate, DeviceState state)
    {
        if (!state.HasSerial || _serialModulus is null) return candidate;
        return PostgresReplayGuard.Unwrap(
            candidate, state.HighWaterSerial, state.HighWaterUnwrapped, _serialModulus);
    }

    /// <summary>Per-device state: a serial high-water mark plus a bounded LRU dedup set.</summary>
    internal sealed class DeviceState
    {
        public readonly object Gate = new();

        private readonly int _capacity;
        private readonly Dictionary<string, LinkedListNode<SeenEntry>> _index;
        private readonly LinkedList<SeenEntry> _order = new(); // First = most-recently seen.

        public long HighWaterSerial { get; private set; }
        public long HighWaterUnwrapped { get; private set; }
        public bool HasSerial { get; private set; }
        public DateTime LastFixTimeUtc { get; private set; }

        /// <summary>
        /// Set by <see cref="OpenNewEpoch"/> and consumed by the very next frame, which is unwrapped
        /// as <c>PendingEpochBase + rawSerial</c>. Null whenever no epoch change is outstanding.
        /// </summary>
        public long? PendingEpochBase { get; private set; }

        /// <summary>
        /// How many login-declared epoch boundaries this device has crossed. Bumped by
        /// <see cref="OpenNewEpoch"/> only — a counter wrap does NOT bump it, which is exactly what
        /// separates "the device really sent 65 536 frames" from "the device was power-cycled".
        /// </summary>
        public int EpochGeneration { get; private set; }

        /// <summary>Clears the pending epoch once a frame has consumed it.</summary>
        public void ConsumePendingEpoch() => PendingEpochBase = null;

        public DeviceState(int capacity)
        {
            _capacity = capacity;
            _index = new Dictionary<string, LinkedListNode<SeenEntry>>(capacity, StringComparer.Ordinal);
        }

        /// <summary>
        /// If <paramref name="dedupKey"/> is present, moves it to the most-recent position and
        /// returns its recorded sighting; otherwise returns <see langword="false"/>.
        /// </summary>
        public bool TryTouch(string dedupKey, out SeenEntry entry)
        {
            if (!_index.TryGetValue(dedupKey, out var node))
            {
                entry = default;
                return false;
            }
            if (!ReferenceEquals(node, _order.First))
            {
                _order.Remove(node);
                _order.AddFirst(node);
            }
            entry = node.Value;
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
            // Insert as most-recent; evict least-recent if over capacity. Re-recording an existing
            // digest (an accepted repeat in a later counter generation) replaces its entry, so the
            // window always describes the MOST RECENT sighting of those bytes.
            if (_index.TryGetValue(dedupKey, out var stale))
            {
                _order.Remove(stale);
                _index.Remove(dedupKey);
            }

            var node = _order.AddFirst(new SeenEntry(dedupKey, eventId, unwrappedSerial, EpochGeneration));
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

        /// <summary>
        /// Moves the unwrap origin to the start of the next counter generation, so a raw serial that
        /// restarts at 1 after a power cycle maps AHEAD of the pre-reboot high-water mark instead of
        /// thousands of steps behind it.
        /// </summary>
        /// <remarks>
        /// <para>
        /// Only the unwrap origin moves. The high-water mark is raised, never lowered, and the
        /// seen-window is untouched — so the content digest continues to reject anything this device
        /// has already sent, including frames captured before the power cycle.
        /// </para>
        /// <para>
        /// The base is <em>pending</em> rather than applied immediately, and the next frame is
        /// unwrapped as <c>base + rawSerial</c> outright. Nudging the origin and letting the ordinary
        /// nearer-half-range rule sort it out is not equivalent: that rule maps any raw serial past
        /// the half-way point BACKWARDS, so a device that power-cycled at a high serial and returned
        /// at, say, 40 000 would still read as stale. Applying the base directly is forward for every
        /// serial in the counter's range, with no half-range cliff.
        /// </para>
        /// </remarks>
        public void OpenNewEpoch(long modulus)
        {
            PendingEpochBase = checked(((HighWaterUnwrapped / modulus) + 1) * modulus);
            EpochGeneration++;
        }

        /// <summary>One remembered sighting: which bytes, what identity they were given, where in
        /// the durable sequence they landed, and which login epoch they belonged to.</summary>
        public readonly record struct SeenEntry(string DedupKey, Guid EventId, long UnwrappedSerial, int EpochGeneration);
    }
}
