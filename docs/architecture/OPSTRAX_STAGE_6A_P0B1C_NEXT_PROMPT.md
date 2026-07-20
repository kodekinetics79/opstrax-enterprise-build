# Stage 6A -> P0-B1C Next Prompt

Stage 6A is complete locally.

Start **P0-B1C** as the first true business-spine slice.

## Scope

Build the canonical spine in this order:

1. Customer master
2. Customer contacts and addresses, minimal
3. Contract foundation
4. Rate card foundation
5. Job foundation
6. Trip foundation
7. Minimal charge / revenue foundation
8. APIs for the above
9. Tests
10. Local migration
11. Completion report

## Required platform dependencies

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
- Build invoice / payment / AR aging yet
- Build full UI redesign
- Build full AI automation
- Build full IoT ingestion
- Push, deploy, or touch production

## Output

- Local code changes only
- Local migration only if needed
- Tests and build verification
- Completion report

