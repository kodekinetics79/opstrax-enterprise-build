namespace Opstrax.Telematics.Gateway;

/// <summary>
/// Redaction helpers for device identifiers.
/// </summary>
/// <remarks>
/// An IMEI is a globally unique, permanent hardware identifier that maps to a vehicle and
/// therefore to a person's movements. It is personal data under GDPR/CCPA, and gateway logs
/// are the least-protected, widest-fanned-out surface in the system (they leave the trust
/// boundary for aggregators, get shipped to vendors, and are retained for months). So the
/// gateway <b>never</b> writes a full IMEI to a log — only a masked form that keeps enough
/// digits to correlate a support ticket without being re-identifying on its own.
/// </remarks>
internal static class DeviceIdentifier
{
    /// <summary>Placeholder written when a frame carried no identifier at all.</summary>
    public const string None = "(none)";

    /// <summary>
    /// Masks an identifier for logging: keeps the leading two and trailing two characters and
    /// replaces the rest with <c>*</c> (for example <c>868120303337976</c> becomes
    /// <c>86***********76</c>). Short values are masked entirely.
    /// </summary>
    /// <param name="identifier">The raw identifier, or <see langword="null"/>.</param>
    /// <returns>A log-safe rendering that is never the full identifier.</returns>
    public static string Mask(string? identifier)
    {
        if (string.IsNullOrWhiteSpace(identifier))
            return None;

        // Too short to reveal any prefix/suffix without effectively disclosing the whole value.
        if (identifier.Length <= 6)
            return new string('*', identifier.Length);

        return string.Concat(
            identifier.AsSpan(0, 2),
            new string('*', identifier.Length - 4),
            identifier.AsSpan(identifier.Length - 2, 2));
    }
}
