# Developer Findings Revalidation — 2026-09-15

## Scope

This is a source-and-test revalidation of the 80-item developer list supplied on 2026-09-15 against local branch `redesign/customer-finance-workspaces-local` at `90b9ea9` plus the existing uncommitted Customer/Sales/Finance redesign. It does not assert that an older deployed build had the same posture.

Verdict meanings:

- **Resolved** — current source and/or an executable test contradicts the reported defect.
- **Open** — current source still contains the reported defect or materially equivalent behavior.
- **Resolved by removal** — the affected retired implementation was proven unused and deleted with its obsolete CI path.
- **Locator needed** — the description does not identify an endpoint/file/query precisely enough to prove or disprove the claim without guessing. No findings remain in this state after the locator follow-up.
- **Observation** — no current functional defect is claimed.

## Executive result

| Verdict | Count |
|---|---:|
| Resolved / observation | 80 |
| Open in current code | 0 |
| Exact locator or reproduction needed | 0 |
| **Total** | **80** |

There is **no currently reproduced open defect** in the live .NET path. The two maintenance findings passed a production-shaped two-company HTTP and RLS rehearsal. All twelve findings that were open during the first pass are now resolved locally. The eight descriptions that originally lacked locators were matched to current routes, symbols, queries, and regression evidence in the follow-up below; five required bounded corrections, two were already protected, and one remains a maintainability observation rather than a functional defect. The retired Node API and its dormant defects were removed after dependency and release-path verification.

## Executable evidence

### Production-shaped maintenance isolation rehearsal

The local rehearsal created a disposable database, applied the owner migration chain, started the real API in `Production`, used the restricted `opstrax_app` and `opstrax_system` identities, and verified forced RLS. Results:

- 29 authenticated HTTP assertions passed.
- Company A and Company B could read their own maintenance records.
- Cross-company and cross-branch detail guesses returned `404` without data.
- Missing token, invalid token, and missing maintenance permission returned `401/403`.
- Tenant query-parameter overrides were ignored.
- 60 interleaved concurrent reads retained tenant isolation.
- An unscoped restricted-role query returned zero rows after pooled connection reuse.

Relevant source/tests:

- `backend-dotnet/Controllers/EndpointMappings.cs` — `MaintenanceItems` and `MaintenanceDetail` require `maintenance:view` and bind `company_id` plus branch scope.
- `backend-dotnet.Tests/JourneyEndpointTenantIsolationPostgresTests.cs` — `MaintenanceListAndDetailHaveTwoCompanyPositiveControlsAndForeignDenials`.
- `tools/verify-maintenance-tenant-http.py` — real login, two-company, branch, permission, IDOR, query-override, and pooled-read checks.

### Focused regression run

Eight current tests passed together against local PostgreSQL:

1. Maintenance two-company list/detail isolation.
2. Approval request tenant/branch scope and different-reviewer rule.
3. Concurrent invoice reviewers allow one decision.
4. Payment ledger rejects unsupported, duplicate, foreign, and oversized claims.
5. Concurrent dispatch allows one booking per vehicle/driver.
6. Concurrent trip starts allow one active trip.
7. Driver messaging and broadcast authorization/branch evidence.
8. Governed device installation transfer concurrency and durable history.

## Priority findings

| # | Verdict | Current assessment |
|---:|---|---|
| 1 | **Resolved** | Maintenance list requires `maintenance:view`, filters `mi.company_id=@cid`, applies branch scope, and passed real two-company HTTP/RLS verification. |
| 2 | **Resolved** | Maintenance detail binds both ID and company, applies branch scope to related data, returns foreign records as `404`, and passed guessed-ID tests. |
| 3 | **Resolved** | Device reassignment now uses governed installation create/transfer/remove endpoints. The old direct assignment route is retired; driver identity is not resent or cleared by transfer. The registered transfer concurrency test passed. |
| 4 | **Resolved** | Cost leakage reads require `finance:view`; acknowledge/action mutations require `finance:manage`; queries are company-scoped. |
| 5 | **Resolved** | Invoice approval decision uses the approval-to-draft-to-job tenant/branch join, returns foreign scope as `404`, enforces maker/checker separation, and serializes concurrent decisions. |
| 6 | **Resolved** | Current dispatch and trip-start paths have transactional/concurrency protection; both current concurrency regression tests passed. |
| 7 | **Resolved** | Driver message history requires `dispatch:view`; create/broadcast require `dispatch:manage` and internal-user context. Branch-scoped messaging regression passed. |
| 8 | **Resolved** | Payment recording is transactional, rejects duplicate reference, wrong currency, foreign invoice, and amount above balance due. Current payment-ledger test passed. |
| 9 | **Resolved by removal** | `render.yaml`, the root container, and the supported runtime use only `backend-dotnet`; the retired Node API prototype and its self-referential CI job were removed. |
| 10 | **Resolved by removal** | The retired Node `/login` implementation was deleted with the unused prototype, so the unthrottled route is no longer present. |
| 11 | **Resolved by removal** | The retired Node role-name wildcard implementation was deleted with the unused prototype. Current authorization remains in the .NET API. |
| 12 | **Resolved by removal** | The retired process-local Node telemetry route was deleted. Durable telemetry remains owned by the .NET API and supported event service. |

## Current medium-severity defects

| # | Verdict | Evidence and needed correction |
|---:|---|---|
| 13 | **Resolved** | Command-center summary requires `dashboard:view`. |
| 14 | **Resolved** | Digital form reads require `safety:view`; mutations require `safety:manage`. |
| 15 | **Resolved** | Expense detail requires `finance:view` and company scope. |
| 16 | **Resolved** | Cost-margin recalculation routes require `finance:manage`. |
| 17 | **Resolved safely** | Fake recalculation was removed; the endpoint now returns `501` and does not claim invented results. |
| 18 | **Resolved** | Safety summary now emits `distracted_driving_events`, matching the frontend `distractedDrivingEvents` projection; the remaining tiles already match their counted event types. |
| 19 | **Resolved** | Dispatch holds the shipment row, includes the expected status in the update predicate, checks exactly one affected row, and returns `409` if the state changed before pickup was recorded. |
| 20 | **Resolved** | All CSV exports now use the centralized spreadsheet-safe encoder, including revenue readiness, platform audit, reporting, ledger, shipment, logistics, and general exports. Formula-injection regression coverage passes. |
| 21 | **Resolved** | Last-active-Super-Admin protection now runs under a transaction and advisory lock; concurrent-demotion coverage proves that one active Super Admin remains. |
| 22 | **Resolved** | MFA enrollment challenges now expire after ten minutes and lock for fifteen minutes after five failed verification attempts, with persisted counters. |
| 23 | **Resolved** | Service-vehicle now locks the fleet vehicle and performs the active-shipment check and state change in one tenant transaction; dispatch locks the same row. |
| 24 | **Resolved** | RFID scans resolve prior RFID mappings through `fleet_tms_rfid_events` and no longer compare an RFID tag against the asset barcode field; explicit asset scans still enforce tenant/branch ownership. |
| 25 | **Resolved** | `MarketPackEndpoints.BuildMeta` now serializes a typed dictionary with `System.Text.Json`, preserving backslashes and control characters safely. |
| 26 | **Resolved** | Driver repair acknowledgement commits the DVIR transaction before running fleet availability reconciliation, so the whole-fleet pass no longer extends driver/DVIR locks. |
| 27 | **Resolved** | `BusinessSpineService.ValidateChargeResourcesAsync` in `backend-dotnet/Services/BusinessSpineServices.cs` binds job, trip, and rate-card references to the authenticated company and permitted branch on both create and update. `RevenueReadinessPostgresTests.JobChargesRejectForeignResourcesAndWrongBranchesOnCreateAndUpdate` supplies the two-company, foreign-reference, wrong-branch, create/update regression. |
| 28 | **Resolved** | The malformed billing-input path was `BusinessSpineEndpoints` in `backend-dotnet/Controllers/BusinessSpineEndpoints.cs`. Rate-card and job-charge create/update handlers now validate identifiers and decimals through non-throwing `TryLong` / `TryDecimal` helpers and return field-level `400` responses. `DeveloperFindingsLocatorRegressionTests.BillingInputParsersRejectMalformedValuesWithoutThrowing` prevents a return to direct conversion. |
| 29 | **Resolved** | The persistence boundary was `PostgresAiFoundationService` plus telemetry, safety, maintenance, and agentic background workers. `backend-dotnet/Foundation/FoundationPersistenceServices.cs` now exposes awaited async run/recommendation writes; each operational worker awaits them with cancellation. `IEventProcessingLogService.RecordAsync` removes the same synchronous database bridge from dispatchers. `DeveloperFindingsLocatorRegressionTests.OperationalBackgroundWorkersAwaitFoundationPersistence` covers the corrected call sites. |
| 30 | **Resolved** | Customer payments are capped at the invoice balance; oversized payments are rejected and tested. |
| 31 | **Resolved** | Invoice draft creation now encloses the header, lines, tax totals, idempotency reservation, and event write in one tenant transaction with a company/job advisory lock. The revenue-readiness PostgreSQL suite passed. |
| 32 | **Resolved** | ZATCA preparation now serializes ICV/PIH allocation per seller with a transactional advisory lock. A six-invoice concurrent regression proved contiguous ICV values and the complete PIH chain. |
| 33 | **Resolved by removal** | The unused synthetic Node device-registration route was deleted with the retired prototype. |
| 34 | **Resolved by removal** | The retired Node startup DDL was deleted; production schema changes remain migration-owned. |
| 35 | **Resolved by removal** | The retired Node read-path catalog seeding was deleted; the active integration catalog remains in the .NET API. |
| 36 | **Resolved** | Stage 144 removes all literal `company_id`/`tenant_id DEFAULT 1` values. The local catalog moved from 44 unsafe defaults to zero, the migration is idempotent, and every formerly affected runtime insert binds tenant identity explicitly. |
| 37 | **Resolved** | Tenant-leading indexes for maintenance work/items already exist in `2026_09_08_maintenance_analytics_evidence_integrity.sql`; fuel-log indexes exist in `2026_09_08_fuel_evidence_integrity.sql`. The remaining protected-schema gaps for expense and generic/vehicle/driver document reads are migration-owned by `database/migrations/2026_09_15_stage146_tenant_query_indexes.sql`, enrolled in `tools/apply-neon-predeploy-migrations.sh`; local bootstrap parity for vehicle/driver documents is in `Batch1SchemaService`. The locator regression verifies the four new index definitions and runner enrollment. |
| 38 | **Resolved by removal** | The shared setup code was the `config` table in `frontend/src/pages/EntityListPage.tsx`. Route/import search proves only `drivers` (`DriversModulePage`) and `assets` (`App.tsx`) use it; vehicles, customers, and jobs have dedicated pages. Their unused configurations and unreachable vehicle-planning branches were removed, `EntityKind` now admits only the two live consumers, and `SharedEntityListOnlyConfiguresItsTwoLiveConsumers` prevents reintroduction. |
| 39 | **Resolved / not reproduced** | Every current page above 1,500 lines is lazy-loaded and has a live route (`IotDevices`, `Integrations`, `FleetAssignments`, `DispatchWorkspace`, `Admin`). No 1,500-line shipped-but-unreachable page was found. |
| 40 | **Resolved** | Dispatch workspace task panels were extracted; the routed page is now 1,168 lines and keeps shared state/query boundaries explicit. |
| 41 | **Resolved** | Admin task panels were extracted; the routed page is now 1,174 lines with focused child modules. |
| 42 | **Resolved** | Platform tenant management was split into focused components; the routed `PlatformTenantsPage.tsx` is now 286 lines. |
| 43 | **Observation** | The cited multi-state page is `frontend/src/pages/FleetCompliancePage.tsx`: `FleetCompliancePage` routes country selection, while `CanadaReadiness` and `SaudiReadiness` own their respective query and mutation state. It is a large coordinated workspace, but current source already separates the country implementations and no functional failure or unsafe boundary was found. Further hook extraction is optional maintainability work, not a defect correction. |
| 44 | **Resolved** | Portal list/device scope now consumes authenticated `driverId` / `customerId` relationships returned by login, MFA completion, and `/auth/me`; named email/display-name mappings and partial-name matching are removed, and a missing relationship fails closed. |

## Low-severity and housekeeping findings

| # | Verdict | Current assessment |
|---:|---|---|
| 45 | **Resolved** | The read-only routes are `/api/carbon-emissions`, `/api/carbon-emissions/trend`, and `/api/owners` in `backend-dotnet/Controllers/EndpointMappings.cs`. Carbon reads require `reports:view`; owner/profit-share reads require `dispatch:view`; every query binds the authenticated company. `AdminRolePermissionCatalogTests.MaintenanceManagerIsDeniedByRegisteredReportingRoutesBeforeDatabaseAccess` now performs real HTTP denials for all three routes. |
| 46 | **Resolved** | Every previously wrapper-only handler now performs its authorization check inside the handler; the regression enumerates the complete reviewed handler list. |
| 47 | **Resolved** | Tenant and platform login now verify a fixed PBKDF2 dummy hash for unknown/inactive accounts while preserving the same external response. |
| 48 | **Resolved** | Dormant unscoped list/detail/summary SQL fields were removed from `ModuleDefinitions`; current reads use the centralized tenant-scoped path. |
| 49 | **Resolved** | Numeric authenticated-user fallbacks were removed; missing authenticated identity now fails closed and is covered by regression tests. |
| 50 | **Resolved** | Carrier status has a server-side allow-list and guarded transition policy with an expected-state write check. |
| 51 | **Resolved** | Telemetry and safety rule types use a server-side supported-type allow-list. |
| 52 | **Resolved** | Retired HOS/ELD handlers were removed; the mapped pilot handlers remain the only supported implementations. |
| 53 | **Resolved** | Fleet-health attention counts exclude cancelled, closed, and deleted maintenance work. |
| 54 | **Resolved** | Access-review reviewer validation already requires an active user in the same company; adversarial tenant-isolation coverage confirms the boundary. |
| 55 | **Resolved** | Platform login now enforces independent account/email and IP throttles. |
| 56 | **Resolved** | Bulk-admin routes authenticate before parsing the request body. |
| 57 | **Resolved** | Self-service platform-admin password change uses the shared bootstrap/invite/reset password policy. |
| 58 | **Resolved** | Bulk-admin responses return a trace reference; raw exception detail is logged only on the server. |
| 59 | **Resolved** | Platform support impersonation now has a 28-case mutation-denial matrix covering every allowed read family plus `/api/auth/me`; all unreviewed routes fail closed. |
| 60 | **Resolved** | The retired duplicate Saudi compliance endpoints and handler DTO were removed; the tenant-market route is the sole supported surface. |
| 61 | **Resolved** | The helper is `FleetTmsColdChainEndpoints.ParseCities` in `backend-dotnet/Controllers/FleetTmsColdChainEndpoints.cs`. Its fallback now catches only `System.Text.Json.JsonException`; unrelated programming/runtime exceptions are no longer swallowed. `DeveloperFindingsLocatorRegressionTests.CityJsonFallbackIsLimitedToMalformedJson` checks valid and malformed input and the narrowed catch. |
| 62 | **Resolved locally** | Recovery now requires recent accepted evidence and valid exact-provider identity where a verifier is supported; unsupported providers remain preview-only and cannot claim verified recovery. Live provider activation still requires credentials. |
| 63 | **Resolved** | Compliance writes share one company/branch ownership validator for driver and vehicle references. |
| 64 | **Resolved** | Route/order city, area, and note fields have server-side limits with field-level `400` responses. |
| 65 | **Resolved** | Saudi VAT readiness registration, tax-profile publication, entitlement event, and returned readiness state now share one tenant transaction. |
| 66 | **Resolved** | Saudi compliance subject types use the same driver/vehicle allow-list as the North American path. |
| 67 | **Resolved** | Driver settlement payment recording now locks the statement, rejects amounts above the outstanding balance, and updates the ledger and statement state atomically. The PostgreSQL regression passed. |
| 68 | **Resolved** | Detention evaluation now batches the referenced tenant/site lookups before evaluating the fleet set. |
| 69 | **Resolved by removal** | The retired Node login implementation was deleted, removing its timing distinction from the codebase. |
| 70 | **Resolved** | Production comments and source contracts now identify `database/migrations` plus the ledger as the schema authority. |
| 71 | **Resolved historical item** | Later migration reconciliation already normalizes the password-reset RLS policy. No action. |
| 72 | **Resolved / not reproduced** | No `@ts-nocheck` remains under `frontend/src`; the reported disabled type check is absent on this branch. |
| 73 | **Resolved locally / deployment constraint documented** | Platform login now issues an HttpOnly platform-session cookie and no longer returns the privileged token to JavaScript; the SPA restores authority through `/platform/auth/me` and removes the retired local-storage credential. HTTPS uses `Secure; SameSite=None` for the current Vercel→Render topology. Browsers that block cross-site cookies still require a same-site API proxy or shared custom parent domain at deployment. Bearer fallback remains for deliberate non-browser clients. |
| 74 | **Resolved** | Protected API `401` responses clear the tenant session and redirect once to `/login`; login, activation, forgot-password, and reset-password requests remain outside that post-session flow. |
| 75 | **Resolved / route retired** | `/load-bookings` and `/shipments` now route to the real `JobsPage`; the empty generic booking configuration is not user-facing. Delete the remaining dead configuration to avoid regression. |
| 76 | **Resolved** | Tenant and platform clients now use the same credentials, CSRF, timeout, W3C tracing, trace capture, and 401 pipeline factory while retaining separate identity boundaries. |
| 77 | **Resolved** | `WorkspaceExperience.tsx` was proven unused by import search and `ts-prune`; the dead helper and its `ArrowRight` icon import were removed. The live `WorkspaceGuidance` component remains in use by Alerts, Integrations, and Maintenance. |
| 78 | **Observation** | Large IoT Devices and Integrations pages are organized and routed. No action required solely because of line count. |
| 79 | **Resolved** | Platform billing plan set/remove and contract override removal now write platform audit events. |
| 80 | **Resolved by removal** | Retired `api-dotnet/`, `backend/`, and `db/` implementations were removed after release-path verification; CI and deployment use only `backend-dotnet/` and `database/`. |

## Recommended correction order

1. Run clean-CI, migration rehearsal, deployment, and external-provider acceptance after the local branch is approved.
2. Capture production query plans for the indexed expense/document reads after representative tenant volumes are available; retain or adjust indexes from measured plans rather than intuition.
3. Keep ZATCA, payment-provider, TAMM/Elm, Locus, and provider-device qualification explicitly pending until external credentials and certification evidence exist.

## Local runtime note

At the time of this review, the local API answered `/health/ready` with `200`, and the local Intelliflow System Test login completed successfully against `http://127.0.0.1:18090`. The frontend intentionally calls that absolute local API origin; requesting `/api/...` directly from the Vite port returns the frontend document rather than proxying to the API.
