using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Opstrax.Api.Controllers;
using Opstrax.Api.Data;
using Opstrax.Api.Services;
using Xunit;

namespace Opstrax.Tests;

/// <summary>
/// A mutation must never be gated on a <c>:view</c> permission.
///
/// Two endpoints were: <c>POST /api/digital-forms/submissions</c> required only
/// <c>safety:view</c> yet INSERTed a compliance record with a hard-coded
/// <c>status='Passed'</c> (a Read-only Auditor could fabricate a passed compliance
/// record), and the three control-tower action POSTs were gated <c>dashboard:view</c> —
/// held by every internal role — letting a viewer mint <c>eta.sent</c> /
/// <c>dispatch.review.created</c> / <c>maintenance.review.created</c> audit records.
///
/// The behavioural tests below execute the real RequirePermission closure against the
/// SHIPPED backend grant sets (EndpointMappings.RolePermissionDefaults), not an idealised
/// catalogue — that gap is why earlier role regressions passed a contract script.
/// </summary>
public class MutationPermissionTierTests
{
    // ── Blast radius: who KEEPS the action, and who is correctly locked out ──────
    // Every role that can already write in the module must still satisfy the manage
    // tier through the alias table, or the tightening would break real operators.
    [Theory]
    [InlineData("Tenant Admin", "safety:manage")]
    [InlineData("Safety Manager", "safety:manage")]
    [InlineData("Company Admin", "safety:manage")]
    [InlineData("Super Admin", "safety:manage")]
    [InlineData("Tenant Admin", "dispatch:manage")]
    [InlineData("Fleet Manager", "dispatch:manage")]
    [InlineData("Fleet Owner", "dispatch:manage")]
    [InlineData("Tenant Admin", "maintenance:manage")]
    [InlineData("Maintenance Manager", "maintenance:manage")]
    [InlineData("Fleet Manager", "maintenance:manage")]
    [InlineData("Mechanic", "maintenance:manage")]
    public void OperatorRoles_KeepTheirMutationTier(string role, string permission)
        => Assert.Null(EndpointMappings.RequirePermission(Principal(role), permission));

    // The whole point of the tightening: a read-only principal cannot write.
    [Theory]
    [InlineData("Read-Only Auditor", "safety:manage")]
    [InlineData("Read-Only Auditor", "dispatch:manage")]
    [InlineData("Read-Only Auditor", "maintenance:manage")]
    [InlineData("Carrier Partner", "dispatch:manage")]
    [InlineData("Carrier Partner", "maintenance:manage")]
    [InlineData("Customer Service", "safety:manage")]
    public void ReadOnlyRoles_CannotReachTheMutationTier(string role, string permission)
    {
        var denied = EndpointMappings.RequirePermission(Principal(role), permission);
        Assert.NotNull(denied);
        Assert.Equal(StatusCodes.Status403Forbidden,
            Assert.IsAssignableFrom<IStatusCodeHttpResult>(denied).StatusCode);
    }

    /// <summary>
    /// The Read-Only Auditor holds every module's :view token, so a `:view`-gated mutation
    /// is open to it by construction. This pins the specific gates that were wrong.
    /// </summary>
    [Fact]
    public void ReadOnlyAuditor_HoldsTheViewTokensThatUsedToGateTheseMutations()
    {
        var auditor = EndpointMappings.RolePermissionDefaults["Read-Only Auditor"];
        Assert.Contains("safety:view", auditor);
        Assert.Contains("dashboard:view", auditor);
        // …and it must NOT hold any write token in those modules.
        Assert.DoesNotContain(auditor, p => p.StartsWith("safety:", StringComparison.OrdinalIgnoreCase) && p != "safety:view");
        Assert.DoesNotContain(auditor, p => p.StartsWith("dispatch:", StringComparison.OrdinalIgnoreCase) && p != "dispatch:view");
        Assert.DoesNotContain(auditor, p => p.StartsWith("maintenance:", StringComparison.OrdinalIgnoreCase) && p != "maintenance:view");
    }

    /// <summary>Source pin: the gates must not quietly drift back to a :view tier.</summary>
    [Fact]
    public void MutationEndpoints_AreGatedAtTheWriteTier()
    {
        var source = MappingsSource();

        var submission = Slice(source, "app.MapPost(\"/api/digital-forms/submissions\"", "app.MapGet");
        Assert.Contains("RequirePermission(http, \"safety:manage\")", submission, StringComparison.Ordinal);
        Assert.DoesNotContain("RequirePermission(http, \"safety:view\")", submission, StringComparison.Ordinal);
        // It still writes the hard-coded Passed status, which is why the tier matters.
        Assert.Contains("'Passed'", submission, StringComparison.Ordinal);

        foreach (var (route, expected) in new[]
        {
            ("/api/control-tower/actions/send-eta-update", "dispatch:manage"),
            ("/api/control-tower/actions/create-dispatch-review", "dispatch:manage"),
            ("/api/control-tower/actions/create-maintenance-review", "maintenance:manage"),
        })
        {
            var line = Regex.Match(source, @"app\.MapPost\(""" + Regex.Escape(route) + @""".*", RegexOptions.None).Value;
            Assert.True(line.Length > 0, $"route not found: {route}");
            Assert.Contains($"\"{expected}\"", line, StringComparison.Ordinal);
            Assert.DoesNotContain("\"dashboard:view\"", line, StringComparison.Ordinal);
        }
    }

    // ── NEW-R1-06: the defaults are the FALLBACK when a tenant has no seeded roles row. ──
    // They must match database/init/002_seed.sql for the same role, and must NOT exceed it.

    [Theory]
    // Seed role 3 (Fleet Manager) grants map:view + telematics:view.
    [InlineData("Fleet Manager", "map:view")]
    [InlineData("Fleet Manager", "telematics:view")]
    // Seed role 4 (Dispatcher) grants map:view.
    [InlineData("Dispatcher", "map:view")]
    public void RoleDefaults_MatchTheDatabaseSeed(string role, string token)
        => Assert.Contains(token, EndpointMappings.RolePermissionDefaults[role], StringComparer.OrdinalIgnoreCase);

    [Theory]
    // The seed grants telematics:devices:view to NO role — the device registry is not part of
    // any seeded operator's surface, so the fallback must not invent it either.
    [InlineData("Fleet Manager", "telematics:devices:view")]
    [InlineData("Dispatcher", "telematics:devices:view")]
    [InlineData("Maintenance Manager", "telematics:devices:view")]
    [InlineData("Read-Only Auditor", "telematics:devices:view")]
    // 'Maintenance Manager' has no seed row; its analogue 'Mechanic' (role 6) has no map/
    // telematics grant. 'Read-only Auditor' (role 12) is audit/fleet/dashboard only — granting
    // map:view would hand a read-only role live GPS via the live-state alias group.
    [InlineData("Maintenance Manager", "map:view")]
    [InlineData("Read-Only Auditor", "map:view")]
    [InlineData("Maintenance Manager", "telematics:view")]
    [InlineData("Read-Only Auditor", "telematics:view")]
    public void RoleDefaults_DoNotExceedTheDatabaseSeed(string role, string token)
        => Assert.DoesNotContain(token, EndpointMappings.RolePermissionDefaults[role], StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// The reconciliation must not have loosened the telemetry satisfy-sets: a session holding
    /// ONLY fleet:view still reaches neither the live map nor the device registry. The frontend
    /// closure denies all four of these paths; the backend must stay at least as tight.
    /// </summary>
    [Theory]
    [InlineData("fleet:view", "telemetry.live_state.read")]
    [InlineData("fleet:view", "telemetry.devices.read")]
    [InlineData("telematics:view", "telemetry.devices.read")]
    [InlineData("map:view", "telemetry.devices.read")]
    public void FleetViewAlone_StillReachesNeitherLiveStateNorTheDeviceRegistry(string held, string required)
    {
        var http = new DefaultHttpContext();
        http.Items[EndpointMappings.AuthCompanyIdItemKey] = 771L;
        http.Items[EndpointMappings.AuthUserIdItemKey] = 55L;
        http.Items[EndpointMappings.AuthRoleItemKey] = "Fleet Manager";
        http.Items[EndpointMappings.AuthPermissionsItemKey] = new[] { held };
        Assert.NotNull(EndpointMappings.RequirePermission(http, required));
    }

    private static DefaultHttpContext Principal(string role)
    {
        var http = new DefaultHttpContext();
        http.Items[EndpointMappings.AuthCompanyIdItemKey] = 771L;
        http.Items[EndpointMappings.AuthUserIdItemKey] = 55L;
        http.Items[EndpointMappings.AuthRoleItemKey] = role;
        http.Items[EndpointMappings.AuthPermissionsItemKey] = EndpointMappings.RolePermissionDefaults[role];
        return http;
    }

    internal static string MappingsSource()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !Directory.Exists(Path.Combine(dir.FullName, "backend-dotnet")))
            dir = dir.Parent;
        Assert.NotNull(dir);
        return File.ReadAllText(Path.Combine(dir!.FullName, "backend-dotnet", "Controllers", "EndpointMappings.cs"));
    }

    private static string Slice(string source, string start, string end)
    {
        var s = source.IndexOf(start, StringComparison.Ordinal);
        Assert.True(s >= 0, $"marker not found: {start}");
        var e = source.IndexOf(end, s + start.Length, StringComparison.Ordinal);
        return e > s ? source[s..e] : source[s..];
    }
}

/// <summary>
/// DEF-027 retained-binding gap: <see cref="EndpointMappings.UpdateAdminUser"/> validated the
/// customer binding ONLY when the <c>customerId</c> key was present in the body. Changing a
/// bound portal user to an INTERNAL role while omitting the key therefore RETAINED the
/// binding and bricked the account — precisely what RequireCustomerBindableRole's own error
/// message warns about ("the account would lose access to every internal page").
/// </summary>
public class AdminUserBindingRevalidationPostgresTests
{
    private static Database CreateDatabase()
        => new(new ConfigurationBuilder().AddInMemoryCollection(
            new Dictionary<string, string?> { ["ConnectionStrings:DefaultConnection"] = TestDb.ConnectionString }).Build());

    [Fact]
    public async Task RoleChangeToInternalRole_ClearsARetainedCustomerBinding_EvenWhenCustomerIdIsOmitted()
    {
        var db = CreateDatabase();
        var suffix = Guid.NewGuid().ToString("N")[..12];
        var companyId = await db.InsertAsync(
            @"INSERT INTO companies (company_code, name, industry, timezone, status)
              VALUES (@code, 'Binding Revalidation Tenant', 'Logistics', 'America/New_York', 'Active')",
            c => c.Parameters.AddWithValue("@code", $"bind-{suffix}"));
        try
        {
            var customerId = await db.InsertAsync(
                "INSERT INTO customers (company_id, customer_code, name, status) VALUES (@cid, @code, 'Bound Customer', 'Active')",
                c => { c.Parameters.AddWithValue("@cid", companyId); c.Parameters.AddWithValue("@code", $"BIND-{suffix}"); });

            var userId = await db.InsertAsync(
                @"INSERT INTO users (company_id, customer_id, role_id, full_name, email, role_name, status, permissions_json)
                  VALUES (@cid, @cust,
                          (SELECT id FROM roles WHERE name='Customer Portal User' AND (company_id IS NULL OR company_id=@cid) ORDER BY company_id NULLS LAST LIMIT 1),
                          'Bound Portal User', @em, 'Customer Portal User', 'Active', '[""customer_portal:view""]'::jsonb)",
                c =>
                {
                    c.Parameters.AddWithValue("@cid", companyId);
                    c.Parameters.AddWithValue("@cust", customerId);
                    c.Parameters.AddWithValue("@em", $"bound-{suffix}@t.example");
                });
            await db.ExecuteAsync(
                "INSERT INTO user_sessions(user_id,company_id,session_token,expires_at) VALUES(@uid,@cid,@tok,NOW()+INTERVAL '1 day')",
                c =>
                {
                    c.Parameters.AddWithValue("@uid", userId); c.Parameters.AddWithValue("@cid", companyId);
                    c.Parameters.AddWithValue("@tok", $"bound-{suffix}");
                });

            var http = new DefaultHttpContext();
            http.Items[EndpointMappings.AuthCompanyIdItemKey] = companyId;
            http.Items[EndpointMappings.AuthUserIdItemKey] = 1L;
            http.Items[EndpointMappings.AuthRoleItemKey] = "Company Admin";
            http.Items[EndpointMappings.AuthPermissionsItemKey] = new[] { "*" };

            // The exact shape that used to brick the account: role change to an INTERNAL
            // role with NO customerId key in the body.
            var result = await EndpointMappings.UpdateAdminUser(http, userId,
                new Dictionary<string, object?> { ["roleName"] = "Dispatcher" },
                db, new AuditService(db), CancellationToken.None);
            Assert.Equal(StatusCodes.Status200OK, Assert.IsAssignableFrom<IStatusCodeHttpResult>(result).StatusCode);

            var after = await db.QuerySingleAsync(
                "SELECT customer_id, role_name FROM users WHERE id=@id", c => c.Parameters.AddWithValue("@id", userId));
            Assert.Equal("Dispatcher", after!["roleName"]);
            Assert.True(after["customerId"] is null or DBNull,
                "an internal role must not retain a customer binding — the account would lose every internal page.");

            // The change is audited and the in-flight session revoked.
            Assert.Equal(1, await db.ScalarLongAsync(
                "SELECT COUNT(*) FROM audit_logs WHERE company_id=@c AND entity_id=@id AND action_name='user.customer_binding.changed'",
                c => { c.Parameters.AddWithValue("@c", companyId); c.Parameters.AddWithValue("@id", userId); }));
            Assert.Equal(0, await db.ScalarLongAsync(
                "SELECT COUNT(*) FROM user_sessions WHERE user_id=@id", c => c.Parameters.AddWithValue("@id", userId)));
        }
        finally
        {
            await db.ExecuteAsync("DELETE FROM user_sessions WHERE company_id=@c", c => c.Parameters.AddWithValue("@c", companyId));
            await db.ExecuteAsync("DELETE FROM audit_logs WHERE company_id=@c", c => c.Parameters.AddWithValue("@c", companyId));
            await db.ExecuteAsync("DELETE FROM users WHERE company_id=@c", c => c.Parameters.AddWithValue("@c", companyId));
            await db.ExecuteAsync("DELETE FROM customers WHERE company_id=@c", c => c.Parameters.AddWithValue("@c", companyId));
            await db.ExecuteAsync("DELETE FROM companies WHERE id=@c", c => c.Parameters.AddWithValue("@c", companyId));
        }
    }

    /// <summary>
    /// The binding guard keys on the role's PERMISSION SHAPE, never on its name.
    ///
    /// The old <c>roleName.Contains("Portal")</c> test 400'd on the shipped roles
    /// <c>Customer</c> and <c>Customer Viewer</c> — neither contains "Portal" — which are
    /// exactly the roles the SPA's routing layer treats as portal identities. Routing keyed on
    /// permissions while binding keyed on the name: a self-contradictory contract that made
    /// those two roles unbindable.
    ///
    /// It must ALSO keep rejecting internal staff. <c>Customer Service</c> and
    /// <c>CRM &amp; Sales Manager</c> hold <c>customer_portal:view</c> without
    /// <c>dashboard:view</c>, so the naive "portal shape" rule captures them — and binding one
    /// would lock it out of every internal endpoint. The guard mirrors the SHIPPED
    /// sessionRouting.ts predicate, which disqualifies any internal-only direct grant.
    /// </summary>
    [Theory]
    // Genuine portal identities — bindable, and the two the name test used to reject.
    [InlineData("Customer", true)]
    [InlineData("Customer Viewer", true)]
    [InlineData("Customer Portal User", true)]
    // Internal staff that merely holds customer_portal:view — must stay unbindable.
    [InlineData("Customer Service", false)]
    // Wildcard admins and ordinary internal roles — never bindable.
    [InlineData("Company Admin", false)]
    [InlineData("Super Admin", false)]
    [InlineData("Tenant Admin", false)]
    [InlineData("Dispatcher", false)]
    [InlineData("Fleet Manager", false)]
    [InlineData("Read-Only Auditor", false)]
    [InlineData("Driver", false)]
    public async Task CustomerBindableRole_IsDecidedByPermissionShape_NotByTheRoleName(string roleName, bool bindable)
    {
        var db = CreateDatabase();
        var method = typeof(EndpointMappings).GetMethod("RequireCustomerBindableRoleAsync",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)
            ?? throw new InvalidOperationException("RequireCustomerBindableRoleAsync not found");

        // No role row: the resolver falls back to RolePermissionDefaults for the name, which is
        // exactly the shipped backend grant set the frontend mirror is derived from.
        var task = (Task<IResult?>)method.Invoke(null, [db, null, roleName, CancellationToken.None])!;
        var denied = await task;

        if (bindable) Assert.Null(denied);
        else Assert.NotNull(denied);
    }

    /// <summary>Fail closed: a role that resolves to nothing is never bindable.</summary>
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("No Such Role At All")]
    public async Task CustomerBindableRole_FailsClosed_ForAnUnresolvableRole(string? roleName)
    {
        var db = CreateDatabase();
        var method = typeof(EndpointMappings).GetMethod("RequireCustomerBindableRoleAsync",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)!;
        var denied = await (Task<IResult?>)method.Invoke(null, [db, null, roleName, CancellationToken.None])!;
        Assert.NotNull(denied);
    }

    /// <summary>
    /// A PORTAL-to-PORTAL role change keeps the binding: revalidation must clear only when
    /// the new role genuinely cannot hold one.
    /// </summary>
    [Fact]
    public async Task RoleChangeBetweenPortalRoles_KeepsTheBinding()
    {
        var db = CreateDatabase();
        var suffix = Guid.NewGuid().ToString("N")[..12];
        var companyId = await db.InsertAsync(
            @"INSERT INTO companies (company_code, name, industry, timezone, status)
              VALUES (@code, 'Binding Retention Tenant', 'Logistics', 'America/New_York', 'Active')",
            c => c.Parameters.AddWithValue("@code", $"keep-{suffix}"));
        try
        {
            var customerId = await db.InsertAsync(
                "INSERT INTO customers (company_id, customer_code, name, status) VALUES (@cid, @code, 'Kept Customer', 'Active')",
                c => { c.Parameters.AddWithValue("@cid", companyId); c.Parameters.AddWithValue("@code", $"KEEP-{suffix}"); });
            var roleId = await db.InsertAsync(
                "INSERT INTO roles (company_id, name, permissions_json, is_system) VALUES (@cid, @n, '[\"customer_portal:view\"]'::jsonb, FALSE)",
                c => { c.Parameters.AddWithValue("@cid", companyId); c.Parameters.AddWithValue("@n", $"Portal Viewer {suffix}"); });
            Assert.True(roleId > 0);

            var userId = await db.InsertAsync(
                @"INSERT INTO users (company_id, customer_id, role_id, full_name, email, role_name, status, permissions_json)
                  VALUES (@cid, @cust,
                          (SELECT id FROM roles WHERE name='Customer Portal User' AND (company_id IS NULL OR company_id=@cid) ORDER BY company_id NULLS LAST LIMIT 1),
                          'Kept Portal User', @em, 'Customer Portal User', 'Active', '[""customer_portal:view""]'::jsonb)",
                c =>
                {
                    c.Parameters.AddWithValue("@cid", companyId);
                    c.Parameters.AddWithValue("@cust", customerId);
                    c.Parameters.AddWithValue("@em", $"kept-{suffix}@t.example");
                });

            var http = new DefaultHttpContext();
            http.Items[EndpointMappings.AuthCompanyIdItemKey] = companyId;
            http.Items[EndpointMappings.AuthUserIdItemKey] = 1L;
            http.Items[EndpointMappings.AuthRoleItemKey] = "Company Admin";
            http.Items[EndpointMappings.AuthPermissionsItemKey] = new[] { "*" };

            var result = await EndpointMappings.UpdateAdminUser(http, userId,
                new Dictionary<string, object?> { ["roleName"] = $"Portal Viewer {suffix}" },
                db, new AuditService(db), CancellationToken.None);
            Assert.Equal(StatusCodes.Status200OK, Assert.IsAssignableFrom<IStatusCodeHttpResult>(result).StatusCode);

            var after = await db.QuerySingleAsync(
                "SELECT customer_id FROM users WHERE id=@id", c => c.Parameters.AddWithValue("@id", userId));
            Assert.Equal(customerId, Convert.ToInt64(after!["customerId"]));
        }
        finally
        {
            await db.ExecuteAsync("DELETE FROM user_sessions WHERE company_id=@c", c => c.Parameters.AddWithValue("@c", companyId));
            await db.ExecuteAsync("DELETE FROM audit_logs WHERE company_id=@c", c => c.Parameters.AddWithValue("@c", companyId));
            await db.ExecuteAsync("DELETE FROM users WHERE company_id=@c", c => c.Parameters.AddWithValue("@c", companyId));
            await db.ExecuteAsync("DELETE FROM roles WHERE company_id=@c", c => c.Parameters.AddWithValue("@c", companyId));
            await db.ExecuteAsync("DELETE FROM customers WHERE company_id=@c", c => c.Parameters.AddWithValue("@c", companyId));
            await db.ExecuteAsync("DELETE FROM companies WHERE id=@c", c => c.Parameters.AddWithValue("@c", companyId));
        }
    }
}
