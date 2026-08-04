namespace Opstrax.Api.Services;

// Gate for demo/synthetic seed statements embedded in startup schema services.
//
// Stricter than FleetTmsSeeder's gate (which defaults ON in Development): the
// Batch1-3 SeedStatements MUTATE EXISTING TENANT ROWS CROSS-TENANT (synthetic
// risk scores, invented revenue/cost estimates, fabricated vendor names) rather
// than seeding their own isolated demo companies. That must never happen
// implicitly against a real database — so this requires an EXPLICIT opt-in
// (ENABLE_LEGACY_BATCH_DEMO_SEED / LegacyBatchDemoSeed:Enabled = true) and has
// no environment-based default. This is intentionally separate from
// ENABLE_FLEET_DEMO_SEED: enabling the tenant-scoped Fleet TMS demo must never
// activate old cross-tenant batch mutations. Schema DDL is unaffected.
public static class DemoSeedGate
{
    public static bool IsExplicitlyEnabled(IConfiguration? configuration)
    {
        var raw = Environment.GetEnvironmentVariable("ENABLE_LEGACY_BATCH_DEMO_SEED")
                  ?? configuration?["LegacyBatchDemoSeed:Enabled"]
                  ?? configuration?["ENABLE_LEGACY_BATCH_DEMO_SEED"];
        return !string.IsNullOrWhiteSpace(raw) && bool.TryParse(raw.Trim(), out var value) && value;
    }
}
