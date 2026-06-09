using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Zayra.Api.Data;

namespace Zayra.Api.Controllers;

[ApiController]
[Route("api/notifications")]
[Authorize]
public class NotificationsController : ControllerBase
{
    private readonly ZayraDbContext _db;

    public NotificationsController(ZayraDbContext db)
    {
        _db = db;
    }

    [HttpGet]
    public async Task<IActionResult> Recent(CancellationToken cancellationToken)
    {
        var tenantId = Guid.Parse(User.FindFirstValue("tenant_id")!);
        var userId = Guid.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier) ?? User.FindFirstValue("sub"), out var parsed) ? parsed : (Guid?)null;
        var items = await _db.Notifications
            .Where(x => x.TenantId == tenantId && (x.UserId == null || x.UserId == userId))
            .OrderByDescending(x => x.CreatedAtUtc)
            .Take(50)
            .ToListAsync(cancellationToken);
        return Ok(items);
    }

    [HttpPost("{id:guid}/read")]
    public async Task<IActionResult> MarkRead(Guid id, CancellationToken cancellationToken)
    {
        var tenantId = Guid.Parse(User.FindFirstValue("tenant_id")!);
        var notification = await _db.Notifications.FirstOrDefaultAsync(x => x.Id == id && x.TenantId == tenantId, cancellationToken);
        if (notification is null) return NotFound();
        notification.Status = "Read";
        notification.ReadAtUtc = DateTime.UtcNow;
        await _db.SaveChangesAsync(cancellationToken);
        return NoContent();
    }

    [HttpPost("read-all")]
    public async Task<IActionResult> MarkAllRead(CancellationToken cancellationToken)
    {
        var tenantId = Guid.Parse(User.FindFirstValue("tenant_id")!);
        var userId = Guid.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier) ?? User.FindFirstValue("sub"), out var parsed) ? parsed : (Guid?)null;
        await _db.Notifications
            .Where(x => x.TenantId == tenantId && (x.UserId == null || x.UserId == userId) && x.Status == "Unread")
            .ExecuteUpdateAsync(s => s
                .SetProperty(n => n.Status, "Read")
                .SetProperty(n => n.ReadAtUtc, DateTime.UtcNow),
            cancellationToken);
        return NoContent();
    }

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Dismiss(Guid id, CancellationToken cancellationToken)
    {
        var tenantId = Guid.Parse(User.FindFirstValue("tenant_id")!);
        var notification = await _db.Notifications.FirstOrDefaultAsync(x => x.Id == id && x.TenantId == tenantId, cancellationToken);
        if (notification is null) return NotFound();
        _db.Notifications.Remove(notification);
        await _db.SaveChangesAsync(cancellationToken);
        return NoContent();
    }
}
