# RBAC & TENANT ISOLATION AUDIT — KynexOne

_Audit date: 2026-06-08 · Scope: all 69 API controllers in `backend-dotnet/Zayra.Api/Controllers`, plus frontend route/nav guards._
_Spec reference: §7 (RBAC, Security, Tenant Separation)._

## Method

1. **Auth coverage** — every controller checked for `[Authorize]`.
2. **Tenant isolation** — a static analyzer parsed every `_db.<DbSet>…<async terminator>`
   query chain (full statement, multi-line) and flagged any that did **not** contain a
   `TenantId` predicate.
3. Each flagged chain was then read in context to determine whether it was a genuine
   cross-tenant leak or a query gated by a prior tenant-scoped lookup (parent/child).

## Headline results

- ✅ **All 69 controllers carry `[Authorize]`.** No anonymous data endpoints (only
  `AuthController` login/refresh are intentionally `[AllowAnonymous]`).
- ✅ **No actively exploitable cross-tenant READ leak remains.** Aside from the Dashboard
  (already fixed — see below), every flagged by-id query is gated by a prior
  `TenantId`-scoped lookup that returns `NotFound` for foreign ids.
- The codebase is **consistently tenant-scoped** — the overwhelming majority of queries
  already filter `TenantId` explicitly.

## Fixed in this pass

| # | File | Issue | Fix |
|---|------|-------|-----|
| 1 | `Controllers/DashboardController.cs` | **Real cross-tenant leak** — `summary` & `trends` counted `Employees`/`AttendanceRecords` across **all tenants** (no `TenantId` filter). | Tenant-scoped all queries (fixed in the Dashboard wiring task). |
| 2 | `Controllers/Performance/FeedbackController.cs` | `Submit360` inserted 360-feedback **without verifying `req.ReviewId` belongs to the caller's tenant**, and the duplicate-check query omitted `TenantId`. | Added a tenant-scoped `AppraisalReviews` existence check (→ `NotFound`) and added `TenantId` to the dedup query. |
| 3 | `Controllers/Finance/BonusesController.cs` | `FirstAsync(x => x.Id == batchId)` fetched a batch by PK without `TenantId` (gated upstream, but fragile). | Added `&& x.TenantId == tid`. |
| 4 | `Controllers/Finance/LoansController.cs` | `FirstAsync(x => x.Id == id)` fetched a loan by PK without `TenantId` (gated upstream). | Added `&& x.TenantId == tid`. |
| 5 | `Controllers/EmployeesController.cs` | `ApproveHrTransfer` re-fetched the employee by id without `TenantId` (id derived from a tenant-scoped transfer). | Added `&& x.TenantId == tenantId`. |
| 6 | `Controllers/OvertimeController.cs` | `CreateCompOffConversion` fetched the overtime policy by id without `TenantId` (id from a tenant-scoped request). | Added `&& x.TenantId == tenantId`. |
| 7 | `frontend/src/App.tsx` + `routes/navigation.ts` | `/ai-assistant` route and nav item had **no permission guard** (backend was protected, but the UI exposed it to any authenticated user). | Wrapped route in `ProtectedRoute requiredPermissions={['ai.query','ai.insights_view']}` and added the same to the nav item. |

## Reviewed and confirmed SAFE (gated child queries — no change required)

These were flagged by the analyzer but are queries on **child rows keyed by a parent FK**,
inside methods that first load the parent with a `TenantId` filter and return `NotFound`
for foreign/absent ids. Listed for traceability:

- `Finance/AdvancesController.cs` L115–116 — installments/approvals by `AdvanceId`; advance gated in `Get`.
- `Finance/BonusesController.cs` L92, L215 — approvals/bonuses by `BonusBatchId`; batch gated.
- `Finance/LoansController.cs` L112–113, L197 — installments/approvals by `LoanId`; loan gated.
- `Performance/ReviewsController.cs` L128, L188 — competency ratings by `ReviewId`; review gated by `TenantId` in `SubmitSelfAssessment`.
- `Recruitment/AssessmentsController.cs` L188 — template passing-score lookup via tenant-owned `assessment.TemplateId`.
- `EmployeesController.cs` L285 — documents/history by `EmployeeId`; employee gated.

## RBAC model (verified present)

- **Roles** (seeded, `AuthSeeder.cs`): Super Admin / Admin, Company Admin, HR Manager,
  HR Officer, Payroll Manager/Officer, Finance Approver, Department Manager / Supervisor,
  Recruiter, Employee, Viewer/Auditor — 10 roles, 45 permissions.
- **Enforcement**: class-level `[Authorize]` on every controller + method-level
  `[Authorize(Roles = …)]` on sensitive actions (e.g. payroll, bonus approval, AI
  insights). Frontend mirrors with `ProtectedRoute requiredPermissions` per route.

## Residual risks / recommendations

1. ✅ **Global tenant query filter — IMPLEMENTED (2026-06-08).** Previously isolation
   depended entirely on every developer remembering `.Where(TenantId == …)` — which is how
   the Dashboard leak happened. `ZayraDbContext` now applies an EF Core global
   `HasQueryFilter` to **every entity that exposes a `TenantId` property** (added
   reflectively in `OnModelCreating`). The context reads the current `tenant_id` claim via
   `IHttpContextAccessor`; the filter is
   `e => _tenantId == null || e.TenantId == _tenantId`, so it **auto-scopes every
   authenticated query** while bypassing safely when there is no tenant context (startup
   seeding, login/refresh before auth, background work). A forgotten manual filter can no
   longer leak across tenants by default.
   - Verified end-to-end: login (bypass) issues a token; authenticated reads
     (`/employees`, `/departments`, `/access/roles`, `/dashboard/*`) all return the
     caller's own tenant data; startup seeding succeeded; no model-build exceptions.
   - **Follow-up test recommended:** create a second tenant + user and confirm
     cross-tenant reads return empty (negative test — not possible in the current
     single-seeded-tenant environment).
2. **Defence-in-depth `TenantId` on remaining gated child queries** (the "SAFE" list
   above) — cheap to add; would make every query self-contained and satisfy §7's
   "tenant isolation in *every* query" literally.
3. **Role checks are role-name based** (`[Authorize(Roles="…")]`) rather than
   permission-key based in some controllers, while the frontend uses permission keys.
   Consider standardising on permission-policy authorization for consistency.

## Build status
- Backend: `dotnet build` ✅ 0 errors.
- Frontend: `npm run build` ✅ (tsc + vite), 0 errors.
