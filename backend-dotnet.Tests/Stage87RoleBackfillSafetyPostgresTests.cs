using Microsoft.Extensions.Configuration;
using Npgsql;
using Opstrax.Api.Controllers;
using Opstrax.Api.Data;
using Xunit;

namespace Opstrax.Tests;

/// <summary>
/// Stage87 must NEVER widen a live account.
///
/// EndpointMappings.ResolveEffectivePermissionsAsync picks the permission SOURCE on
/// role_id — <c>ParsePermissionKeys(roleId &gt; 0 ? rolePermissionsJson : userPermissionsJson)</c>
/// — so writing role_id makes the user's own (deliberately narrower) permissions_json stop
/// being consulted and the ROLE's grant set take over. Permissions re-resolve per request,
/// so an unconditional backfill escalates in-flight sessions the instant it commits, with no
/// audit row and no session revocation. Worst case: a trimmed-json user carrying
/// role_name='Company Admin' resolves straight to ["*"].
///
/// These tests execute the REAL migration file (not a paraphrase) inside a transaction that
/// is rolled back, so they assert the shipped behaviour and leave no trace on the test DB.
/// </summary>
public class Stage87RoleBackfillSafetyPostgresTests
{
    [Fact]
    public async Task Backfill_WidensNobody_BackfillsOnlyHalfProvisionedAndProvablyNoOpRows()
    {
        var migration = MigrationBody();
        await using var connection = new NpgsqlConnection(TestDb.ConnectionString);
        await connection.OpenAsync();
        await using var tx = await connection.BeginTransactionAsync();

        var suffix = Guid.NewGuid().ToString("N")[..12];
        var companyId = await ScalarAsync<long>(connection, tx,
            "INSERT INTO companies(company_code,name,industry,status) VALUES(@code,'Stage87 Safety','Logistics','Active') RETURNING id",
            c => c.Parameters.AddWithValue("@code", $"S87-{suffix}".ToUpperInvariant()));

        // A role whose grant set is strictly WIDER than the user's own json.
        var roleId = await ScalarAsync<long>(connection, tx,
            @"INSERT INTO roles(company_id,name,permissions_json)
              VALUES(@cid,@name,'[""dashboard:view"",""dispatch:view"",""dispatch:manage"",""users:delete""]'::jsonb)
              RETURNING id",
            c => { c.Parameters.AddWithValue("@cid", companyId); c.Parameters.AddWithValue("@name", $"S87 Wide {suffix}"); });

        // 1. The escalation case: role_id NULL, role_name resolves, own grants NARROWER.
        var narrowUserId = await ScalarAsync<long>(connection, tx,
            @"INSERT INTO users(company_id,full_name,email,role_name,status,permissions_json,role_id)
              VALUES(@cid,'Narrow User',@email,@role,'Active','[""dashboard:view""]'::jsonb,NULL) RETURNING id",
            c =>
            {
                c.Parameters.AddWithValue("@cid", companyId);
                c.Parameters.AddWithValue("@email", $"narrow-{suffix}@s87.test");
                c.Parameters.AddWithValue("@role", $"S87 Wide {suffix}");
            });
        await ExecuteAsync(connection, tx,
            "INSERT INTO user_sessions(user_id,company_id,session_token,expires_at) VALUES(@uid,@cid,@tok,NOW()+INTERVAL '1 day')",
            c =>
            {
                c.Parameters.AddWithValue("@uid", narrowUserId); c.Parameters.AddWithValue("@cid", companyId);
                c.Parameters.AddWithValue("@tok", $"narrow-{suffix}");
            });

        // 2. The genuine DEF-021 case: role_name resolves, NO usable per-user grants.
        var halfUserId = await ScalarAsync<long>(connection, tx,
            @"INSERT INTO users(company_id,full_name,email,role_name,status,permissions_json,role_id)
              VALUES(@cid,'Half User',@email,@role,'Active','[]'::jsonb,NULL) RETURNING id",
            c =>
            {
                c.Parameters.AddWithValue("@cid", companyId);
                c.Parameters.AddWithValue("@email", $"half-{suffix}@s87.test");
                c.Parameters.AddWithValue("@role", $"S87 Wide {suffix}");
            });
        await ExecuteAsync(connection, tx,
            "INSERT INTO user_sessions(user_id,company_id,session_token,expires_at) VALUES(@uid,@cid,@tok,NOW()+INTERVAL '1 day')",
            c =>
            {
                c.Parameters.AddWithValue("@uid", halfUserId); c.Parameters.AddWithValue("@cid", companyId);
                c.Parameters.AddWithValue("@tok", $"half-{suffix}");
            });

        // 3. Provably no-op: own grants EXACTLY equal the role's (case/whitespace noise on purpose).
        var exactUserId = await ScalarAsync<long>(connection, tx,
            @"INSERT INTO users(company_id,full_name,email,role_name,status,permissions_json,role_id)
              VALUES(@cid,'Exact User',@email,@role,'Active',
                     '[""dashboard:view"",""  Dispatch:View  "",""dispatch:manage"",""users:delete""]'::jsonb,NULL) RETURNING id",
            c =>
            {
                c.Parameters.AddWithValue("@cid", companyId);
                c.Parameters.AddWithValue("@email", $"exact-{suffix}@s87.test");
                c.Parameters.AddWithValue("@role", $"S87 Wide {suffix}");
            });

        // Run the SHIPPED migration.
        await ExecuteAsync(connection, tx, migration, _ => { });

        // ── The load-bearing assertion: the narrower user was NOT widened. ──────────
        var narrowRoleId = await ScalarAsync<object>(connection, tx,
            "SELECT role_id FROM users WHERE id=@id", c => c.Parameters.AddWithValue("@id", narrowUserId));
        Assert.True(narrowRoleId is null or DBNull,
            "stage87 must NOT set role_id on a user whose own permissions_json is narrower than the role — " +
            "ResolveEffectivePermissionsAsync would silently swap the source and widen every in-flight session.");

        // Its effective permissions are therefore untouched, and its session survives
        // (nothing changed, so there is nothing to revoke).
        var narrowPermissions = await ResolveEffectiveAsync(connection, tx, narrowUserId);
        Assert.Equal(new[] { "dashboard:view" }, narrowPermissions);
        Assert.Equal(1, await ScalarAsync<long>(connection, tx,
            "SELECT COUNT(*) FROM user_sessions WHERE user_id=@id", c => c.Parameters.AddWithValue("@id", narrowUserId)));

        // It is reported for operator adjudication, with the exact delta.
        var review = await ScalarAsync<object>(connection, tx,
            "SELECT would_gain FROM stage87_role_backfill_review WHERE user_id=@id",
            c => c.Parameters.AddWithValue("@id", narrowUserId));
        var wouldGain = Assert.IsType<string[]>(review);
        Assert.Equal(
            new[] { "dispatch:manage", "dispatch:view", "users:delete" },
            wouldGain.OrderBy(x => x, StringComparer.Ordinal).ToArray());

        // ── The half-provisioned user IS repaired, and its stale session revoked. ───
        Assert.Equal(roleId, await ScalarAsync<long>(connection, tx,
            "SELECT role_id FROM users WHERE id=@id", c => c.Parameters.AddWithValue("@id", halfUserId)));
        Assert.Equal(0, await ScalarAsync<long>(connection, tx,
            "SELECT COUNT(*) FROM user_sessions WHERE user_id=@id", c => c.Parameters.AddWithValue("@id", halfUserId)));
        Assert.Equal(0, await ScalarAsync<long>(connection, tx,
            "SELECT COUNT(*) FROM stage87_role_backfill_review WHERE user_id=@id",
            c => c.Parameters.AddWithValue("@id", halfUserId)));

        // ── The provably-equal user is repaired too: resolving cannot change anything. ──
        Assert.Equal(roleId, await ScalarAsync<long>(connection, tx,
            "SELECT role_id FROM users WHERE id=@id", c => c.Parameters.AddWithValue("@id", exactUserId)));

        await tx.RollbackAsync();
    }

    /// <summary>
    /// ROUND-2: a NON-ARRAY permissions_json must never be classified "provably no-op".
    ///
    /// Any non-array shape (object / string / number / boolean) normalises to '[]' in the
    /// migration's token CASE, so user_tokens = {} — while ALSO failing the
    /// half_provisioned test, which only accepts NULL, JSON null, an empty array, or a
    /// string that trims to '' / '[]'. Against a role whose own grant set is likewise
    /// empty, `{} &lt;@ {} AND {} &lt;@ {}` was TRUE, so the row was backfilled. A user
    /// carrying role_name='Company Admin' and a legacy JSON OBJECT would then re-resolve
    /// through role_id, hit the runtime `Count == 0 -&gt; RolePermissionDefaults` fallback,
    /// and land on wildcard '*' — the exact escalation the migration header promises to
    /// prevent. Every shape must be held for operator review instead.
    /// </summary>
    [Theory]
    [InlineData("object", "{\"legacy\":\"shape\"}")]
    [InlineData("string", "\"dashboard:view\"")]
    [InlineData("number", "42")]
    [InlineData("boolean", "true")]
    [InlineData("emptyobject", "{}")]
    public async Task Backfill_HoldsNonArrayPermissionsJson_ForOperatorReview(string shape, string json)
    {
        var migration = MigrationBody();
        await using var connection = new NpgsqlConnection(TestDb.ConnectionString);
        await connection.OpenAsync();
        await using var tx = await connection.BeginTransactionAsync();

        var suffix = Guid.NewGuid().ToString("N")[..12];
        var companyId = await ScalarAsync<long>(connection, tx,
            "INSERT INTO companies(company_code,name,industry,status) VALUES(@code,'Stage87 Shape','Logistics','Active') RETURNING id",
            c => c.Parameters.AddWithValue("@code", $"S87X-{suffix}".ToUpperInvariant()));

        // A role with NO grants of its own — this is what makes the empty-vs-empty
        // containment test pass and turns the misclassification into a wildcard escalation.
        var roleName = $"S87 Empty {suffix}";
        await ScalarAsync<long>(connection, tx,
            "INSERT INTO roles(company_id,name,permissions_json) VALUES(@cid,@name,'[]'::jsonb) RETURNING id",
            c => { c.Parameters.AddWithValue("@cid", companyId); c.Parameters.AddWithValue("@name", roleName); });

        var userId = await ScalarAsync<long>(connection, tx,
            @"INSERT INTO users(company_id,full_name,email,role_name,status,permissions_json,role_id)
              VALUES(@cid,'Shape User',@email,@role,'Active',@json::jsonb,NULL) RETURNING id",
            c =>
            {
                c.Parameters.AddWithValue("@cid", companyId);
                c.Parameters.AddWithValue("@email", $"{shape}-{suffix}@s87.test");
                c.Parameters.AddWithValue("@role", roleName);
                c.Parameters.AddWithValue("@json", json);
            });

        await ExecuteAsync(connection, tx, migration, _ => { });

        var roleIdAfter = await ScalarAsync<object>(connection, tx,
            "SELECT role_id FROM users WHERE id=@id", c => c.Parameters.AddWithValue("@id", userId));
        Assert.True(roleIdAfter is null or DBNull,
            $"stage87 must NOT backfill role_id for a '{shape}' permissions_json ({json}) — it is neither " +
            "half-provisioned nor provably no-op, and resolving role_id would send the user to the " +
            "RolePermissionDefaults fallback (wildcard for any admin-sounding role_name).");

        Assert.Equal(1, await ScalarAsync<long>(connection, tx,
            "SELECT COUNT(*) FROM stage87_role_backfill_review WHERE user_id=@id",
            c => c.Parameters.AddWithValue("@id", userId)));

        await tx.RollbackAsync();
    }

    /// <summary>
    /// ROUND-2: role_permissions must be in the FORCE-RLS lift list.
    ///
    /// The token-comparison query unions role_permissions.permission_key into role_tokens.
    /// role_permissions is FORCE ROW LEVEL SECURITY with policies for opstrax_app /
    /// opstrax_system only, so under a NON-SUPERUSER owner — the Neon/production shape this
    /// guard exists for — that subquery returns zero rows and role_tokens is silently
    /// truncated. A role whose grants live partly in role_permissions then compares EQUAL to
    /// a narrower user, is classified "provably no-op", and the user gains the missing
    /// permissions on the next request.
    ///
    /// Our test role is a SUPERUSER, which has BYPASSRLS and therefore cannot reproduce the
    /// truncation — that is exactly why this shipped. What IS assertable here is the
    /// migration's own contract: role_permissions is lifted and restored alongside the
    /// others, and its grants are counted into role_tokens (so a role with grants ONLY in
    /// role_permissions is not mistaken for an empty role). The escalation itself was
    /// reproduced by execution on a scratch database owned by a NOSUPERUSER NOBYPASSRLS
    /// role; see the packet report.
    /// </summary>
    [Fact]
    public async Task Backfill_CountsRolePermissionsRows_AndGuardsThatTableToo()
    {
        var migration = MigrationBody();
        Assert.Contains("ALTER TABLE public.role_permissions NO FORCE ROW LEVEL SECURITY", migration);
        Assert.Contains("ALTER TABLE public.role_permissions FORCE ROW LEVEL SECURITY", migration);

        await using var connection = new NpgsqlConnection(TestDb.ConnectionString);
        await connection.OpenAsync();
        await using var tx = await connection.BeginTransactionAsync();

        var before = await ForceFlagsAsync(connection, tx);

        var suffix = Guid.NewGuid().ToString("N")[..12];
        var companyId = await ScalarAsync<long>(connection, tx,
            "INSERT INTO companies(company_code,name,industry,status) VALUES(@code,'Stage87 Split','Logistics','Active') RETURNING id",
            c => c.Parameters.AddWithValue("@code", $"S87S-{suffix}".ToUpperInvariant()));

        // The dangerous shape: the role's grants are SPLIT across permissions_json and
        // role_permissions. If role_permissions is invisible, role_tokens truncates to
        // {dashboard:view} and this user compares EQUAL — a widening dressed as a no-op.
        var roleName = $"S87 Split {suffix}";
        var roleId = await ScalarAsync<long>(connection, tx,
            @"INSERT INTO roles(company_id,name,permissions_json)
              VALUES(@cid,@name,'[""dashboard:view""]'::jsonb) RETURNING id",
            c => { c.Parameters.AddWithValue("@cid", companyId); c.Parameters.AddWithValue("@name", roleName); });
        await ExecuteAsync(connection, tx,
            "INSERT INTO role_permissions(role_id,permission_key) VALUES(@rid,'users:delete')",
            c => c.Parameters.AddWithValue("@rid", roleId));

        var userId = await ScalarAsync<long>(connection, tx,
            @"INSERT INTO users(company_id,full_name,email,role_name,status,permissions_json,role_id)
              VALUES(@cid,'Split User',@email,@role,'Active','[""dashboard:view""]'::jsonb,NULL) RETURNING id",
            c =>
            {
                c.Parameters.AddWithValue("@cid", companyId);
                c.Parameters.AddWithValue("@email", $"split-{suffix}@s87.test");
                c.Parameters.AddWithValue("@role", roleName);
            });

        await ExecuteAsync(connection, tx, migration, _ => { });

        var roleIdAfter = await ScalarAsync<object>(connection, tx,
            "SELECT role_id FROM users WHERE id=@id", c => c.Parameters.AddWithValue("@id", userId));
        Assert.True(roleIdAfter is null or DBNull,
            "role_permissions grants must count into role_tokens — otherwise a role whose grants are split " +
            "between permissions_json and role_permissions looks equal to a narrower user and gets backfilled.");

        var review = await ScalarAsync<object>(connection, tx,
            "SELECT would_gain FROM stage87_role_backfill_review WHERE user_id=@id",
            c => c.Parameters.AddWithValue("@id", userId));
        Assert.Equal(new[] { "users:delete" }, Assert.IsType<string[]>(review));

        // FORCE flags restored to the exact prior state, role_permissions included.
        Assert.Equal(before, await ForceFlagsAsync(connection, tx));

        await tx.RollbackAsync();
    }

    [Fact]
    public async Task Backfill_IsIdempotent_AndRestoresForceRowLevelSecurity()
    {
        var migration = MigrationBody();
        await using var connection = new NpgsqlConnection(TestDb.ConnectionString);
        await connection.OpenAsync();
        await using var tx = await connection.BeginTransactionAsync();

        var forcedBefore = await ForceFlagsAsync(connection, tx);
        await ExecuteAsync(connection, tx, migration, _ => { });
        var firstPass = await ScalarAsync<long>(connection, tx,
            "SELECT COUNT(*) FROM users WHERE role_id IS NULL", _ => { });
        await ExecuteAsync(connection, tx, migration, _ => { });
        var secondPass = await ScalarAsync<long>(connection, tx,
            "SELECT COUNT(*) FROM users WHERE role_id IS NULL", _ => { });

        Assert.Equal(firstPass, secondPass);
        Assert.Equal(forcedBefore, await ForceFlagsAsync(connection, tx));
        await tx.RollbackAsync();
    }

    // The migration's own BEGIN/COMMIT is stripped so the test can run it inside a
    // transaction it rolls back — the DO block, guards and predicates are otherwise
    // executed verbatim, so a drifted copy here is impossible.
    private static string MigrationBody()
    {
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (root is not null && !Directory.Exists(Path.Combine(root.FullName, "database", "migrations")))
            root = root.Parent;
        Assert.NotNull(root);
        var path = Path.Combine(root!.FullName, "database", "migrations", "2026_08_22_stage87_user_role_id_backfill.sql");
        Assert.True(File.Exists(path), $"stage87 migration not found at {path}");
        var sql = File.ReadAllText(path);

        // Fail loudly if the file stops being a single guarded transaction.
        Assert.Contains("FORCE ROW LEVEL SECURITY", sql, StringComparison.Ordinal);
        Assert.Contains("stage87_role_backfill_review", sql, StringComparison.Ordinal);

        var begin = sql.IndexOf("BEGIN;", StringComparison.Ordinal);
        Assert.True(begin >= 0, "stage87 migration must open with BEGIN;");
        var commit = sql.LastIndexOf("COMMIT;", StringComparison.Ordinal);
        Assert.True(commit > begin, "stage87 migration must close with COMMIT;");
        return sql[(begin + "BEGIN;".Length)..commit];
    }

    private static async Task<string[]> ResolveEffectiveAsync(NpgsqlConnection connection, NpgsqlTransaction tx, long userId)
    {
        var config = new ConfigurationBuilder().AddInMemoryCollection(
            new Dictionary<string, string?> { ["ConnectionStrings:DefaultConnection"] = TestDb.ConnectionString }).Build();
        var db = new Database(config);

        // Read the row through the open transaction so uncommitted fixture state is visible.
        await using var command = new NpgsqlCommand(
            @"SELECT COALESCE(u.role_id,0) role_id, u.role_name, r.permissions_json role_json, u.permissions_json user_json
              FROM users u LEFT JOIN roles r ON r.id=u.role_id WHERE u.id=@id", connection, tx);
        command.Parameters.AddWithValue("@id", userId);
        await using var reader = await command.ExecuteReaderAsync();
        Assert.True(await reader.ReadAsync());
        var roleId = reader.GetInt64(0);
        var roleName = reader.GetString(1);
        var roleJson = reader.IsDBNull(2) ? null : reader.GetValue(2);
        var userJson = reader.IsDBNull(3) ? null : reader.GetValue(3);
        await reader.CloseAsync();

        var resolved = await EndpointMappings.ResolveEffectivePermissionsAsync(
            roleId, roleName, roleJson, userJson, db, CancellationToken.None);
        return [.. resolved.OrderBy(x => x, StringComparer.Ordinal)];
    }

    private static async Task<string> ForceFlagsAsync(NpgsqlConnection connection, NpgsqlTransaction tx)
    {
        await using var command = new NpgsqlCommand(
            @"SELECT string_agg(relname||'='||relforcerowsecurity::text, ',' ORDER BY relname)
              FROM pg_class WHERE relname IN ('users','roles','role_permissions','user_sessions')", connection, tx);
        return (await command.ExecuteScalarAsync())?.ToString() ?? "";
    }

    private static async Task ExecuteAsync(NpgsqlConnection connection, NpgsqlTransaction tx, string sql, Action<NpgsqlCommand> bind)
    {
        await using var command = new NpgsqlCommand(sql, connection, tx);
        bind(command);
        await command.ExecuteNonQueryAsync();
    }

    private static async Task<T> ScalarAsync<T>(NpgsqlConnection connection, NpgsqlTransaction tx, string sql, Action<NpgsqlCommand> bind)
    {
        await using var command = new NpgsqlCommand(sql, connection, tx);
        bind(command);
        var value = await command.ExecuteScalarAsync();
        if (typeof(T) == typeof(object)) return (T)(value ?? DBNull.Value);
        return (T)Convert.ChangeType(value!, typeof(T));
    }
}
