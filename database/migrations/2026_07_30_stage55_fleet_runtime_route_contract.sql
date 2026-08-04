-- Stage 55 — Fleet runtime route column contract
--
-- Production runs as opstrax_app and deliberately skips owner-capable schema
-- services. This migration mirrors the exact Batch5 carrier and canonical
-- telemetry columns consumed by FleetTmsEndpoints so a clean production install
-- cannot boot ready and then fail its first route with SQLSTATE 42703.

BEGIN;

-- Authentication is the entry point to every restricted Fleet route. Country
-- profile schema was previously owner-boot-only, while login projects both.
ALTER TABLE companies
  ADD COLUMN IF NOT EXISTS country VARCHAR(2) NULL,
  ADD COLUMN IF NOT EXISTS currency VARCHAR(8) NULL;
ALTER TABLE companies
  ALTER COLUMN country DROP NOT NULL,
  ALTER COLUMN country DROP DEFAULT,
  ALTER COLUMN currency DROP NOT NULL,
  ALTER COLUMN currency DROP DEFAULT;

-- Authorization decision logging runs before every guarded Fleet handler. The
-- historic foundation ledger could be present even when its table was not
-- installed by the production runner, so make this prerequisite self-contained.
CREATE TABLE IF NOT EXISTS authorization_decision_logs (
  id BIGINT GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
  tenant_id BIGINT NOT NULL,
  actor_type VARCHAR(40) NOT NULL,
  actor_id VARCHAR(120) NULL,
  permission_key VARCHAR(120) NOT NULL,
  resource_type VARCHAR(80) NOT NULL,
  resource_id VARCHAR(120) NULL,
  decision VARCHAR(32) NOT NULL,
  reason TEXT NOT NULL,
  correlation_id VARCHAR(120) NULL,
  request_id VARCHAR(120) NULL,
  created_at TIMESTAMPTZ NOT NULL DEFAULT NOW()
);
DROP INDEX IF EXISTS idx_auth_decision_tenant_created;
CREATE INDEX idx_auth_decision_tenant_created
  ON authorization_decision_logs(tenant_id,created_at DESC);
ALTER TABLE authorization_decision_logs ENABLE ROW LEVEL SECURITY;
ALTER TABLE authorization_decision_logs FORCE ROW LEVEL SECURITY;
DO $auth_policy_repair$
DECLARE policy_rec RECORD;
BEGIN
  FOR policy_rec IN
    SELECT policyname FROM pg_policies
    WHERE schemaname='public' AND tablename='authorization_decision_logs'
  LOOP
    EXECUTE format('DROP POLICY %I ON public.authorization_decision_logs',policy_rec.policyname);
  END LOOP;
  IF to_regprocedure('opstrax_security.current_tenant_id()') IS NOT NULL
     AND EXISTS(SELECT 1 FROM pg_roles WHERE rolname='opstrax_system') THEN
    CREATE POLICY tenant_ticket_app ON authorization_decision_logs FOR ALL TO opstrax_app
      USING (tenant_id=(SELECT opstrax_security.current_tenant_id()))
      WITH CHECK (tenant_id=(SELECT opstrax_security.current_tenant_id()));
    CREATE POLICY system_control_plane ON authorization_decision_logs FOR ALL TO opstrax_system
      USING (true) WITH CHECK (true);
    GRANT SELECT,INSERT ON authorization_decision_logs TO opstrax_system;
    GRANT USAGE,SELECT ON SEQUENCE authorization_decision_logs_id_seq TO opstrax_system;
  ELSE
    CREATE POLICY tenant_isolation ON authorization_decision_logs FOR ALL
      USING (tenant_id=NULLIF(current_setting('app.current_tenant_id',true),'')::bigint)
      WITH CHECK (tenant_id=NULLIF(current_setting('app.current_tenant_id',true),'')::bigint);
    CREATE POLICY platform_admin_bypass ON authorization_decision_logs FOR ALL
      USING (NULLIF(current_setting('app.platform_admin',true),'')='on')
      WITH CHECK (NULLIF(current_setting('app.platform_admin',true),'')='on');
  END IF;
END
$auth_policy_repair$;
REVOKE ALL PRIVILEGES ON authorization_decision_logs FROM opstrax_app;
GRANT SELECT,INSERT ON authorization_decision_logs TO opstrax_app;
REVOKE ALL PRIVILEGES ON SEQUENCE authorization_decision_logs_id_seq FROM opstrax_app;
GRANT USAGE,SELECT ON SEQUENCE authorization_decision_logs_id_seq TO opstrax_app;

ALTER TABLE carriers
  ADD COLUMN IF NOT EXISTS carrier_number VARCHAR(80) NULL,
  ADD COLUMN IF NOT EXISTS contact_name VARCHAR(160) NULL,
  ADD COLUMN IF NOT EXISTS phone VARCHAR(50) NULL,
  ADD COLUMN IF NOT EXISTS email VARCHAR(220) NULL,
  ADD COLUMN IF NOT EXISTS region VARCHAR(120) NULL,
  ADD COLUMN IF NOT EXISTS compliance_status VARCHAR(80) NOT NULL DEFAULT 'Compliant',
  ADD COLUMN IF NOT EXISTS insurance_expiry DATE NULL,
  ADD COLUMN IF NOT EXISTS contract_status VARCHAR(80) NOT NULL DEFAULT 'Active',
  ADD COLUMN IF NOT EXISTS on_time_percent DECIMAL(6,2) NOT NULL DEFAULT 90,
  ADD COLUMN IF NOT EXISTS safety_score DECIMAL(6,2) NOT NULL DEFAULT 88,
  ADD COLUMN IF NOT EXISTS cost_score DECIMAL(6,2) NOT NULL DEFAULT 82,
  ADD COLUMN IF NOT EXISTS performance_score DECIMAL(6,2) NOT NULL DEFAULT 86,
  ADD COLUMN IF NOT EXISTS risk_score DECIMAL(6,2) NOT NULL DEFAULT 20,
  ADD COLUMN IF NOT EXISTS recommended_action VARCHAR(260) NULL,
  ADD COLUMN IF NOT EXISTS notes TEXT NULL,
  ADD COLUMN IF NOT EXISTS updated_at TIMESTAMPTZ NULL,
  ADD COLUMN IF NOT EXISTS deleted_at TIMESTAMPTZ NULL;

UPDATE carriers
SET carrier_number=COALESCE(carrier_number,'CAR-'||LPAD(id::TEXT,5,'0'))
WHERE carrier_number IS NULL;
UPDATE carriers SET
  compliance_status=COALESCE(compliance_status,'Compliant'),
  contract_status=COALESCE(contract_status,'Active'),
  on_time_percent=COALESCE(on_time_percent,90),
  safety_score=COALESCE(safety_score,88),
  cost_score=COALESCE(cost_score,82),
  performance_score=COALESCE(performance_score,86),
  risk_score=COALESCE(risk_score,20);
ALTER TABLE carriers
  ALTER COLUMN carrier_number DROP NOT NULL, ALTER COLUMN carrier_number DROP DEFAULT,
  ALTER COLUMN contact_name DROP NOT NULL, ALTER COLUMN contact_name DROP DEFAULT,
  ALTER COLUMN phone DROP NOT NULL, ALTER COLUMN phone DROP DEFAULT,
  ALTER COLUMN email DROP NOT NULL, ALTER COLUMN email DROP DEFAULT,
  ALTER COLUMN region DROP NOT NULL, ALTER COLUMN region DROP DEFAULT,
  ALTER COLUMN compliance_status SET DEFAULT 'Compliant', ALTER COLUMN compliance_status SET NOT NULL,
  ALTER COLUMN insurance_expiry DROP NOT NULL, ALTER COLUMN insurance_expiry DROP DEFAULT,
  ALTER COLUMN contract_status SET DEFAULT 'Active', ALTER COLUMN contract_status SET NOT NULL,
  ALTER COLUMN on_time_percent SET DEFAULT 90, ALTER COLUMN on_time_percent SET NOT NULL,
  ALTER COLUMN safety_score SET DEFAULT 88, ALTER COLUMN safety_score SET NOT NULL,
  ALTER COLUMN cost_score SET DEFAULT 82, ALTER COLUMN cost_score SET NOT NULL,
  ALTER COLUMN performance_score SET DEFAULT 86, ALTER COLUMN performance_score SET NOT NULL,
  ALTER COLUMN risk_score SET DEFAULT 20, ALTER COLUMN risk_score SET NOT NULL,
  ALTER COLUMN recommended_action DROP NOT NULL, ALTER COLUMN recommended_action DROP DEFAULT,
  ALTER COLUMN notes DROP NOT NULL, ALTER COLUMN notes DROP DEFAULT,
  ALTER COLUMN updated_at DROP NOT NULL, ALTER COLUMN updated_at DROP DEFAULT,
  ALTER COLUMN deleted_at DROP NOT NULL, ALTER COLUMN deleted_at DROP DEFAULT;

DROP INDEX IF EXISTS ix_b5_carriers;
CREATE INDEX ix_b5_carriers
  ON carriers(company_id,status,compliance_status,risk_score);

ALTER TABLE latest_vehicle_positions
  ADD COLUMN IF NOT EXISTS source_event_id BIGINT NULL,
  ADD COLUMN IF NOT EXISTS correlation_id VARCHAR(120) NULL,
  ADD COLUMN IF NOT EXISTS causation_id VARCHAR(120) NULL,
  ADD COLUMN IF NOT EXISTS source_channel VARCHAR(40) NULL,
  ADD COLUMN IF NOT EXISTS source TEXT NULL,
  ADD COLUMN IF NOT EXISTS provider TEXT NULL,
  ADD COLUMN IF NOT EXISTS protocol TEXT NULL,
  ADD COLUMN IF NOT EXISTS adapter_version TEXT NULL,
  ADD COLUMN IF NOT EXISTS device_fix_time TIMESTAMPTZ NULL,
  ADD COLUMN IF NOT EXISTS gateway_received_at TIMESTAMPTZ NULL,
  ADD COLUMN IF NOT EXISTS normalized_at TIMESTAMPTZ NULL,
  ADD COLUMN IF NOT EXISTS confidence NUMERIC(4,3) NULL,
  ADD COLUMN IF NOT EXISTS trust_score NUMERIC(4,3) NULL,
  ADD COLUMN IF NOT EXISTS quality_flags JSONB NULL;
ALTER TABLE latest_vehicle_positions
  ALTER COLUMN source_event_id DROP NOT NULL, ALTER COLUMN source_event_id DROP DEFAULT,
  ALTER COLUMN correlation_id DROP NOT NULL, ALTER COLUMN correlation_id DROP DEFAULT,
  ALTER COLUMN causation_id DROP NOT NULL, ALTER COLUMN causation_id DROP DEFAULT,
  ALTER COLUMN source_channel DROP NOT NULL, ALTER COLUMN source_channel DROP DEFAULT,
  ALTER COLUMN source DROP NOT NULL, ALTER COLUMN source DROP DEFAULT,
  ALTER COLUMN provider DROP NOT NULL, ALTER COLUMN provider DROP DEFAULT,
  ALTER COLUMN protocol DROP NOT NULL, ALTER COLUMN protocol DROP DEFAULT,
  ALTER COLUMN adapter_version DROP NOT NULL, ALTER COLUMN adapter_version DROP DEFAULT,
  ALTER COLUMN device_fix_time DROP NOT NULL, ALTER COLUMN device_fix_time DROP DEFAULT,
  ALTER COLUMN gateway_received_at DROP NOT NULL, ALTER COLUMN gateway_received_at DROP DEFAULT,
  ALTER COLUMN normalized_at DROP NOT NULL, ALTER COLUMN normalized_at DROP DEFAULT,
  ALTER COLUMN confidence DROP NOT NULL, ALTER COLUMN confidence DROP DEFAULT,
  ALTER COLUMN trust_score DROP NOT NULL, ALTER COLUMN trust_score DROP DEFAULT,
  ALTER COLUMN quality_flags DROP NOT NULL, ALTER COLUMN quality_flags DROP DEFAULT;

ALTER TABLE latest_vehicle_positions DROP CONSTRAINT IF EXISTS ck_lvp_confidence_range;
ALTER TABLE latest_vehicle_positions ADD CONSTRAINT ck_lvp_confidence_range
  CHECK (confidence IS NULL OR (confidence>=0 AND confidence<=1));
ALTER TABLE latest_vehicle_positions DROP CONSTRAINT IF EXISTS ck_lvp_trust_score_range;
ALTER TABLE latest_vehicle_positions ADD CONSTRAINT ck_lvp_trust_score_range
  CHECK (trust_score IS NULL OR (trust_score>=0 AND trust_score<=1));

UPDATE latest_vehicle_positions
SET source=COALESCE(source,'legacy'),
    device_fix_time=COALESCE(device_fix_time,event_time),
    gateway_received_at=COALESCE(gateway_received_at,received_at),
    normalized_at=COALESCE(normalized_at,received_at)
WHERE source IS NULL OR device_fix_time IS NULL
   OR gateway_received_at IS NULL OR normalized_at IS NULL;

DROP INDEX IF EXISTS idx_lvp_company_correlation;
CREATE INDEX idx_lvp_company_correlation
  ON latest_vehicle_positions(company_id,correlation_id)
  WHERE correlation_id IS NOT NULL;

-- The Fleet tracking union directly projects both fields from location_events.
-- Stage 51 created the table but omitted the owner-only TelemetrySchemaService
-- enrichment, so install the complete provenance envelope it relies on.
ALTER TABLE location_events
  ADD COLUMN IF NOT EXISTS source VARCHAR(40) NOT NULL DEFAULT 'device',
  ADD COLUMN IF NOT EXISTS nonce VARCHAR(128) NULL,
  ADD COLUMN IF NOT EXISTS source_channel VARCHAR(40) NULL,
  ADD COLUMN IF NOT EXISTS correlation_id VARCHAR(120) NULL,
  ADD COLUMN IF NOT EXISTS causation_id VARCHAR(120) NULL,
  ADD COLUMN IF NOT EXISTS client_generated_id VARCHAR(120) NULL,
  ADD COLUMN IF NOT EXISTS idempotency_key VARCHAR(120) NULL;
ALTER TABLE location_events
  ALTER COLUMN source SET DEFAULT 'device', ALTER COLUMN source SET NOT NULL,
  ALTER COLUMN nonce DROP NOT NULL, ALTER COLUMN nonce DROP DEFAULT,
  ALTER COLUMN source_channel DROP NOT NULL, ALTER COLUMN source_channel DROP DEFAULT,
  ALTER COLUMN correlation_id DROP NOT NULL, ALTER COLUMN correlation_id DROP DEFAULT,
  ALTER COLUMN causation_id DROP NOT NULL, ALTER COLUMN causation_id DROP DEFAULT,
  ALTER COLUMN client_generated_id DROP NOT NULL, ALTER COLUMN client_generated_id DROP DEFAULT,
  ALTER COLUMN idempotency_key DROP NOT NULL, ALTER COLUMN idempotency_key DROP DEFAULT;

DROP INDEX IF EXISTS idx_le_received;
CREATE INDEX idx_le_received ON location_events(company_id,received_at);

DO $verify$
DECLARE
  missing TEXT[];
BEGIN
  WITH required(table_name,column_name,data_type,not_null,column_default,identity_kind) AS (VALUES
    ('companies','country','character varying(2)',false,'',''),
    ('companies','currency','character varying(8)',false,'',''),
    ('authorization_decision_logs','id','bigint',true,'','a'),
    ('authorization_decision_logs','tenant_id','bigint',true,'',''),
    ('authorization_decision_logs','actor_type','character varying(40)',true,'',''),
    ('authorization_decision_logs','actor_id','character varying(120)',false,'',''),
    ('authorization_decision_logs','permission_key','character varying(120)',true,'',''),
    ('authorization_decision_logs','resource_type','character varying(80)',true,'',''),
    ('authorization_decision_logs','resource_id','character varying(120)',false,'',''),
    ('authorization_decision_logs','decision','character varying(32)',true,'',''),
    ('authorization_decision_logs','reason','text',true,'',''),
    ('authorization_decision_logs','correlation_id','character varying(120)',false,'',''),
    ('authorization_decision_logs','request_id','character varying(120)',false,'',''),
    ('authorization_decision_logs','created_at','timestamp with time zone',true,'now()',''),
    ('carriers','carrier_number','character varying(80)',false,'',''),
    ('carriers','contact_name','character varying(160)',false,'',''),
    ('carriers','phone','character varying(50)',false,'',''),
    ('carriers','email','character varying(220)',false,'',''),
    ('carriers','region','character varying(120)',false,'',''),
    ('carriers','compliance_status','character varying(80)',true,'''Compliant''::character varying',''),
    ('carriers','insurance_expiry','date',false,'',''),
    ('carriers','contract_status','character varying(80)',true,'''Active''::character varying',''),
    ('carriers','on_time_percent','numeric(6,2)',true,'90',''),
    ('carriers','safety_score','numeric(6,2)',true,'88',''),
    ('carriers','cost_score','numeric(6,2)',true,'82',''),
    ('carriers','performance_score','numeric(6,2)',true,'86',''),
    ('carriers','risk_score','numeric(6,2)',true,'20',''),
    ('carriers','recommended_action','character varying(260)',false,'',''),
    ('carriers','notes','text',false,'',''),
    ('carriers','updated_at','timestamp with time zone',false,'',''),
    ('carriers','deleted_at','timestamp with time zone',false,'',''),
    ('latest_vehicle_positions','source_event_id','bigint',false,'',''),
    ('latest_vehicle_positions','correlation_id','character varying(120)',false,'',''),
    ('latest_vehicle_positions','causation_id','character varying(120)',false,'',''),
    ('latest_vehicle_positions','source_channel','character varying(40)',false,'',''),
    ('latest_vehicle_positions','source','text',false,'',''),
    ('latest_vehicle_positions','provider','text',false,'',''),
    ('latest_vehicle_positions','protocol','text',false,'',''),
    ('latest_vehicle_positions','adapter_version','text',false,'',''),
    ('latest_vehicle_positions','device_fix_time','timestamp with time zone',false,'',''),
    ('latest_vehicle_positions','gateway_received_at','timestamp with time zone',false,'',''),
    ('latest_vehicle_positions','normalized_at','timestamp with time zone',false,'',''),
    ('latest_vehicle_positions','confidence','numeric(4,3)',false,'',''),
    ('latest_vehicle_positions','trust_score','numeric(4,3)',false,'',''),
    ('latest_vehicle_positions','quality_flags','jsonb',false,'',''),
    ('location_events','source','character varying(40)',true,'''device''::character varying',''),
    ('location_events','nonce','character varying(128)',false,'',''),
    ('location_events','source_channel','character varying(40)',false,'',''),
    ('location_events','correlation_id','character varying(120)',false,'',''),
    ('location_events','causation_id','character varying(120)',false,'',''),
    ('location_events','client_generated_id','character varying(120)',false,'',''),
    ('location_events','idempotency_key','character varying(120)',false,'','')
  )
  SELECT array_agg(r.table_name||'.'||r.column_name ORDER BY r.table_name,r.column_name) INTO missing
  FROM required r
  LEFT JOIN pg_class tbl ON tbl.oid=to_regclass('public.'||r.table_name)
  LEFT JOIN pg_attribute a ON a.attrelid=tbl.oid AND a.attname=r.column_name AND a.attnum>0 AND NOT a.attisdropped
  LEFT JOIN pg_attrdef d ON d.adrelid=tbl.oid AND d.adnum=a.attnum
  WHERE a.attname IS NULL
     OR format_type(a.atttypid,a.atttypmod)<>r.data_type
     OR a.attnotnull<>r.not_null
     OR COALESCE(pg_get_expr(d.adbin,d.adrelid),'')<>r.column_default
     OR a.attidentity::text<>r.identity_kind;
  IF COALESCE(cardinality(missing),0)>0 THEN
    RAISE EXCEPTION 'Stage 55 Fleet runtime route columns missing or drifted: %',missing;
  END IF;

  IF NOT EXISTS (SELECT 1 FROM pg_class c WHERE c.oid=to_regclass('public.authorization_decision_logs')
      AND c.relrowsecurity AND c.relforcerowsecurity)
     OR (SELECT COUNT(*) FROM pg_policies
         WHERE schemaname='public' AND tablename='authorization_decision_logs')<>2
     OR NOT EXISTS (SELECT 1 FROM pg_policies
         WHERE schemaname='public' AND tablename='authorization_decision_logs'
           AND ((to_regprocedure('opstrax_security.current_tenant_id()') IS NOT NULL
                 AND policyname='tenant_ticket_app' AND roles='{opstrax_app}'::name[]
                 AND qual LIKE '%opstrax_security.current_tenant_id()%' AND qual LIKE '%SELECT%' AND with_check=qual)
             OR (to_regprocedure('opstrax_security.current_tenant_id()') IS NULL
                 AND policyname='tenant_isolation' AND roles='{public}'::name[])))
     OR NOT EXISTS (SELECT 1 FROM pg_policies
         WHERE schemaname='public' AND tablename='authorization_decision_logs'
           AND ((to_regprocedure('opstrax_security.current_tenant_id()') IS NOT NULL
                 AND policyname='system_control_plane' AND roles='{opstrax_system}'::name[] AND qual='true' AND with_check='true')
             OR (to_regprocedure('opstrax_security.current_tenant_id()') IS NULL
                 AND policyname='platform_admin_bypass' AND roles='{public}'::name[])))
     OR NOT has_table_privilege('opstrax_app','public.authorization_decision_logs','SELECT')
     OR NOT has_table_privilege('opstrax_app','public.authorization_decision_logs','INSERT')
     OR has_table_privilege('opstrax_app','public.authorization_decision_logs','UPDATE')
     OR has_table_privilege('opstrax_app','public.authorization_decision_logs','DELETE')
     OR NOT has_sequence_privilege('opstrax_app','public.authorization_decision_logs_id_seq','USAGE')
     OR NOT has_sequence_privilege('opstrax_app','public.authorization_decision_logs_id_seq','SELECT')
     OR has_sequence_privilege('opstrax_app','public.authorization_decision_logs_id_seq','UPDATE') THEN
    RAISE EXCEPTION 'Stage 55 authorization decision log RLS/grant contract incomplete';
  END IF;

  IF EXISTS (
    SELECT 1 FROM (VALUES
      ('ck_lvp_confidence_range','confidence IS NULL OR confidence >= 0::numeric AND confidence <= 1::numeric'),
      ('ck_lvp_trust_score_range','trust_score IS NULL OR trust_score >= 0::numeric AND trust_score <= 1::numeric')
    ) expected(name,expression)
    LEFT JOIN pg_constraint c ON c.conname=expected.name AND c.conrelid='public.latest_vehicle_positions'::regclass
    WHERE c.oid IS NULL OR NOT c.convalidated OR pg_get_expr(c.conbin,c.conrelid,true)<>expected.expression
  ) THEN
    RAISE EXCEPTION 'Stage 55 Fleet runtime route constraints missing or drifted';
  END IF;

  IF EXISTS (
    SELECT 1 FROM (VALUES
      ('idx_auth_decision_tenant_created','CREATE INDEX idx_auth_decision_tenant_created ON public.authorization_decision_logs USING btree (tenant_id, created_at DESC)'),
      ('ix_b5_carriers','CREATE INDEX ix_b5_carriers ON public.carriers USING btree (company_id, status, compliance_status, risk_score)'),
      ('idx_lvp_company_correlation','CREATE INDEX idx_lvp_company_correlation ON public.latest_vehicle_positions USING btree (company_id, correlation_id) WHERE (correlation_id IS NOT NULL)'),
      ('idx_le_received','CREATE INDEX idx_le_received ON public.location_events USING btree (company_id, received_at)')
    ) expected(name,definition)
    LEFT JOIN pg_class idx ON idx.oid=to_regclass('public.'||expected.name)
    LEFT JOIN pg_index i ON i.indexrelid=idx.oid
    WHERE idx.oid IS NULL OR i.indisunique OR NOT i.indisvalid OR NOT i.indisready
      OR pg_get_indexdef(idx.oid)<>expected.definition
  ) THEN
    RAISE EXCEPTION 'Stage 55 Fleet runtime route indexes missing, invalid, or drifted';
  END IF;
END
$verify$;

INSERT INTO schema_migrations(version,description)
VALUES ('2026_07_30_stage55_fleet_runtime_route_contract','Fleet carrier and canonical telemetry runtime route columns')
ON CONFLICT(version) DO NOTHING;

COMMIT;
