namespace Opstrax.Api.Services;

/// <summary>
/// Pure validation policy for a vehicle's regulated identity. VIN validation follows the
/// 17-character ISO 3779/NHTSA alphabet and check-digit calculation. Vehicles that genuinely
/// do not have a VIN must use one of the explicitly governed alternate identity kinds below;
/// an alternate identity is never a way to persist a malformed VIN.
/// </summary>
public static class VehicleIdentityPolicy
{
    public const string ManufacturerSerialNumber = "manufacturer-serial-number";
    public const string GovernmentRegistrationNumber = "government-registration-number";
    public const string LegacyFleetIdentifier = "legacy-fleet-identifier";

    private static readonly HashSet<string> AlternateIdentityKinds = new(StringComparer.Ordinal)
    {
        ManufacturerSerialNumber,
        GovernmentRegistrationNumber,
        LegacyFleetIdentifier,
    };

    private static readonly HashSet<string> AlternateIdentityPlaceholders = new(StringComparer.Ordinal)
    {
        "UNKNOWN", "NONE", "N/A", "NA", "TBD", "NOT-APPLICABLE", "NO-VIN", "NOVIN",
    };

    private static readonly int[] VinWeights = [8, 7, 6, 5, 4, 3, 2, 10, 0, 9, 8, 7, 6, 5, 4, 3, 2];

    public static string? NormalizeVin(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim().ToUpperInvariant();

    public static VehicleIdentityValidationResult ValidateVin(string? value)
    {
        var normalized = NormalizeVin(value);
        if (normalized is null)
            return VehicleIdentityValidationResult.Invalid("vin_required", "VIN is required.");
        if (normalized.Length != 17)
            return VehicleIdentityValidationResult.Invalid("vin_length", "VIN must contain exactly 17 characters.");
        if (normalized.Any(character => VinTransliteration(character) < 0))
            return VehicleIdentityValidationResult.Invalid(
                "vin_characters", "VIN may contain only ASCII letters and digits, excluding I, O, and Q.");

        var expectedCheckDigit = CalculateCheckDigit(normalized);
        if (normalized[8] != expectedCheckDigit)
            return VehicleIdentityValidationResult.Invalid(
                "vin_check_digit", $"VIN check digit is invalid; expected '{expectedCheckDigit}'.");

        return VehicleIdentityValidationResult.Valid(normalized);
    }

    public static VehicleIdentityValidationResult ValidateAlternateIdentity(string? kind, string? value)
    {
        var normalizedKind = NormalizeAlternateIdentityKind(kind);
        if (normalizedKind is null)
            return VehicleIdentityValidationResult.Invalid(
                "alternate_kind_required", "An alternate vehicle identity kind is required.");
        if (!AlternateIdentityKinds.Contains(normalizedKind))
            return VehicleIdentityValidationResult.Invalid(
                "alternate_kind_unsupported", "The alternate vehicle identity kind is not supported.");

        var normalized = string.IsNullOrWhiteSpace(value) ? null : value.Trim().ToUpperInvariant();
        if (normalized is null)
            return VehicleIdentityValidationResult.Invalid(
                "alternate_value_required", "An alternate vehicle identity value is required.");
        if (AlternateIdentityPlaceholders.Contains(normalized))
            return VehicleIdentityValidationResult.Invalid(
                "alternate_value_placeholder", "Placeholder values are not valid vehicle identities.");
        if (normalized.Length is < 4 or > 64)
            return VehicleIdentityValidationResult.Invalid(
                "alternate_value_length", "Alternate vehicle identity must contain 4 to 64 characters.");
        if (normalized.Any(character => !IsAlternateIdentityCharacter(character)))
            return VehicleIdentityValidationResult.Invalid(
                "alternate_value_characters", "Alternate vehicle identity contains unsupported characters.");

        // A 17-character alphanumeric token is VIN-shaped, including tokens containing I/O/Q.
        // Reject it from this explicitly non-VIN channel whether its VIN check digit is valid or
        // invalid; callers must use the VIN field and VIN policy instead.
        if (normalized.Length == 17 && normalized.All(IsAsciiLetterOrDigit))
            return VehicleIdentityValidationResult.Invalid(
                "alternate_vin_bypass", "A VIN-shaped value must be submitted through VIN validation.");

        return VehicleIdentityValidationResult.Valid(normalized, normalizedKind);
    }

    public static string? NormalizeAlternateIdentityKind(string? kind)
    {
        if (string.IsNullOrWhiteSpace(kind)) return null;
        return kind.Trim().ToLowerInvariant().Replace('_', '-').Replace(' ', '-');
    }

    private static char CalculateCheckDigit(string normalizedVin)
    {
        var sum = 0;
        for (var index = 0; index < normalizedVin.Length; index++)
            sum += VinTransliteration(normalizedVin[index]) * VinWeights[index];
        var remainder = sum % 11;
        return remainder == 10 ? 'X' : (char)('0' + remainder);
    }

    private static int VinTransliteration(char character) => character switch
    {
        >= '0' and <= '9' => character - '0',
        'A' or 'J' => 1,
        'B' or 'K' or 'S' => 2,
        'C' or 'L' or 'T' => 3,
        'D' or 'M' or 'U' => 4,
        'E' or 'N' or 'V' => 5,
        'F' or 'W' => 6,
        'G' or 'P' or 'X' => 7,
        'H' or 'Y' => 8,
        'R' or 'Z' => 9,
        _ => -1,
    };

    private static bool IsAlternateIdentityCharacter(char character) =>
        character is >= 'A' and <= 'Z' or >= '0' and <= '9' or '-' or '_' or '.' or '/';

    private static bool IsAsciiLetterOrDigit(char character) =>
        character is >= 'A' and <= 'Z' or >= '0' and <= '9';
}

public sealed record VehicleIdentityValidationResult(
    bool IsValid,
    string? NormalizedValue,
    string? ErrorCode,
    string? ErrorMessage,
    string? AlternateIdentityKind = null)
{
    internal static VehicleIdentityValidationResult Valid(string normalizedValue, string? alternateIdentityKind = null) =>
        new(true, normalizedValue, null, null, alternateIdentityKind);

    internal static VehicleIdentityValidationResult Invalid(string errorCode, string errorMessage) =>
        new(false, null, errorCode, errorMessage);
}
