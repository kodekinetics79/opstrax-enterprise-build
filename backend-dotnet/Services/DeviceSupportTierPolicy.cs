using System.Globalization;

namespace Opstrax.Api.Services;

public sealed record DeviceSupportTierActionRequest(
    string? ActionType,
    string? TierCode,
    string? CoverageWindow,
    int? RoutingResponseTargetMinutes,
    string? EscalationPolicyReference,
    string? CommercialReference,
    string? ActionReason,
    string? SourceReference,
    string? EffectiveAt,
    string? IdempotencyKey);

public sealed record ValidatedDeviceSupportTierAction(
    string ActionType,
    string? TierCode,
    string? CoverageWindow,
    int? RoutingResponseTargetMinutes,
    string? EscalationPolicyReference,
    string? CommercialReference,
    string ActionReason,
    string SourceReference,
    DateTimeOffset EffectiveAt,
    Guid IdempotencyKey);

/// <summary>
/// Validates operator-recorded device support routing. It does not validate a
/// commercial entitlement, provider supportability, hardware, or certification.
/// </summary>
public static class DeviceSupportTierPolicy
{
    private static readonly HashSet<string> ActionTypes = ["Assign", "Change", "End"];
    private static readonly HashSet<string> TierCodes = ["Standard", "Priority", "CriticalOps", "Custom"];
    private static readonly HashSet<string> CoverageWindows = ["BusinessHours", "ExtendedHours", "AlwaysOn", "Custom"];

    public static (ValidatedDeviceSupportTierAction? Value, string? Error) Validate(
        DeviceSupportTierActionRequest? request, DateTimeOffset now)
    {
        if (request is null) return (null, "A support-tier action is required.");
        var action = Clean(request.ActionType);
        if (action is null || !ActionTypes.Contains(action))
            return (null, "actionType must be Assign, Change, or End.");

        var tier = Clean(request.TierCode);
        var coverage = Clean(request.CoverageWindow);
        var escalation = Clean(request.EscalationPolicyReference);
        var commercial = Clean(request.CommercialReference);
        if (action is "Assign" or "Change")
        {
            if (tier is null || !TierCodes.Contains(tier))
                return (null, "tierCode must be Standard, Priority, CriticalOps, or Custom.");
            if (coverage is null || !CoverageWindows.Contains(coverage))
                return (null, "coverageWindow must be BusinessHours, ExtendedHours, AlwaysOn, or Custom.");
            if (request.RoutingResponseTargetMinutes is null or < 15 or > 10080)
                return (null, "routingResponseTargetMinutes must be from 15 to 10080.");
            if (escalation is null || escalation.Length is < 3 or > 240)
                return (null, "escalationPolicyReference must contain 3-240 characters.");
            if (commercial is null || commercial.Length is < 3 or > 240)
                return (null, "commercialReference must contain 3-240 characters.");
        }
        else if (tier is not null || coverage is not null || request.RoutingResponseTargetMinutes is not null ||
                 escalation is not null || commercial is not null)
        {
            return (null, "Tier-plan fields must be omitted when ending support coverage.");
        }

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
        return (new(action, tier, coverage, request.RoutingResponseTargetMinutes, escalation, commercial,
            reason, source, effectiveAt.Value, idempotencyKey), null);
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
