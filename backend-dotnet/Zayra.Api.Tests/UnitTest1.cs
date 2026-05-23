using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Zayra.Api.Controllers;
using Zayra.Api.Data;
using Zayra.Api.Models;

namespace Zayra.Api.Tests;

public class DashboardControllerTests
{
    [Fact]
    public async Task Summary_ReturnsLiveAttendanceMetrics()
    {
        await using var db = CreateDb();
        var today = DateOnly.FromDateTime(DateTime.UtcNow.Date);
        SeedEmployees(db);
        db.AttendanceRecords.AddRange(
            new AttendanceRecord { EmployeeId = 1, WorkDate = today, Status = "Present", OvertimeHours = 2m },
            new AttendanceRecord { EmployeeId = 2, WorkDate = today, Status = "Absent", OvertimeHours = 0m },
            new AttendanceRecord { EmployeeId = 3, WorkDate = today, Status = "On Leave", OvertimeHours = 0m },
            new AttendanceRecord { EmployeeId = 4, WorkDate = today.AddDays(-2), Status = "Present", OvertimeHours = 4.5m });
        await db.SaveChangesAsync();

        var result = await new DashboardController(db).Summary(CancellationToken.None);

        var summary = AssertOk<DashboardSummaryDto>(result);
        Assert.Equal(4, summary.TotalEmployees);
        Assert.Equal(3, summary.ActiveEmployees);
        Assert.Equal(1, summary.PresentToday);
        Assert.Equal(1, summary.Absent);
        Assert.Equal(1, summary.OnLeave);
        Assert.Equal(6.5m, summary.OvertimeHours);
        Assert.Equal(2, summary.ChurnRisk);
    }

    [Fact]
    public async Task Trends_ReturnsMonthlyAttendanceAndOvertimeSeries()
    {
        await using var db = CreateDb();
        var today = DateOnly.FromDateTime(DateTime.UtcNow.Date);
        var currentMonth = new DateOnly(today.Year, today.Month, 1);
        var previousMonth = currentMonth.AddMonths(-1);
        SeedEmployees(db);
        db.AttendanceRecords.AddRange(
            new AttendanceRecord { EmployeeId = 1, WorkDate = previousMonth.AddDays(1), Status = "Present", OvertimeHours = 1m },
            new AttendanceRecord { EmployeeId = 2, WorkDate = previousMonth.AddDays(2), Status = "Absent", OvertimeHours = 0m },
            new AttendanceRecord { EmployeeId = 1, WorkDate = today, Status = "Present", OvertimeHours = 3m },
            new AttendanceRecord { EmployeeId = 2, WorkDate = today, Status = "Present", OvertimeHours = 2m });
        await db.SaveChangesAsync();

        var result = await new DashboardController(db).Trends(2, CancellationToken.None);

        var trends = AssertOk<List<DashboardTrendDto>>(result);
        Assert.Equal(2, trends.Count);
        Assert.Equal(50m, trends[0].AttendanceRate);
        Assert.Equal(1m, trends[0].OvertimeHours);
        Assert.Equal(100m, trends[1].AttendanceRate);
        Assert.Equal(5m, trends[1].OvertimeHours);
    }

    private static ZayraDbContext CreateDb()
    {
        var options = new DbContextOptionsBuilder<ZayraDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        return new ZayraDbContext(options);
    }

    private static void SeedEmployees(ZayraDbContext db)
    {
        db.Employees.AddRange(
            new Employee { Id = 1, EmployeeCode = "E001", FullName = "Aisha Khan", Status = "Active" },
            new Employee { Id = 2, EmployeeCode = "E002", FullName = "Omar Ali", Status = "Active" },
            new Employee { Id = 3, EmployeeCode = "E003", FullName = "Maya Shah", Status = "Inactive" },
            new Employee { Id = 4, EmployeeCode = "E004", FullName = "Noor Ahmed", Status = "Active" });
    }

    private static T AssertOk<T>(IActionResult result)
    {
        var ok = Assert.IsType<OkObjectResult>(result);
        return Assert.IsType<T>(ok.Value);
    }
}
