namespace Opstrax.Tests;

public sealed class ControlTowerTruthContractTests
{
    [Fact]
    public void SummaryReturnsOnlyPersistedOperationalEvidence()
    {
        var source = Read("backend-dotnet", "Controllers", "EndpointMappings.cs");
        var start = source.IndexOf("private static async Task<IResult> ControlTowerSummary(", StringComparison.Ordinal);
        var end = source.IndexOf("private static async Task<IResult> ControlTowerEntities(", start, StringComparison.Ordinal);
        Assert.True(start >= 0 && end > start);
        var summary = source[start..end];

        Assert.Contains("Current operational snapshot", summary, StringComparison.Ordinal);
        Assert.DoesNotContain("Live Simulation", summary, StringComparison.Ordinal);
        Assert.DoesNotContain("placeholder", summary, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("competitorGapAnalysis", summary, StringComparison.Ordinal);
        Assert.DoesNotContain("available = true", summary, StringComparison.Ordinal);
        Assert.Contains("var canViewDeviceEvidence = RequirePermission(http, \"telematics:devices:view\") is null", summary, StringComparison.Ordinal);
        Assert.Contains("var canViewCameraEvidence = RequirePermission(http, \"dashcam:view\") is null", summary, StringComparison.Ordinal);
        Assert.Contains("i.device_role IN ('GPS','ELD','OBD-II','J1939/CAN')", summary, StringComparison.Ordinal);
        Assert.Contains("de.source_authority='Authoritative' AND de.media_status='Ready'", summary, StringComparison.Ordinal);
        Assert.DoesNotContain("v.device_status deviceStatus", summary, StringComparison.Ordinal);
        Assert.DoesNotContain("SUM(CASE WHEN v.device_status='Online'", summary, StringComparison.Ordinal);
        Assert.DoesNotContain("v.camera_status cameraStatus", summary, StringComparison.Ordinal);
        Assert.DoesNotContain("SUM(CASE WHEN v.camera_status='Online'", summary, StringComparison.Ordinal);
    }

    [Fact]
    public void VehicleDetailsExposeOnlyAuthorizedAuthoritativeReadyCameraMedia()
    {
        var source = Read("backend-dotnet", "Controllers", "EndpointMappings.cs");
        var controlStart = source.IndexOf("private static async Task<IResult> ControlTowerVehicleDetail(", StringComparison.Ordinal);
        var controlEnd = source.IndexOf("private const string VehicleOperationalProjectionSql", controlStart, StringComparison.Ordinal);
        var vehicleStart = source.IndexOf("private static async Task<IResult> VehicleDetail(", StringComparison.Ordinal);
        var vehicleEnd = source.IndexOf("private static async Task<IResult> DriverDetail(", vehicleStart, StringComparison.Ordinal);
        Assert.True(controlStart >= 0 && controlEnd > controlStart && vehicleStart >= 0 && vehicleEnd > vehicleStart);

        foreach (var detail in new[] { source[controlStart..controlEnd], source[vehicleStart..vehicleEnd] })
        {
            Assert.Contains("RequirePermission(http, \"dashcam:view\") is null", detail, StringComparison.Ordinal);
            Assert.Contains("source_authority='Authoritative' AND media_status='Ready'", detail, StringComparison.Ordinal);
        }

        var controlDetail = source[controlStart..controlEnd];
        Assert.Contains("RequirePermission(http, \"telematics:devices:view\") is null", controlDetail, StringComparison.Ordinal);
        Assert.Contains("END device_status", controlDetail, StringComparison.Ordinal);
        Assert.Contains("END camera_status", controlDetail, StringComparison.Ordinal);
        Assert.Contains("i.device_role IN ('GPS','ELD','OBD-II','J1939/CAN')", controlDetail, StringComparison.Ordinal);
    }

    [Fact]
    public void DiagnosticAndColdChainReadsExposeStoredProvenanceAndRelatedIds()
    {
        var endpoints = Read("backend-dotnet", "Controllers", "EndpointMappings.cs");
        Assert.Contains("fc.source_event_id fault_source_event_id", endpoints, StringComparison.Ordinal);
        Assert.Contains("fc.last_source_event_id fault_last_source_event_id", endpoints, StringComparison.Ordinal);

        var cold = Read("backend-dotnet", "Controllers", "FleetTmsColdChainEndpoints.cs");
        Assert.Contains("r.source_channel, r.client_generated_id, r.correlation_id, r.causation_id", cold, StringComparison.Ordinal);
        Assert.Contains("a.source_channel, a.client_generated_id, a.correlation_id, a.causation_id", cold, StringComparison.Ordinal);
        Assert.Contains("r.applied_policy_code, r.applied_policy_scope", cold, StringComparison.Ordinal);
        Assert.Contains("a.device_id", cold, StringComparison.Ordinal);
        Assert.Contains("a.reading_id", cold, StringComparison.Ordinal);
    }

    private static string Read(params string[] parts)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !Directory.Exists(Path.Combine(directory.FullName, "backend-dotnet")))
            directory = directory.Parent;
        Assert.NotNull(directory);
        return File.ReadAllText(Path.Combine([directory!.FullName, .. parts]));
    }
}
