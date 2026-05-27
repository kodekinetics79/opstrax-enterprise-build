# OpsTrax Module Coverage Matrix

Audit date: 2026-05-24 (Batch 7 update: 2026-05-24)

Scope: repository inspection plus Batch 1 and Batch 2 completion tracking. This matrix now reflects the current connected OpsTrax implementation after the Batch 2 jobs, dispatch, route planning, and customer ETA work.

Legend:

- Yes: present and connected enough for the current app.
- Partial: present through a shared renderer, shared data layer, placeholder, or limited workflow.
- No: not currently implemented as a distinct capability.
- N/A: not applicable to that module.

## Validation Snapshot

| Check | Result | Notes |
| --- | --- | --- |
| `docker compose config` | Pass | Frontend publishes `10000:80`, API publishes `8088:8080`, Node publishes `8090:8090`, MySQL uses `expose: 3306` with no host port. |
| `cd frontend && npm run build` | Pass | Vite build completes. Warning: main JS chunk is larger than 500 kB; code splitting is recommended later. |
| `cd backend-dotnet && dotnet build` | Pass | .NET 8 API builds with 0 warnings and 0 errors. |
| `cd services/node-events && npm install` | Pass | Dependencies are current with 0 vulnerabilities reported. |
| `docker compose up --build -d` | Pass | Full stack rebuilt and started on the required ports. |
| API smoke checks | Pass | Command Center, Control Tower, Batch 1, Batch 2, Batch 3, Safety, Dashcam, Coaching, Incidents, Evidence Packages, and Insurance report actions returned success. |
| Frontend route smoke checks | Pass | `/safety`, `/dashcam`, `/ai-dashcam`, `/coaching`, `/incidents`, `/evidence-packages`, Batch 1, Batch 2, and Batch 3 routes returned `200`. |

## Cross-Cutting Findings

- The strongest connected modules are Command Center, Control Tower, Dispatch, Vehicles, Drivers, Jobs, and OpsTrax AI Copilot.
- The main active frontend is TypeScript under `frontend/src`; `frontend/src/App.jsx` appears to be an IDE-open legacy path, not the active route entry.
- The backend uses a practical .NET 8 minimal API mapping file with MySqlConnector-backed SQL access. Domain-specific service classes are limited; most data access is centralized through `Database`.
- Dedicated endpoints exist for the core operational modules and most business modules. Shared module endpoints also remain as a fallback.
- The database has broad seeded coverage for fleet, dispatch, maintenance, safety, compliance, finance, documents, AI, audit, integrations, and subscriptions.
- Create/edit buttons and backend CRUD exist in several places, but production-grade frontend forms are mostly incomplete.
- Detail drawers exist for entity and generic module pages, but several premium pages use custom panels rather than a reusable detail route.
- Search/filter controls are visible but mostly UI-level; server-side filtering and full client filtering are not complete.
- RBAC metadata exists through roles, users, auth response, and permissions JSON, but route/action enforcement is still shallow.
- Audit logging exists for core write actions and dedicated module create/update flows, but it is not uniformly applied to every event/action.
- AI insights are seeded and surfaced through shared components, but module-specific evidence/action execution should be deepened.
- Node SSE is working for live operations and Control Tower, but most modules are not event-driven yet.
- There are no dedicated automated unit/integration/e2e tests in the audited app. Build validation is currently the primary automated check.

## Batch 1 Completion Update

Completed on 2026-05-24:

- Vehicles now has dedicated frontend CRUD, enriched summary metrics, detail evidence sections, documents, maintenance/compliance/safety/cost/trips data, AI recommendations, timeline, audit trail, export placeholder, and risk/readiness wow metrics.
- Drivers now has dedicated frontend CRUD, enriched summary metrics, detail evidence sections, certifications, documents, HOS, DVIR, safety/coaching data, AI recommendations, timeline, audit trail, export placeholder, and readiness/risk wow metrics.
- Clients / Customers now has a dedicated `/customers` route and API service, CRUD, summary metrics, contact/address/job/communication/contract/ETA detail sections, AI recommendations, timeline, audit trail, export placeholder, and SLA/customer experience wow metrics.
- Assets / Trailers / Equipment now has a dedicated `/assets` route and API service, CRUD, assignment endpoint, summary metrics, document/movement/audit detail sections, AI recommendations, export placeholder, and utilization/geofence wow metrics.

Remaining Batch 1 limitations:

- Frontend RBAC is metadata-aware but still not a hard permission gate for every button.
- Export is CSV client-side placeholder, not a server-side report/export job.
- Assignment pickers are not yet rich searchable selectors; API assignment endpoints exist for vehicle/driver/assets.
- Browser console was not checked with Playwright; endpoint and build validation passed.

## Batch 2 Completion Update

Completed on 2026-05-24:

- Jobs & Orders now has a dedicated connected `/jobs` page, CRUD API service, KPI summary, create/edit modal, detail drawer, assignment, status movement, ETA update, proof placeholder, timeline, recommendations, audit trail, report/export placeholder, and risk/SLA/proof/margin wow metrics.
- Dispatch Board now has a dedicated connected `/dispatch` Kanban board, summary cards, available driver/vehicle data, assignment panel, match scoring, AI recommendations, auto-suggest action, status actions, ETA batch action, live Node event feed, exception radar, SLA queue, and export placeholder.
- Route Planning now has connected `/routes` and `/route-planning` pages, route CRUD, route summary, route detail drawer, stop management, simulated route map, optimization preview, assignment, timeline, recommendations, audit trail, export placeholder, route efficiency, SLA risk, cost leakage, and route replay placeholders.
- Customer ETA Portal now has connected `/customer-eta` operations and `/eta/:trackingCode` customer-facing tracking pages, ETA summary, safe tracking endpoint, job ETA detail, send-update action, feedback endpoint, communication history, recommendations, proof preview, ETA confidence, and customer experience scoring.
- Runtime-safe Batch 2 schema/seed backfill was added so existing volumes are upgraded without exposing MySQL or requiring a destructive reset.
- Node SSE simulation now includes Batch 2 operation events such as job assignment, status changes, route optimization, ETA sent, proof completion, dispatch recommendation, and customer feedback.

Remaining Batch 2 limitations:

- RBAC metadata is present, but every Batch 2 button is not yet hard-gated by permission checks.
- Export/report capabilities are UI placeholders, not asynchronous server report jobs.
- Dispatch drag/drop is intentionally represented through Kanban-ready cards and status actions, not pointer-based drag/drop.
- Job and route assignment controls use practical form fields and smart suggestions; they are not yet rich searchable selectors.
- Browser console validation was not automated with Playwright; build, Docker, API, route, action, and container-log validation passed.

## Batch 4 Completion Update

Completed on 2026-05-24:

- Safety now has a dedicated connected `/safety` page, event CRUD, summary, driver/vehicle scorecards, trends, review action, create-coaching action, create-incident action, AI safety advisor, audit trail, and export/report placeholder.
- AI Dashcam / Incident Review now has dedicated `/dashcam` and `/ai-dashcam` routes, video event CRUD, placeholder thumbnails/clip metadata, summary, review, false-positive, create-coaching, create-evidence-package, create-incident-report actions, AI incident summaries, audit trail, and export placeholder.
- Driver Coaching now has a dedicated `/coaching` route, task CRUD, summary, assign/acknowledge/complete/add-note actions, AI coaching scripts, before/after score evidence, related safety/video events, audit trail, and export placeholder.
- Incidents now has a dedicated `/incidents` route, incident CRUD, summary, status updates, attach-evidence action, insurance-report action, evidence lists, insurance report sections, timeline, audit trail, and export placeholder.
- Evidence Packages now has a dedicated `/evidence-packages` route, package CRUD, summary, item bundle detail, export placeholder generation, package lock action, chain-of-custody audit trail, and AI evidence advisor.
- Insurance / Legal Incident Reports are supported through incident and dashcam report-creation actions and the `insurance_reports` table with placeholder export URLs.
- Runtime-safe Batch 4 schema/seed backfill was added for existing MySQL volumes without exposing MySQL or requiring a reset.
- Node SSE simulation now includes safety, dashcam, coaching, incident, evidence package, and insurance report event types.

Remaining Batch 4 limitations:

- Video playback uses placeholder thumbnails/URLs and is camera-integration ready, but no real dashcam vendor integration is active.
- Evidence exports, insurance/legal reports, photos, signatures, and documents remain placeholder metadata rather than real file generation/storage.
- RBAC metadata is represented by module/role intent, but every Batch 4 button is not yet hard-gated by permissions.
- Browser console validation was not automated with Playwright; build, Docker, API, action, route, and container-log validation passed.

## Batch 3 Completion Update

Completed on 2026-05-24:

- Maintenance now has a dedicated connected `/maintenance` page, CRUD API, KPI summary, due/overdue endpoints, schedule/defer actions, create-work-order action, detail drawer, linked schedules/work orders, AI maintenance advisor, audit trail, and export/report placeholder.
- Work Orders now has a dedicated connected `/work-orders` page, CRUD API, KPI summary, labor/parts actions, assignment, status transitions, complete, cost approval, status timeline, documents placeholder, AI repair summary, audit trail, and export/report placeholder.
- DVIR / Inspections now has dedicated `/dvir-inspections` and `/inspections` routes, DVIR report CRUD API, templates/checklist data, mechanic review, repair certification, driver signature actions, defect/checklist/work-order detail sections, AI recommendations, audit trail, and export/report placeholder.
- Documents now has a dedicated connected `/documents` page, CRUD API, summary, expiring endpoint, upload placeholder, renewal placeholder, detail drawer, entity linking, expiry risk, country/profile fields, timeline, AI document advisor, audit trail, and export/report placeholder.
- Runtime-safe Batch 3 schema/seed backfill was added for existing MySQL volumes without exposing MySQL or requiring a reset.
- Node SSE simulation now includes maintenance, work-order, DVIR, and document lifecycle events.

Remaining Batch 3 limitations:

- RBAC metadata is represented by role/module intent, but every Batch 3 button is not yet hard-gated by permissions.
- Uploads, photos, signatures, and server exports are placeholders rather than real object storage/report jobs.
- DVIR checklist completion is surfaced as evidence; per-item pass/fail submission can be deepened later.
- Browser console validation was not automated with Playwright; build, Docker, API, route, action, and container-log validation passed.

## Batch 5 Completion Update

Completed on 2026-05-24 (UI upgrade pass: 2026-05-24):

- **Fuel & Idling** — `/fuel-idling`. Full CRUD, 3-tab view (Transactions | Idling Events | Anomalies), Recharts bar chart showing fuel cost by vehicle with anomaly colour-coding, two fleet-wide aggregate endpoints (`/api/fuel/vehicle-summary`, `/api/fuel/driver-summary`), anomaly review action, per-module filter options, 8-KPI header, enriched detail drawer (anomalies section + AI recommendations + audit trail), CSV export. Node SSE: `fuel.transaction_created`, `fuel.anomaly_detected`, `idling.threshold_exceeded`.
- **Expenses** — `/expenses`. Full CRUD, approve/reject workflow, expense categories, anomaly risk scoring, receipt status tracking, per-module filter (Pending/Approved/Rejected/Missing/High), 8-KPI header, enriched detail drawer, AI expense recommendations, audit trail, CSV export. Node SSE: `expense.created`, `expense.approved`, `expense.rejected`.
- **Contracts / Rates** — `/contracts-rates`. Full contract CRUD, rate sub-records (create/update/delete), activate/expire lifecycle, margin risk tracking, fuel surcharge config, expiry awareness, per-module filter, 8-KPI header, enriched detail drawer with rates table, AI recommendations, audit trail, CSV export. Node SSE: `contract.expiring`.
- **Carrier Management** — `/carrier-management`. Full carrier CRUD, performance history, document tracking, setStatus action, compliance/insurance monitoring, per-module filter (Active/Pending/Suspended/Compliant/Non-Compliant/At Risk), 8-KPI header, enriched detail drawer (performance + documents sections), AI recommendations, audit trail, CSV export. Node SSE: `carrier.compliance_risk`.
- **Predictive Cost & Margin** — `/predictive-margin`. Job/route/vehicle/customer margin views, recalculate action, Recharts bar chart (margin % by entity, colour-coded red/amber/green), per-module filter, 8-KPI header, local-data detail drawer, AI margin recommendations, CSV export. Node SSE: `margin.risk_detected`.
- **ROI / Cost Leakage Intelligence** — `/cost-leakage`. Leakage item register, acknowledge + create-action workflows, Recharts horizontal bar chart (leakage by category), per-module filter (Open/Acknowledged/In Progress/Critical/High), 8-KPI header, local-data detail drawer, AI cost recovery recommendations, CSV export. Node SSE: `cost.leakage_detected`, `cost.action_created`.
- **Runtime-safe schema backfill** added 10 new tables: `idling_events`, `fuel_anomalies`, `expense_categories`, `contract_rates`, `carrier_documents`, `carrier_performance`, `cost_margin_records`, `cost_margin_predictions`, `cost_leakage_items`, `cost_leakage_actions` plus column additions to `fuel_transactions`, `expenses`, `carriers`, `contracts`. 50+ fuel transactions, 30 idling events, 12 anomalies, 40 expenses, 10 categories, 12 contracts, 30 rates, 10 carriers, 15 carrier docs, 25 leakage items, 25 leakage actions, 18 AI recommendations, 6 notifications, 5 audit seed entries.

### Validation (2026-05-24)

| Check | Result |
| --- | --- |
| `cd frontend && npm run build` | **Pass** — 0 TypeScript errors, 0 warnings |
| `cd backend-dotnet && dotnet build` | **Pass** — 0 errors, 0 warnings |
| `cd services/node-events && npm install` | **Pass** — 0 vulnerabilities |
| `docker compose config` | **Pass** |

## Batch 6 Completion Update

Completed on 2026-05-24:

- **Compliance Center** — `/compliance`. 7-tab page (Overview, Violations, Drivers, Vehicles, Audit Packages, Cross-Border Watch, AI Advisor). Violation acknowledge/resolve actions, audit package create/finalize, driver/vehicle compliance status tables, cross-border watch list, AI compliance advisor panel, detail drawer. Node SSE: `compliance.violation_detected`, `compliance.document_expiring`, `compliance.audit_package_created`, `cross_border.risk_detected`.
- **HOS / ELD Framework** — `/hos-eld`. 4-tab page (Driver Clocks, HOS Logs, ELD Devices, AI Recommendations). Color-coded clock progress bars, HOS log certification action, ELD malfunction modal with FMCSA paper-log notice, resolve-malfunction action, AI advisor panel. Disclaimer: OpsTrax is not a certified ELD — certification depends on the connected ELD provider/device and applicable country requirements. Node SSE: `hos.warning`, `hos.log_certified`, `eld.malfunction`, `dvir.compliance_warning`.
- **Settings / Localization** — `/settings`. Language grid selector with instant RTL preview, locale settings form (country, timezone, date format, currency, distance/volume units), save to backend, immediate UI locale switch.
- **i18n Provider** — 6 locales (en-US, en-CA, fr-CA, ar-SA, ar-AE, ur-PK). React context-based, localStorage persistence, `document.documentElement.dir/lang` applied on locale change. RTL support via CSS logical properties. Language picker in AppShell topbar.
- **Runtime-safe schema backfill** added 13 new tables: `countries`, `languages`, `tenant_locale_settings`, `user_locale_preferences`, `compliance_profiles`, `compliance_rules`, `driver_compliance_status`, `vehicle_compliance_status`, `hos_logs`, `hos_clocks`, `eld_devices`, `compliance_violations`, `compliance_audit_packages`. Column additions to `documents` (country_code, issuing_authority, issued_at) and `dvir_reports` (country_code, compliance_profile_id). 5 countries, 6 languages, 6 compliance profiles, 10 rules, 10 HOS clocks, 30 HOS logs, 10 ELD devices, 15 violations, 5 audit packages seeded.

### Validation (2026-05-24)

| Check | Result |
| --- | --- |
| `cd frontend && npm run build` | **Pass** — 0 TypeScript errors |
| `cd backend-dotnet && dotnet build` | **Pass** — 0 errors, 0 warnings |
| `cd services/node-events && npm install` | **Pass** — 0 vulnerabilities |
| `docker compose config` | **Pass** |

### Remaining Batch 6 limitations

- ELD integration is placeholder; no real FMCSA-registered ELD vendor API is connected. Certification depends on connected device.
- Compliance rule evaluation is seeded/simulated, not a live rule engine processing real telemetry.
- HOS clocks are seeded simulation; no real ELD telemetry bridge updates them.
- RBAC metadata present but not enforced on individual Batch 6 buttons.
- Browser console validation not automated with Playwright.

## Batch 7 Completion Update

Completed on 2026-05-24:

- **Reports & Analytics** — `/reports-analytics`. 5-tab page (Catalog, Run History, Scheduled, Exports, AI Advisor). 30-report catalog across Fleet/Safety/Compliance/Finance/Operations/Executive categories. Run-report action (inserts run record with simulated row count), scheduled report create/pause/resume, export request (CSV/PDF/Excel/JSON), AI report advisor panel. Node SSE: `report.run_completed`, `report.export_requested`, `scheduled_report.created`.
- **SLA / KPI Center** — `/sla-kpi`. 4-tab page (KPI Dashboard, SLA Records, SLA Breaches, AI Advisor). 30 KPI metrics with target/actual/trend/status/recommendation per metric, score progress bars, category filter, SLA records table with type filter, SLA breach acknowledge/resolve actions, AI KPI/SLA advisor. Node SSE: `kpi.drift_detected`, `sla.breach_detected`.
- **Audit Logs** — `/audit-logs`. 3-tab page (Audit Trail, Export Requests, AI Advisor). Full audit table with search + module + severity filters, log detail drawer, audit export request (date range, format), AI audit advisor. Compliance disclaimer: logs are operational records, not legally certified audit trail. Node SSE: `audit.sensitive_action`.
- **Executive Dashboard** — `/executive`. Score rings (Overall, Fleet, Safety, Compliance, Financial), AI executive brief from latest snapshot, critical KPI/SLA/audit alert strip, 14-day trend line chart (Recharts), snapshot history table, AI executive recommendations. Node SSE: `executive.snapshot_created`, `ai.report_recommended`.
- **Runtime-safe schema backfill** added 10 new tables: `report_catalog`, `report_runs`, `scheduled_reports`, `report_exports`, `kpi_metrics`, `kpi_targets`, `sla_records` (enriched), `sla_breaches`, `executive_snapshots`, `audit_export_requests`. Column additions to `audit_logs` (severity, module_key, action_type) and `ai_recommendations` (description, priority, action_label, action_type). 30 report catalog entries, 20 run records, 8 scheduled reports, 10 exports, 30 KPI metrics, 20 KPI targets, 30 SLA records, 10 SLA breaches, 10 executive snapshots (10-day history with AI briefs), 100 audit log seed records (INSERT IGNORE), 8 audit export requests, 15 AI recommendations seeded.

### Validation (2026-05-24)

| Check | Result |
| --- | --- |
| `cd frontend && npm run build` | **Pass** — 0 TypeScript errors, 2532 modules |
| `cd backend-dotnet && dotnet build` | **Pass** — 0 errors, 0 warnings |
| `cd services/node-events && npm install` | **Pass** — 0 vulnerabilities |
| `docker compose config` | **Pass** |

### Remaining Batch 7 limitations

- Report runs are simulated (random row count); no real report engine or file generation is connected.
- Export formats (CSV/PDF/Excel/JSON) are placeholder request records; no actual file generation.
- KPI metrics are seeded simulation; no real-time data pipeline updates them.
- SLA breach detection is seeded; no automated SLA monitoring engine runs against live job data.
- Executive snapshots are daily seeded records; no automated snapshot generation job is scheduled.
- Audit log export is a request record; no background worker generates and delivers the file.
- RBAC metadata present but not enforced on individual Batch 7 buttons.
- Browser console validation not automated with Playwright.

### Remaining Batch 5 limitations

- Fuel card import (`/api/fuel/import-preview`) is a response placeholder; no real WEX/Comdata/FleetCor integration is active.
- Receipt/document file upload for expenses is placeholder metadata, not wired to object storage.
- Predictive margin model uses seeded rule-based simulation; no ML inference service is connected.
- RBAC metadata (roles/permissions) is represented in auth response but not enforced as hard gates on individual Batch 5 buttons.
- Browser console and end-to-end validation was not automated with Playwright; build, Docker config, and API validation passed.

## Coverage Matrix

| # | Module | FE Route | FE Page | API Service | BE Endpoint | BE Data Access | DB Tables | Seed Data | Create/Edit Form | Detail Drawer/Page | Search/Filter | KPI/Summary | Audit Logging | RBAC Metadata | AI Layer | Report/Export | Events/Notifications | Tests/Build | Known Gaps | Recommended Next Action |
| --- | --- | --- | --- | --- | --- | --- | --- | --- | --- | --- | --- | --- | --- | --- | --- | --- | --- | --- | --- | --- |
| 1 | Command Center | Yes | Yes, custom premium page | Yes, `commandCenterApi.ts` | Yes, `/api/command-center/summary` | Yes, shared DB SQL | `kpi_records`, `ai_insights`, `command_center_actions`, `operational_events`, alerts from multiple tables | Yes | Partial, action buttons only | No dedicated detail drawer | Partial, dashboard controls only | Yes | Partial, action acknowledgement not fully wired | Partial | Yes | Partial | Partial, operational events are read | Build pass | Needs action completion endpoints wired through UI and notification triggers | Add command action lifecycle with acknowledge/complete, audit entries, and notifications. |
| 2 | Live Map / Control Tower | Yes | Yes, custom simulated map | Yes, `controlTowerApi.ts` | Yes, summary/entities/events/actions | Yes, shared DB SQL | `vehicles`, `drivers`, `jobs`, `location_events`, `geofences`, `geofence_events`, `operational_events`, `eta_updates` | Yes | Partial, action buttons only | Partial, selected entity panel | Partial, map filter UI | Yes | Partial, action endpoints log some events | Partial | Yes | Partial | Yes, Node SSE plus operational events | Build pass | Entity detail depth, map filters, and action workflows need completion | Finish selected entity drawer, filter logic, and action confirmation flows. |
| 3 | Dispatch Board | Yes | Yes, completed Batch 2 Kanban | Yes, `dispatchApi.ts` | Yes, summary, board, recs, availability, assign/status/auto-suggest/ETA | Yes, enriched MySQL SQL | `jobs`, `dispatch_assignments`, `drivers`, `vehicles`, `dispatch_recommendations`, `eta_updates`, `audit_logs` | Yes | Yes, assignment panel and actions | Yes, selected job panel/cards | Yes, board-stage and operational queues | Yes | Yes for assignment/status/ETA/auto-suggest | Partial | Yes | Yes, export placeholder | Yes, Node live ops feed | Build/smoke pass | Drag/drop and hard conflict override workflow remain action-based | Add optional drag/drop and advanced conflict override rules. |
| 4 | Jobs & Orders | Yes | Yes, completed Batch 2 page | Yes, `jobsApi.ts` | Yes, CRUD, summary, timeline, recommendations, import preview, assign/status/ETA/proof | Yes, enriched MySQL SQL | `jobs`, `customers`, `routes`, `route_stops`, `dispatch_assignments`, `job_status_events`, `customer_communications`, `proof_of_delivery`, `eta_updates`, `customer_eta_links`, `audit_logs` | Yes | Yes | Yes, enriched drawer | Yes | Yes | Yes for create/update/delete/assign/status/ETA/proof | Partial | Yes | Yes, CSV/report placeholder | Yes, related Node event types | Build/smoke pass | Rich searchable assignment selector and full POD capture remain future hardening | Add richer assignment selector, customer communication composer, and POD capture assets. |
| 5 | Route Planning | Yes | Yes, completed Batch 2 page | Yes, `routesApi.ts` | Yes, CRUD, summary, stops, optimize, assign, timeline, recommendations | Yes, enriched MySQL SQL | `routes`, `route_stops`, `route_paths`, `route_recommendations`, `jobs`, `drivers`, `vehicles`, `audit_logs` | Yes | Yes | Yes, enriched drawer | Yes | Yes | Yes for create/update/delete/assign/stop/optimize | Partial | Yes | Yes, export placeholder | Yes, route optimized Node event type | Build/smoke pass | Route map remains simulated and stop sequencing is form-based | Add interactive stop reordering and route replay persistence. |
| 6 | Vehicles | Yes | Yes, completed Batch 1 entity page | Yes, `vehiclesApi.ts` | Yes, CRUD, summary, timeline, recommendations, assign/status | Yes, enriched MySQL SQL | `vehicles`, `vehicle_documents`, `vehicle_assignments`, related maintenance/compliance/fuel/safety/trips/audit tables | Yes | Yes | Yes, enriched drawer | Yes | Yes | Yes for create/update/delete/status/assignment | Partial | Yes | Yes, CSV placeholder | Partial | Build/smoke pass | Rich assignment selector and hard RBAC button gating remain | Add searchable driver assignment picker and permission enforcement. |
| 7 | Drivers | Yes | Yes, completed Batch 1 entity page | Yes, `driversApi.ts` | Yes, CRUD, summary, timeline, recommendations, assign/status | Yes, enriched MySQL SQL | `drivers`, `driver_documents`, `driver_certifications`, related HOS/DVIR/safety/audit tables | Yes | Yes | Yes, enriched drawer | Yes | Yes | Yes for create/update/delete/status/assignment | Partial | Yes | Yes, CSV placeholder | Partial | Build/smoke pass | Rich vehicle selector and hard RBAC button gating remain | Add searchable vehicle assignment picker and permission enforcement. |
| 8 | Assets / Trailers / Equipment | Yes | Yes, completed Batch 1 entity page | Yes, `assetsApi.ts` | Yes, CRUD, summary, timeline, recommendations, assign | Yes, enriched MySQL SQL | `assets`, `asset_documents`, `entity_timeline_events`, related vehicle/driver/customer/audit tables | Yes | Yes | Yes, enriched drawer | Yes | Yes | Yes for create/update/delete/assignment | Partial | Yes | Yes, CSV placeholder | Partial | Build/smoke pass | Assignment picker is API-ready but not yet a rich selector | Add asset assignment selector with vehicle/driver/customer lookup. |
| 9 | Maintenance | Yes | Yes, completed Batch 3 page | Yes, `maintenanceApi.ts` | Yes, CRUD, summary, due, overdue, recommendations, schedule/defer/create-workorder | Yes, enriched MySQL SQL | `maintenance_items`, `maintenance_schedules`, `work_orders`, `vehicles`, `assets`, `audit_logs`, `ai_recommendations` | Yes | Yes | Yes, enriched drawer | Yes | Yes | Yes for create/update/delete/schedule/defer/work-order conversion | Partial | Yes | Yes, export placeholder | Yes, maintenance Node event types | Build/smoke pass | Full calendar drag scheduling and hard RBAC button gating remain | Add calendar planner and permission enforcement. |
| 10 | Work Orders | Yes | Yes, completed Batch 3 page | Yes, `workOrdersApi.ts` | Yes, CRUD, summary, timeline, recommendations, assign/status/labor/part/complete/cost approval | Yes, enriched MySQL SQL | `work_orders`, `work_order_labor`, `work_order_parts`, `work_order_status_events`, `maintenance_items`, `dvir_reports`, `documents`, `audit_logs` | Yes | Yes | Yes, enriched drawer | Yes | Yes | Yes for create/update/delete/assign/status/labor/part/complete/cost approval | Partial | Yes | Yes, export placeholder | Yes, work-order Node event types | Build/smoke pass | Photos/signatures and vendor SLA provider integration remain placeholders | Add real attachment upload and vendor SLA tracking. |
| 11 | Fuel & Idling | Yes | Yes, **completed Batch 5 page** | Yes, `fuelApi.ts` | Yes, full CRUD `/api/fuel/transactions`, `/api/fuel/idling-events`, vehicle/driver summaries, anomalies, recommendations | Yes, enriched MySQL SQL | `fuel_transactions`, `idling_events`, `fuel_anomalies`, `vehicles`, `drivers`, `jobs`, `audit_logs` | Yes | Yes | Yes, enriched drawer | Yes | Yes | Yes for create/update/delete/anomaly review | Partial | Yes | Yes, CSV export placeholder | Yes, fuel/idling/anomaly Node event types | Build pass | Fuel card import is placeholder; real card provider integration not connected | Add fuel card integration adapter and idling cost dashboard chart. |
| 12 | Safety | Yes | Yes, completed Batch 4 page | Yes, `safetyApi.ts` | Yes, summary, events CRUD, scorecards, trends, recommendations, review/coaching/incident actions | Yes, enriched MySQL SQL | `safety_events`, `driver_safety_scorecards`, `vehicle_safety_scorecards`, `safety_trends`, `coaching_tasks`, `incidents`, `audit_logs` | Yes | Yes | Yes, enriched drawer | Yes | Yes | Yes for create/update/delete/review/coaching/incident | Partial | Yes | Yes, export placeholder | Yes, safety Node event types | Build/smoke pass | Hard RBAC gates and automated browser console capture remain | Add permission enforcement and deeper route/zone risk analytics. |
| 13 | AI Dashcam / Incident Review | Yes | Yes, completed Batch 4 page | Yes, `dashcamApi.ts` | Yes, events CRUD, summary, recommendations, review, false positive, coaching, evidence package, incident report | Yes, enriched MySQL SQL | `dashcam_events`, `safety_events`, `coaching_tasks`, `evidence_packages`, `incidents`, `insurance_reports`, `audit_logs` | Yes | Yes | Yes, enriched drawer plus video cards | Yes | Yes | Yes for create/update/delete/review/false-positive/coaching/evidence/report | Partial | Yes | Yes, evidence/export placeholder | Yes, dashcam Node event types | Build/smoke pass | Real video playback/vendor integrations remain placeholders | Add camera provider adapter and signed clip storage. |
| 13A | Driver Coaching | Yes | Yes, completed Batch 4 page | Yes, `coachingApi.ts` | Yes, tasks CRUD, summary, recommendations, assign/acknowledge/complete/add-note | Yes, enriched MySQL SQL | `coaching_tasks`, `coaching_notes`, `drivers`, `safety_events`, `dashcam_events`, `audit_logs` | Yes | Yes | Yes, enriched drawer | Yes | Yes | Yes for create/update/delete/assign/ack/complete/note | Partial | Yes | Yes, export placeholder | Yes, coaching Node event types | Build/smoke pass | Driver self-service role scoping is metadata-only | Add role-scoped driver portal view and training content library. |
| 13B | Incidents | Yes | Yes, completed Batch 4 page | Yes, `incidentsApi.ts` | Yes, incidents CRUD, summary, timeline, recommendations, status/evidence/insurance report actions | Yes, enriched MySQL SQL | `incidents`, `incident_evidence`, `evidence_packages`, `insurance_reports`, `safety_events`, `dashcam_events`, `audit_logs` | Yes | Yes | Yes, enriched drawer | Yes | Yes | Yes for create/update/delete/status/evidence/report | Partial | Yes | Yes, export placeholder | Yes, incident Node event types | Build/smoke pass | Legal review workflow is placeholder-level | Add legal review states and approval workflow. |
| 13C | Evidence Packages / Insurance Reports | Yes | Yes, completed Batch 4 page | Yes, `evidenceApi.ts` | Yes, package CRUD, summary, lock, export placeholder; insurance reports via incident/dashcam actions | Yes, enriched MySQL SQL | `evidence_packages`, `evidence_package_items`, `insurance_reports`, `incident_evidence`, `documents`, `audit_logs` | Yes | Yes | Yes, enriched drawer | Yes | Yes | Yes for create/update/delete/export/lock/report creation | Partial | Yes | Yes, export placeholder | Yes, evidence/insurance Node event types | Build/smoke pass | Actual generated PDF/file export is placeholder metadata | Add file generation/storage adapter and immutable chain-of-custody export. |
| 14 | Compliance | Yes | Yes, shared module page | Yes, `modulesApi.ts` dedicated map | Yes, `/api/compliance` | Yes, dedicated module SQL | `compliance_documents` | Yes | Partial | Yes | Partial | Yes, embedded summary | Yes for create/update | Partial | Yes | Partial | No | Build pass | General compliance exists, but not country-specific rule packs | Add compliance rule metadata, expiry workflow, and country profile links. |
| 15 | HOS / ELD Framework | Yes | Yes, shared module page | Yes, `modulesApi.ts` dedicated map | Yes, `/api/hos-eld` | Yes, dedicated module SQL | `hos_logs`, `drivers` | Yes | Partial | Yes | Partial | Yes, embedded summary | Yes for create/update | Partial | Yes | Partial | No | Build pass | Needs rule validation, violation detection, and ELD import placeholder | Add HOS violation engine and driver duty timeline. |
| 16 | DVIR / Inspections | Yes | Yes, completed Batch 3 page | Yes, `dvirApi.ts` | Yes, reports CRUD, summary, templates, mechanic review, repair certification, driver sign, timeline, recommendations | Yes, enriched MySQL SQL | `dvir_reports`, `dvir_templates`, `dvir_defects`, `inspection_checklist_items`, `work_orders`, `drivers`, `vehicles`, `audit_logs` | Yes | Yes | Yes, enriched drawer | Yes | Yes | Yes for create/update/delete/review/certify/sign | Partial | Yes | Yes, export placeholder | Yes, DVIR Node event types | Build/smoke pass | Per-checklist-item pass/fail capture and real photo upload remain placeholders | Add checklist answer table and defect-to-work-order wizard. |
| 17 | Customer ETA Portal | Yes | Yes, completed Batch 2 internal and public pages | Yes, `customerEtaApi.ts` | Yes, summary, track, job detail, send update, feedback, communications, recommendations | Yes, enriched MySQL SQL | `eta_updates`, `customer_eta_links`, `jobs`, `customers`, `customer_communications`, `proof_of_delivery`, `customer_feedback`, `audit_logs` | Yes | Yes, update/feedback actions | Yes, internal job tracking plus public ETA page | Yes | Yes | Yes for ETA update and feedback | Partial, public tracking intentionally unauthenticated | Yes | Yes, report placeholders | Yes, ETA/customer feedback Node event types | Build/smoke pass | Public page is safe tracking only and does not yet support branded per-customer theming | Add customer-specific theming, language packs, and notification delivery provider adapters. |
| 18 | Clients / Customers | Yes | Yes, completed Batch 1 entity page | Yes, `customersApi.ts` | Yes, CRUD, summary, timeline, recommendations | Yes, enriched MySQL SQL | `customers`, `customer_contacts`, `customer_addresses`, related jobs/contracts/communications/ETA/audit tables | Yes | Yes | Yes, enriched drawer | Yes | Yes | Yes for create/update/delete | Partial | Yes | Yes, CSV placeholder | Partial | Build/smoke pass | Customer contact sub-record editing and hard RBAC button gating remain | Add inline contact/address editing and permission enforcement. |
| 19 | Contracts / Rates | Yes | Yes, **completed Batch 5 page** | Yes, `contractsApi.ts` | Yes, full CRUD `/api/contracts`, rates sub-endpoints, activate/expire actions, recommendations | Yes, enriched MySQL SQL | `contracts`, `contract_rates`, `customers`, `carriers`, `audit_logs` | Yes | Yes | Yes, enriched drawer with rates table | Yes | Yes | Yes for create/update/delete/activate/expire | Partial | Yes | Yes, CSV export placeholder | Yes, contract expiry Node event types | Build pass | Rate editing via UI sub-form and real contract PDF export remain placeholders | Add inline rate editor and contract document generator. |
| 20 | Carrier Management | Yes | Yes, **completed Batch 5 page** | Yes, `carriersApi.ts` | Yes, full CRUD `/api/carriers`, performance, documents, setStatus, recommendations | Yes, enriched MySQL SQL | `carriers`, `carrier_documents`, `carrier_performance`, `contracts`, `expenses`, `audit_logs` | Yes | Yes | Yes, enriched drawer with performance/docs/contracts/expenses | Yes | Yes | Yes for create/update/delete/setStatus | Partial | Yes | Yes, CSV export placeholder | Yes, carrier compliance risk Node event types | Build pass | Carrier onboarding form and dispatch eligibility scoring require deepening | Add carrier onboarding wizard and capacity/availability endpoint. |
| 21 | Expenses | Yes | Yes, **completed Batch 5 page** | Yes, `expensesApi.ts` | Yes, full CRUD `/api/expenses`, approve/reject actions, categories, recommendations | Yes, enriched MySQL SQL | `expenses`, `expense_categories`, `vehicles`, `drivers`, `jobs`, `customers`, `audit_logs` | Yes | Yes | Yes, enriched drawer | Yes | Yes | Yes for create/update/delete/approve/reject | Partial | Yes | Yes, CSV export placeholder | Yes, expense approved/rejected Node event types | Build pass | Receipt file upload is placeholder; actual object storage not connected | Add upload endpoint and real receipt storage adapter. |
| 22 | Documents | Yes | Yes, completed Batch 3 page | Yes, `documentsApi.ts` | Yes, CRUD, summary, expiring, upload placeholder, renew placeholder, timeline, recommendations | Yes, enriched MySQL SQL | `documents`, `compliance_documents`, `document_timeline_events`, linked vehicle/driver/asset/customer/work-order/inspection records, `audit_logs` | Yes | Yes | Yes, enriched drawer | Yes | Yes | Yes for create/update/delete/upload placeholder/renewal | Partial | Yes | Yes, export placeholder | Yes, document Node event types | Build/smoke pass | Real file storage and country-specific compliance packages remain placeholders | Add object storage adapter and audit package generator. |
| 23 | Reports & Analytics | Yes | Yes, **completed Batch 7 page** | Yes, `batch7Api.ts` `reportsApi` | Yes, `/api/reports/catalog`, `/api/reports/summary`, `/api/reports/{key}/run`, `/api/reports/scheduled`, `/api/reports/exports`, `/api/reports/ai/recommendations` | Yes, `report_catalog`, `report_runs`, `scheduled_reports`, `report_exports` | `report_catalog`, `report_runs`, `scheduled_reports`, `report_exports`, `ai_recommendations` | Yes, 30 catalog entries, 20 runs, 8 scheduled, 10 exports | Partial, schedule create modal | Partial, run history table | Yes, category filter | Yes, summary cards | Yes for run/schedule/export | Partial | Yes | Yes, placeholder export formats | Yes, Node SSE event types | Build pass | File generation is placeholder; real export engine not connected | Add background export worker and file delivery. |
| 24 | SLA / KPI Center | Yes | Yes, **completed Batch 7 page** | Yes, `batch7Api.ts` `kpiApi` + `slaApi` | Yes, `/api/kpi/metrics`, `/api/kpi/summary`, `/api/sla/records`, `/api/sla/summary`, `/api/sla/breaches`, breach ack/resolve | Yes, `kpi_metrics`, `kpi_targets`, `sla_records`, `sla_breaches` | `kpi_metrics`, `kpi_targets`, `sla_records`, `sla_breaches`, `customers`, `jobs` | Yes, 30 KPI metrics, 20 targets, 30 SLA records, 10 breaches | No (analytics view) | Yes, KPI card with progress bar | Yes, category and SLA type filters | Yes, summary strip + per-card | Yes for breach acknowledge/resolve | Partial | Yes, AI advisor tab | Partial | Yes, Node SSE event types | Build pass | KPI metrics are seeded simulation; SLA breach engine does not auto-detect from live jobs | Add SLA monitoring job and KPI data pipeline. |
| 25 | Predictive Cost & Margin | Yes | Yes, **completed Batch 5 page** | Yes, `costMarginApi.ts` | Yes, summary, `/api/cost-margin/jobs`, routes, vehicles, customers, predictions, recommendations, recalculate | Yes, enriched MySQL SQL | `cost_margin_records`, `cost_margin_predictions`, `jobs`, `routes`, `vehicles`, `customers`, `audit_logs` | Yes | No (analytics-only read view) | Yes, enriched drawer from row data | Yes | Yes | Yes for recalculate action; analytics view only | Partial | Yes | Yes, CSV export placeholder | Yes, margin risk Node event types | Build pass | Prediction model is seeded simulation; real ML model integration not connected | Add ML prediction service adapter and job/route margin drilldown with cost breakdown charts. |
| 25A | ROI / Cost Leakage Intelligence | Yes | Yes, **completed Batch 5 page** | Yes, `costLeakageApi.ts` | Yes, summary, `/api/cost-leakage/items`, acknowledge/create-action, recommendations | Yes, enriched MySQL SQL | `cost_leakage_items`, `cost_leakage_actions`, `audit_logs` | Yes | No (action-queue read view; createAction adds action records) | Yes, enriched drawer from row data | Yes | Yes | Yes for acknowledge and createAction | Partial | Yes | Yes, CSV export placeholder | Yes, cost leakage Node event types | Build pass | Leakage detection rules are seeded simulation; real anomaly engine not connected | Add rule-engine adapter and automated leakage scan scheduling. |
| 26 | Audit Logs | Yes | Yes, **completed Batch 7 page** | Yes, `batch7Api.ts` `auditApi` | Yes, `/api/audit/logs` (search/module/severity/actor filters), `/api/audit/logs/{id}`, `/api/audit/export-requests`, `/api/audit/ai/recommendations` | Yes, `audit_logs` (+ severity, module_key, action_type columns), `audit_export_requests` | `audit_logs`, `audit_export_requests`, `ai_recommendations` | Yes, 100 seed records (INSERT IGNORE), 8 export requests | N/A, read-only trail | Yes, detail drawer | Yes, search + module + severity filters | Yes, count per filter | N/A, read-only | Partial | Yes, AI advisor tab | Yes, export request (date range + format) | Yes, Node SSE event types | Build pass | Export is a request record; no background worker generates the file | Add audit export background worker and tamper-evident hash chain. |
| 26A | Executive Dashboard | Yes | Yes, **completed Batch 7 page** (`/executive`) | Yes, `batch7Api.ts` `executiveApi` | Yes, `/api/executive/snapshots`, `/api/executive/summary`, `/api/executive/ai/recommendations` | Yes, `executive_snapshots`, `kpi_metrics`, `sla_breaches`, `audit_logs`, `ai_recommendations` | `executive_snapshots`, `kpi_metrics`, `sla_breaches`, `ai_recommendations` | Yes, 10 snapshots (10-day history with AI briefs), 15 AI recommendations | N/A, read-only dashboard | N/A | N/A | Yes, score rings + alert strip | N/A, read-only | Partial | Yes, AI executive brief + advisor | N/A | Yes, Node SSE event types | Build pass | Snapshots are seeded simulation; no automated daily snapshot generation job | Add cron-triggered snapshot generator and real metric aggregation pipeline. |
| 27 | OpsTrax AI Copilot | Yes | Yes, custom AI workspace | Yes, `aiApi.ts` | Yes, `/api/ai/ask`, `/api/ai/insights` | Yes, shared DB SQL | `ai_insights`, `ai_recommendations`, operational source tables | Yes | N/A | Partial, evidence/action cards | Partial, prompt categories | Yes through insights | Partial, recommended actions not executed | Partial | Yes | Partial | Partial through actions only | Build pass | Needs persisted conversations, action execution, and evidence deep links | Add chat history table and action dispatch endpoints. |
| 28 | Integrations | Yes | Yes, shared module page | Yes, `modulesApi.ts` dedicated map | Yes, `/api/integrations` | Yes, dedicated module SQL | `integrations` | Yes | Partial | Yes | Partial | Yes, embedded summary | Yes for create/update | Partial | Yes | Partial | No | Build pass | Needs connector setup flows, secrets strategy, and sync logs | Add integration detail, status checks, and mock connector lifecycle. |
| 29 | User Management | Yes | Yes, shared module page | Yes, `modulesApi.ts` dedicated map | Yes, `/api/user-management` plus auth login | Yes, dedicated module SQL | `users`, `roles`, `companies` | Yes | Partial | Yes | Partial | Yes, embedded summary | Yes for create/update | Partial, strongest metadata source | Partial | Partial | No | Build pass | Needs actual role editor, permission guard, invite/reset flows | Complete RBAC enforcement and user administration first. |
| 30 | Settings | Yes | Yes, shared module page | Yes, `modulesApi.ts` dedicated map | Yes, `/api/settings` | Partial, uses `module_records` | `module_records`; no dedicated settings table | Partial | Partial | Yes | Partial | Yes, embedded summary | Yes for create/update, but settings-specific audit is limited | Partial | Partial | Partial | No | Build pass | Needs settings schema, tenant preferences, and guarded write flow | Add company settings table and audited settings update endpoints. |
| 31 | Billing / Subscription | Yes | Yes, shared module page | Yes, `modulesApi.ts` dedicated map | Yes, `/api/billing` | Yes, dedicated module SQL | `subscription_plans` | Yes | Partial | Yes | Partial | Yes, embedded summary | Yes for create/update | Partial | Partial | Partial | No | Build pass | Needs invoices, subscription state, usage metering, and payment provider placeholder | Add subscription account state and invoice/usage schema. |
| 32 | Country Compliance / HOS / ELD | Yes | Yes, **completed Batch 6 pages** (`/compliance`, `/hos-eld`) | Yes, `complianceApi.ts`, `hosApi`, `eldApi` | Yes, `/api/compliance/*`, `/api/hos/*`, `/api/eld/*` | Yes, dedicated MySQL SQL | `compliance_profiles`, `compliance_rules`, `compliance_violations`, `compliance_audit_packages`, `driver_compliance_status`, `vehicle_compliance_status`, `hos_logs`, `hos_clocks`, `eld_devices`, `countries` | Yes, 5 countries, 6 profiles, 10 rules, 15 violations, 5 audit packages, 10 HOS clocks, 30 HOS logs, 10 ELD devices | Partial, audit package create modal | Yes, violation + driver + ELD drawers | Yes, status/severity filters | Yes, summary cards | Yes for violation ack/resolve/certify/malfunction | Partial | Yes, AI advisor tabs | Yes, export placeholder | Yes, Batch 6 Node SSE event types | Build pass | ELD is not FMCSA-certified; compliance rules are seeded simulation; HOS clocks not updated by real telemetry | Add real ELD telemetry bridge and automated violation detection rule engine. |
| 33 | Multi-Language / RTL | Yes (settings page) | Yes, **completed Batch 6** (`/settings`) | Yes, `localizationApi` | Yes, `/api/localization/*` | Yes, `tenant_locale_settings`, `user_locale_preferences`, `languages`, `countries` | `tenant_locale_settings`, `user_locale_preferences`, `languages`, `countries` | Yes, 6 languages, 5 countries | Yes, language selector grid | Yes, settings form | N/A | N/A | Yes for settings save | Partial | N/A | N/A | Yes, `localization.preference_changed` Node SSE | Build pass | RTL layout tested visually; no automated RTL regression tests; no per-string translation override UI | Add RTL visual regression testing and user-level translation overrides. |
| 34 | API / DevOps / Docker | N/A | N/A | Partial, app uses API clients | Yes, Swagger/API host | Yes, DB connector and middleware | N/A | N/A | N/A | N/A | N/A | Health through services; no API health endpoint observed | N/A | Partial through auth metadata | N/A | N/A | N/A | Build/config pass | Missing formal API test suite, health/readiness endpoint, and CI pipeline docs | Add API health endpoint, contract smoke tests, and CI validation script. |
| 35 | Node Events / Telemetry | Partial, consumed by Control Tower | Partial, live feed appears in Control Tower | Partial, `useEventStream` hook | Yes, `/health`, `/events/stream`, telemetry and AI brief endpoints | Partial, simulation service does not persist to MySQL | No dedicated Node persistence tables; API has event tables | Seed via API DB only | N/A | N/A | N/A | `/health` only | Partial, telemetry post does not write audit logs | N/A | Partial, AI brief stub | N/A | Yes, SSE stream | Build/config pass | Events are simulated and not persisted; no replay, auth, or per-tenant channels | Add event persistence bridge to API, tenant channels, replay cursor, and typed event contracts. |

## Database Table Coverage

Tables present in `database/init/001_schema.sql`, `database/init/002_seed.sql`, and runtime-safe schema/seed upgrade services include:

`companies`, `roles`, `users`, `drivers`, `vehicles`, `customers`, `contracts`, `assets`, `jobs`, `routes`, `route_stops`, `dispatch_assignments`, `trips`, `location_events`, `geofences`, `geofence_events`, `maintenance_items`, `work_orders`, `fuel_transactions`, `safety_events`, `dashcam_events`, `compliance_documents`, `inspections`, `hos_logs`, `expenses`, `carriers`, `documents`, `sla_records`, `kpi_records`, `ai_insights`, `ai_recommendations`, `notifications`, `audit_logs`, `integrations`, `subscription_plans`, `command_center_actions`, `operational_events`, `customer_communications`, `proof_of_delivery`, `dispatch_recommendations`, `eta_updates`, and `module_records`.

Batch 2 additionally upgrades or creates: `job_status_events`, `route_paths`, `route_recommendations`, `customer_eta_links`, and `customer_feedback`, and enriches `jobs`, `routes`, `route_stops`, `dispatch_assignments`, `customer_communications`, `eta_updates`, and `proof_of_delivery` with operational fields, indexes, and realistic Northern Virginia/DC seed data.

Batch 3 additionally upgrades or creates: `maintenance_schedules`, `work_order_labor`, `work_order_parts`, `work_order_status_events`, `dvir_reports`, `dvir_templates`, `dvir_defects`, `inspection_checklist_items`, and `document_timeline_events`, and enriches `maintenance_items`, `work_orders`, `documents`, `notifications`, `audit_logs`, and `ai_recommendations` with realistic maintenance/compliance seed data.

Batch 4 additionally upgrades or creates: `coaching_tasks`, `coaching_notes`, `incidents`, `incident_evidence`, `evidence_packages`, `evidence_package_items`, `insurance_reports`, `driver_safety_scorecards`, `vehicle_safety_scorecards`, and `safety_trends`, and enriches `safety_events`, `dashcam_events`, `notifications`, `audit_logs`, and `ai_recommendations` with realistic safety/video/incident seed data.

Batch 5 additionally upgrades or creates: `idling_events`, `fuel_anomalies`, `expense_categories`, `contract_rates`, `carrier_documents`, `carrier_performance`, `cost_margin_records`, `cost_margin_predictions`, `cost_leakage_items`, and `cost_leakage_actions`, and adds columns to `fuel_transactions` (transaction_number, driver_id, job_id, fuel_date, fuel_type, quantity, unit, unit_price, currency, odometer, payment_method, fuel_card_number, region, anomaly_status), `expenses` (expense_number, category_name, currency, vehicle_id, driver_id, job_id, customer_id, carrier_id, vendor_name, approval_status, receipt_status, risk_score, recommended_action), `carriers` (carrier_number, contact_name, phone, email, region, compliance_status, insurance_expiry, contract_status, on_time_percent, safety_score, cost_score, performance_score, risk_score, recommended_action), and `contracts` (contract_number, carrier_id, contract_type, currency, base_rate, fuel_surcharge_enabled, fuel_surcharge_percent, sla_terms, margin_risk). Enriches `notifications`, `audit_logs`, and `ai_recommendations` with realistic finance/cost intelligence seed data.

Batch 6 additionally upgrades or creates: `countries`, `languages`, `tenant_locale_settings`, `user_locale_preferences`, `compliance_profiles`, `compliance_rules`, `driver_compliance_status`, `vehicle_compliance_status`, `hos_logs`, `hos_clocks`, `eld_devices`, `compliance_violations`, `compliance_audit_packages`, and adds columns to `documents` (country_code, issuing_authority, issued_at) and `dvir_reports` (country_code, compliance_profile_id).

Batch 7 additionally upgrades or creates: `report_catalog`, `report_runs`, `scheduled_reports`, `report_exports`, `kpi_metrics`, `kpi_targets`, `sla_breaches`, `executive_snapshots`, `audit_export_requests`, and adds columns to `audit_logs` (severity, module_key, action_type) and `ai_recommendations` (description, priority, action_label, action_type).

Missing or only partially modeled tables:

- Company settings/preferences as a first-class table (currently uses `module_records` fallback).
- Billing account, invoices, usage metering, and payment status beyond `subscription_plans`.
- Predictive cost/margin scenario tables.
- AI chat history and executed AI action records.
- Node telemetry persistence/replay tables, unless routed through API event tables.
- Automated SLA monitoring and KPI pipeline tables (metrics currently seeded, not live-computed).
- Report file storage and delivery tables (exports are request records, not file references).
