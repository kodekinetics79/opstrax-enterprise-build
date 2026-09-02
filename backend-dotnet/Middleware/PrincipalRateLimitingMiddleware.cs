using System.Threading.RateLimiting;
using Opstrax.Api.Controllers;
using Opstrax.Api.DTOs;

namespace Opstrax.Api.Middleware;

/// <summary>
/// Validated, bounded rate-limit settings. Configuration can tune capacity without
/// allowing an accidental zero/unbounded value to disable either availability or
/// abuse protection.
/// </summary>
internal sealed record ApiRateLimitSettings(
    int AuthenticatedPermitLimit,
    int PreAuthBearerPermitLimit,
    int AnonymousPermitLimit,
    int LoginPermitLimit,
    int RefreshPermitLimit,
    int DevicePermitLimit,
    int PublicPermitLimit,
    TimeSpan Window)
{
    internal const int DefaultAuthenticatedPermitLimit = 1_200;
    internal const int DefaultPreAuthBearerPermitLimit = 6_000;
    internal const int DefaultAnonymousPermitLimit = 240;
    internal const int DefaultLoginPermitLimit = 10;
    internal const int DefaultRefreshPermitLimit = 120;
    internal const int DefaultDevicePermitLimit = 1_200;
    internal const int DefaultPublicPermitLimit = 600;
    internal const int DefaultWindowSeconds = 60;

    internal static ApiRateLimitSettings FromConfiguration(IConfiguration configuration)
    {
        static int Bounded(IConfiguration config, string key, int fallback, int min, int max)
            => Math.Clamp(config.GetValue(key, fallback), min, max);

        return new ApiRateLimitSettings(
            Bounded(configuration, "RateLimit:Authenticated:PermitLimit", DefaultAuthenticatedPermitLimit, 120, 6_000),
            Bounded(configuration, "RateLimit:PreAuthBearer:PermitLimit", DefaultPreAuthBearerPermitLimit, 600, 30_000),
            Bounded(configuration, "RateLimit:Anonymous:PermitLimit", DefaultAnonymousPermitLimit, 30, 2_000),
            Bounded(configuration, "RateLimit:Login:PermitLimit", DefaultLoginPermitLimit, 5, 100),
            Bounded(configuration, "RateLimit:Refresh:PermitLimit", DefaultRefreshPermitLimit, 10, 600),
            Bounded(configuration, "RateLimit:Device:PermitLimit", DefaultDevicePermitLimit, 60, 10_000),
            Bounded(configuration, "RateLimit:Public:PermitLimit", DefaultPublicPermitLimit, 30, 5_000),
            TimeSpan.FromSeconds(Bounded(configuration, "RateLimit:WindowSeconds", DefaultWindowSeconds, 10, 300)));
    }
}

/// <summary>
/// Exact framework limiter factories used by Program and by deterministic tests.
/// The pre-auth Bearer ceiling is intentionally keyed by IP, never token text, so
/// rotating garbage tokens cannot create unlimited partitions and DB lookups.
/// </summary>
internal static class ApiRateLimiterFactory
{
    internal static PartitionedRateLimiter<HttpContext> CreatePreAuthGeneral(ApiRateLimitSettings settings)
        => PartitionedRateLimiter.Create<HttpContext, string>(context =>
        {
            if (HttpMethods.IsOptions(context.Request.Method) ||
                !context.Request.Path.StartsWithSegments("/api", StringComparison.OrdinalIgnoreCase) ||
                ApiRateLimitClassifier.IsHealthProbe(context.Request.Path) ||
                ApiRateLimitClassifier.IsSensitive(context.Request.Path))
            {
                return RateLimitPartition.GetNoLimiter("general-exempt");
            }

            var bearer = ApiRateLimitClassifier.HasBearerToken(context);
            var category = bearer ? "bearer-ip" : "anonymous";
            var permitLimit = bearer ? settings.PreAuthBearerPermitLimit : settings.AnonymousPermitLimit;
            return RateLimitPartition.GetFixedWindowLimiter(
                $"{category}:{ApiRateLimitClassifier.ClientKey(context)}",
                _ => new FixedWindowRateLimiterOptions
                {
                    AutoReplenishment = true,
                    PermitLimit = permitLimit,
                    QueueLimit = 0,
                    Window = settings.Window
                });
        });

    internal static PartitionedRateLimiter<HttpContext> CreateAbuse(ApiRateLimitSettings settings)
        => PartitionedRateLimiter.Create<HttpContext, string>(context =>
        {
            var path = context.Request.Path;
            if (HttpMethods.IsOptions(context.Request.Method) || !ApiRateLimitClassifier.IsSensitive(path))
                return RateLimitPartition.GetNoLimiter("not-sensitive");

            var (category, permitLimit) =
                ApiRateLimitClassifier.IsLogin(path) ? ("login", settings.LoginPermitLimit) :
                ApiRateLimitClassifier.IsRefresh(path) ? ("refresh", settings.RefreshPermitLimit) :
                ApiRateLimitClassifier.IsDevice(path) ? ("device", settings.DevicePermitLimit) :
                ("public", settings.PublicPermitLimit);

            return RateLimitPartition.GetFixedWindowLimiter(
                $"{category}:{ApiRateLimitClassifier.ClientKey(context)}",
                _ => new FixedWindowRateLimiterOptions
                {
                    AutoReplenishment = true,
                    PermitLimit = permitLimit,
                    QueueLimit = 0,
                    Window = settings.Window
                });
        });
}

/// <summary>
/// Shared path classification for the pre-authentication abuse controls. Regular
/// bearer-authenticated API traffic is deliberately deferred to the authoritative
/// tenant/user limiter after session validation.
/// </summary>
internal static class ApiRateLimitClassifier
{
    internal static string ClientKey(HttpContext context)
        => context.Connection.RemoteIpAddress?.ToString() ?? "unknown";

    internal static bool HasBearerToken(HttpContext context)
    {
        var authorization = context.Request.Headers.Authorization.ToString();
        return authorization.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase) &&
               !string.IsNullOrWhiteSpace(authorization["Bearer ".Length..]);
    }

    internal static bool IsHealthProbe(PathString path)
        => path.Equals("/api/health", StringComparison.OrdinalIgnoreCase) ||
           path.Equals("/api/ready", StringComparison.OrdinalIgnoreCase) ||
           path.StartsWithSegments("/health", StringComparison.OrdinalIgnoreCase);

    internal static bool IsLogin(PathString path)
        => path.Equals("/api/auth/login", StringComparison.OrdinalIgnoreCase) ||
           path.Equals("/api/auth/mfa/login-verify", StringComparison.OrdinalIgnoreCase) ||
           path.Equals("/api/auth/sso/discover", StringComparison.OrdinalIgnoreCase) ||
           path.StartsWithSegments("/api/auth/sso/start", StringComparison.OrdinalIgnoreCase) ||
           path.Equals("/api/auth/sso/callback", StringComparison.OrdinalIgnoreCase) ||
           // OAuth callbacks may trigger an external token exchange. Keep them on
           // the low authentication ceiling, never the general anonymous API quota.
           path.Equals("/api/integrations/motive/oauth/callback", StringComparison.OrdinalIgnoreCase) ||
           path.Equals("/api/auth/forgot-password", StringComparison.OrdinalIgnoreCase) ||
           path.Equals("/api/auth/reset-password", StringComparison.OrdinalIgnoreCase) ||
           path.Equals("/api/platform/auth/login", StringComparison.OrdinalIgnoreCase);

    internal static bool IsRefresh(PathString path)
        => path.Equals("/api/auth/refresh", StringComparison.OrdinalIgnoreCase);

    internal static bool IsDevice(PathString path)
        => path.StartsWithSegments("/api/telemetry/ingest", StringComparison.OrdinalIgnoreCase) ||
           path.StartsWithSegments("/api/telemetry/gps-ingest", StringComparison.OrdinalIgnoreCase) ||
           path.StartsWithSegments("/api/maintenance/fault-codes/ingest", StringComparison.OrdinalIgnoreCase);

    internal static bool IsPublic(PathString path)
        => path.StartsWithSegments("/api/public", StringComparison.OrdinalIgnoreCase) ||
           path.StartsWithSegments("/api/customer-eta/track", StringComparison.OrdinalIgnoreCase) ||
           path.StartsWithSegments("/api/customer-visibility/tracking", StringComparison.OrdinalIgnoreCase);

    internal static bool IsSensitive(PathString path)
        => IsLogin(path) || IsRefresh(path) || IsDevice(path) || IsPublic(path);

    internal static string PrincipalKey(HttpContext context)
    {
        if (context.Items.TryGetValue(EndpointMappings.AuthCompanyIdItemKey, out var company) &&
            context.Items.TryGetValue(EndpointMappings.AuthUserIdItemKey, out var user) &&
            company is not null && user is not null)
        {
            return $"principal:{Convert.ToInt64(company)}:{Convert.ToInt64(user)}";
        }

        return $"ip:{ClientKey(context)}";
    }
}

/// <summary>
/// Per-principal limiter used only after the session middleware has populated the
/// authoritative tenant/user identifiers. Multiple sessions belonging to the same
/// user therefore share a quota, while unrelated users behind one corporate NAT do not.
/// </summary>
internal sealed class PrincipalApiRateLimiter : IAsyncDisposable
{
    private readonly PartitionedRateLimiter<HttpContext> _limiter;

    public PrincipalApiRateLimiter(ApiRateLimitSettings settings)
    {
        _limiter = PartitionedRateLimiter.Create<HttpContext, string>(context =>
            RateLimitPartition.GetFixedWindowLimiter(
                ApiRateLimitClassifier.PrincipalKey(context),
                _ => new FixedWindowRateLimiterOptions
                {
                    AutoReplenishment = true,
                    PermitLimit = settings.AuthenticatedPermitLimit,
                    QueueLimit = 0,
                    Window = settings.Window
                }));
    }

    internal ValueTask<RateLimitLease> AcquireAsync(HttpContext context, CancellationToken cancellationToken)
        => _limiter.AcquireAsync(context, 1, cancellationToken);

    public ValueTask DisposeAsync() => _limiter.DisposeAsync();
}

/// <summary>
/// Runs inside the authenticated API branch, after the session has been resolved.
/// Sensitive pre-session/device/public routes remain protected by the earlier IP
/// abuse limiter and arrive here without an authenticated principal, so they pass
/// through without being counted twice.
/// </summary>
internal sealed class PrincipalRateLimitingMiddleware
{
    private readonly RequestDelegate _next;
    private readonly PrincipalApiRateLimiter _limiter;
    private readonly ApiRateLimitSettings _settings;

    public PrincipalRateLimitingMiddleware(
        RequestDelegate next,
        PrincipalApiRateLimiter limiter,
        ApiRateLimitSettings settings)
    {
        _next = next;
        _limiter = limiter;
        _settings = settings;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        if (!context.Items.ContainsKey(EndpointMappings.AuthCompanyIdItemKey) ||
            !context.Items.ContainsKey(EndpointMappings.AuthUserIdItemKey) ||
            ApiRateLimitClassifier.IsSensitive(context.Request.Path))
        {
            await _next(context);
            return;
        }

        using var lease = await _limiter.AcquireAsync(context, context.RequestAborted);
        if (lease.IsAcquired)
        {
            await _next(context);
            return;
        }

        var retryAfter = _settings.Window;
        if (lease.TryGetMetadata(MetadataName.RetryAfter, out var leaseRetryAfter))
            retryAfter = leaseRetryAfter;

        context.Response.StatusCode = StatusCodes.Status429TooManyRequests;
        context.Response.Headers.RetryAfter =
            Math.Max(1, (int)Math.Ceiling(retryAfter.TotalSeconds)).ToString();
        await context.Response.WriteAsJsonAsync(
            ApiResponse<object>.Fail("Too many requests", "Rate limit exceeded"),
            context.RequestAborted);
    }
}
