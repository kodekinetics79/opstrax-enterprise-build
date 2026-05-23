namespace Zayra.Api.Application.Auth;

public record RequestContext(string? IpAddress, string? UserAgent, Guid? UserId = null, Guid? TenantId = null);
