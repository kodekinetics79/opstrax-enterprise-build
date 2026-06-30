# Stage 8 Next Prompt

Stage 7A is complete locally.

Start **Stage 8** as the finance activation foundation.

## Scope

1. Invoice approval
2. Invoice issue conversion from draft to issued invoice
3. Issued invoice table
4. Invoice lines
5. Payment recording foundation
6. Credit note / dispute planning only
7. AR aging summary foundation only
8. Events / outbox
9. Approval rules
10. Tests
11. No payment gateway yet

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
- Build full AR aging
- Build full accounting
- Build full marketing automation
- Build full UI redesign
- Build full AI autonomy
- Build full IoT ingestion
- Push, deploy, or touch production

## Output

- Local code changes only
- Local migration only if required
- Tests and build verification
- Completion report
