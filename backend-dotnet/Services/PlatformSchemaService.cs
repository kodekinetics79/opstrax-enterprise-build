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
                reason         VARCHAR(400) NOT NULL,
                expires_at     TIMESTAMPTZ  NOT NULL,
                ended_at       TIMESTAMPTZ  NULL,
                created_at     TIMESTAMPTZ  NOT NULL DEFAULT NOW()
            )
            """);

        await SeedRolesAsync();
        await SeedSuperAdminAsync();
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
        ["support_admin"] = ("Support Admin", "Inspect tenant status and run safe impersonation. No billing/package control.",
            ["platform:dashboard:view", "platform:tenants:view", "platform:support:view", "platform:impersonation:start"]),
        ["product_admin"] = ("Product Admin", "Manage feature entitlements, packages and platform health.",
            ["platform:dashboard:view", "platform:tenants:view", "platform:entitlements:view", "platform:entitlements:manage", "platform:packages:view", "platform:packages:manage", "platform:countries:view", "platform:countries:manage", "platform:ops:view"]),
        ["compliance_admin"] = ("Compliance Admin", "Audit, security and access review oversight.",
            ["platform:dashboard:view", "platform:tenants:view", "platform:audit:view", "platform:ops:view", "platform:admins:view"]),
        ["readonly_executive"] = ("Read-only Executive", "Executive read-only visibility of the whole business.",
            ["platform:dashboard:view", "platform:tenants:view", "platform:packages:view", "platform:billing:view", "platform:health:view", "platform:crm:view", "platform:audit:view"]),
    };

    private async Task SeedRolesAsync()
    {
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

        var email = Environment.GetEnvironmentVariable("PLATFORM_SUPERADMIN_EMAIL") ?? "platform@opstrax.io";
        var password = Environment.GetEnvironmentVariable("PLATFORM_SUPERADMIN_PASSWORD") ?? "Platform@12345";

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
