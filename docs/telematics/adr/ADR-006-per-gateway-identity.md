# ADR-006 — Per-gateway identity & credentials (P1-001)

**Status:** Accepted (design). Implementation pending user go-ahead.
**Supersedes the "single global `Telemetry:GatewaySecret`" trust model for `POST /api/telemetry/gps-ingest`.**
Synthesizes three independent specialist designs (IAM/crypto, PostgreSQL/RLS, AppSec threat model) and
reconciles them with the prior sketches in `security/identity-trust-architecture.md` and
`security/legacy-cutover-plan.md`.

## Problem
One process-wide HMAC secret authenticates *every* gateway forward. A leak = fleet-wide forgery
for any known IMEI, across all tenants. There is no gateway identity, rotation, revocation, or
per-tenant authorization. The `gps_gateway_replay.gateway_id` column already exists but is hardwired
to the constant `"default"` — a deliberate seam for this work.

## Decisions

1. **Credential scheme: per-gateway HMAC now; ed25519/mTLS as the target end-state (phased).**
   Same wire format as today (HMAC over the signed message), keyed per gateway. A new `X-Gateway-Id`
   header *selects* which credential to verify against — it is an untrusted lookup key, never proof.

2. **Storage — the key correction from the brief: an HMAC key CANNOT be a one-way hash.** The server
   must hold recoverable key bytes to recompute the MAC. So the symmetric secret is stored **AEAD-
   encrypted at rest** (app/KMS-wrapped), never plaintext, never in config; a separate **SHA-256
   fingerprint** is stored for lookup/audit/reuse-detection only. The honest "a DB read discloses no
   forgery capability" property belongs only to the **ed25519/mTLS** path (store public key / cert
   fingerprint) — which is exactly why that is the declared next phase, not cosmetic. If app-managed
   encryption keys aren't in place, stand up the ed25519 path first.

3. **Schema (no RLS — control-plane infra, like `gps_gateway_replay`/`platform_audit_log`).** The
   credential is read *before* any tenant is known (company is resolved from `eld_devices` only after
   the signature verifies), and a multi-tenant forwarder has no single `company_id`, so no correct RLS
   predicate exists. Three tables:
   - `telematics_gateways` — identity + credential (current + previous for rotation) + lifecycle.
   - `telematics_gateway_grants` — **the authorization truth**: which `(company_id[, device_id])` a
     gateway may submit for. `device_id NULL` = whole company. Single-tenant gateway = one row.
   - `telematics_gateway_audit` — per-decision + lifecycle audit ledger (append-only, pruned).
   Deploy-safe dual path (TelemetrySchemaService ensure + owner migration `stage34`), matching stage33.

4. **Rotation: dual-active with a bounded overlap.** `current` + `previous` credentials +
   `prev_expires_at`. Verify current first (steady state = one compare); on miss, try previous only
   while `now() <= prev_expires_at`; audit `rotation_previous_used`. Zero-downtime; old key hard-fails
   fail-closed after expiry.

5. **Lifecycle: `Provisioning | Active | Suspended | Revoked`.** Suspended = authenticated-but-forbidden
   (**403**). Revoked = credential material destroyed → verification structurally fails (**401**, no
   existence oracle). Status checked fresh per request (revocation is immediate, not eventually-consistent).

6. **Tenant-scoped authorization is the highest-value new control.** After the device resolves its
   `company_id` **from `eld_devices` (never the payload)**, assert that company ∈ the gateway's grants;
   else **403**, no write. Binding tenant from the device row alone is necessary but NOT sufficient.

7. **Replay scope becomes real.** Pass the *resolved, verified* `gateway_id` (not `DefaultGatewayId`)
   into `GpsGatewayReplayGuard` so each gateway occupies its own replay namespace.

8. **Backward-compatible cutover (must not break Khalid's pilot).** Dual-path, flag-gated
   (`Telemetry:GatewayRegistryMode: shadow → enforce`):
   - No `X-Gateway-Id` → legacy global-secret path (pilot unaffected), `gateway_id='default'`.
   - `X-Gateway-Id` present + registry `Present` → per-gateway verify + grant check.
   - **Fail-closed asymmetry:** `X-Gateway-Id` present but registry `Absent`/`ProbeError` → **503**
     (never silently fall back to the global secret for a *claimed* gateway). No header + ProbeError →
     still legacy (pilot never breaks). Mirrors `GpsGatewayReplayGuard`'s ProbeError→503 idiom.
   - Migration backfills a `'default'` gateway row + one grant per company that currently has devices
     (a tightening: the global secret goes from "any company" to "companies with devices today").
   - `shadow` mode computes + audits the grant decision but honors the legacy verdict; flip to
     `enforce` per-entity once audit shows zero false denials, then remove `Telemetry:GatewaySecret`.

## Invariants (must hold)
Authorization is scoped not global · IMEI/serial is never authentication · tenant resolved server-side
always · gateway identity is proven not claimed · replay scope = authenticated gateway · revocation is
atomic/immediate · rotation overlap bounded & fail-closed · no signing secret plaintext at rest · legacy
path explicitly gated & removable · fail-closed everywhere · no ingest-time lifecycle promotion ·
every accepted fix attributable to a gateway id.

## Acceptance criteria & test matrix
See the threat-model deliverable (captured in the findings ledger). Security tests must all REJECT:
wrong gateway secret, revoked gateway, cross-tenant submission, expired-after-rotation old key,
downgrade attack (migrated gateway's device via legacy secret), cross-gateway replay, IMEI-as-auth,
spoofed cert-fingerprint header, replay-store ProbeError → 503. Regression: **Khalid's PT40-Q
(device 1011 / IMEI …6321 / company 8 / vehicle 1024) keeps landing fixes at every phase**, verified in
shadow before any enforcement flip.

## Naming reconciliation (open, decide at implementation)
Prior docs use `telemetry_gateways`/`telemetry_gateway_devices`; the schema design proposes
`telematics_gateways`/`telematics_gateway_grants`. Pick one namespace before writing the migration;
`telematics_*` aligns with the newer `gps_gateway_replay` + `DefaultGatewayId` seam. The junction
(`_grants`, authz-as-truth, device-grain optional) is preferred over a `company_id` column on the
gateway (which can't model a multi-tenant forwarder).

## Anti-patterns (do NOT)
Trust `X-Gateway-Id` without verifying the signature · leave replay scope as `"default"` · derive tenant
from payload or keep a `company_id=1` default · skip the grant check · fall back to the global secret
when a *claimed* gateway can't be verified · cache revocation for long · store the HMAC key hashed
(forces a weaker bearer secret) or in plaintext/config · derive per-gateway keys from a global root ·
one key for "a few" gateways · big-bang enforcement flip without shadow validation.
