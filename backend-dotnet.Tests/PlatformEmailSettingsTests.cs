using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Opstrax.Api.Data;
using Opstrax.Api.Security;
using Opstrax.Api.Services;

namespace Opstrax.Tests;

// ─────────────────────────────────────────────────────────────────────────────
// Operator-editable SMTP configuration. These pin the behaviours the onboarding flow
// depends on: a value saved in the console beats the deployment environment, clearing
// it falls back to the environment rather than to nothing, and an SMTP credential is
// never written to the database unencrypted.
// ─────────────────────────────────────────────────────────────────────────────
[Collection("platform-control-plane")]
[Trait("Category", "Integration")]
public class PlatformEmailSettingsTests
{
    private static Database CreateDatabase()
        => new(new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:DefaultConnection"] = TestDb.ConnectionString,
            })
            .Build());

    // A provider with a real 32-byte key encrypts; one with no key passes plaintext through
    // (the dev convenience the fail-closed guard exists to compensate for).
    private static PiiProtectionService Pii(bool withKey)
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Pii:DataKey"] = withKey ? Convert.ToBase64String(new byte[32]) : null,
            })
            .Build();
        return new PiiProtectionService(new EnvDataKeyProvider(config), NullLogger<PiiProtectionService>.Instance);
    }

    private static async Task<PlatformSettingsService> ServiceAsync(bool withKey = true)
    {
        var service = new PlatformSettingsService(CreateDatabase(), Pii(withKey));
        await service.EnsureSchemaAsync();
        return service;
    }

    private static string UniqueKey() => $"test.{Guid.NewGuid():N}";

    private sealed class EnvScope(string name, string? value) : IDisposable
    {
        private readonly string? _previous = Environment.GetEnvironmentVariable(name);
        private readonly string _name = name;
        private bool _set = SetAndReturn(name, value);
        private static bool SetAndReturn(string n, string? v) { Environment.SetEnvironmentVariable(n, v); return true; }
        public void Dispose() { if (_set) Environment.SetEnvironmentVariable(_name, _previous); }
    }

    [Fact]
    public async Task Falls_Back_To_The_Environment_When_Nothing_Is_Stored()
    {
        var settings = await ServiceAsync();
        var envName = $"OPSTRAX_TEST_{Guid.NewGuid():N}"[..24].ToUpperInvariant();
        using var _ = new EnvScope(envName, "smtp.from-env.example");

        Assert.Equal("smtp.from-env.example", await settings.GetAsync(UniqueKey(), envName));
    }

    [Fact]
    public async Task A_Stored_Value_Overrides_The_Environment()
    {
        var settings = await ServiceAsync();
        var key = UniqueKey();
        var envName = $"OPSTRAX_TEST_{Guid.NewGuid():N}"[..24].ToUpperInvariant();
        using var _ = new EnvScope(envName, "smtp.from-env.example");

        try
        {
            await settings.SetAsync(key, "smtp.from-console.example", isSecret: false, "ops@opstrax.test");
            Assert.Equal("smtp.from-console.example", await settings.GetAsync(key, envName));
            Assert.True(await settings.HasStoredAsync(key));
        }
        finally { await settings.SetAsync(key, null, false, null); }
    }

    // Clearing a console value must REVEAL the environment default again, not leave the
    // platform with no configuration at all.
    [Fact]
    public async Task Clearing_A_Stored_Value_Reverts_To_The_Environment()
    {
        var settings = await ServiceAsync();
        var key = UniqueKey();
        var envName = $"OPSTRAX_TEST_{Guid.NewGuid():N}"[..24].ToUpperInvariant();
        using var _ = new EnvScope(envName, "smtp.from-env.example");

        await settings.SetAsync(key, "smtp.from-console.example", isSecret: false, "ops@opstrax.test");
        await settings.SetAsync(key, "", isSecret: false, "ops@opstrax.test");

        Assert.False(await settings.HasStoredAsync(key));
        Assert.Equal("smtp.from-env.example", await settings.GetAsync(key, envName));
    }

    [Fact]
    public async Task A_Secret_Is_Encrypted_At_Rest_And_Reads_Back_Intact()
    {
        var settings = await ServiceAsync(withKey: true);
        var key = UniqueKey();
        var db = CreateDatabase();

        try
        {
            await settings.SetAsync(key, "super-secret-smtp-pw", isSecret: true, "ops@opstrax.test");

            var stored = (await db.QuerySingleAsync(
                "SELECT setting_value FROM platform_settings WHERE setting_key=@k",
                c => c.Parameters.AddWithValue("@k", key)))?["settingValue"]?.ToString();

            Assert.NotNull(stored);
            Assert.StartsWith("enc:", stored);
            Assert.DoesNotContain("super-secret-smtp-pw", stored);
            Assert.Equal("super-secret-smtp-pw", await settings.GetAsync(key));
        }
        finally { await settings.SetAsync(key, null, false, null); }
    }

    // The fail-closed guard: with no data key configured PiiProtectionService would pass the
    // credential through in plaintext, so storing it is refused outright.
    [Fact]
    public async Task Refuses_To_Store_A_Secret_When_No_Encryption_Key_Is_Configured()
    {
        var settings = await ServiceAsync(withKey: false);

        Assert.False(settings.EncryptionAvailable);
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => settings.SetAsync(UniqueKey(), "super-secret-smtp-pw", isSecret: true, "ops@opstrax.test"));
    }

    // Clearing a secret must still work without a key — it writes nothing.
    [Fact]
    public async Task Clearing_A_Secret_Is_Allowed_Without_An_Encryption_Key()
    {
        var settings = await ServiceAsync(withKey: false);
        await settings.SetAsync(UniqueKey(), null, isSecret: true, "ops@opstrax.test");
    }

    // ── Mail configuration resolution ───────────────────────────────────────────

    private static PlatformMailService Mail(PlatformSettingsService settings)
        => new(settings, NullLogger<PlatformMailService>.Instance);

    // platform_settings is shared state, so a mail test must start from a known-empty
    // configuration — an explicit enable_ssl left behind by another test (or by an operator)
    // legitimately overrides the port-derived default and would mask what is under test.
    private static readonly string[] SmtpKeys =
    [
        PlatformMailService.HostKey, PlatformMailService.PortKey, PlatformMailService.UserKey,
        PlatformMailService.FromKey, PlatformMailService.FromNameKey, PlatformMailService.SslKey,
    ];

    private static async Task ClearSmtpAsync(PlatformSettingsService settings)
    {
        foreach (var key in SmtpKeys) await settings.SetAsync(key, null, false, null);
    }

    [Fact]
    public async Task Mail_Is_Not_Usable_Without_Both_A_Host_And_A_From_Address()
    {
        var settings = await ServiceAsync();
        await ClearSmtpAsync(settings);
        try
        {
            await settings.SetAsync(PlatformMailService.HostKey, "smtp.example.com", false, null);
            await settings.SetAsync(PlatformMailService.FromKey, null, false, null);
            using var noEnvHost = new EnvScope("SMTP_HOST", null);
            using var noEnvFrom = new EnvScope("SMTP_FROM", null);

            Assert.False((await Mail(settings).ResolveAsync()).IsUsable);
        }
        finally { await ClearSmtpAsync(settings); }
    }

    [Fact]
    public async Task Mail_Resolves_Console_Values_And_Defaults_Tls_On_For_587()
    {
        var settings = await ServiceAsync();
        await ClearSmtpAsync(settings);
        try
        {
            await settings.SetAsync(PlatformMailService.HostKey, "smtp.example.com", false, null);
            await settings.SetAsync(PlatformMailService.FromKey, "no-reply@example.com", false, null);
            await settings.SetAsync(PlatformMailService.PortKey, "587", false, null);

            var config = await Mail(settings).ResolveAsync();
            Assert.True(config.IsUsable);
            Assert.Equal("smtp.example.com", config.Host);
            Assert.Equal(587, config.Port);
            Assert.True(config.EnableSsl);
        }
        finally { await ClearSmtpAsync(settings); }
    }

    // Port 465 is implicit TLS: SmtpClient must NOT try to STARTTLS, so the default flips.
    [Fact]
    public async Task Mail_Defaults_Tls_Off_For_Implicit_Tls_Port_465()
    {
        var settings = await ServiceAsync();
        await ClearSmtpAsync(settings);
        try
        {
            await settings.SetAsync(PlatformMailService.HostKey, "smtp.example.com", false, null);
            await settings.SetAsync(PlatformMailService.FromKey, "no-reply@example.com", false, null);
            await settings.SetAsync(PlatformMailService.PortKey, "465", false, null);

            Assert.False((await Mail(settings).ResolveAsync()).EnableSsl);
        }
        finally { await ClearSmtpAsync(settings); }
    }

    // Unconfigured mail reports the reason instead of throwing — callers fall back to the link.
    [Fact]
    public async Task Sending_With_No_Configuration_Fails_Softly_With_A_Reason()
    {
        var settings = await ServiceAsync();
        using var noEnvHost = new EnvScope("SMTP_HOST", null);
        using var noEnvFrom = new EnvScope("SMTP_FROM", null);
        await ClearSmtpAsync(settings);

        var (sent, error) = await Mail(settings).SendAsync("someone@example.com", "s", "b");
        Assert.False(sent);
        Assert.Contains("not configured", error);
    }

    // ── Test-before-save: when may the stored secret be replayed? ────────────────
    // The pre-save test falls back to the stored password only when the target server
    // AND username are unchanged — otherwise anyone with settings access could point
    // "Host" at a server they control and harvest the stored credential.
    [Fact]
    public void Stored_Secret_Reusable_When_Host_And_User_Unchanged()
        => Assert.True(Opstrax.Api.Controllers.PlatformSettingsEndpoints.MayReuseStoredSecret(
            "smtp.example.com", "info@k.com", "smtp.example.com", "info@k.com"));

    [Fact]
    public void Stored_Secret_Reuse_Is_Case_And_Whitespace_Insensitive()
        => Assert.True(Opstrax.Api.Controllers.PlatformSettingsEndpoints.MayReuseStoredSecret(
            "SMTP.Example.com", "Info@K.com", "  smtp.example.com ", "info@k.com "));

    [Theory]
    [InlineData("smtp.evil.example", "info@k.com")]   // different host = credential exfil vector
    [InlineData("smtp.example.com", "other@k.com")]   // different account on the same host
    public void Stored_Secret_Not_Reusable_When_Target_Changes(string reqHost, string reqUser)
        => Assert.False(Opstrax.Api.Controllers.PlatformSettingsEndpoints.MayReuseStoredSecret(
            "smtp.example.com", "info@k.com", reqHost, reqUser));

    [Fact]
    public void Stored_Secret_Not_Reusable_When_Nothing_Is_Stored()
        => Assert.False(Opstrax.Api.Controllers.PlatformSettingsEndpoints.MayReuseStoredSecret(
            null, null, "smtp.example.com", "info@k.com"));
}
