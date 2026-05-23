using System.Security.Claims;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Zayra.Api.Data;
using Zayra.Api.Models;

namespace Zayra.Api.Controllers;

[ApiController]
[Route("api/ess")]
[Authorize]
public class EmployeeSelfServiceController : ControllerBase
{
    private static readonly HashSet<string> SensitiveProfileFields = new(StringComparer.OrdinalIgnoreCase)
    {
        "passportNumber", "visaNumber", "iqamaNumber", "emiratesId", "bankName", "bankIban", "medicalInformation"
    };

    private readonly ZayraDbContext _db;

    public EmployeeSelfServiceController(ZayraDbContext db)
    {
        _db = db;
    }

    [HttpGet("dashboard")]
    public async Task<ActionResult<ESSDashboardDto>> Dashboard(CancellationToken cancellationToken)
    {
        var (tenantId, employeeId) = RequireEssRead();
        var employee = await OwnEmployee(tenantId, employeeId, cancellationToken);
        if (employee is null) return NotFound();
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var attendance = await _db.AttendanceDailyRecords.AsNoTracking().FirstOrDefaultAsync(x => x.TenantId == tenantId && x.EmployeeId == employeeId && x.WorkDate == today && !x.IsDeleted, cancellationToken);
        var leaveBalances = await _db.EmployeeLeaveBalances.AsNoTracking().Where(x => x.TenantId == tenantId && x.EmployeeId == employeeId && x.Year == DateTime.UtcNow.Year).ToListAsync(cancellationToken);
        var pendingRequests = await _db.HRRequests.CountAsync(x => x.TenantId == tenantId && x.EmployeeId == employeeId && x.Status != "Closed", cancellationToken);
        var pendingLeave = await _db.LeaveRequests.CountAsync(x => x.TenantId == tenantId && x.EmployeeId == employeeId && x.Status.Contains("Pending"), cancellationToken);
        var documentAlerts = await _db.EmployeeDocuments.AsNoTracking()
            .Where(x => x.TenantId == tenantId && x.EmployeeId == employeeId && !x.IsDeleted && x.ExpiryDate != null && x.ExpiryDate <= today.AddDays(60))
            .OrderBy(x => x.ExpiryDate)
            .Take(5)
            .Select(x => new ESSDocumentDto(x.Id, x.DocumentType, x.FileName, x.ExpiryDate, x.ApprovalStatus))
            .ToListAsync(cancellationToken);
        var announcements = await ActiveAnnouncements(tenantId).Take(5).ToListAsync(cancellationToken);
        var notifications = await _db.EmployeeNotifications.AsNoTracking().Where(x => x.TenantId == tenantId && x.EmployeeId == employeeId && !x.IsRead).OrderByDescending(x => x.CreatedAtUtc).Take(5).ToListAsync(cancellationToken);
        var actionItems = await _db.EmployeeActionItems.AsNoTracking().Where(x => x.TenantId == tenantId && x.EmployeeId == employeeId && x.Status == "Open").OrderBy(x => x.DueAtUtc).Take(6).ToListAsync(cancellationToken);

        await EssAudit(tenantId, employeeId, "ess.dashboard.viewed", "Employee", employeeId.ToString(), cancellationToken);
        return Ok(new ESSDashboardDto(
            new ESSProfileSummaryDto(employee.Id, employee.EmployeeCode, employee.FullName, employee.JobTitle, employee.Department, employee.ProfilePhotoUrl, employee.ProfileCompletenessScore),
            attendance,
            leaveBalances.Select(x => new ESSLeaveBalanceDto(x.LeaveTypeId, x.LeaveTypeName, x.Entitled, x.Used, x.Pending, x.Available)).ToList(),
            pendingRequests + pendingLeave,
            documentAlerts,
            announcements.Select(ToAnnouncementDto).ToList(),
            notifications.Select(ToNotificationDto).ToList(),
            actionItems.Select(x => new ESSActionItemDto(x.Id, x.Title, x.Category, x.DueAtUtc)).ToList()));
    }

    [HttpGet("profile")]
    public async Task<ActionResult<Employee>> Profile(CancellationToken cancellationToken)
    {
        var (tenantId, employeeId) = RequireEssRead();
        var employee = await OwnEmployee(tenantId, employeeId, cancellationToken);
        if (employee is null) return NotFound();
        await EssAudit(tenantId, employeeId, "ess.profile.viewed", "Employee", employeeId.ToString(), cancellationToken);
        return Ok(employee);
    }

    [HttpPut("profile-change-request")]
    public async Task<ActionResult<EmployeeProfileChangeRequest>> ProfileChangeRequest(ProfileChangeRequestDto request, CancellationToken cancellationToken)
    {
        var (tenantId, employeeId) = RequireEssWrite();
        var changesJson = JsonSerializer.Serialize(request.Changes);
        var containsSensitive = request.Changes.Keys.Any(SensitiveProfileFields.Contains);
        var change = new EmployeeProfileChangeRequest
        {
            TenantId = tenantId,
            EmployeeId = employeeId,
            RequestedChangesJson = changesJson,
            Reason = request.Reason ?? string.Empty,
            ContainsSensitiveFields = containsSensitive,
            CreatedBy = GetUserId()
        };
        _db.EmployeeProfileChangeRequests.Add(change);
        await _db.SaveChangesAsync(cancellationToken);
        await EssAudit(tenantId, employeeId, "ess.profile_change.requested", "EmployeeProfileChangeRequest", change.Id.ToString(), cancellationToken);
        return Created($"/api/ess/profile-change-request/{change.Id}", change);
    }

    [HttpGet("payslips")]
    public async Task<ActionResult<IReadOnlyCollection<PayrollSlip>>> Payslips(CancellationToken cancellationToken)
    {
        var (tenantId, employeeId) = RequireEssRead();
        var slips = await _db.PayrollSlips.AsNoTracking()
            .Where(x => x.TenantId == tenantId && x.EmployeeId == employeeId)
            .OrderByDescending(x => x.RunId)
            .ToListAsync(cancellationToken);
        await EssAudit(tenantId, employeeId, "ess.payslips.viewed", "PayrollSlip", employeeId.ToString(), cancellationToken);
        return Ok(slips);
    }

    [HttpGet("payslips/{id:guid}/download")]
    public async Task<IActionResult> DownloadPayslip(Guid id, CancellationToken cancellationToken)
    {
        var (tenantId, employeeId) = RequireEssRead();
        var slip = await _db.PayrollSlips.AsNoTracking().FirstOrDefaultAsync(x => x.TenantId == tenantId && x.EmployeeId == employeeId && x.Id == id, cancellationToken);
        if (slip is null) return NotFound();
        _db.EmployeePayslipAccessLogs.Add(new EmployeePayslipAccessLog { TenantId = tenantId, EmployeeId = employeeId, PayslipId = id, Action = "Download", UserId = GetUserId() });
        await _db.SaveChangesAsync(cancellationToken);
        var body = $"Zayra Payslip\nEmployee: {slip.EmployeeName}\nGross: {slip.GrossSalary:0.00}\nDeductions: {slip.Deductions:0.00}\nNet: {slip.NetSalary:0.00}\nStatus: {slip.Status}\n";
        return File(Encoding.UTF8.GetBytes(body), "application/pdf", $"payslip-{slip.EmployeeCode}-{id:N}.pdf");
    }

    [HttpGet("attendance")]
    public async Task<ActionResult<IReadOnlyCollection<AttendanceDailyRecord>>> Attendance([FromQuery] DateOnly? from, [FromQuery] DateOnly? to, CancellationToken cancellationToken)
    {
        var (tenantId, employeeId) = RequireEssRead();
        var start = from ?? DateOnly.FromDateTime(DateTime.UtcNow.AddDays(-30));
        var end = to ?? DateOnly.FromDateTime(DateTime.UtcNow);
        return Ok(await _db.AttendanceDailyRecords.AsNoTracking()
            .Where(x => x.TenantId == tenantId && x.EmployeeId == employeeId && !x.IsDeleted && x.WorkDate >= start && x.WorkDate <= end)
            .OrderByDescending(x => x.WorkDate)
            .ToListAsync(cancellationToken));
    }

    [HttpPost("attendance/regularization")]
    public async Task<ActionResult<AttendanceRegularizationRequest>> AttendanceRegularization(ESSAttendanceRegularizationDto request, CancellationToken cancellationToken)
    {
        var (tenantId, employeeId) = RequireEssWrite();
        var regularization = new AttendanceRegularizationRequest
        {
            TenantId = tenantId,
            EmployeeId = employeeId,
            WorkDate = request.WorkDate,
            RequestType = request.RequestType,
            RequestedInUtc = request.RequestedInUtc,
            RequestedOutUtc = request.RequestedOutUtc,
            Reason = request.Reason,
            RequestedByUserId = GetUserId(),
            PayrollLockChecked = true
        };
        _db.AttendanceRegularizationRequests.Add(regularization);
        await _db.SaveChangesAsync(cancellationToken);
        await EssAudit(tenantId, employeeId, "ess.attendance_regularization.created", "AttendanceRegularizationRequest", regularization.Id.ToString(), cancellationToken);
        return Created($"/api/ess/attendance/regularization/{regularization.Id}", regularization);
    }

    [HttpGet("leave/balance")]
    public async Task<ActionResult<IReadOnlyCollection<ESSLeaveBalanceDto>>> LeaveBalance(CancellationToken cancellationToken)
    {
        var (tenantId, employeeId) = RequireEssRead();
        var balances = await _db.EmployeeLeaveBalances.AsNoTracking().Where(x => x.TenantId == tenantId && x.EmployeeId == employeeId && x.Year == DateTime.UtcNow.Year).ToListAsync(cancellationToken);
        return Ok(balances.Select(x => new ESSLeaveBalanceDto(x.LeaveTypeId, x.LeaveTypeName, x.Entitled, x.Used, x.Pending, x.Available)).ToList());
    }

    [HttpPost("leave/request")]
    public async Task<ActionResult<LeaveRequest>> LeaveRequest(ESSLeaveRequestDto request, CancellationToken cancellationToken)
    {
        var (tenantId, employeeId) = RequireEssWrite();
        var employee = await OwnEmployee(tenantId, employeeId, cancellationToken);
        if (employee is null) return NotFound();
        var leaveType = await _db.LeaveTypes.AsNoTracking().FirstOrDefaultAsync(x => x.TenantId == tenantId && x.Id == request.LeaveTypeId && x.IsActive, cancellationToken);
        if (leaveType is null) return BadRequest(new { message = "Leave type is not available." });
        var days = Math.Max(1, request.EndDate.DayNumber - request.StartDate.DayNumber + 1);
        var leave = new LeaveRequest
        {
            TenantId = tenantId,
            EmployeeId = employeeId,
            EmployeeName = employee.FullName,
            DepartmentName = employee.Department,
            DesignationTitle = employee.Designation,
            LeaveTypeId = leaveType.Id,
            LeaveTypeName = leaveType.NameEn,
            StartDate = request.StartDate,
            EndDate = request.EndDate,
            TotalDays = days,
            DayType = request.DayType ?? "Full",
            Reason = request.Reason,
            Status = "PendingManager",
            SubmittedAtUtc = DateTime.UtcNow
        };
        _db.LeaveRequests.Add(leave);
        await _db.SaveChangesAsync(cancellationToken);
        await EssAudit(tenantId, employeeId, "ess.leave.requested", "LeaveRequest", leave.Id.ToString(), cancellationToken);
        return Created($"/api/ess/leave/request/{leave.Id}", leave);
    }

    [HttpGet("documents")]
    public async Task<ActionResult<IReadOnlyCollection<ESSDocumentDto>>> Documents(CancellationToken cancellationToken)
    {
        var (tenantId, employeeId) = RequireEssRead();
        var documents = await _db.EmployeeDocuments.AsNoTracking()
            .Where(x => x.TenantId == tenantId && x.EmployeeId == employeeId && !x.IsDeleted)
            .OrderBy(x => x.DocumentType)
            .Select(x => new ESSDocumentDto(x.Id, x.DocumentType, x.FileName, x.ExpiryDate, x.ApprovalStatus))
            .ToListAsync(cancellationToken);
        await EssAudit(tenantId, employeeId, "ess.documents.viewed", "EmployeeDocument", employeeId.ToString(), cancellationToken);
        return Ok(documents);
    }

    [HttpPost("documents/upload")]
    public async Task<ActionResult<EmployeeDocument>> UploadDocument(ESSDocumentUploadDto request, CancellationToken cancellationToken)
    {
        var (tenantId, employeeId) = RequireEssWrite();
        var document = new EmployeeDocument
        {
            TenantId = tenantId,
            EmployeeId = employeeId,
            DocumentType = request.DocumentType,
            FileName = request.FileName,
            ContentType = request.ContentType,
            StorageUrl = request.StorageUrl,
            ExpiryDate = request.ExpiryDate,
            ApprovalStatus = "Pending",
            IsRequired = request.IsRequired
        };
        _db.EmployeeDocuments.Add(document);
        await _db.SaveChangesAsync(cancellationToken);
        await EssAudit(tenantId, employeeId, "ess.document.uploaded", "EmployeeDocument", document.Id.ToString(), cancellationToken);
        return Created($"/api/ess/documents/{document.Id}", document);
    }

    [HttpPost("hr-requests")]
    public async Task<ActionResult<HRRequest>> CreateHrRequest(ESSHRRequestCreateDto request, CancellationToken cancellationToken)
    {
        var (tenantId, employeeId) = RequireEssWrite();
        var category = request.CategoryId is null ? null : await _db.HRRequestCategories.AsNoTracking().FirstOrDefaultAsync(x => x.TenantId == tenantId && x.Id == request.CategoryId && x.IsActive, cancellationToken);
        var slaHours = category?.DefaultSlaHours ?? 48;
        var hrRequest = new HRRequest
        {
            TenantId = tenantId,
            EmployeeId = employeeId,
            CategoryId = category?.Id,
            CategoryName = category?.Name ?? request.CategoryName ?? "General HR",
            Subject = request.Subject,
            Description = request.Description,
            Priority = request.Priority ?? "Normal",
            DueAtUtc = DateTime.UtcNow.AddHours(slaHours),
            CreatedBy = GetUserId()
        };
        _db.HRRequests.Add(hrRequest);
        await _db.SaveChangesAsync(cancellationToken);
        await EssAudit(tenantId, employeeId, "ess.hr_request.created", "HRRequest", hrRequest.Id.ToString(), cancellationToken);
        return Created($"/api/ess/hr-requests/{hrRequest.Id}", hrRequest);
    }

    [HttpGet("hr-requests/my")]
    public async Task<ActionResult<IReadOnlyCollection<HRRequest>>> MyHrRequests(CancellationToken cancellationToken)
    {
        var (tenantId, employeeId) = RequireEssRead();
        return Ok(await _db.HRRequests.AsNoTracking().Where(x => x.TenantId == tenantId && x.EmployeeId == employeeId).OrderByDescending(x => x.CreatedAtUtc).ToListAsync(cancellationToken));
    }

    [HttpPost("hr-requests/{id:guid}/comments")]
    public async Task<ActionResult<HRRequestComment>> AddHrRequestComment(Guid id, ESSCommentDto request, CancellationToken cancellationToken)
    {
        var (tenantId, employeeId) = RequireEssWrite();
        if (!await _db.HRRequests.AnyAsync(x => x.TenantId == tenantId && x.EmployeeId == employeeId && x.Id == id, cancellationToken)) return NotFound();
        var comment = new HRRequestComment { TenantId = tenantId, HRRequestId = id, EmployeeId = employeeId, UserId = GetUserId(), Comment = request.Comment };
        _db.HRRequestComments.Add(comment);
        await _db.SaveChangesAsync(cancellationToken);
        return Created($"/api/ess/hr-requests/{id}/comments/{comment.Id}", comment);
    }

    [HttpGet("announcements")]
    public async Task<ActionResult<IReadOnlyCollection<ESSAnnouncementDto>>> Announcements(CancellationToken cancellationToken)
    {
        var (tenantId, _) = RequireEssRead();
        var announcements = await ActiveAnnouncements(tenantId).ToListAsync(cancellationToken);
        return Ok(announcements.Select(ToAnnouncementDto).ToList());
    }

    [HttpGet("policies")]
    public async Task<ActionResult<IReadOnlyCollection<ESSDocumentDto>>> Policies(CancellationToken cancellationToken)
    {
        var (tenantId, employeeId) = RequireEssRead();
        return Ok(await _db.EmployeeDocuments.AsNoTracking()
            .Where(x => x.TenantId == tenantId && (x.EmployeeId == employeeId || x.DocumentType.Contains("Policy")) && !x.IsDeleted)
            .Select(x => new ESSDocumentDto(x.Id, x.DocumentType, x.FileName, x.ExpiryDate, x.ApprovalStatus))
            .ToListAsync(cancellationToken));
    }

    [HttpPost("policies/{id:guid}/acknowledge")]
    public async Task<ActionResult<EmployeePolicyAcknowledgement>> AcknowledgePolicy(Guid id, CancellationToken cancellationToken)
    {
        var (tenantId, employeeId) = RequireEssWrite();
        if (!await _db.EmployeeDocuments.AnyAsync(x => x.TenantId == tenantId && x.Id == id && !x.IsDeleted, cancellationToken)) return NotFound();
        var existing = await _db.EmployeePolicyAcknowledgements.FirstOrDefaultAsync(x => x.TenantId == tenantId && x.EmployeeId == employeeId && x.PolicyId == id, cancellationToken);
        if (existing is not null) return Ok(existing);
        var ack = new EmployeePolicyAcknowledgement { TenantId = tenantId, EmployeeId = employeeId, PolicyId = id, UserId = GetUserId() };
        _db.EmployeePolicyAcknowledgements.Add(ack);
        await _db.SaveChangesAsync(cancellationToken);
        await EssAudit(tenantId, employeeId, "ess.policy.acknowledged", "EmployeeDocument", id.ToString(), cancellationToken);
        return Created($"/api/ess/policies/{id}/acknowledgement", ack);
    }

    [HttpPost("ai/ask")]
    public async Task<ActionResult<ESSAIAnswerDto>> AskAi(ESSAIQuestionDto request, CancellationToken cancellationToken)
    {
        var (tenantId, employeeId) = RequireEssRead();
        var leaveAvailable = await _db.EmployeeLeaveBalances.Where(x => x.TenantId == tenantId && x.EmployeeId == employeeId && x.Year == DateTime.UtcNow.Year).SumAsync(x => x.Entitled + x.Accrued + x.CarriedForward + x.ManualAdjustment - x.Used - x.Pending - x.Encashed, cancellationToken);
        var openTickets = await _db.HRRequests.CountAsync(x => x.TenantId == tenantId && x.EmployeeId == employeeId && x.Status != "Closed", cancellationToken);
        var expiringDocs = await _db.EmployeeDocuments.CountAsync(x => x.TenantId == tenantId && x.EmployeeId == employeeId && !x.IsDeleted && x.ExpiryDate != null && x.ExpiryDate <= DateOnly.FromDateTime(DateTime.UtcNow.AddDays(60)), cancellationToken);
        var answer = $"I can only use your own Zayra employee data. Current snapshot: leave available {leaveAvailable:0.##} days, open HR requests {openTickets}, documents expiring in 60 days {expiringDocs}. I cannot approve, reject, or expose another employee's data.";
        _db.EmployeeAIQueryLogs.Add(new EmployeeAIQueryLog { TenantId = tenantId, EmployeeId = employeeId, Question = request.Question, Answer = answer, UserId = GetUserId() });
        await _db.SaveChangesAsync(cancellationToken);
        return Ok(new ESSAIAnswerDto(answer));
    }

    [HttpGet("notifications")]
    public async Task<ActionResult<IReadOnlyCollection<ESSNotificationDto>>> Notifications(CancellationToken cancellationToken)
    {
        var (tenantId, employeeId) = RequireEssRead();
        var notifications = await _db.EmployeeNotifications.AsNoTracking().Where(x => x.TenantId == tenantId && x.EmployeeId == employeeId).OrderByDescending(x => x.CreatedAtUtc).ToListAsync(cancellationToken);
        return Ok(notifications.Select(ToNotificationDto).ToList());
    }

    [HttpPatch("notifications/{id:guid}/read")]
    public async Task<IActionResult> MarkNotificationRead(Guid id, CancellationToken cancellationToken)
    {
        var (tenantId, employeeId) = RequireEssWrite();
        var notification = await _db.EmployeeNotifications.FirstOrDefaultAsync(x => x.TenantId == tenantId && x.EmployeeId == employeeId && x.Id == id, cancellationToken);
        if (notification is null) return NotFound();
        notification.IsRead = true;
        notification.ReadAtUtc = DateTime.UtcNow;
        await _db.SaveChangesAsync(cancellationToken);
        return NoContent();
    }

    private IQueryable<EmployeeAnnouncement> ActiveAnnouncements(Guid tenantId) =>
        _db.EmployeeAnnouncements.AsNoTracking().Where(x => x.TenantId == tenantId && x.IsActive && (x.ExpiresAtUtc == null || x.ExpiresAtUtc > DateTime.UtcNow)).OrderByDescending(x => x.PublishedAtUtc);

    private async Task<Employee?> OwnEmployee(Guid tenantId, int employeeId, CancellationToken cancellationToken) =>
        await _db.Employees.AsNoTracking().FirstOrDefaultAsync(x => x.TenantId == tenantId && x.Id == employeeId && !x.IsDeleted, cancellationToken);

    private (Guid TenantId, int EmployeeId) RequireEssRead()
    {
        var accessMode = User.FindFirstValue("access_mode") ?? string.Empty;
        if (accessMode is "NoLogin" or "KioskOnly") throw new UnauthorizedAccessException("This access mode cannot use ESS.");
        if (!HasPermission("ess.read") && !HasPermission("ess.write")) throw new UnauthorizedAccessException("ESS permission is required.");
        return (RequireTenantId(), RequireEmployeeId());
    }

    private (Guid TenantId, int EmployeeId) RequireEssWrite()
    {
        var data = RequireEssRead();
        if (!HasPermission("ess.write")) throw new UnauthorizedAccessException("ESS write permission is required.");
        return data;
    }

    private bool HasPermission(string permission) => User.Claims.Any(x => x.Type == "permission" && x.Value == permission);
    private Guid RequireTenantId() => Guid.Parse(User.FindFirstValue("tenant_id") ?? throw new UnauthorizedAccessException("Tenant claim is required."));
    private int RequireEmployeeId() => int.Parse(User.FindFirstValue("employee_id") ?? throw new UnauthorizedAccessException("Employee claim is required."));
    private Guid? GetUserId() => Guid.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier) ?? User.FindFirstValue("sub"), out var id) ? id : null;

    private async Task EssAudit(Guid tenantId, int employeeId, string action, string entityName, string entityId, CancellationToken cancellationToken)
    {
        _db.EmployeeSelfServiceAuditLogs.Add(new EmployeeSelfServiceAuditLog { TenantId = tenantId, EmployeeId = employeeId, Action = action, EntityName = entityName, EntityId = entityId, UserId = GetUserId() });
        await _db.SaveChangesAsync(cancellationToken);
    }

    private static ESSAnnouncementDto ToAnnouncementDto(EmployeeAnnouncement announcement) =>
        new(announcement.Id, announcement.Title, announcement.Body, announcement.Audience, announcement.PublishedAtUtc);

    private static ESSNotificationDto ToNotificationDto(EmployeeNotification notification) =>
        new(notification.Id, notification.Title, notification.Body, notification.NotificationType, notification.IsRead, notification.CreatedAtUtc);
}

public record ESSProfileSummaryDto(int EmployeeId, string EmployeeCode, string FullName, string JobTitle, string Department, string ProfilePhotoUrl, decimal ProfileCompletenessScore);
public record ESSLeaveBalanceDto(Guid LeaveTypeId, string LeaveTypeName, decimal Entitled, decimal Used, decimal Pending, decimal Available);
public record ESSDocumentDto(Guid Id, string DocumentType, string FileName, DateOnly? ExpiryDate, string ApprovalStatus);
public record ESSAnnouncementDto(Guid Id, string Title, string Body, string Audience, DateTime PublishedAtUtc);
public record ESSNotificationDto(Guid Id, string Title, string Body, string NotificationType, bool IsRead, DateTime CreatedAtUtc);
public record ESSActionItemDto(Guid Id, string Title, string Category, DateTime? DueAtUtc);
public record ESSDashboardDto(ESSProfileSummaryDto Profile, AttendanceDailyRecord? AttendanceToday, IReadOnlyCollection<ESSLeaveBalanceDto> LeaveBalances, int PendingRequests, IReadOnlyCollection<ESSDocumentDto> DocumentAlerts, IReadOnlyCollection<ESSAnnouncementDto> Announcements, IReadOnlyCollection<ESSNotificationDto> Notifications, IReadOnlyCollection<ESSActionItemDto> ActionItems);
public record ProfileChangeRequestDto(Dictionary<string, object?> Changes, string? Reason);
public record ESSAttendanceRegularizationDto(DateOnly WorkDate, string RequestType, DateTime? RequestedInUtc, DateTime? RequestedOutUtc, string Reason);
public record ESSLeaveRequestDto(Guid LeaveTypeId, DateOnly StartDate, DateOnly EndDate, string? DayType, string Reason);
public record ESSDocumentUploadDto(string DocumentType, string FileName, string ContentType, string StorageUrl, DateOnly? ExpiryDate, bool IsRequired);
public record ESSHRRequestCreateDto(Guid? CategoryId, string? CategoryName, string Subject, string Description, string? Priority);
public record ESSCommentDto(string Comment);
public record ESSAIQuestionDto(string Question);
public record ESSAIAnswerDto(string Answer);
