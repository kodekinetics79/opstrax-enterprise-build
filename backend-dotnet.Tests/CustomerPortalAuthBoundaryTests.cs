using Microsoft.AspNetCore.Http;
using Opstrax.Api.Controllers;
using Xunit;

namespace Opstrax.Tests;

// STEP 0 boundary: a customer-portal-bound principal (users.customer_id != null) must be
// rejected OUTRIGHT (403) from internal endpoints — even when its role grants an
// overlapping permission like shipments:view / alerts:view — never a scoped-empty response.
public class CustomerPortalAuthBoundaryTests
{
    // Permissions guarding representative internal endpoints across 7 modules. The two
    // marked (*) are the real overlap: the "Customer"/"Customer Portal User" roles carry
    // shipments:view and alerts:view, so a company_id-only check would have leaked.
    private static readonly string[] InternalPermissions =
    {
        "fleet:view",            // Fleet / /api/vehicles
        "shipments:view",        // Jobs/shipments / /api/jobs (*)
        "customers:view",        // Customers / /api/customers
        "drivers:view",          // Drivers / /api/drivers
        "dispatch:view",         // Dispatch / /api/dispatch/*
        "finance.invoice.read",  // Finance / /api/finance/*
        "alerts:view",           // Alert Center / /api/alerts (*)
    };

    private static DefaultHttpContext InternalUser()
    {
        var http = new DefaultHttpContext();
        http.Items[EndpointMappings.AuthRoleItemKey] = "Tenant Admin";
        http.Items[EndpointMappings.AuthUserIdItemKey] = "42";
        http.Items[EndpointMappings.AuthCompanyIdItemKey] = 1L;
        http.Items[EndpointMappings.AuthPermissionsItemKey] = InternalPermissions.Append("customer_portal:view").ToArray();
        // No AuthCustomerIdItemKey — internal staff.
        return http;
    }

    private static DefaultHttpContext CustomerPortalUser()
    {
        var http = new DefaultHttpContext();
        http.Items[EndpointMappings.AuthRoleItemKey] = "Customer Portal User";
        http.Items[EndpointMappings.AuthUserIdItemKey] = "99";
        http.Items[EndpointMappings.AuthCompanyIdItemKey] = 1L;
        // The real overlapping permissions this role carries.
        http.Items[EndpointMappings.AuthPermissionsItemKey] = new[] { "customer_portal:view", "shipments:view", "alerts:view" };
        http.Items[EndpointMappings.AuthCustomerIdItemKey] = 7L; // bound to a customer
        return http;
    }

    [Fact]
    public void InternalUser_IsAllowed_OnInternalEndpoints()
    {
        var http = InternalUser();
        foreach (var perm in InternalPermissions)
        {
            Assert.Null(EndpointMappings.RequirePermission(http, perm)); // null == allowed
        }
        Assert.Null(EndpointMappings.RequireInternalUser(http));
    }

    [Fact]
    public void CustomerPortalUser_IsRejected_OutrightFromEveryInternalEndpoint()
    {
        var http = CustomerPortalUser();
        foreach (var perm in InternalPermissions)
        {
            var result = EndpointMappings.RequirePermission(http, perm);
            Assert.NotNull(result); // rejected outright (403) — NOT a scoped-empty response
            // The rejection happens before any authorization decision / data query is reached.
            Assert.False(http.Items.ContainsKey("opstrax.authorization.decision"));
        }
        Assert.NotNull(EndpointMappings.RequireInternalUser(http));
        Assert.True(EndpointMappings.IsCustomerPortalPrincipal(http));
    }

    [Fact]
    public void CustomerPortalUser_IsStillAllowed_OnPortalPermissions()
    {
        var http = CustomerPortalUser();
        // The portal principal keeps access to the permission its role actually grants.
        Assert.Null(EndpointMappings.RequirePermission(http, "customer_portal:view"));
    }

    [Fact]
    public void OverlappingPermission_ShipmentsView_DoesNotLetCustomerReachInternalJobs()
    {
        // The customer role HAS shipments:view, but the customer binding still blocks it.
        var http = CustomerPortalUser();
        Assert.Contains("shipments:view", (string[])http.Items[EndpointMappings.AuthPermissionsItemKey]!);
        Assert.NotNull(EndpointMappings.RequirePermission(http, "shipments:view")); // still 403
    }
}
