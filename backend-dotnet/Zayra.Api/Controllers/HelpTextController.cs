using System.ComponentModel.DataAnnotations;
using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Zayra.Api.Data;
using Zayra.Api.Models;

namespace Zayra.Api.Controllers;

/// <summary>
/// Tenant-customizable field help text: every signed-in user reads the overrides
/// so InfoTips show company-specific wording; only tenant Admins can edit them.
/// </summary>
[ApiController]
[Route("api/help-texts")]
[Authorize]
public class HelpTextController : ControllerBase
{
    private readonly ZayraDbContext _db;

    public HelpTextController(ZayraDbContext db)
    {
        _db = db;
    }

    [HttpGet]
    public async Task<IActionResult> List(CancellationToken ct)
    {
        var tenantId = RequireTenant();
        var items = await _db.TenantFieldHelpTexts.AsNoTracking()
            .Where(x => x.TenantId == tenantId)
            .OrderBy(x => x.FieldKey)
            .Select(x => new { x.FieldKey, x.Text, x.UpdatedAtUtc })
            .ToListAsync(ct);
        return Ok(items);
    }

    [HttpPut("{fieldKey}")]
    [Authorize(Roles = "Admin")]
    public async Task<IActionResult> Upsert(string fieldKey, [FromBody] UpsertHelpTextRequest req, CancellationToken ct)
    {
        fieldKey = fieldKey.Trim().ToLowerInvariant();
        if (fieldKey.Length is < 2 or > 120) return BadRequest(new { message = "Field key must be 2-120 characters." });
        if (string.IsNullOrWhiteSpace(req.Text) || req.Text.Length > 500)
            return BadRequest(new { message = "Help text is required (max 500 characters)." });

        var tenantId = RequireTenant();
        var row = await _db.TenantFieldHelpTexts.FirstOrDefaultAsync(x => x.TenantId == tenantId && x.FieldKey == fieldKey, ct);
        if (row is null)
        {
            row = new TenantFieldHelpText { TenantId = tenantId, FieldKey = fieldKey };
            _db.TenantFieldHelpTexts.Add(row);
        }
        row.Text = req.Text.Trim();
        row.UpdatedAtUtc = DateTime.UtcNow;
        row.UpdatedBy = GetUserId();
        await _db.SaveChangesAsync(ct);
        return Ok(new { row.FieldKey, row.Text, row.UpdatedAtUtc });
    }

    [HttpDelete("{fieldKey}")]
    [Authorize(Roles = "Admin")]
    public async Task<IActionResult> Delete(string fieldKey, CancellationToken ct)
    {
        var tenantId = RequireTenant();
        fieldKey = fieldKey.Trim().ToLowerInvariant();
        var row = await _db.TenantFieldHelpTexts.FirstOrDefaultAsync(x => x.TenantId == tenantId && x.FieldKey == fieldKey, ct);
        if (row is null) return NotFound();
        _db.TenantFieldHelpTexts.Remove(row);
        await _db.SaveChangesAsync(ct);
        return NoContent();
    }

    private Guid RequireTenant()
    {
        var value = User.FindFirstValue("tenant_id");
        return Guid.TryParse(value, out var tenantId) ? tenantId : throw new UnauthorizedAccessException("Tenant claim missing.");
    }

    private Guid? GetUserId() =>
        Guid.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier) ?? User.FindFirstValue("sub"), out var id) ? id : null;
}

public record UpsertHelpTextRequest([Required] string Text);
