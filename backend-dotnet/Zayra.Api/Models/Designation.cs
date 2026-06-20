using Zayra.Api.Domain.Entities;
namespace Zayra.Api.Models;

public class Designation : ITenantOwned
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid TenantId { get; set; }
    public Guid? DepartmentId { get; set; }
    public string Code { get; set; } = string.Empty;
    public string TitleEn { get; set; } = string.Empty;
    public string TitleAr { get; set; } = string.Empty;
    public string JobGrade { get; set; } = string.Empty;
    public Guid? GradeId { get; set; }
    public string JobLevel { get; set; } = string.Empty;
    public string JobDescription { get; set; } = string.Empty;
    public bool IsManagerRole { get; set; }
    public bool IsSystemDefault { get; set; }
    public int LevelRank { get; set; }
    public bool IsActive { get; set; } = true;
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    public Guid? CreatedBy { get; set; }
    public DateTime? UpdatedAtUtc { get; set; }
    public Guid? UpdatedBy { get; set; }
    public bool IsDeleted { get; set; }
    public DateTime? DeletedAtUtc { get; set; }
    public Guid? DeletedBy { get; set; }
}
