using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Zayra.Api.Application.Common;
using Zayra.Api.Data;

namespace Zayra.Api.Controllers.Leave;

[ApiController]
[Route("api/leave/reports")]
[Authorize]
public class LeaveReportsController : ControllerBase
{
    private readonly ZayraDbContext _db;

    public LeaveReportsController(ZayraDbContext db) => _db = db;

    [HttpGet("balance-summary")]
    public async Task<IActionResult> BalanceSummary([FromQuery] int? year, CancellationToken ct)
    {
        var tenantId = this.GetTenantId();
        if (tenantId is null) return Unauthorized();

        year ??= DateTime.UtcNow.Year;

        var balances = await _db.EmployeeLeaveBalances
            .Where(b => b.TenantId == tenantId && b.Year == year)
            .OrderBy(b => b.EmployeeName)
            .ThenBy(b => b.LeaveTypeName)
            .ToListAsync(ct);

        var summary = balances.Select(b => new
        {
            b.EmployeeId,
            b.EmployeeName,
            b.LeaveTypeName,
            b.Year,
            b.Entitled,
            b.Accrued,
            b.Used,
            b.Pending,
            b.CarriedForward,
            b.Encashed,
            b.Expired,
            b.ManualAdjustment,
            b.Available
        });

        return Ok(summary);
    }

    [HttpGet("usage")]
    public async Task<IActionResult> Usage(
        [FromQuery] DateOnly? fromDate,
        [FromQuery] DateOnly? toDate,
        [FromQuery] string? departmentName,
        CancellationToken ct = default)
    {
        var tenantId = this.GetTenantId();
        if (tenantId is null) return Unauthorized();

        var from = fromDate ?? DateOnly.FromDateTime(new DateTime(DateTime.UtcNow.Year, 1, 1));
        var to = toDate ?? DateOnly.FromDateTime(DateTime.UtcNow);

        var query = _db.LeaveRequests
            .Where(r => r.TenantId == tenantId
                && r.Status == "Approved"
                && r.StartDate >= from
                && r.StartDate <= to);

        if (!string.IsNullOrWhiteSpace(departmentName))
            query = query.Where(r => r.DepartmentName == departmentName);

        var requests = await query.ToListAsync(ct);

        var byDepartmentAndType = requests
            .GroupBy(r => new { r.DepartmentName, r.LeaveTypeName })
            .Select(g => new
            {
                g.Key.DepartmentName,
                g.Key.LeaveTypeName,
                RequestCount = g.Count(),
                TotalDays = g.Sum(r => r.TotalDays)
            })
            .OrderBy(x => x.DepartmentName)
            .ThenBy(x => x.LeaveTypeName);

        return Ok(new { from, to, usage = byDepartmentAndType });
    }

    [HttpGet("on-leave-today")]
    public async Task<IActionResult> OnLeaveToday(CancellationToken ct)
    {
        var tenantId = this.GetTenantId();
        if (tenantId is null) return Unauthorized();

        var today = DateOnly.FromDateTime(DateTime.UtcNow);

        var onLeave = await _db.LeaveRequests
            .Where(r => r.TenantId == tenantId
                && r.Status == "Approved"
                && r.StartDate <= today
                && r.EndDate >= today)
            .OrderBy(r => r.DepartmentName)
            .ThenBy(r => r.EmployeeName)
            .Select(r => new
            {
                r.EmployeeId,
                r.EmployeeName,
                r.DepartmentName,
                r.LeaveTypeName,
                r.StartDate,
                r.EndDate
            })
            .ToListAsync(ct);

        return Ok(new { date = today, count = onLeave.Count, employees = onLeave });
    }

    [HttpGet("pending-approvals")]
    public async Task<IActionResult> PendingApprovals(CancellationToken ct)
    {
        var tenantId = this.GetTenantId();
        if (tenantId is null) return Unauthorized();

        var pendingStatuses = new[] { "Submitted", "PendingManagerApproval", "PendingHRApproval" };

        var pending = await _db.LeaveRequests
            .Where(r => r.TenantId == tenantId && pendingStatuses.Contains(r.Status))
            .OrderBy(r => r.SubmittedAtUtc)
            .Select(r => new
            {
                r.Id,
                r.EmployeeId,
                r.EmployeeName,
                r.DepartmentName,
                r.LeaveTypeName,
                r.StartDate,
                r.EndDate,
                Days = r.TotalDays,
                r.Status,
                r.SubmittedAtUtc
            })
            .ToListAsync(ct);

        var byStatus = pending
            .GroupBy(r => r.Status)
            .Select(g => new { Status = g.Key, Count = g.Count() });

        return Ok(new { total = pending.Count, byStatus, requests = pending });
    }

    [HttpGet("sick-leave-trend")]
    public async Task<IActionResult> SickLeaveTrend(CancellationToken ct)
    {
        var tenantId = this.GetTenantId();
        if (tenantId is null) return Unauthorized();

        var sickLeaveTypeIds = await _db.LeaveTypes
            .Where(t => t.TenantId == tenantId && (t.Category == "Sick" || t.NameEn.Contains("Sick")))
            .Select(t => t.Id)
            .ToListAsync(ct);

        var twelveMonthsAgo = DateOnly.FromDateTime(DateTime.UtcNow.AddMonths(-12));
        var today = DateOnly.FromDateTime(DateTime.UtcNow);

        var sickLeave = await _db.LeaveRequests
            .Where(r => r.TenantId == tenantId
                && sickLeaveTypeIds.Contains(r.LeaveTypeId)
                && r.Status == "Approved"
                && r.StartDate >= twelveMonthsAgo
                && r.StartDate <= today)
            .ToListAsync(ct);

        var byMonth = sickLeave
            .GroupBy(r => new { r.StartDate.Year, r.StartDate.Month })
            .Select(g => new
            {
                Year = g.Key.Year,
                Month = g.Key.Month,
                MonthName = new DateTime(g.Key.Year, g.Key.Month, 1).ToString("MMM yyyy"),
                RequestCount = g.Count(),
                TotalDays = g.Sum(r => r.TotalDays)
            })
            .OrderBy(x => x.Year)
            .ThenBy(x => x.Month);

        return Ok(byMonth);
    }

    [HttpGet("encashment-summary")]
    public async Task<IActionResult> EncashmentSummary([FromQuery] int? year, CancellationToken ct)
    {
        var tenantId = this.GetTenantId();
        if (tenantId is null) return Unauthorized();

        year ??= DateTime.UtcNow.Year;

        var encashments = await _db.LeaveEncashmentRequests
            .Where(e => e.TenantId == tenantId && e.Year == year)
            .ToListAsync(ct);

        var byType = encashments
            .GroupBy(e => e.LeaveTypeName)
            .Select(g => new
            {
                LeaveTypeName = g.Key,
                RequestCount = g.Count(),
                TotalDays = g.Sum(e => e.DaysToEncash),
                TotalAmount = g.Sum(e => e.TotalAmount),
                ProcessedCount = g.Count(e => e.Status == "Processed"),
                PendingCount = g.Count(e => e.Status == "Pending" || e.Status == "HRApproved")
            });

        return Ok(new { year, summary = byType, grandTotalAmount = encashments.Where(e => e.Status == "Processed").Sum(e => e.TotalAmount) });
    }

    [HttpGet("liability")]
    public async Task<IActionResult> Liability([FromQuery] int? year, CancellationToken ct)
    {
        var tenantId = this.GetTenantId();
        if (tenantId is null) return Unauthorized();

        year ??= DateTime.UtcNow.Year;

        var balances = (await _db.EmployeeLeaveBalances
            .Where(b => b.TenantId == tenantId && b.Year == year)
            .ToListAsync(ct))
            .Where(b => b.Available > 0)
            .ToList();

        var employeeIds = balances.Select(b => b.EmployeeId).Distinct().ToList();
        var employees = await _db.Employees
            .Where(e => employeeIds.Contains(e.Id) && e.TenantId == tenantId)
            .ToDictionaryAsync(e => e.Id, e => e.Salary ?? 0m, ct);

        var liability = balances.Select(b =>
        {
            var monthlySalary = employees.TryGetValue(b.EmployeeId, out var salary) ? salary : 0m;
            var dailyRate = monthlySalary > 0 ? monthlySalary / 30 : 0m;
            var liabilityAmount = b.Available * dailyRate;
            return new
            {
                b.EmployeeId,
                b.EmployeeName,
                b.LeaveTypeName,
                b.Available,
                DailyRate = dailyRate,
                LiabilityAmount = liabilityAmount
            };
        }).OrderByDescending(x => x.LiabilityAmount);

        return Ok(new
        {
            year,
            totalLiability = liability.Sum(x => x.LiabilityAmount),
            details = liability
        });
    }

    [HttpGet("blackout-dates")]
    public async Task<IActionResult> BlackoutDates(
        [FromQuery] DateOnly? fromDate,
        [FromQuery] DateOnly? toDate,
        CancellationToken ct = default)
    {
        var tenantId = this.GetTenantId();
        if (tenantId is null) return Unauthorized();

        var from = fromDate ?? DateOnly.FromDateTime(DateTime.UtcNow);
        var to = toDate ?? from.AddMonths(3);

        var blackouts = await _db.LeaveBlackoutDates
            .Where(b => b.TenantId == tenantId && b.StartDate <= to && b.EndDate >= from)
            .OrderBy(b => b.StartDate)
            .ToListAsync(ct);

        return Ok(blackouts);
    }
}
