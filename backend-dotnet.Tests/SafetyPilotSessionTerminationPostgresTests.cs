using System.Reflection;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Opstrax.Api.Controllers;
using Opstrax.Api.Data;
using Opstrax.Api.Services;

namespace Opstrax.Tests;

[Trait("Category", "Integration")]
public sealed class SafetyPilotSessionTerminationPostgresTests
{
    [Fact]
    public async Task LogoutRevokesOnlyThePresentedSessionAndWritesOneTenantAudit()
    {
        var db = new Database(new ConfigurationBuilder().AddInMemoryCollection(
            new Dictionary<string, string?>
            {
                ["ConnectionStrings:DefaultConnection"] = TestDb.ConnectionString,
                ["Rls:EnforceTenantContext"] = "false",
            }).Build());
        var suffix = Guid.NewGuid().ToString("N");
        var companyId = await db.InsertAsync(
            "INSERT INTO companies(company_code,name,industry,status) VALUES(@code,@name,'Logistics','Active') RETURNING id",
            c => { c.Parameters.AddWithValue("@code", $"LOGOUT-{suffix[..12]}"); c.Parameters.AddWithValue("@name", $"Logout contract {suffix[..8]}"); });
        long userId = 0;
        var currentToken = $"current-{suffix}";
        var otherToken = $"other-{suffix}";
        try
        {
            userId = await db.InsertAsync(
                "INSERT INTO users(company_id,full_name,email,role_name,status) VALUES(@c,'Logout User',@email,'Safety Manager','Active') RETURNING id",
                c => { c.Parameters.AddWithValue("@c", companyId); c.Parameters.AddWithValue("@email", $"logout-{suffix}@example.invalid"); });
            foreach (var token in new[] { currentToken, otherToken })
            {
                await db.ExecuteAsync(
                    "INSERT INTO user_sessions(user_id,company_id,session_token,expires_at) VALUES(@u,@c,@token,NOW()+INTERVAL '1 hour')",
                    c => { c.Parameters.AddWithValue("@u", userId); c.Parameters.AddWithValue("@c", companyId); c.Parameters.AddWithValue("@token", token); });
            }

            var http = new DefaultHttpContext();
            http.Request.Headers.Authorization = $"Bearer {currentToken}";
            http.Items[EndpointMappings.AuthCompanyIdItemKey] = companyId;
            http.Items[EndpointMappings.AuthUserIdItemKey] = userId;
            http.Items[EndpointMappings.AuthRoleItemKey] = "Safety Manager";
            var result = await Invoke("AuthLogout", http, db, new AuditService(db), CancellationToken.None);

            Assert.Equal(StatusCodes.Status200OK, Assert.IsAssignableFrom<IStatusCodeHttpResult>(result).StatusCode);
            Assert.Equal(0, await db.ScalarLongAsync(
                "SELECT COUNT(*) FROM user_sessions WHERE company_id=@c AND session_token=@token",
                c => { c.Parameters.AddWithValue("@c", companyId); c.Parameters.AddWithValue("@token", currentToken); }));
            Assert.Equal(1, await db.ScalarLongAsync(
                "SELECT COUNT(*) FROM user_sessions WHERE company_id=@c AND session_token=@token",
                c => { c.Parameters.AddWithValue("@c", companyId); c.Parameters.AddWithValue("@token", otherToken); }));
            Assert.Equal(1, await db.ScalarLongAsync(
                "SELECT COUNT(*) FROM audit_logs WHERE company_id=@c AND actor_user_id=@u AND action_name='user.logout' AND entity_name='User' AND entity_id=@u",
                c => { c.Parameters.AddWithValue("@c", companyId); c.Parameters.AddWithValue("@u", userId); }));
        }
        finally
        {
            await db.ExecuteAsync("DELETE FROM audit_logs WHERE company_id=@c", c => c.Parameters.AddWithValue("@c", companyId));
            await db.ExecuteAsync("DELETE FROM user_sessions WHERE company_id=@c", c => c.Parameters.AddWithValue("@c", companyId));
            if (userId > 0) await db.ExecuteAsync("DELETE FROM users WHERE id=@u", c => c.Parameters.AddWithValue("@u", userId));
            await db.ExecuteAsync("DELETE FROM companies WHERE id=@c", c => c.Parameters.AddWithValue("@c", companyId));
        }
    }

    private static async Task<IResult> Invoke(string method, params object[] args)
    {
        var target = typeof(EndpointMappings).GetMethod(method, BindingFlags.NonPublic | BindingFlags.Static)!;
        try { return await (Task<IResult>)target.Invoke(null, args)!; }
        catch (TargetInvocationException ex) when (ex.InnerException is not null)
        {
            System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(ex.InnerException).Throw();
            throw;
        }
    }
}
