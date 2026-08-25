using Opstrax.Api.Controllers;

namespace Opstrax.Api.Tests;

public sealed class AdminRolePermissionCatalogTests
{
    [Theory]
    [InlineData("telemetry.devices.manage")]
    [InlineData("telematics:devices:view")]
    [InlineData("telematics:devices:diagnostics")]
    [InlineData("telematics:gps:view")]
    public void NarrowTelematicsPermissionsRemainAvailableToCustomRoles(string permission)
    {
        Assert.Contains(permission, EndpointMappings.CustomRolePermissionCatalog,
            StringComparer.OrdinalIgnoreCase);
    }
}
