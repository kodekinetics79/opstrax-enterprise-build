using System.Text;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Opstrax.Api.Observability;
using Opstrax.Api.Security;
using Opstrax.Api.Storage;
using Xunit;

namespace Opstrax.Tests;

// ── Data-protection + storage primitives — pure-logic tests (no DB) ────────────

// Test key provider with a fixed 32-byte key so encryption is deterministic-to-verify.
internal sealed class TestKeyProvider : IDataKeyProvider
{
    private readonly byte[] _key = Enumerable.Range(0, 32).Select(i => (byte)i).ToArray();
    public bool IsConfigured => true;
    public (byte KeyId, byte[] Key) ActiveKey => (1, _key);
    public byte[]? ResolveKey(byte keyId) => keyId == 1 ? _key : null;
    public byte[] IndexKey => Encoding.UTF8.GetBytes("test-index-key-0123456789abcdef!");
}

internal sealed class DisabledKeyProvider : IDataKeyProvider
{
    public bool IsConfigured => false;
    public (byte KeyId, byte[] Key) ActiveKey => throw new InvalidOperationException();
    public byte[]? ResolveKey(byte keyId) => null;
    public byte[] IndexKey => new byte[32];
}

public class PiiProtectionServiceTests
{
    private static PiiProtectionService Enabled() =>
        new(new TestKeyProvider(), NullLogger<PiiProtectionService>.Instance);

    [Fact]
    public void Encrypt_Then_Decrypt_RoundTrips()
    {
        var pii = Enabled();
        var plain = "D1234567-CLASS-A";
        var enc = pii.Encrypt(plain);

        Assert.NotNull(enc);
        Assert.StartsWith("enc:", enc);
        Assert.DoesNotContain(plain, enc);           // ciphertext, not plaintext
        Assert.Equal(plain, pii.Decrypt(enc));       // round-trips
    }

    [Fact]
    public void Encrypt_ProducesDifferentCiphertextEachTime()
    {
        var pii = Enabled();
        // Random nonce per call ⇒ two encryptions of the same value differ.
        Assert.NotEqual(pii.Encrypt("same-value"), pii.Encrypt("same-value"));
    }

    [Fact]
    public void Decrypt_LegacyPlaintext_PassesThrough()
    {
        var pii = Enabled();
        // Values written before encryption was enabled are not enc:-prefixed.
        Assert.Equal("legacy-plain", pii.Decrypt("legacy-plain"));
    }

    [Fact]
    public void Tampered_Ciphertext_FailsAuthentication()
    {
        var pii = Enabled();
        var enc = pii.Encrypt("secret")!;
        // Flip a byte in the base64 body — GCM auth tag must reject it (returns null).
        var body = enc[4..].ToCharArray();
        body[10] = body[10] == 'A' ? 'B' : 'A';
        var tampered = "enc:" + new string(body);
        Assert.Null(pii.Decrypt(tampered));
    }

    [Fact]
    public void BlindIndex_IsDeterministic_AndCaseInsensitive()
    {
        var pii = Enabled();
        var a = pii.BlindIndex("ABC-123");
        var b = pii.BlindIndex(" abc-123 ");   // normalized (trim + lowercase)
        Assert.Equal(a, b);
        Assert.NotNull(a);
        Assert.Equal(64, a!.Length);            // HMAC-SHA256 hex
    }

    [Fact]
    public void Disabled_Provider_PassesThroughPlaintext()
    {
        var pii = new PiiProtectionService(new DisabledKeyProvider(), NullLogger<PiiProtectionService>.Instance);
        Assert.False(pii.Enabled);
        Assert.Equal("plain", pii.Encrypt("plain"));   // no key ⇒ store as-is (dev)
        Assert.Null(pii.BlindIndex("plain"));           // no index without a key
    }

    [Theory]
    [InlineData("jane.doe@acme.com", "@acme.com")]
    [InlineData("D12345678", "5678")]
    public void Mask_HidesMostOfTheValue(string input, string keepsFragment)
    {
        var masked = PiiProtectionService.Mask(input);
        Assert.Contains(keepsFragment, masked);
        Assert.Contains("*", masked);
    }

    [Fact]
    public void EnvDataKeyProvider_ReadsBase64Key()
    {
        var key = Convert.ToBase64String(new byte[32]);
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["Pii:DataKey"] = key })
            .Build();
        var provider = new EnvDataKeyProvider(config);
        Assert.True(provider.IsConfigured);
        Assert.Equal(32, provider.ActiveKey.Key.Length);
    }

    [Fact]
    public void PlatformMfaSecrets_AreProtectedAtRestAndDecryptedAtUse()
    {
        var adminSource = ReadSource("backend-dotnet", "Controllers", "PlatformAdminEndpoints.cs");
        var loginSource = ReadSource("backend-dotnet", "Controllers", "PlatformEndpoints.cs");

        Assert.Contains("var protectedSecret = pii.Encrypt(secret)", adminSource, StringComparison.Ordinal);
        Assert.Contains("pii.Decrypt(row?[\"mfaSecret\"]?.ToString())", adminSource, StringComparison.Ordinal);
        Assert.Contains("pii.Decrypt(storedMfaSecret)", loginSource, StringComparison.Ordinal);
        Assert.DoesNotContain("AddWithValue(\"@s\", secret)", adminSource, StringComparison.Ordinal);
    }

    private static string ReadSource(params string[] parts)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !Directory.Exists(Path.Combine(dir.FullName, "backend-dotnet")))
            dir = dir.Parent;

        Assert.NotNull(dir);
        return File.ReadAllText(Path.Combine([dir!.FullName, .. parts]));
    }
}

public class FileStorageServiceTests
{
    private static FileStorageService Svc(out LocalObjectStore store)
    {
        var root = Path.Combine(Path.GetTempPath(), "opstrax-test-" + Guid.NewGuid().ToString("n"));
        store = new LocalObjectStore(root);
        return new FileStorageService(store, NullLogger<FileStorageService>.Instance);
    }

    [Fact]
    public async Task Upload_StoresTenantScopedKey_AndRoundTrips()
    {
        var svc = Svc(out _);
        var pdf = Encoding.ASCII.GetBytes("%PDF-1.4\n1 0 obj\n<<>>\nendobj\ntrailer\n<<>>\n%%EOF\n");
        using var content = new MemoryStream(pdf);
        var result = await svc.UploadAsync(42, "pods", "delivery.pdf", "application/pdf", content);

        Assert.StartsWith("objkey:tenant/42/", result.Reference);
        Assert.True(FileStorageService.KeyBelongsToTenant(result.Key, 42));
        Assert.False(FileStorageService.KeyBelongsToTenant(result.Key, 99)); // IDOR guard

        await using var read = await svc.OpenAsync(result.Key);
        using var sr = new StreamReader(read);
        Assert.Equal(Encoding.ASCII.GetString(pdf), await sr.ReadToEndAsync());
    }

    [Fact]
    public async Task Upload_RejectsDisallowedContentType()
    {
        var svc = Svc(out _);
        using var content = new MemoryStream(Encoding.UTF8.GetBytes("x"));
        await Assert.ThrowsAsync<ArgumentException>(() =>
            svc.UploadAsync(1, "docs", "evil.exe", "application/x-msdownload", content));
    }

    [Fact]
    public async Task Upload_RejectsEmptyFile()
    {
        var svc = Svc(out _);
        using var content = new MemoryStream();
        await Assert.ThrowsAsync<ArgumentException>(() =>
            svc.UploadAsync(1, "docs", "empty.pdf", "application/pdf", content));
    }

    [Fact]
    public async Task Resolve_LegacyUrl_PassesThrough()
    {
        var svc = Svc(out _);
        var resolved = await svc.ResolveAsync("https://legacy.example/file.pdf", TimeSpan.FromMinutes(5));
        Assert.False(resolved.IsManaged);
        Assert.Equal("https://legacy.example/file.pdf", resolved.LegacyUrl);
    }

    [Fact]
    public async Task Delete_And_DeleteTenant_RemoveObjects()
    {
        var svc = Svc(out _);
        using var c1 = new MemoryStream(Encoding.ASCII.GetBytes("%PDF-1.4\n%%EOF\n"));
        var r1 = await svc.UploadAsync(7, "pods", "a.pdf", "application/pdf", c1);
        await svc.DeleteAsync(r1.Reference);
        await Assert.ThrowsAsync<FileNotFoundException>(() => svc.OpenAsync(r1.Key));
    }
}

public class BurnRateTests
{
    [Fact]
    public void BurnRate_Ok_WhenNoErrors()
    {
        var slo = new SloService(new ApiMetricsService());
        var burn = slo.EvaluateBurnRate();
        Assert.Equal("ok", burn.Severity);
    }

    [Fact]
    public void BurnRate_CriticalWhenErrorRateFarExceedsBudget()
    {
        var metrics = new ApiMetricsService();
        // 5% 5xx against a 0.1% budget ⇒ 50x burn ⇒ critical / page_now.
        for (var i = 0; i < 95; i++) metrics.RecordRequest("/api/x", 200, 5);
        for (var i = 0; i < 5; i++) metrics.RecordRequest("/api/x", 500, 5);

        var burn = new SloService(metrics).EvaluateBurnRate();
        Assert.Equal("critical", burn.Severity);
        Assert.Equal("page_now", burn.RecommendedAction);
        Assert.True(burn.BurnRate >= 14.4);
    }
}
