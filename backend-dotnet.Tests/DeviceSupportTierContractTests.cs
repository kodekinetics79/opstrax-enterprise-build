namespace Opstrax.Tests;

public sealed class DeviceSupportTierContractTests
{
    private static readonly string Migration = Read("database", "migrations", "2026_09_07_stage126_device_support_tier_history.sql");
    private static readonly string Endpoint = Read("backend-dotnet", "Controllers", "DeviceSupportTierEndpoints.cs");
    private static readonly string Routes = Read("backend-dotnet", "Controllers", "EndpointMappings.cs");
    private static readonly string Service = Read("frontend", "src", "services", "telematicsService.ts");
    private static readonly string Page = Read("frontend", "src", "pages", "IotDevicesPage.tsx");

    [Fact]
    public void DatabaseOwnsAppendOnlyExactDeviceRoutingWithoutPromotedClaims()
    {
        Assert.Contains("Device support-tier events are append-only", Migration, StringComparison.Ordinal);
        Assert.Contains("First device support-tier event must assign a tier", Migration, StringComparison.Ordinal);
        Assert.Contains("Support-tier change must alter the routing plan", Migration, StringComparison.Ordinal);
        Assert.Contains("ck_stage126_device_support_tier_active", Migration, StringComparison.Ordinal);
        Assert.Contains("commercial_entitlement_verified_claim=FALSE", Migration, StringComparison.Ordinal);
        Assert.Contains("provider_support_claim=FALSE", Migration, StringComparison.Ordinal);
        Assert.Contains("hardware_supportability_claim=FALSE", Migration, StringComparison.Ordinal);
        Assert.Contains("certification_claim=FALSE", Migration, StringComparison.Ordinal);
    }

    [Fact]
    public void EndpointSerializesTheDeviceAndReturnsEveryNonClaim()
    {
        Assert.Contains("/api/telemetry/devices/{id:long}/support-tier-actions", Routes, StringComparison.Ordinal);
        Assert.Contains("RequirePermission(http, \"telematics:devices:rma\")", Endpoint, StringComparison.Ordinal);
        Assert.Contains("device-support-tier:{companyId}:{id}", Endpoint, StringComparison.Ordinal);
        Assert.Contains("support_tier_idempotency_mismatch", Endpoint, StringComparison.Ordinal);
        Assert.Contains("commercialEntitlementVerifiedClaim = false", Endpoint, StringComparison.Ordinal);
        Assert.Contains("providerSupportClaim = false", Endpoint, StringComparison.Ordinal);
        Assert.Contains("hardwareSupportabilityClaim = false", Endpoint, StringComparison.Ordinal);
        Assert.Contains("certificationClaim = false", Endpoint, StringComparison.Ordinal);
    }

    [Fact]
    public void CustomerSurfaceFailsClosedAndSeparatesRoutingFromAssurance()
    {
        Assert.Contains("recordDeviceSupportTierAction", Service, StringComparison.Ordinal);
        Assert.Contains("commercial_entitlement_verified_claim !== false", Service, StringComparison.Ordinal);
        Assert.Contains("Device support-tier data crossed the operator-recorded unverified boundary", Service, StringComparison.Ordinal);
        Assert.Contains("Device support tier", Page, StringComparison.Ordinal);
        Assert.Contains("Assign tier", Page, StringComparison.Ordinal);
        Assert.Contains("does not verify a commercial entitlement, provider support, hardware supportability, or certification", Page, StringComparison.Ordinal);
    }

    private static string Read(params string[] parts) => File.ReadAllText(Path.GetFullPath(Path.Combine(
        AppContext.BaseDirectory, "../../../../", Path.Combine(parts))));
}
