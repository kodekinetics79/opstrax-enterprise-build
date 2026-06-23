namespace Opstrax.Api;

// HMAC-SHA256 payload signing for device ingest requests.
// Canonical string: "{METHOD}\n{path}\n{X-Timestamp}\n{X-Nonce}\n{hex-sha256(raw-body)}"
internal static class TelemetryHmacHelper
{
    // Compute expected signature from canonical request components.
    internal static string ComputeSignature(
        byte[] secret, string method, string path,
        string timestamp, string nonce, string bodyHex)
    {
        var canonical  = $"{method}\n{path}\n{timestamp}\n{nonce}\n{bodyHex}";
        var msgBytes   = System.Text.Encoding.UTF8.GetBytes(canonical);
        using var hmac = new System.Security.Cryptography.HMACSHA256(secret);
        return Convert.ToHexString(hmac.ComputeHash(msgBytes)).ToLowerInvariant();
    }

    // Compute lowercase hex SHA-256 of a UTF-8 string (used for body hash).
    internal static string Sha256Hex(string body)
    {
        var bytes = System.Text.Encoding.UTF8.GetBytes(body);
        return Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(bytes)).ToLowerInvariant();
    }

    // Constant-time comparison of two ASCII hex strings (lengths may differ → immediate false).
    internal static bool ConstantTimeEquals(string a, string b)
    {
        var aBytes = System.Text.Encoding.UTF8.GetBytes(a ?? "");
        var bBytes = System.Text.Encoding.UTF8.GetBytes(b ?? "");
        return System.Security.Cryptography.CryptographicOperations.FixedTimeEquals(aBytes, bBytes);
    }
}
