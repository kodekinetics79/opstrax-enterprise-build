using Opstrax.Api.Data;

namespace Opstrax.Api.Services;

// ─────────────────────────────────────────────────────────────────────────────
// REVENUE FOUNDATION — extends the existing Platform Admin control plane
// (packages, tenant_subscriptions, tenant_entitlements, platform_invoices) with
// the commercial primitives that were missing: a module-package CATALOG, usage
// meters, raw usage events, rolled-up usage counters, pricing rules and per-tenant
// contract overrides. Invoice previews are COMPUTED on demand from subscription +
// usage + pricing + overrides, so there is no persisted-preview drift.
//
// Nothing here is destructive: every statement is CREATE TABLE/INDEX IF NOT EXISTS
// or an idempotent upsert seed. The existing PlatformSchemaService tables are
// reused as-is (this service runs after it in the startup pipeline).
//
// Module keys are the single vocabulary shared by tenant_entitlements.module_key,
// module_packages.module_keys and the in-code entitlement guards.
// ─────────────────────────────────────────────────────────────────────────────
public sealed class RevenueSchemaService(Database db)
{
    // Canonical fleet module keys (entitlement vocabulary).
    public static class Modules
    {
        public const string CommandCenter   = "fleet.command_center";
        public const string Shipments       = "fleet.shipments";
        public const string Vehicles        = "fleet.vehicles";
        public const string Pod             = "fleet.pod";
        public const string PublicTracking  = "fleet.public_tracking";
        public const string CustomerTracking = "fleet.customer_tracking";
        public const string Carriers        = "fleet.carriers";
        public const string Booking         = "fleet.booking";
        public const string Maintenance     = "fleet.maintenance";
        public const string Fuel            = "fleet.fuel";
        public const string ColdChain       = "fleet.cold_chain";
        public const string Assets          = "fleet.assets";
        public const string Compliance      = "fleet.compliance";
        public const string Integrations    = "fleet.integrations";
        public const string Bi              = "fleet.bi";
        public const string Api             = "fleet.api";
        public const string Billing         = "fleet.billing";
    }

    public async Task EnsureAsync()
    {
        // Module-package catalog — the buyable bundles of capability. Distinct from
        // pricing `packages` (commercial tiers); a tier may include several packages.
        await db.ExecuteAsync("""
            CREATE TABLE IF NOT EXISTS module_packages (
                id           BIGINT       GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
                package_key  VARCHAR(80)  NOT NULL UNIQUE,
                name         VARCHAR(160) NOT NULL,
                description  VARCHAR(400) NULL,
                category     VARCHAR(40)  NOT NULL DEFAULT 'fleet',
                module_keys  JSONB        NOT NULL,
                is_core      BOOLEAN      NOT NULL DEFAULT false,
                base_price_cents BIGINT   NOT NULL DEFAULT 0,
                sort_order   INT          NOT NULL DEFAULT 100,
                active       BOOLEAN      NOT NULL DEFAULT true,
                created_at   TIMESTAMPTZ  NOT NULL DEFAULT NOW()
            )
            """);

        // Usage meter definitions.
        await db.ExecuteAsync("""
            CREATE TABLE IF NOT EXISTS usage_meters (
                id           BIGINT       GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
                meter_key    VARCHAR(80)  NOT NULL UNIQUE,
                name         VARCHAR(160) NOT NULL,
                unit         VARCHAR(40)  NOT NULL DEFAULT 'count',
                aggregation  VARCHAR(20)  NOT NULL DEFAULT 'sum',  -- sum | gauge
                period       VARCHAR(20)  NOT NULL DEFAULT 'monthly', -- monthly | lifetime
                module_key   VARCHAR(80)  NULL,
                active       BOOLEAN      NOT NULL DEFAULT true,
                created_at   TIMESTAMPTZ  NOT NULL DEFAULT NOW()
            )
            """);

        // Raw, append-only usage events.
        await db.ExecuteAsync("""
            CREATE TABLE IF NOT EXISTS usage_events (
                id            BIGINT       GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
                company_id    BIGINT       NOT NULL,
                meter_key     VARCHAR(80)  NOT NULL,
                quantity      NUMERIC(18,4) NOT NULL DEFAULT 1,
                reference     VARCHAR(160) NULL,
                actor         VARCHAR(160) NULL,
                period_key    VARCHAR(20)  NOT NULL,
                occurred_at   TIMESTAMPTZ  NOT NULL DEFAULT NOW()
            )
            """);
        await db.ExecuteAsync("CREATE INDEX IF NOT EXISTS idx_usage_events_company_meter ON usage_events (company_id, meter_key, period_key)");

        // Rolled-up usage counters (fast read for dashboards / invoice preview).
        await db.ExecuteAsync("""
            CREATE TABLE IF NOT EXISTS usage_counters (
                id          BIGINT        GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
                company_id  BIGINT        NOT NULL,
                meter_key   VARCHAR(80)   NOT NULL,
                period_key  VARCHAR(20)   NOT NULL,
                value       NUMERIC(18,4) NOT NULL DEFAULT 0,
                updated_at  TIMESTAMPTZ   NOT NULL DEFAULT NOW(),
                UNIQUE (company_id, meter_key, period_key)
            )
            """);

        // Pricing rules — included quantity + overage unit price per meter, optionally
        // scoped to a pricing package (NULL = applies as default).
        await db.ExecuteAsync("""
            CREATE TABLE IF NOT EXISTS pricing_rules (
                id                BIGINT       GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
                package_id        BIGINT       NULL REFERENCES packages(id) ON DELETE CASCADE,
                meter_key         VARCHAR(80)  NOT NULL,
                included_quantity NUMERIC(18,4) NOT NULL DEFAULT 0,
                unit_price_cents  BIGINT       NOT NULL DEFAULT 0,
                currency          VARCHAR(8)   NOT NULL DEFAULT 'USD',
                overage_allowed   BOOLEAN      NOT NULL DEFAULT true,
                active            BOOLEAN      NOT NULL DEFAULT true,
                created_at        TIMESTAMPTZ  NOT NULL DEFAULT NOW(),
                UNIQUE (package_id, meter_key)
            )
            """);

        // Per-tenant contract overrides — custom included quantity / unit price /
        // flat discount that supersede the package pricing rule for one tenant.
        await db.ExecuteAsync("""
            CREATE TABLE IF NOT EXISTS tenant_contract_overrides (
                id                BIGINT       GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
                company_id        BIGINT       NOT NULL REFERENCES companies(id),
                meter_key         VARCHAR(80)  NULL,
                included_quantity NUMERIC(18,4) NULL,
                unit_price_cents  BIGINT       NULL,
                flat_discount_cents BIGINT     NULL,
                note              VARCHAR(400) NULL,
                updated_by        VARCHAR(220) NULL,
                updated_at        TIMESTAMPTZ  NOT NULL DEFAULT NOW(),
                UNIQUE (company_id, meter_key)
            )
            """);

        await SeedModulePackagesAsync();
        await SeedUsageMetersAsync();
    }

    private static readonly (string Key, string Name, string Category, bool Core, int Sort, string[] Modules)[] PackageSeed =
    {
        ("core_fleet", "Core Fleet", "fleet", true, 10, new[]{ Modules.CommandCenter, Modules.Shipments, Modules.Vehicles, Modules.Pod, Modules.PublicTracking }),
        ("logistics_operations", "Logistics Operations", "fleet", false, 20, new[]{ Modules.Carriers, Modules.Booking }),
        ("customer_visibility", "Customer Visibility", "fleet", false, 30, new[]{ Modules.CustomerTracking }),
        ("compliance_market_pack", "Compliance / Market Pack", "compliance", false, 40, new[]{ Modules.Compliance }),
        ("cold_chain", "Cold Chain", "addon", false, 50, new[]{ Modules.ColdChain }),
        ("asset_tracking", "Asset Tracking", "addon", false, 60, new[]{ Modules.Assets }),
        ("fuel_intelligence", "Fuel Intelligence", "addon", false, 70, new[]{ Modules.Fuel }),
        ("driver_safety", "Driver Safety & Compliance", "addon", false, 80, new[]{ "fleet.driver_safety" }),
        ("integration_hub", "Integration Hub", "platform", false, 90, new[]{ Modules.Integrations }),
        ("bi_pnl", "BI / P&L Intelligence", "platform", false, 100, new[]{ Modules.Bi }),
        ("enterprise_api", "Enterprise API", "platform", false, 110, new[]{ Modules.Api }),
    };

    private async Task SeedModulePackagesAsync()
    {
        foreach (var p in PackageSeed)
        {
            var modulesJson = "[" + string.Join(",", p.Modules.Select(m => $"\"{m}\"")) + "]";
            await db.ExecuteAsync("""
                INSERT INTO module_packages (package_key, name, category, module_keys, is_core, sort_order)
                VALUES (@k, @n, @c, @mk::jsonb, @core, @sort)
                ON CONFLICT (package_key) DO UPDATE SET
                    name = EXCLUDED.name, category = EXCLUDED.category,
                    module_keys = EXCLUDED.module_keys, is_core = EXCLUDED.is_core,
                    sort_order = EXCLUDED.sort_order
                """,
                c =>
                {
                    c.Parameters.AddWithValue("@k", p.Key);
                    c.Parameters.AddWithValue("@n", p.Name);
                    c.Parameters.AddWithValue("@c", p.Category);
                    c.Parameters.AddWithValue("@mk", modulesJson);
                    c.Parameters.AddWithValue("@core", p.Core);
                    c.Parameters.AddWithValue("@sort", p.Sort);
                });
        }
    }

    private static readonly (string Key, string Name, string Unit, string Period, string? Module)[] MeterSeed =
    {
        ("vehicles.count", "Vehicles", "count", "lifetime", Modules.Vehicles),
        ("drivers.count", "Drivers", "count", "lifetime", null),
        ("shipments.monthly", "Shipments / month", "count", "monthly", Modules.Shipments),
        ("pod.monthly", "POD submissions / month", "count", "monthly", Modules.Pod),
        ("tracking_links.monthly", "Tracking links / month", "count", "monthly", Modules.CustomerTracking),
        ("assets.count", "Returnable assets", "count", "lifetime", Modules.Assets),
        ("temperature_devices.count", "Temperature devices", "count", "lifetime", Modules.ColdChain),
        ("temperature_readings.monthly", "Temperature readings / month", "count", "monthly", Modules.ColdChain),
        ("fuel_transactions.monthly", "Fuel transactions / month", "count", "monthly", Modules.Fuel),
        ("integrations.count", "Integrations", "count", "lifetime", Modules.Integrations),
        ("api_calls.monthly", "API calls / month", "count", "monthly", Modules.Api),
        ("users.count", "Users", "count", "lifetime", null),
        ("market_packs.enabled", "Market packs enabled", "count", "lifetime", Modules.Compliance),
    };

    private async Task SeedUsageMetersAsync()
    {
        foreach (var m in MeterSeed)
        {
            await db.ExecuteAsync("""
                INSERT INTO usage_meters (meter_key, name, unit, aggregation, period, module_key)
                VALUES (@k, @n, @u, 'sum', @p, @m)
                ON CONFLICT (meter_key) DO UPDATE SET
                    name = EXCLUDED.name, unit = EXCLUDED.unit,
                    period = EXCLUDED.period, module_key = EXCLUDED.module_key
                """,
                c =>
                {
                    c.Parameters.AddWithValue("@k", m.Key);
                    c.Parameters.AddWithValue("@n", m.Name);
                    c.Parameters.AddWithValue("@u", m.Unit);
                    c.Parameters.AddWithValue("@p", m.Period);
                    c.Parameters.AddWithValue("@m", (object?)m.Module ?? DBNull.Value);
                });
        }
    }
}
