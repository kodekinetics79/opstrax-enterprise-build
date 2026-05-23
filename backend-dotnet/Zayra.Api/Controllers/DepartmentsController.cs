using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Zayra.Api.Application.Auth;
using Zayra.Api.Application.Common;
using Zayra.Api.Application.Organization;

namespace Zayra.Api.Controllers;

[ApiController]
[Route("api/departments")]
[Authorize(Roles = "Admin,HR Manager,HR Officer,Auditor")]
public class DepartmentsController : ControllerBase
{
    private readonly IOrganizationSetupService _organization;

    public DepartmentsController(IOrganizationSetupService organization)
    {
        _organization = organization;
    }

    [HttpGet]
    public async Task<ActionResult<PagedResult<DepartmentDto>>> Search([FromQuery] Guid? branchId, [FromQuery] int page = 1, [FromQuery] int pageSize = 25, CancellationToken cancellationToken = default)
    {
        var tenantId = this.GetTenantId();
        if (tenantId is null) return Unauthorized();
        return Ok(await _organization.GetDepartmentsAsync(tenantId.Value, branchId, page, pageSize, cancellationToken));
    }

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<DepartmentDto>> Get(Guid id, CancellationToken cancellationToken)
    {
        var tenantId = this.GetTenantId();
        if (tenantId is null) return Unauthorized();
        var department = await _organization.GetDepartmentAsync(tenantId.Value, id, cancellationToken);
        return department is null ? NotFound() : Ok(department);
    }

    [HttpPost]
    [Authorize(Roles = "Admin,HR Manager")]
    public async Task<ActionResult<DepartmentDto>> Create(DepartmentRequest request, CancellationToken cancellationToken)
    {
        try
        {
            var tenantId = this.GetTenantId();
            if (tenantId is null) return Unauthorized();
            var department = await _organization.CreateDepartmentAsync(tenantId.Value, request, Context(), cancellationToken);
            return CreatedAtAction(nameof(Get), new { id = department.Id }, department);
        }
        catch (InvalidOperationException ex) { return BadRequest(new { message = ex.Message }); }
    }

    [HttpPut("{id:guid}")]
    [Authorize(Roles = "Admin,HR Manager")]
    public async Task<ActionResult<DepartmentDto>> Update(Guid id, DepartmentRequest request, CancellationToken cancellationToken)
    {
        try
        {
            var tenantId = this.GetTenantId();
            if (tenantId is null) return Unauthorized();
            var department = await _organization.UpdateDepartmentAsync(tenantId.Value, id, request, Context(), cancellationToken);
            return department is null ? NotFound() : Ok(department);
        }
        catch (InvalidOperationException ex) { return BadRequest(new { message = ex.Message }); }
    }

    [HttpDelete("{id:guid}")]
    [Authorize(Roles = "Admin,HR Manager")]
    public async Task<IActionResult> Delete(Guid id, CancellationToken cancellationToken)
    {
        var tenantId = this.GetTenantId();
        if (tenantId is null) return Unauthorized();
        return await _organization.DeleteDepartmentAsync(tenantId.Value, id, Context(), cancellationToken) ? NoContent() : NotFound();
    }

    private RequestContext Context() => new(HttpContext.Connection.RemoteIpAddress?.ToString(), Request.Headers.UserAgent.ToString(), this.GetUserId(), this.GetTenantId());
}
