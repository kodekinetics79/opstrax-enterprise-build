using Opstrax.Api.Controllers;
namespace Opstrax.Tests;
public sealed class JourneyHosEligibilityTests
{
    [Fact] public void MissingSourceBlocks() => Assert.NotNull(EndpointMappings.AuthoritativeHosBlock(null));
    [Theory]
    [InlineData("Unavailable",480)] [InlineData("Violation",480)] [InlineData("Off Duty",480)]
    [InlineData("Unknown",480)] [InlineData("OK",59)]
    public void UnknownUnsafeOrExhaustedClocksBlock(string status,int minutes)
        => Assert.NotNull(EndpointMappings.AuthoritativeHosBlock(new() { ["status"]=status,["driveTimeRemainingMinutes"]=minutes }));
    [Fact] public void MissingRemainingTimeBlocks() => Assert.NotNull(EndpointMappings.AuthoritativeHosBlock(new() { ["status"]="OK" }));
    [Theory] [InlineData("Compliant")] [InlineData("OK")] [InlineData("Warning")] [InlineData("On Duty")]
    public void SupportedOperationalStatusesWithTimePass(string status)
        => Assert.Null(EndpointMappings.AuthoritativeHosBlock(new() { ["status"]=status,["driveTimeRemainingMinutes"]=480 }));
}
