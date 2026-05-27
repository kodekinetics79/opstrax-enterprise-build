using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Zayra.Api.Application.Common;
using Zayra.Api.Data;

namespace Zayra.Api.Controllers.Leave;

[ApiController]
[Route("api/leave/calendar")]
[Authorize]
public class LeaveCalendarController : ControllerBase
{
    private readonly ZayraDbContext _db;
    private readonly IDataScopeService _scopeService;

    public LeaveCalendarController(ZayraDbContext db, IDataScopeService scopeService)
    {
        _db = db;
        _scopeService = scopeService;
    }

    [HttpGet]
    public async Task<IActionResult> List(
        [FromQuery] DateOnly? fromDate,
        [FromQuery] DateOnly? toDate,
        [FromQuery] string? departmentName,
        [FromQuery] int? employeeId,
        CancellationToken ct = default)
    {
        var tenantId = this.GetTenantId();
        if (tenantId is null) return Unauthorized();

        var scope = await _scopeService.ResolveAsync(User, tenantId.Value, ct);

        var from = fromDate ?? DateOnly.FromDateTime(new DateTime(DateTime.UtcNow.Year, DateTime.UtcNow.Month, 1));
        var to = toDate ?? from.AddMonths(1).AddDays(-1);

        var query = _db.LeaveRequests
            .Where(r => r.TenantId == tenantId
                && (r.Status == "Approved" || r.Status == "Submitted" || r.Status == "PendingManagerApproval" || r.Status == "PendingHRApproval")
                && r.StartDate <= to
                && r.EndDate >= from);

        if (!scope.IsUnrestricted)
            query = query.Where(r => scope.AllowedEmployeeIds!.Contains(r.EmployeeId));
        if (!string.IsNullOrWhiteSpace(departmentName)) query = query.Where(r => r.DepartmentName == departmentName);
        if (employeeId.HasValue) query = query.Where(r => r.EmployeeId == employeeId.Value);

        var requests = await query
            .OrderBy(r => r.StartDate)
            .ThenBy(r => r.EmployeeName)
            .ToListAsync(ct);

        var leaveTypeIds = requests.Select(r => r.LeaveTypeId).Distinct().ToList();
        var colorMap = await _db.LeaveTypes
            .Where(t => leaveTypeIds.Contains(t.Id))
            .ToDictionaryAsync(t => t.Id, t => t.ColorCode, ct);

        var result = requests.Select(r => new
        {
            r.EmployeeId,
            r.EmployeeName,
            r.DepartmentName,
            r.LeaveTypeName,
            r.StartDate,
            r.EndDate,
            TotalDays = r.TotalDays,
            r.Status,
            ColorCode = colorMap.TryGetValue(r.LeaveTypeId, out var color) ? color : "#3B82F6"
        });

        return Ok(result);
    }

    [HttpGet("team")]
    public async Task<IActionResult> Team(
        [FromQuery] DateOnly? fromDate,
        [FromQuery] DateOnly? toDate,
        CancellationToken ct = default)
    {
        var tenantId = this.GetTenantId();
        if (tenantId is null) return Unauthorized();

        var scope = await _scopeService.ResolveAsync(User, tenantId.Value, ct);

        var from = fromDate ?? DateOnly.FromDateTime(new DateTime(DateTime.UtcNow.Year, DateTime.UtcNow.Month, 1));
        var to = toDate ?? from.AddMonths(1).AddDays(-1);

        var query = _db.LeaveRequests
            .Where(r => r.TenantId == tenantId
                && (r.Status == "Approved" || r.Status == "Submitted")
                && r.StartDate <= to
                && r.EndDate >= from);

        if (!scope.IsUnrestricted)
            query = query.Where(r => scope.AllowedEmployeeIds!.Contains(r.EmployeeId));

        var requests = await query
            .OrderBy(r => r.DepartmentName)
            .ThenBy(r => r.StartDate)
            .ToListAsync(ct);

        var grouped = requests
            .GroupBy(r => r.DepartmentName)
            .Select(g => new
            {
                DepartmentName = g.Key,
                Employees = g.Select(r => new
                {
                    r.EmployeeId,
                    r.EmployeeName,
                    r.LeaveTypeName,
                    r.StartDate,
                    r.EndDate,
                    Days = r.TotalDays,
                    r.Status
                })
            });

        return Ok(grouped);
    }

    [HttpGet("today")]
    public async Task<IActionResult> Today(CancellationToken ct)
    {
        var tenantId = this.GetTenantId();
        if (tenantId is null) return Unauthorized();

        var scope = await _scopeService.ResolveAsync(User, tenantId.Value, ct);

        var today = DateOnly.FromDateTime(DateTime.UtcNow);

        var query = _db.LeaveRequests
            .Where(r => r.TenantId == tenantId
                && r.Status == "Approved"
                && r.StartDate <= today
                && r.EndDate >= today);

        if (!scope.IsUnrestricted)
            query = query.Where(r => scope.AllowedEmployeeIds!.Contains(r.EmployeeId));

        var requests = await query
            .OrderBy(r => r.DepartmentName)
            .ThenBy(r => r.EmployeeName)
            .ToListAsync(ct);

        var leaveTypeIds = requests.Select(r => r.LeaveTypeId).Distinct().ToList();
        var colorMap = await _db.LeaveTypes
            .Where(t => leaveTypeIds.Contains(t.Id))
            .ToDictionaryAsync(t => t.Id, t => t.ColorCode, ct);

        var result = requests.Select(r => new
        {
            r.EmployeeId,
            r.EmployeeName,
            r.DepartmentName,
            r.LeaveTypeName,
            r.StartDate,
            r.EndDate,
            TotalDays = r.TotalDays,
            r.Status,
            ColorCode = colorMap.TryGetValue(r.LeaveTypeId, out var color) ? color : "#3B82F6"
        });

        return Ok(result);
    }
}
