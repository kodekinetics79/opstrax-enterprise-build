using Opstrax.Api.Controllers;
using Microsoft.AspNetCore.Http;

namespace Opstrax.Api.Tests;

public sealed class AdminRolePermissionCatalogTests
{
    [Theory]
    [InlineData("telemetry.devices.manage")]
    [InlineData("telematics:devices:view")]
    [InlineData("telematics:devices:diagnostics")]
    [InlineData("telematics:devices:export")]
    [InlineData("telematics:gps:view")]
    public void NarrowTelematicsPermissionsRemainAvailableToCustomRoles(string permission)
    {
        Assert.Contains(permission, EndpointMappings.CustomRolePermissionCatalog,
            StringComparer.OrdinalIgnoreCase);
    }

    [Fact]
    public void FleetManagerCanExportDevicesButDispatcherCannot()
    {
        Assert.Null(EndpointMappings.RequirePermission(
            Principal("Fleet Manager", EndpointMappings.RolePermissionDefaults["Fleet Manager"]),
            "telematics:devices:export"));

        var denied = EndpointMappings.RequirePermission(
            Principal("Dispatcher", EndpointMappings.RolePermissionDefaults["Dispatcher"]),
            "telematics:devices:export");
        Assert.Equal(StatusCodes.Status403Forbidden,
            Assert.IsAssignableFrom<IStatusCodeHttpResult>(denied).StatusCode);
    }

    [Fact]
    public void DispatcherCanReadDevicesButCannotManageOrExportThem()
    {
        var dispatcher = Principal("Dispatcher", EndpointMappings.RolePermissionDefaults["Dispatcher"]);

        Assert.Null(EndpointMappings.RequirePermission(dispatcher, "telemetry.devices.read"));
        foreach (var forbidden in new[] { "telemetry.devices.manage", "telematics:devices:export" })
        {
            var denied = EndpointMappings.RequirePermission(dispatcher, forbidden);
            Assert.Equal(StatusCodes.Status403Forbidden,
                Assert.IsAssignableFrom<IStatusCodeHttpResult>(denied).StatusCode);
        }
    }

    [Theory]
    [InlineData("telematics:devices:view")]
    [InlineData("telematics:devices:export")]
    public void SafetyManagerCannotViewOrExportDeviceRegistry(string permission)
    {
        var denied = EndpointMappings.RequirePermission(
            Principal("Safety Manager", EndpointMappings.RolePermissionDefaults["Safety Manager"]),
            permission);
        Assert.Equal(StatusCodes.Status403Forbidden,
            Assert.IsAssignableFrom<IStatusCodeHttpResult>(denied).StatusCode);
    }

    private static DefaultHttpContext Principal(string role, string[] permissions)
    {
        var http = new DefaultHttpContext();
        http.Items[EndpointMappings.AuthUserIdItemKey] = 17L;
        http.Items[EndpointMappings.AuthCompanyIdItemKey] = 23L;
        http.Items[EndpointMappings.AuthRoleItemKey] = role;
        http.Items[EndpointMappings.AuthPermissionsItemKey] = permissions;
        return http;
    }
}
