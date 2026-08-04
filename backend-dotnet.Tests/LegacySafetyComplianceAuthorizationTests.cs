using System.Reflection;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.AspNetCore.Routing;
using Opstrax.Api.Controllers;
using Opstrax.Api.Data;
using Opstrax.Api.Services;

namespace Opstrax.Tests;

public sealed class LegacySafetyComplianceAuthorizationTests
{
    [Fact]
    public async Task CoachingDeleteRouteMaterializesWithoutAnInferredRequestBody()
    {
        var builder = WebApplication.CreateBuilder();
        builder.Services.AddSingleton<Database>(_ => null!);
        builder.Services.AddSingleton<AuditService>(_ => null!);
        await using var app = builder.Build();
        var method = typeof(EndpointMappings).GetMethod("PilotDeleteCoachingTask", BindingFlags.NonPublic | BindingFlags.Static)!;
        var handler = (Func<HttpContext, long, Database, AuditService, CancellationToken, Task<IResult>>)
            method.CreateDelegate(typeof(Func<HttpContext, long, Database, AuditService, CancellationToken, Task<IResult>>));

        app.MapDelete("/api/coaching/tasks/{id:long}", handler);

        var exception = Record.Exception(() => ((IEndpointRouteBuilder)app).DataSources.SelectMany(source => source.Endpoints).ToArray());
        Assert.Null(exception);
    }

    [Theory]
    [InlineData("driver:self")]
    [InlineData("safety:view")]
    [InlineData("dashboard:view")]
    public void UnrelatedPrincipalsCannotReadOrMutateCompliance(string permission)
    {
        var http = Principal([permission]);
        Assert.NotNull(EndpointMappings.RequirePermission(http, "compliance:view"));
        Assert.NotNull(Direct(http, "compliance:update", "compliance:manage"));
        Assert.NotNull(Direct(http, "compliance:manage"));
    }

    [Fact]
    public void ComplianceViewIsReadOnlyAndUpdateCannotCreateTenantWideAuditPackages()
    {
        var reader = Principal(["compliance:view"]);
        Assert.Null(EndpointMappings.RequirePermission(reader, "compliance:view"));
        Assert.NotNull(Direct(reader, "compliance:update", "compliance:manage"));
        Assert.NotNull(Direct(reader, "compliance:manage"));

        var updater = Principal(["compliance:view", "compliance:update"]);
        Assert.Null(Direct(updater, "compliance:update", "compliance:manage"));
        Assert.NotNull(Direct(updater, "compliance:manage"));

        var manager = Principal(["compliance:view", "compliance:manage"]);
        Assert.Null(Direct(manager, "compliance:update", "compliance:manage"));
        Assert.Null(Direct(manager, "compliance:manage"));
    }

    [Fact]
    public async Task RetiredLegacyCoachingRoutesDenyWrongIdentityAndDirectAuthorizedCallersToCanonicalApi()
    {
        var wrongIdentity = Principal(["safety:view"]);
        var denied = InvokeRetired(wrongIdentity, "driver:self");
        Assert.Equal(StatusCodes.Status403Forbidden, await Status(denied));

        var driver = Principal(["driver:self"]);
        var retired = InvokeRetired(driver, "driver:self");
        Assert.Equal(StatusCodes.Status410Gone, await Status(retired));
    }

    [Fact]
    public void BranchBoundSafetyPrincipalsAlwaysReceiveAConcreteSqlScope()
    {
        var tenantWide = Principal(["safety:view"]);
        Assert.Equal(string.Empty, LegacyBranchClause(tenantWide));

        var branchUser = Principal(["safety:view"], branchId: 77);
        Assert.Equal(" AND se.branch_id=@branchId", LegacyBranchClause(branchUser));
    }

    [Fact]
    public void ComplianceRouteRegistrationsHaveExplicitLeastPrivilegeAndBranchContracts()
    {
        var source = Source();
        foreach (var route in new[]
        {
            "/api/compliance/summary", "/api/compliance/profiles", "/api/compliance/rules",
            "/api/compliance/violations", "/api/compliance/violations/{id:long}",
            "/api/compliance/violations/{id:long}/acknowledge", "/api/compliance/violations/{id:long}/resolve",
            "/api/compliance/documents", "/api/compliance/audit-packages",
            "/api/compliance/audit-packages/{id:long}", "/api/compliance/audit-packages/{id:long}/finalize",
            "/api/compliance/cross-border-watch", "/api/compliance/driver-status",
            "/api/compliance/vehicle-status", "/api/compliance/ai/recommendations"
        })
        {
            var start = source.IndexOf($"\"{route}\"", StringComparison.Ordinal);
            Assert.True(start >= 0, $"Missing compliance route {route}");
            var registration = source.Substring(start, Math.Min(1500, source.Length - start));
            Assert.Contains("Require", registration, StringComparison.Ordinal);
        }

        Assert.Contains("(@branchId::BIGINT IS NULL OR cv.branch_id=@branchId)", source, StringComparison.Ordinal);
        Assert.Contains("AND @branchId::BIGINT IS NULL ORDER BY cap.created_at", source, StringComparison.Ordinal);
        Assert.Contains("module_key='compliance' AND @branchId::BIGINT IS NULL", source, StringComparison.Ordinal);
    }

    [Fact]
    public void LegacySafetyMutationsUseDirectPermissionsCanonicalCreateAndOptimisticConcurrency()
    {
        var source = Source();
        Assert.Contains("RequireAnyDirectPermission(http, \"safety:create\", \"safety:manage\")", source, StringComparison.Ordinal);
        Assert.Contains("'New','Not Created','None'", source, StringComparison.Ordinal);
        Assert.Contains("rowVersion is required for safe updates", source, StringComparison.Ordinal);
        Assert.Contains("AND row_version=@rowVersion", source, StringComparison.Ordinal);
        Assert.Contains("row_version=row_version+1", source, StringComparison.Ordinal);
        Assert.Contains("Use the guarded safety-event workflow actions to change lifecycle status", source, StringComparison.Ordinal);
        Assert.Contains("module_key='safety' AND @branchId::BIGINT IS NULL", source, StringComparison.Ordinal);
        Assert.Contains("MapPost(\"/api/safety/events/{id:long}/coaching\", CanonicalCoachingFromSafetyEvent)", source, StringComparison.Ordinal);
        Assert.Contains("CanonicalCoachingFromDashcamEvent(http, id, body, db, audit, ct)", source, StringComparison.Ordinal);
        Assert.Contains("PilotCreateCoachingTask(http, payload, db, audit, ct)", source, StringComparison.Ordinal);
    }

    private static DefaultHttpContext Principal(string[] permissions, long? branchId = null)
    {
        var http = new DefaultHttpContext();
        http.Items[EndpointMappings.AuthUserIdItemKey] = 42L;
        http.Items[EndpointMappings.AuthCompanyIdItemKey] = 9L;
        http.Items[EndpointMappings.AuthRoleItemKey] = "Test Principal";
        http.Items[EndpointMappings.AuthPermissionsItemKey] = permissions;
        if (branchId is not null) http.Items[EndpointMappings.AuthBranchIdItemKey] = branchId.Value;
        return http;
    }

    private static IResult? Direct(HttpContext http, params string[] permissions)
        => (IResult?)typeof(EndpointMappings)
            .GetMethod("RequireAnyDirectPermission", BindingFlags.NonPublic | BindingFlags.Static)!
            .Invoke(null, [http, permissions]);

    private static IResult InvokeRetired(HttpContext http, string permission)
        => (IResult)typeof(EndpointMappings)
            .GetMethod("RetiredLegacyCoachingRoute", BindingFlags.NonPublic | BindingFlags.Static)!
            .Invoke(null, [http, permission, "Use the canonical coaching API."])!;

    private static string LegacyBranchClause(HttpContext http)
        => (string)typeof(EndpointMappings)
            .GetMethod("LegacySafetyBranchScope", BindingFlags.NonPublic | BindingFlags.Static)!
            .Invoke(null, [http, "se"])!;

    private static async Task<int> Status(IResult result)
    {
        var http = new DefaultHttpContext
        {
            RequestServices = new ServiceCollection().AddLogging().BuildServiceProvider()
        };
        http.Response.Body = new MemoryStream();
        await result.ExecuteAsync(http);
        return http.Response.StatusCode;
    }

    private static string Source()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !Directory.Exists(Path.Combine(dir.FullName, "backend-dotnet"))) dir = dir.Parent;
        Assert.NotNull(dir);
        return File.ReadAllText(Path.Combine(dir!.FullName, "backend-dotnet", "Controllers", "EndpointMappings.cs"));
    }
}
