using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Opstrax.Api.Security;

namespace Opstrax.Api.Services;

/// <summary>
/// Central fail-closed policy for device HMAC material. New writes are always AES-GCM envelopes
/// produced by <see cref="PiiProtectionService"/>. Plaintext is never queried or read in
/// production; a deliberately enabled, non-production-only compatibility switch exists solely
/// to let operators rotate old development devices onto encrypted credentials.
/// </summary>
public static class DeviceHmacSecretProtection
{
    public const string LegacyReadSetting = "Telemetry:AllowLegacyDeviceSecrets";
    public const string RotationGraceMinutesSetting = "Telemetry:DeviceSecretRotationGraceMinutes";
    public const int DefaultRotationGraceMinutes = 10;
    public const int MaximumRotationGraceMinutes = 60;

    public sealed record VerificationKeys(byte[] Current, byte[]? Previous, bool UsedLegacyPlaintext)
        : IDisposable
    {
        public void Dispose()
        {
            CryptographicOperations.ZeroMemory(Current);
            if (Previous is not null)
                CryptographicOperations.ZeroMemory(Previous);
        }
    }

    public static bool LegacyReadAllowed(IHostEnvironment environment, IConfiguration configuration) =>
        !environment.IsProduction() && configuration.GetValue(LegacyReadSetting, false);

    public static int RotationGraceMinutes(IConfiguration configuration) =>
        Math.Clamp(
            configuration.GetValue(RotationGraceMinutesSetting, DefaultRotationGraceMinutes),
            0,
            MaximumRotationGraceMinutes);

    /// <summary>Encrypts a newly generated secret or returns null when encryption is unavailable.</summary>
    public static string? EncryptForStorage(PiiProtectionService protection, string plaintext)
    {
        if (!protection.Enabled || string.IsNullOrWhiteSpace(plaintext))
            return null;

        var encrypted = protection.Encrypt(plaintext);
        return encrypted?.StartsWith("enc:", StringComparison.Ordinal) == true ? encrypted : null;
    }

    /// <summary>
    /// Resolves encrypted current/previous keys for constant-time verification. A previous key is
    /// exposed only until its bounded grace timestamp. Legacy plaintext is considered only when
    /// the caller explicitly selected the non-production compatibility projection.
    /// </summary>
    public static VerificationKeys? ResolveForVerification(
        PiiProtectionService protection,
        string? currentEncrypted,
        string? previousEncrypted,
        DateTimeOffset? previousValidUntil,
        string? legacyPlaintext,
        bool allowLegacyPlaintext,
        DateTimeOffset now,
        ILogger logger)
    {
        var current = DecryptEnvelope(protection, currentEncrypted);
        var usedLegacy = false;

        if (current is null && allowLegacyPlaintext && !string.IsNullOrWhiteSpace(legacyPlaintext))
        {
            current = Encoding.UTF8.GetBytes(legacyPlaintext);
            usedLegacy = true;
            logger.LogWarning(
                "A non-production telemetry device used legacy plaintext HMAC material. Rotate it immediately; production never enables or queries this path.");
        }

        if (current is null || current.Length < 32)
        {
            if (current is not null)
                CryptographicOperations.ZeroMemory(current);
            return null;
        }

        byte[]? previous = null;
        if (previousValidUntil is { } validUntil && validUntil > now)
        {
            previous = DecryptEnvelope(protection, previousEncrypted);
            if (previous is { Length: < 32 })
            {
                CryptographicOperations.ZeroMemory(previous);
                previous = null;
            }
        }

        return new VerificationKeys(current, previous, usedLegacy);
    }

    private static byte[]? DecryptEnvelope(PiiProtectionService protection, string? stored)
    {
        // PiiProtectionService supports plaintext for broad legacy PII compatibility. Device
        // credentials intentionally do not: enforcing the prefix here prevents an accidental
        // production plaintext read even if malformed data reaches an encrypted column.
        if (!protection.Enabled || stored?.StartsWith("enc:", StringComparison.Ordinal) != true)
            return null;

        var plaintext = protection.Decrypt(stored);
        return string.IsNullOrWhiteSpace(plaintext) ? null : Encoding.UTF8.GetBytes(plaintext);
    }
}
