using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Zayra.Api.Application.Common;
using Zayra.Api.Application.Performance;
using Zayra.Api.Data;
using Zayra.Api.Models;

namespace Zayra.Api.Controllers.Performance;

[ApiController]
[Route("api/performance/cycles")]
[Authorize]
public class CyclesController : ControllerBase
{
    private readonly ZayraDbContext _db;
    private readonly IPerformanceService _svc;

    public CyclesController(ZayraDbContext db, IPerformanceService svc)
    { _db = db; _svc = svc; }

    [HttpGet]
    public async Task<IActionResult> List(
        [FromQuery] string? status,
        [FromQuery] string? type,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 25,
        CancellationToken ct = default)
    {
        var tenantId = this.GetTenantId()!.Value;
        var query = _db.PerformanceCycles.Where(c => c.TenantId == tenantId);
        if (!string.IsNullOrWhiteSpace(status)) query = query.Where(c => c.Status == status);
        if (!string.IsNullOrWhiteSpace(type))   query = query.Where(c => c.CycleType == type);

        var total = await query.CountAsync(ct);
        var items = await query
            .OrderByDescending(c => c.CreatedAtUtc)
            .Skip((page - 1) * pageSize).Take(pageSize)
            .ToListAsync(ct);

        // Enrich with enrollment counts
        var ids = items.Select(c => c.Id).ToList();
        var counts = await _db.PerformanceCycleEmployees
            .Where(e => e.TenantId == tenantId && ids.Contains(e.CycleId))
            .GroupBy(e => e.CycleId)
            .Select(g => new { CycleId = g.Key, Count = g.Count() })
            .ToListAsync(ct);
        var countMap = counts.ToDictionary(x => x.CycleId, x => x.Count);

        var enriched = items.Select(c => new
        {
            c.Id, c.Name, c.CycleType, c.ReviewPeriodStart, c.ReviewPeriodEnd, c.Status,
            c.EnableCalibration, c.Enable360Feedback, c.EnableSelfAssessment,
            c.SelfAssessmentDeadline, c.ManagerReviewDeadline, c.CalibrationDeadline,
            c.DefaultScorecardTemplateId, c.Notes, c.CreatedAtUtc, c.LaunchedAtUtc,
            c.PublishedAtUtc, c.ClosedAtUtc,
            EnrolledCount = countMap.GetValueOrDefault(c.Id, 0),
        });

        return Ok(new { items = enriched, total, page, pageSize });
    }

    [HttpGet("stats")]
    public async Task<IActionResult> Stats(CancellationToken ct)
    {
        var tenantId = this.GetTenantId()!.Value;
        var cycles = await _db.PerformanceCycles.Where(c => c.TenantId == tenantId).ToListAsync(ct);
        var reviews = await _db.AppraisalReviews.Where(r => r.TenantId == tenantId).ToListAsync(ct);
        var pips    = await _db.PerformanceImprovementPlans.Where(p => p.TenantId == tenantId && p.Status == "Active").CountAsync(ct);
        var probation = await _db.ProbationReviews.Where(p => p.TenantId == tenantId && p.Status == "Pending").CountAsync(ct);

        return Ok(new
        {
            activeCycles        = cycles.Count(c => c.Status == "Active"),
            inReviewCycles      = cycles.Count(c => c.Status == "InReview"),
            totalReviews        = reviews.Count,
            pendingSelfAssessment = reviews.Count(r => r.Status == "SelfAssessmentDue"),
            pendingManagerReview  = reviews.Count(r => r.Status == "ManagerReview"),
            published           = reviews.Count(r => r.Status == "Published"),
            activePips          = pips,
            pendingProbation    = probation,
        });
    }

    [HttpGet("{id:guid}")]
    public async Task<IActionResult> Get(Guid id, CancellationToken ct)
    {
        var tenantId = this.GetTenantId()!.Value;
        var cycle = await _db.PerformanceCycles
            .FirstOrDefaultAsync(c => c.Id == id && c.TenantId == tenantId, ct);
        if (cycle is null) return NotFound();

        var employees = await _db.PerformanceCycleEmployees
            .Where(e => e.TenantId == tenantId && e.CycleId == id)
            .ToListAsync(ct);

        var reviews = await _db.AppraisalReviews
            .Where(r => r.TenantId == tenantId && r.CycleId == id)
            .ToListAsync(ct);

        var statusCounts = reviews
            .GroupBy(r => r.Status)
            .ToDictionary(g => g.Key, g => g.Count());

        return Ok(new { cycle, employees, reviews, statusCounts });
    }

    [HttpPost]
    [Authorize(Roles = "Admin,HR Manager")]
    public async Task<IActionResult> Create([FromBody] CreateCycleRequest req, CancellationToken ct)
    {
        var tenantId = this.GetTenantId()!.Value;
        var userId   = this.GetUserId();

        var cycle = new PerformanceCycle
        {
            TenantId                  = tenantId,
            Name                      = req.Name,
            CycleType                 = req.CycleType,
            ReviewPeriodStart         = req.ReviewPeriodStart,
            ReviewPeriodEnd           = req.ReviewPeriodEnd,
            EnableCalibration         = req.EnableCalibration,
            Enable360Feedback         = req.Enable360Feedback,
            EnableSelfAssessment      = req.EnableSelfAssessment,
            EnableForcedDistribution  = req.EnableForcedDistribution,
            SelfAssessmentDeadline    = req.SelfAssessmentDeadline,
            ManagerReviewDeadline     = req.ManagerReviewDeadline,
            CalibrationDeadline       = req.CalibrationDeadline,
            DefaultScorecardTemplateId = req.DefaultScorecardTemplateId,
            Notes                     = req.Notes ?? string.Empty,
            CreatedByUserId           = userId,
        };
        _db.PerformanceCycles.Add(cycle);
        await _db.SaveChangesAsync(ct);
        return Created($"/api/performance/cycles/{cycle.Id}", cycle);
    }

    [HttpPut("{id:guid}")]
    [Authorize(Roles = "Admin,HR Manager")]
    public async Task<IActionResult> Update(Guid id, [FromBody] CreateCycleRequest req, CancellationToken ct)
    {
        var tenantId = this.GetTenantId()!.Value;
        var cycle = await _db.PerformanceCycles
            .FirstOrDefaultAsync(c => c.Id == id && c.TenantId == tenantId, ct);
        if (cycle is null) return NotFound();
        if (cycle.Status != "Draft") return BadRequest(new { message = "Only Draft cycles can be edited." });

        cycle.Name                      = req.Name;
        cycle.CycleType                 = req.CycleType;
        cycle.ReviewPeriodStart         = req.ReviewPeriodStart;
        cycle.ReviewPeriodEnd           = req.ReviewPeriodEnd;
        cycle.EnableCalibration         = req.EnableCalibration;
        cycle.Enable360Feedback         = req.Enable360Feedback;
        cycle.EnableSelfAssessment      = req.EnableSelfAssessment;
        cycle.EnableForcedDistribution  = req.EnableForcedDistribution;
        cycle.SelfAssessmentDeadline    = req.SelfAssessmentDeadline;
        cycle.ManagerReviewDeadline     = req.ManagerReviewDeadline;
        cycle.CalibrationDeadline       = req.CalibrationDeadline;
        cycle.DefaultScorecardTemplateId = req.DefaultScorecardTemplateId;
        cycle.Notes                     = req.Notes ?? string.Empty;
        await _db.SaveChangesAsync(ct);
        return Ok(cycle);
    }

    // ── Lifecycle actions ──────────────────────────────────────────────────────

    [HttpPost("{id:guid}/launch")]
    [Authorize(Roles = "Admin,HR Manager")]
    public async Task<IActionResult> Launch(Guid id, CancellationToken ct)
    {
        var tenantId = this.GetTenantId()!.Value;
        var cycle = await _db.PerformanceCycles
            .FirstOrDefaultAsync(c => c.Id == id && c.TenantId == tenantId, ct);
        if (cycle is null) return NotFound();
        if (cycle.Status != "Draft") return BadRequest(new { message = "Only Draft cycles can be launched." });
        if (cycle.DefaultScorecardTemplateId is null)
            return BadRequest(new { message = "A default scorecard template is required before launching." });

        cycle.Status         = "Active";
        cycle.LaunchedAtUtc  = DateTime.UtcNow;
        await _db.SaveChangesAsync(ct);

        var enrolled = await _svc.EnrollEmployeesAsync(tenantId, id, ct);
        return Ok(new { cycle, enrolledCount = enrolled });
    }

    [HttpPost("{id:guid}/advance")]
    [Authorize(Roles = "Admin,HR Manager")]
    public async Task<IActionResult> Advance(Guid id, CancellationToken ct)
    {
        var tenantId = this.GetTenantId()!.Value;
        var cycle = await _db.PerformanceCycles
            .FirstOrDefaultAsync(c => c.Id == id && c.TenantId == tenantId, ct);
        if (cycle is null) return NotFound();

        var next = cycle.Status switch
        {
            "Active"         => "InReview",
            "InReview"       => cycle.EnableCalibration ? "Calibration" : "FinalApproval",
            "Calibration"    => "FinalApproval",
            "FinalApproval"  => "Published",
            _                => null,
        };
        if (next is null) return BadRequest(new { message = $"Cannot advance from status {cycle.Status}." });

        cycle.Status = next;
        if (next == "Published") cycle.PublishedAtUtc = DateTime.UtcNow;
        await _db.SaveChangesAsync(ct);
        return Ok(cycle);
    }

    [HttpPost("{id:guid}/close")]
    [Authorize(Roles = "Admin,HR Manager")]
    public async Task<IActionResult> Close(Guid id, CancellationToken ct)
    {
        var tenantId = this.GetTenantId()!.Value;
        var cycle = await _db.PerformanceCycles
            .FirstOrDefaultAsync(c => c.Id == id && c.TenantId == tenantId, ct);
        if (cycle is null) return NotFound();

        cycle.Status      = "Closed";
        cycle.ClosedAtUtc = DateTime.UtcNow;
        await _db.SaveChangesAsync(ct);
        return Ok(cycle);
    }
}

// ── DTOs ───────────────────────────────────────────────────────────────────────

public record CreateCycleRequest(
    string Name, string CycleType,
    DateOnly ReviewPeriodStart, DateOnly ReviewPeriodEnd,
    bool EnableCalibration, bool Enable360Feedback,
    bool EnableSelfAssessment, bool EnableForcedDistribution,
    DateOnly? SelfAssessmentDeadline, DateOnly? ManagerReviewDeadline,
    DateOnly? CalibrationDeadline,
    Guid? DefaultScorecardTemplateId, string? Notes);
