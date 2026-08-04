using System.Collections.Concurrent;
using Opstrax.Telematics.Gateway.Security.Replay;

namespace Opstrax.Telematics.Security.Tests;

/// <summary>
/// Behavioural contract for <see cref="InMemoryReplayGuard"/>: the dev/test implementation of the
/// replay + sequence defense. Asserts the four decision arms (accept, exact duplicate, out-of-order,
/// wrap tolerance), the bounded-LRU eviction guarantee, and thread-safety under concurrency.
/// </summary>
public class InMemoryReplayGuardTests
{
    private const string Device = "dev-abc-0001";
    private static readonly DateTime Fix = new(2026, 7, 12, 10, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void FirstOccurrence_IsAccepted()
    {
        var guard = new InMemoryReplayGuard();

        var decision = guard.Check(Device, protocolSerial: 10, contentHash: "hashA", Fix);

        Assert.Equal(ReplayOutcome.Accept, decision.Outcome);
        Assert.True(decision.IsAccepted);
        Assert.Null(decision.LastSeenSerial);
    }

    [Fact]
    public async Task CheckAsync_IsBehaviorParity_And_Dedups()
    {
        var guard = new InMemoryReplayGuard();

        // Async path (used by the gateway read loop) accepts first sight, then flags the exact replay.
        var first = await guard.CheckAsync(Device, 10, "hashA", Fix);
        Assert.Equal(ReplayOutcome.Accept, first.Outcome);
        var replay = await guard.CheckAsync(Device, 10, "hashA", Fix);
        Assert.Equal(ReplayOutcome.DuplicateReplay, replay.Outcome);
    }

    [Fact]
    public void ExactDuplicate_IsDuplicateReplay()
    {
        var guard = new InMemoryReplayGuard();
        Assert.Equal(ReplayOutcome.Accept, guard.Check(Device, 10, "hashA", Fix).Outcome);

        var replay = guard.Check(Device, 10, "hashA", Fix);

        Assert.Equal(ReplayOutcome.DuplicateReplay, replay.Outcome);
        Assert.False(replay.IsAccepted);
    }

    [Fact]
    public void LowerSerialAfterHigher_IsOutOfOrder_AndCarriesHighWater()
    {
        var guard = new InMemoryReplayGuard();
        Assert.Equal(ReplayOutcome.Accept, guard.Check(Device, 20, "hashHigh", Fix).Outcome);

        var stale = guard.Check(Device, 5, "hashLow", Fix);

        Assert.Equal(ReplayOutcome.OutOfOrder, stale.Outcome);
        Assert.Equal(20L, stale.LastSeenSerial);
    }

    [Fact]
    public void OutOfOrder_IsNotRecorded_SoRepeatStaysOutOfOrder()
    {
        var guard = new InMemoryReplayGuard();
        guard.Check(Device, 20, "hashHigh", Fix);

        var first = guard.Check(Device, 5, "hashLow", Fix);
        var second = guard.Check(Device, 5, "hashLow", Fix);

        Assert.Equal(ReplayOutcome.OutOfOrder, first.Outcome);
        Assert.Equal(ReplayOutcome.OutOfOrder, second.Outcome);
    }

    [Fact]
    public void ReplayOfAcceptedFrame_IsAlwaysRejected_EvenAfterEviction()
    {
        // Window of 4; advance the high-water mark, then evict the first frame with newer serials.
        var guard = new InMemoryReplayGuard(perDeviceWindow: 4);
        Assert.Equal(ReplayOutcome.Accept, guard.Check(Device, 1, "h1", Fix).Outcome);
        for (long s = 2; s <= 6; s++)
            Assert.Equal(ReplayOutcome.Accept, guard.Check(Device, s, "h" + s, Fix).Outcome);

        // (1,"h1") has been evicted from the dedup window, but its serial is below the high-water
        // mark (6), so a replay is still rejected — now as OutOfOrder rather than DuplicateReplay.
        var replay = guard.Check(Device, 1, "h1", Fix);

        Assert.Equal(ReplayOutcome.OutOfOrder, replay.Outcome);
        Assert.Equal(6L, replay.LastSeenSerial);
    }

    [Fact]
    public void DedupWindow_EvictsLeastRecentEntry_AtCapacity()
    {
        // Isolate the LRU eviction from the sequence check by holding the serial at the high-water
        // mark (equal serial, distinct content is accepted but does not advance the mark), so the
        // ONLY thing that can flip a repeat from DuplicateReplay->Accept is window eviction.
        var guard = new InMemoryReplayGuard(perDeviceWindow: 4);
        const long serial = 100;
        for (int i = 1; i <= 8; i++)
            Assert.Equal(ReplayOutcome.Accept, guard.Check(Device, serial, "h" + i, Fix).Outcome);

        // The 4 most-recent (h5..h8) are still deduped; the oldest (h1..h4) were evicted.
        Assert.Equal(ReplayOutcome.DuplicateReplay, guard.Check(Device, serial, "h8", Fix).Outcome);
        Assert.Equal(ReplayOutcome.DuplicateReplay, guard.Check(Device, serial, "h7", Fix).Outcome);
        Assert.Equal(ReplayOutcome.Accept, guard.Check(Device, serial, "h1", Fix).Outcome);
    }

    [Fact]
    public void RecentUse_RefreshesLruRecency()
    {
        var guard = new InMemoryReplayGuard(perDeviceWindow: 3);
        const long serial = 7;
        guard.Check(Device, serial, "a", Fix); // oldest
        guard.Check(Device, serial, "b", Fix);
        guard.Check(Device, serial, "c", Fix);

        // Touch "a" (duplicate) -> it becomes most-recent again.
        Assert.Equal(ReplayOutcome.DuplicateReplay, guard.Check(Device, serial, "a", Fix).Outcome);

        // Insert "d": capacity 3 forces one eviction. "b" is now the least-recent, not "a".
        Assert.Equal(ReplayOutcome.Accept, guard.Check(Device, serial, "d", Fix).Outcome);

        Assert.Equal(ReplayOutcome.DuplicateReplay, guard.Check(Device, serial, "a", Fix).Outcome);
        Assert.Equal(ReplayOutcome.Accept, guard.Check(Device, serial, "b", Fix).Outcome); // evicted
    }

    [Fact]
    public void DifferentDevices_AreTrackedIndependently()
    {
        var guard = new InMemoryReplayGuard();
        guard.Check("dev-1", 50, "shared-hash", Fix);

        // Same serial+hash on a DIFFERENT device is a first occurrence, not a replay.
        var other = guard.Check("dev-2", 50, "shared-hash", Fix);

        Assert.Equal(ReplayOutcome.Accept, other.Outcome);
        Assert.Equal(2, guard.TrackedDeviceCount);
    }

    [Fact]
    public void EqualSerial_DistinctContent_IsAccepted_ButDoesNotBlockLaterProgress()
    {
        var guard = new InMemoryReplayGuard();
        Assert.Equal(ReplayOutcome.Accept, guard.Check(Device, 30, "x", Fix).Outcome);
        Assert.Equal(ReplayOutcome.Accept, guard.Check(Device, 30, "y", Fix).Outcome);
        Assert.Equal(ReplayOutcome.Accept, guard.Check(Device, 31, "z", Fix).Outcome);
        Assert.Equal(ReplayOutcome.OutOfOrder, guard.Check(Device, 29, "w", Fix).Outcome);
    }

    [Fact]
    public void WraparoundMode_TreatsCounterWrap_AsForwardProgress()
    {
        // GT06's serial is 16-bit and wraps at 65536. With the modulus set, 65530 -> 3 is forward.
        var guard = new InMemoryReplayGuard(perDeviceWindow: 64, serialModulus: 65536);
        Assert.Equal(ReplayOutcome.Accept, guard.Check(Device, 65530, "near-top", Fix).Outcome);

        var afterWrap = guard.Check(Device, 3, "after-wrap", Fix);

        Assert.Equal(ReplayOutcome.Accept, afterWrap.Outcome);

        // A genuine backward step within the near half is still out-of-order.
        var backward = guard.Check(Device, 1, "backward", Fix);
        Assert.Equal(ReplayOutcome.OutOfOrder, backward.Outcome);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void InvalidDeviceId_Throws(string? deviceId)
    {
        var guard = new InMemoryReplayGuard();
        Assert.Throws<ArgumentException>(() => guard.Check(deviceId!, 1, "h", Fix));
    }

    [Fact]
    public void InvalidConstructorArgs_Throw()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new InMemoryReplayGuard(perDeviceWindow: 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => new InMemoryReplayGuard(serialModulus: 1));
    }

    [Fact]
    public void ConcurrentDuplicates_AcceptExactlyOnce_NoTornState()
    {
        // Many threads race identical frames for the same device: for each distinct content hash
        // exactly ONE thread may see Accept; the rest must see DuplicateReplay. No thread may throw.
        // The serial is held constant and the window is large enough for every frame, so the ONLY
        // thing under test is the atomicity of check-and-record (not the sequence arm).
        var guard = new InMemoryReplayGuard(perDeviceWindow: 4096);
        const long serial = 1;
        const int distinctFrames = 200;
        const int racersPerFrame = 16;

        var accepts = new ConcurrentDictionary<int, int>();
        var exceptions = new ConcurrentQueue<Exception>();

        Parallel.For(0, distinctFrames * racersPerFrame, i =>
        {
            int frame = i % distinctFrames; // each frame hit by `racersPerFrame` threads
            try
            {
                var d = guard.Check("race-device", serial, "hash-" + frame, Fix);
                if (d.Outcome == ReplayOutcome.Accept)
                    accepts.AddOrUpdate(frame, 1, (_, c) => c + 1);
                else
                    Assert.Equal(ReplayOutcome.DuplicateReplay, d.Outcome);
            }
            catch (Exception ex)
            {
                exceptions.Enqueue(ex);
            }
        });

        Assert.Empty(exceptions);
        Assert.Equal(distinctFrames, accepts.Count);
        Assert.All(accepts.Values, count => Assert.Equal(1, count));
    }

    [Fact]
    public void ConcurrentDistinctDevices_DoNotContend_AndAllAccept()
    {
        var guard = new InMemoryReplayGuard();
        var exceptions = new ConcurrentQueue<Exception>();
        int accepted = 0;

        Parallel.For(0, 1000, i =>
        {
            try
            {
                var d = guard.Check("dev-" + i, 1, "h", Fix);
                if (d.IsAccepted)
                    Interlocked.Increment(ref accepted);
            }
            catch (Exception ex)
            {
                exceptions.Enqueue(ex);
            }
        });

        Assert.Empty(exceptions);
        Assert.Equal(1000, accepted);
        Assert.Equal(1000, guard.TrackedDeviceCount);
    }
}
