using System;
using System.IO;
using System.Text.RegularExpressions;
using Xunit;

namespace Opstrax.Tests;

public sealed class EntitlementAwareNavigationTests
{
    private static string RepoRoot =>
        Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../"));

    private static string Read(params string[] parts) =>
        File.ReadAllText(Path.Combine(new[] { RepoRoot }.Concat(parts).ToArray()));

    [Fact]
    public void SafetyNavigation_ComposesPlatformEntitlementWithTenantRbac()
    {
        var types = Read("frontend", "src", "types", "index.ts");
        var modules = Read("frontend", "src", "modules", "moduleConfig.ts");
        var shell = Read("frontend", "src", "layouts", "AppShell.tsx");

        Assert.Contains("requiredEntitlement?: string", types, StringComparison.Ordinal);
        Assert.Contains("entitlementPolicyMode?", types, StringComparison.Ordinal);
        Assert.Contains("entitlements?: Record<string, boolean>", types, StringComparison.Ordinal);
        Assert.Contains("requiredEntitlement: \"safety\"", modules, StringComparison.Ordinal);
        Assert.Contains("requiredEntitlement: \"maintenance\"", modules, StringComparison.Ordinal);
        Assert.Contains("requiredEntitlement: \"compliance\"", modules, StringComparison.Ordinal);
        Assert.Contains("moduleAllowedByEntitlement(module, session)", shell, StringComparison.Ordinal);
        Assert.Contains("session?.entitlementPolicyMode !== \"package_allowlist\"", shell, StringComparison.Ordinal);
    }

    [Fact]
    public void DeepLink_ToCommerciallyDisabledModule_ShowsPlanBoundary()
    {
        var shell = Read("frontend", "src", "layouts", "AppShell.tsx");

        Assert.Contains("activeModuleEntitled ? <Outlet />", shell, StringComparison.Ordinal);
        Assert.Contains("Not included in your plan", shell, StringComparison.Ordinal);
        Assert.Contains("Contact your account owner", shell, StringComparison.Ordinal);
    }

    [Fact]
    public void LoginAndSessionRefresh_ReturnAuthoritativeCommercialAccessSnapshot()
    {
        var endpoints = Read("backend-dotnet", "Controllers", "EndpointMappings.cs");

        Assert.Contains("ResolveAuthEntitlementsAsync", endpoints, StringComparison.Ordinal);
        Assert.Contains("SELECT module_key,enabled FROM tenant_entitlements", endpoints, StringComparison.Ordinal);
        Assert.Contains("entitlementPolicyMode = user[\"entitlementPolicyMode\"]", endpoints, StringComparison.Ordinal);
        Assert.Contains("entitlements,", endpoints, StringComparison.Ordinal);
    }

    [Fact]
    public void DriverIdentityBoundary_UsesDirectGrantBeforeSelectingBackOfficeShell()
    {
        var routing = Read("frontend", "src", "auth", "sessionRouting.ts");

        Assert.Contains("ownsDirectly(\"driver:self\")", routing, StringComparison.Ordinal);
        Assert.Contains("!ownsDirectly(\"dashboard:view\")", routing, StringComparison.Ordinal);
        Assert.DoesNotContain("hasPermission(permissions, \"driver:self\")", routing, StringComparison.Ordinal);
    }

    [Fact]
    public void CompositeDashboard_UsesExactServerPermissionsAndAccessibleSearchOnly()
    {
        var types = Read("frontend", "src", "types", "index.ts");
        var modules = Read("frontend", "src", "modules", "moduleConfig.ts");
        var shell = Read("frontend", "src", "layouts", "AppShell.tsx");
        var overview = Read("frontend", "src", "pages", "FleetOverviewPage.tsx");
        var telematics = Read("frontend", "src", "services", "telematicsService.ts");
        var routes = Read("frontend", "src", "App.tsx");
        var endpoints = Read("backend-dotnet", "Controllers", "EndpointMappings.cs");
        var fixture = Read("backend-dotnet", "Services", "DemoTenantSeeder.cs");

        Assert.Contains("permissionMatch?: \"semantic\" | \"direct\"", types, StringComparison.Ordinal);
        Assert.Contains("requiredPermission: \"alerts:view\", permissionMatch: \"direct\"", modules, StringComparison.Ordinal);
        Assert.Contains("requiredPermission: \"shipments:view\", permissionMatch: \"direct\"", modules, StringComparison.Ordinal);
        Assert.Contains("module.permissionMatch === \"direct\"", shell, StringComparison.Ordinal);
        Assert.DoesNotContain("resolveSearchRoute(sidebarQuery)", shell, StringComparison.Ordinal);
        Assert.Contains("No accessible modules match.", shell, StringComparison.Ordinal);
        Assert.Contains("enabled: canViewAlerts", overview, StringComparison.Ordinal);
        Assert.Contains("enabled: canViewJobs", overview, StringComparison.Ordinal);
        Assert.Contains("enabled: canViewVehicles", overview, StringComparison.Ordinal);
        Assert.DoesNotContain("driversApi.list()", overview, StringComparison.Ordinal);
        Assert.Contains("/api/live-operations/fleet-overview", endpoints, StringComparison.Ordinal);
        Assert.Contains("const canViewDevices = hasPermission(\"telemetry.devices.read\")", overview, StringComparison.Ordinal);
        Assert.Contains("if (!canViewAlerts) return []", overview, StringComparison.Ordinal);
        Assert.Contains("Not available for this role.", overview, StringComparison.Ordinal);
        Assert.Contains("permission=\"alerts:view\" direct", routes, StringComparison.Ordinal);
        Assert.Contains("permission=\"shipments:view\" direct", routes, StringComparison.Ordinal);
        Assert.Contains("fetchActiveFaultsIfAuthorized(session)", telematics, StringComparison.Ordinal);
        Assert.Contains("fetchOpenAlertsIfAuthorized(session)", telematics, StringComparison.Ordinal);
        Assert.DoesNotContain("RequirePermission(http, \"alerts:view\")", endpoints, StringComparison.Ordinal);
        Assert.True(Regex.Matches(endpoints, "RequireAnyDirectPermission\\(http, \\\"alerts:view\\\"\\)").Count >= 4);
        Assert.Contains("\\\"alerts:view\\\",\\\"alerts:acknowledge\\\"", fixture, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("map-view", "telematics")]
    [InlineData("fleet-live-wall", "telematics")]
    [InlineData("geofences", "telematics")]
    [InlineData("trips", "dispatch")]
    [InlineData("route-plans", "dispatch")]
    [InlineData("proof-of-delivery", "dispatch")]
    [InlineData("telematics-control-tower", "telematics")]
    [InlineData("cold-chain", "telematics")]
    [InlineData("sensor-health", "telematics")]
    [InlineData("dashcam", "safety")]
    [InlineData("traffic-violations", "safety")]
    [InlineData("evidence-packages", "safety")]
    [InlineData("leads", "crm")]
    [InlineData("sales-pipeline", "crm")]
    [InlineData("opportunities", "crm")]
    [InlineData("campaigns", "crm")]
    [InlineData("customers", "crm")]
    [InlineData("contracts", "crm")]
    [InlineData("rate-cards", "crm")]
    [InlineData("price-simulation", "crm")]
    [InlineData("quotations", "crm")]
    [InlineData("reports-analytics", "reports")]
    [InlineData("customer-eta", "customer_portal")]
    [InlineData("customer-portal", "customer_portal")]
    [InlineData("customer-visibility", "customer_portal")]
    [InlineData("safety-center", "safety")]
    [InlineData("maintenance-center", "maintenance")]
    [InlineData("compliance-center", "compliance")]
    public void CanonicalNavigation_MatchesItsServerOwnedCommercialModule(string moduleKey, string entitlement)
    {
        var modules = Read("frontend", "src", "modules", "moduleConfig.ts");
        var pattern = $@"\{{\s*key:\s*""{Regex.Escape(moduleKey)}""[^\r\n]*requiredEntitlement:\s*""{Regex.Escape(entitlement)}""";
        Assert.Matches(new Regex(pattern, RegexOptions.CultureInvariant), modules);
    }

    [Fact]
    public void SafetyEvidence_AndProofOfDelivery_AreAlsoServerEdgeGated()
    {
        var program = Read("backend-dotnet", "Program.cs");
        Assert.Contains("(\"/api/evidence-packages\",   \"safety\")", program, StringComparison.Ordinal);
        Assert.Contains("(\"/api/proof-of-delivery\",   \"dispatch\")", program, StringComparison.Ordinal);
    }
}
