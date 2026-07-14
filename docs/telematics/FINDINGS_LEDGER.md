# Telematics Fabric — Findings Ledger

Continuous hardening loop. States: NEW · CONFIRMED · IN_PROGRESS · FIXED · VERIFIED ·
BLOCKED_EXTERNAL · BLOCKED_DECISION · ACCEPTED_RISK · FALSE_POSITIVE.
Evidence classes: **VERIFIED** (repo/runtime/test proof) · **INFERRED** · **BLOCKED**.

Identifiers for the client pilot unit are redacted in this ledger (`IMEI …6321`,
`serial …7803`); exact values are held only in the source/config that needs them for
deterministic lookup.

---

## Cycle 1 — 2026-07-14

Baseline captured before any change:
- `telematics/Opstrax.Telematics.sln` — **95 tests pass** (32 protocol, 34 security, 29
  integration), `dotnet test`, 0 failed.
- `backend-dotnet.Tests` source-inspection suite builds and runs DB-free (Postgres-category
  tests require a live DB, not reachable in this environment).

### TEL-P1-TRUTH-003 — Freshness label derived from receipt time, not fix time — **FIXED / VERIFIED (in-repo)**

- **Severity:** P1 (truth model — "false live-location presentation" class).
- **Module:** live map delivery (REST `/api/telemetry/positions`, SSE
  `/api/telemetry/stream`), frontend provenance rendering.
- **Evidence (VERIFIED):** both endpoints computed `freshness` (`live`/`delayed`/`stale`)
  from `EXTRACT(EPOCH FROM (NOW() - lvp.received_at))` only. `gps-ingest` stamps
  `received_at = NOW()` on every write and accepts a device `event_time` as old as
  **−30 days**. So an offline-buffered batch dump — or a frame replayed with a fresh
  gateway timestamp but a stale device clock — lands with `received_at = NOW()` and was
  labeled **`live`** while the actual GPS fix was hours/days old. The frontend made it
  worse: it derived its bucket from `secondsSincePing` (receipt age) and only *fell back*
  to the server string, so it trusted the wrong basis even when the server sent a value.
- **Root cause:** conflating "pipeline delivered a row recently" with "the position is
  current now." The two are distinct and diverge exactly under the adversarial/edge cases
  the truth model targets.
- **Fix (smallest durable):**
  - `backend-dotnet/Controllers/EndpointMappings.cs` — both `freshness` sites now classify
    off `freshAge`: when provenance columns exist, `GREATEST(receipt-age,
    COALESCE(device_fix_time, received_at)-age)` so the OLDER of the two wins; pre-migration
    (columns absent) it degrades to `received_at`-only, unchanged. `seconds_since_ping` and
    `is_stale` keep their receipt-age meaning (distinct signals), so blast radius is limited
    to the fix-currency label.
  - `frontend/src/utils/telemetryProvenance.ts` — new `bucketFromServerFreshness()` makes
    the server (authoritative) freshness the primary signal.
  - `frontend/src/components/LiveMap.tsx`, `frontend/src/pages/LiveMapPage.tsx` (marker,
    roster row, Fix-Provenance drawer) — prefer server freshness, fall back to client
    receipt-age bucket only when the server sent none.
- **Tests added:** `backend-dotnet.Tests/TelemetryFreshnessProvenanceTests.cs` (2 tests)
  pin the honest-age expression on both surfaces — a genuine red→green guard (the
  `GREATEST`/`COALESCE(device_fix_time…)` it asserts did not exist pre-fix).
- **Commands:** `dotnet test --filter TelemetryFreshnessProvenanceTests|EndpointMappingsSecurityHardeningTests`
  → **14 passed**. `npx tsc --noEmit` (frontend) → **0 errors**.
- **Regression scope:** freshness label only; receipt-age fields untouched; deploy-safe
  fallback preserved. **Remaining risk:** the −30-day `event_time` acceptance window in
  `gps-ingest` is still wide (a separate hardening item — see TEL-P2-INGEST-004).

### TEL-P2-DOC-002 — PT40 onboarding docs drifted from code — **FIXED / VERIFIED**

- **Severity:** P2 (documentation drift that misstates the blocker set — made real,
  shippable code look like unfinished work, and hid that the true blocker is deployment).
- **Evidence (VERIFIED):** docs claimed the GT06 adapter was a "compile-time / name-only
  placeholder" and the gateway "`GatewayWorker` binds no listener (idle skeleton)." In fact
  `Gt06Adapter : IProtocolAdapter` implements `TryIdentify` + CRC-ITU `Decode` (30 tests),
  and `TcpGatewayService` binds a real `TcpListener` in `StartAsync` (`GatewayTcpSliceTests`).
  There is no `GatewayWorker` type. The real gap is that the listener binds
  `IPAddress.Loopback` and nothing deploys it publicly.
- **Fix:** corrected `docs/telematics/pt40/pt40-fingerprint.md` (branch 1 adapter note) and
  `docs/telematics/pt40/pt40-onboarding-runbook.md` (steps 6, 10, root-blocker table) to
  state the code exists/tested and reframe root blocker #1 as a **deploy/infra** gap, not a
  build task.
- **Remaining risk:** none in-repo.

### TEL-P1-PT40-001 — Real PT40-Q client pilot: verify & complete the physical path — **BLOCKED_EXTERNAL / UNVERIFIED**

Full investigation and deliverable below. Root blockers are external
(device-config + gateway deployment + physical/DB access); no in-repo code bug gates it.

### TEL-P1-DEPLOY-006 — Gateway could not be deployed (bound loopback only) — **FIXED / VERIFIED**

- **Severity:** P1 (this was the code half of PT40 root blocker #1 — the client's pilot
  could not go live because the gateway physically could not be exposed).
- **CTO/CIO decision:** prioritized the client (Khalid) pilot critical path over speculative
  hardening. The one engineering lever on that path was the hardcoded loopback bind.
- **Evidence (VERIFIED):** `TcpGatewayService.StartAsync` created
  `new TcpListener(IPAddress.Loopback, port)` — no interface was configurable, so the
  gateway was undeployable as a device edge regardless of infra.
- **Fix:** added `GatewayOptions.ListenAddress` (default `127.0.0.1`, safe-by-default) +
  `ResolveListenAddress()` that **fails closed to loopback** on any empty/unparseable value
  (a bad config must never widen exposure); `TcpGatewayService` binds it and logs a WARNING
  on any non-loopback bind. Documented in `appsettings.json`.
- **Tests added:** `GatewayBindAddressTests` (9 cases: default-loopback, explicit
  `0.0.0.0`/`::`/specific honored, empty/whitespace/garbage/null → loopback).
- **Commands:** telematics `dotnet test` → **104 passed** (was 95). **Remaining:** the other
  half of the blocker (an actual public host + firewall) is infra/ops, captured in the
  go-live checklist.
- **Deliverable:** `docs/telematics/pt40/pt40-go-live-checklist.md` — turnkey ops/device/DB
  steps (register device → Khalid's tenant with exact SQL; deploy gateway; SMS repoint;
  capture + fingerprint + verify) so the external blockers are an executable checklist.

---

## Cycle 2 — 2026-07-14 (production access granted)

Owner confirmed the Neon instance is **production** and authorized the writes below.
All prod writes were read-back-verified, transactional, and idempotent.

- **TEL-P0-INGEST-007 — FIXED/VERIFIED (prod):** `imei` column was absent in prod, but
  `GpsTrackerIngest` resolves `WHERE imei=@imei OR device_serial=@imei` unconditionally →
  `column "imei" does not exist` (500) on every hardware-tracker request. Migration
  `2026_07_11_stage32_device_imei.sql` had never been applied. Applied it (column +
  `ux_eld_devices_imei` unique partial index); verified present. GPS ingest now resolves.
- **TEL-P1-ONBOARD-008 — FIXED/VERIFIED (code):** provisioning API had no `Imei` field, so
  every future GPS-tracker client needed manual SQL. Added optional `Imei` to
  `DeviceProvisionBody` + INSERT (blank→null, trimmed), serial-vs-IMEI conflict message,
  IMEI in audit; added `imei` col + unique index to `TelemetrySchemaService` so owner envs
  self-heal. General fix, not client-specific. Backend builds clean.
- **Khalid pilot control-plane (tenant DATA, authorized):** vehicle `KHALID-PILOT-01`
  (id 1024) created in company 8; device id 1011 registered (serial `…7803`, IMEI `…6321`,
  PT40-Q, `status='provisioning'`) and bound. `gps-ingest` resolution query returns the row.

---

## Open items carried forward

| ID | Sev | Title | Status | Blocker |
|---|---|---|---|---|
| TEL-P1-PT40-001 | P1 | Real PT40-Q physical telemetry (data plane) | BLOCKED_EXTERNAL | device repoint (SMS) + public gateway deploy; control-plane DONE |
| TEL-P1-ONBOARD-008b | P1 | Surface `imei` on admin device UI / detail responses | NEW | none (in-repo; next cycle) |
| TEL-P2-INGEST-004 | P2 | `gps-ingest` accepts device `event_time` up to −30 days | NEW | none (in-repo) |
| TEL-P1-REPLAY-005 | P1 | `gps-ingest` replay cache in-process/static (per-instance, lost on restart) | NEW | none (in-repo; needs DB-backed guard) |

---

# Client PT40-Q Pilot Status — TEL-P1-PT40-001

**Classification: UNVERIFIED — BLOCKED.** No authenticated packet from this physical unit
has been captured or traced end to end in this environment, and production data stores are
not reachable here, so physical telemetry cannot be asserted. This is the honest status;
nothing below is inferred from the model name or a UI state.

| Field | Result | Evidence |
|---|---|---|
| Model | PT40-Q | Client label |
| IMEI match | ✅ **REGISTERED (verified in prod 2026-07-14)** | `eld_devices` id 1011, `imei` = `…6321`. Was absent before; provisioned to Khalid's tenant. VERIFIED by direct query. |
| Serial match | ✅ **REGISTERED** | Same row, `device_serial` = `…7803`, unique. VERIFIED. |
| Tenant/company | ✅ **company_id 8 — "Test Company for Khalid"** | Device bound to Khalid's real tenant (was empty before). VERIFIED. |
| Assigned vehicle | ✅ **vehicle_id 1024 — `KHALID-PILOT-01`** | Vehicle created in company 8, device bound to it; map will plot fixes for it. VERIFIED. |
| Current device lifecycle state | **PROVISIONED (`status='provisioning'`)** | Registered + assigned, awaiting first signed fix. Active is reachable only via a valid `gps-ingest` fix (none yet). The exact `gps-ingest` resolution query now returns this row. VERIFIED. |
| Actual protocol | **UNKNOWN** | No manufacturer doc, no captured byte. PT40-Q is *reputationally* GT06/Concox-family but that is **not** protocol evidence. BLOCKED_PROTOCOL_EVIDENCE. |
| Transport | Presumed raw TCP (unconfirmed) | Inferred from the reputed family only; confirm from bytes per `pt40-fingerprint.md`. BLOCKED_PROTOCOL_EVIDENCE. |
| Destination host/IP | **Not configured** | Device not repointed; no `SERVER=` sent to the SIM. BLOCKED_DEVICE_CONFIGURATION. |
| Destination port | **Not configured** | Same as above. |
| First packet receiver | None yet | No gateway deployed publicly to receive it. BLOCKED_GATEWAY_INFRASTRUCTURE. |
| Gateway/vendor involved | In-repo gateway only (loopback, not deployed) | `TcpGatewayService` exists + tested but binds `IPAddress.Loopback`; no Traccar/Wialon/Flespi/Navixy/vendor cloud wired in repo. |
| Last authenticated packet | **None** | No signed forward for this identity observed; production logs not reachable. BLOCKED. |
| Last device fix | **None proven** | No `location_events` for this IMEI confirmable here. BLOCKED_PHYSICAL_ACCESS. |
| Last gateway receipt | **None** | No gateway receipt for this unit. BLOCKED. |
| Position source | **None proven live** | If any map dot exists for it, it would be SEEDED_OR_MANUAL until a signed fix proves otherwise — cannot verify without DB. |
| Simulator involved | Not for this unit | Simulator writes `Simulator`-class rows; no evidence it targets this IMEI. |
| Seed data involved | No seed row for this unit | IMEI not present in any seed/migration file (grep VERIFIED). |
| Raw packet retained | **No** | Capture tooling exists (`tools/telematics/capture_listener.py`, replay guard, store-and-forward) but no capture from this physical unit. BLOCKED_PHYSICAL_ACCESS. |
| Adapter/version | `Gt06Adapter` v1.0.0 *ready IF* GT06 confirmed | Adapter implemented + tested; not yet exercised against this device's bytes. |
| End-to-end trace | **Absent** | No correlation id threads a real packet from this unit through decode→projection→map. |
| Final classification | **UNVERIFIED — BLOCKED** | See blockers below. |
| Exact blocker / next action | (1) Repoint device via SMS `SERVER=<host:port>` + `APN`; (2) deploy `TcpGatewayService` on a public host (bind routable interface, ≥32-char `Telemetry:GatewaySecret`); (3) capture ONE frame, fingerprint per `pt40-fingerprint.md`; (4) with production DB access, confirm/complete the `eld_devices` row (IMEI/serial → company + vehicle). | |

## Proven vs missing architecture

```
PT40-Q
  → cellular network                         [assumed OK — SIM active per client]
  → destination host:port                    MISSING — device not repointed (BLOCKED_DEVICE_CONFIGURATION)
  → protocol gateway (TcpGatewayService)     CODE EXISTS + TESTED, but LOOPBACK-ONLY / NOT DEPLOYED (BLOCKED_GATEWAY_INFRASTRUCTURE)
  → authentication (per-device + HMAC fwd)   EXISTS (DefaultDeviceAuthenticator; gps-ingest HMAC; IMEI = lookup only)
  → decoder (Gt06Adapter, IF GT06)           EXISTS + TESTED (30 cases); protocol for THIS unit still UNKNOWN
  → canonical telemetry (CanonicalTelemetryEvent) EXISTS (schema v1)
  → event backbone / projection inbox        EXISTS (InMemoryEventBackbone; telemetry_projection_inbox, migration 006)
  → latest_vehicle_positions                 EXISTS (PostgresPositionProjectionStore, monotonic device_fix_time upsert)
  → API/SSE (/positions, /stream)            EXISTS (tenant-scoped; provenance + honest freshness — TEL-P1-TRUTH-003)
  → Live Map                                 EXISTS (provenance badges + honest freshness)
```

The software spine from gateway to map is present and largely tested. The path is broken at
the **physical edge and its deployment**: the device has no destination configured, and no
public host runs the gateway. Those two are external (device SMS command + infra deploy),
not code defects — consistent with the STOP condition B classification for this workstream.
