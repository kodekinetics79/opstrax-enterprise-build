# OpsTrax Migration Verification Report — Gate 1

Run: `RETEST-20260821-1035-R1` · Gate 1 status: **RETRACTED — FAIL** (see retraction notice below) · Isolated environment: Docker `opstrax-retest-r1-pg`, 127.0.0.1:55437, CI-pinned `postgres:17` digest, databases `opstrax_local` (full chain) + `staging_upgrade` (upgrade-path replica)

## ⛔ RETRACTION NOTICE (orchestrator, post-Wave-C)

**This report previously concluded Gate 1 PASS. That conclusion was WRONG and is retracted.**

Proof (e) — "the five defect queries execute against the repaired contract" — was run against `opstrax_local`, which is **not migration-pure**: it was built with a bounded Dev boot that ran the runtime `*SchemaService` DDL, silently creating the very columns under test. The correct oracle is the migration-only replica `staging_upgrade`.

Re-run against `staging_upgrade` (routes: **16** columns vs opstrax_local's **24**):

| Contract | Migration-pure result |
|---|---|
| DEF-016 `hos_records` | OK |
| **DEF-017 route plans** | **FAIL — `column "efficiency_score" does not exist`** |
| DEF-018 `work_orders.asset_id` | OK |
| **DEF-019 safety events** | **FAIL — `column "incident_status" does not exist`** |
| DEF-020 audit list | OK |

`efficiency_score` sits in the **same `CASE` expression** as the `sla_risk` stage86 added (`EndpointMappings.cs:7307`), so `/api/routes` still 500s. Stage86 fixed 8 columns of a far larger split-brain: **1,006 runtime-only columns are absent from the migration-pure database; 507 distinct names, ~317 referenced in controller code.** `/api/routes/summary`, `/api/expenses/summary`, `/api/safety/summary`, `/api/routes/export` and others were independently reproduced as 42703 failures.

Compounding cause: `RuntimeSchemaMigrationParityTests` scans only **2 of 48** `*SchemaService.cs` files and matches column names **table-unqualified**, so it reported coverage it never had.

**DEF-017 and DEF-019 remain STILL FAILING. Gate 1 does not pass. No deployment may proceed on this basis.**

## Parser repair (the Stage-84 blocker)

The defective splitter — `CoreSchemaService.SplitStatements`, single-quote-aware only — is **deleted**, replaced by [SqlStatementSplitter.cs](../../backend-dotnet/Services/SqlStatementSplitter.cs): a five-state lexer (normal / single-quote with `''`+`E'\'` / `$tag$`-exact-match dollar quote / `--` line comment / nested `/* */` block comment, plus quoted-identifier consumption). 12 regression fixtures cover every mandated construct: `DO $$…$$`, tagged dollar quotes, interior semicolons, functions, triggers, line/block comments, escapes, quoted identifiers, `BEGIN;/COMMIT;` semantics (documented), a **whole-repo corpus test** (every `database/**/*.sql` splits with balanced dollar-quotes and content-preserving round-trip), an old-vs-new equivalence check on `001_schema.sql`, and a legacy-fixture test **reproducing the shred** (old algorithm emits a bare piecewise `REVOKE` from stage84's DO block; new splitter keeps it intact).

## Migration proofs (a–e)

| Proof | Result | Evidence |
|---|---|---|
| (a) Full clean chain incl. stage83ps/86/87 via the real runner | PASS — exit 0, all ledgered; bounded Dev boot → 335 tables | `artifacts/db-provisioning.log` |
| (b) Idempotent rerun of stage83ps/84/85/86/87 | PASS — benign NOTICEs only | log |
| (c) **Staging-upgrade path**: chain minus the five, then the five applied on the post-cutover FORCE-RLS baseline | PASS — safety_events backfill 15 rows/0 NULL; 0 unresolved users; FORCE RLS restored | log |
| (d) Stage-84 psql A/B | PASS — applies cleanly via `psql -f`; shred reproduced test-side → **splitter isolated as the defect** | SqlStatementSplitterTests |
| (e) Five defect queries as restricted `opstrax_app` | PASS — columns resolve (the 42703/42P01 class is gone); 0 rows without tenant ticket = correct RLS | log |

Notable correctness detail: stage86's `event_number` backfill runs inside a guarded DO block that lifts/restores `FORCE ROW LEVEL SECURITY` — without it, the owner-run UPDATE silently matches 0 rows post-cutover.

## Schema contracts closed

Stage86 adds all 8 runtime-only columns (`routes.sla_risk`, `work_orders.asset_id`, `maintenance_items.asset_id`, `safety_events.event_number`, `dashcam_events.event_number`, `audit_logs.severity/module_key/action_type`) — orchestrator-verified present in the isolated DB and enrolled in the readiness contract (`FleetProductionReadinessService` :349-356) so `/health/ready` turns red instead of shipping 500s if this class regresses. Stage87 backfills `users.role_id` with tenant-local-over-global precedence. Three new guard tests: runtime-schema↔migration parity (83-pair pre-existing allowlist, shrink-only), runner-array↔directory parity (25 orphans NAMED and fenced), and three-way stage86/87 contract.

## Packaging drift closed

`backend-dotnet/Dockerfile` per-file COPY list (silently stopped at stage82) → directory COPY; ci.yml packaging asserts now derive from the runner array (64 entries verified). `2026_08_21_stage83_platform_settings` enrolled in the runner (was applied by nothing). Dead contradictory `Telemetry__GatewaySecret` removed from the CI bounded-boot env.

## Test lanes (isolated DB, all three identity env vars)

| Lane | Result | Artifact |
|---|---|---|
| Non-DB | **1588/1588** (baseline 1529 + new guards) | `backend-unit-RETEST-R1-packet1.trx` |
| PostgreSQL integration | **401/401** (was BLOCKED entirely in UAT-20260821-1035) | `backend-db-RETEST-R1-packet1.trx` |
| RLS/tenant isolation | **9/9** | `rls-RETEST-R1-packet1.trx` |
| Warning ratchet | PASS, zero new | — |

## Exit criteria — NOT MET

The parser repair, packaging fixes, and lane execution below stand as verified work. The schema-contract exit criterion does **not**:

Zero Critical schema-contract failures on the isolated environment; migration-parser blocker resolved with regression fixtures; upgrade-from-staging-equivalent proven; readiness contract can now see the previously-invisible columns. **Gate 1: PASS.** Residuals: root `Dockerfile` (repo root) still hand-lists migrations (same drift class, unowned this run); 25 orphan migrations named for deliberate follow-up; staging Neon still needs stage83ps/84/85/86/87 applied at Gate 6.
