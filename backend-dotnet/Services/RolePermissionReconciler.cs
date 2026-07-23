using Opstrax.Api.Controllers;
using Opstrax.Api.Data;

namespace Opstrax.Api.Services;

/// <summary>
/// Keeps the built-in ("system") roles in the DB in step with
/// <see cref="EndpointMappings.RolePermissionDefaults"/>, which is the single source of
/// truth for what a built-in role may do.
///
/// WHY THIS EXISTS — the P0 it permanently closes:
/// The permission middleware (Program.cs) resolves a user's grants as
/// `roles.permissions_json ∪ role_permissions`, and only falls back to
/// RolePermissionDefaults when that set is EMPTY. The seeded `Driver` role was
/// `["driver:portal","jobs:view","dvir:manage"]` — non-empty, and containing none of the
/// permissions the driver endpoints actually require. So the code default
/// (`["driver:self", …]`) never loaded, every `/api/driver/*` route 403'd, and the whole
/// Driver Portal was unreachable for every driver in every tenant. The DB silently won a
/// fight the code did not know it was in.
///
/// Editing the seed file would have fixed the four tenants we have today and nothing else;
/// the drift would simply recur the next time someone added a permission in C#. Instead the
/// invariant is enforced on every boot, in every environment.
///
/// DESIGN NOTES
///  • Runs as DML (no DDL), so — unlike the schema services — it runs even when schema init
///    is skipped (restricted `opstrax_app` role under RLS enforcement, i.e. production).
///    That is the whole point: prod is exactly where the drift was fatal. Executes under a
///    system scope so RLS does not hide the global (company_id IS NULL) role rows.
///  • ADDITIVE, never subtractive — except for the explicitly-listed <see cref="Retired"/>
///    keys. A blanket "make the DB match the code" would have stripped Dispatcher of
///    `jobs:view`, `map:view`, `fleet:view` and `dispatch:manage`, which are NOT in that
///    role's code default but ARE live via the semantic alias tables in FoundationServices.
///    Adding a missing grant can only restore intended access; removing one can cause an
///    outage. So removals require proof, one key at a time.
///  • Idempotent: a converged DB produces zero writes.
///  • Built-in roles are already immutable through the admin API (`UpdateAdminRole` filters
///    `is_system=FALSE`), so there is no admin-authored state here for this to trample.
///    Tenant-authored custom roles are never touched.
/// </summary>
public sealed class RolePermissionReconciler(Database db, ILogger<RolePermissionReconciler> logger)
{
    /// <summary>
    /// Permission keys seeded historically that are enforced by NOTHING — verified by
    /// exhaustive grep across the solution: zero RequirePermission sites, zero entries in
    /// either alias table (EndpointMappings.PermissionAliases,
    /// FoundationServices.SemanticPermissionAliases), zero frontend gates.
    ///
    /// They are removed because they are actively harmful: `driver:portal` on the Driver
    /// role *looks* like the grant that opens the driver portal, which is precisely why the
    /// real defect went undiagnosed. The canonical key is `driver:self`.
    ///
    /// Do not add to this list without the same proof.
    /// </summary>
    private static readonly string[] Retired = ["driver:portal", "dvir:manage"];

    /// <summary>
    /// Roles whose grant set is AUTHORITATIVE — the code default is the exact, single source of
    /// truth, so a grant present in the DB but absent from the default is REVOKED (not preserved
    /// additively). Reserved for locked-down, isolated roles where over-granting is a security
    /// problem, not a convenience. The Driver role is portal-only: a driver must never accumulate
    /// back-office grants (dispatch/shipments/etc.), so it is reconciled to exactly its default.
    /// Broad staff roles are intentionally NOT here — for them additive reconciliation stands, so a
    /// tenant's bespoke extra grant is never silently stripped.
    /// </summary>
    private static readonly HashSet<string> Authoritative = new(StringComparer.OrdinalIgnoreCase) { "Driver" };

    public async Task ReconcileAsync(CancellationToken ct = default)
    {
        try
        {
            await db.RunInSystemScopeAsync(() => ReconcileCoreAsync(ct), ct);
        }
        catch (Exception ex)
        {
            // Never take the API down over this. A failure here means roles stay as they
            // are (the pre-existing behaviour), so it is degraded, not broken — but it is
            // loud, because a silent failure is what created this bug in the first place.
            logger.LogError(new EventId(0, "role_reconcile_failed"), ex,
                "System-role permission reconciliation FAILED — built-in roles may be missing grants " +
                "(e.g. drivers locked out of the driver portal). API is still serving.");
        }
    }

    private async Task ReconcileCoreAsync(CancellationToken ct)
    {
        var roles = await db.QueryAsync(
            "SELECT id, name, permissions_json FROM roles WHERE is_system = TRUE ORDER BY id",
            ct: ct);

        var changedRoles = 0;
        var addedGrants = 0;
        var removedGrants = 0;

        foreach (var role in roles)
        {
            var roleId = Convert.ToInt64(role["id"]);
            var name = role["name"]?.ToString() ?? string.Empty;

            // A system role with no code default (e.g. "Operations Manager") is left exactly
            // as the DB has it. We only assert the invariant where we actually declare one.
            if (!EndpointMappings.RolePermissionDefaults.TryGetValue(name, out var codeDefault))
                continue;

            var current = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var key in EndpointMappings.ParsePermissionKeys(role["permissionsJson"]))
                current.Add(key);

            var grantRows = await db.QueryAsync(
                "SELECT permission_key FROM role_permissions WHERE role_id=@roleId",
                c => c.Parameters.AddWithValue("@roleId", roleId), ct);
            var currentGrantRows = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var row in grantRows)
            {
                var key = row.GetValueOrDefault("permissionKey")?.ToString()?.Trim();
                if (!string.IsNullOrWhiteSpace(key))
                {
                    current.Add(key);
                    currentGrantRows.Add(key);
                }
            }

            // desired = everything the DB already grants, plus everything the code declares,
            // minus the proven-dead keys. Additive by construction (see class remarks) — EXCEPT for
            // authoritative roles (e.g. Driver), whose desired set is exactly the code default so
            // stray back-office grants are revoked, keeping the role truly isolated.
            var authoritative = Authoritative.Contains(name);
            var desired = new HashSet<string>(authoritative ? codeDefault : current, StringComparer.OrdinalIgnoreCase);
            if (!authoritative) desired.UnionWith(codeDefault);
            desired.ExceptWith(Retired);

            var toAdd = desired.Except(currentGrantRows, StringComparer.OrdinalIgnoreCase).ToArray();
            var toRemove = currentGrantRows.Except(desired, StringComparer.OrdinalIgnoreCase).ToArray();
            var jsonDrifted = !desired.SetEquals(current);

            if (toAdd.Length == 0 && toRemove.Length == 0 && !jsonDrifted) continue;

            foreach (var key in toRemove)
            {
                await db.ExecuteAsync(
                    "DELETE FROM role_permissions WHERE role_id=@roleId AND permission_key=@key",
                    c =>
                    {
                        c.Parameters.AddWithValue("@roleId", roleId);
                        c.Parameters.AddWithValue("@key", key);
                    }, ct);
                removedGrants++;
            }

            foreach (var key in toAdd)
            {
                await db.ExecuteAsync(
                    @"INSERT INTO role_permissions (role_id, permission_key)
                      VALUES (@roleId, @key)
                      ON CONFLICT DO NOTHING",
                    c =>
                    {
                        c.Parameters.AddWithValue("@roleId", roleId);
                        c.Parameters.AddWithValue("@key", key);
                    }, ct);
                addedGrants++;
            }

            // Mirror the same set onto roles.permissions_json. Both columns are read by the
            // middleware (it unions them), and the admin UI renders the JSON column — so if
            // they disagree, the UI lies about what a role can do.
            var payload = System.Text.Json.JsonSerializer.Serialize(
                desired.OrderBy(static k => k, StringComparer.Ordinal).ToArray());
            await db.ExecuteAsync(
                "UPDATE roles SET permissions_json=@payload::jsonb WHERE id=@roleId",
                c =>
                {
                    c.Parameters.AddWithValue("@roleId", roleId);
                    c.Parameters.AddWithValue("@payload", payload);
                }, ct);

            changedRoles++;
            logger.LogInformation(new EventId(0, "role_reconciled"),
                "System role '{Role}' (id {RoleId}) reconciled: +{Added} grant(s) {AddedKeys}, -{Removed} retired {RemovedKeys}",
                name, roleId, toAdd.Length, string.Join(",", toAdd), toRemove.Length, string.Join(",", toRemove));
        }

        if (changedRoles == 0)
        {
            logger.LogInformation(new EventId(0, "role_reconcile_noop"),
                "System-role permissions already in sync with RolePermissionDefaults ({Count} built-in roles checked).",
                roles.Count);
        }
        else
        {
            logger.LogWarning(new EventId(0, "role_reconcile_applied"),
                "System-role permissions RECONCILED: {Roles} role(s) changed, {Added} grant(s) added, {Removed} retired key(s) removed. " +
                "Drift between the DB and RolePermissionDefaults has been corrected.",
                changedRoles, addedGrants, removedGrants);
        }
    }

}
