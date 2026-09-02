using System.Collections.Concurrent;

namespace Opstrax.Telematics.Gateway.Quality;

/// <summary>
/// Answers one question per fix: <em>could this vehicle physically have got here from where it
/// last was, in the time that has passed?</em>
/// </summary>
/// <remarks>
/// <para>
/// <b>Why this exists.</b> <see cref="Contracts.Quality.QualityFlags.TeleportSuspected"/> and
/// <see cref="Contracts.Quality.QualityFlags.ImpossibleSpeed"/> have been part of the canonical
/// event contract since it was written, and the projection has been serialising both into every
/// persisted row. Nothing ever set them, so every event ever stored claims a clean bill of health
/// it was never actually given. This is the missing producer.
/// </para>
/// <para>
/// <b>What a range check cannot do.</b> The normalizer already rejects coordinates outside WGS-84
/// and the null-island sentinel. That catches garbage, but it cannot catch a fix that is
/// well-formed and wrong — a mirrored hemisphere, a transposed lat/lng, a spoofed position — because
/// every one of those is still a perfectly valid coordinate. Only continuity catches those: the
/// previous fix is the only evidence that says where this vehicle actually is.
/// </para>
/// <para>
/// <b>What this catches, stated honestly.</b> It catches a fix that jumps: GPS spoofing, a decoder
/// regression shipped mid-flight (every device on the fleet jumps at once, which is the signature
/// worth alerting on), a device swapped between vehicles, a wiring or provisioning error. It does
/// <b>not</b> catch a decoder that has been uniformly wrong since birth, because consecutive
/// equally-wrong fixes sit next to each other and imply an ordinary speed. Nothing derived from
/// continuity alone can catch that; it takes a comparison against known ground truth, which is a
/// commissioning-time check, not a runtime one.
/// </para>
/// <para>
/// <b>Flag, never drop.</b> A plausibility heuristic is not permitted to destroy evidence. A
/// vehicle really can be craned onto a ship, and a fleet really does have devices in a truck being
/// towed. The verdict is advisory: it is published on the event, counted, and left for downstream
/// trust scoring to weigh. Discarding a fix on suspicion would make this class a data-loss
/// mechanism dressed as a safety control.
/// </para>
/// <para><b>Thread-safety.</b> Safe for concurrent calls across devices and for the same device.</para>
/// </remarks>
public sealed class FixPlausibilityGuard
{
    /// <summary>
    /// Default ground-speed ceiling, matching the ingest endpoint's own 250 mph bound so the edge
    /// and the API agree on what "impossible" means.
    /// </summary>
    public const double DefaultMaxGroundSpeedKph = 250.0 / 0.621371;

    /// <summary>
    /// Displacements below this are not evaluated at all.
    /// </summary>
    /// <remarks>
    /// Consumer GNSS scatters by tens of metres while stationary, and the implied speed of a small
    /// scatter over a short interval is enormous: 50 m of jitter one second apart implies 180 km/h.
    /// Without a floor, a parked truck would raise a teleport alert on every heartbeat, and the
    /// signal would be trained out of existence within a day.
    /// </remarks>
    public const double DefaultNoiseFloorMetres = 250.0;

    /// <summary>Default ceiling on tracked devices, bounding the guard against forged-identifier floods.</summary>
    public const int DefaultMaxTrackedDevices = 50_000;

    private const double EarthRadiusMetres = 6_371_008.8; // IUGG mean radius.

    private readonly ConcurrentDictionary<string, LastFix> _lastFix = new(StringComparer.Ordinal);
    private readonly object _recencyGate = new();
    private readonly LinkedList<string> _recency = new();
    private readonly Dictionary<string, LinkedListNode<string>> _recencyIndex = new(StringComparer.Ordinal);

    /// <summary>
    /// Devices flagged for an impossible displacement, and when. Bounded by the same device
    /// ceiling as the baseline map.
    /// </summary>
    private readonly ConcurrentDictionary<string, DateTime> _recentlyFlagged = new(StringComparer.Ordinal);

    private readonly double _maxGroundSpeedKph;
    private readonly double _noiseFloorMetres;
    private readonly int _maxTrackedDevices;

    /// <summary>Creates a guard.</summary>
    /// <param name="maxGroundSpeedKph">Implied speed above which a displacement is impossible. Must be positive.</param>
    /// <param name="noiseFloorMetres">Displacements below this are treated as GNSS scatter and not evaluated.</param>
    /// <param name="maxTrackedDevices">Ceiling on tracked devices; least-recently-seen is evicted first.</param>
    public FixPlausibilityGuard(
        double maxGroundSpeedKph = DefaultMaxGroundSpeedKph,
        double noiseFloorMetres = DefaultNoiseFloorMetres,
        int maxTrackedDevices = DefaultMaxTrackedDevices)
    {
        if (maxGroundSpeedKph <= 0)
            throw new ArgumentOutOfRangeException(nameof(maxGroundSpeedKph), "Speed ceiling must be positive.");
        if (noiseFloorMetres < 0)
            throw new ArgumentOutOfRangeException(nameof(noiseFloorMetres), "Noise floor cannot be negative.");
        if (maxTrackedDevices <= 0)
            throw new ArgumentOutOfRangeException(nameof(maxTrackedDevices), "Device ceiling must be positive.");

        _maxGroundSpeedKph = maxGroundSpeedKph;
        _noiseFloorMetres = noiseFloorMetres;
        _maxTrackedDevices = maxTrackedDevices;
    }

    /// <summary>The implied-speed ceiling in km/h.</summary>
    public double MaxGroundSpeedKph => _maxGroundSpeedKph;

    /// <summary>Distinct devices currently tracked. For tests and health output.</summary>
    public int TrackedDeviceCount => _lastFix.Count;

    /// <summary>
    /// How many <em>distinct devices</em> have been flagged for an impossible displacement within
    /// <paramref name="window"/>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The shape of a teleport spike is its diagnosis, and a plain counter cannot express the
    /// difference. One device flagging repeatedly is tampering, a swapped unit, or a failing GNSS
    /// module: investigate that vehicle. Many devices flagging at once is not a fleet of teleporting
    /// trucks, it is a decoder or coordinate-handling regression that just shipped, and it wants a
    /// rollback rather than an investigation.
    /// </para>
    /// <para>
    /// Counting distinct devices is deliberately done here rather than by adding a
    /// <c>device_id</c> metric label. That label would be unbounded — an attacker choosing IMEIs
    /// controls how many Prometheus series exist — and every other label in this system is bounded
    /// on purpose. Aggregating in-process keeps the cardinality at one.
    /// </para>
    /// </remarks>
    /// <param name="window">How far back to look.</param>
    /// <param name="nowUtc">The current time; passed in so the value is testable.</param>
    public int DistinctDevicesFlaggedWithin(TimeSpan window, DateTime nowUtc)
    {
        DateTime cutoff = nowUtc - window;
        int count = 0;

        foreach (KeyValuePair<string, DateTime> entry in _recentlyFlagged)
        {
            if (entry.Value >= cutoff)
                count++;
            else
                _recentlyFlagged.TryRemove(entry.Key, out _); // Aged out; reclaim as we go.
        }

        return count;
    }

    /// <summary>
    /// Evaluates one fix against this device's previous one and records it as the new baseline.
    /// </summary>
    /// <param name="deviceId">Partition key — the resolved device id where available, otherwise the admitted claim.</param>
    /// <param name="latitude">Decoded latitude in decimal degrees.</param>
    /// <param name="longitude">Decoded longitude in decimal degrees.</param>
    /// <param name="fixTimeUtc">Device-stamped fix time. The gateway receive time is not a substitute.</param>
    /// <param name="reportedSpeedKph">Speed the device claimed, when it reported one.</param>
    /// <returns>The verdict for this fix. Advisory: the caller flags and counts, and never drops.</returns>
    public PlausibilityVerdict Evaluate(
        string deviceId,
        double latitude,
        double longitude,
        DateTime fixTimeUtc,
        double? reportedSpeedKph = null)
    {
        ArgumentException.ThrowIfNullOrEmpty(deviceId);

        bool impossibleSpeed = reportedSpeedKph is { } reported
            && !double.IsNaN(reported)
            && reported > _maxGroundSpeedKph;

        if (double.IsNaN(latitude) || double.IsNaN(longitude))
            return new PlausibilityVerdict(false, impossibleSpeed, null, null);

        Touch(deviceId);
        var candidate = new LastFix(latitude, longitude, fixTimeUtc);

        bool teleport = false;
        double? impliedKph = null;
        double? distanceMetres = null;

        // The baseline is replaced ONLY by a fix that moves time forward. An out-of-order or
        // same-second frame is evaluated against the baseline but never becomes it, so a delayed
        // packet cannot rewrite this device's idea of where it is.
        _lastFix.AddOrUpdate(
            deviceId,
            candidate,
            (_, previous) =>
            {
                double elapsedSeconds = (fixTimeUtc - previous.FixTimeUtc).TotalSeconds;
                if (elapsedSeconds <= 0)
                    return previous; // Out of order; flagged elsewhere, and not our baseline.

                double metres = DistanceMetres(previous.Latitude, previous.Longitude, latitude, longitude);
                distanceMetres = metres;

                if (metres >= _noiseFloorMetres)
                {
                    double kph = metres / 1000.0 / (elapsedSeconds / 3600.0);
                    impliedKph = kph;
                    teleport = kph > _maxGroundSpeedKph;
                }

                // The flagged fix still becomes the baseline. Refusing to advance would pin the
                // device to a position it has left and flag every subsequent fix forever — one bad
                // reading would become a permanent alert storm rather than a single incident.
                return candidate;
            });

        if (teleport)
            _recentlyFlagged[deviceId] = DateTime.UtcNow;

        return new PlausibilityVerdict(teleport, impossibleSpeed, impliedKph, distanceMetres);
    }

    /// <summary>Great-circle distance in metres between two WGS-84 points (haversine).</summary>
    internal static double DistanceMetres(double lat1, double lon1, double lat2, double lon2)
    {
        double phi1 = lat1 * Math.PI / 180.0;
        double phi2 = lat2 * Math.PI / 180.0;
        double deltaPhi = (lat2 - lat1) * Math.PI / 180.0;
        double deltaLambda = (lon2 - lon1) * Math.PI / 180.0;

        double a = Math.Sin(deltaPhi / 2) * Math.Sin(deltaPhi / 2)
                 + Math.Cos(phi1) * Math.Cos(phi2) * Math.Sin(deltaLambda / 2) * Math.Sin(deltaLambda / 2);

        return 2 * EarthRadiusMetres * Math.Asin(Math.Min(1.0, Math.Sqrt(a)));
    }

    /// <summary>Marks a device most-recently-seen, evicting the least-recently-seen past the ceiling.</summary>
    private void Touch(string deviceId)
    {
        string? evict = null;

        lock (_recencyGate)
        {
            if (_recencyIndex.TryGetValue(deviceId, out LinkedListNode<string>? node))
            {
                if (!ReferenceEquals(node, _recency.First))
                {
                    _recency.Remove(node);
                    _recency.AddFirst(node);
                }
            }
            else
            {
                _recencyIndex[deviceId] = _recency.AddFirst(deviceId);

                if (_recencyIndex.Count > _maxTrackedDevices)
                {
                    LinkedListNode<string> oldest = _recency.Last!;
                    _recency.RemoveLast();
                    _recencyIndex.Remove(oldest.Value);
                    evict = oldest.Value;
                }
            }
        }

        // Outside the lock: an eviction only forgets a baseline, so the evicted device's next fix
        // is treated as its first and is not flagged. Losing a baseline can never manufacture a
        // false alert, only miss one.
        if (evict is not null)
        {
            _lastFix.TryRemove(evict, out _);
            _recentlyFlagged.TryRemove(evict, out _);
        }
    }

    private readonly record struct LastFix(double Latitude, double Longitude, DateTime FixTimeUtc);
}

/// <summary>The advisory outcome of one plausibility evaluation.</summary>
/// <param name="TeleportSuspected">Displacement from the previous fix is impossible in the elapsed time.</param>
/// <param name="ImpossibleSpeed">The device reported a ground speed above the physical ceiling.</param>
/// <param name="ImpliedSpeedKph">Speed implied by the displacement, when one was computed.</param>
/// <param name="DistanceMetres">Displacement from the previous fix, when there was one.</param>
public readonly record struct PlausibilityVerdict(
    bool TeleportSuspected,
    bool ImpossibleSpeed,
    double? ImpliedSpeedKph,
    double? DistanceMetres)
{
    /// <summary>Whether this fix raised any plausibility concern.</summary>
    public bool IsSuspect => TeleportSuspected || ImpossibleSpeed;
}
