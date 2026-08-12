#!/usr/bin/env bash
set -euo pipefail

: "${OPSTRAX_TEST_DB_HOST:=127.0.0.1}"
: "${OPSTRAX_TEST_DB_PORT:=59955}"
: "${OPSTRAX_TEST_DB_USER:=zayra}"
: "${OPSTRAX_TEST_DB_PASSWORD:?Set OPSTRAX_TEST_DB_PASSWORD for the disposable test database}"

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

# Reproduce the pre-Stage66 runtime-created nonce ledger. That legacy table used
# nonce_hash as its primary key and had neither an id column nor an owned sequence.
# Stage66 must reconcile it in place without losing outstanding stream tickets.
psql -h "$OPSTRAX_TEST_DB_HOST" -p "$OPSTRAX_TEST_DB_PORT" \
  -U "$OPSTRAX_TEST_DB_USER" -d "$audit_db" -v ON_ERROR_STOP=1 -q <<'SQL'
CREATE TABLE telemetry_stream_ticket_nonces (
  nonce_hash VARCHAR(64) PRIMARY KEY,
  audit_company_id BIGINT NOT NULL,
  branch_id BIGINT NULL,
  user_id BIGINT NOT NULL,
  expires_at TIMESTAMPTZ NOT NULL,
  consumed_at TIMESTAMPTZ NULL,
  created_at TIMESTAMPTZ NOT NULL DEFAULT NOW()
);
INSERT INTO telemetry_stream_ticket_nonces
  (nonce_hash,audit_company_id,branch_id,user_id,expires_at)
VALUES
  (repeat('a',64),1,NULL,1,NOW()+INTERVAL '90 seconds');
SQL

log_dir=$(mktemp -d /tmp/opstrax-predeploy-clean.XXXXXX)
export NEON_PG_URI="postgresql://${OPSTRAX_TEST_DB_USER}:${OPSTRAX_TEST_DB_PASSWORD}@${OPSTRAX_TEST_DB_HOST}:${OPSTRAX_TEST_DB_PORT}/${audit_db}?sslmode=disable"
./tools/apply-neon-predeploy-migrations.sh >"$log_dir/pass1.log" 2>&1
./tools/apply-neon-predeploy-migrations.sh >"$log_dir/pass2.log" 2>&1
# Stage66 is advertised as additive/idempotent and may be rerun during a credential
# cutover repair. Exercise its SQL directly instead of only testing the ledger skip.
psql "$NEON_PG_URI" -v ON_ERROR_STOP=1 -q -f database/migrations/2026_08_02_stage66_telematics_pilot.sql
psql "$NEON_PG_URI" -v ON_ERROR_STOP=1 -q -f database/migrations/2026_08_02_stage67_telematics_diagnostics_integrity.sql
# Simulate a ledgered but weakened Stage68 commercial boundary.
psql "$NEON_PG_URI" -v ON_ERROR_STOP=1 -q <<'SQL'
ALTER TABLE companies ALTER COLUMN entitlement_policy_mode DROP NOT NULL;
ALTER TABLE companies ALTER COLUMN entitlement_policy_mode DROP DEFAULT;
ALTER TABLE companies DROP CONSTRAINT ck_companies_entitlement_policy_mode;
ALTER TABLE companies ADD CONSTRAINT ck_companies_entitlement_policy_mode
  CHECK (entitlement_policy_mode IN ('legacy_allow','package_allowlist','open')) NOT VALID;
SQL
psql "$NEON_PG_URI" -v ON_ERROR_STOP=1 -q -f database/migrations/2026_08_02_stage68_entitlement_policy_mode.sql
# Simulate a ledgered but weakened Stage69 catalog. The repair replay must not
# trust the constraint name and must restore nullability/default/enforcement.
psql "$NEON_PG_URI" -v ON_ERROR_STOP=1 -q <<'SQL'
ALTER TABLE tenant_market_packs ALTER COLUMN status DROP NOT NULL;
ALTER TABLE tenant_market_packs ALTER COLUMN status DROP DEFAULT;
ALTER TABLE tenant_market_packs DROP CONSTRAINT ck_tenant_market_packs_status;
ALTER TABLE tenant_market_packs ADD CONSTRAINT ck_tenant_market_packs_status
  CHECK (status IN ('active','disabled','trial')) NOT VALID;
SQL
psql "$NEON_PG_URI" -v ON_ERROR_STOP=1 -q -f database/migrations/2026_08_02_stage69_market_pack_control_hardening.sql
# Simulate post-ledger HOS column drift on the disposable database, then prove
# Stage70's recovery replay is additive and complete.
psql "$NEON_PG_URI" -v ON_ERROR_STOP=1 -q <<'SQL'
ALTER TABLE hos_logs DROP COLUMN notes;
ALTER TABLE hos_clocks DROP COLUMN break_needed_at;
ALTER TABLE hos_clocks DROP COLUMN reset_at;
ALTER TABLE hos_clocks DROP COLUMN updated_at;
SQL
psql "$NEON_PG_URI" -v ON_ERROR_STOP=1 -q -f database/migrations/2026_08_02_stage70_hos_pilot_schema_reconciliation.sql
# Production skips runtime schema initialization. Prove the owner repair
# migration restores the driver acknowledgement evidence field after drift.
psql "$NEON_PG_URI" -v ON_ERROR_STOP=1 -q -c 'ALTER TABLE coaching_tasks DROP COLUMN acknowledged_note'
psql "$NEON_PG_URI" -v ON_ERROR_STOP=1 -q -f database/migrations/2026_08_02_stage71_coaching_evidence_reconciliation.sql
# Reapply the HOS immutability/offboarding guard as a drift repair. Its function
# bodies are owner-managed security contract, not runtime application state.
psql "$NEON_PG_URI" -v ON_ERROR_STOP=1 -q -f database/migrations/2026_08_02_stage72_hos_offboarding_immutability_reconciliation.sql
psql "$NEON_PG_URI" -v ON_ERROR_STOP=1 -q -f database/migrations/2026_08_02_stage73_hos_offboarding_null_fail_closed.sql
# Prove the Production retention ledger is owner-repairable after schema drift.
psql "$NEON_PG_URI" -v ON_ERROR_STOP=1 -q -c 'DROP TABLE data_retention_policies'
# Simulate a ledgered Stage42 schema loss too. The owner runner must recreate the credential
# registry before terminal Stage58/76 restore its policies and secret-column boundary.
psql "$NEON_PG_URI" -v ON_ERROR_STOP=1 -q -c 'DROP TABLE telemetry_gateways'
# Use the real runner so Stage42/74 recreate their tables and the terminal Stage58
# reconciliation restores FORCE-RLS policies and restricted-role grants.
./tools/apply-neon-predeploy-migrations.sh >"$log_dir/pass3.log" 2>&1
psql "$NEON_PG_URI" -v ON_ERROR_STOP=1 -q <<'SQL'
SET ROLE opstrax_system;
INSERT INTO telemetry_stream_ticket_nonces
  (nonce_hash,audit_company_id,branch_id,user_id,expires_at)
VALUES
  (repeat('b',64),1,NULL,2,NOW()+INTERVAL '90 seconds');
RESET ROLE;
SQL

# A bare app-role session has no signed tenant ticket. Even with INSERT granted,
# FORCE RLS must reject an arbitrary audit_company_id instead of trusting input.
if psql "$NEON_PG_URI" -v ON_ERROR_STOP=1 -q <<'SQL'
BEGIN;
SET LOCAL ROLE opstrax_app;
INSERT INTO telemetry_stream_ticket_nonces
  (nonce_hash,audit_company_id,branch_id,user_id,expires_at)
VALUES
  (repeat('c',64),2,NULL,3,NOW()+INTERVAL '90 seconds');
ROLLBACK;
SQL
then
  echo "Bare app role bypassed the stream-ticket nonce tenant boundary" >&2
  exit 1
fi

terminal_line=$(grep -n "Applying terminal Stage58/59 security reconciliation" "$log_dir/pass1.log" | cut -d: -f1)
coverage_line=$(grep -n "Post-check: production-wide tenant RLS coverage" "$log_dir/pass1.log" | cut -d: -f1)
stage67_line=$(grep -n "Reapplying Stage67 least-privilege device credential boundary" "$log_dir/pass1.log" | cut -d: -f1)
stage76_line=$(grep -n "Applying terminal Stage76 telemetry" "$log_dir/pass1.log" | cut -d: -f1)
test -n "$terminal_line" && test -n "$coverage_line" && test "$terminal_line" -lt "$coverage_line"
test -n "$stage67_line" && test -n "$stage76_line" && test "$stage67_line" -lt "$stage76_line"
grep -q "applying 2026_07_16_stage42_telemetry_gateways" "$log_dir/pass1.log"
grep -q "Tenant RLS coverage: .* in-scope tables verified" "$log_dir/pass1.log"
grep -q "Existing Stage58/59/67/76 deployment reconciled with Stage76 terminal" "$log_dir/pass2.log"
grep -q "Applying terminal Stage76 telemetry" "$log_dir/pass2.log"
grep -q "Existing Stage58/59/67/76 deployment reconciled with Stage76 terminal" "$log_dir/pass3.log"
grep -q "Applying terminal Stage76 telemetry" "$log_dir/pass3.log"

psql "$NEON_PG_URI" -v ON_ERROR_STOP=1 -q <<'SQL'
DO $clean_chain$
DECLARE
  bad_tables text[];
BEGIN
  IF EXISTS (
    SELECT 1
    FROM pg_default_acl defaults
    LEFT JOIN pg_namespace namespace ON namespace.oid=defaults.defaclnamespace
    CROSS JOIN LATERAL aclexplode(defaults.defaclacl) privilege
    LEFT JOIN pg_roles grantee ON grantee.oid=privilege.grantee
    WHERE (defaults.defaclnamespace=0 OR namespace.nspname='public')
      AND defaults.defaclobjtype IN ('r','S')
      AND (privilege.grantee=0 OR grantee.rolname IN ('opstrax_app','opstrax_system'))
  ) THEN
    RAISE EXCEPTION 'Clean-chain Stage76 global/public default ACL boundary is unsafe';
  END IF;

  IF EXISTS (
    SELECT 1 FROM (VALUES
      ('2026_07_16_stage42_telemetry_gateways'),
      ('2026_07_31_stage58_nonforgeable_tenant_ticket'),
      ('2026_07_22_stage47_detention_recovery'),
      ('2026_07_31_stage59_data_protection_key_ring'),
      ('2026_08_01_stage60_dispatch_trip_pilot'),
      ('2026_08_01_stage61_operations_proof_center'),
      ('2026_08_01_stage62_last_mile_pilot'),
      ('2026_08_01_stage63_route_plans_pilot'),
      ('2026_08_01_stage64_shipments_pilot'),
      ('2026_08_01_stage65_safety_pilot'),
      ('telematics_001_latest_position_provenance'),
      ('telematics_002_device_lifecycle'),
      ('telematics_003_canonical_telemetry_timeseries'),
      ('telematics_004_device_trust_policy'),
      ('telematics_005_replay_guard'),
      ('telematics_006_projection_inbox'),
      ('2026_08_02_stage66_telematics_pilot'),
      ('2026_08_02_stage67_telematics_diagnostics_integrity'),
      ('2026_08_02_stage68_entitlement_policy_mode'),
      ('2026_08_02_stage69_market_pack_control_hardening'),
      ('2026_08_02_stage70_hos_pilot_schema_reconciliation'),
      ('2026_08_02_stage71_coaching_evidence_reconciliation'),
      ('2026_08_02_stage72_hos_offboarding_immutability_reconciliation'),
      ('2026_08_02_stage73_hos_offboarding_null_fail_closed'),
      ('2026_08_02_stage74_retention_policy_production_contract'),
      ('2026_08_02_stage75_bounded_support_access'),
      ('2026_08_12_stage77_protected_role_bootstrap'),
      ('2026_08_11_stage76_telematics_security_hardening')) required(version)
    WHERE (SELECT count(*) FROM schema_migrations sm WHERE sm.version=required.version)<>1
  ) THEN
    RAISE EXCEPTION 'Clean-chain target ledgers are missing or duplicated';
  END IF;

  IF to_regclass('public.telemetry_gateways') IS NULL THEN
    RAISE EXCEPTION 'Stage42 telemetry gateway owner schema is missing from the clean chain';
  END IF;

  IF NOT EXISTS (
    SELECT 1 FROM information_schema.columns
    WHERE table_schema='public' AND table_name='coaching_tasks' AND column_name='acknowledged_note'
  ) OR NOT EXISTS (
    SELECT 1 FROM pg_constraint
    WHERE conrelid='public.coaching_tasks'::regclass
      AND conname='ck_stage71_coaching_acknowledged_note_length'
  ) THEN
    RAISE EXCEPTION 'Stage71 coaching acknowledgement evidence contract is incomplete';
  END IF;

  IF EXISTS (
    SELECT 1 FROM (VALUES
      ('Super Admin'),('Company Admin'),('Fleet Manager'),('Dispatcher'),('Driver'),
      ('Mechanic'),('Safety Manager'),('Compliance Manager'),('Customer Service'),
      ('Customer Portal User'),('Reseller / Partner Admin'),('Read-only Auditor'),
      ('Operations Manager'),('Finance & Billing Manager'),('CRM & Sales Manager'),
      ('Vendor Service Provider')
    ) required(name)
    WHERE NOT EXISTS (
      SELECT 1 FROM roles role
      WHERE role.company_id IS NULL AND role.is_system AND role.name=required.name
    )
  ) OR EXISTS (
    SELECT 1 FROM (VALUES
      ('platform_super_admin'),('sales_admin'),('marketing_admin'),('finance_admin'),
      ('customer_success_admin'),('support_admin'),('product_admin'),
      ('compliance_admin'),('readonly_executive')
    ) required(role_key)
    WHERE NOT EXISTS (SELECT 1 FROM platform_roles role WHERE role.role_key=required.role_key)
  ) OR NOT EXISTS (
    SELECT 1 FROM platform_role_permissions permission
    JOIN platform_roles role ON role.id=permission.role_id
    WHERE role.role_key='platform_super_admin' AND permission.permission_key='platform:*'
  ) OR EXISTS (
    SELECT 1 FROM platform_role_permissions permission
    JOIN platform_roles role ON role.id=permission.role_id
    WHERE role.role_key='support_admin' AND permission.permission_key='platform:impersonation:start'
  ) THEN
    RAISE EXCEPTION 'Clean-chain Stage77 authorization bootstrap is incomplete or unsafe';
  END IF;

  IF to_regclass('public.data_retention_policies') IS NULL
     OR NOT EXISTS (
       SELECT 1 FROM pg_constraint
       WHERE conrelid='public.data_retention_policies'::regclass
         AND conname='ck_data_retention_policy_minimums'
     ) THEN
    RAISE EXCEPTION 'Stage 74 retention policy Production contract is incomplete';
  END IF;

  IF NOT EXISTS (
    SELECT 1 FROM information_schema.columns
    WHERE table_schema='public' AND table_name='dispatch_assignments'
      AND column_name='completed_at'
  ) THEN
    RAISE EXCEPTION 'Stage60 detention attribution timestamp dependency is missing';
  END IF;

  IF NOT has_table_privilege('opstrax_app','public.telemetry_stream_ticket_nonces','INSERT')
     OR has_table_privilege('opstrax_app','public.telemetry_stream_ticket_nonces','SELECT')
     OR has_table_privilege('opstrax_app','public.telemetry_stream_ticket_nonces','UPDATE,DELETE,TRUNCATE,REFERENCES,TRIGGER')
     OR NOT has_table_privilege('opstrax_system','public.telemetry_stream_ticket_nonces','SELECT')
     OR NOT has_table_privilege('opstrax_system','public.telemetry_stream_ticket_nonces','INSERT')
     OR NOT has_table_privilege('opstrax_system','public.telemetry_stream_ticket_nonces','UPDATE')
     OR NOT has_table_privilege('opstrax_system','public.telemetry_stream_ticket_nonces','DELETE')
     OR has_table_privilege('opstrax_system','public.telemetry_stream_ticket_nonces','TRUNCATE,REFERENCES,TRIGGER')
     OR NOT (SELECT relrowsecurity AND relforcerowsecurity FROM pg_class
             WHERE oid='public.telemetry_stream_ticket_nonces'::regclass)
     OR (SELECT count(*) FROM pg_policies
         WHERE schemaname='public' AND tablename='telemetry_stream_ticket_nonces')<>2
     OR NOT has_sequence_privilege('opstrax_app','telemetry_stream_ticket_nonces_id_seq','USAGE')
     OR has_sequence_privilege('opstrax_app','telemetry_stream_ticket_nonces_id_seq','SELECT,UPDATE')
     OR NOT has_sequence_privilege('opstrax_system','telemetry_stream_ticket_nonces_id_seq','USAGE')
     OR has_sequence_privilege('opstrax_system','telemetry_stream_ticket_nonces_id_seq','SELECT,UPDATE')
     OR has_sequence_privilege('public','telemetry_stream_ticket_nonces_id_seq','USAGE,SELECT,UPDATE') THEN
    RAISE EXCEPTION 'Terminal reconciliation broke stream-ticket nonce least-privilege grants';
  END IF;

  IF position('COALESCE(current_setting(''opstrax.offboarding''' in pg_get_functiondef('stage65_prevent_certified_hos_log_delete()'::regprocedure))=0
     OR position('opstrax_system' in pg_get_functiondef('stage65_prevent_certified_hos_log_delete()'::regprocedure))=0
     OR position('COALESCE(current_setting(''opstrax.offboarding''' in pg_get_functiondef('stage65_guard_hos_certification_snapshot()'::regprocedure))=0
     OR position('opstrax_system' in pg_get_functiondef('stage65_guard_hos_certification_snapshot()'::regprocedure))=0 THEN
    RAISE EXCEPTION 'Stage73 null-safe HOS offboarding immutability guard is incomplete';
  END IF;
  IF position('COALESCE(current_setting(''opstrax.offboarding''' in pg_get_functiondef('detention_evidence_immutable()'::regprocedure))=0
     OR position('opstrax_system' in pg_get_functiondef('detention_evidence_immutable()'::regprocedure))=0 THEN
    RAISE EXCEPTION 'Stage72 null-safe detention offboarding immutability guard is incomplete';
  END IF;

  IF to_regclass('public.idx_jobs_active_projection') IS NULL
     OR to_regclass('public.idx_dispatch_assignments_job_projection_recent') IS NULL
     OR to_regclass('public.idx_proof_packages_company_job_recent') IS NULL
     OR to_regclass('public.idx_location_company_vehicle_recent') IS NULL
     OR to_regclass('public.idx_pod_company_job_projection_recent') IS NULL
     OR to_regclass('public.uq_incidents_company_idempotency') IS NULL
     OR to_regclass('public.uq_coaching_tasks_company_idempotency') IS NULL
     OR to_regclass('public.uq_dvir_reports_company_idempotency') IS NULL
     OR to_regclass('public.uq_hos_logs_company_source_event') IS NULL
     OR to_regclass('public.idx_hos_certifications_company_branch_driver_date') IS NULL
     OR to_regclass('public.idx_eld_malfunction_history_company_device') IS NULL
     OR to_regclass('public.idx_eld_devices_company_branch_status') IS NULL
     OR to_regclass('public.uq_fault_codes_canonical_projection') IS NULL
     OR to_regclass('public.uq_fault_occurrences_source_dtc') IS NULL
     OR to_regclass('public.uq_diagnostic_holds_active_fault') IS NULL
     OR to_regclass('public.idx_fault_occurrences_company_vehicle_time') IS NULL
     OR to_regclass('public.uq_dvir_defects_active_fault') IS NULL
     OR to_regclass('public.idx_device_channel_health_company_status') IS NULL
     OR to_regclass('public.idx_telematics_commands_dispatch') IS NULL
     OR to_regclass('public.idx_telemetry_store_forward_pending') IS NULL
     OR to_regclass('public.idx_telemetry_stream_ticket_expiry') IS NULL
     OR to_regclass('public.idx_telemetry_gateway_rejections_received') IS NULL THEN
    RAISE EXCEPTION 'Clean-chain pilot integrity indexes are missing';
  END IF;

  IF to_regclass('public.canonical_telemetry_events') IS NULL
     OR to_regclass('public.telematics_device_trust_policy') IS NULL
     OR to_regclass('public.telemetry_replay_seen') IS NULL
     OR to_regclass('public.telemetry_projection_inbox') IS NULL THEN
    RAISE EXCEPTION 'Clean-chain Telematics fabric persistence contract is incomplete';
  END IF;

  IF EXISTS (
    SELECT 1 FROM (VALUES
      ('hos_logs','notes'),
      ('hos_clocks','break_needed_at'),
      ('hos_clocks','reset_at'),
      ('hos_clocks','updated_at')
    ) required(table_name,column_name)
    WHERE NOT EXISTS (
      SELECT 1 FROM information_schema.columns c
      WHERE c.table_schema='public' AND c.table_name=required.table_name
        AND c.column_name=required.column_name)
  ) THEN
    RAISE EXCEPTION 'Clean-chain HOS pilot legacy columns are missing';
  END IF;

  IF NOT EXISTS (
    SELECT 1 FROM information_schema.columns
    WHERE table_schema='public' AND table_name='companies'
      AND column_name='entitlement_policy_mode'
      AND is_nullable='NO'
      AND column_default LIKE '%legacy_allow%'
  ) OR NOT EXISTS (
    SELECT 1 FROM pg_constraint
    WHERE conname='ck_companies_entitlement_policy_mode'
      AND conrelid='companies'::regclass
      AND convalidated
  ) OR EXISTS (
    SELECT 1 FROM companies
    WHERE entitlement_policy_mode NOT IN ('legacy_allow','package_allowlist')
  ) THEN
    RAISE EXCEPTION 'Clean-chain tenant entitlement policy schema/default/constraint contract is incomplete';
  END IF;
  BEGIN
    UPDATE companies SET entitlement_policy_mode='open'
    WHERE id=(SELECT min(id) FROM companies);
    RAISE EXCEPTION 'Stage68 weakened constraint accepted open policy mode';
  EXCEPTION WHEN check_violation THEN
    NULL;
  END;

  IF NOT EXISTS (
    SELECT 1 FROM information_schema.columns
    WHERE table_schema='public' AND table_name='tenant_market_packs'
      AND column_name='status' AND is_nullable='NO'
      AND column_default LIKE '%active%'
  ) OR NOT EXISTS (
    SELECT 1 FROM pg_constraint
    WHERE conname='ck_tenant_market_packs_status'
      AND conrelid='tenant_market_packs'::regclass
      AND contype='c' AND convalidated
  ) OR EXISTS (
    SELECT 1 FROM tenant_market_packs WHERE status NOT IN ('active','disabled')
  ) THEN
    RAISE EXCEPTION 'Clean-chain market-pack commercial status contract is incomplete';
  END IF;
  BEGIN
    INSERT INTO tenant_market_packs(company_id,pack_code,status)
    VALUES ((SELECT min(id) FROM companies),'__stage69_constraint_probe__','trial');
    RAISE EXCEPTION 'Stage69 weakened constraint accepted trial status';
  EXCEPTION WHEN check_violation THEN
    NULL;
  END;

  IF to_regclass('public.demo_fixture_versions') IS NULL
     OR NOT has_table_privilege('opstrax_app','demo_fixture_versions','SELECT')
     OR has_table_privilege('opstrax_app','demo_fixture_versions','INSERT')
     OR has_table_privilege('opstrax_app','demo_fixture_versions','UPDATE')
     OR has_table_privilege('opstrax_app','demo_fixture_versions','DELETE')
     OR NOT has_table_privilege('opstrax_system','demo_fixture_versions','SELECT')
     OR NOT has_table_privilege('opstrax_system','demo_fixture_versions','INSERT')
     OR NOT has_table_privilege('opstrax_system','demo_fixture_versions','UPDATE')
     OR NOT has_table_privilege('opstrax_system','demo_fixture_versions','DELETE') THEN
    RAISE EXCEPTION 'Clean-chain fixture provenance grants are unsafe or incomplete';
  END IF;

  IF NOT has_table_privilege('opstrax_app','telemetry_stream_ticket_nonces','INSERT')
     OR has_table_privilege('opstrax_app','telemetry_stream_ticket_nonces','SELECT')
     OR has_table_privilege('opstrax_app','telemetry_stream_ticket_nonces','UPDATE')
     OR has_table_privilege('opstrax_app','telemetry_stream_ticket_nonces','DELETE,TRUNCATE,REFERENCES,TRIGGER')
     OR NOT has_table_privilege('opstrax_system','telemetry_stream_ticket_nonces','SELECT')
     OR NOT has_table_privilege('opstrax_system','telemetry_stream_ticket_nonces','INSERT')
     OR NOT has_table_privilege('opstrax_system','telemetry_stream_ticket_nonces','UPDATE')
     OR NOT has_table_privilege('opstrax_system','telemetry_stream_ticket_nonces','DELETE')
     OR has_table_privilege('opstrax_system','telemetry_stream_ticket_nonces','TRUNCATE,REFERENCES,TRIGGER')
     OR NOT has_table_privilege('opstrax_system','telemetry_store_forward','SELECT')
     OR NOT has_table_privilege('opstrax_system','telemetry_store_forward','INSERT')
     OR NOT has_table_privilege('opstrax_system','telemetry_store_forward','UPDATE')
     OR NOT has_table_privilege('opstrax_system','telemetry_store_forward','DELETE')
     OR NOT has_table_privilege('opstrax_system','telemetry_gateway_rejections','SELECT')
     OR NOT has_table_privilege('opstrax_system','telemetry_gateway_rejections','INSERT')
     OR NOT has_table_privilege('opstrax_system','telemetry_gateway_rejections','DELETE') THEN
    RAISE EXCEPTION 'Clean-chain Telematics infrastructure-ledger grants are unsafe or incomplete';
  END IF;

  IF to_regclass('public.telemetry_stream_ticket_nonces_id_seq') IS NULL
     OR NOT EXISTS (
       SELECT 1 FROM information_schema.columns
       WHERE table_schema='public' AND table_name='telemetry_stream_ticket_nonces'
         AND column_name='id' AND is_nullable='NO'
         AND column_default LIKE 'nextval(%telemetry_stream_ticket_nonces_id_seq%'
     )
     OR (SELECT count(*) FROM telemetry_stream_ticket_nonces
         WHERE nonce_hash IN (repeat('a',64),repeat('b',64)) AND id IS NOT NULL)<>2
     OR NOT has_sequence_privilege('opstrax_app','telemetry_stream_ticket_nonces_id_seq','USAGE')
     OR NOT has_sequence_privilege('opstrax_system','telemetry_stream_ticket_nonces_id_seq','USAGE')
     OR has_sequence_privilege('opstrax_app','telemetry_stream_ticket_nonces_id_seq','SELECT,UPDATE')
     OR has_sequence_privilege('opstrax_system','telemetry_stream_ticket_nonces_id_seq','SELECT,UPDATE')
     OR has_sequence_privilege('public','telemetry_stream_ticket_nonces_id_seq','USAGE') THEN
    RAISE EXCEPTION 'Stage66 did not safely reconcile the legacy stream-ticket nonce identity';
  END IF;

  IF has_table_privilege('opstrax_app','eld_devices','SELECT')
     OR has_column_privilege('opstrax_app','eld_devices','api_key_hash','SELECT')
     OR has_column_privilege('opstrax_app','eld_devices','api_key_previous_hash','SELECT')
     OR has_column_privilege('opstrax_app','eld_devices','hmac_secret','SELECT')
     OR has_column_privilege('opstrax_app','eld_devices','hmac_secret_encrypted','SELECT')
     OR has_column_privilege('opstrax_app','eld_devices','hmac_previous_secret_encrypted','SELECT')
     OR NOT has_column_privilege('opstrax_app','eld_devices','device_serial','SELECT')
     OR NOT has_column_privilege('opstrax_app','eld_devices','malfunction_resolved_at','UPDATE')
     OR NOT has_column_privilege('opstrax_app','eld_devices','malfunction_resolved_by','UPDATE')
     OR NOT has_column_privilege('opstrax_app','eld_devices','resolution_evidence','UPDATE')
     OR NOT has_column_privilege('opstrax_app','eld_devices','row_version','UPDATE')
     OR has_column_privilege('opstrax_app','eld_devices','company_id','UPDATE')
     OR has_column_privilege('opstrax_app','eld_devices','id','UPDATE')
     OR has_column_privilege('opstrax_app','eld_devices','api_key_hash','UPDATE')
     OR has_column_privilege('opstrax_app','eld_devices','hmac_secret_encrypted','UPDATE') THEN
    RAISE EXCEPTION 'Clean-chain device credential column grants are unsafe or incomplete';
  END IF;

  IF EXISTS (
    SELECT 1
    FROM (VALUES
      ('opstrax_app','eld_devices','SELECT',FALSE),
      ('opstrax_app','eld_devices','INSERT',FALSE),
      ('opstrax_app','eld_devices','UPDATE',FALSE),
      ('opstrax_app','eld_devices','DELETE',FALSE),
      ('opstrax_app','telemetry_gateways','SELECT',FALSE),
      ('opstrax_app','telemetry_gateways','INSERT',FALSE),
      ('opstrax_app','telemetry_gateways','UPDATE',FALSE),
      ('opstrax_app','telemetry_gateways','DELETE',FALSE),
      ('opstrax_app','telemetry_stream_ticket_nonces','SELECT',FALSE),
      ('opstrax_app','telemetry_stream_ticket_nonces','INSERT',TRUE),
      ('opstrax_app','telemetry_stream_ticket_nonces','UPDATE',FALSE),
      ('opstrax_app','telemetry_stream_ticket_nonces','DELETE',FALSE),
      ('opstrax_app','telemetry_replay_seen','SELECT',FALSE),
      ('opstrax_app','telemetry_replay_seen','INSERT',FALSE),
      ('opstrax_app','telemetry_replay_seen','UPDATE',FALSE),
      ('opstrax_app','telemetry_replay_seen','DELETE',FALSE),
      ('opstrax_app','telemetry_replay_device_state','SELECT',FALSE),
      ('opstrax_app','telemetry_replay_device_state','INSERT',FALSE),
      ('opstrax_app','telemetry_replay_device_state','UPDATE',FALSE),
      ('opstrax_app','telemetry_replay_device_state','DELETE',FALSE),
      ('opstrax_app','canonical_telemetry_events','SELECT',FALSE),
      ('opstrax_app','canonical_telemetry_events','INSERT',FALSE),
      ('opstrax_app','canonical_telemetry_events','UPDATE',FALSE),
      ('opstrax_app','canonical_telemetry_events','DELETE',FALSE),
      ('opstrax_system','telemetry_nonces','SELECT',TRUE),
      ('opstrax_system','telemetry_nonces','INSERT',TRUE),
      ('opstrax_system','telemetry_nonces','UPDATE',FALSE),
      ('opstrax_system','telemetry_nonces','DELETE',TRUE),
      ('opstrax_system','gps_gateway_replay','SELECT',TRUE),
      ('opstrax_system','gps_gateway_replay','INSERT',TRUE),
      ('opstrax_system','gps_gateway_replay','UPDATE',FALSE),
      ('opstrax_system','gps_gateway_replay','DELETE',TRUE),
      ('opstrax_system','telemetry_gateway_rejections','SELECT',TRUE),
      ('opstrax_system','telemetry_gateway_rejections','INSERT',TRUE),
      ('opstrax_system','telemetry_gateway_rejections','UPDATE',FALSE),
      ('opstrax_system','telemetry_gateway_rejections','DELETE',TRUE),
      ('opstrax_system','telemetry_replay_seen','SELECT',TRUE),
      ('opstrax_system','telemetry_replay_seen','INSERT',TRUE),
      ('opstrax_system','telemetry_replay_seen','UPDATE',FALSE),
      ('opstrax_system','telemetry_replay_seen','DELETE',TRUE),
      ('opstrax_system','telemetry_replay_device_state','SELECT',TRUE),
      ('opstrax_system','telemetry_replay_device_state','INSERT',TRUE),
      ('opstrax_system','telemetry_replay_device_state','UPDATE',TRUE),
      ('opstrax_system','telemetry_replay_device_state','DELETE',FALSE),
      ('opstrax_system','telemetry_projection_inbox','SELECT',TRUE),
      ('opstrax_system','telemetry_projection_inbox','INSERT',TRUE),
      ('opstrax_system','telemetry_projection_inbox','UPDATE',FALSE),
      ('opstrax_system','telemetry_projection_inbox','DELETE',TRUE),
      ('opstrax_system','canonical_telemetry_events','SELECT',TRUE),
      ('opstrax_system','canonical_telemetry_events','INSERT',TRUE),
      ('opstrax_system','canonical_telemetry_events','UPDATE',FALSE),
      ('opstrax_system','canonical_telemetry_events','DELETE',TRUE)
    ) expected(role_name,table_name,privilege_name,allowed)
    WHERE has_table_privilege(
      expected.role_name::name,
      'public.'||expected.table_name,
      expected.privilege_name
    )<>expected.allowed
  ) OR has_table_privilege('opstrax_app','telemetry_gateways','SELECT,INSERT,UPDATE,DELETE')
     OR has_any_column_privilege('opstrax_app','telemetry_replay_seen','SELECT,INSERT,UPDATE,REFERENCES')
     OR has_any_column_privilege('opstrax_app','telemetry_replay_device_state','SELECT,INSERT,UPDATE,REFERENCES')
     OR has_any_column_privilege('opstrax_app','canonical_telemetry_events','SELECT,INSERT,UPDATE,REFERENCES')
     OR NOT has_column_privilege('opstrax_app','telemetry_gateways','gateway_id','SELECT')
     OR has_column_privilege('opstrax_app','telemetry_gateways','secret_encrypted','SELECT')
     OR NOT has_column_privilege('opstrax_app','telemetry_gateways','status','UPDATE')
     OR has_column_privilege('opstrax_app','telemetry_gateways','secret_encrypted','UPDATE') THEN
    RAISE EXCEPTION 'Clean-chain Stage76 exact telemetry runtime ACL matrix is unsafe';
  END IF;

  IF to_regclass('public.telemetry_replay_device_state') IS NULL
     OR NOT EXISTS (
       SELECT 1 FROM pg_constraint
       WHERE conname='uq_telemetry_replay_seen_unwrapped'
         AND conrelid='public.telemetry_replay_seen'::regclass
         AND contype='u'
         AND pg_get_constraintdef(oid)='UNIQUE (device_id, unwrapped_serial, content_hash)'
     ) OR EXISTS (
       SELECT 1 FROM (VALUES
         ('telemetry_replay_seen','unwrapped_serial'),
         ('telemetry_replay_seen','event_id'),
         ('telemetry_replay_device_state','device_id'),
         ('telemetry_replay_device_state','last_raw_serial'),
         ('telemetry_replay_device_state','high_water_unwrapped'),
         ('telemetry_replay_device_state','updated_at')
       ) required(table_name,column_name)
       WHERE NOT EXISTS (
         SELECT 1 FROM information_schema.columns actual
         WHERE actual.table_schema='public'
           AND actual.table_name=required.table_name
           AND actual.column_name=required.column_name
           AND actual.is_nullable='NO'
       )
     ) OR EXISTS (SELECT 1 FROM telemetry_replay_device_state)
     OR has_sequence_privilege('opstrax_app','telemetry_replay_seen_id_seq','USAGE,SELECT,UPDATE')
     OR NOT has_sequence_privilege('opstrax_system','telemetry_replay_seen_id_seq','USAGE')
     OR NOT has_sequence_privilege('opstrax_system','telemetry_replay_seen_id_seq','SELECT')
     OR has_sequence_privilege('opstrax_system','telemetry_replay_seen_id_seq','UPDATE')
     OR has_sequence_privilege('opstrax_app','canonical_telemetry_events_id_seq','USAGE,SELECT,UPDATE')
     OR NOT has_sequence_privilege('opstrax_system','canonical_telemetry_events_id_seq','USAGE')
     OR NOT has_sequence_privilege('opstrax_system','canonical_telemetry_events_id_seq','SELECT')
     OR has_sequence_privilege('opstrax_system','canonical_telemetry_events_id_seq','UPDATE') THEN
    RAISE EXCEPTION 'Clean-chain Stage76 replay generation schema/sequence boundary is unsafe';
  END IF;

  IF to_regprocedure('public.stage65_hos_day_lock_key(bigint,bigint,date,bigint)') IS NULL
  OR EXISTS (
    SELECT 1 FROM (VALUES
      ('stage65_lock_hos_day_on_insert'),
      ('stage65_lock_hos_days_on_identity_change'),
      ('stage65_invalidate_hos_days_on_identity_change'),
      ('stage65_invalidate_hos_certification_on_material_change'),
      ('stage65_invalidate_hos_certified_day'),
      ('stage65_invalidate_hos_day_on_insert'),
      ('stage65_prevent_certified_hos_log_delete'),
      ('stage65_guard_hos_certification_snapshot')) required(function_name)
    WHERE to_regprocedure('public.'||required.function_name||'()') IS NULL
  ) OR EXISTS (
    SELECT 1 FROM (VALUES
      ('hos_logs','trg_stage65_hos_insert_day_lock'),
      ('hos_logs','trg_stage65_hos_identity_day_lock'),
      ('hos_logs','trg_stage65_hos_identity_change_invalidates_days'),
      ('hos_logs','trg_stage65_hos_material_change_invalidates_certification'),
      ('hos_logs','trg_stage65_hos_segment_change_invalidates_day'),
      ('hos_logs','trg_stage65_hos_insert_invalidates_certified_day'),
      ('hos_logs','trg_stage65_prevent_certified_hos_log_delete'),
      ('hos_certifications','trg_stage65_hos_certification_snapshot_immutable')) required(table_name,trigger_name)
    WHERE NOT EXISTS (
      SELECT 1 FROM pg_trigger t
      JOIN pg_class c ON c.oid=t.tgrelid
      JOIN pg_namespace n ON n.oid=c.relnamespace
      WHERE n.nspname='public' AND c.relname=required.table_name
        AND t.tgname=required.trigger_name AND NOT t.tgisinternal AND t.tgenabled<>'D')
  ) THEN
    RAISE EXCEPTION 'Clean-chain HOS certification functions or triggers are missing/disabled';
  END IF;

  SELECT array_agg(required.table_name ORDER BY required.table_name) INTO bad_tables
  FROM (VALUES
    ('dispatch_proofs'),('dispatch_proof_artifacts'),
    ('smart_assignment_recommendations'),('assignment_confirmations'),
    ('site_access_requirements'),('access_documents'),('pickup_authorizations'),
    ('warehouse_handovers'),('proof_packages'),('proof_artifacts'),
    ('billing_confidence_records'),
    ('incidents'),('incident_evidence'),('insurance_reports'),
    ('coaching_tasks'),('coaching_notes'),('driver_safety_scorecards'),
    ('dvir_reports'),('dvir_defects'),('hos_logs'),('hos_clocks'),
    ('compliance_violations'),('eld_devices'),('eld_malfunction_history'),
    ('hos_certifications'),('fault_codes'),('fault_occurrences'),('device_state_transitions'),
    ('device_installations'),('device_installation_evidence'),
    ('device_channel_health'),('telematics_device_commands'),
    ('telemetry_privacy_policies'),('demo_fixture_versions')) required(table_name)
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

echo "Predeploy clean-chain regression passed (fresh Stage19/20/22 baseline + Stage76-terminal runner replays + policy/ACL checks)."
