using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Opstrax.Api.Security;
using Opstrax.Api.Services;

namespace Opstrax.Tests;

public sealed class DeviceHmacSecretProtectionTests
{
    private static readonly PiiProtectionService Protection =
        new(new TestKeyProvider(), NullLogger<PiiProtectionService>.Instance);

    [Fact]
    public void NewSecret_IsStoredOnlyAsAuthenticatedEncryptionEnvelope()
    {
        var secret = new string('s', 48);
        var encrypted = DeviceHmacSecretProtection.EncryptForStorage(Protection, secret);

        Assert.StartsWith("enc:", encrypted, StringComparison.Ordinal);
        Assert.DoesNotContain(secret, encrypted, StringComparison.Ordinal);
        Assert.Equal(secret, Protection.Decrypt(encrypted));
    }

    [Fact]
    public void LegacyPlaintextRead_IsNeverAllowedInProduction()
    {
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            [DeviceHmacSecretProtection.LegacyReadSetting] = "true",
        }).Build();

        Assert.False(DeviceHmacSecretProtection.LegacyReadAllowed(new Environment("Production"), config));
        Assert.True(DeviceHmacSecretProtection.LegacyReadAllowed(new Environment("Development"), config));
    }

    [Fact]
    public void PreviousCredential_IsAcceptedOnlyInsideBoundedGraceWindow()
    {
        var current = DeviceHmacSecretProtection.EncryptForStorage(Protection, new string('c', 40));
        var previous = DeviceHmacSecretProtection.EncryptForStorage(Protection, new string('p', 40));
        var now = DateTimeOffset.UtcNow;

        using var inGrace = DeviceHmacSecretProtection.ResolveForVerification(
            Protection, current, previous, now.AddMinutes(1), null, false, now, NullLogger.Instance);
        using var expired = DeviceHmacSecretProtection.ResolveForVerification(
            Protection, current, previous, now.AddSeconds(-1), null, false, now, NullLogger.Instance);

        Assert.NotNull(inGrace?.Previous);
        Assert.Null(expired?.Previous);
    }

    private sealed class Environment(string name) : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = name;
        public string ApplicationName { get; set; } = "tests";
        public string ContentRootPath { get; set; } = Directory.GetCurrentDirectory();
        public Microsoft.Extensions.FileProviders.IFileProvider ContentRootFileProvider { get; set; } =
            new Microsoft.Extensions.FileProviders.NullFileProvider();
    }
}
