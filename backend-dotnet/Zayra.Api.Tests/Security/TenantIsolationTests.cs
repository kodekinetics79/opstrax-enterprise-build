using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Zayra.Api.Data;
using Zayra.Api.Domain.Entities;
using Zayra.Api.Models;

namespace Zayra.Api.Tests.Security;

/// <summary>
/// Verifies that tenant-scoped data queries are correctly isolated by tenant_id.
/// Tenant isolation in Zayra is enforced at the EF query level: every controller reads
/// from the DB with a WHERE tenant_id = {claim} filter.
///
/// These tests prove the data layer contracts that underpin that enforcement:
///   1. Querying employees for Tenant A never returns Tenant B records.
///   2. Querying Tenant B never reveals Tenant A documents.
///   3. Platform admin can see cross-tenant data (no WHERE tenant_id restriction).
///
/// Uses InMemory DB — no Docker/MySQL required.
/// </summary>
public class TenantIsolationTests
{
    private static ZayraDbContext CreateDb()
    {
        var opts = new DbContextOptionsBuilder<ZayraDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        return new ZayraDbContext(opts);
    }

    // ── Employee isolation ─────────────────────────────────────────────────────

    [Fact]
    public async Task EmployeeQuery_WithTenantIdFilter_OnlyReturnsTenantAEmployees()
    {
        await using var db = CreateDb();

        var tenantA = Guid.NewGuid();
        var tenantB = Guid.NewGuid();

        db.Employees.AddRange(
            new Employee { TenantId = tenantA, EmployeeCode = "EMP-A-001", FullName = "Alice A", Department = "HR",    Designation = "Officer", Status = "Active", JoiningDate = DateTime.UtcNow.Date, Salary = 10000m },
            new Employee { TenantId = tenantA, EmployeeCode = "EMP-A-002", FullName = "Bob A",   Department = "IT",    Designation = "Dev",     Status = "Active", JoiningDate = DateTime.UtcNow.Date, Salary = 12000m },
            new Employee { TenantId = tenantB, EmployeeCode = "EMP-B-001", FullName = "Carol B", Department = "HR",    Designation = "Manager", Status = "Active", JoiningDate = DateTime.UtcNow.Date, Salary = 15000m });
        await db.SaveChangesAsync();

        // Simulate what the controller does: filter by the authenticated tenant_id claim
        var tenantAResults = await db.Employees
            .Where(e => e.TenantId == tenantA && !e.IsDeleted)
            .ToListAsync();

        tenantAResults.Should().HaveCount(2);
        tenantAResults.Select(e => e.FullName).Should().Contain("Alice A").And.Contain("Bob A");
        tenantAResults.Select(e => e.FullName).Should().NotContain("Carol B");
    }

    [Fact]
    public async Task EmployeeQuery_TenantBCannotSeeTenantARecord()
    {
        await using var db = CreateDb();

        var tenantA = Guid.NewGuid();
        var tenantB = Guid.NewGuid();

        var secretEmployee = new Employee
        {
            TenantId    = tenantA,
            EmployeeCode = "EMP-A-SECRET",
            FullName    = "Secret Employee A",
            Department  = "Finance",
            Designation = "CFO",
            Status      = "Active",
            JoiningDate = DateTime.UtcNow.Date,
            Salary      = 99999m
        };
        db.Employees.Add(secretEmployee);
        await db.SaveChangesAsync();

        // Tenant B query — must see zero results
        var tenantBResults = await db.Employees
            .Where(e => e.TenantId == tenantB && !e.IsDeleted)
            .ToListAsync();

        tenantBResults.Should().BeEmpty();
    }

    [Fact]
    public async Task GetEmployee_ById_WithWrongTenant_ReturnsNull()
    {
        await using var db = CreateDb();

        var tenantA = Guid.NewGuid();
        var tenantB = Guid.NewGuid();

        var emp = new Employee
        {
            TenantId    = tenantA,
            EmployeeCode = "EMP-A-XYZ",
            FullName    = "Confidential",
            Department  = "Executive",
            Designation = "CEO",
            Status      = "Active",
            JoiningDate = DateTime.UtcNow.Date,
            Salary      = 50000m
        };
        db.Employees.Add(emp);
        await db.SaveChangesAsync();

        // Tenant B tries to fetch by the known numeric id + tenantB scope
        var fetched = await db.Employees
            .FirstOrDefaultAsync(e => e.Id == emp.Id && e.TenantId == tenantB && !e.IsDeleted);

        fetched.Should().BeNull("tenant B must never see tenant A's employee");
    }

    // ── Document isolation ─────────────────────────────────────────────────────

    [Fact]
    public async Task EmployeeDocuments_AreIsolatedByTenantId()
    {
        await using var db = CreateDb();

        var tenantA = Guid.NewGuid();
        var tenantB = Guid.NewGuid();

        // Create an employee and document for Tenant A
        var empA = new Employee
        {
            TenantId    = tenantA,
            EmployeeCode = "EMP-A-DOC",
            FullName    = "Doc Employee A",
            Department  = "HR",
            Designation = "Officer",
            Status      = "Active",
            JoiningDate = DateTime.UtcNow.Date,
            Salary      = 8000m
        };
        db.Employees.Add(empA);
        await db.SaveChangesAsync();

        db.EmployeeDocuments.Add(new EmployeeDocument
        {
            TenantId    = tenantA,
            EmployeeId  = empA.Id,
            DocumentType = "Passport",
            FileName    = "passport_a.pdf",
            StorageUrl  = "storage/a/passport.pdf"
        });
        await db.SaveChangesAsync();

        // Tenant B querying documents with its own tenant scope sees nothing
        var tenantBDocs = await db.EmployeeDocuments
            .Where(d => d.TenantId == tenantB)
            .ToListAsync();

        tenantBDocs.Should().BeEmpty();
    }

    // ── Company master data isolation ──────────────────────────────────────────

    [Fact]
    public async Task Companies_AreIsolatedByTenantId()
    {
        await using var db = CreateDb();

        var tenantA = Guid.NewGuid();
        var tenantB = Guid.NewGuid();

        db.Companies.AddRange(
            new Company { TenantId = tenantA, LegalNameEn = "Alpha Corp",  CountryCode = "UAE" },
            new Company { TenantId = tenantA, LegalNameEn = "Alpha Corp 2", CountryCode = "UAE" },
            new Company { TenantId = tenantB, LegalNameEn = "Beta Corp",   CountryCode = "KSA" });
        await db.SaveChangesAsync();

        var aTenantCompanies = await db.Companies.Where(c => c.TenantId == tenantA).ToListAsync();
        var bTenantCompanies = await db.Companies.Where(c => c.TenantId == tenantB).ToListAsync();

        aTenantCompanies.Should().HaveCount(2);
        aTenantCompanies.Should().NotContain(c => c.LegalNameEn == "Beta Corp");

        bTenantCompanies.Should().HaveCount(1);
        bTenantCompanies.Should().NotContain(c => c.LegalNameEn!.StartsWith("Alpha"));
    }

    // ── Platform admin: cross-tenant visibility ────────────────────────────────

    [Fact]
    public async Task PlatformAdminQuery_WithoutTenantFilter_CanSeeAllTenants()
    {
        await using var db = CreateDb();

        var tenantA = Guid.NewGuid();
        var tenantB = Guid.NewGuid();

        db.Tenants.AddRange(
            new Tenant { Id = tenantA, Name = "Alpha Org", Slug = "alpha-org" },
            new Tenant { Id = tenantB, Name = "Beta Org",  Slug = "beta-org"  });
        await db.SaveChangesAsync();

        // Platform admin query (no tenant_id WHERE clause)
        var allTenants = await db.Tenants.ToListAsync();

        allTenants.Should().HaveCount(2);
        allTenants.Select(t => t.Name).Should().Contain("Alpha Org").And.Contain("Beta Org");
    }

    // ── AdminAuditLog isolation by tenant ─────────────────────────────────────

    [Fact]
    public async Task AdminAuditLogs_WhenFilteredByTenantId_OnlyReturnThatTenantsLogs()
    {
        await using var db = CreateDb();

        var tenantA = Guid.NewGuid();
        var tenantB = Guid.NewGuid();

        db.AdminAuditLogs.AddRange(
            new AdminAuditLog { TenantId = tenantA, EntityType = "Employee", EntityId = "1", Action = "Created",   PerformedByName = "hr", IpAddress = "127.0.0.1" },
            new AdminAuditLog { TenantId = tenantA, EntityType = "Employee", EntityId = "2", Action = "Terminated", PerformedByName = "hr", IpAddress = "127.0.0.1" },
            new AdminAuditLog { TenantId = tenantB, EntityType = "Employee", EntityId = "3", Action = "Created",   PerformedByName = "hr", IpAddress = "127.0.0.1" });
        await db.SaveChangesAsync();

        var tenantALogs = await db.AdminAuditLogs.Where(l => l.TenantId == tenantA).ToListAsync();
        var tenantBLogs = await db.AdminAuditLogs.Where(l => l.TenantId == tenantB).ToListAsync();

        tenantALogs.Should().HaveCount(2);
        tenantBLogs.Should().HaveCount(1);
        tenantALogs.Should().NotContain(l => l.TenantId == tenantB);
    }
}
