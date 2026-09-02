using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Opstrax.Api.Controllers;
using Opstrax.Api.Data;
using Opstrax.Api.Services;

namespace Opstrax.Tests;

// ─────────────────────────────────────────────────────────────────────────────
// Platform-side tenant user administration — the 360 edit surface.
//
// What must hold when a platform operator edits somebody else's users:
//   • A sign-in email can be corrected, but never onto an address owned by
//     another tenant (that is account takeover by typo) or by a sibling user.
//   • Changing the email, the role, or disabling the account kills that user's
//     live sessions — the old identity must not keep working.
//   • The tenant's last active administrator cannot be demoted or disabled;
//     a tenant with nobody able to administer it is a support incident.
//   • Every mutation lands in the platform audit log.
// ─────────────────────────────────────────────────────────────────────────────
[Collection("platform-control-plane")]
[Trait("Category", "Integration")]
public sealed class PlatformTenantUserAdministrationPostgresTests
{
    private static Database Db()
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:DefaultConnection"] = TestDb.ConnectionString,
            })
            .Build();
        return new Database(config);
    }

    private static string Unique() => Guid.NewGuid().ToString("N")[..10];

    private static DefaultHttpContext Http(string bearer)
    {
        var http = new DefaultHttpContext();
        http.Request.Headers.Authorization = $"Bearer {bearer}";
        return http;
    }

    private static int? Status(IResult r) => (r as IStatusCodeHttpResult)?.StatusCode;

    private static async Task<string> SeedPlatformAdminAsync(Database db, string roleKey, string email)
    {
        var roleId = await db.ScalarLongAsync("SELECT id FROM platform_roles WHERE role_key=@k",
            c => c.Parameters.AddWithValue("@k", roleKey));
        var adminId = await db.InsertAsync(
            @"INSERT INTO platform_admins (email, full_name, password_hash, role_id, status)
              VALUES (@e, 'User Admin Test', @h, @r, 'Active') RETURNING id",
            c =>
            {
                c.Parameters.AddWithValue("@e", email);
                c.Parameters.AddWithValue("@h", PlatformSchemaService.HashPassword("Test-Password-123!"));
                c.Parameters.AddWithValue("@r", roleId);
            });
        var token = Guid.NewGuid().ToString("N");
        await db.ExecuteAsync(
            "INSERT INTO platform_sessions (admin_id, session_token, expires_at) VALUES (@a, @t, NOW() + INTERVAL '1 hour')",
            c => { c.Parameters.AddWithValue("@a", adminId); c.Parameters.AddWithValue("@t", token); });
        return token;
    }

    // These tests assert on role validation and the last-admin guard, so the roles
    // they name must exist. CoreSchemaService seeds them at boot, but no test in
    // this collection invokes it — so on a cold database the roles are absent until
    // some other suite happens to create them first. Seed the ones we depend on
    // rather than inherit whatever a previous run left behind.
    private static Task SeedRequiredRolesAsync(Database db) =>
        db.ExecuteAsync("""
            INSERT INTO roles (name, permissions_json, is_system)
            SELECT seed.name, seed.permissions_json, TRUE
            FROM (VALUES
                ('Company Admin', jsonb_build_array('*')),
                ('Dispatcher',    jsonb_build_array('dispatch:view')),
                ('Driver',        jsonb_build_array('jobs:view'))
            ) AS seed(name, permissions_json)
            WHERE NOT EXISTS (
                SELECT 1 FROM roles r WHERE r.company_id IS NULL AND LOWER(r.name) = LOWER(seed.name))
            """);

    private static Task<long> SeedCompanyAsync(Database db, string suffix) =>
        db.InsertAsync(
            "INSERT INTO companies (company_code, name, industry, status) VALUES (@code, @name, 'Logistics', 'Active') RETURNING id",
            c =>
            {
                c.Parameters.AddWithValue("@code", $"USR-{suffix}");
                c.Parameters.AddWithValue("@name", $"User Admin Test {suffix}");
            });

    private static Task<long> SeedUserAsync(Database db, long companyId, string email, string role, string status = "Active") =>
        db.InsertAsync(
            @"INSERT INTO users (company_id, full_name, email, role_name, status, password_hash)
              VALUES (@c, @n, @e, @r, @s, @h) RETURNING id",
            c =>
            {
                c.Parameters.AddWithValue("@c", companyId);
                c.Parameters.AddWithValue("@n", email.Split('@')[0]);
                c.Parameters.AddWithValue("@e", email);
                c.Parameters.AddWithValue("@r", role);
                c.Parameters.AddWithValue("@s", status);
                c.Parameters.AddWithValue("@h", PlatformSchemaService.HashPassword("Test-Password-123!"));
            });

    [Fact]
    public async Task Email_Change_Is_Validated_Collision_Safe_And_Revokes_Sessions()
    {
        var db = Db();
        await new PlatformSchemaService(db).EnsureAsync();
        await new SecuritySchemaService(db).EnsureAsync();
        await SeedRequiredRolesAsync(db);
        var suffix = Unique();
        var token = await SeedPlatformAdminAsync(db, "platform_super_admin", $"ua-{suffix}@opstrax.test");

        var companyA = await SeedCompanyAsync(db, suffix);
        var companyB = await SeedCompanyAsync(db, $"b{suffix}");
        await SeedUserAsync(db, companyA, $"admin-{suffix}@acme.test", "Company Admin");
        var dispatcher = await SeedUserAsync(db, companyA, $"dispatch-{suffix}@acme.test", "Dispatcher");
        var foreign = $"foreign-{suffix}@other.test";
        await SeedUserAsync(db, companyB, foreign, "Company Admin");

        // A live session for the user whose identity is about to change.
        await db.ExecuteAsync(
            @"INSERT INTO user_sessions (user_id, company_id, session_token, expires_at)
              VALUES (@u, @c, @t, NOW() + INTERVAL '8 hours')",
            c =>
            {
                c.Parameters.AddWithValue("@u", dispatcher);
                c.Parameters.AddWithValue("@c", companyA);
                c.Parameters.AddWithValue("@t", Guid.NewGuid().ToString("N"));
            });

        // Malformed address is refused outright.
        var malformed = await PlatformEndpoints.TenantUserUpdate(companyA, dispatcher, Http(token),
            new() { ["email"] = "not-an-email" }, db, CancellationToken.None);
        Assert.Equal(StatusCodes.Status400BadRequest, Status(malformed));

        // An address owned by ANOTHER tenant is refused — never relocated.
        var crossTenant = await PlatformEndpoints.TenantUserUpdate(companyA, dispatcher, Http(token),
            new() { ["email"] = foreign }, db, CancellationToken.None);
        Assert.Equal(StatusCodes.Status409Conflict, Status(crossTenant));
        Assert.Equal(companyB, await db.ScalarLongAsync("SELECT company_id FROM users WHERE LOWER(email)=LOWER(@e)",
            c => c.Parameters.AddWithValue("@e", foreign)));

        // A sibling's address in the SAME tenant is refused too.
        var sibling = await PlatformEndpoints.TenantUserUpdate(companyA, dispatcher, Http(token),
            new() { ["email"] = $"admin-{suffix}@acme.test" }, db, CancellationToken.None);
        Assert.Equal(StatusCodes.Status409Conflict, Status(sibling));

        // The legitimate correction lands, and takes the live session with it.
        var corrected = $"dispatcher-{suffix}@acme.test";
        var ok = await PlatformEndpoints.TenantUserUpdate(companyA, dispatcher, Http(token),
            new() { ["email"] = corrected }, db, CancellationToken.None);
        Assert.Equal(StatusCodes.Status200OK, Status(ok));
        Assert.Equal(corrected, (await db.QuerySingleAsync("SELECT email FROM users WHERE id=@u",
            c => c.Parameters.AddWithValue("@u", dispatcher)))?["email"]?.ToString());
        Assert.Equal(0, await db.ScalarLongAsync("SELECT COUNT(*) FROM user_sessions WHERE user_id=@u",
            c => c.Parameters.AddWithValue("@u", dispatcher)));

        var audited = await db.ScalarLongAsync(
            "SELECT COUNT(*) FROM platform_audit_log WHERE action='tenant.user.updated' AND entity_id=@u",
            c => c.Parameters.AddWithValue("@u", dispatcher));
        Assert.True(audited > 0, "the email change must be audited");

        // Housekeeping so repeated local runs stay clean.
        await db.ExecuteAsync("DELETE FROM users WHERE company_id = ANY(@ids)",
            c => c.Parameters.AddWithValue("@ids", new[] { companyA, companyB }));
    }

    [Fact]
    public async Task Last_Active_Administrator_Cannot_Be_Demoted_Or_Disabled()
    {
        var db = Db();
        await new PlatformSchemaService(db).EnsureAsync();
        await new SecuritySchemaService(db).EnsureAsync();
        await SeedRequiredRolesAsync(db);
        var suffix = Unique();
        var token = await SeedPlatformAdminAsync(db, "platform_super_admin", $"ua2-{suffix}@opstrax.test");

        var companyId = await SeedCompanyAsync(db, suffix);
        var onlyAdmin = await SeedUserAsync(db, companyId, $"solo-{suffix}@acme.test", "Company Admin");
        await SeedUserAsync(db, companyId, $"driver-{suffix}@acme.test", "Driver");

        var demote = await PlatformEndpoints.TenantUserUpdate(companyId, onlyAdmin, Http(token),
            new() { ["roleName"] = "Dispatcher" }, db, CancellationToken.None);
        Assert.Equal(StatusCodes.Status409Conflict, Status(demote));

        var disable = await PlatformEndpoints.TenantUserUpdate(companyId, onlyAdmin, Http(token),
            new() { ["status"] = "Disabled" }, db, CancellationToken.None);
        Assert.Equal(StatusCodes.Status409Conflict, Status(disable));

        // Unchanged on both counts.
        var row = await db.QuerySingleAsync("SELECT role_name, status FROM users WHERE id=@u",
            c => c.Parameters.AddWithValue("@u", onlyAdmin));
        Assert.Equal("Company Admin", row?["roleName"]?.ToString());
        Assert.Equal("Active", row?["status"]?.ToString());

        // Create a second administrator from the platform console, then the first
        // may be stood down.
        var created = await PlatformEndpoints.TenantUserCreate(companyId, Http(token),
            new()
            {
                ["email"] = $"second-admin-{suffix}@acme.test",
                ["fullName"] = "Second Admin",
                ["roleName"] = "Company Admin",
            }, db, CancellationToken.None);
        Assert.Equal(StatusCodes.Status200OK, Status(created));

        var nowAllowed = await PlatformEndpoints.TenantUserUpdate(companyId, onlyAdmin, Http(token),
            new() { ["status"] = "Disabled" }, db, CancellationToken.None);
        Assert.Equal(StatusCodes.Status200OK, Status(nowAllowed));

        await db.ExecuteAsync("DELETE FROM users WHERE company_id=@c",
            c => c.Parameters.AddWithValue("@c", companyId));
    }

    [Fact]
    public async Task Creating_A_Tenant_User_Refuses_A_Foreign_Email_And_Unknown_Roles()
    {
        var db = Db();
        await new PlatformSchemaService(db).EnsureAsync();
        await new SecuritySchemaService(db).EnsureAsync();
        await SeedRequiredRolesAsync(db);
        var suffix = Unique();
        var token = await SeedPlatformAdminAsync(db, "platform_super_admin", $"ua3-{suffix}@opstrax.test");

        var companyA = await SeedCompanyAsync(db, suffix);
        var companyB = await SeedCompanyAsync(db, $"b{suffix}");
        var foreign = $"taken-{suffix}@other.test";
        await SeedUserAsync(db, companyB, foreign, "Company Admin");

        var stolen = await PlatformEndpoints.TenantUserCreate(companyA, Http(token),
            new() { ["email"] = foreign, ["fullName"] = "Impostor", ["roleName"] = "Company Admin" },
            db, CancellationToken.None);
        Assert.Equal(StatusCodes.Status409Conflict, Status(stolen));
        Assert.Equal(companyB, await db.ScalarLongAsync("SELECT company_id FROM users WHERE LOWER(email)=LOWER(@e)",
            c => c.Parameters.AddWithValue("@e", foreign)));

        var badRole = await PlatformEndpoints.TenantUserCreate(companyA, Http(token),
            new() { ["email"] = $"new-{suffix}@acme.test", ["fullName"] = "New User", ["roleName"] = "Emperor" },
            db, CancellationToken.None);
        Assert.Equal(StatusCodes.Status400BadRequest, Status(badRole));
        Assert.Equal(0, await db.ScalarLongAsync("SELECT COUNT(*) FROM users WHERE company_id=@c",
            c => c.Parameters.AddWithValue("@c", companyA)));

        await db.ExecuteAsync("DELETE FROM users WHERE company_id = ANY(@ids)",
            c => c.Parameters.AddWithValue("@ids", new[] { companyA, companyB }));
    }
}
