using Zayra.Api.Domain.Entities;

namespace Zayra.Api.Application.Auth;

public interface IPasswordHasher
{
    string Hash(string password);
    bool Verify(string password, string passwordHash);
}

public interface ITokenService
{
    string CreateAccessToken(User user, IReadOnlyCollection<string> roles, IReadOnlyCollection<string> permissions, Tenant tenant, out DateTime expiresAtUtc);
    string CreateSecureToken();
    string HashToken(string token);
}

public interface IAuditService
{
    Task WriteAsync(string action, string entityName, string? entityId, RequestContext context, string? metadata, CancellationToken cancellationToken);
}

public interface IAuthSeeder
{
    Task SeedAsync(CancellationToken cancellationToken = default);
}
