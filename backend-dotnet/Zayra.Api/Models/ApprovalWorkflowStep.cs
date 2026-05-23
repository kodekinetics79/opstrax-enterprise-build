namespace Zayra.Api.Models;

public class ApprovalWorkflowStep
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid TenantId { get; set; }
    public Guid WorkflowId { get; set; }
    public int StepOrder { get; set; }
    public string StepName { get; set; } = string.Empty;
    public string ApproverRole { get; set; } = string.Empty;
    public bool IsFinalStep { get; set; }
}
