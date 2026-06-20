using Zayra.Api.Application.CountryPack;

namespace Zayra.Api.Infrastructure.CountryPack.Qatar;

public sealed class QatarLocalizationProfile : ILocalizationProfile
{
    public LocalizationProfile GetProfile()
        => new("QAR", "ر.ق", "ar-QA", IsRtl: true, "dd/MM/yyyy", "Gregorian");
}
