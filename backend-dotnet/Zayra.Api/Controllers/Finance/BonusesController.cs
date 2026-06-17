using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Text.Json;
using Zayra.Api.Application.Common;
using Zayra.Api.Data;
using Zayra.Api.Models;

namespace Zayra.Api.Controllers.Finance;

[Authorize]
[ApiController]
[Route("api/finance/bonuses")]
public class BonusesController : ControllerBase
{
    private readonly ZayraDbContext _db;
    private readonly IDataScopeService _scopeService;

    // Federal supplemental withholding rate used when isTaxable = true
    private const decimal FederalSupplementalRate = 0.22m;

    public BonusesController(ZayraDbContext db, IDataScopeService scopeService)
    {
        _db = db;
        _scopeService = scopeService;
    }

    private Guid GetTenantId() =>
        Guid.TryParse(User.FindFirst("tenant_id")?.Value, out var id) ? id : Guid.Empty;
    private Guid? GetUserId() =>
        Guid.TryParse(User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value, out var id) ? id : null;
    private string GetUserName() => User.FindFirst(System.Security.Claims.ClaimTypes.Name)?.Value ?? "Unknown";

    // ── Bonus Types ───────────────────────────────────────────────────────────

    [HttpGet("types")]
    public async Task<IActionResult> ListBonusTypes(CancellationToken ct)
    {
        var tid = GetTenantId();
        return Ok(await _db.BonusTypes.Where(x => x.TenantId == tid && !x.IsDeleted && x.IsActive)
            .OrderBy(x => x.NameEn).ToListAsync(ct));
    }

    [HttpPost("types")]
    [Authorize(Roles = "Admin,HR Manager")]
    public async Task<IActionResult> CreateBonusType([FromBody] BonusTypeRequest req, CancellationToken ct)
    {
        var tid = GetTenantId();
        if (await _db.BonusTypes.AnyAsync(x => x.TenantId == tid && x.Code == req.Code && !x.IsDeleted, ct))
            return Conflict("Bonus type code already exists.");
        var t = new BonusType
        {
            TenantId = tid, Code = req.Code, NameEn = req.NameEn, NameAr = req.NameAr ?? string.Empty,
            CalculationMethod = req.CalculationMethod, IsTaxable = req.IsTaxable, CreatedBy = GetUserId(),
        };
        _db.BonusTypes.Add(t);
        await _db.SaveChangesAsync(ct);
        return Ok(t);
    }

    // ── Bonus Batches ─────────────────────────────────────────────────────────

    [HttpGet("batches")]
    public async Task<IActionResult> ListBatches(
        [FromQuery] string? status, [FromQuery] int page = 1, [FromQuery] int pageSize = 20, CancellationToken ct = default)
    {
        var tid = GetTenantId();
        var q = _db.BonusBatches.Where(x => x.TenantId == tid && !x.IsDeleted);
        if (!string.IsNullOrEmpty(status)) q = q.Where(x => x.Status == status);
        var total = await q.CountAsync(ct);
        var items = await q.OrderByDescending(x => x.CreatedAtUtc)
            .Skip((page - 1) * pageSize).Take(pageSize).ToListAsync(ct);
        return Ok(new { total, items });
    }

    [HttpGet("batches/{id:guid}")]
    public async Task<IActionResult> GetBatch(Guid id, CancellationToken ct)
    {
        var tid = GetTenantId();
        var batch = await _db.BonusBatches.FirstOrDefaultAsync(x => x.Id == id && x.TenantId == tid && !x.IsDeleted, ct);
        if (batch == null) return NotFound();

        var scope = await _scopeService.ResolveAsync(User, tid, ct);
        IQueryable<EmployeeBonus> bonusQuery = _db.EmployeeBonuses.Where(x => x.BonusBatchId == id && !x.IsDeleted);
        if (!scope.IsUnrestricted)
        {
            var callerUserId = GetUserId();
            if (callerUserId.HasValue)
                bonusQuery = bonusQuery.Where(x => x.EmployeeId == callerUserId.Value);
            else
                bonusQuery = bonusQuery.Where(_ => false);
        }

        var bonuses = await bonusQuery.ToListAsync(ct);
        var approvals = await _db.BonusApprovals.Where(x => x.BonusBatchId == id).OrderBy(x => x.StepOrder).ToListAsync(ct);
        var auditLogs = await _db.BonusAuditLogs.Where(x => x.BonusBatchId == id).OrderByDescending(x => x.CreatedAtUtc).ToListAsync(ct);
        var glEntries = await _db.FinanceGlEntries.Where(x => x.SourceEntityId == id).OrderByDescending(x => x.EntryDate).ToListAsync(ct);
        return Ok(new { batch, bonuses, approvals, auditLogs, glEntries });
    }

    [HttpPost("batches")]
    [Authorize(Roles = "Admin,HR Manager,Finance")]
    public async Task<IActionResult> CreateBatch([FromBody] CreateBatchRequest req, CancellationToken ct)
    {
        var tid = GetTenantId();
        var uid = GetUserId();
        var bonusType = await _db.BonusTypes.FirstOrDefaultAsync(x => x.Id == req.BonusTypeId && x.TenantId == tid, ct);
        if (bonusType == null) return NotFound("Bonus type not found.");

        var count = await _db.BonusBatches.CountAsync(x => x.TenantId == tid, ct);
        var batchNumber = $"BON-{DateTime.UtcNow.Year}-{(count + 1):D3}";

        var batch = new BonusBatch
        {
            TenantId = tid, BonusTypeId = req.BonusTypeId, BonusTypeName = bonusType.NameEn,
            BatchNumber = batchNumber, BatchName = req.BatchName, PaymentPeriod = req.PaymentPeriod,
            PaymentDate = req.PaymentDate, Notes = req.Notes ?? string.Empty, Status = "Draft",
            CreatedBy = uid,
        };
        _db.BonusBatches.Add(batch);
        await _db.SaveChangesAsync(ct);
        await WriteBonusAudit(tid, uid, batch.Id, null, "BatchCreated", null,
            JsonSerializer.Serialize(new { batch.BatchNumber, batch.BatchName }), ct);
        return Ok(batch);
    }

    // ── Employee Bonuses ──────────────────────────────────────────────────────

    [HttpPost("batches/{batchId:guid}/employees")]
    [Authorize(Roles = "Admin,HR Manager,Finance")]
    public async Task<IActionResult> AddEmployeeBonus(Guid batchId, [FromBody] AddEmployeeBonusRequest req, CancellationToken ct)
    {
        var tid = GetTenantId();
        var uid = GetUserId();
        var batch = await _db.BonusBatches.FirstOrDefaultAsync(x => x.Id == batchId && x.TenantId == tid && !x.IsDeleted, ct);
        if (batch == null) return NotFound("Batch not found.");
        if (batch.Status != "Draft") return BadRequest("Can only add employees to a Draft batch.");

        var bonusType = await _db.BonusTypes.FirstOrDefaultAsync(x => x.Id == batch.BonusTypeId && x.TenantId == tid, ct);

        decimal grossBonusAmount = req.CalculationMethod switch
        {
            "PercentageSalary" => req.BasicSalary * (req.CalculationValue / 100m),
            _ => req.CalculationValue,
        };

        // Federal supplemental withholding on taxable bonuses (22% flat rate for supplemental wages)
        decimal taxWithheld = (bonusType?.IsTaxable == true) ? Math.Round(grossBonusAmount * FederalSupplementalRate, 2) : 0m;
        decimal netBonusAmount = grossBonusAmount - taxWithheld;

        var eb = new EmployeeBonus
        {
            TenantId = tid, BonusBatchId = batchId, EmployeeId = req.EmployeeId,
            EmployeeName = req.EmployeeName, Department = req.Department ?? string.Empty,
            BonusTypeId = batch.BonusTypeId, BonusTypeName = batch.BonusTypeName,
            BasicSalary = req.BasicSalary, CalculationMethod = req.CalculationMethod,
            CalculationValue = req.CalculationValue, BonusAmount = grossBonusAmount,
            PaymentPeriod = batch.PaymentPeriod, Status = "Draft", Notes = req.Notes ?? string.Empty,
            CreatedBy = uid,
        };
        _db.EmployeeBonuses.Add(eb);

        batch.TotalAmount += grossBonusAmount;
        batch.EmployeeCount++;
        batch.UpdatedAtUtc = DateTime.UtcNow; batch.UpdatedBy = uid;

        await _db.SaveChangesAsync(ct);
        return Ok(new { bonus = eb, grossBonusAmount, taxWithheld, netBonusAmount });
    }

    [HttpDelete("batches/{batchId:guid}/employees/{bonusId:guid}")]
    [Authorize(Roles = "Admin,HR Manager,Finance")]
    public async Task<IActionResult> RemoveEmployeeBonus(Guid batchId, Guid bonusId, CancellationToken ct)
    {
        var tid = GetTenantId();
        var eb = await _db.EmployeeBonuses.FirstOrDefaultAsync(x => x.Id == bonusId && x.BonusBatchId == batchId && x.TenantId == tid, ct);
        if (eb == null) return NotFound();
        var batch = await _db.BonusBatches.FirstAsync(x => x.Id == batchId && x.TenantId == tid, ct);
        if (batch.Status != "Draft") return BadRequest("Cannot remove employees from non-Draft batch.");
        eb.IsDeleted = true; eb.UpdatedAtUtc = DateTime.UtcNow;
        batch.TotalAmount -= eb.BonusAmount; batch.EmployeeCount--;
        await _db.SaveChangesAsync(ct);
        return NoContent();
    }

    // ── Batch Workflow ────────────────────────────────────────────────────────

    [HttpPatch("batches/{id:guid}/submit")]
    [Authorize(Roles = "Admin,HR Manager,Finance")]
    public async Task<IActionResult> SubmitBatch(Guid id, CancellationToken ct)
    {
        var tid = GetTenantId();
        var uid = GetUserId();
        var batch = await _db.BonusBatches.FirstOrDefaultAsync(x => x.Id == id && x.TenantId == tid && !x.IsDeleted, ct);
        if (batch == null) return NotFound();
        if (batch.Status != "Draft") return BadRequest("Only Draft batches can be submitted.");
        if (batch.EmployeeCount == 0) return BadRequest("Batch has no employees.");
        batch.Status = "PendingApproval"; batch.UpdatedAtUtc = DateTime.UtcNow; batch.UpdatedBy = uid;
        await _db.SaveChangesAsync(ct);
        await WriteBonusAudit(tid, uid, id, null, "BatchSubmitted", "Draft", "PendingApproval", ct);
        return Ok(batch);
    }

    [HttpPatch("batches/{id:guid}/approve")]
    [Authorize(Roles = "Admin,Finance")]
    public async Task<IActionResult> ApproveBatch(Guid id, [FromBody] BatchApproveRequest req, CancellationToken ct)
    {
        var tid = GetTenantId();
        var uid = GetUserId();
        var batch = await _db.BonusBatches.FirstOrDefaultAsync(x => x.Id == id && x.TenantId == tid && !x.IsDeleted, ct);
        if (batch == null) return NotFound();
        if (batch.Status != "PendingApproval") return BadRequest("Batch is not pending approval.");
        batch.Status = "Approved"; batch.UpdatedAtUtc = DateTime.UtcNow; batch.UpdatedBy = uid;

        var approval = new BonusApproval
        {
            TenantId = tid, BonusBatchId = id, StepOrder = 1, ApproverRole = "Finance",
            ApprovedBy = uid, ApprovedByName = GetUserName(),
            Status = "Approved", Comments = req.Comments ?? string.Empty,
            DecidedAtUtc = DateTime.UtcNow,
        };
        _db.BonusApprovals.Add(approval);

        var bonuses = await _db.EmployeeBonuses.Where(x => x.BonusBatchId == id && !x.IsDeleted).ToListAsync(ct);
        foreach (var b in bonuses) b.Status = "Approved";

        // GL: Bonus Expense Dr / Bonus Payable Cr
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        _db.FinanceGlEntries.Add(new FinanceGlEntry
        {
            TenantId = tid, SourceModule = "Bonus", SourceEntityId = id,
            SourceEntityRef = batch.BatchNumber, EventType = "BonusApproval",
            DebitAccount = "6100 - Employee Bonus Expense",
            CreditAccount = "2300 - Bonus Payable",
            Amount = batch.TotalAmount, Currency = "USD",
            EntryDate = today, Period = batch.PaymentPeriod,
            Description = $"Bonus approval: {batch.BatchName} ({batch.BatchNumber})",
            PostedBy = uid, PostedByName = GetUserName(),
        });

        await _db.SaveChangesAsync(ct);
        await WriteBonusAudit(tid, uid, id, null, "BatchApproved", "PendingApproval",
            JsonSerializer.Serialize(new { Status = "Approved", Comments = req.Comments, batch.TotalAmount }), ct);
        return Ok(batch);
    }

    [HttpPatch("batches/{id:guid}/reject")]
    [Authorize(Roles = "Admin,Finance")]
    public async Task<IActionResult> RejectBatch(Guid id, [FromBody] RejectRequest req, CancellationToken ct)
    {
        var tid = GetTenantId();
        var uid = GetUserId();
        var batch = await _db.BonusBatches.FirstOrDefaultAsync(x => x.Id == id && x.TenantId == tid && !x.IsDeleted, ct);
        if (batch == null) return NotFound();
        batch.Status = "Cancelled"; batch.UpdatedAtUtc = DateTime.UtcNow; batch.UpdatedBy = uid;
        await _db.SaveChangesAsync(ct);
        await WriteBonusAudit(tid, uid, id, null, "BatchRejected", "PendingApproval",
            $"Cancelled: {req.Reason}", ct);
        return Ok(batch);
    }

    [HttpPatch("batches/{id:guid}/mark-paid")]
    [Authorize(Roles = "Admin,Finance")]
    public async Task<IActionResult> MarkBatchPaid(Guid id, [FromBody] MarkBatchPaidRequest req, CancellationToken ct)
    {
        var tid = GetTenantId();
        var uid = GetUserId();
        var batch = await _db.BonusBatches.FirstOrDefaultAsync(x => x.Id == id && x.TenantId == tid && !x.IsDeleted, ct);
        if (batch == null) return NotFound();
        if (batch.Status != "Approved") return BadRequest("Only Approved batches can be marked as paid.");

        batch.Status = "Paid"; batch.IsLockedByPayroll = true;
        batch.UpdatedAtUtc = DateTime.UtcNow; batch.UpdatedBy = uid;

        var bonuses = await _db.EmployeeBonuses.Where(x => x.BonusBatchId == id && !x.IsDeleted).ToListAsync(ct);
        foreach (var b in bonuses)
        {
            b.Status = "PaidInPayroll";
            if (req.PayrollRunId.HasValue) b.PayrollRunId = req.PayrollRunId;
        }

        // GL: Bonus Payable Dr / Cash/Bank Cr
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        _db.FinanceGlEntries.Add(new FinanceGlEntry
        {
            TenantId = tid, SourceModule = "Bonus", SourceEntityId = id,
            SourceEntityRef = batch.BatchNumber, EventType = "BonusPayment",
            DebitAccount = "2300 - Bonus Payable",
            CreditAccount = "1000 - Cash/Bank",
            Amount = batch.TotalAmount, Currency = "USD",
            EntryDate = today, Period = batch.PaymentPeriod,
            Description = $"Bonus payment: {batch.BatchName} ({batch.BatchNumber})",
            PostedBy = uid, PostedByName = GetUserName(),
        });

        await _db.SaveChangesAsync(ct);
        await WriteBonusAudit(tid, uid, id, null, "BatchPaid", "Approved",
            JsonSerializer.Serialize(new { Status = "Paid", PayrollRunId = req.PayrollRunId, batch.TotalAmount }), ct);
        return Ok(batch);
    }

    // ── Payroll Integration ───────────────────────────────────────────────────

    [HttpGet("payroll-pending")]
    [Authorize(Roles = "Admin,Finance,HR Manager")]
    public async Task<IActionResult> GetPayrollPendingBonuses([FromQuery] string paymentPeriod, CancellationToken ct)
    {
        var tid = GetTenantId();
        var bonuses = await _db.EmployeeBonuses
            .Where(x => x.TenantId == tid && !x.IsDeleted && x.Status == "Approved"
                && x.PaymentPeriod == paymentPeriod && x.PayrollRunId == null)
            .ToListAsync(ct);
        return Ok(new { count = bonuses.Count, totalAmount = bonuses.Sum(x => x.BonusAmount), bonuses });
    }

    // ── Audit & Reconciliation Report ────────────────────────────────────────

    [HttpGet("audit")]
    [Authorize(Roles = "Admin,Finance,HR Manager")]
    public async Task<IActionResult> AuditReport([FromQuery] string? period, CancellationToken ct)
    {
        var tid = GetTenantId();
        var batchQuery = _db.BonusBatches.Where(x => x.TenantId == tid && !x.IsDeleted);
        if (!string.IsNullOrEmpty(period)) batchQuery = batchQuery.Where(x => x.PaymentPeriod == period);

        var batches = await batchQuery.OrderByDescending(x => x.CreatedAtUtc).ToListAsync(ct);
        var glEntries = await _db.FinanceGlEntries
            .Where(x => x.TenantId == tid && x.SourceModule == "Bonus").ToListAsync(ct);

        var bonusSummary = await _db.EmployeeBonuses
            .Where(x => x.TenantId == tid && !x.IsDeleted)
            .GroupBy(x => x.Department)
            .Select(g => new { Department = g.Key, Count = g.Count(), TotalAmount = g.Sum(b => b.BonusAmount) })
            .ToListAsync(ct);

        return Ok(new
        {
            GeneratedAtUtc = DateTime.UtcNow,
            Period = period ?? "All",
            TotalBatches = batches.Count,
            ApprovedBatches = batches.Count(x => x.Status == "Approved"),
            PaidBatches = batches.Count(x => x.Status == "Paid"),
            TotalBonusAmount = batches.Sum(x => x.TotalAmount),
            PaidAmount = batches.Where(x => x.Status == "Paid").Sum(x => x.TotalAmount),
            PendingPaymentAmount = batches.Where(x => x.Status == "Approved").Sum(x => x.TotalAmount),
            GlEntriesCount = glEntries.Count,
            ByDepartment = bonusSummary,
            Batches = batches.Select(b => new
            {
                b.BatchNumber, b.BatchName, b.BonusTypeName,
                b.PaymentPeriod, b.PaymentDate, b.EmployeeCount,
                b.TotalAmount, b.Status, b.IsLockedByPayroll,
            }).ToList(),
        });
    }

    private async Task WriteBonusAudit(Guid tid, Guid? uid, Guid? batchId, Guid? bonusId, string action, string? oldVal, string newVal, CancellationToken ct)
    {
        _db.BonusAuditLogs.Add(new BonusAuditLog
        {
            TenantId = tid, BonusBatchId = batchId, EmployeeBonusId = bonusId,
            EntityType = batchId.HasValue ? "BonusBatch" : "EmployeeBonus",
            Action = action, OldValuesJson = oldVal ?? string.Empty, NewValuesJson = newVal,
            PerformedBy = uid, PerformedByName = GetUserName(),
        });
        await _db.SaveChangesAsync(ct);
    }
}

public record BonusTypeRequest(string Code, string NameEn, string? NameAr, string CalculationMethod, bool IsTaxable);
public record CreateBatchRequest(Guid BonusTypeId, string BatchName, string PaymentPeriod, DateOnly PaymentDate, string? Notes);
public record AddEmployeeBonusRequest(Guid EmployeeId, string EmployeeName, string? Department, decimal BasicSalary, string CalculationMethod, decimal CalculationValue, string? Notes);
public record BatchApproveRequest(string? Comments);
public record MarkBatchPaidRequest(Guid? PayrollRunId);
