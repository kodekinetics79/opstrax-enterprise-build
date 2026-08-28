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

    [Fact]
    public void SafetyManagerCanReadTelematicsButCannotManageOrExportDeviceRegistry()
    {
        var safety = Principal("Safety Manager", EndpointMappings.RolePermissionDefaults["Safety Manager"]);
        foreach (var allowed in new[] { "telemetry.devices.read", "telematics:gps:view", "telematics:diagnostics:view", "telematics:sensors:view" })
            Assert.Null(EndpointMappings.RequirePermission(safety, allowed));
        foreach (var forbidden in new[] { "telemetry.devices.manage", "telematics:devices:export" })
        {
            var denied = EndpointMappings.RequirePermission(safety, forbidden);
            Assert.Equal(StatusCodes.Status403Forbidden,
                Assert.IsAssignableFrom<IStatusCodeHttpResult>(denied).StatusCode);
        }
    }

    [Theory]
    [InlineData("Fleet Manager", "telematics:gps:view")]
    [InlineData("Fleet Manager", "telematics:diagnostics:view")]
    [InlineData("Fleet Manager", "telematics:sensors:view")]
    [InlineData("Dispatcher", "telematics:gps:view")]
    [InlineData("Maintenance Manager", "telematics:gps:view")]
    [InlineData("Maintenance Manager", "telematics:diagnostics:view")]
    [InlineData("Maintenance Manager", "telematics:sensors:view")]
    public void OperationalRolesCanOpenTheirShippedTelematicsReadRoutes(string role, string permission)
        => Assert.Null(EndpointMappings.RequirePermission(
            Principal(role, EndpointMappings.RolePermissionDefaults[role]), permission));

    [Theory]
    [InlineData("Driver")]
    [InlineData("Customer")]
    public void PortalAndPrivacyScopedRolesRemainClosedToBackOfficeTelematics(string role)
    {
        var principal = Principal(role, EndpointMappings.RolePermissionDefaults[role]);
        foreach (var permission in new[] { "telemetry.devices.read", "telematics:gps:view", "telematics:diagnostics:view", "telematics:sensors:view" })
        {
            var denied = EndpointMappings.RequirePermission(principal, permission);
            Assert.Equal(StatusCodes.Status403Forbidden,
                Assert.IsAssignableFrom<IStatusCodeHttpResult>(denied).StatusCode);
        }
    }

    [Fact]
    public void ReadOnlyAuditorCanInspectButCannotChangeOrExportTelematics()
    {
        var auditor = Principal("Read-Only Auditor", EndpointMappings.RolePermissionDefaults["Read-Only Auditor"]);
        foreach (var allowed in new[] { "telemetry.devices.read", "telematics:gps:view", "telematics:diagnostics:view", "telematics:sensors:view" })
            Assert.Null(EndpointMappings.RequirePermission(auditor, allowed));
        foreach (var forbidden in new[] { "telemetry.devices.manage", "telematics:devices:export", "telematics:gps:export", "telematics:diagnostics:update", "telematics:sensors:update" })
        {
            var denied = EndpointMappings.RequirePermission(auditor, forbidden);
            Assert.Equal(StatusCodes.Status403Forbidden,
                Assert.IsAssignableFrom<IStatusCodeHttpResult>(denied).StatusCode);
        }
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
