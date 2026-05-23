using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Zayra.Api.Application.Auth;
using Zayra.Api.Application.Common;
using Zayra.Api.Application.Organization;

namespace Zayra.Api.Controllers;

[ApiController]
[Route("api/companies")]
[Authorize(Roles = "Admin,HR Manager,HR Officer,Auditor")]
public class CompaniesController : ControllerBase
{
    private readonly IOrganizationSetupService _organization;

    public CompaniesController(IOrganizationSetupService organization)
    {
        _organization = organization;
    }

    [HttpGet]
    public async Task<ActionResult<PagedResult<CompanyDto>>> Search([FromQuery] int page = 1, [FromQuery] int pageSize = 25, CancellationToken cancellationToken = default)
    {
        var tenantId = this.GetTenantId();
        if (tenantId is null) return Unauthorized();
        return Ok(await _organization.GetCompaniesAsync(tenantId.Value, page, pageSize, cancellationToken));
    }

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<CompanyDto>> Get(Guid id, CancellationToken cancellationToken)
    {
        var tenantId = this.GetTenantId();
        if (tenantId is null) return Unauthorized();
        var company = await _organization.GetCompanyAsync(tenantId.Value, id, cancellationToken);
        return company is null ? NotFound() : Ok(company);
    }

    [HttpPost]
    [Authorize(Roles = "Admin,HR Manager")]
    public async Task<ActionResult<CompanyDto>> Create(CompanyRequest request, CancellationToken cancellationToken)
    {
        try
        {
            var tenantId = this.GetTenantId();
            if (tenantId is null) return Unauthorized();
            var company = await _organization.CreateCompanyAsync(tenantId.Value, request, Context(), cancellationToken);
            return CreatedAtAction(nameof(Get), new { id = company.Id }, company);
        }
        catch (InvalidOperationException ex) { return BadRequest(new { message = ex.Message }); }
    }

    [HttpPut("{id:guid}")]
    [Authorize(Roles = "Admin,HR Manager")]
    public async Task<ActionResult<CompanyDto>> Update(Guid id, CompanyRequest request, CancellationToken cancellationToken)
    {
        try
        {
            var tenantId = this.GetTenantId();
            if (tenantId is null) return Unauthorized();
            var company = await _organization.UpdateCompanyAsync(tenantId.Value, id, request, Context(), cancellationToken);
            return company is null ? NotFound() : Ok(company);
        }
        catch (InvalidOperationException ex) { return BadRequest(new { message = ex.Message }); }
    }

    [HttpDelete("{id:guid}")]
    [Authorize(Roles = "Admin,HR Manager")]
    public async Task<IActionResult> Delete(Guid id, CancellationToken cancellationToken)
    {
        var tenantId = this.GetTenantId();
        if (tenantId is null) return Unauthorized();
        return await _organization.DeleteCompanyAsync(tenantId.Value, id, Context(), cancellationToken) ? NoContent() : NotFound();
    }

    private RequestContext Context() => new(HttpContext.Connection.RemoteIpAddress?.ToString(), Request.Headers.UserAgent.ToString(), this.GetUserId(), this.GetTenantId());
}
