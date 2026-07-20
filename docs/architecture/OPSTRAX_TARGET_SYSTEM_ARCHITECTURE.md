# OpsTrax Target System Architecture

## Goal
Define a clean enterprise target state that is safe to implement locally, auditable, and PostgreSQL-first.

## Core Layers
1. Presentation shell
2. API gateway / application backend
3. Domain services by bounded context
4. Eventing and integration layer
5. PostgreSQL operational store
6. Reporting and materialized summaries
7. AI/insight layer

## Module Map
- Platform Admin
- Tenant & Identity
- CRM / Sales
- Contracts / Pricing
- Revenue / Finance
- Fleet Assets
- Drivers / Workforce
- Dispatch / Trips
- Maintenance / Fleet Health
- Safety / Incidents
- Compliance / Audit
- IoT / Telematics
- Alerts / Notifications
- AI / Knowledge
- Reporting / Analytics
- Integrations / Event Bus

## Delivery Rules
- Keep the platform/tenant split strict.
- Avoid a big-ball-of-mud rewrite.
- Prefer additive changes with explicit rollback notes.
- Do not let AI bypass business services.

## Current Gap
The repo already has many tables and screens, but the architecture is still partially legacy and partially governed. The target state must normalize this into explicit contexts and canonical data flows.

