namespace Opstrax.Tests;

// Single source of truth for the integration-test database connection.
// Reads OPSTRAX_TEST_DB when set (local Docker PG, a Neon SIT branch, or CI's Postgres service),
// falling back to the historical local default so an existing dev box keeps working untouched.
// NEVER point this at a production database — the *Postgres/Integration suites write + seed data.
public static class TestDb
{
    public static string ConnectionString =>
        Environment.GetEnvironmentVariable("OPSTRAX_TEST_DB")
        ?? "Host=127.0.0.1;Port=5433;Database=opstrax_local;Username=zayra;Password=zayra";

    // The restricted, NON-superuser opstrax_app role — the only way to actually exercise RLS
    // (the owner bypasses it). Reads OPSTRAX_TEST_DB_APP, else derives from OPSTRAX_TEST_DB by
    // swapping in the app role, else the historical local default.
    public static string AppConnectionString
    {
        get
        {
            var explicitApp = Environment.GetEnvironmentVariable("OPSTRAX_TEST_DB_APP");
            if (!string.IsNullOrWhiteSpace(explicitApp)) return explicitApp;
            var owner = Environment.GetEnvironmentVariable("OPSTRAX_TEST_DB");
            if (!string.IsNullOrWhiteSpace(owner))
                return System.Text.RegularExpressions.Regex.Replace(
                    System.Text.RegularExpressions.Regex.Replace(owner, "Username=[^;]*", "Username=opstrax_app"),
                    "Password=[^;]*", "Password=opstrax_app_local");
            return "Host=127.0.0.1;Port=5433;Database=opstrax_local;Username=opstrax_app;Password=opstrax_app_local";
        }
    }
}
