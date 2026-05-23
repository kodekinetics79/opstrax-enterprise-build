namespace Zayra.Api.Models;

public class ApprovalDecision
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid TenantId { get; set; }
    public Guid ApprovalRequestId { get; set; }
    public int StepOrder { get; set; }
    public string Decision { get; set; } = string.Empty;
    public string Comments { get; set; } = string.Empty;
    public Guid? DecidedByUserId { get; set; }
    public DateTime DecidedAtUtc { get; set; } = DateTime.UtcNow;
}
