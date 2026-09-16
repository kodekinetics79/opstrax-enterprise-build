namespace Opstrax.Api.Services;

/// <summary>
/// Decides whether a malfunction recovery has an authoritative, recent transport
/// observation. Registry status, credentials and operator prose are prerequisites
/// or context; none of them is connectivity evidence by itself.
/// </summary>
public static class EldRecoveryEvidencePolicy
{
    public const int FreshnessMinutes = 15;

    public sealed record Evidence(
        string? DeviceProvider,
        bool HasValidDeviceCredentials,
        bool HasExactProviderIdentity,
        bool ProviderAccountVerified,
        string? SourceChannel,
        DateTimeOffset? ObservedAt,
        DateTimeOffset? ReceivedAt);

    public sealed record Decision(
        string VerificationLane,
        bool ProviderSupported,
        bool ConnectivityAuthoritative,
        bool CanActivate,
        bool PreviewOnly,
        string Reason);

    private static readonly string[] GatewayProviders =
    [
        "concox", "gt06", "teltonika", "ruptela", "queclink"
    ];

    public static Decision Evaluate(Evidence evidence, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(evidence);
        if (now.Offset != TimeSpan.Zero)
            throw new ArgumentException("The recovery clock must use UTC.", nameof(now));

        var provider = evidence.DeviceProvider?.Trim().ToLowerInvariant() ?? "";
        var lane = evidence.HasValidDeviceCredentials
            ? "native-device-hmac"
            : provider == "samsara" && evidence.HasExactProviderIdentity
                ? "samsara-provider-api"
                : GatewayProviders.Contains(provider, StringComparer.OrdinalIgnoreCase)
                    ? "trusted-protocol-gateway"
                    : "unsupported-provider-preview";

        if (lane == "unsupported-provider-preview")
            return new(lane, false, false, false, true,
                "No provider-specific recovery verifier is available for this device; the submitted evidence is previewed only.");

        var expectedChannel = lane switch
        {
            "native-device-hmac" => "native-hmac",
            "samsara-provider-api" => "samsara-api",
            _ => "trusted-gateway",
        };
        var timestampsSane = evidence.ObservedAt is { } observed
            && evidence.ReceivedAt is { } received
            && observed <= received.AddMinutes(5)
            && received <= now.AddMinutes(5)
            && observed >= now.AddMinutes(-FreshnessMinutes)
            && received >= now.AddMinutes(-FreshnessMinutes);
        var providerIdentitySane = lane != "samsara-provider-api" || evidence.ProviderAccountVerified;
        var authoritative = timestampsSane
            && providerIdentitySane
            && string.Equals(evidence.SourceChannel, expectedChannel, StringComparison.OrdinalIgnoreCase);

        if (!authoritative)
            return new(lane, true, false, false, false,
                $"A recent accepted {expectedChannel} connectivity observation is required.");

        // The Active ELD state is protected by a database constraint requiring
        // device credentials. Provider/gateway evidence can prove a live upstream
        // path, but cannot manufacture direct-device credentials.
        var canActivate = evidence.HasValidDeviceCredentials;
        return new(lane, true, true, canActivate, false, canActivate
            ? "Recent authenticated device telemetry verifies recovery."
            : "Recent provider connectivity is verified, but direct-device credentials are not provisioned; the device remains Diagnostic.");
    }
}
