-- Stage 59 -- shared, encrypted ASP.NET Core Data Protection key repository.
-- The XML payload is already certificate-encrypted by the application. PostgreSQL
-- provides durable, cross-instance coordination; only opstrax_system can read/write.

BEGIN;

DO $preflight$
BEGIN
  IF NOT EXISTS (SELECT 1 FROM schema_migrations WHERE version='2026_07_31_stage58_nonforgeable_tenant_ticket') THEN
    RAISE EXCEPTION 'Stage 59 requires terminal Stage 58 first';
  END IF;
  IF NOT EXISTS (SELECT 1 FROM pg_roles WHERE rolname='opstrax_system')
     OR NOT EXISTS (SELECT 1 FROM pg_roles WHERE rolname='opstrax_app') THEN
    RAISE EXCEPTION 'Stage 59 requires both restricted runtime identities';
  END IF;
END
$preflight$;

CREATE TABLE IF NOT EXISTS public.platform_data_protection_keys (
  id bigint GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
  friendly_name varchar(256) NOT NULL UNIQUE,
  xml_payload text NOT NULL CHECK (octet_length(xml_payload)<=1048576),
  created_at timestamptz NOT NULL DEFAULT clock_timestamp()
);

ALTER TABLE public.platform_data_protection_keys ENABLE ROW LEVEL SECURITY;
ALTER TABLE public.platform_data_protection_keys FORCE ROW LEVEL SECURITY;

DO $policies$
DECLARE p record;
BEGIN
  FOR p IN SELECT policyname FROM pg_policies
    WHERE schemaname='public' AND tablename='platform_data_protection_keys'
  LOOP
    EXECUTE format('DROP POLICY %I ON public.platform_data_protection_keys',p.policyname);
  END LOOP;
END
$policies$;

CREATE POLICY system_control_plane ON public.platform_data_protection_keys
  AS PERMISSIVE FOR ALL TO opstrax_system USING (true) WITH CHECK (true);

REVOKE ALL ON TABLE public.platform_data_protection_keys FROM PUBLIC,opstrax_app,opstrax_system;
GRANT SELECT,INSERT ON TABLE public.platform_data_protection_keys TO opstrax_system;
REVOKE ALL ON SEQUENCE public.platform_data_protection_keys_id_seq FROM PUBLIC,opstrax_app,opstrax_system;
GRANT USAGE,SELECT ON SEQUENCE public.platform_data_protection_keys_id_seq TO opstrax_system;

DO $verify$
DECLARE
  key_table regclass := 'public.platform_data_protection_keys'::regclass;
  id_attnum smallint;
  friendly_name_attnum smallint;
BEGIN
  SELECT attnum INTO id_attnum FROM pg_attribute
   WHERE attrelid=key_table AND attname='id' AND NOT attisdropped;
  SELECT attnum INTO friendly_name_attnum FROM pg_attribute
   WHERE attrelid=key_table AND attname='friendly_name' AND NOT attisdropped;

  IF (SELECT count(*) FROM pg_attribute
       WHERE attrelid=key_table AND attnum>0 AND NOT attisdropped)<>4
     OR NOT EXISTS (SELECT 1 FROM pg_attribute WHERE attrelid=key_table
          AND attname='id' AND atttypid='bigint'::regtype AND attnotnull AND attidentity='a')
     OR NOT EXISTS (SELECT 1 FROM pg_attribute WHERE attrelid=key_table
          AND attname='friendly_name' AND atttypid='character varying'::regtype
          AND atttypmod=260 AND attnotnull AND attidentity='')
     OR NOT EXISTS (SELECT 1 FROM pg_attribute WHERE attrelid=key_table
          AND attname='xml_payload' AND atttypid='text'::regtype
          AND atttypmod=-1 AND attnotnull AND attidentity='')
     OR NOT EXISTS (SELECT 1 FROM pg_attribute WHERE attrelid=key_table
          AND attname='created_at' AND atttypid='timestamp with time zone'::regtype
          AND attnotnull AND attidentity='')
     OR (SELECT count(*) FROM pg_constraint WHERE conrelid=key_table AND contype='p')<>1
     OR NOT EXISTS (SELECT 1 FROM pg_constraint WHERE conrelid=key_table AND contype='p'
          AND conkey=ARRAY[id_attnum]::smallint[])
     OR (SELECT count(*) FROM pg_constraint WHERE conrelid=key_table AND contype='u')<>1
     OR NOT EXISTS (SELECT 1 FROM pg_constraint WHERE conrelid=key_table AND contype='u'
          AND conkey=ARRAY[friendly_name_attnum]::smallint[])
     OR (SELECT count(*) FROM pg_constraint WHERE conrelid=key_table AND contype='c')<>1
     OR NOT EXISTS (SELECT 1 FROM pg_constraint WHERE conrelid=key_table AND contype='c'
          AND regexp_replace(pg_get_expr(conbin,conrelid),'[()[:space:]]','','g')
              ='octet_lengthxml_payload<=1048576')
     OR pg_get_serial_sequence('public.platform_data_protection_keys','id')
          IS DISTINCT FROM 'public.platform_data_protection_keys_id_seq'
     OR NOT EXISTS (SELECT 1 FROM pg_attrdef d JOIN pg_attribute a
          ON a.attrelid=d.adrelid AND a.attnum=d.adnum
          WHERE d.adrelid=key_table AND a.attname='created_at'
            AND regexp_replace(pg_get_expr(d.adbin,d.adrelid),'[()[:space:]]','','g')='clock_timestamp') THEN
    RAISE EXCEPTION 'Stage 59 key-ring table schema contract drifted';
  END IF;

  IF NOT EXISTS (SELECT 1 FROM pg_class c JOIN pg_namespace n ON n.oid=c.relnamespace
      WHERE n.nspname='public' AND c.relname='platform_data_protection_keys'
        AND c.relrowsecurity AND c.relforcerowsecurity)
     OR (SELECT count(*) FROM pg_policies WHERE schemaname='public'
          AND tablename='platform_data_protection_keys')<>1
     OR NOT EXISTS (SELECT 1 FROM pg_policies WHERE schemaname='public'
          AND tablename='platform_data_protection_keys' AND policyname='system_control_plane'
          AND cmd='ALL' AND roles='{opstrax_system}'::name[]
          AND qual='true' AND with_check='true') THEN
    RAISE EXCEPTION 'Stage 59 key-ring RLS contract unsafe';
  END IF;

  IF has_table_privilege('opstrax_app','public.platform_data_protection_keys','SELECT')
     OR has_table_privilege('opstrax_app','public.platform_data_protection_keys','INSERT')
     OR has_table_privilege('opstrax_app','public.platform_data_protection_keys','UPDATE')
     OR has_table_privilege('opstrax_app','public.platform_data_protection_keys','DELETE')
     OR NOT has_table_privilege('opstrax_system','public.platform_data_protection_keys','SELECT')
     OR NOT has_table_privilege('opstrax_system','public.platform_data_protection_keys','INSERT')
     OR has_table_privilege('opstrax_system','public.platform_data_protection_keys','UPDATE')
     OR has_table_privilege('opstrax_system','public.platform_data_protection_keys','DELETE') THEN
    RAISE EXCEPTION 'Stage 59 key-ring table privilege contract unsafe';
  END IF;

  IF has_sequence_privilege('opstrax_app','public.platform_data_protection_keys_id_seq','USAGE')
     OR NOT has_sequence_privilege('opstrax_system','public.platform_data_protection_keys_id_seq','USAGE')
     OR NOT has_sequence_privilege('opstrax_system','public.platform_data_protection_keys_id_seq','SELECT')
     OR has_sequence_privilege('opstrax_system','public.platform_data_protection_keys_id_seq','UPDATE') THEN
    RAISE EXCEPTION 'Stage 59 key-ring sequence privilege contract unsafe';
  END IF;
END
$verify$;

INSERT INTO schema_migrations(version,description)
VALUES ('2026_07_31_stage59_data_protection_key_ring','Shared certificate-encrypted Data Protection key repository')
ON CONFLICT(version) DO UPDATE SET description=EXCLUDED.description;

COMMIT;
