# Security Hardening PR-1

**Date:** 2026-06-17  
**Engineer:** Senior .NET / SaaS Security  
**Status:** COMPLETE — all tests pass (124/124)  
**Branch:** main

---

## Scope

Minimum security fixes required before Track A Saudi Regulatory Compliance work begins. No QIWA/WPS/GOSI implementation in this PR.

---

## Changes Made

### 1. Account Lockout — `AuthService.cs`

**Problem:** `User.FailedLoginCount`, `User.IsLocked`, and `User.LockoutEnd` existed in the DB schema but `LoginAsync` never read or wrote them. An attacker could brute-force passwords indefinitely.

**Fix:** Restructured `LoginAsync` into 5 phases:

| Phase | Action |
|-------|--------|
| 1 | Structural validation (tenant slug, user existence). No lockout counting during pre-checks. |
| 2 | Load per-tenant `SecuritySetting` from DB. Fallback: `maxAttempts=5`, `lockoutMinutes=15`. |
| 3 | If `LockoutEnd > UtcNow` → reject immediately, audit `auth.login_blocked_lockout`. |
| 4 | Password mismatch → increment `FailedLoginCount`; lock if threshold reached, audit `auth.account_locked` or `auth.login_failed`. |
| 5 | Success → reset `FailedLoginCount=0`, `IsLocked=false`, `LockoutEnd=null`, issue tokens. |

**Lockout policy per tenant** (from `SecuritySetting` table):
- Default max failed attempts: **5**
- Default lockout duration: **15 minutes**

Locked accounts are blocked even with the correct password until `LockoutEnd` expires. All failure paths return the same generic message: `"Invalid email, password, or tenant."` (prevents user enumeration).

New audit events: `auth.account_locked`, `auth.login_blocked_lockout`.

---

### 2. Rate Limiting — `Program.cs`, `AuthController.cs`, `PlatformController.cs`

**Problem:** No rate limiting on any auth endpoint. Brute-force and credential-stuffing had no server-side throttle.

**Fix:** Added `AddRateLimiter()` with three per-IP fixed-window policies:

| Policy | Endpoint | Default Limit |
|--------|----------|---------------|
| `auth_login` | `POST /api/auth/login` | 10 req/60s per IP |
| `auth_refresh` | `POST /api/auth/refresh` | 30 req/60s per IP |
| `platform_login` | `POST /api/platform/auth/login` | 5 req/60s per IP |

Rejected requests return **HTTP 429**. All limits are configurable via `appsettings.json`:

```json
"RateLimit": {
  "LoginPermitLimit": 10,
  "LoginWindowSeconds": 60,
  "RefreshPermitLimit": 30,
  "RefreshWindowSeconds": 60,
  "PlatformLoginPermitLimit": 5,
  "PlatformLoginWindowSeconds": 60
}
```

`app.UseRateLimiter()` placed immediately after `app.UseCors("zayra")` in the middleware pipeline.

Uses `System.Threading.RateLimiting` (built into .NET 8 — no extra package).

---

### 3. Platform Admin MFA — `PlatformController.cs`

**Problem:** Platform admin login response contained `hasMfaEnabled = false` with a comment marking it as a placeholder. Clients receiving `false` have no way to distinguish "MFA is off" from "MFA not implemented."

**Fix:** Added `mfaStatus = "not_implemented"` field alongside `hasMfaEnabled = false`. Comment clarifies that `User.MFAEnabled` schema field exists but TOTP login challenge is not yet implemented.

Clients can now detect the not-implemented state and avoid rendering MFA UI that would never activate.

---

### 4. MailKit Vulnerability — `Zayra.Api.csproj`

**Problem:** MailKit 4.8.0 was in use. GHSA-9j88-vvj5-vhgr (moderate severity) affects this version.

**Fix:** Upgraded `MailKit` to **4.16.0** (patched release).

```xml
<PackageReference Include="MailKit" Version="4.16.0" />
```

---

### 5. Demo Seeder Production Gate — `Program.cs`

**Problem:** `DemoDataSeeder.SeedAsync()` was called unconditionally on every startup. If deployed to production, it would silently seed test data.

**Fix:** Demo seeder now requires explicit opt-in via either:
- Environment variable: `SEED_DEMO_DATA=true`
- Configuration: `SeedAdmin:SeedDemoData=true` in appsettings

Default is **disabled**. Startup logs: `"Demo data seeding: DISABLED (environment=Production)"`.

---

### 6. Swagger in Production — `Program.cs`

**Problem:** Swagger UI and OpenAPI spec were registered unconditionally, exposing the full API surface and schema in production.

**Fix:** Wrapped `app.UseSwagger()` and `app.UseSwaggerUI()` in `if (app.Environment.IsDevelopment())`.

---

### 7. `EnsureCreatedAsync` Risk — Documented, Not Changed

**Risk noted:** `DbContext.Database.EnsureCreatedAsync()` at startup creates the schema from the EF model snapshot. It does **not** apply EF Core migrations. When migrations are introduced, they will be silently skipped on databases that were created with `EnsureCreated`.

**Decision:** Behavior left unchanged in this PR. Replacing with `MigrateAsync()` requires a coordinated migration baseline pass and is a separate tracked item (post-PR-1).

---

## Tests Added — `AuthServiceTests.cs`

| Test | Verifies |
|------|----------|
| `PasswordHasher_VerifiesValidPassword_AndRejectsInvalidPassword` | Hasher round-trip |
| `LoginAndRefresh_IssueTokensAndRotateRefreshToken` | Happy path: tokens issued, refresh rotated, audit logged |
| `Login_IncrementsFailedLoginCount_OnPasswordMismatch` | Counter increments, no lockout yet |
| `Login_ResetsFailedLoginCount_OnSuccessfulLogin` | Counter reset to 0 on success |
| `Login_LocksAccount_AfterMaxFailedAttempts` | `IsLocked=true`, `LockoutEnd` set, `auth.account_locked` audit |
| `Login_BlocksLockedAccount_WithCorrectPassword` | Correct password blocked while locked, counter does not grow |
| `Login_AllowsLoginAfterLockoutExpires` | Login succeeds when `LockoutEnd` is in the past |
| `Login_ReturnsIdenticalErrorMessage_ForUnknownUserAndWrongPassword` | Same error string prevents user enumeration |
| `DemoSeeder_ShouldNotRunInProduction_WhenEnvVarNotSet` | Seeder gate logic: absent env var → disabled |
| `DemoSeeder_ShouldRun_WhenEnvVarIsTrue` | Seeder gate logic: `SEED_DEMO_DATA=true` → enabled |

---

## Build & Test Results

```
dotnet restore   → OK (1 expected NU1603 warning for QuestPDF version float)
dotnet build     → Build succeeded. 6 Warning(s), 0 Error(s)
dotnet test      → Passed! Failed: 0, Passed: 124, Skipped: 0, Total: 124
```

Warnings in build output are pre-existing nullable reference warnings in `QiwaSyncWorker.cs` and `ApplicationsController.cs` — not introduced by this PR.

---

## Remaining Risks (Post-PR-1)

| Risk | Priority | PR |
|------|----------|----|
| `EnsureCreatedAsync` → `MigrateAsync` migration | P1 | PR-2 |
| `frontend/middleware.ts` route guard missing | P1 | PR-2 |
| MFA TOTP challenge not implemented (`mfaStatus = "not_implemented"`) | P1 | PR-3 |
| React Error Boundary absent in AppLayout | P2 | PR-2 |
| Dashboard hardcoded `AED` currency | P2 | PR-2 |
| Rate limiting bypasses reverse proxy (X-Forwarded-For not read) | P2 | PR-2 |
| Nullable warnings in QiwaSyncWorker, ApplicationsController | P3 | Cleanup |

---

## Configuration Reference

### Rate Limits (appsettings.json)
```json
"RateLimit": {
  "LoginPermitLimit": 10,
  "LoginWindowSeconds": 60,
  "RefreshPermitLimit": 30,
  "RefreshWindowSeconds": 60,
  "PlatformLoginPermitLimit": 5,
  "PlatformLoginWindowSeconds": 60
}
```

### Demo Seeder Gate (environment)
```
SEED_DEMO_DATA=true       # env var (Docker/k8s)
SeedAdmin:SeedDemoData=true  # appsettings override
```

### Account Lockout Policy (DB — per tenant)
```sql
UPDATE security_settings 
SET max_failed_login_attempts = 5, lockout_duration_minutes = 15 
WHERE tenant_id = '...';
```
