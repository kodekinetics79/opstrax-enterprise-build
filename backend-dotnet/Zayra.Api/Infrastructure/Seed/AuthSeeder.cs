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
        var Ps = (string[] keys) => permissions.Where(x => keys.Contains(x.Key)).ToList();

        // Level 1 — Admin: all permissions
        var adminRole = await EnsureRole(tenant.Id, "Admin", "Tenant system administrator with full access", permissions, 1, false, cancellationToken);

        // Level 2 — HR Director: full HR + payroll visibility + reports + compliance
        await EnsureRole(tenant.Id, "HR Director", "Senior HR leader with strategic visibility", permissions.Where(x =>
            x.Key.StartsWith("employees.") || x.Key.StartsWith("attendance.") || x.Key.StartsWith("leave.") ||
            x.Key.StartsWith("overtime.") || x.Key.StartsWith("dashboard.") || x.Key.StartsWith("organization.") ||
            x.Key.StartsWith("approvals.") || x.Key.StartsWith("notifications.") || x.Key.StartsWith("localization.") ||
            x.Key.StartsWith("performance.") || x.Key.StartsWith("compliance.") || x.Key.StartsWith("reports.") ||
            x.Key.StartsWith("recruitment.") || x.Key is "payroll.read" or "loans.read" or "audit.read" or
            "roles.manage" or "users.manage" or "manager.read" or "manager.approve"
        ).ToList(), 2, true, cancellationToken);

        // Level 3 — HR Manager: operational HR management
        await EnsureRole(tenant.Id, "HR Manager", "HR operations manager", permissions.Where(x =>
            x.Key.StartsWith("employees.") || x.Key.StartsWith("attendance.") || x.Key.StartsWith("leave.") ||
            x.Key.StartsWith("overtime.") || x.Key.StartsWith("dashboard.") || x.Key.StartsWith("organization.") ||
            x.Key.StartsWith("approvals.") || x.Key.StartsWith("notifications.") || x.Key.StartsWith("localization.") ||
            x.Key is "audit.read" or "manager.read" or "manager.approve" or "reports.read"
        ).ToList(), 3, true, cancellationToken);

        // Level 4 — Payroll Manager: payroll + finance + employees
        await EnsureRole(tenant.Id, "Payroll Manager", "Manages payroll processing and WPS submissions", Ps(new[] {
            "dashboard.read", "employees.read", "employees.sensitive", "attendance.read", "leave.read",
            "overtime.read", "payroll.read", "payroll.write", "payroll.approve", "loans.read", "loans.approve",
            "approvals.read", "approvals.decide", "reports.read", "notifications.read"
        }), 4, true, cancellationToken);

        // Level 5 — HR Officer: HR operations specialist
        await EnsureRole(tenant.Id, "HR Officer", "HR operations specialist", Ps(new[] {
            "dashboard.read", "employees.read", "employees.write", "employees.documents", "employees.templates",
            "organization.read", "approvals.read", "approvals.write", "notifications.read", "localization.read",
            "leave.read", "leave.write", "attendance.read", "overtime.read", "profile.read"
        }), 5, true, cancellationToken);

        // Level 6 — Payroll Officer: payroll processing
        await EnsureRole(tenant.Id, "Payroll Officer", "Payroll and WPS specialist", Ps(new[] {
            "dashboard.read", "employees.read", "employees.sensitive", "attendance.read",
            "payroll.read", "payroll.write", "loans.read", "notifications.read", "reports.read"
        }), 6, true, cancellationToken);

        // Level 7 — Finance Approver: finance approvals
        await EnsureRole(tenant.Id, "Finance Approver", "Finance approver for loans, advances and payroll", Ps(new[] {
            "dashboard.read", "employees.read", "payroll.read", "payroll.approve",
            "loans.read", "loans.approve", "approvals.read", "approvals.decide"
        }), 7, true, cancellationToken);

        // Level 8 — Compliance Officer: compliance and contracts
        await EnsureRole(tenant.Id, "Compliance Officer", "Manages compliance, contracts and regulatory records", Ps(new[] {
            "dashboard.read", "employees.read", "employees.documents", "organization.read",
            "compliance.read", "compliance.write", "approvals.read", "audit.read", "reports.read", "notifications.read"
        }), 8, true, cancellationToken);

        // Level 9 — Manager: team management and approvals
        await EnsureRole(tenant.Id, "Manager", "People manager with team oversight and approval authority", Ps(new[] {
            "dashboard.read", "employees.read", "approvals.read", "approvals.decide", "notifications.read",
            "manager.read", "manager.approve", "ess.read", "ess.write", "leave.read", "leave.approve",
            "attendance.read", "overtime.read", "overtime.approve", "profile.read"
        }), 9, true, cancellationToken);

        // Level 10 — Supervisor: front-line supervision
        await EnsureRole(tenant.Id, "Supervisor", "Front-line supervisor for operational staff", Ps(new[] {
            "dashboard.read", "employees.read", "attendance.read", "attendance.write",
            "manager.read", "manager.approve", "leave.read", "overtime.read", "ess.read", "ess.write", "profile.read"
        }), 10, true, cancellationToken);

        // Level 11 — Recruiter: talent acquisition
        await EnsureRole(tenant.Id, "Recruiter", "Recruitment and hiring specialist", Ps(new[] {
            "dashboard.read", "employees.read", "recruitment.read", "recruitment.write",
            "notifications.read", "organization.read", "profile.read"
        }), 11, true, cancellationToken);

        // Level 12 — HR Assistant: limited HR support
        await EnsureRole(tenant.Id, "HR Assistant", "Junior HR support with limited write access", Ps(new[] {
            "dashboard.read", "employees.read", "organization.read", "notifications.read",
            "attendance.read", "leave.read", "ess.read", "profile.read", "localization.read"
        }), 12, true, cancellationToken);

        // Level 13 — Auditor: read-only audit
        await EnsureRole(tenant.Id, "Auditor", "Read-only audit and compliance reviewer", Ps(new[] {
            "dashboard.read", "employees.read", "organization.read", "approvals.read",
            "audit.read", "payroll.read", "attendance.read", "leave.read", "compliance.read", "reports.read"
        }), 13, true, cancellationToken);

        // Level 14 — Kiosk Operator: attendance kiosk only
        await EnsureRole(tenant.Id, "Kiosk Operator", "Restricted to kiosk attendance capture only", Ps(new[] {
            "attendance.kiosk"
        }), 14, true, cancellationToken);

        // Level 15 — Employee: self-service only
        await EnsureRole(tenant.Id, "Employee", "Employee self-service user", Ps(new[] {
            "dashboard.read", "profile.read", "ess.read", "ess.write"
        }), 15, true, cancellationToken);

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
                AccessMode = "FullPortal",
                Status = "Active",
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

        // Sample organisation/business data is seeded only when explicitly enabled
        // (SeedAdmin:SeedDemoData). Production tenants start clean — tenant, roles,
        // permissions and the admin account above are always seeded; nothing else.
        if (_options.SeedDemoData)
        {
            await EnsureFoundationSeedData(tenant.Id, cancellationToken);
        }
    }

    private async Task<List<Permission>> EnsurePermissions(CancellationToken cancellationToken)
    {
        var definitions = new (string Key, string Module, string Description)[]
        {
            // Dashboard
            ("dashboard.read", "Dashboard", "Read workforce dashboard metrics"),
            ("dashboard.export", "Dashboard", "Export dashboard data"),
            // Employees
            ("employees.read", "Employees", "Read employee records"),
            ("employees.write", "Employees", "Create and update employee records"),
            ("employees.delete", "Employees", "Delete/archive employee records"),
            ("employees.sensitive", "Employees", "View sensitive employee fields (salary, NID, passport)"),
            ("employees.approve", "Employees", "Approve employee drafts, changes, and transfers"),
            ("employees.documents", "Employees", "Upload and download employee documents"),
            ("employees.templates", "Employees", "Generate localized employee document templates"),
            ("employees.bulk_import", "Employees", "Bulk import employee records"),
            // Profile
            ("profile.read", "Profile", "Read own profile"),
            ("profile.write", "Profile", "Update own profile"),
            // Organization
            ("organization.read", "Organization", "Read companies, branches, departments, and designations"),
            ("organization.write", "Organization", "Create and update organization master data"),
            ("organization.delete", "Organization", "Delete organization master data"),
            // Attendance
            ("attendance.read", "Attendance", "Read attendance records"),
            ("attendance.write", "Attendance", "Create and update attendance records"),
            ("attendance.delete", "Attendance", "Delete or cancel attendance records"),
            ("attendance.kiosk", "Attendance", "Use kiosk-only attendance capture"),
            ("attendance.bulk_import", "Attendance", "Bulk import attendance data"),
            ("attendance.lock", "Attendance", "Lock/unlock attendance periods"),
            // Leave
            ("leave.read", "Leave", "Read leave requests and balances"),
            ("leave.write", "Leave", "Submit and manage leave requests"),
            ("leave.approve", "Leave", "Approve or reject leave requests"),
            ("leave.cancel", "Leave", "Cancel approved leave"),
            ("leave.policy_manage", "Leave", "Manage leave types and policies"),
            // Overtime
            ("overtime.read", "Overtime", "Read overtime requests"),
            ("overtime.write", "Overtime", "Submit overtime requests"),
            ("overtime.approve", "Overtime", "Approve or reject overtime requests"),
            ("overtime.policy_manage", "Overtime", "Manage overtime types and policies"),
            // Payroll
            ("payroll.read", "Payroll", "Read payroll runs and slips"),
            ("payroll.write", "Payroll", "Create and process payroll runs"),
            ("payroll.approve", "Payroll", "Approve payroll runs"),
            ("payroll.export", "Payroll", "Export payroll and WPS files"),
            ("payroll.structure_manage", "Payroll", "Manage salary structures and components"),
            // Loans & Advances
            ("loans.read", "Loans", "Read loan and advance records"),
            ("loans.write", "Loans", "Create loan and advance applications"),
            ("loans.approve", "Loans", "Approve or reject loans and advances"),
            ("loans.policy_manage", "Loans", "Manage loan types and policies"),
            // Recruitment
            ("recruitment.read", "Recruitment", "Read job openings and applications"),
            ("recruitment.write", "Recruitment", "Manage recruitment pipeline"),
            ("recruitment.approve", "Recruitment", "Approve requisitions and offers"),
            ("recruitment.delete", "Recruitment", "Delete recruitment records"),
            // Performance
            ("performance.read", "Performance", "Read appraisal and performance data"),
            ("performance.write", "Performance", "Create and update performance reviews"),
            ("performance.approve", "Performance", "Approve performance ratings and recommendations"),
            ("performance.cycle_manage", "Performance", "Manage performance cycles and templates"),
            // Compliance
            ("compliance.read", "Compliance", "Read compliance and contract records"),
            ("compliance.write", "Compliance", "Manage compliance documents"),
            ("compliance.approve", "Compliance", "Approve compliance items"),
            // Manager
            ("manager.read", "Manager", "Read direct and indirect team records"),
            ("manager.approve", "Manager", "Approve assigned team requests"),
            // ESS
            ("ess.read", "ESS", "Read own employee self-service records"),
            ("ess.write", "ESS", "Create employee self-service requests"),
            // Approvals
            ("approvals.read", "Approvals", "Read approval workflows and approval requests"),
            ("approvals.write", "Approvals", "Create approval workflows and start approval requests"),
            ("approvals.decide", "Approvals", "Approve or reject approval requests"),
            ("approvals.manage", "Approvals", "Manage approval workflow definitions"),
            // Reports
            ("reports.read", "Reports", "Run and view reports"),
            ("reports.schedule", "Reports", "Create scheduled reports"),
            ("reports.export", "Reports", "Export reports to Excel/PDF"),
            // Notifications
            ("notifications.read", "Notifications", "Read workflow notifications"),
            ("notifications.manage", "Notifications", "Manage notification templates"),
            // Localization
            ("localization.read", "Localization", "Read localized calendar conversions"),
            ("localization.manage", "Localization", "Manage localization and calendar settings"),
            // Access Control
            ("roles.manage", "Access", "Manage user roles and permissions"),
            ("users.manage", "Access", "Manage users and user accounts"),
            ("security.manage", "Security", "Manage security settings and access policies"),
            // Audit
            ("audit.read", "Audit", "Read audit logs"),
            ("audit.export", "Audit", "Export audit logs"),
            // AI & Intelligence
            ("ai.query", "AI", "Query the AI HR assistant"),
            ("ai.insights_view", "AI", "View AI-generated workforce insights"),
            // Shifts
            ("shifts.read", "Shifts", "Read shift schedules and rosters"),
            ("shifts.write", "Shifts", "Create and update shift schedules"),
            ("shifts.manage", "Shifts", "Manage shift definitions and policies"),
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

    private async Task<Role> EnsureRole(Guid tenantId, string name, string description, IReadOnlyCollection<Permission> permissions, int authorityLevel, bool isEditable, CancellationToken cancellationToken)
    {
        var normalized = AuthService.Normalize(name);
        var role = await _db.Roles
            .Include(x => x.RolePermissions)
            .FirstOrDefaultAsync(x => x.TenantId == tenantId && x.NormalizedName == normalized, cancellationToken);
        if (role is null)
        {
            role = new Role
            {
                TenantId = tenantId, Name = name, NormalizedName = normalized, Description = description,
                IsSystem = true, AuthorityLevel = authorityLevel, IsActive = true, IsEditable = isEditable
            };
            _db.Roles.Add(role);
            await _db.SaveChangesAsync(cancellationToken);
        }
        else
        {
            role.AuthorityLevel = authorityLevel;
            role.IsEditable = isEditable;
            role.Description = description;
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
