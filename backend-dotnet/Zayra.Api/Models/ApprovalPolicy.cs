using Zayra.Api.Domain.Entities;
namespace Zayra.Api.Models;

/// <summary>
/// Tenant-configurable approval routing policy for a specific workflow type.
/// Matches on WorkflowType + optional DepartmentId + optional GradeId.
/// When IsDefault = true, this policy applies to all employees not matched by a more specific policy.
/// </summary>
public class ApprovalPolicy : ITenantOwned
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid TenantId { get; set; }
    /// <summary>Leave | Overtime | Payroll | Expense | Recruitment | Travel</summary>
    public string WorkflowType { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public Guid? DepartmentId { get; set; }
    public Guid? GradeId { get; set; }
    public bool IsDefault { get; set; }
    public bool IsActive { get; set; } = true;
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    public Guid? CreatedBy { get; set; }
    public DateTime? UpdatedAtUtc { get; set; }
    public Guid? UpdatedBy { get; set; }
    public bool IsDeleted { get; set; }
    public DateTime? DeletedAtUtc { get; set; }

    public ICollection<ApprovalPolicyStep> Steps { get; set; } = new List<ApprovalPolicyStep>();
}
