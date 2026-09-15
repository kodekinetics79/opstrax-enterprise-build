using Opstrax.Api.Services;

namespace Opstrax.Tests;

public sealed class DeviceRmaSupportPolicyTests
{
    private static readonly DateTimeOffset Now = DateTimeOffset.Parse("2026-09-07T12:00:00Z");

    [Fact]
    public void AcceptsOwnershipAndEscalationWithExactSeverityRules()
    {
        var (ownership, ownershipError) = DeviceRmaSupportPolicy.Validate(new(
            "TakeOwnership", "Device Support", null, "Assigned for diagnosis", "SUP-124",
            Now.ToString("O"), Guid.NewGuid().ToString("D")), Now);
        Assert.Null(ownershipError);
        Assert.Equal("TakeOwnership", ownership!.ActionType);
        Assert.Null(ownership.EscalationSeverity);

        var (escalation, escalationError) = DeviceRmaSupportPolicy.Validate(new(
            "Escalate", "Device Engineering", "P1", "Repeated field failure", "INC-124",
            Now.ToString("O"), Guid.NewGuid().ToString("D")), Now);
        Assert.Null(escalationError);
        Assert.Equal("P1", escalation!.EscalationSeverity);
    }

    [Theory]
    [InlineData("TakeOwnership", "P2")]
    [InlineData("Escalate", null)]
    [InlineData("Escalate", "Critical")]
    public void RejectsSeverityThatDoesNotMatchTheAction(string actionType, string? severity)
    {
        var (_, error) = DeviceRmaSupportPolicy.Validate(new(
            actionType, "Device Support", severity, "Reason for routing", "SUP-124",
            Now.ToString("O"), Guid.NewGuid().ToString("D")), Now);
        Assert.Contains("escalationSeverity", error);
    }

    [Fact]
    public void RejectsImplicitOffsetOrFutureActionTime()
    {
        foreach (var timestamp in new[] { "2026-09-07T12:00:00", Now.AddMinutes(6).ToString("O") })
        {
            var (_, error) = DeviceRmaSupportPolicy.Validate(new(
                "TakeOwnership", "Device Support", null, "Assigned for diagnosis", "SUP-124",
                timestamp, Guid.NewGuid().ToString("D")), Now);
            Assert.Contains("effectiveAt", error);
        }
    }
}
