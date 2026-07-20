-- Stage 26 — Platform Admin control plane schema (migration honesty pass)
--
-- PURPOSE
--   The platform control-plane tables have only ever been created at runtime by
--   PlatformSchemaService.EnsureAsync(). Under the production posture (restricted
--   opstrax_app role + RLS enforced) schema init is SKIPPED at boot, so any deployed
--   environment relies on the tables having been created out-of-band. This migration
--   makes that DDL an explicit, reviewable artifact so a fresh environment can be
--   built from migrations alone.
--
--   DDL mirrors PlatformSchemaService exactly (CREATE TABLE IF NOT EXISTS — additive,
--   idempotent, safe to run against a DB where the runtime already created them).
--
-- SECURITY
--   * Seeds platform ROLES and PERMISSIONS only. It NEVER seeds an admin identity or
--     password — the bootstrap super admin comes from PLATFORM_SUPERADMIN_EMAIL /
--     PLATFORM_SUPERADMIN_PASSWORD env at app start (see PlatformSchemaService), or is
--     created manually by the operator.
--   * platform_* tables are control-plane tables: intentionally NOT tenant-RLS-enrolled
--     (they carry no tenant company_id, or — like platform_invoices — are platform
--     billing artifacts). This matches the Stage 19/22 exclusion list.
--   * tenant_subscriptions / tenant_entitlements DO carry company_id and are enrolled
--     by the Stage 22 reconciliation pass (tenant_isolation + platform_admin_bypass).
--     Re-run Stage 22 after applying this migration on any RLS-enforced environment.
--
-- ROLLBACK (manual): DROP TABLE ... for any table created here that holds no data.
--   Never drop platform_audit_log in an environment that has audit history.

BEGIN;

CREATE TABLE IF NOT EXISTS platform_roles (
    id           BIGINT       GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
    role_key     VARCHAR(60)  NOT NULL UNIQUE,
    name         VARCHAR(120) NOT NULL,
    description  VARCHAR(300) NULL,
    created_at   TIMESTAMPTZ  NOT NULL DEFAULT NOW()
);

CREATE TABLE IF NOT EXISTS platform_role_permissions (
    role_id        BIGINT      NOT NULL REFERENCES platform_roles(id) ON DELETE CASCADE,
    permission_key VARCHAR(80) NOT NULL,
    PRIMARY KEY (role_id, permission_key)
);

CREATE TABLE IF NOT EXISTS platform_admins (
    id             BIGINT       GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
    email          VARCHAR(220) NOT NULL UNIQUE,
    full_name      VARCHAR(160) NOT NULL,
    password_hash  VARCHAR(255) NULL,
    role_id        BIGINT       NULL REFERENCES platform_roles(id),
    status         VARCHAR(40)  NOT NULL DEFAULT 'Active',
    mfa_enabled    BOOLEAN      NOT NULL DEFAULT false,
    last_login_at  TIMESTAMPTZ  NULL,
    created_at     TIMESTAMPTZ  NOT NULL DEFAULT NOW()
);

CREATE TABLE IF NOT EXISTS platform_sessions (
    id            BIGINT       GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
    admin_id      BIGINT       NOT NULL REFERENCES platform_admins(id) ON DELETE CASCADE,
    session_token VARCHAR(255) NOT NULL UNIQUE,
    expires_at    TIMESTAMPTZ  NOT NULL,
    created_at    TIMESTAMPTZ  NOT NULL DEFAULT NOW()
);

CREATE TABLE IF NOT EXISTS platform_audit_log (
    id                BIGINT       GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
    actor_admin_id    BIGINT       NULL,
    actor_email       VARCHAR(220) NULL,
    actor_role        VARCHAR(80)  NULL,
    action            VARCHAR(120) NOT NULL,
    entity_type       VARCHAR(80)  NOT NULL,
    entity_id         BIGINT       NULL,
    target_company_id BIGINT       NULL,
    details_json      JSONB        NULL,
    ip_address        VARCHAR(80)  NULL,
    created_at        TIMESTAMPTZ  NOT NULL DEFAULT NOW()
);
CREATE INDEX IF NOT EXISTS idx_platform_audit_created ON platform_audit_log (created_at DESC);

CREATE TABLE IF NOT EXISTS packages (
    id               BIGINT       GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
    package_code     VARCHAR(60)  NOT NULL UNIQUE,
    name             VARCHAR(160) NOT NULL,
    description      VARCHAR(400) NULL,
    billing_interval VARCHAR(20)  NOT NULL DEFAULT 'monthly',
    currency         VARCHAR(8)   NOT NULL DEFAULT 'USD',
    base_price_cents BIGINT       NOT NULL DEFAULT 0,
    seat_price_cents BIGINT       NOT NULL DEFAULT 0,
    included_seats   INT          NOT NULL DEFAULT 0,
    setup_fee_cents  BIGINT       NOT NULL DEFAULT 0,
    annual_price_cents BIGINT     NOT NULL DEFAULT 0,
    module_keys      JSONB        NULL,
    is_custom        BOOLEAN      NOT NULL DEFAULT false,
    active           BOOLEAN      NOT NULL DEFAULT true,
    created_at       TIMESTAMPTZ  NOT NULL DEFAULT NOW()
);

CREATE TABLE IF NOT EXISTS tenant_subscriptions (
    id               BIGINT       GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
    company_id       BIGINT       NOT NULL UNIQUE REFERENCES companies(id),
    package_id       BIGINT       NULL REFERENCES packages(id),
    status           VARCHAR(30)  NOT NULL DEFAULT 'trial',
    seat_limit       INT          NOT NULL DEFAULT 5,
    billing_currency VARCHAR(8)   NOT NULL DEFAULT 'USD',
    mrr_cents        BIGINT       NOT NULL DEFAULT 0,
    trial_ends_at    TIMESTAMPTZ  NULL,
    contract_start   DATE         NULL,
    contract_end     DATE         NULL,
    account_owner    VARCHAR(160) NULL,
    support_owner    VARCHAR(160) NULL,
    created_at       TIMESTAMPTZ  NOT NULL DEFAULT NOW(),
    updated_at       TIMESTAMPTZ  NOT NULL DEFAULT NOW()
);
CREATE INDEX IF NOT EXISTS idx_tenant_sub_status ON tenant_subscriptions (status);

CREATE TABLE IF NOT EXISTS tenant_entitlements (
    id           BIGINT       GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
    company_id   BIGINT       NOT NULL REFERENCES companies(id),
    module_key   VARCHAR(80)  NOT NULL,
    enabled      BOOLEAN      NOT NULL DEFAULT true,
    limit_value  INT          NULL,
    tier         VARCHAR(20)  NOT NULL DEFAULT 'standard',
    source       VARCHAR(20)  NOT NULL DEFAULT 'package',
    updated_by   VARCHAR(220) NULL,
    updated_at   TIMESTAMPTZ  NOT NULL DEFAULT NOW(),
    UNIQUE (company_id, module_key)
);
CREATE INDEX IF NOT EXISTS idx_entitlement_company ON tenant_entitlements (company_id);

CREATE TABLE IF NOT EXISTS platform_invoices (
    id             BIGINT       GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
    company_id     BIGINT       NOT NULL REFERENCES companies(id),
    invoice_number VARCHAR(60)  NOT NULL UNIQUE,
    status         VARCHAR(20)  NOT NULL DEFAULT 'draft',
    kind           VARCHAR(20)  NOT NULL DEFAULT 'recurring',
    amount_cents   BIGINT       NOT NULL DEFAULT 0,
    currency       VARCHAR(8)   NOT NULL DEFAULT 'USD',
    line_items     JSONB        NULL,
    notes          VARCHAR(400) NULL,
    issued_at      TIMESTAMPTZ  NULL,
    due_at         TIMESTAMPTZ  NULL,
    paid_at        TIMESTAMPTZ  NULL,
    created_at     TIMESTAMPTZ  NOT NULL DEFAULT NOW()
);
CREATE INDEX IF NOT EXISTS idx_invoice_company ON platform_invoices (company_id);

-- Reserved for a future safe support-access model. No endpoints use it yet;
-- see docs/platform/platform_admin_control_plane_standard.md.
CREATE TABLE IF NOT EXISTS platform_impersonation_sessions (
    id             BIGINT       GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
    admin_id       BIGINT       NOT NULL REFERENCES platform_admins(id),
    company_id     BIGINT       NOT NULL REFERENCES companies(id),
    reason         VARCHAR(400) NOT NULL,
    expires_at     TIMESTAMPTZ  NOT NULL,
    ended_at       TIMESTAMPTZ  NULL,
    created_at     TIMESTAMPTZ  NOT NULL DEFAULT NOW()
);

-- Restricted runtime role needs DML on these (RLS-enforced environments).
GRANT SELECT, INSERT, UPDATE, DELETE ON ALL TABLES IN SCHEMA public TO opstrax_app;
GRANT USAGE, SELECT ON ALL SEQUENCES IN SCHEMA public TO opstrax_app;

-- Ledger: register this migration and backfill stage 24/25 which shipped without
-- self-registration (files exist in the repo; rows were missing from the ledger).
INSERT INTO schema_migrations (version, description) VALUES
    ('2026_07_02_stage24_compliance_tenant_scope',   'Compliance tenant scope'),
    ('2026_07_02_stage25_branches_org_hierarchy',    'Branches / org hierarchy'),
    ('2026_07_02_stage26_platform_control_plane',    'Platform Admin control plane schema (this)')
ON CONFLICT (version) DO NOTHING;

COMMIT;
