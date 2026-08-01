using Opstrax.Api.Services;
using Opstrax.Api.Controllers;
using System.Reflection;

namespace Opstrax.Tests;

public sealed class AuthenticationMfaTests
{
    [Fact]
    public void DisabledPolicy_DoesNotRequireMfa()
    {
        var settings = Settings(required: false, "Company Admin");

        Assert.False(SecuritySettingsService.IsMfaRequiredForRole(settings, "Company Admin"));
    }

    [Fact]
    public void EnabledPolicy_WithNoRoles_AppliesToEveryRole()
    {
        var settings = Settings(required: true);

        Assert.True(SecuritySettingsService.IsMfaRequiredForRole(settings, "Driver"));
    }

    [Theory]
    [InlineData("Company Admin", "company_admin")]
    [InlineData("GROUP-ADMIN", "Group Admin")]
    [InlineData(" manager ", "Manager")]
    public void ConfiguredRole_MatchesCaseAndSeparators(string configuredRole, string loginRole)
    {
        var settings = Settings(required: true, configuredRole);

        Assert.True(SecuritySettingsService.IsMfaRequiredForRole(settings, loginRole));
    }

    [Fact]
    public void UnconfiguredRole_IsNotBlockedByRoleScopedPolicy()
    {
        var settings = Settings(required: true, "Company Admin", "Manager");

        Assert.False(SecuritySettingsService.IsMfaRequiredForRole(settings, "Driver"));
    }

    [Fact]
    public void WildcardPolicy_AppliesToEveryRole()
    {
        var settings = Settings(required: true, "*");

        Assert.True(SecuritySettingsService.IsMfaRequiredForRole(settings, "Customer"));
    }

    [Fact]
    public void BlankConfiguredRoles_FailClosedAsTenantWidePolicy()
    {
        var settings = Settings(required: true, " ", "");

        Assert.True(SecuritySettingsService.IsMfaRequiredForRole(settings, "Driver"));
    }

    [Fact]
    public void LoginSource_RejectsMfaBeforeCreatingTokenOrSession()
    {
        var source = ReadSource("backend-dotnet", "Controllers", "EndpointMappings.cs");
        var loginStart = source.IndexOf("private static async Task<IResult> Login(", StringComparison.Ordinal);
        var loginEnd = source.IndexOf("private static IResult InvalidCredentials", loginStart, StringComparison.Ordinal);
        var login = source[loginStart..loginEnd];

        var mfaGate = login.IndexOf("IsMfaRequiredForRole", StringComparison.Ordinal);
        var tokenCreation = login.IndexOf("var token =", StringComparison.Ordinal);
        var sessionInsert = login.IndexOf("INSERT INTO user_sessions", StringComparison.Ordinal);

        Assert.True(mfaGate >= 0, "login must enforce tenant MFA policy");
        Assert.True(mfaGate < tokenCreation, "MFA must be enforced before token creation");
        Assert.True(mfaGate < sessionInsert, "MFA must be enforced before session persistence");
        Assert.DoesNotContain("demo_password", login, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("demoPassword", login, StringComparison.Ordinal);
    }

    [Fact]
    public void MfaCompletion_Consumes_Durable_Challenge_Before_Session_Issuance()
    {
        var source = ReadSource("backend-dotnet", "Controllers", "EndpointMappings.cs");
        var start = source.IndexOf("private static async Task<IResult> MfaLoginVerify(", StringComparison.Ordinal);
        var end = source.IndexOf("private static async Task<IResult> SsoDiscover(", start, StringComparison.Ordinal);
        var completion = source[start..end];

        var totp = completion.IndexOf("TotpService.VerifyCode", StringComparison.Ordinal);
        var consume = completion.IndexOf("challengeConsumptions.TryConsumeAsync", StringComparison.Ordinal);
        var token = completion.IndexOf("var token =", StringComparison.Ordinal);
        var session = completion.IndexOf("INSERT INTO user_sessions", StringComparison.Ordinal);

        Assert.True(totp >= 0 && totp < consume, "challenge must only be consumed after a valid TOTP");
        Assert.True(consume < token, "challenge must be consumed before creating a session token");
        Assert.True(consume < session, "challenge must be consumed before persisting a session");
        Assert.Contains("mfa_challenge_consumed", completion, StringComparison.Ordinal);
        Assert.Contains("mfa_challenge_rejected", completion, StringComparison.Ordinal);
    }

    [Fact]
    public void MfaCompletion_FailsClosedOnLifecycleBeforeFactorOrChallengeConsumption()
    {
        var source = ReadSource("backend-dotnet", "Controllers", "EndpointMappings.cs");
        var start = source.IndexOf("private static async Task<IResult> MfaLoginVerify(", StringComparison.Ordinal);
        var end = source.IndexOf("private static async Task<IResult> SsoDiscover(", start, StringComparison.Ordinal);
        var completion = source[start..end];

        var lifecycle = completion.IndexOf("var userActive = IsActiveLifecycleStatus", StringComparison.Ordinal);
        var totp = completion.IndexOf("TotpService.VerifyCode", StringComparison.Ordinal);
        var consume = completion.IndexOf("challengeConsumptions.TryConsumeAsync", StringComparison.Ordinal);
        var session = completion.IndexOf("INSERT INTO user_sessions", StringComparison.Ordinal);

        Assert.True(lifecycle >= 0 && lifecycle < totp, "lifecycle must be checked before the factor");
        Assert.True(lifecycle < consume, "inactive identities must not consume the challenge");
        Assert.True(lifecycle < session, "inactive identities must not persist sessions");
        Assert.Contains("FOR SHARE OF u, c", completion, StringComparison.Ordinal);
        Assert.Contains("user.login.mfa_lifecycle_rejected", completion, StringComparison.Ordinal);
        Assert.Contains("Invalid or expired challenge", completion, StringComparison.Ordinal);
        Assert.DoesNotContain("challengeToken =", completion[(lifecycle + 1)..], StringComparison.Ordinal);
    }

    [Fact]
    public void LoginAndMfaCompletion_AreExplicitSystemTransactions()
    {
        var source = ReadSource("backend-dotnet", "Controllers", "EndpointMappings.cs");
        var mappings = source[..source.IndexOf("// General Ledger", StringComparison.Ordinal)];

        Assert.Equal(2, Count(mappings, "db.RunInSystemTransactionAsync("));
        Assert.Contains("() => Login(", mappings, StringComparison.Ordinal);
        Assert.Contains("() => MfaLoginVerify(", mappings, StringComparison.Ordinal);
    }

    [Fact]
    public void ChangePasswordSource_DoesNotAcceptLegacyPlaintextCredential()
    {
        var source = ReadSource("backend-dotnet", "Controllers", "EndpointMappings.cs");
        var start = source.IndexOf("private static async Task<IResult> ChangePassword(", StringComparison.Ordinal);
        var end = source.IndexOf("private static async Task<IResult> CommandCenterSummary", start, StringComparison.Ordinal);
        var changePassword = source[start..end];

        Assert.DoesNotContain("SELECT password_hash, demo_password", changePassword, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("user[\"demoPassword\"]", changePassword, StringComparison.Ordinal);
        Assert.Contains("VerifyPasswordHash(current", changePassword, StringComparison.Ordinal);
    }

    [Fact]
    public void AdminUserManagement_NeverStoresOrReturnsDemoPassword()
    {
        var source = ReadSource("backend-dotnet", "Controllers", "EndpointMappings.cs");

        Assert.DoesNotContain("@demoPassword", source, StringComparison.Ordinal);
        Assert.DoesNotContain("demo_password demoPassword", source, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("u.demo_password", source, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [MemberData(nameof(MalformedPasswordHashes))]
    public void PasswordVerification_FailsClosedOnMalformedOrWeakHashStructure(string storedHash)
    {
        var verify = typeof(EndpointMappings).GetMethod(
            "VerifyPasswordHash", BindingFlags.NonPublic | BindingFlags.Static);

        Assert.NotNull(verify);
        Assert.False((bool)verify!.Invoke(null, ["any-password", storedHash])!);
    }

    [Fact]
    public void PasswordVerification_AcceptsOnlyThePasswordForAValidAppHash()
    {
        var hash = typeof(EndpointMappings).GetMethod(
            "HashPassword", BindingFlags.NonPublic | BindingFlags.Static);
        var verify = typeof(EndpointMappings).GetMethod(
            "VerifyPasswordHash", BindingFlags.NonPublic | BindingFlags.Static);
        Assert.NotNull(hash);
        Assert.NotNull(verify);

        var stored = Assert.IsType<string>(hash!.Invoke(null, ["Correct-Rehearsal-Password!2026"]));
        Assert.True((bool)verify!.Invoke(null, ["Correct-Rehearsal-Password!2026", stored])!);
        Assert.False((bool)verify.Invoke(null, ["wrong-password", stored])!);
    }

    [Theory]
    [MemberData(nameof(MalformedPasswordHashes))]
    public void PlatformPasswordVerification_FailsClosedOnMalformedOrWeakHashStructure(string storedHash)
        => Assert.False(PlatformEndpoints.VerifyPassword("any-password", storedHash));

    [Fact]
    public void PlatformPasswordVerification_AcceptsOnlyThePasswordForAValidAppHash()
    {
        var stored = PlatformSchemaService.HashPassword("Correct-Platform-Password!2026");

        Assert.True(PlatformEndpoints.VerifyPassword("Correct-Platform-Password!2026", stored));
        Assert.False(PlatformEndpoints.VerifyPassword("wrong-password", stored));
    }

    public static IEnumerable<object[]> MalformedPasswordHashes()
    {
        var validSalt = Convert.ToBase64String(new byte[16]);
        var validSubkey = Convert.ToBase64String(new byte[32]);
        foreach (var value in new[]
        {
            $"PBKDF2$100000${validSalt}$",                       // empty subkey (historic fail-open)
            $"PBKDF2$100000$${validSubkey}",                    // empty salt
            $"PBKDF2$1${validSalt}${validSubkey}",              // CPU-cheap weak hash
            $"PBKDF2$2000001${validSalt}${validSubkey}",        // CPU-exhaustion iteration count
            $"PBKDF2$-1${validSalt}${validSubkey}",
            $"PBKDF2$not-a-number${validSalt}${validSubkey}",
            $"ARGON2$100000${validSalt}${validSubkey}",
            $"PBKDF2$100000$%%%${validSubkey}",
            $"PBKDF2$100000${validSalt}$%%%",
            $"PBKDF2$100000${Convert.ToBase64String(new byte[15])}${validSubkey}",
            $"PBKDF2$100000${Convert.ToBase64String(new byte[17])}${validSubkey}",
            $"PBKDF2$100000${validSalt}${Convert.ToBase64String(new byte[31])}",
            $"PBKDF2$100000${validSalt}${Convert.ToBase64String(new byte[33])}",
            $"PBKDF2$100000${validSalt}${validSubkey}$extra",
        })
            yield return [value];
    }

    private static SecuritySettings Settings(bool required, params string[] roles) => new()
    {
        CompanyId = 7,
        MfaRequired = required,
        MfaRequiredRoles = roles,
    };

    private static string ReadSource(params string[] parts)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !Directory.Exists(Path.Combine(dir.FullName, "backend-dotnet")))
            dir = dir.Parent;

        Assert.NotNull(dir);
        return File.ReadAllText(Path.Combine([dir!.FullName, .. parts]));
    }

    private static int Count(string source, string value)
    {
        var count = 0;
        for (var offset = 0; (offset = source.IndexOf(value, offset, StringComparison.Ordinal)) >= 0; offset += value.Length)
            count++;
        return count;
    }
}
