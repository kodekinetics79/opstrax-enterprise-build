# OpsTrax Reality Audit

Hostile, evidence-based audit of the current Opstrax/KynexOne fleet build.

Scope rule: no assumptions. Every finding below is backed by file evidence or a command result. If a fact could not be verified, it is marked `UNVERIFIED`.

## Executive Scorecard

| Axis | Score | Verdict | Evidence |
|---|---:|---|---|
| Schema reality | 86/100 | Broadly real, but not EF-based and not named exactly like the target model | `backend-dotnet/Opstrax.Api.csproj:7`, `backend-dotnet/Program.cs:55`, `database/migrations/2026_06_27_stage5_p0b1a_foundation.sql:8` |
| RBAC | 94/100 | Fail-closed on the critical paths inspected | `backend-dotnet/Controllers/EndpointMappings.cs:1648`, `backend-dotnet/Controllers/PlatformEndpoints.cs:107` |
| Tenant isolation | 88/100 | Explicit `company_id` / `tenant_id` filtering is real, but there is no DB RLS or EF query filter | `backend-dotnet/Controllers/EndpointMappings.cs:279`, `backend-dotnet/Controllers/PlatformEndpoints.cs:500`, `rg -n "ENABLE ROW LEVEL SECURITY|CREATE POLICY|HasQueryFilter|DbContext|EntityFrameworkCore" backend-dotnet database` |
| Demo masking risk | 72/100 | Several pages still contain seed/demo scaffolds that can make the app look less real than the backend is | `frontend/src/pages/ExecutivePage.tsx:25`, `frontend/src/pages/SlaKpiPage.tsx:16`, `frontend/src/pages/DriverMessagingPage.tsx:17`, `database/init/005_local_module_test_data.sql:99` |
| AI governance | 96/100 | Recommendation-only; no direct business-table mutation path found in the inspected AI flow | `backend-dotnet/Services/Stage9OperationalFoundationService.cs:166`, `backend-dotnet/Foundation/FoundationPersistenceServices.cs:56` |
| Audit / idempotency / concurrency | 93/100 | Real tables, uniqueness, retry/backoff, and `SKIP LOCKED` claim logic exist | `database/migrations/2026_06_27_stage5_p0b1a_foundation.sql:125`, `database/migrations/2026_06_28_stage5d_p0b1a3_dispatcher.sql:20`, `backend-dotnet/Foundation/FoundationPersistenceServices.cs:185` |
| Build / test health | 98/100 | Backend, frontend, and workspace smoke checks passed | command outputs below |
| Production readiness | 74/100 | Strong foundation, but local `.env` exists and demo scaffolding still leaks into the UX | `.env`, `frontend/src/pages/ExecutivePage.tsx:25`, `backend-dotnet/Services/ConfigValidationService.cs:62` |

Overall verdict: the core platform is narrow-but-real, not a fake shell. The biggest remaining credibility risk is demo masking, not backend emptiness.

## Commands Run

- `dotnet build backend-dotnet/Opstrax.Api.csproj` -> passed; 0 errors, 472 warnings.
- `dotnet test backend-dotnet.Tests/Opstrax.Tests.csproj --no-restore` -> passed; `862` tests passed, `0` failed, `0` skipped.
- `npm run build` in `frontend/` -> passed.
- `npm run lint` in `frontend/` -> passed.
- `npm run build` in `backend/` -> passed.
- `dotnet ef migrations list --project backend-dotnet/Opstrax.Api.csproj --startup-project backend-dotnet/Opstrax.Api.csproj` -> failed because `dotnet-ef` is not installed.
- `dotnet ef dbcontext info --project backend-dotnet/Opstrax.Api.csproj --startup-project backend-dotnet/Opstrax.Api.csproj` -> failed because `dotnet-ef` is not installed.
- Route smoke on `http://localhost:10000` returned `200 OK` for `/command-center`, `/fleet-health`, `/fleet-workspace`, `/fleet-cold-chain`, `/fleet-saudi-readiness`, and `/operations/proof-center`.

## 1. Schema Reality

The repo is not EF Core driven. The backend project references only `Npgsql` and `Swashbuckle.AspNetCore`; there is no EF Core package in the API project. `dotnet ef` is unavailable, so EF migration list / `DbContext` inspection is `UNVERIFIED` by tooling and by architecture.

| Target family | Reality | Status | Evidence |
|---|---|---|---|
| companies / users / roles / permissions / role_permissions / user_roles | Real tenant and auth tables are used directly in middleware and platform APIs | EXISTS-MATCHES | `backend-dotnet/Program.cs:279`, `backend-dotnet/Controllers/PlatformEndpoints.cs:74` |
| feature_flags / subscriptions | The canonical controls are `tenant_entitlements` and `tenant_subscriptions`, not a single feature_flags/subscriptions model | EXISTS-DIFFERENT | `backend-dotnet/Program.cs:332`, `backend-dotnet/Controllers/PlatformEndpoints.cs:505` |
| customers / customer_addresses / customer_sites | Real operational customer/site rows are used by seed data and operational workflows | EXISTS-MATCHES | `database/init/005_local_module_test_data.sql:82`, `backend-dotnet/Services/Stage9OperationalFoundationService.cs:319` |
| contracts / quotes / leads / opportunities | Frontend modules exist, but this audit did not fully verify a canonical schema for every reference-table name | PARTIAL | `frontend/src/App.tsx:231`, `frontend/src/App.tsx:233`, `frontend/src/App.tsx:236`, `frontend/src/App.tsx:237` |
| vehicles / drivers / driver_vehicle_assignments | Real backend queries and detail views exist and are tenant-scoped | EXISTS-MATCHES | `backend-dotnet/Controllers/EndpointMappings.cs:14632`, `backend-dotnet/Controllers/EndpointMappings.cs:14725`, `backend-dotnet/Controllers/EndpointMappings.cs:10331` |
| jobs / trips / trip_stops / dispatch_assignments | Real backend dispatch / job / trip flows exist and are tenant-scoped | EXISTS-MATCHES | `backend-dotnet/Controllers/EndpointMappings.cs:10196`, `backend-dotnet/Services/Stage9OperationalFoundationService.cs:26` |
| assignment_recommendations | Implemented as `smart_assignment_recommendations`, not the target name | EXISTS-DIFFERENT | `backend-dotnet/Services/Stage9OperationalFoundationService.cs:26`, `frontend/src/pages/OperationsProofCenterPage.tsx:125` |
| proof_packages / proof_artifacts | Real persisted operational proof objects | EXISTS-MATCHES | `database/migrations/2026_06_27_stage5_p0b1a_foundation.sql:156`, `frontend/src/pages/OperationsProofCenterPage.tsx:128` |
| telemetry_devices / telemetry_events / asset_live_states | Implemented with different physical names: `eld_devices`, `telemetry_live_asset_states`, `telemetry_alerts` | EXISTS-DIFFERENT | `backend-dotnet/Controllers/EndpointMappings.cs:8500`, `database/init/006_local_telemetry_live_state_seed.sql:9` |
| telemetry_alerts | Real alert table and ack/resolve flows exist | EXISTS-MATCHES | `backend-dotnet/Controllers/EndpointMappings.cs:8378`, `backend-dotnet/Controllers/EndpointMappings.cs:8405` |
| safety_events / driver_coaching_tasks / vehicle_inspections / defect_reports | Real detail queries exist for safety / coaching / HOS / DVIR, but not every target table name was exhaustively proven in this audit | PARTIAL | `backend-dotnet/Controllers/EndpointMappings.cs:14741`, `backend-dotnet/Controllers/EndpointMappings.cs:14648` |
| maintenance_work_orders / preventive_maintenance_schedules | Work orders and overdue PM records are real; schedule-level schema was not exhaustively proven here | PARTIAL | `backend-dotnet/Controllers/EndpointMappings.cs:14662`, `backend-dotnet/Controllers/EndpointMappings.cs:14674` |
| fleet_health_scores | Fleet health is real as computed queries and pages, but not as a single canonical table | EXISTS-DIFFERENT | `backend-dotnet/Controllers/PlatformEndpoints.cs:907`, `frontend/src/pages/FleetHealthPage.tsx:932` |
| charges / invoice_drafts / issued_invoices / payments | Finance exists in platform and reporting surfaces, but the exact target table set was not fully traced in this pass | PARTIAL | `backend-dotnet/Controllers/PlatformEndpoints.cs:508`, `frontend/src/pages/ReportsPage.tsx:156` |
| documents / compliance_items / compliance_alerts / alerts | Compliance and alerting are real; naming is split across multiple operational tables | PARTIAL | `frontend/src/pages/FleetSaudiReadinessPage.tsx:186`, `backend-dotnet/Controllers/EndpointMappings.cs:8378` |
| ai_recommendations / action_requests / audit_logs / domain_events / outbox_messages / idempotency_keys | Real foundation tables exist; `action_requests` is implemented as `ai_action_requests`, and audit is split across multiple tables | EXISTS-DIFFERENT | `database/migrations/2026_06_27_stage5_p0b1a_foundation.sql:156`, `database/migrations/2026_06_27_stage5_p0b1a_foundation.sql:193`, `database/migrations/2026_06_27_stage5_p0b1a_foundation.sql:8` |

### Schema findings that matter

- There is no evidence of a canonical EF `DbContext` or EF migration chain. This codebase uses raw PostgreSQL and additive SQL migrations instead.
- `organization_groups`, `branches_sites`, `telemetry_devices`, `asset_live_states`, `assignment_recommendations`, `action_requests`, and `subscriptions` are not the canonical live names in the inspected implementation. The system uses differently named but functionally similar tables.
- The migration set is PostgreSQL-only and non-destructive.
- `tenant_id` / `company_id` is present on the tenant-owned operational tables that were directly verified.
- Unique keys and indexes exist for the core reliability tables, especially inbox and idempotency.

## 2. Tenant Isolation - Prove or Disprove

### What is real

- Tenant auth context is loaded from `user_sessions`, `users`, `roles`, and `role_permissions`, then stored in request items for downstream authorization checks: `backend-dotnet/Program.cs:279`.
- Feature/module gating is enforced server-side using `tenant_entitlements`: `backend-dotnet/Program.cs:332`.
- Critical endpoints query with `company_id=@cid` or `tenant_id=@tenantId` and fail closed on missing tenant context: `backend-dotnet/Controllers/EndpointMappings.cs:1703`, `backend-dotnet/Controllers/PlatformEndpoints.cs:107`.
- Fleet health vehicle and driver detail endpoints are tenant-scoped and not global: `backend-dotnet/Controllers/EndpointMappings.cs:14638`, `backend-dotnet/Controllers/EndpointMappings.cs:14731`.
- Dispatch assignment list/create checks tenant membership on vehicle and driver rows before mutation: `backend-dotnet/Controllers/EndpointMappings.cs:10331`.
- Stage 9 operational read models are tenant-scoped in every queried table: `backend-dotnet/Services/Stage9OperationalFoundationService.cs:1274`.

### What is not there

- No Postgres RLS policy was found.
- No global EF `HasQueryFilter` was found.
- `dotnet ef` tooling is not present, so EF-specific schema/projection verification is not the live source of truth here.

### Tenant-isolation verdict

Fail-closed at the application layer: yes.
Database-native isolation: no evidence found.

That is acceptable for now only because every critical code path audited here carries explicit `company_id` / `tenant_id` predicates. It is still weaker than row-level security.

## 3. RBAC Reality

### Backend authorization

- Centralized permission checking exists and is used by the route layer: `backend-dotnet/Controllers/EndpointMappings.cs:1648`.
- Missing permissions, missing tenant context, or missing role context return `401` rather than allowing access: `backend-dotnet/Controllers/EndpointMappings.cs:1650`.
- The decision service denies missing actor, tenant boundary violations, deny overrides, and missing permissions before allowing anything: `backend-dotnet/Foundation/FoundationServices.cs:44`.
- Platform admin endpoints are isolated from tenant auth and use separate bearer-session auth plus permission checks: `backend-dotnet/Controllers/PlatformEndpoints.cs:12`, `backend-dotnet/Controllers/PlatformEndpoints.cs:107`.
- Super-admin wildcard support exists, but only in explicit platform or admin contexts: `backend-dotnet/Controllers/EndpointMappings.cs:7241`, `backend-dotnet/Controllers/EndpointMappings.cs:7252`.

### RBAC verdict

I did not find an allow-by-default critical path in the audited business routes.

### Frontend RBAC

- The frontend hides actions and routes through `RequirePermission`, but the backend still does the real enforcement: `frontend/src/App.tsx:152`, `frontend/src/pages/OperationsProofCenterPage.tsx:299`.
- That is the right pattern. Frontend hiding is not security. Backend enforcement exists, so this is not a frontend-only RBAC system.

## 4. Fake Data / Demo Masking Detection

This is where the app still looks less real than the backend actually is.

| Finding | What it fakes | Evidence | Risk |
|---|---|---|---|
| Executive dashboard seed KPI tiles | “Live” KPI cards are still hardcoded presentation values, not a backend-driven metric source | `frontend/src/pages/ExecutivePage.tsx:25` | Medium |
| SLA KPI page seed arrays | Empty seed arrays and fallback data can make the page look supported without live rows | `frontend/src/pages/SlaKpiPage.tsx:16` | Medium |
| Driver messaging seed arrays | Message templates and broadcasts are still seeded UI constructs, not a proven live data model | `frontend/src/pages/DriverMessagingPage.tsx:17` | Medium |
| Local module test data | A very large local seed file injects realistic-looking rows for customers, jobs, telemetry, proof, billing, and AI | `database/init/005_local_module_test_data.sql:99`, `database/init/005_local_module_test_data.sql:524`, `database/init/005_local_module_test_data.sql:647` | High for demo honesty, low for backend correctness |
| Local telemetry live-state seed | Live map / cold-chain style states are preloaded with plausible operational telemetry | `database/init/006_local_telemetry_live_state_seed.sql:3` | High for demo honesty, low for backend correctness |
| Legacy demo credentials | Demo auth credentials still exist in seeded data | `database/init/002_seed.sql:50` | High if exposed outside controlled local/demo environments |
| Placeholder report/UI copy | Some pages still use “Live” labels or placeholder chart shells that are not the same as live data plumbing | `frontend/src/pages/ExecutivePage.tsx:25`, `frontend/src/pages/ExecutivePage.tsx:241` | Medium |

Important distinction:

- The audited live pages such as Fleet Workspace, Fleet Cold Chain, Saudi Readiness, Last Mile Delivery, and Operational Proof Center are not fake shells. They call real backend APIs and show honest empty/error states.
- The masking issue is concentrated in some older/demo-assistive surfaces and local seed data, not the main operational proof path.

## 5. Module-by-Module Reality

| Module | Backend reality | Frontend reality | RBAC enforced? | Tenant isolated? | Fake data found? | Verdict | Top blocking gap |
|---|---|---|---|---|---|---|---|
| Platform Admin | Real separate control plane with login, tenant create/update, entitlements, invoices, audit, roles | Real `/platform/*` app exists | Yes | Yes | No live fake path found in audited control plane | REAL-END-TO-END | None major in audited scope |
| Tenant Admin / Users / Roles | DB-backed auth, roles, permissions, user-role links, session auth, audit logging | Admin screens exist | Yes | Yes | Legacy demo password handling exists | REAL, but legacy auth debt | Retire legacy demo-password fallback |
| Dashboard / Command Center / Executive | Live summary endpoints exist; executive page still uses seed KPI tiles | Real dashboard pages exist | Yes | Yes | Yes | PARTIAL | Replace seed KPI presentation scaffolds |
| Fleet / Vehicles | Vehicle detail is live, tenant-scoped, and includes defects/work orders/PM/assignment | Fleet pages are live | Yes | Yes | No obvious fake path in audited detail views | REAL-END-TO-END | None major |
| Drivers | Driver detail is live and tenant-scoped with safety, coaching, HOS, assignment | Driver pages are live | Yes | Yes | No obvious fake path in audited detail views | REAL-END-TO-END | None major |
| Jobs / Trips | Jobs, trips, dispatch and proof flows exist end-to-end | Jobs / trips / last-mile / POD pages exist | Yes | Yes | Local seed data still exists | REAL, but demo-masked in some pages | Reduce seed reliance in user-facing surfaces |
| Dispatch | List, detail, create, override, eligibility, assignment acceptance/rejection exist | Dispatch workspace and proof center consume them | Yes | Yes | No fake success path in audited backend | REAL-END-TO-END | None major |
| Live Map / Telemetry | Live map summary, alert ack/resolve, rules, devices are real | Live map / health views exist | Yes | Yes | Telemetry is seeded locally for demo realism | REAL-END-TO-END | Add DB-native isolation if you want defense-in-depth |
| Proof Center | Stage 9 execution summary, smart assignment, access docs, pickup auth, warehouse handover, proof packages, billing confidence are real | Operational Proof Center is live and permissioned | Yes | Yes | No synthetic data injected by the page | REAL-END-TO-END | None major |
| Safety | Safety events, coaching, HOS, DVIR signals are real in audited queries | Safety and compliance views exist | Yes | Yes | Some seed/demo scaffolds elsewhere | PARTIAL | Full module inventory not exhaustively audited here |
| Maintenance | Work orders and PM warnings are real in fleet-health detail | Maintenance surfaces exist | Yes | Yes | Seed data exists in local fixtures | PARTIAL | Separate the PM schedule story from seeded data |
| Fleet Health | Vehicle and driver risk detail is real, with computed risk scores | Fleet Health page is live | Yes | Yes | No obvious fake path in audited view | REAL-END-TO-END | None major |
| Alert Center | Telemetry alerts are real, ack/resolution is real, audit is written | Alert center page exists | Yes | Yes | Local telemetry seeds can make it look more complete than it is | REAL-END-TO-END | None major |
| Finance | Platform invoices exist; billing confidence exists; readiness view exists | Reports/finance pages exist | Yes | Yes | Some KPI/storyboard surfaces are seeded | PARTIAL | Full invoice/charge flow not fully proven in this audit |
| CRM | Customer/account/quote/opportunity pages exist | CRM pages exist | Likely yes, but not exhaustively proven here | Likely yes | Some demo scaffolds likely remain | PARTIAL | Exhaustive backend schema tracing for leads/opportunities |
| Customer Portal | Public ETA/tracking routes exist | Portal pages exist | Yes for protected pages; public routes are public by design | Mostly yes | No fake path found for public tracking | PARTIAL | Separate customer portal auth model not fully verified |
| Compliance | Saudi/GCC readiness page is live and DB-backed | Compliance/readiness page is live | Yes | Yes | Some local test fixtures exist | REAL-END-TO-END | None major |
| Reports | Live reporting endpoints exist; pages are wired | Reports and analytics pages exist | Yes | Yes | Executive/SLA demo scaffolding still leaks into story | PARTIAL | Replace stale seed presentation surfaces |
| AI Recommendations | DB-backed AI reasoning, recommendations, action requests, outcomes, and approval workflow exist | UI labels AI as assistive / recommendation-only | Yes | Yes | No direct mutation path found | REAL-END-TO-END | Keep AI recommendation-only, never direct-write business tables |

## 6. AI Governance Check

### Verified

- AI reasoning runs are persisted: `backend-dotnet/Foundation/FoundationPersistenceServices.cs:519`.
- AI recommendations are persisted with tenant, correlation, causation, and status: `backend-dotnet/Foundation/FoundationPersistenceServices.cs:581`.
- AI action requests are persisted separately and can require approval: `backend-dotnet/Foundation/FoundationPersistenceServices.cs:614`.
- The Stage 9 smoke handler creates a recommendation and an approval-required action request; it does not execute the business action automatically: `backend-dotnet/Foundation/FoundationPersistenceServices.cs:56`.
- The operational proof UI says AI is recommendation-only and acceptance is gated by backend permission: `frontend/src/pages/OperationsProofCenterPage.tsx:337`.

### Not found

- No audited path allows an AI response to directly assign a driver, mutate business tables, issue an invoice, or bypass approval.

### Verdict

AI governance is real and correctly constrained for the scope inspected here.

## 7. Audit / Idempotency / Concurrency

### Audit logging

- Authorization decisions are recorded to `authorization_decision_logs`: `backend-dotnet/Controllers/EndpointMappings.cs:1690`, `database/migrations/2026_06_27_stage5_p0b1a_foundation.sql:8`.
- Platform admin mutations write `platform_audit_log`: `backend-dotnet/Controllers/PlatformEndpoints.cs:18`, `backend-dotnet/Controllers/PlatformEndpoints.cs:145`.
- Stage 9 business mutations call `AuditService.LogAsync(...)` on create/update/accept/reject paths: `backend-dotnet/Controllers/Stage9Endpoints.cs:87`, `backend-dotnet/Controllers/Stage9Endpoints.cs:145`, `backend-dotnet/Controllers/Stage9Endpoints.cs:194`, `backend-dotnet/Controllers/Stage9Endpoints.cs:214`.

### Idempotency

- The idempotency table exists with a unique key on `(tenant_id, operation, idempotency_key)`: `database/migrations/2026_06_27_stage5_p0b1a_foundation.sql:125`.
- The service reserves and completes idempotency entries by tenant/operation/key, and rejects same-key/different-hash reuse: `backend-dotnet/Foundation/FoundationPersistenceServices.cs:431`.
- Stage 9 operational create/recommend routes accept `idempotencyKey` and thread it through the DB writes: `backend-dotnet/Controllers/Stage9Endpoints.cs:69`.

### Concurrency / retry

- Outbox and inbox claim logic uses `FOR UPDATE SKIP LOCKED`: `backend-dotnet/Foundation/FoundationPersistenceServices.cs:185`, `backend-dotnet/Foundation/FoundationPersistenceServices.cs:235`.
- Dispatcher rows have `claimed_at`, `claimed_by`, `locked_until`, `last_error`, and `dead_letter_reason`: `database/migrations/2026_06_28_stage5d_p0b1a3_dispatcher.sql:6`.
- Retry scheduling exists and is tenant-scoped: `backend-dotnet/Foundation/FoundationPersistenceServices.cs:301`.

### Verdict

This is not a pretend audit trail. The foundation has real persistence, real duplicate detection, and real retry/dead-letter semantics.

## 8. Build / Test Health

| Command | Result | Notes |
|---|---|---|
| `dotnet build backend-dotnet/Opstrax.Api.csproj` | PASS | 0 errors, 472 warnings |
| `dotnet test backend-dotnet.Tests/Opstrax.Tests.csproj --no-restore` | PASS | 862 passed, 0 failed, 0 skipped |
| `npm run build` in `frontend/` | PASS | Build succeeded |
| `npm run lint` in `frontend/` | PASS | Lint succeeded |
| `npm run build` in `backend/` | PASS | TypeScript build succeeded |
| `dotnet ef migrations list` | FAIL | `dotnet-ef` not installed |
| `dotnet ef dbcontext info` | FAIL | `dotnet-ef` not installed |
| Route smoke on localhost:10000 | PASS | 200 OK on all audited routes |

### Test coverage that matters

- Dispatcher success / failure / dead-letter / duplicate inbox / missing tenant tests are real: `backend-dotnet.Tests/FoundationDispatcherPostgresTests.cs:13`.
- Execution summary collects all workflow sections and does not mutate counts: `backend-dotnet.Tests/Stage10PostgresTests.cs:14`.
- Cross-tenant execution summary returns `no_data` instead of leaking data: `backend-dotnet.Tests/Stage10PostgresTests.cs:181`.

## 9. Secrets / Hygiene

### What was found

- A local root `.env` file exists on disk: `.env`.
- It is not shown as a tracked git file in the audit sample, which means it is local-only, but it still contains real local connection values on disk.
- `.env.example` is tracked in git history: `git ls-files .env.example`.
- History grep for env-related commits showed only placeholder credential strings, not confirmed live production secrets in the scanned history snippet: `git log -p --all -- '*.env*' | rg -n "(PG_CONNECTION|Password=|SECRET|API_KEY|JWT|token=|connection string|ConnectionStrings)"`.

### Risk

Local secret hygiene is not clean enough to call this production-safe. The live app may be fine, but the repository workspace still contains a local `.env` with real credentials.

## 10. Final Scorecard

| Module | Backend Reality | Frontend Reality | RBAC Enforced? | Tenant Isolated? | Fake Data Found? | MVP-Ready | Top Blocking Gap |
|---|---|---|---|---|---|---|---|
| Platform Admin | Real | Real | Yes | Yes | No obvious fake path in audited control plane | Y | None major |
| Tenant Admin | Real | Real | Yes | Yes | Legacy demo-password behavior exists | Y | Remove legacy auth debt |
| Dashboard | Real but mixed with seed scaffolds | Real | Yes | Yes | Yes | N | Seed KPI / placeholder presentation cleanup |
| Fleet / Vehicles | Real | Real | Yes | Yes | No obvious fake path in audited views | Y | None major |
| Drivers | Real | Real | Yes | Yes | No obvious fake path in audited views | Y | None major |
| Jobs / Trips | Real | Real | Yes | Yes | Some local seed data exists | Y | Reduce demo masking in user-facing surfaces |
| Dispatch | Real | Real | Yes | Yes | No obvious fake path in audited backend | Y | None major |
| Live Map / Telemetry | Real | Real | Yes | Yes | Local telemetry seed data exists | Y | Add DB-native isolation if needed |
| Proof Center | Real | Real | Yes | Yes | No synthetic data injected by the page | Y | None major |
| Safety | Real but not exhaustively audited | Real | Yes | Yes | Some demo scaffolds elsewhere | P | Full module inventory not finished |
| Maintenance | Real but not exhaustively audited | Real | Yes | Yes | Seed fixtures exist | P | Separate schedule truth from seeds |
| Fleet Health | Real | Real | Yes | Yes | No obvious fake path | Y | None major |
| Alert Center | Real | Real | Yes | Yes | Telemetry seeds can make it look more complete than it is | Y | None major |
| Finance | Partial | Partial | Yes | Yes | Seeded storyboards remain | P | Full charge/invoice path not proven here |
| CRM | Partial | Partial | Likely yes | Likely yes | Seed/demo scaffolding likely remains | P | Exhaustive schema trace for leads/opportunities |
| Customer Portal | Partial | Partial | Yes for protected routes | Mostly yes | No obvious fake path in audited public routes | P | Separate customer portal auth model not fully proven |
| Compliance | Real | Real | Yes | Yes | Seed fixtures exist | Y | None major |
| Reports | Partial | Real | Yes | Yes | Demo KPI scaffolds remain | P | Replace seed presentation surfaces |
| AI Recommendations | Real | Real | Yes | Yes | No direct-business-write path found | Y | Keep AI recommendation-only |

## Executive Summary

This build is not broad-but-shallow. It is broad-and-partly-real, with a strong backend spine and a few dishonest-looking surfaces on top.

Single biggest risk to going live with real customer or operational data: local secret hygiene plus demo masking creating false confidence in a system that is actually much more capable than some pages imply.

Highest-priority fixes by business risk:

1. Remove or quarantine local `.env` secrets from the workspace.
2. Remove the remaining seed/demo presentation scaffolds from public-facing dashboard-style pages.
3. Decide whether you want database-native tenant isolation (RLS) for defense in depth.
4. Finish exhaustive schema verification for CRM, finance, safety, and maintenance naming differences.
5. Keep AI strictly recommendation-only and keep the approval workflow auditable.

## Bottom Line

The core product is real. The biggest lie in the repo is not the backend; it is the demo-layer polish and the presence of local secret material.
