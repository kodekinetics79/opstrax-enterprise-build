# OpsTrax Platform Admin — Control Plane Standard

**Status: Enforced.** This document defines what the Platform Admin (System Owner
Console) is allowed to do, what requires elevated protection, and what it must
never do. Any change to `/api/platform/*` or `frontend/src/pages/platform/`
must conform to this standard.

## Executive principle

Platform Admin is a **SaaS business control plane**, not a superuser backdoor.
It controls *tenants as commercial entities* (lifecycle, subscription, billing,
entitlements, limits, health) and *platform governance* (audit, sessions,
security visibility). It does **not** read or edit tenant operational data
(jobs, vehicles, drivers, customers, finance records) — tenant data belongs to
the tenant and is reachable only through tenant RBAC or a future explicit,
audited support-access workflow.

## Architecture invariants (must always hold)

1. **Separate identity plane.** Platform staff live in `platform_admins`, never
   in tenant `users`. Sessions live in `platform_sessions`, never `user_sessions`.
2. **Token separation.** A tenant bearer token MUST return 401 on every
   `/api/platform/*` route. A platform bearer token MUST return 401 on every
   tenant route. There is no shared token format, secret, or lookup table.
3. **Per-handler auth.** Every platform handler authenticates itself via
   `PlatformEndpoints.RequireAsync(http, db, "<permission>", ct)` — no handler
   may rely on middleware alone.
4. **Namespaced RBAC.** Platform permissions use the `platform:` prefix and can
   never collide with tenant permission keys.
5. **Every mutation is audited.** Any handler that changes state MUST write a
   `platform_audit_log` row (actor, action, entity, target company, details,
   IP) before returning success. No API may update or delete audit rows.
6. **Server-side enforcement.** Entitlements, billing gates, and suspension are
   enforced in the API/middleware, never only in the UI.
7. **No secrets in responses.** Password hashes, session tokens (other than the
   caller's own), connection strings, and env values never appear in any
   platform API response or log.

## Allowed (standard platform permissions)

| Capability | Permission | Notes |
|---|---|---|
| Create tenant | `platform:tenants:manage` | Auto-generates unique code; optional package, country cascade, admin invite |
| Update tenant profile | `platform:tenants:manage` | Name, owners, contract dates, currency, seat limit |
| Suspend / reactivate / cancel tenant | `platform:tenants:manage` | MUST revoke all tenant sessions on suspend/cancel and mirror `companies.status` |
| Extend trial / manual contract | `platform:tenants:manage` | |
| Change plan / assign package | `platform:tenants:manage` | Recomputes MRR; seeds entitlements without clobbering overrides |
| Set limits / quotas (seats) | `platform:tenants:manage` | Enforced at tenant login/user-creation paths |
| Enable / disable modules | `platform:entitlements:manage` | Server-enforced at the API middleware per request |
| Create / reset tenant admin invite | `platform:tenants:manage` | Creates `users` row with status `Invited`; never sets a password directly |
| Revoke tenant sessions | `platform:tenants:manage` | Explicit endpoint; also implicit on suspend/cancel |
| Billing: invoices, mark paid | `platform:billing:manage` | |
| Packages / pricing CRUD | `platform:packages:manage` | |
| Country profiles | `platform:countries:manage` | |
| View tenant health / counts / usage | `platform:health:view` | Aggregates only — never row-level tenant operational data |
| View audit / security events | `platform:audit:view` | |
| View dashboards | `platform:dashboard:view` | |

## Restricted — allowed only with elevated permission + mandatory audit

| Capability | Requirement |
|---|---|
| **Tenant offboarding (hard delete)** | Dedicated permission `platform:tenants:offboard` (super admin only via `platform:*`), a body confirm token equal to the tenant's `company_code`, and an audit row that survives the deletion. Never exposed as a casual UI button. |
| **Support access to tenant data / impersonation** | **Not implemented.** The `platform_impersonation_sessions` table and `platform:impersonation:start` permission are reserved. If ever built it MUST be: time-boxed, reason-required, visibly audited to the tenant, and read-only by default. Do not build silent impersonation. |
| **Export of tenant data** | Only through tenant-scoped, tenant-RBAC'd export endpoints. The platform plane has no bulk tenant-data export. |
| **Emergency lockout** | Use suspend (revokes sessions + blocks login). There is no separate "kill switch" that bypasses audit. |
| **Manual DB-like corrections** | Never through ad-hoc SQL against production-like DBs. Schema changes require a migration file; data corrections require an audited endpoint or a documented, reviewed runbook entry. |

## Not allowed — ever

- Silent tenant data access without an audit trail.
- Hard delete of tenant data outside the protected offboarding workflow.
- Returning or logging secrets (password hashes, tokens, connection strings).
- Bypassing tenant RBAC "for convenience" — the platform token is not a tenant
  super-token and MUST NOT be accepted by tenant operational APIs.
- Mutating production-like data without traceability (audit row or migration).
- Seeding or keeping default credentials in production (`PLATFORM_SUPERADMIN_*`
  env vars are mandatory in production; config validation flags the default).

## Session & authentication rules

- Platform sessions: opaque 256-bit random tokens, 8-hour expiry, revoked on
  logout. No JWT — nothing to forge offline.
- Platform login failures MUST be audited (`platform.login_failed`, email + IP,
  never the password) and rate-limited.
- Suspending or cancelling a tenant MUST delete all of that tenant's
  `user_sessions` rows in the same request — blocking new logins is not enough.

## Frontend rules

- The platform SPA (`/platform/*`) uses its own auth context and storage key —
  never the tenant session.
- Destructive actions (suspend, cancel, offboard) require explicit user
  confirmation in the UI; cancel/offboard require typed confirmation.
- 401 responses clear the platform session and return to `/platform/login`.
- Screens must render real loading / empty / error states — no silent seed data.
