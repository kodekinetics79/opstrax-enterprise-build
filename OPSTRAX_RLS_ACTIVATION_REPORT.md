# OpsTrax — RLS Activation Report (Option A1)

Session scope: activate the dormant Stage-19 RLS policies into ACTIVE enforcement
using Option A1 (request-scoped transaction + `set_config('app.current_tenant_id',
cid, true)` — SET LOCAL semantics). Backend-only. UI/UX Phase 1 files untouched.

- Remote confirmed: `origin → github.com/kodekinetics79/opstrax-enterprise-build.git` (the `zayra` remote is the off-limits sibling; not touched).
- Files changed / added this session (only these):
  - `database/migrations/2026_06_30_stage20_rls_force_and_app_role.sql` **(new)** — role + FORCE
  - `backend-dotnet/Data/Database.cs` — tenant/system scope mechanism (`+214/−…`)
  - `backend-dotnet/Program.cs` — flag-gated request-scope wiring (`+68/−…`)
  - `backend-dotnet.Tests/RlsTenantIsolationPostgresTests.cs` **(new)** — pool-leakage test
  - No UI/UX or other prior-session files modified.

---

## Before / after enforcement status

| | Before this session | After |
|---|---|---|
| RLS policies present (Stage-19) | 207 tables | 207 tables |
| Tables FORCE'd | **0** | **207** |
| Restricted, non-BYPASSRLS app role | none | **`opstrax_app`** (DML-only, no DDL/ownership) |
| Per-request tenant GUC mechanism | none | `Database.BeginTenantScopeAsync` (SET LOCAL) |
| **Tables with ACTIVE enforcement** (RLS + FORCE + both policies) | **0** | **207 / 207** |
| Cross-tenant read as restricted role, no context | (n/a — dormant) | **0 rows (fail-closed)** — proven |
| Platform-admin cross-tenant access | (n/a) | **works via `platform_admin_bypass`** — proven |

"Active enforcement" = a query by the non-superuser `opstrax_app` role is filtered
by RLS. Verified directly (see below). The current local/CI role `zayra` is a
superuser+BYPASSRLS and continues to bypass — which is why the existing suite is
unaffected.

---

## Step 1 — Migration: restricted role + FORCE (`…stage20…`)

- Creates `opstrax_app` `LOGIN NOSUPERUSER NOBYPASSRLS NOCREATEDB NOCREATEROLE`
  (idempotent). Grants **DML only** (`SELECT/INSERT/UPDATE/DELETE`) + sequence
  usage + default privileges — no DDL, not an owner, so RLS always applies to it.
- `FORCE ROW LEVEL SECURITY` on all 207 RLS-enabled tables (loop over
  `rowsecurity=true`). Additive, idempotent, documented rollback.
- Password intentionally **not** in git (set per-env via `ALTER ROLE … PASSWORD`).
- Applied locally; verified **207 forced**, role attributes `super=f bypassrls=f`.

Direct enforcement proof (psql, as `opstrax_app`):
- no GUC → `SELECT count(*) FROM dvir_reports` = **0** (owner `zayra` sees 25).
- `set_config('app.current_tenant_id','1',true)` → **25**; `='2'` → **0**.
- after `COMMIT`, the GUC does **not** persist on the connection → **0** (no pool leak).
- `set_config('app.platform_admin','on',true)` → **25** (bypass policy).
- `INSERT` with a `company_id` ≠ GUC → **blocked by WITH CHECK**.

---

## Step 2 — `Database.cs`: request-scoped transaction + `set_config`

- `BeginTenantScopeAsync(companyId)` opens a connection + transaction and runs
  `SELECT set_config('app.current_tenant_id', @cid, true)` as the **first**
  statement (transaction-local → cannot leak across the pool). Returns a
  `TenantScope` (`CompleteAsync` = commit; `DisposeAsync` = rollback-if-not-completed
  + release connection).
- `BeginSystemScopeAsync()` sets the **separate** `app.platform_admin='on'` GUC for
  the `platform_admin_bypass` policy (never sets a tenant id).
- A `TenantScopeAccessor` (singleton, `AsyncLocal`) carries the ambient scope; all
  query methods route through `AcquireAsync` — ambient scope's connection+transaction
  when present, otherwise a fresh per-query connection (**unchanged pre-RLS behaviour**).
- `WithTransactionAsync` joins the ambient request transaction when one exists
  (Postgres has no nested transactions); otherwise behaves as before.
- The `TenantScopeAccessor` ctor param is optional, so `new Database(config)`
  (tests, schema services) still works with no ambient scope.

## Step 3 — No-tenant paths use `platform_admin_bypass`

Wired in `Program.cs` (flag-gated). When enforcement is on:
- **Pre-tenant auth bootstrap** (session lookup on `user_sessions`/`users`, and the
  `tenant_entitlements` module check — all RLS tables) runs via `BootstrapReadAsync`
  under a **system bypass** scope, so auth succeeds before tenant context exists.
- **Public / platform / device-auth / token-tracking** allowlist paths run their
  handler via `InvokeUnderBypassAsync` (system scope) so they can reach RLS tables.
- **Authenticated handlers** run inside a **tenant** scope (`BeginTenantScopeAsync`),
  committed after `next()`.
- These never "silently fail" RLS: bootstrap/public/platform use the explicit
  bypass GUC; tenant handlers use the tenant GUC.

## Step 4 — Existing Postgres fixtures

The ~10 existing Postgres fixtures connect as `zayra` (the DB **owner/superuser**),
which legitimately bypasses RLS — appropriate for cross-tenant test **setup/teardown**
(they seed data across companies). Under FORCE they still bypass, so **none failed**,
and the instruction's condition ("if a test fails because it depended on missing
isolation") did not arise. **No policy was weakened and no fixture was altered.**
Enforcement is instead validated by the dedicated restricted-role fixture (Step 5),
which is the correct way to observe RLS.

## Step 5 — Two-tenant concurrent pool-leakage test

`RlsTenantIsolationPostgresTests` (runs as `opstrax_app`; owner seeds/cleans):
- 25× interleaved concurrent `Task.Run` requests as Tenant A and Tenant B sharing
  the connection pool. Each asserts it sees **only its own** marker row and
  **never** the other tenant's — **zero cross-tenant visibility in either direction**.
- Asserts `platform_admin` bypass sees **both** tenants (platform-admin path).
- Asserts a no-ambient-scope query returns **0** (fail-closed + proves the SET LOCAL
  GUC did not leak onto a reused pooled connection).
- **Result: PASS.**

---

## Step 6 — Verification

- `dotnet build` → **Build succeeded, 0 warnings, 0 errors**.
- `dotnet test` (full) → **Passed! Failed: 0, Passed: 863, Skipped: 0**
  (862 prior + the new pool-leakage test). No test disabled/skipped/weakened.
- **Active-enforcement count: 207 / 207** tenant tables (RLS + FORCE + both
  policies), confirmed enforced by the passing pool-leakage test.
- Platform-admin cross-tenant access: **tested specifically** (the `BeginSystemScopeAsync`
  branch in the pool-leakage test) and passes — not assumed.

---

## Safe-by-default gating & production activation

The request-pipeline wiring is gated on config `Rls:EnforceTenantContext`
(**default false**). With it false the pipeline behaves exactly as before (no
per-request transaction, no GUC) — which is why the local app and the 863 tests are
unaffected. The DB-level mechanism + policies are proven independently by the
restricted-role test.

**To activate enforcement in an environment (owner action):**
1. Set the `opstrax_app` password there: `ALTER ROLE opstrax_app WITH PASSWORD '<secret>'`.
2. Point `PG_CONNECTION` at `opstrax_app` (restricted role) — NOT the superuser/owner.
3. Set `Rls__EnforceTenantContext=true`.
4. Run schema migrations/seeders as the **owner** (opstrax_app has no DDL) — keep a
   separate owner connection for `dotnet`/init; runtime uses `opstrax_app`.

**Explicitly flagged as remaining activation work (not wired this session):**
- **Background services** (8: Outbox/Escalation/Maintenance/Safety/Telemetry×2/Trip/
  ScheduledReport) run outside the HTTP pipeline; each needs a `BeginSystemScopeAsync`
  (or per-tenant scope) wrapper before it will function as `opstrax_app`.
- **SSE stream** (`/api/telemetry/stream`) must NOT be wrapped in a request-length
  transaction; it needs a per-read scoping strategy. Its `next()` is intentionally
  left unwrapped and flagged.
- **Startup schema-init** (`Batch*SchemaService.EnsureAsync`) does DDL/seeding and
  must run as the owner, not `opstrax_app`.
- These are why a full end-to-end "app runs entirely as `opstrax_app`" cannot be
  validated in this environment (local/CI run as the superuser owner; there are no
  HTTP-pipeline integration tests). They should be completed + smoke-tested in staging.

---

## Compliance statement

- Additive migration only; no data touched; documented rollback. **No destructive
  migration.**
- **No RLS policy was weakened** and **no test was disabled, skipped, or altered** to
  pass — 863/863, and the existing fixtures were left exactly as they were.
- Platform-admin cross-tenant access verified via its own `platform_admin_bypass`
  policy (tested, not assumed).
- The DB-level enforcement + the connection mechanism are proven; the live request
  wiring is implemented, safe-by-default, and flagged for staging activation.
