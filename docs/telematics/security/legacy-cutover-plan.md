# OpsTrax Telematics — Legacy gps-ingest Cutover Plan

**Purpose:** a **staged, non-breaking** plan to migrate the live production
`POST /api/telemetry/gps-ingest` endpoint off the single shared
`Telemetry:GatewaySecret` and onto **per-device / dual credential** authentication —
without dropping a single fix from a correctly-configured device in flight.

**Status:** PLAN ONLY. This document changes no code. It does not modify
`backend-dotnet/`. It is the operational sequencing that the backend team executes;
the target credential design is `identity-trust-architecture.md` (Stages A–F there map
to Phases below).

**Companion docs:** `identity-trust-architecture.md` (target design), `threat-model.md`
(threats S1/S2/T1/T4/E3/I1 + the replay gap this closes).

---

## 0. Guardrails (read before touching anything)

1. **Never break a good device.** At no phase is an existing, correctly-configured
   gateway/device rejected until (a) a stronger credential is available to it **and**
   (b) telemetry confirms it has adopted the stronger credential. Enforcement is always
   gated behind an **observed-adoption metric** and a **per-entity flag**, never a
   big-bang flip.
2. **Additive & reversible.** Every schema change is `IF NOT EXISTS` / additive; every
   behavior change ships behind a config flag defaulted to today's behavior; every phase
   has an explicit, time-boxed rollback below.
3. **The shared secret keeps working until the last phase.** It is retired only after
   metrics show 100% adoption of the replacement, and even then a documented rollback
   re-enables it.
4. **Honesty about raw GT06.** Cheap raw-TCP trackers (GT06/Concox) cannot present a
   per-device cryptographic proof at the protocol level. For those, the replacement for
   the fleet secret is a **per-gateway credential (mTLS + `kid`)** on the forwarder that
   fronts them — not a per-device HMAC on the device itself. Per-device HMAC applies to
   devices/firmware/SDKs that post to the HTTP path directly. The plan supports both.

---

## 1. Current live state (verified against `backend-dotnet/Controllers/EndpointMappings.cs`)

`GpsTrackerIngest` (≈L10995) today:

| Property | Implementation | Note |
|---|---|---|
| Gateway auth | single global `config["Telemetry:GatewaySecret"]`; HMAC-SHA256 over `{timestamp}.{rawPayload}`; headers `X-Gateway-Timestamp` / `X-Gateway-Signature`; 503 if secret < required length | one key = whole fleet (threat S1) |
| Freshness | `X-Gateway-Timestamp` within ±300 s | |
| Replay | in-memory `ConcurrentDictionary GpsGatewayReplayCache`, key `{timestamp}:{signature}`, pruned inline on next request | per-process, non-durable (D2) |
| Device identity | `X-Device-IMEI` header **or** body `imei`/`deviceId`/`id`; lookup `WHERE (imei=@imei OR device_serial=@imei)` | IMEI is a spoofable index (S2) |
| State gate | accepts `active`, `provisioning`, `pending`; else 403 | |
| Auto-activation | `UPDATE eld_devices SET status = CASE WHEN LOWER(status) IN ('provisioning','pending') THEN 'Active' ELSE status END` on any accepted fix | ingest promotes state (T4) |
| Tenant binding | `company_id`/`vehicle_id`/`driver_id` from the device row (not the body) — good, but the row is chosen by attacker-supplied IMEI | |
| Write | `RunInSystemScopeAsync` → `INSERT location_events` (`source_channel='trusted-gateway'`) + `UPSERT latest_vehicle_positions` (RLS bypassed) | no RLS backstop (T1) |

The **reference model already exists** in the same file: `TelemetryIngest`
(`/api/telemetry/ingest`) uses per-device `api_key_hash` + `hmac_secret`, a **durable**
`telemetry_nonces (device_id, nonce)` table with a `UNIQUE` constraint + `23505`-as-replay,
and a strict `status='Active'` gate. The cutover's job is to bring gps-ingest up to that bar
without an outage. `eld_devices` already carries `imei`, `device_serial`, `api_key_hash`,
`hmac_secret`, `status`, and (from `telematics 002`) `device_state`.

---

## 2. Target end state

- Every accepted gps-ingest request is attributable to a **specific, individually revocable
  credential**: a per-gateway key (`kid` + optionally mTLS cert) for forwarded raw trackers,
  and/or a per-device HMAC for capable devices.
- Replay defense is **durable and shared across instances** (same guarantee as
  `telemetry_nonces`), plus an optional monotonic per-device sequence.
- Tenant is derived **only** from an authenticated identity; a forged IMEI resolves to no
  writable tenant.
- No ingest-time state promotion; activation is an explicit, authenticated provisioning action.
- The shared `Telemetry:GatewaySecret` is removed from config.

---

## 3. Rollout mechanics (how every phase stays safe)

- **Feature flags** (config or `tenant.config`): `GpsIngest:PerGatewayAuth`,
  `GpsIngest:DurableReplay`, `GpsIngest:PerDeviceSignature`, `GpsIngest:RequireMtls`,
  `GpsIngest:FailClosedTenant`, `GpsIngest:ShadowMode`. All default **off** (= today's behavior).
- **Per-entity opt-in columns** (additive): `telemetry_gateways.status/trust_tier`,
  `eld_devices.require_device_signature bool default false`.
- **Shadow-first for every enforcement.** Each new check runs in *observe* mode first:
  compute the verdict, **log/count it, but still honor the legacy decision**. Only after the
  shadow metric shows the new check would not have wrongly rejected real traffic do we flip it
  to enforce — per entity.
- **Adoption metrics gate promotion.** No phase advances to enforcement until its dashboard
  shows the target population at ~100% on the new path for a defined soak window.

---

## 4. Phases

### Phase 0 — Instrumentation & schema (no behavior change)

**Change:** Add the target tables additively — `telemetry_gateways`,
`telemetry_gateway_devices`, `telemetry_gateway_nonces` (per `identity-trust-architecture.md`
§4.1/§5.1). Register the current implicit gateway as one row `gateway_code='legacy-shared'`
whose signing key **is** the existing `Telemetry:GatewaySecret` (store only its hash). Add
counters/structured logs to `GpsTrackerIngest`: resolved gateway, device id, device `status`,
and whether a per-device signature *would* have validated **if present** — all observe-only.

**Backward-compat:** Zero. No request is rejected differently. Shared secret still authorizes.

**Shadow / observability:** New dashboard "gps-ingest identity" — requests/sec by (gateway,
device state, has-device-signature, has-gateway-id). Establishes the baseline.

**Rollback:** Drop the (unused) tables; remove the logging. No traffic impact.

### Phase 1 — Durable, shared replay (drop-in, safe)

**Change:** Behind `GpsIngest:DurableReplay`, replace the in-memory `GpsGatewayReplayCache`
with the **atomic-INSERT** pattern against `telemetry_gateway_nonces`, exactly as
`TelemetryIngest` does with `telemetry_nonces` (`INSERT … UNIQUE`, catch Postgres `23505` →
reject as replay). Keep a small bounded in-memory LRU only as a fast-path. Extend the existing
`TelemetryBackgroundService` prune to cover the new table.

**Backward-compat:** Full. Gateways send the **same** headers; only where the replay record
lives changes. A device that was accepted before is still accepted.

**Shadow / observability:** Run in shadow first — record to the durable table **and** the old
cache; alert if the durable check would reject something the cache accepted (should be only
genuine cross-instance replays, which is the win). Then flip to durable-authoritative.

**Rollback:** Flip `GpsIngest:DurableReplay` off → instantly back to the in-memory cache. The
nonce table is harmless if unused.

**Result:** Closes threat D2 (restart/scale-out replay window) with no change to what gateways
must send. This is the highest safety-to-effort phase; do it first.

### Phase 2 — Per-gateway keys alongside the shared secret (dual-validate)

**Change:** Behind `GpsIngest:PerGatewayAuth`, accept **either** the legacy fleet HMAC **or** a
per-gateway `kid`-scoped signature: header `X-Gateway-Id: <gateway_code>` → load that gateway's
key → verify the signature over the canonical `{gateway_code}\n{timestamp}\n{sequence}\n
sha256hex(payload)`. Provision real gateway rows/keys and onboard forwarders **one at a time**
to their own key. Legacy shared-secret requests (no `X-Gateway-Id`) keep working.

**Backward-compat:** Full during dual-run. A gateway is migrated only when it has been issued and
has adopted its own key; until then it uses the shared secret.

**Shadow / observability:** Metric "% of gps-ingest traffic on per-gateway keys", split by
gateway. Migrate → watch that gateway's error rate stay flat for its soak window before moving
the next.

**Rollback:** Per gateway — revert that forwarder to the shared secret (its legacy path is still
live). Global kill: flip `GpsIngest:PerGatewayAuth` off.

### Phase 3 — Per-device proof for capable devices (dual-mode, per-device opt-in)

**Change:** Behind `GpsIngest:PerDeviceSignature`, accept an optional `X-Device-Signature`
computed with the device's `hmac_secret` (reuse `TelemetryHmacHelper.ComputeSignature` /
`ConstantTimeEquals`, the same primitive `TelemetryIngest` uses). Enforcement is **per-device**:
a device flagged `eld_devices.require_device_signature=true` must send a valid one; all others
still work exactly as today. Populate `telemetry_gateway_devices` (which gateway may report for
which device) in observe mode.

**Backward-compat:** Full. IMEI stays the **index**; the signature (where required) becomes the
**authenticator**. Devices are flipped to `require_device_signature=true` in waves as fleets
update firmware.

**Shadow / observability:** For each device, log "would the required-signature check have passed"
before setting its flag. Only set the flag once the shadow shows the device is reliably signing.
Dashboard: % of active devices requiring + passing a device signature.

**Rollback:** Per device — clear `require_device_signature`. Global: flip
`GpsIngest:PerDeviceSignature` off.

> Honest scope: raw GT06 trackers that post only via a forwarder cannot self-sign; their
> assurance comes from Phase 2 (per-gateway) + Phase 5 (mTLS) + the `telemetry_gateway_devices`
> authorization scope, not from a device signature. Phase 3 targets devices/SDKs that post to
> the HTTP path directly.

### Phase 4 — Fail-closed tenant resolution & remove auto-activation

**Change:** Behind `GpsIngest:FailClosedTenant`, once a request carries an authenticated identity
(per-gateway and/or per-device): require **authenticated gateway ⇒ device ∈ that gateway's
authorized set ⇒ device's `company_id`**; no match ⇒ 401/403, never a write. Remove the
ingest-time `provisioning/pending → Active` auto-activation (threat T4) — activation becomes an
explicit provisioning action. Where feasible, run the landing write under the tenant RLS context
instead of `RunInSystemScopeAsync`, or assert `company_id` matches the authenticated tenant before
the system-scope write (defence-in-depth for T1). Uniform auth-stage error responses so IMEI
validity/device state don't leak before the credential proves out (threat I4).

**Backward-compat:** Applies **only** to requests that already present an authenticated identity
(i.e. devices/gateways past Phases 2–3). Legacy-shared requests are unaffected until Phase 6.
Shadow the tenant-resolution and authorization decisions first; enforce per authenticated entity.

**Rollback:** Flip `GpsIngest:FailClosedTenant` off → prior tenant-resolution + auto-activation
behavior returns.

### Phase 5 — mTLS at the edge (transport gateway auth)

**Change:** Behind `GpsIngest:RequireMtls`, require a client cert at the nginx edge for gps-ingest;
nginx passes the verified `X-Client-Cert-Fingerprint` (not client-settable) → app maps it to a
`telemetry_gateways` row and cross-checks gateway→device authorization. mTLS authenticates the
*connection*; the `kid` signature authenticates each *message* (survives TLS termination, gives
non-repudiation).

**Backward-compat:** Enforced per gateway once that gateway is confirmed presenting a valid cert
(shadow the fingerprint match first). Others continue on Phase 2 signatures.

**Rollback:** Flip `GpsIngest:RequireMtls` off; edge stops requiring the client cert.

### Phase 6 — Retire the shared secret (fail-closed cutover)

**Precondition (hard gate):** dashboards show **100% of gateways on per-gateway keys** and
**100% of active devices** either signing or covered by an authorized mTLS gateway, sustained for
the agreed soak window.

**Change:** Set `telemetry_gateways` row `legacy-shared` → `Revoked`. Reject any gps-ingest
request lacking a per-gateway identity. Remove `Telemetry:GatewaySecret` from config/secret store.
Add a gateway CHECK-constraint invariant mirroring Stage-27's device-credential one (an `Active`
gateway must have a valid non-dev `signing_key_hash` or `cert_fingerprint`).

**Backward-compat:** By construction, nothing correctly-configured is left on the shared secret at
this point — that is what the precondition guarantees.

**Rollback (time-boxed):** Re-enable the `legacy-shared` row + `Telemetry:GatewaySecret` for a
documented window if an unforeseen laggard appears; delete the secret for good only after the
window passes clean.

---

## 5. Promotion gates (do not advance without these)

| Advance to | Requires |
|---|---|
| Phase 1 enforce | Shadow shows durable check rejects only true cross-instance replays; nonce prune job healthy. |
| Phase 2 enforce (per gateway) | That gateway issued + adopted its key; its error rate flat over soak; `X-Gateway-Id` traffic ≈100% for it. |
| Phase 3 flag (per device) | Device's shadow signature check passes reliably; firmware confirmed. |
| Phase 4 enforce | Authorization + tenant-resolution shadow shows zero false rejects on authenticated traffic. |
| Phase 5 enforce (per gateway) | Valid cert fingerprint observed for that gateway in shadow. |
| Phase 6 | 100% adoption of Phases 2/3/5 for the whole active fleet, sustained. |

## 6. Global kill switch & monitoring

- A single `GpsIngest:*` flag set to legacy behavior reverts each phase independently; there is
  no phase whose rollback depends on a schema drop.
- **Alarms:** spike in gps-ingest 401/403; drop in accepted-fix rate per gateway/tenant; any
  device that *was* signing and stops; nonce-table growth anomalies; cert-expiry approaching.
- **Data-loss guard:** because the buffer of trust is on the *server*, a forwarder that briefly
  misconfigures during migration should be caught by the shadow metric **before** enforcement, not
  by lost fixes.

## 7. Risks specific to the cutover

| Risk | Mitigation |
|---|---|
| A quiet, rarely-reporting device is flipped to `require_device_signature` before it next connects and then fails | Flip only from an *observed* signing history; keep a per-device grace flag; alarm on first post-flip reject. |
| mTLS at nginx accidentally rejects a whole gateway of devices | Shadow the fingerprint match first; enforce per gateway, never fleet-wide. |
| Durable nonce table becomes a write hotspot at fleet scale | Index `(gateway_id, nonce)`; timed prune; bounded LRU fast-path; consider Redis if Postgres write load is a concern. |
| Removing auto-activation strands half-provisioned devices | Ship the explicit activation path (authenticated provisioning action) **before** Phase 4 removes the ingest-time promotion. |
| Shared secret lingers in an old config/env after Phase 6 | Secret-scan in CI; explicit deletion checklist item + the gateway CHECK-constraint invariant. |

---

**Bottom line:** the order is **durable replay first (Phase 1, pure safety win)**, then
per-gateway keys, then per-device proof, then fail-closed tenant + no auto-activation, then mTLS,
then retire the shared secret last — each phase additive, flag-gated, shadow-validated, and
independently reversible, so the live fleet never loses a fix it should have kept.
