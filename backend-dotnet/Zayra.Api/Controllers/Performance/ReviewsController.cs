using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Zayra.Api.Application.Common;
using Zayra.Api.Application.Performance;
using Zayra.Api.Data;
using Zayra.Api.Models;

namespace Zayra.Api.Controllers.Performance;

[ApiController]
[Route("api/performance/reviews")]
[Authorize]
public class ReviewsController : ControllerBase
{
    private readonly ZayraDbContext _db;
    private readonly IPerformanceService _svc;
    private readonly IDataScopeService _scopeService;

    public ReviewsController(ZayraDbContext db, IPerformanceService svc, IDataScopeService scopeService)
    {
        _db = db;
        _svc = svc;
        _scopeService = scopeService;
    }

    [HttpGet]
    public async Task<IActionResult> List(
        [FromQuery] Guid? cycleId,
        [FromQuery] int? employeeId,
        [FromQuery] string? status,
        [FromQuery] string? department,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 50,
        CancellationToken ct = default)
    {
        var tenantId = this.GetTenantId()!.Value;
        var scope = await _scopeService.ResolveAsync(User, tenantId, ct);
        var query = _db.AppraisalReviews.Where(r => r.TenantId == tenantId);
        if (!scope.IsUnrestricted)
            query = query.Where(r => scope.AllowedEmployeeIds!.Contains(r.EmployeeId));
        if (cycleId.HasValue)    query = query.Where(r => r.CycleId == cycleId.Value);
        if (employeeId.HasValue) query = query.Where(r => r.EmployeeId == employeeId.Value);
        if (!string.IsNullOrWhiteSpace(status))     query = query.Where(r => r.Status == status);
        if (!string.IsNullOrWhiteSpace(department)) query = query.Where(r => r.DepartmentName == department);

        var total = await query.CountAsync(ct);
        var items = await query
            .OrderByDescending(r => r.CreatedAtUtc)
            .Skip((page - 1) * pageSize).Take(pageSize)
            .ToListAsync(ct);
        return Ok(new { items, total, page, pageSize });
    }

    [HttpGet("{id:guid}")]
    public async Task<IActionResult> Get(Guid id, CancellationToken ct)
    {
        var tenantId = this.GetTenantId()!.Value;
        var review = await _db.AppraisalReviews
            .FirstOrDefaultAsync(r => r.Id == id && r.TenantId == tenantId, ct);
        if (review is null) return NotFound();

        var template = await _db.PerformanceScorecardTemplates
            .FirstOrDefaultAsync(t => t.Id == review.ScorecardTemplateId && t.TenantId == tenantId, ct);

        var breakdown = await _db.AppraisalScoreBreakdowns
            .Where(b => b.TenantId == tenantId && b.ReviewId == id)
            .ToListAsync(ct);

        var competencies = await _db.AppraisalCompetencyRatings
            .Where(c => c.TenantId == tenantId && c.ReviewId == id)
            .ToListAsync(ct);

        var goals = await _db.EmployeeGoals
            .Where(g => g.TenantId == tenantId && g.EmployeeId == review.EmployeeId && g.CycleId == review.CycleId)
            .ToListAsync(ct);

        var feedback360 = await _db.Feedback360
            .Where(f => f.TenantId == tenantId && f.ReviewId == id)
            .ToListAsync(ct);

        var auditLog = await _db.PerformanceAuditLogs
            .Where(a => a.TenantId == tenantId && a.EntityType == "AppraisalReview" && a.EntityId == id.ToString())
            .OrderByDescending(a => a.CreatedAtUtc)
            .Take(50)
            .ToListAsync(ct);

        var calibration = await _db.AppraisalCalibrations
            .Where(c => c.TenantId == tenantId && c.ReviewId == id)
            .OrderByDescending(c => c.CalibratedAtUtc)
            .FirstOrDefaultAsync(ct);

        var appeal = await _db.AppraisalAppeals
            .Where(a => a.TenantId == tenantId && a.ReviewId == id)
            .FirstOrDefaultAsync(ct);

        return Ok(new { review, template, breakdown, competencies, goals, feedback360, auditLog, calibration, appeal });
    }

    // ── Self-assessment ────────────────────────────────────────────────────────

    [HttpPost("{id:guid}/self-assessment")]
    public async Task<IActionResult> SubmitSelfAssessment(
        Guid id, [FromBody] SelfAssessmentRequest req, CancellationToken ct)
    {
        var tenantId = this.GetTenantId()!.Value;
        var userId   = this.GetUserId();
        var review = await _db.AppraisalReviews
            .FirstOrDefaultAsync(r => r.Id == id && r.TenantId == tenantId, ct);
        if (review is null) return NotFound();
        if (review.Status != "SelfAssessmentDue")
            return BadRequest(new { message = "Review is not in Self-Assessment stage." });

        var old = review.SelfAssessmentNotes;
        review.SelfAssessmentNotes         = req.Notes;
        review.KpiScore                    = req.KpiScore;
        review.CompetencyScore             = req.CompetencyScore;
        review.ProductivityScore           = req.ProductivityScore;
        review.Status                      = "SelfAssessmentSubmitted";
        review.SelfAssessmentSubmittedAt   = DateTime.UtcNow;
        review.UpdatedAtUtc                = DateTime.UtcNow;

        // Upsert competency ratings
        if (req.CompetencyRatings is not null)
        {
            foreach (var cr in req.CompetencyRatings)
            {
                var existing = await _db.AppraisalCompetencyRatings
                    .FirstOrDefaultAsync(x => x.ReviewId == id && x.CompetencyId == cr.CompetencyId, ct);
                if (existing is null)
                {
                    _db.AppraisalCompetencyRatings.Add(new AppraisalCompetencyRating
                    {
                        TenantId = tenantId, ReviewId = id, CompetencyId = cr.CompetencyId,
                        CompetencyName = cr.CompetencyName, CompetencyCategory = cr.CompetencyCategory,
                        SelfRating = cr.Rating, SelfComments = cr.Comments ?? string.Empty,
                        Weight = cr.Weight,
                    });
                }
                else
                {
                    existing.SelfRating   = cr.Rating;
                    existing.SelfComments = cr.Comments ?? string.Empty;
                }
            }
        }

        await _svc.LogAuditAsync(tenantId, "AppraisalReview", id.ToString(),
            "SelfAssessmentSubmitted", old, req.Notes, string.Empty, userId, "Employee", ct);

        await _db.SaveChangesAsync(ct);
        return Ok(review);
    }

    // ── Manager review ─────────────────────────────────────────────────────────

    [HttpPost("{id:guid}/manager-review")]
    [Authorize(Roles = "Admin,HR Manager,Manager")]
    public async Task<IActionResult> SubmitManagerReview(
        Guid id, [FromBody] ManagerReviewRequest req, CancellationToken ct)
    {
        var tenantId = this.GetTenantId()!.Value;
        var userId   = this.GetUserId();
        var review = await _db.AppraisalReviews
            .FirstOrDefaultAsync(r => r.Id == id && r.TenantId == tenantId, ct);
        if (review is null) return NotFound();

        var oldScore = review.FinalScore.ToString("F2");

        review.KpiScore          = req.KpiScore;
        review.CompetencyScore   = req.CompetencyScore;
        review.AttendanceScore   = req.AttendanceScore;
        review.ProductivityScore = req.ProductivityScore;
        review.FeedbackScore     = req.FeedbackScore;
        review.DisciplineScore   = req.DisciplineScore;
        review.ManagerNotes      = req.ManagerNotes ?? string.Empty;
        review.ReviewerManagerId = req.ReviewerManagerId;
        review.ReviewerManagerName = req.ReviewerManagerName ?? string.Empty;
        review.Status            = "ManagerReviewComplete";
        review.ManagerReviewedAt = DateTime.UtcNow;
        review.UpdatedAtUtc      = DateTime.UtcNow;

        // Update competency manager ratings
        if (req.CompetencyRatings is not null)
        {
            foreach (var cr in req.CompetencyRatings)
            {
                var existing = await _db.AppraisalCompetencyRatings
                    .FirstOrDefaultAsync(x => x.ReviewId == id && x.CompetencyId == cr.CompetencyId, ct);
                if (existing is null)
                {
                    _db.AppraisalCompetencyRatings.Add(new AppraisalCompetencyRating
                    {
                        TenantId = tenantId, ReviewId = id, CompetencyId = cr.CompetencyId,
                        CompetencyName = cr.CompetencyName, CompetencyCategory = cr.CompetencyCategory,
                        ManagerRating = cr.Rating, ManagerComments = cr.Comments ?? string.Empty,
                        Weight = cr.Weight,
                    });
                }
                else
                {
                    existing.ManagerRating   = cr.Rating;
                    existing.ManagerComments = cr.Comments ?? string.Empty;
                }
            }
        }

        var newScore = await _svc.CalculateAndSaveFinalScoreAsync(tenantId, id, ct);

        await _svc.LogAuditAsync(tenantId, "AppraisalReview", id.ToString(),
            "ManagerReviewSubmitted", $"Score:{oldScore}", $"Score:{newScore}",
            req.ManagerNotes ?? string.Empty, userId, req.ReviewerManagerName ?? "Manager", ct);

        return Ok(review);
    }

    // ── Score override (HR/Admin) ──────────────────────────────────────────────

    [HttpPost("{id:guid}/override-score")]
    [Authorize(Roles = "Admin,HR Manager")]
    public async Task<IActionResult> OverrideScore(
        Guid id, [FromBody] ScoreOverrideRequest req, CancellationToken ct)
    {
        var tenantId = this.GetTenantId()!.Value;
        var userId   = this.GetUserId();
        var review = await _db.AppraisalReviews
            .FirstOrDefaultAsync(r => r.Id == id && r.TenantId == tenantId, ct);
        if (review is null) return NotFound();
        if (string.IsNullOrWhiteSpace(req.Reason))
            return BadRequest(new { message = "A reason is required for any score override." });

        var old = $"KPI:{review.KpiScore},Comp:{review.CompetencyScore},Att:{review.AttendanceScore}," +
                  $"Prod:{review.ProductivityScore},FB:{review.FeedbackScore},Disc:{review.DisciplineScore}";

        review.KpiScore          = req.KpiScore          ?? review.KpiScore;
        review.CompetencyScore   = req.CompetencyScore   ?? review.CompetencyScore;
        review.AttendanceScore   = req.AttendanceScore   ?? review.AttendanceScore;
        review.ProductivityScore = req.ProductivityScore ?? review.ProductivityScore;
        review.FeedbackScore     = req.FeedbackScore     ?? review.FeedbackScore;
        review.DisciplineScore   = req.DisciplineScore   ?? review.DisciplineScore;
        review.HrNotes           = (review.HrNotes + "\n" + req.Reason).Trim();
        review.UpdatedAtUtc      = DateTime.UtcNow;

        var newScore = await _svc.CalculateAndSaveFinalScoreAsync(tenantId, id, ct);

        await _svc.LogAuditAsync(tenantId, "AppraisalReview", id.ToString(),
            "ScoreOverride", old, $"FinalScore:{newScore}", req.Reason, userId, "HR", ct);

        return Ok(review);
    }

    // ── Publish ────────────────────────────────────────────────────────────────

    [HttpPost("{id:guid}/publish")]
    [Authorize(Roles = "Admin,HR Manager")]
    public async Task<IActionResult> Publish(Guid id, CancellationToken ct)
    {
        var tenantId = this.GetTenantId()!.Value;
        var userId   = this.GetUserId();
        var review = await _db.AppraisalReviews
            .FirstOrDefaultAsync(r => r.Id == id && r.TenantId == tenantId, ct);
        if (review is null) return NotFound();

        review.Status      = "Published";
        review.PublishedAt = DateTime.UtcNow;
        review.UpdatedAtUtc = DateTime.UtcNow;
        await _svc.LogAuditAsync(tenantId, "AppraisalReview", id.ToString(),
            "Published", review.Status, "Published", string.Empty, userId, "HR", ct);
        await _db.SaveChangesAsync(ct);
        return Ok(review);
    }

    // ── Employee acknowledgement ────────────────────────────────────────────────

    [HttpPost("{id:guid}/acknowledge")]
    public async Task<IActionResult> Acknowledge(Guid id, CancellationToken ct)
    {
        var tenantId = this.GetTenantId()!.Value;
        var userId   = this.GetUserId();
        var review = await _db.AppraisalReviews
            .FirstOrDefaultAsync(r => r.Id == id && r.TenantId == tenantId, ct);
        if (review is null) return NotFound();
        if (review.Status != "Published")
            return BadRequest(new { message = "Review must be Published before acknowledgement." });

        review.Status          = "Acknowledged";
        review.AcknowledgedAt  = DateTime.UtcNow;
        review.UpdatedAtUtc    = DateTime.UtcNow;
        await _svc.LogAuditAsync(tenantId, "AppraisalReview", id.ToString(),
            "Acknowledged", "Published", "Acknowledged", string.Empty, userId, "Employee", ct);
        await _db.SaveChangesAsync(ct);
        return Ok(review);
    }

    // ── Appeal ─────────────────────────────────────────────────────────────────

    [HttpPost("{id:guid}/appeal")]
    public async Task<IActionResult> SubmitAppeal(Guid id, [FromBody] AppealRequest req, CancellationToken ct)
    {
        var tenantId = this.GetTenantId()!.Value;
        var userId   = this.GetUserId();
        var review = await _db.AppraisalReviews
            .FirstOrDefaultAsync(r => r.Id == id && r.TenantId == tenantId, ct);
        if (review is null) return NotFound();
        if (review.Status is not ("Published" or "Acknowledged"))
            return BadRequest(new { message = "Appeals can only be submitted after results are published." });

        var appeal = new AppraisalAppeal
        {
            TenantId              = tenantId,
            ReviewId              = id,
            EmployeeId            = review.EmployeeId,
            EmployeeName          = review.EmployeeName,
            AppealReason          = req.AppealReason,
            EmployeeJustification = req.Justification ?? string.Empty,
        };
        _db.AppraisalAppeals.Add(appeal);
        review.IsAppealed   = true;
        review.Status       = "Appealed";
        review.UpdatedAtUtc = DateTime.UtcNow;
        await _db.SaveChangesAsync(ct);
        return Created($"/api/performance/reviews/{id}/appeal", appeal);
    }

    [HttpPost("appeals/{appealId:guid}/respond")]
    [Authorize(Roles = "Admin,HR Manager")]
    public async Task<IActionResult> RespondToAppeal(
        Guid appealId, [FromBody] AppealResponseRequest req, CancellationToken ct)
    {
        var tenantId = this.GetTenantId()!.Value;
        var userId   = this.GetUserId();
        var appeal = await _db.AppraisalAppeals
            .FirstOrDefaultAsync(a => a.Id == appealId && a.TenantId == tenantId, ct);
        if (appeal is null) return NotFound();

        appeal.Status           = req.Decision; // Upheld/Rejected
        appeal.HrResponse       = req.Response;
        appeal.ReviewedByUserId = userId;
        appeal.ReviewedAt       = DateTime.UtcNow;
        await _db.SaveChangesAsync(ct);
        return Ok(appeal);
    }

    // ── Compute attendance score ────────────────────────────────────────────────

    [HttpPost("{id:guid}/compute-attendance")]
    [Authorize(Roles = "Admin,HR Manager")]
    public async Task<IActionResult> ComputeAttendance(Guid id, CancellationToken ct)
    {
        var tenantId = this.GetTenantId()!.Value;
        var review = await _db.AppraisalReviews
            .FirstOrDefaultAsync(r => r.Id == id && r.TenantId == tenantId, ct);
        if (review is null) return NotFound();

        var cycle = await _db.PerformanceCycles
            .FirstOrDefaultAsync(c => c.Id == review.CycleId && c.TenantId == tenantId, ct);
        if (cycle is null) return NotFound();

        var score = await _svc.ComputeAttendanceScoreAsync(
            tenantId, review.EmployeeId, cycle.ReviewPeriodStart, cycle.ReviewPeriodEnd, ct);

        review.AttendanceScore = score;
        review.UpdatedAtUtc    = DateTime.UtcNow;
        await _db.SaveChangesAsync(ct);
        return Ok(new { attendanceScore = score });
    }
}

// ── DTOs ───────────────────────────────────────────────────────────────────────

public record CompetencyRatingDto(
    Guid CompetencyId, string CompetencyName, string CompetencyCategory,
    decimal Rating, string? Comments, decimal Weight);

public record SelfAssessmentRequest(
    string Notes, decimal KpiScore, decimal CompetencyScore, decimal ProductivityScore,
    List<CompetencyRatingDto>? CompetencyRatings);

public record ManagerReviewRequest(
    decimal KpiScore, decimal CompetencyScore, decimal AttendanceScore,
    decimal ProductivityScore, decimal FeedbackScore, decimal DisciplineScore,
    string? ManagerNotes, int? ReviewerManagerId, string? ReviewerManagerName,
    List<CompetencyRatingDto>? CompetencyRatings);

public record ScoreOverrideRequest(
    decimal? KpiScore, decimal? CompetencyScore, decimal? AttendanceScore,
    decimal? ProductivityScore, decimal? FeedbackScore, decimal? DisciplineScore,
    string Reason);

public record AppealRequest(string AppealReason, string? Justification);

public record AppealResponseRequest(string Decision, string Response);
