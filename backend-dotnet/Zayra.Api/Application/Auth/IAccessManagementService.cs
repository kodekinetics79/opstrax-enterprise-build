namespace Zayra.Api.Application.Auth;

public interface IAccessManagementService
{
    Task<IReadOnlyCollection<RoleDto>> GetRolesAsync(Guid tenantId, CancellationToken cancellationToken);
    Task<IReadOnlyCollection<PermissionDto>> GetPermissionsAsync(CancellationToken cancellationToken);
    Task<AuthUserDto> CreateUserAsync(Guid tenantId, CreateUserRequest request, RequestContext context, CancellationToken cancellationToken);
    Task<AuthUserDto> AssignRolesAsync(Guid tenantId, Guid userId, AssignRolesRequest request, RequestContext context, CancellationToken cancellationToken);
    Task<EmployeeLoginInvitationDto> InviteEmployeeLoginAsync(Guid tenantId, InviteEmployeeLoginRequest request, RequestContext context, CancellationToken cancellationToken);
    Task<UserAccessDto?> GetUserAccessAsync(Guid tenantId, Guid userId, CancellationToken cancellationToken);
    Task<UserAccessDto?> SetAccessModeAsync(Guid tenantId, Guid userId, AccessModeRequest request, RequestContext context, CancellationToken cancellationToken);
    Task<UserAccessDto?> SetPermissionOverrideAsync(Guid tenantId, Guid userId, PermissionOverrideRequest request, RequestContext context, CancellationToken cancellationToken);
    Task<IReadOnlyCollection<EmployeeTeamMemberDto>> GetTeamAsync(Guid tenantId, int managerEmployeeId, CancellationToken cancellationToken);
    Task<ApprovalDelegationDto> CreateDelegationAsync(Guid tenantId, ApprovalDelegationRequest request, RequestContext context, CancellationToken cancellationToken);
    Task<IReadOnlyCollection<ApprovalDelegationDto>> GetDelegationsAsync(Guid tenantId, CancellationToken cancellationToken);
    Task<ApprovalAuthorityDto> CreateAuthorityAsync(Guid tenantId, ApprovalAuthorityRequest request, RequestContext context, CancellationToken cancellationToken);
    Task<IReadOnlyCollection<ApprovalAuthorityDto>> GetAuthoritiesAsync(Guid tenantId, CancellationToken cancellationToken);
}

public record EmployeeTeamMemberDto(int EmployeeId, string EmployeeCode, string FullName, string Department, string Designation, int? ManagerEmployeeId, int Depth);
