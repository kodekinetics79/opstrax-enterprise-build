using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using FluentAssertions;
using Microsoft.IdentityModel.Tokens;
using Zayra.Api.Application.Auth;

namespace Zayra.Api.Tests.Security;

/// <summary>
/// Verifies that audience-based token boundary enforcement works correctly.
///
/// After P1.5 hardening, the PlatformAdmin policy requires BOTH:
///   - is_platform_admin:"true"
///   - aud:"kynexone-platform"
///
/// A tenant-audience token carrying is_platform_admin is rejected at the claim level.
/// A platform-audience token is rejected by the tenant JWT validator (wrong audience).
///
/// These tests operate at the JWT claim level — no HTTP stack needed.
/// </summary>
public class TokenAudienceIsolationTests
{
    private const string SigningKey       = "TEST_AUDIENCE_ISOLATION_SIGNING_KEY_MUST_BE_64_CHARS__PADDED00";
    private const string Issuer           = "Zayra.Tests";
    private const string TenantAudience   = "kynexone-tenant";
    private const string PlatformAudience = "kynexone-platform";

    // ── Token factory helpers ─────────────────────────────────────────────────

    private static string BuildToken(string audience, IEnumerable<Claim> claims, int expiryHours = 1)
    {
        var key         = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(SigningKey));
        var credentials = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);
        var token = new JwtSecurityToken(
            issuer:             Issuer,
            audience:           audience,
            claims:             claims,
            expires:            DateTime.UtcNow.AddHours(expiryHours),
            signingCredentials: credentials);
        return new JwtSecurityTokenHandler().WriteToken(token);
    }

    private static ClaimsPrincipal? ParseToken(string tokenString, string[] validAudiences)
    {
        var handler = new JwtSecurityTokenHandler();
        var key     = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(SigningKey));

        try
        {
            var principal = handler.ValidateToken(tokenString, new TokenValidationParameters
            {
                ValidateIssuer           = true,
                ValidateAudience         = true,
                ValidateLifetime         = true,
                ValidateIssuerSigningKey = true,
                ValidIssuer              = Issuer,
                ValidAudiences           = validAudiences,
                IssuerSigningKey         = key,
                ClockSkew                = TimeSpan.Zero
            }, out _);
            return principal;
        }
        catch (SecurityTokenException)
        {
            return null;
        }
    }

    // ── PlatformAdmin policy simulation ──────────────────────────────────────

    /// <summary>
    /// Returns true only when BOTH conditions of the PlatformAdmin policy are satisfied:
    ///   1. is_platform_admin == "true"
    ///   2. aud == kynexone-platform
    /// This mirrors the RequireClaim calls in Program.cs.
    /// </summary>
    private static bool SatisfiesPlatformAdminPolicy(ClaimsPrincipal principal) =>
        principal.HasClaim("is_platform_admin", "true") &&
        principal.HasClaim("aud", PlatformAudience);

    // ── Acceptance: platform token passes ────────────────────────────────────

    [Fact]
    public void PlatformToken_WithCorrectAudienceAndClaims_PassesPlatformAdminPolicy()
    {
        var tokenString = BuildToken(PlatformAudience, new[]
        {
            new Claim(JwtRegisteredClaimNames.Sub, "platform-admin"),
            new Claim("is_platform_admin", "true"),
            new Claim("platform_role", "Owner"),
        });

        var principal = ParseToken(tokenString, new[] { TenantAudience, PlatformAudience });

        principal.Should().NotBeNull("platform token must validate with the shared signing key");
        SatisfiesPlatformAdminPolicy(principal!).Should().BeTrue(
            "platform token carries both is_platform_admin and the correct aud claim");
    }

    // ── Rejection: tenant token is 403'd on platform routes ──────────────────

    [Fact]
    public void TenantToken_IsRejectedByPlatformAdminPolicy_DueToWrongAudience()
    {
        // A regular tenant login token — correct claims for its own scope, but tenant audience.
        var tokenString = BuildToken(TenantAudience, new[]
        {
            new Claim(JwtRegisteredClaimNames.Sub, "user-guid"),
            new Claim("tenant_id", Guid.NewGuid().ToString()),
            new Claim(ClaimTypes.Role, "HR Manager"),
        });

        var principal = ParseToken(tokenString, new[] { TenantAudience, PlatformAudience });

        principal.Should().NotBeNull("JWT signature is valid so validation should succeed");
        SatisfiesPlatformAdminPolicy(principal!).Should().BeFalse(
            "tenant token lacks both is_platform_admin and the platform audience claim");
    }

    [Fact]
    public void TenantToken_CarryingForgishedPlatformClaim_IsStillRejectedByAudienceCheck()
    {
        // Worst-case: a tenant-audience token that somehow carries is_platform_admin.
        // This was the only guard BEFORE P1.5. Now the aud check provides a second gate.
        var tokenString = BuildToken(TenantAudience, new[]
        {
            new Claim(JwtRegisteredClaimNames.Sub, "attacker"),
            new Claim("tenant_id", Guid.NewGuid().ToString()),
            new Claim("is_platform_admin", "true"),  // injected claim — wrong audience token
        });

        var principal = ParseToken(tokenString, new[] { TenantAudience, PlatformAudience });

        principal.Should().NotBeNull("signature is still valid — we're proving claim-based gate");
        // Even with is_platform_admin present, the aud claim is kynexone-tenant, not kynexone-platform.
        principal!.HasClaim("is_platform_admin", "true").Should().BeTrue(
            "claim is present — this is what the OLD single-gate check would have passed");
        SatisfiesPlatformAdminPolicy(principal).Should().BeFalse(
            "PlatformAdmin policy now requires aud=kynexone-platform — tenant aud fails the second gate");
    }

    // ── Rejection: platform token rejected on tenant-only endpoints ───────────

    [Fact]
    public void PlatformToken_IsRejectedByTenantAudienceValidator()
    {
        // Simulate a tenant-only endpoint that validates only kynexone-tenant.
        // (Currently enforced via the PlatformAdmin policy requiring platform aud,
        //  but this test confirms the JWT middleware audience check would catch it too.)
        var tokenString = BuildToken(PlatformAudience, new[]
        {
            new Claim(JwtRegisteredClaimNames.Sub, "platform-admin"),
            new Claim("is_platform_admin", "true"),
            new Claim("platform_role", "Owner"),
        });

        // Tenant endpoints only accept TenantAudience — simulate that restriction.
        var principal = ParseToken(tokenString, new[] { TenantAudience });

        principal.Should().BeNull(
            "platform-audience token must be rejected when only kynexone-tenant is a valid audience");
    }

    // ── Structural: JwtOptions defaults are well-formed ───────────────────────

    [Fact]
    public void JwtOptions_DefaultAudienceValues_AreDistinctAndNonEmpty()
    {
        var opts = new JwtOptions();

        opts.TenantAudience.Should().NotBeNullOrWhiteSpace();
        opts.PlatformAudience.Should().NotBeNullOrWhiteSpace();
        opts.TenantAudience.Should().NotBe(opts.PlatformAudience,
            "each token type must have a distinct audience so one cannot be used in place of the other");
    }
}
