namespace Zayra.Api.Models;

/// <summary>
/// One step in an ApprovalPolicy.
/// ApproverType determines how the actual approver is resolved at runtime:
///   Manager           → Employee.ManagerEmployeeId of the requester
///   Supervisor        → Employee.SupervisorEmployeeId
///   DepartmentHead    → Department.ManagerEmployeeId of the requester's department
///   HR                → Any user with the HR Manager or HR Officer role for this tenant
///   HRBusinessPartner → Employee.HRBusinessPartnerEmployeeId
///   SpecificEmployee  → SpecificEmployeeId (fixed person)
///   Role              → Any user with ApproverRole role (legacy behaviour)
/// </summary>
public class ApprovalPolicyStep
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid TenantId { get; set; }
    public Guid PolicyId { get; set; }
    public int StepOrder { get; set; }
    public string StepName { get; set; } = string.Empty;
    /// <summary>Manager | Supervisor | DepartmentHead | HR | HRBusinessPartner | SpecificEmployee | Role</summary>
    public string ApproverType { get; set; } = "Manager";
    public int? SpecificEmployeeId { get; set; }
    public string? ApproverRole { get; set; }
    public int? EscalationAfterHours { get; set; }
    public bool IsFinalStep { get; set; }
}
