using System.Globalization;
using Opstrax.Telematics.Contracts;
using Opstrax.Telematics.Contracts.Adapters;
using Opstrax.Telematics.Contracts.Identity;
using Opstrax.Telematics.Contracts.Lifecycle;
using Opstrax.Telematics.Contracts.Provenance;
using Opstrax.Telematics.Protocols.Gt06;

namespace Opstrax.Telematics.Protocols.Tests;

/// <summary>
/// Behavioural tests for <see cref="Gt06Adapter"/>. Every frame comes from a byte-accurate
/// fixture under <c>telematics/fixtures/gt06/*.hex</c> (see that folder's README for the
/// protocol-doc citations and a worked byte breakdown of each frame).
/// </summary>
public class Gt06AdapterTests
{
    private readonly Gt06Adapter _adapter = new();

    // ── Fixture loading ────────────────────────────────────────────────────────

    private static readonly string FixtureDir = LocateFixtureDir();

    private static string LocateFixtureDir()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            var candidate = Path.Combine(dir.FullName, "telematics", "fixtures", "gt06");
            if (File.Exists(Path.Combine(candidate, "login.hex")))
                return candidate;

            var candidate2 = Path.Combine(dir.FullName, "fixtures", "gt06");
            if (File.Exists(Path.Combine(candidate2, "login.hex")))
                return candidate2;

            dir = dir.Parent;
        }

        throw new DirectoryNotFoundException(
            "Could not locate telematics/fixtures/gt06 from " + AppContext.BaseDirectory);
    }

    private static byte[] Fixture(string name)
    {
        var raw = File.ReadAllText(Path.Combine(FixtureDir, name));
        return FromHex(raw);
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

    private static string ToHex(IEnumerable<byte> bytes) =>
        string.Concat(bytes.Select(b => b.ToString("X2", CultureInfo.InvariantCulture)));

    private IReadOnlyList<DecodedMessage> DecodeAll(byte[] buffer, out int consumed) =>
        _adapter.Decode(buffer, out consumed);

    private DecodedMessage DecodeSingle(string fixture)
    {
        var messages = _adapter.Decode(Fixture(fixture), out var consumed);
        Assert.Single(messages);
        Assert.Equal(Fixture(fixture).Length, consumed);
        return messages[0];
    }

    // ── CRC self-check ─────────────────────────────────────────────────────────

    [Fact]
    public void Crc16Itu_matches_the_canonical_x25_check_value()
    {
        // CRC-16/X.25 of ASCII "123456789" is the standard reference value 0x906E.
        var data = System.Text.Encoding.ASCII.GetBytes("123456789");
        Assert.Equal(0x906E, Gt06Adapter.Crc16Itu(data));
    }

    // ── Identification ─────────────────────────────────────────────────────────

    [Fact]
    public void TryIdentify_matches_both_start_markers_and_rejects_others()
    {
        Assert.True(_adapter.TryIdentify(new byte[] { 0x78, 0x78, 0x0D }).IsMatch);
        Assert.True(_adapter.TryIdentify(new byte[] { 0x79, 0x79, 0x00 }).IsMatch);
        Assert.False(_adapter.TryIdentify(new byte[] { 0x24, 0x24 }).IsMatch);

        var incomplete = _adapter.TryIdentify(new byte[] { 0x78 });
        Assert.False(incomplete.IsMatch);
        Assert.True(incomplete.NeedMoreData);
    }

    [Fact]
    public void Metadata_publishes_gt06_name_and_version()
    {
        Assert.Equal("GT06", _adapter.Metadata.Name);
        Assert.Equal("1.0.0", _adapter.Metadata.Version);
    }

    // ── Valid login ────────────────────────────────────────────────────────────

    [Fact]
    public void Decodes_valid_login_with_imei_and_requires_ack()
    {
        var msg = DecodeSingle("login.hex");

        Assert.Equal(MessageType.Login, msg.MessageType);
        Assert.True(msg.RequiresAck);
        Assert.Equal(0x0001, msg.ProtocolMessageId);

        Assert.NotNull(msg.Identity);
        Assert.Equal("868120303337976", msg.Identity!.Value.Imei);
        Assert.Equal("868120303337976", msg.Fields["imei"]);
    }

    [Fact]
    public void EncodeAck_for_login_matches_the_expected_server_response()
    {
        var login = DecodeSingle("login.hex");
        var ack = _adapter.EncodeAck(login);

        Assert.Equal(ToHex(Fixture("login_ack.hex")), ToHex(ack));
        // Structure: 78 78 05 01 <serialHi serialLo> <crcHi crcLo> 0D 0A
        Assert.Equal(new byte[] { 0x78, 0x78, 0x05, 0x01, 0x00, 0x01 }, ack[..6]);
        Assert.Equal(new byte[] { 0x0D, 0x0A }, ack[^2..]);
    }

    // ── Valid location ─────────────────────────────────────────────────────────

    [Fact]
    public void Decodes_valid_location_0x12_fields()
    {
        var msg = DecodeSingle("location_0x12.hex");

        Assert.Equal(MessageType.Location, msg.MessageType);
        Assert.Equal(new DateTime(2024, 1, 15, 10, 20, 30, DateTimeKind.Utc), msg.Fields["fixTimeUtc"]);
        Assert.Equal(9, msg.Fields["satellites"]);
        Assert.Equal(32.7767, (double)msg.Fields["latitude"]!, 4);
        Assert.Equal(-96.7970, (double)msg.Fields["longitude"]!, 4);
        Assert.Equal(60, msg.Fields["speedKph"]);
        Assert.Equal(217, msg.Fields["courseDeg"]);
        Assert.True((bool)msg.Fields["positioned"]!);
        Assert.True((bool)msg.Fields["realTimeGps"]!);
        Assert.True((bool)msg.Fields["coordinatesValid"]!);
        Assert.True((bool)msg.Fields["hemisphereNorth"]!);
        Assert.True((bool)msg.Fields["hemisphereWest"]!);
    }

    [Fact]
    public void Decodes_location_0x22_under_the_two_byte_length_7979_framing()
    {
        var msg = DecodeSingle("location_0x22_7979.hex");

        Assert.Equal(MessageType.Location, msg.MessageType);
        Assert.Equal(51.5074, (double)msg.Fields["latitude"]!, 3);
        Assert.Equal(-0.1278, (double)msg.Fields["longitude"]!, 3);
        Assert.Equal(0x000A, msg.ProtocolMessageId);
    }

    [Fact]
    public void Decodes_south_and_east_hemispheres_from_the_status_word()
    {
        var msg = DecodeSingle("south_east.hex");

        Assert.Equal(-33.8688, (double)msg.Fields["latitude"]!, 3);   // South -> negative
        Assert.Equal(151.2093, (double)msg.Fields["longitude"]!, 3);  // East  -> positive
        Assert.False((bool)msg.Fields["hemisphereNorth"]!);
        Assert.False((bool)msg.Fields["hemisphereWest"]!);
    }

    // ── Status / heartbeat ─────────────────────────────────────────────────────

    [Fact]
    public void Decodes_heartbeat_0x13_status_and_requires_ack()
    {
        var msg = DecodeSingle("heartbeat_0x13.hex");

        Assert.Equal(MessageType.Heartbeat, msg.MessageType);
        Assert.True(msg.RequiresAck);
        Assert.Equal(5, msg.Fields["voltageLevel"]);
        Assert.Equal(4, msg.Fields["gsmSignal"]);
        Assert.Equal(0, msg.Fields["alarmCode"]);
        Assert.Equal("Normal", msg.Fields["alarmName"]);
        Assert.True((bool)msg.Fields["ignitionOn"]!);
        Assert.True((bool)msg.Fields["charging"]!);
    }

    [Fact]
    public void EncodeAck_for_heartbeat_matches_the_expected_server_response()
    {
        var hb = DecodeSingle("heartbeat_0x13.hex");
        var ack = _adapter.EncodeAck(hb);
        Assert.Equal(ToHex(Fixture("heartbeat_ack.hex")), ToHex(ack));
    }

    [Fact]
    public void Decodes_status_0x23_as_status_message()
    {
        var msg = DecodeSingle("status_0x23.hex");
        Assert.Equal(MessageType.Status, msg.MessageType);
        Assert.Equal(6, msg.Fields["voltageLevel"]);
        Assert.Equal(3, msg.Fields["gsmSignal"]);
    }

    // ── Alarm ──────────────────────────────────────────────────────────────────

    [Fact]
    public void Decodes_sos_alarm_0x16_with_position_and_alarm_code()
    {
        var msg = DecodeSingle("alarm_sos_0x16.hex");

        Assert.Equal(MessageType.Alarm, msg.MessageType);
        Assert.Equal(0x01, msg.Fields["alarmCode"]);
        Assert.Equal("SOS", msg.Fields["alarmName"]);
        Assert.True(msg.RequiresAck);
        // Alarm frames still carry a GPS fix.
        Assert.Equal(32.7767, (double)msg.Fields["latitude"]!, 4);
    }

    // ── Time / command / unknown ───────────────────────────────────────────────

    [Fact]
    public void Decodes_time_0x8A_as_a_known_non_location_message()
    {
        var msg = DecodeSingle("time_0x8A.hex");
        Assert.NotEqual(MessageType.Unknown, msg.MessageType);
        Assert.Equal("TimeSync", msg.Fields["messageKind"]);
    }

    [Fact]
    public void Unknown_protocol_number_yields_Unknown_and_retains_raw_frame()
    {
        var raw = Fixture("unknown_protocol_0x99.hex");
        var msg = DecodeSingle("unknown_protocol_0x99.hex");

        Assert.Equal(MessageType.Unknown, msg.MessageType);
        // Raw bytes preserved verbatim for audit / forensic re-decode.
        Assert.Equal(ToHex(raw), ToHex(msg.RawFrame));
    }

    // ── Malformed / bad CRC / truncated ────────────────────────────────────────

    [Fact]
    public void Malformed_length_throws_ProtocolException_and_does_not_crash()
    {
        var buffer = Fixture("malformed_length.hex");
        var ex = Assert.Throws<ProtocolException>(() => _adapter.Decode(buffer, out _));
        Assert.Equal("GT06", ex.AdapterName);
    }

    [Fact]
    public void Bad_crc_frame_is_rejected_without_throwing_and_emits_no_message()
    {
        var buffer = Fixture("bad_crc.hex");

        var messages = _adapter.Decode(buffer, out var consumed);

        Assert.Empty(messages);                 // rejected, not surfaced
        Assert.Equal(buffer.Length, consumed);  // frame skipped, buffer drained
    }

    [Fact]
    public void Truncated_frame_reports_needs_more_and_consumes_nothing()
    {
        var buffer = Fixture("truncated.hex");

        var messages = _adapter.Decode(buffer, out var consumed);

        Assert.Empty(messages);
        Assert.Equal(0, consumed); // caller retains all bytes and appends the next read
    }

    [Fact]
    public void Truncated_frame_completes_once_the_rest_of_the_bytes_arrive()
    {
        var full = Fixture("login.hex");
        var partial = Fixture("truncated.hex");

        // First pass: partial buffer -> nothing consumed.
        Assert.Empty(_adapter.Decode(partial, out var c1));
        Assert.Equal(0, c1);

        // Second pass: the completed buffer decodes fully.
        var messages = _adapter.Decode(full, out var c2);
        Assert.Single(messages);
        Assert.Equal(full.Length, c2);
        Assert.Equal(MessageType.Login, messages[0].MessageType);
    }

    // ── Duplicate serial / out of order ────────────────────────────────────────

    [Fact]
    public void Duplicate_serial_is_reported_faithfully_for_downstream_idempotency()
    {
        var first = DecodeSingle("location_0x12.hex");
        var dup = DecodeSingle("duplicate_serial.hex");

        // The decoder does not dedupe (that is a normalization concern); it must faithfully
        // report the identical serial so the gateway can detect the replay/duplicate.
        Assert.Equal(first.ProtocolMessageId, dup.ProtocolMessageId);
        Assert.Equal(0x0002, dup.ProtocolMessageId);
    }

    [Fact]
    public void Out_of_order_frame_reports_its_own_earlier_device_time_and_higher_serial()
    {
        var inOrder = DecodeSingle("location_0x12.hex");   // 10:20:30, serial 0x0002
        var outOfOrder = DecodeSingle("out_of_order.hex"); // 10:19:00, serial 0x0003

        var t1 = (DateTime)inOrder.Fields["fixTimeUtc"]!;
        var t2 = (DateTime)outOfOrder.Fields["fixTimeUtc"]!;

        Assert.True(t2 < t1);                                  // earlier device time
        Assert.True(outOfOrder.ProtocolMessageId > inOrder.ProtocolMessageId); // later serial
    }

    // ── Invalid coordinates / impossible speed ─────────────────────────────────

    [Fact]
    public void Invalid_coordinates_are_surfaced_verbatim_and_flagged_not_dropped()
    {
        var msg = DecodeSingle("invalid_coordinates.hex");

        Assert.Equal(MessageType.Location, msg.MessageType);
        Assert.False((bool)msg.Fields["coordinatesValid"]!);
        // Raw out-of-range magnitude is still representable (plausibility is downstream).
        Assert.True((double)msg.Fields["latitude"]! > 90.0);
    }

    [Fact]
    public void Extreme_speed_is_decoded_verbatim_and_never_silently_clamped()
    {
        var msg = DecodeSingle("extreme_speed.hex");
        Assert.Equal(255, msg.Fields["speedKph"]);
    }

    // ── Multi-frame buffer ─────────────────────────────────────────────────────

    [Fact]
    public void Multiple_frames_in_one_buffer_decode_in_wire_order()
    {
        var buffer = Fixture("multi_frame.hex");

        var messages = _adapter.Decode(buffer, out var consumed);

        Assert.Equal(3, messages.Count);
        Assert.Equal(MessageType.Login, messages[0].MessageType);
        Assert.Equal(MessageType.Location, messages[1].MessageType);
        Assert.Equal(MessageType.Heartbeat, messages[2].MessageType);
        Assert.Equal(buffer.Length, consumed);
    }

    [Fact]
    public void A_bad_crc_frame_between_good_frames_is_skipped_without_losing_the_others()
    {
        var good1 = Fixture("login.hex");
        var bad = Fixture("bad_crc.hex");
        var good2 = Fixture("heartbeat_0x13.hex");
        var buffer = good1.Concat(bad).Concat(good2).ToArray();

        var messages = _adapter.Decode(buffer, out var consumed);

        Assert.Equal(2, messages.Count);
        Assert.Equal(MessageType.Login, messages[0].MessageType);
        Assert.Equal(MessageType.Heartbeat, messages[1].MessageType);
        Assert.Equal(buffer.Length, consumed);
    }

    // ── IMEI is a claim, never a tenant ────────────────────────────────────────

    [Fact]
    public void Imei_is_not_trusted_as_tenant_ownership_comes_only_from_the_registry()
    {
        // Decode a real login so the IMEI claim is present.
        var login = DecodeSingle("login.hex");
        var claimedImei = login.Identity!.Value.Imei;
        Assert.Equal("868120303337976", claimedImei);

        // Registry-resolved owner — deliberately unrelated to the IMEI.
        var tenant = Guid.Parse("11111111-2222-3333-4444-555555555555");
        var owner = new ResolvedDeviceOwner(
            TenantId: tenant,
            CompanyId: 42,
            DeviceId: "device-abc-987",
            VehicleId: 7,
            LifecycleState: DeviceLifecycleState.Online,
            CredentialHandle: "vault://psk/abc");

        var location = DecodeSingle("location_0x12.hex");
        var evt = _adapter.ToCanonicalEvent(
            location, owner,
            receivedAtGatewayUtc: new DateTime(2024, 1, 15, 10, 20, 31, DateTimeKind.Utc),
            correlationId: Guid.NewGuid());

        // Ownership is the registry's, NEVER the packet's IMEI.
        Assert.Equal(tenant, evt.TenantId);
        Assert.Equal(42, evt.CompanyId);
        Assert.Equal("device-abc-987", evt.DeviceId);
        Assert.Equal(7, evt.VehicleId);

        // The IMEI must not have leaked into any ownership field.
        Assert.NotEqual(claimedImei, evt.DeviceId);
        Assert.NotEqual(claimedImei, evt.CompanyId.ToString(CultureInfo.InvariantCulture));
        Assert.NotEqual(claimedImei, evt.TenantId.ToString());
    }

    [Fact]
    public void ToCanonicalEvent_stamps_gt06_provenance_and_maps_the_fix()
    {
        var owner = new ResolvedDeviceOwner(
            Guid.NewGuid(), 1, "dev-1", null, DeviceLifecycleState.Online, "h");
        var location = DecodeSingle("location_0x12.hex");

        var evt = _adapter.ToCanonicalEvent(
            location, owner,
            receivedAtGatewayUtc: DateTime.UtcNow,
            correlationId: Guid.NewGuid());

        Assert.Equal(TelemetrySource.DirectDevice, evt.Source);
        Assert.Equal(Transport.Tcp, evt.Transport);
        Assert.Equal("GT06", evt.ProtocolName);
        Assert.Equal("GT06", evt.AdapterName);
        Assert.Equal("1.0.0", evt.AdapterVersion);
        Assert.Equal(CanonicalTelemetryEvent.CurrentSchemaVersion, evt.SchemaVersion);

        Assert.NotNull(evt.Location);
        Assert.Equal(32.7767, evt.Location!.Value.Lat, 4);
        Assert.Equal(-96.7970, evt.Location!.Value.Lng, 4);
        Assert.Equal(9, evt.Location!.Value.Satellites);
        Assert.Equal(60, evt.Location!.Value.SpeedKph);
        Assert.Equal(217, evt.Location!.Value.HeadingDeg);
        Assert.Equal(new DateTime(2024, 1, 15, 10, 20, 30, DateTimeKind.Utc), evt.OccurredAtDeviceUtc);
    }

    // ── Ack / command negatives ────────────────────────────────────────────────

    [Fact]
    public void EncodeAck_returns_empty_for_a_frame_that_needs_no_ack()
    {
        var location = DecodeSingle("location_0x12.hex");
        Assert.False(location.RequiresAck);
        Assert.Empty(_adapter.EncodeAck(location));
    }

    [Fact]
    public void EncodeCommand_returns_null_when_no_command_text_is_supplied()
    {
        var cmd = new DeviceCommand("EngineCutoff", new Dictionary<string, string>());
        Assert.Null(_adapter.EncodeCommand(cmd));
    }

    [Fact]
    public void EncodeCommand_builds_a_valid_0x80_frame_when_given_command_text()
    {
        var cmd = new DeviceCommand("Custom", new Dictionary<string, string>
        {
            ["text"] = "DWXX#",
            ["serial"] = "5",
        });

        var frame = _adapter.EncodeCommand(cmd);
        Assert.NotNull(frame);
        Assert.Equal(0x78, frame![0]);
        Assert.Equal(0x78, frame[1]);
        Assert.Equal(0x80, frame[3]);          // protocol number 0x80
        Assert.Equal(0x0D, frame[^2]);
        Assert.Equal(0x0A, frame[^1]);

        // The frame we just built must round-trip through our own decoder with a valid CRC.
        var messages = _adapter.Decode(frame, out var consumed);
        Assert.Single(messages);
        Assert.Equal(frame.Length, consumed);
        Assert.Equal(MessageType.Ack, messages[0].MessageType);
        Assert.Equal(5, messages[0].ProtocolMessageId);
    }

    [Fact]
    public void A_hostile_garbage_buffer_never_throws_anything_but_ProtocolException()
    {
        // No valid start marker anywhere -> impossible framing -> ProtocolException (not a crash).
        var garbage = new byte[] { 0x00, 0x11, 0x22, 0x33, 0x44, 0x55, 0x66, 0x77 };
        Assert.Throws<ProtocolException>(() => _adapter.Decode(garbage, out _));
    }
}
