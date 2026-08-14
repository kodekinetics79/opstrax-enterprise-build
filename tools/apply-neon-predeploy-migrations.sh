#!/usr/bin/env bash
# ─────────────────────────────────────────────────────────────────────────────
# Pre-deploy migration runner for the DEPLOYED (Neon) database.
#
# WHY THIS EXISTS
#   The code currently on origin/main (deployed to Render) predates several
#   schema changes that the new code REQUIRES at request time — most critically
#   users.customer_id and users.branch_id, which the auth middleware selects on
#   EVERY authenticated request. Deploying the new backend before these columns
#   exist would 500 every tenant API call. Run this against Neon FIRST, then
#   merge/deploy.
#
# WHAT IT APPLIES
#   Idempotent schema migrations followed by a terminal security cutover. Stage58
#   intentionally revokes stale privileges and replaces RLS policies atomically;
#   its ownership/provenance preflights fail and roll back the whole migration.
#   stage21  customer portal      (users.customer_id, portal tables)
#   stage23  schema_migrations ledger
#   stage24  compliance tenant scope (company_id columns + backfill)
#   stage25  branches org hierarchy  (branches table, users.branch_id, …)
#   stage26  platform control plane  (platform tables as migration)
#   stage32  device IMEI identifier and ambiguity preflight
#   2026-07-30 customer feedback contract (portal service columns + index)
#   stage49  durable one-time MFA challenge consumption
#   stage50  complete Fleet/market-pack production schema + RLS contract
#   stage51  production runtime support for observability/escalation/report workers
#   stage52  Fleet vehicle/driver identity uniqueness under concurrent writes
#   stage53  production-wide tenant RLS/policy/grant reconciliation
#   stage54  cold-chain device normalized identity/idempotency integrity
#   stage55  restricted-runtime Fleet route schema contract
#   stage56  returnable-asset type normalized identity integrity
#   stage57  workforce schedule tenant/driver ownership + RLS integrity
#   stage47  detention recovery owner schema required by immutable offboarding
#   stage58  terminal non-forgeable tenant ticket + dual runtime identities
#   stage59  final system-only shared Data Protection key repository
#   stage60  Dispatch + Trips customer-pilot integrity contract
#   stage61  Operations Proof Center production contract
#   stage62  Last Mile pilot integrity contract
#   stage63  Route Plans pilot integrity contract
#   stage64  Shipments pilot read-path performance contract
#   stage65  Safety pilot integrity contract
#   stage66  Telematics trust, lifecycle, health and diagnostics contract
#   stage67  Canonical diagnostics integrity and machine safety holds
#   stage68  Explicit tenant commercial entitlement policy mode
#   stage69  Market-pack commercial status and audit-control hardening
#   stage70  HOS pilot legacy-schema reconciliation
#   stage71  Coaching acknowledgement/completion evidence reconciliation
#   stage72  Dual-gated immutable-evidence offboarding reconciliation
#   stage73  Null-safe fail-closed HOS offboarding reconciliation
#   stage74  Production retention-policy schema contract
#   stage75  Bounded support-access control plane
#   stage76  FINAL telemetry default-deny/exact runtime ACL reconciliation
#   stage77  Protected-environment authorization reference bootstrap
#   stage78  Protected-environment country-profile runtime contract
#   stage79  Protected-environment tenant-provisioning runtime contract
#   stage80  Effective-dated fleet identity backbone
#
# WHAT IT DELIBERATELY SKIPS
#   stage19/20/22 (the broad Row-Level Security cutover). Stage49 itself is
#   FORCE-RLS because the restricted production runtime requires its replay
#   ledger. This runner therefore fails closed unless stage20 has already
#   provisioned opstrax_app; it does not create or password that role here.
#
# USAGE (run from repo root; credentials never leave your shell):
#   export NEON_PG_URI='postgresql://USER:PASSWORD@HOST/DB?sslmode=require'
#   ./tools/apply-neon-predeploy-migrations.sh
#
# The URI is read from the environment only — never passed as an argument
# (arguments show up in `ps` output and shell history).
# ─────────────────────────────────────────────────────────────────────────────
set -euo pipefail

if [ -z "${NEON_PG_URI:-}" ]; then
  echo "ERROR: set NEON_PG_URI in your environment first (see header)." >&2
  exit 1
fi
command -v psql >/dev/null || { echo "ERROR: psql not found. brew install libpq (or run via docker exec)." >&2; exit 1; }

MIGRATIONS=(
  # The dated migrations are additive overlays. A genuinely empty Neon database
  # must first receive the canonical 001 predecessor that owns core tables such
  # as jobs, trips, users, vehicles and eld_devices. Without this bootstrap the
  # old runner reached Stage 6 and failed at ALTER TABLE jobs.
  ../init/001_schema
  # A clean protected database never runs owner-capable runtime schema services.
  # Package the complete pre-RLS foundation explicitly before the security cutover.
  2026_06_27_stage5_p0b1a_foundation
  2026_06_28_stage5b_p0b1a2_persistence_hardening
  2026_06_28_stage5d_p0b1a3_dispatcher
  2026_06_28_stage6_p0b1b_business_spine
  2026_06_28_stage7a_revenue_readiness_schema_contract
  2026_06_28_stage8_finance_activation
  2026_06_28_stage13b_safety_maintenance_foundation
  2026_06_29_stage18_commercial_foundation
  # Stage 20 MUST run first: it creates the restricted opstrax_app role that Stage 50's
  # preflight hard-requires ("Stage 50 requires the restricted opstrax_app role"). It was
  # missing from this list, so a database that never had the role could not be migrated
  # past Stage 50. Additive + idempotent: the FORCE-RLS loop only touches tables that
  # already have rowsecurity=true, so re-running it changes nothing.
  2026_06_30_stage20_rls_force_and_app_role
  2026_06_30_stage21_customer_portal
  2026_07_02_stage23_schema_migration_ledger
  2026_07_02_stage24_compliance_tenant_scope
  2026_07_02_stage25_branches_org_hierarchy
  2026_07_02_stage26_platform_control_plane
  2026_07_11_stage32_device_imei
  # Required owner schema for per-gateway credentials. Stage76 intentionally fails closed when
  # this table is absent; do not rely on the runtime EnsureAsync path to materialize Production.
  2026_07_16_stage42_telemetry_gateways
  2026_07_30_customer_feedback_contract
  2026_07_30_stage49_mfa_challenge_one_time
  2026_07_30_stage50_fleet_production_contract
  2026_07_30_stage51_production_runtime_support
  # Stage12 enriches telemetry tables whose owner-safe definitions are installed
  # by Stage51 on the supported protected predecessor.
  2026_06_28_stage12a_telemetry_live_state
  2026_07_22_stage47_detention_recovery
  2026_07_30_stage52_fleet_identity_uniqueness
  2026_07_30_stage53_tenant_rls_reconciliation
  2026_07_30_stage54_cold_chain_device_integrity
  2026_07_30_stage55_fleet_runtime_route_contract
  2026_07_30_stage56_asset_type_integrity
  2026_07_30_stage57_workforce_schedule_tenant_integrity
  2026_08_01_stage60_dispatch_trip_pilot
  2026_08_01_stage61_operations_proof_center
  2026_08_01_stage62_last_mile_pilot
  2026_08_01_stage63_route_plans_pilot
  2026_08_01_stage64_shipments_pilot
  2026_08_01_stage65_safety_pilot
  telematics/001_latest_position_provenance
  telematics/002_device_lifecycle
  telematics/003_canonical_telemetry_timeseries
  telematics/004_device_trust_policy
  telematics/005_replay_guard
  telematics/006_projection_inbox
  2026_08_02_stage66_telematics_pilot
  2026_08_02_stage67_telematics_diagnostics_integrity
  2026_08_02_stage68_entitlement_policy_mode
  2026_08_02_stage69_market_pack_control_hardening
  2026_08_02_stage70_hos_pilot_schema_reconciliation
  2026_08_02_stage71_coaching_evidence_reconciliation
  2026_08_02_stage72_hos_offboarding_immutability_reconciliation
  2026_08_02_stage73_hos_offboarding_null_fail_closed
  2026_08_02_stage74_retention_policy_production_contract
  2026_08_02_stage75_bounded_support_access
  2026_08_12_stage77_protected_role_bootstrap
  2026_08_13_stage78_country_profiles_runtime_contract
  2026_08_13_stage79_tenant_provisioning_runtime_contract
  2026_08_14_stage80_fleet_identity_backbone
)

echo "Target host: $(printf '%s' "$NEON_PG_URI" | sed -E 's|.*@([^/:?]+).*|\1|')"
echo "Pre-check: read-only connectivity…"
psql "$NEON_PG_URI" -tA -c "SELECT current_database(), version()" | head -1
stage58_already_applied=$(psql "$NEON_PG_URI" -tA -c "SELECT CASE WHEN to_regclass('public.schema_migrations') IS NOT NULL THEN (SELECT COUNT(*) FROM schema_migrations WHERE version='2026_07_31_stage58_nonforgeable_tenant_ticket') ELSE 0 END" 2>/dev/null || echo 0)

for m in "${MIGRATIONS[@]}"; do
  f="database/migrations/$m.sql"
  ledger_version="$m"
  if [ "$m" = "../init/001_schema" ]; then
    ledger_version="database_init_001_schema"
  fi
  case "$m" in
    telematics/*) ledger_version="telematics_$(basename "$m")" ;;
  esac
  [ -f "$f" ] || { echo "ERROR: missing $f (run from repo root)" >&2; exit 1; }
  # Once Stage58 is live, never replay a historical migration that recreates
  # forgeable GUC policies. Stage58 supersedes and repairs their security surface.
  if [ "$stage58_already_applied" = "1" ] && [[ "$m" =~ ^2026_07_30_stage53_ ]]; then
    echo "── $m: security policy superseded by Stage58 — skipping legacy replay"
    continue
  fi
  # Skip if already registered in the ledger (ledger may not exist before stage23 — treat as not applied).
  applied=$(psql "$NEON_PG_URI" -tA -c "SELECT COUNT(*) FROM schema_migrations WHERE version='$ledger_version'" 2>/dev/null || echo 0)
  # Some established environments and production-shaped tests predate Stage23's
  # ledger but already contain the complete canonical predecessor. Replaying
  # 001_schema.sql there is unsafe because its two circular foreign keys are
  # intentionally one-time bootstrap statements. Recognize the materialized
  # predecessor from both ends of the file plus those exact constraints; Stage23
  # then records database_init_001_schema in the authoritative ledger. A genuinely
  # empty Neon database fails this probe and still receives 001_schema.sql.
  if [ "$m" = "../init/001_schema" ] && [ "$applied" != "1" ]; then
    predecessor_materialized=$(psql "$NEON_PG_URI" -tA -v ON_ERROR_STOP=1 -c "
      SELECT CASE WHEN
        to_regclass('public.companies') IS NOT NULL
        AND to_regclass('public.drivers') IS NOT NULL
        AND to_regclass('public.vehicles') IS NOT NULL
        AND to_regclass('public.jobs') IS NOT NULL
        AND to_regclass('public.eld_devices') IS NOT NULL
        AND to_regclass('public.file_storage_metadata') IS NOT NULL
        AND EXISTS (
          SELECT 1 FROM pg_constraint
          WHERE conrelid=to_regclass('public.drivers') AND conname='fk_drivers_vehicle'
        )
        AND EXISTS (
          SELECT 1 FROM pg_constraint
          WHERE conrelid=to_regclass('public.vehicles') AND conname='fk_vehicles_driver'
        )
      THEN 1 ELSE 0 END")
    if [ "$predecessor_materialized" = "1" ]; then
      echo "── $m: canonical predecessor already materialized — Stage23 will ledger it"
      continue
    fi
  fi
  repair_migration=false
  case "$m" in
    2026_06_27_stage5_p0b1a_foundation|\
    2026_06_28_stage5b_p0b1a2_persistence_hardening|\
    2026_06_28_stage5d_p0b1a3_dispatcher|\
    2026_06_28_stage6_p0b1b_business_spine|\
    2026_06_28_stage7a_revenue_readiness_schema_contract|\
    2026_06_28_stage8_finance_activation|\
    2026_06_28_stage12a_telemetry_live_state|\
    2026_06_28_stage13b_safety_maintenance_foundation|\
    2026_06_29_stage18_commercial_foundation|\
    2026_07_11_stage32_device_imei|\
    2026_07_16_stage42_telemetry_gateways|\
    2026_07_30_stage53_tenant_rls_reconciliation|\
    2026_07_30_stage54_cold_chain_device_integrity|\
    2026_07_30_stage55_fleet_runtime_route_contract|\
    2026_07_30_stage56_asset_type_integrity|\
    2026_07_30_stage57_workforce_schedule_tenant_integrity|\
    2026_07_22_stage47_detention_recovery|\
    2026_08_01_stage60_dispatch_trip_pilot|\
    2026_08_02_stage67_telematics_diagnostics_integrity|\
    2026_08_02_stage68_entitlement_policy_mode|\
    2026_08_02_stage69_market_pack_control_hardening|\
    2026_08_02_stage70_hos_pilot_schema_reconciliation|\
    2026_08_02_stage71_coaching_evidence_reconciliation|\
    2026_08_02_stage72_hos_offboarding_immutability_reconciliation|\
    2026_08_02_stage73_hos_offboarding_null_fail_closed|\
    2026_08_02_stage74_retention_policy_production_contract|\
    2026_08_02_stage75_bounded_support_access|\
    2026_08_12_stage77_protected_role_bootstrap|\
    2026_08_13_stage78_country_profiles_runtime_contract|\
    2026_08_13_stage79_tenant_provisioning_runtime_contract|\
    2026_08_14_stage80_fleet_identity_backbone) repair_migration=true ;;
  esac
  if [ "$applied" = "1" ] && [ "$repair_migration" = false ]; then
    echo "── $m: already applied (ledger) — skipping"
    continue
  fi
  if [ "$applied" = "1" ]; then
    echo "── $m: ledgered reconciliation — reapplying to repair drift"
  else
    echo "── applying $m"
  fi
  psql "$NEON_PG_URI" -v ON_ERROR_STOP=1 -q -f "$f"
  # stage21 precedes the ledger; later migrations must register successfully so a
  # failed bookkeeping write cannot masquerade as a complete deploy on the next run.
  if [ "$(psql "$NEON_PG_URI" -tA -c "SELECT to_regclass('public.schema_migrations') IS NOT NULL")" = "t" ]; then
    psql "$NEON_PG_URI" -v ON_ERROR_STOP=1 -q -c "INSERT INTO schema_migrations (version, description) VALUES ('$ledger_version', 'applied by apply-neon-predeploy-migrations.sh') ON CONFLICT (version) DO NOTHING"
  fi
done

# Required owner schemas and pilot-wave migrations are release gates, not optional seed packs.
# Verify their ledgers and critical objects before the terminal Stage58 reconciliation.
psql "$NEON_PG_URI" -v ON_ERROR_STOP=1 <<'SQL'
DO $verify_pilot_wave$
BEGIN
  IF EXISTS (
    SELECT 1 FROM (VALUES
      ('2026_06_27_stage5_p0b1a_foundation'),
      ('2026_06_28_stage5b_p0b1a2_persistence_hardening'),
      ('2026_06_28_stage5d_p0b1a3_dispatcher'),
      ('2026_06_28_stage6_p0b1b_business_spine'),
      ('2026_06_28_stage7a_revenue_readiness_schema_contract'),
      ('2026_06_28_stage8_finance_activation'),
      ('2026_06_28_stage12a_telemetry_live_state'),
      ('2026_06_28_stage13b_safety_maintenance_foundation'),
      ('2026_06_29_stage18_commercial_foundation'),
      ('2026_07_11_stage32_device_imei'),
      ('2026_07_16_stage42_telemetry_gateways'),
      ('2026_08_01_stage60_dispatch_trip_pilot'),
      ('2026_07_22_stage47_detention_recovery'),
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
      ('2026_08_13_stage78_country_profiles_runtime_contract'),
      ('2026_08_13_stage79_tenant_provisioning_runtime_contract'),
      ('2026_08_14_stage80_fleet_identity_backbone')) required(version)
    WHERE (SELECT count(*) FROM schema_migrations sm WHERE sm.version=required.version)<>1
  ) THEN RAISE EXCEPTION 'Required owner/pilot migration ledger missing or duplicated'; END IF;
  IF to_regclass('public.uq_ftms_dorders_company_number') IS NULL
     OR to_regclass('public.telemetry_gateways') IS NULL
     OR to_regclass('public.uq_ftms_route_progress_key') IS NULL
     OR to_regclass('public.uq_route_stops_company_route_sequence') IS NULL
     OR to_regclass('public.uq_routes_active_driver') IS NULL
     OR to_regclass('public.uq_routes_active_vehicle') IS NULL
     OR to_regclass('public.idx_jobs_active_projection') IS NULL
     OR to_regclass('public.idx_dispatch_assignments_job_projection_recent') IS NULL
     OR to_regclass('public.idx_proof_packages_company_job_recent') IS NULL
     OR to_regclass('public.idx_location_company_vehicle_recent') IS NULL
     OR to_regclass('public.idx_pod_company_job_projection_recent') IS NULL
     OR to_regclass('public.uq_fault_codes_canonical_projection') IS NULL
     OR to_regclass('public.uq_fault_occurrences_source_dtc') IS NULL
     OR to_regclass('public.uq_diagnostic_holds_active_fault') IS NULL
     OR to_regclass('public.uq_location_events_company_idempotency') IS NULL
     OR to_regclass('public.idx_telemetry_store_forward_pending') IS NULL
     OR to_regclass('public.idx_telemetry_stream_ticket_expiry') IS NULL
     OR to_regclass('public.idx_telemetry_gateway_rejections_received') IS NULL THEN
    RAISE EXCEPTION 'Required owner/pilot schema and concurrency contract is incomplete';
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
    RAISE EXCEPTION 'Stage77 protected authorization reference bootstrap is incomplete or unsafe';
  END IF;
  IF to_regclass('public.outbox_messages') IS NULL
     OR to_regclass('public.inbox_messages') IS NULL
     OR to_regclass('public.country_profiles') IS NULL
     OR EXISTS (
       SELECT 1 FROM (VALUES
         ('invite_token_hash'),('invite_expires_at'),('updated_at'),('mfa_secret')
       ) required(column_name)
       WHERE NOT EXISTS (
         SELECT 1 FROM information_schema.columns column_state
         WHERE column_state.table_schema='public'
           AND column_state.table_name='platform_admins'
           AND column_state.column_name=required.column_name
       )
     ) THEN
    RAISE EXCEPTION 'Protected runtime foundation schema is incomplete';
  END IF;
  IF (SELECT count(*) FROM country_profiles WHERE country_code IN ('US','CA','SA'))<>3
     OR NOT EXISTS (SELECT 1 FROM country_profiles WHERE country_code='US' AND default_currency='USD')
     OR NOT EXISTS (SELECT 1 FROM country_profiles WHERE country_code='SA' AND text_direction='rtl') THEN
    RAISE EXCEPTION 'Stage78 country-profile runtime catalog is incomplete';
  END IF;
  IF to_regclass('public.password_reset_tokens') IS NULL
     OR to_regclass('public.feature_flags') IS NULL
     OR to_regclass('public.ux_users_company_email_ci') IS NULL
     OR EXISTS (
       SELECT 1 FROM (VALUES
         ('companies','legal_name'),('companies','website'),('companies','fleet_size'),
         ('companies','tax_id'),('companies','primary_contact_name'),
         ('companies','primary_contact_email'),('companies','primary_contact_phone'),
         ('companies','billing_email'),('tenant_subscriptions','billing_cycle'),
         ('feature_flags','environment'),('password_reset_tokens','token_hash')
       ) required(table_name,column_name)
       WHERE NOT EXISTS (
         SELECT 1 FROM information_schema.columns actual
         WHERE actual.table_schema='public'
           AND actual.table_name=required.table_name
           AND actual.column_name=required.column_name
       )
     ) THEN
    RAISE EXCEPTION 'Stage79 tenant-provisioning runtime contract is incomplete';
  END IF;
  IF to_regclass('public.device_installation_quarantine') IS NULL
     OR to_regclass('public.ex_stage80_device_installation_period') IS NULL
     OR to_regclass('public.uq_stage80_vehicle_primary_role') IS NULL
     OR to_regprocedure('public.stage80_sync_device_vehicle_projection()') IS NULL
     OR to_regclass('public.module_packages') IS NULL
     OR to_regclass('public.usage_meters') IS NULL
     OR to_regclass('public.usage_events') IS NULL
     OR to_regclass('public.usage_counters') IS NULL
     OR to_regclass('public.pricing_rules') IS NULL
     OR to_regclass('public.tenant_contract_overrides') IS NULL
     OR EXISTS (
       SELECT 1 FROM (VALUES
         ('device_installations','effective_from'),('device_installations','effective_to'),
         ('device_installations','device_role'),('device_installations','row_version'),
         ('eld_devices','notes'),
         ('location_events','installation_id'),('location_events','assignment_id'),
         ('location_events','battery_voltage'),
         ('location_events','engine_status'),
         ('latest_vehicle_positions','address'),
         ('latest_vehicle_positions','battery_voltage'),
         ('latest_vehicle_positions','installation_id'),('latest_vehicle_positions','assignment_id'),
         ('fleet_tms_temperature_devices','last_reported_temperature_celsius'),
         ('fleet_tms_temperature_devices','battery_percent'),
         ('fleet_tms_temperature_devices','last_ping_at_utc'),
         ('fleet_tms_temperature_alerts','measured_humidity'),
         ('fleet_tms_temperature_alerts','humidity_threshold_min'),
         ('fleet_tms_temperature_alerts','humidity_threshold_max'),
         ('customers','sla_health_score'),('customers','delivery_experience_score'),
         ('customers','risk_score'),('customers','health_state'),('customers','health_computed_at'),
         ('module_packages','package_key'),('module_packages','module_keys'),
         ('module_packages','base_price_cents'),('usage_meters','meter_key'),
         ('usage_meters','period'),('usage_events','company_id'),
         ('usage_events','meter_key'),('usage_events','period_key'),
         ('usage_counters','company_id'),('usage_counters','meter_key'),
         ('usage_counters','period_key'),('pricing_rules','package_id'),
         ('pricing_rules','meter_key'),('tenant_contract_overrides','company_id'),
         ('tenant_contract_overrides','meter_key'),
         ('canonical_telemetry_events','installation_id'),('canonical_telemetry_events','assignment_id')
       ) required(table_name,column_name)
       WHERE NOT EXISTS (SELECT 1 FROM information_schema.columns actual
         WHERE actual.table_schema='public' AND actual.table_name=required.table_name
           AND actual.column_name=required.column_name)
  ) THEN
    RAISE EXCEPTION 'Stage80 fleet identity backbone contract is incomplete';
  END IF;
  IF (SELECT COUNT(*) FROM pg_roles WHERE rolname IN ('opstrax_app','opstrax_system'))=2 AND (
  EXISTS (
    SELECT 1 FROM (VALUES ('module_packages'),('usage_meters'),('pricing_rules')) refs(table_name)
    WHERE NOT has_table_privilege('opstrax_app',table_name,'SELECT')
       OR has_table_privilege('opstrax_app',table_name,'INSERT')
       OR has_table_privilege('opstrax_app',table_name,'UPDATE')
       OR has_table_privilege('opstrax_app',table_name,'DELETE')
       OR NOT has_table_privilege('opstrax_system',table_name,'SELECT')
       OR NOT has_table_privilege('opstrax_system',table_name,'INSERT')
       OR NOT has_table_privilege('opstrax_system',table_name,'UPDATE')
       OR NOT has_table_privilege('opstrax_system',table_name,'DELETE')
  ) OR EXISTS (
    SELECT 1 FROM (VALUES ('module_packages_id_seq'),('usage_meters_id_seq'),('pricing_rules_id_seq')) refs(sequence_name)
    WHERE has_sequence_privilege('opstrax_app',sequence_name,'USAGE')
       OR NOT has_sequence_privilege('opstrax_system',sequence_name,'USAGE')
  ) OR NOT COALESCE((SELECT c.relrowsecurity AND c.relforcerowsecurity
                       FROM pg_class c WHERE c.oid=to_regclass('public.usage_events')),false)
       OR NOT has_table_privilege('opstrax_app','usage_events','SELECT')
       OR NOT has_table_privilege('opstrax_app','usage_events','INSERT')
       OR has_table_privilege('opstrax_app','usage_events','UPDATE')
       OR has_table_privilege('opstrax_app','usage_events','DELETE')
       OR NOT has_table_privilege('opstrax_system','usage_events','SELECT')
       OR NOT has_table_privilege('opstrax_system','usage_events','INSERT')
       OR NOT has_table_privilege('opstrax_system','usage_events','UPDATE')
       OR NOT has_table_privilege('opstrax_system','usage_events','DELETE')
  OR EXISTS (
    SELECT 1 FROM (VALUES ('usage_counters'),('tenant_contract_overrides')) tenant_tables(table_name)
    JOIN pg_class c ON c.oid=to_regclass('public.'||tenant_tables.table_name)
    WHERE NOT c.relrowsecurity OR NOT c.relforcerowsecurity
       OR NOT has_table_privilege('opstrax_app',table_name,'SELECT')
       OR NOT has_table_privilege('opstrax_app',table_name,'INSERT')
       OR NOT has_table_privilege('opstrax_app',table_name,'UPDATE')
       OR NOT has_table_privilege('opstrax_app',table_name,'DELETE')
       OR NOT has_table_privilege('opstrax_system',table_name,'SELECT')
       OR NOT has_table_privilege('opstrax_system',table_name,'INSERT')
       OR NOT has_table_privilege('opstrax_system',table_name,'UPDATE')
       OR NOT has_table_privilege('opstrax_system',table_name,'DELETE')
  -- Stage58 is intentionally applied after this owner-integrity checkpoint. Stage80
  -- must establish the three control-plane policies itself; the terminal check below
  -- separately requires all six policies after Stage58 adds tenant_ticket_app.
  ) OR (SELECT COUNT(*) FROM pg_policies WHERE schemaname='public'
          AND tablename IN ('usage_events','usage_counters','tenant_contract_overrides')
          AND policyname='system_control_plane')<>3) THEN
    RAISE EXCEPTION 'Stage80 revenue/market catalog ACL and tenant-RLS contract is incomplete';
  END IF;
  IF NOT EXISTS (
    SELECT 1 FROM information_schema.columns
    WHERE table_schema='public' AND table_name='companies'
      AND column_name='entitlement_policy_mode' AND is_nullable='NO'
  ) OR EXISTS (
    SELECT 1 FROM companies
    WHERE entitlement_policy_mode NOT IN ('legacy_allow','package_allowlist')
  ) THEN
    RAISE EXCEPTION 'Tenant entitlement policy contract is incomplete';
  END IF;
  IF NOT EXISTS (
    SELECT 1 FROM information_schema.columns
    WHERE table_schema='public' AND table_name='tenant_market_packs'
      AND column_name='status' AND is_nullable='NO'
  ) OR NOT EXISTS (
    SELECT 1 FROM pg_constraint
    WHERE conname='ck_tenant_market_packs_status'
      AND conrelid='tenant_market_packs'::regclass
      AND contype='c' AND convalidated
  ) OR EXISTS (
    SELECT 1 FROM tenant_market_packs WHERE status NOT IN ('active','disabled')
  ) THEN
    RAISE EXCEPTION 'Market-pack commercial status contract is incomplete';
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
    RAISE EXCEPTION 'HOS pilot legacy-schema reconciliation is incomplete';
  END IF;
  IF has_column_privilege('opstrax_app','eld_devices','api_key_hash','SELECT')
     OR has_column_privilege('opstrax_app','eld_devices','api_key_previous_hash','SELECT')
     OR has_column_privilege('opstrax_app','eld_devices','hmac_secret','SELECT')
     OR has_column_privilege('opstrax_app','eld_devices','hmac_secret_encrypted','SELECT')
     OR has_column_privilege('opstrax_app','eld_devices','hmac_previous_secret_encrypted','SELECT')
     OR NOT has_column_privilege('opstrax_app','eld_devices','device_serial','SELECT') THEN
    RAISE EXCEPTION 'Device credential column grants are unsafe or incomplete';
  END IF;
  IF position('COALESCE(current_setting(''opstrax.offboarding''' in
       pg_get_functiondef('stage65_prevent_certified_hos_log_delete()'::regprocedure))=0
     OR position('opstrax_system' in
       pg_get_functiondef('stage65_prevent_certified_hos_log_delete()'::regprocedure))=0
     OR position('COALESCE(current_setting(''opstrax.offboarding''' in
       pg_get_functiondef('stage65_guard_hos_certification_snapshot()'::regprocedure))=0
     OR position('opstrax_system' in
       pg_get_functiondef('stage65_guard_hos_certification_snapshot()'::regprocedure))=0 THEN
    RAISE EXCEPTION 'Null-safe dual-gated HOS offboarding immutability guard is incomplete';
  END IF;
  IF position('COALESCE(current_setting(''opstrax.offboarding''' in
       pg_get_functiondef('detention_evidence_immutable()'::regprocedure))=0
     OR position('opstrax_system' in
       pg_get_functiondef('detention_evidence_immutable()'::regprocedure))=0 THEN
    RAISE EXCEPTION 'Null-safe dual-gated detention offboarding immutability guard is incomplete';
  END IF;
END
$verify_pilot_wave$;
SQL
echo "Owner integrity: Stage42 gateway schema plus pilot ledgers and critical contracts verified"

if [ "$stage58_already_applied" = "1" ]; then
  echo "Reapplying terminal Stage58 without a legacy-policy window…"
  psql "$NEON_PG_URI" -v ON_ERROR_STOP=1 -q -f database/migrations/2026_07_31_stage58_nonforgeable_tenant_ticket.sql
  psql "$NEON_PG_URI" -v ON_ERROR_STOP=1 -q -f database/migrations/2026_07_31_stage59_data_protection_key_ring.sql
  echo "Reapplying Stage67 device-credential boundary after Stage58…"
  psql "$NEON_PG_URI" -v ON_ERROR_STOP=1 -q -f database/migrations/2026_08_02_stage67_telematics_diagnostics_integrity.sql
  echo "Applying terminal Stage76 telemetry ACL reconciliation…"
  psql "$NEON_PG_URI" -v ON_ERROR_STOP=1 -q -f database/migrations/2026_08_11_stage76_telematics_security_hardening.sql
  psql "$NEON_PG_URI" -v ON_ERROR_STOP=1 <<'SQL'
DO $stage58_rerun$
BEGIN
  IF (SELECT count(*) FROM pg_policies WHERE schemaname='public' AND roles='{public}'::name[])<>0
     OR (SELECT count(*) FROM schema_migrations WHERE version='2026_07_31_stage58_nonforgeable_tenant_ticket')<>1
     OR (SELECT count(*) FROM schema_migrations WHERE version='2026_08_11_stage76_telematics_security_hardening')<>1
     OR NOT has_function_privilege('opstrax_system','opstrax_security.issue_tenant_ticket(bigint,integer,bigint,integer)','EXECUTE')
     OR has_function_privilege('opstrax_app','opstrax_security.issue_tenant_ticket(bigint,integer,bigint,integer)','EXECUTE')
     OR has_table_privilege('opstrax_app','eld_devices','SELECT')
     OR has_column_privilege('opstrax_app','eld_devices','hmac_secret_encrypted','SELECT') THEN
    RAISE EXCEPTION 'Stage58/59/67/76 terminal rerun verification failed';
  END IF;
END
$stage58_rerun$;
SQL
  echo "✅ Existing Stage58/59/67/76 deployment reconciled with Stage76 terminal."
  exit 0
fi

echo
echo "Post-check: auth-critical columns…"
psql "$NEON_PG_URI" -v ON_ERROR_STOP=1 -tA -c "
  SELECT
    'users.customer_id: ' || COUNT(*) FILTER (WHERE column_name='customer_id') ||
    ' | users.branch_id: ' || COUNT(*) FILTER (WHERE column_name='branch_id')
  FROM information_schema.columns WHERE table_name='users' AND column_name IN ('customer_id','branch_id')"
psql "$NEON_PG_URI" -v ON_ERROR_STOP=1 -tA -c "SELECT 'branches table: ' || COUNT(*) FROM information_schema.tables WHERE table_name='branches'"
echo "Post-check: customer feedback service contract…"
psql "$NEON_PG_URI" -v ON_ERROR_STOP=1 <<'SQL'
DO $verify$
DECLARE
  missing_columns TEXT[];
BEGIN
  SELECT ARRAY_AGG(expected.column_name ORDER BY expected.column_name)
    INTO missing_columns
  FROM (VALUES
      ('customer_id'), ('comment'), ('feedback_type'),
      ('subject'), ('status'), ('updated_at')
    ) AS expected(column_name)
  LEFT JOIN information_schema.columns actual
    ON actual.table_schema='public'
   AND actual.table_name='customer_feedback'
   AND actual.column_name=expected.column_name
  WHERE actual.column_name IS NULL;

  IF COALESCE(cardinality(missing_columns), 0) > 0 THEN
    RAISE EXCEPTION 'customer_feedback contract missing columns: %', missing_columns;
  END IF;
  IF to_regclass('public.ix_customer_feedback_company_customer') IS NULL THEN
    RAISE EXCEPTION 'customer_feedback contract missing index ix_customer_feedback_company_customer';
  END IF;
END
$verify$;
SQL
echo "customer_feedback: 6/6 columns + ix_customer_feedback_company_customer"
echo "Post-check: durable one-time MFA challenge ledger…"
if [ "$stage58_already_applied" != "1" ]; then
psql "$NEON_PG_URI" -v ON_ERROR_STOP=1 <<'SQL'
DO $verify$
DECLARE
  table_oid OID := to_regclass('public.mfa_login_challenge_consumptions');
BEGIN
  IF table_oid IS NULL THEN
    RAISE EXCEPTION 'MFA replay protection missing table mfa_login_challenge_consumptions';
  END IF;

  IF NOT EXISTS (
      SELECT 1 FROM pg_constraint
      WHERE conrelid=table_oid AND conname='ck_mfa_challenge_hash_sha256'
        AND convalidated
        AND pg_get_constraintdef(oid) LIKE '%[0-9a-f]{64}%'
  ) THEN
    RAISE EXCEPTION 'MFA replay protection missing/invalid SHA-256 digest constraint';
  END IF;

  IF NOT EXISTS (
      SELECT 1 FROM pg_index i JOIN pg_class c ON c.oid=i.indexrelid
      WHERE i.indrelid=table_oid AND c.relname='ux_mfa_challenge_consumptions_hash' AND i.indisunique
  ) OR to_regclass('public.idx_mfa_challenge_consumptions_expiry') IS NULL
     OR to_regclass('public.idx_mfa_challenge_consumptions_tenant_user') IS NULL THEN
    RAISE EXCEPTION 'MFA replay protection missing digest/expiry/tenant-user indexes';
  END IF;

  IF NOT EXISTS (
      SELECT 1 FROM pg_class
      WHERE oid=table_oid AND relrowsecurity AND relforcerowsecurity
  ) THEN
    RAISE EXCEPTION 'MFA replay protection must have ENABLE + FORCE RLS';
  END IF;

  IF NOT EXISTS (
      SELECT 1 FROM pg_policies
      WHERE schemaname='public' AND tablename='mfa_login_challenge_consumptions'
        AND policyname='tenant_isolation' AND cmd='ALL'
        AND qual LIKE '%app.current_tenant_id%' AND with_check LIKE '%app.current_tenant_id%'
  ) OR NOT EXISTS (
      SELECT 1 FROM pg_policies
      WHERE schemaname='public' AND tablename='mfa_login_challenge_consumptions'
        AND policyname='platform_admin_bypass' AND cmd='ALL'
        AND qual LIKE '%app.platform_admin%' AND with_check LIKE '%app.platform_admin%'
  ) THEN
    RAISE EXCEPTION 'MFA replay protection missing tenant/platform RLS policies';
  END IF;

  IF NOT EXISTS (SELECT 1 FROM pg_roles WHERE rolname='opstrax_app') THEN
    RAISE EXCEPTION 'MFA replay protection requires pre-provisioned opstrax_app role (apply stage20 first)';
  END IF;
  IF NOT has_table_privilege('opstrax_app', table_oid, 'SELECT')
     OR NOT has_table_privilege('opstrax_app', table_oid, 'INSERT')
     OR NOT has_table_privilege('opstrax_app', table_oid, 'DELETE')
     OR has_table_privilege('opstrax_app', table_oid, 'UPDATE') THEN
    RAISE EXCEPTION 'MFA replay ledger opstrax_app table privileges are not least-privilege SELECT/INSERT/DELETE';
  END IF;
  IF NOT has_sequence_privilege('opstrax_app', 'public.mfa_login_challenge_consumptions_id_seq', 'USAGE')
     OR NOT has_sequence_privilege('opstrax_app', 'public.mfa_login_challenge_consumptions_id_seq', 'SELECT') THEN
    RAISE EXCEPTION 'MFA replay ledger opstrax_app sequence privileges missing';
  END IF;

  IF (SELECT COUNT(*) FROM schema_migrations WHERE version='2026_07_30_stage49_mfa_challenge_one_time') <> 1 THEN
    RAISE EXCEPTION 'MFA replay migration ledger registration missing or duplicated';
  END IF;
END
$verify$;
SQL
echo "mfa_login_challenge_consumptions: digest + indexes + FORCE RLS + policies + runtime grants verified"
else
  echo "MFA replay: terminal Stage58 policy contract already active — legacy pre-terminal policy check skipped"
fi
echo "Post-check: complete Fleet production schema/RLS contract…"
if [ "$stage58_already_applied" != "1" ]; then
psql "$NEON_PG_URI" -v ON_ERROR_STOP=1 <<'SQL'
DO $verify$
DECLARE
  required_tables TEXT[] := ARRAY[
    'vehicles','drivers','vehicle_assignments','dispatch_assignments',
    'fleet_tms_shipments','fleet_tms_shipment_stops','fleet_tms_pods','fleet_tms_tracking_links',
    'fleet_tms_shipment_events','fleet_tms_driver_tasks','fleet_tms_vehicles','fleet_tms_tracking_points',
    'fleet_tms_maintenance_tickets','fleet_tms_fuel_events','fleet_tms_temperature_zones',
    'fleet_tms_temperature_devices','fleet_tms_temperature_readings','fleet_tms_temperature_alerts',
    'fleet_tms_cold_chain_reports','fleet_tms_refrigeration_unit_health','fleet_tms_asset_types',
    'fleet_tms_assets','fleet_tms_asset_assignments','fleet_tms_asset_events',
    'fleet_tms_barcode_scan_events','fleet_tms_rfid_events','fleet_tms_saudi_regions',
    'fleet_tms_readiness_documents','fleet_tms_cold_chain_policies','fleet_tms_cold_chain_event_log',
    'fleet_tms_dispatch_orders','fleet_tms_delivery_routes','fleet_tms_last_mile_stops',
    'market_packs','market_pack_features','tenant_market_packs','market_address_schemas',
    'market_document_types','market_driver_requirements','market_vehicle_requirements',
    'market_inspection_templates','inspection_items','market_tax_reporting_rules','market_unit_settings',
    'market_currency_settings','market_language_settings','compliance_records','compliance_record_documents',
    'compliance_expiry_events','vehicle_inspection_records','inspection_defects',
    'jurisdiction_mileage_records','jurisdiction_fuel_records','driver_duty_status_records',
    'eld_device_registry','market_addresses','business_tax_readiness'
  ];
  missing TEXT[];
  bad_rls TEXT[];
  bad_grants TEXT[];
  bad_reference_grants TEXT[];
  reference_tables TEXT[] := ARRAY[
    'fleet_tms_saudi_regions','market_packs','market_pack_features','market_address_schemas',
    'market_document_types','market_driver_requirements','market_vehicle_requirements',
    'market_inspection_templates','inspection_items','market_tax_reporting_rules',
    'market_unit_settings','market_currency_settings','market_language_settings'
  ];
  app_super BOOLEAN;
  app_bypass BOOLEAN;
BEGIN
  SELECT ARRAY_AGG(name ORDER BY name) INTO missing
  FROM unnest(required_tables) name WHERE to_regclass('public.' || name) IS NULL;
  IF COALESCE(cardinality(missing),0)>0 THEN
    RAISE EXCEPTION 'Fleet production contract missing tables: %',missing;
  END IF;

  IF NOT EXISTS (SELECT 1 FROM pg_roles role
    WHERE role.rolname='opstrax_app' AND role.rolcanlogin
      AND NOT role.rolsuper AND NOT role.rolbypassrls
      AND NOT role.rolcreatedb AND NOT role.rolcreaterole
      AND NOT role.rolinherit AND NOT role.rolreplication
      AND NOT EXISTS (SELECT 1 FROM pg_auth_members membership WHERE membership.member=role.oid)
      AND has_database_privilege('opstrax_app',current_database(),'CONNECT')
      AND NOT has_database_privilege('opstrax_app',current_database(),'CREATE')
      AND NOT has_database_privilege('opstrax_app',current_database(),'TEMPORARY')
      AND has_schema_privilege('opstrax_app','public','USAGE')
      AND NOT has_schema_privilege('opstrax_app','public','CREATE')) THEN
    RAISE EXCEPTION 'opstrax_app role capabilities or memberships are unsafe';
  END IF;

  SELECT ARRAY_AGG(c.relname ORDER BY c.relname) INTO bad_rls
  FROM pg_class c JOIN pg_namespace n ON n.oid=c.relnamespace
  CROSS JOIN LATERAL (SELECT CASE
    WHEN EXISTS (SELECT 1 FROM information_schema.columns x WHERE x.table_schema='public' AND x.table_name=c.relname AND x.column_name='company_id' AND x.data_type='bigint') THEN 'company_id'
    WHEN EXISTS (SELECT 1 FROM information_schema.columns x WHERE x.table_schema='public' AND x.table_name=c.relname AND x.column_name='tenant_id' AND x.data_type='bigint') THEN 'tenant_id'
  END AS tenant_col) scope
  WHERE n.nspname='public' AND c.relkind IN ('r','p')
    AND c.relname=ANY(required_tables) AND scope.tenant_col IS NOT NULL
    AND (NOT c.relrowsecurity OR NOT c.relforcerowsecurity
      OR (SELECT COUNT(*) FROM pg_policies p WHERE p.schemaname='public' AND p.tablename=c.relname)<>2
      OR NOT EXISTS (SELECT 1 FROM pg_policies p WHERE p.schemaname='public' AND p.tablename=c.relname
        AND p.policyname='tenant_isolation' AND p.permissive='PERMISSIVE' AND p.roles='{public}'::name[] AND p.cmd='ALL'
        AND p.qual=format('(%s = (NULLIF(current_setting(''app.current_tenant_id''::text, true), ''''::text))::bigint)',scope.tenant_col)
        AND p.with_check=p.qual)
      OR NOT EXISTS (SELECT 1 FROM pg_policies p WHERE p.schemaname='public' AND p.tablename=c.relname
        AND p.policyname='platform_admin_bypass' AND p.permissive='PERMISSIVE' AND p.roles='{public}'::name[] AND p.cmd='ALL'
        AND p.qual='(NULLIF(current_setting(''app.platform_admin''::text, true), ''''::text) = ''on''::text)'
        AND p.with_check=p.qual));
  IF COALESCE(cardinality(bad_rls),0)>0 THEN
    RAISE EXCEPTION 'Fleet tenant tables missing ENABLE/FORCE RLS or canonical policies: %',bad_rls;
  END IF;

  WITH fleet_privileges(name,allow_insert,allow_update,allow_delete) AS (VALUES
    ('fleet_tms_shipment_events',true,false,false),
    ('fleet_tms_cold_chain_event_log',true,false,false),
    ('fleet_tms_asset_events',true,false,false),
    ('fleet_tms_barcode_scan_events',true,false,false),
    ('fleet_tms_rfid_events',true,false,false),
    ('compliance_expiry_events',true,true,false)
  )
  SELECT ARRAY_AGG(name ORDER BY name) INTO bad_grants
  FROM unnest(required_tables) name
  JOIN information_schema.columns col ON col.table_schema='public' AND col.table_name=name AND col.column_name='company_id'
  LEFT JOIN fleet_privileges expected USING(name)
  WHERE NOT has_table_privilege('opstrax_app','public.' || name,'SELECT')
     OR has_table_privilege('opstrax_app','public.' || name,'TRUNCATE')
     OR has_table_privilege('opstrax_app','public.' || name,'REFERENCES')
     OR has_table_privilege('opstrax_app','public.' || name,'TRIGGER')
     OR has_table_privilege('opstrax_app','public.' || name,'INSERT')<>COALESCE(expected.allow_insert,true)
     OR has_table_privilege('opstrax_app','public.' || name,'UPDATE')<>COALESCE(expected.allow_update,true)
     OR has_table_privilege('opstrax_app','public.' || name,'DELETE')<>COALESCE(expected.allow_delete,true);
  IF COALESCE(cardinality(bad_grants),0)>0 THEN
    RAISE EXCEPTION 'Fleet tenant tables missing runtime DML grants: %',bad_grants;
  END IF;

  SELECT ARRAY_AGG(name ORDER BY name) INTO bad_reference_grants
  FROM unnest(reference_tables) name
  WHERE NOT has_table_privilege('opstrax_app','public.'||name,'SELECT')
     OR has_table_privilege('opstrax_app','public.'||name,'INSERT')
     OR has_table_privilege('opstrax_app','public.'||name,'UPDATE')
     OR has_table_privilege('opstrax_app','public.'||name,'DELETE')
     OR has_table_privilege('opstrax_app','public.'||name,'TRUNCATE')
     OR has_table_privilege('opstrax_app','public.'||name,'REFERENCES')
     OR has_table_privilege('opstrax_app','public.'||name,'TRIGGER');
  IF COALESCE(cardinality(bad_reference_grants),0)>0 THEN
    RAISE EXCEPTION 'Fleet reference tables are not SELECT-only for opstrax_app: %',bad_reference_grants;
  END IF;

  IF (SELECT COUNT(*) FROM market_packs WHERE code IN ('canada_na','saudi_gcc') AND status='active')<>2 THEN
    RAISE EXCEPTION 'Fleet market reference catalog is incomplete';
  END IF;
  IF to_regclass('public.uq_ftms_shipment_identity') IS NULL
     OR to_regclass('public.uq_ftms_vehicle_identity') IS NULL
     OR to_regclass('public.ux_ftms_assets_branch_tag_normalized') IS NULL
     OR to_regclass('public.uq_ftms_ccpolicy_branch_idem') IS NULL THEN
    RAISE EXCEPTION 'Fleet correctness/idempotency indexes are incomplete';
  END IF;
  IF (SELECT COUNT(*) FROM schema_migrations WHERE version='2026_07_30_stage50_fleet_production_contract')<>1 THEN
    RAISE EXCEPTION 'Fleet Stage-50 migration ledger registration missing or duplicated';
  END IF;

  -- Stage 50 must never regress Stage 49's append/delete-only replay ledger.
  IF has_table_privilege('opstrax_app','public.mfa_login_challenge_consumptions','UPDATE') THEN
    RAISE EXCEPTION 'Stage 50 regressed MFA replay-ledger least privilege';
  END IF;
END
$verify$;
SQL
echo "Fleet: 58 route-contract tables + market catalog + RLS/FORCE/policies/grants/indexes verified"
else
  echo "Fleet production: terminal Stage58 policy contract already active — legacy pre-terminal policy check skipped"
fi
echo "Post-check: production runtime worker support…"
if [ "$stage58_already_applied" != "1" ]; then
psql "$NEON_PG_URI" -v ON_ERROR_STOP=1 <<'SQL'
DO $verify$
DECLARE
  required TEXT[] := ARRAY[
    'service_run_history','service_heartbeats','platform_incidents','notifications',
    'notification_recipients','escalation_rules','saved_reports','report_execution_log',
    'scheduled_report_deliveries','scheduled_reports','routes','route_stops','trips','trip_stops',
    'location_events','latest_vehicle_positions','telemetry_alerts','telemetry_rules','telemetry_nonces',
    'gps_gateway_replay','safety_events','driver_safety_scores','telemetry_live_asset_states',
    'fleet_health_snapshots','evidence_package_items','vehicle_safety_scorecards','ai_recommendations',
    'maintenance_pm_rules','vehicles','maintenance_items','integrations','geofences',
    'dispatch_assignments','dispatch_exceptions'
  ];
  tenant_tables TEXT[] := ARRAY[
    'platform_incidents','notifications','notification_recipients','escalation_rules',
    'saved_reports','report_execution_log','scheduled_report_deliveries','scheduled_reports',
    'routes','route_stops','trips','trip_stops','location_events','latest_vehicle_positions',
    'telemetry_alerts','telemetry_rules','safety_events','driver_safety_scores',
    'telemetry_live_asset_states','fleet_health_snapshots','evidence_package_items',
    'vehicle_safety_scorecards','ai_recommendations','maintenance_pm_rules','vehicles',
    'maintenance_items','integrations','geofences','dispatch_assignments','dispatch_exceptions'
  ];
  missing TEXT[];
  bad TEXT[];
BEGIN
  SELECT ARRAY_AGG(name ORDER BY name) INTO missing FROM unnest(required) name
  WHERE to_regclass('public.'||name) IS NULL;
  IF COALESCE(cardinality(missing),0)>0 THEN
    RAISE EXCEPTION 'Production worker support missing tables: %',missing;
  END IF;
  SELECT ARRAY_AGG(name ORDER BY name) INTO bad FROM unnest(tenant_tables) name
  JOIN pg_class c ON c.oid=to_regclass('public.'||name)
  WHERE NOT c.relrowsecurity OR NOT c.relforcerowsecurity
    OR NOT EXISTS (SELECT 1 FROM pg_policies p WHERE p.schemaname='public' AND p.tablename=name
      AND p.policyname='tenant_isolation' AND p.permissive='PERMISSIVE' AND p.roles='{public}'::name[] AND p.cmd='ALL'
      AND p.qual=format('(%s = (NULLIF(current_setting(''app.current_tenant_id''::text, true), ''''::text))::bigint)',
        CASE WHEN name='scheduled_reports' THEN 'tenant_id' ELSE 'company_id' END)
      AND p.with_check=p.qual)
    OR NOT EXISTS (SELECT 1 FROM pg_policies p WHERE p.schemaname='public' AND p.tablename=name
      AND p.policyname='platform_admin_bypass' AND p.permissive='PERMISSIVE' AND p.roles='{public}'::name[] AND p.cmd='ALL'
      AND p.qual='(NULLIF(current_setting(''app.platform_admin''::text, true), ''''::text) = ''on''::text)'
      AND p.with_check=p.qual)
    OR NOT has_table_privilege('opstrax_app','public.'||name,'SELECT')
    OR NOT has_table_privilege('opstrax_app','public.'||name,'INSERT')
    OR NOT has_table_privilege('opstrax_app','public.'||name,'UPDATE')
    OR NOT has_table_privilege('opstrax_app','public.'||name,'DELETE');
  IF COALESCE(cardinality(bad),0)>0 THEN
    RAISE EXCEPTION 'Production worker support RLS/policy/grant violations: %',bad;
  END IF;
  IF NOT has_table_privilege('opstrax_app','public.service_run_history','SELECT')
     OR NOT has_table_privilege('opstrax_app','public.service_run_history','INSERT')
     OR NOT has_table_privilege('opstrax_app','public.service_run_history','UPDATE')
     OR NOT has_table_privilege('opstrax_app','public.service_run_history','DELETE')
     OR NOT has_table_privilege('opstrax_app','public.service_heartbeats','SELECT')
     OR NOT has_table_privilege('opstrax_app','public.service_heartbeats','INSERT')
     OR NOT has_table_privilege('opstrax_app','public.service_heartbeats','UPDATE')
     OR NOT has_table_privilege('opstrax_app','public.service_heartbeats','DELETE') THEN
    RAISE EXCEPTION 'Production worker telemetry grants missing';
  END IF;
  IF NOT EXISTS (SELECT 1 FROM information_schema.columns WHERE table_schema='public' AND table_name='scheduled_reports' AND column_name='saved_report_id')
     OR NOT EXISTS (SELECT 1 FROM information_schema.columns WHERE table_schema='public' AND table_name='notifications' AND column_name='escalated_from') THEN
    RAISE EXCEPTION 'Production worker support column contract incomplete';
  END IF;
  IF EXISTS (SELECT 1 FROM information_schema.columns WHERE table_schema='public' AND table_name='route_stops'
             AND column_name='company_id' AND column_default IS NOT NULL)
     OR EXISTS (SELECT 1 FROM route_stops rs LEFT JOIN routes r ON r.id=rs.route_id
                WHERE rs.company_id IS NULL OR r.id IS NULL OR rs.company_id<>r.company_id) THEN
    RAISE EXCEPTION 'route_stops tenant scope is unresolved, mismatched, or retains an unsafe default';
  END IF;
  IF (SELECT COUNT(*) FROM schema_migrations WHERE version='2026_07_30_stage51_production_runtime_support')<>1 THEN
    RAISE EXCEPTION 'Stage-51 runtime support ledger registration missing or duplicated';
  END IF;
  IF has_table_privilege('opstrax_app','public.mfa_login_challenge_consumptions','UPDATE') THEN
    RAISE EXCEPTION 'Stage 51 regressed MFA replay-ledger least privilege';
  END IF;
END
$verify$;
SQL
echo "Runtime support: worker tables + RLS/FORCE/policies/grants/columns verified"
else
  echo "runtime worker: terminal Stage58 policy contract already active — legacy pre-terminal policy check skipped"
fi
echo "Post-check: Fleet master identity uniqueness…"
psql "$NEON_PG_URI" -v ON_ERROR_STOP=1 <<'SQL'
DO $verify$
DECLARE
  invalid TEXT[];
BEGIN
  SELECT ARRAY_AGG(expected.name ORDER BY expected.name) INTO invalid
  FROM (VALUES
    ('uq_vehicles_identity_code_normalized','vehicles','company_id','lower(btrim(vehicle_code::text))',''),
    ('uq_drivers_identity_code_normalized','drivers','company_id','lower(btrim(driver_code::text))',''),
    ('uq_vehicles_active_vin_normalized','vehicles','company_id','lower(btrim(vin::text))','deleted_at IS NULL AND NULLIF(btrim(vin::text), ''''::text) IS NOT NULL'),
    ('uq_drivers_active_license_plaintext_normalized','drivers','company_id','lower(btrim(license_number::text))','deleted_at IS NULL AND NULLIF(btrim(license_number::text), ''''::text) IS NOT NULL AND NULLIF(btrim(license_number_bidx::text), ''''::text) IS NULL'),
    ('uq_drivers_active_license_bidx','drivers','company_id','license_number_bidx','deleted_at IS NULL AND NULLIF(btrim(license_number_bidx::text), ''''::text) IS NOT NULL')
  ) expected(name,table_name,key1,key2,predicate)
  LEFT JOIN pg_class idx ON idx.oid=to_regclass('public.'||expected.name)
  LEFT JOIN pg_index i ON i.indexrelid=idx.oid
  WHERE NOT COALESCE(
    i.indisunique AND i.indisvalid AND i.indisready
    AND i.indnkeyatts=2 AND i.indnatts=2
    AND i.indrelid=to_regclass('public.'||expected.table_name)
    AND pg_get_indexdef(i.indexrelid,1,true)=expected.key1
    AND pg_get_indexdef(i.indexrelid,2,true)=expected.key2
    AND COALESCE(pg_get_expr(i.indpred,i.indrelid,true),'')=expected.predicate,
    false);
  IF COALESCE(cardinality(invalid),0)>0 THEN
    RAISE EXCEPTION 'Fleet identity uniqueness indexes missing, invalid, or drifted: %',invalid;
  END IF;
  IF EXISTS (
    SELECT 1 FROM drivers
    WHERE deleted_at IS NULL
      AND license_number LIKE 'enc:%'
      AND NULLIF(BTRIM(license_number_bidx),'') IS NULL
  ) THEN
    RAISE EXCEPTION 'Fleet identity transition unsafe: active encrypted driver licence rows lack license_number_bidx; run a key-aware blind-index backfill';
  END IF;
  IF (SELECT COUNT(*) FROM schema_migrations WHERE version='2026_07_30_stage52_fleet_identity_uniqueness')<>1 THEN
    RAISE EXCEPTION 'Stage-52 Fleet identity migration ledger registration missing or duplicated';
  END IF;
END
$verify$;
SQL
echo "Fleet identity: normalized code + active VIN/licence indexes verified"
echo "Applying terminal Stage58/59 security reconciliation before production-wide coverage…"
terminal_migration=2026_07_31_stage58_nonforgeable_tenant_ticket
terminal_file="database/migrations/${terminal_migration}.sql"
[ -f "$terminal_file" ] || { echo "ERROR: missing $terminal_file (run from repo root)" >&2; exit 1; }
# Historical Stage49-52 checks above intentionally validate their pre-cutover
# contracts. Every tenant table now exists, including Stage60-63 additions, so
# Stage58 can atomically replace all legacy/public GUC policies before the first
# production-wide policy scan. There is no post-scan legacy-policy window.
psql "$NEON_PG_URI" -v ON_ERROR_STOP=1 -q -f "$terminal_file"
psql "$NEON_PG_URI" -v ON_ERROR_STOP=1 -q -f database/migrations/2026_07_31_stage59_data_protection_key_ring.sql

echo "Post-check: production-wide tenant RLS coverage…"
if [ "$stage58_already_applied" != "1" ]; then
psql "$NEON_PG_URI" -v ON_ERROR_STOP=1 <<'SQL'
DO $verify$
DECLARE
  violations TEXT[];
BEGIN
  IF EXISTS (
    SELECT 1
    FROM pg_default_acl d
    LEFT JOIN pg_namespace ns ON ns.oid=d.defaclnamespace
    CROSS JOIN LATERAL aclexplode(d.defaclacl) acl
    JOIN pg_roles grantee ON grantee.oid=acl.grantee
    WHERE (d.defaclnamespace=0 OR ns.nspname='public')
      AND grantee.rolname='opstrax_app'
      AND d.defaclobjtype IN ('r','S')
  ) THEN
    RAISE EXCEPTION 'Broad opstrax_app default privileges remain';
  END IF;
  WITH tenant_scope AS (
    SELECT cls.oid,cls.relname AS table_name,cls.relrowsecurity,cls.relforcerowsecurity,
      CASE
        WHEN cls.relname='companies' THEN 'id'
        WHEN EXISTS (SELECT 1 FROM information_schema.columns x WHERE x.table_schema='public' AND x.table_name=cls.relname AND x.column_name='company_id' AND x.data_type='bigint') THEN 'company_id'
        ELSE 'tenant_id'
      END AS tenant_col
    FROM pg_class cls JOIN pg_namespace ns ON ns.oid=cls.relnamespace
    WHERE ns.nspname='public' AND cls.relkind IN ('r','p')
      AND cls.relname NOT IN ('platform_invoices','gps_gateway_replay','platform_impersonation_sessions','roles','report_catalog')
      AND (cls.relname='companies' OR EXISTS (SELECT 1 FROM information_schema.columns x
        WHERE x.table_schema='public' AND x.table_name=cls.relname
          AND x.column_name IN ('company_id','tenant_id') AND x.data_type='bigint'))
  ), privilege_matrix(table_name,allow_insert,allow_update,allow_delete) AS (VALUES
    ('authorization_decision_logs',true,false,false),
    ('companies',false,true,false),
    ('audit_logs',true,false,false),
    ('usage_events',true,false,false),
    ('compliance_evidence',true,false,false),
    ('fleet_tms_shipment_events',true,false,false),
    ('fleet_tms_cold_chain_event_log',true,false,false),
    ('fleet_tms_asset_events',true,false,false),
    ('fleet_tms_barcode_scan_events',true,false,false),
    ('fleet_tms_rfid_events',true,false,false),
    ('compliance_expiry_events',true,true,false),
    ('market_pack_branch_migration_audit',false,false,false),
    ('access_review_items',true,true,false),('access_reviews',true,true,false),
    ('backup_verifications',true,false,false),('company_security_settings',true,true,false),
    ('compliance_audit_packages',true,true,false),('compliance_violations',false,true,false),
    ('data_retention_policies',true,true,false),('driver_compliance_status',false,false,false),
    ('export_requests',true,true,false),('fleet_tms_branch_migration_audit',false,false,false),
    ('hos_clocks',false,false,false),('platform_impersonation_sessions',true,true,false),
    ('security_events',true,false,false),('sso_connections',true,true,false),
    ('tenant_market_packs',false,false,false),
    ('tenant_entitlements',false,false,false),('demo_fixture_versions',false,false,false),('tenant_subscriptions',false,false,false),
    ('vehicle_compliance_status',false,false,false),('workforce_schedules',true,true,false),
    ('mfa_login_challenge_consumptions',true,false,true)
  )
  SELECT array_agg(s.table_name ORDER BY s.table_name) INTO violations
  FROM tenant_scope s LEFT JOIN privilege_matrix expected ON expected.table_name=s.table_name
  WHERE NOT s.relrowsecurity OR NOT s.relforcerowsecurity
    OR (SELECT COUNT(*) FROM pg_policies p WHERE p.schemaname='public' AND p.tablename=s.table_name)<>2
    OR NOT EXISTS (SELECT 1 FROM pg_policies p
      WHERE p.schemaname='public' AND p.tablename=s.table_name
        AND p.policyname='tenant_ticket_app' AND p.permissive='PERMISSIVE'
        AND p.roles='{opstrax_app}'::name[] AND p.cmd='ALL'
        AND p.qual LIKE '%opstrax_security.current_tenant_id()%'
        AND p.qual LIKE '%SELECT%'
        AND p.with_check=p.qual)
    OR NOT EXISTS (SELECT 1 FROM pg_policies p
      WHERE p.schemaname='public' AND p.tablename=s.table_name
        AND p.policyname='system_control_plane' AND p.permissive='PERMISSIVE'
        AND p.roles='{opstrax_system}'::name[] AND p.cmd='ALL'
        AND p.qual='true'
        AND p.with_check=p.qual)
    OR NOT has_table_privilege('opstrax_app',s.oid,'SELECT')
    OR has_table_privilege('opstrax_app',s.oid,'TRUNCATE')
    OR has_table_privilege('opstrax_app',s.oid,'REFERENCES')
    OR has_table_privilege('opstrax_app',s.oid,'TRIGGER')
    OR (expected.table_name IS NULL AND (
      NOT has_table_privilege('opstrax_app',s.oid,'INSERT')
      OR NOT has_table_privilege('opstrax_app',s.oid,'UPDATE')
      OR NOT has_table_privilege('opstrax_app',s.oid,'DELETE')))
    OR (expected.table_name IS NOT NULL AND (
      has_table_privilege('opstrax_app',s.oid,'INSERT')<>expected.allow_insert
      OR has_table_privilege('opstrax_app',s.oid,'UPDATE')<>expected.allow_update
      OR has_table_privilege('opstrax_app',s.oid,'DELETE')<>expected.allow_delete));
  IF COALESCE(cardinality(violations),0)>0 THEN
    RAISE EXCEPTION 'Tenant RLS coverage/policy/grant violations: %',violations;
  END IF;
  IF EXISTS (
    SELECT 1 FROM pg_class tbl
    JOIN pg_namespace ns ON ns.oid=tbl.relnamespace AND ns.nspname='public'
    JOIN pg_depend dep ON dep.refobjid=tbl.oid AND dep.refobjsubid>0 AND dep.deptype IN ('a','i')
    JOIN pg_class seq ON seq.oid=dep.objid AND seq.relkind='S'
    WHERE tbl.relkind IN ('r','p')
      AND tbl.relname NOT IN ('platform_invoices','gps_gateway_replay','platform_impersonation_sessions','roles','report_catalog')
      AND (tbl.relname='companies' OR EXISTS (SELECT 1 FROM information_schema.columns x
        WHERE x.table_schema='public' AND x.table_name=tbl.relname
          AND x.column_name IN ('company_id','tenant_id') AND x.data_type='bigint'))
      AND (has_sequence_privilege('opstrax_app',seq.oid,'USAGE')<>
             has_table_privilege('opstrax_app',tbl.oid,'INSERT')
        OR has_sequence_privilege('opstrax_app',seq.oid,'SELECT')<>
             has_table_privilege('opstrax_app',tbl.oid,'INSERT')
        OR has_sequence_privilege('opstrax_app',seq.oid,'UPDATE'))
  ) THEN
    RAISE EXCEPTION 'Tenant sequence privileges do not match insert capability';
  END IF;
  IF (SELECT COUNT(*) FROM schema_migrations WHERE version='2026_07_30_stage53_tenant_rls_reconciliation')<>1 THEN
    RAISE EXCEPTION 'Stage-53 tenant RLS reconciliation ledger missing or duplicated';
  END IF;
  IF has_table_privilege('opstrax_app','public.mfa_login_challenge_consumptions','UPDATE') THEN
    RAISE EXCEPTION 'Stage 53 regressed MFA replay-ledger least privilege';
  END IF;
END
$verify$;
SQL
tenant_rls_count=$(psql "$NEON_PG_URI" -v ON_ERROR_STOP=1 -tA -c "
  SELECT COUNT(*) FROM pg_class cls JOIN pg_namespace ns ON ns.oid=cls.relnamespace
  WHERE ns.nspname='public' AND cls.relkind IN ('r','p')
    AND cls.relname NOT IN ('platform_invoices','gps_gateway_replay','platform_impersonation_sessions','roles','report_catalog')
    AND (cls.relname='companies' OR EXISTS (
      SELECT 1 FROM information_schema.columns c
      WHERE c.table_schema='public' AND c.table_name=cls.relname
        AND c.column_name IN ('company_id','tenant_id') AND c.data_type='bigint'))")
echo "Tenant RLS coverage: ${tenant_rls_count} in-scope tables verified"
else
  echo "tenant RLS: terminal Stage58 policy contract already active — legacy pre-terminal policy check skipped"
fi
echo "Post-check: Fleet cold-chain/runtime-route/asset/workforce integrity contracts…"
psql "$NEON_PG_URI" -v ON_ERROR_STOP=1 <<'SQL'
DO $verify$
DECLARE invalid TEXT[];
BEGIN
  SELECT array_agg(expected.name ORDER BY expected.name) INTO invalid
  FROM (VALUES
    ('uq_ftms_tdev_tenant_code_norm','fleet_tms_temperature_devices',2,'company_id','lower(btrim(device_code::text))','',''),
    ('uq_ftms_tdev_branch_idem','fleet_tms_temperature_devices',3,'company_id','COALESCE(branch_id, 0::bigint)','idempotency_key','(NULLIF(btrim((idempotency_key)::text), ''''::text) IS NOT NULL)'),
    ('uq_ftms_atype_tenant_code_norm','fleet_tms_asset_types',2,'company_id','lower(btrim(code::text))','','')
  ) expected(name,table_name,key_count,key1,key2,key3,predicate)
  LEFT JOIN pg_class idx ON idx.oid=to_regclass('public.'||expected.name)
  LEFT JOIN pg_index i ON i.indexrelid=idx.oid
  WHERE idx.oid IS NULL OR NOT i.indisunique OR NOT i.indisvalid OR NOT i.indisready
    OR i.indrelid<>to_regclass('public.'||expected.table_name)
    OR i.indnkeyatts<>expected.key_count OR i.indnatts<>expected.key_count
    OR pg_get_indexdef(i.indexrelid,1,true)<>expected.key1
    OR pg_get_indexdef(i.indexrelid,2,true)<>expected.key2
    OR (expected.key_count=3 AND pg_get_indexdef(i.indexrelid,3,true)<>expected.key3)
    OR COALESCE(pg_get_expr(i.indpred,i.indrelid),'')<>expected.predicate;
  IF COALESCE(cardinality(invalid),0)>0 THEN
    RAISE EXCEPTION 'Fleet Stage54/56 integrity indexes missing, invalid, or drifted: %',invalid;
  END IF;

  IF (SELECT COUNT(*) FROM schema_migrations WHERE version='2026_07_30_stage54_cold_chain_device_integrity')<>1
     OR (SELECT COUNT(*) FROM schema_migrations WHERE version='2026_07_30_stage55_fleet_runtime_route_contract')<>1
     OR (SELECT COUNT(*) FROM schema_migrations WHERE version='2026_07_30_stage56_asset_type_integrity')<>1
     OR (SELECT COUNT(*) FROM schema_migrations WHERE version='2026_07_30_stage57_workforce_schedule_tenant_integrity')<>1 THEN
    RAISE EXCEPTION 'Fleet Stage54/55/56/57 migration ledger missing or duplicated';
  END IF;
  IF (SELECT COUNT(*) FROM pg_policies
      WHERE schemaname='public' AND tablename='authorization_decision_logs')<>2
     OR NOT has_table_privilege('opstrax_app','public.authorization_decision_logs','SELECT')
     OR NOT has_table_privilege('opstrax_app','public.authorization_decision_logs','INSERT')
     OR has_table_privilege('opstrax_app','public.authorization_decision_logs','UPDATE')
     OR has_table_privilege('opstrax_app','public.authorization_decision_logs','DELETE')
     OR NOT has_sequence_privilege('opstrax_app','public.authorization_decision_logs_id_seq','USAGE')
     OR NOT has_sequence_privilege('opstrax_app','public.authorization_decision_logs_id_seq','SELECT')
     OR has_sequence_privilege('opstrax_app','public.authorization_decision_logs_id_seq','UPDATE') THEN
    RAISE EXCEPTION 'Fleet Stage55 authorization evidence contract drifted';
  END IF;
  IF to_regclass('public.workforce_schedules') IS NULL
     OR NOT EXISTS (SELECT 1 FROM information_schema.columns WHERE table_schema='public'
                    AND table_name='workforce_schedules' AND column_name='company_id'
                    AND data_type='bigint' AND is_nullable='NO')
     OR NOT EXISTS (SELECT 1 FROM information_schema.columns WHERE table_schema='public'
                    AND table_name='workforce_schedules' AND column_name='branch_id'
                    AND data_type='bigint' AND is_nullable='YES')
     OR NOT EXISTS (SELECT 1 FROM pg_class WHERE oid='public.workforce_schedules'::regclass
                    AND relrowsecurity AND relforcerowsecurity)
     OR (SELECT COUNT(*) FROM pg_policies WHERE schemaname='public' AND tablename='workforce_schedules')<>2
     OR NOT has_table_privilege('opstrax_app','public.workforce_schedules','SELECT')
     OR NOT has_table_privilege('opstrax_app','public.workforce_schedules','INSERT')
     OR NOT has_table_privilege('opstrax_app','public.workforce_schedules','UPDATE')
     OR has_table_privilege('opstrax_app','public.workforce_schedules','DELETE')
     OR has_table_privilege('opstrax_app','public.workforce_schedules','TRUNCATE')
     OR has_table_privilege('opstrax_app','public.workforce_schedules','REFERENCES')
     OR has_table_privilege('opstrax_app','public.workforce_schedules','TRIGGER')
     OR NOT has_sequence_privilege('opstrax_app','public.workforce_schedules_id_seq','USAGE')
     OR NOT has_sequence_privilege('opstrax_app','public.workforce_schedules_id_seq','SELECT')
     OR has_sequence_privilege('opstrax_app','public.workforce_schedules_id_seq','UPDATE') THEN
    RAISE EXCEPTION 'Fleet Stage57 workforce schema/RLS/grant contract drifted';
  END IF;
  IF NOT EXISTS (SELECT 1 FROM pg_constraint
      WHERE conrelid='public.workforce_schedules'::regclass
        AND conname='fk_workforce_schedules_tenant_driver' AND convalidated
        AND pg_get_constraintdef(oid)='FOREIGN KEY (company_id, driver_id) REFERENCES drivers(company_id, id)')
     OR EXISTS (SELECT 1 FROM workforce_schedules ws LEFT JOIN drivers d ON d.id=ws.driver_id
                WHERE d.id IS NULL OR ws.company_id IS DISTINCT FROM d.company_id
                  OR ws.branch_id IS DISTINCT FROM d.branch_id) THEN
    RAISE EXCEPTION 'Fleet Stage57 workforce tenant/driver ownership contract drifted';
  END IF;
  SELECT array_agg(expected.name ORDER BY expected.name) INTO invalid
  FROM (VALUES
    ('uq_drivers_company_id_id','drivers',true,2,'company_id','id',''),
    ('uq_workforce_schedules_tenant_driver_week','workforce_schedules',true,3,'company_id','driver_id','week_start'),
    ('idx_workforce_schedules_tenant_week','workforce_schedules',false,2,'company_id','week_start','')
  ) expected(name,table_name,is_unique,key_count,key1,key2,key3)
  LEFT JOIN pg_class idx ON idx.oid=to_regclass('public.'||expected.name)
  LEFT JOIN pg_index i ON i.indexrelid=idx.oid
  WHERE idx.oid IS NULL OR i.indisunique<>expected.is_unique OR NOT i.indisvalid OR NOT i.indisready
    OR i.indrelid<>to_regclass('public.'||expected.table_name)
    OR i.indnkeyatts<>expected.key_count OR i.indnatts<>expected.key_count
    OR pg_get_indexdef(i.indexrelid,1,true)<>expected.key1
    OR pg_get_indexdef(i.indexrelid,2,true)<>expected.key2
    OR (expected.key_count=3 AND pg_get_indexdef(i.indexrelid,3,true)<>expected.key3)
    OR i.indpred IS NOT NULL;
  IF COALESCE(cardinality(invalid),0)>0 THEN
    RAISE EXCEPTION 'Fleet Stage57 workforce ownership indexes missing, invalid, or drifted: %',invalid;
  END IF;
END
$verify$;
SQL
echo "Fleet integrity: Stage54/55/56/57 exact indexes, authorization/workforce evidence, and ledgers verified"
echo "Verifying terminal Stage58/59 non-forgeable tenant boundary…"
psql "$NEON_PG_URI" -v ON_ERROR_STOP=1 <<'SQL'
DO $verify_stage58$
BEGIN
  IF (SELECT count(*) FROM schema_migrations WHERE version='2026_07_31_stage58_nonforgeable_tenant_ticket')<>1 THEN
    RAISE EXCEPTION 'Stage58 migration ledger missing or duplicated';
  END IF;
  IF to_regclass('public.platform_data_protection_keys') IS NULL THEN
    RAISE EXCEPTION 'Stage59 Data Protection key-ring table missing';
  END IF;
  IF (SELECT count(*) FROM pg_attribute a
        WHERE a.attrelid=to_regclass('public.platform_data_protection_keys')
          AND a.attnum>0 AND NOT a.attisdropped)<>4
     OR EXISTS (SELECT 1 FROM (VALUES
          ('id','bigint',true,'a',''),
          ('friendly_name','character varying(256)',true,'',''),
          ('xml_payload','text',true,'',''),
          ('created_at','timestamp with time zone',true,'','clock_timestamp()')
        ) expected(column_name,data_type,not_null,identity_kind,column_default)
        LEFT JOIN pg_attribute actual
          ON actual.attrelid=to_regclass('public.platform_data_protection_keys')
         AND actual.attname=expected.column_name AND actual.attnum>0 AND NOT actual.attisdropped
        LEFT JOIN pg_attrdef actual_default
          ON actual_default.adrelid=actual.attrelid AND actual_default.adnum=actual.attnum
        WHERE actual.attname IS NULL
          OR format_type(actual.atttypid,actual.atttypmod)<>expected.data_type
          OR actual.attnotnull<>expected.not_null
          OR actual.attidentity::text<>expected.identity_kind
          OR COALESCE(pg_get_expr(actual_default.adbin,actual_default.adrelid),'')<>expected.column_default)
     OR pg_get_serial_sequence('public.platform_data_protection_keys','id')<>'public.platform_data_protection_keys_id_seq'
     OR (SELECT count(*) FROM pg_constraint c
          WHERE c.conrelid=to_regclass('public.platform_data_protection_keys'))<>3
     OR NOT EXISTS (SELECT 1 FROM pg_constraint c
          WHERE c.conrelid=to_regclass('public.platform_data_protection_keys') AND c.contype='p'
            AND c.convalidated AND c.conkey=ARRAY[(SELECT attnum FROM pg_attribute
              WHERE attrelid=to_regclass('public.platform_data_protection_keys') AND attname='id')]::smallint[])
     OR NOT EXISTS (SELECT 1 FROM pg_constraint c
          WHERE c.conrelid=to_regclass('public.platform_data_protection_keys') AND c.contype='u'
            AND c.convalidated AND c.conkey=ARRAY[(SELECT attnum FROM pg_attribute
              WHERE attrelid=to_regclass('public.platform_data_protection_keys') AND attname='friendly_name')]::smallint[])
     OR NOT EXISTS (SELECT 1 FROM pg_constraint c
          WHERE c.conrelid=to_regclass('public.platform_data_protection_keys') AND c.contype='c'
            AND c.convalidated
            AND regexp_replace(pg_get_constraintdef(c.oid,true),'\s+','','g')='CHECK(octet_length(xml_payload)<=1048576)') THEN
    RAISE EXCEPTION 'Stage59 Data Protection key-ring exact schema contract unsafe';
  END IF;
  IF (SELECT count(*) FROM schema_migrations WHERE version='2026_07_31_stage59_data_protection_key_ring')<>1
     OR (SELECT count(*) FROM pg_policies WHERE schemaname='public' AND tablename='platform_data_protection_keys')<>1
     OR NOT has_table_privilege('opstrax_system','platform_data_protection_keys','SELECT')
     OR NOT has_table_privilege('opstrax_system','platform_data_protection_keys','INSERT')
     OR has_table_privilege('opstrax_system','platform_data_protection_keys','UPDATE')
     OR has_table_privilege('opstrax_system','platform_data_protection_keys','DELETE')
     OR has_table_privilege('opstrax_app','platform_data_protection_keys','SELECT')
     OR has_table_privilege('opstrax_app','platform_data_protection_keys','INSERT')
     OR has_table_privilege('opstrax_app','platform_data_protection_keys','UPDATE')
     OR has_table_privilege('opstrax_app','platform_data_protection_keys','DELETE')
     OR has_sequence_privilege('opstrax_app','platform_data_protection_keys_id_seq','USAGE')
     OR has_sequence_privilege('opstrax_app','platform_data_protection_keys_id_seq','SELECT')
     OR has_sequence_privilege('opstrax_app','platform_data_protection_keys_id_seq','UPDATE')
     OR NOT has_sequence_privilege('opstrax_system','platform_data_protection_keys_id_seq','USAGE')
     OR NOT has_sequence_privilege('opstrax_system','platform_data_protection_keys_id_seq','SELECT')
     OR has_sequence_privilege('opstrax_system','platform_data_protection_keys_id_seq','UPDATE') THEN
    RAISE EXCEPTION 'Stage59 Data Protection key-ring contract unsafe';
  END IF;
  IF EXISTS(SELECT 1 FROM pg_policies WHERE schemaname='public' AND roles='{public}'::name[]) THEN
    RAISE EXCEPTION 'Stage58 left PUBLIC RLS policies';
  END IF;
  IF has_function_privilege('opstrax_app','opstrax_security.issue_tenant_ticket(bigint,integer,bigint,integer)','EXECUTE')
     OR NOT has_function_privilege('opstrax_system','opstrax_security.issue_tenant_ticket(bigint,integer,bigint,integer)','EXECUTE')
     OR NOT has_function_privilege('opstrax_app','opstrax_security.current_tenant_id()','EXECUTE') THEN
    RAISE EXCEPTION 'Stage58 ticket function grants unsafe';
  END IF;
  -- Managed Postgres (Neon/RDS/Cloud SQL) auto-grants the database owner ADMIN membership
  -- in every role created under it, recorded with the provider's internal superuser as
  -- grantor (Neon: cloud_admin), so the customer-visible owner CANNOT revoke it — REVOKE is
  -- a no-op warning. That membership grants the database owner nothing it does not already
  -- hold, so it is not an escalation edge. Mirrors the same relaxation in Stage 58 itself:
  -- the runtime identity must still never be a member of anything, and no principal other
  -- than the database owner may SET ROLE into it.
  IF EXISTS(SELECT 1 FROM pg_auth_members m JOIN pg_roles r ON r.oid=m.member
            WHERE r.rolname IN ('opstrax_app','opstrax_system'))
     OR EXISTS(SELECT 1 FROM pg_auth_members m JOIN pg_roles r ON r.oid=m.roleid
               WHERE r.rolname IN ('opstrax_app','opstrax_system')
                 AND m.member<>(SELECT d.datdba FROM pg_database d WHERE d.datname=current_database())) THEN
    RAISE EXCEPTION 'Stage58 runtime role membership graph unsafe';
  END IF;
  IF NOT EXISTS(SELECT 1 FROM pg_policies WHERE schemaname='public' AND tablename='vehicles'
      AND policyname='tenant_ticket_app' AND roles='{opstrax_app}'::name[])
     OR NOT EXISTS(SELECT 1 FROM pg_policies WHERE schemaname='public' AND tablename='vehicles'
      AND policyname='system_control_plane' AND roles='{opstrax_system}'::name[]) THEN
    RAISE EXCEPTION 'Stage58 canonical role-targeted policies missing';
  END IF;
  IF has_table_privilege('opstrax_app','platform_admins','SELECT')
     OR has_any_column_privilege('opstrax_app','platform_admins','SELECT,INSERT,UPDATE,REFERENCES')
     OR has_table_privilege('opstrax_app','service_heartbeats','SELECT')
     OR has_table_privilege('opstrax_app','telemetry_nonces','SELECT') THEN
    RAISE EXCEPTION 'Stage58 app retains control-plane/system-ledger access';
  END IF;
  IF NOT has_table_privilege('opstrax_system','platform_invoices','SELECT')
     OR NOT has_table_privilege('opstrax_system','platform_invoices','INSERT')
     OR NOT has_table_privilege('opstrax_system','platform_invoices','UPDATE')
     OR NOT has_table_privilege('opstrax_system','platform_invoices','DELETE')
     OR NOT has_table_privilege('opstrax_system','gps_gateway_replay','SELECT')
     OR NOT has_table_privilege('opstrax_system','gps_gateway_replay','INSERT')
     OR NOT has_table_privilege('opstrax_system','gps_gateway_replay','UPDATE')
     OR NOT has_table_privilege('opstrax_system','gps_gateway_replay','DELETE') THEN
    RAISE EXCEPTION 'Stage58 system excluded-table grants missing';
  END IF;
END
$verify_stage58$;
SQL
echo "Stage58: signed transaction tickets, exact roles/policies/grants, and control-plane separation verified"
echo "Reapplying Stage67 least-privilege device credential boundary after terminal reconciliation…"
psql "$NEON_PG_URI" -v ON_ERROR_STOP=1 -q -f database/migrations/2026_08_02_stage67_telematics_diagnostics_integrity.sql
psql "$NEON_PG_URI" -v ON_ERROR_STOP=1 -q <<'SQL'
DO $verify_stage67_credentials$
BEGIN
  IF has_column_privilege('opstrax_app','eld_devices','api_key_hash','SELECT')
     OR has_column_privilege('opstrax_app','eld_devices','api_key_previous_hash','SELECT')
     OR has_column_privilege('opstrax_app','eld_devices','hmac_secret','SELECT')
     OR has_column_privilege('opstrax_app','eld_devices','hmac_secret_encrypted','SELECT')
     OR has_column_privilege('opstrax_app','eld_devices','hmac_previous_secret_encrypted','SELECT')
     OR NOT has_column_privilege('opstrax_app','eld_devices','device_serial','SELECT') THEN
    RAISE EXCEPTION 'Stage67 post-terminal device credential boundary unsafe';
  END IF;
END
$verify_stage67_credentials$;
SQL
echo "Applying terminal Stage76 telemetry default-deny/runtime ACL reconciliation…"
psql "$NEON_PG_URI" -v ON_ERROR_STOP=1 -q -f database/migrations/2026_08_11_stage76_telematics_security_hardening.sql
psql "$NEON_PG_URI" -v ON_ERROR_STOP=1 -q <<'SQL'
DO $verify_stage76_terminal$
BEGIN
  IF (SELECT count(*) FROM schema_migrations
      WHERE version='2026_08_11_stage76_telematics_security_hardening')<>1
     OR has_table_privilege('opstrax_app','eld_devices','SELECT')
     OR has_column_privilege('opstrax_app','eld_devices','api_key_hash','SELECT')
     OR has_column_privilege('opstrax_app','eld_devices','hmac_secret_encrypted','SELECT')
     OR NOT has_column_privilege('opstrax_app','eld_devices','device_serial','SELECT') THEN
    RAISE EXCEPTION 'Stage76 is not the effective terminal telemetry boundary';
  END IF;
  IF EXISTS (
    SELECT 1 FROM (VALUES ('module_packages'),('usage_meters'),('pricing_rules')) refs(table_name)
    WHERE NOT has_table_privilege('opstrax_app',table_name,'SELECT')
       OR has_table_privilege('opstrax_app',table_name,'INSERT')
       OR has_table_privilege('opstrax_app',table_name,'UPDATE')
       OR has_table_privilege('opstrax_app',table_name,'DELETE')
       OR NOT has_table_privilege('opstrax_system',table_name,'SELECT')
       OR NOT has_table_privilege('opstrax_system',table_name,'INSERT')
       OR NOT has_table_privilege('opstrax_system',table_name,'UPDATE')
       OR NOT has_table_privilege('opstrax_system',table_name,'DELETE')
  ) OR EXISTS (
    SELECT 1 FROM (VALUES ('module_packages_id_seq'),('usage_meters_id_seq'),('pricing_rules_id_seq')) refs(sequence_name)
    WHERE has_sequence_privilege('opstrax_app',sequence_name,'USAGE')
       OR NOT has_sequence_privilege('opstrax_system',sequence_name,'USAGE')
  ) OR NOT COALESCE((SELECT c.relrowsecurity AND c.relforcerowsecurity
                       FROM pg_class c WHERE c.oid=to_regclass('public.usage_events')),false)
       OR NOT has_table_privilege('opstrax_app','usage_events','SELECT')
       OR NOT has_table_privilege('opstrax_app','usage_events','INSERT')
       OR has_table_privilege('opstrax_app','usage_events','UPDATE')
       OR has_table_privilege('opstrax_app','usage_events','DELETE')
       OR NOT has_table_privilege('opstrax_system','usage_events','SELECT')
       OR NOT has_table_privilege('opstrax_system','usage_events','INSERT')
       OR NOT has_table_privilege('opstrax_system','usage_events','UPDATE')
       OR NOT has_table_privilege('opstrax_system','usage_events','DELETE')
  OR EXISTS (
    SELECT 1 FROM (VALUES ('usage_counters'),('tenant_contract_overrides')) tenant_tables(table_name)
    JOIN pg_class c ON c.oid=to_regclass('public.'||tenant_tables.table_name)
    WHERE NOT c.relrowsecurity OR NOT c.relforcerowsecurity
       OR NOT has_table_privilege('opstrax_app',table_name,'SELECT')
       OR NOT has_table_privilege('opstrax_app',table_name,'INSERT')
       OR NOT has_table_privilege('opstrax_app',table_name,'UPDATE')
       OR NOT has_table_privilege('opstrax_app',table_name,'DELETE')
       OR NOT has_table_privilege('opstrax_system',table_name,'SELECT')
       OR NOT has_table_privilege('opstrax_system',table_name,'INSERT')
       OR NOT has_table_privilege('opstrax_system',table_name,'UPDATE')
       OR NOT has_table_privilege('opstrax_system',table_name,'DELETE')
  ) OR (SELECT COUNT(*) FROM pg_policies WHERE schemaname='public'
          AND tablename IN ('usage_events','usage_counters','tenant_contract_overrides')
          AND policyname IN ('tenant_ticket_app','system_control_plane'))<>6 THEN
    RAISE EXCEPTION 'Stage80 revenue/market catalog ACL and tenant-RLS contract is not terminally reconciled';
  END IF;
END
$verify_stage76_terminal$;
SQL
echo
echo "Ledger:"
psql "$NEON_PG_URI" -v ON_ERROR_STOP=1 -tA -c "SELECT version FROM schema_migrations ORDER BY version"
echo
echo "✅ Neon predeploy chain is prepared with Stage76 terminal; deployment still requires exact-SHA release evidence."
