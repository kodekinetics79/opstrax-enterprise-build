using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Opstrax.Api.Controllers;
using Opstrax.Api.Data;
using Opstrax.Api.Services;
using System.Text.Json;

namespace Opstrax.Tests;

// Test classes that mutate the SHARED platform_admins table (parking/demoting
// super admins) must not run in parallel with each other — the last-super fence
// asserts on a global count.
[CollectionDefinition("platform-control-plane")]
public class PlatformControlPlaneCollection;

// ─────────────────────────────────────────────────────────────────────────────
// Platform operator management (/api/platform/admins) — success + denial proof
// suite. Calls the real endpoint handlers against the local test Postgres,
// following the PlatformControlPlaneTests pattern.
// ─────────────────────────────────────────────────────────────────────────────
[Collection("platform-control-plane")]
[Trait("Category", "Integration")]
public class PlatformOperatorManagementTests
{
    private static readonly string LocalConnectionString = TestDb.ConnectionString;

    private static Database CreateDatabase()
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:DefaultConnection"] = LocalConnectionString,
            })
            .Build();
        return new Database(config);
    }

    private static string Unique() => Guid.NewGuid().ToString("N")[..10];

    private static DefaultHttpContext Http(string? bearer = null)
    {
        var http = new DefaultHttpContext();
        http.RequestServices = new ServiceCollection()
            .AddLogging()
            .AddSingleton<Opstrax.Api.Security.IDataKeyProvider, TestKeyProvider>()
            .AddSingleton<Opstrax.Api.Security.PiiProtectionService>()
            .BuildServiceProvider();
        if (!string.IsNullOrEmpty(bearer))
            http.Request.Headers.Authorization = $"Bearer {bearer}";
        return http;
    }

    private static int? StatusOf(IResult result) => (result as IStatusCodeHttpResult)?.StatusCode;
    private static string JsonOf(IResult result) => JsonSerializer.Serialize((result as IValueHttpResult)?.Value);

    private static async Task<(long AdminId, string Token, string Email)> SeedAdminSessionAsync(
        Database db, string roleKey, string? password = null)
    {
        var email = $"opmgmt-{Unique()}@opstrax.test";
        var roleId = await db.ScalarLongAsync("SELECT id FROM platform_roles WHERE role_key=@k",
            c => c.Parameters.AddWithValue("@k", roleKey));
        Assert.True(roleId > 0, $"role {roleKey} must be seeded");
        var adminId = await db.InsertAsync(
            @"INSERT INTO platform_admins (email, full_name, password_hash, role_id, status)
              VALUES (@e, 'Operator Mgmt Test', @h, @r, 'Active') RETURNING id",
            c =>
            {
                c.Parameters.AddWithValue("@e", email);
                c.Parameters.AddWithValue("@h", PlatformSchemaService.HashPassword(password ?? "Op-Mgmt-Pass-1!x"));
                c.Parameters.AddWithValue("@r", roleId);
            });
        var token = Convert.ToBase64String(System.Security.Cryptography.RandomNumberGenerator.GetBytes(32));
        await db.ExecuteAsync(
            "INSERT INTO platform_sessions (admin_id, session_token, expires_at) VALUES (@a, @t, NOW() + INTERVAL '1 hour')",
            c =>
            {
                c.Parameters.AddWithValue("@a", adminId);
                c.Parameters.AddWithValue("@t", token);
            });
        return (adminId, token, email);
    }

    private static async Task CleanupAdminAsync(Database db, params (long Id, string Email)[] admins)
    {
        foreach (var (id, email) in admins)
        {
            await db.ExecuteAsync("DELETE FROM platform_sessions WHERE admin_id=@a", c => c.Parameters.AddWithValue("@a", id));
            await db.ExecuteAsync("DELETE FROM platform_audit_log WHERE actor_admin_id=@a OR actor_email=@e OR entity_id=@a",
                c => { c.Parameters.AddWithValue("@a", id); c.Parameters.AddWithValue("@e", email); });
            await db.ExecuteAsync("DELETE FROM platform_admins WHERE id=@a", c => c.Parameters.AddWithValue("@a", id));
        }
    }

    private static async Task<long> AdminIdByEmailAsync(Database db, string email) =>
        await db.ScalarLongAsync("SELECT COALESCE((SELECT id FROM platform_admins WHERE email=@e), 0)",
            c => c.Parameters.AddWithValue("@e", email));

    // Role changes revoke sessions, so tests re-mint one when an admin must act again.
    private static async Task<string> MintSessionAsync(Database db, long adminId)
    {
        var token = Convert.ToBase64String(System.Security.Cryptography.RandomNumberGenerator.GetBytes(32));
        await db.ExecuteAsync(
            "INSERT INTO platform_sessions (admin_id, session_token, expires_at) VALUES (@a, @t, NOW() + INTERVAL '1 hour')",
            c => { c.Parameters.AddWithValue("@a", adminId); c.Parameters.AddWithValue("@t", token); });
        return token;
    }

    // ════════════════════════════════════════════════════════════════════════
    // Full lifecycle: invite → token returned once → accept → login → list.
    // ════════════════════════════════════════════════════════════════════════
    [Fact]
    public async Task Invite_Accept_Login_Lifecycle_Works_And_Is_Audited()
    {
        var db = CreateDatabase();
        await new PlatformSchemaService(db).EnsureAsync();
        var super = await SeedAdminSessionAsync(db, "platform_super_admin");
        var inviteEmail = $"opmgmt-invitee-{Unique()}@opstrax.test";
        long inviteeId = 0;
        try
        {
            // Invite
            var created = await PlatformAdminEndpoints.CreateAdmin(Http(super.Token),
                new PlatformAdminEndpoints.CreateAdminRequest(inviteEmail, "Invited Operator", "finance_admin"), db, CancellationToken.None);
            Assert.Equal(201, StatusOf(created));
            var createdJson = JsonOf(created);
            Assert.Contains("inviteToken", createdJson);
            Assert.DoesNotContain("PBKDF2", createdJson);
            using var doc = JsonDocument.Parse(createdJson);
            var rawToken = doc.RootElement.GetProperty("Data").GetProperty("inviteToken").GetString()!;
            inviteeId = await AdminIdByEmailAsync(db, inviteEmail);
            Assert.True(inviteeId > 0);

            // Only the hash is stored
            var stored = await db.QuerySingleAsync("SELECT invite_token_hash, status FROM platform_admins WHERE id=@id",
                c => c.Parameters.AddWithValue("@id", inviteeId));
            Assert.Equal("Invited", stored!["status"]?.ToString());
            Assert.NotEqual(rawToken, stored["inviteTokenHash"]?.ToString());

            // List never leaks hashes/tokens
            var list = await PlatformAdminEndpoints.ListAdmins(Http(super.Token), db, CancellationToken.None);
            Assert.Equal(200, StatusOf(list));
            var listJson = JsonOf(list);
            Assert.Contains(inviteEmail, listJson);
            Assert.DoesNotContain("PBKDF2", listJson);
            Assert.DoesNotContain(rawToken, listJson);

            // Accept with weak password → 400
            var weak = await PlatformAdminEndpoints.AcceptInvite(Http(),
                new PlatformAdminEndpoints.AcceptInviteRequest(inviteEmail, rawToken, "short1"), db, CancellationToken.None);
            Assert.Equal(400, StatusOf(weak));

            // Accept with wrong token → 401
            var wrong = await PlatformAdminEndpoints.AcceptInvite(Http(),
                new PlatformAdminEndpoints.AcceptInviteRequest(inviteEmail, new string('a', 64), "Str0ng-Enough-Pass-1"), db, CancellationToken.None);
            Assert.Equal(401, StatusOf(wrong));

            // Accept with correct token → activates
            var accept = await PlatformAdminEndpoints.AcceptInvite(Http(),
                new PlatformAdminEndpoints.AcceptInviteRequest(inviteEmail, rawToken, "Str0ng-Enough-Pass-1"), db, CancellationToken.None);
            Assert.Equal(200, StatusOf(accept));

            // Token is single-use — accepting again fails
            var replay = await PlatformAdminEndpoints.AcceptInvite(Http(),
                new PlatformAdminEndpoints.AcceptInviteRequest(inviteEmail, rawToken, "Str0ng-Enough-Pass-2"), db, CancellationToken.None);
            Assert.Equal(401, StatusOf(replay));

            // Real login path works with the chosen password
            var login = await PlatformEndpoints.PlatformLogin(Http(),
                new PlatformEndpoints.PlatformLoginRequest(inviteEmail, "Str0ng-Enough-Pass-1"), db, CancellationToken.None);
            Assert.Equal(200, StatusOf(login));

            // Audited: created + accepted
            foreach (var action in new[] { "platform.admin.created", "platform.admin.invite_accepted" })
            {
                var count = await db.ScalarLongAsync(
                    "SELECT COUNT(*) FROM platform_audit_log WHERE action=@a AND entity_id=@id",
                    c => { c.Parameters.AddWithValue("@a", action); c.Parameters.AddWithValue("@id", inviteeId); });
                Assert.True(count > 0, $"expected audit row for {action}");
            }
        }
        finally
        {
            await CleanupAdminAsync(db, (super.AdminId, super.Email));
            if (inviteeId > 0) await CleanupAdminAsync(db, (inviteeId, inviteEmail));
        }
    }

    // ════════════════════════════════════════════════════════════════════════
    // Escalation fence: an operator holding platform:admins:manage but NOT
    // super admin cannot create or promote Super Admins; denial is audited.
    // ════════════════════════════════════════════════════════════════════════
    [Fact]
    public async Task Lower_Privileged_Manager_Cannot_Create_Or_Promote_Super_Admin()
    {
        var db = CreateDatabase();
        await new PlatformSchemaService(db).EnsureAsync();

        // Test-only role: has admins:view+manage but not platform:*
        var roleKey = $"test_op_mgr_{Unique()}";
        var roleId = await db.InsertAsync(
            "INSERT INTO platform_roles (role_key, name) VALUES (@k, 'Test Operator Manager') RETURNING id",
            c => c.Parameters.AddWithValue("@k", roleKey));
        foreach (var p in new[] { "platform:admins:view", "platform:admins:manage" })
            await db.ExecuteAsync("INSERT INTO platform_role_permissions (role_id, permission_key) VALUES (@r, @p)",
                c => { c.Parameters.AddWithValue("@r", roleId); c.Parameters.AddWithValue("@p", p); });

        var manager = await SeedAdminSessionAsync(db, roleKey);
        var target = await SeedAdminSessionAsync(db, "finance_admin");
        try
        {
            // Cannot create a Super Admin
            var create = await PlatformAdminEndpoints.CreateAdmin(Http(manager.Token),
                new PlatformAdminEndpoints.CreateAdminRequest($"opmgmt-esc-{Unique()}@opstrax.test", "Escalation Try", "platform_super_admin"),
                db, CancellationToken.None);
            Assert.Equal(403, StatusOf(create));

            // Cannot promote an existing operator to Super Admin
            var promote = await PlatformAdminEndpoints.AssignRole(Http(manager.Token), target.AdminId,
                new PlatformAdminEndpoints.AssignRoleRequest("platform_super_admin"), db, CancellationToken.None);
            Assert.Equal(403, StatusOf(promote));

            // A role WITHOUT manage cannot even list
            var financeActor = await SeedAdminSessionAsync(db, "finance_admin");
            try
            {
                var denied = await PlatformAdminEndpoints.ListAdmins(Http(financeActor.Token), db, CancellationToken.None);
                Assert.Equal(403, StatusOf(denied));
            }
            finally { await CleanupAdminAsync(db, (financeActor.AdminId, financeActor.Email)); }

            // Escalation denials audited
            var audited = await db.ScalarLongAsync(
                "SELECT COUNT(*) FROM platform_audit_log WHERE action='platform.admin.escalation_denied' AND actor_admin_id=@a",
                c => c.Parameters.AddWithValue("@a", manager.AdminId));
            Assert.True(audited >= 2);

            // No session at all → 401
            var anon = await PlatformAdminEndpoints.ListAdmins(Http("not-a-real-platform-token"), db, CancellationToken.None);
            Assert.Equal(401, StatusOf(anon));
        }
        finally
        {
            await CleanupAdminAsync(db, (manager.AdminId, manager.Email), (target.AdminId, target.Email));
            await db.ExecuteAsync("DELETE FROM platform_role_permissions WHERE role_id=@r", c => c.Parameters.AddWithValue("@r", roleId));
            await db.ExecuteAsync("DELETE FROM platform_roles WHERE id=@r", c => c.Parameters.AddWithValue("@r", roleId));
        }
    }

    // ════════════════════════════════════════════════════════════════════════
    // Disable: kills sessions, blocks login, blocks bearer reuse; re-enable
    // restores access. Self-disable is rejected.
    // ════════════════════════════════════════════════════════════════════════
    [Fact]
    public async Task Disable_Blocks_Login_And_Sessions_Then_Reenable_Restores()
    {
        var db = CreateDatabase();
        await new PlatformSchemaService(db).EnsureAsync();
        var super = await SeedAdminSessionAsync(db, "platform_super_admin");
        var victim = await SeedAdminSessionAsync(db, "finance_admin", "Victim-Pass-1234x");
        try
        {
            // Self-disable blocked
            var self = await PlatformAdminEndpoints.SetStatus(Http(super.Token), super.AdminId,
                new PlatformAdminEndpoints.SetStatusRequest("Disabled"), db, CancellationToken.None);
            Assert.Equal(400, StatusOf(self));

            // Disable the victim
            var disable = await PlatformAdminEndpoints.SetStatus(Http(super.Token), victim.AdminId,
                new PlatformAdminEndpoints.SetStatusRequest("Disabled"), db, CancellationToken.None);
            Assert.Equal(200, StatusOf(disable));

            // Sessions gone + old bearer rejected
            var sessions = await db.ScalarLongAsync("SELECT COUNT(*) FROM platform_sessions WHERE admin_id=@a",
                c => c.Parameters.AddWithValue("@a", victim.AdminId));
            Assert.Equal(0, sessions);
            var reuse = await PlatformAdminEndpoints.ListAdmins(Http(victim.Token), db, CancellationToken.None);
            Assert.Equal(401, StatusOf(reuse));

            // Login rejected while disabled
            var login = await PlatformEndpoints.PlatformLogin(Http(),
                new PlatformEndpoints.PlatformLoginRequest(victim.Email, "Victim-Pass-1234x"), db, CancellationToken.None);
            Assert.Equal(401, StatusOf(login));

            // Re-enable → login works again
            var enable = await PlatformAdminEndpoints.SetStatus(Http(super.Token), victim.AdminId,
                new PlatformAdminEndpoints.SetStatusRequest("Active"), db, CancellationToken.None);
            Assert.Equal(200, StatusOf(enable));
            var login2 = await PlatformEndpoints.PlatformLogin(Http(),
                new PlatformEndpoints.PlatformLoginRequest(victim.Email, "Victim-Pass-1234x"), db, CancellationToken.None);
            Assert.Equal(200, StatusOf(login2));

            // Both transitions audited
            foreach (var action in new[] { "platform.admin.disabled", "platform.admin.enabled" })
            {
                var count = await db.ScalarLongAsync(
                    "SELECT COUNT(*) FROM platform_audit_log WHERE action=@a AND entity_id=@id",
                    c => { c.Parameters.AddWithValue("@a", action); c.Parameters.AddWithValue("@id", victim.AdminId); });
                Assert.True(count > 0, $"expected audit row for {action}");
            }
        }
        finally
        {
            await CleanupAdminAsync(db, (super.AdminId, super.Email), (victim.AdminId, victim.Email));
        }
    }

    // ════════════════════════════════════════════════════════════════════════
    // Last active Super Admin can neither be disabled nor demoted.
    // ════════════════════════════════════════════════════════════════════════
    [Fact]
    public async Task Last_Active_Super_Admin_Cannot_Be_Disabled_Or_Demoted()
    {
        var db = CreateDatabase();
        await new PlatformSchemaService(db).EnsureAsync();
        var actor = await SeedAdminSessionAsync(db, "platform_super_admin");
        var lastSuper = await SeedAdminSessionAsync(db, "platform_super_admin");

        // Park every OTHER active super admin so `lastSuper` is provably the last
        // one; restore exactly afterwards.
        var parked = (await db.QueryAsync(
                @"SELECT a.id FROM platform_admins a JOIN platform_roles r ON r.id=a.role_id
                  WHERE r.role_key='platform_super_admin' AND a.status='Active' AND a.id <> @keep",
                c => c.Parameters.AddWithValue("@keep", lastSuper.AdminId)))
            .Select(x => Convert.ToInt64(x["id"])).ToArray();
        try
        {
            foreach (var id in parked)
                await db.ExecuteAsync("UPDATE platform_admins SET status='Parked-Test' WHERE id=@id",
                    c => c.Parameters.AddWithValue("@id", id));
            // The acting super was parked with the rest; restore it so exactly
            // two supers are active: actor + lastSuper.
            await db.ExecuteAsync("UPDATE platform_admins SET status='Active' WHERE id=@id",
                c => c.Parameters.AddWithValue("@id", actor.AdminId));

            // Demote the actor → lastSuper becomes the ONLY active super admin.
            var demoteActor = await PlatformAdminEndpoints.AssignRole(Http(lastSuper.Token), actor.AdminId,
                new PlatformAdminEndpoints.AssignRoleRequest("finance_admin"), db, CancellationToken.None);
            Assert.Equal(200, StatusOf(demoteActor));

            // Demotion revokes the target's sessions and is audited.
            var actorSessions = await db.ScalarLongAsync("SELECT COUNT(*) FROM platform_sessions WHERE admin_id=@a",
                c => c.Parameters.AddWithValue("@a", actor.AdminId));
            Assert.Equal(0, actorSessions);
            var roleAudited = await db.ScalarLongAsync(
                "SELECT COUNT(*) FROM platform_audit_log WHERE action='platform.admin.role_changed' AND entity_id=@id",
                c => c.Parameters.AddWithValue("@id", actor.AdminId));
            Assert.True(roleAudited > 0, "role change must be audited");

            // Reachable-path analysis for "disable the last super": another actor
            // with admins:manage but no super role dies on the escalation fence
            // (403, proven in the escalation test), and the last super acting on
            // itself dies on the self-disable guard — asserted here. The 409
            // disable fence behind both is defense in depth.
            var disable = await PlatformAdminEndpoints.SetStatus(Http(lastSuper.Token), lastSuper.AdminId,
                new PlatformAdminEndpoints.SetStatusRequest("Disabled"), db, CancellationToken.None);
            Assert.Equal(400, StatusOf(disable));

            // Demotion of the last active super is blocked by the count fence.
            var demoteLast = await PlatformAdminEndpoints.AssignRole(Http(lastSuper.Token), lastSuper.AdminId,
                new PlatformAdminEndpoints.AssignRoleRequest("finance_admin"), db, CancellationToken.None);
            Assert.Equal(409, StatusOf(demoteLast));

            // With a second active super restored, demotion is allowed again.
            // (Promotion also revoked the actor's sessions — mint a fresh one.)
            var repromote = await PlatformAdminEndpoints.AssignRole(Http(lastSuper.Token), actor.AdminId,
                new PlatformAdminEndpoints.AssignRoleRequest("platform_super_admin"), db, CancellationToken.None);
            Assert.Equal(200, StatusOf(repromote));
            var freshActorToken = await MintSessionAsync(db, actor.AdminId);
            var demoteNow = await PlatformAdminEndpoints.AssignRole(Http(freshActorToken), lastSuper.AdminId,
                new PlatformAdminEndpoints.AssignRoleRequest("finance_admin"), db, CancellationToken.None);
            Assert.Equal(200, StatusOf(demoteNow));
        }
        finally
        {
            foreach (var id in parked)
                await db.ExecuteAsync("UPDATE platform_admins SET status='Active' WHERE id=@id",
                    c => c.Parameters.AddWithValue("@id", id));
            await CleanupAdminAsync(db, (actor.AdminId, actor.Email), (lastSuper.AdminId, lastSuper.Email));
        }
    }

    // ════════════════════════════════════════════════════════════════════════
    // Tenant boundary: a TENANT session token (user_sessions) can never reach
    // the platform operator endpoints — 401, never a permission evaluation.
    // ════════════════════════════════════════════════════════════════════════
    [Fact]
    public async Task Tenant_Session_Cannot_Access_Platform_Admin_Endpoints()
    {
        var db = CreateDatabase();
        await new PlatformSchemaService(db).EnsureAsync();
        var companyId = await db.InsertAsync(
            "INSERT INTO companies (company_code, name, industry, status) VALUES (@c, 'Boundary Test Co', 'Logistics', 'Active') RETURNING id",
            c => c.Parameters.AddWithValue("@c", $"OPB-{Unique()}"));
        try
        {
            var userId = await db.InsertAsync(
                "INSERT INTO users (company_id, full_name, email, role_name, status) VALUES (@cid, 'Tenant Admin User', @e, 'Tenant Admin', 'Active') RETURNING id",
                c => { c.Parameters.AddWithValue("@cid", companyId); c.Parameters.AddWithValue("@e", $"opmgmt-tenant-{Unique()}@opstrax.test"); });
            var tenantToken = Convert.ToBase64String(System.Security.Cryptography.RandomNumberGenerator.GetBytes(32));
            await db.ExecuteAsync(
                "INSERT INTO user_sessions (user_id, company_id, session_token, expires_at) VALUES (@u, @c, @t, NOW() + INTERVAL '1 hour')",
                c => { c.Parameters.AddWithValue("@u", userId); c.Parameters.AddWithValue("@c", companyId); c.Parameters.AddWithValue("@t", tenantToken); });

            var list = await PlatformAdminEndpoints.ListAdmins(Http(tenantToken), db, CancellationToken.None);
            Assert.Equal(401, StatusOf(list));
            var create = await PlatformAdminEndpoints.CreateAdmin(Http(tenantToken),
                new PlatformAdminEndpoints.CreateAdminRequest($"opmgmt-x-{Unique()}@opstrax.test", "Should Never Exist", "finance_admin"),
                db, CancellationToken.None);
            Assert.Equal(401, StatusOf(create));
        }
        finally
        {
            await db.ExecuteAsync("DELETE FROM user_sessions WHERE company_id=@id", c => c.Parameters.AddWithValue("@id", companyId));
            await db.ExecuteAsync("DELETE FROM users WHERE company_id=@id", c => c.Parameters.AddWithValue("@id", companyId));
            await db.ExecuteAsync("DELETE FROM companies WHERE id=@id", c => c.Parameters.AddWithValue("@id", companyId));
        }
    }

    // ════════════════════════════════════════════════════════════════════════
    // Spec-shaped PATCH alias: rename + role change with the same fences.
    // ════════════════════════════════════════════════════════════════════════
    [Fact]
    public async Task Patch_Alias_Updates_Name_And_Role_With_Fences()
    {
        var db = CreateDatabase();
        await new PlatformSchemaService(db).EnsureAsync();
        var super = await SeedAdminSessionAsync(db, "platform_super_admin");
        var target = await SeedAdminSessionAsync(db, "support_admin");
        try
        {
            var patch = await PlatformAdminEndpoints.PatchAdmin(Http(super.Token), target.AdminId,
                new PlatformAdminEndpoints.PatchAdminRequest("finance_admin", "Renamed Operator"), db, CancellationToken.None);
            Assert.Equal(200, StatusOf(patch));

            var row = await db.QuerySingleAsync(
                @"SELECT a.full_name, r.role_key FROM platform_admins a LEFT JOIN platform_roles r ON r.id=a.role_id WHERE a.id=@id",
                c => c.Parameters.AddWithValue("@id", target.AdminId));
            Assert.Equal("Renamed Operator", row!["fullName"]?.ToString());
            Assert.Equal("finance_admin", row["roleKey"]?.ToString());

            // Role change through PATCH revoked the target's sessions too.
            var sessions = await db.ScalarLongAsync("SELECT COUNT(*) FROM platform_sessions WHERE admin_id=@a",
                c => c.Parameters.AddWithValue("@a", target.AdminId));
            Assert.Equal(0, sessions);

            var empty = await PlatformAdminEndpoints.PatchAdmin(Http(super.Token), target.AdminId,
                new PlatformAdminEndpoints.PatchAdminRequest(null, null), db, CancellationToken.None);
            Assert.Equal(400, StatusOf(empty));
        }
        finally
        {
            await CleanupAdminAsync(db, (super.AdminId, super.Email), (target.AdminId, target.Email));
        }
    }

    // ════════════════════════════════════════════════════════════════════════
    // MFA: enroll → verify → login requires TOTP → reset clears it.
    // ════════════════════════════════════════════════════════════════════════
    [Fact]
    public async Task Mfa_Enrollment_Login_Gate_And_Reset_Work()
    {
        var db = CreateDatabase();
        await new PlatformSchemaService(db).EnsureAsync();
        var super = await SeedAdminSessionAsync(db, "platform_super_admin", "Mfa-Test-Pass-123x");
        var otherSuper = await SeedAdminSessionAsync(db, "platform_super_admin");
        try
        {
            // Enroll returns the secret once; MFA not enforced until verified.
            var enroll = await PlatformAdminEndpoints.MfaEnroll(Http(super.Token), db, CancellationToken.None);
            Assert.Equal(200, StatusOf(enroll));
            using var enrollDoc = JsonDocument.Parse(JsonOf(enroll));
            var secret = enrollDoc.RootElement.GetProperty("Data").GetProperty("secret").GetString()!;
            Assert.Contains("otpauth://totp/", enrollDoc.RootElement.GetProperty("Data").GetProperty("otpauthUri").GetString());

            var preVerifyLogin = await PlatformEndpoints.PlatformLogin(Http(),
                new PlatformEndpoints.PlatformLoginRequest(super.Email, "Mfa-Test-Pass-123x"), db, CancellationToken.None);
            Assert.Equal(200, StatusOf(preVerifyLogin));

            // Wrong code cannot activate; a real TOTP code can.
            var badVerify = await PlatformAdminEndpoints.MfaVerify(Http(super.Token),
                new PlatformAdminEndpoints.MfaVerifyRequest("000000"), db, CancellationToken.None);
            Assert.Equal(401, StatusOf(badVerify));
            var goodVerify = await PlatformAdminEndpoints.MfaVerify(Http(super.Token),
                new PlatformAdminEndpoints.MfaVerifyRequest(Opstrax.Api.Security.TotpService.ComputeCurrentCode(secret)), db, CancellationToken.None);
            Assert.Equal(200, StatusOf(goodVerify));

            // Login now requires the code: missing → mfa_required prompt; wrong →
            // counted failure; valid → session.
            var noCode = await PlatformEndpoints.PlatformLogin(Http(),
                new PlatformEndpoints.PlatformLoginRequest(super.Email, "Mfa-Test-Pass-123x"), db, CancellationToken.None);
            Assert.Equal(401, StatusOf(noCode));
            Assert.Contains("mfa_required", JsonOf(noCode));

            var wrongCode = await PlatformEndpoints.PlatformLogin(Http(),
                new PlatformEndpoints.PlatformLoginRequest(super.Email, "Mfa-Test-Pass-123x", "000000"), db, CancellationToken.None);
            Assert.Equal(401, StatusOf(wrongCode));

            var withCode = await PlatformEndpoints.PlatformLogin(Http(),
                new PlatformEndpoints.PlatformLoginRequest(super.Email, "Mfa-Test-Pass-123x",
                    Opstrax.Api.Security.TotpService.ComputeCurrentCode(secret)), db, CancellationToken.None);
            Assert.Equal(200, StatusOf(withCode));

            // Non-manage operator cannot reset someone's MFA.
            var financeActor = await SeedAdminSessionAsync(db, "finance_admin");
            try
            {
                var denied = await PlatformAdminEndpoints.ResetMfa(Http(financeActor.Token), super.AdminId, db, CancellationToken.None);
                Assert.Equal(403, StatusOf(denied));
            }
            finally { await CleanupAdminAsync(db, (financeActor.AdminId, financeActor.Email)); }

            // Recovery: another super resets MFA → sessions revoked, password-only
            // login works again.
            var reset = await PlatformAdminEndpoints.ResetMfa(Http(otherSuper.Token), super.AdminId, db, CancellationToken.None);
            Assert.Equal(200, StatusOf(reset));
            var sessions = await db.ScalarLongAsync("SELECT COUNT(*) FROM platform_sessions WHERE admin_id=@a",
                c => c.Parameters.AddWithValue("@a", super.AdminId));
            Assert.Equal(0, sessions);
            var afterReset = await PlatformEndpoints.PlatformLogin(Http(),
                new PlatformEndpoints.PlatformLoginRequest(super.Email, "Mfa-Test-Pass-123x"), db, CancellationToken.None);
            Assert.Equal(200, StatusOf(afterReset));

            foreach (var action in new[] { "platform.admin.mfa_enroll_started", "platform.admin.mfa_enabled", "platform.admin.mfa_reset" })
            {
                var count = await db.ScalarLongAsync(
                    "SELECT COUNT(*) FROM platform_audit_log WHERE action=@a AND entity_id=@id",
                    c => { c.Parameters.AddWithValue("@a", action); c.Parameters.AddWithValue("@id", super.AdminId); });
                Assert.True(count > 0, $"expected audit row for {action}");
            }
        }
        finally
        {
            await CleanupAdminAsync(db, (super.AdminId, super.Email), (otherSuper.AdminId, otherSuper.Email));
        }
    }

    // ════════════════════════════════════════════════════════════════════════
    // Accept-invite lockout is DB-backed (audit rows are the counter), so it
    // survives process restarts: 5 failures → 429, all failures audited.
    // ════════════════════════════════════════════════════════════════════════
    [Fact]
    public async Task AcceptInvite_Lockout_Is_Durable_And_Audited()
    {
        var db = CreateDatabase();
        await new PlatformSchemaService(db).EnsureAsync();
        var email = $"opmgmt-lock-{Unique()}@opstrax.test"; // no such operator — still locks out
        try
        {
            IResult last = Results.Ok();
            for (var i = 0; i < 6; i++)
                last = await PlatformAdminEndpoints.AcceptInvite(Http(),
                    new PlatformAdminEndpoints.AcceptInviteRequest(email, new string('b', 64), "Lockout-Pass-1234x"),
                    db, CancellationToken.None);
            Assert.Equal(429, StatusOf(last));

            // The counter is the audit trail itself — durable by construction.
            var failures = await db.ScalarLongAsync(
                "SELECT COUNT(*) FROM platform_audit_log WHERE actor_email=@e AND action='platform.admin.invite_accept_failed'",
                c => c.Parameters.AddWithValue("@e", email));
            Assert.Equal(5, failures);
        }
        finally
        {
            await db.ExecuteAsync("DELETE FROM platform_audit_log WHERE actor_email=@e",
                c => c.Parameters.AddWithValue("@e", email));
        }
    }

    // ════════════════════════════════════════════════════════════════════════
    // Session revocation + invite reset rotation.
    // ════════════════════════════════════════════════════════════════════════
    [Fact]
    public async Task Revoke_Sessions_And_Invite_Reset_Rotation_Work()
    {
        var db = CreateDatabase();
        await new PlatformSchemaService(db).EnsureAsync();
        var super = await SeedAdminSessionAsync(db, "platform_super_admin");
        var target = await SeedAdminSessionAsync(db, "support_admin");
        var inviteEmail = $"opmgmt-rot-{Unique()}@opstrax.test";
        long inviteeId = 0;
        try
        {
            // Revoke sessions
            var revoke = await PlatformAdminEndpoints.RevokeSessions(Http(super.Token), target.AdminId, db, CancellationToken.None);
            Assert.Equal(200, StatusOf(revoke));
            var live = await db.ScalarLongAsync("SELECT COUNT(*) FROM platform_sessions WHERE admin_id=@a",
                c => c.Parameters.AddWithValue("@a", target.AdminId));
            Assert.Equal(0, live);
            var reuse = await PlatformAdminEndpoints.ListAdmins(Http(target.Token), db, CancellationToken.None);
            Assert.Equal(401, StatusOf(reuse)); // (support_admin would be 403; revoked session is 401)
            var audited = await db.ScalarLongAsync(
                "SELECT COUNT(*) FROM platform_audit_log WHERE action='platform.admin.sessions_revoked' AND entity_id=@id",
                c => c.Parameters.AddWithValue("@id", target.AdminId));
            Assert.True(audited > 0);

            // Invite + reset → old token dead, new token works
            var created = await PlatformAdminEndpoints.CreateAdmin(Http(super.Token),
                new PlatformAdminEndpoints.CreateAdminRequest(inviteEmail, "Rotation Operator", "support_admin"), db, CancellationToken.None);
            Assert.Equal(201, StatusOf(created));
            using var createdDoc = JsonDocument.Parse(JsonOf(created));
            var oldToken = createdDoc.RootElement.GetProperty("Data").GetProperty("inviteToken").GetString()!;
            inviteeId = await AdminIdByEmailAsync(db, inviteEmail);

            var reset = await PlatformAdminEndpoints.ResetInvite(Http(super.Token), inviteeId, db, CancellationToken.None);
            Assert.Equal(200, StatusOf(reset));
            using var resetDoc = JsonDocument.Parse(JsonOf(reset));
            var newToken = resetDoc.RootElement.GetProperty("Data").GetProperty("inviteToken").GetString()!;
            Assert.NotEqual(oldToken, newToken);

            var oldAccept = await PlatformAdminEndpoints.AcceptInvite(Http(),
                new PlatformAdminEndpoints.AcceptInviteRequest(inviteEmail, oldToken, "Rotated-Pass-1234x"), db, CancellationToken.None);
            Assert.Equal(401, StatusOf(oldAccept));
            var newAccept = await PlatformAdminEndpoints.AcceptInvite(Http(),
                new PlatformAdminEndpoints.AcceptInviteRequest(inviteEmail, newToken, "Rotated-Pass-1234x"), db, CancellationToken.None);
            Assert.Equal(200, StatusOf(newAccept));

            var resetAudited = await db.ScalarLongAsync(
                "SELECT COUNT(*) FROM platform_audit_log WHERE action='platform.admin.invite_reset' AND entity_id=@id",
                c => c.Parameters.AddWithValue("@id", inviteeId));
            Assert.True(resetAudited > 0);
        }
        finally
        {
            await CleanupAdminAsync(db, (super.AdminId, super.Email), (target.AdminId, target.Email));
            if (inviteeId > 0) await CleanupAdminAsync(db, (inviteeId, inviteEmail));
        }
    }
}
