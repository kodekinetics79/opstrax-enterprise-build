using System.Text.Json;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Opstrax.Api.Observability;
using Opstrax.Api.Security;
using Opstrax.Api.Services.Connectors;

namespace Opstrax.Tests;

public sealed class ConnectorSecretHardeningTests
{
    [Theory]
    [InlineData("apiToken")]
    [InlineData("APITOKEN")]
    [InlineData("apiKey")]
    [InlineData("refreshToken")]
    [InlineData("api_token")]
    [InlineData("client-secret")]
    [InlineData("access token")]
    [InlineData("webhook_secret")]
    [InlineData("hmac-secret")]
    [InlineData("private_key")]
    [InlineData("credential")]
    [InlineData("credentials")]
    [InlineData("tokens")]
    [InlineData("apiKeys")]
    [InlineData("bearerToken")]
    [InlineData("consumer_secret")]
    [InlineData("secretKey")]
    [InlineData("signing-key")]
    [InlineData("signingSecret")]
    [InlineData("connectionString")]
    [InlineData("passphrase")]
    [InlineData("authorization")]
    public void SensitiveRegistry_RecognizesProviderCredentialAliases(string key)
        => Assert.True(ConnectorRegistry.IsSensitive(key));

    [Theory]
    [InlineData("authHeader")]
    [InlineData("authScheme")]
    [InlineData("authenticationMode")]
    public void SensitiveRegistry_DoesNotEncryptNonSecretAuthMetadata(string key)
        => Assert.False(ConnectorRegistry.IsSensitive(key));

    [Fact]
    public void ProtectedEnvironment_RecursivelyEncryptsDecryptsAndRedactsSecrets()
    {
        var registry = Registry(new TestKeyProvider(), Environments.Staging);
        using var input = JsonDocument.Parse(
            """
            {
              "apiToken":"samsara-secret",
              "region":"us",
              "credentials":{"password":"nested-secret","account":"fleet"},
              "fallbacks":[{"apiKey":"fallback-secret"}]
            }
            """);

        var stored = registry.EncryptConfigForStorage(input.RootElement);
        Assert.DoesNotContain("samsara-secret", stored, StringComparison.Ordinal);
        Assert.DoesNotContain("nested-secret", stored, StringComparison.Ordinal);
        Assert.DoesNotContain("fallback-secret", stored, StringComparison.Ordinal);

        using var storedDoc = JsonDocument.Parse(stored);
        Assert.StartsWith("enc:", storedDoc.RootElement.GetProperty("apiToken").GetString(), StringComparison.Ordinal);
        Assert.StartsWith("enc:", storedDoc.RootElement.GetProperty("credentials").GetProperty("password").GetString(), StringComparison.Ordinal);
        Assert.StartsWith("enc:", storedDoc.RootElement.GetProperty("fallbacks")[0].GetProperty("apiKey").GetString(), StringComparison.Ordinal);

        var decrypted = registry.DecryptConfig(stored);
        Assert.Equal("samsara-secret", decrypted["apiToken"]);
        using var nested = JsonDocument.Parse(decrypted["credentials"]!);
        Assert.Equal("nested-secret", nested.RootElement.GetProperty("password").GetString());
        Assert.Equal("fleet", nested.RootElement.GetProperty("account").GetString());

        var redacted = ConnectorRegistry.RedactConfig(stored);
        Assert.Equal("••••••••", redacted["apiToken"]);
        var credentials = Assert.IsType<Dictionary<string, object?>>(redacted["credentials"]);
        Assert.Equal("••••••••", credentials["password"]);
        Assert.Equal("••••••••", credentials["account"]);
    }

    [Fact]
    public void SecretContainers_ProtectScalarArrayAndStructuredLeavesWithoutMaskingAuthMetadata()
    {
        var registry = Registry(new TestKeyProvider(), Environments.Staging);
        using var input = JsonDocument.Parse(
            """
            {
              "credentials":"credential-value",
              "tokens":["primary-token",{"fallback":"secondary-token"}],
              "apiKeys":{"north":"north-key"},
              "authHeader":"X-Provider-Authorization",
              "authScheme":"Bearer"
            }
            """);

        var stored = registry.EncryptConfigForStorage(input.RootElement);
        Assert.DoesNotContain("credential-value", stored, StringComparison.Ordinal);
        Assert.DoesNotContain("primary-token", stored, StringComparison.Ordinal);
        Assert.DoesNotContain("secondary-token", stored, StringComparison.Ordinal);
        Assert.DoesNotContain("north-key", stored, StringComparison.Ordinal);
        Assert.Contains("X-Provider-Authorization", stored, StringComparison.Ordinal);

        using var storedJson = JsonDocument.Parse(stored);
        Assert.StartsWith("enc:", storedJson.RootElement.GetProperty("credentials").GetString(), StringComparison.Ordinal);
        Assert.StartsWith("enc:", storedJson.RootElement.GetProperty("tokens")[0].GetString(), StringComparison.Ordinal);
        Assert.StartsWith("enc:", storedJson.RootElement.GetProperty("tokens")[1].GetProperty("fallback").GetString(), StringComparison.Ordinal);
        Assert.StartsWith("enc:", storedJson.RootElement.GetProperty("apiKeys").GetProperty("north").GetString(), StringComparison.Ordinal);

        var decrypted = registry.DecryptConfig(stored);
        Assert.Equal("credential-value", decrypted["credentials"]);
        Assert.Equal("X-Provider-Authorization", decrypted["authHeader"]);
        Assert.Equal("Bearer", decrypted["authScheme"]);
        using var tokens = JsonDocument.Parse(decrypted["tokens"]!);
        Assert.Equal("primary-token", tokens.RootElement[0].GetString());
        Assert.Equal("secondary-token", tokens.RootElement[1].GetProperty("fallback").GetString());

        var redacted = ConnectorRegistry.RedactConfig(stored);
        Assert.Equal("••••••••", redacted["credentials"]);
        var redactedTokens = Assert.IsType<List<object?>>(redacted["tokens"]);
        Assert.Equal("••••••••", redactedTokens[0]);
        var redactedFallback = Assert.IsType<Dictionary<string, object?>>(redactedTokens[1]);
        Assert.Equal("••••••••", redactedFallback["fallback"]);
        Assert.Equal("X-Provider-Authorization", redacted["authHeader"]);
        Assert.Equal("Bearer", redacted["authScheme"]);
    }

    [Theory]
    [InlineData(Environments.Staging)]
    [InlineData(Environments.Production)]
    public void ProtectedEnvironment_RejectsMissingEncryptionAndLegacyPlaintext(string environment)
    {
        var registry = Registry(new DisabledKeyProvider(), environment);
        using var input = JsonDocument.Parse("{\"apiToken\":\"plaintext\"}");

        Assert.Throws<ConnectorSecretProtectionException>(() => registry.EncryptConfigForStorage(input.RootElement));
        Assert.Throws<ConnectorSecretProtectionException>(() => registry.DecryptConfig(input.RootElement.GetRawText()));
    }

    [Fact]
    public void LogRedactor_ScrubsSamsaraAndNestedJsonSecretValues()
    {
        const string token = "samsara-live-token-value";
        const string nested = "nested-password-value";
        const string privateKey = "private-key-value";
        const string accessToken = "oauth-access-value";
        var scrubbed = LogRedactor.Scrub(
            $"connector={{\"apiToken\":\"{token}\",\"credentials\":{{\"password\":\"{nested}\",\"private_key\":\"{privateKey}\",\"access-token\":\"{accessToken}\"}}}}");

        Assert.DoesNotContain(token, scrubbed, StringComparison.Ordinal);
        Assert.DoesNotContain(nested, scrubbed, StringComparison.Ordinal);
        Assert.DoesNotContain(privateKey, scrubbed, StringComparison.Ordinal);
        Assert.DoesNotContain(accessToken, scrubbed, StringComparison.Ordinal);
        Assert.Contains("***REDACTED***", scrubbed, StringComparison.Ordinal);
    }

    [Fact]
    public void LogRedactor_ScrubsSensitiveScalarArrayAndObjectContainers()
    {
        var scrubbed = LogRedactor.Scrub(
            """connector={"tokens":["array-secret",{"opaque":"nested-secret"}],"apiKeys":{"primary":"object-secret"},"credentials":"scalar-secret","authHeader":"X-Custom"}""");

        Assert.DoesNotContain("array-secret", scrubbed, StringComparison.Ordinal);
        Assert.DoesNotContain("nested-secret", scrubbed, StringComparison.Ordinal);
        Assert.DoesNotContain("object-secret", scrubbed, StringComparison.Ordinal);
        Assert.DoesNotContain("scalar-secret", scrubbed, StringComparison.Ordinal);
        Assert.Contains("X-Custom", scrubbed, StringComparison.Ordinal);
        Assert.Contains("***REDACTED***", scrubbed, StringComparison.Ordinal);
    }

    [Fact]
    public void CustomConnectorUpdate_PreservesMaskedAndOmittedSecretsWhileEncryptingChanges()
    {
        var registry = Registry(new TestKeyProvider(), Environments.Staging);
        using var original = JsonDocument.Parse(
            """{"api_token":"first-secret","credentials":{"client-secret":"nested-secret","region":"us"},"name":"before"}""");
        var stored = registry.EncryptConfigForStorage(original.RootElement);
        using var patch = JsonDocument.Parse(
            """{"api_token":"••••••••","credentials":{"client-secret":"••••••••","region":"eu"},"name":"after"}""");

        var merged = registry.MergeConfigForStorage(patch.RootElement, stored);
        Assert.DoesNotContain("first-secret", merged, StringComparison.Ordinal);
        Assert.DoesNotContain("nested-secret", merged, StringComparison.Ordinal);
        Assert.DoesNotContain("••••••••", merged, StringComparison.Ordinal);

        var decrypted = registry.DecryptConfig(merged);
        Assert.Equal("first-secret", decrypted["api_token"]);
        Assert.Equal("after", decrypted["name"]);
        using var credentials = JsonDocument.Parse(decrypted["credentials"]!);
        Assert.Equal("nested-secret", credentials.RootElement.GetProperty("client-secret").GetString());
        Assert.Equal("eu", credentials.RootElement.GetProperty("region").GetString());
    }

    [Fact]
    public void CustomConnectorUpdate_EncryptsWholeStructuredValueUnderSensitiveKey()
    {
        var registry = Registry(new TestKeyProvider(), Environments.Staging);
        using var patch = JsonDocument.Parse("""{"api_token":{"value":"structured-secret"}}""");

        var merged = registry.MergeConfigForStorage(patch.RootElement, "{}");

        Assert.DoesNotContain("structured-secret", merged, StringComparison.Ordinal);
        using var stored = JsonDocument.Parse(merged);
        Assert.StartsWith("enc:", stored.RootElement.GetProperty("api_token").GetString(), StringComparison.Ordinal);
        Assert.Contains("structured-secret", registry.DecryptConfig(merged)["api_token"], StringComparison.Ordinal);
    }

    private static ConnectorRegistry Registry(IDataKeyProvider keys, string environment)
    {
        var pii = new PiiProtectionService(keys, NullLogger<PiiProtectionService>.Instance);
        var fallback = new GenericHttpConnector(new NeverHttpClientFactory(), NullLogger<GenericHttpConnector>.Instance);
        return new ConnectorRegistry([], fallback, pii, new TestEnvironment(environment));
    }

    private sealed class NeverHttpClientFactory : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => throw new InvalidOperationException("No network expected in this test.");
    }

    private sealed class TestEnvironment(string name) : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = name;
        public string ApplicationName { get; set; } = "tests";
        public string ContentRootPath { get; set; } = Directory.GetCurrentDirectory();
        public Microsoft.Extensions.FileProviders.IFileProvider ContentRootFileProvider { get; set; } =
            new Microsoft.Extensions.FileProviders.NullFileProvider();
    }
}
