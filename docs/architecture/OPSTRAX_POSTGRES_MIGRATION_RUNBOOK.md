# OpsTrax PostgreSQL Migration Runbook

## Current State
- Active backend uses PostgreSQL connection strings and Npgsql.
- Schema is currently created by startup schema services rather than a versioned migration tool.
- Stage 5 foundation migrations are additive and can be applied locally in order
  to a disposable database such as `opstrax_local`.

## Target Local Workflow
1. Create a migration with a clear, ordered name.
2. Review SQL for expand-only safety where possible.
3. Apply locally against a disposable or dev database.
4. Verify with build and targeted smoke tests.
5. Record the exact schema delta and rollback path.

## How Migrations Should Be Generated
- Prefer explicit SQL or framework-generated SQL that can be reviewed.
- Use one migration per bounded change.
- Include irreversible data moves in a dedicated review step.

## How Migrations Should Be Applied
- Local/dev only first.
- Use an explicit command or script.
- Never auto-apply production migrations without human approval.
- For Stage 5 work in this workspace, the verified local target is the Docker
  PostgreSQL instance on `127.0.0.1:5433`, not `.env` or any remote connection
  string.

## How Migration History Should Be Checked
- Maintain a migration history table or equivalent version ledger.
- Verify the expected version before and after every apply.
- If a worker/dispatcher is not yet present, the migration can still be valid
  provided the storage model includes retry/status fields and the rollback path
  is documented.

## Naming Strategy
- Prefix by sequence and purpose, for example `2026_06_27_p0b1_customer_master`.
