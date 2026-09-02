namespace Opstrax.Api.Services;

/// <summary>
/// Coalesces concurrent readiness refreshes and briefly reuses only successful
/// results. A failed result is never cached, so dependency or contract failures
/// continue to fail closed on the next request.
/// </summary>
internal sealed class PositiveReadinessCache<T>(
    TimeSpan duration,
    Func<T, bool> isPositive,
    TimeProvider timeProvider)
{
    internal static readonly TimeSpan DefaultDuration = TimeSpan.FromSeconds(15);

    private readonly SemaphoreSlim refreshGate = new(1, 1);
    private readonly object cacheLock = new();
    private bool hasValue;
    private T cachedValue = default!;
    private DateTimeOffset validUntil;

    public async Task<T> GetOrRefreshAsync(
        Func<CancellationToken, Task<T>> refresh,
        CancellationToken ct = default)
    {
        if (TryGet(out var cached)) return cached;

        await refreshGate.WaitAsync(ct);
        try
        {
            // A concurrent caller may have populated the cache while this caller
            // was waiting. Recheck before executing the expensive proof.
            if (TryGet(out cached)) return cached;

            var current = await refresh(ct);
            lock (cacheLock)
            {
                if (isPositive(current))
                {
                    cachedValue = current;
                    validUntil = timeProvider.GetUtcNow() + duration;
                    hasValue = true;
                }
                else
                {
                    cachedValue = default!;
                    validUntil = default;
                    hasValue = false;
                }
            }
            return current;
        }
        finally
        {
            refreshGate.Release();
        }
    }

    private bool TryGet(out T value)
    {
        lock (cacheLock)
        {
            if (hasValue && timeProvider.GetUtcNow() < validUntil)
            {
                value = cachedValue;
                return true;
            }

            value = default!;
            return false;
        }
    }
}
