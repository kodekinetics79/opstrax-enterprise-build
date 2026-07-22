# OpsTrax Telematics — Identity & Trust Architecture

**Purpose:** define the credential and trust architecture that **replaces the
single global shared secret** (`Telemetry:GatewaySecret`) currently used by
`GpsTrackerIngest`, and give a concrete, staged migration that does **not break
existing ingest**.

**Companion:** `threat-model.md` (STRIDE register). This document is the
remediation design for S1, S2, T1, T4, E3, I1 and the replay gap.

**Grounded against:**
`backend-dotnet/Controllers/EndpointMappings.cs` — `GpsTrackerIngest` (L10823),
`TelemetryIngest` (L10268), `DeviceProvision` (L11226), `DeviceRotateSecret`
(L11279), `DeviceRevoke/Suspend/Activate` (L11312–11351);
`backend-dotnet/TelemetryHmacHelper.cs`;
`backend-dotnet/Services/TelemetrySchemaService.cs` (`telemetry_nonces` L113);
`backend-dotnet/Services/TelemetryBackgroundService.cs` (prune L143);
`database/migrations/2026_07_05_stage27_iot_credential_quarantine.sql`;
`database/migrations/2026_07_11_stage32_device_imei.sql`.

---

## 1. Where we are today

### 1.1 The good pattern already exists (`/api/telemetry/ingest`)

`TelemetryIngest` is already the reference model:

- **Per-device credential.** `X-Device-Key` (raw) → `sha256` → lookup on
  `eld_devices.api_key_hash` (L10338–10343). Only the **hash** is used for
  lookup; the raw key is a bearer secret the device holds.
- **Per-device HMAC secret.** `eld_devices.hmac_secret`, unique per device,
  ≥32 chars, signs a canonical string (`TelemetryHmacHelper`).
- **Durable replay defense.** `telemetry_nonces (device_id, nonce)` with
  `UNIQUE(device_id, nonce)`; the handler does an *atomic* `INSERT` and treats
  Postgres `23505` as a replay (L10384–10396). Survives restarts and is shared
  across all app instances because it lives in Postgres. Pruned >24 h.
- **Strict state gate.** Only `status='Active'` is accepted (L10353).
- **Fail-closed credentials.** Stage-27 migration quarantines dev/malformed
  secrets and enforces `ck_eld_devices_active_credentials` — an Active device
  *must* have a valid non-dev `api_key_hash` + `hmac_secret`.
- **Tenant bound from the authenticated record**, never the body (L10420–10423).

### 1.2 The weak pattern we must replace (`/api/telemetry/gps-ingest`)

| Weakness | Where | Consequence |
|---|---|---|
| **Single global secret** authenticates the whole fleet | `config["Telemetry:GatewaySecret"]` L10825, HMAC L10836 | One leak → forge for every tenant (threat S1/E3) |
| **Device identity = IMEI**, a non-secret bearer id | L10868, resolve L10879–10884 | Any known IMEI is impersonable (S2) |
| **In-memory, per-process, non-durable replay cache** | `GpsGatewayReplayCache` L33, L10844–10847 | Restart/scale-out reopens the replay window (D2) |
| **Auto-activation on signed fix** | L10977–10982 | Ingest promotes device state (T4) |
| **RLS bypass** on write | `RunInSystemScopeAsync` L10914 | No tenant backstop if IMEI is forged (T1) |

The target is to give gps-ingest the *same* assurance as telemetry-ingest while
keeping real trackers (which cannot always do per-request per-device HMAC)
working through a **trusted gateway that itself holds a scoped, revocable,
mutually-authenticated credential** — not a fleet-wide shared secret.

---

## 1a. Increment-2 status against this design

Increment-2 delivered a standalone `telematics/` ingestion fabric. It advances **part** of
this design and, importantly, records the honest limits of the rest. Map it precisely so no
one over-reads it:

| This doc's element | Increment-2 reality | Classification |
|---|---|---|
| §8 Fail-closed tenant resolution | **Done in the fabric.** `InMemoryDeviceRegistry.ResolveAsync` returns the registry owner or `null`; `GatewayConnection` binds tenant/company only from the resolved owner and rejects unknown identities **unbound** (tenant `Guid.Empty`). IMEI is an index, not an authenticator. | Runtime-enforced (in the fabric, not yet on live `GpsTrackerIngest`) |
| §3 Per-device credentials | **Contract only.** `DeviceAuthMode` (`PerDeviceHmac`/`MutualTls`/`ClientCertificate`) + `DeviceTrustTier` exist and honestly mark `ImeiAllowlistOnly` as spoofable. `ResolvedDeviceOwner.CredentialHandle` is an opaque vault pointer that is **never dereferenced or verified** by the fabric. | Design + contract; no verification code |
| §5 Durable/shared replay & sequence | **Not built anywhere.** The fabric has no replay/sequence store; the live path still uses the in-memory `GpsGatewayReplayCache`. The `telemetry_gateway_nonces` table + monotonic sequence are still design. | Design-only |
| §6 Quarantine | **State + audit, not enforcement.** `DeviceLifecycleState.Quarantined` + `telematics 002` (`device_state` 16-state CHECK, RLS-scoped `device_state_transitions` audit) landed. But `GatewayConnection.IsIngestAllowed` bars only `Suspended`/`Retired`; a `Quarantined` device is still admitted and nothing auto-quarantines. | Schema/state landed; enforcement is a gap |
| §7 Anti-spoofing trust score | **Fields only.** `TrustScore`/`QualityFlags` exist on the canonical event and as `trust_score` columns (`telematics 001`/`003`); no scoring engine populates them (`TrustScore` defaults `1.0`). | Design-only |
| §4 Per-gateway keys + mTLS | Unbuilt. The raw GT06 TCP gateway performs **no** transport or message auth. | Design-only |

**Residual risk restated (honesty requirement):** for raw GT06 the strongest achievable
device-identity assurance is `DeviceTrustTier.LowSpoofable`. Fail-closed resolution stops an
attacker from *inventing* a tenant, but an attacker who knows a **valid provisioned IMEI** can
still impersonate that device on a raw-TCP session, because the wire protocol carries no
cryptographic proof. Cryptographic auth (§3/§4) is reachable only by moving the *proof* to a
transport that can carry it — per-device HMAC over HTTP, a mobile SDK, or a signing gateway with
its own mTLS credential — never by trusting the GT06 frame itself. Everything below is the target
these tiers migrate toward; none of it retro-secures the raw GT06 wire.

---

## 2. Target trust model (layered)

Three independent identities, each individually revocable, each fail-closed:

```
                 ┌─────────────────────────────────────────────┐
   Device  ──────┤ 1. Device identity (per-device)             │
   (tracker)     │    - api_key_hash + hmac_secret  (today)    │
                 │    - OR X.509 client cert (target)          │
                 └───────────────────┬─────────────────────────┘
                                     │ carried inside the gateway session
                 ┌───────────────────▼─────────────────────────┐
   Gateway ──────┤ 2. Gateway identity (per-gateway)           │
   (aggregator)  │    - mTLS client cert (target)              │
                 │    - per-gateway signing key + `kid`        │
                 │    - scoped to its assigned device set      │
                 └───────────────────┬─────────────────────────┘
                                     │ TLS
                 ┌───────────────────▼─────────────────────────┐
   Platform ─────┤ 3. Tenant resolution (fail-closed)          │
   (ingest)      │    - company_id derived ONLY from the       │
                 │      authenticated credential, never body   │
                 └─────────────────────────────────────────────┘
```

Authentication decision on gps-ingest becomes: **gateway proves itself (mTLS +
`kid` signature)** AND **the device is one this gateway is authorized to report
for** AND **a per-device proof or trusted-gateway attestation is present** AND
**the (gateway,device,sequence/nonce) has not been seen**. Tenant is then the
device's `company_id`, enforced fail-closed.

---

## 3. Per-device credentials

**Keep and extend the existing model.** No new table needed for the baseline;
`eld_devices` already carries `api_key_hash`, `hmac_secret`, `imei`,
`device_serial`, `status`, `revoked_at`, `updated_at`, `deleted_at`.

### 3.1 Baseline (available now, no cert infra)

- Every device — including trackers reporting via a gateway — gets a
  `hmac_secret` at provisioning (`DeviceProvision` L11234–11235 already mints
  32 random bytes each for `api_key` and `hmac_secret`).
- gps-ingest gains an **optional-then-mandatory** per-device signature header
  (`X-Device-Signature`) computed with the device's `hmac_secret` over the same
  canonical body, verified exactly like `TelemetryIngest` (reuse
  `TelemetryHmacHelper.ComputeSignature` / `ConstantTimeEquals`).
- The IMEI stays as the **index** to find the device row, but is no longer the
  **authenticator**.

### 3.2 Target (asymmetric, server holds no signing secret)

- Provision issues an **X.509 client certificate** per device (or per device a
  key pair; server stores only the public key / cert fingerprint).
- Fixes advantage over HMAC: the platform DB no longer stores a *plaintext
  signing secret* (`hmac_secret` is plaintext today — threat I1). A DB read no
  longer discloses the ability to forge.
- Store `device_cert_fingerprint`, `device_cert_not_after`, `public_key_pem`.

### 3.3 Secret-at-rest hardening (do regardless of 3.2)

- Encrypt `hmac_secret` at rest (KMS-wrapped column or app-layer envelope
  encryption) so DB-admin insiders (threat E4) cannot read fleet signing keys.
- Never return secrets except once at provision/rotate (already the behavior,
  L11267–11274 / L11302–11308; list endpoint already excludes them, L11182).

---

## 4. Per-gateway credentials + mTLS

**This is the direct replacement for `Telemetry:GatewaySecret`.**

### 4.1 New table: `telemetry_gateways`

```sql
CREATE TABLE IF NOT EXISTS telemetry_gateways (
    id                BIGINT GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
    gateway_code      VARCHAR(120) NOT NULL UNIQUE,     -- stable public id (the `kid`)
    company_id        BIGINT NULL,                      -- NULL = shared/managed gateway
    display_name      VARCHAR(200) NULL,
    signing_key_hash  VARCHAR(64)  NULL,                -- sha256(hex) of the per-gateway HMAC key
    cert_fingerprint  VARCHAR(128) NULL,                -- SHA-256 of the mTLS client cert (DER)
    cert_not_after    TIMESTAMPTZ  NULL,
    status            VARCHAR(40)  NOT NULL DEFAULT 'Active',  -- Active|Suspended|Quarantined|Revoked
    trust_tier        VARCHAR(40)  NOT NULL DEFAULT 'standard',-- standard|attested|probation
    last_seen_at      TIMESTAMPTZ  NULL,
    revoked_at        TIMESTAMPTZ  NULL,
    created_at        TIMESTAMPTZ  NOT NULL DEFAULT NOW(),
    updated_at        TIMESTAMPTZ  NULL,
    deleted_at        TIMESTAMPTZ  NULL
);

-- Which devices a gateway is allowed to report for (authorization scope).
CREATE TABLE IF NOT EXISTS telemetry_gateway_devices (
    gateway_id BIGINT NOT NULL,
    device_id  BIGINT NOT NULL,
    created_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    PRIMARY KEY (gateway_id, device_id)
);
```

### 4.2 Signature envelope with a key id (`kid`)

Replace the fleet-wide `HMAC(gatewaySecret, "{ts}.{payload}")` with:

- Header `X-Gateway-Id: <gateway_code>` → look up the gateway row and its
  per-gateway signing key (`signing_key_hash` gates which key to load; the raw
  key is held by the gateway and stored server-side KMS-wrapped, or replaced
  entirely by the mTLS cert).
- Signature over canonical `{gateway_code}\n{timestamp}\n{sequence}\n{sha256hex(payload)}`.
- Because each gateway has its own key, **revoking one gateway does not affect
  any other** — this is the whole point (threat S1/E3 blast-radius reduction).

### 4.3 mTLS (transport-level gateway auth)

- Terminate mTLS at the edge (nginx already fronts the service per repo
  layout); require a client cert whose `cert_fingerprint` matches an `Active`
  row in `telemetry_gateways`, and pass the verified fingerprint to the app
  (e.g. `X-Client-Cert-Fingerprint` set by nginx, not client-settable).
- The app cross-checks: verified cert fingerprint ⇒ resolves to a specific
  gateway ⇒ that gateway must be authorized for the device's IMEI via
  `telemetry_gateway_devices`.
- mTLS + `kid` signature are complementary: mTLS authenticates the *connection*,
  the signature authenticates each *message* (non-repudiation, and survives TLS
  termination/proxy hops).

### 4.4 Certificate support

- Support an internal CA (or ACM PCA / step-ca) issuing short-lived
  (e.g. ≤90-day) gateway certs and, in the target, device certs.
- Store issuer chain + `not_after`; reject on expiry (fail-closed) and warn
  ahead of expiry so gateways rotate before outage.
- OCSP/CRL or short lifetimes for revocation propagation.

---

## 5. Durable, shared replay & sequence defense

**Contrast (this is the core fix for the gps-ingest replay gap):**

| | gps-ingest **today** | telemetry-ingest **today** | gps-ingest **target** |
|---|---|---|---|
| Store | `GpsGatewayReplayCache` = in-process `ConcurrentDictionary` (L33) | `telemetry_nonces` DB table, `UNIQUE(device_id,nonce)` | durable table (below) |
| Durability | **lost on restart** | survives restart | survives restart |
| Multi-instance | **per-process only** — instance A's cache doesn't stop instance B replay | shared via Postgres | shared via Postgres/Redis |
| Enforcement | `TryAdd` (best-effort, racy across nodes) | atomic `INSERT` → 23505 (L10391) | atomic `INSERT` → 23505 |
| Prune | inline, only on next request (L10846) | timed background job (L143) | timed background job |

### 5.1 Target: `telemetry_gateway_nonces` (+ optional monotonic sequence)

```sql
CREATE TABLE IF NOT EXISTS telemetry_gateway_nonces (
    id         BIGINT GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
    gateway_id BIGINT NOT NULL,
    nonce      VARCHAR(128) NOT NULL,      -- {timestamp}:{signature} today, or a real nonce
    used_at    TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    UNIQUE (gateway_id, nonce)
);
CREATE INDEX IF NOT EXISTS idx_tgn_gateway_used ON telemetry_gateway_nonces(gateway_id, used_at);
```

- Ingest does the **same atomic-INSERT / catch-23505** pattern as
  `TelemetryIngest` L10384–10396 — proven, race-free across instances.
- Add a **monotonic per-device sequence** (`eld_devices.last_sequence` or a
  `device_sequences` row): reject `sequence <= last_seen` and advance on accept.
  Sequence numbers defeat replay *and* reordering without unbounded nonce
  storage, which is ideal for constrained trackers.
- Prune >24 h via the existing `TelemetryBackgroundService` (extend L143), on a
  timer (not inline), and bound any in-memory fast-path cache with an LRU size
  cap so a nonce flood cannot balloon memory (threat D2).

---

## 6. Rotation, quarantine, revocation

### 6.1 Device (mostly exists — keep)

- **Rotation:** `DeviceRotateSecret` L11279 mints new `api_key`+`hmac_secret`,
  old immediately invalid. Extend to cert rotation in the target.
- **Revoke / Suspend / Activate:** L11312–11351 set `status` and `revoked_at`;
  ingest rejects non-Active on the strong path (L10353) and non-enabled on
  gps-ingest (L10888). Keep, and **tighten gps-ingest to reject `provisioning`/
  `pending` for actual location writes once per-device auth exists** (removes
  T4 auto-activation).
- **Quarantine (exists):** Stage-27 `CredentialRotationRequired` status +
  `ck_eld_devices_active_credentials` CHECK constraint. Reuse this exact pattern
  for gateways.

### 6.2 Gateway (new — mirror the device lifecycle)

- **Provision:** `POST /api/telemetry/gateways/provision` (RBAC
  `telemetry.gateways.manage`) → mint per-gateway key / issue cert, return once.
- **Rotate:** `POST /api/telemetry/gateways/{id}/rotate-key` — new key, dual
  overlap window (accept old+new for N hours) so no fleet outage during cutover.
- **Quarantine:** auto-set `status='Quarantined'` on trust-score breach (see §7);
  quarantined gateways are rejected fail-closed.
- **Revoke:** `status='Revoked'`, `revoked_at=NOW()`, cert added to CRL. Blast
  radius is only that gateway's devices — the design goal.
- Enforce a gateway analog of `ck_eld_devices_active_credentials`: an `Active`
  gateway must have a valid non-dev `signing_key_hash` or `cert_fingerprint`.

### 6.3 Rotation ordering rule

Because the migration keeps the shared secret valid during cutover (§8), the
sequencing is: **stand up per-gateway keys → dual-validate → migrate each
gateway → retire the shared secret last.** Never delete the shared secret before
100% of gateways report on their own key.

---

## 7. Anti-spoofing trust score

A per-device (and per-gateway) score, recomputed on ingest, feeding
quarantine/alert decisions. Inputs available or cheap to add:

| Signal | Source in code | Contribution |
|---|---|---|
| Valid per-device signature present | new `X-Device-Signature` (§3.1) | strong positive |
| mTLS + gateway `kid` match | §4 | strong positive |
| Coordinate plausibility | `TelemetryTicketHelper.IsCoordinateValid` (L10874) | reject if fail |
| Speed/heading/fuel/odo in bounds | L10903 | negative if repeated near-limit |
| **Geo-jump / implied speed between fixes** | derive from `latest_vehicle_positions.event_time`+prev lat/lng | negative (GPS spoof S3) |
| **Odometer monotonicity** | compare to `latest_vehicle_positions.odometer_miles` | negative (T3) |
| Event-time skew vs receive-time | L10905–10912 | negative |
| IMSI / carrier / source-IP-ASN change | ingest metadata (add capture) | negative (SIM swap S5) |
| Nonce/sequence anomalies (gaps, replays) | §5 | negative |
| Gateway reporting for unassigned device | `telemetry_gateway_devices` miss | strong negative → reject |

- Score thresholds: `>=T_ok` accept; `T_watch..T_ok` accept + flag +
  `risk_level='medium'`; `<T_watch` quarantine device/gateway and alert.
- Persist score on `latest_vehicle_positions` (already has `risk_level`,
  `telemetry_status` columns, L10946) and on the gateway row (`trust_tier`).

---

## 8. Fail-closed tenant resolution

**Rule:** `company_id` is derived **only** from an authenticated identity, and
any ambiguity or lookup failure **rejects** (never defaults, never uses a body
value).

Current gaps to fix:

1. gps-ingest resolves the device by attacker-controlled IMEI then binds tenant
   from that record (L10879–10892). Once per-device/per-gateway auth exists,
   require: **authenticated gateway ⇒ device in that gateway's authorized set ⇒
   device's `company_id`.** No match ⇒ 401/403, not a write.
2. `RunInSystemScopeAsync` (L10914) bypasses RLS. Keep the system-scope write
   only *after* an explicit assertion that the resolved `company_id` matches the
   authenticated credential's tenant; prefer running the write under the tenant
   RLS context where feasible so RLS is a genuine backstop (defence-in-depth for
   T1).
3. `TelemetryIngest` L10421 falls back to `company_id = 1` when the device
   record has a NULL company. **Change this to reject** — a NULL-tenant device
   must not silently write into tenant 1. Fail closed.
4. Uniform auth-stage error responses (threat I4): do not reveal IMEI validity
   or device state before the credential proof passes.

---

## 9. Staged migration OFF the single shared secret (no ingest breakage)

Each stage is independently deployable and backward-compatible. The shared
secret keeps working until the final stage.

### Stage A — Observability & schema (no behavior change)
- Add `telemetry_gateways`, `telemetry_gateway_devices`,
  `telemetry_gateway_nonces` (§4.1, §5.1). Additive, `IF NOT EXISTS`.
- Register the current implicit gateway as a single row `gateway_code='legacy-shared'`
  whose signing key **is** the existing `Telemetry:GatewaySecret` (hash stored).
- Start logging, on every gps-ingest, the resolved gateway (`legacy-shared`),
  device id, and whether a per-device signature *would* have validated. Emit
  metrics. No requests rejected yet.

### Stage B — Durable/shared replay (drop-in, safe)
- Replace `GpsGatewayReplayCache` usage with the atomic-INSERT pattern against
  `telemetry_gateway_nonces` (reuse the `TelemetryIngest` 23505 approach).
  Keep a small bounded in-memory LRU as a fast-path only.
- Extend `TelemetryBackgroundService` prune (L143) to cover the new table.
- Result: replay defense becomes durable + multi-instance immediately, with no
  change to what gateways must send.

### Stage C — Per-gateway keys alongside the shared secret (dual-validate)
- Accept **either** the legacy fleet HMAC **or** a per-gateway `kid`-scoped
  signature (`X-Gateway-Id` + per-gateway key). Provision real gateway rows and
  keys; onboard gateways one at a time to their own key.
- Metric: % of traffic on per-gateway keys. No breakage — legacy still valid.

### Stage D — Per-device proof on gps-ingest (dual-mode)
- Begin accepting `X-Device-Signature` (per-device `hmac_secret`, §3.1).
- Enforcement is **per-device opt-in**: a device flagged
  `require_device_signature=true` must send it; others still work. Flip devices
  on as fleets update firmware. Populate `telemetry_gateway_devices`
  authorization scope.

### Stage E — mTLS + fail-closed tenant resolution
- Require client certs at the edge for gps-ingest; map `cert_fingerprint` →
  gateway. Enforce gateway→device authorization and the fail-closed tenant rules
  (§8), including removing the `company_id=1` fallback and the
  provisioning/pending auto-activation.

### Stage F — Retire the shared secret (fail-closed cutover)
- When metrics show 100% of gateways on per-gateway keys and 100% of active
  devices sending per-device proof:
  - Set `telemetry_gateways` row `legacy-shared` → `Revoked`.
  - Reject any request lacking a per-gateway identity.
  - Remove `Telemetry:GatewaySecret` from config/secret store.
  - Add a gateway CHECK-constraint invariant mirroring Stage-27's device one.
- Keep a documented, time-boxed rollback (re-enable `legacy-shared`) until the
  cutover is proven, then delete.

**Non-breakage guarantee:** at no stage is an existing, correctly-configured
gateway rejected until (a) an equivalent stronger credential is available to it
and (b) telemetry confirms it has adopted the stronger credential. Enforcement
is always gated behind an observed-adoption metric and a per-entity flag, never
a big-bang flip.

---

## 10. Endpoint / RBAC additions summary

| Endpoint | Permission | Purpose |
|---|---|---|
| `POST /api/telemetry/gateways/provision` | `telemetry.gateways.manage` | Mint per-gateway key / issue cert (return once) |
| `POST /api/telemetry/gateways/{id}/rotate-key` | `telemetry.gateways.manage` | Rotate with overlap window |
| `POST /api/telemetry/gateways/{id}/revoke` | `telemetry.gateways.manage` | Revoke + CRL |
| `POST /api/telemetry/gateways/{id}/quarantine` | system / trust-score job | Auto-quarantine on low trust |
| `POST /api/telemetry/gateways/{id}/devices` | `telemetry.gateways.manage` | Manage gateway→device authorization scope |

Device provisioning/rotation/revocation endpoints already exist
(`/api/devices/*`, L11223–11351) and are the model for the gateway lifecycle.
Enforce separation of duties (provision vs rotate vs revoke) to reduce insider
abuse (threat E4).

---

## 11. Traceability to the threat model

| Threat (STRIDE row) | Addressed by |
|---|---|
| S1 shared-secret blast radius | §4 per-gateway keys + `kid`, §9 Stage C/F |
| S2 IMEI spoofing | §3 per-device proof, §8 gateway→device authorization |
| S3 GPS spoofing | §7 trust score (geo-jump), quarantine |
| S4 firmware impersonation | §3.2 bind firmware to credential |
| S5 SIM swapping | §7 IMSI/carrier/ASN change signal |
| T1 cross-tenant injection | §8 fail-closed tenant resolution, RLS backstop |
| T4 provisioning-state escalation | §6.1 remove auto-activation |
| E3 gateway compromise | §4 mTLS + per-gateway revocation |
| I1 credential theft | §3.3 encrypt `hmac_secret`, §3.2 asymmetric certs |
| Replay gap (gps-ingest) | §5 durable+shared nonce/sequence store |
