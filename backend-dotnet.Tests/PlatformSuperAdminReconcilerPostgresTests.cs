using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Opstrax.Api.Controllers;
using Opstrax.Api.Data;
using Opstrax.Api.Services;

namespace Opstrax.Tests;

// ─────────────────────────────────────────────────────────────────────────────
// The reconciler's DB path against real Postgres. The unit tests pin the decision
// logic; these pin the thing an operator actually depends on — that after a boot
// with PLATFORM_SUPERADMIN_EMAIL/PASSWORD set, the declared credential verifies
// against platform_admins, from every starting state:
//   • the account was never created (production skipped schema init entirely)
//   • the account exists holding a DIFFERENT password (env rotated in Render but
//     never re-read — the "Invalid credentials against the password I configured"
//     lockout this suite exists to prevent regressing)
//   • the account exists but is Disabled / mid-invite / carrying the wrong role
// and equally that an unchanged env does NOT revert a later in-app password change.
//
// Env vars are process-global, so this shares the platform-control-plane collection
// to stay off the parallel path and restores every variable it touches.
// ─────────────────────────────────────────────────────────────────────────────
[Collection("platform-control-plane")]
[Trait("Category", "Integration")]
public class PlatformSuperAdminReconcilerPostgresTests
{
    private static Database CreateDatabase()
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:DefaultConnection"] = TestDb.ConnectionString,
            })
            .Build();
        return new Database(config);
    }

    private static PlatformSuperAdminReconciler Reconciler(Database db) =>
        new(db, NullLogger<PlatformSuperAdminReconciler>.Instance);

    private static string UniqueEmail() => $"bootstrap-{Guid.NewGuid():N}@opstrax.test".ToLowerInvariant();

    // Sets the env the reconciler reads, and restores the previous values on dispose so
    // one test can never leak a bootstrap credential into another.
    private sealed class EnvScope : IDisposable
    {
        private readonly Dictionary<string, string?> _previous = new();

        public EnvScope(string? email, string? password, string? reset = null, string? sync = null)
        {
            Set("PLATFORM_SUPERADMIN_EMAIL", email);
            Set("PLATFORM_SUPERADMIN_PASSWORD", password);
            Set("PLATFORM_SUPERADMIN_RESET", reset);
            Set("PLATFORM_SUPERADMIN_SYNC", sync);
        }

        private void Set(string key, string? value)
        {
            _previous[key] = Environment.GetEnvironmentVariable(key);
            Environment.SetEnvironmentVariable(key, value);
        }

        public void Dispose()
        {
            foreach (var (key, value) in _previous) Environment.SetEnvironmentVariable(key, value);
        }
    }

    private static async Task<Dictionary<string, object?>?> ReadAdminAsync(Database db, string email)
        => await db.QuerySingleAsync(
            @"SELECT a.id, a.password_hash, a.status, a.invite_token_hash, COALESCE(r.role_key,'') AS role_key
              FROM platform_admins a LEFT JOIN platform_roles r ON r.id = a.role_id
              WHERE LOWER(a.email)=LOWER(@e) LIMIT 1",
            c => c.Parameters.AddWithValue("@e", email));

    private static bool PasswordWorks(Dictionary<string, object?>? admin, string password)
        => admin is not null && PlatformEndpoints.VerifyPassword(password, admin["passwordHash"]?.ToString());

    private static async Task EnsureSchemaAsync(Database db) => await new PlatformSchemaService(db).EnsureAsync();

    private static async Task CleanupAsync(Database db, string email)
    {
        await db.ExecuteAsync("DELETE FROM platform_audit_log WHERE LOWER(actor_email)=LOWER(@e)",
            c => c.Parameters.AddWithValue("@e", email));
        await db.ExecuteAsync("DELETE FROM platform_admins WHERE LOWER(email)=LOWER(@e)",
            c => c.Parameters.AddWithValue("@e", email));
    }

    // THE PRODUCTION CASE: schema init is skipped under the restricted role, so the
    // one-time seed never ran and the declared operator simply is not in the roster.
    // A boot must mint them — Active, super admin, env password in force.
    [Fact]
    public async Task Creates_The_Declared_Operator_When_The_Roster_Has_No_Such_Account()
    {
        var db = CreateDatabase();
        await EnsureSchemaAsync(db);
        var email = UniqueEmail();
        const string password = "Bootstrap2026!x";

        try
        {
            using (new EnvScope(email, password))
                await Reconciler(db).ReconcileAsync();

            var admin = await ReadAdminAsync(db, email);
            Assert.NotNull(admin);
            Assert.True(PasswordWorks(admin, password), "the declared env password must verify after boot");
            Assert.Equal("Active", admin!["status"]?.ToString());
            Assert.Equal("platform_super_admin", admin["roleKey"]?.ToString());
            Assert.Null(admin["inviteTokenHash"]);
        }
        finally { await CleanupAsync(db, email); }
    }

    // THE LOCKOUT: the account exists with some other password (rotated in Render but never
    // re-read, or seeded long ago). No flag is armed. The env credential must win.
    [Fact]
    public async Task Applies_The_Env_Password_When_It_Never_Reached_The_Database()
    {
        var db = CreateDatabase();
        await EnsureSchemaAsync(db);
        var email = UniqueEmail();
        const string stale = "StaleSeeded2024!";
        const string intended = "RenderRotated2026!";

        try
        {
            using (new EnvScope(email, stale)) await Reconciler(db).ReconcileAsync();
            Assert.True(PasswordWorks(await ReadAdminAsync(db, email), stale));

            // Simulate the real-world gap: the DB hash predates any fingerprint record.
            await db.ExecuteAsync("DELETE FROM platform_audit_log WHERE LOWER(actor_email)=LOWER(@e)",
                c => c.Parameters.AddWithValue("@e", email));

            using (new EnvScope(email, intended)) await Reconciler(db).ReconcileAsync();

            var admin = await ReadAdminAsync(db, email);
            Assert.True(PasswordWorks(admin, intended), "the password configured in env must be the one that works");
            Assert.False(PasswordWorks(admin, stale));
        }
        finally { await CleanupAsync(db, email); }
    }

    // Rotating PLATFORM_SUPERADMIN_PASSWORD reaches the DB on the next boot, and the old
    // credential stops working.
    [Fact]
    public async Task Applies_A_Rotated_Env_Credential_On_The_Next_Boot()
    {
        var db = CreateDatabase();
        await EnsureSchemaAsync(db);
        var email = UniqueEmail();
        const string first = "FirstCredential1!";
        const string rotated = "RotatedCredential2!";

        try
        {
            using (new EnvScope(email, first)) await Reconciler(db).ReconcileAsync();
            using (new EnvScope(email, rotated)) await Reconciler(db).ReconcileAsync();

            var admin = await ReadAdminAsync(db, email);
            Assert.True(PasswordWorks(admin, rotated));
            Assert.False(PasswordWorks(admin, first));
        }
        finally { await CleanupAsync(db, email); }
    }

    // Running on EVERY boot is only safe if an unchanged env leaves a later in-app password
    // change alone. Two extra boots must not revert it.
    [Fact]
    public async Task Does_Not_Revert_A_SelfService_Password_Change_While_Env_Is_Unchanged()
    {
        var db = CreateDatabase();
        await EnsureSchemaAsync(db);
        var email = UniqueEmail();
        const string envPassword = "EnvDeclared2026!";
        const string chosenInApp = "OperatorChose2026!";

        try
        {
            using (new EnvScope(email, envPassword)) await Reconciler(db).ReconcileAsync();

            await db.ExecuteAsync("UPDATE platform_admins SET password_hash=@h WHERE LOWER(email)=LOWER(@e)",
                c =>
                {
                    c.Parameters.AddWithValue("@h", PlatformSchemaService.HashPassword(chosenInApp));
                    c.Parameters.AddWithValue("@e", email);
                });

            using (new EnvScope(email, envPassword)) await Reconciler(db).ReconcileAsync();
            using (new EnvScope(email, envPassword)) await Reconciler(db).ReconcileAsync();

            var admin = await ReadAdminAsync(db, email);
            Assert.True(PasswordWorks(admin, chosenInApp), "an in-app password change must survive redeploys");
            Assert.False(PasswordWorks(admin, envPassword));
        }
        finally { await CleanupAsync(db, email); }
    }

    // ...unless the operator explicitly asks for it. PLATFORM_SUPERADMIN_RESET=true is the
    // escape hatch for a forgotten in-app password.
    [Fact]
    public async Task Force_Flag_Reinstates_The_Env_Password_Over_An_InApp_Change()
    {
        var db = CreateDatabase();
        await EnsureSchemaAsync(db);
        var email = UniqueEmail();
        const string envPassword = "EnvDeclared2026!";
        const string forgotten = "ForgottenInApp2026!";

        try
        {
            using (new EnvScope(email, envPassword)) await Reconciler(db).ReconcileAsync();

            await db.ExecuteAsync("UPDATE platform_admins SET password_hash=@h WHERE LOWER(email)=LOWER(@e)",
                c =>
                {
                    c.Parameters.AddWithValue("@h", PlatformSchemaService.HashPassword(forgotten));
                    c.Parameters.AddWithValue("@e", email);
                });

            using (new EnvScope(email, envPassword, reset: "true")) await Reconciler(db).ReconcileAsync();

            Assert.True(PasswordWorks(await ReadAdminAsync(db, email), envPassword));
        }
        finally { await CleanupAsync(db, email); }
    }

    // Drift that blocks sign-in regardless of the password is repaired without touching a
    // password the operator owns.
    [Fact]
    public async Task Repairs_A_Disabled_Mid_Invite_Wrong_Role_Account_Without_Touching_The_Password()
    {
        var db = CreateDatabase();
        await EnsureSchemaAsync(db);
        var email = UniqueEmail();
        const string envPassword = "EnvDeclared2026!";
        const string chosenInApp = "OperatorChose2026!";

        try
        {
            using (new EnvScope(email, envPassword)) await Reconciler(db).ReconcileAsync();

            await db.ExecuteAsync(
                @"UPDATE platform_admins
                  SET password_hash=@h, status='Disabled', invite_token_hash='pending', role_id=NULL
                  WHERE LOWER(email)=LOWER(@e)",
                c =>
                {
                    c.Parameters.AddWithValue("@h", PlatformSchemaService.HashPassword(chosenInApp));
                    c.Parameters.AddWithValue("@e", email);
                });

            using (new EnvScope(email, envPassword)) await Reconciler(db).ReconcileAsync();

            var admin = await ReadAdminAsync(db, email);
            Assert.Equal("Active", admin!["status"]?.ToString());
            Assert.Equal("platform_super_admin", admin["roleKey"]?.ToString());
            Assert.Null(admin["inviteTokenHash"]);
            Assert.True(PasswordWorks(admin, chosenInApp), "a repair must not rewrite an env-unrotated password");
        }
        finally { await CleanupAsync(db, email); }
    }

    // The super-admin role and its wildcard grant are created if the platform RBAC seed never
    // ran, so a minted admin is never left permission-less (403 on every screen).
    [Fact]
    public async Task Ensures_The_Super_Admin_Role_Carries_The_Wildcard_Grant()
    {
        var db = CreateDatabase();
        await EnsureSchemaAsync(db);
        var email = UniqueEmail();

        try
        {
            using (new EnvScope(email, "RoleGrantCheck2026!")) await Reconciler(db).ReconcileAsync();

            var grants = await db.ScalarLongAsync(
                @"SELECT COUNT(*) FROM platform_role_permissions rp
                  JOIN platform_roles r ON r.id = rp.role_id
                  WHERE r.role_key='platform_super_admin' AND rp.permission_key='platform:*'");
            Assert.Equal(1, grants);
        }
        finally { await CleanupAsync(db, email); }
    }

    // A weak bootstrap password is refused rather than installed.
    [Fact]
    public async Task Refuses_To_Install_A_Weak_Bootstrap_Password()
    {
        var db = CreateDatabase();
        await EnsureSchemaAsync(db);
        var email = UniqueEmail();

        try
        {
            using (new EnvScope(email, "short1")) await Reconciler(db).ReconcileAsync();
            Assert.Null(await ReadAdminAsync(db, email));
        }
        finally { await CleanupAsync(db, email); }
    }

    // The opt-out keeps the database authoritative — nothing is created.
    [Fact]
    public async Task Sync_Off_Leaves_The_Roster_Untouched()
    {
        var db = CreateDatabase();
        await EnsureSchemaAsync(db);
        var email = UniqueEmail();

        try
        {
            using (new EnvScope(email, "OptedOutCredential1!", sync: "off")) await Reconciler(db).ReconcileAsync();
            Assert.Null(await ReadAdminAsync(db, email));
        }
        finally { await CleanupAsync(db, email); }
    }
}
