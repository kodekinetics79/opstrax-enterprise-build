using Opstrax.Api.Services;

namespace Opstrax.Tests;

public sealed class HosComplianceCalculatorTests
{
    private static readonly DateTimeOffset AsOf = new(2026, 9, 3, 18, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset DayStart = new(2026, 9, 3, 0, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset CoverageStart = AsOf.AddDays(-14);
    private static readonly DateTimeOffset WeekStart = new(2026, 8, 31, 0, 0, 0, TimeSpan.Zero);

    [Fact]
    public void CanadaSouth_ExactlyAtThirteenHoursStopsDrivingWithoutCreatingOverage()
    {
        var segments = CoverWithOffDutyThen(
            DayStart,
            Segment(DayStart, DayStart.AddHours(13), "Driving"),
            Segment(DayStart.AddHours(13), AsOf, "Off Duty"));

        var result = EvalCanadaSouth(segments);

        Assert.Equal(0, result.DriveRemainingMinutes);
        Assert.DoesNotContain("CA_S60_DAILY_DRIVE_LIMIT", result.Violations);
        Assert.False(result.CanDrive);
    }

    [Fact]
    public void CanadaSouth_OneMinuteOverThirteenHoursIsViolation()
    {
        var asOf = DayStart.AddHours(13).AddMinutes(1);
        var segments = CoverWithOffDutyThen(
            DayStart,
            Segment(DayStart, asOf, "Driving"));

        var result = EvalCanadaSouth(segments, asOf);

        Assert.Contains("CA_S60_DAILY_DRIVE_LIMIT", result.Violations);
        Assert.Equal("Violation", result.Status);
        Assert.False(result.CanDrive);
    }

    [Fact]
    public void CanadaSouth_OneMinutePastFourteenOnDutyWhileDrivingIsViolation()
    {
        var asOf = DayStart.AddHours(14).AddMinutes(1);
        var segments = CoverWithOffDutyThen(
            DayStart,
            Segment(DayStart, DayStart.AddHours(10), "On Duty (Not Driving)"),
            Segment(DayStart.AddHours(10), asOf, "Driving"));

        var result = EvalCanadaSouth(segments, asOf);

        Assert.Contains("CA_S60_DAILY_ON_DUTY_LIMIT", result.Violations);
        Assert.Contains("CA_S60_ON_DUTY_SINCE_8H_REST", result.Violations);
        Assert.Equal(0, result.DriveRemainingMinutes);
    }

    [Fact]
    public void CanadaSouth_ElapsedClockBlocksDrivingAfterSixteenHours()
    {
        var asOf = DayStart.AddHours(16).AddMinutes(1);
        var segments = CoverWithOffDutyThen(
            DayStart,
            Segment(DayStart, DayStart.AddHours(4), "Off Duty"),
            Segment(DayStart.AddHours(4), DayStart.AddHours(12), "On Duty (Not Driving)"),
            Segment(DayStart.AddHours(12), asOf, "Driving"));

        var result = EvalCanadaSouth(segments, asOf);

        Assert.Contains("CA_S60_ELAPSED_LIMIT", result.Violations);
        Assert.Equal(0, result.ShiftRemainingMinutes);
        Assert.False(result.CanDrive);
    }

    [Fact]
    public void CanadaSouth_CycleOneBlocksAfterSeventyOnDutyHours()
    {
        var dayStart = new DateTimeOffset(2026, 9, 6, 0, 0, 0, TimeSpan.Zero);
        var asOf = dayStart.AddHours(4).AddMinutes(1);
        var coverageStart = asOf.AddDays(-14);
        var segments = new List<HosComplianceCalculator.DutySegment>
        {
            Segment(coverageStart, dayStart.AddDays(-7), "Off Duty")
        };

        // 6 previous days x 11h on duty = 66h. Each 13h rest satisfies the
        // daily 8h reset without creating a 36h cycle reset.
        for (var i = 7; i >= 2; i--)
        {
            var start = dayStart.AddDays(-i + 1);
            segments.Add(Segment(start, start.AddHours(11), "On Duty (Not Driving)"));
            segments.Add(Segment(start.AddHours(11), start.AddDays(1), "Off Duty"));
        }
        segments.Add(Segment(dayStart.AddDays(-1), dayStart, "Off Duty"));
        segments.Add(Segment(dayStart, asOf, "Driving"));

        var result = HosComplianceCalculator.Evaluate(new(
            HosComplianceCalculator.CanadaSouth60,
            asOf,
            coverageStart,
            dayStart,
            "Cycle 1",
            segments));

        Assert.True(result.Metrics["cycle_used_minutes"] > 70 * 60);
        Assert.Contains("CA_CYCLE1_LIMIT", result.Violations);
        Assert.Equal(0, result.CycleRemainingMinutes);
    }

    [Fact]
    public void CanadaNorth_AfterEightHourResetDoesNotInventSouthStyleDailyCap()
    {
        var asOf = DayStart.AddHours(23).AddMinutes(30);
        var segments = CoverWithOffDutyThen(
            DayStart,
            Segment(DayStart, DayStart.AddHours(5), "Driving"),
            Segment(DayStart.AddHours(5), DayStart.AddHours(13), "Off Duty"),
            Segment(DayStart.AddHours(13), asOf, "Driving"));

        var result = HosComplianceCalculator.Evaluate(new(
            HosComplianceCalculator.CanadaNorth60,
            asOf,
            CoverageStart,
            DayStart,
            "Cycle 1",
            segments));

        Assert.DoesNotContain("CA_N60_DRIVE_LIMIT", result.Violations);
        Assert.True(result.DriveRemainingMinutes > 0);
    }

    [Fact]
    public void Canada_YardMoveCountsAsOnDutyAndUnvalidatedPersonalConveyanceFailsClosed()
    {
        var segments = CoverWithOffDutyThen(
            DayStart,
            Segment(DayStart, DayStart.AddHours(3), "Yard Move"),
            Segment(DayStart.AddHours(3), DayStart.AddHours(4), "Personal Conveyance"),
            Segment(DayStart.AddHours(4), AsOf, "Off Duty"));

        var result = EvalCanadaSouth(segments);

        Assert.Equal(180m, result.Metrics["on_duty_today_minutes"]);
        Assert.Contains("CA_PERSONAL_CONVEYANCE_VALIDATION_REQUIRED", result.ReviewFlags);
        Assert.Equal("Unverified", result.Status);
        Assert.False(result.CanDrive);
    }

    [Fact]
    public void Saudi_DefaultDayStopsAtNineHoursUnlessExtensionIsEvidenceBacked()
    {
        var asOf = DayStart.AddHours(9);
        var segments = CoverWithOffDutyThen(DayStart, Segment(DayStart, asOf, "Driving"));

        var normal = EvalSaudi(segments, asOf);
        var extended = EvalSaudi(segments, asOf, extensionAuthorized: true, extensionEvidence: true);

        Assert.Equal(0, normal.DriveRemainingMinutes);
        Assert.Equal(60, extended.DriveRemainingMinutes);
        Assert.Equal(540m, normal.Metrics["today_drive_limit_minutes"]);
        Assert.Equal(600m, extended.Metrics["today_drive_limit_minutes"]);
    }

    [Fact]
    public void Saudi_TenHourExtensionWithoutEvidenceFailsClosed()
    {
        var asOf = DayStart.AddHours(9).AddMinutes(1);
        var segments = CoverWithOffDutyThen(DayStart, Segment(DayStart, asOf, "Driving"));

        var result = EvalSaudi(segments, asOf, extensionAuthorized: true, extensionEvidence: false);

        Assert.Contains("SA_10H_EXTENSION_EVIDENCE_REQUIRED", result.ReviewFlags);
        Assert.Contains("SA_9H_DAILY_DRIVE_LIMIT_WITHOUT_VALID_EXTENSION", result.Violations);
        Assert.False(result.CanDrive);
    }

    [Fact]
    public void Saudi_ThirdWeeklyExtensionIsRejected()
    {
        var dayStart = new DateTimeOffset(2026, 9, 3, 0, 0, 0, TimeSpan.Zero);
        var asOf = dayStart.AddHours(9).AddMinutes(1);
        var coverageStart = asOf.AddDays(-14);
        var weekStart = new DateTimeOffset(2026, 8, 31, 0, 0, 0, TimeSpan.Zero);
        var segments = new List<HosComplianceCalculator.DutySegment>
        {
            Segment(coverageStart, weekStart, "Off Duty")
        };
        AddSaudiDay(segments, weekStart, 9 * 60 + 30);
        AddSaudiDay(segments, weekStart.AddDays(1), 9 * 60 + 15);
        AddSaudiDay(segments, weekStart.AddDays(2), 60);
        segments.Add(Segment(dayStart, asOf, "Driving"));

        var result = HosComplianceCalculator.Evaluate(new(
            HosComplianceCalculator.SaudiTgaGoods,
            asOf,
            coverageStart,
            dayStart,
            "TGA",
            segments,
            weekStart,
            SaudiExtensionAuthorizedToday: true,
            SaudiExtensionEvidencePresent: true));

        Assert.Equal(2m, result.Metrics["prior_10h_extension_days"]);
        Assert.Contains("SA_10H_EXTENSION_MORE_THAN_TWICE_WEEKLY", result.Violations);
        Assert.False(result.CanDrive);
    }

    [Fact]
    public void Saudi_ContinuousDrivingPastFourAndHalfHoursRequiresBreak()
    {
        var asOf = DayStart.AddHours(4).AddMinutes(31);
        var segments = CoverWithOffDutyThen(DayStart, Segment(DayStart, asOf, "Driving"));

        var result = EvalSaudi(segments, asOf);

        Assert.Contains("SA_45M_BREAK_AFTER_4_5H_DRIVING", result.Violations);
        Assert.Equal(0, result.BreakRemainingMinutes);
        Assert.False(result.CanDrive);
    }

    [Fact]
    public void Saudi_QualifyingFortyFiveMinuteBreakResetsContinuousDrivingClock()
    {
        var asOf = DayStart.AddHours(8).AddMinutes(45);
        var segments = CoverWithOffDutyThen(
            DayStart,
            Segment(DayStart, DayStart.AddHours(4), "Driving"),
            Segment(DayStart.AddHours(4), DayStart.AddHours(4).AddMinutes(45), "Off Duty"),
            Segment(DayStart.AddHours(4).AddMinutes(45), asOf, "Driving"));

        var result = EvalSaudi(segments, asOf);

        Assert.DoesNotContain("SA_45M_BREAK_AFTER_4_5H_DRIVING", result.Violations);
        Assert.Equal(30, result.BreakRemainingMinutes);
    }

    [Fact]
    public void MissingDutyStatusCoverageIsUnverifiedNotZeroFilled()
    {
        var segments = new[]
        {
            Segment(DayStart, DayStart.AddHours(2), "Driving"),
            // Two-hour gap: a compliant engine must not guess what happened.
            Segment(DayStart.AddHours(4), AsOf, "Off Duty")
        };

        var result = EvalCanadaSouth(segments);

        Assert.False(result.DataComplete);
        Assert.Contains("HOS_DUTY_STATUS_GAP", result.ReviewFlags);
        Assert.Equal("Unverified", result.Status);
        Assert.False(result.CanDrive);
    }

    private static HosComplianceCalculator.Result EvalCanadaSouth(
        IReadOnlyList<HosComplianceCalculator.DutySegment> segments,
        DateTimeOffset? asOf = null)
        => HosComplianceCalculator.Evaluate(new(
            HosComplianceCalculator.CanadaSouth60,
            asOf ?? AsOf,
            CoverageStart,
            DayStart,
            "Cycle 1",
            segments));

    private static HosComplianceCalculator.Result EvalSaudi(
        IReadOnlyList<HosComplianceCalculator.DutySegment> segments,
        DateTimeOffset? asOf = null,
        bool extensionAuthorized = false,
        bool extensionEvidence = false)
        => HosComplianceCalculator.Evaluate(new(
            HosComplianceCalculator.SaudiTgaGoods,
            asOf ?? AsOf,
            CoverageStart,
            DayStart,
            "TGA",
            segments,
            WeekStart,
            SaudiExtensionAuthorizedToday: extensionAuthorized,
            SaudiExtensionEvidencePresent: extensionEvidence));

    private static List<HosComplianceCalculator.DutySegment> CoverWithOffDutyThen(
        DateTimeOffset firstActivity,
        params HosComplianceCalculator.DutySegment[] activity)
    {
        var list = new List<HosComplianceCalculator.DutySegment>
        {
            Segment(CoverageStart, firstActivity, "Off Duty")
        };
        list.AddRange(activity);
        return list;
    }

    private static void AddSaudiDay(
        List<HosComplianceCalculator.DutySegment> segments,
        DateTimeOffset dayStart,
        int drivingMinutes)
    {
        segments.Add(Segment(dayStart, dayStart.AddMinutes(drivingMinutes), "Driving"));
        segments.Add(Segment(dayStart.AddMinutes(drivingMinutes), dayStart.AddDays(1), "Off Duty"));
    }

    private static HosComplianceCalculator.DutySegment Segment(
        DateTimeOffset start,
        DateTimeOffset end,
        string status)
        => new(start, end, status);
}