using System.ComponentModel.DataAnnotations;
using Zayra.Api.Models;

namespace Zayra.Api.Application.Approvals;

public record ApprovalWorkflowDto(
    Guid Id,
    string Code,
    string Name,
    string EntityName,
    bool IsActive,
    IReadOnlyCollection<ApprovalWorkflowStepDto> Steps);

public record ApprovalWorkflowStepDto(
    Guid Id,
    int StepOrder,
    string StepName,
    string ApproverRole,
    bool IsFinalStep);

public record ApprovalWorkflowRequest(
    [Required, MaxLength(80)] string Code,
    [Required, MaxLength(180)] string Name,
    [Required, MaxLength(120)] string EntityName,
    bool IsActive,
    [Required, MinLength(1)] IReadOnlyCollection<ApprovalWorkflowStepRequest> Steps);

public record ApprovalWorkflowStepRequest(
    [Range(1, 50)] int StepOrder,
    [Required, MaxLength(120)] string StepName,
    [Required, MaxLength(80)] string ApproverRole,
    bool IsFinalStep);

public record ApprovalRequestDto(
    Guid Id,
    Guid WorkflowId,
    string EntityName,
    string EntityId,
    string Title,
    string Status,
    int CurrentStepOrder,
    Guid? RequestedByUserId,
    DateTime CreatedAtUtc,
    DateTime? CompletedAtUtc,
    IReadOnlyCollection<ApprovalDecisionDto> Decisions);

public record CreateApprovalRequest(
    [Required] Guid WorkflowId,
    [Required, MaxLength(120)] string EntityName,
    [Required, MaxLength(80)] string EntityId,
    [Required, MaxLength(240)] string Title);

public record ApprovalDecisionRequest(
    [Required, RegularExpression("Approve|Reject", ErrorMessage = "Decision must be Approve or Reject.")] string Decision,
    [MaxLength(1000)] string? Comments);

public record ApprovalDecisionDto(
    Guid Id,
    int StepOrder,
    string Decision,
    string Comments,
    Guid? DecidedByUserId,
    DateTime DecidedAtUtc);

public static class ApprovalMappings
{
    public static ApprovalWorkflowDto ToDto(this ApprovalWorkflow workflow) => new(
        workflow.Id,
        workflow.Code,
        workflow.Name,
        workflow.EntityName,
        workflow.IsActive,
        workflow.Steps.OrderBy(x => x.StepOrder).Select(x => x.ToDto()).ToList());

    public static ApprovalWorkflowStepDto ToDto(this ApprovalWorkflowStep step) => new(
        step.Id,
        step.StepOrder,
        step.StepName,
        step.ApproverRole,
        step.IsFinalStep);

    public static ApprovalRequestDto ToDto(this ApprovalRequest request) => new(
        request.Id,
        request.WorkflowId,
        request.EntityName,
        request.EntityId,
        request.Title,
        request.Status,
        request.CurrentStepOrder,
        request.RequestedByUserId,
        request.CreatedAtUtc,
        request.CompletedAtUtc,
        request.Decisions.OrderBy(x => x.StepOrder).Select(x => x.ToDto()).ToList());

    public static ApprovalDecisionDto ToDto(this ApprovalDecision decision) => new(
        decision.Id,
        decision.StepOrder,
        decision.Decision,
        decision.Comments,
        decision.DecidedByUserId,
        decision.DecidedAtUtc);
}
