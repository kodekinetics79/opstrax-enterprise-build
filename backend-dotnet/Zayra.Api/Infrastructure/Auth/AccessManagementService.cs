using Microsoft.EntityFrameworkCore;
using Zayra.Api.Application.Auth;
using Zayra.Api.Data;
using Zayra.Api.Domain.Entities;
using Zayra.Api.Models;

namespace Zayra.Api.Infrastructure.Auth;

public class AccessManagementService : IAccessManagementService
{
    private readonly ZayraDbContext _db;
    private readonly IPasswordHasher _passwordHasher;
    private readonly IAuditService _auditService;
    private readonly ITokenService _tokenService;

    public AccessManagementService(ZayraDbContext db, IPasswordHasher passwordHasher, IAuditService auditService, ITokenService tokenService)
    {
        _db = db;
        _passwordHasher = passwordHasher;
        _auditService = auditService;
        _tokenService = tokenService;
    }

    public async Task<IReadOnlyCollection<RoleDto>> GetRolesAsync(Guid tenantId, CancellationToken cancellationToken)
    {
        return await _db.Roles
            .Include(x => x.RolePermissions).ThenInclude(x => x.Permission)
            .Where(x => x.TenantId == tenantId || x.TenantId == null)
            .OrderBy(x => x.Name)
            .Select(x => new RoleDto(x.Id, x.Name, x.Description, x.RolePermissions.Select(rp => rp.Permission!.Key).OrderBy(p => p).ToList()))
            .ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyCollection<PermissionDto>> GetPermissionsAsync(CancellationToken cancellationToken)
    {
        return await _db.Permissions
            .OrderBy(x => x.Module).ThenBy(x => x.Key)
            .Select(x => new PermissionDto(x.Id, x.Key, x.Module, x.Description))
            .ToListAsync(cancellationToken);
    }

    public async Task<AuthUserDto> CreateUserAsync(Guid tenantId, CreateUserRequest request, RequestContext context, CancellationToken cancellationToken)
    {
        var tenant = await _db.Tenants.FirstOrDefaultAsync(x => x.Id == tenantId && x.IsActive, cancellationToken)
            ?? throw new InvalidOperationException("Tenant not found.");
        var normalizedEmail = AuthService.Normalize(request.Email);
        var exists = await _db.Users.AnyAsync(x => x.TenantId == tenantId && x.NormalizedEmail == normalizedEmail, cancellationToken);
        if (exists) throw new InvalidOperationException("A user with this email already exists in this tenant.");

        var roles = await LoadRoles(tenantId, request.Roles, cancellationToken);
        var user = new User
        {
            TenantId = tenantId,
            Tenant = tenant,
            Email = request.Email.Trim().ToLowerInvariant(),
            NormalizedEmail = normalizedEmail,
            FullName = request.FullName.Trim(),
            PasswordHash = _passwordHasher.Hash(request.Password),
            IsActive = true,
            IsEmailConfirmed = true
        };
        foreach (var role in roles) user.UserRoles.Add(new UserRole { User = user, Role = role });
        _db.Users.Add(user);
        await _db.SaveChangesAsync(cancellationToken);
        await _auditService.WriteAsync("access.user_created", "User", user.Id.ToString(), context, $"{{\"email\":\"{user.Email}\"}}", cancellationToken);
        return ToUserDto(user, tenant, roles);
    }

    public async Task<EmployeeLoginInvitationDto> InviteEmployeeLoginAsync(Guid tenantId, InviteEmployeeLoginRequest request, RequestContext context, CancellationToken cancellationToken)
    {
        var employee = await _db.Employees.FirstOrDefaultAsync(x => x.TenantId == tenantId && x.Id == request.EmployeeId && !x.IsDeleted, cancellationToken)
            ?? throw new InvalidOperationException("Employee not found.");
        var accessMode = NormalizeAccessMode(request.AccessMode);
        var email = (request.Email ?? employee.WorkEmail).Trim().ToLowerInvariant();
        if (string.IsNullOrWhiteSpace(email)) throw new InvalidOperationException("Employee login requires a work email or explicit email.");

        var normalizedEmail = AuthService.Normalize(email);
        var user = await _db.Users
            .Include(x => x.UserRoles)
            .Include(x => x.EmployeeUserAccounts)
            .FirstOrDefaultAsync(x => x.TenantId == tenantId && x.NormalizedEmail == normalizedEmail, cancellationToken);
        if (user is null)
        {
            user = new User
            {
                TenantId = tenantId,
                Email = email,
                NormalizedEmail = normalizedEmail,
                FullName = employee.FullName,
                PasswordHash = _passwordHasher.Hash(Guid.NewGuid().ToString("N") + "!Aa1"),
                IsActive = accessMode != AccessModes.NoLogin,
                IsEmailConfirmed = false
            };
            _db.Users.Add(user);
        }

        var roles = await LoadRoles(tenantId, request.Roles is { Count: > 0 } ? request.Roles : DefaultRoles(accessMode), cancellationToken);
        foreach (var role in roles)
            if (!user.UserRoles.Any(x => x.RoleId == role.Id)) user.UserRoles.Add(new UserRole { User = user, Role = role });

        var invitationToken = accessMode != AccessModes.NoLogin ? _tokenService.CreateSecureToken() : string.Empty;
        var link = user.EmployeeUserAccounts.FirstOrDefault(x => x.TenantId == tenantId && x.EmployeeId == employee.Id && !x.IsDeleted);
        if (link is null)
        {
            link = new EmployeeUserAccount { TenantId = tenantId, EmployeeId = employee.Id, User = user, CreatedBy = context.UserId };
            user.EmployeeUserAccounts.Add(link);
        }
        link.AccessMode = accessMode;
        link.Status = accessMode == AccessModes.NoLogin ? "NoLogin" : "Invited";
        link.RequiresPasswordSetup = accessMode != AccessModes.NoLogin;
        link.InvitedAtUtc = DateTime.UtcNow;
        link.InvitationExpiresAtUtc = accessMode == AccessModes.NoLogin ? null : DateTime.UtcNow.AddHours(Math.Clamp(request.InvitationHours, 1, 720));
        link.InvitationTokenHash = string.IsNullOrEmpty(invitationToken) ? string.Empty : _tokenService.HashToken(invitationToken);
        link.UpdatedAtUtc = DateTime.UtcNow;
        link.UpdatedBy = context.UserId;

        employee.UserAccountId = accessMode == AccessModes.NoLogin ? null : user.Id;
        await _db.SaveChangesAsync(cancellationToken);
        await _auditService.WriteAsync("access.employee_invited", "EmployeeUserAccount", link.Id.ToString(), context, $"{{\"employeeId\":{employee.Id},\"accessMode\":\"{accessMode}\"}}", cancellationToken);
        return new EmployeeLoginInvitationDto(user.Id, employee.Id, user.Email, accessMode, link.Status, invitationToken, link.InvitationExpiresAtUtc);
    }

    public async Task<AuthUserDto> AssignRolesAsync(Guid tenantId, Guid userId, AssignRolesRequest request, RequestContext context, CancellationToken cancellationToken)
    {
        var user = await _db.Users
            .Include(x => x.Tenant)
            .Include(x => x.UserRoles).ThenInclude(x => x.Role).ThenInclude(x => x!.RolePermissions).ThenInclude(x => x.Permission)
            .FirstOrDefaultAsync(x => x.Id == userId && x.TenantId == tenantId, cancellationToken)
            ?? throw new InvalidOperationException("User not found.");
        var roles = await LoadRoles(tenantId, request.Roles, cancellationToken);

        _db.UserRoles.RemoveRange(user.UserRoles);
        user.UserRoles.Clear();
        foreach (var role in roles) user.UserRoles.Add(new UserRole { UserId = user.Id, RoleId = role.Id, Role = role });
        user.UpdatedAtUtc = DateTime.UtcNow;
        await _db.SaveChangesAsync(cancellationToken);
        await _auditService.WriteAsync("access.roles_assigned", "User", user.Id.ToString(), context, $"{{\"roles\":[{string.Join(',', roles.Select(r => $"\"{r.Name}\""))}]}}", cancellationToken);
        return ToUserDto(user, user.Tenant!, roles);
    }

    public async Task<UserAccessDto?> GetUserAccessAsync(Guid tenantId, Guid userId, CancellationToken cancellationToken)
    {
        var user = await LoadAccessUser(tenantId, userId, cancellationToken);
        return user is null ? null : ToAccessDto(user);
    }

    public async Task<UserAccessDto?> SetAccessModeAsync(Guid tenantId, Guid userId, AccessModeRequest request, RequestContext context, CancellationToken cancellationToken)
    {
        var user = await LoadAccessUser(tenantId, userId, cancellationToken);
        if (user is null) return null;
        var accessMode = NormalizeAccessMode(request.AccessMode);
        var link = user.EmployeeUserAccounts.Where(x => !x.IsDeleted).OrderByDescending(x => x.IsPrimary).FirstOrDefault()
            ?? throw new InvalidOperationException("User is not linked to an employee.");
        link.AccessMode = accessMode;
        link.Status = accessMode == AccessModes.NoLogin ? "NoLogin" : "Active";
        link.LoginDisabledReason = accessMode == AccessModes.NoLogin ? request.Reason ?? "No login access" : string.Empty;
        link.UpdatedAtUtc = DateTime.UtcNow;
        link.UpdatedBy = context.UserId;
        user.IsActive = accessMode != AccessModes.NoLogin;
        user.UpdatedAtUtc = DateTime.UtcNow;
        await _db.SaveChangesAsync(cancellationToken);
        await _auditService.WriteAsync("access.mode_changed", "User", user.Id.ToString(), context, $"{{\"accessMode\":\"{accessMode}\",\"reason\":\"{request.Reason ?? string.Empty}\"}}", cancellationToken);
        return ToAccessDto(user);
    }

    public async Task<UserAccessDto?> SetPermissionOverrideAsync(Guid tenantId, Guid userId, PermissionOverrideRequest request, RequestContext context, CancellationToken cancellationToken)
    {
        var user = await LoadAccessUser(tenantId, userId, cancellationToken);
        if (user is null) return null;
        var permissionExists = await _db.Permissions.AnyAsync(x => x.Key == request.PermissionKey, cancellationToken);
        if (!permissionExists) throw new InvalidOperationException("Permission does not exist.");
        var effect = request.Effect.Equals("Deny", StringComparison.OrdinalIgnoreCase) ? "Deny" : "Allow";
        var ov = user.PermissionOverrides.FirstOrDefault(x => x.PermissionKey == request.PermissionKey);
        if (ov is null)
        {
            ov = new UserPermissionOverride { TenantId = tenantId, UserId = userId, PermissionKey = request.PermissionKey, CreatedBy = context.UserId };
            _db.UserPermissionOverrides.Add(ov);
        }
        ov.Effect = effect;
        ov.Reason = request.Reason ?? string.Empty;
        ov.ExpiresAtUtc = request.ExpiresAtUtc;
        ov.IsActive = true;
        ov.UpdatedAtUtc = DateTime.UtcNow;
        ov.UpdatedBy = context.UserId;
        await _db.SaveChangesAsync(cancellationToken);
        await _auditService.WriteAsync("access.permission_override", "UserPermissionOverride", ov.Id.ToString(), context, $"{{\"userId\":\"{userId}\",\"permission\":\"{request.PermissionKey}\",\"effect\":\"{effect}\"}}", cancellationToken);
        return await GetUserAccessAsync(tenantId, userId, cancellationToken);
    }

    public async Task<IReadOnlyCollection<EmployeeTeamMemberDto>> GetTeamAsync(Guid tenantId, int managerEmployeeId, CancellationToken cancellationToken)
    {
        var all = await _db.Employees.AsNoTracking().Where(x => x.TenantId == tenantId && !x.IsDeleted).ToListAsync(cancellationToken);
        var result = new List<EmployeeTeamMemberDto>();
        var frontier = all.Where(x => x.ManagerEmployeeId == managerEmployeeId).Select(x => (Employee: x, Depth: 1)).ToList();
        while (frontier.Count > 0)
        {
            var next = new List<(Employee Employee, int Depth)>();
            foreach (var item in frontier)
            {
                result.Add(new EmployeeTeamMemberDto(item.Employee.Id, item.Employee.EmployeeCode, item.Employee.FullName, item.Employee.Department, item.Employee.Designation, item.Employee.ManagerEmployeeId, item.Depth));
                next.AddRange(all.Where(x => x.ManagerEmployeeId == item.Employee.Id).Select(x => (x, item.Depth + 1)));
            }
            frontier = next;
        }
        return result;
    }

    public async Task<ApprovalDelegationDto> CreateDelegationAsync(Guid tenantId, ApprovalDelegationRequest request, RequestContext context, CancellationToken cancellationToken)
    {
        if (request.FromEmployeeId == request.ToEmployeeId) throw new InvalidOperationException("Delegation must be assigned to a different employee.");
        if (request.EndDate < request.StartDate) throw new InvalidOperationException("Delegation end date must be on or after the start date.");

        var employees = await _db.Employees
            .Where(x => x.TenantId == tenantId && !x.IsDeleted && (x.Id == request.FromEmployeeId || x.Id == request.ToEmployeeId))
            .ToListAsync(cancellationToken);
        var from = employees.FirstOrDefault(x => x.Id == request.FromEmployeeId) ?? throw new InvalidOperationException("Delegating employee not found.");
        var to = employees.FirstOrDefault(x => x.Id == request.ToEmployeeId) ?? throw new InvalidOperationException("Delegate employee not found.");

        var delegation = new ApprovalDelegation
        {
            TenantId = tenantId,
            FromEmployeeId = from.Id,
            ToEmployeeId = to.Id,
            FromUserId = from.UserAccountId,
            ToUserId = to.UserAccountId,
            Scope = request.Scope.Trim(),
            StartDate = request.StartDate,
            EndDate = request.EndDate,
            Reason = request.Reason ?? string.Empty,
            CreatedBy = context.UserId
        };
        _db.ApprovalDelegations.Add(delegation);
        await _db.SaveChangesAsync(cancellationToken);
        await _auditService.WriteAsync("access.approval_delegation_created", "ApprovalDelegation", delegation.Id.ToString(), context, $"{{\"fromEmployeeId\":{from.Id},\"toEmployeeId\":{to.Id},\"scope\":\"{delegation.Scope}\"}}", cancellationToken);
        return ToDelegationDto(delegation);
    }

    public async Task<IReadOnlyCollection<ApprovalDelegationDto>> GetDelegationsAsync(Guid tenantId, CancellationToken cancellationToken)
    {
        var delegations = await _db.ApprovalDelegations.AsNoTracking()
            .Where(x => x.TenantId == tenantId)
            .OrderByDescending(x => x.CreatedAtUtc)
            .ToListAsync(cancellationToken);
        return delegations.Select(ToDelegationDto).ToList();
    }

    public async Task<ApprovalAuthorityDto> CreateAuthorityAsync(Guid tenantId, ApprovalAuthorityRequest request, RequestContext context, CancellationToken cancellationToken)
    {
        var employee = await _db.Employees.FirstOrDefaultAsync(x => x.TenantId == tenantId && x.Id == request.EmployeeId && !x.IsDeleted, cancellationToken)
            ?? throw new InvalidOperationException("Employee not found.");
        var authority = new ApprovalAuthority
        {
            TenantId = tenantId,
            EmployeeId = employee.Id,
            UserId = employee.UserAccountId,
            AuthorityScope = request.AuthorityScope.Trim(),
            ApproverRole = request.ApproverRole.Trim(),
            AmountLimit = request.AmountLimit,
            Currency = request.Currency ?? string.Empty,
            CanFinalApprove = request.CanFinalApprove,
            CreatedBy = context.UserId
        };
        _db.ApprovalAuthorities.Add(authority);
        await _db.SaveChangesAsync(cancellationToken);
        await _auditService.WriteAsync("access.approval_authority_created", "ApprovalAuthority", authority.Id.ToString(), context, $"{{\"employeeId\":{employee.Id},\"scope\":\"{authority.AuthorityScope}\",\"final\":{authority.CanFinalApprove.ToString().ToLowerInvariant()}}}", cancellationToken);
        return ToAuthorityDto(authority);
    }

    public async Task<IReadOnlyCollection<ApprovalAuthorityDto>> GetAuthoritiesAsync(Guid tenantId, CancellationToken cancellationToken)
    {
        var authorities = await _db.ApprovalAuthorities.AsNoTracking()
            .Where(x => x.TenantId == tenantId)
            .OrderByDescending(x => x.CreatedAtUtc)
            .ToListAsync(cancellationToken);
        return authorities.Select(ToAuthorityDto).ToList();
    }

    private async Task<IReadOnlyCollection<Role>> LoadRoles(Guid tenantId, IReadOnlyCollection<string> roleNames, CancellationToken cancellationToken)
    {
        var normalizedRoles = roleNames.Select(AuthService.Normalize).Distinct().ToList();
        var roles = await _db.Roles
            .Include(x => x.RolePermissions).ThenInclude(x => x.Permission)
            .Where(x => normalizedRoles.Contains(x.NormalizedName) && (x.TenantId == tenantId || x.TenantId == null))
            .ToListAsync(cancellationToken);
        if (roles.Count != normalizedRoles.Count) throw new InvalidOperationException("One or more roles are invalid for this tenant.");
        return roles;
    }

    private async Task<User?> LoadAccessUser(Guid tenantId, Guid userId, CancellationToken cancellationToken) =>
        await _db.Users
            .Include(x => x.Tenant)
            .Include(x => x.UserRoles).ThenInclude(x => x.Role).ThenInclude(x => x!.RolePermissions).ThenInclude(x => x.Permission)
            .Include(x => x.EmployeeUserAccounts)
            .Include(x => x.PermissionOverrides)
            .FirstOrDefaultAsync(x => x.TenantId == tenantId && x.Id == userId, cancellationToken);

    private static IReadOnlyCollection<string> DefaultRoles(string accessMode) => accessMode switch
    {
        AccessModes.ManagerPortal => new[] { "Employee", "Manager" },
        AccessModes.KioskOnly or AccessModes.NoLogin => Array.Empty<string>(),
        _ => new[] { "Employee" }
    };

    private static string NormalizeAccessMode(string accessMode) => accessMode.Trim().Replace(" ", "", StringComparison.OrdinalIgnoreCase).ToLowerInvariant() switch
    {
        "fullportal" => AccessModes.FullPortal,
        "essonly" => AccessModes.EssOnly,
        "managerportal" => AccessModes.ManagerPortal,
        "mobile" => AccessModes.Mobile,
        "kioskonly" => AccessModes.KioskOnly,
        "nologin" => AccessModes.NoLogin,
        _ => throw new InvalidOperationException("Invalid access mode.")
    };

    private static UserAccessDto ToAccessDto(User user)
    {
        var link = user.EmployeeUserAccounts.Where(x => !x.IsDeleted).OrderByDescending(x => x.IsPrimary).FirstOrDefault();
        var roles = user.UserRoles.Select(x => x.Role?.Name).Where(x => !string.IsNullOrWhiteSpace(x)).Select(x => x!).Distinct().OrderBy(x => x).ToList();
        var basePermissions = user.UserRoles.SelectMany(x => x.Role?.RolePermissions ?? Array.Empty<RolePermission>()).Select(x => x.Permission?.Key).Where(x => !string.IsNullOrWhiteSpace(x)).Select(x => x!).ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var ov in user.PermissionOverrides.Where(x => x.IsActive && (x.ExpiresAtUtc is null || x.ExpiresAtUtc > DateTime.UtcNow)))
        {
            if (ov.Effect == "Allow") basePermissions.Add(ov.PermissionKey);
            if (ov.Effect == "Deny") basePermissions.Remove(ov.PermissionKey);
        }
        var denied = user.PermissionOverrides
            .Where(x => x.IsActive && x.Effect == "Deny")
            .Select(x => x.PermissionKey)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(x => x)
            .ToList();
        return new UserAccessDto(user.Id, link?.EmployeeId, user.Email, user.FullName, link?.AccessMode ?? AccessModes.FullPortal, link?.RequiresPasswordSetup ?? false, roles, basePermissions.OrderBy(x => x).ToList(), denied);
    }

    private static AuthUserDto ToUserDto(User user, Tenant tenant, IReadOnlyCollection<Role> roles)
    {
        var permissions = roles
            .SelectMany(x => x.RolePermissions)
            .Select(x => x.Permission!.Key)
            .Distinct()
            .OrderBy(x => x)
            .ToList();
        return new AuthUserDto(user.Id, user.TenantId, tenant.Slug, user.Email, user.FullName, roles.Select(x => x.Name).OrderBy(x => x).ToList(), permissions);
    }

    private static ApprovalDelegationDto ToDelegationDto(ApprovalDelegation delegation) =>
        new(delegation.Id, delegation.FromEmployeeId, delegation.ToEmployeeId, delegation.FromUserId, delegation.ToUserId, delegation.Scope, delegation.StartDate, delegation.EndDate, delegation.Status, delegation.Reason);

    private static ApprovalAuthorityDto ToAuthorityDto(ApprovalAuthority authority) =>
        new(authority.Id, authority.EmployeeId, authority.UserId, authority.AuthorityScope, authority.ApproverRole, authority.AmountLimit, authority.Currency, authority.CanFinalApprove, authority.IsActive);
}
