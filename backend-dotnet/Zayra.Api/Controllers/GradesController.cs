using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Zayra.Api.Application.Auth;
using Zayra.Api.Application.Common;
using Zayra.Api.Application.Organization;

namespace Zayra.Api.Controllers;

[ApiController]
[Route("api/grades")]
[Authorize(Roles = "Admin,HR Manager,HR Officer,Auditor")]
public class GradesController : ControllerBase
{
    private readonly IOrganizationSetupService _organization;

    public GradesController(IOrganizationSetupService organization) => _organization = organization;

    [HttpGet]
    public async Task<ActionResult<PagedResult<GradeDto>>> Search([FromQuery] int page = 1, [FromQuery] int pageSize = 25, CancellationToken cancellationToken = default)
    {
        var tenantId = this.GetTenantId();
        if (tenantId is null) return Unauthorized();
        return Ok(await _organization.GetGradesAsync(tenantId.Value, page, pageSize, cancellationToken));
    }

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<GradeDto>> Get(Guid id, CancellationToken cancellationToken)
    {
        var tenantId = this.GetTenantId();
        if (tenantId is null) return Unauthorized();
        var grade = await _organization.GetGradeAsync(tenantId.Value, id, cancellationToken);
        return grade is null ? NotFound() : Ok(grade);
    }

    [HttpPost]
    [Authorize(Roles = "Admin,HR Manager")]
    public async Task<ActionResult<GradeDto>> Create(GradeRequest request, CancellationToken cancellationToken)
    {
        try
        {
            var tenantId = this.GetTenantId();
            if (tenantId is null) return Unauthorized();
            var grade = await _organization.CreateGradeAsync(tenantId.Value, request, Context(), cancellationToken);
            return CreatedAtAction(nameof(Get), new { id = grade.Id }, grade);
        }
        catch (InvalidOperationException ex) { return BadRequest(new { message = ex.Message }); }
    }

    [HttpPut("{id:guid}")]
    [Authorize(Roles = "Admin,HR Manager")]
    public async Task<ActionResult<GradeDto>> Update(Guid id, GradeRequest request, CancellationToken cancellationToken)
    {
        try
        {
            var tenantId = this.GetTenantId();
            if (tenantId is null) return Unauthorized();
            var grade = await _organization.UpdateGradeAsync(tenantId.Value, id, request, Context(), cancellationToken);
            return grade is null ? NotFound() : Ok(grade);
        }
        catch (InvalidOperationException ex) { return BadRequest(new { message = ex.Message }); }
    }

    [HttpDelete("{id:guid}")]
    [Authorize(Roles = "Admin,HR Manager")]
    public async Task<IActionResult> Delete(Guid id, CancellationToken cancellationToken)
    {
        var tenantId = this.GetTenantId();
        if (tenantId is null) return Unauthorized();
        return await _organization.DeleteGradeAsync(tenantId.Value, id, Context(), cancellationToken) ? NoContent() : NotFound();
    }

    private RequestContext Context() => new(HttpContext.Connection.RemoteIpAddress?.ToString(), Request.Headers.UserAgent.ToString(), this.GetUserId(), this.GetTenantId());
}
