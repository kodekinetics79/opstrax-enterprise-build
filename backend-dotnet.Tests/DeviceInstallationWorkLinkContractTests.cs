namespace Opstrax.Tests;

public sealed class DeviceInstallationWorkLinkContractTests
{
    private static readonly string Migration = Read("database", "migrations", "2026_09_07_stage122_installation_work_package_links.sql");
    private static readonly string Endpoints = Read("backend-dotnet", "Controllers", "DeviceInstallationWorkPackageEndpoints.cs");
    private static readonly string Routes = Read("backend-dotnet", "Controllers", "EndpointMappings.cs");
    private static readonly string Service = Read("frontend", "src", "services", "telematicsService.ts");
    private static readonly string Page = Read("frontend", "src", "pages", "IotDevicesPage.tsx");

    [Fact]
    public void DatabaseRequiresExactCompleteWorkAndKeepsLinkUnverified()
    {
        Assert.Contains("link_assurance_status='RecordedUnverified'", Migration, StringComparison.Ordinal);
        Assert.Contains("physical_work_claim=FALSE", Migration, StringComparison.Ordinal);
        Assert.Contains("certification_claim=FALSE", Migration, StringComparison.Ordinal);
        Assert.Contains("Installation work-package links are append-only", Migration, StringComparison.Ordinal);
        Assert.Contains("installation_row.vehicle_id IS DISTINCT FROM work_row.vehicle_id", Migration, StringComparison.Ordinal);
        Assert.Contains("NEW.linked_by IS DISTINCT FROM work_row.assigned_installer_user_id", Migration, StringComparison.Ordinal);
        Assert.Contains("latest Pass or NotApplicable", Migration, StringComparison.Ordinal);
        Assert.Contains("At least one artifact reference is required", Migration, StringComparison.Ordinal);
        Assert.Contains("GRANT SELECT,INSERT ON TABLE device_installation_work_package_links", Migration, StringComparison.Ordinal);
    }

    [Fact]
    public void ApiUsesSignedInAssignedInstallerAndReturnsNoPromotedClaim()
    {
        Assert.Contains("DeviceInstallationWorkPackageLinkCreate", Endpoints, StringComparison.Ordinal);
        Assert.Contains("w.assigned_installer_user_id=@actor", Endpoints, StringComparison.Ordinal);
        Assert.Contains("ORDER BY (idempotency_key=@key) DESC,id LIMIT 1", Endpoints, StringComparison.Ordinal);
        Assert.Contains("physicalWorkClaim = false", Endpoints, StringComparison.Ordinal);
        Assert.Contains("certificationClaim = false", Endpoints, StringComparison.Ordinal);
        Assert.Contains("physical work and certification remain unverified", Endpoints, StringComparison.Ordinal);
        Assert.Contains("/installation-work-packages/{workPackageId:long}/installation-links", Routes, StringComparison.Ordinal);
    }

    [Fact]
    public void CustomerProjectionFailsClosedAndExplainsTraceabilityBoundary()
    {
        Assert.Contains("row.link_assurance_status !== \"RecordedUnverified\"", Service, StringComparison.Ordinal);
        Assert.Contains("row.link_physical_work_claim !== false", Service, StringComparison.Ordinal);
        Assert.Contains("row.link_certification_claim !== false", Service, StringComparison.Ordinal);
        Assert.Contains("LinkedAwaitingIndependentVerification", Service, StringComparison.Ordinal);
        Assert.Contains("The server did not preserve the unverified installation-link boundary", Service, StringComparison.Ordinal);
        Assert.Contains("Link recorded installation", Page, StringComparison.Ordinal);
        Assert.Contains("Linking records traceability only; physical work and certification remain unverified", Page, StringComparison.Ordinal);
        Assert.Contains("This link does not verify physical work", Page, StringComparison.Ordinal);
    }

    private static string Read(params string[] parts) => File.ReadAllText(Path.GetFullPath(Path.Combine(
        AppContext.BaseDirectory, "../../../../", Path.Combine(parts))));
}
