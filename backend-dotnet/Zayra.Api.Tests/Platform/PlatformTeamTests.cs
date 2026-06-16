using FluentAssertions;
using Microsoft.AspNetCore.Mvc;
using Zayra.Api.Controllers;   // CreatePlatformUserRequest, UpdatePlatformUserRequest (in PlatformController.cs)
using Zayra.Api.Models;

namespace Zayra.Api.Tests.Platform;

public class PlatformTeamTests : PlatformTestBase
{
    // ── List team ──────────────────────────────────────────────────────────────

    [Fact]
    public async Task ListTeam_WhenEmpty_Returns200WithEmptyArray()
    {
        await using var db  = CreateDb();
        var controller      = CreateController(db);

        var result = await controller.ListTeam(CancellationToken.None);

        var ok = result.Should().BeOfType<OkObjectResult>().Subject;
        System.Text.Json.JsonSerializer.Serialize(ok.Value).Should().Be("[]");
    }

    [Fact]
    public async Task ListTeam_WithSeededUser_ReturnsTheMember()
    {
        await using var db = CreateDb();
        var hasher         = new Zayra.Api.Infrastructure.Auth.Pbkdf2PasswordHasher();
        db.PlatformUsers.Add(new PlatformUser
        {
            Email        = "finance@platform.local",
            FullName     = "Finance User",
            PasswordHash = hasher.Hash("Finance123!"),
            Role         = PlatformRoles.Finance,
            IsActive     = true,
            CreatedAtUtc = DateTime.UtcNow
        });
        await db.SaveChangesAsync();

        var controller = CreateController(db);

        var result = await controller.ListTeam(CancellationToken.None);

        var ok   = result.Should().BeOfType<OkObjectResult>().Subject;
        var json = System.Text.Json.JsonSerializer.Serialize(ok.Value);
        json.Should().Contain("finance@platform.local");
        json.Should().Contain("Finance");
    }

    // ── Create team member ─────────────────────────────────────────────────────

    [Fact]
    public async Task CreateTeamMember_WithValidBody_Returns200()
    {
        await using var db  = CreateDb();
        var controller      = CreateController(db);
        var req = new CreatePlatformUserRequest(
            Email:    "support@platform.local",
            FullName: "Support Agent",
            Password: "Support123!",
            Role:     PlatformRoles.Support);

        var result = await controller.CreateTeamMember(req, CancellationToken.None);

        result.Should().BeOfType<OkObjectResult>();
        var json = System.Text.Json.JsonSerializer.Serialize(((OkObjectResult)result).Value);
        json.Should().Contain("support@platform.local");
    }

    [Fact]
    public async Task CreateTeamMember_WithDuplicateEmail_Returns409Conflict()
    {
        await using var db = CreateDb();
        var hasher         = new Zayra.Api.Infrastructure.Auth.Pbkdf2PasswordHasher();
        db.PlatformUsers.Add(new PlatformUser
        {
            Email        = "duplicate@platform.local",
            FullName     = "Existing",
            PasswordHash = hasher.Hash("ExistingPass123!"),
            Role         = PlatformRoles.Admin,
            IsActive     = true
        });
        await db.SaveChangesAsync();

        var controller = CreateController(db);
        var req = new CreatePlatformUserRequest(
            Email:    "duplicate@platform.local",
            FullName: "Another",
            Password: "AnotherPass123!",
            Role:     PlatformRoles.Admin);

        var result = await controller.CreateTeamMember(req, CancellationToken.None);

        result.Should().BeOfType<ConflictObjectResult>();
    }

    [Fact]
    public async Task CreateTeamMember_WithInvalidEmail_Returns400()
    {
        await using var db  = CreateDb();
        var controller      = CreateController(db);
        var req = new CreatePlatformUserRequest(
            Email:    "not-an-email",
            FullName: null,
            Password: "ValidPass123!",
            Role:     null);

        var result = await controller.CreateTeamMember(req, CancellationToken.None);

        result.Should().BeOfType<BadRequestObjectResult>();
    }

    [Fact]
    public async Task CreateTeamMember_WithShortPassword_Returns400()
    {
        await using var db  = CreateDb();
        var controller      = CreateController(db);
        var req = new CreatePlatformUserRequest(
            Email:    "short@platform.local",
            FullName: null,
            Password: "123",
            Role:     null);

        var result = await controller.CreateTeamMember(req, CancellationToken.None);

        result.Should().BeOfType<BadRequestObjectResult>();
    }

    [Fact]
    public async Task CreateTeamMember_WithInvalidRole_Returns400()
    {
        await using var db  = CreateDb();
        var controller      = CreateController(db);
        var req = new CreatePlatformUserRequest(
            Email:    "role@platform.local",
            FullName: null,
            Password: "ValidPass123!",
            Role:     "SuperDuperRole");   // not in PlatformRoles.All

        var result = await controller.CreateTeamMember(req, CancellationToken.None);

        result.Should().BeOfType<BadRequestObjectResult>();
    }

    // ── Update team member ─────────────────────────────────────────────────────

    [Fact]
    public async Task UpdateTeamMember_WithValidRole_Returns200WithUpdatedRole()
    {
        await using var db = CreateDb();
        var hasher         = new Zayra.Api.Infrastructure.Auth.Pbkdf2PasswordHasher();
        var user = new PlatformUser
        {
            Id           = Guid.NewGuid(),
            Email        = "update@platform.local",
            FullName     = "To Update",
            PasswordHash = hasher.Hash("UpdatePass123!"),
            Role         = PlatformRoles.Support,
            IsActive     = true
        };
        db.PlatformUsers.Add(user);
        await db.SaveChangesAsync();

        var controller = CreateController(db);
        var req        = new UpdatePlatformUserRequest(Role: PlatformRoles.Finance, IsActive: null, FullName: null);

        var result = await controller.UpdateTeamMember(user.Id, req, CancellationToken.None);

        var ok   = result.Should().BeOfType<OkObjectResult>().Subject;
        var json = System.Text.Json.JsonSerializer.Serialize(ok.Value);
        json.Should().Contain("Finance");
    }

    [Fact]
    public async Task UpdateTeamMember_ForNonExistentUser_Returns404()
    {
        await using var db  = CreateDb();
        var controller      = CreateController(db);
        var req             = new UpdatePlatformUserRequest(Role: PlatformRoles.Admin, IsActive: null, FullName: null);

        var result = await controller.UpdateTeamMember(Guid.NewGuid(), req, CancellationToken.None);

        result.Should().BeOfType<NotFoundObjectResult>();
    }

    [Fact]
    public async Task UpdateTeamMember_Deactivate_SetsIsActiveFalse()
    {
        await using var db = CreateDb();
        var hasher         = new Zayra.Api.Infrastructure.Auth.Pbkdf2PasswordHasher();
        var user = new PlatformUser
        {
            Id           = Guid.NewGuid(),
            Email        = "deactivate@platform.local",
            FullName     = "Active User",
            PasswordHash = hasher.Hash("ActivePass123!"),
            Role         = PlatformRoles.Admin,
            IsActive     = true
        };
        db.PlatformUsers.Add(user);
        await db.SaveChangesAsync();

        var controller = CreateController(db);
        var req        = new UpdatePlatformUserRequest(Role: null, IsActive: false, FullName: null);

        var result = await controller.UpdateTeamMember(user.Id, req, CancellationToken.None);

        result.Should().BeOfType<OkObjectResult>();
        var updated = await db.PlatformUsers.FindAsync(user.Id);
        updated!.IsActive.Should().BeFalse();
    }
}
