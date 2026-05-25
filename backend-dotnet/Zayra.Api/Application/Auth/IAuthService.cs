namespace Zayra.Api.Application.Auth;

public interface IAuthService
{
    Task<AuthResponse> LoginAsync(LoginRequest request, RequestContext context, CancellationToken cancellationToken);
    Task<AuthResponse> RefreshAsync(RefreshTokenRequest request, RequestContext context, CancellationToken cancellationToken);
    Task LogoutAsync(LogoutRequest request, RequestContext context, CancellationToken cancellationToken);
    Task<ForgotPasswordResponse> ForgotPasswordAsync(ForgotPasswordRequest request, RequestContext context, CancellationToken cancellationToken);
    Task ResetPasswordAsync(ResetPasswordRequest request, RequestContext context, CancellationToken cancellationToken);
    Task<AuthResponse> AcceptInvitationAsync(AcceptInvitationRequest request, RequestContext context, CancellationToken cancellationToken);
    Task<AuthUserDto?> GetCurrentUserAsync(Guid userId, CancellationToken cancellationToken);
    Task ChangePasswordAsync(Guid userId, ChangePasswordRequest request, RequestContext context, CancellationToken cancellationToken);
}
