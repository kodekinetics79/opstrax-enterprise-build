using System.Globalization;
using System.Text.Json;
using Opstrax.Telematics.Contracts.Adapters;
using Opstrax.Telematics.Contracts.Identity;

namespace Opstrax.Telematics.Protocols.PacificTrack;

/// <summary>
/// An <see cref="IPacificTrackParser"/> that delegates decoding to Pacific Track's official
/// parser running as a co-located child process, over newline-delimited JSON on the child's
/// stdin/stdout. This is how the vendor's <b>Python</b> or <b>Java</b> parser is used without
/// reimplementing it; the vendor's C# parser can also be hosted this way, though implementing
/// <see cref="IPacificTrackParser"/> in-process is faster and keeps adapter purity.
/// </summary>
/// <remarks>
/// <para><b>Wire protocol.</b> One JSON object per line, request and response, UTF-8. The child
/// reads a request line, writes exactly one response line, flushes, and loops. Hex is
/// case-insensitive on input.</para>
/// <code>
/// -> {"op":"identify","hex":"7878..."}
/// &lt;- {"ok":true,"match":true,"confidence":0.98,"needMoreData":false}
///
/// -> {"op":"decode","hex":"7878..."}
/// &lt;- {"ok":true,"consumed":18,"frames":[
///      {"type":"Location","hex":"7878...","messageId":7,"requiresAck":true,
///       "imei":"862464068456321","fields":{"latitude":34.05,"longitude":-118.24,
///                                          "speedKph":52,"courseDeg":91,
///                                          "fixTimeUtc":"2026-08-21T12:00:00Z"}}]}
///
/// -> {"op":"ack","hex":"7878...","messageId":7}
/// &lt;- {"ok":true,"hex":"787805010001D9DC0D0A"}
///
/// &lt;- {"ok":false,"error":"bad crc","offset":12}      // any op: malformed input
/// </code>
/// <para>
/// <b>Field names.</b> <c>fields</c> is passed through to the gateway's normalizer, which reads
/// well-known aliases (<c>latitude</c>/<c>lat</c>, <c>speedKph</c>/<c>speed</c>,
/// <c>courseDeg</c>/<c>heading</c>, <c>fixTimeUtc</c>/<c>gpsTime</c>, …). Emitting a
/// device-originated <c>fixTimeUtc</c> is <b>mandatory</b> for a location frame — OpsTrax rejects
/// a fix with no device clock, and substituting arrival time would silently launder an
/// offline-buffered frame into a "live" one.
/// </para>
/// <para>
/// <b>Deliberate deviation from adapter purity.</b> <see cref="IProtocolAdapter"/> asks decoders
/// to be pure and I/O-free. This one is not: it does a bounded, serialized round-trip to a child
/// process. The costs are real and bounded on purpose — every call takes
/// <see cref="RequestTimeout"/> at worst, and all connections share one child, so this path
/// serializes fleet-wide decode. It is the correct trade when the alternative is guessing at a
/// licensed wire format; for high fleet counts, port the vendor C# parser in-process instead.
/// </para>
/// <para>
/// <b>Desynchronization is terminal.</b> A timeout or an unparseable response line means the
/// bridge no longer knows where it is in the child's output. Continuing would pair the next
/// request with a stale response — decoding one truck's frame into another truck's answer. So
/// the bridge latches <see cref="IsAvailable"/> to <see langword="false"/> and refuses all
/// further work until the host restarts the child and constructs a new bridge.
/// </para>
/// </remarks>
public sealed class StdioParserBridge : IPacificTrackParser, IDisposable
{
    private readonly TextWriter _requests;
    private readonly TextReader _responses;
    private readonly object _gate = new();
    private readonly Action<string>? _onFault;

    private bool _faulted;
    private bool _disposed;

    /// <summary>Creates a bridge over an already-started child process's streams.</summary>
    /// <param name="requests">The child's stdin. The bridge writes one request line per call.</param>
    /// <param name="responses">The child's stdout. The bridge reads exactly one response line per call.</param>
    /// <param name="parserVersion">Vendor parser version, for provenance and support tickets.</param>
    /// <param name="requestTimeout">
    /// Per-call ceiling. A wedged child must not hold a tracker connection open indefinitely.
    /// Defaults to 2 seconds — decode is CPU-bound work on a few kilobytes, so anything slower is
    /// a fault, not slowness.
    /// </param>
    /// <param name="onFault">
    /// Invoked once when the bridge latches faulted, so a supervisor can restart the child. The
    /// bridge itself never restarts anything: it does not own the process.
    /// </param>
    public StdioParserBridge(
        TextWriter requests,
        TextReader responses,
        string parserVersion = "",
        TimeSpan? requestTimeout = null,
        Action<string>? onFault = null)
    {
        _requests = requests ?? throw new ArgumentNullException(nameof(requests));
        _responses = responses ?? throw new ArgumentNullException(nameof(responses));
        ParserVersion = parserVersion ?? string.Empty;
        RequestTimeout = requestTimeout ?? TimeSpan.FromSeconds(2);
        _onFault = onFault;

        if (RequestTimeout <= TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(requestTimeout), "Request timeout must be positive.");
    }

    /// <summary>Per-call ceiling on the child's response.</summary>
    public TimeSpan RequestTimeout { get; }

    /// <inheritdoc />
    public string ParserVersion { get; }

    /// <inheritdoc />
    public bool IsAvailable
    {
        get { lock (_gate) return !_faulted && !_disposed; }
    }

    /// <inheritdoc />
    public ProtocolMatch Identify(ReadOnlySpan<byte> opening)
    {
        if (opening.Length == 0) return ProtocolMatch.Incomplete();

        JsonDocument? response = Exchange("identify", Convert.ToHexString(opening), messageId: null);
        if (response is null) return ProtocolMatch.NoMatch(); // Faulted/unavailable: claim nothing.

        using (response)
        {
            JsonElement root = response.RootElement;
            if (ReadBool(root, "needMoreData")) return ProtocolMatch.Incomplete();
            if (!ReadBool(root, "match")) return ProtocolMatch.NoMatch();

            double confidence = ReadDouble(root, "confidence") ?? 1.0;
            return ProtocolMatch.Match(confidence);
        }
    }

    /// <inheritdoc />
    public IReadOnlyList<DecodedMessage> Decode(ReadOnlySpan<byte> buffer, out int consumed)
    {
        consumed = 0;
        if (buffer.Length == 0) return Array.Empty<DecodedMessage>();

        JsonDocument? response = Exchange("decode", Convert.ToHexString(buffer), messageId: null);
        if (response is null)
            throw new ProtocolException(
                "Pacific Track parser bridge is unavailable; refusing to decode.", PacificTrackAdapter.ProtocolName);

        using (response)
        {
            JsonElement root = response.RootElement;

            int reported = (int)(ReadDouble(root, "consumed") ?? 0);
            // A child that over-reports would make the gateway discard bytes it never decoded,
            // silently dropping a frame. Clamp to what actually exists.
            consumed = Math.Clamp(reported, 0, buffer.Length);

            if (!root.TryGetProperty("frames", out JsonElement frames) ||
                frames.ValueKind != JsonValueKind.Array)
                return Array.Empty<DecodedMessage>();

            var decoded = new List<DecodedMessage>(frames.GetArrayLength());
            foreach (JsonElement frame in frames.EnumerateArray())
                decoded.Add(ReadFrame(frame));

            return decoded;
        }
    }

    /// <inheritdoc />
    public byte[] EncodeAck(DecodedMessage message)
    {
        ArgumentNullException.ThrowIfNull(message);

        byte[] raw = message.RawFrame as byte[] ?? message.RawFrame.ToArray();
        JsonDocument? response = Exchange("ack", Convert.ToHexString(raw), message.ProtocolMessageId);
        if (response is null) return Array.Empty<byte>();

        using (response)
        {
            if (!response.RootElement.TryGetProperty("hex", out JsonElement hex) ||
                hex.ValueKind != JsonValueKind.String)
                return Array.Empty<byte>();

            return ParseHex(hex.GetString());
        }
    }

    // ── Wire protocol ──────────────────────────────────────────────────────────

    /// <summary>
    /// Performs one request/response round-trip. Returns <see langword="null"/> when the bridge is
    /// unavailable (never throws for that), and throws <see cref="ProtocolException"/> when the
    /// child reports the input is malformed.
    /// </summary>
    private JsonDocument? Exchange(string op, string hex, int? messageId)
    {
        // One child, one pipe: requests must not interleave. The lock is held across the whole
        // round-trip, which is what makes the request/response pairing sound.
        lock (_gate)
        {
            if (_faulted || _disposed) return null;

            string request = messageId is { } id
                ? $"{{\"op\":\"{op}\",\"hex\":\"{hex}\",\"messageId\":{id.ToString(CultureInfo.InvariantCulture)}}}"
                : $"{{\"op\":\"{op}\",\"hex\":\"{hex}\"}}";

            string? line;
            try
            {
                _requests.WriteLine(request);
                _requests.Flush();
                line = ReadLineWithTimeout();
            }
            catch (Exception ex) when (ex is IOException or ObjectDisposedException)
            {
                Fault($"Pacific Track parser child process stream failed: {ex.Message}");
                return null;
            }

            if (line is null)
            {
                Fault("Pacific Track parser child process closed its output stream.");
                return null;
            }

            JsonDocument document;
            try
            {
                document = JsonDocument.Parse(line);
            }
            catch (JsonException ex)
            {
                // We can no longer trust the stream position, so this is terminal, not a retry.
                Fault($"Pacific Track parser returned a non-JSON line: {ex.Message}");
                return null;
            }

            if (document.RootElement.ValueKind != JsonValueKind.Object)
            {
                document.Dispose();
                Fault("Pacific Track parser returned a JSON value that is not an object.");
                return null;
            }

            if (!ReadBool(document.RootElement, "ok"))
            {
                string error = ReadString(document.RootElement, "error") ?? "unspecified parser error";
                int? offset = (int?)ReadDouble(document.RootElement, "offset");
                document.Dispose();

                // The child says the BYTES are bad, not that the child is bad. That is a normal,
                // per-connection outcome: the gateway drops this connection and keeps serving others.
                throw new ProtocolException(
                    $"Pacific Track parser rejected the frame: {error}",
                    PacificTrackAdapter.ProtocolName,
                    offset);
            }

            return document;
        }
    }

    /// <summary>
    /// Reads one response line, giving up after <see cref="RequestTimeout"/>. A timeout is
    /// terminal: the child may still write that line later, which would desynchronize every
    /// subsequent exchange.
    /// </summary>
    private string? ReadLineWithTimeout()
    {
        Task<string?> read = Task.Run(() => _responses.ReadLine());

        if (!read.Wait(RequestTimeout))
        {
            Fault($"Pacific Track parser did not respond within {RequestTimeout}.");
            return null;
        }

        return read.GetAwaiter().GetResult();
    }

    /// <summary>Latches the bridge unavailable and notifies the supervisor exactly once. Caller holds <see cref="_gate"/>.</summary>
    private void Fault(string reason)
    {
        if (_faulted) return;
        _faulted = true;
        _onFault?.Invoke(reason);
    }

    // ── Response mapping ───────────────────────────────────────────────────────

    private static DecodedMessage ReadFrame(JsonElement frame)
    {
        if (frame.ValueKind != JsonValueKind.Object)
            throw new ProtocolException(
                "Pacific Track parser emitted a frame that is not a JSON object.", PacificTrackAdapter.ProtocolName);

        byte[] raw = ParseHex(ReadString(frame, "hex"));

        MessageType type = Enum.TryParse(ReadString(frame, "type"), ignoreCase: true, out MessageType parsed)
            ? parsed
            : MessageType.Unknown;

        string? imei = ReadString(frame, "imei");
        string? serial = ReadString(frame, "serial");
        DeviceIdentityRef? identity = imei is not null || serial is not null
            ? new DeviceIdentityRef(Imei: imei, Serial: serial)
            : null;

        var fields = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
        if (frame.TryGetProperty("fields", out JsonElement bag) && bag.ValueKind == JsonValueKind.Object)
            foreach (JsonProperty property in bag.EnumerateObject())
                fields[property.Name] = ReadScalar(property.Value);

        return new DecodedMessage(
            messageType: type,
            rawFrame: raw,
            fields: fields,
            identity: identity,
            protocolMessageId: (int?)ReadDouble(frame, "messageId"),
            requiresAck: ReadBool(frame, "requiresAck"));
    }

    /// <summary>
    /// Maps a JSON leaf to the adapter-local field bag. Objects and arrays are flattened to their
    /// raw JSON text rather than dropped, so a vendor field the normalizer does not yet understand
    /// survives into logs instead of vanishing.
    /// </summary>
    private static object? ReadScalar(JsonElement value) => value.ValueKind switch
    {
        JsonValueKind.String => value.GetString(),
        // The cast matters: without it the conditional's common type is double, and every
        // integral vendor field would silently arrive as a floating-point value.
        JsonValueKind.Number => value.TryGetInt64(out long l) ? (object)l : value.GetDouble(),
        JsonValueKind.True => true,
        JsonValueKind.False => false,
        JsonValueKind.Null or JsonValueKind.Undefined => null,
        _ => value.GetRawText(),
    };

    private static byte[] ParseHex(string? hex)
    {
        if (string.IsNullOrEmpty(hex)) return Array.Empty<byte>();
        try
        {
            return Convert.FromHexString(hex);
        }
        catch (FormatException ex)
        {
            throw new ProtocolException(
                $"Pacific Track parser emitted invalid hex: {ex.Message}", PacificTrackAdapter.ProtocolName);
        }
    }

    private static bool ReadBool(JsonElement root, string name) =>
        root.TryGetProperty(name, out JsonElement value) && value.ValueKind == JsonValueKind.True;

    private static double? ReadDouble(JsonElement root, string name) =>
        root.TryGetProperty(name, out JsonElement value) &&
        value.ValueKind == JsonValueKind.Number &&
        value.TryGetDouble(out double d)
            ? d
            : null;

    private static string? ReadString(JsonElement root, string name) =>
        root.TryGetProperty(name, out JsonElement value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    /// <summary>
    /// Releases the bridge. The child process itself belongs to whoever started it; disposing here
    /// only stops this bridge from issuing further requests.
    /// </summary>
    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed) return;
            _disposed = true;
        }
    }
}
