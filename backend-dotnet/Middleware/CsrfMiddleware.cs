using System.Security.Cryptography;
using System.Text;

namespace Opstrax.Api.Middleware;

/// <summary>
/// CSRF Token Middleware - Generates and validates CSRF tokens for state-changing requests
/// </summary>
public class CsrfMiddleware
{
    private const string CSRF_TOKEN_HEADER = "X-CSRF-Token";
    private const string CSRF_COOKIE_NAME = "__CSRF_Token__";
    private static readonly string[] SAFE_METHODS = { "GET", "HEAD", "OPTIONS" };

    private readonly RequestDelegate _next;
    private readonly HashSet<string> _allowedOrigins;

    public CsrfMiddleware(RequestDelegate next, IConfiguration configuration)
    {
        _next = next;
        _allowedOrigins = configuration["Cors:AllowedOrigins"]?
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(NormalizeOrigin)
            .Where(origin => origin is not null)
            .Cast<string>()
            .ToHashSet(StringComparer.OrdinalIgnoreCase)
            ?? [];
    }

    public async Task InvokeAsync(HttpContext context)
    {
        var path = context.Request.Path.Value ?? string.Empty;
        var cookieToken = context.Request.Cookies[CSRF_COOKIE_NAME];
        var bearerOnly = string.IsNullOrEmpty(cookieToken) && HasBearerToken(context.Request);
        var responseToken = cookieToken;

        // Bearer-only clients do not use ambient browser credentials, so CSRF does not
        // apply until a CSRF cookie is present. This also avoids turning API clients
        // into cookie-authenticated clients as a side effect of an API response.
        //
        // Generate one token for browser clients and use that exact value for both
        // the response cookie and header.
        // Cookie flags must adapt to the request scheme: a Secure cookie is dropped
        // by the browser over plain HTTP (e.g. http://localhost:10000 in local/dev),
        // and SameSite=None *requires* Secure per spec — so pairing them breaks HTTP.
        // Over HTTPS keep the strict cross-site posture; over HTTP fall back to a
        // non-Secure Lax cookie so the double-submit token actually persists.
        if (string.IsNullOrEmpty(cookieToken) && !bearerOnly)
        {
            responseToken = GenerateToken();
            var isHttps = context.Request.IsHttps;
            context.Response.Cookies.Append(CSRF_COOKIE_NAME, responseToken, new CookieOptions
            {
                HttpOnly = false,
                Secure = isHttps,
                SameSite = isHttps ? SameSiteMode.None : SameSiteMode.Lax,
                MaxAge = TimeSpan.FromHours(8)
            });
        }

        // Validate cookie-authenticated state-changing requests. Bearer-only clients
        // are intentionally exempt because browsers do not attach Authorization
        // headers cross-origin without the caller explicitly possessing the token.
        if (!SAFE_METHODS.Contains(context.Request.Method) &&
            !bearerOnly &&
            !string.Equals(path, "/api/auth/login", StringComparison.OrdinalIgnoreCase) &&
            // Identifier-first SSO discovery is pre-session (no bearer/CSRF cookie
            // yet, same as login) and read-only apart from an audit log entry.
            !string.Equals(path, "/api/auth/sso/discover", StringComparison.OrdinalIgnoreCase) &&
            // Public account-recovery endpoints are pre-session, rate-limited, and
            // use opaque one-time tokens; there is no authenticated cookie to forge.
            !string.Equals(path, "/api/auth/forgot-password", StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(path, "/api/auth/reset-password", StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(path, "/api/platform/auth/login", StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(path, "/api/platform/auth/logout", StringComparison.OrdinalIgnoreCase) &&
            // Pre-session like login: the invited operator has no bearer/cookie yet.
            // Token-gated + lockout-limited inside the handler.
            !string.Equals(path, "/api/platform/auth/accept-invite", StringComparison.OrdinalIgnoreCase) &&
            // Device-authenticated machine-to-machine ingest (native GPS/OBD/telemetry).
            // These carry NO cookie — they authenticate with X-Device-Key + HMAC-SHA256
            // signature + nonce (stronger than CSRF). A physical device/tracker cannot
            // hold a CSRF cookie, so requiring one here would break every real device.
            !string.Equals(path, "/api/telemetry/ingest", StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(path, "/api/telemetry/gps-ingest", StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(path, "/api/maintenance/fault-codes/ingest", StringComparison.OrdinalIgnoreCase))
        {
            var headerToken = context.Request.Headers[CSRF_TOKEN_HEADER].ToString();

            if (!IsAllowedOrigin(context.Request) || !TokensMatch(cookieToken, headerToken))
            {
                context.Response.StatusCode = StatusCodes.Status403Forbidden;
                await context.Response.WriteAsJsonAsync(new { error = "CSRF token validation failed" });
                return;
            }
        }

        // Expose CSRF token as response header
        if (!string.IsNullOrEmpty(responseToken))
        {
            context.Response.Headers[CSRF_TOKEN_HEADER] = responseToken;
        }

        await _next(context);
    }

    private static bool HasBearerToken(HttpRequest request)
    {
        var authorization = request.Headers.Authorization.ToString();
        return authorization.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase) &&
               !string.IsNullOrWhiteSpace(authorization["Bearer ".Length..]);
    }

    private bool IsAllowedOrigin(HttpRequest request)
    {
        var origin = request.Headers.Origin.ToString();
        if (string.IsNullOrWhiteSpace(origin))
        {
            return true;
        }

        var normalizedOrigin = NormalizeOrigin(origin);
        var requestOrigin = NormalizeOrigin($"{request.Scheme}://{request.Host.Value}");
        return normalizedOrigin is not null &&
               (string.Equals(normalizedOrigin, requestOrigin, StringComparison.OrdinalIgnoreCase) ||
                _allowedOrigins.Contains(normalizedOrigin));
    }

    private static string? NormalizeOrigin(string origin)
    {
        if (!Uri.TryCreate(origin, UriKind.Absolute, out var uri) ||
            (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps) ||
            !string.IsNullOrEmpty(uri.UserInfo) ||
            uri.AbsolutePath != "/" ||
            !string.IsNullOrEmpty(uri.Query) ||
            !string.IsNullOrEmpty(uri.Fragment))
        {
            return null;
        }

        return uri.GetLeftPart(UriPartial.Authority);
    }

    private static bool TokensMatch(string? cookieToken, string? headerToken)
    {
        if (string.IsNullOrEmpty(cookieToken) || string.IsNullOrEmpty(headerToken))
        {
            return false;
        }

        var cookieHash = SHA256.HashData(Encoding.UTF8.GetBytes(cookieToken));
        var headerHash = SHA256.HashData(Encoding.UTF8.GetBytes(headerToken));
        return CryptographicOperations.FixedTimeEquals(cookieHash, headerHash);
    }

    private static string GenerateToken()
    {
        return Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));
    }
}
