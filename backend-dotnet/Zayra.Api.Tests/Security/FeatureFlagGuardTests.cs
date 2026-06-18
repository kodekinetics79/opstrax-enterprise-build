using System.Security.Claims;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;
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
///   6. Cross-tenant flag isolation is enforced.
///   7. New routes: /api/saudi-compliance, /api/gosi, /api/wps are correctly gated.
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

    private static FeatureFlagGuardFilter MakeFilter(ZayraDbContext db)
        => new(db, new MemoryCache(new MemoryCacheOptions()), NullLogger<FeatureFlagGuardFilter>.Instance);

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

        var filter = MakeFilter(db);
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

    // ── Newly-mapped routes ───────────────────────────────────────────────────

    [Fact]
    public async Task SaudiCompliance_DisabledCompliance_Returns403()
    {
        await using var db = CreateDb();
        var tenantId = Guid.NewGuid();

        var result = await RunFilter(db, "/api/saudi-compliance/reports", tenantId, false, FeatureKeys.Compliance);

        result.Should().BeOfType<ObjectResult>()
            .Which.StatusCode.Should().Be(403);
    }

    [Fact]
    public async Task SaudiCompliance_EnabledCompliance_PassesThrough()
    {
        await using var db = CreateDb();
        var tenantId = Guid.NewGuid();

        var result = await RunFilter(db, "/api/saudi-compliance/reports", tenantId, true, FeatureKeys.Compliance);

        result.Should().BeNull();
    }

    [Fact]
    public async Task Gosi_DisabledQiwaIntegration_Returns403()
    {
        await using var db = CreateDb();
        var tenantId = Guid.NewGuid();

        var result = await RunFilter(db, "/api/gosi/contributions", tenantId, false, FeatureKeys.QiwaIntegration);

        result.Should().BeOfType<ObjectResult>()
            .Which.StatusCode.Should().Be(403);
    }

    [Fact]
    public async Task Gosi_EnabledQiwaIntegration_PassesThrough()
    {
        await using var db = CreateDb();
        var tenantId = Guid.NewGuid();

        var result = await RunFilter(db, "/api/gosi/contributions", tenantId, true, FeatureKeys.QiwaIntegration);

        result.Should().BeNull();
    }

    [Fact]
    public async Task Wps_DisabledWpsExport_Returns403()
    {
        await using var db = CreateDb();
        var tenantId = Guid.NewGuid();

        var result = await RunFilter(db, "/api/wps/export", tenantId, false, FeatureKeys.WpsExport);

        result.Should().BeOfType<ObjectResult>()
            .Which.StatusCode.Should().Be(403);
    }

    [Fact]
    public async Task Wps_EnabledWpsExport_PassesThrough()
    {
        await using var db = CreateDb();
        var tenantId = Guid.NewGuid();

        var result = await RunFilter(db, "/api/wps/export", tenantId, true, FeatureKeys.WpsExport);

        result.Should().BeNull();
    }

    // ── New feature key constants compile and are usable ─────────────────────

    [Theory]
    [InlineData(FeatureKeys.EosbCalc,            "eosb_calc")]
    [InlineData(FeatureKeys.ResumeScreening,     "resume_screening")]
    [InlineData(FeatureKeys.PayrollAiValidation, "payroll_ai_validation")]
    [InlineData(FeatureKeys.RiskScores,          "risk_scores")]
    [InlineData(FeatureKeys.HijriCalendar,       "hijri_calendar")]
    public void NewFeatureKeyConstants_HaveCorrectValues(string constant, string expected)
    {
        constant.Should().Be(expected);
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

        var filter = MakeFilter(db);
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

        var filter = MakeFilter(db);
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
        httpCtx.User = new ClaimsPrincipal(new ClaimsIdentity(new[]
        {
            new Claim("is_platform_admin", "true"),
        }, "Test"));

        var actionCtx = new ActionContext(httpCtx, new RouteData(), new ActionDescriptor());
        var ctx = new ActionExecutingContext(actionCtx, [], new Dictionary<string, object?>(), null!);

        var filter = MakeFilter(db);
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

        db.TenantFeatureFlags.Add(new TenantFeatureFlag
        {
            TenantId = tenantA, FeatureKey = FeatureKeys.AiAssistant, IsEnabled = false
        });
        db.TenantFeatureFlags.Add(new TenantFeatureFlag
        {
            TenantId = tenantB, FeatureKey = FeatureKeys.AiAssistant, IsEnabled = true
        });
        await db.SaveChangesAsync();

        var httpCtx = new DefaultHttpContext();
        httpCtx.Request.Path = "/api/ai-assistant/ask";
        var claims = new[] { new Claim("tenant_id", tenantB.ToString()) };
        httpCtx.User = new ClaimsPrincipal(new ClaimsIdentity(claims, "Test"));

        var actionCtx = new ActionContext(httpCtx, new RouteData(), new ActionDescriptor());
        var ctx = new ActionExecutingContext(actionCtx, [], new Dictionary<string, object?>(), null!);

        var filter = MakeFilter(db);
        await filter.OnActionExecutionAsync(ctx, () =>
            Task.FromResult(new ActionExecutedContext(actionCtx, [], null!)));

        ctx.Result.Should().BeNull("TenantB has AI enabled; TenantA's disabled flag must not affect it");
    }

    [Fact]
    public async Task CrossTenantBypass_NotPossible_TenantABlockedEvenIfBEnabled()
    {
        await using var db = CreateDb();
        var tenantA = Guid.NewGuid();
        var tenantB = Guid.NewGuid();

        db.TenantFeatureFlags.Add(new TenantFeatureFlag
        {
            TenantId = tenantA, FeatureKey = FeatureKeys.Payroll, IsEnabled = false
        });
        db.TenantFeatureFlags.Add(new TenantFeatureFlag
        {
            TenantId = tenantB, FeatureKey = FeatureKeys.Payroll, IsEnabled = true
        });
        await db.SaveChangesAsync();

        // TenantA request — must still be blocked even though TenantB has it enabled
        var httpCtx = new DefaultHttpContext();
        httpCtx.Request.Path = "/api/payroll/runs";
        var claims = new[] { new Claim("tenant_id", tenantA.ToString()) };
        httpCtx.User = new ClaimsPrincipal(new ClaimsIdentity(claims, "Test"));

        var actionCtx = new ActionContext(httpCtx, new RouteData(), new ActionDescriptor());
        var ctx = new ActionExecutingContext(actionCtx, [], new Dictionary<string, object?>(), null!);

        var filter = MakeFilter(db);
        await filter.OnActionExecutionAsync(ctx, () =>
            Task.FromResult(new ActionExecutedContext(actionCtx, [], null!)));

        ctx.Result.Should().BeOfType<ObjectResult>()
            .Which.StatusCode.Should().Be(403, "TenantA's payroll is disabled; TenantB's flag must not grant access to TenantA");
    }
}
