using Zayra.Api.Domain.Entities;
namespace Zayra.Api.Models;

public class EmployeeHistory : ITenantOwned
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid TenantId { get; set; }
    public int EmployeeId { get; set; }
    public string EventType { get; set; } = string.Empty;
    public string FieldName { get; set; } = string.Empty;
    public string OldValue { get; set; } = string.Empty;
    public string NewValue { get; set; } = string.Empty;
    public DateOnly EffectiveDate { get; set; }
    public string Reason { get; set; } = string.Empty;
    public Guid? ApprovedByUserId { get; set; }
    public Guid? SupportingDocumentId { get; set; }
    public string SnapshotJson { get; set; } = "{}";
    public Guid? CreatedByUserId { get; set; }
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
}
