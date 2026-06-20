using Zayra.Api.Application.CountryPack;

namespace Zayra.Api.Infrastructure.CountryPack.Uae;

public sealed class UaeLocalizationProfile : ILocalizationProfile
{
    public LocalizationProfile GetProfile()
        => new("AED", "د.إ", "ar-AE", IsRtl: true, "dd/MM/yyyy", "Gregorian");
}
