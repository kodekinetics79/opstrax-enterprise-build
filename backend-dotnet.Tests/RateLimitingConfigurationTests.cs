namespace Opstrax.Tests;

public sealed class RateLimitingConfigurationTests
{
    private static readonly string ProgramSource = ReadProgramSource();

    [Fact]
    public void UsesFrameworkPartitionedLimitersInsteadOfCustomUnboundedDictionary()
    {
        Assert.Contains("builder.Services.AddRateLimiter", ProgramSource);
        Assert.Contains("PartitionedRateLimiter.CreateChained", ProgramSource);
        Assert.Contains("RateLimitPartition.GetFixedWindowLimiter", ProgramSource);
        Assert.DoesNotContain("ConcurrentDictionary<string, (DateTimeOffset WindowStart, int Count)>", ProgramSource);
        Assert.DoesNotContain("rateWindows.AddOrUpdate", ProgramSource);
    }

    [Fact]
    public void AppliesStrictLoginAndGeneralApiPoliciesWithoutQueues()
    {
        Assert.Contains("const int apiPermitLimit = 240;", ProgramSource);
        Assert.Contains("const int loginPermitLimit = 10;", ProgramSource);
        Assert.Contains("path.Equals(\"/api/auth/login\"", ProgramSource);
        Assert.Contains("path.Equals(\"/api/platform/auth/login\"", ProgramSource);
        Assert.Equal(2, CountOccurrences(ProgramSource, "QueueLimit = 0"));
    }

    [Fact]
    public void ExemptsOnlyNonApiAndHealthProbeTrafficFromGeneralPolicy()
    {
        Assert.Contains("!context.Request.Path.StartsWithSegments(\"/api\"", ProgramSource);
        Assert.Contains("path.Equals(\"/api/health\"", ProgramSource);
        Assert.Contains("path.Equals(\"/api/ready\"", ProgramSource);
        Assert.Contains("path.StartsWithSegments(\"/health\"", ProgramSource);
        Assert.Contains("RateLimitPartition.GetNoLimiter(\"unlimited\")", ProgramSource);
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
}
