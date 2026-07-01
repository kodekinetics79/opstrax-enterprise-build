using Microsoft.Extensions.Configuration;
using Opstrax.Api.Data;
using Xunit;

namespace Opstrax.Tests;

// Validates that Stage-19 RLS policies + Stage-20 FORCE / restricted role +
// Database.BeginTenantScopeAsync (set_config(..., is_local:=true)) deliver ACTIVE,
// concurrency-safe tenant isolation with no cross-request pool leakage.
//
// Runs the assertions as the NON-superuser `opstrax_app` role (the only way to
// observe enforcement — the owner/superuser `zayra` bypasses RLS). Cross-tenant
// seed/cleanup is done as the owner, which is legitimate admin access.
//
// Named *PostgresTests so it is excluded by the CI filter (FullyQualifiedName!~Postgres),
// like the other DB-backed integration tests; it runs locally against :5433.
public class RlsTenantIsolationPostgresTests
{
    private const string OwnerConnectionString =
        "Host=127.0.0.1;Port=5433;Database=opstrax_local;Username=zayra;Password=zayra";

    // Restricted app role created by 2026_06_30_stage20_rls_force_and_app_role.sql.
    // Local-only test password (set out-of-band via ALTER ROLE; not a production secret).
    private const string AppConnectionString =
        "Host=127.0.0.1;Port=5433;Database=opstrax_local;Username=opstrax_app;Password=opstrax_app_local";

    private const long TenantA = 910001;
    private const long TenantB = 910002;
    private const string MarkerA = "RLS-MARK-A";
    private const string MarkerB = "RLS-MARK-B";

    private static Database Db(string conn, TenantScopeAccessor? accessor = null)
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["ConnectionStrings:DefaultConnection"] = conn })
            .Build();
        return new Database(config, accessor);
    }

    [Fact]
    public async Task Concurrent_two_tenants_sharing_the_pool_have_zero_cross_tenant_visibility()
    {
        var owner = Db(OwnerConnectionString); // superuser bypasses RLS → seeds both tenants
        await owner.ExecuteAsync(
            @"INSERT INTO dvir_reports (company_id, report_number, driver_id, vehicle_id, inspection_type, inspection_status)
              VALUES (@a, @ma, 0, 0, 'Pre-Trip', 'Submitted'),
                     (@b, @mb, 0, 0, 'Pre-Trip', 'Submitted')",
            c =>
            {
                c.Parameters.AddWithValue("@a", TenantA);
                c.Parameters.AddWithValue("@b", TenantB);
                c.Parameters.AddWithValue("@ma", MarkerA);
                c.Parameters.AddWithValue("@mb", MarkerB);
            });

        try
        {
            var accessor = new TenantScopeAccessor();
            var app = Db(AppConnectionString, accessor);

            async Task RunAsTenant(long companyId, string ownMarker, string foreignMarker)
            {
                await using var scope = await app.BeginTenantScopeAsync(companyId);
                accessor.Current = scope; // set in this task's own frame → flows to the awaited query
                try
                {
                    var rows = await app.QueryAsync(
                        "SELECT report_number FROM dvir_reports WHERE report_number IN (@ma, @mb)",
                        c => { c.Parameters.AddWithValue("@ma", MarkerA); c.Parameters.AddWithValue("@mb", MarkerB); });
                    var visible = rows.Select(r => r["reportNumber"]?.ToString()).ToHashSet();

                    Assert.Contains(ownMarker, visible);          // sees its own tenant's row
                    Assert.DoesNotContain(foreignMarker, visible); // NEVER sees the other tenant's row
                    Assert.Single(visible);
                }
                finally
                {
                    accessor.Current = null;
                    await scope.CompleteAsync();
                }
            }

            // Heavy interleaving forces the same pooled physical connections to be
            // reused across tenant A and tenant B requests — the leakage stress case.
            var tasks = new List<Task>();
            for (var i = 0; i < 25; i++)
            {
                tasks.Add(Task.Run(() => RunAsTenant(TenantA, MarkerA, MarkerB)));
                tasks.Add(Task.Run(() => RunAsTenant(TenantB, MarkerB, MarkerA)));
            }
            await Task.WhenAll(tasks);

            // Platform-admin bypass (separate GUC / separate policy) sees BOTH tenants.
            await using (var sys = await app.BeginSystemScopeAsync())
            {
                accessor.Current = sys;
                try
                {
                    var rows = await app.QueryAsync(
                        "SELECT report_number FROM dvir_reports WHERE report_number IN (@ma, @mb)",
                        c => { c.Parameters.AddWithValue("@ma", MarkerA); c.Parameters.AddWithValue("@mb", MarkerB); });
                    var visible = rows.Select(r => r["reportNumber"]?.ToString()).ToHashSet();
                    Assert.Contains(MarkerA, visible);
                    Assert.Contains(MarkerB, visible);
                }
                finally { accessor.Current = null; await sys.CompleteAsync(); }
            }

            // No ambient scope on the restricted role → fail-closed (RLS returns nothing).
            // This also proves the SET LOCAL GUC did not leak onto a reused pooled connection.
            var noContext = await app.QueryAsync(
                "SELECT report_number FROM dvir_reports WHERE report_number IN (@ma, @mb)",
                c => { c.Parameters.AddWithValue("@ma", MarkerA); c.Parameters.AddWithValue("@mb", MarkerB); });
            Assert.Empty(noContext);
        }
        finally
        {
            await owner.ExecuteAsync(
                "DELETE FROM dvir_reports WHERE report_number IN (@ma, @mb)",
                c => { c.Parameters.AddWithValue("@ma", MarkerA); c.Parameters.AddWithValue("@mb", MarkerB); });
        }
    }
}
