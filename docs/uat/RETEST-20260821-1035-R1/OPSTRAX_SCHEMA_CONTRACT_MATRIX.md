# OpsTrax Schema Contract Matrix

Run: `RETEST-20260821-1035-R1` · Gate 0 three-way reconciliation with post-repair status
Deployed staging SHA `979c142b3b0b228e7c84b88a37c2eacb66b76d38` (2026-08-13) · Local HEAD `a6378c7` + remediation worktree · Isolated proof DB: `opstrax-retest-r1-pg` @ 127.0.0.1:55437

## The structural finding that reframed four defects

Four "schema drift" defects were **not** drift and **not** a failed Stage 84. `routes.sla_risk`, `work_orders.asset_id`, `safety_events.event_number`, and `audit_logs.severity/module_key/action_type` had **no migration anywhere in the repository**. They existed only inside `Batch2/3/4/7SchemaService`, and [Program.cs `ShouldRunSchemaInitAsync`](../../backend-dotnet/Program.cs) deliberately **skips every runtime schema service** when connected as a restricted role under RLS enforcement — precisely the condition staging and production always run under, logging *"Skipping schema init … Migrations/seeders must be applied out-of-band by the DB owner."*

Every protected environment was therefore **structurally guaranteed** to lack these columns. No redeploy and no Stage 84 fix could ever have produced them. This also explains DEF-020 exactly: the audit *count* query reads base columns and succeeded (6 events) while the audit *list* query filters on `module_key`/`severity`, threw 42703, and rendered zero — the surfaces never disagreed about data; one was erroring.

## Matrix

| Contract | Deployed app expects | Staging DB contains | Current source expects | Defect | Post-repair status |
|---|---|---|---|---|---|
| `hos_records` | table (assignment board, driver HOS) | **absent** — Stage 84 never applied | Stage 84 + runtime `DriverSchemaService` | DEF-016 / DEF-026 | Migration in chain, proven on isolated DB; **staging still needs stage83ps/84/85/86/87** |
| `routes.sla_risk` | column (Route Plans) | **absent — no migration existed** | Batch2SchemaService only → **now stage86** | DEF-017 | **stage86** creates it; enrolled in runner + readiness |
| `work_orders.asset_id` | column (Work Orders) | **absent — no migration existed** | Batch3SchemaService only → **now stage86** | DEF-018 | **stage86** |
| `maintenance_items.asset_id` | column | absent — no migration | Batch3SchemaService | (same class, found during repair) | **stage86** (8th column, beyond the original 7) |
| `safety_events.event_number` | column (Incidents) | **absent — no migration existed** | Batch4SchemaService only → **now stage86** | DEF-019 | **stage86** + `SAFE-` backfill under a FORCE-RLS lift/restore guard |
| `dashcam_events.event_number` | column | absent — no migration | Batch4SchemaService | (same class) | **stage86** |
| `audit_logs.severity` / `module_key` / `action_type` | columns (Audit Logs list) | **absent — no migration existed** | Batch7SchemaService only → **now stage86** | DEF-020 | **stage86**; UI no longer renders a failed load as zero |
| Driver-dashboard queries | `hos_records`, `coaching_tasks`, `drivers.user_id` | partial | Stage 84 objects | DEF-026 | Schema via stage84; **code now degrades** (to_regclass + 42P01/42501/42703 catch) instead of 500 |
| Customer/job ownership | `users.customer_id` → `jobs.customer_id` | **present — not a schema defect** | same | DEF-027 | Cause was binding/data + missing `deleted_at` filters + silent-empty on dangling binding → now fail-closed 403, binding validated and audited, UI field added |
| Audit stores/projections | `audit_logs` base + enrichment | base only | both | DEF-020 | stage86 supplies enrichment; single authoritative projection |
| Role membership calculation | `users.role_id` → `roles` | populated inconsistently (NULL from platform/seeder paths) | same | DEF-021 | **stage87** backfill (tenant-local-over-global precedence) + role_id supplied at all 4 provisioning inserts |
| `company_security_settings`, lockout cols, `security_events` | present | **hand-applied out-of-band 2026-08-21** | Stage 83 | DEF-012 (fixed) | Staging DB is **ahead of** its deployed code — environment not reproducible from any single SHA |
| Fleet + market identity readiness | contract booleans | present | stage50/51/52/80 | DEF-010 (fixed) | Closed |

## Readiness-contract blind spot (closed)

`FleetProductionReadinessService` required `hos_records` but had **no entry** for `sla_risk`, `asset_id`, or `event_number` — so DEF-017/018/019 produced live 500s while `/health/ready` reported green. All 8 columns are now enrolled (`:349-356`), so this failure class turns readiness **red** instead of shipping 500s. *(Wave C is independently verifying the declared `format_type` strings against the live catalog — a mismatch there would make readiness permanently red, which is itself an outage vector.)*

## Deploy-order constraint (blocking, carried to Gate 6)

Commit `de00b75` enrolled `company_security_settings`, `security_events`, `driver_offline_queue`, and `hos_records` into the readiness contract. **Deploying any SHA at or after `de00b75` before stage83ps/84/85/86/87 are applied will fail `/health/ready`**, and `render.yaml` gates traffic on that endpoint. Migrations must land first.

## Fenced residuals (named, test-enforced, not silently ignored)

- **83 runtime-only (table,column) pairs** still exist solely in `Batch*SchemaService` — the same split-brain class. Allowlisted in `RuntimeSchemaMigrationParityTests` with shrink-only hygiene assertions. *Wave C is auditing whether any is queried by an endpoint — that combination would be a live 500 waiting to fire.*
- **25 orphan migrations** enrolled in no applier — named in `MigrationRunnerEnrollmentParityTests`.
- Root `Dockerfile` still hand-lists migrations (same drift class fixed in `backend-dotnet/Dockerfile`).
