using System.Text.Json;
using Opstrax.Telematics.Contracts.Adapters;
using Opstrax.Telematics.Gateway.Forwarding;

namespace Opstrax.Telematics.IntegrationTests;

/// <summary>
/// Covers the translation from a decoder's field bag to the exact payload
/// <c>POST /api/telemetry/gps-ingest</c> accepts.
/// </summary>
/// <remarks>
/// Every case here corresponds to a rule the receiving endpoint enforces. A payload that breaks
/// one is refused wholesale, which loses a real fix from a real truck for a formatting reason —
/// so these are correctness tests, not cosmetics.
/// </remarks>
public sealed class EdgeNormalizationTests
{
    private const string Imei = "862464068456321";
    private static readonly DateTime Now = new(2026, 8, 21, 12, 0, 0, DateTimeKind.Utc);

    private static NormalizationResult Normalize(params (string Key, object? Value)[] fields)
    {
        var bag = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
        foreach ((string key, object? value) in fields) bag[key] = value;

        var message = new DecodedMessage(MessageType.Location, new byte[] { 0x78, 0x78 }, bag);
        return ObservationNormalizer.Normalize(message, Imei, "Pacific Track", "GT06", "edge-1", Now);
    }

    private static (string Key, object? Value)[] ValidFix(params (string Key, object? Value)[] extra) =>
        new (string, object?)[]
        {
            ("latitude", 38.9072),
            ("longitude", -77.0369),
            ("fixTimeUtc", Now.AddSeconds(-30)),
        }.Concat(extra).ToArray();

    // ── Heading: the 360.0 trap ────────────────────────────────────────────────

    [Theory]
    [InlineData(359.97, 0.0)]    // rounds to 360.0, which the endpoint rejects outright
    [InlineData(359.94, 359.9)]
    [InlineData(360.0, 0.0)]
    [InlineData(0.0, 0.0)]
    [InlineData(91.44, 91.4)]
    [InlineData(-0.5, 359.5)]
    [InlineData(720.5, 0.5)]
    public void Heading_IsAlwaysInsideTheAcceptedRange(double raw, double expected)
    {
        double? heading = ObservationNormalizer.Heading(raw);

        Assert.NotNull(heading);
        Assert.Equal(expected, heading!.Value, precision: 6);
        Assert.InRange(heading.Value, 0.0, 359.9999);
    }

    [Fact]
    public void Heading_IsAbsentWhenNotReported()
    {
        Assert.Null(ObservationNormalizer.Heading(null));
        Assert.Null(ObservationNormalizer.Heading(double.NaN));
    }

    // ── Motion vocabulary the live map actually buckets on ─────────────────────

    [Theory]
    [InlineData(false, 0.0, "Off")]
    [InlineData(false, 80.0, "Off")]
    [InlineData(true, 0.0, "Idle")]
    [InlineData(true, 3.0, "Idle")]
    [InlineData(true, 80.0, "Moving")]
    public void EngineStatus_UsesTheVocabularyTheMapUnderstands(bool ignition, double speed, string expected)
    {
        // "On" would match neither the map's moving regex nor its speed rule, parking a moving truck.
        Assert.Equal(expected, ObservationNormalizer.EngineStatus(ignition, speed));
    }

    [Fact]
    public void EngineStatus_IsNullWhenTheDeviceReportedNothingAboutMotion()
    {
        // Not "Idle": that would assert a measurement the hardware never made.
        Assert.Null(ObservationNormalizer.EngineStatus(null, null));
        Assert.Equal("Moving", ObservationNormalizer.EngineStatus(null, 80.0));
    }

    // ── Safety-event vocabulary ────────────────────────────────────────────────

    [Theory]
    [InlineData("SOS", "sos")]
    [InlineData("Fall", "crash")]
    [InlineData("harsh-braking", "harsh_braking")]
    [InlineData("Hard Brake", "harsh_braking")]
    [InlineData("cornering", "harsh_turn")]
    [InlineData("collision", "crash")]
    public void HarshEvent_MapsOntoTheFiveValuesOpsTraxTurnsIntoSafetyEvents(string raw, string expected)
    {
        Assert.Equal(expected, ObservationNormalizer.HarshEvent(raw));
    }

    [Theory]
    [InlineData("PowerCut")]
    [InlineData("EnterFence")]
    [InlineData("Overspeed")]
    [InlineData("LowBattery")]
    [InlineData("Normal")]
    public void NonSafetyAlarms_AreNotForwardedAsHarshEvents(string raw)
    {
        // Real events, but not safety events. OpsTrax would drop them silently; dropping them here
        // keeps the decision counted at the edge instead.
        Assert.Null(ObservationNormalizer.HarshEvent(raw));
    }

    [Fact]
    public void Magnitude_IsOmittedWhenThereIsNoHarshEvent()
    {
        NormalizationResult result = Normalize(ValidFix(("alarmName", "Overspeed"), ("magnitude", 0.7)));

        Assert.True(result.IsForwardable);
        using JsonDocument payload = JsonDocument.Parse(result.Observation!.ToJson());
        Assert.False(payload.RootElement.TryGetProperty("magnitude", out _));
        Assert.False(payload.RootElement.TryGetProperty("harshEvent", out _));
    }

    // ── Fix-defining values reject the whole observation ───────────────────────

    [Fact]
    public void NullIsland_IsRejected_BecauseItIsWhatATrackerSendsWithNoFix()
    {
        NormalizationResult result = Normalize(("latitude", 0.0), ("longitude", 0.0), ("fixTimeUtc", Now));

        Assert.False(result.IsForwardable);
        Assert.Equal(NormalizationRejection.InvalidCoordinates, result.Rejection);
    }

    [Theory]
    [InlineData(91.0, 0.0)]
    [InlineData(0.0, 181.0)]
    [InlineData(-90.5, 10.0)]
    public void OutOfRangeCoordinates_AreRejected(double lat, double lng)
    {
        NormalizationResult result = Normalize(("latitude", lat), ("longitude", lng), ("fixTimeUtc", Now));

        Assert.Equal(NormalizationRejection.InvalidCoordinates, result.Rejection);
    }

    [Fact]
    public void MissingDeviceClock_IsRejected_AndArrivalTimeIsNeverSubstituted()
    {
        // Substituting arrival time would relabel an offline-buffered frame as live, defeating the
        // freshness grading the map relies on.
        NormalizationResult result = Normalize(("latitude", 38.9), ("longitude", -77.0));

        Assert.Equal(NormalizationRejection.MissingDeviceTime, result.Rejection);
    }

    [Theory]
    [InlineData(-31 * 24 * 60)]  // beyond the 30-day backdate window
    [InlineData(10)]             // beyond the 5-minute future window
    public void DeviceClockOutsideTheAcceptedWindow_IsRejectedAtTheEdge(int offsetMinutes)
    {
        NormalizationResult result = Normalize(
            ("latitude", 38.9), ("longitude", -77.0), ("fixTimeUtc", Now.AddMinutes(offsetMinutes)));

        Assert.Equal(NormalizationRejection.DeviceTimeOutOfWindow, result.Rejection);
    }

    [Fact]
    public void FrameWithNoCoordinate_IsReportedAsNoLocation_NotAsAFault()
    {
        NormalizationResult result = Normalize(("fixTimeUtc", Now), ("ignitionOn", true));

        Assert.Equal(NormalizationRejection.NoLocation, result.Rejection);
    }

    // ── Auxiliary readings are dropped, not fatal ──────────────────────────────

    [Fact]
    public void ImplausibleAuxiliaryReadings_AreDroppedWhileThePositionStillGoesThrough()
    {
        NormalizationResult result = Normalize(ValidFix(
            ("speedKph", 9_000.0),
            ("fuelPercent", 250.0),
            ("odometerKm", -5.0)));

        Assert.True(result.IsForwardable);
        Assert.Null(result.Observation!.SpeedKph);
        Assert.Null(result.Observation.FuelPercent);
        Assert.Null(result.Observation.OdometerKm);

        // Named, so a repeated field-layout mismatch is diagnosable rather than invisible.
        Assert.Contains("speedKmh", result.DroppedFields);
        Assert.Contains("fuel", result.DroppedFields);
        Assert.Contains("odometer", result.DroppedFields);
    }

    // ── Field aliases across adapters ──────────────────────────────────────────

    [Fact]
    public void AdapterLocalFieldNames_AreReconciled()
    {
        // GT06 emits speedKph/courseDeg; a bridged vendor parser may emit speed/heading and an
        // epoch. Both must land on the same payload.
        NormalizationResult gt06 = Normalize(
            ("latitude", 38.9072), ("longitude", -77.0369),
            ("fixTimeUtc", Now.AddSeconds(-30)), ("speedKph", 52.0), ("courseDeg", 91));

        NormalizationResult bridged = Normalize(
            ("lat", 38.9072), ("lon", -77.0369),
            ("gpsTime", new DateTimeOffset(Now.AddSeconds(-30)).ToUnixTimeSeconds()),
            ("speed", "52"), ("heading", 91.0));

        Assert.True(gt06.IsForwardable);
        Assert.True(bridged.IsForwardable);
        Assert.Equal(gt06.Observation!.SpeedKph, bridged.Observation!.SpeedKph);
        Assert.Equal(gt06.Observation.HeadingDeg, bridged.Observation.HeadingDeg);
        Assert.Equal(gt06.Observation.FixTimeUtc, bridged.Observation.FixTimeUtc);
    }

    // ── The rendered payload ───────────────────────────────────────────────────

    [Fact]
    public void Payload_CarriesTheIdentifierAndNoOwnership()
    {
        NormalizationResult result = Normalize(ValidFix(("speedKph", 52.0), ("ignitionOn", true)));

        using JsonDocument payload = JsonDocument.Parse(result.Observation!.ToJson());
        JsonElement root = payload.RootElement;

        Assert.Equal(Imei, root.GetProperty("imei").GetString());
        Assert.Equal("Moving", root.GetProperty("engineStatus").GetString());
        Assert.Equal("GT06", root.GetProperty("protocol").GetString());
        Assert.Equal("edge-1", root.GetProperty("edgeInstance").GetString());

        // The edge is not an ownership authority; OpsTrax resolves all of these from the IMEI.
        foreach (string forbidden in new[] { "companyId", "tenantId", "vehicleId", "driverId", "assignmentId" })
            Assert.False(root.TryGetProperty(forbidden, out _), $"payload must not assert {forbidden}");
    }

    [Fact]
    public void AbsentReadings_AreOmittedRatherThanSentAsZero()
    {
        // "The sensor reported nothing" is not the same claim as "the sensor reported zero", and the
        // endpoint treats a present key as a reading.
        NormalizationResult result = Normalize(ValidFix());

        using JsonDocument payload = JsonDocument.Parse(result.Observation!.ToJson());
        foreach (string absent in new[] { "speedKmh", "heading", "fuel", "odometer", "altitude" })
            Assert.False(payload.RootElement.TryGetProperty(absent, out _), $"{absent} should be omitted");
    }

    [Fact]
    public void Payload_UsesADeviceTimestampOpsTraxCanParse()
    {
        NormalizationResult result = Normalize(ValidFix());

        using JsonDocument payload = JsonDocument.Parse(result.Observation!.ToJson());
        string raw = payload.RootElement.GetProperty("gpsTime").GetString()!;

        Assert.True(DateTimeOffset.TryParse(
            raw, System.Globalization.CultureInfo.InvariantCulture,
            System.Globalization.DateTimeStyles.AssumeUniversal |
            System.Globalization.DateTimeStyles.AdjustToUniversal, out DateTimeOffset parsed));
        Assert.Equal(Now.AddSeconds(-30), parsed.UtcDateTime);
    }

    [Fact]
    public void Provider_IsOmittedForMultiVendorProtocols()
    {
        // GT06 is spoken by many OEMs, so no manufacturer is derivable from the bytes and none is
        // claimed — an invented provider would surface in the Fix Provenance drawer as fact.
        var message = new DecodedMessage(
            MessageType.Location,
            new byte[] { 0x78, 0x78 },
            new Dictionary<string, object?>
            {
                ["latitude"] = 38.9072, ["longitude"] = -77.0369, ["fixTimeUtc"] = Now,
            });

        NormalizationResult result = ObservationNormalizer.Normalize(
            message, Imei, provider: null, protocol: "GT06", edgeInstance: null, receivedAtUtc: Now);

        using JsonDocument payload = JsonDocument.Parse(result.Observation!.ToJson());
        Assert.False(payload.RootElement.TryGetProperty("provider", out _));
    }
}
