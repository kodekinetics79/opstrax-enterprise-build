# Fleet specialized branch migration runbook

Cold Chain devices, returnable assets, and Saudi readiness documents are operational records. They must have an explicit `branch_id` before branch-scoped users can see or mutate them. Temperature zones, asset types, and cold-chain policies may intentionally remain tenant-shared (`branch_id IS NULL`).

At startup, `FleetTmsColdChainSchemaService` performs deterministic backfill:

1. Child readings, alerts, assignments, asset events, barcode scans, and RFID events inherit the branch of their device or asset.
2. A NULL-branch top-level operational record is assigned automatically only when its company has exactly one active, non-deleted branch.
3. Ambiguous top-level rows remain tenant-only and are recorded in `fleet_tms_branch_migration_audit` as `tenant_unassigned`. They are never exposed to a branch account.

Before pilot cutover, review unresolved rows:

```sql
SELECT company_id, source_table, source_id, classification, reason
FROM fleet_tms_branch_migration_audit
WHERE classification = 'tenant_unassigned'
ORDER BY company_id, source_table, source_id;
```

After confirming ownership with the customer, update the source row with an active branch from the same company. Re-run application startup so child rows inherit that branch. Validate that no operational rows remain unresolved, then retain the audit rows as migration evidence; do not delete them.

Never bulk-assign ambiguous rows to the first branch of a multi-branch tenant. If ownership cannot be established, keep the row tenant-only and exclude it from branch pilot workflows.
