using Zayra.Api.Domain.Entities;
namespace Zayra.Api.Models;

public class EmployeeTransferRequest : ITenantOwned
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid TenantId { get; set; }
    public int EmployeeId { get; set; }
    public string CurrentBranch { get; set; } = string.Empty;
    public string CurrentDepartment { get; set; } = string.Empty;
    public string CurrentDesignation { get; set; } = string.Empty;
    public int? CurrentManagerEmployeeId { get; set; }
    public string NewDepartment { get; set; } = string.Empty;
    public string NewBranch { get; set; } = string.Empty;
    public string NewDesignation { get; set; } = string.Empty;
    public int? NewManagerEmployeeId { get; set; }
    public DateOnly EffectiveDate { get; set; }
    public string Reason { get; set; } = string.Empty;
    public string Status { get; set; } = "PendingCurrentManager";
    public Guid? RequestedByUserId { get; set; }
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime? CurrentManagerApprovedAtUtc { get; set; }
    public DateTime? NewManagerApprovedAtUtc { get; set; }
    public DateTime? HrApprovedAtUtc { get; set; }
}
