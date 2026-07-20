using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Net.Http.Headers;
using Opstrax.Api.Middleware;

namespace Opstrax.Tests;

public class CsrfMiddlewareTests
{
    private const string CookieName = "__CSRF_Token__";
    private const string HeaderName = "X-CSRF-Token";

    [Fact]
    public async Task FirstBrowserResponse_UsesSameTokenInCookieAndHeader()
    {
        var context = CreateContext("GET");

        await InvokeAsync(context);

        var headerToken = context.Response.Headers[HeaderName].ToString();
        var cookieToken = ReadIssuedCookie(context);
        Assert.False(string.IsNullOrWhiteSpace(headerToken));
        Assert.Equal(cookieToken, headerToken);
    }

    [Fact]
    public async Task Mutation_WithMatchingTokenAndSameOrigin_IsAccepted()
    {
        const string token = "matching-token";
        var context = CreateContext("POST", token, token);
        context.Request.Headers.Origin = "https://app.example.test";

        var nextCalled = await InvokeAsync(context);

        Assert.True(nextCalled);
        Assert.NotEqual(StatusCodes.Status403Forbidden, context.Response.StatusCode);
    }

    [Fact]
    public async Task CookieMutation_FromConfiguredFrontendOrigin_IsAccepted()
    {
        const string token = "matching-token";
        var context = CreateContext("POST", token, token);
        context.Request.Headers.Origin = "https://frontend.example.test";

        var nextCalled = await InvokeAsync(context, "https://frontend.example.test");

        Assert.True(nextCalled);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("different-token")]
    public async Task Mutation_WithMissingOrMismatchedHeader_IsRejected(string? headerToken)
    {
        var context = CreateContext("PATCH", "cookie-token", headerToken);

        var nextCalled = await InvokeAsync(context);

        Assert.False(nextCalled);
        Assert.Equal(StatusCodes.Status403Forbidden, context.Response.StatusCode);
    }

    [Fact]
    public async Task CookieMutation_FromCrossOrigin_IsRejectedEvenWithMatchingToken()
    {
        const string token = "matching-token";
        var context = CreateContext("DELETE", token, token);
        context.Request.Headers.Origin = "https://evil.example";

        var nextCalled = await InvokeAsync(context);

        Assert.False(nextCalled);
        Assert.Equal(StatusCodes.Status403Forbidden, context.Response.StatusCode);
    }

    [Fact]
    public async Task BearerOnlyMutation_WithoutCookie_IsAcceptedAndDoesNotIssueCsrfState()
    {
        var context = CreateContext("POST");
        context.Request.Headers.Authorization = "Bearer api-client-token";
        context.Request.Headers.Origin = "https://api-client.example";

        var nextCalled = await InvokeAsync(context);

        Assert.True(nextCalled);
        Assert.False(context.Response.Headers.ContainsKey(HeaderName));
        Assert.False(context.Response.Headers.ContainsKey(HeaderNames.SetCookie));
    }

    [Fact]
    public async Task BearerMutation_WithCsrfCookie_StillRequiresMatchingHeader()
    {
        var context = CreateContext("POST", "cookie-token");
        context.Request.Headers.Authorization = "Bearer browser-session-token";

        var nextCalled = await InvokeAsync(context);

        Assert.False(nextCalled);
        Assert.Equal(StatusCodes.Status403Forbidden, context.Response.StatusCode);
    }

    private static DefaultHttpContext CreateContext(
        string method,
        string? cookieToken = null,
        string? headerToken = null)
    {
        var context = new DefaultHttpContext();
        context.Request.Method = method;
        context.Request.Scheme = "https";
        context.Request.Host = new HostString("app.example.test");
        context.Response.Body = new MemoryStream();

        if (cookieToken is not null)
        {
            context.Request.Headers.Cookie = $"{CookieName}={cookieToken}";
        }

        if (headerToken is not null)
        {
            context.Request.Headers[HeaderName] = headerToken;
        }

        return context;
    }

    private static async Task<bool> InvokeAsync(
        DefaultHttpContext context,
        string allowedOrigins = "https://frontend.example.test")
    {
        var nextCalled = false;
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Cors:AllowedOrigins"] = allowedOrigins
            })
            .Build();
        var middleware = new CsrfMiddleware(
            _ =>
            {
                nextCalled = true;
                return Task.CompletedTask;
            },
            configuration);

        await middleware.InvokeAsync(context);
        return nextCalled;
    }

    private static string ReadIssuedCookie(DefaultHttpContext context)
    {
        var setCookie = Assert.Single(context.Response.Headers.SetCookie);
        var parsed = SetCookieHeaderValue.Parse(setCookie!);
        Assert.Equal(CookieName, parsed.Name.Value);
        return Uri.UnescapeDataString(parsed.Value.Value!);
    }
}
