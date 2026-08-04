\set ON_ERROR_STOP on

-- Database-only restore verifier. It intentionally does not claim application,
-- object-storage, alerting, or cutover recovery. Those are separate release gates.

SELECT id AS pilot_company_id
FROM companies
WHERE company_code = :'pilot_company_code'
  AND LOWER(status) IN ('active','trial')
LIMIT 1
\gset

\if :{?pilot_company_id}
\else
  \echo 'Safety restore verification failed: active/trial pilot tenant not found.'
  SELECT 1/0 AS safety_restore_verification_failed;
\endif

SELECT (
  to_regclass('public.incidents') IS NOT NULL
  AND to_regclass('public.incident_evidence') IS NOT NULL
  AND to_regclass('public.coaching_tasks') IS NOT NULL
  AND to_regclass('public.dvir_reports') IS NOT NULL
  AND to_regclass('public.dvir_defects') IS NOT NULL
  AND to_regclass('public.hos_logs') IS NOT NULL
  AND to_regclass('public.hos_certifications') IS NOT NULL
  AND to_regclass('public.demo_fixture_versions') IS NOT NULL
  AND to_regclass('public.telemetry_stream_ticket_nonces_id_seq') IS NOT NULL
  AND to_regclass('public.uq_incidents_company_idempotency') IS NOT NULL
  AND to_regclass('public.uq_coaching_tasks_company_idempotency') IS NOT NULL
  AND to_regclass('public.uq_dvir_reports_company_idempotency') IS NOT NULL
  AND EXISTS (SELECT 1 FROM information_schema.columns
    WHERE table_schema='public' AND table_name='hos_logs' AND column_name='notes')
  AND (SELECT count(*) FROM information_schema.columns
    WHERE table_schema='public' AND table_name='hos_clocks'
      AND column_name IN ('break_needed_at','reset_at','updated_at'))=3
) AS safety_schema_ok
\gset

\if :safety_schema_ok
\else
  \echo 'Safety restore verification failed: required Safety tables/indexes are absent.'
  SELECT 1/0 AS safety_restore_verification_failed;
\endif

SELECT (
  (SELECT count(*) FROM schema_migrations WHERE version='2026_07_22_stage47_detention_recovery')=1
  AND
  (SELECT count(*) FROM schema_migrations WHERE version='2026_07_31_stage58_nonforgeable_tenant_ticket')=1
  AND (SELECT count(*) FROM schema_migrations WHERE version='2026_07_31_stage59_data_protection_key_ring')=1
  AND (SELECT count(*) FROM schema_migrations WHERE version='2026_08_01_stage65_safety_pilot')=1
  AND (SELECT count(*) FROM schema_migrations WHERE version='2026_08_02_stage66_telematics_pilot')=1
  AND (SELECT count(*) FROM schema_migrations WHERE version='2026_08_02_stage67_telematics_diagnostics_integrity')=1
  AND (SELECT count(*) FROM schema_migrations WHERE version='2026_08_02_stage68_entitlement_policy_mode')=1
  AND (SELECT count(*) FROM schema_migrations WHERE version='2026_08_02_stage69_market_pack_control_hardening')=1
  AND (SELECT count(*) FROM schema_migrations WHERE version='2026_08_02_stage70_hos_pilot_schema_reconciliation')=1
  AND (SELECT count(*) FROM schema_migrations WHERE version='2026_08_02_stage71_coaching_evidence_reconciliation')=1
  AND (SELECT count(*) FROM schema_migrations WHERE version='2026_08_02_stage72_hos_offboarding_immutability_reconciliation')=1
  AND (SELECT count(*) FROM schema_migrations WHERE version='2026_08_02_stage73_hos_offboarding_null_fail_closed')=1
  AND (SELECT count(*) FROM schema_migrations WHERE version='2026_08_02_stage74_retention_policy_production_contract')=1
  AND (SELECT count(*) FROM schema_migrations WHERE version='2026_08_02_stage75_bounded_support_access')=1
  AND to_regclass('public.data_retention_policies') IS NOT NULL
  AND EXISTS (SELECT 1 FROM pg_constraint
    WHERE conrelid='public.data_retention_policies'::regclass
      AND conname='ck_data_retention_policy_minimums')
  AND EXISTS (SELECT 1 FROM information_schema.columns
    WHERE table_schema='public' AND table_name='coaching_tasks' AND column_name='acknowledged_note')
  AND EXISTS (SELECT 1 FROM pg_constraint
    WHERE conrelid='public.coaching_tasks'::regclass
      AND conname='ck_stage71_coaching_acknowledged_note_length')
  AND position('COALESCE(current_setting(''opstrax.offboarding''' in pg_get_functiondef('stage65_prevent_certified_hos_log_delete()'::regprocedure))>0
  AND position('opstrax_system' in pg_get_functiondef('stage65_prevent_certified_hos_log_delete()'::regprocedure))>0
  AND position('COALESCE(current_setting(''opstrax.offboarding''' in pg_get_functiondef('stage65_guard_hos_certification_snapshot()'::regprocedure))>0
  AND position('opstrax_system' in pg_get_functiondef('stage65_guard_hos_certification_snapshot()'::regprocedure))>0
  AND position('COALESCE(current_setting(''opstrax.offboarding''' in pg_get_functiondef('detention_evidence_immutable()'::regprocedure))>0
  AND position('opstrax_system' in pg_get_functiondef('detention_evidence_immutable()'::regprocedure))>0
  AND EXISTS (
    SELECT 1 FROM pg_constraint
    WHERE conname='ck_tenant_market_packs_status'
      AND conrelid='public.tenant_market_packs'::regclass
      AND contype='c' AND convalidated)
  AND NOT EXISTS (SELECT 1 FROM tenant_market_packs
    WHERE status IS NULL OR status NOT IN ('active','disabled'))
  AND (SELECT relrowsecurity AND relforcerowsecurity
       FROM pg_class WHERE oid='public.demo_fixture_versions'::regclass)
  AND (SELECT count(*) FROM pg_policies
    WHERE schemaname='public' AND tablename='demo_fixture_versions')=2
  AND EXISTS (SELECT 1 FROM pg_policies
    WHERE schemaname='public' AND tablename='demo_fixture_versions'
      AND policyname='tenant_ticket_app' AND roles='{opstrax_app}'::name[]
      AND cmd='ALL' AND qual LIKE '%opstrax_security.current_tenant_id()%'
      AND with_check=qual)
  AND EXISTS (SELECT 1 FROM pg_policies
    WHERE schemaname='public' AND tablename='demo_fixture_versions'
      AND policyname='system_control_plane' AND roles='{opstrax_system}'::name[]
      AND cmd='ALL' AND qual='true' AND with_check='true')
  AND NOT has_table_privilege('opstrax_app','demo_fixture_versions','INSERT')
  AND NOT has_table_privilege('opstrax_app','demo_fixture_versions','UPDATE')
  AND NOT has_table_privilege('opstrax_app','demo_fixture_versions','DELETE')
  AND has_table_privilege('opstrax_app','demo_fixture_versions','SELECT')
  AND has_table_privilege('opstrax_system','demo_fixture_versions','SELECT,INSERT,UPDATE,DELETE')
  AND NOT EXISTS (SELECT 1 FROM pg_policies WHERE schemaname='public' AND roles='{public}'::name[])
) AS security_contract_ok
\gset

\if :security_contract_ok
\else
  \echo 'Safety restore verification failed: migration/RLS security contract is incomplete.'
  SELECT 1/0 AS safety_restore_verification_failed;
\endif

SELECT (
  (SELECT count(*) FROM demo_fixture_versions
    WHERE company_id=:pilot_company_id AND fixture_key='safety-pilot' AND fixture_version=7)=1
  AND (SELECT count(*) FROM companies
    WHERE id=:pilot_company_id AND entitlement_policy_mode='package_allowlist')=1
  AND (SELECT count(*) FROM tenant_entitlements
    WHERE company_id=:pilot_company_id AND enabled=true AND tier='pilot' AND source='fixture'
      AND module_key = ANY(ARRAY['safety','maintenance','dispatch','telematics','crm','customer_portal','reports','compliance','integrations']))=9
  AND (SELECT count(*) FROM branches WHERE company_id=:pilot_company_id AND status='Active')>=2
  AND (SELECT count(*) FROM users WHERE company_id=:pilot_company_id AND role_name='Safety Manager' AND status='Active')>=1
  AND (SELECT count(*) FROM users WHERE company_id=:pilot_company_id AND role_name='Driver' AND status='Active')>=1
  AND (SELECT count(*) FROM users WHERE company_id=:pilot_company_id AND role_name='Dispatcher' AND status='Active')>=1
  AND (SELECT count(*) FROM users WHERE company_id=:pilot_company_id AND role_name='Maintenance Manager' AND status='Active')>=1
  AND (SELECT count(*) FROM incidents WHERE company_id=:pilot_company_id)>=1
  AND (SELECT count(*) FROM incident_evidence e
    JOIN incidents i ON i.id=e.incident_id AND i.company_id=e.company_id
    WHERE e.company_id=:pilot_company_id AND i.incident_number='MER-INC-1'
      AND e.evidence_title='Synthetic harsh-braking telemetry metadata'
      AND e.evidence_url IS NULL AND e.content_hash ~ '^[0-9a-fA-F]{64}$'
      AND e.evidence_json @> '{"synthetic":true,"verificationStatus":"not_verified","custodyStatus":"not_managed","retrievalStatus":"not_available"}'::jsonb
      AND NOT (e.evidence_json ? 'verified'))=1
  AND (SELECT count(*) FROM coaching_tasks WHERE company_id=:pilot_company_id)>=1
  AND (SELECT count(*) FROM dvir_reports WHERE company_id=:pilot_company_id)>=1
  AND (SELECT count(*) FROM hos_logs WHERE company_id=:pilot_company_id
    AND source='demo' AND source_event_id='safety-pilot-hos-1')=1
  AND (SELECT count(*) FROM hos_clocks WHERE company_id=:pilot_company_id
    AND status='Warning' AND drive_time_remaining_minutes=165)>=1
  AND (SELECT count(*) FROM eld_devices WHERE company_id=:pilot_company_id
    AND device_serial LIKE 'MER-ELD-%' AND branch_id IS NOT NULL
    AND status='Diagnostic' AND provider_sync_status='Healthy'
    AND api_key_hash IS NULL AND hmac_secret_encrypted IS NULL)=1
  AND (SELECT count(*) FROM dvir_reports r
    JOIN vehicles v ON v.id=r.vehicle_id AND v.company_id=r.company_id
    WHERE r.company_id=:pilot_company_id AND r.report_number='MER-DVIR-1'
      AND v.out_of_service=true AND v.availability_status='out_of_service')=1
) AS fixture_contract_ok
\gset

\if :fixture_contract_ok
\else
  \echo 'Safety restore verification failed: pilot fixture/persona minimums are incomplete.'
  SELECT 1/0 AS safety_restore_verification_failed;
\endif

SELECT (
  NOT EXISTS (
    SELECT 1 FROM incident_evidence e
    LEFT JOIN incidents i ON i.id=e.incident_id AND i.company_id=e.company_id
    WHERE e.company_id=:pilot_company_id AND i.id IS NULL)
  AND NOT EXISTS (
    SELECT 1 FROM coaching_tasks t
    LEFT JOIN drivers d ON d.id=t.driver_id AND d.company_id=t.company_id
    WHERE t.company_id=:pilot_company_id AND d.id IS NULL)
  AND NOT EXISTS (
    SELECT 1 FROM dvir_defects x
    LEFT JOIN dvir_reports r ON r.id=x.dvir_report_id AND r.company_id=x.company_id
    WHERE x.company_id=:pilot_company_id AND r.id IS NULL)
) AS relational_integrity_ok
\gset

\if :relational_integrity_ok
\else
  \echo 'Safety restore verification failed: orphaned Safety relationships detected.'
  SELECT 1/0 AS safety_restore_verification_failed;
\endif

SELECT
  :pilot_company_id AS pilot_company_id,
  (SELECT count(*) FROM branches WHERE company_id=:pilot_company_id) AS branches,
  (SELECT count(*) FROM users WHERE company_id=:pilot_company_id) AS users,
  (SELECT count(*) FROM incidents WHERE company_id=:pilot_company_id) AS incidents,
  (SELECT count(*) FROM incident_evidence WHERE company_id=:pilot_company_id) AS incident_evidence,
  (SELECT count(*) FROM coaching_tasks WHERE company_id=:pilot_company_id) AS coaching_tasks,
  (SELECT count(*) FROM dvir_reports WHERE company_id=:pilot_company_id) AS dvir_reports,
  (SELECT count(*) FROM dvir_defects WHERE company_id=:pilot_company_id) AS dvir_defects;

\echo 'Safety pilot restored database contract passed. Application/object/cutover verification remains required.'
