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

        // Delete existing slips so reprocessing is idempotent
        var existingSlips = _db.PayrollSlips.Where(s => s.RunId == id && s.TenantId == tenantId);
        _db.PayrollSlips.RemoveRange(existingSlips);

        var employees = await _db.Employees.Where(e => (e.TenantId == tenantId || e.TenantId == null) && e.Status == "Active").ToListAsync(cancellationToken);

        var slips = employees.Select(e =>
        {
            var basic = e.Salary ?? 5000m;
            var housing = Math.Round(basic * 0.25m, 2);
            var transport = Math.Round(basic * 0.10m, 2);
            var gross = basic + housing + transport;
            var deductions = Math.Round(gross * 0.05m, 2); // 5% GOSI
            return new PayrollSlip
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
                GrossSalary = gross,
                Deductions = deductions,
                NetSalary = gross - deductions,
                Status = "Draft",
            };
        }).ToList();

        _db.PayrollSlips.AddRange(slips);
        run.Status = "Processed";
        run.ProcessedAtUtc = DateTime.UtcNow;
        run.EmployeeCount = slips.Count;
        run.TotalGrossSalary = slips.Sum(s => s.GrossSalary);
        run.TotalDeductions = slips.Sum(s => s.Deductions);
        run.TotalNetSalary = slips.Sum(s => s.NetSalary);
        await _db.SaveChangesAsync(cancellationToken);
        return Ok(run);
    }

    [HttpPost("runs/{id:guid}/lock")]
    public async Task<IActionResult> Lock(Guid id, CancellationToken cancellationToken)
    {
        var tenantId = GetTenantId();
        var run = await _db.PayrollRuns.FirstOrDefaultAsync(r => r.Id == id && r.TenantId == tenantId, cancellationToken);
        if (run is null) return NotFound();
        if (run.Status != "Processed") return BadRequest(new { message = "Only processed runs can be locked." });
        run.Status = "Locked";
        run.LockedAtUtc = DateTime.UtcNow;
        await _db.PayrollSlips.Where(s => s.RunId == id).ExecuteUpdateAsync(s => s.SetProperty(p => p.Status, "Final"), cancellationToken);
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

    private Guid GetTenantId() => Guid.Parse(User.FindFirstValue("tenant_id")!);
    private Guid? GetUserId() => Guid.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier) ?? User.FindFirstValue("sub"), out var id) ? id : null;
}

public record CreatePayrollRunRequest(int Year, int Month);
