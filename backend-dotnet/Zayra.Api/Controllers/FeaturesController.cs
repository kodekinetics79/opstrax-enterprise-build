using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Zayra.Api.Application.Common;
using Zayra.Api.Data;

namespace Zayra.Api.Controllers;

/// <summary>
/// Read-only feature visibility endpoint — accessible to any authenticated tenant user.
/// Returns the set of feature keys that have been explicitly disabled for the tenant.
/// An absent key means the feature is enabled (default state for new tenants with no flags).
/// Writes are not permitted here; flag management remains in TenantAdminController.
/// </summary>
[ApiController]
[Route("api/[controller]")]
[Authorize]
public class FeaturesController : ControllerBase
{
    private readonly ZayraDbContext _db;

    public FeaturesController(ZayraDbContext db) => _db = db;

    [HttpGet("disabled-keys")]
    public async Task<IActionResult> GetDisabledKeys(CancellationToken ct)
    {
        var tenantId = this.GetTenantId();
        if (tenantId is null) return Unauthorized();

        var disabledKeys = await _db.TenantFeatureFlags
            .Where(f => f.TenantId == tenantId && !f.IsEnabled)
            .Select(f => f.FeatureKey)
            .ToListAsync(ct);

        return Ok(disabledKeys);
    }
}
