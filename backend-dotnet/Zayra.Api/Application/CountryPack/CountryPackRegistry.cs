namespace Zayra.Api.Application.CountryPack;

// Registry of country packs available in this deployment.
// Adding a new pack = register strategy services in Program.cs + add an entry here.
// The wizard dropdown reads from GET /api/country-packs/available.
public static class CountryPackRegistry
{
    public static readonly IReadOnlyList<AvailableCountryPack> Available =
    [
        new AvailableCountryPack(CountryCodes.Saudi, "Saudi Arabia", "المملكة العربية السعودية",
        [
            new AvailableJurisdiction(Jurisdictions.KsaMainland, "KSA — Mainland"),
        ]),
        new AvailableCountryPack(CountryCodes.UAE, "United Arab Emirates", "الإمارات العربية المتحدة",
        [
            new AvailableJurisdiction(Jurisdictions.UAEMainland, "UAE — Mainland"),
            new AvailableJurisdiction(Jurisdictions.Difc,        "UAE — DIFC (Dubai International Financial Centre)"),
            new AvailableJurisdiction(Jurisdictions.Adgm,        "UAE — ADGM (Abu Dhabi Global Market)"),
        ]),
        new AvailableCountryPack(CountryCodes.Qatar, "Qatar", "قطر",
        [
            new AvailableJurisdiction(Jurisdictions.QatarMainland, "Qatar — Mainland"),
        ]),
    ];
}
