# OpsTrax — UAT Test Pack (Dev → UAT Gate)

**Prepared:** 2026-08-03 · **Owner:** Engineering (CTO office) · **Status:** Ready for UAT execution

This pack is the entry criteria evidence for moving OpsTrax from development into UAT. Every
scenario uses **real seeded data** (no mocks) and is written to be executed either through a
browser (Claude Chrome extension / manual) or directly against the API. Scenarios are ordered
by priority; run all **P0** before sign-off.

## How to run

- **UI/browser:** start the SPA + API, then follow the steps. Routes are relative to the app origin.
- **API:** `POST`/`GET` against the .NET API (`:8088` local, Render URL in prod). Auth is a bearer
  token from the login response; the **platform** portal uses a *separate* axios instance and
  storage key (`opstrax.platform.session.v1`) from the tenant app.
- **DB checks:** the local test/dev Postgres is `127.0.0.1:5433/opstrax_local`. Never run the
  write/seed suites against a production database.

## Demo credentials (seeded)

| Persona | Email | Password | Scope |
|---|---|---|---|
| Platform Super Admin (dev) | `platform@opstrax.io` | `Platform@12345` | Platform control plane |
| Tenant Fleet Manager | `admin@meridian.demo` | `MeridianDemo!23` | MERIDIAN-DEMO (company_id **1692**), full ops |
| Dispatcher (South branch) | `dispatch@meridian.demo` | `MeridianDemo!23` | Branch-scoped dispatch |
| Driver | `driver@meridian.demo` | `MeridianDemo!23` | Driver portal only (MER-DRV-1) |
| Read-only Auditor | `auditor@meridian.demo` | `MeridianDemo!23` | No dispatch tokens |
| Customer Portal | `portal@acme.demo` | `MeridianDemo!23` | Customer MER-ACME only |

> Staging/prod platform admin uses `PLATFORM_SUPERADMIN_EMAIL` / `PLATFORM_SUPERADMIN_PASSWORD`.

## Defects fixed in this candidate (regression-test these first)

| ID | Sev | Fix | Verify with |
|---|---|---|---|
| TELE-CONTRACT-001 | P1 | Device Health/GPS/Diagnostics rendered blank (API camelCase vs UI snake_case). Added additive key-normalizer. | UAT-DEV-01 |
| TELE-001 | P1 | Recovery Resolve/Mark now gate on real ELD status (was derived → 409). | UAT-DEV-02 |
| TELE-002 | P1 | Assign sent vehicle *code* as `vehicleId` (→400). Now sends numeric id. | UAT-DEV-03 / assign flow |
| TELE-003 | P2 | Mark/Resolve gated on `maintenance:manage` (backend 403). Now `compliance:update`. | UAT-DEV-02 as maintenance role |
| SEC-INTEG-ENTITLEMENT-01 | P2 | `/api/integrations/*` now enforces the Integrations module entitlement server-side. | UAT-DEV-04 (+ non-entitled tenant → 403) |
| DEV-PROVISION-500 | P3 | Missing `deviceSerial` now returns 400, not 500. | `POST /api/telemetry/devices/provision {}` → 400 |
| PlatformLogin MFA | P2 | Wrong MFA code no longer collapses the code input. | UAT-PLAT-03 step 4 |

---

## P0 — must pass for UAT sign-off

### UAT-PLAT-01 — Platform Admin login → control plane
**Data:** `platform@opstrax.io` / `Platform@12345`.
1. Open `/platform/login` in a fresh browser (no `opstrax.platform.session.v1`).
2. Enter email + password, Sign in.
3. Confirm redirect to `/platform`; tenant app session untouched (separate store).
**Expected:** 200; `role.key='platform_super_admin'`; a `platform.login` row in `platform_audit_log`.

### UAT-PLAT-02 — Platform lockout after 5 failures (email+IP, 15-min window)
**Data:** `lockout-uat@opstrax.test` + wrong password ×5, then 6th attempt.
1. `POST /api/platform/auth/login` ×5 with wrong password → 401 each.
2. 6th attempt (any password) → **429 "Too many failed attempts"**.
3. UI shows the lockout banner; `platform_audit_log` has `platform.login_locked`.
**Expected:** 1–5 → 401; 6 → 429; clears 15 min after last failure.

### UAT-PLAT-03 — Platform MFA (TOTP) enforced
**Data:** enroll → verify current TOTP → login with code; wrong-code probe `000000`.
1. `POST /api/platform/auth/mfa/enroll`; generate TOTP (RFC 6238) → `.../mfa/verify {code}` → 200.
2. Log out; login with email+password only → 401 `mfa_required`; UI reveals the code field.
3. Login with **wrong** `mfaCode` (`000000`) → 401 `invalid_mfa_code`; **the code field must stay visible** (regression: PlatformLogin MFA fix).
4. Login with a fresh valid code → 200.
**Expected:** missing code not counted; wrong code counted toward lockout; correct code → 200.

### UAT-TEN-01 — Tenant admin login + correct role landing
**Data:** `admin@meridian.demo` / `MeridianDemo!23` (company 1692, Fleet Manager).
1. `/login` → submit. 2. 200 with permissions incl. `dispatch:update`, `finance:view`. 3. Lands on the ops shell, **not** `/driver`.
**Expected:** back-office landing; `failed_login_attempts` reset to 0.

### UAT-TEN-03 — Driver portal isolation
**Data:** `driver@meridian.demo` / `MeridianDemo!23` (MER-DRV-1).
1. Login → auto-redirect to `/driver`.
2. Manually open `/dispatch`, `/vehicles`, `/iot-devices`, `/invoices` → each access-denied.
3. `/driver/assignments` shows only MER-DRV-1 loads.
4. `GET /api/dispatch/assignments` with driver token → 403/empty.
**Expected:** confined to `/driver`; no cross-role data.

### UAT-ISO-01 — Cross-tenant isolation (read-by-id) *(P0 leak check)*
**Data:** Meridian token (1692); probe foreign IDs owned by company 1.
1. `GET /api/dispatch/assignments/1`, `/api/vehicles/1`, `/api/jobs/1`, `/api/telemetry/devices/1` → **404 each**.
2. `GET /api/dispatch/assignments` → every `company_id` is 1692.
3. `POST /api/eld/devices/<foreign-id>/mark-malfunction` → 404.
**Expected:** all cross-tenant reads 404; no list leaks foreign rows. *Any 200 with foreign data = P0.*

### UAT-DISP-01 — Dispatch full lifecycle (canonical lowercase tokens)
**Data:** jobId MER-JOB-2, vehicle MER-TRK-2, driver MER-DRV-5.
1. `/dispatch` → create assignment → status `assigned`.
2. `accept` → `accepted`; then `status` through `en_route_pickup → arrived_pickup → loaded → in_transit → arrived_delivery`.
3. From `arrived_delivery`, `POST .../proof` (POD) → `delivered`.
**Expected:** full canonical chain; `delivered` reachable **only** via proof; audit rows written.

### UAT-DEV-01 — Device Health renders live data *(regression: TELE-CONTRACT-001)*
**Data:** MER-ELD-1 (Diagnostic), enriched ELD-1692-001..005.
1. Login → `/iot-devices`.
2. Table lists **real** serial, assigned vehicle/driver, connection status, health score — **no blank cells / seed placeholders**.
3. Open a device → `GET /api/telemetry/devices/{id}`; `connectionStatus` derived from real signals (Diagnostic → "Needs attention", stale >900s → "Offline").
4. Tab counts (All/Unassigned/Offline/Attention/Providers/Firmware) reflect real fields.
**Expected:** live rows; correct derivations; **Firmware tab lists devices with current versions (no longer always-empty)**.

### UAT-DEV-02 — Mark attention → resolve (two-stage Active gating) *(regression: TELE-001/003)*
**Data:** ELD-1692-001 (Active, has credentials). `mark {rowVersion, malfunctionCode:'PWR-01', malfunctionDescription:'Power fault flagged during pre-trip'}`.
1. Select an **Active** device; note `rowVersion`.
2. "Needs Attention" → `POST .../mark-malfunction` → 200, status `Malfunction`, rowVersion+1.
3. Re-submit the **same** rowVersion → 409 (optimistic concurrency).
4. With a recent healthy provider sync + valid creds, "Resolve" → `POST .../resolve-malfunction {rowVersion, resolutionEvidence:'…'}` → 200 status **Active**.
5. Resolve MER-ELD-1 (no creds) → stays **Diagnostic** with the "remains Diagnostic until a recent healthy provider sync" message.
**Expected (post-fix):** the Resolve/Needs-Attention button is enabled for the correct real status (no spurious 409); maintenance-only roles no longer see an enabled button that 403s.

---

## P1

### UAT-TEN-02 — Tenant lockout (5 → 30-min lock)
`admin@meridian.demo` wrong password ×5 → account `locked_until = now+30m`; correct password while locked still returns the **generic** 401 (no lockout disclosure). Reset: `UPDATE users SET locked_until=NULL, failed_login_attempts=0 …`.

### UAT-ISO-02 — Branch scoping
`dispatch@meridian.demo` (branch MER-SOUTH) lists only South assignments; acting on a North assignment (`POST .../status`) → 404.

### UAT-ISO-03 — Customer portal isolation
`portal@acme.demo` sees only Acme (MER-ACME) shipments; `/iot-devices` returns 0 devices; back-office denied.

### UAT-DISP-02 — Dispatch guards
On `in_transit`: `status {delivered}` → 409 (proof required). On `accepted`: `{delivered}` and `{in_transit}` → 409 (invalid transition). Open exception then attempt `arrived_delivery` → 409 (exception can only resume to prior status or cancel).

### UAT-DISP-03 — Dispatch RBAC
`auditor@meridian.demo` (no dispatch tokens): `POST .../status` and `.../accept` → 403; `/dispatch` route denied.

### UAT-PLAT-04 — Tenant suspend kills logins + sessions
Platform admin `POST /api/platform/tenants/1692/status {Suspended}` → `admin@meridian.demo` correct password now 401; existing sessions revoked. Reactivate → login restored.

---

## P2

### UAT-DEV-03 — Unassign device
`admin@meridian.demo`: select MER-ELD-1 (assigned) → "Unassign" → `POST .../assign {vehicleId:null, driverId:null}` → device moves to **Unassigned** tab. Portal roles get "Permission denied" with no mutation. *(Regression: the assign path now sends numeric ids — TELE-002.)*

### UAT-DEV-04 — Provider sync + entitlement *(regression: SEC-INTEG-ENTITLEMENT)*
`admin@meridian.demo` → `/iot-devices` → Providers tab lists connectors with real status; "Sync" → `POST /api/integrations/{id}/sync` → success + updated `lastSyncAt`; unknown-provider device shows an honest "no matching connector" audit.
**Additional entitlement check:** as a `package_allowlist` tenant **without** the Integrations add-on, `GET /api/integrations` and `.../sync` now return **403 "Feature not entitled"** (previously 200 — the commercial bypass). Legacy-allow tenants are unaffected.

---

## Results log

| Scenario | Result (Pass/Fail) | Tester | Date | Notes |
|---|---|---|---|---|
| UAT-PLAT-01 | | | | |
| UAT-PLAT-02 | | | | |
| UAT-PLAT-03 | | | | |
| UAT-TEN-01 | | | | |
| UAT-TEN-03 | | | | |
| UAT-ISO-01 | | | | |
| UAT-DISP-01 | | | | |
| UAT-DEV-01 | | | | |
| UAT-DEV-02 | | | | |
| UAT-TEN-02 | | | | |
| UAT-ISO-02 | | | | |
| UAT-ISO-03 | | | | |
| UAT-DISP-02 | | | | |
| UAT-DISP-03 | | | | |
| UAT-PLAT-04 | | | | |
| UAT-DEV-03 | | | | |
| UAT-DEV-04 | | | | |

## Known automated-coverage gaps (recommended before/with UAT)
- `POST /api/integrations/{id}/sync` has **no** automated test — add one (now also covering the entitlement gate).
- Platform-login **MFA** branch has no HTTP-level test (only tenant-side unit tests).
- Dispatch full happy-path is unit-tested as a pure function but has **no** end-to-end HTTP walk with company/branch scoping + the delivered-requires-proof gate.
- Cross-tenant GET-by-id 404 has no explicit per-endpoint regression asserting a foreign id → 404 (covered structurally by RLS tests only).
