namespace Opstrax.Api.Services;

// Gate for demo/synthetic seed statements embedded in startup schema services.
//
// Stricter than FleetTmsSeeder's gate (which defaults ON in Development): the
// Batch1-3 SeedStatements MUTATE EXISTING TENANT ROWS CROSS-TENANT (synthetic
// risk scores, invented revenue/cost estimates, fabricated vendor names) rather
// than seeding their own isolated demo companies. That must never happen
// implicitly against a real database — so this requires an EXPLICIT opt-in
// (ENABLE_FLEET_DEMO_SEED / Fleet:EnableDemoSeed = true) and has no
// environment-based default. Schema DDL (tables/columns/indexes) is unaffected.
public static class DemoSeedGate
{
    public static bool IsExplicitlyEnabled(IConfiguration? configuration)
    {
        var raw = Environment.GetEnvironmentVariable("ENABLE_FLEET_DEMO_SEED")
                  ?? configuration?["Fleet:EnableDemoSeed"]
                  ?? configuration?["ENABLE_FLEET_DEMO_SEED"];
        return !string.IsNullOrWhiteSpace(raw) && bool.TryParse(raw.Trim(), out var value) && value;
    }
}
