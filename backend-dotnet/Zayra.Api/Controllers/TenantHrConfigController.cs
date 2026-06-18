using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Zayra.Api.Application.Common;
using Zayra.Api.Data;
using Zayra.Api.Domain.Entities;

namespace Zayra.Api.Controllers;

[ApiController]
[Route("api/tenant-hr-config")]
[Authorize(Roles = "Admin,HR Manager")]
public class TenantHrConfigController : ControllerBase
{
    private readonly ZayraDbContext _db;

    public TenantHrConfigController(ZayraDbContext db) => _db = db;

    [HttpGet]
    public async Task<IActionResult> Get(CancellationToken ct)
    {
        var tenantId = this.GetTenantId();
        if (tenantId is null) return Unauthorized();

        var config = await _db.TenantHrConfigs
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.TenantId == tenantId.Value, ct);

        if (config is null)
        {
            // Return safe defaults without persisting
            return Ok(new TenantHrConfig { TenantId = tenantId.Value });
        }

        return Ok(config);
    }

    [HttpPut]
    public async Task<IActionResult> Upsert([FromBody] TenantHrConfigRequest req, CancellationToken ct)
    {
        var tenantId = this.GetTenantId();
        if (tenantId is null) return Unauthorized();

        var config = await _db.TenantHrConfigs
            .FirstOrDefaultAsync(x => x.TenantId == tenantId.Value, ct);

        if (config is null)
        {
            config = new TenantHrConfig { TenantId = tenantId.Value };
            _db.TenantHrConfigs.Add(config);
        }

        config.UseDeptHeadApproval = req.UseDeptHeadApproval;
        config.UseHrFinalApproval = req.UseHrFinalApproval;
        config.UseSupervisorBeforeManager = req.UseSupervisorBeforeManager;
        config.AllowDottedLineApproval = req.AllowDottedLineApproval;
        config.AutoCreateDeptOnImport = req.AutoCreateDeptOnImport;
        config.AutoCreateDesignationOnImport = req.AutoCreateDesignationOnImport;
        config.RequireImportPreviewBeforeCommit = req.RequireImportPreviewBeforeCommit;
        config.AllowCrossDeptManager = req.AllowCrossDeptManager;
        config.AllowCrossLocationManager = req.AllowCrossLocationManager;
        config.RequireCostCenterForPayroll = req.RequireCostCenterForPayroll;
        config.RequireGradeForApprovalPolicy = req.RequireGradeForApprovalPolicy;
        config.UpdatedAtUtc = DateTime.UtcNow;

        await _db.SaveChangesAsync(ct);
        return Ok(config);
    }
}

public record TenantHrConfigRequest(
    bool UseDeptHeadApproval = true,
    bool UseHrFinalApproval = true,
    bool UseSupervisorBeforeManager = false,
    bool AllowDottedLineApproval = false,
    bool AutoCreateDeptOnImport = false,
    bool AutoCreateDesignationOnImport = false,
    bool RequireImportPreviewBeforeCommit = true,
    bool AllowCrossDeptManager = true,
    bool AllowCrossLocationManager = true,
    bool RequireCostCenterForPayroll = false,
    bool RequireGradeForApprovalPolicy = false);
