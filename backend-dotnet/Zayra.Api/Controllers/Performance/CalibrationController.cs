using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Zayra.Api.Application.Common;
using Zayra.Api.Application.Performance;
using Zayra.Api.Data;
using Zayra.Api.Models;

namespace Zayra.Api.Controllers.Performance;

[ApiController]
[Route("api/performance/calibration")]
[Authorize(Roles = "Admin,HR Manager")]
public class CalibrationController : ControllerBase
{
    private readonly ZayraDbContext _db;
    private readonly IPerformanceService _svc;

    public CalibrationController(ZayraDbContext db, IPerformanceService svc)
    { _db = db; _svc = svc; }

    [HttpGet("{cycleId:guid}")]
    public async Task<IActionResult> GetBoard(
        Guid cycleId,
        [FromQuery] string? department,
        CancellationToken ct)
    {
        var tenantId = this.GetTenantId()!.Value;

        var query = _db.AppraisalReviews.Where(r => r.TenantId == tenantId && r.CycleId == cycleId);
        if (!string.IsNullOrWhiteSpace(department)) query = query.Where(r => r.DepartmentName == department);

        var reviews = await query
            .OrderByDescending(r => r.FinalScore)
            .ToListAsync(ct);

        var calibrations = await _db.AppraisalCalibrations
            .Where(c => c.TenantId == tenantId && c.CycleId == cycleId)
            .ToListAsync(ct);
        var calibMap = calibrations.ToDictionary(c => c.ReviewId);

        // Distribution bands
        var distribution = new Dictionary<string, int>
        {
            ["Outstanding"]           = reviews.Count(r => r.FinalScore >= 90),
            ["Exceeds Expectations"]  = reviews.Count(r => r.FinalScore >= 75 && r.FinalScore < 90),
            ["Meets Expectations"]    = reviews.Count(r => r.FinalScore >= 60 && r.FinalScore < 75),
            ["Developing"]            = reviews.Count(r => r.FinalScore >= 45 && r.FinalScore < 60),
            ["Unsatisfactory"]        = reviews.Count(r => r.FinalScore < 45),
        };

        // Detect manager bias: managers with avg > 85 or < 50
        var managerStats = reviews
            .Where(r => r.ReviewerManagerId.HasValue)
            .GroupBy(r => new { r.ReviewerManagerId, r.ReviewerManagerName })
            .Select(g => new
            {
                ManagerId   = g.Key.ReviewerManagerId,
                ManagerName = g.Key.ReviewerManagerName,
                AvgScore    = Math.Round(g.Average(r => r.FinalScore), 1),
                Count       = g.Count(),
                PossibleBias = g.Average(r => r.FinalScore) > 85 || g.Average(r => r.FinalScore) < 50,
            })
            .ToList();

        var board = reviews.Select(r =>
        {
            calibMap.TryGetValue(r.Id, out var cal);
            return new
            {
                r.Id, r.EmployeeId, r.EmployeeName, r.DepartmentName, r.DesignationTitle,
                r.FinalScore, r.FinalRating, r.Status, r.ManagerNotes, r.CalibrationAdjustment,
                CalibrationRecord = cal,
            };
        });

        return Ok(new { reviews = board, distribution, managerStats, totalReviews = reviews.Count });
    }

    [HttpPost("{cycleId:guid}/adjust")]
    public async Task<IActionResult> AdjustScore(
        Guid cycleId, [FromBody] CalibrationAdjustRequest req, CancellationToken ct)
    {
        var tenantId = this.GetTenantId()!.Value;
        var userId   = this.GetUserId();
        var userName = HttpContext.User.FindFirst("FullName")?.Value ?? "HR";

        var review = await _db.AppraisalReviews
            .FirstOrDefaultAsync(r => r.Id == req.ReviewId && r.TenantId == tenantId, ct);
        if (review is null) return NotFound();
        if (string.IsNullOrWhiteSpace(req.Reason))
            return BadRequest(new { message = "A reason is mandatory for all calibration adjustments." });

        var originalScore  = review.FinalScore;
        var originalRating = review.FinalRating;

        review.CalibrationAdjustment = req.Adjustment;
        review.CalibrationNotes      = req.Reason;
        review.UpdatedAtUtc          = DateTime.UtcNow;

        var newScore = await _svc.CalculateAndSaveFinalScoreAsync(tenantId, req.ReviewId, ct);

        _db.AppraisalCalibrations.Add(new AppraisalCalibration
        {
            TenantId           = tenantId,
            ReviewId           = req.ReviewId,
            CycleId            = cycleId,
            EmployeeName       = review.EmployeeName,
            DepartmentName     = review.DepartmentName,
            OriginalScore      = originalScore,
            AdjustedScore      = newScore,
            AdjustmentReason   = req.Reason,
            OriginalRating     = originalRating,
            AdjustedRating     = review.FinalRating,
            CalibratedByUserId = userId,
            CalibratedByName   = userName,
        });

        await _svc.LogAuditAsync(tenantId, "AppraisalReview", req.ReviewId.ToString(),
            "CalibrationAdjustment",
            $"Score:{originalScore},Rating:{originalRating}",
            $"Score:{newScore},Rating:{review.FinalRating}",
            req.Reason, userId, userName, ct);

        await _db.SaveChangesAsync(ct);
        return Ok(new { originalScore, newScore, newRating = review.FinalRating });
    }
}

public record CalibrationAdjustRequest(Guid ReviewId, decimal Adjustment, string Reason);
