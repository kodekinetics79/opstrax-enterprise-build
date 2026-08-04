using Microsoft.AspNetCore.Http;
using Opstrax.Api.Controllers;

namespace Opstrax.Tests;

public sealed class FleetTmsAuthorizationTests
{
    [Theory]
    [InlineData("fleet:view")]
    [InlineData("fleet:manage")]
    [InlineData("operations.proof.validate")]
    public void FleetManager_IsAllowedForFleetWorkspaceResponsibilities(string permission)
    {
        var http = Principal("Fleet Manager", EndpointMappings.RolePermissionDefaults["Fleet Manager"]);

        Assert.Null(EndpointMappings.RequirePermission(http, permission));
    }

    [Theory]
    [InlineData("compliance:view")]
    [InlineData("compliance:manage")]
    public void ComplianceManager_IsAllowedForSaudiReadinessResponsibilities(string permission)
    {
        var http = Principal("Compliance Manager", EndpointMappings.RolePermissionDefaults["Compliance Manager"]);

        Assert.Null(EndpointMappings.RequirePermission(http, permission));
    }

    [Theory]
    [InlineData("fleet:view")]
    [InlineData("fleet:manage")]
    [InlineData("operations.proof.validate")]
    [InlineData("compliance:view")]
    [InlineData("compliance:manage")]
    public void LowPrivilegePrincipal_IsDeniedFleetAndComplianceResponsibilities(string permission)
    {
        var http = Principal("Read Only", ["dashboard:view"]);

        Assert.NotNull(EndpointMappings.RequirePermission(http, permission));
    }

    [Fact]
    public void CustomerPortalPrincipal_IsDeniedEvenWithOverlappingFleetPermission()
    {
        var http = Principal("Customer Portal User", ["customer_portal:view", "fleet:view"]);
        http.Items[EndpointMappings.AuthCustomerIdItemKey] = 7L;

        Assert.NotNull(EndpointMappings.RequirePermission(http, "fleet:view"));
    }

    [Fact]
    public void MainFleetTmsRouteRegistrationsAreServerPermissionGuarded()
    {
        var source = ReadSource("backend-dotnet", "Controllers", "FleetTmsEndpoints.cs");
        var registrations = source.Split('\n')
            .Where(line => line.Contains("app.Map", StringComparison.Ordinal)
                           && line.Contains("/api/fleet-tms/", StringComparison.Ordinal));

        Assert.All(registrations, line => Assert.True(
            line.Contains("Guard(app.Map", StringComparison.Ordinal) || line.Contains("GuardDriverTask(app.Map", StringComparison.Ordinal),
            $"Route is not guarded: {line}"));
    }

    [Fact]
    public void SensitiveProofApprovalRequiresManageButSecretFreeTrackingMetadataAllowsView()
    {
        var source = ReadSource("backend-dotnet", "Controllers", "FleetTmsEndpoints.cs");

        Assert.Contains("pod/{podId:long}/verify\", VerifyPod), \"fleet:manage\"", source, StringComparison.Ordinal);
        Assert.Contains("pod/{podId:long}/reject\", RejectPod), \"fleet:manage\"", source, StringComparison.Ordinal);
        Assert.Contains("tracking-link\", GetTrackingLinks), \"fleet:view\"", source, StringComparison.Ordinal);
        Assert.Contains("tracking-link\", CreateTrackingLink), \"fleet:manage\"", source, StringComparison.Ordinal);
        Assert.Contains("tracking-link/{linkId:long}\", RevokeTrackingLink), \"fleet:manage\"", source, StringComparison.Ordinal);
    }

    [Fact]
    public void ColdChainAssetAndSaudiRouteRegistrationsAreServerPermissionGuarded()
    {
        var source = ReadSource("backend-dotnet", "Controllers", "FleetTmsColdChainEndpoints.cs");
        var registrations = source.Split('\n')
            .Where(line => line.Contains("app.Map", StringComparison.Ordinal)
                           && (line.Contains("/api/fleet-tms/cold-chain", StringComparison.Ordinal)
                               || line.Contains("/api/fleet-tms/assets", StringComparison.Ordinal)
                               || line.Contains("/api/fleet-tms/saudi", StringComparison.Ordinal)
                               || line.Contains("/api/fleet-tms/compliance", StringComparison.Ordinal)
                               || line.Contains("/api/fleet-tms/vat", StringComparison.Ordinal)));

        Assert.NotEmpty(registrations);
        Assert.All(registrations, line => Assert.Contains("Guard(app.Map", line, StringComparison.Ordinal));
    }

    [Fact]
    public void FleetWorkspaceUsesBranchOwnershipInsteadOfFailClosedGuard()
    {
        var source = ReadSource("backend-dotnet", "Controllers", "FleetTmsEndpoints.cs");
        Assert.DoesNotContain("Fleet TMS is not available for branch-scoped accounts.", source, StringComparison.Ordinal);
        Assert.Contains("private static string Owned(HttpContext http", source, StringComparison.Ordinal);
        Assert.Contains("branch_id=@branchId", source, StringComparison.Ordinal);
    }

    private static DefaultHttpContext Principal(string role, string[] permissions)
    {
        var http = new DefaultHttpContext();
        http.Items[EndpointMappings.AuthRoleItemKey] = role;
        http.Items[EndpointMappings.AuthUserIdItemKey] = 42L;
        http.Items[EndpointMappings.AuthCompanyIdItemKey] = 1L;
        http.Items[EndpointMappings.AuthPermissionsItemKey] = permissions;
        return http;
    }

    private static string ReadSource(params string[] parts)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !Directory.Exists(Path.Combine(dir.FullName, "backend-dotnet")))
            dir = dir.Parent;
        Assert.NotNull(dir);
        return File.ReadAllText(Path.Combine([dir!.FullName, .. parts]));
    }
}
