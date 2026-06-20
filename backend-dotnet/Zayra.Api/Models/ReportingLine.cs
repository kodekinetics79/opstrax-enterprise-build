using Zayra.Api.Domain.Entities;
namespace Zayra.Api.Models;

/// <summary>
/// Represents a formal reporting relationship between two employees.
/// SolidLine = direct manager chain, DottedLine = functional/matrix relationship,
/// Temporary = covers like leave replacements, Functional = cross-team technical lead.
/// </summary>
public class ReportingLine : ITenantOwned
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid TenantId { get; set; }
    public int EmployeeId { get; set; }
    public int ManagerEmployeeId { get; set; }
    /// <summary>SolidLine | DottedLine | Temporary | Functional</summary>
    public string RelationshipType { get; set; } = "SolidLine";
    public DateTime EffectiveFrom { get; set; } = DateTime.UtcNow;
    public DateTime? EffectiveTo { get; set; }
    public bool IsPrimary { get; set; } = true;
    public bool IsActive { get; set; } = true;
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    public Guid? CreatedBy { get; set; }
    public DateTime? UpdatedAtUtc { get; set; }
    public Guid? UpdatedBy { get; set; }
}
