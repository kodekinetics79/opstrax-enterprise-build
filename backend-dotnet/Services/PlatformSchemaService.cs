using System.Security.Cryptography;
using Opstrax.Api.Data;

namespace Opstrax.Api.Services;

// ─────────────────────────────────────────────────────────────────────────────
// PLATFORM ADMIN — Global SaaS business control plane (separate from Tenant Admin)
//
// Tenant Admin = one company (companies row). Platform Admin = the SaaS business
// across ALL tenants. Platform identity, sessions, RBAC and audit are intentionally
// kept in dedicated tables so platform staff are never tenant users and platform
// auth never grants tenant data access except where a platform permission allows it.
//
// Tables:
//   platform_roles             — platform RBAC roles (super admin, sales, finance…)
//   platform_role_permissions  — permission_key grants per role
//   platform_admins            — platform staff identities (no tenant company_id)
//   platform_sessions          — bearer tokens for platform staff
//   platform_audit_log         — every platform action (create/update/status/billing/impersonation)
//   packages                   — pricing packages (base + seat + modules + custom)
//   tenant_subscriptions       — commercial state per tenant (status, seats, dates, MRR)
//   tenant_entitlements        — per-module enable/limit, package default + override
//   platform_invoices          — recurring/one-time invoices + payment status
//   platform_impersonation_sessions — safe, time-limited, audited tenant access
// ─────────────────────────────────────────────────────────────────────────────

public sealed class PlatformSchemaService(Database db)
{
    private const int PasswordHashIterations = 100_000;
    private const int PasswordSaltLength = 16;
    private const int PasswordSubkeyLength = 32;

    public async Task EnsureAsync()
    {
        await db.ExecuteAsync("""
            CREATE TABLE IF NOT EXISTS platform_roles (
                id           BIGINT       GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
                role_key     VARCHAR(60)  NOT NULL UNIQUE,
                name         VARCHAR(120) NOT NULL,
                description  VARCHAR(300) NULL,
                created_at   TIMESTAMPTZ  NOT NULL DEFAULT NOW()
            )
            """);

        await db.ExecuteAsync("""
            CREATE TABLE IF NOT EXISTS platform_role_permissions (
                role_id        BIGINT      NOT NULL REFERENCES platform_roles(id) ON DELETE CASCADE,
                permission_key VARCHAR(80) NOT NULL,
                PRIMARY KEY (role_id, permission_key)
            )
            """);

        await db.ExecuteAsync("""
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
            )
            """);

        // Operator-management columns (additive): invite/password-setup flow state.
        // Only the SHA-256 hash of an invite token is ever stored.
        await db.ExecuteAsync("ALTER TABLE platform_admins ADD COLUMN IF NOT EXISTS invite_token_hash VARCHAR(128) NULL");
        await db.ExecuteAsync("ALTER TABLE platform_admins ADD COLUMN IF NOT EXISTS invite_expires_at TIMESTAMPTZ NULL");
        await db.ExecuteAsync("ALTER TABLE platform_admins ADD COLUMN IF NOT EXISTS updated_at TIMESTAMPTZ NOT NULL DEFAULT NOW()");
        // TOTP second factor: base32 secret set at enrollment; mfa_enabled flips
        // true only after the operator proves possession with a valid code.
        await db.ExecuteAsync("ALTER TABLE platform_admins ADD COLUMN IF NOT EXISTS mfa_secret VARCHAR(160) NULL");

        await db.ExecuteAsync("""
            CREATE TABLE IF NOT EXISTS platform_sessions (
                id            BIGINT       GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
                admin_id      BIGINT       NOT NULL REFERENCES platform_admins(id) ON DELETE CASCADE,
                session_token VARCHAR(255) NOT NULL UNIQUE,
                expires_at    TIMESTAMPTZ  NOT NULL,
                created_at    TIMESTAMPTZ  NOT NULL DEFAULT NOW()
            )
            """);

        await db.ExecuteAsync("""
            CREATE TABLE IF NOT EXISTS platform_audit_log (
                id               BIGINT       GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
                actor_admin_id   BIGINT       NULL,
                actor_email      VARCHAR(220) NULL,
                actor_role       VARCHAR(80)  NULL,
                action           VARCHAR(120) NOT NULL,
                entity_type      VARCHAR(80)  NOT NULL,
                entity_id        BIGINT       NULL,
                target_company_id BIGINT      NULL,
                details_json     JSONB        NULL,
                ip_address       VARCHAR(80)  NULL,
                created_at       TIMESTAMPTZ  NOT NULL DEFAULT NOW()
            )
            """);
        await db.ExecuteAsync("CREATE INDEX IF NOT EXISTS idx_platform_audit_created ON platform_audit_log (created_at DESC)");
        // Serves the durable (DB-backed) login / accept-invite lockout counters.
        await db.ExecuteAsync("CREATE INDEX IF NOT EXISTS idx_platform_audit_email_action ON platform_audit_log (actor_email, action, created_at DESC)");

        await db.ExecuteAsync("""
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
            )
            """);

        await db.ExecuteAsync("""
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
            )
            """);
        await db.ExecuteAsync("CREATE INDEX IF NOT EXISTS idx_tenant_sub_status ON tenant_subscriptions (status)");

        await db.ExecuteAsync("""
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
            )
            """);
        await db.ExecuteAsync("CREATE INDEX IF NOT EXISTS idx_entitlement_company ON tenant_entitlements (company_id)");

        // System-owned provenance for versioned demo/pilot fixture reconciliation.
        // It is created during owner-capable schema initialization so the terminal RLS
        // reconciliation can enroll it before any non-production seed endpoint runs.
        // Production creates the same object out-of-band in Stage 68.
        await db.ExecuteAsync("""
            CREATE TABLE IF NOT EXISTS demo_fixture_versions (
                company_id      BIGINT      NOT NULL REFERENCES companies(id) ON DELETE CASCADE,
                fixture_key     VARCHAR(80) NOT NULL,
                fixture_version INT         NOT NULL CHECK (fixture_version > 0),
                applied_at      TIMESTAMPTZ NOT NULL DEFAULT NOW(),
                PRIMARY KEY (company_id, fixture_key)
            )
            """);

        await db.ExecuteAsync("""
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
            )
            """);
        await db.ExecuteAsync("CREATE INDEX IF NOT EXISTS idx_invoice_company ON platform_invoices (company_id)");

        await db.ExecuteAsync("""
            CREATE TABLE IF NOT EXISTS platform_impersonation_sessions (
                id             BIGINT       GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
                admin_id       BIGINT       NOT NULL REFERENCES platform_admins(id),
                company_id     BIGINT       NOT NULL REFERENCES companies(id),
                target_user_id BIGINT       NULL REFERENCES users(id),
                grant_ref      UUID         NULL DEFAULT gen_random_uuid(),
                reason         VARCHAR(400) NOT NULL,
                expires_at     TIMESTAMPTZ  NOT NULL,
                ended_at       TIMESTAMPTZ  NULL,
                created_at     TIMESTAMPTZ  NOT NULL DEFAULT NOW()
            )
            """);
        await db.ExecuteAsync("ALTER TABLE platform_impersonation_sessions ADD COLUMN IF NOT EXISTS target_user_id BIGINT NULL REFERENCES users(id)");
        await db.ExecuteAsync("ALTER TABLE platform_impersonation_sessions ADD COLUMN IF NOT EXISTS grant_ref UUID NULL DEFAULT gen_random_uuid()");
        await db.ExecuteAsync("UPDATE platform_impersonation_sessions SET grant_ref=gen_random_uuid() WHERE grant_ref IS NULL");
        await db.ExecuteAsync("""
            DO $$ BEGIN
                IF EXISTS (SELECT 1 FROM platform_impersonation_sessions WHERE target_user_id IS NULL) THEN
                    RAISE EXCEPTION 'Unbound historical impersonation grants require operator reconciliation';
                END IF;
            END $$
            """);
        await db.ExecuteAsync("ALTER TABLE platform_impersonation_sessions ALTER COLUMN grant_ref SET NOT NULL");
        await db.ExecuteAsync("ALTER TABLE platform_impersonation_sessions ALTER COLUMN target_user_id SET NOT NULL");
        await db.ExecuteAsync("CREATE UNIQUE INDEX IF NOT EXISTS ux_platform_impersonation_grant_ref ON platform_impersonation_sessions(grant_ref)");
        await db.ExecuteAsync("ALTER TABLE user_sessions ADD COLUMN IF NOT EXISTS impersonation_grant_id BIGINT NULL");
        await db.ExecuteAsync("CREATE UNIQUE INDEX IF NOT EXISTS ux_user_sessions_impersonation_grant ON user_sessions(impersonation_grant_id) WHERE impersonation_grant_id IS NOT NULL");
        await db.ExecuteAsync("""
            DO $$
            BEGIN
                IF NOT EXISTS (SELECT 1 FROM pg_constraint WHERE conname='fk_user_sessions_impersonation_grant') THEN
                    ALTER TABLE user_sessions ADD CONSTRAINT fk_user_sessions_impersonation_grant
                    FOREIGN KEY (impersonation_grant_id) REFERENCES platform_impersonation_sessions(id) ON DELETE CASCADE;
                END IF;
                IF NOT EXISTS (SELECT 1 FROM pg_constraint WHERE conname='ck_platform_impersonation_expiry') THEN
                    ALTER TABLE platform_impersonation_sessions ADD CONSTRAINT ck_platform_impersonation_expiry
                    CHECK (expires_at > created_at AND expires_at <= created_at + INTERVAL '60 minutes');
                END IF;
            END $$
            """);
        await db.ExecuteAsync("""
            CREATE OR REPLACE FUNCTION validate_impersonation_session_binding()
            RETURNS trigger LANGUAGE plpgsql AS $$
            BEGIN
                IF NEW.impersonation_grant_id IS NOT NULL AND NOT EXISTS (
                    SELECT 1 FROM platform_impersonation_sessions p
                    WHERE p.id=NEW.impersonation_grant_id
                      AND p.company_id=NEW.company_id AND p.target_user_id=NEW.user_id
                      AND p.ended_at IS NULL AND p.expires_at>NOW()
                ) THEN
                    RAISE EXCEPTION 'Invalid or inactive impersonation grant binding';
                END IF;
                RETURN NEW;
            END $$;
            DROP TRIGGER IF EXISTS trg_validate_impersonation_session_binding ON user_sessions;
            CREATE TRIGGER trg_validate_impersonation_session_binding
              BEFORE INSERT OR UPDATE OF impersonation_grant_id, user_id, company_id ON user_sessions
              FOR EACH ROW EXECUTE FUNCTION validate_impersonation_session_binding();
            """);

        await EnsureTenantProfileColumnsAsync();

        await SeedRolesAsync();
        await SeedSuperAdminAsync();
    }

    // Extended tenant provisioning attributes captured on the (Samsara-benchmark)
    // New Tenant form. Additive + idempotent — companies pre-exists and already
    // carries country/currency/timezone/status, so these follow the same
    // ADD COLUMN IF NOT EXISTS pattern rather than a destructive rebuild.
    private async Task EnsureTenantProfileColumnsAsync()
    {
        string[] companyCols =
        {
            "ALTER TABLE companies ADD COLUMN IF NOT EXISTS legal_name VARCHAR(220) NULL",
            "ALTER TABLE companies ADD COLUMN IF NOT EXISTS website VARCHAR(200) NULL",
            "ALTER TABLE companies ADD COLUMN IF NOT EXISTS fleet_size INT NULL",
            "ALTER TABLE companies ADD COLUMN IF NOT EXISTS tax_id VARCHAR(80) NULL",
            "ALTER TABLE companies ADD COLUMN IF NOT EXISTS primary_contact_name VARCHAR(160) NULL",
            "ALTER TABLE companies ADD COLUMN IF NOT EXISTS primary_contact_email VARCHAR(200) NULL",
            "ALTER TABLE companies ADD COLUMN IF NOT EXISTS primary_contact_phone VARCHAR(40) NULL",
            "ALTER TABLE companies ADD COLUMN IF NOT EXISTS billing_email VARCHAR(200) NULL",
            // Compatibility-safe commercial authorization migration. Existing rows
            // receive legacy_allow; tenant provisioning explicitly opts new customers
            // into package_allowlist. A database default is intentionally legacy-safe
            // for older seed/import paths that do not yet name the policy.
            "ALTER TABLE companies ADD COLUMN IF NOT EXISTS entitlement_policy_mode VARCHAR(32) NOT NULL DEFAULT 'legacy_allow'",
        };
        foreach (var sql in companyCols) await db.ExecuteAsync(sql);

        // PostgreSQL has no ADD CONSTRAINT IF NOT EXISTS. The catalog guard keeps this
        // idempotent while ensuring invalid policy text can never silently fail open.
        await db.ExecuteAsync("""
            DO $$
            BEGIN
                IF NOT EXISTS (
                    SELECT 1 FROM pg_constraint
                    WHERE conname = 'ck_companies_entitlement_policy_mode'
                      AND conrelid = 'companies'::regclass
                ) THEN
                    ALTER TABLE companies
                    ADD CONSTRAINT ck_companies_entitlement_policy_mode
                    CHECK (entitlement_policy_mode IN ('legacy_allow', 'package_allowlist'));
                END IF;
            END $$
            """);

        // Commercial term on the subscription: monthly | annual billing cadence.
        await db.ExecuteAsync("ALTER TABLE tenant_subscriptions ADD COLUMN IF NOT EXISTS billing_cycle VARCHAR(20) NOT NULL DEFAULT 'monthly'");
    }

    // Platform RBAC roles + their permission grants. permission_key uses the
    // platform: namespace so it can never collide with tenant permissions.
    private static readonly Dictionary<string, (string Name, string Description, string[] Permissions)> RoleSeed = new()
    {
        ["platform_super_admin"] = ("Platform Super Admin", "Full control of the SaaS business across all tenants.",
            ["platform:*"]),
        ["sales_admin"] = ("Sales Admin", "Manage CRM pipeline, proposals and tenant provisioning.",
            ["platform:dashboard:view", "platform:tenants:view", "platform:tenants:manage", "platform:packages:view", "platform:countries:view", "platform:crm:view", "platform:crm:manage", "platform:proposals:view", "platform:proposals:manage"]),
        ["marketing_admin"] = ("Marketing Admin", "Manage campaigns and customer segments.",
            ["platform:dashboard:view", "platform:tenants:view", "platform:marketing:view", "platform:marketing:manage", "platform:crm:view"]),
        ["finance_admin"] = ("Finance Admin", "Manage billing, invoices and revenue. Read-only on entitlements.",
            ["platform:dashboard:view", "platform:tenants:view", "platform:packages:view", "platform:packages:manage", "platform:billing:view", "platform:billing:manage", "platform:audit:view"]),
        ["customer_success_admin"] = ("Customer Success Admin", "Tenant health, renewals and upsell follow-up.",
            ["platform:dashboard:view", "platform:tenants:view", "platform:health:view", "platform:health:manage", "platform:crm:view"]),
        ["support_admin"] = ("Support Admin", "Inspect tenant status. Bounded support access requires an explicit, separately reviewed grant.",
            ["platform:dashboard:view", "platform:tenants:view", "platform:support:view"]),
        ["product_admin"] = ("Product Admin", "Manage feature entitlements, packages and platform health.",
            ["platform:dashboard:view", "platform:tenants:view", "platform:entitlements:view", "platform:entitlements:manage", "platform:packages:view", "platform:packages:manage", "platform:countries:view", "platform:countries:manage", "platform:ops:view"]),
        ["compliance_admin"] = ("Compliance Admin", "Audit, security and access review oversight.",
            ["platform:dashboard:view", "platform:tenants:view", "platform:audit:view", "platform:ops:view", "platform:admins:view"]),
        ["readonly_executive"] = ("Read-only Executive", "Executive read-only visibility of the whole business.",
            ["platform:dashboard:view", "platform:tenants:view", "platform:packages:view", "platform:billing:view", "platform:health:view", "platform:crm:view", "platform:audit:view"]),
    };

    private async Task SeedRolesAsync()
    {
        // The former seed granted write-capable impersonation to every support
        // operator. Revoke that inherited grant; only an explicitly reviewed custom
        // role (or platform super-admin) may reach the separately disabled endpoint.
        await db.ExecuteAsync("""
            DELETE FROM platform_role_permissions rp
            USING platform_roles r
            WHERE rp.role_id=r.id AND r.role_key='support_admin'
              AND rp.permission_key='platform:impersonation:start'
            """);
        foreach (var (key, def) in RoleSeed)
        {
            var roleId = await db.InsertAsync(
                @"INSERT INTO platform_roles (role_key, name, description) VALUES (@k, @n, @d)
                  ON CONFLICT (role_key) DO UPDATE SET name = EXCLUDED.name, description = EXCLUDED.description
                  RETURNING id",
                c =>
                {
                    c.Parameters.AddWithValue("@k", key);
                    c.Parameters.AddWithValue("@n", def.Name);
                    c.Parameters.AddWithValue("@d", def.Description);
                });

            if (roleId <= 0)
            {
                roleId = await db.ScalarLongAsync("SELECT id FROM platform_roles WHERE role_key=@k",
                    c => c.Parameters.AddWithValue("@k", key));
            }

            foreach (var perm in def.Permissions)
            {
                await db.ExecuteAsync(
                    @"INSERT INTO platform_role_permissions (role_id, permission_key) VALUES (@r, @p)
                      ON CONFLICT (role_id, permission_key) DO NOTHING",
                    c =>
                    {
                        c.Parameters.AddWithValue("@r", roleId);
                        c.Parameters.AddWithValue("@p", perm);
                    });
            }
        }
    }

    // Bootstrap super admin. Credentials come from env (PLATFORM_SUPERADMIN_EMAIL /
    // PLATFORM_SUPERADMIN_PASSWORD) so they are never hard-coded; falls back to a
    // well-known demo identity for local/dev only.
    // FIRST-SETUP ONLY: once ANY platform admin exists, the seed never runs again —
    // operator lifecycle is owned by /api/platform/admins from that point on, so a
    // changed env var cannot silently mint a new bootstrap identity later.
    private async Task SeedSuperAdminAsync()
    {
        var anyAdmin = await db.ScalarLongAsync("SELECT COUNT(*) FROM platform_admins");
        if (anyAdmin > 0) return;

        var email = Environment.GetEnvironmentVariable("PLATFORM_SUPERADMIN_EMAIL");
        var password = Environment.GetEnvironmentVariable("PLATFORM_SUPERADMIN_PASSWORD");

        // SECURITY (fail-closed): never seed a repo-known default credential in production. If the
        // bootstrap env vars are absent on a production deploy, skip seeding entirely — the operator
        // must provide PLATFORM_SUPERADMIN_EMAIL/PASSWORD to mint the first admin. The well-known demo
        // identity is ONLY a local/dev convenience. (A default super-admin whose password is in the
        // source tree would grant cross-tenant control on any misconfigured deploy.)
        var env = Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT") ?? "Production";
        var isProduction = string.Equals(env, "Production", StringComparison.OrdinalIgnoreCase);
        if (string.IsNullOrWhiteSpace(email) || string.IsNullOrWhiteSpace(password))
        {
            if (isProduction) return;
            email ??= "platform@opstrax.io";
            password ??= "Platform@12345";
        }

        var roleId = await db.ScalarLongAsync("SELECT id FROM platform_roles WHERE role_key='platform_super_admin'");
        var hash = HashPassword(password);

        await db.ExecuteAsync(
            @"INSERT INTO platform_admins (email, full_name, password_hash, role_id, status)
              VALUES (@e, @n, @h, @r, 'Active')
              ON CONFLICT (email) DO NOTHING",
            c =>
            {
                c.Parameters.AddWithValue("@e", email);
                c.Parameters.AddWithValue("@n", "Platform Owner");
                c.Parameters.AddWithValue("@h", hash);
                c.Parameters.AddWithValue("@r", roleId > 0 ? roleId : DBNull.Value);
            });
    }

    public static string HashPassword(string password)
    {
        var salt = RandomNumberGenerator.GetBytes(PasswordSaltLength);
        var subkey = Rfc2898DeriveBytes.Pbkdf2(password, salt, PasswordHashIterations, HashAlgorithmName.SHA256, PasswordSubkeyLength);
        return $"PBKDF2${PasswordHashIterations}${Convert.ToBase64String(salt)}${Convert.ToBase64String(subkey)}";
    }
}
