using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Zayra.Api.Application.Common;
using Zayra.Api.Data;
using Zayra.Api.Models;

namespace Zayra.Api.Controllers;

// ── DTOs ─────────────────────────────────────────────────────────────────────

public sealed record StatutoryRuleDto(
    Guid Id,
    string CountryCode,
    string Jurisdiction,
    string RuleKey,
    string RuleValue,
    string DataType,
    string Description,
    DateTime EffectiveFrom,
    DateTime? EffectiveTo,
    bool IsTenantOverride);   // false = platform default (read-only to tenants)

public sealed record CreateStatutoryRuleRequest(
    string CountryCode,
    string Jurisdiction,
    string RuleKey,
    string RuleValue,
    string DataType,
    string Description,
    DateTime EffectiveFrom,
    DateTime? EffectiveTo);

public sealed record UpdateStatutoryRuleRequest(
    string RuleValue,
    string Description,
    DateTime EffectiveFrom,
    DateTime? EffectiveTo);

// ── Controller ────────────────────────────────────────────────────────────────

/// <summary>
/// Admin view of the StatutoryRule engine.
/// Platform defaults (TenantId=null) are visible but not editable by tenants.
/// Tenant overrides (TenantId=caller's tenantId) are CRUD.
/// All writes are RBAC-gated to Admin only.
/// </summary>
[ApiController]
[Route("api/statutory-rules")]
[Authorize(Roles = "Admin,HR Manager,Auditor")]
public class StatutoryRulesController : ControllerBase
{
    private readonly ZayraDbContext _db;

    public StatutoryRulesController(ZayraDbContext db) => _db = db;

    /// <summary>
    /// Lists effective-dated rules visible to this tenant:
    ///   - All platform defaults (TenantId = null)
    ///   - Tenant-specific overrides (TenantId = caller)
    /// Both sets are returned so the UI can show what is overridden and what is not.
    /// Query-filtered by countryCode and/or jurisdiction if provided.
    /// </summary>
    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<StatutoryRuleDto>>> List(
        [FromQuery] string? countryCode,
        [FromQuery] string? jurisdiction,
        CancellationToken ct)
    {
        var tenantId = this.GetTenantId();
        if (tenantId is null) return Unauthorized();

        var query = _db.StatutoryRules
            .AsNoTracking()
            .Where(r => r.TenantId == null || r.TenantId == tenantId);

        if (!string.IsNullOrWhiteSpace(countryCode))
            query = query.Where(r => r.CountryCode == countryCode.ToUpperInvariant());

        if (!string.IsNullOrWhiteSpace(jurisdiction))
            query = query.Where(r => r.Jurisdiction == jurisdiction);

        var items = await query
            .OrderBy(r => r.CountryCode)
            .ThenBy(r => r.Jurisdiction)
            .ThenBy(r => r.RuleKey)
            .ThenByDescending(r => r.EffectiveFrom)
            .Select(r => new StatutoryRuleDto(
                r.Id,
                r.CountryCode,
                r.Jurisdiction,
                r.RuleKey,
                r.RuleValue,
                r.DataType,
                r.Description,
                r.EffectiveFrom,
                r.EffectiveTo,
                r.TenantId != null))
            .ToListAsync(ct);

        return Ok(items);
    }

    /// <summary>Creates a tenant-specific override for a statutory rule.</summary>
    [HttpPost]
    [Authorize(Roles = "Admin")]
    public async Task<ActionResult<StatutoryRuleDto>> Create(
        [FromBody] CreateStatutoryRuleRequest req,
        CancellationToken ct)
    {
        var tenantId = this.GetTenantId();
        if (tenantId is null) return Unauthorized();

        if (string.IsNullOrWhiteSpace(req.CountryCode) ||
            string.IsNullOrWhiteSpace(req.RuleKey)     ||
            string.IsNullOrWhiteSpace(req.RuleValue))
            return BadRequest("CountryCode, RuleKey, and RuleValue are required.");

        var rule = new StatutoryRule
        {
            TenantId     = tenantId,
            CountryCode  = req.CountryCode.ToUpperInvariant(),
            Jurisdiction = req.Jurisdiction ?? string.Empty,
            RuleKey      = req.RuleKey.Trim(),
            RuleValue    = req.RuleValue.Trim(),
            DataType     = string.IsNullOrWhiteSpace(req.DataType) ? "decimal" : req.DataType,
            Description  = req.Description ?? string.Empty,
            EffectiveFrom = req.EffectiveFrom,
            EffectiveTo   = req.EffectiveTo,
            CreatedBy     = this.GetUserId(),
            CreatedAtUtc  = DateTime.UtcNow,
        };

        _db.StatutoryRules.Add(rule);
        await _db.SaveChangesAsync(ct);

        var dto = ToDto(rule, isTenantOverride: true);
        return CreatedAtAction(nameof(List), new { }, dto);
    }

    /// <summary>Updates a tenant-owned statutory rule override. Platform defaults are not editable.</summary>
    [HttpPut("{id:guid}")]
    [Authorize(Roles = "Admin")]
    public async Task<ActionResult<StatutoryRuleDto>> Update(
        Guid id,
        [FromBody] UpdateStatutoryRuleRequest req,
        CancellationToken ct)
    {
        var tenantId = this.GetTenantId();
        if (tenantId is null) return Unauthorized();

        // IDOR guard: rule must belong to this tenant (not a platform default)
        var rule = await _db.StatutoryRules
            .FirstOrDefaultAsync(r => r.Id == id && r.TenantId == tenantId, ct);
        if (rule is null) return NotFound();

        rule.RuleValue    = req.RuleValue.Trim();
        rule.Description  = req.Description ?? rule.Description;
        rule.EffectiveFrom = req.EffectiveFrom;
        rule.EffectiveTo  = req.EffectiveTo;

        await _db.SaveChangesAsync(ct);
        return Ok(ToDto(rule, isTenantOverride: true));
    }

    /// <summary>Deletes a tenant-owned statutory rule override. Platform defaults cannot be deleted.</summary>
    [HttpDelete("{id:guid}")]
    [Authorize(Roles = "Admin")]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct)
    {
        var tenantId = this.GetTenantId();
        if (tenantId is null) return Unauthorized();

        var rule = await _db.StatutoryRules
            .FirstOrDefaultAsync(r => r.Id == id && r.TenantId == tenantId, ct);
        if (rule is null) return NotFound();

        _db.StatutoryRules.Remove(rule);
        await _db.SaveChangesAsync(ct);
        return NoContent();
    }

    private static StatutoryRuleDto ToDto(StatutoryRule r, bool isTenantOverride) =>
        new(r.Id, r.CountryCode, r.Jurisdiction, r.RuleKey, r.RuleValue,
            r.DataType, r.Description, r.EffectiveFrom, r.EffectiveTo, isTenantOverride);
}

