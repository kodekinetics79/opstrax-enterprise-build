# OpsTrax Telematics — Hardware Pilot Plan


This is a from-scratch plan for getting real GPS/ELD hardware onto the OpsTrax live map,
written against what is **actually built today** (a tested GT06 decoder, a loopback-only
TCP gateway, one physical pilot unit) rather than the target cloud-native architecture in
`docs/telematics/architecture.md`, which is a longer-term destination, not a prerequisite.

---

## 1. Where things stand today

| Fact | Evidence |
|---|---|
| A real, tested GT06/Concox protocol decoder exists | `Gt06Adapter.cs` — CRC-verified framing, login/location/status/alarm decode, 30 unit tests against byte-accurate fixtures in `telematics/fixtures/gt06/` |
| A TCP gateway exists but isn't publicly reachable | `TcpGatewayService` binds `127.0.0.1` only (`GatewayOptions.ListenAddress` default) — code is real and tested, deployment is not |
| One physical pilot device is already owned | PT40-Q, IMEI `862464068456321`, serial `4C4000067803` — protocol believed GT06-family, **unconfirmed** until a real frame is captured |
| Two external blockers, not code gaps | (1) no public gateway host running yet, (2) the PT40-Q hasn't been told where to connect (needs an SMS `SERVER=`/`APN=` command) |
| Security debt flagged before scaling past 1 device | one shared `Telemetry:GatewaySecret` authenticates every device of every tenant today; SOS/crash alarms decode but are dropped before reaching `safety_events`; harsh-braking dashboard reads a label nothing writes — all detailed in `docs/telematics/COMPLETION_PLAN.md` §A |

---

## 2. Execution plan

### Phase 0 — Prove one real device end-to-end (no purchase required)

- [ ] **0.1 Deploy the gateway publicly.** Stand up `TcpGatewayService` on a host with a real
  routable IP (not loopback) and a static `host:port`. Do **not** build the full k8s/NLB
  topology in ADR-004 for a one-device pilot — that's the scale answer, not the pilot answer.
  See §4 for hosting options.
- [ ] **0.2 Repoint the PT40-Q.** Get SMS access to its SIM and send the `SERVER=<host>:<port>`
  (+ `APN=`) command per `pt40-onboarding-runbook.md` step 5. Confirm the device ACKs.
- [ ] **0.3 Capture the first frame.** Use `tools/telematics/capture_listener.py` (or `tcpdump`)
  on the gateway host to record the raw bytes verbatim from the real device session.
- [ ] **0.4 Fingerprint it.** Run `tools/telematics/fingerprint.py` against the capture and walk
  `pt40-fingerprint.md`'s decision tree. Only record a `ProtocolName` once the Confirm step
  passes — never guess from the model name.
- [ ] **0.5 Walk runbook steps 8–18.** These are already-wired plumbing (decode → normalize →
  validate → `latest_vehicle_positions` → live map). Once step 4 lands a confirmed GT06
  fingerprint, this is verification work, not new engineering.
- [ ] **0.6 Prove non-simulated provenance.** Confirm the fix's `TelemetrySource == DirectDevice`
  (not `Seed`/`Simulator`) and that the correlation id traces from the raw capture through to
  the map row — runbook step 18's acceptance gate.

### Phase 1 — Close security gaps before onboarding a second device

Straight from `COMPLETION_PLAN.md`'s P0 tier — do this before Phase 2, not after:

- [ ] **1.1 Kill the shared secret.** Replace the single global `Telemetry:GatewaySecret` with
  per-device/per-gateway credentials (`COMPLETION_PLAN.md` P0-2). One leaked secret currently
  impersonates any device across all 4 tenants.
- [ ] **1.2 Bridge SOS/crash alarms.** GT06 already decodes `alarmName` (SOS, Fall, Vibration,
  PowerCut, Overspeed) but `Gt06Adapter.ToCanonicalEvent` drops it before it reaches
  `safety_events` (P0-4). A real SOS button press currently goes nowhere.
- [ ] **1.3 Fix the harsh-braking dashboard mismatch.** The dashboard query filters on
  `'Harsh Braking'` (title case); the pipeline writes `harsh_braking` (lowercase snake). Align
  them (P0-5) so the tile isn't silently empty forever.

### Phase 2 — Scale the device roster

- [ ] **2.1** Buy 2–3 cheap GT06-family units (see §3.A) to get a second and third real data
  point on the now-proven pipeline before spending on anything unproven.
- [ ] **2.2** If moving upmarket, build the Teltonika adapter (see §3.B) — same
  `IProtocolAdapter` pattern as `Gt06Adapter.cs`, signature already scoped in
  `pt40-fingerprint.md` branch 2. This unlocks real accelerometer-based harsh-event detection
  instead of the GPS-speed-delta fallback the completion plan calls "lower fidelity."
- [ ] **2.3** Move gateway hosting from the Phase-0 single VPS/Fly app toward the
  multi-region topology in `ADR-004` once device count actually justifies it — not before.

---

## 3. Recommended trackers by category

### A. GT06-family — ready today, zero new adapter code

The only protocol with a tested decoder in this repo. Lowest-risk category by a wide margin.

| Device | Form factor | Notes |
|---|---|---|
| **Concox GT06N** | Wired (hardwired to vehicle power), reads ignition/ACC | The reference device the protocol is literally named for; cheapest way to get a guaranteed match to `Gt06Adapter.cs`. |
| **Concox TR06** | Wired, compact | Same protocol family, smaller footprint, common in fleet resale channels. |
| **Concox JC400 / JM01** | Battery-powered asset tracker, magnetic mount | No vehicle wiring needed — good for trailers, containers, or a quick desk-test unit. |
| **Jimi IoT JM-VL01** | Battery-powered, GT06-compatible | Jimi is Concox's successor brand; several models re-use the exact GT06 wire dialect. |

### B. Teltonika — next protocol to build, industry standard

No adapter exists yet, but `pt40-fingerprint.md` branch 2 has already scoped the wire
signature (`00 00 00 00` + Data Field Length + Codec ID `08`/`8E`/`10`, or the `00 0F` IMEI
handshake). Building `Opstrax.Telematics.Protocols.Teltonika` follows the exact pattern
already proven in `Gt06Adapter.cs`.

| Device | Form factor | Notes |
|---|---|---|
| **Teltonika FMB920** | Wired, entry fleet-grade | The most widely deployed professional tracker in this class; built-in accelerometer for real harsh-braking/cornering data. |
| **Teltonika FMC130** | OBD-II plug-in | No wiring — plugs into the OBD-II port, reads engine data directly. Fastest to pilot with a rental/leased vehicle. |
| **Teltonika FMB640** | Wired, heavy-duty CAN/J1939 | Pairs with the J1939 decoder already in this repo (`Opstrax.Telematics.Protocols.J1939`) for real fault-code/fuel/odometer data on trucks. |
| **Teltonika FMB010** | Wired, budget tier | Cut-down FMB920 sibling if the accelerometer isn't needed for a given vehicle class. |

### C. Queclink — alternate protocol, fingerprinted but unbuilt

Listed in `pt40-fingerprint.md` branch 5 (`+RESP:`/`+ACK:`/`+BUFF:` ASCII framing). Worth
knowing if a customer already has these installed; not worth building for speculatively.

| Device | Form factor | Notes |
|---|---|---|
| **Queclink GL300** | Battery, compact | Common consumer/asset-tracking unit; ASCII protocol is easier to hand-inspect while building the adapter than a binary one. |
| **Queclink GV350M** | Wired, mid-range | Typical vehicle-installed unit if a prospective customer's existing hardware needs to be supported rather than replaced. |
| **Queclink GV500** | Wired, OBD-II + J1939 | Advanced tier with engine-bus access, closer to Teltonika's heavy-duty class. |

### D. Heavy-duty / ELD-class (J1939 focus)

For customers who need engine diagnostics (fault codes, fuel, engine hours) rather than
just GPS position — pairs with the existing `Opstrax.Telematics.Protocols.J1939` decoder.

| Device | Form factor | Notes |
|---|---|---|
| **Teltonika FMB640** | Wired, heavy CAN | Already listed in §B — the practical default since it shares the Teltonika adapter work. |
| **Queclink GV500** | OBD-II + J1939 | Also listed in §C — alternative if a fleet already standardizes on Queclink. |
| **Geotab GO9** | OBD-II, ELD-certified | Widely deployed in North American trucking; would require a dedicated adapter (not scoped yet) but is the device US carriers most often already have installed. |
| **Generic J1939-to-serial gateway + existing tracker** | Add-on module | For a vehicle that already has a GT06/Teltonika GPS unit installed, a separate J1939 bridge can add engine data without replacing the GPS hardware. |

---

## 4. Platforms to test from

### 4.1 No hardware, no purchase — test the decoder itself

This repo already ships everything needed to exercise the GT06 pipeline with zero physical
devices and zero network calls:

- **Unit test suite:** `dotnet test telematics/tests/Opstrax.Telematics.Protocols.Tests` runs
  the 30 GT06 fixture tests (`telematics/fixtures/gt06/*.hex`) — login, location, alarm,
  bad-CRC rejection, malformed framing, multi-frame buffers.
- **`tools/telematics/fingerprint.py --self-test`** — offline, read-only, no network, validates
  the fingerprint decision tree against known-good byte strings.
- **`tools/telematics/capture_listener.py`** — binds loopback by default; use it locally to
  practice capturing a frame before pointing it at a real gateway host. A non-loopback bind
  requires an explicit staging flag and acknowledgement (it refuses `production` outright).
- **`tools/telematics/public_replay.py`** — replays a *committed synthetic fixture* (never a
  real capture) at an explicitly allow-listed staging host, one frame at a time, dry-run by
  default. Useful for testing the gateway's ingest path before real hardware is repointed.
- **Integration tests:** `GatewaySmokeTests.cs`, `GatewayTcpSliceTests.cs` in
  `telematics/tests/Opstrax.Telematics.IntegrationTests/` exercise the actual `TcpGatewayService`
  listener end-to-end against an in-process fixture.

This is the cheapest and lowest-risk way to validate changes before touching a real device or
paying for hosting.

### 4.2 Gateway hosting — to receive a real device's TCP connection

Per `ADR-004`, Render (the current API host) cannot terminate raw TCP, so the gateway needs a
different target. For a single-device pilot, skip the multi-region k8s/NLB topology entirely:

| Platform | Why it fits a pilot |
|---|---|
| **Fly.io** | ADR-004's own named pragmatic alternative — supports raw TCP/UDP passthrough with a static IP per app, no Kubernetes cluster to run. Deploy the existing `telematics/Dockerfile` image as-is. |
| **A small VPS (Hetzner, DigitalOcean, Linode)** | Simplest possible option — one box, one public IP, run the gateway binary or container directly, point the device at `<ip>:<port>`. |
| **k8s `Service type=LoadBalancer` + NLB** | The ADR-004 primary target — only worth the operational overhead once device count and multi-region firmware constraints actually demand it. |

### 4.3 SIM / SMS control — to repoint the physical device

The PT40-Q (and most GT06/Teltonika hardware) is repointed via an SMS command to its SIM:

| Option | When to use |
|---|---|
| **A local prepaid SIM with SMS enabled** | Cheapest path for a single pilot device — send the `SERVER=`/`APN=` command from any phone. |
| **Twilio Super SIM** | Programmatic SMS API once more than one device needs repointing — no manual texting per unit. |
| **Hologram** | IoT-focused SIM provider with a dashboard/API for send-to-device SMS and usage across many units. |
| **1NCE** | Flat-rate IoT SIM (SMS + data) aimed at exactly this device class; good fit once piloting moves to a small fleet. |

---

## 5. Reference docs already in this repo

| Doc | What it covers |
|---|---|
| `docs/telematics/pt40/pt40-onboarding-runbook.md` | The 18-step path from "device catalogued" to "one real fix on the map," with explicit gates and the two current blockers |
| `docs/telematics/pt40/pt40-fingerprint.md` | Deterministic protocol-identification decision tree for the PT40-Q, covering all 7 candidate protocols |
| `docs/telematics/COMPLETION_PLAN.md` | Prioritized P0/P1/P2 hardening work (security, replay defense, alarm bridging, harsh-event detection) |
| `docs/telematics/adr/ADR-004-gateway-hosting.md` | Why Render can't host the gateway and what can |
| `docs/telematics/adr/ADR-001` – `ADR-006` | Full architecture decision record set (plane split, event backbone, storage tiers, gateway hosting, idempotent projection, per-gateway identity) |
| `docs/telematics/architecture.md` | Target full cloud-native topology — the long-term destination, not a pilot prerequisite |
| `telematics/fixtures/gt06/README.md` | Byte-level documentation of every GT06 test fixture, with public protocol-spec citations |
| `tools/telematics/README.md` | Usage for the fingerprinting, capture, and replay tools |
