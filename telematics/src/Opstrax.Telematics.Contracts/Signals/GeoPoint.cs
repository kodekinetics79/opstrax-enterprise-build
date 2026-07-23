namespace Opstrax.Telematics.Contracts.Signals;

/// <summary>
/// An immutable geographic fix. Latitude/longitude are always present; all other
/// components are optional because trackers report wildly different subsets of the
/// GNSS fix depending on firmware, antenna and fix quality.
/// </summary>
/// <remarks>
/// Coordinates use WGS-84 decimal degrees. This type performs no validation of its
/// own — plausibility (in-range coordinates, impossible speed, teleport detection)
/// is a normalization-stage concern surfaced through the event quality flags, not a
/// constructor invariant, so that a raw-but-suspect fix can still be represented.
/// </remarks>
/// <param name="Lat">WGS-84 latitude in decimal degrees.</param>
/// <param name="Lng">WGS-84 longitude in decimal degrees.</param>
/// <param name="AltitudeM">Altitude above mean sea level in metres, when reported.</param>
/// <param name="AccuracyM">Estimated horizontal accuracy radius in metres, when reported.</param>
/// <param name="Satellites">Number of satellites used in the fix, when reported.</param>
/// <param name="HeadingDeg">Course over ground in degrees clockwise from true north [0,360), when reported.</param>
/// <param name="SpeedKph">Ground speed in kilometres per hour, when reported.</param>
public readonly record struct GeoPoint(
    double Lat,
    double Lng,
    double? AltitudeM = null,
    double? AccuracyM = null,
    int? Satellites = null,
    double? HeadingDeg = null,
    double? SpeedKph = null);
