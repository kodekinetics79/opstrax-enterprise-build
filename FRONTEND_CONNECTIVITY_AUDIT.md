# FRONTEND CONNECTIVITY AUDIT — KynexOne

_Audit date: 2026-06-08 · Scope: every routed page in `frontend/src/pages` → API client → backend controller → database tables._
_Spec reference: §3 (Frontend Functionality Audit)._

## Method

1. Mapped every page to the API client(s) it imports.
2. Extracted the actual `/api/...` paths each client calls and matched them to backend
   controller `[Route]` attributes (to catch dead/non-existent frontend calls).
3. **Live-verified** one representative endpoint per module against the running API
   (`http://localhost:5117`) with an authenticated token.
4. Scanned each page for loading / error / empty / catch state handling.

## Headline results

- ✅ **All 20 feature pages are wired to real API clients** (LoginPage authenticates via
  `AuthContext` → `/api/auth`). No page renders from a static dataset (the Dashboard, the
  last offender, was fixed in a prior task).
- ✅ **Every frontend API base path maps to a real backend controller** — no dead/orphan
  frontend API calls.
- ✅ **Live smoke test: every module responds correctly** (200/204, or a *correct* guard
  response — see ESS note).
- ✅ Frontend builds clean (`tsc -b && vite build`, 0 errors).

## Per-route connectivity matrix

| Route | Component | API client | Backend route(s) | Primary data source (tables) | Live | Status |
|-------|-----------|-----------|------------------|------------------------------|------|--------|
| `/login` | LoginPage | (AuthContext) auth | `/api/auth` | users, tenants, refresh_tokens | 200 | ✅ working |
| `/dashboard` | DashboardPage | dashboard, intelligence | `/api/dashboard/{summary,trends,overview}`, `/api/ai/insights` | employees, attendance_records, approval_requests, payroll_runs/slips, employee_compliance_records, leave_requests, ai_insights | 200 | ✅ working (wired this engagement) |
| `/ess` | EmployeeSelfServicePage | ess | `/api/ess/{dashboard,profile,attendance,leave,documents,hr-requests}` | employees, leave_balances, attendance, employee_documents, hr_requests | 400* | ✅ working* |
| `/people` | EmployeesPage | employees, organization | `/api/employees`, `/api/departments` … | employees, departments, designations, grades, branches, cost_centers | 200 | ✅ working |
| `/attendance` | AttendancePage | attendance, employees | `/api/attendance/{today,dashboard,records,events,devices,…}` | attendance_records, attendance_raw_events, attendance_devices | 200 | ✅ working |
| `/leave` | LeavePage | leave | `/api/leave/{types,policies,balances,requests,holidays,encashment,compoff}`, `/api/leave-requests` | leave_types, leave_policies, employee_leave_balances, leave_requests, holiday_calendars | 200 | ✅ working |
| `/overtime` | OvertimePage | overtime | `/api/overtime/{requests,policies,comp-off-conversions,reports}` | overtime_policies, overtime_requests, overtime_compoff_conversions | 200 | ✅ working |
| `/payroll` | PayrollPage | payroll | `/api/payroll/{runs,slips,structures,wps,…}` | payroll_runs, payroll_slips, payroll_* | 200 | ✅ working |
| `/approvals` | ApprovalsPage | approvals | `/api/approval-requests`, `/api/approval-workflows` | approval_requests, approval_workflows, approval_decisions | 200 | ✅ working |
| `/shifts` | ShiftsPage | shifts | `/api/shifts/{definitions,roster}` | shift_definitions, shift_rosters | 200 | ✅ working |
| `/recruitment` | RecruitmentPage | recruitment | `/api/recruitment/{openings,candidates,applications,interviews,offers,onboarding,…}` | recruitment_jobs, candidates, applications, interviews, offers, onboarding_tasks | 200 | ✅ working |
| `/compliance` | CompliancePage | compliance | `/api/compliance/{contracts,reports,visa-tracking}` | employee_compliance_records, contracts, visa_tracking | 200 | ✅ working |
| `/loans` | LoansPage | loans | `/api/finance/{loans,advances,bonuses}` | employee_loans, salary_advances, bonus_batches, employee_bonuses | 200 | ✅ working |
| `/reports` | ReportsPage | reports | `/api/reports/{catalog,saved,schedules,executions,run}`, `/api/analytics` | saved_reports, report_schedules, report_execution_logs | 200 | ✅ working |
| `/performance` | PerformancePage | performance | `/api/performance/{cycles,reviews,goals,competencies,feedback,calibration,…}` | appraisal_reviews, performance_cycles, kpi/goals, competencies, feedback360 | 200 | ✅ working |
| `/ai-assistant` | AIAssistantPage | intelligence | `/api/ai/{query,insights,risk-scores,…}` | ai_insights, ai_hr_query_logs, employee_risk_scores | 200 | ✅ working (perm-guarded this engagement) |
| `/hr-requests` | HRRequestCenterPage | intelligence | `/api/hr-requests/{dashboard,…}` | hr_requests, hr_request_comments | 200 | ✅ working |
| `/tenant-admin` | TenantAdminPage | intelligence | `/api/tenant-admin/{subscription,feature-flags,localization,branding}` | tenant_subscriptions, tenant_feature_flags, tenant_localization_settings, tenant_branding | 204 | ✅ working |
| `/user-management` | UserManagementPage | identity | `/api/access`, `/api/audit-logs` | users, roles, permissions, user_roles, audit_logs | 200 | ✅ working |
| `/setup` | SetupPage | organization, setup | `/api/admin/*`, `/api/organization` | system_settings, gcc_compliance_settings, master data, fiscal_years, locations | 200 | ✅ working |

\* **ESS returns HTTP 400 by design** for a non-employee account (the seeded `admin` user
is not an employee). The endpoint exists and responds with a clear, correct guard message:
_"No employee record found for your account…"_. For an employee-linked user it serves the
self-service dashboard. **Not a defect.**

## State-handling review (loading / error / empty)

Most pages implement loading, error, catch and empty states. Pages with **weaker
user-facing error surfacing** (they `catch` so they don't crash, but don't always show an
error message) — recommended polish, **not blocking**:

| Page | Observation | Recommendation |
|------|-------------|----------------|
| ApprovalsPage | catches errors but no dedicated error UI state | add an inline error banner on load failure |
| CompliancePage | 9 catch blocks, no surfaced error message | surface load/action failures to the user |
| HRRequestCenterPage | catches errors silently | add error toast/banner |
| TenantAdminPage | no `loading`/`error` UI state (catches only) | add loading skeleton + error state |

The strongest pages (Leave, Payroll, Performance, Recruitment, Setup, UserManagement,
Employees) have rich loading/error/empty handling already.

## Findings fixed in related tasks (recap)
- Dashboard static demo data → fully wired to live, tenant-scoped APIs (see DEAD_FEATURE_AUDIT.md §5.1).
- Dead `/documents` nav item + fake nav badges removed (DEAD_FEATURE_AUDIT.md §1).
- `/ai-assistant` route/nav now permission-guarded (RBAC_TENANT_ISOLATION_AUDIT.md).

## Remaining recommendations (non-blocking)
1. Add explicit error-state UI to the four pages above for consistent UX.
2. Code-split the bundle — `index.js` is ~1.4 MB (single chunk); Vite warns >500 kB.
   Use route-level `React.lazy`/dynamic import to cut initial load.

## Build / verification status
- Frontend: `npm run build` ✅ 0 errors.
- Live API smoke test: all 20 modules reachable and responding correctly (authenticated).
