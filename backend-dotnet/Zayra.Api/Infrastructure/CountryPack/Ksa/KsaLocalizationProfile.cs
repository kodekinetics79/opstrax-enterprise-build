using Zayra.Api.Application.CountryPack;

namespace Zayra.Api.Infrastructure.CountryPack.Ksa;

public sealed class KsaLocalizationProfile : ILocalizationProfile
{
    public LocalizationProfile GetProfile()
        => new("SAR", "﷼", "ar-SA", IsRtl: true, "dd/MM/yyyy", "UmAlQura");
}
