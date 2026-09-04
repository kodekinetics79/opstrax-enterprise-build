using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Npgsql;
using Opstrax.Api.Data;

namespace Opstrax.Api.Services;

/// <summary>
/// Tenant-scoped HOS shadow-calculation service.
///
/// It intentionally never writes the legacy/customer-facing hos_clocks table.
/// Every successful calculation is an append-only evidence snapshot tied to an
/// explicit policy assignment, algorithm version and deterministic source watermark.
/// Missing/ambiguous configuration or unverified provider provenance fails closed.
/// </summary>
internal sealed class HosShadowClockService(Database db)
{
    internal const string AlgorithmVersion = "hos-shadow-v1.0.0";

    internal sealed record ShadowOutcome(
        long SnapshotId,
        long DriverId,
        string Status,
        bool DataComplete,
        bool ReviewRequired,
        bool CanDrive,
        int DriveRemainingMinutes,
        int? ShiftRemainingMinutes,
        int CycleRemainingMinutes,
        int? BreakRemainingMinutes,
        IReadOnlyList<string> Violations,
        IReadOnlyList<string> DrivingBlocks,
        IReadOnlyList<string> ReviewFlags,
        string? RuleProfileCode,
        string? SourceWatermark,
        bool Persisted);

    internal sealed record AnchorResolution(
        bool Valid,
        DateTimeOffset? DayStart,
        DateTimeOffset? WeekStart,
        DateOnly? LocalDate,
        IReadOnlyList<string> ReviewFlags);

    internal sealed record SourceFingerprint(
        long Id,
        DateTimeOffset Start,
        DateTimeOffset End,
        string Status,
        string? Source,
        string? SourceProvider,
        string? SourceEventId,
        string? SourcePayloadSha256,
        string? SourceSequence,
        DateTimeOffset? SourceReceivedAt,
        bool ProvenanceVerified);

    private sealed record Policy(
        long Id,
        long CompanyId,
        long? BranchId,
        long DriverId,
        string JurisdictionCode,
        string RuleProfileCode,
        string CycleType,
        string Timezone,
        TimeOnly DayStartLocal,
        short? WeekStartIso,
        string ReviewStatus,
        long RowVersion);

    private sealed record LoadedEvent(
        SourceFingerprint Fingerprint,
        HosComplianceCalculator.DutySegment Segment);

    internal async Task<ShadowOutcome> CalculateDriverAsync(
        long companyId,
        long driverId,
        DateTimeOffset asOf,
        CancellationToken ct = default)
    {
        if (companyId <= 0) throw new ArgumentOutOfRangeException(nameof(companyId));
        if (driverId <= 0) throw new ArgumentOutOfRangeException(nameof(driverId));

        // Normalize calculations to a minute boundary. HOS rules are minute-level in
        // this implementation, and this prevents repeated API calls within the same
        // minute from creating meaningless evidence snapshots.
        asOf = FloorToMinute(asOf.ToUniversalTime());

        var policyRows = await db.QueryAsync(
            @"SELECT id,company_id,branch_id,driver_id,jurisdiction_code,rule_profile_code,
                     cycle_type,timezone,day_start_local,week_start_iso,review_status,row_version
                FROM driver_hos_policy_assignments
               WHERE company_id=@c AND driver_id=@d
                 AND effective_from<=@asof
                 AND (effective_to IS NULL OR effective_to>@asof)
               ORDER BY effective_from DESC,id DESC
               LIMIT 2",
            cmd =>
            {
                cmd.Parameters.AddWithValue("@c", companyId);
                cmd.Parameters.AddWithValue("@d", driverId);
                cmd.Parameters.AddWithValue("@asof", asOf.UtcDateTime);
            }, ct);

        if (policyRows.Count == 0)
            return Unpersisted(driverId, "HOS_POLICY_ASSIGNMENT_MISSING");
        if (policyRows.Count > 1)
            return Unpersisted(driverId, "HOS_POLICY_ASSIGNMENT_AMBIGUOUS");

        var policy = ReadPolicy(policyRows[0]);
        if (policy.CompanyId != companyId || policy.DriverId != driverId)
            return Unpersisted(driverId, "HOS_POLICY_TENANT_IDENTITY_MISMATCH");

        var anchors = ResolveAnchors(
            asOf,
            policy.Timezone,
            policy.DayStartLocal,
            policy.JurisdictionCode == "SA" ? policy.WeekStartIso : null);
        if (!anchors.Valid || anchors.DayStart is null || anchors.LocalDate is null)
            return Unpersisted(driverId, anchors.ReviewFlags.ToArray());

        // 15 days gives a full preceding-14-day window even when the local HOS day
        // boundary differs from UTC. Saudi also needs the immediately preceding week.
        var coverageStart = Min(anchors.DayStart.Value.AddDays(-15), anchors.WeekStart?.AddDays(-7));
        var rows = await db.QueryAsync(
            @"SELECT id,start_time,end_time,status,source,source_event_id,
                     source_provider,source_device_identifier,source_received_at,
                     source_payload_sha256,source_sequence,provenance_verified
                FROM hos_logs
               WHERE company_id=@c AND driver_id=@d AND deleted_at IS NULL
                 AND start_time IS NOT NULL
                 AND start_time<@asof
                 AND COALESCE(end_time,@asof)>@coverage
               ORDER BY start_time,id",
            cmd =>
            {
                cmd.Parameters.AddWithValue("@c", companyId);
                cmd.Parameters.AddWithValue("@d", driverId);
                cmd.Parameters.AddWithValue("@asof", asOf.UtcDateTime);
                cmd.Parameters.AddWithValue("@coverage", coverageStart.UtcDateTime);
            }, ct);

        var events = rows.Select(r => ReadEvent(r, asOf)).ToList();
        var sourceFlags = SourceConfidenceFlags(events);

        var localPcDates = policy.JurisdictionCode == "CA"
            ? DistinctPersonalConveyanceDates(events, policy.Timezone)
            : [];
        var exceptionRows = await db.QueryAsync(
            @"SELECT exception_type,local_date,authorized,evidence_reference,approved_at
                FROM hos_exception_authorizations
               WHERE company_id=@c AND driver_id=@d AND jurisdiction_code=@j
                 AND local_date BETWEEN @from_date AND @to_date
                 AND revoked_at IS NULL AND authorized=TRUE
               ORDER BY local_date,exception_type",
            cmd =>
            {
                cmd.Parameters.AddWithValue("@c", companyId);
                cmd.Parameters.AddWithValue("@d", driverId);
                cmd.Parameters.AddWithValue("@j", policy.JurisdictionCode);
                cmd.Parameters.AddWithValue("@from_date", DateOnly.FromDateTime(coverageStart.UtcDateTime.Date));
                cmd.Parameters.AddWithValue("@to_date", anchors.LocalDate.Value);
            }, ct);

        var exceptions = exceptionRows
            .Select(r => new
            {
                Type = Text(r, "exceptionType"),
                Date = DateOnlyValue(r, "localDate"),
                Evidence = Text(r, "evidenceReference"),
                ApprovedAt = NullableTimestamp(r, "approvedAt")
            })
            .Where(x => !string.IsNullOrWhiteSpace(x.Type) && x.Date is not null
                        && !string.IsNullOrWhiteSpace(x.Evidence) && x.ApprovedAt is not null)
            .ToList();

        bool HasCurrent(string type) => exceptions.Any(x =>
            string.Equals(x.Type, type, StringComparison.Ordinal)
            && x.Date == anchors.LocalDate.Value);

        var pcValidated = localPcDates.Count == 0 || localPcDates.All(date => exceptions.Any(x =>
            string.Equals(x.Type, "CA_PERSONAL_CONVEYANCE_VALIDATED", StringComparison.Ordinal)
            && x.Date == date));

        var request = new HosComplianceCalculator.Request(
            policy.RuleProfileCode,
            asOf,
            coverageStart,
            anchors.DayStart.Value,
            policy.CycleType,
            events.Select(e => e.Segment).ToArray(),
            anchors.WeekStart,
            CanadaOffDutyDeferralDeclared: HasCurrent("CA_DAILY_OFF_DUTY_DEFERRAL"),
            PersonalConveyanceValidated: pcValidated,
            SaudiExtensionAuthorizedToday: HasCurrent("SA_DAILY_10H_EXTENSION"),
            SaudiExtensionEvidencePresent: HasCurrent("SA_DAILY_10H_EXTENSION"));

        var result = HosComplianceCalculator.Evaluate(request);
        var reviewFlags = result.ReviewFlags
            .Concat(sourceFlags)
            .Concat(!string.Equals(policy.ReviewStatus, "Approved", StringComparison.Ordinal)
                ? ["HOS_POLICY_NOT_APPROVED"]
                : Array.Empty<string>())
            .Distinct(StringComparer.Ordinal)
            .OrderBy(x => x, StringComparer.Ordinal)
            .ToArray();

        var trustComplete = result.DataComplete
            && sourceFlags.Count == 0
            && string.Equals(policy.ReviewStatus, "Approved", StringComparison.Ordinal);
        var reviewRequired = result.ReviewRequired || reviewFlags.Length > 0 || !trustComplete;
        var status = !trustComplete
            ? "Unverified"
            : result.Status;
        var canDrive = trustComplete && !reviewRequired && result.CanDrive;

        var watermark = ComputeSourceWatermark(
            policy.Id,
            policy.RowVersion,
            policy.RuleProfileCode,
            asOf,
            events.Select(e => e.Fingerprint));
        var maxEventTime = events.Count == 0
            ? (DateTimeOffset?)null
            : events.Max(e => e.Fingerprint.End);

        var snapshotId = await db.InsertAsync(
            @"INSERT INTO hos_shadow_clock_snapshots
                (company_id,branch_id,driver_id,policy_assignment_id,rule_profile_code,
                 algorithm_version,calculated_at,coverage_start,day_start,week_start,
                 drive_remaining_minutes,shift_remaining_minutes,cycle_remaining_minutes,
                 break_remaining_minutes,data_complete,review_required,can_drive,status,
                 violations,driving_blocks,review_flags,metrics,source_event_count,
                 source_max_event_time,source_watermark)
              VALUES
                (@c,@b,@d,@p,@profile,@algorithm,@calculated,@coverage,@day,@week,
                 @drive,@shift,@cycle,@break,@complete,@review,@can_drive,@status,
                 CAST(@violations AS jsonb),CAST(@blocks AS jsonb),CAST(@flags AS jsonb),
                 CAST(@metrics AS jsonb),@event_count,@max_event,@watermark)
              ON CONFLICT (company_id,driver_id,policy_assignment_id,algorithm_version,source_watermark)
              DO NOTHING",
            cmd =>
            {
                cmd.Parameters.AddWithValue("@c", companyId);
                AddNullable(cmd, "@b", policy.BranchId);
                cmd.Parameters.AddWithValue("@d", driverId);
                cmd.Parameters.AddWithValue("@p", policy.Id);
                cmd.Parameters.AddWithValue("@profile", policy.RuleProfileCode);
                cmd.Parameters.AddWithValue("@algorithm", AlgorithmVersion);
                cmd.Parameters.AddWithValue("@calculated", asOf.UtcDateTime);
                cmd.Parameters.AddWithValue("@coverage", coverageStart.UtcDateTime);
                cmd.Parameters.AddWithValue("@day", anchors.DayStart.Value.UtcDateTime);
                AddNullableTimestamp(cmd, "@week", anchors.WeekStart);
                cmd.Parameters.AddWithValue("@drive", result.DriveRemainingMinutes);
                AddNullable(cmd, "@shift", result.ShiftRemainingMinutes);
                cmd.Parameters.AddWithValue("@cycle", result.CycleRemainingMinutes);
                AddNullable(cmd, "@break", result.BreakRemainingMinutes);
                cmd.Parameters.AddWithValue("@complete", trustComplete);
                cmd.Parameters.AddWithValue("@review", reviewRequired);
                cmd.Parameters.AddWithValue("@can_drive", canDrive);
                cmd.Parameters.AddWithValue("@status", status);
                cmd.Parameters.AddWithValue("@violations", JsonSerializer.Serialize(result.Violations));
                cmd.Parameters.AddWithValue("@blocks", JsonSerializer.Serialize(result.DrivingBlocks));
                cmd.Parameters.AddWithValue("@flags", JsonSerializer.Serialize(reviewFlags));
                cmd.Parameters.AddWithValue("@metrics", JsonSerializer.Serialize(result.Metrics));
                cmd.Parameters.AddWithValue("@event_count", events.Count);
                AddNullableTimestamp(cmd, "@max_event", maxEventTime);
                cmd.Parameters.AddWithValue("@watermark", watermark);
            }, ct);

        if (snapshotId == 0)
        {
            snapshotId = await db.ScalarLongAsync(
                @"SELECT id FROM hos_shadow_clock_snapshots
                   WHERE company_id=@c AND driver_id=@d AND policy_assignment_id=@p
                     AND algorithm_version=@algorithm AND source_watermark=@watermark",
                cmd =>
                {
                    cmd.Parameters.AddWithValue("@c", companyId);
                    cmd.Parameters.AddWithValue("@d", driverId);
                    cmd.Parameters.AddWithValue("@p", policy.Id);
                    cmd.Parameters.AddWithValue("@algorithm", AlgorithmVersion);
                    cmd.Parameters.AddWithValue("@watermark", watermark);
                }, ct);
        }

        return new ShadowOutcome(
            snapshotId,
            driverId,
            status,
            trustComplete,
            reviewRequired,
            canDrive,
            result.DriveRemainingMinutes,
            result.ShiftRemainingMinutes,
            result.CycleRemainingMinutes,
            result.BreakRemainingMinutes,
            result.Violations,
            result.DrivingBlocks,
            reviewFlags,
            policy.RuleProfileCode,
            watermark,
            snapshotId > 0);
    }

    internal static AnchorResolution ResolveAnchors(
        DateTimeOffset asOf,
        string timezoneId,
        TimeOnly dayStartLocal,
        short? weekStartIso)
    {
        var flags = new List<string>();
        TimeZoneInfo zone;
        try
        {
            zone = TimeZoneInfo.FindSystemTimeZoneById(timezoneId);
        }
        catch (TimeZoneNotFoundException)
        {
            return InvalidAnchor("HOS_POLICY_TIMEZONE_UNKNOWN");
        }
        catch (InvalidTimeZoneException)
        {
            return InvalidAnchor("HOS_POLICY_TIMEZONE_INVALID");
        }

        var localNow = TimeZoneInfo.ConvertTime(asOf.ToUniversalTime(), zone);
        var localDate = DateOnly.FromDateTime(localNow.DateTime);
        if (TimeOnly.FromDateTime(localNow.DateTime) < dayStartLocal)
            localDate = localDate.AddDays(-1);

        var dayLocal = localDate.ToDateTime(dayStartLocal, DateTimeKind.Unspecified);
        if (!TryLocalToUtc(zone, dayLocal, out var dayStart, out var dayFlag))
            return InvalidAnchor(dayFlag!);

        DateTimeOffset? weekStart = null;
        if (weekStartIso is not null)
        {
            if (weekStartIso is < 1 or > 7)
                return InvalidAnchor("HOS_POLICY_WEEK_START_INVALID");
            var currentIso = IsoDay(localDate.DayOfWeek);
            var back = (currentIso - weekStartIso.Value + 7) % 7;
            var weekDate = localDate.AddDays(-back);
            var weekLocal = weekDate.ToDateTime(dayStartLocal, DateTimeKind.Unspecified);
            if (!TryLocalToUtc(zone, weekLocal, out var resolvedWeek, out var weekFlag))
                return InvalidAnchor(weekFlag!);
            weekStart = resolvedWeek;
        }

        return new AnchorResolution(true, dayStart, weekStart, localDate, flags);
    }

    internal static string ComputeSourceWatermark(
        long policyId,
        long policyRowVersion,
        string ruleProfile,
        DateTimeOffset asOf,
        IEnumerable<SourceFingerprint> events)
    {
        var builder = new StringBuilder();
        builder.Append(AlgorithmVersion).Append('|')
            .Append(policyId).Append('|')
            .Append(policyRowVersion).Append('|')
            .Append(ruleProfile).Append('|')
            .Append(FloorToMinute(asOf.ToUniversalTime()).ToString("O", CultureInfo.InvariantCulture));

        foreach (var item in events
                     .OrderBy(e => e.Start)
                     .ThenBy(e => e.End)
                     .ThenBy(e => e.Id))
        {
            builder.Append('\n')
                .Append(item.Id).Append('|')
                .Append(item.Start.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture)).Append('|')
                .Append(item.End.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture)).Append('|')
                .Append(item.Status).Append('|')
                .Append(item.Source).Append('|')
                .Append(item.SourceProvider).Append('|')
                .Append(item.SourceEventId).Append('|')
                .Append(item.SourcePayloadSha256).Append('|')
                .Append(item.SourceSequence).Append('|')
                .Append(item.SourceReceivedAt?.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture)).Append('|')
                .Append(item.ProvenanceVerified ? '1' : '0');
        }

        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(builder.ToString()))).ToLowerInvariant();
    }

    private static Policy ReadPolicy(IReadOnlyDictionary<string, object?> row)
        => new(
            Long(row, "id"),
            Long(row, "companyId"),
            NullableLong(row, "branchId"),
            Long(row, "driverId"),
            Text(row, "jurisdictionCode") ?? string.Empty,
            Text(row, "ruleProfileCode") ?? string.Empty,
            Text(row, "cycleType") ?? string.Empty,
            Text(row, "timezone") ?? string.Empty,
            TimeOnlyValue(row, "dayStartLocal"),
            NullableShort(row, "weekStartIso"),
            Text(row, "reviewStatus") ?? string.Empty,
            Long(row, "rowVersion"));

    private static LoadedEvent ReadEvent(IReadOnlyDictionary<string, object?> row, DateTimeOffset asOf)
    {
        var start = Timestamp(row, "startTime");
        var end = NullableTimestamp(row, "endTime") ?? asOf;
        var status = Text(row, "status") ?? string.Empty;
        var fingerprint = new SourceFingerprint(
            Long(row, "id"),
            start,
            end,
            status,
            Text(row, "source"),
            Text(row, "sourceProvider"),
            Text(row, "sourceEventId"),
            Text(row, "sourcePayloadSha256"),
            Text(row, "sourceSequence"),
            NullableTimestamp(row, "sourceReceivedAt"),
            Bool(row, "provenanceVerified"));
        return new LoadedEvent(
            fingerprint,
            new HosComplianceCalculator.DutySegment(start, end, status, fingerprint.SourceEventId));
    }

    private static List<string> SourceConfidenceFlags(IReadOnlyList<LoadedEvent> events)
    {
        var flags = new List<string>();
        if (events.Count == 0)
        {
            flags.Add("HOS_SOURCE_EVENTS_MISSING");
            return flags;
        }

        if (events.Any(e => !e.Fingerprint.ProvenanceVerified))
            flags.Add("HOS_SOURCE_PROVENANCE_UNVERIFIED");
        if (events.Any(e => string.IsNullOrWhiteSpace(e.Fingerprint.SourceProvider)))
            flags.Add("HOS_SOURCE_PROVIDER_MISSING");
        if (events.Any(e => string.IsNullOrWhiteSpace(e.Fingerprint.SourceEventId)))
            flags.Add("HOS_SOURCE_EVENT_ID_MISSING");
        if (events.Any(e => e.Fingerprint.SourceReceivedAt is null))
            flags.Add("HOS_SOURCE_RECEIVED_AT_MISSING");
        if (events.Any(e => string.IsNullOrWhiteSpace(e.Fingerprint.SourcePayloadSha256)))
            flags.Add("HOS_SOURCE_PAYLOAD_HASH_MISSING");
        return flags;
    }

    private static HashSet<DateOnly> DistinctPersonalConveyanceDates(
        IReadOnlyList<LoadedEvent> events,
        string timezoneId)
    {
        var zone = TimeZoneInfo.FindSystemTimeZoneById(timezoneId);
        return events
            .Where(e => string.Equals(e.Segment.Status, "personal conveyance", StringComparison.OrdinalIgnoreCase)
                        || string.Equals(e.Segment.Status, "personal use", StringComparison.OrdinalIgnoreCase)
                        || string.Equals(e.Segment.Status, "personal_conveyance", StringComparison.OrdinalIgnoreCase))
            .Select(e => DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(e.Segment.Start, zone).DateTime))
            .ToHashSet();
    }

    private static ShadowOutcome Unpersisted(long driverId, params string[] flags)
        => new(
            0,
            driverId,
            "Unverified",
            false,
            true,
            false,
            0,
            null,
            0,
            null,
            Array.Empty<string>(),
            Array.Empty<string>(),
            flags.Distinct(StringComparer.Ordinal).ToArray(),
            null,
            null,
            false);

    private static AnchorResolution InvalidAnchor(string flag)
        => new(false, null, null, null, [flag]);

    private static bool TryLocalToUtc(
        TimeZoneInfo zone,
        DateTime local,
        out DateTimeOffset utc,
        out string? flag)
    {
        if (zone.IsInvalidTime(local))
        {
            utc = default;
            flag = "HOS_POLICY_DAY_ANCHOR_DST_INVALID";
            return false;
        }
        if (zone.IsAmbiguousTime(local))
        {
            // Choosing either repeated local-time offset would be a regulatory
            // assumption. Require an explicit non-ambiguous carrier anchor instead.
            utc = default;
            flag = "HOS_POLICY_DAY_ANCHOR_DST_AMBIGUOUS";
            return false;
        }

        utc = new DateTimeOffset(TimeZoneInfo.ConvertTimeToUtc(local, zone), TimeSpan.Zero);
        flag = null;
        return true;
    }

    private static DateTimeOffset FloorToMinute(DateTimeOffset value)
        => new(value.Year, value.Month, value.Day, value.Hour, value.Minute, 0, value.Offset);

    private static int IsoDay(DayOfWeek day)
        => day == DayOfWeek.Sunday ? 7 : (int)day;

    private static DateTimeOffset Min(DateTimeOffset a, DateTimeOffset? b)
        => b is null || a <= b.Value ? a : b.Value;

    private static string? Text(IReadOnlyDictionary<string, object?> row, string key)
        => row.TryGetValue(key, out var value) && value is not null and not DBNull
            ? Convert.ToString(value, CultureInfo.InvariantCulture)
            : null;

    private static long Long(IReadOnlyDictionary<string, object?> row, string key)
        => Convert.ToInt64(row[key], CultureInfo.InvariantCulture);

    private static long? NullableLong(IReadOnlyDictionary<string, object?> row, string key)
        => row.TryGetValue(key, out var value) && value is not null and not DBNull
            ? Convert.ToInt64(value, CultureInfo.InvariantCulture)
            : null;

    private static short? NullableShort(IReadOnlyDictionary<string, object?> row, string key)
        => row.TryGetValue(key, out var value) && value is not null and not DBNull
            ? Convert.ToInt16(value, CultureInfo.InvariantCulture)
            : null;

    private static bool Bool(IReadOnlyDictionary<string, object?> row, string key)
        => row.TryGetValue(key, out var value) && value is not null and not DBNull && Convert.ToBoolean(value, CultureInfo.InvariantCulture);

    private static DateTimeOffset Timestamp(IReadOnlyDictionary<string, object?> row, string key)
        => NullableTimestamp(row, key) ?? throw new InvalidOperationException($"Required timestamp '{key}' missing");

    private static DateTimeOffset? NullableTimestamp(IReadOnlyDictionary<string, object?> row, string key)
    {
        if (!row.TryGetValue(key, out var value) || value is null or DBNull) return null;
        return value switch
        {
            DateTimeOffset dto => dto.ToUniversalTime(),
            DateTime dt => new DateTimeOffset(DateTime.SpecifyKind(dt, DateTimeKind.Utc)),
            _ => DateTimeOffset.Parse(Convert.ToString(value, CultureInfo.InvariantCulture)!, CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal)
        };
    }

    private static DateOnly? DateOnlyValue(IReadOnlyDictionary<string, object?> row, string key)
    {
        if (!row.TryGetValue(key, out var value) || value is null or DBNull) return null;
        return value switch
        {
            DateOnly date => date,
            DateTime dt => DateOnly.FromDateTime(dt),
            _ => DateOnly.Parse(Convert.ToString(value, CultureInfo.InvariantCulture)!, CultureInfo.InvariantCulture)
        };
    }

    private static TimeOnly TimeOnlyValue(IReadOnlyDictionary<string, object?> row, string key)
    {
        var value = row[key] ?? throw new InvalidOperationException($"Required time '{key}' missing");
        return value switch
        {
            TimeOnly time => time,
            TimeSpan span => TimeOnly.FromTimeSpan(span),
            _ => TimeOnly.Parse(Convert.ToString(value, CultureInfo.InvariantCulture)!, CultureInfo.InvariantCulture)
        };
    }

    private static void AddNullable(NpgsqlCommand command, string name, long? value)
        => command.Parameters.AddWithValue(name, value is null ? DBNull.Value : value.Value);

    private static void AddNullable(NpgsqlCommand command, string name, int? value)
        => command.Parameters.AddWithValue(name, value is null ? DBNull.Value : value.Value);

    private static void AddNullableTimestamp(NpgsqlCommand command, string name, DateTimeOffset? value)
        => command.Parameters.AddWithValue(name, value is null ? DBNull.Value : value.Value.UtcDateTime);
}