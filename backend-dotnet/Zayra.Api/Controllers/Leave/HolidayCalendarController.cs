using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Zayra.Api.Application.Common;
using Zayra.Api.Data;
using Zayra.Api.Models;

namespace Zayra.Api.Controllers.Leave;

[ApiController]
[Route("api/leave/holidays")]
[Authorize]
public class HolidayCalendarController : ControllerBase
{
    private readonly ZayraDbContext _db;

    public HolidayCalendarController(ZayraDbContext db) => _db = db;

    [HttpGet("calendars")]
    public async Task<IActionResult> ListCalendars([FromQuery] int? year, CancellationToken ct)
    {
        var tenantId = this.GetTenantId();
        if (tenantId is null) return Unauthorized();

        var query = _db.PublicHolidayCalendars.Where(c => c.TenantId == tenantId);
        if (year.HasValue) query = query.Where(c => c.CalendarYear == year.Value);

        var items = await query
            .OrderByDescending(c => c.CalendarYear)
            .ThenBy(c => c.Name)
            .ToListAsync(ct);

        return Ok(items);
    }

    [HttpPost("calendars")]
    [Authorize(Roles = "Admin,HR Manager")]
    public async Task<IActionResult> CreateCalendar([FromBody] CreateCalendarRequest req, CancellationToken ct)
    {
        var tenantId = this.GetTenantId();
        if (tenantId is null) return Unauthorized();

        var calendar = new PublicHolidayCalendar
        {
            TenantId = tenantId.Value,
            Name = req.Name,
            CountryCode = req.CountryCode,
            CompanyId = req.CompanyId,
            BranchId = req.BranchId,
            CalendarYear = req.CalendarYear,
            IsActive = true
        };

        _db.PublicHolidayCalendars.Add(calendar);
        await _db.SaveChangesAsync(ct);
        return Created($"/api/leave/holidays/calendars/{calendar.Id}", calendar);
    }

    [HttpGet("calendars/{id:guid}/holidays")]
    public async Task<IActionResult> ListHolidays(Guid id, CancellationToken ct)
    {
        var tenantId = this.GetTenantId();
        if (tenantId is null) return Unauthorized();

        var calendarExists = await _db.PublicHolidayCalendars
            .AnyAsync(c => c.Id == id && c.TenantId == tenantId, ct);
        if (!calendarExists) return NotFound();

        var holidays = await _db.PublicHolidays
            .Where(h => h.TenantId == tenantId && h.CalendarId == id)
            .OrderBy(h => h.Date)
            .ToListAsync(ct);

        return Ok(holidays);
    }

    [HttpPost("calendars/{id:guid}/holidays")]
    [Authorize(Roles = "Admin,HR Manager")]
    public async Task<IActionResult> AddHoliday(Guid id, [FromBody] AddHolidayRequest req, CancellationToken ct)
    {
        var tenantId = this.GetTenantId();
        if (tenantId is null) return Unauthorized();

        var calendarExists = await _db.PublicHolidayCalendars
            .AnyAsync(c => c.Id == id && c.TenantId == tenantId, ct);
        if (!calendarExists) return NotFound();

        var holiday = new PublicHoliday
        {
            TenantId = tenantId.Value,
            CalendarId = id,
            NameEn = req.NameEn,
            NameAr = req.NameAr ?? string.Empty,
            Date = req.Date,
            HijriDate = req.HijriDate ?? string.Empty,
            IsRecurring = req.IsRecurring,
            IsOptional = req.IsOptional,
            HolidayType = req.HolidayType ?? "National",
            Notes = req.Notes ?? string.Empty
        };

        _db.PublicHolidays.Add(holiday);
        await _db.SaveChangesAsync(ct);
        return Created($"/api/leave/holidays/calendars/{id}/holidays", holiday);
    }

    [HttpDelete("holidays/{id:guid}")]
    [Authorize(Roles = "Admin,HR Manager")]
    public async Task<IActionResult> DeleteHoliday(Guid id, CancellationToken ct)
    {
        var tenantId = this.GetTenantId();
        if (tenantId is null) return Unauthorized();

        var holiday = await _db.PublicHolidays
            .FirstOrDefaultAsync(h => h.Id == id && h.TenantId == tenantId, ct);
        if (holiday is null) return NotFound();

        _db.PublicHolidays.Remove(holiday);
        await _db.SaveChangesAsync(ct);
        return NoContent();
    }

    [HttpGet("today")]
    public async Task<IActionResult> Today(CancellationToken ct)
    {
        var tenantId = this.GetTenantId();
        if (tenantId is null) return Unauthorized();

        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var holiday = await _db.PublicHolidays
            .Where(h => h.TenantId == tenantId && h.Date == today)
            .FirstOrDefaultAsync(ct);

        return Ok(new { isHoliday = holiday is not null, holiday });
    }

    [HttpGet("range")]
    public async Task<IActionResult> Range([FromQuery] DateOnly? fromDate, [FromQuery] DateOnly? toDate, CancellationToken ct)
    {
        var tenantId = this.GetTenantId();
        if (tenantId is null) return Unauthorized();

        var from = fromDate ?? DateOnly.FromDateTime(DateTime.UtcNow);
        var to = toDate ?? from.AddMonths(1);

        var holidays = await _db.PublicHolidays
            .Where(h => h.TenantId == tenantId && h.Date >= from && h.Date <= to)
            .OrderBy(h => h.Date)
            .ToListAsync(ct);

        return Ok(holidays);
    }
}

public record CreateCalendarRequest(string Name, string CountryCode, int CalendarYear, Guid? CompanyId, Guid? BranchId);
public record AddHolidayRequest(
    string NameEn,
    string? NameAr,
    DateOnly Date,
    string? HijriDate,
    bool IsRecurring,
    bool IsOptional,
    string? HolidayType,
    string? Notes);
