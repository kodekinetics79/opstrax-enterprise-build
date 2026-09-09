using System.Text.Json;
using Opstrax.Api.Services;

namespace Opstrax.Tests;

public sealed class DeviceRemoteCommandPolicyTests
{
    [Fact]
    public void CatalogContainsOnlyBoundedNonImmobilizingCommands()
    {
        Assert.Equal(new[] { "RequestDiagnostics", "RequestPosition", "RestartDevice" },
            DeviceRemoteCommandPolicy.Catalog.Select(value => value.CommandType).Order().ToArray());
        Assert.DoesNotContain(DeviceRemoteCommandPolicy.Catalog,
            value => value.CommandType.Contains("Lock", StringComparison.OrdinalIgnoreCase) ||
                     value.CommandType.Contains("Immobil", StringComparison.OrdinalIgnoreCase));
        Assert.All(DeviceRemoteCommandPolicy.Catalog, value => Assert.Equal(1, value.MaxAttempts));
    }

    [Theory]
    [InlineData("RequestPosition", "{}")]
    [InlineData("RequestDiagnostics", "{\"channels\":[\"gnss\",\"power\"]}")]
    [InlineData("RestartDevice", "{\"delaySeconds\":30}")]
    public void ValidRequestProducesCanonicalPayloadAndHashedConfirmation(string commandType, string payload)
    {
        var confirmation = DeviceRemoteCommandPolicy.ConfirmationFor(commandType, "DEV-119");
        var result = DeviceRemoteCommandPolicy.Validate(Request(commandType, payload, confirmation), "DEV-119");
        Assert.Null(result.Error);
        Assert.Matches("^[0-9a-f]{64}$", result.Value!.SafetyConfirmationHash);
        Assert.DoesNotContain("DEV-119", result.Value.SafetyConfirmationHash, StringComparison.Ordinal);
    }

    [Fact]
    public void ExactSafetyPhraseIsMandatory()
    {
        var result = DeviceRemoteCommandPolicy.Validate(Request("RestartDevice", "{}", "RESTART ANOTHER-DEVICE"), "DEV-119");
        Assert.Contains("RESTART DEV-119", result.Error, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("RequestPosition", "{\"extra\":true}")]
    [InlineData("RequestDiagnostics", "{\"channels\":[\"gnss\",\"gnss\"]}")]
    [InlineData("RequestDiagnostics", "{\"channels\":[\"made_up\"]}")]
    [InlineData("RestartDevice", "{\"delaySeconds\":301}")]
    [InlineData("RestartDevice", "{\"force\":true}")]
    public void PayloadAllowlistRejectsUnknownOrUnsafeShapes(string commandType, string payload)
    {
        var confirmation = DeviceRemoteCommandPolicy.ConfirmationFor(commandType, "DEV-119");
        Assert.NotNull(DeviceRemoteCommandPolicy.Validate(Request(commandType, payload, confirmation), "DEV-119").Error);
    }

    [Fact]
    public void RepeatedJsonPropertyIsRejectedWithoutThrowing()
    {
        var confirmation = DeviceRemoteCommandPolicy.ConfirmationFor("RestartDevice", "DEV-119");
        var result = DeviceRemoteCommandPolicy.Validate(
            Request("RestartDevice", "{\"delaySeconds\":1,\"delaySeconds\":2}", confirmation), "DEV-119");
        Assert.Contains("repeated", result.Error, StringComparison.OrdinalIgnoreCase);
    }

    private static DeviceRemoteCommandRequest Request(string commandType, string payload, string confirmation) => new(
        commandType, JsonDocument.Parse(payload).RootElement.Clone(), "Investigate the reported device condition",
        "Approved ticket CMD-119", confirmation, Guid.NewGuid().ToString("D"));
}
