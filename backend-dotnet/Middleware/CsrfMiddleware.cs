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

    public CsrfMiddleware(RequestDelegate next)
    {
        _next = next;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        var path = context.Request.Path.Value ?? string.Empty;

        // Generate CSRF token for GET requests or if not present.
        // Cookie flags must adapt to the request scheme: a Secure cookie is dropped
        // by the browser over plain HTTP (e.g. http://localhost:10000 in local/dev),
        // and SameSite=None *requires* Secure per spec — so pairing them breaks HTTP.
        // Over HTTPS keep the strict cross-site posture; over HTTP fall back to a
        // non-Secure Lax cookie so the double-submit token actually persists.
        if (!context.Request.Cookies.ContainsKey(CSRF_COOKIE_NAME))
        {
            var token = GenerateToken();
            var isHttps = context.Request.IsHttps;
            context.Response.Cookies.Append(CSRF_COOKIE_NAME, token, new Microsoft.AspNetCore.Http.CookieOptions
            {
                HttpOnly = false,
                Secure = isHttps,
                SameSite = isHttps
                    ? Microsoft.AspNetCore.Http.SameSiteMode.None
                    : Microsoft.AspNetCore.Http.SameSiteMode.Lax,
                MaxAge = TimeSpan.FromHours(8)
            });
        }

        // Validate CSRF token for state-changing requests (POST, PUT, DELETE)
        if (!SAFE_METHODS.Contains(context.Request.Method) &&
            !string.Equals(path, "/api/auth/login", StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(path, "/api/platform/auth/login", StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(path, "/api/platform/auth/logout", StringComparison.OrdinalIgnoreCase))
        {
            var cookieToken = context.Request.Cookies[CSRF_COOKIE_NAME];
            var headerToken = context.Request.Headers[CSRF_TOKEN_HEADER].ToString();

            if (string.IsNullOrEmpty(cookieToken) || cookieToken != headerToken)
            {
                context.Response.StatusCode = StatusCodes.Status403Forbidden;
                await context.Response.WriteAsJsonAsync(new { error = "CSRF token validation failed" });
                return;
            }
        }

        // Expose CSRF token as response header
        context.Response.Headers[CSRF_TOKEN_HEADER] = context.Request.Cookies[CSRF_COOKIE_NAME] ?? GenerateToken();

        await _next(context);
    }

    private static string GenerateToken()
    {
        return Convert.ToBase64String(System.Security.Cryptography.RandomNumberGenerator.GetBytes(32));
    }
}
