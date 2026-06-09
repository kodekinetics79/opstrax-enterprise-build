using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Zayra.Api.Application.Common;
using Zayra.Api.Data;
using Zayra.Api.Models;

namespace Zayra.Api.Controllers.Leave;

[ApiController]
[Route("api/leave/compoff")]
[Authorize]
public class CompOffController : ControllerBase
{
    private readonly ZayraDbContext _db;
    private readonly IDataScopeService _scopeService;

    public CompOffController(ZayraDbContext db, IDataScopeService scopeService)
    {
        _db = db;
        _scopeService = scopeService;
    }

    [HttpGet]
    public async Task<IActionResult> List(
        [FromQuery] int? employeeId,
        [FromQuery] string? status,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 25,
        CancellationToken ct = default)
    {
        var tenantId = this.GetTenantId();
        if (tenantId is null) return Unauthorized();

        var scope = await _scopeService.ResolveAsync(User, tenantId.Value, ct);

        var query = _db.CompOffCredits.Where(c => c.TenantId == tenantId);
        if (!scope.IsUnrestricted)
            query = query.Where(c => scope.AllowedEmployeeIds!.Contains(c.EmployeeId));
        if (employeeId.HasValue) query = query.Where(c => c.EmployeeId == employeeId.Value);
        if (!string.IsNullOrWhiteSpace(status)) query = query.Where(c => c.Status == status);

        var total = await query.CountAsync(ct);
        var items = await query
            .OrderByDescending(c => c.WorkedDate)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(ct);

        return Ok(new PagedResult<CompOffCredit>(items, total, page, pageSize));
    }

    [HttpPost]
    public async Task<IActionResult> Create([FromBody] CreateCompOffRequest req, CancellationToken ct)
    {
        var tenantId = this.GetTenantId();
        if (tenantId is null) return Unauthorized();

        var employee = await _db.Employees
            .FirstOrDefaultAsync(e => e.Id == req.EmployeeId && e.TenantId == tenantId, ct);
        if (employee is null)
            return BadRequest(new { message = "Employee not found." });

        var credit = new CompOffCredit
        {
            TenantId = tenantId.Value,
            EmployeeId = req.EmployeeId,
            EmployeeName = employee.FullName,
            WorkedDate = req.WorkedDate,
            WorkType = req.WorkType ?? "Overtime",
            HoursWorked = req.HoursWorked,
            DaysEarned = req.DaysEarned,
            ExpiryDate = req.ExpiryDate,
            Status = "Pending"
        };

        _db.CompOffCredits.Add(credit);
        await _db.SaveChangesAsync(ct);
        return Created($"/api/leave/compoff/{credit.Id}", credit);
    }

    [HttpPost("{id:guid}/approve")]
    [Authorize(Roles = "Manager,Admin")]
    public async Task<IActionResult> Approve(Guid id, [FromBody] CompOffApproveRequest req, CancellationToken ct)
    {
        var tenantId = this.GetTenantId();
        if (tenantId is null) return Unauthorized();

        var credit = await _db.CompOffCredits
            .FirstOrDefaultAsync(c => c.Id == id && c.TenantId == tenantId, ct);
        if (credit is null) return NotFound();

        if (credit.Status != "Pending")
            return BadRequest(new { message = "Only pending comp-off requests can be approved." });

        credit.Status = "Approved";
        credit.ManagerApprovalNotes = req.Notes ?? string.Empty;
        credit.ApprovedByName = User.Identity?.Name ?? this.GetUserId()?.ToString() ?? "Manager";
        credit.ApprovedAtUtc = DateTime.UtcNow;

        await _db.SaveChangesAsync(ct);
        return Ok(credit);
    }

    [HttpPost("{id:guid}/use")]
    public async Task<IActionResult> Use(Guid id, [FromBody] UseCompOffRequest req, CancellationToken ct)
    {
        var tenantId = this.GetTenantId();
        if (tenantId is null) return Unauthorized();

        var credit = await _db.CompOffCredits
            .FirstOrDefaultAsync(c => c.Id == id && c.TenantId == tenantId, ct);
        if (credit is null) return NotFound();

        if (credit.Status != "Approved")
            return BadRequest(new { message = "Only approved comp-off credits can be used." });

        var alreadyUsed = await _db.CompOffUsages
            .Where(u => u.TenantId == tenantId && u.CompOffCreditId == id)
            .SumAsync(u => u.DaysUsed, ct);

        var remaining = credit.DaysEarned - alreadyUsed;
        if (req.DaysToUse > remaining)
            return BadRequest(new { message = $"Only {remaining} comp-off days remaining." });

        var usage = new CompOffUsage
        {
            TenantId = tenantId.Value,
            EmployeeId = credit.EmployeeId,
            CompOffCreditId = id,
            LeaveRequestId = req.LeaveRequestId,
            DaysUsed = req.DaysToUse
        };
        _db.CompOffUsages.Add(usage);

        if (remaining - req.DaysToUse <= 0)
            credit.Status = "Used";

        await _db.SaveChangesAsync(ct);
        return Ok(usage);
    }
}

public record CreateCompOffRequest(
    int EmployeeId,
    DateOnly WorkedDate,
    string? WorkType,
    decimal HoursWorked,
    decimal DaysEarned,
    DateOnly? ExpiryDate);

public record CompOffApproveRequest(string? Notes);
public record UseCompOffRequest(decimal DaysToUse, Guid? LeaveRequestId);
