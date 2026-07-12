-- Stage 34 — Enterprise SSO connections (identifier-first login discovery)
--
-- Admin-provisioned, per-tenant SSO/IdP connections keyed by a verified email
-- domain. Powers POST /api/auth/sso/discover: identifier-first login checks
-- whether the tenant that owns an email's domain has an ENABLED SSO connection
-- and, if so, routes the browser to the IdP instead of the password field.
--
-- Starts EMPTY on every environment: no seed rows, no placeholder IdPs. Until a
-- platform/tenant admin provisions a row here, every discover call resolves to
-- "no SSO -> use password", which is the intended password-first behavior. This
-- keeps the SSO button honest (it only ever renders for a real, enabled, https
-- connection) per the CLAUDE.md no-fake-data / no-dead-buttons rule.
--
-- MUST be applied by the DB OWNER (the app runs as the restricted opstrax_app
-- role under FORCE RLS). Idempotent; safe to re-run.

BEGIN;

CREATE TABLE IF NOT EXISTS sso_connections (
    id              BIGINT        GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
    company_id      BIGINT        NOT NULL,
    -- Verified email domain that maps a user to this tenant's IdP: lower-cased,
    -- bare host only (e.g. 'acmelogistics.com'). One ENABLED connection per
    -- domain across the whole platform (a domain cannot route to two tenants).
    email_domain    VARCHAR(253)  NOT NULL,
    display_name    VARCHAR(200)  NOT NULL,          -- shown on the SSO button ("Acme SSO")
    protocol        VARCHAR(10)   NOT NULL,          -- 'saml' | 'oidc'
    -- Absolute https IdP entry URL the browser is redirected to (SAML SSO URL or
    -- OIDC authorization endpoint). Validated at write time AND read time.
    idp_entry_url   VARCHAR(2048) NOT NULL,
    status          VARCHAR(20)   NOT NULL DEFAULT 'disabled',  -- 'enabled' | 'disabled'
    created_by      VARCHAR(200)  NULL,
    created_at      TIMESTAMPTZ   NOT NULL DEFAULT NOW(),
    updated_at      TIMESTAMPTZ   NOT NULL DEFAULT NOW(),
    updated_by      VARCHAR(200)  NULL,
    CONSTRAINT ck_sso_protocol  CHECK (protocol IN ('saml','oidc')),
    CONSTRAINT ck_sso_status    CHECK (status   IN ('enabled','disabled')),
    -- Defense-in-depth: the app also validates, but reject non-https at the DB.
    CONSTRAINT ck_sso_idp_https CHECK (idp_entry_url ILIKE 'https://%')
);

-- A domain may exist disabled while being set up, but only ONE row may be ENABLED
-- per domain platform-wide — a partial unique index enforces deterministic routing.
CREATE UNIQUE INDEX IF NOT EXISTS ux_sso_domain_enabled
    ON sso_connections (LOWER(email_domain))
    WHERE status = 'enabled';

-- Discovery lookup path: WHERE LOWER(email_domain)=@d AND status='enabled'.
CREATE INDEX IF NOT EXISTS idx_sso_domain_lookup
    ON sso_connections (LOWER(email_domain)) WHERE status = 'enabled';
CREATE INDEX IF NOT EXISTS idx_sso_company ON sso_connections (company_id);

-- Relationship enforcement, additive-safe (NOT VALID) like stage33.
DO $$
BEGIN
    IF NOT EXISTS (SELECT 1 FROM pg_constraint WHERE conname = 'fk_sso_company') THEN
        ALTER TABLE sso_connections ADD CONSTRAINT fk_sso_company
            FOREIGN KEY (company_id) REFERENCES companies(id) NOT VALID;
    END IF;
END $$;

-- RLS — same tenant_isolation / platform_admin_bypass pair every tenant-owned
-- table carries since stage19. Admin CRUD is tenant-scoped; the pre-login
-- discover handler reads it under the platform_admin bypass scope (Program.cs
-- pre-tenant branch sets app.platform_admin='on'), exactly like the existing
-- public /api/auth/* handlers already do.
ALTER TABLE sso_connections ENABLE ROW LEVEL SECURITY;
ALTER TABLE sso_connections FORCE  ROW LEVEL SECURITY;

DO $$
BEGIN
    IF NOT EXISTS (SELECT 1 FROM pg_policies WHERE tablename = 'sso_connections' AND policyname = 'tenant_isolation') THEN
        CREATE POLICY tenant_isolation ON sso_connections
            USING      (company_id = NULLIF(current_setting('app.current_tenant_id', true), '')::bigint)
            WITH CHECK (company_id = NULLIF(current_setting('app.current_tenant_id', true), '')::bigint);
    END IF;
    IF NOT EXISTS (SELECT 1 FROM pg_policies WHERE tablename = 'sso_connections' AND policyname = 'platform_admin_bypass') THEN
        CREATE POLICY platform_admin_bypass ON sso_connections
            USING      (NULLIF(current_setting('app.platform_admin', true), '') = 'on')
            WITH CHECK (NULLIF(current_setting('app.platform_admin', true), '') = 'on');
    END IF;
END $$;

DO $$
BEGIN
    IF EXISTS (SELECT 1 FROM pg_roles WHERE rolname = 'opstrax_app') THEN
        GRANT SELECT, INSERT, UPDATE ON sso_connections TO opstrax_app;
        GRANT USAGE, SELECT ON SEQUENCE sso_connections_id_seq TO opstrax_app;
    END IF;
END $$;

COMMIT;
