using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Zayra.Api.Application.Common;
using Zayra.Api.Application.Performance;
using Zayra.Api.Data;
using Zayra.Api.Models;

namespace Zayra.Api.Controllers.Performance;

[ApiController]
[Route("api/performance/recommendations")]
[Authorize]
public class RecommendationsController : ControllerBase
{
    private readonly ZayraDbContext _db;
    private readonly IPerformanceService _svc;

    public RecommendationsController(ZayraDbContext db, IPerformanceService svc)
    { _db = db; _svc = svc; }

    // ── Increment ──────────────────────────────────────────────────────────────

    [HttpGet("increments")]
    public async Task<IActionResult> ListIncrements([FromQuery] string? status, CancellationToken ct)
    {
        var tenantId = this.GetTenantId()!.Value;
        var query = _db.IncrementRecommendations.Where(r => r.TenantId == tenantId);
        if (!string.IsNullOrWhiteSpace(status)) query = query.Where(r => r.Status == status);
        return Ok(await query.OrderByDescending(r => r.CreatedAtUtc).ToListAsync(ct));
    }

    [HttpPost("increments")]
    [Authorize(Roles = "Admin,HR Manager,Manager")]
    public async Task<IActionResult> CreateIncrement([FromBody] IncrementRequest req, CancellationToken ct)
    {
        var tenantId = this.GetTenantId()!.Value;
        var userId   = this.GetUserId();
        var userName = HttpContext.User.FindFirst("FullName")?.Value ?? "HR";

        var rec = new IncrementRecommendation
        {
            TenantId                   = tenantId,
            ReviewId                   = req.ReviewId,
            EmployeeId                 = req.EmployeeId,
            EmployeeName               = req.EmployeeName,
            DepartmentName             = req.DepartmentName,
            DesignationTitle           = req.DesignationTitle,
            CurrentSalary              = req.CurrentSalary,
            RecommendedIncrementPct    = req.IncrementPct,
            RecommendedIncrementAmount = Math.Round(req.CurrentSalary * req.IncrementPct / 100, 2),
            NewSalary                  = Math.Round(req.CurrentSalary * (1 + req.IncrementPct / 100), 2),
            EffectiveDate              = req.EffectiveDate,
            Reason                     = req.Reason,
            RecommendedByUserId        = userId,
            RecommendedByName          = userName,
        };
        _db.IncrementRecommendations.Add(rec);
        await _svc.LogAuditAsync(tenantId, "IncrementRecommendation", rec.Id.ToString(),
            "Created", string.Empty, $"Pct:{req.IncrementPct}%,NewSalary:{rec.NewSalary}", req.Reason, userId, userName, ct);
        await _db.SaveChangesAsync(ct);
        return Created($"/api/performance/recommendations/increments/{rec.Id}", rec);
    }

    [HttpPost("increments/{id:guid}/approve")]
    [Authorize(Roles = "Admin,HR Manager")]
    public async Task<IActionResult> ApproveIncrement(Guid id, [FromBody] SimpleDecisionRequest req, CancellationToken ct)
    {
        var tenantId = this.GetTenantId()!.Value;
        var userId   = this.GetUserId();
        var rec = await _db.IncrementRecommendations
            .FirstOrDefaultAsync(r => r.Id == id && r.TenantId == tenantId, ct);
        if (rec is null) return NotFound();

        rec.Status          = req.Decision; // Approved/Rejected
        rec.ApprovedByUserId = userId;
        rec.ApprovedAtUtc   = DateTime.UtcNow;
        await _svc.LogAuditAsync(tenantId, "IncrementRecommendation", id.ToString(),
            req.Decision, "Pending", req.Decision, req.Notes ?? string.Empty, userId, "HR", ct);
        await _db.SaveChangesAsync(ct);
        return Ok(rec);
    }

    // ── Promotion ──────────────────────────────────────────────────────────────

    [HttpGet("promotions")]
    public async Task<IActionResult> ListPromotions([FromQuery] string? status, CancellationToken ct)
    {
        var tenantId = this.GetTenantId()!.Value;
        var query = _db.PromotionRecommendations.Where(r => r.TenantId == tenantId);
        if (!string.IsNullOrWhiteSpace(status)) query = query.Where(r => r.Status == status);
        return Ok(await query.OrderByDescending(r => r.CreatedAtUtc).ToListAsync(ct));
    }

    [HttpPost("promotions")]
    [Authorize(Roles = "Admin,HR Manager,Manager")]
    public async Task<IActionResult> CreatePromotion([FromBody] PromotionRequest req, CancellationToken ct)
    {
        var tenantId = this.GetTenantId()!.Value;
        var userId   = this.GetUserId();
        var userName = HttpContext.User.FindFirst("FullName")?.Value ?? "HR";

        var rec = new PromotionRecommendation
        {
            TenantId             = tenantId,
            ReviewId             = req.ReviewId,
            EmployeeId           = req.EmployeeId,
            EmployeeName         = req.EmployeeName,
            DepartmentName       = req.DepartmentName,
            CurrentDesignation   = req.CurrentDesignation,
            ProposedDesignation  = req.ProposedDesignation,
            EffectiveDate        = req.EffectiveDate,
            Reason               = req.Reason,
            RecommendedByUserId  = userId,
            RecommendedByName    = userName,
        };
        _db.PromotionRecommendations.Add(rec);
        await _db.SaveChangesAsync(ct);
        return Created($"/api/performance/recommendations/promotions/{rec.Id}", rec);
    }

    [HttpPost("promotions/{id:guid}/approve")]
    [Authorize(Roles = "Admin,HR Manager")]
    public async Task<IActionResult> ApprovePromotion(Guid id, [FromBody] SimpleDecisionRequest req, CancellationToken ct)
    {
        var tenantId = this.GetTenantId()!.Value;
        var userId   = this.GetUserId();
        var rec = await _db.PromotionRecommendations
            .FirstOrDefaultAsync(r => r.Id == id && r.TenantId == tenantId, ct);
        if (rec is null) return NotFound();
        rec.Status = req.Decision; rec.ApprovedByUserId = userId; rec.ApprovedAtUtc = DateTime.UtcNow;
        await _db.SaveChangesAsync(ct);
        return Ok(rec);
    }

    // ── Bonus ──────────────────────────────────────────────────────────────────

    [HttpGet("bonuses")]
    public async Task<IActionResult> ListBonuses([FromQuery] string? status, CancellationToken ct)
    {
        var tenantId = this.GetTenantId()!.Value;
        var query = _db.BonusRecommendations.Where(r => r.TenantId == tenantId);
        if (!string.IsNullOrWhiteSpace(status)) query = query.Where(r => r.Status == status);
        return Ok(await query.OrderByDescending(r => r.CreatedAtUtc).ToListAsync(ct));
    }

    [HttpPost("bonuses")]
    [Authorize(Roles = "Admin,HR Manager,Manager")]
    public async Task<IActionResult> CreateBonus([FromBody] BonusRequest req, CancellationToken ct)
    {
        var tenantId = this.GetTenantId()!.Value;
        var userId   = this.GetUserId();
        var userName = HttpContext.User.FindFirst("FullName")?.Value ?? "HR";

        var rec = new BonusRecommendation
        {
            TenantId = tenantId, ReviewId = req.ReviewId,
            EmployeeId = req.EmployeeId, EmployeeName = req.EmployeeName,
            DepartmentName = req.DepartmentName,
            BonusAmount = req.BonusAmount, BonusType = req.BonusType,
            Reason = req.Reason, RecommendedByUserId = userId, RecommendedByName = userName,
        };
        _db.BonusRecommendations.Add(rec);
        await _db.SaveChangesAsync(ct);
        return Created($"/api/performance/recommendations/bonuses/{rec.Id}", rec);
    }

    [HttpPost("bonuses/{id:guid}/approve")]
    [Authorize(Roles = "Admin,HR Manager")]
    public async Task<IActionResult> ApproveBonus(Guid id, [FromBody] SimpleDecisionRequest req, CancellationToken ct)
    {
        var tenantId = this.GetTenantId()!.Value;
        var userId   = this.GetUserId();
        var rec = await _db.BonusRecommendations
            .FirstOrDefaultAsync(r => r.Id == id && r.TenantId == tenantId, ct);
        if (rec is null) return NotFound();
        rec.Status = req.Decision; rec.ApprovedByUserId = userId; rec.ApprovedAtUtc = DateTime.UtcNow;
        await _db.SaveChangesAsync(ct);
        return Ok(rec);
    }
}

public record IncrementRequest(
    Guid ReviewId, int EmployeeId, string EmployeeName,
    string DepartmentName, string DesignationTitle,
    decimal CurrentSalary, decimal IncrementPct,
    DateOnly EffectiveDate, string Reason);

public record PromotionRequest(
    Guid ReviewId, int EmployeeId, string EmployeeName, string DepartmentName,
    string CurrentDesignation, string ProposedDesignation,
    DateOnly EffectiveDate, string Reason);

public record BonusRequest(
    Guid ReviewId, int EmployeeId, string EmployeeName, string DepartmentName,
    decimal BonusAmount, string BonusType, string Reason);

public record SimpleDecisionRequest(string Decision, string? Notes);
