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
[Route("api/recruitment/openings")]
[Authorize]
public class OpeningsController : ControllerBase
{
    private readonly ZayraDbContext _db;
    private readonly IRecruitmentService _svc;
    private readonly INotificationService _notify;

    public OpeningsController(ZayraDbContext db, IRecruitmentService svc, INotificationService notify)
    {
        _db = db; _svc = svc; _notify = notify;
    }

    [HttpGet]
    public async Task<IActionResult> List(
        [FromQuery] string? status,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 25,
        CancellationToken ct = default)
    {
        var tenantId = this.GetTenantId()!.Value;
        var query = _db.JobOpenings.Where(j => j.TenantId == tenantId);
        if (!string.IsNullOrWhiteSpace(status)) query = query.Where(j => j.Status == status);

        var total = await query.CountAsync(ct);
        var items = await query.OrderByDescending(j => j.CreatedAtUtc).Skip((page - 1) * pageSize).Take(pageSize).ToListAsync(ct);

        // Enrich with application counts per opening
        var ids = items.Select(j => j.Id).ToList();
        var counts = await _db.JobApplications
            .Where(a => a.TenantId == tenantId && ids.Contains(a.JobOpeningId) && a.Status == "Active")
            .GroupBy(a => a.JobOpeningId)
            .Select(g => new { JobOpeningId = g.Key, Count = g.Count() })
            .ToListAsync(ct);
        var countMap = counts.ToDictionary(c => c.JobOpeningId, c => c.Count);

        var enriched = items.Select(j => new
        {
            j.Id, j.JobCode, j.RequisitionId, j.Title, j.DepartmentName, j.DesignationTitle,
            j.EmploymentType, j.HeadCount, j.FilledCount, j.SalaryFrom, j.SalaryTo, j.Location,
            j.Status, j.AssignedHrName, j.CreatedAtUtc, j.PublishedAtUtc,
            ActiveApplications = countMap.GetValueOrDefault(j.Id, 0),
            Remaining = j.HeadCount - j.FilledCount,
        });

        return Ok(new { items = enriched, total, page, pageSize });
    }

    [HttpGet("{id:guid}")]
    public async Task<IActionResult> Get(Guid id, CancellationToken ct)
    {
        var tenantId = this.GetTenantId()!.Value;
        var j = await _db.JobOpenings.FirstOrDefaultAsync(x => x.Id == id && x.TenantId == tenantId, ct);
        if (j is null) return NotFound();

        var stageCounts = await _db.JobApplications
            .Where(a => a.TenantId == tenantId && a.JobOpeningId == id && a.Status == "Active")
            .GroupBy(a => a.Stage)
            .Select(g => new { Stage = g.Key, Count = g.Count() })
            .ToListAsync(ct);

        return Ok(new { opening = j, stageCounts });
    }

    [HttpPost]
    [Authorize(Roles = "Admin,HR Manager,HR Officer")]
    public async Task<IActionResult> Create([FromBody] CreateOpeningRequest req, CancellationToken ct)
    {
        var tenantId = this.GetTenantId()!.Value;
        var userId = this.GetUserId();
        var code = await _svc.GenerateJobCodeAsync(tenantId, ct);

        // Mark source requisition as Converted
        if (req.RequisitionId.HasValue)
        {
            var requisition = await _db.ManpowerRequisitions
                .FirstOrDefaultAsync(r => r.Id == req.RequisitionId && r.TenantId == tenantId, ct);
            if (requisition is not null && requisition.Status == "Approved")
            {
                requisition.Status = "Converted";
                await _db.SaveChangesAsync(ct);
            }
        }

        var j = new JobOpening
        {
            TenantId = tenantId,
            JobCode = code,
            RequisitionId = req.RequisitionId,
            Title = req.Title,
            DepartmentName = req.DepartmentName,
            DesignationTitle = req.DesignationTitle,
            EmploymentType = req.EmploymentType,
            HeadCount = req.HeadCount,
            Description = req.Description,
            Requirements = req.Requirements,
            Responsibilities = req.Responsibilities,
            SalaryFrom = req.SalaryFrom,
            SalaryTo = req.SalaryTo,
            Location = req.Location,
            Status = "Open",
            AssignedHrUserId = userId,
            AssignedHrName = req.AssignedHrName,
            PublishedAtUtc = DateTime.UtcNow,
        };
        _db.JobOpenings.Add(j);
        await _db.SaveChangesAsync(ct);
        return Created($"/api/recruitment/openings/{j.Id}", j);
    }

    [HttpPut("{id:guid}")]
    [Authorize(Roles = "Admin,HR Manager,HR Officer")]
    public async Task<IActionResult> Update(Guid id, [FromBody] CreateOpeningRequest req, CancellationToken ct)
    {
        var tenantId = this.GetTenantId()!.Value;
        var j = await _db.JobOpenings.FirstOrDefaultAsync(x => x.Id == id && x.TenantId == tenantId, ct);
        if (j is null) return NotFound();

        j.Title = req.Title;
        j.DepartmentName = req.DepartmentName;
        j.DesignationTitle = req.DesignationTitle;
        j.EmploymentType = req.EmploymentType;
        j.HeadCount = req.HeadCount;
        j.Description = req.Description;
        j.Requirements = req.Requirements;
        j.Responsibilities = req.Responsibilities;
        j.SalaryFrom = req.SalaryFrom;
        j.SalaryTo = req.SalaryTo;
        j.Location = req.Location;
        j.AssignedHrName = req.AssignedHrName;
        await _db.SaveChangesAsync(ct);
        return Ok(j);
    }

    [HttpPost("{id:guid}/close")]
    [Authorize(Roles = "Admin,HR Manager")]
    public async Task<IActionResult> Close(Guid id, CancellationToken ct)
    {
        var tenantId = this.GetTenantId()!.Value;
        var j = await _db.JobOpenings.FirstOrDefaultAsync(x => x.Id == id && x.TenantId == tenantId, ct);
        if (j is null) return NotFound();
        j.Status = "Closed";
        j.ClosedAtUtc = DateTime.UtcNow;
        await _db.SaveChangesAsync(ct);
        return Ok(j);
    }

    [HttpGet("stats")]
    public async Task<IActionResult> Stats(CancellationToken ct)
    {
        var tenantId = this.GetTenantId()!.Value;
        var openings = await _db.JobOpenings.Where(j => j.TenantId == tenantId).ToListAsync(ct);
        var apps = await _db.JobApplications.Where(a => a.TenantId == tenantId).ToListAsync(ct);
        return Ok(new
        {
            openPositions = openings.Where(j => j.Status is "Open" or "InProgress").Sum(j => j.HeadCount - j.FilledCount),
            totalOpenings = openings.Count(j => j.Status is "Open" or "InProgress"),
            activeApplications = apps.Count(a => a.Status == "Active"),
            hiredThisMonth = apps.Count(a => a.Status == "Hired" && a.HiredAtUtc >= DateTime.UtcNow.AddDays(-30)),
            offersPending = apps.Count(a => a.Stage == "Offer" && a.Status == "Active"),
        });
    }
}

public record CreateOpeningRequest(
    Guid? RequisitionId, string Title, string DepartmentName, string DesignationTitle,
    string EmploymentType, int HeadCount, string Description, string Requirements,
    string Responsibilities, decimal? SalaryFrom, decimal? SalaryTo,
    string Location, string AssignedHrName);
