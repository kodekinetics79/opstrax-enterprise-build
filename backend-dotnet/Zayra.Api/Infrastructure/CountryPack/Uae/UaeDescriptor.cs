using Zayra.Api.Application.CountryPack;

namespace Zayra.Api.Infrastructure.CountryPack.Uae;

// UAE — covers mainland and ADGM; DIFC overrides only the EOS path (via jurisdiction key).
public sealed class UaeDescriptor : ICountryPackDescriptor
{
    private readonly string _jurisdiction;

    public UaeDescriptor(string jurisdiction = Jurisdictions.UAEMainland)
        => _jurisdiction = jurisdiction;

    public PackDescriptor GetDescriptor()
    {
        var (eosbFormula, wpsLabel) = _jurisdiction == Jurisdictions.Difc
            ? ("DIFC Employment Law 2 of 2019 — DEWS monthly employer contribution: 5.83% (yrs 1–5) / 8.33% (>5 yrs) of basic salary.",
               "DEWS (DIFC Employee Workplace Savings) — employer-managed fund, no SIF file required")
            : ("UAE Decree-Law 33/2021 Art. 51 — 21 days basic/yr (≤5 yrs), 30 days basic/yr (>5 yrs); daily rate = basic/30; cap = 24 months basic.",
               "MOHRE SIF (Ministry of Human Resources & Emiratisation — VERIFY specification before live integration)");

        return new PackDescriptor(
            CountryCode:              CountryCodes.UAE,
            CountryNameEn:            "United Arab Emirates",
            CountryNameAr:            "الإمارات العربية المتحدة",
            SocialInsuranceScheme:    "GPSSA",
            SocialInsuranceDescription:
                "General Pension & Social Security Authority — UAE nationals only: 5% employee + 12.5% employer on basic+housing. Expatriates: no statutory pension contribution.",
            EosbFormula:              eosbFormula,
            WpsFormat:                _jurisdiction == Jurisdictions.Difc ? "dews" : "mohre-sif",
            WpsFormatLabel:           wpsLabel,
            NationalizationScheme:    "Emiratisation + Nafis");
    }
}

// Dedicated DIFC descriptor registered under "ARE:UAE-DIFC" key.
public sealed class UaeDifcDescriptor : ICountryPackDescriptor
{
    public PackDescriptor GetDescriptor()
        => new UaeDescriptor(Jurisdictions.Difc).GetDescriptor();
}
