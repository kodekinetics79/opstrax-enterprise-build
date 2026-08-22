# Pacific Track protocol adapter

This project is the **seam** Pacific Track's official parser is installed behind. It contains
no PT wire-format knowledge, and that is deliberate.

## Why OpsTrax does not ship a PT decoder

Three reasons, in order of weight:

1. **We have never captured a byte from the device.** `docs/telematics/pt40/pt40-fingerprint.md`
   is explicit that the PT40-Q's protocol is UNKNOWN and must be derived from a real capture, not
   from the model name. A registry row is an identity record, never protocol evidence.
2. **The specification is the vendor's.** Pacific Track distribute the protocol document and the
   parser under licence, in C#, Python and Java.
3. **A guessed decoder is worse than no decoder.** A wrong field offset does not crash — it
   produces coordinates that are in range, plausible, and wrong. Nothing downstream can detect
   that. Refusing to decode is recoverable; a silently mis-decoded fleet is not.

So the decode step is a dependency you supply. Everything around it — the listener, IMEI
allowlist, replay defence, normalization, HMAC-signed HTTPS forwarding, and the durable outbox —
is complete and covered by tests.

## What happens before you install a parser

`UnavailablePacificTrackParser` is registered by default. `PacificTrackAdapter.TryIdentify` then
returns `NoMatch` for every stream, so:

- a PT device that connects is **refused and counted** (`UnidentifiedProtocolConnections`),
- it is **never** handed to the GT06 adapter as a fallback, and
- the gateway keeps serving GT06 hardware normally.

The startup log says so plainly:

```
warn: Pacific Track support is enabled but no parser is installed
      (Gateway:Edge:Protocols:PacificTrack:ParserCommand is unset). The adapter is registered
      fail-closed: PT devices will be refused, never decoded by another vendor's adapter.
```

## Option A — the vendor's C# parser, in-process (fastest)

Reference the vendor assembly and implement `IPacificTrackParser` directly. This keeps the
adapter pure and I/O-free, which is what `IProtocolAdapter` asks for, and it has no per-frame
process hop.

```csharp
public sealed class VendorPacificTrackParser : IPacificTrackParser
{
    private readonly PacificTrack.Sdk.Parser _vendor = new();   // <- the vendor's type

    public bool IsAvailable => true;
    public string ParserVersion => PacificTrack.Sdk.Parser.Version;

    public ProtocolMatch Identify(ReadOnlySpan<byte> opening) =>
        _vendor.Looks Like PT(opening) ? ProtocolMatch.Match(0.95) : ProtocolMatch.NoMatch();

    public IReadOnlyList<DecodedMessage> Decode(ReadOnlySpan<byte> buffer, out int consumed)
    {
        // Translate the vendor's frames into DecodedMessage. Emit a device-originated
        // fixTimeUtc on every location frame -- see "Required fields" below.
    }

    public byte[] EncodeAck(DecodedMessage message) => _vendor.BuildAck(...);
}
```

Then register it in `BuildProtocolRouter` (`Program.cs`) in place of `host.Parser`.

Implementations are shared as singletons across every connection, so they **must be thread-safe**.

## Option B — the vendor's Python or Java parser, as a child process

`StdioParserBridge` speaks newline-delimited JSON over a child process's stdin/stdout, so the
vendor's Python or Java parser can be used without porting it.

```jsonc
"Gateway": {
  "Edge": {
    "Protocols": {
      "PacificTrack": {
        "Enabled": true,
        "ParserCommand": "python3",
        "ParserArguments": ["/opt/opstrax/pt-parser/bridge.py"],
        "ParserVersion": "pt-sdk 4.1.2",
        "ParserTimeout": "00:00:02"
      }
    }
  }
}
```

`reference/bridge.py` in this directory implements the whole protocol correctly and leaves two
clearly-marked functions for you to fill in with vendor calls. Start from it.

### Wire protocol

One JSON object per line, request and response, UTF-8. The child reads a request line, writes
exactly one response line, flushes, and loops. Hex is case-insensitive on input.

```
-> {"op":"identify","hex":"7878..."}
<- {"ok":true,"match":true,"confidence":0.98,"needMoreData":false}

-> {"op":"decode","hex":"7878..."}
<- {"ok":true,"consumed":18,"frames":[
     {"type":"Location","hex":"7878...","messageId":7,"requiresAck":true,
      "imei":"862464068456321",
      "fields":{"latitude":34.05,"longitude":-118.24,"speedKph":52,"courseDeg":91,
                "fixTimeUtc":"2026-08-21T12:00:00Z"}}]}

-> {"op":"ack","hex":"7878...","messageId":7}
<- {"ok":true,"hex":"787805010001D9DC0D0A"}

<- {"ok":false,"error":"bad crc","offset":12}     // any op: the BYTES are bad
```

`type` is one of `Login`, `Heartbeat`, `Location`, `Alarm`, `Status`, `Ack`, `Unknown`.

### Costs you are accepting with Option B

- **One child, serialized.** All connections share it, so fleet-wide decode is serialized behind
  one pipe. Fine for a pilot; port to Option A before a large fleet.
- **Bounded per call.** `ParserTimeout` (default 2s) caps every request so a wedged child cannot
  hold a tracker connection open.
- **Desynchronization is terminal.** A timeout or an unparseable response line means the bridge no
  longer knows where it is in the child's output; pairing the next request with a stale response
  would decode one truck's frame into another truck's answer. The bridge latches itself unavailable
  and PT devices are refused until the gateway restarts.

## Required fields

The normalizer reads well-known aliases, so `latitude`/`lat`, `speedKph`/`speed`,
`courseDeg`/`heading` and so on all work. Two rules are not negotiable:

- **Every location frame must carry a device-originated timestamp** (`fixTimeUtc`, `gpsTime`, or
  an epoch). OpsTrax rejects a fix with no device clock, and substituting arrival time would
  relabel an offline-buffered frame as live — which is exactly what the map's freshness grading
  exists to prevent.
- **`hex` must be the exact bytes of that one frame.** It is what the replay guard hashes, so a
  re-serialized or re-cased frame would not deduplicate.

Emit `alarmName` for events. Only `sos`, `crash`, `harsh_braking`, `harsh_acceleration` and
`harsh_turn` (and their common spellings) become driver safety events; everything else is
correctly dropped by the normalizer rather than silently discarded server-side.

## Confirming the fingerprint before you trust it

Do not write a `ProtocolName` into `eld_devices` or claim PT support anywhere until a real capture
has been fingerprinted:

```bash
python3 tools/telematics/fingerprint.py --hex "<first frame the device sent>"
```

Walk `docs/telematics/pt40/pt40-fingerprint.md` top to bottom; the first branch whose signature
matches wins, and a match is only a *candidate* until that branch's Confirm step passes. If the
PT40-Q turns out to speak GT06 after all, the existing `Gt06Adapter` (39/39 protocol tests) already
handles it and you need none of this.
