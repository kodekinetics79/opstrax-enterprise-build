using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Zayra.Api.Application.Common;
using Zayra.Api.Data;
using Zayra.Api.Models;

namespace Zayra.Api.Controllers;

[ApiController]
[Route("api/shifts")]
[Authorize]
public class ShiftsController : ControllerBase
{
    private readonly ZayraDbContext _db;
    private readonly IDataScopeService _scopeService;

    public ShiftsController(ZayraDbContext db, IDataScopeService scopeService)
    {
        _db = db;
        _scopeService = scopeService;
    }

    // ── Shift Definitions ─────────────────────────────────────────────

    [HttpGet("definitions")]
    public async Task<IActionResult> ListDefinitions(CancellationToken ct)
    {
        var tenantId = GetTenantId();
        var defs = await _db.ShiftDefinitions
            .Where(x => x.TenantId == tenantId)
            .OrderBy(x => x.StartTime)
            .ToListAsync(ct);
        return Ok(defs);
    }

    [HttpPost("definitions")]
    [Authorize(Roles = "Admin,HR Manager")]
    public async Task<IActionResult> CreateDefinition([FromBody] ShiftDefinitionRequest req, CancellationToken ct)
    {
        var tenantId = GetTenantId();
        var def = new ShiftDefinition
        {
            TenantId = tenantId,
            Code = req.Code.ToUpperInvariant(),
            Name = req.Name,
            StartTime = TimeOnly.Parse(req.StartTime),
            EndTime = TimeOnly.Parse(req.EndTime),
            BreakMinutes = req.BreakMinutes,
            Color = req.Color,
            IsActive = true,
        };
        _db.ShiftDefinitions.Add(def);
        await _db.SaveChangesAsync(ct);
        return Created($"/api/shifts/definitions/{def.Id}", def);
    }

    [HttpPut("definitions/{id:guid}")]
    [Authorize(Roles = "Admin,HR Manager")]
    public async Task<IActionResult> UpdateDefinition(Guid id, [FromBody] ShiftDefinitionRequest req, CancellationToken ct)
    {
        var tenantId = GetTenantId();
        var def = await _db.ShiftDefinitions.FirstOrDefaultAsync(x => x.Id == id && x.TenantId == tenantId, ct);
        if (def is null) return NotFound();

        def.Code = req.Code.ToUpperInvariant();
        def.Name = req.Name;
        def.StartTime = TimeOnly.Parse(req.StartTime);
        def.EndTime = TimeOnly.Parse(req.EndTime);
        def.BreakMinutes = req.BreakMinutes;
        def.Color = req.Color;
        def.UpdatedAtUtc = DateTime.UtcNow;
        await _db.SaveChangesAsync(ct);
        return Ok(def);
    }

    [HttpDelete("definitions/{id:guid}")]
    [Authorize(Roles = "Admin,HR Manager")]
    public async Task<IActionResult> DeleteDefinition(Guid id, CancellationToken ct)
    {
        var tenantId = GetTenantId();
        var def = await _db.ShiftDefinitions.FirstOrDefaultAsync(x => x.Id == id && x.TenantId == tenantId, ct);
        if (def is null) return NotFound();
        _db.ShiftDefinitions.Remove(def);
        await _db.SaveChangesAsync(ct);
        return NoContent();
    }

    // ── Roster ────────────────────────────────────────────────────────

    [HttpGet("roster")]
    public async Task<IActionResult> GetRoster([FromQuery] DateOnly? from, [FromQuery] DateOnly? to, CancellationToken ct)
    {
        var tenantId = GetTenantId();
        var scope = await _scopeService.ResolveAsync(User, tenantId, ct);
        var today = DateOnly.FromDateTime(DateTime.UtcNow.Date);
        var start = from ?? today.AddDays(-(int)today.DayOfWeek + 1); // Monday
        var end = to ?? start.AddDays(6); // Sunday

        var employeeQuery = _db.Employees
            .Where(e => e.TenantId == tenantId && e.Status == "Active");
        if (!scope.IsUnrestricted)
            employeeQuery = employeeQuery.Where(e => scope.AllowedEmployeeIds!.Contains(e.Id));

        var employees = await employeeQuery
            .OrderBy(e => e.Department).ThenBy(e => e.FullName)
            .Select(e => new RosterEmployee(e.Id, e.FullName, e.Department, e.EmployeeCode))
            .ToListAsync(ct);

        var assignmentQuery = _db.ShiftAssignments
            .Where(a => a.TenantId == tenantId && a.AssignedDate >= start && a.AssignedDate <= end);
        if (!scope.IsUnrestricted)
            assignmentQuery = assignmentQuery.Where(a => scope.AllowedEmployeeIds!.Contains(a.EmployeeId));

        var assignments = await assignmentQuery
            .OrderBy(a => a.AssignedDate)
            .Select(a => new RosterAssignment(a.Id, a.EmployeeId, a.AssignedDate, a.ShiftDefinitionId, a.ShiftName, a.ShiftCode, a.ShiftColor))
            .ToListAsync(ct);

        return Ok(new { from = start, to = end, employees, assignments });
    }

    [HttpPost("roster/assign")]
    [Authorize(Roles = "Admin,HR Manager,HR Officer")]
    public async Task<IActionResult> Assign([FromBody] AssignShiftRequest req, CancellationToken ct)
    {
        var tenantId = GetTenantId();

        var emp = await _db.Employees.FirstOrDefaultAsync(e => e.Id == req.EmployeeId && e.TenantId == tenantId, ct);
        if (emp is null) return BadRequest(new { message = "Employee not found." });

        var def = await _db.ShiftDefinitions.FirstOrDefaultAsync(d => d.Id == req.ShiftDefinitionId && d.TenantId == tenantId, ct);
        if (def is null) return BadRequest(new { message = "Shift definition not found." });

        var existing = await _db.ShiftAssignments
            .FirstOrDefaultAsync(a => a.EmployeeId == req.EmployeeId && a.AssignedDate == req.Date && a.TenantId == tenantId, ct);

        if (existing is not null)
        {
            existing.ShiftDefinitionId = def.Id;
            existing.ShiftName = def.Name;
            existing.ShiftCode = def.Code;
            existing.ShiftColor = def.Color;
            existing.Notes = req.Notes ?? string.Empty;
        }
        else
        {
            _db.ShiftAssignments.Add(new ShiftAssignment
            {
                TenantId = tenantId,
                EmployeeId = req.EmployeeId,
                EmployeeName = emp.FullName,
                ShiftDefinitionId = def.Id,
                ShiftName = def.Name,
                ShiftCode = def.Code,
                ShiftColor = def.Color,
                AssignedDate = req.Date,
                Notes = req.Notes ?? string.Empty,
            });
        }

        await _db.SaveChangesAsync(ct);
        return Ok();
    }

    [HttpPost("roster/auto-plan")]
    [Authorize(Roles = "Admin,HR Manager")]
    public async Task<IActionResult> AutoPlan([FromBody] AutoPlanRequest req, CancellationToken ct)
    {
        var tenantId = GetTenantId();

        if (req.DateFrom > req.DateTo)
            return BadRequest(new { message = "From date must be before To date." });
        if (req.ShiftIds == null || req.ShiftIds.Count == 0)
            return BadRequest(new { message = "At least one shift must be selected." });

        var shifts = await _db.ShiftDefinitions
            .Where(d => req.ShiftIds.Contains(d.Id) && d.TenantId == tenantId && d.IsActive)
            .ToListAsync(ct);
        if (shifts.Count == 0)
            return BadRequest(new { message = "No valid active shifts found." });

        var employeeQuery = _db.Employees.Where(e => e.TenantId == tenantId && e.Status == "Active");
        if (req.EmployeeIds != null && req.EmployeeIds.Count > 0)
            employeeQuery = employeeQuery.Where(e => req.EmployeeIds.Contains(e.Id));
        var employees = await employeeQuery.Select(e => new { e.Id, e.FullName }).ToListAsync(ct);

        var days = new List<DateOnly>();
        for (var d = req.DateFrom; d <= req.DateTo; d = d.AddDays(1))
        {
            if (req.SkipWeekend && (d.DayOfWeek == DayOfWeek.Saturday || d.DayOfWeek == DayOfWeek.Sunday))
                continue;
            days.Add(d);
        }

        var existingKeys = (await _db.ShiftAssignments
            .Where(a => a.TenantId == tenantId && a.AssignedDate >= req.DateFrom && a.AssignedDate <= req.DateTo)
            .Select(a => new { a.EmployeeId, a.AssignedDate })
            .ToListAsync(ct))
            .Select(a => (a.EmployeeId, a.AssignedDate))
            .ToHashSet(new AssignmentKeyComparer());

        int created = 0, skipped = 0;
        for (int ei = 0; ei < employees.Count; ei++)
        {
            var emp = employees[ei];
            for (int di = 0; di < days.Count; di++)
            {
                var day = days[di];
                if (req.OverwriteExisting == false && existingKeys.Contains((emp.Id, day))) { skipped++; continue; }

                var shift = req.Pattern switch
                {
                    "rotating" => shifts[(ei + di) % shifts.Count],
                    "alternating" => shifts[di % shifts.Count],
                    _ => shifts[0],
                };

                var existing = await _db.ShiftAssignments
                    .FirstOrDefaultAsync(a => a.EmployeeId == emp.Id && a.AssignedDate == day && a.TenantId == tenantId, ct);
                if (existing != null)
                {
                    existing.ShiftDefinitionId = shift.Id; existing.ShiftName = shift.Name;
                    existing.ShiftCode = shift.Code; existing.ShiftColor = shift.Color;
                }
                else
                {
                    _db.ShiftAssignments.Add(new ShiftAssignment
                    {
                        TenantId = tenantId, EmployeeId = emp.Id, EmployeeName = emp.FullName,
                        ShiftDefinitionId = shift.Id, ShiftName = shift.Name,
                        ShiftCode = shift.Code, ShiftColor = shift.Color,
                        AssignedDate = day,
                    });
                }
                created++;
            }
        }
        await _db.SaveChangesAsync(ct);
        return Ok(new { created, skipped, employees = employees.Count, days = days.Count });
    }

    [HttpDelete("roster/{id:guid}")]
    [Authorize(Roles = "Admin,HR Manager,HR Officer")]
    public async Task<IActionResult> RemoveAssignment(Guid id, CancellationToken ct)
    {
        var tenantId = GetTenantId();
        var a = await _db.ShiftAssignments.FirstOrDefaultAsync(x => x.Id == id && x.TenantId == tenantId, ct);
        if (a is null) return NotFound();
        _db.ShiftAssignments.Remove(a);
        await _db.SaveChangesAsync(ct);
        return NoContent();
    }

    private Guid GetTenantId() => Guid.Parse(User.FindFirstValue("tenant_id")!);
}

public record ShiftDefinitionRequest(string Code, string Name, string StartTime, string EndTime, int BreakMinutes, string Color);
public record AssignShiftRequest(int EmployeeId, Guid ShiftDefinitionId, DateOnly Date, string? Notes);
public record RosterEmployee(int Id, string FullName, string Department, string EmployeeCode);
public record RosterAssignment(Guid Id, int EmployeeId, DateOnly Date, Guid ShiftDefinitionId, string ShiftName, string ShiftCode, string ShiftColor);
public record AutoPlanRequest(DateOnly DateFrom, DateOnly DateTo, List<Guid> ShiftIds, string Pattern, bool SkipWeekend, bool OverwriteExisting, List<int>? EmployeeIds);

internal class AssignmentKeyComparer : IEqualityComparer<(int EmployeeId, DateOnly Date)>
{
    public bool Equals((int, DateOnly) x, (int, DateOnly) y) => x == y;
    public int GetHashCode((int, DateOnly) obj) => HashCode.Combine(obj.Item1, obj.Item2);
}
