using System.Globalization;

namespace Opstrax.Api.Services;

public sealed record DeviceRmaSupportActionRequest(
    string? ActionType,
    string? SupportQueue,
    string? EscalationSeverity,
    string? ActionReason,
    string? SourceReference,
    string? EffectiveAt,
    string? IdempotencyKey);

public sealed record ValidatedDeviceRmaSupportAction(
    string ActionType,
    string SupportQueue,
    string? EscalationSeverity,
    string ActionReason,
    string SourceReference,
    DateTimeOffset EffectiveAt,
    Guid IdempotencyKey);

/// <summary>
/// Validates operator-recorded RMA routing. Ownership and escalation records
/// do not prove a support response, physical work, or warranty acceptance.
/// </summary>
public static class DeviceRmaSupportPolicy
{
    private static readonly HashSet<string> ActionTypes = ["TakeOwnership", "Escalate"];
    private static readonly HashSet<string> Severities = ["P0", "P1", "P2", "P3"];

    public static (ValidatedDeviceRmaSupportAction? Value, string? Error) Validate(
        DeviceRmaSupportActionRequest? request, DateTimeOffset now)
    {
        if (request is null) return (null, "An RMA support action is required.");
        var actionType = Clean(request.ActionType);
        if (actionType is null || !ActionTypes.Contains(actionType))
            return (null, "actionType must be TakeOwnership or Escalate.");
        var queue = Clean(request.SupportQueue);
        if (queue is null || queue.Length is < 3 or > 120)
            return (null, "supportQueue must contain 3-120 characters.");
        var severity = Clean(request.EscalationSeverity);
        if (actionType == "Escalate" && (severity is null || !Severities.Contains(severity)))
            return (null, "escalationSeverity must be P0, P1, P2, or P3 for escalation.");
        if (actionType == "TakeOwnership" && severity is not null)
            return (null, "escalationSeverity must be omitted when taking ownership.");
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
        return (new(actionType, queue, severity, reason, source, effectiveAt.Value, idempotencyKey), null);
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

    private static string? Clean(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
