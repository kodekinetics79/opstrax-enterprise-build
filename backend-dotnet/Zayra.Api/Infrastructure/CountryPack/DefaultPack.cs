using Zayra.Api.Application.CountryPack;

namespace Zayra.Api.Infrastructure.CountryPack;

// Safe no-op implementations — returned by the resolver when no country-specific
// pack matches.  Each real pack replaces exactly one of these per (country, jurisdiction).

public sealed class DefaultStatutoryDeductionCalculator : IStatutoryDeductionCalculator
{
    public Task<StatutoryDeductionResult> CalculateAsync(StatutoryDeductionInput input, CancellationToken ct = default)
        => Task.FromResult(new StatutoryDeductionResult(0m, 0m, Array.Empty<StatutoryDeductionLine>()));
}

public sealed class DefaultEndOfServiceCalculator : IEndOfServiceCalculator
{
    public Task<EndOfServiceResult> CalculateAsync(EndOfServiceInput input, CancellationToken ct = default)
        => Task.FromResult(new EndOfServiceResult(0m, "default-no-op", Array.Empty<EndOfServiceBreakdown>()));
}

public sealed class DefaultWageProtectionExporter : IWageProtectionExporter
{
    public Task<WageProtectionExportResult> ExportAsync(WageProtectionExportInput input, CancellationToken ct = default)
        => Task.FromResult(new WageProtectionExportResult(Array.Empty<byte>(), string.Empty, "none", 0));
}

public sealed class DefaultNationalizationTracker : INationalizationTracker
{
    public Task<NationalizationResult> GetStatusAsync(NationalizationInput input, CancellationToken ct = default)
        => Task.FromResult(new NationalizationResult(0d, 0d, input.TotalHeadcount, input.NationalHeadcount,
            NationalizationComplianceStatus.NotApplicable, "default"));
}

public sealed class DefaultLocalizationProfile : ILocalizationProfile
{
    public LocalizationProfile GetProfile()
        => new("USD", "$", "en", false, "yyyy-MM-dd", "Gregorian");
}

public sealed class DefaultCountryPackDescriptor : ICountryPackDescriptor
{
    public PackDescriptor GetDescriptor() => new(
        CountryCode:              "N/A",
        CountryNameEn:            "Not configured",
        CountryNameAr:            string.Empty,
        SocialInsuranceScheme:    "None",
        SocialInsuranceDescription: "No statutory deduction pack is configured for this company.",
        EosbFormula:              "No end-of-service pack configured.",
        WpsFormat:                "none",
        WpsFormatLabel:           "No WPS format configured.",
        NationalizationScheme:    "None");
}
