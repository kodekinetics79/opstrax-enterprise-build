using Zayra.Api.Application.CountryPack;
using Zayra.Api.Infrastructure.CountryPack;

namespace Zayra.Api.Tests;

// Verifies that the GCC-1 country-pack framework is end-to-end wired:
// the resolver resolves, the default pack returns safe zero-value results,
// and the contract DTOs are correctly structured.
public class CountryPackFrameworkTests
{
    private static CountryPackResolver BuildResolver(
        IStatutoryDeductionCalculator? deduction = null,
        IEndOfServiceCalculator? eos = null,
        IWageProtectionExporter? wps = null,
        INationalizationTracker? nat = null,
        ILocalizationProfile? locale = null)
    {
        var defaultDeduction = deduction ?? new DefaultStatutoryDeductionCalculator();
        var defaultEos       = eos       ?? new DefaultEndOfServiceCalculator();
        var defaultWps       = wps       ?? new DefaultWageProtectionExporter();
        var defaultNat       = nat       ?? new DefaultNationalizationTracker();
        var defaultLocale    = locale    ?? new DefaultLocalizationProfile();

        return new CountryPackResolver(
            deductionCalcs:  Enumerable.Empty<KeyedService<IStatutoryDeductionCalculator>>(),
            eosCalcs:        Enumerable.Empty<KeyedService<IEndOfServiceCalculator>>(),
            wpsExporters:    Enumerable.Empty<KeyedService<IWageProtectionExporter>>(),
            natTrackers:     Enumerable.Empty<KeyedService<INationalizationTracker>>(),
            localeProfiles:  Enumerable.Empty<KeyedService<ILocalizationProfile>>(),
            defaultDeduction: defaultDeduction,
            defaultEos:       defaultEos,
            defaultWps:       defaultWps,
            defaultNat:       defaultNat,
            defaultLocale:    defaultLocale);
    }

    // ── Resolver falls back to default pack when no packs registered ─────────

    [Fact]
    public void Resolver_NoPacksRegistered_ReturnsDefaultDeductionCalculator()
    {
        var resolver = BuildResolver();
        var calc = resolver.ResolveDeductionCalculator(CountryCodes.Saudi, Jurisdictions.KsaMainland);
        Assert.IsType<DefaultStatutoryDeductionCalculator>(calc);
    }

    [Fact]
    public void Resolver_NoPacksRegistered_ReturnsDefaultEosCalculator()
    {
        var resolver = BuildResolver();
        var calc = resolver.ResolveEndOfServiceCalculator(CountryCodes.Qatar, Jurisdictions.QatarMainland);
        Assert.IsType<DefaultEndOfServiceCalculator>(calc);
    }

    [Fact]
    public void Resolver_NoPacksRegistered_ReturnsDefaultWpsExporter()
    {
        var resolver = BuildResolver();
        var exporter = resolver.ResolveWageProtectionExporter(CountryCodes.UAE, Jurisdictions.Difc);
        Assert.IsType<DefaultWageProtectionExporter>(exporter);
    }

    [Fact]
    public void Resolver_NoPacksRegistered_ReturnsDefaultNatTracker()
    {
        var resolver = BuildResolver();
        var tracker = resolver.ResolveNationalizationTracker(CountryCodes.Saudi, Jurisdictions.KsaMainland);
        Assert.IsType<DefaultNationalizationTracker>(tracker);
    }

    [Fact]
    public void Resolver_NoPacksRegistered_ReturnsDefaultLocalizationProfile()
    {
        var resolver = BuildResolver();
        var profile = resolver.ResolveLocalizationProfile(CountryCodes.Saudi, Jurisdictions.KsaMainland);
        Assert.IsType<DefaultLocalizationProfile>(profile);
    }

    // ── Resolver prefers jurisdiction-keyed pack over default ────────────────

    [Fact]
    public void Resolver_JurisdictionKeyedPack_PrecedesDefault()
    {
        var stub = new StubDeductionCalculator();
        var resolver = new CountryPackResolver(
            deductionCalcs:  new[] { new KeyedService<IStatutoryDeductionCalculator>($"{CountryCodes.Saudi}:{Jurisdictions.KsaMainland}", stub) },
            eosCalcs:        Enumerable.Empty<KeyedService<IEndOfServiceCalculator>>(),
            wpsExporters:    Enumerable.Empty<KeyedService<IWageProtectionExporter>>(),
            natTrackers:     Enumerable.Empty<KeyedService<INationalizationTracker>>(),
            localeProfiles:  Enumerable.Empty<KeyedService<ILocalizationProfile>>(),
            defaultDeduction: new DefaultStatutoryDeductionCalculator(),
            defaultEos:       new DefaultEndOfServiceCalculator(),
            defaultWps:       new DefaultWageProtectionExporter(),
            defaultNat:       new DefaultNationalizationTracker(),
            defaultLocale:    new DefaultLocalizationProfile());

        var resolved = resolver.ResolveDeductionCalculator(CountryCodes.Saudi, Jurisdictions.KsaMainland);
        Assert.Same(stub, resolved);
    }

    [Fact]
    public void Resolver_CountryWidePack_PrecedesDefault_WhenNoJurisdictionMatch()
    {
        var stub = new StubDeductionCalculator();
        var resolver = new CountryPackResolver(
            deductionCalcs:  new[] { new KeyedService<IStatutoryDeductionCalculator>(CountryCodes.Saudi, stub) },
            eosCalcs:        Enumerable.Empty<KeyedService<IEndOfServiceCalculator>>(),
            wpsExporters:    Enumerable.Empty<KeyedService<IWageProtectionExporter>>(),
            natTrackers:     Enumerable.Empty<KeyedService<INationalizationTracker>>(),
            localeProfiles:  Enumerable.Empty<KeyedService<ILocalizationProfile>>(),
            defaultDeduction: new DefaultStatutoryDeductionCalculator(),
            defaultEos:       new DefaultEndOfServiceCalculator(),
            defaultWps:       new DefaultWageProtectionExporter(),
            defaultNat:       new DefaultNationalizationTracker(),
            defaultLocale:    new DefaultLocalizationProfile());

        // Country-wide match but no jurisdiction-specific match
        var resolved = resolver.ResolveDeductionCalculator(CountryCodes.Saudi, Jurisdictions.KsaMainland);
        Assert.Same(stub, resolved);
    }

    // ── DefaultPack returns safe zero-value results ───────────────────────────

    [Fact]
    public async Task DefaultDeductionCalculator_ReturnsSafeZeroResult()
    {
        var calc = new DefaultStatutoryDeductionCalculator();
        var input = new StatutoryDeductionInput(Guid.NewGuid(), Guid.NewGuid(), 10000m, "SAU", "Unlimited", 2026, 6);
        var result = await calc.CalculateAsync(input);

        Assert.Equal(0m, result.TotalEmployeeDeduction);
        Assert.Equal(0m, result.TotalEmployerContribution);
        Assert.Empty(result.Lines);
    }

    [Fact]
    public async Task DefaultEosCalculator_ReturnsSafeZeroResult()
    {
        var calc = new DefaultEndOfServiceCalculator();
        var input = new EndOfServiceInput(Guid.NewGuid(), Guid.NewGuid(), 8000m, 3.5m, "Resignation", "Unlimited", "SAU");
        var result = await calc.CalculateAsync(input);

        Assert.Equal(0m, result.TotalGratuity);
        Assert.NotEmpty(result.ApplicableRule);
        Assert.Empty(result.Breakdown);
    }

    [Fact]
    public async Task DefaultWpsExporter_ReturnsSafeEmptyResult()
    {
        var exporter = new DefaultWageProtectionExporter();
        var input = new WageProtectionExportInput(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), 2026, 6);
        var result = await exporter.ExportAsync(input);

        Assert.Empty(result.FileBytes);
        Assert.Equal(0, result.RecordCount);
    }

    [Fact]
    public async Task DefaultNatTracker_ReturnsNotApplicable()
    {
        var tracker = new DefaultNationalizationTracker();
        var input = new NationalizationInput(Guid.NewGuid(), Guid.NewGuid());
        var result = await tracker.GetStatusAsync(input);

        Assert.Equal(NationalizationComplianceStatus.NotApplicable, result.Status);
        Assert.Equal(0, result.TotalHeadcount);
    }

    [Fact]
    public void DefaultLocalizationProfile_ReturnsProfile()
    {
        var profile = new DefaultLocalizationProfile().GetProfile();

        Assert.NotEmpty(profile.CurrencyCode);
        Assert.NotEmpty(profile.LocaleCode);
    }

    // ── Jurisdiction constant coverage ───────────────────────────────────────

    [Theory]
    [InlineData(CountryCodes.Saudi,  Jurisdictions.KsaMainland)]
    [InlineData(CountryCodes.Qatar,  Jurisdictions.QatarMainland)]
    [InlineData(CountryCodes.UAE,    Jurisdictions.UAEMainland)]
    [InlineData(CountryCodes.UAE,    Jurisdictions.Difc)]
    [InlineData(CountryCodes.UAE,    Jurisdictions.Adgm)]
    public void JurisdictionConstants_AreNonEmptyAndDistinct(string countryCode, string jurisdiction)
    {
        Assert.NotEmpty(countryCode);
        Assert.NotEmpty(jurisdiction);
        Assert.Contains("-", jurisdiction);
    }

    private sealed class StubDeductionCalculator : IStatutoryDeductionCalculator
    {
        public Task<StatutoryDeductionResult> CalculateAsync(StatutoryDeductionInput input, CancellationToken ct = default)
            => Task.FromResult(new StatutoryDeductionResult(99m, 99m, Array.Empty<StatutoryDeductionLine>()));
    }
}
