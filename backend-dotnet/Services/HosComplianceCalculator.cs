namespace Opstrax.Api.Services;

/// <summary>
/// Deterministic HOS clock calculator for the immediate Canada/KSA pilot lanes.
///
/// IMPORTANT REGULATORY BOUNDARY:
/// - This calculator does not certify an ELD, provider, device, carrier, or OpsTrax.
/// - It evaluates normalized duty-status segments against the core rule baselines
///   implemented here. Provider/device provenance and official certification /
///   qualification remain separate launch gates.
/// - Unsupported exception paths (split sleeper, Canadian deferral, unvalidated
///   personal conveyance, etc.) fail closed as ReviewRequired rather than silently
///   granting driving time.
///
/// Rule source/version is intentionally explicit so every persisted clock can be
/// traced back to the regulatory baseline used for its calculation.
/// </summary>
internal static class HosComplianceCalculator
{
    internal const string CanadaSouth60 = "CA-S60-SOR-2005-313-2026-06-21";
    internal const string CanadaNorth60 = "CA-N60-SOR-2005-313-2026-06-21";
    internal const string SaudiTgaGoods = "SA-TGA-GOODS-2026-09-03";

    internal enum DutyKind
    {
        OffDuty,
        SleeperBerth,
        Driving,
        OnDutyNotDriving,
        PersonalConveyance,
        YardMove,
        Unknown
    }

    internal sealed record DutySegment(
        DateTimeOffset Start,
        DateTimeOffset End,
        string Status,
        string? SourceEventId = null);

    internal sealed record Request(
        string RuleProfile,
        DateTimeOffset AsOf,
        DateTimeOffset CoverageStart,
        DateTimeOffset DayStart,
        string CycleType,
        IReadOnlyList<DutySegment> Segments,
        DateTimeOffset? WeekStart = null,
        bool CanadaOffDutyDeferralDeclared = false,
        bool PersonalConveyanceValidated = false,
        bool SaudiExtensionAuthorizedToday = false,
        bool SaudiExtensionEvidencePresent = false);

    internal sealed record Result(
        string RuleProfile,
        int DriveRemainingMinutes,
        int? ShiftRemainingMinutes,
        int CycleRemainingMinutes,
        int? BreakRemainingMinutes,
        bool DataComplete,
        bool ReviewRequired,
        bool CanDrive,
        string Status,
        IReadOnlyList<string> Violations,
        IReadOnlyList<string> ReviewFlags,
        IReadOnlyDictionary<string, decimal> Metrics);

    private sealed record NormalizedSegment(
        DateTimeOffset Start,
        DateTimeOffset End,
        DutyKind Kind,
        string RawStatus,
        string? SourceEventId)
    {
        internal int Minutes => WholeMinutes(Start, End);
    }

    private sealed record OffDutyBlock(DateTimeOffset Start, DateTimeOffset End)
    {
        internal int Minutes => WholeMinutes(Start, End);
    }

    internal static Result Evaluate(Request request)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(request.Segments);

        if (request.CoverageStart >= request.AsOf)
            throw new ArgumentException("CoverageStart must be before AsOf", nameof(request));
        if (request.DayStart > request.AsOf || request.DayStart.AddHours(24) <= request.AsOf)
            throw new ArgumentException("DayStart must identify the carrier-designated 24-hour day containing AsOf", nameof(request));

        var violations = new List<string>();
        var reviews = new List<string>();
        var normalized = Normalize(request, reviews, out var dataComplete);

        Result result = request.RuleProfile switch
        {
            CanadaSouth60 => EvaluateCanada(request, normalized, northOf60: false, dataComplete, violations, reviews),
            CanadaNorth60 => EvaluateCanada(request, normalized, northOf60: true, dataComplete, violations, reviews),
            SaudiTgaGoods => EvaluateSaudi(request, normalized, dataComplete, violations, reviews),
            _ => throw new ArgumentOutOfRangeException(nameof(request.RuleProfile), request.RuleProfile, "Unsupported HOS rule profile")
        };

        return result;
    }

    private static Result EvaluateCanada(
        Request request,
        IReadOnlyList<NormalizedSegment> segments,
        bool northOf60,
        bool dataComplete,
        List<string> violations,
        List<string> reviews)
    {
        var driveLimit = northOf60 ? 15 * 60 : 13 * 60;
        var dutyLimit = northOf60 ? 18 * 60 : 14 * 60;
        var elapsedLimit = northOf60 ? 20 * 60 : 16 * 60;
        var cycleOneLimit = northOf60 ? 80 * 60 : 70 * 60;
        const int cycleTwoLimit = 120 * 60;
        var cycleTwoIntermediateLimit = northOf60 ? 80 * 60 : 70 * 60;

        var dayEnd = request.DayStart.AddHours(24);
        var asOf = request.AsOf;
        var driveToday = SumMinutes(segments, request.DayStart, Min(asOf, dayEnd), IsDriving);
        var dutyToday = SumMinutes(segments, request.DayStart, Min(asOf, dayEnd), IsOnDuty);
        var offToday = SumMinutes(segments, request.DayStart, Min(asOf, dayEnd), IsOffDuty);

        if (request.CanadaOffDutyDeferralDeclared)
            AddReview(reviews, "CA_OFF_DUTY_DEFERRAL_REVIEW");

        // Personal conveyance is off-duty only when the statutory conditions are
        // satisfied (including the Canadian personal-use conditions). Do not grant
        // off-duty credit merely because an upstream payload used that label.
        if (!request.PersonalConveyanceValidated && segments.Any(s => s.Kind == DutyKind.PersonalConveyance))
            AddReview(reviews, "CA_PERSONAL_CONVEYANCE_VALIDATION_REQUIRED");

        // Split sleeper calculations alter elapsed-time treatment. Until the full
        // split-sleeper evidence model is implemented, detect candidates and fail
        // closed instead of treating them as ordinary consecutive rest.
        if (HasPotentialSplitSleeper(segments, northOf60 ? 8 * 60 : 10 * 60))
            AddReview(reviews, northOf60 ? "CA_N60_SPLIT_SLEEPER_REVIEW" : "CA_S60_SPLIT_SLEEPER_REVIEW");

        var offBlocks = OffDutyBlocks(segments, request.PersonalConveyanceValidated);
        var lastEightHourRest = offBlocks
            .Where(b => b.Minutes >= 8 * 60 && b.End <= asOf)
            .OrderByDescending(b => b.End)
            .FirstOrDefault();

        if (lastEightHourRest is null)
        {
            dataComplete = false;
            AddReview(reviews, "CA_8H_REST_HISTORY_INCOMPLETE");
        }

        var shiftStart = lastEightHourRest?.End ?? request.CoverageStart;
        var driveSinceRest = SumMinutes(segments, shiftStart, asOf, IsDriving);
        var dutySinceRest = SumMinutes(segments, shiftStart, asOf, IsOnDuty);
        var elapsedSinceRest = WholeMinutes(shiftStart, asOf);

        var dailyDriveRemaining = driveLimit - driveToday;
        var dailyDutyRemaining = dutyLimit - dutyToday;
        var shiftDriveRemaining = driveLimit - driveSinceRest;
        var shiftDutyRemaining = dutyLimit - dutySinceRest;
        var elapsedRemaining = elapsedLimit - elapsedSinceRest;

        if (driveToday > driveLimit)
            AddViolation(violations, northOf60 ? "CA_N60_DRIVE_LIMIT" : "CA_S60_DAILY_DRIVE_LIMIT");
        if (!northOf60 && dutyToday > dutyLimit)
            AddViolation(violations, "CA_S60_DAILY_ON_DUTY_LIMIT");
        if (driveSinceRest > driveLimit)
            AddViolation(violations, northOf60 ? "CA_N60_DRIVE_SINCE_8H_REST" : "CA_S60_DRIVE_SINCE_8H_REST");
        if (dutySinceRest > dutyLimit)
            AddViolation(violations, northOf60 ? "CA_N60_ON_DUTY_SINCE_8H_REST" : "CA_S60_ON_DUTY_SINCE_8H_REST");
        if (elapsedSinceRest > elapsedLimit)
            AddViolation(violations, northOf60 ? "CA_N60_ELAPSED_LIMIT" : "CA_S60_ELAPSED_LIMIT");

        // South-of-60 has the explicit daily 10-hour off-duty requirement. This
        // live clock records today's accumulated off-duty minutes, but the final
        // daily violation is intentionally evaluated only for completed days by a
        // separate daily-certification pass. A declared deferral remains review-only.
        if (!northOf60 && offToday < 10 * 60 && asOf >= dayEnd)
        {
            if (request.CanadaOffDutyDeferralDeclared)
                AddReview(reviews, "CA_DAILY_10H_OFF_DUTY_DEFERRAL_REVIEW");
            else
                AddViolation(violations, "CA_S60_DAILY_10H_OFF_DUTY");
        }

        var cycleTwo = IsCycleTwo(request.CycleType);
        var cycleDays = cycleTwo ? 14 : 7;
        var resetMinutes = cycleTwo ? 72 * 60 : 36 * 60;
        var rollingStart = asOf.AddDays(-cycleDays);
        var reset = offBlocks
            .Where(b => b.Minutes >= resetMinutes && b.End <= asOf)
            .OrderByDescending(b => b.End)
            .FirstOrDefault();
        var cycleStart = reset is { End: var end } && end > rollingStart ? end : rollingStart;
        var cycleUsed = SumMinutes(segments, cycleStart, asOf, IsOnDuty);
        var cycleLimit = cycleTwo ? cycleTwoLimit : cycleOneLimit;
        var cycleRemaining = cycleLimit - cycleUsed;

        if (cycleUsed > cycleLimit)
            AddViolation(violations, cycleTwo ? "CA_CYCLE2_LIMIT" : "CA_CYCLE1_LIMIT");

        // A driver may not drive without a 24-consecutive-hour off-duty period in
        // the preceding 14 days. For Cycle 2, the additional 70/80-hour limit also
        // applies until a 24-hour block has occurred.
        var last24 = offBlocks
            .Where(b => b.Minutes >= 24 * 60 && b.End <= asOf && b.End >= asOf.AddDays(-14))
            .OrderByDescending(b => b.End)
            .FirstOrDefault();

        if (last24 is null)
        {
            if (request.CoverageStart <= asOf.AddDays(-14))
                AddViolation(violations, "CA_24H_OFF_IN_PRECEDING_14_DAYS");
            else
            {
                dataComplete = false;
                AddReview(reviews, "CA_24H_OFF_HISTORY_INCOMPLETE");
            }
        }

        if (cycleTwo)
        {
            var since24Start = last24?.End ?? Max(request.CoverageStart, asOf.AddDays(-14));
            var dutySince24 = SumMinutes(segments, since24Start, asOf, IsOnDuty);
            var intermediateRemaining = cycleTwoIntermediateLimit - dutySince24;
            cycleRemaining = Math.Min(cycleRemaining, intermediateRemaining);
            if (dutySince24 > cycleTwoIntermediateLimit)
                AddViolation(violations, northOf60 ? "CA_N60_CYCLE2_80H_WITHOUT_24H_OFF" : "CA_S60_CYCLE2_70H_WITHOUT_24H_OFF");
        }

        var driveRemaining = MinNonNegative(dailyDriveRemaining, shiftDriveRemaining, dailyDutyRemaining,
            shiftDutyRemaining, elapsedRemaining, cycleRemaining);
        var shiftRemaining = Math.Max(0, Math.Min(Math.Min(dailyDutyRemaining, shiftDutyRemaining), elapsedRemaining));
        cycleRemaining = Math.Max(0, cycleRemaining);

        var metrics = new Dictionary<string, decimal>
        {
            ["drive_today_minutes"] = driveToday,
            ["on_duty_today_minutes"] = dutyToday,
            ["off_duty_today_minutes"] = offToday,
            ["drive_since_8h_rest_minutes"] = driveSinceRest,
            ["on_duty_since_8h_rest_minutes"] = dutySinceRest,
            ["elapsed_since_8h_rest_minutes"] = elapsedSinceRest,
            ["cycle_used_minutes"] = cycleUsed,
            ["cycle_limit_minutes"] = cycleLimit
        };

        return FinalizeResult(request.RuleProfile, driveRemaining, shiftRemaining, cycleRemaining, null,
            dataComplete, violations, reviews, metrics);
    }

    private static Result EvaluateSaudi(
        Request request,
        IReadOnlyList<NormalizedSegment> segments,
        bool dataComplete,
        List<string> violations,
        List<string> reviews)
    {
        if (request.WeekStart is null)
            throw new ArgumentException("Saudi TGA calculation requires WeekStart for the carrier schedule", nameof(request));
        if (request.WeekStart > request.AsOf || request.WeekStart.Value.AddDays(7) <= request.AsOf)
            throw new ArgumentException("WeekStart must identify the 7-day week containing AsOf", nameof(request));

        var weekStart = request.WeekStart.Value;
        var twoWeekStart = weekStart.AddDays(-7);
        var dayEnd = request.DayStart.AddHours(24);
        var asOf = request.AsOf;

        if (segments.Any(s => s.Kind == DutyKind.PersonalConveyance))
            AddReview(reviews, "SA_PERSONAL_USE_CLASSIFICATION_REVIEW");

        // Treat any 15/30-minute split-break pattern as review-required until the
        // provider's exact TGA-compliant rest coding is mapped. We never silently
        // grant a break based on ambiguous split records.
        if (HasSaudiSplitBreakCandidate(segments, asOf))
            AddReview(reviews, "SA_SPLIT_BREAK_REVIEW");

        var offBlocks = OffDutyBlocks(segments, personalConveyanceValidated: false);
        var lastDailyRest = offBlocks
            .Where(b => b.Minutes >= 11 * 60 && b.End <= asOf)
            .OrderByDescending(b => b.End)
            .FirstOrDefault();
        if (lastDailyRest is null)
        {
            dataComplete = false;
            AddReview(reviews, "SA_11H_REST_HISTORY_INCOMPLETE");
        }

        var driveToday = SumMinutes(segments, request.DayStart, Min(asOf, dayEnd), IsDriving);
        var driveThisWeek = SumMinutes(segments, weekStart, asOf, IsDriving);
        var driveTwoWeeks = SumMinutes(segments, twoWeekStart, asOf, IsDriving);

        var priorExtensionDays = CountSaudiExtensionDays(segments, weekStart, request.DayStart);
        var extensionAllowed = request.SaudiExtensionAuthorizedToday
            && request.SaudiExtensionEvidencePresent
            && priorExtensionDays < 2;
        var todayLimit = extensionAllowed ? 10 * 60 : 9 * 60;

        if (request.SaudiExtensionAuthorizedToday && !request.SaudiExtensionEvidencePresent)
            AddReview(reviews, "SA_10H_EXTENSION_EVIDENCE_REQUIRED");
        if (request.SaudiExtensionAuthorizedToday && priorExtensionDays >= 2)
            AddViolation(violations, "SA_10H_EXTENSION_MORE_THAN_TWICE_WEEKLY");
        if (driveToday > 10 * 60)
            AddViolation(violations, "SA_10H_ABSOLUTE_DAILY_DRIVE_LIMIT");
        else if (driveToday > 9 * 60 && !extensionAllowed)
            AddViolation(violations, "SA_9H_DAILY_DRIVE_LIMIT_WITHOUT_VALID_EXTENSION");

        if (driveThisWeek > 56 * 60)
            AddViolation(violations, "SA_56H_WEEKLY_DRIVE_LIMIT");
        if (driveTwoWeeks > 90 * 60)
            AddViolation(violations, "SA_90H_TWO_WEEK_DRIVE_LIMIT");

        var continuousDrive = ContinuousDrivingSinceQualifyingBreak(segments, asOf);
        var breakRemaining = 270 - continuousDrive;
        if (continuousDrive > 270)
            AddViolation(violations, "SA_45M_BREAK_AFTER_4_5H_DRIVING");

        // At least 48 consecutive hours weekly rest / no more than six consecutive
        // working days. The exact carrier week anchor is explicit in the request.
        var hasWeeklyRest = offBlocks.Any(b => b.Minutes >= 48 * 60 && b.End > twoWeekStart && b.End <= asOf);
        var consecutiveWorkDays = CountConsecutiveWorkDays(segments, request.DayStart, asOf, maxDays: 7);
        if (consecutiveWorkDays > 6 && !hasWeeklyRest)
            AddViolation(violations, "SA_MAX_6_CONSECUTIVE_WORK_DAYS");

        var lastWorkBeforeToday = segments
            .Where(s => s.End <= request.DayStart && IsOnDuty(s.Kind))
            .OrderByDescending(s => s.End)
            .FirstOrDefault();
        if (lastWorkBeforeToday is not null)
        {
            var restBetween = offBlocks
                .Where(b => b.Start >= lastWorkBeforeToday.End && b.End <= request.DayStart)
                .Sum(b => b.Minutes);
            var maxConsecutiveRest = offBlocks
                .Where(b => b.End <= request.DayStart && b.End > request.DayStart.AddDays(-2))
                .Select(b => b.Minutes)
                .DefaultIfEmpty(0)
                .Max();
            if (restBetween > 0 && maxConsecutiveRest < 11 * 60)
                AddViolation(violations, "SA_11H_DAILY_REST_REQUIREMENT");
        }

        var dailyRemaining = todayLimit - driveToday;
        var weeklyRemaining = 56 * 60 - driveThisWeek;
        var twoWeekRemaining = 90 * 60 - driveTwoWeeks;
        var driveRemaining = MinNonNegative(dailyRemaining, weeklyRemaining, twoWeekRemaining, breakRemaining);
        var cycleRemaining = Math.Max(0, Math.Min(weeklyRemaining, twoWeekRemaining));

        var metrics = new Dictionary<string, decimal>
        {
            ["drive_today_minutes"] = driveToday,
            ["drive_week_minutes"] = driveThisWeek,
            ["drive_two_week_minutes"] = driveTwoWeeks,
            ["prior_10h_extension_days"] = priorExtensionDays,
            ["today_drive_limit_minutes"] = todayLimit,
            ["continuous_drive_minutes"] = continuousDrive,
            ["consecutive_work_days"] = consecutiveWorkDays
        };

        return FinalizeResult(request.RuleProfile, driveRemaining, null, cycleRemaining, Math.Max(0, breakRemaining),
            dataComplete, violations, reviews, metrics);
    }

    private static Result FinalizeResult(
        string profile,
        int driveRemaining,
        int? shiftRemaining,
        int cycleRemaining,
        int? breakRemaining,
        bool dataComplete,
        List<string> violations,
        List<string> reviews,
        IReadOnlyDictionary<string, decimal> metrics)
    {
        var reviewRequired = reviews.Count > 0 || !dataComplete;
        var status = violations.Count > 0
            ? "Violation"
            : reviewRequired
                ? "Unverified"
                : IsWarning(driveRemaining, shiftRemaining, cycleRemaining, breakRemaining)
                    ? "Warning"
                    : "OK";
        var canDrive = dataComplete && !reviewRequired && violations.Count == 0 && driveRemaining > 0;

        return new Result(
            profile,
            Math.Max(0, driveRemaining),
            shiftRemaining is null ? null : Math.Max(0, shiftRemaining.Value),
            Math.Max(0, cycleRemaining),
            breakRemaining is null ? null : Math.Max(0, breakRemaining.Value),
            dataComplete,
            reviewRequired,
            canDrive,
            status,
            violations.AsReadOnly(),
            reviews.AsReadOnly(),
            metrics);
    }

    private static IReadOnlyList<NormalizedSegment> Normalize(Request request, List<string> reviews, out bool dataComplete)
    {
        dataComplete = true;
        var list = request.Segments
            .Where(s => s.End > request.CoverageStart && s.Start < request.AsOf)
            .Select(s => new NormalizedSegment(
                Max(s.Start, request.CoverageStart),
                Min(s.End, request.AsOf),
                ParseStatus(s.Status),
                s.Status,
                s.SourceEventId))
            .Where(s => s.End > s.Start)
            .OrderBy(s => s.Start)
            .ThenBy(s => s.End)
            .ToList();

        if (list.Count == 0)
        {
            dataComplete = false;
            AddReview(reviews, "HOS_NO_DUTY_STATUS_HISTORY");
            return list;
        }

        var cursor = request.CoverageStart;
        foreach (var segment in list)
        {
            if (segment.Kind == DutyKind.Unknown)
            {
                dataComplete = false;
                AddReview(reviews, $"HOS_UNKNOWN_STATUS:{segment.RawStatus}");
            }
            if (segment.Start > cursor.AddMinutes(1))
            {
                dataComplete = false;
                AddReview(reviews, "HOS_DUTY_STATUS_GAP");
            }
            if (segment.Start < cursor.AddMinutes(-1))
            {
                dataComplete = false;
                AddReview(reviews, "HOS_OVERLAPPING_DUTY_STATUS");
            }
            if (segment.End > cursor) cursor = segment.End;
        }
        if (cursor < request.AsOf.AddMinutes(-1))
        {
            dataComplete = false;
            AddReview(reviews, "HOS_DUTY_STATUS_GAP_AT_ASOF");
        }

        return list;
    }

    private static DutyKind ParseStatus(string? raw)
    {
        var status = (raw ?? string.Empty).Trim().ToLowerInvariant();
        return status switch
        {
            "off duty" or "off-duty" or "off_duty" => DutyKind.OffDuty,
            "sleeper berth" or "sleeper" or "sleeper_berth" => DutyKind.SleeperBerth,
            "driving" or "drive" => DutyKind.Driving,
            "on duty" or "on-duty" or "on duty (not driving)" or "on-duty not driving" or "on_duty_not_driving" => DutyKind.OnDutyNotDriving,
            "personal conveyance" or "personal use" or "personal_conveyance" => DutyKind.PersonalConveyance,
            "yard move" or "yard_move" => DutyKind.YardMove,
            _ => DutyKind.Unknown
        };
    }

    private static bool IsDriving(DutyKind kind) => kind == DutyKind.Driving;

    private static bool IsOnDuty(DutyKind kind) => kind is DutyKind.Driving or DutyKind.OnDutyNotDriving or DutyKind.YardMove;

    private static bool IsOffDuty(DutyKind kind) => kind is DutyKind.OffDuty or DutyKind.SleeperBerth;

    private static IReadOnlyList<OffDutyBlock> OffDutyBlocks(
        IReadOnlyList<NormalizedSegment> segments,
        bool personalConveyanceValidated)
    {
        var blocks = new List<OffDutyBlock>();
        DateTimeOffset? start = null;
        DateTimeOffset? end = null;

        foreach (var segment in segments)
        {
            var off = IsOffDuty(segment.Kind) || (personalConveyanceValidated && segment.Kind == DutyKind.PersonalConveyance);
            if (!off)
            {
                if (start is not null && end is not null) blocks.Add(new OffDutyBlock(start.Value, end.Value));
                start = end = null;
                continue;
            }

            if (start is null)
            {
                start = segment.Start;
                end = segment.End;
            }
            else if (segment.Start <= end!.Value.AddMinutes(1))
            {
                if (segment.End > end.Value) end = segment.End;
            }
            else
            {
                blocks.Add(new OffDutyBlock(start.Value, end!.Value));
                start = segment.Start;
                end = segment.End;
            }
        }

        if (start is not null && end is not null) blocks.Add(new OffDutyBlock(start.Value, end.Value));
        return blocks;
    }

    private static int SumMinutes(
        IReadOnlyList<NormalizedSegment> segments,
        DateTimeOffset start,
        DateTimeOffset end,
        Func<DutyKind, bool> predicate)
    {
        if (end <= start) return 0;
        var total = 0;
        foreach (var segment in segments)
        {
            if (!predicate(segment.Kind) || segment.End <= start || segment.Start >= end) continue;
            total += WholeMinutes(Max(segment.Start, start), Min(segment.End, end));
        }
        return total;
    }

    private static bool HasPotentialSplitSleeper(IReadOnlyList<NormalizedSegment> segments, int combinedRequiredMinutes)
    {
        var sleepers = segments
            .Where(s => s.Kind == DutyKind.SleeperBerth && s.Minutes >= 120 && s.Minutes < 8 * 60)
            .Select(s => s.Minutes)
            .OrderByDescending(x => x)
            .Take(2)
            .ToArray();
        return sleepers.Length == 2 && sleepers.Sum() >= combinedRequiredMinutes;
    }

    private static bool HasSaudiSplitBreakCandidate(IReadOnlyList<NormalizedSegment> segments, DateTimeOffset asOf)
    {
        var recent = segments
            .Where(s => s.End <= asOf && s.End >= asOf.AddHours(-8) && IsOffDuty(s.Kind))
            .Select(s => s.Minutes)
            .ToArray();
        return recent.Any(m => m is >= 15 and < 45) && recent.Sum(m => m < 45 ? m : 0) >= 45;
    }

    private static int ContinuousDrivingSinceQualifyingBreak(IReadOnlyList<NormalizedSegment> segments, DateTimeOffset asOf)
    {
        var total = 0;
        foreach (var segment in segments.Where(s => s.End <= asOf).OrderByDescending(s => s.End))
        {
            if (IsOffDuty(segment.Kind) && segment.Minutes >= 45) break;
            if (segment.Kind == DutyKind.Driving) total += segment.Minutes;
            else if (segment.Kind is DutyKind.OnDutyNotDriving or DutyKind.YardMove)
            {
                // A non-driving work interval is not assumed to be the required rest break.
            }
        }
        return total;
    }

    private static int CountSaudiExtensionDays(
        IReadOnlyList<NormalizedSegment> segments,
        DateTimeOffset weekStart,
        DateTimeOffset currentDayStart)
    {
        var count = 0;
        for (var day = weekStart; day < currentDayStart; day = day.AddDays(1))
        {
            var drive = SumMinutes(segments, day, day.AddDays(1), IsDriving);
            if (drive > 9 * 60) count++;
        }
        return count;
    }

    private static int CountConsecutiveWorkDays(
        IReadOnlyList<NormalizedSegment> segments,
        DateTimeOffset currentDayStart,
        DateTimeOffset asOf,
        int maxDays)
    {
        var count = 0;
        for (var i = 0; i < maxDays; i++)
        {
            var dayStart = currentDayStart.AddDays(-i);
            var dayEnd = i == 0 ? asOf : dayStart.AddDays(1);
            var worked = SumMinutes(segments, dayStart, dayEnd, IsOnDuty) > 0;
            if (!worked) break;
            count++;
        }
        return count;
    }

    private static bool IsCycleTwo(string cycleType)
    {
        var cycle = (cycleType ?? string.Empty).Trim().ToLowerInvariant();
        return cycle is "cycle 2" or "cycle2" or "c2" or "120hr/14day" or "120h/14d";
    }

    private static bool IsWarning(int driveRemaining, int? shiftRemaining, int cycleRemaining, int? breakRemaining)
    {
        if (driveRemaining <= 60 || cycleRemaining <= 60) return true;
        if (shiftRemaining is <= 60) return true;
        if (breakRemaining is <= 30) return true;
        return false;
    }

    private static int MinNonNegative(params int[] values) => Math.Max(0, values.Min());

    private static int WholeMinutes(DateTimeOffset start, DateTimeOffset end)
        => Math.Max(0, (int)Math.Floor((end - start).TotalMinutes));

    private static DateTimeOffset Min(DateTimeOffset a, DateTimeOffset b) => a <= b ? a : b;
    private static DateTimeOffset Max(DateTimeOffset a, DateTimeOffset b) => a >= b ? a : b;

    private static void AddViolation(List<string> list, string value)
    {
        if (!list.Contains(value, StringComparer.Ordinal)) list.Add(value);
    }

    private static void AddReview(List<string> list, string value)
    {
        if (!list.Contains(value, StringComparer.Ordinal)) list.Add(value);
    }
}