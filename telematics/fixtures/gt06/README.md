# GT06 / Concox protocol test fixtures

Byte-accurate GT06 wire frames used by `Gt06AdapterTests`. Every `.hex` file holds one
whitespace-free hex string for a single scenario (a couple hold a deliberately multi-frame
or truncated buffer — noted below). All CRC-valid fixtures were generated with the
**CRC-ITU / CRC-16/X.25** checksum the protocol mandates and re-validated by the decoder's
own `Gt06Adapter.Crc16Itu`.

## Public documentation sources (cited)

These fixtures and the decoder follow the publicly circulated GT06 specification and two
widely used open-source decoders. Nothing here is reverse-engineered from proprietary
material:

1. **"GT06 Protocol" / "Concox GT06N GPS tracker communication protocol"** — the public
   vendor protocol PDF (Shenzhen Concox / Jimi IoT). Defines the frame layout
   `Start Bit | Packet Length | Protocol Number | Information Content | Information Serial
   Number | Error Check | Stop Bit`, the two start markers `0x7878` (1-byte length) and
   `0x7979` (2-byte length), stop bits `0x0D0A`, the login/location/status/alarm protocol
   numbers, the GPS information layout (date, satellite-count nibble, lat/lng at
   1/1 800 000 degree, speed, and the course/status bit field), and the CRC-ITU error check.
2. **Traccar `Gt06ProtocolDecoder`** (Apache-2.0) —
   `github.com/traccar/traccar` `.../protocol/Gt06ProtocolDecoder.java`. Cross-checked the
   CRC (`Checksum.CRC16_X25`), the `latitude/longitude = raw / 60.0 / 30000.0` scaling, and
   the course/status flag bit positions.
3. **`node-gt06`** (MIT) — community Node.js GT06 parser. Cross-checked the login BCD IMEI
   packing and the server login/heartbeat ACK frame shape.

### CRC-ITU (CRC-16/X.25)

Reflected polynomial `0x8408` (= `0x1021` reflected), init `0xFFFF`, reflected in/out,
final XOR `0xFFFF`. Standard check value: CRC of ASCII `"123456789"` = `0x906E`. It is
computed over the bytes **from the length field through the information serial number,
inclusive** — i.e. everything between the start bits and the 2-byte error check.

## Frame anatomy (0x7878)

```
78 78 | LL | PP | <information content ...> | SS SS | CC CC | 0D 0A
 start   |    |                                 |        |      stop
         |    protocol number                   serial   CRC-ITU
         packet length = protocol(1)+content(N)+serial(2)+crc(2)
```

`0x7979` frames are identical except the start marker is `79 79` and the length field is
**2 bytes** (`LL LL`).

## Fixtures

| File | Protocol | What it exercises |
|------|----------|-------------------|
| `login.hex` | `0x01` login | IMEI `868120303337976` as 8-byte packed BCD `08 68 12 03 03 33 79 76` (one leading pad nibble), serial `0x0001`. Decodes to `MessageType.Login`, `RequiresAck=true`, `Identity.Imei` set. |
| `login_ack.hex` | server → device | Expected server response to `login.hex`: `78 78 05 01 00 01 D9 DC 0D 0A` (protocol `0x01`, echoed serial `0x0001`, fresh CRC). Asserted equal to `EncodeAck(login)`. |
| `heartbeat_ack.hex` | server → device | Expected server response to `heartbeat_0x13.hex`: protocol `0x13`, serial `0x0007`. Asserted equal to `EncodeAck(heartbeat)`. |
| `location_0x12.hex` | `0x12` GPS | Fix 2024-01-15 10:20:30 UTC, 9 sats, **32.7767 N, -96.7970 W** (Dallas), speed 60 kph, course 217°, positioned + real-time. Course/status word `0x3CD9`. Includes trailing LBS (MCC 460 / MNC 1 / LAC / CellId). |
| `location_0x22_7979.hex` | `0x22` GPS via `0x7979` | Same GPS block shape but carried under the **2-byte-length** `0x7979` start marker: 51.5074 N, -0.1278 W (London), 33 kph, serial `0x000A`. Proves both framings decode. |
| `heartbeat_0x13.hex` | `0x13` status/heartbeat | Terminal info `0x46` (ignition on, charging, GPS tracking), voltage **level** 5/6, GSM 4/4, alarm `0x00`, language `0x02`. `RequiresAck=true`. |
| `status_0x23.hex` | `0x23` status | Terminal info `0x46`, voltage level 6, GSM 3, serial `0x0008`. Decodes to `MessageType.Status`. |
| `alarm_sos_0x16.hex` | `0x16` alarm | GPS block + LBS + status tail with **alarm code `0x01` (SOS)**. Decodes to `MessageType.Alarm` with `alarmName="SOS"`. |
| `time_0x8A.hex` | `0x8A` time | Empty-content time-sync request, serial `0x000E`. Decoded as a known (non-location) message, not `Unknown`. |
| `unknown_protocol_0x99.hex` | `0x99` (unassigned) | Well-framed, CRC-valid frame with an unmapped protocol number → `MessageType.Unknown`, raw frame retained. |
| `bad_crc.hex` | `0x12` | `location_0x12.hex` with the low CRC byte flipped (`89`→`76`). Must be **rejected without throwing** and yield no message. |
| `truncated.hex` | `0x01` (partial) | First 10 bytes of `login.hex` only. Decoder must report `consumed=0` and wait for more (no throw, no message). |
| `malformed_length.hex` | — | `78 78 02 ...`: packet length `0x02` is below the 5-byte minimum → impossible framing → `ProtocolException`. |
| `multi_frame.hex` | mixed | `login` + `location_0x12` + `heartbeat_0x13` concatenated in one buffer. Decoder returns all three in wire order and consumes the whole buffer. |
| `invalid_coordinates.hex` | `0x12` | Raw lat `0x0F000000` (≈139.8°) and lng `0x20000000` (≈298.3°) — out of geographic range. Values are surfaced verbatim with `coordinatesValid=false` (plausibility is a normalization concern, not a decode error). |
| `extreme_speed.hex` | `0x12` | Speed byte `0xFF` = 255 kph (physically implausible for most fleet vehicles). Decoded verbatim; the adapter never silently clamps. |
| `duplicate_serial.hex` | `0x12` | Second location reusing serial `0x0002` (same as `location_0x12.hex`). Duplicate/idempotency detection is downstream; the decoder still returns the correct serial for the gateway to notice. |
| `out_of_order.hex` | `0x12` | Fix time 10:19:00 (earlier) carried on a **higher** serial `0x0003`. Ordering/skew is a normalization concern; the decoder faithfully reports device time + serial. |
| `south_east.hex` | `0x12` | Sydney -33.8688 S, 151.2093 E — exercises the **South** and **East** hemisphere bits (course/status word `0x3078`, bit11=0 → South, bit10=0 → East). |

## Regenerating

The generator script lives alongside this fixture set's history; each frame is
`start | length | protocol | content | serial | CRC-ITU | stop`, with the CRC computed by
the exact algorithm in `Gt06Adapter.Crc16Itu`. If you change a fixture's content you MUST
recompute its CRC or the decoder will (correctly) reject it.
