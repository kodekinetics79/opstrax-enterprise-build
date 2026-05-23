using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Zayra.Api.Data;

namespace Zayra.Api.Controllers;

[ApiController]
[Route("api/audit-logs")]
[Authorize(Roles = "Admin")]
public class AuditLogsController : ControllerBase
{
    private readonly ZayraDbContext _db;

    public AuditLogsController(ZayraDbContext db)
    {
        _db = db;
    }

    [HttpGet]
    public async Task<IActionResult> Recent([FromQuery] int limit = 100, CancellationToken cancellationToken = default)
    {
        var tenantId = GetTenantId();
        if (tenantId is null) return Unauthorized();
        limit = Math.Clamp(limit, 1, 500);
        var logs = await _db.AuditLogs
            .Where(x => x.TenantId == tenantId)
            .OrderByDescending(x => x.CreatedAtUtc)
            .Take(limit)
            .ToListAsync(cancellationToken);
        return Ok(logs);
    }

    private Guid? GetTenantId()
    {
        var value = User.FindFirstValue("tenant_id");
        return Guid.TryParse(value, out var id) ? id : null;
    }
}
