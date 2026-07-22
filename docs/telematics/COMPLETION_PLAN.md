# OpsTrax Telematics — Completion Plan (SME + consultant synthesis)

I have deep grounding across all five findings, the migrations, the ADRs (005/006 already drafted), the PT40 fingerprint doc, and the simulator. Here is the synthesized plan.

---

# OpsTrax Telematics — Unified Completion Plan (Lead Architect Synthesis)

Scope: C1, H1, H3 (SME designs + adversarial audits provided), plus C2 and C3 (SME designs were truncated out of the input JSON — I designed these from the code directly and flag them as lead-authored). Every audit finding (ingest D1–D9, device-security risks) is folded in as a correction, not a footnote. Read-only; no files were edited.

Verified anchors: `ITelemetryReplayGuard.Check` is sync (ITelemetryReplayGuard.cs:134), called inline at GatewayConnection.cs:411 on the read-loop thread. Alarm frames route through `PublishTelemetryAsync` (GatewayConnection.cs:377) → `Gt06Adapter.ToCanonicalEvent` (Gt06Adapter.cs:588) which maps only position/speed/heading/ignition — `alarmCode`/`alarmName` (SOS=0x01, Vibration=0x03, Fall=0x13, PowerCut=0x02, Overspeed=0x06; Gt06Adapter.cs:481-501) are dropped. The `InMemoryEventBackbone` (Program.cs:42) has one consumer, the store-and-forward projector (Program.cs:87) — no backend bridge. `PostgresPositionProjectionStore` writes inbox + `latest_vehicle_positions` only, never `canonical_telemetry_events`. Both REST handlers write flat `location_events` + a LVP upsert; strong path's monotonic guard compares `event_time = NOW()` (11588/11594 — a no-op); gps path authenticates the whole fleet with one `Telemetry:GatewaySecret` (11989) and constant `DefaultGatewayId` (12104/12126). `SafetyBackgroundService` only converts `telemetry_alerts` of type `speeding`/`geofence_breach`/`stale_device` (line 90). The safety dashboard sums `event_type='Harsh Braking'`/`'Harsh Acceleration'` (6106) over `safety_events` that nothing ever writes.

---

## A. Audit reconciliation — the must-fix corrections applied before anything ships

These override the SME designs where they conflict. Every one is load-bearing.

1. **Inbox stays globally dedup-keyed, tenant-composite (kills ingest-D1 + D2/D7).** Do **not** RANGE-partition `telemetry_projection_inbox` on `projected_at` — Postgres forbids a `UNIQUE(event_id)` that omits the partition key, it breaks the working `ON CONFLICT (event_id)` at PostgresPositionProjectionStore.cs:121, and an arrival-time partition defeats dedup for late redeliveries (the exact case dedup exists for). Change the key to **`PRIMARY KEY (company_id, event_id)`** (tenant-local, matching RLS scope), keep the table unpartitioned, and prune by batched `DELETE … WHERE projected_at < now()-window` using `idx_projection_inbox_company_projected`. If churn ever demands partitioning, use `HASH(event_id)`, never range-on-arrival.

2. **`event_id` is derived from post-auth resolved identity, tenant-local (kills device-D2/ingest-D2/D7).** gps path: `UuidV5(ns, $"{companyId}:{eldDeviceId}:{canonicalSig}")` — never the constant `gateway_id='default'`. Strong path: `UuidV5(ns, $"{companyId}:{deviceId}:{xNonce}")`. Folding `company_id` into the seed makes a device-reassignment or signature collision fail *within* the tenant, not silently drop a victim tenant's fix behind RLS.

3. **Do not swap `event_time`→device time on the strong path (kills ingest-D2 live-map freeze).** Keep `event_time = NOW()` (arrival) and run the monotonic guard on the dedicated `device_fix_time` column, exactly as the gateway/gps paths already do (PostgresPositionProjectionStore.cs:166-168, gps 12178). Feed `body.EventTime` into `device_fix_time`, not into `event_time`.

4. **The durable watermark is mandatory and never shed; only per-triple dedup rows are sheddable (kills ingest-D3).** Split the two durability concerns. Under journal saturation, drop `telemetry_replay_seen` rows (recoverable by the high-water floor) but the watermark upsert must always land or the replay window reopens under attacker-inducible load.

5. **Watermark is an unwrapped 64-bit monotonic sequence, not `MAX(raw serial)` (kills ingest-D4).** GT06's 16-bit serial wraps at 65536 and resets to 0/1 on power-cycle. Accumulate the wrap epoch in `DeviceState` and persist the widened value; the durable floor's semantics must equal the in-memory circular gate's.

6. **The inbox→canonical→latest transaction is ONE owned artifact (kills ingest-D6 drift).** Implement `telemetry_project_event(...)` as a single Postgres function invoked by **both** the .NET REST writer and the gateway projection store. Its CTE must gate the canonical insert and the LVP upsert on `WHERE EXISTS (SELECT 1 FROM ins_inbox)` (kills ingest-D5 double-count; canonical has no unique constraint by design).

7. **Strong-path nonce reservation moves inside the writer transaction (kills ingest-D6/nonce-burn).** Today the `telemetry_nonces` INSERT (11474) is outside any tx around the writes; a canonical rollback leaves the nonce burned → legitimate retry permanently 409s. Reserve it inside `RunInSystemScopeAsync`, matching the gps path (12119-12130). Drop the SME's false claim that canonical `event_id` adds retry-idempotency here — the nonce gate already rejects the retry upstream.

8. **Repoint readers before dropping `location_events` (kills ingest-D8 break-links).** `source_event_id` threads `location_events.id` into `latest_vehicle_positions` (11610) and `telemetry_alerts.source_event_id` (used by SafetyBackgroundService.cs:87-88,104). Repoint those and the trip-replay/safety readers (EndpointMappings.cs:3297/3385/3422/13596) to `canonical_telemetry_events` as an explicit P2 migration step, then drop the flat write — never as a side effect.

9. **Partition automation is a health-gated dependency with a backward window (kills ingest-D9).** gps path allows fixes up to 30 days stale (12076), so pre-create backward partitions too; alarm on `_default` row count; the partition service ships *with* the migration or canonical inserts silently pool in `_default`.

10. **Warm-load fail-closed + atomic first-sight (device-sec D5).** A failed watermark warm-load must reject-until-loaded (matching the gps path's fail-closed `ProbeError`→503 at 12097), never admit a session against an empty in-memory floor. First-sight load+seed is atomic per `device_id` (`GetOrAdd` an `AsyncLazy`), and after a warm seed the floor is **strict** (`serial <= high_water` rejected) to close the at-mark replay hole.

11. **Two-tier replay is safe only under single-bind invariant (device-sec risk).** The in-memory gate is authoritative because GT06 pins one device to one TCP connection on one instance (GatewayConnection.cs:86-87). The registry/auth must reject a second concurrent bind for an already-bound device; document this alongside the guard.

---

## B. Ordered work list P0 → P1 → P2 (with dependency graph)

**P0 — unblock production (three parallel tracks, each an independently shippable compiling PR):**

- **P0-1 (C1)** Async replay interface. `ITelemetryReplayGuard.CheckAsync` + await at 411 + InMemory/Postgres/tests migration. **Depends on nothing.** Removes thread-pool starvation immediately (worst case becomes a non-blocking awaited round-trip).
- **P0-2 (H3)** Per-gateway identity + tenant-scope reject. Migration 007, `GatewayCredentialService`, `TelemetrySecretProtector`, X-Gateway-Id auth + tenant reject at gps-ingest, real `gateway_id` in replay scope, provisioning/rotate/revoke endpoints, **dual-run** (per-gateway OR legacy shared secret). **Depends on 007 + envelope protector.** Closes the cross-tenant skeleton key's *scoping* the moment a device is onboarded onto a gateway; full closure is P1.
- **P0-3 (H1)** Shared idempotent projection writer. Migration 008 (`event_id` on canonical, inbox key → `(company_id,event_id)`, `telemetry_project_event()` fn, partition fns), `TelemetryCanonicalWriter` used by both REST handlers with deterministic tenant-local `event_id`, nonce-inside-tx, `device_fix_time` monotonic guard. **Depends on 008 + P0-1 is independent of it.**
- **P0-4 (C3)** Alarm bridge. Carry alarm/SOS/crash semantics into `CanonicalTelemetryEvent`; gateway projection + REST writer emit a `telemetry_alert` for alarm-bearing events; widen `SafetyBackgroundService` alert vocabulary. **Depends on P0-3's writer seam** (shares the `telemetry_project_event` path) — sequence C3 immediately after P0-3.
- **P0-5 (C2)** Harsh-event detection + dashboard vocabulary fix. Device-reported harsh/crash events (GT06 alarms + a `harshEvent` field on both ingest paths) → `safety_events`; server-side speed-delta fallback detector; align the safety-dashboard `event_type` tokens to what is actually written. **Depends on P0-4** (reuses the alert→safety_event pipe).

**P1 — durability, hardening, full closure:**

- **P1-1 (C1)** `TwoTierReplayGuard` + `DurableReplayJournalService` + `telemetry_device_watermark` (unwrapped seq, never-shed) + lazy warm-load. Depends on P0-1.
- **P1-2 (H1)** Gateway `PostgresPositionProjectionStore` also appends `canonical_telemetry_events` via the shared `telemetry_project_event()` fn. Depends on P0-3.
- **P1-3 (H1/ops)** `TelemetryPartitionBackgroundService` (ensure forward+backward, prune, `_default` alarm). Must be live before canonical write volume ramps. Depends on 008.
- **P1-4 (H3)** Migrate every forwarder to a gateway credential; envelope-wrap `eld_devices.hmac_secret`; enable `PerDeviceHmac` on capable devices; flip `ConfigValidationService` to fail-on-present shared secret and **delete** the `Telemetry:GatewaySecret` code path. *This is the step that actually closes H3.* Depends on P0-2.
- **P1-5 (C2)** Real accelerometer/g-force canonical path for devices that report it (Teltonika IO, Queclink), harsh-cornering, crash-pulse severity. Depends on P0-5.

**P2 — market-parity north-star & scale ceiling:**

- **P2-1 (H3)** mTLS/ClientCertificate tier at the edge; `VaultSecretProtector`; converge the TCP fabric bridge (C3) onto the same `telemetry_gateways` identity.
- **P2-2 (H1)** Repoint safety/trip readers (3297/3385/3422/13596) to `canonical_telemetry_events`, then drop `location_events` writes (reclaim one write/fix). Inbox HASH-partition if churn demands it. Optional Kafka/Redpanda write-behind for canonical appends past the Postgres synchronous-write ceiling (ADR-002 already scoped).
- **P2-3 (C2)** Behavioural/ML safety scoring, dashcam correlation, trust-score-driven auto-quarantine consuming the `LowSpoofable` tier from H3.

Dependency summary: `007 → P0-2`; `008 → P0-3 → {P0-4 → P0-5}`; `P0-1 → P1-1`; `P0-3 → P1-2`; `P0-2 → P1-4`. P0-1, P0-2, P0-3 are mutually independent and parallelizable.

---

## C. Per-item specs (schema + code file:line + tests)

### P0-1 — C1: async replay guard (no migration)

**Code**
- `ITelemetryReplayGuard.cs:134` — replace `ReplayDecision Check(string, long, string, DateTime)` with `ValueTask<ReplayDecision> CheckAsync(string deviceId, long protocolSerial, string contentHash, DateTime deviceFixTimeUtc, CancellationToken ct = default)`. `ValueTask` so the in-memory hot path allocates nothing when it completes synchronously.
- `GatewayConnection.cs:411` — `decision = await _replayGuard.CheckAsync(owner.DeviceId, serial, contentHash, evt.OccurredAtDeviceUtc, cancellationToken).ConfigureAwait(false);`. Lines 413-455 unchanged (already async).
- `InMemoryReplayGuard.cs` — rename `Check`→`CheckAsync`, keep the lock-based classify-and-record verbatim in a private synchronous `Classify(...)`, `return new ValueTask<ReplayDecision>(Classify(...))`. Add `SeedHighWater(deviceId, unwrappedSeq)` for P1-1.
- `PostgresReplayGuard.cs` — delete the synchronous `Check` (the blocking `OpenConnection`/`ExecuteReader`). Keep `CheckAsync` as the durable primitive (now only reachable from P1-1's journal/warm-load, not per-frame).
- `Program.cs:69,76` — both branches now register the async guard; startup warning (99-108) unchanged.

**Tests**
- `InMemoryReplayGuardTests.cs:21-72` — migrate `Check`→`await CheckAsync`; assert `ValueTask.IsCompletedSuccessfully` (proves zero I/O / no thread hop on the hot path).
- New `GatewayConnectionCriticalPathTests` — a fake guard whose `CheckAsync` does `await Task.Yield()` still lets the read loop drain subsequent frames; a fake that throws on synchronous blocking proves the sync `Check` is gone.

### P0-2 — H3: per-gateway identity, tenant scope, envelope secrets

**Schema — `007_gateway_identity.sql`** (additive, idempotent, DB-OWNER, mirrored into `TelemetrySchemaService` for owner-capable envs; ADR-006 already drafts this):
- `telemetry_gateways(id BIGINT identity PK, gateway_id VARCHAR(120) UNIQUE NOT NULL, api_key_hash VARCHAR(64) NOT NULL, hmac_secret_ref VARCHAR(400) NOT NULL /* opaque 'vault://…' or 'env:v<n>:<b64>' — never a raw secret */, prev_api_key_hash VARCHAR(64), prev_hmac_secret_ref VARCHAR(400), prev_expires_at TIMESTAMPTZ, status VARCHAR(40) NOT NULL DEFAULT 'Provisioning' CHECK IN ('Provisioning','Active','Suspended','Revoked'), pinned_source_cidrs TEXT[], label, created_by/created_at/rotated_at/revoked_at/last_seen_at)`. Infra registry written pre-tenant-context → **no RLS** (document the rationale inline, like 002/005).
- `telemetry_gateway_tenants(gateway_id VARCHAR(120) REFERENCES telemetry_gateways(gateway_id) ON DELETE CASCADE, company_id BIGINT NOT NULL, added_by/added_at, PRIMARY KEY (gateway_id, company_id))` + `idx_tgt_company`. This is the authorization allowlist.
- `ALTER gps_gateway_replay DROP DEFAULT 'default'` on `gateway_id` (the `UNIQUE(gateway_id, signature)` already scopes replay correctly once code stops passing the constant).
- `ALTER eld_devices ADD hmac_secret_ref VARCHAR(400), ADD device_auth_mode VARCHAR(40) NOT NULL DEFAULT 'ImeiAllowlistOnly' CHECK IN ('ImeiAllowlistOnly','PerDeviceHmac','MutualTls','ClientCertificate')`. Assert `company_id` presence (the tenant-scope backstop is only as trustworthy as `eld_devices.company_id`).
- `telemetry_secret_keyring(key_version INT PK, wrapped_dek BYTEA NOT NULL, kms_key_id VARCHAR(200), algorithm VARCHAR(40) DEFAULT 'AES-256-GCM', created_at, retired_at)` for the envelope default.
- Grants guarded by `IF EXISTS opstrax_app`; RBAC-gate `telemetry_gateway_tenants` writes at the app layer (table not RLS-isolated).

**Code**
- `TelemetrySecretProtector.cs` (new) — `ITelemetrySecretProtector { string Protect(ReadOnlySpan<byte>); Task<byte[]?> ResolveAsync(string secretRef, CancellationToken); }`. `EnvelopeSecretProtector` parses `env:v<n>:<b64>`, AES-256-GCM under a DEK unwrapped from `telemetry_secret_keyring` via KMS or `Telemetry:MasterKek`, zeroes the key after use. Fail-closed (null) on unknown/unavailable, mirroring `ICredentialKeyResolver` (telematics ICredentialKeyResolver.cs:18). `VaultSecretProtector` for `vault://` is P2.
- `GatewayCredentialService.cs` (new) — `AuthenticateAsync(gatewayId, rawApiKey?, timestamp, bodyHex, signatureHex, remoteIp, ct)`: SELECT the gateway row; status ∈ {Active,Provisioning}; constant-time compare `sha256(rawKey)` to `api_key_hash` (or `prev_api_key_hash` while `prev_expires_at>now`); `ResolveAsync` the chosen `hmac_secret_ref`; recompute HMAC over canonical `{gatewayId}\n{timestamp}\n{bodyHex}` and `FixedTimeEquals` (try prev secret in the overlap window); enforce `pinned_source_cidrs` (reuse `DefaultDeviceAuthenticator` IPNetwork logic). `IsTenantAuthorizedAsync(gatewayId, companyId, ct)` → EXISTS in `telemetry_gateway_tenants`.
- `EndpointMappings.cs:11989-12013` — read required `X-Gateway-Id`; call `AuthenticateAsync`; canonical message becomes `{gatewayId}\n{timestamp}\n{bodyHex}` (binds a captured signature to its gateway). Dual-run: fall back to the legacy `Telemetry:GatewaySecret` verification only when no `X-Gateway-Id` present (removed entirely in P1-4).
- `EndpointMappings.cs:12057-12058` — after resolving `device.company_id`, insert the backstop: `if (!await gatewayCreds.IsTenantAuthorizedAsync(resolvedGatewayId, companyId, ct)) { audit 'telemetry.gps.tenant_scope_violation'; return 403; }`. **This line kills the skeleton key.** When `device_auth_mode='PerDeviceHmac'`, additionally require+verify `X-Device-Signature` over the body using the device's own `hmac_secret_ref`.
- `EndpointMappings.cs:12104,12126,12218` — replace `GpsGatewayReplayGuard.DefaultGatewayId` with the resolved real `gatewayId`.
- New endpoints near the device-provision block (RBAC `telemetry.gateways.manage`, all audited): `POST /provision` (mint key shown once, store `api_key_hash`+`Protect()`ed ref, insert caller company into `telemetry_gateway_tenants`), `POST /{id}/rotate` (move current→`prev_*` with `prev_expires_at=now()+overlap` — dual-key rollover the current hard-cut `DeviceRotateSecret` at ~12533 lacks), `suspend`/`revoke`, `POST /{id}/tenants`, `GET /gateways` (prefix+last4 only). First signed forward flips `Provisioning`→`Active` (mirror 12203-12209).
- `ConfigValidationService.cs:53-64` — Phase 1: keep `GatewaySecret` as `warn`, add "≥1 Active gateway" check, validate protector is wired (`MasterKek`/KMS ≥32 bytes). Phase 2 (P1-4): `GatewaySecret` present ⇒ `fail` in Production.

**Tests** (the H3 regression `CrossTenantSkeletonKey_Rejected` must stay green forever): valid signed gps-ingest for company B's IMEI on a gateway authorized only for A → 403, no row in `location_events`/`latest_vehicle_positions`. Plus `SharedSecretRemoved→401`, `SignatureBoundToGateway` (G1 sig replayed as G2 → 401), `ReplayScopedPerGateway`, `RotationOverlap_BothKeysValid`, `PerDeviceHmac_Tier`, `SecretProtector_FailClosed` (503/401, no plaintext in logs or `/gateways`), `SourceIpPin`, `Provisioning_CredentialsShownOnce`, `Commissioning`, `Concurrency…Postgres` (N identical signed packets → one 200, rest 409).

### P0-3 — H1: shared idempotent projection writer (corrected)

**Schema — `008_canonical_ingest_adoption.sql`** (additive, idempotent, DB-OWNER):
- `ALTER canonical_telemetry_events ADD event_id UUID NULL` + `CREATE INDEX idx_cte_event_id … WHERE event_id IS NOT NULL`. No UNIQUE on the partitioned canonical (a partitioned unique must include `event_time`); the inbox is the dedup gate, canonical is append-only after the gate — matches 003's SELECT/INSERT grant.
- **Inbox rekey (correction 1):** `telemetry_projection_inbox` PK `(event_id)` → `(company_id, event_id)`. Table stays **unpartitioned**; retention by batched `DELETE … WHERE projected_at < now()-interval` on `idx_projection_inbox_company_projected`.
- `CREATE FUNCTION telemetry_project_event(p_event_id uuid, p_company_id bigint, p_tenant_id uuid, p_device_id text, p_vehicle_id bigint, p_event_type text, p_lat/p_lng/p_speed/p_heading numeric, p_source/p_provider/p_protocol/p_adapter text, p_confidence/p_trust numeric, p_quality jsonb, p_payload jsonb, p_device_fix_time timestamptz, p_gateway_received_at timestamptz, p_event_time timestamptz) RETURNS text` — one CTE: `WITH ins_inbox AS (INSERT INTO telemetry_projection_inbox(...) VALUES(...) ON CONFLICT (company_id,event_id) DO NOTHING RETURNING 1), ins_cte AS (INSERT INTO canonical_telemetry_events(...event_id...) SELECT ... WHERE EXISTS (SELECT 1 FROM ins_inbox)), ins_lvp AS (INSERT INTO latest_vehicle_positions(...) SELECT ... WHERE EXISTS (SELECT 1 FROM ins_inbox) AND p_vehicle_id IS NOT NULL ON CONFLICT (company_id,vehicle_id) DO UPDATE SET ... WHERE EXCLUDED.device_fix_time IS NOT NULL AND (lvp.device_fix_time IS NULL OR EXCLUDED.device_fix_time >= lvp.device_fix_time) RETURNING 1) SELECT CASE WHEN NOT EXISTS(SELECT 1 FROM ins_inbox) THEN 'DuplicateIgnored' WHEN EXISTS(SELECT 1 FROM ins_lvp) THEN 'Applied' WHEN p_vehicle_id IS NULL THEN 'NoVehicle' ELSE 'StaleIgnored' END`. Both writes gated on `EXISTS(ins_inbox)` (correction 6). Caller runs it after `SET LOCAL app.current_tenant_id`.
- `telematics_ensure_partitions(months_ahead INT, months_back INT)` and `telematics_prune(warm_months INT, inbox_days INT, replay_hours INT)`. Re-`GRANT SELECT,INSERT` on each new child partition inside `ensure` (a missing grant fails-closed and 500s ingest).

**Code**
- `TelemetryCanonicalWriter.cs` (new) — `Task<ProjectionOutcome> WriteAsync(CanonicalFix fix, Guid eventId, CancellationToken)` → `SELECT telemetry_project_event(...)` inside `RunInSystemScopeAsync`.
- `EndpointMappings.cs:11517-11594` (strong) — replace the ad-hoc `location_events` INSERT + LVP upsert with `TelemetryCanonicalWriter.WriteAsync`. `eventId = UuidV5(TelemetryNs, $"{companyId}:{deviceId}:{xNonce}")` (correction 2). **Move the nonce INSERT (11474) inside the writer tx** (correction 7). Run monotonicity on `device_fix_time` from `body.EventTime`, keep `event_time=NOW()` (correction 3).
- `EndpointMappings.cs:12132-12200` (gps) — replace the `location_events` INSERT + LVP upsert (inside the existing `RunInSystemScopeAsync` at 12119) with `WriteAsync`. `eventId = UuidV5(TelemetryNs, $"{companyId}:{eldDeviceId}:{canonicalSig}")`. Keep the existing `device_fix_time` guard (12178) and the durable `GpsGatewayReplayGuard` reservation (12124) — the inbox `ON CONFLICT` becomes a second, storage-level replay defense that composes with it.

**Tests** — `TelemetryCanonicalWriterPostgres`: same `eventId` twice ⇒ one inbox row, one canonical row, one LVP row, second call `DuplicateIgnored`; an older `device_fix_time` after a newer ⇒ canonical row **is** appended (history retained) but LVP unchanged (`StaleIgnored`). REST idempotency: `/ingest` replayed with same nonce → rejected at nonce gate (not double-inserted); `/gps-ingest` replayed with same `canonicalSig` → same `event_id` → deduped even if `GpsGatewayReplayGuard` bypassed. Cross-tenant: writer with `tenant_id=A` cannot read/insert B rows. Live-map non-freeze regression: after deploy, first fix with device time slightly behind server clock still advances LVP (proves correction 3).

### P0-4 — C3: SOS/crash/alarm reaches the backend

**Root cause:** `Gt06Adapter.ToCanonicalEvent` (588) drops `alarmCode`/`alarmName`; the backbone is never bridged (Program.cs:87).

**Contract change**
- `CanonicalTelemetryEvent.cs` — add `public string EventType { get; init; } = "location.updated";` and `public TelematicsAlarm? Alarm { get; init; }` where `TelematicsAlarm(int Code, string Name, AlarmSeverity Severity)`. (The DB `canonical_telemetry_events.event_type` column already exists, 003:51.)
- `Gt06Adapter.cs:588-652` — in `ToCanonicalEvent`, when `fields["alarmName"]` is present, set `EventType = "alarm."+name` and `Alarm = new(code, name, MapSeverity(name))`. `MapSeverity`: `SOS`/`Fall`→Critical, `Vibration`/`Overspeed`/`PowerCut`→High, else Medium. Also surface `Vibration`(0x03)/`Fall`(0x13) as the crash-candidate class.

**Bridge**
- `telemetry_project_event()` (P0-3) writes `event_type` into canonical. When `p_event_type LIKE 'alarm.%'`, the writer additionally inserts a `telemetry_alerts` row (`alert_type` = mapped token: `sos`, `crash`, `power_cut`, `overspeed`, `geofence_breach`; `severity` from `AlarmSeverity`; `source_event_id` = the canonical `event_id`). This is the seam that carries the alarm from the projection into the safety pipe that already exists.
- Gateway side: `PostgresPositionProjectionStore.ApplyAsync` — when `evt.Alarm is not null`, call the same `telemetry_project_event` path so a TCP-decoded SOS lands a `telemetry_alert` (bridges the backbone to the backend for safety-critical frames without waiting for the full Kafka bridge in P2).
- `SafetyBackgroundService.cs:90` — widen `alert_type IN ('speeding','geofence_breach','stale_device')` to also include `('sos','crash','power_cut','overspeed','harsh_braking','harsh_acceleration','harsh_cornering')`. The existing `telemetry_alerts → safety_events` conversion (76-159), dedup unique key, evidence hash, and recommendation creation then apply unchanged.

**Tests** — GT06 alarm-frame fixture (0x16, `alarmCode=0x01` SOS) through the adapter ⇒ canonical `EventType="alarm.SOS"`, `Alarm.Severity=Critical`; through the projection ⇒ one `telemetry_alerts` row `alert_type='sos'`; SafetyBackgroundService tick ⇒ one `safety_events` row `event_type='sos' severity='Critical'`. Crash candidate (0x03 Vibration) ⇒ `alert_type='crash'`. Idempotency: reprocessing the same alarm event ⇒ no duplicate safety_event (existing `uq_se_telemetry_alert`).

### P0-5 — C2: harsh-driving detection + dashboard vocabulary fix

**This is the biggest deal-loser vs Samsara — two honest tiers, both shipped:**

**Tier 1 — device-reported harsh/crash events (authoritative when present):**
- Add an optional `harshEvent` field to both ingest paths. gps-ingest (`EndpointMappings.cs:12034-12071` region): parse `Str("harshEvent","event","alarmType")` ∈ `{harsh_brake, harsh_accel, harsh_corner, crash, rollover}`; when present, emit a `telemetry_alerts` row of the mapped type in the same tx. Strong path (`/ingest`): `body.EventType` already flows to `location_events.event_type` (11539); map known harsh tokens to a `telemetry_alerts` insert.
- GT06 `Vibration`/`Fall` alarms (from P0-4) already map to `crash`/harsh candidates.

**Tier 2 — server-side derivation (fallback for dumb GPS-only units, the common OpsTrax case):**
- New `HarshEventDetectionService` (BackgroundService, or fold into `SafetyBackgroundService`'s tick): per vehicle, over the last N `canonical_telemetry_events`/`location_events`, compute longitudinal acceleration `a = Δspeed/Δt`. Flag `harsh_braking` when `a < -T_brake` (default ~-0.35g ≈ -11 km/h/s), `harsh_acceleration` when `a > T_accel`, `harsh_cornering` from heading-rate × speed. Thresholds are tenant-configurable via `telemetry_rules` (same pattern as the `speeding` threshold read at 11513). Write `telemetry_alerts` (deduped by `(vehicle_id, event_time bucket)`), which flow to `safety_events` via P0-4's widened vocabulary.
- Honesty: Tier 2 is lower-fidelity than a real 100Hz accelerometer; mark these `safety_events` with `meta_json.detection='derived'` so coaching/disputes can distinguish device-reported from server-derived. Tier 1 supersedes Tier 2 for the same window.

**Dashboard vocabulary fix (the "label over an empty set"):**
- `EndpointMappings.cs:6106-6107` sums `event_type='Harsh Braking'`/`'Harsh Acceleration'`/`'Speeding'` (title-case). The pipeline writes lowercase snake tokens (`harsh_braking`, `speeding`). Align them: either normalize the writer to the dashboard's tokens or (preferred, matches the P4 lowercase-token convention in CLAUDE.md) change the dashboard SQL to `event_type='harsh_braking'` etc. Do the same at 13382 (`event_type='Harsh Braking' AS repeated_speeding` — also mislabeled). Add `harsh_cornering` and `crash`/`sos` tiles.
- Safety score: `SafetyBackgroundService.ComputeScoreAsync` already sums `score_impact` across event types generically (345-381), so once harsh/crash `safety_events` exist the driver score stops being a pure speeding score automatically. Seed `telemetry_rules` weights `safety_weight_harsh_braking`, `_harsh_acceleration`, `_harsh_cornering`, `_crash` (crash Critical).

**Tests** — speed trace 60→10 km/h in 2s ⇒ derived `harsh_braking` alert → `safety_events` with `meta.detection='derived'`; a device-reported `harshEvent=harsh_brake` in the same window supersedes (no double count); dashboard `SafetySummary` returns non-zero `harsh_braking` after a tick (regression against the empty-set bug); driver score reflects harsh deductions, not just speeding.

---

## D. Build now vs design-for-later

**Build now (P0/P1):**
- C1 async interface + two-tier durable replay with never-shed unwrapped watermark.
- H3 per-gateway identity, tenant-scope reject, `PerDeviceHmac` tier, envelope `EnvelopeSecretProtector`, provisioning/rotation, shared-secret deletion.
- H1 `telemetry_project_event()` single-artifact writer, tenant-local deterministic `event_id`, canonical adoption in both REST + gateway, partition automation.
- C3 alarm→canonical→telemetry_alert→safety_event bridge for SOS/crash.
- C2 both tiers (device-reported + server-derived harsh) and the dashboard vocabulary fix.

**Design now, build later (P2), with the seam already in place:**
- **mTLS/ClientCertificate device tier** — `device_auth_mode` enum already carries it; terminate at the LB, assert upstream. The north-star Samsara/Geotab bar for capable hardware.
- **Kafka/Redpanda write-behind** — ADR-002 exists; the deterministic `event_id` + inbox seam is the exactly-once attach point. Only needed past the Postgres synchronous-write ceiling.
- **`VaultSecretProtector`** (`vault://` handles) — the `ITelemetrySecretProtector` seam is built; wiring HashiCorp/AWS SM is config, not code.
- **Full TCP-fabric→backend bridge** — P0-4 bridges only safety-critical alarms via the projection store's `telemetry_alerts` write; the general canonical event stream over the fabric identity (C3 at volume) is P2 once the gateway is actually deployed (the PT40 runbook lists gateway deployment as a root blocker).
- **Real accelerometer/g-force canonical fields, crash-pulse severity, dashcam correlation, ML/behavioural scoring + auto-quarantine** — the `CanonicalTelemetryEvent.Signals` VSS bag + `TrustScore`/`LowSpoofable` already exist as the extension points; no schema fork needed.
- **`location_events` retirement** — design the reader repoint (correction 8) now; execute drop in P2 after readers move.

**Explicitly NOT now:** do not write a `ProtocolName` for the PT40-Q anywhere until a real capture confirms its fingerprint (the field-commissioning gate; pt40-fingerprint.md:10-13). Do not claim ImeiAllowlistOnly (dumb GT06) units are cryptographically authenticated — they are scoped, gateway-authenticated, and IP/SIM/replay-pinned, not crypto-authenticated.

---

## E. PT40-Q end-to-end simulation plan (IMEI 862464068456321)

**Honesty gate first:** the physical PT40-Q's protocol is UNKNOWN/unconfirmed (pt40-fingerprint.md:3-8) — reputed GT06 but no captured byte yet. So this plan proves the *pipeline* against GT06-shaped frames (the adapter is real and 30-test-covered) and against the REST path; it does **not** assert the PT40-Q speaks GT06 until a lab capture passes the Confirm step. Two tracks, one identity.

**Fixture setup (both tracks):** seed an `eld_devices` row `imei='862464068456321'`, `device_serial='4C4000067803'`, `company_id = C_pt40`, `status='Provisioning'`, `vehicle_id = V`, `device_auth_mode='PerDeviceHmac'` with a provisioned `hmac_secret_ref`. Provision a `telemetry_gateways` row `gateway_id='gw_pt40lab'` scoped in `telemetry_gateway_tenants` to `C_pt40` only. IMEI 862464068456321 packs to the 8-byte GT06 login terminal id `08 62 46 40 68 45 63 21` (one leading pad nibble + 15 digits).

**Track A — TCP gateway, GT06 binary (proves C1 + C3 + H1 gateway path):**
1. Build a small frame generator (extend `tools/telematics/fingerprint.py`'s codec, or a new `tools/telematics/pt40_sim.py`) emitting CRC-ITU-valid frames: **login** (0x01, terminal id above), **location** (0x12, a valid fix), a **harsh/crash candidate** (0x16 alarm, `alarmCode=0x03` Vibration), and an **SOS** (0x16 alarm, `alarmCode=0x01`). Reuse `Gt06Adapter.Crc16Itu` semantics (verified against `"123456789"→0x906E`).
2. Drive them over TCP into the gateway listener (dev gateway with a Telematics Postgres connection so durable stores engage). `capture_listener.py` archives the raw bytes for evidence.
3. **Assert C1:** the read loop stays responsive — send a burst while a fake/slow durable warm-load is in flight; frames keep draining (no thread-pool stall). `ValueTask` hot path completes synchronously for in-order frames.
4. **Assert C3:** the SOS frame produces a `CanonicalTelemetryEvent` with `EventType="alarm.SOS"`, `Alarm.Severity=Critical` (not a bare position); the projection writes a `canonical_telemetry_events` row (P1-2) **and** a `telemetry_alerts` row `alert_type='sos'`; after a `SafetyBackgroundService` tick a `safety_events` row `event_type='sos' severity='Critical'` exists and the safety dashboard SOS/critical tile increments. The Vibration frame lands `alert_type='crash'`.
5. **Assert H1:** replay the exact SOS frame bytes → `DuplicateReplay` at the guard AND, if it reached the writer, `event_id` inbox `ON CONFLICT` dedups (one canonical row, one alert). Out-of-order location (lower serial) is flagged `IsOutOfOrder`, retained, but does not stamp LVP backward.

**Track B — REST gps-ingest, per-device auth (proves H3 + C2 + H1 REST path):**
1. Sign `POST /api/telemetry/gps-ingest` with `X-Gateway-Id: gw_pt40lab`, HMAC over `{gatewayId}\n{timestamp}\n{sha256(body)}` using gw_pt40lab's key, `X-Device-IMEI: 862464068456321`, `X-Device-Signature` over the body with the device key, body carrying a fix + `harshEvent:"harsh_brake"`.
2. **Assert H3 (the forever-green regression):** re-send the same valid gateway signature but with `X-Device-IMEI` = a device belonging to a *different* tenant → **403 tenant_scope_violation**, zero rows written for that tenant. Then send with no `X-Device-Signature` on a `PerDeviceHmac` device → 401 (IMEI spoofing alone fails). Then send under a second, differently-scoped gateway → replay is a distinct message (`gps_gateway_replay` rows carry the true `gateway_id`, not `default`).
3. **Assert C2:** the `harshEvent` fix produces a `telemetry_alerts` row `harsh_braking` → `safety_events` `harsh_braking` → dashboard `harsh_braking` tile non-zero and driver safety score deducted (no longer a pure speeding score). Separately, POST a two-sample speed trace (60→10 km/h/2s) with no `harshEvent` and confirm the server-derived Tier-2 detector raises `harsh_braking` with `meta.detection='derived'`.
4. **Assert H1:** replay the same signed body (same `canonicalSig`) → same tenant-local `event_id` → `Conflict` at `GpsGatewayReplayGuard` and inbox dedup; a legitimate retry after a simulated write failure still succeeds (nonce/reservation rolled back inside the tx — proves correction 7).
5. **Assert commissioning:** the first valid signed forward flips both `eld_devices` `Provisioning→Active` (12207) and `telemetry_gateways` `Provisioning→Active`.

**Evidence output:** archive raw captures + a pass/fail table keyed to C1/C2/C3/H1/H3 assertions, linked from `docs/telematics/pt40/pt40-go-live-checklist.md`. Naming: any Postgres-touching integration test ends in `*Postgres` per repo convention (c97652d).

---

## F. Standing invariants to document alongside the code (so the guarantees don't silently rot)

- **Single-bind:** registry/auth rejects a second concurrent TCP bind for an already-bound `device_id`; the two-tier in-memory gate's authority depends on it.
- **Watermark is sacred:** `telemetry_device_watermark` is never pruned and never shed; only `telemetry_replay_seen` dedup rows are sheddable. `telematics_prune` touches `telemetry_replay_seen`/aged partitions only.
- **`event_id` seed = post-auth tenant-local identity.** Never derive from a constant `gateway_id` or a pre-ownership claim.
- **Partition automation is load-bearing:** ship `TelemetryPartitionBackgroundService` (or pg_cron) with migration 008, health-gated, alarming on `_default` row count, pre-creating forward **and** backward (30-day) partitions.
- **Interface break is atomic:** `Check`→`CheckAsync` touches InMemory/Postgres guards, `TcpGatewayService`, and all guard tests — one PR, or the tree won't compile (grep confirms the only production call site is GatewayConnection.cs:411).
- **H3 is not closed until P1-4:** during dual-run the shared secret still works; the skeleton key is only dead when `Telemetry:GatewaySecret` is deleted and `ConfigValidationService` fails-on-present. Track per-forwarder cutover to a hard deadline.