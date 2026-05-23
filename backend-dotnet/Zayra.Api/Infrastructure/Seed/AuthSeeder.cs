using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Zayra.Api.Application.Auth;
using Zayra.Api.Data;
using Zayra.Api.Domain.Entities;
using Zayra.Api.Infrastructure.Auth;
using Zayra.Api.Models;

namespace Zayra.Api.Infrastructure.Seed;

public class AuthSeeder : IAuthSeeder
{
    private readonly ZayraDbContext _db;
    private readonly IPasswordHasher _passwordHasher;
    private readonly SeedAdminOptions _options;

    public AuthSeeder(ZayraDbContext db, IPasswordHasher passwordHasher, IOptions<SeedAdminOptions> options)
    {
        _db = db;
        _passwordHasher = passwordHasher;
        _options = options.Value;
    }

    public async Task SeedAsync(CancellationToken cancellationToken = default)
    {
        await _db.Database.EnsureCreatedAsync(cancellationToken);

        var tenantSlug = _options.TenantSlug.Trim().ToLowerInvariant();
        var tenant = await _db.Tenants.FirstOrDefaultAsync(x => x.Slug == tenantSlug, cancellationToken);
        if (tenant is null)
        {
            tenant = new Tenant { Name = _options.TenantName.Trim(), Slug = tenantSlug };
            _db.Tenants.Add(tenant);
            await _db.SaveChangesAsync(cancellationToken);
        }

        var permissions = await EnsurePermissions(cancellationToken);
        var adminRole = await EnsureRole(tenant.Id, "Admin", "Tenant administrator", permissions, cancellationToken);
        await EnsureRole(tenant.Id, "HR Manager", "HR operations manager", permissions.Where(x => x.Key.StartsWith("employees.") || x.Key.StartsWith("attendance.") || x.Key.StartsWith("dashboard.") || x.Key.StartsWith("organization.") || x.Key.StartsWith("approvals.") || x.Key.StartsWith("notifications.") || x.Key.StartsWith("localization.")).ToList(), cancellationToken);
        await EnsureRole(tenant.Id, "HR Officer", "HR operations specialist", permissions.Where(x => x.Key is "dashboard.read" or "employees.read" or "employees.write" or "employees.documents" or "employees.templates" or "organization.read" or "approvals.read" or "approvals.write" or "notifications.read" or "localization.read").ToList(), cancellationToken);
        await EnsureRole(tenant.Id, "Payroll Officer", "Payroll and WPS specialist", permissions.Where(x => x.Key is "dashboard.read" or "employees.read" or "employees.sensitive" or "attendance.read" or "notifications.read").ToList(), cancellationToken);
        await EnsureRole(tenant.Id, "Manager", "People manager approver", permissions.Where(x => x.Key is "dashboard.read" or "employees.read" or "approvals.read" or "approvals.decide" or "notifications.read" or "manager.read" or "manager.approve" or "ess.read" or "ess.write").ToList(), cancellationToken);
        await EnsureRole(tenant.Id, "Auditor", "Read-only audit and compliance reviewer", permissions.Where(x => x.Key is "dashboard.read" or "employees.read" or "organization.read" or "approvals.read" or "audit.read").ToList(), cancellationToken);
        await EnsureRole(tenant.Id, "Employee", "Employee self-service user", permissions.Where(x => x.Key is "dashboard.read" or "profile.read" or "ess.read" or "ess.write").ToList(), cancellationToken);

        var normalizedEmail = AuthService.Normalize(_options.Email);
        var admin = await _db.Users
            .Include(x => x.UserRoles)
            .FirstOrDefaultAsync(x => x.TenantId == tenant.Id && x.NormalizedEmail == normalizedEmail, cancellationToken);
        if (admin is null)
        {
            admin = new User
            {
                TenantId = tenant.Id,
                Email = _options.Email.Trim().ToLowerInvariant(),
                NormalizedEmail = normalizedEmail,
                FullName = _options.FullName.Trim(),
                PasswordHash = _passwordHasher.Hash(_options.Password),
                IsActive = true,
                IsEmailConfirmed = true
            };
            admin.UserRoles.Add(new UserRole { User = admin, Role = adminRole });
            _db.Users.Add(admin);
        }
        else if (!admin.UserRoles.Any(x => x.RoleId == adminRole.Id))
        {
            admin.UserRoles.Add(new UserRole { UserId = admin.Id, RoleId = adminRole.Id });
        }

        await _db.SaveChangesAsync(cancellationToken);
        await EnsureFoundationSeedData(tenant.Id, cancellationToken);
    }

    private async Task<List<Permission>> EnsurePermissions(CancellationToken cancellationToken)
    {
        var definitions = new (string Key, string Module, string Description)[]
        {
            ("dashboard.read", "Dashboard", "Read workforce dashboard metrics"),
            ("employees.read", "Employees", "Read employee records"),
            ("employees.write", "Employees", "Create and update employee records"),
            ("employees.sensitive", "Employees", "View and approve sensitive employee fields"),
            ("employees.approve", "Employees", "Approve employee drafts, changes, and transfers"),
            ("employees.documents", "Employees", "Upload and download employee documents"),
            ("employees.templates", "Employees", "Generate localized employee document templates"),
            ("organization.read", "Organization", "Read companies, branches, departments, and designations"),
            ("organization.write", "Organization", "Create and update organization master data"),
            ("approvals.read", "Approvals", "Read approval workflows and approval requests"),
            ("approvals.write", "Approvals", "Create approval workflows and start approval requests"),
            ("approvals.decide", "Approvals", "Approve or reject approval requests"),
            ("notifications.read", "Notifications", "Read workflow notifications"),
            ("localization.read", "Localization", "Read localized calendar conversions"),
            ("attendance.read", "Attendance", "Read attendance records"),
            ("attendance.write", "Attendance", "Create and update attendance records"),
            ("attendance.kiosk", "Attendance", "Use kiosk-only attendance capture"),
            ("ess.read", "ESS", "Read own employee self-service records"),
            ("ess.write", "ESS", "Create employee self-service requests"),
            ("manager.read", "Manager", "Read direct and indirect team records"),
            ("manager.approve", "Manager", "Approve assigned team requests"),
            ("roles.manage", "Access", "Manage user roles"),
            ("users.manage", "Access", "Manage users"),
            ("audit.read", "Audit", "Read audit logs"),
            ("profile.read", "Profile", "Read own profile")
        };

        foreach (var definition in definitions)
        {
            if (!await _db.Permissions.AnyAsync(x => x.Key == definition.Key, cancellationToken))
            {
                _db.Permissions.Add(new Permission { Key = definition.Key, Module = definition.Module, Description = definition.Description });
            }
        }
        await _db.SaveChangesAsync(cancellationToken);
        return await _db.Permissions.ToListAsync(cancellationToken);
    }

    private async Task<Role> EnsureRole(Guid tenantId, string name, string description, IReadOnlyCollection<Permission> permissions, CancellationToken cancellationToken)
    {
        var normalized = AuthService.Normalize(name);
        var role = await _db.Roles
            .Include(x => x.RolePermissions)
            .FirstOrDefaultAsync(x => x.TenantId == tenantId && x.NormalizedName == normalized, cancellationToken);
        if (role is null)
        {
            role = new Role { TenantId = tenantId, Name = name, NormalizedName = normalized, Description = description, IsSystem = true };
            _db.Roles.Add(role);
            await _db.SaveChangesAsync(cancellationToken);
        }

        foreach (var permission in permissions)
        {
            if (!role.RolePermissions.Any(x => x.PermissionId == permission.Id))
            {
                role.RolePermissions.Add(new RolePermission { RoleId = role.Id, PermissionId = permission.Id });
            }
        }
        await _db.SaveChangesAsync(cancellationToken);
        return role;
    }

    private async Task EnsureFoundationSeedData(Guid tenantId, CancellationToken cancellationToken)
    {
        var company = await _db.Companies.FirstOrDefaultAsync(x => x.TenantId == tenantId && x.RegistrationNumber == "ZAYRA-DEMO", cancellationToken);
        if (company is null)
        {
            company = new Company
            {
                TenantId = tenantId,
                LegalNameEn = "Zayra Workforce AI",
                LegalNameAr = "زيرا لإدارة القوى العاملة",
                TradeName = "Zayra",
                CountryCode = "UAE",
                RegistrationNumber = "ZAYRA-DEMO",
                DefaultCurrency = "AED"
            };
            _db.Companies.Add(company);
            await _db.SaveChangesAsync(cancellationToken);
        }

        var branch = await _db.Branches.FirstOrDefaultAsync(x => x.TenantId == tenantId && x.Code == "DXB-HQ", cancellationToken);
        if (branch is null)
        {
            branch = new Branch
            {
                TenantId = tenantId,
                CompanyId = company.Id,
                Code = "DXB-HQ",
                NameEn = "Dubai Headquarters",
                NameAr = "المقر الرئيسي دبي",
                CountryCode = "UAE",
                City = "Dubai",
                AddressLine1 = "Business Bay",
                TimeZoneId = "Asia/Dubai",
                IsHeadOffice = true
            };
            _db.Branches.Add(branch);
            await _db.SaveChangesAsync(cancellationToken);
        }

        var department = await _db.Departments.FirstOrDefaultAsync(x => x.TenantId == tenantId && x.Code == "HR", cancellationToken);
        if (department is null)
        {
            department = new Department
            {
                TenantId = tenantId,
                BranchId = branch.Id,
                Code = "HR",
                NameEn = "Human Resources",
                NameAr = "الموارد البشرية"
            };
            _db.Departments.Add(department);
            await _db.SaveChangesAsync(cancellationToken);
        }

        var grade = await _db.Grades.FirstOrDefaultAsync(x => x.TenantId == tenantId && x.Code == "G5", cancellationToken);
        if (grade is null)
        {
            grade = new Grade
            {
                TenantId = tenantId,
                Code = "G5",
                Name = "Professional Grade 5",
                Band = "Professional",
                Level = 5
            };
            _db.Grades.Add(grade);
            await _db.SaveChangesAsync(cancellationToken);
        }

        var costCenter = await _db.CostCenters.FirstOrDefaultAsync(x => x.TenantId == tenantId && x.Code == "HR-OPS", cancellationToken);
        if (costCenter is null)
        {
            costCenter = new CostCenter
            {
                TenantId = tenantId,
                CompanyId = company.Id,
                Code = "HR-OPS",
                Name = "HR Operations"
            };
            _db.CostCenters.Add(costCenter);
            await _db.SaveChangesAsync(cancellationToken);
        }

        if (department.CostCenterId is null)
        {
            department.CostCenterId = costCenter.Id;
            department.UpdatedAtUtc = DateTime.UtcNow;
        }

        if (!await _db.Designations.AnyAsync(x => x.TenantId == tenantId && x.Code == "HR-OFFICER", cancellationToken))
        {
            _db.Designations.Add(new Designation
            {
                TenantId = tenantId,
                DepartmentId = department.Id,
                GradeId = grade.Id,
                Code = "HR-OFFICER",
                TitleEn = "HR Officer",
                TitleAr = "مسؤول الموارد البشرية",
                JobGrade = "G5",
                JobLevel = "Officer",
                JobDescription = "Supports employee lifecycle administration, records, and compliance workflows."
            });
        }

        if (!await _db.EmployeeIdRules.AnyAsync(x => x.TenantId == tenantId && x.CompanyId == company.Id && x.IsActive && !x.IsDeleted, cancellationToken))
        {
            _db.EmployeeIdRules.Add(new EmployeeIdRule
            {
                TenantId = tenantId,
                CompanyId = company.Id,
                Name = "Default GCC employee ID rule",
                CompanyPrefix = "ZAY",
                UseCountryPrefix = true,
                UseBranchPrefix = false,
                UseDepartmentPrefix = true,
                UseYear = true,
                PaddingLength = 4,
                NextSequence = 1,
                AllowManualOverride = false
            });
        }

        if (!await _db.AttendancePolicies.AnyAsync(x => x.TenantId == tenantId && x.Code == "DEFAULT", cancellationToken))
        {
            _db.AttendancePolicies.Add(new AttendancePolicy
            {
                TenantId = tenantId,
                Code = "DEFAULT",
                Name = "Default attendance policy",
                GraceMinutes = 10,
                LateThresholdMinutes = 15,
                EarlyExitThresholdMinutes = 15,
                HalfDayThresholdMinutes = 240,
                AbsentThresholdMinutes = 120,
                StandardWorkMinutes = 480,
                BreakMinutes = 60,
                RequiresOvertimeApproval = true,
                AllowAbsenceToLeaveConversion = true
            });
        }

        if (!await _db.ApprovalWorkflows.AnyAsync(x => x.TenantId == tenantId && x.Code == "EMPLOYEE-ONBOARDING", cancellationToken))
        {
            var onboarding = new ApprovalWorkflow
            {
                TenantId = tenantId,
                Code = "EMPLOYEE-ONBOARDING",
                Name = "Employee Onboarding Approval",
                EntityName = "EmployeeDraft"
            };
            onboarding.Steps.Add(new ApprovalWorkflowStep { TenantId = tenantId, WorkflowId = onboarding.Id, StepOrder = 1, StepName = "HR Review", ApproverRole = "HR Manager", IsFinalStep = true });
            _db.ApprovalWorkflows.Add(onboarding);
        }

        if (!await _db.ApprovalWorkflows.AnyAsync(x => x.TenantId == tenantId && x.Code == "EMPLOYEE-TRANSFER", cancellationToken))
        {
            var transfer = new ApprovalWorkflow
            {
                TenantId = tenantId,
                Code = "EMPLOYEE-TRANSFER",
                Name = "Employee Transfer Approval",
                EntityName = "EmployeeTransferRequest"
            };
            transfer.Steps.Add(new ApprovalWorkflowStep { TenantId = tenantId, WorkflowId = transfer.Id, StepOrder = 1, StepName = "Current Manager Approval", ApproverRole = "Manager" });
            transfer.Steps.Add(new ApprovalWorkflowStep { TenantId = tenantId, WorkflowId = transfer.Id, StepOrder = 2, StepName = "HR Approval", ApproverRole = "HR Manager", IsFinalStep = true });
            _db.ApprovalWorkflows.Add(transfer);
        }

        await _db.SaveChangesAsync(cancellationToken);
    }
}
