using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Zayra.Api.Data;
using Zayra.Api.Infrastructure.Compliance;
using Zayra.Api.Models;

namespace Zayra.Api.Controllers;

/// <summary>
/// Saudi regulatory compliance dashboard (QIWA + WPS + GOSI readiness).
///
/// This route is NOT in the FeatureFlagGuardFilter prefix map, so the feature
/// gate is enforced here: the tenant must have at least one of qiwa_integration,
/// wps_export, payroll or compliance enabled.  All data is tenant-scoped.
/// </summary>
[ApiController]
[Route("api/saudi-compliance")]
[Authorize]
public class SaudiComplianceController : ControllerBase
{
    private readonly SaudiComplianceDashboardService _dashboard;
    private readonly GosiReadinessReportService _readiness;
    private readonly ZayraDbContext _db;

    private static readonly string[] GatingFeatures =
    {
        FeatureKeys.QiwaIntegration, FeatureKeys.WpsExport, FeatureKeys.Payroll, FeatureKeys.Compliance
    };

    public SaudiComplianceController(
        SaudiComplianceDashboardService dashboard,
        GosiReadinessReportService readiness,
        ZayraDbContext db)
    {
        _dashboard = dashboard;
        _readiness = readiness;
        _db = db;
    }

    [HttpGet("dashboard")]
    public async Task<IActionResult> GetDashboard(CancellationToken cancellationToken)
    {
        if (!HasPermission("compliance.read") && !HasPermission("qiwa.read"))
            return Forbid();

        var tenantId = RequireTenant();
        if (!await HasAnyGatingFeatureAsync(tenantId, cancellationToken))
            return StatusCode(403, new
            {
                error = "feature_not_enabled",
                message = "Saudi compliance requires one of: QIWA, WPS, Payroll or Compliance modules."
            });

        return Ok(await _dashboard.BuildAsync(tenantId, cancellationToken));
    }

    /// <summary>GOSI readiness report using official contribution rules (PR-3 GosiCalculationService).</summary>
    [HttpGet("gosi-readiness")]
    public async Task<IActionResult> GetGosiReadiness(CancellationToken cancellationToken)
    {
        if (!HasPermission("compliance.read")) return Forbid();

        var tenantId = RequireTenant();
        if (!await HasAnyGatingFeatureAsync(tenantId, cancellationToken))
            return StatusCode(403, new { error = "feature_not_enabled" });

        return Ok(await _readiness.BuildAsync(tenantId, cancellationToken));
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    // Absent flag = feature enabled by default (see FeaturesController design).
    // Block only if every gating feature is explicitly disabled.
    private async Task<bool> HasAnyGatingFeatureAsync(Guid tenantId, CancellationToken ct)
    {
        var explicitlyDisabled = await _db.TenantFeatureFlags
            .CountAsync(f => f.TenantId == tenantId && GatingFeatures.Contains(f.FeatureKey) && !f.IsEnabled, ct);
        return explicitlyDisabled < GatingFeatures.Length;
    }

    private Guid RequireTenant()
        => Guid.Parse(User.FindFirstValue("tenant_id") ?? throw new UnauthorizedAccessException("Tenant claim missing."));

    private bool HasPermission(string permission) =>
        User.Claims.Any(c => c.Type == "permission" && string.Equals(c.Value, permission, StringComparison.OrdinalIgnoreCase));
}
