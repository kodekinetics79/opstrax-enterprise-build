using System.Net;
using System.Reflection;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Opstrax.Api.Controllers;
using Opstrax.Api.Data;
using Opstrax.Api.Services;

namespace Opstrax.Tests;

[Collection("platform-control-plane")]
[Trait("Category", "Integration")]
public sealed class PlatformAuthHardeningTests
{
    private static Database Db() => new(new ConfigurationBuilder()
        .AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["ConnectionStrings:DefaultConnection"] = TestDb.ConnectionString,
        }).Build());

    private static string Unique() => Guid.NewGuid().ToString("N")[..10];

    private static DefaultHttpContext Http(string? bearer = null, string? ip = null, string? body = null)
    {
        var http = new DefaultHttpContext();
        http.RequestServices = new ServiceCollection()
            .AddLogging()
            .AddSingleton<Opstrax.Api.Security.IDataKeyProvider, TestKeyProvider>()
            .AddSingleton<Opstrax.Api.Security.PiiProtectionService>()
            .BuildServiceProvider();
        if (bearer is not null) http.Request.Headers.Authorization = $"Bearer {bearer}";
        if (ip is not null) http.Connection.RemoteIpAddress = IPAddress.Parse(ip);
        if (body is not null)
        {
            http.Request.Body = new MemoryStream(Encoding.UTF8.GetBytes(body));
            http.Request.ContentType = "application/json";
        }
        return http;
    }

    private static int? Status(IResult result) => (result as IStatusCodeHttpResult)?.StatusCode;

    private static async Task<(long Id, string Token, string Email)> SeedAsync(Database db, string role = "platform_super_admin")
    {
        var email = $"auth-hardening-{Unique()}@opstrax.test";
        var roleId = await db.ScalarLongAsync("SELECT id FROM platform_roles WHERE role_key=@r",
            c => c.Parameters.AddWithValue("@r", role));
        var id = await db.InsertAsync(
            @"INSERT INTO platform_admins(email,full_name,password_hash,role_id,status)
              VALUES(@e,'Auth Hardening',@h,@r,'Active') RETURNING id",
            c =>
            {
                c.Parameters.AddWithValue("@e", email);
                c.Parameters.AddWithValue("@h", PlatformSchemaService.HashPassword("Auth-Hardening-123x"));
                c.Parameters.AddWithValue("@r", roleId);
            });
        var token = Convert.ToBase64String(System.Security.Cryptography.RandomNumberGenerator.GetBytes(32));
        await db.ExecuteAsync(
            "INSERT INTO platform_sessions(admin_id,session_token,expires_at) VALUES(@id,@t,NOW()+INTERVAL '1 hour')",
            c => { c.Parameters.AddWithValue("@id", id); c.Parameters.AddWithValue("@t", token); });
        return (id, token, email);
    }

    private static async Task CleanupAsync(Database db, params (long Id, string Email)[] rows)
    {
        foreach (var row in rows)
        {
            await db.ExecuteAsync("DELETE FROM platform_sessions WHERE admin_id=@id", c => c.Parameters.AddWithValue("@id", row.Id));
            await db.ExecuteAsync("DELETE FROM platform_audit_log WHERE actor_admin_id=@id OR actor_email=@e OR entity_id=@id",
                c => { c.Parameters.AddWithValue("@id", row.Id); c.Parameters.AddWithValue("@e", row.Email); });
            await db.ExecuteAsync("DELETE FROM platform_admins WHERE id=@id", c => c.Parameters.AddWithValue("@id", row.Id));
        }
    }

    [Fact]
    public async Task Concurrent_Demotions_Leave_One_Active_Super_Admin()
    {
        var db = Db();
        await new PlatformSchemaService(db).EnsureAsync();
        var first = await SeedAsync(db);
        var second = await SeedAsync(db);
        var parked = (await db.QueryAsync(
            @"SELECT a.id FROM platform_admins a JOIN platform_roles r ON r.id=a.role_id
               WHERE r.role_key='platform_super_admin' AND a.status='Active' AND a.id NOT IN (@a,@b)",
            c => { c.Parameters.AddWithValue("@a", first.Id); c.Parameters.AddWithValue("@b", second.Id); }))
            .Select(r => Convert.ToInt64(r["id"])).ToArray();
        try
        {
            foreach (var id in parked)
                await db.ExecuteAsync("UPDATE platform_admins SET status='Parked-Test' WHERE id=@id", c => c.Parameters.AddWithValue("@id", id));

            var attempts = await Task.WhenAll(
                PlatformAdminEndpoints.AssignRole(Http(first.Token), second.Id,
                    new PlatformAdminEndpoints.AssignRoleRequest("finance_admin"), db, CancellationToken.None),
                PlatformAdminEndpoints.AssignRole(Http(second.Token), first.Id,
                    new PlatformAdminEndpoints.AssignRoleRequest("finance_admin"), db, CancellationToken.None));

            var statuses = attempts.Select(r => Status(r)!.Value).ToArray();
            Assert.Equal(1, statuses.Count(status => status == 200));
            Assert.Contains(statuses.Single(status => status != 200), new[] { 401, 409 });
            var active = await db.ScalarLongAsync(
                @"SELECT COUNT(*) FROM platform_admins a JOIN platform_roles r ON r.id=a.role_id
                   WHERE r.role_key='platform_super_admin' AND a.status='Active'");
            Assert.Equal(1, active);
        }
        finally
        {
            foreach (var id in parked)
                await db.ExecuteAsync("UPDATE platform_admins SET status='Active' WHERE id=@id", c => c.Parameters.AddWithValue("@id", id));
            await CleanupAsync(db, (first.Id, first.Email), (second.Id, second.Email));
        }
    }

    [Fact]
    public async Task Mfa_Enrollment_Expires_And_Locks_After_Five_Guesses()
    {
        var db = Db();
        await new PlatformSchemaService(db).EnsureAsync();
        var admin = await SeedAsync(db);
        try
        {
            Assert.Equal(200, Status(await PlatformAdminEndpoints.MfaEnroll(Http(admin.Token), db, CancellationToken.None)));
            for (var i = 1; i <= PlatformAdminEndpoints.MaxMfaEnrollmentAttempts; i++)
            {
                var result = await PlatformAdminEndpoints.MfaVerify(Http(admin.Token),
                    new PlatformAdminEndpoints.MfaVerifyRequest("000000"), db, CancellationToken.None);
                Assert.Equal(i == PlatformAdminEndpoints.MaxMfaEnrollmentAttempts ? 429 : 401, Status(result));
            }
            Assert.Equal(429, Status(await PlatformAdminEndpoints.MfaEnroll(Http(admin.Token), db, CancellationToken.None)));

            await db.ExecuteAsync(
                @"UPDATE platform_admins SET mfa_enrollment_locked_until=NULL,
                    mfa_enrollment_started_at=NOW()-INTERVAL '11 minutes' WHERE id=@id",
                c => c.Parameters.AddWithValue("@id", admin.Id));
            var expired = await PlatformAdminEndpoints.MfaVerify(Http(admin.Token),
                new PlatformAdminEndpoints.MfaVerifyRequest("000000"), db, CancellationToken.None);
            Assert.Equal(410, Status(expired));
            var secretCount = await db.ScalarLongAsync(
                "SELECT COUNT(*) FROM platform_admins WHERE id=@id AND mfa_secret IS NOT NULL",
                c => c.Parameters.AddWithValue("@id", admin.Id));
            Assert.Equal(0, secretCount);
        }
        finally { await CleanupAsync(db, (admin.Id, admin.Email)); }
    }

    [Fact]
    public async Task Login_Throttle_Enforces_Email_Across_Rotating_Ips_And_Ip_Across_Emails()
    {
        var db = Db();
        await new PlatformSchemaService(db).EnsureAsync();
        var email = $"unknown-{Unique()}@opstrax.test";
        var sprayPrefix = $"spray-{Unique()}";
        try
        {
            for (var i = 0; i < PlatformEndpoints.MaxFailedLogins; i++)
                Assert.Equal(401, Status(await PlatformEndpoints.PlatformLogin(Http(ip: $"203.0.113.{i + 1}"),
                    new PlatformEndpoints.PlatformLoginRequest(email, "wrong"), db, CancellationToken.None)));
            Assert.Equal(429, Status(await PlatformEndpoints.PlatformLogin(Http(ip: "198.51.100.99"),
                new PlatformEndpoints.PlatformLoginRequest(email, "wrong"), db, CancellationToken.None)));

            for (var i = 0; i < PlatformEndpoints.MaxFailedLogins; i++)
                Assert.Equal(401, Status(await PlatformEndpoints.PlatformLogin(Http(ip: "192.0.2.77"),
                    new PlatformEndpoints.PlatformLoginRequest($"{sprayPrefix}-{i}@opstrax.test", "wrong"), db, CancellationToken.None)));
            Assert.Equal(429, Status(await PlatformEndpoints.PlatformLogin(Http(ip: "192.0.2.77"),
                new PlatformEndpoints.PlatformLoginRequest($"{sprayPrefix}-last@opstrax.test", "wrong"), db, CancellationToken.None)));
        }
        finally
        {
            await db.ExecuteAsync("DELETE FROM platform_audit_log WHERE actor_email=@e OR actor_email LIKE @p",
                c => { c.Parameters.AddWithValue("@e", email); c.Parameters.AddWithValue("@p", sprayPrefix + "%"); });
        }
    }

    [Fact]
    public async Task Bulk_Admin_Authenticates_Before_Parsing_And_Rejects_Malformed_Json_After_Auth()
    {
        var db = Db();
        await new PlatformSchemaService(db).EnsureAsync();
        var admin = await SeedAsync(db);
        var method = typeof(PlatformAdminEndpoints).GetMethod("BulkAdmins", BindingFlags.NonPublic | BindingFlags.Static)!;
        try
        {
            static Task<IResult> Invoke(MethodInfo method, HttpContext http, Database db) =>
                (Task<IResult>)method.Invoke(null, [http, db, CancellationToken.None])!;

            Assert.Equal(401, Status(await Invoke(method, Http(body: "{"), db)));
            Assert.Equal(400, Status(await Invoke(method, Http(admin.Token, body: "{"), db)));
        }
        finally { await CleanupAsync(db, (admin.Id, admin.Email)); }
    }

    [Fact]
    public async Task Self_Service_Password_Uses_Invite_Password_Policy()
    {
        var db = Db();
        await new PlatformSchemaService(db).EnsureAsync();
        var admin = await SeedAsync(db);
        try
        {
            var weak = await PlatformAdminEndpoints.ChangeOwnPassword(Http(admin.Token),
                new PlatformAdminEndpoints.ChangeOwnPasswordRequest("Auth-Hardening-123x", "abcdefghijkl"), db, CancellationToken.None);
            Assert.Equal(400, Status(weak));
            Assert.True(PlatformEndpoints.VerifyPassword("Auth-Hardening-123x",
                (await db.QuerySingleAsync("SELECT password_hash FROM platform_admins WHERE id=@id",
                    c => c.Parameters.AddWithValue("@id", admin.Id)))!["passwordHash"]?.ToString()));
        }
        finally { await CleanupAsync(db, (admin.Id, admin.Email)); }
    }
}
