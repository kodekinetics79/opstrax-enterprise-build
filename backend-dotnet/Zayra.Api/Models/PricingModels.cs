namespace Zayra.Api.Models;

// ── Module keys for pricing (superset of FeatureKeys) ─────────────────────────

public static class PricingModuleKeys
{
    public const string CoreHr            = "core_hr";
    public const string LeaveAttendance   = "leave_attendance";
    public const string Payroll           = "payroll";
    public const string Performance       = "performance";
    public const string Recruitment       = "recruitment";
    public const string Documents         = "documents";
    public const string Compliance        = "compliance";
    public const string KsaCompliance     = "ksa_compliance";
    public const string AiAssistant       = "ai_assistant";
    public const string AdvancedAnalytics = "advanced_analytics";
    public const string MobileApp         = "mobile_app";
    public const string SsoMfa            = "sso_mfa";
}

// ── Platform-wide pricing parameters (one row per key) ────────────────────────

public class PricingConfig
{
    public string Key { get; set; } = null!;
    public string Label { get; set; } = null!;
    // "base" | "per_employee" | "per_company" | "per_admin_user" | "implementation" | "supplement"
    public string Group { get; set; } = null!;
    // plan scope: "starter" | "growth" | "enterprise" | "all"
    public string Plan { get; set; } = "all";
    public decimal Value { get; set; }
    public DateTime UpdatedAtUtc { get; set; } = DateTime.UtcNow;
}

// ── Per-module pricing configuration ──────────────────────────────────────────

public class PricingModuleConfig
{
    public string ModuleKey { get; set; } = null!;
    public string ModuleName { get; set; } = null!;
    public bool IncludedInTrial { get; set; }
    public bool IncludedInStarter { get; set; }
    public bool IncludedInGrowth { get; set; }
    public bool IncludedInEnterprise { get; set; }
    public bool IsEnterpriseOnly { get; set; }
    public decimal AddonPriceMonthly { get; set; }
    public int SortOrder { get; set; }
    public DateTime UpdatedAtUtc { get; set; } = DateTime.UtcNow;
}

// ── Customer pricing quote request ────────────────────────────────────────────

public class PricingQuote
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string CompanyName { get; set; } = null!;
    public string ContactName { get; set; } = null!;
    public string ContactEmail { get; set; } = null!;
    public string? Phone { get; set; }
    // "single" | "group" | "enterprise_holding"
    public string OrgType { get; set; } = "single";
    public int NumCompanies { get; set; } = 1;
    public int NumBranches { get; set; }
    public int NumEmployees { get; set; }
    public int NumAdminUsers { get; set; }
    public int NumCountries { get; set; } = 1;
    public bool NeedsArabic { get; set; }
    public string SelectedModulesJson { get; set; } = "[]";
    public decimal EstimatedMonthlyAmount { get; set; }
    public decimal EstimatedAnnualAmount { get; set; }
    public string? Notes { get; set; }
    // "New" | "Contacted" | "Converted" | "Lost"
    public string Status { get; set; } = "New";
    public Guid? ConvertedToTenantId { get; set; }
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime? UpdatedAtUtc { get; set; }
}

public static class QuoteStatuses
{
    public const string New       = "New";
    public const string Contacted = "Contacted";
    public const string Converted = "Converted";
    public const string Lost      = "Lost";
}
