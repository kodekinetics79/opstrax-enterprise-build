-- Stage 33 — Settings persistence: tenant API keys, webhooks, company profile,
--            per-user notification preferences
--
-- Replaces the cosmetic client-side "API key" on the Settings page with a real,
-- company-scoped credential: keys are SHA-256 hashed at rest (only a display
-- prefix + last-4 persist in clear), shown ONCE at creation, revocable, and
-- audit-logged. Webhook config (endpoint URL + event subscriptions + HMAC
-- signing secret for X-OpsTrax-Signature) persists per tenant. Also makes the
-- other Settings "Save Changes" buttons real: company contact profile and the
-- per-user notification channel matrix.
--
-- Endpoints: GET/POST /api/settings/api-keys, POST /api/settings/api-keys/{id}/revoke,
--            GET/PUT /api/settings/webhook, POST /api/settings/webhook/rotate-secret,
--            GET/PUT /api/settings/company-profile, GET/PUT /api/settings/notification-prefs
--
-- MUST be applied by the DB OWNER (the app runs as the restricted opstrax_app role and
-- skips startup schema init under RLS). Idempotent; safe to re-run.

BEGIN;

CREATE TABLE IF NOT EXISTS tenant_api_keys (
    id             BIGINT       GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
    company_id     BIGINT       NOT NULL,
    key_hash       VARCHAR(64)  NOT NULL UNIQUE,
    key_prefix     VARCHAR(32)  NOT NULL,
    last_four      VARCHAR(8)   NOT NULL,
    label          VARCHAR(200) NULL,
    created_by     VARCHAR(200) NULL,
    created_at     TIMESTAMPTZ  NOT NULL DEFAULT NOW(),
    last_used_at   TIMESTAMPTZ  NULL,
    revoked_at     TIMESTAMPTZ  NULL,
    revoked_by     VARCHAR(200) NULL
);

CREATE INDEX IF NOT EXISTS idx_tak_company ON tenant_api_keys (company_id);
CREATE INDEX IF NOT EXISTS idx_tak_hash    ON tenant_api_keys (key_hash);

CREATE TABLE IF NOT EXISTS tenant_webhook_settings (
    company_id      BIGINT        PRIMARY KEY,
    endpoint_url    VARCHAR(1000) NULL,
    events          JSONB         NOT NULL DEFAULT '[]'::jsonb,
    signing_secret  VARCHAR(128)  NOT NULL,
    enabled         BOOLEAN       NOT NULL DEFAULT true,
    created_at      TIMESTAMPTZ   NOT NULL DEFAULT NOW(),
    updated_at      TIMESTAMPTZ   NOT NULL DEFAULT NOW(),
    updated_by      VARCHAR(200)  NULL
);

CREATE TABLE IF NOT EXISTS company_profile (
    company_id      BIGINT        PRIMARY KEY,
    display_name    VARCHAR(200)  NULL,
    address_line1   VARCHAR(300)  NULL,
    city            VARCHAR(120)  NULL,
    state           VARCHAR(120)  NULL,
    country         VARCHAR(8)    NULL,
    phone           VARCHAR(50)   NULL,
    contact_email   VARCHAR(220)  NULL,
    website         VARCHAR(300)  NULL,
    updated_at      TIMESTAMPTZ   NOT NULL DEFAULT NOW(),
    updated_by      VARCHAR(200)  NULL
);

CREATE TABLE IF NOT EXISTS user_notification_prefs (
    user_id         BIGINT        PRIMARY KEY,
    company_id      BIGINT        NOT NULL,
    prefs           JSONB         NOT NULL DEFAULT '{}'::jsonb,
    updated_at      TIMESTAMPTZ   NOT NULL DEFAULT NOW()
);

CREATE INDEX IF NOT EXISTS idx_unp_company ON user_notification_prefs (company_id);

-- Add relationship enforcement without making an additive release fail on any
-- pre-existing orphan rows. NOT VALID still enforces every new/updated row; the
-- release gate reports legacy violations for deliberate remediation/validation.
DO $$
BEGIN
    IF NOT EXISTS (SELECT 1 FROM pg_constraint WHERE conname = 'fk_tak_company') THEN
        ALTER TABLE tenant_api_keys ADD CONSTRAINT fk_tak_company
            FOREIGN KEY (company_id) REFERENCES companies(id) NOT VALID;
    END IF;
    IF NOT EXISTS (SELECT 1 FROM pg_constraint WHERE conname = 'fk_tws_company') THEN
        ALTER TABLE tenant_webhook_settings ADD CONSTRAINT fk_tws_company
            FOREIGN KEY (company_id) REFERENCES companies(id) NOT VALID;
    END IF;
    IF NOT EXISTS (SELECT 1 FROM pg_constraint WHERE conname = 'fk_company_profile_company') THEN
        ALTER TABLE company_profile ADD CONSTRAINT fk_company_profile_company
            FOREIGN KEY (company_id) REFERENCES companies(id) NOT VALID;
    END IF;
    IF NOT EXISTS (SELECT 1 FROM pg_constraint WHERE conname = 'fk_unp_company') THEN
        ALTER TABLE user_notification_prefs ADD CONSTRAINT fk_unp_company
            FOREIGN KEY (company_id) REFERENCES companies(id) NOT VALID;
    END IF;
    IF NOT EXISTS (SELECT 1 FROM pg_constraint WHERE conname = 'fk_unp_user') THEN
        ALTER TABLE user_notification_prefs ADD CONSTRAINT fk_unp_user
            FOREIGN KEY (user_id) REFERENCES users(id) NOT VALID;
    END IF;
END $$;

-- RLS — same tenant_isolation / platform_admin_bypass pair stage19 applies to
-- every tenant-owned table (these tables post-date that migration).
ALTER TABLE tenant_api_keys         ENABLE ROW LEVEL SECURITY;
ALTER TABLE tenant_api_keys         FORCE  ROW LEVEL SECURITY;
ALTER TABLE tenant_webhook_settings ENABLE ROW LEVEL SECURITY;
ALTER TABLE tenant_webhook_settings FORCE  ROW LEVEL SECURITY;
ALTER TABLE company_profile         ENABLE ROW LEVEL SECURITY;
ALTER TABLE company_profile         FORCE  ROW LEVEL SECURITY;
ALTER TABLE user_notification_prefs ENABLE ROW LEVEL SECURITY;
ALTER TABLE user_notification_prefs FORCE  ROW LEVEL SECURITY;

DO $$
BEGIN
    IF NOT EXISTS (SELECT 1 FROM pg_policies WHERE tablename = 'tenant_api_keys' AND policyname = 'tenant_isolation') THEN
        CREATE POLICY tenant_isolation ON tenant_api_keys
            USING      (company_id = NULLIF(current_setting('app.current_tenant_id', true), '')::bigint)
            WITH CHECK (company_id = NULLIF(current_setting('app.current_tenant_id', true), '')::bigint);
    END IF;
    IF NOT EXISTS (SELECT 1 FROM pg_policies WHERE tablename = 'tenant_api_keys' AND policyname = 'platform_admin_bypass') THEN
        CREATE POLICY platform_admin_bypass ON tenant_api_keys
            USING      (NULLIF(current_setting('app.platform_admin', true), '') = 'on')
            WITH CHECK (NULLIF(current_setting('app.platform_admin', true), '') = 'on');
    END IF;
    IF NOT EXISTS (SELECT 1 FROM pg_policies WHERE tablename = 'tenant_webhook_settings' AND policyname = 'tenant_isolation') THEN
        CREATE POLICY tenant_isolation ON tenant_webhook_settings
            USING      (company_id = NULLIF(current_setting('app.current_tenant_id', true), '')::bigint)
            WITH CHECK (company_id = NULLIF(current_setting('app.current_tenant_id', true), '')::bigint);
    END IF;
    IF NOT EXISTS (SELECT 1 FROM pg_policies WHERE tablename = 'tenant_webhook_settings' AND policyname = 'platform_admin_bypass') THEN
        CREATE POLICY platform_admin_bypass ON tenant_webhook_settings
            USING      (NULLIF(current_setting('app.platform_admin', true), '') = 'on')
            WITH CHECK (NULLIF(current_setting('app.platform_admin', true), '') = 'on');
    END IF;
    IF NOT EXISTS (SELECT 1 FROM pg_policies WHERE tablename = 'company_profile' AND policyname = 'tenant_isolation') THEN
        CREATE POLICY tenant_isolation ON company_profile
            USING      (company_id = NULLIF(current_setting('app.current_tenant_id', true), '')::bigint)
            WITH CHECK (company_id = NULLIF(current_setting('app.current_tenant_id', true), '')::bigint);
    END IF;
    IF NOT EXISTS (SELECT 1 FROM pg_policies WHERE tablename = 'company_profile' AND policyname = 'platform_admin_bypass') THEN
        CREATE POLICY platform_admin_bypass ON company_profile
            USING      (NULLIF(current_setting('app.platform_admin', true), '') = 'on')
            WITH CHECK (NULLIF(current_setting('app.platform_admin', true), '') = 'on');
    END IF;
    IF NOT EXISTS (SELECT 1 FROM pg_policies WHERE tablename = 'user_notification_prefs' AND policyname = 'tenant_isolation') THEN
        CREATE POLICY tenant_isolation ON user_notification_prefs
            USING      (company_id = NULLIF(current_setting('app.current_tenant_id', true), '')::bigint)
            WITH CHECK (company_id = NULLIF(current_setting('app.current_tenant_id', true), '')::bigint);
    END IF;
    IF NOT EXISTS (SELECT 1 FROM pg_policies WHERE tablename = 'user_notification_prefs' AND policyname = 'platform_admin_bypass') THEN
        CREATE POLICY platform_admin_bypass ON user_notification_prefs
            USING      (NULLIF(current_setting('app.platform_admin', true), '') = 'on')
            WITH CHECK (NULLIF(current_setting('app.platform_admin', true), '') = 'on');
    END IF;
END $$;

-- opstrax_app is only provisioned once stage20 is activated on a given
-- environment (see stage19's dormancy note); skip the grants rather than
-- fail the whole migration where that role doesn't exist yet.
DO $$
BEGIN
    IF EXISTS (SELECT 1 FROM pg_roles WHERE rolname = 'opstrax_app') THEN
        GRANT SELECT, INSERT, UPDATE ON tenant_api_keys         TO opstrax_app;
        GRANT SELECT, INSERT, UPDATE ON tenant_webhook_settings TO opstrax_app;
        GRANT SELECT, INSERT, UPDATE ON company_profile         TO opstrax_app;
        GRANT SELECT, INSERT, UPDATE ON user_notification_prefs TO opstrax_app;
        GRANT USAGE, SELECT ON SEQUENCE tenant_api_keys_id_seq TO opstrax_app;
    END IF;
END $$;

COMMIT;
