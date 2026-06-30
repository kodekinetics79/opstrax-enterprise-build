# Stage 7 P0-B1D Next Prompt

Stage 7 P0-B1C is complete locally.

Start **P0-B1D** as the first revenue activation slice.

## Scope

1. Invoice issue / approval / conversion from draft
2. Credit note foundation
3. Revenue adjustment and dispute foundation
4. Minimal AR visibility
5. Customer-facing invoice summary endpoints
6. Tests
7. Local migration only if required
8. Completion report

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
- Build full invoice/payment/AR aging
- Build full UI redesign
- Build full AI automation
- Build full IoT ingestion
- Push, deploy, or touch production

## Output

- Local code changes only
- Local migration only if needed
- Tests and build verification
- Completion report
