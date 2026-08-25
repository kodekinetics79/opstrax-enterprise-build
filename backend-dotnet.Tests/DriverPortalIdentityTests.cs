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
        // DVIR submit is authorized only by the dedicated driver:self route. Granting the
        // back-office permission would let a driver call the generic maintenance endpoint.
        Assert.DoesNotContain("maintenance:create", effective);
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
    /// AUD-003 makes Dispatcher authoritative: historical umbrella grants must be removed,
    /// leaving exactly the reviewed action-level set shared by code and seed data.
    /// </summary>
    [Fact]
    public async Task DispatcherReconcile_IsAuthoritative_AndStripsLegacyUmbrellas()
    {
        var db = CreateDatabase();
        await EnsureCoreBootstrapAsync(db);
        await new RolePermissionReconciler(db, NullLogger<RolePermissionReconciler>.Instance).ReconcileAsync();

        var dispatcher = await EffectiveRoleGrantsAsync(db, "Dispatcher");

        Assert.DoesNotContain("jobs:view", dispatcher);
        Assert.DoesNotContain("jobs:manage", dispatcher);
        Assert.DoesNotContain("dispatch:manage", dispatcher);
        Assert.DoesNotContain("fleet:view", dispatcher);
        Assert.Equal(
            EndpointMappings.RolePermissionDefaults["Dispatcher"].OrderBy(x => x, StringComparer.Ordinal),
            dispatcher.OrderBy(x => x, StringComparer.Ordinal));
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


/// <summary>
/// DEF-026 — the driver portal must DEGRADE, not die, and must FAIL CLOSED on revoked
/// driver lifecycle states. Two halves:
///  • Source contract: DriverMe / DriverHos guard their optional companion tables with
///    to_regclass and degrade 42P01/42501/42703 to the data-unavailable branch.
///  • Postgres facts against the REAL resolution SQL (extracted from the handler source,
///    so the test cannot drift from what production executes).
/// </summary>
public class DriverPortalHardeningPostgresTests
{
    private static readonly string LocalConnectionString = TestDb.ConnectionString;

    private static Database CreateDatabase()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["ConnectionStrings:DefaultConnection"] = LocalConnectionString })
            .Build();
        return new Database(configuration);
    }

    private static string MappingsSource()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !Directory.Exists(Path.Combine(dir.FullName, "backend-dotnet")))
            dir = dir.Parent;
        Assert.NotNull(dir);
        return File.ReadAllText(Path.Combine(dir!.FullName, "backend-dotnet", "Controllers", "EndpointMappings.cs"));
    }

    private static string Block(string source, string start, string end)
    {
        var s = source.IndexOf(start, StringComparison.Ordinal);
        Assert.True(s >= 0, $"marker not found: {start}");
        var e = source.IndexOf(end, s, StringComparison.Ordinal);
        Assert.True(e > s, $"end marker not found: {end}");
        return source[s..e];
    }

    [Fact]
    public void DriverMeAndDriverHos_DegradeOnSchemaOrGrantDrift_InsteadOf500()
    {
        var source = MappingsSource();

        var driverMe = Block(source, "private static async Task<IResult> DriverMe(", "private static async Task<IResult> DriverAssignments(");
        Assert.Contains("to_regclass('public.hos_records')", driverMe, StringComparison.Ordinal);
        Assert.Contains("to_regclass('public.coaching_tasks')", driverMe, StringComparison.Ordinal);
        Assert.Contains("PostgresErrorCodes.UndefinedTable", driverMe, StringComparison.Ordinal);
        Assert.Contains("PostgresErrorCodes.InsufficientPrivilege", driverMe, StringComparison.Ordinal);
        Assert.Contains("PostgresErrorCodes.UndefinedColumn", driverMe, StringComparison.Ordinal);

        var driverHos = Block(source, "private static async Task<IResult> DriverHos(", "private static async Task<IResult> DriverEarnings(");
        Assert.Contains("to_regclass('public.hos_records')", driverHos, StringComparison.Ordinal);
        Assert.Contains("PostgresErrorCodes.InsufficientPrivilege", driverHos, StringComparison.Ordinal);
        // The unavailable branch survives as the degrade target.
        Assert.Contains("dataAvailable = false", driverHos, StringComparison.Ordinal);
    }

    [Fact]
    public void DriverIdentityResolution_FailsClosed_ForRevokedLifecycleStatuses()
    {
        var source = MappingsSource();
        var resolver = Block(source, "private static async Task<long> GetDriverIdFromAuthAsync(", "private static IResult DriverIdentityNotFound()");
        Assert.Contains("NOT IN ('inactive','suspended','deleted','terminated','retired')", resolver, StringComparison.Ordinal);
        // The comparison must TRIM: status is untrimmed free text, so LOWER() alone lets
        // 'Inactive ' keep portal access. The explicit character set matters too — 1-arg
        // BTRIM strips spaces only, leaving a tab/CR/LF-padded status alive.
        Assert.Contains(@"LOWER(BTRIM(COALESCE(status,''), E' \t\r\n\f\v'))", resolver, StringComparison.Ordinal);
    }

    /// <summary>
    /// Runs the EXACT resolution SQL the handler executes (extracted from source) against
    /// a seeded driver: Available resolves; Suspended/Inactive do not; and with HOS data
    /// present the DriverMe HOS projection returns it (the positive path that used to 500).
    /// </summary>
    [Fact]
    public async Task BoundDriver_WithHosData_ResolvesAndReadsHos_ButLosesPortalWhenSuspended()
    {
        var source = MappingsSource();
        var resolver = Block(source, "private static async Task<long> GetDriverIdFromAuthAsync(", "private static IResult DriverIdentityNotFound()");
        var sqlStart = resolver.IndexOf("@\"SELECT id FROM drivers", StringComparison.Ordinal);
        Assert.True(sqlStart >= 0, "resolution SQL literal not found");
        var sqlEnd = resolver.IndexOf('"', sqlStart + 2);
        var resolutionSql = resolver[(sqlStart + 2)..sqlEnd];

        var db = CreateDatabase();
        var companyId = await db.InsertAsync(
            @"INSERT INTO companies (company_code, name, industry, timezone, status)
              VALUES (@code, 'Driver Portal Hardening Tenant', 'Logistics', 'America/New_York', 'Active')",
            c => c.Parameters.AddWithValue("@code", $"dph-{Guid.NewGuid():N}"));
        try
        {
            var userId = await db.InsertAsync(
                @"INSERT INTO users (company_id, full_name, email, role_name, status)
                  VALUES (@cid, 'Portal Driver', @em, 'Driver', 'Active')",
                c => { c.Parameters.AddWithValue("@cid", companyId); c.Parameters.AddWithValue("@em", $"dph-{Guid.NewGuid():N}@t.example"); });
            var driverId = await db.InsertAsync(
                @"INSERT INTO drivers (company_id, driver_code, full_name, status, user_id)
                  VALUES (@cid, @code, 'Portal Driver', 'Available', @uid)",
                c => { c.Parameters.AddWithValue("@cid", companyId); c.Parameters.AddWithValue("@code", $"DPH-{Guid.NewGuid():N}"[..16]); c.Parameters.AddWithValue("@uid", userId); });
            await db.ExecuteAsync(
                @"INSERT INTO hos_records (company_id, driver_id, shift_date, remaining_drive_hours, remaining_shift_hours, hos_status)
                  VALUES (@cid, @did, CURRENT_DATE, 7.5, 9.0, 'Compliant')",
                c => { c.Parameters.AddWithValue("@cid", companyId); c.Parameters.AddWithValue("@did", driverId); });

            async Task<long?> ResolveAsync()
            {
                var row = await db.QuerySingleAsync(resolutionSql,
                    c => { c.Parameters.AddWithValue("@uid", userId); c.Parameters.AddWithValue("@cid", companyId); });
                return row?["id"] is not null and not DBNull ? Convert.ToInt64(row["id"]) : null;
            }

            // Positive path: an Available bound driver resolves, and the HOS row is readable.
            Assert.Equal(driverId, await ResolveAsync());
            var hos = await db.QuerySingleAsync(
                "SELECT remaining_drive_hours, remaining_shift_hours, hos_status FROM hos_records WHERE driver_id=@did AND company_id=@cid ORDER BY shift_date DESC LIMIT 1",
                c => { c.Parameters.AddWithValue("@did", driverId); c.Parameters.AddWithValue("@cid", companyId); });
            Assert.NotNull(hos);
            Assert.Equal("Compliant", hos!["hosStatus"]);

            // Fail closed: Suspended / Inactive lifecycle statuses lose the portal…
            // …INCLUDING untrimmed values. drivers.status is free-text varchar with no CHECK
            // constraint, so an import or a UI field can store 'Inactive ' / ' Suspended';
            // without BTRIM those slip past the blocklist and an ex-driver keeps a working app.
            foreach (var revoked in new[]
            {
                "Suspended", "Inactive",
                "Inactive ", " Inactive", "  Suspended  ", "TERMINATED ", "\tRetired\n", "Deleted ",
            })
            {
                await db.ExecuteAsync("UPDATE drivers SET status=@s WHERE id=@id",
                    c => { c.Parameters.AddWithValue("@s", revoked); c.Parameters.AddWithValue("@id", driverId); });
                Assert.True(await ResolveAsync() is null,
                    $"a driver whose status is '{revoked}' must lose portal access (revoked lifecycle status, untrimmed)");
            }

            // …and operational statuses keep it.
            foreach (var operational in new[] { "Available", "On Route", "Delayed" })
            {
                await db.ExecuteAsync("UPDATE drivers SET status=@s WHERE id=@id",
                    c => { c.Parameters.AddWithValue("@s", operational); c.Parameters.AddWithValue("@id", driverId); });
                Assert.Equal(driverId, await ResolveAsync());
            }
        }
        finally
        {
            await db.ExecuteAsync("DELETE FROM hos_records WHERE company_id=@c", c => c.Parameters.AddWithValue("@c", companyId));
            await db.ExecuteAsync("DELETE FROM drivers WHERE company_id=@c", c => c.Parameters.AddWithValue("@c", companyId));
            await db.ExecuteAsync("DELETE FROM users WHERE company_id=@c", c => c.Parameters.AddWithValue("@c", companyId));
            await db.ExecuteAsync("DELETE FROM companies WHERE id=@c", c => c.Parameters.AddWithValue("@c", companyId));
        }
    }
}
