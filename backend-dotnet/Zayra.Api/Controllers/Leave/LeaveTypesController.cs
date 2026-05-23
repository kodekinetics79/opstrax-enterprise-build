using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Zayra.Api.Application.Common;
using Zayra.Api.Data;
using Zayra.Api.Models;

namespace Zayra.Api.Controllers.Leave;

[ApiController]
[Route("api/leave/types")]
[Authorize]
public class LeaveTypesController : ControllerBase
{
    private readonly ZayraDbContext _db;

    public LeaveTypesController(ZayraDbContext db) => _db = db;

    [HttpGet]
    public async Task<IActionResult> List(CancellationToken ct)
    {
        var tenantId = this.GetTenantId();
        if (tenantId is null) return Unauthorized();

        var items = await _db.LeaveTypes
            .Where(t => t.TenantId == tenantId && t.IsActive)
            .OrderBy(t => t.SortOrder)
            .ThenBy(t => t.NameEn)
            .ToListAsync(ct);

        return Ok(items);
    }

    [HttpPost]
    [Authorize(Roles = "Admin,HR Manager")]
    public async Task<IActionResult> Create([FromBody] CreateLeaveTypeRequest req, CancellationToken ct)
    {
        var tenantId = this.GetTenantId();
        if (tenantId is null) return Unauthorized();

        var duplicate = await _db.LeaveTypes
            .AnyAsync(t => t.TenantId == tenantId && t.Code == req.Code, ct);
        if (duplicate)
            return BadRequest(new { message = $"A leave type with code '{req.Code}' already exists." });

        var leaveType = new LeaveType
        {
            TenantId = tenantId.Value,
            Code = req.Code,
            NameEn = req.NameEn,
            NameAr = req.NameAr ?? string.Empty,
            Category = req.Category,
            IsPaid = req.IsPaid,
            IsHalfDayAllowed = req.IsHalfDayAllowed,
            IsHourlyAllowed = req.IsHourlyAllowed,
            RequiresAttachment = req.RequiresAttachment,
            RequiresReason = req.RequiresReason,
            MaxConsecutiveDays = req.MaxConsecutiveDays,
            ColorCode = req.ColorCode ?? "#3B82F6",
            IsActive = true,
            SortOrder = req.SortOrder
        };

        _db.LeaveTypes.Add(leaveType);
        await _db.SaveChangesAsync(ct);
        return Created($"/api/leave/types/{leaveType.Id}", leaveType);
    }

    [HttpPut("{id:guid}")]
    [Authorize(Roles = "Admin,HR Manager")]
    public async Task<IActionResult> Update(Guid id, [FromBody] UpdateLeaveTypeRequest req, CancellationToken ct)
    {
        var tenantId = this.GetTenantId();
        if (tenantId is null) return Unauthorized();

        var leaveType = await _db.LeaveTypes
            .FirstOrDefaultAsync(t => t.Id == id && t.TenantId == tenantId, ct);
        if (leaveType is null) return NotFound();

        if (!string.IsNullOrWhiteSpace(req.NameEn)) leaveType.NameEn = req.NameEn;
        if (req.NameAr is not null) leaveType.NameAr = req.NameAr;
        if (!string.IsNullOrWhiteSpace(req.Category)) leaveType.Category = req.Category;
        if (req.IsPaid.HasValue) leaveType.IsPaid = req.IsPaid.Value;
        if (req.IsHalfDayAllowed.HasValue) leaveType.IsHalfDayAllowed = req.IsHalfDayAllowed.Value;
        if (req.IsHourlyAllowed.HasValue) leaveType.IsHourlyAllowed = req.IsHourlyAllowed.Value;
        if (req.RequiresAttachment.HasValue) leaveType.RequiresAttachment = req.RequiresAttachment.Value;
        if (req.RequiresReason.HasValue) leaveType.RequiresReason = req.RequiresReason.Value;
        if (req.MaxConsecutiveDays.HasValue) leaveType.MaxConsecutiveDays = req.MaxConsecutiveDays.Value;
        if (!string.IsNullOrWhiteSpace(req.ColorCode)) leaveType.ColorCode = req.ColorCode;
        if (req.SortOrder.HasValue) leaveType.SortOrder = req.SortOrder.Value;

        await _db.SaveChangesAsync(ct);
        return Ok(leaveType);
    }

    [HttpDelete("{id:guid}")]
    [Authorize(Roles = "Admin,HR Manager")]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct)
    {
        var tenantId = this.GetTenantId();
        if (tenantId is null) return Unauthorized();

        var leaveType = await _db.LeaveTypes
            .FirstOrDefaultAsync(t => t.Id == id && t.TenantId == tenantId, ct);
        if (leaveType is null) return NotFound();

        leaveType.IsActive = false;
        await _db.SaveChangesAsync(ct);
        return NoContent();
    }
}

public record CreateLeaveTypeRequest(
    string Code,
    string NameEn,
    string? NameAr,
    string Category,
    bool IsPaid,
    bool IsHalfDayAllowed,
    bool IsHourlyAllowed,
    bool RequiresAttachment,
    bool RequiresReason,
    int MaxConsecutiveDays,
    string? ColorCode,
    int SortOrder);

public record UpdateLeaveTypeRequest(
    string? NameEn,
    string? NameAr,
    string? Category,
    bool? IsPaid,
    bool? IsHalfDayAllowed,
    bool? IsHourlyAllowed,
    bool? RequiresAttachment,
    bool? RequiresReason,
    int? MaxConsecutiveDays,
    string? ColorCode,
    int? SortOrder);
