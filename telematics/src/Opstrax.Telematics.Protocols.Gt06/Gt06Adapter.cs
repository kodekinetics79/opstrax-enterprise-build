using System.Globalization;
using System.Text;
using Opstrax.Telematics.Contracts;
using Opstrax.Telematics.Contracts.Adapters;
using Opstrax.Telematics.Contracts.Identity;
using Opstrax.Telematics.Contracts.Provenance;
using Opstrax.Telematics.Contracts.Signals;

namespace Opstrax.Telematics.Protocols.Gt06;

/// <summary>
/// A real, standards-faithful decoder for the GT06 / Concox / Jimi-family device
/// protocol (the wire dialect spoken by GT06N, GT06E, TR06, GT02, and many
/// OEM-relabelled variants).
/// </summary>
/// <remarks>
/// <para>
/// <b>Wire framing.</b> A GT06 packet is
/// <c>StartBits | PacketLength | ProtocolNumber | Information | InfoSerial | ErrorCheck | StopBits</c>.
/// Two start markers exist: <c>0x78 0x78</c> uses a <b>1-byte</b> length field and
/// <c>0x79 0x79</c> uses a <b>2-byte</b> length field. Stop bits are always
/// <c>0x0D 0x0A</c>. <c>PacketLength</c> counts every byte from the protocol number
/// through the error-check field inclusive (i.e. protocol(1) + information(N) +
/// serial(2) + errorcheck(2)).
/// </para>
/// <para>
/// <b>Checksum.</b> The <c>ErrorCheck</c> is CRC-ITU (a.k.a. CRC-16/X.25: poly 0x1021
/// reflected = 0x8408, init 0xFFFF, refin/refout, xorout 0xFFFF), computed over the
/// bytes from the length field through the information serial number inclusive — i.e.
/// everything between the start bits and the error-check field. See
/// <see cref="Crc16Itu"/>.
/// </para>
/// <para>
/// <b>Purity &amp; safety.</b> This adapter holds no per-connection state and is safe to
/// share as a singleton. Every decode path is total: a hostile or corrupt buffer yields
/// a rejected frame, a needs-more-data signal, or a <see cref="ProtocolException"/> —
/// never an unhandled exception that could tear down the host process.
/// </para>
/// <para>
/// <b>Documentation sources.</b> The frame layout, protocol numbers, GPS/status field
/// packing and CRC-ITU definition follow the public "GT06 Protocol" / "Concox GT06N
/// communication protocol" specification, cross-checked against the open-source Traccar
/// <c>Gt06ProtocolDecoder</c> (Apache-2.0) and the community <c>node-gt06</c> decoder.
/// See <c>telematics/fixtures/gt06/README.md</c> for the exact citations and worked
/// byte-by-byte examples for every fixture.
/// </para>
/// </remarks>
public sealed class Gt06Adapter : IProtocolAdapter
{
    /// <summary>The stable adapter/protocol name written into provenance.</summary>
    public const string ProtocolName = "GT06";

    /// <summary>Semantic version of this decoder implementation (distinct from the wire protocol version).</summary>
    public const string AdapterVersion = "1.0.0";

    /// <summary>
    /// Default hard ceiling on a single frame's total size. Standard GT06 GPS/status/alarm frames
    /// are well under 100 bytes; the 0x7979 length field can nominally claim up to 65 535
    /// content bytes, so we bound it to protect the gateway from a hostile length header.
    /// A claimed frame larger than this is treated as impossible framing.
    /// </summary>
    /// <remarks>
    /// This is only the <b>default</b>. The effective ceiling is per-instance and set through the
    /// constructor so the gateway can drive it from a single configuration source
    /// (<c>GatewayOptions.MaxFrameBytes</c>) rather than letting a hardcoded constant silently
    /// diverge from what the reassembly buffer is actually bounded to.
    /// </remarks>
    public const int MaxFrameBytes = 2048;

    /// <summary>The effective per-frame ceiling for this instance (see <see cref="MaxFrameBytes"/>).</summary>
    private readonly int _maxFrameBytes;

    /// <summary>
    /// Source of the UTC instant published in a <c>0x8A</c> time response. Injectable purely so the
    /// response can be asserted byte-for-byte in a test; the adapter still holds no protocol state.
    /// </summary>
    private readonly Func<DateTime> _utcNow;

    /// <summary>Creates the adapter with the default per-frame ceiling (<see cref="MaxFrameBytes"/>).</summary>
    public Gt06Adapter()
        : this(MaxFrameBytes)
    {
    }

    /// <summary>
    /// Creates the adapter with an explicit per-frame size ceiling. The gateway passes
    /// <c>GatewayOptions.MaxFrameBytes</c> here so the decoder's frame bound and the connection's
    /// reassembly-buffer bound come from one place and cannot drift apart.
    /// </summary>
    /// <param name="maxFrameBytes">The largest total frame size to admit. Must be positive.</param>
    /// <param name="utcNow">
    /// Optional UTC clock used only to fill a <c>0x8A</c> time response. Defaults to the system
    /// clock; tests pass a fixed instant so the response frame is deterministic.
    /// </param>
    public Gt06Adapter(int maxFrameBytes, Func<DateTime>? utcNow = null)
    {
        if (maxFrameBytes <= 0)
            throw new ArgumentOutOfRangeException(nameof(maxFrameBytes), maxFrameBytes,
                "Per-frame ceiling must be positive.");
        _maxFrameBytes = maxFrameBytes;
        _utcNow = utcNow ?? (() => DateTime.UtcNow);
    }

    // GT06 constant markers.
    private const byte Start1 = 0x78; // 0x7878 -> 1-byte length
    private const byte Start2 = 0x79; // 0x7979 -> 2-byte length
    private const byte Stop1 = 0x0D;
    private const byte Stop2 = 0x0A;

    // Protocol numbers we map to first-class semantics.
    private const byte ProtoLogin = 0x01;
    private const byte ProtoLocation12 = 0x12;
    private const byte ProtoLocation22 = 0x22;
    private const byte ProtoStatus13 = 0x13;
    private const byte ProtoStatus23 = 0x23;
    private const byte ProtoAlarm16 = 0x16;
    private const byte ProtoAlarm26 = 0x26;

    /// <summary>
    /// LBS multiple-base-station extended information — <b>not</b> an alarm. Traccar's
    /// <c>Gt06ProtocolDecoder</c> names this <c>MSG_LBS_EXTEND</c> and deliberately excludes it from
    /// its <c>hasGps()</c> set. Running the GPS/alarm layout over its cell-tower payload fabricates a
    /// position and an alarm code out of MCC/MNC/LAC/CellId bytes, so this decoder retains the raw
    /// frame and decodes nothing. See <c>fixtures/gt06/README.md</c>.
    /// </summary>
    private const byte ProtoLbsExtended18 = 0x18;

    /// <summary>Device→server string / command result (Traccar <c>MSG_STRING</c>). Never GPS or status.</summary>
    private const byte ProtoCommandResponse15 = 0x15;

    private const byte ProtoTime8A = 0x8A;
    private const byte ProtoCommand80 = 0x80;

    // Minimum PacketLength value: protocol(1) + serial(2) + errorcheck(2).
    private const int MinPacketLength = 5;

    /// <inheritdoc />
    public AdapterMetadata Metadata { get; } = new(
        Name: ProtocolName,
        Version: AdapterVersion,
        SupportedModels: new[] { "GT06", "GT06N", "GT06E", "TR06", "GT02", "Concox-compatible" },
        SupportedFirmware: Array.Empty<string>());

    /// <inheritdoc />
    public ProtocolMatch TryIdentify(ReadOnlySpan<byte> opening)
    {
        if (opening.Length < 2)
            return ProtocolMatch.Incomplete();

        if (opening[0] == Start1 && opening[1] == Start1)
            return ProtocolMatch.Match(confidence: 0.95);
        if (opening[0] == Start2 && opening[1] == Start2)
            return ProtocolMatch.Match(confidence: 0.95);

        return ProtocolMatch.NoMatch();
    }

    /// <inheritdoc />
    /// <remarks>
    /// Decodes every complete frame at the head of <paramref name="buffer"/>:
    /// <list type="bullet">
    ///   <item><description>A truncated trailing frame stops the loop; its bytes are left
    ///     unconsumed so the gateway can append the next read and retry.</description></item>
    ///   <item><description>A frame whose CRC does not verify is <b>rejected</b> — skipped
    ///     over (its declared length is consumed) and no message is emitted for it — without
    ///     throwing, so one corrupt frame cannot poison a batch.</description></item>
    ///   <item><description>Impossible framing (bad start marker, a length below the
    ///     protocol minimum, a length exceeding <see cref="MaxFrameBytes"/>, or missing
    ///     stop bits) throws <see cref="ProtocolException"/> — a fail-closed,
    ///     drop-the-connection condition, never a fabricated event.</description></item>
    /// </list>
    /// </remarks>
    public IReadOnlyList<DecodedMessage> Decode(ReadOnlySpan<byte> buffer, out int consumed) =>
        Decode(buffer, out consumed, out _);

    /// <inheritdoc />
    /// <remarks>
    /// The statistics overload. <see cref="FrameDecodeStats.FramesRead"/> counts every complete,
    /// well-framed frame this call stepped over — CRC-valid <em>and</em> CRC-invalid — so
    /// <c>FramesRead - CrcFailures</c> is exactly the number of returned messages. A truncated
    /// trailing frame is not yet a frame and is not counted; it is counted on the later call that
    /// completes it. Malformed framing throws before any count is reported for the offending frame.
    /// </remarks>
    public IReadOnlyList<DecodedMessage> Decode(ReadOnlySpan<byte> buffer, out int consumed, out FrameDecodeStats stats)
    {
        consumed = 0;
        int framesRead = 0;
        int crcFailures = 0;
        List<DecodedMessage>? messages = null;

        while (consumed < buffer.Length)
        {
            var remaining = buffer[consumed..];
            var status = TryReadFrame(remaining, out var frameLength, out var message);

            switch (status)
            {
                case FrameStatus.NeedMore:
                    // Leave the partial frame unconsumed and wait for more bytes.
                    stats = new FrameDecodeStats(framesRead, crcFailures);
                    return (IReadOnlyList<DecodedMessage>?)messages ?? Array.Empty<DecodedMessage>();

                case FrameStatus.Malformed:
                    throw new ProtocolException(
                        "GT06 frame is malformed beyond recovery (bad framing/length/stop bits).",
                        ProtocolName,
                        offset: consumed);

                case FrameStatus.BadCrc:
                    // Reject this frame but do NOT throw: consume its declared span and continue.
                    // It IS a frame we read off the wire, so it counts as an attempt — and as the
                    // CRC failure that explains why no message came out of it.
                    framesRead++;
                    crcFailures++;
                    consumed += frameLength;
                    continue;

                case FrameStatus.Ok:
                    framesRead++;
                    messages ??= new List<DecodedMessage>();
                    messages.Add(message!);
                    consumed += frameLength;
                    continue;
            }
        }

        stats = new FrameDecodeStats(framesRead, crcFailures);
        return (IReadOnlyList<DecodedMessage>?)messages ?? Array.Empty<DecodedMessage>();
    }

    private enum FrameStatus
    {
        Ok,
        NeedMore,
        BadCrc,
        Malformed,
    }

    /// <summary>
    /// Attempts to read exactly one frame from the head of <paramref name="span"/>.
    /// </summary>
    private FrameStatus TryReadFrame(ReadOnlySpan<byte> span, out int frameLength, out DecodedMessage? message)
    {
        frameLength = 0;
        message = null;

        if (span.Length < 2)
            return FrameStatus.NeedMore;

        int lengthFieldSize;
        int packetLength;
        int headerLength; // start bits + length field

        if (span[0] == Start1 && span[1] == Start1)
        {
            lengthFieldSize = 1;
            headerLength = 2 + 1;
            if (span.Length < headerLength)
                return FrameStatus.NeedMore;
            packetLength = span[2];
        }
        else if (span[0] == Start2 && span[1] == Start2)
        {
            lengthFieldSize = 2;
            headerLength = 2 + 2;
            if (span.Length < headerLength)
                return FrameStatus.NeedMore;
            packetLength = (span[2] << 8) | span[3];
        }
        else
        {
            // The buffer does not begin with a recognised start marker: unrecoverable.
            return FrameStatus.Malformed;
        }

        // PacketLength covers protocol(1) + info(N) + serial(2) + crc(2).
        if (packetLength < MinPacketLength)
            return FrameStatus.Malformed;

        // total = start(2) + lengthField + packetLength + stop(2)
        int totalFrameLength = 2 + lengthFieldSize + packetLength + 2;
        if (totalFrameLength > _maxFrameBytes)
            return FrameStatus.Malformed;

        if (span.Length < totalFrameLength)
            return FrameStatus.NeedMore;

        // Stop bits must sit exactly where PacketLength says the frame ends.
        int stopIndex = headerLength + packetLength;
        if (span[stopIndex] != Stop1 || span[stopIndex + 1] != Stop2)
            return FrameStatus.Malformed;

        // CRC-ITU covers [length field .. serial] inclusive: from index 2 up to the 2 CRC bytes.
        int crcIndex = headerLength + packetLength - 2;
        var crcRegion = span[2..crcIndex];
        ushort expectedCrc = (ushort)((span[crcIndex] << 8) | span[crcIndex + 1]);
        ushort actualCrc = Crc16Itu(crcRegion);
        if (expectedCrc != actualCrc)
        {
            frameLength = totalFrameLength; // known length: caller can skip the rejected frame
            return FrameStatus.BadCrc;
        }

        byte protocolNumber = span[headerLength];
        var content = span[(headerLength + 1)..(crcIndex - 2)]; // between protocol and serial
        int serial = (span[crcIndex - 2] << 8) | span[crcIndex - 1];
        var rawFrame = span[..totalFrameLength];

        frameLength = totalFrameLength;
        message = BuildMessage(protocolNumber, content, serial, rawFrame, extendedFraming: lengthFieldSize == 2);
        return FrameStatus.Ok;
    }

    private DecodedMessage BuildMessage(
        byte protocolNumber,
        ReadOnlySpan<byte> content,
        int serial,
        ReadOnlySpan<byte> rawFrame,
        bool extendedFraming)
    {
        var fields = new Dictionary<string, object?>
        {
            ["protocolNumber"] = (int)protocolNumber,
            ["serial"] = serial,
            // Which start marker carried this frame. The server answer mirrors it, so an ACK is
            // never written back in a framing the device did not use.
            ["framing"] = extendedFraming ? "7979" : "7878",
            ["extendedFraming"] = extendedFraming,
        };

        switch (protocolNumber)
        {
            case ProtoLogin:
                return BuildLogin(content, serial, rawFrame, fields);

            case ProtoLocation12:
            case ProtoLocation22:
                DecodeGps(content, fields);
                return new DecodedMessage(MessageType.Location, rawFrame, fields, protocolMessageId: serial, requiresAck: false);

            case ProtoStatus13:
                DecodeStatus(content, fields);
                return new DecodedMessage(MessageType.Heartbeat, rawFrame, fields, protocolMessageId: serial, requiresAck: true);

            case ProtoStatus23:
                DecodeStatus(content, fields);
                return new DecodedMessage(MessageType.Status, rawFrame, fields, protocolMessageId: serial, requiresAck: true);

            case ProtoAlarm16:
            case ProtoAlarm26:
                DecodeAlarm(content, fields);
                return new DecodedMessage(MessageType.Alarm, rawFrame, fields, protocolMessageId: serial, requiresAck: true);

            case ProtoLbsExtended18:
                // LBS multiple-base-station extended information. The frame is well-formed and its
                // checksum verified, but its content is cell-tower data whose per-model layout this
                // decoder does not claim to know. Decoding NOTHING is the only safe answer: the GPS
                // and alarm layouts would happily read MCC/MNC/LAC/CellId bytes as a coordinate and
                // an alarm code and hand the fleet a fabricated position. Raw frame retained.
                fields["messageKind"] = "LbsExtended";
                fields["decoded"] = false;
                fields["undecodedReason"] = "GT06 0x18 LBS extended layout is model-specific and unverified.";
                return new DecodedMessage(MessageType.Unknown, rawFrame, fields, protocolMessageId: serial, requiresAck: false);

            case ProtoCommandResponse15:
                DecodeCommandResponse(content, fields);
                return new DecodedMessage(MessageType.Ack, rawFrame, fields, protocolMessageId: serial, requiresAck: false);

            case ProtoTime8A:
                // The device is asking the server for UTC. Answering is the whole point of the
                // packet, so it requires a response; EncodeAck builds the 6-byte UTC body.
                fields["messageKind"] = "TimeSync";
                return new DecodedMessage(MessageType.Status, rawFrame, fields, protocolMessageId: serial, requiresAck: true);

            case ProtoCommand80:
                fields["messageKind"] = "Command";
                return new DecodedMessage(MessageType.Ack, rawFrame, fields, protocolMessageId: serial, requiresAck: false);

            default:
                // Well-framed, CRC-valid, but semantics unmapped: retain raw, do not guess.
                fields["messageKind"] = "Unknown";
                return new DecodedMessage(MessageType.Unknown, rawFrame, fields, protocolMessageId: serial, requiresAck: false);
        }
    }

    /// <summary>
    /// Decodes a device→server string / command result (protocol <c>0x15</c>, Traccar's
    /// <c>MSG_STRING</c>): <c>LengthOfCommand(1) | ServerFlagBit(4) | ASCII content</c> — the exact
    /// inverse of the <c>0x80</c> downlink this adapter encodes.
    /// </summary>
    /// <remarks>
    /// The server flag is echoed back verbatim by the device, so it is retained as the correlation
    /// token for the command that produced this reply. Nothing here is ever treated as GPS or
    /// status: a command result carries no position, and inventing one from its ASCII would put a
    /// fabricated fix on the map. A frame too short to hold the fixed header decodes nothing.
    /// </remarks>
    private static void DecodeCommandResponse(ReadOnlySpan<byte> content, Dictionary<string, object?> fields)
    {
        fields["messageKind"] = "CommandResponse";

        // LengthOfCommand(1) + ServerFlagBit(4) is the smallest header that can be present.
        if (content.Length < 5)
        {
            fields["decoded"] = false;
            return;
        }

        int declaredLength = content[0];
        uint serverFlag = ReadUInt32(content.Slice(1, 4));

        // declaredLength counts the 4 server-flag bytes plus the ASCII payload.
        int asciiLength = declaredLength - 4;
        int available = content.Length - 5;
        if (asciiLength < 0 || asciiLength > available)
        {
            // The declared length disagrees with the frame. Report the disagreement rather than
            // reading past it or silently trusting the smaller number.
            fields["decoded"] = false;
            fields["commandLengthMismatch"] = true;
            fields["serverFlag"] = serverFlag.ToString("X8", CultureInfo.InvariantCulture);
            return;
        }

        fields["decoded"] = true;
        fields["serverFlag"] = serverFlag.ToString("X8", CultureInfo.InvariantCulture);
        fields["commandText"] = Encoding.ASCII.GetString(content.Slice(5, asciiLength));
    }

    private static DecodedMessage BuildLogin(ReadOnlySpan<byte> content, int serial, ReadOnlySpan<byte> rawFrame, Dictionary<string, object?> fields)
    {
        // Login information: Terminal ID = 8 bytes packed BCD (16 nibbles); the IMEI is the
        // low 15 digits (one leading padding nibble). Optional type/timezone bytes may follow.
        DeviceIdentityRef? identity = null;
        if (content.Length >= 8)
        {
            // A packed-BCD terminal id whose nibbles are not all decimal digits is malformed:
            // decoding it as ASCII would fabricate a garbage IMEI (nibbles 0xA–0xF map to ':'..'?').
            // Treat the identity as ABSENT instead — the frame is retained, but it carries no
            // resolvable claim, so the registry rejects it rather than a manufactured identifier
            // silently matching (or polluting) the lookup space.
            string? imei = TryDecodeImei(content[..8]);
            if (imei is not null)
            {
                fields["imei"] = imei;
                // The IMEI is an untrusted CLAIM only; it becomes the registry lookup key and is
                // NEVER a tenant/company/owner. Ownership is resolved elsewhere by IDeviceRegistry.
                identity = new DeviceIdentityRef(Imei: imei);
            }
            else
            {
                fields["imeiMalformed"] = true;
            }
        }

        return new DecodedMessage(
            MessageType.Login,
            rawFrame,
            fields,
            identity: identity,
            protocolMessageId: serial,
            requiresAck: true);
    }

    /// <summary>Decodes the fixed GPS information block (date, satellites, lat/lng, speed, course/status).</summary>
    private static void DecodeGps(ReadOnlySpan<byte> content, Dictionary<string, object?> fields)
    {
        // Minimum GPS block: date(6) + quantity(1) + lat(4) + lng(4) + speed(1) + course(2) = 18.
        if (content.Length < 18)
        {
            fields["gpsDecoded"] = false;
            return;
        }

        fields["gpsDecoded"] = true;

        DateTime? fixTime = ParseDateTime(content[..6]);
        fields["fixTimeUtc"] = fixTime;
        fields["dateTimeValid"] = fixTime is not null;

        // Quantity byte: high nibble = length of GPS info, low nibble = satellites in use.
        byte quantity = content[6];
        int satellites = quantity & 0x0F;
        fields["satellites"] = satellites;

        uint latRaw = ReadUInt32(content.Slice(7, 4));
        uint lngRaw = ReadUInt32(content.Slice(11, 4));
        int speedKph = content[15];
        int courseStatus = (content[16] << 8) | content[17];

        // Course/Status word (big-endian bit field), per the GT06 spec. The vendor document
        // tabulates this as two bytes; BYTE1 bit N of that table is bit (N + 8) of the word:
        //   bits 0-9 : course over ground, degrees [0,360)          (BYTE2 all + BYTE1 bits 0-1)
        //   bit 10   : LATITUDE  hemisphere -> 1 = North, 0 = South (BYTE1 bit2)
        //   bit 11   : LONGITUDE hemisphere -> 1 = West,  0 = East  (BYTE1 bit3)
        //   bit 12   : 1 = GPS positioned (fix valid), 0 = not positioned            (BYTE1 bit4)
        //   bit 13   : 0 = real-time GPS, 1 = differential positioning               (BYTE1 bit5)
        //
        // Bits 10 and 11 are LATITUDE then LONGITUDE, in that order, and bit 13 is asserted for
        // DIFFERENTIAL positioning — not real-time. Both facts are easy to invert and neither is
        // observable from a fixture whose two hemisphere bits happen to agree, so both are pinned
        // by independent sources:
        //   * Traccar Gt06ProtocolDecoder (Apache-2.0, cited in fixtures/gt06/README.md):
        //     "if (!BitUtil.check(flags, 10)) latitude = -latitude;" and
        //     "if (BitUtil.check(flags, 11)) longitude = -longitude;".
        //   * The public GT06/GT06N vendor protocol document: BYTE1 bit2 "South Latitude, North
        //     Latitude", bit3 "East Longitude, West Longitude", bit4 "GPS having been positioned
        //     or not", bit5 "GPS real-time/differential positioning" — with the worked example
        //     0x154C annotated "Bit5=0 -> real time GPS, Bit4=1 -> GPS has been positioned".
        int course = courseStatus & 0x03FF;
        bool north = (courseStatus & (1 << 10)) != 0;
        bool west = (courseStatus & (1 << 11)) != 0;
        bool positioned = (courseStatus & (1 << 12)) != 0;

        // Named for what the BIT means, so the polarity cannot be silently re-inverted by someone
        // reading only the field name. `realTimeGps` is retained for downstream consumers and is
        // now computed as the negation it always should have been.
        bool differentialPositioning = (courseStatus & (1 << 13)) != 0;
        bool realTime = !differentialPositioning;

        // Raw units are 1/1800000 degree (= 1/(60*30000)).
        double latMagnitude = latRaw / 1800000.0;
        double lngMagnitude = lngRaw / 1800000.0;
        double latitude = north ? latMagnitude : -latMagnitude;
        double longitude = west ? -lngMagnitude : lngMagnitude;

        fields["latRaw"] = latRaw;
        fields["lngRaw"] = lngRaw;
        fields["latitude"] = latitude;
        fields["longitude"] = longitude;
        fields["speedKph"] = speedKph;
        fields["courseDeg"] = course;
        fields["courseStatusWord"] = courseStatus;
        fields["hemisphereNorth"] = north;
        fields["hemisphereWest"] = west;
        fields["positioned"] = positioned;
        fields["realTimeGps"] = realTime;
        fields["isDifferentialPositioning"] = differentialPositioning;

        // Plausibility is a normalization concern, not a decode invariant: we still surface
        // out-of-range or impossible values verbatim and merely FLAG them so the pipeline
        // (not the adapter) decides what to do. A raw-but-suspect fix must still be representable.
        bool coordinatesInRange =
            latitude is >= -90.0 and <= 90.0 &&
            longitude is >= -180.0 and <= 180.0;
        fields["coordinatesValid"] = coordinatesInRange;
    }

    /// <summary>Decodes a status/heartbeat information block (terminal info, voltage level, GSM, alarm/language).</summary>
    private static void DecodeStatus(ReadOnlySpan<byte> content, Dictionary<string, object?> fields)
    {
        if (content.Length < 5)
        {
            fields["statusDecoded"] = false;
            return;
        }

        fields["statusDecoded"] = true;

        byte terminalInfo = content[0];
        int voltageLevel = content[1];   // 0..6 coarse level, NOT volts
        int gsmSignal = content[2];      // 0..4
        int alarm = content[3];
        int language = content[4];

        DecodeTerminalInfo(terminalInfo, fields);
        fields["voltageLevel"] = voltageLevel;      // 0=no power .. 6=full
        fields["gsmSignal"] = gsmSignal;            // 0=no signal .. 4=strong
        fields["alarmCode"] = alarm;
        fields["alarmName"] = AlarmName(alarm);
        fields["language"] = language;
    }

    /// <summary>Decodes an alarm information block: GPS block up front, status/alarm tail at the end.</summary>
    private static void DecodeAlarm(ReadOnlySpan<byte> content, Dictionary<string, object?> fields)
    {
        // Alarm frames carry the GPS block, then LBS, then a 5-byte tail:
        // terminalInfo(1) + voltageLevel(1) + gsmSignal(1) + alarm(1) + language(1).
        DecodeGps(content, fields);

        if (content.Length >= 5)
        {
            var tail = content[^5..];
            DecodeTerminalInfo(tail[0], fields);
            fields["voltageLevel"] = (int)tail[1];
            fields["gsmSignal"] = (int)tail[2];
            int alarm = tail[3];
            fields["alarmCode"] = alarm;
            fields["alarmName"] = AlarmName(alarm);
            fields["language"] = (int)tail[4];
        }
    }

    private static void DecodeTerminalInfo(byte terminalInfo, Dictionary<string, object?> fields)
    {
        // Terminal information byte bit layout (GT06 spec):
        //   bit0   : defense/activated
        //   bit1   : ACC (ignition) -> 1 = high/on
        //   bit2   : charging       -> 1 = charging
        //   bit3-5 : alarm status (3-bit)
        //   bit6   : 1 = GPS tracking on, 0 = GPS tracking off
        //   bit7   : 1 = oil & electricity DISCONNECTED, 0 = connected
        //
        // Bit 7 is asserted when the relay has CUT power, not when power is present. The public
        // GT06 vendor document states it as "1: oil and electricity disconnected / 0: oil and
        // electricity connected", and Traccar maps this same bit to Position.KEY_BLOCKED. The
        // canonical downstream field name stays `oilElectricityConnected`, so it must be the
        // NEGATION of the bit; reporting the raw bit under that name told the fleet a cut-off
        // vehicle was powered and a powered vehicle was cut off.
        fields["terminalInfo"] = (int)terminalInfo;
        fields["defenseActivated"] = (terminalInfo & 0x01) != 0;
        fields["ignitionOn"] = (terminalInfo & 0x02) != 0;
        fields["charging"] = (terminalInfo & 0x04) != 0;
        fields["terminalAlarmBits"] = (terminalInfo >> 3) & 0x07;
        fields["gpsTracking"] = (terminalInfo & 0x40) != 0;
        fields["oilElectricityDisconnected"] = (terminalInfo & 0x80) != 0;
        fields["oilElectricityConnected"] = (terminalInfo & 0x80) == 0;
    }

    /// <summary>
    /// Maps a GT06 alarm byte to a stable name for the <b>documented baseline dialect</b>
    /// (GT06/GT06N/Concox as described by the public vendor protocol document and decoded by
    /// Traccar's <c>Gt06ProtocolDecoder</c>).
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Only codes both sources agree on are named.</b> The vendor document itself enumerates
    /// only <c>0x00</c>–<c>0x05</c>; everything above that is dialect territory where public
    /// references disagree materially. Where they disagree the code is deliberately rendered as
    /// <c>Vendor0xNN</c> rather than given a confident label, because a wrong alarm name is worse
    /// than an unmapped one: a fleet acting on "SIM change" when the device said "door" is a
    /// dispatch decision made on fiction. The raw <c>alarmCode</c> is always published alongside
    /// this name, so a deployment that knows its model can map the remainder itself.
    /// </para>
    /// <para>
    /// Corrected here against both sources: <c>0x11</c> is power off (was reported as an airplane
    /// mode guess), <c>0x13</c> is disassemble/tamper (was reported as a fall), and <c>0x23</c> is
    /// the fall alarm (was unmapped). <c>0x10</c> and <c>0x12</c> are the contested pair — Traccar
    /// reads them as door and removal, other circulated tables as SIM change and airplane mode —
    /// so neither is asserted.
    /// </para>
    /// </remarks>
    private static string AlarmName(int code) => code switch
    {
        // ── Vendor-document codes: unambiguous across every source. ──
        0x00 => "Normal",
        0x01 => "SOS",
        0x02 => "PowerCut",
        0x03 => "Vibration",
        0x04 => "EnterFence",
        0x05 => "ExitFence",

        // ── Widely agreed extended codes. ──
        0x06 => "Overspeed",
        0x09 => "Displacement",
        0x0A => "EnterGpsBlindArea",
        0x0B => "ExitGpsBlindArea",
        0x0C => "PowerOn",
        0x0D => "GpsFirstFix",
        0x0E => "LowBattery",
        0x0F => "LowPower",
        0x11 => "PowerOff",
        0x13 => "Disassemble",
        0x14 => "Door",
        0x23 => "Fall",

        // ── Contested by model; named generically rather than wrongly. ──
        0x10 or 0x12 => VendorSpecificAlarmName(code),

        _ => "Unknown",
    };

    /// <summary>
    /// Renders a real but dialect-dependent alarm code as <c>Vendor0xNN</c>: it says "the device
    /// raised alarm NN and we will not guess which alarm that is on your hardware".
    /// </summary>
    private static string VendorSpecificAlarmName(int code) =>
        string.Create(CultureInfo.InvariantCulture, $"Vendor0x{code:X2}");

    /// <inheritdoc />
    /// <remarks>
    /// Builds the standard GT06 server response for frames the device expects answered
    /// (login and heartbeat/status): <c>78 78 05 &lt;protocol&gt; &lt;serialHi&gt;
    /// &lt;serialLo&gt; &lt;crcHi&gt; &lt;crcLo&gt; 0D 0A</c>, echoing the request protocol
    /// number and serial with a fresh CRC-ITU. Frames that require no acknowledgement
    /// return an empty array.
    /// </remarks>
    public byte[] EncodeAck(DecodedMessage message)
    {
        ArgumentNullException.ThrowIfNull(message);

        if (!message.RequiresAck)
            return Array.Empty<byte>();

        if (!message.Fields.TryGetValue("protocolNumber", out var protoObj) || protoObj is not int proto
            || message.ProtocolMessageId is not int serial)
        {
            return Array.Empty<byte>();
        }

        // Answer in the framing the device used. A frame that arrived under the 2-byte-length
        // 0x7979 marker is answered under 0x7979; a 0x7878 frame is answered under 0x7878. Traccar
        // makes exactly this choice (its sendResponse takes an `extended` flag threaded through
        // from the received framing). See the ACK note in fixtures/gt06/README.md for the residual
        // uncertainty this does NOT resolve.
        bool extended = message.Fields.TryGetValue("extendedFraming", out var extObj) && extObj is true;

        if ((byte)proto == ProtoTime8A)
            return BuildResponse(ProtoTime8A, serial, BuildUtcTimeBody(_utcNow()), extended);

        return BuildResponse((byte)proto, serial, ReadOnlySpan<byte>.Empty, extended);
    }

    /// <summary>
    /// Builds the 6-byte GT06 time-synchronisation body a <c>0x8A</c> request is answered with:
    /// <c>YY MM DD HH MM SS</c> in <b>UTC</b>, year encoded as <c>year - 2000</c> — the same
    /// encoding the device uses for its own fix timestamps, and the same body Traccar sends.
    /// </summary>
    /// <param name="utcNow">The current UTC time to publish to the device.</param>
    internal static byte[] BuildUtcTimeBody(DateTime utcNow)
    {
        DateTime utc = utcNow.Kind == DateTimeKind.Utc ? utcNow : utcNow.ToUniversalTime();
        return new[]
        {
            (byte)(utc.Year - 2000),
            (byte)utc.Month,
            (byte)utc.Day,
            (byte)utc.Hour,
            (byte)utc.Minute,
            (byte)utc.Second,
        };
    }

    /// <inheritdoc />
    /// <remarks>
    /// GT06 downlinks ride on protocol <c>0x80</c> as an ASCII command wrapped in a
    /// 4-byte server flag. This adapter only encodes a command when the caller supplies
    /// the exact device command text (argument <c>"text"</c> or <c>"command"</c>) — it
    /// never fabricates a vendor-specific/passworded command string. Unsupported requests
    /// return <see langword="null"/> so they cannot be silently mis-sent.
    /// </remarks>
    public byte[]? EncodeCommand(DeviceCommand command)
    {
        if (command.Arguments is null)
            return null;

        string? text = null;
        if (command.Arguments.TryGetValue("text", out var t) && !string.IsNullOrEmpty(t))
            text = t;
        else if (command.Arguments.TryGetValue("command", out var c) && !string.IsNullOrEmpty(c))
            text = c;

        if (text is null)
            return null;

        int serial = 1;
        if (command.Arguments.TryGetValue("serial", out var s) &&
            int.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed))
        {
            serial = parsed;
        }

        byte[] ascii = Encoding.ASCII.GetBytes(text);
        if (ascii.Length > 0xFB) // length byte must hold 4 (server flag) + ascii
            return null;

        // Command content = LengthOfCommand(1) + ServerFlagBit(4) + ASCII + Language(2).
        var body = new byte[1 + 4 + ascii.Length + 2];
        body[0] = (byte)(4 + ascii.Length);        // server flag (4) + command length
        // ServerFlagBit body[1..5] left 0x00000000 (echoed back verbatim by the device).
        Array.Copy(ascii, 0, body, 5, ascii.Length);
        body[^2] = 0x00;
        body[^1] = 0x02;                           // language = English

        return BuildResponse(ProtoCommand80, serial, body);
    }

    /// <summary>
    /// Maps a decoded GT06 <see cref="MessageType.Location"/> frame into the fabric's
    /// canonical event, stamping GT06 provenance.
    /// </summary>
    /// <remarks>
    /// <b>Ownership comes only from <paramref name="owner"/>.</b> The registry-resolved
    /// tenant/company/device/vehicle are copied verbatim; nothing here is derived from the
    /// packet. In particular the frame's IMEI is <em>never</em> promoted to a tenant,
    /// company or device id — it stays an untrusted claim on
    /// <see cref="DecodedMessage.Identity"/>.
    /// </remarks>
    /// <param name="message">A decoded frame (typically a <see cref="MessageType.Location"/>).</param>
    /// <param name="owner">Registry-resolved ownership. The ONLY source of tenant/company/device.</param>
    /// <param name="receivedAtGatewayUtc">When the gateway received the frame.</param>
    /// <param name="correlationId">Correlates all events derived from the same frame.</param>
    /// <param name="eventId">Optional explicit event id; a new GUID is minted when omitted.</param>
    /// <param name="normalizedAtUtc">Optional normalization timestamp; defaults to <see cref="DateTime.UtcNow"/>.</param>
    public CanonicalTelemetryEvent ToCanonicalEvent(
        DecodedMessage message,
        ResolvedDeviceOwner owner,
        DateTime receivedAtGatewayUtc,
        Guid correlationId,
        Guid? eventId = null,
        DateTime? normalizedAtUtc = null)
    {
        ArgumentNullException.ThrowIfNull(message);

        var fields = message.Fields;

        DateTime occurredAt = fields.TryGetValue("fixTimeUtc", out var ft) && ft is DateTime dtv
            ? dtv
            : receivedAtGatewayUtc; // no device clock on this frame -> fall back to receive time

        GeoPoint? location = null;
        var signals = new Dictionary<string, SignalValue>();

        if (fields.TryGetValue("latitude", out var latObj) && latObj is double lat &&
            fields.TryGetValue("longitude", out var lngObj) && lngObj is double lng)
        {
            int? sats = fields.TryGetValue("satellites", out var sObj) && sObj is int si ? si : null;
            double? course = fields.TryGetValue("courseDeg", out var cObj) && cObj is int ci ? (double)ci : null;
            double? speed = fields.TryGetValue("speedKph", out var spObj) && spObj is int spi ? (double)spi : null;

            location = new GeoPoint(lat, lng, Satellites: sats, HeadingDeg: course, SpeedKph: speed);

            if (speed is double sp)
                signals[VssSignals.Speed] = new SignalValue(sp, "kph", TelemetrySource.DirectDevice);
            if (course is double co)
                signals[VssSignals.Heading] = new SignalValue(co, "degrees", TelemetrySource.DirectDevice);
        }

        if (fields.TryGetValue("ignitionOn", out var ignObj) && ignObj is bool ign)
            signals[VssSignals.Ignition] = new SignalValue(ign, string.Empty, TelemetrySource.DirectDevice);

        bool? ignitionOn = fields.TryGetValue("ignitionOn", out var ig2) && ig2 is bool igb ? igb : null;

        return new CanonicalTelemetryEvent
        {
            SchemaVersion = CanonicalTelemetryEvent.CurrentSchemaVersion,
            EventId = eventId ?? Guid.NewGuid(),
            CorrelationId = correlationId,

            OccurredAtDeviceUtc = occurredAt,
            ReceivedAtGatewayUtc = receivedAtGatewayUtc,
            NormalizedAtUtc = normalizedAtUtc ?? DateTime.UtcNow,

            // Ownership: registry-resolved ONLY. Never from the packet/IMEI.
            TenantId = owner.TenantId,
            CompanyId = owner.CompanyId,
            DeviceId = owner.DeviceId,
            VehicleId = owner.VehicleId,

            Source = TelemetrySource.DirectDevice,
            Transport = Transport.Tcp,
            ProtocolName = ProtocolName,
            AdapterName = ProtocolName,
            AdapterVersion = AdapterVersion,

            Location = location,
            Signals = signals,
            IgnitionOn = ignitionOn,
        };
    }

    // ── Low-level helpers ──────────────────────────────────────────────────────

    /// <summary>
    /// CRC-ITU / CRC-16/X.25 as used by the GT06 error-check field: reflected polynomial
    /// 0x8408 (= 0x1021 reflected), initial value 0xFFFF, reflected in/out, final XOR
    /// 0xFFFF. Verified against the canonical check string "123456789" -&gt; 0x906E.
    /// </summary>
    /// <param name="data">The bytes from the length field through the information serial number inclusive.</param>
    /// <returns>The 16-bit checksum, compared big-endian against the on-wire error-check bytes.</returns>
    public static ushort Crc16Itu(ReadOnlySpan<byte> data)
    {
        ushort crc = 0xFFFF;
        foreach (byte b in data)
        {
            crc ^= b;
            for (int i = 0; i < 8; i++)
            {
                if ((crc & 0x0001) != 0)
                    crc = (ushort)((crc >> 1) ^ 0x8408);
                else
                    crc >>= 1;
            }
        }
        return (ushort)(~crc & 0xFFFF);
    }

    /// <summary>
    /// Builds a server frame: start, length, protocol, body, serial, CRC-ITU, stop — under
    /// <c>0x7878</c> (1-byte length) or, when <paramref name="extendedFraming"/> is set,
    /// <c>0x7979</c> (2-byte length).
    /// </summary>
    /// <param name="protocolNumber">The protocol number to echo back.</param>
    /// <param name="serial">The information serial number of the frame being answered.</param>
    /// <param name="body">Response content between the protocol number and the serial; usually empty.</param>
    /// <param name="extendedFraming">
    /// <see langword="true"/> to answer under the 2-byte-length <c>0x7979</c> marker. The caller
    /// passes the framing the request arrived under so a response is never written in a framing the
    /// device did not use.
    /// </param>
    private static byte[] BuildResponse(byte protocolNumber, int serial, ReadOnlySpan<byte> body, bool extendedFraming = false)
    {
        // PacketLength = protocol(1) + body(N) + serial(2) + crc(2).
        int packetLength = 1 + body.Length + 2 + 2;
        int lengthFieldSize = extendedFraming ? 2 : 1;
        var frame = new byte[2 + lengthFieldSize + packetLength + 2];

        byte start = extendedFraming ? Start2 : Start1;
        frame[0] = start;
        frame[1] = start;

        if (extendedFraming)
        {
            frame[2] = (byte)((packetLength >> 8) & 0xFF);
            frame[3] = (byte)(packetLength & 0xFF);
        }
        else
        {
            frame[2] = (byte)packetLength;
        }

        int protoIdx = 2 + lengthFieldSize;
        frame[protoIdx] = protocolNumber;
        body.CopyTo(frame.AsSpan(protoIdx + 1));

        int serialIdx = protoIdx + 1 + body.Length;
        frame[serialIdx] = (byte)((serial >> 8) & 0xFF);
        frame[serialIdx + 1] = (byte)(serial & 0xFF);

        int crcIdx = serialIdx + 2;
        // CRC covers from the length field (index 2) through the serial (exclusive of CRC bytes).
        ushort crc = Crc16Itu(frame.AsSpan(2, crcIdx - 2));
        frame[crcIdx] = (byte)((crc >> 8) & 0xFF);
        frame[crcIdx + 1] = (byte)(crc & 0xFF);
        frame[crcIdx + 2] = Stop1;
        frame[crcIdx + 3] = Stop2;

        return frame;
    }

    private static uint ReadUInt32(ReadOnlySpan<byte> b) =>
        (uint)((b[0] << 24) | (b[1] << 16) | (b[2] << 8) | b[3]);

    /// <summary>
    /// Parses a 6-byte GT06 date/time (YY MM DD HH MM SS, year = 2000+YY, UTC). Returns
    /// <see langword="null"/> for an out-of-range value rather than throwing, so a corrupt
    /// timestamp on an otherwise CRC-valid frame cannot crash decoding.
    /// </summary>
    private static DateTime? ParseDateTime(ReadOnlySpan<byte> b)
    {
        int year = 2000 + b[0];
        int month = b[1];
        int day = b[2];
        int hour = b[3];
        int minute = b[4];
        int second = b[5];

        if (month is < 1 or > 12) return null;
        if (day < 1 || day > DateTime.DaysInMonth(year, month)) return null;
        if (hour > 23 || minute > 59 || second > 59) return null;

        return new DateTime(year, month, day, hour, minute, second, DateTimeKind.Utc);
    }

    /// <summary>
    /// Decodes an 8-byte packed-BCD terminal id into its IMEI digit string (leading pad nibbles
    /// trimmed), or <see langword="null"/> when the terminal id is not valid packed BCD.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Each nibble must be a decimal digit (0–9). A nibble of 0xA–0xF is not a BCD digit: emitting
    /// <c>'0' + nibble</c> for it would produce a non-digit ASCII character (':', ';', … '?') and
    /// fabricate a garbage identifier. Rather than pass that off as an IMEI, a malformed terminal id
    /// yields <see langword="null"/> so the caller treats the identity as absent.
    /// </para>
    /// <para>
    /// <b>Exactly one pad nibble is removed, not every leading zero.</b> The terminal id is eight
    /// bytes, which is sixteen nibbles, and a 15-digit IMEI is stored with a single leading pad
    /// nibble — so the IMEI is the last fifteen digits, always. Trimming every leading zero instead
    /// silently eats real digits from any IMEI that begins with one, and the reporting-body
    /// prefixes that start <c>0</c> are ordinary allocations, not a curiosity. Such a device
    /// decoded to a 14-digit string, matched nothing in the registry or the allowlist, and could
    /// never be onboarded at all.
    /// </para>
    /// <para>
    /// The change is safe for the existing fleet by construction: for any IMEI that does not begin
    /// with a zero, trimming one nibble and trimming all leading zeros produce the same string, so
    /// no device that resolves today decodes differently tomorrow. The only devices affected are
    /// ones that cannot connect at present.
    /// </para>
    /// </remarks>
    private static string? TryDecodeImei(ReadOnlySpan<byte> bcd)
    {
        var sb = new StringBuilder(16);
        foreach (byte b in bcd)
        {
            int high = b >> 4;
            int low = b & 0x0F;
            if (high > 9 || low > 9)
                return null; // not packed BCD -> malformed terminal id, no resolvable identity.

            sb.Append((char)('0' + high));
            sb.Append((char)('0' + low));
        }

        // 16 nibbles -> drop the single pad nibble -> the 15 digits the device actually sent.
        return sb.ToString(1, sb.Length - 1);
    }
}
