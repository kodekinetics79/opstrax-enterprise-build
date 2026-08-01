using Microsoft.Extensions.Configuration;
using Npgsql;
using Opstrax.Api.Controllers;
using Opstrax.Api.Data;

namespace Opstrax.Tests;

// Runs after the owner migration runner has applied terminal Stage58 and provisioned
// independent local/SIT passwords for both runtime identities.
public sealed class DualIdentityAuthBootstrapPostgresTests
{
    [Fact]
    public async Task ProductionIdentityProof_ValidatesExactRolesAndCrossPoolTicketBridge()
    {
        var app = CreateDatabase(TestDb.AppConnectionString, rls: true, SystemConnectionString());
        await app.ValidateProductionIdentitiesAsync();
    }

    [Fact]
    public async Task AuthBootstrap_SystemScope_IncludesNormalizedRolePermissions_AfterStage58()
    {
        var owner = CreateDatabase(TestDb.ConnectionString, rls: false);
        var app = CreateDatabase(TestDb.AppConnectionString, rls: true, SystemConnectionString());
        var suffix = Guid.NewGuid().ToString("N");
        var companyCode = $"TKT-{suffix[..16]}".ToUpperInvariant();
        var roleName = $"Ticket Role {suffix[..8]}";
        var normalizedPermission = $"stage58:normalized:{suffix}";

        var companyId = await owner.InsertAsync(
            "INSERT INTO companies(company_code,name,industry,status) VALUES(@code,@name,'Logistics','Active') RETURNING id",
            c => { c.Parameters.AddWithValue("@code", companyCode); c.Parameters.AddWithValue("@name", roleName); });
        long roleId = 0;

        try
        {
            roleId = await owner.InsertAsync(
                "INSERT INTO roles(company_id,name,permissions_json,is_system) VALUES(@cid,@name,'[]'::jsonb,false) RETURNING id",
                c => { c.Parameters.AddWithValue("@cid", companyId); c.Parameters.AddWithValue("@name", roleName); });
            await owner.InsertAsync(
                "INSERT INTO role_permissions(role_id,permission_key) VALUES(@rid,@permission) RETURNING id",
                c => { c.Parameters.AddWithValue("@rid", roleId); c.Parameters.AddWithValue("@permission", normalizedPermission); });

            var resolved = await app.RunInSystemScopeAsync(() =>
                EndpointMappings.ResolveEffectivePermissionsAsync(
                    roleId, roleName, "[]", null, app, CancellationToken.None));
            Assert.Contains(normalizedPermission, resolved);

            // The app lane independently proves the ticket can see only this role.
            var visible = await app.RunInTenantScopeAsync(companyId, () => app.QueryAsync(
                "SELECT permission_key FROM role_permissions WHERE role_id=@id",
                c => c.Parameters.AddWithValue("@id", roleId)));
            Assert.Equal(normalizedPermission, Assert.Single(visible)["permissionKey"]);
        }
        finally
        {
            if (roleId > 0)
            {
                await owner.ExecuteAsync("DELETE FROM role_permissions WHERE role_id=@id",
                    c => c.Parameters.AddWithValue("@id", roleId));
                await owner.ExecuteAsync("DELETE FROM roles WHERE id=@id",
                    c => c.Parameters.AddWithValue("@id", roleId));
            }
            await owner.ExecuteAsync("DELETE FROM companies WHERE id=@id",
                c => c.Parameters.AddWithValue("@id", companyId));
        }
    }

    [Fact]
    public async Task TenantScope_RenewsFiveSecondTicket_AndRemainsAuthorizedAfterExpiryWindow()
    {
        var owner = CreateDatabase(TestDb.ConnectionString, rls: false);
        var accessor = new TenantScopeAccessor();
        var app = CreateDatabase(
            TestDb.AppConnectionString, rls: true, SystemConnectionString(), accessor, ticketTtlSeconds: 5);
        var suffix = Guid.NewGuid().ToString("N");
        var companyId = await owner.InsertAsync(
            "INSERT INTO companies(company_code,name,industry,status) VALUES(@code,@name,'Logistics','Active') RETURNING id",
            c =>
            {
                c.Parameters.AddWithValue("@code", $"TTL-{suffix[..16]}".ToUpperInvariant());
                c.Parameters.AddWithValue("@name", $"Ticket renewal {suffix[..8]}");
            });
        long roleId = 0;

        try
        {
            roleId = await owner.InsertAsync(
                "INSERT INTO roles(company_id,name,permissions_json,is_system) VALUES(@cid,@name,'[]'::jsonb,false) RETURNING id",
                c =>
                {
                    c.Parameters.AddWithValue("@cid", companyId);
                    c.Parameters.AddWithValue("@name", $"TTL marker {suffix[..8]}");
                });

            await using (var scope = await app.BeginTenantScopeAsync(companyId))
            {
                accessor.Current = scope;
                try
                {
                    Assert.Equal(1, await app.ScalarLongAsync(
                        "SELECT COUNT(*) FROM roles WHERE id=@id AND company_id=@cid",
                        c =>
                        {
                            c.Parameters.AddWithValue("@id", roleId);
                            c.Parameters.AddWithValue("@cid", companyId);
                        }));

                    // Exceed the database-enforced minimum TTL. The next statement must
                    // renew the same scope binding before RLS evaluates it.
                    await Task.Delay(TimeSpan.FromSeconds(6));

                    Assert.Equal(1, await app.ScalarLongAsync(
                        "SELECT COUNT(*) FROM roles WHERE id=@id AND company_id=@cid",
                        c =>
                        {
                            c.Parameters.AddWithValue("@id", roleId);
                            c.Parameters.AddWithValue("@cid", companyId);
                        }));
                    await scope.CompleteAsync();
                }
                finally
                {
                    accessor.Current = null;
                }
            }
        }
        finally
        {
            if (roleId > 0)
                await owner.ExecuteAsync("DELETE FROM roles WHERE id=@id", c => c.Parameters.AddWithValue("@id", roleId));
            await owner.ExecuteAsync("DELETE FROM companies WHERE id=@id", c => c.Parameters.AddWithValue("@id", companyId));
        }
    }

    private static Database CreateDatabase(
        string appConnection,
        bool rls,
        string? systemConnection = null,
        TenantScopeAccessor? accessor = null,
        int ticketTtlSeconds = 120)
    {
        var values = new Dictionary<string, string?>
        {
            ["ConnectionStrings:DefaultConnection"] = appConnection,
            ["ConnectionStrings:SystemConnection"] = systemConnection,
            ["Rls:EnforceTenantContext"] = rls.ToString(),
            ["Rls:TenantTicketTtlSeconds"] = ticketTtlSeconds.ToString(),
        };
        return new Database(
            new ConfigurationBuilder().AddInMemoryCollection(values).Build(),
            accessor ?? new TenantScopeAccessor());
    }

    private static string SystemConnectionString()
    {
        var explicitSystem = Environment.GetEnvironmentVariable("OPSTRAX_TEST_DB_SYSTEM");
        if (!string.IsNullOrWhiteSpace(explicitSystem)) return explicitSystem;
        var builder = new NpgsqlConnectionStringBuilder(TestDb.ConnectionString)
        {
            Username = "opstrax_system",
            Password = "opstrax_system_local",
        };
        return builder.ConnectionString;
    }
}
