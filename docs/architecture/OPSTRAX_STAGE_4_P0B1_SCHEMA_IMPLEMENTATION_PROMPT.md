# OpsTrax Stage 4 P0-B1 Schema Implementation Prompt

You are now executing Stage 4 P0-B1 locally only.

## Hard Rules
- Do not push.
- Do not deploy.
- Do not touch production.
- Do not create the full enterprise schema.
- Do not add fake data to cover missing APIs.
- Do not bypass auth, tenant isolation, or RBAC.
- Do not let AI write directly to business tables.

## Scope
Implement only the first controlled schema slice:
1. PostgreSQL provider/connection readiness if still needed
2. Tenant/RBAC/security foundation
3. Customer master
4. Contract/rate-card foundation
5. Job/trip/revenue minimal spine
6. Domain event foundation
7. AI recommendation/action-request foundation
8. Approval foundation
9. Required backend models/entities
10. Required API contracts
11. Required tests
12. Required local migration
13. Required rollback check
14. Required completion report

## Mandatory Design Rules
- PostgreSQL-first.
- Tenant-owned tables require tenant_id and tenant indexes.
- Use idempotency for event and action processing.
- Centralize authorization decisions.
- Require feature flag and subscription checks.
- Keep AI read/recommend/request only.
- Force human approval for high-risk actions.

## Delivery
- Make the smallest safe schema slice.
- Verify with build and tests after changes.
- Document every migration and rollback path locally.

