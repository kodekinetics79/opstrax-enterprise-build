using Opstrax.Api.Services;

namespace Opstrax.Tests;

public sealed class HosShadowClockServiceTests
{
    [Fact]
    public void CanadaDayAnchorUsesCarrierDesignatedLocalHourNotUtcMidnight()
    {
        // 08:00Z = 04:00 in Toronto on Sep 3. With a carrier day beginning
        // at 06:00 local, the active HOS day began Sep 2 at 06:00 EDT = 10:00Z.
        var result = HosShadowClockService.ResolveAnchors(
            new DateTimeOffset(2026, 9, 3, 8, 0, 0, TimeSpan.Zero),
            "America/Toronto",
            new TimeOnly(6, 0),
            null);

        Assert.True(result.Valid);
        Assert.Equal(new DateOnly(2026, 9, 2), result.LocalDate);
        Assert.Equal(new DateTimeOffset(2026, 9, 2, 10, 0, 0, TimeSpan.Zero), result.DayStart);
        Assert.Null(result.WeekStart);
        Assert.Empty(result.ReviewFlags);
    }

    [Fact]
    public void SaudiWeekAnchorUsesExplicitCarrierIsoWeekStartAndLocalDayStart()
    {
        // Riyadh is UTC+3. Thu Sep 3 12:00Z = 15:00 local. Carrier day begins
        // at 04:00, and week_start_iso=7 means Sunday.
        var result = HosShadowClockService.ResolveAnchors(
            new DateTimeOffset(2026, 9, 3, 12, 0, 0, TimeSpan.Zero),
            "Asia/Riyadh",
            new TimeOnly(4, 0),
            7);

        Assert.True(result.Valid);
        Assert.Equal(new DateOnly(2026, 9, 3), result.LocalDate);
        Assert.Equal(new DateTimeOffset(2026, 9, 3, 1, 0, 0, TimeSpan.Zero), result.DayStart);
        Assert.Equal(new DateTimeOffset(2026, 8, 30, 1, 0, 0, TimeSpan.Zero), result.WeekStart);
    }

    [Fact]
    public void NonexistentDstCarrierAnchorFailsClosedInsteadOfGuessing()
    {
        // Toronto springs forward at 02:00 on 2026-03-08. 02:30 never occurs.
        var result = HosShadowClockService.ResolveAnchors(
            new DateTimeOffset(2026, 3, 8, 18, 0, 0, TimeSpan.Zero),
            "America/Toronto",
            new TimeOnly(2, 30),
            null);

        Assert.False(result.Valid);
        Assert.Null(result.DayStart);
        Assert.Contains("HOS_POLICY_DAY_ANCHOR_DST_INVALID", result.ReviewFlags);
    }

    [Fact]
    public void RepeatedDstCarrierAnchorFailsClosedInsteadOfChoosingAnOffset()
    {
        // 01:30 occurs twice when Toronto falls back on 2026-11-01. Selecting an
        // offset without an explicit carrier rule would change the elapsed clock.
        var result = HosShadowClockService.ResolveAnchors(
            new DateTimeOffset(2026, 11, 1, 18, 0, 0, TimeSpan.Zero),
            "America/Toronto",
            new TimeOnly(1, 30),
            null);

        Assert.False(result.Valid);
        Assert.Null(result.DayStart);
        Assert.Contains("HOS_POLICY_DAY_ANCHOR_DST_AMBIGUOUS", result.ReviewFlags);
    }

    [Fact]
    public void UnknownTimezoneFailsClosed()
    {
        var result = HosShadowClockService.ResolveAnchors(
            new DateTimeOffset(2026, 9, 3, 12, 0, 0, TimeSpan.Zero),
            "Not/A-Real-Zone",
            new TimeOnly(0, 0),
            null);

        Assert.False(result.Valid);
        Assert.Contains("HOS_POLICY_TIMEZONE_UNKNOWN", result.ReviewFlags);
    }

    [Fact]
    public void SourceWatermarkIsOrderIndependentAndMinuteStable()
    {
        var first = Event(
            1,
            new DateTimeOffset(2026, 9, 3, 10, 0, 0, TimeSpan.Zero),
            new DateTimeOffset(2026, 9, 3, 11, 0, 0, TimeSpan.Zero),
            "Driving",
            "evt-1");
        var second = Event(
            2,
            new DateTimeOffset(2026, 9, 3, 11, 0, 0, TimeSpan.Zero),
            new DateTimeOffset(2026, 9, 3, 12, 0, 0, TimeSpan.Zero),
            "Off Duty",
            "evt-2");

        var a = HosShadowClockService.ComputeSourceWatermark(
            44, 3, HosComplianceCalculator.CanadaSouth60,
            new DateTimeOffset(2026, 9, 3, 12, 15, 2, TimeSpan.Zero),
            [first, second]);
        var b = HosShadowClockService.ComputeSourceWatermark(
            44, 3, HosComplianceCalculator.CanadaSouth60,
            new DateTimeOffset(2026, 9, 3, 12, 15, 58, TimeSpan.Zero),
            [second, first]);

        Assert.Equal(a, b);
        Assert.Equal(64, a.Length);
    }

    [Fact]
    public void SourceWatermarkChangesWhenEvidenceOrPolicyVersionChanges()
    {
        var driving = Event(
            1,
            new DateTimeOffset(2026, 9, 3, 10, 0, 0, TimeSpan.Zero),
            new DateTimeOffset(2026, 9, 3, 11, 0, 0, TimeSpan.Zero),
            "Driving",
            "evt-1");
        var edited = driving with { Status = "On Duty (Not Driving)" };
        var asOf = new DateTimeOffset(2026, 9, 3, 12, 15, 0, TimeSpan.Zero);

        var original = HosShadowClockService.ComputeSourceWatermark(
            44, 3, HosComplianceCalculator.CanadaSouth60, asOf, [driving]);
        var changedEvidence = HosShadowClockService.ComputeSourceWatermark(
            44, 3, HosComplianceCalculator.CanadaSouth60, asOf, [edited]);
        var changedPolicy = HosShadowClockService.ComputeSourceWatermark(
            44, 4, HosComplianceCalculator.CanadaSouth60, asOf, [driving]);

        Assert.NotEqual(original, changedEvidence);
        Assert.NotEqual(original, changedPolicy);
    }

    private static HosShadowClockService.SourceFingerprint Event(
        long id,
        DateTimeOffset start,
        DateTimeOffset end,
        string status,
        string eventId)
        => new(
            id,
            start,
            end,
            status,
            "provider",
            "Motive",
            eventId,
            new string('a', 64),
            id.ToString(),
            end.AddMinutes(1),
            true);
}