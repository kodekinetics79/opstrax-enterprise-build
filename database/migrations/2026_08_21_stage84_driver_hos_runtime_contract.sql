-- Stage 84 — protected-environment driver/HOS runtime contract
-- Protected environments skip owner-capable runtime schema initialization, but
-- dispatch eligibility and driver portal paths require these objects.
BEGIN;

ALTER TABLE public.drivers ADD COLUMN IF NOT EXISTS user_id bigint;
ALTER TABLE public.coaching_tasks ADD COLUMN IF NOT EXISTS acknowledged_note text;

DROP INDEX IF EXISTS public.idx_drivers_user_id;
CREATE UNIQUE INDEX IF NOT EXISTS uq_drivers_user_id
  ON public.drivers(user_id) WHERE user_id IS NOT NULL AND deleted_at IS NULL;

CREATE TABLE IF NOT EXISTS public.driver_offline_queue (
  id bigint GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
  company_id bigint NOT NULL,
  driver_id bigint NOT NULL,
  idempotency_key varchar(64) NOT NULL UNIQUE,
  action_type varchar(60) NOT NULL,
  payload_json jsonb NOT NULL,
  status varchar(20) NOT NULL DEFAULT 'pending',
  processed_at timestamptz,
  error_message text,
  created_at timestamptz NOT NULL DEFAULT now()
);
CREATE INDEX IF NOT EXISTS idx_dq_driver_status ON public.driver_offline_queue(driver_id,status);
CREATE INDEX IF NOT EXISTS idx_dq_company ON public.driver_offline_queue(company_id);

CREATE TABLE IF NOT EXISTS public.hos_records (
  id bigint GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
  company_id bigint,
  branch_id bigint,
  driver_id bigint NOT NULL,
  shift_date date NOT NULL DEFAULT current_date,
  remaining_drive_hours numeric(5,2),
  remaining_shift_hours numeric(5,2),
  remaining_cycle_hours numeric(5,2),
  hos_status varchar(40),
  eld_device_id bigint,
  created_at timestamptz NOT NULL DEFAULT now()
);
CREATE INDEX IF NOT EXISTS idx_hos_driver_shift ON public.hos_records(driver_id,shift_date DESC);
CREATE INDEX IF NOT EXISTS idx_hos_company_branch_shift ON public.hos_records(company_id,branch_id,driver_id,shift_date DESC);

ALTER TABLE public.driver_offline_queue ENABLE ROW LEVEL SECURITY;
ALTER TABLE public.driver_offline_queue FORCE ROW LEVEL SECURITY;
ALTER TABLE public.hos_records ENABLE ROW LEVEL SECURITY;
ALTER TABLE public.hos_records FORCE ROW LEVEL SECURITY;
-- Policy/grant enrolment is guarded (stage65 pattern): on a fresh database the
-- opstrax_security schema (stage58) may not exist yet; the RLS reconciliation
-- pass enrolls these tables once the security cutover has run.
DO $$
BEGIN
  IF EXISTS (SELECT 1 FROM pg_roles WHERE rolname='opstrax_app')
     AND to_regprocedure('opstrax_security.current_tenant_id()') IS NOT NULL THEN
    DROP POLICY IF EXISTS tenant_ticket_app ON public.driver_offline_queue;
    CREATE POLICY tenant_ticket_app ON public.driver_offline_queue
      AS PERMISSIVE FOR ALL TO opstrax_app
      USING (company_id=(SELECT opstrax_security.current_tenant_id()))
      WITH CHECK (company_id=(SELECT opstrax_security.current_tenant_id()));
    DROP POLICY IF EXISTS tenant_ticket_app ON public.hos_records;
    CREATE POLICY tenant_ticket_app ON public.hos_records
      AS PERMISSIVE FOR ALL TO opstrax_app
      USING (company_id=(SELECT opstrax_security.current_tenant_id()))
      WITH CHECK (company_id=(SELECT opstrax_security.current_tenant_id()));
    REVOKE ALL ON TABLE public.driver_offline_queue FROM opstrax_app;
    REVOKE ALL ON TABLE public.hos_records FROM opstrax_app;
    GRANT SELECT,INSERT,UPDATE,DELETE ON TABLE public.driver_offline_queue TO opstrax_app;
    GRANT SELECT,INSERT,UPDATE,DELETE ON TABLE public.hos_records TO opstrax_app;
    GRANT USAGE,SELECT ON SEQUENCE public.driver_offline_queue_id_seq TO opstrax_app;
    GRANT USAGE,SELECT ON SEQUENCE public.hos_records_id_seq TO opstrax_app;
  END IF;
  IF EXISTS (SELECT 1 FROM pg_roles WHERE rolname='opstrax_system') THEN
    DROP POLICY IF EXISTS system_control_plane ON public.driver_offline_queue;
    CREATE POLICY system_control_plane ON public.driver_offline_queue
      AS PERMISSIVE FOR ALL TO opstrax_system USING (true) WITH CHECK (true);
    DROP POLICY IF EXISTS system_control_plane ON public.hos_records;
    CREATE POLICY system_control_plane ON public.hos_records
      AS PERMISSIVE FOR ALL TO opstrax_system USING (true) WITH CHECK (true);
    GRANT SELECT,INSERT,UPDATE,DELETE ON TABLE public.driver_offline_queue TO opstrax_system;
    GRANT SELECT,INSERT,UPDATE,DELETE ON TABLE public.hos_records TO opstrax_system;
    GRANT USAGE,SELECT ON SEQUENCE public.driver_offline_queue_id_seq TO opstrax_system;
    GRANT USAGE,SELECT ON SEQUENCE public.hos_records_id_seq TO opstrax_system;
  END IF;
END $$;

INSERT INTO public.schema_migrations(version,description)
VALUES ('2026_08_21_stage84_driver_hos_runtime_contract','Materialize driver portal, offline queue and HOS objects required by protected-environment dispatch paths')
ON CONFLICT(version) DO NOTHING;
COMMIT;
