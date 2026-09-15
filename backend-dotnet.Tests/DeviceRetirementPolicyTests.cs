using Opstrax.Api.Services;

namespace Opstrax.Tests;

public sealed class DeviceRetirementPolicyTests
{
    private static readonly DateTimeOffset Now = DateTimeOffset.Parse("2026-09-07T12:00:00Z");

    [Fact]
    public void AcceptsCanonicalImmediateRetirementInstruction()
    {
        var key = Guid.NewGuid();
        var (value, error) = DeviceRetirementPolicy.Validate(new(
            "Fleet replacement cycle", "ReturnToVendor", "WO-RET-123",
            Now.ToString("O"), 7, key.ToString("D"), "RETIRE DEV-123"), Now);

        Assert.Null(error);
        Assert.NotNull(value);
        Assert.Equal(7, value!.ExpectedRowVersion);
        Assert.Equal(key, value.IdempotencyKey);
        Assert.Equal("RETIRE DEV-123", value.SafetyConfirmation);
    }

    [Theory]
    [InlineData("Destroy")]
    [InlineData("")]
    [InlineData("returned")]
    public void RejectsUnsupportedDisposition(string disposition)
    {
        var (_, error) = DeviceRetirementPolicy.Validate(new(
            "Fleet replacement cycle", disposition, "WO-RET-123",
            Now.ToString("O"), 7, Guid.NewGuid().ToString("D"), "RETIRE DEV-123"), Now);
        Assert.Contains("dispositionPlan", error);
    }

    [Fact]
    public void RejectsBackdatedOrFutureCredentialBoundary()
    {
        foreach (var timestamp in new[] { Now.AddMinutes(-6), Now.AddMinutes(6) })
        {
            var (_, error) = DeviceRetirementPolicy.Validate(new(
                "Fleet replacement cycle", "Recycle", "WO-RET-123",
                timestamp.ToString("O"), 7, Guid.NewGuid().ToString("D"), "RETIRE DEV-123"), Now);
            Assert.Contains("within five minutes", error);
        }
    }
}
