-- Stage 83 — protected-environment company security settings runtime contract
--
-- Protected environments skip owner-capable runtime schema initialization. The
-- tenant login path nevertheless reads this table when calculating absolute
-- session expiry, so its absence turns every valid password login into SQLSTATE
-- 42P01. Materialize the exact SecuritySchemaService contract out-of-band.
BEGIN;

CREATE TABLE IF NOT EXISTS public.company_security_settings (
  id bigint GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
  company_id bigint NOT NULL UNIQUE,
  mfa_required boolean NOT NULL DEFAULT false,
  mfa_required_roles jsonb,
  password_min_length integer NOT NULL DEFAULT 8,
  password_requires_uppercase boolean NOT NULL DEFAULT false,
  password_requires_number boolean NOT NULL DEFAULT false,
  password_requires_symbol boolean NOT NULL DEFAULT false,
  password_expiry_days integer NOT NULL DEFAULT 0,
  session_idle_timeout_minutes integer NOT NULL DEFAULT 60,
  session_absolute_timeout_minutes integer NOT NULL DEFAULT 480,
  max_failed_login_attempts integer NOT NULL DEFAULT 5,
  lockout_duration_minutes integer NOT NULL DEFAULT 30,
  allowed_sso_providers jsonb,
  export_approval_required boolean NOT NULL DEFAULT false,
  audit_retention_days integer NOT NULL DEFAULT 90,
  data_retention_days integer NOT NULL DEFAULT 365,
  created_at timestamptz NOT NULL DEFAULT now(),
  updated_at timestamptz NOT NULL DEFAULT now(),
  updated_by varchar(200)
);

CREATE INDEX IF NOT EXISTS idx_css_company
  ON public.company_security_settings(company_id);

-- PasswordPolicyService and the login/reset paths consume these columns before
-- a session can be issued. They were historically owned only by the skipped
-- runtime SecuritySchemaService.
ALTER TABLE public.users
  ADD COLUMN IF NOT EXISTS failed_login_attempts integer NOT NULL DEFAULT 0;
ALTER TABLE public.users
  ADD COLUMN IF NOT EXISTS locked_until timestamptz;
ALTER TABLE public.users
  ADD COLUMN IF NOT EXISTS force_password_change boolean NOT NULL DEFAULT false;
ALTER TABLE public.users
  ADD COLUMN IF NOT EXISTS password_changed_at timestamptz;

CREATE TABLE IF NOT EXISTS public.security_events (
  id bigint GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
  company_id bigint NOT NULL,
  user_id bigint,
  event_type varchar(100) NOT NULL,
  severity varchar(20) NOT NULL DEFAULT 'info'
    CHECK (severity IN ('critical','high','medium','low','info')),
  source_ip_truncated varchar(30),
  user_agent_hash varchar(16),
  success boolean NOT NULL DEFAULT true,
  safe_message varchar(500) NOT NULL,
  metadata_json jsonb,
  created_at timestamptz NOT NULL DEFAULT now()
);

CREATE INDEX IF NOT EXISTS idx_se_company ON public.security_events(company_id);
CREATE INDEX IF NOT EXISTS idx_se_type ON public.security_events(event_type);
CREATE INDEX IF NOT EXISTS idx_se_created ON public.security_events(created_at);
CREATE INDEX IF NOT EXISTS idx_se_user ON public.security_events(user_id);

ALTER TABLE public.security_events ENABLE ROW LEVEL SECURITY;
ALTER TABLE public.security_events FORCE ROW LEVEL SECURITY;
-- Policy/grant enrolment is guarded (stage65 pattern): on a fresh database the
-- opstrax_security schema (stage58) may not exist yet; the RLS reconciliation
-- pass enrolls these tables once the security cutover has run.
DO $$
BEGIN
  IF EXISTS (SELECT 1 FROM pg_roles WHERE rolname='opstrax_app')
     AND to_regprocedure('opstrax_security.current_tenant_id()') IS NOT NULL THEN
    DROP POLICY IF EXISTS tenant_ticket_app ON public.security_events;
    CREATE POLICY tenant_ticket_app ON public.security_events
      AS PERMISSIVE FOR ALL TO opstrax_app
      USING (company_id=(SELECT opstrax_security.current_tenant_id()))
      WITH CHECK (company_id=(SELECT opstrax_security.current_tenant_id()));
    REVOKE ALL ON TABLE public.security_events FROM opstrax_app;
    GRANT SELECT,INSERT ON TABLE public.security_events TO opstrax_app;
    GRANT USAGE,SELECT ON SEQUENCE public.security_events_id_seq TO opstrax_app;
  END IF;
  IF EXISTS (SELECT 1 FROM pg_roles WHERE rolname='opstrax_system') THEN
    DROP POLICY IF EXISTS system_control_plane ON public.security_events;
    CREATE POLICY system_control_plane ON public.security_events
      AS PERMISSIVE FOR ALL TO opstrax_system
      USING (true) WITH CHECK (true);
    GRANT SELECT,INSERT,UPDATE,DELETE ON TABLE public.security_events TO opstrax_system;
    GRANT USAGE,SELECT ON SEQUENCE public.security_events_id_seq TO opstrax_system;
  END IF;
END $$;

DO $stage83_fk$
BEGIN
  IF NOT EXISTS (
    SELECT 1 FROM pg_constraint
    WHERE conrelid='public.company_security_settings'::regclass
      AND conname='fk_css_company'
  ) THEN
    ALTER TABLE public.company_security_settings
      ADD CONSTRAINT fk_css_company
      FOREIGN KEY(company_id) REFERENCES public.companies(id) NOT VALID;
  END IF;
END
$stage83_fk$;

ALTER TABLE public.company_security_settings
  VALIDATE CONSTRAINT fk_css_company;
ALTER TABLE public.company_security_settings ENABLE ROW LEVEL SECURITY;
ALTER TABLE public.company_security_settings FORCE ROW LEVEL SECURITY;

-- Guarded like the security_events block above: fresh databases may not have the
-- opstrax_security schema (stage58) yet.
DO $$
BEGIN
  IF EXISTS (SELECT 1 FROM pg_roles WHERE rolname='opstrax_app')
     AND to_regprocedure('opstrax_security.current_tenant_id()') IS NOT NULL THEN
    DROP POLICY IF EXISTS tenant_ticket_app ON public.company_security_settings;
    CREATE POLICY tenant_ticket_app ON public.company_security_settings
      AS PERMISSIVE FOR ALL TO opstrax_app
      USING (company_id=(SELECT opstrax_security.current_tenant_id()))
      WITH CHECK (company_id=(SELECT opstrax_security.current_tenant_id()));
    REVOKE ALL ON TABLE public.company_security_settings FROM opstrax_app;
    GRANT SELECT,INSERT,UPDATE ON TABLE public.company_security_settings TO opstrax_app;
    GRANT USAGE,SELECT ON SEQUENCE public.company_security_settings_id_seq TO opstrax_app;
  END IF;
  IF EXISTS (SELECT 1 FROM pg_roles WHERE rolname='opstrax_system') THEN
    DROP POLICY IF EXISTS system_control_plane ON public.company_security_settings;
    CREATE POLICY system_control_plane ON public.company_security_settings
      AS PERMISSIVE FOR ALL TO opstrax_system
      USING (true) WITH CHECK (true);
    GRANT SELECT,INSERT,UPDATE,DELETE ON TABLE public.company_security_settings TO opstrax_system;
    GRANT USAGE,SELECT ON SEQUENCE public.company_security_settings_id_seq TO opstrax_system;
  END IF;
END $$;

INSERT INTO public.schema_migrations(version,description)
VALUES (
  '2026_08_21_stage83_company_security_settings_runtime_contract',
  'Materialize security settings, lockout columns and append-only security events required by protected-environment login'
)
ON CONFLICT(version) DO NOTHING;

COMMIT;
