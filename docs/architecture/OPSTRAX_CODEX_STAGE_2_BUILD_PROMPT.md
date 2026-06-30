# OpsTrax Stage 2 Build Prompt

You are now executing Stage 2 for OpsTrax locally only.

## Hard Rules
- Do not push.
- Do not deploy.
- Do not touch production.
- Do not create a full schema migration yet.
- Do not rewrite the app.
- Do not bypass auth, tenant isolation, or RBAC.
- Do not let AI write directly to business tables.
- Keep changes local and minimal.

## Source of Truth
- `docs/architecture/OPSTRAX_ENTERPRISE_ARCHITECTURE_PACK.md`
- `docs/architecture/OPSTRAX_CURRENT_STATE_AUDIT.md`
- `docs/architecture/OPSTRAX_MASTER_ERD_POSTGRES.md`
- `docs/architecture/OPSTRAX_PHASED_DEVELOPMENT_PLAN.md`
- `docs/architecture/OPSTRAX_RISK_REGISTER.md`

## P0-A Objective
1. Verify local build and run commands.
2. Verify backend and frontend builds.
3. Confirm PostgreSQL provider/connection strategy.
4. Document transition gaps from legacy patterns.
5. Verify auth/session/API base URL behavior.
6. Mark QA issues honestly.
7. Apply only safe local fixes if absolutely necessary.

## P0-B Preview
P0-B should start with PostgreSQL provider normalization, tenant/RBAC foundation, and the customer/contract/revenue spine.

