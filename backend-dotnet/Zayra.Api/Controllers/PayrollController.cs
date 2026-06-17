using System.Security.Claims;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Zayra.Api.Application.Common;
using Zayra.Api.Data;
using Zayra.Api.Infrastructure.Notifications;
using Zayra.Api.Models;

namespace Zayra.Api.Controllers;

[ApiController]
[Route("api/payroll")]
[Authorize(Roles = "Admin,HR Manager,Payroll Manager,Payroll Officer")]
public class PayrollController : ControllerBase
{
    private readonly ZayraDbContext _db;
    private readonly IDataScopeService _scopeService;
    private readonly IHttpContextAccessor _http;
    private readonly INotificationService _notifications;

    public PayrollController(ZayraDbContext db, IDataScopeService scopeService, IHttpContextAccessor http, INotificationService notifications)
    {
        _db = db;
        _scopeService = scopeService;
        _http = http;
        _notifications = notifications;
    }

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
        var structure = new SalaryStructure { TenantId = tenantId, Code = req.Code.Trim(), Name = req.Name.Trim(), Currency = req.Currency ?? "USD", EffectiveDate = req.EffectiveDate, CreatedBy = GetUserId() };
        _db.SalaryStructures.Add(structure);
        foreach (var component in req.Components ?? Array.Empty<SalaryComponentRequest>())
            _db.SalaryComponents.Add(new SalaryComponent { TenantId = tenantId, SalaryStructureId = structure.Id, Code = component.Code, Name = component.Name, ComponentType = component.ComponentType, CalculationType = component.CalculationType, Amount = component.Amount, Percentage = component.Percentage, IsTaxable = component.IsTaxable });
        await PayrollAudit("payroll.salary_structure.created", "SalaryStructure", structure.Id.ToString(), null, cancellationToken);
        await _db.SaveChangesAsync(cancellationToken);
        return Created($"/api/payroll/salary-structures/{structure.Id}", structure);
    }

    [HttpPost("employee-salary-structures")]
    public async Task<IActionResult> AssignEmployeeSalary(EmployeeSalaryStructureRequest req, CancellationToken cancellationToken)
    {
        var tenantId = GetTenantId();
        // M3: reject zero/negative basic salary at assignment time
        if (req.BasicSalary <= 0)
            return BadRequest(new { message = "Basic salary must be greater than zero." });
        if (!await _db.Employees.AnyAsync(x => x.TenantId == tenantId && x.Id == req.EmployeeId && !x.IsDeleted, cancellationToken)) return BadRequest(new { message = "Employee not found." });
        if (!await _db.SalaryStructures.AnyAsync(x => x.TenantId == tenantId && x.Id == req.SalaryStructureId && !x.IsDeleted, cancellationToken)) return BadRequest(new { message = "Salary structure not found." });
        await _db.EmployeeSalaryStructures.Where(x => x.TenantId == tenantId && x.EmployeeId == req.EmployeeId && x.IsActive).ExecuteUpdateAsync(x => x.SetProperty(p => p.IsActive, false), cancellationToken);
        var assignment = new EmployeeSalaryStructure { TenantId = tenantId, EmployeeId = req.EmployeeId, SalaryStructureId = req.SalaryStructureId, BasicSalary = req.BasicSalary, HousingAllowance = req.HousingAllowance, TransportAllowance = req.TransportAllowance, FoodAllowance = req.FoodAllowance, MobileAllowance = req.MobileAllowance, OtherAllowance = req.OtherAllowance, FixedDeduction = req.FixedDeduction, EffectiveDate = req.EffectiveDate, Currency = req.Currency ?? "USD", CreatedBy = GetUserId() };
        _db.EmployeeSalaryStructures.Add(assignment);
        await PayrollAudit("payroll.employee_salary.assigned", "EmployeeSalaryStructure", assignment.Id.ToString(), new { employeeId = req.EmployeeId, basicSalary = req.BasicSalary }, cancellationToken);
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
        // H2: validate calendar bounds
        if (req.Month < 1 || req.Month > 12)
            return BadRequest(new { message = "Month must be between 1 and 12." });
        if (req.Year < 2000 || req.Year > 2100)
            return BadRequest(new { message = "Year is out of range." });
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
        // C2: approved runs cannot be silently overwritten
        if (run.Status is "Locked" or "Approved")
            return BadRequest(new { message = $"A run in '{run.Status}' status cannot be reprocessed. To reprocess, the approval must be revoked first." });

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

        // Load salary structure components (for IsTaxable-based tax deduction)
        var structureIds = salaryAssignments.Select(x => x.SalaryStructureId).Distinct().ToList();
        var salaryComponents = await _db.SalaryComponents.AsNoTracking()
            .Where(x => x.TenantId == tenantId && x.SalaryStructureId.HasValue && structureIds.Contains(x.SalaryStructureId!.Value))
            .ToListAsync(cancellationToken);

        // Income tax rate from System Settings (0 if not configured — GCC has no personal income tax by default)
        var taxRateSetting = await _db.SystemSettings.AsNoTracking()
            .Where(x => x.Category == "Payroll" && x.SettingKey == "IncomeTaxRate")
            .Select(x => x.SettingValue)
            .FirstOrDefaultAsync(cancellationToken);
        decimal.TryParse(taxRateSetting, out var incomeTaxRate); // 0 if unset
        var gosiRateSetting = await _db.SystemSettings.AsNoTracking()
            .Where(x => x.Category == "Payroll" && x.SettingKey == "GosiEmployeeRate")
            .Select(x => x.SettingValue)
            .FirstOrDefaultAsync(cancellationToken);
        decimal.TryParse(gosiRateSetting, out var gosiEmployeeRate); // 0 if unset — GCC GOSI/social insurance
        var attendanceImpacts = await _db.AttendancePayrollImpacts.AsNoTracking().Where(x => x.TenantId == tenantId && x.WorkDate >= periodStart && x.WorkDate <= periodEnd && x.Status != "Processed").ToListAsync(cancellationToken);
        var leaveImpacts = await _db.LeavePayrollImpacts.AsNoTracking().Where(x => x.TenantId == tenantId && x.PayPeriod == $"{run.Year}-{run.Month:00}" && x.Status != "Processed").ToListAsync(cancellationToken);

        // C4: filter overtime impacts to the current pay period only (via WorkDate on the originating request)
        var periodOvertimeRequestIds = await _db.OvertimeRequests.AsNoTracking()
            .Where(r => r.TenantId == tenantId && r.WorkDate >= periodStart && r.WorkDate <= periodEnd)
            .Select(r => r.Id)
            .ToListAsync(cancellationToken);
        var overtimeImpacts = await _db.OvertimePayrollImpacts.AsNoTracking()
            .Where(x => x.TenantId == tenantId && x.Status != "Processed" && periodOvertimeRequestIds.Contains(x.OvertimeRequestId))
            .ToListAsync(cancellationToken);

        // L1: use policy-configured monthly hours as divisor; fall back to 240
        var standardMonthlyHours = await _db.OvertimePolicies.AsNoTracking()
            .Where(p => p.TenantId == tenantId && p.IsActive && !p.IsDeleted)
            .OrderBy(p => p.CreatedAtUtc)
            .Select(p => (int?)p.StandardMonthlyHours)
            .FirstOrDefaultAsync(cancellationToken) ?? 240;

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
            var hourlyRate = standardMonthlyHours > 0 ? Math.Round(basic / standardMonthlyHours, 2) : 0m;
            var attendanceDeduction = Math.Round(attendanceImpacts.Where(x => x.EmployeeId == e.Id && x.ImpactType.Contains("deduction", StringComparison.OrdinalIgnoreCase)).Sum(x => x.Minutes) / 60m * hourlyRate, 2);
            var absenceDeduction = Math.Round(attendanceImpacts.Where(x => x.EmployeeId == e.Id && x.ImpactType.Contains("Absence", StringComparison.OrdinalIgnoreCase)).Sum(x => x.Minutes) / 60m * hourlyRate, 2);
            var leaveDeduction = leaveImpacts.Where(x => x.EmployeeId == e.Id && x.ImpactType.Contains("Deduction", StringComparison.OrdinalIgnoreCase)).Sum(x => x.Amount);
            var overtimePay = overtimeImpacts.Where(x => x.EmployeeId == e.Id).Sum(x => x.Amount);
            // Tax deduction: apply income tax rate to taxable components only
            decimal taxDeduction = 0m;
            if (incomeTaxRate > 0 && salary is not null)
            {
                var structureComponents = salaryComponents.Where(c => c.SalaryStructureId == salary.SalaryStructureId && c.IsTaxable).ToList();
                // If no explicit taxable components defined, treat basic salary as taxable
                var taxableBase = structureComponents.Count > 0
                    ? structureComponents.Sum(c => c.CalculationType == "Percentage" ? basic * c.Percentage / 100m : c.Amount)
                    : basic;
                taxDeduction = Math.Round(taxableBase * incomeTaxRate / 100m, 2);
            }

            var gosiDeduction = gosiEmployeeRate > 0 && basic > 0 ? Math.Round(basic * gosiEmployeeRate / 100m, 2) : 0m;
            var deductions = fixedDeduction + attendanceDeduction + absenceDeduction + leaveDeduction + taxDeduction + gosiDeduction;
            // C3: net salary cannot be negative (GCC labour law)
            var netSalary = Math.Max(0m, gross + overtimePay - deductions);
            if (gross + overtimePay - deductions < 0)
                _db.PayrollValidationResults.Add(new PayrollValidationResult { TenantId = tenantId, PayrollRunId = id, EmployeeId = e.Id, Severity = "Error", Code = "NEGATIVE_NET", Message = "Calculated net salary is negative. Deductions exceed gross pay. Run blocked for this employee." });
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
                NetSalary = netSalary,
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
            if (taxDeduction > 0) AddDeduction(tenantId, id, e.Id, "INCOME_TAX", $"Income tax ({incomeTaxRate}%)", taxDeduction, "Tax");
            if (attendanceDeduction > 0) AddDeduction(tenantId, id, e.Id, "ATTENDANCE", "Late/early attendance deduction", attendanceDeduction, "Attendance");
            if (absenceDeduction > 0) AddDeduction(tenantId, id, e.Id, "ABSENCE", "Absence deduction", absenceDeduction, "Attendance");
            if (leaveDeduction > 0) AddDeduction(tenantId, id, e.Id, "LEAVE", "Leave deduction", leaveDeduction, "Leave");
            if (gosiDeduction > 0) AddDeduction(tenantId, id, e.Id, "GOSI_EMPLOYEE", $"GOSI employee contribution ({gosiEmployeeRate}%)", gosiDeduction, "GOSI");
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
        await _db.OvertimePayrollImpacts
            .Where(x => x.TenantId == tenantId && x.Status != "Processed" && periodOvertimeRequestIds.Contains(x.OvertimeRequestId))
            .ExecuteUpdateAsync(x => x.SetProperty(p => p.Status, "Processed").SetProperty(p => p.PayrollRunId, id).SetProperty(p => p.ProcessedAtUtc, DateTime.UtcNow), cancellationToken);
        await PayrollAudit("payroll.run.processed", "PayrollRun", run.Id.ToString(), new { employeeCount = slips.Count, totalNet = run.TotalNetSalary }, cancellationToken);
        await _db.SaveChangesAsync(cancellationToken);
        return Ok(run);
    }

    [HttpPost("runs/{id:guid}/lock")]
    public async Task<IActionResult> Lock(Guid id, CancellationToken cancellationToken)
    {
        var tenantId = GetTenantId();
        var run = await _db.PayrollRuns.FirstOrDefaultAsync(r => r.Id == id && r.TenantId == tenantId, cancellationToken);
        if (run is null) return NotFound();
        if (run.Status is not ("Processed" or "Approved" or "PendingFinanceReview")) return BadRequest(new { message = "Only processed, pending finance review, or approved runs can be locked." });
        run.Status = "Locked";
        run.LockedAtUtc = DateTime.UtcNow;
        await _db.PayrollSlips.Where(s => s.RunId == id).ExecuteUpdateAsync(s => s.SetProperty(p => p.Status, "Final"), cancellationToken);
        await _db.Payslips.Where(s => s.PayrollRunId == id && s.TenantId == tenantId).ExecuteUpdateAsync(s => s.SetProperty(p => p.IsPublishedToEss, true).SetProperty(p => p.PublishedAtUtc, DateTime.UtcNow), cancellationToken);
        await PayrollAudit("payroll.run.locked", "PayrollRun", id.ToString(), null, cancellationToken);
        await _db.SaveChangesAsync(cancellationToken);
        // Notify all employees with a payslip for this run
        var employeeIds = await _db.PayrollSlips.AsNoTracking().Where(s => s.RunId == id && s.TenantId == tenantId).Select(s => s.EmployeeId).ToListAsync(cancellationToken);
        var usersByEmployee = await _db.Users.AsNoTracking()
            .Where(u => u.TenantId == tenantId)
            .Join(_db.Employees.AsNoTracking().Where(e => employeeIds.Contains(e.Id)), u => u.Email, e => e.WorkEmail, (u, e) => new { u.Id, u.Email, u.FullName })
            .ToListAsync(cancellationToken);
        foreach (var user in usersByEmployee)
        {
            try
            {
                await _notifications.SendEmailAsync(tenantId, "PAYSLIP_READY", user.Email, user.FullName,
                    new Dictionary<string, string> { ["EmployeeName"] = user.FullName, ["Month"] = run.Month.ToString("D2"), ["Year"] = run.Year.ToString(), ["Subject"] = $"Your payslip for {run.Year}/{run.Month:D2} is ready" },
                    cancellationToken);
            }
            catch { /* best-effort per employee */ }
        }
        return Ok(run);
    }

    [HttpGet("runs/{id:guid}/slips")]
    public async Task<IActionResult> Slips(Guid id, [FromQuery] int page = 1, [FromQuery] int pageSize = 50, CancellationToken cancellationToken = default)
    {
        var tenantId = GetTenantId();
        var scope = await _scopeService.ResolveAsync(User, tenantId, cancellationToken);
        var query = _db.PayrollSlips.Where(s => s.RunId == id && s.TenantId == tenantId);
        if (!scope.IsUnrestricted)
            query = query.Where(s => scope.AllowedEmployeeIds!.Contains(s.EmployeeId));
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

    // C1: segregation of duties — Payroll Officer who processes cannot self-approve.
    // Two-step: Payroll Manager/HR advances Processed → PendingFinanceReview (level 1);
    // Finance Controller/Approver finalises PendingFinanceReview → Approved (level 2).
    // Admin bypasses all levels.
    [HttpPost("runs/{id:guid}/approve")]
    [Authorize(Roles = "Admin,HR Manager,Finance Approver,Finance Controller,Payroll Manager")]
    public async Task<IActionResult> Approve(Guid id, PayrollDecisionRequest req, CancellationToken cancellationToken)
    {
        var tenantId = GetTenantId();
        var run = await _db.PayrollRuns.FirstOrDefaultAsync(x => x.TenantId == tenantId && x.Id == id, cancellationToken);
        if (run is null) return NotFound();
        if (run.Status != "Processed" && run.Status != "PendingFinanceReview")
            return BadRequest(new { message = "Only a Processed or PendingFinanceReview run can be approved." });

        var isAdmin = User.IsInRole("Admin");
        var isHROrPayroll = User.IsInRole("HR Manager") || User.IsInRole("Payroll Manager");
        var isFinance = User.IsInRole("Finance Controller") || User.IsInRole("Finance Approver");

        // Admin and Finance finalise directly to Approved
        if (isAdmin || isFinance)
        {
            _db.PayrollApprovals.Add(new PayrollApproval { TenantId = tenantId, PayrollRunId = id, ApprovalLevel = "FinanceReview", Decision = "Approved", Notes = req.Notes ?? string.Empty, DecidedByUserId = GetUserId(), DecidedAtUtc = DateTime.UtcNow });
            run.Status = "Approved";
            await PayrollAudit("payroll.run.approved", "PayrollRun", id.ToString(), new { notes = req.Notes, level = "Finance" }, cancellationToken);
            await _db.SaveChangesAsync(cancellationToken);
            await _notifications.NotifyAsync(tenantId, GetUserId(), $"Payroll Run Approved — {run.Year}/{run.Month:D2}", $"Payroll run for {run.Year}/{run.Month:D2} has been approved by Finance. Total net: {run.TotalNetSalary:N2} AED.", "PayrollRun", id.ToString(), cancellationToken);
            return Ok(run);
        }

        // HR Manager or Payroll Manager advances Processed → PendingFinanceReview
        if (isHROrPayroll && run.Status == "Processed")
        {
            _db.PayrollApprovals.Add(new PayrollApproval { TenantId = tenantId, PayrollRunId = id, ApprovalLevel = "PayrollReview", Decision = "Approved", Notes = req.Notes ?? string.Empty, DecidedByUserId = GetUserId(), DecidedAtUtc = DateTime.UtcNow });
            run.Status = "PendingFinanceReview";
            await PayrollAudit("payroll.run.payroll_approved", "PayrollRun", id.ToString(), new { notes = req.Notes }, cancellationToken);
            await _db.SaveChangesAsync(cancellationToken);
            return Ok(run);
        }

        return BadRequest(new { message = "You cannot approve this run at its current stage." });
    }

    [HttpPost("runs/{id:guid}/send-back")]
    [Authorize(Roles = "Admin,Finance Controller,Finance Approver")]
    public async Task<IActionResult> SendBack(Guid id, PayrollDecisionRequest req, CancellationToken cancellationToken)
    {
        var tenantId = GetTenantId();
        var run = await _db.PayrollRuns.FirstOrDefaultAsync(x => x.TenantId == tenantId && x.Id == id, cancellationToken);
        if (run is null) return NotFound();
        if (run.Status != "PendingFinanceReview")
            return BadRequest(new { message = "Only a PendingFinanceReview run can be sent back." });
        _db.PayrollApprovals.Add(new PayrollApproval { TenantId = tenantId, PayrollRunId = id, ApprovalLevel = "FinanceReview", Decision = "SentBack", Notes = req.Notes ?? string.Empty, DecidedByUserId = GetUserId(), DecidedAtUtc = DateTime.UtcNow });
        run.Status = "Processed";
        await PayrollAudit("payroll.run.sent_back", "PayrollRun", id.ToString(), new { notes = req.Notes }, cancellationToken);
        await _db.SaveChangesAsync(cancellationToken);
        return Ok(run);
    }

    [HttpGet("runs/{id:guid}/gl-journal")]
    [Authorize(Roles = "Admin,HR Manager,Finance Approver,Finance Controller,Payroll Manager")]
    public async Task<IActionResult> GlJournal(Guid id, CancellationToken cancellationToken)
    {
        var tenantId = GetTenantId();
        var run = await _db.PayrollRuns.AsNoTracking().FirstOrDefaultAsync(x => x.TenantId == tenantId && x.Id == id, cancellationToken);
        if (run is null) return NotFound();

        var earnings  = await _db.PayrollEarnings.AsNoTracking().Where(x => x.TenantId == tenantId && x.PayrollRunId == id).ToListAsync(cancellationToken);
        var deductions = await _db.PayrollDeductions.AsNoTracking().Where(x => x.TenantId == tenantId && x.PayrollRunId == id).ToListAsync(cancellationToken);
        var totalNet   = await _db.PayrollSlips.AsNoTracking().Where(x => x.TenantId == tenantId && x.RunId == id).SumAsync(x => x.NetSalary, cancellationToken);

        var glMap = new Dictionary<string, (string Account, string AccountName, string EntryType)>
        {
            ["BASIC"]            = ("5001", "Basic Salary Expense",            "DR"),
            ["HOUSING"]          = ("5002", "Housing Allowance Expense",       "DR"),
            ["TRANSPORT"]        = ("5003", "Transport Allowance Expense",     "DR"),
            ["OTHER_ALLOWANCES"] = ("5004", "Other Allowances Expense",        "DR"),
            ["OVERTIME"]         = ("5005", "Overtime Expense",                "DR"),
            ["GOSI_EMPLOYEE"]    = ("2101", "GOSI / Social Insurance Payable", "CR"),
            ["INCOME_TAX"]       = ("2102", "Income Tax Payable",              "CR"),
            ["FIXED_DEDUCTION"]  = ("2103", "Fixed Deductions Payable",        "CR"),
            ["ATTENDANCE"]       = ("2104", "Attendance Adjustment Payable",   "CR"),
            ["ABSENCE"]          = ("2104", "Attendance Adjustment Payable",   "CR"),
            ["LEAVE"]            = ("2105", "Leave Deduction Payable",         "CR"),
        };

        var entries = new List<(string Code, string Name, string Account, string AccountName, string EntryType, decimal Amount)>();

        foreach (var grp in earnings.GroupBy(e => e.ComponentCode))
        {
            if (!glMap.TryGetValue(grp.Key, out var acct))
                acct = ("5099", $"Other Earnings — {grp.Key}", "DR");
            entries.Add((grp.Key, grp.First().ComponentName, acct.Account, acct.AccountName, acct.EntryType, grp.Sum(e => e.Amount)));
        }
        foreach (var grp in deductions.GroupBy(d => d.ComponentCode))
        {
            if (!glMap.TryGetValue(grp.Key, out var acct))
                acct = ("2199", $"Other Deductions — {grp.Key}", "CR");
            entries.Add((grp.Key, grp.First().ComponentName, acct.Account, acct.AccountName, acct.EntryType, grp.Sum(d => d.Amount)));
        }

        // Employer GOSI contra-entry (if configured)
        var gosiEmployerRateSetting = await _db.SystemSettings.AsNoTracking()
            .Where(x => x.Category == "Payroll" && x.SettingKey == "GosiEmployerRate")
            .Select(x => x.SettingValue).FirstOrDefaultAsync(cancellationToken);
        decimal.TryParse(gosiEmployerRateSetting, out var gosiEmployerRate);
        if (gosiEmployerRate > 0)
        {
            var totalBasic    = earnings.Where(e => e.ComponentCode == "BASIC").Sum(e => e.Amount);
            var employerGosi  = Math.Round(totalBasic * gosiEmployerRate / 100m, 2);
            entries.Add(("GOSI_EMPLOYER",    "Employer GOSI Expense",              "5101", "Employer GOSI Expense",    "DR", employerGosi));
            entries.Add(("GOSI_EMPLOYER_CR", "Employer GOSI Contribution Payable", "2106", "Employer GOSI Payable",    "CR", employerGosi));
        }

        // Net salary payable CR balances all earning DRs net of deduction CRs
        entries.Add(("NET_SALARY", "Net Salary Payable", "2100", "Salaries Payable", "CR", totalNet));

        var totalDebits  = entries.Where(e => e.EntryType == "DR").Sum(e => e.Amount);
        var totalCredits = entries.Where(e => e.EntryType == "CR").Sum(e => e.Amount);

        return Ok(new
        {
            runId    = id,
            period   = $"{run.Year}-{run.Month:D2}",
            entries  = entries.Select(e => new { componentCode = e.Code, componentName = e.Name, glAccount = e.Account, glAccountName = e.AccountName, entryType = e.EntryType, amount = e.Amount }),
            totalDebits, totalCredits,
            isBalanced = Math.Abs(totalDebits - totalCredits) < 0.01m
        });
    }

    [HttpPost("runs/{id:guid}/payslips/generate")]
    public async Task<IActionResult> GeneratePayslips(Guid id, CancellationToken cancellationToken)
    {
        var tenantId = GetTenantId();
        var slips = await _db.PayrollSlips.AsNoTracking().Where(x => x.TenantId == tenantId && x.RunId == id).ToListAsync(cancellationToken);
        // M2: load itemized earnings and deductions for proper payslip line items
        var earnings = await _db.PayrollEarnings.AsNoTracking().Where(x => x.TenantId == tenantId && x.PayrollRunId == id).ToListAsync(cancellationToken);
        var deductions = await _db.PayrollDeductions.AsNoTracking().Where(x => x.TenantId == tenantId && x.PayrollRunId == id).ToListAsync(cancellationToken);
        foreach (var slip in slips)
        {
            if (await _db.Payslips.AnyAsync(x => x.TenantId == tenantId && x.PayrollRunId == id && x.EmployeeId == slip.EmployeeId, cancellationToken)) continue;
            var payslip = new Payslip { TenantId = tenantId, PayrollRunId = id, EmployeeId = slip.EmployeeId, PayslipNumber = $"PS-{slip.EmployeeCode}-{DateTime.UtcNow:yyyyMMddHHmmss}" };
            _db.Payslips.Add(payslip);
            foreach (var e in earnings.Where(x => x.EmployeeId == slip.EmployeeId))
                _db.PayslipComponents.Add(new PayslipComponent { TenantId = tenantId, PayslipId = payslip.Id, ComponentType = "Earning", ComponentName = e.ComponentName, Amount = e.Amount });
            foreach (var d in deductions.Where(x => x.EmployeeId == slip.EmployeeId))
                _db.PayslipComponents.Add(new PayslipComponent { TenantId = tenantId, PayslipId = payslip.Id, ComponentType = "Deduction", ComponentName = d.ComponentName, Amount = d.Amount });
            _db.PayslipComponents.Add(new PayslipComponent { TenantId = tenantId, PayslipId = payslip.Id, ComponentType = "Net", ComponentName = "Net pay", Amount = slip.NetSalary });
        }
        await PayrollAudit("payroll.payslips.generated", "PayrollRun", id.ToString(), null, cancellationToken);
        await _db.SaveChangesAsync(cancellationToken);
        return Ok(await _db.Payslips.AsNoTracking().Where(x => x.TenantId == tenantId && x.PayrollRunId == id).ToListAsync(cancellationToken));
    }

    [HttpPost("runs/{id:guid}/payment-batches")]
    public async Task<IActionResult> CreatePaymentBatch(Guid id, PayrollPaymentBatchRequest req, CancellationToken cancellationToken)
    {
        var tenantId = GetTenantId();
        var run = await _db.PayrollRuns.FirstOrDefaultAsync(x => x.TenantId == tenantId && x.Id == id, cancellationToken);
        if (run is null) return NotFound();
        // C5: payment batch requires a locked run — approval workflow must complete first
        if (run.Status != "Locked")
            return BadRequest(new { message = "Payment batches can only be created for Locked runs. Approve and lock the run before creating a payment batch." });
        // L2: prevent duplicate payment batches on the same run
        if (await _db.PayrollPaymentBatches.AnyAsync(x => x.TenantId == tenantId && x.PayrollRunId == id, cancellationToken))
            return Conflict(new { message = "A payment batch already exists for this payroll run." });
        var slips = await _db.PayrollSlips.AsNoTracking().Where(x => x.TenantId == tenantId && x.RunId == id).ToListAsync(cancellationToken);
        var profiles = await _db.EmployeePayrollProfiles.AsNoTracking().Where(x => x.TenantId == tenantId && !x.IsDeleted).ToListAsync(cancellationToken);
        var currency = req.Currency ?? await ResolveCurrencyAsync(tenantId, cancellationToken);
        var batch = new PayrollPaymentBatch { TenantId = tenantId, PayrollRunId = id, BatchNumber = $"PAY-{run.Year}{run.Month:00}-{DateTime.UtcNow:HHmmss}", PaymentMethod = req.PaymentMethod ?? "WPS", TotalAmount = slips.Sum(x => x.NetSalary), Currency = currency, WpsStatus = WpsStatuses.Draft };
        _db.PayrollPaymentBatches.Add(batch);
        foreach (var slip in slips)
        {
            var profile = profiles.FirstOrDefault(x => x.EmployeeId == slip.EmployeeId);
            if (string.IsNullOrWhiteSpace(profile?.Iban))
                _db.PayrollValidationResults.Add(new PayrollValidationResult { TenantId = tenantId, PayrollRunId = id, EmployeeId = slip.EmployeeId, Severity = "Warning", Code = "MISSING_IBAN", Message = "Employee is missing IBAN for payment file." });
            _db.PayrollPaymentRecords.Add(new PayrollPaymentRecord { TenantId = tenantId, PaymentBatchId = batch.Id, EmployeeId = slip.EmployeeId, Amount = slip.NetSalary, Iban = profile?.Iban ?? string.Empty, Status = "Pending", WpsReference = $"WPS-{slip.EmployeeCode}-{run.Year}{run.Month:00}" });
        }
        await PayrollAudit("payroll.payment_batch.created", "PayrollPaymentBatch", batch.Id.ToString(), new { totalAmount = batch.TotalAmount, method = batch.PaymentMethod }, cancellationToken);
        await _db.SaveChangesAsync(cancellationToken);
        return Created($"/api/payroll/payment-batches/{batch.Id}", batch);
    }

    /// <summary>
    /// Pre-export WPS validation: returns the list of blocking issues (missing/invalid
    /// IBANs, unapproved run) that must be cleared before a SIF file can be exported.
    /// The same checks are enforced inside <see cref="GenerateWps"/> — the frontend is never trusted.
    /// </summary>
    [HttpPost("runs/{id:guid}/wps-validation")]
    public async Task<IActionResult> WpsValidation(Guid id, CancellationToken cancellationToken)
    {
        var tenantId = GetTenantId();
        var run = await _db.PayrollRuns.AsNoTracking().FirstOrDefaultAsync(x => x.TenantId == tenantId && x.Id == id, cancellationToken);
        if (run is null) return NotFound();

        var issues = new List<object>();
        if (run.Status != "Locked" && run.Status != "Paid")
            issues.Add(new { code = "RUN_NOT_LOCKED", message = "Payroll run must be approved and locked before WPS export." });

        var slips = await _db.PayrollSlips.AsNoTracking().Where(x => x.TenantId == tenantId && x.RunId == id).ToListAsync(cancellationToken);
        var profiles = await _db.EmployeePayrollProfiles.AsNoTracking().Where(x => x.TenantId == tenantId && !x.IsDeleted).ToListAsync(cancellationToken);

        foreach (var slip in slips)
        {
            var iban = profiles.FirstOrDefault(p => p.EmployeeId == slip.EmployeeId)?.Iban;
            if (!Infrastructure.Payroll.IbanValidator.IsValid(iban))
                issues.Add(new { code = "INVALID_IBAN", employeeId = slip.EmployeeId, employeeCode = slip.EmployeeCode, message = "Missing or invalid IBAN for WPS payment." });
        }

        return Ok(new { runId = id, canExport = issues.Count == 0, issueCount = issues.Count, issues });
    }

    [HttpPost("payment-batches/{id:guid}/wps-file")]
    public async Task<IActionResult> GenerateWps(Guid id, CancellationToken cancellationToken)
    {
        var tenantId = GetTenantId();
        var batch = await _db.PayrollPaymentBatches.FirstOrDefaultAsync(x => x.TenantId == tenantId && x.Id == id, cancellationToken);
        if (batch is null) return NotFound();

        var run = await _db.PayrollRuns.AsNoTracking().FirstOrDefaultAsync(x => x.TenantId == tenantId && x.Id == batch.PayrollRunId, cancellationToken);
        if (run is not null && run.Status != "Locked" && run.Status != "Paid")
            return BadRequest(new { error = "run_not_locked", message = "Payroll run must be approved and locked before WPS export." });

        var records = await _db.PayrollPaymentRecords.AsNoTracking().Where(x => x.TenantId == tenantId && x.PaymentBatchId == id).ToListAsync(cancellationToken);

        // Backend-enforced IBAN validation — never trust the frontend pre-check.
        var employeeCodes = await _db.Employees.AsNoTracking()
            .Where(e => e.TenantId == tenantId && records.Select(r => r.EmployeeId).Contains(e.Id))
            .Select(e => new { e.Id, e.EmployeeCode })
            .ToListAsync(cancellationToken);

        var invalid = records
            .Where(r => !Infrastructure.Payroll.IbanValidator.IsValid(r.Iban))
            .Select(r => new
            {
                employeeId = r.EmployeeId,
                employeeCode = employeeCodes.FirstOrDefault(e => e.Id == r.EmployeeId)?.EmployeeCode ?? r.EmployeeId.ToString()
            })
            .ToList();

        if (invalid.Count > 0)
            return BadRequest(new { error = "invalid_ibans", message = "WPS export blocked: some payment records have missing or invalid IBANs.", employees = invalid });

        // Persist WPS records
        var wps = new WPSFileBatch { TenantId = tenantId, PaymentBatchId = id, SifFileName = $"SIF-{batch.BatchNumber}.txt" };
        _db.WPSFileBatches.Add(wps);
        foreach (var record in records)
        {
            var code = employeeCodes.FirstOrDefault(e => e.Id == record.EmployeeId)?.EmployeeCode ?? record.EmployeeId.ToString();
            _db.SIFFileRecords.Add(new SIFFileRecord { TenantId = tenantId, WPSFileBatchId = wps.Id, EmployeeId = record.EmployeeId, EmployeeCode = code, Iban = record.Iban, NetPay = record.Amount });
        }
        batch.Status = "FileGenerated";
        batch.WpsStatus = WpsStatuses.Generated;
        await PayrollAudit("payroll.wps.generated", "WPSFileBatch", wps.Id.ToString(), null, cancellationToken);
        await _db.SaveChangesAsync(cancellationToken);
        return Ok(wps);
    }

    /// <summary>Updates the WPS submission status of a payment batch (Submitted/Accepted/Rejected/Reconciled).</summary>
    [HttpPost("payment-batches/{batchId:guid}/wps-status")]
    public async Task<IActionResult> UpdateWpsStatus(Guid batchId, [FromBody] WpsStatusRequest req, CancellationToken cancellationToken)
    {
        if (!WpsStatuses.All.Contains(req.Status))
            return BadRequest(new { error = "invalid_status", message = $"Status must be one of: {string.Join(", ", WpsStatuses.All)}." });

        var tenantId = GetTenantId();
        var batch = await _db.PayrollPaymentBatches.FirstOrDefaultAsync(x => x.TenantId == tenantId && x.Id == batchId, cancellationToken);
        if (batch is null) return NotFound();

        var old = batch.WpsStatus;
        batch.WpsStatus = req.Status;
        await PayrollAudit("payroll.wps.status_changed", "PayrollPaymentBatch", batchId.ToString(), new { from = old, to = req.Status, notes = req.Notes }, cancellationToken);
        await _db.SaveChangesAsync(cancellationToken);
        return Ok(new { batchId, wpsStatus = batch.WpsStatus });
    }

    /// <summary>Download the actual SIF text file for a WPS batch — per UAE CBUAE SIF v2 format.</summary>
    [HttpGet("payment-batches/{batchId:guid}/wps-file/download")]
    public async Task<IActionResult> DownloadWpsFile(Guid batchId, CancellationToken cancellationToken)
    {
        var tenantId = GetTenantId();
        var batch = await _db.PayrollPaymentBatches.AsNoTracking().FirstOrDefaultAsync(x => x.TenantId == tenantId && x.Id == batchId, cancellationToken);
        if (batch is null) return NotFound();
        var wpsFile = await _db.WPSFileBatches.AsNoTracking().FirstOrDefaultAsync(x => x.TenantId == tenantId && x.PaymentBatchId == batchId, cancellationToken);
        if (wpsFile is null) return BadRequest(new { message = "WPS file has not been generated for this batch yet." });

        var sifRecords = await _db.SIFFileRecords.AsNoTracking().Where(x => x.TenantId == tenantId && x.WPSFileBatchId == wpsFile.Id).ToListAsync(cancellationToken);
        var gcc = await _db.GCCComplianceSettings.AsNoTracking().FirstOrDefaultAsync(x => x.TenantId == tenantId, cancellationToken);
        var agentId = (gcc?.WpsAgentId ?? "0000000000").PadRight(10).Substring(0, 10);
        var molCode = (gcc?.WpsMolCode ?? "0000000").PadRight(7).Substring(0, 7);
        var run = await _db.PayrollRuns.AsNoTracking().FirstOrDefaultAsync(x => x.Id == batch.PayrollRunId, cancellationToken);
        var paymentDate = run is not null
            ? new DateTime(run.Year, run.Month, DateTime.DaysInMonth(run.Year, run.Month))
            : DateTime.UtcNow;

        // Currency: batch currency, falling back to the tenant company default (SAR for Saudi).
        var currency = !string.IsNullOrWhiteSpace(batch.Currency) && batch.Currency != "USD"
            ? batch.Currency
            : await ResolveCurrencyAsync(tenantId, cancellationToken);

        // CBUAE SIF v2 format (adapted for Saudi context — currency is tenant-driven, defaulting to SAR)
        // Header record: EDI_DC40 segment
        var sb = new StringBuilder();
        sb.AppendLine($"EDI_DC40+{agentId}+{molCode}+{paymentDate:yyyyMMdd}+{sifRecords.Count:D6}+{batch.TotalAmount:F2}+{currency}'");
        foreach (var rec in sifRecords)
        {
            var iban = rec.Iban.Replace(" ", string.Empty).PadRight(34).Substring(0, 34);
            // E1EDL20: employee salary record
            sb.AppendLine($"E1EDL20+{rec.EmployeeCode.PadRight(10).Substring(0, 10)}+{iban}+{rec.NetPay:F2}+{currency}+{paymentDate:yyyyMMdd}+01'");
        }
        // Trailer
        sb.AppendLine($"EOF+{sifRecords.Count:D6}+{sifRecords.Sum(r => r.NetPay):F2}'");

        // Mark the batch as downloaded (lifecycle tracking).
        var tracked = await _db.PayrollPaymentBatches.FirstOrDefaultAsync(x => x.TenantId == tenantId && x.Id == batchId, cancellationToken);
        if (tracked is not null && tracked.WpsStatus is "Draft" or "Generated")
        {
            tracked.WpsStatus = WpsStatuses.Downloaded;
            await _db.SaveChangesAsync(cancellationToken);
        }

        var bytes = Encoding.UTF8.GetBytes(sb.ToString());
        Response.Headers["Content-Disposition"] = $"attachment; filename={wpsFile.SifFileName}";
        return File(bytes, "text/plain", wpsFile.SifFileName);
    }

    [HttpGet("employee-salary-structures")]
    public async Task<IActionResult> ListEmployeeSalaryStructures([FromQuery] int? employeeId, CancellationToken cancellationToken)
    {
        var tenantId = GetTenantId();
        // M4: scope to allowed employees
        var scope = await _scopeService.ResolveAsync(User, tenantId, cancellationToken);
        var query = _db.EmployeeSalaryStructures.AsNoTracking().Where(x => x.TenantId == tenantId);
        if (employeeId.HasValue) query = query.Where(x => x.EmployeeId == employeeId.Value);
        if (!scope.IsUnrestricted)
            query = query.Where(x => scope.AllowedEmployeeIds!.Contains(x.EmployeeId));
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
        var scope = await _scopeService.ResolveAsync(User, tenantId, cancellationToken);
        var query = _db.Payslips.Where(x => x.TenantId == tenantId && x.PayrollRunId == id);
        if (!scope.IsUnrestricted)
            query = query.Where(x => scope.AllowedEmployeeIds!.Contains(x.EmployeeId));
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
        var group = new PayrollGroup { TenantId = tenantId, Code = req.Code.Trim(), Name = req.Name.Trim(), Currency = req.Currency ?? "USD" };
        _db.PayrollGroups.Add(group);
        await _db.SaveChangesAsync(cancellationToken);
        return Created($"/api/payroll/groups/{group.Id}", group);
    }

    // H3: salary register is scoped — managers cannot see all employee salaries
    [HttpGet("reports/register")]
    public async Task<IActionResult> Register([FromQuery] Guid runId, CancellationToken cancellationToken)
    {
        var tenantId = GetTenantId();
        var scope = await _scopeService.ResolveAsync(User, tenantId, cancellationToken);
        var query = _db.PayrollSlips.AsNoTracking().Where(x => x.TenantId == tenantId && x.RunId == runId);
        if (!scope.IsUnrestricted)
            query = query.Where(x => scope.AllowedEmployeeIds!.Contains(x.EmployeeId));
        return Ok(await query.OrderBy(x => x.EmployeeCode).ToListAsync(cancellationToken));
    }

    [HttpGet("reports/register/export")]
    public async Task<IActionResult> ExportRegister([FromQuery] Guid runId, CancellationToken cancellationToken)
    {
        var tenantId = GetTenantId();
        var scope = await _scopeService.ResolveAsync(User, tenantId, cancellationToken);
        var run = await _db.PayrollRuns.AsNoTracking().FirstOrDefaultAsync(x => x.Id == runId && x.TenantId == tenantId, cancellationToken);
        if (run is null) return NotFound();
        var query = _db.PayrollSlips.AsNoTracking().Where(x => x.TenantId == tenantId && x.RunId == runId);
        if (!scope.IsUnrestricted)
            query = query.Where(x => scope.AllowedEmployeeIds!.Contains(x.EmployeeId));
        var slips = await query.OrderBy(x => x.EmployeeCode).ToListAsync(cancellationToken);

        var headers = new[] { "Employee Code", "Employee Name", "Department", "Basic Salary", "Housing Allowance", "Transport Allowance", "Other Allowances", "Gross Salary", "Deductions", "Net Salary", "Status" };
        var rows = slips.Select(s => (IReadOnlyList<object?>)new object?[]
        {
            s.EmployeeCode, s.EmployeeName, s.Department,
            s.BasicSalary.ToString("F2"), s.HousingAllowance.ToString("F2"), s.TransportAllowance.ToString("F2"), s.OtherAllowances.ToString("F2"),
            s.GrossSalary.ToString("F2"), s.Deductions.ToString("F2"), s.NetSalary.ToString("F2"), s.Status
        });
        var csv = Csv.Build(headers, rows);
        Response.Headers["Content-Disposition"] = $"attachment; filename=salary-register-{run.Year}-{run.Month:D2}.csv";
        return Content(csv, "text/csv");
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

    // ── EOSB / Gratuity ──────────────────────────────────────────────────────────

    /// <summary>Calculate EOSB/Gratuity for a single employee using tenant GCC settings.</summary>
    [HttpPost("eosb/calculate")]
    public async Task<IActionResult> CalculateEosb([FromBody] EosbCalculationRequest req, CancellationToken cancellationToken)
    {
        var tenantId = GetTenantId();
        var employee = await _db.Employees.AsNoTracking().FirstOrDefaultAsync(x => x.TenantId == tenantId && x.Id == req.EmployeeId && !x.IsDeleted, cancellationToken);
        if (employee is null) return NotFound(new { message = "Employee not found." });

        var gcc = await _db.GCCComplianceSettings.AsNoTracking().FirstOrDefaultAsync(x => x.TenantId == tenantId, cancellationToken);
        if (gcc is null || !gcc.EosbEnabled)
            return BadRequest(new { message = "EOSB is not enabled for this tenant. Enable it in GCC Settings first." });

        var salary = await _db.EmployeeSalaryStructures.AsNoTracking()
            .Where(x => x.TenantId == tenantId && x.EmployeeId == req.EmployeeId && x.IsActive)
            .OrderByDescending(x => x.EffectiveDate)
            .FirstOrDefaultAsync(cancellationToken);

        var eligibleSalary = salary?.BasicSalary ?? employee.Salary ?? 0m;
        var joiningDate = employee.JoiningDate;
        var calcDate = req.AsOfDate ?? DateTime.UtcNow;
        var totalYears = (calcDate - joiningDate).Days / 365.0;
        var minYears = gcc.EosbMinYears > 0 ? gcc.EosbMinYears : 1;

        if (totalYears < minYears)
            return Ok(new { employeeId = req.EmployeeId, employeeName = employee.FullName, eligibleSalary, totalYears, eosbAmount = 0m, message = $"Employee has {totalYears:F1} years of service. Minimum required: {minYears} year(s)." });

        // UAE Gratuity: 21 days per year for first 5 years, 30 days per year thereafter
        var rate1 = gcc.EosbYears1To5Rate > 0 ? gcc.EosbYears1To5Rate : 21m;  // days per year
        var rate2 = gcc.EosbYearsAbove5Rate > 0 ? gcc.EosbYearsAbove5Rate : 30m; // days per year
        var dailySalary = eligibleSalary * 12 / 365m;

        decimal eosbAmount;
        if (totalYears <= 5)
            eosbAmount = dailySalary * rate1 * (decimal)totalYears;
        else
            eosbAmount = dailySalary * rate1 * 5 + dailySalary * rate2 * (decimal)(totalYears - 5);

        eosbAmount = Math.Round(eosbAmount, 2);

        // Persist the calculation
        var existing = await _db.EOSBCalculations.FirstOrDefaultAsync(x => x.TenantId == tenantId && x.EmployeeId == req.EmployeeId && x.Status == "Draft", cancellationToken);
        if (existing is not null)
        {
            existing.CalculationDate = DateOnly.FromDateTime(calcDate);
            existing.EligibleSalary = eligibleSalary;
            existing.CalculatedAmount = eosbAmount;
            existing.RulesSnapshotJson = System.Text.Json.JsonSerializer.Serialize(new { rate1, rate2, totalYears, dailySalary });
        }
        else
        {
            _db.EOSBCalculations.Add(new EOSBCalculation
            {
                TenantId = tenantId, EmployeeId = req.EmployeeId, CalculationDate = DateOnly.FromDateTime(calcDate),
                EligibleSalary = eligibleSalary, CalculatedAmount = eosbAmount,
                RulesSnapshotJson = System.Text.Json.JsonSerializer.Serialize(new { rate1, rate2, totalYears, dailySalary })
            });
        }
        await _db.SaveChangesAsync(cancellationToken);

        return Ok(new
        {
            employeeId = req.EmployeeId,
            employeeName = employee.FullName,
            joiningDate,
            asOfDate = calcDate,
            totalYears = Math.Round(totalYears, 2),
            eligibleSalary,
            dailySalary = Math.Round(dailySalary, 4),
            rate1To5Years = rate1,
            rateAbove5Years = rate2,
            eosbAmount,
            currency = salary?.Currency ?? "USD",
            message = $"Calculated EOSB/Gratuity for {employee.FullName}: {salary?.Currency ?? "USD"} {eosbAmount:N2}"
        });
    }

    [HttpGet("eosb/list")]
    public async Task<IActionResult> ListEosb([FromQuery] int? employeeId, CancellationToken cancellationToken)
    {
        var tenantId = GetTenantId();
        var query = _db.EOSBCalculations.AsNoTracking().Where(x => x.TenantId == tenantId);
        if (employeeId.HasValue) query = query.Where(x => x.EmployeeId == employeeId.Value);
        return Ok(await query.OrderByDescending(x => x.CalculationDate).ToListAsync(cancellationToken));
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

    // M1: audit log now captures caller IP and structured metadata
    private async Task PayrollAudit(string action, string entity, string entityId, object? metadata, CancellationToken ct)
    {
        var ip = _http.HttpContext?.Connection.RemoteIpAddress?.ToString() ?? "unknown";
        var meta = new { ip, userId = GetUserId()?.ToString(), data = metadata };
        _db.PayrollAuditLogs.Add(new PayrollAuditLog
        {
            TenantId = GetTenantId(),
            Action = action,
            EntityName = entity,
            EntityId = entityId,
            UserId = GetUserId(),
            MetadataJson = JsonSerializer.Serialize(meta),
        });
        await Task.CompletedTask;
    }

    // ── Salary Structure Export / Import / Template ───────────────────────────
    private static readonly string[] SalaryStructureCsvHeaders =
        { "Code", "Name", "Currency", "EffectiveDate" };

    [HttpGet("structures/export")]
    public async Task<IActionResult> ExportStructures(CancellationToken cancellationToken)
    {
        var tenantId = GetTenantId();
        var structures = await _db.SalaryStructures
            .AsNoTracking()
            .Where(x => x.TenantId == tenantId && !x.IsDeleted)
            .OrderBy(x => x.Name)
            .ToListAsync(cancellationToken);
        var rows = structures.Select(s => (IReadOnlyList<object?>)new object?[]
        {
            s.Code, s.Name, s.Currency, s.EffectiveDate.ToString("yyyy-MM-dd")
        });
        var csv = Csv.Build(SalaryStructureCsvHeaders, rows);
        Response.Headers["Content-Disposition"] = "attachment; filename=salary_structures_export.csv";
        return Content(csv, "text/csv");
    }

    [HttpGet("structures/import-template")]
    public IActionResult StructuresImportTemplate()
    {
        Response.Headers["Content-Disposition"] = "attachment; filename=salary_structures_import_template.csv";
        return Content(Csv.Template(SalaryStructureCsvHeaders), "text/csv");
    }

    [HttpPost("structures/import")]
    public async Task<IActionResult> ImportStructures([FromBody] ImportSalaryStructuresRequest req, CancellationToken cancellationToken)
    {
        var tenantId = GetTenantId();
        var rows = Csv.Parse(req.CsvContent ?? string.Empty);
        int created = 0, skipped = 0;
        var errors = new List<string>();
        var rowNum = 1;
        foreach (var row in rows)
        {
            rowNum++;
            var code = row.GetValueOrDefault("Code", string.Empty).Trim();
            var name = row.GetValueOrDefault("Name", string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(code) || string.IsNullOrWhiteSpace(name)) { skipped++; continue; }
            if (await _db.SalaryStructures.AnyAsync(x => x.TenantId == tenantId && x.Code == code && !x.IsDeleted, cancellationToken))
            { skipped++; errors.Add($"Row {rowNum}: Code '{code}' already exists."); continue; }
            DateOnly.TryParse(row.GetValueOrDefault("EffectiveDate", string.Empty), out var effectiveDate);
            if (effectiveDate == default) effectiveDate = DateOnly.FromDateTime(DateTime.UtcNow);
            _db.SalaryStructures.Add(new SalaryStructure
            {
                TenantId = tenantId,
                Code = code,
                Name = name,
                Currency = row.GetValueOrDefault("Currency", "USD"),
                EffectiveDate = effectiveDate,
                CreatedBy = GetUserId()
            });
            created++;
        }
        await _db.SaveChangesAsync(cancellationToken);
        return Ok(new { received = rows.Count, created, skipped, errors = errors.Take(20) });
    }

    /// <summary>
    /// Per-employee reconciliation between contract salary and the processed payroll
    /// slip, plus WPS/GOSI/QIWA readiness flags.  Variance &gt; 5% is flagged as a warning.
    /// </summary>
    [HttpGet("runs/{id:guid}/mismatch-report")]
    public async Task<IActionResult> MismatchReport(Guid id, CancellationToken cancellationToken)
    {
        if (!HasPermission("payroll.review")) return Forbid();

        var tenantId = GetTenantId();
        var run = await _db.PayrollRuns.AsNoTracking().FirstOrDefaultAsync(x => x.TenantId == tenantId && x.Id == id, cancellationToken);
        if (run is null) return NotFound();

        var slips = await _db.PayrollSlips.AsNoTracking().Where(x => x.TenantId == tenantId && x.RunId == id).ToListAsync(cancellationToken);
        var salaries = await _db.EmployeeSalaryStructures.AsNoTracking().Where(x => x.TenantId == tenantId).ToListAsync(cancellationToken);
        var profiles = await _db.EmployeePayrollProfiles.AsNoTracking().Where(x => x.TenantId == tenantId && !x.IsDeleted).ToListAsync(cancellationToken);
        var empIds = slips.Select(s => s.EmployeeId).ToList();
        var employees = await _db.Employees.AsNoTracking()
            .Where(e => e.TenantId == tenantId && empIds.Contains(e.Id))
            .ToListAsync(cancellationToken);

        var rows = new List<object>();
        foreach (var slip in slips)
        {
            var contractBasic = salaries
                .Where(s => s.EmployeeId == slip.EmployeeId)
                .OrderByDescending(s => s.EffectiveDate)
                .Select(s => s.BasicSalary)
                .FirstOrDefault();
            var variance = slip.BasicSalary - contractBasic;
            var variancePercent = contractBasic == 0 ? 0 : Math.Round((double)(variance / contractBasic) * 100, 2);

            var iban = profiles.FirstOrDefault(p => p.EmployeeId == slip.EmployeeId)?.Iban;
            var hasValidIban = Infrastructure.Payroll.IbanValidator.IsValid(iban);
            var emp = employees.FirstOrDefault(e => e.Id == slip.EmployeeId);
            var missingGosiRef = emp is null || string.IsNullOrWhiteSpace(emp.GosiReference);
            var missingQiwa = emp is null ? new List<string>() : Infrastructure.Qiwa.QiwaIntegrationService.MissingQiwaFields(emp);

            var issues = new List<string>();
            if (Math.Abs(variancePercent) > 5) issues.Add($"Payroll basic differs from contract salary by {variancePercent}%.");
            if (!hasValidIban) issues.Add("Missing or invalid IBAN.");
            if (missingGosiRef) issues.Add("Missing GOSI reference.");
            if (missingQiwa.Count > 0) issues.Add($"Missing QIWA fields: {string.Join(", ", missingQiwa)}.");

            rows.Add(new
            {
                employeeId = slip.EmployeeId,
                employeeCode = slip.EmployeeCode,
                employeeName = slip.EmployeeName,
                contractSalary = contractBasic,
                payrollBasic = slip.BasicSalary,
                variance,
                variancePercent,
                hasValidIban,
                missingGosiRef,
                missingQiwaFields = missingQiwa,
                isWarning = Math.Abs(variancePercent) > 5,
                issues
            });
        }

        return Ok(new { runId = id, period = $"{run.Year}-{run.Month:D2}", employeeCount = rows.Count, employees = rows });
    }

    /// <summary>Month-over-month headcount and compensation reconciliation vs the prior payroll run.</summary>
    [HttpGet("reports/reconciliation")]
    public async Task<IActionResult> Reconciliation([FromQuery] Guid runId, CancellationToken cancellationToken)
    {
        if (!HasPermission("payroll.review")) return Forbid();
        var tenantId = GetTenantId();
        var run = await _db.PayrollRuns.AsNoTracking().FirstOrDefaultAsync(x => x.TenantId == tenantId && x.Id == runId, cancellationToken);
        if (run is null) return NotFound();

        var (priorYear, priorMonth) = run.Month == 1 ? (run.Year - 1, 12) : (run.Year, run.Month - 1);
        var priorRun = await _db.PayrollRuns.AsNoTracking().FirstOrDefaultAsync(x => x.TenantId == tenantId && x.Year == priorYear && x.Month == priorMonth, cancellationToken);

        var currentSlips = await _db.PayrollSlips.AsNoTracking().Where(x => x.TenantId == tenantId && x.RunId == runId).ToListAsync(cancellationToken);
        var priorSlips   = priorRun is not null ? await _db.PayrollSlips.AsNoTracking().Where(x => x.TenantId == tenantId && x.RunId == priorRun.Id).ToListAsync(cancellationToken) : new List<PayrollSlip>();

        var currentIds = currentSlips.Select(s => s.EmployeeId).ToHashSet();
        var priorIds   = priorSlips.Select(s => s.EmployeeId).ToHashSet();

        var joiners    = currentIds.Except(priorIds).ToList();
        var leavers    = priorIds.Except(currentIds).ToList();
        var continuing = currentIds.Intersect(priorIds).ToList();

        var variances = continuing.Select(empId =>
        {
            var cur  = currentSlips.First(s => s.EmployeeId == empId);
            var prev = priorSlips.First(s => s.EmployeeId == empId);
            var grossDelta       = cur.GrossSalary - prev.GrossSalary;
            var grossVariancePct = prev.GrossSalary == 0 ? 0.0 : Math.Round((double)(grossDelta / prev.GrossSalary) * 100, 2);
            return new
            {
                employeeId = empId, employeeName = cur.EmployeeName, employeeCode = cur.EmployeeCode,
                priorGross = prev.GrossSalary, currentGross = cur.GrossSalary, grossDelta, grossVariancePct,
                priorNet = prev.NetSalary, currentNet = cur.NetSalary, netDelta = cur.NetSalary - prev.NetSalary,
                isVarianceFlag = Math.Abs(grossVariancePct) > 5
            };
        }).ToList();

        return Ok(new
        {
            runId, period = $"{run.Year}-{run.Month:D2}",
            priorPeriod   = priorRun is not null ? $"{priorRun.Year}-{priorRun.Month:D2}" : null,
            currentHeadcount = currentIds.Count, priorHeadcount = priorIds.Count,
            joinerCount = joiners.Count, leaverCount = leavers.Count,
            currentTotalGross = currentSlips.Sum(s => s.GrossSalary), priorTotalGross = priorSlips.Sum(s => s.GrossSalary),
            currentTotalNet   = currentSlips.Sum(s => s.NetSalary),   priorTotalNet   = priorSlips.Sum(s => s.NetSalary),
            flaggedVariances  = variances.Count(v => v.isVarianceFlag),
            variances
        });
    }

    /// <summary>Final settlement calculator: pro-rata salary + EOSB + leave encashment - notice deduction.</summary>
    [HttpPost("final-settlement")]
    [Authorize(Roles = "Admin,HR Manager,Payroll Manager")]
    public async Task<IActionResult> FinalSettlement([FromBody] FinalSettlementRequest req, CancellationToken cancellationToken)
    {
        var tenantId = GetTenantId();
        var employee = await _db.Employees.FirstOrDefaultAsync(e => e.TenantId == tenantId && e.Id == req.EmployeeId && !e.IsDeleted, cancellationToken);
        if (employee is null) return NotFound(new { message = "Employee not found." });

        var salary = await _db.EmployeeSalaryStructures.AsNoTracking()
            .Where(x => x.TenantId == tenantId && x.EmployeeId == req.EmployeeId && x.IsActive)
            .OrderByDescending(x => x.EffectiveDate)
            .FirstOrDefaultAsync(cancellationToken);
        var basicSalary    = salary?.BasicSalary ?? employee.Salary ?? 0m;
        var grossSalary    = basicSalary + (salary?.HousingAllowance ?? 0m) + (salary?.TransportAllowance ?? 0m)
                           + (salary?.FoodAllowance ?? 0m) + (salary?.MobileAllowance ?? 0m) + (salary?.OtherAllowance ?? 0m);
        var currency       = salary?.Currency ?? "USD";

        // Pro-rata salary for partial month
        var lastDay        = req.LastWorkingDay;
        var daysInMonth    = DateTime.DaysInMonth(lastDay.Year, lastDay.Month);
        var dailyGross     = grossSalary / daysInMonth;
        var proRataSalary  = Math.Round(dailyGross * lastDay.Day, 2);

        // EOSB / Gratuity (reuse GCC compliance settings)
        var gcc = await _db.GCCComplianceSettings.AsNoTracking().Where(x => x.TenantId == tenantId).FirstOrDefaultAsync(cancellationToken);
        var calcDate    = lastDay.ToDateTime(TimeOnly.MinValue);
        var totalYears  = (calcDate - employee.JoiningDate).Days / 365.0;
        var minYears    = gcc?.EosbMinYears > 0 ? gcc!.EosbMinYears : 1;
        var rate1       = gcc?.EosbYears1To5Rate > 0 ? gcc!.EosbYears1To5Rate : 21m;
        var rate2       = gcc?.EosbYearsAbove5Rate > 0 ? gcc!.EosbYearsAbove5Rate : 30m;
        var dailySalary = basicSalary * 12 / 365m;
        decimal eosbAmount = 0m;
        if (totalYears >= minYears)
            eosbAmount = totalYears <= 5
                ? Math.Round(dailySalary * rate1 * (decimal)totalYears, 2)
                : Math.Round(dailySalary * rate1 * 5 + dailySalary * rate2 * (decimal)(totalYears - 5), 2);

        // Leave encashment: remaining balance × daily gross (30-day basis)
        var leaveBalances = await _db.EmployeeLeaveBalances.AsNoTracking()
            .Where(x => x.TenantId == tenantId && x.EmployeeId == req.EmployeeId && x.Year == lastDay.Year)
            .ToListAsync(cancellationToken);
        var leaveBalanceDays = Math.Max(0m, leaveBalances.Sum(b => b.Accrued + b.CarriedForward + b.ManualAdjustment - b.Used - b.Pending - b.Encashed - b.Expired));
        var leaveEncashment  = Math.Round(leaveBalanceDays * grossSalary / 30m, 2);

        // Notice period deduction for days short
        var noticePeriodDeduction = Math.Round(req.NoticePeriodDaysShort * grossSalary / 30m, 2);

        var totalPayable = proRataSalary + eosbAmount + leaveEncashment - noticePeriodDeduction;

        await PayrollAudit("payroll.final_settlement.calculated", "Employee", req.EmployeeId.ToString(), new { lastWorkingDay = req.LastWorkingDay, totalPayable }, cancellationToken);
        await _db.SaveChangesAsync(cancellationToken);

        return Ok(new
        {
            employeeId = req.EmployeeId, employeeName = employee.FullName,
            lastWorkingDay = req.LastWorkingDay, currency,
            basicSalary, grossSalary,
            proRataSalary, daysWorkedInMonth = lastDay.Day, daysInMonth,
            eosbAmount, totalYears = Math.Round(totalYears, 2),
            leaveBalanceDays, leaveEncashment,
            noticePeriodDaysShort = req.NoticePeriodDaysShort, noticePeriodDeduction,
            totalPayable = Math.Round(totalPayable, 2),
            breakdown = new[]
            {
                new { component = "Pro-rata Salary",          amount =  proRataSalary },
                new { component = "EOSB / Gratuity",          amount =  eosbAmount },
                new { component = "Leave Encashment",         amount =  leaveEncashment },
                new { component = "Notice Period Deduction",  amount = -noticePeriodDeduction },
            }
        });
    }

    /// <summary>
    /// Resolves the tenant's payroll currency.
    /// Priority: Company.DefaultCurrency → TenantLocalizationSetting.CurrencyCode → "USD"
    /// </summary>
    private async Task<string> ResolveCurrencyAsync(Guid tenantId, CancellationToken ct)
    {
        var company = await _db.Companies.AsNoTracking()
            .Where(c => c.TenantId == tenantId)
            .OrderByDescending(c => c.IsActive)
            .Select(c => c.DefaultCurrency)
            .FirstOrDefaultAsync(ct);
        if (!string.IsNullOrWhiteSpace(company)) return company;

        var loc = await _db.TenantLocalizationSettings.AsNoTracking()
            .Where(l => l.TenantId == tenantId)
            .Select(l => l.CurrencyCode)
            .FirstOrDefaultAsync(ct);
        return string.IsNullOrWhiteSpace(loc) ? "USD" : loc;
    }

    private Guid GetTenantId() => Guid.Parse(User.FindFirstValue("tenant_id")!);
    private Guid? GetUserId() => Guid.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier) ?? User.FindFirstValue("sub"), out var id) ? id : null;
    private bool HasPermission(string permission) =>
        User.Claims.Any(c => c.Type == "permission" && string.Equals(c.Value, permission, StringComparison.OrdinalIgnoreCase));
}

public record CreatePayrollRunRequest(int Year, int Month);
public record SalaryStructureRequest(string Code, string Name, string? Currency, DateOnly EffectiveDate, IReadOnlyCollection<SalaryComponentRequest>? Components);
public record SalaryComponentRequest(string Code, string Name, string ComponentType, string CalculationType, decimal Amount, decimal Percentage, bool IsTaxable);
public record EmployeeSalaryStructureRequest(int EmployeeId, Guid SalaryStructureId, decimal BasicSalary, decimal HousingAllowance, decimal TransportAllowance, decimal FoodAllowance, decimal MobileAllowance, decimal OtherAllowance, decimal FixedDeduction, DateOnly EffectiveDate, string? Currency);
public record PayrollDecisionRequest(string? Notes);
public record PayrollPaymentBatchRequest(string? PaymentMethod, string? Currency);
public record WpsStatusRequest(string Status, string? Notes);
public record PayrollGroupRequest(string Code, string Name, string? Currency);
public record ImportSalaryStructuresRequest(string CsvContent);
public record EosbCalculationRequest(int EmployeeId, DateTime? AsOfDate);
public record FinalSettlementRequest(int EmployeeId, DateOnly LastWorkingDay, int NoticePeriodDaysShort = 0);
