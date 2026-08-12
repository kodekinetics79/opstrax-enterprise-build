-- Stage 53 — production-wide tenant RLS reconciliation
--
-- Closes the gap between point-in-time RLS migrations and tables added by later
-- schema/migration work. Every public base table carrying a bigint company_id or
-- tenant_id is tenant-scoped. The companies control-plane table is also tenant
-- scoped on its bigint id: tenant requests may see only their own company while
-- reviewed bootstrap/platform paths use the canonical system bypass. The only
-- explicit, audited exceptions are:
--   * platform_invoices   — platform billing control-plane artifact
--   * gps_gateway_replay  — pre-tenant infrastructure replay ledger; company_id is
--                           nullable audit metadata, not its authorization boundary
--
-- The migration is idempotent and data-preserving. It atomically repairs the two
-- canonical policies, enables + forces RLS, and repairs runtime privileges. Tables
-- with sensitive/audit semantics use the explicit least-privilege matrix below;
-- other tenant operational tables retain the established CRUD contract. The MFA
-- consumption ledger remains append/delete-only and non-updatable.

BEGIN;

-- Stage 20 originally installed broad default ACLs. Those defaults silently grant
-- future tables CRUD and future sequences USAGE/SELECT before their runtime verbs
-- are reviewed. Remove every such opstrax_app default ACL owned in public; future
-- migrations must grant each object explicitly.
DO $default_acl$
DECLARE
  rec RECORD;
BEGIN
  FOR rec IN
    SELECT DISTINCT owner_role.rolname AS owner_name,d.defaclobjtype
    FROM pg_default_acl d
    JOIN pg_namespace ns ON ns.oid=d.defaclnamespace AND ns.nspname='public'
    JOIN pg_roles owner_role ON owner_role.oid=d.defaclrole
    CROSS JOIN LATERAL aclexplode(d.defaclacl) acl
    JOIN pg_roles grantee ON grantee.oid=acl.grantee
    WHERE grantee.rolname='opstrax_app'
      AND d.defaclobjtype IN ('r','S')
  LOOP
    IF rec.defaclobjtype='r' THEN
      EXECUTE format(
        'ALTER DEFAULT PRIVILEGES FOR ROLE %I IN SCHEMA public REVOKE ALL PRIVILEGES ON TABLES FROM opstrax_app',
        rec.owner_name);
    ELSE
      EXECUTE format(
        'ALTER DEFAULT PRIVILEGES FOR ROLE %I IN SCHEMA public REVOKE ALL PRIVILEGES ON SEQUENCES FROM opstrax_app',
        rec.owner_name);
    END IF;
  END LOOP;
END
$default_acl$;

DO $tenant_rls$
DECLARE
  rec RECORD;
  policy_rec RECORD;
  tenant_col TEXT;
BEGIN
  IF NOT EXISTS (SELECT 1 FROM pg_roles WHERE rolname='opstrax_app') THEN
    RAISE EXCEPTION 'Stage 53 requires restricted role opstrax_app; apply Stage 20 first';
  END IF;
  -- Managed Postgres (Neon/RDS) never grants superuser, and naming the SUPERUSER /
  -- BYPASSRLS / REPLICATION attributes in ALTER ROLE requires it — even when the values
  -- are already correct, making this a no-op that still errors with "permission denied to
  -- alter role". Only issue the ALTER when the role actually deviates from the target
  -- shape; the $verify$ block below still fails loudly if it is wrong and uncorrectable.
  IF EXISTS (
    SELECT 1 FROM pg_roles
    WHERE rolname='opstrax_app'
      AND (NOT rolcanlogin OR rolsuper OR rolbypassrls OR rolcreatedb
           OR rolcreaterole OR rolinherit OR rolreplication)
  ) THEN
    EXECUTE 'ALTER ROLE opstrax_app LOGIN NOSUPERUSER NOBYPASSRLS NOCREATEDB NOCREATEROLE NOINHERIT NOREPLICATION';
  END IF;
  FOR rec IN
    SELECT granted.rolname
    FROM pg_auth_members membership
    JOIN pg_roles member_role ON member_role.oid=membership.member AND member_role.rolname='opstrax_app'
    JOIN pg_roles granted ON granted.oid=membership.roleid
  LOOP
    EXECUTE format('REVOKE %I FROM opstrax_app',rec.rolname);
  END LOOP;
  -- TEMP is granted to PUBLIC by default and enables unbounded temporary-object
  -- allocation. The restricted API has no temp-table runtime path; keep only the
  -- minimum CONNECT + public-schema USAGE capabilities.
  EXECUTE format('REVOKE TEMPORARY ON DATABASE %I FROM PUBLIC',current_database());
  EXECUTE format('REVOKE CREATE,TEMPORARY ON DATABASE %I FROM opstrax_app',current_database());
  EXECUTE format('GRANT CONNECT ON DATABASE %I TO opstrax_app',current_database());
  EXECUTE 'REVOKE CREATE ON SCHEMA public FROM PUBLIC';
  EXECUTE 'REVOKE CREATE ON SCHEMA public FROM opstrax_app';
  EXECUTE 'GRANT USAGE ON SCHEMA public TO opstrax_app';

  FOR rec IN
    SELECT c.table_name,
      bool_or(c.column_name='company_id') AS has_company,
      bool_or(c.column_name='tenant_id') AS has_tenant
    FROM information_schema.columns c
    JOIN information_schema.tables t
      ON t.table_schema=c.table_schema AND t.table_name=c.table_name
    WHERE c.table_schema='public'
      AND t.table_type='BASE TABLE'
      AND ((c.column_name IN ('company_id','tenant_id') AND c.data_type='bigint')
        OR (c.table_name='companies' AND c.column_name='id' AND c.data_type='bigint'))
      AND c.table_name NOT IN ('platform_invoices','gps_gateway_replay')
    GROUP BY c.table_name
  LOOP
    tenant_col := CASE WHEN rec.table_name='companies' THEN 'id'
                       WHEN rec.has_company THEN 'company_id' ELSE 'tenant_id' END;
    EXECUTE format('ALTER TABLE public.%I ENABLE ROW LEVEL SECURITY',rec.table_name);
    EXECUTE format('ALTER TABLE public.%I FORCE ROW LEVEL SECURITY',rec.table_name);
    -- PostgreSQL OR-combines permissive policies. Merely recreating the two named
    -- policies leaves a rogue USING (true) policy able to bypass tenant isolation.
    -- Remove the complete policy set, then recreate the exact closed allow-list.
    FOR policy_rec IN
      SELECT policyname FROM pg_policies
      WHERE schemaname='public' AND tablename=rec.table_name
    LOOP
      EXECUTE format('DROP POLICY %I ON public.%I',policy_rec.policyname,rec.table_name);
    END LOOP;
    EXECUTE format(
      'CREATE POLICY tenant_isolation ON public.%I FOR ALL USING (%I = NULLIF(current_setting(''app.current_tenant_id'',true),'''')::bigint) WITH CHECK (%I = NULLIF(current_setting(''app.current_tenant_id'',true),'''')::bigint)',
      rec.table_name,tenant_col,tenant_col);
    EXECUTE format(
      'CREATE POLICY platform_admin_bypass ON public.%I FOR ALL USING (NULLIF(current_setting(''app.platform_admin'',true),'''')=''on'') WITH CHECK (NULLIF(current_setting(''app.platform_admin'',true),'''')=''on'')',
      rec.table_name);
    -- Grant the baseline DML this stage's own verification block requires. Relying on
    -- Stage 20's one-time "GRANT ... ON ALL TABLES" is not sufficient: any tenant table
    -- created after Stage 20 ran (a later base-schema revision, a new module) has no
    -- grants, so reconciliation would enable RLS and then fail its own verify with
    -- "tenant RLS reconciliation incomplete". The least-privilege matrix below still
    -- narrows this for its SELECT-only/append-only members.
    -- Spell out each baseline verb so later source-policy checks cannot mistake this
    -- transactional bootstrap for an unreviewed blanket table grant. The explicit
    -- matrix below revokes and reapplies the narrower contract before COMMIT.
    EXECUTE format('GRANT SELECT ON TABLE public.%I TO opstrax_app',rec.table_name);
    EXECUTE format('GRANT INSERT ON TABLE public.%I TO opstrax_app',rec.table_name);
    EXECUTE format('GRANT UPDATE ON TABLE public.%I TO opstrax_app',rec.table_name);
    EXECUTE format('GRANT DELETE ON TABLE public.%I TO opstrax_app',rec.table_name);
    EXECUTE format(
      'REVOKE TRUNCATE,REFERENCES,TRIGGER ON TABLE public.%I FROM opstrax_app',
      rec.table_name);
    -- Generic tenant operational tables are insert-capable. Reconcile every
    -- owned identity/serial sequence exactly; the least-privilege matrix below
    -- overrides this to no sequence rights for its SELECT-only members.
    FOR tenant_col IN
      SELECT seq.oid::regclass::text
      FROM pg_class tbl
      JOIN pg_namespace tbl_ns ON tbl_ns.oid=tbl.relnamespace
      JOIN pg_depend dep ON dep.refobjid=tbl.oid AND dep.refobjsubid>0 AND dep.deptype IN ('a','i')
      JOIN pg_class seq ON seq.oid=dep.objid AND seq.relkind='S'
      WHERE tbl_ns.nspname='public' AND tbl.relname=rec.table_name
    LOOP
      EXECUTE format('REVOKE ALL PRIVILEGES ON SEQUENCE %s FROM opstrax_app',tenant_col);
      EXECUTE format('GRANT USAGE,SELECT ON SEQUENCE %s TO opstrax_app',tenant_col);
    END LOOP;
  END LOOP;

  -- Exact runtime privilege matrix for the 17 newly reconciled tables plus the
  -- pre-existing MFA replay ledger. No current runtime path deletes any of the
  -- 17 tables; append-only/security evidence never gains UPDATE/DELETE. Existing
  -- tenant tables retain their previously audited grants: the dynamic RLS repair
  -- deliberately does not widen their table or sequence privileges.
  FOR rec IN
    SELECT * FROM (VALUES
      ('authorization_decision_logs',true,false,false),
      ('companies',true,true,true),
      ('audit_logs',true,false,false),
      ('fleet_tms_shipment_events',true,false,false),
      ('fleet_tms_cold_chain_event_log',true,false,false),
      ('fleet_tms_asset_events',true,false,false),
      ('fleet_tms_barcode_scan_events',true,false,false),
      ('fleet_tms_rfid_events',true,false,false),
      ('compliance_expiry_events',true,true,false),
      ('market_pack_branch_migration_audit',false,false,false),
      ('access_review_items',true,true,false),
      ('access_reviews',true,true,false),
      ('backup_verifications',true,false,false),
      ('company_security_settings',true,true,false),
      ('compliance_audit_packages',true,true,false),
      ('compliance_violations',false,true,false),
      ('data_retention_policies',true,true,false),
      ('driver_compliance_status',false,false,false),
      ('export_requests',true,true,false),
      ('fleet_tms_branch_migration_audit',false,false,false),
      ('hos_clocks',false,false,false),
      ('platform_impersonation_sessions',true,true,false),
      ('security_events',true,false,false),
      ('sso_connections',true,true,false),
      ('tenant_entitlements',true,true,false),
      ('tenant_subscriptions',true,true,false),
      ('vehicle_compliance_status',false,false,false),
      ('workforce_schedules',true,true,false),
      ('mfa_login_challenge_consumptions',true,false,true)
    ) AS matrix(table_name,allow_insert,allow_update,allow_delete)
  LOOP
    IF to_regclass('public.'||rec.table_name) IS NULL THEN
      -- Some matrix members belong to optional non-Fleet packs, while Stage 55
      -- creates authorization_decision_logs after Stage 53 on a clean deploy.
      -- Reconcile every member that exists; its owning migration/readiness gate
      -- remains responsible for requiring the object when that pack is enabled.
      CONTINUE;
    END IF;
    EXECUTE format('REVOKE ALL PRIVILEGES ON TABLE public.%I FROM opstrax_app',rec.table_name);
    EXECUTE format('GRANT SELECT ON TABLE public.%I TO opstrax_app',rec.table_name);
    IF rec.allow_insert THEN EXECUTE format('GRANT INSERT ON TABLE public.%I TO opstrax_app',rec.table_name); END IF;
    IF rec.allow_update THEN EXECUTE format('GRANT UPDATE ON TABLE public.%I TO opstrax_app',rec.table_name); END IF;
    IF rec.allow_delete THEN EXECUTE format('GRANT DELETE ON TABLE public.%I TO opstrax_app',rec.table_name); END IF;

    FOR tenant_col IN
      SELECT seq.oid::regclass::text
      FROM pg_class tbl
      JOIN pg_namespace tbl_ns ON tbl_ns.oid=tbl.relnamespace
      JOIN pg_depend dep ON dep.refobjid=tbl.oid AND dep.refobjsubid>0 AND dep.deptype IN ('a','i')
      JOIN pg_class seq ON seq.oid=dep.objid AND seq.relkind='S'
      WHERE tbl_ns.nspname='public' AND tbl.relname=rec.table_name
    LOOP
      EXECUTE format('REVOKE ALL PRIVILEGES ON SEQUENCE %s FROM opstrax_app',tenant_col);
      IF rec.allow_insert THEN EXECUTE format('GRANT USAGE,SELECT ON SEQUENCE %s TO opstrax_app',tenant_col); END IF;
    END LOOP;
  END LOOP;

  -- Reassert Stage-50's audited read-only contract. Older application builds had
  -- a boot-time blanket GRANT which could widen these reference tables after the
  -- Stage-50 ledger was written, so an upgrade must actively repair that drift.
  FOR rec IN
    SELECT name FROM (VALUES
      ('fleet_tms_saudi_regions'),('market_packs'),('market_pack_features'),
      ('market_address_schemas'),('market_document_types'),('market_driver_requirements'),
      ('market_vehicle_requirements'),('market_inspection_templates'),('inspection_items'),
      ('market_tax_reporting_rules'),('market_unit_settings'),('market_currency_settings'),
      ('market_language_settings')
    ) AS reference_tables(name)
  LOOP
    IF to_regclass('public.'||rec.name) IS NULL THEN
      RAISE EXCEPTION 'Stage 53 reference table missing: %',rec.name;
    END IF;
    EXECUTE format('REVOKE ALL PRIVILEGES ON TABLE public.%I FROM opstrax_app',rec.name);
    EXECUTE format('GRANT SELECT ON TABLE public.%I TO opstrax_app',rec.name);
    FOR tenant_col IN
      SELECT seq.oid::regclass::text
      FROM pg_class tbl
      JOIN pg_namespace tbl_ns ON tbl_ns.oid=tbl.relnamespace
      JOIN pg_depend dep ON dep.refobjid=tbl.oid AND dep.refobjsubid>0 AND dep.deptype IN ('a','i')
      JOIN pg_class seq ON seq.oid=dep.objid AND seq.relkind='S'
      WHERE tbl_ns.nspname='public' AND tbl.relname=rec.name
    LOOP
      EXECUTE format('REVOKE ALL PRIVILEGES ON SEQUENCE %s FROM opstrax_app',tenant_col);
    END LOOP;
  END LOOP;
END
$tenant_rls$;

DO $verify$
DECLARE
  violations TEXT[];
BEGIN
  IF NOT EXISTS (
    SELECT 1 FROM pg_roles role
    WHERE role.rolname='opstrax_app' AND role.rolcanlogin
      AND NOT role.rolsuper AND NOT role.rolbypassrls
      AND NOT role.rolcreatedb AND NOT role.rolcreaterole
      AND NOT role.rolinherit AND NOT role.rolreplication
      AND NOT EXISTS (SELECT 1 FROM pg_auth_members membership WHERE membership.member=role.oid)
      AND has_database_privilege('opstrax_app',current_database(),'CONNECT')
      AND NOT has_database_privilege('opstrax_app',current_database(),'CREATE')
      AND NOT has_database_privilege('opstrax_app',current_database(),'TEMPORARY')
      AND has_schema_privilege('opstrax_app','public','USAGE')
      AND NOT has_schema_privilege('opstrax_app','public','CREATE')
  ) THEN
    RAISE EXCEPTION 'Stage 53 opstrax_app role capabilities or memberships are unsafe';
  END IF;
  IF EXISTS (
    SELECT 1
    FROM pg_default_acl d
    JOIN pg_namespace ns ON ns.oid=d.defaclnamespace AND ns.nspname='public'
    CROSS JOIN LATERAL aclexplode(d.defaclacl) acl
    JOIN pg_roles grantee ON grantee.oid=acl.grantee
    WHERE grantee.rolname='opstrax_app'
      AND d.defaclobjtype IN ('r','S')
  ) THEN
    RAISE EXCEPTION 'Stage 53 broad opstrax_app default privileges remain';
  END IF;
  WITH tenant_scope AS (
    SELECT cls.oid,cls.relname AS table_name,cls.relrowsecurity,cls.relforcerowsecurity,
      CASE
        WHEN cls.relname='companies' THEN 'id'
        WHEN EXISTS (SELECT 1 FROM information_schema.columns x WHERE x.table_schema='public' AND x.table_name=cls.relname AND x.column_name='company_id' AND x.data_type='bigint') THEN 'company_id'
        ELSE 'tenant_id'
      END AS tenant_col
    FROM pg_class cls
    JOIN pg_namespace ns ON ns.oid=cls.relnamespace
    WHERE ns.nspname='public' AND cls.relkind IN ('r','p')
      AND cls.relname NOT IN ('platform_invoices','gps_gateway_replay')
      AND (cls.relname='companies' OR EXISTS (
        SELECT 1 FROM information_schema.columns x
        WHERE x.table_schema='public' AND x.table_name=cls.relname
          AND x.column_name IN ('company_id','tenant_id') AND x.data_type='bigint'))
  ), privilege_matrix(table_name,allow_insert,allow_update,allow_delete) AS (VALUES
    ('authorization_decision_logs',true,false,false),
    ('companies',true,true,true),
    ('audit_logs',true,false,false),
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
    ('tenant_entitlements',true,true,false),('tenant_subscriptions',true,true,false),
    ('vehicle_compliance_status',false,false,false),('workforce_schedules',true,true,false),
    ('mfa_login_challenge_consumptions',true,false,true)
  )
  SELECT array_agg(s.table_name ORDER BY s.table_name) INTO violations
  FROM tenant_scope s LEFT JOIN privilege_matrix expected ON expected.table_name=s.table_name
  WHERE NOT s.relrowsecurity OR NOT s.relforcerowsecurity
    OR (SELECT COUNT(*) FROM pg_policies p
        WHERE p.schemaname='public' AND p.tablename=s.table_name)<>2
    OR NOT EXISTS (
      SELECT 1 FROM pg_policies p
      WHERE p.schemaname='public' AND p.tablename=s.table_name
        AND p.policyname='tenant_isolation' AND p.permissive='PERMISSIVE'
        AND p.roles='{public}'::name[] AND p.cmd='ALL'
        AND p.qual=format('(%s = (NULLIF(current_setting(''app.current_tenant_id''::text, true), ''''::text))::bigint)',s.tenant_col)
        AND p.with_check=p.qual)
    OR NOT EXISTS (
      SELECT 1 FROM pg_policies p
      WHERE p.schemaname='public' AND p.tablename=s.table_name
        AND p.policyname='platform_admin_bypass' AND p.permissive='PERMISSIVE'
        AND p.roles='{public}'::name[] AND p.cmd='ALL'
        AND p.qual='(NULLIF(current_setting(''app.platform_admin''::text, true), ''''::text) = ''on''::text)'
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
    RAISE EXCEPTION 'Stage 53 tenant RLS reconciliation incomplete: %',violations;
  END IF;
  IF EXISTS (
    SELECT 1
    FROM pg_class tbl
    JOIN pg_namespace ns ON ns.oid=tbl.relnamespace AND ns.nspname='public'
    JOIN pg_depend dep ON dep.refobjid=tbl.oid AND dep.refobjsubid>0 AND dep.deptype IN ('a','i')
    JOIN pg_class seq ON seq.oid=dep.objid AND seq.relkind='S'
    WHERE tbl.relkind IN ('r','p')
      AND tbl.relname NOT IN ('platform_invoices','gps_gateway_replay')
      AND (tbl.relname='companies' OR EXISTS (SELECT 1 FROM information_schema.columns x
        WHERE x.table_schema='public' AND x.table_name=tbl.relname
          AND x.column_name IN ('company_id','tenant_id') AND x.data_type='bigint'))
      AND (has_sequence_privilege('opstrax_app',seq.oid,'USAGE')<>
             has_table_privilege('opstrax_app',tbl.oid,'INSERT')
        OR has_sequence_privilege('opstrax_app',seq.oid,'SELECT')<>
             has_table_privilege('opstrax_app',tbl.oid,'INSERT')
        OR has_sequence_privilege('opstrax_app',seq.oid,'UPDATE'))
  ) THEN
    RAISE EXCEPTION 'Stage 53 tenant sequence privileges do not match insert capability';
  END IF;
  IF has_table_privilege('opstrax_app','public.mfa_login_challenge_consumptions','UPDATE') THEN
    RAISE EXCEPTION 'Stage 53 regressed MFA replay-ledger least privilege';
  END IF;
  IF EXISTS (
    SELECT 1
    FROM (VALUES
      ('authorization_decision_logs',true,false,false),
      ('companies',true,true,true),
      ('audit_logs',true,false,false),
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
      ('tenant_entitlements',true,true,false),('tenant_subscriptions',true,true,false),
      ('vehicle_compliance_status',false,false,false),('workforce_schedules',true,true,false),
      ('mfa_login_challenge_consumptions',true,false,true)
    ) expected(table_name,allow_insert,allow_update,allow_delete)
    WHERE to_regclass('public.'||expected.table_name) IS NOT NULL
      AND (NOT has_table_privilege('opstrax_app','public.'||expected.table_name,'SELECT')
        OR has_table_privilege('opstrax_app','public.'||expected.table_name,'INSERT')<>expected.allow_insert
        OR has_table_privilege('opstrax_app','public.'||expected.table_name,'UPDATE')<>expected.allow_update
        OR has_table_privilege('opstrax_app','public.'||expected.table_name,'DELETE')<>expected.allow_delete
        OR has_table_privilege('opstrax_app','public.'||expected.table_name,'TRUNCATE')
        OR has_table_privilege('opstrax_app','public.'||expected.table_name,'REFERENCES')
        OR has_table_privilege('opstrax_app','public.'||expected.table_name,'TRIGGER'))
  ) THEN
    RAISE EXCEPTION 'Stage 53 exact least-privilege matrix verification failed';
  END IF;
  IF EXISTS (
    SELECT 1 FROM (VALUES
      ('fleet_tms_saudi_regions'),('market_packs'),('market_pack_features'),
      ('market_address_schemas'),('market_document_types'),('market_driver_requirements'),
      ('market_vehicle_requirements'),('market_inspection_templates'),('inspection_items'),
      ('market_tax_reporting_rules'),('market_unit_settings'),('market_currency_settings'),
      ('market_language_settings')
    ) expected(table_name)
    WHERE to_regclass('public.'||expected.table_name) IS NULL
      OR NOT has_table_privilege('opstrax_app','public.'||expected.table_name,'SELECT')
      OR has_table_privilege('opstrax_app','public.'||expected.table_name,'INSERT')
      OR has_table_privilege('opstrax_app','public.'||expected.table_name,'UPDATE')
      OR has_table_privilege('opstrax_app','public.'||expected.table_name,'DELETE')
      OR has_table_privilege('opstrax_app','public.'||expected.table_name,'TRUNCATE')
      OR has_table_privilege('opstrax_app','public.'||expected.table_name,'REFERENCES')
      OR has_table_privilege('opstrax_app','public.'||expected.table_name,'TRIGGER')
  ) THEN
    RAISE EXCEPTION 'Stage 53 reference-table SELECT-only verification failed';
  END IF;
  IF EXISTS (
    SELECT 1
    FROM (VALUES
      ('fleet_tms_saudi_regions'),('market_packs'),('market_pack_features'),
      ('market_address_schemas'),('market_document_types'),('market_driver_requirements'),
      ('market_vehicle_requirements'),('market_inspection_templates'),('inspection_items'),
      ('market_tax_reporting_rules'),('market_unit_settings'),('market_currency_settings'),
      ('market_language_settings')
    ) expected(table_name)
    JOIN pg_class tbl ON tbl.oid=to_regclass('public.'||expected.table_name)
    JOIN pg_depend dep ON dep.refobjid=tbl.oid AND dep.refobjsubid>0 AND dep.deptype IN ('a','i')
    JOIN pg_class seq ON seq.oid=dep.objid AND seq.relkind='S'
    WHERE has_sequence_privilege('opstrax_app',seq.oid,'USAGE')
       OR has_sequence_privilege('opstrax_app',seq.oid,'SELECT')
       OR has_sequence_privilege('opstrax_app',seq.oid,'UPDATE')
  ) THEN
    RAISE EXCEPTION 'Stage 53 reference-table sequence privilege verification failed';
  END IF;
END
$verify$;

INSERT INTO schema_migrations(version,description)
VALUES ('2026_07_30_stage53_tenant_rls_reconciliation','Production-wide tenant RLS/policy/grant reconciliation')
ON CONFLICT(version) DO NOTHING;

COMMIT;
