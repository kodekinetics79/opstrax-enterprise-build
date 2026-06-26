using Opstrax.Api.Controllers;
using Xunit;

namespace Opstrax.Tests;

// Sprint 1 — fleet.* RBAC taxonomy + fleet personas (Phase 3) and the entitlement
// alias resolution that lets new fleet.* keys be satisfied by the tokens existing
// roles already hold.
public class FleetRbacTaxonomyTests
{
    private static string[] Role(string name) =>
        EndpointMappings.RolePermissionDefaults.TryGetValue(name, out var p) ? p : System.Array.Empty<string>();

    [Theory]
    [InlineData("Fleet Owner")]
    [InlineData("Carrier Partner")]
    [InlineData("Finance/Billing User")]
    [InlineData("Customer Viewer")]
    public void New_Fleet_Personas_Are_Registered(string role)
    {
        Assert.True(EndpointMappings.RolePermissionDefaults.ContainsKey(role), $"{role} persona must exist");
        Assert.NotEmpty(Role(role));
    }

    [Fact]
    public void FleetManager_Holding_Granular_Tokens_Satisfies_FleetTaxonomy()
    {
        var perms = Role("Fleet Manager");
        // shipments:update (held) must satisfy the new canonical fleet.shipments.manage
        Assert.True(EndpointMappings.HasPermission(perms, "fleet.shipments.manage"));
        Assert.True(EndpointMappings.HasPermission(perms, "fleet.read"));
        Assert.True(EndpointMappings.HasPermission(perms, "fleet.carriers.manage"));
        Assert.True(EndpointMappings.HasPermission(perms, "fleet.fuel.view"));
    }

    [Fact]
    public void CustomerViewer_Cannot_Manage_Carriers_Or_Fuel()
    {
        var perms = Role("Customer Viewer");
        Assert.True(EndpointMappings.HasPermission(perms, "fleet.shipments.view"));
        Assert.False(EndpointMappings.HasPermission(perms, "fleet.carriers.manage"));
        Assert.False(EndpointMappings.HasPermission(perms, "fleet.fuel.manage"));
        Assert.False(EndpointMappings.HasPermission(perms, "fleet.billing.manage"));
    }

    [Fact]
    public void FinanceBillingUser_Can_Manage_Billing_But_Not_Shipments()
    {
        var perms = Role("Finance/Billing User");
        Assert.True(EndpointMappings.HasPermission(perms, "fleet.billing.manage"));
        Assert.False(EndpointMappings.HasPermission(perms, "fleet.shipments.manage"));
    }

    [Fact]
    public void CarrierPartner_Is_Read_Only_On_Carriers()
    {
        var perms = Role("Carrier Partner");
        Assert.True(EndpointMappings.HasPermission(perms, "fleet.carriers.view"));
        Assert.False(EndpointMappings.HasPermission(perms, "fleet.carriers.manage"));
    }
}
