using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
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
        var today = DateOnly.FromDateTime(DateTime.UtcNow.Date);
        var monthStart = new DateOnly(today.Year, today.Month, 1);

        var totalEmployees = await _db.Employees.CountAsync(cancellationToken);
        var activeEmployees = await _db.Employees.CountAsync(e => e.Status == "Active", cancellationToken);
        var todayAttendance = _db.AttendanceRecords.Where(a => a.WorkDate == today);

        var presentToday = await todayAttendance.CountAsync(a => a.Status == "Present", cancellationToken);
        var onLeave = await todayAttendance.CountAsync(a => a.Status == "Leave" || a.Status == "On Leave", cancellationToken);
        var absent = await todayAttendance.CountAsync(a => a.Status == "Absent", cancellationToken);
        var overtimeHours = await _db.AttendanceRecords
            .Where(a => a.WorkDate >= monthStart && a.WorkDate <= today)
            .SumAsync(a => (decimal?)a.OvertimeHours, cancellationToken) ?? 0m;

        var churnRisk = await _db.AttendanceRecords
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
        months = Math.Clamp(months, 1, 12);

        var today = DateOnly.FromDateTime(DateTime.UtcNow.Date);
        var currentMonth = new DateOnly(today.Year, today.Month, 1);
        var firstMonth = currentMonth.AddMonths(-(months - 1));

        var records = await _db.AttendanceRecords
            .Where(a => a.WorkDate >= firstMonth && a.WorkDate <= today)
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
