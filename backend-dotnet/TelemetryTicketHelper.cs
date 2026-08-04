namespace Opstrax.Api;

internal static class TelemetryTicketHelper
{
    internal sealed record ScopedTicketClaims(
        bool Ok, long UserId, long CompanyId, long? BranchId,
        bool AllBranches, string[] Permissions, string Nonce, long ExpiresAt);

    private sealed record ScopedTicketPayload(
        int Version, long UserId, long CompanyId, long? BranchId,
        bool AllBranches, string[] Permissions, string Nonce, long ExpiresAt);

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

    internal static string IssueScoped(
        byte[] key, long userId, long companyId, long? branchId,
        IEnumerable<string> permissions, int ttlSeconds = 90)
    {
        var payload = new ScopedTicketPayload(
            2, userId, companyId, branchId, branchId is null,
            permissions.Distinct(StringComparer.OrdinalIgnoreCase).Order(StringComparer.OrdinalIgnoreCase).ToArray(),
            Convert.ToHexString(System.Security.Cryptography.RandomNumberGenerator.GetBytes(16)).ToLowerInvariant(),
            DateTimeOffset.UtcNow.AddSeconds(ttlSeconds).ToUnixTimeSeconds());
        var payloadBytes = System.Text.Json.JsonSerializer.SerializeToUtf8Bytes(payload);
        using var hmac = new System.Security.Cryptography.HMACSHA256(key);
        var sig = hmac.ComputeHash(payloadBytes);
        return Convert.ToBase64String(payloadBytes) + "." + Convert.ToBase64String(sig);
    }

    internal static ScopedTicketClaims ValidateScoped(byte[] key, string? ticket)
    {
        static ScopedTicketClaims Invalid() => new(false, 0, 0, null, false, [], "", 0);
        if (string.IsNullOrWhiteSpace(ticket)) return Invalid();
        var parts = ticket.Split('.');
        if (parts.Length != 2) return Invalid();
        try
        {
            var payloadBytes = Convert.FromBase64String(parts[0]);
            var actualSig = Convert.FromBase64String(parts[1]);
            using var hmac = new System.Security.Cryptography.HMACSHA256(key);
            var expectedSig = hmac.ComputeHash(payloadBytes);
            if (expectedSig.Length != actualSig.Length ||
                !System.Security.Cryptography.CryptographicOperations.FixedTimeEquals(expectedSig, actualSig))
                return Invalid();

            var payload = System.Text.Json.JsonSerializer.Deserialize<ScopedTicketPayload>(payloadBytes);
            var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            if (payload is null || payload.Version != 2 || payload.UserId <= 0 || payload.CompanyId <= 0 ||
                payload.BranchId is <= 0 || (payload.BranchId is null) != payload.AllBranches || string.IsNullOrWhiteSpace(payload.Nonce) ||
                payload.ExpiresAt < now || payload.ExpiresAt > now + 300 ||
                payload.Permissions is null || !payload.Permissions.Contains("telemetry.live_state.read", StringComparer.OrdinalIgnoreCase))
                return Invalid();
            return new(true, payload.UserId, payload.CompanyId, payload.BranchId, payload.AllBranches,
                payload.Permissions, payload.Nonce, payload.ExpiresAt);
        }
        catch { return Invalid(); }
    }

    internal static string HashNonce(string nonce)
        => Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(nonce))).ToLowerInvariant();

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

    // Device request time is independently protected by the HMAC timestamp/nonce.
    // The GPS fix may legitimately be buffered while offline, so accept up to seven
    // days of backlog while rejecting excessive future skew and malformed values.
    internal static bool TryParseObservedAt(string? eventTimeStr, out DateTime observedAt)
    {
        observedAt = DateTime.UtcNow;
        if (string.IsNullOrWhiteSpace(eventTimeStr)) return true;
        if (!DateTimeOffset.TryParse(eventTimeStr, System.Globalization.CultureInfo.InvariantCulture,
                System.Globalization.DateTimeStyles.AssumeUniversal | System.Globalization.DateTimeStyles.AdjustToUniversal,
                out var parsed)) return false;
        var now = DateTimeOffset.UtcNow;
        if (parsed < now.AddDays(-7) || parsed > now.AddMinutes(5)) return false;
        observedAt = parsed.UtcDateTime;
        return true;
    }
}
