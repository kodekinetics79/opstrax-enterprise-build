using Microsoft.Extensions.DependencyInjection;
using Zayra.Api.Application.CountryPack;

namespace Zayra.Api.Infrastructure.CountryPack;

// Resolves the correct strategy implementation for a given country + jurisdiction.
// Lookup order: exact jurisdiction key (e.g. "ARE:UAE-DIFC") → country-wide key
// ("ARE") → non-keyed fallback (Default pack).
// Business logic calls the resolver — it never branches on country with if/switch.

public sealed class CountryPackResolver : ICountryPackResolver
{
    private readonly IServiceProvider _sp;
    public CountryPackResolver(IServiceProvider sp) => _sp = sp;

    public IStatutoryDeductionCalculator ResolveDeductionCalculator(string cc, string j)
        => Resolve<IStatutoryDeductionCalculator>(cc, j);

    public IEndOfServiceCalculator ResolveEndOfServiceCalculator(string cc, string j)
        => Resolve<IEndOfServiceCalculator>(cc, j);

    public IWageProtectionExporter ResolveWageProtectionExporter(string cc, string j)
        => Resolve<IWageProtectionExporter>(cc, j);

    public INationalizationTracker ResolveNationalizationTracker(string cc, string j)
        => Resolve<INationalizationTracker>(cc, j);

    public ILocalizationProfile ResolveLocalizationProfile(string cc, string j)
        => Resolve<ILocalizationProfile>(cc, j);

    private T Resolve<T>(string countryCode, string jurisdiction) where T : class
        => _sp.GetKeyedService<T>($"{countryCode}:{jurisdiction}")
        ?? _sp.GetKeyedService<T>(countryCode)
        ?? _sp.GetRequiredService<T>();  // non-keyed default pack
}

// Retained for test use: allows constructing a resolver with explicit strategy
// instances without needing a full DI container.
public sealed record KeyedService<T>(string Key, T Service);
