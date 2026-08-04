using System.Net;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Opstrax.Api.Controllers;
using Opstrax.Api.Middleware;

namespace Opstrax.Tests;

public sealed class RateLimitingConfigurationTests
{
    private static readonly string ProgramSource = ReadProgramSource();

    [Fact]
    public void UsesFrameworkPartitionedLimitersInsteadOfCustomUnboundedDictionary()
    {
        Assert.Contains("builder.Services.AddRateLimiter", ProgramSource);
        Assert.Contains("PartitionedRateLimiter.CreateChained", ProgramSource);
        Assert.Contains("ApiRateLimiterFactory.CreatePreAuthGeneral", ProgramSource);
        Assert.Contains("ApiRateLimiterFactory.CreateAbuse", ProgramSource);
        Assert.Contains("PrincipalRateLimitingMiddleware", ProgramSource);
        Assert.DoesNotContain("ConcurrentDictionary<string, (DateTimeOffset WindowStart, int Count)>", ProgramSource);
        Assert.DoesNotContain("rateWindows.AddOrUpdate", ProgramSource);
    }

    [Fact]
    public void AppliesStrictLoginAndGeneralApiPoliciesWithoutQueues()
    {
        Assert.Contains("ApiRateLimitSettings.FromConfiguration", ProgramSource);
        Assert.Contains("RateLimit:Authenticated:PermitLimit", RateLimitSource);
        Assert.Contains("RateLimit:PreAuthBearer:PermitLimit", RateLimitSource);
        Assert.Contains("RateLimit:Login:PermitLimit", RateLimitSource);
        Assert.Contains("RateLimit:Refresh:PermitLimit", RateLimitSource);
        Assert.Contains("RateLimit:Device:PermitLimit", RateLimitSource);
        Assert.Contains("RateLimit:Public:PermitLimit", RateLimitSource);
        Assert.Equal(3, CountOccurrences(RateLimitSource, "QueueLimit = 0"));
    }

    [Fact]
    public void ExemptsOnlyNonApiAndHealthProbeTrafficFromGeneralPolicy()
    {
        Assert.Contains("!context.Request.Path.StartsWithSegments(\"/api\"", RateLimitSource);
        Assert.Contains("path.Equals(\"/api/health\"", RateLimitSource);
        Assert.Contains("path.Equals(\"/api/ready\"", RateLimitSource);
        Assert.Contains("path.StartsWithSegments(\"/health\"", RateLimitSource);
        Assert.Contains("RateLimitPartition.GetNoLimiter(\"general-exempt\")", RateLimitSource);
    }

    [Fact]
    public void EmitsCompatibleJsonRejectionAndRetryAfter()
    {
        Assert.Contains("StatusCodes.Status429TooManyRequests", ProgramSource);
        Assert.Contains("MetadataName.RetryAfter", ProgramSource);
        Assert.Contains("Response.Headers.RetryAfter", ProgramSource);
        Assert.Contains("ApiResponse<object>.Fail(\"Too many requests\", \"Rate limit exceeded\")", ProgramSource);
    }

    [Fact]
    public void TrustsOnlyOneForwardedHopFromExplicitOrRenderPrivateProxies()
    {
        Assert.Contains("options.ForwardLimit = 1;", ProgramSource);
        Assert.Contains("options.KnownNetworks.Clear();", ProgramSource);
        Assert.Contains("options.KnownProxies.Clear();", ProgramSource);
        Assert.Contains("Proxy:KnownNetworks", ProgramSource);
        Assert.Contains("10.0.0.0/8", ProgramSource);
        Assert.Contains("172.16.0.0/12", ProgramSource);
        Assert.Contains("192.168.0.0/16", ProgramSource);
        Assert.DoesNotContain("KnownNetworks.Add(new IPNetwork(IPAddress.Any", ProgramSource);
    }

    [Fact]
    public void ResolvesForwardedClientBeforeTelemetryAndRateLimitingBeforeAuthBypass()
    {
        var forwardedHeaders = ProgramSource.IndexOf("app.UseForwardedHeaders();", StringComparison.Ordinal);
        var telemetry = ProgramSource.IndexOf("app.UseMiddleware<RequestTelemetryMiddleware>();", StringComparison.Ordinal);
        var rateLimiter = ProgramSource.IndexOf("app.UseRateLimiter();", StringComparison.Ordinal);
        var authBypass = ProgramSource.IndexOf("app.UseWhen(", StringComparison.Ordinal);

        Assert.True(forwardedHeaders >= 0 && forwardedHeaders < telemetry);
        Assert.True(rateLimiter >= 0 && rateLimiter < authBypass);
    }

    [Fact]
    public void ConfigurationValuesAreBoundedAndDefaultsArePilotSafe()
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["RateLimit:Authenticated:PermitLimit"] = "999999",
            ["RateLimit:PreAuthBearer:PermitLimit"] = "-1",
            ["RateLimit:Anonymous:PermitLimit"] = "0",
            ["RateLimit:Login:PermitLimit"] = "999",
            ["RateLimit:Refresh:PermitLimit"] = "1",
            ["RateLimit:Device:PermitLimit"] = "999999",
            ["RateLimit:Public:PermitLimit"] = "1",
            ["RateLimit:WindowSeconds"] = "9999"
        }).Build();

        var settings = ApiRateLimitSettings.FromConfiguration(configuration);

        Assert.Equal(6_000, settings.AuthenticatedPermitLimit);
        Assert.Equal(600, settings.PreAuthBearerPermitLimit);
        Assert.Equal(30, settings.AnonymousPermitLimit);
        Assert.Equal(100, settings.LoginPermitLimit);
        Assert.Equal(10, settings.RefreshPermitLimit);
        Assert.Equal(10_000, settings.DevicePermitLimit);
        Assert.Equal(30, settings.PublicPermitLimit);
        Assert.Equal(TimeSpan.FromSeconds(300), settings.Window);

        var defaults = ApiRateLimitSettings.FromConfiguration(new ConfigurationBuilder().Build());
        Assert.Equal(1_200, defaults.AuthenticatedPermitLimit);
        Assert.Equal(6_000, defaults.PreAuthBearerPermitLimit);
        Assert.Equal(10, defaults.LoginPermitLimit);
        Assert.Equal(TimeSpan.FromMinutes(1), defaults.Window);
    }

    [Fact]
    public void PartitionKeysUseAuthoritativeTenantAndUserWithIpFallback()
    {
        var first = Context("203.0.113.25", companyId: 7, userId: 11);
        var second = Context("203.0.113.25", companyId: 7, userId: 12);
        var anotherTenant = Context("203.0.113.25", companyId: 8, userId: 11);
        var samePrincipalAnotherSession = Context("203.0.113.25", companyId: 7, userId: 11);
        samePrincipalAnotherSession.Request.Headers.Authorization = "Bearer another-session";

        Assert.Equal("principal:7:11", ApiRateLimitClassifier.PrincipalKey(first));
        Assert.Equal("principal:7:12", ApiRateLimitClassifier.PrincipalKey(second));
        Assert.Equal("principal:8:11", ApiRateLimitClassifier.PrincipalKey(anotherTenant));
        Assert.Equal("principal:7:11", ApiRateLimitClassifier.PrincipalKey(samePrincipalAnotherSession));
        Assert.Equal("ip:203.0.113.25", ApiRateLimitClassifier.PrincipalKey(Context("203.0.113.25")));
        Assert.Equal("ip:203.0.113.26", ApiRateLimitClassifier.PrincipalKey(Context("203.0.113.26")));
    }

    [Fact]
    public async Task SameNatPrincipalsHaveIndependentQuotas()
    {
        var settings = SmallSettings(authenticated: 2, preAuthBearer: 100);
        await using var limiter = new PrincipalApiRateLimiter(settings);
        var first = Context("198.51.100.9", companyId: 2, userId: 10);
        var second = Context("198.51.100.9", companyId: 2, userId: 11);

        using var first1 = await limiter.AcquireAsync(first, default);
        using var first2 = await limiter.AcquireAsync(first, default);
        using var firstRejected = await limiter.AcquireAsync(first, default);
        using var second1 = await limiter.AcquireAsync(second, default);

        Assert.True(first1.IsAcquired);
        Assert.True(first2.IsAcquired);
        Assert.False(firstRejected.IsAcquired);
        Assert.True(second1.IsAcquired);
    }

    [Fact]
    public async Task RotatingInvalidBearerTokensShareOneBoundedIpQuota()
    {
        var settings = SmallSettings(authenticated: 100, preAuthBearer: 2);
        await using var limiter = ApiRateLimiterFactory.CreatePreAuthGeneral(settings);

        var first = Context("192.0.2.44");
        first.Request.Path = "/api/vehicles";
        first.Request.Headers.Authorization = "Bearer invalid-one";
        var second = Context("192.0.2.44");
        second.Request.Path = "/api/drivers";
        second.Request.Headers.Authorization = "Bearer invalid-two";
        var third = Context("192.0.2.44");
        third.Request.Path = "/api/fleet/utilization";
        third.Request.Headers.Authorization = "Bearer invalid-three";
        var otherIp = Context("192.0.2.45");
        otherIp.Request.Path = "/api/vehicles";
        otherIp.Request.Headers.Authorization = "Bearer invalid-four";

        using var lease1 = await limiter.AcquireAsync(first, 1);
        using var lease2 = await limiter.AcquireAsync(second, 1);
        using var rejected = await limiter.AcquireAsync(third, 1);
        using var isolated = await limiter.AcquireAsync(otherIp, 1);

        Assert.True(lease1.IsAcquired);
        Assert.True(lease2.IsAcquired);
        Assert.False(rejected.IsAcquired);
        Assert.True(isolated.IsAcquired);
    }

    [Fact]
    public void SensitivePathsRetainDedicatedAbuseClassification()
    {
        Assert.True(ApiRateLimitClassifier.IsLogin("/api/auth/login"));
        Assert.True(ApiRateLimitClassifier.IsRefresh("/api/auth/refresh"));
        Assert.True(ApiRateLimitClassifier.IsDevice("/api/telemetry/ingest"));
        Assert.True(ApiRateLimitClassifier.IsPublic("/api/public/shipments/track/token"));
        Assert.False(ApiRateLimitClassifier.IsSensitive("/api/vehicles"));
    }

    private static DefaultHttpContext Context(string ip, long? companyId = null, long? userId = null)
    {
        var context = new DefaultHttpContext();
        context.Connection.RemoteIpAddress = IPAddress.Parse(ip);
        if (companyId is not null)
            context.Items[EndpointMappings.AuthCompanyIdItemKey] = companyId.Value;
        if (userId is not null)
            context.Items[EndpointMappings.AuthUserIdItemKey] = userId.Value;
        return context;
    }

    private static ApiRateLimitSettings SmallSettings(int authenticated, int preAuthBearer)
        => new(
            AuthenticatedPermitLimit: authenticated,
            PreAuthBearerPermitLimit: preAuthBearer,
            AnonymousPermitLimit: 2,
            LoginPermitLimit: 2,
            RefreshPermitLimit: 2,
            DevicePermitLimit: 2,
            PublicPermitLimit: 2,
            Window: TimeSpan.FromMinutes(1));

    private static int CountOccurrences(string value, string term)
    {
        var count = 0;
        var offset = 0;
        while ((offset = value.IndexOf(term, offset, StringComparison.Ordinal)) >= 0)
        {
            count++;
            offset += term.Length;
        }

        return count;
    }

    private static string ReadProgramSource()
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current is not null)
        {
            var candidate = Path.Combine(current.FullName, "backend-dotnet", "Program.cs");
            if (File.Exists(candidate))
                return File.ReadAllText(candidate);
            current = current.Parent;
        }

        throw new FileNotFoundException("Could not locate backend-dotnet/Program.cs from test output directory.");
    }

    private static readonly string RateLimitSource = ReadRateLimitSource();

    private static string ReadRateLimitSource()
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current is not null)
        {
            var candidate = Path.Combine(current.FullName, "backend-dotnet", "Middleware", "PrincipalRateLimitingMiddleware.cs");
            if (File.Exists(candidate))
                return File.ReadAllText(candidate);
            current = current.Parent;
        }

        throw new FileNotFoundException("Could not locate PrincipalRateLimitingMiddleware.cs from test output directory.");
    }
}
