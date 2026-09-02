# Migration and Readiness Evidence

The governing production deploy at `b982ef8b7020b490cdf7968364f6c15421fcf83f`
reported `missing_tables=5` while its other readiness categories were green. The
public health contract intentionally exposes only the aggregate count. Protected
catalog/log access sufficient to name those five relations was unavailable; the
smallest aggregate/catalog query required is recorded as a residual blocker rather
than guessing names from source.

Safe local proof completed:

- genuinely empty PostgreSQL 16 cluster, migration-only and never runtime-booted;
- discovered and fixed Stage80's pre-terminal dependency on the cluster-global
  `opstrax_system` role;
- clean runner completion: 336 public tables, 76 migration ledger rows, both
  restricted roles present;
- Stage89 owner migration adds and indexes `inbox_messages.next_attempt_at`, keeping
  exponential inbox backoff executable in protected environments;
- predecessor-plus-upgrade database completed the full owner chain;
- restricted `opstrax_app` / `opstrax_system` database suites passed;
- production-mode readiness agreed with the migrated catalog and returned HTTP 200;
- no runtime schema initializer was needed to make the migration-pure chain pass.

Required protected query (names only; no row or payload data):

```sql
WITH required(object_name) AS (VALUES
  ('alert_rules'),('alert_follow_up_tasks'),('customer_visibility'),
  ('dispatch_eligibility_config'),('journal_entries'),
  ('messaging_conversations'),('messaging_messages'),('platform_invoice_lines'),
  ('tenant_billing_plan_items'),('platform_tax_registrations'),
  ('platform_tax_rules'),('safety_coaching_tasks'),('sso_connections'),
  ('access_reviews'),('backup_verifications'),('tenant_api_keys'),
  ('tenant_webhook_settings'),('company_profile'),('user_notification_prefs')
)
SELECT required.object_name
FROM required
WHERE to_regclass('public.' || required.object_name) IS NULL
ORDER BY required.object_name;
```

A staging candidate run can prove whether any of the 19 required objects remain
missing in that isolated database; it cannot retroactively name production's five.
Only the protected production catalog query can complete that exact-name subtask,
which remains BLOCKED. The local migration/readiness implementation gate is PASS.
