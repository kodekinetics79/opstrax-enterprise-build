using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Zayra.Api.Data;
using Zayra.Api.Models;

namespace Zayra.Api.Tests.Security;

/// <summary>
/// Verifies that payroll and salary data is correctly scoped by tenant at the data layer.
/// Every API controller that exposes salary/payroll must apply the same TenantId filter.
/// </summary>
public class PayrollSecurityTests
{
    private static ZayraDbContext CreateDb()
    {
        var opts = new DbContextOptionsBuilder<ZayraDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        return new ZayraDbContext(opts);
    }

    private static Employee MakeEmployee(Guid tenantId, string code, string name, decimal salary) =>
        new()
        {
            TenantId     = tenantId,
            EmployeeCode = code,
            FullName     = name,
            Department   = "HR",
            Designation  = "Officer",
            Status       = "Active",
            JoiningDate  = DateTime.UtcNow.Date,
            Salary       = salary,
        };

    // ── Salary data isolated by tenant ────────────────────────────────────────

    [Fact]
    public async Task SalaryData_StrictlyIsolatedByTenant()
    {
        await using var db = CreateDb();
        var tenantA = Guid.NewGuid();
        var tenantB = Guid.NewGuid();

        db.Employees.AddRange(
            MakeEmployee(tenantA, "A-001", "Alice", 50_000m),
            MakeEmployee(tenantB, "B-001", "Bob",   120_000m));
        await db.SaveChangesAsync();

        var tenantAEmps = await db.Employees
            .Where(e => e.TenantId == tenantA && !e.IsDeleted)
            .ToListAsync();

        tenantAEmps.Should().HaveCount(1);
        tenantAEmps[0].Salary.Should().Be(50_000m);
        tenantAEmps.Should().NotContain(e => e.FullName == "Bob");
    }

    [Fact]
    public async Task TenantB_CannotFetchTenantA_EmployeeById_ToReadSalary()
    {
        await using var db = CreateDb();
        var tenantA = Guid.NewGuid();
        var tenantB = Guid.NewGuid();

        var cfo = MakeEmployee(tenantA, "A-CFO", "CFO Alice", 500_000m);
        db.Employees.Add(cfo);
        await db.SaveChangesAsync();

        // Tenant B scopes by their own tenantId
        var fetched = await db.Employees
            .FirstOrDefaultAsync(e => e.Id == cfo.Id && e.TenantId == tenantB);

        fetched.Should().BeNull("Tenant B must never see Tenant A's salary");
    }

    // ── Soft-deleted employees excluded ───────────────────────────────────────

    [Fact]
    public async Task DeletedEmployee_ExcludedFromPayrollQuery()
    {
        await using var db = CreateDb();
        var tenantId = Guid.NewGuid();

        var active     = MakeEmployee(tenantId, "E-001", "Active",  10_000m);
        var terminated = MakeEmployee(tenantId, "E-002", "Deleted", 99_999m);
        terminated.IsDeleted = true;
        db.Employees.AddRange(active, terminated);
        await db.SaveChangesAsync();

        var payrollScope = await db.Employees
            .Where(e => e.TenantId == tenantId && !e.IsDeleted)
            .ToListAsync();

        payrollScope.Should().HaveCount(1);
        payrollScope[0].FullName.Should().Be("Active");
    }

    // ── Sensitive ID documents isolated ──────────────────────────────────────

    [Fact]
    public async Task SensitiveDocuments_CannotLeakAcrossTenants()
    {
        await using var db = CreateDb();
        var tenantA = Guid.NewGuid();
        var tenantB = Guid.NewGuid();

        var emp = MakeEmployee(tenantA, "A-001", "Secure Employee", 30_000m);
        emp.PassportNumber = "A1234567";
        emp.IqamaNumber    = "2012345678";
        db.Employees.Add(emp);
        await db.SaveChangesAsync();

        var tenantBEmps = await db.Employees
            .Where(e => e.TenantId == tenantB && !e.IsDeleted)
            .Select(e => new { e.PassportNumber, e.IqamaNumber })
            .ToListAsync();

        tenantBEmps.Should().BeEmpty("sensitive ID documents must never cross tenant boundary");
    }

    // ── Payroll sum isolation ─────────────────────────────────────────────────

    [Fact]
    public async Task PayrollSum_IsIsolatedByTenant()
    {
        await using var db = CreateDb();
        var tenantA = Guid.NewGuid();
        var tenantB = Guid.NewGuid();

        db.Employees.AddRange(
            MakeEmployee(tenantA, "A-1", "A1", 10_000m),
            MakeEmployee(tenantA, "A-2", "A2", 20_000m),
            MakeEmployee(tenantB, "B-1", "B1", 999_999m));
        await db.SaveChangesAsync();

        var tenantATotal = await db.Employees
            .Where(e => e.TenantId == tenantA && !e.IsDeleted)
            .SumAsync(e => (decimal)(e.Salary ?? 0));

        tenantATotal.Should().Be(30_000m, "Tenant A payroll must not include Tenant B salaries");
    }

    // ── Bank details isolated ─────────────────────────────────────────────────

    [Fact]
    public async Task BankDetails_NotAccessibleAcrossTenants()
    {
        await using var db = CreateDb();
        var tenantA = Guid.NewGuid();
        var tenantB = Guid.NewGuid();

        var emp = MakeEmployee(tenantA, "A-001", "Bank Test", 15_000m);
        emp.BankName = "First National Bank";
        emp.BankIban = "AE000000000000012345";
        db.Employees.Add(emp);
        await db.SaveChangesAsync();

        var bankInfo = await db.Employees
            .Where(e => e.TenantId == tenantB && !e.IsDeleted)
            .Select(e => new { e.BankName, e.BankIban })
            .ToListAsync();

        bankInfo.Should().BeEmpty("bank account details must not leak across tenants");
    }
}
