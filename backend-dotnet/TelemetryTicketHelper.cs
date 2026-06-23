namespace Opstrax.Api;

internal static class TelemetryTicketHelper
{
    internal static string Issue(byte[] key, long userId, long companyId, int ttlSeconds = 90)
    {
        var exp          = DateTimeOffset.UtcNow.AddSeconds(ttlSeconds).ToUnixTimeSeconds();
        var payload      = $"{userId}:{companyId}:{exp}";
        var payloadBytes = System.Text.Encoding.UTF8.GetBytes(payload);
        using var hmac   = new System.Security.Cryptography.HMACSHA256(key);
        var sig          = Convert.ToBase64String(hmac.ComputeHash(payloadBytes));
        return Convert.ToBase64String(payloadBytes) + "." + sig;
    }

    internal static (bool Ok, long UserId, long CompanyId) Validate(byte[] key, string? ticket)
    {
        if (string.IsNullOrWhiteSpace(ticket)) return (false, 0, 0);
        var parts = ticket.Split('.');
        if (parts.Length != 2) return (false, 0, 0);
        try
        {
            var payloadBytes = Convert.FromBase64String(parts[0]);
            using var hmac   = new System.Security.Cryptography.HMACSHA256(key);
            var expectedSig  = Convert.ToBase64String(hmac.ComputeHash(payloadBytes));
            var eSigBytes    = System.Text.Encoding.UTF8.GetBytes(expectedSig);
            var aSigBytes    = System.Text.Encoding.UTF8.GetBytes(parts[1]);
            if (!System.Security.Cryptography.CryptographicOperations.FixedTimeEquals(eSigBytes, aSigBytes)) return (false, 0, 0);
            var payload = System.Text.Encoding.UTF8.GetString(payloadBytes);
            var fields  = payload.Split(':');
            if (fields.Length != 3) return (false, 0, 0);
            var exp = long.Parse(fields[2]);
            if (DateTimeOffset.UtcNow.ToUnixTimeSeconds() > exp) return (false, 0, 0);
            return (true, long.Parse(fields[0]), long.Parse(fields[1]));
        }
        catch { return (false, 0, 0); }
    }

    internal static bool IsCoordinateValid(decimal lat, decimal lng)
        => lat is >= -90m and <= 90m && lng is >= -180m and <= 180m && !(lat is 0 and 0);

    internal static bool IsSpeedValid(decimal speedMph)
        => speedMph is >= 0m and <= 200m;

    internal static bool IsTimestampFresh(string? eventTimeStr, double windowSeconds = 300)
    {
        if (string.IsNullOrWhiteSpace(eventTimeStr)) return true;
        if (!DateTimeOffset.TryParse(eventTimeStr, out var deviceTime)) return false;
        return Math.Abs((DateTimeOffset.UtcNow - deviceTime).TotalSeconds) <= windowSeconds;
    }
}
