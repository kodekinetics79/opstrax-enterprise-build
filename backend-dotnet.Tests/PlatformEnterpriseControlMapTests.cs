using System.Text.RegularExpressions;
using Opstrax.Api.Controllers;

namespace Opstrax.Tests;

public sealed class PlatformEnterpriseControlMapTests
{
    private static string RepoRoot => Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../"));
    private static string Read(params string[] parts) => File.ReadAllText(Path.Combine(new[] { RepoRoot }.Concat(parts).ToArray()));

    [Fact]
    public void EveryTenantVisibleModule_IsPresentInAuthoritativeControlMap()
    {
        var source = Read("frontend", "src", "modules", "moduleConfig.ts");
        var controlMap = Read("docs", "platform", "PLATFORM_ADMIN_ENTERPRISE_CONTROL_MAP.md");
        var matches = Regex.Matches(source,
            "\\{ key: \\\"(?<key>[^\\\"]+)\\\".*?route: \\\"(?<route>[^\\\"]+)\\\"(?<tail>.*)$",
            RegexOptions.Multiline);

        Assert.Equal(92, matches.Count);
        var keys = matches.Select(match => match.Groups["key"].Value).ToArray();
        Assert.Equal(keys.Length, keys.Distinct(StringComparer.Ordinal).Count());
        Assert.All(keys, key => Assert.Contains($"`{key}`", controlMap, StringComparison.Ordinal));
    }

    [Fact]
    public void AuthoritativeControlMap_PutsEveryModuleInExactlyOneCorrectCommercialBucket()
    {
        var source = Read("frontend", "src", "modules", "moduleConfig.ts");
        var controlMap = Read("docs", "platform", "PLATFORM_ADMIN_ENTERPRISE_CONTROL_MAP.md");
        var matches = Regex.Matches(source,
            "\\{ key: \\\"(?<key>[^\\\"]+)\\\".*?route: \\\"(?<route>[^\\\"]+)\\\"(?<tail>.*)$",
            RegexOptions.Multiline);
        var catalogKeys = matches.Select(match => match.Groups["key"].Value).ToHashSet(StringComparer.Ordinal);
        var expectedGoverned = matches
            .Where(match => match.Groups["tail"].Value.Contains("requiredEntitlement:", StringComparison.Ordinal))
            .Select(match => match.Groups["key"].Value)
            .ToHashSet(StringComparer.Ordinal);
        var expectedCore = catalogKeys.Except(expectedGoverned, StringComparer.Ordinal).ToHashSet(StringComparer.Ordinal);

        static string Section(string document, string startHeading, string endHeading)
        {
            var start = document.IndexOf(startHeading, StringComparison.Ordinal);
            Assert.True(start >= 0, $"Missing section {startHeading}");
            var end = document.IndexOf(endHeading, start + startHeading.Length, StringComparison.Ordinal);
            Assert.True(end > start, $"Missing section terminator {endHeading}");
            return document[start..end];
        }

        static HashSet<string> DocumentedModuleKeys(string section, HashSet<string> catalog) =>
            Regex.Matches(section, "`(?<key>[^`]+)`")
                .Select(match => match.Groups["key"].Value)
                .Where(catalog.Contains)
                .ToHashSet(StringComparer.Ordinal);

        var documentedGoverned = DocumentedModuleKeys(
            Section(controlMap, "## Platform-commercially controlled catalog", "## Tenant-governed core/open catalog"), catalogKeys);
        var documentedCore = DocumentedModuleKeys(
            Section(controlMap, "## Tenant-governed core/open catalog", "## Settings and override ownership"), catalogKeys);

        Assert.Equal(expectedGoverned.Order(), documentedGoverned.Order());
        Assert.Equal(expectedCore.Order(), documentedCore.Order());
        Assert.Empty(documentedGoverned.Intersect(documentedCore, StringComparer.Ordinal));
    }

    [Fact]
    public void EveryCatalogModule_HasARegisteredTenantRoute()
    {
        var source = Read("frontend", "src", "modules", "moduleConfig.ts");
        var app = Read("frontend", "src", "App.tsx");
        var configuredRoutes = Regex.Matches(source,
                "\\{ key: \\\"[^\\\"]+\\\".*?route: \\\"(?<route>[^\\\"]+)\\\"",
                RegexOptions.Multiline)
            .Select(match => match.Groups["route"].Value)
            .ToArray();
        var registeredRoutes = Regex.Matches(app, "<Route\\s+path=\\\"(?<route>[^\\\"]+)\\\"")
            .Select(match => match.Groups["route"].Value)
            .ToHashSet(StringComparer.Ordinal);

        Assert.Equal(92, configuredRoutes.Length);
        Assert.All(configuredRoutes, route => Assert.Contains(route, registeredRoutes));
    }

    [Fact]
    public void CommercialCatalog_HasExplicitGovernedAndCoreCounts()
    {
        var source = Read("frontend", "src", "modules", "moduleConfig.ts");
        var matches = Regex.Matches(source,
            "\\{ key: \\\"(?<key>[^\\\"]+)\\\".*?route: \\\"(?<route>[^\\\"]+)\\\"(?<tail>.*)$",
            RegexOptions.Multiline);
        var controlled = matches
            .Where(match => match.Groups["tail"].Value.Contains("requiredEntitlement:", StringComparison.Ordinal))
            .ToArray();

        Assert.Equal(45, controlled.Length);
        Assert.Equal(47, matches.Count - controlled.Length);

        var entitlementCounts = controlled
            .Select(match => Regex.Match(match.Groups["tail"].Value, "requiredEntitlement: \\\"(?<value>[^\\\"]+)\\\"").Groups["value"].Value)
            .GroupBy(value => value, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.Count(), StringComparer.Ordinal);
        Assert.Equal(new Dictionary<string, int>
        {
            ["compliance"] = 3,
            ["crm"] = 9,
            ["customer_portal"] = 3,
            ["dispatch"] = 6,
            ["integrations"] = 1,
            ["maintenance"] = 6,
            ["reports"] = 1,
            ["safety"] = 7,
            ["telematics"] = 9,
        }, entitlementCounts);
    }

    [Fact]
    public void GovernedModules_HaveNavigationDeepLinkAndServerEdgeContracts()
    {
        var shell = Read("frontend", "src", "layouts", "AppShell.tsx");
        var program = Read("backend-dotnet", "Program.cs");
        var auth = Read("backend-dotnet", "Controllers", "EndpointMappings.cs");

        Assert.Contains("moduleAllowedByEntitlement", shell, StringComparison.Ordinal);
        Assert.Contains("Not included in your plan", shell, StringComparison.Ordinal);
        Assert.Contains("session?.entitlementPolicyMode !== \"package_allowlist\"", shell, StringComparison.Ordinal);
        Assert.Contains("var moduleKey = ModuleKeyForPath(path);", program, StringComparison.Ordinal);
        Assert.Contains("c.entitlement_policy_mode='package_allowlist' AND COALESCE(e.enabled,false)=false", program, StringComparison.Ordinal);
        Assert.DoesNotContain("(\"/api/foundation/safety-maintenance\", \"dashboard\")", program, StringComparison.Ordinal);
        Assert.Contains("GovernedEntitlementModuleKeys.Contains(moduleKey)",
            Read("backend-dotnet", "Controllers", "PlatformEndpoints.cs"), StringComparison.Ordinal);

        foreach (var entitlement in new[] { "telematics", "safety", "maintenance", "dispatch", "crm", "customer_portal", "compliance", "reports", "integrations" })
            Assert.Contains($"\"{entitlement}\")", program, StringComparison.Ordinal);

        Assert.Contains("entitlementPolicyMode", auth, StringComparison.Ordinal);
        Assert.Contains("ResolveAuthEntitlementsAsync", auth, StringComparison.Ordinal);
    }

    [Fact]
    public void LegacyModuleRecordSurface_CannotBypassCommercialOrRbacControls()
    {
        var program = Read("backend-dotnet", "Program.cs");
        var endpoints = Read("backend-dotnet", "Controllers", "EndpointMappings.cs");

        // Defense in depth: should a catalogued generic route ever be reintroduced,
        // middleware resolves its UI key back to the commercial entitlement.
        Assert.Contains("const string genericModulePrefix = \"/api/modules/\"", program, StringComparison.Ordinal);
        Assert.Contains("PlatformTenantModuleCatalog.Modules.FirstOrDefault", program, StringComparison.Ordinal);
        Assert.Contains("catalogEntry?.RequiredEntitlement", program, StringComparison.Ordinal);

        // The currently unused arbitrary bucket API is intentionally not routable.
        Assert.DoesNotContain("app.MapGet(\"/api/modules/{moduleKey}\"", endpoints, StringComparison.Ordinal);
        Assert.DoesNotContain("app.MapPost(\"/api/modules/{moduleKey}\"", endpoints, StringComparison.Ordinal);
        Assert.DoesNotContain("app.MapPut(\"/api/modules/{moduleKey}/{id:long}\"", endpoints, StringComparison.Ordinal);

        // Every legacy dedicated root has an explicit read and write permission;
        // dictionary indexing makes a newly registered root fail during startup.
        var registered = Regex.Matches(endpoints, "MapDedicatedModule\\(app, \\\"(?<key>[^\\\"]+)\\\"\\)")
            .Select(match => match.Groups["key"].Value)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Order(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        static string[] PermissionKeys(string source, string dictionary)
        {
            var start = source.IndexOf($"Dictionary<string, string> {dictionary}", StringComparison.Ordinal);
            Assert.True(start >= 0);
            var end = source.IndexOf("};", start, StringComparison.Ordinal);
            return Regex.Matches(source[start..end], "\\[\\\"(?<key>[^\\\"]+)\\\"\\]")
                .Select(match => match.Groups["key"].Value)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Order(StringComparer.OrdinalIgnoreCase)
                .ToArray();
        }
        Assert.Equal(registered, PermissionKeys(endpoints, "ModuleReadPermissionByKey"));
        Assert.All(registered, key => Assert.Contains(key, PermissionKeys(endpoints, "ModuleWritePermissionByKey"), StringComparer.OrdinalIgnoreCase));
        Assert.Contains("RequirePermission(http, ModuleReadPermissionByKey[moduleKey])", endpoints, StringComparison.Ordinal);
        Assert.Contains("RequirePermission(http, ModuleWritePermissionByKey[moduleKey])", endpoints, StringComparison.Ordinal);

        // Compatibility roots that back commercially governed product areas must
        // stay inside the same server-side entitlement envelope as canonical APIs.
        foreach (var (prefix, entitlement) in new[]
        {
            ("/api/route-planning", "dispatch"),
            ("/api/hos-eld", "compliance"),
            ("/api/customer-portal", "customer_portal"),
            ("/api/reports-analytics", "reports"),
            ("/api/contracts-rates", "crm"),
        })
            Assert.Matches(
                new Regex($@"\(\s*""{Regex.Escape(prefix)}""\s*,\s*""{Regex.Escape(entitlement)}""\s*\)"),
                program);
    }

    [Fact]
    public void SafetyTrafficViolationReads_RequireTenantSafetyRbac()
    {
        var endpoints = Read("backend-dotnet", "Controllers", "EndpointMappings.cs");
        var start = endpoints.IndexOf("// ── Traffic Violations", StringComparison.Ordinal);
        var end = endpoints.IndexOf("// ── Service History", start, StringComparison.Ordinal);
        var surface = endpoints[start..end];

        Assert.Equal(2, Regex.Matches(surface, "RequirePermission\\(http, \\\"safety:view\\\"\\)").Count);
        Assert.Contains("HttpContext http", surface, StringComparison.Ordinal);
        Assert.Contains("StrictBranchFilter(http, \"se\")", surface, StringComparison.Ordinal);
    }

    [Fact]
    public void LegacyControlledReads_RequireRbacAndOperationalBranchScope()
    {
        var endpoints = Read("backend-dotnet", "Controllers", "EndpointMappings.cs");
        var maintenanceStart = endpoints.IndexOf("// ── Service History", StringComparison.Ordinal);
        var maintenanceEnd = endpoints.IndexOf("app.MapGet(\"/api/fleet/utilization\"", maintenanceStart, StringComparison.Ordinal);
        var maintenance = endpoints[maintenanceStart..maintenanceEnd];
        Assert.Equal(3, Regex.Matches(maintenance, "RequirePermission\\(http, \\\"maintenance:view\\\"\\)").Count);
        Assert.Equal(3, Regex.Matches(maintenance, "StrictBranchFilter\\(http, \\\"v\\\"\\)").Count);

        var reportsStart = endpoints.IndexOf("// ===== BATCH 7: REPORTS", StringComparison.Ordinal);
        var reportsEnd = endpoints.IndexOf("// ===== P8 REPORTING", reportsStart, StringComparison.Ordinal);
        var reports = endpoints[reportsStart..reportsEnd];
        foreach (var route in new[] { "/api/reports/catalog", "/api/reports/runs", "/api/reports/scheduled" })
        {
            var routeStart = reports.IndexOf($"app.MapGet(\"{route}\"", StringComparison.Ordinal);
            Assert.True(routeStart >= 0);
            Assert.Contains("RequirePermission(http, \"reports:view\")", reports[routeStart..Math.Min(routeStart + 420, reports.Length)], StringComparison.Ordinal);
        }
    }

    [Fact]
    public void CommercialWritePaths_RejectPhantomCatalogKeys()
    {
        var endpoints = Read("backend-dotnet", "Controllers", "PlatformEndpoints.cs");
        var expected = new[] { "telematics", "safety", "maintenance", "dispatch", "crm", "customer_portal", "compliance", "reports", "integrations" };

        Assert.Equal(expected.Order(), PlatformEndpoints.GovernedEntitlementModuleKeys.Order());
        foreach (var entitlement in expected)
            Assert.Contains($"\"{entitlement}\"", endpoints, StringComparison.Ordinal);

        Assert.Contains("GovernedEntitlementModuleKeys.Contains(moduleKey)", endpoints, StringComparison.Ordinal);
        Assert.Equal(2, Regex.Matches(endpoints, "ParseGovernedModuleKeys\\(body").Count);
        Assert.Contains("modules.Any(module => !GovernedEntitlementModuleKeys.Contains(module))", endpoints, StringComparison.Ordinal);
        Assert.Contains("Package module catalog is invalid JSON", endpoints, StringComparison.Ordinal);
        Assert.Contains("Package contains an unknown governed module key", endpoints, StringComparison.Ordinal);
    }

    [Fact]
    public void ControlMap_RecordsKnownOpenAndSplitBoundaryRisks()
    {
        var controlMap = Read("docs", "platform", "PLATFORM_ADMIN_ENTERPRISE_CONTROL_MAP.md");

        Assert.Contains("47 modules are not Platform-commercially gated", controlMap, StringComparison.Ordinal);
        Assert.Contains("API ownership is prefix-based", controlMap, StringComparison.Ordinal);
        Assert.Contains("Saudi Readiness has a split boundary", controlMap, StringComparison.Ordinal);
        Assert.Contains("quotas are incomplete", controlMap, StringComparison.Ordinal);
        Assert.Contains("public token", controlMap, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("generic `/api/modules/{moduleKey}` surface is not routable", controlMap, StringComparison.Ordinal);
    }
}
