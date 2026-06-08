# DEAD_FEATURE_AUDIT — KynexOne

_Audit date: 2026-06-08 · Scope: frontend (`frontend/src`) routes, navigation, pages, components._

This audit hunts for fake buttons, dead tabs/links, static charts with fake numbers,
hardcoded lists in production screens, "coming soon" modules in main nav, and any
clickable action that does not perform a real function.

## Summary

The application is in **much better shape than a typical prototype**: 19 of 20 routed
pages are wired to live backend APIs, and a frontend-wide scan found **no `mock`/`dummy`/
`sample`/`fake` data arrays inside page components**. Every `alert()` call is legitimate
error handling inside a `catch` around a real API call — not placeholder UI.

The dead-feature problems were concentrated in **two places**: the navigation config and
the **Dashboard**, which is the only screen still backed by a static demo dataset.

---

## 1. Removed (dead nav / fake metrics)

| Item | File | Problem | Action |
|---|---|---|---|
| "Documents & Letters" nav item (`/documents`) | `src/routes/navigation.ts` | No route and no page exist; it fell through to the `*` `ComingSoonPage` ("This module is coming soon") — a coming-soon module in main nav. | **Removed** from nav. (`FileText` import also removed.) |
| Attendance nav badge `12` | `src/routes/navigation.ts` | Hardcoded fake notification count. | **Removed** |
| Payroll nav badge `4` | `src/routes/navigation.ts` | Hardcoded fake notification count. | **Removed** |
| Approval Center nav badge `18` | `src/routes/navigation.ts` | Hardcoded fake notification count. | **Removed** |

> Badges were removed rather than wired because no backend endpoint currently returns
> per-module pending counts. They can be re-introduced once a `/api/dashboard/badges`
> (or per-module count) endpoint exists. See Remaining Risks.

## 2. Fixed / verified

| Item | File | Status |
|---|---|---|
| `ComingSoonPage` catch-all (`*` route) | `src/App.tsx` | **Kept as a 404 fallback only.** After removing `/documents`, no nav item routes to it, so users can no longer reach a "coming soon" screen through normal navigation. |
| All non-Dashboard pages | `src/pages/*` | **Verified wired** — each reads/writes through its `src/api/*` client to the .NET backend (People, Attendance, Leave, Overtime, Payroll, Approvals, Shifts, Recruitment, Compliance, Loans, Reports, Performance, ESS, HR Requests, Tenant Admin, User Management, Setup, AI Assistant). |
| Branding | multiple | All user-facing "Zayra" strings replaced with **KynexOne** (see git history / README note). |

## 3. Hidden / incomplete modules

| Module | Status | Notes |
|---|---|---|
| Documents & Letters | **Hidden** (nav item removed) | No page or backend wiring exists yet. Model/entities exist server-side (`EmployeeDocument`, `EmployeeDocumentVersion`) but there is no frontend page. Re-add to nav only when a real `DocumentsPage` + route is built. |

## 4. Buttons / actions wired (confirmed real)

No previously-dead buttons required wiring — the audit found the action buttons across
Leave, Overtime, Payroll, Performance, User Management, etc. already call live APIs
(approve/reject/withdraw/cancel/publish/launch/generate-WPS/etc.), each with real
error handling. No fake "success" toasts were found that fire without a backend call.

---

## 5. Remaining risks (NOT yet fixed — needs decision/backend)

### 5.1 Dashboard static demo data — ✅ **FIXED** (2026-06-08)
Previously `src/pages/DashboardPage.tsx` rendered most panels from a static demo
dataset (`src/modules/dashboard/dashboardData.ts`). This has been fully wired to real,
tenant-scoped data and **verified end-to-end** (login → `GET /api/dashboard/overview`
returns HTTP 200 with a valid, tenant-scoped payload):

- **Backend:** added `GET /api/dashboard/overview` to `DashboardController` returning
  real aggregates — pending-approvals count + recent queue, latest payroll-run summary,
  payroll-by-department, workforce mix (active employees by employment type), compliance
  expiry alerts, open leave requests, new joiners this month. All queries filter by
  `TenantId`. **Also fixed a pre-existing cross-tenant leak**: the existing `summary`
  and `trends` actions queried `Employees`/`AttendanceRecords` with **no tenant filter**
  (counted all tenants); they are now tenant-scoped.
- **Frontend:** `DashboardPage` now fetches `/summary`, `/trends`, `/overview`, and the
  real `/api/ai/insights` endpoint. Every panel (KPIs, attendance trend, approval queue,
  payroll command center, payroll-by-entity chart, workforce-mix pie, AI insights,
  alerts, self-service, bottom module summaries) reads live data with proper **empty
  states** — no fabricated fallbacks. Quick-action buttons, header buttons, and the
  featured-action card now navigate to real routes (were previously dead buttons).
- `src/modules/dashboard/dashboardData.ts` **deleted**.

> A real LINQ-translation bug (a conditional inside an EF `GroupBy().Select()`) was
> caught by the end-to-end smoke test and fixed before shipping.

### 5.2 Navigation grouping vs. brand spec — LOW priority
Current sidebar groups (Core HR, Leave & Time, Finance, Talent, Intelligence, Service
Desk, Admin) don't match the requested KynexOne grouping (Command Center, Workforce,
Time & Leave, Payroll, Talent, Performance, Operations, Reports, Administration,
Settings). Cosmetic; addressed under the UI/UX cleanup task (spec §8), not a dead feature.

### 5.3 `ai-assistant` route has no permission guard — note for RBAC audit
`/ai-assistant` is the only app route without a `ProtectedRoute requiredPermissions`
wrapper. Functionally reachable by any authenticated user. Flagged here, to be resolved
in the RBAC/security audit (spec §7).

---

## Files changed in this audit
- `src/routes/navigation.ts` — removed dead `/documents` item, removed 3 fake badges, removed unused `FileText` import.

## Build status
`npm run build` — ✅ passes (tsc + vite), no TypeScript errors.
