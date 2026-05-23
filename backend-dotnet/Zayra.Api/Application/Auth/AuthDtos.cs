using System.ComponentModel.DataAnnotations;

namespace Zayra.Api.Application.Auth;

public record LoginRequest(
    [Required, EmailAddress] string Email,
    [Required] string Password,
    string? TenantSlug);

public record RefreshTokenRequest([Required] string RefreshToken);

public record LogoutRequest([Required] string RefreshToken);

public record ForgotPasswordRequest([Required, EmailAddress] string Email, string? TenantSlug);

public record ResetPasswordRequest(
    [Required, EmailAddress] string Email,
    [Required] string ResetToken,
    [Required, MinLength(10)] string NewPassword,
    string? TenantSlug);

public record AcceptInvitationRequest(
    [Required, EmailAddress] string Email,
    [Required] string InvitationToken,
    [Required, MinLength(10)] string NewPassword,
    string? TenantSlug);

public record CreateUserRequest(
    [Required, EmailAddress] string Email,
    [Required] string FullName,
    [Required, MinLength(10)] string Password,
    IReadOnlyCollection<string> Roles);

public record AssignRolesRequest([Required] IReadOnlyCollection<string> Roles);

public record InviteEmployeeLoginRequest(
    [Required] int EmployeeId,
    string? Email,
    [Required] string AccessMode,
    IReadOnlyCollection<string>? Roles,
    int InvitationHours = 72);

public record EmployeeLoginInvitationDto(
    Guid UserId,
    int EmployeeId,
    string Email,
    string AccessMode,
    string Status,
    string InvitationToken,
    DateTime? InvitationExpiresAtUtc);

public record AccessModeRequest([Required] string AccessMode, string? Reason);

public record PermissionOverrideRequest([Required] string PermissionKey, [Required] string Effect, string? Reason, DateTime? ExpiresAtUtc);

public record ApprovalDelegationRequest(
    [Required] int FromEmployeeId,
    [Required] int ToEmployeeId,
    [Required] string Scope,
    DateOnly StartDate,
    DateOnly EndDate,
    string? Reason);

public record ApprovalDelegationDto(
    Guid Id,
    int FromEmployeeId,
    int ToEmployeeId,
    Guid? FromUserId,
    Guid? ToUserId,
    string Scope,
    DateOnly StartDate,
    DateOnly EndDate,
    string Status,
    string Reason);

public record ApprovalAuthorityRequest(
    [Required] int EmployeeId,
    [Required] string AuthorityScope,
    [Required] string ApproverRole,
    decimal? AmountLimit,
    string? Currency,
    bool CanFinalApprove);

public record ApprovalAuthorityDto(
    Guid Id,
    int EmployeeId,
    Guid? UserId,
    string AuthorityScope,
    string ApproverRole,
    decimal? AmountLimit,
    string Currency,
    bool CanFinalApprove,
    bool IsActive);

public record UserAccessDto(
    Guid UserId,
    int? EmployeeId,
    string Email,
    string FullName,
    string AccessMode,
    bool RequiresPasswordSetup,
    IReadOnlyCollection<string> Roles,
    IReadOnlyCollection<string> Permissions,
    IReadOnlyCollection<string> DeniedPermissions);

public record AuthResponse(
    string AccessToken,
    string RefreshToken,
    DateTime ExpiresAtUtc,
    AuthUserDto User);

public record AuthUserDto(
    Guid Id,
    Guid TenantId,
    string TenantSlug,
    string Email,
    string FullName,
    IReadOnlyCollection<string> Roles,
    IReadOnlyCollection<string> Permissions,
    int? EmployeeId = null,
    string AccessMode = "FullPortal",
    bool RequiresPasswordSetup = false);

public record ForgotPasswordResponse(string Message, string? ResetToken, DateTime? ResetTokenExpiresAtUtc);

public record RoleDto(Guid Id, string Name, string Description, IReadOnlyCollection<string> Permissions);

public record PermissionDto(Guid Id, string Key, string Module, string Description);
