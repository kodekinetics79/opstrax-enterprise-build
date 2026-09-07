using System.Text;
using Opstrax.Api.Services;

namespace Opstrax.Tests;

public sealed class DeviceConnectivityObservationServiceTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 7, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void PrepareAcceptsBoundedProviderStatusWithoutCreatingOutcomeClaims()
    {
        var prepared = DeviceConnectivityObservationService.Prepare(
            Valid(), Encoding.UTF8.GetBytes("{\"status\":\"active\"}"), Now);

        Assert.Equal("carrier-api", prepared.SourceProvider);
        Assert.Equal("account-reference", prepared.SourceAccountReference);
        Assert.Equal("observation-001", prepared.SourceObservationId);
        Assert.Matches("^[0-9a-f]{64}$", prepared.PayloadSha256);
        Assert.Equal("Active", prepared.SubscriptionStatus);
        Assert.Equal("Registered", prepared.NetworkRegistrationStatus);
        Assert.Equal("Attached", prepared.DataSessionStatus);
        Assert.Equal(Now.UtcDateTime, prepared.ReceivedAtUtc);
    }

    [Theory]
    [InlineData("subscription", "Connected")]
    [InlineData("network", "Online")]
    [InlineData("session", "Streaming")]
    public void PrepareRejectsInventedStatusVocabulary(string field, string value)
    {
        var envelope = field switch
        {
            "subscription" => Valid() with { SubscriptionStatus = value },
            "network" => Valid() with { NetworkRegistrationStatus = value },
            _ => Valid() with { DataSessionStatus = value },
        };
        Assert.Throws<DeviceConnectivityObservationValidationException>(() =>
            DeviceConnectivityObservationService.Prepare(envelope, new byte[] { 1 }, Now));
    }

    [Fact]
    public void PrepareRejectsEmptyPayloadInvalidIccidAndNonUtcEvidence()
    {
        Assert.Throws<DeviceConnectivityObservationValidationException>(() =>
            DeviceConnectivityObservationService.Prepare(Valid(), ReadOnlyMemory<byte>.Empty, Now));
        Assert.Throws<DeviceConnectivityObservationValidationException>(() =>
            DeviceConnectivityObservationService.Prepare(Valid() with { Iccid = "not-an-iccid" }, new byte[] { 1 }, Now));
        Assert.Throws<DeviceConnectivityObservationValidationException>(() =>
            DeviceConnectivityObservationService.Prepare(
                Valid() with { ObservedAtUtc = Now.ToOffset(TimeSpan.FromHours(-4)) }, new byte[] { 1 }, Now));
    }

    [Fact]
    public void PrepareRejectsTimestampPrecisionPostgresCannotRoundTripExactly()
    {
        var subMicrosecond = Now.AddTicks(1);

        Assert.Throws<DeviceConnectivityObservationValidationException>(() =>
            DeviceConnectivityObservationService.Prepare(
                Valid() with { ObservedAtUtc = subMicrosecond }, new byte[] { 1 }, Now));
    }

    private static DeviceConnectivityObservationEnvelope Valid() => new(
        "Carrier-API", "account-reference", "observation-001", "89014103211118510720",
        "Active", "Registered", "Attached", 4096, false, Now.AddMinutes(-1));
}
