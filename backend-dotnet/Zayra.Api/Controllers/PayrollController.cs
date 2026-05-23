using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Zayra.Api.Application.Common;
using Zayra.Api.Data;
using Zayra.Api.Models;

namespace Zayra.Api.Controllers;

[ApiController]
[Route("api/payroll")]
[Authorize(Roles = "Admin,HR Manager,Payroll Officer")]
public class PayrollController : ControllerBase
{
    private readonly ZayraDbContext _db;

    public PayrollController(ZayraDbContext db) => _db = db;

    [HttpGet("salary-structures")]
    public async Task<IActionResult> SalaryStructures(CancellationToken cancellationToken)
    {
        var tenantId = GetTenantId();
        return Ok(await _db.SalaryStructures.AsNoTracking().Where(x => x.TenantId == tenantId && !x.IsDeleted).OrderBy(x => x.Name).ToListAsync(cancellationToken));
    }

    [HttpPost("salary-structures")]
    public async Task<IActionResult> CreateSalaryStructure(SalaryStructureRequest req, CancellationToken cancellationToken)
    {
        var tenantId = GetTenantId();
        if (await _db.SalaryStructures.AnyAsync(x => x.TenantId == tenantId && x.Code == req.Code && !x.IsDeleted, cancellationToken))
            return Conflict(new { message = "Salary structure code already exists." });
        var structure = new SalaryStructure { TenantId = tenantId, Code = req.Code.Trim(), Name = req.Name.Trim(), Currency = req.Currency ?? "AED", EffectiveDate = req.EffectiveDate, CreatedBy = GetUserId() };
        _db.SalaryStructures.Add(structure);
        foreach (var component in req.Components ?? Array.Empty<SalaryComponentRequest>())
            _db.SalaryComponents.Add(new SalaryComponent { TenantId = tenantId, SalaryStructureId = structure.Id, Code = component.Code, Name = component.Name, ComponentType = component.ComponentType, CalculationType = component.CalculationType, Amount = component.Amount, Percentage = component.Percentage, IsTaxable = component.IsTaxable });
        await PayrollAudit("payroll.salary_structure.created", "SalaryStructure", structure.Id.ToString(), cancellationToken);
        await _db.SaveChangesAsync(cancellationToken);
        return Created($"/api/payroll/salary-structures/{structure.Id}", structure);
    }

    [HttpPost("employee-salary-structures")]
    public async Task<IActionResult> AssignEmployeeSalary(EmployeeSalaryStructureRequest req, CancellationToken cancellationToken)
    {
        var tenantId = GetTenantId();
        if (!await _db.Employees.AnyAsync(x => x.TenantId == tenantId && x.Id == req.EmployeeId && !x.IsDeleted, cancellationToken)) return BadRequest(new { message = "Employee not found." });
        if (!await _db.SalaryStructures.AnyAsync(x => x.TenantId == tenantId && x.Id == req.SalaryStructureId && !x.IsDeleted, cancellationToken)) return BadRequest(new { message = "Salary structure not found." });
        await _db.EmployeeSalaryStructures.Where(x => x.TenantId == tenantId && x.EmployeeId == req.EmployeeId && x.IsActive).ExecuteUpdateAsync(x => x.SetProperty(p => p.IsActive, false), cancellationToken);
        var assignment = new EmployeeSalaryStructure { TenantId = tenantId, EmployeeId = req.EmployeeId, SalaryStructureId = req.SalaryStructureId, BasicSalary = req.BasicSalary, HousingAllowance = req.HousingAllowance, TransportAllowance = req.TransportAllowance, FoodAllowance = req.FoodAllowance, MobileAllowance = req.MobileAllowance, OtherAllowance = req.OtherAllowance, FixedDeduction = req.FixedDeduction, EffectiveDate = req.EffectiveDate, Currency = req.Currency ?? "AED", CreatedBy = GetUserId() };
        _db.EmployeeSalaryStructures.Add(assignment);
        await PayrollAudit("payroll.employee_salary.assigned", "EmployeeSalaryStructure", assignment.Id.ToString(), cancellationToken);
        await _db.SaveChangesAsync(cancellationToken);
        return Created($"/api/payroll/employee-salary-structures/{assignment.Id}", assignment);
    }

    [HttpGet("runs")]
    public async Task<IActionResult> ListRuns([FromQuery] string? status, [FromQuery] int page = 1, [FromQuery] int pageSize = 25, CancellationToken cancellationToken = default)
    {
        var tenantId = GetTenantId();
        var query = _db.PayrollRuns.Where(r => r.TenantId == tenantId);
        if (!string.IsNullOrWhiteSpace(status)) query = query.Where(r => r.Status == status);
        var total = await query.CountAsync(cancellationToken);
        var items = await query.OrderByDescending(r => r.Year).ThenByDescending(r => r.Month).Skip((page - 1) * pageSize).Take(pageSize).ToListAsync(cancellationToken);
        return Ok(new PagedResult<PayrollRun>(items, total, page, pageSize));
    }

    [HttpPost("runs")]
    public async Task<IActionResult> CreateRun([FromBody] CreatePayrollRunRequest req, CancellationToken cancellationToken)
    {
        var tenantId = GetTenantId();
        if (await _db.PayrollRuns.AnyAsync(r => r.TenantId == tenantId && r.Year == req.Year && r.Month == req.Month, cancellationToken))
            return Conflict(new { message = $"A payroll run for {req.Year}/{req.Month:D2} already exists." });

        var run = new PayrollRun
        {
            TenantId = tenantId,
            Year = req.Year,
            Month = req.Month,
            CreatedByUserId = GetUserId(),
        };
        _db.PayrollRuns.Add(run);
        await _db.SaveChangesAsync(cancellationToken);
        return Created($"/api/payroll/runs/{run.Id}", run);
    }

    [HttpPost("runs/{id:guid}/process")]
    public async Task<IActionResult> Process(Guid id, CancellationToken cancellationToken)
    {
        var tenantId = GetTenantId();
        var run = await _db.PayrollRuns.FirstOrDefaultAsync(r => r.Id == id && r.TenantId == tenantId, cancellationToken);
        if (run is null) return NotFound();
        if (run.Status == "Locked") return BadRequest(new { message = "This run is locked and cannot be reprocessed." });

        var periodStart = new DateOnly(run.Year, run.Month, 1);
        var periodEnd = periodStart.AddMonths(1).AddDays(-1);

        // Delete existing generated rows so reprocessing is idempotent.
        var existingSlips = _db.PayrollSlips.Where(s => s.RunId == id && s.TenantId == tenantId);
        _db.PayrollSlips.RemoveRange(existingSlips);
        _db.PayrollRunEmployees.RemoveRange(_db.PayrollRunEmployees.Where(x => x.TenantId == tenantId && x.PayrollRunId == id));
        _db.PayrollEarnings.RemoveRange(_db.PayrollEarnings.Where(x => x.TenantId == tenantId && x.PayrollRunId == id));
        _db.PayrollDeductions.RemoveRange(_db.PayrollDeductions.Where(x => x.TenantId == tenantId && x.PayrollRunId == id));
        _db.PayrollValidationResults.RemoveRange(_db.PayrollValidationResults.Where(x => x.TenantId == tenantId && x.PayrollRunId == id));

        var employees = await _db.Employees.Where(e => e.TenantId == tenantId && e.Status == "Active" && !e.IsDeleted).ToListAsync(cancellationToken);
        var salaryAssignments = await _db.EmployeeSalaryStructures.AsNoTracking().Where(x => x.TenantId == tenantId && x.IsActive).ToListAsync(cancellationToken);
        var attendanceImpacts = await _db.AttendancePayrollImpacts.AsNoTracking().Where(x => x.TenantId == tenantId && x.WorkDate >= periodStart && x.WorkDate <= periodEnd && x.Status != "Processed").ToListAsync(cancellationToken);
        var leaveImpacts = await _db.LeavePayrollImpacts.AsNoTracking().Where(x => x.TenantId == tenantId && x.PayPeriod == $"{run.Year}-{run.Month:00}" && x.Status != "Processed").ToListAsync(cancellationToken);
        var overtimeImpacts = await _db.OvertimePayrollImpacts.AsNoTracking().Where(x => x.TenantId == tenantId && x.Status != "Processed").ToListAsync(cancellationToken);

        var slips = new List<PayrollSlip>();
        foreach (var e in employees)
        {
            var salary = salaryAssignments.Where(x => x.EmployeeId == e.Id && x.EffectiveDate <= periodEnd).OrderByDescending(x => x.EffectiveDate).FirstOrDefault();
            var basic = salary?.BasicSalary ?? e.Salary ?? 0m;
            if (basic <= 0) _db.PayrollValidationResults.Add(new PayrollValidationResult { TenantId = tenantId, PayrollRunId = id, EmployeeId = e.Id, Severity = "Warning", Code = "MISSING_SALARY", Message = "Employee has no active salary structure or salary amount." });
            var housing = salary?.HousingAllowance ?? 0m;
            var transport = salary?.TransportAllowance ?? 0m;
            var otherAllowances = (salary?.FoodAllowance ?? 0m) + (salary?.MobileAllowance ?? 0m) + (salary?.OtherAllowance ?? 0m);
            var gross = basic + housing + transport + otherAllowances;
            var fixedDeduction = salary?.FixedDeduction ?? 0m;
            var hourlyRate = Math.Round(basic / 240m, 2);
            var attendanceDeduction = Math.Round(attendanceImpacts.Where(x => x.EmployeeId == e.Id && x.ImpactType.Contains("deduction", StringComparison.OrdinalIgnoreCase)).Sum(x => x.Minutes) / 60m * hourlyRate, 2);
            var absenceDeduction = Math.Round(attendanceImpacts.Where(x => x.EmployeeId == e.Id && x.ImpactType.Contains("Absence", StringComparison.OrdinalIgnoreCase)).Sum(x => x.Minutes) / 60m * hourlyRate, 2);
            var leaveDeduction = leaveImpacts.Where(x => x.EmployeeId == e.Id && x.ImpactType.Contains("Deduction", StringComparison.OrdinalIgnoreCase)).Sum(x => x.Amount);
            var overtimePay = overtimeImpacts.Where(x => x.EmployeeId == e.Id).Sum(x => x.Amount);
            var deductions = fixedDeduction + attendanceDeduction + absenceDeduction + leaveDeduction;
            var slip = new PayrollSlip
            {
                TenantId = tenantId,
                RunId = id,
                EmployeeId = e.Id,
                EmployeeCode = e.EmployeeCode,
                EmployeeName = e.FullName,
                Department = e.Department,
                BasicSalary = basic,
                HousingAllowance = housing,
                TransportAllowance = transport,
                OtherAllowances = otherAllowances + overtimePay,
                GrossSalary = gross + overtimePay,
                Deductions = deductions,
                NetSalary = gross + overtimePay - deductions,
                Status = "Draft",
            };
            slips.Add(slip);
            _db.PayrollRunEmployees.Add(new PayrollRunEmployee { TenantId = tenantId, PayrollRunId = id, EmployeeId = e.Id, GrossEarnings = slip.GrossSalary, TotalDeductions = deductions, NetPay = slip.NetSalary });
            AddEarning(tenantId, id, e.Id, "BASIC", "Basic salary", basic, "Salary");
            if (housing > 0) AddEarning(tenantId, id, e.Id, "HOUSING", "Housing allowance", housing, "Salary");
            if (transport > 0) AddEarning(tenantId, id, e.Id, "TRANSPORT", "Transport allowance", transport, "Salary");
            if (otherAllowances > 0) AddEarning(tenantId, id, e.Id, "OTHER_ALLOWANCES", "Other allowances", otherAllowances, "Salary");
            if (overtimePay > 0) AddEarning(tenantId, id, e.Id, "OVERTIME", "Approved overtime", overtimePay, "Overtime");
            if (fixedDeduction > 0) AddDeduction(tenantId, id, e.Id, "FIXED_DEDUCTION", "Fixed deduction", fixedDeduction, "Salary");
            if (attendanceDeduction > 0) AddDeduction(tenantId, id, e.Id, "ATTENDANCE", "Late/early attendance deduction", attendanceDeduction, "Attendance");
            if (absenceDeduction > 0) AddDeduction(tenantId, id, e.Id, "ABSENCE", "Absence deduction", absenceDeduction, "Attendance");
            if (leaveDeduction > 0) AddDeduction(tenantId, id, e.Id, "LEAVE", "Leave deduction", leaveDeduction, "Leave");
            if (overtimePay > gross * 0.35m && gross > 0) _db.PayrollValidationResults.Add(new PayrollValidationResult { TenantId = tenantId, PayrollRunId = id, EmployeeId = e.Id, Severity = "Warning", Code = "UNUSUAL_OVERTIME", Message = "Overtime payout is above 35% of regular gross earnings." });
        }

        _db.PayrollSlips.AddRange(slips);
        run.Status = "Processed";
        run.ProcessedAtUtc = DateTime.UtcNow;
        run.EmployeeCount = slips.Count;
        run.TotalGrossSalary = slips.Sum(s => s.GrossSalary);
        run.TotalDeductions = slips.Sum(s => s.Deductions);
        run.TotalNetSalary = slips.Sum(s => s.NetSalary);
        await _db.AttendancePayrollImpacts.Where(x => x.TenantId == tenantId && x.WorkDate >= periodStart && x.WorkDate <= periodEnd && x.Status != "Processed").ExecuteUpdateAsync(x => x.SetProperty(p => p.Status, "Processed"), cancellationToken);
        await _db.LeavePayrollImpacts.Where(x => x.TenantId == tenantId && x.PayPeriod == $"{run.Year}-{run.Month:00}" && x.Status != "Processed").ExecuteUpdateAsync(x => x.SetProperty(p => p.Status, "Processed").SetProperty(p => p.ProcessedAtUtc, DateTime.UtcNow), cancellationToken);
        await _db.OvertimePayrollImpacts.Where(x => x.TenantId == tenantId && x.Status != "Processed").ExecuteUpdateAsync(x => x.SetProperty(p => p.Status, "Processed").SetProperty(p => p.PayrollRunId, id).SetProperty(p => p.ProcessedAtUtc, DateTime.UtcNow), cancellationToken);
        await PayrollAudit("payroll.run.processed", "PayrollRun", run.Id.ToString(), cancellationToken);
        await _db.SaveChangesAsync(cancellationToken);
        return Ok(run);
    }

    [HttpPost("runs/{id:guid}/lock")]
    public async Task<IActionResult> Lock(Guid id, CancellationToken cancellationToken)
    {
        var tenantId = GetTenantId();
        var run = await _db.PayrollRuns.FirstOrDefaultAsync(r => r.Id == id && r.TenantId == tenantId, cancellationToken);
        if (run is null) return NotFound();
        if (run.Status != "Processed" && run.Status != "Approved") return BadRequest(new { message = "Only processed or approved runs can be locked." });
        run.Status = "Locked";
        run.LockedAtUtc = DateTime.UtcNow;
        await _db.PayrollSlips.Where(s => s.RunId == id).ExecuteUpdateAsync(s => s.SetProperty(p => p.Status, "Final"), cancellationToken);
        await _db.Payslips.Where(s => s.PayrollRunId == id && s.TenantId == tenantId).ExecuteUpdateAsync(s => s.SetProperty(p => p.IsPublishedToEss, true).SetProperty(p => p.PublishedAtUtc, DateTime.UtcNow), cancellationToken);
        await PayrollAudit("payroll.run.locked", "PayrollRun", id.ToString(), cancellationToken);
        await _db.SaveChangesAsync(cancellationToken);
        return Ok(run);
    }

    [HttpGet("runs/{id:guid}/slips")]
    public async Task<IActionResult> Slips(Guid id, [FromQuery] int page = 1, [FromQuery] int pageSize = 50, CancellationToken cancellationToken = default)
    {
        var tenantId = GetTenantId();
        var query = _db.PayrollSlips.Where(s => s.RunId == id && s.TenantId == tenantId);
        var total = await query.CountAsync(cancellationToken);
        var items = await query.OrderBy(s => s.EmployeeCode).Skip((page - 1) * pageSize).Take(pageSize).ToListAsync(cancellationToken);
        return Ok(new PagedResult<PayrollSlip>(items, total, page, pageSize));
    }

    [HttpPost("runs/{id:guid}/validate")]
    public async Task<IActionResult> Validate(Guid id, CancellationToken cancellationToken)
    {
        var tenantId = GetTenantId();
        if (!await _db.PayrollRuns.AnyAsync(x => x.TenantId == tenantId && x.Id == id, cancellationToken)) return NotFound();
        return Ok(await _db.PayrollValidationResults.AsNoTracking().Where(x => x.TenantId == tenantId && x.PayrollRunId == id).OrderByDescending(x => x.Severity).ToListAsync(cancellationToken));
    }

    [HttpPost("runs/{id:guid}/approve")]
    public async Task<IActionResult> Approve(Guid id, PayrollDecisionRequest req, CancellationToken cancellationToken)
    {
        var tenantId = GetTenantId();
        var run = await _db.PayrollRuns.FirstOrDefaultAsync(x => x.TenantId == tenantId && x.Id == id, cancellationToken);
        if (run is null) return NotFound();
        _db.PayrollApprovals.Add(new PayrollApproval { TenantId = tenantId, PayrollRunId = id, Decision = "Approved", Notes = req.Notes ?? string.Empty, DecidedByUserId = GetUserId(), DecidedAtUtc = DateTime.UtcNow });
        run.Status = "Approved";
        await PayrollAudit("payroll.run.approved", "PayrollRun", id.ToString(), cancellationToken);
        await _db.SaveChangesAsync(cancellationToken);
        return Ok(run);
    }

    [HttpPost("runs/{id:guid}/payslips/generate")]
    public async Task<IActionResult> GeneratePayslips(Guid id, CancellationToken cancellationToken)
    {
        var tenantId = GetTenantId();
        var slips = await _db.PayrollSlips.AsNoTracking().Where(x => x.TenantId == tenantId && x.RunId == id).ToListAsync(cancellationToken);
        foreach (var slip in slips)
        {
            if (await _db.Payslips.AnyAsync(x => x.TenantId == tenantId && x.PayrollRunId == id && x.EmployeeId == slip.EmployeeId, cancellationToken)) continue;
            var payslip = new Payslip { TenantId = tenantId, PayrollRunId = id, EmployeeId = slip.EmployeeId, PayslipNumber = $"PS-{slip.EmployeeCode}-{DateTime.UtcNow:yyyyMMddHHmmss}" };
            _db.Payslips.Add(payslip);
            _db.PayslipComponents.Add(new PayslipComponent { TenantId = tenantId, PayslipId = payslip.Id, ComponentType = "Earning", ComponentName = "Gross earnings", Amount = slip.GrossSalary });
            _db.PayslipComponents.Add(new PayslipComponent { TenantId = tenantId, PayslipId = payslip.Id, ComponentType = "Deduction", ComponentName = "Total deductions", Amount = slip.Deductions });
            _db.PayslipComponents.Add(new PayslipComponent { TenantId = tenantId, PayslipId = payslip.Id, ComponentType = "Net", ComponentName = "Net pay", Amount = slip.NetSalary });
        }
        await PayrollAudit("payroll.payslips.generated", "PayrollRun", id.ToString(), cancellationToken);
        await _db.SaveChangesAsync(cancellationToken);
        return Ok(await _db.Payslips.AsNoTracking().Where(x => x.TenantId == tenantId && x.PayrollRunId == id).ToListAsync(cancellationToken));
    }

    [HttpPost("runs/{id:guid}/payment-batches")]
    public async Task<IActionResult> CreatePaymentBatch(Guid id, PayrollPaymentBatchRequest req, CancellationToken cancellationToken)
    {
        var tenantId = GetTenantId();
        var run = await _db.PayrollRuns.FirstOrDefaultAsync(x => x.TenantId == tenantId && x.Id == id, cancellationToken);
        if (run is null) return NotFound();
        var slips = await _db.PayrollSlips.AsNoTracking().Where(x => x.TenantId == tenantId && x.RunId == id).ToListAsync(cancellationToken);
        var profiles = await _db.EmployeePayrollProfiles.AsNoTracking().Where(x => x.TenantId == tenantId && !x.IsDeleted).ToListAsync(cancellationToken);
        var batch = new PayrollPaymentBatch { TenantId = tenantId, PayrollRunId = id, BatchNumber = $"PAY-{run.Year}{run.Month:00}-{DateTime.UtcNow:HHmmss}", PaymentMethod = req.PaymentMethod ?? "WPS", TotalAmount = slips.Sum(x => x.NetSalary), Currency = req.Currency ?? "AED" };
        _db.PayrollPaymentBatches.Add(batch);
        foreach (var slip in slips)
        {
            var profile = profiles.FirstOrDefault(x => x.EmployeeId == slip.EmployeeId);
            if (string.IsNullOrWhiteSpace(profile?.Iban))
                _db.PayrollValidationResults.Add(new PayrollValidationResult { TenantId = tenantId, PayrollRunId = id, EmployeeId = slip.EmployeeId, Severity = "Warning", Code = "MISSING_IBAN", Message = "Employee is missing IBAN for payment file." });
            _db.PayrollPaymentRecords.Add(new PayrollPaymentRecord { TenantId = tenantId, PaymentBatchId = batch.Id, EmployeeId = slip.EmployeeId, Amount = slip.NetSalary, Iban = profile?.Iban ?? string.Empty, Status = "Pending", WpsReference = $"WPS-{slip.EmployeeCode}-{run.Year}{run.Month:00}" });
        }
        await PayrollAudit("payroll.payment_batch.created", "PayrollPaymentBatch", batch.Id.ToString(), cancellationToken);
        await _db.SaveChangesAsync(cancellationToken);
        return Created($"/api/payroll/payment-batches/{batch.Id}", batch);
    }

    [HttpPost("payment-batches/{id:guid}/wps-file")]
    public async Task<IActionResult> GenerateWps(Guid id, CancellationToken cancellationToken)
    {
        var tenantId = GetTenantId();
        var batch = await _db.PayrollPaymentBatches.FirstOrDefaultAsync(x => x.TenantId == tenantId && x.Id == id, cancellationToken);
        if (batch is null) return NotFound();
        var records = await _db.PayrollPaymentRecords.AsNoTracking().Where(x => x.TenantId == tenantId && x.PaymentBatchId == id).ToListAsync(cancellationToken);
        var wps = new WPSFileBatch { TenantId = tenantId, PaymentBatchId = id, SifFileName = $"SIF-{batch.BatchNumber}.txt" };
        _db.WPSFileBatches.Add(wps);
        foreach (var record in records)
            _db.SIFFileRecords.Add(new SIFFileRecord { TenantId = tenantId, WPSFileBatchId = wps.Id, EmployeeId = record.EmployeeId, Iban = record.Iban, NetPay = record.Amount });
        batch.Status = "FileGenerated";
        await PayrollAudit("payroll.wps.generated", "WPSFileBatch", wps.Id.ToString(), cancellationToken);
        await _db.SaveChangesAsync(cancellationToken);
        return Ok(wps);
    }

    [HttpGet("employee-salary-structures")]
    public async Task<IActionResult> ListEmployeeSalaryStructures([FromQuery] int? employeeId, CancellationToken cancellationToken)
    {
        var tenantId = GetTenantId();
        var query = _db.EmployeeSalaryStructures.AsNoTracking().Where(x => x.TenantId == tenantId);
        if (employeeId.HasValue) query = query.Where(x => x.EmployeeId == employeeId.Value);
        return Ok(await query.OrderByDescending(x => x.EffectiveDate).ToListAsync(cancellationToken));
    }

    [HttpGet("payment-batches")]
    public async Task<IActionResult> ListPaymentBatches([FromQuery] Guid? runId, CancellationToken cancellationToken)
    {
        var tenantId = GetTenantId();
        var query = _db.PayrollPaymentBatches.AsNoTracking().Where(x => x.TenantId == tenantId);
        if (runId.HasValue) query = query.Where(x => x.PayrollRunId == runId.Value);
        return Ok(await query.OrderByDescending(x => x.CreatedAtUtc).ToListAsync(cancellationToken));
    }

    [HttpGet("payment-batches/{id:guid}/records")]
    public async Task<IActionResult> PaymentRecords(Guid id, CancellationToken cancellationToken)
    {
        var tenantId = GetTenantId();
        return Ok(await _db.PayrollPaymentRecords.AsNoTracking().Where(x => x.TenantId == tenantId && x.PaymentBatchId == id).ToListAsync(cancellationToken));
    }

    [HttpGet("runs/{id:guid}/payslips")]
    public async Task<IActionResult> ListPayslips(Guid id, [FromQuery] int page = 1, [FromQuery] int pageSize = 50, CancellationToken cancellationToken = default)
    {
        var tenantId = GetTenantId();
        var query = _db.Payslips.Where(x => x.TenantId == tenantId && x.PayrollRunId == id);
        var total = await query.CountAsync(cancellationToken);
        var items = await query.OrderBy(x => x.EmployeeId).Skip((page - 1) * pageSize).Take(pageSize).ToListAsync(cancellationToken);
        return Ok(new PagedResult<Payslip>(items, total, page, pageSize));
    }

    [HttpGet("runs/{id:guid}/approvals")]
    public async Task<IActionResult> ListRunApprovals(Guid id, CancellationToken cancellationToken)
    {
        var tenantId = GetTenantId();
        return Ok(await _db.PayrollApprovals.AsNoTracking().Where(x => x.TenantId == tenantId && x.PayrollRunId == id).OrderByDescending(x => x.DecidedAtUtc).ToListAsync(cancellationToken));
    }

    [HttpGet("groups")]
    public async Task<IActionResult> ListGroups(CancellationToken cancellationToken)
    {
        var tenantId = GetTenantId();
        return Ok(await _db.PayrollGroups.AsNoTracking().Where(x => x.TenantId == tenantId && x.IsActive).ToListAsync(cancellationToken));
    }

    [HttpPost("groups")]
    public async Task<IActionResult> CreateGroup(PayrollGroupRequest req, CancellationToken cancellationToken)
    {
        var tenantId = GetTenantId();
        var group = new PayrollGroup { TenantId = tenantId, Code = req.Code.Trim(), Name = req.Name.Trim(), Currency = req.Currency ?? "AED" };
        _db.PayrollGroups.Add(group);
        await _db.SaveChangesAsync(cancellationToken);
        return Created($"/api/payroll/groups/{group.Id}", group);
    }

    [HttpGet("reports/register")]
    public async Task<IActionResult> Register([FromQuery] Guid runId, CancellationToken cancellationToken)
    {
        var tenantId = GetTenantId();
        return Ok(await _db.PayrollSlips.AsNoTracking().Where(x => x.TenantId == tenantId && x.RunId == runId).OrderBy(x => x.EmployeeCode).ToListAsync(cancellationToken));
    }

    [HttpGet("reports/summary")]
    public async Task<IActionResult> ReportSummary(CancellationToken cancellationToken)
    {
        var tenantId = GetTenantId();
        var runs = await _db.PayrollRuns.AsNoTracking().Where(x => x.TenantId == tenantId).ToListAsync(cancellationToken);
        return Ok(new
        {
            totalRuns = runs.Count,
            lockedRuns = runs.Count(x => x.Status == "Locked"),
            totalEmployeesPaid = runs.Where(x => x.Status == "Locked").Sum(x => x.EmployeeCount),
            totalGrossYtd = runs.Where(x => x.Status == "Locked" && x.Year == DateTime.UtcNow.Year).Sum(x => x.TotalGrossSalary),
            totalNetYtd = runs.Where(x => x.Status == "Locked" && x.Year == DateTime.UtcNow.Year).Sum(x => x.TotalNetSalary),
        });
    }

    [HttpGet("ai-validation")]
    public async Task<IActionResult> AiValidation([FromQuery] Guid runId, CancellationToken cancellationToken)
    {
        var tenantId = GetTenantId();
        var warnings = await _db.PayrollValidationResults.AsNoTracking().Where(x => x.TenantId == tenantId && x.PayrollRunId == runId).ToListAsync(cancellationToken);
        return Ok(new { advisoryOnly = true, warnings, summary = "AI payroll validation is advisory. It does not approve payroll or change salaries automatically." });
    }

    private void AddEarning(Guid tenantId, Guid runId, int employeeId, string code, string name, decimal amount, string source) =>
        _db.PayrollEarnings.Add(new PayrollEarning { TenantId = tenantId, PayrollRunId = runId, EmployeeId = employeeId, ComponentCode = code, ComponentName = name, Amount = amount, Source = source });

    private void AddDeduction(Guid tenantId, Guid runId, int employeeId, string code, string name, decimal amount, string source) =>
        _db.PayrollDeductions.Add(new PayrollDeduction { TenantId = tenantId, PayrollRunId = runId, EmployeeId = employeeId, ComponentCode = code, ComponentName = name, Amount = amount, Source = source });

    private async Task PayrollAudit(string action, string entity, string entityId, CancellationToken ct)
    {
        _db.PayrollAuditLogs.Add(new PayrollAuditLog { TenantId = GetTenantId(), Action = action, EntityName = entity, EntityId = entityId, UserId = GetUserId() });
        await Task.CompletedTask;
    }

    private Guid GetTenantId() => Guid.Parse(User.FindFirstValue("tenant_id")!);
    private Guid? GetUserId() => Guid.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier) ?? User.FindFirstValue("sub"), out var id) ? id : null;
}

public record CreatePayrollRunRequest(int Year, int Month);
public record SalaryStructureRequest(string Code, string Name, string? Currency, DateOnly EffectiveDate, IReadOnlyCollection<SalaryComponentRequest>? Components);
public record SalaryComponentRequest(string Code, string Name, string ComponentType, string CalculationType, decimal Amount, decimal Percentage, bool IsTaxable);
public record EmployeeSalaryStructureRequest(int EmployeeId, Guid SalaryStructureId, decimal BasicSalary, decimal HousingAllowance, decimal TransportAllowance, decimal FoodAllowance, decimal MobileAllowance, decimal OtherAllowance, decimal FixedDeduction, DateOnly EffectiveDate, string? Currency);
public record PayrollDecisionRequest(string? Notes);
public record PayrollPaymentBatchRequest(string? PaymentMethod, string? Currency);
public record PayrollGroupRequest(string Code, string Name, string? Currency);
