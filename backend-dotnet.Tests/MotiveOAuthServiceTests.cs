using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using System.Net;
using System.Text;
using Opstrax.Api.Services.Connectors;

namespace Opstrax.Tests;

public sealed class MotiveOAuthServiceTests
{
    [Fact]
    public void AuthorizationUrl_UsesProtectedStateAndReadOnlyScopesWithoutLeakingClientSecret()
    {
        var service = Service();
        Assert.True(service.TryGetSettings(out var settings, out var error), error);
        var state = service.CreateState(7, 11, 13, 4);

        var url = service.BuildAuthorizationUrl(settings!, state.State);

        Assert.Contains("https://gomotive.com/oauth/authorize?", url, StringComparison.Ordinal);
        Assert.Contains("response_type=code", url, StringComparison.Ordinal);
        Assert.Contains(Uri.EscapeDataString("companies.read users.read vehicles.read"), url, StringComparison.Ordinal);
        Assert.Contains(Uri.EscapeDataString(state.State), url, StringComparison.Ordinal);
        Assert.DoesNotContain("test-client-secret", url, StringComparison.Ordinal);
        Assert.True(service.TryReadState(state.State, out var roundTrip));
        Assert.Equal(7, roundTrip!.CompanyId);
        Assert.Equal(11, roundTrip.IntegrationId);
        Assert.Equal(4, roundTrip.OperationGeneration);
        Assert.False(service.TryReadState(state.State + "tampered", out _));
    }

    [Fact]
    public void ProtectedEnvironment_RejectsNonHttpsFrontendAndNonApiCallback()
    {
        var badFrontend = Service(new Dictionary<string, string?>
        {
            ["PUBLIC_APP_URL"] = "http://frontend.example.test",
        });
        var badCallback = Service(new Dictionary<string, string?>
        {
            ["Motive:RedirectUri"] = "https://api.example.test/not-the-callback",
        });

        Assert.False(badFrontend.TryGetSettings(out _, out var frontendError));
        Assert.Contains("PUBLIC_APP_URL", frontendError, StringComparison.Ordinal);
        Assert.False(badCallback.TryGetSettings(out _, out var callbackError));
        Assert.Contains(MotiveOAuthService.CallbackPath, callbackError, StringComparison.Ordinal);
    }

    [Fact]
    public void BrowserBinding_RequiresMatchingNonceAndSecureHostOnlyCookie()
    {
        var state = Service().CreateState(1, 2, 3, 4);
        Assert.True(MotiveOAuthService.MatchesBrowserNonce(state.Payload.Nonce, state.Payload));
        Assert.False(MotiveOAuthService.MatchesBrowserNonce(null, state.Payload));
        Assert.False(MotiveOAuthService.MatchesBrowserNonce("wrong-browser", state.Payload));
        var options = MotiveOAuthService.FlowCookieOptions();
        Assert.True(options.HttpOnly);
        Assert.True(options.Secure);
        Assert.Equal("/", options.Path);
        Assert.Null(options.Domain);
        Assert.Equal(Microsoft.AspNetCore.Http.SameSiteMode.None, options.SameSite);
        Assert.Equal(TimeSpan.FromMinutes(10), options.MaxAge);
    }

    [Fact]
    public void SourceContract_CallbackIsPreSessionAndSecretsAreServerSideEncrypted()
    {
        var root = RepositoryRoot();
        var program = File.ReadAllText(Path.Combine(root, "backend-dotnet", "Program.cs"));
        var endpoints = File.ReadAllText(Path.Combine(root, "backend-dotnet", "Controllers", "EndpointMappings.cs"));
        var registry = File.ReadAllText(Path.Combine(root, "backend-dotnet", "Services", "Connectors", "ConnectorRegistry.cs"));

        Assert.Contains("/api/integrations/motive/oauth/callback", program, StringComparison.Ordinal);
        Assert.Contains("oauthStateHash", endpoints, StringComparison.Ordinal);
        Assert.Contains("CryptographicOperations.FixedTimeEquals", endpoints, StringComparison.Ordinal);
        Assert.Contains("accessToken = verified ? exchange.Tokens.AccessToken : null", endpoints, StringComparison.Ordinal);
        Assert.Contains("refreshToken = (string?)null", endpoints, StringComparison.Ordinal);
        Assert.Contains("RunInSystemTransactionAsync", endpoints, StringComparison.Ordinal);
        Assert.Contains("integration.oauth.callback.claimed", endpoints, StringComparison.Ordinal);
        Assert.Contains("integration.oauth.authorization_revoked", endpoints, StringComparison.Ordinal);
        Assert.Contains("integration.oauth.denied", endpoints, StringComparison.Ordinal);
        Assert.Contains("integration.oauth.token_exchange_failed", endpoints, StringComparison.Ordinal);
        Assert.Contains("AllowAutoRedirect = false", program, StringComparison.Ordinal);
        Assert.Contains("oauthstatehash", registry, StringComparison.Ordinal);
        Assert.DoesNotContain("clientSecret = settings.ClientSecret", endpoints, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("https://other.example.test/api/integrations/motive/oauth/callback")]
    [InlineData("https://api.example.test/api/integrations/motive/oauth/callback?x=1")]
    [InlineData("https://api.example.test/api/integrations/motive/oauth/callback#x")]
    public void Settings_RejectCallbackOutsideConfiguredApiOrigin(string redirect)
    {
        Assert.False(Service(new() { ["Motive:RedirectUri"] = redirect }).TryGetSettings(out _, out _));
    }

    [Theory]
    [InlineData("https://keeptruckin.com/oauth/token", true)]
    [InlineData("https://gomotive.com/oauth/token", true)]
    [InlineData("https://attacker.example.test/oauth/token", false)]
    [InlineData("https://gomotive.com/oauth/token?destination=elsewhere", false)]
    [InlineData("http://keeptruckin.com/oauth/token", false)]
    public void Settings_AllowOnlyExactOfficialTokenEndpoints(string endpoint, bool allowed)
    {
        Assert.Equal(allowed, Service(new() { ["Motive:TokenEndpoint"] = endpoint })
            .TryGetSettings(out _, out _));
    }

    [Fact]
    public async Task ExchangeCode_UsesServerSideFormAndValidatesTokens()
    {
        var handler = new ResponseHandler(HttpStatusCode.OK,
            "{\"access_token\":\"access-fixture\",\"refresh_token\":\"refresh-fixture\",\"token_type\":\"bearer\",\"expires_in\":7200}");
        var service = Service(httpFactory: new StaticHttpClientFactory(handler));
        Assert.True(service.TryGetSettings(out var settings, out _));
        Assert.Equal("https://keeptruckin.com/oauth/token", settings!.TokenEndpoint);

        var result = await service.ExchangeCodeAsync(settings, "test-code", CancellationToken.None);

        Assert.NotNull(result.Tokens);
        Assert.Null(result.Error);
        Assert.Equal("access-fixture", result.Tokens!.AccessToken);
        Assert.Equal("refresh-fixture", result.Tokens.RefreshToken);
        Assert.InRange(result.Tokens.ExpiresAt, DateTimeOffset.UtcNow.AddMinutes(119), DateTimeOffset.UtcNow.AddMinutes(121));
        Assert.Equal(HttpMethod.Post, handler.Method);
        Assert.Equal(settings.TokenEndpoint, handler.Url?.AbsoluteUri);
        Assert.Equal("application/x-www-form-urlencoded", handler.ContentType);
        Assert.Contains("grant_type=authorization_code", handler.Body);
        Assert.Contains("client_secret=test-client-secret", handler.Body);
        Assert.DoesNotContain("test-client-secret", handler.Url!.AbsoluteUri);
    }

    [Theory]
    [InlineData(400, "{\"error\":\"invalid_grant\"}")]
    [InlineData(302, "{}")]
    [InlineData(200, "not-json")]
    [InlineData(200, "{}")]
    [InlineData(200, "{\"access_token\":\"a\",\"refresh_token\":\"r\",\"token_type\":\"Bearer\",\"expires_in\":0}")]
    [InlineData(200, "{\"access_token\":\"a\",\"refresh_token\":\"r\",\"token_type\":\"Bearer\",\"expires_in\":90000}")]
    [InlineData(200, "{\"access_token\":\"a\",\"refresh_token\":\"r\",\"token_type\":\"Basic\",\"expires_in\":7200}")]
    [InlineData(200, "{\"access_token\":\"a\",\"token_type\":\"Bearer\",\"expires_in\":7200}")]
    public async Task ExchangeCode_RejectsFailedOrIncompleteResponses(int status, string body)
    {
        var service = Service(httpFactory: new StaticHttpClientFactory(new ResponseHandler((HttpStatusCode)status, body)));
        Assert.True(service.TryGetSettings(out var settings, out _));
        var result = await service.ExchangeCodeAsync(settings!, "test-code", CancellationToken.None);
        Assert.Null(result.Tokens);
        Assert.False(string.IsNullOrWhiteSpace(result.Error));
        Assert.DoesNotContain("test-client-secret", result.Error);
    }

    [Fact]
    public async Task ExchangeCode_RejectsMissingCodeWithoutNetwork()
    {
        var service = Service();
        Assert.True(service.TryGetSettings(out var settings, out _));
        var result = await service.ExchangeCodeAsync(settings!, "", CancellationToken.None);
        Assert.Null(result.Tokens);
        Assert.Contains("authorization code", result.Error);
    }

    private static MotiveOAuthService Service(Dictionary<string, string?>? overrides = null, IHttpClientFactory? httpFactory = null)
    {
        var values = new Dictionary<string, string?>
        {
            ["Motive:ClientId"] = "test-client-id",
            ["Motive:ClientSecret"] = "test-client-secret",
            ["PUBLIC_API_URL"] = "https://api.example.test",
            ["PUBLIC_APP_URL"] = "https://frontend.example.test",
        };
        foreach (var item in overrides ?? new Dictionary<string, string?>())
            values[item.Key] = item.Value;
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(values).Build();
        return new MotiveOAuthService(
            httpFactory ?? new NeverHttpClientFactory(),
            configuration,
            new EphemeralDataProtectionProvider(),
            new TestEnvironment(Environments.Staging),
            NullLogger<MotiveOAuthService>.Instance);
    }

    private static string RepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !Directory.Exists(Path.Combine(directory.FullName, "backend-dotnet")))
            directory = directory.Parent;
        Assert.NotNull(directory);
        return directory!.FullName;
    }

    private sealed class NeverHttpClientFactory : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => throw new InvalidOperationException("No network expected.");
    }

    private sealed class StaticHttpClientFactory(HttpMessageHandler handler) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new(handler, disposeHandler: false);
    }

    private sealed class ResponseHandler(HttpStatusCode status, string body) : HttpMessageHandler
    {
        public Uri? Url { get; private set; }
        public HttpMethod? Method { get; private set; }
        public string? Body { get; private set; }
        public string? ContentType { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Url = request.RequestUri;
            Method = request.Method;
            Body = await request.Content!.ReadAsStringAsync(ct);
            ContentType = request.Content.Headers.ContentType?.MediaType;
            return new HttpResponseMessage(status) { Content = new StringContent(body, Encoding.UTF8, "application/json") };
        }
    }

    private sealed class TestEnvironment(string name) : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = name;
        public string ApplicationName { get; set; } = "tests";
        public string ContentRootPath { get; set; } = Directory.GetCurrentDirectory();
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }
}
