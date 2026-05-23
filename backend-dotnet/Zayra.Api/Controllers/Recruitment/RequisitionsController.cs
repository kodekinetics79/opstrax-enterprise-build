using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Zayra.Api.Application.Common;
using Zayra.Api.Application.Recruitment;
using Zayra.Api.Data;
using Zayra.Api.Infrastructure.Notifications;
using Zayra.Api.Models;

namespace Zayra.Api.Controllers.Recruitment;

[ApiController]
[Route("api/recruitment/requisitions")]
[Authorize]
public class RequisitionsController : ControllerBase
{
    private readonly ZayraDbContext _db;
    private readonly IRecruitmentService _svc;
    private readonly INotificationService _notify;

    public RequisitionsController(ZayraDbContext db, IRecruitmentService svc, INotificationService notify)
    {
        _db = db;
        _svc = svc;
        _notify = notify;
    }

    [HttpGet]
    public async Task<IActionResult> List(
        [FromQuery] string? status,
        [FromQuery] string? priority,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 25,
        CancellationToken ct = default)
    {
        var tenantId = this.GetTenantId()!.Value;
        var query = _db.ManpowerRequisitions.Where(r => r.TenantId == tenantId);
        if (!string.IsNullOrWhiteSpace(status)) query = query.Where(r => r.Status == status);
        if (!string.IsNullOrWhiteSpace(priority)) query = query.Where(r => r.Priority == priority);

        var total = await query.CountAsync(ct);
        var items = await query.OrderByDescending(r => r.CreatedAtUtc).Skip((page - 1) * pageSize).Take(pageSize).ToListAsync(ct);
        return Ok(new { items, total, page, pageSize });
    }

    [HttpGet("{id:guid}")]
    public async Task<IActionResult> Get(Guid id, CancellationToken ct)
    {
        var tenantId = this.GetTenantId()!.Value;
        var r = await _db.ManpowerRequisitions.FirstOrDefaultAsync(x => x.Id == id && x.TenantId == tenantId, ct);
        return r is null ? NotFound() : Ok(r);
    }

    [HttpPost]
    [Authorize(Roles = "Admin,HR Manager,HR Officer,Manager")]
    public async Task<IActionResult> Create([FromBody] CreateRequisitionRequest req, CancellationToken ct)
    {
        var tenantId = this.GetTenantId()!.Value;
        var userId = this.GetUserId();
        var number = await _svc.GenerateRequisitionNumberAsync(tenantId, ct);

        var dept = req.DepartmentId.HasValue
            ? await _db.Departments.FirstOrDefaultAsync(d => d.Id == req.DepartmentId.Value && d.TenantId == tenantId, ct)
            : null;
        var desig = req.DesignationId.HasValue
            ? await _db.Designations.FirstOrDefaultAsync(d => d.Id == req.DesignationId.Value && d.TenantId == tenantId, ct)
            : null;

        var r = new ManpowerRequisition
        {
            TenantId = tenantId,
            RequisitionNumber = number,
            DepartmentId = req.DepartmentId,
            DepartmentName = dept?.NameEn ?? req.DepartmentName,
            DesignationId = req.DesignationId,
            DesignationTitle = desig?.TitleEn ?? req.DesignationTitle,
            HeadCount = req.HeadCount,
            EmploymentType = req.EmploymentType,
            Priority = req.Priority,
            Justification = req.Justification,
            RequiredSkills = req.RequiredSkills,
            MinExperienceYears = req.MinExperienceYears,
            MaxExperienceYears = req.MaxExperienceYears,
            BudgetFrom = req.BudgetFrom,
            BudgetTo = req.BudgetTo,
            TargetJoiningDate = req.TargetJoiningDate,
            RequestedByUserId = userId,
            RequestedByName = req.RequestedByName,
        };
        _db.ManpowerRequisitions.Add(r);
        await _db.SaveChangesAsync(ct);
        return Created($"/api/recruitment/requisitions/{r.Id}", r);
    }

    [HttpPut("{id:guid}")]
    [Authorize(Roles = "Admin,HR Manager,HR Officer,Manager")]
    public async Task<IActionResult> Update(Guid id, [FromBody] CreateRequisitionRequest req, CancellationToken ct)
    {
        var tenantId = this.GetTenantId()!.Value;
        var r = await _db.ManpowerRequisitions.FirstOrDefaultAsync(x => x.Id == id && x.TenantId == tenantId, ct);
        if (r is null) return NotFound();
        if (r.Status is not ("Draft" or "Rejected")) return BadRequest(new { message = "Only Draft or Rejected requisitions can be edited." });

        r.DepartmentName = req.DepartmentName;
        r.DesignationTitle = req.DesignationTitle;
        r.HeadCount = req.HeadCount;
        r.EmploymentType = req.EmploymentType;
        r.Priority = req.Priority;
        r.Justification = req.Justification;
        r.RequiredSkills = req.RequiredSkills;
        r.MinExperienceYears = req.MinExperienceYears;
        r.MaxExperienceYears = req.MaxExperienceYears;
        r.BudgetFrom = req.BudgetFrom;
        r.BudgetTo = req.BudgetTo;
        r.TargetJoiningDate = req.TargetJoiningDate;
        r.Status = "Draft";
        r.RejectionReason = string.Empty;
        await _db.SaveChangesAsync(ct);
        return Ok(r);
    }

    [HttpPost("{id:guid}/submit")]
    [Authorize(Roles = "Admin,HR Manager,HR Officer,Manager")]
    public async Task<IActionResult> Submit(Guid id, CancellationToken ct)
    {
        var tenantId = this.GetTenantId()!.Value;
        var userId = this.GetUserId();
        var r = await _db.ManpowerRequisitions.FirstOrDefaultAsync(x => x.Id == id && x.TenantId == tenantId, ct);
        if (r is null) return NotFound();
        if (r.Status != "Draft") return BadRequest(new { message = "Only Draft requisitions can be submitted." });

        var approvalId = await _svc.CreateApprovalRequestAsync(
            tenantId, "ManpowerRequisition", id,
            $"Manpower Requisition {r.RequisitionNumber} — {r.DesignationTitle} × {r.HeadCount}",
            userId, ct);

        r.Status = approvalId.HasValue ? "PendingApproval" : "Submitted";
        r.SubmittedAtUtc = DateTime.UtcNow;
        r.ApprovalRequestId = approvalId;
        await _db.SaveChangesAsync(ct);

        await _notify.NotifyAsync(tenantId, null,
            "Requisition Submitted",
            $"{r.RequisitionNumber} — {r.DesignationTitle} × {r.HeadCount} has been submitted for approval.",
            "ManpowerRequisition", r.Id.ToString(), ct);

        return Ok(r);
    }

    [HttpPost("{id:guid}/approve")]
    [Authorize(Roles = "Admin,HR Manager")]
    public async Task<IActionResult> Approve(Guid id, [FromBody] DecisionRequest req, CancellationToken ct)
    {
        var tenantId = this.GetTenantId()!.Value;
        var r = await _db.ManpowerRequisitions.FirstOrDefaultAsync(x => x.Id == id && x.TenantId == tenantId, ct);
        if (r is null) return NotFound();
        if (r.Status is not ("Submitted" or "PendingApproval")) return BadRequest(new { message = "Requisition is not pending approval." });

        r.Status = "Approved";
        r.ApprovedAtUtc = DateTime.UtcNow;
        await _db.SaveChangesAsync(ct);

        await _notify.NotifyAsync(tenantId, r.RequestedByUserId,
            "Requisition Approved",
            $"{r.RequisitionNumber} has been approved. HR can now create a job opening.",
            "ManpowerRequisition", r.Id.ToString(), ct);

        return Ok(r);
    }

    [HttpPost("{id:guid}/reject")]
    [Authorize(Roles = "Admin,HR Manager")]
    public async Task<IActionResult> Reject(Guid id, [FromBody] DecisionRequest req, CancellationToken ct)
    {
        var tenantId = this.GetTenantId()!.Value;
        var r = await _db.ManpowerRequisitions.FirstOrDefaultAsync(x => x.Id == id && x.TenantId == tenantId, ct);
        if (r is null) return NotFound();
        if (r.Status is not ("Submitted" or "PendingApproval")) return BadRequest(new { message = "Requisition is not pending approval." });

        r.Status = "Rejected";
        r.RejectionReason = req.Reason ?? string.Empty;
        r.RejectedAtUtc = DateTime.UtcNow;
        await _db.SaveChangesAsync(ct);

        await _notify.NotifyAsync(tenantId, r.RequestedByUserId,
            "Requisition Rejected",
            $"{r.RequisitionNumber} was rejected. Reason: {r.RejectionReason}",
            "ManpowerRequisition", r.Id.ToString(), ct);

        return Ok(r);
    }

    [HttpGet("stats")]
    public async Task<IActionResult> Stats(CancellationToken ct)
    {
        var tenantId = this.GetTenantId()!.Value;
        var all = await _db.ManpowerRequisitions.Where(r => r.TenantId == tenantId).ToListAsync(ct);
        return Ok(new
        {
            total = all.Count,
            draft = all.Count(r => r.Status == "Draft"),
            pending = all.Count(r => r.Status is "Submitted" or "PendingApproval"),
            approved = all.Count(r => r.Status == "Approved"),
            converted = all.Count(r => r.Status == "Converted"),
        });
    }
}

public record CreateRequisitionRequest(
    Guid? DepartmentId, string DepartmentName, Guid? DesignationId, string DesignationTitle,
    int HeadCount, string EmploymentType, string Priority, string Justification,
    string RequiredSkills, int? MinExperienceYears, int? MaxExperienceYears,
    decimal? BudgetFrom, decimal? BudgetTo, DateOnly? TargetJoiningDate, string RequestedByName);

public record DecisionRequest(string? Reason, string? Comments);
