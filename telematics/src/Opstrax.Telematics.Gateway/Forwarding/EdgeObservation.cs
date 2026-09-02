using System.Buffers;
using System.Globalization;
using System.Text;
using System.Text.Json;

namespace Opstrax.Telematics.Gateway.Forwarding;

/// <summary>
/// One decoded fix, normalized into exactly the shape
/// <c>POST /api/telemetry/gps-ingest</c> accepts, and nothing more.
/// </summary>
/// <remarks>
/// <para>
/// <b>This type carries no ownership.</b> There is no tenant, company, vehicle or driver here —
/// only the device's own claim and what its sensors reported. That is the whole point of the
/// HTTPS edge topology: the edge is a protocol translator, OpsTrax is the identity authority. It
/// resolves the IMEI against <c>eld_devices</c>, enforces that the forwarding gateway's
/// credential is scoped to that device's tenant, and resolves the installation and dispatch
/// assignment itself. An edge that guessed at ownership would be a second, unsynchronised source
/// of truth for tenancy — the exact class of bug that leaks one customer's trucks into another's
/// map.
/// </para>
/// </remarks>
/// <param name="Imei">The device's self-asserted identifier. A lookup key for OpsTrax, never a credential.</param>
/// <param name="Lat">WGS-84 latitude in decimal degrees.</param>
/// <param name="Lng">WGS-84 longitude in decimal degrees.</param>
/// <param name="FixTimeUtc">The device's own fix clock. Mandatory — see <see cref="ObservationNormalizer"/>.</param>
/// <param name="SpeedKph">Ground speed in km/h, when reported.</param>
/// <param name="HeadingDeg">Course over ground in [0,360), when reported.</param>
/// <param name="AltitudeM">Altitude in metres, when reported.</param>
/// <param name="Satellites">Satellites used in the fix, when reported.</param>
/// <param name="EngineStatus">Motion bucket in OpsTrax's own vocabulary. See <see cref="ObservationNormalizer"/>.</param>
/// <param name="OdometerKm">Odometer in kilometres, when reported.</param>
/// <param name="FuelPercent">Fuel level percentage, when reported.</param>
/// <param name="BatteryVoltage">Starter battery voltage, when reported.</param>
/// <param name="HarshEvent">Safety event in OpsTrax's accepted vocabulary, or null.</param>
/// <param name="Magnitude">Severity/g-force accompanying <paramref name="HarshEvent"/>.</param>
/// <param name="Provider">Hardware vendor, when the protocol identifies one. Null for multi-vendor protocols.</param>
/// <param name="Protocol">Wire protocol the frame was decoded as, stamped into fix provenance.</param>
/// <param name="EdgeInstance">Identifier of the edge host that relayed this fix.</param>
internal sealed record EdgeObservation(
    string Imei,
    double Lat,
    double Lng,
    DateTime FixTimeUtc,
    double? SpeedKph,
    double? HeadingDeg,
    double? AltitudeM,
    int? Satellites,
    string? EngineStatus,
    double? OdometerKm,
    double? FuelPercent,
    double? BatteryVoltage,
    string? HarshEvent,
    double? Magnitude,
    string? Provider,
    string Protocol,
    string? EdgeInstance)
{
    /// <summary>
    /// Serializes to the exact JSON body that will be signed and sent.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>The bytes are the contract.</b> OpsTrax verifies
    /// <c>HMAC-SHA256(secret, "{timestamp}.{rawBody}")</c> against <c>body.GetRawText()</c> — the
    /// exact bytes it received. So this string is produced once, signed, and transmitted verbatim;
    /// re-serializing it anywhere between here and the socket invalidates the signature. That is
    /// also why the outbox stores this rendered string rather than the record.
    /// </para>
    /// <para>
    /// Absent values are omitted rather than sent as null: the endpoint's readers treat a present
    /// key as a reading, and "the sensor reported nothing" is not the same claim as "the sensor
    /// reported zero".
    /// </para>
    /// </remarks>
    public string ToJson()
    {
        var buffer = new ArrayBufferWriter<byte>();
        using (var writer = new Utf8JsonWriter(buffer))
        {
            writer.WriteStartObject();

            writer.WriteString("imei", Imei);
            writer.WriteNumber("lat", Lat);
            writer.WriteNumber("lng", Lng);

            // Round-trip ("O") with an explicit Z: unambiguous to DateTimeOffset.TryParse under
            // AssumeUniversal, and it preserves sub-second precision so two fixes in the same
            // second stay distinguishable in history.
            writer.WriteString("gpsTime", FixTimeUtc.ToString("O", CultureInfo.InvariantCulture));

            WriteOptionalNumber(writer, "speedKmh", SpeedKph);
            WriteOptionalNumber(writer, "heading", HeadingDeg);
            WriteOptionalNumber(writer, "altitude", AltitudeM);
            if (Satellites is { } sats) writer.WriteNumber("satellites", sats);
            if (!string.IsNullOrEmpty(EngineStatus)) writer.WriteString("engineStatus", EngineStatus);
            WriteOptionalNumber(writer, "odometer", OdometerKm);
            WriteOptionalNumber(writer, "fuel", FuelPercent);
            WriteOptionalNumber(writer, "batteryVoltage", BatteryVoltage);

            if (!string.IsNullOrEmpty(HarshEvent))
            {
                writer.WriteString("harshEvent", HarshEvent);
                WriteOptionalNumber(writer, "magnitude", Magnitude);
            }

            if (!string.IsNullOrEmpty(Provider)) writer.WriteString("provider", Provider);
            writer.WriteString("protocol", Protocol);
            if (!string.IsNullOrEmpty(EdgeInstance)) writer.WriteString("edgeInstance", EdgeInstance);

            writer.WriteEndObject();
        }

        return Encoding.UTF8.GetString(buffer.WrittenSpan);
    }

    private static void WriteOptionalNumber(Utf8JsonWriter writer, string name, double? value)
    {
        if (value is not { } v || double.IsNaN(v) || double.IsInfinity(v)) return;
        writer.WriteNumber(name, v);
    }
}
