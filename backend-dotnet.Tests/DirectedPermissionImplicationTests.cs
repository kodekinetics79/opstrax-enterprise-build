using Microsoft.AspNetCore.Http;
using Opstrax.Api.Controllers;
using Xunit;

namespace Opstrax.Tests;

/// <summary>
/// AUD-003 regression: permission inheritance is directed. These witnesses all
/// succeeded on b982ef8 because sibling actions were symmetric alias classes.
/// </summary>
public sealed class DirectedPermissionImplicationTests
{
    [Theory]
    [InlineData("drivers:create", "drivers:delete")]
    [InlineData("vehicles:update", "vehicles:delete")]
    [InlineData("shipments:create", "shipments:delete")]
    [InlineData("dispatch:create", "dispatch:cancel")]
    [InlineData("customers:update", "customers:delete")]
    [InlineData("safety:create", "safety:review")]
    [InlineData("maintenance:update", "maintenance:close")]
    [InlineData("compliance:export", "compliance:manage")]
    [InlineData("alerts:acknowledge", "alerts:close")]
    [InlineData("reports:export", "reports:manage")]
    [InlineData("users:create", "users:delete")]
    [InlineData("roles:create", "roles:update")]
    [InlineData("telematics:devices:update", "telemetry.devices.manage")]
    [InlineData("dispatch.smart_assign.read", "dispatch.smart_assign.accept")]
    [InlineData("dispatch:assign", "dispatch.smart_assign.recommend")]
    [InlineData("dispatch:assign", "dispatch.smart_assign.accept")]
    [InlineData("dispatch:manage", "operations.site_access.create")]
    [InlineData("driver:self", "operations.access_document.create")]
    [InlineData("operations.site_access.read", "operations.site_access.create")]
    [InlineData("operations.access_document.read", "operations.access_document.verify")]
    [InlineData("operations.access_document.update", "operations.access_document.verify")]
    [InlineData("operations.pickup_authorization.update", "operations.pickup_authorization.verify")]
    [InlineData("operations.proof.read", "operations.proof.validate")]
    [InlineData("operations.proof.create", "operations.proof.update")]
    [InlineData("operations.proof.create", "operations.proof.submit")]
    [InlineData("finance.invoice.issue", "finance.invoice.approve")]
    [InlineData("settlement.create", "settlement.pay")]
    [InlineData("tax.update", "tax.publish")]
    [InlineData("revrec.update", "revrec.period.close")]
    public void NarrowGrant_DoesNotSatisfySiblingMutation(string held, string required)
    {
        var denied = EndpointMappings.RequirePermission(Principal(held), required);
        Assert.NotNull(denied);
        Assert.Equal(StatusCodes.Status403Forbidden,
            Assert.IsAssignableFrom<IStatusCodeHttpResult>(denied).StatusCode);
    }

    [Theory]
    [InlineData("fleet:manage", "vehicles:create")]
    [InlineData("fleet:manage", "vehicles:delete")]
    [InlineData("drivers:manage", "drivers:assign")]
    [InlineData("shipments:manage", "shipments:delete")]
    [InlineData("dispatch:manage", "dispatch:cancel")]
    [InlineData("maintenance:manage", "maintenance:close")]
    [InlineData("compliance:manage", "compliance:export")]
    [InlineData("alerts:manage", "alerts:close")]
    [InlineData("reports:manage", "reports:export")]
    [InlineData("users:manage", "users:delete")]
    [InlineData("roles:manage", "roles:update")]
    [InlineData("settings:manage", "settings:update")]
    [InlineData("finance:manage", "finance.invoice.approve")]
    [InlineData("finance:manage", "settlement.pay")]
    public void ApprovedManageGrant_StillSatisfiesNarrowAction(string held, string required)
        => Assert.Null(EndpointMappings.RequirePermission(Principal(held), required));

    [Fact]
    public void DispatcherFallback_DoesNotAcquireUnlistedDeleteCloseOrManageGrants()
    {
        var dispatcher = EndpointMappings.RolePermissionDefaults["Dispatcher"];
        AssertDenied(dispatcher, "shipments:delete");
        AssertDenied(dispatcher, "alerts:close");
        AssertDenied(dispatcher, "dispatch:manage");
        AssertDenied(dispatcher, "alerts:manage");
    }

    private static void AssertDenied(string[] held, string required)
        => Assert.NotNull(EndpointMappings.RequirePermission(Principal(held), required));

    private static DefaultHttpContext Principal(params string[] permissions)
    {
        var http = new DefaultHttpContext();
        http.Items[EndpointMappings.AuthCompanyIdItemKey] = 4242L;
        http.Items[EndpointMappings.AuthUserIdItemKey] = 99L;
        http.Items[EndpointMappings.AuthRoleItemKey] = "AUD-003 probe";
        http.Items[EndpointMappings.AuthPermissionsItemKey] = permissions;
        return http;
    }
}
