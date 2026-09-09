namespace Opstrax.Tests;

public sealed class DeviceSparePoolContractTests
{
    private static readonly string Migration = Read("database", "migrations", "2026_09_07_stage125_device_spare_pool.sql");
    private static readonly string Endpoint = Read("backend-dotnet", "Controllers", "DeviceSparePoolEndpoints.cs");
    private static readonly string Routes = Read("backend-dotnet", "Controllers", "EndpointMappings.cs");
    private static readonly string Service = Read("frontend", "src", "services", "telematicsService.ts");
    private static readonly string Page = Read("frontend", "src", "pages", "IotDevicesPage.tsx");

    [Fact]
    public void DatabaseHistoryIsExactDeviceAppendOnlyAndFailClosed()
    {
        Assert.Contains("Spare-pool entries are immutable", Migration, StringComparison.Ordinal);
        Assert.Contains("Spare-pool events are append-only", Migration, StringComparison.Ordinal);
        Assert.Contains("First spare-pool event must add the exact device", Migration, StringComparison.Ordinal);
        Assert.Contains("RMA case already has an active spare reservation", Migration, StringComparison.Ordinal);
        Assert.Contains("ck_stage125_device_not_in_pool", Migration, StringComparison.Ordinal);
        Assert.Contains("physical_possession_claim=FALSE", Migration, StringComparison.Ordinal);
        Assert.Contains("condition_verified_claim=FALSE", Migration, StringComparison.Ordinal);
        Assert.Contains("compatibility_claim=FALSE", Migration, StringComparison.Ordinal);
        Assert.Contains("certification_claim=FALSE", Migration, StringComparison.Ordinal);
    }

    [Fact]
    public void EndpointSerializesDeviceCaseAndIdempotencyBoundaries()
    {
        Assert.Contains("/api/telemetry/devices/{id:long}/spare-pool-actions", Routes, StringComparison.Ordinal);
        Assert.Contains("RequirePermission(http, \"telematics:devices:rma\")", Endpoint, StringComparison.Ordinal);
        Assert.Contains("device-spare-pool:{companyId}:{id}", Endpoint, StringComparison.Ordinal);
        Assert.Contains("device-spare-pool-case:{companyId}:{input.RmaCaseId.Value}", Endpoint, StringComparison.Ordinal);
        Assert.Contains("spare_pool_idempotency_mismatch", Endpoint, StringComparison.Ordinal);
        Assert.Contains("physicalPossessionClaim = false", Endpoint, StringComparison.Ordinal);
        Assert.Contains("conditionVerifiedClaim = false", Endpoint, StringComparison.Ordinal);
        Assert.Contains("compatibilityClaim = false", Endpoint, StringComparison.Ordinal);
        Assert.Contains("certificationClaim = false", Endpoint, StringComparison.Ordinal);
    }

    [Fact]
    public void CustomerSurfaceFailsClosedAndExplainsEvidenceLimits()
    {
        Assert.Contains("recordDeviceSparePoolAction", Service, StringComparison.Ordinal);
        Assert.Contains("physical_possession_claim !== false", Service, StringComparison.Ordinal);
        Assert.Contains("Spare-pool event crossed the operator-recorded no-evidence-claim boundary", Service, StringComparison.Ordinal);
        Assert.Contains("Spare-pool entry crossed the unverified inventory-planning boundary", Service, StringComparison.Ordinal);
        Assert.Contains("Spare-device pool planning", Page, StringComparison.Ordinal);
        Assert.Contains("Reserve for RMA", Page, StringComparison.Ordinal);
        Assert.Contains("do not prove physical possession, device condition, compatibility, installation, or certification", Page, StringComparison.Ordinal);
    }

    private static string Read(params string[] parts) => File.ReadAllText(Path.GetFullPath(Path.Combine(
        AppContext.BaseDirectory, "../../../../", Path.Combine(parts))));
}
