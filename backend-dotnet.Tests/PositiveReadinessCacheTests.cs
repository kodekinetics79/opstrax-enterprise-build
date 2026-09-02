using Opstrax.Api.Services;

namespace Opstrax.Tests;

public sealed class PositiveReadinessCacheTests
{
    [Fact]
    public async Task PositiveResultIsReusedOnlyWithinTheBoundedWindow()
    {
        var clock = new MutableTimeProvider(new DateTimeOffset(2026, 9, 1, 0, 0, 0, TimeSpan.Zero));
        var cache = new PositiveReadinessCache<int>(TimeSpan.FromSeconds(15), value => value > 0, clock);
        var refreshes = 0;

        Task<int> Refresh(CancellationToken _) => Task.FromResult(++refreshes);

        Assert.Equal(1, await cache.GetOrRefreshAsync(Refresh));
        Assert.Equal(1, await cache.GetOrRefreshAsync(Refresh));
        Assert.Equal(1, refreshes);

        clock.Advance(TimeSpan.FromSeconds(15));

        Assert.Equal(2, await cache.GetOrRefreshAsync(Refresh));
        Assert.Equal(2, refreshes);
    }

    [Fact]
    public async Task FailedResultIsNeverCached()
    {
        var clock = new MutableTimeProvider(DateTimeOffset.UnixEpoch);
        var cache = new PositiveReadinessCache<bool>(TimeSpan.FromSeconds(15), value => value, clock);
        var refreshes = 0;

        Task<bool> Refresh(CancellationToken _)
        {
            refreshes++;
            return Task.FromResult(false);
        }

        Assert.False(await cache.GetOrRefreshAsync(Refresh));
        Assert.False(await cache.GetOrRefreshAsync(Refresh));
        Assert.Equal(2, refreshes);
    }

    [Fact]
    public async Task ConcurrentCallersShareOneSuccessfulRefresh()
    {
        var clock = new MutableTimeProvider(DateTimeOffset.UnixEpoch);
        var cache = new PositiveReadinessCache<bool>(TimeSpan.FromSeconds(15), value => value, clock);
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var refreshes = 0;

        async Task<bool> Refresh(CancellationToken _)
        {
            Interlocked.Increment(ref refreshes);
            started.TrySetResult();
            await release.Task;
            return true;
        }

        var calls = Enumerable.Range(0, 8)
            .Select(_ => cache.GetOrRefreshAsync(Refresh))
            .ToArray();

        await started.Task;
        release.SetResult();

        Assert.All(await Task.WhenAll(calls), Assert.True);
        Assert.Equal(1, refreshes);
    }

    private sealed class MutableTimeProvider(DateTimeOffset current) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => current;

        public void Advance(TimeSpan duration) => current += duration;
    }
}
