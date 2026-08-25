using System;
using System.IO;
using System.Linq;
using Xunit;

namespace Opstrax.Tests;

public class FleetPilotSafetyUiTests
{
    [Fact]
    public void SafetyMutationControls_UseDirectPermissionChecksThatMatchServerGuards()
    {
        var root = RepoRoot;
        var hook = File.ReadAllText(Path.Combine(root, "frontend", "src", "hooks", "usePermission.tsx"));
        var dvir = File.ReadAllText(Path.Combine(root, "frontend", "src", "pages", "DvirInspectionsPage.tsx"));
        var safety = File.ReadAllText(Path.Combine(root, "frontend", "src", "pages", "Batch4SafetyPage.tsx"));

        Assert.Contains("export function useHasDirectPermission", hook, StringComparison.Ordinal);
        Assert.Contains("useHasDirectPermission", dvir, StringComparison.Ordinal);
        Assert.Contains("Immutable activity timeline", dvir, StringComparison.Ordinal);
        Assert.Contains("dvirApi.timeline", dvir, StringComparison.Ordinal);
        Assert.Contains("canMutate", safety, StringComparison.Ordinal);
        Assert.Contains("hasDirectPermission(permission)", safety, StringComparison.Ordinal);
        Assert.Contains("driver acknowledged", safety, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("role=\"dialog\"", safety, StringComparison.Ordinal);
        Assert.Contains("aria-modal=\"true\"", safety, StringComparison.Ordinal);
        Assert.Contains("requiredFields = kind === \"coaching\"", safety, StringComparison.Ordinal);
        Assert.Contains("[\"driverId\", \"coachingType\", \"title\", \"description\"]", safety, StringComparison.Ordinal);
        Assert.Contains("disabled={saving || missingRequired}", safety, StringComparison.Ordinal);
        Assert.Contains("Driver, coaching type, title, and description are required.", safety, StringComparison.Ordinal);
        Assert.Contains("Complete coaching task", safety, StringComparison.Ordinal);
        Assert.Contains("completionNote", safety, StringComparison.Ordinal);
        Assert.Contains("afterSafetyScore", safety, StringComparison.Ordinal);
        Assert.Contains("disabled={saving || !valid}", safety, StringComparison.Ordinal);

        var scorecards = File.ReadAllText(Path.Combine(root, "frontend", "src", "pages", "DriverScorecardsPage.tsx"));
        Assert.Contains("useHasDirectPermission", scorecards, StringComparison.Ordinal);
        Assert.Contains("idempotencyKey: payload.idempotencyKey", scorecards, StringComparison.Ordinal);
    }

    [Fact]
    public void IncidentEvidenceRendersVerificationCustodyAndRetrievalTruth()
    {
        var frontend = ReadFrontend("pages", "Batch4SafetyPage.tsx");
        var backend = File.ReadAllText(Path.Combine(RepoRoot, "backend-dotnet", "Controllers", "EndpointMappings.cs"));

        foreach (var field in new[] { "evidenceUrl", "contentHash", "verificationStatus", "custodyStatus", "retrievalStatus" })
            Assert.Contains(field, frontend, StringComparison.Ordinal);
        Assert.Contains("evidence_json->>'verificationStatus' verification_status", backend, StringComparison.Ordinal);
        Assert.Contains("evidence_json->>'custodyStatus' custody_status", backend, StringComparison.Ordinal);
        Assert.Contains("evidence_json->>'retrievalStatus' retrieval_status", backend, StringComparison.Ordinal);
    }

    [Fact]
    public void SafetyDialogs_HandleOnlyTheTopmostLayerAndBindTheirFocusRefs()
    {
        var hook = ReadFrontend("hooks", "useDialogFocus.ts");
        var safety = ReadFrontend("pages", "Batch4SafetyPage.tsx");

        Assert.Contains("const isTopmostDialog", hook, StringComparison.Ordinal);
        Assert.Contains("if (!isTopmostDialog()) return", hook, StringComparison.Ordinal);
        foreach (var dialogRef in new[]
        {
            "detailDialogRef",
            "recordDialogRef",
            "incidentActionDialogRef",
            "coachingNoteDialogRef",
            "coachingCompleteDialogRef",
        })
        {
            Assert.Contains($"const {dialogRef} = useDialogFocus", safety, StringComparison.Ordinal);
            Assert.Contains($"ref={{{dialogRef}}}", safety, StringComparison.Ordinal);
        }
    }

    private static string RepoRoot =>
        Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../"));

    private static string ReadFrontend(params string[] parts)
    {
        var path = Path.Combine(new[] { RepoRoot, "frontend", "src" }.Concat(parts).ToArray());
        return File.ReadAllText(path);
    }

    [Theory]
    [InlineData("pages", "FleetWorkspacePage.tsx", "fleet:manage", "Read-only Fleet Workspace")]
    [InlineData("pages", "FleetColdChainPage.tsx", "fleet:manage", "Read-only Cold Chain Monitor")]
    [InlineData("pages", "FleetAssetManagementPage.tsx", "fleet:manage", "Read-only Returnable Assets")]
    [InlineData("pages", "FleetSaudiReadinessPage.tsx", "compliance:manage", "Read-only Saudi Readiness")]
    public void PilotFleetMutationSurfaces_ExposePermissionAwareReadOnlyMode(
        string folder, string file, string permission, string readOnlyLabel)
    {
        var source = ReadFrontend(folder, file);

        Assert.Contains("useHasPermission", source, StringComparison.Ordinal);
        var permissionGuard = file == "FleetAssetManagementPage.tsx"
            ? "hasPermission(PERMISSIONS.FLEET_MANAGE)"
            : $"hasPermission('{permission}')";
        Assert.Contains(permissionGuard, source, StringComparison.Ordinal);
        Assert.Contains(readOnlyLabel, source, StringComparison.Ordinal);
    }

    [Fact]
    public void AssetWorkspace_UsesVisibleLoadingState_AndRejectsBlankScans()
    {
        var source = ReadFrontend("pages", "FleetAssetManagementPage.tsx");

        Assert.Contains("if (loading) return <LoadingState />", source, StringComparison.Ordinal);
        Assert.Contains("const scannedValue = forms.scanValue.trim()", source, StringComparison.Ordinal);
        Assert.Contains("if (!scannedValue)", source, StringComparison.Ordinal);
        Assert.Contains("{canManageFleet ? <section", source, StringComparison.Ordinal);
        Assert.Contains("disabled={!forms.scanValue.trim()}", source, StringComparison.Ordinal);
        Assert.DoesNotContain("disabled={!canManageFleet || !forms.scanValue.trim()}", source, StringComparison.Ordinal);
        Assert.DoesNotContain("if (loading) return <div className=\"min-h-screen bg-slate-950\" />", source, StringComparison.Ordinal);
    }

    [Fact]
    public void ShipmentDrawer_UsesRealPermissionAwareCarrierAssignmentControl()
    {
        var source = ReadFrontend("components", "fleet", "ShipmentLifecycleDrawer.tsx");

        Assert.Contains("handleAssignCarrier", source, StringComparison.Ordinal);
        Assert.Contains("fleetCommercialApi.assignShipmentCarrier", source, StringComparison.Ordinal);
        Assert.Contains("disabled={!canManage || !carrierId", source, StringComparison.Ordinal);
        Assert.DoesNotContain("Not included in the customer pilot", source, StringComparison.Ordinal);
    }

    [Fact]
    public void TrackingWorkspace_LabelsCanonicalFreshnessAndNonLiveProjections()
    {
        var page = ReadFrontend("pages", "FleetWorkspacePage.tsx");
        var service = ReadFrontend("services", "fleetTmsApi.ts");

        Assert.Contains("Non-live workspace projection", page, StringComparison.Ordinal);
        Assert.Contains("point.freshnessStatus", page, StringComparison.Ordinal);
        Assert.Contains("point.source", page, StringComparison.Ordinal);
        Assert.Contains("isLive?: boolean", service, StringComparison.Ordinal);
        Assert.Contains("freshnessSeconds?: number", service, StringComparison.Ordinal);
    }

    [Fact]
    public void TenantAuth_HidesBfcacheDocumentsUntilServerSessionRevalidationCompletes()
    {
        var source = ReadFrontend("hooks", "useAuth.tsx");

        Assert.Contains("event.persisted", source, StringComparison.Ordinal);
        Assert.Contains("document.documentElement.style.visibility = \"hidden\"", source, StringComparison.Ordinal);
        Assert.Contains("authApi.me()", source, StringComparison.Ordinal);
        Assert.Contains("document.documentElement.style.visibility = \"\"", source, StringComparison.Ordinal);
        Assert.Contains("window.addEventListener(\"pagehide\"", source, StringComparison.Ordinal);
        Assert.Contains("window.addEventListener(\"pageshow\"", source, StringComparison.Ordinal);
    }

    [Fact]
    public void BrowserCsrf_ServerTokenRemainsAuthoritativeAfterSessionBootstrap()
    {
        var client = ReadFrontend("services", "apiClient.ts");
        var store = ReadFrontend("auth", "csrfTokenStore.ts");

        Assert.Contains("hydrateGlobalCsrfToken(inner.csrfToken)", client, StringComparison.Ordinal);
        Assert.DoesNotContain("setGlobalCsrfToken(inner.csrfToken)", client, StringComparison.Ordinal);
        Assert.Contains("export function hydrateGlobalCsrfToken", store, StringComparison.Ordinal);
        Assert.Contains("if (!csrfToken) csrfToken = token", store, StringComparison.Ordinal);
        Assert.Contains("setGlobalCsrfToken(csrfToken)", client, StringComparison.Ordinal);
    }

    [Fact]
    public void BrowserCsrf_UsesOneVersionedRootScopedCookieAcrossApiRoutes()
    {
        var middleware = File.ReadAllText(Path.Combine(RepoRoot, "backend-dotnet", "Middleware", "CsrfMiddleware.cs"));

        Assert.Contains("__CSRF_Token_v2__", middleware, StringComparison.Ordinal);
        Assert.Contains("Path = \"/\"", middleware, StringComparison.Ordinal);
    }
}
