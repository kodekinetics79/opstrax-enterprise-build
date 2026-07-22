# OpsTrax Telematics Ingest — STRIDE Threat Model

**Scope:** the two device/gateway ingest paths and everything reachable from an
attacker who controls a device, a gateway, a SIM, the network, or a dependency.
**Status:** formal model of the *current* codebase (not aspirational).
**Owner:** AppSec / DevSecOps.
**Last grounded against:** `backend-dotnet/Controllers/EndpointMappings.cs`
(`GpsTrackerIngest` @ L10823, `TelemetryIngest` @ L10268),
`backend-dotnet/TelemetryHmacHelper.cs`,
`backend-dotnet/Services/TelemetrySchemaService.cs` (telemetry_nonces @ L113),
`backend-dotnet/Services/TelemetryBackgroundService.cs` (nonce prune @ L143),
`database/migrations/2026_07_05_stage27_iot_credential_quarantine.sql`,
`database/migrations/2026_07_11_stage32_device_imei.sql`,
and the Increment-2 standalone fabric `telematics/` (`Gt06Adapter`,
`TcpGatewayService`, `GatewayConnection`, `InMemoryDeviceRegistry`, the
`Identity/DeviceAuthMode` + `Lifecycle/DeviceLifecycleState` contracts) and
`database/migrations/telematics/001..003`.

---

## 0. Increment-2 status — what changed, and what did NOT

Increment-2 built a **new, standalone ingestion fabric** under `telematics/`: a fully
implemented GT06 decoder and a working TCP gateway (`TcpGatewayService` /
`GatewayConnection`) covered by 38 tests. **This fabric is a parallel build; it is NOT
yet the production ingest path.** The live production path is still the `Opstrax.Api`
`GpsTrackerIngest` HTTP endpoint with the single shared `Telemetry:GatewaySecret`
(§1.2), and that endpoint is **unchanged** by Increment-2. The staged plan to migrate
the live endpoint off the shared secret is `legacy-cutover-plan.md`.

Against that reality, the resolutions below are classified honestly:

### 0.1 Runtime-enforced now (in the `telematics/` fabric)

| Resolves | What the fabric actually does | Evidence |
|---|---|---|
| S2 (partial), T1 (partial) | **Fail-closed identity resolution.** Ownership (tenant/company/device/vehicle) comes only from `IDeviceRegistry`; the IMEI is an **index**, never an authenticator. An unknown/unprovisioned IMEI resolves to `null` → rejected, **never bound to a tenant** (rejection envelope tenant `Guid.Empty`). | `InMemoryDeviceRegistry.ResolveAsync`, `GatewayConnection.HandleLoginAsync`; socket test `Login_from_an_unknown_imei_is_rejected_and_is_never_bound_to_a_tenant` |
| T4 (partial) | **No ingest-time state promotion.** The gateway binds an owner and acks; it does not flip device state. (Contrast the live `GpsTrackerIngest`, which auto-activates provisioning/pending devices.) | `GatewayConnection` (no state write) |
| E1/D3 (partial) | **Fail-closed framing.** Malformed/oversize/garbage streams drop the connection only; a 24-connection hostile flood cannot down the listener; bounded reassembly buffer, connection quota, idle-timeout reaping, backpressure. No event is fabricated from corrupt bytes. | `GatewayConnection.ReadLoopAsync`; socket test `A_malformed_flood_never_takes_the_gateway_down_...` |
| I-lite | **IMEI redaction in logs.** The gateway logs only a masked IMEI (`86***********76`), on the rejection lane too. | `DeviceIdentifier.Mask`; asserted in the unknown-IMEI test |

### 0.2 Contract / schema landed, but NOT yet runtime-wired

- **Per-device trust *policy* (contract).** `DeviceAuthMode` / `DeviceTrustTier` encode the
  trust tiers **honestly**: `ImeiAllowlistOnly` is explicitly labelled *spoofable, no
  cryptographic proof*, and `ProvidesCryptographicDeviceAuth()` returns `false` for it. This is
  the type-level statement of the residual risk — but **no code branches on it yet** and no
  device presents a per-device credential to the fabric.
- **Trust score / quality flags (fields only).** `CanonicalTelemetryEvent.TrustScore` and
  `QualityFlags` (incl. `IsReplay`) exist and `latest_vehicle_positions` / `canonical_telemetry_events`
  gained a `trust_score` column (`telematics 001`/`003`) — but **nothing computes a score or
  raises a flag**; `TrustScore` defaults to `1.0`.
- **Quarantine (state + audit, not enforcement).** `DeviceLifecycleState.Quarantined` exists, and
  `telematics 002` adds a 16-state `device_state` CHECK + an append-only, RLS-scoped
  `device_state_transitions` audit table. **But the gateway does not enforce it:**
  `GatewayConnection.IsIngestAllowed` bars only `Suspended`/`Retired` — a `Quarantined` device is
  currently still admitted, and nothing auto-quarantines on anomaly. This is a **known gap**, not a
  resolution.

### 0.3 Still design-only (no implementation anywhere)

- **Durable / shared replay & sequence defense.** The fabric has **no** replay or sequence
  detection at all — not in-memory, not durable. The decoder faithfully reports a reused serial and
  an out-of-order device time so a *future* stage can act, but that stage does not exist. On the
  live HTTP path, replay defense remains the in-memory, per-process `GpsGatewayReplayCache` (D2).
  The durable `telemetry_gateway_nonces` + monotonic-sequence design lives in
  `identity-trust-architecture.md §5` and is unbuilt.
- **Any cryptographic device authentication on the GT06 path.** See §0.4.

### 0.4 Residual risk — raw GT06 spoofability (state plainly)

Raw GT06/Concox trackers carry **no cryptographic device authentication at the protocol level**.
The IMEI in the login frame is the only device identifier, it is printed on the hardware and in SIM
records, and it is trivially spoofable. Fail-closed registry resolution (§0.1) ensures a forged
IMEI can only be attributed to a device that was **already provisioned** with that exact IMEI — it
stops *invented* tenants — but it does **not** stop an attacker who knows a valid provisioned IMEI
from impersonating that device over a raw-TCP session. For this device class the trust model is,
and can only be, **defence-in-depth**: explicit provisioning + IMEI allowlist + optional
source-IP / SIM pinning + durable replay/sequence defense + behavioural trust-scoring + quarantine
on anomaly. **Residual risk is reduced, never eliminated.** Cryptographic device auth
(`PerDeviceHmac`, `MutualTls`, `ClientCertificate`) applies only where the transport can carry it —
a per-device-HMAC HTTP ingest, a mobile SDK, or a **signing gateway** that itself holds a scoped
mTLS credential and vouches for the raw devices behind it. It cannot be retrofitted onto the raw
GT06 wire protocol itself.

---

## 1. System context and trust boundaries

Two ingest endpoints exist, with **materially different** trust models. The
security posture of the platform is dominated by the *weaker* of the two.

### 1.1 `/api/telemetry/ingest` — `TelemetryIngest` (STRONG path)

| Property | Implementation | Line |
|---|---|---|
| Device identity | `X-Device-Key` (raw) → `sha256` → `eld_devices.api_key_hash` lookup | L10338–10343 |
| Per-device secret | `eld_devices.hmac_secret` (unique per device, ≥32 chars) | L10360 |
| Signature | HMAC-SHA256 over canonical `METHOD\npath\nX-Timestamp\nX-Nonce\nsha256hex(body)`, constant-time compare | L10371–10382, `TelemetryHmacHelper` |
| Freshness | `X-Timestamp` within **±60 s** | L10322–10328 |
| Replay defense | **Durable** — atomic `INSERT INTO telemetry_nonces(device_id,nonce)`, `UNIQUE(device_id,nonce)`, 23505 → reject | L10384–10396 |
| Device state gate | only `status = 'Active'` accepted | L10353–10357 |
| Tenant binding | `company_id/vehicle_id/driver_id` bound from the device record; body cannot override | L10420–10423 |

### 1.2 `/api/telemetry/gps-ingest` — `GpsTrackerIngest` (WEAK path)

| Property | Implementation | Line |
|---|---|---|
| Gateway identity | **single global** `Telemetry:GatewaySecret` (config), ≥32 chars else 503 | L10825, L10828 |
| Per-device secret | **none** — device is identified by **IMEI only** (`X-Device-IMEI` header or body `imei/deviceId/id`) | L10868 |
| Signature | HMAC-SHA256 over `{timestamp}.{rawPayload}`, one key for the whole fleet | L10836–10842 |
| Freshness | `X-Gateway-Timestamp` within **±300 s** | L10830–10832 |
| Replay defense | **In-memory** `ConcurrentDictionary` `GpsGatewayReplayCache`, key `{timestamp}:{signature}`, 300 s TTL pruned inline | L33, L10844–10847 |
| Device state gate | accepts `active` **and** `provisioning` **and** `pending`; a valid signed fix **auto-activates** provisioning/pending devices | L10888, L10977–10982 |
| Tenant binding | `company_id/vehicle_id` bound from the device record (good), but the **record is selected by attacker-supplied IMEI** | L10879–10884 |
| DB write | `RunInSystemScopeAsync` → **bypasses RLS** | L10914 |

**The central finding:** on the gps-ingest path a *single* secret authenticates
the *entire multi-tenant fleet*, device identity is a *non-secret bearer
identifier* (IMEI), replay protection is *process-local and non-durable*, and a
signed request *promotes* a device to Active. Every threat below inherits from
this.

---

## 2. STRIDE threat register

Each row: **STRIDE category · attack · current exposure in THIS codebase ·
mitigation · residual risk**. Rows are ordered by current severity.

### 2.1 Spoofing

| # | Attack | Current exposure in this codebase | Mitigation | Residual risk |
|---|---|---|---|---|
| S1 | **Shared-secret blast radius** — one leaked `Telemetry:GatewaySecret` authenticates *every* tenant's ingest on gps-ingest. | HIGH. `GpsTrackerIngest` L10825/10836 uses one config secret for the whole fleet. Leak via env dump, log, Render dashboard, or gateway compromise = full-fleet forgery. No per-gateway scoping, no key id, no rotation without global outage. | Move to per-gateway credentials + mTLS (see identity-trust doc §3–4). Add `kid` to the signature envelope so keys are individually revocable. Stage the migration so both secrets validate during cutover. | MED until per-gateway keys ship; MED-LOW after (residual: a compromised gateway still forges for *its* assigned devices). |
| S2 | **IMEI spoofing / device impersonation** — attacker submits any IMEI they know or guess and the fix is attributed to that vehicle/tenant. | HIGH. IMEI is not a secret; it is printed on hardware, in SIM records, and in prior payloads. `GpsTrackerIngest` L10868/L10879–10884 resolves the device solely by attacker-controlled IMEI, then binds tenant from that record. With the shared secret, an attacker writes location for *any* registered IMEI. | Require a **per-device** proof on gps-ingest too (per-device HMAC like `TelemetryIngest`, or device client cert). Until then, treat IMEI as an *index*, never as *authentication*; add per-device trust score (identity-trust §7). | MED-HIGH now. LOW once per-device proof is mandatory. |
| S3 | **GPS spoofing** — RF-level GPS simulation feeds a genuine device false coordinates; the signed envelope is valid. | MED. Neither endpoint can distinguish a spoofed fix from a real one — only bounds checks exist (`IsCoordinateValid`, speed/heading/fuel/odo bounds L10874/10903, TelemetryIngest L10406–10418). A real device with real credentials reporting a teleport is accepted. | Server-side plausibility: max-speed-between-fixes / geo-jump detection, HDOP/satellite-count fields, dead-reckoning cross-check, cell-tower vs GPS divergence → feed trust score, quarantine on repeated anomalies. | MED — RF spoofing is not fully solvable server-side; residual reduced to detection latency. |
| S4 | **Firmware / device-model impersonation** — attacker claims a trusted firmware or provider to dodge stricter policy. | LOW-MED. `firmware_version`/`provider` are self-declared strings stored unauthenticated (`eld_devices`, provision L11255). No signature over firmware identity; policy does not yet key off firmware, so limited leverage today, but any future firmware-based trust decision would be forgeable. | Bind firmware/provider to the credential at provisioning; never let ingest self-assert firmware for a trust decision. Signed firmware attestation for command-eligible devices. | LOW. |
| S5 | **SIM swapping / re-carriering** — attacker moves the SIM/IMEI to attacker hardware and continues reporting. | MED on gps-ingest (IMEI-only auth means the new hardware is indistinguishable). LOW on `/api/telemetry/ingest` (needs the per-device key + hmac_secret, which the SIM does not carry). | Bind device identity to the *secret*, not the SIM/IMEI. Alert on IMSI/carrier/IP-ASN change for a device; require re-provision on hardware change. | LOW-MED. |

### 2.2 Tampering

| # | Attack | Current exposure in this codebase | Mitigation | Residual risk |
|---|---|---|---|---|
| T1 | **Cross-tenant injection** — write location/alerts into another tenant's data. | MED-HIGH on gps-ingest. Tenant is bound from the device record (good, L10892) but the record is chosen by attacker IMEI (S2) and the insert runs under `RunInSystemScopeAsync` which **bypasses RLS** (L10914). So a forged-but-signed request lands cross-tenant with no RLS backstop. `TelemetryIngest` binds tenant from an *authenticated* device, so it is LOW there. | Per-device auth on gps-ingest (removes the arbitrary-IMEI primitive). Keep RLS as defence-in-depth even inside system scope where feasible, or assert `company_id` equals the authenticated device's tenant before the system-scope write. | LOW once S2 is closed. |
| T2 | **Event tampering** — modify lat/lng/speed/odometer in transit. | LOW. Both paths HMAC the body (gps: `{ts}.{rawPayload}` L10837; telemetry: `sha256hex(body)` in canonical string). A body edit breaks the signature. | Maintained by HMAC coverage of the full raw body. Keep signing the *raw* bytes (both paths buffer raw body — TelemetryIngest L10270–10275). | LOW. |
| T3 | **Odometer / fuel / engine-state falsification by a genuine device** — legitimate credentials, lying sensor values (e.g. odometer rollback for billing/maintenance fraud). | MED. Only static bounds are enforced (L10903; TelemetryIngest speed/coord L10406–10418). No monotonicity or rate-of-change checks; odometer can jump or regress within bounds. | Monotonic odometer enforcement, per-vehicle rate-of-change limits, reconcile against maintenance/fuel records; anomalies → trust score + alert. | MED — insider/genuine-device fraud is a detection problem. |
| T4 | **Provisioning-state escalation via ingest** — a signed fix flips a device from `provisioning/pending` to `Active`. | MED. `GpsTrackerIngest` L10977–10982 sets `status='Active'` on any accepted fix. Combined with S1+S2, an attacker with the shared secret can *activate* a half-provisioned device by IMEI. | Remove auto-activation from the ingest path; activation should be an explicit, authenticated provisioning action. At minimum require per-device proof before promotion. | LOW once per-device auth ships. |

### 2.3 Repudiation

| # | Attack | Current exposure in this codebase | Mitigation | Residual risk |
|---|---|---|---|---|
| R1 | **Device denies sending / operator denies acting** — dispute over who wrote a fix or rotated a credential. | LOW-MED. Management actions are audited (`audit.LogAsync` for provision/rotate/revoke/suspend/assign, L11266/11301/11321…). But **ingest events on gps-ingest are not attributable to a specific credential** (shared secret has no identity), and raw signatures are not retained for non-repudiation. | Log `kid`/gateway id + device id on every accepted ingest; retain signature + timestamp for a defined window; sign audit log. | LOW-MED — improves once per-gateway/per-device identity exists. |

### 2.4 Information Disclosure

| # | Attack | Current exposure in this codebase | Mitigation | Residual risk |
|---|---|---|---|---|
| I1 | **Credential theft** — steal `Telemetry:GatewaySecret`, a device `hmac_secret`, or an `api_key`. | HIGH for the gateway secret (single value, broadest blast radius, lives in config/env — grep found no committed value, but Render env and process memory hold it). MED for per-device secrets. Note `hmac_secret` is stored **plaintext** in `eld_devices` (L10360, provision L11258) — a DB read discloses every device's signing key. | Store the gateway secret in a KMS/secret manager, not plain env; move per-device `hmac_secret` to an HSM/KMS-wrapped or hashed-derivation scheme; prefer asymmetric device certs so the server never holds a signing secret. Device-list endpoint already excludes secrets (L11182). | MED — plaintext `hmac_secret` at rest is the key residual. |
| I2 | **Raw-packet / payload leakage** — sensitive raw ingest payloads exposed via logs or error responses. | LOW-MED. Handlers do not appear to log raw bodies, and errors return generic messages. Risk is in future debug logging of `rawBody`/`rawPayload` (buffered at L10272/10834). | Explicit "never log raw ingest body" rule + redaction middleware; cap and scrub `notes`/free-text; structured logging allowlist. | LOW. |
| I3 | **Media exposure** — dashcam video/snapshots accessible cross-tenant or via guessable URLs. | MED. `/api/dashcam/*` endpoints exist (L1086–1096) and proof/media references are stored (L456). Media authorization and URL unguessability were not verified in this pass; historically media object URLs are a common IDOR/unsigned-URL leak. | Signed, expiring, tenant-scoped media URLs; authorize every media fetch by `company_id`; no public buckets. **Follow-up: audit `DashcamEventDetail` + proof media for tenant scoping.** | MED — unverified, flagged. |
| I4 | **Device/tenant enumeration** — distinguish "IMEI known vs unknown" or "device suspended vs active" from responses. | LOW-MED. gps-ingest returns `404 Device not found` (L10886) vs `403 not enabled` (L10889) vs `200`, leaking IMEI validity and device state to anyone past the shared-secret gate. | Uniform response/timing for auth-stage failures; only differentiate *after* per-device proof. | LOW. |

### 2.5 Denial of Service

| # | Attack | Current exposure in this codebase | Mitigation | Residual risk |
|---|---|---|---|---|
| D1 | **Connection / ingest flood** — high-volume signed or unsigned requests exhaust DB/connections. | MED. No per-device or per-gateway rate limiting is visible in either handler. Each accepted gps-ingest does 2–3 DB writes under system scope (L10916–10982); each telemetry-ingest does a nonce INSERT + event insert + upsert. Unsigned floods are cheap-rejected but still consume connection/parse budget. | Per-credential rate limits + token buckets at the edge (nginx/WAF), connection caps, shed-load on the ingest pool, separate pool for ingest vs API. | MED. |
| D2 | **Nonce-table growth / replay-store exhaustion** — flood distinct nonces to bloat `telemetry_nonces`, or exploit the in-memory gps cache. | LOW-MED. `telemetry_nonces` grows one row per accepted request; pruned only >24 h (`TelemetryBackgroundService` L143). The gps `GpsGatewayReplayCache` is unbounded except by 300 s TTL and only pruned *on the next request* (L10846) — a burst can balloon process memory; and it is **per-process**, so it is not a shared limiter. | Bound the in-memory cache size (LRU) or move to a durable/shared store (Redis/DB) as done for telemetry_nonces; add nonce-rate quotas per device; ensure prune runs on a timer, not only inline. | LOW-MED. |
| D3 | **Malformed-payload CPU/alloc abuse** — oversized or deeply-nested JSON. | LOW. gps-ingest caps raw payload at 32,768 bytes (L10835 → 413) and nonce length at 128 (TelemetryIngest L10331). JSON is parsed with default depth limits. | Keep the size cap; add explicit `MaxDepth`/element-count limits on the JSON reader; request-body size limit at the server. | LOW. |

### 2.6 Elevation of Privilege

| # | Attack | Current exposure in this codebase | Mitigation | Residual risk |
|---|---|---|---|---|
| E1 | **Malformed binary packet / parser exploitation** — crafted input triggers a parser bug (buffer/int overflow, injection through the parser). | LOW-MED today because ingest is JSON via `System.Text.Json` (memory-safe) with parameterized SQL (`AddWithValue` everywhere, no string-concatenated device input). Risk rises sharply **if a raw binary/proprietary tracker protocol decoder is added** (common for real GPS trackers). | For any future binary decoder: fuzz it, run it sandboxed/out-of-process, bounds-check every field, never trust length prefixes; keep SQL parameterized. | LOW now; re-assess on binary-protocol support. |
| E2 | **Command abuse** — attacker sends control commands to a device/vehicle (immobilize, unlock, reboot, config change). | N/A→WATCH. No device-command / remote-actuation endpoint exists in the ingest surface today (only inbound telemetry). Dashcam endpoints are data CRUD, not device control. This is the highest-consequence *future* threat class. | Before any command channel ships: mutual auth (mTLS), per-command signing, RBAC + break-glass approval, per-device authorization, full audit, rate limits, and a hard tenant check. Treat command issuance as a separate, higher-assurance credential than telemetry. | N/A now; design-time gate required. |
| E3 | **Gateway compromise** — attacker owns a trusted gateway host. | HIGH consequence. With the current single shared secret, a compromised gateway can forge for the **entire fleet** (it holds the fleet key). It also runs under `trusted-gateway` source channel (L10922/10948) which downstream logic treats as high-trust. | Per-gateway credentials + mTLS client certs scoped to the gateway's assigned devices; short-lived certs; gateway attestation; a compromised gateway is then limited to its own devices and is individually revocable. | MED — reduced from "fleet" to "gateway's devices" after per-gateway keys. |
| E4 | **Insider abuse** — an operator with `telemetry.devices.manage` rotates/reassigns/revokes devices maliciously, or reads secrets. | MED. Management endpoints gate on `RequirePermission(http, "telemetry.devices.manage")` (L11228/11281/11314…) and audit, but a single role can provision, rotate (learn new secret, L11305), reassign across vehicles, and activate. Plaintext `hmac_secret` at rest (I1) widens DB-admin insider reach. | Separation of duties (provision vs rotate vs revoke), dual-control for bulk rotation, secret access logged + alerted, encrypt `hmac_secret` at rest, scope DB roles. | MED. |
| E5 | **Supply-chain / dependency compromise** — malicious or vulnerable NuGet/npm dependency in the ingest path (Npgsql, System.Text.Json, JWT, etc.). | MED (industry-baseline). Ingest depends on Npgsql and the .NET BCL crypto; the broader repo has npm frontend + Node side-service. No SBOM/pinning/scanning verified in this pass. | Lockfile pinning, SBOM generation, `dotnet list package --vulnerable` + `npm audit` in CI, Dependabot/renovate, verify package signatures, minimal ingest dependency surface. | MED. |

---

## 3. Cross-cutting residual-risk summary

| Theme | Current worst case | After identity-trust migration (see companion doc) |
|---|---|---|
| Authentication of gps-ingest | One secret = whole-fleet forgery by IMEI | Per-gateway + per-device proof; forgery limited to a compromised device |
| Replay (gps-ingest) | Process-local, non-durable, multi-instance replay window reopens on restart/scale-out | Durable + shared nonce/sequence store, same guarantee as `telemetry_nonces` |
| Tenant isolation on ingest | RLS bypassed in system scope; tenant chosen by attacker IMEI | Tenant derived from authenticated credential; fail-closed resolution |
| Credential secrecy | `hmac_secret` plaintext at rest; single shared gateway secret in env | KMS-wrapped / asymmetric device certs; per-gateway keys in secret manager |
| Detection of spoofed-but-signed fixes | None (bounds only) | Trust-score + plausibility checks + quarantine |
| Command channel | Does not exist (good) | Must not ship without mutual auth + per-command signing |

**Increment-2 movement (see §0):** the *new* `telematics/` fabric already closes the
"invented tenant" half of S2/T1 with fail-closed registry resolution, and lands the
contracts/schema for trust-scoring and quarantine. It does **not** yet add cryptographic
device auth, durable/shared replay defense, or quarantine *enforcement*, and it is not yet
the production ingest path — so the "current worst case" column above still describes the
live `GpsTrackerIngest`. The middle-state migration is `legacy-cutover-plan.md`.

---

## 4. Security non-negotiables → coverage map

Every non-negotiable from the mandate maps to at least one register row.

| Non-negotiable | Rows |
|---|---|
| GPS spoofing | S3 |
| IMEI spoofing | S2 |
| Packet replay | §1.1 vs §1.2 replay rows, D2 |
| Credential theft | I1 |
| Shared-secret blast radius | S1, E3 |
| Malformed binary packets | D3, E1 |
| Parser exploitation | E1 |
| DoS / connection exhaustion | D1, D2, D3 |
| Cross-tenant injection | T1 |
| Command abuse | E2 |
| Firmware impersonation | S4 |
| SIM swapping | S5 |
| Gateway compromise | S1, E3 |
| Event tampering | T2, T3, T4 |
| Raw-packet leakage | I2 |
| Media exposure | I3 |
| Insider abuse | E4, R1 |
| Supply-chain / dependency | E5 |

**Highest-priority remediations (in order):** S1/S2 (per-gateway + per-device
auth on gps-ingest), durable+shared replay store for gps-ingest, T1 (fail-closed
tenant resolution), I1 (encrypt `hmac_secret` at rest), I3 (verify media tenant
scoping). These are detailed as a staged migration in
`identity-trust-architecture.md`.
