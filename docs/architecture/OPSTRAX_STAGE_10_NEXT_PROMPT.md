# Stage 10 Next Prompt

Stage 9 is complete locally.

Start **Stage 10** as the first business spine slice.

## Scope

1. Customer master
2. Customer contacts and addresses minimal
3. Contract foundation
4. Rate card foundation
5. Job foundation
6. Trip foundation
7. Minimal charge / revenue foundation
8. APIs for the above
9. Tests
10. Local migration only if required
11. Completion report

## Required dependencies

- Centralized authorization service
- DB-backed approval workflow
- DB-backed domain events / outbox
- DB-backed idempotency
- DB-backed AI recommendation / action request foundation
- DB-backed audit / correlation
- Outbox dispatcher
- PostgreSQL migration discipline

## Do not

- Build full CRM
- Build full invoice / payment / AR aging engine
- Build full AI automation
- Build full IoT ingestion
- Redesign frontend
- Push, deploy, or touch production

