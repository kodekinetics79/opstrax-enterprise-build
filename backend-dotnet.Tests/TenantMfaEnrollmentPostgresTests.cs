using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Opstrax.Api.Data;
using Opstrax.Api.Security;

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
        await db.ExecuteAsync("DELETE FROM user_mfa_status WHERE user_id=@id", c => c.Parameters.AddWithValue("@id", uid));
        await db.ExecuteAsync("DELETE FROM users WHERE id=@id", c => c.Parameters.AddWithValue("@id", uid));
        await db.ExecuteAsync("DELETE FROM companies WHERE id=@id", c => c.Parameters.AddWithValue("@id", cid));
    }
}
