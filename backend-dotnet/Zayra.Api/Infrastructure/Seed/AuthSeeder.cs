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

        // Global labour-law / payroll rule packs for major countries — seeded for every
        // tenant as configurable reference data (not demo). Admins can edit, override,
        // or import additional country packs. This keeps the platform global, not UAE-only.
        try { await EnsureGlobalCountryRules(tenant.Id, cancellationToken); }
        catch (Exception ex) { Console.WriteLine($"[Seed] Country rule packs skipped: {ex.Message}"); }

        // Sample organisation/business data is seeded only when explicitly enabled
        // (SeedAdmin:SeedDemoData). Production tenants start clean — tenant, roles,
        // permissions and the admin account above are always seeded; nothing else.
        if (_options.SeedDemoData)
        {
            await EnsureFoundationSeedData(tenant.Id, cancellationToken);
        }
    }

    private async Task EnsureGlobalCountryRules(Guid tenantId, CancellationToken ct)
    {
        if (await _db.CountryPayrollRules.AnyAsync(x => x.TenantId == tenantId, ct)) return;

        // (CountryCode, Currency, Weekend, AnnualLeaveDays, SickLeaveDays, ProbationMonths,
        //  NoticeDays, OtNormal, OtHoliday, EndOfServiceNote)
        var packs = new (string Country, string Currency, string Weekend, int Annual, int Sick, int Probation, int Notice, decimal OtNormal, decimal OtHoliday, string Eosb)[]
        {
            // GCC
            ("AE", "AED", "Fri-Sat", 30, 90, 6, 30, 1.25m, 1.50m, "Gratuity: 21 days/yr for first 5 yrs, 30 days/yr thereafter (UAE Labour Law)"),
            ("SA", "SAR", "Fri-Sat", 21, 120, 3, 60, 1.50m, 2.00m, "End-of-service: 0.5 month/yr first 5 yrs, 1 month/yr after"),
            ("QA", "QAR", "Fri-Sat", 21, 84, 6, 30, 1.25m, 1.50m, "End-of-service gratuity: min 3 weeks basic/yr (Labour Law No. 14/2004)"),
            ("KW", "KWD", "Fri-Sat", 30, 75, 3, 90, 1.25m, 1.50m, "Indemnity: 15 days/yr first 5 yrs, 1 month/yr thereafter"),
            ("OM", "OMR", "Fri-Sat", 30, 70, 3, 30, 1.25m, 2.00m, "Gratuity per Omani Labour Law for non-citizens"),
            ("BH", "BHD", "Fri-Sat", 30, 55, 3, 30, 1.25m, 1.50m, "Leaving indemnity: 15 days/yr first 3 yrs, 1 month/yr after"),
            // Middle East / Africa
            ("EG", "EGP", "Fri-Sat", 21, 180, 3, 60, 1.35m, 2.00m, "End-of-service per Egyptian Labour Law No. 12/2003"),
            ("ZA", "ZAR", "Sat-Sun", 21, 30, 3, 30, 1.50m, 2.00m, "Severance 1 week/yr (BCEA); sick 30 days per 36-month cycle"),
            ("NG", "NGN", "Sat-Sun", 6, 12, 3, 30, 1.50m, 2.00m, "Per Nigerian Labour Act; redundancy by agreement"),
            // Asia
            ("IN", "INR", "Sat-Sun", 18, 12, 6, 30, 2.00m, 2.00m, "Gratuity (Payment of Gratuity Act): 15 days wages/yr after 5 yrs"),
            ("PK", "PKR", "Sat-Sun", 14, 16, 3, 30, 2.00m, 2.00m, "Gratuity 30 days/yr or provident fund"),
            ("PH", "PHP", "Sat-Sun", 5, 0, 6, 30, 1.25m, 2.00m, "13th-month pay mandatory; separation pay per Labor Code"),
            ("SG", "SGD", "Sat-Sun", 14, 14, 3, 30, 1.50m, 2.00m, "No statutory gratuity; OT under Employment Act for covered staff"),
            // Europe
            ("GB", "GBP", "Sat-Sun", 28, 28, 3, 30, 1.50m, 2.00m, "Statutory: no gratuity; redundancy pay per service length"),
            ("DE", "EUR", "Sat-Sun", 20, 42, 6, 28, 1.25m, 1.50m, "No statutory severance; 6 weeks continued sick pay; notice per BGB §622"),
            ("FR", "EUR", "Sat-Sun", 25, 90, 2, 30, 1.25m, 1.50m, "35-hr week; severance per Code du Travail; OT +25% then +50%"),
            // North America
            ("US", "USD", "Sat-Sun", 15, 5, 3, 14, 1.50m, 1.50m, "At-will; FLSA overtime 1.5x over 40 hrs/week; no statutory gratuity"),
            ("CA", "CAD", "Sat-Sun", 10, 10, 3, 14, 1.50m, 1.50m, "Vacation pay 4%+; severance per ESA; no gratuity (province-specific)"),
            // Oceania
            ("AU", "AUD", "Sat-Sun", 20, 10, 6, 28, 1.50m, 2.00m, "4 weeks annual leave; redundancy & long-service leave per NES"),
        };

        var rules = new List<CountryPayrollRule>();
        void Add(string country, string key, string val, string type, string desc) =>
            rules.Add(new CountryPayrollRule { TenantId = tenantId, CountryCode = country, RuleKey = key, RuleValue = val, DataType = type, Description = desc });

        foreach (var p in packs)
        {
            Add(p.Country, "default_currency", p.Currency, "string", "Default payroll currency");
            Add(p.Country, "weekend_days", p.Weekend, "string", "Statutory weekend");
            Add(p.Country, "annual_leave_days", p.Annual.ToString(), "int", "Statutory annual leave entitlement (days)");
            Add(p.Country, "sick_leave_days", p.Sick.ToString(), "int", "Statutory sick leave entitlement (days)");
            Add(p.Country, "probation_months", p.Probation.ToString(), "int", "Maximum probation period (months)");
            Add(p.Country, "notice_period_days", p.Notice.ToString(), "int", "Default notice period (days)");
            Add(p.Country, "overtime_multiplier_normal", p.OtNormal.ToString(System.Globalization.CultureInfo.InvariantCulture), "decimal", "Overtime multiplier — normal day");
            Add(p.Country, "overtime_multiplier_holiday", p.OtHoliday.ToString(System.Globalization.CultureInfo.InvariantCulture), "decimal", "Overtime multiplier — public holiday/rest day");
            Add(p.Country, "end_of_service", p.Eosb, "string", "End-of-service / gratuity rule");
        }
        _db.CountryPayrollRules.AddRange(rules);
        await _db.SaveChangesAsync(ct);
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
                LegalNameEn = "KynexOne Technologies FZ-LLC",
                LegalNameAr = "كينكس ون للتقنية",
                TradeName = "KynexOne",
                CountryCode = "UAE",
                RegistrationNumber = "KNX-DEMO",
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

        try
        {
            await EnsureDemoOperationalData(tenantId, company.Id, branch.Id, cancellationToken);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[Seed] Demo operational data skipped: {ex.Message}");
        }
    }

    private record DemoPerson(string Code, string Name, string Dept, string Title, string Type, string Gender, string Nationality, decimal Basic);

    private async Task EnsureDemoOperationalData(Guid tenantId, Guid companyId, Guid branchId, CancellationToken ct)
    {
        // Idempotent: if attendance already seeded for this tenant, assume demo data exists.
        if (await _db.AttendanceRecords.AnyAsync(x => x.TenantId == tenantId, ct)) return;

        // ── Departments (beyond HR) ──────────────────────────────────────────────
        var deptDefs = new (string Code, string En, string Ar)[]
        {
            ("ENG", "Engineering", "الهندسة"),
            ("FIN", "Finance", "المالية"),
            ("OPS", "Operations", "العمليات"),
            ("SAL", "Sales", "المبيعات"),
        };
        foreach (var d in deptDefs)
        {
            if (!await _db.Departments.AnyAsync(x => x.TenantId == tenantId && x.Code == d.Code, ct))
                _db.Departments.Add(new Department { TenantId = tenantId, BranchId = branchId, Code = d.Code, NameEn = d.En, NameAr = d.Ar });
        }
        await _db.SaveChangesAsync(ct);

        // ── Employees ────────────────────────────────────────────────────────────
        var people = new[]
        {
            new DemoPerson("KNX-0001", "Aisha Al Mansoori", "Human Resources", "HR Director", "Full-time", "Female", "Emirati", 24000m),
            new DemoPerson("KNX-0002", "Omar Khalifa", "Engineering", "Engineering Manager", "Full-time", "Male", "Emirati", 22000m),
            new DemoPerson("KNX-0003", "Priya Nair", "Finance", "Finance Manager", "Full-time", "Female", "Indian", 19000m),
            new DemoPerson("KNX-0004", "James Carter", "Engineering", "Senior Software Engineer", "Full-time", "Male", "British", 18000m),
            new DemoPerson("KNX-0005", "Fatima Al Hashimi", "Human Resources", "HR Officer", "Full-time", "Female", "Emirati", 12000m),
            new DemoPerson("KNX-0006", "Rahul Mehta", "Operations", "Operations Lead", "Full-time", "Male", "Indian", 14000m),
            new DemoPerson("KNX-0007", "Sara Abdullah", "Sales", "Account Executive", "Contract", "Female", "Saudi", 13000m),
            new DemoPerson("KNX-0008", "Daniel Okoro", "Operations", "Logistics Coordinator", "Part-time", "Male", "Nigerian", 9000m),
        };

        var today = DateOnly.FromDateTime(DateTime.UtcNow.Date);
        var employees = new List<Employee>();
        foreach (var (p, i) in people.Select((p, i) => (p, i)))
        {
            var existing = await _db.Employees.FirstOrDefaultAsync(x => x.TenantId == tenantId && x.EmployeeCode == p.Code, ct);
            if (existing is not null) { employees.Add(existing); continue; }
            var emp = new Employee
            {
                TenantId = tenantId,
                CompanyId = companyId,
                BranchId = branchId,
                EmployeeCode = p.Code,
                FullName = p.Name,
                EnglishName = p.Name,
                WorkEmail = $"{p.Name.Split(' ')[0].ToLowerInvariant()}.{p.Name.Split(' ')[^1].ToLowerInvariant()}@kynexone.com",
                Phone = $"+9715{(50000000 + i):00000000}",
                Gender = p.Gender,
                Nationality = p.Nationality,
                CountryCode = "UAE",
                Department = p.Dept,
                Designation = p.Title,
                JobTitle = p.Title,
                EmploymentType = p.Type,
                ContractType = p.Type == "Contract" ? "Fixed-term" : "Permanent",
                WorkLocation = "Dubai HQ",
                Status = "Active",
                JoiningDate = DateTime.UtcNow.AddDays(-(40 + i * 35)),
            };
            // make a couple of them joined this month for "new joiners" signal
            if (i >= 6) emp.JoiningDate = DateTime.UtcNow.AddDays(-8);
            _db.Employees.Add(emp);
            employees.Add(emp);
        }
        await _db.SaveChangesAsync(ct);

        // ── Attendance (last ~4 months of working days) ──────────────────────────
        var rng = new Random(42);
        var attendance = new List<AttendanceRecord>();
        for (var offset = 0; offset <= 120; offset++)
        {
            var date = today.AddDays(-offset);
            if (date.DayOfWeek is DayOfWeek.Friday or DayOfWeek.Saturday) continue; // GCC weekend
            foreach (var emp in employees)
            {
                var roll = rng.Next(100);
                var status = roll < 85 ? "Present" : roll < 92 ? "Late" : roll < 97 ? "Leave" : "Absent";
                attendance.Add(new AttendanceRecord
                {
                    TenantId = tenantId,
                    EmployeeId = emp.Id,
                    WorkDate = date,
                    Status = status,
                    OvertimeHours = status == "Present" && rng.Next(100) < 20 ? rng.Next(1, 4) : 0,
                    Notes = string.Empty,
                });
            }
        }
        _db.AttendanceRecords.AddRange(attendance);
        await _db.SaveChangesAsync(ct);

        // ── Leave types ──────────────────────────────────────────────────────────
        var leaveTypeDefs = new (string Code, string En, string Ar, string Cat, bool Paid)[]
        {
            ("ANNUAL", "Annual Leave", "إجازة سنوية", "Annual", true),
            ("SICK", "Sick Leave", "إجازة مرضية", "Sick", true),
            ("CASUAL", "Casual Leave", "إجازة عارضة", "Casual", true),
            ("MATERNITY", "Maternity Leave", "إجازة أمومة", "Maternity", true),
            ("UNPAID", "Unpaid Leave", "إجازة بدون راتب", "Unpaid", false),
        };
        var leaveTypes = new List<LeaveType>();
        var ltSort = 0;
        foreach (var lt in leaveTypeDefs)
        {
            var existing = await _db.LeaveTypes.FirstOrDefaultAsync(x => x.TenantId == tenantId && x.Code == lt.Code, ct);
            if (existing is not null) { leaveTypes.Add(existing); continue; }
            var entity = new LeaveType { TenantId = tenantId, Code = lt.Code, NameEn = lt.En, NameAr = lt.Ar, Category = lt.Cat, IsPaid = lt.Paid, IsActive = true, SortOrder = ltSort++ };
            _db.LeaveTypes.Add(entity);
            leaveTypes.Add(entity);
        }
        await _db.SaveChangesAsync(ct);

        // ── Leave requests (mix of pending + approved) ───────────────────────────
        var annual = leaveTypes.First(x => x.Code == "ANNUAL");
        var sick = leaveTypes.First(x => x.Code == "SICK");
        _db.LeaveRequests.AddRange(
            new LeaveRequest { TenantId = tenantId, EmployeeId = employees[3].Id, EmployeeName = employees[3].FullName, DepartmentName = employees[3].Department, LeaveTypeId = annual.Id, LeaveTypeName = annual.NameEn, StartDate = today.AddDays(5), EndDate = today.AddDays(9), DayType = "Full", Reason = "Family vacation", Status = "Submitted", SubmittedAtUtc = DateTime.UtcNow.AddDays(-1) },
            new LeaveRequest { TenantId = tenantId, EmployeeId = employees[5].Id, EmployeeName = employees[5].FullName, DepartmentName = employees[5].Department, LeaveTypeId = sick.Id, LeaveTypeName = sick.NameEn, StartDate = today.AddDays(-2), EndDate = today.AddDays(-1), DayType = "Full", Reason = "Flu", Status = "Approved", SubmittedAtUtc = DateTime.UtcNow.AddDays(-3), DecidedAtUtc = DateTime.UtcNow.AddDays(-2) },
            new LeaveRequest { TenantId = tenantId, EmployeeId = employees[6].Id, EmployeeName = employees[6].FullName, DepartmentName = employees[6].Department, LeaveTypeId = annual.Id, LeaveTypeName = annual.NameEn, StartDate = today.AddDays(12), EndDate = today.AddDays(14), DayType = "Full", Reason = "Personal", Status = "Submitted", SubmittedAtUtc = DateTime.UtcNow.AddHours(-6) }
        );
        await _db.SaveChangesAsync(ct);

        // ── Payroll run for last month + slips ───────────────────────────────────
        var lastMonth = new DateOnly(today.Year, today.Month, 1).AddMonths(-1);
        var run = new PayrollRun
        {
            TenantId = tenantId, Year = lastMonth.Year, Month = lastMonth.Month, Status = "Completed",
            CreatedAtUtc = DateTime.UtcNow.AddDays(-10), ProcessedAtUtc = DateTime.UtcNow.AddDays(-8),
        };
        decimal totalGross = 0, totalDed = 0, totalNet = 0;
        var slips = new List<PayrollSlip>();
        foreach (var emp in employees)
        {
            var person = people.First(p => p.Code == emp.EmployeeCode);
            var basic = person.Basic;
            var housing = Math.Round(basic * 0.25m);
            var transport = 1000m;
            var gross = basic + housing + transport;
            var deductions = Math.Round(gross * 0.05m); // sample social/insurance
            var net = gross - deductions;
            totalGross += gross; totalDed += deductions; totalNet += net;
            slips.Add(new PayrollSlip
            {
                TenantId = tenantId, RunId = run.Id, EmployeeId = emp.Id, EmployeeCode = emp.EmployeeCode,
                EmployeeName = emp.FullName, Department = emp.Department, BasicSalary = basic,
                HousingAllowance = housing, TransportAllowance = transport, OtherAllowances = 0,
                GrossSalary = gross, Deductions = deductions, NetSalary = net, Status = "Paid",
            });
        }
        run.TotalGrossSalary = totalGross; run.TotalDeductions = totalDed; run.TotalNetSalary = totalNet; run.EmployeeCount = slips.Count;
        _db.PayrollRuns.Add(run);
        _db.PayrollSlips.AddRange(slips);
        await _db.SaveChangesAsync(ct);

        // ── Pending approvals ────────────────────────────────────────────────────
        var workflowId = (await _db.ApprovalWorkflows.FirstOrDefaultAsync(x => x.TenantId == tenantId, ct))?.Id ?? Guid.NewGuid();
        _db.ApprovalRequests.AddRange(
            new ApprovalRequest { TenantId = tenantId, WorkflowId = workflowId, EntityName = "LeaveRequest", EntityId = Guid.NewGuid().ToString(), Title = $"Annual leave — {employees[3].FullName}", Status = "Pending", CurrentStepOrder = 1, CreatedAtUtc = DateTime.UtcNow.AddDays(-1) },
            new ApprovalRequest { TenantId = tenantId, WorkflowId = workflowId, EntityName = "PayrollRun", EntityId = run.Id.ToString(), Title = $"Payroll approval — {lastMonth:MMM yyyy}", Status = "Pending", CurrentStepOrder = 1, CreatedAtUtc = DateTime.UtcNow.AddHours(-20) },
            new ApprovalRequest { TenantId = tenantId, WorkflowId = workflowId, EntityName = "EmployeeTransferRequest", EntityId = Guid.NewGuid().ToString(), Title = $"Transfer request — {employees[5].FullName}", Status = "Pending", CurrentStepOrder = 1, CreatedAtUtc = DateTime.UtcNow.AddHours(-8) },
            new ApprovalRequest { TenantId = tenantId, WorkflowId = workflowId, EntityName = "LeaveRequest", EntityId = Guid.NewGuid().ToString(), Title = $"Annual leave — {employees[6].FullName}", Status = "Pending", CurrentStepOrder = 1, CreatedAtUtc = DateTime.UtcNow.AddHours(-4) }
        );
        await _db.SaveChangesAsync(ct);

        // ── Compliance records nearing expiry (powers dashboard alerts) ──────────
        _db.EmployeeComplianceRecords.AddRange(
            new EmployeeComplianceRecord { TenantId = tenantId, EmployeeId = employees[1].Id, CountryCode = "UAE", FieldKey = "passport", FieldLabel = "Passport", FieldValue = "P1234567", ExpiryDate = today.AddDays(18), IsRequired = true },
            new EmployeeComplianceRecord { TenantId = tenantId, EmployeeId = employees[3].Id, CountryCode = "UAE", FieldKey = "visa", FieldLabel = "Residence Visa", FieldValue = "V998877", ExpiryDate = today.AddDays(45), IsRequired = true },
            new EmployeeComplianceRecord { TenantId = tenantId, EmployeeId = employees[6].Id, CountryCode = "UAE", FieldKey = "emirates_id", FieldLabel = "Emirates ID", FieldValue = "784-XXXX", ExpiryDate = today.AddDays(-3), IsRequired = true }
        );
        await _db.SaveChangesAsync(ct);

        // ── Recruitment: job openings + candidates ───────────────────────────────
        _db.JobOpenings.AddRange(
            new JobOpening { TenantId = tenantId, JobCode = "JOB-2026-0001", Title = "Senior Software Engineer", DepartmentName = "Engineering", EmploymentType = "Full-Time", HeadCount = 2, FilledCount = 0, Location = "Dubai HQ", SalaryFrom = 18000, SalaryTo = 26000, Status = "Open", Description = "Build and scale KynexOne platform services.", PublishedAtUtc = DateTime.UtcNow.AddDays(-12) },
            new JobOpening { TenantId = tenantId, JobCode = "JOB-2026-0002", Title = "Payroll Specialist", DepartmentName = "Finance", EmploymentType = "Full-Time", HeadCount = 1, FilledCount = 0, Location = "Dubai HQ", SalaryFrom = 11000, SalaryTo = 15000, Status = "Open", Description = "Own monthly WPS payroll processing.", PublishedAtUtc = DateTime.UtcNow.AddDays(-6) },
            new JobOpening { TenantId = tenantId, JobCode = "JOB-2026-0003", Title = "Sales Account Executive", DepartmentName = "Sales", EmploymentType = "Full-Time", HeadCount = 3, FilledCount = 1, Location = "Abu Dhabi", SalaryFrom = 12000, SalaryTo = 18000, Status = "InProgress", Description = "Drive enterprise SaaS sales across the GCC.", PublishedAtUtc = DateTime.UtcNow.AddDays(-20) }
        );
        _db.Candidates.AddRange(
            new Candidate { TenantId = tenantId, FirstName = "Layla", LastName = "Haddad", Email = "layla.haddad@example.com", Phone = "+971551110001", CurrentJobTitle = "Software Engineer", CurrentCompany = "Tech Co", TotalExperienceYears = 6, EducationLevel = "Bachelor", Nationality = "Lebanese", Source = "LinkedIn", Status = "Active" },
            new Candidate { TenantId = tenantId, FirstName = "Mohammed", LastName = "Raza", Email = "m.raza@example.com", Phone = "+971551110002", CurrentJobTitle = "Payroll Analyst", CurrentCompany = "Finance Ltd", TotalExperienceYears = 4, EducationLevel = "Bachelor", Nationality = "Pakistani", Source = "Referral", Status = "Active" },
            new Candidate { TenantId = tenantId, FirstName = "Elena", LastName = "Petrova", Email = "elena.p@example.com", Phone = "+971551110003", CurrentJobTitle = "Account Executive", CurrentCompany = "SaaS Inc", TotalExperienceYears = 8, EducationLevel = "Master", Nationality = "Russian", Source = "Agency", Status = "Active" },
            new Candidate { TenantId = tenantId, FirstName = "Yousef", LastName = "Salem", Email = "yousef.salem@example.com", Phone = "+971551110004", CurrentJobTitle = "Backend Engineer", CurrentCompany = "Cloud Co", TotalExperienceYears = 5, EducationLevel = "Bachelor", Nationality = "Jordanian", Source = "JobBoard", Status = "Active" }
        );
        await _db.SaveChangesAsync(ct);

        // ── Performance: appraisal reviews ────────────────────────────────────────
        var cycleId = Guid.NewGuid();
        var ratings = new[] { "Exceeds Expectations", "Meets Expectations", "Meets Expectations", "Outstanding", "Meets Expectations" };
        var reviews = employees.Take(5).Select((emp, i) =>
        {
            var kpi = 3.5m + (i % 3) * 0.4m;
            var comp = 3.2m + (i % 4) * 0.3m;
            var final = Math.Round((kpi + comp) / 2m, 2);
            return new AppraisalReview
            {
                TenantId = tenantId, CycleId = cycleId, CycleName = "H1 2026 Performance Review",
                ScorecardTemplateId = Guid.NewGuid(), EmployeeId = emp.Id, EmployeeName = emp.FullName,
                DepartmentName = emp.Department, DesignationTitle = emp.Designation,
                KpiScore = kpi, CompetencyScore = comp, AttendanceScore = 4.2m, ProductivityScore = 3.8m,
                FinalScore = final, FinalRating = ratings[i % ratings.Length], Status = "Published",
                PublishedAt = DateTime.UtcNow.AddDays(-5),
            };
        }).ToList();
        _db.AppraisalReviews.AddRange(reviews);
        await _db.SaveChangesAsync(ct);

        // ── Notifications for the admin + AI insights ────────────────────────────
        var adminUserId = (await _db.Users.FirstOrDefaultAsync(x => x.TenantId == tenantId, ct))?.Id;
        if (adminUserId is not null)
        {
            _db.Notifications.AddRange(
                new Notification { TenantId = tenantId, UserId = adminUserId, Title = "Payroll ready for approval", Message = $"{lastMonth:MMMM yyyy} payroll run is awaiting your approval.", EntityName = "PayrollRun", Status = "Unread" },
                new Notification { TenantId = tenantId, UserId = adminUserId, Title = "Document expiring", Message = "An Emirates ID has expired and needs renewal.", EntityName = "Compliance", Status = "Unread" },
                new Notification { TenantId = tenantId, UserId = adminUserId, Title = "New leave request", Message = $"{employees[3].FullName} submitted an annual leave request.", EntityName = "LeaveRequest", Status = "Unread" }
            );
        }
        _db.AIInsights.AddRange(
            new AIInsight { TenantId = tenantId, Module = "Attendance", InsightType = "AbsenteeismPattern", Severity = "Warning", Title = "Rising absenteeism in Operations", Summary = "Operations shows a 12% higher unplanned-absence rate over the last two weeks versus the company average.", GeneratedBy = "System" },
            new AIInsight { TenantId = tenantId, Module = "Compliance", InsightType = "DocumentExpiry", Severity = "Critical", Title = "1 document expired, 2 expiring soon", Summary = "An Emirates ID has expired and a passport/visa expire within 45 days. Initiate renewals to avoid compliance gaps.", GeneratedBy = "System" },
            new AIInsight { TenantId = tenantId, Module = "Payroll", InsightType = "PayrollVariance", Severity = "Info", Title = "Payroll stable month-over-month", Summary = "Net payroll is within 2% of the prior period with no unusual variances detected.", GeneratedBy = "System" }
        );
        await _db.SaveChangesAsync(ct);
    }
}
