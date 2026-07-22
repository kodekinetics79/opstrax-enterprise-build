# PT40 pilot onboarding runbook — deterministic path to one real fix on the map

**Device under test:** PT40-Q, IMEI `862464068456321`, serial `4C4000067803`.
**Goal:** advance this one physical unit from "catalogued" to "one genuine, decoded,
normalized fix visible on the live map, provably not seeded/simulated/manual, traceable
end-to-end by a single correlation id."

This is a deterministic 18-step path. Each step lists what it does, the concrete
artifact/table/contract that proves it, and its **gate** (what must be true to proceed).
**Steps are executed in order.** Two steps are **externally BLOCKED right now** — they
depend on infrastructure and a device action we do not yet control; every step after them
is *blocked-downstream* and cannot be genuinely completed until the two root blockers
clear. The blockers and their smallest unblock action are called out inline and summarized
at the end.

> Non-negotiable honesty gate (from `docs/TELEMATICS_INTEGRATION.md`): a registry row or
> a simulated fix does **not** make PT40 "live." Do not mark it Active, do not claim its
> protocol, and do not show it on the map as real until a valid **signed** packet is
> observed end-to-end.

Legend: **[READY]** doable now · **[BLOCKED]** externally blocked now · **[DOWNSTREAM]**
unblocks automatically once the two root blockers clear.

---

## The 18 steps

### 1. Register identity — **[READY]**
Create/verify the device record keyed by IMEI + serial. In this build the registry is the
`eld_devices` table (`imei`, `device_serial`, `device_model`, `provider`,
`firmware_version`, `status`). Lifecycle contract: `DeviceLifecycleState.Draft →
Provisioned`. IMEI/serial are **identifiers, never credentials**
(`DeviceIdentityRef` — "treat every field as attacker-controlled input").
**Gate:** exactly one non-deleted row resolves for `imei='862464068456321'` (the
`gps-ingest` lookup also matches `device_serial == imei`).

### 2. Link tenant — **[READY]**
Bind the device to its owning tenant/company (`eld_devices.company_id`, one of the 4 real
companies). Ownership will be read from this row at ingest and written to
`CanonicalTelemetryEvent.TenantId/CompanyId` — **never** from the packet
(`IDeviceRegistry` fail-closed contract: null resolution ⇒ reject, never invent a tenant).
**Gate:** `company_id` set to the real pilot tenant; `ResolveAsync` would return a
non-null `ResolvedDeviceOwner`.

### 3. Assign vehicle — **[READY]**
Bind the device to a real vehicle (`eld_devices.vehicle_id`). The map only plots fixes that
carry a `vehicle_id`; `gps-ingest` upserts `latest_vehicle_positions` **only when
`vehicleId is not null`**. Lifecycle: `Provisioned → AwaitingAssignment →
AwaitingConfiguration`.
**Gate:** `vehicle_id` points at a real vehicle in the same company.

### 4. Record model / firmware / **protocol evidence** — **[READY, protocol stays UNKNOWN]**
Record `device_model` (PT40-Q) and `firmware_version` from the physical unit. **Leave
protocol UNKNOWN** — we have no manufacturer doc and no capture yet. Protocol is filled in
only after step 7's capture is fingerprinted per `pt40-fingerprint.md`. A model name is not
protocol evidence.
**Gate:** model/firmware recorded from the device; protocol field explicitly empty/"unknown"
with a note that it is pending capture.

### 5. Set destination host:port on the device (APN + server) — **[BLOCKED]**
Point the tracker at the gateway: configure APN and the destination `host:port` it will
open a TCP session to. On PT40/Concox-class hardware this is done by **SMS server
command** to the SIM (e.g. a `SERVER,` / `APN,` command), or a vendor config tool.
**Why blocked:** we cannot yet repoint the device — it requires sending the SMS server
command to the unit's SIM (SMS-capable path + the correct command set), which we do not
have provisioned. **Smallest unblock:** obtain the SIM's SMS command channel and send the
one `SERVER=<gateway-host>:<port>` (+ `APN`) command; confirm the device ACKs the new
destination. Until then the device has nowhere to send bytes.
**Gate:** device reports the new server/APN accepted.

### 6. Stand up a gateway that can receive its transport — **[BLOCKED]**
A TCP-capable gateway host must be listening on that `host:port`, accept the connection,
run protocol auto-detection, and buffer frames. **Status correction (verified
2026-07-14):** the gateway is **no longer an idle skeleton**. `TcpGatewayService`
(a `BackgroundService`) binds a real `TcpListener` in `StartAsync`, runs
`IProtocolAdapter.TryIdentify` + framing over a connection, and its slice is covered by
`GatewayTcpSliceTests`/`GatewaySmokeTests`. The projection path is wired too:
`PostgresPositionProjectionStore` upserts the **same `latest_vehicle_positions`** the map
reads (idempotency inbox + monotonic `device_fix_time` upsert, migration 006). **What is
still blocked is DEPLOYMENT, not code:** the listener binds `IPAddress.Loopback` only and
**no publicly reachable host runs it**, so the physical device has nowhere on the public
internet to connect. Raw-TCP hardware trackers still cannot speak to
`/api/telemetry/gps-ingest` directly — that endpoint is HTTP+HMAC for a *gateway*, not the
device. **Smallest unblock:** deploy `TcpGatewayService` on a publicly reachable host
(bind a routable interface, not loopback) at the step-5 `host:port`, holding the
`Telemetry:GatewaySecret` (≥32 chars) so it can sign forwards / write the projection.
**Root blocker #1 (now a DEPLOYMENT/INFRA blocker, not a build task).**
**Gate:** a reachable host accepts a TCP connection on the destination and buffers bytes.

### 7. Capture ONE real packet — **[DOWNSTREAM of 5 + 6]**
When the device connects and sends its first frame, capture the **raw bytes verbatim**
(the gateway's framing buffer, or a `tcpdump`/pcap on the listener). One frame is enough
to fingerprint and to prove the device is transmitting.
**Gate:** a raw byte capture exists whose source is the physical PT40-Q session (not a
fixture). This is the first artifact that could not have come from seed/simulator.

### 8. Identify + authenticate — **[DOWNSTREAM]**
Two distinct acts:
- **Identify the protocol** from the captured bytes using the deterministic tree in
  `pt40-fingerprint.md` (and/or `tools/telematics/fingerprint.py`). Record the confirmed
  `ProtocolName`/version (this retro-fills step 4's protocol field with real evidence).
- **Authenticate the gateway→API forward:** the gateway POSTs to
  `/api/telemetry/gps-ingest` with `X-Gateway-Timestamp` + `X-Gateway-Signature`
  (hex HMAC-SHA256 of `"<timestamp>.<raw-json>"` under `Telemetry:GatewaySecret`).
  The API rejects if the secret is `<32` chars (503), if the timestamp skews `>300s`, if
  the HMAC fails (401), or if the `timestamp:signature` pair replays (409). The IMEI is
  only a **lookup key**, never authentication. Device identity is then resolved from
  `eld_devices` (fail-closed: unknown ⇒ 404; not `active/provisioning/pending` ⇒ 403).
**Gate:** protocol confirmed; forward passes HMAC + freshness + replay + device-status
checks.

### 9. Archive the raw frame — **[DOWNSTREAM]**
Preserve the exact bytes for audit/replay/forensic re-decode. `DecodedMessage.RawFrame`
holds the verbatim single frame (defensively copied); the captured pcap/hex from step 7 is
the durable archive. Nothing downstream may mutate it.
**Gate:** raw frame stored and linked to the correlation id (step 17).

### 10. Decode — **[DOWNSTREAM]**
The matching `IProtocolAdapter` frames + decodes the bytes into `DecodedMessage`
(`MessageType`, adapter-local `Fields`, any `Identity` claim, `ProtocolMessageId`,
`RequiresAck`). Adapters are **pure/stateless** — no I/O, no ownership resolution. For a
GT06-family confirmation the decoder is `Gt06Adapter` (`ProtocolName="GT06"`,
`AdapterVersion="1.0.0"`) — **implemented and tested (30 cases)**, not a placeholder, so
this step runs as soon as a real frame fingerprints GT06.
**Gate:** at least one complete `DecodedMessage` with a `Location` (or a Login carrying the
IMEI claim) is produced; `consumed > 0`.

### 11. Normalize — **[DOWNSTREAM]**
Map the adapter fields into one `CanonicalTelemetryEvent` (schema v1): registry-resolved
`TenantId/CompanyId/DeviceId/VehicleId`, the three distinct clocks
(`OccurredAtDeviceUtc`, `ReceivedAtGatewayUtc`, `NormalizedAtUtc`), `GeoPoint`, VSS
signal bag, and provenance. **Ownership is taken from the registry, never the packet.**
In this build the JSON forward to `gps-ingest` is the normalization boundary (aliases like
`latitude`/`speedKmh`/`course`/`gpsTime` collapse to canonical fields; km/h → mph).
**Gate:** a canonical event exists with `Source = DirectDevice`, `Transport = Tcp`,
`ProtocolName`/`AdapterName`/version set from steps 8/10.

### 12. Validate — **[DOWNSTREAM]**
Run plausibility/quality checks before persisting. `gps-ingest` enforces: coordinate
validity (`IsCoordinateValid`), speed 0–250 mph, heading 0–359, fuel 0–100, odometer ≥0,
and device timestamp within `[-30 days, +5 min]`. The canonical model expresses richer
checks via `QualityFlags` (out-of-order, replay, stale, clock-skew, teleport, impossible
speed, jamming) feeding `TrustScore`/`Confidence`.
**Gate:** values in bounds; event is `QualityFlags.IsClean` (or its flags are recorded).

### 13. Update latest position — **[DOWNSTREAM]**
Persist the fix: insert into `location_events` (`source='gps-tracker'`,
`source_channel='trusted-gateway'`) and upsert `latest_vehicle_positions`
(`ON CONFLICT (company_id, vehicle_id)`), advancing only when the new `event_time` is newer
(monotonic, ordered latest-position update). This is the same table the map reads.
**Gate:** one new `location_events` row and an updated `latest_vehicle_positions` row for
this vehicle.

### 14. Publish via the backbone — **[DOWNSTREAM]**
Surface the fix on the live channel: the SSE stream `GET /api/telemetry/stream` (authorized
via a short-lived `stream-ticket`) and the positions snapshot endpoints. A valid signed fix
also advances device lifecycle (`status: provisioning|pending → Active`, heartbeat fields
touched) — the **only** path to Active.
**Gate:** the new position is observable on the stream / snapshot for the pilot tenant.

### 15. Show on map — **[DOWNSTREAM]**
The GPS Tracking page (`/gps-tracking`) plots `latest_vehicle_positions`; the vehicle's
marker moves to the captured coordinate. Because `withFallback` seed overlay was removed,
the marker rendering *is* the live row — no fixture behind it.
**Gate:** the PT40-bound vehicle appears at the real captured lat/lng, freshly timestamped.

### 16. Display source / age / confidence / protocol / gateway — **[DOWNSTREAM]**
The surfaced fix must show its provenance honestly: **source** (`DirectDevice` /
`source='gps-tracker'`), **age** (now − `event_time`; decays toward Stale/Offline
≈15 min without a follow-up fix), **confidence** (`CanonicalTelemetryEvent.Confidence` /
`TrustScore`), **protocol** (the confirmed name from step 8), and **gateway**
(`source_channel='trusted-gateway'` — which forwarder). No value may be shown that the
backend did not supply; missing values render `—`.
**Gate:** all five attributes displayed from real fields, none hard-coded.

### 17. Preserve one correlation id end-to-end — **[DOWNSTREAM]**
A single `CorrelationId` (GUID) minted at capture must ride through decode → normalize →
validate → persist → publish, so the raw frame, the `location_events` row
(`correlation_id`), the canonical event, and the stream message all point back to the same
originating packet. `CanonicalTelemetryEvent.CorrelationId` is the contract field; the
ingest payload accepts a `correlationId`.
**Gate:** grepping that one id turns up the raw capture, the DB row, and the published
event — one thread, no gaps.

### 18. Prove NOT seeded / simulated / manual — **[DOWNSTREAM]**
Final acceptance. Demonstrate the fix's provenance is a real device:
- `TelemetrySource == DirectDevice` (not `Seed`, `Simulator`, or `Manual`);
  `source='gps-tracker'`, `source_channel='trusted-gateway'` on the row.
- It arrived via the HMAC-signed gateway forward (step 8), passed replay/freshness, and
  came from the archived raw capture (step 9) — reproducible by re-decoding those bytes.
- It is **not** attributable to `TelemetrySimulatorBackgroundService` (that writes
  `Simulator`-class rows) or the demo seeder (`Seed`).
- The correlation id (step 17) traces to a physical PT40-Q TCP session, not a fixture.
**Gate:** all four hold. Only now is PT40 "live" for real.

---

## What is externally BLOCKED right now, and the smallest unblock

Two **root** blockers gate everything from step 7 onward. Neither is a code bug — both are
external infrastructure/device actions.

| Root blocker | Step(s) | Why it's blocked | Smallest unblock action |
|--------------|---------|------------------|-------------------------|
| **#1 — No *deployed* public gateway host** | **6** (root); 7–18 downstream | Gateway CODE exists and is tested — `TcpGatewayService` binds a `TcpListener` and runs `TryIdentify` + framing; `PostgresPositionProjectionStore` writes the map's `latest_vehicle_positions`. But it binds `IPAddress.Loopback` and **no publicly reachable host runs it**, so the tracker has nowhere to connect. This is now a DEPLOY/INFRA gap, not a code gap. | Deploy `TcpGatewayService` on a reachable host (bind a routable interface, not loopback) at the step-5 `host:port`, holding a ≥32-char `Telemetry:GatewaySecret`. No new adapter code needed for a GT06-family device. |
| **#2 — Device not repointed (SMS server command)** | **5** (root); 7–18 downstream | We cannot yet send the SMS `SERVER=`/`APN=` command to the PT40-Q's SIM, so the device has no destination `host:port` to transmit to. | Get the SIM's SMS command channel and send one `SERVER=<gateway-host>:<port>` (+ `APN=`) command; confirm the unit ACKs the new destination. |

**Steps 1–4 are executable now** (identity, tenant link, vehicle assignment, model/firmware
evidence — protocol deliberately left UNKNOWN). **Step 7 (capture) is the first step that
requires both root blockers cleared**; once a real packet is captured, steps 8–18 are
ordinary engineering with no further external dependency (they need the fingerprint tree,
the relevant `IProtocolAdapter` implemented — GT06's is still a placeholder — and the
existing ingest/stream/map plumbing, all of which are in-repo).

**Do not fake progress past step 4.** Using the simulator or a hand-inserted row to make the
map "look alive" for PT40 fails step 18 by construction and violates the commissioning gate.
