using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Zayra.Api.Application.Common;
using Zayra.Api.Application.Leave;
using Zayra.Api.Data;
using Zayra.Api.Models;

namespace Zayra.Api.Controllers.Leave;

[ApiController]
[Route("api/leave/policies")]
[Authorize]
public class LeavePoliciesController : ControllerBase
{
    private readonly ZayraDbContext _db;
    private readonly ILeaveService _leaveService;

    public LeavePoliciesController(ZayraDbContext db, ILeaveService leaveService)
    {
        _db = db;
        _leaveService = leaveService;
    }

    [HttpGet]
    public async Task<IActionResult> List(
        [FromQuery] string? countryCode,
        [FromQuery] string? status,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 25,
        CancellationToken ct = default)
    {
        var tenantId = this.GetTenantId();
        if (tenantId is null) return Unauthorized();

        var query = _db.LeavePolicies.Where(p => p.TenantId == tenantId);

        if (!string.IsNullOrWhiteSpace(countryCode))
            query = query.Where(p => p.CountryCode == countryCode);

        if (!string.IsNullOrWhiteSpace(status))
            query = query.Where(p => p.Status == status);

        var total = await query.CountAsync(ct);
        var items = await query
            .OrderBy(p => p.Name)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(ct);

        return Ok(new PagedResult<LeavePolicy>(items, total, page, pageSize));
    }

    [HttpGet("{id:guid}")]
    public async Task<IActionResult> Get(Guid id, CancellationToken ct)
    {
        var tenantId = this.GetTenantId();
        if (tenantId is null) return Unauthorized();

        var policy = await _db.LeavePolicies
            .FirstOrDefaultAsync(p => p.Id == id && p.TenantId == tenantId, ct);
        if (policy is null) return NotFound();

        return Ok(policy);
    }

    [HttpPost]
    [Authorize(Roles = "Admin,HR Manager")]
    public async Task<IActionResult> Create([FromBody] CreateLeavePolicyRequest req, CancellationToken ct)
    {
        var tenantId = this.GetTenantId();
        if (tenantId is null) return Unauthorized();

        var leaveTypeExists = await _db.LeaveTypes
            .AnyAsync(t => t.Id == req.LeaveTypeId && t.TenantId == tenantId, ct);
        if (!leaveTypeExists)
            return BadRequest(new { message = "Leave type not found." });

        var policy = new LeavePolicy
        {
            TenantId = tenantId.Value,
            Name = req.Name,
            LeaveTypeId = req.LeaveTypeId,
            CountryCode = req.CountryCode ?? string.Empty,
            CompanyId = req.CompanyId,
            BranchId = req.BranchId,
            DepartmentName = req.DepartmentName ?? string.Empty,
            Grade = req.Grade ?? string.Empty,
            EmploymentType = req.EmploymentType ?? string.Empty,
            ContractType = req.ContractType ?? string.Empty,
            Gender = req.Gender ?? string.Empty,
            AppliesOnProbation = req.AppliesOnProbation,
            AnnualEntitlementDays = req.AnnualEntitlementDays,
            AccrualMethod = req.AccrualMethod ?? "Yearly",
            CarryForwardMax = req.CarryForwardMax,
            CarryForwardExpiry = req.CarryForwardExpiry,
            EncashmentAllowed = req.EncashmentAllowed,
            EncashmentMaxDays = req.EncashmentMaxDays,
            MinimumDaysPerRequest = req.MinimumDaysPerRequest > 0 ? req.MinimumDaysPerRequest : 1,
            MaximumDaysPerRequest = req.MaximumDaysPerRequest,
            NoticeRequiredDays = req.NoticeRequiredDays,
            WeekendsIncluded = req.WeekendsIncluded,
            PublicHolidaysIncluded = req.PublicHolidaysIncluded,
            PayrollImpact = req.PayrollImpact ?? "Full",
            ApprovalWorkflowId = req.ApprovalWorkflowId,
            Status = req.Status ?? "Draft"
        };

        _db.LeavePolicies.Add(policy);
        await _db.SaveChangesAsync(ct);

        await _leaveService.LogAuditAsync(tenantId.Value, "LeavePolicy", policy.Id.ToString(),
            "Created", string.Empty, policy.Name, "Leave policy created",
            User.Identity?.Name ?? "Admin", ct);

        return Created($"/api/leave/policies/{policy.Id}", policy);
    }

    [HttpPut("{id:guid}")]
    [Authorize(Roles = "Admin,HR Manager")]
    public async Task<IActionResult> Update(Guid id, [FromBody] UpdateLeavePolicyRequest req, CancellationToken ct)
    {
        var tenantId = this.GetTenantId();
        if (tenantId is null) return Unauthorized();

        var policy = await _db.LeavePolicies
            .FirstOrDefaultAsync(p => p.Id == id && p.TenantId == tenantId, ct);
        if (policy is null) return NotFound();

        if (!string.IsNullOrWhiteSpace(req.Name)) policy.Name = req.Name;
        if (req.CountryCode is not null) policy.CountryCode = req.CountryCode;
        if (req.CompanyId.HasValue) policy.CompanyId = req.CompanyId;
        if (req.BranchId.HasValue) policy.BranchId = req.BranchId;
        if (req.DepartmentName is not null) policy.DepartmentName = req.DepartmentName;
        if (req.Grade is not null) policy.Grade = req.Grade;
        if (req.EmploymentType is not null) policy.EmploymentType = req.EmploymentType;
        if (req.ContractType is not null) policy.ContractType = req.ContractType;
        if (req.Gender is not null) policy.Gender = req.Gender;
        if (req.AppliesOnProbation.HasValue) policy.AppliesOnProbation = req.AppliesOnProbation.Value;
        if (req.AnnualEntitlementDays.HasValue) policy.AnnualEntitlementDays = req.AnnualEntitlementDays.Value;
        if (!string.IsNullOrWhiteSpace(req.AccrualMethod)) policy.AccrualMethod = req.AccrualMethod;
        if (req.CarryForwardMax.HasValue) policy.CarryForwardMax = req.CarryForwardMax.Value;
        if (req.CarryForwardExpiry.HasValue) policy.CarryForwardExpiry = req.CarryForwardExpiry.Value;
        if (req.EncashmentAllowed.HasValue) policy.EncashmentAllowed = req.EncashmentAllowed.Value;
        if (req.EncashmentMaxDays.HasValue) policy.EncashmentMaxDays = req.EncashmentMaxDays.Value;
        if (req.MinimumDaysPerRequest.HasValue) policy.MinimumDaysPerRequest = req.MinimumDaysPerRequest.Value;
        if (req.MaximumDaysPerRequest.HasValue) policy.MaximumDaysPerRequest = req.MaximumDaysPerRequest.Value;
        if (req.NoticeRequiredDays.HasValue) policy.NoticeRequiredDays = req.NoticeRequiredDays.Value;
        if (req.WeekendsIncluded.HasValue) policy.WeekendsIncluded = req.WeekendsIncluded.Value;
        if (req.PublicHolidaysIncluded.HasValue) policy.PublicHolidaysIncluded = req.PublicHolidaysIncluded.Value;
        if (!string.IsNullOrWhiteSpace(req.PayrollImpact)) policy.PayrollImpact = req.PayrollImpact;
        if (req.ApprovalWorkflowId.HasValue) policy.ApprovalWorkflowId = req.ApprovalWorkflowId;
        if (!string.IsNullOrWhiteSpace(req.Status)) policy.Status = req.Status;
        policy.UpdatedAtUtc = DateTime.UtcNow;

        await _db.SaveChangesAsync(ct);

        await _leaveService.LogAuditAsync(tenantId.Value, "LeavePolicy", policy.Id.ToString(),
            "Updated", string.Empty, policy.Name, "Leave policy updated",
            User.Identity?.Name ?? "Admin", ct);

        return Ok(policy);
    }

    [HttpDelete("{id:guid}")]
    [Authorize(Roles = "Admin,HR Manager")]
    public async Task<IActionResult> Archive(Guid id, CancellationToken ct)
    {
        var tenantId = this.GetTenantId();
        if (tenantId is null) return Unauthorized();

        var policy = await _db.LeavePolicies
            .FirstOrDefaultAsync(p => p.Id == id && p.TenantId == tenantId, ct);
        if (policy is null) return NotFound();

        policy.Status = "Archived";
        policy.UpdatedAtUtc = DateTime.UtcNow;
        await _db.SaveChangesAsync(ct);
        return NoContent();
    }
}

public record CreateLeavePolicyRequest(
    string Name,
    Guid LeaveTypeId,
    string? CountryCode,
    Guid? CompanyId,
    Guid? BranchId,
    string? DepartmentName,
    string? Grade,
    string? EmploymentType,
    string? ContractType,
    string? Gender,
    bool AppliesOnProbation,
    decimal AnnualEntitlementDays,
    string? AccrualMethod,
    decimal CarryForwardMax,
    int CarryForwardExpiry,
    bool EncashmentAllowed,
    decimal EncashmentMaxDays,
    decimal MinimumDaysPerRequest,
    decimal MaximumDaysPerRequest,
    int NoticeRequiredDays,
    bool WeekendsIncluded,
    bool PublicHolidaysIncluded,
    string? PayrollImpact,
    Guid? ApprovalWorkflowId,
    string? Status);

public record UpdateLeavePolicyRequest(
    string? Name,
    string? CountryCode,
    Guid? CompanyId,
    Guid? BranchId,
    string? DepartmentName,
    string? Grade,
    string? EmploymentType,
    string? ContractType,
    string? Gender,
    bool? AppliesOnProbation,
    decimal? AnnualEntitlementDays,
    string? AccrualMethod,
    decimal? CarryForwardMax,
    int? CarryForwardExpiry,
    bool? EncashmentAllowed,
    decimal? EncashmentMaxDays,
    decimal? MinimumDaysPerRequest,
    decimal? MaximumDaysPerRequest,
    int? NoticeRequiredDays,
    bool? WeekendsIncluded,
    bool? PublicHolidaysIncluded,
    string? PayrollImpact,
    Guid? ApprovalWorkflowId,
    string? Status);
