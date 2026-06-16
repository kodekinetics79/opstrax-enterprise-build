using System.Collections.Concurrent;

namespace Zayra.Api.Infrastructure.Qiwa;

/// <summary>
/// Process-wide in-memory cache of Qiwa OAuth2 access tokens keyed by tenant.
/// Tokens are refreshed by callers when <see cref="TryGet"/> reports expiry.
/// Registered as a singleton.
/// </summary>
public sealed class QiwaOAuthTokenCache
{
    private sealed record Entry(string Token, DateTime ExpiresAtUtc);

    private readonly ConcurrentDictionary<Guid, Entry> _tokens = new();

    /// <summary>Skew applied before expiry so a token is refreshed slightly early.</summary>
    private static readonly TimeSpan Skew = TimeSpan.FromSeconds(60);

    /// <summary>Returns a non-expired token for the tenant, or null if absent/expired.</summary>
    public string? TryGet(Guid tenantId)
        => _tokens.TryGetValue(tenantId, out var e) && DateTime.UtcNow < e.ExpiresAtUtc - Skew
            ? e.Token
            : null;

    public void Set(Guid tenantId, string token, DateTime expiresAtUtc)
        => _tokens[tenantId] = new Entry(token, expiresAtUtc);

    /// <summary>Default Qiwa access-token lifetime is 5 minutes; assume that when unknown.</summary>
    public void Set(Guid tenantId, string token, int? expiresInSeconds)
        => Set(tenantId, token, DateTime.UtcNow.AddSeconds(expiresInSeconds ?? 300));

    public void Invalidate(Guid tenantId) => _tokens.TryRemove(tenantId, out _);
}
