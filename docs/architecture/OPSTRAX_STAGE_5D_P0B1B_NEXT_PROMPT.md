# Stage 5D P0-B1B Next Prompt

Use this prompt for the next slice only. Do not execute it yet.

## Objective

Build the first business spine slice on top of the DB-backed foundation:

1. Customer master
2. Customer contacts and addresses, minimal only
3. Contract foundation
4. Rate card foundation
5. Job foundation
6. Trip foundation
7. Minimal charge/revenue foundation
8. APIs for the above
9. Tests
10. Local migration
11. Completion report

## Required Dependencies

- Centralized authorization service
- DB-backed approval workflow
- DB-backed domain events and outbox
- DB-backed idempotency
- DB-backed AI recommendation and action-request foundation
- DB-backed audit and correlation
- Outbox dispatcher
- PostgreSQL migration discipline

## Hard Limits

- Do not build full CRM.
- Do not build invoice/payment/AR aging yet.
- Do not redesign the frontend.
- Do not build full AI automation.
- Do not build full IoT ingestion.
- Do not push, deploy, or touch production.

## Prompt Guidance

- Keep the slice minimal but durable.
- Keep tenant isolation explicit everywhere.
- Use the dispatcher and approval workflow as real runtime dependencies.
- Add tests for the new entities and APIs.
- Apply only local migrations.
- End with a readiness report that is honest about remaining gaps.

