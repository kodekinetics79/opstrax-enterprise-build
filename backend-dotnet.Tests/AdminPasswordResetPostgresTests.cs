using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Opstrax.Api.Controllers;
using Opstrax.Api.Data;
using Opstrax.Api.Services;

namespace Opstrax.Tests;

[Collection("tenant-admin-password-reset")]
[Trait("Category", "Integration")]
public sealed class AdminPasswordResetPostgresTests
{
    [Fact]
    public async Task AdminResetIsTenantScopedPolicyBoundAndRevokesCredentialPathsAtomically()
    {
        var db = CreateDatabase();
        await new SecuritySchemaService(db).EnsureAsync();
        var suffix = Guid.NewGuid().ToString("N")[..12];
        var companyId = await db.InsertAsync(
            "INSERT INTO companies (company_code,name,industry,status) VALUES (@code,@name,'Logistics','Active') RETURNING id",
            command =>
            {
                command.Parameters.AddWithValue("@code", $"PW-{suffix}");
                command.Parameters.AddWithValue("@name", $"Password Reset {suffix}");
            });
        var foreignCompanyId = await db.InsertAsync(
            "INSERT INTO companies (company_code,name,industry,status) VALUES (@code,@name,'Logistics','Active') RETURNING id",
            command =>
            {
                command.Parameters.AddWithValue("@code", $"PX-{suffix}");
                command.Parameters.AddWithValue("@name", $"Foreign Reset {suffix}");
            });

        long adminId = 0;
        long targetId = 0;
        long foreignId = 0;
        try
        {
            adminId = await SeedUser(db, companyId, $"admin-{suffix}@opstrax.test", "Company Admin", "Active");
            targetId = await SeedUser(db, companyId, $"target-{suffix}@opstrax.test", "Dispatcher", "Active");
            foreignId = await SeedUser(db, foreignCompanyId, $"foreign-{suffix}@opstrax.test", "Dispatcher", "Active");
            await db.ExecuteAsync(
                "UPDATE users SET failed_login_attempts=5,locked_until=NOW()+INTERVAL '20 minutes' WHERE id=@id",
                command => command.Parameters.AddWithValue("@id", targetId));
            await db.ExecuteAsync(
                "INSERT INTO user_sessions (user_id,company_id,session_token,expires_at) VALUES (@id,@cid,@token,NOW()+INTERVAL '1 hour')",
                command =>
                {
                    command.Parameters.AddWithValue("@id", targetId);
                    command.Parameters.AddWithValue("@cid", companyId);
                    command.Parameters.AddWithValue("@token", $"reset-session-{suffix}");
                });
            await db.ExecuteAsync(
                "INSERT INTO password_reset_tokens (user_id,company_id,token_hash,expires_at) VALUES (@id,@cid,@hash,NOW()+INTERVAL '30 minutes')",
                command =>
                {
                    command.Parameters.AddWithValue("@id", targetId);
                    command.Parameters.AddWithValue("@cid", companyId);
                    command.Parameters.AddWithValue("@hash", new string('a', 52) + suffix);
                });

            var audit = new AuditService(db);
            var settings = new SecuritySettingsService(db, audit);
            var invalid = await EndpointMappings.AdminUserResetPassword(
                Principal(adminId, companyId), targetId,
                new Dictionary<string, object?> { ["newPassword"] = "short" },
                db, settings, audit, CancellationToken.None);
            Assert.Equal(StatusCodes.Status400BadRequest, Status(invalid));
            Assert.Equal(1, await Count(db, "user_sessions", targetId));

            var crossTenant = await EndpointMappings.AdminUserResetPassword(
                Principal(adminId, companyId), foreignId,
                new Dictionary<string, object?> { ["newPassword"] = "Ready-Pass-2!" },
                db, settings, audit, CancellationToken.None);
            Assert.Equal(StatusCodes.Status404NotFound, Status(crossTenant));

            var result = await EndpointMappings.AdminUserResetPassword(
                Principal(adminId, companyId), targetId,
                new Dictionary<string, object?> { ["newPassword"] = "Ready-Pass-2!" },
                db, settings, audit, CancellationToken.None);
            Assert.Equal(StatusCodes.Status200OK, Status(result));

            var target = await db.QuerySingleAsync(
                "SELECT password_hash,demo_password,password_changed_at,failed_login_attempts,locked_until FROM users WHERE id=@id",
                command => command.Parameters.AddWithValue("@id", targetId));
            Assert.NotNull(target);
            Assert.StartsWith("PBKDF2$100000$", target!["passwordHash"]?.ToString(), StringComparison.Ordinal);
            Assert.Equal(string.Empty, target["demoPassword"]?.ToString());
            Assert.NotNull(target["passwordChangedAt"]);
            Assert.Equal(0, Convert.ToInt32(target["failedLoginAttempts"]));
            Assert.True(target["lockedUntil"] is null or DBNull);
            Assert.Equal(0, await Count(db, "user_sessions", targetId));
            Assert.Equal(0, await Count(db, "password_reset_tokens", targetId));
            Assert.Equal(1, await db.ScalarLongAsync(
                "SELECT COUNT(*) FROM audit_logs WHERE company_id=@cid AND entity_id=@id AND action_name='user.password.reset_by_admin'",
                command =>
                {
                    command.Parameters.AddWithValue("@cid", companyId);
                    command.Parameters.AddWithValue("@id", targetId);
                }));
        }
        finally
        {
            await db.ExecuteAsync("DELETE FROM audit_logs WHERE company_id IN (@own,@foreign)", command =>
            {
                command.Parameters.AddWithValue("@own", companyId);
                command.Parameters.AddWithValue("@foreign", foreignCompanyId);
            });
            await db.ExecuteAsync("DELETE FROM password_reset_tokens WHERE company_id IN (@own,@foreign)", command =>
            {
                command.Parameters.AddWithValue("@own", companyId);
                command.Parameters.AddWithValue("@foreign", foreignCompanyId);
            });
            await db.ExecuteAsync("DELETE FROM user_sessions WHERE company_id IN (@own,@foreign)", command =>
            {
                command.Parameters.AddWithValue("@own", companyId);
                command.Parameters.AddWithValue("@foreign", foreignCompanyId);
            });
            await db.ExecuteAsync("DELETE FROM users WHERE company_id IN (@own,@foreign)", command =>
            {
                command.Parameters.AddWithValue("@own", companyId);
                command.Parameters.AddWithValue("@foreign", foreignCompanyId);
            });
            await db.ExecuteAsync("DELETE FROM company_security_settings WHERE company_id IN (@own,@foreign)", command =>
            {
                command.Parameters.AddWithValue("@own", companyId);
                command.Parameters.AddWithValue("@foreign", foreignCompanyId);
            });
            await db.ExecuteAsync("DELETE FROM companies WHERE id IN (@own,@foreign)", command =>
            {
                command.Parameters.AddWithValue("@own", companyId);
                command.Parameters.AddWithValue("@foreign", foreignCompanyId);
            });
        }
    }

    private static Database CreateDatabase() => new(new ConfigurationBuilder()
        .AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["ConnectionStrings:DefaultConnection"] = TestDb.ConnectionString,
        }).Build());

    private static async Task<long> SeedUser(Database db, long companyId, string email, string role, string status) =>
        await db.InsertAsync(
            @"INSERT INTO users (company_id,full_name,email,role_name,status,password_hash,demo_password)
              VALUES (@cid,'Reset Test User',@email,@role,@status,'PBKDF2$1$old$old','legacy') RETURNING id",
            command =>
            {
                command.Parameters.AddWithValue("@cid", companyId);
                command.Parameters.AddWithValue("@email", email);
                command.Parameters.AddWithValue("@role", role);
                command.Parameters.AddWithValue("@status", status);
            });

    private static DefaultHttpContext Principal(long userId, long companyId)
    {
        var http = new DefaultHttpContext();
        http.Items[EndpointMappings.AuthUserIdItemKey] = userId;
        http.Items[EndpointMappings.AuthCompanyIdItemKey] = companyId;
        http.Items[EndpointMappings.AuthRoleItemKey] = "Company Admin";
        http.Items[EndpointMappings.AuthPermissionsItemKey] = new[] { "users:manage" };
        return http;
    }

    private static int? Status(IResult result) => (result as IStatusCodeHttpResult)?.StatusCode;

    private static Task<long> Count(Database db, string table, long userId) => db.ScalarLongAsync(
        $"SELECT COUNT(*) FROM {table} WHERE user_id=@id",
        command => command.Parameters.AddWithValue("@id", userId));
}
