using System;
using Opstrax.Api.Controllers;
using Opstrax.Api.Services;
using Xunit;

namespace Opstrax.Tests;

// Sprint 2 — market-pack engine. The repo's test harness is pure-logic (no live
// DB), so these cover the unit-testable pieces: expiry-status computation, the
// pack→module mapping that drives deny-by-default gating, and the RBAC boundary
// that keeps regional compliance management away from low-permission roles.
public class MarketPackExpiryTests
{
    private static readonly DateTime Today = new(2026, 06, 26);

    [Fact] public void NullExpiry_Is_Valid() => Assert.Equal("valid", MarketPackEndpoints.ExpiryStatus(null, Today));
    [Fact] public void FarFuture_Is_Valid() => Assert.Equal("valid", MarketPackEndpoints.ExpiryStatus(Today.AddDays(31), Today));
    [Fact] public void Within30Days_Is_Expiring() => Assert.Equal("expiring", MarketPackEndpoints.ExpiryStatus(Today.AddDays(30), Today));
    [Fact] public void Today_Is_Expiring() => Assert.Equal("expiring", MarketPackEndpoints.ExpiryStatus(Today, Today));
    [Fact] public void Past_Is_Expired() => Assert.Equal("expired", MarketPackEndpoints.ExpiryStatus(Today.AddDays(-1), Today));
}

public class MarketPackMappingTests
{
    [Fact]
    public void Pack_Maps_To_Market_Module_Key()
    {
        Assert.Equal("market.canada_na", MarketPackSchemaService.ModuleKeyForPack(MarketPackSchemaService.Packs.CanadaNa));
        Assert.Equal("market.saudi_gcc", MarketPackSchemaService.ModuleKeyForPack(MarketPackSchemaService.Packs.SaudiGcc));
    }

    [Fact]
    public void Unknown_Pack_Falls_Back_To_Namespaced_Key()
        => Assert.Equal("market.brazil", MarketPackSchemaService.ModuleKeyForPack("brazil"));

    [Fact]
    public void Feature_Keys_Match_Spec()
    {
        Assert.Equal("market.canada_na", MarketPackSchemaService.Features.MarketCanadaNa);
        Assert.Equal("market.saudi_gcc", MarketPackSchemaService.Features.MarketSaudiGcc);
        Assert.Equal("compliance.documents", MarketPackSchemaService.Features.Documents);
        Assert.Equal("compliance.inspections", MarketPackSchemaService.Features.Inspections);
        Assert.Equal("compliance.tax_readiness", MarketPackSchemaService.Features.TaxReadiness);
    }
}

public class MarketPackRbacTests
{
    private static string[] Role(string n) =>
        EndpointMappings.RolePermissionDefaults.TryGetValue(n, out var p) ? p : Array.Empty<string>();

    [Theory]
    [InlineData("Fleet Manager")]
    [InlineData("Compliance Manager")]
    [InlineData("Tenant Admin")]
    public void Compliance_Managers_Can_Manage_Regional_Compliance(string role)
        => Assert.True(EndpointMappings.HasPermission(Role(role), "compliance:manage"));

    [Theory]
    [InlineData("Customer Viewer")]
    [InlineData("Driver")]
    [InlineData("Carrier Partner")]
    public void LowPermission_Roles_Cannot_Manage_Regional_Compliance(string role)
        => Assert.False(EndpointMappings.HasPermission(Role(role), "compliance:manage"));
}
