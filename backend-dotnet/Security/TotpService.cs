using System.Security.Cryptography;
using System.Text;

namespace Opstrax.Api.Security;

// ─────────────────────────────────────────────────────────────────────────────
// RFC 6238 TOTP (SHA-1, 6 digits, 30-second step) — dependency-free.
// Used for Platform Admin second-factor auth. Verification accepts ±1 time
// step to absorb clock skew. Secrets are 20 random bytes, base32-encoded so
// they can be typed into any authenticator app.
// ─────────────────────────────────────────────────────────────────────────────
public static class TotpService
{
    private const int SecretBytes = 20;
    private const int Digits = 6;
    private const long StepSeconds = 30;
    private const string Base32Alphabet = "ABCDEFGHIJKLMNOPQRSTUVWXYZ234567";

    public static string GenerateSecret()
        => Base32Encode(RandomNumberGenerator.GetBytes(SecretBytes));

    public static string BuildOtpAuthUri(string issuer, string account, string secret)
        => $"otpauth://totp/{Uri.EscapeDataString(issuer)}:{Uri.EscapeDataString(account)}" +
           $"?secret={secret}&issuer={Uri.EscapeDataString(issuer)}&algorithm=SHA1&digits={Digits}&period={StepSeconds}";

    public static bool VerifyCode(string base32Secret, string? code, DateTimeOffset? now = null)
    {
        if (string.IsNullOrWhiteSpace(code)) return false;
        var trimmed = code.Trim().Replace(" ", "");
        if (trimmed.Length != Digits || !trimmed.All(char.IsAsciiDigit)) return false;

        byte[] key;
        try { key = Base32Decode(base32Secret); }
        catch { return false; }

        var step = (now ?? DateTimeOffset.UtcNow).ToUnixTimeSeconds() / StepSeconds;
        for (var offset = -1; offset <= 1; offset++)
        {
            var expected = ComputeCode(key, step + offset);
            if (CryptographicOperations.FixedTimeEquals(
                    Encoding.ASCII.GetBytes(expected), Encoding.ASCII.GetBytes(trimmed)))
                return true;
        }
        return false;
    }

    // internal so tests can mint a currently-valid code without duplicating the math.
    internal static string ComputeCurrentCode(string base32Secret, DateTimeOffset? now = null)
    {
        var step = (now ?? DateTimeOffset.UtcNow).ToUnixTimeSeconds() / StepSeconds;
        return ComputeCode(Base32Decode(base32Secret), step);
    }

    private static string ComputeCode(byte[] key, long timeStep)
    {
        Span<byte> counter = stackalloc byte[8];
        System.Buffers.Binary.BinaryPrimitives.WriteInt64BigEndian(counter, timeStep);
        using var hmac = new HMACSHA1(key);
        var hash = hmac.ComputeHash(counter.ToArray());
        var dynOffset = hash[^1] & 0x0F;
        var binary = ((hash[dynOffset] & 0x7F) << 24)
                   | ((hash[dynOffset + 1] & 0xFF) << 16)
                   | ((hash[dynOffset + 2] & 0xFF) << 8)
                   | (hash[dynOffset + 3] & 0xFF);
        return (binary % 1_000_000).ToString("D6");
    }

    private static string Base32Encode(byte[] data)
    {
        var sb = new StringBuilder((data.Length + 4) / 5 * 8);
        int buffer = 0, bits = 0;
        foreach (var b in data)
        {
            buffer = (buffer << 8) | b;
            bits += 8;
            while (bits >= 5)
            {
                sb.Append(Base32Alphabet[(buffer >> (bits - 5)) & 0x1F]);
                bits -= 5;
            }
        }
        if (bits > 0) sb.Append(Base32Alphabet[(buffer << (5 - bits)) & 0x1F]);
        return sb.ToString();
    }

    private static byte[] Base32Decode(string encoded)
    {
        var clean = encoded.Trim().TrimEnd('=').ToUpperInvariant();
        var output = new List<byte>(clean.Length * 5 / 8);
        int buffer = 0, bits = 0;
        foreach (var ch in clean)
        {
            var index = Base32Alphabet.IndexOf(ch);
            if (index < 0) throw new FormatException("Invalid base32 character");
            buffer = (buffer << 5) | index;
            bits += 5;
            if (bits >= 8)
            {
                output.Add((byte)((buffer >> (bits - 8)) & 0xFF));
                bits -= 8;
            }
        }
        return output.ToArray();
    }
}
