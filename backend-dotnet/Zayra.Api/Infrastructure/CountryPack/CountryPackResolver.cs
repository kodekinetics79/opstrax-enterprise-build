using Zayra.Api.Application.CountryPack;

namespace Zayra.Api.Infrastructure.CountryPack;

// Resolves the correct strategy implementation for a given country + jurisdiction.
// Business logic must call this resolver — never branch on country code directly
// with if/switch statements.

public sealed class CountryPackResolver : ICountryPackResolver
{
    private readonly IReadOnlyDictionary<string, IStatutoryDeductionCalculator> _deductionCalcs;
    private readonly IReadOnlyDictionary<string, IEndOfServiceCalculator> _eosCalcs;
    private readonly IReadOnlyDictionary<string, IWageProtectionExporter> _wpsExporters;
    private readonly IReadOnlyDictionary<string, INationalizationTracker> _natTrackers;
    private readonly IReadOnlyDictionary<string, ILocalizationProfile> _localeProfiles;

    private readonly IStatutoryDeductionCalculator _defaultDeduction;
    private readonly IEndOfServiceCalculator _defaultEos;
    private readonly IWageProtectionExporter _defaultWps;
    private readonly INationalizationTracker _defaultNat;
    private readonly ILocalizationProfile _defaultLocale;

    public CountryPackResolver(
        IEnumerable<KeyedService<IStatutoryDeductionCalculator>> deductionCalcs,
        IEnumerable<KeyedService<IEndOfServiceCalculator>> eosCalcs,
        IEnumerable<KeyedService<IWageProtectionExporter>> wpsExporters,
        IEnumerable<KeyedService<INationalizationTracker>> natTrackers,
        IEnumerable<KeyedService<ILocalizationProfile>> localeProfiles,
        IStatutoryDeductionCalculator defaultDeduction,
        IEndOfServiceCalculator defaultEos,
        IWageProtectionExporter defaultWps,
        INationalizationTracker defaultNat,
        ILocalizationProfile defaultLocale)
    {
        _deductionCalcs  = deductionCalcs.ToDictionary(k => k.Key, k => k.Service);
        _eosCalcs        = eosCalcs.ToDictionary(k => k.Key, k => k.Service);
        _wpsExporters    = wpsExporters.ToDictionary(k => k.Key, k => k.Service);
        _natTrackers     = natTrackers.ToDictionary(k => k.Key, k => k.Service);
        _localeProfiles  = localeProfiles.ToDictionary(k => k.Key, k => k.Service);

        _defaultDeduction = defaultDeduction;
        _defaultEos       = defaultEos;
        _defaultWps       = defaultWps;
        _defaultNat       = defaultNat;
        _defaultLocale    = defaultLocale;
    }

    public IStatutoryDeductionCalculator ResolveDeductionCalculator(string countryCode, string jurisdiction)
        => Lookup(_deductionCalcs, countryCode, jurisdiction) ?? _defaultDeduction;

    public IEndOfServiceCalculator ResolveEndOfServiceCalculator(string countryCode, string jurisdiction)
        => Lookup(_eosCalcs, countryCode, jurisdiction) ?? _defaultEos;

    public IWageProtectionExporter ResolveWageProtectionExporter(string countryCode, string jurisdiction)
        => Lookup(_wpsExporters, countryCode, jurisdiction) ?? _defaultWps;

    public INationalizationTracker ResolveNationalizationTracker(string countryCode, string jurisdiction)
        => Lookup(_natTrackers, countryCode, jurisdiction) ?? _defaultNat;

    public ILocalizationProfile ResolveLocalizationProfile(string countryCode, string jurisdiction)
        => Lookup(_localeProfiles, countryCode, jurisdiction) ?? _defaultLocale;

    // Lookup order: exact jurisdiction key → country-wildcard key → null (caller uses default)
    private static T? Lookup<T>(IReadOnlyDictionary<string, T> registry, string countryCode, string jurisdiction)
        where T : class
    {
        var jurisdictionKey = $"{countryCode}:{jurisdiction}";
        if (registry.TryGetValue(jurisdictionKey, out var exact)) return exact;
        if (registry.TryGetValue(countryCode, out var countryWide)) return countryWide;
        return null;
    }
}

// Carrier that pairs a registry key with a strategy implementation.
// Country packs register their strategies as KeyedService<T> so the resolver
// can build its lookup dictionaries without a hard dependency on each pack.
public sealed record KeyedService<T>(string Key, T Service);
