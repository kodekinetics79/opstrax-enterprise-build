using Microsoft.Extensions.Configuration;
using Opstrax.Api.Data;

namespace Opstrax.Tests;

public sealed class DualIdentityRuntimeContractTests
{
    private static readonly string Root = FindRoot();

    [Fact]
    public void TenantScope_UsesPidAndTransactionBoundDatabaseTicket_InRequiredOrder()
    {
        var source = Read("backend-dotnet/Data/Database.cs");
        var methodStart = source.IndexOf("public async Task<TenantScope> BeginTenantScopeAsync", StringComparison.Ordinal);
        var methodEnd = source.IndexOf("public async Task<TenantScope> BeginSystemScopeAsync", methodStart, StringComparison.Ordinal);
        Assert.True(methodStart >= 0 && methodEnd > methodStart);
        var tenantScope = source[methodStart..methodEnd];
        AssertOrdered(tenantScope,
            "BeginTransactionAsync",
            "SELECT pg_backend_pid(), txid_current()::bigint",
            "OpenSystemAsync",
            "IssueTenantTicketAsync(",
            "set_config('app.tenant_ticket', @ticket, true)");
        Assert.Contains("opstrax_security.issue_tenant_ticket(@tenant_id,@backend_pid,@txid,@ttl_seconds)", source, StringComparison.Ordinal);
        Assert.Contains("if (!RlsEnforced)", source, StringComparison.Ordinal);
        Assert.DoesNotContain("set_config('app.platform_admin'", source, StringComparison.Ordinal);
    }

    [Fact]
    public void SystemAndOffboardingScopes_UseSeparatePool_WithoutBypassGuc()
    {
        var database = Read("backend-dotnet/Data/Database.cs");
        var offboarding = Read("backend-dotnet/Services/TenantOffboardingService.cs");
        var program = Read("backend-dotnet/Program.cs");

        Assert.Contains("var connection = await OpenSystemAsync(ct);", database, StringComparison.Ordinal);
        Assert.Contains("await db.OpenSystemAsync(ct)", offboarding, StringComparison.Ordinal);
        Assert.DoesNotContain("set_config('app.platform_admin'", database + offboarding + program, StringComparison.Ordinal);
        Assert.Contains("finally { _scopes.Current = priorScope; }", database, StringComparison.Ordinal);
    }

    [Fact]
    public void ProductionIdentityProof_IsExactAndChecksFunctionPrivileges()
    {
        var source = Read("backend-dotnet/Data/Database.cs");
        foreach (var required in new[]
        {
            "opstrax_app", "opstrax_system", "rolcanlogin", "rolsuper", "rolbypassrls",
            "rolcreatedb", "rolcreaterole", "rolinherit", "rolreplication", "pg_auth_members",
            "m.member=r.oid", "m.roleid=r.oid",
            "pg_database", "pg_namespace", "pg_class", "pg_proc", "pg_type",
            "'CONNECT'", "'CREATE'", "'TEMPORARY'", "'public','USAGE'", "'public','CREATE'",
            "opstrax_security.issue_tenant_ticket(bigint,integer,bigint,integer)",
            "opstrax_security.current_tenant_id()"
        })
            Assert.Contains(required, source, StringComparison.Ordinal);
    }

    [Fact]
    public void DeploymentManifests_HaveNoOwnerFallbackForRuntime()
    {
        var compose = Read("docker-compose.yml");
        var render = Read("render.yaml");
        var example = Read(".env.example");

        Assert.Contains("PG_CONNECTION_APP must use opstrax_app", compose, StringComparison.Ordinal);
        Assert.Contains("PG_CONNECTION_SYSTEM must use opstrax_system", compose, StringComparison.Ordinal);
        Assert.DoesNotContain("PG_CONNECTION_APP:-${PG_CONNECTION}", compose, StringComparison.Ordinal);
        Assert.Contains("key: PG_CONNECTION_APP", render, StringComparison.Ordinal);
        Assert.Contains("key: PG_CONNECTION_SYSTEM", render, StringComparison.Ordinal);
        Assert.Contains("Username=opstrax_app", example, StringComparison.Ordinal);
        Assert.Contains("Username=opstrax_system", example, StringComparison.Ordinal);
    }

    [Fact]
    public void RlsEnabledDatabase_RejectsOutOfRangeTicketTtlBeforeOpeningConnection()
    {
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["ConnectionStrings:DefaultConnection"] = "Host=localhost;Database=x;Username=opstrax_app",
            ["ConnectionStrings:SystemConnection"] = "Host=localhost;Database=x;Username=opstrax_system",
            ["Rls:EnforceTenantContext"] = "true",
            ["Rls:TenantTicketTtlSeconds"] = "301",
        }).Build();

        var ex = Assert.Throws<InvalidOperationException>(() => new Database(config));
        Assert.Contains("between 5 and 300", ex.Message, StringComparison.Ordinal);
    }

    private static string Read(string relative) => File.ReadAllText(Path.Combine(Root, relative));

    private static void AssertOrdered(string source, params string[] values)
    {
        var previous = -1;
        foreach (var value in values)
        {
            var current = source.IndexOf(value, previous + 1, StringComparison.Ordinal);
            Assert.True(current > previous, $"Expected '{value}' after prior contract step.");
            previous = current;
        }
    }

    private static string FindRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !Directory.Exists(Path.Combine(dir.FullName, "backend-dotnet")))
            dir = dir.Parent;
        return dir?.FullName ?? throw new DirectoryNotFoundException("Repository root not found.");
    }
}
