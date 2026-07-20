using Opstrax.Api.Data;

namespace Opstrax.Api.Services;

// ─────────────────────────────────────────────────────────────────────────────
// Settings persistence schema — everything the Settings page saves for real.
//
// Tables:
//   tenant_api_keys            — hashed per-tenant API keys (SHA-256 at rest).
//                                The raw key is shown ONCE at creation/rotation and
//                                never stored or returned again. Only a prefix +
//                                last-4 are persisted for display/identification.
//   tenant_webhook_settings    — one row per company: endpoint URL, enabled event
//                                subscriptions, and the HMAC signing secret used to
//                                sign delivered payloads (X-OpsTrax-Signature).
//   company_profile            — tenant display/contact profile (name, address,
//                                phone, email, website) edited on Settings → General.
//   user_notification_prefs    — per-user notification channel matrix (JSONB),
//                                company_id carried for RLS tenant isolation.
//
// Modelled on the device-provisioning credential pattern (eld_devices) and the
// company_security_settings config pattern. All access is company_id-scoped.
// ─────────────────────────────────────────────────────────────────────────────

public sealed class TenantApiSchemaService(Database db)
{
    public async Task EnsureAsync()
    {
        await db.ExecuteAsync("""
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
            )
            """);

        await db.ExecuteAsync("CREATE INDEX IF NOT EXISTS idx_tak_company ON tenant_api_keys (company_id)");
        await db.ExecuteAsync("CREATE INDEX IF NOT EXISTS idx_tak_hash ON tenant_api_keys (key_hash)");

        await db.ExecuteAsync("""
            CREATE TABLE IF NOT EXISTS tenant_webhook_settings (
                company_id      BIGINT       PRIMARY KEY,
                endpoint_url    VARCHAR(1000) NULL,
                events          JSONB        NOT NULL DEFAULT '[]'::jsonb,
                signing_secret  VARCHAR(128) NOT NULL,
                enabled         BOOLEAN      NOT NULL DEFAULT true,
                created_at      TIMESTAMPTZ  NOT NULL DEFAULT NOW(),
                updated_at      TIMESTAMPTZ  NOT NULL DEFAULT NOW(),
                updated_by      VARCHAR(200) NULL
            )
            """);

        await db.ExecuteAsync("""
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
            )
            """);

        await db.ExecuteAsync("""
            CREATE TABLE IF NOT EXISTS user_notification_prefs (
                user_id         BIGINT        PRIMARY KEY,
                company_id      BIGINT        NOT NULL,
                prefs           JSONB         NOT NULL DEFAULT '{}'::jsonb,
                updated_at      TIMESTAMPTZ   NOT NULL DEFAULT NOW()
            )
            """);

        await db.ExecuteAsync("CREATE INDEX IF NOT EXISTS idx_unp_company ON user_notification_prefs (company_id)");
    }
}
