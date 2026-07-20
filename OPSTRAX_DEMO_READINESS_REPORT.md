# OpsTrax — Demo Readiness Report (Visual / Rendering Conformance Sweep)

**Session scope:** Visual + demo-readiness pass against the seeded **"Meridian Logistics — Demo"** tenant.
Remote confirmed: `origin → github.com/kodekinetics79/opstrax-enterprise-build.git` (the `zayra`
remote is the off-limits sibling — **not touched**). **RLS-staging work was not touched** (no edits to
Program.cs, the RLS middleware, or the stage19/20/21 migrations this session).

**Method:** app run locally (backend `:8088` against `opstrax_local`, frontend Vite `:5173`),
real browser walkthrough via headless Chromium (Playwright) capturing full-page screenshots,
per-page console errors, and network failures. Logins: `admin@meridian.demo` (internal, now 34
permissions) and `portal@acme.demo` (customer portal), password `MeridianDemo!23`.

## Verdict: ✅ **GO** (with 2 documented, non-blocking data-wiring notes)

Every one of the 16 page loads walked (13 admin pages + portal, across two logins) renders with
**0 console errors and 0 network failures**. All backend 500s surfaced during the walkthrough were
root-caused and fixed. Build + tests are green. The two remaining items are **data-wiring
observations** (a page reads a different table than the seeder populated, and one KPI has no UI
surface) — not rendering defects, and they do not block a demo of the migrated modules.

---

## STEP 1 — Walkthrough findings (before fixes)

The first-pass walkthrough surfaced **one hard blocker and several backend 500s** — no cosmetic
defects (no truncation / overflow / badge-wrapping / misalignment was observed on any page). The
`net::ERR_ABORTED` entries for `*.css` / `*.ts` / `/src/*` are Vite dev-server HMR/optimize aborts
(benign, they re-fetch) and were filtered out as non-defects.

| # | Page(s) | Symptom | Root cause | Severity |
|---|---|---|---|---|
| 0 | **ALL (login)** | Every login → HTTP 500 | `AuditService` login-audit insert did `COALESCE(@details, jsonb_build_object(...))` with `@details` bound as **text** → Postgres `42804` (text/jsonb mismatch); login also passed a **non-JSON** details string | 🔴 **P0 — nobody can log in** |
| 1 | Alerts | `GET /api/alerts` → 500 | `AlertsSql` `LEFT JOIN users` + the endpoint appended `WHERE company_id=…`; **both tables have `company_id`** → `42702` ambiguous column | 🔴 Page-blocking (product bug) |
| 2 | Alert Rules | `GET /api/alert-rules` → 500 | `alert_rules` table has **no `CREATE` anywhere**; only `ALTER … ADD COLUMN` assumed it existed → `42P01` | 🟠 Page-blocking (schema gap) |
| 3 | Dispatch | `GET /api/dispatch/available-drivers` → 500 | `hos_records` table has **no `CREATE` anywhere** → `42P01`; after adding it, an aggregate bug surfaced: `MAX(...) … ORDER BY shift_date` without `GROUP BY` → `42803` | 🟠 Panel-blocking (schema + SQL bug) |
| 4 | Trips | `GET /api/trips/{id}/breadcrumbs` → 500 | trip-replay query selects `accuracy_meters` from `location_events`, but a `location_events` created by an older path lacks that column (no idempotent backfill) → `42703` | 🟠 Trip-replay-blocking (schema drift) |
| 5 | Finance / AR | `/invoices`, `/cost-leakage` → **"Permission Denied"** | demo admin holds granular `finance.*` perms; the frontend Finance **routes** gate on the coarse legacy `finance:view`, which the seeder didn't grant | 🟠 Demo AR walkthrough blocked |
| 6 | Finance / Invoices | after #5 fixed, `/api/invoices` + `/api/payments` → 500 | app queries a **rich `module_records` schema** (record_code, priority, tags, secondary_value, numeric_value, notes, currency, company_id, …) that **no schema step ever adds** — the table only ever gets its 11-column `001_schema.sql` shape → `42703` | 🟠 Invoices/Payments-blocking (schema gap) |

### KPI spot-check (AR aging) — verified correct at the source
`GET /api/finance/ar-aging` returns **exactly** the hand-calculated demo figures:
`current $2,100.50 · 31-60 $875.25 · 90+ $3,300.00 · total outstanding $6,275.75` (paid $1,450 excluded),
split across ColdChain Pharma / Acme Freight / Northwind Retail. This matches the seeder's KPI test.
**Caveat:** these figures are correct in the API/DB but have **no UI surface** — see Note A below.

---

## STEP 2 — UI/UX Phase-2 conformance migration (rendering only)

Migrated the in-scope modules' raw `<table>` / off-system inline badges to the canonical shared
components (`DataTable`, `StatusBadge`, `RiskBadge`), which are Phase-1 contrast-corrected
(600/700-level text) and keyboard-accessible. **Net −48 lines** of one-off table/badge markup.

| Page | Change | Before → After |
|---|---|---|
| [VehiclesModulePage.tsx](frontend/src/pages/VehiclesModulePage.tsx) | `SimpleListCard` raw `<table>` → `DataTable`; snapshot status `<span>` → `StatusBadge`; removed dead local `fmt`, dropped unused `labelize` import | raw table + 2 inline status spans → shared |
| [DriversModulePage.tsx](frontend/src/pages/DriversModulePage.tsx) | `SimpleListCard` raw `<table>` → `DataTable`; 2 status `<span>`s → `StatusBadge`; dropped unused `labelize` | raw table + 2 inline status spans → shared |
| [JobsPage.tsx](frontend/src/pages/JobsPage.tsx) | `Grid` helper raw `<table>` → `DataTable` (the redundant raw table beside the existing DataTable; used by the Stops / Proof / Comms / Audit detail grids, so status columns now auto-render `StatusBadge`) | raw table **and** DataTable → single shared DataTable |
| [AlertRulesPage.tsx](frontend/src/pages/AlertRulesPage.tsx) | custom `PRIORITY_COLOR` priority pill → canonical `RiskBadge`; removed the dead `PRIORITY_COLOR` map + `priColor` local (`StatusBadge` was already used for status) | inline priority badge → shared `RiskBadge` |

**Deliberately left as-is (rendering unchanged) — with rationale:**

- **TripsPage / DispatchWorkspacePage** — already conformant: **0 raw tables**. Trips already renders
  via the shared `DataTable`; DispatchWorkspace is a custom card layout (no table to migrate).
- **DispatchCommandPage assignments board**, **FleetUtilizationPage capacity/efficiency tables** —
  these are **rich interactive tables** (per-row status-transition / cancel / coach buttons, row-click
  → detail-panel selection, deployability progress-bar cells, responsive `hidden lg:table-cell`
  columns). They **already use the canonical `StatusBadge`/`RiskBadge`**. `DataTable` (a
  `columns[]/rows[]` renderer) cannot host per-row action buttons or those custom cells — swapping them
  would be a **functional regression**, not a conformance gain. Kept as-is.
- **AlertsCenterPage** — no raw table; already uses `StatusBadge` for severity and `LoadingState` for
  the load path. `severityTone` is a **card-background tint**, not a status badge. It is a deliberate
  action-first command surface; forcing `DataTable` would destroy its triage UX. Kept as-is.
- **DriverScorecardsPage** — **out of scope**: it is a **Safety** module page (`/driver-scorecards`,
  `safety:view`), not the Drivers module, and its `ScoreBadge`/`ScoreRing` are domain components that
  grade a numeric 0–100 safety score (not a status/risk string, so not a `StatusBadge`/`RiskBadge`
  target). Per "do not touch Safety/Maintenance deep pages," untouched.

---

## STEP 3 / 4 — Fixes applied + re-walkthrough

No cosmetic/visual defects were found in Step 1, so Step 3 focused on the rendering-blocking backend
500s. Fixes (all additive / minimal; the schema fixes follow the codebase's existing idempotent
`ADD COLUMN IF NOT EXISTS` pattern and were explicitly approved this session):

| # | Fix | File |
|---|---|---|
| 0 | `COALESCE(@details::jsonb, …)` cast + login now passes valid JSON (`{source, email}`) | [AuditService.cs](backend-dotnet/Services/AuditService.cs), [EndpointMappings.cs](backend-dotnet/Controllers/EndpointMappings.cs) |
| 1 | Qualified the ambiguous column: `WHERE ai.company_id=@cid` (and `ai.id` in AlertDetail) | [EndpointMappings.cs](backend-dotnet/Controllers/EndpointMappings.cs) |
| 2 | Added base `CREATE TABLE IF NOT EXISTS alert_rules (…)` before the `ALTER`s | [AlertWorkflowSchemaService.cs](backend-dotnet/Services/AlertWorkflowSchemaService.cs) |
| 3 | Added `CREATE TABLE IF NOT EXISTS hos_records (…)` + index; fixed the correlated subquery (dropped the invalid `MAX() … ORDER BY`) | [DriverSchemaService.cs](backend-dotnet/Services/DriverSchemaService.cs), [EndpointMappings.cs](backend-dotnet/Controllers/EndpointMappings.cs) |
| 4 | Idempotent backfill `location_events.accuracy_meters` (matches the file's existing `EnsureColumn` mechanism) | [TelemetrySchemaService.cs](backend-dotnet/Services/TelemetrySchemaService.cs) |
| 5 | Granted `finance:view` to the demo admin (seeder + the already-seeded row) so the Finance/AR routes are reachable | [DemoTenantSeeder.cs](backend-dotnet/Services/DemoTenantSeeder.cs) |
| 6 | Idempotent `module_records` reconciliation: `ADD COLUMN IF NOT EXISTS` for the 15 columns the app queries but no schema step added | [AlertWorkflowSchemaService.cs](backend-dotnet/Services/AlertWorkflowSchemaService.cs) |

**Re-walkthrough result (final pass, 16 loads across admin + portal):**

```
LOGIN  dashboard  fleet-vehicles  fleet-utilization  drivers  driver-scorecards
jobs   trips      dispatch        proof-center       finance-cost-leakage
finance-invoices  alerts          alert-rules        (portal) LOGIN  customer-portal
    → every page: 0 console errors, 0 network failures
```

Endpoint re-verification after fixes: `/api/alerts`, `/api/alert-rules`,
`/api/dispatch/available-drivers`, `/api/trips/40/breadcrumbs`, `/api/invoices`, `/api/payments`,
`/api/profitability` all **200**. Migrated components confirmed rendering in-browser: Vehicles "Live
fleet snapshot" shows `StatusBadge` (AVAILABLE / ON ROUTE / MAINTENANCE, correct contrast); Dispatch
board shows `RiskBadge` (LOW) + `StatusBadge`; Alert Rules table headers + empty state render; Invoices
page shows a clean KPI strip + "No invoices found" empty state (no error).

---

## STEP 5 — Build & test

| Gate | Result |
|---|---|
| Backend build (`dotnet build`) | ✅ 0 errors |
| Backend tests (CI filter `FullyQualifiedName!~Postgres`) | ✅ **839 passed / 0 failed / 0 skipped** |
| Frontend build (`tsc -b && vite build`) | ✅ built, 0 type errors |
| Frontend lint (`eslint .`) | ✅ exit 0 (0 problems) |

Test count held with **zero failures**. (The "877 baseline" from the prior session's ledger includes
the DB-gated `*PostgresTests`, which run only in the SIT/Neon environment and are excluded from this
CI-equivalent run of 839; no test regressed.) All fixes are additive backend SQL/schema + frontend
rendering — no behavioral tests changed.

---

## Open items — data-wiring notes (non-rendering, non-blocking)

These are **logic/data-contract observations**, reported per the "report logic bugs separately"
instruction. They do **not** cause an error or broken render, so they do not gate the demo, but they
mean two finance surfaces show **empty** rather than the seeded numbers:

- **Note A — AR aging has no UI surface.** `GET /api/finance/ar-aging` returns the correct
  `$6,275.75` outstanding split, but **no frontend page consumes that endpoint**. The AR aging KPI is
  verified correct at the API/DB layer (and by the seeder KPI test) but is currently **not viewable in
  the browser**. *Recommendation:* wire an AR-aging card/panel into a Finance page (e.g.
  FinancialAnalyticsPage) to surface the buckets.

- **Note B — Invoices/Payments page reads a different table than the seeder populates.** After the
  reconciliation, `/api/invoices` and `/api/payments` return **200 (empty)** because they read
  `module_records` (`module_key='invoices'/'payments'`), while the demo's invoices live in the real
  finance chain (`issued_invoices`, populated by `RevenueReadinessService`). The page renders a clean
  empty state ("No invoices found", $0 KPIs). *Recommendation:* point the invoices/payments read at
  `issued_invoices`/`invoice_payments`, or seed matching `module_records` rows, so the page shows the
  4 seeded invoices.

Also observed (expected, not a bug): the **Alert Rules** and **Alerts** lists render **empty** for the
demo tenant because the seeder created telemetry alerts in `telemetry_alerts` and no `alert_rules`/
`ai_insights` rows — the pages themselves are healthy (200, correct headers, correct empty states).

---

## Access path (unchanged, still valid)

| Role | Email | Password |
|---|---|---|
| Internal (Fleet Manager — full app, now incl. Finance/AR) | `admin@meridian.demo` | `MeridianDemo!23` |
| Customer Portal (Acme Freight) | `portal@acme.demo` | `MeridianDemo!23` |

Local run: backend `dotnet run --project backend-dotnet/Opstrax.Api.csproj`
(`ConnectionStrings__DefaultConnection=…opstrax_local`, `DemoSeed__Enabled=true`), frontend
`npm run dev` (frontend defaults its API base to `http://localhost:8088`).

---

## Files changed this session

**Frontend (rendering conformance):** `VehiclesModulePage.tsx`, `DriversModulePage.tsx`,
`JobsPage.tsx`, `AlertRulesPage.tsx`.
**Backend (rendering-blocking 500 fixes + approved schema reconciliation):** `AuditService.cs`,
`EndpointMappings.cs` (alerts ambiguity, available-drivers aggregate), `AlertWorkflowSchemaService.cs`
(alert_rules CREATE + module_records reconciliation), `DriverSchemaService.cs` (hos_records),
`TelemetrySchemaService.cs` (accuracy_meters backfill), `DemoTenantSeeder.cs` (finance:view grant).
**Not touched:** Program.cs, RLS middleware, stage19/20/21 migrations, CRM, and all
Safety/Maintenance deep pages.
