using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Zayra.Api.Application.Auth;

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

    [HttpGet("permissions")]
    public async Task<ActionResult<IReadOnlyCollection<PermissionDto>>> Permissions(CancellationToken cancellationToken)
    {
        return Ok(await _accessManagement.GetPermissionsAsync(cancellationToken));
    }

    [HttpPost("users")]
    public async Task<ActionResult<AuthUserDto>> CreateUser(CreateUserRequest request, CancellationToken cancellationToken)
    {
        try
        {
            var tenantId = GetTenantId();
            if (tenantId is null) return Unauthorized();
            var user = await _accessManagement.CreateUserAsync(tenantId.Value, request, GetContext(), cancellationToken);
            return CreatedAtAction(nameof(CreateUser), new { id = user.Id }, user);
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
