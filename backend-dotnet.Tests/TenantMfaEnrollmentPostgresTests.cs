using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Npgsql;
using Opstrax.Api.Data;
using Opstrax.Api.Security;
using Opstrax.Api.Services;

namespace Opstrax.Tests;

// P0 fix — tenant-user MFA enrollment. Before this, "require MFA" was a login lockout: there was no
// path to write user_mfa_status for a tenant user, so mfa_enabled could never become true. These tests
// exercise the exact store-and-activate logic the /api/auth/mfa/enroll + /verify endpoints run:
// generate a TOTP secret, store it ENCRYPTED, prove a currently-valid code activates it, and prove a
// wrong code does not. Also asserts the schema column the enrollment path depends on exists.
[Trait("Category", "Integration")]
public class TenantMfaEnrollmentPostgresTests
{
    private static PiiProtectionService Pii() =>
        new(new TestKeyProvider(), NullLogger<PiiProtectionService>.Instance);

    private static Database CreateDatabase() =>
        new(new ConfigurationBuilder().AddInMemoryCollection(
            new Dictionary<string, string?> { ["ConnectionStrings:DefaultConnection"] = TestDb.ConnectionString }).Build());

    [Fact]
    public async Task Enroll_Then_ValidCode_Activates_And_WrongCode_Does_Not()
    {
        var db = CreateDatabase();
        var pii = Pii();
        var (cid, uid) = await SeedUserAsync(db);
        try
        {
            // ── enroll: store an ENCRYPTED secret, not yet enabled (what /enroll does) ──
            var secret = TotpService.GenerateSecret();
            var enc = pii.Encrypt(secret);
            Assert.NotNull(enc);
            Assert.NotEqual(secret, enc); // stored form must be ciphertext, never the raw secret
            await db.ExecuteAsync(
                @"INSERT INTO user_mfa_status (user_id, mfa_enabled, mfa_provider, mfa_secret, updated_at)
                  VALUES (@id, false, 'totp', @s, NOW())
                  ON CONFLICT (user_id) DO UPDATE SET mfa_secret=@s, mfa_enabled=false, updated_at=NOW()",
                c => { c.Parameters.AddWithValue("@id", uid); c.Parameters.AddWithValue("@s", enc!); });

            var stored = pii.Decrypt((await db.QuerySingleAsync(
                "SELECT mfa_secret FROM user_mfa_status WHERE user_id=@id",
                c => c.Parameters.AddWithValue("@id", uid)))?["mfaSecret"]?.ToString());
            Assert.Equal(secret, stored); // round-trips back to the original secret

            // ── verify: a wrong code must NOT activate ──
            Assert.False(TotpService.VerifyCode(stored!, "000000"));

            // ── verify: a currently-valid code DOES, and the activation UPDATE flips mfa_enabled ──
            var code = TotpService.ComputeCurrentCode(stored!);
            Assert.True(TotpService.VerifyCode(stored!, code));
            await db.ExecuteAsync(
                @"UPDATE user_mfa_status SET mfa_enabled=true, enrolled_at=COALESCE(enrolled_at, NOW()),
                    last_used_at=NOW(), updated_at=NOW() WHERE user_id=@id",
                c => c.Parameters.AddWithValue("@id", uid));

            var enabled = await db.ScalarLongAsync(
                "SELECT COUNT(*) FROM user_mfa_status WHERE user_id=@id AND mfa_enabled=true",
                c => c.Parameters.AddWithValue("@id", uid));
            Assert.Equal(1, enabled);
        }
        finally { await CleanupAsync(db, cid, uid); }
    }

    [Fact]
    public async Task Challenge_Consumption_Is_Durable_Atomic_And_Expires()
    {
        var db = CreateDatabase();
        await new SecuritySchemaService(db).EnsureAsync();
        var (cid, uid) = await SeedUserAsync(db);
        try
        {
            const string key = "mfa-consumption-test-key-0123456789";
            var now = DateTimeOffset.UtcNow;
            var challenge = MfaChallengeService.Issue(key, uid, cid, now);
            Assert.True(MfaChallengeService.TryValidate(key, challenge, now, out MfaChallengeClaims claims));

            // Two independent service/Database instances model two API replicas
            // racing to complete the same valid challenge. PostgreSQL must choose
            // exactly one winner via the unique challenge digest.
            var contenders = Enumerable.Range(0, 2).Select(async _ =>
            {
                var contenderDb = CreateDatabase();
                return await new MfaChallengeConsumptionService(contenderDb)
                    .TryConsumeAsync(challenge, claims);
            });
            var outcomes = await Task.WhenAll(contenders);
            Assert.Equal(1, outcomes.Count(value => value));
            Assert.Equal(1, outcomes.Count(value => !value));

            // Restart/new-instance replay remains rejected because state is durable.
            Assert.False(await new MfaChallengeConsumptionService(CreateDatabase())
                .TryConsumeAsync(challenge, claims));

            var expiredChallenge = MfaChallengeService.Issue(key, uid, cid, now.AddMinutes(-10), ttlSeconds: 60);
            Assert.False(MfaChallengeService.TryValidate(key, expiredChallenge, now, out MfaChallengeClaims _));
            var expiredClaims = new MfaChallengeClaims(uid, cid, now.AddSeconds(-1), "expired-test-jti");
            Assert.False(await new MfaChallengeConsumptionService(CreateDatabase())
                .TryConsumeAsync(expiredChallenge, expiredClaims));

            var stored = await db.QuerySingleAsync(
                "SELECT company_id, user_id, challenge_hash, expires_at FROM mfa_login_challenge_consumptions WHERE company_id=@cid AND user_id=@uid",
                c => { c.Parameters.AddWithValue("@cid", cid); c.Parameters.AddWithValue("@uid", uid); });
            Assert.NotNull(stored);
            Assert.Matches("^[0-9a-f]{64}$", stored!["challengeHash"]?.ToString() ?? "");
            Assert.DoesNotContain(challenge, stored.Values.Select(value => value?.ToString() ?? ""));
        }
        finally
        {
            await db.ExecuteAsync("DELETE FROM mfa_login_challenge_consumptions WHERE company_id=@cid", c => c.Parameters.AddWithValue("@cid", cid));
            await CleanupAsync(db, cid, uid);
        }
    }

    [Fact]
    public async Task MfaShareLocks_SerializeConcurrentUserDisable_AndLifecycleRevokesWinningSession()
    {
        var db = CreateDatabase();
        var (cid, uid) = await SeedUserAsync(db);
        try
        {
            await using var mfaConnection = new NpgsqlConnection(TestDb.ConnectionString);
            await using var lifecycleConnection = new NpgsqlConnection(TestDb.ConnectionString);
            await mfaConnection.OpenAsync();
            await lifecycleConnection.OpenAsync();
            await using var mfaTx = await mfaConnection.BeginTransactionAsync();
            await using var lifecycleTx = await lifecycleConnection.BeginTransactionAsync();

            await using (var select = new NpgsqlCommand(
                @"SELECT u.status, c.status FROM users u JOIN companies c ON c.id=u.company_id
                  WHERE u.id=@uid AND u.company_id=@cid FOR SHARE OF u, c", mfaConnection, mfaTx))
            {
                select.Parameters.AddWithValue("@uid", uid);
                select.Parameters.AddWithValue("@cid", cid);
                await select.ExecuteScalarAsync();
            }

            var lifecycle = Task.Run(async () =>
            {
                await using var disable = new NpgsqlCommand(
                    "UPDATE users SET status='Disabled' WHERE id=@uid", lifecycleConnection, lifecycleTx);
                disable.Parameters.AddWithValue("@uid", uid);
                await disable.ExecuteNonQueryAsync();
                await using var revoke = new NpgsqlCommand(
                    "DELETE FROM user_sessions WHERE user_id=@uid", lifecycleConnection, lifecycleTx);
                revoke.Parameters.AddWithValue("@uid", uid);
                await revoke.ExecuteNonQueryAsync();
                await lifecycleTx.CommitAsync();
            });

            Assert.NotSame(lifecycle, await Task.WhenAny(lifecycle, Task.Delay(150)));
            await using (var session = new NpgsqlCommand(
                @"INSERT INTO user_sessions (user_id, company_id, session_token, expires_at)
                  VALUES (@uid, @cid, @token, NOW() + INTERVAL '1 hour')", mfaConnection, mfaTx))
            {
                session.Parameters.AddWithValue("@uid", uid);
                session.Parameters.AddWithValue("@cid", cid);
                session.Parameters.AddWithValue("@token", $"mfa-race-{Guid.NewGuid():N}");
                await session.ExecuteNonQueryAsync();
            }
            await mfaTx.CommitAsync();
            await lifecycle.WaitAsync(TimeSpan.FromSeconds(5));

            Assert.Equal(0, await db.ScalarLongAsync(
                "SELECT COUNT(*) FROM user_sessions WHERE user_id=@uid",
                c => c.Parameters.AddWithValue("@uid", uid)));
        }
        finally { await CleanupAsync(db, cid, uid); }
    }

    [Fact]
    public async Task TenantSuspensionWinningRace_IsObservedByBlockedMfaLifecycleRead()
    {
        var db = CreateDatabase();
        var (cid, uid) = await SeedUserAsync(db);
        try
        {
            await using var lifecycleConnection = new NpgsqlConnection(TestDb.ConnectionString);
            await using var mfaConnection = new NpgsqlConnection(TestDb.ConnectionString);
            await lifecycleConnection.OpenAsync();
            await mfaConnection.OpenAsync();
            await using var lifecycleTx = await lifecycleConnection.BeginTransactionAsync();
            await using var mfaTx = await mfaConnection.BeginTransactionAsync();

            await using (var suspend = new NpgsqlCommand(
                "UPDATE companies SET status='Suspended' WHERE id=@cid", lifecycleConnection, lifecycleTx))
            {
                suspend.Parameters.AddWithValue("@cid", cid);
                await suspend.ExecuteNonQueryAsync();
            }

            var mfaRead = Task.Run(async () =>
            {
                await using var select = new NpgsqlCommand(
                    @"SELECT c.status FROM users u JOIN companies c ON c.id=u.company_id
                      WHERE u.id=@uid AND u.company_id=@cid FOR SHARE OF u, c", mfaConnection, mfaTx);
                select.Parameters.AddWithValue("@uid", uid);
                select.Parameters.AddWithValue("@cid", cid);
                return (string?)await select.ExecuteScalarAsync();
            });

            Assert.NotSame(mfaRead, await Task.WhenAny(mfaRead, Task.Delay(150)));
            await lifecycleTx.CommitAsync();
            Assert.Equal("Suspended", await mfaRead.WaitAsync(TimeSpan.FromSeconds(5)));
            await mfaTx.CommitAsync();
        }
        finally { await CleanupAsync(db, cid, uid); }
    }

    private static async Task<(long cid, long uid)> SeedUserAsync(Database db)
    {
        var cid = await db.InsertAsync(
            "INSERT INTO companies (company_code, name, industry) VALUES (@code, 'MFA Co', 'logistics') RETURNING id",
            c => c.Parameters.AddWithValue("@code", $"MFA-{Guid.NewGuid():N}".Substring(0, 15)));
        var uid = await db.InsertAsync(
            @"INSERT INTO users (company_id, email, full_name, role_name, password_hash)
              VALUES (@cid, @email, 'MFA User', 'Company Admin', 'x') RETURNING id",
            c => { c.Parameters.AddWithValue("@cid", cid); c.Parameters.AddWithValue("@email", $"mfa-{Guid.NewGuid():N}@ex.com"); });
        return (cid, uid);
    }

    private static async Task CleanupAsync(Database db, long cid, long uid)
    {
        await db.ExecuteAsync("DELETE FROM user_sessions WHERE user_id=@id", c => c.Parameters.AddWithValue("@id", uid));
        await db.ExecuteAsync("DELETE FROM user_mfa_status WHERE user_id=@id", c => c.Parameters.AddWithValue("@id", uid));
        await db.ExecuteAsync("DELETE FROM users WHERE id=@id", c => c.Parameters.AddWithValue("@id", uid));
        await db.ExecuteAsync("DELETE FROM companies WHERE id=@id", c => c.Parameters.AddWithValue("@id", cid));
    }
}
