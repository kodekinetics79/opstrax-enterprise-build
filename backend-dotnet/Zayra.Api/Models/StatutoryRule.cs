namespace Zayra.Api.Models;

// Effective-dated configuration row for a country + jurisdiction.
// Null TenantId = platform-wide default; non-null = tenant override.
// Keys: CountryCode + Jurisdiction + RuleKey + EffectiveFrom.
public class StatutoryRule
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid? TenantId { get; set; }
    public string CountryCode { get; set; } = string.Empty;   // e.g. "SAU", "QAT", "ARE"
    public string Jurisdiction { get; set; } = string.Empty;   // e.g. "KSA-mainland", "UAE-DIFC"
    public string RuleKey { get; set; } = string.Empty;        // e.g. "gosi.employee_rate"
    public string RuleValue { get; set; } = string.Empty;
    public string DataType { get; set; } = "decimal";          // string / decimal / int / bool / json
    public string Description { get; set; } = string.Empty;
    public DateTime EffectiveFrom { get; set; }
    public DateTime? EffectiveTo { get; set; }
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    public Guid? CreatedBy { get; set; }
}
