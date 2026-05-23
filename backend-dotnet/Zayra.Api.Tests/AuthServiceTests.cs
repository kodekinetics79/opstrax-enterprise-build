using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Zayra.Api.Application.Auth;
using Zayra.Api.Data;
using Zayra.Api.Domain.Entities;
using Zayra.Api.Infrastructure.Audit;
using Zayra.Api.Infrastructure.Auth;

namespace Zayra.Api.Tests;

public class AuthServiceTests
{
    [Fact]
    public void PasswordHasher_VerifiesValidPassword_AndRejectsInvalidPassword()
    {
        var hasher = new Pbkdf2PasswordHasher();
        var hash = hasher.Hash("CorrectHorse123!");

        Assert.True(hasher.Verify("CorrectHorse123!", hash));
        Assert.False(hasher.Verify("wrong-password", hash));
    }

    [Fact]
    public async Task LoginAndRefresh_IssueTokensAndRotateRefreshToken()
    {
        await using var db = CreateDb();
        var hasher = new Pbkdf2PasswordHasher();
        var jwt = Options.Create(new JwtOptions
        {
            Issuer = "Zayra.Tests",
            Audience = "Zayra.Tests",
            SigningKey = "TEST_SIGNING_KEY_WITH_MORE_THAN_64_CHARACTERS_FOR_AUTH_TESTS",
            AccessTokenMinutes = 30,
            RefreshTokenDays = 7
        });
        var tokenService = new JwtTokenService(jwt);
        var audit = new AuditService(db);
        var auth = new AuthService(db, hasher, tokenService, audit, jwt);
        await SeedAuthUser(db, hasher);

        var login = await auth.LoginAsync(new LoginRequest("admin@zayra.local", "ChangeMe123!", "zayra"), new RequestContext("127.0.0.1", "tests"), CancellationToken.None);
        var refresh = await auth.RefreshAsync(new RefreshTokenRequest(login.RefreshToken), new RequestContext("127.0.0.1", "tests"), CancellationToken.None);

        Assert.False(string.IsNullOrWhiteSpace(login.AccessToken));
        Assert.False(string.IsNullOrWhiteSpace(refresh.AccessToken));
        Assert.NotEqual(login.RefreshToken, refresh.RefreshToken);
        Assert.Equal("zayra", login.User.TenantSlug);
        Assert.Contains("Admin", login.User.Roles);
        Assert.Contains("dashboard.read", login.User.Permissions);
        Assert.Equal(2, await db.RefreshTokens.CountAsync());
        Assert.Equal(1, await db.RefreshTokens.CountAsync(x => x.RevokedAtUtc != null));
        Assert.True(await db.AuditLogs.AnyAsync(x => x.Action == "auth.login"));
        Assert.True(await db.AuditLogs.AnyAsync(x => x.Action == "auth.refresh"));
    }

    private static ZayraDbContext CreateDb()
    {
        var options = new DbContextOptionsBuilder<ZayraDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        return new ZayraDbContext(options);
    }

    private static async Task SeedAuthUser(ZayraDbContext db, IPasswordHasher hasher)
    {
        var tenant = new Tenant { Id = Guid.NewGuid(), Name = "Zayra HQ", Slug = "zayra" };
        var permission = new Permission { Id = Guid.NewGuid(), Key = "dashboard.read", Module = "Dashboard", Description = "Read dashboard" };
        var role = new Role { Id = Guid.NewGuid(), TenantId = tenant.Id, Tenant = tenant, Name = "Admin", NormalizedName = "ADMIN", Description = "Admin" };
        var user = new User
        {
            Id = Guid.NewGuid(),
            TenantId = tenant.Id,
            Tenant = tenant,
            Email = "admin@zayra.local",
            NormalizedEmail = "ADMIN@ZAYRA.LOCAL",
            FullName = "Zayra Admin",
            PasswordHash = hasher.Hash("ChangeMe123!")
        };
        db.Tenants.Add(tenant);
        db.Permissions.Add(permission);
        db.Roles.Add(role);
        db.RolePermissions.Add(new RolePermission { RoleId = role.Id, PermissionId = permission.Id });
        db.Users.Add(user);
        db.UserRoles.Add(new UserRole { UserId = user.Id, RoleId = role.Id });
        await db.SaveChangesAsync();
    }
}
