using Zayra.Api.Domain.Entities;
namespace Zayra.Api.Models;

public class ApprovalWorkflowStep : ITenantOwned
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid TenantId { get; set; }
    public Guid WorkflowId { get; set; }
    public int StepOrder { get; set; }
    public string StepName { get; set; } = string.Empty;
    public string ApproverRole { get; set; } = string.Empty;
    /// <summary>Manager | Supervisor | DepartmentHead | HR | HRBusinessPartner | SpecificEmployee | Role</summary>
    public string ApproverType { get; set; } = "Role";
    public int? SpecificEmployeeId { get; set; }
    public int? EscalationAfterHours { get; set; }
    public bool IsFinalStep { get; set; }
}
