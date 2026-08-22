using System.Globalization;
using Opstrax.Telematics.Contracts.Adapters;

namespace Opstrax.Telematics.Gateway.Forwarding;

/// <summary>Why a decoded frame did not become a forwardable observation.</summary>
internal enum NormalizationRejection
{
    /// <summary>Not a rejection — the frame normalized successfully.</summary>
    None = 0,

    /// <summary>The frame carried no GNSS fix (heartbeat, pure status). Not an error.</summary>
    NoLocation,

    /// <summary>Latitude/longitude are outside WGS-84 range, or are the null-island sentinel (0,0).</summary>
    InvalidCoordinates,

    /// <summary>The frame carried no device-originated fix clock. OpsTrax refuses such a fix, and rightly.</summary>
    MissingDeviceTime,

    /// <summary>The device clock is further than 30 days in the past or 5 minutes in the future.</summary>
    DeviceTimeOutOfWindow,
}

/// <summary>The outcome of normalizing one decoded frame.</summary>
/// <param name="Observation">The forwardable observation, or null when <paramref name="Rejection"/> is set.</param>
/// <param name="Rejection">Why the frame was not forwardable.</param>
/// <param name="DroppedFields">
/// Auxiliary sensor readings discarded as out-of-range. Present so a decode fault is
/// <em>counted and named</em> rather than silently smoothed away.
/// </param>
internal readonly record struct NormalizationResult(
    EdgeObservation? Observation,
    NormalizationRejection Rejection,
    IReadOnlyList<string> DroppedFields)
{
    /// <summary><see langword="true"/> when an observation was produced.</summary>
    public bool IsForwardable => Observation is not null;

    internal static NormalizationResult Reject(NormalizationRejection reason) =>
        new(null, reason, Array.Empty<string>());
}

/// <summary>
/// Turns an adapter's protocol-local field bag into the exact payload OpsTrax accepts.
/// </summary>
/// <remarks>
/// <para>
/// <b>Why this is not a trivial mapping.</b> Every rule below exists because the receiving
/// endpoint enforces it, and a payload that violates one is rejected wholesale — losing a real
/// fix from a real truck for a formatting reason. The normalizer's job is to make that
/// impossible before a byte leaves the edge.
/// </para>
/// <para>
/// <b>Vocabulary matters more than it looks.</b> The live map buckets a vehicle as moving from
/// <c>engineStatus</c> matching <c>/active|on route|moving|driving|en route/</c> or a speed above
/// 3 — so the obvious-looking <c>"On"</c> matches neither and parks a moving truck on the map.
/// Likewise <c>harshEvent</c> is the <em>only</em> producer of driver safety events, and OpsTrax
/// accepts exactly five values; anything else is silently dropped there, so it is dropped here
/// instead, where it can be counted.
/// </para>
/// <para>
/// <b>Two classes of bad data, two policies.</b> Anything that constitutes the fix itself
/// (coordinates, device clock) rejects the whole observation — a fix that is wrong about where or
/// when is worse than no fix. An auxiliary sensor reading that is out of range is dropped from
/// the payload and reported in <see cref="NormalizationResult.DroppedFields"/>, so the position
/// still reaches the map while the fault stays visible.
/// </para>
/// </remarks>
internal static class ObservationNormalizer
{
    /// <summary>Speed above which a vehicle is bucketed as moving, matching the live map's own threshold.</summary>
    private const double MovingSpeedKph = 5.0; // ≈3 mph, the frontend's bucket boundary.

    /// <summary>OpsTrax rejects a device clock older than this.</summary>
    private static readonly TimeSpan MaxBackdate = TimeSpan.FromDays(30);

    /// <summary>OpsTrax rejects a device clock further ahead than this.</summary>
    private static readonly TimeSpan MaxFuturedate = TimeSpan.FromMinutes(5);

    /// <summary>km/h equivalent of the endpoint's 250 mph ceiling.</summary>
    private const double MaxSpeedKph = 250.0 / 0.621371;

    /// <summary>
    /// Normalizes one decoded frame for an allowlisted IMEI.
    /// </summary>
    /// <param name="message">The decoded frame.</param>
    /// <param name="imei">The session's bound IMEI claim.</param>
/// <param name="provider">Hardware vendor label for provenance, or null when the protocol does not identify one.</param>
    /// <param name="protocol">Wire protocol label for provenance (for example <c>"GT06"</c>).</param>
    /// <param name="edgeInstance">Identifier of the relaying edge host.</param>
    /// <param name="receivedAtUtc">Gateway receive time. Used only to bound the device clock, never as a substitute for it.</param>
    public static NormalizationResult Normalize(
        DecodedMessage message,
        string imei,
        string? provider,
        string protocol,
        string? edgeInstance,
        DateTime receivedAtUtc)
    {
        ArgumentNullException.ThrowIfNull(message);

        IReadOnlyDictionary<string, object?> fields = message.Fields;

        double? lat = Number(fields, "latitude", "lat");
        double? lng = Number(fields, "longitude", "lng", "lon", "long");
        if (lat is null || lng is null)
            return NormalizationResult.Reject(NormalizationRejection.NoLocation);

        // Mirrors TelemetryTicketHelper.IsCoordinateValid, including its null-island rejection:
        // (0,0) is what a tracker emits when it has no fix, not a position in the Gulf of Guinea.
        if (lat is < -90 or > 90 || lng is < -180 or > 180 ||
            double.IsNaN(lat.Value) || double.IsNaN(lng.Value) ||
            (lat.Value == 0 && lng.Value == 0))
            return NormalizationResult.Reject(NormalizationRejection.InvalidCoordinates);

        // The device clock is mandatory and is never substituted with arrival time. Substituting it
        // would relabel an offline-buffered frame as a live one, defeating the freshness grading
        // the map relies on to tell a moving truck from a parked modem.
        if (DeviceTime(fields) is not { } fixTime)
            return NormalizationResult.Reject(NormalizationRejection.MissingDeviceTime);

        if (fixTime < receivedAtUtc - MaxBackdate || fixTime > receivedAtUtc + MaxFuturedate)
            return NormalizationResult.Reject(NormalizationRejection.DeviceTimeOutOfWindow);

        var dropped = new List<string>();

        double? speedKph = Bounded(Number(fields, "speedKph", "speedKmh", "speed"), 0, MaxSpeedKph, "speedKmh", dropped);
        double? heading = Heading(Number(fields, "courseDeg", "heading", "course", "bearing"));
        double? altitude = Number(fields, "altitudeM", "altitude", "alt");
        double? fuel = Bounded(Number(fields, "fuelPercent", "fuelLevel", "fuel"), 0, 100, "fuel", dropped);
        double? odometer = Bounded(Number(fields, "odometerKm", "odometer", "mileage"), 0, double.MaxValue, "odometer", dropped);
        double? battery = Bounded(Number(fields, "batteryVoltage", "voltage"), 0, 100, "batteryVoltage", dropped);
        int? satellites = (int?)Bounded(Number(fields, "satellites", "sats"), 0, 64, "satellites", dropped);

        bool? ignition = Boolean(fields, "ignitionOn", "ignition", "acc");
        string? harsh = HarshEvent(Text(fields, "harshEvent", "alarmName", "alarmType", "alarm", "event"));
        double? magnitude = Number(fields, "magnitude", "gForce", "g_force", "severityValue");

        return new NormalizationResult(
            new EdgeObservation(
                Imei: imei,
                Lat: lat.Value,
                Lng: lng.Value,
                FixTimeUtc: fixTime,
                SpeedKph: speedKph,
                HeadingDeg: heading,
                AltitudeM: altitude,
                Satellites: satellites,
                EngineStatus: EngineStatus(ignition, speedKph),
                OdometerKm: odometer,
                FuelPercent: fuel,
                BatteryVoltage: battery,
                HarshEvent: harsh,
                Magnitude: harsh is null ? null : magnitude,
                Provider: provider,
                Protocol: protocol,
                EdgeInstance: edgeInstance),
            NormalizationRejection.None,
            dropped);
    }

    /// <summary>
    /// Maps ignition and speed to the motion vocabulary the OpsTrax live map actually buckets on.
    /// </summary>
    /// <remarks>
    /// Returns <see langword="null"/> — not a guess — when the device reported neither ignition nor
    /// speed. The map renders an unknown state as parked, which is the honest reading of "this
    /// frame told us nothing about motion"; inventing <c>"Idle"</c> would assert a measurement the
    /// hardware never made.
    /// </remarks>
    internal static string? EngineStatus(bool? ignitionOn, double? speedKph)
    {
        bool moving = speedKph is { } s && s > MovingSpeedKph;

        return ignitionOn switch
        {
            false => "Off",
            true => moving ? "Moving" : "Idle",
            null => moving ? "Moving" : null,
        };
    }

    /// <summary>
    /// Wraps a course into [0,360) and rounds to one decimal.
    /// </summary>
    /// <remarks>
    /// The rounding order is the point. OpsTrax rejects <c>heading &gt;= 360</c>, and a raw
    /// 359.97° rounds to exactly 360.0 — so rounding first and wrapping second is what keeps a
    /// legitimate near-north heading from failing the whole request. Wrapping is the correct
    /// reading of a course value (it is modular), which is why this is normalized rather than
    /// dropped like the other out-of-range readings.
    /// </remarks>
    internal static double? Heading(double? raw)
    {
        if (raw is not { } value || double.IsNaN(value) || double.IsInfinity(value)) return null;

        double rounded = Math.Round(value, 1, MidpointRounding.AwayFromZero);
        double wrapped = rounded % 360.0;
        if (wrapped < 0) wrapped += 360.0;

        // 359.97 -> 360.0 -> 0.0 is the case this whole method exists for. The equality also
        // collapses IEEE negative zero, which would otherwise serialize as "-0".
        return wrapped == 0.0 ? 0.0 : wrapped;
    }

    /// <summary>
    /// Maps a protocol alarm name onto the five values OpsTrax turns into driver safety events,
    /// or <see langword="null"/> when the alarm has no safety meaning.
    /// </summary>
    /// <remarks>
    /// Mirrors the endpoint's own <c>NormalizeHarshEvent</c>. Alarms outside this set — power cut,
    /// geofence crossings, overspeed, low battery — are real events but are <em>not</em> safety
    /// events, and forwarding them under <c>harshEvent</c> would have them silently discarded
    /// server-side. Returning null here keeps that a counted edge decision instead.
    /// </remarks>
    internal static string? HarshEvent(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return null;

        string v = raw.Trim().ToLowerInvariant().Replace('-', '_').Replace(' ', '_');
        return v switch
        {
            "harsh_braking" or "harshbraking" or "hard_brake" or "harsh_brake" or "braking" or "brake"
                => "harsh_braking",
            "harsh_acceleration" or "harshacceleration" or "hard_accel" or "harsh_accel" or "acceleration" or "accel"
                => "harsh_acceleration",
            "harsh_turn" or "harsh_cornering" or "hard_turn" or "cornering" or "turn" or "corner"
                => "harsh_turn",
            "crash" or "collision" or "accident" or "impact" or "fall"
                => "crash",
            "sos" or "panic" or "emergency"
                => "sos",
            _ => null,
        };
    }

    // ── Field access ───────────────────────────────────────────────────────────

    /// <summary>
    /// Reads the first present alias as a double. Aliases exist because each adapter names its own
    /// fields (GT06 emits <c>speedKph</c>/<c>courseDeg</c>; a vendor parser behind the Pacific
    /// Track seam will emit its own), and the normalizer is the one place that reconciles them.
    /// </summary>
    private static double? Number(IReadOnlyDictionary<string, object?> fields, params string[] names)
    {
        foreach (string name in names)
        {
            if (!fields.TryGetValue(name, out object? raw) || raw is null) continue;

            switch (raw)
            {
                case double d: return d;
                case float f: return f;
                case int i: return i;
                case long l: return l;
                case short s: return s;
                case byte b: return b;
                case decimal m: return (double)m;
                case string text when double.TryParse(
                    text, NumberStyles.Float, CultureInfo.InvariantCulture, out double parsed):
                    return parsed;
            }
        }

        return null;
    }

    private static bool? Boolean(IReadOnlyDictionary<string, object?> fields, params string[] names)
    {
        foreach (string name in names)
        {
            if (!fields.TryGetValue(name, out object? raw) || raw is null) continue;

            switch (raw)
            {
                case bool b: return b;
                case int i: return i != 0;
                case long l: return l != 0;
                case string text when bool.TryParse(text, out bool parsed): return parsed;
            }
        }

        return null;
    }

    private static string? Text(IReadOnlyDictionary<string, object?> fields, params string[] names)
    {
        foreach (string name in names)
            if (fields.TryGetValue(name, out object? raw) && raw is string text && !string.IsNullOrWhiteSpace(text))
                return text;

        return null;
    }

    /// <summary>
    /// Reads a device fix clock. Accepts a <see cref="DateTime"/> straight from an adapter, or a
    /// string/epoch from a bridged vendor parser that only speaks JSON.
    /// </summary>
    private static DateTime? DeviceTime(IReadOnlyDictionary<string, object?> fields)
    {
        foreach (string name in new[] { "fixTimeUtc", "gpsTime", "deviceTimeUtc", "eventTime", "timestamp", "ts" })
        {
            if (!fields.TryGetValue(name, out object? raw) || raw is null) continue;

            switch (raw)
            {
                case DateTime dt:
                    return dt.Kind == DateTimeKind.Utc ? dt : dt.ToUniversalTime();

                case DateTimeOffset dto:
                    return dto.UtcDateTime;

                case string text when DateTimeOffset.TryParse(
                    text, CultureInfo.InvariantCulture,
                    DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out DateTimeOffset parsed):
                    return parsed.UtcDateTime;

                case long epoch:
                    return FromEpoch(epoch);

                case int epoch32:
                    return FromEpoch(epoch32);
            }
        }

        return null;
    }

    /// <summary>Interprets an epoch as milliseconds when it is too large to be seconds.</summary>
    private static DateTime? FromEpoch(long epoch)
    {
        try
        {
            return Math.Abs(epoch) >= 100_000_000_000L
                ? DateTimeOffset.FromUnixTimeMilliseconds(epoch).UtcDateTime
                : DateTimeOffset.FromUnixTimeSeconds(epoch).UtcDateTime;
        }
        catch (ArgumentOutOfRangeException)
        {
            return null;
        }
    }

    /// <summary>
    /// Keeps an auxiliary reading only when it is inside the range OpsTrax accepts; otherwise drops
    /// it and records the field name so the caller can count and log a probable decode fault.
    /// </summary>
    private static double? Bounded(double? value, double min, double max, string name, List<string> dropped)
    {
        if (value is not { } v) return null;

        if (double.IsNaN(v) || double.IsInfinity(v) || v < min || v > max)
        {
            dropped.Add(name);
            return null;
        }

        return v;
    }
}
