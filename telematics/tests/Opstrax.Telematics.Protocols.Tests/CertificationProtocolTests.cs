using System.Globalization;
using Opstrax.Telematics.Contracts.Adapters;
using Opstrax.Telematics.Protocols.Gt06;

namespace Opstrax.Telematics.Protocols.Tests;

/// <summary>
/// INDEPENDENT CERTIFICATION of the GT06 decoder at candidate SHA 7bf66aa.
/// </summary>
/// <remarks>
/// <para>
/// Every expectation in this file is derived from the published GT06/Concox protocol description
/// and cross-checked against Traccar's <c>Gt06ProtocolDecoder</c>. None of it is derived from the
/// candidate's own tests or fixtures. Where the candidate's existing suite and this file overlap,
/// that is deliberate duplication: a certification pass that reuses the implementation's own
/// expectations certifies nothing.
/// </para>
/// <para>
/// Coordinates and CRCs below were produced by a separate reference decoder written in Python for
/// this review; they are pasted here as literals so the assertion cannot drift with the candidate.
/// </para>
/// </remarks>
public class CertificationProtocolTests
{
    private readonly Gt06Adapter _adapter = new();

    // ── F. CRC ────────────────────────────────────────────────────────────────

    /// <summary>
    /// The canonical CRC-16/X.25 check value. If this is wrong nothing else in the file means
    /// anything.
    /// </summary>
    [Fact]
    public void F1_Crc16X25_matches_the_canonical_check_value()
    {
        Assert.Equal(0x906E, Gt06Adapter.Crc16Itu("123456789"u8.ToArray()));
    }

    /// <summary>
    /// Wire CRC vs independently calculated CRC vs the decoder's decision, for the shipped
    /// fixtures. The expected values come from the reference decoder, not from the candidate.
    /// </summary>
    [Theory]
    [InlineData("login.hex", 0x618E, true)]
    [InlineData("location_0x12.hex", 0xA689, true)]
    [InlineData("location_0x22_7979.hex", 0xC220, true)]
    [InlineData("heartbeat_0x13.hex", 0xE324, true)]
    [InlineData("status_0x23.hex", 0xCAFA, true)]
    [InlineData("alarm_sos_0x16.hex", 0xC03A, true)]
    [InlineData("time_0x8A.hex", 0x0461, true)]
    [InlineData("unknown_protocol_0x99.hex", 0x8527, true)]
    [InlineData("quadrant_north_east.hex", 0x91BB, true)]
    [InlineData("bad_crc.hex", 0xA676, false)]   // wire 0xA676, true CRC 0xA689
    public void F2_Fixture_crc_agrees_with_an_independent_calculation(
        string fixture, int expectedWireCrc, bool shouldVerify)
    {
        byte[] frame = Fixture(fixture);

        // Re-derive the CRC region from the framing rules, independently of the decoder.
        int lengthFieldSize = frame[0] == 0x79 ? 2 : 1;
        int headerLength = 2 + lengthFieldSize;
        int packetLength = lengthFieldSize == 2 ? (frame[2] << 8) | frame[3] : frame[2];
        int crcIndex = headerLength + packetLength - 2;

        int wireCrc = (frame[crcIndex] << 8) | frame[crcIndex + 1];
        ushort calculated = Gt06Adapter.Crc16Itu(frame.AsSpan(2, crcIndex - 2));

        Assert.Equal(expectedWireCrc, wireCrc);
        Assert.Equal(shouldVerify, wireCrc == calculated);

        // And the decoder's decision must follow the arithmetic.
        IReadOnlyList<DecodedMessage> messages = _adapter.Decode(frame, out _, out FrameDecodeStats stats);
        if (shouldVerify)
        {
            Assert.Single(messages);
            Assert.Equal(0, stats.CrcFailures);
        }
        else
        {
            Assert.Empty(messages);
            Assert.Equal(1, stats.CrcFailures);
        }
    }

    /// <summary>The ACK the server emits must itself carry a CRC that independently verifies.</summary>
    [Theory]
    [InlineData("login.hex")]
    [InlineData("heartbeat_0x13.hex")]
    [InlineData("status_0x23.hex")]
    [InlineData("alarm_sos_0x16.hex")]
    public void F3_Emitted_ack_crc_verifies_independently(string fixture)
    {
        DecodedMessage message = DecodeSingle(fixture);
        Assert.True(message.RequiresAck);

        byte[] ack = _adapter.EncodeAck(message);
        Assert.NotEmpty(ack);

        int lengthFieldSize = ack[0] == 0x79 ? 2 : 1;
        int headerLength = 2 + lengthFieldSize;
        int packetLength = lengthFieldSize == 2 ? (ack[2] << 8) | ack[3] : ack[2];
        int crcIndex = headerLength + packetLength - 2;

        Assert.Equal(0x0D, ack[headerLength + packetLength]);
        Assert.Equal(0x0A, ack[headerLength + packetLength + 1]);

        ushort calculated = Gt06Adapter.Crc16Itu(ack.AsSpan(2, crcIndex - 2));
        int wire = (ack[crcIndex] << 8) | ack[crcIndex + 1];
        Assert.Equal(calculated, (ushort)wire);

        // Protocol number and serial are echoed, which is what the device correlates on.
        Assert.Equal(message.Fields["protocolNumber"], (int)ack[headerLength]);
        Assert.Equal(message.ProtocolMessageId, (ack[crcIndex - 2] << 8) | ack[crcIndex - 1]);
    }

    // ── D. GPS hemispheres ────────────────────────────────────────────────────

    /// <summary>
    /// All four quadrants, with coordinates produced by the independent reference decoder.
    /// </summary>
    [Theory]
    [InlineData("quadrant_north_east.hex", 0x142D, 35.6762, 139.6503, true, false)]
    [InlineData("quadrant_north_west.hex", 0x1C87, 40.7128, -74.0060, true, true)]
    [InlineData("quadrant_south_east.hex", 0x10E1, -33.8688, 151.2093, false, false)]
    [InlineData("quadrant_south_west.hex", 0x193B, -34.6037, -58.3816, false, true)]
    [InlineData("south_east.hex", 0x3078, -33.8688, 151.2093, false, false)]
    [InlineData("location_0x12.hex", 0x3CD9, 32.7767, -96.7970, true, true)]
    [InlineData("location_0x22_7979.hex", 0x3C5A, 51.5074, -0.1278, true, true)]
    public void D1_Every_quadrant_decodes_to_the_reference_coordinates(
        string fixture, int expectedCourseStatus, double lat, double lng, bool north, bool west)
    {
        DecodedMessage msg = DecodeSingle(fixture);

        Assert.Equal(expectedCourseStatus, msg.Fields["courseStatusWord"]);
        Assert.Equal(lat, (double)msg.Fields["latitude"]!, 4);
        Assert.Equal(lng, (double)msg.Fields["longitude"]!, 4);
        Assert.Equal(north, (bool)msg.Fields["hemisphereNorth"]!);
        Assert.Equal(west, (bool)msg.Fields["hemisphereWest"]!);
    }

    /// <summary>
    /// Exhaustive bit-level check over the whole course/status space: for every combination of the
    /// four status bits and a sample of courses, the decoder must agree with the documented table.
    /// This is stronger than any fixture set, because it cannot miss a quadrant.
    /// </summary>
    [Fact]
    public void D2_Course_status_word_matches_the_documented_bit_table_exhaustively()
    {
        var mismatches = new List<string>();

        foreach (int course in new[] { 0, 1, 90, 217, 359, 511, 1023 })
        foreach (int north in new[] { 0, 1 })
        foreach (int west in new[] { 0, 1 })
        foreach (int positioned in new[] { 0, 1 })
        foreach (int differential in new[] { 0, 1 })
        {
            int word = course
                     | (north << 10)
                     | (west << 11)
                     | (positioned << 12)
                     | (differential << 13);

            DecodedMessage msg = _adapter.Decode(GpsFrame(word), out _)[0];

            if ((int)msg.Fields["courseDeg"]! != course) mismatches.Add($"0x{word:X4} course");
            if ((bool)msg.Fields["hemisphereNorth"]! != (north == 1)) mismatches.Add($"0x{word:X4} north");
            if ((bool)msg.Fields["hemisphereWest"]! != (west == 1)) mismatches.Add($"0x{word:X4} west");
            if ((bool)msg.Fields["positioned"]! != (positioned == 1)) mismatches.Add($"0x{word:X4} positioned");
            if ((bool)msg.Fields["isDifferentialPositioning"]! != (differential == 1)) mismatches.Add($"0x{word:X4} differential");
            if ((bool)msg.Fields["realTimeGps"]! != (differential == 0)) mismatches.Add($"0x{word:X4} realtime");

            // Sign must follow the hemisphere bits, never the other way round.
            if (((double)msg.Fields["latitude"]! > 0) != (north == 1)) mismatches.Add($"0x{word:X4} lat sign");
            if (((double)msg.Fields["longitude"]! < 0) != (west == 1)) mismatches.Add($"0x{word:X4} lng sign");
        }

        Assert.True(mismatches.Count == 0,
            $"{mismatches.Count} bit-table mismatches: {string.Join(", ", mismatches.Take(10))}");
    }

    // ── E. Protocol numbers ───────────────────────────────────────────────────

    /// <summary>
    /// Classification and acknowledgement policy for every protocol number in scope. Expectations
    /// follow the vendor document and Traccar, not the candidate's own tests.
    /// </summary>
    [Theory]
    [InlineData(0x01, MessageType.Login, true, "login: server must answer or the device never registers")]
    [InlineData(0x12, MessageType.Location, false, "GPS+LBS: no server response in the documented dialect")]
    [InlineData(0x13, MessageType.Heartbeat, true, "status/heartbeat: server answers")]
    [InlineData(0x16, MessageType.Alarm, true, "GPS+LBS+status alarm: server answers")]
    [InlineData(0x18, MessageType.Unknown, false, "LBS extended (Traccar MSG_LBS_EXTEND): NOT an alarm")]
    [InlineData(0x22, MessageType.Location, false, "GPS+LBS")]
    [InlineData(0x23, MessageType.Status, true, "status/heartbeat variant (Traccar MSG_HEARTBEAT)")]
    [InlineData(0x26, MessageType.Alarm, true, "GPS+LBS+status alarm")]
    [InlineData(0x8A, MessageType.Status, true, "time request: server returns UTC")]
    [InlineData(0x99, MessageType.Unknown, false, "unassigned: must stay unknown, raw retained")]
    public void E1_Protocol_numbers_classify_and_acknowledge_per_documentation(
        int protocol, MessageType expectedType, bool expectedRequiresAck, string rationale)
    {
        // A content block long enough to satisfy every semantic decoder, so classification is what
        // is under test rather than a length guard.
        byte[] content = GpsBlock(0x142D)
            .Concat(Convert.FromHexString("01CC0101550009C6"))
            .Concat(new byte[] { 0x46, 0x05, 0x04, 0x00, 0x02 })
            .ToArray();

        DecodedMessage msg = _adapter.Decode(Frame((byte)protocol, content, 0x0007), out _)[0];

        Assert.Equal(expectedType, msg.MessageType);
        Assert.Equal(expectedRequiresAck, msg.RequiresAck);
        Assert.Equal(protocol, msg.Fields["protocolNumber"]);
        Assert.False(string.IsNullOrEmpty(rationale));
    }

    /// <summary>
    /// <c>0x18</c> must never fabricate a position or an alarm from LBS bytes. This is the specific
    /// corruption the baseline produced: cell-tower identifiers read as a coordinate.
    /// </summary>
    [Fact]
    public void E2_Protocol_0x18_produces_no_position_and_no_alarm()
    {
        byte[] lbs = Convert.FromHexString("18010F0A141E01CC0101550009C60102030405");
        DecodedMessage msg = _adapter.Decode(Frame(0x18, lbs, 0x0031), out _)[0];

        Assert.Equal(MessageType.Unknown, msg.MessageType);
        foreach (string forbidden in new[]
                 { "latitude", "longitude", "alarmCode", "alarmName", "speedKph", "courseDeg" })
        {
            Assert.False(msg.Fields.ContainsKey(forbidden),
                $"0x18 fabricated '{forbidden}' out of LBS bytes");
        }
        Assert.False(msg.RequiresAck);
    }

    /// <summary>
    /// <c>0x80</c> is a SERVER-to-device downlink. A device is not expected to send it, and it must
    /// never be read as telemetry if one does.
    /// </summary>
    [Fact]
    public void E3_Protocol_0x80_is_never_treated_as_telemetry()
    {
        DecodedMessage msg = _adapter.Decode(
            Frame(0x80, new byte[] { 0x05, 0x00, 0x00, 0x00, 0x01, 0x00, 0x02 }, 0x0009), out _)[0];

        Assert.Equal(MessageType.Ack, msg.MessageType);
        Assert.False(msg.Fields.ContainsKey("latitude"));
        Assert.False(msg.Fields.ContainsKey("alarmCode"));
    }

    /// <summary>Alarm names, for the codes the vendor document and Traccar agree on.</summary>
    [Theory]
    [InlineData(0x00, "Normal")]
    [InlineData(0x01, "SOS")]
    [InlineData(0x02, "PowerCut")]
    [InlineData(0x03, "Vibration")]
    [InlineData(0x04, "EnterFence")]
    [InlineData(0x05, "ExitFence")]
    [InlineData(0x11, "PowerOff")]
    [InlineData(0x13, "Disassemble")]
    [InlineData(0x23, "Fall")]
    public void E4_Alarm_codes_both_sources_agree_on_are_named_correctly(int code, string expected)
    {
        DecodedMessage msg = _adapter.Decode(StatusFrame(0x46, (byte)code), out _)[0];
        Assert.Equal(expected, msg.Fields["alarmName"]);
        Assert.Equal(code, msg.Fields["alarmCode"]);
    }

    /// <summary>Codes the sources contradict each other on must not be asserted as either reading.</summary>
    [Theory]
    [InlineData(0x10)]
    [InlineData(0x12)]
    public void E5_Contested_alarm_codes_assert_neither_reading(int code)
    {
        string name = (string)_adapter.Decode(StatusFrame(0x46, (byte)code), out _)[0].Fields["alarmName"]!;

        Assert.DoesNotContain("Door", name, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Sim", name, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Airplane", name, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Removing", name, StringComparison.OrdinalIgnoreCase);
        Assert.StartsWith("Vendor0x", name, StringComparison.Ordinal);
    }

    // ── Item 6: per-protocol minimum lengths ──────────────────────────────────

    /// <summary>
    /// The framing minimum is a single constant for all protocol numbers, so a frame can be
    /// well-framed and still too short for its own semantics. Every semantic decoder must therefore
    /// refuse to read past its content and must fabricate nothing.
    /// </summary>
    [Theory]
    [InlineData(0x12, 0)] [InlineData(0x12, 4)] [InlineData(0x12, 17)]
    [InlineData(0x22, 17)]
    [InlineData(0x13, 0)] [InlineData(0x13, 4)]
    [InlineData(0x23, 4)]
    [InlineData(0x16, 4)]
    [InlineData(0x26, 4)]
    [InlineData(0x01, 0)] [InlineData(0x01, 7)]
    [InlineData(0x15, 4)]
    public void G6_A_wellframed_but_undersized_frame_fabricates_nothing(int protocol, int contentBytes)
    {
        byte[] content = Enumerable.Range(0, contentBytes).Select(i => (byte)(i + 1)).ToArray();

        // Must not throw, must not read out of bounds, must not invent a field.
        DecodedMessage msg = _adapter.Decode(Frame((byte)protocol, content, 0x0011), out int consumed)[0];

        Assert.True(consumed > 0);
        Assert.False(msg.Fields.ContainsKey("latitude"), $"0x{protocol:X2} with {contentBytes}B invented a latitude");
        Assert.False(msg.Fields.ContainsKey("longitude"), $"0x{protocol:X2} with {contentBytes}B invented a longitude");

        if (contentBytes < 5)
            Assert.False(msg.Fields.ContainsKey("voltageLevel"), $"0x{protocol:X2} with {contentBytes}B invented a voltage");
    }

    // ── Item 7: IMEI BCD handling ─────────────────────────────────────────────

    /// <summary>
    /// CERT-001. A 15-digit IMEI is carried as 8 bytes of packed BCD, which is 16 nibbles, so
    /// exactly ONE leading pad nibble is removed. Removing every leading zero instead eats real
    /// digits from any IMEI that begins with one, and such a device then matches nothing in the
    /// registry or the allowlist and can never be onboarded.
    /// </summary>
    /// <remarks>
    /// The full chain is asserted: IMEI → packed BCD on the wire → decoder → the exact original
    /// IMEI. The BCD column is written out as a literal so the encoding itself is pinned, not
    /// merely the round trip through a helper that could drift with the decoder.
    /// </remarks>
    [Theory]
    [InlineData("868120303337976", "0868120303337976", "ordinary IMEI, no leading zero")]
    [InlineData("012345678901234", "0012345678901234", "IMEI beginning with a single zero")]
    [InlineData("001234567890123", "0001234567890123", "IMEI beginning with two zeros")]
    [InlineData("000000000000001", "0000000000000001", "minimal identifier, fourteen pad zeros")]
    [InlineData("999999999999999", "0999999999999999", "maximal identifier")]
    public void Item7_Decoded_imei_round_trips_the_terminal_id(string imei, string expectedBcdDigits, string why)
    {
        // IMEI -> packed BCD, asserted against the literal nibble string.
        byte[] terminalId = PackBcd(imei);
        Assert.Equal(expectedBcdDigits, Convert.ToHexString(terminalId));
        Assert.Equal(8, terminalId.Length);

        // packed BCD -> decoder -> exact original IMEI.
        DecodedMessage msg = _adapter.Decode(Frame(0x01, terminalId, 0x0001), out _)[0];

        Assert.Equal(MessageType.Login, msg.MessageType);
        Assert.NotNull(msg.Identity);
        Assert.Equal(imei, msg.Identity!.Value.Imei);
        Assert.Equal(15, msg.Identity!.Value.Imei!.Length);
        Assert.Equal(imei, msg.Fields["imei"]);
        Assert.False(string.IsNullOrEmpty(why));
    }

    /// <summary>
    /// A terminal identifier containing any non-decimal nibble is not BCD. It must fail closed —
    /// no identity at all — rather than being coerced into a plausible-looking identifier.
    /// </summary>
    /// <remarks>
    /// Emitting <c>'0' + nibble</c> for 0xA–0xF yields ':', ';', '&lt;', '=', '&gt;', '?', so a
    /// coerced value would be a garbage string that could still be looked up, and on a public port
    /// an attacker chooses those bytes.
    /// </remarks>
    [Theory]
    [InlineData("0868120303337A76", "high nibble 0xA in the last-but-one byte")]
    [InlineData("F868120303337976", "leading nibble 0xF")]
    [InlineData("086812030333797F", "trailing nibble 0xF")]
    [InlineData("FFFFFFFFFFFFFFFF", "no decimal nibble anywhere")]
    [InlineData("08681203033379B6", "single 0xB mid-identifier")]
    public void Item7_Malformed_bcd_fails_closed_with_no_identity(string terminalIdHex, string why)
    {
        byte[] terminalId = Convert.FromHexString(terminalIdHex);
        DecodedMessage msg = _adapter.Decode(Frame(0x01, terminalId, 0x0001), out _)[0];

        Assert.Equal(MessageType.Login, msg.MessageType);
        Assert.Null(msg.Identity);                       // no resolvable claim at all
        Assert.False(msg.Fields.ContainsKey("imei"));    // and nothing that looks like one
        Assert.Equal(true, msg.Fields["imeiMalformed"]);
        Assert.False(string.IsNullOrEmpty(why));
    }

    /// <summary>No decoded identifier may contain a character that is not a decimal digit.</summary>
    [Fact]
    public void Item7_A_decoded_identifier_is_always_fifteen_decimal_digits_or_absent()
    {
        for (int b = 0; b <= 0xFF; b++)
        {
            var terminalId = new byte[8];
            terminalId[3] = (byte)b;                    // vary one byte across its whole range
            DecodedMessage msg = _adapter.Decode(Frame(0x01, terminalId, 0x0001), out _)[0];

            string? imei = msg.Identity?.Imei;
            bool nibblesAreDecimal = (b >> 4) <= 9 && (b & 0x0F) <= 9;

            if (!nibblesAreDecimal)
            {
                Assert.Null(imei);
                continue;
            }

            Assert.NotNull(imei);
            Assert.Equal(15, imei!.Length);
            Assert.All(imei, c => Assert.InRange(c, '0', '9'));
        }
    }

    /// <summary>Packs a 15-digit IMEI into the 8-byte terminal identifier the protocol carries.</summary>
    private static byte[] PackBcd(string imei)
    {
        string padded = imei.PadLeft(16, '0');
        var bytes = new byte[8];
        for (int i = 0; i < 8; i++)
            bytes[i] = (byte)(((padded[i * 2] - '0') << 4) | (padded[(i * 2) + 1] - '0'));
        return bytes;
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private DecodedMessage DecodeSingle(string fixture)
    {
        byte[] frame = Fixture(fixture);
        IReadOnlyList<DecodedMessage> messages = _adapter.Decode(frame, out int consumed);
        Assert.Equal(frame.Length, consumed);
        return Assert.Single(messages);
    }

    private static byte[] GpsBlock(int courseStatus, double lat = 35.6762, double lng = 139.6503)
    {
        var b = new List<byte> { 24, 1, 15, 10, 20, 30, 0x09 };
        void Be(uint v) { b.Add((byte)(v >> 24)); b.Add((byte)(v >> 16)); b.Add((byte)(v >> 8)); b.Add((byte)v); }
        Be((uint)Math.Round(Math.Abs(lat) * 1_800_000));
        Be((uint)Math.Round(Math.Abs(lng) * 1_800_000));
        b.Add(42);
        b.Add((byte)(courseStatus >> 8));
        b.Add((byte)(courseStatus & 0xFF));
        return b.ToArray();
    }

    private static byte[] GpsFrame(int courseStatus) => Frame(0x12, GpsBlock(courseStatus), 0x0002);

    private static byte[] StatusFrame(byte terminalInfo, byte alarm) =>
        Frame(0x13, new byte[] { terminalInfo, 0x05, 0x04, alarm, 0x02 }, 0x0007);

    private static byte[] Frame(byte protocol, byte[] content, int serial)
    {
        int packetLength = 1 + content.Length + 2 + 2;
        var region = new List<byte> { (byte)packetLength, protocol };
        region.AddRange(content);
        region.Add((byte)(serial >> 8));
        region.Add((byte)(serial & 0xFF));

        ushort crc = Gt06Adapter.Crc16Itu(region.ToArray());
        var frame = new List<byte> { 0x78, 0x78 };
        frame.AddRange(region);
        frame.Add((byte)(crc >> 8));
        frame.Add((byte)(crc & 0xFF));
        frame.Add(0x0D);
        frame.Add(0x0A);
        return frame.ToArray();
    }

    private static readonly string FixtureDir = Locate();

    private static string Locate()
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
                if (File.Exists(Path.Combine(candidate, "login.hex"))) return candidate;
            }
            dir = dir.Parent;
        }
        throw new DirectoryNotFoundException("fixtures not found from " + AppContext.BaseDirectory);
    }

    private static byte[] Fixture(string name)
    {
        string hex = new(File.ReadAllText(Path.Combine(FixtureDir, name)).Where(Uri.IsHexDigit).ToArray());
        var bytes = new byte[hex.Length / 2];
        for (int i = 0; i < bytes.Length; i++)
            bytes[i] = byte.Parse(hex.AsSpan(i * 2, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture);
        return bytes;
    }
}
