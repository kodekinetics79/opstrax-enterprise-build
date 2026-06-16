namespace Zayra.Api.Infrastructure.Payroll;

/// <summary>
/// ISO 13616 IBAN validator (mod-97 check).  Used to gate WPS/SIF bank-file
/// export so that no payment record with an invalid or missing IBAN is exported.
/// </summary>
public static class IbanValidator
{
    public static bool IsValid(string? iban)
    {
        if (string.IsNullOrWhiteSpace(iban)) return false;
        iban = iban.Replace(" ", "").ToUpperInvariant();
        if (iban.Length is < 15 or > 34) return false;
        if (!iban.All(c => char.IsLetterOrDigit(c))) return false;

        // Move the first 4 chars to the end, map letters → numbers, then mod-97 must equal 1.
        var rearranged = iban[4..] + iban[..4];
        var numeric = string.Concat(rearranged.Select(c => char.IsLetter(c) ? (c - 'A' + 10).ToString() : c.ToString()));

        var remainder = 0;
        foreach (var ch in numeric)
            remainder = (remainder * 10 + (ch - '0')) % 97;

        return remainder == 1;
    }

    /// <summary>True only for a structurally valid IBAN that begins with the Saudi country code "SA".</summary>
    public static bool IsSaudiIban(string? iban)
        => IsValid(iban) && iban!.Replace(" ", "").ToUpperInvariant().StartsWith("SA");
}
