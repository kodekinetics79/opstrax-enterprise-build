using Zayra.Api.Application.CountryPack;

namespace Zayra.Api.Infrastructure.CountryPack;

// ── Default (no-op) implementations ──────────────────────────────────────────
// Used as the fallback pack when no country-specific pack is registered.
// Returns safe zero-value results so the framework is provably end-to-end
// before real packs exist. Each real pack replaces exactly one of these.

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
        => Task.FromResult(new NationalizationResult(0d, 0d, 0, 0, NationalizationComplianceStatus.NotApplicable));
}

public sealed class DefaultLocalizationProfile : ILocalizationProfile
{
    public LocalizationProfile GetProfile()
        => new("USD", "$", "en", false, "yyyy-MM-dd", "Gregorian");
}
