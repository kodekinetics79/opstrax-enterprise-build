namespace Zayra.Api.Models;

// ── Platform role constants ────────────────────────────────────────────────────

public static class PlatformRoles
{
    public const string Owner     = "Owner";
    public const string Admin     = "Admin";
    public const string Finance   = "Finance";
    public const string Support   = "Support";
    public const string Marketing = "Marketing";
    public const string Auditor   = "Auditor";

    public static readonly IReadOnlySet<string> All = new HashSet<string>
    {
        Owner, Admin, Finance, Support, Marketing, Auditor
    };
}

// ── Platform User (separate from tenant users) ────────────────────────────────

public class PlatformUser
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Email { get; set; } = null!;
    public string FullName { get; set; } = null!;
    public string PasswordHash { get; set; } = null!;
    public string Role { get; set; } = PlatformRoles.Admin; // Owner, Admin, Finance, Support, Marketing, Auditor
    public bool IsActive { get; set; } = true;
    public DateTime? LastLoginAtUtc { get; set; }
    public string? LastLoginIp { get; set; }
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime? UpdatedAtUtc { get; set; }
}

// ── Subscription status constants ─────────────────────────────────────────────

public static class SubscriptionStatuses
{
    public const string Trial          = "Trial";
    public const string Active         = "Active";
    public const string PastDue        = "PastDue";
    public const string Suspended      = "Suspended";
    public const string Cancelled      = "Cancelled";
    public const string ManualContract = "ManualContract";

    public static readonly IReadOnlySet<string> All = new HashSet<string>
    {
        Trial, Active, PastDue, Suspended, Cancelled, ManualContract
    };
}

// ── Feature key constants ─────────────────────────────────────────────────────

public static class FeatureKeys
{
    public const string Recruitment = "recruitment";
    public const string Performance = "performance";
    public const string Compliance  = "compliance";
    public const string AiAssistant = "ai_assistant";
    public const string Finance     = "finance";
    public const string Payroll     = "payroll";
    public const string Shifts      = "shifts";
    public const string Overtime    = "overtime";
    public const string MobileApp        = "mobile_app";
    public const string WpsExport        = "wps_export";
    public const string QiwaIntegration  = "qiwa_integration";
}

// ── Tenant Subscription ───────────────────────────────────────────────────────

public class TenantSubscription
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid TenantId { get; set; }
    public string Plan { get; set; } = "Starter"; // Starter, Growth, Enterprise
    public string Status { get; set; } = "Active"; // see SubscriptionStatuses constants
    public DateTime StartedAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime? ExpiresAtUtc { get; set; }
    public int MaxEmployees { get; set; } = 50;
    public int MaxUsers { get; set; } = 10;
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

// ── Tenant Field Help Text (admin-customizable tooltips) ─────────────────────

public class TenantFieldHelpText
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid TenantId { get; set; }
    /// <summary>Stable key identifying the form field, e.g. "employees.joining_date".</summary>
    public string FieldKey { get; set; } = string.Empty;
    /// <summary>Tenant-specific tooltip text shown instead of the built-in default.</summary>
    public string Text { get; set; } = string.Empty;
    public DateTime UpdatedAtUtc { get; set; } = DateTime.UtcNow;
    public Guid? UpdatedBy { get; set; }
}

// ── Platform Support Sessions (break-glass access) ───────────────────────────

/// <summary>Audit-trail record for every platform-admin support access session.</summary>
public class PlatformSupportSession
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid TenantId { get; set; }
    public Guid TargetUserId { get; set; }
    public string TargetUserEmail { get; set; } = string.Empty;
    public string Reason { get; set; } = string.Empty;
    public string StartedByEmail { get; set; } = string.Empty;
    public string StartedByIp { get; set; } = string.Empty;
    public DateTime StartedAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime ExpiresAtUtc { get; set; }
    public DateTime? EndedAtUtc { get; set; }
    /// <summary>SHA-256 hash of the impersonation JWT — lets us match the token to a session on End.</summary>
    public string TokenHash { get; set; } = string.Empty;
    public bool IsActive => EndedAtUtc is null && DateTime.UtcNow < ExpiresAtUtc;
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

// ── Tenant AI Usage ───────────────────────────────────────────────────────────

public class TenantAiUsage
{
    public Guid TenantId { get; set; }
    public int YearMonth { get; set; } // YYYYMM
    public long TokensUsed { get; set; }
    public int RequestCount { get; set; }
    public int BlockedCount { get; set; }
    public DateTime LastUpdatedUtc { get; set; } = DateTime.UtcNow;
}

// ── Tenant Invoices ───────────────────────────────────────────────────────────

public class TenantInvoice
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid TenantId { get; set; }
    public string InvoiceNumber { get; set; } = string.Empty;
    public decimal Amount { get; set; }
    public string CurrencyCode { get; set; } = "USD";
    /// <summary>Draft | Sent | Paid | Overdue | Cancelled</summary>
    public string Status { get; set; } = "Draft";
    public string? PaymentMethod { get; set; }   // BankTransfer, Cheque, Online, etc.
    public string? PaymentReference { get; set; }
    public string? PeriodDescription { get; set; } // e.g. "June 2026"
    public DateOnly InvoiceDate { get; set; }
    public DateOnly DueDate { get; set; }
    public DateOnly? PaidDate { get; set; }
    public string? Notes { get; set; }
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime? UpdatedAtUtc { get; set; }
    public Guid? CreatedBy { get; set; }
}

public static class InvoiceStatuses
{
    public const string Draft     = "Draft";
    public const string Sent      = "Sent";
    public const string Paid      = "Paid";
    public const string Overdue   = "Overdue";
    public const string Cancelled = "Cancelled";
}

public static class AiPlanLimits
{
    // Monthly token limits per plan. 0 = unlimited.
    public static long GetMonthlyTokenLimit(string plan) => plan switch
    {
        "Starter"    => 50_000,
        "Growth"     => 200_000,
        "Enterprise" => 0,
        _            => 50_000
    };
}

// ── Platform Announcements ────────────────────────────────────────────────────

public class PlatformAnnouncement
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Title { get; set; } = null!;
    public string Body { get; set; } = null!;
    /// <summary>"All", "Trial", "Starter", "Growth", "Enterprise"</summary>
    public string TargetPlan { get; set; } = "All";
    /// <summary>Draft | Published | Archived</summary>
    public string Status { get; set; } = "Draft";
    public DateTime? PublishedAtUtc { get; set; }
    public DateTime? ExpiresAtUtc { get; set; }
    public string CreatedByEmail { get; set; } = null!;
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime? UpdatedAtUtc { get; set; }
}

// ── Platform Leads ────────────────────────────────────────────────────────────

public class PlatformLead
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string CompanyName { get; set; } = null!;
    public string ContactName { get; set; } = null!;
    public string ContactEmail { get; set; } = null!;
    public string? Phone { get; set; }
    public string? Message { get; set; }
    /// <summary>New | Contacted | DemoScheduled | Converted | Lost</summary>
    public string Status { get; set; } = "New";
    public string? Notes { get; set; }
    /// <summary>Platform user email assigned to this lead.</summary>
    public string? AssignedTo { get; set; }
    /// <summary>Manual | WebForm | Import</summary>
    public string Source { get; set; } = "Manual";
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime? UpdatedAtUtc { get; set; }
    public Guid? ConvertedToTenantId { get; set; }
}
