namespace Opstrax.Tests;

public sealed class DeviceInstallationWorkPackageContractTests
{
    private static readonly string Migration = Read("database", "migrations", "2026_09_07_stage121_device_installation_work_packages.sql");
    private static readonly string Endpoints = Read("backend-dotnet", "Controllers", "DeviceInstallationWorkPackageEndpoints.cs");
    private static readonly string Routes = Read("backend-dotnet", "Controllers", "EndpointMappings.cs");
    private static readonly string Service = Read("frontend", "src", "services", "telematicsService.ts");
    private static readonly string Page = Read("frontend", "src", "pages", "IotDevicesPage.tsx");

    [Fact]
    public void DatabaseMakesAllInstallationWorkEvidenceAppendOnlyAndNonCertifying()
    {
        Assert.Contains("physical_appointment_claim=FALSE", Migration, StringComparison.Ordinal);
        Assert.Contains("physical_work_claim=FALSE", Migration, StringComparison.Ordinal);
        Assert.Contains("assurance_status='Unverified'", Migration, StringComparison.Ordinal);
        Assert.Contains("content_verification_status='Unverified'", Migration, StringComparison.Ordinal);
        Assert.Contains("certification_claim=FALSE", Migration, StringComparison.Ordinal);
        Assert.Contains("Installation work packages are append-only", Migration, StringComparison.Ordinal);
        Assert.Contains("Installation work-package evidence is append-only", Migration, StringComparison.Ordinal);
        Assert.Contains("device_branch IS DISTINCT FROM vehicle_branch", Migration, StringComparison.Ordinal);
        Assert.Contains("installer_branch IS DISTINCT FROM NEW.branch_id", Migration, StringComparison.Ordinal);
        Assert.Contains("NEW.recorded_by IS DISTINCT FROM package_installer", Migration, StringComparison.Ordinal);
    }

    [Fact]
    public void ApiRecordsExactOperatorAndNeverAcceptsClaimFlags()
    {
        Assert.Contains("assigned_installer_user_id", Endpoints, StringComparison.Ordinal);
        Assert.Contains("command.Parameters.AddWithValue(\"@actor\", actorId)", Endpoints, StringComparison.Ordinal);
        Assert.Contains("StoredInstantEquals(existing[\"appointmentStart\"]", Endpoints, StringComparison.Ordinal);
        Assert.Contains("existing[\"observationNotes\"]", Endpoints, StringComparison.Ordinal);
        Assert.Contains("existing[\"capturedAt\"]", Endpoints, StringComparison.Ordinal);
        Assert.Contains("physicalAppointmentClaim = false", Endpoints, StringComparison.Ordinal);
        Assert.Contains("physicalEvidenceClaim = false", Endpoints, StringComparison.Ordinal);
        Assert.Contains("contentVerificationStatus = \"Unverified\"", Endpoints, StringComparison.Ordinal);
        Assert.DoesNotContain("PhysicalAppointmentClaim", Endpoints, StringComparison.Ordinal);
        Assert.DoesNotContain("CertificationClaim", Endpoints, StringComparison.Ordinal);
        Assert.Contains("/installation-work-packages/{workPackageId:long}/checklist-observations", Routes, StringComparison.Ordinal);
        Assert.Contains("/installation-work-packages/{workPackageId:long}/artifact-references", Routes, StringComparison.Ordinal);
    }

    [Fact]
    public void CustomerProjectionAndSurfaceFailClosed()
    {
        Assert.Contains("row.physical_appointment_claim !== false", Service, StringComparison.Ordinal);
        Assert.Contains("row.assurance_status !== \"Unverified\"", Service, StringComparison.Ordinal);
        Assert.Contains("row.content_verification_status !== \"Unverified\"", Service, StringComparison.Ordinal);
        Assert.Contains("RecordedAwaitingIndependentVerification", Service, StringComparison.Ordinal);
        Assert.Contains("Installer work packages", Page, StringComparison.Ordinal);
        Assert.Contains("Attendance, artifact content, physical work, and certification remain unverified", Page, StringComparison.Ordinal);
        Assert.Contains("Record unverified observation", Page, StringComparison.Ordinal);
        Assert.Contains("Record unverified reference", Page, StringComparison.Ordinal);
    }

    private static string Read(params string[] parts) => File.ReadAllText(Path.GetFullPath(Path.Combine(
        AppContext.BaseDirectory, "../../../../", Path.Combine(parts))));
}
