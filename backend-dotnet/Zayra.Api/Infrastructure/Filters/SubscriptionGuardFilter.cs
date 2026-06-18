using System.Security.Claims;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Zayra.Api.Data;
using Zayra.Api.Models;

namespace Zayra.Api.Infrastructure.Filters;

public class SubscriptionGuardFilter : IAsyncActionFilter
{
    private static readonly string[] SkippedPrefixes =
    {
        "/api/auth",
        "/api/platform",
        "/api/tenant-admin/localization",
        "/api/health",
        "/api/version",
    };

    private readonly ZayraDbContext _db;
    private readonly ILogger<SubscriptionGuardFilter> _log;

    public SubscriptionGuardFilter(ZayraDbContext db, ILogger<SubscriptionGuardFilter> log)
    {
        _db = db;
        _log = log;
    }

    public async Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
    {
        var path = context.HttpContext.Request.Path.Value ?? string.Empty;

        foreach (var prefix in SkippedPrefixes)
        {
            if (path.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            {
                await next();
                return;
            }
        }

        var tenantClaim = context.HttpContext.User.FindFirstValue("tenant_id");
        if (!Guid.TryParse(tenantClaim, out var tenantId))
        {
            await next();
            return;
        }

        var sub = await _db.TenantSubscriptions
            .AsNoTracking()
            .FirstOrDefaultAsync(s => s.TenantId == tenantId);

        if (sub is null)
        {
            await next();
            return;
        }

        // Suspended or Cancelled — full block.
        if (sub.Status is SubscriptionStatuses.Suspended or SubscriptionStatuses.Cancelled)
        {
            _log.LogWarning(
                "SubscriptionGuard blocked request — inactive. Tenant={TenantId} Status={Status} Path={Path} IP={IP} UserAgent={UserAgent}",
                tenantId,
                sub.Status,
                path,
                context.HttpContext.Connection.RemoteIpAddress?.ToString() ?? "",
                context.HttpContext.Request.Headers.UserAgent.ToString());

            context.Result = new ObjectResult(new
            {
                error = "subscription_inactive",
                status = sub.Status,
                message = "Your subscription is inactive. Please contact support."
            })
            { StatusCode = StatusCodes.Status402PaymentRequired };
            return;
        }

        // ManualContract — enterprise/bespoke deal, no expiry enforcement; always allow.
        if (sub.Status == SubscriptionStatuses.ManualContract)
        {
            await next();
            return;
        }

        // Expired (applies to Trial, Active, PastDue).
        if (sub.ExpiresAtUtc.HasValue && sub.ExpiresAtUtc.Value < DateTime.UtcNow)
        {
            _log.LogWarning(
                "SubscriptionGuard blocked request — expired. Tenant={TenantId} Status={Status} ExpiredAt={ExpiresAtUtc} Path={Path} IP={IP} UserAgent={UserAgent}",
                tenantId,
                sub.Status,
                sub.ExpiresAtUtc,
                path,
                context.HttpContext.Connection.RemoteIpAddress?.ToString() ?? "",
                context.HttpContext.Request.Headers.UserAgent.ToString());

            context.Result = new ObjectResult(new
            {
                error = "subscription_expired",
                status = sub.Status,
                message = "Your subscription has expired. Please renew to continue."
            })
            { StatusCode = StatusCodes.Status402PaymentRequired };
            return;
        }

        // PastDue — allow through but signal it in a response header so the frontend can show a banner.
        if (sub.Status == SubscriptionStatuses.PastDue)
        {
            context.HttpContext.Response.Headers["X-Subscription-Status"] = SubscriptionStatuses.PastDue;
        }

        await next();
    }
}
