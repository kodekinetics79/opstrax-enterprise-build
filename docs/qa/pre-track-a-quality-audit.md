# Pre-Track A Quality Audit
**KynexOne AI Workforce — HRM SaaS**  
**Audit Date:** 2026-06-17  
**Auditor Roles:** Software Architect · QA Lead · Security Engineer · DB Architect · DevOps · Performance · UI/UX  
**Scope:** Full codebase audit before implementing Track A (Saudi Regulatory Compliance)  
**Codebase Scale:** 255 .cs backend files · 22 frontend view files · 36 EF Core migrations · 73 controllers

---

## Executive Verdict

```
STATUS: PARTIAL — NOT READY for Track A
```

The platform has strong foundations in tenant isolation, payroll business logic, RBAC, and database schema. However **4 security gaps** (two P0/P1 authentication failures, missing MFA, no rate limiting) and **1 deployment-critical database issue** (`EnsureCreated` vs `Migrate`) must be resolved before production client exposure or Saudi compliance work begins. All must be fixed in a single hardening PR before Track A.

---

## Scores

| Domain | Score | Verdict |
|---|---|---|
| Architecture | **8 / 10** | Strong, two missing headers |
| Business Logic | **8 / 10** | Solid; demo seeder is unconditional |
| Security | **5 / 10** | No lockout, no rate limit, no MFA |
| Database / Scalability | **7 / 10** | Good schema; wrong migration runner |
| UI / UX | **8 / 10** | Enterprise-grade; no error boundaries |
| Test Coverage | **5 / 10** | Integration + Playwright added; no unit tests for core payroll math |
| **Overall** | **6.8 / 10** | — |

---

## Commands Run & Results

| Command | Result |
|---|---|
| `dotnet build Zayra.Api.csproj` | ✅ 0 errors, 10 warnings (see §3) |
| `npx tsc --noEmit` (frontend) | ✅ 0 errors |
| `npm run build` (frontend) | ✅ Clean build, all 19 routes generated |
| `find backend-dotnet -name "*.cs" | grep -v bin | grep -v obj | wc -l` | 255 |
| `find backend-dotnet -name "*Controller*" | grep -v bin | grep -v obj | wc -l` | 73 |
| `find backend-dotnet -name "*.cs" -path "*/Migrations/*" | grep -v bin | wc -l` | 36 |
| `find frontend/src/views -name "*.tsx" | wc -l` | 22 |
| `cat frontend/middleware.ts` | ❌ NO FILE |
| `grep -rn "RateLimit\|AddRateLimiter" Program.cs` | ❌ No output |
| `grep -n "LockoutEnd\|FailedLogin" AuthService.cs` | ❌ No output |
| `grep "hasMfaEnabled" PlatformController.cs` | ❌ `= false, // placeholder` |

---

## Area 1 — Architecture

### Findings

| ID | Severity | Status | Finding | File / Evidence |
|---|---|---|---|---|
| A-01 | P3 | PASS | Clean controller/service/infrastructure separation throughout | All controllers thin; business logic in `Infrastructure/` implementations |
| A-02 | P0 | PASS | Global EF Core tenant query filters — reflection-based, covers all entities with `TenantId` | `ZayraDbContext.cs:2126-2182` |
| A-03 | P1 | PASS | `FeatureFlagGuardFilter` — global action filter, route-prefix→feature-key map, 2-min cache | `FeatureFlagGuardFilter.cs:18-161`, `Program.cs:52` |
| A-04 | P1 | PASS | `SubscriptionGuardFilter` — 402 on expired/cancelled, 200 with warning header on past-due | `SubscriptionGuardFilter.cs:10-94` |
| A-05 | P1 | PASS | Global exception handler — structured JSON, traceId, no stack-trace leak | `Program.cs:193-211` |
| A-06 | P2 | PASS | Security headers: `X-Content-Type-Options`, `X-Frame-Options`, `Referrer-Policy` | `Program.cs:214-228` |
| A-07 | P2 | FAIL | **Missing `Content-Security-Policy` header** | `Program.cs` — no CSP in security middleware |
| A-08 | P2 | FAIL | **Swagger UI exposed in all environments, no env guard** | `Program.cs:249-250` |
| A-09 | P2 | PASS | Correct middleware pipeline order (exception → security → CORS → auth → authz) | `Program.cs:248-253` |
| A-10 | P2 | PASS | 10 domain-specific audit tables + central AuditLogs | `ZayraDbContext.cs` — 10 `*AuditLog` DbSets |
| A-11 | P2 | PASS | Background jobs with graceful shutdown (`stoppingToken`) | `QiwaSyncWorker.cs` |
| A-12 | P2 | WARN | No HSTS or `UseHttpsRedirection` — acceptable if TLS terminated at Railway edge | `Program.cs` |

---

## Area 2 — Business Logic

### Findings

| ID | Severity | Status | Finding | File / Evidence |
|---|---|---|---|---|
| B-01 | P0 | PASS | Payroll tenant scoping — every query filtered by `tenantId` from JWT claim | `PayrollController.cs:1132`, `GetTenantId()` |
| B-02 | P1 | PASS | Payroll segregation of duties — processor cannot self-approve | `PayrollController.cs:317-320` |
| B-03 | P1 | PASS | WPS/IBAN server-side validation — explicit comment "never trust frontend pre-check" | `PayrollController.cs:527-543` |
| B-04 | P1 | PASS | Net salary floor (≥ 0, GCC law) | `PayrollController.cs:197-199` |
| B-05 | P1 | PASS | EOSB calculation correct (21 days/yr ≤5yrs, 30 days/yr >5yrs), tenant-configurable | `PayrollController.cs:773-820` |
| B-06 | P1 | FAIL | **`DemoDataSeeder.SeedAsync()` called unconditionally — demo tenants always created with `Demo@1234`** | `Program.cs:269-278`, `DemoDataSeeder.cs` |
| B-07 | P2 | WARN | Payroll processing loads all active employees into memory — no batch limit | `PayrollController.cs:127-128` |
| B-08 | P2 | PASS | Leave approval workflow — separate service, ACID balance tracking | `LeaveService.cs` |
| B-09 | P2 | PASS | Manager scope enforcement via `DataScopeService` | `PayrollController.cs:700`, `EmployeesController.cs:74` |

---

## Area 3 — Connectivity & Integration

### Findings

| ID | Severity | Status | Finding | File / Evidence |
|---|---|---|---|---|
| C-01 | P2 | PASS | Frontend uses relative API proxy in browser; server-side uses `NEXT_PUBLIC_API_BASE_URL` | `frontend/next.config.ts:14-23` |
| C-02 | P2 | PASS | 401 triggers silent refresh; 402 redirects to subscription page | `frontend/src/api/client.ts` |
| C-03 | P2 | PASS | Docker Compose: MySQL + Redis healthchecks; backend `depends_on: condition: healthy` | `docker-compose.yml` |
| C-04 | P1 | WARN | **Default JWT signing key fallback is a known placeholder string** | `docker-compose.yml:45` — `${JWT_SIGNING_KEY:-CHANGE_ME_...}` |
| C-05 | P2 | PASS | Redis is optional — graceful in-memory fallback | `Program.cs:82-91` |
| C-06 | P3 | PASS | dotnet build: 0 errors | build output |
| C-07 | P3 | PASS | TypeScript typecheck: 0 errors | tsc output |
| C-08 | P3 | WARN | **10 build warnings — 9 CS8602/CS8601 nullable dereferences + 1 vulnerable package** | `AuthService.cs:52`, `ApplicationsController.cs:246,247,429`, `PlatformController.cs:407`, `QiwaSyncWorker.cs:116` |

---

## Area 4 — Security Audit

### Findings

| ID | Severity | Status | Finding | File / Evidence |
|---|---|---|---|---|
| S-01 | P0 | FAIL | **Account lockout NOT enforced.** `User.LockoutEnd` / `FailedLoginCount` fields exist in schema but `AuthService.LoginAsync` never reads/writes them. Unlimited brute-force attacks possible.** | `AuthService.cs:32-57` — no lockout logic; `User.cs:22-23` — fields defined |
| S-02 | P1 | FAIL | **No HTTP rate limiting on `/api/auth/login` or `/api/platform/auth/login`** | `Program.cs` — no `AddRateLimiter()` call anywhere |
| S-03 | P1 | FAIL | **MFA not implemented — confirmed hardcoded `false` placeholder** | `PlatformController.cs:1878` — `hasMfaEnabled = false, // MFA not yet...` |
| S-04 | P1 | FAIL | **Known vulnerable dependency: MailKit 4.8.0 (GHSA-9j88-vvj5-vhgr, moderate)** | `Zayra.Api.csproj` — NU1902 build warning |
| S-05 | P1 | WARN | Default signing key in appsettings.json and docker-compose.yml fallback | `appsettings.json:8`, `docker-compose.yml:45` |
| S-06 | P1 | PASS | PBKDF2-SHA256, 100,000 iterations, 16-byte salt, constant-time compare | `Pbkdf2PasswordHasher.cs` |
| S-07 | P1 | PASS | JWT: all 4 validations enabled, 1-min clock skew | `Program.cs:96-108` |
| S-08 | P1 | PASS | Refresh token rotation — revoke-on-use, replacement hash stored | `AuthService.cs:60-79` |
| S-09 | P1 | PASS | Forgot password resistant to user enumeration | `AuthService.cs:97-101` |
| S-10 | P1 | PASS | Payroll endpoints behind `[Authorize(Roles = ...)]`; manager scoped | `PayrollController.cs:16` |
| S-11 | P1 | PASS | Platform admin separate JWT claim (`is_platform_admin=true`), separate policy | `PlatformController.cs:25`, `Program.cs:112-113` |
| S-12 | P2 | PASS | Over-posting protected — all inputs use DTOs, never direct entity binding | `PayrollController.cs:1139-1147` |
| S-13 | P2 | PASS | CORS — explicit allowlist, not wildcard | `Program.cs:64-72` |
| S-14 | P2 | PASS | No secrets in frontend bundle | `grep` search returned zero secrets |
| S-15 | P2 | WARN | `[AllowAnonymous]` on localization endpoint — acceptable (non-sensitive UI strings) | `TenantAdminController.cs:76` |

---

## Area 5 — Database Quality

### Findings

| ID | Severity | Status | Finding | File / Evidence |
|---|---|---|---|---|
| D-01 | P0 | PASS | Global query filters via reflection — tenant + soft-delete applied universally | `ZayraDbContext.cs:2126-2182` |
| D-02 | P1 | PASS | Critical composite indexes on high-traffic paths | `ZayraDbContext.cs` + migration `20260612020000_AddHighVolumeQueryIndexes.cs` |
| D-03 | P1 | PASS | All monetary columns: `decimal` with `HasPrecision(14,2)` | consistent throughout |
| D-04 | P1 | PASS | WPS/SIF schema ready | `ZayraDbContext.cs:152-153` — `WPSFileBatch`, `SIFFileRecord` |
| D-05 | P1 | PASS | QIWA integration schema ready | `ZayraDbContext.cs:304-306`, migration `20260611212436` |
| D-06 | P2 | FAIL | **`Database.EnsureCreatedAsync()` used — EF Core migrations NOT run automatically in production** | `Program.cs:264` — should be `Database.MigrateAsync()` |
| D-07 | P2 | PASS | Cascade behavior selective — `Restrict` on Tenant→Company guards against accidental cascade deletes | `ZayraDbContext.cs` |
| D-08 | P2 | WARN | Payroll processing joins all employees in memory — no batch size ceiling | `PayrollController.cs:127-166` |
| D-09 | P2 | WARN | `MissingTableCreator.EnsureAsync()` is an ad-hoc schema supplement alongside EF migrations — risk of divergence | `Program.cs:265` |
| D-10 | P3 | WARN | Audit log growth — no retention policy or archival strategy defined | No `AuditLog` cleanup job found |

---

## Area 6 — UI/UX Quality

### Findings

| ID | Severity | Status | Finding | File / Evidence |
|---|---|---|---|---|
| U-01 | P2 | PASS | All 19 nav routes have corresponding page files | Verified against `navigation.ts` |
| U-02 | P2 | PASS | Feature-gated nav items hidden correctly (client side) | `Sidebar.tsx:125` |
| U-03 | P2 | PASS | `PermissionGate` on every dashboard page | `app/(dashboard)/*/page.tsx` |
| U-04 | P2 | PASS | Empty states in list views | `EmployeesPage.tsx:462`, `PerformancePage.tsx:333` |
| U-05 | P2 | PASS | Loading states — spinners in forms, skeletons in heavy pages | `LoginPage.tsx:372-376` |
| U-06 | P2 | PASS | 403 Access Denied page exists | `app/access-denied/page.tsx` |
| U-07 | P2 | WARN | **No React Error Boundary component detected** — unhandled render errors will crash subtrees silently | `grep -rn "ErrorBoundary" frontend/src` → zero matches |
| U-08 | P2 | WARN | **Dashboard monetary figures hardcoded to `AED`** — multi-currency tenants see wrong currency | `DashboardPage.tsx:64-66` |
| U-09 | P3 | PASS | Language switcher wired to `LocaleContext` — EN/AR/FR/ES with RTL support | `LocaleContext.tsx`, `TopBar.tsx` |
| U-10 | P3 | PASS | No dead/fake controls found in views | `grep "TODO\|FIXME\|HACK\|mock\|fake\|demo"` → zero issues |
| U-11 | P3 | PASS | Login UX — password toggle, three modes, autocomplete attributes | `LoginPage.tsx` |
| U-12 | P3 | PASS | Saudi compliance dashboard + config UI complete (QIWA/GOSI/WPS/EOSB/Docs) | `SaudiComplianceDashboard.tsx`, `SaudiComplianceConfig.tsx` |

---

## Area 7 — Routes & Navigation

### Route Matrix

| Route | Nav Declared | Page File | Permission Gate | Feature Gate | Status |
|---|---|---|---|---|---|
| `/dashboard` | ✅ | ✅ | `dashboard.read` | — | ✅ OK |
| `/ess` | ✅ | ✅ | `ess.read` | — | ✅ OK |
| `/people` | ✅ | ✅ | `employees.read` | — | ✅ OK |
| `/attendance` | ✅ | ✅ | `attendance.read` | — | ✅ OK |
| `/leave` | ✅ | ✅ | `leave.read` | — | ✅ OK |
| `/shifts` | ✅ | ✅ | `attendance.read` | `shifts` | ✅ OK |
| `/overtime` | ✅ | ✅ | `overtime.read` | `overtime` | ✅ OK |
| `/payroll` | ✅ | ✅ | `payroll.read` | `payroll` | ✅ OK |
| `/loans` | ✅ | ✅ | `loans.read` | — | ✅ OK |
| `/recruitment` | ✅ | ✅ | `recruitment.read` | `recruitment` | ✅ OK |
| `/performance` | ✅ | ✅ | `performance.read` | `performance` | ✅ OK |
| `/compliance` | ✅ | ✅ | `compliance.read` | `compliance` | ✅ OK |
| `/ai-assistant` | ✅ | ✅ | `ai.query` | `ai_assistant` | ✅ OK |
| `/reports` | ✅ | ✅ | `reports.read` | — | ✅ OK |
| `/hr-requests` | ✅ | ✅ | `approvals.read` | — | ✅ OK |
| `/approvals` | ✅ | ✅ | `approvals.decide` | — | ✅ OK |
| `/user-management` | ✅ | ✅ | `users.manage` | — | ✅ OK |
| `/saudi-compliance` | ✅ | ✅ | `compliance.read` | `qiwa` | ✅ OK |
| `/tenant-admin` | ✅ | ✅ | `security.manage` | — | ✅ OK |
| `/setup` | ✅ | ✅ | `organization.write` | — | ✅ OK |

**Result: 0 broken navigation routes.**

### Route Security Gap

| ID | Severity | Status | Finding | Evidence |
|---|---|---|---|---|
| R-01 | P2 | FAIL | **No `frontend/middleware.ts` — all route auth is client-side only.** Server-rendered HTML returned to unauthenticated direct URL access before client JS checks auth. | `cat frontend/middleware.ts` → NO FILE |
| R-02 | P3 | WARN | `app/(dashboard)/page.tsx` exists but not in nav — verify it redirects to `/dashboard` | verify redirect behavior |

---

## Area 8 — Performance

### Findings

| ID | Severity | Status | Finding | File / Evidence |
|---|---|---|---|---|
| P-01 | P1 | WARN | **Payroll `ProcessRun` endpoint loads all active employees unbounded — O(n) memory for all employees** | `PayrollController.cs:127` |
| P-02 | P2 | PASS | Employee/payroll list endpoints paginated | `EmployeesController.cs:59`, `PayrollController.cs:72-79` |
| P-03 | P2 | PASS | Dashboard chart components use `dynamic()` + `{ ssr: false }` — code split | `DashboardPage.tsx:37-52` |
| P-04 | P2 | PASS | Redis distributed cache wired for feature flags + AI response cache | `Program.cs:82-91` |
| P-05 | P2 | WARN | `QiwaSyncWorker` poll interval 30s — no max-records-per-iteration cap, dead-letter backlog could overflow poll window | `QiwaSyncWorker.cs` |
| P-06 | P3 | PASS | Static assets: `max-age=31536000, immutable` cache headers | `next.config.ts` |
| P-07 | P3 | INFO | Frontend `.next/` build: 425MB on disk — production deployment will be significantly smaller | `du -sh .next` |

### Load Test Tooling Status

No k6/Artillery scripts currently exist in the repository.  
**Recommended:** Create `tests/load/` with the following k6 scripts before production:

```javascript
// tests/load/auth-smoke.js — safe local-only
import http from 'k6/http';
export const options = { vus: 10, duration: '30s' };
export default function () {
  http.post('http://localhost:5117/api/auth/login', JSON.stringify({
    email: 'test@invalid.com', password: 'wrongpass'
  }), { headers: { 'Content-Type': 'application/json' } });
}
// Expected: should rate-limit after implementation; currently returns 401 unlimited
```

```javascript
// tests/load/employee-list-smoke.js
import http from 'k6/http';
import { check } from 'k6';
export const options = { vus: 5, duration: '20s' };
export default function () {
  const res = http.get('http://localhost:5117/api/employees?page=1&pageSize=25', {
    headers: { Authorization: `Bearer ${__ENV.TOKEN}` }
  });
  check(res, { 'status 200': (r) => r.status === 200, 'has items': (r) => JSON.parse(r.body).items?.length > 0 });
}
```

---

## Defect Register — Full Prioritized List

### P0 — Launch Blockers

| # | Finding | File | Action |
|---|---|---|---|
| 1 | Account lockout never triggered by failed logins | `AuthService.cs:32-57` | Increment `FailedLoginCount` on failure; set `LockoutEnd = UtcNow + 15min` at threshold (e.g. 5); check at login start |

### P1 — Must Fix Before Client Demo

| # | Finding | File | Action |
|---|---|---|---|
| 2 | No rate limiting on auth endpoints | `Program.cs` | Add `builder.Services.AddRateLimiter()` with fixed-window policy on `/api/auth/login` |
| 3 | MFA is hardcoded `false` placeholder | `PlatformController.cs:1878` | Document as `MFA_NOT_IMPLEMENTED` in security disclosure; do not fake it |
| 4 | MailKit 4.8.0 vulnerable (GHSA-9j88-vvj5-vhgr) | `Zayra.Api.csproj` | Upgrade to MailKit ≥ 4.9.0 |
| 5 | Demo seeder always runs, creates live demo tenants | `Program.cs:269-278` | Guard with `if (config["SeedAdmin:SeedDemoData"] == "true")` check |
| 6 | Default JWT signing key placeholder in docker-compose | `docker-compose.yml:45` | Remove fallback default; fail fast if `JWT_SIGNING_KEY` is not set |

### P2 — Should Fix Before Production

| # | Finding | File | Action |
|---|---|---|---|
| 7 | `EnsureCreatedAsync()` — migrations not applied in prod | `Program.cs:264` | Replace with `dbContext.Database.MigrateAsync()` |
| 8 | No Next.js middleware.ts — client-side-only route guards | `frontend/` | Add `middleware.ts` to redirect unauthenticated requests to `/login` |
| 9 | CSP header missing | `Program.cs` | Add `Content-Security-Policy` to security headers middleware |
| 10 | Swagger always exposed | `Program.cs:249-250` | Wrap in `if (app.Environment.IsDevelopment())` |
| 11 | No React Error Boundary | `frontend/src/` | Add `ErrorBoundary` wrapper in `AppLayout.tsx` |
| 12 | Dashboard currency hardcoded to `AED` | `DashboardPage.tsx:64-66` | Use `useTenantSettings().currencyCode` |
| 13 | `QiwaSyncWorker` no per-iteration record cap | `QiwaSyncWorker.cs` | Add `Take(100)` limit on dead-letter requeue fetch |
| 14 | `MissingTableCreator` divergence risk | `Program.cs:265` | Migrate ad-hoc columns into proper EF migrations; remove `MissingTableCreator` |

### P3 — Improvements

| # | Finding | File | Action |
|---|---|---|---|
| 15 | 9 CS8602/CS8601 nullable dereference warnings | various | Fix with null guards |
| 16 | No audit log retention/archival policy | — | Define retention policy; add archive job |
| 17 | No k6/Artillery load test scripts | `tests/load/` | Create scripts per recommendation above |
| 18 | Payroll ProcessRun unbounded memory | `PayrollController.cs:127` | Add batch processing (chunk employees in groups of 200) |

---

## Files Inspected

```
backend-dotnet/Zayra.Api/Program.cs
backend-dotnet/Zayra.Api/Data/ZayraDbContext.cs
backend-dotnet/Zayra.Api/Infrastructure/Auth/AuthService.cs
backend-dotnet/Zayra.Api/Infrastructure/Auth/Pbkdf2PasswordHasher.cs
backend-dotnet/Zayra.Api/Infrastructure/Filters/FeatureFlagGuardFilter.cs
backend-dotnet/Zayra.Api/Infrastructure/Filters/SubscriptionGuardFilter.cs
backend-dotnet/Zayra.Api/Infrastructure/Qiwa/QiwaSyncWorker.cs
backend-dotnet/Zayra.Api/Infrastructure/Seed/DemoDataSeeder.cs
backend-dotnet/Zayra.Api/Controllers/PayrollController.cs
backend-dotnet/Zayra.Api/Controllers/PlatformController.cs
backend-dotnet/Zayra.Api/Controllers/EmployeesController.cs
backend-dotnet/Zayra.Api/Controllers/QiwaController.cs
backend-dotnet/Zayra.Api/Controllers/LeaveController.cs
backend-dotnet/Zayra.Api/Controllers/SaudiComplianceController.cs
backend-dotnet/Zayra.Api/Controllers/LocalizationController.cs
backend-dotnet/Zayra.Api/Models/User.cs
backend-dotnet/Zayra.Api/Zayra.Api.csproj
docker-compose.yml
appsettings.json
frontend/src/api/client.ts
frontend/src/layouts/AppLayout.tsx
frontend/src/layouts/Sidebar.tsx
frontend/src/layouts/TopBar.tsx
frontend/src/routes/navigation.ts
frontend/src/contexts/LocaleContext.tsx
frontend/src/views/DashboardPage.tsx
frontend/src/views/LoginPage.tsx
frontend/src/views/EmployeesPage.tsx
frontend/src/views/PerformancePage.tsx
frontend/src/views/SaudiComplianceDashboard.tsx
frontend/src/views/SaudiComplianceConfig.tsx
frontend/next.config.ts
frontend/middleware.ts (DOES NOT EXIST)
```

---

## Recommended PR Order Before Track A

```
PR-1: Security Hardening (P0+P1 — REQUIRED before any demo)
  - Implement account lockout in AuthService.LoginAsync
  - Add ASP.NET Core rate limiter on /api/auth/login
  - Upgrade MailKit to ≥ 4.9.0
  - Gate DemoDataSeeder behind SeedAdmin:SeedDemoData config flag
  - Remove JWT signing key fallback default from docker-compose
  - Add Content-Security-Policy header

PR-2: Infrastructure Correctness (P2 — REQUIRED before production deploy)
  - Replace EnsureCreatedAsync → MigrateAsync
  - Add frontend/middleware.ts route guard
  - Wrap Swagger in Development env guard

PR-3: UI Polish (P2)
  - Add React Error Boundary in AppLayout
  - Fix Dashboard currency to use useTenantSettings()
  - Add QiwaSyncWorker per-iteration record cap

PR-4: Track A — Saudi Regulatory Compliance
  (safe to start ONLY after PR-1 and PR-2 merge)
```

---

## Track A Readiness Checklist

| Requirement | Status |
|---|---|
| Tenant isolation proven | ✅ Global EF query filters cover all entities |
| QIWA schema exists | ✅ `QiwaTenantConnection`, `QiwaApiCredential`, `QiwaSyncLog` |
| WPS/SIF schema exists | ✅ `WPSFileBatch`, `SIFFileRecord` |
| GOSI fields on Company + Employee | ✅ `GosiEmployerId`, `GosiReference` |
| Saudi compliance dashboard UI | ✅ Built and deployed |
| Saudi compliance config UI (5 panels) | ✅ Built and deployed |
| Feature flag gate on Saudi compliance | ✅ `SaudiComplianceController.HasAnyGatingFeatureAsync` |
| EOSB calculation | ✅ Tenant-configurable, correct formula |
| Account lockout (P0 security gap) | ❌ **Not implemented** |
| Rate limiting on auth endpoints | ❌ **Not implemented** |
| Production migration runner | ❌ **EnsureCreated, not Migrate** |

**Conclusion:** The Saudi compliance infrastructure (schema, backend controller, frontend dashboard + config) is fully scaffolded and ready for Track A feature work. However, the platform **cannot be shown to a Saudi enterprise client or run in production** until the P0 account lockout and P1 rate-limiting issues are resolved. These are not Track A work — they are baseline security hygiene that should have shipped before the platform went live.

**Recommendation: Merge PR-1 (security hardening) before writing a single line of Track A code.**
