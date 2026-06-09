using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Zayra.Api.Application.Auth;
using Zayra.Api.Data;
using Zayra.Api.Domain.Entities;
using Zayra.Api.Models;

namespace Zayra.Api.Infrastructure.Auth;

public class AuthService : IAuthService
{
    private readonly ZayraDbContext _db;
    private readonly IPasswordHasher _passwordHasher;
    private readonly ITokenService _tokenService;
    private readonly IAuditService _auditService;
    private readonly JwtOptions _jwtOptions;
    private readonly ILogger<AuthService> _log;

    public AuthService(ZayraDbContext db, IPasswordHasher passwordHasher, ITokenService tokenService, IAuditService auditService, IOptions<JwtOptions> jwtOptions, ILogger<AuthService> log)
    {
        _db = db;
        _passwordHasher = passwordHasher;
        _tokenService = tokenService;
        _auditService = auditService;
        _jwtOptions = jwtOptions.Value;
        _log = log;
    }

    public async Task<AuthResponse> LoginAsync(LoginRequest request, RequestContext context, CancellationToken cancellationToken)
    {
        var user = await LoadUserGraph(request.Email, request.TenantSlug, cancellationToken);

        string? failReason = null;
        if (user is null)                                           failReason = "user_not_found";
        else if (user.Tenant is null)                              failReason = "tenant_not_loaded";
        else if (!user.IsActive)                                   failReason = "user_inactive";
        else if (!user.Tenant.IsActive)                            failReason = "tenant_inactive";
        else if (IsNoLogin(user))                                  failReason = "access_mode_no_login";
        else if (RequiresPasswordSetup(user))                      failReason = "requires_password_setup";
        else if (!_passwordHasher.Verify(request.Password, user.PasswordHash)) failReason = "password_mismatch";

        if (failReason is not null)
        {
            _log.LogWarning("Login failed for {Email} / tenant={Slug}: {Reason}", request.Email, request.TenantSlug, failReason);
            await _auditService.WriteAsync("auth.login_failed", "User", null, context, $"{{\"email\":\"{request.Email}\",\"reason\":\"{failReason}\"}}", cancellationToken);
            throw new UnauthorizedAccessException("Invalid email, password, or tenant.");
        }

        user.LastLoginAtUtc = DateTime.UtcNow;
        user.UpdatedAtUtc = DateTime.UtcNow;
        var refreshToken = AddRefreshToken(user, context);
        await _db.SaveChangesAsync(cancellationToken);
        await _auditService.WriteAsync("auth.login", "User", user.Id.ToString(), context with { UserId = user.Id, TenantId = user.TenantId }, null, cancellationToken);
        return BuildAuthResponse(user, refreshToken);
    }

    public async Task<AuthResponse> RefreshAsync(RefreshTokenRequest request, RequestContext context, CancellationToken cancellationToken)
    {
        var tokenHash = _tokenService.HashToken(request.RefreshToken);
        var storedToken = await _db.RefreshTokens
            .Include(x => x.User).ThenInclude(x => x!.Tenant)
            .Include(x => x.User).ThenInclude(x => x!.UserRoles).ThenInclude(x => x.Role).ThenInclude(x => x!.RolePermissions).ThenInclude(x => x.Permission)
            .FirstOrDefaultAsync(x => x.TokenHash == tokenHash, cancellationToken);

        if (storedToken?.User is null || storedToken.User.Tenant is null || !storedToken.IsActive || !storedToken.User.IsActive || !storedToken.User.Tenant.IsActive || IsNoLogin(storedToken.User))
        {
            throw new UnauthorizedAccessException("Refresh token is invalid or expired.");
        }

        var newRefreshToken = AddRefreshToken(storedToken.User, context);
        storedToken.RevokedAtUtc = DateTime.UtcNow;
        storedToken.RevokedByIp = context.IpAddress;
        storedToken.ReplacedByTokenHash = _tokenService.HashToken(newRefreshToken);
        await _db.SaveChangesAsync(cancellationToken);
        await _auditService.WriteAsync("auth.refresh", "RefreshToken", storedToken.Id.ToString(), context with { UserId = storedToken.UserId, TenantId = storedToken.User.TenantId }, null, cancellationToken);
        return BuildAuthResponse(storedToken.User, newRefreshToken);
    }

    public async Task LogoutAsync(LogoutRequest request, RequestContext context, CancellationToken cancellationToken)
    {
        var tokenHash = _tokenService.HashToken(request.RefreshToken);
        var storedToken = await _db.RefreshTokens.Include(x => x.User).FirstOrDefaultAsync(x => x.TokenHash == tokenHash, cancellationToken);
        if (storedToken is null) return;

        storedToken.RevokedAtUtc ??= DateTime.UtcNow;
        storedToken.RevokedByIp = context.IpAddress;
        await _db.SaveChangesAsync(cancellationToken);
        await _auditService.WriteAsync("auth.logout", "RefreshToken", storedToken.Id.ToString(), context with { UserId = storedToken.UserId, TenantId = storedToken.User?.TenantId }, null, cancellationToken);
    }

    public async Task<ForgotPasswordResponse> ForgotPasswordAsync(ForgotPasswordRequest request, RequestContext context, CancellationToken cancellationToken)
    {
        var user = await LoadUserGraph(request.Email, request.TenantSlug, cancellationToken);
        if (user is null || !user.IsActive)
        {
            return new ForgotPasswordResponse("If the account exists, a password reset token has been created.", null, null);
        }

        var resetToken = _tokenService.CreateSecureToken();
        var expiresAt = DateTime.UtcNow.AddHours(1);
        _db.PasswordResetTokens.Add(new PasswordResetToken
        {
            UserId = user.Id,
            TokenHash = _tokenService.HashToken(resetToken),
            ExpiresAtUtc = expiresAt,
            CreatedByIp = context.IpAddress
        });
        await _db.SaveChangesAsync(cancellationToken);
        await _auditService.WriteAsync("auth.password_reset_requested", "User", user.Id.ToString(), context with { UserId = user.Id, TenantId = user.TenantId }, null, cancellationToken);
        return new ForgotPasswordResponse("Password reset token created. Connect this token to email delivery before production launch.", resetToken, expiresAt);
    }

    public async Task ResetPasswordAsync(ResetPasswordRequest request, RequestContext context, CancellationToken cancellationToken)
    {
        var user = await LoadUserGraph(request.Email, request.TenantSlug, cancellationToken) ?? throw new UnauthorizedAccessException("Reset token is invalid or expired.");
        var tokenHash = _tokenService.HashToken(request.ResetToken);
        var resetToken = await _db.PasswordResetTokens.FirstOrDefaultAsync(x => x.UserId == user.Id && x.TokenHash == tokenHash, cancellationToken);
        if (resetToken is null || !resetToken.IsActive) throw new UnauthorizedAccessException("Reset token is invalid or expired.");

        user.PasswordHash = _passwordHasher.Hash(request.NewPassword);
        user.UpdatedAtUtc = DateTime.UtcNow;
        resetToken.UsedAtUtc = DateTime.UtcNow;
        await _db.RefreshTokens.Where(x => x.UserId == user.Id && x.RevokedAtUtc == null).ExecuteUpdateAsync(x => x.SetProperty(t => t.RevokedAtUtc, DateTime.UtcNow), cancellationToken);
        await _db.SaveChangesAsync(cancellationToken);
        await _auditService.WriteAsync("auth.password_reset", "User", user.Id.ToString(), context with { UserId = user.Id, TenantId = user.TenantId }, null, cancellationToken);
    }

    public async Task<AuthResponse> AcceptInvitationAsync(AcceptInvitationRequest request, RequestContext context, CancellationToken cancellationToken)
    {
        var user = await LoadUserGraph(request.Email, request.TenantSlug, cancellationToken) ?? throw new UnauthorizedAccessException("Invitation token is invalid or expired.");
        var tokenHash = _tokenService.HashToken(request.InvitationToken);
        var link = user.EmployeeUserAccounts.FirstOrDefault(x => x.InvitationTokenHash == tokenHash && !x.IsDeleted);
        if (link is null || link.InvitationExpiresAtUtc < DateTime.UtcNow || link.AccessMode == AccessModes.NoLogin)
            throw new UnauthorizedAccessException("Invitation token is invalid or expired.");

        user.PasswordHash = _passwordHasher.Hash(request.NewPassword);
        user.IsActive = true;
        user.IsEmailConfirmed = true;
        user.UpdatedAtUtc = DateTime.UtcNow;
        link.RequiresPasswordSetup = false;
        link.Status = "Active";
        link.InvitationAcceptedAtUtc = DateTime.UtcNow;
        link.UpdatedAtUtc = DateTime.UtcNow;
        var refreshToken = AddRefreshToken(user, context);
        await _db.SaveChangesAsync(cancellationToken);
        await _auditService.WriteAsync("auth.invitation_accepted", "User", user.Id.ToString(), context with { UserId = user.Id, TenantId = user.TenantId }, $"{{\"employeeId\":{link.EmployeeId}}}", cancellationToken);
        return BuildAuthResponse(user, refreshToken);
    }

    public async Task<AuthUserDto?> GetCurrentUserAsync(Guid userId, CancellationToken cancellationToken)
    {
        var user = await LoadUserGraph(userId, cancellationToken);
        return user?.Tenant is null ? null : ToUserDto(user);
    }

    public async Task ChangePasswordAsync(Guid userId, ChangePasswordRequest request, RequestContext context, CancellationToken cancellationToken)
    {
        var user = await _db.Users.FirstOrDefaultAsync(x => x.Id == userId && !x.IsDeleted, cancellationToken)
            ?? throw new UnauthorizedAccessException("User not found.");
        if (!_passwordHasher.Verify(request.CurrentPassword, user.PasswordHash))
            throw new InvalidOperationException("Current password is incorrect.");
        user.PasswordHash = _passwordHasher.Hash(request.NewPassword);
        user.MustChangePassword = false;
        user.LastPasswordChangedAt = DateTime.UtcNow;
        user.UpdatedAtUtc = DateTime.UtcNow;
        await _db.SaveChangesAsync(cancellationToken);
        await _auditService.WriteAsync("auth.password_changed", "User", user.Id.ToString(), context, null, cancellationToken);
    }

    private async Task<User?> LoadUserGraph(string email, string? tenantSlug, CancellationToken cancellationToken)
    {
        var normalizedEmail = Normalize(email);
        var query = _db.Users
            .Include(x => x.Tenant)
            .Include(x => x.UserRoles).ThenInclude(x => x.Role).ThenInclude(x => x!.RolePermissions).ThenInclude(x => x.Permission)
            .Include(x => x.EmployeeUserAccounts)
            .Include(x => x.PermissionOverrides)
            .Where(x => x.NormalizedEmail == normalizedEmail && !x.IsDeleted);
        if (!string.IsNullOrWhiteSpace(tenantSlug)) query = query.Where(x => x.Tenant!.Slug == tenantSlug.Trim().ToLowerInvariant());
        return await query.FirstOrDefaultAsync(cancellationToken);
    }

    private async Task<User?> LoadUserGraph(Guid userId, CancellationToken cancellationToken)
    {
        return await _db.Users
            .Include(x => x.Tenant)
            .Include(x => x.UserRoles).ThenInclude(x => x.Role).ThenInclude(x => x!.RolePermissions).ThenInclude(x => x.Permission)
            .Include(x => x.EmployeeUserAccounts)
            .Include(x => x.PermissionOverrides)
            .FirstOrDefaultAsync(x => x.Id == userId, cancellationToken);
    }

    private string AddRefreshToken(User user, RequestContext context)
    {
        var refreshToken = _tokenService.CreateSecureToken();
        _db.RefreshTokens.Add(new RefreshToken
        {
            UserId = user.Id,
            TokenHash = _tokenService.HashToken(refreshToken),
            ExpiresAtUtc = DateTime.UtcNow.AddDays(_jwtOptions.RefreshTokenDays),
            CreatedByIp = context.IpAddress
        });
        return refreshToken;
    }

    private AuthResponse BuildAuthResponse(User user, string refreshToken)
    {
        var roles = GetRoles(user);
        var permissions = GetPermissions(user);
        var accessToken = _tokenService.CreateAccessToken(user, roles, permissions, user.Tenant!, out var expiresAtUtc);
        return new AuthResponse(accessToken, refreshToken, expiresAtUtc, ToUserDto(user));
    }

    private static AuthUserDto ToUserDto(User user)
    {
        var link = PrimaryAccess(user);
        return new AuthUserDto(user.Id, user.TenantId, user.Tenant!.Slug, user.Email, user.FullName, GetRoles(user), GetPermissions(user), link?.EmployeeId, link?.AccessMode ?? AccessModes.FullPortal, link?.RequiresPasswordSetup ?? false);
    }

    private static IReadOnlyCollection<string> GetRoles(User user)
    {
        return user.UserRoles.Select(x => x.Role?.Name).Where(x => !string.IsNullOrWhiteSpace(x)).Select(x => x!).Distinct().OrderBy(x => x).ToList();
    }

    private static IReadOnlyCollection<string> GetPermissions(User user)
    {
        var permissions = user.UserRoles
            .SelectMany(x => x.Role?.RolePermissions ?? Array.Empty<RolePermission>())
            .Select(x => x.Permission?.Key)
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Select(x => x!)
            .Distinct()
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var accessPermission in AccessModePermissions(PrimaryAccess(user)?.AccessMode))
            permissions.Add(accessPermission);
        foreach (var ov in user.PermissionOverrides.Where(x => x.IsActive && (x.ExpiresAtUtc is null || x.ExpiresAtUtc > DateTime.UtcNow)))
        {
            if (ov.Effect.Equals("Deny", StringComparison.OrdinalIgnoreCase)) permissions.Remove(ov.PermissionKey);
            else permissions.Add(ov.PermissionKey);
        }
        return permissions.OrderBy(x => x).ToList();
    }

    private static EmployeeUserAccount? PrimaryAccess(User user) =>
        user.EmployeeUserAccounts.Where(x => !x.IsDeleted).OrderByDescending(x => x.IsPrimary).ThenByDescending(x => x.CreatedAtUtc).FirstOrDefault();

    private static bool IsNoLogin(User user) => PrimaryAccess(user)?.AccessMode == AccessModes.NoLogin;
    private static bool RequiresPasswordSetup(User user) => PrimaryAccess(user)?.RequiresPasswordSetup == true;

    private static IReadOnlyCollection<string> AccessModePermissions(string? accessMode) => accessMode switch
    {
        AccessModes.EssOnly => new[] { "ess.read", "ess.write", "profile.read" },
        AccessModes.ManagerPortal => new[] { "ess.read", "ess.write", "manager.read", "approvals.read", "approvals.decide", "profile.read" },
        AccessModes.Mobile => new[] { "ess.read", "ess.write", "attendance.write", "profile.read" },
        AccessModes.KioskOnly => new[] { "attendance.kiosk" },
        AccessModes.NoLogin => Array.Empty<string>(),
        _ => Array.Empty<string>()
    };

    public static string Normalize(string value) => value.Trim().ToUpperInvariant();
}
