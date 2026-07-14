# Telematics — Fixture Catalog

Owner: QA Architect & Test Automation Manager
Purpose: the **versioned, deterministic** catalog of packet fixtures that drive the telematics
test suite. Every fixture is reproducible, self-describing (via the fixture README), and free of
live secrets. This is the contract for what lives under `telematics/fixtures/`.

## Status honesty

**The GT06 fixture corpus is committed and in active use.** `telematics/fixtures/gt06/` contains
**19 `.hex` files + a `README.md`**, and both `Gt06AdapterTests` (30 tests) and
`GatewayTcpSliceTests` (5 real-socket tests) load them at runtime. What is described below is the
**actual on-disk scheme**, not an aspiration.

> Correction from an earlier draft: fixtures are **`.hex` text files**, not `.bin` binaries with
> sibling `.json` manifests, and they live **flat** under `gt06/` — there are no `valid/`,
> `invalid/`, `stale/`, `replay/`, `dual-feed/`, `commands/`, `registry/`, or `canonical/`
> subdirectories today. Those families are still design-only (see "Not yet present" below).

## The `.hex` scheme (what is real)

- Each fixture is a single file `telematics/fixtures/gt06/<name>.hex` holding **one
  whitespace-free hex string** for one scenario (a couple hold a deliberately multi-frame or
  truncated buffer).
- Tests decode the hex with a small `FromHex` helper (`Uri.IsHexDigit` filter → byte parse); the
  same helper lives in both `Gt06AdapterTests` and `GatewayTcpSliceTests`.
- Tests locate the folder by walking up from `AppContext.BaseDirectory` looking for
  `telematics/fixtures/gt06/login.hex` (or `fixtures/gt06/login.hex`), so the corpus is found
  regardless of the test working directory.
- The human-readable expectations (what each frame is, what it decodes to, and why it is
  valid/invalid) live in `telematics/fixtures/gt06/README.md` — that README is the manifest, with
  cited public sources (the public GT06/Concox protocol PDF, Traccar's `Gt06ProtocolDecoder`, and
  the `node-gt06` parser) and a byte-anatomy breakdown.

### CRC discipline

All CRC-valid fixtures are computed with **CRC-16/X.25 (CRC-ITU)** — reflected polynomial
`0x8408`, init `0xFFFF`, reflected in/out, final XOR `0xFFFF` — the exact algorithm in
`Gt06Adapter.Crc16Itu` (self-checked against the standard `"123456789"` → `0x906E` reference in
`Gt06AdapterTests.Crc16Itu_matches_the_canonical_x25_check_value`). The CRC is computed over the
bytes **from the length field through the information serial number, inclusive**. If you edit a
fixture's content you MUST recompute its CRC or the decoder will (correctly) reject it.

### Reserved test IMEI

The one seeded known device uses IMEI `868120303337976` (carried by `login.hex`), which the
`InMemoryDeviceRegistry` maps to a registry-assigned owner deliberately **unrelated** to the IMEI
(tenant `2f1c9a54-…`, company `100`, device `dev-known-0001`, vehicle `5501`). This is what lets
the fixtures exercise the "ownership comes from the registry, not the packet" property end to end.
The unknown-device socket test uses a *different*, unprovisioned IMEI (`351234567890999`) that
must be rejected.

## Committed fixtures (19 `.hex` + README)

| File | Protocol | What it exercises |
|---|---|---|
| `login.hex` | `0x01` login | IMEI `868120303337976` as 8-byte packed BCD, serial `0x0001` → `MessageType.Login`, `RequiresAck`, `Identity.Imei` set. |
| `login_ack.hex` | server → device | Expected server response to `login.hex` (`78 78 05 01 00 01 D9 DC 0D 0A`); asserted equal to `EncodeAck(login)`. |
| `heartbeat_ack.hex` | server → device | Expected server response to `heartbeat_0x13.hex`; asserted equal to `EncodeAck(heartbeat)`. |
| `location_0x12.hex` | `0x12` GPS | Dallas fix 2024-01-15 10:20:30 UTC, 9 sats, 32.7767 N / -96.7970 W, 60 kph, course 217°, LBS tail. The primary happy-path fix. |
| `location_0x22_7979.hex` | `0x22` via `0x7979` | Same GPS block under the **2-byte-length** `0x7979` marker: London 51.5074 N / -0.1278 W, serial `0x000A`. Proves both framings. |
| `south_east.hex` | `0x12` | Sydney -33.8688 S / 151.2093 E — exercises the South + East hemisphere bits. |
| `heartbeat_0x13.hex` | `0x13` status | Ignition-on / charging terminal info, voltage level 5, GSM 4, alarm `0x00`; `RequiresAck`. |
| `status_0x23.hex` | `0x23` status | Terminal info, voltage level 6, GSM 3, serial `0x0008` → `MessageType.Status`. |
| `alarm_sos_0x16.hex` | `0x16` alarm | GPS + LBS + status tail with alarm code `0x01` → `MessageType.Alarm`, `alarmName="SOS"`. |
| `time_0x8A.hex` | `0x8A` time | Empty-content time-sync, serial `0x000E`; decoded as a known non-location message, not `Unknown`. |
| `unknown_protocol_0x99.hex` | `0x99` | Well-framed CRC-valid frame with an unmapped protocol number → `MessageType.Unknown`, raw frame retained verbatim. |
| `bad_crc.hex` | `0x12` | `location_0x12` with the low CRC byte flipped → **rejected without throwing**, no message, buffer drained. |
| `truncated.hex` | `0x01` (partial) | First 10 bytes of `login.hex` → decoder reports `consumed=0` and waits for more. |
| `malformed_length.hex` | — | `78 78 02 …`: length below the 5-byte minimum → impossible framing → `ProtocolException`. |
| `multi_frame.hex` | mixed | `login` + `location_0x12` + `heartbeat_0x13` concatenated → all three decode in wire order, whole buffer consumed. |
| `invalid_coordinates.hex` | `0x12` | Out-of-range lat/lng surfaced verbatim with `coordinatesValid=false` (plausibility is a downstream concern). |
| `extreme_speed.hex` | `0x12` | Speed byte `0xFF` = 255 kph, decoded verbatim; the adapter never silently clamps. |
| `duplicate_serial.hex` | `0x12` | Second location reusing serial `0x0002`; the decoder still reports the serial faithfully so a downstream stage can detect the duplicate. |
| `out_of_order.hex` | `0x12` | Fix time 10:19:00 carried on a **higher** serial `0x0003`; decoder reports device time + serial faithfully. |

Note: `GatewayTcpSliceTests` also constructs some frames **in code** at test time rather than from
a fixture — a CRC-valid login for an arbitrary unknown IMEI (`BuildLoginFrame`), random
marker-less `Garbage`, and a `WithBrokenStopBits` mutation of `location_0x12.hex`. Those are
generated with the decoder's own `Crc16Itu` so they are byte-correct on the wire and still must be
handled fail-closed; they are documented here so the corpus inventory is complete.

## Design principles (upheld by the current corpus)

1. **Deterministic.** No wall-clock or RNG at read time; timestamps are baked into the frame.
   (Full replay-regression determinism is not yet closed — `ToCanonicalEvent` still fills
   `EventId`/`NormalizedAtUtc` from `Guid.NewGuid()`/`UtcNow`; see `quality-gates.md` G13.)
2. **Self-describing.** Every `.hex` is documented in `fixtures/gt06/README.md` with its expected
   decode and rationale, plus cited public protocol sources.
3. **No live secrets.** No gateway/device secret appears in any fixture; IMEIs are test values.
4. **Fail-closed by default.** The adversarial fixtures (`bad_crc`, `truncated`,
   `malformed_length`) assert a *rejection* outcome, never a fabricated event.

## Adding a `.hex` fixture (checklist)

- [ ] One whitespace-free hex string in `telematics/fixtures/gt06/<name>.hex`.
- [ ] A row added to `fixtures/gt06/README.md` stating the frame, its expected decode, and why.
- [ ] CRC recomputed with the CRC-16/X.25 algorithm in `Gt06Adapter.Crc16Itu` (unless the fixture
      is *deliberately* bad-CRC, in which case assert rejection).
- [ ] No real secret and no real device IMEI (use a test IMEI).
- [ ] A named test in `Gt06AdapterTests` (and/or `GatewayTcpSliceTests`) that loads and asserts it.
- [ ] Adversarial/invalid fixtures assert a rejection, not a fabricated event.

## Not yet present (design-only — do not read as committed)

These families are referenced by the traceability matrix's design-only layers and are **not** in
the tree:

| Planned family | Would drive | Blocking need |
|---|---|---|
| Anomaly fixtures that a scoring stage *flags* (`stale`, `replay`, `teleport`, `clockskew`, `jamming`) | Layer 7 / G8 quality-flag raising | A normalization/quality stage exists to set `QualityFlags` (none today). |
| Canonical goldens (`fixtures/canonical/`) | Layer 13 / G13 replay-regression | An injected clock/id abstraction + a golden-comparison harness. |
| Registry seed set (unknown / dup-IMEI / suspended / retired / quarantined devices) | Layer 5 barred-state paths | A registry fixture loader beyond the single seeded device. |
| Command round-trip fixtures (`EngineCutoff`/`SetInterval` → expected bytes) | Layer 9 typed commands | A typed high-level command encoder (only the generic `0x80` path exists). |
| Dual-feed pair (`DirectDevice` + `VendorCloud` for one vehicle) | G15 reconciliation | A vendor-cloud feed (blocked-external). |
| PT40-class hardware frames | G16 commissioning | Physical hardware (blocked-external). |

When any of these is added, move its row up into the committed inventory and give it a named test —
the same rule the current 19 fixtures already follow.
