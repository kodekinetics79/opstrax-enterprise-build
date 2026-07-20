# Platform Admin Capability Matrix

Audited 2026-07-02 on branch `harden/platform-admin-control-plane`.

Legend: ✅ Exists and verified · 🟡 Exists but incomplete · 🔴 Exists but unsafe ·
⬜ Missing · ➖ Not applicable. "Verified" means proven by an automated test or
the smoke script in this pass (see final column).

## Tenant Lifecycle

| Capability | Before this pass | After this pass | Evidence |
|---|---|---|---|
| List tenants | 🟡 exists, untested | ✅ | `PlatformControlPlaneTests`, smoke §3 |
| View tenant detail (sub + entitlements + invoices) | 🟡 exists, untested | ✅ | tests, smoke §4 |
| Create tenant | 🔴 dup code → 500 | ✅ dup code → 409 | tests, smoke §5 |
| Edit tenant | 🟡 200 on missing id | ✅ 404 on missing id | tests, smoke §6 |
| Suspend tenant | 🟡 exists, untested | ✅ | tests, smoke §9 |
| Reactivate tenant | 🟡 exists, untested | ✅ | tests, smoke §11 |
| Cancel tenant | 🟡 exists, untested | ✅ | tests |
| Archive tenant | ⬜ no distinct archive state | ⬜ intentionally not built — `cancel` retains all data (soft off state); see standard | — |
| Hard-delete / offboard tenant | 🔴 same permission as routine edits | ✅ dedicated `platform:tenants:offboard` + typed confirm | tests, smoke §12 |
| Create tenant admin (invite) | 🟡 exists, untested | ✅ | tests |
| Reset/re-invite tenant admin | 🟡 API only, no UI | ✅ API tested + UI button | tests, UI |
| Revoke tenant sessions | 🟡 only implicit via suspend/cancel | ✅ explicit audited endpoint + UI | tests, smoke §10 |

## Commercial Control

| Capability | Before | After | Evidence |
|---|---|---|---|
| Plan/package assignment | 🟡 untested | ✅ | tests |
| Status vocabulary (trial/active/past_due/suspended/cancelled/manual_contract) | 🟡 untested; invalid action rejected 400 | ✅ | tests |
| Seat limits | 🟡 create-only in UI | ✅ editable in tenant detail; enforced server-side at user creation | tests, UI |
| Vehicle limits | ⬜ | ⬜ intentionally not built — `tenant_entitlements.limit_value` per `fleet` module is the designed slot; enforcement hook not yet wired | — |
| Driver limits | ⬜ | ⬜ same as vehicle limits | — |
| Asset/device limits | ⬜ | ⬜ same | — |
| AI limits | ➖ AI module gating exists via entitlements; usage quota not built | ➖ | — |
| Usage visibility (per-tenant counts) | 🟡 user counts only | 🟡 user counts + health; record-count diagnostics deferred (P2) | — |
| Revenue/invoice summary | 🟡 exists | ✅ | tests, smoke |

## Feature Control (server-enforced entitlements)

| Capability | Before | After | Evidence |
|---|---|---|---|
| Module enable/disable API | 🟡 untested | ✅ | tests |
| Enforcement: disabled module blocks tenant API | 🟡 middleware exists, untested | ✅ | tests |
| Invalid module key rejected | 🔴 accepted anything | ✅ format-validated (lowercase snake_case id); injection/typo payloads → 400 | tests |
| AI enable/disable | ✅ via `ai` module key | ✅ | tests (module toggle path) |
| Finance enable/disable | ✅ via `finance` key | ✅ | same |
| Maintenance enable/disable | ✅ via `maintenance` key | ✅ | same |
| Dispatch enable/disable | ✅ via `dispatch` key | ✅ | same |
| Reports enable/disable | ✅ via `reports` key | ✅ | same |
| Customer portal enable/disable | ✅ via `customer_portal` key | ✅ | same |
| Mobile/driver feature | ➖ no separate mobile module key | ➖ | — |

## Security / Governance

| Capability | Before | After | Evidence |
|---|---|---|---|
| Platform login | 🟡 works, unaudited failures | ✅ | tests, smoke §1 |
| Platform route protection (401 unauth) | 🟡 | ✅ | tests, smoke |
| Tenant token blocked from platform routes | 🟡 by construction, unproven | ✅ proven | tests, smoke §13 |
| Platform token rejected by tenant APIs | 🟡 by construction, unproven | ✅ proven | tests |
| Audit log on every mutation | 🟡 implemented, unproven | ✅ proven for lifecycle/entitlement/billing | tests, smoke §14 |
| Audit log tamper-proof via APIs | ✅ no write/update/delete endpoint exists | ✅ | code audit |
| Session revocation on suspend/cancel | 🟡 implemented, unproven | ✅ proven | tests |
| Failed-login security visibility | ⬜ | ✅ `platform.login_failed` audit rows | tests |
| Rate limiting on platform + login routes | 🔴 bypassed the limiter entirely | ✅ limiter runs before auth bypass | code + test |
| Dangerous action confirmations (UI) | 🔴 none | ✅ confirm for suspend, typed confirm for cancel | UI + browser plan |
| No secret leakage in responses | 🟡 unproven | ✅ asserted in tests | tests |
| Default superadmin credential guard | 🔴 silent fallback | ✅ config validation fails in production when env unset/default | code + test |
| SQL injection inert (tenant search) | ✅ all queries parameterized; search is client-side | ✅ payload test | tests |
| XSS payload in tenant name/notes | 🟡 stored raw; React escapes | ✅ stored safely, returned as data, never executed server-side | tests |

## Operations

| Capability | Before | After | Evidence |
|---|---|---|---|
| Tenant health scores | 🟡 exists | ✅ | tests, smoke |
| Record counts | 🟡 users only | 🟡 users only (P2) | — |
| Last login visibility | 🟡 platform admins only | 🟡 (tenant users last-login P2) | — |
| Active sessions count | ⬜ | 🟡 revoke endpoint reports revoked count | tests |
| API health | ✅ /api/health, /health/* probes | ✅ | smoke |
| DB schema drift signal | 🟡 schema_migrations ledger exists | ✅ platform schema now in migration `2026_07_02_stage26` | Phase 8 |
| Support diagnostics | ⬜ | ⬜ intentionally not built (needs support-access model first) | — |
| Acme pilot visibility | ✅ tenant list/detail/health | ✅ | smoke §4 |

## Intentionally NOT built (and why)

- **Impersonation / support access to tenant data** — no safe audited model
  exists yet; building it quickly would create the exact backdoor this pass is
  meant to prevent. Reserved table + permission exist for a future design.
- **Archive as separate state** — `cancel` already retains all tenant data and
  blocks access; a distinct archive lifecycle adds state-machine complexity the
  pilot does not need.
- **Per-resource quotas (vehicles/drivers/devices)** — the entitlement
  `limit_value` slot exists; enforcement wiring is a scoped follow-up, not a
  pilot blocker.
- **Support diagnostics beyond health/counts** — requires the support-access
  model first.
