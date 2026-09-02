using Opstrax.Telematics.Gateway.Quality;

namespace Opstrax.Telematics.Security.Tests;

/// <summary>
/// Tests for the fix-continuity guard: the producer of the
/// <c>TeleportSuspected</c> and <c>ImpossibleSpeed</c> quality flags that the canonical event has
/// always declared, the projection has always persisted, and nothing ever set.
/// </summary>
/// <remarks>
/// The negative tests matter as much as the positive ones. A plausibility signal that cries wolf
/// on a parked truck gets muted within a day and is then worth less than no signal at all, because
/// the dashboard still claims the check exists.
/// </remarks>
public class FixPlausibilityGuardTests
{
    private const string Device = "dev-known-0001";
    private static readonly DateTime T0 = new(2024, 1, 15, 10, 20, 30, DateTimeKind.Utc);

    // Washington DC and Tokyo: the OpsTrax fleet's actual operating area, and somewhere it is not.
    private const double DcLat = 38.8951, DcLng = -77.0364;
    private const double TokyoLat = 35.6762, TokyoLng = 139.6503;

    // ── Distance maths, checked against known ground truth ─────────────────────

    /// <summary>
    /// Haversine sanity, because every verdict below is derived from it. The published
    /// great-circle distances are used, not distances this class computed for itself.
    /// </summary>
    [Theory]
    [InlineData(DcLat, DcLng, TokyoLat, TokyoLng, 10_900_000, 400_000)]   // DC → Tokyo ≈ 10 900 km
    [InlineData(DcLat, DcLng, DcLat, DcLng, 0, 1)]                        // identity
    [InlineData(0, 0, 0, 1, 111_195, 500)]                                // 1° of longitude at the equator
    [InlineData(0, 0, 1, 0, 111_195, 500)]                                // 1° of latitude
    public void Distance_matches_known_great_circle_values(
        double lat1, double lng1, double lat2, double lng2, double expectedMetres, double toleranceMetres)
    {
        double actual = FixPlausibilityGuard.DistanceMetres(lat1, lng1, lat2, lng2);
        Assert.True(Math.Abs(actual - expectedMetres) <= toleranceMetres,
            $"expected ~{expectedMetres} m (±{toleranceMetres}), got {actual:F0} m");
    }

    // ── The signal fires when it should ────────────────────────────────────────

    /// <summary>A vehicle cannot cross the Pacific between two fixes a minute apart.</summary>
    [Fact]
    public void An_intercontinental_jump_between_consecutive_fixes_is_flagged()
    {
        var guard = new FixPlausibilityGuard();

        Assert.False(guard.Evaluate(Device, DcLat, DcLng, T0).TeleportSuspected);

        PlausibilityVerdict jump = guard.Evaluate(Device, TokyoLat, TokyoLng, T0.AddMinutes(1));

        Assert.True(jump.TeleportSuspected);
        Assert.NotNull(jump.ImpliedSpeedKph);
        Assert.True(jump.ImpliedSpeedKph > 100_000);
    }

    /// <summary>
    /// A mirrored hemisphere is a valid coordinate, so no range check can see it. Continuity can,
    /// but only at the moment the reading changes.
    /// </summary>
    [Fact]
    public void A_hemisphere_mirror_appearing_mid_stream_is_flagged()
    {
        var guard = new FixPlausibilityGuard();

        guard.Evaluate(Device, TokyoLat, TokyoLng, T0);

        // The same physical position, decoded with latitude and longitude signs inverted.
        PlausibilityVerdict mirrored = guard.Evaluate(Device, -TokyoLat, -TokyoLng, T0.AddSeconds(10));

        Assert.True(mirrored.TeleportSuspected);
    }

    /// <summary>The device's own claimed speed is bounded independently of displacement.</summary>
    [Fact]
    public void A_reported_speed_above_the_ceiling_is_flagged_on_its_own()
    {
        var guard = new FixPlausibilityGuard();

        PlausibilityVerdict verdict = guard.Evaluate(Device, DcLat, DcLng, T0, reportedSpeedKph: 900);

        Assert.True(verdict.ImpossibleSpeed);
        Assert.False(verdict.TeleportSuspected); // no previous fix to displace from
    }

    // ── The signal stays quiet when it should ──────────────────────────────────

    /// <summary>Ordinary driving is not an incident.</summary>
    [Theory]
    [InlineData(60)]
    [InlineData(120)]
    [InlineData(300)]
    public void Ordinary_road_speeds_are_not_flagged(double kph)
    {
        var guard = new FixPlausibilityGuard();
        guard.Evaluate(Device, 38.0, -77.0, T0);

        // Drive north for an hour at the given speed: 1 degree of latitude ≈ 111.195 km.
        double degrees = kph / 111.195;
        PlausibilityVerdict verdict = guard.Evaluate(Device, 38.0 + degrees, -77.0, T0.AddHours(1));

        Assert.False(verdict.TeleportSuspected);
    }

    /// <summary>
    /// The reason a noise floor exists. Consumer GNSS scatters tens of metres while stationary, and
    /// a small scatter over a short interval implies an enormous speed: 50 m one second apart is
    /// 180 km/h. Without the floor a parked truck alerts on every heartbeat and the signal is muted
    /// within a day, leaving a dashboard that claims a check nobody is reading.
    /// </summary>
    [Theory]
    [InlineData(1)]
    [InlineData(5)]
    [InlineData(30)]
    public void Gnss_scatter_on_a_stationary_vehicle_is_not_flagged(int secondsApart)
    {
        var guard = new FixPlausibilityGuard();
        guard.Evaluate(Device, DcLat, DcLng, T0);

        // ~40 m of jitter: well inside the noise floor, absurd as an implied speed.
        PlausibilityVerdict verdict = guard.Evaluate(
            Device, DcLat + 0.00036, DcLng, T0.AddSeconds(secondsApart));

        Assert.False(verdict.TeleportSuspected);
    }

    /// <summary>A device's first ever fix has nothing to be continuous with.</summary>
    [Fact]
    public void A_first_fix_is_never_flagged()
    {
        var guard = new FixPlausibilityGuard();
        Assert.False(guard.Evaluate(Device, TokyoLat, TokyoLng, T0).TeleportSuspected);
    }

    /// <summary>Devices are evaluated independently; one vehicle's motion is not another's.</summary>
    [Fact]
    public void Devices_are_evaluated_independently()
    {
        var guard = new FixPlausibilityGuard();

        guard.Evaluate("device-a", DcLat, DcLng, T0);
        Assert.False(guard.Evaluate("device-b", TokyoLat, TokyoLng, T0).TeleportSuspected);
    }

    /// <summary>
    /// An out-of-order frame is evaluated but never becomes the baseline, so a delayed packet
    /// cannot rewrite where the guard believes a vehicle is.
    /// </summary>
    [Fact]
    public void An_out_of_order_fix_does_not_become_the_baseline()
    {
        var guard = new FixPlausibilityGuard();

        guard.Evaluate(Device, 38.0, -77.0, T0.AddMinutes(10));
        guard.Evaluate(Device, TokyoLat, TokyoLng, T0);           // older; must not take over

        // Continuing from the ORIGINAL position at a road speed stays clean. If the stale Tokyo fix
        // had become the baseline, this would read as an intercontinental jump.
        PlausibilityVerdict next = guard.Evaluate(Device, 38.01, -77.0, T0.AddMinutes(20));
        Assert.False(next.TeleportSuspected);
    }

    /// <summary>
    /// A flagged fix still becomes the baseline. Refusing to advance would pin the device to a
    /// position it has left, so one bad reading would flag every fix that followed it forever —
    /// an alert storm from a single incident.
    /// </summary>
    [Fact]
    public void A_flagged_fix_still_advances_the_baseline_so_one_incident_stays_one_incident()
    {
        var guard = new FixPlausibilityGuard();

        guard.Evaluate(Device, DcLat, DcLng, T0);
        Assert.True(guard.Evaluate(Device, TokyoLat, TokyoLng, T0.AddMinutes(1)).TeleportSuspected);

        // Now genuinely driving around Tokyo: clean again.
        Assert.False(guard.Evaluate(Device, TokyoLat + 0.01, TokyoLng, T0.AddMinutes(11)).TeleportSuspected);
        Assert.False(guard.Evaluate(Device, TokyoLat + 0.02, TokyoLng, T0.AddMinutes(21)).TeleportSuspected);
    }

    // ── The limit of this control, asserted rather than assumed ────────────────

    /// <summary>
    /// <b>This control cannot catch a decoder that has been uniformly wrong since birth.</b>
    /// Consecutive equally-wrong fixes sit beside each other and imply an ordinary speed, so
    /// continuity has nothing to object to. The hemisphere defect would have looked exactly like
    /// this on a fleet that never crossed a hemisphere boundary.
    /// </summary>
    /// <remarks>
    /// Pinned as a test so the limitation is a documented property rather than folklore. Catching
    /// this class of fault needs a comparison against known ground truth — a commissioning-time
    /// check that the decoded position matches where the device physically is — which is a bench
    /// procedure, not a runtime one.
    /// </remarks>
    [Fact]
    public void A_uniformly_mirrored_decoder_is_NOT_caught_and_that_is_a_known_limit()
    {
        var guard = new FixPlausibilityGuard();

        // Every fix mirrored the same way, from the very first one: a truck driving around Tokyo
        // rendered as a truck driving around the South Atlantic, consistently.
        PlausibilityVerdict first = guard.Evaluate(Device, -TokyoLat, -TokyoLng, T0);
        PlausibilityVerdict second = guard.Evaluate(Device, -(TokyoLat + 0.01), -TokyoLng, T0.AddMinutes(10));
        PlausibilityVerdict third = guard.Evaluate(Device, -(TokyoLat + 0.02), -TokyoLng, T0.AddMinutes(20));

        Assert.False(first.TeleportSuspected);
        Assert.False(second.TeleportSuspected);
        Assert.False(third.TeleportSuspected);
    }

    // ── Bounded memory ─────────────────────────────────────────────────────────

    /// <summary>
    /// The device map is bounded. A public port sees forged identifiers, and a per-device baseline
    /// store keyed on an attacker-chosen string is the same unbounded-collection defect the replay
    /// guard's device map had.
    /// </summary>
    [Fact]
    public void The_tracked_device_map_is_bounded()
    {
        var guard = new FixPlausibilityGuard(maxTrackedDevices: 100);

        for (int i = 0; i < 5_000; i++)
            guard.Evaluate($"forged-{i}", DcLat, DcLng, T0);

        Assert.True(guard.TrackedDeviceCount <= 100,
            $"device map grew to {guard.TrackedDeviceCount}, above the ceiling");
    }

    /// <summary>Eviction only forgets a baseline, so it can never manufacture a false alert.</summary>
    [Fact]
    public void Eviction_can_only_miss_an_alert_never_invent_one()
    {
        var guard = new FixPlausibilityGuard(maxTrackedDevices: 2);

        guard.Evaluate("victim", DcLat, DcLng, T0);
        guard.Evaluate("filler-1", DcLat, DcLng, T0);
        guard.Evaluate("filler-2", DcLat, DcLng, T0);
        guard.Evaluate("filler-3", DcLat, DcLng, T0);   // pushes "victim" out

        // Its baseline is gone, so this reads as a first fix rather than as a jump.
        Assert.False(guard.Evaluate("victim", TokyoLat, TokyoLng, T0.AddSeconds(1)).TeleportSuspected);
    }


    // ── Distinct-device aggregation: the shape of the spike is the diagnosis ───

    /// <summary>
    /// One device flagging repeatedly is one affected vehicle, not a fleet event. If this counted
    /// occurrences instead of devices, a single tampered unit reporting every ten seconds would
    /// look exactly like a decoder regression and would page the wrong response.
    /// </summary>
    [Fact]
    public void One_device_flagging_repeatedly_counts_as_one_affected_device()
    {
        var guard = new FixPlausibilityGuard();
        DateTime now = T0;

        guard.Evaluate(Device, DcLat, DcLng, now);
        for (int i = 1; i <= 10; i++)
        {
            // Alternate continents every minute: flags every time.
            bool tokyo = i % 2 == 1;
            guard.Evaluate(Device, tokyo ? TokyoLat : DcLat, tokyo ? TokyoLng : DcLng, now.AddMinutes(i));
        }

        Assert.Equal(1, guard.DistinctDevicesFlaggedWithin(TimeSpan.FromMinutes(10), DateTime.UtcNow));
    }

    /// <summary>
    /// Many devices flagging at once is the decoder-regression signature, and it must be
    /// distinguishable from the single-device case by the metric alone.
    /// </summary>
    [Fact]
    public void Many_devices_flagging_at_once_is_reported_as_many()
    {
        var guard = new FixPlausibilityGuard();

        for (int i = 0; i < 12; i++)
        {
            string device = $"device-{i}";
            guard.Evaluate(device, DcLat, DcLng, T0);
            guard.Evaluate(device, TokyoLat, TokyoLng, T0.AddMinutes(1));
        }

        Assert.Equal(12, guard.DistinctDevicesFlaggedWithin(TimeSpan.FromMinutes(10), DateTime.UtcNow));
    }

    /// <summary>A clean fleet reports zero, so the gauge is not simply always positive.</summary>
    [Fact]
    public void A_clean_fleet_reports_no_affected_devices()
    {
        var guard = new FixPlausibilityGuard();

        for (int i = 0; i < 12; i++)
        {
            string device = $"device-{i}";
            guard.Evaluate(device, 38.0, -77.0, T0);
            guard.Evaluate(device, 38.01, -77.0, T0.AddMinutes(10));
        }

        Assert.Equal(0, guard.DistinctDevicesFlaggedWithin(TimeSpan.FromMinutes(10), DateTime.UtcNow));
    }

    /// <summary>The count is a window, not a running total: an old incident stops paging.</summary>
    [Fact]
    public void Flagged_devices_age_out_of_the_window()
    {
        var guard = new FixPlausibilityGuard();

        guard.Evaluate(Device, DcLat, DcLng, T0);
        guard.Evaluate(Device, TokyoLat, TokyoLng, T0.AddMinutes(1));

        Assert.Equal(1, guard.DistinctDevicesFlaggedWithin(TimeSpan.FromMinutes(10), DateTime.UtcNow));
        Assert.Equal(0, guard.DistinctDevicesFlaggedWithin(TimeSpan.FromMinutes(10), DateTime.UtcNow.AddHours(1)));
    }

    [Fact]
    public void InvalidConstructorArgs_Throw()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new FixPlausibilityGuard(maxGroundSpeedKph: 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => new FixPlausibilityGuard(noiseFloorMetres: -1));
        Assert.Throws<ArgumentOutOfRangeException>(() => new FixPlausibilityGuard(maxTrackedDevices: 0));
    }

    [Fact]
    public void Concurrent_evaluation_is_safe_and_never_throws()
    {
        var guard = new FixPlausibilityGuard();
        const int threads = 16;
        var workers = new Thread[threads];
        var failures = new List<Exception>();

        for (int t = 0; t < threads; t++)
        {
            int index = t;
            workers[index] = new Thread(() =>
            {
                try
                {
                    for (int i = 0; i < 500; i++)
                        guard.Evaluate($"device-{index % 4}", 38.0 + (i * 0.001), -77.0, T0.AddSeconds(i * 10));
                }
                catch (Exception ex)
                {
                    lock (failures) failures.Add(ex);
                }
            }) { IsBackground = true };
            workers[index].Start();
        }

        foreach (Thread worker in workers)
            Assert.True(worker.Join(TimeSpan.FromSeconds(30)), "a worker thread did not finish");

        Assert.Empty(failures);
    }
}
