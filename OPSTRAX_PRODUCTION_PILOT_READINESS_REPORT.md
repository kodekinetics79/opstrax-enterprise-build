# OpsTrax — Production Pilot Readiness Report

**Session scope:** production-infrastructure + security-closure only. No CRM, no UI/UX
conformance, no new features. Two real pilot customers (Canada, Saudi Arabia) waiting
to onboard real operational/financial data — this session closes the go-live gates.

- Remote confirmed: `origin → github.com/kodekinetics79/opstrax-enterprise-build.git`
  (the `zayra` remote is the off-limits sibling; untouched).
- **Final build:** `dotnet build` → 0 errors. Frontend `tsc --noEmit` → clean (no UI files changed).
- **Final tests:** `dotnet test` → **884 passed / 0 failed / 0 skipped**
  (882 prior baseline + 2 new tenant-offboarding Postgres tests; the previously-failing
  demo-seeder idempotency test now passes — see STEP 6). No test disabled or weakened.

---

## GO / NO-GO

### 🔴 NO-GO for real pilot data **until the owner-action items below are cleared.**

The **codebase** is ready: RLS activation gaps closed, the two deferred security holes
fixed (defense-in-depth), and tenant "delete on request" is now provable. But go-live is
gated on items only Zack can action outside the repo (credential rotation confirmation,
production DB tier + separation, and a staging smoke-test under real RLS enforcement).
None of these can be self-certified from inside the codebase.

**Codebase gates: CLEARED. Operational gates: PENDING owner action (listed at the end).**

---

## STEP 1 — Production environment

### 1a. Hosting setup — what exists vs. what needs provisioning
**Exists in-repo:**
- [render.yaml](render.yaml): `opstrax-api` (Docker, .NET) with `healthCheckPath: /health`,
  `ASPNETCORE_ENVIRONMENT=Production`, and `PG_CONNECTION` as a **`sync: false`** secret
  (i.e. set in the Render dashboard, never in git). A second `opstrax-events` Node service.
- [docker-compose.yml](docker-compose.yml): local/dev only — maps `PG_CONNECTION` from `.env`.
- [Database.cs](backend-dotnet/Data/Database.cs) resolves the connection string from
  `ConnectionStrings__DefaultConnection` → `PG_CONNECTION` and **fails fast** if neither is set.
- Neon transient-retry logic is already in `OpenAsync` (serverless pooler cold-start aware).

**Needs provisioning (owner action — cannot be done from the codebase):**
- A **dedicated production Neon project/branch on a paid tier** (not the dev/free branch).
  Nothing in the repo pins a Neon tier — it's whatever `PG_CONNECTION` points at. This must
  be a separate, backed-up production database, provisioned in the Neon dashboard by Zack.
- The **restricted `opstrax_app` role** must be created in that production DB and given a
  password (see STEP 2). Migration `2026_06_30_stage20_rls_force_and_app_role.sql` creates
  the role; the password is set out-of-band (`ALTER ROLE opstrax_app WITH PASSWORD …`).

### 1b. Config separation — CONFIRMED separated
- No credentials in git: `appsettings.json` ships an **empty** connection string with a note;
  `.gitignore` ignores `appsettings.json` / `appsettings.*.json` / `.env*` (keeping only the
  `.example` files). Production secrets live only in the Render dashboard (`sync: false`).
- Local/dev uses `.env` → docker-compose; production uses Render env vars — **no shared
  credentials between environments** as configured.
- ⚠️ **Minor finding (defense-in-depth):** `api-dotnet/appsettings.json` is **tracked** in git
  (added before the ignore rule) though it is currently empty of secrets. It is the legacy
  MySQL-side config, not the live .NET API's (`backend-dotnet/appsettings.json`). Recommend
  `git rm --cached api-dotnet/appsettings.json` so it can never re-capture a secret locally.
  Not blocking (no secret present today).

---

## STEP 2 — RLS activation (finishing the deferred work)

All items from `OPSTRAX_RLS_ACTIVATION_REPORT.md`'s "remaining activation work" are now done
in code, **safe-by-default** (everything is a no-op wrapper when `Rls:EnforceTenantContext`
is false — which is why all 884 tests are unaffected).

### 2a. Background services — DONE (checked each individually)
All **8** hosted services do genuinely **cross-tenant** work (they iterate every company's
rows and filter by `company_id`/`tenant_id` in SQL). Under the restricted role with no
context, RLS returns 0 rows → silent no-op. Each is therefore wrapped in the
platform-admin **bypass** scope (`BeginSystemScopeAsync`), not a per-tenant scope:

| Service | Wrapped | Scope |
|---|---|---|
| EscalationBackgroundService | ✅ | system bypass (all-company escalation rules) |
| MaintenanceBackgroundService | ✅ | system bypass (all-company PM rules) |
| SafetyBackgroundService | ✅ | system bypass (all-company telemetry→safety) |
| TelemetryBackgroundService | ✅ | system bypass (all-company positions/nonces) |
| TelemetrySimulatorBackgroundService | ✅ | system bypass (demo/dev only) |
| TripBackgroundService | ✅ | system bypass (all-company routes) |
| ScheduledReportBackgroundService | ✅ | system bypass (all-tenant scheduled reports) |
| OutboxDispatcherBackgroundService | ✅ | system bypass (drains all-tenant outbox/inbox) |

Plus **`ServiceRunTracker`** (shared by all 8; writes `service_run_history` /
`service_heartbeats` / auto-incidents) is wrapped too — otherwise heartbeats would silently
stop under enforcement. New reusable helper: `Database.RunInSystemScopeAsync(...)` (no-op when
the flag is off; sets/clears the ambient scope from the caller's frame → no pool leak).

### 2b. SSE stream — DONE
`/api/telemetry/stream` must NOT hold one request-length transaction open for the connection's
whole lifetime. Fixed with a **per-tick tenant scope**: each 3-second position read runs inside
a short `Database.RunInTenantScopeAsync(companyId, …)` (opens scope → reads → commits →
releases). RLS filters each tick by the SST-derived `company_id`. No-op (fresh per-read
connection) when the flag is off. The middleware's SSE branch remains intentionally
un-transaction-wrapped.

### 2c. Startup role assertion — DONE
`AssertSchemaInitRoleAsync` runs before schema init in [Program.cs](backend-dotnet/Program.cs).
It checks `current_user`'s `rolsuper`/`rolbypassrls`: schema DDL/seeding must run as the DB
**owner**. If connected as a `NOSUPERUSER/NOBYPASSRLS` role (i.e. `opstrax_app`) **and** RLS
enforcement is on, it **throws and halts startup** with a clear message instead of emitting
dozens of confusing permission errors mid-bootstrap. (Warns rather than throws when the flag
is off, so nothing changes for the current dev/CI owner role.)

### 2d. Staging environment + smoke test — **NOT DONE (requires owner action)**
Standing up a staging environment configured like production (`opstrax_app` role,
`PG_CONNECTION` pointed at it, `Rls__EnforceTenantContext=true`) requires provisioning a
staging DB + host — account-level work outside this repo. **This is a required gate.** The
smoke test to run there, end-to-end under REAL enforcement:
1. Normal tenant user flows (login, list vehicles/drivers/jobs, dispatch board).
2. Background jobs (confirm heartbeats advance in `/api/ops/services` under `opstrax_app`).
3. SSE live map (`/api/telemetry/stream` streams positions for the tenant only).
4. Platform-admin cross-tenant views (tenants list, commercial-ops summary).
5. Demo-tenant seeder (runs as owner; confirm it seeds cleanly).
6. Customer portal (token-scoped public tracking).
Each must show correct isolation (a tenant sees only its own rows) with no 0-row "fail-closed"
regressions on legitimate reads.

### 2e. Flag flip — **NOT DONE, by design; DO NOT flip without 2d passing**
`Rls:EnforceTenantContext` remains **false**. Per instruction, it must not be flipped in this
session, and only after the 2d staging smoke test passes cleanly. The mechanism is proven
independently (207/207 tables enforced; pool-leakage test passes), but production activation is
an explicit, owner-gated step: create `opstrax_app` password → point runtime `PG_CONNECTION` at
it (keep a separate owner connection for migrations/init) → set `Rls__EnforceTenantContext=true`.

---

## STEP 3 — Deferred security gaps (fixed; defense-in-depth)

### 3a. `SimpleUpdateStatus` cross-tenant UPDATE — FIXED
Was `UPDATE {table} SET status=@s WHERE id=@id` — **no tenant filter**, so any tenant could
flip another tenant's row by ID. Now takes a `tenantColumn` and constrains the UPDATE to the
caller's tenant; a no-op update returns **404** (can't confirm/mutate another tenant's row):
- `sla_breaches`, `scheduled_reports` → scoped by `tenant_id`.
- `eld_devices` → RLS-forced but has **no tenant column** (policy scopes via a non-column
  mechanism); relies on RLS (documented at the call site).
- `compliance_violations`, `compliance_audit_packages` → **not tenant-owned** (global
  reference data keyed by `country_code`/`profile_id`, no RLS, no tenant column) → no filter
  applicable (documented).

### 3b. `customer-eta/recommendations` unscoped read — FIXED
`GET /api/customer-eta/recommendations` selected from `ai_recommendations` filtered only by
`module_key` — cross-tenant leak. Now filters `AND company_id=@cid` (from the authenticated
tenant). Fixed the identical bug in the sibling `GET /api/maintenance/recommendations` too.

### 3c. Build + test after each — DONE (final: 0 errors, 884/884).

---

## STEP 4 — Credential audit

### 4a. The commit-`36a97a0` credential — **flagged for owner confirmation**
At `36a97a0`, `api-dotnet/appsettings.json` contained
`Server=localhost;…User=opstrax_user;Password=***REMOVED-CREDENTIAL***` — a **localhost MySQL dev
credential**, not a production Neon/Postgres secret. It is **removed from the current tree**
(empty connection string + externalized). It is unlikely to have ever been a live production
secret. **HOWEVER**, I cannot confirm from inside the codebase whether any credential that was
ever committed was rotated at the provider. → **OWNER ACTION (see list): confirm no
still-live DB/service credential matches anything ever committed; rotate at the provider if in
doubt.** This is a hard gate per the instruction.

### 4b. Full git-history secret sweep — DONE
Swept all history for connection-string / password / API-key patterns:
- **Managed-DB hostnames** (`neon.tech`, `amazonaws`, `pooler`, `supabase`): only **placeholder
  examples** (`your-project.pooler.neon.tech`, `your-neon-password`) — no real host.
- **Passwords in history:** almost all are dev defaults / env-var placeholders / `CHANGE_ME`
  templates (`opstraxpass`, `rootpass`, `zayra_password`, `Admin@12345`, `password`).
- **Three real-looking demo-tenant login passwords once existed** — `***REMOVED-CREDENTIAL***`,
  `***REMOVED-CREDENTIAL***`, `***REMOVED-CREDENTIAL***` — introduced in `226b552` ("clean KSA demo tenant")
  and **since removed from the working tree**. These were **demo-tenant user login passwords**,
  not infrastructure secrets. Because they were committed, they should be treated as burned:
  → **OWNER ACTION: ensure no real/pilot account ever reused these values.**
- **No live production database credential or API key was found in current or historical code.**

---

## STEP 5 — Monitoring baseline

**What's real (in-app):**
- Liveness/readiness probes: `/health`, `/health/live`, `/ready`, `/health/ready` (DB ping →
  503 if DB down), `/health/deep` (DB latency + config validation). Render's
  `healthCheckPath: /health` gives platform-level up/down + auto-restart.
- Per-service observability: `ServiceRunTracker` writes `service_run_history` +
  `service_heartbeats`; surfaced via `/api/ops/services` and `/api/ops/metrics`.
- **Auto-incidents:** ≥3 consecutive background-service failures auto-create a platform
  incident (`/api/ops/incidents`), severity escalates at ≥10. (Preserved through the RLS
  scoping change.)
- Error sanitization: tracker scrubs connection strings / tokens / IPs before persisting.
- `/api/ops/config/check` validates required configuration.

**Gap (report honestly):**
- **No EXTERNAL alerting.** Incidents and heartbeat failures are recorded **in the database**
  only — nothing emails/pages/webhooks a human when production goes down or errors spike.
  Someone must actively watch the ops dashboard. Before a pilot: wire Render health-check
  alerts (dashboard) and/or an external uptime monitor (e.g. a pinger on `/health/deep`) and
  route auto-incidents to email/Slack/PagerDuty. **Recommended before go-live; owner/ops
  action** (no code required for the Render/uptime piece).

---

## STEP 6 — Data offboarding (delete on request) — DONE & PROVEN

The demo-seeder cleanup bug (a hand-maintained `DELETE` list that omitted child tables) is the
reason offboarding is now **schema-driven**, not list-driven:
- New [TenantOffboardingService](backend-dotnet/Services/TenantOffboardingService.cs): discovers
  **every** base table carrying `company_id`/`tenant_id` from `information_schema` (**211
  tables**, vs the demo list's ~30), deletes the tenant's rows in **iterative FK-safe passes**
  (each per-table delete in its own SAVEPOINT; FK-blocked rows fall to a later pass), verifies
  **zero residual rows**, then deletes the `companies` row — all in one transaction that **rolls
  back entirely** if any residual remains (never half-deletes a tenant).
- New platform endpoint `DELETE /api/platform/tenants/{id}` (perm `platform:tenants:manage`),
  requires an explicit `{"confirm":"<companyCode>"}` body so a tenant can't be purged by a
  stray DELETE; audits the offboarding to `platform_audit_log` (which survives the deletion).
- **Proven by real Postgres tests** (`TenantOffboardingPostgresTests`, +2):
  1. Seeds a realistic tenant **including the exact child tables the demo cleanup missed**
     (`driver_documents`, `vehicle_documents`, `user_sessions`, `customer_contacts`,
     `customer_addresses`), deletes it, asserts the company is gone **and zero rows remain in
     every one of the 211 tenant-scoped tables**.
  2. Asserts deleting one tenant leaves a neighbour tenant **fully intact** (no collateral).

**6b note / data-quality finding:** while clearing the stale `MERIDIAN-DEMO` demo tenant (left
in the shared local DB by the old buggy cleanup, which was failing the idempotency test), I
found the **old demo seeder wrote child rows with `company_id=1` while the parent driver
belonged to a different company** — genuine data corruption from that seeder, not a flaw in the
new cascade. The new service correctly refuses to half-delete such mismatched data; the stale
row was cleaned (data-only) and the demo test now passes. **Recommend auditing the demo seeder's
`company_id` assignment** (out of scope to change here) so it doesn't recreate mislabeled rows.

---

## Items requiring Zack's action OUTSIDE the codebase (go-live blockers)

| # | Item | Why it's a gate | Who / where |
|---|---|---|---|
| A | **Confirm credential rotation at the provider** — verify no still-live DB/service credential matches anything ever committed (esp. any legacy MySQL creds and the removed `@2026!` demo passwords); rotate if in doubt. | STEP 4a — cannot be verified from code; hard gate. | Zack — Neon / service dashboards |
| B | **Provision a dedicated production Neon (paid tier), separate + backed up** from dev; create the `opstrax_app` role there and set its password. | STEP 1a / 2e — real pilot data needs a real, isolated, backed-up DB. | Zack — Neon dashboard |
| C | **Stand up staging (opstrax_app role, `Rls__EnforceTenantContext=true`) and run the STEP 2d smoke test**; only then flip the flag in production. | STEP 2d/2e — RLS enforcement must be validated end-to-end before real data. | Zack + ops |
| D | **Wire external alerting** (Render health alerts + uptime monitor on `/health/deep`; route auto-incidents to email/Slack/PagerDuty). | STEP 5 — today nobody is notified if prod goes down. | Zack / ops (no code needed for the Render/uptime piece) |
| E | **Signed pilot data agreement** (export + delete-on-request). Delete-on-request is now provably implemented (STEP 6); the agreement itself is a business artifact. | Business/legal gate for real customer data. | Zack |
| F | *(Nice-to-have, non-blocking)* `git rm --cached api-dotnet/appsettings.json`; audit demo-seeder `company_id` assignment. | STEP 1b / 6b hygiene. | Dev |

---

## Files changed this session (backend + tests only; no UI, no features)

- `backend-dotnet/Data/Database.cs` — `RlsEnforced` flag + `RunInSystemScopeAsync` /
  `RunInTenantScopeAsync` scope helpers.
- `backend-dotnet/Program.cs` — `AssertSchemaInitRoleAsync` startup guard + DI registration
  for `TenantOffboardingService`.
- `backend-dotnet/Services/ServiceRunTracker.cs` — tracker DB writes wrapped in system scope.
- 8 background services — per-tick DB work wrapped in system scope.
- `backend-dotnet/Controllers/EndpointMappings.cs` — SSE per-tick tenant scope;
  `SimpleUpdateStatus` tenant-scoped; customer-eta + maintenance recommendations tenant-scoped.
- `backend-dotnet/Services/TenantOffboardingService.cs` **(new)** — schema-driven cascade delete.
- `backend-dotnet/Controllers/PlatformEndpoints.cs` — `DELETE /api/platform/tenants/{id}`.
- `backend-dotnet.Tests/TenantOffboardingPostgresTests.cs` **(new, +2 tests)**.

No RLS policy weakened, no test disabled/skipped/altered. All wrappers are safe-by-default
(no-ops until `Rls:EnforceTenantContext=true`).
