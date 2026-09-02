using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Opstrax.Api.Controllers;
using Opstrax.Api.Data;
using Opstrax.Api.DTOs;
using Opstrax.Api.Services;
using Xunit;

namespace Opstrax.Tests;

/// <summary>
/// DEF-021 (code half) — role MEMBERSHIP is what the middleware and the role-card user
/// counts read (users.role_id), yet several provisioning paths inserted users with only a
/// role_name. Stage87 backfilled the data; these tests keep the code from re-creating the
/// drift: every INSERT INTO users must supply role_id, and a user provisioned through the
/// platform path must be countable on /api/admin/roles.
/// </summary>
public class UserProvisioningRoleMembershipTests
{
    /// <summary>
    /// Source contract: every "INSERT INTO users" in the production backend supplies a
    /// role_id column. The allowlist below is for inserts that legitimately have no role
    /// concept — currently NONE exist; do not add entries without a written justification.
    /// </summary>
    [Fact]
    public void EveryUsersInsert_SuppliesRoleId()
    {
        var allowlistedContexts = Array.Empty<string>();

        var root = RepoRoot();
        var sources = Directory.GetFiles(Path.Combine(root, "backend-dotnet", "Controllers"), "*.cs")
            .Concat(Directory.GetFiles(Path.Combine(root, "backend-dotnet", "Services"), "*.cs"))
            .OrderBy(f => f);

        var violations = new List<string>();
        foreach (var file in sources)
        {
            var source = File.ReadAllText(file);
            foreach (Match m in Regex.Matches(source, @"INSERT INTO users\s*\(", RegexOptions.IgnoreCase))
            {
                var valuesAt = source.IndexOf("VALUES", m.Index, StringComparison.OrdinalIgnoreCase);
                var columnList = valuesAt > m.Index ? source[m.Index..valuesAt] : source[m.Index..Math.Min(m.Index + 400, source.Length)];
                if (columnList.Contains("role_id", StringComparison.OrdinalIgnoreCase)) continue;
                var context = $"{Path.GetFileName(file)}:{source[..m.Index].Count(c => c == '\n') + 1}";
                if (allowlistedContexts.Contains(context)) continue;
                violations.Add(context);
            }
        }

        Assert.True(violations.Count == 0,
            "INSERT INTO users without role_id (the role card and middleware count MEMBERSHIP " +
            "via users.role_id — a name-only insert creates an uncountable user):\n  " +
            string.Join("\n  ", violations));
    }

    /// <summary>
    /// Naming the role_id column is NOT the contract — RESOLVING it is.
    ///
    /// <see cref="EveryUsersInsert_SuppliesRoleId"/> is satisfied by
    /// <c>role_id = (SELECT id FROM roles WHERE name=@roleName …)</c>, which writes NULL
    /// whenever the role name is absent from the catalog. That is exactly how the demo
    /// seeder kept re-creating DEF-021 rows ('Maintenance Manager' and 'Safety Auditor'
    /// are not global catalog roles) while this suite stayed green. A provisioning path
    /// must therefore resolve the role BEFORE inserting and fail loudly when it cannot,
    /// never hand a nullable column a nullable subselect.
    /// </summary>
    [Fact]
    public void NoUsersInsert_ResolvesRoleIdFromANullableSubselect()
    {
        // Known-unfixed sites, owned by a different work packet (PlatformEndpoints.cs is not
        // this packet's file). Both are the same DEF-021 class and should be converted to the
        // resolve-first-and-throw pattern DemoTenantSeeder now uses:
        //   PlatformEndpoints.cs ~:1478 — tenant-user provisioning, role name comes from the
        //     request body, so an unknown/renamed role writes NULL role_id silently.
        //   PlatformEndpoints.cs ~:2826 — hard-coded 'Company Admin'; lower risk (stage77
        //     bootstraps that protected role) but still unguarded if the row is ever renamed.
        // Do NOT add entries here without a written justification and an owner.
        var allowlistedContexts = new[]
        {
            "PlatformEndpoints.cs",
        };

        var root = RepoRoot();
        var sources = Directory.GetFiles(Path.Combine(root, "backend-dotnet", "Controllers"), "*.cs")
            .Concat(Directory.GetFiles(Path.Combine(root, "backend-dotnet", "Services"), "*.cs"))
            .OrderBy(f => f);

        var violations = new List<string>();
        foreach (var file in sources)
        {
            var source = File.ReadAllText(file);
            foreach (Match m in Regex.Matches(source, @"INSERT INTO users\s*\(", RegexOptions.IgnoreCase))
            {
                // The statement body: from the INSERT to the end of the SQL string literal.
                var end = source.IndexOf("RETURNING", m.Index, StringComparison.OrdinalIgnoreCase);
                if (end < 0) end = Math.Min(m.Index + 1200, source.Length);
                var statement = source[m.Index..end];

                // A bare `(SELECT … FROM roles …)` in the VALUES list resolves to NULL for an
                // unknown role name. COALESCE(...) / a pre-resolved @parameter are both fine.
                foreach (Match sub in Regex.Matches(statement, @"\(\s*SELECT\s+id\s+FROM\s+roles\b", RegexOptions.IgnoreCase))
                {
                    var prefix = statement[..sub.Index];
                    var lastOpen = prefix.LastIndexOf("COALESCE", StringComparison.OrdinalIgnoreCase);
                    var guarded = lastOpen >= 0 && prefix.Length - lastOpen < 40;
                    if (guarded) continue;
                    if (allowlistedContexts.Contains(Path.GetFileName(file))) continue;
                    violations.Add($"{Path.GetFileName(file)}:{source[..(m.Index + sub.Index)].Count(c => c == '\n') + 1}");
                }
            }
        }

        Assert.True(violations.Count == 0,
            "INSERT INTO users whose role_id comes from an unguarded `(SELECT id FROM roles …)` subselect. " +
            "An unknown role name silently writes NULL, producing an uncountable user and re-creating DEF-021. " +
            "Resolve the role id first and throw when it does not exist:\n  " +
            string.Join("\n  ", violations));
    }

    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !Directory.Exists(Path.Combine(dir.FullName, "backend-dotnet")))
            dir = dir.Parent;
        Assert.NotNull(dir);
        return dir!.FullName;
    }
}

/// <summary>
/// DEF-021 Postgres fact: provisioning through the platform tenant-user insert (the exact
/// SQL the handler executes, extracted from source) produces a user whose role_id resolves,
/// and /api/admin/roles reports a userCount equal to the active roster of that role.
/// </summary>
public class UserProvisioningRoleMembershipPostgresTests
{
    private static Database CreateDatabase()
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["ConnectionStrings:DefaultConnection"] = TestDb.ConnectionString })
            .Build();
        return new Database(config);
    }

    [Fact]
    public async Task PlatformProvisionedUser_HasRoleId_AndCountsOnTheRoleCard()
    {
        // Extract the platform provisioning INSERT from source so the test executes what
        // production executes (a drifted copy here would be worthless).
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (root is not null && !Directory.Exists(Path.Combine(root.FullName, "backend-dotnet")))
            root = root.Parent;
        Assert.NotNull(root);
        var platformSource = File.ReadAllText(Path.Combine(root!.FullName, "backend-dotnet", "Controllers", "PlatformEndpoints.cs"));
        var marker = platformSource.IndexOf("INSERT INTO users (company_id, role_id, full_name, email, role_name, status)", StringComparison.Ordinal);
        Assert.True(marker >= 0, "platform provisioning INSERT not found (did the column list change?)");
        var literalEnd = platformSource.IndexOf('"', marker);
        var insertSql = platformSource[marker..literalEnd].Replace("\"\"", "\"");
        Assert.Contains("RETURNING id", insertSql);

        var db = CreateDatabase();
        var companyId = await db.InsertAsync(
            @"INSERT INTO companies (company_code, name, industry, timezone, status)
              VALUES (@code, 'Role Membership Tenant', 'Logistics', 'America/New_York', 'Active')",
            c => c.Parameters.AddWithValue("@code", $"rolemem-{Guid.NewGuid():N}"));
        try
        {
            var email = $"rolemem-{Guid.NewGuid():N}@t.example";
            var userId = await db.InsertAsync(insertSql, c =>
            {
                c.Parameters.AddWithValue("@cid", companyId);
                c.Parameters.AddWithValue("@name", "Provisioned Dispatcher");
                c.Parameters.AddWithValue("@email", email);
                c.Parameters.AddWithValue("@role", "Dispatcher");
            });

            var row = await db.QuerySingleAsync("SELECT role_id, role_name, status FROM users WHERE id=@id",
                c => c.Parameters.AddWithValue("@id", userId));
            Assert.NotNull(row);
            Assert.False(row!["roleId"] is null or DBNull, "provisioned user must carry role membership (role_id)");
            var roleId = Convert.ToInt64(row["roleId"]);

            // The role-card FILTER requires status='Active' — Pending users deliberately do
            // not count (the register documents this). Activate, then the card must count 1.
            await db.ExecuteAsync("UPDATE users SET status='Active' WHERE id=@id", c => c.Parameters.AddWithValue("@id", userId));

            var http = new DefaultHttpContext();
            http.Items[EndpointMappings.AuthUserIdItemKey] = userId;
            http.Items[EndpointMappings.AuthCompanyIdItemKey] = companyId;
            http.Items[EndpointMappings.AuthRoleItemKey] = "Company Admin";
            http.Items[EndpointMappings.AuthPermissionsItemKey] = new[] { "roles:view" };

            var result = await EndpointMappings.AdminRoles(http, db, new AuditService(db), CancellationToken.None);
            var api = Assert.IsType<ApiResponse<object>>((result as IValueHttpResult)?.Value);
            var roles = Assert.IsAssignableFrom<List<Dictionary<string, object?>>>(api.Data);

            var dispatcherCard = roles.Single(r => Convert.ToInt64(r["id"]) == roleId);
            var cardCount = Convert.ToInt64(dispatcherCard["userCount"]);
            var activeRoster = await db.ScalarLongAsync(
                "SELECT COUNT(*) FROM users WHERE company_id=@cid AND role_id=@rid AND status='Active'",
                c => { c.Parameters.AddWithValue("@cid", companyId); c.Parameters.AddWithValue("@rid", roleId); });

            Assert.Equal(activeRoster, cardCount);
            Assert.Equal(1, cardCount);
        }
        finally
        {
            await db.ExecuteAsync("DELETE FROM users WHERE company_id=@c", c => c.Parameters.AddWithValue("@c", companyId));
            await db.ExecuteAsync("DELETE FROM companies WHERE id=@c", c => c.Parameters.AddWithValue("@c", companyId));
        }
    }

    /// <summary>
    /// The demo seeder is a PROVISIONING PATH, and it kept re-creating DEF-021 rows after
    /// stage87 had cleaned them up: two of its personas ('Maintenance Manager',
    /// 'Safety Auditor') name roles the global catalog does not ship, so the inline role_id
    /// subselect resolved NULL for every fresh tenant. Every seeded user must now carry role
    /// membership, and the persona's EFFECTIVE permissions must be unchanged by that — the
    /// tenant-local role's json has to equal the persona's own grants, because resolving
    /// role_id makes ResolveEffectivePermissionsAsync read the role instead of the user.
    /// </summary>
    [Fact]
    public async Task DemoSeededTenant_HasNoUserWithoutRoleMembership()
    {
        var db = CreateDatabase();
        var suffix = Guid.NewGuid().ToString("N")[..10].ToUpperInvariant();
        var companyCode = $"ROLEMEM-{suffix}";
        var seeder = new DemoTenantSeeder(db);
        var result = await seeder.SeedAsync(companyCode, $"Role Membership Demo {suffix}");
        var companyId = result.CompanyId;
        try
        {
            Assert.True(companyId > 0);

            var orphans = await db.QueryAsync(
                "SELECT email, role_name FROM users WHERE company_id=@c AND role_id IS NULL ORDER BY email",
                c => c.Parameters.AddWithValue("@c", companyId));
            Assert.True(orphans.Count == 0,
                "demo-seeded users without role_id (uncountable on the role card, and exactly the DEF-021 rows " +
                "stage87 exists to eliminate):\n  " + string.Join("\n  ",
                    orphans.Select(o => $"{o.GetValueOrDefault("email")} (role_name={o.GetValueOrDefault("roleName")})")));

            // No seeded user may borrow another tenant's role.
            var foreign = await db.QueryAsync(
                @"SELECT u.email FROM users u JOIN roles r ON r.id = u.role_id
                   WHERE u.company_id=@c AND r.company_id IS NOT NULL AND r.company_id <> u.company_id",
                c => c.Parameters.AddWithValue("@c", companyId));
            Assert.True(foreign.Count == 0,
                "demo user bound to another tenant's role:\n  " + string.Join("\n  ",
                    foreign.Select(m => m.GetValueOrDefault("email")?.ToString())));

            // For the personas the seeder had to create TENANT-LOCAL roles for (their names
            // are absent from the global catalog), the role's grant set must equal the
            // persona's own json — otherwise resolving role_id silently reshapes the persona,
            // because ResolveEffectivePermissionsAsync then reads the role instead of the user.
            var reshaped = await db.QueryAsync(
                @"SELECT u.email
                    FROM users u JOIN roles r ON r.id = u.role_id
                   WHERE u.company_id=@c AND r.company_id = u.company_id
                     AND NOT (r.permissions_json @> u.permissions_json
                              AND u.permissions_json @> r.permissions_json)",
                c => c.Parameters.AddWithValue("@c", companyId));
            Assert.True(reshaped.Count == 0,
                "demo persona whose tenant-local role grants differ from its own permissions_json — " +
                "resolving role_id silently reshapes the persona:\n  " + string.Join("\n  ",
                    reshaped.Select(m => m.GetValueOrDefault("email")?.ToString())));

            // The two off-catalog personas exist as tenant-local roles (not as global rows).
            foreach (var localRole in new[] { "Maintenance Manager", "Safety Auditor" })
            {
                Assert.Equal(1, await db.ScalarLongAsync(
                    "SELECT COUNT(*) FROM roles WHERE company_id=@c AND name=@n",
                    c => { c.Parameters.AddWithValue("@c", companyId); c.Parameters.AddWithValue("@n", localRole); }));
            }
        }
        finally
        {
            await db.ExecuteAsync("DELETE FROM users WHERE company_id=@c", c => c.Parameters.AddWithValue("@c", companyId));
            await db.ExecuteAsync("DELETE FROM roles WHERE company_id=@c", c => c.Parameters.AddWithValue("@c", companyId));
        }
    }
}
