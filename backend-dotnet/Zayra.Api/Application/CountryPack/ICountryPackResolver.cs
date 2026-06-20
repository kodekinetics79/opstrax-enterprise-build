namespace Zayra.Api.Application.CountryPack;

public interface ICountryPackResolver
{
    IStatutoryDeductionCalculator ResolveDeductionCalculator(string countryCode, string jurisdiction);
    IEndOfServiceCalculator ResolveEndOfServiceCalculator(string countryCode, string jurisdiction);
    IWageProtectionExporter ResolveWageProtectionExporter(string countryCode, string jurisdiction);
    INationalizationTracker ResolveNationalizationTracker(string countryCode, string jurisdiction);
    ILocalizationProfile ResolveLocalizationProfile(string countryCode, string jurisdiction);
    ICountryPackDescriptor ResolveDescriptor(string countryCode, string jurisdiction);
}
