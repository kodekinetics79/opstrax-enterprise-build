using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Zayra.Api.Application.Common;
using Zayra.Api.Data;
using Zayra.Api.Models;

namespace Zayra.Api.Controllers.Finance;

[Authorize]
[ApiController]
[Route("api/finance/loans")]
public class LoansController : ControllerBase
{
    private readonly ZayraDbContext _db;
    private readonly IDataScopeService _scopeService;

    public LoansController(ZayraDbContext db, IDataScopeService scopeService)
    {
        _db = db;
        _scopeService = scopeService;
    }

    private Guid GetTenantId() =>
        Guid.TryParse(User.FindFirst("tenant_id")?.Value, out var id) ? id : Guid.Empty;
    private Guid? GetUserId() =>
        Guid.TryParse(User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value, out var id) ? id : null;
    private string GetUserName() => User.FindFirst(System.Security.Claims.ClaimTypes.Name)?.Value ?? "Unknown";

    // ── Loan Types ────────────────────────────────────────────────────────────

    [HttpGet("types")]
    public async Task<IActionResult> ListLoanTypes(CancellationToken ct)
    {
        var tid = GetTenantId();
        return Ok(await _db.LoanTypes.Where(x => x.TenantId == tid && !x.IsDeleted && x.IsActive)
            .OrderBy(x => x.NameEn).ToListAsync(ct));
    }

    [HttpPost("types")]
    [Authorize(Roles = "Admin,HR Manager,Finance")]
    public async Task<IActionResult> CreateLoanType([FromBody] LoanTypeRequest req, CancellationToken ct)
    {
        var tid = GetTenantId();
        if (await _db.LoanTypes.AnyAsync(x => x.TenantId == tid && x.Code == req.Code && !x.IsDeleted, ct))
            return Conflict("Loan type code already exists.");
        var t = new LoanType
        {
            TenantId = tid, Code = req.Code, NameEn = req.NameEn, NameAr = req.NameAr ?? string.Empty,
            MaxAmount = req.MaxAmount, MaxInstallments = req.MaxInstallments,
            RepaymentFrequency = req.RepaymentFrequency, IsInterestFree = req.IsInterestFree,
            InterestRate = req.InterestRate, MinServiceMonths = req.MinServiceMonths,
            RequiresApproval = req.RequiresApproval, CreatedBy = GetUserId(),
        };
        _db.LoanTypes.Add(t);
        await _db.SaveChangesAsync(ct);
        return Ok(t);
    }

    // ── Employee Loans ────────────────────────────────────────────────────────

    [HttpGet]
    public async Task<IActionResult> ListLoans(
        [FromQuery] Guid? employeeId, [FromQuery] string? status,
        [FromQuery] int page = 1, [FromQuery] int pageSize = 30, CancellationToken ct = default)
    {
        var tid = GetTenantId();

        // GCC salary confidentiality: loans are restricted to HR/Finance (employees.read) or own record only.
        // Managers do NOT see their team's loan data (UAE Labour Law / Saudi MoHRE requirement).
        var scope = await _scopeService.ResolveAsync(User, tid, ct);

        var q = _db.EmployeeLoans.Where(x => x.TenantId == tid && !x.IsDeleted);

        if (!scope.IsUnrestricted)
        {
            // GCC salary confidentiality: restrict to caller's own loan records only.
            // Managers do NOT see their team's loan data (UAE Labour Law / Saudi MoHRE).
            // Look up caller's employee GUID via their UserAccountId from the sub/NameIdentifier claim.
            var callerUserId = GetUserId();
            if (callerUserId.HasValue)
            {
                // EmployeeLoan.EmployeeId is the UserAccountId of the employee
                var allowedId = callerUserId.Value;
                var effectiveId = (employeeId.HasValue && employeeId.Value == allowedId) ? employeeId : allowedId;
                q = q.Where(x => x.EmployeeId == effectiveId);
            }
            else
            {
                // Cannot identify caller — return empty to be safe
                return Ok(new { total = 0, items = Array.Empty<EmployeeLoan>() });
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
    public async Task<IActionResult> GetLoan(Guid id, CancellationToken ct)
    {
        var tid = GetTenantId();
        var loan = await _db.EmployeeLoans.FirstOrDefaultAsync(x => x.Id == id && x.TenantId == tid && !x.IsDeleted, ct);
        if (loan == null) return NotFound();
        var installments = await _db.LoanInstallments.Where(x => x.LoanId == id).OrderBy(x => x.InstallmentNumber).ToListAsync(ct);
        var approvals = await _db.LoanApprovals.Where(x => x.LoanId == id).OrderBy(x => x.StepOrder).ToListAsync(ct);
        return Ok(new { loan, installments, approvals });
    }

    [HttpPost]
    public async Task<IActionResult> CreateLoan([FromBody] CreateLoanRequest req, CancellationToken ct)
    {
        var tid = GetTenantId();
        var uid = GetUserId();
        var loanType = await _db.LoanTypes.FirstOrDefaultAsync(x => x.Id == req.LoanTypeId && x.TenantId == tid && !x.IsDeleted, ct);
        if (loanType == null) return NotFound("Loan type not found.");
        if (req.RequestedAmount > loanType.MaxAmount && loanType.MaxAmount > 0)
            return BadRequest($"Requested amount exceeds maximum allowed ({loanType.MaxAmount}).");
        if (req.RequestedInstallments > loanType.MaxInstallments)
            return BadRequest($"Installments exceed maximum allowed ({loanType.MaxInstallments}).");

        // Generate loan number
        var count = await _db.EmployeeLoans.CountAsync(x => x.TenantId == tid, ct);
        var loanNumber = $"LN-{DateTime.UtcNow.Year}-{(count + 1):D5}";

        var loan = new EmployeeLoan
        {
            TenantId = tid, EmployeeId = req.EmployeeId, EmployeeName = req.EmployeeName,
            LoanTypeId = req.LoanTypeId, LoanTypeName = loanType.NameEn, LoanNumber = loanNumber,
            RequestedAmount = req.RequestedAmount, RequestedInstallments = req.RequestedInstallments,
            RepaymentFrequency = loanType.RepaymentFrequency, Notes = req.Notes ?? string.Empty,
            Status = loanType.RequiresApproval ? "Pending" : "Approved",
            CreatedBy = uid,
        };
        _db.EmployeeLoans.Add(loan);

        if (!loanType.RequiresApproval)
        {
            loan.ApprovedAmount = req.RequestedAmount;
            loan.ApprovedInstallments = req.RequestedInstallments;
            loan.InstallmentAmount = req.RequestedAmount / req.RequestedInstallments;
            loan.OutstandingBalance = req.RequestedAmount;
            loan.Status = "Active";
            GenerateInstallments(tid, loan);
        }

        await _db.SaveChangesAsync(ct);
        await WriteLoanAudit(tid, uid, loan.Id, "LoanRequested", null, $"Amount={req.RequestedAmount}", ct);
        return Ok(loan);
    }

    [HttpPost("{id:guid}/approvals")]
    [Authorize(Roles = "Admin,HR Manager,Finance,Manager")]
    public async Task<IActionResult> AddApprovalStep(Guid id, [FromBody] LoanApprovalRequest req, CancellationToken ct)
    {
        var tid = GetTenantId();
        var uid = GetUserId();
        var loan = await _db.EmployeeLoans.FirstOrDefaultAsync(x => x.Id == id && x.TenantId == tid, ct);
        if (loan == null) return NotFound();
        var step = new LoanApproval
        {
            TenantId = tid, LoanId = id, StepOrder = req.StepOrder,
            ApproverRole = req.ApproverRole,
        };
        _db.LoanApprovals.Add(step);
        await _db.SaveChangesAsync(ct);
        return Ok(step);
    }

    [HttpPatch("{id:guid}/approvals/{approvalId:guid}/decide")]
    [Authorize(Roles = "Admin,HR Manager,Finance,Manager")]
    public async Task<IActionResult> DecideApproval(Guid id, Guid approvalId, [FromBody] ApprovalDecisionRequest req, CancellationToken ct)
    {
        var tid = GetTenantId();
        var uid = GetUserId();
        var approval = await _db.LoanApprovals.FirstOrDefaultAsync(x => x.Id == approvalId && x.LoanId == id && x.TenantId == tid, ct);
        if (approval == null) return NotFound();
        approval.Status = req.Decision; approval.Comments = req.Comments ?? string.Empty;
        approval.ApprovedBy = uid; approval.ApprovedByName = GetUserName();
        approval.DecidedAtUtc = DateTime.UtcNow;

        var loan = await _db.EmployeeLoans.FirstAsync(x => x.Id == id && x.TenantId == tid, ct);
        if (req.Decision == "Rejected")
        {
            loan.Status = "Rejected"; loan.RejectionReason = req.Comments;
        }
        else
        {
            // Check if all steps approved
            var allApprovals = await _db.LoanApprovals.Where(x => x.LoanId == id).ToListAsync(ct);
            if (allApprovals.All(a => a.Status == "Approved"))
            {
                loan.Status = "Active";
                loan.ApprovedAmount = req.ApprovedAmount ?? loan.RequestedAmount;
                loan.ApprovedInstallments = req.ApprovedInstallments ?? loan.RequestedInstallments;
                loan.InstallmentAmount = loan.ApprovedAmount / loan.ApprovedInstallments;
                loan.OutstandingBalance = loan.ApprovedAmount;
                loan.DisbursementDate = DateOnly.FromDateTime(DateTime.UtcNow);
                if (req.RepaymentStartDate.HasValue) loan.RepaymentStartDate = req.RepaymentStartDate;
                else loan.RepaymentStartDate = DateOnly.FromDateTime(DateTime.UtcNow.AddMonths(1));
                GenerateInstallments(tid, loan);
            }
        }
        loan.UpdatedAtUtc = DateTime.UtcNow; loan.UpdatedBy = uid;
        await _db.SaveChangesAsync(ct);
        await WriteLoanAudit(tid, uid, id, $"Approval{req.Decision}", null, $"Step={approval.StepOrder},Decision={req.Decision}", ct);
        return Ok(new { loan, approval });
    }

    [HttpPatch("{id:guid}/settle")]
    [Authorize(Roles = "Admin,HR Manager,Finance")]
    public async Task<IActionResult> SettleLoan(Guid id, [FromBody] LoanSettlementRequest req, CancellationToken ct)
    {
        var tid = GetTenantId();
        var uid = GetUserId();
        var loan = await _db.EmployeeLoans.FirstOrDefaultAsync(x => x.Id == id && x.TenantId == tid && !x.IsDeleted, ct);
        if (loan == null) return NotFound();
        if (loan.Status != "Active") return BadRequest("Only active loans can be settled.");

        var settlement = new LoanSettlement
        {
            TenantId = tid, LoanId = id, SettlementType = req.SettlementType,
            SettlementAmount = req.SettlementAmount, SettlementDate = req.SettlementDate,
            Notes = req.Notes ?? string.Empty, ApprovedBy = uid, ApprovedByName = GetUserName(),
            CreatedBy = uid,
        };
        _db.LoanSettlements.Add(settlement);
        loan.TotalRepaid += req.SettlementAmount;
        loan.OutstandingBalance = Math.Max(0, loan.OutstandingBalance - req.SettlementAmount);
        if (loan.OutstandingBalance == 0) loan.Status = "Settled";
        loan.UpdatedAtUtc = DateTime.UtcNow; loan.UpdatedBy = uid;
        await _db.SaveChangesAsync(ct);
        await WriteLoanAudit(tid, uid, id, "LoanSettled", null, $"Amount={req.SettlementAmount},Type={req.SettlementType}", ct);
        return Ok(new { loan, settlement });
    }

    [HttpGet("{id:guid}/installments")]
    public async Task<IActionResult> GetInstallments(Guid id, CancellationToken ct)
    {
        var tid = GetTenantId();
        return Ok(await _db.LoanInstallments.Where(x => x.LoanId == id && x.TenantId == tid)
            .OrderBy(x => x.InstallmentNumber).ToListAsync(ct));
    }

    private void GenerateInstallments(Guid tid, EmployeeLoan loan)
    {
        var start = loan.RepaymentStartDate ?? DateOnly.FromDateTime(DateTime.UtcNow.AddMonths(1));
        for (int i = 1; i <= loan.ApprovedInstallments; i++)
        {
            _db.LoanInstallments.Add(new LoanInstallment
            {
                TenantId = tid, LoanId = loan.Id, InstallmentNumber = i,
                DueDate = start.AddMonths(i - 1), AmountDue = loan.InstallmentAmount, Status = "Pending",
            });
        }
    }

    private async Task WriteLoanAudit(Guid tid, Guid? uid, Guid loanId, string action, string? oldVal, string newVal, CancellationToken ct)
    {
        _db.LoanAuditLogs.Add(new LoanAuditLog
        {
            TenantId = tid, LoanId = loanId, Action = action,
            OldValuesJson = oldVal ?? string.Empty, NewValuesJson = newVal,
            PerformedBy = uid, PerformedByName = GetUserName(),
        });
        await _db.SaveChangesAsync(ct);
    }
}

public record LoanTypeRequest(string Code, string NameEn, string? NameAr, decimal MaxAmount, int MaxInstallments, string RepaymentFrequency, bool IsInterestFree, decimal InterestRate, int MinServiceMonths, bool RequiresApproval);
public record CreateLoanRequest(Guid EmployeeId, string EmployeeName, Guid LoanTypeId, decimal RequestedAmount, int RequestedInstallments, string? Notes);
public record LoanApprovalRequest(int StepOrder, string ApproverRole);
public record ApprovalDecisionRequest(string Decision, string? Comments, decimal? ApprovedAmount, int? ApprovedInstallments, DateOnly? RepaymentStartDate);
public record LoanSettlementRequest(string SettlementType, decimal SettlementAmount, DateOnly SettlementDate, string? Notes);
