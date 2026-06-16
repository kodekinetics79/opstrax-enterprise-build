using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Zayra.Api.Data;
using Zayra.Api.Domain.Entities;

namespace Zayra.Api.Tests.Security;

/// <summary>
/// Verifies that role and permission assignments are correctly scoped per tenant
/// and that cross-tenant role leakage is impossible.
///
/// RBAC enforcement in this system works in two layers:
///   1. JWT claims carry the user's permissions for the request (issued at login).
///   2. The DB is the authority — these tests verify the DB layer cannot produce
///      cross-tenant role exposure.
/// </summary>
public class RbacAuthorizationTests
{
    private static ZayraDbContext CreateDb()
    {
        var opts = new DbContextOptionsBuilder<ZayraDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        return new ZayraDbContext(opts);
    }

    // ── Role isolation by tenant ──────────────────────────────────────────────

    [Fact]
    public async Task Roles_AreIsolatedByTenant()
    {
        await using var db = CreateDb();
        var tenantA = Guid.NewGuid();
        var tenantB = Guid.NewGuid();

        db.Roles.AddRange(
            new Role { Id = Guid.NewGuid(), TenantId = tenantA, Name = "Admin",    NormalizedName = "ADMIN",    Description = "Admin A" },
            new Role { Id = Guid.NewGuid(), TenantId = tenantA, Name = "Employee", NormalizedName = "EMPLOYEE", Description = "Employee A" },
            new Role { Id = Guid.NewGuid(), TenantId = tenantB, Name = "Admin",    NormalizedName = "ADMIN",    Description = "Admin B" });
        await db.SaveChangesAsync();

        var tenantARoles = await db.Roles.Where(r => r.TenantId == tenantA).ToListAsync();
        var tenantBRoles = await db.Roles.Where(r => r.TenantId == tenantB).ToListAsync();

        tenantARoles.Should().HaveCount(2);
        tenantBRoles.Should().HaveCount(1);
        tenantARoles.Should().NotContain(r => r.TenantId == tenantB);
    }

    [Fact]
    public async Task UserRoles_DoNotCrossTenantsWhenFilteredCorrectly()
    {
        await using var db = CreateDb();
        var tenantA = Guid.NewGuid();
        var tenantB = Guid.NewGuid();

        var roleA = new Role { Id = Guid.NewGuid(), TenantId = tenantA, Name = "Admin", NormalizedName = "ADMIN", Description = "A" };
        var roleB = new Role { Id = Guid.NewGuid(), TenantId = tenantB, Name = "Admin", NormalizedName = "ADMIN", Description = "B" };
        db.Roles.AddRange(roleA, roleB);

        var userA = new User { TenantId = tenantA, Email = "a@a.com", NormalizedEmail = "A@A.COM", FullName = "User A", PasswordHash = "hash" };
        var userB = new User { TenantId = tenantB, Email = "b@b.com", NormalizedEmail = "B@B.COM", FullName = "User B", PasswordHash = "hash" };
        db.Users.AddRange(userA, userB);
        await db.SaveChangesAsync();

        db.UserRoles.AddRange(
            new UserRole { UserId = userA.Id, RoleId = roleA.Id },
            new UserRole { UserId = userB.Id, RoleId = roleB.Id });
        await db.SaveChangesAsync();

        // Tenant A query: users + their roles scoped to tenantA
        var tenantAUserRoles = await db.Users
            .Where(u => u.TenantId == tenantA)
            .SelectMany(u => u.UserRoles)
            .Select(ur => ur.RoleId)
            .ToListAsync();

        tenantAUserRoles.Should().Contain(roleA.Id);
        tenantAUserRoles.Should().NotContain(roleB.Id);
    }

    // ── Permission isolation ──────────────────────────────────────────────────

    [Fact]
    public async Task Permissions_CannotLeakFromTenantAToTenantB_ViaRoles()
    {
        await using var db = CreateDb();
        var tenantA = Guid.NewGuid();
        var tenantB = Guid.NewGuid();

        var sensitivePermission = new Permission
        {
            Id          = Guid.NewGuid(),
            Key         = "sensitive_data.view",
            Module      = "Security",
            Description = "View sensitive employee data"
        };
        db.Permissions.Add(sensitivePermission);

        var roleA = new Role { Id = Guid.NewGuid(), TenantId = tenantA, Name = "HR Director", NormalizedName = "HR DIRECTOR", Description = "" };
        var roleB = new Role { Id = Guid.NewGuid(), TenantId = tenantB, Name = "Employee",    NormalizedName = "EMPLOYEE",    Description = "" };
        db.Roles.AddRange(roleA, roleB);
        await db.SaveChangesAsync();

        // Assign sensitive permission ONLY to Tenant A's HR Director
        db.RolePermissions.Add(new RolePermission { RoleId = roleA.Id, PermissionId = sensitivePermission.Id });
        await db.SaveChangesAsync();

        // Tenant B's roles should not have the sensitive permission
        var tenantBRoleIds = await db.Roles.Where(r => r.TenantId == tenantB).Select(r => r.Id).ToListAsync();
        var tenantBPermissions = await db.RolePermissions
            .Where(rp => tenantBRoleIds.Contains(rp.RoleId))
            .Select(rp => rp.PermissionId)
            .ToListAsync();

        tenantBPermissions.Should().NotContain(sensitivePermission.Id,
            "Tenant B must not inherit Tenant A's role permissions");
    }

    // ── Employee role (limited permissions) ──────────────────────────────────

    [Fact]
    public async Task EmployeeRole_HasLimitedPermissions_CannotEscalate()
    {
        await using var db = CreateDb();
        var tenantId = Guid.NewGuid();

        var viewOwn    = new Permission { Id = Guid.NewGuid(), Key = "salary.view_own",        Module = "Salary",   Description = "Own salary only" };
        var viewAll    = new Permission { Id = Guid.NewGuid(), Key = "salary.view_all",         Module = "Salary",   Description = "All salaries" };
        var sensitiveP = new Permission { Id = Guid.NewGuid(), Key = "sensitive_data.view",     Module = "Security", Description = "Sensitive data" };
        db.Permissions.AddRange(viewOwn, viewAll, sensitiveP);

        var employeeRole  = new Role { Id = Guid.NewGuid(), TenantId = tenantId, Name = "Employee",    NormalizedName = "EMPLOYEE",    Description = "" };
        var hrDirectorRole = new Role { Id = Guid.NewGuid(), TenantId = tenantId, Name = "HR Director", NormalizedName = "HR DIRECTOR", Description = "" };
        db.Roles.AddRange(employeeRole, hrDirectorRole);
        await db.SaveChangesAsync();

        // Employee only gets view_own
        db.RolePermissions.Add(new RolePermission { RoleId = employeeRole.Id,   PermissionId = viewOwn.Id });
        // HR Director gets view_all + sensitive
        db.RolePermissions.Add(new RolePermission { RoleId = hrDirectorRole.Id, PermissionId = viewAll.Id });
        db.RolePermissions.Add(new RolePermission { RoleId = hrDirectorRole.Id, PermissionId = sensitiveP.Id });
        await db.SaveChangesAsync();

        var employeePermissions = await db.RolePermissions
            .Where(rp => rp.RoleId == employeeRole.Id)
            .Select(rp => rp.PermissionId)
            .ToListAsync();

        employeePermissions.Should().Contain(viewOwn.Id);
        employeePermissions.Should().NotContain(viewAll.Id,    "Employee must not see all salaries");
        employeePermissions.Should().NotContain(sensitiveP.Id, "Employee must not access sensitive data");
    }

    // ── User-in-wrong-tenant role lookup ─────────────────────────────────────

    [Fact]
    public async Task User_LookingUpRole_InDifferentTenant_GetsNoResults()
    {
        await using var db = CreateDb();
        var tenantA  = Guid.NewGuid();
        var tenantB  = Guid.NewGuid();
        var userId   = Guid.NewGuid();

        var roleInA = new Role { Id = Guid.NewGuid(), TenantId = tenantA, Name = "Admin", NormalizedName = "ADMIN", Description = "" };
        db.Roles.Add(roleInA);
        await db.SaveChangesAsync();

        // User in Tenant A is assigned the Admin role
        var userA = new User { Id = userId, TenantId = tenantA, Email = "a@a.com", NormalizedEmail = "A@A.COM", FullName = "A", PasswordHash = "hash" };
        db.Users.Add(userA);
        await db.SaveChangesAsync();
        db.UserRoles.Add(new UserRole { UserId = userId, RoleId = roleInA.Id });
        await db.SaveChangesAsync();

        // If Tenant B tries to look up this user's roles scoped to THEIR tenant — must be empty
        var tenantBUserRoles = await db.Users
            .Where(u => u.TenantId == tenantB && u.Id == userId)
            .SelectMany(u => u.UserRoles)
            .ToListAsync();

        tenantBUserRoles.Should().BeEmpty("user scoped to Tenant A must have no roles visible from Tenant B's query");
    }

    // ── Seat limit contract ───────────────────────────────────────────────────

    [Fact]
    public async Task UserCount_CanBeQueriedForSeatLimitEnforcement()
    {
        await using var db = CreateDb();
        var tenantId = Guid.NewGuid();

        // Simulate 5 active users
        for (int i = 1; i <= 5; i++)
        {
            db.Users.Add(new User
            {
                TenantId       = tenantId,
                Email          = $"user{i}@test.com",
                NormalizedEmail = $"USER{i}@TEST.COM",
                FullName       = $"User {i}",
                PasswordHash   = "hash",
                IsActive       = true,
            });
        }
        // Plus one deleted user — must not count toward the seat limit
        db.Users.Add(new User
        {
            TenantId       = tenantId,
            Email          = "deleted@test.com",
            NormalizedEmail = "DELETED@TEST.COM",
            FullName       = "Deleted User",
            PasswordHash   = "hash",
            IsActive       = false,
            IsDeleted      = true,
        });
        await db.SaveChangesAsync();

        var activeCount = await db.Users
            .CountAsync(u => u.TenantId == tenantId && !u.IsDeleted);

        activeCount.Should().Be(5, "soft-deleted users must not count against seat limits");
    }
}
