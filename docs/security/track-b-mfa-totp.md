# Track B PR-1 — MFA / TOTP Security Foundation

**Status:** Implemented  
**PR:** Track B PR-1  
**Date:** 2026-06-17

---

## Summary

Full RFC 6238 TOTP-based multi-factor authentication for both tenant users and platform admins. The backend is the single source of truth; the frontend never issues tokens until TOTP is verified.

---

## Architecture

### Login flow (tenant users)

```
POST /api/auth/login
  │
  ├─ Phase 1-4: structural checks, lockout, password verify (unchanged)
  │
  ├─ Phase 4b: MFA check
  │     IF user.MFAEnabled AND MfaSecretEncrypted is set:
  │       → Issue MfaChallengeToken (5-min, single-use, stored as SHA-256 hash)
  │       → Return { mfaRequired: true, challengeToken, expiresInSeconds: 300 }
  │       ← Tokens NOT issued; full session not created
  │
  └─ Phase 5: normal path (MFA not enabled)
        → Issue access + refresh tokens (unchanged)

POST /api/auth/mfa/challenge/verify
  │   Body: { challengeToken, totpCode }
  ├─ Verify challenge token (hash lookup, expiry, used-at-utc)
  ├─ Decrypt MfaSecretEncrypted with IDataProtector
  ├─ RFC 6238 TOTP verify (±1 window)
  └─ If valid: consume token, update MfaLastVerifiedAtUtc, issue access + refresh tokens
```

### Login flow (platform admins)

Same phases — after password verify, if `PlatformUser.MfaEnabled` is set:
- Issue platform MFA challenge → client POSTs to `/api/platform/auth/mfa/challenge/verify`
- On success: issue platform JWT (8h, PlatformAdmin role)

**Known gap:** Env-var fallback platform admin (`PLATFORM_ADMIN_EMAIL` / `PLATFORM_ADMIN_PASSWORD`) cannot have MFA — no DB record. This path is for emergency/bootstrap only; DB platform users are the production path.

---

## Data Model

### `User` (tenant users)

| Field | Type | Description |
|---|---|---|
| `MFAEnabled` | `bool` | Existing field — set true when TOTP setup is confirmed |
| `MfaSecretEncrypted` | `varchar(1024)?` | TOTP secret encrypted by `IDataProtector` |
| `MfaConfiguredAtUtc` | `datetime?` | When MFA was enabled |
| `MfaLastVerifiedAtUtc` | `datetime?` | Last successful TOTP verification |
| `MfaFailedCount` | `int` | Failed TOTP attempts (cleared on success) |

### `PlatformUser`

| Field | Type | Description |
|---|---|---|
| `MfaEnabled` | `bool` | True once TOTP setup confirmed |
| `MfaSecretEncrypted` | `varchar(1024)?` | Encrypted TOTP secret |
| `MfaConfiguredAtUtc` | `datetime?` | When MFA was enabled |

### `SecuritySetting`

| Field | Type | Description |
|---|---|---|
| `MfaRequired` | `bool` | When true, all tenant users must have MFA to log in |

### `MfaChallengeToken` (new table)

| Field | Type | Description |
|---|---|---|
| `Id` | `guid` | PK |
| `UserId` | `guid?` | Tenant user (null for platform admin) |
| `PlatformUserId` | `guid?` | Platform user (null for tenant user) |
| `TenantId` | `guid?` | Tenant scope |
| `TokenHash` | `varchar(128)` | SHA-256 of raw token, unique index |
| `ExpiresAtUtc` | `datetime` | 5 minutes after creation |
| `CreatedByIp` | `varchar(64)` | IP for audit |
| `UsedAtUtc` | `datetime?` | Set on first use; null = still valid |

---

## TOTP Implementation

- **Algorithm:** HMAC-SHA1 (RFC 4226 / RFC 6238)
- **Period:** 30 seconds
- **Digits:** 6
- **Window:** ±1 step (accommodates up to 30s clock skew)
- **Secret:** 20 random bytes, base32-encoded (RFC 4648)
- **Secret storage:** `IDataProtector.Protect()` with purpose `"Zayra.Mfa.TotpSecret.v1"`
- **Library:** Pure .NET — no external package required

### Secret handling rules

The TOTP secret is:
- **Never** stored in plaintext
- **Never** returned in any API response after setup confirmation
- **Never** included in audit log payloads
- **Never** logged
- Only read inside `MfaService` to verify a TOTP code

---

## API Endpoints

### Tenant user MFA

| Method | Path | Auth | Description |
|---|---|---|---|
| `POST` | `/api/auth/mfa/setup` | `Bearer` (authenticated) | Returns provisioning URI (shown once) |
| `POST` | `/api/auth/mfa/verify-setup` | `Bearer` | First code confirms setup, persists encrypted secret |
| `POST` | `/api/auth/mfa/challenge/verify` | None (challenge = auth) | Verifies challenge token + TOTP code → returns tokens |
| `POST` | `/api/auth/mfa/disable` | `Bearer` | Disables MFA (requires valid TOTP code) |

### Platform admin MFA

| Method | Path | Auth | Description |
|---|---|---|---|
| `POST` | `/api/platform/auth/mfa/setup` | Platform JWT | Returns provisioning URI |
| `POST` | `/api/platform/auth/mfa/verify-setup` | Platform JWT | Confirms setup |
| `POST` | `/api/platform/auth/mfa/challenge/verify` | None | Challenge + TOTP → platform JWT |
| `POST` | `/api/platform/auth/mfa/disable` | Platform JWT | Disable MFA |

### Security settings (tenant MFA policy)

```
GET  /api/access/security-settings  → includes mfaRequired: bool
PUT  /api/access/security-settings  → body: { mfaRequired?: bool, … }
```

---

## Frontend

### Login flow

`LoginPage.tsx` has a new `'mfa'` mode in addition to `'login'`, `'forgot'`, `'reset'`.

`AuthContext.tsx` exposes:
- `mfaPending: MfaPendingState | null` — populated when backend returns `mfaRequired: true`
- `verifyMfaChallenge(totpCode: string): Promise<void>` — completes the challenge

The login page automatically switches to the MFA step when `mfaPending` is set. Back-to-login clears the pending state.

### MFA setup

Call `authApi.mfaSetup()` → show provisioning URI as QR code. Then call `authApi.mfaVerifySetup(tempSecret, code)` with the first TOTP code from the authenticator app.

---

## Security Properties

| Constraint | Status |
|---|---|
| MFA secrets never in logs or API responses | ✅ |
| Challenge token is single-use (UsedAtUtc) | ✅ |
| Challenge token expires in 5 minutes | ✅ |
| Challenge token stored as SHA-256 hash | ✅ |
| Full tokens NOT issued before TOTP verified | ✅ |
| Platform admin MFA mandatory enforced (DB users) | ✅ |
| Tenant MFA policy (`MfaRequired`) enforceable | ✅ |
| Existing lockout / rate limiting unchanged | ✅ |
| Existing audit logging unchanged | ✅ |
| IDataProtector purpose-scoped key rotation support | ✅ |

---

## Tests

**File:** `backend-dotnet/Zayra.Api.Tests/MfaTests.cs` — 22 tests

| Test | Category |
|---|---|
| `TotpService_GenerateBase32Secret_IsValidBase32` | TOTP unit |
| `TotpService_ProvisioningUri_HasCorrectScheme` | TOTP unit |
| `TotpService_EncryptDecrypt_RoundTrip` | TOTP unit |
| `TotpService_Verify_CurrentStep_Passes` | TOTP unit |
| `TotpService_Verify_WrongCode_Fails` | TOTP unit |
| `TotpService_Verify_WindowStep_Minus1_Passes` | TOTP unit |
| `TotpService_Verify_WindowStep_Plus1_Passes` | TOTP unit |
| `MfaEnabled_Login_CreatesChallengeToken_NotTokens` | Challenge flow |
| `VerifyChallenge_ValidCodeAndToken_ReturnsUser` | Challenge flow |
| `VerifyChallenge_InvalidCode_ReturnsNull_IncrementsFailCount` | Challenge flow |
| `VerifyChallenge_ExpiredToken_ReturnsNull` | Challenge flow |
| `VerifyChallenge_AlreadyUsedToken_ReturnsNull` | Challenge flow |
| `TenantIsolation_ChallengeForUserB_CannotUnlockUserA` | Tenant isolation |
| `MfaSecretEncrypted_NeverAppearsInChallengeToken` | Secret masking |
| `SetupFlow_ValidCode_EnablesMfa` | Setup flow |
| `SetupFlow_WrongCode_DoesNotEnableMfa` | Setup flow |
| `SetupFlow_ProvisioningUri_DoesNotExposeSecretAfterVerify` | Secret masking |
| `DisableFlow_ValidCode_ClearsMfa` | Disable flow |
| `DisableFlow_WrongCode_KeepsMfa` | Disable flow |
| `PlatformUser_MfaChallenge_HappyPath` | Platform MFA |
| `PlatformUser_MfaChallenge_WrongCode_ReturnsNull` | Platform MFA |
| `SecuritySetting_MfaRequired_DefaultsFalse` | Policy |
| `SecuritySetting_MfaRequired_CanBeSetTrue` | Policy |
| `VerifyChallenge_Success_ClearsMfaFailedCount` | Fail count |

---

## Enterprise Benchmark

| Capability | Workday / SAP | Jisr / Bayzat | Zayra (current) |
|---|---|---|---|
| TOTP-based MFA | ✅ | ⚠️ SMS only | ✅ RFC 6238 |
| Platform admin MFA | ✅ | N/A | ✅ DB users |
| Tenant-level MFA policy | ✅ | ⚠️ | ✅ |
| Single-use challenge tokens | ✅ | ⚠️ | ✅ |
| Secret encrypted at rest | ✅ | ⚠️ | ✅ |
| Clock skew tolerance | ✅ | ✅ | ✅ ±1 step |
| Recovery codes | ✅ | ⚠️ | ❌ Pending (P2) |

### Gaps

**P2 — Enhancement:**
- Recovery codes (backup codes when authenticator app lost)
- Remember-this-device option (30-day session)
- Email/SMS fallback OTP for non-TOTP devices
