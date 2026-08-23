# OpsTrax Defect Register

Run ID: `UAT-20260821-1035`

| ID | Severity | Classification | Summary | Evidence | Status |
|---|---|---|---|---|---|
| DEF-001 | High | Environment | Staging UI is access-protected and local SHA `4653d2e...` differs from deployed staging SHA `979c142...`; safe personas/customer-data state remain unavailable | screenshot 002; live staging health | OPEN / BLOCKING |
| DEF-002 | High | Environment | Object storage and public TCP telemetry edge/hardware remain unverified; database-dependent local security diagnostics could not complete | Gate 0 report and diagnostic output | OPEN / BLOCKING |
| DEF-003 | Critical | Security | Telemetry installer copies a populated example allowlist when none exists, admitting a known-looking spoofable device identifier | `telematics/deploy/install.sh`; `telematics/deploy/imei-allowlist.example.txt` | OPEN |
| DEF-004 | High | Security | Telemetry gateway secret is accepted through a CLI argument and legacy shared gateway-secret configuration conflicts with protected-environment validation | `telematics/deploy/install.sh`; `docker-compose.yml`; `render.yaml`; `ConfigValidationService.cs` | OPEN |
| DEF-005 | High | Privacy | Durable edge outbox writes precise telemetry and device identifiers unencrypted to disk | `telematics/src/Opstrax.Telematics.Gateway/Forwarding/FileForwardOutbox.cs`; production settings | OPEN |
| DEF-006 | High | Authorization | Internal timeline/recommendation/control-tower endpoints lack the common internal-user/permission gate | `backend-dotnet/Controllers/EndpointMappings.cs` | OPEN |
| DEF-007 | Medium | Local security | Ignored `.env` and `DEMO_CREDENTIALS.md` are locally world-readable (`0644`) | File mode inspection | OPEN |
| DEF-008 | Medium | Documentation | Root README describes MySQL and demo seeding while active deployment uses PostgreSQL/RLS and protected production configuration | `README.md`; `docker-compose.yml`; `render.yaml` | OPEN |
| DEF-009 | High | Deployment | Staging CORS omitted the exact protected Vercel preview origin, causing platform login requests to fail in-browser | Preflight before/after; Render `Cors__AllowedOrigins` | FIXED / RETEST PASS |
| DEF-010 | High | Database drift | Replacement API failed readiness with 12 runtime route column violations and market/fleet identity contracts false | Render deploy logs; exact Neon readiness query | FIXED / RETEST PASS |
| DEF-011 | High | Onboarding | Deployed tenant invite UI displays only `Admin invite sent/reset`; SMTP is unavailable and the one-time activation link returned by the API is not visible, leaving the user Pending without a usable onboarding path | Platform tenant drawer; user row `Pending`, no password | OPEN / WORKAROUND APPLIED |
| DEF-012 | Critical | Authentication / schema | Every valid tenant password login returned HTTP 500 because protected staging lacked `company_security_settings`, user lockout columns, and `security_events` | Render correlations `3e793f...`, `49f639...`, `d2dfbc...`; SQLSTATE 42P01/42703 | FIXED / RETEST PASS |
| DEF-013 | Medium | Fleet UI | `/vehicles` and `/drivers` initially render an empty main region for several seconds without a loading explanation | Authenticated Company Admin browser sweep | CLOSED AS TIMING / UX OBSERVATION |
| DEF-014 | Medium | Validation UX | Vehicle VIN policy correctly rejects invalid length/check digit, but the UI discards the API detail and displays only `Request failed with status code 400` | Browser create vehicle; valid VIN retry passed | OPEN |
| DEF-015 | High | Privacy / UI | Driver roster renders encrypted license ciphertext (`enc:...`) instead of a protected display value | Browser-created `UAT-1035-D01` | OPEN / BLOCKING |
| DEF-016 | Critical | Dispatch / schema | Assignment board supporting data returns 500 because protected staging lacks `hos_records` | Render correlation `c4992bf...`; SQLSTATE 42P01 | OPEN / BLOCKING |
| DEF-017 | High | Routing / schema | Route Plans returns 500 because `routes.sla_risk` is absent | Render correlation `98d7123e...`; SQLSTATE 42703 | OPEN / BLOCKING |
| DEF-018 | Critical | Maintenance / schema | Work Orders dashboard returns 500 because `work_orders.asset_id` is absent | Render correlation `5d65bdf8...`; SQLSTATE 42703 | OPEN / BLOCKING |
| DEF-019 | Critical | Safety / schema | Incidents returns 500 because `safety_events.event_number` is absent | Render correlation `a0ad93f...`; SQLSTATE 42703 | OPEN / BLOCKING |
| DEF-020 | High | Audit integrity | Users & Roles reports 6 audit events today after real mutations, while Audit Logs reports zero total events | Authenticated browser comparison | OPEN / BLOCKING |
| DEF-021 | Medium | RBAC reporting | Company Admin role card reports 0 assigned users while the active user roster contains one Company Admin | Authenticated browser comparison | OPEN |
| DEF-022 | High | Telematics lifecycle | Device registration succeeds, but a complete valid installation form never submits and generates no API request; provider audit also returns 403 for the Company Admin session | Browser lifecycle and Render request observation | OPEN / BLOCKING |
| DEF-023 | High | Telematics revocation UX | `Archive Device` opens a browser confirmation that the automation bridge cannot complete and the device remains Active; emergency cleanup required the canonical revoke update directly in staging | Live browser retest plus Neon verification for `UAT-1035-GPS01` | OPEN; CREDENTIALS REVOKED BY WORKAROUND |
| DEF-024 | High | Portal routing / least privilege | A bound Customer Portal User authenticates into `/live-dashboard` and receives the internal application shell with 35 internal modules instead of being routed to `/customer-portal`; protected APIs tested returned 403, but internal navigation is exposed | Live login as `customer-persona-uat-20260821-1035@opstrax.invalid` | OPEN / BLOCKING |
| DEF-025 | High | RBAC / direct URL | Dispatcher can open `/user-management` and render the Users & Roles governance surface and audit navigation by direct URL; mutation controls are disabled but the route is not denied | Live login as `dispatcher-uat-20260821-1035@opstrax.invalid` | OPEN / BLOCKING |
| DEF-026 | Critical | Driver portal / runtime schema | Driver login routes correctly to `/driver`, but the linked driver dashboard changes from 403 before binding to HTTP 500 after binding to `UAT-1035-D01`; the internal-route redirect back to `/driver` passes | Live Driver persona browser retest | OPEN / BLOCKING |
| DEF-027 | High | Customer portal data | After binding the portal user to customer `UAT-1035-C01`, owned job `UAT-1035-J01` is still absent from Your Shipments and feedback remains disabled | Live Customer Portal browser retest plus database ownership verification | OPEN / BLOCKING |

## Retest status

DEF-009 and DEF-010 were repaired and passed live browser/API/database retest. DEF-011 was reproduced on the deployed build; the disposable UAT-only administrator was activated directly with the application's PBKDF2 format so testing can continue. DEF-003 through DEF-007 remain open source findings; applicability to the older deployed staging SHA still requires exact-source comparison.

DEF-012 was repaired by Stage 83, first verified on temporary Neon branch `br-plain-darkness-aw16uj1l`, then applied to staging. The branch was deleted after the authenticated tenant login passed and landed on `/live-dashboard`.

DEF-013 was not a CRUD blocker after allowing the delayed data load to complete. Browser creation subsequently passed for vehicle `UAT-1035-V01`, driver `UAT-1035-D01`, customer `UAT-1035-C01`, and job `UAT-1035-J01`. Stage 84 was drafted locally for the protected driver/HOS runtime contract, but its isolated Neon preparation failed on the migration service's procedural-block parser; no Stage 84 change reached staging.

Four lower-privilege UAT accounts were created through the live Users & Roles UI (Dispatcher, Fleet Manager, Driver, and Customer Portal User), bringing tenant `T-C221B5BA` to five active users. Dispatcher, Fleet Manager, Driver, and Customer Portal authentication all passed. The resulting authorization and portal failures are tracked as DEF-024 through DEF-027.

Tenant isolation received a live negative test with separately provisioned tenant `T-F1A74082`. Its administrator authenticated successfully, saw an empty vehicle registry, could not see `UAT-1035-V01` through `/vehicles/1/live`, and received a plan-entitlement denial for `/jobs`. No tenant-A record was disclosed. The result is a runtime PASS for the tested vehicle/job surfaces, not a substitute for the still-blocked complete RLS integration suite.

The exposed test device `UAT-1035-GPS01` is now `Revoked`; `api_key_hash`, current HMAC material, and previous credential material were cleared and verified in staging at `2026-08-21T20:30:07.850Z`.
