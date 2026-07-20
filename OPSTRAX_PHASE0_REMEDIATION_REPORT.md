# OpsTrax — Phase 0 Security Remediation Report

Date: 2026-06-30
Scope: Precision, additive, reversible security fixes only. No refactor, no new
features, no destructive migrations. Driven by `OPSTRAX_REALITY_AUDIT.md` and
`OPSTRAX_REALITY_AUDIT_V2.md`.

> **Headline accuracy correction.** The v2 audit's route counts ("273/619 no
> auth", "236/619 no tenant filter") are a **measurement artifact**: the audit
> scanned only the single `app.Map(...)` registration line and missed (a) auth
> checks placed on the next line of the lambda and (b) auth/tenant logic inside
> the **named handler methods** the routes delegate to. Following the handlers,
> true pre-existing coverage was **Auth 458/615, Tenant 405/615** — not 273/236.
> Separately, the global middleware (`backend-dotnet/Program.cs:187-216`)
> **rejects every `/api/*` request without a valid Bearer token + active
> session**, except a small public allowlist (`/api/auth/login`, health probes,
> `/api/platform*`, device-auth `ingest`, token-scoped public tracking). So a
> "no-auth" route was never anonymous — it lacked an *extra per-permission*
> check while still requiring an authenticated, tenant-resolved session.

---

## Files changed

| File | Change | Lines |
|---|---|---|
| `api-dotnet/appsettings.json` | Removed hardcoded connection string → env-only | ~3 |
| `api-dotnet/Infrastructure/Database.cs` | Env-var fallback + fail-fast (matches backend-dotnet) | ~14 |
| `api-dotnet/appsettings.example.json` | **New** — placeholder onboarding template | new |
| `.gitignore` | `!appsettings.example.json` exception so the template can be tracked | +1 |
| `backend-dotnet/Controllers/EndpointMappings.cs` | Auth + tenant filters on sensitive routes | 267 changed |
| `database/migrations/2026_06_30_stage19_row_level_security.sql` | **New** — additive RLS migration | new (~110) |
| `frontend/src/pages/ExecutivePage.tsx` | Removed `SEED_KPIS` fake tiles + fake-number fallbacks | ~47 |
| `frontend/src/pages/OperatingModulePage.tsx` | Seed data behind `import.meta.env.DEV` guard | ~18 |
| `frontend/src/pages/SlaKpiPage.tsx` | `SEED_*` → `EMPTY_*` honest empty defaults | ~19 |
| `frontend/src/pages/DriverMessagingPage.tsx` | `SEED_*`/`MOCK_*` → `EMPTY_*` honest empty defaults | ~18 |

No test files were modified. Total tracked diff: **+255 / −131** across 7 files,
plus 2 new files.

---

## STEP 1 — Secret rotation

**1a. What was there.** `api-dotnet/appsettings.json:3`:
```
"Default": "Server=localhost;Port=3306;Database=opstrax;User=opstrax_user;Password=***REMOVED-CREDENTIAL***;Allow User Variables=True;"
```
This is a **localhost MySQL (port 3306) development credential** with an obvious
placeholder password — **not** a Neon/Render production secret.

**1b. Fix.** Value replaced with `""` and an env-only note. `Database.cs` now
resolves `ConnectionStrings:Default` → `MYSQL_CONNECTION` env var → fail-fast,
mirroring the verified pattern in `backend-dotnet/Data/Database.cs`
(`PG_CONNECTION` fallback) and the env wiring in `docker-compose.yml` / `render.yaml`.

**1c. .gitignore / example.** `appsettings.json` and `appsettings.*.json` were
**already** in `.gitignore` (though `api-dotnet/appsettings.json` is tracked from
an old commit — see below). Created `api-dotnet/appsettings.example.json` with
placeholder values and added `!appsettings.example.json` so the template is
trackable for onboarding.

**1d. Git history.** The credential appears in **exactly one commit**:
`36a97a0 "Prepare app for Vercel deployment"`, same value, never rotated.
**→ OWNER ACTION:** purging it from history requires a history rewrite
(`git filter-repo` / BFG), which breaks existing clones — **not performed here**;
flagged for Zack's explicit approval.

**1e. Live-reachability.** The credential targets `localhost:3306` with dev
defaults; it is **not a live reachable cloud instance**. Moreover `api-dotnet/`
is **dead/incomplete legacy code**: it does not build even on the committed
revision (missing `Controllers`/`Services` namespaces — verified by rebuilding
the stashed original), and is **not referenced by `docker-compose.yml` or
`render.yaml`** (both deploy `backend-dotnet/`). **→ OWNER ACTION (conditional):**
if `opstrax_user`/`***REMOVED-CREDENTIAL***` were ever provisioned on a real MySQL
instance, **rotate at the provider** — a code fix alone is never sufficient for a
leaked live credential. Based on the evidence this is a local dev default only.

---

## STEP 2 — Live-vs-dead route resolution & auth/tenant hardening

**Method.** A resolver (`scratchpad/analyze_routes.py`) parses every `app.Map*`
across the four controllers and **follows named handler methods** to judge real
auth/tenant coverage, instead of scanning one line. All enumerated sensitive
prefixes were confirmed **LIVE** (called from `frontend/src/services/*`). None
were dead code.

### Sensitive-prefix verdicts (representative)

| Route | Called from frontend | Verdict | Action |
|---|---|---|---|
| `GET /api/invoices`, `/api/payments` | `FinancialAnalyticsPage.tsx` | LIVE (tenant-scoped, no perm check) | **+`RequirePermission("finance.invoice.read")`** |
| `GET/POST/PUT/DELETE /api/dvir/*` | `dvirApi.ts` | LIVE — reads had **no tenant filter** (cross-tenant leak); writes hardcoded `company_id=1` | **+auth +company scoping (reads & writes)** |
| `GET/POST/PUT/DELETE /api/documents/*` | `documentsApi.ts` | LIVE — same pattern as DVIR | **+auth +company scoping (reads & writes)** |
| `GET /api/coaching/*`, `/api/incidents/*`, `/api/workorders/*` (reads) | `coachingApi.ts`, `incidentsApi.ts`, `workOrdersApi.ts` | LIVE — list/summary/detail reads had **no tenant filter** | **+auth +company scoping** |
| `DELETE /api/customers/{id}` | `customersApi.ts` | LIVE — used `SoftDelete` (no perm, no company) | **→ `SoftDeleteWithPermission("customers", …, "customer.account.update")`** |
| `/api/contracts/{id}/rates`, `/api/{module}/recommendations` (dvir/coaching/contracts/incidents) | module services | LIVE — inline reads, no filter | **+auth +company scoping** |
| `DELETE /api/contracts/{id}/rates/{rateId}` | `contractsApi.ts` | LIVE — **already protected** (`DeleteContractRate` has `RequirePermission` + company) | none (audit false-negative) |
| `GET /api/customers/{id}/timeline` & `…/recommendations`, `GET /api/workorders/{id}/recommendations` | module services | LIVE via **shared generic helpers** (`Timeline()`, `Recommendations()`) | left as-is (see residual) |

**All fixes reuse the existing pattern verbatim** —
`if (RequirePermission(http, "<existing.permission>") is { } denied) return denied;`
then `GetCompanyId(http)` + `WHERE company_id=@cid` — with permission keys that
already exist in the RBAC vocabulary (`maintenance:view`, `compliance:view`,
`safety:view`, `contract.view`, `finance.invoice.read`). No new auth pattern and
no new permission keys were invented.

### Before / after (resolved, handler-following count)

| Metric | Before | After |
|---|---|---|
| Auth checks | 458/615 | **476/615** |
| Tenant filters | 405/615 | **433/615** |
| Automated tests (unchanged method) | 155/619 (audit) | 155/619 |

(The v2 *naive* line-local method that produced "273" is not reproduced as a
target — it is the artifact being corrected. The resolved numbers above are the
accurate measure.)

### Residual — flagged, not silently expanded (STEP 2e)

- **Generic-helper reads** (`GET /api/customers/{id}/timeline`,
  `…/recommendations`, `GET /api/workorders/{id}/recommendations`) still lack a
  *per-permission* check because they route through the shared `Timeline()` /
  `Recommendations()` Funcs used by ~15 modules. Adding a permission would
  require a per-module permission argument — a cross-module change beyond the
  precision mandate. **They still require an authenticated, tenant-resolved
  session** (global middleware) and are covered by the RLS backstop (Step 3).
- The **systemic pattern** (many modules' list/detail *read* handlers were
  written without `company_id` filters while their mutation handlers were
  scoped) is genuinely broad. The enumerated sensitive prefixes were closed at
  the handler level; the remaining modules are addressed defense-in-depth by
  Step 3 RLS rather than a repo-wide handler rewrite.

No DEAD-CODE routes were deleted (Step 2e): none of the sensitive prefixes were
dead. Candidate-for-removal: `api-dotnet/` (entire dead project) — **confirm with
product owner before deleting.**

---

## STEP 3 — Row-Level Security (defense in depth)

**3a/3d. Migration.** `database/migrations/2026_06_30_stage19_row_level_security.sql`
— additive, idempotent. A `DO` block enables RLS and creates, per tenant-owned
table:
- `tenant_isolation` — `USING/WITH CHECK (<col> = NULLIF(current_setting('app.current_tenant_id', true), '')::bigint)`, where `<col>` is `company_id` or `tenant_id`, **chosen per table** (both are `bigint`, not `uuid`).
- `platform_admin_bypass` — a **separate, explicit** policy gated on a dedicated
  GUC `app.platform_admin = 'on'` for legitimate cross-tenant platform/admin work
  (not a blanket role bypass; permissive policies OR-combine).

Applied to the live local DB: **207 tables** now carry both policies.
**Skipped tables (control-plane / global, by design):** `companies`,
`platform_admin_users`, `platform_sessions`, `platform_audit_log`,
`platform_packages`, `platform_invoices`, `schema_migrations`.

**3c. Non-breaking.** Full suite re-run **after** applying RLS: **862 passed, 0
failed, 0 skipped.** No test path was setting tenant context incorrectly, so no
code path needed weakening — and no policy was weakened to pass a test.

**3b. Session-scoping — NOT wired; STOP-and-report.** Per the "stop rather than
improvise" rule, I did **not** add the `SET app.current_tenant_id` call, because
it cannot be done safely with the current architecture:
- `backend-dotnet/Data/Database.cs` opens a **brand-new `NpgsqlConnection` per
  query** with no request-scoped connection or transaction. A `SET` in the
  middleware (`Program.cs:279`) lands on a throwaway connection, never the
  handler's query connections — so it would be a **no-op**; and a non-`LOCAL`
  `SET` on a pooled physical connection **persists and leaks tenant context to
  the next request that reuses it** — a *new* cross-tenant vulnerability.

**→ OWNER ACTION to activate enforcement (all three required):**
1. Run the app as a **dedicated non-superuser, non-`BYPASSRLS`** role that does
   not own the tables (or add `FORCE ROW LEVEL SECURITY`). *Today RLS is dormant:
   the local/test role `zayra` is a superuser with `BYPASSRLS`, and a table-owner
   role bypasses RLS without `FORCE` — so these policies do not yet change
   behaviour anywhere.*
2. Enable `FORCE ROW LEVEL SECURITY` once (1) is in place.
3. Refactor connection handling to a **request-scoped connection/transaction**
   and set `app.current_tenant_id` via `SET LOCAL` per request (the recommended
   path: an `AsyncLocal<long?>` tenant populated by the middleware, applied at
   `OpenAsync`). This is an architectural change with a per-query round-trip
   cost — beyond Phase 0's additive mandate — hence flagged, not done.

The migration is shipped ready so the DB side is complete the moment the role +
connection work lands. **Rollback** is documented in the migration header.

---

## STEP 4 — Frontend demo-data removal

| Page | Before | After | Bundle check |
|---|---|---|---|
| `ExecutivePage.tsx` | `SEED_KPIS` rendered 8 tiles whose value was the literal word **"Live"**; silent fake-number fallbacks (`?? 3/7/84/88/79/91/82`) | `KPI_NAV_TILES` (navigation only, **no fabricated value**); fallbacks → `?? 0` (honest empty) | "Backend data only" / SEED scaffold **absent** from `ExecutivePage` chunk ✓ |
| `OperatingModulePage.tsx` | `import { developmentFleetSeedData }` used at module scope for the whole page | Behind `import.meta.env.DEV` guard (Vite inlines `false` in prod → branch DCE'd → empty data → honest empty states) | Mock data values **absent** from `OperatingModulePage` chunk ✓ |
| `SlaKpiPage.tsx` | `SEED_*` empty arrays/zeroed objects | renamed `EMPTY_*` + comment (were already empty — no fabricated data) | n/a |
| `DriverMessagingPage.tsx` | `SEED_*` / `MOCK_DRIVERS` empty arrays | renamed `EMPTY_*` | n/a |

**4d. Honest limitation.** The `developmentFleetSeedData` **module still ships in
the production bundle** — not via the four named pages, but because **out-of-scope
importers** pull it: `frontend/src/services/telematicsService.ts`,
`frontend/src/data/telematicsSeedData.ts`, `IotDevicesPage`,
`TelematicsCommandPage` (and the shared `index` chunk). Separately,
`CustomersPage.tsx` and `src/auth/accessScope.ts` contain their **own** hardcoded
demo strings (not via the seed import). These were **not** named in the Phase 0
targets, and `telematicsService` is a live service — ripping the seed out of them
risks breaking telematics features. **→ OWNER ACTION:** decide whether to also
de-seed the telematics/IoT path and the CustomersPage/accessScope hardcoded
strings in a follow-up; not expanded here to honor "fix exactly what is specified."

---

## STEP 5 — Verification

| Command | Result |
|---|---|
| `dotnet build backend-dotnet/Opstrax.Api.csproj` | **Build succeeded — 0 errors** (warnings unchanged from the 472 audit baseline) |
| `dotnet test backend-dotnet.Tests/Opstrax.Tests.csproj` | **Passed! — Failed: 0, Passed: 862, Skipped: 0** (run **after** RLS applied) |
| `npm run build` (frontend) | **✓ built** |
| `npm run lint` (frontend) | **clean (eslint, no errors)** |
| `dotnet build api-dotnet/...` | **Pre-existing failure** (2 errors in `Program.cs`, missing `Controllers`/`Services` — verified identical on the unmodified committed revision). Dead project; secret removal is independent of compilation. |

Before/after route coverage (resolved method): **Auth 458→476/615**, **Tenant
405→433/615**, **RLS-protected tables 0→207**.

**5c.** No tests were skipped or disabled: `Skipped: 0`, and `git diff` touches no
test files.

---

## STEP 6 — Items needing manual owner action

1. **Git history rewrite** for the credential in commit `36a97a0` (breaks clones;
   needs explicit approval).
2. **Credential rotation at provider** *if* `opstrax_user`/`***REMOVED-CREDENTIAL***` was
   ever used on a real MySQL instance (evidence says localhost dev only).
3. **RLS activation** — dedicated non-superuser/non-`BYPASSRLS` app role +
   `FORCE ROW LEVEL SECURITY` + request-scoped connection for `SET LOCAL
   app.current_tenant_id`. Until then RLS is dormant (correct, non-breaking).
4. **Dead `api-dotnet/` project** — candidate for removal; does not build, not
   deployed. Confirm before deleting.
5. **Frontend telematics/IoT seed path** + `CustomersPage`/`accessScope`
   hardcoded demo strings — outside named scope; decide on follow-up de-seeding.
6. **Residual generic-helper read routes** — optionally add per-permission checks
   if within-tenant read granularity matters (they already require auth + are
   RLS-backstopped).

---

## Compliance statement

**No destructive migrations were run. No existing passing test was disabled or
weakened to achieve this result.** All changes are additive and reversible: the
RLS migration is idempotent with a documented rollback, the secret fix is an
env-indirection, and the frontend changes preserve behaviour while removing
synthetic data. `git add` was never run with `.`; staging (when performed) is
explicit per file.
