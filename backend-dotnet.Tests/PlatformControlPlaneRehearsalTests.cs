using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Opstrax.Api.Controllers;
using Opstrax.Api.Data;
using Opstrax.Api.Services;

namespace Opstrax.Tests;

[Collection("platform-control-plane")]
public sealed class PlatformControlPlaneRehearsalTests
{
    [Fact]
    [Trait("Category", "Integration")]
    public async Task DisposableTenant_PackageOverrideDenyAuditAndRestoration_Rehearsal()
    {
        var db = Db();
        await new PlatformSchemaService(db).EnsureAsync();
        var suffix = Guid.NewGuid().ToString("N")[..10];
        long companyId = 0, adminId = 0, safetyPackageId = 0, maintenancePackageId = 0;
        string adminEmail = $"control-rehearsal-{suffix}@opstrax.test";
        try
        {
            companyId = await db.InsertAsync(
                "INSERT INTO companies(company_code,name,industry,status,entitlement_policy_mode) VALUES (@code,@name,'Logistics','Active','package_allowlist') RETURNING id",
                c => { c.Parameters.AddWithValue("@code", $"CTRL-{suffix}"); c.Parameters.AddWithValue("@name", $"Disposable control rehearsal {suffix}"); });
            safetyPackageId = await Package(db, $"rehearsal-safety-{suffix}", "[\"safety\"]");
            maintenancePackageId = await Package(db, $"rehearsal-maint-{suffix}", "[\"maintenance\"]");
            var admin = await Admin(db, adminEmail);
            adminId = admin.AdminId;
            var http = Http(admin.Token);
            var evaluator = new EntitlementService(db);

            // Baseline allowlist: omitted modules fail closed before any package.
            Assert.False((await evaluator.CheckModuleAsync(companyId, "safety")).Allowed);
            Assert.False((await evaluator.CheckModuleAsync(companyId, "maintenance")).Allowed);

            Assert.Equal(200, Status(await PlatformEndpoints.TenantAssignPackage(companyId, http,
                new() { ["packageId"] = safetyPackageId, ["seatLimit"] = 7 }, db, CancellationToken.None)));
            Assert.True((await evaluator.CheckModuleAsync(companyId, "safety")).Allowed);
            Assert.False((await evaluator.CheckModuleAsync(companyId, "maintenance")).Allowed);

            Assert.Equal(200, Status(await PlatformEndpoints.EntitlementsSet(companyId, http,
                new() { ["moduleKey"] = "integrations", ["enabled"] = true, ["tier"] = "pilot_override" }, db, CancellationToken.None)));
            Assert.True((await evaluator.CheckModuleAsync(companyId, "integrations")).Allowed);

            // Package transition removes package-derived Safety, enables Maintenance,
            // and preserves the explicit Integrations override.
            Assert.Equal(200, Status(await PlatformEndpoints.TenantAssignPackage(companyId, http,
                new() { ["packageId"] = maintenancePackageId }, db, CancellationToken.None)));
            Assert.False((await evaluator.CheckModuleAsync(companyId, "safety")).Allowed);
            Assert.True((await evaluator.CheckModuleAsync(companyId, "maintenance")).Allowed);
            Assert.True((await evaluator.CheckModuleAsync(companyId, "integrations")).Allowed);
            Assert.Equal("override", (await db.QuerySingleAsync(
                "SELECT source FROM tenant_entitlements WHERE company_id=@c AND module_key='integrations'",
                c => c.Parameters.AddWithValue("@c", companyId)))?["source"]?.ToString());

            // Restore the original commercial package, then explicitly withdraw the
            // temporary override. This proves a reversible operator sequence before
            // the disposable tenant itself is removed.
            Assert.Equal(200, Status(await PlatformEndpoints.TenantAssignPackage(companyId, http,
                new() { ["packageId"] = safetyPackageId, ["seatLimit"] = 7 }, db, CancellationToken.None)));
            Assert.Equal(200, Status(await PlatformEndpoints.EntitlementsSet(companyId, http,
                new() { ["moduleKey"] = "integrations", ["enabled"] = false, ["tier"] = "standard" }, db, CancellationToken.None)));
            Assert.True((await evaluator.CheckModuleAsync(companyId, "safety")).Allowed);
            Assert.False((await evaluator.CheckModuleAsync(companyId, "maintenance")).Allowed);
            Assert.False((await evaluator.CheckModuleAsync(companyId, "integrations")).Allowed);

            var audit = await db.QueryAsync(
                "SELECT action,actor_admin_id,target_company_id,details_json FROM platform_audit_log WHERE target_company_id=@c ORDER BY id",
                c => c.Parameters.AddWithValue("@c", companyId));
            Assert.Equal(5, audit.Count(row => row["action"]?.ToString() is "tenant.package.assigned" or "entitlement.enabled" or "entitlement.disabled"));
            Assert.Equal(3, audit.Count(row => row["action"]?.ToString() == "tenant.package.assigned"));
            Assert.Contains(audit, row => row["action"]?.ToString() == "entitlement.enabled");
            Assert.Contains(audit, row => row["action"]?.ToString() == "entitlement.disabled");
            Assert.All(audit, row => Assert.Equal(adminId, Convert.ToInt64(row["actorAdminId"])));
        }
        finally
        {
            if (companyId > 0)
            {
                foreach (var sql in new[]
                {
                    "DELETE FROM platform_audit_log WHERE target_company_id=@id",
                    "DELETE FROM tenant_entitlements WHERE company_id=@id",
                    "DELETE FROM tenant_subscriptions WHERE company_id=@id",
                    "DELETE FROM companies WHERE id=@id",
                }) await db.ExecuteAsync(sql, c => c.Parameters.AddWithValue("@id", companyId));
            }
            if (adminId > 0)
            {
                await db.ExecuteAsync("DELETE FROM platform_sessions WHERE admin_id=@id", c => c.Parameters.AddWithValue("@id", adminId));
                await db.ExecuteAsync("DELETE FROM platform_audit_log WHERE actor_admin_id=@id", c => c.Parameters.AddWithValue("@id", adminId));
                await db.ExecuteAsync("DELETE FROM platform_admins WHERE id=@id", c => c.Parameters.AddWithValue("@id", adminId));
            }
            foreach (var id in new[] { safetyPackageId, maintenancePackageId }.Where(id => id > 0))
                await db.ExecuteAsync("DELETE FROM packages WHERE id=@id", c => c.Parameters.AddWithValue("@id", id));
        }

        Assert.Equal(0, await db.ScalarLongAsync("SELECT COUNT(*) FROM companies WHERE id=@id", c => c.Parameters.AddWithValue("@id", companyId)));
        Assert.Equal(0, await db.ScalarLongAsync("SELECT COUNT(*) FROM tenant_entitlements WHERE company_id=@id", c => c.Parameters.AddWithValue("@id", companyId)));
        Assert.Equal(0, await db.ScalarLongAsync("SELECT COUNT(*) FROM tenant_subscriptions WHERE company_id=@id", c => c.Parameters.AddWithValue("@id", companyId)));
    }

    [Fact]
    public void NavDeepLinkAndApiDenial_AreBoundToTheSameSessionPolicy()
    {
        var root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../"));
        var shell = File.ReadAllText(Path.Combine(root, "frontend/src/layouts/AppShell.tsx"));
        var program = File.ReadAllText(Path.Combine(root, "backend-dotnet/Program.cs"));
        var auth = File.ReadAllText(Path.Combine(root, "backend-dotnet/Controllers/EndpointMappings.cs"));

        Assert.Contains("&& moduleAllowedByEntitlement(module, session)", shell, StringComparison.Ordinal);
        Assert.Contains("Not included in your plan", shell, StringComparison.Ordinal);
        Assert.Contains("session?.entitlementPolicyMode !== \"package_allowlist\"", shell, StringComparison.Ordinal);
        Assert.Contains("var moduleKey = ModuleKeyForPath(path);", program, StringComparison.Ordinal);
        Assert.Contains("Module disabled", program, StringComparison.Ordinal);
        Assert.Contains("package_allowlist' AND COALESCE(e.enabled,false)=false", program, StringComparison.Ordinal);
        Assert.Contains("ResolveAuthEntitlementsAsync", auth, StringComparison.Ordinal);
    }

    private static Database Db() => new(new ConfigurationBuilder().AddInMemoryCollection(
        new Dictionary<string, string?> { ["ConnectionStrings:DefaultConnection"] = TestDb.ConnectionString }).Build());
    private static int Status(IResult result) => (result as IStatusCodeHttpResult)?.StatusCode ?? 200;
    private static DefaultHttpContext Http(string token)
    {
        var http = new DefaultHttpContext();
        http.Request.Headers.Authorization = $"Bearer {token}";
        http.Connection.RemoteIpAddress = System.Net.IPAddress.Loopback;
        return http;
    }
    private static Task<long> Package(Database db, string code, string modules) => db.InsertAsync(
        "INSERT INTO packages(package_code,name,module_keys,active) VALUES (@code,@code,@modules::jsonb,true) RETURNING id",
        c => { c.Parameters.AddWithValue("@code", code); c.Parameters.AddWithValue("@modules", modules); });
    private static async Task<(long AdminId, string Token)> Admin(Database db, string email)
    {
        var roleId = await db.ScalarLongAsync("SELECT id FROM platform_roles WHERE role_key='platform_super_admin'");
        var id = await db.InsertAsync(
            "INSERT INTO platform_admins(email,full_name,password_hash,role_id,status) VALUES (@email,'Control Rehearsal',@hash,@role,'Active') RETURNING id",
            c => { c.Parameters.AddWithValue("@email", email); c.Parameters.AddWithValue("@hash", PlatformSchemaService.HashPassword("Control-Rehearsal-1!")); c.Parameters.AddWithValue("@role", roleId); });
        var token = Convert.ToBase64String(System.Security.Cryptography.RandomNumberGenerator.GetBytes(32));
        await db.ExecuteAsync("INSERT INTO platform_sessions(admin_id,session_token,expires_at) VALUES (@id,@token,NOW()+INTERVAL '1 hour')",
            c => { c.Parameters.AddWithValue("@id", id); c.Parameters.AddWithValue("@token", token); });
        return (id, token);
    }
}
