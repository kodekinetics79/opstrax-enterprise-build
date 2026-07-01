-- Stage 20 — RLS activation: restricted application role + FORCE ROW LEVEL SECURITY
--
-- PURPOSE
--   Turns the (previously dormant) Stage-19 RLS policies into ACTIVE enforcement.
--   Two prerequisites are established here:
--     1. A dedicated NON-superuser, NON-BYPASSRLS application role (`opstrax_app`)
--        that the app connects as in production. Superusers and BYPASSRLS roles
--        always bypass RLS; a table's owner bypasses it too unless FORCE is set.
--        `opstrax_app` is none of those, so the Stage-19 tenant_isolation /
--        platform_admin_bypass policies actually apply to it.
--     2. `FORCE ROW LEVEL SECURITY` on every RLS-enabled tenant table, so even a
--        table-owner connection is subject to the policies (belt-and-suspenders).
--
-- SAFETY / REVERSIBILITY
--   * ADDITIVE and idempotent. Re-runnable. Touches no data.
--   * Does NOT change which role the app/tests connect as — that is an env/deploy
--     decision (production PG_CONNECTION should use `opstrax_app`). The local/test
--     superuser (`zayra`) continues to bypass RLS, so existing tests are unaffected.
--   * Password for `opstrax_app` is intentionally NOT set here (no secret in git).
--     Set it out-of-band per environment:  ALTER ROLE opstrax_app WITH PASSWORD '<secret>';
--   * ROLLBACK (manual):
--       DO $$ DECLARE t text; BEGIN
--         FOR t IN SELECT tablename FROM pg_tables WHERE schemaname='public' AND rowsecurity LOOP
--           EXECUTE format('ALTER TABLE public.%I NO FORCE ROW LEVEL SECURITY', t);
--         END LOOP; END $$;
--       REVOKE ALL ON ALL TABLES IN SCHEMA public FROM opstrax_app;  -- then DROP ROLE opstrax_app;

BEGIN;

-- 1. Restricted application role (idempotent; no password — set per environment).
DO $role$
BEGIN
    IF NOT EXISTS (SELECT 1 FROM pg_roles WHERE rolname = 'opstrax_app') THEN
        CREATE ROLE opstrax_app LOGIN NOSUPERUSER NOBYPASSRLS NOCREATEDB NOCREATEROLE NOINHERIT;
    ELSE
        ALTER ROLE opstrax_app NOSUPERUSER NOBYPASSRLS NOCREATEDB NOCREATEROLE;
    END IF;
END
$role$;

-- 2. Least-privilege grants: DML only, no DDL/ownership (so it never bypasses RLS).
GRANT USAGE ON SCHEMA public TO opstrax_app;
GRANT SELECT, INSERT, UPDATE, DELETE ON ALL TABLES IN SCHEMA public TO opstrax_app;
GRANT USAGE, SELECT ON ALL SEQUENCES IN SCHEMA public TO opstrax_app;
-- Future tables/sequences created by the owner inherit the same grants.
ALTER DEFAULT PRIVILEGES IN SCHEMA public GRANT SELECT, INSERT, UPDATE, DELETE ON TABLES TO opstrax_app;
ALTER DEFAULT PRIVILEGES IN SCHEMA public GRANT USAGE, SELECT ON SEQUENCES TO opstrax_app;

-- 3. FORCE RLS on every RLS-enabled tenant table (Stage-19 set rowsecurity=true).
--    Non-owner roles (opstrax_app) are already subject to RLS; FORCE additionally
--    subjects the owner, closing the owner-bypass gap.
DO $force$
DECLARE
    t text;
BEGIN
    FOR t IN
        SELECT tablename FROM pg_tables
        WHERE schemaname = 'public' AND rowsecurity = true
    LOOP
        EXECUTE format('ALTER TABLE public.%I FORCE ROW LEVEL SECURITY', t);
    END LOOP;
END
$force$;

COMMIT;
