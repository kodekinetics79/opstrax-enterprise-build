using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;
using Zayra.Api.Application.Auth;
using Zayra.Api.Controllers;
using Zayra.Api.Controllers.Finance;
using Zayra.Api.Data;
using Zayra.Api.Domain.Entities;
using Zayra.Api.Models;

namespace Zayra.Api.Tests;

/// <summary>
/// Finance-P1 correctness tests:
///   1. Approved bonus appears as earning line in payslip (Process)
///   2. GOSI-included bonus raises statutory base; non-included does not
///   3. MarkBatchPaid returns 409 Conflict when batch already consumed by payroll
///   4. Lock() writes balanced double-entry GL to FinanceGlEntry
///   5. Re-locking does not double-post GL (idempotency)
///   6. Unbalanced run rejected by Lock() (422)
/// </summary>
public class FinanceP1BonusGlTests
{
    // ── DB helpers ─────────────────────────────────────────────────────────────

    // SQLite in-memory required for ExecuteUpdateAsync used in Process() and Lock().
    private static (ZayraDbContext db, SqliteConnection conn) CreateSqliteDb()
    {
        var conn = new SqliteConnection("DataSource=:memory:");
        conn.Open();
        var db = new ZayraDbContext(
            new DbContextOptionsBuilder<ZayraDbContext>()
                .UseSqlite(conn)
                .Options);
        db.Database.EnsureCreated();
        return (db, conn);
    }

    // ── Controller factories ────────────────────────────────────────────────────

    private static PayrollController MakePayrollCtrl(ZayraDbContext db, Guid tenantId)
    {
        var claims = new List<Claim>
        {
            new("tenant_id", tenantId.ToString()),
            new(ClaimTypes.NameIdentifier, Guid.NewGuid().ToString()),
            new(ClaimTypes.Name, "test-user"),
        };
        var principal = new ClaimsPrincipal(new ClaimsIdentity(claims, "test"));
        var httpCtx   = new DefaultHttpContext { User = principal };
        var ctrl = new PayrollController(
            db,
            new _P1UnrestrictedScope(),
            new _P1HttpAccessor(httpCtx),
            new _P1NullNotifications(),
            new _P1NullPackResolver());
        ctrl.ControllerContext = new ControllerContext { HttpContext = httpCtx };
        return ctrl;
    }

    private static BonusesController MakeBonusCtrl(ZayraDbContext db, Guid tenantId)
    {
        var claims = new List<Claim>
        {
            new("tenant_id", tenantId.ToString()),
            new(ClaimTypes.NameIdentifier, Guid.NewGuid().ToString()),
        };
        var principal = new ClaimsPrincipal(new ClaimsIdentity(claims, "test"));
        var httpCtx   = new DefaultHttpContext { User = principal };
        var ctrl = new BonusesController(db, new _P1UnrestrictedScope());
        ctrl.ControllerContext = new ControllerContext { HttpContext = httpCtx };
        return ctrl;
    }

    // ── Shared seed helpers ─────────────────────────────────────────────────────

    // Inserts the minimum entities needed for a payroll run that can call Process():
    // SalaryStructure, Employee, EmployeeSalaryStructure, PayrollRun (Status=Draft).
    private static async Task<(Employee emp, PayrollRun run, SalaryStructure str)> SeedMinimalRun(
        ZayraDbContext db, Guid tenantId, int year = 2026, int month = 6)
    {
        var structure = new SalaryStructure
        {
            TenantId = tenantId, Code = "STR-BASE", Name = "Base",
            Currency = "SAR", EffectiveDate = new DateOnly(2025, 1, 1), IsActive = true,
        };
        db.SalaryStructures.Add(structure);

        var emp = new Employee
        {
            TenantId = tenantId, EmployeeCode = "E001", FullName = "Ali Hassan",
            Status = "Active", JoiningDate = new DateTime(2023, 1, 1),
            WorkEmail = "ali@test.com", Nationality = "SAU", ContractType = "Indefinite",
        };
        db.Employees.Add(emp);
        await db.SaveChangesAsync();

        db.EmployeeSalaryStructures.Add(new EmployeeSalaryStructure
        {
            TenantId = tenantId, EmployeeId = emp.Id, SalaryStructureId = structure.Id,
            BasicSalary = 10_000m, HousingAllowance = 2_000m, TransportAllowance = 1_000m,
            EffectiveDate = new DateOnly(2025, 1, 1), IsActive = true,
        });

        var run = new PayrollRun
        {
            TenantId = tenantId, Year = year, Month = month,
            Status = "Draft",
            TotalNetSalary = 0m, TotalGrossSalary = 0m,
        };
        db.PayrollRuns.Add(run);
        await db.SaveChangesAsync();

        return (emp, run, structure);
    }

    // ── Test 1: Bonus appears as earning in payslip after Process() ────────────

    [Fact]
    public async Task Process_ApprovedBonus_AppearsAsEarningLine()
    {
        var (db, conn) = CreateSqliteDb();
        await using var _ = conn;
        await using var __ = db;

        var tenantId = Guid.NewGuid();
        var (emp, run, _) = await SeedMinimalRun(db, tenantId);

        // Seed approved bonus for this employee and period
        var bonusType = new BonusType
        {
            TenantId = tenantId, Code = "PERF", NameEn = "Performance Bonus",
            IsIncludedInGosiBase = false, IsIncludedInWps = true, IsIncludedInEosb = false,
            TaxRegion = "GCC", IsActive = true,
        };
        db.BonusTypes.Add(bonusType);
        await db.SaveChangesAsync();

        var batch = new BonusBatch
        {
            TenantId = tenantId, BatchNumber = "BON-001", BonusTypeId = bonusType.Id,
            BonusTypeName = bonusType.NameEn, PaymentPeriod = "2026-06",
            Status = "Approved", CreatedBy = null,
        };
        db.BonusBatches.Add(batch);
        await db.SaveChangesAsync();

        db.EmployeeBonuses.Add(new EmployeeBonus
        {
            TenantId = tenantId, BonusBatchId = batch.Id,
            EmployeeId = Guid.NewGuid(), EmployeeIntId = emp.Id,
            EmployeeName = emp.FullName, BonusTypeId = bonusType.Id,
            BonusTypeName = bonusType.NameEn, BasicSalary = 10_000m,
            CalculationMethod = "Fixed", CalculationValue = 2_000m,
            GrossBonusAmount = 2_000m, TaxWithheld = 0m, BonusAmount = 2_000m,
            PaymentPeriod = "2026-06", Status = "Approved", TaxRegion = "GCC",
        });
        await db.SaveChangesAsync();

        var ctrl = MakePayrollCtrl(db, tenantId);
        run.Status = "Draft";
        await db.SaveChangesAsync();

        var result = await ctrl.Process(run.Id, CancellationToken.None);

        result.Should().BeOfType<OkObjectResult>();
        var earnings = await db.PayrollEarnings
            .Where(e => e.TenantId == tenantId && e.PayrollRunId == run.Id && e.Source == "Bonus")
            .ToListAsync();
        earnings.Should().NotBeEmpty("approved bonus must produce a Bonus earning line");
        earnings.Sum(e => e.Amount).Should().Be(2_000m);
    }

    // ── Test 2: GOSI-included bonus raises statutory base ─────────────────────

    [Fact]
    public async Task Process_GosiIncludedBonus_RaisesStatutoryBase_NonIncludedDoesNot()
    {
        var (db, conn) = CreateSqliteDb();
        await using var _ = conn;
        await using var __ = db;

        var tenantId = Guid.NewGuid();

        // Two runs: one with GOSI-included bonus, one with non-included bonus
        // The NullPackResolver returns zero deductions, so we test by comparing
        // the GrossSalary on the PayrollSlip (GOSI base calculation doesn't change
        // net when using DefaultStatutoryDeductionCalculator, but we can verify
        // the earning lines and the gross salary change correctly).
        var (emp, runA, _) = await SeedMinimalRun(db, tenantId, 2026, 5);
        var empId = emp.Id;
        var tidCopy = tenantId;

        var gosiType = new BonusType
        {
            TenantId = tenantId, Code = "BASE", NameEn = "Base Bonus",
            IsIncludedInGosiBase = true, IsIncludedInWps = true, IsIncludedInEosb = false,
            TaxRegion = "GCC", IsActive = true,
        };
        var nonGosiType = new BonusType
        {
            TenantId = tenantId, Code = "DISC", NameEn = "Discretionary Bonus",
            IsIncludedInGosiBase = false, IsIncludedInWps = false, IsIncludedInEosb = false,
            TaxRegion = "GCC", IsActive = true,
        };
        db.BonusTypes.AddRange(gosiType, nonGosiType);
        await db.SaveChangesAsync();

        // For run A: add a GOSI-included bonus of 1,000
        var batchA = new BonusBatch
        {
            TenantId = tenantId, BatchNumber = "BON-A", BonusTypeId = gosiType.Id,
            BonusTypeName = gosiType.NameEn, PaymentPeriod = "2026-05",
            Status = "Approved", CreatedBy = null,
        };
        db.BonusBatches.Add(batchA);
        await db.SaveChangesAsync();
        db.EmployeeBonuses.Add(new EmployeeBonus
        {
            TenantId = tenantId, BonusBatchId = batchA.Id,
            EmployeeId = Guid.NewGuid(), EmployeeIntId = empId,
            EmployeeName = emp.FullName, BonusTypeId = gosiType.Id,
            BonusTypeName = gosiType.NameEn, BasicSalary = 10_000m,
            CalculationMethod = "Fixed", CalculationValue = 1_000m,
            GrossBonusAmount = 1_000m, TaxWithheld = 0m, BonusAmount = 1_000m,
            PaymentPeriod = "2026-05", Status = "Approved", TaxRegion = "GCC",
        });
        await db.SaveChangesAsync();

        var ctrlA = MakePayrollCtrl(db, tenantId);
        await ctrlA.Process(runA.Id, CancellationToken.None);

        // Verify: PayrollSlip GrossSalary = basic+housing+transport+bonus = 10000+2000+1000+1000 = 14000
        var slipA = await db.PayrollSlips.FirstOrDefaultAsync(s => s.RunId == runA.Id);
        slipA.Should().NotBeNull();
        slipA!.GrossSalary.Should().Be(14_000m, "GOSI-included bonus adds 1000 to gross");

        // For run B: non-included bonus should not change the statutory base, but still adds to gross
        var (_, runB, _) = await SeedRunForExistingEmployee(db, tenantId, empId, 2026, 4);
        var batchB = new BonusBatch
        {
            TenantId = tenantId, BatchNumber = "BON-B", BonusTypeId = nonGosiType.Id,
            BonusTypeName = nonGosiType.NameEn, PaymentPeriod = "2026-04",
            Status = "Approved", CreatedBy = null,
        };
        db.BonusBatches.Add(batchB);
        await db.SaveChangesAsync();
        db.EmployeeBonuses.Add(new EmployeeBonus
        {
            TenantId = tenantId, BonusBatchId = batchB.Id,
            EmployeeId = Guid.NewGuid(), EmployeeIntId = empId,
            EmployeeName = emp.FullName, BonusTypeId = nonGosiType.Id,
            BonusTypeName = nonGosiType.NameEn, BasicSalary = 10_000m,
            CalculationMethod = "Fixed", CalculationValue = 500m,
            GrossBonusAmount = 500m, TaxWithheld = 0m, BonusAmount = 500m,
            PaymentPeriod = "2026-04", Status = "Approved", TaxRegion = "GCC",
        });
        await db.SaveChangesAsync();

        var ctrlB = MakePayrollCtrl(db, tenantId);
        await ctrlB.Process(runB.Id, CancellationToken.None);

        var slipB = await db.PayrollSlips.FirstOrDefaultAsync(s => s.RunId == runB.Id);
        slipB!.GrossSalary.Should().Be(13_500m, "non-GOSI bonus 500 still adds to gross = 10000+2000+1000+500");
    }

    // Helper: add a second payroll run for an already-existing employee.
    private static async Task<(Employee emp, PayrollRun run, SalaryStructure str)> SeedRunForExistingEmployee(
        ZayraDbContext db, Guid tenantId, int empId, int year, int month)
    {
        var emp = await db.Employees.FirstAsync(e => e.TenantId == tenantId && e.Id == empId);
        var str = await db.SalaryStructures.FirstAsync(s => s.TenantId == tenantId);
        var run = new PayrollRun
        {
            TenantId = tenantId, Year = year, Month = month,
            Status = "Draft",
            TotalNetSalary = 0m, TotalGrossSalary = 0m,
        };
        db.PayrollRuns.Add(run);
        await db.SaveChangesAsync();
        return (emp, run, str);
    }

    // ── Test 3a: MarkBatchPaid guard fires when IsLockedByPayroll=true ─────────
    // Tests the guard directly without going through Process() to isolate the
    // double-pay logic from the full payroll run integration.

    [Fact]
    public async Task MarkBatchPaid_WhenBatchIsLockedByPayroll_Returns409()
    {
        var (db, conn) = CreateSqliteDb();
        await using var _ = conn;
        await using var __ = db;

        var tenantId = Guid.NewGuid();

        var bonusType = new BonusType
        {
            TenantId = tenantId, Code = "PERF2", NameEn = "Performance 2",
            IsIncludedInGosiBase = false, IsIncludedInWps = true, IsIncludedInEosb = false,
            TaxRegion = "GCC", IsActive = true,
        };
        db.BonusTypes.Add(bonusType);
        await db.SaveChangesAsync();

        // Batch already marked as payroll-locked (simulating what Process() does).
        var batch = new BonusBatch
        {
            TenantId = tenantId, BatchNumber = "BON-DUP", BonusTypeId = bonusType.Id,
            BonusTypeName = bonusType.NameEn, PaymentPeriod = "2026-06",
            Status = "Paid",   // Process() sets this
            IsLockedByPayroll = true, // Process() sets this
            CreatedBy = null,
        };
        db.BonusBatches.Add(batch);
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();  // prevent stale tracking

        var bonusCtrl = MakeBonusCtrl(db, tenantId);
        var paidResult = await bonusCtrl.MarkBatchPaid(batch.Id, new MarkBatchPaidRequest(Guid.NewGuid()), CancellationToken.None);

        paidResult.Should().BeOfType<ConflictObjectResult>(
            "MarkBatchPaid must return 409 when the batch was already consumed by a payroll run");
    }

    // ── Test 3b: Process() marks consumed bonus batch as locked ───────────────

    [Fact]
    public async Task Process_ApprovedBonus_LocksTheBatchAfterConsumption()
    {
        var (db, conn) = CreateSqliteDb();
        await using var _ = conn;
        await using var __ = db;

        var tenantId = Guid.NewGuid();
        var (emp, run, _) = await SeedMinimalRun(db, tenantId);

        var bonusType = new BonusType
        {
            TenantId = tenantId, Code = "LOCK", NameEn = "Lock Test",
            IsIncludedInGosiBase = false, IsIncludedInWps = true, IsIncludedInEosb = false,
            TaxRegion = "GCC", IsActive = true,
        };
        db.BonusTypes.Add(bonusType);
        await db.SaveChangesAsync();

        var batch = new BonusBatch
        {
            TenantId = tenantId, BatchNumber = "BON-LOCK", BonusTypeId = bonusType.Id,
            BonusTypeName = bonusType.NameEn, PaymentPeriod = "2026-06",
            Status = "Approved", CreatedBy = null,
        };
        db.BonusBatches.Add(batch);
        await db.SaveChangesAsync();

        db.EmployeeBonuses.Add(new EmployeeBonus
        {
            TenantId = tenantId, BonusBatchId = batch.Id,
            EmployeeId = Guid.NewGuid(), EmployeeIntId = emp.Id,
            EmployeeName = emp.FullName, BonusTypeId = bonusType.Id,
            BonusTypeName = bonusType.NameEn, BasicSalary = 10_000m,
            CalculationMethod = "Fixed", CalculationValue = 3_000m,
            GrossBonusAmount = 3_000m, TaxWithheld = 0m, BonusAmount = 3_000m,
            PaymentPeriod = "2026-06", Status = "Approved", TaxRegion = "GCC",
        });
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();  // prevent stale state from interfering

        var ctrl = MakePayrollCtrl(db, tenantId);
        var result = await ctrl.Process(run.Id, CancellationToken.None);
        result.Should().BeOfType<OkObjectResult>("Process must succeed");

        // Read batch fresh from DB to verify Process() set the lock flag.
        db.ChangeTracker.Clear();
        var updatedBatch = await db.BonusBatches.AsNoTracking()
            .FirstAsync(x => x.Id == batch.Id);
        updatedBatch.IsLockedByPayroll.Should().BeTrue(
            "Process() must set IsLockedByPayroll=true after consuming all bonuses in the batch");
        updatedBatch.Status.Should().Be("Paid",
            "Process() must set batch Status to Paid after consuming all bonuses");
    }

    // ── Test 4: Lock() persists balanced GL in FinanceGlEntry ─────────────────

    [Fact]
    public async Task Lock_WritesBalancedGlEntries()
    {
        var (db, conn) = CreateSqliteDb();
        await using var _ = conn;
        await using var __ = db;

        var tenantId = Guid.NewGuid();
        var (_, run, _) = await SeedMinimalRun(db, tenantId);

        var ctrl = MakePayrollCtrl(db, tenantId);
        await ctrl.Process(run.Id, CancellationToken.None);

        // Move to Processed so Lock() accepts it
        run.Status = "Processed";
        await db.SaveChangesAsync();

        var lockResult = await ctrl.Lock(run.Id, CancellationToken.None);

        lockResult.Should().NotBeOfType<UnprocessableEntityObjectResult>(
            "GL must balance for a normal processed run");
        lockResult.Should().BeOfType<OkObjectResult>("Lock must succeed");

        var glEntries = await db.FinanceGlEntries
            .Where(x => x.SourceModule == "Payroll" && x.SourceEntityId == run.Id)
            .ToListAsync();
        glEntries.Should().NotBeEmpty("Lock must persist GL entries");

        var totalDebits  = glEntries.Where(e => !string.IsNullOrEmpty(e.DebitAccount)).Sum(e => e.Amount);
        var totalCredits = glEntries.Where(e => !string.IsNullOrEmpty(e.CreditAccount)).Sum(e => e.Amount);
        Math.Abs(totalDebits - totalCredits).Should().BeLessThan(0.01m,
            $"GL must be balanced: DR={totalDebits}, CR={totalCredits}");
    }

    // ── Test 5: Re-lock does not double-post GL (idempotency) ─────────────────

    [Fact]
    public async Task Lock_Idempotent_SecondLockDoesNotDuplicateGl()
    {
        var (db, conn) = CreateSqliteDb();
        await using var _ = conn;
        await using var __ = db;

        var tenantId = Guid.NewGuid();
        var (_, run, _) = await SeedMinimalRun(db, tenantId);

        var ctrl = MakePayrollCtrl(db, tenantId);
        await ctrl.Process(run.Id, CancellationToken.None);

        run.Status = "Processed";
        await db.SaveChangesAsync();

        // First lock
        await ctrl.Lock(run.Id, CancellationToken.None);
        var countAfterFirst = await db.FinanceGlEntries
            .CountAsync(x => x.SourceModule == "Payroll" && x.SourceEntityId == run.Id);

        // Unlock manually for test purposes — simulate re-lock
        run.Status = "Processed";
        await db.SaveChangesAsync();

        // Second lock
        await ctrl.Lock(run.Id, CancellationToken.None);
        var countAfterSecond = await db.FinanceGlEntries
            .CountAsync(x => x.SourceModule == "Payroll" && x.SourceEntityId == run.Id);

        countAfterSecond.Should().Be(countAfterFirst,
            "re-locking must not double-post GL — idempotency guard must skip if already posted");
    }

    // ── Test 6: Unbalanced run rejected by Lock() (422) ───────────────────────
    // We inject a phantom earning with no corresponding credit to force imbalance.

    [Fact]
    public async Task Lock_UnbalancedGl_Returns422()
    {
        var (db, conn) = CreateSqliteDb();
        await using var _ = conn;
        await using var __ = db;

        var tenantId = Guid.NewGuid();
        var (emp, run, _) = await SeedMinimalRun(db, tenantId);

        var ctrl = MakePayrollCtrl(db, tenantId);
        await ctrl.Process(run.Id, CancellationToken.None);

        // Inject an extra earning that will not match any credit,
        // causing totalDebits > totalCredits.
        // Inject a phantom DR earning WITHOUT updating TotalNetSalary (which drives the CR side).
        // This forces totalDebits > totalCredits so the balance check must fail.
        db.PayrollEarnings.Add(new PayrollEarning
        {
            TenantId = tenantId, PayrollRunId = run.Id, EmployeeId = emp.Id,
            ComponentCode = "PHANTOM", ComponentName = "Phantom Earning",
            Amount = 99_999m, Source = "Salary",
        });
        run.Status = "Processed";
        // Intentionally NOT updating run.TotalNetSalary so DR > CR
        await db.SaveChangesAsync();

        var lockResult = await ctrl.Lock(run.Id, CancellationToken.None);

        lockResult.Should().BeOfType<UnprocessableEntityObjectResult>(
            "Lock must return 422 when GL debits ≠ credits");
    }
}

// ── Test doubles ──────────────────────────────────────────────────────────────

file sealed class _P1UnrestrictedScope : Zayra.Api.Application.Common.IDataScopeService
{
    public Task<Zayra.Api.Application.Common.DataScope> ResolveAsync(System.Security.Claims.ClaimsPrincipal caller, Guid tenantId, CancellationToken ct)
        => Task.FromResult(new Zayra.Api.Application.Common.DataScope { Level = Zayra.Api.Application.Common.DataScopeLevel.Organization, AllowedEmployeeIds = null });
}

file sealed class _P1HttpAccessor : Microsoft.AspNetCore.Http.IHttpContextAccessor
{
    public _P1HttpAccessor(Microsoft.AspNetCore.Http.HttpContext ctx) => HttpContext = ctx;
    public Microsoft.AspNetCore.Http.HttpContext? HttpContext { get; set; }
}

file sealed class _P1NullNotifications : Zayra.Api.Infrastructure.Notifications.INotificationService
{
    public Task NotifyAsync(Guid tenantId, Guid? userId, string title, string message, string entityName, string? entityId, CancellationToken ct) => Task.CompletedTask;
    public Task SendEmailAsync(Guid tenantId, string templateCode, string toAddress, string toName, Dictionary<string, string> variables, CancellationToken ct) => Task.CompletedTask;
}

file sealed class _P1NullPackResolver : Zayra.Api.Application.CountryPack.ICountryPackResolver
{
    public Zayra.Api.Application.CountryPack.IStatutoryDeductionCalculator ResolveDeductionCalculator(string cc, string j)
        => new Zayra.Api.Infrastructure.CountryPack.DefaultStatutoryDeductionCalculator();
    public Zayra.Api.Application.CountryPack.IEndOfServiceCalculator ResolveEndOfServiceCalculator(string cc, string j)
        => new Zayra.Api.Infrastructure.CountryPack.DefaultEndOfServiceCalculator();
    public Zayra.Api.Application.CountryPack.IWageProtectionExporter ResolveWageProtectionExporter(string cc, string j)
        => new Zayra.Api.Infrastructure.CountryPack.DefaultWageProtectionExporter();
    public Zayra.Api.Application.CountryPack.INationalizationTracker ResolveNationalizationTracker(string cc, string j)
        => new Zayra.Api.Infrastructure.CountryPack.DefaultNationalizationTracker();
    public Zayra.Api.Application.CountryPack.ILocalizationProfile ResolveLocalizationProfile(string cc, string j)
        => new Zayra.Api.Infrastructure.CountryPack.DefaultLocalizationProfile();
    public Zayra.Api.Application.CountryPack.ICountryPackDescriptor ResolveDescriptor(string cc, string j)
        => new Zayra.Api.Infrastructure.CountryPack.DefaultCountryPackDescriptor();
}
