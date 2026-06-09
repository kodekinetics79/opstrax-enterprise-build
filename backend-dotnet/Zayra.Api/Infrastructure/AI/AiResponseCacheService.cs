using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Zayra.Api.Application.AI;
using Zayra.Api.Data;
using Zayra.Api.Models;

namespace Zayra.Api.Infrastructure.AI;

public sealed class AiResponseCacheService : IAiResponseCacheService
{
    private readonly ZayraDbContext _db;
    private readonly ILogger<AiResponseCacheService> _logger;

    public AiResponseCacheService(ZayraDbContext db, ILogger<AiResponseCacheService> logger)
    {
        _db = db;
        _logger = logger;
    }

    public async Task<AiCachedResponse?> TryGetAsync(AiCacheKey key, CancellationToken cancellationToken)
    {
        try
        {
            var now = DateTime.UtcNow;
            var entry = await _db.AIHRQueryCaches
                .FirstOrDefaultAsync(x => x.TenantId == key.TenantId && x.CacheKey == key.CacheKey, cancellationToken);

            if (entry is null) return null;
            if (entry.ExpiresAtUtc <= now)
            {
                _db.AIHRQueryCaches.Remove(entry);
                await _db.SaveChangesAsync(cancellationToken);
                return null;
            }

            entry.HitCount += 1;
            entry.LastHitAtUtc = now;
            await _db.SaveChangesAsync(cancellationToken);

            return new AiCachedResponse(
                entry.Answer,
                entry.Provider,
                entry.Model,
                entry.ResponseStatus,
                entry.HumanReviewRequired,
                entry.IsAdvisoryLabelShown,
                entry.TokensUsed,
                entry.PromptTokens,
                entry.CompletionTokens,
                entry.ResponseTimeMs,
                entry.HitCount,
                entry.ExpiresAtUtc);
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "AI cache lookup failed for tenant {TenantId} and cache key {CacheKey}. Falling back to uncached AI flow.",
                key.TenantId,
                key.CacheKey);
            return null;
        }
    }

    public async Task StoreAsync(AiCacheKey key, AIQueryResponse response, string responseStatus, int promptTokens, int completionTokens, int responseTimeMs, CancellationToken cancellationToken)
    {
        try
        {
            var now = DateTime.UtcNow;
            var entry = await _db.AIHRQueryCaches
                .FirstOrDefaultAsync(x => x.TenantId == key.TenantId && x.CacheKey == key.CacheKey, cancellationToken);

            if (entry is null)
            {
                entry = new AIHRQueryCache
                {
                    TenantId = key.TenantId,
                    CacheKey = key.CacheKey
                };
                _db.AIHRQueryCaches.Add(entry);
            }

            entry.QueryHash = key.QueryHash;
            entry.NormalizedQuery = key.NormalizedQuery;
            entry.IntentClassified = key.IntentClassified;
            entry.Module = key.Module;
            entry.EmployeeId = key.EmployeeId;
            entry.UserRoleSignature = key.UserRoleSignature;
            entry.PermissionSignature = key.PermissionSignature;
            entry.Answer = response.Answer;
            entry.Provider = response.Provider;
            entry.Model = response.Model ?? string.Empty;
            entry.ResponseStatus = responseStatus;
            entry.HumanReviewRequired = response.HumanReviewRequired;
            entry.IsAdvisoryLabelShown = true;
            entry.TokensUsed = response.TokensUsed;
            entry.PromptTokens = promptTokens;
            entry.CompletionTokens = completionTokens;
            entry.ResponseTimeMs = responseTimeMs;
            entry.CreatedAtUtc = entry.CreatedAtUtc == default ? now : entry.CreatedAtUtc;
            entry.LastHitAtUtc = now;
            entry.HitCount = 0;
            entry.ExpiresAtUtc = now.AddMinutes(5);

            await _db.SaveChangesAsync(cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "AI cache write failed for tenant {TenantId} and cache key {CacheKey}. Continuing without cache persistence.",
                key.TenantId,
                key.CacheKey);
        }
    }
}
