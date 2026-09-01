using System.IO;
using System.Linq;
using Xunit;

namespace Opstrax.Tests;

public sealed class EnterpriseJourneyHardeningTests
{
    private static string RepoRoot => Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../"));
    private static string Read(params string[] parts) =>
        File.ReadAllText(Path.Combine(new[] { RepoRoot }.Concat(parts).ToArray()));

    [Fact]
    public void WorkOrdersUseRealFleetSelectionAndEvidenceBearingTransitions()
    {
        var page = Read("frontend", "src", "pages", "MaintenanceCommandPage.tsx");
        var apiService = Read("frontend", "src", "services", "maintenanceApi.ts");
        var endpoint = Read("backend-dotnet", "Controllers", "EndpointMappings.cs");
        var dvir = Read("backend-dotnet", "Controllers", "DvirHosEndpoints.cs");
        var availabilityService = Read("backend-dotnet", "Services", "MaintenanceBackgroundService.cs");

        Assert.Contains("queryFn: vehiclesApi.list", page);
        Assert.Contains("Create work order", page);
        Assert.Contains("Actual cost", page);
        Assert.Contains("Service notes", page);
        Assert.Contains("Resolution notes", page);
        Assert.Contains("useDialogFocus", page);
        Assert.Contains("rowVersion: Number(resolveTarget.rowVersion ?? resolveTarget.row_version)", page);
        Assert.Contains("repair certification and driver acknowledgment", page, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("resolveDefect: (id: number | string, rowVersion: number, notes: string)", apiService);
        Assert.Contains("{ rowVersion, notes }", apiService);
        Assert.Contains("/api/maintenance/defects/{id:long}/resolve\",     DvirDefectResolvePilot", endpoint);
        Assert.DoesNotContain("MaintDefectResolve(", endpoint);
        Assert.Contains("private static async Task<IResult> DvirDefectResolvePilot", dvir);
        Assert.Contains("rowVersion is required", dvir);
        Assert.Contains("Repair notes must be at least 3 characters", dvir);
        Assert.Contains("Repair notes must be 2000 characters or fewer", dvir);
        Assert.Contains("row_version=row_version+1", dvir);
        Assert.Contains("(@branchId::bigint IS NULL OR branch_id=@branchId)", dvir);
        Assert.Contains("repair certification and driver acknowledgment are still required", dvir);
        Assert.Contains("actual_cost=@cost", endpoint);
        Assert.Contains("notes=CASE", endpoint);
        Assert.Contains("actualCost is required and must be zero or greater", endpoint);
        Assert.Contains("service notes must be at least 3 characters", endpoint);
        Assert.Contains("title is required and must be 220 characters or fewer", endpoint);
        Assert.Contains("serviceType is required and must be 120 characters or fewer", endpoint);
        Assert.Contains("priority must be Low, Medium, High, or Critical", endpoint);
        Assert.Contains("estimatedCost is required and must be zero or greater", endpoint);
        Assert.Contains("scheduledAt is required in YYYY-MM-DD format", endpoint);
        Assert.Contains("Active assignee not found in the authorized tenant and branch", endpoint);
        Assert.Contains("LOWER(COALESCE(u.status,''))='active'", endpoint);
        Assert.Contains("COALESCE(v.branch_id,av.branch_id)=@branchId", endpoint);
        Assert.Contains("JOIN vehicles v ON v.id=dr.vehicle_id AND v.company_id=dr.company_id", endpoint);
        Assert.Contains("var woCode = $\"WO-{companyId}-{Guid.NewGuid():N}\"", endpoint);
        Assert.True(Occurrences(endpoint, "COALESCE(dr.repair_certification_status,'')<>'Certified'") >= 2);
        Assert.True(Occurrences(endpoint, "dr.driver_repair_acknowledged_at IS NULL") >= 2);
        Assert.True(Occurrences(endpoint, "wo.status IN ('in_progress','waiting_parts','In Progress','Waiting Parts')") >= 2);
        Assert.Contains("diagnostic_holds dh", availabilityService);
        Assert.Contains("COALESCE(dr.repair_certification_status,'')<>'Certified'", availabilityService);
        Assert.Contains("dr.driver_repair_acknowledged_at IS NULL", availabilityService);
        Assert.Contains("vehicle.plateNumber ?? vehicle.plate_number", page);
    }

    [Fact]
    public void DriverDeliveryRequiresProofAndArtifactReadsStayInsideAssignmentTenant()
    {
        var page = Read("frontend", "src", "pages", "driver", "DriverAssignmentPage.tsx");
        var endpoint = Read("backend-dotnet", "Controllers", "EndpointMappings.cs");

        Assert.Contains("Delivery cannot be closed until", page);
        Assert.Contains("does not queue proof offline", page);
        Assert.Contains("proofType == \"delivery\" && requestedReferences.Length == 0", endpoint);
        Assert.Contains("FROM dispatch_proof_uploads", endpoint);
        Assert.Contains("AND consumed_at IS NULL AND reference=ANY(@refs)", endpoint);
        Assert.Contains("JOIN dispatch_proofs p ON p.id=pa.proof_id AND p.company_id=pa.company_id", endpoint);
        Assert.Contains("JOIN dispatch_assignments da ON da.id=p.assignment_id AND da.company_id=p.company_id", endpoint);
        Assert.Contains("new { assignment, exceptions, proofs, proofArtifacts, auditTrail }", endpoint);
    }

    [Fact]
    public void UserEditorDoesNotClaimIgnoredPasswordWrites()
    {
        var page = Read("frontend", "src", "pages", "AdminPage.tsx");
        var editBranch = Block(page, "else if (userModal === \"edit\"", "setUserModal(null)");

        Assert.DoesNotContain("body.password", editBranch);
        Assert.DoesNotContain("Leave blank to keep current password", page);
        Assert.Contains("Passwords are not changed from this form", page);
        Assert.Contains("Generate activation link", page);
        Assert.Contains("Sign out all sessions", page);
    }

    [Fact]
    public void LiveSurfacesKeepUnknownAndAlertFailureDistinctFromAllClear()
    {
        var map = Read("frontend", "src", "pages", "LiveMapPage.tsx");
        var monitor = Read("frontend", "src", "pages", "VehicleLiveMonitorPage.tsx");
        var wall = Read("frontend", "src", "pages", "FleetLiveWallPage.tsx");

        Assert.Contains("type StatusBucket = \"Moving\" | \"Idle\" | \"Offline\" | \"Unknown\"", map);
        Assert.Contains("Alert feed unavailable. No all-clear can be confirmed.", map);
        Assert.Contains("Retry telemetry snapshot", map);
        Assert.Contains("if (!hasFreshnessAge) return \"Unknown\"", map);
        Assert.Contains("live|fresh|online|healthy|delayed", map);
        // A KPI tile must render "--" when the API omits the value, never a fabricated
        // number. The KEYS changed -- onlineDevices/onlineCameras/telemetryQuality/
        // speedAlerts are emitted by no endpoint, so that header read "-- -- -- --"
        // permanently -- but the honesty requirement is unchanged, and is now asserted
        // across all four tiles instead of one.
        Assert.Contains("kpis.registeredDevices ?? \"--\"", map);
        Assert.Contains("kpis.connectedUnits ?? \"--\"", map);
        Assert.Contains("kpis.openAlerts ?? \"--\"", map);
        Assert.Contains("kpis.liveCoverage != null ?", map);
        // The camera tile is deliberately absent: vehicles.camera_status defaults to
        // 'Online' and is never recomputed, so any camera figure would be invented.
        Assert.DoesNotContain("kpis.onlineCameras", map);
        Assert.Contains("s === \"healthy\"", monitor);
        Assert.Contains("Unknown telemetry", monitor);
        Assert.Contains("Alert state unknown", wall);
    }

    [Fact]
    public void CoordinateValidationRejectsOnlySentinelAndOutOfRangeValues()
    {
        var map = Read("frontend", "src", "components", "LiveMap.tsx");
        var page = Read("frontend", "src", "pages", "LiveMapPage.tsx");

        Assert.Contains("Math.abs(lat) <= 90 && Math.abs(lng) <= 180 && !(lat === 0 && lng === 0)", map);
        Assert.Contains("Math.abs(lat) <= 90 && Math.abs(lng) <= 180 && !(lat === 0 && lng === 0)", page);
        Assert.DoesNotContain("lat !== 0 && lng !== 0", map);
        Assert.DoesNotContain("lat !== 0 && lng !== 0", page);
    }

    [Fact]
    public void NavigationAndFinanceExposeHonestResponsiveContracts()
    {
        var tenantShell = Read("frontend", "src", "layouts", "AppShell.tsx");
        var platformShell = Read("frontend", "src", "layouts", "PlatformShell.tsx");
        var finance = Read("frontend", "src", "pages", "FinancialAnalyticsPage.tsx");
        var tracking = Read("frontend", "src", "pages", "PublicShipmentTrackingPage.tsx");

        Assert.Contains("id=\"tenant-mobile-navigation\"", tenantShell);
        Assert.Contains("aria-modal=\"true\"", tenantShell);
        Assert.Contains("id=\"platform-mobile-navigation\"", platformShell);
        Assert.Contains("Open platform navigation", platformShell);
        Assert.Contains("totalsByCurrency", finance);
        Assert.Contains("Outstanding (${currency})", finance);
        Assert.DoesNotContain("label=\"ETA\"", tracking);
        Assert.Contains("Planned arrival", tracking);
    }

    [Fact]
    public void SafetyDoesNotGeneratePlaceholderMediaOrFakeExportFiles()
    {
        var endpoint = Read("backend-dotnet", "Controllers", "EndpointMappings.cs");
        var page = Read("frontend", "src", "pages", "Batch4SafetyPage.tsx");

        Assert.DoesNotContain("/placeholder/dashcam-thumb.jpg", endpoint);
        Assert.DoesNotContain("CONCAT('/exports/evidence-package-'", endpoint);
        Assert.Contains("Evidence export generation is not configured; no file was created", endpoint);
        Assert.Contains("mediaAvailable = false", endpoint);
        Assert.Contains("actions: [\"lock\"]", page);
    }

    [Fact]
    public void VehicleCreationAndArchiveDoNotPretendTelemetryOrDeletion()
    {
        var endpoint = Read("backend-dotnet", "Controllers", "EndpointMappings.cs");
        var page = Read("frontend", "src", "pages", "VehiclesPage.tsx");
        var module = Read("frontend", "src", "pages", "VehiclesModulePage.tsx");

        Assert.Contains("0, 0, 'Unknown', 'Unknown'", endpoint);
        Assert.DoesNotContain("92, 96, 'Unknown', 'Unknown'", endpoint);
        Assert.Contains("AVG(readiness_score) FILTER", endpoint);
        Assert.Contains("function hasReadinessEvidence", page);
        Assert.Contains("readiness == null ? \"Unknown\"", page);
        Assert.Contains("if (value == null) return", page);
        Assert.Contains("function hasReadinessEvidence", module);
        Assert.Contains("readiness == null ? \"Unknown\"", module);
        Assert.Contains("Archive vehicle", page);
        Assert.Contains("leave the active fleet registry", page);
        Assert.Contains("No partial-page fallback was downloaded", page);
    }

    [Fact]
    public void MaintenancePlanningShowsFailuresAndExportsOnlyLiveServerRows()
    {
        var page = Read("frontend", "src", "pages", "MaintenancePlanningPage.tsx");

        Assert.DoesNotContain("withFallback", page);
        Assert.DoesNotContain("buildServiceHistorySeed", page);
        Assert.DoesNotContain("totalHours * 280", page);
        Assert.Contains("actualCost ?? r.actual_cost", page);
        Assert.Contains("useTenantCurrency", page);
        Assert.Contains("if (q.isError) return <ErrorState", page);
        Assert.Contains("exportCsv(\"service-history\", await serviceHistoryApi())", page);
        Assert.Contains("exportCsv(\"downtime\", await downtimeApi())", page);
        Assert.Contains("exportCsv(\"preventive-maintenance\", await pmApi())", page);
        Assert.Contains("const canExport = hasPermission(\"reports:export\")", page);
        Assert.Contains("if (!canExport) return", page);
        Assert.Contains("{canExport && <button", page);

        var command = Read("frontend", "src", "pages", "MaintenanceCommandPage.tsx");
        Assert.Contains("const canExport = hasPermission(\"reports:export\")", command);
        Assert.Contains("{canExport && <button", command);
        Assert.Contains("exportCsv(\"maintenance-defects\"", command);

        var endpoints = Read("backend-dotnet", "Controllers", "EndpointMappings.cs");
        var recommendations = Block(endpoints,
            "app.MapGet(\"/api/maintenance/recommendations\"",
            "app.MapGet(\"/api/maintenance\", MaintenanceItems)");
        Assert.Contains("RequirePermission(http, \"maintenance:view\")", recommendations);
        Assert.DoesNotContain("telemetry.recommendations.read", recommendations);
    }

    private static string Block(string source, string startMarker, string endMarker)
    {
        var start = source.IndexOf(startMarker, StringComparison.Ordinal);
        var end = source.IndexOf(endMarker, start + startMarker.Length, StringComparison.Ordinal);
        Assert.True(start >= 0 && end > start);
        return source[start..end];
    }

    private static int Occurrences(string source, string marker)
    {
        var count = 0;
        var offset = 0;
        while ((offset = source.IndexOf(marker, offset, StringComparison.Ordinal)) >= 0)
        {
            count++;
            offset += marker.Length;
        }
        return count;
    }
}
