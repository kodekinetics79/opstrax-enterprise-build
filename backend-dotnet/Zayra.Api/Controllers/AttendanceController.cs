using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Zayra.Api.Application.Attendance;
using Zayra.Api.Application.Auth;
using Zayra.Api.Application.Common;
using Zayra.Api.Models;

namespace Zayra.Api.Controllers;

[ApiController]
[Route("api/attendance")]
[Authorize]
public class AttendanceController : ControllerBase
{
    private readonly IAttendanceService _attendance;

    public AttendanceController(IAttendanceService attendance) => _attendance = attendance;

    [HttpGet("dashboard")]
    public Task<AttendanceDashboardDto> Dashboard([FromQuery] DateOnly? date, CancellationToken ct) =>
        _attendance.DashboardAsync(RequireTenant(), date ?? DateOnly.FromDateTime(DateTime.UtcNow), ct);

    [HttpGet("today")]
    public async Task<IActionResult> Today(CancellationToken ct)
    {
        var date = DateOnly.FromDateTime(DateTime.UtcNow);
        var summary = await _attendance.DashboardAsync(RequireTenant(), date, ct);
        return Ok(new { date = summary.Date, totalActive = summary.ActiveEmployees, present = summary.Present, absent = summary.Absent, onLeave = 0, late = summary.Late });
    }

    [HttpGet("devices")]
    [Authorize(Roles = "Admin,HR Manager,HR Officer,Auditor")]
    public Task<PagedResult<AttendanceDevice>> Devices([FromQuery] int page = 1, [FromQuery] int pageSize = 25, CancellationToken ct = default) =>
        _attendance.GetDevicesAsync(RequireTenant(), page, pageSize, ct);

    [HttpGet("devices/{id:guid}")]
    public async Task<ActionResult<AttendanceDevice>> Device(Guid id, CancellationToken ct) =>
        await _attendance.GetDeviceAsync(RequireTenant(), id, ct) is { } device ? Ok(device) : NotFound();

    [HttpPost("devices")]
    [Authorize(Roles = "Admin,HR Manager,HR Officer")]
    public async Task<ActionResult<AttendanceDevice>> CreateDevice(AttendanceDeviceRequest request, CancellationToken ct)
    {
        try
        {
            var device = await _attendance.CreateDeviceAsync(RequireTenant(), request, Context(), ct);
            return Created($"/api/attendance/devices/{device.Id}", device);
        }
        catch (InvalidOperationException ex) { return BadRequest(new { message = ex.Message }); }
    }

    [HttpPut("devices/{id:guid}")]
    [Authorize(Roles = "Admin,HR Manager,HR Officer")]
    public async Task<ActionResult<AttendanceDevice>> UpdateDevice(Guid id, AttendanceDeviceRequest request, CancellationToken ct)
    {
        try
        {
            var device = await _attendance.UpdateDeviceAsync(RequireTenant(), id, request, Context(), ct);
            return device is null ? NotFound() : Ok(device);
        }
        catch (InvalidOperationException ex) { return BadRequest(new { message = ex.Message }); }
    }

    [HttpDelete("devices/{id:guid}")]
    [Authorize(Roles = "Admin,HR Manager,HR Officer")]
    public async Task<IActionResult> DeleteDevice(Guid id, CancellationToken ct) =>
        await _attendance.DeleteDeviceAsync(RequireTenant(), id, Context(), ct) ? NoContent() : NotFound();

    [HttpPost("devices/{id:guid}/test-connection")]
    public async Task<ActionResult<AttendanceDeviceSyncLog>> TestConnection(Guid id, CancellationToken ct) =>
        await _attendance.TestConnectionAsync(RequireTenant(), id, Context(), ct) is { } log ? Ok(log) : NotFound();

    [HttpPost("devices/{id:guid}/sync")]
    public async Task<ActionResult<AttendanceDeviceSyncLog>> Sync(Guid id, CancellationToken ct) =>
        await _attendance.SyncDeviceAsync(RequireTenant(), id, Context(), ct) is { } log ? Ok(log) : NotFound();

    [HttpGet("devices/{id:guid}/sync-logs")]
    public Task<IReadOnlyCollection<AttendanceDeviceSyncLog>> SyncLogs(Guid id, CancellationToken ct) =>
        _attendance.GetSyncLogsAsync(RequireTenant(), id, ct);

    [HttpPost("events/push")]
    public async Task<ActionResult<AttendanceRawEvent>> PushEvent(AttendanceRawEventRequest request, CancellationToken ct)
    {
        try
        {
            var raw = await _attendance.PushEventAsync(RequireTenant(), request, Context(), ct);
            return Created($"/api/attendance/events/raw/{raw.Id}", raw);
        }
        catch (InvalidOperationException ex) { return BadRequest(new { message = ex.Message }); }
    }

    [HttpPost("events/import")]
    [Authorize(Roles = "Admin,HR Manager,HR Officer")]
    public Task<AttendanceImportBatch> Import(ImportAttendanceRequest request, CancellationToken ct) =>
        _attendance.ImportCsvAsync(RequireTenant(), request, Context(), ct);

    [HttpGet("events/raw")]
    public Task<PagedResult<AttendanceRawEvent>> Raw([FromQuery] DateOnly? from, [FromQuery] DateOnly? to, [FromQuery] int? employeeId, [FromQuery] bool? processed, [FromQuery] int page = 1, [FromQuery] int pageSize = 50, CancellationToken ct = default) =>
        _attendance.GetRawEventsAsync(RequireTenant(), from, to, employeeId, processed, page, pageSize, ct);

    [HttpGet]
    [HttpGet("daily")]
    public Task<PagedResult<AttendanceDailyDto>> Daily([FromQuery] DateOnly? from, [FromQuery] DateOnly? to, [FromQuery] int? employeeId, [FromQuery] string? status, [FromQuery] int page = 1, [FromQuery] int pageSize = 50, CancellationToken ct = default) =>
        _attendance.GetDailyAsync(RequireTenant(), from, to, employeeId, status, page, pageSize, ct);

    [HttpGet("monthly")]
    public Task<IReadOnlyCollection<AttendanceMonthlyDto>> Monthly([FromQuery] int year, [FromQuery] int month, [FromQuery] int? employeeId, CancellationToken ct) =>
        _attendance.GetMonthlyAsync(RequireTenant(), year == 0 ? DateTime.UtcNow.Year : year, month == 0 ? DateTime.UtcNow.Month : month, employeeId, ct);

    [HttpPost("process")]
    public Task<int> Process(ProcessAttendanceRequest request, CancellationToken ct) =>
        _attendance.ProcessAsync(RequireTenant(), request, Context(), ct);

    [HttpPost("reprocess")]
    public Task<int> Reprocess(ProcessAttendanceRequest request, CancellationToken ct) =>
        _attendance.ProcessAsync(RequireTenant(), request, Context(), ct);

    [HttpPost("punch/web")]
    public Task<AttendanceRawEvent> WebPunch(WebPunchRequest request, CancellationToken ct) =>
        _attendance.PunchAsync(RequireTenant(), request, "Web punch", Context(), ct);

    [HttpPost("punch/mobile")]
    public Task<AttendanceRawEvent> MobilePunch(WebPunchRequest request, CancellationToken ct) =>
        _attendance.PunchAsync(RequireTenant(), request, "Mobile app punch", Context(), ct);

    [HttpPost("punch/kiosk")]
    public Task<AttendanceRawEvent> KioskPunch(WebPunchRequest request, CancellationToken ct) =>
        _attendance.PunchAsync(RequireTenant(), request, "Tablet/kiosk punch", Context(), ct);

    [HttpPost("regularization")]
    public Task<AttendanceRegularizationRequest> Regularization(RegularizationRequestDto request, CancellationToken ct) =>
        _attendance.CreateRegularizationAsync(RequireTenant(), request, Context(), ct);

    [HttpGet("regularization/my")]
    public Task<PagedResult<AttendanceRegularizationRequest>> MyRegularization([FromQuery] int? employeeId, [FromQuery] int page = 1, [FromQuery] int pageSize = 25, CancellationToken ct = default) =>
        _attendance.GetRegularizationAsync(RequireTenant(), employeeId, null, page, pageSize, ct);

    [HttpGet("regularization/pending-approval")]
    public Task<PagedResult<AttendanceRegularizationRequest>> PendingRegularization([FromQuery] int page = 1, [FromQuery] int pageSize = 25, CancellationToken ct = default) =>
        _attendance.GetRegularizationAsync(RequireTenant(), null, "PendingManager", page, pageSize, ct);

    [HttpPost("regularization/{id:guid}/approve")]
    public async Task<ActionResult<AttendanceRegularizationRequest>> ApproveRegularization(Guid id, RegularizationDecisionRequest request, CancellationToken ct)
    {
        try
        {
            var regularization = await _attendance.ApproveRegularizationAsync(RequireTenant(), id, request, Context(), ct);
            return regularization is null ? NotFound() : Ok(regularization);
        }
        catch (InvalidOperationException ex) { return BadRequest(new { message = ex.Message }); }
    }

    [HttpPost("regularization/{id:guid}/reject")]
    public async Task<ActionResult<AttendanceRegularizationRequest>> RejectRegularization(Guid id, RegularizationDecisionRequest request, CancellationToken ct) =>
        await _attendance.RejectRegularizationAsync(RequireTenant(), id, request, Context(), ct) is { } regularization ? Ok(regularization) : NotFound();

    [HttpGet("reports/daily")]
    public Task<IReadOnlyCollection<AttendanceDailyDto>> ReportDaily([FromQuery] DateOnly from, [FromQuery] DateOnly to, CancellationToken ct) =>
        _attendance.ReportDailyAsync(RequireTenant(), from, to, ct);

    [HttpGet("reports/monthly")]
    public Task<IReadOnlyCollection<AttendanceMonthlyDto>> ReportMonthly([FromQuery] int year, [FromQuery] int month, CancellationToken ct) =>
        _attendance.ReportMonthlyAsync(RequireTenant(), year, month, ct);

    [HttpGet("reports/late")]
    public Task<IReadOnlyCollection<AttendanceDailyDto>> ReportLate([FromQuery] DateOnly from, [FromQuery] DateOnly to, CancellationToken ct) =>
        _attendance.ReportByStatusAsync(RequireTenant(), from, to, "Late", ct);

    [HttpGet("reports/absence")]
    public Task<IReadOnlyCollection<AttendanceDailyDto>> ReportAbsence([FromQuery] DateOnly from, [FromQuery] DateOnly to, CancellationToken ct) =>
        _attendance.ReportByStatusAsync(RequireTenant(), from, to, "Absent", ct);

    [HttpGet("reports/missing-punch")]
    public Task<IReadOnlyCollection<AttendanceDailyDto>> ReportMissingPunch([FromQuery] DateOnly from, [FromQuery] DateOnly to, CancellationToken ct) =>
        _attendance.ReportMissingPunchAsync(RequireTenant(), from, to, ct);

    [HttpGet("reports/payroll-summary")]
    public Task<IReadOnlyCollection<AttendancePayrollSummaryDto>> ReportPayroll([FromQuery] DateOnly from, [FromQuery] DateOnly to, CancellationToken ct) =>
        _attendance.PayrollSummaryAsync(RequireTenant(), from, to, ct);

    [HttpGet("reports/device-sync")]
    public Task<IReadOnlyCollection<AttendanceDeviceSyncDto>> ReportDeviceSync(CancellationToken ct) =>
        _attendance.DeviceSyncReportAsync(RequireTenant(), ct);

    [HttpGet("ai/insights")]
    public Task<IReadOnlyCollection<AttendanceAIInsight>> Insights(CancellationToken ct) =>
        _attendance.GenerateInsightsAsync(RequireTenant(), ct);

    private Guid RequireTenant() => Guid.Parse(User.FindFirstValue("tenant_id")!);
    private RequestContext Context() => new(HttpContext.Connection.RemoteIpAddress?.ToString(), Request.Headers.UserAgent.ToString(), GetUserId(), RequireTenant());
    private Guid? GetUserId() => Guid.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier) ?? User.FindFirstValue("sub"), out var id) ? id : null;
}
