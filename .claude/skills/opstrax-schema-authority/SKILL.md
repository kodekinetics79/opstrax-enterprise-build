---
name: opstrax-schema-authority
description: >
  Use before writing ANY migration, adding a column, diagnosing a 42703/42P01
  (column/table does not exist), or claiming a schema fix works. Triggers:
  "column does not exist", "42703", "42P01", "relation does not exist",
  "add a column", "write a migration", "schema drift", "readiness contract",
  "health/ready is red", "the endpoint 500s", "apply migrations to staging",
  "SchemaService", "EnsureAsync", "is the schema fixed", "migration-pure",
  "stage8x". ALWAYS use before declaring a schema-contract defect repaired.
---

# OpsTrax Schema Authority

## The rule that matters most

**Never certify a schema fix against a database a Dev boot has touched.**

A Dev boot runs the runtime `*SchemaService` DDL and silently creates the very
columns under test. Certifying there produces a false PASS. This exact mistake
produced a retracted Gate 1 PASS in run RETEST-20260821-1035-R1: `/api/routes`
was declared fixed while `routes.efficiency_score` did not exist in any
migration, and the page would still have 500'd in staging.

| Database shape | Valid oracle? |
|---|---|
| Built by the migration chain ONLY | ✅ the only valid oracle |
| Any database a Dev boot ran against | ❌ contaminated — schema services created columns |
| Staging as it stands today | ❌ hand-patched; its DB is ahead of its code |

Build the oracle explicitly: roles → `database/init/*` → RLS cutover files →
`tools/apply-neon-predeploy-migrations.sh`. Nothing else. Then run the **real
controller SQL** against it, not a simplified paraphrase.

## Why the split-brain exists

`Program.cs ShouldRunSchemaInitAsync` **skips every runtime schema service**
when connected as a restricted role under RLS enforcement — which is always
true in staging and production. It logs *"Skipping schema init … Migrations/
seeders must be applied out-of-band by the DB owner."*

Consequence: **any column or table declared only in a `*SchemaService` can never
exist in a protected environment.** It is not drift and no redeploy fixes it.
Measured in Aug 2026: 1,006 runtime-only columns absent from a migration-pure
database, 507 distinct names, ~317 referenced by controller SQL.

Before blaming "drift", check: does a migration create this object at all?

```bash
grep -rn "column_name" database/migrations/ database/init/   # if empty, no migration exists
grep -rn "column_name" backend-dotnet/Services/*SchemaService.cs
```

## Migration requirements

- Ordered, idempotent, self-ledgering. `ADD COLUMN IF NOT EXISTS`,
  `CREATE TABLE IF NOT EXISTS`. Bare `ADD CONSTRAINT` / `CREATE INDEX` are NOT
  rerun-safe — guard them.
- Enrol in `tools/apply-neon-predeploy-migrations.sh` — ordering is a
  **hand-maintained array, not a filename sort**. A file enrolled nowhere is
  applied by nothing. (25 such orphans existed as of Aug 2026.)
- Must apply cleanly where the terminal stage58 cutover is already ledgered —
  that is staging's real state.
- Backfills under FORCE ROW LEVEL SECURITY match 0 rows unless you lift and
  restore FORCE inside the same transaction. Copy stage86's guard.
- Read `2026_08_11_stage76_telematics_security_hardening.sql` before writing
  grants: it **RAISES** if `opstrax_app` holds privileges it must not.

## The readiness contract is a tripwire, not decoration

`FleetProductionReadinessService` decides `/health/ready`, and `render.yaml`
gates traffic on that endpoint. Two failure directions:

1. A column missing from the contract → live 500s while readiness stays green.
2. A contract entry whose `format_type`/notnull/default string does not match
   the live catalog → readiness permanently RED → Render withholds traffic.

Verify every entry with the same functions the check uses:
`format_type(a.atttypid, a.atttypmod)`, `attnotnull`,
`pg_get_expr(d.adbin, d.adrelid)`.

## Deploy order is load-bearing

Migrations land **before** the SHA. Commit `de00b75` enrolled new tables into
the readiness contract; deploying any SHA at or after it before those
migrations apply makes `/health/ready` fail and Render withhold traffic.

## Proof checklist before saying "fixed"

1. Fresh migration-pure DB built from the chain.
2. Full chain applied twice — second run idempotent, benign NOTICEs only.
3. Upgrade path proven on a clone of the staging-equivalent baseline, not only
   on a clean database.
4. **Real controller SQL** executed and succeeding.
5. Queries run as restricted `opstrax_app`, not as owner.
6. Readiness entries verified against the live catalog.

Anything less is a source-inspection claim, not proof.
