using System.Net;
using System.Net.Http.Headers;
using System.Text;
using Microsoft.Extensions.Logging.Abstractions;
using Opstrax.Api.Services.Connectors;

namespace Opstrax.Tests;

public sealed class MotiveConnectorBehaviorTests
{
    [Fact]
    public async Task TestConnection_VerifiesOnlyTheNineRequiredReadScopes()
    {
        var handler = new ScriptedHandler(_ => Json(HttpStatusCode.OK, "{\"data\":[]}"));
        var connector = Connector(handler);

        var result = await connector.TestConnectionAsync(Config(), CancellationToken.None);

        Assert.True(result.Success, result.Message);
        Assert.Equal(9, handler.Requests.Count);
        Assert.Equal(9, result.Details!["verifiedEndpointCount"]);
        Assert.Equal(false, result.Details["writeScopesRequested"]);
        Assert.All(handler.Authorization, header =>
        {
            Assert.Equal("Bearer", header?.Scheme);
            Assert.Equal("test-motive-access-token", header?.Parameter);
        });
        Assert.Contains(handler.Requests, uri => uri.AbsolutePath == "/v1/companies");
        Assert.Contains(handler.Requests, uri => uri.AbsolutePath == "/v1/eld_devices");
        Assert.Contains(handler.Requests, uri => uri.AbsolutePath == "/v1/hours_of_service");
        var hos = Assert.Single(handler.Requests, uri => uri.AbsolutePath == "/v1/hours_of_service");
        var query = Microsoft.AspNetCore.WebUtilities.QueryHelpers.ParseQuery(hos.Query);
        Assert.True(DateOnly.TryParseExact(query["start_date"], "yyyy-MM-dd", out _));
        Assert.Equal(query["start_date"], query["end_date"]);
        Assert.Contains(handler.Requests, uri => uri.AbsolutePath == "/v1/inspection_reports");
    }

    [Theory]
    [InlineData(429, "{}")]
    [InlineData(500, "{}")]
    [InlineData(302, "{}")]
    [InlineData(200, "not-json")]
    [InlineData(200, "null")]
    public async Task TestConnection_RejectsRateLimitErrorsRedirectsAndInvalidJson(int status, string body)
    {
        var handler = new ScriptedHandler(_ => Json((HttpStatusCode)status, body));
        var result = await Connector(handler).TestConnectionAsync(Config(), CancellationToken.None);
        Assert.False(result.Success);
        Assert.Single(handler.Requests);
    }

    [Fact]
    public async Task TestConnection_FailsClosedAtTheFirstDeniedScope()
    {
        var handler = new ScriptedHandler(request =>
            request.RequestUri!.AbsolutePath == "/v1/eld_devices"
                ? Json(HttpStatusCode.Forbidden, "{}")
                : Json(HttpStatusCode.OK, "{\"data\":[]}"));
        var connector = Connector(handler);

        var result = await connector.TestConnectionAsync(Config(), CancellationToken.None);

        Assert.False(result.Success);
        Assert.Contains("eld_devices.read", result.Message, StringComparison.Ordinal);
        Assert.Equal(4, handler.Requests.Count);
    }

    [Fact]
    public async Task TestConnection_RejectsExpiredAndMissingTokensWithoutNetworkCalls()
    {
        var handler = new ScriptedHandler(_ => throw new InvalidOperationException("Network must not run."));
        var connector = Connector(handler);

        var missing = await connector.TestConnectionAsync(
            new Dictionary<string, string?>(), CancellationToken.None);
        var expired = await connector.TestConnectionAsync(
            new Dictionary<string, string?>
            {
                ["accessToken"] = "expired-token",
                ["tokenExpiresAt"] = DateTimeOffset.UtcNow.AddMinutes(-1).ToString("O"),
            }, CancellationToken.None);

        Assert.False(missing.Success);
        Assert.False(expired.Success);
        Assert.Empty(handler.Requests);
    }

    private static MotiveConnector Connector(HttpMessageHandler handler) => new(
        new StaticHttpClientFactory(handler), NullLogger<MotiveConnector>.Instance);

    private static IReadOnlyDictionary<string, string?> Config() =>
        new Dictionary<string, string?>
        {
            ["accessToken"] = "test-motive-access-token",
            ["tokenExpiresAt"] = DateTimeOffset.UtcNow.AddHours(1).ToString("O"),
        };

    private static HttpResponseMessage Json(HttpStatusCode status, string body) => new(status)
    {
        Content = new StringContent(body, Encoding.UTF8, "application/json"),
    };

    private sealed class StaticHttpClientFactory(HttpMessageHandler handler) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new(handler, disposeHandler: false);
    }

    private sealed class ScriptedHandler(Func<HttpRequestMessage, HttpResponseMessage> response) : HttpMessageHandler
    {
        public List<Uri> Requests { get; } = [];
        public List<AuthenticationHeaderValue?> Authorization { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests.Add(request.RequestUri!);
            Authorization.Add(request.Headers.Authorization);
            return Task.FromResult(response(request));
        }
    }
}
