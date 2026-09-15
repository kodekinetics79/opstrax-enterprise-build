namespace Opstrax.Tests;

public sealed class DeviceRetirementContractTests
{
    private static readonly string Migration = Read("database", "migrations", "2026_09_07_stage123_device_retirement.sql");
    private static readonly string Endpoint = Read("backend-dotnet", "Controllers", "DeviceRetirementEndpoints.cs");
    private static readonly string Routes = Read("backend-dotnet", "Controllers", "EndpointMappings.cs");
    private static readonly string Service = Read("frontend", "src", "services", "telematicsService.ts");
    private static readonly string Page = Read("frontend", "src", "pages", "IotDevicesPage.tsx");

    [Fact]
    public void DatabaseReceiptRequiresExactClosedSoftwareStateAndNoPromotedClaim()
    {
        Assert.Contains("Device retirement records are append-only", Migration, StringComparison.Ordinal);
        Assert.Contains("status<>'Retired'", Migration, StringComparison.Ordinal);
        Assert.Contains("device_state<>'Retired'", Migration, StringComparison.Ordinal);
        Assert.Contains("Retired device still has usable credential material", Migration, StringComparison.Ordinal);
        Assert.Contains("Installed device must be removed before retirement", Migration, StringComparison.Ordinal);
        Assert.Contains("Current connectivity profile must end before retirement", Migration, StringComparison.Ordinal);
        Assert.Contains("physical_disposition_status='Unverified'", Migration, StringComparison.Ordinal);
        Assert.Contains("physical_disposition_claim=FALSE", Migration, StringComparison.Ordinal);
        Assert.Contains("certification_claim=FALSE", Migration, StringComparison.Ordinal);
        Assert.Contains("GRANT SELECT ON TABLE device_retirement_records TO opstrax_app", Migration, StringComparison.Ordinal);
        Assert.Contains("GRANT SELECT,INSERT ON TABLE device_retirement_records TO opstrax_system", Migration, StringComparison.Ordinal);
    }

    [Fact]
    public void ApiClosesDependenciesBeforeRetirementAndReturnsTruthfulReceipt()
    {
        Assert.Contains("retirement_active_installation", Endpoint, StringComparison.Ordinal);
        Assert.Contains("assignment_status='Ended',effective_to=@effectiveAt", Endpoint, StringComparison.Ordinal);
        Assert.Contains("status='Retired',device_state='Retired',retired_at=@effectiveAt", Endpoint, StringComparison.Ordinal);
        Assert.Contains("api_key_hash=NULL", Endpoint, StringComparison.Ordinal);
        Assert.Contains("AppendDeviceTransitionAsync", Endpoint, StringComparison.Ordinal);
        Assert.Contains("physicalDispositionClaim = false", Endpoint, StringComparison.Ordinal);
        Assert.Contains("certificationClaim = false", Endpoint, StringComparison.Ordinal);
        Assert.Contains("/api/telemetry/devices/{id:long}/retire", Routes, StringComparison.Ordinal);
    }

    [Fact]
    public void CustomerSurfaceRequiresTypedConfirmationAndFailsClosedOnClaims()
    {
        Assert.Contains("/api/telemetry/devices/${requestedId}/retire", Service, StringComparison.Ordinal);
        Assert.Contains("payload.physical_disposition_claim !== false", Service, StringComparison.Ordinal);
        Assert.Contains("The server did not preserve the governed unverified retirement boundary", Service, StringComparison.Ordinal);
        Assert.Contains("Type RETIRE ${retirementTarget.serialNumber} to confirm", Page, StringComparison.Ordinal);
        Assert.Contains("The disposition selection is a plan only", Page, StringComparison.Ordinal);
        Assert.Contains("Device Lifecycle History", Page, StringComparison.Ordinal);
        Assert.DoesNotContain("PanelSection title=\"Assignment History\"", Page, StringComparison.Ordinal);
    }

    private static string Read(params string[] parts) => File.ReadAllText(Path.GetFullPath(Path.Combine(
        AppContext.BaseDirectory, "../../../../", Path.Combine(parts))));
}
