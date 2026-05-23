using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Zayra.Api.Application.Auth;
using Zayra.Api.Application.Common;
using Zayra.Api.Application.Organization;

namespace Zayra.Api.Controllers;

[ApiController]
[Route("api/branches")]
[Authorize(Roles = "Admin,HR Manager,HR Officer,Auditor")]
public class BranchesController : ControllerBase
{
    private readonly IOrganizationSetupService _organization;

    public BranchesController(IOrganizationSetupService organization)
    {
        _organization = organization;
    }

    [HttpGet]
    public async Task<ActionResult<PagedResult<BranchDto>>> Search([FromQuery] Guid? companyId, [FromQuery] int page = 1, [FromQuery] int pageSize = 25, CancellationToken cancellationToken = default)
    {
        var tenantId = this.GetTenantId();
        if (tenantId is null) return Unauthorized();
        return Ok(await _organization.GetBranchesAsync(tenantId.Value, companyId, page, pageSize, cancellationToken));
    }

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<BranchDto>> Get(Guid id, CancellationToken cancellationToken)
    {
        var tenantId = this.GetTenantId();
        if (tenantId is null) return Unauthorized();
        var branch = await _organization.GetBranchAsync(tenantId.Value, id, cancellationToken);
        return branch is null ? NotFound() : Ok(branch);
    }

    [HttpPost]
    [Authorize(Roles = "Admin,HR Manager")]
    public async Task<ActionResult<BranchDto>> Create(BranchRequest request, CancellationToken cancellationToken)
    {
        try
        {
            var tenantId = this.GetTenantId();
            if (tenantId is null) return Unauthorized();
            var branch = await _organization.CreateBranchAsync(tenantId.Value, request, Context(), cancellationToken);
            return CreatedAtAction(nameof(Get), new { id = branch.Id }, branch);
        }
        catch (InvalidOperationException ex) { return BadRequest(new { message = ex.Message }); }
    }

    [HttpPut("{id:guid}")]
    [Authorize(Roles = "Admin,HR Manager")]
    public async Task<ActionResult<BranchDto>> Update(Guid id, BranchRequest request, CancellationToken cancellationToken)
    {
        try
        {
            var tenantId = this.GetTenantId();
            if (tenantId is null) return Unauthorized();
            var branch = await _organization.UpdateBranchAsync(tenantId.Value, id, request, Context(), cancellationToken);
            return branch is null ? NotFound() : Ok(branch);
        }
        catch (InvalidOperationException ex) { return BadRequest(new { message = ex.Message }); }
    }

    [HttpDelete("{id:guid}")]
    [Authorize(Roles = "Admin,HR Manager")]
    public async Task<IActionResult> Delete(Guid id, CancellationToken cancellationToken)
    {
        var tenantId = this.GetTenantId();
        if (tenantId is null) return Unauthorized();
        return await _organization.DeleteBranchAsync(tenantId.Value, id, Context(), cancellationToken) ? NoContent() : NotFound();
    }

    private RequestContext Context() => new(HttpContext.Connection.RemoteIpAddress?.ToString(), Request.Headers.UserAgent.ToString(), this.GetUserId(), this.GetTenantId());
}
