#!/usr/bin/env bash
set -euo pipefail

: "${OPSTRAX_TEST_DB_HOST:=127.0.0.1}"
: "${OPSTRAX_TEST_DB_PORT:=59955}"
: "${OPSTRAX_TEST_DB_USER:=zayra}"
: "${OPSTRAX_TEST_DB_PASSWORD:=zayra}"

audit_db="opstrax_predeploy_clean_${$}"
case "$audit_db" in
  opstrax_predeploy_clean_[0-9]*) ;;
  *) echo "Unsafe audit database name" >&2; exit 2 ;;
esac

export PGPASSWORD="$OPSTRAX_TEST_DB_PASSWORD"
cleanup() {
  dropdb --if-exists -h "$OPSTRAX_TEST_DB_HOST" -p "$OPSTRAX_TEST_DB_PORT" \
    -U "$OPSTRAX_TEST_DB_USER" "$audit_db" >/dev/null 2>&1 || true
}
trap cleanup EXIT

createdb -h "$OPSTRAX_TEST_DB_HOST" -p "$OPSTRAX_TEST_DB_PORT" \
  -U "$OPSTRAX_TEST_DB_USER" "$audit_db"

# This is the actual pre-run production baseline, deliberately without Stage58.
# It catches migration ordering defects that a clone of an already-secured database masks.
for fixture in \
  database/init/001_schema.sql \
  database/init/002_seed.sql \
  database/init/004_jobs_execution.sql \
  database/migrations/2026_06_30_stage19_row_level_security.sql \
  database/migrations/2026_06_30_stage20_rls_force_and_app_role.sql \
  database/migrations/2026_07_01_stage22_rls_reconcile_coverage.sql
do
  psql -h "$OPSTRAX_TEST_DB_HOST" -p "$OPSTRAX_TEST_DB_PORT" \
    -U "$OPSTRAX_TEST_DB_USER" -d "$audit_db" -v ON_ERROR_STOP=1 -q -f "$fixture"
done

log_dir=$(mktemp -d /tmp/opstrax-predeploy-clean.XXXXXX)
export NEON_PG_URI="postgresql://${OPSTRAX_TEST_DB_USER}:${OPSTRAX_TEST_DB_PASSWORD}@${OPSTRAX_TEST_DB_HOST}:${OPSTRAX_TEST_DB_PORT}/${audit_db}?sslmode=disable"
./tools/apply-neon-predeploy-migrations.sh >"$log_dir/pass1.log" 2>&1
./tools/apply-neon-predeploy-migrations.sh >"$log_dir/pass2.log" 2>&1

terminal_line=$(grep -n "Applying terminal Stage58/59 security reconciliation" "$log_dir/pass1.log" | cut -d: -f1)
coverage_line=$(grep -n "Post-check: production-wide tenant RLS coverage" "$log_dir/pass1.log" | cut -d: -f1)
test -n "$terminal_line" && test -n "$coverage_line" && test "$terminal_line" -lt "$coverage_line"
grep -q "Tenant RLS coverage: .* in-scope tables verified" "$log_dir/pass1.log"
grep -q "Existing Stage58/59 deployment reconciled without restoring legacy GUC policies" "$log_dir/pass2.log"

psql "$NEON_PG_URI" -v ON_ERROR_STOP=1 -q <<'SQL'
DO $clean_chain$
DECLARE
  bad_tables text[];
BEGIN
  IF EXISTS (
    SELECT 1 FROM (VALUES
      ('2026_07_31_stage58_nonforgeable_tenant_ticket'),
      ('2026_07_31_stage59_data_protection_key_ring'),
      ('2026_08_01_stage60_dispatch_trip_pilot'),
      ('2026_08_01_stage61_operations_proof_center'),
      ('2026_08_01_stage62_last_mile_pilot'),
      ('2026_08_01_stage63_route_plans_pilot')) required(version)
    WHERE (SELECT count(*) FROM schema_migrations sm WHERE sm.version=required.version)<>1
  ) THEN
    RAISE EXCEPTION 'Clean-chain target ledgers are missing or duplicated';
  END IF;

  SELECT array_agg(required.table_name ORDER BY required.table_name) INTO bad_tables
  FROM (VALUES
    ('dispatch_proofs'),('dispatch_proof_artifacts'),
    ('smart_assignment_recommendations'),('assignment_confirmations'),
    ('site_access_requirements'),('access_documents'),('pickup_authorizations'),
    ('warehouse_handovers'),('proof_packages'),('proof_artifacts'),
    ('billing_confidence_records')) required(table_name)
  JOIN pg_class c ON c.oid=to_regclass('public.'||required.table_name)
  WHERE NOT c.relrowsecurity OR NOT c.relforcerowsecurity
    OR (SELECT count(*) FROM pg_policies p
        WHERE p.schemaname='public' AND p.tablename=required.table_name)<>2
    OR NOT EXISTS (SELECT 1 FROM pg_policies p
        WHERE p.schemaname='public' AND p.tablename=required.table_name
          AND p.policyname='tenant_ticket_app' AND p.roles='{opstrax_app}'::name[]
          AND p.cmd='ALL' AND p.qual LIKE '%opstrax_security.current_tenant_id()%'
          AND p.with_check=p.qual)
    OR NOT EXISTS (SELECT 1 FROM pg_policies p
        WHERE p.schemaname='public' AND p.tablename=required.table_name
          AND p.policyname='system_control_plane' AND p.roles='{opstrax_system}'::name[]
          AND p.cmd='ALL' AND p.qual='true' AND p.with_check='true');
  IF COALESCE(cardinality(bad_tables),0)>0 THEN
    RAISE EXCEPTION 'Clean-chain pilot policy reconciliation failed: %',bad_tables;
  END IF;

  IF EXISTS (SELECT 1 FROM pg_policies WHERE schemaname='public' AND roles='{public}'::name[])
     OR EXISTS (SELECT 1 FROM pg_policies WHERE schemaname='public'
        AND (COALESCE(qual,'') LIKE '%app.current_tenant_id%'
          OR COALESCE(with_check,'') LIKE '%app.current_tenant_id%')) THEN
    RAISE EXCEPTION 'Clean-chain left a legacy/public tenant-policy window';
  END IF;
END
$clean_chain$;
SQL

echo "Predeploy clean-chain regression passed (fresh Stage19/20/22 baseline + runner twice + terminal policy checks)."
