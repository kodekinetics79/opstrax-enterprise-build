using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Npgsql;
using Opstrax.Api.Controllers;
using Opstrax.Api.Data;
using Opstrax.Api.Services;

namespace Opstrax.Tests;

[Collection("tenant-admin-password-reset")]
[Trait("Category", "Integration")]
public sealed class AdminPasswordResetPostgresTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task AdminResetIsTenantScopedPolicyBoundAndRevokesCredentialPathsAtomically(bool restrictedRuntime)
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
        var auditTrigger = $"password_reset_audit_failure_{suffix}";
        var auditTriggerInstalled = false;
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

            var runtime = restrictedRuntime ? CreateRestrictedDatabase() : db;
            var audit = new AuditService(runtime);
            var settings = new SecuritySettingsService(runtime, audit);
            async Task<IResult> Reset(long target, string password, bool mayManage = true)
            {
                async Task<IResult> Invoke()
                {
                    if (restrictedRuntime)
                    {
                        var identity = await runtime.QuerySingleAsync(
                            "SELECT current_user AS role,opstrax_security.current_tenant_id() AS tenant,opstrax_security.current_user_id() AS principal,has_table_privilege(current_user,'password_reset_tokens','DELETE') AS can_delete_tokens");
                        Assert.Equal("opstrax_app", identity!["role"]);
                        Assert.Equal(companyId, Convert.ToInt64(identity["tenant"]));
                        Assert.Equal(adminId, Convert.ToInt64(identity["principal"]));
                        Assert.False(Convert.ToBoolean(identity["canDeleteTokens"]));
                    }
                    var principal = Principal(adminId, companyId);
                    if (!mayManage) principal.Items[EndpointMappings.AuthPermissionsItemKey] = Array.Empty<string>();
                    return await EndpointMappings.AdminUserResetPassword(
                        principal, target,
                        new Dictionary<string, object?> { ["newPassword"] = password },
                        runtime, settings, audit, CancellationToken.None);
                }
                return restrictedRuntime
                    ? await runtime.RunInTenantScopeAsync(companyId, adminId, Invoke)
                    : await Invoke();
            }
            var invalid = await Reset(targetId, "short");
            Assert.Equal(StatusCodes.Status400BadRequest, Status(invalid));
            Assert.Equal(1, await Count(db, "user_sessions", targetId));

            var selfReset = await Reset(adminId, "Ready-Pass-2!");
            Assert.Equal(StatusCodes.Status409Conflict, Status(selfReset));

            var crossTenant = await Reset(foreignId, "Ready-Pass-2!");
            Assert.Equal(StatusCodes.Status404NotFound, Status(crossTenant));

            var denied = await Reset(targetId, "Ready-Pass-2!", mayManage: false);
            Assert.Equal(StatusCodes.Status403Forbidden, Status(denied));
            Assert.Equal(1, await Count(db, "user_sessions", targetId));
            Assert.Equal(1, await Count(db, "password_reset_tokens", targetId));

            await db.ExecuteAsync("UPDATE users SET status='Inactive' WHERE id=@id",
                command => command.Parameters.AddWithValue("@id", targetId));
            Assert.Equal(StatusCodes.Status409Conflict, Status(await Reset(targetId, "Ready-Pass-2!")));
            await db.ExecuteAsync("UPDATE users SET status='Active' WHERE id=@id",
                command => command.Parameters.AddWithValue("@id", targetId));

            if (restrictedRuntime)
            {
                // Fail the final audit write for this uniquely-seeded fixture only.
                // The earlier password/session/token mutations must all roll back.
                await db.ExecuteAsync($"""
                    CREATE FUNCTION {auditTrigger}() RETURNS trigger LANGUAGE plpgsql AS $audit$
                    BEGIN
                      IF NEW.company_id={companyId} AND NEW.entity_id={targetId}
                         AND NEW.action_name='user.password.reset_by_admin' THEN
                        RAISE EXCEPTION 'isolated reset audit failure' USING ERRCODE='23514';
                      END IF;
                      RETURN NEW;
                    END
                    $audit$;
                    CREATE TRIGGER {auditTrigger} BEFORE INSERT ON audit_logs
                    FOR EACH ROW EXECUTE FUNCTION {auditTrigger}();
                    """);
                auditTriggerInstalled = true;
                var failure = await Assert.ThrowsAsync<PostgresException>(() => Reset(targetId, "Ready-Pass-2!"));
                Assert.Equal(PostgresErrorCodes.CheckViolation, failure.SqlState);
                var unchanged = await db.QuerySingleAsync(
                    "SELECT password_hash,failed_login_attempts,locked_until FROM users WHERE id=@id",
                    command => command.Parameters.AddWithValue("@id", targetId));
                Assert.Equal("PBKDF2$1$old$old", unchanged!["passwordHash"]);
                Assert.Equal(5, Convert.ToInt32(unchanged["failedLoginAttempts"]));
                Assert.NotNull(unchanged["lockedUntil"]);
                Assert.Equal(1, await Count(db, "user_sessions", targetId));
                Assert.Equal(1, await Count(db, "password_reset_tokens", targetId));
                await db.ExecuteAsync($"DROP TRIGGER {auditTrigger} ON audit_logs; DROP FUNCTION {auditTrigger}();");
                auditTriggerInstalled = false;
            }

            var result = await Reset(targetId, "Ready-Pass-2!");
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
            if (auditTriggerInstalled)
                await db.ExecuteAsync($"DROP TRIGGER IF EXISTS {auditTrigger} ON audit_logs; DROP FUNCTION IF EXISTS {auditTrigger}();");
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

    private static Database CreateRestrictedDatabase() => new(new ConfigurationBuilder()
        .AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["ASPNETCORE_ENVIRONMENT"] = "Staging",
            ["Rls:EnforceTenantContext"] = "true",
            ["ConnectionStrings:DefaultConnection"] = TestDb.AppConnectionString,
            ["ConnectionStrings:SystemConnection"] = TestDb.SystemConnectionString,
        }).Build());

    private static async Task<long> SeedUser(Database db, long companyId, string email, string role, string status) =>
        await db.InsertAsync(
            @"INSERT INTO users (company_id,full_name,email,role_name,status,password_hash,demo_password,permissions_json)
              VALUES (@cid,'Reset Test User',@email,@role,@status,'PBKDF2$1$old$old','legacy',CASE WHEN @role='Company Admin' THEN '[""users:manage"",""users:view"",""users:update""]'::jsonb ELSE '[]'::jsonb END) RETURNING id",
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
