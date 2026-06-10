using System.Security.Claims;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.EntityFrameworkCore;
using Zayra.Api.Data;

namespace Zayra.Api.Infrastructure.Filters;

public class SubscriptionGuardFilter : IAsyncActionFilter
{
    private static readonly string[] SkippedPrefixes =
    {
        "/api/auth",
        "/api/platform",
        "/api/tenant-admin/localization"
    };

    private readonly ZayraDbContext _db;

    public SubscriptionGuardFilter(ZayraDbContext db)
    {
        _db = db;
    }

    public async Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
    {
        var path = context.HttpContext.Request.Path.Value ?? string.Empty;

        // Skip exempt routes
        foreach (var prefix in SkippedPrefixes)
        {
            if (path.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            {
                await next();
                return;
            }
        }

        // Extract tenant_id from JWT claims
        var tenantClaim = context.HttpContext.User.FindFirstValue("tenant_id");
        if (!Guid.TryParse(tenantClaim, out var tenantId))
        {
            // No tenant claim — let the controller handle auth
            await next();
            return;
        }

        var sub = await _db.TenantSubscriptions
            .AsNoTracking()
            .FirstOrDefaultAsync(s => s.TenantId == tenantId);

        // No subscription yet — allow through
        if (sub is null)
        {
            await next();
            return;
        }

        // Suspended or Cancelled
        if (sub.Status is "Suspended" or "Cancelled")
        {
            context.Result = new ObjectResult(new
            {
                error = "subscription_inactive",
                message = "Your subscription is inactive. Please contact support."
            })
            {
                StatusCode = StatusCodes.Status402PaymentRequired
            };
            return;
        }

        // Expired
        if (sub.ExpiresAtUtc.HasValue && sub.ExpiresAtUtc.Value < DateTime.UtcNow)
        {
            context.Result = new ObjectResult(new
            {
                error = "subscription_expired",
                message = "Your subscription has expired. Please renew to continue."
            })
            {
                StatusCode = StatusCodes.Status402PaymentRequired
            };
            return;
        }

        await next();
    }
}
