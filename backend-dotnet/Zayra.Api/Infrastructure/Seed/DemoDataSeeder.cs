using Microsoft.EntityFrameworkCore;
using Zayra.Api.Application.Auth;
using Zayra.Api.Data;
using Zayra.Api.Domain.Entities;
using Zayra.Api.Infrastructure.Auth;
using Zayra.Api.Models;

namespace Zayra.Api.Infrastructure.Seed;

/// <summary>
/// Seeds two demo tenants (IntelliFlow Systems and Evostel LLC) on startup.
/// Fully idempotent — the outer tenant guard skips the whole block when the slug exists.
/// Each tenant gets a complete set of named demo users:
///   admin, hrdirector, hrmanager, finance, manager, supervisor, employee1, employee2, auditor
/// All passwords are Demo@1234.
/// </summary>
public static class DemoDataSeeder
{
    private const string DemoPassword = "Demo@1234";

    public static async Task SeedAsync(
        ZayraDbContext db,
        IPasswordHasher hasher,
        IAuthSeeder authSeeder,
        ILogger logger,
        CancellationToken ct = default)
    {
        await SeedPlatformOwnerAsync(db, hasher, logger, ct);

        await SeedTenantAsync(db, hasher, authSeeder, logger, ct, new DemoTenantSpec
        {
            Name         = "IntelliFlow Systems",
            Slug         = "intelliflow",
            Plan         = "Enterprise",
            Status       = "Active",
            MaxEmployees = 500,
            MaxUsers     = 100,
            MonthlyAmount = 2500m,
            CurrencyCode  = "USD",
            BillingEmail  = "billing@intelliflow.com",
            ExpiresAtUtc  = null,
            SeedOrgStructure = true,
            Features = new Dictionary<string, bool>
            {
                [FeatureKeys.Recruitment]     = true,
                [FeatureKeys.Performance]     = true,
                [FeatureKeys.Compliance]      = true,
                [FeatureKeys.AiAssistant]     = true,
                [FeatureKeys.Finance]         = true,
                [FeatureKeys.Payroll]         = true,
                [FeatureKeys.Shifts]          = true,
                [FeatureKeys.Overtime]        = true,
                [FeatureKeys.MobileApp]       = true,
                [FeatureKeys.WpsExport]       = true,
                [FeatureKeys.QiwaIntegration] = true,
            },
            Users = new[]
            {
                new DemoUserSpec("Admin",           "IntelliFlow Administrator", "admin@intelliflow.com"),
                new DemoUserSpec("HR Director",     "Sarah Mitchell",            "hrdirector@intelliflow.com"),
                new DemoUserSpec("HR Manager",      "Omar Al-Farsi",             "hrmanager@intelliflow.com"),
                new DemoUserSpec("Finance Approver","Chen Wei",                  "finance@intelliflow.com"),
                new DemoUserSpec("Manager",         "Priya Sharma",              "manager@intelliflow.com"),
                new DemoUserSpec("Supervisor",      "Khalid Al-Rashid",          "supervisor@intelliflow.com"),
                new DemoUserSpec("Employee",        "Fatima Al-Zahra",           "employee1@intelliflow.com"),
                new DemoUserSpec("Employee",        "James O'Brien",             "employee2@intelliflow.com"),
                new DemoUserSpec("Auditor",         "Maya Johnson",              "auditor@intelliflow.com"),
            }
        });

        await SeedTenantAsync(db, hasher, authSeeder, logger, ct, new DemoTenantSpec
        {
            Name         = "Evostel LLC",
            Slug         = "evostel",
            Plan         = "Starter",
            Status       = "PastDue",
            MaxEmployees = 50,
            MaxUsers     = 10,
            MonthlyAmount = 299m,
            CurrencyCode  = "USD",
            BillingEmail  = "billing@evostel.com",
            ExpiresAtUtc  = DateTime.UtcNow.AddDays(7),
            SeedOrgStructure = true,
            Features = new Dictionary<string, bool>
            {
                [FeatureKeys.Recruitment]     = false,
                [FeatureKeys.Performance]     = false,
                [FeatureKeys.Compliance]      = false,
                [FeatureKeys.AiAssistant]     = false,
                [FeatureKeys.Finance]         = true,
                [FeatureKeys.Payroll]         = true,
                [FeatureKeys.Shifts]          = false,
                [FeatureKeys.Overtime]        = false,
                [FeatureKeys.MobileApp]       = false,
                [FeatureKeys.WpsExport]       = false,
                [FeatureKeys.QiwaIntegration] = false,
            },
            Users = new[]
            {
                new DemoUserSpec("Admin",           "Evostel Administrator",   "admin@evostel.com"),
                new DemoUserSpec("HR Manager",      "Dana Wilkins",            "hrmanager@evostel.com"),
                new DemoUserSpec("Finance Approver","Tom Reyes",               "finance@evostel.com"),
                new DemoUserSpec("Manager",         "Lena Müller",             "manager@evostel.com"),
                new DemoUserSpec("Supervisor",      "Raj Patel",               "supervisor@evostel.com"),
                new DemoUserSpec("Employee",        "Nina Costa",              "employee1@evostel.com"),
                new DemoUserSpec("Employee",        "David Kim",               "employee2@evostel.com"),
                new DemoUserSpec("Auditor",         "Ama Owusu",               "auditor@evostel.com"),
            }
        });
    }

    // ── Platform owner ────────────────────────────────────────────────────────

    private static async Task SeedPlatformOwnerAsync(
        ZayraDbContext db,
        IPasswordHasher hasher,
        ILogger logger,
        CancellationToken ct)
    {
        if (await db.PlatformUsers.AnyAsync(ct))
            return;

        var email    = Environment.GetEnvironmentVariable("PLATFORM_ADMIN_EMAIL") ?? "platform@kynex.one";
        var password = Environment.GetEnvironmentVariable("PLATFORM_ADMIN_PASSWORD");

        if (string.IsNullOrWhiteSpace(password))
        {
            logger.LogWarning("DemoDataSeeder: PLATFORM_ADMIN_PASSWORD not set — skipping platform owner seed.");
            return;
        }

        db.PlatformUsers.Add(new PlatformUser
        {
            Email        = email.Trim().ToLowerInvariant(),
            FullName     = "Platform Owner",
            PasswordHash = hasher.Hash(password),
            Role         = PlatformRoles.Owner,
            IsActive     = true,
            CreatedAtUtc = DateTime.UtcNow
        });
        await db.SaveChangesAsync(ct);
        logger.LogInformation("DemoDataSeeder: seeded platform owner {Email}", email);
    }

    // ── Tenant helper ─────────────────────────────────────────────────────────

    private static async Task SeedTenantAsync(
        ZayraDbContext db,
        IPasswordHasher hasher,
        IAuthSeeder authSeeder,
        ILogger logger,
        CancellationToken ct,
        DemoTenantSpec spec)
    {
        if (await db.Tenants.AsNoTracking().AnyAsync(t => t.Slug == spec.Slug, ct))
        {
            var existingTenant = await db.Tenants.FirstAsync(t => t.Slug == spec.Slug, ct);

            // Ensure any new permissions added since initial seed are propagated to existing roles.
            await authSeeder.EnsureTenantRolesAsync(existingTenant.Id, ct);

            // Restore is_active if a test or admin deactivated the demo tenant
            if (!existingTenant.IsActive)
            {
                existingTenant.IsActive = true;
                await db.SaveChangesAsync(ct);
                logger.LogInformation("DemoDataSeeder: restored is_active for '{Slug}'.", spec.Slug);
            }

            // Update subscription if status/plan drifted
            var existingSub = await db.TenantSubscriptions.FirstOrDefaultAsync(s => s.TenantId == existingTenant.Id, ct);
            if (existingSub is not null && (existingSub.Status != spec.Status || existingSub.Plan != spec.Plan))
            {
                existingSub.Status = spec.Status;
                existingSub.Plan   = spec.Plan;
                existingSub.ExpiresAtUtc = spec.ExpiresAtUtc;
                await db.SaveChangesAsync(ct);
                logger.LogInformation("DemoDataSeeder: updated subscription for '{Slug}' to {Plan}/{Status}.", spec.Slug, spec.Plan, spec.Status);
            }

            // Add any missing demo users (idempotent: skip emails that already exist in this tenant)
            var existingEmailList = await db.Users
                .AsNoTracking()
                .Where(u => u.TenantId == existingTenant.Id)
                .Select(u => u.NormalizedEmail)
                .ToListAsync(ct);
            var existingEmails = new HashSet<string>(existingEmailList, StringComparer.OrdinalIgnoreCase);

            var existingRoleMap = await db.Roles.AsNoTracking()
                .Where(r => r.TenantId == existingTenant.Id)
                .ToDictionaryAsync(r => r.Name, StringComparer.OrdinalIgnoreCase, ct);

            bool addedUsers = false;
            foreach (var userSpec in spec.Users)
            {
                var normalized = AuthService.Normalize(userSpec.Email);
                if (existingEmails.Contains(normalized)) continue;
                if (!existingRoleMap.TryGetValue(userSpec.RoleName, out var role))
                {
                    logger.LogWarning("DemoDataSeeder: role '{Role}' not found for tenant '{Slug}' — skipping user {Email}.", userSpec.RoleName, spec.Slug, userSpec.Email);
                    continue;
                }
                var user = new User
                {
                    TenantId         = existingTenant.Id,
                    Email            = userSpec.Email.Trim().ToLowerInvariant(),
                    NormalizedEmail  = normalized,
                    FullName         = userSpec.FullName,
                    PasswordHash     = hasher.Hash(DemoPassword),
                    AccessMode       = "FullPortal",
                    Status           = "Active",
                    IsActive         = true,
                    IsEmailConfirmed = true,
                };
                user.UserRoles.Add(new UserRole { User = user, RoleId = role.Id });
                db.Users.Add(user);
                addedUsers = true;
                logger.LogInformation("DemoDataSeeder: added missing user {Email} to tenant '{Slug}'.", userSpec.Email, spec.Slug);
            }
            if (addedUsers) await db.SaveChangesAsync(ct);

            return;
        }

        // 1. Create tenant
        var tenant = new Tenant { Name = spec.Name, Slug = spec.Slug, IsActive = true };
        db.Tenants.Add(tenant);
        await db.SaveChangesAsync(ct);

        // 2. Seed full RBAC (creates all roles + permissions)
        await authSeeder.EnsureTenantRolesAsync(tenant.Id, ct);

        // 3. Load roles for assignment
        var roleMap = await db.Roles.AsNoTracking()
            .Where(r => r.TenantId == tenant.Id)
            .ToDictionaryAsync(r => r.Name, StringComparer.OrdinalIgnoreCase, ct);

        // 4. Create users
        foreach (var userSpec in spec.Users)
        {
            if (!roleMap.TryGetValue(userSpec.RoleName, out var role))
            {
                logger.LogWarning("DemoDataSeeder: role '{Role}' not found for tenant '{Slug}' — skipping user {Email}.",
                    userSpec.RoleName, spec.Slug, userSpec.Email);
                continue;
            }

            var user = new User
            {
                TenantId         = tenant.Id,
                Email            = userSpec.Email.Trim().ToLowerInvariant(),
                NormalizedEmail  = AuthService.Normalize(userSpec.Email),
                FullName         = userSpec.FullName,
                PasswordHash     = hasher.Hash(DemoPassword),
                AccessMode       = "FullPortal",
                Status           = "Active",
                IsActive         = true,
                IsEmailConfirmed = true,
            };
            user.UserRoles.Add(new UserRole { User = user, RoleId = role.Id });
            db.Users.Add(user);
        }

        // 5. Subscription
        db.TenantSubscriptions.Add(new TenantSubscription
        {
            TenantId     = tenant.Id,
            Plan         = spec.Plan,
            Status       = spec.Status,
            MaxEmployees = spec.MaxEmployees,
            MaxUsers     = spec.MaxUsers,
            BillingEmail = spec.BillingEmail,
            BillingCycle = "Monthly",
            MonthlyAmount = spec.MonthlyAmount,
            CurrencyCode  = spec.CurrencyCode,
            ExpiresAtUtc  = spec.ExpiresAtUtc,
        });

        // 6. Feature flags
        foreach (var (key, enabled) in spec.Features)
        {
            db.TenantFeatureFlags.Add(new TenantFeatureFlag
            {
                TenantId    = tenant.Id,
                FeatureKey  = key,
                IsEnabled   = enabled,
                UpdatedAtUtc = DateTime.UtcNow,
            });
        }

        await db.SaveChangesAsync(ct);

        // 7. Seed org structure (company, grades, departments, designations, employees)
        if (spec.SeedOrgStructure)
            await SeedOrgStructureAsync(db, tenant.Id, spec.Slug, logger, ct);

        logger.LogInformation(
            "DemoDataSeeder: seeded tenant '{Slug}' ({Plan}/{Status}) with {Count} users.",
            spec.Slug, spec.Plan, spec.Status, spec.Users.Length);
    }

    // ── Org structure seed ────────────────────────────────────────────────────

    private static async Task SeedOrgStructureAsync(
        ZayraDbContext db,
        Guid tenantId,
        string slug,
        ILogger logger,
        CancellationToken ct)
    {
        // Idempotent: skip if employees already exist
        if (await db.Employees.AnyAsync(e => e.TenantId == tenantId, ct))
            return;

        // ── Company ─────────────────────────────────────────────────────────
        var company = new Company
        {
            TenantId = tenantId,
            LegalNameEn = slug == "intelliflow" ? "IntelliFlow Systems Pvt Ltd" : "Evostel LLC",
            LegalNameAr = string.Empty,
            TradeName = slug == "intelliflow" ? "IntelliFlow" : "Evostel",
            CountryCode = "AE",
            RegistrationNumber = slug == "intelliflow" ? "IFL-2019-001" : "EVS-2021-001",
            DefaultCurrency = "AED",
            IsActive = true,
        };
        db.Companies.Add(company);
        await db.SaveChangesAsync(ct);

        var branch = new Branch
        {
            TenantId = tenantId,
            CompanyId = company.Id,
            Code = "HQ",
            NameEn = "Head Office",
            NameAr = "المقر الرئيسي",
            CountryCode = "AE",
            City = "Dubai",
            TimeZoneId = "Arabian Standard Time",
            IsHeadOffice = true,
            IsActive = true,
        };
        db.Branches.Add(branch);
        await db.SaveChangesAsync(ct);

        if (slug != "intelliflow")
        {
            // Evostel: minimal org structure only
            await SeedMinimalOrgAsync(db, tenantId, company, branch, ct);
            logger.LogInformation("DemoDataSeeder: seeded minimal org for '{Slug}'.", slug);
            return;
        }

        // ── Grades (IntelliFlow) ─────────────────────────────────────────────
        var grades = new[]
        {
            new Grade { TenantId = tenantId, Code = "L1", Name = "Level 1 – Associate",      Band = "Individual Contributor", Level = 1 },
            new Grade { TenantId = tenantId, Code = "L2", Name = "Level 2 – Professional",   Band = "Individual Contributor", Level = 2 },
            new Grade { TenantId = tenantId, Code = "L3", Name = "Level 3 – Senior",         Band = "Individual Contributor", Level = 3 },
            new Grade { TenantId = tenantId, Code = "L4", Name = "Level 4 – Lead",           Band = "Management",            Level = 4 },
            new Grade { TenantId = tenantId, Code = "L5", Name = "Level 5 – Manager",        Band = "Management",            Level = 5 },
            new Grade { TenantId = tenantId, Code = "L6", Name = "Level 6 – Director",       Band = "Executive",             Level = 6 },
            new Grade { TenantId = tenantId, Code = "L7", Name = "Level 7 – VP / C-Suite",   Band = "Executive",             Level = 7 },
        };
        db.Grades.AddRange(grades);
        await db.SaveChangesAsync(ct);
        var gradeMap = grades.ToDictionary(g => g.Code);

        // ── Departments (IntelliFlow) ────────────────────────────────────────
        var deptExec = new Department { TenantId = tenantId, BranchId = branch.Id, Code = "EXEC",     NameEn = "Executive",                     SortOrder = 0 };
        var deptHR   = new Department { TenantId = tenantId, BranchId = branch.Id, Code = "HR",       NameEn = "Human Resources",               SortOrder = 1 };
        var deptEng  = new Department { TenantId = tenantId, BranchId = branch.Id, Code = "ENG",      NameEn = "Engineering",                   SortOrder = 2 };
        var deptFin  = new Department { TenantId = tenantId, BranchId = branch.Id, Code = "FIN",      NameEn = "Finance",                       SortOrder = 3 };
        var deptSales= new Department { TenantId = tenantId, BranchId = branch.Id, Code = "SALES",    NameEn = "Sales & Business Development",  SortOrder = 4 };
        var deptOps  = new Department { TenantId = tenantId, BranchId = branch.Id, Code = "OPS",      NameEn = "Operations",                    SortOrder = 5 };
        db.Departments.AddRange(deptExec, deptHR, deptEng, deptFin, deptSales, deptOps);
        await db.SaveChangesAsync(ct);

        // Sub-departments
        var deptEngFE = new Department { TenantId = tenantId, BranchId = branch.Id, Code = "ENG-FE", NameEn = "Frontend Engineering", ParentDepartmentId = deptEng.Id, SortOrder = 0 };
        var deptEngBE = new Department { TenantId = tenantId, BranchId = branch.Id, Code = "ENG-BE", NameEn = "Backend Engineering",  ParentDepartmentId = deptEng.Id, SortOrder = 1 };
        db.Departments.AddRange(deptEngFE, deptEngBE);
        await db.SaveChangesAsync(ct);

        // ── Designations (IntelliFlow) ───────────────────────────────────────
        var desigCTO     = new Designation { TenantId = tenantId, Code = "CTO",    TitleEn = "Chief Technology Officer",   GradeId = gradeMap["L7"].Id, IsManagerRole = true,  LevelRank = 70, IsSystemDefault = true };
        var desigVPEng   = new Designation { TenantId = tenantId, Code = "VPENG",  TitleEn = "VP Engineering",             GradeId = gradeMap["L6"].Id, IsManagerRole = true,  LevelRank = 60 };
        var desigDirHR   = new Designation { TenantId = tenantId, Code = "DIRHR",  TitleEn = "HR Director",                GradeId = gradeMap["L6"].Id, IsManagerRole = true,  LevelRank = 60, DepartmentId = deptHR.Id };
        var desigHRMgr   = new Designation { TenantId = tenantId, Code = "HRMGR",  TitleEn = "HR Manager",                 GradeId = gradeMap["L5"].Id, IsManagerRole = true,  LevelRank = 50, DepartmentId = deptHR.Id };
        var desigHROff   = new Designation { TenantId = tenantId, Code = "HROFF",  TitleEn = "HR Officer",                 GradeId = gradeMap["L3"].Id, IsManagerRole = false, LevelRank = 30, DepartmentId = deptHR.Id };
        var desigEngMgr  = new Designation { TenantId = tenantId, Code = "ENGMGR", TitleEn = "Engineering Manager",        GradeId = gradeMap["L5"].Id, IsManagerRole = true,  LevelRank = 50, DepartmentId = deptEng.Id };
        var desigSrEng   = new Designation { TenantId = tenantId, Code = "SRENG",  TitleEn = "Senior Software Engineer",   GradeId = gradeMap["L3"].Id, IsManagerRole = false, LevelRank = 30, DepartmentId = deptEng.Id };
        var desigEng     = new Designation { TenantId = tenantId, Code = "ENG",    TitleEn = "Software Engineer",          GradeId = gradeMap["L2"].Id, IsManagerRole = false, LevelRank = 20, DepartmentId = deptEng.Id };
        var desigFinDir  = new Designation { TenantId = tenantId, Code = "FINDIR", TitleEn = "Finance Director",           GradeId = gradeMap["L6"].Id, IsManagerRole = true,  LevelRank = 60, DepartmentId = deptFin.Id };
        var desigFinAcc  = new Designation { TenantId = tenantId, Code = "FINACC", TitleEn = "Finance Accountant",         GradeId = gradeMap["L2"].Id, IsManagerRole = false, LevelRank = 20, DepartmentId = deptFin.Id };
        var desigSalesMgr= new Designation { TenantId = tenantId, Code = "SALESMGR",TitleEn = "Sales Manager",            GradeId = gradeMap["L5"].Id, IsManagerRole = true,  LevelRank = 50, DepartmentId = deptSales.Id };
        var desigSalesExec=new Designation { TenantId = tenantId, Code = "SALESEXC",TitleEn = "Sales Executive",          GradeId = gradeMap["L2"].Id, IsManagerRole = false, LevelRank = 20, DepartmentId = deptSales.Id };
        var desigOpsMgr  = new Designation { TenantId = tenantId, Code = "OPSMGR", TitleEn = "Operations Manager",        GradeId = gradeMap["L5"].Id, IsManagerRole = true,  LevelRank = 50, DepartmentId = deptOps.Id };
        db.Designations.AddRange(desigCTO, desigVPEng, desigDirHR, desigHRMgr, desigHROff,
            desigEngMgr, desigSrEng, desigEng, desigFinDir, desigFinAcc,
            desigSalesMgr, desigSalesExec, desigOpsMgr);
        await db.SaveChangesAsync(ct);

        // ── Employees — Level 7 (C-Suite) ───────────────────────────────────
        var cto = AddEmployee(db, tenantId, company, branch, "IFL-001", "Arjun Sharma",       deptExec, desigCTO,    gradeMap["L7"], "arjun.sharma@intelliflow.com",   "1980-03-15");

        await db.SaveChangesAsync(ct);

        // ── Level 6 (Directors) ──────────────────────────────────────────────
        var vpEng   = AddEmployee(db, tenantId, company, branch, "IFL-002", "Sarah Mitchell",  deptEng,  desigVPEng,  gradeMap["L6"], "sarah.mitchell@intelliflow.com",  "1985-07-22", cto.Id);
        var dirHR   = AddEmployee(db, tenantId, company, branch, "IFL-003", "Omar Al-Farsi",   deptHR,   desigDirHR,  gradeMap["L6"], "omar.alfarsi@intelliflow.com",    "1983-11-05", cto.Id);
        var finDir  = AddEmployee(db, tenantId, company, branch, "IFL-004", "Chen Wei",        deptFin,  desigFinDir, gradeMap["L6"], "chen.wei@intelliflow.com",        "1979-04-30", cto.Id);
        await db.SaveChangesAsync(ct);

        // Department heads
        deptEng.ManagerEmployeeId  = vpEng.Id;
        deptHR.ManagerEmployeeId   = dirHR.Id;
        deptFin.ManagerEmployeeId  = finDir.Id;

        // ── Level 5 (Managers) ───────────────────────────────────────────────
        var hrMgr    = AddEmployee(db, tenantId, company, branch, "IFL-005", "Priya Sharma",   deptHR,   desigHRMgr,  gradeMap["L5"], "priya.sharma@intelliflow.com",    "1990-02-14", dirHR.Id);
        var engMgr   = AddEmployee(db, tenantId, company, branch, "IFL-006", "Khalid Al-Rashid",deptEng, desigEngMgr, gradeMap["L5"], "khalid.rashid@intelliflow.com",   "1988-09-18", vpEng.Id);
        var salesMgr = AddEmployee(db, tenantId, company, branch, "IFL-007", "Lena Mueller",   deptSales,desigSalesMgr,gradeMap["L5"],"lena.mueller@intelliflow.com",    "1986-06-01", cto.Id);
        var opsMgr   = AddEmployee(db, tenantId, company, branch, "IFL-008", "Raj Patel",      deptOps,  desigOpsMgr, gradeMap["L5"], "raj.patel@intelliflow.com",       "1987-12-20", cto.Id);
        await db.SaveChangesAsync(ct);

        deptSales.ManagerEmployeeId = salesMgr.Id;
        deptOps.ManagerEmployeeId   = opsMgr.Id;

        // ── Level 3 (Seniors) ────────────────────────────────────────────────
        var hrOff    = AddEmployee(db, tenantId, company, branch, "IFL-009", "Fatima Al-Zahra",deptHR,   desigHROff, gradeMap["L3"], "fatima.alzahra@intelliflow.com",  "1994-08-10", hrMgr.Id);
        var srEng1   = AddEmployee(db, tenantId, company, branch, "IFL-010", "James O'Brien",  deptEngBE,desigSrEng, gradeMap["L3"], "james.obrien@intelliflow.com",    "1992-01-25", engMgr.Id);
        var srEng2   = AddEmployee(db, tenantId, company, branch, "IFL-011", "Maya Johnson",   deptEngFE,desigSrEng, gradeMap["L3"], "maya.johnson@intelliflow.com",    "1993-05-03", engMgr.Id);
        await db.SaveChangesAsync(ct);

        // ── Level 2 (Individual Contributors) ───────────────────────────────
        var eng1     = AddEmployee(db, tenantId, company, branch, "IFL-012", "Yuki Tanaka",    deptEngBE,desigEng,   gradeMap["L2"], "yuki.tanaka@intelliflow.com",     "1997-03-11", srEng1.Id);
        var eng2     = AddEmployee(db, tenantId, company, branch, "IFL-013", "Dana Wilkins",   deptEngFE,desigEng,   gradeMap["L2"], "dana.wilkins@intelliflow.com",    "1998-07-29", srEng2.Id);
        var salesExec= AddEmployee(db, tenantId, company, branch, "IFL-014", "Nina Costa",     deptSales,desigSalesExec,gradeMap["L2"],"nina.costa@intelliflow.com",    "1996-11-15", salesMgr.Id);
        var finAcc   = AddEmployee(db, tenantId, company, branch, "IFL-015", "David Kim",      deptFin,  desigFinAcc, gradeMap["L2"], "david.kim@intelliflow.com",       "1995-04-22", finDir.Id);
        await db.SaveChangesAsync(ct);

        // Update department head on sub-departments
        deptEngBE.ManagerEmployeeId = srEng1.Id;
        deptEngFE.ManagerEmployeeId = srEng2.Id;
        await db.SaveChangesAsync(ct);

        // ── ReportingLines ───────────────────────────────────────────────────
        var hierarchyPairs = new (Employee Emp, Employee Mgr)[]
        {
            (vpEng,    cto),
            (dirHR,    cto),
            (finDir,   cto),
            (salesMgr, cto),
            (opsMgr,   cto),
            (hrMgr,    dirHR),
            (engMgr,   vpEng),
            (hrOff,    hrMgr),
            (srEng1,   engMgr),
            (srEng2,   engMgr),
            (eng1,     srEng1),
            (eng2,     srEng2),
            (salesExec,salesMgr),
            (finAcc,   finDir),
        };
        foreach (var (emp, mgr) in hierarchyPairs)
        {
            db.ReportingLines.Add(new ReportingLine
            {
                TenantId = tenantId,
                EmployeeId = emp.Id,
                ManagerEmployeeId = mgr.Id,
                RelationshipType = "SolidLine",
                EffectiveFrom = emp.JoiningDate,
                IsPrimary = true,
                IsActive = true,
            });
        }
        // Add a dotted-line: HR Officer also reports to VP Eng for engineering HR matters
        db.ReportingLines.Add(new ReportingLine
        {
            TenantId = tenantId,
            EmployeeId = hrOff.Id,
            ManagerEmployeeId = vpEng.Id,
            RelationshipType = "DottedLine",
            EffectiveFrom = hrOff.JoiningDate,
            IsPrimary = false,
            IsActive = true,
        });
        await db.SaveChangesAsync(ct);

        // ── Approval policies (Leave: Manager → HR Director) ─────────────────
        var leavePolicy = new ApprovalPolicy
        {
            TenantId = tenantId,
            WorkflowType = "Leave",
            Name = "Standard Leave Approval",
            IsDefault = true,
            IsActive = true,
        };
        db.ApprovalPolicies.Add(leavePolicy);
        await db.SaveChangesAsync(ct);
        db.ApprovalPolicySteps.AddRange(
            new ApprovalPolicyStep { TenantId = tenantId, PolicyId = leavePolicy.Id, StepOrder = 1, StepName = "Manager Approval", ApproverType = "Manager", IsFinalStep = false },
            new ApprovalPolicyStep { TenantId = tenantId, PolicyId = leavePolicy.Id, StepOrder = 2, StepName = "HR Director Sign-Off", ApproverType = "HR", IsFinalStep = true }
        );
        await db.SaveChangesAsync(ct);

        // ── QA scenario employees: probation, contract, Saudi, terminated ────────
        // Adds diversity of statuses and nationalities needed for real workflow testing.

        // 16 — Saudi engineer on probation (Leave/Overtime approval flows differently during probation)
        var saudiEng = new Employee
        {
            TenantId = tenantId, CompanyId = company.Id, BranchId = branch.Id,
            EmployeeCode = "IFL-016", FullName = "Abdullah Al-Otaibi",
            WorkEmail = "abdullah.alotaibi@intelliflow.com",
            DepartmentId = deptEngBE.Id, Department = deptEngBE.NameEn,
            DesignationId = desigEng.Id, Designation = desigEng.TitleEn,
            GradeId = gradeMap["L2"].Id, Grade = gradeMap["L2"].Code,
            JobTitle = desigEng.TitleEn, ManagerEmployeeId = srEng1.Id,
            Status = "Probation", JoiningDate = DateTime.UtcNow.AddDays(-60),
            EmploymentType = "Full-time", ContractType = "Unlimited",
            Nationality = "Saudi", IqamaNumber = string.Empty, // local national
        };
        db.Employees.Add(saudiEng);

        // 17 — Contract employee (fixed-term, Sales dept)
        var contractEmp = new Employee
        {
            TenantId = tenantId, CompanyId = company.Id, BranchId = branch.Id,
            EmployeeCode = "IFL-017", FullName = "Marco Rossi",
            WorkEmail = "marco.rossi@intelliflow.com",
            DepartmentId = deptSales.Id, Department = deptSales.NameEn,
            DesignationId = desigSalesExec.Id, Designation = desigSalesExec.TitleEn,
            GradeId = gradeMap["L2"].Id, Grade = gradeMap["L2"].Code,
            JobTitle = desigSalesExec.TitleEn, ManagerEmployeeId = salesMgr.Id,
            Status = "Active", JoiningDate = DateTime.UtcNow.AddDays(-180),
            EmploymentType = "Contract", ContractType = "Fixed-term",
            Nationality = "Italian",
        };
        db.Employees.Add(contractEmp);

        // 18 — Resigned employee (no longer in active headcount, but workflows may reference history)
        var resignedEmp = new Employee
        {
            TenantId = tenantId, CompanyId = company.Id, BranchId = branch.Id,
            EmployeeCode = "IFL-018", FullName = "Sophie Laurent",
            WorkEmail = "sophie.laurent@intelliflow.com",
            DepartmentId = deptFin.Id, Department = deptFin.NameEn,
            DesignationId = desigFinAcc.Id, Designation = desigFinAcc.TitleEn,
            GradeId = gradeMap["L2"].Id, Grade = gradeMap["L2"].Code,
            JobTitle = desigFinAcc.TitleEn, ManagerEmployeeId = finDir.Id,
            Status = "Resigned", JoiningDate = DateTime.UtcNow.AddDays(-730),
            EmploymentType = "Full-time", ContractType = "Unlimited",
            Nationality = "French",
        };
        db.Employees.Add(resignedEmp);

        // 19 — Saudi female HR officer on confirmed status (for GOSI/Qiwa readiness scenarios)
        var saudiHR = new Employee
        {
            TenantId = tenantId, CompanyId = company.Id, BranchId = branch.Id,
            EmployeeCode = "IFL-019", FullName = "Noura Al-Ghamdi",
            WorkEmail = "noura.alghamdi@intelliflow.com",
            DepartmentId = deptHR.Id, Department = deptHR.NameEn,
            DesignationId = desigHROff.Id, Designation = desigHROff.TitleEn,
            GradeId = gradeMap["L3"].Id, Grade = gradeMap["L3"].Code,
            JobTitle = desigHROff.TitleEn, ManagerEmployeeId = hrMgr.Id,
            Status = "Confirmed", JoiningDate = DateTime.UtcNow.AddDays(-400),
            EmploymentType = "Full-time", ContractType = "Unlimited",
            Nationality = "Saudi", IqamaNumber = string.Empty, Gender = "Female",
        };
        db.Employees.Add(saudiHR);

        await db.SaveChangesAsync(ct);

        // Overtime approval policy (all departments, all grades — default)
        var overtimePolicy = new ApprovalPolicy
        {
            TenantId = tenantId,
            WorkflowType = "Overtime",
            Name = "Standard Overtime Approval",
            IsDefault = true,
            IsActive = true,
        };
        db.ApprovalPolicies.Add(overtimePolicy);
        await db.SaveChangesAsync(ct);
        db.ApprovalPolicySteps.AddRange(
            new ApprovalPolicyStep { TenantId = tenantId, PolicyId = overtimePolicy.Id, StepOrder = 1, StepName = "Manager Approval", ApproverType = "Manager", IsFinalStep = true }
        );
        await db.SaveChangesAsync(ct);

        logger.LogInformation("DemoDataSeeder: seeded full org structure for IntelliFlow ({Count} employees, including QA scenarios).", 19);
    }

    private static async Task SeedMinimalOrgAsync(
        ZayraDbContext db, Guid tenantId, Company company, Branch branch, CancellationToken ct)
    {
        // Evostel gets a small flat structure: 3 grades + 2 departments + 4 employees
        var grades = new[]
        {
            new Grade { TenantId = tenantId, Code = "S1", Name = "Level 1 – Staff",   Level = 1 },
            new Grade { TenantId = tenantId, Code = "S2", Name = "Level 2 – Senior",  Level = 2 },
            new Grade { TenantId = tenantId, Code = "M1", Name = "Level 3 – Manager", Level = 3 },
        };
        db.Grades.AddRange(grades);
        await db.SaveChangesAsync(ct);
        var gMap = grades.ToDictionary(g => g.Code);

        var deptMgmt = new Department { TenantId = tenantId, BranchId = branch.Id, Code = "MGT",  NameEn = "Management",  SortOrder = 0 };
        var deptOps  = new Department { TenantId = tenantId, BranchId = branch.Id, Code = "OPS",  NameEn = "Operations",  SortOrder = 1 };
        db.Departments.AddRange(deptMgmt, deptOps);
        await db.SaveChangesAsync(ct);

        var desigMgr = new Designation { TenantId = tenantId, Code = "MGR",  TitleEn = "Manager",  GradeId = gMap["M1"].Id, IsManagerRole = true,  LevelRank = 30 };
        var desigStf = new Designation { TenantId = tenantId, Code = "STAFF", TitleEn = "Staff",   GradeId = gMap["S1"].Id, IsManagerRole = false, LevelRank = 10 };
        db.Designations.AddRange(desigMgr, desigStf);
        await db.SaveChangesAsync(ct);

        var ceo = AddEmployee(db, tenantId, company, branch, "EVS-001", "Tom Reyes",   deptMgmt, desigMgr, gMap["M1"], "tom.reyes@evostel.com",   "1982-05-10");
        await db.SaveChangesAsync(ct);

        var emp1 = AddEmployee(db, tenantId, company, branch, "EVS-002", "Lena Müller", deptOps, desigStf, gMap["S1"], "lena.mueller@evostel.com", "1994-03-20", ceo.Id);
        var emp2 = AddEmployee(db, tenantId, company, branch, "EVS-003", "Raj Patel",   deptOps, desigStf, gMap["S1"], "raj.patel@evostel.com",    "1995-08-15", ceo.Id);
        await db.SaveChangesAsync(ct);

        db.ReportingLines.Add(new ReportingLine { TenantId = tenantId, EmployeeId = emp1.Id, ManagerEmployeeId = ceo.Id, RelationshipType = "SolidLine", EffectiveFrom = emp1.JoiningDate, IsPrimary = true, IsActive = true });
        db.ReportingLines.Add(new ReportingLine { TenantId = tenantId, EmployeeId = emp2.Id, ManagerEmployeeId = ceo.Id, RelationshipType = "SolidLine", EffectiveFrom = emp2.JoiningDate, IsPrimary = true, IsActive = true });
        await db.SaveChangesAsync(ct);
    }

    private static Employee AddEmployee(
        ZayraDbContext db,
        Guid tenantId,
        Company company,
        Branch branch,
        string code,
        string name,
        Department dept,
        Designation desig,
        Grade grade,
        string email,
        string dob,
        int? managerId = null)
    {
        var joiningDate = new DateTime(2022, 1, 1).AddDays(new Random(code.GetHashCode()).Next(0, 730));
        var emp = new Employee
        {
            TenantId          = tenantId,
            CompanyId         = company.Id,
            BranchId          = branch.Id,
            EmployeeCode      = code,
            FullName          = name,
            EnglishName       = name,
            WorkEmail         = email,
            DepartmentId      = dept.Id,
            Department        = dept.NameEn,
            DesignationId     = desig.Id,
            Designation       = desig.TitleEn,
            GradeId           = grade.Id,
            Grade             = grade.Code,
            JobTitle          = desig.TitleEn,
            ManagerEmployeeId = managerId,
            Status            = EmployeeStatuses.Active,
            JoiningDate       = joiningDate,
            EmploymentType    = "Full-time",
            ContractType      = "Unlimited",
        };
        db.Employees.Add(emp);
        return emp;
    }

    // ── Spec records ──────────────────────────────────────────────────────────

    private sealed class DemoTenantSpec
    {
        public string Name { get; init; } = null!;
        public string Slug { get; init; } = null!;
        public string Plan { get; init; } = null!;
        public string Status { get; init; } = null!;
        public int MaxEmployees { get; init; }
        public int MaxUsers { get; init; }
        public decimal MonthlyAmount { get; init; }
        public string CurrencyCode { get; init; } = "USD";
        public string BillingEmail { get; init; } = null!;
        public DateTime? ExpiresAtUtc { get; init; }
        public Dictionary<string, bool> Features { get; init; } = new();
        public DemoUserSpec[] Users { get; init; } = Array.Empty<DemoUserSpec>();
        public bool SeedOrgStructure { get; init; }
    }

    private sealed record DemoUserSpec(string RoleName, string FullName, string Email);
}
