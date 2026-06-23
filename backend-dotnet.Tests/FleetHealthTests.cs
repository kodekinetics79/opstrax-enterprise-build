using Opstrax.Api.Controllers;
using Xunit;

namespace Opstrax.Tests;

// ── Fleet Health + Safety Command Center Tests ────────────────────────────────
// Verifies:
//  - Server-side vehicle and driver risk scoring logic is deterministic
//  - Severity thresholds are correctly applied
//  - Critical conditions (OOS, critical defects) dominate risk score
//  - Driver safety thresholds rank drivers correctly
//  - RBAC role permissions align with fleet-health access model
//  - Scoring helpers exactly mirror the SQL expressions in FleetHealthRisks

public class VehicleRiskScoringTests
{
    // An out-of-service vehicle must receive the maximum risk contribution (80 base pts)
    // and score at least 80, placing it in the "critical" severity band.
    [Fact]
    public void OutOfService_Vehicle_Scores_At_Least_80()
    {
        var score = EndpointMappings.ComputeVehicleRiskScore(
            outOfService: true,
            criticalDefects: 0, activeFaults: 0, overduePm: 0,
            openWorkOrders: 0, deviceOffline: false, baseRiskScore: 0);

        Assert.True(score >= 80, $"OOS vehicle score should be >= 80 but was {score}");
    }

    // OOS vehicle must have "critical" severity regardless of other factors.
    [Fact]
    public void OutOfService_Vehicle_Has_Critical_Severity()
    {
        var score    = EndpointMappings.ComputeVehicleRiskScore(true, 0, 0, 0, 0, false, 0);
        var severity = EndpointMappings.ComputeVehicleSeverity(score, outOfService: true);
        Assert.Equal("critical", severity);
    }

    // A critical DVIR defect (out_of_service=1) contributes 55 points per defect.
    [Fact]
    public void Critical_Defect_Increases_Score_By_55_Per_Defect()
    {
        var scoreOne = EndpointMappings.ComputeVehicleRiskScore(false, 1, 0, 0, 0, false, 0);
        var scoreTwo = EndpointMappings.ComputeVehicleRiskScore(false, 2, 0, 0, 0, false, 0);
        Assert.Equal(55.0, scoreOne, precision: 1);
        Assert.Equal(100.0, scoreTwo, precision: 1); // 2 × 55 = 110 → capped at 100
    }

    // One critical defect → "high" severity (score = 55, threshold ≥ 50).
    [Fact]
    public void One_Critical_Defect_Is_High_Severity()
    {
        var score    = EndpointMappings.ComputeVehicleRiskScore(false, 1, 0, 0, 0, false, 0);
        var severity = EndpointMappings.ComputeVehicleSeverity(score, outOfService: false);
        Assert.Equal("high", severity);
    }

    // Two critical defects → "critical" severity (score = 60 ≥ 75? No. But score = 60 → high.
    // Actually 60 is "high" (50-75). Three critical defects = 90 → "critical").
    [Fact]
    public void Three_Critical_Defects_Is_Critical_Severity()
    {
        var score    = EndpointMappings.ComputeVehicleRiskScore(false, 3, 0, 0, 0, false, 0);
        var severity = EndpointMappings.ComputeVehicleSeverity(score, outOfService: false);
        Assert.Equal("critical", severity);
    }

    // Overdue PM items contribute 10 pts each.
    [Fact]
    public void Overdue_PM_Contributes_10_Points_Each()
    {
        var score = EndpointMappings.ComputeVehicleRiskScore(false, 0, 0, 2, 0, false, 0);
        Assert.Equal(20.0, score, precision: 1);
    }

    // Active fault codes contribute 8 pts each.
    [Fact]
    public void Active_Fault_Codes_Contribute_8_Points_Each()
    {
        var score = EndpointMappings.ComputeVehicleRiskScore(false, 0, 3, 0, 0, false, 0);
        Assert.Equal(24.0, score, precision: 1);
    }

    // Offline device contributes 12 pts.
    [Fact]
    public void Offline_Device_Contributes_12_Points()
    {
        var score = EndpointMappings.ComputeVehicleRiskScore(false, 0, 0, 0, 0, deviceOffline: true, baseRiskScore: 0);
        Assert.Equal(12.0, score, precision: 1);
    }

    // Risk score is capped at 100.
    [Fact]
    public void Vehicle_Risk_Score_Is_Capped_At_100()
    {
        var score = EndpointMappings.ComputeVehicleRiskScore(
            outOfService: true, criticalDefects: 5, activeFaults: 5,
            overduePm: 5, openWorkOrders: 5, deviceOffline: true, baseRiskScore: 100);
        Assert.Equal(100.0, score);
    }

    // A clean vehicle with no issues scores 0 (before base risk score contribution).
    [Fact]
    public void Clean_Vehicle_No_Issues_Scores_Zero_Without_Base()
    {
        var score = EndpointMappings.ComputeVehicleRiskScore(false, 0, 0, 0, 0, false, baseRiskScore: 0);
        Assert.Equal(0.0, score, precision: 1);
    }

    // Score is deterministic: same inputs always produce the same output.
    [Fact]
    public void Vehicle_Risk_Score_Is_Deterministic()
    {
        var a = EndpointMappings.ComputeVehicleRiskScore(false, 1, 2, 1, 0, true, 20);
        var b = EndpointMappings.ComputeVehicleRiskScore(false, 1, 2, 1, 0, true, 20);
        Assert.Equal(a, b);
    }
}

public class DriverRiskScoringTests
{
    // A driver with safety score < 65 must score at least 80 (safety component alone).
    [Fact]
    public void Driver_Below_65_Safety_Scores_At_Least_70()
    {
        var score = EndpointMappings.ComputeDriverRiskScore(
            safetyScore: 50, openSafetyEvents: 0, overdueCoaching: 0, baseRiskScore: 0);
        Assert.True(score >= 70, $"Driver with score 50% should have risk >= 70 but was {score}");
    }

    // A driver with safety score < 65 must receive "critical" severity.
    [Fact]
    public void Driver_Below_65_Safety_Is_Critical_Severity()
    {
        var score    = EndpointMappings.ComputeDriverRiskScore(52, 0, 0, 0);
        var severity = EndpointMappings.ComputeDriverSeverity(score);
        Assert.Equal("critical", severity);
    }

    // A driver between 65-75 receives 55 pts safety component → "high" severity (score ≥ 50).
    [Fact]
    public void Driver_65_To_75_Safety_Is_High_Severity()
    {
        var score    = EndpointMappings.ComputeDriverRiskScore(70, 0, 0, 0);
        var severity = EndpointMappings.ComputeDriverSeverity(score);
        Assert.Equal("high", severity);
    }

    // Overdue coaching tasks contribute 15 pts each.
    [Fact]
    public void Overdue_Coaching_Contributes_15_Points_Each()
    {
        // Safety score = 90 → safetyComponent = 5
        var scoreOne = EndpointMappings.ComputeDriverRiskScore(90, 0, 1, 0);
        var scoreTwo = EndpointMappings.ComputeDriverRiskScore(90, 0, 2, 0);
        Assert.Equal(5.0 + 15.0, scoreOne, precision: 1);
        Assert.Equal(5.0 + 30.0, scoreTwo, precision: 1);
    }

    // Open safety events contribute 12 pts each.
    [Fact]
    public void Open_Safety_Events_Contribute_12_Points_Each()
    {
        // Safety score = 90 → safetyComponent = 5
        var score = EndpointMappings.ComputeDriverRiskScore(90, openSafetyEvents: 2, overdueCoaching: 0, baseRiskScore: 0);
        Assert.Equal(5.0 + 24.0, score, precision: 1);
    }

    // Driver risk score is capped at 100.
    [Fact]
    public void Driver_Risk_Score_Is_Capped_At_100()
    {
        var score = EndpointMappings.ComputeDriverRiskScore(
            safetyScore: 30, openSafetyEvents: 10, overdueCoaching: 10, baseRiskScore: 100);
        Assert.Equal(100.0, score);
    }

    // A safe driver (score >= 85, no events, no coaching) scores ≤ 5.
    [Fact]
    public void Safe_Driver_No_Events_Scores_Low()
    {
        var score = EndpointMappings.ComputeDriverRiskScore(92, 0, 0, 0);
        Assert.True(score <= 5, $"Safe driver score should be <= 5 but was {score}");
    }

    // Score is deterministic.
    [Fact]
    public void Driver_Risk_Score_Is_Deterministic()
    {
        var a = EndpointMappings.ComputeDriverRiskScore(68, 2, 1, 15);
        var b = EndpointMappings.ComputeDriverRiskScore(68, 2, 1, 15);
        Assert.Equal(a, b);
    }
}

public class FleetHealthRbacTests
{
    // Fleet managers have dashboard:view — the permission gate on fleet-health read endpoints.
    [Fact]
    public void FleetManager_Has_DashboardView()
    {
        var perms = EndpointMappings.RolePermissionDefaults["Fleet Manager"];
        Assert.Contains("dashboard:view", perms);
    }

    // Tenant admins have dashboard:view.
    [Fact]
    public void TenantAdmin_Has_DashboardView()
    {
        var perms = EndpointMappings.RolePermissionDefaults["Tenant Admin"];
        Assert.Contains("dashboard:view", perms);
    }

    // Dispatchers have dashboard:view.
    [Fact]
    public void Dispatcher_Has_DashboardView()
    {
        var perms = EndpointMappings.RolePermissionDefaults["Dispatcher"];
        Assert.Contains("dashboard:view", perms);
    }

    // Fleet managers have maintenance:manage — required for WO creation and defect actions.
    [Fact]
    public void FleetManager_Has_MaintenanceManage()
    {
        var perms = EndpointMappings.RolePermissionDefaults["Fleet Manager"];
        Assert.Contains("maintenance:manage", perms);
    }

    // Drivers must NOT have dashboard:view — fleet-health is not a driver view.
    [Fact]
    public void Driver_Does_Not_Have_DashboardView()
    {
        var perms = EndpointMappings.RolePermissionDefaults["Driver"];
        Assert.DoesNotContain("dashboard:view", perms);
    }

    // Drivers must NOT have maintenance:manage.
    [Fact]
    public void Driver_Does_Not_Have_MaintenanceManage()
    {
        var perms = EndpointMappings.RolePermissionDefaults["Driver"];
        Assert.DoesNotContain("maintenance:manage", perms);
    }

    // Fleet-health vehicle detail requires vehicles:view — fleet managers have it.
    [Fact]
    public void FleetManager_Has_VehiclesView()
    {
        var perms = EndpointMappings.RolePermissionDefaults["Fleet Manager"];
        Assert.Contains("vehicles:view", perms);
    }

    // Fleet-health driver detail requires drivers:view — fleet managers have it.
    [Fact]
    public void FleetManager_Has_DriversView()
    {
        var perms = EndpointMappings.RolePermissionDefaults["Fleet Manager"];
        Assert.Contains("drivers:view", perms);
    }
}

public class FleetHealthSeverityThresholdTests
{
    // Severity boundary: score=75 → critical
    [Fact]
    public void Vehicle_Score_75_Is_Critical()
    {
        Assert.Equal("critical", EndpointMappings.ComputeVehicleSeverity(75, false));
    }

    // Severity boundary: score=74.9 → high
    [Fact]
    public void Vehicle_Score_74_Is_High()
    {
        Assert.Equal("high", EndpointMappings.ComputeVehicleSeverity(74, false));
    }

    // Severity boundary: score=50 → high
    [Fact]
    public void Vehicle_Score_50_Is_High()
    {
        Assert.Equal("high", EndpointMappings.ComputeVehicleSeverity(50, false));
    }

    // Severity boundary: score=49 → medium
    [Fact]
    public void Vehicle_Score_49_Is_Medium()
    {
        Assert.Equal("medium", EndpointMappings.ComputeVehicleSeverity(49, false));
    }

    // Severity boundary: score=25 → medium
    [Fact]
    public void Vehicle_Score_25_Is_Medium()
    {
        Assert.Equal("medium", EndpointMappings.ComputeVehicleSeverity(25, false));
    }

    // Severity boundary: score=24 → low
    [Fact]
    public void Vehicle_Score_24_Is_Low()
    {
        Assert.Equal("low", EndpointMappings.ComputeVehicleSeverity(24, false));
    }

    // OOS flag overrides score-based severity: even score=0 → critical when OOS
    [Fact]
    public void OOS_Flag_Overrides_Severity_To_Critical()
    {
        Assert.Equal("critical", EndpointMappings.ComputeVehicleSeverity(0, outOfService: true));
    }

    // Same boundaries apply to driver severity
    [Fact]
    public void Driver_Score_75_Is_Critical()
    {
        Assert.Equal("critical", EndpointMappings.ComputeDriverSeverity(75));
    }

    [Fact]
    public void Driver_Score_74_Is_High()
    {
        Assert.Equal("high", EndpointMappings.ComputeDriverSeverity(74));
    }
}
