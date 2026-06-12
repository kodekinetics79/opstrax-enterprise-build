using System.Security.Claims;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.EntityFrameworkCore;
using Zayra.Api.Data;
using Zayra.Api.Models;

namespace Zayra.Api.Infrastructure.Filters;

/// <summary>
/// Blocks access to optional modules when a tenant has that feature explicitly disabled
/// (IsEnabled = false in tenant_feature_flags).
///
/// Default policy: absent flag = allowed (backwards compat for existing tenants).
/// To restrict a module for a tenant, set IsEnabled = false via the platform admin.
/// </summary>
public class FeatureFlagGuardFilter : IAsyncActionFilter
{
    // Routes that are always allowed regardless of feature flags (core HR + infra).
    private static readonly string[] AlwaysAllowedPrefixes =
    {
        "/api/auth",
        "/api/platform",
        "/api/tenant-admin",
        "/api/access",
        "/api/employees",
        "/api/branches",
        "/api/departments",
        "/api/designations",
        "/api/grades",
        "/api/cost-centers",
        "/api/companies",
        "/api/organization",
        "/api/dashboard",
        "/api/leave",
        "/api/attendance",
        "/api/approvals",
        "/api/approval-requests",
        "/api/approval-workflows",
        "/api/notifications",
        "/api/reports",
        "/api/analytics",
        "/api/ess",
        "/api/hr-requests",
        "/api/audit-logs",
        "/api/help-text",
        "/api/localization",
        "/api/master-data",
        "/api/setup",
        "/api/policy-documents",
        "/api/features",       // read-only tenant feature visibility — must never gate itself
    };

    // Route-prefix → feature key.  Order matters: first match wins.
    private static readonly (string Prefix, string FeatureKey)[] RouteFeatureMap =
    {
        ("/api/recruitment",    FeatureKeys.Recruitment),
        ("/api/performance",    FeatureKeys.Performance),
        ("/api/visa-tracking",  FeatureKeys.Compliance),
        ("/api/compliance",     FeatureKeys.Compliance),
        ("/api/contracts",      FeatureKeys.Compliance),
        ("/api/ai-assistant",   FeatureKeys.AiAssistant),
        ("/api/ai",             FeatureKeys.AiAssistant),
        ("/api/loans",          FeatureKeys.Finance),
        ("/api/advances",       FeatureKeys.Finance),
        ("/api/bonuses",        FeatureKeys.Finance),
        ("/api/payroll",        FeatureKeys.Payroll),
        ("/api/shifts",         FeatureKeys.Shifts),
        ("/api/overtime",       FeatureKeys.Overtime),
        ("/api/mobile",         FeatureKeys.MobileApp),
        ("/api/qiwa",           FeatureKeys.QiwaIntegration),
    };

    private readonly ZayraDbContext _db;

    public FeatureFlagGuardFilter(ZayraDbContext db) => _db = db;

    public async Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
    {
        var path = context.HttpContext.Request.Path.Value ?? string.Empty;

        foreach (var prefix in AlwaysAllowedPrefixes)
        {
            if (path.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            {
                await next();
                return;
            }
        }

        string? featureKey = null;
        foreach (var (prefix, key) in RouteFeatureMap)
        {
            if (path.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            {
                featureKey = key;
                break;
            }
        }

        if (featureKey is null)
        {
            // Route not mapped — not a gated module, allow.
            await next();
            return;
        }

        var tenantClaim = context.HttpContext.User.FindFirstValue("tenant_id");
        if (!Guid.TryParse(tenantClaim, out var tenantId))
        {
            // No tenant context (unauthenticated or platform admin JWT) — let auth handle it.
            await next();
            return;
        }

        var flag = await _db.TenantFeatureFlags
            .AsNoTracking()
            .FirstOrDefaultAsync(f => f.TenantId == tenantId && f.FeatureKey == featureKey);

        // Absent = allowed. Only block when the flag is explicitly set to false.
        if (flag is not null && !flag.IsEnabled)
        {
            context.Result = new ObjectResult(new
            {
                error = "feature_not_enabled",
                feature = featureKey,
                message = $"The '{featureKey}' module is not enabled for your account. Contact your platform administrator."
            })
            { StatusCode = StatusCodes.Status403Forbidden };
            return;
        }

        await next();
    }
}
