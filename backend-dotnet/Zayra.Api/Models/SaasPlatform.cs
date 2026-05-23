namespace Zayra.Api.Models;

// ── Tenant Subscription ───────────────────────────────────────────────────────

public class TenantSubscription
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid TenantId { get; set; }
    public string Plan { get; set; } = "Starter"; // Starter, Growth, Enterprise
    public string Status { get; set; } = "Active"; // Active, Suspended, Cancelled, Trial
    public DateTime StartedAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime? ExpiresAtUtc { get; set; }
    public int MaxEmployees { get; set; } = 50;
    public string BillingEmail { get; set; } = string.Empty;
    public string BillingCycle { get; set; } = "Monthly"; // Monthly, Annual
    public decimal MonthlyAmount { get; set; }
    public string CurrencyCode { get; set; } = "USD";
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime? UpdatedAtUtc { get; set; }
}

// ── Tenant Feature Flags ──────────────────────────────────────────────────────

public class TenantFeatureFlag
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid TenantId { get; set; }
    public string FeatureKey { get; set; } = string.Empty; // ai_assistant, mobile_app, wps_export, etc.
    public bool IsEnabled { get; set; }
    public string? ConfigJson { get; set; }
    public DateTime UpdatedAtUtc { get; set; } = DateTime.UtcNow;
    public Guid? UpdatedBy { get; set; }
}

// ── Tenant Localization ───────────────────────────────────────────────────────

public class TenantLocalizationSetting
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid TenantId { get; set; }
    public string DefaultLanguage { get; set; } = "en"; // en, ar
    public bool RtlEnabled { get; set; }
    public string CalendarSystem { get; set; } = "Gregorian"; // Gregorian, Hijri
    public string DefaultTimezone { get; set; } = "Asia/Dubai";
    public string DateFormat { get; set; } = "DD/MM/YYYY";
    public string CurrencyCode { get; set; } = "AED";
    public string CountryCode { get; set; } = "AE";
    public string WeekStartDay { get; set; } = "Sunday";
    public string WorkWeek { get; set; } = "Sun-Thu"; // Sun-Thu, Mon-Fri, Mon-Sat
    public bool HijriDatesEnabled { get; set; }
    public DateTime UpdatedAtUtc { get; set; } = DateTime.UtcNow;
}

// ── Tenant Branding ───────────────────────────────────────────────────────────

public class TenantBranding
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid TenantId { get; set; }
    public string LogoUrl { get; set; } = string.Empty;
    public string PrimaryColor { get; set; } = "#2563EB";
    public string AccentColor { get; set; } = "#7C3AED";
    public string CompanyNameEn { get; set; } = string.Empty;
    public string CompanyNameAr { get; set; } = string.Empty;
    public string PortalTitle { get; set; } = string.Empty;
    public string FaviconUrl { get; set; } = string.Empty;
    public DateTime UpdatedAtUtc { get; set; } = DateTime.UtcNow;
}

// ── Country Payroll Rules (Configurable per country) ─────────────────────────

public class CountryPayrollRule
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid TenantId { get; set; }
    public string CountryCode { get; set; } = string.Empty;
    public string RuleKey { get; set; } = string.Empty; // eosb_rate, overtime_multiplier_holiday, gratuity_threshold_years, etc.
    public string RuleValue { get; set; } = string.Empty;
    public string DataType { get; set; } = "string"; // string, decimal, int, bool, json
    public string Description { get; set; } = string.Empty;
    public bool IsOverride { get; set; }
    public DateTime EffectiveFrom { get; set; } = DateTime.UtcNow;
    public DateTime? EffectiveTo { get; set; }
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    public Guid? CreatedBy { get; set; }
}
