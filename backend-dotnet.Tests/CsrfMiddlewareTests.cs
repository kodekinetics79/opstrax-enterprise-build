using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Net.Http.Headers;
using Opstrax.Api.Middleware;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace Opstrax.Tests;

public class CsrfMiddlewareTests
{
    private const string CookieName = "__CSRF_Token_v2__";
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
        Assert.Equal(headerToken, context.Items[CsrfMiddleware.TokenItemKey]);
    }

    [Fact]
    public async Task ExistingBrowserCookie_IsPublishedAsAuthoritativeItemAndHeader()
    {
        const string token = "existing-cookie-token";
        var context = CreateContext("GET", token);

        await InvokeAsync(context);

        Assert.Equal(token, context.Response.Headers[HeaderName].ToString());
        Assert.Equal(token, context.Items[CsrfMiddleware.TokenItemKey]);
        Assert.False(context.Response.Headers.ContainsKey(HeaderNames.SetCookie));
    }

    [Theory]
    [InlineData("https", true, "None")]
    [InlineData("http", false, "Lax")]
    public async Task IssuedCookie_UsesSchemeAppropriateSecurityFlags(
        string scheme,
        bool secure,
        string sameSite)
    {
        var context = CreateContext("GET");
        context.Request.Scheme = scheme;

        await InvokeAsync(context);

        var setCookie = Assert.Single(context.Response.Headers.SetCookie).ToString();
        Assert.Contains("max-age=28800", setCookie, StringComparison.OrdinalIgnoreCase);
        Assert.Contains($"samesite={sameSite}", setCookie, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("path=/", setCookie, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("httponly", setCookie, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(secure, setCookie.Contains("secure", StringComparison.OrdinalIgnoreCase));
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

    [Fact]
    public async Task ExistingBrowserWithLegacyCookie_IsUpgradedOnSafeRequest()
    {
        var context = CreateContext("GET");
        context.Request.Headers.Cookie = "__CSRF_Token__=legacy-path-token";
        context.Request.Headers.Authorization = "Bearer browser-session-token";

        await InvokeAsync(context);

        Assert.False(string.IsNullOrWhiteSpace(context.Response.Headers[HeaderName]));
        var setCookie = Assert.Single(context.Response.Headers.SetCookie).ToString();
        Assert.Contains($"{CookieName}=", setCookie, StringComparison.Ordinal);
        Assert.Contains("path=/", setCookie, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task RealHttp_LoginTokenMatchesCookieAndHeader_ThenProtectsFleetMutation()
    {
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseKestrel().UseUrls("http://127.0.0.1:0");
        builder.Configuration["Cors:AllowedOrigins"] = "http://127.0.0.1";
        var app = builder.Build();
        app.UseMiddleware<CsrfMiddleware>();
        app.MapPost("/api/auth/login", (HttpContext http) => Results.Ok(new
        {
            csrfToken = http.Items[CsrfMiddleware.TokenItemKey]?.ToString(),
            token = "browser-session-token"
        }));
        app.MapPost("/api/fleet-tms/vehicles/1/service", () => Results.Ok(new { serviced = true }));

        await app.StartAsync();
        try
        {
            var address = app.Services.GetRequiredService<IServer>().Features
                .Get<IServerAddressesFeature>()!.Addresses.Single();
            var baseUri = new Uri(address);
            var cookies = new CookieContainer();
            using var client = new HttpClient(new HttpClientHandler { CookieContainer = cookies }) { BaseAddress = baseUri };

            var login = await client.PostAsJsonAsync("/api/auth/login", new { email = "pilot@example.test", password = "secret" });
            login.EnsureSuccessStatusCode();
            var headerToken = login.Headers.GetValues(HeaderName).Single();
            var cookieToken = Uri.UnescapeDataString(cookies.GetCookies(baseUri)[CookieName]!.Value);
            using var loginJson = JsonDocument.Parse(await login.Content.ReadAsStringAsync());
            var bodyToken = loginJson.RootElement.GetProperty("csrfToken").GetString();
            Assert.False(string.IsNullOrWhiteSpace(bodyToken));
            Assert.Equal(cookieToken, bodyToken);
            Assert.Equal(headerToken, bodyToken);

            // A subsequent login-style response must reuse the cookie already held
            // by the browser, rather than silently replacing the body/header token.
            var repeatLogin = await client.PostAsJsonAsync("/api/auth/login", new { email = "pilot@example.test", password = "secret" });
            repeatLogin.EnsureSuccessStatusCode();
            using var repeatJson = JsonDocument.Parse(await repeatLogin.Content.ReadAsStringAsync());
            Assert.Equal(cookieToken, repeatLogin.Headers.GetValues(HeaderName).Single());
            Assert.Equal(cookieToken, repeatJson.RootElement.GetProperty("csrfToken").GetString());
            Assert.Equal(cookieToken, Uri.UnescapeDataString(cookies.GetCookies(baseUri)[CookieName]!.Value));

            using var valid = new HttpRequestMessage(HttpMethod.Post, "/api/fleet-tms/vehicles/1/service")
            { Content = JsonContent.Create(new { status = "Maintenance" }) };
            valid.Headers.Add(HeaderName, bodyToken);
            var validResponse = await client.SendAsync(valid);
            Assert.Equal(HttpStatusCode.OK, validResponse.StatusCode);

            using var invalid = new HttpRequestMessage(HttpMethod.Post, "/api/fleet-tms/vehicles/1/service")
            { Content = JsonContent.Create(new { status = "Maintenance" }) };
            invalid.Headers.Add(HeaderName, "wrong-token");
            var invalidResponse = await client.SendAsync(invalid);
            Assert.Equal(HttpStatusCode.Forbidden, invalidResponse.StatusCode);
        }
        finally
        {
            await app.StopAsync();
            await app.DisposeAsync();
        }
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
