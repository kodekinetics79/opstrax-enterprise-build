using Zayra.Api.Application.CountryPack;

namespace Zayra.Api.Infrastructure.CountryPack.Qatar;

public sealed class QatarDescriptor : ICountryPackDescriptor
{
    public PackDescriptor GetDescriptor() => new(
        CountryCode:              CountryCodes.Qatar,
        CountryNameEn:            "Qatar",
        CountryNameAr:            "قطر",
        SocialInsuranceScheme:    "GRSIA",
        SocialInsuranceDescription:
            "General Retirement & Social Insurance Authority — Qatari nationals only: 7% employee + 14% employer on basic salary. Expatriates: exempt.",
        EosbFormula:
            "Qatar Labor Law 14 of 2004, Art. 54 — minimum 3 weeks (21 days) basic/yr; daily rate = basic/30; pro-rated by days of service.",
        WpsFormat:     "qcb-sif",
        WpsFormatLabel: "QCB SIF (Qatar Central Bank Salary Information File — VERIFY specification before live integration)",
        NationalizationScheme: "Qatarization");
}
