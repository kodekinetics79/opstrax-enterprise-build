using Opstrax.Api.Services;

namespace Opstrax.Tests;

public sealed class DeviceFirmwareCampaignPolicyTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 7, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void ValidCampaignNormalizesScheduleAndPreservesTargetOrder()
    {
        var result = DeviceFirmwareCampaignPolicy.Validate(Valid() with
        {
            DeviceIds = [42, 7],
            ScheduledFor = "2026-09-07T08:30:00-04:00",
        }, Now);

        Assert.Null(result.Error);
        Assert.NotNull(result.Value);
        Assert.Equal(new long[] { 42, 7 }, result.Value!.DeviceIds);
        Assert.Equal(new DateTimeOffset(2026, 9, 7, 12, 30, 0, TimeSpan.Zero), result.Value.ScheduledFor);
        Assert.Equal("v2.4.1", result.Value.TargetFirmwareVersion);
    }

    [Fact]
    public void DuplicateTargetsAreRejected()
    {
        var result = DeviceFirmwareCampaignPolicy.Validate(Valid() with { DeviceIds = [7, 7] }, Now);
        Assert.Equal("deviceIds cannot contain duplicates.", result.Error);
    }

    [Fact]
    public void BatchCannotExceedTargetCount()
    {
        var result = DeviceFirmwareCampaignPolicy.Validate(Valid() with { DeviceIds = [7], BatchSize = 2 }, Now);
        Assert.Contains("cannot exceed", result.Error);
    }

    [Fact]
    public void ScheduleWithoutOffsetIsRejected()
    {
        var result = DeviceFirmwareCampaignPolicy.Validate(Valid() with { ScheduledFor = "2026-09-07T12:30:00" }, Now);
        Assert.Contains("explicit UTC offset", result.Error);
    }

    [Theory]
    [InlineData("version with spaces")]
    [InlineData("../../firmware")]
    [InlineData("")]
    public void InvalidTargetVersionIsRejected(string version)
    {
        var result = DeviceFirmwareCampaignPolicy.Validate(Valid() with { TargetFirmwareVersion = version }, Now);
        Assert.Contains("targetFirmwareVersion", result.Error);
    }

    [Fact]
    public void RollbackMustDifferFromTarget()
    {
        var result = DeviceFirmwareCampaignPolicy.Validate(Valid() with { RollbackFirmwareVersion = "V2.4.1" }, Now);
        Assert.Contains("must differ", result.Error);
    }

    [Fact]
    public void CompleteIdentityIsOnlyReadyForExternalEvidence()
    {
        var result = DeviceFirmwareCampaignPolicy.AssessTarget("Acme", "Tracker-X", "rev-a", "v2.3.0", "v2.4.1");
        Assert.Equal("ReadyForExternalEvidence", result.Status);
        Assert.Contains("remain required", result.Reason);
    }

    [Fact]
    public void MissingIdentityBlocksPlanningReadiness()
    {
        var result = DeviceFirmwareCampaignPolicy.AssessTarget("Acme", null, "rev-a", null, "v2.4.1");
        Assert.Equal("BlockedIdentity", result.Status);
        Assert.Contains("device model", result.Reason);
        Assert.Contains("reported firmware version", result.Reason);
    }

    [Fact]
    public void MatchingReportedVersionIsAlreadyCurrent()
    {
        var result = DeviceFirmwareCampaignPolicy.AssessTarget("Acme", "Tracker-X", "rev-a", "V2.4.1", "v2.4.1");
        Assert.Equal("AlreadyCurrent", result.Status);
    }

    private static DeviceFirmwareCampaignRequest Valid() => new(
        "September canary", "v2.4.1", "v2.3.0", "Canary", "2026-09-07T12:30:00Z",
        60, 1, [7], "Prepare the vendor firmware evidence lane", "Change ticket CHG-117",
        Guid.NewGuid().ToString("D"));
}
