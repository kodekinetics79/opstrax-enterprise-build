# Stage 9 Hand-off Note

Stage 9 is complete locally.

The next slice should begin from the durable POD / site-access / smart-assign / proof foundation already in the repo and move into the first fully staffed business execution slice.

## What Stage 9 established

- Mobile-safe operational endpoints for smart assignment, site access, access documents, pickup authorization, warehouse handover, proof packages, proof artifacts, and billing confidence.
- Tenant-scoped, fail-closed permission gates for the new operational permissions.
- PostgreSQL-backed persistence for the new operational entities.
- Approval-required behavior for high-risk smart assignment acceptance and access-document waiver flows.
- AI recommendation creation for missing evidence and unresolved access conditions.
- Local test coverage for the critical submit/approval/idempotency edges.

## Next prompt should build

1. Customer master
2. Customer contacts and addresses foundation
3. Contract foundation
4. Rate card foundation
5. Job foundation
6. Trip foundation
7. Minimal charge and revenue foundation
8. APIs for the above
9. Tests
10. Local migration only if required
11. Completion report

## Required dependencies

- Centralized authorization service
- DB-backed approval workflow
- DB-backed domain events and outbox
- DB-backed idempotency
- DB-backed AI recommendation / action request foundation
- DB-backed audit and correlation
- Outbox dispatcher
- PostgreSQL migration discipline

## Do not

- Build full CRM
- Build full invoice / payment / AR aging engine
- Build full AI automation
- Build full IoT ingestion
- Redesign frontend
- Push, deploy, or touch production

