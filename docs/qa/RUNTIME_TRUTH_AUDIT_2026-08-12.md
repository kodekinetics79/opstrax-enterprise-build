# OpsTrax runtime truth audit — 2026-08-12

Status: **Staging NO-GO / Production NO-GO**

This ledger distinguishes source-code contracts from executed runtime evidence. A route, import, build, or CI pass is not proof that a module works. `SOURCE TRACED` below means only that the code path was followed. `NOT CERTIFIED` means authenticated browser, database-row, persistence, audit, and tenant-boundary evidence is still absent.

## Deployed production identification (read-only)

| Item | Observed value | Evidence |
|---|---|---|
| Public frontend | `https://opstrax.vercel.app` | Vercel production alias |
| Vercel deployment | `dpl_GBeuAV2tNXqDHAYdYUviD2vzCdfC` | `vercel inspect` and Vercel deployment API |
| Vercel project | `prj_ZObu3iKNIkMccLgdzShArd0yE1b8` | Vercel deployment metadata |
| Frontend Git ref | `main` | Vercel deployment metadata |
| Frontend exact SHA | `f6a18b98ef106250781d98057e34131bb2f7b3e6` | Vercel `gitSource.sha` and deployment metadata |
| Compiled API base URL | `https://osptrax-fleet-management.onrender.com` | deployed `index-DweGDMA0.js` inspection |
| Render service | `srv-d93dha0k1i2s73dm6ub0` (`Osptrax Fleet Management`) | Render read-only service inventory |
| API exact SHA | `f6a18b98ef106250781d98057e34131bb2f7b3e6` | `x-deployment-version: f6a18b98ef10` and `/health/live` version |
| Environment | `Production` | Vercel target and API health envelope |
| API liveness | `alive` | `GET /health/live`, HTTP 200 |
| API readiness | `not_ready` | `GET /health/ready`, HTTP 503 |
| Deep health | `unhealthy` | `GET /health/deep` |

The production frontend and API are the same old `main` SHA, not PR #19 and not the current certification branch. Production was not mutated and no production database or customer record was accessed.

### Why the production “Live” badge is false

The process is alive, but the readiness contract reports `fleet_production_contract_invalid`: one missing table, six grant violations, seventeen tenant-grant violations, one stale critical worker, and one raw critical-worker violation. Deep health identifies `MaintenanceBackgroundService` as stale. Database connectivity alone cannot establish a healthy application. The global badge was a hardcoded string in `AppShell.tsx` and did not consume any health endpoint.

## Identity and tenant boundary

For the target endpoints, the bearer session token is looked up in `user_sessions`, joined to the active user and role, and its database `company_id` is installed in `HttpContext.Items`. `GetCompanyId` has no numeric fallback and rejects missing/non-positive identity. With `Rls:EnforceTenantContext=true`, the request opens one application transaction, obtains a short-lived PID/transaction-bound tenant ticket through the distinct system identity, and installs it with `SET LOCAL` semantics. Target queries additionally bind `@cid`; branch-limited users also bind `@branchId`. This is source-level evidence only until the isolated staging cross-tenant browser/DB test executes.

## Field-level provenance matrix

| Surface / displayed fields | UI → query/client | HTTP → handler/service | Repository/query → table/view | Tenant/RLS boundary | Record origin | Evidence status |
|---|---|---|---|---|---|---|
| GPS: device, vehicle, driver, provider, status | `TelematicsCommandPage` → `useQuery` → `getGpsTrackingRecords` → `loadScopedDevices` | `GET /api/telemetry/devices` → `TelemetryDeviceList` → `DeviceList` | explicit select from `eld_devices`, joined `vehicles` and `drivers` | bearer session company + explicit `e.company_id=@cid` + branch + request RLS | provisioned device, provider sync, legacy seed, or unknown per row; no staging DB evidence yet | SOURCE TRACED; NOT CERTIFIED |
| GPS: lat/lng, speed, heading, address, engine, fuel, odometer, voltage, timestamps | `getGpsTrackingRecords` → `fetchPositions` → `toClusterRecord` | `GET /api/telemetry/positions` → `TelemetryPositions` | `latest_vehicle_positions`, joined `vehicles`/`drivers`; historical replay uses `location_events` | explicit `lvp.company_id=@cid` + branch + RLS | `source`, `provider`, `protocol`, device-fix and gateway timestamps are returned; deployed row origin unknown | SOURCE TRACED; production request fails/unavailable; NOT CERTIFIED |
| GPS freshness / “current fix” | `toClusterRecord` consumes server `freshness`, `is_stale`, fix/receipt timestamps | `TelemetryPositions` computes worst of receipt age and device-fix age | timestamps in `latest_vehicle_positions` | same as position row | live ≤120s, delayed ≤900s, otherwise stale; no frontend invention after absent fix | SOURCE TRACED; NOT CERTIFIED |
| OBD/J1939 protocol and engine fields | `TelematicsCommandPage` → `getDiagnosticsRecords` → `toClusterRecord` | positions + `GET /api/maintenance/fault-codes?status=active` | `latest_vehicle_positions` plus `fault_codes` | explicit company/branch predicates + RLS | protocol from position provenance or device model; DTC from persisted fault observation; missing engine channels display `—` | SOURCE TRACED; production page error observed; NOT CERTIFIED |
| OBD/J1939 DTC, severity, description, last seen | `fetchActiveFaultsIfAuthorized` → cluster mapping | `MaintFaultCodesList` | `fault_codes` joined tenant vehicle | explicit `fc.company_id=@cid`, branch + RLS | native/device/provider observation or unknown until row evidence | SOURCE TRACED; NOT CERTIFIED |
| Device Health identity/assignment/firmware/check-in | `IotDevicesPage` → `getDevices/getDeviceById` → `mapDeviceRow` | device list/detail endpoints | `eld_devices`, `vehicles`, `drivers` | explicit company/branch + RLS | persisted device record; legacy seed remains possible and must be labeled from staging data | SOURCE TRACED; NOT CERTIFIED |
| Device Health score/status | `mapDeviceRow` client derivation | device + optional faults + optional telemetry alerts | `eld_devices.last_seen_at/status/revoked_at`, `fault_codes`, `telemetry_alerts` | each feed permission and tenant scoped | deterministic client calculation from real signals; unavailable when no signals | SOURCE TRACED; derived value, NOT CERTIFIED |
| Work Orders list fields and totals | `MaintenanceCommandPage` → `maintenanceApi.workOrders` | `GET /api/maintenance/work-orders` → `MaintWorkOrdersList` | `work_orders`, `vehicles`, `assets`, `users` | explicit `wo.company_id=@cid`, branch + RLS | `WO-B3-*`/Batch 3 patterns classified `seeded_synthetic_database`; all others remain `unknown_database_record` until correlated | SOURCE TRACED; NOT CERTIFIED |
| Work Order create/complete | dialogs → mutation → `createWorkOrder/completeWorkOrder` | POST create/complete handlers | insert/update `work_orders`; `AuditService` writes audit event | permissions, explicit company/branch validation + RLS | user-created staging record after execution | SOURCE TRACED; persistence/audit browser proof NOT EXECUTED |
| Service History | `MaintenancePlanningPage` → `serviceHistoryApi` | `GET /api/service-history` | completed `work_orders`, joined `vehicles`/`drivers` | explicit company + branch + RLS | known Batch 3 patterns labeled Demo Data | SOURCE TRACED; NOT CERTIFIED |
| Service History actual cost total | page reducer over returned rows | endpoint now returns `actual_cost` separately from `estimated_cost` | `work_orders.actual_cost` | same as service history | missing actual cost is `—`; estimates are not relabeled as actuals | REGRESSION ADDED; runtime NOT CERTIFIED |
| Downtime hours / estimated cost | `MaintenancePlanningPage` → `downtimeApi`; page reducers use same response rows | `GET /api/downtime` | `work_orders.downtime_hours`, `estimated_cost`, joined vehicle | explicit company + branch + RLS | known Batch 3 patterns labeled Demo Data | SOURCE TRACED; NOT CERTIFIED |
| PM Schedule due/status/risk/cost | `MaintenancePlanningPage` → `pmApi` | `GET /api/preventive-maintenance` | `maintenance_items` joined `vehicles`; status/days computed in SQL | explicit company + branch + RLS | `B3 maintenance item *`/seed description labeled Demo Data | SOURCE TRACED; NOT CERTIFIED |
| CSV exports | tab export calls the same API mapper used by the displayed tab | same endpoint as tab | same filtered tenant/branch query, maximum 50 for history/downtime | same as screen | same origin tag included | SOURCE TRACED; byte/row reconciliation NOT EXECUTED |

## Synthetic, fallback, cache, and identity inventory

The repository scan found 134 candidate files containing seed/mock/sample/demo/fallback vocabulary. The classifications below cover executable or data-bearing sources relevant to runtime truth; ordinary UI fallback labels and test-only fixtures are not operational records.

| Source | Classification | Runtime finding / disposition |
|---|---|---|
| `frontend/src/data/developmentFleetSeedData.ts` | frontend hardcoded | contains Avery Stone and patterned demo entities. It was statically imported by `OperatingModulePage`; those strings were present in the production bundle despite the tree-shaking comment. Runtime import removed; production bundle scan now finds none of the known identities/patterns. |
| `frontend/src/data/mockOperatingData.ts` | frontend hardcoded | dependency of the development seed graph; quarantined from production application imports. |
| `backend-dotnet/Services/Batch1SchemaService.cs` through `Batch7SchemaService.cs` | backend hardcoded seed generators | schema execution runs at startup, but fabricated inserts are behind `DemoSeedGate`. They use legacy company/tenant `1` and must not be enabled in staging or production. |
| `Batch3SchemaService.cs` | backend hardcoded → seeded synthetic database data | generates `B3 maintenance item N`, `WO-B3-*`, `Batch 3 work order N`, vendors, costs, and downtime. API responses now classify these known rows as `seeded_synthetic_database`; UI labels them `Demo Data`. |
| `DemoSeedGate.cs` | demo-mode environment flag | only explicit `ENABLE_LEGACY_BATCH_DEMO_SEED=true` or equivalent enables legacy batch inserts. Production deep config currently reports demo flags disabled; existing seeded rows can persist and are still synthetic. |
| `database/init/002_seed.sql` | seeded synthetic database data | creates `OpsTrax Demo Logistics`, admin/demo identities including Avery Stone. Local/demo only; forbidden for staging certification seeding. |
| `database/init/003_live_telemetry_seed.sql`, `006_local_telemetry_live_state_seed.sql` | seeded synthetic database data | local telemetry rows; must never be treated as physical/provider evidence. |
| `database/seeds/acme_pilot_harness.sql` | seeded synthetic database data | deterministic test harness, including map positions; staging use must be explicitly labeled if selected. |
| `TelemetrySimulatorBackgroundService.cs` | backend synthetic generator | config-gated; production deep health says simulator disabled. Must remain disabled for runtime certification except an explicitly labeled simulator-only test. |
| session storage (`opstrax.session.v2` and retired keys) | authenticated client cache | holds session/token only; it is not an operational data fallback. Invalid JSON clears the session. |
| sidebar/i18n preferences | UI preference cache | local storage only; not business records. |
| offline mutation queue | cached pending actions | separate risk surface; not used by the target GPS/OBD/maintenance pages in this matrix. Requires its own failure/replay certification. |
| target-page API catch/error paths | unavailable | no target page substitutes sample rows after API failure. Telematics and maintenance show error + Retry or honest empty state. |
| global `Live` in `AppShell` | frontend hardcoded false health | removed. Runtime status now requires readiness, database contract, all critical workers, and telemetry worker freshness. Synthetic tenant names force `Demo Data`. |

## Remediation and regression ledger

| ID | Defect | Change | Evidence | State |
|---|---|---|---|---|
| RT-001 | Global status always rendered `Live` | fail-closed runtime diagnostics using `/health/ready` + `/health/deep` | runtime truth tests + frontend build | FIXED IN BRANCH; NOT DEPLOYED |
| RT-002 | No visible frontend/API SHA, environment, or API URL | Vite injects immutable build metadata; About shows runtime provenance | runtime truth tests + frontend build | FIXED IN BRANCH; NOT DEPLOYED |
| RT-003 | Production bundle contained Avery Stone/demo fleet graph | removed runtime seed import; generic unwired shell is empty | built-bundle synthetic string scan | FIXED IN BRANCH; NOT DEPLOYED |
| RT-004 | Batch 3 database rows looked operational | backend origin classification + `Demo Data` badges | source regression | FIXED IN BRANCH; NOT DEPLOYED |
| RT-005 | Completed service “Total Cost” could use estimated cost | return/use actual cost only; missing actual is explicit | source regression | FIXED IN BRANCH; NOT DEPLOYED |
| RT-006 | legacy maintenance list endpoints relied only on RLS | added explicit company predicates and tenant-safe joins, retaining branch + RLS | backend build/test pending | FIXED IN BRANCH; NOT DEPLOYED |
| RT-007 | production API is not ready and telematics UI requests fail | isolated staging API/database migration and authenticated runtime evidence required | production read-only health + supplied screenshot | OPEN; blocks GO |

## Staging plan, cost, and blockers

| Resource | Minimum configuration | Expected monthly cost |
|---|---|---:|
| Vercel staging frontend | preview/staging deployment, exact SHA | $0 incremental |
| Render API + embedded workers | Starter, one instance, no autoscaling | $7 |
| Neon PostgreSQL | Free project, scale to zero, 0.5 GB, max 2 CU | $0 |
| One-off migrator | API one-off command using migrator identity | $0 incremental |
| Evidence artifacts | GitHub Actions retention | $0 incremental |
| **Expected recurring total** | | **$7/month** |

Neon Free provides 100 CU-hours/project and a six-hour restore window. That is sufficient for a bounded 10,000-row certification attempt but constrains the recovery drill. The current machine has authenticated Vercel, Render, and GitHub sessions but no Neon CLI/session, so database creation is **BLOCKED by unavailable Neon credentials**. No uncertain or production database will be substituted.

Browser control is also unavailable in the current tool session. Therefore no authenticated browser, network trace, screenshot, create/refresh persistence, audit-row, or cross-tenant denial is claimed. The existing Playwright personas remain unexecuted until the isolated endpoints and disposable credentials exist.

## Certification counts (current runtime-truth scope)

| Category | Executed | Passed | Failed | Blocked / not executed |
|---|---:|---:|---:|---:|
| Runtime-truth source regression tests | 8 | 8 | 0 | 0 |
| Complete launch-tooling source suite | 44 | 44 | 0 | 0 |
| Focused telemetry/security backend regressions | 39 | 39 | 0 | 0 |
| Broader backend selection | 66 | 60 | 0 product failures | 6 PostgreSQL credential blocks |
| Production bundle synthetic-string scan | 1 | 1 | 0 | 0 |
| Authenticated target-module browser journeys | 0 | 0 | 0 | all |
| UI → API → database → persistence → audit journeys | 0 | 0 | 0 | all |
| Cross-tenant/branch runtime matrix | 0 | 0 | 0 | all |
| 10,000-row/load/replay/back-pressure | 0 | 0 | 0 | all |
| Backup/restore drill | 0 | 0 | 0 | 1 environment drill |
| Samsara provider | 0 | 0 | 0 | BLOCKED: credentials absent |
| PT40/GT06 physical device | 0 | 0 | 0 | BLOCKED: hardware absent |
| Physical iOS/Android | 0 | 0 | 0 | BLOCKED: devices absent |

No module is labeled wired, functional, revived, or passed in this ledger. Staging and Production remain **NO-GO**.
