using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.DataProtection;

namespace Opstrax.Api.Services.Connectors;

public sealed record MotiveOAuthSettings(
    string ClientId,
    string ClientSecret,
    string RedirectUri,
    string FrontendUri,
    IReadOnlyList<string> Scopes,
    string TokenEndpoint);

public sealed record MotiveOAuthState(
    long CompanyId,
    long IntegrationId,
    long ActorUserId,
    long OperationGeneration,
    string Nonce,
    DateTimeOffset ExpiresAt);

public sealed record MotiveOAuthTokens(
    string AccessToken,
    string RefreshToken,
    string TokenType,
    DateTimeOffset ExpiresAt);

/// <summary>
/// Motive OAuth 2.0 coordinator. Provider app credentials remain server-side;
/// browsers receive only the provider authorization URL. The protected state is
/// additionally bound to a one-time encrypted hash in the integration row.
/// </summary>
public sealed class MotiveOAuthService(
    IHttpClientFactory httpFactory,
    IConfiguration configuration,
    IDataProtectionProvider dataProtection,
    IHostEnvironment environment,
    ILogger<MotiveOAuthService> logger)
{
    internal const string AuthorizationEndpoint = "https://gomotive.com/oauth/authorize";
    // Motive's current endpoint-specific guide uses keeptruckin.com, while its
    // overview still names gomotive.com. Credential-free probes on 2026-09-02
    // returned OAuth JSON/400 from keeptruckin and HTML/404 from gomotive. The
    // override is restricted to these two exact official destinations, not a URL input.
    internal const string TokenEndpoint = "https://keeptruckin.com/oauth/token";
    internal const string CallbackPath = "/api/integrations/motive/oauth/callback";
    internal const string FlowCookieName = "__Host-opstrax-motive-oauth";

    public static CookieOptions FlowCookieOptions() => new()
    {
        HttpOnly = true,
        Secure = true,
        Path = "/",
        // The staging SPA and API can be on different sites. A blocked cookie
        // fails closed at callback; there is deliberately no cookie-less fallback.
        SameSite = SameSiteMode.None,
        MaxAge = TimeSpan.FromMinutes(10),
        IsEssential = true,
    };

    public static bool MatchesBrowserNonce(string? cookie, MotiveOAuthState state) =>
        !string.IsNullOrWhiteSpace(cookie)
        && CryptographicOperations.FixedTimeEquals(
            SHA256.HashData(Encoding.UTF8.GetBytes(cookie)),
            SHA256.HashData(Encoding.UTF8.GetBytes(state.Nonce)));

    // Least-privilege, read-only evidence set for the active G2B lane. Write,
    // messaging, dispatch, card, camera, and webhook-management scopes are excluded.
    public static readonly IReadOnlyList<string> RequiredScopes =
    [
        "companies.read",
        "users.read",
        "vehicles.read",
        "eld_devices.read",
        "locations.vehicle_locations_list",
        "hos_logs.hours_of_service",
        "hos_logs.hos_violation",
        "hos_logs.logs",
        "inspection_reports.read",
    ];

    private readonly IDataProtector _stateProtector = dataProtection.CreateProtector(
        "opstrax.integrations.motive.oauth-state.v1");

    public bool TryGetSettings(out MotiveOAuthSettings? settings, out string? error)
    {
        settings = null;
        var clientId = (configuration["Motive:ClientId"]
            ?? Environment.GetEnvironmentVariable("MOTIVE_CLIENT_ID"))?.Trim();
        var clientSecret = (configuration["Motive:ClientSecret"]
            ?? Environment.GetEnvironmentVariable("MOTIVE_CLIENT_SECRET"))?.Trim();
        var publicApi = (configuration["PublicApiUrl"]
            ?? configuration["PUBLIC_API_URL"]
            ?? Environment.GetEnvironmentVariable("PUBLIC_API_URL"))?.Trim().TrimEnd('/');
        var explicitRedirect = (configuration["Motive:RedirectUri"]
            ?? Environment.GetEnvironmentVariable("MOTIVE_REDIRECT_URI"))?.Trim();
        var redirectUri = !string.IsNullOrWhiteSpace(explicitRedirect)
            ? explicitRedirect
            : string.IsNullOrWhiteSpace(publicApi) ? null : publicApi + CallbackPath;
        var frontendUri = (configuration["PublicAppUrl"]
            ?? configuration["PUBLIC_APP_URL"]
            ?? Environment.GetEnvironmentVariable("PUBLIC_APP_URL"))?.Trim().TrimEnd('/');
        var tokenEndpoint = (configuration["Motive:TokenEndpoint"]
            ?? configuration["MOTIVE_TOKEN_ENDPOINT"]
            ?? Environment.GetEnvironmentVariable("MOTIVE_TOKEN_ENDPOINT")
            ?? TokenEndpoint).Trim();
        var protectedEnvironment = environment.IsProduction() || environment.IsStaging();

        if (string.IsNullOrWhiteSpace(clientId) || string.IsNullOrWhiteSpace(clientSecret))
        {
            error = "Motive OAuth app credentials are not configured on the API service.";
            return false;
        }
        if (!Uri.TryCreate(redirectUri, UriKind.Absolute, out var redirect)
            || redirect.Scheme != Uri.UriSchemeHttps
            || !redirect.AbsolutePath.Equals(CallbackPath, StringComparison.Ordinal)
            || !string.IsNullOrEmpty(redirect.UserInfo)
            || !string.IsNullOrEmpty(redirect.Query)
            || !string.IsNullOrEmpty(redirect.Fragment)
            || protectedEnvironment && !string.Equals(redirect.AbsoluteUri, publicApi + CallbackPath, StringComparison.Ordinal))
        {
            error = $"Motive redirect URI must match PUBLIC_API_URL plus {CallbackPath} using HTTPS.";
            return false;
        }
        if (!Uri.TryCreate(frontendUri, UriKind.Absolute, out var frontend)
            || protectedEnvironment && frontend.Scheme != Uri.UriSchemeHttps
            || !string.IsNullOrEmpty(frontend.UserInfo)
            || !string.IsNullOrEmpty(frontend.Query)
            || !string.IsNullOrEmpty(frontend.Fragment))
        {
            error = "PUBLIC_APP_URL must be an absolute HTTPS URL in a protected environment.";
            return false;
        }
        if (tokenEndpoint is not ("https://keeptruckin.com/oauth/token" or "https://gomotive.com/oauth/token"))
        {
            error = "Motive token endpoint must be one of the two exact official OAuth token URLs.";
            return false;
        }

        settings = new MotiveOAuthSettings(
            clientId!, clientSecret!, redirect.AbsoluteUri, frontend.AbsoluteUri.TrimEnd('/'), RequiredScopes, tokenEndpoint);
        error = null;
        return true;
    }

    public (string State, string StateHash, MotiveOAuthState Payload) CreateState(
        long companyId, long integrationId, long actorUserId, long operationGeneration)
    {
        var payload = new MotiveOAuthState(
            companyId,
            integrationId,
            actorUserId,
            operationGeneration,
            Convert.ToHexString(RandomNumberGenerator.GetBytes(24)).ToLowerInvariant(),
            DateTimeOffset.UtcNow.AddMinutes(10));
        var state = _stateProtector.Protect(JsonSerializer.Serialize(payload));
        return (state, HashState(state), payload);
    }

    public bool TryReadState(string? state, out MotiveOAuthState? payload)
    {
        payload = null;
        if (string.IsNullOrWhiteSpace(state)) return false;
        try
        {
            payload = JsonSerializer.Deserialize<MotiveOAuthState>(_stateProtector.Unprotect(state));
            return payload is not null
                && payload.CompanyId > 0
                && payload.IntegrationId > 0
                && payload.ActorUserId > 0
                && payload.OperationGeneration >= 0
                && payload.ExpiresAt > DateTimeOffset.UtcNow
                && payload.ExpiresAt <= DateTimeOffset.UtcNow.AddMinutes(11)
                && !string.IsNullOrWhiteSpace(payload.Nonce)
                && payload.Nonce.Length >= 32;
        }
        catch (Exception ex) when (ex is CryptographicException or JsonException)
        {
            logger.LogWarning("Rejected an invalid or expired Motive OAuth state.");
            return false;
        }
    }

    public string BuildAuthorizationUrl(MotiveOAuthSettings settings, string state)
    {
        static string E(string value) => Uri.EscapeDataString(value);
        return AuthorizationEndpoint
            + $"?client_id={E(settings.ClientId)}"
            + $"&redirect_uri={E(settings.RedirectUri)}"
            + "&response_type=code"
            + $"&scope={E(string.Join(' ', settings.Scopes))}"
            + $"&state={E(state)}";
    }

    public async Task<(MotiveOAuthTokens? Tokens, string? Error)> ExchangeCodeAsync(
        MotiveOAuthSettings settings, string code, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(code)) return (null, "Motive did not return an authorization code.");
        try
        {
            var client = httpFactory.CreateClient("motive-oauth");
            client.Timeout = TimeSpan.FromSeconds(20);
            using var request = new HttpRequestMessage(HttpMethod.Post, settings.TokenEndpoint)
            {
                Content = new FormUrlEncodedContent(new Dictionary<string, string>
                {
                    ["grant_type"] = "authorization_code",
                    ["code"] = code,
                    ["redirect_uri"] = settings.RedirectUri,
                    ["client_id"] = settings.ClientId,
                    ["client_secret"] = settings.ClientSecret,
                }),
            };
            request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
            using var response = await client.SendAsync(request, ct);
            if (!response.IsSuccessStatusCode)
                return (null, $"Motive token exchange returned HTTP {(int)response.StatusCode}.");

            using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync(ct));
            var root = document.RootElement;
            var accessToken = root.TryGetProperty("access_token", out var access) ? access.GetString() : null;
            var refreshToken = root.TryGetProperty("refresh_token", out var refresh) ? refresh.GetString() : null;
            var tokenType = root.TryGetProperty("token_type", out var type) ? type.GetString() : null;
            var expiresIn = root.TryGetProperty("expires_in", out var expiry) && expiry.TryGetInt32(out var seconds)
                ? seconds
                : 0;
            if (string.IsNullOrWhiteSpace(accessToken) || string.IsNullOrWhiteSpace(refreshToken)
                || expiresIn is < 60 or > 86_400
                || !string.Equals(tokenType, "Bearer", StringComparison.OrdinalIgnoreCase))
                return (null, "Motive token exchange returned an incomplete token response.");

            return (new MotiveOAuthTokens(
                accessToken!, refreshToken!, string.IsNullOrWhiteSpace(tokenType) ? "Bearer" : tokenType!,
                DateTimeOffset.UtcNow.AddSeconds(expiresIn)), null);
        }
        catch (TaskCanceledException)
        {
            return (null, "Motive token exchange timed out.");
        }
        catch (JsonException)
        {
            return (null, "Motive token exchange returned malformed JSON.");
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Motive OAuth token exchange failed.");
            return (null, "Motive token exchange could not be completed.");
        }
    }

    public static string HashState(string state) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(state))).ToLowerInvariant();

    public static string ResultRedirect(MotiveOAuthSettings settings, string result) =>
        settings.FrontendUri + "/integrations?motiveOAuth=" + Uri.EscapeDataString(result);
}
