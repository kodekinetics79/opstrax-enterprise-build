using Opstrax.Api.Security;

namespace Opstrax.Tests;

// Unit tests for the stateless MFA login challenge (the new security-critical crypto behind the
// two-step login). Possession of a token must be useless if tampered, from a different key, or expired.
public class MfaChallengeServiceTests
{
    private const string Key = "test-signing-key-0123456789-abcdef";
    private static readonly DateTimeOffset T0 = DateTimeOffset.FromUnixTimeSeconds(1_800_000_000);

    [Fact]
    public void Issue_Then_Validate_RoundTrips_The_Identity()
    {
        var token = MfaChallengeService.Issue(Key, userId: 42, companyId: 7, T0);
        Assert.True(MfaChallengeService.TryValidate(Key, token, T0, out var uid, out var cid));
        Assert.Equal(42, uid);
        Assert.Equal(7, cid);
    }

    [Fact]
    public void Tampered_Token_Is_Rejected()
    {
        var token = MfaChallengeService.Issue(Key, 42, 7, T0);
        // Flip a character in the payload half.
        var dot = token.IndexOf('.');
        var tampered = (token[0] == 'A' ? 'B' : 'A') + token[1..dot] + token[dot..];
        Assert.False(MfaChallengeService.TryValidate(Key, tampered, T0, out _, out _));
    }

    [Fact]
    public void Different_Key_Is_Rejected()
    {
        var token = MfaChallengeService.Issue(Key, 42, 7, T0);
        Assert.False(MfaChallengeService.TryValidate("a-completely-different-key", token, T0, out _, out _));
    }

    [Fact]
    public void Expired_Token_Is_Rejected()
    {
        var token = MfaChallengeService.Issue(Key, 42, 7, T0, ttlSeconds: 300);
        var later = T0.AddSeconds(301);
        Assert.False(MfaChallengeService.TryValidate(Key, token, later, out _, out _));
    }

    [Fact]
    public void Within_Ttl_Is_Accepted()
    {
        var token = MfaChallengeService.Issue(Key, 42, 7, T0, ttlSeconds: 300);
        Assert.True(MfaChallengeService.TryValidate(Key, token, T0.AddSeconds(299), out _, out _));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("garbage")]
    [InlineData("no-dot-here")]
    [InlineData(".")]
    public void Malformed_Tokens_Are_Rejected(string? token)
    {
        Assert.False(MfaChallengeService.TryValidate(Key, token, T0, out _, out _));
    }
}
