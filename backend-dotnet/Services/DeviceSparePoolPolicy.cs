using System.Globalization;

namespace Opstrax.Api.Services;

public sealed record DeviceSparePoolActionRequest(
    string? ActionType,
    string? PoolName,
    string? RmaCaseId,
    string? ActionReason,
    string? SourceReference,
    string? EffectiveAt,
    string? IdempotencyKey);

public sealed record ValidatedDeviceSparePoolAction(
    string ActionType,
    string? PoolName,
    long? RmaCaseId,
    string ActionReason,
    string SourceReference,
    DateTimeOffset EffectiveAt,
    Guid IdempotencyKey);

/// <summary>
/// Validates exact-device spare-pool planning facts. These records do not prove
/// physical possession, condition, compatibility, installation, or certification.
/// </summary>
public static class DeviceSparePoolPolicy
{
    private static readonly HashSet<string> ActionTypes = ["Add", "Reserve", "Release", "Remove"];

    public static (ValidatedDeviceSparePoolAction? Value, string? Error) Validate(
        DeviceSparePoolActionRequest? request, DateTimeOffset now)
    {
        if (request is null) return (null, "A spare-pool action is required.");
        var actionType = Clean(request.ActionType);
        if (actionType is null || !ActionTypes.Contains(actionType))
            return (null, "actionType must be Add, Reserve, Release, or Remove.");
        var poolName = Clean(request.PoolName);
        if (actionType == "Add" && (poolName is null || poolName.Length is < 3 or > 120))
            return (null, "poolName must contain 3-120 characters when adding a device.");
        if (actionType != "Add" && poolName is not null)
            return (null, "poolName must be omitted after the device enters a pool.");
        var caseText = Clean(request.RmaCaseId);
        long? caseId = null;
        if (caseText is not null && (!long.TryParse(caseText, NumberStyles.None, CultureInfo.InvariantCulture, out var parsed) ||
                                     parsed <= 0 || parsed.ToString(CultureInfo.InvariantCulture) != caseText))
            return (null, "rmaCaseId must be a canonical positive integer string.");
        else if (caseText is not null)
            caseId = long.Parse(caseText, CultureInfo.InvariantCulture);
        if (actionType == "Reserve" && caseId is null)
            return (null, "rmaCaseId is required when reserving a spare device.");
        if (actionType != "Reserve" && caseId is not null)
            return (null, "rmaCaseId must be omitted unless reserving a spare device.");
        var reason = Clean(request.ActionReason);
        if (reason is null || reason.Length is < 5 or > 500)
            return (null, "actionReason must contain 5-500 characters.");
        var source = Clean(request.SourceReference);
        if (source is null || source.Length is < 3 or > 240)
            return (null, "sourceReference must contain 3-240 characters.");
        var effectiveAt = Timestamp(request.EffectiveAt);
        var utcNow = now.ToUniversalTime();
        if (effectiveAt is null || effectiveAt < utcNow.AddDays(-365) || effectiveAt > utcNow.AddMinutes(5))
            return (null, "effectiveAt must be an explicit-offset timestamp from the last 365 days and no more than five minutes in the future.");
        if (!Guid.TryParseExact(Clean(request.IdempotencyKey), "D", out var idempotencyKey))
            return (null, "idempotencyKey must be a canonical UUID.");
        return (new(actionType, poolName, caseId, reason, source, effectiveAt.Value, idempotencyKey), null);
    }

    private static DateTimeOffset? Timestamp(string? value)
    {
        var cleaned = Clean(value);
        if (cleaned is null || !(cleaned.EndsWith('Z') ||
            (cleaned.Length >= 6 && (cleaned[^6] is '+' or '-') && cleaned[^3] == ':')))
            return null;
        return DateTimeOffset.TryParse(cleaned, CultureInfo.InvariantCulture,
            DateTimeStyles.AllowWhiteSpaces | DateTimeStyles.AssumeUniversal, out var parsed)
            ? parsed.ToUniversalTime() : null;
    }

    private static string? Clean(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
