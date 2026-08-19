# Finance / GL / tax migration window — runbook

Brings the 33 dead endpoints back. **Not demo week.** Schedule it deliberately.

## What is broken and why

33 GET endpoints return 500 in production. Verified by sweeping all 357 parameterless
GET routes with a real token: 273 × 200, 33 × 500, the rest auth/permission responses.

The 500s cluster cleanly, and 8 of 12 tables spot-checked simply **do not exist**:

| Cluster | Endpoints | Cause |
|---|---|---|
| Finance / GL / tax / revrec / settlements / billing | 20 | tables absent |
| Detention | 4 | tables present, column drift |
| Ops / observability | 3 | `/api/ops/{metrics,reliability,services}` |
| Cold chain | 2 | `cold_chain_readings` absent |
| Audit export, customer-ETA, security insights | 3 | mixed |

Ten migrations were never applied: stage35, 36, 37, 38, 39, 40, 41, 45, 46, 48.

## The trap — read before you start

The migrations are **safe**: 897 lines, 27 CREATE TABLE, zero DELETEs, zero backfills,
zero `SET NOT NULL`, and the only `DROP` sits inside a commented-out rollback block.
Creating tables that do not exist cannot break the 273 endpoints working today.

**But applying them alone will not fix anything.** They predate the stage58 security
cutover and still create the old policy model:

```
tenant_isolation      USING (company_id = current_setting('app.current_tenant_id'))
platform_admin_bypass USING (current_setting('app.platform_admin') = 'on')
```

Production is uniformly on stage58 — `system_control_plane` on 257 tables,
`tenant_ticket_app` on 248, **zero** on the legacy pair — and `BeginTenantScopeAsync`
takes the non-forgeable ticket path, never setting `app.current_tenant_id`. So those
policies can never match: the tables get created and the app still cannot read them.
The endpoints stay 500, just `42501` instead of `42P01`.

`stage45_general_ledger` is worse — it creates `chart_of_accounts`, `journal_entries`
and `journal_lines` with no grants, no RLS and no policies at all.

**Therefore: apply and reconcile in the same window.** Between the two steps the tables
are unreadable and `/health/ready` reports extra `rls_violations`. That is expected; do
not stop in the middle.

## Pre-flight

1. **Take a Neon branch as a checkpoint.** This is the rollback. Do it in the Neon
   console before touching anything — it is instant and it is the only real undo.
2. Confirm nothing else is mid-deploy: `curl -s $API/health | jq .version` should equal
   `git rev-parse --short=12 origin/main`.
3. Snapshot the current failure set so you can prove improvement:
   ```bash
   ./tools/pt40/sweep-endpoints.sh > /tmp/before.txt   # or re-run the sweep by hand
   ```

## Apply

Order matters only in that stage35 touches `job_charges` (which exists) and the rest are
independent. Applying in stage order is simplest:

```bash
cd /Users/zackkhan/Downloads/opstrax-enterprise-build-fixed-nginx
export PATH="/opt/homebrew/opt/libpq/bin:$PATH"
export NEON_PG_URI='postgresql://...'

for m in 2026_07_15_stage35_job_charges_rating_seam \
         2026_07_15_stage36_outbox_job_delivered_idempotency \
         2026_07_15_stage37_settlement_ap \
         2026_07_16_stage38_tax_engine \
         2026_07_16_stage39_billing_consolidation \
         2026_07_16_stage40_revenue_recognition \
         2026_07_16_stage41_fin_config_envelope \
         2026_07_22_stage45_general_ledger \
         2026_07_22_stage46_gl_period_close_export \
         2026_07_22_stage48_driver_detention_pay ; do
  echo "── $m"
  psql "$NEON_PG_URI" -v ON_ERROR_STOP=1 -f "database/migrations/$m.sql" || break
done
```

If one fails, stop and read the error — do not continue past a failure.

**Lock contention:** these create new tables, so they take no locks on hot tables and
should not contend. `stage35` alters `job_charges`; if it times out, that is the usual
background-worker contention — retry it, do not raise the timeout.

## Reconcile — same window, immediately after

```bash
psql "$NEON_PG_URI" -f tools/pt40/12-finance-stage58-reconciliation.sql
```

For each of the 27 tables it enables + forces RLS, drops the legacy policies, creates the
stage58 pair (detecting `company_id` vs `tenant_id` rather than assuming), grants
`opstrax_app` and `opstrax_system`, and grants USAGE on the identity sequences.

Both verification blocks must return **0 rows**:
- no finance table still carrying a legacy policy
- no finance table missing the stage58 pair

## Verify

1. Re-sweep the endpoints and diff against `/tmp/before.txt`. Expect the finance, tax,
   GL, revrec, settlements and billing clusters to move 500 → 200.
2. Anything still 500 is **column drift behind the missing table** — it could not surface
   while the table was absent. Get the real error from the deployment log rather than
   guessing:
   ```bash
   render logs --resources srv-d93dha0k1i2s73dm6ub0 --limit 40 --level error --output text --confirm
   ```
   `42703` → missing column; `42501` → grant or tenant-context problem.
3. `/health/ready` — `rls_violations` must be back to 0. `grant_violations` and
   `tenant_grant_violations` are governed by stage76/stage80 and are a separate backlog;
   they will not reach 0 here.

## Rollback

Restore the Neon branch. That is the whole procedure — do not attempt to reverse the
migrations by hand. The commented rollback block in stage35 is the only one written, and
it is deliberately not automated.

## Not covered by this window

- `/api/ops/{metrics,reliability,services}` and `/api/security/insights` do not obviously
  belong to these migrations. Diagnose them separately, from the logs.
- The `/api/platform/*` 404s are the platform-billing feature still uncommitted in the
  working tree — not deployed, so 404 is correct, not a fault.
