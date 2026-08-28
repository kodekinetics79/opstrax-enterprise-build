using Opstrax.Telematics.Gateway.Security.Replay;

namespace Opstrax.Telematics.Security.Tests;

/// <summary>
/// Regression tests for the GT06 reboot / serial-reset defect and the replay defences that must
/// survive fixing it.
/// </summary>
/// <remarks>
/// <para>
/// <b>The defect.</b> A GT06 tracker restarts its 16-bit information serial at 1 every time it
/// powers up. The guard compares a candidate serial against the device's high-water mark on a
/// circle and treats the farther half as behind — correct for a counter wrap, wrong for a reboot.
/// A vehicle that ignition-cycles at serial 10 000 comes back at serial 1, which reads as 9 999
/// steps backwards, so every frame it sends until its counter climbs past 10 000 is classified
/// out-of-order. At a fix every ten seconds that is more than a full day of degraded telemetry
/// from one ignition cycle.
/// </para>
/// <para>
/// <b>What the fix must NOT do.</b> Relaxing the high-water rule, clearing the seen window, or
/// truncating the durable ledger would all "fix" the symptom by removing the defence. The tests
/// below therefore pin both directions: post-reboot frames are accepted, AND a frame captured
/// before the reboot is still rejected when it is replayed after one.
/// </para>
/// </remarks>
public class ReplaySessionEpochTests
{
    private const string Device = "dev-known-0001";
    private const long Gt06Modulus = 65_536;
    private static readonly DateTime Fix = new(2024, 1, 15, 10, 20, 30, DateTimeKind.Utc);

    private static InMemoryReplayGuard Guard() => new(perDeviceWindow: 512, serialModulus: Gt06Modulus);

    // ── A: ordinary forward progress ───────────────────────────────────────────

    [Fact]
    public void A_normal_next_serial_is_accepted()
    {
        InMemoryReplayGuard guard = Guard();
        Assert.Equal(ReplayOutcome.Accept, guard.Check(Device, 10_000, "frame-10000", Fix).Outcome);
        Assert.Equal(ReplayOutcome.Accept, guard.Check(Device, 10_001, "frame-10001", Fix).Outcome);
    }

    // ── B: counter wrap ────────────────────────────────────────────────────────

    /// <summary>
    /// A genuine 16-bit wrap is forward progress, not a reset: the device really did emit the
    /// intervening frames. No login is involved, so no epoch is opened.
    /// </summary>
    [Fact]
    public void A_counter_wrap_is_forward_progress_without_any_login()
    {
        InMemoryReplayGuard guard = Guard();
        Assert.Equal(ReplayOutcome.Accept, guard.Check(Device, 65_534, "frame-65534", Fix).Outcome);
        Assert.Equal(ReplayOutcome.Accept, guard.Check(Device, 65_535, "frame-65535", Fix).Outcome);
        Assert.Equal(ReplayOutcome.Accept, guard.Check(Device, 0, "frame-wrap-0", Fix).Outcome);
        Assert.Equal(ReplayOutcome.Accept, guard.Check(Device, 1, "frame-wrap-1", Fix).Outcome);
    }

    // ── C: reboot ──────────────────────────────────────────────────────────────

    /// <summary>
    /// The headline case. Serial 10 000, then an authenticated reconnect, then serial 1. Without an
    /// epoch this is thousands of false out-of-order verdicts; with one it is ordinary traffic.
    /// </summary>
    [Fact]
    public void A_post_reboot_counter_reset_is_accepted_after_an_authenticated_login()
    {
        InMemoryReplayGuard guard = Guard();
        Assert.Equal(ReplayOutcome.Accept, guard.Check(Device, 10_000, "pre-reboot", Fix).Outcome);

        guard.BeginSessionEpoch(Device); // the device reconnected and authenticated

        Assert.Equal(ReplayOutcome.Accept, guard.Check(Device, 1, "post-reboot-1", Fix).Outcome);
        Assert.Equal(ReplayOutcome.Accept, guard.Check(Device, 2, "post-reboot-2", Fix).Outcome);
        Assert.Equal(ReplayOutcome.Accept, guard.Check(Device, 3, "post-reboot-3", Fix).Outcome);
    }

    /// <summary>
    /// Not one frame of a long post-reboot run may be lost. This is the quantified version of the
    /// defect: at the baseline every one of these would have been out-of-order.
    /// </summary>
    [Fact]
    public void An_entire_post_reboot_run_is_accepted_not_just_the_first_frame()
    {
        InMemoryReplayGuard guard = Guard();
        guard.Check(Device, 10_000, "pre-reboot", Fix);
        guard.BeginSessionEpoch(Device);

        for (int serial = 1; serial <= 2_000; serial++)
        {
            ReplayDecision decision = guard.Check(Device, serial, $"post-reboot-{serial}", Fix);
            Assert.Equal(ReplayOutcome.Accept, decision.Outcome);
        }
    }

    /// <summary>
    /// A reboot that returns at a HIGH serial is accepted too. This is the case a naive fix misses:
    /// merely nudging the unwrap origin leaves the nearer-half rule in charge, and any serial past
    /// the half-way point still maps backwards.
    /// </summary>
    [Theory]
    [InlineData(1)]
    [InlineData(32_767)]
    [InlineData(32_768)]      // exactly half: the ambiguous point for the wrap rule
    [InlineData(40_000)]
    [InlineData(65_535)]
    public void A_post_reboot_reset_is_accepted_at_every_serial_in_the_counter_range(int resumeSerial)
    {
        InMemoryReplayGuard guard = Guard();
        guard.Check(Device, 60_000, "pre-reboot", Fix);
        guard.BeginSessionEpoch(Device);

        Assert.Equal(ReplayOutcome.Accept, guard.Check(Device, resumeSerial, "post-reboot", Fix).Outcome);
    }

    // ── D: stale frame from a prior epoch ──────────────────────────────────────

    /// <summary>
    /// The attack the epoch boundary would otherwise open. A frame captured before the power cycle
    /// gets a brand-new unwrapped serial in the new epoch, so serial-based dedup cannot see it —
    /// the content digest is what rejects it.
    /// </summary>
    [Fact]
    public void A_frame_captured_before_the_reboot_is_still_rejected_when_replayed_after_it()
    {
        InMemoryReplayGuard guard = Guard();
        ReplayDecision original = guard.Check(Device, 9_000, "captured-frame", Fix);
        Assert.Equal(ReplayOutcome.Accept, original.Outcome);

        guard.BeginSessionEpoch(Device);
        guard.Check(Device, 1, "post-reboot-1", Fix);

        // The attacker replays the captured bytes. Its serial (9 000) is comfortably "ahead" of the
        // new epoch's high-water mark, so nothing about the SEQUENCE can reject it.
        ReplayDecision replayed = guard.Check(Device, 9_000, "captured-frame", Fix);

        Assert.Equal(ReplayOutcome.DuplicateReplay, replayed.Outcome);
        Assert.Equal(original.EventId, replayed.EventId);
    }

    /// <summary>A pre-reboot frame stays rejected across several subsequent power cycles.</summary>
    [Fact]
    public void A_pre_reboot_frame_stays_rejected_across_repeated_power_cycles()
    {
        InMemoryReplayGuard guard = Guard();
        ReplayDecision original = guard.Check(Device, 9_000, "captured-frame", Fix);

        for (int cycle = 1; cycle <= 5; cycle++)
        {
            guard.BeginSessionEpoch(Device);
            guard.Check(Device, 1, $"cycle-{cycle}-frame-1", Fix);

            ReplayDecision replayed = guard.Check(Device, 9_000, "captured-frame", Fix);
            Assert.Equal(ReplayOutcome.DuplicateReplay, replayed.Outcome);
            Assert.Equal(original.EventId, replayed.EventId);
        }
    }

    // ── E: duplicate inside the new epoch ──────────────────────────────────────

    [Fact]
    public void A_duplicate_within_the_new_epoch_is_recognised_and_keeps_its_event_identity()
    {
        InMemoryReplayGuard guard = Guard();
        guard.Check(Device, 10_000, "pre-reboot", Fix);
        guard.BeginSessionEpoch(Device);

        ReplayDecision first = guard.Check(Device, 1, "post-reboot-1", Fix);
        ReplayDecision retry = guard.Check(Device, 1, "post-reboot-1", Fix);

        Assert.Equal(ReplayOutcome.Accept, first.Outcome);
        Assert.Equal(ReplayOutcome.DuplicateReplay, retry.Outcome);
        Assert.Equal(first.EventId, retry.EventId);
    }

    // ── Same-session ordering is untouched ─────────────────────────────────────

    /// <summary>An in-session reordered frame is still out-of-order; the epoch changed nothing here.</summary>
    [Fact]
    public void A_reordered_frame_inside_one_session_is_still_out_of_order()
    {
        InMemoryReplayGuard guard = Guard();
        guard.Check(Device, 10_000, "frame-10000", Fix);
        guard.Check(Device, 10_005, "frame-10005", Fix);

        ReplayDecision late = guard.Check(Device, 10_002, "frame-10002-delayed", Fix);
        Assert.Equal(ReplayOutcome.OutOfOrder, late.Outcome);
    }

    /// <summary>
    /// An epoch NEVER moves the high-water mark backwards. Whatever a login declares, a device can
    /// only ever be given a serial position ahead of where it already was.
    /// </summary>
    [Fact]
    public void An_epoch_only_ever_moves_the_sequence_forward()
    {
        InMemoryReplayGuard guard = Guard();
        guard.Check(Device, 50_000, "high-frame", Fix);
        guard.BeginSessionEpoch(Device);
        guard.Check(Device, 1, "post-reboot-1", Fix);

        // A frame from BEFORE the reboot, with novel content, is not resurrected as in-order
        // history: it is behind the post-reboot mark within the new epoch.
        ReplayDecision stale = guard.Check(Device, 0, "novel-but-behind", Fix);
        Assert.Equal(ReplayOutcome.OutOfOrder, stale.Outcome);
    }

    /// <summary>Opening an epoch for a device with no history at all is a harmless no-op.</summary>
    [Fact]
    public void Opening_an_epoch_for_an_unknown_device_is_a_no_op()
    {
        InMemoryReplayGuard guard = Guard();
        guard.BeginSessionEpoch("never-seen");

        Assert.Equal(ReplayOutcome.Accept, guard.Check("never-seen", 1, "first-frame", Fix).Outcome);
    }

    /// <summary>Epochs are per device: one vehicle's power cycle cannot disturb another's sequence.</summary>
    [Fact]
    public void An_epoch_is_scoped_to_one_device()
    {
        InMemoryReplayGuard guard = Guard();
        guard.Check("device-a", 10_000, "a-pre", Fix);
        guard.Check("device-b", 10_000, "b-pre", Fix);

        guard.BeginSessionEpoch("device-a");

        Assert.Equal(ReplayOutcome.Accept, guard.Check("device-a", 1, "a-post", Fix).Outcome);
        // Device B never rebooted, so its counter reset is still (correctly) out-of-order.
        Assert.Equal(ReplayOutcome.OutOfOrder, guard.Check("device-b", 1, "b-post", Fix).Outcome);
    }

    // ── Bounded device cardinality ─────────────────────────────────────────────

    /// <summary>
    /// The per-device dedup window was already bounded; the DEVICE MAP was not. On a public edge,
    /// every forged IMEI a scanner tries minted a permanent entry, so the guard's footprint grew
    /// with attacker-chosen cardinality and never shrank.
    /// </summary>
    [Fact]
    public void The_tracked_device_map_is_bounded_against_a_forged_identifier_flood()
    {
        var guard = new InMemoryReplayGuard(perDeviceWindow: 8, serialModulus: Gt06Modulus, maxTrackedDevices: 100);

        for (int i = 0; i < 10_000; i++)
            guard.Check($"forged-imei-{i}", 1, $"probe-{i}", Fix);

        Assert.True(guard.TrackedDeviceCount <= 100,
            $"device map grew to {guard.TrackedDeviceCount}, above the {guard.MaxTrackedDevices} ceiling");
    }

    /// <summary>
    /// Eviction is least-recently-active, so a real device streaming through a flood of forged
    /// identifiers keeps its state. Evicting the busy device would be a denial of service dressed
    /// up as a memory bound.
    /// </summary>
    [Fact]
    public void An_active_device_is_not_evicted_by_a_flood_of_idle_ones()
    {
        var guard = new InMemoryReplayGuard(perDeviceWindow: 16, serialModulus: Gt06Modulus, maxTrackedDevices: 50);

        guard.Check(Device, 100, "real-frame-100", Fix);
        for (int i = 0; i < 500; i++)
        {
            guard.Check($"forged-{i}", 1, $"probe-{i}", Fix);
            guard.Check(Device, 101 + i, $"real-frame-{101 + i}", Fix); // the real device keeps working
        }

        // Its sequence state survived: an old serial is still recognised as behind.
        Assert.Equal(ReplayOutcome.OutOfOrder, guard.Check(Device, 100, "real-frame-100-replayed", Fix).Outcome);
        Assert.True(guard.TrackedDeviceCount <= 50);
    }

    /// <summary>
    /// An evicted device is treated as new, and a new device bootstraps its own generation — so
    /// eviction can never resurrect a stale frame as in-order history.
    /// </summary>
    [Fact]
    public void Eviction_does_not_resurrect_a_stale_frame_as_in_order()
    {
        var guard = new InMemoryReplayGuard(perDeviceWindow: 4, serialModulus: Gt06Modulus, maxTrackedDevices: 2);

        guard.Check("victim", 500, "victim-frame", Fix);
        guard.Check("filler-1", 1, "filler-1-frame", Fix);
        guard.Check("filler-2", 1, "filler-2-frame", Fix);
        guard.Check("filler-3", 1, "filler-3-frame", Fix); // pushes "victim" out

        // The cache forgot it, so it is accepted again — which is exactly why the DURABLE ledger,
        // not this process-local cache, is the authority the threat model relies on.
        Assert.Equal(ReplayOutcome.Accept, guard.Check("victim", 500, "victim-frame", Fix).Outcome);
    }
}
