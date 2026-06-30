# OpsTrax Enterprise Architecture Pack

## Executive Vision
OpsTrax is an AI-powered Fleet Business Operating System. It must connect operations, dispatch, telematics, safety, maintenance, compliance, CRM, contracts, revenue, finance, and platform administration in one governed command center.

## Product Positioning
The product wins when it reduces operational friction and revenue leakage, not when it merely visualizes fleet data. It should predict risk, recommend actions, and safely automate low-risk workflows with full auditability.

## Why OpsTrax Wins
- One tenant-aware platform spanning the full customer-to-cash and vehicle-to-compliance lifecycle.
- PostgreSQL-first data design with audit-ready finance and event history.
- AI as a governed intelligence layer, not a chatbot.
- IoT and telematics integrated into operational and commercial workflows.

## Business Capability Map
- Platform administration
- Tenant and identity
- CRM and sales
- Contracts and pricing
- Revenue, billing, margin, and renewals
- Fleet master data
- Dispatch and trip execution
- Maintenance and fleet health
- Safety and incidents
- Compliance and audit readiness
- IoT and telematics
- AI autonomy and recommendations
- Reporting and observability

## System Architecture Overview
- Frontend: React/Vite enterprise shell.
- Tenant API: .NET 8 service with PostgreSQL-backed schemas and route mappings.
- Node service: lightweight integration/event service.
- Database: PostgreSQL as the operational system of record.

## Bounded Contexts
- Platform Administration
- Tenant & Identity
- Customer & CRM
- Contracts & Pricing
- Revenue & Finance
- Fleet Asset
- Driver Workforce
- IoT Device & Telematics
- Dispatch & Trip Operations
- Maintenance & Fleet Health
- Safety & Incident
- Compliance & Audit Readiness
- Alerts & Notifications
- AI Autonomy
- Knowledge & Document
- Integration & Event Bus
- Reporting & Analytics

## Data Architecture
- UUIDs for new tenant-owned entities where practical.
- timestamptz for all temporal data.
- numeric/decimal for money, quantities, mileage, and fuel.
- JSONB only for flexible metadata and event payloads.
- tenant_id on every tenant-owned table.
- partition-ready telemetry/event tables.

## PostgreSQL Architecture
- Treat PostgreSQL as the only P0 target.
- Keep schema bootstrap idempotent for local P0 verification.
- Introduce formal migrations in the next slice.
- Use indexes on tenant-scoped lookup paths.

## Event-Driven Architecture
- Events should represent domain facts, not UI actions.
- Use outbox/inbox patterns when moving beyond local bootstrap.
- Preserve audit records for every automated or user-driven transition.

## AI / LLM Autonomy Architecture
- L1/L2 for launch: insight and recommendations.
- L3 later: approval-based actions.
- No direct AI writes to business tables.
- Sensitive operations require human approval and audit trails.

## IoT Automation Architecture
- Ingest GPS, ELD, OBD/CAN, dashcam, fuel, door, temperature, and lock events.
- Normalize telemetry before producing alerts or recommendations.
- Command actions must be approval-gated and fully logged.

## Security Architecture
- Separate platform admin from tenant admin.
- Enforce tenant isolation, RBAC, CSRF, and bearer-token session handling.
- Protect finance, customer portal, and IoT command paths with stronger controls.

## Multi-Tenant SaaS Architecture
- Tenant context must flow through auth, APIs, and data access.
- Every tenant-owned query path must include tenant scoping.
- Platform staff should not become tenant users.

## CRM / Revenue / Finance Architecture
- Lead -> Opportunity -> Quote -> Customer -> Contract -> Rate Card -> SLA -> Job -> Trip -> POD -> Job Charge -> Invoice -> Payment -> Margin -> Renewal
- Capture revenue leakage and margin leakage as first-class workflows.

## Integration Architecture
- Use adapter-based integrations for GPS/ELD/dashcam/accounting/ERP/maps/payment systems.
- Store credentials separately from business data.
- Validate signatures and replay windows for inbound webhooks.

## Observability Architecture
- Correlate logs, metrics, and events by tenant and request.
- Measure API latency, telemetry ingestion, AI latency, and error budgets.
- Preserve audit trails for safety and finance operations.

## Testing Architecture
- Unit, integration, API contract, tenant isolation, RBAC, finance, dispatch, telematics, AI safety, and E2E tests.
- Local build/test gates are mandatory before Stage 2 expansion.

## Phased Delivery Plan
- P0-A: local readiness, build verification, gap report, safe fixes only.
- P0-B: PostgreSQL provider and tenant/RBAC foundation.
- P1: customer master, contracts, revenue spine.
- P2: AI autonomy, IoT automation, and advanced analytics.

## Risk Register
- Legacy MySQL-era documentation causing drift.
- No formal migration framework yet.
- Some modules still need tenant isolation review.
- AI and IoT need tighter approval and audit controls.

## Acceptance Criteria
- Build and tests pass locally.
- PostgreSQL target is explicit and documented.
- No source changes beyond safe local fixes in P0-A.
- Stage 2 prompt and architecture docs are clear enough to build from.

## Stage 2 Build Recommendation
Approve Stage 2 only in controlled slices, starting with P0-A verification and P0-B PostgreSQL/tenant foundation.

