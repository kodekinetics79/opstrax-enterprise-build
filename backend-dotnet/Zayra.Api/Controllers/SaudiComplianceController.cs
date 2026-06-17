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
    private readonly ZayraDbContext _db;

    private static readonly string[] GatingFeatures =
    {
        FeatureKeys.QiwaIntegration, FeatureKeys.WpsExport, FeatureKeys.Payroll, FeatureKeys.Compliance
    };

    public SaudiComplianceController(SaudiComplianceDashboardService dashboard, ZayraDbContext db)
    {
        _dashboard = dashboard;
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

    /// <summary>GOSI readiness report with illustrative contribution estimates (Task 9).</summary>
    [HttpGet("gosi-readiness")]
    public async Task<IActionResult> GetGosiReadiness(CancellationToken cancellationToken)
    {
        if (!HasPermission("compliance.read")) return Forbid();

        var tenantId = RequireTenant();
        if (!await HasAnyGatingFeatureAsync(tenantId, cancellationToken))
            return StatusCode(403, new { error = "feature_not_enabled" });

        var company = await _db.Companies.AsNoTracking()
            .FirstOrDefaultAsync(c => c.TenantId == tenantId, cancellationToken);
        var companyGosiEmployerId = company?.GosiEmployerId ?? string.Empty;

        var employees = await _db.Employees.AsNoTracking()
            .Where(e => e.TenantId == tenantId && !e.IsDeleted && e.Status == "Active")
            .ToListAsync(cancellationToken);

        var salaries = await _db.EmployeeSalaryStructures.AsNoTracking()
            .Where(s => s.TenantId == tenantId)
            .ToListAsync(cancellationToken);

        var rows = new List<object>();
        var readyCount = 0;
        foreach (var e in employees)
        {
            var gaps = new List<string>();
            if (string.IsNullOrWhiteSpace(e.GosiReference))        gaps.Add("gosi_reference");
            if (string.IsNullOrWhiteSpace(companyGosiEmployerId))  gaps.Add("gosi_employer_id");
            if (string.IsNullOrWhiteSpace(e.Nationality))          gaps.Add("nationality");

            var basic = salaries
                .Where(s => s.EmployeeId == e.Id)
                .OrderByDescending(s => s.EffectiveDate)
                .Select(s => s.BasicSalary)
                .FirstOrDefault();
            if (basic <= 0) gaps.Add("contract_salary");

            // Illustrative GOSI contribution estimate.
            var isSaudi = string.Equals(e.SaudiOrNonSaudi, "Saudi", StringComparison.OrdinalIgnoreCase)
                          || string.Equals(e.Nationality, "Saudi", StringComparison.OrdinalIgnoreCase)
                          || string.Equals(e.Nationality, "Saudi Arabian", StringComparison.OrdinalIgnoreCase);

            var employeeRate = isSaudi ? 0.10m : 0.00m;   // 10% employee (Saudi only)
            var employerRate = isSaudi ? 0.12m : 0.02m;   // 12% employer (Saudi) / 2% (Non-Saudi occupational hazard)

            if (gaps.Count == 0) readyCount++;

            rows.Add(new
            {
                employeeId = e.Id,
                employeeCode = e.EmployeeCode,
                fullName = e.FullName,
                nationality = e.Nationality,
                category = isSaudi ? "Saudi" : "NonSaudi",
                basicSalary = basic,
                estimatedEmployeeContribution = Math.Round(basic * employeeRate, 2),
                estimatedEmployerContribution = Math.Round(basic * employerRate, 2),
                gaps,
                ready = gaps.Count == 0
            });
        }

        return Ok(new
        {
            totalEmployees = employees.Count,
            ready = readyCount,
            employees = rows,
            disclaimer = "GOSI contribution rates shown are illustrative. Verify current rates with GOSI portal."
        });
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
