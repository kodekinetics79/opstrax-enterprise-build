using Opstrax.Api.Services;

namespace Opstrax.Tests;

public sealed class DeviceSupportTierPolicyTests
{
    private static readonly DateTimeOffset Now = DateTimeOffset.Parse("2026-09-07T12:00:00Z");

    [Theory]
    [InlineData("Assign")]
    [InlineData("Change")]
    public void AcceptsBoundedRoutingPlans(string action)
    {
        var (value, error) = DeviceSupportTierPolicy.Validate(new(action, "Priority", "AlwaysOn", 60,
            "ESC-126", "CONTRACT-126", "Approved support routing plan", "OPS-126",
            Now.ToString("O"), Guid.NewGuid().ToString("D")), Now);
        Assert.Null(error);
        Assert.Equal(60, value!.RoutingResponseTargetMinutes);
    }

    [Fact]
    public void EndRequiresEveryTierPlanFieldToBeOmitted()
    {
        var (value, error) = DeviceSupportTierPolicy.Validate(new("End", null, null, null,
            null, null, "Coverage end recorded by operator", "OPS-126",
            Now.ToString("O"), Guid.NewGuid().ToString("D")), Now);
        Assert.Null(error);
        Assert.Equal("End", value!.ActionType);

        var (_, invalid) = DeviceSupportTierPolicy.Validate(new("End", "Standard", null, null,
            null, null, "Coverage end recorded by operator", "OPS-126",
            Now.ToString("O"), Guid.NewGuid().ToString("D")), Now);
        Assert.Contains("omitted", invalid);
    }

    [Theory]
    [InlineData("Unknown", "AlwaysOn", 60)]
    [InlineData("Priority", "Unknown", 60)]
    [InlineData("Priority", "AlwaysOn", 14)]
    [InlineData("Priority", "AlwaysOn", 10081)]
    public void RejectsUnknownOrUnboundedPlanValues(string tier, string coverage, int target)
    {
        var (_, error) = DeviceSupportTierPolicy.Validate(new("Assign", tier, coverage, target,
            "ESC-126", "CONTRACT-126", "Approved support routing plan", "OPS-126",
            Now.ToString("O"), Guid.NewGuid().ToString("D")), Now);
        Assert.NotNull(error);
    }

    [Fact]
    public void RejectsImplicitOffsetOrFutureTime()
    {
        foreach (var timestamp in new[] { "2026-09-07T12:00:00", Now.AddMinutes(6).ToString("O") })
        {
            var (_, error) = DeviceSupportTierPolicy.Validate(new("Assign", "Standard", "BusinessHours", 240,
                "ESC-126", "CONTRACT-126", "Approved support routing plan", "OPS-126",
                timestamp, Guid.NewGuid().ToString("D")), Now);
            Assert.Contains("effectiveAt", error);
        }
    }
}
