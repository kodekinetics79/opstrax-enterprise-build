# OpsTrax Stage 2 P0-B Implementation Plan

## Objective
Move from local readiness into a controlled PostgreSQL-first foundation without expanding the full product surface.

## Scope
1. PostgreSQL provider and connection strategy
2. Tenant/RBAC foundation
3. Customer master
4. Contract and rate-card foundation
5. Job/trip/revenue minimal spine
6. Required migrations
7. Required backend entities/services
8. Required API contracts
9. Required frontend integration
10. Tests and acceptance criteria
11. Rollback strategy

## Implementation Slice
- Normalize the active backend into a single authoritative PostgreSQL path.
- Add formal migration tooling before new schema growth.
- Consolidate tenant scoping conventions.
- Add the smallest customer/contract/revenue spine required to support the enterprise model.

## Acceptance Criteria
- PostgreSQL connection path is explicit and tested.
- New tenant-owned tables follow the tenant_id rule.
- Core commercial entities can be created/read locally.
- Rollback strategy is documented before any schema expansion.

