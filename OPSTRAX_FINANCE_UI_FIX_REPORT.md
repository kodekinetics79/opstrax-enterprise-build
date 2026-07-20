# OpsTrax — Finance UI Data-Wiring Fix Report

**Scope:** the two non-blocking data-wiring gaps flagged in `OPSTRAX_DEMO_READINESS_REPORT.md`:
(1) the Invoices page read `module_records` instead of the real revenue spine, and (2) AR aging
(`GET /api/finance/ar-aging`) had no UI surface. Both are now wired to their real, already-tested
Finance endpoints and verified on screen against the seeded **"Meridian Logistics — Demo"** tenant.

Remote confirmed: `origin → github.com/kodekinetics79/opstrax-enterprise-build.git` (the `zayra`
remote is the off-limits sibling — **not touched**). **RLS-staging, Program.cs tenant-context, CRM,
and Safety/Maintenance deep pages were not touched.**

## Result: ✅ **Real financial data is now visible and correct on screen**

Both `/invoices` and the new `/ar-aging` render the seeded tenant's real revenue-spine data with
**0 console errors and 0 network failures**, matching the hand-verified figures proven by the
Finance module's Postgres integration tests.

---

## Pre-flight blocker (had to unblock before any Finance work)

On starting the app I hit two blockers that stopped the whole backend, unrelated to this task:

1. **Docker/Postgres was down.** The `zayra_pg` container (the local `opstrax_local` DB) was not
   running — every request 500'd with "Failed to connect to 127.0.0.1:5433". Started Docker + the
   container; the seeded demo tenant was intact (company `MERIDIAN-DEMO`, 4 issued invoices,
   `$6,275.75` outstanding).
2. **Backend crashed at endpoint-routing init** (every route 500'd). An **uncommitted prior-session
   Platform-track change** registered `app.MapDelete("/api/platform/tenants/{id}", TenantDelete)`
   whose handler takes an **inferred `Dictionary<string,object?> body`** parameter — ASP.NET cannot
   infer a request body on `DELETE`, so the entire route-matcher failed to build. With your approval I
   applied the **minimal one-attribute fix**: annotated that parameter
   `[FromBody(EmptyBodyBehavior = Allow)]` so the DELETE body is optional and no longer inferred.
   `TenantOffboardingService` is a real DI service (Program.cs:86), so nothing else was needed. This
   is a **pre-existing Platform-track bug**, flagged here; no other Platform logic was touched.

---

## STEP 1 — Invoices page rewired to the real revenue spine

**1a — root cause.** `FinancialAnalyticsPage.tsx` called `GET /api/invoices`, which reads the
generic `module_records` table (`module_key='invoices'`). The demo's real invoices are created by
the Finance module's revenue chain and live in `issued_invoices`, surfaced by the **existing,
already-tested** endpoint `GET /api/issued-invoices` (`RevenueReadinessEndpoints`). No new endpoint
was invented.

**1b — rewire (existing components only).**
- `financialApi.invoices` now calls `GET /api/issued-invoices` (returns `{ items: [...] }`) and maps
  the real fields: `invoiceNumber`, `total`, `amountPaid`, `balanceDue`, `paymentStatus`, `dueAt`,
  deriving a display status (Paid / Overdue / Partial / Sent) + aging days from balance & due date.
- The raw `<table>` and the one-off `InvoiceStatusBadge` were replaced with the shared **`DataTable`**
  (which auto-renders the canonical **`StatusBadge`** on the `status` column and currency formatting
  on `$`-amounts). KPI cards now use the shared **`KpiCard`**. No new UI pattern was introduced.

**1c — verified on screen (seeded tenant, admin@meridian.demo):** the page shows the **4 real
invoices** with correct amounts + payment status:

| Invoice | Total | Paid | Balance | Status |
|---|---|---|---|---|
| INV-…09fbd | $1,450.00 | $1,450.00 | $0.00 | **PAID** |
| INV-…982c9 | $2,100.50 | $0.00 | $2,100.50 | **SENT** |
| INV-…f56a  | $875.25   | $0.00 | $875.25   | **OVERDUE** (45d) |
| INV-…8341  | $3,300.00 | $0.00 | $3,300.00 | **OVERDUE** (120d) |

KPI tiles: Total Invoices **4** · Outstanding **$6,275.75** · Overdue **2** · Paid **1** · Total
Value **$7,725.75**. Copy updated to "Sourced from the live revenue spine (issued_invoices)."

---

## STEP 2 — AR Aging surface added

**2a/2b — least-disruption surface.** Added an **"AR Aging" tab** to the existing
`FinancialAnalyticsPage` (which already hosts Invoices / Payments / Profitability tabs), plus its own
nav entry + route so it's reachable from the Financials group:
- New route `/ar-aging` (App.tsx, gated on `finance:view` like the other Finance routes).
- New `moduleConfig` entry `{ key: "ar-aging", title: "AR Aging", group: "Financials" }` + icon.
- New `ArAgingTab` calls the existing **`GET /api/finance/ar-aging`** and renders with the shared
  **`KpiCard`** (one tile per bucket) + a per-customer **`DataTable`** — no new design pattern.

**2c — verified on screen (seeded tenant):** bucket tiles show **Current $2,100.50 · 1–30 $0.00 ·
31–60 $875.25 · 61–90 $0.00 · 90+ $3,300.00 · Total Outstanding $6,275.75** — exactly the
hand-calculated values proven in the Finance module's tests. Per-customer table: ColdChain Pharma
$3,300.00 (90+), Acme Freight Co. $2,100.50 (current), Northwind Retail $875.25 (31–60).

---

## STEP 3 — Verification

| Gate | Result |
|---|---|
| Backend build | ✅ 0 errors |
| Backend tests (CI filter `!~Postgres`) | ✅ **839 passed / 0 failed** — baseline held |
| **Finance Postgres integration tests** (`RevenueReadinessPostgres`, real DB + real seed) | ✅ **14 passed / 0 failed** — the endpoints the UI now consumes remain fully proven |
| Frontend build (`tsc -b && vite build`) | ✅ built, 0 type errors |
| Frontend lint (`eslint .`) | ✅ exit 0 |
| Browser re-walkthrough (`/invoices`, `/ar-aging`) | ✅ 0 console errors, 0 network failures, correct data rendered |

### Standing rule — test coverage preserved (not weakened)
One source-regression guard (`Stage16ASourceRegressionTests.CrmAndFinance_Surfaces_Are_Live_Only_No_Seed_Fallback_Masking`)
asserted two **UI-copy string markers** on the finance page ("Ready to bill", "No seed fallback is
used on this surface.") that my rewire renamed. Rather than deleting the guard, I **updated and
strengthened** its positive assertions to the new live-spine wiring — it now asserts the page
references `/api/issued-invoices` **and** `/api/finance/ar-aging`, and **does not** reference the old
`/api/invoices` module_records feed. Every anti-fallback assertion (`DoesNotContain "withFallback" /
"seedInvoices" / "seedCustomers"`) is unchanged. **No Finance endpoint test coverage was removed or
weakened**; the 14 real-Postgres Finance integration tests remain green.

---

## Files changed this session
- `frontend/src/pages/FinancialAnalyticsPage.tsx` — Invoices tab → `/api/issued-invoices` +
  shared `DataTable`/`StatusBadge`/`KpiCard`; new `ArAgingTab` → `/api/finance/ar-aging`.
- `frontend/src/App.tsx` — new `/ar-aging` route.
- `frontend/src/modules/moduleConfig.ts` — new "AR Aging" Financials nav entry + icon.
- `backend-dotnet.Tests/Stage16ASourceRegressionTests.cs` — updated the finance source-regression
  guard to the live-spine wiring (strengthened, not weakened).
- `backend-dotnet/Controllers/PlatformEndpoints.cs` — one-attribute `[FromBody]` unblock on the
  pre-existing Platform `TenantDelete` route (crash-on-boot; not part of this task's Finance scope).

**Not touched:** RLS-staging, Program.cs tenant-context, CRM, Safety/Maintenance deep pages, and the
Finance endpoint/service logic itself (only the UI that consumes it).
