using System.Globalization;
using System.Text.RegularExpressions;

namespace Opstrax.Api.Services;

public sealed record DeviceRetirementRequest(
    string? RetirementReason,
    string? DispositionPlan,
    string? SourceReference,
    string? EffectiveAt,
    long? ExpectedRowVersion,
    string? IdempotencyKey,
    string? SafetyConfirmation);

public sealed record ValidatedDeviceRetirement(
    string RetirementReason,
    string DispositionPlan,
    string SourceReference,
    DateTimeOffset EffectiveAt,
    long ExpectedRowVersion,
    Guid IdempotencyKey,
    string SafetyConfirmation);

/// <summary>
/// Validates the operator's software retirement instruction. A disposition plan
/// is intent only: it never proves return, recycling, storage, destruction, or
/// any physical or certification outcome.
/// </summary>
public static partial class DeviceRetirementPolicy
{
    public static (ValidatedDeviceRetirement? Value, string? Error) Validate(
        DeviceRetirementRequest? request,
        DateTimeOffset now)
    {
        if (request is null) return (null, "A device retirement request is required.");

        var reason = Clean(request.RetirementReason);
        if (reason is null || reason.Length is < 5 or > 500)
            return (null, "retirementReason must contain 5-500 characters.");

        var disposition = Clean(request.DispositionPlan) switch
        {
            "ReturnToVendor" => "ReturnToVendor",
            "Recycle" => "Recycle",
            "SecureStorage" => "SecureStorage",
            "Other" => "Other",
            _ => null,
        };
        if (disposition is null)
            return (null, "dispositionPlan must be ReturnToVendor, Recycle, SecureStorage, or Other.");

        var source = Clean(request.SourceReference);
        if (source is null || source.Length is < 3 or > 240)
            return (null, "sourceReference must contain 3-240 characters.");

        var effectiveText = Clean(request.EffectiveAt);
        if (effectiveText is null || !OffsetPattern().IsMatch(effectiveText) ||
            !DateTimeOffset.TryParse(effectiveText, CultureInfo.InvariantCulture,
                DateTimeStyles.AllowWhiteSpaces | DateTimeStyles.AssumeUniversal, out var effectiveAt))
            return (null, "effectiveAt must be an ISO-8601 timestamp with an explicit UTC offset.");
        effectiveAt = effectiveAt.ToUniversalTime();
        var utcNow = now.ToUniversalTime();
        if (effectiveAt < utcNow.AddMinutes(-5) || effectiveAt > utcNow.AddMinutes(5))
            return (null, "effectiveAt must be within five minutes of the current time.");

        if (request.ExpectedRowVersion is null or < 1)
            return (null, "expectedRowVersion must identify the current device revision.");

        if (!Guid.TryParseExact(Clean(request.IdempotencyKey), "D", out var idempotencyKey))
            return (null, "idempotencyKey must be a canonical UUID.");

        var confirmation = Clean(request.SafetyConfirmation);
        if (confirmation is null || confirmation.Length > 240)
            return (null, "safetyConfirmation is required.");

        return (new(reason, disposition, source, effectiveAt,
            request.ExpectedRowVersion.Value, idempotencyKey, confirmation), null);
    }

    public static string ConfirmationFor(string serial) => $"RETIRE {serial}";

    private static string? Clean(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    [GeneratedRegex("(?:Z|[+-][0-9]{2}:[0-9]{2})$", RegexOptions.CultureInvariant | RegexOptions.IgnoreCase)]
    private static partial Regex OffsetPattern();
}
