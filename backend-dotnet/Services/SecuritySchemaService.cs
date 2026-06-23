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
            CREATE TABLE IF NOT EXISTS company_security_settings (
                id                              BIGINT       PRIMARY KEY AUTO_INCREMENT,
                company_id                      BIGINT       NOT NULL UNIQUE,
                mfa_required                    TINYINT(1)   NOT NULL DEFAULT 0,
                mfa_required_roles              JSON         NULL,
                password_min_length             INT          NOT NULL DEFAULT 8,
                password_requires_uppercase     TINYINT(1)   NOT NULL DEFAULT 0,
                password_requires_number        TINYINT(1)   NOT NULL DEFAULT 0,
                password_requires_symbol        TINYINT(1)   NOT NULL DEFAULT 0,
                password_expiry_days            INT          NOT NULL DEFAULT 0,
                session_idle_timeout_minutes    INT          NOT NULL DEFAULT 60,
                session_absolute_timeout_minutes INT         NOT NULL DEFAULT 480,
                max_failed_login_attempts       INT          NOT NULL DEFAULT 5,
                lockout_duration_minutes        INT          NOT NULL DEFAULT 30,
                allowed_sso_providers           JSON         NULL,
                export_approval_required        TINYINT(1)   NOT NULL DEFAULT 0,
                audit_retention_days            INT          NOT NULL DEFAULT 90,
                data_retention_days             INT          NOT NULL DEFAULT 365,
                created_at                      DATETIME     NOT NULL DEFAULT NOW(),
                updated_at                      DATETIME     NOT NULL DEFAULT NOW(),
                updated_by                      VARCHAR(200) NULL,
                INDEX idx_css_company (company_id)
            ) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4
            """);

        await db.ExecuteAsync("""
            CREATE TABLE IF NOT EXISTS user_mfa_status (
                user_id                         BIGINT       PRIMARY KEY,
                mfa_enabled                     TINYINT(1)   NOT NULL DEFAULT 0,
                mfa_provider                    VARCHAR(50)  NULL,
                enrolled_at                     DATETIME     NULL,
                last_used_at                    DATETIME     NULL,
                recovery_codes_generated_at     DATETIME     NULL,
                recovery_codes_remaining        INT          NOT NULL DEFAULT 0,
                updated_at                      DATETIME     NOT NULL DEFAULT NOW()
            ) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4
            """);

        await db.ExecuteAsync("""
            CREATE TABLE IF NOT EXISTS security_events (
                id                  BIGINT       PRIMARY KEY AUTO_INCREMENT,
                company_id          BIGINT       NOT NULL,
                user_id             BIGINT       NULL,
                event_type          VARCHAR(100) NOT NULL,
                severity            ENUM('critical','high','medium','low','info')
                                    NOT NULL DEFAULT 'info',
                source_ip_truncated VARCHAR(30)  NULL,
                user_agent_hash     VARCHAR(16)  NULL,
                success             TINYINT(1)   NOT NULL DEFAULT 1,
                safe_message        VARCHAR(500) NOT NULL,
                metadata_json       JSON         NULL,
                created_at          DATETIME     NOT NULL DEFAULT NOW(),
                INDEX idx_se_company (company_id),
                INDEX idx_se_type    (event_type),
                INDEX idx_se_created (created_at),
                INDEX idx_se_user    (user_id)
            ) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4
            """);

        await db.ExecuteAsync("""
            CREATE TABLE IF NOT EXISTS sso_connections (
                id                    BIGINT       PRIMARY KEY AUTO_INCREMENT,
                company_id            BIGINT       NOT NULL,
                provider_type         ENUM('oidc','saml') NOT NULL,
                display_name          VARCHAR(200) NOT NULL,
                issuer_or_entity_id   VARCHAR(500) NOT NULL,
                client_id             VARCHAR(500) NOT NULL,
                client_secret_ref     VARCHAR(200) NULL,
                certificate_thumbprint VARCHAR(200) NULL,
                enabled               TINYINT(1)   NOT NULL DEFAULT 1,
                domain_hints          JSON         NULL,
                metadata_url          VARCHAR(500) NULL,
                created_at            DATETIME     NOT NULL DEFAULT NOW(),
                updated_at            DATETIME     NOT NULL DEFAULT NOW(),
                created_by            VARCHAR(200) NULL,
                INDEX idx_sso_company (company_id),
                INDEX idx_sso_enabled (enabled)
            ) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4
            """);

        await db.ExecuteAsync("""
            CREATE TABLE IF NOT EXISTS access_reviews (
                id                  BIGINT       PRIMARY KEY AUTO_INCREMENT,
                company_id          BIGINT       NOT NULL,
                title               VARCHAR(500) NOT NULL,
                description         TEXT         NULL,
                reviewer_user_id    BIGINT       NOT NULL,
                status              ENUM('pending','in_progress','completed','cancelled')
                                    NOT NULL DEFAULT 'pending',
                due_date            DATE         NULL,
                started_at          DATETIME     NULL,
                completed_at        DATETIME     NULL,
                total_items         INT          NOT NULL DEFAULT 0,
                items_approved      INT          NOT NULL DEFAULT 0,
                items_revoked       INT          NOT NULL DEFAULT 0,
                items_pending       INT          NOT NULL DEFAULT 0,
                created_at          DATETIME     NOT NULL DEFAULT NOW(),
                created_by          VARCHAR(200) NULL,
                INDEX idx_ar_company  (company_id),
                INDEX idx_ar_status   (status),
                INDEX idx_ar_reviewer (reviewer_user_id)
            ) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4
            """);

        await db.ExecuteAsync("""
            CREATE TABLE IF NOT EXISTS access_review_items (
                id                      BIGINT       PRIMARY KEY AUTO_INCREMENT,
                review_id               BIGINT       NOT NULL,
                company_id              BIGINT       NOT NULL,
                target_user_id          BIGINT       NOT NULL,
                target_user_name        VARCHAR(200) NULL,
                target_user_email       VARCHAR(500) NULL,
                role_name               VARCHAR(200) NULL,
                permissions_snapshot    JSON         NULL,
                status                  ENUM('pending','approved','revoked','remediated')
                                        NOT NULL DEFAULT 'pending',
                completed_at            DATETIME     NULL,
                completed_by            VARCHAR(200) NULL,
                notes                   TEXT         NULL,
                created_at              DATETIME     NOT NULL DEFAULT NOW(),
                INDEX idx_ari_review  (review_id),
                INDEX idx_ari_company (company_id),
                INDEX idx_ari_status  (status),
                INDEX idx_ari_user    (target_user_id)
            ) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4
            """);

        await db.ExecuteAsync("""
            CREATE TABLE IF NOT EXISTS compliance_controls (
                id              BIGINT       PRIMARY KEY AUTO_INCREMENT,
                control_id      VARCHAR(50)  NOT NULL UNIQUE,
                framework       VARCHAR(100) NOT NULL DEFAULT 'SOC2_readiness',
                title           VARCHAR(500) NOT NULL,
                description     TEXT         NULL,
                owner           VARCHAR(200) NULL,
                status          ENUM('implemented','partial','not_implemented','not_applicable')
                                NOT NULL DEFAULT 'not_implemented',
                category        VARCHAR(200) NULL,
                created_at      DATETIME     NOT NULL DEFAULT NOW(),
                updated_at      DATETIME     NOT NULL DEFAULT NOW(),
                INDEX idx_cc_framework (framework),
                INDEX idx_cc_status    (status)
            ) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4
            """);

        await db.ExecuteAsync("""
            CREATE TABLE IF NOT EXISTS compliance_evidence (
                id               BIGINT       PRIMARY KEY AUTO_INCREMENT,
                control_id       VARCHAR(50)  NOT NULL,
                evidence_type    VARCHAR(100) NOT NULL,
                source_system    VARCHAR(100) NOT NULL,
                source_entity    VARCHAR(200) NULL,
                source_record_id BIGINT       NULL,
                title            VARCHAR(500) NOT NULL,
                safe_summary     TEXT         NULL,
                generated_at     DATETIME     NOT NULL DEFAULT NOW(),
                evidence_hash    VARCHAR(64)  NULL,
                retention_until  DATE         NULL,
                generated_by     VARCHAR(200) NULL,
                INDEX idx_ce_control   (control_id),
                INDEX idx_ce_type      (evidence_type),
                INDEX idx_ce_generated (generated_at)
            ) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4
            """);

        await db.ExecuteAsync("""
            CREATE TABLE IF NOT EXISTS backup_verifications (
                id                      BIGINT       PRIMARY KEY AUTO_INCREMENT,
                company_id              BIGINT       NULL,
                backup_type             VARCHAR(100) NOT NULL,
                status                  ENUM('passed','failed','warning','not_configured')
                                        NOT NULL DEFAULT 'not_configured',
                verified_at             DATETIME     NOT NULL DEFAULT NOW(),
                restore_tested          TINYINT(1)   NOT NULL DEFAULT 0,
                duration_ms             INT          NULL,
                storage_location_label  VARCHAR(200) NULL,
                safe_error              TEXT         NULL,
                evidence_hash           VARCHAR(64)  NULL,
                INDEX idx_bv_company  (company_id),
                INDEX idx_bv_type     (backup_type),
                INDEX idx_bv_status   (status),
                INDEX idx_bv_verified (verified_at)
            ) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4
            """);

        await db.ExecuteAsync("""
            CREATE TABLE IF NOT EXISTS data_retention_policies (
                id                      BIGINT       PRIMARY KEY AUTO_INCREMENT,
                company_id              BIGINT       NOT NULL UNIQUE,
                audit_log_days          INT          NOT NULL DEFAULT 90,
                telemetry_days          INT          NOT NULL DEFAULT 90,
                notification_days       INT          NOT NULL DEFAULT 30,
                report_execution_days   INT          NOT NULL DEFAULT 180,
                security_event_days     INT          NOT NULL DEFAULT 365,
                soft_delete_only        TINYINT(1)   NOT NULL DEFAULT 1,
                legal_hold_active       TINYINT(1)   NOT NULL DEFAULT 0,
                legal_hold_reason       TEXT         NULL,
                legal_hold_set_at       DATETIME     NULL,
                legal_hold_set_by       VARCHAR(200) NULL,
                created_at              DATETIME     NOT NULL DEFAULT NOW(),
                updated_at              DATETIME     NOT NULL DEFAULT NOW(),
                updated_by              VARCHAR(200) NULL,
                INDEX idx_drp_company (company_id)
            ) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4
            """);

        await db.ExecuteAsync("""
            CREATE TABLE IF NOT EXISTS export_requests (
                id                      BIGINT       PRIMARY KEY AUTO_INCREMENT,
                company_id              BIGINT       NOT NULL,
                requested_by_user_id    BIGINT       NOT NULL,
                requested_by_name       VARCHAR(200) NULL,
                export_type             VARCHAR(100) NOT NULL,
                dataset_name            VARCHAR(200) NULL,
                row_count_estimate      INT          NULL,
                status                  ENUM('pending_approval','approved','rejected','completed','cancelled')
                                        NOT NULL DEFAULT 'pending_approval',
                approved_by_user_id     BIGINT       NULL,
                approved_by_name        VARCHAR(200) NULL,
                reviewed_at             DATETIME     NULL,
                review_notes            TEXT         NULL,
                created_at              DATETIME     NOT NULL DEFAULT NOW(),
                completed_at            DATETIME     NULL,
                INDEX idx_er_company   (company_id),
                INDEX idx_er_status    (status),
                INDEX idx_er_requester (requested_by_user_id),
                INDEX idx_er_created   (created_at)
            ) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4
            """);

        // Safely add security columns to users table (ignore if already exist)
        foreach (var col in new[]
        {
            "ALTER TABLE users ADD COLUMN failed_login_attempts INT NOT NULL DEFAULT 0",
            "ALTER TABLE users ADD COLUMN locked_until DATETIME NULL",
            "ALTER TABLE users ADD COLUMN force_password_change TINYINT(1) NOT NULL DEFAULT 0",
            "ALTER TABLE users ADD COLUMN password_changed_at DATETIME NULL",
        })
        {
            try { await db.ExecuteAsync(col); }
            catch { /* column already exists — safe to ignore */ }
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
        ON DUPLICATE KEY UPDATE title = VALUES(title)
        """);
}
