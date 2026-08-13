-- Stage 76 — terminal telemetry secret, projection, and runtime-ACL hardening.
--
-- This migration deliberately does not CREATE/ALTER runtime roles, memberships, or object
-- ownership. Managed Postgres providers retain their database-owner compatibility; the already
-- provisioned Stage58 app/system identities receive only explicit per-object capabilities.
BEGIN;

ALTER TABLE eld_devices ADD COLUMN IF NOT EXISTS last_seen_at TIMESTAMPTZ NULL;

DO $stage76_replay_generation_repair$
BEGIN
  IF to_regclass('public.telemetry_replay_seen') IS NOT NULL THEN
    ALTER TABLE telemetry_replay_seen ADD COLUMN IF NOT EXISTS unwrapped_serial BIGINT NULL;
    ALTER TABLE telemetry_replay_seen ADD COLUMN IF NOT EXISTS event_id UUID NULL;
    UPDATE telemetry_replay_seen SET unwrapped_serial=serial WHERE unwrapped_serial IS NULL;
    UPDATE telemetry_replay_seen
       SET event_id=md5('opstrax.replay.legacy.v1:'||id::text||':'||device_id||':'||serial::text||':'||content_hash)::uuid
     WHERE event_id IS NULL;
    ALTER TABLE telemetry_replay_seen ALTER COLUMN unwrapped_serial SET NOT NULL;
    ALTER TABLE telemetry_replay_seen ALTER COLUMN event_id SET NOT NULL;
    ALTER TABLE telemetry_replay_seen DROP CONSTRAINT IF EXISTS uq_telemetry_replay_seen_triple;
    ALTER TABLE telemetry_replay_seen DROP CONSTRAINT IF EXISTS uq_telemetry_replay_seen_unwrapped;
    ALTER TABLE telemetry_replay_seen ADD CONSTRAINT uq_telemetry_replay_seen_unwrapped
      UNIQUE(device_id,unwrapped_serial,content_hash);

    CREATE TABLE IF NOT EXISTS telemetry_replay_device_state (
      device_id TEXT PRIMARY KEY,
      last_raw_serial BIGINT NOT NULL,
      high_water_unwrapped BIGINT NOT NULL,
      updated_at TIMESTAMPTZ NOT NULL DEFAULT NOW()
    );
    -- Deliberately do not infer state from legacy rows. Raw-key uniqueness could already have
    -- suppressed later-generation identical heartbeats, so their chronology is incomplete.
    -- PostgresReplayGuard bootstraps the first post-upgrade frame into a fresh epoch above all
    -- legacy unwrapped values while holding the per-device advisory transaction lock.
  END IF;
END
$stage76_replay_generation_repair$;

DO $stage76_roles_and_defaults$
DECLARE
  owner_record RECORD;
BEGIN
  IF NOT EXISTS (SELECT 1 FROM pg_roles WHERE rolname='opstrax_app')
     OR NOT EXISTS (SELECT 1 FROM pg_roles WHERE rolname='opstrax_system') THEN
    RAISE EXCEPTION 'Stage76 requires the independently authenticated Stage58 runtime roles';
  END IF;

  REVOKE ALL ON SCHEMA public FROM PUBLIC;
  REVOKE CREATE ON SCHEMA public FROM opstrax_app,opstrax_system;
  GRANT USAGE ON SCHEMA public TO opstrax_app,opstrax_system;

  -- Revoke defaults only as an owner/member that PostgreSQL authorizes to do so. If a foreign
  -- owner has granted ambient telemetry access, stop rather than silently ship a non-default-deny
  -- database. This avoids provider-incompatible role/ownership rewrites while remaining exact.
  FOR owner_record IN
    SELECT DISTINCT defaults.defaclrole,owner_role.rolname AS owner_name,
           defaults.defaclnamespace=0 AS is_global
      FROM pg_default_acl defaults
      LEFT JOIN pg_namespace schema_ns ON schema_ns.oid=defaults.defaclnamespace
      JOIN pg_roles owner_role ON owner_role.oid=defaults.defaclrole
      CROSS JOIN LATERAL aclexplode(defaults.defaclacl) privilege
      LEFT JOIN pg_roles grantee ON grantee.oid=privilege.grantee
     WHERE (defaults.defaclnamespace=0 OR schema_ns.nspname='public')
       AND defaults.defaclobjtype IN ('r','S')
       AND (privilege.grantee=0 OR grantee.rolname IN ('opstrax_app','opstrax_system'))
  LOOP
    IF NOT pg_has_role(current_user,owner_record.defaclrole,'MEMBER') THEN
      RAISE EXCEPTION 'Stage76 cannot repair default ACL owned by %; apply as that migration owner',
        owner_record.owner_name;
    END IF;
    IF owner_record.is_global THEN
      EXECUTE format(
        'ALTER DEFAULT PRIVILEGES FOR ROLE %I REVOKE ALL PRIVILEGES ON TABLES FROM PUBLIC,opstrax_app,opstrax_system',
        owner_record.owner_name);
      EXECUTE format(
        'ALTER DEFAULT PRIVILEGES FOR ROLE %I REVOKE ALL PRIVILEGES ON SEQUENCES FROM PUBLIC,opstrax_app,opstrax_system',
        owner_record.owner_name);
    ELSE
      EXECUTE format(
        'ALTER DEFAULT PRIVILEGES FOR ROLE %I IN SCHEMA public REVOKE ALL PRIVILEGES ON TABLES FROM PUBLIC,opstrax_app,opstrax_system',
        owner_record.owner_name);
      EXECUTE format(
        'ALTER DEFAULT PRIVILEGES FOR ROLE %I IN SCHEMA public REVOKE ALL PRIVILEGES ON SEQUENCES FROM PUBLIC,opstrax_app,opstrax_system',
        owner_record.owner_name);
    END IF;
  END LOOP;

  -- Ensure objects created by this migration owner remain closed even if no explicit default ACL
  -- row existed before this migration.
  EXECUTE format(
    'ALTER DEFAULT PRIVILEGES FOR ROLE %I IN SCHEMA public REVOKE ALL PRIVILEGES ON TABLES FROM PUBLIC,opstrax_app,opstrax_system',
    current_user);
  EXECUTE format(
    'ALTER DEFAULT PRIVILEGES FOR ROLE %I IN SCHEMA public REVOKE ALL PRIVILEGES ON SEQUENCES FROM PUBLIC,opstrax_app,opstrax_system',
    current_user);
  EXECUTE format(
    'ALTER DEFAULT PRIVILEGES FOR ROLE %I REVOKE ALL PRIVILEGES ON TABLES FROM PUBLIC,opstrax_app,opstrax_system',
    current_user);
  EXECUTE format(
    'ALTER DEFAULT PRIVILEGES FOR ROLE %I REVOKE ALL PRIVILEGES ON SEQUENCES FROM PUBLIC,opstrax_app,opstrax_system',
    current_user);
END
$stage76_roles_and_defaults$;

DO $stage76_exact_acl$
DECLARE
  object_record RECORD;
  column_record RECORD;
  sequence_name TEXT;
BEGIN
  FOR object_record IN
    SELECT * FROM (VALUES
      -- table                              required app S/I/U/D       system S/I/U/D
      ('eld_devices',                         TRUE,  FALSE,FALSE,FALSE,FALSE, TRUE,TRUE,TRUE,TRUE),
      ('location_events',                     TRUE,  TRUE, FALSE,FALSE,FALSE, TRUE,TRUE,TRUE,TRUE),
      ('latest_vehicle_positions',            TRUE,  TRUE, FALSE,FALSE,FALSE, TRUE,TRUE,TRUE,TRUE),
      ('telemetry_alerts',                    TRUE,  TRUE, FALSE,TRUE, FALSE, TRUE,TRUE,TRUE,TRUE),
      ('telemetry_rules',                     TRUE,  TRUE, TRUE, TRUE, FALSE, TRUE,TRUE,TRUE,TRUE),
      ('telemetry_gateways',                  TRUE,  FALSE,FALSE,FALSE,FALSE, TRUE,TRUE,TRUE,TRUE),
      ('device_state_transitions',            TRUE,  TRUE, TRUE, FALSE,FALSE, TRUE,TRUE,TRUE,TRUE),
      ('device_installations',                TRUE,  TRUE, TRUE, TRUE, FALSE, TRUE,TRUE,TRUE,TRUE),
      ('device_installation_evidence',        TRUE,  TRUE, TRUE, FALSE,FALSE, TRUE,TRUE,TRUE,TRUE),
      ('device_installation_quarantine',      TRUE,  FALSE,FALSE,FALSE,FALSE, TRUE,TRUE,TRUE,FALSE),
      ('dvir_inspection_results',             TRUE,  TRUE, TRUE, FALSE,FALSE, TRUE,TRUE,TRUE,TRUE),
      ('device_channel_health',               TRUE,  TRUE, TRUE, TRUE, FALSE, TRUE,TRUE,TRUE,TRUE),
      ('telematics_device_commands',          TRUE,  TRUE, TRUE, TRUE, FALSE, TRUE,TRUE,TRUE,TRUE),
      ('telemetry_privacy_policies',          TRUE,  TRUE, TRUE, TRUE, FALSE, TRUE,TRUE,TRUE,TRUE),
      ('fault_codes',                         TRUE,  TRUE, TRUE, TRUE, FALSE, TRUE,TRUE,TRUE,TRUE),
      ('fault_occurrences',                   TRUE,  TRUE, TRUE, FALSE,FALSE, TRUE,TRUE,TRUE,TRUE),
      ('diagnostic_holds',                    TRUE,  TRUE, TRUE, TRUE, FALSE, TRUE,TRUE,TRUE,TRUE),
      ('telemetry_nonces',                    TRUE,  FALSE,FALSE,FALSE,FALSE, TRUE,TRUE,FALSE,TRUE),
      ('gps_gateway_replay',                  TRUE,  FALSE,FALSE,FALSE,FALSE, TRUE,TRUE,FALSE,TRUE),
      ('telemetry_store_forward',             TRUE,  FALSE,FALSE,FALSE,FALSE, TRUE,TRUE,TRUE,TRUE),
      ('telemetry_gateway_rejections',        TRUE,  FALSE,FALSE,FALSE,FALSE, TRUE,TRUE,FALSE,TRUE),
      ('telemetry_stream_ticket_nonces',      TRUE,  FALSE,TRUE, FALSE,FALSE, TRUE,TRUE,TRUE,TRUE),
      -- Standalone raw-gateway topology is optional for the main API deployment. If installed,
      -- its ledgers are system owned; raw payload/inbox/replay state is never tenant-queryable.
      ('telematics_device_trust_policy',      FALSE, FALSE,FALSE,FALSE,FALSE, TRUE,TRUE,TRUE,TRUE),
      ('telemetry_replay_seen',               FALSE, FALSE,FALSE,FALSE,FALSE, TRUE,TRUE,FALSE,TRUE),
      ('telemetry_replay_device_state',       FALSE, FALSE,FALSE,FALSE,FALSE, TRUE,TRUE,TRUE, FALSE),
      ('telemetry_projection_inbox',          FALSE, FALSE,FALSE,FALSE,FALSE, TRUE,TRUE,FALSE,TRUE),
      ('canonical_telemetry_events',          FALSE, FALSE,FALSE,FALSE,FALSE, TRUE,TRUE,FALSE,TRUE),
      ('raw_packets',                         FALSE, FALSE,FALSE,FALSE,FALSE, TRUE,TRUE,TRUE,TRUE)
    ) AS matrix(table_name,required,
                app_select,app_insert,app_update,app_delete,
                system_select,system_insert,system_update,system_delete)
  LOOP
    IF to_regclass('public.'||object_record.table_name) IS NULL THEN
      IF object_record.required THEN
        RAISE EXCEPTION 'Stage76 required telemetry table is missing: %',object_record.table_name;
      END IF;
      CONTINUE;
    END IF;

    -- Independent column ACLs survive a table-level revoke. Remove them before rebuilding the
    -- one intentional safe-column SELECT grant on eld_devices below.
    FOR column_record IN
      SELECT attribute.attname
        FROM pg_attribute attribute
       WHERE attribute.attrelid=to_regclass('public.'||object_record.table_name)
         AND attribute.attnum>0 AND NOT attribute.attisdropped AND attribute.attacl IS NOT NULL
    LOOP
      EXECUTE format(
        'REVOKE ALL PRIVILEGES (%I) ON TABLE public.%I FROM PUBLIC,opstrax_app,opstrax_system',
        column_record.attname,object_record.table_name);
    END LOOP;

    EXECUTE format(
      'REVOKE ALL PRIVILEGES ON TABLE public.%I FROM PUBLIC,opstrax_app,opstrax_system',
      object_record.table_name);
    IF object_record.app_select THEN EXECUTE format('GRANT SELECT ON TABLE public.%I TO opstrax_app',object_record.table_name); END IF;
    IF object_record.app_insert THEN EXECUTE format('GRANT INSERT ON TABLE public.%I TO opstrax_app',object_record.table_name); END IF;
    IF object_record.app_update THEN EXECUTE format('GRANT UPDATE ON TABLE public.%I TO opstrax_app',object_record.table_name); END IF;
    IF object_record.app_delete THEN EXECUTE format('GRANT DELETE ON TABLE public.%I TO opstrax_app',object_record.table_name); END IF;
    IF object_record.system_select THEN EXECUTE format('GRANT SELECT ON TABLE public.%I TO opstrax_system',object_record.table_name); END IF;
    IF object_record.system_insert THEN EXECUTE format('GRANT INSERT ON TABLE public.%I TO opstrax_system',object_record.table_name); END IF;
    IF object_record.system_update THEN EXECUTE format('GRANT UPDATE ON TABLE public.%I TO opstrax_system',object_record.table_name); END IF;
    IF object_record.system_delete THEN EXECUTE format('GRANT DELETE ON TABLE public.%I TO opstrax_system',object_record.table_name); END IF;

    FOR sequence_name IN
      SELECT sequence.oid::regclass::text
        FROM pg_class table_class
        JOIN pg_namespace table_namespace
          ON table_namespace.oid=table_class.relnamespace AND table_namespace.nspname='public'
        JOIN pg_depend dependency
          ON dependency.refobjid=table_class.oid AND dependency.refobjsubid>0
         AND dependency.deptype IN ('a','i')
        JOIN pg_class sequence ON sequence.oid=dependency.objid AND sequence.relkind='S'
       WHERE table_class.relname=object_record.table_name
    LOOP
      EXECUTE format('REVOKE ALL PRIVILEGES ON SEQUENCE %s FROM PUBLIC,opstrax_app,opstrax_system',sequence_name);
      IF object_record.app_insert THEN
        -- Stream-ticket insertion uses nextval but may not inspect the nonce sequence.
        -- Other tenant-writable identities retain SELECT for currval-compatible callers.
        IF object_record.table_name='telemetry_stream_ticket_nonces' THEN
          EXECUTE format('GRANT USAGE ON SEQUENCE %s TO opstrax_app',sequence_name);
        ELSE
          EXECUTE format('GRANT USAGE,SELECT ON SEQUENCE %s TO opstrax_app',sequence_name);
        END IF;
      END IF;
      IF object_record.system_insert THEN
        IF object_record.table_name='telemetry_stream_ticket_nonces' THEN
          EXECUTE format('GRANT USAGE ON SEQUENCE %s TO opstrax_system',sequence_name);
        ELSE
          EXECUTE format('GRANT USAGE,SELECT ON SEQUENCE %s TO opstrax_system',sequence_name);
        END IF;
      END IF;
    END LOOP;
  END LOOP;

  -- Tenant reads receive inventory/lifecycle metadata, never hashes, plaintext, current encrypted
  -- material, or grace-period encrypted material. Secret-bearing inserts/rotations/revocations run
  -- through the separately authenticated system connection after endpoint RBAC + tenant checks.
  SELECT string_agg(quote_ident(column_name),',' ORDER BY ordinal_position)
    INTO sequence_name
    FROM information_schema.columns
   WHERE table_schema='public' AND table_name='eld_devices'
     AND column_name NOT IN (
       'api_key_hash','api_key_previous_hash','hmac_secret',
       'hmac_secret_encrypted','hmac_previous_secret_encrypted');
  IF sequence_name IS NULL THEN RAISE EXCEPTION 'Stage76 could not build safe eld_devices column grant'; END IF;
  EXECUTE format('GRANT SELECT (%s) ON TABLE public.eld_devices TO opstrax_app',sequence_name);
  SELECT string_agg(quote_ident(column_name),',' ORDER BY ordinal_position)
    INTO sequence_name
    FROM information_schema.columns
   WHERE table_schema='public' AND table_name='eld_devices'
     AND column_name=ANY(ARRAY[
       'branch_id','imei','device_model','provider','device_state',
       'firmware_version','status','malfunction_code','malfunction_description',
       'malfunction_resolved_at','malfunction_resolved_by','resolution_evidence',
       'row_version','updated_at'
     ]);
  IF sequence_name IS NULL THEN RAISE EXCEPTION 'Stage76 could not build safe eld_devices update grant'; END IF;
  EXECUTE format('GRANT UPDATE (%s) ON TABLE public.eld_devices TO opstrax_app',sequence_name);

  SELECT string_agg(quote_ident(column_name),',' ORDER BY ordinal_position)
    INTO sequence_name
    FROM information_schema.columns
   WHERE table_schema='public' AND table_name='telemetry_gateways'
     AND column_name<>'secret_encrypted';
  IF sequence_name IS NULL THEN RAISE EXCEPTION 'Stage76 could not build safe telemetry_gateways column grant'; END IF;
  EXECUTE format('GRANT SELECT (%s) ON TABLE public.telemetry_gateways TO opstrax_app',sequence_name);
  GRANT UPDATE (gateway_name,status,updated_at) ON TABLE public.telemetry_gateways TO opstrax_app;
END
$stage76_exact_acl$;

DO $stage76_verify$
DECLARE
  object_record RECORD;
  sequence_record RECORD;
BEGIN
  IF EXISTS (
    SELECT 1
      FROM pg_default_acl defaults
      LEFT JOIN pg_namespace schema_ns ON schema_ns.oid=defaults.defaclnamespace
      CROSS JOIN LATERAL aclexplode(defaults.defaclacl) privilege
      LEFT JOIN pg_roles grantee ON grantee.oid=privilege.grantee
     WHERE (defaults.defaclnamespace=0 OR schema_ns.nspname='public')
       AND defaults.defaclobjtype IN ('r','S')
       AND (privilege.grantee=0 OR grantee.rolname IN ('opstrax_app','opstrax_system'))
  ) THEN
    RAISE EXCEPTION 'Stage76 broad PUBLIC/runtime default privileges remain';
  END IF;

  IF has_table_privilege('opstrax_app','eld_devices','SELECT')
     OR NOT has_column_privilege('opstrax_app','eld_devices','device_serial','SELECT')
     OR has_column_privilege('opstrax_app','eld_devices','api_key_hash','SELECT')
     OR has_column_privilege('opstrax_app','eld_devices','api_key_previous_hash','SELECT')
     OR has_column_privilege('opstrax_app','eld_devices','hmac_secret','SELECT')
     OR has_column_privilege('opstrax_app','eld_devices','hmac_secret_encrypted','SELECT')
     OR has_column_privilege('opstrax_app','eld_devices','hmac_previous_secret_encrypted','SELECT')
     OR has_table_privilege('opstrax_app','eld_devices','INSERT,UPDATE,DELETE')
     OR NOT has_column_privilege('opstrax_app','eld_devices','status','UPDATE')
     OR NOT has_column_privilege('opstrax_app','eld_devices','malfunction_resolved_at','UPDATE')
     OR NOT has_column_privilege('opstrax_app','eld_devices','malfunction_resolved_by','UPDATE')
     OR NOT has_column_privilege('opstrax_app','eld_devices','resolution_evidence','UPDATE')
     OR NOT has_column_privilege('opstrax_app','eld_devices','row_version','UPDATE')
     OR NOT has_column_privilege('opstrax_app','eld_devices','device_state','UPDATE')
     OR has_column_privilege('opstrax_app','eld_devices','vehicle_id','UPDATE')
     OR has_column_privilege('opstrax_app','eld_devices','driver_id','UPDATE')
     OR has_column_privilege('opstrax_app','eld_devices','api_key_hash','UPDATE')
     OR has_column_privilege('opstrax_app','eld_devices','hmac_secret_encrypted','UPDATE') THEN
    RAISE EXCEPTION 'Stage76 eld_devices credential read boundary is unsafe';
  END IF;

  IF has_table_privilege('opstrax_app','telemetry_gateways','SELECT,INSERT,UPDATE,DELETE')
     OR NOT has_column_privilege('opstrax_app','telemetry_gateways','gateway_id','SELECT')
     OR NOT has_column_privilege('opstrax_app','telemetry_gateways','status','UPDATE')
     OR has_column_privilege('opstrax_app','telemetry_gateways','secret_encrypted','SELECT')
     OR has_column_privilege('opstrax_app','telemetry_gateways','secret_encrypted','UPDATE') THEN
    RAISE EXCEPTION 'Stage76 gateway credential boundary is unsafe';
  END IF;

  FOR object_record IN
    SELECT * FROM (VALUES
      ('location_events',TRUE,FALSE,FALSE,FALSE),
      ('latest_vehicle_positions',TRUE,FALSE,FALSE,FALSE),
      ('telemetry_alerts',TRUE,FALSE,TRUE,FALSE),
      ('telemetry_rules',TRUE,TRUE,TRUE,FALSE),
      ('telemetry_gateways',FALSE,FALSE,FALSE,FALSE),
      ('device_state_transitions',TRUE,TRUE,FALSE,FALSE),
      ('device_installation_evidence',TRUE,TRUE,FALSE,FALSE),
      ('fault_occurrences',TRUE,TRUE,FALSE,FALSE),
      ('telemetry_nonces',FALSE,FALSE,FALSE,FALSE),
      ('gps_gateway_replay',FALSE,FALSE,FALSE,FALSE),
      ('telemetry_store_forward',FALSE,FALSE,FALSE,FALSE),
      ('telemetry_gateway_rejections',FALSE,FALSE,FALSE,FALSE),
      ('telemetry_stream_ticket_nonces',FALSE,TRUE,FALSE,FALSE),
      ('canonical_telemetry_events',FALSE,FALSE,FALSE,FALSE)
    ) AS expected(table_name,allow_select,allow_insert,allow_update,allow_delete)
  LOOP
    IF has_table_privilege('opstrax_app','public.'||object_record.table_name,'SELECT')<>object_record.allow_select
       OR has_table_privilege('opstrax_app','public.'||object_record.table_name,'INSERT')<>object_record.allow_insert
       OR has_table_privilege('opstrax_app','public.'||object_record.table_name,'UPDATE')<>object_record.allow_update
       OR has_table_privilege('opstrax_app','public.'||object_record.table_name,'DELETE')<>object_record.allow_delete
       OR has_table_privilege('opstrax_app','public.'||object_record.table_name,'TRUNCATE,REFERENCES,TRIGGER')
       OR has_table_privilege('public','public.'||object_record.table_name,'SELECT,INSERT,UPDATE,DELETE,TRUNCATE,REFERENCES,TRIGGER') THEN
      RAISE EXCEPTION 'Stage76 exact application table ACL failed for %',object_record.table_name;
    END IF;
  END LOOP;

  FOR sequence_record IN
    SELECT sequence.oid
      FROM pg_class sequence
      JOIN pg_namespace namespace ON namespace.oid=sequence.relnamespace
     WHERE namespace.nspname='public' AND sequence.relkind='S'
       AND sequence.relname IN (
         'telemetry_nonces_id_seq','gps_gateway_replay_id_seq',
         'telemetry_store_forward_id_seq','telemetry_gateway_rejections_id_seq',
         'canonical_telemetry_events_id_seq')
  LOOP
    IF has_sequence_privilege('opstrax_app',sequence_record.oid,'USAGE,SELECT,UPDATE')
       OR has_sequence_privilege('public',sequence_record.oid,'USAGE,SELECT,UPDATE')
       -- PostgreSQL treats a comma-separated privilege request as "any", not "all".
       -- Positive requirements therefore remain deliberately split.
       OR NOT has_sequence_privilege('opstrax_system',sequence_record.oid,'USAGE')
       OR NOT has_sequence_privilege('opstrax_system',sequence_record.oid,'SELECT')
       OR has_sequence_privilege('opstrax_system',sequence_record.oid,'UPDATE') THEN
      RAISE EXCEPTION 'Stage76 infrastructure sequence ACL is unsafe';
    END IF;
  END LOOP;

  IF NOT has_table_privilege('opstrax_system','canonical_telemetry_events','SELECT')
     OR NOT has_table_privilege('opstrax_system','canonical_telemetry_events','INSERT')
     OR has_table_privilege('opstrax_system','canonical_telemetry_events','UPDATE')
     OR NOT has_table_privilege('opstrax_system','canonical_telemetry_events','DELETE') THEN
    RAISE EXCEPTION 'Stage76 canonical telemetry system-only ACL is unsafe';
  END IF;

  IF to_regclass('public.telemetry_replay_device_state') IS NOT NULL AND (
       has_table_privilege('opstrax_app','telemetry_replay_device_state','SELECT,INSERT,UPDATE,DELETE')
       OR has_any_column_privilege('opstrax_app','telemetry_replay_device_state','SELECT,INSERT,UPDATE,REFERENCES')
       OR NOT has_table_privilege('opstrax_system','telemetry_replay_device_state','SELECT')
       OR NOT has_table_privilege('opstrax_system','telemetry_replay_device_state','INSERT')
       OR NOT has_table_privilege('opstrax_system','telemetry_replay_device_state','UPDATE')
       OR has_table_privilege('opstrax_system','telemetry_replay_device_state','DELETE')
       OR to_regclass('public.uq_telemetry_replay_seen_unwrapped') IS NULL
       OR EXISTS (
         SELECT 1 FROM (VALUES ('unwrapped_serial'),('event_id')) required(column_name)
         WHERE NOT EXISTS (
           SELECT 1 FROM information_schema.columns actual
            WHERE actual.table_schema='public' AND actual.table_name='telemetry_replay_seen'
              AND actual.column_name=required.column_name AND actual.is_nullable='NO'))
     ) THEN
    RAISE EXCEPTION 'Stage76 durable replay generation ACL/schema is unsafe';
  END IF;

  IF to_regclass('public.telemetry_stream_ticket_nonces_id_seq') IS NOT NULL AND (
       NOT has_sequence_privilege('opstrax_app','telemetry_stream_ticket_nonces_id_seq','USAGE')
       OR has_sequence_privilege('opstrax_app','telemetry_stream_ticket_nonces_id_seq','SELECT,UPDATE')
       OR NOT has_sequence_privilege('opstrax_system','telemetry_stream_ticket_nonces_id_seq','USAGE')
       OR has_sequence_privilege('opstrax_system','telemetry_stream_ticket_nonces_id_seq','SELECT,UPDATE')
       OR has_sequence_privilege('public','telemetry_stream_ticket_nonces_id_seq','USAGE,SELECT,UPDATE')) THEN
    RAISE EXCEPTION 'Stage76 stream-ticket nonce sequence ACL is unsafe';
  END IF;
END
$stage76_verify$;

INSERT INTO schema_migrations(version,description)
VALUES ('2026_08_11_stage76_telematics_security_hardening',
        'Terminal telemetry projection topology and exact default-deny runtime grants')
ON CONFLICT(version) DO UPDATE SET description=EXCLUDED.description;

COMMIT;
