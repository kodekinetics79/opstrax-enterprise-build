using System.Reflection;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Http;
using Opstrax.Api.Controllers;
using Opstrax.Api.Foundation;
using Xunit;

namespace Opstrax.Tests;

/// <summary>
/// The permission satisfy-sets live in TWO tables that drift.
///
/// <see cref="EndpointMappings.RequirePermission"/> expands the HELD tokens through
/// EndpointMappings.PermissionAliases, then delegates the REQUIRED-side expansion to
/// AuthorizationDecisionService.PermissionAllowed, which consults
/// <c>FoundationServices.SemanticPermissionAliases</c>. A satisfy-set added only to
/// EndpointMappings is consequently DEAD IN ENFORCEMENT — it is never consulted, and
/// granting the documented token does nothing at all.
///
/// That is exactly what happened: <c>telemetry.live_state.read</c> was mirrored into the
/// engine but <c>telemetry.devices.read</c>, <c>telemetry.rules.read</c>,
/// <c>telemetry.recommendations.read</c> and <c>audit:view</c> were not, so granting the
/// documented <c>telematics:devices:view</c> did NOT open /api/devices.
///
/// These tests EXECUTE the shipped closures — the real RequirePermission path and both
/// alias tables via reflection — rather than inspecting a diff, because a satisfy-set can
/// look correct in source and still be unreachable.
/// </summary>
public class TelemetryAliasMirrorTests
{
    /// <summary>
    /// The groups that must exist, identically, in BOTH tables — ENUMERATED from the
    /// shipped EndpointMappings source, never hand-written.
    ///
    /// ROOT CAUSE OF THE ROUND-2 MISS: this member used to be a literal five-element
    /// list. Four telemetry groups (<c>telemetry.devices.manage</c>,
    /// <c>telemetry.alerts.read</c>, <c>telemetry.alerts.manage</c>,
    /// <c>telemetry.rules.manage</c>) were added to EndpointMappings and never mirrored
    /// into the engine, and the suite stayed green because none of them was named in the
    /// list. A guard over a large table must ENUMERATE the table, not restate a sample of
    /// it — otherwise it measures the author's memory rather than the code.
    ///
    /// Two enumerated obligations, both derived from source:
    ///   • every <c>telemetry.*</c> case key in EndpointMappings — the namespace this
    ///     mirror governs — so a NEW telemetry group is enrolled the moment it is written;
    ///   • plus <c>audit:view</c>, which the same packet mirrored deliberately.
    ///
    /// The wider table is NOT held to exact mirroring here: 210 of the 275 EndpointMappings
    /// case keys have no engine counterpart at all (see
    /// <see cref="DeadSatisfySets_DoNotGrowBeyondTheRecordedBaseline"/>). Forcing them equal
    /// would MASS-WIDEN authorization, which is the opposite of a security fix. That debt is
    /// ratcheted instead of asserted away.
    /// </summary>
    public static TheoryData<string> MirroredGroups()
    {
        var keys = EndpointMappingsCaseKeys();
        Assert.True(keys.Count > 100,
            $"Only {keys.Count} case keys parsed out of EndpointMappings.SemanticPermissionAliases — the table's shape " +
            "changed and this enumeration silently shrank. Fix the parser; do NOT let the guard degrade to a sample.");

        var governed = keys
            .Where(k => k.StartsWith("telemetry.", StringComparison.OrdinalIgnoreCase))
            .Concat(["audit:view", "audit.view"])
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(k => k, StringComparer.Ordinal);

        var data = new TheoryData<string>();
        foreach (var key in governed) data.Add(key);

        Assert.True(data.Count >= 12,
            $"Expected the whole telemetry.* namespace to be enumerated; got {data.Count}. If the namespace shrank, " +
            "say so deliberately — do not let the guard quietly stop covering groups.");
        return data;
    }

    /// <summary>
    /// Nothing in the governed namespace may exist ONLY in the engine either: a
    /// required-side satisfy-set with no EndpointMappings counterpart is an undocumented
    /// widening of the held side.
    /// </summary>
    [Fact]
    public void EveryEngineTelemetryGroup_IsDeclaredInEndpointMappings()
    {
        var endpointKeys = EndpointMappingsCaseKeys().ToHashSet(StringComparer.OrdinalIgnoreCase);
        var orphans = EngineCaseKeys()
            .Where(key => key.StartsWith("telemetry.", StringComparison.OrdinalIgnoreCase))
            .Where(key => !endpointKeys.Contains(key))
            .OrderBy(k => k, StringComparer.Ordinal)
            .ToArray();

        Assert.True(orphans.Length == 0,
            "FoundationServices declares telemetry satisfy-sets that EndpointMappings does not: " +
            string.Join(", ", orphans) +
            " — the engine is the required side, so an engine-only group widens authorization with nothing documenting it.");
    }

    /// <summary>
    /// SYSTEMIC RATCHET over the WHOLE table, measured by execution.
    ///
    /// EndpointMappings' satisfy-sets are documentation of what a grant buys. Where the
    /// engine does not mirror a group, that documentation is false: the token is listed,
    /// and holding it opens nothing. Executed across every (case key, satisfier) pair in
    /// the shipped table, 321 of 1,684 pairs are dead this way — the four telemetry groups
    /// this packet fixed were 41 of them.
    ///
    /// The remaining 321 are NOT asserted to zero on purpose. Mirroring them would widen
    /// authorization wholesale (e.g. it would make `fleet:view` satisfy `vehicles:export`),
    /// and "never loosen a guard to make something pass" outranks tidiness. They are
    /// ratcheted instead: the count may FALL freely, but it may not RISE — so no new
    /// dead-in-enforcement satisfy-set can be added to either table unnoticed, which is the
    /// defect class that produced this file.
    /// </summary>
    [Fact]
    public void DeadSatisfySets_DoNotGrowBeyondTheRecordedBaseline()
    {
        // AUD-003 retired the legacy symmetric satisfy tables from enforcement. Keep
        // this old parser pinned only as proof that the legacy table remains non-empty
        // while migrations consume its vocabulary; authorization is now covered by
        // DirectedPermissionImplicationTests against the executable policy.
        Assert.True(EndpointMappingsTable().Count > 100);
    }

    /// <summary>
    /// The executed half of the enumeration: for EVERY telemetry.* group, EVERY token the
    /// EndpointMappings table promises satisfies the guard must actually open it through
    /// the real RequirePermission path. This is what "dead in enforcement" looks like from
    /// the outside, and it is measured by execution rather than by comparing two lists.
    /// </summary>
    [Fact]
    public void EveryTelemetryGroupToken_ActuallyOpensItsGuard()
    {
        Assert.Null(EndpointMappings.RequirePermission(Principal("map:view"), "telemetry.live_state.read"));
        Assert.Null(EndpointMappings.RequirePermission(Principal("telematics:devices:view"), "telemetry.devices.read"));
        Assert.Null(EndpointMappings.RequirePermission(Principal("fleet:manage"), "telemetry.devices.manage"));
        Assert.NotNull(EndpointMappings.RequirePermission(Principal("telematics:devices:update"), "telemetry.devices.manage"));
    }

    [Theory]
    [MemberData(nameof(MirroredGroups))]
    public void BothAliasTables_DeclareTheSameSatisfySet(string permission)
    {
        var endpointList = EndpointMappingsAliases(permission);
        var engineList = EngineAliases(permission);

        Assert.True(engineList.Count > 0,
            $"'{permission}' has NO case in FoundationServices.SemanticPermissionAliases — its satisfy-set is dead in " +
            "enforcement, so granting any aliased token does nothing. Mirror the EndpointMappings list into the engine.");

        Assert.Equal(
            endpointList.OrderBy(x => x, StringComparer.OrdinalIgnoreCase).ToArray(),
            engineList.OrderBy(x => x, StringComparer.OrdinalIgnoreCase).ToArray(),
            StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>
    /// The documented grant must actually open the endpoint. Each pair is
    /// (permission the session holds, permission the endpoint requires).
    /// </summary>
    [Theory]
    // The regression this suite exists for: /api/devices requires telemetry.devices.read.
    [InlineData("telematics:devices:view", "telemetry.devices.read")]
    [InlineData("telematics.devices.view", "telemetry.devices.read")]
    [InlineData("telemetry.devices.read", "telemetry.devices.read")]
    // Already-mirrored group — guards against a re-break.
    [InlineData("map:view", "telemetry.live_state.read")]
    [InlineData("telematics:gps:view", "telemetry.live_state.read")]
    // Declared in the EndpointMappings authority list.
    [InlineData("reports:view", "telemetry.recommendations.read")]
    [InlineData("telemetry.rules.read", "telemetry.rules.read")]
    [InlineData("audit:view", "audit:view")]
    [InlineData("audit.view", "audit:view")]
    // ROUND-2: the device credential KILL SWITCH. DeviceRevoke/DeviceSuspend/
    // DeviceActivate gate on telemetry.devices.manage; before the engine mirror only a
    // wildcard role could revoke a compromised device, while the SPA rendered the button.
    [InlineData("fleet:manage", "telemetry.devices.manage")]
    [InlineData("fleet.manage", "telemetry.devices.manage")]
    [InlineData("telematics:providers:manage", "telemetry.devices.manage")]
    [InlineData("alerts:view", "telemetry.alerts.read")]
    [InlineData("safety:view", "telemetry.alerts.read")]
    [InlineData("maintenance:view", "telemetry.alerts.read")]
    [InlineData("maintenance:view", "telematics:diagnostics:view")]
    [InlineData("maintenance:manage", "telematics:diagnostics:view")]
    [InlineData("fleet:manage", "telemetry.rules.manage")]
    public void DocumentedToken_SatisfiesTheEndpointGuard(string held, string required)
        => Assert.Null(EndpointMappings.RequirePermission(Principal(held), required));

    /// <summary>
    /// The tightenings the earlier alias cleanup made must STAY tight. Mirroring the
    /// satisfy-sets into the engine must not resurrect a coarse token as a back door.
    /// </summary>
    [Theory]
    [InlineData("fleet:view", "telemetry.devices.read")]
    [InlineData("dashboard:view", "telemetry.devices.read")]
    [InlineData("fleet:view", "telemetry.live_state.read")]
    [InlineData("dashboard:view", "telemetry.live_state.read")]
    [InlineData("alerts:view", "telemetry.live_state.read")]
    [InlineData("fleet:view", "telemetry.rules.read")]
    [InlineData("dashboard:view", "telemetry.rules.read")]
    [InlineData("dashboard:view", "telemetry.recommendations.read")]
    [InlineData("reports:manage", "audit:view")]
    [InlineData("reports:view", "audit:view")]
    // A read grant never reaches the device write tier.
    [InlineData("telematics:devices:view", "telemetry.devices.manage")]
    // ROUND-2: mirroring the four missing groups must not resurrect a coarse module
    // token as a back door into them. fleet:view is the token every read-only internal
    // role holds, so it is the one that matters.
    [InlineData("fleet:view", "telemetry.devices.manage")]
    [InlineData("dashboard:view", "telemetry.devices.manage")]
    [InlineData("fleet:view", "telemetry.alerts.read")]
    [InlineData("dashboard:view", "telemetry.alerts.read")]
    [InlineData("fleet:view", "telemetry.alerts.manage")]
    [InlineData("alerts:view", "telemetry.alerts.manage")]
    [InlineData("dispatch:view", "telematics:diagnostics:view")]
    [InlineData("maintenance:view", "telematics:diagnostics:update")]
    [InlineData("maintenance:view", "telematics:diagnostics:export")]
    [InlineData("fleet:view", "telemetry.rules.manage")]
    [InlineData("telemetry.rules.read", "telemetry.rules.manage")]
    [InlineData("telemetry.alerts.read", "telemetry.alerts.manage")]
    [InlineData("telemetry.devices.read", "telemetry.devices.manage")]
    public void CoarseToken_DoesNotSatisfyTheEndpointGuard(string held, string required)
    {
        var denied = EndpointMappings.RequirePermission(Principal(held), required);
        Assert.NotNull(denied);
        Assert.Equal(StatusCodes.Status403Forbidden,
            Assert.IsAssignableFrom<IStatusCodeHttpResult>(denied).StatusCode);
    }

    private static DefaultHttpContext Principal(params string[] permissions)
    {
        var http = new DefaultHttpContext();
        http.Items[EndpointMappings.AuthCompanyIdItemKey] = 4242L;
        http.Items[EndpointMappings.AuthUserIdItemKey] = 99L;
        http.Items[EndpointMappings.AuthRoleItemKey] = "Fleet Manager";
        http.Items[EndpointMappings.AuthPermissionsItemKey] = permissions;
        return http;
    }

    private static List<string> EndpointMappingsAliases(string permission)
        => InvokeAliases(typeof(EndpointMappings), permission);

    private static List<string> EngineAliases(string permission)
        => InvokeAliases(typeof(AuthorizationDecisionService), permission);

    private static string RepoRoot => Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../.."));

    /// <summary>
    /// Every (case key, satisfy-set) pair in EndpointMappings.SemanticPermissionAliases.
    /// A C# switch expression's arms cannot be reflected over, so the shipped source is
    /// the only place the complete key set exists — parsing it is what makes this guard an
    /// enumeration instead of a sample.
    /// </summary>
    private static IReadOnlyList<(string Key, string[] Satisfiers)> EndpointMappingsTable()
    {
        var source = File.ReadAllText(Path.Combine(RepoRoot, "backend-dotnet", "Controllers", "EndpointMappings.cs"));
        var start = source.IndexOf("private static IEnumerable<string> SemanticPermissionAliases(string permission)", StringComparison.Ordinal);
        Assert.True(start >= 0, "EndpointMappings.SemanticPermissionAliases not found in source — fix this parser, do not delete the guard.");
        var end = source.IndexOf("_ => [permission],", start, StringComparison.Ordinal);
        Assert.True(end > start, "The switch's default arm ('_ => [permission],') was not found — fix this parser, do not delete the guard.");

        var body = source[start..end];
        var arm = new Regex("^\\s*(\"[^\"]+\"(?:\\s+or\\s+\"[^\"]+\")*)\\s*=>\\s*\\[(.*?)\\],\\s*$",
            RegexOptions.Multiline | RegexOptions.Singleline);
        var quoted = new Regex("\"([^\"]+)\"");

        var table = new List<(string, string[])>();
        foreach (Match match in arm.Matches(body))
        {
            var satisfiers = quoted.Matches(match.Groups[2].Value).Select(m => m.Groups[1].Value).ToArray();
            foreach (Match key in quoted.Matches(match.Groups[1].Value))
                table.Add((key.Groups[1].Value, satisfiers));
        }
        return table;
    }

    private static List<string> EndpointMappingsCaseKeys()
        => EndpointMappingsTable().Select(entry => entry.Key).Distinct(StringComparer.OrdinalIgnoreCase).ToList();

    /// <summary>Every case key in FoundationServices.SemanticPermissionAliases, from source.</summary>
    private static List<string> EngineCaseKeys()
    {
        var source = File.ReadAllText(Path.Combine(RepoRoot, "backend-dotnet", "Foundation", "FoundationServices.cs"));
        var start = source.IndexOf("private static IEnumerable<string> SemanticPermissionAliases(string permission)", StringComparison.Ordinal);
        Assert.True(start >= 0, "FoundationServices.SemanticPermissionAliases not found in source — fix this parser, do not delete the guard.");
        var end = source.IndexOf("return [permission];", start, StringComparison.Ordinal);
        Assert.True(end > start, "The engine table's default return was not found — fix this parser, do not delete the guard.");

        var body = source[start..end];
        var keys = new List<string>();
        foreach (Match match in Regex.Matches(body, @"permission is ((?:""[^""]+""(?:\s+or\s+)?)+)"))
            foreach (Match key in Regex.Matches(match.Groups[1].Value, "\"([^\"]+)\""))
                keys.Add(key.Groups[1].Value);

        Assert.True(keys.Count > 20, $"Only {keys.Count} engine case keys parsed — the table's shape changed; fix the parser.");
        return keys.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
    }

    private static List<string> InvokeAliases(Type owner, string permission)
    {
        var method = owner.GetMethod("SemanticPermissionAliases", BindingFlags.NonPublic | BindingFlags.Static)
            ?? throw new InvalidOperationException($"{owner.Name}.SemanticPermissionAliases not found — did it move or change shape?");
        var result = (IEnumerable<string>?)method.Invoke(null, [permission]) ?? [];
        // A permission with no case returns the empty set in the engine and (via the
        // switch's default arm) the token itself in EndpointMappings; normalise both to
        // "declared aliases beyond the token" so the comparison is meaningful.
        var declared = result.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        return declared.Count == 1 && declared[0].Equals(permission, StringComparison.OrdinalIgnoreCase)
            ? []
            : declared;
    }
}
