namespace Opstrax.Tests;

public sealed class DeviceConnectivityObservationContractTests
{
    private static readonly string Migration = Read("database", "migrations", "2026_09_07_stage120_device_connectivity_observations.sql");
    private static readonly string Service = Read("backend-dotnet", "Services", "DeviceConnectivityObservationService.cs");
    private static readonly string Endpoints = Read("backend-dotnet", "Controllers", "EndpointMappings.cs");
    private static readonly string Frontend = Read("frontend", "src", "services", "telematicsService.ts");
    private static readonly string Page = Read("frontend", "src", "pages", "IotDevicesPage.tsx");

    [Fact]
    public void DatabaseKeepsProviderIdentityProtectedAndClaimsFalse()
    {
        Assert.Contains("source_authentication_status='Authenticated'", Migration, StringComparison.Ordinal);
        Assert.Contains("reconciliation_status='ExactCurrentProfile'", Migration, StringComparison.Ordinal);
        Assert.Contains("provider_verified_claim=FALSE", Migration, StringComparison.Ordinal);
        Assert.Contains("physical_connectivity_claim=FALSE", Migration, StringComparison.Ordinal);
        Assert.Contains("certification_claim=FALSE", Migration, StringComparison.Ordinal);
        Assert.Contains("GRANT SELECT,INSERT ON device_connectivity_observations TO opstrax_system", Migration, StringComparison.Ordinal);
        Assert.DoesNotContain("source_account_bidx,source_observation_bidx,payload_sha256,created_at\n    ) ON device_connectivity_observations TO opstrax_app", Migration, StringComparison.Ordinal);
    }

    [Fact]
    public void ServiceRequiresProtectedExactProfileAndNeverCreatesOutcomeClaims()
    {
        Assert.Contains("if (!pii.Enabled)", Service, StringComparison.Ordinal);
        Assert.Contains("$\"provider-account:{companyId}:", Service, StringComparison.Ordinal);
        Assert.Contains("pii.BlindIndexExact(\n                $\"provider-observation:", Service, StringComparison.Ordinal);
        Assert.Contains("p.iccid_bidx=@iccid", Service, StringComparison.Ordinal);
        Assert.Contains("p.branch_id IS NOT DISTINCT FROM e.branch_id", Service, StringComparison.Ordinal);
        Assert.Contains("p.assignment_status='Assigned' AND p.effective_to IS NULL", Service, StringComparison.Ordinal);
        Assert.Contains("'ExactCurrentProfile',FALSE,FALSE,FALSE", Service, StringComparison.Ordinal);
        Assert.DoesNotContain("HashIdentity", Service, StringComparison.Ordinal);
        Assert.DoesNotContain("prepared.SourceAccountReference,", Service, StringComparison.Ordinal);
    }

    [Fact]
    public void CustomerProjectionContainsSafeFactsAndDeniesExternalProof()
    {
        Assert.Contains("connectivityObservations", Endpoints, StringComparison.Ordinal);
        Assert.DoesNotContain("SELECT source_account_bidx", Endpoints, StringComparison.Ordinal);
        Assert.Contains("softwareObservationAvailable", Frontend, StringComparison.Ordinal);
        Assert.Contains("Provider-reported software status only", Page, StringComparison.Ordinal);
        Assert.Contains("does not prove radio attachment, telemetry delivery, physical operation, or certification", Page, StringComparison.Ordinal);
    }

    private static string Read(params string[] parts) => File.ReadAllText(Path.GetFullPath(Path.Combine(
        AppContext.BaseDirectory, "../../../../", Path.Combine(parts))));
}
