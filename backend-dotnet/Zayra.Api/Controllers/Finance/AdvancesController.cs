using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Zayra.Api.Application.Common;
using Zayra.Api.Data;
using Zayra.Api.Models;

namespace Zayra.Api.Controllers.Finance;

[Authorize]
[ApiController]
[Route("api/finance/advances")]
public class AdvancesController : ControllerBase
{
    private readonly ZayraDbContext _db;
    private readonly IDataScopeService _scopeService;

    public AdvancesController(ZayraDbContext db, IDataScopeService scopeService)
    {
        _db = db;
        _scopeService = scopeService;
    }

    private Guid GetTenantId() =>
        Guid.TryParse(User.FindFirst("tenant_id")?.Value, out var id) ? id : Guid.Empty;
    private Guid? GetUserId() =>
        Guid.TryParse(User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value, out var id) ? id : null;
    private string GetUserName() => User.FindFirst(System.Security.Claims.ClaimTypes.Name)?.Value ?? "Unknown";

    // ── Policy ────────────────────────────────────────────────────────────────

    [HttpGet("policy")]
    public async Task<IActionResult> GetPolicy(CancellationToken ct)
    {
        var tid = GetTenantId();
        var p = await _db.AdvancePolicies.FirstOrDefaultAsync(x => x.TenantId == tid && x.IsActive, ct);
        return Ok(p);
    }

    [HttpPost("policy")]
    [Authorize(Roles = "Admin,HR Manager")]
    public async Task<IActionResult> UpsertPolicy([FromBody] AdvancePolicyRequest req, CancellationToken ct)
    {
        var tid = GetTenantId();
        var uid = GetUserId();
        var p = await _db.AdvancePolicies.FirstOrDefaultAsync(x => x.TenantId == tid && x.IsActive, ct);
        if (p != null)
        {
            p.PolicyName = req.PolicyName; p.MaxPercentageOfSalary = req.MaxPercentageOfSalary;
            p.MaxAdvancesPerYear = req.MaxAdvancesPerYear; p.MinServiceMonths = req.MinServiceMonths;
            p.AllowInstallments = req.AllowInstallments; p.MaxInstallments = req.MaxInstallments;
            p.CooldownMonths = req.CooldownMonths; p.RequiresApproval = req.RequiresApproval;
            await _db.SaveChangesAsync(ct);
            return Ok(p);
        }
        p = new AdvancePolicy
        {
            TenantId = tid, PolicyName = req.PolicyName, MaxPercentageOfSalary = req.MaxPercentageOfSalary,
            MaxAdvancesPerYear = req.MaxAdvancesPerYear, MinServiceMonths = req.MinServiceMonths,
            AllowInstallments = req.AllowInstallments, MaxInstallments = req.MaxInstallments,
            CooldownMonths = req.CooldownMonths, RequiresApproval = req.RequiresApproval, CreatedBy = uid,
        };
        _db.AdvancePolicies.Add(p);
        await _db.SaveChangesAsync(ct);
        return Ok(p);
    }

    // ── Salary Advances ───────────────────────────────────────────────────────

    [HttpGet]
    public async Task<IActionResult> List(
        [FromQuery] Guid? employeeId, [FromQuery] string? status,
        [FromQuery] int page = 1, [FromQuery] int pageSize = 30, CancellationToken ct = default)
    {
        var tid = GetTenantId();

        // GCC salary confidentiality: salary advances are restricted to HR/Finance (employees.read or payroll.read)
        // or caller's own record only. Managers do NOT see their team's advance data.
        var scope = await _scopeService.ResolveAsync(User, tid, ct);
        var q = _db.SalaryAdvances.Where(x => x.TenantId == tid && !x.IsDeleted);

        if (!scope.IsUnrestricted)
        {
            // Restrict to caller's own records via UserAccountId
            var callerUserId = GetUserId();
            if (callerUserId.HasValue)
            {
                var allowedId = callerUserId.Value;
                var effectiveId = (employeeId.HasValue && employeeId.Value == allowedId) ? employeeId : allowedId;
                q = q.Where(x => x.EmployeeId == effectiveId);
            }
            else
            {
                return Ok(new { total = 0, items = Array.Empty<SalaryAdvance>() });
            }
        }
        else if (employeeId.HasValue)
        {
            q = q.Where(x => x.EmployeeId == employeeId);
        }

        if (!string.IsNullOrEmpty(status)) q = q.Where(x => x.Status == status);
        var total = await q.CountAsync(ct);
        var items = await q.OrderByDescending(x => x.CreatedAtUtc)
            .Skip((page - 1) * pageSize).Take(pageSize).ToListAsync(ct);
        return Ok(new { total, items });
    }

    [HttpGet("{id:guid}")]
    public async Task<IActionResult> Get(Guid id, CancellationToken ct)
    {
        var tid = GetTenantId();
        var adv = await _db.SalaryAdvances.FirstOrDefaultAsync(x => x.Id == id && x.TenantId == tid && !x.IsDeleted, ct);
        if (adv == null) return NotFound();
        var installments = await _db.AdvanceInstallments.Where(x => x.AdvanceId == id).OrderBy(x => x.InstallmentNumber).ToListAsync(ct);
        var approvals = await _db.AdvanceApprovals.Where(x => x.AdvanceId == id).OrderBy(x => x.StepOrder).ToListAsync(ct);
        return Ok(new { advance = adv, installments, approvals });
    }

    [HttpPost]
    public async Task<IActionResult> Create([FromBody] CreateAdvanceRequest req, CancellationToken ct)
    {
        var tid = GetTenantId();
        var uid = GetUserId();
        var policy = await _db.AdvancePolicies.FirstOrDefaultAsync(x => x.TenantId == tid && x.IsActive, ct);
        var count = await _db.SalaryAdvances.CountAsync(x => x.TenantId == tid, ct);
        var advNumber = $"ADV-{DateTime.UtcNow.Year}-{(count + 1):D5}";

        var adv = new SalaryAdvance
        {
            TenantId = tid, EmployeeId = req.EmployeeId, EmployeeName = req.EmployeeName,
            AdvanceNumber = advNumber, RequestedAmount = req.RequestedAmount,
            RepaymentType = req.RepaymentType, Installments = req.Installments,
            Reason = req.Reason ?? string.Empty,
            Status = (policy?.RequiresApproval ?? true) ? "Pending" : "Approved",
            CreatedBy = uid,
        };

        if (!(policy?.RequiresApproval ?? true))
        {
            adv.ApprovedAmount = req.RequestedAmount;
            adv.InstallmentAmount = req.Installments > 0 ? req.RequestedAmount / req.Installments : req.RequestedAmount;
            adv.OutstandingBalance = req.RequestedAmount;
            adv.Status = "Active";
            GenerateAdvanceInstallments(tid, adv, req.RepaymentStartDate);
        }

        _db.SalaryAdvances.Add(adv);
        await _db.SaveChangesAsync(ct);
        await WriteAdvanceAudit(tid, uid, adv.Id, "AdvanceRequested", null, $"Amount={req.RequestedAmount}", ct);
        return Ok(adv);
    }

    [HttpPatch("{id:guid}/approve")]
    [Authorize(Roles = "Admin,HR Manager,Finance,Manager")]
    public async Task<IActionResult> Approve(Guid id, [FromBody] AdvanceApproveRequest req, CancellationToken ct)
    {
        var tid = GetTenantId();
        var uid = GetUserId();
        var adv = await _db.SalaryAdvances.FirstOrDefaultAsync(x => x.Id == id && x.TenantId == tid, ct);
        if (adv == null) return NotFound();
        if (adv.Status != "Pending") return BadRequest("Advance is not in Pending status.");

        adv.Status = "Active"; adv.ApprovedAmount = req.ApprovedAmount;
        adv.InstallmentAmount = req.Installments > 0 ? req.ApprovedAmount / req.Installments : req.ApprovedAmount;
        adv.Installments = req.Installments; adv.OutstandingBalance = req.ApprovedAmount;
        adv.RepaymentStartDate = req.RepaymentStartDate; adv.UpdatedAtUtc = DateTime.UtcNow; adv.UpdatedBy = uid;

        GenerateAdvanceInstallments(tid, adv, req.RepaymentStartDate);

        var approval = new AdvanceApproval
        {
            TenantId = tid, AdvanceId = id, StepOrder = 1, ApproverRole = "HR",
            ApprovedBy = uid, ApprovedByName = GetUserName(), Status = "Approved",
            DecidedAtUtc = DateTime.UtcNow,
        };
        _db.AdvanceApprovals.Add(approval);
        await _db.SaveChangesAsync(ct);
        await WriteAdvanceAudit(tid, uid, id, "AdvanceApproved", null, $"Amount={req.ApprovedAmount}", ct);
        return Ok(adv);
    }

    [HttpPatch("{id:guid}/reject")]
    [Authorize(Roles = "Admin,HR Manager,Finance,Manager")]
    public async Task<IActionResult> Reject(Guid id, [FromBody] RejectRequest req, CancellationToken ct)
    {
        var tid = GetTenantId();
        var uid = GetUserId();
        var adv = await _db.SalaryAdvances.FirstOrDefaultAsync(x => x.Id == id && x.TenantId == tid, ct);
        if (adv == null) return NotFound();
        adv.Status = "Rejected"; adv.RejectionReason = req.Reason;
        adv.UpdatedAtUtc = DateTime.UtcNow; adv.UpdatedBy = uid;
        await _db.SaveChangesAsync(ct);
        await WriteAdvanceAudit(tid, uid, id, "AdvanceRejected", null, $"Reason={req.Reason}", ct);
        return Ok(adv);
    }

    private void GenerateAdvanceInstallments(Guid tid, SalaryAdvance adv, DateOnly? startDate)
    {
        var start = startDate ?? DateOnly.FromDateTime(DateTime.UtcNow.AddMonths(1));
        for (int i = 1; i <= adv.Installments; i++)
        {
            _db.AdvanceInstallments.Add(new AdvanceInstallment
            {
                TenantId = tid, AdvanceId = adv.Id, InstallmentNumber = i,
                DueDate = start.AddMonths(i - 1), AmountDue = adv.InstallmentAmount, Status = "Pending",
            });
        }
    }

    private async Task WriteAdvanceAudit(Guid tid, Guid? uid, Guid advId, string action, string? oldVal, string newVal, CancellationToken ct)
    {
        _db.AdvanceAuditLogs.Add(new AdvanceAuditLog
        {
            TenantId = tid, AdvanceId = advId, Action = action,
            OldValuesJson = oldVal ?? string.Empty, NewValuesJson = newVal,
            PerformedBy = uid, PerformedByName = GetUserName(),
        });
        await _db.SaveChangesAsync(ct);
    }
}

public record AdvancePolicyRequest(string PolicyName, decimal MaxPercentageOfSalary, int MaxAdvancesPerYear, int MinServiceMonths, bool AllowInstallments, int MaxInstallments, int CooldownMonths, bool RequiresApproval);
public record CreateAdvanceRequest(Guid EmployeeId, string EmployeeName, decimal RequestedAmount, string RepaymentType, int Installments, DateOnly? RepaymentStartDate, string? Reason);
public record AdvanceApproveRequest(decimal ApprovedAmount, int Installments, DateOnly? RepaymentStartDate);
public record RejectRequest(string? Reason);
