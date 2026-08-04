using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Opstrax.Api.Controllers;
using Opstrax.Api.Data;
using Opstrax.Api.Services;

namespace Opstrax.Tests;

public sealed class PlatformImpersonationPolicyTests
{
    [Theory]
    [InlineData("GET", "/api/auth/me")]
    [InlineData("GET", "/api/incidents")]
    [InlineData("HEAD", "/api/hos/logs/42")]
    [InlineData("GET", "/api/audit/logs")]
    [InlineData("POST", "/api/auth/logout")]
    [InlineData("OPTIONS", "/api/incidents")]
    public void ExplicitSafetyReadAllowlist_AllowsOnlyBoundedRoutes(string method, string path) =>
        Assert.True(PlatformImpersonationPolicy.IsReadOnlyRequestAllowed(method, path));

    [Theory]
    [InlineData("POST", "/api/incidents")]
    [InlineData("PUT", "/api/coaching/tasks/1")]
    [InlineData("DELETE", "/api/dvir/reports/1")]
    [InlineData("GET", "/api/integrations")]
    [InlineData("GET", "/api/users")]
    [InlineData("POST", "/api/auth/refresh")]
    public void MutationAndUnreviewedGetRoutes_FailClosed(string method, string path) =>
        Assert.False(PlatformImpersonationPolicy.IsReadOnlyRequestAllowed(method, path));

    [Fact]
    public void DeploymentGate_DefaultsOff() =>
        Assert.False(PlatformImpersonationPolicy.IsEnabled(new ConfigurationBuilder().Build()));
}

public sealed class PlatformImpersonationTransportContractTests
{
    private static string Read(params string[] parts)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !Directory.Exists(Path.Combine(directory.FullName, "backend-dotnet")))
            directory = directory.Parent;
        Assert.NotNull(directory);
        return File.ReadAllText(Path.Combine(new[] { directory.FullName }.Concat(parts).ToArray()));
    }

    [Fact]
    public void TenantEdge_RevalidatesGrantDeniesBeforeHandlerAndAuditsCompletedOutcome()
    {
        var program = Read("backend-dotnet", "Program.cs");
        Assert.Contains("LEFT JOIN platform_impersonation_sessions pis ON pis.id=s.impersonation_grant_id", program, StringComparison.Ordinal);
        Assert.Contains("pis.ended_at IS NULL AND pis.expires_at > NOW()", program, StringComparison.Ordinal);
        Assert.Contains("pis.company_id=s.company_id AND pis.target_user_id=s.user_id", program, StringComparison.Ordinal);
        Assert.Contains("PlatformImpersonationPolicy.IsReadOnlyRequestAllowed", program, StringComparison.Ordinal);
        Assert.Contains("This support session cannot change tenant data", program, StringComparison.Ordinal);
        Assert.Contains("platform.impersonation.read_{outcome}", program, StringComparison.Ordinal);
        Assert.Contains("responseStatus", program, StringComparison.Ordinal);
        Assert.Contains("return count < 120", program, StringComparison.Ordinal);

        var deniedIndex = program.IndexOf("This support session cannot change tenant data", StringComparison.Ordinal);
        var tenantScopeIndex = program.IndexOf("BeginTenantScopeAsync(companyId", deniedIndex, StringComparison.Ordinal);
        Assert.True(deniedIndex >= 0 && tenantScopeIndex > deniedIndex,
            "Read-only denial must execute before the tenant handler transaction opens.");
    }

    [Fact]
    public void SessionAndUiExposePseudonymousReadOnlySupportState()
    {
        var auth = Read("backend-dotnet", "Controllers", "EndpointMappings.cs");
        var shell = Read("frontend", "src", "layouts", "AppShell.tsx");
        var settings = Read("backend-dotnet", "appsettings.json");
        Assert.Contains("supportAccess = hasSupportAccess", auth, StringComparison.Ordinal);
        Assert.Contains("support-access-banner", shell, StringComparison.Ordinal);
        Assert.Contains("Read-only Platform support session", shell, StringComparison.Ordinal);
        Assert.Contains("\"PlatformImpersonation\"", settings, StringComparison.Ordinal);
        Assert.Contains("\"Enabled\": false", settings, StringComparison.Ordinal);
    }
}

[Collection("platform-control-plane")]
[Trait("Category", "Integration")]
public sealed class PlatformImpersonationPostgresTests
{
    [Fact]
    public async Task Grant_IsDefaultOffUniquelyBoundDualAuditedAndExactlyRevoked()
    {
        var db = Db();
        await new PlatformSchemaService(db).EnsureAsync();
        Assert.Equal(0, await db.ScalarLongAsync(
            @"SELECT COUNT(*) FROM platform_role_permissions rp JOIN platform_roles r ON r.id=rp.role_id
              WHERE r.role_key='support_admin' AND rp.permission_key='platform:impersonation:start'"));
        var suffix = Guid.NewGuid().ToString("N")[..10];
        var companyId = await db.InsertAsync(
            "INSERT INTO companies(company_code,name,industry,status) VALUES (@code,@name,'Logistics','Active') RETURNING id",
            c => { c.Parameters.AddWithValue("@code", $"IMP-{suffix}"); c.Parameters.AddWithValue("@name", $"Support Access {suffix}"); });
        var userId = await db.InsertAsync(
            "INSERT INTO users(company_id,full_name,email,role_name,status) VALUES (@c,'Support Target',@email,'Safety Manager','Active') RETURNING id",
            c => { c.Parameters.AddWithValue("@c", companyId); c.Parameters.AddWithValue("@email", $"support-target-{suffix}@opstrax.test"); });
        var admin = await SeedSuperAdminAsync(db, suffix);
        var ordinaryToken = Convert.ToBase64String(System.Security.Cryptography.RandomNumberGenerator.GetBytes(32));
        await db.ExecuteAsync(
            "INSERT INTO user_sessions(user_id,company_id,session_token,expires_at) VALUES (@u,@c,@t,NOW()+INTERVAL '1 hour')",
            c => { c.Parameters.AddWithValue("@u", userId); c.Parameters.AddWithValue("@c", companyId); c.Parameters.AddWithValue("@t", ordinaryToken); });

        try
        {
            var body = JsonSerializer.SerializeToElement(new
            {
                targetUserId = userId,
                reason = "Investigate synthetic Safety pilot incident",
                minutes = 15,
            });
            var disabled = await PlatformEndpoints.TenantImpersonate(companyId, Http(admin.Token), body,
                db, Config(enabled: false), CancellationToken.None);
            Assert.Equal(StatusCodes.Status503ServiceUnavailable, Status(disabled));
            Assert.Equal(0, await GrantCount(db, companyId));

            var issued = await PlatformEndpoints.TenantImpersonate(companyId, Http(admin.Token), body,
                db, Config(enabled: true), CancellationToken.None);
            Assert.Equal(StatusCodes.Status200OK, Status(issued));

            var grant = await db.QuerySingleAsync(
                @"SELECT p.id,p.grant_ref,p.target_user_id,p.reason,p.ended_at,s.id session_id
                  FROM platform_impersonation_sessions p
                  JOIN user_sessions s ON s.impersonation_grant_id=p.id
                  WHERE p.company_id=@c",
                c => c.Parameters.AddWithValue("@c", companyId));
            Assert.NotNull(grant);
            var grantId = Convert.ToInt64(grant!["id"]);
            Assert.Equal(userId, Convert.ToInt64(grant["targetUserId"]));
            Assert.NotNull(grant["grantRef"]);
            Assert.Null(grant["endedAt"]);

            Assert.Equal(1, await db.ScalarLongAsync(
                "SELECT COUNT(*) FROM platform_audit_log WHERE target_company_id=@c AND action='platform.impersonation.started'",
                c => c.Parameters.AddWithValue("@c", companyId)));
            var tenantStart = await db.QuerySingleAsync(
                "SELECT actor_user_id,actor_name,details_json FROM audit_logs WHERE company_id=@c AND action_name='platform.support_access.started'",
                c => c.Parameters.AddWithValue("@c", companyId));
            Assert.NotNull(tenantStart);
            Assert.True(tenantStart!["actorUserId"] is null or DBNull);
            Assert.StartsWith("platform-support:", tenantStart["actorName"]?.ToString(), StringComparison.Ordinal);
            Assert.DoesNotContain(admin.Email, tenantStart["detailsJson"]?.ToString() ?? "", StringComparison.OrdinalIgnoreCase);

            // Database trigger rejects cross-user binding; unique index rejects a
            // second bearer for the same grant.
            var otherUserId = await db.InsertAsync(
                "INSERT INTO users(company_id,full_name,email,role_name,status) VALUES (@c,'Other User',@email,'Safety Auditor','Active') RETURNING id",
                c => { c.Parameters.AddWithValue("@c", companyId); c.Parameters.AddWithValue("@email", $"other-{suffix}@opstrax.test"); });
            await Assert.ThrowsAnyAsync<Exception>(() => db.ExecuteAsync(
                @"INSERT INTO user_sessions(user_id,company_id,session_token,expires_at,impersonation_grant_id)
                  VALUES (@u,@c,@t,NOW()+INTERVAL '10 minutes',@g)",
                c => { c.Parameters.AddWithValue("@u", otherUserId); c.Parameters.AddWithValue("@c", companyId); c.Parameters.AddWithValue("@t", $"mismatch-{suffix}"); c.Parameters.AddWithValue("@g", grantId); }));
            await Assert.ThrowsAnyAsync<Exception>(() => db.ExecuteAsync(
                @"INSERT INTO user_sessions(user_id,company_id,session_token,expires_at,impersonation_grant_id)
                  VALUES (@u,@c,@t,NOW()+INTERVAL '10 minutes',@g)",
                c => { c.Parameters.AddWithValue("@u", userId); c.Parameters.AddWithValue("@c", companyId); c.Parameters.AddWithValue("@t", $"duplicate-{suffix}"); c.Parameters.AddWithValue("@g", grantId); }));

            var ended = await PlatformEndpoints.ImpersonationEnd(grantId, Http(admin.Token), db, CancellationToken.None);
            Assert.Equal(StatusCodes.Status200OK, Status(ended));
            Assert.Equal(0, await db.ScalarLongAsync(
                "SELECT COUNT(*) FROM user_sessions WHERE impersonation_grant_id=@g",
                c => c.Parameters.AddWithValue("@g", grantId)));
            Assert.Equal(1, await db.ScalarLongAsync(
                "SELECT COUNT(*) FROM user_sessions WHERE session_token=@t",
                c => c.Parameters.AddWithValue("@t", ordinaryToken)));
            Assert.Equal(1, await db.ScalarLongAsync(
                "SELECT COUNT(*) FROM audit_logs WHERE company_id=@c AND action_name='platform.support_access.ended'",
                c => c.Parameters.AddWithValue("@c", companyId)));
        }
        finally
        {
            await db.ExecuteAsync("DELETE FROM user_sessions WHERE company_id=@c", c => c.Parameters.AddWithValue("@c", companyId));
            await db.ExecuteAsync("DELETE FROM audit_logs WHERE company_id=@c", c => c.Parameters.AddWithValue("@c", companyId));
            await db.ExecuteAsync("DELETE FROM platform_audit_log WHERE target_company_id=@c OR actor_admin_id=@a", c => { c.Parameters.AddWithValue("@c", companyId); c.Parameters.AddWithValue("@a", admin.Id); });
            await db.ExecuteAsync("DELETE FROM platform_impersonation_sessions WHERE company_id=@c", c => c.Parameters.AddWithValue("@c", companyId));
            await db.ExecuteAsync("DELETE FROM users WHERE company_id=@c", c => c.Parameters.AddWithValue("@c", companyId));
            await db.ExecuteAsync("DELETE FROM platform_sessions WHERE admin_id=@a", c => c.Parameters.AddWithValue("@a", admin.Id));
            await db.ExecuteAsync("DELETE FROM platform_admins WHERE id=@a", c => c.Parameters.AddWithValue("@a", admin.Id));
            await db.ExecuteAsync("DELETE FROM companies WHERE id=@c", c => c.Parameters.AddWithValue("@c", companyId));
        }
    }

    private static Database Db() => new(new ConfigurationBuilder().AddInMemoryCollection(
        new Dictionary<string, string?> { ["ConnectionStrings:DefaultConnection"] = TestDb.ConnectionString }).Build());
    private static IConfiguration Config(bool enabled) => new ConfigurationBuilder().AddInMemoryCollection(
        new Dictionary<string, string?> { ["PlatformImpersonation:Enabled"] = enabled.ToString() }).Build();
    private static DefaultHttpContext Http(string token)
    {
        var http = new DefaultHttpContext();
        http.Request.Headers.Authorization = $"Bearer {token}";
        http.Connection.RemoteIpAddress = System.Net.IPAddress.Loopback;
        return http;
    }
    private static int Status(IResult result) => (result as IStatusCodeHttpResult)?.StatusCode ?? StatusCodes.Status200OK;
    private static Task<long> GrantCount(Database db, long companyId) => db.ScalarLongAsync(
        "SELECT COUNT(*) FROM platform_impersonation_sessions WHERE company_id=@c", c => c.Parameters.AddWithValue("@c", companyId));
    private static async Task<(long Id, string Token, string Email)> SeedSuperAdminAsync(Database db, string suffix)
    {
        var roleId = await db.ScalarLongAsync("SELECT id FROM platform_roles WHERE role_key='platform_super_admin'");
        var email = $"support-control-{suffix}@opstrax.test";
        var id = await db.InsertAsync(
            "INSERT INTO platform_admins(email,full_name,password_hash,role_id,status) VALUES (@email,'Support Control Test',@hash,@role,'Active') RETURNING id",
            c => { c.Parameters.AddWithValue("@email", email); c.Parameters.AddWithValue("@hash", PlatformSchemaService.HashPassword("Support-Test-1!")); c.Parameters.AddWithValue("@role", roleId); });
        var token = Convert.ToBase64String(System.Security.Cryptography.RandomNumberGenerator.GetBytes(32));
        await db.ExecuteAsync("INSERT INTO platform_sessions(admin_id,session_token,expires_at) VALUES (@a,@t,NOW()+INTERVAL '1 hour')",
            c => { c.Parameters.AddWithValue("@a", id); c.Parameters.AddWithValue("@t", token); });
        return (id, token, email);
    }
}
