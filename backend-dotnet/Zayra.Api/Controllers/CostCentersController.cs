using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Zayra.Api.Application.Auth;
using Zayra.Api.Application.Common;
using Zayra.Api.Application.Organization;

namespace Zayra.Api.Controllers;

[ApiController]
[Route("api/cost-centers")]
[Authorize(Roles = "Admin,HR Manager,HR Officer,Auditor")]
public class CostCentersController : ControllerBase
{
    private readonly IOrganizationSetupService _organization;

    public CostCentersController(IOrganizationSetupService organization) => _organization = organization;

    [HttpGet]
    public async Task<ActionResult<PagedResult<CostCenterDto>>> Search([FromQuery] Guid? companyId, [FromQuery] int page = 1, [FromQuery] int pageSize = 25, CancellationToken cancellationToken = default)
    {
        var tenantId = this.GetTenantId();
        if (tenantId is null) return Unauthorized();
        return Ok(await _organization.GetCostCentersAsync(tenantId.Value, companyId, page, pageSize, cancellationToken));
    }

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<CostCenterDto>> Get(Guid id, CancellationToken cancellationToken)
    {
        var tenantId = this.GetTenantId();
        if (tenantId is null) return Unauthorized();
        var costCenter = await _organization.GetCostCenterAsync(tenantId.Value, id, cancellationToken);
        return costCenter is null ? NotFound() : Ok(costCenter);
    }

    [HttpPost]
    [Authorize(Roles = "Admin,HR Manager")]
    public async Task<ActionResult<CostCenterDto>> Create(CostCenterRequest request, CancellationToken cancellationToken)
    {
        try
        {
            var tenantId = this.GetTenantId();
            if (tenantId is null) return Unauthorized();
            var costCenter = await _organization.CreateCostCenterAsync(tenantId.Value, request, Context(), cancellationToken);
            return CreatedAtAction(nameof(Get), new { id = costCenter.Id }, costCenter);
        }
        catch (InvalidOperationException ex) { return BadRequest(new { message = ex.Message }); }
    }

    [HttpPut("{id:guid}")]
    [Authorize(Roles = "Admin,HR Manager")]
    public async Task<ActionResult<CostCenterDto>> Update(Guid id, CostCenterRequest request, CancellationToken cancellationToken)
    {
        try
        {
            var tenantId = this.GetTenantId();
            if (tenantId is null) return Unauthorized();
            var costCenter = await _organization.UpdateCostCenterAsync(tenantId.Value, id, request, Context(), cancellationToken);
            return costCenter is null ? NotFound() : Ok(costCenter);
        }
        catch (InvalidOperationException ex) { return BadRequest(new { message = ex.Message }); }
    }

    [HttpDelete("{id:guid}")]
    [Authorize(Roles = "Admin,HR Manager")]
    public async Task<IActionResult> Delete(Guid id, CancellationToken cancellationToken)
    {
        var tenantId = this.GetTenantId();
        if (tenantId is null) return Unauthorized();
        return await _organization.DeleteCostCenterAsync(tenantId.Value, id, Context(), cancellationToken) ? NoContent() : NotFound();
    }

    private RequestContext Context() => new(HttpContext.Connection.RemoteIpAddress?.ToString(), Request.Headers.UserAgent.ToString(), this.GetUserId(), this.GetTenantId());
}
