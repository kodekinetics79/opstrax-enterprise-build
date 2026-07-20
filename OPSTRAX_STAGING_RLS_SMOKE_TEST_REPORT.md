# OpsTrax — Staging RLS Smoke Test Report

**Session goal:** stand up and smoke-test staging RLS enforcement (`opstrax_app` role,
`PG_CONNECTION` at staging, `Rls:EnforceTenantContext=true`) — the last gate from
`OPSTRAX_PRODUCTION_PILOT_READINESS_REPORT.md`.

- Remote confirmed: `origin → github.com/kodekinetics79/opstrax-enterprise-build.git`
  (the `zayra` remote is the off-limits sibling; untouched).

---

## ⛔ RESULT: BLOCKED — prerequisite not met. Smoke test NOT run.

Per the session's own guard: *"If [a staging Postgres with the `opstrax_app` role] is not
available, report exactly what's needed and stop — do not simulate this against
`opstrax_local` as if it were staging."*

That condition is triggered. **No staging environment exists, and the flag was not flipped
anywhere.** Nothing was simulated against the local dev DB.

---

## Evidence — why this is blocked (not a judgment call)

| Check | Finding |
|---|---|
| A separate **staging** Postgres host configured anywhere | **None.** No `.env.staging`, no staging connection string, no `STAGING`/`PG_CONNECTION` env var set in this shell, no staging/managed-DB hostname anywhere in the repo (only prose references to the *concept* of staging in docs). |
| What `PG_CONNECTION` (`.env`) actually points at | The **local dev DB**: `Host=host.docker.internal;Port=5433;Database=opstrax_local;Username=zayra;SSL Mode=Disable`. This is the same database the test suite uses. |
| Role in that connection string | **`zayra`** — the owner/superuser (BYPASSRLS). This is the exact role that *cannot* observe RLS enforcement. It is **not** `opstrax_app`. |
| Is `opstrax_app` even the runtime role here | No — the configured runtime user is the superuser owner. |
| Is any Postgres reachable right now | **No.** Ports 5432 and 5433 are both CLOSED (connection refused). Even the local dev DB is currently down. |
| `Rls:EnforceTenantContext` current value | Not present in `appsettings.json` → **defaults to `false`** (safe-by-default, as designed). |

Running the smoke test against this configuration would prove nothing: as the `zayra`
superuser with the flag off, every query bypasses RLS by definition. A "pass" there would be
a false pass — precisely the outcome the prerequisite guard exists to prevent.

**No changes were made this session.** The flag was not set in any environment; `.env` was not
repointed; no application was launched against a mislabeled DB.

---

## Smoke-test areas — status

All deferred to a real staging run. Not executed, because executing them against
`opstrax_local`/`zayra`/flag-off would be a simulation, not a test.

| Area | Status |
|---|---|
| Normal authenticated user flows (all major modules) | ⏸️ Not run — no staging env |
| 8 background services under enforcement | ⏸️ Not run — no staging env |
| SSE stream (`/api/telemetry/stream`) | ⏸️ Not run — no staging env |
| Platform-admin cross-tenant views (bypass scope) | ⏸️ Not run — no staging env |
| Demo tenant seeder (as owner) | ⏸️ Not run — no staging env |
| Customer portal (token-scoped public tracking) | ⏸️ Not run — no staging env |
| `TenantOffboardingService` delete path | ⏸️ Not run — no staging env |

---

## FINAL GO / NO-GO on flipping the flag in production

### 🔴 NO-GO.

The production flag flip (`Rls:EnforceTenantContext=true`) remains gated on a clean staging
smoke test, and that smoke test **has not been performed** because staging does not yet exist.
Do **not** flip the flag in production. (It was also not flipped in staging — there is no
staging — nor anywhere else.)

---

## What Zack needs to provide before this session can proceed (exact prerequisites)

The blocker is entirely infrastructure provisioning — account-level work outside the codebase.
To unblock a real staging smoke test, all of the following must be true:

1. **A staging Postgres instance exists, separate from local dev and from production.**
   A dedicated Neon branch/project (or an equivalent Postgres) that mirrors production shape.
   It must NOT be `opstrax_local`.

2. **Schema + policies applied to it, as the owner.** Run all schema migrations/seeders against
   staging using the **owner** role (opstrax_app has no DDL), including
   `database/migrations/2026_06_30_stage19_row_level_security.sql` and
   `database/migrations/2026_06_30_stage20_rls_force_and_app_role.sql` (which creates the
   `opstrax_app` role and FORCEs RLS on the 207 tables).

3. **The `opstrax_app` role has a password set on staging, out-of-band:**
   `ALTER ROLE opstrax_app WITH PASSWORD '<staging-secret>';`
   (The password is intentionally never in git.)

4. **A staging runtime connection string using the `opstrax_app` role**, with SSL appropriate
   to the host, provided to this session (e.g. an `.env.staging` or an exported
   `PG_CONNECTION`), of the form:
   `Host=<staging-host>;Port=5432;Database=<staging-db>;Username=opstrax_app;Password=<secret>;SSL Mode=Require`
   — and a **separate owner connection string** for running migrations/seeders and the demo
   seeder (schema-init asserts and will halt if it detects the restricted role — by design).

5. **The staging instance must be reachable** from where this session runs (currently no
   Postgres is listening on 5432/5433 at all).

Once 1–5 are in place, re-run this session: it will point `PG_CONNECTION` at staging with
`opstrax_app`, set `Rls:EnforceTenantContext=true`, launch the full app, and execute every
smoke-test area above with explicit per-area pass/fail and a production go/no-go.

### Note on confirming enforcement is *real*
When staging is available, the first assertion this session will make (before any functional
flow) is that enforcement is genuinely active — i.e. `current_user` = `opstrax_app` with
`rolsuper=false`/`rolbypassrls=false`, and a no-context query on a tenant table returns 0 rows
(fail-closed). Only after that baseline holds are the functional areas meaningful — otherwise a
"pass" could be a silent bypass.
