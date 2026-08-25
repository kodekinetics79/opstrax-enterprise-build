using System.Reflection;
using Microsoft.AspNetCore.Http;
using Opstrax.Api.Controllers;

namespace Opstrax.Tests;

/// <summary>
/// Certification recovery regressions for AUD-004. A missing customer binding must
/// never convert a customer-portal identity into an internal tenant user.
/// </summary>
public sealed class Phase1CustomerIsolationRegressionTests
{
    [Fact]
    public void PortalPermissionShapeWithoutBinding_IsStillClassifiedAsPortal()
    {
        var http = UnboundPortalPrincipal();

        Assert.True(EndpointMappings.IsCustomerPortalPrincipal(http));
    }

    [Theory]
    [InlineData("shipments:view")]
    [InlineData("alerts:view")]
    public void PortalPermissionShapeWithoutBinding_IsDeniedByInternalPermissionGate(string permission)
    {
        var http = UnboundPortalPrincipal();

        var denied = EndpointMappings.RequirePermission(http, permission);

        AssertForbidden(denied);
    }

    [Fact]
    public void PortalPermissionShapeWithoutBinding_IsDeniedByDirectJobsGate()
    {
        var gate = typeof(EndpointMappings).GetMethod(
            "RequireAnyDirectPermission",
            BindingFlags.NonPublic | BindingFlags.Static)
            ?? throw new InvalidOperationException("Direct permission gate was not found.");
        var http = UnboundPortalPrincipal();

        var denied = (IResult?)gate.Invoke(null, [http, new[] { "shipments:view" }]);

        AssertForbidden(denied);
    }

    [Fact]
    public void InternalUserWithPortalPreviewGrant_RemainsInternal()
    {
        var http = Principal("Customer Service",
            ["customer_portal:view", "customers:view", "dispatch:view", "crm:view"]);

        Assert.False(EndpointMappings.IsCustomerPortalPrincipal(http));
        Assert.Null(EndpointMappings.RequirePermission(http, "dispatch:view"));
    }

    [Fact]
    public void TenantWideCustomerManagementRoutes_RequireInternalPrincipal()
    {
        var source = ReadEndpointSource();
        var routeStart = source.IndexOf("app.MapGet(\"/api/customer-eta/recommendations\"", StringComparison.Ordinal);
        var routeEnd = source.IndexOf("// Public token endpoints", routeStart, StringComparison.Ordinal);
        Assert.True(routeStart >= 0 && routeEnd > routeStart);
        var managementRoutes = source[routeStart..routeEnd];
        Assert.Equal(6, Count(managementRoutes, "RequireInternalUser(http)"));

        foreach (var method in new[]
                 {
                     "private static async Task<IResult> CustomerEtaSummary(",
                     "private static Task<IResult> CustomerEtaCommunications("
                 })
        {
            var methodStart = source.IndexOf(method, StringComparison.Ordinal);
            Assert.True(methodStart >= 0, $"Missing handler declaration: {method}");
            var methodEnd = source.IndexOf("\n    private static ", methodStart, StringComparison.Ordinal);
            var block = methodEnd < 0 ? source[methodStart..] : source[methodStart..methodEnd];
            Assert.Contains("RequireInternalUser(http)", block, StringComparison.Ordinal);
        }
    }

    private static DefaultHttpContext UnboundPortalPrincipal() => Principal(
        "Customer Portal User",
        ["customer_portal:view", "shipments:view", "alerts:view"]);

    private static string ReadEndpointSource()
    {
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (root is not null && !Directory.Exists(Path.Combine(root.FullName, "backend-dotnet")))
            root = root.Parent;
        Assert.NotNull(root);
        return File.ReadAllText(Path.Combine(root!.FullName, "backend-dotnet", "Controllers", "EndpointMappings.cs"));
    }

    private static int Count(string source, string value)
    {
        var count = 0;
        for (var index = 0; (index = source.IndexOf(value, index, StringComparison.Ordinal)) >= 0; index += value.Length)
            count++;
        return count;
    }

    private static DefaultHttpContext Principal(string role, string[] permissions)
    {
        var http = new DefaultHttpContext();
        http.Items[EndpointMappings.AuthRoleItemKey] = role;
        http.Items[EndpointMappings.AuthUserIdItemKey] = 91L;
        http.Items[EndpointMappings.AuthCompanyIdItemKey] = 17L;
        http.Items[EndpointMappings.AuthPermissionsItemKey] = permissions;
        return http;
    }

    private static void AssertForbidden(IResult? result)
    {
        Assert.NotNull(result);
        Assert.Equal(StatusCodes.Status403Forbidden,
            Assert.IsAssignableFrom<IStatusCodeHttpResult>(result).StatusCode);
    }
}

/// <summary>
/// Certification recovery regressions for AUD-029. Each analytics handler must make
/// an explicit branch-scope decision: apply a branch predicate through a shared or
/// local scope helper, or fail closed for branch-bound principals when the dataset
/// has no defensible branch ownership model.
/// </summary>
public sealed class Phase1AnalyticsBranchIsolationRegressionTests
{
    [Fact]
    public void AnalyticsScopeGate_DeniesBranchPrincipal()
    {
        var http = new DefaultHttpContext();
        http.Items[EndpointMappings.AuthBranchIdItemKey] = 27L;

        var denied = InvokeAnalyticsScopeGate(http);

        Assert.NotNull(denied);
        Assert.Equal(StatusCodes.Status403Forbidden,
            Assert.IsAssignableFrom<IStatusCodeHttpResult>(denied).StatusCode);
    }

    [Fact]
    public void AnalyticsScopeGate_AllowsTenantWidePrincipal()
    {
        var http = new DefaultHttpContext();

        Assert.Null(InvokeAnalyticsScopeGate(http));
    }

    [Fact]
    public void AnalyticsScopeGate_DeniesCustomerPortalPrincipal()
    {
        var http = new DefaultHttpContext();
        http.Items[EndpointMappings.AuthPermissionsItemKey] =
            new[] { "customer_portal:view", "shipments:view", "alerts:view" };

        var denied = InvokeAnalyticsScopeGate(http);

        Assert.NotNull(denied);
        Assert.Equal(StatusCodes.Status403Forbidden,
            Assert.IsAssignableFrom<IStatusCodeHttpResult>(denied).StatusCode);
    }

    [Theory]
    [InlineData("AnalyticsExecutive")]
    [InlineData("AnalyticsOperations")]
    [InlineData("AnalyticsDispatch")]
    [InlineData("AnalyticsSafety")]
    [InlineData("AnalyticsMaintenance")]
    [InlineData("AnalyticsCustomer")]
    [InlineData("AnalyticsTrends")]
    [InlineData("AnalyticsInsights")]
    public void AnalyticsHandler_MakesAnExplicitBranchScopeDecision(string methodName)
    {
        var source = ReadSource("backend-dotnet", "Controllers", "EndpointMappings.cs");
        var method = MethodBlock(source, methodName);

        var readsBranchContext = method.Contains("GetBranchId(http)", StringComparison.Ordinal)
            || method.Contains("StrictBranchFilter(http", StringComparison.Ordinal)
            || method.Contains("AnalyticsBranchScope", StringComparison.Ordinal)
            || method.Contains("BranchScopedAnalytics", StringComparison.Ordinal);
        var appliesBranchPredicate = method.Contains("@branch", StringComparison.OrdinalIgnoreCase)
            || method.Contains("AnalyticsBranchScope", StringComparison.Ordinal)
            || method.Contains("BranchScopedAnalytics", StringComparison.Ordinal);
        var explicitlyDeniesBranchScope = readsBranchContext
            && (method.Contains("Results.Forbid", StringComparison.Ordinal)
                || method.Contains("Status403Forbidden", StringComparison.Ordinal)
                || method.Contains("branch-scoped", StringComparison.OrdinalIgnoreCase));

        Assert.True(readsBranchContext && (appliesBranchPredicate || explicitlyDeniesBranchScope),
            $"{methodName} has company scoping but no explicit branch predicate or fail-closed branch decision.");
    }

    private static string ReadSource(params string[] parts)
    {
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (root is not null && !Directory.Exists(Path.Combine(root.FullName, "backend-dotnet")))
            root = root.Parent;
        Assert.NotNull(root);
        return File.ReadAllText(Path.Combine([root!.FullName, .. parts]));
    }

    private static IResult? InvokeAnalyticsScopeGate(HttpContext http)
    {
        var gate = typeof(EndpointMappings).GetMethod(
            "RequireAnalyticsBranchScope",
            BindingFlags.NonPublic | BindingFlags.Static)
            ?? throw new InvalidOperationException("Analytics branch-scope gate was not found.");
        return (IResult?)gate.Invoke(null, [http]);
    }

    private static string MethodBlock(string source, string name)
    {
        var start = source.IndexOf($"private static async Task<IResult> {name}(", StringComparison.Ordinal);
        Assert.True(start >= 0, $"Missing method {name}");
        var next = source.IndexOf("\n    private static ", start + name.Length, StringComparison.Ordinal);
        return next < 0 ? source[start..] : source[start..next];
    }
}
