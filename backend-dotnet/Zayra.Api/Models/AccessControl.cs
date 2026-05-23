using Zayra.Api.Domain.Entities;

namespace Zayra.Api.Models;

public static class AccessModes
{
    public const string FullPortal = "FullPortal";
    public const string EssOnly = "ESSOnly";
    public const string ManagerPortal = "ManagerPortal";
    public const string Mobile = "Mobile";
    public const string KioskOnly = "KioskOnly";
    public const string NoLogin = "NoLogin";
}

public class EmployeeUserAccount
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid TenantId { get; set; }
    public int EmployeeId { get; set; }
    public Guid? UserId { get; set; }
    public User? User { get; set; }
    public string AccessMode { get; set; } = AccessModes.EssOnly;
    public bool IsPrimary { get; set; } = true;
    public string Status { get; set; } = "Invited";
    public bool RequiresPasswordSetup { get; set; } = true;
    public string InvitationTokenHash { get; set; } = string.Empty;
    public DateTime? InvitationExpiresAtUtc { get; set; }
    public DateTime? InvitedAtUtc { get; set; }
    public DateTime? InvitationAcceptedAtUtc { get; set; }
    public string LoginDisabledReason { get; set; } = string.Empty;
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    public Guid? CreatedBy { get; set; }
    public DateTime? UpdatedAtUtc { get; set; }
    public Guid? UpdatedBy { get; set; }
    public bool IsDeleted { get; set; }
    public DateTime? DeletedAtUtc { get; set; }
    public Guid? DeletedBy { get; set; }
}

public class UserPermissionOverride
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid TenantId { get; set; }
    public Guid UserId { get; set; }
    public User? User { get; set; }
    public string PermissionKey { get; set; } = string.Empty;
    public string Effect { get; set; } = "Allow";
    public string Reason { get; set; } = string.Empty;
    public DateTime? ExpiresAtUtc { get; set; }
    public bool IsActive { get; set; } = true;
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    public Guid? CreatedBy { get; set; }
    public DateTime? UpdatedAtUtc { get; set; }
    public Guid? UpdatedBy { get; set; }
}

public class ApprovalDelegation
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid TenantId { get; set; }
    public int FromEmployeeId { get; set; }
    public int ToEmployeeId { get; set; }
    public Guid? FromUserId { get; set; }
    public Guid? ToUserId { get; set; }
    public string Scope { get; set; } = "All";
    public DateOnly StartDate { get; set; }
    public DateOnly EndDate { get; set; }
    public string Status { get; set; } = "Active";
    public string Reason { get; set; } = string.Empty;
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    public Guid? CreatedBy { get; set; }
}

public class ApprovalAuthority
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid TenantId { get; set; }
    public int EmployeeId { get; set; }
    public Guid? UserId { get; set; }
    public string AuthorityScope { get; set; } = string.Empty;
    public string ApproverRole { get; set; } = string.Empty;
    public decimal? AmountLimit { get; set; }
    public string Currency { get; set; } = string.Empty;
    public bool CanFinalApprove { get; set; }
    public bool IsActive { get; set; } = true;
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    public Guid? CreatedBy { get; set; }
}
