namespace Opstrax.Tests;

public sealed class DeviceRemoteCommandEndpointContractTests
{
    private static readonly string Source = File.ReadAllText(Path.GetFullPath(Path.Combine(
        AppContext.BaseDirectory, "../../../../backend-dotnet/Controllers/EndpointMappings.cs")));

    [Fact]
    public void MutationUsesDedicatedPermissionSystemTransactionAndIdempotency()
    {
        var handler = Handler();
        Assert.Contains("RequirePermission(http, \"telematics:devices:command\")", handler, StringComparison.Ordinal);
        Assert.Contains("RunInSystemTransactionAsync", handler, StringComparison.Ordinal);
        Assert.Contains("idempotency_key=@key", handler, StringComparison.Ordinal);
        Assert.Contains("RemoteCommandReplayMatches", handler, StringComparison.Ordinal);
    }

    [Fact]
    public void AdmissionRequiresCurrentVerifiedExactTupleCapability()
    {
        var handler = Handler();
        Assert.Contains("cap.capability_status='Verified'", handler, StringComparison.Ordinal);
        Assert.Contains("cap.expires_at>NOW()", handler, StringComparison.Ordinal);
        foreach (var field in new[] { "device_serial", "manufacturer", "device_model", "hardware_revision", "firmware_version", "provider" })
            Assert.Contains($"cap.{field} IS NOT DISTINCT FROM", handler, StringComparison.Ordinal);
        Assert.Contains("DeviceUnavailableForFirmwarePlanning", handler, StringComparison.Ordinal);
    }

    [Fact]
    public void ResponseSeparatesRecordingFromDeliveryAndOutcome()
    {
        var handler = Handler();
        Assert.Contains("requestRecorded = true", handler, StringComparison.Ordinal);
        Assert.Contains("dispatched = false", handler, StringComparison.Ordinal);
        Assert.Contains("acknowledged = false", handler, StringComparison.Ordinal);
        Assert.Contains("applied = false", handler, StringComparison.Ordinal);
        Assert.Contains("providerDeliveryClaim = false", handler, StringComparison.Ordinal);
        Assert.Contains("physicalOutcomeClaim = false", handler, StringComparison.Ordinal);
        Assert.DoesNotContain("SafetyConfirmation)", handler, StringComparison.Ordinal);
        Assert.Contains("SafetyConfirmationHash", handler, StringComparison.Ordinal);
    }

    private static string Handler()
    {
        var start = Source.IndexOf("private static async Task<IResult> DeviceRemoteCommandCreate", StringComparison.Ordinal);
        var end = Source.IndexOf("private static Task<Dictionary<string, object?>?> LoadRmaCase", start, StringComparison.Ordinal);
        Assert.True(start >= 0 && end > start, "Remote command handler could not be isolated.");
        return Source[start..end];
    }
}
