using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Zayra.Api.Application.Common;
using Zayra.Api.Data;
using Zayra.Api.Models;

namespace Zayra.Api.Controllers;

[ApiController]
[Route("api/tenant-admin")]
[Authorize(Roles = "Admin")]
public class TenantAdminController : ControllerBase
{
    private readonly ZayraDbContext _db;

    public TenantAdminController(ZayraDbContext db) => _db = db;

    // ── Subscription ────────────────────────────────────────────────────────

    [HttpGet("subscription")]
    public async Task<IActionResult> GetSubscription(CancellationToken ct)
    {
        var tenantId = this.GetTenantId();
        if (tenantId is null) return Unauthorized();

        var sub = await _db.TenantSubscriptions
            .FirstOrDefaultAsync(s => s.TenantId == tenantId, ct);

        return Ok(sub);
    }

    [HttpPut("subscription")]
    public async Task<IActionResult> UpsertSubscription([FromBody] UpsertSubscriptionRequest req, CancellationToken ct)
    {
        var tenantId = this.GetTenantId();
        if (tenantId is null) return Unauthorized();

        var sub = await _db.TenantSubscriptions
            .FirstOrDefaultAsync(s => s.TenantId == tenantId, ct);

        if (sub is null)
        {
            sub = new TenantSubscription { TenantId = tenantId.Value };
            _db.TenantSubscriptions.Add(sub);
        }

        sub.Plan = req.Plan;
        sub.Status = req.Status;
        sub.MaxEmployees = req.MaxEmployees;
        sub.BillingEmail = req.BillingEmail;
        sub.BillingCycle = req.BillingCycle;
        sub.MonthlyAmount = req.MonthlyAmount;
        sub.CurrencyCode = req.CurrencyCode;
        sub.ExpiresAtUtc = req.ExpiresAtUtc;

        await _db.SaveChangesAsync(ct);
        return Ok(sub);
    }

    // ── Feature Flags ───────────────────────────────────────────────────────

    [HttpGet("feature-flags")]
    public async Task<IActionResult> ListFeatureFlags(CancellationToken ct)
    {
        var tenantId = this.GetTenantId();
        if (tenantId is null) return Unauthorized();

        var flags = await _db.TenantFeatureFlags
            .Where(f => f.TenantId == tenantId)
            .OrderBy(f => f.FeatureKey)
            .ToListAsync(ct);

        return Ok(flags);
    }

    [HttpPut("feature-flags/{featureKey}")]
    public async Task<IActionResult> SetFeatureFlag(string featureKey, [FromBody] SetFeatureFlagRequest req, CancellationToken ct)
    {
        var tenantId = this.GetTenantId();
        if (tenantId is null) return Unauthorized();

        var flag = await _db.TenantFeatureFlags
            .FirstOrDefaultAsync(f => f.TenantId == tenantId && f.FeatureKey == featureKey, ct);

        if (flag is null)
        {
            flag = new TenantFeatureFlag { TenantId = tenantId.Value, FeatureKey = featureKey };
            _db.TenantFeatureFlags.Add(flag);
        }

        flag.IsEnabled = req.IsEnabled;
        flag.ConfigJson = req.ConfigJson;
        flag.UpdatedAtUtc = DateTime.UtcNow;
        flag.UpdatedBy = this.GetUserId();

        await _db.SaveChangesAsync(ct);
        return Ok(flag);
    }

    // ── Localization ─────────────────────────────────────────────────────────

    [HttpGet("localization")]
    [AllowAnonymous] // needed for initial load before auth
    public async Task<IActionResult> GetLocalization([FromQuery] string? slug, CancellationToken ct)
    {
        Guid? tenantId = this.GetTenantId();

        if (tenantId is null && !string.IsNullOrWhiteSpace(slug))
        {
            var tenant = await _db.Tenants.FirstOrDefaultAsync(t => t.Slug == slug, ct);
            tenantId = tenant?.Id;
        }

        if (tenantId is null) return Ok(new TenantLocalizationSetting()); // defaults

        var loc = await _db.TenantLocalizationSettings
            .FirstOrDefaultAsync(l => l.TenantId == tenantId, ct);

        return Ok(loc ?? new TenantLocalizationSetting { TenantId = tenantId.Value });
    }

    [HttpPut("localization")]
    public async Task<IActionResult> UpsertLocalization([FromBody] UpsertLocalizationRequest req, CancellationToken ct)
    {
        var tenantId = this.GetTenantId();
        if (tenantId is null) return Unauthorized();

        var loc = await _db.TenantLocalizationSettings
            .FirstOrDefaultAsync(l => l.TenantId == tenantId, ct);

        if (loc is null)
        {
            loc = new TenantLocalizationSetting { TenantId = tenantId.Value };
            _db.TenantLocalizationSettings.Add(loc);
        }

        loc.DefaultLanguage = req.DefaultLanguage;
        loc.RtlEnabled = req.RtlEnabled;
        loc.CalendarSystem = req.CalendarSystem;
        loc.DefaultTimezone = req.DefaultTimezone;
        loc.DateFormat = req.DateFormat;
        loc.CurrencyCode = req.CurrencyCode;
        loc.CountryCode = req.CountryCode;
        loc.WeekStartDay = req.WeekStartDay;
        loc.WorkWeek = req.WorkWeek;
        loc.HijriDatesEnabled = req.HijriDatesEnabled;
        loc.UpdatedAtUtc = DateTime.UtcNow;

        await _db.SaveChangesAsync(ct);
        return Ok(loc);
    }

    // ── Branding ─────────────────────────────────────────────────────────────

    [HttpGet("branding")]
    public async Task<IActionResult> GetBranding(CancellationToken ct)
    {
        var tenantId = this.GetTenantId();
        if (tenantId is null) return Unauthorized();

        var branding = await _db.TenantBrandings
            .FirstOrDefaultAsync(b => b.TenantId == tenantId, ct);

        return Ok(branding);
    }

    [HttpPut("branding")]
    public async Task<IActionResult> UpsertBranding([FromBody] UpsertBrandingRequest req, CancellationToken ct)
    {
        var tenantId = this.GetTenantId();
        if (tenantId is null) return Unauthorized();

        var branding = await _db.TenantBrandings
            .FirstOrDefaultAsync(b => b.TenantId == tenantId, ct);

        if (branding is null)
        {
            branding = new TenantBranding { TenantId = tenantId.Value };
            _db.TenantBrandings.Add(branding);
        }

        branding.LogoUrl = req.LogoUrl ?? branding.LogoUrl;
        branding.PrimaryColor = req.PrimaryColor ?? branding.PrimaryColor;
        branding.AccentColor = req.AccentColor ?? branding.AccentColor;
        branding.CompanyNameEn = req.CompanyNameEn ?? branding.CompanyNameEn;
        branding.CompanyNameAr = req.CompanyNameAr ?? branding.CompanyNameAr;
        branding.PortalTitle = req.PortalTitle ?? branding.PortalTitle;
        branding.FaviconUrl = req.FaviconUrl ?? branding.FaviconUrl;
        branding.UpdatedAtUtc = DateTime.UtcNow;

        await _db.SaveChangesAsync(ct);
        return Ok(branding);
    }

    // ── Country Payroll Rules ────────────────────────────────────────────────

    [HttpGet("country-rules")]
    public async Task<IActionResult> ListCountryRules([FromQuery] string? countryCode, CancellationToken ct)
    {
        var tenantId = this.GetTenantId();
        if (tenantId is null) return Unauthorized();

        var query = _db.CountryPayrollRules.Where(r => r.TenantId == tenantId);
        if (!string.IsNullOrWhiteSpace(countryCode))
            query = query.Where(r => r.CountryCode == countryCode.ToUpperInvariant());

        var items = await query
            .OrderBy(r => r.CountryCode)
            .ThenBy(r => r.RuleKey)
            .ToListAsync(ct);

        return Ok(items);
    }

    [HttpPost("country-rules")]
    public async Task<IActionResult> CreateCountryRule([FromBody] CreateCountryRuleRequest req, CancellationToken ct)
    {
        var tenantId = this.GetTenantId();
        if (tenantId is null) return Unauthorized();

        var rule = new CountryPayrollRule
        {
            TenantId = tenantId.Value,
            CountryCode = req.CountryCode.ToUpperInvariant(),
            RuleKey = req.RuleKey,
            RuleValue = req.RuleValue,
            DataType = req.DataType ?? "string",
            Description = req.Description ?? string.Empty,
            IsOverride = req.IsOverride ?? false,
            EffectiveFrom = req.EffectiveFrom ?? DateTime.UtcNow,
            EffectiveTo = req.EffectiveTo,
            CreatedBy = this.GetUserId()
        };

        _db.CountryPayrollRules.Add(rule);
        await _db.SaveChangesAsync(ct);
        return Created($"/api/tenant-admin/country-rules/{rule.Id}", rule);
    }
}

public record UpsertSubscriptionRequest(
    string Plan, string Status, int MaxEmployees, string BillingEmail,
    string BillingCycle, decimal MonthlyAmount, string CurrencyCode, DateTime? ExpiresAtUtc);

public record SetFeatureFlagRequest(bool IsEnabled, string? ConfigJson);

public record UpsertLocalizationRequest(
    string DefaultLanguage, bool RtlEnabled, string CalendarSystem,
    string DefaultTimezone, string DateFormat, string CurrencyCode,
    string CountryCode, string WeekStartDay, string WorkWeek, bool HijriDatesEnabled);

public record UpsertBrandingRequest(
    string? LogoUrl, string? PrimaryColor, string? AccentColor,
    string? CompanyNameEn, string? CompanyNameAr, string? PortalTitle, string? FaviconUrl);

public record CreateCountryRuleRequest(
    string CountryCode, string RuleKey, string RuleValue,
    string? DataType, string? Description, bool? IsOverride,
    DateTime? EffectiveFrom, DateTime? EffectiveTo);
