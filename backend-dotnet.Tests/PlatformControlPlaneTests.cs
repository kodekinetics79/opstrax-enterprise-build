using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Opstrax.Api.Controllers;
using Opstrax.Api.Data;
using Opstrax.Api.Services;
using System.Text.Json;

namespace Opstrax.Tests;

// ─────────────────────────────────────────────────────────────────────────────
// Platform Admin control plane — security + CRUD proof suite.
//
// Calls the real endpoint handlers (internal) against the local test Postgres,
// exactly like the other *PostgresTests in this project. HTTP-transport-level
// behaviour (middleware ordering, CSRF, rate limiting) is additionally covered
// by tools/platform-admin-smoke-test.sh against a running API.
// ─────────────────────────────────────────────────────────────────────────────
[Collection("platform-control-plane")]
[Trait("Category", "Integration")]
public class PlatformControlPlaneTests
{
    private const string LocalConnectionString =
        "Host=127.0.0.1;Port=5433;Database=opstrax_local;Username=zayra;Password=zayra";

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
        if (!string.IsNullOrEmpty(bearer))
            http.Request.Headers.Authorization = $"Bearer {bearer}";
        return http;
    }

    private static int? StatusOf(IResult result) => (result as IStatusCodeHttpResult)?.StatusCode;
    private static object? ValueOf(IResult result) => (result as IValueHttpResult)?.Value;
    private static string JsonOf(IResult result) => JsonSerializer.Serialize(ValueOf(result));

    // Seeds a platform admin with the given role and an active session; returns the token.
    private static async Task<(long AdminId, string Token, string Email)> SeedAdminSessionAsync(Database db, string roleKey)
    {
        var email = $"cp-test-{Unique()}@opstrax.test";
        var roleId = await db.ScalarLongAsync("SELECT id FROM platform_roles WHERE role_key=@k",
            c => c.Parameters.AddWithValue("@k", roleKey));
        Assert.True(roleId > 0, $"role {roleKey} must be seeded");
        var adminId = await db.InsertAsync(
            @"INSERT INTO platform_admins (email, full_name, password_hash, role_id, status)
              VALUES (@e, 'Control Plane Test', @h, @r, 'Active') RETURNING id",
            c =>
            {
                c.Parameters.AddWithValue("@e", email);
                c.Parameters.AddWithValue("@h", PlatformSchemaService.HashPassword("Cp-Test-Pass-1!"));
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

    private static async Task CleanupAdminAsync(Database db, long adminId, string email)
    {
        await db.ExecuteAsync("DELETE FROM platform_sessions WHERE admin_id=@a", c => c.Parameters.AddWithValue("@a", adminId));
        await db.ExecuteAsync("DELETE FROM platform_audit_log WHERE actor_admin_id=@a OR actor_email=@e",
            c => { c.Parameters.AddWithValue("@a", adminId); c.Parameters.AddWithValue("@e", email); });
        await db.ExecuteAsync("DELETE FROM platform_admins WHERE id=@a", c => c.Parameters.AddWithValue("@a", adminId));
    }

    private static async Task CleanupTenantAsync(Database db, long companyId)
    {
        foreach (var sql in new[]
        {
            "DELETE FROM platform_audit_log WHERE target_company_id=@id",
            "DELETE FROM platform_invoices WHERE company_id=@id",
            "DELETE FROM tenant_entitlements WHERE company_id=@id",
            "DELETE FROM tenant_subscriptions WHERE company_id=@id",
            "DELETE FROM user_sessions WHERE company_id=@id",
            "DELETE FROM users WHERE company_id=@id",
            "DELETE FROM companies WHERE id=@id",
        })
            await db.ExecuteAsync(sql, c => c.Parameters.AddWithValue("@id", companyId));
    }

    // ════════════════════════════════════════════════════════════════════════
    // 1–2. Login success + failure, both audited; response leaks no secrets.
    // ════════════════════════════════════════════════════════════════════════
    [Fact]
    public async Task PlatformLogin_Succeeds_Audits_And_Leaks_No_Secrets()
    {
        var db = CreateDatabase();
        await new PlatformSchemaService(db).EnsureAsync();
        await new SecuritySchemaService(db).EnsureAsync();
        var (adminId, _, email) = await SeedAdminSessionAsync(db, "platform_super_admin");
        try
        {
            var result = await PlatformEndpoints.PlatformLogin(Http(), new PlatformEndpoints.PlatformLoginRequest(email, "Cp-Test-Pass-1!"), db, CancellationToken.None);
            Assert.Equal(200, StatusOf(result));

            var json = JsonOf(result);
            Assert.Contains("token", json);
            Assert.DoesNotContain("passwordHash", json, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("password_hash", json, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("PBKDF2", json);

            var audited = await db.ScalarLongAsync(
                "SELECT COUNT(*) FROM platform_audit_log WHERE actor_email=@e AND action='platform.login'",
                c => c.Parameters.AddWithValue("@e", email));
            Assert.True(audited >= 1, "successful login must write an audit row");
        }
        finally { await CleanupAdminAsync(db, adminId, email); }
    }

    [Fact]
    public async Task PlatformLogin_InvalidPassword_Is_401_And_Audited()
    {
        var db = CreateDatabase();
        await new PlatformSchemaService(db).EnsureAsync();
        var (adminId, _, email) = await SeedAdminSessionAsync(db, "platform_super_admin");
        try
        {
            var result = await PlatformEndpoints.PlatformLogin(Http(), new PlatformEndpoints.PlatformLoginRequest(email, "wrong-password"), db, CancellationToken.None);
            Assert.Equal(401, StatusOf(result));

            var audited = await db.ScalarLongAsync(
                "SELECT COUNT(*) FROM platform_audit_log WHERE actor_email=@e AND action='platform.login_failed'",
                c => c.Parameters.AddWithValue("@e", email));
            Assert.True(audited >= 1, "failed login must write an audit row");

            var details = await db.QueryAsync(
                "SELECT details_json::text detail FROM platform_audit_log WHERE actor_email=@e AND action='platform.login_failed'",
                c => c.Parameters.AddWithValue("@e", email));
            Assert.All(details, d => Assert.DoesNotContain("wrong-password", d["detail"]?.ToString() ?? ""));
        }
        finally { await CleanupAdminAsync(db, adminId, email); }
    }

    [Fact]
    public async Task PlatformLogin_Locks_Out_After_Repeated_Failures()
    {
        var db = CreateDatabase();
        await new PlatformSchemaService(db).EnsureAsync();
        var email = $"lockout-{Unique()}@opstrax.test"; // account need not exist — lockout still applies
        try
        {
            IResult last = Results.Ok();
            for (var i = 0; i < 6; i++)
                last = await PlatformEndpoints.PlatformLogin(Http(), new PlatformEndpoints.PlatformLoginRequest(email, "nope"), db, CancellationToken.None);
            Assert.Equal(429, StatusOf(last));

            var locked = await db.ScalarLongAsync(
                "SELECT COUNT(*) FROM platform_audit_log WHERE actor_email=@e AND action='platform.login_locked'",
                c => c.Parameters.AddWithValue("@e", email));
            Assert.True(locked >= 1);
        }
        finally
        {
            await db.ExecuteAsync("DELETE FROM platform_audit_log WHERE actor_email=@e", c => c.Parameters.AddWithValue("@e", email));
        }
    }

    // ════════════════════════════════════════════════════════════════════════
    // 3–4. Token separation: no token / tenant token / permission boundaries.
    // ════════════════════════════════════════════════════════════════════════
    [Fact]
    public async Task Platform_Guard_Rejects_Missing_And_Tenant_Tokens()
    {
        var db = CreateDatabase();
        await new PlatformSchemaService(db).EnsureAsync();

        // No token → 401
        var (p1, e1) = await PlatformEndpoints.RequireAsync(Http(), db, "platform:tenants:view", CancellationToken.None);
        Assert.Null(p1);
        Assert.Equal(401, StatusOf(e1!));

        // A TENANT session token must never authenticate on the platform plane.
        var code = $"CPT-{Unique()}";
        var companyId = await db.InsertAsync(
            "INSERT INTO companies (company_code, name, industry, status) VALUES (@c, 'Guard Test Co', 'Logistics', 'Active') RETURNING id",
            c => c.Parameters.AddWithValue("@c", code));
        try
        {
            var userId = await db.InsertAsync(
                "INSERT INTO users (company_id, full_name, email, role_name, status) VALUES (@cid, 'Guard User', @e, 'Company Admin', 'Active') RETURNING id",
                c => { c.Parameters.AddWithValue("@cid", companyId); c.Parameters.AddWithValue("@e", $"guard-{Unique()}@opstrax.test"); });
            var tenantToken = Convert.ToBase64String(System.Security.Cryptography.RandomNumberGenerator.GetBytes(32));
            await db.ExecuteAsync(
                "INSERT INTO user_sessions (user_id, company_id, session_token, expires_at) VALUES (@u, @c, @t, NOW() + INTERVAL '1 hour')",
                c => { c.Parameters.AddWithValue("@u", userId); c.Parameters.AddWithValue("@c", companyId); c.Parameters.AddWithValue("@t", tenantToken); });

            var (p2, e2) = await PlatformEndpoints.RequireAsync(Http(tenantToken), db, "platform:tenants:view", CancellationToken.None);
            Assert.Null(p2);
            Assert.Equal(401, StatusOf(e2!));
        }
        finally { await CleanupTenantAsync(db, companyId); }
    }

    [Fact]
    public async Task Platform_Token_Is_Never_A_Tenant_Session()
    {
        var db = CreateDatabase();
        await new PlatformSchemaService(db).EnsureAsync();
        var (adminId, token, email) = await SeedAdminSessionAsync(db, "platform_super_admin");
        try
        {
            // The tenant auth middleware validates bearer tokens with this exact lookup
            // (Program.cs). A platform token must not resolve to a tenant session.
            var session = await db.QuerySingleAsync(
                @"SELECT s.user_id FROM user_sessions s JOIN users u ON u.id = s.user_id
                  WHERE s.session_token=@token AND s.expires_at > NOW() AND u.status='Active' LIMIT 1",
                c => c.Parameters.AddWithValue("@token", token));
            Assert.Null(session);
        }
        finally { await CleanupAdminAsync(db, adminId, email); }
    }

    [Fact]
    public async Task Permissions_Are_Enforced_And_Offboard_Is_Elevated()
    {
        var db = CreateDatabase();
        await new PlatformSchemaService(db).EnsureAsync();

        // finance_admin lacks tenants:manage → 403
        var (finId, finToken, finEmail) = await SeedAdminSessionAsync(db, "finance_admin");
        // sales_admin HAS tenants:manage but NOT tenants:offboard → hard delete denied
        var (salesId, salesToken, salesEmail) = await SeedAdminSessionAsync(db, "sales_admin");
        var (superId, superToken, superEmail) = await SeedAdminSessionAsync(db, "platform_super_admin");
        try
        {
            var (p1, e1) = await PlatformEndpoints.RequireAsync(Http(finToken), db, "platform:tenants:manage", CancellationToken.None);
            Assert.Null(p1);
            Assert.Equal(403, StatusOf(e1!));

            var (p2, e2) = await PlatformEndpoints.RequireAsync(Http(salesToken), db, "platform:tenants:offboard", CancellationToken.None);
            Assert.Null(p2);
            Assert.Equal(403, StatusOf(e2!));

            var (p3, e3) = await PlatformEndpoints.RequireAsync(Http(superToken), db, "platform:tenants:offboard", CancellationToken.None);
            Assert.NotNull(p3);
            Assert.Null(e3);
        }
        finally
        {
            await CleanupAdminAsync(db, finId, finEmail);
            await CleanupAdminAsync(db, salesId, salesEmail);
            await CleanupAdminAsync(db, superId, superEmail);
        }
    }

    // ════════════════════════════════════════════════════════════════════════
    // 5–20. Full tenant lifecycle with audit + session revocation + billing +
    //        entitlements + injection/XSS safety, via the real handlers.
    // ════════════════════════════════════════════════════════════════════════
    [Fact]
    public async Task Tenant_Lifecycle_Is_Safe_Audited_And_Revokes_Sessions()
    {
        var db = CreateDatabase();
        await new PlatformSchemaService(db).EnsureAsync();
        var (adminId, token, email) = await SeedAdminSessionAsync(db, "platform_super_admin");
        var countries = new CountryProfileService(db);
        var flags = new FeatureFlagService(db); // TenantCreate seeds the tenant's default flags
        var code = $"CPT-{Unique()}";
        // Hostile name: XSS + SQLi payload must be stored inertly as a literal.
        var hostileName = "<script>alert(1)</script>'; DROP TABLE companies;--";
        long companyId = 0;
        try
        {
            // CREATE
            var create = await PlatformEndpoints.TenantCreate(Http(token),
                new Dictionary<string, object?> { ["name"] = hostileName, ["companyCode"] = code, ["seatLimit"] = 5L, ["status"] = "trial" },
                db, countries, flags, CancellationToken.None);
            Assert.Equal(200, StatusOf(create));
            companyId = await db.ScalarLongAsync("SELECT id FROM companies WHERE company_code=@c", c => c.Parameters.AddWithValue("@c", code));
            Assert.True(companyId > 0);

            // companies table survived the SQLi payload; name round-trips as a literal
            var storedName = (await db.QuerySingleAsync("SELECT name FROM companies WHERE id=@id",
                c => c.Parameters.AddWithValue("@id", companyId)))?["name"]?.ToString();
            Assert.Equal(hostileName, storedName);

            // DUPLICATE CODE → 409
            var dup = await PlatformEndpoints.TenantCreate(Http(token),
                new Dictionary<string, object?> { ["name"] = "Dup", ["companyCode"] = code },
                db, countries, flags, CancellationToken.None);
            Assert.Equal(409, StatusOf(dup));

            // LIST + DETAIL work and leak no secrets
            var list = await PlatformEndpoints.TenantsList(Http(token), db, CancellationToken.None);
            Assert.Equal(200, StatusOf(list));
            var listJson = JsonOf(list);
            Assert.Contains(code, listJson);
            Assert.DoesNotContain("passwordHash", listJson, StringComparison.OrdinalIgnoreCase);

            var detail = await PlatformEndpoints.TenantDetail(companyId, Http(token), db, CancellationToken.None);
            Assert.Equal(200, StatusOf(detail));
            Assert.Contains("alert(1)", JsonOf(detail)); // returned as data, not stripped server-side (client renders as text)

            // UPDATE (limits/quota) + 404 for unknown tenant
            var update = await PlatformEndpoints.TenantUpdate(companyId, Http(token),
                new Dictionary<string, object?> { ["seatLimit"] = 42L }, db, countries, CancellationToken.None);
            Assert.Equal(200, StatusOf(update));
            var seats = await db.ScalarLongAsync("SELECT seat_limit FROM tenant_subscriptions WHERE company_id=@id",
                c => c.Parameters.AddWithValue("@id", companyId));
            Assert.Equal(42, seats);

            var missing = await PlatformEndpoints.TenantUpdate(999_999_999, Http(token),
                new Dictionary<string, object?> { ["seatLimit"] = 5L }, db, countries, CancellationToken.None);
            Assert.Equal(404, StatusOf(missing));

            // TENANT ADMIN INVITE — creates an Invited user without any credential
            var inviteEmail = $"invite-{Unique()}@opstrax.test";
            var invite = await PlatformEndpoints.TenantResetInvite(companyId, Http(token),
                new Dictionary<string, object?> { ["adminEmail"] = inviteEmail }, db, CancellationToken.None);
            Assert.Equal(200, StatusOf(invite));
            var invited = await db.QuerySingleAsync("SELECT status, password_hash FROM users WHERE email=@e",
                c => c.Parameters.AddWithValue("@e", inviteEmail));
            Assert.NotNull(invited);
            Assert.Equal("Invited", invited!["status"]?.ToString());
            Assert.True(invited["passwordHash"] is null or DBNull, "invite must never set a password");

            // ACTIVE USER SESSION → SUSPEND revokes it and locks the company
            var userId = await db.InsertAsync(
                "INSERT INTO users (company_id, full_name, email, role_name, status) VALUES (@cid, 'Session User', @e, 'Dispatcher', 'Active') RETURNING id",
                c => { c.Parameters.AddWithValue("@cid", companyId); c.Parameters.AddWithValue("@e", $"sess-{Unique()}@opstrax.test"); });
            async Task<string> OpenSessionAsync()
            {
                var t = Convert.ToBase64String(System.Security.Cryptography.RandomNumberGenerator.GetBytes(32));
                await db.ExecuteAsync(
                    "INSERT INTO user_sessions (user_id, company_id, session_token, expires_at) VALUES (@u, @c, @t, NOW() + INTERVAL '8 hour')",
                    c => { c.Parameters.AddWithValue("@u", userId); c.Parameters.AddWithValue("@c", companyId); c.Parameters.AddWithValue("@t", t); });
                return t;
            }
            await OpenSessionAsync();

            var suspend = await PlatformEndpoints.TenantStatus(companyId, Http(token),
                new Dictionary<string, object?> { ["action"] = "suspend" }, db, CancellationToken.None);
            Assert.Equal(200, StatusOf(suspend));
            var liveSessions = await db.ScalarLongAsync("SELECT COUNT(*) FROM user_sessions WHERE company_id=@id",
                c => c.Parameters.AddWithValue("@id", companyId));
            Assert.Equal(0, liveSessions);
            var companyStatus = (await db.QuerySingleAsync("SELECT status FROM companies WHERE id=@id",
                c => c.Parameters.AddWithValue("@id", companyId)))?["status"]?.ToString();
            Assert.Equal("Suspended", companyStatus);

            // REACTIVATE
            var reactivate = await PlatformEndpoints.TenantStatus(companyId, Http(token),
                new Dictionary<string, object?> { ["action"] = "reactivate" }, db, CancellationToken.None);
            Assert.Equal(200, StatusOf(reactivate));
            companyStatus = (await db.QuerySingleAsync("SELECT status FROM companies WHERE id=@id",
                c => c.Parameters.AddWithValue("@id", companyId)))?["status"]?.ToString();
            Assert.Equal("Active", companyStatus);

            // EXPLICIT REVOKE-SESSIONS endpoint
            await OpenSessionAsync();
            var revoke = await PlatformEndpoints.TenantRevokeSessions(companyId, Http(token), db, CancellationToken.None);
            Assert.Equal(200, StatusOf(revoke));
            Assert.Equal(0, await db.ScalarLongAsync("SELECT COUNT(*) FROM user_sessions WHERE company_id=@id",
                c => c.Parameters.AddWithValue("@id", companyId)));

            // INVALID STATUS ACTION → 400
            var badAction = await PlatformEndpoints.TenantStatus(companyId, Http(token),
                new Dictionary<string, object?> { ["action"] = "obliterate" }, db, CancellationToken.None);
            Assert.Equal(400, StatusOf(badAction));

            // CANCEL is safe: data retained, sessions revoked, company locked
            await OpenSessionAsync();
            var cancel = await PlatformEndpoints.TenantStatus(companyId, Http(token),
                new Dictionary<string, object?> { ["action"] = "cancel" }, db, CancellationToken.None);
            Assert.Equal(200, StatusOf(cancel));
            Assert.Equal(0, await db.ScalarLongAsync("SELECT COUNT(*) FROM user_sessions WHERE company_id=@id",
                c => c.Parameters.AddWithValue("@id", companyId)));
            Assert.True(await db.ScalarLongAsync("SELECT COUNT(*) FROM users WHERE company_id=@id",
                c => c.Parameters.AddWithValue("@id", companyId)) >= 2, "cancel must retain tenant data");

            // ENTITLEMENTS: disable a module; verify the middleware gate query blocks it
            var entSet = await PlatformEndpoints.EntitlementsSet(companyId, Http(token),
                new Dictionary<string, object?> { ["moduleKey"] = "dispatch", ["enabled"] = false }, db, CancellationToken.None);
            Assert.Equal(200, StatusOf(entSet));
            var blocked = await db.ScalarLongAsync(
                "SELECT COUNT(*) FROM tenant_entitlements WHERE company_id=@cid AND module_key='dispatch' AND enabled=false",
                c => c.Parameters.AddWithValue("@cid", companyId));
            Assert.Equal(1, blocked); // same predicate Program.cs uses to 403 tenant requests

            var entGet = await PlatformEndpoints.EntitlementsGet(companyId, Http(token), db, CancellationToken.None);
            Assert.Equal(200, StatusOf(entGet));
            Assert.Contains("dispatch", JsonOf(entGet));

            // re-enable → gate opens
            await PlatformEndpoints.EntitlementsSet(companyId, Http(token),
                new Dictionary<string, object?> { ["moduleKey"] = "dispatch", ["enabled"] = true }, db, CancellationToken.None);
            Assert.Equal(0, await db.ScalarLongAsync(
                "SELECT COUNT(*) FROM tenant_entitlements WHERE company_id=@cid AND module_key='dispatch' AND enabled=false",
                c => c.Parameters.AddWithValue("@cid", companyId)));

            // INVALID module keys rejected (typo/injection can't become a phantom row)
            foreach (var bad in new[] { "'; DROP TABLE tenant_entitlements;--", "<script>", "Dispatch", "a" })
            {
                var rejected = await PlatformEndpoints.EntitlementsSet(companyId, Http(token),
                    new Dictionary<string, object?> { ["moduleKey"] = bad, ["enabled"] = false }, db, CancellationToken.None);
                Assert.Equal(400, StatusOf(rejected));
            }

            // BILLING: create invoice + mark paid
            var inv = await PlatformEndpoints.InvoiceCreate(Http(token),
                new Dictionary<string, object?> { ["companyId"] = companyId, ["amountCents"] = 9900L }, db, CancellationToken.None);
            Assert.Equal(200, StatusOf(inv));
            var invoiceId = await db.ScalarLongAsync("SELECT id FROM platform_invoices WHERE company_id=@id ORDER BY id DESC LIMIT 1",
                c => c.Parameters.AddWithValue("@id", companyId));
            var paid = await PlatformEndpoints.InvoiceMarkPaid(invoiceId, Http(token), db, CancellationToken.None);
            Assert.Equal(200, StatusOf(paid));
            Assert.Equal("paid", (await db.QuerySingleAsync("SELECT status FROM platform_invoices WHERE id=@id",
                c => c.Parameters.AddWithValue("@id", invoiceId)))?["status"]?.ToString());

            // EVERY MUTATION AUDITED — one row per action performed above
            var auditActions = (await db.QueryAsync(
                    "SELECT action FROM platform_audit_log WHERE target_company_id=@id",
                    c => c.Parameters.AddWithValue("@id", companyId)))
                .Select(r => r["action"]?.ToString())
                .ToHashSet();
            foreach (var expected in new[]
            {
                "tenant.created", "tenant.updated", "tenant.admin_invite.reset",
                "tenant.suspend", "tenant.reactivate", "tenant.sessions_revoked", "tenant.cancel",
                "entitlement.disabled", "entitlement.enabled", "invoice.created", "invoice.paid",
            })
                Assert.Contains(expected, auditActions);
        }
        finally
        {
            if (companyId > 0) await CleanupTenantAsync(db, companyId);
            await CleanupAdminAsync(db, adminId, email);
        }
    }

    // ════════════════════════════════════════════════════════════════════════
    // Seat-limit quota set by Platform Admin is enforced at tenant user creation.
    // ════════════════════════════════════════════════════════════════════════
    [Fact]
    public async Task Seat_Limit_Blocks_User_Creation_At_Capacity()
    {
        var db = CreateDatabase();
        await new PlatformSchemaService(db).EnsureAsync();
        await new SecuritySchemaService(db).EnsureAsync();
        var code = $"CPT-{Unique()}";
        var companyId = await db.InsertAsync(
            "INSERT INTO companies (company_code, name, industry, status) VALUES (@c, 'Seat Cap Co', 'Logistics', 'Active') RETURNING id",
            c => c.Parameters.AddWithValue("@c", code));
        try
        {
            await db.ExecuteAsync(
                "INSERT INTO tenant_subscriptions (company_id, status, seat_limit) VALUES (@cid, 'active', 2)",
                c => c.Parameters.AddWithValue("@cid", companyId));
            var adminUserId = await db.InsertAsync(
                "INSERT INTO users (company_id, full_name, email, role_name, status) VALUES (@cid, 'Seat Admin', @e, 'Company Admin', 'Active') RETURNING id",
                c => { c.Parameters.AddWithValue("@cid", companyId); c.Parameters.AddWithValue("@e", $"seat-admin-{Unique()}@opstrax.test"); });

            var audit = new AuditService(db);
            DefaultHttpContext TenantAdminHttp()
            {
                var http = Http();
                http.Items[EndpointMappings.AuthUserIdItemKey] = adminUserId;
                http.Items[EndpointMappings.AuthCompanyIdItemKey] = companyId;
                http.Items[EndpointMappings.AuthRoleItemKey] = "Company Admin";
                http.Items[EndpointMappings.AuthPermissionsItemKey] = new[] { "users:create" };
                return http;
            }

            // Seat 2 of 2 — allowed.
            var second = await EndpointMappings.CreateAdminUser(TenantAdminHttp(),
                new Dictionary<string, object?> { ["fullName"] = "Second Seat", ["roleName"] = "Dispatcher", ["email"] = $"seat2-{Unique()}@opstrax.test", ["password"] = "Seat-Pass-1!" },
                db, audit, CancellationToken.None);
            Assert.Equal(201, StatusOf(second));

            // Seat 3 of 2 — blocked with 409.
            var third = await EndpointMappings.CreateAdminUser(TenantAdminHttp(),
                new Dictionary<string, object?> { ["fullName"] = "Third Seat", ["roleName"] = "Dispatcher", ["email"] = $"seat3-{Unique()}@opstrax.test", ["password"] = "Seat-Pass-1!" },
                db, audit, CancellationToken.None);
            Assert.Equal(409, StatusOf(third));

            // Platform raises the cap → creation allowed again.
            await db.ExecuteAsync("UPDATE tenant_subscriptions SET seat_limit=3 WHERE company_id=@cid",
                c => c.Parameters.AddWithValue("@cid", companyId));
            var afterRaise = await EndpointMappings.CreateAdminUser(TenantAdminHttp(),
                new Dictionary<string, object?> { ["fullName"] = "Third Seat OK", ["roleName"] = "Dispatcher", ["email"] = $"seat3b-{Unique()}@opstrax.test", ["password"] = "Seat-Pass-1!" },
                db, audit, CancellationToken.None);
            Assert.Equal(201, StatusOf(afterRaise));
        }
        finally
        {
            await db.ExecuteAsync("DELETE FROM audit_logs WHERE company_id=@id", c => c.Parameters.AddWithValue("@id", companyId));
            await CleanupTenantAsync(db, companyId);
        }
    }

    // ════════════════════════════════════════════════════════════════════════
    // Source regressions: audit log immutable via APIs; guard invariants hold.
    // ════════════════════════════════════════════════════════════════════════
    private static string RepoRoot =>
        Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../"));

    private static string ReadSource(params string[] parts)
        => File.ReadAllText(Path.Combine(new[] { RepoRoot }.Concat(parts).ToArray()));

    [Fact]
    public void AuditLog_Is_InsertOnly_Across_All_Endpoint_Sources()
    {
        foreach (var file in Directory.GetFiles(Path.Combine(RepoRoot, "backend-dotnet", "Controllers"), "*.cs"))
        {
            var src = File.ReadAllText(file);
            Assert.DoesNotContain("UPDATE platform_audit_log", src, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("DELETE FROM platform_audit_log", src, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("TRUNCATE platform_audit_log", src, StringComparison.OrdinalIgnoreCase);
        }
    }

    [Fact]
    public void Platform_Guard_Invariants_Hold_In_Source()
    {
        var platform = ReadSource("backend-dotnet", "Controllers", "PlatformEndpoints.cs");
        // Hard delete must stay behind the elevated offboard permission.
        Assert.Contains("platform:tenants:offboard", platform);
        // Failed logins must stay audited.
        Assert.Contains("platform.login_failed", platform);

        var program = ReadSource("backend-dotnet", "Program.cs");
        // Platform routes must keep bypassing the TENANT session middleware (they
        // self-authenticate), and rate limiting must run before that bypass.
        Assert.Contains("path.StartsWith(\"/api/platform\"", program);
        var rateLimitIdx = program.IndexOf("app.UseRateLimiter();", StringComparison.Ordinal);
        var bypassIdx = program.IndexOf("path.StartsWith(\"/api/platform\"", StringComparison.Ordinal);
        Assert.True(rateLimitIdx >= 0 && rateLimitIdx < bypassIdx, "rate limiter must precede the platform/login auth bypass");
    }
}
