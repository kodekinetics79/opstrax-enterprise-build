namespace Opstrax.Tests;

public sealed class PlatformHardwareReadinessContractTests
{
    private static readonly string Source = File.ReadAllText(Path.GetFullPath(Path.Combine(
        AppContext.BaseDirectory, "../../../../backend-dotnet/Controllers/PlatformEndpoints.cs")));
    private static readonly string Migration = File.ReadAllText(Path.GetFullPath(Path.Combine(
        AppContext.BaseDirectory, "../../../../database/migrations/2026_09_11_stage136_platform_hardware_readiness_permission.sql")));

    [Fact]
    public void PlatformRoutesSeparateReadinessFromTenantDeviceProvisioning()
    {
        Assert.Contains("/api/platform/device-compatibility-candidates", Source, StringComparison.Ordinal);
        Assert.Contains("platform:devices:view", Section(), StringComparison.Ordinal);
        Assert.Contains("platform:devices:manage", Section(), StringComparison.Ordinal);
        Assert.Contains("GT06 direct TCP", Section(), StringComparison.Ordinal);
        Assert.Contains("Pacific Track proprietary", Section(), StringComparison.Ordinal);
        Assert.Contains("Supplier cloud", Section(), StringComparison.Ordinal);
    }

    [Fact]
    public void ExactCandidateStartsAndRemainsOnExternalHold()
    {
        var section = Section();
        Assert.Contains("^[0-9a-f]{40}$", section, StringComparison.Ordinal);
        Assert.Contains("software_candidate_sha", section, StringComparison.Ordinal);
        Assert.Contains("'Candidate','ExternalHold'", section, StringComparison.Ordinal);
        Assert.Contains("capability_declaration_status='EngineeringDeclaredUnverified'", section, StringComparison.Ordinal);
        Assert.Contains("certificationStatus = \"ExternalHold\"", section, StringComparison.Ordinal);
        Assert.Contains("certificationClaim = false", section, StringComparison.Ordinal);
        Assert.DoesNotContain("certification_status='Certified", section, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("physical_evidence_claim=TRUE", section, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void DeclarationAcceptsOnlySoftwareImplementedProtocolPaths()
    {
        var section = Section();
        Assert.Contains("\"GT06\"", section, StringComparison.Ordinal);
        Assert.Contains("\"J1939\"", section, StringComparison.Ordinal);
        Assert.Contains("These protocol paths are not software-ready", section, StringComparison.Ordinal);
        Assert.Contains("Register a new exact candidate for changed capabilities", section, StringComparison.Ordinal);
        Assert.Contains("Physical certification evidence remains pending", section, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ProductAdminAccessIsInstalledForProtectedEnvironments()
    {
        Assert.Contains("role_key = 'product_admin'", Migration, StringComparison.Ordinal);
        Assert.Contains("'platform:devices:view'", Migration, StringComparison.Ordinal);
        Assert.Contains("'platform:devices:manage'", Migration, StringComparison.Ordinal);
        Assert.Contains("ON CONFLICT (role_id, permission_key) DO NOTHING", Migration, StringComparison.Ordinal);
        Assert.DoesNotContain("INSERT INTO roles", Migration, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("INSERT INTO role_permissions", Migration, StringComparison.OrdinalIgnoreCase);
    }

    private static string Section()
    {
        var start = Source.IndexOf("SoftwareReadyHardwareProtocols", StringComparison.Ordinal);
        var end = Source.IndexOf("private static async Task<IResult> PackageUpdate", start, StringComparison.Ordinal);
        Assert.True(start >= 0 && end > start, "Hardware-readiness handlers could not be isolated.");
        return Source[start..end];
    }
}
