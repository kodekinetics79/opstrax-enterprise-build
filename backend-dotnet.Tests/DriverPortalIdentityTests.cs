using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Opstrax.Api.Controllers;
using Opstrax.Api.Data;
using Opstrax.Api.Services;
using Xunit;

namespace Opstrax.Tests;

/// <summary>
/// Guards the three defects that made the driver portal unusable for every driver in every
/// tenant, and — more importantly — guards the CLASS of defect each one belongs to.
///
/// Note what already existed before this file: DriverWorkflowTests asserts
/// `RolePermissionDefaults["Driver"]` contains "driver:self", and it passed, continuously,
/// the entire time the portal was 100% broken. It asserted the C# dictionary. The DATABASE
/// said something else, the database is what the middleware reads, and no test ever compared
/// the two. A test that cannot fail when the product is broken is worse than no test — it is
/// a source of false confidence. Every assertion below is made against the real DB.
/// </summary>
/// Named *Postgres* per the repo convention: CI's unit lane filters on
/// `FullyQualifiedName!~Postgres`, so DB-backed suites run in the SIT environment against a
/// live database rather than in unit CI. These assertions are meaningless without a real DB —
/// asserting against anything else is precisely the mistake that let this bug ship.
public class RolePermissionReconcilerPostgresTests
{
    private static readonly string LocalConnectionString = TestDb.ConnectionString;

    /// <summary>
    /// THE regression test for the P0. The seeded Driver role was
    /// ["driver:portal","jobs:view","dvir:manage"] — a non-empty set that contained none of
    /// the permissions the driver endpoints require. Because the middleware only falls back
    /// to RolePermissionDefaults when the resolved set is EMPTY, the correct code default
    /// never loaded and every /api/driver/* route 403'd. This asserts what the middleware
    /// will actually read.
    /// </summary>
    [Fact]
    public async Task Reconcile_GrantsDriverRole_ThePermissionsTheDriverEndpointsRequire()
    {
        var db = CreateDatabase();
        await EnsureCoreBootstrapAsync(db);
        await new RolePermissionReconciler(db, NullLogger<RolePermissionReconciler>.Instance).ReconcileAsync();

        var effective = await EffectiveRoleGrantsAsync(db, "Driver");

        // driver:self gates all ~20 /api/driver/* routes.
        Assert.Contains("driver:self", effective);
        // DVIR submit delegates to MaintInspectionCreate, which requires maintenance:create.
        // Without it a driver could reach the DVIR form and still be denied on submit.
        Assert.Contains("maintenance:create", effective);
        // The "New Assignment" push is worthless if the driver cannot open their alerts.
        Assert.Contains("notifications:view", effective);
    }

    /// <summary>
    /// The DB is what gets enforced; the code default is what we intend. If they can diverge
    /// silently, the P0 comes straight back. This is the drift guard proper: for EVERY
    /// built-in role we declare a default for, the DB must grant at least that.
    /// </summary>
    [Fact]
    public async Task Reconcile_LeavesNoBuiltInRole_MissingItsDeclaredPermissions()
    {
        var db = CreateDatabase();
        await EnsureCoreBootstrapAsync(db);
        await new RolePermissionReconciler(db, NullLogger<RolePermissionReconciler>.Instance).ReconcileAsync();

        var systemRoles = await db.QueryAsync("SELECT name FROM roles WHERE is_system = TRUE");
        var checkedRoles = 0;

        foreach (var role in systemRoles)
        {
            var name = role["name"]?.ToString() ?? "";
            if (!EndpointMappings.RolePermissionDefaults.TryGetValue(name, out var declared)) continue;

            var effective = await EffectiveRoleGrantsAsync(db, name);
            var missing = declared.Except(effective, StringComparer.OrdinalIgnoreCase).ToArray();

            Assert.True(missing.Length == 0,
                $"Built-in role '{name}' is missing {missing.Length} declared permission(s) in the DB: " +
                $"{string.Join(", ", missing)}. The middleware reads the DB, not RolePermissionDefaults — " +
                "so these are NOT granted at runtime, whatever the C# says.");
            checkedRoles++;
        }

        Assert.True(checkedRoles > 0, "No built-in roles were checked — the guard is not actually running.");
    }

    /// <summary>
    /// `driver:portal` is enforced by nothing (zero RequirePermission sites, zero alias
    /// entries). Its presence on the Driver role is what made the real defect invisible: the
    /// role LOOKED like it opened the driver portal. Retiring it must stick.
    /// </summary>
    [Fact]
    public async Task Reconcile_RemovesRetiredPermissionKeys_ThatEnforceNothing()
    {
        var db = CreateDatabase();
        await EnsureCoreBootstrapAsync(db);
        await new RolePermissionReconciler(db, NullLogger<RolePermissionReconciler>.Instance).ReconcileAsync();

        var effective = await EffectiveRoleGrantsAsync(db, "Driver");

        Assert.DoesNotContain("driver:portal", effective);
        Assert.DoesNotContain("dvir:manage", effective);
    }

    /// <summary>Runs on every boot, so a second pass must be a no-op, not a duplicate-key storm.</summary>
    [Fact]
    public async Task Reconcile_IsIdempotent()
    {
        var db = CreateDatabase();
        await EnsureCoreBootstrapAsync(db);
        var reconciler = new RolePermissionReconciler(db, NullLogger<RolePermissionReconciler>.Instance);

        await reconciler.ReconcileAsync();
        var first = await EffectiveRoleGrantsAsync(db, "Driver");

        await reconciler.ReconcileAsync();
        var second = await EffectiveRoleGrantsAsync(db, "Driver");

        Assert.Equal(first.OrderBy(x => x, StringComparer.Ordinal), second.OrderBy(x => x, StringComparer.Ordinal));

        var duplicates = await db.ScalarLongAsync(
            @"SELECT COUNT(*) FROM (
                SELECT role_id, permission_key FROM role_permissions
                GROUP BY role_id, permission_key HAVING COUNT(*) > 1
              ) dupes");
        Assert.Equal(0, duplicates);
    }

    /// <summary>
    /// The reconciler must never REMOVE a grant that is live but not in the code default.
    /// Dispatcher's `jobs:view` / `map:view` / `fleet:view` / `dispatch:manage` are exactly
    /// that — absent from RolePermissionDefaults["Dispatcher"], but enforced via the semantic
    /// alias tables. A "make the DB match the code" reconciler would have silently broken the
    /// Dispatcher role while fixing the Driver one.
    /// </summary>
    [Fact]
    public async Task Reconcile_IsAdditive_AndDoesNotStripLiveGrantsAbsentFromCodeDefaults()
    {
        var db = CreateDatabase();
        await EnsureCoreBootstrapAsync(db);
        await new RolePermissionReconciler(db, NullLogger<RolePermissionReconciler>.Instance).ReconcileAsync();

        var dispatcher = await EffectiveRoleGrantsAsync(db, "Dispatcher");

        Assert.Contains("jobs:view", dispatcher);        // → job.read via FoundationServices
        Assert.Contains("dispatch:manage", dispatcher);  // 19 enforcement sites
        Assert.Contains("fleet:view", dispatcher);       // 12 enforcement sites
        // …and it still gained everything the code declares.
        foreach (var declared in EndpointMappings.RolePermissionDefaults["Dispatcher"])
            Assert.Contains(declared, dispatcher);
    }

    /// <summary>The union the middleware actually resolves: roles.permissions_json ∪ role_permissions.</summary>
    private static async Task<HashSet<string>> EffectiveRoleGrantsAsync(Database db, string roleName)
    {
        var role = await db.QuerySingleAsync(
            "SELECT id, permissions_json FROM roles WHERE name=@name AND is_system=TRUE LIMIT 1",
            c => c.Parameters.AddWithValue("@name", roleName));
        Assert.NotNull(role);

        var grants = EndpointMappings.ParsePermissionKeys(role!["permissionsJson"])
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var rows = await db.QueryAsync(
            "SELECT permission_key FROM role_permissions WHERE role_id=@id",
            c => c.Parameters.AddWithValue("@id", Convert.ToInt64(role["id"])));
        foreach (var row in rows)
        {
            var key = row.GetValueOrDefault("permissionKey")?.ToString()?.Trim();
            if (!string.IsNullOrWhiteSpace(key)) grants.Add(key);
        }

        return grants;
    }

    private static Database CreateDatabase()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["ConnectionStrings:DefaultConnection"] = LocalConnectionString })
            .Build();
        return new Database(configuration);
    }

    private static async Task EnsureCoreBootstrapAsync(Database db)
        => await new CoreSchemaService(db, NullLogger<CoreSchemaService>.Instance).EnsureAsync();
}

/// <summary>
/// The DVIR out-of-service interlock. A driver reports a critical brake defect; the vehicle
/// must be grounded. It never was: the check compared `Severity == "critical"` ordinally while
/// the driver app posts "Critical". Worse, CapitalizeSeverity stored severity='Critical'
/// correctly — so the DVIR record LOOKED right in every report while the safety action it
/// exists to trigger silently never fired.
/// </summary>
public class DvirSeverityInterlockTests
{
    [Theory]
    [InlineData("Critical")]   // what the driver app actually sends
    [InlineData("critical")]
    [InlineData("CRITICAL")]
    [InlineData(" Critical ")]
    public void CriticalDefect_GroundsTheVehicle_RegardlessOfCasing(string severity)
    {
        Assert.True(EndpointMappings.IsCriticalSeverity(severity),
            $"Severity '{severity}' must ground the vehicle. An ordinal comparison here means a " +
            "driver can report a critical defect and be cleared to depart.");
    }

    [Theory]
    [InlineData("Major")]
    [InlineData("Minor")]
    [InlineData("Low")]
    [InlineData(null)]
    [InlineData("")]
    public void NonCriticalDefect_DoesNotGroundTheVehicle(string? severity)
    {
        Assert.False(EndpointMappings.IsCriticalSeverity(severity));
    }
}
