using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Zayra.Api.Application.Common;
using Zayra.Api.Data;

namespace Zayra.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize]
public class DashboardController : ControllerBase
{
    private readonly ZayraDbContext _db;

    public DashboardController(ZayraDbContext db)
    {
        _db = db;
    }

    [HttpGet("summary")]
    public async Task<IActionResult> Summary(CancellationToken cancellationToken)
    {
        var tenantId = this.GetTenantId();
        if (tenantId is null) return Ok(new DashboardSummaryDto(0, 0, 0, 0, 0, 0m, 0));

        var today = DateOnly.FromDateTime(DateTime.UtcNow.Date);
        var monthStart = new DateOnly(today.Year, today.Month, 1);

        var employees = _db.Employees.Where(e => e.TenantId == tenantId);
        var attendance = _db.AttendanceRecords.Where(a => a.TenantId == tenantId);

        var totalEmployees = await employees.CountAsync(cancellationToken);
        var activeEmployees = await employees.CountAsync(e => e.Status == "Active", cancellationToken);
        var todayAttendance = attendance.Where(a => a.WorkDate == today);

        var presentToday = await todayAttendance.CountAsync(a => a.Status == "Present", cancellationToken);
        var onLeave = await todayAttendance.CountAsync(a => a.Status == "Leave" || a.Status == "On Leave", cancellationToken);
        var absent = await todayAttendance.CountAsync(a => a.Status == "Absent", cancellationToken);
        var overtimeHours = await attendance
            .Where(a => a.WorkDate >= monthStart && a.WorkDate <= today)
            .SumAsync(a => (decimal?)a.OvertimeHours, cancellationToken) ?? 0m;

        var churnRisk = await attendance
            .Where(a => a.WorkDate >= today.AddDays(-30) && (a.Status == "Absent" || a.OvertimeHours >= 4))
            .Select(a => a.EmployeeId)
            .Distinct()
            .CountAsync(cancellationToken);

        return Ok(new DashboardSummaryDto(
            totalEmployees,
            activeEmployees,
            presentToday,
            onLeave,
            absent,
            overtimeHours,
            churnRisk));
    }

    [HttpGet("trends")]
    public async Task<IActionResult> Trends([FromQuery] int months = 6, CancellationToken cancellationToken = default)
    {
        var tenantId = this.GetTenantId();
        months = Math.Clamp(months, 1, 12);

        var today = DateOnly.FromDateTime(DateTime.UtcNow.Date);
        var currentMonth = new DateOnly(today.Year, today.Month, 1);
        var firstMonth = currentMonth.AddMonths(-(months - 1));

        if (tenantId is null) return Ok(Array.Empty<DashboardTrendDto>());

        var records = await _db.AttendanceRecords
            .Where(a => a.TenantId == tenantId && a.WorkDate >= firstMonth && a.WorkDate <= today)
            .Select(a => new
            {
                a.WorkDate,
                a.Status,
                a.OvertimeHours
            })
            .ToListAsync(cancellationToken);

        var trends = Enumerable.Range(0, months)
            .Select(offset =>
            {
                var month = firstMonth.AddMonths(offset);
                var monthRecords = records
                    .Where(a => a.WorkDate.Year == month.Year && a.WorkDate.Month == month.Month)
                    .ToList();
                var attendanceRate = monthRecords.Count == 0
                    ? 0m
                    : Math.Round(monthRecords.Count(a => a.Status == "Present") * 100m / monthRecords.Count, 1);

                return new DashboardTrendDto(
                    month.ToString("MMM"),
                    attendanceRate,
                    monthRecords.Sum(a => a.OvertimeHours));
            })
            .ToList();

        return Ok(trends);
    }

    /// <summary>
    /// Consolidated, tenant-scoped operational data for the dashboard's
    /// approval, payroll, workforce-mix, alerts and self-service panels.
    /// Replaces the former static demo dataset on the frontend.
    /// </summary>
    [HttpGet("overview")]
    public async Task<IActionResult> Overview(CancellationToken cancellationToken)
    {
        var tenantId = this.GetTenantId();
        if (tenantId is null)
        {
            return Ok(new DashboardOverviewDto(
                0, Array.Empty<ApprovalQueueItemDto>(), null,
                Array.Empty<NamedValueDto>(), Array.Empty<NamedValueDto>(),
                Array.Empty<NamedValueDto>(),
                Array.Empty<DashboardAlertDto>(), 0, 0));
        }

        var today = DateOnly.FromDateTime(DateTime.UtcNow.Date);
        var monthStart = new DateOnly(today.Year, today.Month, 1);

        // --- Approvals ---
        var pendingApprovalsQuery = _db.ApprovalRequests
            .Where(a => a.TenantId == tenantId && a.Status == "Pending");
        var pendingApprovals = await pendingApprovalsQuery.CountAsync(cancellationToken);
        var approvalQueue = await pendingApprovalsQuery
            .OrderByDescending(a => a.CreatedAtUtc)
            .Take(6)
            .Select(a => new ApprovalQueueItemDto(
                a.Id,
                string.IsNullOrWhiteSpace(a.Title) ? a.EntityName : a.Title,
                a.EntityName,
                a.CreatedAtUtc))
            .ToListAsync(cancellationToken);

        // --- Payroll (latest run) ---
        var latestRun = await _db.PayrollRuns
            .Where(p => p.TenantId == tenantId)
            .OrderByDescending(p => p.Year).ThenByDescending(p => p.Month)
            .FirstOrDefaultAsync(cancellationToken);

        PayrollSummaryDto? payrollSummary = null;
        IReadOnlyList<NamedValueDto> payrollByEntity = Array.Empty<NamedValueDto>();
        if (latestRun is not null)
        {
            var periodLabel = new DateOnly(latestRun.Year, latestRun.Month, 1).ToString("MMM yyyy");
            payrollSummary = new PayrollSummaryDto(
                periodLabel,
                latestRun.TotalGrossSalary,
                latestRun.TotalNetSalary,
                latestRun.TotalDeductions,
                latestRun.EmployeeCount,
                latestRun.Status);

            var rawPayroll = await _db.PayrollSlips
                .Where(s => s.TenantId == tenantId && s.RunId == latestRun.Id)
                .GroupBy(s => s.Department)
                .Select(g => new { Key = g.Key, Total = g.Sum(x => x.NetSalary) })
                .OrderByDescending(x => x.Total)
                .Take(8)
                .ToListAsync(cancellationToken);
            payrollByEntity = rawPayroll
                .Select(x => new NamedValueDto(string.IsNullOrWhiteSpace(x.Key) ? "Unspecified" : x.Key, x.Total))
                .ToList();
        }

        // --- Workforce mix (active employees by employment type) ---
        var rawMix = await _db.Employees
            .Where(e => e.TenantId == tenantId && e.Status == "Active")
            .GroupBy(e => e.EmploymentType)
            .Select(g => new { Key = g.Key, Count = g.Count() })
            .OrderByDescending(x => x.Count)
            .ToListAsync(cancellationToken);
        var workforceMix = rawMix
            .Select(x => new NamedValueDto(string.IsNullOrWhiteSpace(x.Key) ? "Unspecified" : x.Key, x.Count))
            .ToList();

        // --- Headcount by department (active employees) ---
        var rawDept = await _db.Employees
            .Where(e => e.TenantId == tenantId && e.Status == "Active")
            .GroupBy(e => e.Department)
            .Select(g => new { Key = g.Key, Count = g.Count() })
            .OrderByDescending(x => x.Count)
            .Take(6)
            .ToListAsync(cancellationToken);
        var headcountByDepartment = rawDept
            .Select(x => new NamedValueDto(string.IsNullOrWhiteSpace(x.Key) ? "Unassigned" : x.Key, x.Count))
            .ToList();

        // --- Alerts (compliance documents expiring within 60 days) ---
        var horizon = today.AddDays(60);
        var expiring = await _db.EmployeeComplianceRecords
            .Where(c => c.TenantId == tenantId && !c.IsDeleted
                && c.ExpiryDate != null && c.ExpiryDate <= horizon)
            .OrderBy(c => c.ExpiryDate)
            .Take(8)
            .Select(c => new { c.FieldLabel, c.ExpiryDate })
            .ToListAsync(cancellationToken);

        var alerts = expiring.Select(c =>
        {
            var expiry = c.ExpiryDate!.Value;
            var severity = expiry < today ? "Critical" : expiry <= today.AddDays(30) ? "Warning" : "Info";
            var label = string.IsNullOrWhiteSpace(c.FieldLabel) ? "Document" : c.FieldLabel;
            var title = expiry < today
                ? $"{label} expired {expiry:dd MMM}"
                : $"{label} expires {expiry:dd MMM}";
            return new DashboardAlertDto(title, severity);
        }).ToList();

        // --- Self-service signals ---
        var openLeaveRequests = await _db.LeaveRequests
            .CountAsync(l => l.TenantId == tenantId
                && l.Status != "Approved" && l.Status != "Rejected"
                && l.Status != "Cancelled" && l.Status != "Withdrawn" && l.Status != "Draft",
                cancellationToken);

        var newJoinersThisMonth = await _db.Employees
            .CountAsync(e => e.TenantId == tenantId
                && e.JoiningDate >= monthStart.ToDateTime(TimeOnly.MinValue),
                cancellationToken);

        return Ok(new DashboardOverviewDto(
            pendingApprovals,
            approvalQueue,
            payrollSummary,
            payrollByEntity,
            workforceMix,
            headcountByDepartment,
            alerts,
            openLeaveRequests,
            newJoinersThisMonth));
    }
}

public record DashboardSummaryDto(
    int TotalEmployees,
    int ActiveEmployees,
    int PresentToday,
    int OnLeave,
    int Absent,
    decimal OvertimeHours,
    int ChurnRisk);

public record DashboardTrendDto(
    string Month,
    decimal AttendanceRate,
    decimal OvertimeHours);

public record DashboardOverviewDto(
    int PendingApprovals,
    IReadOnlyList<ApprovalQueueItemDto> ApprovalQueue,
    PayrollSummaryDto? PayrollSummary,
    IReadOnlyList<NamedValueDto> PayrollByEntity,
    IReadOnlyList<NamedValueDto> WorkforceMix,
    IReadOnlyList<NamedValueDto> HeadcountByDepartment,
    IReadOnlyList<DashboardAlertDto> Alerts,
    int OpenLeaveRequests,
    int NewJoinersThisMonth);

public record ApprovalQueueItemDto(Guid Id, string Title, string Module, DateTime CreatedAtUtc);

public record PayrollSummaryDto(
    string PeriodLabel,
    decimal TotalGross,
    decimal TotalNet,
    decimal TotalDeductions,
    int EmployeeCount,
    string Status);

public record NamedValueDto(string Name, decimal Value);

public record DashboardAlertDto(string Title, string Severity);
