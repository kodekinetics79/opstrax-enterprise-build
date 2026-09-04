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
///   personal conveyance, ambiguous Saudi split-break records, etc.) fail closed
///   as ReviewRequired rather than silently granting driving time.
/// - The live calculator distinguishes an eligibility block (the driver has no
///   legal time available to begin/continue driving) from a proven overrun. A
///   separate historical audit pass is still required to reconstruct whether the
///   driver actually drove after a non-driving threshold was crossed.
///
/// Rule source/version is intentionally explicit so every persisted shadow clock
/// can be traced back to the regulatory baseline used for its calculation.
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
        IReadOnlyList<string> DrivingBlocks,
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
        var blocks = new List<string>();
        var reviews = new List<string>();
        var normalized = Normalize(request, reviews, out var dataComplete);

        return request.RuleProfile switch
        {
            CanadaSouth60 => EvaluateCanada(request, normalized, northOf60: false, dataComplete, violations, blocks, reviews),
            CanadaNorth60 => EvaluateCanada(request, normalized, northOf60: true, dataComplete, violations, blocks, reviews),
            SaudiTgaGoods => EvaluateSaudi(request, normalized, dataComplete, violations, blocks, reviews),
            _ => throw new ArgumentOutOfRangeException(nameof(request.RuleProfile), request.RuleProfile, "Unsupported HOS rule profile")
        };
    }

    private static Result EvaluateCanada(
        Request request,
        IReadOnlyList<NormalizedSegment> segments,
        bool northOf60,
        bool dataComplete,
        List<string> violations,
        List<string> blocks,
        List<string> reviews)
    {
        var driveLimit = northOf60 ? 15 * 60 : 13 * 60;
        var dutyLimit = northOf60 ? 18 * 60 : 14 * 60;
        var elapsedLimit = northOf60 ? 20 * 60 : 16 * 60;
        var cycleOneLimit = northOf60 ? 80 * 60 : 70 * 60;
        const int cycleTwoLimit = 120 * 60;
        var cycleTwoIntermediateLimit = northOf60 ? 80 * 60 : 70 * 60;
        var asOf = request.AsOf;

        var driveToday = SumMinutes(segments, request.DayStart, asOf, IsDriving);
        var dutyToday = SumMinutes(segments, request.DayStart, asOf, IsOnDuty);
        var offToday = SumMinutes(segments, request.DayStart, asOf, IsOffDuty);
        var currentlyDriving = IsDrivingAtAsOf(segments, asOf);

        if (request.CanadaOffDutyDeferralDeclared)
            AddReview(reviews, "CA_OFF_DUTY_DEFERRAL_REVIEW");

        // Canadian personal-use driving is excluded from on-duty time only when
        // the statutory conditions are satisfied. An upstream label by itself is
        // insufficient evidence, so ambiguous personal conveyance fails closed.
        if (!request.PersonalConveyanceValidated && segments.Any(s => s.Kind == DutyKind.PersonalConveyance))
            AddReview(reviews, "CA_PERSONAL_CONVEYANCE_VALIDATION_REQUIRED");

        // Split sleeper changes elapsed-time treatment. We detect the candidate
        // pattern and require dedicated review instead of granting extra time.
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

        // South-of-60 has both carrier-designated-day limits (s.12) and the
        // post-8-hour-rest limits (s.13). North-of-60 s.39 is rest-window based;
        // do not invent an additional south-style per-day 15/18-hour cap.
        var dailyDriveRemaining = northOf60 ? int.MaxValue : driveLimit - driveToday;
        var dailyDutyRemaining = northOf60 ? int.MaxValue : dutyLimit - dutyToday;
        var shiftDriveRemaining = driveLimit - driveSinceRest;
        var shiftDutyRemaining = dutyLimit - dutySinceRest;
        var elapsedRemaining = elapsedLimit - elapsedSinceRest;

        if (!northOf60 && driveToday > driveLimit)
            AddViolation(violations, "CA_S60_DAILY_DRIVE_LIMIT");
        if (!northOf60 && dutyToday > dutyLimit && currentlyDriving)
            AddViolation(violations, "CA_S60_DAILY_ON_DUTY_LIMIT");
        if (driveSinceRest > driveLimit)
            AddViolation(violations, northOf60 ? "CA_N60_DRIVE_SINCE_8H_REST" : "CA_S60_DRIVE_SINCE_8H_REST");
        if (dutySinceRest > dutyLimit && currentlyDriving)
            AddViolation(violations, northOf60 ? "CA_N60_ON_DUTY_SINCE_8H_REST" : "CA_S60_ON_DUTY_SINCE_8H_REST");
        if (elapsedSinceRest > elapsedLimit && currentlyDriving)
            AddViolation(violations, northOf60 ? "CA_N60_ELAPSED_LIMIT" : "CA_S60_ELAPSED_LIMIT");

        if (!northOf60 && dailyDriveRemaining <= 0) AddBlock(blocks, "CA_S60_DAILY_DRIVE_LIMIT_REACHED");
        if (!northOf60 && dailyDutyRemaining <= 0) AddBlock(blocks, "CA_S60_DAILY_ON_DUTY_LIMIT_REACHED");
        if (shiftDriveRemaining <= 0) AddBlock(blocks, northOf60 ? "CA_N60_DRIVE_LIMIT_REACHED" : "CA_S60_DRIVE_SINCE_8H_REST_REACHED");
        if (shiftDutyRemaining <= 0) AddBlock(blocks, northOf60 ? "CA_N60_ON_DUTY_LIMIT_REACHED" : "CA_S60_ON_DUTY_SINCE_8H_REST_REACHED");
        if (elapsedRemaining <= 0) AddBlock(blocks, northOf60 ? "CA_N60_ELAPSED_LIMIT_REACHED" : "CA_S60_ELAPSED_LIMIT_REACHED");

        // South-of-60 s.14 daily off-duty completion and s.16 deferral require a
        // completed-day / two-day audit. This live calculator exposes accumulated
        // off-duty time but deliberately does not fabricate a completed-day result.

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

        if (cycleRemaining <= 0)
            AddBlock(blocks, cycleTwo ? "CA_CYCLE2_LIMIT_REACHED" : "CA_CYCLE1_LIMIT_REACHED");
        if (cycleUsed > cycleLimit && currentlyDriving)
            AddViolation(violations, cycleTwo ? "CA_CYCLE2_LIMIT" : "CA_CYCLE1_LIMIT");

        // A driver may not drive without 24 consecutive hours off in the
        // preceding 14 days. Missing history is not converted to zero/off-duty.
        var last24 = offBlocks
            .Where(b => b.Minutes >= 24 * 60 && b.End <= asOf && b.End >= asOf.AddDays(-14))
            .OrderByDescending(b => b.End)
            .FirstOrDefault();

        if (last24 is null)
        {
            if (request.CoverageStart <= asOf.AddDays(-14))
            {
                AddBlock(blocks, "CA_24H_OFF_IN_PRECEDING_14_DAYS_REQUIRED");
                if (currentlyDriving) AddViolation(violations, "CA_24H_OFF_IN_PRECEDING_14_DAYS");
            }
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
            if (intermediateRemaining <= 0)
                AddBlock(blocks, northOf60 ? "CA_N60_CYCLE2_80H_WITHOUT_24H_OFF_REACHED" : "CA_S60_CYCLE2_70H_WITHOUT_24H_OFF_REACHED");
            if (dutySince24 > cycleTwoIntermediateLimit && currentlyDriving)
                AddViolation(violations, northOf60 ? "CA_N60_CYCLE2_80H_WITHOUT_24H_OFF" : "CA_S60_CYCLE2_70H_WITHOUT_24H_OFF");
        }

        var driveRemainingInputs = northOf60
            ? new[] { shiftDriveRemaining, shiftDutyRemaining, elapsedRemaining, cycleRemaining }
            : new[] { dailyDriveRemaining, shiftDriveRemaining, dailyDutyRemaining, shiftDutyRemaining, elapsedRemaining, cycleRemaining };
        var driveRemaining = MinNonNegative(driveRemainingInputs);
        var shiftRemaining = Math.Max(0, Math.Min(shiftDutyRemaining, elapsedRemaining));
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
            dataComplete, violations, blocks, reviews, metrics);
    }

    private static Result EvaluateSaudi(
        Request request,
        IReadOnlyList<NormalizedSegment> segments,
        bool dataComplete,
        List<string> violations,
        List<string> blocks,
        List<string> reviews)
    {
        if (request.WeekStart is null)
            throw new ArgumentException("Saudi TGA calculation requires WeekStart for the carrier schedule", nameof(request));
        if (request.WeekStart > request.AsOf || request.WeekStart.Value.AddDays(7) <= request.AsOf)
            throw new ArgumentException("WeekStart must identify the 7-day week containing AsOf", nameof(request));

        var weekStart = request.WeekStart.Value;
        var twoWeekStart = weekStart.AddDays(-7);
        var asOf = request.AsOf;

        if (segments.Any(s => s.Kind == DutyKind.PersonalConveyance))
            AddReview(reviews, "SA_PERSONAL_USE_CLASSIFICATION_REVIEW");

        // Do not infer compliance from ambiguous split break fragments until the
        // exact provider/TGA coding is independently accepted.
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

        var driveToday = SumMinutes(segments, request.DayStart, asOf, IsDriving);
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

        if (driveToday >= todayLimit) AddBlock(blocks, "SA_DAILY_DRIVE_LIMIT_REACHED");
        if (driveThisWeek > 56 * 60)
            AddViolation(violations, "SA_56H_WEEKLY_DRIVE_LIMIT");
        if (driveThisWeek >= 56 * 60) AddBlock(blocks, "SA_56H_WEEKLY_DRIVE_LIMIT_REACHED");
        if (driveTwoWeeks > 90 * 60)
            AddViolation(violations, "SA_90H_TWO_WEEK_DRIVE_LIMIT");
        if (driveTwoWeeks >= 90 * 60) AddBlock(blocks, "SA_90H_TWO_WEEK_DRIVE_LIMIT_REACHED");

        var continuousDrive = ContinuousDrivingSinceQualifyingBreak(segments, asOf);
        var breakRemaining = 270 - continuousDrive;
        if (continuousDrive > 270)
            AddViolation(violations, "SA_45M_BREAK_AFTER_4_5H_DRIVING");
        if (breakRemaining <= 0) AddBlock(blocks, "SA_45M_BREAK_REQUIRED");

        // Weekly-rest / workday control. The carrier's actual week anchor is an
        // explicit request input; it is not guessed from UTC/server locale.
        var hasWeeklyRest = offBlocks.Any(b => b.Minutes >= 48 * 60 && b.End > twoWeekStart && b.End <= asOf);
        var consecutiveWorkDays = CountConsecutiveWorkDays(segments, request.DayStart, asOf, maxDays: 7);
        if (consecutiveWorkDays > 6 && !hasWeeklyRest)
            AddViolation(violations, "SA_MAX_6_CONSECUTIVE_WORK_DAYS");
        if (consecutiveWorkDays >= 6 && !hasWeeklyRest)
            AddBlock(blocks, "SA_WEEKLY_48H_REST_REQUIRED_BEFORE_NEXT_WORKDAY");

        // Evaluate the most recent completed inter-day rest only when the history
        // is contiguous. A later dedicated historical audit will reconstruct every
        // completed day; this live path does not invent missing rest records.
        var lastWorkBeforeToday = segments
            .Where(s => s.End <= request.DayStart && IsOnDuty(s.Kind))
            .OrderByDescending(s => s.End)
            .FirstOrDefault();
        if (lastWorkBeforeToday is not null)
        {
            var maxConsecutiveRest = offBlocks
                .Where(b => b.End <= request.DayStart && b.Start >= lastWorkBeforeToday.End)
                .Select(b => b.Minutes)
                .DefaultIfEmpty(0)
                .Max();
            if (maxConsecutiveRest < 11 * 60)
            {
                AddBlock(blocks, "SA_11H_DAILY_REST_REQUIRED");
                if (IsDrivingAtAsOf(segments, asOf))
                    AddViolation(violations, "SA_11H_DAILY_REST_REQUIREMENT");
            }
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
            dataComplete, violations, blocks, reviews, metrics);
    }

    private static Result FinalizeResult(
        string profile,
        int driveRemaining,
        int? shiftRemaining,
        int cycleRemaining,
        int? breakRemaining,
        bool dataComplete,
        List<string> violations,
        List<string> blocks,
        List<string> reviews,
        IReadOnlyDictionary<string, decimal> metrics)
    {
        var reviewRequired = reviews.Count > 0 || !dataComplete;
        var status = violations.Count > 0
            ? "Violation"
            : reviewRequired
                ? "Unverified"
                : blocks.Count > 0
                    ? "Blocked"
                    : IsWarning(driveRemaining, shiftRemaining, cycleRemaining, breakRemaining)
                        ? "Warning"
                        : "OK";
        var canDrive = dataComplete && !reviewRequired && violations.Count == 0 && blocks.Count == 0 && driveRemaining > 0;

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
            blocks.AsReadOnly(),
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

    private static bool IsDrivingAtAsOf(IReadOnlyList<NormalizedSegment> segments, DateTimeOffset asOf)
        => segments.Any(s => s.Kind == DutyKind.Driving && s.Start < asOf && s.End >= asOf.AddMinutes(-1));

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
            // On-duty-not-driving and yard moves do not automatically satisfy a
            // required rest break; they simply leave continuous driving unchanged.
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

    private static void AddBlock(List<string> list, string value)
    {
        if (!list.Contains(value, StringComparer.Ordinal)) list.Add(value);
    }

    private static void AddReview(List<string> list, string value)
    {
        if (!list.Contains(value, StringComparer.Ordinal)) list.Add(value);
    }
}