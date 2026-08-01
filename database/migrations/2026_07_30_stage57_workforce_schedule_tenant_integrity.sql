-- Stage 57 — Workforce schedule tenant and driver-ownership integrity
--
-- Workforce schedules were historically keyed only by the globally allocated
-- driver_id and had no tenant column or RLS. A tenant could therefore schedule a
-- foreign driver's id. This migration derives ownership from the authoritative
-- drivers row (never from a default tenant), rejects orphaned history atomically,
-- and installs the exact restricted-runtime contract.

BEGIN;

DO $workforce_prerequisites$
BEGIN
  IF to_regclass('public.drivers') IS NULL THEN
    RAISE EXCEPTION 'Stage 57 requires drivers; apply the core Fleet schema first';
  END IF;
  IF NOT EXISTS (SELECT 1 FROM pg_roles WHERE rolname='opstrax_app') THEN
    RAISE EXCEPTION 'Stage 57 requires restricted role opstrax_app; apply Stage 20 first';
  END IF;
END
$workforce_prerequisites$;

-- Restricted production never runs owner-only schema services. Build the final
-- table on a clean migration path as well as upgrading the historical shape.
CREATE TABLE IF NOT EXISTS workforce_schedules (
  id BIGINT GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
  company_id BIGINT NOT NULL,
  branch_id BIGINT NULL,
  driver_id BIGINT NOT NULL,
  week_start DATE NOT NULL,
  monday VARCHAR(40) NOT NULL DEFAULT 'Off',
  tuesday VARCHAR(40) NOT NULL DEFAULT 'Off',
  wednesday VARCHAR(40) NOT NULL DEFAULT 'Off',
  thursday VARCHAR(40) NOT NULL DEFAULT 'Off',
  friday VARCHAR(40) NOT NULL DEFAULT 'Off',
  saturday VARCHAR(40) NOT NULL DEFAULT 'Off',
  sunday VARCHAR(40) NOT NULL DEFAULT 'Off',
  created_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
  updated_at TIMESTAMPTZ NULL
);

ALTER TABLE workforce_schedules ADD COLUMN IF NOT EXISTS company_id BIGINT NULL;
ALTER TABLE workforce_schedules ADD COLUMN IF NOT EXISTS branch_id BIGINT NULL;

-- Driver ownership is authoritative. Repair any previously hand-added/drifted
-- ownership values and backfill both columns without assuming company 1.
UPDATE workforce_schedules ws
SET company_id=d.company_id,
    branch_id=d.branch_id
FROM drivers d
WHERE d.id=ws.driver_id
  AND (ws.company_id IS DISTINCT FROM d.company_id OR ws.branch_id IS DISTINCT FROM d.branch_id);

DO $workforce_preflight$
DECLARE
  orphan_count BIGINT;
  orphan_examples TEXT;
  duplicate_count BIGINT;
  duplicate_examples TEXT;
BEGIN
  SELECT COUNT(*) INTO orphan_count
  FROM workforce_schedules ws
  WHERE NOT EXISTS (SELECT 1 FROM drivers d WHERE d.id=ws.driver_id);
  IF orphan_count>0 THEN
    SELECT string_agg(format('schedule_id=%s driver_id=%s',id,driver_id),'; ' ORDER BY id)
      INTO orphan_examples
    FROM (SELECT id,driver_id FROM workforce_schedules ws
          WHERE NOT EXISTS (SELECT 1 FROM drivers d WHERE d.id=ws.driver_id)
          ORDER BY id LIMIT 10) examples;
    RAISE EXCEPTION USING
      MESSAGE=format('Stage 57 blocked: %s orphan workforce schedule row(s)',orphan_count),
      DETAIL=orphan_examples,
      HINT='Restore the referenced driver or remove the orphan schedule after an audited business decision, then rerun Stage 57.';
  END IF;

  SELECT COUNT(*) INTO duplicate_count
  FROM (SELECT company_id,driver_id,week_start FROM workforce_schedules
        GROUP BY company_id,driver_id,week_start HAVING COUNT(*)>1) duplicates;
  IF duplicate_count>0 THEN
    SELECT string_agg(example,'; ' ORDER BY example) INTO duplicate_examples
    FROM (
      SELECT format('company_id=%s driver_id=%s week_start=%s ids=%s',
               company_id,driver_id,week_start,array_agg(id ORDER BY id)::text) AS example
      FROM workforce_schedules
      GROUP BY company_id,driver_id,week_start HAVING COUNT(*)>1
      ORDER BY company_id,driver_id,week_start LIMIT 10
    ) examples;
    RAISE EXCEPTION USING
      MESSAGE=format('Stage 57 blocked: %s duplicate tenant/driver/week group(s)',duplicate_count),
      DETAIL=left(duplicate_examples,2000),
      HINT='Reconcile duplicate shift values without losing business history, then rerun Stage 57.';
  END IF;
END
$workforce_preflight$;

ALTER TABLE workforce_schedules ALTER COLUMN company_id SET NOT NULL;

-- Remove the legacy driver/week-only uniqueness contract. The canonical identity
-- explicitly includes tenant ownership even though driver ids are globally unique.
DO $drop_legacy_unique$
DECLARE rec RECORD;
BEGIN
  FOR rec IN
    SELECT con.conname
    FROM pg_constraint con
    WHERE con.conrelid='public.workforce_schedules'::regclass AND con.contype='u'
      AND (SELECT array_agg(att.attname ORDER BY key.ord)
           FROM unnest(con.conkey) WITH ORDINALITY key(attnum,ord)
           JOIN pg_attribute att ON att.attrelid=con.conrelid AND att.attnum=key.attnum)
          = ARRAY['driver_id','week_start']::name[]
  LOOP
    EXECUTE format('ALTER TABLE workforce_schedules DROP CONSTRAINT %I',rec.conname);
  END LOOP;
END
$drop_legacy_unique$;

-- A composite FK makes a forged company_id/driver_id pair impossible even for a
-- future write path that forgets endpoint validation.
ALTER TABLE workforce_schedules DROP CONSTRAINT IF EXISTS fk_workforce_schedules_tenant_driver;
DROP INDEX IF EXISTS uq_drivers_company_id_id;
CREATE UNIQUE INDEX uq_drivers_company_id_id ON drivers(company_id,id);
ALTER TABLE workforce_schedules
  ADD CONSTRAINT fk_workforce_schedules_tenant_driver
  FOREIGN KEY(company_id,driver_id) REFERENCES drivers(company_id,id) NOT VALID;
ALTER TABLE workforce_schedules VALIDATE CONSTRAINT fk_workforce_schedules_tenant_driver;

DROP INDEX IF EXISTS uq_workforce_schedules_tenant_driver_week;
CREATE UNIQUE INDEX uq_workforce_schedules_tenant_driver_week
  ON workforce_schedules(company_id,driver_id,week_start);
DROP INDEX IF EXISTS idx_workforce_schedules_tenant_week;
CREATE INDEX idx_workforce_schedules_tenant_week
  ON workforce_schedules(company_id,week_start);

ALTER TABLE workforce_schedules ENABLE ROW LEVEL SECURITY;
ALTER TABLE workforce_schedules FORCE ROW LEVEL SECURITY;
DO $workforce_policies$
DECLARE rec RECORD;
BEGIN
  FOR rec IN SELECT policyname FROM pg_policies
             WHERE schemaname='public' AND tablename='workforce_schedules'
  LOOP
    EXECUTE format('DROP POLICY %I ON workforce_schedules',rec.policyname);
  END LOOP;
  IF to_regprocedure('opstrax_security.current_tenant_id()') IS NOT NULL
     AND EXISTS(SELECT 1 FROM pg_roles WHERE rolname='opstrax_system') THEN
    CREATE POLICY tenant_ticket_app ON workforce_schedules FOR ALL TO opstrax_app
      USING (company_id=(SELECT opstrax_security.current_tenant_id()))
      WITH CHECK (company_id=(SELECT opstrax_security.current_tenant_id()));
    CREATE POLICY system_control_plane ON workforce_schedules FOR ALL TO opstrax_system USING(true) WITH CHECK(true);
    GRANT SELECT,INSERT,UPDATE,DELETE ON workforce_schedules TO opstrax_system;
    GRANT USAGE,SELECT ON SEQUENCE workforce_schedules_id_seq TO opstrax_system;
  ELSE
    CREATE POLICY tenant_isolation ON workforce_schedules FOR ALL
      USING (company_id=NULLIF(current_setting('app.current_tenant_id',true),'')::BIGINT)
      WITH CHECK (company_id=NULLIF(current_setting('app.current_tenant_id',true),'')::BIGINT);
    CREATE POLICY platform_admin_bypass ON workforce_schedules FOR ALL
      USING (NULLIF(current_setting('app.platform_admin',true),'')='on')
      WITH CHECK (NULLIF(current_setting('app.platform_admin',true),'')='on');
  END IF;
END
$workforce_policies$;

REVOKE ALL PRIVILEGES ON TABLE workforce_schedules FROM opstrax_app;
GRANT SELECT,INSERT,UPDATE ON TABLE workforce_schedules TO opstrax_app;
REVOKE ALL PRIVILEGES ON SEQUENCE workforce_schedules_id_seq FROM opstrax_app;
GRANT USAGE,SELECT ON SEQUENCE workforce_schedules_id_seq TO opstrax_app;

DO $workforce_verify$
DECLARE idx RECORD;
BEGIN
  IF NOT EXISTS (
    SELECT 1 FROM information_schema.columns
    WHERE table_schema='public' AND table_name='workforce_schedules'
      AND column_name='company_id' AND data_type='bigint' AND is_nullable='NO'
  ) OR NOT EXISTS (
    SELECT 1 FROM information_schema.columns
    WHERE table_schema='public' AND table_name='workforce_schedules'
      AND column_name='branch_id' AND data_type='bigint' AND is_nullable='YES'
  ) THEN
    RAISE EXCEPTION 'Stage 57 verification failed: workforce tenant/branch columns do not match the contract';
  END IF;
  IF EXISTS (
    SELECT 1 FROM workforce_schedules ws JOIN drivers d ON d.id=ws.driver_id
    WHERE ws.company_id IS DISTINCT FROM d.company_id OR ws.branch_id IS DISTINCT FROM d.branch_id
  ) OR EXISTS (
    SELECT 1 FROM workforce_schedules ws
    WHERE NOT EXISTS (SELECT 1 FROM drivers d WHERE d.id=ws.driver_id)
  ) THEN
    RAISE EXCEPTION 'Stage 57 verification failed: workforce ownership mismatch remains';
  END IF;
  IF NOT EXISTS (
    SELECT 1 FROM pg_constraint
    WHERE conrelid='public.workforce_schedules'::regclass
      AND conname='fk_workforce_schedules_tenant_driver' AND contype='f' AND convalidated
      AND pg_get_constraintdef(oid)='FOREIGN KEY (company_id, driver_id) REFERENCES drivers(company_id, id)'
  ) THEN
    RAISE EXCEPTION 'Stage 57 verification failed: workforce tenant/driver FK is absent or drifted';
  END IF;

  SELECT i.* INTO idx FROM pg_index i JOIN pg_class c ON c.oid=i.indexrelid
    JOIN pg_namespace n ON n.oid=c.relnamespace
    WHERE n.nspname='public' AND c.relname='uq_workforce_schedules_tenant_driver_week';
  IF idx IS NULL OR idx.indrelid<>'public.workforce_schedules'::regclass
     OR NOT idx.indisunique OR NOT idx.indisvalid OR NOT idx.indisready
     OR idx.indnkeyatts<>3 OR idx.indnatts<>3 OR idx.indpred IS NOT NULL
     OR pg_get_indexdef(idx.indexrelid,1,true)<>'company_id'
     OR pg_get_indexdef(idx.indexrelid,2,true)<>'driver_id'
     OR pg_get_indexdef(idx.indexrelid,3,true)<>'week_start' THEN
    RAISE EXCEPTION 'Stage 57 verification failed: workforce tenant/driver/week index is absent or drifted';
  END IF;
  IF NOT EXISTS (SELECT 1 FROM pg_class WHERE oid='public.workforce_schedules'::regclass
                 AND relrowsecurity AND relforcerowsecurity)
     OR (SELECT COUNT(*) FROM pg_policies WHERE schemaname='public' AND tablename='workforce_schedules')<>2
     OR NOT EXISTS (SELECT 1 FROM pg_policies WHERE schemaname='public' AND tablename='workforce_schedules'
                    AND permissive='PERMISSIVE'
                    AND ((to_regprocedure('opstrax_security.current_tenant_id()') IS NOT NULL
                          AND policyname='tenant_ticket_app' AND roles='{opstrax_app}'::name[]
                          AND qual LIKE '%opstrax_security.current_tenant_id()%' AND qual LIKE '%SELECT%' AND with_check=qual)
                      OR (to_regprocedure('opstrax_security.current_tenant_id()') IS NULL
                          AND policyname='tenant_isolation' AND roles='{public}'::name[])))
     OR NOT EXISTS (SELECT 1 FROM pg_policies WHERE schemaname='public' AND tablename='workforce_schedules'
                    AND permissive='PERMISSIVE'
                    AND ((to_regprocedure('opstrax_security.current_tenant_id()') IS NOT NULL
                          AND policyname='system_control_plane' AND roles='{opstrax_system}'::name[] AND qual='true' AND with_check='true')
                      OR (to_regprocedure('opstrax_security.current_tenant_id()') IS NULL
                          AND policyname='platform_admin_bypass' AND roles='{public}'::name[]))) THEN
    RAISE EXCEPTION 'Stage 57 verification failed: workforce RLS is absent or contains extra policy drift';
  END IF;
  IF NOT has_table_privilege('opstrax_app','workforce_schedules','SELECT')
     OR NOT has_table_privilege('opstrax_app','workforce_schedules','INSERT')
     OR NOT has_table_privilege('opstrax_app','workforce_schedules','UPDATE')
     OR has_table_privilege('opstrax_app','workforce_schedules','DELETE')
     OR has_table_privilege('opstrax_app','workforce_schedules','TRUNCATE')
     OR has_table_privilege('opstrax_app','workforce_schedules','REFERENCES')
     OR has_table_privilege('opstrax_app','workforce_schedules','TRIGGER')
     OR NOT has_sequence_privilege('opstrax_app','workforce_schedules_id_seq','USAGE')
     OR NOT has_sequence_privilege('opstrax_app','workforce_schedules_id_seq','SELECT')
     OR has_sequence_privilege('opstrax_app','workforce_schedules_id_seq','UPDATE') THEN
    RAISE EXCEPTION 'Stage 57 verification failed: workforce restricted-runtime grants are unsafe';
  END IF;
END
$workforce_verify$;

INSERT INTO schema_migrations(version,description)
VALUES ('2026_07_30_stage57_workforce_schedule_tenant_integrity',
        'Workforce schedule tenant/branch ownership, exact RLS, uniqueness, FK, and least privilege')
ON CONFLICT(version) DO NOTHING;

COMMIT;
