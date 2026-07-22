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
    public Gt06Adapter(int maxFrameBytes)
    {
        if (maxFrameBytes <= 0)
            throw new ArgumentOutOfRangeException(nameof(maxFrameBytes), maxFrameBytes,
                "Per-frame ceiling must be positive.");
        _maxFrameBytes = maxFrameBytes;
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
    private const byte ProtoAlarm18 = 0x18;
    private const byte ProtoAlarm26 = 0x26;
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
    public IReadOnlyList<DecodedMessage> Decode(ReadOnlySpan<byte> buffer, out int consumed)
    {
        consumed = 0;
        List<DecodedMessage>? messages = null;

        while (consumed < buffer.Length)
        {
            var remaining = buffer[consumed..];
            var status = TryReadFrame(remaining, out var frameLength, out var message);

            switch (status)
            {
                case FrameStatus.NeedMore:
                    // Leave the partial frame unconsumed and wait for more bytes.
                    return (IReadOnlyList<DecodedMessage>?)messages ?? Array.Empty<DecodedMessage>();

                case FrameStatus.Malformed:
                    throw new ProtocolException(
                        "GT06 frame is malformed beyond recovery (bad framing/length/stop bits).",
                        ProtocolName,
                        offset: consumed);

                case FrameStatus.BadCrc:
                    // Reject this frame but do NOT throw: consume its declared span and continue.
                    consumed += frameLength;
                    continue;

                case FrameStatus.Ok:
                    messages ??= new List<DecodedMessage>();
                    messages.Add(message!);
                    consumed += frameLength;
                    continue;
            }
        }

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
        message = BuildMessage(protocolNumber, content, serial, rawFrame);
        return FrameStatus.Ok;
    }

    private DecodedMessage BuildMessage(byte protocolNumber, ReadOnlySpan<byte> content, int serial, ReadOnlySpan<byte> rawFrame)
    {
        var fields = new Dictionary<string, object?>
        {
            ["protocolNumber"] = (int)protocolNumber,
            ["serial"] = serial,
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
            case ProtoAlarm18:
            case ProtoAlarm26:
                DecodeAlarm(content, fields);
                return new DecodedMessage(MessageType.Alarm, rawFrame, fields, protocolMessageId: serial, requiresAck: true);

            case ProtoTime8A:
                fields["messageKind"] = "TimeSync";
                return new DecodedMessage(MessageType.Status, rawFrame, fields, protocolMessageId: serial, requiresAck: false);

            case ProtoCommand80:
                fields["messageKind"] = "Command";
                return new DecodedMessage(MessageType.Ack, rawFrame, fields, protocolMessageId: serial, requiresAck: false);

            default:
                // Well-framed, CRC-valid, but semantics unmapped: retain raw, do not guess.
                fields["messageKind"] = "Unknown";
                return new DecodedMessage(MessageType.Unknown, rawFrame, fields, protocolMessageId: serial, requiresAck: false);
        }
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

        // Course/Status word (big-endian bit field), per the GT06 spec:
        //   bits 0-9 : course over ground, degrees [0,360)
        //   bit 10   : longitude hemisphere -> 0 = East, 1 = West
        //   bit 11   : latitude hemisphere  -> 1 = North, 0 = South
        //   bit 12   : 1 = GPS positioned (fix valid), 0 = not positioned
        //   bit 13   : 1 = real-time GPS, 0 = differential positioning
        int course = courseStatus & 0x03FF;
        bool west = (courseStatus & (1 << 10)) != 0;
        bool north = (courseStatus & (1 << 11)) != 0;
        bool positioned = (courseStatus & (1 << 12)) != 0;
        bool realTime = (courseStatus & (1 << 13)) != 0;

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
        //   bit6   : 1 = GPS positioned/tracking
        //   bit7   : oil & electricity -> 1 = connected
        fields["terminalInfo"] = (int)terminalInfo;
        fields["defenseActivated"] = (terminalInfo & 0x01) != 0;
        fields["ignitionOn"] = (terminalInfo & 0x02) != 0;
        fields["charging"] = (terminalInfo & 0x04) != 0;
        fields["terminalAlarmBits"] = (terminalInfo >> 3) & 0x07;
        fields["gpsTracking"] = (terminalInfo & 0x40) != 0;
        fields["oilElectricityConnected"] = (terminalInfo & 0x80) != 0;
    }

    private static string AlarmName(int code) => code switch
    {
        0x00 => "Normal",
        0x01 => "SOS",
        0x02 => "PowerCut",
        0x03 => "Vibration",
        0x04 => "EnterFence",
        0x05 => "ExitFence",
        0x06 => "Overspeed",
        0x09 => "Displacement",
        0x0A => "EnterGpsBlindArea",
        0x0B => "ExitGpsBlindArea",
        0x0C => "PowerOn",
        0x0D => "GpsFirstFix",
        0x0E => "LowBattery",
        0x0F => "LowPower",
        0x10 => "PowerOff",
        0x11 => "AirplaneMode?",
        0x13 => "Fall",
        _ => "Unknown",
    };

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

        if (message.Fields.TryGetValue("protocolNumber", out var protoObj) && protoObj is int proto
            && message.ProtocolMessageId is int serial)
        {
            return BuildResponse((byte)proto, serial, ReadOnlySpan<byte>.Empty);
        }

        return Array.Empty<byte>();
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

    /// <summary>Builds a <c>0x7878</c> server frame: start, length, protocol, body, serial, CRC-ITU, stop.</summary>
    private static byte[] BuildResponse(byte protocolNumber, int serial, ReadOnlySpan<byte> body)
    {
        // PacketLength = protocol(1) + body(N) + serial(2) + crc(2).
        int packetLength = 1 + body.Length + 2 + 2;
        var frame = new byte[2 + 1 + packetLength + 2];

        frame[0] = Start1;
        frame[1] = Start1;
        frame[2] = (byte)packetLength;
        frame[3] = protocolNumber;
        body.CopyTo(frame.AsSpan(4));
        int serialIdx = 4 + body.Length;
        frame[serialIdx] = (byte)((serial >> 8) & 0xFF);
        frame[serialIdx + 1] = (byte)(serial & 0xFF);

        int crcIdx = serialIdx + 2;
        // CRC covers from the length byte (index 2) through the serial (exclusive of CRC bytes).
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
    /// Each nibble must be a decimal digit (0–9). A nibble of 0xA–0xF is not a BCD digit: emitting
    /// <c>'0' + nibble</c> for it would produce a non-digit ASCII character (':', ';', … '?') and
    /// fabricate a garbage identifier. Rather than pass that off as an IMEI, a malformed terminal id
    /// yields <see langword="null"/> so the caller treats the identity as absent.
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
        string digits = sb.ToString().TrimStart('0');
        return digits.Length == 0 ? "0" : digits;
    }
}
