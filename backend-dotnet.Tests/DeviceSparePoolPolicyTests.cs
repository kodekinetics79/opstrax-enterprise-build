using Opstrax.Api.Services;

namespace Opstrax.Tests;

public sealed class DeviceSparePoolPolicyTests
{
    private static readonly DateTimeOffset Now = DateTimeOffset.Parse("2026-09-07T12:00:00Z");

    [Fact]
    public void AcceptsTheBoundedPoolStateActions()
    {
        var (add, addError) = DeviceSparePoolPolicy.Validate(new(
            "Add", "Toronto spares", null, "Held for governed replacement planning", "INV-125",
            Now.ToString("O"), Guid.NewGuid().ToString("D")), Now);
        Assert.Null(addError);
        Assert.Equal("Toronto spares", add!.PoolName);

        var (reserve, reserveError) = DeviceSparePoolPolicy.Validate(new(
            "Reserve", null, "123", "Reserved for the exact open RMA case", "RMA-125",
            Now.ToString("O"), Guid.NewGuid().ToString("D")), Now);
        Assert.Null(reserveError);
        Assert.Equal(123, reserve!.RmaCaseId);

        foreach (var action in new[] { "Release", "Remove" })
        {
            var (value, error) = DeviceSparePoolPolicy.Validate(new(
                action, null, null, "Explicit pool state transition", "INV-125",
                Now.ToString("O"), Guid.NewGuid().ToString("D")), Now);
            Assert.Null(error);
            Assert.Equal(action, value!.ActionType);
        }
    }

    [Theory]
    [InlineData("Add", null, null)]
    [InlineData("Reserve", null, null)]
    [InlineData("Release", null, "22")]
    [InlineData("Remove", "Unexpected pool", null)]
    public void RejectsFieldsThatDoNotMatchTheAction(string action, string? pool, string? caseId)
    {
        var (_, error) = DeviceSparePoolPolicy.Validate(new(
            action, pool, caseId, "Explicit pool state transition", "INV-125",
            Now.ToString("O"), Guid.NewGuid().ToString("D")), Now);
        Assert.NotNull(error);
    }

    [Fact]
    public void RejectsNonCanonicalCaseAndImplicitOrFutureTime()
    {
        var (_, caseError) = DeviceSparePoolPolicy.Validate(new(
            "Reserve", null, "0004", "Reserve exact spare for RMA", "RMA-125",
            Now.ToString("O"), Guid.NewGuid().ToString("D")), Now);
        Assert.Contains("canonical", caseError);

        foreach (var timestamp in new[] { "2026-09-07T12:00:00", Now.AddMinutes(6).ToString("O") })
        {
            var (_, error) = DeviceSparePoolPolicy.Validate(new(
                "Remove", null, null, "Remove device from spare plan", "INV-125",
                timestamp, Guid.NewGuid().ToString("D")), Now);
            Assert.Contains("effectiveAt", error);
        }
    }
}
