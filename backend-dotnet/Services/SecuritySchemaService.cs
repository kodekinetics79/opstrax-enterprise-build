using Opstrax.Api.Data;

namespace Opstrax.Api.Services;

// ─────────────────────────────────────────────────────────────────────────────
// P10 Enterprise Security + Compliance Schema
// Tables:
//   company_security_settings  — tenant security policy config
//   user_mfa_status            — per-user MFA enrollment state
//   security_events            — sanitized security event log
//   sso_connections            — SSO/OIDC/SAML readiness config (no secrets)
//   access_reviews             — periodic access review campaigns
//   access_review_items        — per-user items within a review
//   compliance_controls        — SOC2-readiness and internal security controls
//   compliance_evidence        — evidence records linked to real system data
//   backup_verifications       — backup/restore verification records
//   data_retention_policies    — tenant-level retention config
//   export_requests            — export approval workflow log
//
// Additionally adds optional columns to existing `users` table:
//   failed_login_attempts, locked_until, force_password_change, password_changed_at
// ─────────────────────────────────────────────────────────────────────────────

public sealed class SecuritySchemaService(Database db)
{
    public async Task EnsureAsync()
    {
        await db.ExecuteAsync("""
            CREATE TABLE IF NOT EXISTS password_reset_tokens (
                user_id BIGINT PRIMARY KEY REFERENCES users(id) ON DELETE CASCADE,
                company_id BIGINT NOT NULL REFERENCES companies(id) ON DELETE CASCADE,
                token_hash VARCHAR(64) NOT NULL UNIQUE,
                expires_at TIMESTAMPTZ NOT NULL,
                consumed_at TIMESTAMPTZ NULL,
                request_ip_hash VARCHAR(16) NULL,
                created_at TIMESTAMPTZ NOT NULL DEFAULT NOW()
            )
            """);
        await db.ExecuteAsync("CREATE INDEX IF NOT EXISTS idx_password_reset_expiry ON password_reset_tokens (expires_at) WHERE consumed_at IS NULL");
        await db.ExecuteAsync("ALTER TABLE password_reset_tokens ENABLE ROW LEVEL SECURITY");
        await db.ExecuteAsync("ALTER TABLE password_reset_tokens FORCE ROW LEVEL SECURITY");
        await db.ExecuteAsync("DROP POLICY IF EXISTS tenant_isolation_password_reset_tokens ON password_reset_tokens");
        await db.ExecuteAsync(@"CREATE POLICY tenant_isolation_password_reset_tokens ON password_reset_tokens
            USING (current_setting('app.platform_admin_bypass', true) = 'true'
                OR company_id = NULLIF(current_setting('app.current_tenant_id', true), '')::bigint)
            WITH CHECK (current_setting('app.platform_admin_bypass', true) = 'true'
                OR company_id = NULLIF(current_setting('app.current_tenant_id', true), '')::bigint)");
        // Built-in roles remain immutable system templates. Tenant-created roles are
        // owned by one company so an administrator can never mutate another tenant's
        // authorization policy.
        await db.ExecuteAsync("ALTER TABLE roles ADD COLUMN IF NOT EXISTS company_id BIGINT NULL");
        await db.ExecuteAsync("ALTER TABLE roles ADD COLUMN IF NOT EXISTS is_system BOOLEAN NOT NULL DEFAULT TRUE");
        await db.ExecuteAsync("ALTER TABLE roles DROP CONSTRAINT IF EXISTS roles_name_key");
        await db.ExecuteAsync("CREATE UNIQUE INDEX IF NOT EXISTS ux_roles_system_name ON roles (LOWER(name)) WHERE company_id IS NULL");
        await db.ExecuteAsync("CREATE UNIQUE INDEX IF NOT EXISTS ux_roles_tenant_name ON roles (company_id, LOWER(name)) WHERE company_id IS NOT NULL");
        await db.ExecuteAsync("ALTER TABLE roles ENABLE ROW LEVEL SECURITY");
        await db.ExecuteAsync("ALTER TABLE roles FORCE ROW LEVEL SECURITY");
        await db.ExecuteAsync("DROP POLICY IF EXISTS roles_tenant_read ON roles");
        await db.ExecuteAsync("""
            CREATE POLICY roles_tenant_read ON roles FOR SELECT
            USING (company_id IS NULL OR company_id = NULLIF(current_setting('app.current_tenant_id', true), '')::bigint)
            """);
        await db.ExecuteAsync("DROP POLICY IF EXISTS roles_tenant_insert ON roles");
        await db.ExecuteAsync("""
            CREATE POLICY roles_tenant_insert ON roles FOR INSERT
            WITH CHECK (company_id IS NOT NULL AND company_id = NULLIF(current_setting('app.current_tenant_id', true), '')::bigint)
            """);
        await db.ExecuteAsync("DROP POLICY IF EXISTS roles_tenant_update ON roles");
        await db.ExecuteAsync("""
            CREATE POLICY roles_tenant_update ON roles FOR UPDATE
            USING (company_id IS NOT NULL AND company_id = NULLIF(current_setting('app.current_tenant_id', true), '')::bigint)
            WITH CHECK (company_id IS NOT NULL AND company_id = NULLIF(current_setting('app.current_tenant_id', true), '')::bigint)
            """);
        await db.ExecuteAsync("DROP POLICY IF EXISTS roles_tenant_delete ON roles");
        await db.ExecuteAsync("""
            CREATE POLICY roles_tenant_delete ON roles FOR DELETE
            USING (company_id IS NOT NULL AND company_id = NULLIF(current_setting('app.current_tenant_id', true), '')::bigint)
            """);
        await db.ExecuteAsync("DROP POLICY IF EXISTS roles_platform_admin_bypass ON roles");
        await db.ExecuteAsync("""
            CREATE POLICY roles_platform_admin_bypass ON roles FOR ALL
            USING (NULLIF(current_setting('app.platform_admin', true), '') = 'on')
            WITH CHECK (NULLIF(current_setting('app.platform_admin', true), '') = 'on')
            """);

        await db.ExecuteAsync("""
            CREATE TABLE IF NOT EXISTS company_security_settings (
                id                              BIGINT       GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
                company_id                      BIGINT       NOT NULL UNIQUE,
                mfa_required                    BOOLEAN      NOT NULL DEFAULT false,
                mfa_required_roles              JSONB        NULL,
                password_min_length             INT          NOT NULL DEFAULT 8,
                password_requires_uppercase     BOOLEAN      NOT NULL DEFAULT false,
                password_requires_number        BOOLEAN      NOT NULL DEFAULT false,
                password_requires_symbol        BOOLEAN      NOT NULL DEFAULT false,
                password_expiry_days            INT          NOT NULL DEFAULT 0,
                session_idle_timeout_minutes    INT          NOT NULL DEFAULT 60,
                session_absolute_timeout_minutes INT         NOT NULL DEFAULT 480,
                max_failed_login_attempts       INT          NOT NULL DEFAULT 5,
                lockout_duration_minutes        INT          NOT NULL DEFAULT 30,
                allowed_sso_providers           JSONB        NULL,
                export_approval_required        BOOLEAN      NOT NULL DEFAULT false,
                audit_retention_days            INT          NOT NULL DEFAULT 90,
                data_retention_days             INT          NOT NULL DEFAULT 365,
                created_at                      TIMESTAMPTZ  NOT NULL DEFAULT NOW(),
                updated_at                      TIMESTAMPTZ  NOT NULL DEFAULT NOW(),
                updated_by                      VARCHAR(200) NULL
            )
            """);

        await db.ExecuteAsync("""
            CREATE INDEX IF NOT EXISTS idx_css_company ON company_security_settings (company_id)
            """);

        await db.ExecuteAsync("""
            CREATE TABLE IF NOT EXISTS user_mfa_status (
                user_id                         BIGINT       PRIMARY KEY,
                mfa_enabled                     BOOLEAN      NOT NULL DEFAULT false,
                mfa_provider                    VARCHAR(50)  NULL,
                enrolled_at                     TIMESTAMPTZ  NULL,
                last_used_at                    TIMESTAMPTZ  NULL,
                recovery_codes_generated_at     TIMESTAMPTZ  NULL,
                recovery_codes_remaining        INT          NOT NULL DEFAULT 0,
                updated_at                      TIMESTAMPTZ  NOT NULL DEFAULT NOW()
            )
            """);

        // Encrypted TOTP secret for tenant-user MFA enrollment (mirrors platform_admins.mfa_secret).
        // Without this, "require MFA" was a login lockout with no enrollment path (audit P0).
        await db.ExecuteAsync("ALTER TABLE user_mfa_status ADD COLUMN IF NOT EXISTS mfa_secret TEXT NULL");

        await db.ExecuteAsync("""
            CREATE TABLE IF NOT EXISTS security_events (
                id                  BIGINT       GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
                company_id          BIGINT       NOT NULL,
                user_id             BIGINT       NULL,
                event_type          VARCHAR(100) NOT NULL,
                severity            VARCHAR(20)
                                    NOT NULL DEFAULT 'info'
                                    CHECK (severity IN ('critical','high','medium','low','info')),
                source_ip_truncated VARCHAR(30)  NULL,
                user_agent_hash     VARCHAR(16)  NULL,
                success             BOOLEAN      NOT NULL DEFAULT true,
                safe_message        VARCHAR(500) NOT NULL,
                metadata_json       JSONB        NULL,
                created_at          TIMESTAMPTZ  NOT NULL DEFAULT NOW()
            )
            """);

        await db.ExecuteAsync("""
            CREATE INDEX IF NOT EXISTS idx_se_company ON security_events (company_id)
            """);
        await db.ExecuteAsync("""
            CREATE INDEX IF NOT EXISTS idx_se_type ON security_events (event_type)
            """);
        await db.ExecuteAsync("""
            CREATE INDEX IF NOT EXISTS idx_se_created ON security_events (created_at)
            """);
        await db.ExecuteAsync("""
            CREATE INDEX IF NOT EXISTS idx_se_user ON security_events (user_id)
            """);

        await db.ExecuteAsync("""
            CREATE TABLE IF NOT EXISTS sso_connections (
                id                    BIGINT       GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
                company_id            BIGINT       NOT NULL,
                provider_type         VARCHAR(10)
                                      NOT NULL
                                      CHECK (provider_type IN ('oidc','saml')),
                display_name          VARCHAR(200) NOT NULL,
                issuer_or_entity_id   VARCHAR(500) NOT NULL,
                client_id             VARCHAR(500) NOT NULL,
                client_secret_ref     VARCHAR(200) NULL,
                certificate_thumbprint VARCHAR(200) NULL,
                enabled               BOOLEAN      NOT NULL DEFAULT true,
                domain_hints          JSONB        NULL,
                metadata_url          VARCHAR(500) NULL,
                created_at            TIMESTAMPTZ  NOT NULL DEFAULT NOW(),
                updated_at            TIMESTAMPTZ  NOT NULL DEFAULT NOW(),
                created_by            VARCHAR(200) NULL
            )
            """);

        await db.ExecuteAsync("""
            CREATE INDEX IF NOT EXISTS idx_sso_company ON sso_connections (company_id)
            """);
        await db.ExecuteAsync("""
            CREATE INDEX IF NOT EXISTS idx_sso_enabled ON sso_connections (enabled)
            """);

        await db.ExecuteAsync("""
            CREATE TABLE IF NOT EXISTS access_reviews (
                id                  BIGINT       GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
                company_id          BIGINT       NOT NULL,
                title               VARCHAR(500) NOT NULL,
                description         TEXT         NULL,
                reviewer_user_id    BIGINT       NOT NULL,
                status              VARCHAR(20)
                                    NOT NULL DEFAULT 'pending'
                                    CHECK (status IN ('pending','in_progress','completed','cancelled')),
                due_date            DATE         NULL,
                started_at          TIMESTAMPTZ  NULL,
                completed_at        TIMESTAMPTZ  NULL,
                total_items         INT          NOT NULL DEFAULT 0,
                items_approved      INT          NOT NULL DEFAULT 0,
                items_revoked       INT          NOT NULL DEFAULT 0,
                items_pending       INT          NOT NULL DEFAULT 0,
                created_at          TIMESTAMPTZ  NOT NULL DEFAULT NOW(),
                created_by          VARCHAR(200) NULL
            )
            """);

        await db.ExecuteAsync("""
            CREATE INDEX IF NOT EXISTS idx_ar_company ON access_reviews (company_id)
            """);
        await db.ExecuteAsync("""
            CREATE INDEX IF NOT EXISTS idx_ar_status ON access_reviews (status)
            """);
        await db.ExecuteAsync("""
            CREATE INDEX IF NOT EXISTS idx_ar_reviewer ON access_reviews (reviewer_user_id)
            """);

        await db.ExecuteAsync("""
            CREATE TABLE IF NOT EXISTS access_review_items (
                id                      BIGINT       GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
                review_id               BIGINT       NOT NULL,
                company_id              BIGINT       NOT NULL,
                target_user_id          BIGINT       NOT NULL,
                target_user_name        VARCHAR(200) NULL,
                target_user_email       VARCHAR(500) NULL,
                role_name               VARCHAR(200) NULL,
                permissions_snapshot    JSONB        NULL,
                status                  VARCHAR(20)
                                        NOT NULL DEFAULT 'pending'
                                        CHECK (status IN ('pending','approved','revoked','remediated')),
                completed_at            TIMESTAMPTZ  NULL,
                completed_by            VARCHAR(200) NULL,
                notes                   TEXT         NULL,
                created_at              TIMESTAMPTZ  NOT NULL DEFAULT NOW()
            )
            """);

        await db.ExecuteAsync("""
            CREATE INDEX IF NOT EXISTS idx_ari_review ON access_review_items (review_id)
            """);
        await db.ExecuteAsync("""
            CREATE INDEX IF NOT EXISTS idx_ari_company ON access_review_items (company_id)
            """);
        await db.ExecuteAsync("""
            CREATE INDEX IF NOT EXISTS idx_ari_status ON access_review_items (status)
            """);
        await db.ExecuteAsync("""
            CREATE INDEX IF NOT EXISTS idx_ari_user ON access_review_items (target_user_id)
            """);

        await db.ExecuteAsync("""
            CREATE TABLE IF NOT EXISTS compliance_controls (
                id              BIGINT       GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
                control_id      VARCHAR(50)  NOT NULL UNIQUE,
                framework       VARCHAR(100) NOT NULL DEFAULT 'SOC2_readiness',
                title           VARCHAR(500) NOT NULL,
                description     TEXT         NULL,
                owner           VARCHAR(200) NULL,
                status          VARCHAR(20)
                                NOT NULL DEFAULT 'not_implemented'
                                CHECK (status IN ('implemented','partial','not_implemented','not_applicable')),
                category        VARCHAR(200) NULL,
                created_at      TIMESTAMPTZ  NOT NULL DEFAULT NOW(),
                updated_at      TIMESTAMPTZ  NOT NULL DEFAULT NOW()
            )
            """);

        await db.ExecuteAsync("""
            CREATE INDEX IF NOT EXISTS idx_cc_framework ON compliance_controls (framework)
            """);
        await db.ExecuteAsync("""
            CREATE INDEX IF NOT EXISTS idx_cc_status ON compliance_controls (status)
            """);

        await db.ExecuteAsync("""
            CREATE TABLE IF NOT EXISTS compliance_evidence (
                id               BIGINT       GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
                control_id       VARCHAR(50)  NOT NULL,
                evidence_type    VARCHAR(100) NOT NULL,
                source_system    VARCHAR(100) NOT NULL,
                source_entity    VARCHAR(200) NULL,
                source_record_id BIGINT       NULL,
                title            VARCHAR(500) NOT NULL,
                safe_summary     TEXT         NULL,
                generated_at     TIMESTAMPTZ  NOT NULL DEFAULT NOW(),
                evidence_hash    VARCHAR(64)  NULL,
                retention_until  DATE         NULL,
                generated_by     VARCHAR(200) NULL
            )
            """);

        await db.ExecuteAsync("""
            CREATE INDEX IF NOT EXISTS idx_ce_control ON compliance_evidence (control_id)
            """);
        await db.ExecuteAsync("""
            CREATE INDEX IF NOT EXISTS idx_ce_type ON compliance_evidence (evidence_type)
            """);
        await db.ExecuteAsync("""
            CREATE INDEX IF NOT EXISTS idx_ce_generated ON compliance_evidence (generated_at)
            """);

        await db.ExecuteAsync("""
            CREATE TABLE IF NOT EXISTS backup_verifications (
                id                      BIGINT       GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
                company_id              BIGINT       NULL,
                backup_type             VARCHAR(100) NOT NULL,
                status                  VARCHAR(20)
                                        NOT NULL DEFAULT 'not_configured'
                                        CHECK (status IN ('passed','failed','warning','not_configured')),
                verified_at             TIMESTAMPTZ  NOT NULL DEFAULT NOW(),
                restore_tested          BOOLEAN      NOT NULL DEFAULT false,
                duration_ms             INT          NULL,
                storage_location_label  VARCHAR(200) NULL,
                safe_error              TEXT         NULL,
                evidence_hash           VARCHAR(64)  NULL
            )
            """);

        await db.ExecuteAsync("""
            CREATE INDEX IF NOT EXISTS idx_bv_company ON backup_verifications (company_id)
            """);
        await db.ExecuteAsync("""
            CREATE INDEX IF NOT EXISTS idx_bv_type ON backup_verifications (backup_type)
            """);
        await db.ExecuteAsync("""
            CREATE INDEX IF NOT EXISTS idx_bv_status ON backup_verifications (status)
            """);
        await db.ExecuteAsync("""
            CREATE INDEX IF NOT EXISTS idx_bv_verified ON backup_verifications (verified_at)
            """);

        await db.ExecuteAsync("""
            CREATE TABLE IF NOT EXISTS data_retention_policies (
                id                      BIGINT       GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
                company_id              BIGINT       NOT NULL UNIQUE,
                audit_log_days          INT          NOT NULL DEFAULT 90,
                telemetry_days          INT          NOT NULL DEFAULT 90,
                notification_days       INT          NOT NULL DEFAULT 30,
                report_execution_days   INT          NOT NULL DEFAULT 180,
                security_event_days     INT          NOT NULL DEFAULT 365,
                soft_delete_only        BOOLEAN      NOT NULL DEFAULT true,
                legal_hold_active       BOOLEAN      NOT NULL DEFAULT false,
                legal_hold_reason       TEXT         NULL,
                legal_hold_set_at       TIMESTAMPTZ  NULL,
                legal_hold_set_by       VARCHAR(200) NULL,
                created_at              TIMESTAMPTZ  NOT NULL DEFAULT NOW(),
                updated_at              TIMESTAMPTZ  NOT NULL DEFAULT NOW(),
                updated_by              VARCHAR(200) NULL
            )
            """);

        await db.ExecuteAsync("""
            CREATE INDEX IF NOT EXISTS idx_drp_company ON data_retention_policies (company_id)
            """);

        await db.ExecuteAsync("""
            CREATE TABLE IF NOT EXISTS export_requests (
                id                      BIGINT       GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
                company_id              BIGINT       NOT NULL,
                requested_by_user_id    BIGINT       NOT NULL,
                requested_by_name       VARCHAR(200) NULL,
                export_type             VARCHAR(100) NOT NULL,
                dataset_name            VARCHAR(200) NULL,
                row_count_estimate      INT          NULL,
                status                  VARCHAR(30)
                                        NOT NULL DEFAULT 'pending_approval'
                                        CHECK (status IN ('pending_approval','approved','rejected','completed','cancelled')),
                approved_by_user_id     BIGINT       NULL,
                approved_by_name        VARCHAR(200) NULL,
                reviewed_at             TIMESTAMPTZ  NULL,
                review_notes            TEXT         NULL,
                created_at              TIMESTAMPTZ  NOT NULL DEFAULT NOW(),
                completed_at            TIMESTAMPTZ  NULL
            )
            """);

        await db.ExecuteAsync("""
            CREATE INDEX IF NOT EXISTS idx_er_company ON export_requests (company_id)
            """);
        await db.ExecuteAsync("""
            CREATE INDEX IF NOT EXISTS idx_er_status ON export_requests (status)
            """);
        await db.ExecuteAsync("""
            CREATE INDEX IF NOT EXISTS idx_er_requester ON export_requests (requested_by_user_id)
            """);
        await db.ExecuteAsync("""
            CREATE INDEX IF NOT EXISTS idx_er_created ON export_requests (created_at)
            """);

        // Safely add security columns to users table (ignore if already exist)
        foreach (var col in new[]
        {
            "ALTER TABLE users ADD COLUMN IF NOT EXISTS failed_login_attempts INT NOT NULL DEFAULT 0",
            "ALTER TABLE users ADD COLUMN IF NOT EXISTS locked_until TIMESTAMPTZ NULL",
            "ALTER TABLE users ADD COLUMN IF NOT EXISTS force_password_change BOOLEAN NOT NULL DEFAULT false",
            "ALTER TABLE users ADD COLUMN IF NOT EXISTS password_changed_at TIMESTAMPTZ NULL",
        })
        {
            try { await db.ExecuteAsync(col); }
            catch { /* column already exists — safe to ignore */ }
        }

        // Declared entity relationships for the IAM tables. NOT VALID skips the
        // backfill scan (and tolerates any legacy orphan rows) while still
        // enforcing referential integrity for every new write. Duplicate-add
        // throws, which the catch treats as already-present.
        foreach (var fk in new[]
        {
            "ALTER TABLE user_mfa_status ADD CONSTRAINT fk_mfa_user FOREIGN KEY (user_id) REFERENCES users(id) ON DELETE CASCADE NOT VALID",
            "ALTER TABLE sso_connections ADD CONSTRAINT fk_sso_company FOREIGN KEY (company_id) REFERENCES companies(id) NOT VALID",
            "ALTER TABLE access_reviews ADD CONSTRAINT fk_ar_company FOREIGN KEY (company_id) REFERENCES companies(id) NOT VALID",
            "ALTER TABLE access_review_items ADD CONSTRAINT fk_ari_review FOREIGN KEY (review_id) REFERENCES access_reviews(id) ON DELETE CASCADE NOT VALID",
            "ALTER TABLE access_review_items ADD CONSTRAINT fk_ari_company FOREIGN KEY (company_id) REFERENCES companies(id) NOT VALID",
            "ALTER TABLE company_security_settings ADD CONSTRAINT fk_css_company FOREIGN KEY (company_id) REFERENCES companies(id) NOT VALID",
        })
        {
            try { await db.ExecuteAsync(fk); }
            catch { /* constraint already exists — safe to ignore */ }
        }

        // Seed default compliance controls if none exist
        var count = await db.ScalarLongAsync("SELECT COUNT(*) FROM compliance_controls");
        if (count == 0) await SeedDefaultControlsAsync();
    }

    private Task SeedDefaultControlsAsync() => db.ExecuteAsync("""
        INSERT INTO compliance_controls (control_id, framework, title, description, category, status) VALUES
        ('CC6.1', 'SOC2_readiness', 'Logical Access Controls', 'Access to information assets is restricted to authorized users and processes', 'Access Control', 'partial'),
        ('CC6.2', 'SOC2_readiness', 'New Access Provisioning', 'New logical access is granted to authorized users only', 'Access Control', 'partial'),
        ('CC6.3', 'SOC2_readiness', 'Access Modification and Removal', 'User access is modified or removed when roles change or employment ends', 'Access Control', 'not_implemented'),
        ('CC6.6', 'SOC2_readiness', 'Logical Access - External Threats', 'Logical access security measures protect against threats from external sources', 'Access Control', 'partial'),
        ('CC6.7', 'SOC2_readiness', 'Transmission Integrity', 'Data is protected during transmission from unauthorized access', 'Encryption', 'partial'),
        ('CC6.8', 'SOC2_readiness', 'Malicious Software Prevention', 'Controls prevent malicious software from being introduced', 'Endpoint Security', 'not_implemented'),
        ('CC7.1', 'SOC2_readiness', 'System Monitoring', 'Systems and infrastructure are monitored to detect anomalies', 'Monitoring', 'partial'),
        ('CC7.2', 'SOC2_readiness', 'Security Event Monitoring', 'Security events are identified and responded to in a timely manner', 'Monitoring', 'partial'),
        ('CC7.3', 'SOC2_readiness', 'Incident Identification', 'Security incidents are identified, documented and tracked', 'Incident Response', 'partial'),
        ('CC7.4', 'SOC2_readiness', 'Incident Response', 'Incident response procedures are followed', 'Incident Response', 'not_implemented'),
        ('CC9.1', 'SOC2_readiness', 'Business Continuity and Recovery', 'Recovery plan exists and is tested', 'Business Continuity', 'not_implemented'),
        ('CC9.2', 'SOC2_readiness', 'Backup and Recovery', 'Data is backed up and restore is periodically tested', 'Business Continuity', 'not_implemented'),
        ('A1.1', 'SOC2_readiness', 'Performance Capacity Monitoring', 'Capacity is monitored against commitments and requirements', 'Availability', 'partial'),
        ('A1.2', 'SOC2_readiness', 'Environmental Protections', 'System components are protected from environmental threats', 'Availability', 'not_applicable'),
        ('A1.3', 'SOC2_readiness', 'Backup and Recovery Testing', 'Data recovery procedures are tested', 'Availability', 'not_implemented'),
        ('C1.1', 'SOC2_readiness', 'Data Classification', 'Confidential information is identified and classified', 'Confidentiality', 'partial'),
        ('C1.2', 'SOC2_readiness', 'Confidential Information Disposal', 'Confidential information is protected during disposal', 'Confidentiality', 'not_implemented'),
        ('P1.1', 'SOC2_readiness', 'Privacy Notice', 'Notice is provided regarding privacy practices', 'Privacy', 'not_implemented'),
        ('P3.1', 'SOC2_readiness', 'Privacy Consent', 'Consent for personal data collection and use is obtained', 'Privacy', 'not_implemented'),
        ('INT-1', 'internal_security', 'Multi-Factor Authentication', 'MFA is enforced for all admin and privileged users', 'Identity', 'not_implemented'),
        ('INT-2', 'internal_security', 'Password Policy', 'Password complexity and expiry policies are enforced', 'Identity', 'partial'),
        ('INT-3', 'internal_security', 'Session Management', 'Session idle and absolute timeouts are enforced', 'Identity', 'partial'),
        ('INT-4', 'internal_security', 'Audit Logging', 'All privileged actions are audit-logged and retained', 'Audit', 'partial'),
        ('INT-5', 'internal_security', 'Security Event Logging', 'Authentication events, failures, and lockouts are logged', 'Audit', 'partial'),
        ('INT-6', 'internal_security', 'Access Reviews', 'User access is periodically reviewed and certified', 'Access Control', 'not_implemented'),
        ('INT-7', 'internal_security', 'SSO/IdP Integration', 'Enterprise SSO integration is available for tenant admins', 'Identity', 'partial'),
        ('INT-8', 'internal_security', 'Export Governance', 'Sensitive data exports require approval where configured', 'Data Governance', 'partial'),
        ('INT-9', 'internal_security', 'Data Retention Policies', 'Data retention and deletion policies are configured per tenant', 'Data Governance', 'partial'),
        ('INT-10', 'internal_security', 'Tenant Isolation', 'All data queries enforce company_id scoping', 'Data Isolation', 'implemented')
        ON CONFLICT (control_id) DO UPDATE SET title = EXCLUDED.title
        """);
}
