using System.Security.Cryptography;
using System.Text;

namespace Opstrax.Api.Security;

// ─────────────────────────────────────────────────────────────────────────────
// Stateless, short-lived MFA login challenge. After a correct password, an
// MFA-required user is NOT given a session — they receive a signed challenge
// token that authorises exactly one thing: completing the second factor at
// /api/auth/mfa/login-verify. Stateless (HMAC-signed, no table, works on the
// pre-auth login path with no tenant context) with a short TTL; possession of
// the token alone is useless without a currently-valid TOTP code.
// ─────────────────────────────────────────────────────────────────────────────
public static class MfaChallengeService
{
    private const int DefaultTtlSeconds = 300; // 5 minutes to fetch a code from the authenticator

    // token = base64url(payload) + "." + base64url(HMAC-SHA256(key, payload))
    // payload = "{userId}:{companyId}:{expiresAtUnixSeconds}"
    public static string Issue(string key, long userId, long companyId, DateTimeOffset now, int ttlSeconds = DefaultTtlSeconds)
    {
        var exp = now.ToUnixTimeSeconds() + ttlSeconds;
        var payload = $"{userId}:{companyId}:{exp}";
        return B64(Encoding.UTF8.GetBytes(payload)) + "." + B64(Sign(key, payload));
    }

    public static bool TryValidate(string key, string? token, DateTimeOffset now, out long userId, out long companyId)
    {
        userId = 0;
        companyId = 0;
        if (string.IsNullOrWhiteSpace(token)) return false;

        var dot = token.IndexOf('.');
        if (dot <= 0 || dot == token.Length - 1) return false;

        byte[] payloadBytes, sigBytes;
        try
        {
            payloadBytes = UnB64(token[..dot]);
            sigBytes = UnB64(token[(dot + 1)..]);
        }
        catch { return false; }

        var payload = Encoding.UTF8.GetString(payloadBytes);
        // Constant-time signature check before trusting any field.
        if (!CryptographicOperations.FixedTimeEquals(sigBytes, Sign(key, payload))) return false;

        var parts = payload.Split(':');
        if (parts.Length != 3) return false;
        if (!long.TryParse(parts[0], out var uid) || !long.TryParse(parts[1], out var cid) || !long.TryParse(parts[2], out var exp))
            return false;
        if (now.ToUnixTimeSeconds() > exp) return false; // expired

        userId = uid;
        companyId = cid;
        return true;
    }

    private static byte[] Sign(string key, string payload)
    {
        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(key));
        return hmac.ComputeHash(Encoding.UTF8.GetBytes(payload));
    }

    private static string B64(byte[] data) =>
        Convert.ToBase64String(data).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    private static byte[] UnB64(string s)
    {
        var b = s.Replace('-', '+').Replace('_', '/');
        b += (b.Length % 4) switch { 2 => "==", 3 => "=", _ => "" };
        return Convert.FromBase64String(b);
    }
}
