namespace Opstrax.Tests;

public sealed class DeviceFirmwareCampaignEndpointContractTests
{
    private static readonly string Source = File.ReadAllText(Path.GetFullPath(Path.Combine(
        AppContext.BaseDirectory, "../../../../backend-dotnet/Controllers/EndpointMappings.cs")));

    [Fact]
    public void PlanningEndpointHasDedicatedPermissionAndBranchScope()
    {
        var handler = Handler();
        Assert.Contains("RequirePermission(http, \"telematics:devices:firmware\")", handler, StringComparison.Ordinal);
        Assert.Contains("@branchId::BIGINT IS NULL OR branch_id=@branchId", handler, StringComparison.Ordinal);
        Assert.Contains("visibleDevices.Count != requestedIds.Length", handler, StringComparison.Ordinal);
    }

    [Fact]
    public void PlanningEndpointCannotDispatchExistingDeviceCommands()
    {
        var handler = Handler();
        Assert.DoesNotContain("telematics_device_commands", handler, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("dispatched_at", handler, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("remoteUpgradeClaim = false", handler, StringComparison.Ordinal);
        Assert.Contains("No command was dispatched", handler, StringComparison.Ordinal);
    }

    [Fact]
    public void ReplayIsBranchScopedAndRejectsChangedPayload()
    {
        var handler = Handler();
        Assert.Contains("FirmwareCampaignReplayMatches", handler, StringComparison.Ordinal);
        Assert.Contains("firmware_idempotency_mismatch", handler, StringComparison.Ordinal);
        Assert.Contains("LoadFirmwareCampaignTargets(db, companyId", handler, StringComparison.Ordinal);
    }

    private static string Handler()
    {
        var start = Source.IndexOf("private static async Task<IResult> DeviceFirmwareCampaignCreate", StringComparison.Ordinal);
        var end = Source.IndexOf("private static bool DeviceUnavailableForFirmwarePlanning", start, StringComparison.Ordinal);
        Assert.True(start >= 0 && end > start, "Firmware campaign handler could not be isolated.");
        return Source[start..end];
    }
}
