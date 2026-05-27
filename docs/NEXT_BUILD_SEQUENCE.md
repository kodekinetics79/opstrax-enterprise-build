# OpsTrax Next Build Sequence

Audit date: 2026-05-24

Goal: complete OpsTrax module depth without disrupting the current working full-stack system.

Guiding priority:

1. Core platform and master data first.
2. Operations workflow second.
3. Maintenance, safety, and compliance third.
4. Customer, finance, and reporting fourth.
5. AI, integrations, billing, settings, and advanced platform capabilities last.

## Recommended Build Order

### Phase 0 - Stabilize Platform Contracts

Modules:

- API / DevOps / Docker
- User Management
- Settings
- Audit Logs

Why first:

- Every later workflow depends on authenticated users, permissions, company context, settings, audit history, and stable API contracts.
- Current validation passes, but there is no formal test suite or contract smoke script.

Work items:

- Add API health/readiness endpoint.
- Add contract smoke tests for auth, core summaries, list endpoints, and MySQL connectivity.
- Add route/action permission guard in the frontend.
- Add backend permission metadata checks for protected write actions.
- Replace placeholder settings with a first-class company settings table.
- Add advanced audit log filters and export placeholder.

Exit criteria:

- Authenticated role can be read consistently in frontend and backend.
- At least one guarded action proves RBAC end to end.
- Audit logs can be filtered by actor, module, entity, action, and date.
- `docker compose config`, frontend build, backend build, and API smoke checks pass.

### Phase 1 - Master Data Foundation

Modules:

- Vehicles
- Drivers
- Assets / Trailers / Equipment
- Clients / Customers
- Contracts / Rates
- Carrier Management
- Documents

Why next:

- Dispatch, jobs, route planning, maintenance, compliance, finance, and reporting all need clean master records.
- These modules already have tables, seed data, backend endpoints, and shared pages, so the main missing layer is workflow depth.

Work items:

- Build reusable create/edit modal framework with schema-driven fields.
- Complete vehicle create/edit/detail tabs.
- Complete driver create/edit/detail tabs.
- Add asset assignment to vehicles/jobs.
- Add customer profile tabs for jobs, contracts, contacts, SLA, and documents.
- Add contract/rate editor with effective dates and customer linkage.
- Add carrier compliance and eligibility profile.
- Add document metadata form and entity attachment links.

Exit criteria:

- Master data can be created, updated, viewed in detail, searched, and audited from the UI.
- Shared form framework is reusable by later modules.
- Entity detail drawers show related records from the database.

### Phase 2 - Core Operations Workflow

Modules:

- Jobs & Orders
- Dispatch Board
- Route Planning
- Live Map / Control Tower
- Customer ETA Portal
- Node Events / Telemetry

Why next:

- These modules represent the daily transport operating loop: order intake, assignment, route execution, ETA updates, live exceptions, and customer communication.
- Control Tower and Node SSE already work, but event persistence and operational action loops are still partial.

Work items:

- Complete job create/edit form, pickup/drop-off fields, assignment, SLA, and POD placeholders.
- Add dispatch drag/drop or explicit status movement with conflict validation.
- Add route stop editor and route-to-job handoff.
- Complete Control Tower selected entity drawer and map filters.
- Persist telemetry/event payloads through the API or a durable event bridge.
- Add customer-scoped ETA portal guard and live ETA/event feed.
- Wire ETA sent events to audit logs, customer communications, and notifications.

Exit criteria:

- A seeded or newly created job can move from unassigned to assigned to en route to completed.
- ETA update creates audit, event, and customer communication records.
- Live event feed can refresh relevant UI state without page reload.

### Phase 3 - Maintenance, Safety, and Compliance Depth

Modules:

- Maintenance
- Work Orders
- Fuel & Idling
- Safety
- AI Dashcam / Incident Review
- Compliance
- HOS / ELD Framework
- DVIR / Inspections
- Country Compliance

Why here:

- These modules consume fleet, driver, route, and event data.
- They become more valuable after the operational workflow is producing assignments, events, inspections, and costs.

Work items:

- Add preventive maintenance scheduling and due-date rules.
- Convert maintenance items and DVIR defects into work orders.
- Build work order status workflow with mechanic assignment.
- Add fuel anomaly and idling analytics.
- Add safety review queue and coaching workflow.
- Add dashcam incident evidence/status workflow.
- Add HOS violation detection and driver duty timeline.
- Add country compliance module with jurisdiction profiles, rule versions, and document requirements.

Exit criteria:

- A DVIR defect or maintenance warning can create a work order.
- Safety/dashcam incidents can be reviewed and assigned for coaching.
- Compliance and HOS risks appear in command center and module summaries.

### Phase 4 - Customer, Finance, and Reporting

Modules:

- Expenses
- Reports & Analytics
- SLA / KPI Center
- Predictive Cost & Margin
- Billing / Subscription

Why after operations:

- These modules need trustworthy operational, customer, contract, route, fuel, maintenance, and expense data.
- Predictive margin should not be deepened until cost and contract inputs are more structured.

Work items:

- Add expense approval workflow, receipt placeholder, and cost allocation to job/vehicle/customer.
- Build SLA breach review and KPI target configuration.
- Add report definitions, saved views, and export queue placeholder.
- Add predictive margin scenario schema tied to jobs, contracts, fuel, maintenance, and expenses.
- Expand billing beyond plans into subscription account, usage, invoice, and status records.

Exit criteria:

- Finance records can be tied to operating entities.
- KPI/SLA dashboards can drill down to source records.
- Report/export placeholders have durable request records.
- Margin view can explain source evidence, not just display generic records.

### Phase 5 - Intelligence, Integrations, and Globalization

Modules:

- OpsTrax AI Copilot
- Integrations
- Multi-Language / RTL

Why last:

- AI action execution and integrations should operate on stable domain workflows.
- Localization is safer after route/page structure stabilizes.

Work items:

- Add persisted AI chat history and evidence deep links.
- Add AI action execution endpoints that create auditable domain actions.
- Add integration connector lifecycle: configured, connected, degraded, failed, paused.
- Add sync logs and connector health status.
- Add i18n provider, locale bundles, user/company locale preferences, and RTL visual QA.

Exit criteria:

- AI recommendations can become tracked, auditable actions.
- Integration status has durable records and operator-facing health.
- Multi-language and RTL support can be tested across the app shell and key modules.

## Immediate Next Module

The next best build target is User Management plus RBAC enforcement, paired with the shared create/edit form framework.

Reason:

- It unlocks safe completion of every write workflow.
- It turns existing role/permission metadata into actual product behavior.
- It gives Vehicles, Drivers, Jobs, and all shared module pages a reusable pattern for guarded actions, audit creation, and production-grade forms.

## Dependency Notes

- Vehicles and Drivers should be completed before Dispatch and Jobs assignment workflows are expanded.
- Customers and Contracts should be completed before Customer ETA Portal, SLA/KPI, and Predictive Margin are expanded.
- Documents should be completed before Compliance, Carrier Management, Vehicles, Drivers, and Work Orders get deeper attachment workflows.
- Node event persistence should be completed before adding advanced live refresh across all modules.
- Settings should be completed before Multi-Language/RTL, billing rules, notification preferences, and company-level compliance behavior.

## Validation Gate For Each Phase

Before moving to the next phase:

- `docker compose config`
- `cd frontend && npm run build`
- `cd backend-dotnet && dotnet build`
- API smoke checks for changed endpoints.
- At least one happy-path UI flow verified in browser.
- Confirm MySQL remains internal-only with no `ports` mapping.
- Confirm all visible branding remains OpsTrax.

