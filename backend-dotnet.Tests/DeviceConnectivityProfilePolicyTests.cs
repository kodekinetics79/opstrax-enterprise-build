using Opstrax.Api.Services;

namespace Opstrax.Tests;

public sealed class DeviceConnectivityProfilePolicyTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 7, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void ValidProfilePreservesExactIdentifiersAndNormalizesTime()
    {
        var idempotency = Guid.NewGuid();
        var result = DeviceConnectivityProfilePolicy.Validate(new(
            "PhysicalSIM", "  Example Carrier  ", " 8914800000123456789 ",
            "+14165550123", " fleet.private ", "2026-09-07T07:55:00-04:00",
            "Initial SIM assignment", "Carrier portal order 42", idempotency.ToString("D")), Now);

        Assert.Null(result.Error);
        Assert.NotNull(result.Value);
        Assert.Equal("Example Carrier", result.Value!.CarrierName);
        Assert.Equal("8914800000123456789", result.Value.Iccid);
        Assert.Equal(new DateTimeOffset(2026, 9, 7, 11, 55, 0, TimeSpan.Zero), result.Value.EffectiveAt);
        Assert.Equal(idempotency, result.Value.IdempotencyKey);
        Assert.Equal("6789", DeviceConnectivityProfilePolicy.LastFour(result.Value.Iccid));
    }

    [Theory]
    [InlineData("123", "iccid must contain 18-22 digits.")]
    [InlineData("8914800000123456789A", "iccid must contain 18-22 digits.")]
    public void InvalidIccidFailsClosed(string iccid, string expected)
    {
        var result = DeviceConnectivityProfilePolicy.Validate(Valid() with { Iccid = iccid }, Now);
        Assert.Equal(expected, result.Error);
        Assert.Null(result.Value);
    }

    [Fact]
    public void TimestampWithoutOffsetIsRejected()
    {
        var result = DeviceConnectivityProfilePolicy.Validate(Valid() with { EffectiveAt = "2026-09-07T11:55:00" }, Now);
        Assert.Equal("effectiveAt must be an ISO-8601 timestamp with an explicit UTC offset.", result.Error);
    }

    [Fact]
    public void FutureTimestampIsRejected()
    {
        var result = DeviceConnectivityProfilePolicy.Validate(Valid() with { EffectiveAt = "2026-09-07T12:05:01Z" }, Now);
        Assert.Equal("effectiveAt cannot be more than five minutes in the future.", result.Error);
    }

    [Theory]
    [InlineData("4165550123")]
    [InlineData("+01234567890")]
    [InlineData("+1 416 555 0123")]
    public void NonCanonicalMsisdnIsRejected(string msisdn)
    {
        var result = DeviceConnectivityProfilePolicy.Validate(Valid() with { Msisdn = msisdn }, Now);
        Assert.Contains("E.164", result.Error);
    }

    private static DeviceConnectivityProfileRequest Valid() => new(
        "eSIM", "Example Carrier", "8914800000123456789", null, null,
        "2026-09-07T11:55:00Z", "Initial assignment", "Carrier order 42", Guid.NewGuid().ToString("D"));
}
