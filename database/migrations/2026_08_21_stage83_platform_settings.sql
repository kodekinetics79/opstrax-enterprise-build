-- ─────────────────────────────────────────────────────────────────────────────
-- Stage 83 — platform_settings: operator-editable platform configuration
--
-- WHY THIS MIGRATION EXISTS (production incident, 2026-08-21):
-- The Email & SMTP console page (#29) stores its configuration in
-- platform_settings, created by PlatformSettingsService.EnsureSchemaAsync. That
-- ensure ran INSIDE the boot schema-init gate, which production deliberately
-- skips (restricted opstrax_app role, RLS enforced, owner applies migrations
-- out-of-band). So the table never existed in production: reading degraded to
-- the env fallback (the page loaded), but SAVING threw 42P01 — surfaced to the
-- operator as "Internal server error". Under RLS every /api/platform/* request
-- shares one transaction, so the failed statement also poisoned the rest of the
-- request. This migration is the documented out-of-band path; the API now also
-- best-effort-ensures the table at boot OUTSIDE the gate for deployments whose
-- system identity is allowed to create it.
--
-- Apply as the database OWNER (same flow as every stage file):
--   psql "$NEON_OWNER_URL" -f database/migrations/2026_08_21_stage83_platform_settings.sql
--
-- Idempotent: CREATE TABLE IF NOT EXISTS + guarded policy/grant re-application.
-- Creates no rows. Values marked secret are AES-GCM envelopes (PiiProtectionService),
-- so nothing in this table is ever plaintext credential material.
-- ─────────────────────────────────────────────────────────────────────────────

BEGIN;

CREATE TABLE IF NOT EXISTS platform_settings (
    setting_key   VARCHAR(120)  NOT NULL PRIMARY KEY,
    setting_value TEXT          NULL,
    is_secret     BOOLEAN       NOT NULL DEFAULT false,
    updated_by    VARCHAR(220)  NULL,
    updated_at    TIMESTAMPTZ   NOT NULL DEFAULT NOW()
);

-- ── RLS enrolment ────────────────────────────────────────────────────────────
-- Control-plane artifact holding platform-wide (never tenant) configuration,
-- including encrypted SMTP credentials. Same classification as the stage 26/81
-- platform tables: FORCE RLS with a single system_control_plane policy, so the
-- tenant runtime role (opstrax_app) can never read it even via SQL injection —
-- only the separately-authenticated opstrax_system identity that serves
-- /api/platform/* requests.
DO $$
DECLARE
    pol RECORD;
    tbl_owner TEXT;
BEGIN
    IF EXISTS (SELECT 1 FROM pg_roles WHERE rolname = 'opstrax_system') THEN
        IF to_regclass('public.platform_settings') IS NOT NULL THEN
            BEGIN
                EXECUTE 'ALTER TABLE public.platform_settings ENABLE ROW LEVEL SECURITY';
                EXECUTE 'ALTER TABLE public.platform_settings FORCE ROW LEVEL SECURITY';
                FOR pol IN SELECT policyname FROM pg_policies
                            WHERE schemaname='public' AND tablename='platform_settings' LOOP
                    EXECUTE format('DROP POLICY %I ON public.platform_settings', pol.policyname);
                END LOOP;
                EXECUTE 'CREATE POLICY system_control_plane ON public.platform_settings FOR ALL TO opstrax_system USING(true) WITH CHECK(true)';
                EXECUTE 'GRANT SELECT,INSERT,UPDATE,DELETE ON TABLE public.platform_settings TO opstrax_system';
                EXECUTE 'REVOKE ALL ON TABLE public.platform_settings FROM opstrax_app';
            EXCEPTION WHEN insufficient_privilege THEN
                -- The API's boot-time self-heal already created the table, so it is owned by
                -- opstrax_system and a non-superuser owner cannot ALTER it. That state is
                -- SAFE: a table created by opstrax_system carries no grants for opstrax_app
                -- (the stage20 default privileges cover only owner-created tables), so the
                -- tenant runtime cannot read it — the RLS enrolment here is defense in depth,
                -- not the only wall. Record the fact and continue rather than failing the
                -- whole migration on an ordering difference.
                SELECT tableowner INTO tbl_owner FROM pg_tables
                 WHERE schemaname='public' AND tablename='platform_settings';
                RAISE NOTICE 'platform_settings already exists owned by % (API self-created); RLS enrolment skipped. '
                             'To enrol later, run as a superuser or first: ALTER TABLE public.platform_settings OWNER TO <owner>;',
                             tbl_owner;
            END;
        END IF;
    END IF;
END $$;

INSERT INTO schema_migrations (version, description)
VALUES ('2026_08_21_stage83_platform_settings',
        'platform_settings control-plane table for operator-editable configuration (SMTP, app URLs)')
ON CONFLICT (version) DO NOTHING;

COMMIT;
