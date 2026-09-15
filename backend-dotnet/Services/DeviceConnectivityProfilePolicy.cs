using System.Globalization;
using System.Text.RegularExpressions;

namespace Opstrax.Api.Services;

public sealed record DeviceConnectivityProfileRequest(
    string? ProfileKind,
    string? CarrierName,
    string? Iccid,
    string? Msisdn,
    string? Apn,
    string? EffectiveAt,
    string? ChangeReason,
    string? SourceReference,
    string? IdempotencyKey);

public sealed record ValidatedDeviceConnectivityProfile(
    string ProfileKind,
    string CarrierName,
    string Iccid,
    string? Msisdn,
    string? Apn,
    DateTimeOffset EffectiveAt,
    string ChangeReason,
    string SourceReference,
    Guid IdempotencyKey);

/// <summary>
/// Validates operator-recorded SIM/eSIM inventory. This policy deliberately has no
/// connectivity or certification outcome: an assigned profile is not proof that a
/// modem attached to a network or that a device sent telemetry.
/// </summary>
public static partial class DeviceConnectivityProfilePolicy
{
    public static (ValidatedDeviceConnectivityProfile? Value, string? Error) Validate(
        DeviceConnectivityProfileRequest? request,
        DateTimeOffset now)
    {
        if (request is null) return (null, "A connectivity profile is required.");

        var profileKind = Clean(request.ProfileKind) switch
        {
            "PhysicalSIM" => "PhysicalSIM",
            "eSIM" => "eSIM",
            _ => null,
        };
        if (profileKind is null) return (null, "profileKind must be PhysicalSIM or eSIM.");

        var carrier = Clean(request.CarrierName);
        if (carrier is null || carrier.Length is < 2 or > 120)
            return (null, "carrierName must contain 2-120 characters.");

        var iccid = Clean(request.Iccid);
        if (iccid is null || !IccidPattern().IsMatch(iccid))
            return (null, "iccid must contain 18-22 digits.");

        var msisdn = Clean(request.Msisdn);
        if (msisdn is not null && !MsisdnPattern().IsMatch(msisdn))
            return (null, "msisdn must use E.164 format, for example +14165550123.");

        var apn = Clean(request.Apn);
        if (apn is not null && (apn.Length > 253 || !ApnPattern().IsMatch(apn)))
            return (null, "apn must contain 1-253 letters, digits, periods, underscores, or hyphens.");

        var effectiveText = Clean(request.EffectiveAt);
        if (effectiveText is null || !OffsetPattern().IsMatch(effectiveText) ||
            !DateTimeOffset.TryParse(effectiveText, CultureInfo.InvariantCulture,
                DateTimeStyles.AllowWhiteSpaces | DateTimeStyles.AssumeUniversal, out var effectiveAt))
            return (null, "effectiveAt must be an ISO-8601 timestamp with an explicit UTC offset.");
        effectiveAt = effectiveAt.ToUniversalTime();
        if (effectiveAt > now.ToUniversalTime().AddMinutes(5))
            return (null, "effectiveAt cannot be more than five minutes in the future.");

        var reason = Clean(request.ChangeReason);
        if (reason is null || reason.Length is < 5 or > 500)
            return (null, "changeReason must contain 5-500 characters.");

        var source = Clean(request.SourceReference);
        if (source is null || source.Length is < 3 or > 240)
            return (null, "sourceReference must contain 3-240 characters.");

        if (!Guid.TryParseExact(Clean(request.IdempotencyKey), "D", out var idempotencyKey))
            return (null, "idempotencyKey must be a canonical UUID.");

        return (new(
            profileKind,
            carrier,
            iccid,
            msisdn,
            apn,
            effectiveAt,
            reason,
            source,
            idempotencyKey), null);
    }

    public static string LastFour(string value) => value[^Math.Min(4, value.Length)..];

    private static string? Clean(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    [GeneratedRegex("^[0-9]{18,22}$", RegexOptions.CultureInvariant)]
    private static partial Regex IccidPattern();

    [GeneratedRegex("^\\+[1-9][0-9]{7,14}$", RegexOptions.CultureInvariant)]
    private static partial Regex MsisdnPattern();

    [GeneratedRegex("^[A-Za-z0-9][A-Za-z0-9._-]{0,252}$", RegexOptions.CultureInvariant)]
    private static partial Regex ApnPattern();

    [GeneratedRegex("(?:Z|[+-][0-9]{2}:[0-9]{2})$", RegexOptions.CultureInvariant | RegexOptions.IgnoreCase)]
    private static partial Regex OffsetPattern();
}
