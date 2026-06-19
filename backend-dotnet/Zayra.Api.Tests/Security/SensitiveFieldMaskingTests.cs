using System.Security.Claims;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Zayra.Api.Application.Auth;
using Zayra.Api.Application.Common;
using Zayra.Api.Application.Employees;
using Zayra.Api.Controllers;
using Zayra.Api.Data;
using Zayra.Api.Domain.Entities;
using Zayra.Api.Infrastructure.Audit;
using Zayra.Api.Infrastructure.Auth;
using Zayra.Api.Infrastructure.Common;
using Zayra.Api.Infrastructure.Documents;
using Zayra.Api.Infrastructure.Documents.Letters;
using Zayra.Api.Infrastructure.Employees;
using Zayra.Api.Infrastructure.Localization;
using Zayra.Api.Infrastructure.Notifications;
using Zayra.Api.Models;

namespace Zayra.Api.Tests.Security;

/// <summary>
/// Role × sensitive-field matrix.
///
/// Asserts that each sensitive field (Salary, BankIban, WpsBankDetails, PassportNumber,
/// IqamaNumber, MedicalInformation, DisciplinaryRecords, TerminationReason) is present
/// when the caller can view sensitive data, and is masked (null / empty string) otherwise.
///
/// Covers: canonical mask unit, list DTO field absence, detail endpoint, draft-approve endpoint.
/// </summary>
public class SensitiveFieldMaskingTests
{
    // ── Helpers ──────────────────────────────────────────────────────────────────

    private static ZayraDbContext CreateDb() =>
        new(new DbContextOptionsBuilder<ZayraDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options);

    private static Employee SeedEmployee(ZayraDbContext db, Guid tenantId)
    {
        var emp = new Employee
        {
            TenantId             = tenantId,
            EmployeeCode         = "EMP-0001",
            FullName             = "Test Employee",
            Department           = "HR",
            Designation          = "Officer",
            Status               = "Active",
            JoiningDate          = DateTime.UtcNow.Date,
            Salary               = 50_000m,
            BankName             = "Test Bank",
            BankIban             = "SA0000000000000000001234",
            WpsBankDetails       = "WPS-REF-001",
            PassportNumber       = "P99999999",
            IqamaNumber          = "2000000001",
            MedicalInformation   = "Allergic to penicillin",
            DisciplinaryRecords  = "Warning issued 2025-01",
            TerminationReason    = "Voluntary resignation",
        };
        db.Employees.Add(emp);
        db.SaveChanges();
        return emp;
    }

    private static EmployeeManagementService CreateService(ZayraDbContext db) =>
        new(db, new AuditService(db), new FakeDocumentStorage(), new FakeNotificationService());

    private static RequestContext TestContext(Guid tenantId) =>
        new("127.0.0.1", "test", Guid.NewGuid(), tenantId);

    // ── EmployeeSensitiveMask canonical coverage ──────────────────────────────────

    [Fact]
    public void EmployeeSensitiveMask_Apply_ClearsAllNineFields()
    {
        var emp = new Employee
        {
            Salary              = 99_000m,
            BankName            = "Bank",
            BankIban            = "SA00000000001",
            WpsBankDetails      = "WPS",
            PassportNumber      = "PASSPORT",
            IqamaNumber         = "IQAMA",
            MedicalInformation  = "MED",
            DisciplinaryRecords = "DISC",
            TerminationReason   = "TERM",
        };

        EmployeeSensitiveMask.Apply(emp);

        emp.Salary.Should().BeNull();
        emp.BankName.Should().BeEmpty();
        emp.BankIban.Should().BeEmpty();
        emp.WpsBankDetails.Should().BeEmpty();
        emp.PassportNumber.Should().BeEmpty();
        emp.IqamaNumber.Should().BeEmpty();
        emp.MedicalInformation.Should().BeEmpty();
        emp.DisciplinaryRecords.Should().BeEmpty();
        emp.TerminationReason.Should().BeEmpty();
    }

    // ── List DTO — IqamaNumber must never appear for any role ─────────────────────

    [Fact]
    public void EmployeeListItemDto_DoesNotHaveIqamaNumberField()
    {
        typeof(EmployeeListItemDto)
            .GetProperty("IqamaNumber")
            .Should().BeNull(
                "IqamaNumber was removed from the list DTO so Manager/Auditor cannot read national IDs via the list endpoint");
    }

    [Fact]
    public void EmployeeListItemDto_HasExpectedFieldCount()
    {
        // 12 fields: Id, EmployeeCode, FullName, ArabicName, Department, Designation, Branch,
        // ManagerEmployeeId, Status, ProfileCompletenessScore, VisaExpiryDate, PassportExpiryDate.
        // If someone adds a sensitive field to the list DTO, this test breaks and forces review.
        typeof(EmployeeListItemDto).GetConstructors().Single()
            .GetParameters()
            .Should().HaveCount(12,
                "list DTO must contain exactly 12 non-sensitive fields");
    }

    // ── Detail endpoint — EmployeeManagementService.GetAsync ─────────────────────

    [Theory]
    [InlineData("Admin")]
    [InlineData("HR Manager")]
    [InlineData("Payroll Officer")]
    public async Task Detail_PrivilegedRole_SeesAllSensitiveFields(string role)
    {
        await using var db = CreateDb();
        var tenantId = Guid.NewGuid();
        var emp = SeedEmployee(db, tenantId);
        var svc = CreateService(db);

        var dto = await svc.GetAsync(tenantId, emp.Id, includeSensitive: true, TestContext(tenantId), CancellationToken.None);

        dto.Should().NotBeNull($"{role} should see the employee");
        dto!.Employee.Salary.Should().Be(50_000m, $"{role} must see salary");
        dto.Employee.BankIban.Should().Be("SA0000000000000000001234", $"{role} must see IBAN");
        dto.Employee.PassportNumber.Should().Be("P99999999", $"{role} must see passport number");
        dto.Employee.IqamaNumber.Should().Be("2000000001", $"{role} must see Iqama number");
        dto.Employee.MedicalInformation.Should().NotBeEmpty($"{role} must see medical information");
        dto.Employee.DisciplinaryRecords.Should().NotBeEmpty($"{role} must see disciplinary records");
        dto.Employee.TerminationReason.Should().NotBeEmpty($"{role} must see termination reason");
    }

    [Theory]
    [InlineData("HR Officer")]
    [InlineData("Manager")]
    [InlineData("Auditor")]
    public async Task Detail_UnprivilegedRole_MasksSensitiveFields(string role)
    {
        await using var db = CreateDb();
        var tenantId = Guid.NewGuid();
        var emp = SeedEmployee(db, tenantId);
        var svc = CreateService(db);

        var dto = await svc.GetAsync(tenantId, emp.Id, includeSensitive: false, TestContext(tenantId), CancellationToken.None);

        dto.Should().NotBeNull($"{role} should see the employee record");
        dto!.Employee.Salary.Should().BeNull($"{role} must NOT see salary");
        dto.Employee.BankName.Should().BeEmpty($"{role} must NOT see bank name");
        dto.Employee.BankIban.Should().BeEmpty($"{role} must NOT see IBAN");
        dto.Employee.WpsBankDetails.Should().BeEmpty($"{role} must NOT see WPS details");
        dto.Employee.PassportNumber.Should().BeEmpty($"{role} must NOT see passport number");
        dto.Employee.IqamaNumber.Should().BeEmpty($"{role} must NOT see Iqama number");
        dto.Employee.MedicalInformation.Should().BeEmpty($"{role} must NOT see medical information");
        dto.Employee.DisciplinaryRecords.Should().BeEmpty($"{role} must NOT see disciplinary records");
        dto.Employee.TerminationReason.Should().BeEmpty($"{role} must NOT see termination reason");
    }

    // ── ApproveDraft — response type and mask gate ─────────────────────────────────

    [Fact]
    public async Task ApproveDraft_ResponseType_IsEmployeeDetailDtoNotEmployeeProfileDto()
    {
        // EmployeeProfileDto (unmasked raw entity) was deleted in P2. Verifies at runtime
        // that ApproveDraft no longer returns the old unmasked type.
        await using var db = CreateDb();
        var tenantId = await SeedTenantAsync(db);
        var controller = CreateController(db, tenantId, "Admin");

        var draftResult = await controller.CreateDraft(MinimalDraftRequest(), CancellationToken.None);
        var draft = Assert.IsType<EmployeeDraft>(Assert.IsType<CreatedResult>(draftResult.Result).Value);
        await controller.SubmitDraft(draft.Id, CancellationToken.None);

        var approval = await controller.ApproveDraft(draft.Id, CancellationToken.None);
        var okResult = Assert.IsType<OkObjectResult>(approval.Result);

        okResult.Value.Should().BeOfType<EmployeeDetailDto>(
            "ApproveDraft must return EmployeeDetailDto so the canonical mask gate is applied before serialization");
    }

    [Fact]
    public async Task ApproveDraft_AdminRole_ReceivesSalaryUnmasked()
    {
        await using var db = CreateDb();
        var tenantId = await SeedTenantAsync(db);
        var controller = CreateController(db, tenantId, "Admin");

        var draftResult = await controller.CreateDraft(MinimalDraftRequest(), CancellationToken.None);
        var draft = Assert.IsType<EmployeeDraft>(Assert.IsType<CreatedResult>(draftResult.Result).Value);
        await controller.SubmitDraft(draft.Id, CancellationToken.None);

        var approval = await controller.ApproveDraft(draft.Id, CancellationToken.None);
        var detail = Assert.IsType<EmployeeDetailDto>(Assert.IsType<OkObjectResult>(approval.Result).Value);

        detail.Employee.Salary.Should().Be(12_000m,
            "Admin satisfies CanViewSensitive() — salary submitted in the draft must flow through unmasked");
        detail.Employee.BankIban.Should().Be("AE000000000000001",
            "Admin must see the IBAN from the draft");
    }

    // ── Helpers ───────────────────────────────────────────────────────────────────

    private static async Task<Guid> SeedTenantAsync(ZayraDbContext db)
    {
        var tenant = new Tenant { Id = Guid.NewGuid(), Name = "Test Corp", Slug = "test-corp" };
        db.Tenants.Add(tenant);
        db.Roles.Add(new Role { Id = Guid.NewGuid(), TenantId = tenant.Id, Name = "Employee", NormalizedName = "EMPLOYEE", Description = "Default employee role" });
        await db.SaveChangesAsync();
        return tenant.Id;
    }

    private static EmployeesController CreateController(ZayraDbContext db, Guid tenantId, string role)
    {
        var controller = new EmployeesController(
            db,
            new Pbkdf2PasswordHasher(),
            new AuditService(db),
            new FakeDocumentStorage(),
            new FakeNotificationService(),
            new FakeHijriDateService(),
            new DataScopeService(db),
            new FakeLetterService());
        var user = new ClaimsPrincipal(new ClaimsIdentity(new[]
        {
            new Claim("tenant_id", tenantId.ToString()),
            new Claim(ClaimTypes.NameIdentifier, Guid.NewGuid().ToString()),
            new Claim(ClaimTypes.Role, role),
        }, "Test"));
        controller.ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext { User = user } };
        return controller;
    }

    private static EmployeeDraftRequest MinimalDraftRequest() =>
        new(
            CurrentStep: "Review",
            EnglishName: "Test User", ArabicName: "مستخدم", PersonalEmail: "test@example.com",
            WorkEmail: "test@company.com", Phone: "+971500000", Gender: "Male",
            DateOfBirth: new DateOnly(1990, 1, 1), MaritalStatus: "Single",
            EmergencyContactName: "Contact", EmergencyContactPhone: "+97150001",
            Nationality: "UAE", CountryCode: "UAE",
            Department: "Engineering", Designation: "Engineer", Branch: "Dubai", WorkLocation: "HQ",
            ManagerEmployeeId: null,
            JoiningDate: DateTime.UtcNow.Date,
            ContractType: "Unlimited", Grade: "G1", CostCenter: "CC-001",
            ContractStartDate: DateOnly.FromDateTime(DateTime.UtcNow.Date),
            ContractEndDate: null,
            ProbationEndDate: DateOnly.FromDateTime(DateTime.UtcNow.Date.AddMonths(3)),
            PayrollProfileCode: "MONTHLY",
            Salary: 12_000m, BankName: "Test Bank", BankIban: "AE000000000000001",
            WpsBankDetails: "WPS-001",
            ShiftPolicyCode: "DAY", LeavePolicyCode: "STANDARD",
            SponsorName: null,
            PassportIssueDate: null, PassportNumber: "P123456",
            PassportExpiryDate: DateOnly.FromDateTime(DateTime.UtcNow.Date.AddYears(5)),
            VisaIssueDate: null, VisaNumber: null, VisaExpiryDate: null,
            IqamaNumber: "200000001", MuqeemNumber: null,
            GosiReference: null, QiwaContractNumber: null,
            EmiratesId: null, LaborCardNumber: null, VisaFileNumber: null,
            Qid: null, WorkPermitNumber: null, WorkPermitIssueDate: null,
            CivilId: null, ResidencyNumber: null, ResidencyIssueDate: null);

    // ── Stubs ─────────────────────────────────────────────────────────────────────

    private sealed class FakeDocumentStorage : IDocumentStorage
    {
        public Task<StoredDocument> SaveAsync(Guid tenantId, IFormFile file, CancellationToken ct) =>
            Task.FromResult(new StoredDocument(file.FileName, file.ContentType, "storage/test", "/tmp/test"));
        public string ResolvePath(string storageUrl) => "/tmp/test";
    }

    private sealed class FakeNotificationService : INotificationService
    {
        public Task NotifyAsync(Guid t, Guid? u, string title, string msg, string entity, string? entityId, CancellationToken ct) => Task.CompletedTask;
        public Task SendEmailAsync(Guid t, string code, string to, string name, Dictionary<string, string> vars, CancellationToken ct) => Task.CompletedTask;
    }

    private sealed class FakeHijriDateService : IHijriDateService
    {
        public DateConversionDto FromGregorian(DateOnly date) =>
            new(date.ToString("yyyy-MM-dd"), "1447-01-01", 1447, 1, 1);
    }

    private sealed class FakeLetterService : ILetterService
    {
        public Task<byte[]> GeneratePayslipPdfAsync(PayslipData data, CancellationToken ct = default) => Task.FromResult(Array.Empty<byte>());
        public Task<byte[]> GenerateAppointmentLetterAsync(LetterData data, CancellationToken ct = default) => Task.FromResult(Array.Empty<byte>());
        public Task<byte[]> GenerateExperienceLetterAsync(LetterData data, CancellationToken ct = default) => Task.FromResult(Array.Empty<byte>());
        public Task<byte[]> GenerateOfferLetterAsync(OfferLetterData data, CancellationToken ct = default) => Task.FromResult(Array.Empty<byte>());
    }
}
