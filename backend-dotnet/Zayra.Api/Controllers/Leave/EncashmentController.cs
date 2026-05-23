using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Zayra.Api.Application.Common;
using Zayra.Api.Data;
using Zayra.Api.Models;

namespace Zayra.Api.Controllers.Leave;

[ApiController]
[Route("api/leave/encashment")]
[Authorize]
public class EncashmentController : ControllerBase
{
    private readonly ZayraDbContext _db;

    public EncashmentController(ZayraDbContext db) => _db = db;

    [HttpGet]
    public async Task<IActionResult> List(
        [FromQuery] string? status,
        [FromQuery] int? employeeId,
        [FromQuery] int? year,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 25,
        CancellationToken ct = default)
    {
        var tenantId = this.GetTenantId();
        if (tenantId is null) return Unauthorized();

        var query = _db.LeaveEncashmentRequests.Where(e => e.TenantId == tenantId);
        if (!string.IsNullOrWhiteSpace(status)) query = query.Where(e => e.Status == status);
        if (employeeId.HasValue) query = query.Where(e => e.EmployeeId == employeeId.Value);
        if (year.HasValue) query = query.Where(e => e.Year == year.Value);

        var total = await query.CountAsync(ct);
        var items = await query
            .OrderByDescending(e => e.CreatedAtUtc)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(ct);

        return Ok(new PagedResult<LeaveEncashmentRequest>(items, total, page, pageSize));
    }

    [HttpPost]
    public async Task<IActionResult> Create([FromBody] CreateEncashmentRequest req, CancellationToken ct)
    {
        var tenantId = this.GetTenantId();
        if (tenantId is null) return Unauthorized();

        var employee = await _db.Employees
            .FirstOrDefaultAsync(e => e.Id == req.EmployeeId && e.TenantId == tenantId, ct);
        if (employee is null)
            return BadRequest(new { message = "Employee not found." });

        var leaveType = await _db.LeaveTypes
            .FirstOrDefaultAsync(t => t.Id == req.LeaveTypeId && t.TenantId == tenantId, ct);
        if (leaveType is null)
            return BadRequest(new { message = "Leave type not found." });

        var policy = await _db.LeavePolicies
            .FirstOrDefaultAsync(p => p.TenantId == tenantId && p.LeaveTypeId == req.LeaveTypeId && p.Status == "Active", ct);
        if (policy is null || !policy.EncashmentAllowed)
            return BadRequest(new { message = "Encashment is not allowed for this leave type." });

        var year = req.Year ?? DateTime.UtcNow.Year;
        var balance = await _db.EmployeeLeaveBalances
            .FirstOrDefaultAsync(b => b.TenantId == tenantId && b.EmployeeId == req.EmployeeId
                && b.LeaveTypeId == req.LeaveTypeId && b.Year == year, ct);

        if (balance is null || balance.Available < req.DaysToEncash)
            return BadRequest(new { message = "Insufficient leave balance for encashment." });

        if (req.DaysToEncash > policy.EncashmentMaxDays)
            return BadRequest(new { message = $"Cannot encash more than {policy.EncashmentMaxDays} days per request." });

        var encashmentRequest = new LeaveEncashmentRequest
        {
            TenantId = tenantId.Value,
            EmployeeId = req.EmployeeId,
            EmployeeName = employee.FullName,
            LeaveTypeId = req.LeaveTypeId,
            LeaveTypeName = leaveType.NameEn,
            Year = year,
            DaysToEncash = req.DaysToEncash,
            AmountPerDay = req.AmountPerDay,
            TotalAmount = req.DaysToEncash * req.AmountPerDay,
            Reason = req.Reason ?? string.Empty,
            Status = "Pending"
        };

        _db.LeaveEncashmentRequests.Add(encashmentRequest);
        await _db.SaveChangesAsync(ct);
        return Created($"/api/leave/encashment/{encashmentRequest.Id}", encashmentRequest);
    }

    [HttpPost("{id:guid}/hr-approve")]
    [Authorize(Roles = "HR Manager,Admin")]
    public async Task<IActionResult> HRApprove(Guid id, [FromBody] EncashmentDecisionRequest req, CancellationToken ct)
    {
        var tenantId = this.GetTenantId();
        if (tenantId is null) return Unauthorized();

        var encashment = await _db.LeaveEncashmentRequests
            .FirstOrDefaultAsync(e => e.Id == id && e.TenantId == tenantId, ct);
        if (encashment is null) return NotFound();

        if (encashment.Status != "Pending")
            return BadRequest(new { message = "Only pending requests can be HR-approved." });

        encashment.Status = "HRApproved";
        encashment.HRNotes = req.Notes ?? string.Empty;
        await _db.SaveChangesAsync(ct);
        return Ok(encashment);
    }

    [HttpPost("{id:guid}/payroll-approve")]
    [Authorize(Roles = "Admin")]
    public async Task<IActionResult> PayrollApprove(Guid id, [FromBody] EncashmentDecisionRequest req, CancellationToken ct)
    {
        var tenantId = this.GetTenantId();
        if (tenantId is null) return Unauthorized();

        var encashment = await _db.LeaveEncashmentRequests
            .FirstOrDefaultAsync(e => e.Id == id && e.TenantId == tenantId, ct);
        if (encashment is null) return NotFound();

        if (encashment.Status != "HRApproved")
            return BadRequest(new { message = "Only HR-approved requests can be payroll-approved." });

        encashment.Status = "PayrollApproved";
        encashment.PayrollNotes = req.Notes ?? string.Empty;

        var balance = await _db.EmployeeLeaveBalances
            .FirstOrDefaultAsync(b => b.TenantId == tenantId && b.EmployeeId == encashment.EmployeeId
                && b.LeaveTypeId == encashment.LeaveTypeId && b.Year == encashment.Year, ct);

        if (balance is not null)
        {
            balance.Encashed += encashment.DaysToEncash;
            balance.UpdatedAtUtc = DateTime.UtcNow;

            var txn = new LeaveBalanceTransaction
            {
                TenantId = tenantId.Value,
                EmployeeId = encashment.EmployeeId,
                LeaveTypeId = encashment.LeaveTypeId,
                Year = encashment.Year,
                TransactionType = "Encashed",
                Amount = encashment.DaysToEncash,
                BalanceBefore = balance.Available + encashment.DaysToEncash,
                BalanceAfter = balance.Available,
                Reference = encashment.Id.ToString(),
                Reason = "Encashment approved",
                PerformedByName = User.Identity?.Name ?? "Payroll"
            };
            _db.LeaveBalanceTransactions.Add(txn);
        }

        encashment.ProcessedAtUtc = DateTime.UtcNow;
        await _db.SaveChangesAsync(ct);
        return Ok(encashment);
    }

    [HttpPost("{id:guid}/reject")]
    [Authorize(Roles = "HR Manager,Admin")]
    public async Task<IActionResult> Reject(Guid id, [FromBody] EncashmentDecisionRequest req, CancellationToken ct)
    {
        var tenantId = this.GetTenantId();
        if (tenantId is null) return Unauthorized();

        var encashment = await _db.LeaveEncashmentRequests
            .FirstOrDefaultAsync(e => e.Id == id && e.TenantId == tenantId, ct);
        if (encashment is null) return NotFound();

        if (encashment.Status == "Processed" || encashment.Status == "Rejected")
            return BadRequest(new { message = "Cannot reject a processed or already-rejected request." });

        encashment.Status = "Rejected";
        encashment.HRNotes = req.Notes ?? string.Empty;
        await _db.SaveChangesAsync(ct);
        return Ok(encashment);
    }
}

public record CreateEncashmentRequest(
    int EmployeeId,
    Guid LeaveTypeId,
    decimal DaysToEncash,
    decimal AmountPerDay,
    string? Reason,
    int? Year);

public record EncashmentDecisionRequest(string? Notes);
