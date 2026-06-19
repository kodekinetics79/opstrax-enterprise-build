namespace Zayra.Api.Application.CountryPack;

// ── Jurisdiction constants ────────────────────────────────────────────────────

public static class CountryCodes
{
    public const string Saudi  = "SAU";
    public const string Qatar  = "QAT";
    public const string UAE    = "ARE";
}

public static class Jurisdictions
{
    public const string KsaMainland   = "KSA-mainland";
    public const string QatarMainland = "QAT-mainland";
    public const string UAEMainland   = "UAE-mainland";
    public const string Difc          = "UAE-DIFC";
    public const string Adgm          = "UAE-ADGM";
}

// ── Statutory deduction ───────────────────────────────────────────────────────

public sealed record StatutoryDeductionInput(
    Guid EmployeeId,
    Guid CompanyId,
    decimal GrossSalary,
    string Nationality,
    string ContractType,
    int PeriodYear,
    int PeriodMonth);

public sealed record StatutoryDeductionLine(string Code, string Label, decimal EmployeeAmount, decimal EmployerAmount);

public sealed record StatutoryDeductionResult(
    decimal TotalEmployeeDeduction,
    decimal TotalEmployerContribution,
    IReadOnlyList<StatutoryDeductionLine> Lines);

// ── End of service ───────────────────────────────────────────────────────────

public sealed record EndOfServiceInput(
    Guid EmployeeId,
    Guid CompanyId,
    decimal BasicSalary,
    decimal YearsOfService,
    string TerminationReason,
    string ContractType,
    string Nationality);

public sealed record EndOfServiceBreakdown(string Label, decimal Amount);

public sealed record EndOfServiceResult(
    decimal TotalGratuity,
    string ApplicableRule,
    IReadOnlyList<EndOfServiceBreakdown> Breakdown);

// ── Wage protection export ────────────────────────────────────────────────────

public sealed record WageProtectionExportInput(
    Guid TenantId,
    Guid CompanyId,
    Guid PayrollRunId,
    int PeriodYear,
    int PeriodMonth);

public sealed record WageProtectionExportResult(
    byte[] FileBytes,
    string FileName,
    string Format,
    int RecordCount);

// ── Nationalization tracking ──────────────────────────────────────────────────

public sealed record NationalizationInput(Guid TenantId, Guid CompanyId);

public enum NationalizationComplianceStatus { Compliant, AtRisk, NonCompliant, NotApplicable }

public sealed record NationalizationResult(
    double TargetRatio,
    double CurrentRatio,
    int TotalHeadcount,
    int NationalHeadcount,
    NationalizationComplianceStatus Status);

// ── Localization profile ──────────────────────────────────────────────────────

public sealed record LocalizationProfile(
    string CurrencyCode,
    string CurrencySymbol,
    string LocaleCode,
    bool IsRtl,
    string DateFormat,
    string CalendarSystem);
