using System.Globalization;
using Opstrax.Telematics.Contracts.Adapters;
using Opstrax.Telematics.Protocols.Gt06;

namespace Opstrax.Telematics.Protocols.Tests;

/// <summary>
/// Regression tests for the GT06 field-semantics defects found in the source audit: swapped
/// hemisphere bits, an inverted positioning-mode bit, an inverted oil/electricity bit, protocol
/// <c>0x18</c> decoded as an alarm, and a wrong alarm-code table.
/// </summary>
/// <remarks>
/// <para>
/// <b>Fixtures here are built from the protocol document, never from the parser.</b> Every frame
/// either comes from <c>fixtures/gt06/quadrant_*.hex</c> and <c>positioning_*.hex</c> — generated
/// by <c>fixtures/gt06/generate_quadrant_fixtures.py</c> straight off the vendor bit table — or is
/// assembled here from named bit constants that restate that table. This matters more than usual:
/// the pre-existing fixture set could not have caught the hemisphere swap, because every GPS
/// fixture in it carried bits 10 and 11 with the SAME value, and a swap is invisible when the two
/// bits agree.
/// </para>
/// </remarks>
public class Gt06ProtocolCorrectionTests
{
    private readonly Gt06Adapter _adapter = new();

    // The Course/Status bit table, restated from the vendor document (BYTE1 bit N = word bit N+8).
    private const int BitLatitudeNorth = 1 << 10;
    private const int BitLongitudeWest = 1 << 11;
    private const int BitPositioned = 1 << 12;
    private const int BitDifferential = 1 << 13;

    // ── P0-03: hemispheres ─────────────────────────────────────────────────────

    /// <summary>
    /// The acceptance criterion from the audit: a real North/East coordinate must never decode as
    /// South/West. Under the pre-fix parser this exact case did — bit 10 was read as the longitude
    /// hemisphere and bit 11 as the latitude hemisphere, so N/E (bit10 set, bit11 clear) inverted
    /// both axes at once and put a Tokyo fix in the South Atlantic.
    /// </summary>
    [Theory]
    [InlineData("quadrant_north_east.hex", 35.6762, 139.6503, true, false)]
    [InlineData("quadrant_north_west.hex", 40.7128, -74.0060, true, true)]
    [InlineData("quadrant_south_east.hex", -33.8688, 151.2093, false, false)]
    [InlineData("quadrant_south_west.hex", -34.6037, -58.3816, false, true)]
    public void All_four_hemisphere_quadrants_decode_to_the_documented_signs(
        string fixture, double expectedLat, double expectedLng, bool north, bool west)
    {
        DecodedMessage msg = DecodeSingle(fixture);

        Assert.Equal(expectedLat, (double)msg.Fields["latitude"]!, 4);
        Assert.Equal(expectedLng, (double)msg.Fields["longitude"]!, 4);
        Assert.Equal(north, (bool)msg.Fields["hemisphereNorth"]!);
        Assert.Equal(west, (bool)msg.Fields["hemisphereWest"]!);

        // Sign agreement, stated independently of the magnitudes above.
        Assert.Equal(north, (double)msg.Fields["latitude"]! > 0);
        Assert.Equal(west, (double)msg.Fields["longitude"]! < 0);
    }

    /// <summary>Bit 10 alone drives latitude; bit 11 alone drives longitude. Neither crosses over.</summary>
    [Fact]
    public void Bit10_is_latitude_and_bit11_is_longitude_independently()
    {
        // Only bit 10 set: North latitude, East longitude.
        DecodedMessage latitudeOnly = DecodeSingle(GpsFrame(BitPositioned | BitLatitudeNorth));
        Assert.True((bool)latitudeOnly.Fields["hemisphereNorth"]!);
        Assert.False((bool)latitudeOnly.Fields["hemisphereWest"]!);
        Assert.True((double)latitudeOnly.Fields["latitude"]! > 0);
        Assert.True((double)latitudeOnly.Fields["longitude"]! > 0);

        // Only bit 11 set: South latitude, West longitude.
        DecodedMessage longitudeOnly = DecodeSingle(GpsFrame(BitPositioned | BitLongitudeWest));
        Assert.False((bool)longitudeOnly.Fields["hemisphereNorth"]!);
        Assert.True((bool)longitudeOnly.Fields["hemisphereWest"]!);
        Assert.True((double)longitudeOnly.Fields["latitude"]! < 0);
        Assert.True((double)longitudeOnly.Fields["longitude"]! < 0);
    }

    /// <summary>
    /// The vendor document's own worked example, decoded end to end: course/status <c>0x154C</c> is
    /// annotated "Bit5=0 -> real time GPS, Bit4=1 -> GPS has been positioned". Under the corrected
    /// table it must also read as North/East with course 332 — a self-consistent fix.
    /// </summary>
    [Fact]
    public void Vendor_worked_example_0x154C_decodes_coherently()
    {
        DecodedMessage msg = DecodeSingle(GpsFrame(0x154C));

        Assert.Equal(332, msg.Fields["courseDeg"]);
        Assert.True((bool)msg.Fields["positioned"]!);
        Assert.True((bool)msg.Fields["realTimeGps"]!);
        Assert.False((bool)msg.Fields["isDifferentialPositioning"]!);
        Assert.True((bool)msg.Fields["hemisphereNorth"]!);
        Assert.False((bool)msg.Fields["hemisphereWest"]!);
    }

    /// <summary>The course occupies bits 0-9 only and is unaffected by any status bit.</summary>
    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(217)]
    [InlineData(359)]
    public void Course_is_bits_0_to_9_and_is_not_disturbed_by_the_status_bits(int course)
    {
        int allStatusBits = BitLatitudeNorth | BitLongitudeWest | BitPositioned | BitDifferential;
        Assert.Equal(course, DecodeSingle(GpsFrame(course)).Fields["courseDeg"]);
        Assert.Equal(course, DecodeSingle(GpsFrame(course | allStatusBits)).Fields["courseDeg"]);
    }

    // ── P1-04: positioning mode ────────────────────────────────────────────────

    /// <summary>
    /// Bit 13 asserted means DIFFERENTIAL positioning, not real-time. Both polarities are covered
    /// here on otherwise byte-identical frames, so the assertion cannot pass by accident.
    /// </summary>
    [Fact]
    public void Positioning_mode_bit13_is_asserted_for_differential_not_realtime()
    {
        DecodedMessage realtime = DecodeSingle("positioning_realtime.hex");
        Assert.False((bool)realtime.Fields["isDifferentialPositioning"]!);
        Assert.True((bool)realtime.Fields["realTimeGps"]!);

        DecodedMessage differential = DecodeSingle("positioning_differential.hex");
        Assert.True((bool)differential.Fields["isDifferentialPositioning"]!);
        Assert.False((bool)differential.Fields["realTimeGps"]!);

        // The two fixtures differ ONLY in bit 13, so nothing else may move with it.
        Assert.Equal(realtime.Fields["latitude"], differential.Fields["latitude"]);
        Assert.Equal(realtime.Fields["longitude"], differential.Fields["longitude"]);
        Assert.Equal(realtime.Fields["courseDeg"], differential.Fields["courseDeg"]);
        Assert.Equal(realtime.Fields["positioned"], differential.Fields["positioned"]);
    }

    /// <summary>The two named fields are always exact negations of each other.</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void RealtimeGps_is_always_the_negation_of_differential(bool differential)
    {
        int word = BitPositioned | BitLatitudeNorth | (differential ? BitDifferential : 0);
        DecodedMessage msg = DecodeSingle(GpsFrame(word));

        Assert.Equal(differential, (bool)msg.Fields["isDifferentialPositioning"]!);
        Assert.NotEqual((bool)msg.Fields["isDifferentialPositioning"]!, (bool)msg.Fields["realTimeGps"]!);
    }

    // ── P1-05: oil / electricity ───────────────────────────────────────────────

    /// <summary>
    /// Terminal-information bit 7 is asserted when oil and electricity are DISCONNECTED. The
    /// canonical downstream field keeps its name, so it must carry the negation of the bit —
    /// publishing the raw bit under that name reported a cut-off vehicle as powered and a powered
    /// vehicle as cut off, which is the wrong answer in both directions at once.
    /// </summary>
    [Theory]
    [InlineData(0x00, true, false)]   // bit 7 clear -> connected
    [InlineData(0x80, false, true)]   // bit 7 set   -> disconnected
    [InlineData(0x46, true, false)]   // the heartbeat fixture's terminal info: ignition, charging, tracking
    [InlineData(0xC6, false, true)]   // the same, with the relay cut
    public void Oil_and_electricity_bit7_is_asserted_for_DISCONNECTED(
        int terminalInfo, bool expectConnected, bool expectDisconnected)
    {
        DecodedMessage msg = DecodeSingle(StatusFrame((byte)terminalInfo));

        Assert.Equal(expectConnected, (bool)msg.Fields["oilElectricityConnected"]!);
        Assert.Equal(expectDisconnected, (bool)msg.Fields["oilElectricityDisconnected"]!);
        Assert.NotEqual(
            (bool)msg.Fields["oilElectricityConnected"]!,
            (bool)msg.Fields["oilElectricityDisconnected"]!);
    }

    /// <summary>The other terminal-information bits keep their established meanings.</summary>
    [Fact]
    public void Other_terminal_information_bits_are_unchanged()
    {
        DecodedMessage msg = DecodeSingle(StatusFrame(0x46)); // 0100 0110

        Assert.False((bool)msg.Fields["defenseActivated"]!);  // bit 0
        Assert.True((bool)msg.Fields["ignitionOn"]!);         // bit 1
        Assert.True((bool)msg.Fields["charging"]!);           // bit 2
        Assert.True((bool)msg.Fields["gpsTracking"]!);        // bit 6
    }

    // ── P1-06: protocol 0x18 ───────────────────────────────────────────────────

    /// <summary>
    /// A valid <c>0x18</c> frame must never become a fabricated alarm. It carries LBS
    /// multiple-base-station data, so running the GPS/alarm layout over it reads MCC/MNC/LAC/CellId
    /// bytes as a coordinate and an alarm code — a position and an emergency invented out of
    /// cell-tower identifiers. The frame is retained whole and nothing is decoded from it.
    /// </summary>
    [Fact]
    public void Protocol_0x18_is_not_an_alarm_and_fabricates_no_position()
    {
        // Content shaped like the LBS block the old alarm path would have misread as GPS.
        byte[] lbsContent = Convert.FromHexString("18010F0A141E01CC0101550009C6" + "0102030405");
        byte[] frame = Frame(0x18, lbsContent, serial: 0x0031);

        DecodedMessage msg = DecodeSingle(frame);

        Assert.Equal(MessageType.Unknown, msg.MessageType);
        Assert.NotEqual(MessageType.Alarm, msg.MessageType);
        Assert.Equal("LbsExtended", msg.Fields["messageKind"]);
        Assert.Equal(false, msg.Fields["decoded"]);

        // No position, no alarm, no status was invented from these bytes.
        Assert.False(msg.Fields.ContainsKey("latitude"));
        Assert.False(msg.Fields.ContainsKey("longitude"));
        Assert.False(msg.Fields.ContainsKey("alarmCode"));
        Assert.False(msg.Fields.ContainsKey("alarmName"));
        Assert.False(msg.Fields.ContainsKey("speedKph"));

        // The raw frame is preserved verbatim, so a later decoder can be written against real captures.
        Assert.Equal(Convert.ToHexString(frame), Convert.ToHexString(msg.RawFrame.ToArray()));
    }

    /// <summary>0x16 and 0x26 remain alarms; only 0x18 was wrongly grouped with them.</summary>
    [Theory]
    [InlineData(0x16)]
    [InlineData(0x26)]
    public void The_real_alarm_protocols_still_decode_as_alarms(int protocol)
    {
        byte[] content = GpsBlock(BitPositioned | BitLatitudeNorth)
            .Concat(Convert.FromHexString("01CC0101550009C6"))
            .Concat(new byte[] { 0x46, 0x05, 0x04, 0x01, 0x02 })  // terminal, voltage, gsm, alarm=SOS, language
            .ToArray();

        DecodedMessage msg = DecodeSingle(Frame((byte)protocol, content, serial: 0x0040));

        Assert.Equal(MessageType.Alarm, msg.MessageType);
        Assert.Equal(0x01, msg.Fields["alarmCode"]);
        Assert.Equal("SOS", msg.Fields["alarmName"]);
    }

    // ── P1-07: alarm code table ────────────────────────────────────────────────

    /// <summary>
    /// The codes both authoritative sources agree on. <c>0x11</c> was previously reported as an
    /// airplane-mode guess and <c>0x13</c> as a fall; both are wrong, and <c>0x23</c> — the actual
    /// fall alarm — was unmapped entirely. A wrong alarm name is worse than an unmapped one,
    /// because a dispatcher acts on it.
    /// </summary>
    [Theory]
    [InlineData(0x00, "Normal")]
    [InlineData(0x01, "SOS")]
    [InlineData(0x02, "PowerCut")]
    [InlineData(0x03, "Vibration")]
    [InlineData(0x04, "EnterFence")]
    [InlineData(0x05, "ExitFence")]
    [InlineData(0x06, "Overspeed")]
    [InlineData(0x11, "PowerOff")]
    [InlineData(0x13, "Disassemble")]
    [InlineData(0x23, "Fall")]
    public void Alarm_codes_map_to_their_documented_names(int code, string expected)
    {
        Assert.Equal(expected, AlarmNameFor(code));
    }

    /// <summary>
    /// <c>0x10</c> and <c>0x12</c> are read differently by different authoritative sources (door and
    /// removal versus SIM change and airplane mode). Neither name is asserted; the raw code is
    /// published so a deployment that knows its hardware can map it.
    /// </summary>
    [Theory]
    [InlineData(0x10, "Vendor0x10")]
    [InlineData(0x12, "Vendor0x12")]
    public void Contested_alarm_codes_are_named_generically_rather_than_wrongly(int code, string expected)
    {
        Assert.Equal(expected, AlarmNameFor(code));
    }

    /// <summary>The raw code always accompanies the name, whatever the name turned out to be.</summary>
    [Theory]
    [InlineData(0x11)]
    [InlineData(0x10)]
    [InlineData(0x7F)]
    public void The_raw_alarm_code_is_always_published(int code)
    {
        DecodedMessage msg = DecodeSingle(StatusFrame(0x46, alarm: (byte)code));
        Assert.Equal(code, msg.Fields["alarmCode"]);
    }

    /// <summary>An entirely unknown alarm code stays unknown rather than falling into a neighbour.</summary>
    [Fact]
    public void Unknown_alarm_codes_remain_unknown()
    {
        Assert.Equal("Unknown", AlarmNameFor(0x7E));
    }

    // ── P1-12: 0x23 variant safety ─────────────────────────────────────────────

    /// <summary>
    /// A <c>0x23</c> frame shorter than the fixed status layout must decode NOTHING rather than
    /// reading whatever bytes happen to follow. Model variants of this packet exist, so the guard
    /// against a short one is what keeps an unsupported variant from silently producing fields.
    /// </summary>
    [Fact]
    public void Short_0x23_status_frame_fabricates_no_fields()
    {
        DecodedMessage msg = DecodeSingle(Frame(0x23, new byte[] { 0x46, 0x05 }, serial: 0x0050));

        Assert.Equal(MessageType.Status, msg.MessageType);
        Assert.Equal(false, msg.Fields["statusDecoded"]);
        Assert.False(msg.Fields.ContainsKey("voltageLevel"));
        Assert.False(msg.Fields.ContainsKey("alarmCode"));
        Assert.False(msg.Fields.ContainsKey("ignitionOn"));
    }

    /// <summary>A short GPS frame likewise decodes no coordinate instead of guessing one.</summary>
    [Fact]
    public void Short_location_frame_fabricates_no_coordinate()
    {
        DecodedMessage msg = DecodeSingle(Frame(0x12, new byte[] { 0x18, 0x01, 0x0F, 0x0A }, serial: 0x0051));

        Assert.Equal(MessageType.Location, msg.MessageType);
        Assert.Equal(false, msg.Fields["gpsDecoded"]);
        Assert.False(msg.Fields.ContainsKey("latitude"));
        Assert.False(msg.Fields.ContainsKey("longitude"));
    }

    // ── P1-13: 0x8A time response ──────────────────────────────────────────────

    /// <summary>
    /// A <c>0x8A</c> request is answered with the six-byte UTC body the device asked for, framed and
    /// checksummed like any other server response.
    /// </summary>
    [Fact]
    public void Time_request_0x8A_is_answered_with_utc()
    {
        var frozen = new DateTime(2026, 8, 28, 14, 5, 9, DateTimeKind.Utc);
        var adapter = new Gt06Adapter(Gt06Adapter.MaxFrameBytes, () => frozen);

        byte[] request = Frame(0x8A, Array.Empty<byte>(), serial: 0x000E);
        DecodedMessage msg = Assert.Single(adapter.Decode(request, out _));
        Assert.True(msg.RequiresAck);

        byte[] response = adapter.EncodeAck(msg);

        // 78 78 | len | 8A | YY MM DD HH MM SS | serial | crc | 0D 0A
        Assert.Equal(new byte[] { 0x78, 0x78 }, response[..2]);
        Assert.Equal(0x8A, response[3]);
        Assert.Equal(new byte[] { 26, 8, 28, 14, 5, 9 }, response[4..10]);
        Assert.Equal(new byte[] { 0x00, 0x0E }, response[10..12]);   // echoed serial
        Assert.Equal(new byte[] { 0x0D, 0x0A }, response[^2..]);

        // The response is self-consistent: its own CRC-ITU verifies over [length .. serial].
        ushort expected = Gt06Adapter.Crc16Itu(response[2..12]);
        Assert.Equal(expected, (ushort)((response[12] << 8) | response[13]));
    }

    /// <summary>A local-time clock is normalised to UTC before it is published to a device.</summary>
    [Fact]
    public void Time_response_body_is_utc_even_from_a_local_clock()
    {
        var utc = new DateTime(2026, 8, 28, 14, 5, 9, DateTimeKind.Utc);
        Assert.Equal(
            Gt06Adapter.BuildUtcTimeBody(utc),
            Gt06Adapter.BuildUtcTimeBody(utc.ToLocalTime()));
    }

    // ── P1-14: 0x15 command response ───────────────────────────────────────────

    /// <summary>
    /// A <c>0x15</c> command result is parsed as a command result — never as GPS or status — and it
    /// keeps the four-byte server flag that correlates it with the <c>0x80</c> downlink that caused it.
    /// </summary>
    [Fact]
    public void Command_response_0x15_is_parsed_without_becoming_gps_or_status()
    {
        byte[] ascii = "DYD=Success!"u8.ToArray();
        byte[] content = new byte[] { (byte)(4 + ascii.Length), 0x00, 0x00, 0x00, 0x07 }
            .Concat(ascii).ToArray();

        DecodedMessage msg = DecodeSingle(Frame(0x15, content, serial: 0x0060));

        Assert.Equal(MessageType.Ack, msg.MessageType);
        Assert.Equal("CommandResponse", msg.Fields["messageKind"]);
        Assert.Equal(true, msg.Fields["decoded"]);
        Assert.Equal("00000007", msg.Fields["serverFlag"]);
        Assert.Equal("DYD=Success!", msg.Fields["commandText"]);

        Assert.False(msg.Fields.ContainsKey("latitude"));
        Assert.False(msg.Fields.ContainsKey("speedKph"));
        Assert.False(msg.Fields.ContainsKey("ignitionOn"));
    }

    /// <summary>A declared command length that overruns the frame is reported, never read past.</summary>
    [Fact]
    public void Command_response_with_an_overrunning_length_decodes_nothing()
    {
        byte[] content = new byte[] { 0xF0, 0x00, 0x00, 0x00, 0x07, (byte)'A' };

        DecodedMessage msg = DecodeSingle(Frame(0x15, content, serial: 0x0061));

        Assert.Equal(false, msg.Fields["decoded"]);
        Assert.Equal(true, msg.Fields["commandLengthMismatch"]);
        Assert.False(msg.Fields.ContainsKey("commandText"));
    }

    // ── P1-11: acknowledgement framing ─────────────────────────────────────────

    /// <summary>
    /// An acknowledgement is written in the framing the request arrived under. A <c>0x7979</c>
    /// request answered with a <c>0x7878</c> response is a frame the device did not send us and may
    /// not parse.
    /// </summary>
    /// <remarks>
    /// This pins OUR side of the exchange only. Whether a real ACK-required <c>0x7979</c> packet
    /// exists on target hardware, and what it expects back, is recorded as CANNOT VERIFY pending a
    /// device trace — see <c>fixtures/gt06/README.md</c>. The frames below are synthetic and are
    /// labelled as such; they assert internal consistency, not device truth.
    /// </remarks>
    [Fact]
    public void Acknowledgement_mirrors_the_framing_of_the_request()
    {
        byte[] status = new byte[] { 0x46, 0x05, 0x04, 0x00, 0x02 };

        DecodedMessage standard = DecodeSingle(Frame(0x13, status, serial: 0x0007));
        Assert.Equal("7878", standard.Fields["framing"]);
        byte[] standardAck = _adapter.EncodeAck(standard);
        Assert.Equal(new byte[] { 0x78, 0x78 }, standardAck[..2]);

        DecodedMessage extended = DecodeSingle(ExtendedFrame(0x13, status, serial: 0x0007));
        Assert.Equal("7979", extended.Fields["framing"]);
        byte[] extendedAck = _adapter.EncodeAck(extended);
        Assert.Equal(new byte[] { 0x79, 0x79 }, extendedAck[..2]);

        // The 0x7979 response carries a TWO-byte length field, and its checksum verifies over
        // [length .. serial] exactly as the standard framing does.
        int packetLength = (extendedAck[2] << 8) | extendedAck[3];
        Assert.Equal(5, packetLength);                       // protocol(1) + serial(2) + crc(2)
        Assert.Equal(0x13, extendedAck[4]);
        int crcIndex = 4 + packetLength - 4;
        ushort expected = Gt06Adapter.Crc16Itu(extendedAck[2..(crcIndex + 2)]);
        Assert.Equal(expected, (ushort)((extendedAck[crcIndex + 2] << 8) | extendedAck[crcIndex + 3]));
    }

    // ── Frame builders (documented layout; never derived from the parser) ──────

    private DecodedMessage DecodeSingle(byte[] frame)
    {
        IReadOnlyList<DecodedMessage> messages = _adapter.Decode(frame, out int consumed);
        Assert.Equal(frame.Length, consumed);
        return Assert.Single(messages);
    }

    private DecodedMessage DecodeSingle(string fixtureName)
    {
        byte[] frame = FromHex(File.ReadAllText(Path.Combine(FixtureDir, fixtureName)));
        return DecodeSingle(frame);
    }

    private static string AlarmNameFor(int code) =>
        (string)new Gt06Adapter().Decode(StatusFrame(0x46, alarm: (byte)code), out _)[0].Fields["alarmName"]!;

    /// <summary>The 18-byte GPS information block: date, satellites, lat/lng, speed, course/status.</summary>
    private static byte[] GpsBlock(int courseStatus, double lat = 35.6762, double lng = 139.6503)
    {
        var block = new List<byte> { 24, 1, 15, 10, 20, 30, 0x09 };
        AppendBigEndian(block, (uint)Math.Round(Math.Abs(lat) * 1_800_000));
        AppendBigEndian(block, (uint)Math.Round(Math.Abs(lng) * 1_800_000));
        block.Add(42); // speed kph
        block.Add((byte)((courseStatus >> 8) & 0xFF));
        block.Add((byte)(courseStatus & 0xFF));
        return block.ToArray();
    }

    private static byte[] GpsFrame(int courseStatus) =>
        Frame(0x12, GpsBlock(courseStatus), serial: 0x0002);

    private static byte[] StatusFrame(byte terminalInfo, byte alarm = 0x00) =>
        Frame(0x13, new byte[] { terminalInfo, 0x05, 0x04, alarm, 0x02 }, serial: 0x0007);

    /// <summary>Builds a <c>0x7878</c> frame with a correct CRC-ITU over [length .. serial].</summary>
    private static byte[] Frame(byte protocol, byte[] content, int serial)
    {
        int packetLength = 1 + content.Length + 2 + 2;
        var crcRegion = new List<byte> { (byte)packetLength, protocol };
        crcRegion.AddRange(content);
        crcRegion.Add((byte)(serial >> 8));
        crcRegion.Add((byte)(serial & 0xFF));

        ushort crc = Gt06Adapter.Crc16Itu(crcRegion.ToArray());
        var frame = new List<byte> { 0x78, 0x78 };
        frame.AddRange(crcRegion);
        frame.Add((byte)(crc >> 8));
        frame.Add((byte)(crc & 0xFF));
        frame.Add(0x0D);
        frame.Add(0x0A);
        return frame.ToArray();
    }

    /// <summary>Builds a <c>0x7979</c> frame: same layout, two-byte length field.</summary>
    private static byte[] ExtendedFrame(byte protocol, byte[] content, int serial)
    {
        int packetLength = 1 + content.Length + 2 + 2;
        var crcRegion = new List<byte>
        {
            (byte)((packetLength >> 8) & 0xFF),
            (byte)(packetLength & 0xFF),
            protocol,
        };
        crcRegion.AddRange(content);
        crcRegion.Add((byte)(serial >> 8));
        crcRegion.Add((byte)(serial & 0xFF));

        ushort crc = Gt06Adapter.Crc16Itu(crcRegion.ToArray());
        var frame = new List<byte> { 0x79, 0x79 };
        frame.AddRange(crcRegion);
        frame.Add((byte)(crc >> 8));
        frame.Add((byte)(crc & 0xFF));
        frame.Add(0x0D);
        frame.Add(0x0A);
        return frame.ToArray();
    }

    private static void AppendBigEndian(List<byte> dst, uint value)
    {
        dst.Add((byte)((value >> 24) & 0xFF));
        dst.Add((byte)((value >> 16) & 0xFF));
        dst.Add((byte)((value >> 8) & 0xFF));
        dst.Add((byte)(value & 0xFF));
    }

    private static readonly string FixtureDir = LocateFixtureDir();

    private static string LocateFixtureDir()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            foreach (string candidate in new[]
                     {
                         Path.Combine(dir.FullName, "telematics", "fixtures", "gt06"),
                         Path.Combine(dir.FullName, "fixtures", "gt06"),
                     })
            {
                if (File.Exists(Path.Combine(candidate, "quadrant_north_east.hex")))
                    return candidate;
            }

            dir = dir.Parent;
        }

        throw new DirectoryNotFoundException(
            "Could not locate telematics/fixtures/gt06 from " + AppContext.BaseDirectory);
    }

    private static byte[] FromHex(string hex)
    {
        var clean = new string(hex.Where(Uri.IsHexDigit).ToArray());
        Assert.True(clean.Length % 2 == 0, "hex fixture has odd length");
        var bytes = new byte[clean.Length / 2];
        for (int i = 0; i < bytes.Length; i++)
            bytes[i] = byte.Parse(clean.AsSpan(i * 2, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture);
        return bytes;
    }
}
