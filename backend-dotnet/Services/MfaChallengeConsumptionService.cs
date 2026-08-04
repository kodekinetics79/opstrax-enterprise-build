using System.Security.Cryptography;
using System.Text;
using Opstrax.Api.Data;
using Opstrax.Api.Security;

namespace Opstrax.Api.Services;

/// <summary>
/// Durable, cross-instance one-time consumption for successful MFA login challenges.
/// Only a SHA-256 digest is persisted; neither the signed challenge nor the OTP is stored.
/// The unique digest makes concurrent completion atomic at PostgreSQL.
/// </summary>
public sealed class MfaChallengeConsumptionService(Database db)
{
    public async Task<bool> TryConsumeAsync(
        string rawChallenge,
        MfaChallengeClaims claims,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(rawChallenge) || claims.UserId <= 0 || claims.CompanyId <= 0)
            return false;

        // Bounded opportunistic cleanup prevents an unbounded replay ledger without
        // making successful login depend on a separate scheduler.
        await db.ExecuteAsync("""
            DELETE FROM mfa_login_challenge_consumptions
            WHERE id IN (
                SELECT id FROM mfa_login_challenge_consumptions
                WHERE expires_at < NOW()
                ORDER BY expires_at
                LIMIT 1000
            )
            """, ct: ct);

        var digest = Convert.ToHexString(
            SHA256.HashData(Encoding.UTF8.GetBytes(rawChallenge))).ToLowerInvariant();

        var row = await db.QuerySingleAsync("""
            INSERT INTO mfa_login_challenge_consumptions
                (company_id, user_id, challenge_hash, expires_at, consumed_at)
            SELECT @cid, @uid, @hash, @expires, NOW()
            WHERE @expires > NOW()
            ON CONFLICT (challenge_hash) DO NOTHING
            RETURNING id
            """, c =>
        {
            c.Parameters.AddWithValue("@cid", claims.CompanyId);
            c.Parameters.AddWithValue("@uid", claims.UserId);
            c.Parameters.AddWithValue("@hash", digest);
            c.Parameters.AddWithValue("@expires", claims.ExpiresAt);
        }, ct);

        return row is not null;
    }
}
