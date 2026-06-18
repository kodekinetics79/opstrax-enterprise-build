using System.Security.Claims;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Zayra.Api.Data;
using Zayra.Api.Domain.Entities;
using Zayra.Api.Infrastructure.Filters;
using Zayra.Api.Models;

namespace Zayra.Api.Tests.Security;

/// <summary>
/// Unit-tests for <see cref="FeatureFlagGuardFilter"/>.
/// Verifies that:
///   1. Disabled features return HTTP 403 with error = "feature_not_enabled".
///   2. Enabled features pass through.
///   3. Absent flags (no row in DB) default to ALLOWED (backwards compat).
///   4. Always-allowed prefixes (/api/auth, /api/employees, etc.) bypass the guard.
///   5. Platform admin requests (no tenant_id claim) bypass the guard.
/// </summary>
public class FeatureFlagGuardTests
{
    private static ZayraDbContext CreateDb()
    {
        var opts = new DbContextOptionsBuilder<ZayraDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        return new ZayraDbContext(opts);
    }

    private static (ActionExecutingContext ctx, IActionResult? captured) BuildContext(
        string path,
        Guid? tenantId = null)
    {
        var httpCtx = new DefaultHttpContext();
        httpCtx.Request.Path = path;

        if (tenantId.HasValue)
        {
            var claims = new[] { new Claim("tenant_id", tenantId.Value.ToString()) };
            httpCtx.User = new ClaimsPrincipal(new ClaimsIdentity(claims, "Test"));
        }

        IActionResult? captured = null;
        var actionCtx = new ActionContext(httpCtx, new RouteData(), new ActionDescriptor());
        var ctx = new ActionExecutingContext(actionCtx, [], new Dictionary<string, object?>(), null!);
        return (ctx, captured);
    }

    private static async Task<IActionResult?> RunFilter(
        ZayraDbContext db, string path, Guid tenantId, bool featureEnabled, string featureKey)
    {
        db.TenantFeatureFlags.Add(new TenantFeatureFlag
        {
            TenantId   = tenantId,
            FeatureKey = featureKey,
            IsEnabled  = featureEnabled,
        });
        await db.SaveChangesAsync();

        var httpCtx = new DefaultHttpContext();
        httpCtx.Request.Path = path;
        var claims = new[] { new Claim("tenant_id", tenantId.ToString()) };
        httpCtx.User = new ClaimsPrincipal(new ClaimsIdentity(claims, "Test"));

        IActionResult? result = null;
        var actionCtx = new ActionContext(httpCtx, new RouteData(), new ActionDescriptor());
        var ctx = new ActionExecutingContext(actionCtx, [], new Dictionary<string, object?>(), null!);

        var filter = new FeatureFlagGuardFilter(db, new MemoryCache(new MemoryCacheOptions()));
        await filter.OnActionExecutionAsync(ctx, () =>
        {
            result = null;
            return Task.FromResult(new ActionExecutedContext(actionCtx, [], null!));
        });

        return ctx.Result ?? result;
    }

    // ── Core scenarios ────────────────────────────────────────────────────────

    [Fact]
    public async Task AiAssistant_DisabledForTenant_Returns403()
    {
        await using var db = CreateDb();
        var tenantId = Guid.NewGuid();

        var result = await RunFilter(db, "/api/ai-assistant/chat", tenantId, false, FeatureKeys.AiAssistant);

        result.Should().BeOfType<ObjectResult>()
            .Which.StatusCode.Should().Be(403);

        var body = (result as ObjectResult)!.Value;
        body.Should().NotBeNull();
        body!.ToString().Should().Contain("feature_not_enabled");
    }

    [Fact]
    public async Task AiAssistant_EnabledForTenant_PassesThrough()
    {
        await using var db = CreateDb();
        var tenantId = Guid.NewGuid();

        var result = await RunFilter(db, "/api/ai-assistant/chat", tenantId, true, FeatureKeys.AiAssistant);

        // null result means the next() delegate ran — guard did not block
        result.Should().BeNull();
    }

    [Fact]
    public async Task Recruitment_DisabledForTenant_Returns403()
    {
        await using var db = CreateDb();
        var tenantId = Guid.NewGuid();

        var result = await RunFilter(db, "/api/recruitment/jobs", tenantId, false, FeatureKeys.Recruitment);

        result.Should().BeOfType<ObjectResult>()
            .Which.StatusCode.Should().Be(403);
    }

    [Fact]
    public async Task Performance_EnabledForTenant_PassesThrough()
    {
        await using var db = CreateDb();
        var tenantId = Guid.NewGuid();

        var result = await RunFilter(db, "/api/performance/reviews", tenantId, true, FeatureKeys.Performance);

        result.Should().BeNull();
    }

    [Fact]
    public async Task Payroll_DisabledForTenant_Returns403()
    {
        await using var db = CreateDb();
        var tenantId = Guid.NewGuid();

        var result = await RunFilter(db, "/api/payroll/runs", tenantId, false, FeatureKeys.Payroll);

        result.Should().BeOfType<ObjectResult>()
            .Which.StatusCode.Should().Be(403);
    }

    // ── Absent flag = allowed ─────────────────────────────────────────────────

    [Fact]
    public async Task AbsentFlag_DefaultsToAllowed()
    {
        await using var db = CreateDb();
        var tenantId = Guid.NewGuid();
        // Do NOT add any flag row

        var httpCtx = new DefaultHttpContext();
        httpCtx.Request.Path = "/api/ai-assistant/ask";
        var claims = new[] { new Claim("tenant_id", tenantId.ToString()) };
        httpCtx.User = new ClaimsPrincipal(new ClaimsIdentity(claims, "Test"));

        var actionCtx = new ActionContext(httpCtx, new RouteData(), new ActionDescriptor());
        var ctx = new ActionExecutingContext(actionCtx, [], new Dictionary<string, object?>(), null!);

        var filter = new FeatureFlagGuardFilter(db, new MemoryCache(new MemoryCacheOptions()));
        IActionResult? blockResult = null;
        await filter.OnActionExecutionAsync(ctx, () =>
            Task.FromResult(new ActionExecutedContext(actionCtx, [], null!)));

        ctx.Result.Should().BeNull("absent flag must default to allowed");
    }

    // ── Always-allowed prefixes ───────────────────────────────────────────────

    [Theory]
    [InlineData("/api/auth/login")]
    [InlineData("/api/employees")]
    [InlineData("/api/leave/requests")]
    [InlineData("/api/attendance/records")]
    [InlineData("/api/dashboard/summary")]
    [InlineData("/api/features")]
    [InlineData("/api/approvals")]
    public async Task AlwaysAllowedRoute_BypassesGuard(string path)
    {
        await using var db = CreateDb();
        var tenantId = Guid.NewGuid();

        // Even if we disable AI, the always-allowed route should not be blocked
        db.TenantFeatureFlags.Add(new TenantFeatureFlag
        {
            TenantId = tenantId, FeatureKey = FeatureKeys.AiAssistant, IsEnabled = false
        });
        await db.SaveChangesAsync();

        var httpCtx = new DefaultHttpContext();
        httpCtx.Request.Path = path;
        var claims = new[] { new Claim("tenant_id", tenantId.ToString()) };
        httpCtx.User = new ClaimsPrincipal(new ClaimsIdentity(claims, "Test"));

        var actionCtx = new ActionContext(httpCtx, new RouteData(), new ActionDescriptor());
        var ctx = new ActionExecutingContext(actionCtx, [], new Dictionary<string, object?>(), null!);

        var filter = new FeatureFlagGuardFilter(db, new MemoryCache(new MemoryCacheOptions()));
        await filter.OnActionExecutionAsync(ctx, () =>
            Task.FromResult(new ActionExecutedContext(actionCtx, [], null!)));

        ctx.Result.Should().BeNull($"'{path}' is always-allowed and must not be blocked");
    }

    // ── Platform admin (no tenant_id claim) bypasses guard ───────────────────

    [Fact]
    public async Task PlatformAdminRequest_WithoutTenantId_BypassesGuard()
    {
        await using var db = CreateDb();

        var httpCtx = new DefaultHttpContext();
        httpCtx.Request.Path = "/api/ai-assistant/chat";
        // No tenant_id claim — simulates platform admin JWT
        httpCtx.User = new ClaimsPrincipal(new ClaimsIdentity(new[]
        {
            new Claim("is_platform_admin", "true"),
        }, "Test"));

        var actionCtx = new ActionContext(httpCtx, new RouteData(), new ActionDescriptor());
        var ctx = new ActionExecutingContext(actionCtx, [], new Dictionary<string, object?>(), null!);

        var filter = new FeatureFlagGuardFilter(db, new MemoryCache(new MemoryCacheOptions()));
        await filter.OnActionExecutionAsync(ctx, () =>
            Task.FromResult(new ActionExecutedContext(actionCtx, [], null!)));

        ctx.Result.Should().BeNull("platform admin has no tenant_id claim and must bypass the guard");
    }

    // ── Cross-tenant flag isolation ───────────────────────────────────────────

    [Fact]
    public async Task FeatureFlag_TenantA_DoesNotAffectTenantB()
    {
        await using var db = CreateDb();
        var tenantA = Guid.NewGuid();
        var tenantB = Guid.NewGuid();

        // Disable AI for Tenant A only
        db.TenantFeatureFlags.Add(new TenantFeatureFlag
        {
            TenantId = tenantA, FeatureKey = FeatureKeys.AiAssistant, IsEnabled = false
        });
        // Tenant B has AI enabled
        db.TenantFeatureFlags.Add(new TenantFeatureFlag
        {
            TenantId = tenantB, FeatureKey = FeatureKeys.AiAssistant, IsEnabled = true
        });
        await db.SaveChangesAsync();

        // Tenant B request must pass through
        var httpCtx = new DefaultHttpContext();
        httpCtx.Request.Path = "/api/ai-assistant/ask";
        var claims = new[] { new Claim("tenant_id", tenantB.ToString()) };
        httpCtx.User = new ClaimsPrincipal(new ClaimsIdentity(claims, "Test"));

        var actionCtx = new ActionContext(httpCtx, new RouteData(), new ActionDescriptor());
        var ctx = new ActionExecutingContext(actionCtx, [], new Dictionary<string, object?>(), null!);

        var filter = new FeatureFlagGuardFilter(db, new MemoryCache(new MemoryCacheOptions()));
        await filter.OnActionExecutionAsync(ctx, () =>
            Task.FromResult(new ActionExecutedContext(actionCtx, [], null!)));

        ctx.Result.Should().BeNull("TenantB has AI enabled; TenantA's disabled flag must not affect it");
    }
}
