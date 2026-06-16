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
        logger.LogInformation(
            "DemoDataSeeder: seeded tenant '{Slug}' ({Plan}/{Status}) with {Count} users.",
            spec.Slug, spec.Plan, spec.Status, spec.Users.Length);
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
    }

    private sealed record DemoUserSpec(string RoleName, string FullName, string Email);
}
