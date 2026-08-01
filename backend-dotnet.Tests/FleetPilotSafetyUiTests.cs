using System;
using System.IO;
using System.Linq;
using Xunit;

namespace Opstrax.Tests;

public class FleetPilotSafetyUiTests
{
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
        Assert.Contains($"hasPermission('{permission}')", source, StringComparison.Ordinal);
        Assert.Contains(readOnlyLabel, source, StringComparison.Ordinal);
    }

    [Fact]
    public void AssetWorkspace_UsesVisibleLoadingState_AndRejectsBlankScans()
    {
        var source = ReadFrontend("pages", "FleetAssetManagementPage.tsx");

        Assert.Contains("if (loading) return <LoadingState />", source, StringComparison.Ordinal);
        Assert.Contains("const scannedValue = forms.scanValue.trim()", source, StringComparison.Ordinal);
        Assert.Contains("if (!scannedValue)", source, StringComparison.Ordinal);
        Assert.Contains("disabled={!canManageFleet || !forms.scanValue.trim()}", source, StringComparison.Ordinal);
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
}
