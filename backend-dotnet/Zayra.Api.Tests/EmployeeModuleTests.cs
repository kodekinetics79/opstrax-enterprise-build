using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Zayra.Api.Application.Auth;
using Zayra.Api.Controllers;
using Zayra.Api.Data;
using Zayra.Api.Domain.Entities;
using Zayra.Api.Infrastructure.Audit;
using Zayra.Api.Infrastructure.Auth;
using Zayra.Api.Models;

namespace Zayra.Api.Tests;

public class EmployeeModuleTests
{
    [Fact]
    public async Task ApproveDraft_ActivatesEmployeeCreatesUserAndHistory()
    {
        await using var db = CreateDb();
        var tenantId = await SeedTenantAndEmployeeRole(db);
        var controller = CreateController(db, tenantId);
        var draftResult = await controller.CreateDraft(new EmployeeDraftRequest("Review", "Sara Ahmed", "سارة أحمد", "sara.personal@example.com", "sara@zayra.local", "+9715000000", "Female", DateOnly.FromDateTime(DateTime.UtcNow.Date.AddYears(-30)), "Married", "Ali Ahmed", "+9715111111", "UAE", "UAE", "People", "HR Officer", "Dubai", "Dubai HQ", null, DateTime.UtcNow.Date, "Unlimited", "G5", "HR-001", DateOnly.FromDateTime(DateTime.UtcNow.Date), DateOnly.FromDateTime(DateTime.UtcNow.Date.AddYears(2)), DateOnly.FromDateTime(DateTime.UtcNow.Date.AddMonths(6)), "MONTHLY", 12000m, "Emirates NBD", "AE000000", "WPS-1", "DAY", "UAE-ANNUAL", "Zayra", DateOnly.FromDateTime(DateTime.UtcNow.Date.AddYears(-1)), "P123", DateOnly.FromDateTime(DateTime.UtcNow.Date.AddYears(5)), DateOnly.FromDateTime(DateTime.UtcNow.Date), "V123", DateOnly.FromDateTime(DateTime.UtcNow.Date.AddYears(2)), null, null, null, null, "784-0000", "LC-1", "VF-1", null, null, null, null, null, null), CancellationToken.None);
        var draft = Assert.IsType<EmployeeDraft>(Assert.IsType<CreatedResult>(draftResult.Result).Value);
        await controller.SubmitDraft(draft.Id, CancellationToken.None);

        var approval = await controller.ApproveDraft(draft.Id, CancellationToken.None);

        var profile = Assert.IsType<EmployeeProfileDto>(Assert.IsType<OkObjectResult>(approval.Result).Value);
        Assert.Equal("Active", profile.Employee.Status);
        Assert.StartsWith("EMP-", profile.Employee.EmployeeCode);
        Assert.NotNull(profile.Employee.UserAccountId);
        Assert.True(await db.EmployeeHistories.AnyAsync(x => x.EmployeeId == profile.Employee.Id && x.EventType == "Activated"));
    }

    [Fact]
    public async Task SensitiveUpdate_CreatesApprovalRequestWithoutChangingEmployee()
    {
        await using var db = CreateDb();
        var tenantId = await SeedTenantAndEmployeeRole(db);
        var employee = new Employee { TenantId = tenantId, EmployeeCode = "EMP-00001", FullName = "Sara Ahmed", Department = "People", Designation = "HR Officer", Status = "Active", JoiningDate = DateTime.UtcNow.Date, Salary = 10000m };
        db.Employees.Add(employee);
        await db.SaveChangesAsync();
        var controller = CreateController(db, tenantId);

        var result = await controller.UpdateEmployee(employee.Id, new EmployeeUpdateRequest(DateOnly.FromDateTime(DateTime.UtcNow.Date), new() { ["salary"] = System.Text.Json.JsonDocument.Parse("15000").RootElement }), CancellationToken.None);

        Assert.IsType<AcceptedResult>(result);
        var unchanged = await db.Employees.FindAsync(employee.Id);
        Assert.Equal(10000m, unchanged!.Salary);
        Assert.True(await db.EmployeeChangeRequests.AnyAsync(x => x.EmployeeId == employee.Id && x.SensitiveFields == "salary"));
    }

    private static ZayraDbContext CreateDb()
    {
        var options = new DbContextOptionsBuilder<ZayraDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options;
        return new ZayraDbContext(options);
    }

    private static async Task<Guid> SeedTenantAndEmployeeRole(ZayraDbContext db)
    {
        var tenant = new Tenant { Id = Guid.NewGuid(), Name = "Zayra HQ", Slug = "zayra" };
        var role = new Role { Id = Guid.NewGuid(), TenantId = tenant.Id, Name = "Employee", NormalizedName = "EMPLOYEE", Description = "Employee" };
        db.Tenants.Add(tenant);
        db.Roles.Add(role);
        await db.SaveChangesAsync();
        return tenant.Id;
    }

    private static EmployeesController CreateController(ZayraDbContext db, Guid tenantId)
    {
        var controller = new EmployeesController(db, new Pbkdf2PasswordHasher(), new AuditService(db), new FakeDocumentStorage(), new Zayra.Api.Infrastructure.Notifications.NotificationService(db), new FakeHijriDateService());
        var userId = Guid.NewGuid();
        var user = new ClaimsPrincipal(new ClaimsIdentity(new[]
        {
            new Claim("tenant_id", tenantId.ToString()),
            new Claim(ClaimTypes.NameIdentifier, userId.ToString()),
            new Claim(ClaimTypes.Role, "Admin")
        }, "Test"));
        controller.ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext { User = user } };
        return controller;
    }
}

file sealed class FakeDocumentStorage : Zayra.Api.Infrastructure.Documents.IDocumentStorage
{
    public Task<Zayra.Api.Infrastructure.Documents.StoredDocument> SaveAsync(Guid tenantId, IFormFile file, CancellationToken cancellationToken) => Task.FromResult(new Zayra.Api.Infrastructure.Documents.StoredDocument(file.FileName, file.ContentType, "storage/documents/test", "/tmp/test"));
    public string ResolvePath(string storageUrl) => "/tmp/test";
}

file sealed class FakeHijriDateService : Zayra.Api.Infrastructure.Localization.IHijriDateService
{
    public Zayra.Api.Infrastructure.Localization.DateConversionDto FromGregorian(DateOnly date) => new(date.ToString("yyyy-MM-dd"), "1447-01-01", 1447, 1, 1);
}
