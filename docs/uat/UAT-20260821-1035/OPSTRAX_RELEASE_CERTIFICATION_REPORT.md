# OpsTrax Release Certification Report

Run ID: `UAT-20260821-1035`  
Certification state: **NO-GO — LIVE CUSTOMER JOURNEY FAILURES**

Core staging authentication, basic master-data creation, lower-privilege account creation, and the tested cross-tenant boundary work, but the deployed release is not certifiable. CORS, database readiness, and protected tenant login were repaired and retested. In disposable tenant `T-C221B5BA`, the Company Admin created a vehicle, driver, customer, job, telematics device, and four role personas. Dispatch assignment, route planning, maintenance, safety, driver-portal, and customer-portal journeys then failed on live API/schema/routing mismatches; audit reporting is contradictory and the device installation action is inert.

## Current counts

| Status | Count |
|---|---:|
| PASS | 18 |
| FAIL | 13 |
| BLOCKED | 4 |
| NOT IMPLEMENTED | 1 |

Counts combine the Gate 0 ledger with executed authenticated journey checkpoints; they are not source-test declaration counts.

## Fresh automated evidence on the current working snapshot

- Frontend lint: PASS with zero warnings.
- Frontend source-contract check: PASS.
- Frontend production build and bundle budget: PASS.
- Node backend build: PASS; security-hardening suite: 6/6 PASS.
- .NET backend build: PASS with 486 compiler/analyzer warnings and zero errors.
- .NET backend non-database lane: 1,529/1,529 PASS; artifact `backend-dotnet.Tests/TestResults/backend-unit-UAT-20260821-1035.trx`.
- Telematics build: PASS. Protocols 39/39 PASS, security 39/39 PASS, integration 166/173 PASS; all seven failures are environment BLOCKED because `OPSTRAX_TEST_DB` is absent.
- Backend PostgreSQL/integration lane: BLOCKED. The runner was stopped after more than seven minutes because every reached case failed at setup against unavailable `127.0.0.1:5433`; no product assertion executed. A disposable PostgreSQL service is still required for a valid RLS/database certification artifact.

## Current release posture

- Core journey: FAIL — master data and job creation pass, downstream operations do not
- Tenant isolation: PASS on the executed two-tenant vehicle/job negative checks; full database integration suite remains blocked
- RBAC: FAIL — Dispatcher direct URL and Customer Portal shell exposure reproduced live
- Customer journey: FAIL — intended portal loads but the owned job is absent and login lands on the internal fleet shell
- Driver journey: FAIL — correct portal routing and internal-route redirect pass, but the bound dashboard returns HTTP 500
- Telemetry: FAIL — registration passes, governed installation is inert, public edge/hardware remain unavailable; the test credential has been revoked and cleared
- Automated regression: BLOCKED; database-dependent diagnostics did not complete
- Open live/source findings now include multiple Critical database-contract failures plus unresolved telemetry security findings
- Cleanup: the disposable device credential is revoked; other run-labelled records and the isolated tenant remain intentionally available as UAT evidence

Release decision: **NO-GO** until DEF-015 through DEF-027 are fixed or explicitly dispositioned and the affected journeys pass on the exact deployed SHA. Lower-privilege authentication and the first live cross-tenant negative checks are complete; the full database/RLS and browser regression suites still require current execution artifacts.
