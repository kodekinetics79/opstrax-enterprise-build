using System.Reflection;
using Microsoft.AspNetCore.Http;
using Opstrax.Api.Controllers;
using Xunit;

namespace Opstrax.Tests;

/// <summary>
/// A GOVERNANCE VIEW TIER IS ONE-WAY.
///
/// EndpointMappings.PermissionAliases expands the tokens a session HOLDS
/// (RequirePermission: <c>permissions.SelectMany(PermissionAliases)</c>), so every token
/// listed beside a view token in that table is GRANTED to holders of the view token.
/// Listing <c>users:manage</c> beside <c>users:view</c> therefore did not encode
/// "manage implies view" — it encoded "view implies MANAGE".
///
/// Measured on the shipped closure before the round-2 fix:
///   settings:view → settings:manage   LEAK   (gates POST /api/settings/api-keys,
///                                             PUT /api/settings/webhook,
///                                             POST /api/settings/webhook/rotate-secret,
///                                             POST /api/settings/api-keys/{id}/revoke)
///   users:view    → users:manage      LEAK   (gates POST/PUT/DELETE /api/feature-flags)
///   roles:view    → users:manage      LEAK   (same surface)
/// A Read-only Auditor — a role whose entire purpose is that it cannot change anything —
/// could mint tenant API keys, repoint the tenant webhook at an attacker URL, rotate the
/// signing secret and flip feature flags.
///
/// These tests EXECUTE the shipped RequirePermission path. The alias tables can look
/// correct in source and still resolve differently, which is how the leak survived a
/// green suite: the existing guards were hand-written case lists, not enumerations.
/// </summary>
public class GovernanceViewTierTests
{
    /// <summary>
    /// Enumerated, not sampled: every (view token, write token) pair in the governance
    /// triad, in both the colon and dot spelling.
    /// </summary>
    public static TheoryData<string, string> ViewToWritePairs()
    {
        var tiers = new Dictionary<string, string[]>
        {
            ["settings:view"] = ["settings:manage", "settings:update"],
            ["users:view"] = ["users:manage", "users:create", "users:update", "users:delete"],
            ["roles:view"] = ["users:manage", "roles:manage", "roles:create", "roles:update"],
        };

        var data = new TheoryData<string, string>();
        foreach (var (view, writes) in tiers)
            foreach (var write in writes)
            {
                data.Add(view, write);
                data.Add(view.Replace(':', '.'), write);
                data.Add(view, write.Replace(':', '.'));
                data.Add(view.Replace(':', '.'), write.Replace(':', '.'));
            }
        return data;
    }

    [Theory]
    [MemberData(nameof(ViewToWritePairs))]
    public void ViewToken_NeverSatisfiesItsWriteTier(string held, string required)
    {
        var denied = EndpointMappings.RequirePermission(Principal(held), required);
        Assert.NotNull(denied);
        Assert.Equal(StatusCodes.Status403Forbidden,
            Assert.IsAssignableFrom<IStatusCodeHttpResult>(denied).StatusCode);
    }

    /// <summary>
    /// The role the defect was reported against, driven by the SHIPPED backend grant set
    /// rather than a restatement of it — so a grant added to the role fails here.
    /// </summary>
    [Theory]
    [InlineData("settings:manage")]
    [InlineData("settings:update")]
    [InlineData("users:manage")]
    [InlineData("users:create")]
    [InlineData("users:update")]
    [InlineData("users:delete")]
    [InlineData("roles:manage")]
    [InlineData("roles:create")]
    [InlineData("roles:update")]
    public void ReadOnlyAuditor_ReachesNoWriteTier(string required)
    {
        var auditor = RoleDefaults("Read-Only Auditor");
        Assert.Contains("settings:view", auditor);
        Assert.Contains("users:view", auditor);
        Assert.Contains("roles:view", auditor);

        var denied = EndpointMappings.RequirePermission(Principal(auditor), required);
        Assert.NotNull(denied);
        Assert.Equal(StatusCodes.Status403Forbidden,
            Assert.IsAssignableFrom<IStatusCodeHttpResult>(denied).StatusCode);
    }

    /// <summary>
    /// The seeded roles (database/init/002_seed.sql, a third token vocabulary again) that
    /// hold settings:view and nothing else in the settings family.
    /// </summary>
    [Theory]
    [InlineData("settings:manage")]
    [InlineData("settings:update")]
    public void SeededSettingsViewRoles_ReachNoSettingsWrite(string required)
    {
        string[] operationsManager =
        [
            "dashboard:view", "map:view", "fleet:view", "dispatch:view", "dispatch:manage",
            "orders:view", "orders:manage", "shipments:view", "shipments:manage", "pod:view",
            "pod:upload", "maintenance:view", "safety:view", "dashcam:view", "compliance:view",
            "reports:view", "settings:view",
        ];
        string[] financeBillingManager =
        [
            "finance:view", "finance:manage", "fuel:view", "fuel:manage", "reports:view", "settings:view",
        ];

        foreach (var role in new[] { operationsManager, financeBillingManager })
        {
            var denied = EndpointMappings.RequirePermission(Principal(role), required);
            Assert.NotNull(denied);
            Assert.Equal(StatusCodes.Status403Forbidden,
                Assert.IsAssignableFrom<IStatusCodeHttpResult>(denied).StatusCode);
        }
    }

    /// <summary>
    /// A one-way edge, not a deletion. The tightening must not cost any role a write
    /// capability it holds through a genuine write grant — that is the failure mode the
    /// authorization playbook calls "a partial tightening is worse than none".
    /// </summary>
    [Theory]
    [InlineData("settings:update", "settings:manage")]
    [InlineData("settings:manage", "settings:update")]
    [InlineData("users:create", "users:manage")]
    [InlineData("users:update", "users:manage")]
    [InlineData("users:delete", "users:manage")]
    [InlineData("roles:update", "roles:manage")]
    [InlineData("roles:create", "roles:manage")]
    [InlineData("roles:update", "users:manage")]
    [InlineData("settings:view", "settings:view")]
    [InlineData("users:view", "users:view")]
    [InlineData("roles:view", "roles:view")]
    public void WriteGrants_StillReachTheirWriteTier(string held, string required)
        => Assert.Null(EndpointMappings.RequirePermission(Principal(held), required));

    /// <summary>
    /// Blast radius, measured against the shipped RolePermissionDefaults: no role may LOSE
    /// a token it holds by direct grant, and every role that legitimately administers the
    /// tenant keeps the write tiers it reached through a write grant.
    /// </summary>
    [Fact]
    public void NoRole_LosesAPermissionItHoldsByDirectGrant()
    {
        var lost = new List<string>();
        foreach (var (role, grants) in AllRoleDefaults())
        {
            if (grants.Contains("*")) continue;
            foreach (var token in grants)
            {
                if (EndpointMappings.RequirePermission(Principal(grants), token) is not null)
                    lost.Add($"{role} no longer satisfies its own direct grant '{token}'");
            }
        }
        Assert.True(lost.Count == 0, string.Join("\n", lost));
    }

    [Theory]
    [InlineData("Tenant Admin", "settings:manage")]
    [InlineData("Tenant Admin", "users:manage")]
    [InlineData("Tenant Admin", "roles:manage")]
    [InlineData("Tenant Admin", "roles:update")]
    [InlineData("Fleet Owner", "settings:manage")]
    [InlineData("Fleet Owner", "settings:update")]
    public void AdministrativeRoles_KeepTheirWriteTiers(string role, string required)
        => Assert.Null(EndpointMappings.RequirePermission(Principal(RoleDefaults(role)), required));

    private static DefaultHttpContext Principal(params string[] permissions)
    {
        var http = new DefaultHttpContext();
        http.Items[EndpointMappings.AuthCompanyIdItemKey] = 4242L;
        http.Items[EndpointMappings.AuthUserIdItemKey] = 99L;
        http.Items[EndpointMappings.AuthRoleItemKey] = "Governance Probe";
        http.Items[EndpointMappings.AuthPermissionsItemKey] = permissions;
        return http;
    }

    private static Dictionary<string, string[]> AllRoleDefaults()
    {
        var field = typeof(EndpointMappings).GetField("RolePermissionDefaults", BindingFlags.NonPublic | BindingFlags.Static)
            ?? throw new InvalidOperationException("EndpointMappings.RolePermissionDefaults not found — did it move or change shape?");
        var defaults = (Dictionary<string, string[]>?)field.GetValue(null)
            ?? throw new InvalidOperationException("RolePermissionDefaults was null.");
        Assert.True(defaults.Count >= 15, $"Only {defaults.Count} roles in RolePermissionDefaults — the blast-radius sweep shrank silently.");
        return defaults;
    }

    private static string[] RoleDefaults(string role)
    {
        Assert.True(AllRoleDefaults().TryGetValue(role, out var grants), $"RolePermissionDefaults must still define \"{role}\"");
        return grants!;
    }
}
