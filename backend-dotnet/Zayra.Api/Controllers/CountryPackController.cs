using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Zayra.Api.Application.Common;
using Zayra.Api.Application.CountryPack;
using Zayra.Api.Data;

namespace Zayra.Api.Controllers;

// ── DTOs ─────────────────────────────────────────────────────────────────────

public sealed record JurisdictionOptionDto(string Code, string Label);

public sealed record CountryPackOptionDto(
    string CountryCode,
    string NameEn,
    string NameAr,
    IReadOnlyList<JurisdictionOptionDto> Jurisdictions);

public sealed record StatutorySummaryDto(
    string CountryCode,
    string Jurisdiction,
    string CountryNameEn,
    string CountryNameAr,
    string SocialInsuranceScheme,
    string SocialInsuranceDescription,
    string EosbFormula,
    string WpsFormat,
    string WpsFormatLabel,
    string NationalizationScheme,
    string CurrencyCode,
    string CurrencySymbol,
    string LocaleCode,
    bool IsRtl,
    string CalendarSystem);

// ── Controller ────────────────────────────────────────────────────────────────

[ApiController]
[Route("api/country-packs")]
[Authorize(Roles = "Admin,HR Manager,HR Officer,Auditor")]
public class CountryPackController : ControllerBase
{
    private readonly ZayraDbContext _db;
    private readonly ICountryPackResolver _resolver;

    public CountryPackController(ZayraDbContext db, ICountryPackResolver resolver)
    {
        _db = db;
        _resolver = resolver;
    }

    /// <summary>Returns the list of country packs available in this deployment.</summary>
    /// <remarks>Used by the setup wizard country+jurisdiction selector.
    /// Returns from static registry — no DB query.</remarks>
    [HttpGet("available")]
    public IActionResult GetAvailable()
    {
        var result = CountryPackRegistry.Available.Select(p => new CountryPackOptionDto(
            p.CountryCode, p.NameEn, p.NameAr,
            p.Jurisdictions.Select(j => new JurisdictionOptionDto(j.Code, j.Label)).ToList()))
            .ToList();
        return Ok(result);
    }

    /// <summary>Returns the resolved statutory summary for a company.</summary>
    /// <remarks>
    /// Pulls descriptor + localization profile from the pack via the resolver.
    /// Never branches on country code — all behaviour comes from the pack.
    /// IDOR: company must belong to the caller's tenant.
    /// </remarks>
    [HttpGet("company/{companyId:guid}/statutory-summary")]
    [Authorize(Roles = "Admin,HR Manager")]
    public async Task<ActionResult<StatutorySummaryDto>> GetStatutorySummary(
        Guid companyId, CancellationToken ct)
    {
        var tenantId = this.GetTenantId();
        if (tenantId is null) return Unauthorized();

        var company = await _db.Companies
            .AsNoTracking()
            .FirstOrDefaultAsync(c => c.TenantId == tenantId && c.Id == companyId && !c.IsDeleted, ct);

        if (company is null) return NotFound();

        var cc = company.CountryCode;
        var jurisdiction = company.Jurisdiction;

        var descriptor = _resolver.ResolveDescriptor(cc, jurisdiction);
        var locale     = _resolver.ResolveLocalizationProfile(cc, jurisdiction);

        var desc    = descriptor.GetDescriptor();
        var profile = locale.GetProfile();

        return Ok(new StatutorySummaryDto(
            CountryCode:              cc,
            Jurisdiction:             jurisdiction,
            CountryNameEn:            desc.CountryNameEn,
            CountryNameAr:            desc.CountryNameAr,
            SocialInsuranceScheme:    desc.SocialInsuranceScheme,
            SocialInsuranceDescription: desc.SocialInsuranceDescription,
            EosbFormula:              desc.EosbFormula,
            WpsFormat:                desc.WpsFormat,
            WpsFormatLabel:           desc.WpsFormatLabel,
            NationalizationScheme:    desc.NationalizationScheme,
            CurrencyCode:             profile.CurrencyCode,
            CurrencySymbol:           profile.CurrencySymbol,
            LocaleCode:               profile.LocaleCode,
            IsRtl:                    profile.IsRtl,
            CalendarSystem:           profile.CalendarSystem));
    }
}
