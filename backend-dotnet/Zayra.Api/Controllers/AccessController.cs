using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Zayra.Api.Application.Auth;
using Zayra.Api.Application.Common;

namespace Zayra.Api.Controllers;

[ApiController]
[Route("api/access")]
[Authorize(Roles = "Admin")]
public class AccessController : ControllerBase
{
    private readonly IAccessManagementService _accessManagement;

    public AccessController(IAccessManagementService accessManagement)
    {
        _accessManagement = accessManagement;
    }

    [HttpGet("roles")]
    public async Task<ActionResult<IReadOnlyCollection<RoleDto>>> Roles(CancellationToken cancellationToken)
    {
        var tenantId = GetTenantId();
        if (tenantId is null) return Unauthorized();
        return Ok(await _accessManagement.GetRolesAsync(tenantId.Value, cancellationToken));
    }

    [HttpPost("roles")]
    public async Task<ActionResult<RoleDto>> CreateRole(CreateRoleRequest request, CancellationToken cancellationToken)
    {
        try
        {
            var tenantId = GetTenantId();
            if (tenantId is null) return Unauthorized();
            var role = await _accessManagement.CreateRoleAsync(tenantId.Value, request, GetContext(), cancellationToken);
            return CreatedAtAction(nameof(Roles), role);
        }
        catch (InvalidOperationException ex) { return BadRequest(new { message = ex.Message }); }
    }

    [HttpPut("roles/{roleId:guid}")]
    public async Task<ActionResult<RoleDto>> UpdateRole(Guid roleId, UpdateRoleRequest request, CancellationToken cancellationToken)
    {
        try
        {
            var tenantId = GetTenantId();
            if (tenantId is null) return Unauthorized();
            var role = await _accessManagement.UpdateRoleAsync(tenantId.Value, roleId, request, GetContext(), cancellationToken);
            return role is null ? NotFound() : Ok(role);
        }
        catch (InvalidOperationException ex) { return BadRequest(new { message = ex.Message }); }
    }

    [HttpPatch("roles/{roleId:guid}/activate")]
    public async Task<IActionResult> ActivateRole(Guid roleId, CancellationToken cancellationToken)
    {
        var tenantId = GetTenantId();
        if (tenantId is null) return Unauthorized();
        return await _accessManagement.ActivateRoleAsync(tenantId.Value, roleId, GetContext(), cancellationToken) ? NoContent() : NotFound();
    }

    [HttpPatch("roles/{roleId:guid}/deactivate")]
    public async Task<IActionResult> DeactivateRole(Guid roleId, CancellationToken cancellationToken)
    {
        try
        {
            var tenantId = GetTenantId();
            if (tenantId is null) return Unauthorized();
            return await _accessManagement.DeactivateRoleAsync(tenantId.Value, roleId, GetContext(), cancellationToken) ? NoContent() : NotFound();
        }
        catch (InvalidOperationException ex) { return BadRequest(new { message = ex.Message }); }
    }

    [HttpPut("roles/{roleId:guid}/permissions")]
    public async Task<ActionResult<RoleDto>> SetRolePermissions(Guid roleId, BulkRolePermissionsRequest request, CancellationToken cancellationToken)
    {
        var tenantId = GetTenantId();
        if (tenantId is null) return Unauthorized();
        var role = await _accessManagement.SetRolePermissionsAsync(tenantId.Value, roleId, request, GetContext(), cancellationToken);
        return role is null ? NotFound() : Ok(role);
    }

    [HttpGet("permission-matrix")]
    public async Task<ActionResult<PermissionMatrixDto>> GetPermissionMatrix(CancellationToken cancellationToken)
    {
        var tenantId = GetTenantId();
        if (tenantId is null) return Unauthorized();
        return Ok(await _accessManagement.GetPermissionMatrixAsync(tenantId.Value, cancellationToken));
    }

    [HttpPut("permission-matrix")]
    public async Task<IActionResult> SavePermissionMatrix(PermissionMatrixUpdateRequest request, CancellationToken cancellationToken)
    {
        var tenantId = GetTenantId();
        if (tenantId is null) return Unauthorized();
        await _accessManagement.SavePermissionMatrixAsync(tenantId.Value, request, GetContext(), cancellationToken);
        return NoContent();
    }

    [HttpGet("users/{userId:guid}/effective-permissions")]
    public async Task<ActionResult<EffectivePermissionsDto>> GetEffectivePermissions(Guid userId, CancellationToken cancellationToken)
    {
        var tenantId = GetTenantId();
        if (tenantId is null) return Unauthorized();
        var result = await _accessManagement.GetEffectivePermissionsAsync(tenantId.Value, userId, cancellationToken);
        return result is null ? NotFound() : Ok(result);
    }

    [HttpDelete("users/{userId:guid}/permission-overrides/{overrideId:guid}")]
    public async Task<IActionResult> DeletePermissionOverride(Guid userId, Guid overrideId, CancellationToken cancellationToken)
    {
        var tenantId = GetTenantId();
        if (tenantId is null) return Unauthorized();
        return await _accessManagement.DeletePermissionOverrideAsync(tenantId.Value, userId, overrideId, GetContext(), cancellationToken) ? NoContent() : NotFound();
    }

    [HttpGet("permissions")]
    public async Task<ActionResult<IReadOnlyCollection<PermissionDto>>> Permissions(CancellationToken cancellationToken)
    {
        return Ok(await _accessManagement.GetPermissionsAsync(cancellationToken));
    }

    [HttpGet("users")]
    public async Task<ActionResult<PagedResult<UserListDto>>> ListUsers([FromQuery] string? search, [FromQuery] string? status, [FromQuery] string? role, [FromQuery] int page = 1, [FromQuery] int pageSize = 30, CancellationToken cancellationToken = default)
    {
        var tenantId = GetTenantId();
        if (tenantId is null) return Unauthorized();
        var result = await _accessManagement.ListUsersAsync(tenantId.Value, new UserListQuery(search, status, role, page, Math.Clamp(pageSize, 1, 100)), cancellationToken);
        return Ok(result);
    }

    [HttpGet("users/{userId:guid}")]
    public async Task<ActionResult<UserListDto>> GetUser(Guid userId, CancellationToken cancellationToken)
    {
        var tenantId = GetTenantId();
        if (tenantId is null) return Unauthorized();
        var user = await _accessManagement.GetUserAsync(tenantId.Value, userId, cancellationToken);
        return user is null ? NotFound() : Ok(user);
    }

    [HttpPost("users")]
    public async Task<ActionResult<AuthUserDto>> CreateUser(CreateUserRequest request, CancellationToken cancellationToken)
    {
        try
        {
            var tenantId = GetTenantId();
            if (tenantId is null) return Unauthorized();
            var user = await _accessManagement.CreateUserAsync(tenantId.Value, request, GetContext(), cancellationToken);
            return CreatedAtAction(nameof(GetUser), new { userId = user.Id }, user);
        }
        catch (InvalidOperationException ex) { return BadRequest(new { message = ex.Message }); }
    }

    [HttpPut("users/{userId:guid}")]
    public async Task<ActionResult<UserListDto>> UpdateUser(Guid userId, UpdateUserRequest request, CancellationToken cancellationToken)
    {
        try
        {
            var tenantId = GetTenantId();
            if (tenantId is null) return Unauthorized();
            var user = await _accessManagement.UpdateUserAsync(tenantId.Value, userId, request, GetContext(), cancellationToken);
            return user is null ? NotFound() : Ok(user);
        }
        catch (InvalidOperationException ex) { return BadRequest(new { message = ex.Message }); }
    }

    [HttpPatch("users/{userId:guid}/activate")]
    public async Task<IActionResult> ActivateUser(Guid userId, CancellationToken cancellationToken)
    {
        try
        {
            var tenantId = GetTenantId();
            if (tenantId is null) return Unauthorized();
            await _accessManagement.ActivateUserAsync(tenantId.Value, userId, GetContext(), cancellationToken);
            return NoContent();
        }
        catch (InvalidOperationException ex) { return BadRequest(new { message = ex.Message }); }
    }

    [HttpPatch("users/{userId:guid}/suspend")]
    public async Task<IActionResult> SuspendUser(Guid userId, [FromBody] ReasonRequest? body, CancellationToken cancellationToken)
    {
        try
        {
            var tenantId = GetTenantId();
            if (tenantId is null) return Unauthorized();
            await _accessManagement.SuspendUserAsync(tenantId.Value, userId, body?.Reason ?? string.Empty, GetContext(), cancellationToken);
            return NoContent();
        }
        catch (InvalidOperationException ex) { return BadRequest(new { message = ex.Message }); }
    }

    [HttpPatch("users/{userId:guid}/lock")]
    public async Task<IActionResult> LockUser(Guid userId, [FromBody] ReasonRequest? body, CancellationToken cancellationToken)
    {
        try
        {
            var tenantId = GetTenantId();
            if (tenantId is null) return Unauthorized();
            await _accessManagement.LockUserAsync(tenantId.Value, userId, body?.Reason ?? string.Empty, GetContext(), cancellationToken);
            return NoContent();
        }
        catch (InvalidOperationException ex) { return BadRequest(new { message = ex.Message }); }
    }

    [HttpPatch("users/{userId:guid}/unlock")]
    public async Task<IActionResult> UnlockUser(Guid userId, CancellationToken cancellationToken)
    {
        try
        {
            var tenantId = GetTenantId();
            if (tenantId is null) return Unauthorized();
            await _accessManagement.UnlockUserAsync(tenantId.Value, userId, GetContext(), cancellationToken);
            return NoContent();
        }
        catch (InvalidOperationException ex) { return BadRequest(new { message = ex.Message }); }
    }

    [HttpPost("users/{userId:guid}/admin-reset-password")]
    public async Task<IActionResult> AdminResetPassword(Guid userId, AdminResetPasswordRequest request, CancellationToken cancellationToken)
    {
        try
        {
            var tenantId = GetTenantId();
            if (tenantId is null) return Unauthorized();
            await _accessManagement.AdminResetPasswordAsync(tenantId.Value, userId, request, GetContext(), cancellationToken);
            return NoContent();
        }
        catch (InvalidOperationException ex) { return BadRequest(new { message = ex.Message }); }
    }

    [HttpDelete("users/{userId:guid}")]
    public async Task<IActionResult> DeleteUser(Guid userId, CancellationToken cancellationToken)
    {
        try
        {
            var tenantId = GetTenantId();
            if (tenantId is null) return Unauthorized();
            return await _accessManagement.DeleteUserAsync(tenantId.Value, userId, GetContext(), cancellationToken) ? NoContent() : NotFound();
        }
        catch (InvalidOperationException ex) { return BadRequest(new { message = ex.Message }); }
    }

    [HttpPut("users/{userId:guid}/roles")]
    public async Task<ActionResult<AuthUserDto>> AssignRoles(Guid userId, AssignRolesRequest request, CancellationToken cancellationToken)
    {
        try
        {
            var tenantId = GetTenantId();
            if (tenantId is null) return Unauthorized();
            return Ok(await _accessManagement.AssignRolesAsync(tenantId.Value, userId, request, GetContext(), cancellationToken));
        }
        catch (InvalidOperationException ex) { return BadRequest(new { message = ex.Message }); }
    }

    [HttpPost("employee-logins/invite")]
    public async Task<ActionResult<EmployeeLoginInvitationDto>> InviteEmployeeLogin(InviteEmployeeLoginRequest request, CancellationToken cancellationToken)
    {
        try
        {
            var tenantId = GetTenantId();
            if (tenantId is null) return Unauthorized();
            var invite = await _accessManagement.InviteEmployeeLoginAsync(tenantId.Value, request, GetContext(), cancellationToken);
            return Created($"/api/access/users/{invite.UserId}", invite);
        }
        catch (InvalidOperationException ex) { return BadRequest(new { message = ex.Message }); }
    }

    [HttpGet("users/{userId:guid}/access")]
    public async Task<ActionResult<UserAccessDto>> UserAccess(Guid userId, CancellationToken cancellationToken)
    {
        var tenantId = GetTenantId();
        if (tenantId is null) return Unauthorized();
        var access = await _accessManagement.GetUserAccessAsync(tenantId.Value, userId, cancellationToken);
        return access is null ? NotFound() : Ok(access);
    }

    [HttpPut("users/{userId:guid}/access-mode")]
    public async Task<ActionResult<UserAccessDto>> SetAccessMode(Guid userId, AccessModeRequest request, CancellationToken cancellationToken)
    {
        try
        {
            var tenantId = GetTenantId();
            if (tenantId is null) return Unauthorized();
            var access = await _accessManagement.SetAccessModeAsync(tenantId.Value, userId, request, GetContext(), cancellationToken);
            return access is null ? NotFound() : Ok(access);
        }
        catch (InvalidOperationException ex) { return BadRequest(new { message = ex.Message }); }
    }

    [HttpPost("users/{userId:guid}/permission-overrides")]
    public async Task<ActionResult<UserAccessDto>> SetPermissionOverride(Guid userId, PermissionOverrideRequest request, CancellationToken cancellationToken)
    {
        try
        {
            var tenantId = GetTenantId();
            if (tenantId is null) return Unauthorized();
            var access = await _accessManagement.SetPermissionOverrideAsync(tenantId.Value, userId, request, GetContext(), cancellationToken);
            return access is null ? NotFound() : Ok(access);
        }
        catch (InvalidOperationException ex) { return BadRequest(new { message = ex.Message }); }
    }

    [HttpGet("employees/{managerEmployeeId:int}/team")]
    public async Task<ActionResult<IReadOnlyCollection<EmployeeTeamMemberDto>>> Team(int managerEmployeeId, CancellationToken cancellationToken)
    {
        var tenantId = GetTenantId();
        if (tenantId is null) return Unauthorized();
        return Ok(await _accessManagement.GetTeamAsync(tenantId.Value, managerEmployeeId, cancellationToken));
    }

    [HttpGet("approval-delegations")]
    public async Task<ActionResult<IReadOnlyCollection<ApprovalDelegationDto>>> ApprovalDelegations(CancellationToken cancellationToken)
    {
        var tenantId = GetTenantId();
        if (tenantId is null) return Unauthorized();
        return Ok(await _accessManagement.GetDelegationsAsync(tenantId.Value, cancellationToken));
    }

    [HttpPost("approval-delegations")]
    public async Task<ActionResult<ApprovalDelegationDto>> CreateApprovalDelegation(ApprovalDelegationRequest request, CancellationToken cancellationToken)
    {
        try
        {
            var tenantId = GetTenantId();
            if (tenantId is null) return Unauthorized();
            var delegation = await _accessManagement.CreateDelegationAsync(tenantId.Value, request, GetContext(), cancellationToken);
            return Created($"/api/access/approval-delegations/{delegation.Id}", delegation);
        }
        catch (InvalidOperationException ex) { return BadRequest(new { message = ex.Message }); }
    }

    [HttpGet("approval-authorities")]
    public async Task<ActionResult<IReadOnlyCollection<ApprovalAuthorityDto>>> ApprovalAuthorities(CancellationToken cancellationToken)
    {
        var tenantId = GetTenantId();
        if (tenantId is null) return Unauthorized();
        return Ok(await _accessManagement.GetAuthoritiesAsync(tenantId.Value, cancellationToken));
    }

    [HttpPost("approval-authorities")]
    public async Task<ActionResult<ApprovalAuthorityDto>> CreateApprovalAuthority(ApprovalAuthorityRequest request, CancellationToken cancellationToken)
    {
        try
        {
            var tenantId = GetTenantId();
            if (tenantId is null) return Unauthorized();
            var authority = await _accessManagement.CreateAuthorityAsync(tenantId.Value, request, GetContext(), cancellationToken);
            return Created($"/api/access/approval-authorities/{authority.Id}", authority);
        }
        catch (InvalidOperationException ex) { return BadRequest(new { message = ex.Message }); }
    }

    [HttpPatch("approval-delegations/{delegationId:guid}/cancel")]
    public async Task<IActionResult> CancelDelegation(Guid delegationId, CancellationToken cancellationToken)
    {
        var tenantId = GetTenantId();
        if (tenantId is null) return Unauthorized();
        var found = await _accessManagement.CancelDelegationAsync(tenantId.Value, delegationId, GetContext(), cancellationToken);
        return found ? NoContent() : NotFound();
    }

    [HttpPut("approval-authorities/{authorityId:guid}")]
    public async Task<ActionResult<ApprovalAuthorityDto>> UpdateApprovalAuthority(Guid authorityId, ApprovalAuthorityRequest request, CancellationToken cancellationToken)
    {
        try
        {
            var tenantId = GetTenantId();
            if (tenantId is null) return Unauthorized();
            var authority = await _accessManagement.UpdateAuthorityAsync(tenantId.Value, authorityId, request, GetContext(), cancellationToken);
            return authority is null ? NotFound() : Ok(authority);
        }
        catch (InvalidOperationException ex) { return BadRequest(new { message = ex.Message }); }
    }

    // ── Permission Grantors ───────────────────────────────────────────────────

    [HttpGet("permission-grantors")]
    public async Task<ActionResult<IReadOnlyCollection<PermissionGrantorDto>>> GetGrantors(CancellationToken cancellationToken)
    {
        var tenantId = GetTenantId();
        if (tenantId is null) return Unauthorized();
        return Ok(await _accessManagement.GetGrantorsAsync(tenantId.Value, cancellationToken));
    }

    [HttpPost("permission-grantors")]
    public async Task<ActionResult<PermissionGrantorDto>> AddGrantor(AddGrantorRequest request, CancellationToken cancellationToken)
    {
        try
        {
            var tenantId = GetTenantId();
            if (tenantId is null) return Unauthorized();
            return Ok(await _accessManagement.AddGrantorAsync(tenantId.Value, request, GetContext(), cancellationToken));
        }
        catch (InvalidOperationException ex) { return BadRequest(new { message = ex.Message }); }
    }

    [HttpDelete("permission-grantors/{recordId:guid}")]
    public async Task<IActionResult> RevokeGrantor(Guid recordId, CancellationToken cancellationToken)
    {
        var tenantId = GetTenantId();
        if (tenantId is null) return Unauthorized();
        var found = await _accessManagement.RevokeGrantorAsync(tenantId.Value, recordId, GetContext(), cancellationToken);
        return found ? NoContent() : NotFound();
    }

    // Not restricted to Admin role — service layer checks grantor authority or Admin claim
    [HttpPost("users/{userId:guid}/grant-permission")]
    [Microsoft.AspNetCore.Authorization.AllowAnonymous]
    public async Task<ActionResult<UserAccessDto>> GrantPermission(Guid userId, GrantPermissionRequest request, CancellationToken cancellationToken)
    {
        try
        {
            var tenantId = GetTenantId();
            if (tenantId is null) return Unauthorized();
            var isAdmin = User.IsInRole("Admin");
            var access = await _accessManagement.GrantPermissionAsync(tenantId.Value, userId, request, GetUserId(), isAdmin, cancellationToken);
            return access is null ? NotFound() : Ok(access);
        }
        catch (UnauthorizedAccessException) { return Forbid(); }
        catch (InvalidOperationException ex) { return BadRequest(new { message = ex.Message }); }
    }

    [HttpGet("security-settings")]
    public async Task<ActionResult<SecuritySettingDto>> GetSecuritySettings(CancellationToken cancellationToken)
    {
        var tenantId = GetTenantId();
        if (tenantId is null) return Unauthorized();
        return Ok(await _accessManagement.GetSecuritySettingsAsync(tenantId.Value, cancellationToken));
    }

    [HttpPut("security-settings")]
    public async Task<ActionResult<SecuritySettingDto>> UpdateSecuritySettings(UpdateSecuritySettingRequest request, CancellationToken cancellationToken)
    {
        var tenantId = GetTenantId();
        if (tenantId is null) return Unauthorized();
        return Ok(await _accessManagement.UpdateSecuritySettingsAsync(tenantId.Value, request, GetContext(), cancellationToken));
    }

    private RequestContext GetContext()
    {
        return new RequestContext(HttpContext.Connection.RemoteIpAddress?.ToString(), Request.Headers.UserAgent.ToString(), GetUserId(), GetTenantId());
    }

    private Guid? GetUserId()
    {
        var value = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? User.FindFirstValue("sub");
        return Guid.TryParse(value, out var id) ? id : null;
    }

    private Guid? GetTenantId()
    {
        var value = User.FindFirstValue("tenant_id");
        return Guid.TryParse(value, out var id) ? id : null;
    }
}
