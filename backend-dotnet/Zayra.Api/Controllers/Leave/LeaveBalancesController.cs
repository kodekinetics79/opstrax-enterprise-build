using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Zayra.Api.Application.Common;
using Zayra.Api.Application.Leave;
using Zayra.Api.Data;
using Zayra.Api.Models;

namespace Zayra.Api.Controllers.Leave;

[ApiController]
[Route("api/leave/balances")]
[Authorize]
public class LeaveBalancesController : ControllerBase
{
    private readonly ZayraDbContext _db;
    private readonly ILeaveService _leaveService;

    public LeaveBalancesController(ZayraDbContext db, ILeaveService leaveService)
    {
        _db = db;
        _leaveService = leaveService;
    }

    [HttpGet]
    public async Task<IActionResult> List(
        [FromQuery] int? employeeId,
        [FromQuery] Guid? leaveTypeId,
        [FromQuery] int? year,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 50,
        CancellationToken ct = default)
    {
        var tenantId = this.GetTenantId();
        if (tenantId is null) return Unauthorized();

        year ??= DateTime.UtcNow.Year;
        var query = _db.EmployeeLeaveBalances
            .Where(b => b.TenantId == tenantId && b.Year == year);

        if (employeeId.HasValue) query = query.Where(b => b.EmployeeId == employeeId.Value);
        if (leaveTypeId.HasValue) query = query.Where(b => b.LeaveTypeId == leaveTypeId.Value);

        var total = await query.CountAsync(ct);
        var items = await query
            .OrderBy(b => b.EmployeeName)
            .ThenBy(b => b.LeaveTypeName)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(ct);

        return Ok(new PagedResult<EmployeeLeaveBalance>(items, total, page, pageSize));
    }

    [HttpGet("employee/{employeeId:int}")]
    public async Task<IActionResult> GetByEmployee(int employeeId, [FromQuery] int? year, CancellationToken ct)
    {
        var tenantId = this.GetTenantId();
        if (tenantId is null) return Unauthorized();

        year ??= DateTime.UtcNow.Year;
        var balances = await _db.EmployeeLeaveBalances
            .Where(b => b.TenantId == tenantId && b.EmployeeId == employeeId && b.Year == year)
            .OrderBy(b => b.LeaveTypeName)
            .ToListAsync(ct);

        return Ok(balances);
    }

    [HttpPost("adjust")]
    [Authorize(Roles = "Admin,HR Manager")]
    public async Task<IActionResult> Adjust([FromBody] BalanceAdjustmentRequest req, CancellationToken ct)
    {
        var tenantId = this.GetTenantId();
        if (tenantId is null) return Unauthorized();

        var userId = this.GetUserId();
        var performedByName = User.Identity?.Name ?? userId?.ToString() ?? "HR";

        var leaveTypeExists = await _db.LeaveTypes
            .AnyAsync(t => t.Id == req.LeaveTypeId && t.TenantId == tenantId, ct);
        if (!leaveTypeExists)
            return BadRequest(new { message = "Leave type not found." });

        var year = req.Year ?? DateTime.UtcNow.Year;

        await _leaveService.ApplyLeaveBalanceAsync(
            tenantId.Value,
            req.EmployeeId,
            req.LeaveTypeId,
            req.Days,
            year,
            "Adjustment",
            $"MANUAL-ADJ-{Guid.NewGuid():N}",
            performedByName,
            ct);

        var balance = await _db.EmployeeLeaveBalances
            .FirstOrDefaultAsync(b => b.TenantId == tenantId && b.EmployeeId == req.EmployeeId
                && b.LeaveTypeId == req.LeaveTypeId && b.Year == year, ct);

        return Ok(new { message = "Balance adjusted successfully.", balance });
    }

    [HttpPost("accrue")]
    [Authorize(Roles = "Admin,HR Manager")]
    public async Task<IActionResult> TriggerAccrual(CancellationToken ct)
    {
        var tenantId = this.GetTenantId();
        if (tenantId is null) return Unauthorized();

        await _leaveService.AccrueMonthlyAsync(tenantId.Value, ct);
        return Ok(new { message = "Monthly accrual completed." });
    }
}

public record BalanceAdjustmentRequest(
    int EmployeeId,
    Guid LeaveTypeId,
    decimal Days,
    string Reason,
    int? Year);
